public record ClicksUpdateJob(string ShortCode);

public class ClicksUpdateQueue
{
    private readonly object _gate = new();
    private readonly Dictionary<string, long> _clicks = new(StringComparer.Ordinal);

    // Limit distinct links so memory stays bounded whilst allowing clicks to continue accumulating.
    private const int MaxTrackedLinks = 10_000;

    private readonly ILogger<ClicksUpdateQueue> _logger;
    private bool _completed;

    public ClicksUpdateQueue(ILogger<ClicksUpdateQueue> logger)
    {
        _logger = logger;
    }

    public bool TryEnqueue(ClicksUpdateJob job)
    {
        lock (_gate)
        {
            if (!_completed)
            {
                if (_clicks.TryGetValue(job.ShortCode, out var count))
                {
                    if (count < long.MaxValue)
                    {
                        _clicks[job.ShortCode] = count + 1;
                        return true;
                    }
                }
                else if (_clicks.Count < MaxTrackedLinks)
                {
                    _clicks.Add(job.ShortCode, 1);
                    return true;
                }
            }
        }

        _logger.LogWarning(
            "Dropped click for {ShortCode}: tracking capacity reached, counter full, or stopping",
            job.ShortCode
        );
        return false;
    }

    public Dictionary<string, long> Snapshot()
    {
        lock (_gate)
        {
            return new Dictionary<string, long>(_clicks, StringComparer.Ordinal);
        }
    }

    public void RemovePersistedClicks(IReadOnlyDictionary<string, long> snapshot)
    {
        lock (_gate)
        {
            foreach (var (code, persistedCount) in snapshot)
            {
                var remaining = _clicks[code] - persistedCount;
                if (remaining == 0)
                {
                    _clicks.Remove(code);
                }
                else
                {
                    _clicks[code] = remaining;
                }
            }
        }
    }

    public void StopAcceptingClicks()
    {
        lock (_gate)
        {
            _completed = true;
        }
    }
}
