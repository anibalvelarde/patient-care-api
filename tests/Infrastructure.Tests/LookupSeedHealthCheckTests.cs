using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;
using Neurocorp.Api.Core.Authorization;
using Neurocorp.Api.Core.Entities;
using Neurocorp.Api.Core.Services;
using Neurocorp.Api.Infrastructure.Configurations.HealthChecks;
using Neurocorp.Api.Infrastructure.Data;
using Xunit;

namespace Infrastructure.Tests;

/// <summary>
/// WP-55 B-3: the lookupSeed health check flags when the live AppointmentStatus / RoleType rows
/// disagree with the SessionStatus / RoleTaxonomy constants (the B-2d "7 = Owner vs FrontDesk"
/// drift class), and stays Healthy when they agree.
/// </summary>
public class LookupSeedHealthCheckTests
{
    private static ApplicationDbContext NewContext(string name) =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(name).Options);

    private static void SeedCorrect(ApplicationDbContext ctx)
    {
        foreach (var (id, statusName) in SessionStatus.IdToName)
            ctx.Set<AppointmentStatus>().Add(new AppointmentStatus { Id = id, Name = statusName, Abbreviation = "X" });

        ctx.Set<RoleType>().AddRange(
            new RoleType { Id = RoleTaxonomy.TherapistRoleId, Name = "Therapist", Abbreviation = "THER" },
            new RoleType { Id = RoleTaxonomy.PatientRoleId, Name = "Patient", Abbreviation = "PATI" },
            new RoleType { Id = RoleTaxonomy.CaretakerRoleId, Name = "Caretaker", Abbreviation = "CARE" });
        ctx.SaveChanges();
    }

    private static LookupSeedHealthCheck Check(ApplicationDbContext ctx) =>
        new(ctx, NullLogger<LookupSeedHealthCheck>.Instance);

    [Fact]
    public async Task Healthy_When_SeedMatchesConstants()
    {
        using var ctx = NewContext(nameof(Healthy_When_SeedMatchesConstants));
        SeedCorrect(ctx);

        var result = await Check(ctx).CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    [Fact]
    public async Task Unhealthy_When_AppointmentStatusRenamed()
    {
        using var ctx = NewContext(nameof(Unhealthy_When_AppointmentStatusRenamed));
        SeedCorrect(ctx);
        // Rename the InTherapy row (id 7) — the exact "id means something different now" drift.
        var row = ctx.Set<AppointmentStatus>().Single(r => r.Id == SessionStatus.InTherapy);
        row.Name = "Owner";
        ctx.SaveChanges();

        var result = await Check(ctx).CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Contains("AppointmentStatus id 7", result.Description);
    }

    [Fact]
    public async Task Unhealthy_When_IdentityRoleIdReassigned()
    {
        using var ctx = NewContext(nameof(Unhealthy_When_IdentityRoleIdReassigned));
        SeedCorrect(ctx);
        var row = ctx.Set<RoleType>().Single(r => r.Id == RoleTaxonomy.CaretakerRoleId);
        row.Name = "SomethingElse";
        ctx.SaveChanges();

        var result = await Check(ctx).CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
    }

    [Fact]
    public async Task Unhealthy_When_StatusRowMissing()
    {
        using var ctx = NewContext(nameof(Unhealthy_When_StatusRowMissing));
        SeedCorrect(ctx);
        ctx.Set<AppointmentStatus>().Remove(ctx.Set<AppointmentStatus>().Single(r => r.Id == SessionStatus.NoShow));
        ctx.SaveChanges();

        var result = await Check(ctx).CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
    }
}
