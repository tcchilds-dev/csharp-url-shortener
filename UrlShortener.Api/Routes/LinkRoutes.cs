using System.Diagnostics;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using UrlShortener.Api.Caching;
using UrlShortener.Api.Data;
using UrlShortener.Api.Models;
using UrlShortener.Api.RateLimiting;
using UrlShortener.Api.Utilities;

namespace UrlShortener.Api.Routes;

public record ShortenUrlRequest(string? Url);

public static class LinkRoutes
{
    private const int MaxAttempts = 3;

    public static void MapLinkRoutes(this WebApplication app)
    {
        app.MapGet("/{code}", Redirect)
            .WithName("Redirect")
            .WithSummary("Redirect to the assosciated URL from a short code.")
            .WithDescription("Looks up the given short code and redirects to its destination URL.")
            .Produces(StatusCodes.Status302Found)
            .ProducesProblem(StatusCodes.Status404NotFound);

        app.MapGet("/{code}/blank", BlankRedirect)
            .WithName("BlankRedirect")
            .WithSummary("Shows stats for redirect handler operations.")
            .WithDescription(
                "Instead of redirecting, reports cache hits/misses and lookup and handler completion times."
            )
            .Produces(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        app.MapGet("/{code}/clicks", GetClicks)
            .WithName("GetClicks")
            .WithSummary("Retrieves the amount of clicks a link entry has.")
            .Produces(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        app.MapPost("/shorten", CreateLink)
            .RequireRateLimiting(RateLimitingExtensions.PostLimiter)
            .WithName("CreateLink")
            .WithSummary("Create a short link from a URL.")
            .WithDescription("Creates a new short code for the supplied URL and returns that code.")
            .Accepts<ShortenUrlRequest>("application/json")
            .Produces(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status429TooManyRequests);
    }

    private static async Task<IResult> Redirect(
        string code,
        UrlShortenerContext dbContext,
        LinkCache cache,
        ClicksUpdateQueue queue,
        CancellationToken cancellationToken
    )
    {
        var lookup = await LookupAsync(code, dbContext, cache, queue, cancellationToken);

        return lookup is null ? Results.NotFound() : Results.Redirect(lookup.Url, permanent: false);
    }

    private static async Task<IResult> BlankRedirect(
        string code,
        UrlShortenerContext dbContext,
        LinkCache cache,
        ClicksUpdateQueue queue,
        CancellationToken cancellationToken
    )
    {
        var lookup = await LookupAsync(code, dbContext, cache, queue, cancellationToken);

        if (lookup is null)
        {
            return Results.NotFound();
        }

        var cacheStatus = lookup.CacheHit ? "Cache Hit" : "Cache Miss";

        return Results.Text(
            $"{cacheStatus}\nLookup: {lookup.LookupMs:F2}ms\nHandler Completed In: {lookup.HandlerMs:F2}ms"
        );
    }

    private static async Task<IResult> GetClicks(
        string code,
        UrlShortenerContext dbContext,
        CancellationToken cancellationToken
    )
    {
        var count = await dbContext
            .Links.Where(link => link.ShortCode == code)
            .Select(link => (long?)link.ClickCount)
            .SingleOrDefaultAsync(cancellationToken);

        return count is null ? Results.NotFound() : Results.Text($"{count}");
    }

    private static async Task<IResult> CreateLink(
        ShortenUrlRequest request,
        UrlShortenerContext dbContext,
        LinkCache cache,
        IShortCodeGenerator generator,
        CancellationToken cancellationToken
    )
    {
        if (string.IsNullOrWhiteSpace(request.Url) || request.Url.Length > Link.MaxUrlLength)
        {
            return Results.BadRequest(
                $"A URL length must be between 1 and {Link.MaxUrlLength} characters."
            );
        }

        if (
            !Uri.TryCreate(request.Url, UriKind.Absolute, out var url)
            || (url.Scheme != Uri.UriSchemeHttp && url.Scheme != Uri.UriSchemeHttps)
            || string.IsNullOrWhiteSpace(url.Host)
        )
        {
            return Results.BadRequest("An absolute HTTP or HTTPS URL is required.");
        }

        var originalUrl = url.AbsoluteUri;

        if (originalUrl.Length > Link.MaxUrlLength)
        {
            return Results.BadRequest(
                $"The normalized URL must not exceed {Link.MaxUrlLength} characters."
            );
        }

        for (var attempt = 0; attempt < MaxAttempts; attempt++)
        {
            var link = new Link { OriginalUrl = originalUrl, ShortCode = generator.Generate() };

            dbContext.Links.Add(link);

            try
            {
                await dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException e)
                when (e.InnerException is SqlException { Number: 2601 or 2627 })
            {
                dbContext.Entry(link).State = EntityState.Detached;

                continue;
            }

            await cache.SetAsync(link.ShortCode, link.OriginalUrl);

            return Results.Created($"/{link.ShortCode}", link.ShortCode);
        }

        return Results.InternalServerError("Could not generate a unique short code.");
    }

    private static async Task<LinkLookup?> LookupAsync(
        string code,
        UrlShortenerContext dbContext,
        LinkCache cache,
        ClicksUpdateQueue queue,
        CancellationToken cancellationToken
    )
    {
        if (code.Length != 7 || code.Any(character => !char.IsAsciiLetterOrDigit(character)))
        {
            return null;
        }

        var stopwatch = Stopwatch.StartNew();
        var originalUrl = await cache.GetAsync(code);
        var cacheHit = originalUrl is not null;

        if (!cacheHit)
        {
            originalUrl = await dbContext
                .Links.Where(link => link.ShortCode == code)
                .Select(link => link.OriginalUrl)
                .SingleOrDefaultAsync(cancellationToken);
        }

        var lookupMs = stopwatch.Elapsed.TotalMilliseconds;

        if (originalUrl is null)
        {
            return null;
        }

        if (!cacheHit)
        {
            await cache.SetAsync(code, originalUrl);
        }

        queue.TryEnqueue(new ClicksUpdateJob(code));

        return new LinkLookup(originalUrl, cacheHit, lookupMs, stopwatch.Elapsed.TotalMilliseconds);
    }

    private sealed record LinkLookup(string Url, bool CacheHit, double LookupMs, double HandlerMs);
}
