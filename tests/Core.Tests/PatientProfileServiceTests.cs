using Neurocorp.Api.Core.Interfaces.Repositories;
using Moq;
using Neurocorp.Api.Core.Services;
using Neurocorp.Api.Core.BusinessObjects.Patients;
using Neurocorp.Api.Core.Entities;
using Microsoft.Extensions.Logging;

namespace Core.Tests;

public class PatientProfileServiceTests
{
    [Fact]
    public void GoodConstructorTest()
    {
        // arrange
        var fakeRepo = Mock.Of<IPatientProfileRepository>();
        var fakePatientRepo = Mock.Of<IPatientRepository>();
        var fakeUserRepo = Mock.Of<IUserRepository>();
        var fakeUserRoleRepo = Mock.Of<IUserRoleRepository>();
        var fakeLogger = Mock.Of<ILogger<PatientProfileService>>(); 

        // act
        var fakePatientCaretakerRepo = Mock.Of<IPatientCaretakerRepository>();
        var svc = new PatientProfileService(fakeLogger, fakeRepo, fakePatientRepo, fakeUserRepo, fakeUserRoleRepo, fakePatientCaretakerRepo);

        // assert
        Assert.IsType<PatientProfileService>(svc);
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsPatientProfile_WhenPatientExists()
    {
        // Arrange
        var fakePatientRepo = Mock.Of<IPatientRepository>();
        var fakeUserRepo = Mock.Of<IUserRepository>();      
        var fakeUserRoleRepo = Mock.Of<IUserRoleRepository>();
        var fakeLogger = Mock.Of<ILogger<PatientProfileService>>(); 
        int testId = 1;
        var expectedPatient = new PatientProfile { PatientId = testId, PatientName = "John Doe" };
        var _mockRepository = new Mock<IPatientProfileRepository>(MockBehavior.Strict);
        _mockRepository.Setup(repo => repo.GetByIdAsync(testId)).ReturnsAsync(expectedPatient);
        var fakePatientCaretakerRepo = Mock.Of<IPatientCaretakerRepository>();
        var svc = new PatientProfileService(fakeLogger, _mockRepository.Object, fakePatientRepo, fakeUserRepo, fakeUserRoleRepo, fakePatientCaretakerRepo);

        // Act
        var result = await svc.GetByIdAsync(testId);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(expectedPatient.PatientId, result.PatientId);
        Assert.Equal(expectedPatient.PatientName, result.PatientName);
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsNull_WhenPatientDoesNotExist()
    {
        // Arrange
        var fakePatientRepo = Mock.Of<IPatientRepository>();
        var fakeUserRepo = Mock.Of<IUserRepository>();        
        var fakeUserRoleRepo = Mock.Of<IUserRoleRepository>();
        var fakeLogger = Mock.Of<ILogger<PatientProfileService>>(); 
        int testId = 99;
        var expectedPatient = new PatientProfile { PatientId = testId, PatientName = "John Doe" };
        var _mockRepository = new Mock<IPatientProfileRepository>(MockBehavior.Strict);
        _mockRepository.Setup(repo => repo.GetByIdAsync(testId)).ReturnsAsync((PatientProfile?)null);
        var fakePatientCaretakerRepo = Mock.Of<IPatientCaretakerRepository>();
        var svc = new PatientProfileService(fakeLogger, _mockRepository.Object, fakePatientRepo, fakeUserRepo, fakeUserRoleRepo, fakePatientCaretakerRepo);

        // Act
        var result = await svc.GetByIdAsync(testId);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task CreateProfileInstanceAsync_Throws()
    {
        // Arrange
        var fakePatientRepo = Mock.Of<IPatientRepository>();
        var fakeUserRepo = Mock.Of<IUserRepository>();
        var fakeRepo = Mock.Of<IPatientProfileRepository>();
        var fakeUserRoleRepo = Mock.Of<IUserRoleRepository>();
        var fakeLogger = Mock.Of<ILogger<PatientProfileService>>(); 

        var fakePatientCaretakerRepo = Mock.Of<IPatientCaretakerRepository>();
        var svc = new PatientProfileService(fakeLogger, fakeRepo, fakePatientRepo, fakeUserRepo, fakeUserRoleRepo, fakePatientCaretakerRepo);

        // Act & Assert
        await Assert.ThrowsAsync<NotImplementedException>(() => 
            svc.CreateAsync(new PatientProfile()));
    }

    [Fact]
    public async Task GetListOfProfilesAsync_Throws()
    {
        // Arrange
        var fakePatientRepo = Mock.Of<IPatientRepository>();
        var fakeUserRepo = Mock.Of<IUserRepository>();
        var fakeUserRoleRepo = Mock.Of<IUserRoleRepository>();
        var fakeLogger = Mock.Of<ILogger<PatientProfileService>>(); 

        var _mockRepository = new Mock<IPatientProfileRepository>(MockBehavior.Strict);
        _mockRepository.Setup(repo => repo.GetAllAsync())
            .ReturnsAsync([]);
        var fakePatientCaretakerRepo = Mock.Of<IPatientCaretakerRepository>();
        var svc = new PatientProfileService(fakeLogger, _mockRepository.Object, fakePatientRepo, fakeUserRepo, fakeUserRoleRepo, fakePatientCaretakerRepo);

        // Act
        var result = await svc.GetAllAsync();

        // Assert
        Assert.NotNull(result);
        Assert.IsAssignableFrom<IEnumerable<PatientProfile>>(result);
    }

    [Fact]
    public async Task CreateAsync_WithoutMrn_GeneratesTempMrn()
    {
        // Arrange
        var fakeLogger = Mock.Of<ILogger<PatientProfileService>>();
        var mockProfileRepo = Mock.Of<IPatientProfileRepository>();
        var mockPatientRepo = new Mock<IPatientRepository>();
        var mockUserRepo = new Mock<IUserRepository>();
        var mockUserRoleRepo = new Mock<IUserRoleRepository>();

        var savedUser = new User { Id = 10, FirstName = "Jane", LastName = "Doe", MiddleName = "", Email = "j@d.com", PhoneNumber = "555", ActiveStatus = false };
        mockUserRepo.Setup(r => r.AddAsync(It.IsAny<User>())).ReturnsAsync(savedUser);

        var savedPatient = new Patient { Id = 5, User = savedUser, MedicalRecordNumber = "", DateOfBirth = DateTime.Today, Gender = "F" };
        mockPatientRepo.Setup(r => r.AddAsync(It.IsAny<Patient>())).ReturnsAsync(savedPatient);
        mockPatientRepo.Setup(r => r.UpdateAsync(It.IsAny<Patient>())).Returns(Task.CompletedTask);

        mockUserRoleRepo.Setup(r => r.AddAsync(It.IsAny<UserRole>())).ReturnsAsync(new UserRole { UserRoleId = 1 });

        var fakePatientCaretakerRepo = Mock.Of<IPatientCaretakerRepository>();
        var svc = new PatientProfileService(fakeLogger, mockProfileRepo, mockPatientRepo.Object, mockUserRepo.Object, mockUserRoleRepo.Object, fakePatientCaretakerRepo);
        var request = new PatientProfileRequest { FirstName = "Jane", LastName = "Doe", Email = "j@d.com", PhoneNumber = "555", Gender = "F", DateOfBirth = DateTime.Today, MedicalRecordNumber = "" };

        // Act
        var result = await svc.CreateAsync(request);

        // Assert
        Assert.Equal("TEMP-5", result.MedicalRecordNumber);
        Assert.False(result.IsActive);
        mockPatientRepo.Verify(r => r.UpdateAsync(It.IsAny<Patient>()), Times.Once);
    }

    [Fact]
    public async Task CreateAsync_WithMrn_UsesProvidedMrn()
    {
        // Arrange
        var fakeLogger = Mock.Of<ILogger<PatientProfileService>>();
        var mockProfileRepo = Mock.Of<IPatientProfileRepository>();
        var mockPatientRepo = new Mock<IPatientRepository>();
        var mockUserRepo = new Mock<IUserRepository>();
        var mockUserRoleRepo = new Mock<IUserRoleRepository>();

        var savedUser = new User { Id = 10, FirstName = "Jane", LastName = "Doe", MiddleName = "", Email = "j@d.com", PhoneNumber = "555", ActiveStatus = true };
        mockUserRepo.Setup(r => r.AddAsync(It.IsAny<User>())).ReturnsAsync(savedUser);

        var savedPatient = new Patient { Id = 5, User = savedUser, MedicalRecordNumber = "MRN-001", DateOfBirth = DateTime.Today, Gender = "F" };
        mockPatientRepo.Setup(r => r.AddAsync(It.IsAny<Patient>())).ReturnsAsync(savedPatient);

        mockUserRoleRepo.Setup(r => r.AddAsync(It.IsAny<UserRole>())).ReturnsAsync(new UserRole { UserRoleId = 1 });

        var fakePatientCaretakerRepo = Mock.Of<IPatientCaretakerRepository>();
        var svc = new PatientProfileService(fakeLogger, mockProfileRepo, mockPatientRepo.Object, mockUserRepo.Object, mockUserRoleRepo.Object, fakePatientCaretakerRepo);
        var request = new PatientProfileRequest { FirstName = "Jane", LastName = "Doe", Email = "j@d.com", PhoneNumber = "555", Gender = "F", DateOfBirth = DateTime.Today, MedicalRecordNumber = "MRN-001" };

        // Act
        var result = await svc.CreateAsync(request);

        // Assert
        Assert.Equal("MRN-001", result.MedicalRecordNumber);
        Assert.True(result.IsActive);
        mockPatientRepo.Verify(r => r.UpdateAsync(It.IsAny<Patient>()), Times.Never);
    }

    [Fact]
    public async Task UpdateAsync_ActivateWithTempMrn_ThrowsInvalidOperation()
    {
        // Arrange
        var fakeLogger = Mock.Of<ILogger<PatientProfileService>>();
        var mockProfileRepo = new Mock<IPatientProfileRepository>();
        var fakePatientRepo = Mock.Of<IPatientRepository>();
        var fakeUserRepo = Mock.Of<IUserRepository>();
        var fakeUserRoleRepo = Mock.Of<IUserRoleRepository>();

        var profileOnFile = new PatientProfile { PatientId = 1, UserId = 10, MedicalRecordNumber = "TEMP-1", PatientName = "Test" };
        mockProfileRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(profileOnFile);

        var fakePatientCaretakerRepo = Mock.Of<IPatientCaretakerRepository>();
        var svc = new PatientProfileService(fakeLogger, mockProfileRepo.Object, fakePatientRepo, fakeUserRepo, fakeUserRoleRepo, fakePatientCaretakerRepo);
        var updateRequest = new PatientProfileUpdateRequest { ActiveStatus = true };

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.UpdateAsync(1, updateRequest));
    }

    [Fact]
    public async Task UpdateAsync_ActivateWithRealMrn_Succeeds()
    {
        // Arrange
        var fakeLogger = Mock.Of<ILogger<PatientProfileService>>();
        var mockProfileRepo = new Mock<IPatientProfileRepository>();
        var fakePatientRepo = Mock.Of<IPatientRepository>();
        var fakeUserRepo = Mock.Of<IUserRepository>();
        var fakeUserRoleRepo = Mock.Of<IUserRoleRepository>();

        var profileOnFile = new PatientProfile { PatientId = 1, UserId = 10, MedicalRecordNumber = "MRN-001", PatientName = "Test" };
        mockProfileRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(profileOnFile);
        mockProfileRepo.Setup(r => r.UpdateAsync(1, 10, It.IsAny<PatientProfileUpdateRequest>())).ReturnsAsync(profileOnFile);

        var fakePatientCaretakerRepo = Mock.Of<IPatientCaretakerRepository>();
        var svc = new PatientProfileService(fakeLogger, mockProfileRepo.Object, fakePatientRepo, fakeUserRepo, fakeUserRoleRepo, fakePatientCaretakerRepo);
        var updateRequest = new PatientProfileUpdateRequest { ActiveStatus = true };

        // Act
        var result = await svc.UpdateAsync(1, updateRequest);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task UpdateAsync_DeactivateWithTempMrn_Succeeds()
    {
        // Arrange
        var fakeLogger = Mock.Of<ILogger<PatientProfileService>>();
        var mockProfileRepo = new Mock<IPatientProfileRepository>();
        var fakePatientRepo = Mock.Of<IPatientRepository>();
        var fakeUserRepo = Mock.Of<IUserRepository>();
        var fakeUserRoleRepo = Mock.Of<IUserRoleRepository>();

        var profileOnFile = new PatientProfile { PatientId = 1, UserId = 10, MedicalRecordNumber = "TEMP-1", PatientName = "Test" };
        mockProfileRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(profileOnFile);
        mockProfileRepo.Setup(r => r.UpdateAsync(1, 10, It.IsAny<PatientProfileUpdateRequest>())).ReturnsAsync(profileOnFile);

        var fakePatientCaretakerRepo = Mock.Of<IPatientCaretakerRepository>();
        var svc = new PatientProfileService(fakeLogger, mockProfileRepo.Object, fakePatientRepo, fakeUserRepo, fakeUserRoleRepo, fakePatientCaretakerRepo);
        var updateRequest = new PatientProfileUpdateRequest { ActiveStatus = false };

        // Act
        var result = await svc.UpdateAsync(1, updateRequest);

        // Assert
        Assert.True(result);
    }
}