using Microsoft.EntityFrameworkCore;
using Polly;
using Polly.CircuitBreaker;
using StackExchange.Redis;
using UrlShortener.Api.Caching;

namespace UrlShortener.Api.Data;

public static class DataExtensions
{
    public static void MigrateDb(this WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<UrlShortenerContext>();

        dbContext.Database.Migrate();
    }

    public static void AddSqlDb(this WebApplicationBuilder builder)
    {
        builder.Services.AddDbContext<UrlShortenerContext>(options =>
            options.UseSqlServer(builder.Configuration.GetConnectionString("UrlShortener"))
        );
    }

    public static void AddRedisDb(this WebApplicationBuilder builder)
    {
        builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
        {
            var options = ConfigurationOptions.Parse(
                builder.Configuration.GetConnectionString("Redis")!
            );

            options.AbortOnConnectFail = false;
            options.AsyncTimeout = 250;
            options.BacklogPolicy = BacklogPolicy.FailFast;

            return ConnectionMultiplexer.Connect(options);
        });

        builder.Services.AddResiliencePipeline(
            "redis-pipeline",
            builder =>
            {
                var options = new CircuitBreakerStrategyOptions
                {
                    FailureRatio = 0.1,
                    MinimumThroughput = 2,
                    SamplingDuration = new TimeSpan(0, 0, 10),
                    BreakDuration = new TimeSpan(0, 0, 5),
                    ShouldHandle = new PredicateBuilder()
                        .Handle<RedisException>()
                        .Handle<RedisTimeoutException>(),
                };

                builder.AddCircuitBreaker(options);
            }
        );

        builder.Services.AddSingleton<LinkCache>();
    }
}
