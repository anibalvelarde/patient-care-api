using Microsoft.Extensions.Logging;
using Neurocorp.Api.Core.BusinessObjects.Patients;
using Neurocorp.Api.Core.Entities;
using Neurocorp.Api.Core.Exceptions;
using Neurocorp.Api.Core.Interfaces;
using Neurocorp.Api.Core.Interfaces.Repositories;
using Neurocorp.Api.Core.Interfaces.Services;

namespace Neurocorp.Api.Core.Services;

/// <summary>
/// Duplicate-patient merge (WP-22, SYSADMIN-only). Preview and execute share one plan step so
/// their counts always agree; execute runs the whole remap + hard-delete inside a single
/// IUnitOfWork transaction — any failure rolls everything back.
/// </summary>
public class PatientMergeService : IPatientMergeService
{
    // Greppable prefix on Caretaker.Notes identifying WP-19 placeholder caretakers
    // (see patient-care-db tools/legacy-import/importer/caretaker_backfill.py).
    private const string SyntheticCaretakerNotesPrefix = "SYNTHETIC placeholder";
    private const int PatientRoleId = 2;

    private readonly IPatientMergeRepository _repository;
    private readonly ICurrentUserService _currentUser;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<PatientMergeService> _logger;

    public PatientMergeService(
        ILogger<PatientMergeService> logger,
        IPatientMergeRepository repository,
        ICurrentUserService currentUser,
        IUnitOfWork unitOfWork)
    {
        _logger = logger;
        _repository = repository;
        _currentUser = currentUser;
        _unitOfWork = unitOfWork;
    }

    public async Task<PatientMergePreview> PreviewAsync(PatientMergeRequest request)
    {
        var (survivor, eliminated) = await LoadPairAsync(request);
        return await BuildPlanAsync(survivor, eliminated);
    }

