using System.Collections.Concurrent;
using AmIRite.Web.Services;

namespace AmIRite.Web.Workers;

/// <summary>
/// Processes achievement evaluation jobs off the main request thread so award
/// logic never blocks game flow. Jobs are (playerId, sessionId) tuples.
/// </summary>
public class AchievementWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<AchievementWorker> logger) : BackgroundService
{
    private readonly ConcurrentQueue<(int PlayerId, string SessionId)> _queue = new();

    public void Enqueue(int playerId, string sessionId) =>
        _queue.Enqueue((playerId, sessionId));

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            if (_queue.TryDequeue(out var job))
            {
                try
                {
                    using var scope = scopeFactory.CreateScope();
                    var service = scope.ServiceProvider.GetRequiredService<AchievementService>();
                    await service.EvaluateAsync(job.PlayerId, job.SessionId);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Achievement evaluation failed for player {PlayerId}", job.PlayerId);
                }
            }
            else
            {
                await Task.Delay(TimeSpan.FromSeconds(2), ct);
            }
        }
    }
}
