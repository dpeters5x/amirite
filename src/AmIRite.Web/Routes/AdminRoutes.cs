using AmIRite.Web.Data;
using AmIRite.Web.Models;
using AmIRite.Web.Services;
using Dapper;

namespace AmIRite.Web.Routes;

public static class AdminRoutes
{
    public static void MapAdminRoutes(this WebApplication app)
    {
        // Middleware-style auth check applied to each admin endpoint
        static IResult? RequireAdmin(HttpContext ctx, AdminOptions admin, AuthService auth)
        {
            if (!auth.ValidateAdminCredentials(ctx, admin))
            {
                ctx.Response.Headers["WWW-Authenticate"] = "Basic realm=\"AmIRite Admin\"";
                return Results.StatusCode(401);
            }
            return null;
        }

        // GET /admin — dashboard
        app.MapGet("/admin", async (
            HttpContext ctx, AdminOptions admin, AuthService auth,
            SessionService sessions, IDbConnectionFactory db) =>
        {
            if (RequireAdmin(ctx, admin, auth) is { } r) return r;

            var activeSessions = (await sessions.GetActiveAsync()).ToList();
            using var conn = db.Create();
            var flagCount = await conn.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM question_feedback WHERE reviewed_at IS NULL");

            var body = $"""
                {AdminNav("dashboard")}
                <main class="container">
                  <h1 class="page-title">Admin Dashboard</h1>
                  <div class="stat-grid">
                    <div class="stat-card">
                      <span class="stat-label">Active games</span>
                      <span class="stat-value">{activeSessions.Count}</span>
                    </div>
                    <div class="stat-card">
                      <span class="stat-label">Pending feedback</span>
                      <span class="stat-value">{flagCount}</span>
                      {(flagCount > 0 ? """<a href="/admin/questions/flagged" class="btn btn-sm btn-warning">Review</a>""" : "")}
                    </div>
                  </div>
                  <h2>Active games</h2>
                  {await GameListHtmlAsync(activeSessions, sessions)}
                </main>
                """;
            return HtmlLayout.Page("Admin", body);
        });

        // GET /admin/questions — list questions
        app.MapGet("/admin/questions", async (
            HttpContext ctx, AdminOptions admin, AuthService auth,
            QuestionService questions, IDbConnectionFactory db) =>
        {
            if (RequireAdmin(ctx, admin, auth) is { } r) return r;

            var allQuestions = (await questions.GetAllAsync()).ToList();
            using var conn = db.Create();

            var rows = string.Join("", await Task.WhenAll(allQuestions.Select(async q =>
            {
                var catIds = await questions.GetCategoryIdsForQuestionAsync(q.Id);
                var cats = await conn.QueryAsync<Category>(
                    $"SELECT * FROM categories WHERE id IN ({string.Join(",", catIds.Append(-1))})");
                var catNames = string.Join(", ", cats.Select(c => c.Name));
                var flagCount = await conn.ExecuteScalarAsync<int>(
                    "SELECT COUNT(*) FROM question_feedback WHERE question_id = @id AND reviewed_at IS NULL", new { id = q.Id });
                return $"""
                    <tr>
                      <td>{q.Id}</td>
                      <td><a href="/admin/questions/{q.Id}">{q.Text}</a></td>
                      <td>{catNames}</td>
                      <td>{(q.Active ? "Active" : "Inactive")}</td>
                      <td>{(flagCount > 0 ? $"<span class='badge badge-warning'>{flagCount}</span>" : "0")}</td>
                      <td>
                        <a href="/admin/questions/{q.Id}" class="btn btn-sm btn-secondary">Edit</a>
                      </td>
                    </tr>
                    """;
            })));

            var body = $"""
                {AdminNav("questions")}
                <main class="container">
                  <div class="page-header">
                    <h1 class="page-title">Questions</h1>
                    <a href="/admin/questions/new" class="btn btn-primary">Add question</a>
                  </div>
                  <table class="table">
                    <thead><tr><th>ID</th><th>Text</th><th>Categories</th><th>Status</th><th>Flags</th><th></th></tr></thead>
                    <tbody>{rows}</tbody>
                  </table>
                </main>
                """;
            return HtmlLayout.Page("Questions — Admin", body);
        });

        // GET /admin/questions/flagged — feedback review queue
        app.MapGet("/admin/questions/flagged", async (
            HttpContext ctx, AdminOptions admin, AuthService auth,
            IDbConnectionFactory db) =>
        {
            if (RequireAdmin(ctx, admin, auth) is { } r) return r;

            using var conn = db.Create();
            var feedback = await conn.QueryAsync(
                """
                SELECT qf.*, q.text as question_text
                FROM question_feedback qf
                JOIN questions q ON q.id = qf.question_id
                WHERE qf.reviewed_at IS NULL
                ORDER BY
                  qf.flag_inappropriate DESC,
                  qf.flag_poor_decoys DESC,
                  qf.quality_rating ASC
                """);

            var rows = string.Join("", feedback.Select(f => $"""
                <tr>
                  <td>{f.question_text}</td>
                  <td>{(f.flag_inappropriate ? "Yes" : "")}</td>
                  <td>{(f.flag_duplicate ? "Yes" : "")}</td>
                  <td>{f.quality_rating ?? "—"}</td>
                  <td>{f.phase}</td>
                  <td>
                    <form method="post" action="/admin/questions/{f.question_id}/flag-resolution" style="display:inline">
                      <input type="hidden" name="feedback_id" value="{f.id}" />
                      <select name="resolution" class="input input-sm">
                        <option value="dismissed">Dismiss</option>
                        <option value="deactivated">Deactivate question</option>
                        <option value="noted">Note</option>
                      </select>
                      <button type="submit" class="btn btn-sm btn-primary">Resolve</button>
                    </form>
                  </td>
                </tr>
                """));

            var body = $"""
                {AdminNav("questions")}
                <main class="container">
                  <h1 class="page-title">Feedback Review Queue</h1>
                  <table class="table">
                    <thead><tr><th>Question</th><th>Inappropriate</th><th>Duplicate</th><th>Quality</th><th>Phase</th><th></th></tr></thead>
                    <tbody>{rows}</tbody>
                  </table>
                </main>
                """;
            return HtmlLayout.Page("Feedback — Admin", body);
        });

        // POST /admin/questions/{id}/flag-resolution
        app.MapPost("/admin/questions/{id}/flag-resolution", async (
            int id, HttpContext ctx, AdminOptions admin, AuthService auth,
            QuestionService questions, IDbConnectionFactory db) =>
        {
            if (RequireAdmin(ctx, admin, auth) is { } r) return r;

            var form = await ctx.Request.ReadFormAsync();
            var feedbackId = int.Parse(form["feedback_id"].ToString());
            var resolution = form["resolution"].ToString();

            using var conn = db.Create();
            await conn.ExecuteAsync(
                "UPDATE question_feedback SET reviewed_at = @now, reviewed_by = @by, resolution = @res WHERE id = @fid",
                new { now = DateTime.UtcNow, by = admin.Username, res = resolution, fid = feedbackId });

            if (resolution == "deactivated")
                await questions.SetActiveAsync(id, false);

            return Results.Redirect("/admin/questions/flagged");
        }).DisableAntiforgery();

        // GET /admin/questions/new
        app.MapGet("/admin/questions/new", async (
            HttpContext ctx, AdminOptions admin, AuthService auth,
            QuestionService questions) =>
        {
            if (RequireAdmin(ctx, admin, auth) is { } r) return r;
            var categories = await questions.GetAllCategoriesAsync();
            return HtmlLayout.Page("New Question — Admin",
                $"{AdminNav("questions")}<main class='container'><div class='card'><h1 class='page-title'>Add question</h1>{QuestionForm(null, "", categories, [])}</div></main>");
        });

        // GET /admin/questions/{id} — edit
        app.MapGet("/admin/questions/{id}", async (
            int id, HttpContext ctx, AdminOptions admin, AuthService auth,
            QuestionService questions) =>
        {
            if (RequireAdmin(ctx, admin, auth) is { } r) return r;
            var q = await questions.GetByIdAsync(id);
            if (q == null) return Results.NotFound();
            var categories = await questions.GetAllCategoriesAsync();
            var catIds = (await questions.GetCategoryIdsForQuestionAsync(id)).ToList();
            return HtmlLayout.Page($"Edit Question {id} — Admin",
                $"{AdminNav("questions")}<main class='container'><div class='card'><h1 class='page-title'>Edit question</h1>{QuestionForm(id, q.Text, categories, catIds)}</div></main>");
        });

        // POST /admin/questions — create
        app.MapPost("/admin/questions", async (
            HttpContext ctx, AdminOptions admin, AuthService auth,
            QuestionService questions) =>
        {
            if (RequireAdmin(ctx, admin, auth) is { } r) return r;
            var form = await ctx.Request.ReadFormAsync();
            var text = form["text"].ToString().Trim();
            var catIds = form["categories"].Select(c => int.Parse(c!)).ToList();
            await questions.CreateAsync(text, catIds);
            return Results.Redirect("/admin/questions");
        }).DisableAntiforgery();

        // POST /admin/questions/{id} — update
        app.MapPost("/admin/questions/{id}", async (
            int id, HttpContext ctx, AdminOptions admin, AuthService auth,
            QuestionService questions) =>
        {
            if (RequireAdmin(ctx, admin, auth) is { } r) return r;
            var form = await ctx.Request.ReadFormAsync();
            var text = form["text"].ToString().Trim();
            var catIds = form["categories"].Select(c => int.Parse(c!)).ToList();
            var active = form["active"].ToString() == "1";
            await questions.UpdateAsync(id, text, catIds);
            await questions.SetActiveAsync(id, active);
            return Results.Redirect("/admin/questions");
        }).DisableAntiforgery();

        // POST /admin/questions/import — CSV bulk import
        app.MapPost("/admin/questions/import", async (
            HttpContext ctx, AdminOptions admin, AuthService auth,
            QuestionService questions) =>
        {
            if (RequireAdmin(ctx, admin, auth) is { } r) return r;
            var form = await ctx.Request.ReadFormAsync();
            var file = form.Files["csv"];
            if (file == null) return Results.BadRequest("No file uploaded");

            using var reader = new System.IO.StreamReader(file.OpenReadStream());
            var rows = new List<(string, string[])>();
            string? line;
            var firstLine = true;
            while ((line = await reader.ReadLineAsync()) != null)
            {
                if (firstLine) { firstLine = false; continue; } // skip header
                var parts = line.Split(',', 2);
                if (parts.Length < 2) continue;
                var text = parts[0].Trim('"').Trim();
                var cats = parts[1].Trim('"').Split('|').Select(c => c.Trim()).ToArray();
                if (!string.IsNullOrEmpty(text))
                    rows.Add((text, cats));
            }
            await questions.BulkImportAsync(rows);
            return Results.Redirect("/admin/questions");
        }).DisableAntiforgery();

        // GET /admin/games — list non-archived
        app.MapGet("/admin/games", async (
            HttpContext ctx, AdminOptions admin, AuthService auth,
            SessionService sessions) =>
        {
            if (RequireAdmin(ctx, admin, auth) is { } r) return r;
            var allSessions = (await sessions.GetAllNonArchivedAsync()).ToList();
            var body = $"""
                {AdminNav("games")}
                <main class="container">
                  <div class="page-header">
                    <h1 class="page-title">Games</h1>
                    <a href="/admin/games/archived" class="btn btn-secondary">View archived</a>
                  </div>
                  {await GameListHtmlAsync(allSessions, sessions)}
                </main>
                """;
            return HtmlLayout.Page("Games — Admin", body);
        });

        // GET /admin/games/{sessionId}
        app.MapGet("/admin/games/{sessionId}", async (
            string sessionId, HttpContext ctx, AdminOptions admin, AuthService auth,
            SessionService sessions, PlayerService players, RoundService rounds) =>
        {
            if (RequireAdmin(ctx, admin, auth) is { } r) return r;
            var session = await sessions.GetByIdAsync(sessionId);
            if (session == null) return Results.NotFound();

            var sps = (await sessions.GetSessionPlayersAsync(sessionId)).ToList();
            var body = $"""
                {AdminNav("games")}
                <main class="container">
                  <h1 class="page-title">Game {sessionId[..8]}…</h1>
                  <p>Status: {session.Status}</p>
                  <div class="action-bar">
                    {(session.Status == "active" ? $"""
                      <form method="post" action="/admin/games/{sessionId}/end" style="display:inline">
                        <button class="btn btn-warning">Force end</button>
                      </form>
                    """ : "")}
                    {(session.Status == "finished" ? $"""
                      <form method="post" action="/admin/games/{sessionId}/archive" style="display:inline">
                        <button class="btn btn-secondary">Archive</button>
                      </form>
                    """ : "")}
                  </div>
                  <h2>Players</h2>
                  {string.Join("", sps.Select(sp =>
                      $"<p><a href='/play/{sp.Token}'>{sp.Nickname ?? sp.PlayerId.ToString()}</a> — {sp.Token[..8]}…</p>"))}
                </main>
                """;
            return HtmlLayout.Page($"Game — Admin", body);
        });

        // POST /admin/games/{sessionId}/end
        app.MapPost("/admin/games/{sessionId}/end", async (
            string sessionId, HttpContext ctx, AdminOptions admin, AuthService auth,
            SessionService sessions, EmailService email, PlayerService players) =>
        {
            if (RequireAdmin(ctx, admin, auth) is { } r) return r;
            await sessions.SetStatusAsync(sessionId, "finished");
            // LLM summary and final email would be triggered here (step 11)
            return Results.Redirect($"/admin/games/{sessionId}");
        }).DisableAntiforgery();

        // POST /admin/games/{sessionId}/archive
        app.MapPost("/admin/games/{sessionId}/archive", async (
            string sessionId, HttpContext ctx, AdminOptions admin, AuthService auth,
            IDbConnectionFactory db) =>
        {
            if (RequireAdmin(ctx, admin, auth) is { } r) return r;
            using var conn = db.Create();
            await conn.ExecuteAsync(
                "UPDATE sessions SET archived_at = @now, archived_by = @by WHERE id = @id",
                new { now = DateTime.UtcNow, by = admin.Username, id = sessionId });
            return Results.Redirect("/admin/games");
        }).DisableAntiforgery();

        // GET /admin/games/archived
        app.MapGet("/admin/games/archived", async (
            HttpContext ctx, AdminOptions admin, AuthService auth,
            IDbConnectionFactory db, SessionService sessions) =>
        {
            if (RequireAdmin(ctx, admin, auth) is { } r) return r;
            using var conn = db.Create();
            var archived = (await conn.QueryAsync<Session>(
                "SELECT * FROM sessions WHERE archived_at IS NOT NULL ORDER BY archived_at DESC")).ToList();
            var body = $"""
                {AdminNav("games")}
                <main class="container">
                  <div class="page-header">
                    <h1 class="page-title">Archived Games</h1>
                    <a href="/admin/games" class="btn btn-secondary">Active games</a>
                  </div>
                  {await GameListHtmlAsync(archived, sessions, showUnarchive: true)}
                </main>
                """;
            return HtmlLayout.Page("Archived Games — Admin", body);
        });

        // POST /admin/games/{sessionId}/unarchive
        app.MapPost("/admin/games/{sessionId}/unarchive", async (
            string sessionId, HttpContext ctx, AdminOptions admin, AuthService auth,
            IDbConnectionFactory db) =>
        {
            if (RequireAdmin(ctx, admin, auth) is { } r) return r;
            using var conn = db.Create();
            await conn.ExecuteAsync(
                "UPDATE sessions SET archived_at = NULL, archived_by = NULL WHERE id = @id",
                new { id = sessionId });
            return Results.Redirect("/admin/games");
        }).DisableAntiforgery();

        // GET /admin/players
        app.MapGet("/admin/players", async (
            HttpContext ctx, AdminOptions admin, AuthService auth,
            PlayerService players) =>
        {
            if (RequireAdmin(ctx, admin, auth) is { } r) return r;
            var all = (await players.GetAllAsync()).ToList();
            var rows = string.Join("", all.Select(p => $"""
                <tr>
                  <td>{p.Id}</td>
                  <td><a href="/admin/players/{p.Id}">{p.Email}</a></td>
                  <td>{p.Nickname ?? "—"}</td>
                  <td>{p.LastSeenAt?.ToString("yyyy-MM-dd") ?? "Never"}</td>
                </tr>
                """));
            var body = $"""
                {AdminNav("players")}
                <main class="container">
                  <h1 class="page-title">Players</h1>
                  <table class="table">
                    <thead><tr><th>ID</th><th>Email</th><th>Nickname</th><th>Last seen</th></tr></thead>
                    <tbody>{rows}</tbody>
                  </table>
                </main>
                """;
            return HtmlLayout.Page("Players — Admin", body);
        });

        // GET /admin/players/{id}
        app.MapGet("/admin/players/{id:int}", async (
            int id, HttpContext ctx, AdminOptions admin, AuthService auth,
            PlayerService players, AchievementService achievements) =>
        {
            if (RequireAdmin(ctx, admin, auth) is { } r) return r;
            var player = await players.GetByIdAsync(id);
            if (player == null) return Results.NotFound();
            var playerAchievements = (await achievements.GetPlayerAchievementsAsync(id)).ToList();
            var achHtml = string.Join("", playerAchievements.Select(pair => $"""
                <tr>
                  <td>{pair.Achievement.Name}</td>
                  <td>{pair.Award.AwardedAt:yyyy-MM-dd}</td>
                  <td>{pair.Award.AwardedBy}</td>
                  <td>{pair.Award.SessionId ?? "—"}</td>
                </tr>
                """));
            var body = $"""
                {AdminNav("players")}
                <main class="container">
                  <h1 class="page-title">{player.Email}</h1>
                  <p>Nickname: {player.Nickname ?? "—"}</p>
                  <p>FCM: {(player.FcmToken != null ? "Yes" : "No")}</p>
                  <h2>Achievements</h2>
                  <table class="table">
                    <thead><tr><th>Achievement</th><th>Awarded</th><th>By</th><th>Game</th></tr></thead>
                    <tbody>{achHtml}</tbody>
                  </table>
                </main>
                """;
            return HtmlLayout.Page($"Player {id} — Admin", body);
        });

        // GET /admin/broadcast
        app.MapGet("/admin/broadcast", async (
            HttpContext ctx, AdminOptions admin, AuthService auth,
            PlayerService players) =>
        {
            if (RequireAdmin(ctx, admin, auth) is { } r) return r;
            var all = (await players.GetAllAsync()).ToList();
            var playerRows = string.Join("", all.Select(p => $"""
                <label class="checkbox-label">
                  <input type="checkbox" name="player_ids" value="{p.Id}" class="recipient-check"
                         data-email="{p.Email}" data-nickname="{p.Nickname ?? ""}"
                         data-fcm="{(p.FcmToken != null ? "1" : "0")}" />
                  {p.Email} {(p.Nickname != null ? $"({p.Nickname})" : "")}
                </label>
                """));
            var body = $"""
                {AdminNav("broadcast")}
                <main class="container">
                  <h1 class="page-title">Send Broadcast</h1>
                  <form method="post" action="/admin/broadcast" class="form-stack">
                    <div class="form-group">
                      <label>Subject</label>
                      <input type="text" name="subject" required class="input" />
                    </div>
                    <div class="form-group">
                      <label>Body</label>
                      <textarea name="body" required class="input textarea" rows="6"></textarea>
                    </div>
                    <div class="form-group">
                      <label>Channel</label>
                      <select name="channel" class="input">
                        <option value="email">Email</option>
                        <option value="fcm">FCM Push</option>
                        <option value="both">Both</option>
                      </select>
                    </div>
                    <div class="form-group">
                      <label>Recipients</label>
                      <div class="bulk-controls">
                        <button type="button" onclick="selectAll()">Select all</button>
                        <button type="button" onclick="deselectAll()">Deselect all</button>
                      </div>
                      <div class="recipient-list">{playerRows}</div>
                    </div>
                    <button type="submit" class="btn btn-primary">Send broadcast</button>
                  </form>
                </main>
                """;
            return HtmlLayout.Page("Broadcast — Admin", body);
        });

        // POST /admin/broadcast
        app.MapPost("/admin/broadcast", async (
            HttpContext ctx, AdminOptions admin, AuthService auth,
            PlayerService players, EmailService email, FcmService fcm,
            IDbConnectionFactory db) =>
        {
            if (RequireAdmin(ctx, admin, auth) is { } r) return r;
            var form = await ctx.Request.ReadFormAsync();
            var subject = form["subject"].ToString();
            var bodyText = form["body"].ToString();
            var channel = form["channel"].ToString();
            var playerIds = form["player_ids"].Select(id => int.Parse(id!)).ToList();

            using var conn = db.Create();
            var broadcastId = await conn.ExecuteScalarAsync<int>(
                "INSERT INTO broadcasts (subject, body, channel, sent_by, recipient_count) VALUES (@s, @b, @c, @by, @rc) RETURNING id",
                new { s = subject, b = bodyText, c = channel, by = admin.Username, rc = playerIds.Count });

            foreach (var playerId in playerIds)
            {
                var player = await players.GetByIdAsync(playerId);
                if (player == null) continue;

                bool? emailSuccess = null, fcmSuccess = null;
                string? error = null;

                if (channel is "email" or "both")
                {
                    try { await email.SendAsync(player.Email, subject, $"<p>{bodyText}</p>"); emailSuccess = true; }
                    catch (Exception ex) { emailSuccess = false; error = ex.Message; }
                }
                if (channel is "fcm" or "both" && player.FcmToken != null)
                {
                    fcmSuccess = await fcm.SendAsync(player.FcmToken, subject, bodyText);
                }

                await conn.ExecuteAsync(
                    "INSERT INTO broadcast_recipients (broadcast_id, player_id, email, channel_used, fcm_success, email_success, error_message) VALUES (@bid, @pid, @email, @ch, @fs, @es, @err)",
                    new { bid = broadcastId, pid = playerId, email = player.Email, ch = channel, fs = fcmSuccess, es = emailSuccess, err = error });
            }

            return Results.Redirect("/admin/broadcast/history");
        }).DisableAntiforgery();

        // GET /admin/broadcast/history
        app.MapGet("/admin/broadcast/history", async (
            HttpContext ctx, AdminOptions admin, AuthService auth,
            IDbConnectionFactory db) =>
        {
            if (RequireAdmin(ctx, admin, auth) is { } r) return r;
            using var conn = db.Create();
            var broadcasts = await conn.QueryAsync<Broadcast>(
                "SELECT * FROM broadcasts ORDER BY sent_at DESC");
            var rows = string.Join("", broadcasts.Select(b => $"""
                <tr>
                  <td><a href="/admin/broadcast/{b.Id}">{b.Subject}</a></td>
                  <td>{b.Channel}</td>
                  <td>{b.RecipientCount}</td>
                  <td>{b.SentBy}</td>
                  <td>{b.SentAt:yyyy-MM-dd HH:mm}</td>
                </tr>
                """));
            var body = $"""
                {AdminNav("broadcast")}
                <main class="container">
                  <div class="page-header">
                    <h1 class="page-title">Broadcast History</h1>
                    <a href="/admin/broadcast" class="btn btn-primary">New broadcast</a>
                  </div>
                  <table class="table">
                    <thead><tr><th>Subject</th><th>Channel</th><th>Recipients</th><th>Sent by</th><th>Sent at</th></tr></thead>
                    <tbody>{rows}</tbody>
                  </table>
                </main>
                """;
            return HtmlLayout.Page("Broadcast History — Admin", body);
        });

        // GET /admin/broadcast/{id}
        app.MapGet("/admin/broadcast/{id:int}", async (
            int id, HttpContext ctx, AdminOptions admin, AuthService auth,
            IDbConnectionFactory db) =>
        {
            if (RequireAdmin(ctx, admin, auth) is { } r) return r;
            using var conn = db.Create();
            var broadcast = await conn.QuerySingleOrDefaultAsync<Broadcast>(
                "SELECT * FROM broadcasts WHERE id = @id", new { id });
            if (broadcast == null) return Results.NotFound();
            var recipients = await conn.QueryAsync<BroadcastRecipient>(
                "SELECT * FROM broadcast_recipients WHERE broadcast_id = @id", new { id });
            var rows = string.Join("", recipients.Select(r => $"""
                <tr>
                  <td>{r.Email}</td>
                  <td>{r.ChannelUsed}</td>
                  <td>{(r.EmailSuccess == true ? "✓" : r.EmailSuccess == false ? "✗" : "—")}</td>
                  <td>{(r.FcmSuccess == true ? "✓" : r.FcmSuccess == false ? "✗" : "—")}</td>
                  <td>{r.ErrorMessage ?? ""}</td>
                </tr>
                """));
            var body = $"""
                {AdminNav("broadcast")}
                <main class="container">
                  <h1 class="page-title">{broadcast.Subject}</h1>
                  <p class="text-muted">Sent {broadcast.SentAt:yyyy-MM-dd HH:mm} by {broadcast.SentBy} via {broadcast.Channel}</p>
                  <table class="table">
                    <thead><tr><th>Email</th><th>Channel</th><th>Email</th><th>FCM</th><th>Error</th></tr></thead>
                    <tbody>{rows}</tbody>
                  </table>
                </main>
                """;
            return HtmlLayout.Page($"Broadcast {id} — Admin", body);
        });

        // GET /admin/categories
        app.MapGet("/admin/categories", async (
            HttpContext ctx, AdminOptions admin, AuthService auth,
            QuestionService questions) =>
        {
            if (RequireAdmin(ctx, admin, auth) is { } r) return r;
            var cats = await questions.GetAllCategoriesAsync();
            var rows = string.Join("", cats.Select(c => $"""
                <tr>
                  <td>{c.Id}</td>
                  <td>{c.Name}</td>
                  <td>{(c.Active ? "Active" : "Inactive")}</td>
                  <td>
                    <form method="post" action="/admin/categories" style="display:inline">
                      <input type="hidden" name="id" value="{c.Id}" />
                      <input type="hidden" name="active" value="{(c.Active ? "0" : "1")}" />
                      <button class="btn btn-sm btn-secondary">{(c.Active ? "Deactivate" : "Activate")}</button>
                    </form>
                  </td>
                </tr>
                """));
            var body = $"""
                {AdminNav("categories")}
                <main class="container">
                  <div class="page-header">
                    <h1 class="page-title">Categories</h1>
                  </div>
                  <form method="post" action="/admin/categories" class="form-inline">
                    <input type="text" name="name" placeholder="New category name" class="input" required />
                    <button type="submit" class="btn btn-primary">Add</button>
                  </form>
                  <table class="table">
                    <thead><tr><th>ID</th><th>Name</th><th>Status</th><th></th></tr></thead>
                    <tbody>{rows}</tbody>
                  </table>
                </main>
                """;
            return HtmlLayout.Page("Categories — Admin", body);
        });

        // POST /admin/categories
        app.MapPost("/admin/categories", async (
            HttpContext ctx, AdminOptions admin, AuthService auth,
            IDbConnectionFactory db) =>
        {
            if (RequireAdmin(ctx, admin, auth) is { } r) return r;
            var form = await ctx.Request.ReadFormAsync();
            using var conn = db.Create();

            if (form.ContainsKey("id"))
            {
                // Toggle active
                var catId = int.Parse(form["id"].ToString());
                var active = form["active"].ToString() == "1";
                await conn.ExecuteAsync(
                    "UPDATE categories SET active = @active WHERE id = @id", new { active, id = catId });
            }
            else
            {
                // Create
                var name = form["name"].ToString().Trim();
                await conn.ExecuteAsync(
                    "INSERT INTO categories (name) VALUES (@name)", new { name });
            }
            return Results.Redirect("/admin/categories");
        }).DisableAntiforgery();
    }

