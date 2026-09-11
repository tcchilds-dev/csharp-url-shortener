using System.Threading.Channels;

public record ClicksUpdateJob(string ShortCode);

public class ClicksUpdateQueue(ILogger<ClicksUpdateQueue> logger)
{
    private readonly Channel<ClicksUpdateJob> _channel = Channel.CreateBounded<ClicksUpdateJob>(
        new BoundedChannelOptions(1000)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
        }
    );

    public bool TryEnqueue(ClicksUpdateJob job)
    {
        if (_channel.Writer.TryWrite(job))
        {
            return true;
        }

        logger.LogWarning(
            "Dropped click for {ShortCode}: click queue is full or stopping",
            job.ShortCode
        );

        return false;
    }

    public void Complete() => _channel.Writer.TryComplete();

    public IAsyncEnumerable<ClicksUpdateJob> ReadAllAsync()
    {
        return _channel.Reader.ReadAllAsync();
    }
}
