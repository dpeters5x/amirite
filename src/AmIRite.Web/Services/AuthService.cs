using System.Security.Cryptography;
using AmIRite.Web.Data;
using AmIRite.Web.Models;
using Dapper;

namespace AmIRite.Web.Services;

public class AuthService(
    IDbConnectionFactory db,
    EmailService email,
    GameOptions options,
    ILogger<AuthService> logger)
{
    private const string CookieName = "amirite_session";

    // -- OTP --

    public async Task<string> CreateOtpAsync(string emailAddress)
    {
        var code = GenerateOtp();
        var expiry = DateTime.UtcNow.AddMinutes(options.OtpExpiryMinutes);

        using var conn = db.Create();
        await conn.ExecuteAsync(
            "INSERT INTO otp_codes (email, code, expires_at) VALUES (@email, @code, @expiry)",
            new { email = emailAddress.ToLowerInvariant(), code, expiry });

        await email.SendOtpAsync(emailAddress, code);
        return code; // returned for testing; callers don't need to store it
    }

    public async Task<bool> ValidateOtpAsync(string emailAddress, string code)
    {
        using var conn = db.Create();
        var otp = await conn.QuerySingleOrDefaultAsync<OtpCode>(
            """
            SELECT * FROM otp_codes
            WHERE email = @email AND code = @code AND used = 0 AND expires_at > @now
            ORDER BY id DESC LIMIT 1
            """,
            new { email = emailAddress.ToLowerInvariant(), code, now = DateTime.UtcNow });

        if (otp == null) return false;

        await conn.ExecuteAsync(
            "UPDATE otp_codes SET used = 1 WHERE id = @id", new { id = otp.Id });

        return true;
    }

    // -- Player session (cookie) --

    public async Task<string> CreatePlayerSessionAsync(int playerId)
    {
        var sessionId = Guid.NewGuid().ToString("N");
        var expiry = DateTime.UtcNow.AddDays(options.SessionCookieExpiryDays);

        using var conn = db.Create();
        await conn.ExecuteAsync(
            "INSERT INTO player_sessions (id, player_id, expires_at) VALUES (@id, @playerId, @expiry)",
            new { id = sessionId, playerId, expiry });

        return sessionId;
    }

    public async Task<Player?> GetPlayerFromCookieAsync(HttpContext ctx)
    {
        var sessionId = ctx.Request.Cookies[CookieName];
        if (string.IsNullOrEmpty(sessionId)) return null;

        using var conn = db.Create();
        var session = await conn.QuerySingleOrDefaultAsync<PlayerSession>(
            "SELECT * FROM player_sessions WHERE id = @id AND expires_at > @now",
            new { id = sessionId, now = DateTime.UtcNow });

        if (session == null) return null;

        var player = await conn.QuerySingleOrDefaultAsync<Player>(
            "SELECT * FROM players WHERE id = @id", new { id = session.PlayerId });

        if (player != null)
        {
            await conn.ExecuteAsync(
                "UPDATE players SET last_seen_at = @now WHERE id = @id",
                new { now = DateTime.UtcNow, id = player.Id });
        }

        return player;
    }

    public void SetSessionCookie(HttpContext ctx, string sessionId)
    {
        ctx.Response.Cookies.Append(CookieName, sessionId, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Lax,
            Expires = DateTimeOffset.UtcNow.AddDays(options.SessionCookieExpiryDays)
        });
    }

    public void ClearSessionCookie(HttpContext ctx)
    {
        ctx.Response.Cookies.Delete(CookieName);
    }

    // -- Admin Basic Auth --

    public bool ValidateAdminCredentials(HttpContext ctx, AdminOptions admin)
    {
        var authHeader = ctx.Request.Headers.Authorization.ToString();
        if (!authHeader.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
            return false;

        try
        {
            var encoded = authHeader["Basic ".Length..].Trim();
            var decoded = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
            var parts = decoded.Split(':', 2);
            return parts.Length == 2
                && parts[0] == admin.Username
                && parts[1] == admin.Password;
        }
        catch
        {
            return false;
        }
    }

    // -- Helpers --

    private static string GenerateOtp() =>
        RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
}
