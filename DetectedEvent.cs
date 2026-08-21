namespace LootChatReader;

internal enum DetectedEventKind
{
    Drop,
    QuestItem,
    Experience
}

internal sealed record DetectedEvent(
    DetectedEventKind Kind,
    string Value,
    string RawText,
    int Top,
    string SummaryName,
    long Quantity,
    long Xp,
    long Sp,
    long Adena)
{
    public string KindLabel => Kind switch
    {
        DetectedEventKind.Drop => "Drop",
        DetectedEventKind.QuestItem => "Quest item",
        DetectedEventKind.Experience => "XP / SP",
        _ => Kind.ToString()
    };

    public string Identity => $"{Kind}\u001f{Value}".ToUpperInvariant();
}
