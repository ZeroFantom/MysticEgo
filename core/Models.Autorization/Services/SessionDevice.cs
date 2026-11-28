using System.Text.RegularExpressions;
namespace Models.Autorization.Services;

/// <summary>Informational UA classification only; never used as a trusted device identity.</summary>
public sealed record SessionDevice(string Kind,string Os,string? Version,string Client)
{
    public static SessionDevice Describe(string? ua)
    {
        if(string.IsNullOrWhiteSpace(ua))return new("unknown","unknown",null,"unknown");
        bool Has(string text)=>ua.Contains(text,StringComparison.OrdinalIgnoreCase);
        string? Version(string pattern) {var m=Regex.Match(ua,pattern,RegexOptions.IgnoreCase|RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(50));return m.Success?m.Groups[1].Value.Replace('_','.'):null;}
        var browser=Has("Edg")?"Edge":Has("YaBrowser")?"Yandex":Has("SamsungBrowser")?"Samsung Internet":Has("Firefox")||Has("FxiOS")?"Firefox":Has("Chrome")||Has("CriOS")?"Chrome":Has("Safari")?"Safari":"unknown";
        if(Has("Android"))return new(Has("Mobile")||Has("MysticEgoAndroid")?"mobile":"tablet","Android",Version(@"Android\s+([0-9.]+)"),Has("MysticEgoAndroid/")?"MysticEgo Mobile":browser);
        if(Has("iPhone")||Has("iPad")||Has("iPod"))return new(Has("iPad")?"tablet":"mobile","iOS",Version(@"(?:CPU(?: iPhone)? OS|iPhone OS)\s+([0-9_]+)"),browser);
        var os=Has("Windows")?"Windows":Has("Macintosh")?"macOS":Has("Linux")?"Linux":"unknown";
        return new(os=="unknown"?"unknown":"desktop",os,null,Has("Electron")?"MysticEgo Desktop":browser);
    }
}
