using System.Collections.Generic;
using Neurocorp.Api.Core.BusinessObjects.Auth;

namespace Neurocorp.Api.Core.Interfaces.Services;

/// <summary>Issues and validates the application's access and refresh tokens.</summary>
public interface ITokenService
{
    /// <summary>Creates a signed access token embedding the user's identity, roles, and effective claims.</summary>
    /// <param name="mustChangePassword">
    /// When true, the token carries the must-change-password marker so the API can restrict the
    /// session to the password-change flow until the user updates their credentials.
    /// </param>
    TokenResult CreateAccessToken(
        int userId,
        string? email,
        string fullName,
        IReadOnlyCollection<ClaimDto> claims,
        IReadOnlyCollection<string> roles,
        bool mustChangePassword);

    /// <summary>Creates a longer-lived refresh token bound to the user.</summary>
    string CreateRefreshToken(int userId);

    /// <summary>Validates a refresh token and returns the user id it was issued for, or null if invalid.</summary>
    int? ValidateRefreshToken(string refreshToken);
}

/// <summary>A freshly minted token and how long (in seconds) it is valid.</summary>
public sealed record TokenResult(string Token, int ExpiresInSeconds);
