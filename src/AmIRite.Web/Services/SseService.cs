using System.Collections.Concurrent;
using System.Text;

namespace AmIRite.Web.Services;

/// <summary>
/// Manages open SSE connections keyed by player token.
/// Supports multiple connections per token (e.g. two browser tabs).
/// </summary>
public class SseService
{
    private readonly ConcurrentDictionary<string, ConcurrentBag<HttpResponse>> _clients = new();

    public void Register(string token, HttpResponse response)
    {
        var bag = _clients.GetOrAdd(token, _ => new ConcurrentBag<HttpResponse>());
        bag.Add(response);
    }

    public void Unregister(string token, HttpResponse response)
    {
        if (!_clients.TryGetValue(token, out var bag)) return;

        // Rebuild bag without this response
        var remaining = bag.Where(r => r != response).ToList();
        var newBag = new ConcurrentBag<HttpResponse>(remaining);
        _clients.TryUpdate(token, newBag, bag);
    }

    public bool IsConnected(string token) =>
        _clients.TryGetValue(token, out var bag) && !bag.IsEmpty;

    /// <summary>
    /// Sends a named SSE event containing a pre-rendered HTML partial.
    /// </summary>
    public async Task SendEventAsync(string token, string eventName, string htmlPartial)
    {
        if (!_clients.TryGetValue(token, out var bag)) return;

        var payload = BuildSseFrame(eventName, htmlPartial);
        var stale = new List<HttpResponse>();

        foreach (var response in bag)
        {
            try
            {
                await response.WriteAsync(payload);
                await response.Body.FlushAsync();
            }
            catch
            {
                stale.Add(response);
            }
        }

        foreach (var r in stale) Unregister(token, r);
    }

    /// <summary>
    /// Sends a heartbeat comment to all connected clients, evicting stale connections.
    /// </summary>
    public async Task SendHeartbeatsAsync()
    {
        foreach (var (token, bag) in _clients)
        {
            var stale = new List<HttpResponse>();
            foreach (var response in bag)
            {
                try
                {
                    await response.WriteAsync(": heartbeat\n\n");
                    await response.Body.FlushAsync();
                }
                catch
                {
                    stale.Add(response);
                }
            }
            foreach (var r in stale) Unregister(token, r);
        }
    }

    private static string BuildSseFrame(string eventName, string data)
    {
        // Escape newlines in data per SSE spec
        var escapedData = data.Replace("\n", "\ndata: ");
        return $"event: {eventName}\ndata: {escapedData}\n\n";
    }
}
