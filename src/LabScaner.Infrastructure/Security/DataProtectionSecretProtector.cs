using System.Security.Cryptography;
using LabScaner.Core.Abstractions;
using Microsoft.AspNetCore.DataProtection;

namespace LabScaner.Infrastructure.Security;

/// <summary>Секреты подключений шифруются ключами Data Protection (каталог ключей — том Docker, см. DEPLOY.md).</summary>
public sealed class DataProtectionSecretProtector(IDataProtectionProvider provider) : ISecretProtector
{
    private readonly IDataProtector _protector = provider.CreateProtector("LabScaner.Secrets.v1");

    public string Protect(string secret)
    {
        ArgumentException.ThrowIfNullOrEmpty(secret);
        return _protector.Protect(secret);
    }

    public string? Unprotect(string protectedSecret)
    {
        if (string.IsNullOrEmpty(protectedSecret))
        {
            return null;
        }

        try
        {
            return _protector.Unprotect(protectedSecret);
        }
        catch (CryptographicException)
        {
            return null;
        }
    }
}
