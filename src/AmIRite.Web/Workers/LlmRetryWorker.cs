using AmIRite.Web.Services;

namespace AmIRite.Web.Workers;

/// <summary>
/// Retries LLM decoy generation for sessions stuck in 'paused' status.
/// Runs every 5 minutes and re-triggers decoy generation for the current round.
/// </summary>
public class LlmRetryWorker(
    IServiceScopeFactory scopeFactory,
    IConfiguration config,
    ILogger<LlmRetryWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromMinutes(5), ct);
            try
            {
                using var scope = scopeFactory.CreateScope();
                var rounds = scope.ServiceProvider.GetRequiredService<RoundService>();
                var sessions = scope.ServiceProvider.GetRequiredService<SessionService>();
                var notifications = scope.ServiceProvider.GetRequiredService<NotificationService>();
                var players = scope.ServiceProvider.GetRequiredService<PlayerService>();

                var baseUrl = config["AppBaseUrl"] ?? "";
                var pausedRounds = await rounds.GetPausedRoundsAsync();
                foreach (var round in pausedRounds)
                {
                    logger.LogInformation("Retrying LLM decoy generation for round {RoundId}", round.Id);

                    var session = await sessions.GetByIdAsync(round.SessionId);
                    if (session == null) continue;

                    try
                    {
                        await rounds.RetryDecoysAsync(round.Id);

                        var sessionPlayers = (await sessions.GetSessionPlayersAsync(session.Id)).ToList();
                        foreach (var sp in sessionPlayers)
                        {
                            var player = await players.GetByIdAsync(sp.PlayerId);
                            if (player != null)
                                await notifications.NotifyRoundAdvancedAsync(sp, player, "", baseUrl);
                        }

                        logger.LogInformation(
                            "LLM retry succeeded for session {SessionId} round {RoundId}",
                            session.Id, round.Id);
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex,
                            "LLM retry failed for session {SessionId} round {RoundId}",
                            session.Id, round.Id);
                    }
                }
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                logger.LogError(ex, "LlmRetryWorker error");
            }
        }
    }
}