    // -- Helpers --

    private static string AdminNav(string active) => $"""
        <nav class="navbar admin-nav">
          <a href="/admin" class="navbar-brand">AmIRite Admin</a>
          <div class="navbar-links">
            <a href="/admin/games" class="{(active == "games" ? "active" : "")}">Games</a>
            <a href="/admin/questions" class="{(active == "questions" ? "active" : "")}">Questions</a>
            <a href="/admin/categories" class="{(active == "categories" ? "active" : "")}">Categories</a>
            <a href="/admin/players" class="{(active == "players" ? "active" : "")}">Players</a>
            <a href="/admin/broadcast" class="{(active == "broadcast" ? "active" : "")}">Broadcast</a>
            <a href="/admin/broadcast/history">History</a>
          </div>
        </nav>
        """;

    private static async Task<string> GameListHtmlAsync(
        IEnumerable<Session> sessions, SessionService sessionService,
        bool showUnarchive = false)
    {
        var rows = new List<string>();
        foreach (var s in sessions)
        {
            var sps = (await sessionService.GetSessionPlayersAsync(s.Id)).ToList();
            var p1 = sps.ElementAtOrDefault(0);
            var p2 = sps.ElementAtOrDefault(1);
            var endBtn = s.Status == "active"
                ? $"<form method='post' action='/admin/games/{s.Id}/end' style='display:inline'><button class='btn btn-sm btn-warning'>End</button></form>"
                : "";
            var archiveBtn = showUnarchive
                ? $"<form method='post' action='/admin/games/{s.Id}/unarchive' style='display:inline'><button class='btn btn-sm btn-secondary'>Unarchive</button></form>"
                : s.Status == "finished"
                    ? $"<form method='post' action='/admin/games/{s.Id}/archive' style='display:inline'><button class='btn btn-sm btn-secondary'>Archive</button></form>"
                    : "";

            rows.Add($"""
                <tr>
                  <td><a href="/admin/games/{s.Id}">{s.Id[..8]}&#8230;</a></td>
                  <td>{p1?.Nickname ?? "?"} vs {p2?.Nickname ?? "?"}</td>
                  <td>{s.Status}</td>
                  <td>{s.CreatedAt:yyyy-MM-dd}</td>
                  <td>{endBtn}{archiveBtn}</td>
                </tr>
                """);
        }

        return $"""
            <table class="table">
              <thead><tr><th>ID</th><th>Players</th><th>Status</th><th>Created</th><th></th></tr></thead>
              <tbody>{string.Join("", rows)}</tbody>
            </table>
            """;
    }

