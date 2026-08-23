namespace LootChatReader;

internal sealed class EventSequenceTracker
{
    private const int StableFramesRequired = 2;

    private IReadOnlyList<DetectedEvent> _baseline = Array.Empty<DetectedEvent>();
    private IReadOnlyList<DetectedEvent> _candidate = Array.Empty<DetectedEvent>();
    private int _candidateFrames;
    private bool _needsBaseline = true;

    public IReadOnlyList<DetectedEvent> Observe(
        IReadOnlyList<DetectedEvent> current,
        int visualVerticalShift = 0)
    {
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

        // Deliberately compare only with the immediately preceding OCR frame.
        // There is no session-level replay suppression: rows exposed again by a
        // scroll are allowed to count again. The visual shift only maps instances
        // that remained visible from one frame to the next.
        var newEvents = FindUnmatchedAfterMovement(
            _baseline,
            current,
            Math.Abs(visualVerticalShift) > 4 ? visualVerticalShift : 0);
        _baseline = current.ToArray();
        _candidate = current.ToArray();
        _candidateFrames = 1;
        return newEvents;
    }

    public IReadOnlyList<DetectedEvent> AcceptAllAndSetBaseline(IReadOnlyList<DetectedEvent> current)
    {
        SetBaselineImmediately(current);
        return current.ToArray();
    }

    private static IReadOnlyList<DetectedEvent> FindUnmatchedAfterMovement(
        IReadOnlyList<DetectedEvent> previous,
        IReadOnlyList<DetectedEvent> current,
        int verticalShift)
    {
        const int positionTolerance = 8;
        var matchedCurrent = new bool[current.Count];
        foreach (var previousEvent in previous.OrderBy(item => item.Top))
        {
            var expectedTop = previousEvent.Top + verticalShift;
            var bestIndex = -1;
            var bestDistance = int.MaxValue;
            for (var currentIndex = 0; currentIndex < current.Count; currentIndex++)
            {
                if (matchedCurrent[currentIndex]
                    || current[currentIndex].Identity != previousEvent.Identity)
                {
                    continue;
                }

                var distance = Math.Abs(current[currentIndex].Top - expectedTop);
                if (distance <= positionTolerance && distance < bestDistance)
                {
                    bestDistance = distance;
                    bestIndex = currentIndex;
                }
            }

            if (bestIndex >= 0)
            {
                matchedCurrent[bestIndex] = true;
            }
        }

        return current
            .Where((_, index) => !matchedCurrent[index])
            .ToArray();
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
