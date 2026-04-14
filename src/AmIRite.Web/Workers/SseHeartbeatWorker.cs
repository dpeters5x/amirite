using AmIRite.Web.Services;

namespace AmIRite.Web.Workers;

public class SseHeartbeatWorker(SseService sse, ILogger<SseHeartbeatWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(30), ct);
            try { await sse.SendHeartbeatsAsync(); }
            catch (Exception ex) { logger.LogError(ex, "SSE heartbeat error"); }
        }
    }
}
