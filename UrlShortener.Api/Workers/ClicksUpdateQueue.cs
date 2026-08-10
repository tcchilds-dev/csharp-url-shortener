using System.Threading.Channels;

public record ClicksUpdateJob(string ShortCode);

public class ClicksUpdateQueue
{
    private readonly Channel<ClicksUpdateJob> _channel;

    public ClicksUpdateQueue()
    {
        _channel = Channel.CreateBounded<ClicksUpdateJob>(
            new BoundedChannelOptions(1000) { FullMode = BoundedChannelFullMode.Wait }
        );
    }

    public ValueTask EnqueueAsync(ClicksUpdateJob job, CancellationToken stoppingToken = default)
    {
        return _channel.Writer.WriteAsync(job, stoppingToken);
    }

    public IAsyncEnumerable<ClicksUpdateJob> ReadAllAsync(CancellationToken stoppingToken)
    {
        return _channel.Reader.ReadAllAsync(stoppingToken);
    }
}
