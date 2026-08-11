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
                    // TODO: refresh TTL?
                    var redisLink = await cache.StringGetAsync(code);
                    if (redisLink != RedisValue.Null)
                    {
                        logger.LogInformation("Cache hit for short code: {shortCode}", code);
                        await clicksUpdateQueue.EnqueueAsync(
                            new ClicksUpdateJob(code),
                            stoppingToken
                        );
                        return Results.Redirect(redisLink.ToString(), permanent: false);
                    }
                    else
                    {
                        logger.LogInformation("Cache miss for short code: {shortCode}", code);
                        var link = await dbContext.Links.SingleOrDefaultAsync(link =>
                            link.ShortCode == code
                        );
                        if (link is null)
                        {
                            return Results.NotFound();
                        }
                        await dbContext.SaveChangesAsync();
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
                            // TODO: configure TTL
                            await cache.StringSetAsync(shortCode, URLString);
                            return Results.Created("$/{shortCode}", link.ShortCode);
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
