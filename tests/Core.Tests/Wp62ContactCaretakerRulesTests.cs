using Microsoft.Extensions.Logging;
using Moq;
using Neurocorp.Api.Core.BusinessObjects.Patients;
using Neurocorp.Api.Core.Common;
using Neurocorp.Api.Core.Entities;
using Neurocorp.Api.Core.Exceptions;
using Neurocorp.Api.Core.Interfaces;
using Neurocorp.Api.Core.Interfaces.Repositories;
using Neurocorp.Api.Core.Services;

namespace Core.Tests;

// WP-62 (E2E F-5, F-6 + ruling Q-4): email clean-up/format on create AND edit; a patient keeps at
// least one primary caretaker (several allowed); the primary flag is editable on an existing link.
public class Wp62ContactCaretakerRulesTests
{
    // ---- EmailRules -----------------------------------------------------------------------------

    [Theory]
    [InlineData("a@b.co")]
    [InlineData("Mixed.Case+tag@clinic.example.pa")]
    [InlineData("x@y.z")]
    public void IsValidFormat_AcceptsRealAddresses(string email) => Assert.True(EmailRules.IsValidFormat(email));

    [Theory]
    [InlineData("not-an-email")]
    [InlineData("two@@b.c")]
    [InlineData("a@b")]
    [InlineData("a@b.")]
    [InlineData("@b.c")]
    [InlineData("a b@c.d")]
    [InlineData("Ann <a@b.c>")]
    public void IsValidFormat_RejectsJunk(string email) => Assert.False(EmailRules.IsValidFormat(email));

    [Fact]
    public void ValidateChange_LeavesUnchangedLegacyJunkAlone_AndAllowsClear()
    {
        EmailRules.ValidateChange(null, "legacy junk");          // omitted → unchanged
        EmailRules.ValidateChange("legacy junk", "legacy junk"); // echoed back unchanged
        EmailRules.ValidateChange("  ", "legacy junk");          // clear
        Assert.Throws<ArgumentException>(() => EmailRules.ValidateChange("still junk", "legacy junk"));
    }

    // ---- caretaker create / update -------------------------------------------------------------

    private static CaretakerProfileService Svc(
        Mock<IPatientCaretakerRepository>? pcRepo = null,
        Mock<ICaretakerProfileRepository>? profileRepo = null,
        Mock<IUserRepository>? userRepo = null,
        IPatientRepository? patientRepo = null,
        IUnitOfWork? uow = null)
        => new(Mock.Of<ILogger<CaretakerProfileService>>(),
            (profileRepo ?? new Mock<ICaretakerProfileRepository>()).Object,
            CaretakerRepo().Object,
            (userRepo ?? new Mock<IUserRepository>()).Object,
            Mock.Of<IUserRoleRepository>(r => r.AddAsync(It.IsAny<UserRole>()) == Task.FromResult(new UserRole { UserRoleId = 1 })),
            (pcRepo ?? new Mock<IPatientCaretakerRepository>()).Object,
            patientRepo: patientRepo, unitOfWork: uow);

    private static Mock<ICaretakerRepository> CaretakerRepo()
    {
        var repo = new Mock<ICaretakerRepository>();
        repo.Setup(r => r.AddAsync(It.IsAny<Caretaker>())).ReturnsAsync((Caretaker c) => { c.Id = 7; return c; });
        return repo;
    }

    [Fact]
    public async Task CaretakerCreate_MalformedEmail_Is400_AndNothingIsWritten()
    {
        var userRepo = new Mock<IUserRepository>();
        var svc = Svc(userRepo: userRepo);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => svc.CreateAsync(
            new CaretakerProfileRequest { FirstName = "Ana", LastName = "Ruiz", Email = "not-an-email", PhoneNumber = "555" }));

