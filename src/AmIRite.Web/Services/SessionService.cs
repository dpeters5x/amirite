using AmIRite.Web.Data;
using AmIRite.Web.Models;
using Dapper;

namespace AmIRite.Web.Services;

public class SessionService(IDbConnectionFactory db, GameOptions options)
{
    public async Task<Session> CreateAsync(string organizerEmail, string email1, string email2)
    {
        var sessionId = Guid.NewGuid().ToString("N");
        var token1 = Guid.NewGuid().ToString("N");
        var token2 = Guid.NewGuid().ToString("N");
        var expiry = DateTime.UtcNow.AddDays(options.JoinTokenExpiryDays);

        using var conn = db.Create();
        using var tx = conn.BeginTransaction();

        await conn.ExecuteAsync(
            """
            INSERT INTO sessions (id, organizer_email, status, questions_per_round, decoy_count, join_expires_at)
            VALUES (@id, @email, 'pending_join', @qpr, @dc, @expiry)
            """,
            new { id = sessionId, email = organizerEmail.ToLowerInvariant(),
                  qpr = options.QuestionsPerRound, dc = options.DecoyCount, expiry },
            tx);

        // Create placeholder player records and session_player entries
        foreach (var (playerEmail, token) in new[] { (email1, token1), (email2, token2) })
        {
            var normalizedEmail = playerEmail.ToLowerInvariant().Trim();
            var playerId = await conn.ExecuteScalarAsync<int>(
                """
                INSERT INTO players (email) VALUES (@email)
                ON CONFLICT(email) DO UPDATE SET email = email
                RETURNING id
                """,
                new { email = normalizedEmail }, tx);

            await conn.ExecuteAsync(
                """
                INSERT INTO session_players (session_id, player_id, token)
                VALUES (@sessionId, @playerId, @token)
                """,
                new { sessionId, playerId, token }, tx);
        }

        tx.Commit();

        return await GetByIdAsync(sessionId) ?? throw new InvalidOperationException("Session not created");
    }

    public async Task<Session?> GetByIdAsync(string sessionId)
    {
        using var conn = db.Create();
        return await conn.QuerySingleOrDefaultAsync<Session>(
            "SELECT * FROM sessions WHERE id = @id", new { id = sessionId });
    }

    public async Task<SessionPlayer?> GetSessionPlayerByTokenAsync(string token)
    {
        using var conn = db.Create();
        return await conn.QuerySingleOrDefaultAsync<SessionPlayer>(
            "SELECT * FROM session_players WHERE token = @token", new { token });
    }

    public async Task<IEnumerable<SessionPlayer>> GetSessionPlayersAsync(string sessionId)
    {
        using var conn = db.Create();
        return await conn.QueryAsync<SessionPlayer>(
            "SELECT * FROM session_players WHERE session_id = @sessionId", new { sessionId });
    }

