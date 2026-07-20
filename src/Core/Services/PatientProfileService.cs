using Microsoft.Extensions.Logging;
using Neurocorp.Api.Core.BusinessObjects.Common;
using Neurocorp.Api.Core.BusinessObjects.Patients;
using Neurocorp.Api.Core.Entities;
using Neurocorp.Api.Core.Interfaces;
using Neurocorp.Api.Core.Interfaces.Services;
using Neurocorp.Api.Core.Interfaces.Repositories;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Neurocorp.Api.Core.Services;

public class PatientProfileService : IPatientProfileService
{
    private readonly IPatientProfileRepository _repository;
    private readonly IPatientRepository _patientRepo;
    private readonly IUserRepository _userRepo;
    private readonly IUserRoleRepository _userRoleRepo;
    private readonly IPatientCaretakerRepository _patientCaretakerRepo;
    private readonly ITherapySessionRepository _therapySessionRepo;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IUserNameResolver? _userNameResolver;
    private readonly ILogger<PatientProfileService> _logger;

    public PatientProfileService(
        ILogger<PatientProfileService> logger,
        IPatientProfileRepository patientProfileRepository,
        IPatientRepository patientRepository,
        IUserRepository userRepo,
        IUserRoleRepository userRoleRepo,
        IPatientCaretakerRepository patientCaretakerRepo,
        ITherapySessionRepository therapySessionRepo,
        IUnitOfWork unitOfWork,
        // WP-31 (U1): optional so existing test constructions compile unchanged; DI supplies the real one.
        IUserNameResolver? userNameResolver = null)
    {
        _repository = patientProfileRepository;
        _patientRepo = patientRepository;
        _userRepo = userRepo;
        _userRoleRepo = userRoleRepo;
        _patientCaretakerRepo = patientCaretakerRepo;
        _therapySessionRepo = therapySessionRepo;
        _unitOfWork = unitOfWork;
        _userNameResolver = userNameResolver;
        _logger = logger;
    }

    // WP-31 (U1): resolve audit updater names for a materialized batch (no-op when unresolved/absent).
    private async Task EnrichAuditAsync(IEnumerable<IHasAudit> items)
    {
        if (_userNameResolver != null)
        {
            await AuditEnrichment.ResolveNamesAsync(items, _userNameResolver);
        }
    }

    public async Task<IEnumerable<PatientProfile>> GetAllAsync()
    {
        _logger.LogInformation("Getting all patient profiles.");
        var profiles = await _repository.GetAllAsync();
        await EnrichAuditAsync(profiles);
        return profiles;
    }

    // WP-30 (U2): paged main list. Parity with GetAllAsync — no HasCompletedDiscovery stamp
    // (the list view never used it; GetByIdAsync/GetByIdsAsync keep stamping for detail flows).
    public async Task<PagedResult<PatientProfile>> GetPagedAsync(string? search, bool? isActive, int page, int pageSize)
    {
        _logger.LogInformation("Getting paged patient profiles (search: {Search}, isActive: {IsActive}, page: {Page}, pageSize: {PageSize}).",
            search, isActive, page, pageSize);
        var result = await _repository.GetPagedAsync(search, isActive, page, pageSize);
        await EnrichAuditAsync(result.Items);
        return result;
    }

    public async Task<IReadOnlyList<PatientLookupItem>> LookupAsync(string query, int maxResults)
    {
        _logger.LogInformation("Patient typeahead lookup (query: {Query}, cap: {Cap}).", query, maxResults);
        return await _repository.LookupAsync(query, maxResults);
    }

    public async Task<PatientProfile?> GetByIdAsync(int id)
    {
        _logger.LogInformation("Getting patient profile by ID: {Id}", id);
        var profile = await _repository.GetByIdAsync(id);
        if (profile != null)
        {
            profile.HasCompletedDiscovery = await _therapySessionRepo.HasCompletedDiscoveryAsync(profile.PatientId);
            await EnrichAuditAsync(new[] { profile });
        }
        return profile;
    }

