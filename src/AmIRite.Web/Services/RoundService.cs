using AmIRite.Web.Data;
using AmIRite.Web.Models;
using Dapper;

namespace AmIRite.Web.Services;

public class RoundService(
    IDbConnectionFactory db,
    QuestionService questions,
    LlmService llm,
    NotificationService notifications,
    SessionService sessions,
    GameOptions options,
    ILogger<RoundService> logger)
{
    // -- Round creation --

    public async Task<Round> CreateRoundAsync(string sessionId, int roundNumber)
    {
        var session = await sessions.GetByIdAsync(sessionId)
            ?? throw new InvalidOperationException("Session not found");

        var selectedQuestions = await questions.SelectForRoundAsync(sessionId, session.QuestionsPerRound);
        if (selectedQuestions.Count == 0)
        {
            // Pool exhausted — end the game
            await sessions.SetStatusAsync(sessionId, "finished");
            throw new InvalidOperationException("Question pool exhausted");
        }

        using var conn = db.Create();
        using var tx = conn.BeginTransaction();

        var roundId = await conn.ExecuteScalarAsync<int>(
            """
            INSERT INTO rounds (session_id, round_number, status)
            VALUES (@sessionId, @roundNumber, 'answering')
            RETURNING id
            """,
            new { sessionId, roundNumber }, tx);

        for (var i = 0; i < selectedQuestions.Count; i++)
            await conn.ExecuteAsync(
                "INSERT INTO round_questions (round_id, question_id, sort_order) VALUES (@rid, @qid, @order)",
                new { rid = roundId, qid = selectedQuestions[i].Id, order = i }, tx);

        tx.Commit();

        return await GetRoundByIdAsync(roundId) ?? throw new InvalidOperationException("Round not created");
    }

    // -- Answer submission --

    public async Task<bool> SubmitAnswersAsync(
        int roundId, int playerId, Dictionary<int, string> answersByRoundQuestionId, bool declareFinal)
    {
        using var conn = db.Create();

        foreach (var (rqId, text) in answersByRoundQuestionId)
            await conn.ExecuteAsync(
                """
                INSERT INTO answers (round_question_id, player_id, answer_text)
                VALUES (@rqId, @playerId, @text)
                ON CONFLICT DO NOTHING
                """,
                new { rqId, playerId, text });

        if (declareFinal)
        {
            var round = await GetRoundByIdAsync(roundId);
            if (round != null)
                await conn.ExecuteAsync(
                    "UPDATE session_players SET final_round = @rn WHERE session_id = @sid AND player_id = @pid",
                    new { rn = round.RoundNumber, sid = round.SessionId, pid = playerId });
        }

        // Check if both players have answered
        var round2 = await GetRoundByIdAsync(roundId)
            ?? throw new InvalidOperationException("Round not found");

        var rqIds = await conn.QueryAsync<int>(
            "SELECT id FROM round_questions WHERE round_id = @rid", new { rid = roundId });

        var session = await sessions.GetByIdAsync(round2.SessionId)!
            ?? throw new InvalidOperationException("Session not found");

        var answerCount = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(DISTINCT player_id) FROM answers WHERE round_question_id IN @rqIds",
            new { rqIds });

        if (answerCount >= 2)
        {
            // Both players answered — trigger decoy generation
            await GenerateDecoysAsync(roundId, session);
        }

        return true;
    }

    private async Task GenerateDecoysAsync(int roundId, Session session)
    {
        using var conn = db.Create();

        var rqs = (await conn.QueryAsync<RoundQuestion>(
            "SELECT * FROM round_questions WHERE round_id = @rid ORDER BY sort_order", new { rid = roundId })).ToList();

        var playerIds = new[] { session.Player1Id!.Value, session.Player2Id!.Value };

        try
        {
            foreach (var rq in rqs)
            {
                var q = await questions.GetByIdAsync(rq.QuestionId)!
                    ?? throw new InvalidOperationException("Question not found");

                foreach (var targetPlayerId in playerIds)
                {
                    // Get the other player's answer (what target player will be guessing)
                    var otherPlayerId = playerIds.First(p => p != targetPlayerId);
                    var answer = await conn.QuerySingleOrDefaultAsync<Answer>(
                        "SELECT * FROM answers WHERE round_question_id = @rqId AND player_id = @pid",
                        new { rqId = rq.Id, pid = otherPlayerId });

                    if (answer == null) continue;

                    var decoyTexts = await RetryAsync(
                        () => llm.GenerateDecoysAsync(q.Text, answer.AnswerText, session.DecoyCount),
                        options.LlmRetryCount);

                    foreach (var text in decoyTexts)
                        await conn.ExecuteAsync(
                            "INSERT INTO decoys (round_question_id, target_player_id, decoy_text) VALUES (@rqId, @pid, @text)",
                            new { rqId = rq.Id, pid = targetPlayerId, text });
                }
            }

            // Advance to guessing phase
            await conn.ExecuteAsync(
                "UPDATE rounds SET status = 'guessing' WHERE id = @rid", new { rid = roundId });

            // Notify both players via SSE / FCM / email
            var sessionPlayers = (await sessions.GetSessionPlayersAsync(session.Id)).ToList();
            // Notifications are sent by the callers via NotificationService
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "LLM decoy generation failed for round {RoundId}", roundId);
            await sessions.SetStatusAsync(session.Id, "paused");
            // LlmRetryWorker will pick this up
        }
    }

    // -- Guess submission --

    public async Task<bool> SubmitGuessesAsync(
        int roundId, int guessingPlayerId,
        Dictionary<int, (int? AnswerId, int? DecoyId)> guessesByRoundQuestionId)
    {
        using var conn = db.Create();

        foreach (var (rqId, (answerId, decoyId)) in guessesByRoundQuestionId)
        {
            var isCorrect = answerId.HasValue;
            var points = isCorrect ? 1 : 0;

            await conn.ExecuteAsync(
                """
                INSERT INTO guesses (round_question_id, guessing_player_id, chosen_answer_id, chosen_decoy_id, is_correct, points_awarded)
                VALUES (@rqId, @pid, @aid, @did, @correct, @pts)
                ON CONFLICT DO NOTHING
                """,
                new { rqId, pid = guessingPlayerId, aid = answerId, did = decoyId,
                      correct = isCorrect, pts = points });
        }

        // Check if both players have guessed
        var round = await GetRoundByIdAsync(roundId)!;
        var rqIds = await conn.QueryAsync<int>(
            "SELECT id FROM round_questions WHERE round_id = @rid", new { rid = roundId });

        var guessCount = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(DISTINCT guessing_player_id) FROM guesses WHERE round_question_id IN @rqIds",
            new { rqIds });

        if (guessCount >= 2)
            await CompleteRoundAsync(roundId);

        return true;
    }

    private async Task CompleteRoundAsync(int roundId)
    {
        using var conn = db.Create();
        await conn.ExecuteAsync(
            "UPDATE rounds SET status = 'complete', completed_at = @now WHERE id = @id",
            new { now = DateTime.UtcNow, id = roundId });
    }

    // -- Queries --

    public async Task<Round?> GetRoundByIdAsync(int id)
    {
        using var conn = db.Create();
        return await conn.QuerySingleOrDefaultAsync<Round>(
            "SELECT * FROM rounds WHERE id = @id", new { id });
    }

    public async Task<Round?> GetCurrentRoundAsync(string sessionId)
    {
        using var conn = db.Create();
        return await conn.QuerySingleOrDefaultAsync<Round>(
            """
            SELECT * FROM rounds WHERE session_id = @sessionId
            ORDER BY round_number DESC LIMIT 1
            """,
            new { sessionId });
    }

    public async Task<IEnumerable<RoundQuestion>> GetRoundQuestionsAsync(int roundId)
    {
        using var conn = db.Create();
        return await conn.QueryAsync<RoundQuestion>(
            "SELECT * FROM round_questions WHERE round_id = @rid ORDER BY sort_order", new { rid = roundId });
    }

    public async Task<IEnumerable<Answer>> GetAnswersForRoundAsync(int roundId)
    {
        using var conn = db.Create();
        return await conn.QueryAsync<Answer>(
            """
            SELECT a.* FROM answers a
            JOIN round_questions rq ON rq.id = a.round_question_id
            WHERE rq.round_id = @rid
            """, new { rid = roundId });
    }

    public async Task<IEnumerable<Decoy>> GetDecoysForRoundAsync(int roundId)
    {
        using var conn = db.Create();
        return await conn.QueryAsync<Decoy>(
            """
            SELECT d.* FROM decoys d
            JOIN round_questions rq ON rq.id = d.round_question_id
            WHERE rq.round_id = @rid
            """, new { rid = roundId });
    }

    public async Task<IEnumerable<Guess>> GetGuessesForRoundAsync(int roundId)
    {
        using var conn = db.Create();
        return await conn.QueryAsync<Guess>(
            """
            SELECT g.* FROM guesses g
            JOIN round_questions rq ON rq.id = g.round_question_id
            WHERE rq.round_id = @rid
            """, new { rid = roundId });
    }

    public async Task<int> GetScoreAsync(string sessionId, int playerId)
    {
        using var conn = db.Create();
        return await conn.ExecuteScalarAsync<int>(
            """
            SELECT COALESCE(SUM(g.points_awarded), 0)
            FROM guesses g
            JOIN round_questions rq ON rq.id = g.round_question_id
            JOIN rounds r ON r.id = rq.round_id
            WHERE r.session_id = @sessionId AND g.guessing_player_id = @playerId
            """,
            new { sessionId, playerId });
    }

    public async Task<IEnumerable<Round>> GetPausedRoundsAsync()
    {
        using var conn = db.Create();
        return await conn.QueryAsync<Round>(
            """
            SELECT r.* FROM rounds r
            JOIN sessions s ON s.id = r.session_id
            WHERE s.status = 'paused' AND r.status = 'answering'
            ORDER BY r.started_at
            """);
    }

    // -- Retry helper --

    private static async Task<T> RetryAsync<T>(Func<Task<T>> action, int maxAttempts)
    {
        var delay = TimeSpan.FromSeconds(1);
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try { return await action(); }
            catch when (attempt < maxAttempts)
            {
                await Task.Delay(delay);
                delay *= 2; // exponential backoff
            }
        }
        return await action(); // final attempt — let exception propagate
    }
}