    public async Task<PatientMergeResult> MergeAsync(PatientMergeRequest request)
    {
        var mergedByUserId = _currentUser.UserId ?? 0;

        var (result, logId) = await _unitOfWork.ExecuteAsync(async () =>
        {
            // Load + plan INSIDE the transaction so validation and mutation see one snapshot.
            var (survivor, eliminated) = await LoadPairAsync(request);
            var plan = await BuildPlanAsync(survivor, eliminated);
            if (plan.Blockers.Count > 0)
            {
                throw new ConflictException(string.Join(" ", plan.Blockers));
            }

            // 1. Caretaker link surgery — deletes first (frees the (PatientID, CaretakerID)
            //    unique key), then remaps; synthetic retirements queue their identity.
            var eliminatedLinks = await _repository.GetCaretakerLinksAsync(eliminated.Id);
            var survivorLinks = await _repository.GetCaretakerLinksAsync(survivor.Id);
            var survivorHasPrimary = survivorLinks.Any(l => l.PrimaryCaretaker);
            var identitiesToRetire = new List<int>();
            int remapped = 0, deduped = 0, syntheticsDeleted = 0;

            var dispositionByLinkId = plan.Caretakers.ToDictionary(c => c.CaretakerId, c => c);
            foreach (var link in eliminatedLinks)
            {
                var disposition = dispositionByLinkId[link.CaretakerId].Disposition;
                if (disposition == PatientMergeCaretakerDisposition.DedupeDelete)
                {
                    await _repository.DeleteCaretakerLinkAsync(link);
                    deduped++;
                }
                else if (disposition == PatientMergeCaretakerDisposition.RetireSynthetic)
                {
                    await _repository.DeleteCaretakerLinkAsync(link);
                    if (link.Caretaker is not null)
                    {
                        if (link.Caretaker.User is not null) identitiesToRetire.Add(link.Caretaker.User.Id);
                        await _repository.DeleteCaretakerAsync(link.Caretaker);
                    }
                    syntheticsDeleted++;
                }
                else
                {
                    link.PatientId = survivor.Id;
                    if (link.PrimaryCaretaker && survivorHasPrimary)
                    {
                        link.PrimaryCaretaker = false; // survivor's designation wins
                    }
                    survivorHasPrimary |= link.PrimaryCaretaker;
                    await _repository.UpdateCaretakerLinkAsync(link);
                    remapped++;
                }
            }

            // 2 + 3. Bulk FK repoints (row counts are the audit-log figures).
            var sessionsRemapped = await _repository.ReassignSessionsAsync(eliminated.Id, survivor.Id, mergedByUserId);
            var plansRemapped = await _repository.ReassignTreatmentPlansAsync(eliminated.Id, survivor.Id, mergedByUserId);

            // 4. Snapshot, then hard-delete the eliminated Patient row. Deleting BEFORE the
            //    survivor enrichment frees uq_patient_cedula for a Cedula fill.
            var snapshot = new PatientMergeLog
            {
                SurvivorPatientId = survivor.Id,
                EliminatedPatientId = eliminated.Id,
                EliminatedUserId = eliminated.User!.Id,
                EliminatedName = eliminated.User.GetFullName(),
                EliminatedMrn = eliminated.MedicalRecordNumber,
                EliminatedCedula = eliminated.Cedula,
                EliminatedDateOfBirth = eliminated.DateOfBirth,
                EliminatedNotes = eliminated.Notes,
                MergedByUserId = mergedByUserId,
            };
            await _repository.DeletePatientAsync(eliminated);

            // 5. Enrich the survivor (fill-blanks) + append the Notes audit marker.
            var fieldsFilled = plan.FieldFills.Select(f => f.Field).ToList();
            foreach (var fill in plan.FieldFills)
            {
                switch (fill.Field)
                {
                    case nameof(Patient.DateOfBirth): survivor.DateOfBirth = snapshot.EliminatedDateOfBirth; break;
                    case nameof(Patient.Cedula): survivor.Cedula = snapshot.EliminatedCedula; break;
                    case nameof(Patient.Gender): survivor.Gender = eliminated.Gender; break;
                    case nameof(Patient.Notes): survivor.Notes = snapshot.EliminatedNotes; break;
                }
            }
            var marker = BuildMergedMarker(snapshot, sessionsRemapped, plansRemapped,
                remapped, deduped, syntheticsDeleted, fieldsFilled);
            survivor.Notes = string.IsNullOrWhiteSpace(survivor.Notes) ? marker : $"{survivor.Notes}\n{marker}";
            await _repository.UpdatePatientAsync(survivor);

            // 6. Audit row (actual counts).
            snapshot.SessionsRemapped = sessionsRemapped;
            snapshot.PlansRemapped = plansRemapped;
            snapshot.CaretakerLinksRemapped = remapped;
            snapshot.CaretakerLinksDeduped = deduped;
            snapshot.SyntheticCaretakersDeleted = syntheticsDeleted;
            snapshot.FieldsFilled = fieldsFilled.Count > 0 ? string.Join(",", fieldsFilled) : null;
            var log = await _repository.AddMergeLogAsync(snapshot);

            // 7. Retire identities: the eliminated patient's SystemUser + every deleted
            //    synthetic caretaker's SystemUser (roles → claims → user; fix-b1 order).
            await _repository.DeleteUserIdentityAsync(snapshot.EliminatedUserId);
            foreach (var userId in identitiesToRetire)
            {
                await _repository.DeleteUserIdentityAsync(userId);
            }

            var mergeResult = new PatientMergeResult
            {
                SurvivorPatientId = survivor.Id,
                EliminatedPatientId = snapshot.EliminatedPatientId,
                Counts = new PatientMergeExecutedCounts
                {
                    SessionsRemapped = sessionsRemapped,
                    PlansRemapped = plansRemapped,
                    CaretakerLinksRemapped = remapped,
                    CaretakerLinksDeduped = deduped,
                    SyntheticCaretakersDeleted = syntheticsDeleted,
                },
                FieldsFilled = fieldsFilled,
                MergedAtUtc = DateTime.UtcNow,
            };
            return (mergeResult, log.Id);
        });

        result.MergeLogId = logId;
        _logger.LogInformation(
            "Patient merge executed: survivor {SurvivorId} absorbed {EliminatedId} " +
            "(sessions {Sessions}, plans {Plans}, links remapped {Remapped}/deduped {Deduped}/synthetic {Synthetic}) by user {UserId}, log {LogId}",
            result.SurvivorPatientId, result.EliminatedPatientId, result.Counts.SessionsRemapped,
            result.Counts.PlansRemapped, result.Counts.CaretakerLinksRemapped, result.Counts.CaretakerLinksDeduped,
            result.Counts.SyntheticCaretakersDeleted, mergedByUserId, logId);
        return result;
    }