    // WP-29 (U3): batched GetByIdAsync — same data (profile + HasCompletedDiscovery stamp),
    // two round trips total instead of two per patient.
    public async Task<IReadOnlyList<PatientProfile>> GetByIdsAsync(IReadOnlyCollection<int> patientIds)
    {
        _logger.LogInformation("Getting {Count} patient profiles by ids (batched).", patientIds.Count);
        var profiles = await _repository.GetByIdsAsync(patientIds);
        if (profiles.Count == 0) return profiles;

        var withDiscovery = (await _therapySessionRepo
            .GetPatientIdsWithCompletedDiscoveryAsync(profiles.Select(p => p.PatientId).ToList()))
            .ToHashSet();
        foreach (var profile in profiles)
        {
            profile.HasCompletedDiscovery = withDiscovery.Contains(profile.PatientId);
        }
        await EnrichAuditAsync(profiles);
        return profiles;
    }

    public async Task<PatientProfile> CreateAsync(PatientProfile patient)
    {
        _logger.LogError("Operation Not Allowed: Creating new patient profile.");
        return await Task.FromException<PatientProfile>(new NotImplementedException());
    }

    public async Task UpdateAsync(PatientProfile patient)
    {
        _logger.LogError("Updating patient profile.");
        await Task.FromException<PatientProfile>(new NotImplementedException());
    }

    public async Task<PatientProfile> CreateAsync(PatientProfileRequest patientRequest)
    {
        _logger.LogInformation("Creating new patient profile from request.");

        // WP-36 (G3): the system mints the MRN — a client-supplied value is IGNORED, not
        // rejected. Warn instead of 400 so the old UI (which still posts the field) keeps
        // working during the API-first deploy gap; WP-36C removes the field from the form.
        if (!string.IsNullOrWhiteSpace(patientRequest.MedicalRecordNumber))
        {
            _logger.LogWarning(
                "Ignoring client-supplied MRN '{SuppliedMrn}' on patient create — MRNs are system-minted (WP-36/G3).",
                patientRequest.MedicalRecordNumber);
        }

        (User user, Patient patient, UserRole role) created;
        try
        {
            created = await RunCreateTransactionAsync(patientRequest);
        }
        // WP-36 (G1a): two stations can read the same MAX and mint the same NC{yy}-#### — the
        // DB unique key is the arbiter. Retry ONCE: the transaction rolled back (and the UoW
        // cleared the tracked phantoms), so the re-run re-reads MAX and re-mints. Only an
        // MRN-key collision is retryable — a cedula/email duplicate is a genuine client
        // conflict and propagates to GlobalExceptionHandler's 409, as does a second MRN
        // collision here.
        catch (Exception ex) when (IsMrnDuplicateKey(ex))
        {
            _logger.LogWarning(ex,
                "Minted MRN collided with a concurrent create — retrying once with a re-read sequence (WP-36/G1a).");
            created = await RunCreateTransactionAsync(patientRequest);
        }

        var (newUser, newPatient, newRole) = created;
        _logger.LogInformation($"New Patient Profile was created: Uid[{newUser.Id}], Pid[{newPatient.Id}], Role[{newRole.UserRoleId}]");
        return new PatientProfile
        {
            PatientId = newPatient.Id,
            UserId = newUser.Id,
            PatientName = $"{newUser.LastName}, {newUser.FirstName} {newUser.MiddleName}".Trim(),
            MedicalRecordNumber = newPatient.MedicalRecordNumber,
            Cedula = newPatient.Cedula,
            HasSenadisDiscount = newPatient.HasSenadisDiscount,
            SenadisExpirationDate = newPatient.SenadisExpirationDate,
            RequiresDiscovery = newPatient.RequiresDiscovery,
            DateOfBirth = newPatient.DateOfBirth ?? DateTime.MinValue,
            Email = newUser.Email,
            PhoneNumber = newUser.PhoneNumber,
            CreatedTimestamp = newUser.CreatedTimestamp,
            IsActive = newUser.ActiveStatus
        };
    }

