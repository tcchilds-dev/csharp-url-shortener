using UrlShortener.Api.Data;
using UrlShortener.Api.Routes;

var builder = WebApplication.CreateBuilder(args);

var baseUrl = builder.Configuration["BaseUrl"];

// TODO: implement proper logging

builder.Services.AddValidation();
builder.AddSqlDb();
builder.AddRedisDb();

var app = builder.Build();

app.UseHttpsRedirection();

app.MapGet("/health", () => Results.Ok());
app.MapLinkRoutes();

app.MigrateDb();

app.Run();
