using System.Net.Http.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
namespace MysticEgo.Distributed;
public sealed class NodeAnnouncer : BackgroundService
{
    private readonly IHttpClientFactory _factory;
    private readonly NodeOptions _opts;
    private readonly ILogger<NodeAnnouncer> _log;
    private readonly IdentityTokenSource _tokens;
    public NodeAnnouncer(IHttpClientFactory f,NodeOptions opts,ILogger<NodeAnnouncer> log,IdentityTokenSource tokens){_factory=f;_opts=opts;_log=log;_tokens=tokens;}
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while(!ct.IsCancellationRequested)
        {
            var upstream=_opts.Kind switch {
                NodeKind.Global=>_opts.OrchestraUrl,
                NodeKind.AuditOrchestra=>_opts.OrchestraUrl,
                NodeKind.Local=>_opts.GlobalUrl,
                NodeKind.Agent=>_opts.LocalUrl,
                _=>null
            };
            if(!string.IsNullOrWhiteSpace(upstream))
            {
                try
                {
                    using var client=_factory.CreateClient();client.Timeout=TimeSpan.FromSeconds(8);
                    client.DefaultRequestHeaders.Authorization=new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer",await _tokens.GetAsync(ct));
                    var announce=new NodeAnnounce(_opts.IdentityId,_opts.DisplayName,_opts.Kind.ToString(),_opts.PublicUrl,_opts.ParentId,DateTimeOffset.UtcNow);
                    using var response=await client.PostAsJsonAsync(upstream.TrimEnd('/')+"/api/v1/nodes/announce",announce,ct);
                    response.EnsureSuccessStatusCode();
                }
                catch(Exception e){_log.LogWarning(e,"Parent announce failed; node keeps serving locally");}
            }
            try {await Task.Delay(TimeSpan.FromSeconds(30),ct);}catch(OperationCanceledException){break;}
        }
    }
}
