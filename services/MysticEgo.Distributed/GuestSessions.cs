using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace MysticEgo.Distributed;

/// <summary>Краткоживущая локальная сессия гостя клуба. Не выдаёт JWT и прав управлять ПК.</summary>
public sealed class GuestSessions
{
    private readonly ConcurrentDictionary<string,Guest> _sessions = new();
    public sealed record Guest(string Id,string Nickname,DateTimeOffset Expires);
    public (string Token,Guest Guest) Create(string name)
    {
        Purge();
        if(_sessions.Count >= 100)throw new InvalidOperationException("Достигнут предел гостевых сессий.");
        var token=Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var id=Guid.NewGuid().ToString("N");
        var guest=new Guest(id,name,DateTimeOffset.UtcNow.AddHours(2));
        _sessions[Hash(token)]=guest;
        return (token,guest);
    }
    public Guest? Find(string? token)
    {
        if(token is null || token.Length!=64 || !Regex.IsMatch(token,"^[0-9a-fA-F]{64}$"))return null;
        return _sessions.TryGetValue(Hash(token),out var guest) && guest.Expires>DateTimeOffset.UtcNow?guest:null;
    }
    public bool Active => _sessions.Values.Any(x => x.Expires > DateTimeOffset.UtcNow);
    public void Revoke(string? token){if(token is not null && Regex.IsMatch(token,"^[0-9a-fA-F]{64}$"))_sessions.TryRemove(Hash(token),out _);}
    private static string Hash(string token)=>Convert.ToHexString(SHA256.HashData(Convert.FromHexString(token)));
    private void Purge(){foreach(var pair in _sessions)if(pair.Value.Expires<=DateTimeOffset.UtcNow)_sessions.TryRemove(pair.Key,out _);}
}
public sealed record CreateGuest(string Nickname);
