using Moq;
using Xunit;
using Microsoft.AspNetCore.Mvc;
using Neurocorp.Api.Core.BusinessObjects.Sites;
using Neurocorp.Api.Core.Interfaces.Services;
using Neurocorp.Api.Web.Controllers;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Web.Tests.Controllers;

public class SitesControllerTests
{
    private readonly Mock<ISiteProfileService> _mockService;
    private readonly SitesController _controller;

    public SitesControllerTests()
    {
        var fakeLogger = Mock.Of<ILogger<SitesController>>();
        _mockService = new Mock<ISiteProfileService>();
        _controller = new SitesController(fakeLogger, _mockService.Object);
    }

    [Fact]
    public async Task GetAllSites_ReturnsOkResult_WithSites()
    {
        // Arrange
        var mockSites = new List<SiteProfile>
        {
            new() { SiteId = 1, SiteName = "Clinic A" },
            new() { SiteId = 2, SiteName = "Clinic B" }
        };
        _mockService.Setup(service => service.GetAllAsync()).ReturnsAsync(mockSites);

        // Act
        var result = await _controller.GetAllSites();

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var returnedSites = Assert.IsType<List<SiteProfile>>(okResult.Value);
        Assert.Equal(mockSites.Count, returnedSites.Count);
    }

    [Fact]
    public async Task GetSite_ReturnsNotFound_WhenSiteDoesNotExist()
    {
        // Arrange
        SiteProfile? nullSite = null;
        _mockService.Setup(service =>
            service.GetByIdAsync(It.IsAny<int>()))
        .ReturnsAsync(value: nullSite);

        // Act
        var result = await _controller.GetSite(1);

        // Assert
        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task GetSite_ReturnsOkResult_WithSite()
    {
        // Arrange
        var mockSite = new SiteProfile { SiteId = 1, SiteName = "Main Clinic" };
        _mockService.Setup(service => service.GetByIdAsync(1)).ReturnsAsync(mockSite);

        // Act
        var result = await _controller.GetSite(1);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var returnedSite = Assert.IsType<SiteProfile>(okResult.Value);
        Assert.NotNull(returnedSite);
    }

    [Fact]
    public async Task CreateSite_ReturnsCreatedAtActionResult()
    {
        // Arrange
        var request = new SiteProfileRequest
        {
            SiteName = "New Clinic",
            InceptionDate = new DateTime(2025, 1, 1)
        };
        var createdSite = new SiteProfile { SiteId = 5, SiteName = "New Clinic" };
        _mockService.Setup(service => service.CreateAsync(request)).ReturnsAsync(createdSite);

        // Act
        var result = await _controller.CreateSite(request);

        // Assert
        var createdResult = Assert.IsType<CreatedAtActionResult>(result);
        var returnedSite = Assert.IsType<SiteProfile>(createdResult.Value);
        Assert.Equal(5, returnedSite.SiteId);
    }

    [Fact]
    public async Task UpdateSite_ReturnsNoContent_WhenSiteExists()
    {
        // Arrange
        var updateRequest = new SiteProfileUpdateRequest { SiteName = "Updated Name" };
        _mockService.Setup(service => service.UpdateAsync(1, updateRequest)).ReturnsAsync(true);

        // Act
        var result = await _controller.UpdateSite(1, updateRequest);

        // Assert
        Assert.IsType<NoContentResult>(result);
    }

    [Fact]
    public async Task UpdateSite_ReturnsNotFound_WhenSiteDoesNotExist()
    {
        // Arrange
        var updateRequest = new SiteProfileUpdateRequest { SiteName = "Updated Name" };
        _mockService.Setup(service => service.UpdateAsync(99, updateRequest)).ReturnsAsync(false);

        // Act
        var result = await _controller.UpdateSite(99, updateRequest);

        // Assert
        Assert.IsType<NotFoundResult>(result);
    }

    // ── WP-42 (G1): noShowFeePct is SYSADMIN-role-gated — a CHANGED value needs the role
    // (or the god-mode wildcard, which only SYSADMIN holds); omitted/echoed-unchanged passes
    // any Admin.Sites.Manage holder. Same present-AND-different pattern as the WP-23/WP-40
    // field gates, but keyed on the ROLE, not a claim (owner ruling: no matrix change). ──

    private void UseCaller(params System.Security.Claims.Claim[] claims)
    {
        var identity = new System.Security.Claims.ClaimsIdentity(claims, authenticationType: "TestAuth");
        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext
            {
                User = new System.Security.Claims.ClaimsPrincipal(identity)
            }
        };
    }

