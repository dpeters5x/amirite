using AmIRite.Web.Models;

namespace AmIRite.Web.Services;

/// <summary>
/// Orchestrates round-advance notifications: SSE first (if connected), then FCM, then email fallback.
/// </summary>
public class NotificationService(
    SseService sse,
    FcmService fcm,
    EmailService email,
    ILogger<NotificationService> logger)
{
    public async Task NotifyRoundAdvancedAsync(
        SessionPlayer sp, Player player, string htmlPartial, string baseUrl)
    {
        // 1. SSE — if connected, push the partial and skip push/email
        if (sse.IsConnected(sp.Token))
        {
            await sse.SendEventAsync(sp.Token, "round_advanced", htmlPartial);
            return;
        }

        // 2. FCM — if device token present, try push
        if (!string.IsNullOrEmpty(player.FcmToken))
        {
            var sent = await fcm.SendAsync(player.FcmToken, "AmIRite", "It's your turn!");
            if (sent) return;
        }

        // 3. Email fallback
        var playUrl = $"{baseUrl}/play/{sp.Token}";
        await email.SendAsync(player.Email, "AmIRite — it's your turn!",
            $"""
            <div style="font-family:sans-serif;max-width:480px;margin:0 auto">
              <h2 style="color:#5b4fcf">AmIRite</h2>
              <p>A new round is ready. It's your turn!</p>
              <p><a href="{playUrl}" style="display:inline-block;background:#5b4fcf;color:#fff;padding:12px 24px;border-radius:8px;text-decoration:none;font-weight:600">Play now</a></p>
            </div>
            """);
    }

    public async Task NotifyGamePausedAsync(SessionPlayer sp, Player player)
    {
        if (sse.IsConnected(sp.Token))
        {
            await sse.SendEventAsync(sp.Token, "game_paused",
                "<div class='paused-notice'>We're having a technical issue — hang tight, the game will resume shortly.</div>");
            return;
        }

        if (!string.IsNullOrEmpty(player.FcmToken))
        {
            await fcm.SendAsync(player.FcmToken, "AmIRite",
                "We're having a technical issue — the game will resume shortly.");
            return;
        }

        await email.SendAsync(player.Email, "AmIRite — brief technical issue",
            """
            <div style="font-family:sans-serif;max-width:480px;margin:0 auto">
              <h2 style="color:#5b4fcf">AmIRite</h2>
              <p>We're having a brief technical issue generating the round. The game will resume automatically once it's resolved.</p>
            </div>
            """);
    }

    public async Task NotifyGameResumedAsync(SessionPlayer sp, Player player)
    {
        if (sse.IsConnected(sp.Token))
        {
            await sse.SendEventAsync(sp.Token, "round_advanced", string.Empty);
            return;
        }

        if (!string.IsNullOrEmpty(player.FcmToken))
            await fcm.SendAsync(player.FcmToken, "AmIRite", "The game has resumed — it's time to play!");
    }
}
