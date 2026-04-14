using AmIRite.Web.Services;

namespace AmIRite.Web.Routes;

public static class PlayerRoutes
{
    public static void MapPlayerRoutes(this WebApplication app)
    {
        app.MapGet("/profile", async (
            HttpContext ctx,
            AuthService auth,
            SessionService sessions,
            PlayerService players,
            RoundService rounds,
            AchievementService achievements) =>
        {
            var player = await auth.GetPlayerFromCookieAsync(ctx);
            if (player == null) return Results.Redirect("/auth/otp?returnUrl=/profile");

            var allSessions = (await sessions.GetAllNonArchivedAsync()).ToList();
            var mySessions = allSessions.Where(s =>
                s.Player1Id == player.Id || s.Player2Id == player.Id).ToList();

            var activeSessions = mySessions.Where(s => s.Status == "active").ToList();
            var finishedSessions = mySessions.Where(s => s.Status == "finished").ToList();

            // Achievements
            var myAchievements = (await achievements.GetPlayerAchievementsAsync(player.Id)).ToList();

            var activeGamesHtml = string.Join("", await Task.WhenAll(activeSessions.Select(async s =>
            {
                var sp = (await sessions.GetSessionPlayersAsync(s.Id))
                    .FirstOrDefault(sp => sp.PlayerId == player.Id);
                var opponentId = s.Player1Id == player.Id ? s.Player2Id : s.Player1Id;
                var opponentSp = opponentId.HasValue
                    ? (await sessions.GetSessionPlayersAsync(s.Id)).FirstOrDefault(sp => sp.PlayerId == opponentId)
                    : null;
                var round = await rounds.GetCurrentRoundAsync(s.Id);
                return $"""
                    <div class="game-card">
                      <div class="game-card-meta">
                        <span class="opponent">vs {opponentSp?.Nickname ?? "Unknown"}</span>
                        <span class="round-badge">Round {round?.RoundNumber ?? 1}</span>
                      </div>
                      <a href="/play/{sp?.Token}" class="btn btn-primary btn-sm">Continue</a>
                    </div>
                    """;
            })));

            var finishedGamesHtml = string.Join("", await Task.WhenAll(finishedSessions.Select(async s =>
            {
                var opponentId = s.Player1Id == player.Id ? s.Player2Id : s.Player1Id;
                var opponentSp = opponentId.HasValue
                    ? (await sessions.GetSessionPlayersAsync(s.Id)).FirstOrDefault(sp => sp.PlayerId == opponentId)
                    : null;
                var myScore = await rounds.GetScoreAsync(s.Id, player.Id);
                var opponentScore = opponentId.HasValue
                    ? await rounds.GetScoreAsync(s.Id, opponentId.Value) : 0;
                var date = s.EndedAt?.ToString("MMM d, yyyy") ?? "";

                return $"""
                    <div class="game-card">
                      <div class="game-card-meta">
                        <span class="opponent">vs {opponentSp?.Nickname ?? "Unknown"}</span>
                        <span class="date">{date}</span>
                        <span class="score">{myScore} — {opponentScore}</span>
                      </div>
                      <a href="/results/{s.Id}" class="btn btn-secondary btn-sm">Results</a>
                      <form method="post" action="/api/game/rematch" style="display:inline">
                        <input type="hidden" name="session_id" value="{s.Id}" />
                        <button type="submit" class="btn btn-ghost btn-sm">Rematch</button>
                      </form>
                    </div>
                    """;
            })));

            var achievementsHtml = myAchievements.Count == 0
                ? "<p class='text-muted'>No achievements yet — keep playing!</p>"
                : string.Join("", myAchievements.Select(pair => $"""
                    <div class="achievement-badge">
                      <img src="/img/achievements/{pair.Achievement.Icon}" alt="{pair.Achievement.Name}" />
                      <span>{pair.Achievement.Name}</span>
                    </div>
                    """));

            var body = $"""
                {HtmlLayout.NavBar(player.Nickname)}
                <main class="container">
                  <div class="card">
                    <h1 class="page-title">Your Profile</h1>
                    <p class="text-muted">{player.Email}</p>

                    {(activeSessions.Any() ? $"""
                      <section class="profile-section">
                        <h2>Active games</h2>
                        <div class="game-list">{activeGamesHtml}</div>
                      </section>
                    """ : "")}

                    <section class="profile-section">
                      <h2>Achievements</h2>
                      <div class="achievement-grid">{achievementsHtml}</div>
                    </section>

                    {(finishedSessions.Any() ? $"""
                      <section class="profile-section">
                        <h2>Game history</h2>
                        <div class="game-list">{finishedGamesHtml}</div>
                      </section>
                    """ : "")}
                  </div>
                </main>
                """;

            return HtmlLayout.Page("Profile", body);
        });
    }
}
