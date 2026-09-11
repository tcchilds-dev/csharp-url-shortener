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

var app = builder.Build();

app.UseHttpsRedirection();
app.UseRateLimiter();

app.MapGet("/health", () => Results.Ok());
app.MapLinkRoutes();

app.MigrateDb();

app.Run();

public partial class Program { }
