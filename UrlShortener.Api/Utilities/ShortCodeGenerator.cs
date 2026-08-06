using System.Security.Cryptography;

namespace UrlShortener.Api.Utilities;

public static class ShortCodeGenerator
{
    private const string Alphabet =
        "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";

    public static string Generate(int length = 7)
    {
        return string.Create(length, Alphabet, static (characters, alphabet) =>
        {
            for (var i = 0; i < characters.Length; i++)
            {
                var index = RandomNumberGenerator.GetInt32(alphabet.Length);
                characters[i] = alphabet[index];
            }
        });
    }
}