using StackExchange.Redis;

namespace UrlShortener.Api.Caching;

public class LinkCache(IConnectionMultiplexer redis, ILogger<LinkCache> logger)
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromHours(48);

    public async Task<string?> GetAsync(string code)
    {
        try
        {
            var value = await redis.GetDatabase().StringGetAsync(code);

            return value.IsNull ? null : value.ToString();
        }
        catch (RedisException exception)
        {
            logger.LogWarning(exception, "Failed to read cached link for {ShortCode}", code);

            return null;
        }
    }

    public async Task SetAsync(string code, string originalUrl)
    {
        try
        {
            await redis.GetDatabase().StringSetAsync(code, originalUrl, Lifetime);
        }
        catch (RedisException exception)
        {
            logger.LogWarning(exception, "Failed to cache link for {ShortCode}", code);
        }
    }
}
