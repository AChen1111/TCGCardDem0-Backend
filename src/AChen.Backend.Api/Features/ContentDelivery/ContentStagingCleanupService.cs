namespace AChen.Backend.Api.Features.ContentDelivery;

public sealed class ContentStagingCleanupService(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    ILogger<ContentStagingCleanupService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await CleanAsync(stoppingToken);
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1), timeProvider);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await CleanAsync(stoppingToken);
        }
    }

    private async Task CleanAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var storage = scope.ServiceProvider.GetRequiredService<IContentStorage>();
            var count = await storage.CleanStagingAsync(timeProvider.GetUtcNow().AddHours(-24), cancellationToken);
            if (count > 0)
            {
                logger.LogInformation("Removed {Count} abandoned content staging directories.", count);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Failed to clean abandoned content staging directories.");
        }
    }
}
