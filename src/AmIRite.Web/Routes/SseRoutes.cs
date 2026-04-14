using AmIRite.Web.Services;

namespace AmIRite.Web.Routes;

public static class SseRoutes
{
    public static void MapSseRoutes(this WebApplication app)
    {
        app.MapGet("/api/sse/{token}", async (
            string token,
            HttpContext ctx,
            SseService sse,
            AuthService auth,
            SessionService sessions,
            CancellationToken ct) =>
        {
            // Verify the token belongs to an authenticated player
            var player = await auth.GetPlayerFromCookieAsync(ctx);
            var sp = await sessions.GetSessionPlayerByTokenAsync(token);

            if (player == null || sp == null || sp.PlayerId != player.Id)
                return Results.Unauthorized();

            ctx.Response.Headers["Content-Type"] = "text/event-stream";
            ctx.Response.Headers["Cache-Control"] = "no-cache";
            ctx.Response.Headers["X-Accel-Buffering"] = "no";
            ctx.Response.Headers["Connection"] = "keep-alive";

            sse.Register(token, ctx.Response);
            try
            {
                await Task.Delay(Timeout.Infinite, ct);
            }
            catch (OperationCanceledException)
            {
                // Normal disconnect
            }
            finally
            {
                sse.Unregister(token, ctx.Response);
            }

            return Results.Empty;
        });
    }
}
