using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using StackExchange.Redis;
using UrlShortener.Api.Data;
using UrlShortener.Api.Models;
using UrlShortener.Api.Utilities;

var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true };
var command = args.FirstOrDefault();
if (command is not ("prepare" or "verify"))
    throw new ArgumentException(
        "Usage: prepare [link-count] | verify [timeout-seconds]. Run from the repository root."
    );
var path = Environment.GetEnvironmentVariable("DATA_FILE") ?? "load-test-data.json";
var configuration = new ConfigurationBuilder()
    .AddJsonFile(Path.GetFullPath("UrlShortener.Api/appsettings.json"))
    .AddEnvironmentVariables()
    .Build();
await using var db = new UrlShortenerContext(
    new DbContextOptionsBuilder<UrlShortenerContext>()
        .UseSqlServer(configuration.GetConnectionString("UrlShortener"))
        .Options
);

if (command == "prepare")
{
    var count = args.Length > 1 ? int.Parse(args[1]) : 10_000;
    if (count < 1010)
        throw new ArgumentException("Prepare at least 1010 links for the mixed workload.");

    var previous = File.Exists(path)
        ? JsonSerializer.Deserialize<Manifest>(await File.ReadAllTextAsync(path), jsonOptions)
        : null;
    var codes = previous?.Codes.ToList() ?? [];
    var existing = await db
        .Links.Where(link => codes.Contains(link.ShortCode))
        .ToDictionaryAsync(link => link.ShortCode);
    if (
        existing.Count != codes.Count
        || existing.Values.Any(link => link.OriginalUrl != "https://example.com/")
    )
        throw new InvalidOperationException(
            "Seed data no longer matches SQL. Use a new DATA_FILE to prepare a fresh dataset."
        );
    var generator = new ShortCodeGenerator();
    var reserved = (await db.Links.Select(link => link.ShortCode).ToListAsync()).ToHashSet();
    while (codes.Count < count)
    {
        var code = generator.Generate();
        if (!reserved.Add(code))
            continue;
        db.Links.Add(new Link { ShortCode = code, OriginalUrl = "https://example.com/" });
        codes.Add(code);
    }
    await db.SaveChangesAsync();

    using var redis = await ConnectionMultiplexer.ConnectAsync(
        configuration.GetConnectionString("Redis")!
    );
    foreach (var chunk in codes.Chunk(500))
        await Task.WhenAll(
            chunk.Select(code =>
                redis
                    .GetDatabase()
                    .StringSetAsync(code, "https://example.com/", TimeSpan.FromHours(48))
            )
        );
    var baseline = await db
        .Links.Where(link => codes.Contains(link.ShortCode))
        .SumAsync(link => link.ClickCount);
    var manifest = new Manifest(
        Guid.NewGuid().ToString("N"),
        "https://example.com/",
        codes.ToArray(),
        baseline.ToString(CultureInfo.InvariantCulture)
    );
    await File.WriteAllTextAsync(path, JsonSerializer.Serialize(manifest, jsonOptions));
    Console.WriteLine(
        $"Prepared {codes.Count} cached links in {path}. Run one k6 workload, then verify before preparing again."
    );
}
else
{
    var manifest = JsonSerializer.Deserialize<Manifest>(
        await File.ReadAllTextAsync(path),
        jsonOptions
    )!;
    var summaryPath = Environment.GetEnvironmentVariable("SUMMARY_FILE") ?? "k6-summary.json";
    using var summary = JsonDocument.Parse(await File.ReadAllTextAsync(summaryPath));
    var report = summary.RootElement;
    if (report.GetProperty("runId").GetString() != manifest.RunId)
        throw new InvalidOperationException(
            "The summary is from a different preparation. Run k6 with the current data file."
        );
    var expected = report.GetProperty("successfulRedirects").GetInt64();
    if (expected <= 0)
        throw new InvalidOperationException("No successful redirects were recorded.");
    var timeout = args.Length > 1 ? int.Parse(args[1]) : 30;
    if (timeout <= 0)
        throw new ArgumentException("Timeout must be positive.");
    var baseline = long.Parse(manifest.Baseline, CultureInfo.InvariantCulture);
    var timer = Stopwatch.StartNew();
    long actual;
    var matchingObservations = 0;
    do
    {
        actual =
            await db
                .Links.Where(link => manifest.Codes.Contains(link.ShortCode))
                .SumAsync(link => link.ClickCount) - baseline;
        matchingObservations = actual == expected ? matchingObservations + 1 : 0;
        if (matchingObservations == 2)
            break;
        if (actual > expected)
            break;
        await Task.Delay(1000);
    } while (timer.Elapsed < TimeSpan.FromSeconds(timeout));
    Console.WriteLine($"Successful redirects: {expected}; persisted click increase: {actual}.");
    if (actual != expected || matchingObservations < 2)
    {
        Console.Error.WriteLine(
            "Click verification failed: counts were lost, duplicated, still pending, or affected by another run."
        );
        Environment.ExitCode = 1;
    }
}

record Manifest(string RunId, string TargetUrl, string[] Codes, string Baseline);
