namespace Neurocorp.Api.Core.BusinessObjects.Auth;

/// <summary>API-facing response returned by login / refresh.</summary>
public class AuthTokenResponse
{
    public string AccessToken { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
    public string TokenType { get; set; } = "Bearer";
    public int ExpiresIn { get; set; }
    public bool MustChangePassword { get; set; }
}
