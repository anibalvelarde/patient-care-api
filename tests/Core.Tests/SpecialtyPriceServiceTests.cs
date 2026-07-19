using System.Globalization;
using Microsoft.Extensions.Logging;
using Moq;
using Neurocorp.Api.Core.BusinessObjects.Lookups;
using Neurocorp.Api.Core.Entities;
using Neurocorp.Api.Core.Interfaces.Repositories;
using Neurocorp.Api.Core.Interfaces.Services;
using Neurocorp.Api.Core.Services;

namespace Core.Tests;

/// <summary>
/// WP-39: price-sheet history shape, append-only write semantics (dup → 409 at the wire), and
/// the ResolvePrice temporal rules WP-40's booking flow depends on. Regression-test-first: the
/// temporal cases (latest-≤-date wins, future rows ignored, DefaultAmount fallback, none) pin
/// the contract's resolution order.
/// </summary>
public class SpecialtyPriceServiceTests
{
    private readonly Mock<ISpecialtyPriceRepository> _repo = new();
    private readonly SpecialtyPriceService _service;

    public SpecialtyPriceServiceTests()
    {
        _service = new SpecialtyPriceService(Mock.Of<ILogger<SpecialtyPriceService>>(), _repo.Object);
    }

    private static DateOnly Iso(string date) =>
        DateOnly.ParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static SpecialtyDurationPrice Row(int duration, decimal amount, string effectiveFrom) =>
        new() { DurationMinutes = duration, Amount = amount, EffectiveFrom = Iso(effectiveFrom) };

    private SpecialtyType Seed(int id, decimal? defaultAmount, bool offeredOnSite, params SpecialtyDurationPrice[] rows)
    {
        var specialty = new SpecialtyType
        {
            Id = id,
            Abbreviation = "TL",
            Name = "Language Therapy",
            DefaultAmount = defaultAmount,
            OfferedOnSite = offeredOnSite,
        };
        foreach (var row in rows) specialty.DurationPrices.Add(row);
        _repo.Setup(r => r.GetWithPricesAsync(id)).ReturnsAsync(specialty);
        return specialty;
    }

    // ── GET history ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetHistory_UnknownSpecialty_ReturnsNull()
    {
        _repo.Setup(r => r.GetWithPricesAsync(99)).ReturnsAsync((SpecialtyType?)null);

        Assert.Null(await _service.GetHistoryAsync(99));
    }

    [Fact]
    public async Task GetHistory_CarriesHeaderFields_AndNewestFirstPerDuration()
    {
        Seed(3, 40.00m, true,
            Row(30, 22.00m, "2020-01-01"),
            Row(30, 25.00m, "2025-06-01"),
            Row(60, 45.00m, "2024-01-01"));

        var history = await _service.GetHistoryAsync(3);

        Assert.NotNull(history);
        Assert.Equal(3, history.SpecialtyTypeId);
        Assert.Equal(40.00m, history.DefaultAmount);
        Assert.True(history.OfferedOnSite);
        // Ordered by duration, then newest-first within the duration.
        Assert.Equal(3, history.Prices.Count);
        Assert.Equal((30, Iso("2025-06-01")), (history.Prices[0].DurationMinutes, history.Prices[0].EffectiveFrom));
        Assert.Equal((30, Iso("2020-01-01")), (history.Prices[1].DurationMinutes, history.Prices[1].EffectiveFrom));
        Assert.Equal((60, Iso("2024-01-01")), (history.Prices[2].DurationMinutes, history.Prices[2].EffectiveFrom));
    }

