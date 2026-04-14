using AmIRite.Web.Data;
using AmIRite.Web.Models;
using Dapper;

namespace AmIRite.Web.Services;

public interface IAchievementEvaluator
{
    string Key { get; }
    Task<bool> IsEarnedAsync(int playerId, string sessionId, IDbConnectionFactory db);
}

public class AchievementService(IDbConnectionFactory db, IEnumerable<IAchievementEvaluator> evaluators)
{
    public async Task EvaluateAsync(int playerId, string sessionId)
    {
        using var conn = db.Create();
        var achievements = (await conn.QueryAsync<Achievement>(
            "SELECT * FROM achievements WHERE active = 1")).ToList();

        foreach (var evaluator in evaluators)
        {
            var achievement = achievements.FirstOrDefault(a => a.Key == evaluator.Key);
            if (achievement == null) continue;

            // Skip if already awarded
            var alreadyAwarded = await conn.ExecuteScalarAsync<int>(
                """
                SELECT COUNT(*) FROM player_achievements
                WHERE player_id = @pid AND achievement_id = @aid
                """,
                new { pid = playerId, aid = achievement.Id });
            if (alreadyAwarded > 0) continue;

            try
            {
                if (await evaluator.IsEarnedAsync(playerId, sessionId, db))
                    await conn.ExecuteAsync(
                        """
                        INSERT INTO player_achievements (player_id, achievement_id, awarded_by, session_id)
                        VALUES (@pid, @aid, 'system', @sid)
                        """,
                        new { pid = playerId, aid = achievement.Id, sid = sessionId });
            }
            catch { /* evaluator errors must not block game flow */ }
        }
    }

    public async Task<IEnumerable<(Achievement Achievement, PlayerAchievement Award)>>
        GetPlayerAchievementsAsync(int playerId)
    {
        using var conn = db.Create();
        var rows = await conn.QueryAsync(
            """
            SELECT a.id, a.key, a.name, a.description, a.icon, a.active, a.sort_order,
                   pa.id as pa_id, pa.player_id, pa.achievement_id, pa.awarded_at, pa.awarded_by, pa.session_id
            FROM player_achievements pa
            JOIN achievements a ON a.id = pa.achievement_id
            WHERE pa.player_id = @pid
            ORDER BY a.sort_order
            """,
            new { pid = playerId });

        return rows.Select(r => (
            new Achievement
            {
                Id = (int)r.id, Key = (string)r.key, Name = (string)r.name,
                Description = (string)r.description, Icon = (string)r.icon,
                Active = (bool)r.active, SortOrder = (int)r.sort_order
            },
            new PlayerAchievement
            {
                Id = (int)r.pa_id, PlayerId = (int)r.player_id,
                AchievementId = (int)r.achievement_id,
                AwardedAt = (DateTime)r.awarded_at, AwardedBy = (string)r.awarded_by,
                SessionId = (string?)r.session_id
            }
        ));
    }
}

// -- Evaluators --

public class FirstGameEvaluator(IDbConnectionFactory db) : IAchievementEvaluator
{
    public string Key => "first_game";
    public async Task<bool> IsEarnedAsync(int playerId, string sessionId, IDbConnectionFactory db)
    {
        using var conn = db.Create();
        var count = await conn.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*) FROM sessions s
            JOIN session_players sp ON sp.session_id = s.id
            WHERE sp.player_id = @pid AND s.status = 'finished'
            """, new { pid = playerId });
        return count >= 1;
    }
}

public class PerfectRoundEvaluator : IAchievementEvaluator
{
    public string Key => "perfect_round";
    public async Task<bool> IsEarnedAsync(int playerId, string sessionId, IDbConnectionFactory db)
    {
        using var conn = db.Create();
        // Check if any round in this session has all correct guesses for this player
        var rounds = await conn.QueryAsync<int>(
            """
            SELECT r.id FROM rounds r
            WHERE r.session_id = @sid AND r.status = 'complete'
            """, new { sid = sessionId });

        foreach (var roundId in rounds)
        {
            var rqCount = await conn.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM round_questions WHERE round_id = @rid", new { rid = roundId });
            if (rqCount == 0) continue;

            var correctCount = await conn.ExecuteScalarAsync<int>(
                """
                SELECT COUNT(*) FROM guesses g
                JOIN round_questions rq ON rq.id = g.round_question_id
                WHERE rq.round_id = @rid AND g.guessing_player_id = @pid AND g.is_correct = 1
                """, new { rid = roundId, pid = playerId });

            if (correctCount == rqCount) return true;
        }
        return false;
    }
}

public class PerfectGameEvaluator : IAchievementEvaluator
{
    public string Key => "perfect_game";
    public async Task<bool> IsEarnedAsync(int playerId, string sessionId, IDbConnectionFactory db)
    {
        using var conn = db.Create();
        var total = await conn.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*) FROM guesses g
            JOIN round_questions rq ON rq.id = g.round_question_id
            JOIN rounds r ON r.id = rq.round_id
            WHERE r.session_id = @sid AND g.guessing_player_id = @pid
            """, new { sid = sessionId, pid = playerId });
        if (total == 0) return false;

        var correct = await conn.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*) FROM guesses g
            JOIN round_questions rq ON rq.id = g.round_question_id
            JOIN rounds r ON r.id = rq.round_id
            WHERE r.session_id = @sid AND g.guessing_player_id = @pid AND g.is_correct = 1
            """, new { sid = sessionId, pid = playerId });

        return correct == total;
    }
}

public class TenGamesEvaluator : IAchievementEvaluator
{
    public string Key => "ten_games";
    public async Task<bool> IsEarnedAsync(int playerId, string sessionId, IDbConnectionFactory db)
    {
        using var conn = db.Create();
        var count = await conn.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*) FROM sessions s
            JOIN session_players sp ON sp.session_id = s.id
            WHERE sp.player_id = @pid AND s.status = 'finished'
            """, new { pid = playerId });
        return count >= 10;
    }
}

