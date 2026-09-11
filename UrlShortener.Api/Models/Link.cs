using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace UrlShortener.Api.Models;

[Index(nameof(ShortCode), IsUnique = true)]
public class Link
{
    public const int MaxUrlLength = 4096;

    public int Id { get; init; }

    [Required]
    [Url(ErrorMessage = "A valid URL is required.")]
    [MaxLength(MaxUrlLength)]
    public required string OriginalUrl { get; set; }

    [MaxLength(7)]
    public required string ShortCode { get; set; }

    public int ClickCount { get; set; }

    [DataType(DataType.Date)]
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
}
