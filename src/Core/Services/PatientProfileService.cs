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
        bool isMissingMrn = string.IsNullOrWhiteSpace(patientRequest.MedicalRecordNumber);

        // B1: all four writes are one atomic unit — a failure mid-way (e.g. a rejected Patient
        // INSERT) must not leave a committed, orphaned SystemUser behind.
        var (newUser, newPatient, newRole) = await _unitOfWork.ExecuteAsync(async () =>
        {
            var user = await _userRepo.AddAsync(MapToNewUser(patientRequest, activeStatus: !isMissingMrn));
            var patient = await _patientRepo.AddAsync(MapToNewPatient(patientRequest, user));

            if (isMissingMrn)
            {
                patient.MedicalRecordNumber = $"TEMP-{patient.Id}";
                await _patientRepo.UpdateAsync(patient);
            }

            var role = await _userRoleRepo.AddAsync(patient.MintNewRole());
            return (user, patient, role);
        });

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

    public async Task<bool> UpdateAsync(int patientAggId, PatientProfileUpdateRequest updateRequest)
    {
        _logger.LogInformation("Updating patient profile with ID: {Id}", patientAggId);
        ArgumentNullException.ThrowIfNull(updateRequest);

        var profileOnFile = await this.GetByIdAsync(patientAggId);
        if (profileOnFile != null)
        {
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
            // B1: blank MRN inserts NULL (never '') — '' is a value under the unique key and
            // collides with the next blank-MRN create; the TEMP-{id} stamp follows the insert.
            MedicalRecordNumber = string.IsNullOrWhiteSpace(patientRequest.MedicalRecordNumber)
                ? null
                : patientRequest.MedicalRecordNumber,
            Cedula = string.IsNullOrWhiteSpace(patientRequest.Cedula) ? null : patientRequest.Cedula,
            HasSenadisDiscount = patientRequest.HasSenadisDiscount,
            // WP-37 (SEN-1): ungated at create, same as the flag (SEN-2/G4); .Date — DATE column.
            SenadisExpirationDate = patientRequest.SenadisExpirationDate?.Date,
            RequiresDiscovery = patientRequest.RequiresDiscovery
        };
    }

    private static User MapToNewUser(PatientProfileRequest patientRequest, bool activeStatus = true)
    {
        return new User
        {
            FirstName = patientRequest.FirstName,
            MiddleName = patientRequest.MiddleName,
            LastName = patientRequest.LastName,
            Email = patientRequest.Email,
            PhoneNumber = patientRequest.PhoneNumber,
            CreatedTimestamp = DateTime.UtcNow,
            ActiveStatus = activeStatus
        };
    }
}
