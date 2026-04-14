using AmIRite.Web.Models;
using AmIRite.Web.Services;

namespace AmIRite.Web.Routes;

public static class GameRoutes
{
    public static void MapGameRoutes(this WebApplication app)
    {
        // GET / — landing page
        app.MapGet("/", () => HtmlLayout.Page("Welcome", $"""
            {HtmlLayout.NavBar()}
            <main class="container">
              <section class="hero">
                <h1 class="hero-title">AmIRite?</h1>
                <p class="hero-tagline">A two-player guessing game. How well do you know each other?</p>
                <a href="/signup" class="btn btn-primary btn-lg">Start a game</a>
              </section>
              <section class="how-it-works">
                <h2>How it works</h2>
                <ol class="steps">
                  <li><strong>Answer</strong> personal questions about yourself each round.</li>
                  <li><strong>Guess</strong> which answer your opponent gave — mixed with fakes.</li>
                  <li><strong>Score</strong> a point for every correct guess.</li>
                  <li>See the <strong>final analysis</strong> of how well you know each other.</li>
                </ol>
              </section>
            </main>
            """));

        // GET /signup — organizer enters two email addresses
        app.MapGet("/signup", (HttpContext ctx, AuthService auth) =>
        {
            var body = $"""
                {HtmlLayout.NavBar()}
                <main class="container">
                  <div class="card">
                    <h1 class="page-title">Start a game</h1>
                    <p class="text-muted">Enter the email addresses of the two players. An invitation link will be sent to each.</p>
                    <form method="post" action="/signup" class="form-stack">
                      <div class="form-group">
                        <label for="email1">Player 1 email</label>
                        <input id="email1" type="email" name="email1" required class="input" placeholder="player1@example.com" autofocus />
                      </div>
                      <div class="form-group">
                        <label for="email2">Player 2 email</label>
                        <input id="email2" type="email" name="email2" required class="input" placeholder="player2@example.com" />
                      </div>
                      <button type="submit" class="btn btn-primary btn-block">Send invitations</button>
                    </form>
                  </div>
                </main>
                """;
            return HtmlLayout.Page("Start a game", body);
        });

        // POST /signup — create session, send invitations
        app.MapPost("/signup", async (
            HttpContext ctx,
            SessionService sessions,
            EmailService email,
            RateLimiterService rateLimiter) =>
        {
            var ip = ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            if (!rateLimiter.IsAllowed($"signup:{ip}", 10, TimeSpan.FromHours(1)))
            {
                ctx.Response.Headers["Retry-After"] =
                    rateLimiter.RetryAfterSeconds($"signup:{ip}", TimeSpan.FromHours(1)).ToString();
                return Results.StatusCode(429);
            }

            var form = await ctx.Request.ReadFormAsync();
            var email1 = form["email1"].ToString().Trim().ToLowerInvariant();
            var email2 = form["email2"].ToString().Trim().ToLowerInvariant();
            var organizerEmail = email1; // organizer is the first player by convention

            if (string.IsNullOrEmpty(email1) || string.IsNullOrEmpty(email2))
                return Results.BadRequest("Both email addresses are required.");

            if (email1 == email2)
            {
                var body = $"""
                    {HtmlLayout.NavBar()}
                    <main class="container">
                      <div class="card">
                        <h1 class="page-title">Start a game</h1>
                        <div class="alert alert-error">Players must have different email addresses.</div>
                        <a href="/signup" class="btn btn-secondary">Try again</a>
                      </div>
                    </main>
                    """;
                return HtmlLayout.Page("Start a game", body);
            }

            var session = await sessions.CreateAsync(organizerEmail, email1, email2);
            var sps = (await sessions.GetSessionPlayersAsync(session.Id)).ToList();
            var baseUrl = $"{ctx.Request.Scheme}://{ctx.Request.Host}";

            foreach (var sp in sps)
            {
                var player = sp.PlayerId; // player ID; email looked up from players table
                // Look up email from session_players -> players join (via PlayerService)
            }

            // Send invitations using tokens
            foreach (var sp in sps)
            {
                // Fetch the player email via the join
                var playerEmail = sp.PlayerId == sps[0].PlayerId ? email1 : email2;
                var joinUrl = $"{baseUrl}/join/{sp.Token}";
                await email.SendInvitationAsync(playerEmail, joinUrl, organizerEmail);
            }

            return Results.Redirect($"/lobby/{session.Id}");
        }).DisableAntiforgery();

        // GET /lobby/{sessionId} — waiting room after signup
        app.MapGet("/lobby/{sessionId}", async (
            string sessionId,
            SessionService sessions,
            PlayerService players) =>
        {
            var session = await sessions.GetByIdAsync(sessionId);
            if (session == null) return Results.NotFound();

            var sps = (await sessions.GetSessionPlayersAsync(sessionId)).ToList();

            var playerRows = string.Join("", sps.Select(sp =>
            {
                var status = sp.JoinedAt.HasValue
                    ? "<span class='badge badge-success'>Joined</span>"
                    : "<span class='badge badge-warning'>Pending</span>";
                var resend = sp.JoinedAt == null
                    ? $"""
                       <form method="post" action="/api/game/resend-invitation" style="display:inline">
                         <input type="hidden" name="token" value="{sp.Token}" />
                         <button type="submit" class="btn btn-sm btn-secondary">Resend</button>
                       </form>
                       """
                    : "";
                return $"""
                    <tr>
                      <td>{sp.Nickname ?? "(not yet set)"}</td>
                      <td>{status}</td>
                      <td>{resend}</td>
                    </tr>
                    """;
            }));

            var bothJoined = sps.All(sp => sp.JoinedAt.HasValue);
            var statusMessage = bothJoined
                ? """<div class="alert alert-success">Both players have joined — the game has started!</div>"""
                : """<div class="alert alert-info" id="lobby-status">Waiting for players to join…</div>""";

            var sseConnect = !bothJoined
                ? $"""<div hx-ext="sse" sse-connect="/api/sse/lobby/{sessionId}" hidden></div>"""
                : "";

            var body = $"""
                {HtmlLayout.NavBar()}
                <main class="container">
                  <div class="card">
                    <h1 class="page-title">Game lobby</h1>
                    {statusMessage}
                    {sseConnect}
                    <table class="table">
                      <thead><tr><th>Player</th><th>Status</th><th></th></tr></thead>
                      <tbody id="player-rows">{playerRows}</tbody>
                    </table>
                    {(bothJoined ? $"""<a href="/results/{sessionId}" class="btn btn-secondary">View results later</a>""" : "")}
                  </div>
                </main>
                """;
            return HtmlLayout.Page("Lobby", body);
        });

        // GET /join/{token} — player lands here from invite email
        app.MapGet("/join/{token}", async (
            string token,
            HttpContext ctx,
            SessionService sessions,
            QuestionService questionService,
            AuthService auth) =>
        {
            var sp = await sessions.GetSessionPlayerByTokenAsync(token);
            if (sp == null) return Results.NotFound();

            var session = await sessions.GetByIdAsync(sp.SessionId);
            if (session == null || session.Status == "cancelled")
            {
                return HtmlLayout.Page("Game not available", $"""
                    {HtmlLayout.NavBar()}
                    <main class="container">
                      <div class="card">
                        <h1 class="page-title">Game not available</h1>
                        <p>This game link is no longer valid.</p>
                      </div>
                    </main>
                    """);
            }

            // Must be authenticated
            var player = await auth.GetPlayerFromCookieAsync(ctx);
            if (player == null)
            {
                var returnUrl = Uri.EscapeDataString($"/join/{token}");
                return Results.Redirect($"/auth/otp?returnUrl={returnUrl}");
            }

            if (sp.JoinedAt.HasValue)
                return Results.Redirect($"/play/{token}");

            var categories = (await questionService.GetActiveCategoriesAsync()).ToList();
            var presets = (await questionService.GetActivePresetsAsync()).ToList();
            var presetCategories = (await questionService.GetPresetCategoriesAsync()).ToList();

            var presetTiles = string.Join("", presets.Select(p =>
            {
                var catIds = string.Join(",", presetCategories
                    .Where(pc => pc.PresetId == p.Id)
                    .Select(pc => pc.CategoryId));
                return $"""
                    <button type="button" class="preset-tile" data-cat-ids="{catIds}"
                            onclick="selectPreset(this)">
                      {p.Name}
                      {(p.Description != null ? $"<span class='preset-desc'>{p.Description}</span>" : "")}
                    </button>
                    """;
            }));

            var categoryCheckboxes = string.Join("", categories.Select(c => $"""
                <label class="checkbox-label">
                  <input type="checkbox" name="categories" value="{c.Id}" id="cat-{c.Id}" />
                  {c.Name}
                </label>
                """));

            var body = $"""
                {HtmlLayout.NavBar()}
                <main class="container">
                  <div class="card">
                    <h1 class="page-title">Join the game</h1>
                    <form method="post" action="/join/{token}" class="form-stack">
                      <div class="form-group">
                        <label for="nickname">Your nickname for this game</label>
                        <input id="nickname" type="text" name="nickname" required class="input"
                               placeholder="e.g. Alex" maxlength="30" autofocus />
                      </div>

                      <div class="form-group">
                        <label>Choose question categories</label>
                        <p class="text-muted text-sm">Select a preset to get started, then adjust individual categories as you like.</p>
                        <div class="preset-tiles">{presetTiles}</div>
                        <div class="category-checkboxes">{categoryCheckboxes}</div>
                      </div>

                      <button type="submit" class="btn btn-primary btn-block">Join game</button>
                    </form>
                  </div>
                </main>
                """;
            return HtmlLayout.Page("Join game", body);
        });

        // POST /join/{token}
        app.MapPost("/join/{token}", async (
            string token,
            HttpContext ctx,
            SessionService sessions,
            QuestionService questionService,
            AuthService auth,
            GameOptions options) =>
        {
            var player = await auth.GetPlayerFromCookieAsync(ctx);
            if (player == null) return Results.Redirect($"/auth/otp?returnUrl=/join/{token}");

            var sp = await sessions.GetSessionPlayerByTokenAsync(token);
            if (sp == null) return Results.NotFound();

            var form = await ctx.Request.ReadFormAsync();
            var nickname = form["nickname"].ToString().Trim();
            var categoryIds = form["categories"].Select(c => int.Parse(c!)).ToList();

            if (string.IsNullOrEmpty(nickname))
                return Results.BadRequest("Nickname is required.");

            await sessions.PlayerJoinAsync(
                token, nickname, categoryIds,
                options.CategoryWeightOneVote, options.CategoryWeightBothVotes);

            return Results.Redirect($"/play/{token}");
        }).DisableAntiforgery();

        // GET /play/{token} — main game page
        app.MapGet("/play/{token}", async (
            string token,
            HttpContext ctx,
            SessionService sessions,
            RoundService rounds,
            QuestionService questionService,
            PlayerService players,
            AuthService auth) =>
        {
            var player = await auth.GetPlayerFromCookieAsync(ctx);
            if (player == null) return Results.Redirect($"/auth/otp?returnUrl=/play/{token}");

            var sp = await sessions.GetSessionPlayerByTokenAsync(token);
            if (sp == null || sp.PlayerId != player.Id) return Results.Forbid();

            var session = await sessions.GetByIdAsync(sp.SessionId);
            if (session == null) return Results.NotFound();

            if (session.Status == "pending_join")
            {
                return HtmlLayout.Page("Waiting", $"""
                    {HtmlLayout.NavBar(sp.Nickname)}
                    <main class="container">
                      <div class="card text-center">
                        <h1 class="page-title">Waiting for the other player…</h1>
                        <div class="spinner"></div>
                        <div hx-ext="sse" sse-connect="/api/sse/{token}" sse-swap="round_advanced" hx-target="body"></div>
                      </div>
                    </main>
                    """);
            }

            if (session.Status is "finished" or "cancelled")
                return Results.Redirect($"/results/{sp.SessionId}");

            if (session.Status == "paused")
            {
                return HtmlLayout.Page("Paused", $"""
                    {HtmlLayout.NavBar(sp.Nickname)}
                    <main class="container">
                      <div class="card text-center paused-notice">
                        <h1>We're having a technical issue</h1>
                        <p>Hang tight — the game will resume shortly.</p>
                        <div hx-ext="sse" sse-connect="/api/sse/{token}" sse-swap="round_advanced" hx-target="body"></div>
                      </div>
                    </main>
                    """);
            }

            // Active session — get or create round 1
            var round = await rounds.GetCurrentRoundAsync(sp.SessionId);
            if (round == null)
            {
                round = await rounds.CreateRoundAsync(sp.SessionId, 1);
            }

            var opponentId = session.Player1Id == player.Id ? session.Player2Id!.Value : session.Player1Id!.Value;
            var opponentSp = (await sessions.GetSessionPlayersAsync(sp.SessionId))
                .FirstOrDefault(s => s.PlayerId == opponentId);

            var content = await RenderPlayPage(token, sp, session, round, player, opponentSp,
                rounds, questionService, players);

            return HtmlLayout.Page($"Round {round.RoundNumber}", content);
        });

        // GET /results/{sessionId}
        app.MapGet("/results/{sessionId}", async (
            string sessionId,
            SessionService sessions,
            RoundService rounds,
            PlayerService players) =>
        {
            var session = await sessions.GetByIdAsync(sessionId);
            if (session == null) return Results.NotFound();

            var sps = (await sessions.GetSessionPlayersAsync(sessionId)).ToList();
            var p1 = session.Player1Id.HasValue ? await players.GetByIdAsync(session.Player1Id.Value) : null;
            var p2 = session.Player2Id.HasValue ? await players.GetByIdAsync(session.Player2Id.Value) : null;

            var sp1 = sps.FirstOrDefault(s => s.PlayerId == session.Player1Id);
            var sp2 = sps.FirstOrDefault(s => s.PlayerId == session.Player2Id);

            var score1 = session.Player1Id.HasValue
                ? await rounds.GetScoreAsync(sessionId, session.Player1Id.Value) : 0;
            var score2 = session.Player2Id.HasValue
                ? await rounds.GetScoreAsync(sessionId, session.Player2Id.Value) : 0;

            var allRounds = await GetRoundSummariesAsync(sessionId, rounds, questionService: null!);

            var body = $"""
                {HtmlLayout.NavBar()}
                <main class="container">
                  <div class="card">
                    <h1 class="page-title">Game Results</h1>
                    <div class="score-header">
                      <div class="score-player">
                        <span class="player-name">{sp1?.Nickname ?? "Player 1"}</span>
                        <span class="score-value">{score1}</span>
                      </div>
                      <div class="score-vs">vs</div>
                      <div class="score-player">
                        <span class="player-name">{sp2?.Nickname ?? "Player 2"}</span>
                        <span class="score-value">{score2}</span>
                      </div>
                    </div>
                    <div id="rounds-log">{allRounds}</div>
                  </div>
                </main>
                """;
            return HtmlLayout.Page("Results", body);
        });
    }

