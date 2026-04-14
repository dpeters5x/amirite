using Microsoft.Data.Sqlite;
using System.Reflection;

namespace AmIRite.Web.Data;

public static class Database
{
    private const string MigrationsTable = "__migrations";

    public static void RunMigrations(string connectionString)
    {
        using var conn = new SqliteConnection(connectionString);
        conn.Open();
        EnablePragmas(conn);
        EnsureMigrationsTable(conn);

        var assembly = Assembly.GetExecutingAssembly();
        var scripts = assembly.GetManifestResourceNames()
            .Where(n => n.Contains(".Migrations.") && n.EndsWith(".sql"))
            .OrderBy(n => n)
            .ToList();

        foreach (var scriptName in scripts)
        {
            var applied = IsApplied(conn, scriptName);
            if (applied) continue;

            using var stream = assembly.GetManifestResourceStream(scriptName)!;
            using var reader = new StreamReader(stream);
            var sql = reader.ReadToEnd();

            Console.WriteLine($"[Migration] Applying {scriptName}");
            using var tx = conn.BeginTransaction();
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = sql;
            cmd.ExecuteNonQuery();

            using var insert = conn.CreateCommand();
            insert.Transaction = tx;
            insert.CommandText = $"INSERT INTO {MigrationsTable} (name, applied_at) VALUES (@name, @now)";
            insert.Parameters.AddWithValue("@name", scriptName);
            insert.Parameters.AddWithValue("@now", DateTime.UtcNow.ToString("o"));
            insert.ExecuteNonQuery();

            tx.Commit();
            Console.WriteLine($"[Migration] Applied {scriptName}");
        }
    }

    public static SqliteConnection Open(string connectionString)
    {
        var conn = new SqliteConnection(connectionString);
        conn.Open();
        EnablePragmas(conn);
        return conn;
    }

    private static void EnablePragmas(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA journal_mode=WAL; PRAGMA foreign_keys=ON;";
        cmd.ExecuteNonQuery();
    }

    private static void EnsureMigrationsTable(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            CREATE TABLE IF NOT EXISTS {MigrationsTable} (
                name       TEXT PRIMARY KEY,
                applied_at TEXT NOT NULL
            )
            """;
        cmd.ExecuteNonQuery();
    }

    private static bool IsApplied(SqliteConnection conn, string name)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT COUNT(*) FROM {MigrationsTable} WHERE name = @name";
        cmd.Parameters.AddWithValue("@name", name);
        return (long)(cmd.ExecuteScalar() ?? 0L) > 0;
    }
}