    private static System.Security.Claims.Claim SysAdminRole()
        => new(System.Security.Claims.ClaimTypes.Role, Neurocorp.Api.Core.Authorization.SystemClaims.SystemAdminRoleName);

    private static System.Security.Claims.Claim ManagerRole()
        => new(System.Security.Claims.ClaimTypes.Role, "Manager");

    private static System.Security.Claims.Claim Wildcard()
        => new(Neurocorp.Api.Core.Authorization.SystemClaims.SystemClaimType,
               Neurocorp.Api.Core.Authorization.SystemClaims.FullAccessValue);

    private void SetupSiteOnFile(int id, decimal storedPct)
        => _mockService.Setup(s => s.GetByIdAsync(id))
            .ReturnsAsync(new SiteProfile { SiteId = id, SiteName = "Main", NoShowFeePct = storedPct });

    [Fact]
    public async Task UpdateSite_FeeChanged_WithoutSysAdminRole_Forbids_AndDoesNotUpdate()
    {
        SetupSiteOnFile(1, storedPct: 30m);
        UseCaller(ManagerRole());
        var request = new SiteProfileUpdateRequest { NoShowFeePct = 15m };

        var result = await _controller.UpdateSite(1, request);

        Assert.IsType<ForbidResult>(result);
        _mockService.Verify(s => s.UpdateAsync(It.IsAny<int>(), It.IsAny<SiteProfileUpdateRequest>()), Times.Never);
    }

    [Fact]
    public async Task UpdateSite_FeeChanged_WithSysAdminRole_Updates()
    {
        SetupSiteOnFile(1, storedPct: 30m);
        UseCaller(SysAdminRole());
        var request = new SiteProfileUpdateRequest { NoShowFeePct = 15m };
        _mockService.Setup(s => s.UpdateAsync(1, request)).ReturnsAsync(true);

        var result = await _controller.UpdateSite(1, request);

        Assert.IsType<NoContentResult>(result);
        _mockService.Verify(s => s.UpdateAsync(1, request), Times.Once);
    }

    [Fact]
    public async Task UpdateSite_FeeChanged_WithWildcardClaim_Updates()
    {
        SetupSiteOnFile(1, storedPct: 30m);
        UseCaller(Wildcard()); // ('System','FullAccess') — granted only to SYSADMIN
        var request = new SiteProfileUpdateRequest { NoShowFeePct = 0m }; // waiving the fee is a change too
        _mockService.Setup(s => s.UpdateAsync(1, request)).ReturnsAsync(true);

        var result = await _controller.UpdateSite(1, request);

        Assert.IsType<NoContentResult>(result);
    }

    [Fact]
    public async Task UpdateSite_FeeEchoedUnchanged_WithoutSysAdminRole_Passes()
    {
        SetupSiteOnFile(1, storedPct: 12.5m);
        UseCaller(ManagerRole());
        var request = new SiteProfileUpdateRequest { SiteName = "Renamed", NoShowFeePct = 12.5m }; // echo
        _mockService.Setup(s => s.UpdateAsync(1, request)).ReturnsAsync(true);

        var result = await _controller.UpdateSite(1, request);

        Assert.IsType<NoContentResult>(result);
        _mockService.Verify(s => s.UpdateAsync(1, request), Times.Once);
    }

