namespace LootChatReader;

/// <summary>
/// Associates parsed events with newly appeared physical chat rows. Text values
/// are deliberately not used for replay protection: six identical XP rows are
/// six different visual rows and therefore six events.
/// </summary>
internal sealed class EventSequenceTracker
{
    private const int PositionTolerance = 9;
    private const int MaximumRetryFrames = 8;

    private readonly List<PendingLine> _pendingLines = [];
    private bool _needsBaseline = true;

    public IReadOnlyList<DetectedEvent> Observe(
        IReadOnlyList<DetectedEvent> current,
        int visualVerticalShift,
        IReadOnlyList<Rectangle> newLineBands)
    {
        if (_needsBaseline)
        {
            _pendingLines.Clear();
            _needsBaseline = false;
            return [];
        }

        AdvancePendingLines(visualVerticalShift);
        foreach (var band in newLineBands.OrderBy(band => band.Top))
        {
            if (band.Height <= 0
                || _pendingLines.Any(existing =>
                    existing.Age == 0 && Math.Abs(existing.Bounds.Top - band.Top) <= 3))
            {
                continue;
            }
            _pendingLines.Add(new PendingLine(band, 0));
        }

        var accepted = new List<DetectedEvent>();
        foreach (var detectedEvent in current.OrderBy(item => item.Top))
        {
            var bestIndex = -1;
            var bestDistance = int.MaxValue;
            for (var index = 0; index < _pendingLines.Count; index++)
            {
                var distance = Math.Abs(detectedEvent.Top - _pendingLines[index].Bounds.Top);
                if (distance <= PositionTolerance && distance < bestDistance)
                {
                    bestIndex = index;
                    bestDistance = distance;
                }
            }
            if (bestIndex < 0)
            {
                continue;
            }

            accepted.Add(detectedEvent);
            _pendingLines.RemoveAt(bestIndex);
        }
        return accepted;
    }

    private void AdvancePendingLines(int visualVerticalShift)
    {
        for (var index = _pendingLines.Count - 1; index >= 0; index--)
        {
            var pending = _pendingLines[index];
            var moved = pending with
            {
                Bounds = new Rectangle(
                    pending.Bounds.X,
                    pending.Bounds.Y + visualVerticalShift,
                    pending.Bounds.Width,
                    pending.Bounds.Height),
                Age = pending.Age + 1
            };
            if (moved.Age > MaximumRetryFrames || moved.Bounds.Bottom < 0)
            {
                _pendingLines.RemoveAt(index);
            }
            else
            {
                _pendingLines[index] = moved;
            }
        }
    }

    public void BeginResynchronization()
    {
        _pendingLines.Clear();
        _needsBaseline = true;
    }

    public void SetBaselineImmediately(IReadOnlyList<DetectedEvent> current)
    {
        _ = current;
        _pendingLines.Clear();
        _needsBaseline = false;
    }

    public void Reset()
    {
        BeginResynchronization();
    }

    private sealed record PendingLine(Rectangle Bounds, int Age);
}
