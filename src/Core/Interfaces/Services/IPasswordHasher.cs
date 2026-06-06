namespace Neurocorp.Api.Core.Interfaces.Services;

/// <summary>Hashes and verifies local passwords. Implementation must be salted and slow.</summary>
public interface IPasswordHasher
{
    /// <summary>Produces a self-describing hash string (algorithm + parameters + salt + hash).</summary>
    string Hash(string password);

    /// <summary>Constant-time verification of a password against a stored hash.</summary>
    bool Verify(string password, string storedHash);
}
