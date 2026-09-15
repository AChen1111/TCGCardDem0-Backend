using AChen.Backend.Api.Features.Gacha;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace AChen.Backend.Api.Tests;

public sealed class ApiFactory : WebApplicationFactory<Program>
{
    private readonly string databasePath = Path.Combine(
        Path.GetTempPath(),
        $"achen-auth-{Guid.NewGuid():N}.db");
    private readonly string contentPath = Path.Combine(
        Path.GetTempPath(),
        $"achen-content-{Guid.NewGuid():N}");

    public const string PublishKey = "integration-test-content-publish-key-32-characters";

    public IGachaRandom? GachaRandom { get; init; }

    public ApiFactory()
    {
    }


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
                ["Auth:SigningKey"] = "integration-test-signing-key-32-characters-minimum",
                ["ContentDelivery:StorageRoot"] = contentPath,
                ["ContentDelivery:PublishKey"] = PublishKey,
                ["ContentDelivery:MaxArchiveBytes"] = (10L * 1024 * 1024).ToString(),
                ["ContentDelivery:MaxExpandedBytes"] = (20L * 1024 * 1024).ToString()
            });
        });
        builder.ConfigureTestServices(services =>
        {
            if (GachaRandom is null)
            {
                return;
            }

            foreach (var descriptor in services.Where(value => value.ServiceType == typeof(IGachaRandom)).ToList())
            {
                services.Remove(descriptor);
            }

            services.AddSingleton(GachaRandom);
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

        if (disposing && Directory.Exists(contentPath))
        {
            DeleteTemporaryDirectory(contentPath);
        }

    }

    private static void DeleteTemporaryDirectory(string path)
    {
        var temporaryRoot = Path.GetFullPath(Path.GetTempPath());
        var target = Path.GetFullPath(path);
        if (!target.StartsWith(temporaryRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Test cleanup target must remain inside the temporary directory.");
        }

        foreach (var file in Directory.EnumerateFiles(target, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }

        Directory.Delete(target, recursive: true);
    }
}
