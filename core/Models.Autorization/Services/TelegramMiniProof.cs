using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Models.Autorization.Services;

/// <summary>Telegram Web App HMAC verification, not OAuth and not chat membership proof.</summary>
public static class TelegramMiniProof
{
    public sealed class Verified
    {
        internal Verified(string bot,string subject,string proofKey,DateTimeOffset issued)
        {BotId=bot;Subject=subject;ProofKey=proofKey;Issued=issued;}
        public string BotId {get;}
        public string Subject {get;}
        internal string ProofKey {get;}
        public DateTimeOffset Issued {get;}
    }
    public static bool ValidToken(MessengerRealm realm,string? token)
    {
        if(!realm.Valid||realm.Provider!="telegram"||token is not {Length:>=16 and <=512})return false;
        var prefix=realm.BotId+":";
        return token.StartsWith(prefix,StringComparison.Ordinal)&&token.Length-prefix.Length>=16&&
            token[prefix.Length..].All(c=>char.IsAsciiLetterOrDigit(c)||c is '_' or '-');
    }
    private static string? Decode(string raw)
    {
        // Strict form-urlencoded decoding: no malformed percent escapes or invalid UTF-8.
        try
        {
            var bytes=new List<byte>(raw.Length);
            for(int i=0;i<raw.Length;i++)
            {
                var c=raw[i];
                if(c=='%')
                {
                    if(i+2>=raw.Length||!byte.TryParse(raw.AsSpan(i+1,2),NumberStyles.AllowHexSpecifier,
                        CultureInfo.InvariantCulture,out var b))return null;
                    bytes.Add(b);i+=2;
                }
                else if(c=='+')bytes.Add(32);
                else if(c<128)bytes.Add((byte)c);
                else return null;
            }
            var value=new UTF8Encoding(false,true).GetString(bytes.ToArray());
            return value.Any(char.IsControl)?null:value;
        }
        catch(DecoderFallbackException){return null;}
    }
    internal static bool Fresh(DateTimeOffset issued,DateTimeOffset now) =>
        issued>=now.AddMinutes(-5)&&issued<=now.AddSeconds(30);

    public static Verified? Verify(MessengerRealm realm,string? token,string? initData,DateTimeOffset now)
    {
        if(!ValidToken(realm,token)||initData is not {Length:>0 and <=16384})return null;
        var fields=new SortedDictionary<string,string>(StringComparer.Ordinal);
        foreach(var pair in initData.Split('&'))
        {
            var split=pair.IndexOf('=');
            if(split<=0||fields.Count>=32)return null;
            var key=Decode(pair[..split]);var value=Decode(pair[(split+1)..]);
            if(key is not {Length:>0 and <=64}||value is null||
                !key.All(c=>char.IsAsciiLetterOrDigit(c)||c=='_')||!fields.TryAdd(key,value))return null;
        }
        if(!fields.Remove("hash",out var supplied)||supplied.Length!=64||!supplied.All(Uri.IsHexDigit)||
            !fields.TryGetValue("auth_date",out var date)||!long.TryParse(date,NumberStyles.None,CultureInfo.InvariantCulture,out var seconds)||
            seconds<0||seconds>253402300799||!fields.TryGetValue("user",out var userJson))return null;
        var issued=DateTimeOffset.FromUnixTimeSeconds(seconds);
        if(!Fresh(issued,now))return null;
        var secret=HMACSHA256.HashData(Encoding.UTF8.GetBytes("WebAppData"),Encoding.UTF8.GetBytes(token!));
        var expected=HMACSHA256.HashData(secret,Encoding.UTF8.GetBytes(string.Join("\n",fields.Select(x=>x.Key+"="+x.Value))));
        if(!CryptographicOperations.FixedTimeEquals(expected,Convert.FromHexString(supplied)))return null;
        try
        {
            using var user=JsonDocument.Parse(userJson);
            var obj=user.RootElement;
            if(!TelegramIdentityProof.UniqueObject(obj)||!obj.TryGetProperty("id",out var id)||
                id.ValueKind!=JsonValueKind.Number||!id.TryGetInt64(out var uid)||uid<=0||
                obj.TryGetProperty("is_bot",out var bot)&&bot.ValueKind!=JsonValueKind.False)return null;
            // Ignore signed chat/receiver/start_param as well: they grant no world permission.
            var key=Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(realm.BotId+":"+Convert.ToHexString(expected))));
            return new(realm.BotId,uid.ToString(CultureInfo.InvariantCulture),key,issued);
        }
        catch(JsonException){return null;}
    }
}
