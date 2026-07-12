using System.Security.Claims;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Neurocorp.Api.Core.Authorization;
using Neurocorp.Api.Core.BusinessObjects.Common;
using Neurocorp.Api.Core.BusinessObjects.Sessions;
using Neurocorp.Api.Web.Authorization;
using Xunit;

namespace Neurocorp.Api.Web.Tests.Authorization;

/// <summary>
/// WP-17 ProviderAmount confidentiality. Proves the field-level enforcement in two units that together
/// guarantee "FrontDesk never receives ProviderAmount in the JSON":
///   1. <see cref="ProviderAmountResultFilter"/> nulls the field for principals lacking
///      Appointments.ProviderAmount (FD), and preserves it for holders (AM/MGR) + the wildcard.
///   2. A null ProviderAmount is omitted from the serialized JSON entirely (the DTO's
///      [JsonIgnore(WhenWritingNull)]).
/// Driven at the filter/serialization level rather than over HTTP because the leak is field presence,
/// not status code — and the integration host's in-memory DB returns empty session lists.
/// </summary>
public class ProviderAmountShapingTests
{
    private const string ProviderAmountClaim = "Appointments.ProviderAmount";

    private static ClaimsPrincipal Principal(params Claim[] claims) =>
        new(new ClaimsIdentity(claims, authenticationType: "test"));

    // FD holds Appointments.View/Book but NOT Appointments.ProviderAmount (per the matrix).
    private static ClaimsPrincipal FrontDesk() =>
        Principal(
            new Claim(SystemClaims.PermissionClaimType, "Appointments.View"),
            new Claim(SystemClaims.PermissionClaimType, "Appointments.Book"));

    private static ClaimsPrincipal Manager() =>
        Principal(
            new Claim(SystemClaims.PermissionClaimType, "Appointments.View"),
            new Claim(SystemClaims.PermissionClaimType, ProviderAmountClaim));

    private static ClaimsPrincipal Wildcard() =>
        Principal(new Claim(SystemClaims.SystemClaimType, SystemClaims.FullAccessValue));

    private static async Task<ObjectResult> RunFilter(ClaimsPrincipal user, object payload)
    {
        var http = new DefaultHttpContext { User = user };
        var actionContext = new ActionContext(http, new RouteData(), new ActionDescriptor());
        var result = new ObjectResult(payload);
        var executing = new ResultExecutingContext(
            actionContext, new List<IFilterMetadata>(), result, controller: new object());
        var executed = new ResultExecutedContext(
            actionContext, new List<IFilterMetadata>(), result, controller: new object());

        await new ProviderAmountResultFilter()
            .OnResultExecutionAsync(executing, () => Task.FromResult(executed));

        return (ObjectResult)executing.Result;
    }

    // ── HasPermission extension (mirrors PermissionAuthorizationHandler) ──────────────
    [Fact]
    public void HasPermission_true_for_explicit_claim() =>
        Manager().HasPermission(ProviderAmountClaim).Should().BeTrue();

    [Fact]
    public void HasPermission_true_for_wildcard() =>
        Wildcard().HasPermission(ProviderAmountClaim).Should().BeTrue();

    [Fact]
    public void HasPermission_false_when_claim_absent() =>
        FrontDesk().HasPermission(ProviderAmountClaim).Should().BeFalse();

    [Fact]
    public void HasPermission_false_for_null_principal() =>
        ((ClaimsPrincipal?)null).HasPermission(ProviderAmountClaim).Should().BeFalse();

    // ── filter redaction ─────────────────────────────────────────────────────────────
    [Fact]
    public async Task FrontDesk_single_SessionEvent_is_redacted()
    {
        var result = await RunFilter(FrontDesk(), new SessionEvent { ProviderAmount = 123.45m });
        ((SessionEvent)result.Value!).ProviderAmount.Should().BeNull();
    }

    [Fact]
    public async Task FrontDesk_SessionEvent_list_is_redacted()
    {
        var payload = new List<SessionEvent>
        {
            new() { ProviderAmount = 10m },
            new() { ProviderAmount = 20m },
        };
        var result = await RunFilter(FrontDesk(), payload);
        ((IEnumerable<SessionEvent>)result.Value!).Should().OnlyContain(e => e.ProviderAmount == null);
    }

