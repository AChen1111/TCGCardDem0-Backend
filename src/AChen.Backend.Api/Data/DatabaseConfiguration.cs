using Microsoft.Data.Sqlite;

namespace AChen.Backend.Api.Data;

public static class DatabaseConfiguration
{
    public static string GetSqliteConnectionString(IConfiguration configuration, IHostEnvironment environment)
    {
        var configured = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("ConnectionStrings:Default is required.");
        var connection = new SqliteConnectionStringBuilder(configured);

        if (!Path.IsPathRooted(connection.DataSource))
        {
            connection.DataSource = Path.Combine(environment.ContentRootPath, connection.DataSource);
        }

        var directory = Path.GetDirectoryName(connection.DataSource);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        return connection.ToString();
    }
}