    private static async Task<string> RenderPlayPage(
        string token, SessionPlayer sp, Session session, Round round,
        Player player, SessionPlayer? opponentSp,
        RoundService rounds, QuestionService questionService, PlayerService players)
    {
        var rqs = (await rounds.GetRoundQuestionsAsync(round.Id)).ToList();
        var answers = (await rounds.GetAnswersForRoundAsync(round.Id)).ToList();
        var myAnswer = answers.FirstOrDefault(a => a.PlayerId == player.Id && rqs.Any(rq => rq.Id == a.RoundQuestionId));
        var hasAnswered = answers.Any(a => a.PlayerId == player.Id);

        if (round.Status == "answering")
        {
            if (hasAnswered)
            {
                // Waiting for opponent
                return $"""
                    {HtmlLayout.NavBar(sp.Nickname)}
                    <main class="container">
                      <div class="card text-center">
                        <h2>Round {round.RoundNumber}</h2>
                        <p class="text-muted">Waiting for {opponentSp?.Nickname ?? "your opponent"}…</p>
                        <div class="spinner"></div>
                        <div hx-ext="sse" sse-connect="/api/sse/{token}"
                             sse-swap="round_advanced" hx-target="body"></div>
                      </div>
                    </main>
                    """;
            }

            // Show answering form
            var questionForms = string.Join("", rqs.Select((rq, i) =>
            {
                // We need the question text — fetch inline or pass it in
                return $"""
                    <div class="question-card" data-rq-id="{rq.Id}">
                      <div class="question-header">
                        <span class="question-number">Q{i + 1}</span>
                        <button type="button" class="btn btn-sm btn-ghost feedback-btn"
                                data-rq-id="{rq.Id}" data-phase="answering"
                                onclick="openFeedback(this)">Give feedback</button>
                      </div>
                      <p class="question-text">Loading…</p>
                      <textarea name="answer_{rq.Id}" required class="input textarea"
                                placeholder="Your answer…" rows="3"></textarea>
                    </div>
                    """;
            }));

            return $"""
                {HtmlLayout.NavBar(sp.Nickname)}
                <main class="container">
                  <form method="post" action="/api/round/answer" class="form-stack">
                    <input type="hidden" name="token" value="{token}" />
                    <div class="round-header">
                      <h2>Round {round.RoundNumber}</h2>
                    </div>
                    {questionForms}
                    <div class="form-group">
                      <label class="checkbox-label">
                        <input type="checkbox" name="declare_final" value="1" />
                        This is my last round
                      </label>
                    </div>
                    <button type="submit" class="btn btn-primary btn-block">Submit answers</button>
                  </form>
                </main>
                """;
        }

        if (round.Status == "guessing")
        {
            var guesses = (await rounds.GetGuessesForRoundAsync(round.Id)).ToList();
            var hasGuessed = guesses.Any(g => g.GuessingPlayerId == player.Id);

            if (hasGuessed)
            {
                return $"""
                    {HtmlLayout.NavBar(sp.Nickname)}
                    <main class="container">
                      <div class="card text-center">
                        <h2>Round {round.RoundNumber}</h2>
                        <p class="text-muted">Waiting for {opponentSp?.Nickname ?? "your opponent"}…</p>
                        <div class="spinner"></div>
                        <div hx-ext="sse" sse-connect="/api/sse/{token}"
                             sse-swap="round_advanced" hx-target="body"></div>
                      </div>
                    </main>
                    """;
            }

            var decoys = (await rounds.GetDecoysForRoundAsync(round.Id))
                .Where(d => d.TargetPlayerId == player.Id).ToList();

            // Build guess UI
            var opponentId = session.Player1Id == player.Id
                ? session.Player2Id!.Value : session.Player1Id!.Value;

            var guessForms = string.Join("", rqs.Select((rq, i) =>
            {
                var opponentAnswer = answers.FirstOrDefault(a =>
                    a.RoundQuestionId == rq.Id && a.PlayerId == opponentId);
                var rqDecoys = decoys.Where(d => d.RoundQuestionId == rq.Id).ToList();

                // Build shuffled choices: 1 real answer + decoys
                var choices = new List<(string Text, string InputValue)>();
                if (opponentAnswer != null)
                    choices.Add((opponentAnswer.AnswerText, $"answer_{opponentAnswer.Id}"));
                foreach (var d in rqDecoys)
                    choices.Add((d.DecoyText, $"decoy_{d.Id}"));

                // Shuffle
                choices = choices.OrderBy(_ => Random.Shared.Next()).ToList();

                var choiceHtml = string.Join("", choices.Select(c => $"""
                    <label class="choice-label">
                      <input type="radio" name="choice_{rq.Id}" value="{c.InputValue}" required />
                      <span class="choice-text">{c.Text}</span>
                    </label>
                    """));

                return $"""
                    <div class="question-card" data-rq-id="{rq.Id}">
                      <div class="question-header">
                        <span class="question-number">Q{i + 1}</span>
                        <button type="button" class="btn btn-sm btn-ghost feedback-btn"
                                data-rq-id="{rq.Id}" data-phase="guessing"
                                onclick="openFeedback(this)">Give feedback</button>
                      </div>
                      <p class="question-text">Which answer belongs to {opponentSp?.Nickname ?? "your opponent"}?</p>
                      <div class="choices">{choiceHtml}</div>
                    </div>
                    """;
            }));

            return $"""
                {HtmlLayout.NavBar(sp.Nickname)}
                <main class="container">
                  <form method="post" action="/api/round/guess" class="form-stack">
                    <input type="hidden" name="token" value="{token}" />
                    <div class="round-header">
                      <h2>Round {round.RoundNumber} — Guess!</h2>
                    </div>
                    {guessForms}
                    <button type="submit" class="btn btn-primary btn-block">Submit guesses</button>
                  </form>
                </main>
                """;
        }

        if (round.Status == "complete")
        {
            // Show round results
            return $"""
                {HtmlLayout.NavBar(sp.Nickname)}
                <main class="container">
                  <div class="card">
                    <h2>Round {round.RoundNumber} — Results</h2>
                    <div hx-ext="sse" sse-connect="/api/sse/{token}"
                         sse-swap="round_advanced" hx-target="body"></div>
                    <p class="text-muted">Loading next round…</p>
                    <div class="spinner"></div>
                  </div>
                </main>
                """;
        }

        return $"""
            {HtmlLayout.NavBar(sp.Nickname)}
            <main class="container">
              <div class="card text-center">
                <p class="text-muted">Game over — <a href="/results/{sp.SessionId}">see results</a></p>
              </div>
            </main>
            """;
    }

    private static async Task<string> GetRoundSummariesAsync(string sessionId, RoundService rounds,
        QuestionService questionService)
    {
        // Placeholder — full implementation in step 10 (Round results view)
        return "<p class='text-muted'>Round history will appear here.</p>";
    }
}
