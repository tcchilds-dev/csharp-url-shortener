using Microsoft.EntityFrameworkCore;
using UrlShortener.Api.Data;

public class ClicksUpdateWorker : BackgroundService
{
    private readonly ClicksUpdateQueue _queue;
    private readonly ILogger<ClicksUpdateWorker> _logger;
    private readonly IServiceScopeFactory _scopedFactory;

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
        await foreach (var job in _queue.ReadAllAsync(stoppingToken))
        {
            try
            {
                using var scope = _scopedFactory.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<UrlShortenerContext>();
                var link = await dbContext.Links.SingleOrDefaultAsync(link =>
                    link.ShortCode == job.ShortCode
                );
                if (link is null)
                {
                    _logger.LogWarning("Link for code {ShortCode} not found.", job.ShortCode);
                    continue;
                }
                link.ClickCount++;
                await dbContext.SaveChangesAsync(stoppingToken);
            }
            catch (Exception e)
            {
                _logger.LogError(
                    e,
                    "Failed to process click update for link with code {ShortCode}",
                    job.ShortCode
                );
            }
        }
    }
}
