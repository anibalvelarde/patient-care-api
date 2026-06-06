using System;
using System.Security.Cryptography;
using Neurocorp.Api.Core.Interfaces.Services;

namespace Neurocorp.Api.Core.Services;

/// <summary>
/// PBKDF2 (RFC 2898) password hasher using only the .NET BCL — no external dependency,
/// which keeps the Core project dependency-free and avoids supply-chain surface for a
/// security-critical primitive.
///
/// Stored format (self-describing, so parameters can evolve without a data migration):
///   PBKDF2$&lt;iterations&gt;$&lt;saltBase64&gt;$&lt;hashBase64&gt;
///
/// The explicit <c>PasswordSalt</c> column is intentionally left unused — the salt is
/// embedded in the hash string, matching the comment on the DB column.
/// </summary>
public class Pbkdf2PasswordHasher : IPasswordHasher
{
    private const int SaltSize = 16;          // 128-bit salt
    private const int KeySize = 32;           // 256-bit derived key
    private const int Iterations = 100_000;   // OWASP-aligned cost for PBKDF2-SHA256
    private static readonly HashAlgorithmName Algorithm = HashAlgorithmName.SHA256;
    private const string Prefix = "PBKDF2";

    public string Hash(string password)
    {
        ArgumentNullException.ThrowIfNull(password);

        byte[] salt = RandomNumberGenerator.GetBytes(SaltSize);
        byte[] key = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, Algorithm, KeySize);

        return string.Join('$', Prefix, Iterations, Convert.ToBase64String(salt), Convert.ToBase64String(key));
    }

    public bool Verify(string password, string storedHash)
    {
        if (string.IsNullOrEmpty(password) || string.IsNullOrEmpty(storedHash))
        {
            return false;
        }

        var parts = storedHash.Split('$');
        if (parts.Length != 4 || parts[0] != Prefix)
        {
            return false;
        }

        if (!int.TryParse(parts[1], out var iterations))
        {
            return false;
        }

        byte[] salt;
        byte[] expectedKey;
        try
        {
            salt = Convert.FromBase64String(parts[2]);
            expectedKey = Convert.FromBase64String(parts[3]);
        }
        catch (FormatException)
        {
            return false;
        }

        byte[] actualKey = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, Algorithm, expectedKey.Length);
        return CryptographicOperations.FixedTimeEquals(actualKey, expectedKey);
    }
}
