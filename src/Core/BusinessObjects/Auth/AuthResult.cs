using System.Collections.Generic;

namespace Neurocorp.Api.Core.BusinessObjects.Auth;

/// <summary>
/// Internal result of an authentication operation (login / refresh / change-password).
/// The controller translates a successful result into an <see cref="AuthTokenResponse"/>.
/// </summary>
public class AuthResult
{
    public bool Succeeded { get; init; }
    public string? Error { get; init; }
    public string? AccessToken { get; init; }
    public string? RefreshToken { get; init; }
    public int ExpiresInSeconds { get; init; }
    public bool MustChangePassword { get; init; }

    public static AuthResult Fail(string error) => new() { Succeeded = false, Error = error };

    public static AuthResult Success(string accessToken, string refreshToken, int expiresInSeconds, bool mustChangePassword) =>
        new()
        {
            Succeeded = true,
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            ExpiresInSeconds = expiresInSeconds,
            MustChangePassword = mustChangePassword
        };
}