        Assert.Equal(EmailRules.InvalidMessage, ex.Message);
        userRepo.Verify(r => r.AddAsync(It.IsAny<User>()), Times.Never);
    }

    private static Mock<ICaretakerProfileRepository> ProfileOnFile(string? email)
    {
        var repo = new Mock<ICaretakerProfileRepository>();
        repo.Setup(r => r.GetByIdAsync(5)).ReturnsAsync(new CaretakerProfile { CaretakerId = 5, UserId = 50, Email = email });
        return repo;
    }

    [Theory]
    [InlineData(null)]          // omitted
    [InlineData("")]            // clear
    [InlineData("   ")]         // clear
    [InlineData("legacy junk")] // echoed back unchanged
    [InlineData(" new@ok.test ")]
    public async Task CaretakerUpdate_AcceptedEmails_ReachTheRepository(string? email)
    {
        var repo = ProfileOnFile("legacy junk");
        var request = new CaretakerProfileUpdateRequest { FirstName = "Ana", Email = email };

        Assert.True(await Svc(profileRepo: repo).UpdateAsync(5, request));
        repo.Verify(r => r.UpdateAsync(5, 50, request), Times.Once);
    }

    [Fact]
    public async Task CaretakerUpdate_ChangingToAMalformedEmail_Is400_AndNothingIsWritten()
    {
        var repo = ProfileOnFile("ok@clinic.test");

        await Assert.ThrowsAsync<ArgumentException>(() =>
            Svc(profileRepo: repo).UpdateAsync(5, new CaretakerProfileUpdateRequest { Email = "not-an-email" }));
        repo.Verify(r => r.UpdateAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CaretakerProfileUpdateRequest>()), Times.Never);
    }

    [Fact]
    public void UpdateRequest_EmailDefaultsToNull_SoOmittedMeansUnchanged()
    {
        Assert.Null(new CaretakerProfileUpdateRequest().Email);
        Assert.Null(new PatientProfileUpdateRequest().Email);
    }

    // ---- primary caretaker ---------------------------------------------------------------------

    private static PatientCaretaker Link(int caretakerId, bool primary, string? rel = "Mother")
        => new() { PatientId = 42, CaretakerId = caretakerId, PrimaryCaretaker = primary, RelationshipToPatient = rel };

    private static Mock<IPatientCaretakerRepository> Links(params PatientCaretaker[] links)
    {
        var repo = new Mock<IPatientCaretakerRepository>();
        repo.Setup(r => r.GetByPatientIdAsync(42)).ReturnsAsync(links.ToList());
        foreach (var l in links)
        {
            repo.Setup(r => r.GetByCompositeKeyAsync(42, l.CaretakerId)).ReturnsAsync(l);
        }
        return repo;
    }

    [Fact]
    public async Task Unlink_TheOnlyPrimary_Is409_AndNothingIsDeleted()
    {
        var repo = Links(Link(1, primary: true), Link(2, primary: false));

        var ex = await Assert.ThrowsAsync<ConflictException>(() => Svc(repo).UnlinkPatientAsync(1, 42));

        Assert.Equal(CaretakerProfileService.LastPrimaryMessage, ex.Message);
        repo.Verify(r => r.DeleteAsync(It.IsAny<PatientCaretaker>()), Times.Never);
    }

    [Fact]
    public async Task Unlink_TheOnlyLink_WhenItIsPrimary_Is409()
    {
        var repo = Links(Link(1, primary: true));
        await Assert.ThrowsAsync<ConflictException>(() => Svc(repo).UnlinkPatientAsync(1, 42));
    }

    [Fact]
    public async Task Unlink_OneOfTwoPrimaries_OrANonPrimary_IsAllowed()
    {
        var two = Links(Link(1, primary: true), Link(2, primary: true), Link(3, primary: false));
        Assert.True(await Svc(two).UnlinkPatientAsync(1, 42));
        Assert.True(await Svc(two).UnlinkPatientAsync(3, 42));
        two.Verify(r => r.DeleteAsync(It.IsAny<PatientCaretaker>()), Times.Exactly(2));
    }

    [Fact]
    public async Task Link_ToAPatientWithNoPrimary_IsStoredAsPrimary_EvenIfNotAsked()
    {
        var repo = Links(Link(1, primary: false)); // legacy: linked but no primary
        PatientCaretaker? added = null;
        repo.Setup(r => r.AddAsync(It.IsAny<PatientCaretaker>())).Callback<PatientCaretaker>(pc => added = pc).Returns(Task.CompletedTask);

        Assert.True(await Svc(repo).LinkPatientAsync(9, 42, isPrimary: false, relationship: "Father"));
        Assert.True(added!.PrimaryCaretaker);
    }

    [Fact]
    public async Task Link_WhenAPrimaryExists_KeepsTheRequestedFlag()
    {
        var repo = Links(Link(1, primary: true));
        PatientCaretaker? added = null;
        repo.Setup(r => r.AddAsync(It.IsAny<PatientCaretaker>())).Callback<PatientCaretaker>(pc => added = pc).Returns(Task.CompletedTask);

        await Svc(repo).LinkPatientAsync(9, 42, isPrimary: false, relationship: "Father");
        Assert.False(added!.PrimaryCaretaker);
    }

    [Fact]
    public async Task SetPrimary_NoSuchLink_ReturnsFalse()
    {
        Assert.False(await Svc(Links()).SetPrimaryAsync(1, 42, true));
    }

    [Fact]
    public async Task SetPrimary_UnflagTheOnlyPrimary_Is409_AndNothingIsWritten()
    {
        var repo = Links(Link(1, primary: true), Link(2, primary: false));

        await Assert.ThrowsAsync<ConflictException>(() => Svc(repo).SetPrimaryAsync(1, 42, false));
        repo.Verify(r => r.UpdateAsync(It.IsAny<PatientCaretaker>()), Times.Never);
    }

    [Fact]
    public async Task SetPrimary_PromoteThenDemote_MovesThePrimary()
    {
        var first = Link(1, primary: true);
        var second = Link(2, primary: false);
        var repo = Links(first, second);
        var svc = Svc(repo);

        Assert.True(await svc.SetPrimaryAsync(2, 42, true));
        Assert.True(second.PrimaryCaretaker);
        Assert.True(await svc.SetPrimaryAsync(1, 42, false)); // allowed now: #2 is primary
        Assert.False(first.PrimaryCaretaker);
        repo.Verify(r => r.UpdateAsync(It.IsAny<PatientCaretaker>()), Times.Exactly(2));
    }

    [Fact]
    public async Task SetPrimary_NoChange_WritesNothing()
    {
        var repo = Links(Link(1, primary: true));
        Assert.True(await Svc(repo).SetPrimaryAsync(1, 42, true));
        repo.Verify(r => r.UpdateAsync(It.IsAny<PatientCaretaker>()), Times.Never);
    }

    [Fact]
    public async Task MakeSelf_OnAPatientWithNoPrimary_IsPrimary_EvenIfNotAsked()
    {
        var repo = Links(Link(1, primary: false, rel: "Relative"));
        PatientCaretaker? added = null;
        repo.Setup(r => r.AddAsync(It.IsAny<PatientCaretaker>())).Callback<PatientCaretaker>(pc => added = pc).Returns(Task.CompletedTask);

        var patientRepo = new Mock<IPatientRepository>();
        patientRepo.Setup(r => r.GetByIdWithUserAsync(42)).ReturnsAsync(new Patient
        {
            Id = 42,
            User = new User { Id = 12500, FirstName = "Susana", LastName = "Saiz", PhoneNumber = "555" },
        });
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.ExecuteAsync(It.IsAny<Func<Task<CaretakerProfile>>>()))
           .Returns((Func<Task<CaretakerProfile>> op) => op());

        await Svc(repo, patientRepo: patientRepo.Object, uow: uow.Object).MakeSelfCaretakerAsync(42, isPrimary: false);

        Assert.True(added!.PrimaryCaretaker);
        Assert.Equal("Self", added.RelationshipToPatient);
    }
    // ---- patient create / update ---------------------------------------------------------------

    private static (PatientProfileService svc, Mock<IUserRepository> users, Mock<IPatientProfileRepository> profiles) PatientSvc(string? emailOnFile = null)
    {
        var users = new Mock<IUserRepository>();
        var profiles = new Mock<IPatientProfileRepository>();
        profiles.Setup(r => r.GetByIdAsync(3)).ReturnsAsync(new PatientProfile { PatientId = 3, UserId = 30, Email = emailOnFile, MedicalRecordNumber = "NC26-0001" });
        var svc = new PatientProfileService(Mock.Of<ILogger<PatientProfileService>>(), profiles.Object,
            Mock.Of<IPatientRepository>(), users.Object, Mock.Of<IUserRoleRepository>(),
            Mock.Of<IPatientCaretakerRepository>(), Mock.Of<ITherapySessionRepository>(), Mock.Of<IUnitOfWork>());
        return (svc, users, profiles);
    }

    [Fact]
    public async Task PatientCreate_MalformedEmail_Is400_AndNothingIsWritten()
    {
        var (svc, users, _) = PatientSvc();

        await Assert.ThrowsAsync<ArgumentException>(() => svc.CreateAsync(new PatientProfileRequest
        {
            FirstName = "Jane", LastName = "Doe", Email = "not-an-email", PhoneNumber = "555",
            Gender = "Female", DateOfBirth = DateTime.Today, Cedula = "8-123-456",
        }));
        users.Verify(r => r.AddAsync(It.IsAny<User>()), Times.Never);
    }

    [Fact]
    public async Task PatientUpdate_ChangingToAMalformedEmail_Is400_ButEchoingLegacyJunkIsFine()
    {
        var (svc, _, profiles) = PatientSvc(emailOnFile: "legacy junk");

        await Assert.ThrowsAsync<ArgumentException>(() =>
            svc.UpdateAsync(3, new PatientProfileUpdateRequest { ActiveStatus = true, Email = "other junk" }));
        profiles.Verify(r => r.UpdateAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<PatientProfileUpdateRequest>()), Times.Never);

        Assert.True(await svc.UpdateAsync(3, new PatientProfileUpdateRequest { ActiveStatus = true, Email = "legacy junk", PhoneNumber = "556" }));
        Assert.True(await svc.UpdateAsync(3, new PatientProfileUpdateRequest { ActiveStatus = true, Email = "" }));
        profiles.Verify(r => r.UpdateAsync(3, 30, It.IsAny<PatientProfileUpdateRequest>()), Times.Exactly(2));
    }
}
