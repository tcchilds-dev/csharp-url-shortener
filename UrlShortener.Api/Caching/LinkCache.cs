using Polly;
using Polly.CircuitBreaker;
using Polly.Registry;
using StackExchange.Redis;

namespace UrlShortener.Api.Caching;

public class LinkCache(
    IConnectionMultiplexer redis,
    ILogger<LinkCache> logger,
    ResiliencePipelineProvider<string> pipelineProvider
)
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromHours(48);
    private readonly ResiliencePipeline _pipeline = pipelineProvider.GetPipeline("redis-pipeline");

    public async Task<string?> GetAsync(string code)
    {
        try
        {
            var value = await _pipeline.ExecuteAsync(async cancellationToken =>
                await redis.GetDatabase().StringGetAsync(code)
            );

            return value.IsNull ? null : value.ToString();
        }
        catch (BrokenCircuitException)
        {
            return null;
        }
        catch (Exception e) when (e is RedisException || e is RedisTimeoutException)
        {
            logger.LogWarning(e, "Failed to read cached link for {ShortCode}", code);

            return null;
        }
    }

    public async Task SetAsync(string code, string originalUrl)
    {
        try
        {
            await _pipeline.ExecuteAsync(async cancellationToken =>
                await redis.GetDatabase().StringSetAsync(code, originalUrl, Lifetime)
            );
        }
        catch (BrokenCircuitException)
        {
            // Skip populating the cache while Redis is unavailable.
        }
        catch (Exception e) when (e is RedisException || e is RedisTimeoutException)
        {
            logger.LogWarning(e, "Failed to cache link for {ShortCode}", code);
        }
    }
}
