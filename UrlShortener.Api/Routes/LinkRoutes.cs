using System.Diagnostics;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;
using UrlShortener.Api.Data;
using UrlShortener.Api.Models;
using UrlShortener.Api.RateLimiting;
using UrlShortener.Api.Utilities;

namespace UrlShortener.Api.Routes;

public record ShortenUrlRequest(Uri Url);

public static class LinkRoutes
{
    const int maxAttempts = 3;

    static bool IsUniqueConstraintViolation(DbUpdateException exception)
    {
        return exception.InnerException is SqlException sqlException
            && sqlException.Number is 2601 or 2627;
    }

    public static void MapLinkRoutes(this WebApplication app)
    {
        // GET /{code}
        // Takes a shortened URL and redirects the client to the original URL.
        // TODO: performance logs will be sent to the frontend once it has been created
        app.MapGet(
                "/{code}",
                async (
                    string code,
                    UrlShortenerContext dbContext,
                    IConnectionMultiplexer redis,
                    ILogger<Program> logger,
                    ClicksUpdateQueue clicksUpdateQueue,
                    CancellationToken stoppingToken
                ) =>
                {
                    IDatabase cache = redis.GetDatabase();
                    var stopwatch = Stopwatch.StartNew();
                    var redisLink = await cache.StringGetAsync(code);
                    if (redisLink != RedisValue.Null)
                    {
                        // NOTE: may change time measurement units
                        logger.LogInformation(
                            "Cache hit for {ShortCode}: lookup took {ElapsedMs:F2}ms",
                            code,
                            stopwatch.Elapsed.TotalMilliseconds
                        );
                        try
                        {
                            await clicksUpdateQueue.EnqueueAsync(
                                new ClicksUpdateJob(code),
                                stoppingToken
                            );
                        }
                        catch (Exception e)
                        {
                            logger.LogError(
                                e,
                                "Failed to enqueue click update job for code {ShortCode}",
                                code
                            );
                        }
                        logger.LogInformation(
                            "{ShortCode}: cache hit - request completed in {ElapsedMs:F2}ms",
                            code,
                            stopwatch.Elapsed.TotalMilliseconds
                        );
                        return Results.Redirect(redisLink.ToString(), permanent: false);
                    }
                    else
                    {
                        logger.LogInformation("Cache miss for short code: {ShortCode}", code);
                        var link = await dbContext.Links.SingleOrDefaultAsync(link =>
                            link.ShortCode == code
                        );
                        if (link is null)
                        {
                            return Results.NotFound();
                        }
                        try
                        {
                            link.ClickCount++;
                            await dbContext.SaveChangesAsync();
                        }
                        catch (Exception e)
                        {
                            logger.LogError(
                                e,
                                "Failed to update click count for {ShortCode}",
                                code
                            );
                        }
                        logger.LogInformation(
                            "{ShortCode}: cache miss - request completed in {ElapsedMs:F2}",
                            code,
                            stopwatch.Elapsed.TotalMilliseconds
                        );
                        return Results.Redirect(link.OriginalUrl.ToString(), permanent: false);
                    }
                }
            )
            .WithName("Redirect");

        // POST /shorten
        // Takes a URL and returns a short code URL.
        app.MapPost(
                "/shorten",
                async (
                    ShortenUrlRequest request,
                    UrlShortenerContext dbContext,
                    IConnectionMultiplexer redis,
                    ILogger<Program> logger
                ) =>
                {
                    if (!request.Url.IsAbsoluteUri)
                        return Results.BadRequest("An absolute (full) URL is required.");
                    IDatabase cache = redis.GetDatabase();
                    var URLString = $"{request.Url}";
                    for (var attempt = 1; attempt <= maxAttempts; attempt++)
                    {
                        var shortCode = ShortCodeGenerator.Generate();
                        var link = new Link { OriginalUrl = URLString, ShortCode = shortCode };
                        dbContext.Links.Add(link);
                        try
                        {
                            await dbContext.SaveChangesAsync();
                            await cache.StringSetAsync(
                                shortCode,
                                URLString,
                                TimeSpan.FromHours(48)
                            );
                            return Results.Created("$/{ShortCode}", link.ShortCode);
                        }
                        catch (DbUpdateException e) when (IsUniqueConstraintViolation(e))
                        {
                            dbContext.Entry(link).State = EntityState.Detached;
                        }
                    }
                    return Results.InternalServerError("Could not generate a unique short code.");
                }
            )
            .RequireRateLimiting(RateLimitingExtensions.PostLimiter);
    }
}
