namespace MysticEgo.Soul;

/// <summary>Read-only projection of existing SoulEngine progression. Excludes identity,
/// memories, questionnaire results, assistant card, quest answers and hidden state.
/// Does not prove active embodiment or world placement.</summary>
public sealed record SoulMirrorAttribute(string Id,string Label,int? Value,bool Locked);
public sealed record SoulMirrorView(string State,int Level,string Rank,int Experience,int InLevel,int ToNext,
    int AvailablePoints,IReadOnlyList<SoulMirrorAttribute> Attributes);
public sealed class SoulMirrorReader(ISoulService souls)
{
    public async Task<SoulMirrorView?> Read(Guid account,CancellationToken ct=default)
    {
        if(account==Guid.Empty)return null;
        var record=await souls.Get(account.ToString("D"),ct);
        if(record is null)return null;
        if(record.UserId!=account.ToString("D")||record.State is not ("EgoShard" or "DigitalSoul"))
            throw new InvalidDataException("Invalid soul projection.");
        var progress=GameProgress.Calculate(record);
        return new(record.State,progress.Level,progress.Rank,progress.Experience,progress.InLevel,progress.ToNext,
            progress.AvailablePoints,GameProgress.Attributes(record).Select(x=>new SoulMirrorAttribute(x.Id,x.Label,x.Value,x.Locked)).ToArray());
    }
}
