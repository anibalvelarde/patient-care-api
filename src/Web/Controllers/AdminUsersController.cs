using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Neurocorp.Api.Core.BusinessObjects.AdminUsers;
using Neurocorp.Api.Core.BusinessObjects.Common;
using Neurocorp.Api.Core.Interfaces.Services;
using Neurocorp.Api.Web.Authorization;

namespace Neurocorp.Api.Web.Controllers;

/// <summary>
/// WP-41B: admin user management, SA-only v1 (contract: _contracts/admin-users-api.md).
/// Operator accounts only (G1). Reads ride Admin.Users.View (MGR + OWN); writes ride
/// Admin.Users.Manage (SYSADMIN-only) — both claims seeded since WP-17 (hash f82cab8c9efd),
/// so NO matrix change. Guard rails (G5) and validation surface as 400/404/409 through the
/// global exception handler.
/// </summary>
[ApiController]
[Route("api/admin/users")]
public class AdminUsersController : ControllerBase
{
    private const int DefaultPageSize = 25;

    private readonly IAdminUserService _adminUserService;
    private readonly ICurrentUserService _currentUser;

    public AdminUsersController(IAdminUserService adminUserService, ICurrentUserService currentUser)
    {
        _adminUserService = adminUserService;
        _currentUser = currentUser;
    }

    /// <summary>Paged operators-only list with server-side search (WP-30 rule / WP-21 envelope).</summary>
    [HttpGet]
    [Authorize(Policy = AuthPolicy.PermissionPrefix + Permissions.AdminUsersView)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(PagedResult<AdminUserSummary>))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetUsers(
        [FromQuery] string? search = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = DefaultPageSize)
    {
        var (safePage, safeSize) = PagingParams.Clamp(page, pageSize, DefaultPageSize);
        var result = await _adminUserService.GetPagedAsync(search, safePage, safeSize);
        return Ok(result);
    }

    /// <summary>Single operator account; 404 when missing OR holding no operator role (G1 scope).</summary>
    [HttpGet("{id:int}")]
    [Authorize(Policy = AuthPolicy.PermissionPrefix + Permissions.AdminUsersView)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(AdminUserSummary))]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetUser(int id)
    {
        var summary = await _adminUserService.GetByIdAsync(id);
        return Ok(summary);
    }

    /// <summary>Create an operator account: active, MustChangePassword = true (G3).</summary>
    [HttpPost]
    [Authorize(Policy = AuthPolicy.PermissionPrefix + Permissions.AdminUsersManage)]
    [ProducesResponseType(StatusCodes.Status201Created, Type = typeof(AdminUserSummary))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateUser([FromBody] AdminUserCreateRequest request)
    {
        var created = await _adminUserService.CreateAsync(request);
        return CreatedAtAction(nameof(GetUser), new { id = created.UserId }, created);
    }

    /// <summary>Replace operator-role set and/or set active status. Omitted field = unchanged.</summary>
    [HttpPut("{id:int}")]
    [Authorize(Policy = AuthPolicy.PermissionPrefix + Permissions.AdminUsersManage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateUser(int id, [FromBody] AdminUserUpdateRequest request)
    {
        var callerUserId = _currentUser.UserId;
        if (callerUserId is null)
        {
            return Unauthorized();
        }
        await _adminUserService.UpdateAsync(id, request, callerUserId.Value);
        return NoContent();
    }

    /// <summary>Admin-set temporary password + forced change at next login (G3).</summary>
    [HttpPost("{id:int}/reset-password")]
    [Authorize(Policy = AuthPolicy.PermissionPrefix + Permissions.AdminUsersManage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ResetPassword(int id, [FromBody] AdminUserResetPasswordRequest request)
    {
        await _adminUserService.ResetPasswordAsync(id, request);
        return NoContent();
    }
}
