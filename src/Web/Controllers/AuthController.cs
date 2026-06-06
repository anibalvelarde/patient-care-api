using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Neurocorp.Api.Core.BusinessObjects.Auth;
using Neurocorp.Api.Core.Interfaces.Services;
using Neurocorp.Api.Web.Authorization;

namespace Neurocorp.Api.Web.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;
    private readonly ICurrentUserService _currentUser;

    public AuthController(IAuthService authService, ICurrentUserService currentUser)
    {
        _authService = authService;
        _currentUser = currentUser;
    }

    /// <summary>Exchange email + password for an access token and refresh token.</summary>
    [AllowAnonymous]
    [HttpPost("login")]
    [ProducesResponseType(typeof(AuthTokenResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        var result = await _authService.LoginAsync(request);
        if (!result.Succeeded)
        {
            return Problem(statusCode: StatusCodes.Status401Unauthorized,
                title: "Authentication failed", detail: result.Error);
        }
        return Ok(ToResponse(result));
    }

    /// <summary>Exchange a valid refresh token for a fresh access + refresh token pair.</summary>
    [AllowAnonymous]
    [HttpPost("refresh")]
    [ProducesResponseType(typeof(AuthTokenResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Refresh([FromBody] RefreshTokenRequest request)
    {
        var result = await _authService.RefreshAsync(request?.RefreshToken ?? string.Empty);
        if (!result.Succeeded)
        {
            return Problem(statusCode: StatusCodes.Status401Unauthorized,
                title: "Token refresh failed", detail: result.Error);
        }
        return Ok(ToResponse(result));
    }

    /// <summary>Return the authenticated caller's identity, roles, and effective claims.</summary>
    [HttpGet("me")]
    [ProducesResponseType(typeof(CurrentUserResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Me()
    {
        var userId = _currentUser.UserId;
        if (userId is null)
        {
            return Unauthorized();
        }

        var me = await _authService.GetCurrentUserAsync(userId.Value);
        return me is null ? NotFound() : Ok(me);
    }

    /// <summary>Change the caller's password (also clears the must-change-password flag).</summary>
    [HttpPost("change-password")]
    [ProducesResponseType(typeof(AuthTokenResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request)
    {
        var userId = _currentUser.UserId;
        if (userId is null)
        {
            return Unauthorized();
        }

        var result = await _authService.ChangePasswordAsync(userId.Value, request);
        if (!result.Succeeded)
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest,
                title: "Password change failed", detail: result.Error);
        }
        return Ok(ToResponse(result));
    }

    /// <summary>
    /// Demonstrates claims-based authorization. Requires the "System.Diagnostics" permission,
    /// which is granted to no role by default — so only a SystemAdmin (via the
    /// ('System','FullAccess') wildcard) succeeds here. Used by the 1B verification doc to prove
    /// the wildcard works and that an authenticated non-SA user is correctly denied (403).
    /// </summary>
    [HttpGet("admin-check")]
    [Authorize(Policy = "claim:Permission:System.Diagnostics")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public IActionResult AdminCheck()
    {
        return Ok(new
        {
            message = "Access granted: you hold the System.FullAccess wildcard (SystemAdmin).",
            userId = _currentUser.UserId
        });
    }

    private static AuthTokenResponse ToResponse(AuthResult result) => new()
    {
        AccessToken = result.AccessToken!,
        RefreshToken = result.RefreshToken!,
        ExpiresIn = result.ExpiresInSeconds,
        MustChangePassword = result.MustChangePassword
    };
}
