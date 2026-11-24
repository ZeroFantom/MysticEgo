using System.Net.Http.Headers;
using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MysticEgo.Distributed;

/// <summary>
/// Доверенный прокси Orchestra к конкретному Global API из конфигурации.
/// Не принимает URL от браузера, поэтому не является открытым SSRF-прокси.
/// Окончательная проверка прав выполняется на целевом Global API.
/// </summary>
public sealed record GlobalProxyResponse(int StatusCode,string Json);
public interface IOrchestraControlProxy
{
    Task<GlobalProxyResponse> GlobalOverview(string bearer,CancellationToken ct);
}
public sealed class OrchestraControlProxy(IConfiguration config,IHttpClientFactory factory,NodeOptions options) : IOrchestraControlProxy
{
    public async Task<GlobalProxyResponse> GlobalOverview(string bearer,CancellationToken ct)
    {
        var baseUrl=config["Orchestra:GlobalUrl"];
        if(!Uri.TryCreate(baseUrl,UriKind.Absolute,out var uri)||!(uri.Scheme==Uri.UriSchemeHttps ||
            (options.DeploymentMode=="single-machine"&&uri.Scheme==Uri.UriSchemeHttp&&uri.IsLoopback)))
            throw new NodeApiException(503,"Не настроен доверенный адрес Global API.");
        using var request=new HttpRequestMessage(HttpMethod.Get,baseUrl!.TrimEnd('/')+"/api/v1/overview");
        request.Headers.Authorization=AuthenticationHeaderValue.Parse(bearer);
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct);timeout.CancelAfter(TimeSpan.FromSeconds(8));
        try
        {
            using var client=factory.CreateClient();
            using var response=await client.SendAsync(request,timeout.Token);
            return new((int)response.StatusCode,await response.Content.ReadAsStringAsync(timeout.Token));
        }
        catch(HttpRequestException){throw new NodeApiException(502,"Не удалось связаться с Global API.");}
        catch(OperationCanceledException) when(!ct.IsCancellationRequested){throw new NodeApiException(504,"Global API не ответил вовремя.");}
    }
}
public static class OrchestraProxyRegistration
{
    public static Microsoft.Extensions.DependencyInjection.IServiceCollection AddOrchestraProxy(this Microsoft.Extensions.DependencyInjection.IServiceCollection services)
    {
        services.AddScoped<IOrchestraControlProxy,OrchestraControlProxy>();return services;
    }
}
