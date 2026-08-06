using Microsoft.EntityFrameworkCore;
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
        // TODO: implement redis caching
        app.MapGet(
                "/{code}",
                async (string code, UrlShortenerContext dbContext) =>
                {
                    var link = await dbContext.Links.SingleOrDefaultAsync(link =>
                        link.ShortCode == code
                    );

                    if (link is null)
                    {
                        return Results.NotFound();
                    }

                    link.ClickCount++;

                    await dbContext.SaveChangesAsync();

                    return Results.Redirect(link.OriginalUrl.ToString(), permanent: false);
                }
            )
            .WithName("Redirect");

        // POST /
        // Takes a url and returns a short code url.
        // TODO: implement redis caching
        app.MapPost(
            "/",
            (ShortenUrlRequest request, UrlShortenerContext dbContext) =>
            {
                if (!request.Url.IsAbsoluteUri)
                    return Results.BadRequest("An absolute (full) url is required.");

                var shortCode = ShortCodeGenerator.Generate();

                var urlString = $"{request.Url}";

                var entry = new Link { OriginalUrl = urlString, ShortCode = shortCode };

                dbContext.Links.Add(entry);

                try
                {
                    dbContext.SaveChanges();
                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
                    return Results.InternalServerError();
                }
                // TODO: will implement retries at some point

                return Results.Created(
                    $"localhost:5071/{shortCode}",
                    $"localhost:5071/{shortCode}"
                );
                // TODO: need to implement proper link return with baseurl
            }
        );
    }
}
