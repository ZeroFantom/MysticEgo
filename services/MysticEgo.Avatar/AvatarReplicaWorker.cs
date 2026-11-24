using System.Net.Http.Json;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MysticEgo.Avatar;

public static class ReplicaFiles
{
    public static string Base(IConfiguration config)=>Path.GetFullPath(config["Avatar:ReplicaPath"]??Path.Combine(AppContext.BaseDirectory,"avatar-replica"));
    public static IResult ReadManifest(IConfiguration config)
    {
        var path=Path.Combine(Base(config),"manifest.json");
        if(!File.Exists(path))return Results.NotFound(new {error="No theme synchronized"});
        return Results.File(path,"application/json");
    }
    public static IResult GetAsset(IConfiguration config,string id)
    {
        if(id.Length!=32||!id.All(Uri.IsHexDigit))return Results.BadRequest();
        var path=Path.Combine(Base(config),"assets",id);
        return File.Exists(path)?Results.File(path,"application/octet-stream"):Results.NotFound();
    }
}
public sealed class AvatarReplicaWorker(IHttpClientFactory factory,IConfiguration config,ILogger<AvatarReplicaWorker> logger):BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var upstream=config["Avatar:UpstreamUrl"];
        var key=config["Avatar:SyncKey"];
        if(string.IsNullOrEmpty(upstream)||string.IsNullOrEmpty(key)||key.Length<32)return;
        while(!stoppingToken.IsCancellationRequested)
        {
            try {await SynchronizeAsync(upstream,key,stoppingToken);}
            catch(Exception e) {logger.LogWarning(e,"Avatar theme synchronization failed; keeping previous verified revision");}
            await Task.Delay(TimeSpan.FromSeconds(60),stoppingToken);
        }
    }
    async Task SynchronizeAsync(string upstream,string key,CancellationToken ct)
    {
        if(!Uri.TryCreate(upstream,UriKind.Absolute,out var uri) || (uri.Scheme!="https" && !(uri.Scheme=="http" && (uri.IsLoopback && config.GetValue<bool>("Avatar:AllowInsecureLocalSync") || config.GetValue<bool>("Avatar:AllowInsecureLanSync")))))throw new InvalidOperationException("Avatar upstream requires HTTPS");
        var client=factory.CreateClient("avatar-replica");
        async Task<HttpResponseMessage> Get(string path)
        {
            using var request=new HttpRequestMessage(HttpMethod.Get,new Uri(upstream.TrimEnd('/')+path));
            // Do not log the shared sync secret.
            request.Headers.TryAddWithoutValidation("X-Mystic-Sync-Key",key);
            return await client.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,ct);
        }
        using var res=await Get("/api/v1/avatars/replica/manifest");
        if(!res.IsSuccessStatusCode)return;
        var manifest=await res.Content.ReadFromJsonAsync<ThemeReplicaManifest>(cancellationToken:ct);
        if(manifest is null||manifest.Theme is null || manifest.Assets.Length>101)return;
        var root=ReplicaFiles.Base(config);Directory.CreateDirectory(root);
        var old=Path.Combine(root,"manifest.json");
        if(File.Exists(old))
        {
            try {var previous=JsonSerializer.Deserialize<ThemeReplicaManifest>(await File.ReadAllTextAsync(old,ct),new JsonSerializerOptions(JsonSerializerDefaults.Web));
                if(previous?.Theme.Revision>manifest.Theme.Revision)return;}
            catch(JsonException){}
        }
        var dir=Path.Combine(root,"assets");Directory.CreateDirectory(dir);
        var required=new[]{manifest.Theme.ModelId}.Concat(manifest.Theme.AnimationIds).ToHashSet();
        if(manifest.Assets.Select(x=>x.Id).ToHashSet().SetEquals(required)==false)return;
        foreach(var asset in manifest.Assets)
        {
            if(asset.Id.Length!=32||!asset.Id.All(Uri.IsHexDigit)||asset.Size<=0||asset.Size>32*1024*1024)return;
            var dest=Path.Combine(dir,asset.Id);
            if(File.Exists(dest) && await Matches(dest,asset.Sha256,ct))continue;
            using var bytes=await Get("/api/v1/avatars/replica/assets/"+asset.Id);
            bytes.EnsureSuccessStatusCode();
            using var mem=new MemoryStream();await bytes.Content.CopyToAsync(mem,ct);
            if(mem.Length!=asset.Size || !Convert.ToHexString(SHA256.HashData(mem.ToArray())).Equals(asset.Sha256,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("SHA-256 mismatch");
            var tmp=dest+".tmp";await File.WriteAllBytesAsync(tmp,mem.ToArray(),ct);File.Move(tmp,dest,true);
        }
        // Switch atomically only after all assets have been downloaded and verified.
        var staged=old+".tmp";await File.WriteAllTextAsync(staged,JsonSerializer.Serialize(manifest,new JsonSerializerOptions(JsonSerializerDefaults.Web)),ct);File.Move(staged,old,true);
        logger.LogInformation("Avatar theme revision {revision} synchronized",manifest.Theme.Revision);
    }
    static async Task<bool> Matches(string file,string hash,CancellationToken ct)
    {
        await using var fs=File.OpenRead(file);
        var digest=await SHA256.HashDataAsync(fs,ct);
        return Convert.ToHexString(digest).Equals(hash,StringComparison.OrdinalIgnoreCase);
    }
}
