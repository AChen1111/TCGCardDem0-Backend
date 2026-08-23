using System.Collections.Concurrent;

namespace AChen.Backend.Api.Features.ContentDelivery;

public sealed class ContentReleaseLockProvider
{
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> locks = new();

    public async ValueTask<IAsyncDisposable> AcquireAsync(Guid releaseId, CancellationToken cancellationToken)
    {
        var gate = locks.GetOrAdd(releaseId, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        return new Releaser(gate);
    }

    private sealed class Releaser(SemaphoreSlim gate) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            gate.Release();
            return ValueTask.CompletedTask;
        }
    }
}
