namespace LootChatReader;

internal enum ChatListMotion
{
    Unknown,
    Stationary,
    ScrollUp,
    ScrollDown
}

internal sealed class ChatListMotionDetector
{
    private const int MovementThreshold = 5;
    private IReadOnlyList<DetectedEvent> _previous = Array.Empty<DetectedEvent>();

    public ChatListMotion Observe(IReadOnlyList<DetectedEvent> current)
    {
        if (current.Count == 0)
        {
            return ChatListMotion.Unknown;
        }

        var matchedPairs = FindMatchedPairs(_previous, current);
        _previous = current.ToArray();
        if (matchedPairs.Count == 0)
        {
            return ChatListMotion.Unknown;
        }

        var movements = matchedPairs
            .Select(pair => pair.Current.Top - pair.Previous.Top)
            .OrderBy(value => value)
            .ToArray();
        var middle = movements.Length / 2;
        var medianMovement = movements.Length % 2 == 0
            ? (movements[middle - 1] + movements[middle]) / 2
            : movements[middle];

        // Content moving down means the viewport is scrolling up, and vice versa.
        return medianMovement switch
        {
            > MovementThreshold => ChatListMotion.ScrollUp,
            < -MovementThreshold => ChatListMotion.ScrollDown,
            _ => ChatListMotion.Stationary
        };
    }

    public void Reset()
    {
        _previous = Array.Empty<DetectedEvent>();
    }

    private static IReadOnlyList<MatchedPair> FindMatchedPairs(
        IReadOnlyList<DetectedEvent> previous,
        IReadOnlyList<DetectedEvent> current)
    {
        var lengths = new int[previous.Count + 1, current.Count + 1];
        for (var previousIndex = previous.Count - 1; previousIndex >= 0; previousIndex--)
        {
            for (var currentIndex = current.Count - 1; currentIndex >= 0; currentIndex--)
            {
                lengths[previousIndex, currentIndex] =
                    previous[previousIndex].Identity == current[currentIndex].Identity
                        ? lengths[previousIndex + 1, currentIndex + 1] + 1
                        : Math.Max(
                            lengths[previousIndex + 1, currentIndex],
                            lengths[previousIndex, currentIndex + 1]);
            }
        }

        var result = new List<MatchedPair>();
        var i = 0;
        var j = 0;
        while (i < previous.Count && j < current.Count)
        {
            if (previous[i].Identity == current[j].Identity)
            {
                result.Add(new MatchedPair(previous[i], current[j]));
                i++;
                j++;
            }
            else if (lengths[i + 1, j] >= lengths[i, j + 1])
            {
                i++;
            }
            else
            {
                j++;
            }
        }

        return result;
    }

    private sealed record MatchedPair(DetectedEvent Previous, DetectedEvent Current);
}
