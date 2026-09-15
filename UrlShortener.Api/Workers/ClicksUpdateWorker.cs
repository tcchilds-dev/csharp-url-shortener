using Microsoft.EntityFrameworkCore;
using UrlShortener.Api.Data;

public class ClicksUpdateWorker : BackgroundService
{
    private readonly ClicksUpdateQueue _queue;
    private readonly ILogger<ClicksUpdateWorker> _logger;
    private readonly IServiceScopeFactory _scopedFactory;

    // Shutdown can be requested more than once. Serializing the entire snapshot,
    // write, and acknowledgement stops duplication.
    private readonly SemaphoreSlim _flushGate = new(1, 1);

    public ClicksUpdateWorker(
        ClicksUpdateQueue queue,
        ILogger<ClicksUpdateWorker> logger,
        IServiceScopeFactory scopeFactory
    )
    {
        _queue = queue;
        _logger = logger;
        _scopedFactory = scopeFactory;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await FlushClicksAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        // Stop new increments first, then wait for any normal flush to finish
        // before starting the final flush. Snapshots must never overlap.
        _queue.Complete();

        await base.StopAsync(cancellationToken);

        if (ExecuteTask is { IsCompleted: false } || cancellationToken.IsCancellationRequested)
        {
            return;
        }

        using var finalFlushTimeout = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken
        );
        finalFlushTimeout.CancelAfter(TimeSpan.FromSeconds(10));

        try
        {
            while (!finalFlushTimeout.IsCancellationRequested)
            {
                if (await FlushClicksAsync(finalFlushTimeout.Token))
                {
                    break;
                }

                await Task.Delay(TimeSpan.FromSeconds(1), finalFlushTimeout.Token);
            }
        }
        catch (OperationCanceledException) when (finalFlushTimeout.IsCancellationRequested) { }
    }

    private async Task<bool> FlushClicksAsync(CancellationToken stoppingToken)
    {
        await _flushGate.WaitAsync(stoppingToken);
        try
        {
            return await PersistSnapshotAsync(stoppingToken);
        }
        finally
        {
            _flushGate.Release();
        }
    }

    private async Task<bool> PersistSnapshotAsync(CancellationToken stoppingToken)
    {
        var batch = _queue.Snapshot();
        if (batch.Count == 0)
        {
            return true;
        }

        try
        {
            using var scope = _scopedFactory.CreateScope();

            var dbContext = scope.ServiceProvider.GetRequiredService<UrlShortenerContext>();

            await using var transaction = await dbContext.Database.BeginTransactionAsync(
                stoppingToken
            );

            foreach (var (shortCode, clickCount) in batch)
            {
                var rowsUpdated = await dbContext
                    .Links.Where(link => link.ShortCode == shortCode)
                    .ExecuteUpdateAsync(
                        setters =>
                            setters.SetProperty(
                                link => link.ClickCount,
                                link => link.ClickCount + clickCount
                            ),
                        stoppingToken
                    );

                if (rowsUpdated == 0)
                {
                    _logger.LogError("Link for code {ShortCode} not found.", shortCode);
                }
            }

            await transaction.CommitAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to flush click counts");
            return false;
        }

        // The database work succeeded. Remove only persisted counts. Increments received while SQL
        // was busy stay available for the next tick.
        _queue.Acknowledge(batch);
        _logger.LogInformation("Flushed {UrlCount} URL counters", batch.Count);
        return true;
    }
}
