using System.Security.Cryptography;

namespace WebApp.Identity;

public static class TemporaryPasswords
{
    // No visually ambiguous characters (0/O, 1/l/I) so an operator can read the value aloud or retype it.
    private const string Alphabet = "ABCDEFGHJKMNPQRSTUVWXYZabcdefghijkmnpqrstuvwxyz23456789";

    public static string Generate(int length = 16)
    {
        var chars = new char[length];
        for (var i = 0; i < length; i++) chars[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        return new string(chars);
    }
}
