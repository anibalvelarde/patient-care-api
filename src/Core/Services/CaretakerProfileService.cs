
using Microsoft.Extensions.Logging;
using Neurocorp.Api.Core.BusinessObjects.Common;
using Neurocorp.Api.Core.BusinessObjects.Patients;
using Neurocorp.Api.Core.Entities;
using Neurocorp.Api.Core.Exceptions;
using Neurocorp.Api.Core.Interfaces;
using Neurocorp.Api.Core.Interfaces.Repositories;
using Neurocorp.Api.Core.Interfaces.Services;

namespace Neurocorp.Api.Core.Services;

public class CaretakerProfileService : ICaretakerProfileService
{
    private readonly ICaretakerProfileRepository _repository;
    private readonly IUserRepository _userRepo;
    private readonly ICaretakerRepository _caretakerRepo;
    private readonly IUserRoleRepository _userRoleRepo;
    private readonly IPatientCaretakerRepository _patientCaretakerRepo;
    private readonly IPatientRepository? _patientRepo;
    private readonly IUnitOfWork? _unitOfWork;
    private readonly IUserNameResolver? _userNameResolver;
    private readonly ILogger<CaretakerProfileService> _logger;

    public CaretakerProfileService(
        ILogger<CaretakerProfileService> logger,
        ICaretakerProfileRepository repo,
        ICaretakerRepository caretakerRepo,
        IUserRepository userRepo,
        IUserRoleRepository userRoleRepo,
        IPatientCaretakerRepository patientCaretakerRepo,
        // WP-31 (U1): optional so existing test constructions compile unchanged; DI supplies the real one.
        IUserNameResolver? userNameResolver = null,
        // WP-50B: patientRepo + unitOfWork power MakeSelfCaretakerAsync. Optional/trailing for the
        // same reason as userNameResolver — existing test constructions compile unchanged; DI
        // supplies both, and the self-caretaker tests pass real fakes.
        IPatientRepository? patientRepo = null,
        IUnitOfWork? unitOfWork = null)
    {
        _logger = logger;
        _repository = repo;
        _userRepo = userRepo;
        _caretakerRepo = caretakerRepo;
        _userRoleRepo = userRoleRepo;
        _patientCaretakerRepo = patientCaretakerRepo;
        _patientRepo = patientRepo;
        _unitOfWork = unitOfWork;
        _userNameResolver = userNameResolver;
    }

    // WP-31 (U1): resolve audit updater names for a materialized batch (no-op when unresolved/absent).
    private async Task EnrichAuditAsync(IEnumerable<IHasAudit> items)
    {
        if (_userNameResolver != null)
        {
            await AuditEnrichment.ResolveNamesAsync(items, _userNameResolver);
        }
    }

    public async Task<IEnumerable<CaretakerProfile>> GetAllAsync()
    {
        _logger.LogInformation("Getting all caretaker profiles");
        var profiles = await _repository.GetAllAsync();
        await EnrichAuditAsync(profiles);
        return profiles;
    }

    public async Task<CaretakerProfile?> GetByIdAsync(int id)
    {
        _logger.LogInformation("Getting caretaker profile by ID: {id}", id);
        var profile = await _repository.GetByIdAsync(id);
        if (profile != null)
        {
            await EnrichAuditAsync(new[] { profile });
        }
        return profile;
    }

    // WP-30 (U2): paged main list + typeahead lookup — straight passthroughs.
    public async Task<PagedResult<CaretakerProfile>> GetPagedAsync(string? search, bool? isActive, int page, int pageSize)
    {
        _logger.LogInformation("Getting paged caretaker profiles (search: {Search}, isActive: {IsActive}, page: {Page}, pageSize: {PageSize}).",
            search, isActive, page, pageSize);
        var result = await _repository.GetPagedAsync(search, isActive, page, pageSize);
        await EnrichAuditAsync(result.Items);
        return result;
    }

    public async Task<IReadOnlyList<CaretakerLookupItem>> LookupAsync(string query, int maxResults)
    {
        _logger.LogInformation("Caretaker typeahead lookup (query: {Query}, cap: {Cap}).", query, maxResults);
        return await _repository.LookupAsync(query, maxResults);
    }

    public async Task<CaretakerProfile> CreateAsync(CaretakerProfile caretaker)
    {
        _logger.LogError("Operation Not Allowed: Creating new caretaker profile.");
        return await Task.FromException<CaretakerProfile>(new NotImplementedException());
    }

    public async Task UpdateAsync(CaretakerProfile caretaker)
    {
        _logger.LogError("Updating caretaker profile.");
        await Task.FromException<CaretakerProfile>(new NotImplementedException());
    }

