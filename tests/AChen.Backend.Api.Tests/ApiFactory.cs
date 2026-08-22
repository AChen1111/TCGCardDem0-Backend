using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;

namespace AChen.Backend.Api.Tests;

public sealed class ApiFactory : WebApplicationFactory<Program>
{
    private readonly string databasePath = Path.Combine(
        Path.GetTempPath(),
        $"achen-auth-{Guid.NewGuid():N}.db");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = $"Data Source={databasePath}",
                ["Auth:Issuer"] = "AChen.Backend.Tests",
                ["Auth:Audience"] = "AChen.Game.Tests",
                ["Auth:SigningKey"] = "integration-test-signing-key-32-characters-minimum"
            });
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing && File.Exists(databasePath))
        {
            SqliteConnection.ClearAllPools();
            File.Delete(databasePath);
        }
    }
}
