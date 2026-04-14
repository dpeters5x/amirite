using AmIRite.Web.Data;
using AmIRite.Web.Models;
using Dapper;

namespace AmIRite.Web.Services;

public class PlayerService(IDbConnectionFactory db)
{
    public async Task<Player> GetOrCreateAsync(string email)
    {
        email = email.ToLowerInvariant().Trim();
        using var conn = db.Create();

        var existing = await conn.QuerySingleOrDefaultAsync<Player>(
            "SELECT * FROM players WHERE email = @email", new { email });

        if (existing != null) return existing;

        var id = await conn.ExecuteScalarAsync<int>(
            "INSERT INTO players (email) VALUES (@email) RETURNING id", new { email });

        return (await conn.QuerySingleAsync<Player>(
            "SELECT * FROM players WHERE id = @id", new { id }));
    }

    public async Task<Player?> GetByIdAsync(int id)
    {
        using var conn = db.Create();
        return await conn.QuerySingleOrDefaultAsync<Player>(
            "SELECT * FROM players WHERE id = @id", new { id });
    }

    public async Task<Player?> GetByEmailAsync(string email)
    {
        using var conn = db.Create();
        return await conn.QuerySingleOrDefaultAsync<Player>(
            "SELECT * FROM players WHERE email = @email", new { email = email.ToLowerInvariant() });
    }

    public async Task UpdateFcmTokenAsync(int playerId, string token)
    {
        using var conn = db.Create();
        await conn.ExecuteAsync(
            "UPDATE players SET fcm_token = @token WHERE id = @id",
            new { token, id = playerId });
    }

    public async Task<IEnumerable<Player>> GetAllAsync()
    {
        using var conn = db.Create();
        return await conn.QueryAsync<Player>("SELECT * FROM players ORDER BY created_at DESC");
    }
}
