using System.Collections.Concurrent;

namespace AmIRite.Web.Services;

/// <summary>
/// In-memory sliding window rate limiter keyed by arbitrary string (typically IP address or player token).
/// Thread-safe; no external dependencies.
/// </summary>
public class RateLimiterService
{
    // key -> sorted queue of UTC request timestamps within the window
    private readonly ConcurrentDictionary<string, Queue<DateTime>> _windows = new();

    /// <summary>
    /// Returns true if the request is within the allowed rate; records the request timestamp.
    /// Returns false if the limit is exceeded.
    /// </summary>
    public bool IsAllowed(string key, int limit, TimeSpan window)
    {
        var now = DateTime.UtcNow;
        var cutoff = now - window;

        var queue = _windows.GetOrAdd(key, _ => new Queue<DateTime>());
        lock (queue)
        {
            // Evict timestamps outside the window
            while (queue.Count > 0 && queue.Peek() < cutoff)
                queue.Dequeue();

            if (queue.Count >= limit)
                return false;

            queue.Enqueue(now);
            return true;
        }
    }

    /// <summary>
    /// Returns the number of seconds until the oldest entry in the window expires,
    /// giving callers a value for the Retry-After header.
    /// </summary>
    public int RetryAfterSeconds(string key, TimeSpan window)
    {
        if (!_windows.TryGetValue(key, out var queue))
            return 0;

        lock (queue)
        {
            if (queue.Count == 0) return 0;
            var oldestExpiry = queue.Peek() + window;
            var delay = (oldestExpiry - DateTime.UtcNow).TotalSeconds;
            return delay > 0 ? (int)Math.Ceiling(delay) : 0;
        }
    }
}