    [Fact]
    public async Task Manager_keeps_ProviderAmount()
    {
        var result = await RunFilter(Manager(), new SessionEvent { ProviderAmount = 99m });
        ((SessionEvent)result.Value!).ProviderAmount.Should().Be(99m);
    }

    [Fact]
    public async Task Wildcard_keeps_ProviderAmount()
    {
        var result = await RunFilter(Wildcard(), new SessionEvent { ProviderAmount = 99m });
        ((SessionEvent)result.Value!).ProviderAmount.Should().Be(99m);
    }

    [Fact]
    public async Task NonSessionEvent_payload_is_untouched_even_for_frontdesk()
    {
        var payload = new { Secret = "keep-me" };
        var result = await RunFilter(FrontDesk(), payload);
        result.Value.Should().BeSameAs(payload);
    }

    // ── WP-21: the PagedResult<SessionEvent> envelope must be shaped too ─────────────
    // The WP-21 endpoints are reachable via Patients.View, so principals like ACCT/FD hit them
    // WITHOUT Appointments.ProviderAmount — the envelope arm is what keeps the payout confidential.

    private static PagedResult<SessionEvent> Envelope(params decimal?[] providerAmounts) => new()
    {
        Items = providerAmounts.Select(pa => new SessionEvent { ProviderAmount = pa }).ToList(),
        Page = 2,
        PageSize = 25,
        TotalCount = 132,
    };

    // ACCT-shaped principal: holds Patients.View (reaches the WP-21 endpoints) but no ProviderAmount claim.
    private static ClaimsPrincipal PatientsViewOnly() =>
        Principal(new Claim(SystemClaims.PermissionClaimType, "Patients.View"));

    [Fact]
    public async Task FrontDesk_paged_envelope_items_are_redacted()
    {
        var result = await RunFilter(FrontDesk(), Envelope(10m, 20m));
        ((PagedResult<SessionEvent>)result.Value!).Items.Should().OnlyContain(e => e.ProviderAmount == null);
    }

    [Fact]
    public async Task PatientsView_only_principal_paged_envelope_is_redacted()
    {
        var result = await RunFilter(PatientsViewOnly(), Envelope(50m));
        ((PagedResult<SessionEvent>)result.Value!).Items.Should().OnlyContain(e => e.ProviderAmount == null);
    }

    [Fact]
    public async Task Manager_paged_envelope_keeps_ProviderAmount()
    {
        var result = await RunFilter(Manager(), Envelope(99m));
        ((PagedResult<SessionEvent>)result.Value!).Items.Single().ProviderAmount.Should().Be(99m);
    }

    [Fact]
    public async Task Wildcard_paged_envelope_keeps_ProviderAmount()
    {
        var result = await RunFilter(Wildcard(), Envelope(99m));
        ((PagedResult<SessionEvent>)result.Value!).Items.Single().ProviderAmount.Should().Be(99m);
    }

    [Fact]
    public async Task Paged_envelope_metadata_survives_redaction()
    {
        var result = await RunFilter(FrontDesk(), Envelope(10m, 20m));
        var paged = (PagedResult<SessionEvent>)result.Value!;
        paged.Page.Should().Be(2);
        paged.PageSize.Should().Be(25);
        paged.TotalCount.Should().Be(132);
        paged.Items.Should().HaveCount(2);
    }

    [Fact]
    public async Task Paged_envelope_of_other_type_is_untouched()
    {
        var payload = new PagedResult<string> { Items = ["keep-me"], Page = 1, PageSize = 30, TotalCount = 1 };
        var result = await RunFilter(FrontDesk(), payload);
        result.Value.Should().BeSameAs(payload);
        ((PagedResult<string>)result.Value!).Items.Single().Should().Be("keep-me");
    }

    // ── serialization: null ⇒ property omitted entirely ──────────────────────────────
    [Fact]
    public void Null_ProviderAmount_is_omitted_from_json()
    {
        var json = JsonSerializer.Serialize(
            new SessionEvent { ProviderAmount = null }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        json.ToLowerInvariant().Should().NotContain("provideramount");
    }

    [Fact]
    public void Present_ProviderAmount_is_serialized()
    {
        var json = JsonSerializer.Serialize(
            new SessionEvent { ProviderAmount = 50m }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        json.ToLowerInvariant().Should().Contain("provideramount");
    }
}
