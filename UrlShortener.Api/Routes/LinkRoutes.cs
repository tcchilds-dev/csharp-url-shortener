using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;
using UrlShortener.Api.Data;
using UrlShortener.Api.Models;
using UrlShortener.Api.Utilities;

namespace UrlShortener.Api.Routes;

public record ShortenUrlRequest(Uri Url);

public static class LinkRoutes
{
    public static void MapLinkRoutes(this WebApplication app)
    {
        // GET /:code
        // Takes a shortened url and redirects the client to the original url.
        // TODO: add background worker for click count
        app.MapGet(
                "/{code}",
                async (
                    string code,
                    UrlShortenerContext dbContext,
                    IConnectionMultiplexer redis,
                    ILogger<Program> logger
                ) =>
                {
                    IDatabase cache = redis.GetDatabase();

                    // TODO: refresh TTL?
                    var redisLink = await cache.StringGetAsync(code);

                    if (redisLink != RedisValue.Null)
                    {
                        logger.LogDebug("Cache hit for short code: {shortCode}", code);
                        return Results.Redirect(redisLink.ToString(), permanent: false);
                    }
                    else
                    {
                        logger.LogDebug("Cache miss for short code: {shortCode}", code);

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

        // POST /
        // Takes a url and returns a short code url.
        app.MapPost(
            "/",
            async (
                ShortenUrlRequest request,
                UrlShortenerContext dbContext,
                IConnectionMultiplexer redis,
                ILogger<Program> logger
            ) =>
            {
                if (!request.Url.IsAbsoluteUri)
                    return Results.BadRequest("An absolute (full) url is required.");

                IDatabase cache = redis.GetDatabase();

                var shortCode = ShortCodeGenerator.Generate();

                var urlString = $"{request.Url}";

                var entry = new Link { OriginalUrl = urlString, ShortCode = shortCode };

                await dbContext.Links.AddAsync(entry);

                try
                {
                    await dbContext.SaveChangesAsync();
                }
                catch (Exception e)
                {
                    logger.LogError("{errorMessage}", e.Message);
                    return Results.InternalServerError();
                }
                // TODO: implement retries

                // TODO: configure TTL
                await cache.StringSetAsync(shortCode, urlString);

                return Results.Created(
                    $"localhost:5071/{shortCode}",
                    $"localhost:5071/{shortCode}"
                );
                // TODO: need to implement proper link return with baseurl
            }
        );
    }
}
