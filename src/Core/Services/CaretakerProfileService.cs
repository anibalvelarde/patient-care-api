
using Microsoft.Extensions.Logging;
using Neurocorp.Api.Core.BusinessObjects.Patients;
using Neurocorp.Api.Core.Entities;
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
    private readonly ILogger<CaretakerProfileService> _logger;

    public CaretakerProfileService(
        ILogger<CaretakerProfileService> logger,
        ICaretakerProfileRepository repo,
        ICaretakerRepository caretakerRepo,
        IUserRepository userRepo,
        IUserRoleRepository userRoleRepo,
        IPatientCaretakerRepository patientCaretakerRepo)
    {
        _logger = logger;
        _repository = repo;
        _userRepo = userRepo;
        _caretakerRepo = caretakerRepo;
        _userRoleRepo = userRoleRepo;
        _patientCaretakerRepo = patientCaretakerRepo;
        _repository = repo;
    }

    public async Task<IEnumerable<CaretakerProfile>> GetAllAsync()
    {
        _logger.LogInformation("Getting all caretaker profiles");
        return await _repository.GetAllAsync();
    }

    public async Task<CaretakerProfile?> GetByIdAsync(int id)
    {
        _logger.LogInformation("Getting caretaker profile by ID: {id}", id);
        return await _repository.GetByIdAsync(id);
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

        await _patientCaretakerRepo.DeleteAsync(existing);
        return true;
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