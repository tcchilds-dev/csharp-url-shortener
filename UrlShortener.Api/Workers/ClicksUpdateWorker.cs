using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using UrlShortener.Api.Data;

public class ClicksUpdateWorker : BackgroundService
{
    private readonly ClicksUpdateQueue _queue;
    private readonly ILogger<ClicksUpdateWorker> _logger;
    private readonly IServiceScopeFactory _scopedFactory;

    private readonly ConcurrentDictionary<string, int> _clicks = new();

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
        var accumulateTask = AccumulateClicksAsync();
        var flushTask = FlushPeriodicallyAsync(stoppingToken);

        try
        {
            await Task.WhenAll(accumulateTask, flushTask);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        finally
        {
            using var finalFlushTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));

            await FlushClicksAsync(finalFlushTimeout.Token);
        }
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        _queue.Complete();

        return base.StopAsync(cancellationToken);
    }

    private async Task AccumulateClicksAsync()
    {
        await foreach (var job in _queue.ReadAllAsync())
        {
            _clicks.AddOrUpdate(job.ShortCode, 1, (_, clickCount) => clickCount + 1);
        }
    }

    private async Task FlushPeriodicallyAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await FlushClicksAsync(stoppingToken);
        }
    }

    private async Task FlushClicksAsync(CancellationToken stoppingToken)
    {
        var batch = new Dictionary<string, int>();

        foreach (var shortCode in _clicks.Keys)
        {
            if (_clicks.TryRemove(shortCode, out var clickCount))
            {
                batch[shortCode] = clickCount;
            }
        }

        if (batch.Count == 0)
        {
            return;
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

            _logger.LogInformation(
                "Flushed {UrlCount} URL counters containing {ClickCount} clicks",
                batch.Count,
                batch.Values.Sum()
            );
        }
        catch (Exception e)
        {
            // Put the counts back so we can retry on the next flush.
            foreach (var (shortCode, clickCount) in batch)
            {
                _clicks.AddOrUpdate(
                    shortCode,
                    clickCount,
                    (_, existingCount) => existingCount + clickCount
                );
            }

            _logger.LogError(e, "Failed to flush click counts");
        }
    }
}
