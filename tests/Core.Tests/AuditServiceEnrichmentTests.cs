using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using Neurocorp.Api.Core.BusinessObjects.Common;
using Neurocorp.Api.Core.BusinessObjects.Patients;
using Neurocorp.Api.Core.Interfaces;
using Neurocorp.Api.Core.Interfaces.Repositories;
using Neurocorp.Api.Core.Interfaces.Services;
using Neurocorp.Api.Core.Services;

namespace Core.Tests;

// WP-31 (U1): the service layer wires the resolver into its read paths — proving GetByIdAsync
// resolves the updater name end-to-end (and that the optional resolver stays a no-op when absent).
public class AuditServiceEnrichmentTests
{
    private static PatientProfileService Build(IPatientProfileRepository repo, IUserNameResolver? resolver)
    {
        return new PatientProfileService(
            Mock.Of<ILogger<PatientProfileService>>(),
            repo,
            Mock.Of<IPatientRepository>(),
            Mock.Of<IUserRepository>(),
            Mock.Of<IUserRoleRepository>(),
            Mock.Of<IPatientCaretakerRepository>(),
            Mock.Of<ITherapySessionRepository>(),
            Mock.Of<IUnitOfWork>(),
            resolver);
    }

    [Fact]
    public async Task GetByIdAsync_ResolvesUpdatedByName()
    {
        var profile = new PatientProfile
        {
            PatientId = 1,
            Audit = new AuditInfo { UpdatedByUserId = 5, UpdatedAt = new DateTime(2025, 6, 1) },
        };
        var repo = new Mock<IPatientProfileRepository>();
        repo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(profile);

        var resolver = new Mock<IUserNameResolver>();
        resolver.Setup(r => r.ResolveAsync(It.IsAny<IEnumerable<int>>()))
            .ReturnsAsync(new Dictionary<int, string> { [5] = "Doe, John" });

        var result = await Build(repo.Object, resolver.Object).GetByIdAsync(1);

        Assert.NotNull(result!.Audit);
        Assert.Equal("Doe, John", result.Audit!.UpdatedBy);
    }

    [Fact]
    public async Task GetByIdAsync_LeavesSystemDefault_WhenNoResolverInjected()
    {
        var profile = new PatientProfile
        {
            PatientId = 1,
            Audit = new AuditInfo { UpdatedByUserId = 5 },
        };
        var repo = new Mock<IPatientProfileRepository>();
        repo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(profile);

        var result = await Build(repo.Object, resolver: null).GetByIdAsync(1);

        Assert.Equal("System", result!.Audit!.UpdatedBy); // no-op without a resolver
    }
}