    public async Task<bool> PlayerJoinAsync(string token, string nickname, IEnumerable<int> categoryIds,
        double weightOneVote, double weightBothVotes)
    {
        using var conn = db.Create();
        var sp = await conn.QuerySingleOrDefaultAsync<SessionPlayer>(
            "SELECT * FROM session_players WHERE token = @token AND joined_at IS NULL", new { token });
        if (sp == null) return false;

        var session = await GetByIdAsync(sp.SessionId);
        if (session == null || session.Status != "pending_join") return false;

        using var tx = conn.BeginTransaction();

        await conn.ExecuteAsync(
            "UPDATE session_players SET nickname = @nickname, joined_at = @now WHERE token = @token",
            new { nickname, now = DateTime.UtcNow, token }, tx);

        // Determine which player slot this is
        var players = (await conn.QueryAsync<SessionPlayer>(
            "SELECT * FROM session_players WHERE session_id = @sid", new { sid = sp.SessionId }, tx)).ToList();

        var isPlayer1 = players.Count == 1 || players[0].PlayerId == sp.PlayerId;

        // Upsert category votes
        foreach (var catId in categoryIds)
        {
            if (isPlayer1)
                await conn.ExecuteAsync(
                    """
                    INSERT INTO session_categories (session_id, category_id, player1_vote)
                    VALUES (@sid, @cid, 1)
                    ON CONFLICT(session_id, category_id) DO UPDATE SET player1_vote = 1
                    """,
                    new { sid = sp.SessionId, cid = catId }, tx);
            else
                await conn.ExecuteAsync(
                    """
                    INSERT INTO session_categories (session_id, category_id, player2_vote)
                    VALUES (@sid, @cid, 1)
                    ON CONFLICT(session_id, category_id) DO UPDATE SET player2_vote = 1
                    """,
                    new { sid = sp.SessionId, cid = catId }, tx);
        }

        tx.Commit();

        // Check if both players have now joined
        var joined = await conn.QueryAsync<SessionPlayer>(
            "SELECT * FROM session_players WHERE session_id = @sid AND joined_at IS NOT NULL",
            new { sid = sp.SessionId });

        if (joined.Count() == 2)
            await ActivateSessionAsync(sp.SessionId, weightOneVote, weightBothVotes);

        return true;
    }

    private async Task ActivateSessionAsync(string sessionId, double weightOneVote, double weightBothVotes)
    {
        using var conn = db.Create();
        using var tx = conn.BeginTransaction();

        // Compute weights for all session_categories rows
        var cats = await conn.QueryAsync<SessionCategory>(
            "SELECT * FROM session_categories WHERE session_id = @sid", new { sid = sessionId }, tx);

        foreach (var cat in cats)
        {
            var weight = (cat.Player1Vote, cat.Player2Vote) switch
            {
                (true, true)  => weightBothVotes,
                (true, false) or (false, true) => weightOneVote,
                _ => 0.0
            };
            await conn.ExecuteAsync(
                "UPDATE session_categories SET weight = @w WHERE session_id = @sid AND category_id = @cid",
                new { w = weight, sid = sessionId, cid = cat.CategoryId }, tx);
        }

        // Get player IDs in join order
        var sps = (await conn.QueryAsync<SessionPlayer>(
            "SELECT * FROM session_players WHERE session_id = @sid ORDER BY joined_at", new { sid = sessionId }, tx))
            .ToList();

        await conn.ExecuteAsync(
            """
            UPDATE sessions SET status = 'active', player1_id = @p1, player2_id = @p2
            WHERE id = @sid
            """,
            new { p1 = sps[0].PlayerId, p2 = sps[1].PlayerId, sid = sessionId }, tx);

        tx.Commit();
    }

    public async Task SetStatusAsync(string sessionId, string status, string? endedReason = null)
    {
        using var conn = db.Create();
        if (status == "finished")
            await conn.ExecuteAsync(
                "UPDATE sessions SET status = @status, ended_at = @now WHERE id = @id",
                new { status, now = DateTime.UtcNow, id = sessionId });
        else
            await conn.ExecuteAsync(
                "UPDATE sessions SET status = @status WHERE id = @id",
                new { status, id = sessionId });
    }

    public async Task<IEnumerable<Session>> GetExpiredPendingAsync()
    {
        using var conn = db.Create();
        return await conn.QueryAsync<Session>(
            "SELECT * FROM sessions WHERE status = 'pending_join' AND join_expires_at < @now",
            new { now = DateTime.UtcNow });
    }

    public async Task<IEnumerable<Session>> GetActiveAsync()
    {
        using var conn = db.Create();
        return await conn.QueryAsync<Session>(
            "SELECT * FROM sessions WHERE status = 'active' ORDER BY created_at DESC");
    }

    public async Task<IEnumerable<Session>> GetAllNonArchivedAsync()
    {
        using var conn = db.Create();
        return await conn.QueryAsync<Session>(
            "SELECT * FROM sessions WHERE archived_at IS NULL ORDER BY created_at DESC");
    }
}
