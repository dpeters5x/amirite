using AmIRite.Web.Models;
using AmIRite.Web.Services;

namespace AmIRite.Web.Routes;

public static class AuthRoutes
{
    public static void MapAuthRoutes(this WebApplication app)
    {
        // GET /auth/otp — show OTP entry form
        app.MapGet("/auth/otp", (HttpContext ctx, string? email, string? returnUrl) =>
        {
            var body = $"""
                {HtmlLayout.NavBar()}
                <main class="container">
                  <div class="card auth-card">
                    <h1 class="page-title">Sign in</h1>
                    {(email == null ? OtpEmailForm(returnUrl) : OtpCodeForm(email, returnUrl))}
                  </div>
                </main>
                """;
            return HtmlLayout.Page("Sign in", body);
        });

        // POST /auth/otp — handle email submission (send OTP) or code submission (validate)
        app.MapPost("/auth/otp", async (
            HttpContext ctx,
            AuthService auth,
            PlayerService players,
            RateLimiterService rateLimiter) =>
        {
            var form = await ctx.Request.ReadFormAsync();
            var email = form["email"].ToString().Trim().ToLowerInvariant();
            var code = form["code"].ToString().Trim();
            var returnUrl = form["return_url"].ToString();

            if (!string.IsNullOrEmpty(code))
            {
                // Validate OTP
                var valid = await auth.ValidateOtpAsync(email, code);
                if (!valid)
                {
                    var body = $"""
                        {HtmlLayout.NavBar()}
                        <main class="container">
                          <div class="card auth-card">
                            <h1 class="page-title">Sign in</h1>
                            <div class="alert alert-error">Invalid or expired code. Please try again.</div>
                            {OtpCodeForm(email, returnUrl)}
                          </div>
                        </main>
                        """;
                    return HtmlLayout.Page("Sign in", body);
                }

                var player = await players.GetOrCreateAsync(email);
                var sessionId = await auth.CreatePlayerSessionAsync(player.Id);
                auth.SetSessionCookie(ctx, sessionId);

                var redirect = string.IsNullOrEmpty(returnUrl) ? "/profile" : returnUrl;
                return Results.Redirect(redirect);
            }
            else
            {
                // Send OTP
                var ip = ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                if (!rateLimiter.IsAllowed($"otp:{ip}", 5, TimeSpan.FromHours(1)))
                {
                    var retryAfter = rateLimiter.RetryAfterSeconds($"otp:{ip}", TimeSpan.FromHours(1));
                    ctx.Response.Headers["Retry-After"] = retryAfter.ToString();
                    return Results.StatusCode(429);
                }

                await auth.CreateOtpAsync(email);

                var body = $"""
                    {HtmlLayout.NavBar()}
                    <main class="container">
                      <div class="card auth-card">
                        <h1 class="page-title">Sign in</h1>
                        <div class="alert alert-success">We sent a 6-digit code to <strong>{email}</strong>. Check your inbox.</div>
                        {OtpCodeForm(email, returnUrl)}
                      </div>
                    </main>
                    """;
                return HtmlLayout.Page("Sign in", body);
            }
        }).DisableAntiforgery();

        // POST /auth/signout
        app.MapPost("/auth/signout", (HttpContext ctx, AuthService auth) =>
        {
            auth.ClearSessionCookie(ctx);
            return Results.Redirect("/");
        }).DisableAntiforgery();
    }

    private static string OtpEmailForm(string? returnUrl) => $"""
        <p class="text-muted">Enter your email address to receive a login code.</p>
        <form method="post" action="/auth/otp" class="form-stack">
          {(returnUrl != null ? $"""<input type="hidden" name="return_url" value="{returnUrl}" />""" : "")}
          <div class="form-group">
            <label for="email">Email address</label>
            <input id="email" type="email" name="email" required autofocus
                   class="input" placeholder="you@example.com" />
          </div>
          <button type="submit" class="btn btn-primary btn-block">Send code</button>
        </form>
        """;

    private static string OtpCodeForm(string email, string? returnUrl) => $"""
        <p class="text-muted">Enter the 6-digit code sent to <strong>{email}</strong>.</p>
        <form method="post" action="/auth/otp" class="form-stack">
          <input type="hidden" name="email" value="{email}" />
          {(returnUrl != null ? $"""<input type="hidden" name="return_url" value="{returnUrl}" />""" : "")}
          <div class="form-group">
            <label for="code">Login code</label>
            <input id="code" type="text" name="code" required autofocus inputmode="numeric"
                   maxlength="6" minlength="6" class="input input-code" placeholder="000000" />
          </div>
          <button type="submit" class="btn btn-primary btn-block">Verify</button>
        </form>
        <p class="text-center text-muted" style="margin-top:1rem">
          <a href="/auth/otp?email={Uri.EscapeDataString(email)}">Resend code</a>
        </p>
        """;
}
