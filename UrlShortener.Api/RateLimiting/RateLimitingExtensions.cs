using System.Threading.RateLimiting;

namespace UrlShortener.Api.RateLimiting;

public static class RateLimitingExtensions
{
    public const string PostLimiter = "post";

    public static void AddRateLimiters(this WebApplicationBuilder builder)
    {
        builder.Services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(_ =>
                RateLimitPartition.GetConcurrencyLimiter(
                    partitionKey: "global",
                    factory: _ => new ConcurrencyLimiterOptions
                    {
                        // NOTE: limits are tentative
                        PermitLimit = 1000,
                        QueueLimit = 0,
                    }
                )
            );

            options.AddPolicy(
                PostLimiter,
                httpContext =>
                    RateLimitPartition.GetFixedWindowLimiter(
                        partitionKey: httpContext.Connection.RemoteIpAddress?.ToString()
                            ?? "unknown",
                        factory: _ => new FixedWindowRateLimiterOptions
                        {
                            PermitLimit = 10,
                            Window = TimeSpan.FromMinutes(1),
                        }
                    )
            );
        });
    }
}
