using Microsoft.AspNetCore.Mvc;
using Moq;
using Neurocorp.Api.Core.BusinessObjects.AdminUsers;
using Neurocorp.Api.Core.BusinessObjects.Common;
using Neurocorp.Api.Core.Interfaces.Services;
using Neurocorp.Api.Web.Controllers;

namespace Neurocorp.Api.Web.Tests;

/// <summary>
/// WP-41B: AdminUsersController unit tests — paging clamp (WP-30 cap 100 / default 25),
/// 201 shape, 204s, and caller-id plumbing for the G5 guard rails.
/// </summary>
public class AdminUsersControllerTests
{
    private readonly Mock<IAdminUserService> _service = new();
    private readonly Mock<ICurrentUserService> _currentUser = new();

    private AdminUsersController NewController(int? callerId = 99)
    {
        _currentUser.SetupGet(c => c.UserId).Returns(callerId);
        return new AdminUsersController(_service.Object, _currentUser.Object);
    }

    [Fact]
    public async Task GetUsers_ClampsPageSize_ToWp30Cap()
    {
        var controller = NewController();
        _service.Setup(s => s.GetPagedAsync(null, 1, 100))
            .ReturnsAsync(new PagedResult<AdminUserSummary>())
            .Verifiable();

        var result = await controller.GetUsers(search: null, page: 1, pageSize: 5000);

        Assert.IsType<OkObjectResult>(result);
        _service.Verify();
    }

    [Fact]
    public async Task GetUsers_DefaultsTo25PerPage_AndForwardsSearch()
    {
        var controller = NewController();
        _service.Setup(s => s.GetPagedAsync("dana", 1, 25))
            .ReturnsAsync(new PagedResult<AdminUserSummary>())
            .Verifiable();

        await controller.GetUsers(search: "dana", page: 0, pageSize: 0);

        _service.Verify();
    }

    [Fact]
    public async Task CreateUser_Returns201_PointingAtGetUser()
    {
        var controller = NewController();
        var summary = new AdminUserSummary { UserId = 42 };
        _service.Setup(s => s.CreateAsync(It.IsAny<AdminUserCreateRequest>())).ReturnsAsync(summary);

        var result = await controller.CreateUser(new AdminUserCreateRequest());

        var created = Assert.IsType<CreatedAtActionResult>(result);
        Assert.Equal(nameof(AdminUsersController.GetUser), created.ActionName);
        Assert.Equal(42, created.RouteValues!["id"]);
        Assert.Same(summary, created.Value);
    }

    [Fact]
    public async Task UpdateUser_PassesCallerId_Returns204()
    {
        var controller = NewController(callerId: 77);
        var request = new AdminUserUpdateRequest { IsActive = false };
        _service.Setup(s => s.UpdateAsync(5, request, 77)).Returns(Task.CompletedTask).Verifiable();

        var result = await controller.UpdateUser(5, request);

        Assert.IsType<NoContentResult>(result);
        _service.Verify();
    }

    [Fact]
    public async Task UpdateUser_WithoutAuthenticatedCaller_Returns401()
    {
        var controller = NewController(callerId: null);

        var result = await controller.UpdateUser(5, new AdminUserUpdateRequest());

        Assert.IsType<UnauthorizedResult>(result);
        _service.Verify(s => s.UpdateAsync(It.IsAny<int>(), It.IsAny<AdminUserUpdateRequest>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task ResetPassword_Returns204()
    {
        var controller = NewController();
        var request = new AdminUserResetPasswordRequest { TempPassword = "NewTemp99" };
        _service.Setup(s => s.ResetPasswordAsync(5, request)).Returns(Task.CompletedTask).Verifiable();

        var result = await controller.ResetPassword(5, request);

        Assert.IsType<NoContentResult>(result);
        _service.Verify();
    }
}
