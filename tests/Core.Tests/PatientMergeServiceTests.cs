using Moq;
using Microsoft.Extensions.Logging;
using Neurocorp.Api.Core.BusinessObjects.Patients;
using Neurocorp.Api.Core.Entities;
using Neurocorp.Api.Core.Exceptions;
using Neurocorp.Api.Core.Interfaces;
using Neurocorp.Api.Core.Interfaces.Repositories;
using Neurocorp.Api.Core.Interfaces.Services;
using Neurocorp.Api.Core.Services;
using FluentAssertions;

namespace Core.Tests;

public class PatientMergeServiceTests
{
    private readonly Mock<IPatientMergeRepository> _repo = new();
    private readonly Mock<ICurrentUserService> _currentUser = new();

    private PatientMergeService CreateService()
    {
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.ExecuteAsync(It.IsAny<Func<Task<(PatientMergeResult, int)>>>()))
           .Returns((Func<Task<(PatientMergeResult, int)>> op) => op());
        _currentUser.SetupGet(c => c.UserId).Returns(7);
        return new PatientMergeService(
            Mock.Of<ILogger<PatientMergeService>>(), _repo.Object, _currentUser.Object, uow.Object);
    }

    private static Patient MakePatient(int patientId, int userId, string first, string last,
        string? mrn = null, string? cedula = null, DateTime? dob = null, string? gender = null,
        string? notes = null, bool active = true)
    {
        return new Patient
        {
            Id = patientId,
            User = new User { Id = userId, FirstName = first, LastName = last, ActiveStatus = active },
            MedicalRecordNumber = mrn,
            Cedula = cedula,
            DateOfBirth = dob,
            Gender = gender,
            Notes = notes,
        };
    }

    private static PatientCaretaker MakeLink(int patientId, int caretakerId, bool primary = false,
        bool synthetic = false, int caretakerUserId = 0)
    {
        return new PatientCaretaker
        {
            Id = patientId * 1000 + caretakerId,
            PatientId = patientId,
            CaretakerId = caretakerId,
            PrimaryCaretaker = primary,
            Caretaker = new Caretaker
            {
                Id = caretakerId,
                Notes = synthetic ? "SYNTHETIC placeholder caretaker (legacy-import backfill 2026-07) for patient L24-0001" : string.Empty,
                User = new User { Id = caretakerUserId == 0 ? 9000 + caretakerId : caretakerUserId, FirstName = "CT", LastName = $"Caretaker{caretakerId}" },
            },
        };
    }

    /// <summary>Happy-path repo defaults: both patients exist, clean roles, no links/sessions/plans.</summary>
    private void SetupCleanPair(Patient survivor, Patient eliminated)
    {
        _repo.Setup(r => r.GetPatientWithUserAsync(survivor.Id)).ReturnsAsync(survivor);
        _repo.Setup(r => r.GetPatientWithUserAsync(eliminated.Id)).ReturnsAsync(eliminated);
        _repo.Setup(r => r.GetUserRolesAsync(It.IsAny<int>()))
             .ReturnsAsync([new UserRole { RoleId = 2, UserId = eliminated.User!.Id }]);
        _repo.Setup(r => r.IsTherapistUserAsync(It.IsAny<int>())).ReturnsAsync(false);
        _repo.Setup(r => r.IsCaretakerUserAsync(It.IsAny<int>())).ReturnsAsync(false);
        _repo.Setup(r => r.GetCaretakerLinksAsync(It.IsAny<int>())).ReturnsAsync([]);
        _repo.Setup(r => r.CountSessionsAsync(It.IsAny<int>())).ReturnsAsync(0);
        _repo.Setup(r => r.CountTreatmentPlansAsync(It.IsAny<int>())).ReturnsAsync(0);
        _repo.Setup(r => r.ReassignSessionsAsync(eliminated.Id, survivor.Id, It.IsAny<int>())).ReturnsAsync(0);
        _repo.Setup(r => r.ReassignTreatmentPlansAsync(eliminated.Id, survivor.Id, It.IsAny<int>())).ReturnsAsync(0);
        _repo.Setup(r => r.AddMergeLogAsync(It.IsAny<PatientMergeLog>()))
             .ReturnsAsync((PatientMergeLog log) => { log.Id = 42; return log; });
    }

    // ── Validation guards ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Preview_SameIds_ThrowsArgumentException()
    {
        var svc = CreateService();
        var act = () => svc.PreviewAsync(new PatientMergeRequest { SurvivorPatientId = 5, EliminatedPatientId = 5 });
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task Merge_SameIds_ThrowsArgumentException()
    {
        var svc = CreateService();
        var act = () => svc.MergeAsync(new PatientMergeRequest { SurvivorPatientId = 5, EliminatedPatientId = 5 });
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task Preview_SurvivorMissing_ThrowsNotFound()
    {
        var svc = CreateService();
        _repo.Setup(r => r.GetPatientWithUserAsync(1)).ReturnsAsync((Patient?)null);
        var act = () => svc.PreviewAsync(new PatientMergeRequest { SurvivorPatientId = 1, EliminatedPatientId = 2 });
        (await act.Should().ThrowAsync<NotFoundException>()).WithMessage("*Survivor*1*");
    }

    [Fact]
    public async Task Preview_EliminatedMissing_ThrowsNotFound()
    {
        var svc = CreateService();
        _repo.Setup(r => r.GetPatientWithUserAsync(1)).ReturnsAsync(MakePatient(1, 10, "A", "B"));
        _repo.Setup(r => r.GetPatientWithUserAsync(2)).ReturnsAsync((Patient?)null);
        var act = () => svc.PreviewAsync(new PatientMergeRequest { SurvivorPatientId = 1, EliminatedPatientId = 2 });
        (await act.Should().ThrowAsync<NotFoundException>()).WithMessage("*Eliminated*2*");
    }

    [Fact]
    public async Task Preview_EliminatedUserWithNonPatientRole_ReportsBlocker()
    {
        var svc = CreateService();
        var survivor = MakePatient(1, 10, "Juan", "Perez");
        var eliminated = MakePatient(2, 20, "Jaun", "Perez");
        SetupCleanPair(survivor, eliminated);
        _repo.Setup(r => r.GetUserRolesAsync(20))
             .ReturnsAsync([new UserRole { RoleId = 2, UserId = 20 }, new UserRole { RoleId = 1, UserId = 20 }]);

        var preview = await svc.PreviewAsync(new PatientMergeRequest { SurvivorPatientId = 1, EliminatedPatientId = 2 });

        preview.Blockers.Should().ContainSingle(b => b.Contains("non-Patient roles"));
    }

    [Theory]
    [InlineData(true, false, "Therapist")]
    [InlineData(false, true, "Caretaker")]
    public async Task Preview_EliminatedUserDoublingAsOtherIdentity_ReportsBlocker(
        bool isTherapist, bool isCaretaker, string expected)
    {
        var svc = CreateService();
        var survivor = MakePatient(1, 10, "Juan", "Perez");
        var eliminated = MakePatient(2, 20, "Jaun", "Perez");
        SetupCleanPair(survivor, eliminated);
        _repo.Setup(r => r.IsTherapistUserAsync(20)).ReturnsAsync(isTherapist);
        _repo.Setup(r => r.IsCaretakerUserAsync(20)).ReturnsAsync(isCaretaker);

        var preview = await svc.PreviewAsync(new PatientMergeRequest { SurvivorPatientId = 1, EliminatedPatientId = 2 });

        preview.Blockers.Should().ContainSingle(b => b.Contains(expected));
    }

    [Fact]
    public async Task Merge_WithBlocker_ThrowsConflictAndWritesNothing()
    {
        var svc = CreateService();
        var survivor = MakePatient(1, 10, "Juan", "Perez");
        var eliminated = MakePatient(2, 20, "Jaun", "Perez");
        SetupCleanPair(survivor, eliminated);
        _repo.Setup(r => r.IsTherapistUserAsync(20)).ReturnsAsync(true);

        var act = () => svc.MergeAsync(new PatientMergeRequest { SurvivorPatientId = 1, EliminatedPatientId = 2 });

        await act.Should().ThrowAsync<ConflictException>();
        _repo.Verify(r => r.ReassignSessionsAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>()), Times.Never);
        _repo.Verify(r => r.DeletePatientAsync(It.IsAny<Patient>()), Times.Never);
        _repo.Verify(r => r.DeleteUserIdentityAsync(It.IsAny<int>()), Times.Never);
    }

    // ── Preview purity ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Preview_PerformsNoWrites()
    {
        var svc = CreateService();
        var survivor = MakePatient(1, 10, "Juan", "Perez");
        var eliminated = MakePatient(2, 20, "Jaun", "Perez", mrn: "L24-0313");
        SetupCleanPair(survivor, eliminated);
        _repo.Setup(r => r.GetCaretakerLinksAsync(2)).ReturnsAsync([MakeLink(2, 55, primary: true, synthetic: true)]);

        await svc.PreviewAsync(new PatientMergeRequest { SurvivorPatientId = 1, EliminatedPatientId = 2 });

        _repo.Verify(r => r.ReassignSessionsAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>()), Times.Never);
        _repo.Verify(r => r.ReassignTreatmentPlansAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>()), Times.Never);
        _repo.Verify(r => r.UpdateCaretakerLinkAsync(It.IsAny<PatientCaretaker>()), Times.Never);
        _repo.Verify(r => r.DeleteCaretakerLinkAsync(It.IsAny<PatientCaretaker>()), Times.Never);
        _repo.Verify(r => r.DeleteCaretakerAsync(It.IsAny<Caretaker>()), Times.Never);
        _repo.Verify(r => r.UpdatePatientAsync(It.IsAny<Patient>()), Times.Never);
        _repo.Verify(r => r.DeletePatientAsync(It.IsAny<Patient>()), Times.Never);
        _repo.Verify(r => r.DeleteUserIdentityAsync(It.IsAny<int>()), Times.Never);
        _repo.Verify(r => r.AddMergeLogAsync(It.IsAny<PatientMergeLog>()), Times.Never);
    }

    // ── Caretaker link classification ───────────────────────────────────────────────

    [Fact]
    public async Task Preview_SharedCaretaker_ClassifiedDedupeDelete_SurvivorPrimaryWins()
    {
        var svc = CreateService();
        var survivor = MakePatient(1, 10, "Juan", "Perez");
        var eliminated = MakePatient(2, 20, "Jaun", "Perez");
        SetupCleanPair(survivor, eliminated);
        _repo.Setup(r => r.GetCaretakerLinksAsync(1)).ReturnsAsync([MakeLink(1, 55, primary: false)]);
        _repo.Setup(r => r.GetCaretakerLinksAsync(2)).ReturnsAsync([MakeLink(2, 55, primary: true)]);

        var preview = await svc.PreviewAsync(new PatientMergeRequest { SurvivorPatientId = 1, EliminatedPatientId = 2 });

        var d = preview.Caretakers.Should().ContainSingle().Subject;
        d.Disposition.Should().Be(PatientMergeCaretakerDisposition.DedupeDelete);
        d.PrimaryFlagDropped.Should().BeTrue();
        preview.Counts.CaretakerLinksToDedupe.Should().Be(1);
        preview.Warnings.Should().Contain(w => w.Contains("primary"));
    }

    [Fact]
    public async Task Preview_SyntheticWithSurvivorCaretaker_ClassifiedRetireSynthetic()
    {
        var svc = CreateService();
        var survivor = MakePatient(1, 10, "Juan", "Perez");
        var eliminated = MakePatient(2, 20, "Jaun", "Perez");
        SetupCleanPair(survivor, eliminated);
        _repo.Setup(r => r.GetCaretakerLinksAsync(1)).ReturnsAsync([MakeLink(1, 40)]);
        _repo.Setup(r => r.GetCaretakerLinksAsync(2)).ReturnsAsync([MakeLink(2, 55, primary: true, synthetic: true)]);

        var preview = await svc.PreviewAsync(new PatientMergeRequest { SurvivorPatientId = 1, EliminatedPatientId = 2 });

        var d = preview.Caretakers.Should().ContainSingle().Subject;
        d.IsSynthetic.Should().BeTrue();
        d.Disposition.Should().Be(PatientMergeCaretakerDisposition.RetireSynthetic);
        preview.Counts.SyntheticCaretakersToDelete.Should().Be(1);
    }

    [Fact]
    public async Task Preview_SyntheticButSurvivorWouldEndCaretakerless_RemapsInstead()
    {
        var svc = CreateService();
        var survivor = MakePatient(1, 10, "Juan", "Perez");
        var eliminated = MakePatient(2, 20, "Jaun", "Perez");
        SetupCleanPair(survivor, eliminated);
        // Survivor has NO caretakers; eliminated's only link is the synthetic placeholder.
        _repo.Setup(r => r.GetCaretakerLinksAsync(2)).ReturnsAsync([MakeLink(2, 55, primary: true, synthetic: true)]);

        var preview = await svc.PreviewAsync(new PatientMergeRequest { SurvivorPatientId = 1, EliminatedPatientId = 2 });

        preview.Caretakers.Should().ContainSingle()
            .Which.Disposition.Should().Be(PatientMergeCaretakerDisposition.Remap);
        preview.Counts.CaretakerLinksToRemap.Should().Be(1);
        preview.Counts.SyntheticCaretakersToDelete.Should().Be(0);
    }

    [Fact]
    public async Task Preview_RemappedPrimaryDemoted_WhenSurvivorAlreadyHasPrimary()
    {
        var svc = CreateService();
        var survivor = MakePatient(1, 10, "Juan", "Perez");
        var eliminated = MakePatient(2, 20, "Jaun", "Perez");
        SetupCleanPair(survivor, eliminated);
        _repo.Setup(r => r.GetCaretakerLinksAsync(1)).ReturnsAsync([MakeLink(1, 40, primary: true)]);
        _repo.Setup(r => r.GetCaretakerLinksAsync(2)).ReturnsAsync([MakeLink(2, 55, primary: true)]);

        var preview = await svc.PreviewAsync(new PatientMergeRequest { SurvivorPatientId = 1, EliminatedPatientId = 2 });

        var d = preview.Caretakers.Should().ContainSingle().Subject;
        d.Disposition.Should().Be(PatientMergeCaretakerDisposition.Remap);
        d.PrimaryFlagDropped.Should().BeTrue();
        preview.Warnings.Should().Contain(w => w.Contains("already has a primary"));
    }

    // ── Fill-blanks ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Preview_FillBlanks_OnlyWhereSurvivorEmpty_AndNeverMrn()
    {
        var svc = CreateService();
        var survivor = MakePatient(1, 10, "Juan", "Perez", mrn: "TEMP-1", cedula: null, dob: null, gender: null, notes: null);
        var eliminated = MakePatient(2, 20, "Jaun", "Perez", mrn: "L24-0313", cedula: "8-123-456",
            dob: new DateTime(2018, 5, 4), gender: "Male", notes: "[LEGACY-IMPORT: roster]");
        SetupCleanPair(survivor, eliminated);

        var preview = await svc.PreviewAsync(new PatientMergeRequest { SurvivorPatientId = 1, EliminatedPatientId = 2 });

        preview.FieldFills.Select(f => f.Field).Should().BeEquivalentTo(
            ["DateOfBirth", "Cedula", "Gender", "Notes"]);
        preview.FieldFills.Should().NotContain(f => f.Field == "MedicalRecordNumber");
        preview.Warnings.Should().Contain(w => w.Contains("temporary MRN"));
    }

    [Fact]
    public async Task Preview_NoFills_WhenSurvivorFieldsPopulated_AndCedulaConflictWarned()
    {
        var svc = CreateService();
        var survivor = MakePatient(1, 10, "Juan", "Perez", mrn: "L24-0312", cedula: "8-111-111",
            dob: new DateTime(2018, 1, 1), gender: "Male", notes: "existing");
        var eliminated = MakePatient(2, 20, "Jaun", "Perez", mrn: "L24-0313", cedula: "8-222-222",
            dob: new DateTime(2018, 5, 4), gender: "Female", notes: "other");
        SetupCleanPair(survivor, eliminated);

        var preview = await svc.PreviewAsync(new PatientMergeRequest { SurvivorPatientId = 1, EliminatedPatientId = 2 });

        preview.FieldFills.Should().BeEmpty();
        preview.Warnings.Should().Contain(w => w.Contains("Both records carry a Cedula"));
    }

    // ── Execution ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Merge_HappyPath_CountsFlowFromRepo_AndLogWritten()
    {
        var svc = CreateService();
        var survivor = MakePatient(1, 10, "Juan", "Perez", mrn: "L24-0312");
        var eliminated = MakePatient(2, 20, "Jaun", "Perez", mrn: "L24-0313", cedula: "8-123-456",
            dob: new DateTime(2018, 5, 4), notes: "[LEGACY-IMPORT: roster]");
        SetupCleanPair(survivor, eliminated);
        _repo.Setup(r => r.ReassignSessionsAsync(2, 1, 7)).ReturnsAsync(12);
        _repo.Setup(r => r.ReassignTreatmentPlansAsync(2, 1, 7)).ReturnsAsync(3);
        PatientMergeLog? written = null;
        _repo.Setup(r => r.AddMergeLogAsync(It.IsAny<PatientMergeLog>()))
             .ReturnsAsync((PatientMergeLog log) => { written = log; log.Id = 42; return log; });

        var result = await svc.MergeAsync(new PatientMergeRequest { SurvivorPatientId = 1, EliminatedPatientId = 2 });

        result.MergeLogId.Should().Be(42);
        result.Counts.SessionsRemapped.Should().Be(12);
        result.Counts.PlansRemapped.Should().Be(3);
        result.SurvivorPatientId.Should().Be(1);
        result.EliminatedPatientId.Should().Be(2);
        written.Should().NotBeNull();
        written!.EliminatedName.Should().Be("Perez, Jaun");
        written.EliminatedMrn.Should().Be("L24-0313");
        written.EliminatedCedula.Should().Be("8-123-456");
        written.EliminatedNotes.Should().Be("[LEGACY-IMPORT: roster]");
        written.SessionsRemapped.Should().Be(12);
        written.PlansRemapped.Should().Be(3);
        written.MergedByUserId.Should().Be(7);
        _repo.Verify(r => r.DeletePatientAsync(eliminated), Times.Once);
        _repo.Verify(r => r.DeleteUserIdentityAsync(20), Times.Once);
    }

    [Fact]
    public async Task Merge_DeletesEliminatedPatient_BeforeSurvivorEnrichment()
    {
        // The Cedula fill relies on the eliminated row being gone (uq_patient_cedula).
        var svc = CreateService();
        var survivor = MakePatient(1, 10, "Juan", "Perez", mrn: "L24-0312");
        var eliminated = MakePatient(2, 20, "Jaun", "Perez", cedula: "8-123-456");
        SetupCleanPair(survivor, eliminated);
        var callOrder = new List<string>();
        _repo.Setup(r => r.DeletePatientAsync(It.IsAny<Patient>()))
             .Callback(() => callOrder.Add("delete")).Returns(Task.CompletedTask);
        _repo.Setup(r => r.UpdatePatientAsync(It.IsAny<Patient>()))
             .Callback(() => callOrder.Add("update")).Returns(Task.CompletedTask);
        _repo.Setup(r => r.DeleteUserIdentityAsync(It.IsAny<int>()))
             .Callback(() => callOrder.Add("retire")).Returns(Task.CompletedTask);

        await svc.MergeAsync(new PatientMergeRequest { SurvivorPatientId = 1, EliminatedPatientId = 2 });

        callOrder.Should().Equal("delete", "update", "retire");
        survivor.Cedula.Should().Be("8-123-456");
    }

    [Fact]
    public async Task Merge_AppendsMergedMarker_ToExistingSurvivorNotes()
    {
        var svc = CreateService();
        var survivor = MakePatient(1, 10, "Juan", "Perez", mrn: "L24-0312", notes: "pre-existing note");
        var eliminated = MakePatient(2, 20, "Jaun", "Perez", mrn: "L24-0313");
        SetupCleanPair(survivor, eliminated);

        await svc.MergeAsync(new PatientMergeRequest { SurvivorPatientId = 1, EliminatedPatientId = 2 });

        survivor.Notes.Should().StartWith("pre-existing note\n[MERGED: absorbed Patient #2 MRN L24-0313");
        survivor.Notes.Should().Contain("\"Perez, Jaun\"").And.Contain("by user 7");
    }

    [Fact]
    public async Task Merge_SyntheticRetirement_DeletesLinkCaretakerAndIdentity()
    {
        var svc = CreateService();
        var survivor = MakePatient(1, 10, "Juan", "Perez", mrn: "L24-0312");
        var eliminated = MakePatient(2, 20, "Jaun", "Perez");
        SetupCleanPair(survivor, eliminated);
        var syntheticLink = MakeLink(2, 55, primary: true, synthetic: true, caretakerUserId: 900);
        _repo.Setup(r => r.GetCaretakerLinksAsync(1)).ReturnsAsync([MakeLink(1, 40, primary: true)]);
        _repo.Setup(r => r.GetCaretakerLinksAsync(2)).ReturnsAsync([syntheticLink]);

        var result = await svc.MergeAsync(new PatientMergeRequest { SurvivorPatientId = 1, EliminatedPatientId = 2 });

        result.Counts.SyntheticCaretakersDeleted.Should().Be(1);
        _repo.Verify(r => r.DeleteCaretakerLinkAsync(syntheticLink), Times.Once);
        _repo.Verify(r => r.DeleteCaretakerAsync(syntheticLink.Caretaker!), Times.Once);
        _repo.Verify(r => r.DeleteUserIdentityAsync(900), Times.Once);  // synthetic's SystemUser
        _repo.Verify(r => r.DeleteUserIdentityAsync(20), Times.Once);   // eliminated patient's SystemUser
    }

    [Fact]
    public async Task Merge_RemappedLink_RepointedToSurvivor_AndDemotedWhenSurvivorHasPrimary()
    {
        var svc = CreateService();
        var survivor = MakePatient(1, 10, "Juan", "Perez", mrn: "L24-0312");
        var eliminated = MakePatient(2, 20, "Jaun", "Perez");
        SetupCleanPair(survivor, eliminated);
        var link = MakeLink(2, 55, primary: true);
        _repo.Setup(r => r.GetCaretakerLinksAsync(1)).ReturnsAsync([MakeLink(1, 40, primary: true)]);
        _repo.Setup(r => r.GetCaretakerLinksAsync(2)).ReturnsAsync([link]);

        var result = await svc.MergeAsync(new PatientMergeRequest { SurvivorPatientId = 1, EliminatedPatientId = 2 });

        result.Counts.CaretakerLinksRemapped.Should().Be(1);
        link.PatientId.Should().Be(1);
        link.PrimaryCaretaker.Should().BeFalse();
        _repo.Verify(r => r.UpdateCaretakerLinkAsync(link), Times.Once);
    }

    [Fact]
    public async Task Merge_DedupedLink_DeletedNotRemapped()
    {
        var svc = CreateService();
        var survivor = MakePatient(1, 10, "Juan", "Perez", mrn: "L24-0312");
        var eliminated = MakePatient(2, 20, "Jaun", "Perez");
        SetupCleanPair(survivor, eliminated);
        var dupLink = MakeLink(2, 55);
        _repo.Setup(r => r.GetCaretakerLinksAsync(1)).ReturnsAsync([MakeLink(1, 55, primary: true)]);
        _repo.Setup(r => r.GetCaretakerLinksAsync(2)).ReturnsAsync([dupLink]);

        var result = await svc.MergeAsync(new PatientMergeRequest { SurvivorPatientId = 1, EliminatedPatientId = 2 });

        result.Counts.CaretakerLinksDeduped.Should().Be(1);
        _repo.Verify(r => r.DeleteCaretakerLinkAsync(dupLink), Times.Once);
        _repo.Verify(r => r.UpdateCaretakerLinkAsync(It.IsAny<PatientCaretaker>()), Times.Never);
        _repo.Verify(r => r.DeleteCaretakerAsync(It.IsAny<Caretaker>()), Times.Never);
    }
}
