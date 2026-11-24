using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MysticEgo.Distributed;

// Does not execute any repair operations; safe to run on CI Linux/Windows.
var services=new ServiceCollection().AddLogging().AddHttpClient("network-repair").Services;
using var provider=services.BuildServiceProvider();
var options=new NetworkRepairOptions{Enabled=false,Root=Path.Combine(Path.GetTempPath(),"mysticego-beta4-smoke")};
var service=new NetworkRepairService(options,provider.GetRequiredService<ILogger<NetworkRepairService>>(),provider.GetRequiredService<IHttpClientFactory>());
var status=service.Status();
if(status is null)throw new Exception("Status must be available even when feature is disabled");
foreach(var action in new[]{"game-roblox-hosts","game-pubg-trailers","game-warzone-dns","telegram-proxy","package-install-steam","signalrgb-watchdog-on"})
{
    var result=await service.ExecuteAsync(new RepairOperation(action,Confirmed:true),CancellationToken.None);
    if(result.Success)throw new Exception($"Disabled module unexpectedly executed {action}");
}
Console.WriteLine("NetworkRepair disabled-isolation smoke tests passed");
