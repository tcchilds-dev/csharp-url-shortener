using System.ComponentModel.DataAnnotations;

namespace UrlShortener.Api.Models;

public class Link
{
    public int Id { get; init; }
    [DataType(DataType.Url)]
    [MaxLength(200)] public required string OriginalUrl { get; set; }
    [MaxLength(7)] public required string ShortCode { get; set; }
    public int ClickCount { get; set; }
    [DataType(DataType.Date)]
    public DateTime CreatedAt { get; init; }
}