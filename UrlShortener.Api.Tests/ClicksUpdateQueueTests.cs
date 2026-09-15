using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace UrlShortener.Api.Tests;

public class ClicksUpdateQueueTests
{
    [Fact]
    public void CapacityLimitsDistinctLinks_ButNotClicksForTrackedLinks()
    {
        var queue = CreateQueue();

        Assert.True(queue.TryEnqueue(new ClicksUpdateJob("Abc1234")));
        Assert.True(queue.TryEnqueue(new ClicksUpdateJob("abc1234")));

        FillRemainingSlots(queue);

        for (var index = 0; index < 10_000; index++)
        {
            Assert.True(queue.TryEnqueue(new ClicksUpdateJob("Abc1234")));
        }

        Assert.False(queue.TryEnqueue(new ClicksUpdateJob("New1234")));

        var snapshot = queue.Snapshot();
        Assert.Equal(10_000, snapshot.Count);
        Assert.Equal(10_001L, snapshot["Abc1234"]);
        Assert.Equal(1L, snapshot["abc1234"]);
    }

    [Fact]
    public void ConcurrentNewLinks_CannotExceedCapacity()
    {
        var queue = CreateQueue();
        var accepted = 0;

        Parallel.For(
            0,
            20_000,
            index =>
            {
                if (queue.TryEnqueue(new ClicksUpdateJob(index.ToString("D7"))))
                {
                    Interlocked.Increment(ref accepted);
                }
            }
        );

        Assert.Equal(10_000, accepted);
        Assert.Equal(10_000, queue.Snapshot().Count);
    }

    [Fact]
    public void StopAcceptingClicks_RejectsNewClicks_ButPreservesPendingClickCounts()
    {
        var queue = CreateQueue();
        queue.TryEnqueue(new ClicksUpdateJob("Abc1234"));
        queue.StopAcceptingClicks();

        Assert.False(queue.TryEnqueue(new ClicksUpdateJob("Abc1234")));
        Assert.False(queue.TryEnqueue(new ClicksUpdateJob("New1234")));
        Assert.Equal(1L, queue.Snapshot()["Abc1234"]);
    }

    private static void FillRemainingSlots(ClicksUpdateQueue queue)
    {
        for (var index = 0; index < 9_998; index++)
        {
            Assert.True(queue.TryEnqueue(new ClicksUpdateJob(index.ToString("D7"))));
        }
    }

    private static ClicksUpdateQueue CreateQueue() => new(NullLogger<ClicksUpdateQueue>.Instance);
}
