using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using Neurocorp.Api.Core.Authorization;
using Neurocorp.Api.Core.BusinessObjects.Auth;
using Neurocorp.Api.Core.Interfaces.Services;

namespace Neurocorp.Api.Web.Authentication;

/// <summary>
/// Issues and validates HMAC-SHA256 signed JWTs.
///
/// Access tokens are stateless and carry the user's identity, roles, and effective claims
/// (including the ('System','FullAccess') wildcard for SystemAdmins) so the API can do
/// policy evaluation and the SPA can drive claims-aware rendering without an extra round-trip.
///
/// Refresh tokens are longer-lived signed JWTs marked with typ=refresh. NOTE: with no
/// server-side revocation store (deliberately deferred in Chunk 1A) a refresh token cannot be
/// individually revoked before expiry — see the 1B sub-plan trade-offs. Keep refresh lifetime
/// modest and add a RefreshToken table when revocation becomes a requirement.
/// </summary>
public class JwtTokenService : ITokenService
{
    private const string RefreshTokenTypeClaim = "typ";
    private const string RefreshTokenTypeValue = "refresh";

    private readonly string _issuer;
    private readonly string _audience;
    private readonly int _accessMinutes;
    private readonly int _refreshDays;
    private readonly SymmetricSecurityKey _signingKey;

    public JwtTokenService(IConfiguration config, IHostEnvironment env)
    {
        _issuer = JwtConfig.Issuer(config);
        _audience = JwtConfig.Audience(config);
        _accessMinutes = JwtConfig.AccessTokenMinutes(config);
        _refreshDays = JwtConfig.RefreshTokenDays(config);
        _signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(JwtConfig.ResolveSigningKey(config, env)));
    }

    public TokenResult CreateAccessToken(
        int userId,
        string? email,
        string fullName,
        IReadOnlyCollection<ClaimDto> claims,
        IReadOnlyCollection<string> roles,
        bool mustChangePassword)
    {
        var now = DateTime.UtcNow;
        var expires = now.AddMinutes(_accessMinutes);

        var jwtClaims = new List<Claim>
        {
            new(SystemClaims.UserIdClaimType, userId.ToString()),
            new(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new("name", fullName)
        };

        if (mustChangePassword)
        {
            // Marks the session as restricted to the password-change flow (enforced by
            // PasswordChangeRequiredMiddleware). Dropped once the password is changed.
            jwtClaims.Add(new Claim(SystemClaims.MustChangePasswordClaimType, SystemClaims.MustChangePasswordValue));
        }

        if (!string.IsNullOrEmpty(email))
        {
            jwtClaims.Add(new Claim(JwtRegisteredClaimNames.Email, email));
        }

        foreach (var role in roles)
        {
            jwtClaims.Add(new Claim("role", role));
        }

        foreach (var claim in claims)
        {
            jwtClaims.Add(new Claim(claim.Type, claim.Value));
        }

        var token = new JwtSecurityToken(
            issuer: _issuer,
            audience: _audience,
            claims: jwtClaims,
            notBefore: now,
            expires: expires,
            signingCredentials: new SigningCredentials(_signingKey, SecurityAlgorithms.HmacSha256));

        var encoded = new JwtSecurityTokenHandler().WriteToken(token);
        return new TokenResult(encoded, (int)(expires - now).TotalSeconds);
    }

    public string CreateRefreshToken(int userId)
    {
        var now = DateTime.UtcNow;
        var claims = new List<Claim>
        {
            new(SystemClaims.UserIdClaimType, userId.ToString()),
            new(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(RefreshTokenTypeClaim, RefreshTokenTypeValue)
        };

        var token = new JwtSecurityToken(
            issuer: _issuer,
            audience: _audience,
            claims: claims,
            notBefore: now,
            expires: now.AddDays(_refreshDays),
            signingCredentials: new SigningCredentials(_signingKey, SecurityAlgorithms.HmacSha256));

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public int? ValidateRefreshToken(string refreshToken)
    {
        var parameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = _issuer,
            ValidateAudience = true,
            ValidAudience = _audience,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = _signingKey,
            ClockSkew = TimeSpan.FromSeconds(30)
        };

        try
        {
            var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };
            var principal = handler.ValidateToken(refreshToken, parameters, out _);

            if (principal.FindFirst(RefreshTokenTypeClaim)?.Value != RefreshTokenTypeValue)
            {
                return null; // an access token cannot be used as a refresh token
            }

            var uid = principal.FindFirst(SystemClaims.UserIdClaimType)?.Value;
            return int.TryParse(uid, out var id) ? id : null;
        }
        catch
        {
            return null;
        }
    }
}