    [Fact]
    public async Task GetHistory_IsCurrent_MarksLatestEffectiveRowPerDuration_FutureRowsNotCurrent()
    {
        Seed(3, null, false,
            Row(30, 22.00m, "2020-01-01"),   // superseded
            Row(30, 25.00m, "2025-06-01"),   // current (latest <= today)
            Row(30, 30.00m, "2999-01-01"),   // scheduled future — NOT current
            Row(90, 80.00m, "2999-01-01"));  // future-only duration — nothing current

        var history = await _service.GetHistoryAsync(3);

        Assert.NotNull(history);
        var current = history.Prices.Where(p => p.IsCurrent).ToList();
        var only = Assert.Single(current);
        Assert.Equal(30, only.DurationMinutes);
        Assert.Equal(Iso("2025-06-01"), only.EffectiveFrom);
    }

    // ── PUT append ─────────────────────────────────────────────────────────────────

    private static SpecialtyPricesPutRequest Request(params (int Duration, decimal Amount, string From)[] rows) =>
        new()
        {
            Prices = rows.Select(r => new SpecialtyPriceAppendRow
            {
                DurationMinutes = r.Duration,
                Amount = r.Amount,
                EffectiveFrom = Iso(r.From),
            }).ToList(),
        };

    [Fact]
    public async Task Append_HappyPath_PersistsRows_AndReturnsSuccess()
    {
        Seed(3, null, false, Row(30, 22.00m, "2020-01-01"));
        IReadOnlyCollection<SpecialtyDurationPrice>? captured = null;
        _repo.Setup(r => r.AddRangeAsync(It.IsAny<IReadOnlyCollection<SpecialtyDurationPrice>>()))
            .Callback<IReadOnlyCollection<SpecialtyDurationPrice>>(rows => captured = rows)
            .Returns(Task.CompletedTask);

        var result = await _service.AppendAsync(3,
            Request((60, 45.00m, "2026-08-01"), (30, 25.00m, "2026-08-01")));

        Assert.Equal(PriceAppendResult.Success, result);
        Assert.NotNull(captured);
        Assert.Equal(2, captured.Count);
        Assert.All(captured, r => Assert.Equal(3, r.SpecialtyTypeId));
        var r60 = captured.Single(r => r.DurationMinutes == 60);
        Assert.Equal(45.00m, r60.Amount);
        Assert.Equal(Iso("2026-08-01"), r60.EffectiveFrom);
    }

    [Fact]
    public async Task Append_UnknownSpecialty_ReturnsNotFound_AndWritesNothing()
    {
        _repo.Setup(r => r.GetWithPricesAsync(99)).ReturnsAsync((SpecialtyType?)null);

        var result = await _service.AppendAsync(99, Request((60, 45.00m, "2026-08-01")));

        Assert.Equal(PriceAppendResult.SpecialtyNotFound, result);
        _repo.Verify(r => r.AddRangeAsync(It.IsAny<IReadOnlyCollection<SpecialtyDurationPrice>>()), Times.Never);
    }

    [Fact]
    public async Task Append_DuplicateOfRowOnFile_Returns409Semantics_AndWritesNothing()
    {
        Seed(3, null, false, Row(60, 45.00m, "2026-08-01"));

        // Same (duration, effectiveFrom) pair, even with a DIFFERENT amount → append-only 409;
        // a correction must use a different effective date.
        var result = await _service.AppendAsync(3, Request((60, 47.00m, "2026-08-01")));

        Assert.Equal(PriceAppendResult.DuplicateRow, result);
        _repo.Verify(r => r.AddRangeAsync(It.IsAny<IReadOnlyCollection<SpecialtyDurationPrice>>()), Times.Never);
    }

    [Fact]
    public async Task Append_DuplicateWithinRequest_Returns409Semantics_AndWritesNothing()
    {
        Seed(3, null, false);

        var result = await _service.AppendAsync(3,
            Request((60, 45.00m, "2026-08-01"), (60, 50.00m, "2026-08-01")));

        Assert.Equal(PriceAppendResult.DuplicateRow, result);
        _repo.Verify(r => r.AddRangeAsync(It.IsAny<IReadOnlyCollection<SpecialtyDurationPrice>>()), Times.Never);
    }

