using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using MysticEgo.Deployment;

namespace MysticEgo.WorldAI;

internal sealed class LocalWorldAiDiscovery(WorldAiCluster cluster,IHttpClientFactory clients,IConfiguration config) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if(!config.GetValue<bool>("WorldAI:DiscoverLocalModel"))return;
        var role = config["WorldAI:ModelRole"] ?? "orchestra";
        using var client=clients.CreateClient("world-ai");
        client.Timeout=TimeSpan.FromSeconds(4);
        while(!stoppingToken.IsCancellationRequested)
        {
            string? model=null;
            try { model = (await LocalModels.Ready(client, role, config["LmStudio:VerifiedModelFile"], stoppingToken))?.Id; }
            catch (Exception error) when (error is HttpRequestException or IOException or TaskCanceledException or JsonException or InvalidOperationException or KeyNotFoundException) { }
            cluster.SetLocalModel(model);
            await Task.Delay(TimeSpan.FromSeconds(15),stoppingToken);
        }
    }
}
