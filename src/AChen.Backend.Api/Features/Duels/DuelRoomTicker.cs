namespace AChen.Backend.Api.Features.Duels;

/// <summary>只唤醒房间计时器；实际截止判定在房间串行执行域中完成。</summary>
public sealed class DuelRoomTicker(DuelRoomService rooms) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(250));
        while (await timer.WaitForNextTickAsync(stoppingToken)) rooms.Tick();
    }
}
