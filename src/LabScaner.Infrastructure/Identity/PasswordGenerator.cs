using System.Security.Cryptography;

namespace LabScaner.Infrastructure.Identity;

/// <summary>Временный пароль, который администратор передаёт преподавателю: без похожих символов (0/O, 1/l/I).</summary>
public static class PasswordGenerator
{
    private const string Alphabet = "abcdefghjkmnpqrstuvwxyzABCDEFGHJKMNPQRSTUVWXYZ23456789";

    /// <summary>12 символов группами по 4 через дефис: «k7Rm-x2Pq-9tWe».</summary>
    public static string Generate()
    {
        Span<char> chars = stackalloc char[14];
        for (var i = 0; i < chars.Length; i++)
        {
            chars[i] = i is 4 or 9 ? '-' : Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        }

        return new string(chars);
    }
}