    public async Task<CaretakerProfile> CreateAsync(CaretakerProfileRequest request)
    {
        _logger.LogInformation("Creating new patient profile from request.");
        var newUser = await _userRepo.AddAsync(MapToNewUser(request));
        var newCaretaker = await _caretakerRepo.AddAsync(MapToNewCaretaker(request, newUser));
        var newRole = await _userRoleRepo.AddAsync(newCaretaker.MintNewRole());
        _logger.LogInformation($"New Patient Profile was created: Uid[{newUser.Id}], Pid[{newCaretaker.Id}], Role[{newRole.UserRoleId}]");
        return new CaretakerProfile
        {
            CaretakerId = newCaretaker.Id,
            UserId = newUser.Id,
            CaretakerName = $"{newUser.LastName}, {newUser.FirstName} {newUser.MiddleName}".Trim(),
            Email = newUser.Email,
            PhoneNumber = newUser.PhoneNumber,
            CreatedTimestamp = newUser.CreatedTimestamp,
            LastUpdated = MaxTimestamp(newUser.LastUpdatedTimestamp, newCaretaker.LastUpdatedTimestamp),
        };
    }

    public async Task<bool> UpdateAsync(int caretakerAggId, CaretakerProfileUpdateRequest updateRequest)
    {
        _logger.LogInformation("Updating patient profile with ID: {Id}", caretakerAggId);
        ArgumentNullException.ThrowIfNull(updateRequest);

        var profileOnFile = await this.GetByIdAsync(caretakerAggId);
        if (profileOnFile != null)
        {
            await _repository.UpdateAsync(profileOnFile.CaretakerId, profileOnFile.UserId, updateRequest);
            return true;
        }
        return false;
    }    

    public async Task DeleteAsync(int id)
    {
        _logger.LogInformation("Deleting caretaker profile with ID: {Id}", id);
        var profile = await _repository.GetByIdAsync(id);
        if (profile != null)
        {
            await _repository.DeleteAsync(profile);
        }
    }    

    public async Task<bool> VerifyRequestAsync(int caretakerAggId, CaretakerProfileUpdateRequest request)
    {
        var profile = await this.GetByIdAsync(caretakerAggId);
        if (profile != null)
        {
            var verificationResult = profile.CaretakerId.Equals(caretakerAggId);
            var passOrFailed = verificationResult ? "PASS" : "FAIL";
            _logger.LogInformation($"Request for patient profile ID: {caretakerAggId}  Result: {passOrFailed}");
            return verificationResult;
        }
        return false;
    }

    public async Task<IEnumerable<CaretakerPatientSummary>> GetPatientsForCaretakerAsync(int caretakerId)
    {
        _logger.LogInformation("Getting patients for caretaker ID: {Id}", caretakerId);
        var links = await _patientCaretakerRepo.GetByCaretakerIdAsync(caretakerId);
        return links.Select(pc => new CaretakerPatientSummary
        {
            PatientId = pc.PatientId,
            PatientName = pc.Patient?.User != null
                ? $"{pc.Patient.User.LastName}, {pc.Patient.User.FirstName} {pc.Patient.User.MiddleName}".Trim()
                : string.Empty,
            IsPrimaryCaretaker = pc.PrimaryCaretaker,
            RelationshipToPatient = pc.RelationshipToPatient
        });
    }

    public async Task<bool> LinkPatientAsync(int caretakerId, int patientId, bool isPrimary, string? relationship)
    {
        _logger.LogInformation("Linking patient {PatientId} to caretaker {CaretakerId}", patientId, caretakerId);
        var existing = await _patientCaretakerRepo.GetByCompositeKeyAsync(patientId, caretakerId);
        if (existing != null)
        {
            _logger.LogWarning("Link already exists between patient {PatientId} and caretaker {CaretakerId}", patientId, caretakerId);
            return false;
        }

        var entity = new PatientCaretaker
        {
            PatientId = patientId,
            CaretakerId = caretakerId,
            PrimaryCaretaker = isPrimary,
            RelationshipToPatient = relationship
        };
        await _patientCaretakerRepo.AddAsync(entity);
        return true;
    }

    public async Task<bool> UnlinkPatientAsync(int caretakerId, int patientId)
    {
        _logger.LogInformation("Unlinking patient {PatientId} from caretaker {CaretakerId}", patientId, caretakerId);
        var existing = await _patientCaretakerRepo.GetByCompositeKeyAsync(patientId, caretakerId);
        if (existing == null)
        {
            _logger.LogWarning("No link found between patient {PatientId} and caretaker {CaretakerId}", patientId, caretakerId);
            return false;
        }

        // WP-50 (owner ruling 2026-08-22): a "Self" link is permanent — it can't be unlinked in-app.
        // Removing it would strand the caretaker identity (Caretaker row + UserRole) minted on the
        // patient's own SystemUser, and a subsequent MakeSelfCaretaker would mint a duplicate.
        if (string.Equals(existing.RelationshipToPatient, "Self", StringComparison.OrdinalIgnoreCase))
        {
            throw new ConflictException("A self-caretaker link cannot be removed.");
        }

        await _patientCaretakerRepo.DeleteAsync(existing);
        return true;
    }

