using Microsoft.Extensions.Diagnostics.HealthChecks;
using UrlShortener.Api.Data;
using UrlShortener.Api.RateLimiting;
using UrlShortener.Api.Routes;
using UrlShortener.Api.Utilities;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddValidation();
builder.Services.AddSingleton<IShortCodeGenerator, ShortCodeGenerator>();
builder.Services.AddSingleton<ClicksUpdateQueue>();
builder.Services.AddHostedService<ClicksUpdateWorker>();
builder.AddRateLimiters();
builder.AddSqlDb();
builder.AddRedisDb();

builder
    .Services.AddHealthChecks()
    .AddDbContextCheck<UrlShortenerContext>(name: "sql", failureStatus: HealthStatus.Unhealthy)
    .AddCheck<RedisHealthCheck>(
        name: "redis",
        failureStatus: HealthStatus.Degraded,
        timeout: TimeSpan.FromSeconds(1)
    );

builder.Services.AddOpenApi("api");

var app = builder.Build();

app.UseHttpsRedirection();
app.UseRateLimiter();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapHealthChecks("/healthz");

app.MapLinkRoutes();

app.MigrateDb();

app.Run();

public partial class Program { }
