using System.Security.Cryptography;

namespace UrlShortener.Api.Utilities;

public interface IShortCodeGenerator
{
    string Generate();
}

public class ShortCodeGenerator : IShortCodeGenerator
{
    private const string Choices = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";

    public string Generate() => RandomNumberGenerator.GetString(Choices, 7);
}
