using Neurocorp.Api.Core.Interfaces;
using Neurocorp.Api.Core.Interfaces.Repositories;
using Moq;
using Neurocorp.Api.Core.Services;
using Neurocorp.Api.Core.BusinessObjects.Patients;
using Neurocorp.Api.Core.Entities;
using Microsoft.Extensions.Logging;

namespace Core.Tests;

public class PatientProfileServiceTests
{
    private static ITherapySessionRepository FakeTherapySessionRepo() => Mock.Of<ITherapySessionRepository>();

    /// <summary>Unit of work that just runs the operation — commit/rollback is the real implementation's concern.</summary>
    private static IUnitOfWork PassThroughUow()
    {
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.ExecuteAsync(It.IsAny<Func<Task<(User, Patient, UserRole)>>>()))
           .Returns((Func<Task<(User, Patient, UserRole)>> op) => op());
        return uow.Object;
    }

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
        var svc = new PatientProfileService(fakeLogger, fakeRepo, fakePatientRepo, fakeUserRepo, fakeUserRoleRepo, fakePatientCaretakerRepo, FakeTherapySessionRepo(), PassThroughUow());

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
        var svc = new PatientProfileService(fakeLogger, _mockRepository.Object, fakePatientRepo, fakeUserRepo, fakeUserRoleRepo, fakePatientCaretakerRepo, FakeTherapySessionRepo(), PassThroughUow());

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
        var svc = new PatientProfileService(fakeLogger, _mockRepository.Object, fakePatientRepo, fakeUserRepo, fakeUserRoleRepo, fakePatientCaretakerRepo, FakeTherapySessionRepo(), PassThroughUow());

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
        var svc = new PatientProfileService(fakeLogger, fakeRepo, fakePatientRepo, fakeUserRepo, fakeUserRoleRepo, fakePatientCaretakerRepo, FakeTherapySessionRepo(), PassThroughUow());

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
        var svc = new PatientProfileService(fakeLogger, _mockRepository.Object, fakePatientRepo, fakeUserRepo, fakeUserRoleRepo, fakePatientCaretakerRepo, FakeTherapySessionRepo(), PassThroughUow());

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
        var svc = new PatientProfileService(fakeLogger, mockProfileRepo, mockPatientRepo.Object, mockUserRepo.Object, mockUserRoleRepo.Object, fakePatientCaretakerRepo, FakeTherapySessionRepo(), PassThroughUow());
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
        var svc = new PatientProfileService(fakeLogger, mockProfileRepo, mockPatientRepo.Object, mockUserRepo.Object, mockUserRoleRepo.Object, fakePatientCaretakerRepo, FakeTherapySessionRepo(), PassThroughUow());
        var request = new PatientProfileRequest { FirstName = "Jane", LastName = "Doe", Email = "j@d.com", PhoneNumber = "555", Gender = "F", DateOfBirth = DateTime.Today, MedicalRecordNumber = "MRN-001" };

        // Act
        var result = await svc.CreateAsync(request);

        // Assert
        Assert.Equal("MRN-001", result.MedicalRecordNumber);
        Assert.True(result.IsActive);
        mockPatientRepo.Verify(r => r.UpdateAsync(It.IsAny<Patient>()), Times.Never);
    }

    [Theory]
    [InlineData("001-1234567-8", "001-1234567-8")] // provided value flows to the new entity
    [InlineData("", null)]                          // blank normalized to null (avoids unique-constraint clash)
    [InlineData("   ", null)]                        // whitespace normalized to null
    public async Task CreateAsync_MapsCedula_NormalizingBlankToNull(string requestCedula, string? expectedCedula)
    {
        // Arrange
        var fakeLogger = Mock.Of<ILogger<PatientProfileService>>();
        var mockProfileRepo = Mock.Of<IPatientProfileRepository>();
        var mockPatientRepo = new Mock<IPatientRepository>();
        var mockUserRepo = new Mock<IUserRepository>();
        var mockUserRoleRepo = new Mock<IUserRoleRepository>();

        var savedUser = new User { Id = 10, FirstName = "Jane", LastName = "Doe", MiddleName = "", Email = "j@d.com", PhoneNumber = "555", ActiveStatus = true };
        mockUserRepo.Setup(r => r.AddAsync(It.IsAny<User>())).ReturnsAsync(savedUser);

        // Capture the Patient the service builds so we can assert on the mapped Cedula.
        Patient? captured = null;
        mockPatientRepo.Setup(r => r.AddAsync(It.IsAny<Patient>()))
            .Callback<Patient>(p => captured = p)
            .ReturnsAsync((Patient p) => { p.Id = 5; return p; });

        mockUserRoleRepo.Setup(r => r.AddAsync(It.IsAny<UserRole>())).ReturnsAsync(new UserRole { UserRoleId = 1 });

        var fakePatientCaretakerRepo = Mock.Of<IPatientCaretakerRepository>();
        var svc = new PatientProfileService(fakeLogger, mockProfileRepo, mockPatientRepo.Object, mockUserRepo.Object, mockUserRoleRepo.Object, fakePatientCaretakerRepo, FakeTherapySessionRepo(), PassThroughUow());
        var request = new PatientProfileRequest { FirstName = "Jane", LastName = "Doe", Email = "j@d.com", PhoneNumber = "555", Gender = "F", DateOfBirth = DateTime.Today, MedicalRecordNumber = "MRN-002", Cedula = requestCedula };

        // Act
        var result = await svc.CreateAsync(request);

        // Assert
        Assert.NotNull(captured);
        Assert.Equal(expectedCedula, captured!.Cedula);
        Assert.Equal(expectedCedula, result.Cedula);
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
        var svc = new PatientProfileService(fakeLogger, mockProfileRepo.Object, fakePatientRepo, fakeUserRepo, fakeUserRoleRepo, fakePatientCaretakerRepo, FakeTherapySessionRepo(), PassThroughUow());
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
        var svc = new PatientProfileService(fakeLogger, mockProfileRepo.Object, fakePatientRepo, fakeUserRepo, fakeUserRoleRepo, fakePatientCaretakerRepo, FakeTherapySessionRepo(), PassThroughUow());
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
        var svc = new PatientProfileService(fakeLogger, mockProfileRepo.Object, fakePatientRepo, fakeUserRepo, fakeUserRoleRepo, fakePatientCaretakerRepo, FakeTherapySessionRepo(), PassThroughUow());
        var updateRequest = new PatientProfileUpdateRequest { ActiveStatus = false };

        // Act
        var result = await svc.UpdateAsync(1, updateRequest);

        // Assert
        Assert.True(result);
    }

    // B1 regression (intake 2026-07-07-001): CreateAsync was non-transactional — the SystemUser
    // committed before the Patient INSERT failed, leaving orphaned SystemUsers in prod.
    // Every write must happen inside the unit of work so a failure rolls all of them back.
    [Fact]
    public async Task CreateAsync_RunsAllWritesInsideUnitOfWork()
    {
        // Arrange — a unit of work that swallows the operation without running it. If any
        // repository write still happens, that write lives OUTSIDE the transaction boundary.
        var fakeLogger = Mock.Of<ILogger<PatientProfileService>>();
        var mockProfileRepo = Mock.Of<IPatientProfileRepository>();
        var mockPatientRepo = new Mock<IPatientRepository>();
        var mockUserRepo = new Mock<IUserRepository>();
        var mockUserRoleRepo = new Mock<IUserRoleRepository>();

        var cannedUser = new User { Id = 10, FirstName = "Jane", LastName = "Doe", MiddleName = "", Email = "j@d.com", PhoneNumber = "555", ActiveStatus = true };
        var cannedPatient = new Patient { Id = 5, User = cannedUser, MedicalRecordNumber = "MRN-001", DateOfBirth = DateTime.Today, Gender = "Female" };
        var swallowingUow = new Mock<IUnitOfWork>();
        swallowingUow.Setup(u => u.ExecuteAsync(It.IsAny<Func<Task<(User, Patient, UserRole)>>>()))
            .ReturnsAsync((cannedUser, cannedPatient, new UserRole { UserRoleId = 1 }));

        var fakePatientCaretakerRepo = Mock.Of<IPatientCaretakerRepository>();
        var svc = new PatientProfileService(fakeLogger, mockProfileRepo, mockPatientRepo.Object, mockUserRepo.Object, mockUserRoleRepo.Object, fakePatientCaretakerRepo, FakeTherapySessionRepo(), swallowingUow.Object);
        var request = new PatientProfileRequest { FirstName = "Jane", LastName = "Doe", Email = "j@d.com", PhoneNumber = "555", Gender = "Female", DateOfBirth = DateTime.Today, MedicalRecordNumber = "MRN-001" };

        // Act
        await svc.CreateAsync(request);

        // Assert — the operation went through the unit of work, and no write escaped it.
        swallowingUow.Verify(u => u.ExecuteAsync(It.IsAny<Func<Task<(User, Patient, UserRole)>>>()), Times.Once);
        mockUserRepo.Verify(r => r.AddAsync(It.IsAny<User>()), Times.Never);
        mockPatientRepo.Verify(r => r.AddAsync(It.IsAny<Patient>()), Times.Never);
        mockUserRoleRepo.Verify(r => r.AddAsync(It.IsAny<UserRole>()), Times.Never);
    }

    // B1 regression: a blank MRN was INSERTed as '' (not NULL) before the TEMP-{id} update —
    // a latent unique-key collision under concurrency or failed-create debris.
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CreateAsync_BlankMrn_InsertsNullNotEmptyString(string blankMrn)
    {
        // Arrange
        var fakeLogger = Mock.Of<ILogger<PatientProfileService>>();
        var mockProfileRepo = Mock.Of<IPatientProfileRepository>();
        var mockPatientRepo = new Mock<IPatientRepository>();
        var mockUserRepo = new Mock<IUserRepository>();
        var mockUserRoleRepo = new Mock<IUserRoleRepository>();

        var savedUser = new User { Id = 10, FirstName = "Jane", LastName = "Doe", MiddleName = "", Email = "j@d.com", PhoneNumber = "555", ActiveStatus = false };
        mockUserRepo.Setup(r => r.AddAsync(It.IsAny<User>())).ReturnsAsync(savedUser);

        // Capture the MRN VALUE at insert time — the service mutates the same instance to
        // TEMP-{id} right after, so holding the object reference would assert the wrong moment.
        bool insertSeen = false;
        string? mrnAtInsert = null;
        mockPatientRepo.Setup(r => r.AddAsync(It.IsAny<Patient>()))
            .Callback<Patient>(p => { insertSeen = true; mrnAtInsert = p.MedicalRecordNumber; })
            .ReturnsAsync((Patient p) => { p.Id = 5; return p; });
        mockPatientRepo.Setup(r => r.UpdateAsync(It.IsAny<Patient>())).Returns(Task.CompletedTask);
        mockUserRoleRepo.Setup(r => r.AddAsync(It.IsAny<UserRole>())).ReturnsAsync(new UserRole { UserRoleId = 1 });

        var fakePatientCaretakerRepo = Mock.Of<IPatientCaretakerRepository>();
        var svc = new PatientProfileService(fakeLogger, mockProfileRepo, mockPatientRepo.Object, mockUserRepo.Object, mockUserRoleRepo.Object, fakePatientCaretakerRepo, FakeTherapySessionRepo(), PassThroughUow());
        var request = new PatientProfileRequest { FirstName = "Jane", LastName = "Doe", Email = "j@d.com", PhoneNumber = "555", Gender = "Female", DateOfBirth = DateTime.Today, MedicalRecordNumber = blankMrn };

        // Act
        var result = await svc.CreateAsync(request);

        // Assert — the INSERTed row carried NULL, and the TEMP MRN was stamped afterwards.
        Assert.True(insertSeen);
        Assert.Null(mrnAtInsert);
        Assert.Equal("TEMP-5", result.MedicalRecordNumber);
    }
}