using Microsoft.EntityFrameworkCore;
using UrlShortener.Api.Models;

namespace UrlShortener.Api.Data;

public class UrlShortenerContext : DbContext
{
    public UrlShortenerContext(
        DbContextOptions<UrlShortenerContext> options) : base(options)
    {
    }

    public DbSet<Link> Links => Set<Link>();
}