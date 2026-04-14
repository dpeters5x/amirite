using AmIRite.Web.Services;

namespace AmIRite.Web.Workers;

public class JoinExpiryWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<JoinExpiryWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromMinutes(5), ct);
            try
            {
                using var scope = scopeFactory.CreateScope();
                var sessions = scope.ServiceProvider.GetRequiredService<SessionService>();
                var email = scope.ServiceProvider.GetRequiredService<EmailService>();

                var expired = await sessions.GetExpiredPendingAsync();
                foreach (var session in expired)
                {
                    await sessions.SetStatusAsync(session.Id, "cancelled");

                    // Notify organizer
                    await email.SendAsync(session.OrganizerEmail,
                        "AmIRite — game cancelled",
                        $"""
                        <div style="font-family:sans-serif">
                          <h2 style="color:#5b4fcf">AmIRite</h2>
                          <p>Your game was cancelled because not all players joined within the allotted time.</p>
                        </div>
                        """);

                    logger.LogInformation("Cancelled expired session {SessionId}", session.Id);
                }
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                logger.LogError(ex, "JoinExpiryWorker error");
            }
        }
    }
}
