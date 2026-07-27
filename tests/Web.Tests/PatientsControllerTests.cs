using Moq;
using Xunit;
using Microsoft.AspNetCore.Mvc;
using Neurocorp.Api.Core.BusinessObjects.Patients;
using Neurocorp.Api.Core.Interfaces.Services;
using Neurocorp.Api.Web.Controllers;
using System.Collections.Generic;
using System.Threading.Tasks;
using Neurocorp.Api.Core.BusinessObjects.Common;
using Neurocorp.Api.Core.BusinessObjects.Sessions;
using Neurocorp.Api.Core.Interfaces.Repositories;
using FluentAssertions;
using System.Linq;
using System.Security.Claims;
using Castle.Core.Logging;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Neurocorp.Api.Core.Authorization;
using Neurocorp.Api.Web.Authorization;

namespace Web.Tests.Controllers;

public class PatientsControllerTests
{
    private readonly Mock<IPatientProfileService> _mockPatientProfileService;
    private readonly Mock<IHandleSessionEvent> _mockSessionEventHandler;
    private readonly Mock<ISessionEventRepository> _mockSessionEventRepository;
    private readonly Mock<IPatientMergeService> _mockPatientMergeService;
    private readonly PatientsController _controller;

    public PatientsControllerTests()
    {
        var fakeLogger = Mock.Of<ILogger<PatientsController>>();
        _mockPatientProfileService = new Mock<IPatientProfileService>();
        _mockSessionEventHandler = new Mock<IHandleSessionEvent>();
        _mockSessionEventRepository = new Mock<ISessionEventRepository>();
        _mockPatientMergeService = new Mock<IPatientMergeService>();
        _controller = new PatientsController(fakeLogger, _mockPatientProfileService.Object, _mockSessionEventHandler.Object, _mockSessionEventRepository.Object, _mockPatientMergeService.Object);
    }

    // WP-30 (U2): GET /api/patients is paged-by-default (BREAKING — was a bare array).
    [Fact]
    public async Task GetPatients_ReturnsOkResult_WithPagedEnvelope()
    {
        // Arrange
        var paged = new PagedResult<PatientProfile>
        {
            Items = new List<PatientProfile> { new(), new() },
            Page = 1,
            PageSize = 30,
            TotalCount = 869,
        };
        _mockPatientProfileService
            .Setup(service => service.GetPagedAsync("ana", true, 1, 30))
            .ReturnsAsync(paged);

        // Act
        var result = await _controller.GetPatients(search: "ana", isActive: true, page: 1, pageSize: 30);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var envelope = Assert.IsType<PagedResult<PatientProfile>>(okResult.Value);
        Assert.Equal(869, envelope.TotalCount);
        Assert.Equal(2, envelope.Items.Count);
    }

    // WP-30: lenient clamping via the shared PagingParams helper — bad paging never 400s,
    // and an oversized pageSize can't re-create the unpaged endpoint.
    [Theory]
    [InlineData(0, 0, 1, 30)]        // zero/negative fall back to defaults
    [InlineData(-5, 100000, 1, 100)] // pageSize ceiling = 100
    [InlineData(3, 50, 3, 50)]       // sane values pass through
    public async Task GetPatients_ClampsPagingParams(int page, int pageSize, int expectedPage, int expectedSize)
    {
        _mockPatientProfileService
            .Setup(s => s.GetPagedAsync(null, null, expectedPage, expectedSize))
            .ReturnsAsync(new PagedResult<PatientProfile>())
            .Verifiable();

        var result = await _controller.GetPatients(page: page, pageSize: pageSize);

        Assert.IsType<OkObjectResult>(result);
        _mockPatientProfileService.Verify();
    }

