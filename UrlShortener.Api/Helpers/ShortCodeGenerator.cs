using System.Security.Cryptography;

namespace UrlShortener.Api.Utilities;

public static class ShortCodeGenerator
{
    private const string _choices =
        "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";

    public static string Generate(int length = 7)
    {
        return RandomNumberGenerator.GetString(_choices, length);
    }
}