    [Fact]
    public async Task Append_SameDurationDifferentDate_IsAllowed()
    {
        Seed(3, null, false, Row(60, 45.00m, "2026-08-01"));

        var result = await _service.AppendAsync(3, Request((60, 47.00m, "2026-09-01")));

        Assert.Equal(PriceAppendResult.Success, result);
    }

    // ── ResolvePrice (WP-40's dependency — contract resolution order) ──────────────

    [Fact]
    public async Task Resolve_LatestRowOnOrBeforeSessionDate_Wins()
    {
        Seed(3, 40.00m, false,
            Row(60, 42.00m, "2026-01-01"),
            Row(60, 45.00m, "2026-07-01"),
            Row(60, 50.00m, "2026-10-01"));

        // Session on 2026-08-15: the 2026-07-01 row is the latest <= session date.
        var resolution = await _service.ResolvePriceAsync(3, 60, Iso("2026-08-15"));

        Assert.Equal(45.00m, resolution.Amount);
        Assert.Equal(AmountSource.DurationPrice, resolution.Source);
    }

    [Fact]
    public async Task Resolve_EffectiveFromEqualToSessionDate_Counts()
    {
        Seed(3, null, false, Row(60, 45.00m, "2026-07-01"));

        var resolution = await _service.ResolvePriceAsync(3, 60, Iso("2026-07-01"));

        Assert.Equal(45.00m, resolution.Amount);
        Assert.Equal(AmountSource.DurationPrice, resolution.Source);
    }

    [Fact]
    public async Task Resolve_OnlyFutureRowsForThatDate_FallsBackToDefaultAmount()
    {
        Seed(3, 40.00m, false, Row(60, 50.00m, "2026-10-01"));

        // Session predates every 60-min row → DefaultAmount fallback + "not configured" signal.
        var resolution = await _service.ResolvePriceAsync(3, 60, Iso("2026-08-15"));

        Assert.Equal(40.00m, resolution.Amount);
        Assert.Equal(AmountSource.DefaultAmount, resolution.Source);
    }

    [Fact]
    public async Task Resolve_OtherDurationsRowsDoNotLeak()
    {
        Seed(3, 40.00m, false, Row(30, 25.00m, "2026-01-01"));

        var resolution = await _service.ResolvePriceAsync(3, 60, Iso("2026-08-15"));

        Assert.Equal(AmountSource.DefaultAmount, resolution.Source);
        Assert.Equal(40.00m, resolution.Amount);
    }

    [Fact]
    public async Task Resolve_NoRowAndNoDefault_ReturnsNone()
    {
        Seed(3, null, false);

        var resolution = await _service.ResolvePriceAsync(3, 60, Iso("2026-08-15"));

        Assert.Null(resolution.Amount);
        Assert.Equal(AmountSource.None, resolution.Source);
    }

    [Fact]
    public async Task Resolve_UnknownSpecialty_ReturnsNone()
    {
        _repo.Setup(r => r.GetWithPricesAsync(99)).ReturnsAsync((SpecialtyType?)null);

        var resolution = await _service.ResolvePriceAsync(99, 60, Iso("2026-08-15"));

        Assert.Equal(AmountSource.None, resolution.Source);
        Assert.Null(resolution.Amount);
    }

    [Fact]
    public async Task Resolve_RetroactiveRowDoesNotAffectEarlierSessionDates()
    {
        // A row effective 2026-07-01 exists; resolving for a session dated BEFORE it must not
        // use it (booked sessions never reprice — resolution is by the session's own date).
        Seed(3, 40.00m, false, Row(60, 45.00m, "2026-07-01"));

        var resolution = await _service.ResolvePriceAsync(3, 60, Iso("2026-06-30"));

        Assert.Equal(AmountSource.DefaultAmount, resolution.Source);
        Assert.Equal(40.00m, resolution.Amount);
    }
}