    private static string QuestionForm(int? id, string text, IEnumerable<Category> categories,
        IEnumerable<int> selectedCatIds)
    {
        var action = id.HasValue ? $"/admin/questions/{id}" : "/admin/questions";
        var cats = categories.ToList();
        var selected = selectedCatIds.ToHashSet();
        var checkboxes = string.Join("", cats.Select(c => $"""
            <label class="checkbox-label">
              <input type="checkbox" name="categories" value="{c.Id}"
                     {(selected.Contains(c.Id) ? "checked" : "")} />
              {c.Name}
            </label>
            """));
        return $"""
            <form method="post" action="{action}" class="form-stack">
              <div class="form-group">
                <label>Question text</label>
                <textarea name="text" required class="input textarea" rows="3">{text}</textarea>
              </div>
              {(id.HasValue ? $"""
                <div class="form-group">
                  <label class="checkbox-label">
                    <input type="checkbox" name="active" value="1" checked />
                    Active
                  </label>
                </div>
              """ : "")}
              <div class="form-group">
                <label>Categories</label>
                <div class="category-checkboxes">{checkboxes}</div>
              </div>
              <button type="submit" class="btn btn-primary">Save</button>
              <a href="/admin/questions" class="btn btn-secondary">Cancel</a>
            </form>
            """;
    }
}
