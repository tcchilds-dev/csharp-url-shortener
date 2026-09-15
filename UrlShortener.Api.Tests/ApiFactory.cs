using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using UrlShortener.Api.Data;
using UrlShortener.Api.Models;
using UrlShortener.Api.Utilities;

namespace UrlShortener.Api.Tests;

public class ApiFactory : WebApplicationFactory<Program>
{
    private readonly string _connectionString;
    private readonly string _redisConnection;
    private readonly IShortCodeGenerator? _generator;

    public ApiFactory(IShortCodeGenerator? generator = null, bool redisUnavailable = false)
    {
        var connection = new SqlConnectionStringBuilder(
            "Server=localhost,1434;User Id=sa;Password=TestOnlyPassword123!;TrustServerCertificate=True"
        )
        {
            InitialCatalog = $"UrlShortenerTests_{Guid.NewGuid():N}",
        };

        _connectionString = connection.ConnectionString;
        _redisConnection = redisUnavailable
            ? "127.0.0.1:1,abortConnect=false,connectTimeout=100,syncTimeout=100,asyncTimeout=250,connectRetry=0"
            : "localhost:6380";
        _generator = generator;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:UrlShortener", _connectionString);
        builder.UseSetting("ConnectionStrings:Redis", _redisConnection);
        builder.UseSetting("Logging:LogLevel:Default", "Warning");

        builder.ConfigureTestServices(services =>
        {
            if (_generator is not null)
            {
                services.RemoveAll<IShortCodeGenerator>();
                services.AddSingleton(_generator);
            }
        });
    }

    public HttpClient CreateApiClient() =>
        CreateClient(
            new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false,
                BaseAddress = new Uri("https://localhost"),
            }
        );

    public async Task SeedAsync(params Link[] links)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<UrlShortenerContext>();

        db.Links.AddRange(links);

        await db.SaveChangesAsync();
    }

    public async Task<Link> ReadLinkAsync(string code)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<UrlShortenerContext>();

        return await db.Links.AsNoTracking().SingleAsync(link => link.ShortCode == code);
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();

        var options = new DbContextOptionsBuilder<UrlShortenerContext>()
            .UseSqlServer(_connectionString)
            .Options;

        await using var db = new UrlShortenerContext(options);

        await db.Database.EnsureDeletedAsync();
    }
}
