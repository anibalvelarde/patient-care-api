using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Neurocorp.Api.Web.Authentication;

/// <summary>
/// Single source of truth for JWT settings so the token issuer (<see cref="JwtTokenService"/>)
/// and the bearer validation in <c>Startup</c> can never drift apart. The signing key is read
/// from the <c>JWT_SIGNING_KEY</c> environment variable (preferred) or <c>Jwt:SigningKey</c>
/// configuration. It is never committed; in non-development environments a missing key fails fast.
/// </summary>
public static class JwtConfig
{
    // Used ONLY when running in the Development environment without a configured key,
    // so local dev works out of the box. Long enough for HMAC-SHA256 (>= 256 bits).
    public const string DevelopmentFallbackKey =
        "DEV-ONLY-INSECURE-JWT-SIGNING-KEY-DO-NOT-USE-IN-PRODUCTION-0123456789";

    public static string Issuer(IConfiguration config) => config["Jwt:Issuer"] ?? "neurocorp-api";

    public static string Audience(IConfiguration config) => config["Jwt:Audience"] ?? "neurocorp-clients";

    public static int AccessTokenMinutes(IConfiguration config) =>
        int.TryParse(config["Jwt:AccessTokenMinutes"], out var m) ? m : 60;

    public static int RefreshTokenDays(IConfiguration config) =>
        int.TryParse(config["Jwt:RefreshTokenDays"], out var d) ? d : 14;

    public static string ResolveSigningKey(IConfiguration config, IHostEnvironment env)
    {
        var key = Environment.GetEnvironmentVariable("JWT_SIGNING_KEY") ?? config["Jwt:SigningKey"];
        if (!string.IsNullOrWhiteSpace(key))
        {
            return key;
        }

        if (!env.IsDevelopment())
        {
            throw new InvalidOperationException(
                "JWT signing key is not configured. Set the JWT_SIGNING_KEY environment variable " +
                "(or Jwt:SigningKey) before starting the API in a non-development environment.");
        }

        return DevelopmentFallbackKey;
    }
}
