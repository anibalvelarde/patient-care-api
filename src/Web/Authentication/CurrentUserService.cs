using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Neurocorp.Api.Core.Authorization;
using Neurocorp.Api.Core.Interfaces.Services;

namespace Neurocorp.Api.Web.Authentication;

/// <summary>
/// Reads the authenticated user's id from the current request principal. Backs both
/// audit stamping (via <c>ApplicationDbContext</c>) and the AuthController convenience endpoints.
/// </summary>
public class CurrentUserService : ICurrentUserService
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CurrentUserService(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public bool IsAuthenticated =>
        _httpContextAccessor.HttpContext?.User?.Identity?.IsAuthenticated ?? false;

    public int? UserId
    {
        get
        {
            var user = _httpContextAccessor.HttpContext?.User;
            if (user is null)
            {
                return null;
            }

            var raw = user.FindFirst(SystemClaims.UserIdClaimType)?.Value
                      ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            return int.TryParse(raw, out var id) ? id : null;
        }
    }
}
