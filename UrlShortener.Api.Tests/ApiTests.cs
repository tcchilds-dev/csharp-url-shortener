using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using UrlShortener.Api.Models;
using UrlShortener.Api.Utilities;
using Xunit;

namespace UrlShortener.Api.Tests;

public class ApiTests
{
    [Fact]
    public async Task Shorten_CreatesValidLinkEntry_AndReturnsWorkingUrl()
    {
        await using var app = new ApiFactory();
        using var client = app.CreateApiClient();

        var before = DateTime.UtcNow;

        using var response = await client.PostAsJsonAsync(
            "/shorten",
            new { url = "https://example.com/path" }
        );

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var code = (await response.Content.ReadFromJsonAsync<string>())!;
        var link = await app.ReadLinkAsync(code);

        Assert.Matches("^[a-zA-Z0-9]{7}$", code);
        Assert.Equal($"/{code}", response.Headers.Location!.OriginalString);
        Assert.InRange(link.CreatedAt, before, DateTime.UtcNow);

        using var redirect = await client.GetAsync(response.Headers.Location);

        Assert.Equal(HttpStatusCode.Found, redirect.StatusCode);
        Assert.Equal(link.OriginalUrl, redirect.Headers.Location!.AbsoluteUri);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("example.com")]
    [InlineData("ftp://example.com")]
    [InlineData("javascript:alert(1)")]
    public async Task InvalidUrls_ReturnBadRequest(string? url)
    {
        await using var app = new ApiFactory();
        using var client = app.CreateApiClient();

        using var response = await client.PostAsJsonAsync("/shorten", new { url });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UrlLength_IsValidated_BeforeWriting()
    {
        await using var app = new ApiFactory();
        using var client = app.CreateApiClient();

        const string prefix = "https://example.com/";

        var url = prefix + new string('a', Link.MaxUrlLength - prefix.Length);

        using var accepted = await client.PostAsJsonAsync("/shorten", new { url });
        using var rejected = await client.PostAsJsonAsync("/shorten", new { url = url + "a" });

        // This input fits before normalization, but each é in the path becomes %C3%A9
        // (six ASCII characters) in AbsoluteUri, pushing the stored URL beyond the limit.
        using var expanded = await client.PostAsJsonAsync(
            "/shorten",
            new { url = prefix + new string('é', 1000) }
        );

        Assert.Equal(HttpStatusCode.Created, accepted.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, expanded.StatusCode);

        var code = (await accepted.Content.ReadFromJsonAsync<string>())!;

        Assert.Equal(url, (await app.ReadLinkAsync(code)).OriginalUrl);
    }

    [Theory]
    [InlineData("")]
    [InlineData("/blank")]
    [InlineData("/clicks")]
    public async Task UnknownCode_ReturnsNotFound_OnAllLookupRoutes(string suffix)
    {
        await using var app = new ApiFactory();
        using var client = app.CreateApiClient();

        var code = new ShortCodeGenerator().Generate();

        using var response = await client.GetAsync($"/{code}{suffix}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Eleventh_LinkCreationRequest_IsRateLimited()
    {
        await using var app = new ApiFactory();
        using var client = app.CreateApiClient();

        for (var index = 0; index < 10; index++)
        {
            using var response = await client.PostAsJsonAsync("/shorten", new { url = "invalid" });

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        using var rejected = await client.PostAsJsonAsync("/shorten", new { url = "invalid" });

        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
    }

    [Fact]
    public async Task SqlAndRedis_BothCheckCodeCase()
    {
        await using var app = new ApiFactory();
        using var client = app.CreateApiClient();

        var suffix = Guid.NewGuid().ToString("N")[..6];
        var upper = "A" + suffix;
        var lower = "a" + suffix;

        await app.SeedAsync(
            new Link { ShortCode = upper, OriginalUrl = "https://example.com/upper" },
            new Link { ShortCode = lower, OriginalUrl = "https://example.com/lower" }
        );

        // The first pass should read SQL and populate Redis, the second should read Redis.
        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var upperResponse = await client.GetAsync($"/{upper}");
            using var lowerResponse = await client.GetAsync($"/{lower}");

            Assert.Equal("https://example.com/upper", upperResponse.Headers.Location!.AbsoluteUri);
            Assert.Equal("https://example.com/lower", lowerResponse.Headers.Location!.AbsoluteUri);
        }
    }

    [Fact]
    public async Task CollisionRetries_WithoutLeaving_FailedEntityTracked()
    {
        var existing = new ShortCodeGenerator().Generate();
        var next = new ShortCodeGenerator().Generate();

        var generator = new SequenceGenerator(existing, next);
        await using var app = new ApiFactory(generator);
        using var client = app.CreateApiClient();

        await app.SeedAsync(
            new Link { ShortCode = existing, OriginalUrl = "https://example.com/existing" }
        );

        using var response = await client.PostAsJsonAsync(
            "/shorten",
            new { url = "https://example.com/new" }
        );

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(next, await response.Content.ReadFromJsonAsync<string>());
        Assert.Equal(2, generator.Attempts);
        Assert.Equal(
            "https://example.com/existing",
            (await app.ReadLinkAsync(existing)).OriginalUrl
        );
    }

    [Fact]
    public async Task RedisOutage_StillAllows_SqlLinkCreationAndRedirect()
    {
        await using var app = new ApiFactory(redisUnavailable: true);
        using var client = app.CreateApiClient();

        using var created = await client.PostAsJsonAsync(
            "/shorten",
            new { url = "https://example.com/outage" }
        );

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        using var redirect = await client.GetAsync(created.Headers.Location);

        Assert.Equal(HttpStatusCode.Found, redirect.StatusCode);
        Assert.Equal("https://example.com/outage", redirect.Headers.Location!.AbsoluteUri);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WorkerDrains_QueuedClicks_OnGracefulShutdown(bool cancelBeforeExecution)
    {
        await using var app = new ApiFactory();
        using var client = app.CreateApiClient();
        var code = new ShortCodeGenerator().Generate();

        await app.SeedAsync(
            new Link { ShortCode = code, OriginalUrl = "https://example.com/shutdown" }
        );

        var queue = new ClicksUpdateQueue(NullLogger<ClicksUpdateQueue>.Instance);

        using var worker = new ClicksUpdateWorker(
            queue,
            NullLogger<ClicksUpdateWorker>.Instance,
            app.Services.GetRequiredService<IServiceScopeFactory>()
        );

        for (var index = 0; index < 100; index++)
        {
            Assert.True(queue.TryEnqueue(new ClicksUpdateJob(code)));
        }

        await worker.StartAsync(new CancellationToken(canceled: cancelBeforeExecution));

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        await Task.WhenAll(worker.StopAsync(timeout.Token), worker.StopAsync(timeout.Token));

        Assert.Equal(100, (await app.ReadLinkAsync(code)).ClickCount);
        Assert.False(queue.TryEnqueue(new ClicksUpdateJob(code)));
    }

    private sealed class SequenceGenerator(params string[] codes) : IShortCodeGenerator
    {
        public int Attempts { get; private set; }

        public string Generate() => codes[Attempts++];
    }
}
