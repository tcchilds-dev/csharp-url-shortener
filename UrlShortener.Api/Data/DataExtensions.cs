using Microsoft.EntityFrameworkCore;

namespace UrlShortener.Api.Data;

public static class DataExtensions
{
    public static void MigrateDb(this WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<UrlShortenerContext>();
        dbContext.Database.Migrate();
    }

    public static void AddUrlShortenerDb(this WebApplicationBuilder builder)
    {
        builder.Services.AddDbContext<UrlShortenerContext>(options =>
            options.UseSqlServer(builder.Configuration.GetConnectionString("UrlShortener")));
    }
}