    [Fact]
    public async Task UpdateSite_FeeOmitted_WithoutSysAdminRole_Passes_WithoutReadingSite()
    {
        UseCaller(ManagerRole());
        var request = new SiteProfileUpdateRequest { SiteName = "Renamed" }; // fee omitted
        _mockService.Setup(s => s.UpdateAsync(1, request)).ReturnsAsync(true);

        var result = await _controller.UpdateSite(1, request);

        Assert.IsType<NoContentResult>(result);
        _mockService.Verify(s => s.GetByIdAsync(It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task UpdateSite_FeeProvided_SiteMissing_ReturnsNotFound()
    {
        _mockService.Setup(s => s.GetByIdAsync(99)).ReturnsAsync((SiteProfile?)null);
        UseCaller(SysAdminRole());
        var request = new SiteProfileUpdateRequest { NoShowFeePct = 15m };

        var result = await _controller.UpdateSite(99, request);

        Assert.IsType<NotFoundResult>(result);
        _mockService.Verify(s => s.UpdateAsync(It.IsAny<int>(), It.IsAny<SiteProfileUpdateRequest>()), Times.Never);
    }

    [Fact]
    public async Task CreateSite_NonDefaultFee_WithoutSysAdminRole_Forbids_AndDoesNotCreate()
    {
        UseCaller(ManagerRole());
        var request = new SiteProfileRequest
        {
            SiteName = "New Clinic",
            InceptionDate = new DateTime(2026, 7, 1),
            NoShowFeePct = 50m
        };

        var result = await _controller.CreateSite(request);

        Assert.IsType<ForbidResult>(result);
        _mockService.Verify(s => s.CreateAsync(It.IsAny<SiteProfileRequest>()), Times.Never);
    }

    // WP-49 (BR1/D5): the platform default moved 30 → 100, so the privileged/unprivileged pivot
    // inverts with it — 100 is now the value any Admin.Sites.Manage holder may set, and 30 has
    // become a privileged (SYSADMIN-only) non-default. The gate tracks SiteDefaults.NoShowFeePct
    // rather than a literal, so this is the only place the change surfaces.
    [Theory]
    [InlineData(null)]   // omitted → server default
    [InlineData("100")]  // explicit default — not a privileged value
    public async Task CreateSite_DefaultFee_WithoutSysAdminRole_Creates(string? pct)
    {
        UseCaller(ManagerRole());
        var request = new SiteProfileRequest
        {
            SiteName = "New Clinic",
            InceptionDate = new DateTime(2026, 7, 1),
            NoShowFeePct = pct is null ? null : decimal.Parse(pct, System.Globalization.CultureInfo.InvariantCulture)
        };
        _mockService.Setup(s => s.CreateAsync(request))
            .ReturnsAsync(new SiteProfile { SiteId = 5, SiteName = "New Clinic", NoShowFeePct = 100m });

        var result = await _controller.CreateSite(request);

        Assert.IsType<CreatedAtActionResult>(result);
    }

    [Fact]
    public async Task CreateSite_OldThirtyPctDefault_WithoutSysAdminRole_Forbids()
    {
        // Guards the inversion above: 30 was the old default and is now a deliberate
        // per-site override, so a non-SYSADMIN may no longer set it.
        UseCaller(ManagerRole());
        var request = new SiteProfileRequest
        {
            SiteName = "New Clinic",
            InceptionDate = new DateTime(2026, 7, 1),
            NoShowFeePct = 30m
        };

        var result = await _controller.CreateSite(request);

        Assert.IsType<ForbidResult>(result);
        _mockService.Verify(s => s.CreateAsync(It.IsAny<SiteProfileRequest>()), Times.Never);
    }

    [Fact]
    public async Task CreateSite_NonDefaultFee_WithSysAdminRole_Creates()
    {
        UseCaller(SysAdminRole());
        var request = new SiteProfileRequest
        {
            SiteName = "New Clinic",
            InceptionDate = new DateTime(2026, 7, 1),
            NoShowFeePct = 50m
        };
        _mockService.Setup(s => s.CreateAsync(request))
            .ReturnsAsync(new SiteProfile { SiteId = 5, SiteName = "New Clinic", NoShowFeePct = 50m });

        var result = await _controller.CreateSite(request);

        Assert.IsType<CreatedAtActionResult>(result);
    }
}
