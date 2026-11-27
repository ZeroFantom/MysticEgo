using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MysticEgo.Capacity;

namespace MysticEgo.Api.Capacity;

public sealed record IndependentView(CapacityReport Report,CapacitySample[] Samples,string? Advisory,DateTimeOffset? AdvisoryAt);

/// <summary>Independent collector uses the same deterministic estimator as Orchestra.
/// Its optional local model only explains numeric results and never obtains cloud credentials.</summary>
public sealed class IndependentCapacityMonitor(IConfiguration config,IHttpClientFactory clients,ILogger<IndependentCapacityMonitor> log):BackgroundService
{
    private readonly CapacityRegistry registry=new();
    private IndependentView? latest;
    private DateTimeOffset lastAdvisory;
    public IndependentView? Read()=>Volatile.Read(ref latest);
    public bool Accept(Guid id,CapacitySample sample)
    {
        var role=config["Capacity:Nodes:"+id.ToString("D")];
        if(role is not ("orchestra" or "global" or "local" or "agent" or "audit"))return false;
        return registry.Accept(sample with{NodeId=id.ToString("D"),Role=role,MonthlyCost=null,LinkMbps=null},DateTimeOffset.UtcNow);
    }
    private decimal? Cost(string key)=>decimal.TryParse(config[key],System.Globalization.NumberStyles.Number,System.Globalization.CultureInfo.InvariantCulture,out var price)&&price>=0?price:null;
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        using var timer=new PeriodicTimer(TimeSpan.FromSeconds(5));
        while(await timer.WaitForNextTickAsync(ct))
        {
            try
            {
                var now=DateTimeOffset.UtcNow;
                var measured=registry.Read().ToDictionary(r=>r.NodeId);
                var rows=config.GetSection("Capacity:Nodes").GetChildren().Take(2000).Select(x=>
                    (measured.GetValueOrDefault(x.Key)??new CapacitySample(x.Key,x.Value??"unknown",DateTimeOffset.MinValue,null,null,null,null,null,[],null,null))
                    with{MonthlyCost=Cost("Capacity:Costs:"+x.Key)}).ToArray();
                var budget=new CapacityBudget(config["Capacity:Currency"]??"RUB",Cost("Capacity:AnalyzerMonthlyCost"),Cost("Capacity:ReserveMonthlyCost"),
                    Cost("Capacity:MonthlyLimit"),config.GetValue("Capacity:MaxAdditionalPools",3));
                var report=CapacityAnalyzer.Analyze(rows,budget,now);
                var previous=Read();
                Volatile.Write(ref latest,new IndependentView(report,rows,previous?.Advisory,previous?.AdvisoryAt));
                if(config["Capacity:LocalEvaluationModel"] is {Length:>0} model && now-lastAdvisory>TimeSpan.FromMinutes(2))
                {
                    lastAdvisory=now;
                    // Aggregate values only. No node names, arbitrary user content, tokens, or prompts from other services.
                    var safe=JsonSerializer.Serialize(new{report.FreshNodes,report.StaleNodes,report.AdditionalPools,report.KnownMonthlyCost,report.ProjectedMonthlyCost,report.WithinBudget,report.Currency,
                        attention=report.Nodes.Count(n=>n.State=="attention")});
                    using var client=clients.CreateClient("local-evaluation");
                    using var request=new HttpRequestMessage(HttpMethod.Post,"http://127.0.0.1:1234/v1/chat/completions");
                    if(config["Capacity:LocalEvaluationToken"] is {Length:>0} token)request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",token);
                    request.Content=JsonContent.Create(new{model,temperature=.1,max_tokens=300,messages=new[]{
                        new{role="system",content="Explain measured capacity and uncertainty. Do not claim to deploy resources or authorize spending. Return concise advisory text."},
                        new{role="user",content=safe}}});
                    using var response=await client.SendAsync(request,ct);
                    response.EnsureSuccessStatusCode();
                    using var json=JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
                    var answer=json.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();
                    Volatile.Write(ref latest,new IndependentView(report,rows,answer is null?null:answer[..Math.Min(2000,answer.Length)],now));
                }
            }
            catch(OperationCanceledException) when(ct.IsCancellationRequested){break;}
            catch(Exception e){log.LogWarning("Independent capacity sample or explanation unavailable: {Type}",e.GetType().Name);}
        }
    }
}
