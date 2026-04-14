using Microsoft.Data.Sqlite;

namespace AmIRite.Web.Data;

public interface IDbConnectionFactory
{
    SqliteConnection Create();
}

public class DbConnectionFactory(string connectionString) : IDbConnectionFactory
{
    public SqliteConnection Create() => Database.Open(connectionString);
}
