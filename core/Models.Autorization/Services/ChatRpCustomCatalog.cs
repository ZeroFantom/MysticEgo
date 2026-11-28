using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace Models.Autorization.Services;

/// <summary>Operator-reviewed aliases and captions for existing non-sensitive actions.
/// No executable templates, URLs, mentions, custom consent rules or arbitrary media search.
/// Scope is set by the trusted group configuration, never by message payload.</summary>
public sealed record ChatRpCustomAction(string Command,ChatRpAction Action,string Template)
{
    public bool Valid =>
        Command is {Length:>=2 and <=40} && Command==Normalize(Command) &&
        Regex.IsMatch(Command,@"\A[а-яa-z][а-яa-z0-9 -]*[а-яa-z0-9]\z",RegexOptions.CultureInvariant) &&
        !Command.StartsWith("rp ",StringComparison.Ordinal) &&
        !Reserved.Contains(Command) && ChatRpCatalog.Parse(Command) is null &&
        ChatRpCatalog.Find(Action) is {RequiresConsent:false} &&
        Template is {Length:>=17 and <=240} && Template==Template.Trim() &&
        Template.IndexOf("{actor}",StringComparison.Ordinal)>=0 &&
        Template.IndexOf("{target}",StringComparison.Ordinal)>=0 &&
        Template.IndexOf("{actor}",StringComparison.Ordinal)==Template.LastIndexOf("{actor}",StringComparison.Ordinal) &&
        Template.IndexOf("{target}",StringComparison.Ordinal)==Template.LastIndexOf("{target}",StringComparison.Ordinal) &&
        Regex.IsMatch(Template.Replace("{actor}","").Replace("{target}",""),
            @"\A[\p{L}\p{M}\p{N} .,!?«»—–()\-]+\z",RegexOptions.CultureInvariant);

    private static readonly HashSet<string> Reserved=new(StringComparer.Ordinal)
        {"start","help","status","link","nearby","friends","talk","rp","rp enable","rp disable","rp on","rp off",
         "rp-enable","rp-disable","rp-on","rp-off"};

    public static string Normalize(string? text)
    {
        if(text is null||text.Length>64)return "";
        var value=text.Trim().ToLowerInvariant().Replace('ё','е');
        if(value.StartsWith("/rp ",StringComparison.Ordinal))value=value[4..].Trim();
        else if(value.StartsWith('/'))value=value[1..];
        return value;
    }

    public string Render(ChatRpActor actor,ChatRpActor target)
    {
        if(!Valid)throw new InvalidOperationException("Invalid RP template.");
        // Replace only original tokens; participant names cannot introduce another substitution.
        return Regex.Replace(Template,@"\{actor\}|\{target\}",m=>
            m.Value=="{actor}"?ChatRpCatalog.Name(actor.Name):ChatRpCatalog.Name(target.Name));
    }
}

public sealed class ChatRpCustomCatalog
{
    private readonly Dictionary<string,ChatRpCustomAction> actions=new(StringComparer.Ordinal);
    public ChatRpCustomCatalog(IEnumerable<ChatRpCustomAction> definitions)
    {
        foreach(var action in definitions.Take(65))
            if(actions.Count>=64||!action.Valid||!actions.TryAdd(action.Command,action))
                throw new ArgumentException("Invalid or duplicate group RP command.");
    }
    public ChatRpCustomAction? Find(string? text)=>actions.GetValueOrDefault(ChatRpCustomAction.Normalize(text));
    public bool Contains(ChatRpCustomAction action)=>actions.TryGetValue(action.Command,out var registered)&&registered==action;
    public static ChatRpCustomCatalog Empty { get; }=new([]);
}

/// <summary>Native VK media IDs already approved/uploaded by operator. Random selection from
/// bounded category collections. Legacy single attachment is only used when no list is configured.</summary>
public static class ChatRpVkMediaLibrary
{
    public static bool Valid(string? value)=>value is {Length:>0 and <=80}&&Regex.IsMatch(value,
        @"\A(?:photo|doc)-?[1-9][0-9]{0,18}_[1-9][0-9]{0,18}\z",RegexOptions.CultureInvariant);
    public static string? Pick(IEnumerable<string> configured,string? legacy=null)
    {
        var items=configured.Take(257).ToArray();
        if(items.Length==0)return Valid(legacy)?legacy:null;
        if(items.Length>256||items.Any(x=>!Valid(x)))return null;
        var unique=items.Distinct(StringComparer.Ordinal).ToArray();
        return unique[RandomNumberGenerator.GetInt32(unique.Length)];
    }
}
