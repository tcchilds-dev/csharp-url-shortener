using Microsoft.Extensions.Diagnostics.HealthChecks;
using StackExchange.Redis;

namespace UrlShortener.Api.Data;

public class RedisHealthCheck(IConnectionMultiplexer redis) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            await redis.GetDatabase().PingAsync().WaitAsync(cancellationToken);

            return HealthCheckResult.Healthy();
        }
        catch (Exception e) when (e is RedisException or OperationCanceledException)
        {
            return new HealthCheckResult(
                context.Registration.FailureStatus,
                description: "Redis is unavaiable.",
                exception: e
            );
        }
    }
}
