using UrlShortener.Api.Data;

var builder = WebApplication.CreateBuilder(args);

builder.AddUrlShortenerDb();

var app = builder.Build();

app.UseHttpsRedirection();

app.MapGet("/", () => Results.Ok());
// app.MapGet("/{code}", (string code) => Results.Redirect(..., true)).WithName("Redirect");
// app.MapPost("/", (string url) => Results.CreatedAtRoute("Redirect", ...));

app.MigrateDb();

app.Run();
