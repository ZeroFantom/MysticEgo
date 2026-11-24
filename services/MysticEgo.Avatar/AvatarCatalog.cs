using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;

namespace MysticEgo.Avatar;

public sealed record AvatarAsset(string Id, string Scope, string Owner, string Kind, string Extension, string Sha256, long Size, DateTimeOffset Added, string? Name = null);
public sealed record PersonalAvatar(string UserId, string AssetId, DateTimeOffset Updated, Dictionary<string,string>? Motions = null,
    string MotionStyle = "calm", AvatarAppearance? Appearance = null);
public sealed record AvatarAppearance(double Zoom = 1, double Yaw = 0, double Vertical = 0, double Light = 1, double Speed = 1,
    string IdleMotion = "idle", bool ShowOnSite = true, bool AnimateReplies = true);
public sealed record ThemeManifest(string OrganizationId, long Revision, string ModelId, string[] AnimationIds, string[] ProceduralAnimations, DateTimeOffset Published);
public sealed record ThemePayload(string ModelId, string[] AnimationIds, string[] ProceduralAnimations);
public sealed record AnimationJob(string Id, string Provider, string Status, string? Notes, DateTimeOffset Created);
public sealed class AvatarCatalog
{
    readonly string _root;
    readonly SemaphoreSlim _gate = new(1, 1);
    readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    readonly ConcurrentDictionary<string, AvatarAsset> _assets = new(StringComparer.Ordinal);
    readonly ConcurrentDictionary<string, PersonalAvatar> _personal = new(StringComparer.Ordinal);
    ThemeManifest? _theme;
    public string Root => _root;
    public AvatarCatalog(IConfiguration config)
    {
        _root=Path.GetFullPath(config["Avatar:StoragePath"] ?? Path.Combine(AppContext.BaseDirectory,"avatar-data"));
        Directory.CreateDirectory(_root);
        foreach(var asset in Read<AvatarAsset[]>("assets.json") ?? []) _assets[asset.Id]=asset;
        foreach(var avatar in Read<PersonalAvatar[]>("personal.json") ?? []) _personal[avatar.UserId]=avatar;
        _theme=Read<ThemeManifest>("theme.json");
    }
    T? Read<T>(string name)
    {
        var path=Path.Combine(_root,name);
        if(!File.Exists(path))return default;
        try {return JsonSerializer.Deserialize<T>(File.ReadAllText(path),_json);} catch(JsonException){return default;}
    }
    async Task SaveAsync<T>(string name, T value, CancellationToken ct)
    {
        var target=Path.Combine(_root,name);var temp=target+"."+Guid.NewGuid().ToString("N")+".tmp";
        await File.WriteAllTextAsync(temp,JsonSerializer.Serialize(value,_json),ct);
        File.Move(temp,target,true);
    }
    public static bool ValidOwner(string owner)=>owner.Length is >= 1 and <= 128 && owner.All(c=>char.IsLetterOrDigit(c)||c is '-' or '_' or '.');
    public static bool ValidExtension(string extension, string kind)=>kind switch
    {
        "model" => extension is ".vrm" or ".obj" or ".glb",
        "animation" => extension is ".vrma" or ".glb" or ".fbx",
        _=>false
    };
    public static bool ValidContent(ReadOnlySpan<byte> bytes, string extension)
    {
        if(bytes.Length<16)return false;
        if(extension is ".vrm" or ".glb" or ".vrma")
        {
            if(!bytes[..4].SequenceEqual("glTF"u8) || BitConverter.ToUInt32(bytes.Slice(4,4))!=2 || BitConverter.ToUInt32(bytes.Slice(8,4))!=bytes.Length)return false;
            var jsonSize=BitConverter.ToUInt32(bytes.Slice(12,4));
            if(jsonSize < 2 || jsonSize > bytes.Length-20 || !bytes.Slice(16,4).SequenceEqual("JSON"u8)) return false;
            try {
                using var json=JsonDocument.Parse(bytes.Slice(20,(int)jsonSize).ToArray());
                var root=json.RootElement;
                if((root.TryGetProperty("images",out var images)&&images.ValueKind==JsonValueKind.Array&&images.EnumerateArray().Any(x=>x.TryGetProperty("uri",out var u)&&u.ValueKind==JsonValueKind.String&&!u.GetString()!.StartsWith("data:",StringComparison.OrdinalIgnoreCase))) || (root.TryGetProperty("buffers",out var buffers)&&buffers.ValueKind==JsonValueKind.Array&&buffers.EnumerateArray().Any(x=>x.TryGetProperty("uri",out var u)&&u.ValueKind==JsonValueKind.String&&!u.GetString()!.StartsWith("data:",StringComparison.OrdinalIgnoreCase))))return false;
                if(!root.TryGetProperty("asset",out var a) || !a.TryGetProperty("version",out var v) || v.GetString()!="2.0")return false;
                var extensions=root.TryGetProperty("extensionsUsed",out var ex) && ex.ValueKind==JsonValueKind.Array?ex.EnumerateArray().Select(x=>x.GetString()).ToHashSet():new HashSet<string?>();
                return extension switch {
                    ".vrm"=>extensions.Contains("VRM")||extensions.Contains("VRMC_vrm"),
                    ".vrma"=>extensions.Contains("VRMC_vrm_animation"),
                    _=>true };
            } catch(Exception error) when (error is JsonException or InvalidOperationException){return false;}
        }
        if(extension==".obj")
        {
            try {var s=new UTF8Encoding(false,true).GetString(bytes);
                return s.Split('\n').Any(x=>x.StartsWith("v ")) && s.Split('\n').Any(x=>x.StartsWith("f "))
                    && !s.Contains("\0") && !s.Split('\n').Any(x=>x.Length>16384);}
            catch(DecoderFallbackException){return false;}
        }
        if(extension==".fbx")return bytes.StartsWith("Kaydara FBX Binary  \0\x1a\0"u8) ||
            (bytes.StartsWith("; FBX "u8) && bytes.IndexOf("FBXHeaderExtension"u8) > 0);
        return false;
    }
    public async Task<AvatarAsset> AddAsync(string scope,string owner,string kind,string filename,Stream input,CancellationToken ct)
    {
        if(scope is not ("personal" or "organization") || !ValidOwner(owner))throw new ArgumentException("Invalid owner");
        var extension=Path.GetExtension(filename).ToLowerInvariant();
        if(!ValidExtension(extension,kind))throw new ArgumentException("Формат файла не подходит для выбранного типа ресурса.");
        const int limit=24*1024*1024;
        using var ms=new MemoryStream();
        var buffer=new byte[65536];int n;
        while((n=await input.ReadAsync(buffer,ct))>0)
        {
            if(ms.Length+n>limit)throw new ArgumentException("Максимальный размер файла — 24 МиБ.");
            ms.Write(buffer,0,n);
        }
        var bytes=ms.ToArray();
        if(!ValidContent(bytes,extension))throw new ArgumentException("Файл повреждён или содержит внешние ресурсы. Экспортируйте модель или анимацию одним файлом.");
        var sha=Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var id=Guid.NewGuid().ToString("N");
        await _gate.WaitAsync(ct);
        try {
            var existing = _assets.Values.FirstOrDefault(item => item.Scope == scope && item.Owner == owner && item.Kind == kind && item.Sha256 == sha && item.Extension == extension);
            if (existing is not null) return existing;
            var dir=Path.Combine(_root,"assets");Directory.CreateDirectory(dir);
            var path=Path.Combine(dir,sha+extension);
            if (!File.Exists(path))
            {
                var temporary = path + ".tmp";
                try { await File.WriteAllBytesAsync(temporary, bytes, ct); File.Move(temporary, path); }
                finally { File.Delete(temporary); }
            }
            var name = new string(Path.GetFileName(filename.Replace('\\', '/')).Where(character => !char.IsControl(character)).Take(100).ToArray());
            var asset=new AvatarAsset(id,scope,owner,kind,extension,sha,bytes.Length,DateTimeOffset.UtcNow,name);
            await SaveAsync("assets.json",_assets.Values.Append(asset).ToArray(),ct);
            _assets[id]=asset;
            return asset;
        } finally {_gate.Release();}
    }
    public AvatarAsset? GetAsset(string id)=>_assets.GetValueOrDefault(id);
    public string? AssetPath(string id)
    {
        if(!_assets.TryGetValue(id,out var asset))return null;
        var p=Path.Combine(_root,"assets",asset.Sha256+asset.Extension);
        return File.Exists(p)?p:null;
    }
    public AvatarAsset[] List(string scope,string owner)=>_assets.Values.Where(x=>x.Scope==scope&&x.Owner==owner).OrderByDescending(x=>x.Added).ToArray();
    public PersonalAvatar? GetPersonal(string owner)=>_personal.GetValueOrDefault(owner);
    public Task<PersonalAvatar> SetPersonalAsync(string owner, string id, CancellationToken ct) =>
        SetPersonalAsync(owner, id, GetPersonal(owner)?.Motions, ct, GetPersonal(owner)?.MotionStyle ?? "calm");
    public async Task<PersonalAvatar> SetPersonalAsync(string owner,string id,Dictionary<string,string>? motions,CancellationToken ct,
        string style = "calm", AvatarAppearance? appearance = null)
    {
        var asset=GetAsset(id);
        if (!(id == "bundled" || asset is { Scope: "personal", Kind: "model" } && asset.Owner == owner))
            throw new ArgumentException("Выберите собственную модель VRM.");
        if (style is not ("calm" or "lively" or "focused")) throw new ArgumentException("Выберите стиль движения.");
        if (appearance is not null && !(appearance.Zoom is >= .75 and <= 1.35 && appearance.Yaw is >= -180 and <= 180 &&
            appearance.Vertical is >= -.25 and <= .25 && appearance.Light is >= .5 and <= 1.5 && appearance.Speed is >= .5 and <= 1.75 &&
            PersonalMotionCatalog.Slots.Contains(appearance.IdleMotion) && appearance.IdleMotion is not ("talk" or "wave" or "bump")))
            throw new ArgumentException("Проверьте масштаб, поворот, свет и параметры движения аватара.");
        var map = motions ?? [];
        if (map.Count > 11 || map.Any(x => !PersonalMotionCatalog.Slots.Contains(x.Key) ||
            GetAsset(x.Value) is not { Scope: "personal", Kind: "animation" } clip || clip.Owner != owner))
            throw new ArgumentException("Пакет должен содержать ваши анимации и допустимые движения.");
        await _gate.WaitAsync(ct);
        try {
            var selected = new PersonalAvatar(owner, id, DateTimeOffset.UtcNow, new(map), style, appearance ?? GetPersonal(owner)?.Appearance ?? new());
            await SaveAsync("personal.json", _personal.Values.Where(item => item.UserId != owner).Append(selected).ToArray(), ct);
            _personal[owner] = selected;
            return selected;
        } finally {_gate.Release();}
    }
    public ThemeManifest? Theme => _theme;
    public async Task<ThemeManifest> PublishAsync(string organization,ThemePayload payload,CancellationToken ct)
    {
        bool owned(string id,string kind)=>GetAsset(id) is { } a && a.Scope=="organization" && a.Owner==organization && a.Kind==kind;
        if(!owned(payload.ModelId,"model") || payload.AnimationIds is null || payload.AnimationIds.Length>100 || payload.AnimationIds.Any(x=>!owned(x,"animation"))) throw new ArgumentException("Theme assets must belong to organization");
        var animations=(payload.ProceduralAnimations??[]).Where(x=>x is "idle" or "blink" or "wave" or "nod" or "dance" or "talk").Distinct().ToArray();
        await _gate.WaitAsync(ct);
        try {var x=new ThemeManifest(organization,(_theme?.Revision??0)+1,payload.ModelId,payload.AnimationIds,animations,DateTimeOffset.UtcNow);
            _theme=x;await SaveAsync("theme.json",x,ct);return x;
        } finally {_gate.Release();}
    }
}