    // WP-50B: make an existing patient their own caretaker. Attaches a Caretaker role to the
    // patient's EXISTING SystemUser (never mints a second user — so no email is required and the
    // unique-email login identity is untouched) and self-links with RelationshipToPatient="Self".
    // The self-link row is inherently reciprocal (same SystemUser on both ends); a reverse row is
    // deliberately NOT created — that would be a bug (the "recursion" the owner flagged in Q6.3).
    public async Task<CaretakerProfile> MakeSelfCaretakerAsync(int patientId, bool isPrimary)
    {
        if (_patientRepo is null || _unitOfWork is null)
        {
            throw new InvalidOperationException(
                "MakeSelfCaretakerAsync requires IPatientRepository and IUnitOfWork (supplied by DI).");
        }

        _logger.LogInformation("Making patient {PatientId} their own caretaker", patientId);

        var patient = await _patientRepo.GetByIdWithUserAsync(patientId)
            ?? throw new NotFoundException("Patient", patientId);
        var user = patient.User
            ?? throw new NotFoundException($"Patient {patientId} has no associated SystemUser.");

        // Idempotency has two layers. (1) Fast path for the common sequential re-submit: if any
        // existing caretaker link for this patient is backed by the patient's own SystemUser, they
        // are already their own caretaker → friendly 409, no failed insert. (2) Concurrency
        // backstop: `Caretaker.UserID` is UNIQUE in the schema (`Caretaker.UserID_UNIQUE`), so even
        // if two concurrent calls race past this check, the second AddAsync below violates that
        // index (1062) inside its own transaction, rolls back, and surfaces as a 409 (mapped by
        // GlobalExceptionHandler). Duplicates are therefore impossible — this pre-check is an
        // optimization, the schema is the guarantee. (Same reason UserRole(UserID,RoleID) is unique.)
        var existingLinks = await _patientCaretakerRepo.GetByPatientIdAsync(patientId);
        if (existingLinks.Any(l => l.Caretaker?.User?.Id == user.Id))
        {
            throw new ConflictException($"Patient {patientId} is already their own caretaker.");
        }

        return await _unitOfWork.ExecuteAsync(async () =>
        {
            // Attach a Caretaker identity to the patient's existing user (assigning User as a
            // navigation writes the FK without re-inserting the user); MintNewRole reads User.Id.
            var caretaker = await _caretakerRepo.AddAsync(new Caretaker { User = user, Notes = null });
            var role = await _userRoleRepo.AddAsync(caretaker.MintNewRole());

            await _patientCaretakerRepo.AddAsync(new PatientCaretaker
            {
                PatientId = patientId,
                CaretakerId = caretaker.Id,
                PrimaryCaretaker = isPrimary,
                RelationshipToPatient = "Self"
            });

            _logger.LogInformation(
                "Patient {PatientId} is now their own caretaker: Uid[{Uid}], Cid[{Cid}], Role[{RoleId}]",
                patientId, user.Id, caretaker.Id, role.UserRoleId);

            return new CaretakerProfile
            {
                CaretakerId = caretaker.Id,
                UserId = user.Id,
                CaretakerName = $"{user.LastName}, {user.FirstName} {user.MiddleName}".Trim(),
                Email = user.Email,
                PhoneNumber = user.PhoneNumber,
                CreatedTimestamp = caretaker.CreatedTimestamp,
                LastUpdated = MaxTimestamp(user.LastUpdatedTimestamp, caretaker.LastUpdatedTimestamp),
            };
        });
    }

    private static DateTime MaxTimestamp(DateTime timestamp1, DateTime timestamp2)
    {
        return new[] {timestamp1, timestamp2}.Max();
    }

    private static Caretaker MapToNewCaretaker(CaretakerProfileRequest request, User user)
    {
        return new Caretaker
        {
            User = user,
            Notes = request.Notes
        };
    }

    private static User MapToNewUser(CaretakerProfileRequest request)
    {
        return new User
        {
            FirstName = request.FirstName,
            MiddleName = request.MiddleName,
            LastName = request.LastName,
            Email = request.Email,
            PhoneNumber = request.PhoneNumber,
            CreatedTimestamp = DateTime.UtcNow,
            ActiveStatus = true
        };
    }

}