    private async Task<(Patient Survivor, Patient Eliminated)> LoadPairAsync(PatientMergeRequest request)
    {
        if (request.SurvivorPatientId == request.EliminatedPatientId)
        {
            throw new ArgumentException("Survivor and eliminated patient must be different records.");
        }
        var survivor = await _repository.GetPatientWithUserAsync(request.SurvivorPatientId)
            ?? throw new NotFoundException("Survivor patient", request.SurvivorPatientId);
        var eliminated = await _repository.GetPatientWithUserAsync(request.EliminatedPatientId)
            ?? throw new NotFoundException("Eliminated patient", request.EliminatedPatientId);
        return (survivor, eliminated);
    }

    /// <summary>Read-only plan shared by preview and execute — performs no writes.</summary>
    private async Task<PatientMergePreview> BuildPlanAsync(Patient survivor, Patient eliminated)
    {
        var warnings = new List<string>();
        var blockers = new List<string>();

        // Blockers: the eliminated identity must be a pure Patient — anything else is a
        // hand-curated situation the hard-delete must not steamroll.
        var eliminatedRoles = await _repository.GetUserRolesAsync(eliminated.User!.Id);
        if (eliminatedRoles.Any(r => r.RoleId != PatientRoleId))
        {
            blockers.Add($"Eliminated patient's user {eliminated.User.Id} holds non-Patient roles; resolve manually before merging.");
        }
        if (await _repository.IsTherapistUserAsync(eliminated.User.Id))
        {
            blockers.Add($"Eliminated patient's user {eliminated.User.Id} is also a Therapist identity; resolve manually before merging.");
        }
        if (await _repository.IsCaretakerUserAsync(eliminated.User.Id))
        {
            blockers.Add($"Eliminated patient's user {eliminated.User.Id} is also a Caretaker identity; resolve manually before merging.");
        }

        // Caretaker link classification.
        var survivorLinks = await _repository.GetCaretakerLinksAsync(survivor.Id);
        var eliminatedLinks = await _repository.GetCaretakerLinksAsync(eliminated.Id);
        var survivorCaretakerIds = survivorLinks.Select(l => l.CaretakerId).ToHashSet();
        var survivorHasPrimary = survivorLinks.Any(l => l.PrimaryCaretaker);

        var dispositions = new List<PatientMergeCaretakerDisposition>();
        var nonSyntheticRemaps = eliminatedLinks.Count(l =>
            !survivorCaretakerIds.Contains(l.CaretakerId) && !IsSynthetic(l.Caretaker));
        var survivorKeepsACaretaker = survivorLinks.Count + nonSyntheticRemaps > 0;

        foreach (var link in eliminatedLinks)
        {
            var isSynthetic = IsSynthetic(link.Caretaker);
            var d = new PatientMergeCaretakerDisposition
            {
                CaretakerId = link.CaretakerId,
                CaretakerName = link.Caretaker?.User?.GetFullName() ?? $"Caretaker #{link.CaretakerId}",
                IsSynthetic = isSynthetic,
            };
            if (survivorCaretakerIds.Contains(link.CaretakerId))
            {
                d.Disposition = PatientMergeCaretakerDisposition.DedupeDelete;
                var survivorDup = survivorLinks.First(l => l.CaretakerId == link.CaretakerId);
                if (link.PrimaryCaretaker && !survivorDup.PrimaryCaretaker)
                {
                    d.PrimaryFlagDropped = true;
                    warnings.Add($"Caretaker '{d.CaretakerName}' was primary on the eliminated record; the survivor's existing (non-primary) link is kept as-is.");
                }
            }
            else if (isSynthetic && survivorKeepsACaretaker)
            {
                // Standing rule: retire the WP-19 placeholder when the survivor keeps a
                // caretaker; if it would leave the survivor caretaker-less, remap instead.
                d.Disposition = PatientMergeCaretakerDisposition.RetireSynthetic;
            }
            else
            {
                d.Disposition = PatientMergeCaretakerDisposition.Remap;
                if (link.PrimaryCaretaker && survivorHasPrimary)
                {
                    d.PrimaryFlagDropped = true;
                    warnings.Add($"Caretaker '{d.CaretakerName}' arrives non-primary — the survivor already has a primary caretaker.");
                }
                survivorHasPrimary |= link.PrimaryCaretaker && !d.PrimaryFlagDropped;
            }
            dispositions.Add(d);
        }

        // Fill-blanks: survivor inherits where its own field is empty. MRN is NEVER merged.
        var fills = new List<PatientMergeFieldFill>();
        if (survivor.DateOfBirth is null && eliminated.DateOfBirth is not null)
            fills.Add(new() { Field = nameof(Patient.DateOfBirth), Value = eliminated.DateOfBirth.Value.ToString("yyyy-MM-dd") });
        if (string.IsNullOrWhiteSpace(survivor.Cedula) && !string.IsNullOrWhiteSpace(eliminated.Cedula))
            fills.Add(new() { Field = nameof(Patient.Cedula), Value = eliminated.Cedula! });
        if (string.IsNullOrWhiteSpace(survivor.Gender) && !string.IsNullOrWhiteSpace(eliminated.Gender))
            fills.Add(new() { Field = nameof(Patient.Gender), Value = eliminated.Gender! });
        if (string.IsNullOrWhiteSpace(survivor.Notes) && !string.IsNullOrWhiteSpace(eliminated.Notes))
            fills.Add(new() { Field = nameof(Patient.Notes), Value = Truncate(eliminated.Notes!, 200) });

        if (survivor.HasTemporaryMrn() && !eliminated.HasTemporaryMrn())
        {
            warnings.Add($"Survivor has a temporary MRN while the eliminated record's MRN is '{eliminated.MedicalRecordNumber}' — MRN is never merged; the eliminated MRN is preserved in the merge log.");
        }
        if (!string.IsNullOrWhiteSpace(survivor.Cedula) && !string.IsNullOrWhiteSpace(eliminated.Cedula)
            && !string.Equals(survivor.Cedula, eliminated.Cedula, StringComparison.OrdinalIgnoreCase))
        {
            warnings.Add($"Both records carry a Cedula (survivor '{survivor.Cedula}', eliminated '{eliminated.Cedula}') — the survivor keeps its own; the eliminated value is preserved in the merge log.");
        }
        if (!(survivor.User!.ActiveStatus))
        {
            warnings.Add("The survivor patient is currently inactive.");
        }

        return new PatientMergePreview
        {
            Survivor = await BuildIdentityAsync(survivor, survivorLinks.Count),
            Eliminated = await BuildIdentityAsync(eliminated, eliminatedLinks.Count),
            Counts = new PatientMergePlannedCounts
            {
                SessionsToRemap = await _repository.CountSessionsAsync(eliminated.Id),
                PlansToRemap = await _repository.CountTreatmentPlansAsync(eliminated.Id),
                CaretakerLinksToRemap = dispositions.Count(d => d.Disposition == PatientMergeCaretakerDisposition.Remap),
                CaretakerLinksToDedupe = dispositions.Count(d => d.Disposition == PatientMergeCaretakerDisposition.DedupeDelete),
                SyntheticCaretakersToDelete = dispositions.Count(d => d.Disposition == PatientMergeCaretakerDisposition.RetireSynthetic),
            },
            Caretakers = dispositions,
            FieldFills = fills,
            Warnings = warnings,
            Blockers = blockers,
        };
    }

