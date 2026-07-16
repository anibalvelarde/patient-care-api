using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Moq;
using Xunit;
using Neurocorp.Api.Core.BusinessObjects.Auth;
using Neurocorp.Api.Core.Entities;
using Neurocorp.Api.Core.Interfaces.Repositories;
using Neurocorp.Api.Core.Interfaces.Services;
using Neurocorp.Api.Core.Services;

namespace Core.Tests;

/// <summary>
/// WP-32 (U4): the /auth/me payload carries the idle auto-logoff setting so every operator
/// can arm the client-side timer without an admin-gated Sites read. These tests pin the
/// sourcing rule (first/only Site row, 60 fallback).
/// </summary>
public class AuthServiceTests
{
    private static User BuildUser(int id) => new()
    {
        Id = id,
        FirstName = "Ana",
        LastName = "Lopez",
        Email = "ana@example.com",
        ActiveStatus = true
    };

    private static AuthService BuildService(Mock<IAuthRepository> authRepo, Mock<ISiteRepository> siteRepo)
    {
        return new AuthService(
            authRepo.Object,
            Mock.Of<IPasswordHasher>(),
            Mock.Of<ITokenService>(),
            siteRepo.Object);
    }

    private static Mock<IAuthRepository> BuildAuthRepo(int userId)
    {
        var authRepo = new Mock<IAuthRepository>();
        authRepo.Setup(r => r.GetByIdAsync(userId)).ReturnsAsync(BuildUser(userId));
        authRepo.Setup(r => r.GetEffectiveClaimsAsync(userId))
            .ReturnsAsync((IReadOnlyList<ClaimDto>)new List<ClaimDto>());
        authRepo.Setup(r => r.GetRoleNamesAsync(userId))
            .ReturnsAsync((IReadOnlyList<string>)new List<string>());
        return authRepo;
    }

    [Fact]
    public async Task GetCurrentUserAsync_IncludesIdleLogoffMinutes_FromFirstSiteRow()
    {
        // Arrange — two sites out of order; the lowest-Id row wins (single-site assumption).
        const int userId = 1;
        var authRepo = BuildAuthRepo(userId);
        var siteRepo = new Mock<ISiteRepository>();
        siteRepo.Setup(r => r.GetAllAsync()).ReturnsAsync((IReadOnlyList<Site>)new List<Site>
        {
            new() { Id = 2, SiteName = "Second", IdleLogoffMinutes = 30 },
            new() { Id = 1, SiteName = "First", IdleLogoffMinutes = 90 }
        });
        var svc = BuildService(authRepo, siteRepo);

        // Act
        var me = await svc.GetCurrentUserAsync(userId);

        // Assert
        Assert.NotNull(me);
        Assert.Equal(90, me!.IdleLogoffMinutes);
    }

    [Fact]
    public async Task GetCurrentUserAsync_DefaultsIdleLogoffMinutes_To60_WhenNoSites()
    {
        // Arrange
        const int userId = 1;
        var authRepo = BuildAuthRepo(userId);
        var siteRepo = new Mock<ISiteRepository>();
        siteRepo.Setup(r => r.GetAllAsync()).ReturnsAsync((IReadOnlyList<Site>)new List<Site>());
        var svc = BuildService(authRepo, siteRepo);

        // Act
        var me = await svc.GetCurrentUserAsync(userId);

        // Assert
        Assert.NotNull(me);
        Assert.Equal(60, me!.IdleLogoffMinutes);
    }

    [Fact]
    public async Task GetCurrentUserAsync_HonorsDisabledValue_Zero()
    {
        // Arrange — 0 means "disabled"; it must pass through, not be treated as "unset".
        const int userId = 1;
        var authRepo = BuildAuthRepo(userId);
        var siteRepo = new Mock<ISiteRepository>();
        siteRepo.Setup(r => r.GetAllAsync()).ReturnsAsync((IReadOnlyList<Site>)new List<Site>
        {
            new() { Id = 1, SiteName = "First", IdleLogoffMinutes = 0 }
        });
        var svc = BuildService(authRepo, siteRepo);

        // Act
        var me = await svc.GetCurrentUserAsync(userId);

        // Assert
        Assert.NotNull(me);
        Assert.Equal(0, me!.IdleLogoffMinutes);
    }

    [Fact]
    public async Task GetCurrentUserAsync_ReturnsNull_WhenUserMissing()
    {
        // Arrange
        const int userId = 999;
        var authRepo = new Mock<IAuthRepository>();
        authRepo.Setup(r => r.GetByIdAsync(userId)).ReturnsAsync((User?)null);
        var siteRepo = new Mock<ISiteRepository>();
        var svc = BuildService(authRepo, siteRepo);

        // Act
        var me = await svc.GetCurrentUserAsync(userId);

        // Assert
        Assert.Null(me);
    }
}
