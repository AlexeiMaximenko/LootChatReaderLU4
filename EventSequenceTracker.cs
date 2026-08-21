namespace LootChatReader;

internal sealed class EventSequenceTracker
{
    private const int StableFramesRequired = 2;

    private IReadOnlyList<DetectedEvent> _baseline = Array.Empty<DetectedEvent>();
    private IReadOnlyList<DetectedEvent> _candidate = Array.Empty<DetectedEvent>();
    private int _candidateFrames;
    private bool _needsBaseline = true;

    public IReadOnlyList<DetectedEvent> Observe(IReadOnlyList<DetectedEvent> current)
    {
        if (current.Count == 0 && !_needsBaseline)
        {
            // A transient empty OCR result cannot prove that the visible list changed.
            return Array.Empty<DetectedEvent>();
        }

        if (_needsBaseline)
        {
            if (!SequencesEqual(_candidate, current))
            {
                _candidate = current.ToArray();
                _candidateFrames = 1;
                return Array.Empty<DetectedEvent>();
            }

            _candidateFrames++;
            if (_candidateFrames < StableFramesRequired)
            {
                return Array.Empty<DetectedEvent>();
            }

            _baseline = _candidate.ToArray();
            _needsBaseline = false;
            return Array.Empty<DetectedEvent>();
        }

        if (SequencesEqual(_baseline, current))
        {
            return Array.Empty<DetectedEvent>();
        }

        // During normal monitoring an event is accepted from the first readable
        // frame. Waiting for the entire viewport to repeat loses short-lived rows
        // when combat messages move the chat several times per second.
        var newSuffixStart = FindNewSuffixStart(_baseline, current);
        _baseline = current.ToArray();
        _candidate = current.ToArray();
        _candidateFrames = 1;
        return current.Skip(newSuffixStart).ToArray();
    }

    public void BeginResynchronization()
    {
        _candidate = Array.Empty<DetectedEvent>();
        _candidateFrames = 0;
        _needsBaseline = true;
    }

    public void SetBaselineImmediately(IReadOnlyList<DetectedEvent> current)
    {
        _baseline = current.ToArray();
        _candidate = current.ToArray();
        _candidateFrames = StableFramesRequired;
        _needsBaseline = false;
    }

    public void Reset()
    {
        _baseline = Array.Empty<DetectedEvent>();
        BeginResynchronization();
    }

    private static int FindNewSuffixStart(
        IReadOnlyList<DetectedEvent> previous,
        IReadOnlyList<DetectedEvent> current)
    {
        if (previous.Count == 0)
        {
            return 0;
        }

        var lengths = new int[previous.Count + 1, current.Count + 1];
        for (var previousIndex = previous.Count - 1; previousIndex >= 0; previousIndex--)
        {
            for (var currentIndex = current.Count - 1; currentIndex >= 0; currentIndex--)
            {
                lengths[previousIndex, currentIndex] =
                    previous[previousIndex].Identity == current[currentIndex].Identity
                        ? lengths[previousIndex + 1, currentIndex + 1] + 1
                        : Math.Max(lengths[previousIndex + 1, currentIndex], lengths[previousIndex, currentIndex + 1]);
            }
        }

        var lastMatchedCurrentIndex = -1;
        var i = 0;
        var j = 0;
        while (i < previous.Count && j < current.Count)
        {
            if (previous[i].Identity == current[j].Identity)
            {
                lastMatchedCurrentIndex = j;
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

        // OCR recovery may insert a previously missed row between known rows. Only
        // the suffix after the last known anchor can represent newly appended chat.
        return lastMatchedCurrentIndex + 1;
    }

    private static bool SequencesEqual(
        IReadOnlyList<DetectedEvent> first,
        IReadOnlyList<DetectedEvent> second)
    {
        if (first.Count != second.Count)
        {
            return false;
        }

        for (var index = 0; index < first.Count; index++)
        {
            if (first[index].Identity != second[index].Identity)
            {
                return false;
            }
        }

        return true;
    }
}