    // WP-36 (G1a): one create attempt = one atomic unit of work. B1: all four writes commit or
    // roll back together — a failure mid-way (e.g. a rejected Patient INSERT) must not leave a
    // committed, orphaned SystemUser behind.
    private async Task<(User user, Patient patient, UserRole role)> RunCreateTransactionAsync(PatientProfileRequest patientRequest)
    {
        return await _unitOfWork.ExecuteAsync(async () =>
        {
            var user = await _userRepo.AddAsync(MapToNewUser(patientRequest));

            // WP-36: mint INSIDE the transaction — replaces the old post-insert TEMP-{id}
            // stamp, and the INSERT itself carries the minted value (so a race surfaces as a
            // duplicate-key failure right here, caught by the caller's retry). The MAX read
            // sees committed rows only; the unique key backstops concurrent minters (G1a).
            var patient = MapToNewPatient(patientRequest, user);
            patient.MedicalRecordNumber = await MintMrnAsync();
            patient = await _patientRepo.AddAsync(patient);

            var role = await _userRoleRepo.AddAsync(patient.MintNewRole());
            return (user, patient, role);
        });
    }

    // WP-36 (G6): the MRN year is the CLINIC's (Panama) calendar year, not the server's — the
    // deployed container runs UTC, so DateTime.Now around midnight Dec-31 would roll the NC{yy}
    // prefix (and reset the sequence) 5 hours early. Panama is fixed UTC-5 with no DST.
    private async Task<string> MintMrnAsync()
    {
        var panamaNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, PanamaTimeZone);
        var prefix = $"NC{panamaNow.Year % 100:D2}-";
        var maxSequence = await _patientRepo.GetMaxMrnSequenceAsync(prefix);
        // First mint of a year (or an empty table — prod ships with zero NC rows) => {yy}-0001.
        return $"{prefix}{maxSequence + 1:D4}";
    }

    private static readonly TimeZoneInfo PanamaTimeZone = ResolvePanamaTimeZone();

    // IANA id first (Linux container / .NET-ICU Windows), Windows id as fallback, and — because
    // the k3s containers are slim images that may lack tzdata entirely (the WP-26 lesson) — a
    // fixed UTC-5 custom zone as the last resort. Panama has no DST, so the fixed offset is
    // exactly equivalent, honoring the G6 ruling's intent on any host.
    private static TimeZoneInfo ResolvePanamaTimeZone()
    {
        foreach (var id in new[] { "America/Panama", "SA Pacific Standard Time" })
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(id);
            }
            catch (TimeZoneNotFoundException) { /* try the next id */ }
            catch (InvalidTimeZoneException) { /* corrupt zone data — try the next id */ }
        }
        return TimeZoneInfo.CreateCustomTimeZone("Panama (fixed)", TimeSpan.FromHours(-5), "Panama (UTC-5)", "Panama (UTC-5)");
    }

    // WP-36 (G1a): discriminates the violated unique key the way GlobalExceptionHandler does —
    // by key name in the MySQL 1062 message ("Duplicate entry '…' for key '…'") — but without an
    // EF/MySqlConnector dependency (Core stays infra-free), so it walks the exception chain
    // textually. The MRN unique key is named after the column itself ("MedicalRecordNumber",
    // B1); cedula/email violations carry uq_patient_cedula / uq_systemuser_email and must NOT
    // trigger the mint retry.
    private static bool IsMrnDuplicateKey(Exception exception)
    {
        for (Exception? e = exception; e != null; e = e.InnerException)
        {
            if (e.Message.Contains("Duplicate entry", StringComparison.OrdinalIgnoreCase)
                && e.Message.Contains("MedicalRecordNumber", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    public async Task<bool> UpdateAsync(int patientAggId, PatientProfileUpdateRequest updateRequest)
    {
        _logger.LogInformation("Updating patient profile with ID: {Id}", patientAggId);
        ArgumentNullException.ThrowIfNull(updateRequest);

        var profileOnFile = await this.GetByIdAsync(patientAggId);
        if (profileOnFile != null)
        {
            // Retire-after-backfill (WP-36/G2): new creates always mint a permanent NC MRN, so
            // this guard only fires for straggler TEMP- rows that predate the deploy-time
            // backfill (0 on prod at build time). Remove with the TEMP- convention cleanup WP.
            if (updateRequest.ActiveStatus
                && (profileOnFile.MedicalRecordNumber?.StartsWith("TEMP-", StringComparison.OrdinalIgnoreCase) ?? false))
            {
                throw new InvalidOperationException(
                    "Cannot activate a patient with a temporary Medical Record Number. Assign a permanent MRN first.");
            }

            await _repository.UpdateAsync(profileOnFile.PatientId, profileOnFile.UserId, updateRequest);
            return true;
        }
        return false;
    }

    public async Task DeleteAsync(int id)
    {
        _logger.LogInformation("Deleting patient profile with ID: {Id}", id);
        var profile = await _repository.GetByIdAsync(id);
        if (profile != null)
        {
            await _repository.DeleteAsync(profile);
        }
    }

    public async Task<bool> VerifyRequestAsync(int patientAggId)
    {
        var profile = await this.GetByIdAsync(patientAggId);
        if (profile != null)
        {
            return true;
        }
        return false;
    }

    public async Task<PagedResult<PatientSessionHistorySummary>> GetSessionHistoryAsync(string? search, int page, int pageSize)
    {
        _logger.LogInformation("Getting patient session-history summaries (search: {Search}, page: {Page}, pageSize: {PageSize}).",
            search, page, pageSize);
        return await _repository.GetSessionHistoryAsync(search, page, pageSize);
    }

    public async Task<IEnumerable<PatientCaretakerSummary>> GetCaretakersForPatientAsync(int patientId)
    {
        _logger.LogInformation("Getting caretakers for patient ID: {Id}", patientId);
        var links = await _patientCaretakerRepo.GetByPatientIdAsync(patientId);
        return links.Select(pc => new PatientCaretakerSummary
        {
            CaretakerId = pc.CaretakerId,
            CaretakerName = pc.Caretaker?.User != null
                ? $"{pc.Caretaker.User.LastName}, {pc.Caretaker.User.FirstName} {pc.Caretaker.User.MiddleName}".Trim()
                : string.Empty,
            IsPrimaryCaretaker = pc.PrimaryCaretaker,
            RelationshipToPatient = pc.RelationshipToPatient
        });
    }

    private static Patient MapToNewPatient(PatientProfileRequest patientRequest, User user)
    {
        return new Patient
        {
            User = user,
            DateOfBirth = patientRequest.DateOfBirth,
            Gender = patientRequest.Gender,
            // WP-36 (G3): MedicalRecordNumber is deliberately NOT mapped from the request — the
            // system mints it inside the create transaction (supersedes the B1 blank→NULL
            // handling; a supplied value was already warned-and-ignored upstream).
            Cedula = string.IsNullOrWhiteSpace(patientRequest.Cedula) ? null : patientRequest.Cedula,
            HasSenadisDiscount = patientRequest.HasSenadisDiscount,
            // WP-37 (SEN-1): ungated at create, same as the flag (SEN-2/G4); .Date — DATE column.
            SenadisExpirationDate = patientRequest.SenadisExpirationDate?.Date,
            RequiresDiscovery = patientRequest.RequiresDiscovery
        };
    }

    private static User MapToNewUser(PatientProfileRequest patientRequest)
    {
        return new User
        {
            FirstName = patientRequest.FirstName,
            MiddleName = patientRequest.MiddleName,
            LastName = patientRequest.LastName,
            Email = patientRequest.Email,
            PhoneNumber = patientRequest.PhoneNumber,
            CreatedTimestamp = DateTime.UtcNow,
            // WP-36 (G5): patients are ACTIVE at create — the inactive-until-MRN gate existed
            // only because MRNs could be missing, and the system now always mints one.
            ActiveStatus = true
        };
    }
}
