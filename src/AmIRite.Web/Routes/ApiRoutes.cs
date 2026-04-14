using AmIRite.Web.Data;
using AmIRite.Web.Models;
using AmIRite.Web.Services;
using AmIRite.Web.Workers;
using Dapper;

namespace AmIRite.Web.Routes;

public static class ApiRoutes
{
    public static void MapApiRoutes(this WebApplication app)
    {
        // POST /api/round/answer
        app.MapPost("/api/round/answer", async (
            HttpContext ctx,
            AuthService auth,
            SessionService sessions,
            RoundService rounds,
            NotificationService notifications,
            PlayerService players,
            AchievementWorker achievements,
            GameOptions options) =>
        {
            var player = await auth.GetPlayerFromCookieAsync(ctx);
            if (player == null) return Results.Unauthorized();

            var form = await ctx.Request.ReadFormAsync();
            var token = form["token"].ToString();
            var declareFinal = form["declare_final"].ToString() == "1";

            var sp = await sessions.GetSessionPlayerByTokenAsync(token);
            if (sp == null || sp.PlayerId != player.Id) return Results.Forbid();

            var session = await sessions.GetByIdAsync(sp.SessionId);
            if (session == null) return Results.NotFound();

            var round = await rounds.GetCurrentRoundAsync(sp.SessionId);
            if (round == null || round.Status != "answering") return Results.BadRequest("Not in answering phase.");

            var rqs = (await rounds.GetRoundQuestionsAsync(round.Id)).ToList();
            var answerMap = new Dictionary<int, string>();
            foreach (var rq in rqs)
            {
                var answer = form[$"answer_{rq.Id}"].ToString().Trim();
                if (!string.IsNullOrEmpty(answer))
                    answerMap[rq.Id] = answer;
            }

            if (answerMap.Count == 0) return Results.BadRequest("No answers provided.");

            await rounds.SubmitAnswersAsync(round.Id, player.Id, answerMap, declareFinal);

            // Refresh round to check if it advanced
            var refreshedRound = await rounds.GetCurrentRoundAsync(sp.SessionId);
            var baseUrl = $"{ctx.Request.Scheme}://{ctx.Request.Host}";

            // Notify opponent if round advanced to guessing
            if (refreshedRound?.Status == "guessing")
            {
                var opponentId = session.Player1Id == player.Id
                    ? session.Player2Id!.Value : session.Player1Id!.Value;
                var opponentSps = await sessions.GetSessionPlayersAsync(sp.SessionId);
                var opponentSp = opponentSps.FirstOrDefault(s => s.PlayerId == opponentId);
                var opponent = await players.GetByIdAsync(opponentId);

                if (opponentSp != null && opponent != null)
                    await notifications.NotifyRoundAdvancedAsync(opponentSp, opponent, "", baseUrl);
            }

            return Results.Redirect($"/play/{token}");
        }).DisableAntiforgery();

        // POST /api/round/guess
        app.MapPost("/api/round/guess", async (
            HttpContext ctx,
            AuthService auth,
            SessionService sessions,
            RoundService rounds,
            NotificationService notifications,
            PlayerService players,
            AchievementWorker achievements,
            GameOptions options) =>
        {
            var player = await auth.GetPlayerFromCookieAsync(ctx);
            if (player == null) return Results.Unauthorized();

            var form = await ctx.Request.ReadFormAsync();
            var token = form["token"].ToString();

            var sp = await sessions.GetSessionPlayerByTokenAsync(token);
            if (sp == null || sp.PlayerId != player.Id) return Results.Forbid();

            var session = await sessions.GetByIdAsync(sp.SessionId);
            if (session == null) return Results.NotFound();

            var round = await rounds.GetCurrentRoundAsync(sp.SessionId);
            if (round == null || round.Status != "guessing") return Results.BadRequest("Not in guessing phase.");

            var rqs = (await rounds.GetRoundQuestionsAsync(round.Id)).ToList();
            var guessMap = new Dictionary<int, (int? AnswerId, int? DecoyId)>();

            foreach (var rq in rqs)
            {
                var choice = form[$"choice_{rq.Id}"].ToString();
                if (string.IsNullOrEmpty(choice)) continue;

                if (choice.StartsWith("answer_") && int.TryParse(choice["answer_".Length..], out var aid))
                    guessMap[rq.Id] = (aid, null);
                else if (choice.StartsWith("decoy_") && int.TryParse(choice["decoy_".Length..], out var did))
                    guessMap[rq.Id] = (null, did);
            }

            await rounds.SubmitGuessesAsync(round.Id, player.Id, guessMap);

            // Queue achievement evaluation
            achievements.Enqueue(player.Id, sp.SessionId);

            // Refresh round
            var refreshedRound = await rounds.GetCurrentRoundAsync(sp.SessionId);
            var baseUrl = $"{ctx.Request.Scheme}://{ctx.Request.Host}";

            if (refreshedRound?.Status == "complete")
            {
                // Notify opponent
                var opponentId = session.Player1Id == player.Id
                    ? session.Player2Id!.Value : session.Player1Id!.Value;
                var opponentSps = await sessions.GetSessionPlayersAsync(sp.SessionId);
                var opponentSp = opponentSps.FirstOrDefault(s => s.PlayerId == opponentId);
                var opponent = await players.GetByIdAsync(opponentId);

                if (opponentSp != null && opponent != null)
                    await notifications.NotifyRoundAdvancedAsync(opponentSp, opponent, "", baseUrl);

                // Check if game should end (final round declared or pool exhausted)
                await CheckGameEndAsync(session, round, sp.SessionId, sessions, rounds, players, notifications, baseUrl);
            }

            return Results.Redirect($"/play/{token}");
        }).DisableAntiforgery();

        // POST /api/round/declare-final
        app.MapPost("/api/round/declare-final", async (
            HttpContext ctx,
            AuthService auth,
            SessionService sessions) =>
        {
            var player = await auth.GetPlayerFromCookieAsync(ctx);
            if (player == null) return Results.Unauthorized();

            var form = await ctx.Request.ReadFormAsync();
            var token = form["token"].ToString();
            var sp = await sessions.GetSessionPlayerByTokenAsync(token);
            if (sp == null || sp.PlayerId != player.Id) return Results.Forbid();

            // Declaration is handled during answer submission — this endpoint
            // exists for explicit out-of-band declarations if needed.
            return Results.Ok();
        }).DisableAntiforgery();

        // POST /api/question/feedback
        app.MapPost("/api/question/feedback", async (
            HttpContext ctx,
            AuthService auth,
            SessionService sessions,
            IDbConnectionFactory db) =>
        {
            var player = await auth.GetPlayerFromCookieAsync(ctx);
            if (player == null) return Results.Unauthorized();

            var form = await ctx.Request.ReadFormAsync();
            var token = form["token"].ToString();
            var sp = await sessions.GetSessionPlayerByTokenAsync(token);
            if (sp == null || sp.PlayerId != player.Id) return Results.Forbid();

            var rqId = int.TryParse(form["rq_id"], out var r) ? (int?)r : null;
            var questionId = int.Parse(form["question_id"].ToString());
            var phase = form["phase"].ToString();
            var flagInappropriate = form["flag_inappropriate"].ToString() == "1";
            var flagDuplicate = form["flag_duplicate"].ToString() == "1";
            var flagPoorDecoys = form["flag_poor_decoys"].ToString() == "1";
            int? qualityRating = int.TryParse(form["quality_rating"], out var qr) ? qr : null;
            var notes = form["notes"].ToString().Trim();

            // Only insert if at least one field is set
            if (!flagInappropriate && !flagDuplicate && !flagPoorDecoys
                && qualityRating == null && string.IsNullOrEmpty(notes))
                return Results.Ok(new { message = "No feedback submitted" });

            using var conn = db.Create();
            await conn.ExecuteAsync(
                """
                INSERT INTO question_feedback
                (question_id, player_id, session_id, round_question_id, phase,
                 flag_inappropriate, flag_duplicate, quality_rating, flag_poor_decoys, notes)
                VALUES (@qid, @pid, @sid, @rqId, @phase,
                        @fi, @fd, @qr, @fpd, @notes)
                """,
                new
                {
                    qid = questionId, pid = player.Id, sid = sp.SessionId, rqId,
                    phase, fi = flagInappropriate, fd = flagDuplicate,
                    qr = qualityRating, fpd = flagPoorDecoys,
                    notes = string.IsNullOrEmpty(notes) ? null : notes
                });

            return Results.Ok(new { message = "Feedback recorded" });
        }).DisableAntiforgery();

        // POST /api/fcm/register
        app.MapPost("/api/fcm/register", async (
            HttpContext ctx,
            AuthService auth,
            PlayerService players) =>
        {
            var player = await auth.GetPlayerFromCookieAsync(ctx);
            if (player == null) return Results.Unauthorized();

            var form = await ctx.Request.ReadFormAsync();
            var fcmToken = form["fcm_token"].ToString().Trim();
            if (string.IsNullOrEmpty(fcmToken)) return Results.BadRequest("fcm_token required");

            await players.UpdateFcmTokenAsync(player.Id, fcmToken);
            return Results.Ok();
        }).DisableAntiforgery();

        // POST /api/game/resend-invitation
        app.MapPost("/api/game/resend-invitation", async (
            HttpContext ctx,
            SessionService sessions,
            PlayerService players,
            EmailService email) =>
        {
            var form = await ctx.Request.ReadFormAsync();
            var token = form["token"].ToString();

            var sp = await sessions.GetSessionPlayerByTokenAsync(token);
            if (sp == null) return Results.NotFound();

            var session = await sessions.GetByIdAsync(sp.SessionId);
            if (session == null) return Results.NotFound();

            var player = await players.GetByIdAsync(sp.PlayerId);
            if (player == null) return Results.NotFound();

            var baseUrl = $"{ctx.Request.Scheme}://{ctx.Request.Host}";
            var joinUrl = $"{baseUrl}/join/{token}";
            await email.SendInvitationAsync(player.Email, joinUrl, session.OrganizerEmail);

            return Results.Redirect($"/lobby/{sp.SessionId}");
        }).DisableAntiforgery();

        // POST /api/game/rematch
        app.MapPost("/api/game/rematch", async (
            HttpContext ctx,
            AuthService auth,
            SessionService sessions,
            PlayerService players,
            EmailService email,
            GameOptions options) =>
        {
            var player = await auth.GetPlayerFromCookieAsync(ctx);
            if (player == null) return Results.Unauthorized();

            var form = await ctx.Request.ReadFormAsync();
            var sessionId = form["session_id"].ToString();

            var originalSession = await sessions.GetByIdAsync(sessionId);
            if (originalSession == null) return Results.NotFound();

            var sps = (await sessions.GetSessionPlayersAsync(sessionId)).ToList();
            var p1 = await players.GetByIdAsync(originalSession.Player1Id ?? 0);
            var p2 = await players.GetByIdAsync(originalSession.Player2Id ?? 0);

            if (p1 == null || p2 == null) return Results.BadRequest();

            var newSession = await sessions.CreateAsync(player.Email, p1.Email, p2.Email);
            var newSps = (await sessions.GetSessionPlayersAsync(newSession.Id)).ToList();
            var baseUrl = $"{ctx.Request.Scheme}://{ctx.Request.Host}";

            foreach (var sp in newSps)
            {
                var playerEmail = sp.PlayerId == (originalSession.Player1Id ?? 0) ? p1.Email : p2.Email;
                var joinUrl = $"{baseUrl}/join/{sp.Token}";
                await email.SendInvitationAsync(playerEmail, joinUrl, player.Email);
            }

            return Results.Redirect($"/lobby/{newSession.Id}");
        }).DisableAntiforgery();

        // POST /api/chat/send
        app.MapPost("/api/chat/send", async (
            HttpContext ctx,
            AuthService auth,
            SessionService sessions,
            SseService sse,
            RateLimiterService rateLimiter,
            AmIRite.Web.Data.IDbConnectionFactory db) =>
        {
            var player = await auth.GetPlayerFromCookieAsync(ctx);
            if (player == null) return Results.Unauthorized();

            var form = await ctx.Request.ReadFormAsync();
            var token = form["token"].ToString();
            var message = form["message"].ToString().Trim();

            var sp = await sessions.GetSessionPlayerByTokenAsync(token);
            if (sp == null || sp.PlayerId != player.Id) return Results.Forbid();

            if (!rateLimiter.IsAllowed($"chat:{token}", 10, TimeSpan.FromMinutes(1)))
                return Results.StatusCode(429);

            if (string.IsNullOrEmpty(message)) return Results.BadRequest();

            using var conn = db.Create();
            await conn.ExecuteAsync(
                "INSERT INTO chat_messages (session_id, sender_id, message_text) VALUES (@sid, @pid, @msg)",
                new { sid = sp.SessionId, pid = player.Id, msg = message });

            // Push chat update to all players in session via SSE
            var sessionPlayers = await sessions.GetSessionPlayersAsync(sp.SessionId);
            var chatHtml = $"""<div class="chat-message"><strong>{sp.Nickname}</strong> {message}</div>""";
            foreach (var s in sessionPlayers)
                await sse.SendEventAsync(s.Token, "chat_message", chatHtml);

            return Results.Ok();
        }).DisableAntiforgery();

        // POST /api/chat/read
        app.MapPost("/api/chat/read", async (
            HttpContext ctx,
            AuthService auth,
            SessionService sessions,
            AmIRite.Web.Data.IDbConnectionFactory db) =>
        {
            var player = await auth.GetPlayerFromCookieAsync(ctx);
            if (player == null) return Results.Unauthorized();

            var form = await ctx.Request.ReadFormAsync();
            var token = form["token"].ToString();
            var sp = await sessions.GetSessionPlayerByTokenAsync(token);
            if (sp == null || sp.PlayerId != player.Id) return Results.Forbid();

            using var conn = db.Create();
            await conn.ExecuteAsync(
                """
                UPDATE chat_messages SET read_at = @now
                WHERE session_id = @sid AND sender_id != @pid AND read_at IS NULL
                """,
                new { now = DateTime.UtcNow, sid = sp.SessionId, pid = player.Id });

            return Results.Ok();
        }).DisableAntiforgery();
    }

    private static async Task CheckGameEndAsync(
        Session session, Round round, string sessionId,
        SessionService sessions, RoundService rounds, PlayerService players,
        NotificationService notifications, string baseUrl)
    {
        var sps = (await sessions.GetSessionPlayersAsync(sessionId)).ToList();

        // Check if any player declared final round = this round
        var finalDeclared = sps.Any(sp =>
            sp.FinalRound.HasValue && sp.FinalRound.Value <= round.RoundNumber);

        if (!finalDeclared)
        {
            // Try to create next round (this will throw if pool exhausted, causing game end)
            try
            {
                await rounds.CreateRoundAsync(sessionId, round.RoundNumber + 1);
            }
            catch (InvalidOperationException)
            {
                // Pool exhausted — game ended by SessionService
            }
        }
        else
        {
            await sessions.SetStatusAsync(sessionId, "finished");
        }
    }
}
