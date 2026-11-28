using System.Text.Json;
using System.Text.Json.Serialization;

namespace MysticEgo.Soul;

/// <summary>Local read-only interchange contract. OS permissions must restrict writes to
/// a trusted simulation writer. Never accept a browser/upload path or reuse an old snapshot.</summary>
public sealed record WorldPresenceFeed(DateTimeOffset PublishedUtc,SpiritualWorldSnapshot Snapshot);
public sealed record WorldPresenceRead(bool SnapshotAvailable,SpiritualPresenceView? Presence,int FreshForSeconds);

public sealed class WorldPresenceFeedReader(TimeProvider clock)
{
    private static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web)
    {
        MaxDepth=16,UnmappedMemberHandling=JsonUnmappedMemberHandling.Disallow,
        AllowDuplicateProperties=false
    };
    private const int MaxBytes=1024*1024;
    private static WorldPresenceRead Unavailable()=>new(false,null,0);
    public async Task<WorldPresenceRead> Read(string? path,Guid account,CancellationToken ct=default)
    {
        if(account==Guid.Empty||string.IsNullOrWhiteSpace(path)||!Path.IsPathFullyQualified(path))return Unavailable();
        try
        {
            // Writer must atomically replace a regular file in a private directory.
            await using var file=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read|FileShare.Delete,
                4096,FileOptions.Asynchronous|FileOptions.SequentialScan);
            if(file.Length is <=0 or >MaxBytes)return Unavailable();
            var bytes=new byte[(int)file.Length];
            await file.ReadExactlyAsync(bytes,ct);
            if(file.ReadByte()!=-1)return Unavailable();
            var feed=JsonSerializer.Deserialize<WorldPresenceFeed>(bytes,Json);
            if(feed?.Snapshot is not { } snapshot||!Fresh(feed.PublishedUtc)||
                snapshot.Placements is null||snapshot.ActiveEmbodiments is null||snapshot.Permissions is null||
                snapshot.Friendships is null||snapshot.ContactLabels is null||snapshot.AreaLabels is null||
                snapshot.Placements.Count>2048||snapshot.ActiveEmbodiments.Count>1024||
                snapshot.Permissions.Count>8192||snapshot.Friendships.Count>8192||
                snapshot.ContactLabels.Count>2048||snapshot.AreaLabels.Count>2048)return Unavailable();
            var gateway=new SpiritualCommunicationGateway(clock);
            if(!gateway.TryPublish(gateway.Revision,snapshot,out _))return Unavailable();
            var result=gateway.ReadPresence(account);
            if(!Fresh(feed.PublishedUtc))return Unavailable();
            var remaining=(int)Math.Floor((feed.PublishedUtc.AddSeconds(15)-clock.GetUtcNow()).TotalSeconds);
            return remaining>0?new(true,result,Math.Min(remaining,15)):Unavailable();
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException or
            InvalidOperationException or NullReferenceException or NotSupportedException){return Unavailable();}
    }
    private bool Fresh(DateTimeOffset published)
    {
        var now=clock.GetUtcNow();
        return published<=now.AddSeconds(2)&&published>now.AddSeconds(-15);
    }
}