public class TwentyFiveGamesEvaluator : IAchievementEvaluator
{
    public string Key => "twenty_five_games";
    public async Task<bool> IsEarnedAsync(int playerId, string sessionId, IDbConnectionFactory db)
    {
        using var conn = db.Create();
        var count = await conn.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*) FROM sessions s
            JOIN session_players sp ON sp.session_id = s.id
            WHERE sp.player_id = @pid AND s.status = 'finished'
            """, new { pid = playerId });
        return count >= 25;
    }
}

public class SharpEyeEvaluator : IAchievementEvaluator
{
    public string Key => "sharp_eye";
    public async Task<bool> IsEarnedAsync(int playerId, string sessionId, IDbConnectionFactory db)
    {
        using var conn = db.Create();
        var total = await conn.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*) FROM guesses g
            JOIN round_questions rq ON rq.id = g.round_question_id
            JOIN rounds r ON r.id = rq.round_id
            WHERE r.session_id = @sid AND g.guessing_player_id = @pid
            """, new { sid = sessionId, pid = playerId });
        if (total == 0) return false;

        var correct = await conn.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*) FROM guesses g
            JOIN round_questions rq ON rq.id = g.round_question_id
            JOIN rounds r ON r.id = rq.round_id
            WHERE r.session_id = @sid AND g.guessing_player_id = @pid AND g.is_correct = 1
            """, new { sid = sessionId, pid = playerId });

        return (double)correct / total >= 0.8;
    }
}

public class FooledThemAllEvaluator : IAchievementEvaluator
{
    public string Key => "fooled_them_all";
    public async Task<bool> IsEarnedAsync(int playerId, string sessionId, IDbConnectionFactory db)
    {
        using var conn = db.Create();
        var rounds = await conn.QueryAsync<int>(
            "SELECT id FROM rounds WHERE session_id = @sid AND status = 'complete'", new { sid = sessionId });

        foreach (var roundId in rounds)
        {
            // Count how many times the opponent guessed incorrectly on THIS player's answers
            var rqCount = await conn.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM round_questions WHERE round_id = @rid", new { rid = roundId });
            if (rqCount == 0) continue;

            var opponentWrongCount = await conn.ExecuteScalarAsync<int>(
                """
                SELECT COUNT(*) FROM guesses g
                JOIN round_questions rq ON rq.id = g.round_question_id
                JOIN answers a ON a.round_question_id = rq.id AND a.player_id = @pid
                WHERE rq.round_id = @rid AND g.guessing_player_id != @pid AND g.is_correct = 0
                """, new { rid = roundId, pid = playerId });

            if (opponentWrongCount == rqCount) return true;
        }
        return false;
    }
}

public class MindReaderEvaluator : IAchievementEvaluator
{
    public string Key => "mind_reader";
    public async Task<bool> IsEarnedAsync(int playerId, string sessionId, IDbConnectionFactory db)
    {
        using var conn = db.Create();
        var rounds = (await conn.QueryAsync<int>(
            """
            SELECT id FROM rounds WHERE session_id = @sid AND status = 'complete'
            ORDER BY round_number
            """, new { sid = sessionId })).ToList();

        if (rounds.Count < 5) return false;

        var consecutiveCorrect = 0;
        foreach (var roundId in rounds)
        {
            var rqCount = await conn.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM round_questions WHERE round_id = @rid", new { rid = roundId });
            var correctCount = await conn.ExecuteScalarAsync<int>(
                """
                SELECT COUNT(*) FROM guesses g
                JOIN round_questions rq ON rq.id = g.round_question_id
                WHERE rq.round_id = @rid AND g.guessing_player_id = @pid AND g.is_correct = 1
                """, new { rid = roundId, pid = playerId });

            if (rqCount > 0 && correctCount == rqCount)
                consecutiveCorrect++;
            else
                consecutiveCorrect = 0;

            if (consecutiveCorrect >= 5) return true;
        }
        return false;
    }
}