    private async Task<PatientMergeIdentity> BuildIdentityAsync(Patient patient, int caretakerCount) => new()
    {
        PatientId = patient.Id,
        UserId = patient.User!.Id,
        PatientName = patient.User.GetFullName(),
        MedicalRecordNumber = patient.MedicalRecordNumber,
        Cedula = patient.Cedula,
        DateOfBirth = patient.DateOfBirth,
        Gender = patient.Gender,
        IsActive = patient.User.ActiveStatus,
        SessionCount = await _repository.CountSessionsAsync(patient.Id),
        PlanCount = await _repository.CountTreatmentPlansAsync(patient.Id),
        CaretakerCount = caretakerCount,
    };

    private static bool IsSynthetic(Caretaker? caretaker) =>
        caretaker?.Notes?.StartsWith(SyntheticCaretakerNotesPrefix, StringComparison.OrdinalIgnoreCase) == true;

    private static string BuildMergedMarker(PatientMergeLog snapshot, int sessions, int plans,
        int remapped, int deduped, int syntheticsDeleted, IReadOnlyList<string> fieldsFilled) =>
        $"[MERGED: absorbed Patient #{snapshot.EliminatedPatientId}" +
        $" MRN {snapshot.EliminatedMrn ?? "—"} Cedula {snapshot.EliminatedCedula ?? "—"}" +
        $" \"{snapshot.EliminatedName}\" on {DateTime.UtcNow:yyyy-MM-dd} by user {snapshot.MergedByUserId};" +
        $" sessions {sessions}, plans {plans}," +
        $" caretakerLinks remapped {remapped}/deduped {deduped}/syntheticDeleted {syntheticsDeleted};" +
        $" filled {(fieldsFilled.Count > 0 ? string.Join(",", fieldsFilled) : "none")}]";

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max] + "…";
}
