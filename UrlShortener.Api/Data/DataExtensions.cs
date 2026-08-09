using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;

namespace UrlShortener.Api.Data;

public static class DataExtensions
{
    public static void MigrateDb(this WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<UrlShortenerContext>();
        dbContext.Database.Migrate();
    }

    public static void AddSqlDb(this WebApplicationBuilder builder)
    {
        builder.Services.AddDbContext<UrlShortenerContext>(options =>
            options.UseSqlServer(builder.Configuration.GetConnectionString("UrlShortener"))
        );
    }

    public static void AddRedisDb(this WebApplicationBuilder builder)
    {
        builder.Services.AddSingleton<IConnectionMultiplexer>(
            ConnectionMultiplexer.Connect(builder.Configuration.GetConnectionString("Redis")!)
        );
    }
}