    // WP-30: typeahead — q is required (min length 1), results capped at 20 server-side.
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task LookupPatients_MissingOrBlankQuery_Returns400(string? q)
    {
        var result = await _controller.LookupPatients(q);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, objectResult.StatusCode);
        _mockPatientProfileService.Verify(
            s => s.LookupAsync(It.IsAny<string>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task LookupPatients_ValidQuery_ReturnsItems_WithCap20()
    {
        var items = new List<PatientLookupItem>
        {
            new() { PatientId = 1, PatientName = "Anderson, Amy", MedicalRecordNumber = "L24-0001" },
        };
        _mockPatientProfileService
            .Setup(s => s.LookupAsync("and", 20))
            .ReturnsAsync(items)
            .Verifiable();

        var result = await _controller.LookupPatients("and");

        var okResult = Assert.IsType<OkObjectResult>(result);
        var returned = Assert.IsAssignableFrom<IReadOnlyList<PatientLookupItem>>(okResult.Value);
        Assert.Single(returned);
        _mockPatientProfileService.Verify();
    }

    // ── WP-21 (F1): paged session-history endpoints ──────────────────────────────────

    [Fact]
    public async Task GetPatientSessionHistory_ReturnsOk_WithPagedResult()
    {
        // WP-35 addendum: the endpoint returns the derived SessionHistoryPagedResult
        // (shared paged shape + envelope Totals).
        var envelope = new SessionHistoryPagedResult
        {
            Items = new List<PatientSessionHistorySummary> { new() { PatientId = 7 } },
            Page = 1,
            PageSize = 30,
            TotalCount = 1,
            Totals = new SessionHistoryTotals { SessionCount = 3, GrossAmount = 350m },
        };
        _mockPatientProfileService
            .Setup(s => s.GetSessionHistoryAsync("doe", 1, 30))
            .ReturnsAsync(envelope);

        var result = await _controller.GetPatientSessionHistory("doe", 1, 30);

        var okResult = Assert.IsType<OkObjectResult>(result);
        var returned = Assert.IsType<SessionHistoryPagedResult>(okResult.Value);
        returned.Should().BeSameAs(envelope);
        _mockPatientProfileService.Verify(s => s.GetSessionHistoryAsync("doe", 1, 30), Times.Once);
    }

    [Fact]
    public async Task GetPatientSessionHistory_ClampsPagingParams()
    {
        _mockPatientProfileService
            .Setup(s => s.GetSessionHistoryAsync(null, It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync(new SessionHistoryPagedResult());

        // page 0 → 1; pageSize 9999 → the 100 ceiling; pageSize 0 → the 30 default.
        await _controller.GetPatientSessionHistory(null, page: 0, pageSize: 9999);
        _mockPatientProfileService.Verify(s => s.GetSessionHistoryAsync(null, 1, 100), Times.Once);

        await _controller.GetPatientSessionHistory(null, page: 3, pageSize: 0);
        _mockPatientProfileService.Verify(s => s.GetSessionHistoryAsync(null, 3, 30), Times.Once);
    }

    [Fact]
    public async Task GetPatientSessions_ReturnsOk_WithPagedResult()
    {
        var envelope = new PagedResult<SessionEvent>
        {
            Items = new List<SessionEvent> { new() { SessionId = 42 } },
            Page = 2,
            PageSize = 25,
            TotalCount = 132,
        };
        _mockSessionEventRepository
            .Setup(r => r.GetByPatientIdAsync(7, 2, 25, null, null, null, null))
            .ReturnsAsync(envelope);

        var result = await _controller.GetPatientSessions(7, page: 2, pageSize: 25);

        var okResult = Assert.IsType<OkObjectResult>(result);
        var returned = Assert.IsType<PagedResult<SessionEvent>>(okResult.Value);
        returned.Should().BeSameAs(envelope);
        _mockSessionEventRepository.Verify(r => r.GetByPatientIdAsync(7, 2, 25, null, null, null, null), Times.Once);
    }

    [Fact]
    public async Task GetPatientSessions_ClampsPagingParams_AndForwardsFilters()
    {
        _mockSessionEventRepository
            .Setup(r => r.GetByPatientIdAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<bool?>(), It.IsAny<string?>(), It.IsAny<DateOnly?>(), It.IsAny<DateOnly?>()))
            .ReturnsAsync(new PagedResult<SessionEvent>());

        await _controller.GetPatientSessions(7, page: -5, pageSize: 400, isDiscovery: true, status: "Completed");

        _mockSessionEventRepository.Verify(r => r.GetByPatientIdAsync(7, 1, 100, true, "Completed", null, null), Times.Once);
    }

    // WP-35 (SH-3 support): additive from/to date-range params must reach the repository;
    // omitted params forward as null (unchanged behavior).
    [Fact]
    public async Task GetPatientSessions_ForwardsFromAndToDateRange()
    {
        _mockSessionEventRepository
            .Setup(r => r.GetByPatientIdAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<bool?>(), It.IsAny<string?>(), It.IsAny<DateOnly?>(), It.IsAny<DateOnly?>()))
            .ReturnsAsync(new PagedResult<SessionEvent>());

        var from = new DateOnly(2026, 3, 1);
        var to = new DateOnly(2026, 7, 1);
        await _controller.GetPatientSessions(7, page: 1, pageSize: 25, from: from, to: to);
        _mockSessionEventRepository.Verify(r => r.GetByPatientIdAsync(7, 1, 25, null, null, from, to), Times.Once);

        // from-only and to-only forward independently.
        await _controller.GetPatientSessions(7, page: 1, pageSize: 25, from: from);
        _mockSessionEventRepository.Verify(r => r.GetByPatientIdAsync(7, 1, 25, null, null, from, null), Times.Once);

        await _controller.GetPatientSessions(7, page: 1, pageSize: 25, to: to);
        _mockSessionEventRepository.Verify(r => r.GetByPatientIdAsync(7, 1, 25, null, null, null, to), Times.Once);
    }

    [Fact]
    public async Task GetPatient_ReturnsNotFound_WhenPatientDoesNotExist()
    {
        // Arrange
        PatientProfile? nullPatient = null;
        _mockPatientProfileService.Setup(service =>
            service.GetByIdAsync(It.IsAny<int>()))
        .ReturnsAsync(value: nullPatient);

        // Act
        var result = await _controller.GetPatient(1);

        // Assert
        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task GetPatient_ReturnsOkResult_WithPatient()
    {
        // Arrange
        var mockPatient = new PatientProfile { /* properties */ };
        _mockPatientProfileService.Setup(service => service.GetByIdAsync(1)).ReturnsAsync(mockPatient);

        // Act
        var result = await _controller.GetPatient(1);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var returnedPatient = Assert.IsType<PatientProfile>(okResult.Value);
        Assert.NotNull(returnedPatient);
    }

    [Fact]
    public async Task GetPatient_PastDueEvents_Returns_Ok_Result_WithSessionEvents()
    {
        // Arrange
        var targetPatientId = 1;
        var expectedSessionIds = new[] { 1, 3 };
        _mockPatientProfileService
            .Setup(x => x.GetByIdAsync(targetPatientId))
            .ReturnsAsync(Mock.Of<PatientProfile>(pp => pp.PatientId == targetPatientId));
        // WP-29: the controller asks for the patient-scoped set — the handler owns the filter.
        _mockSessionEventHandler
            .Setup(x => x.GetPastDueByPatientAsync(targetPatientId))
            .ReturnsAsync( [
                new SessionEvent() {SessionId =1, PatientId = 1},
                new SessionEvent() {SessionId = 3, PatientId = 1}]);

        // Act
        var result = await _controller.GetPastDueSessions(targetPatientId);

        // Assert
        result.Should().BeOfType<OkObjectResult>()
            .Which.Value.Should().BeAssignableTo<PatientPastDueInfo>()
            .Which.Should().NotBeNull();
        var patientInfo = ((OkObjectResult)result).Value as PatientPastDueInfo;
        patientInfo.Should().NotBeNull();
        patientInfo!.PartyType.Should().NotBeNull();
        patientInfo.Party.Should().NotBeNull();
        patientInfo.PastDueSessions.Should().BeGreaterOrEqualTo(0);
        patientInfo.PastDueTotalAmount.Should().BeGreaterOrEqualTo(0);
        patientInfo.AmountPaidSoFar.Should().BeGreaterOrEqualTo(0);
        patientInfo.Delinquency.Should().NotBeNull()
            .And.HaveCount(expectedSessionIds.Length)
            .And.OnlyContain(session => expectedSessionIds.Contains(session.SessionId));
    }    

    [Fact]
    public async Task GetAllPastDuePatients_ReturnsOk_WithPastDuePatients()
    {
        // Arrange
        var pastDuePatients = new List<PatientPastDueInfo>
        {
            new() { Party = Mock.Of<PatientProfile>(p => p.PatientId == 1), PastDueSessions = 2, PastDueTotalAmount = 100m, AmountPaidSoFar = 20m },
            new() { Party = Mock.Of<PatientProfile>(p => p.PatientId == 2), PastDueSessions = 1, PastDueTotalAmount = 50m, AmountPaidSoFar = 0m }
        };
        _mockSessionEventHandler
            .Setup(x => x.GetAllPatientsPastDueAsync())
            .ReturnsAsync(pastDuePatients);

        // Act
        var result = await _controller.GetAllPastDuePatients();

        // Assert
        result.Should().BeOfType<OkObjectResult>()
            .Which.Value.Should().BeAssignableTo<IEnumerable<PatientPastDueInfo>>()
            .Which.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetAllPastDuePatients_ReturnsOk_WithEmptyList()
    {
        // Arrange
        _mockSessionEventHandler
            .Setup(x => x.GetAllPatientsPastDueAsync())
            .ReturnsAsync(new List<PatientPastDueInfo>());

        // Act
        var result = await _controller.GetAllPastDuePatients();

        // Assert
        result.Should().BeOfType<OkObjectResult>()
            .Which.Value.Should().BeAssignableTo<IEnumerable<PatientPastDueInfo>>()
            .Which.Should().BeEmpty();
    }

    [Fact]
    public async Task GetPatient_PastDueEvents_Returns_NO_Result_WithSessionEvents()
    {
        // Arrange
        var targetPatientId = 3;
        _mockPatientProfileService
            .Setup(x => x.GetByIdAsync(targetPatientId))
            .ReturnsAsync(Mock.Of<PatientProfile>(pp => pp.PatientId == targetPatientId));
        // WP-29: patient 3 has no past-due sessions — the scoped call returns empty.
        _mockSessionEventHandler
            .Setup(x => x.GetPastDueByPatientAsync(targetPatientId))
            .ReturnsAsync([]);

        // Act
        var result = await _controller.GetPastDueSessions(targetPatientId);

        // Assert
        result.Should().BeOfType<OkObjectResult>()
            .Which.Value.Should().BeAssignableTo<PatientPastDueInfo>()
            .Which.Should().NotBeNull();
        var patientInfo = ((OkObjectResult)result).Value as PatientPastDueInfo;
        patientInfo!.PartyType.Should().NotBeNull();
        patientInfo.Party.Should().NotBeNull();
        patientInfo.PastDueSessions.Should().BeGreaterOrEqualTo(0);
        patientInfo.PastDueTotalAmount.Should().BeGreaterOrEqualTo(0);
        patientInfo.AmountPaidSoFar.Should().BeGreaterOrEqualTo(0);
        patientInfo.Delinquency.Should().NotBeNull()
            .And.HaveCount(0);
    }

    // ── WP-22 (F2): duplicate-patient merge ───────────────────────────────────────────

    [Fact]
    public async Task PreviewPatientMerge_ReturnsOk_WithServicePreview()
    {
        var request = new PatientMergeRequest { SurvivorPatientId = 1, EliminatedPatientId = 2 };
        var preview = new PatientMergePreview { Blockers = new List<string> { "blocked" } };
        _mockPatientMergeService.Setup(s => s.PreviewAsync(request)).ReturnsAsync(preview);

        var result = await _controller.PreviewPatientMerge(request);

        var okResult = Assert.IsType<OkObjectResult>(result);
        okResult.Value.Should().BeSameAs(preview);
        _mockPatientMergeService.Verify(s => s.PreviewAsync(request), Times.Once);
        _mockPatientMergeService.Verify(s => s.MergeAsync(It.IsAny<PatientMergeRequest>()), Times.Never);
    }

    [Fact]
    public async Task MergePatients_ReturnsOk_WithServiceResult()
    {
        var request = new PatientMergeRequest { SurvivorPatientId = 1, EliminatedPatientId = 2 };
        var mergeResult = new PatientMergeResult { SurvivorPatientId = 1, EliminatedPatientId = 2, MergeLogId = 42 };
        _mockPatientMergeService.Setup(s => s.MergeAsync(request)).ReturnsAsync(mergeResult);

        var result = await _controller.MergePatients(request);

        var okResult = Assert.IsType<OkObjectResult>(result);
        okResult.Value.Should().BeSameAs(mergeResult);
        _mockPatientMergeService.Verify(s => s.MergeAsync(request), Times.Once);
    }

    // ── WP-23 (F7): hasSenadisDiscount field-level edit gate on PUT ──────────────────
    // Changing the stored flag needs Patients.SenadisDiscount.Edit; omitted/null/echoed-
    // unchanged values pass so FD clients that round-trip the profile keep working.

    private void UseCaller(params (string Type, string Value)[] claims)
    {
        var identity = new ClaimsIdentity(
            claims.Select(c => new Claim(c.Type, c.Value)), authenticationType: "TestAuth");
        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) }
        };
    }

    private void SetupPatientOnFile(int id, bool hasSenadisDiscount)
    {
        _mockPatientProfileService
            .Setup(s => s.GetByIdAsync(id))
            .ReturnsAsync(new PatientProfile { PatientId = id, PatientName = "P", HasSenadisDiscount = hasSenadisDiscount });
        _mockPatientProfileService
            .Setup(s => s.UpdateAsync(id, It.IsAny<PatientProfileUpdateRequest>()))
            .ReturnsAsync(true);
    }

    [Fact]
    public async Task UpdatePatient_SenadisChanged_WithoutClaim_ReturnsForbid_AndDoesNotUpdate()
    {
        SetupPatientOnFile(7, hasSenadisDiscount: false);
        UseCaller((SystemClaims.PermissionClaimType, Permissions.PatientsEdit)); // FD-like: Edit but not the gate

        var result = await _controller.UpdatePatient(7,
            new PatientProfileUpdateRequest { HasSenadisDiscount = true });

        Assert.IsType<ForbidResult>(result);
        _mockPatientProfileService.Verify(
            s => s.UpdateAsync(It.IsAny<int>(), It.IsAny<PatientProfileUpdateRequest>()), Times.Never);
    }

    [Fact]
    public async Task UpdatePatient_SenadisChanged_WithClaim_Delegates()
    {
        SetupPatientOnFile(7, hasSenadisDiscount: false);
        UseCaller((SystemClaims.PermissionClaimType, Permissions.PatientsSenadisDiscountEdit));

        var result = await _controller.UpdatePatient(7,
            new PatientProfileUpdateRequest { HasSenadisDiscount = true });

        Assert.IsType<NoContentResult>(result);
        _mockPatientProfileService.Verify(
            s => s.UpdateAsync(7, It.Is<PatientProfileUpdateRequest>(r => r.HasSenadisDiscount == true)), Times.Once);
    }

    [Fact]
    public async Task UpdatePatient_SenadisChanged_WithWildcard_Delegates()
    {
        SetupPatientOnFile(7, hasSenadisDiscount: true);
        UseCaller((SystemClaims.SystemClaimType, SystemClaims.FullAccessValue)); // SYSADMIN

        var result = await _controller.UpdatePatient(7,
            new PatientProfileUpdateRequest { HasSenadisDiscount = false });

        Assert.IsType<NoContentResult>(result);
    }

    [Fact]
    public async Task UpdatePatient_SenadisEchoedUnchanged_WithoutClaim_Passes()
    {
        SetupPatientOnFile(7, hasSenadisDiscount: true);
        UseCaller((SystemClaims.PermissionClaimType, Permissions.PatientsEdit));

        var result = await _controller.UpdatePatient(7,
            new PatientProfileUpdateRequest { HasSenadisDiscount = true }); // echo, not a change

        Assert.IsType<NoContentResult>(result);
    }

    [Fact]
    public async Task UpdatePatient_SenadisOmitted_WithoutClaim_Passes()
    {
        SetupPatientOnFile(7, hasSenadisDiscount: true);
        UseCaller((SystemClaims.PermissionClaimType, Permissions.PatientsEdit));

        var result = await _controller.UpdatePatient(7,
            new PatientProfileUpdateRequest { FirstName = "Renamed" }); // null flag = unchanged

        Assert.IsType<NoContentResult>(result);
    }

    // ── WP-37 (SEN-1/G4): senadisExpirationDate rides the SAME edit gate as the flag ──
    // One claim (Patients.SenadisDiscount.Edit) governs both SENADIS fields; omitted/null/
    // echoed-unchanged values pass so FD clients that round-trip the profile keep working.

    private void SetupPatientOnFileWithExpiry(int id, bool hasSenadisDiscount, DateTime? expiry)
    {
        _mockPatientProfileService
            .Setup(s => s.GetByIdAsync(id))
            .ReturnsAsync(new PatientProfile
            {
                PatientId = id,
                PatientName = "P",
                HasSenadisDiscount = hasSenadisDiscount,
                SenadisExpirationDate = expiry,
            });
        _mockPatientProfileService
            .Setup(s => s.UpdateAsync(id, It.IsAny<PatientProfileUpdateRequest>()))
            .ReturnsAsync(true);
    }

    [Fact]
    public async Task UpdatePatient_SenadisExpirationChanged_WithoutClaim_ReturnsForbid_AndDoesNotUpdate()
    {
        SetupPatientOnFileWithExpiry(7, hasSenadisDiscount: true, expiry: null);
        UseCaller((SystemClaims.PermissionClaimType, Permissions.PatientsEdit)); // FD-like: Edit but not the gate

        var result = await _controller.UpdatePatient(7,
            new PatientProfileUpdateRequest { SenadisExpirationDate = new DateTime(2027, 6, 30) });

        Assert.IsType<ForbidResult>(result);
        _mockPatientProfileService.Verify(
            s => s.UpdateAsync(It.IsAny<int>(), It.IsAny<PatientProfileUpdateRequest>()), Times.Never);
    }

    [Fact]
    public async Task UpdatePatient_SenadisExpirationChanged_WithClaim_Delegates()
    {
        SetupPatientOnFileWithExpiry(7, hasSenadisDiscount: true, expiry: new DateTime(2026, 12, 31));
        UseCaller((SystemClaims.PermissionClaimType, Permissions.PatientsSenadisDiscountEdit));

        var result = await _controller.UpdatePatient(7,
            new PatientProfileUpdateRequest { SenadisExpirationDate = new DateTime(2027, 6, 30) });

        Assert.IsType<NoContentResult>(result);
        _mockPatientProfileService.Verify(
            s => s.UpdateAsync(7, It.Is<PatientProfileUpdateRequest>(
                r => r.SenadisExpirationDate == new DateTime(2027, 6, 30))), Times.Once);
    }

    [Fact]
    public async Task UpdatePatient_SenadisExpirationChanged_WithWildcard_Delegates()
    {
        SetupPatientOnFileWithExpiry(7, hasSenadisDiscount: true, expiry: null);
        UseCaller((SystemClaims.SystemClaimType, SystemClaims.FullAccessValue)); // SYSADMIN

        var result = await _controller.UpdatePatient(7,
            new PatientProfileUpdateRequest { SenadisExpirationDate = new DateTime(2027, 6, 30) });

        Assert.IsType<NoContentResult>(result);
    }

    [Fact]
    public async Task UpdatePatient_SenadisExpirationEchoedUnchanged_WithoutClaim_Passes()
    {
        SetupPatientOnFileWithExpiry(7, hasSenadisDiscount: true, expiry: new DateTime(2027, 6, 30));
        UseCaller((SystemClaims.PermissionClaimType, Permissions.PatientsEdit));

        var result = await _controller.UpdatePatient(7,
            new PatientProfileUpdateRequest { SenadisExpirationDate = new DateTime(2027, 6, 30) }); // echo, not a change

        Assert.IsType<NoContentResult>(result);
    }

    [Fact]
    public async Task UpdatePatient_SenadisExpirationOmitted_WithoutClaim_Passes()
    {
        // Null/omitted = unchanged (same pattern as the flag) — the stored expiry survives.
        SetupPatientOnFileWithExpiry(7, hasSenadisDiscount: true, expiry: new DateTime(2027, 6, 30));
        UseCaller((SystemClaims.PermissionClaimType, Permissions.PatientsEdit));

        var result = await _controller.UpdatePatient(7,
            new PatientProfileUpdateRequest { FirstName = "Renamed" }); // null expiry = unchanged

        Assert.IsType<NoContentResult>(result);
    }

    // ── WP-24 (F3/F4): requiresDiscovery field-level edit gate on PUT ─────────────────
    // Changing the stored waiver flag needs Patients.RequiresDiscovery.Edit; omitted/null/
    // echoed-unchanged values pass so FD clients that round-trip the profile keep working.

    private void SetupPatientOnFileWithDiscovery(int id, bool requiresDiscovery)
    {
        _mockPatientProfileService
            .Setup(s => s.GetByIdAsync(id))
            .ReturnsAsync(new PatientProfile { PatientId = id, PatientName = "P", RequiresDiscovery = requiresDiscovery });
        _mockPatientProfileService
            .Setup(s => s.UpdateAsync(id, It.IsAny<PatientProfileUpdateRequest>()))
            .ReturnsAsync(true);
    }

    [Fact]
    public async Task UpdatePatient_RequiresDiscoveryChanged_WithoutClaim_ReturnsForbid_AndDoesNotUpdate()
    {
        SetupPatientOnFileWithDiscovery(7, requiresDiscovery: true);
        UseCaller((SystemClaims.PermissionClaimType, Permissions.PatientsEdit)); // FD-like: Edit but not the gate

        var result = await _controller.UpdatePatient(7,
            new PatientProfileUpdateRequest { RequiresDiscovery = false });

        Assert.IsType<ForbidResult>(result);
        _mockPatientProfileService.Verify(
            s => s.UpdateAsync(It.IsAny<int>(), It.IsAny<PatientProfileUpdateRequest>()), Times.Never);
    }

    [Fact]
    public async Task UpdatePatient_RequiresDiscoveryChanged_WithClaim_Delegates()
    {
        SetupPatientOnFileWithDiscovery(7, requiresDiscovery: true);
        UseCaller((SystemClaims.PermissionClaimType, Permissions.PatientsRequiresDiscoveryEdit));

        var result = await _controller.UpdatePatient(7,
            new PatientProfileUpdateRequest { RequiresDiscovery = false });

        Assert.IsType<NoContentResult>(result);
        _mockPatientProfileService.Verify(
            s => s.UpdateAsync(7, It.Is<PatientProfileUpdateRequest>(r => r.RequiresDiscovery == false)), Times.Once);
    }

    [Fact]
    public async Task UpdatePatient_RequiresDiscoveryChanged_WithWildcard_Delegates()
    {
        SetupPatientOnFileWithDiscovery(7, requiresDiscovery: false);
        UseCaller((SystemClaims.SystemClaimType, SystemClaims.FullAccessValue)); // SYSADMIN

        var result = await _controller.UpdatePatient(7,
            new PatientProfileUpdateRequest { RequiresDiscovery = true });

        Assert.IsType<NoContentResult>(result);
    }

    [Fact]
    public async Task UpdatePatient_RequiresDiscoveryEchoedUnchanged_WithoutClaim_Passes()
    {
        SetupPatientOnFileWithDiscovery(7, requiresDiscovery: true);
        UseCaller((SystemClaims.PermissionClaimType, Permissions.PatientsEdit));

        var result = await _controller.UpdatePatient(7,
            new PatientProfileUpdateRequest { RequiresDiscovery = true }); // echo, not a change

        Assert.IsType<NoContentResult>(result);
    }

    [Fact]
    public async Task UpdatePatient_RequiresDiscoveryOmitted_WithoutClaim_Passes()
    {
        SetupPatientOnFileWithDiscovery(7, requiresDiscovery: false);
        UseCaller((SystemClaims.PermissionClaimType, Permissions.PatientsEdit));

        var result = await _controller.UpdatePatient(7,
            new PatientProfileUpdateRequest { FirstName = "Renamed" }); // null flag = unchanged

        Assert.IsType<NoContentResult>(result);
    }

    [Fact]
    public async Task UpdatePatient_UnknownPatient_Returns400()
    {
        _mockPatientProfileService.Setup(s => s.GetByIdAsync(99)).ReturnsAsync((PatientProfile?)null);
        UseCaller((SystemClaims.PermissionClaimType, Permissions.PatientsEdit));

        var result = await _controller.UpdatePatient(99, new PatientProfileUpdateRequest());

        var problem = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, problem.StatusCode);
    }
}
