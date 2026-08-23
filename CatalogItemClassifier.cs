namespace LootChatReader;

internal static class CatalogItemClassifier
{
    public static DetectedEventKind Classify(DetectedEvent detectedEvent, ItemIconEntry catalogEntry)
    {
        if (detectedEvent.Kind == DetectedEventKind.Experience)
        {
            return detectedEvent.Kind;
        }

        if (detectedEvent.Adena > 0)
        {
            return DetectedEventKind.Drop;
        }

        return catalogEntry.IsQuestItem
            ? DetectedEventKind.QuestItem
            : DetectedEventKind.Drop;
    }
}
