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

    // ---- WP-36 (NP-1): system-managed MRN minting — NC{yy}-#### -------------------------------

    /// <summary>
    /// Expected NC prefix computed the way the service does per G6 (clinic-local year, not the
    /// server's — the deployed container runs UTC): Panama is fixed UTC-5 with no DST, so a
    /// plain offset subtraction from UTC is exact and matches any TimeZoneInfo resolution.
    /// </summary>
    private static string ExpectedNcPrefix()
        => $"NC{(DateTime.UtcNow - TimeSpan.FromHours(5)).Year % 100:D2}-";

    private sealed record CreateHarness(
        PatientProfileService Svc,
        Mock<IPatientRepository> PatientRepo,
        Mock<IUserRepository> UserRepo,
        Mock<IUserRoleRepository> UserRoleRepo,
        Mock<IUnitOfWork> Uow,
        Mock<ILogger<PatientProfileService>> Logger);

    /// <summary>Create-flow harness: pass-through UoW (verifiable), id-assigning Add mocks.</summary>
    private static CreateHarness BuildCreateHarness(int maxMrnSequence = 0)
    {
        var logger = new Mock<ILogger<PatientProfileService>>();
        var patientRepo = new Mock<IPatientRepository>();
        var userRepo = new Mock<IUserRepository>();
        var userRoleRepo = new Mock<IUserRoleRepository>();

        userRepo.Setup(r => r.AddAsync(It.IsAny<User>())).ReturnsAsync((User u) => { u.Id = 10; return u; });
        patientRepo.Setup(r => r.AddAsync(It.IsAny<Patient>())).ReturnsAsync((Patient p) => { p.Id = 5; return p; });
        patientRepo.Setup(r => r.GetMaxMrnSequenceAsync(It.IsAny<string>())).ReturnsAsync(maxMrnSequence);
        userRoleRepo.Setup(r => r.AddAsync(It.IsAny<UserRole>())).ReturnsAsync(new UserRole { UserRoleId = 1 });

        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.ExecuteAsync(It.IsAny<Func<Task<(User, Patient, UserRole)>>>()))
           .Returns((Func<Task<(User, Patient, UserRole)>> op) => op());

        var svc = new PatientProfileService(logger.Object, Mock.Of<IPatientProfileRepository>(),
            patientRepo.Object, userRepo.Object, userRoleRepo.Object,
            Mock.Of<IPatientCaretakerRepository>(), FakeTherapySessionRepo(), uow.Object);
        return new CreateHarness(svc, patientRepo, userRepo, userRoleRepo, uow, logger);
    }

    private static PatientProfileRequest NewCreateRequest(string mrn = "") => new()
    {
        FirstName = "Jane", LastName = "Doe", Email = "j@d.com", PhoneNumber = "555",
        Gender = "Female", DateOfBirth = DateTime.Today, Cedula = "8-123-4567",
        MedicalRecordNumber = mrn,
    };

    private static void VerifyWarningLogged(Mock<ILogger<PatientProfileService>> logger, Times times)
        => logger.Verify(l => l.Log(LogLevel.Warning, It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((v, _) => true), It.IsAny<Exception?>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()), times);

    /// <summary>Duplicate-key shape as GlobalExceptionHandler sees it: outer save exception
    /// wrapping the MySQL 1062 message naming the violated key.</summary>
    private static Exception DuplicateKeyException(string keyName)
        => new InvalidOperationException("Save failed.",
            new Exception($"Duplicate entry 'x' for key '{keyName}'"));

    // WP-36 (G1a default): empty table / first create of a Panama-calendar year mints {yy}-0001.
    [Fact]
    public async Task CreateAsync_MintsFirstMrnOfYear_As0001()
    {
        var h = BuildCreateHarness(maxMrnSequence: 0);

        Patient? atInsert = null;
        h.PatientRepo.Setup(r => r.AddAsync(It.IsAny<Patient>()))
            .Callback<Patient>(p => atInsert = p)
            .ReturnsAsync((Patient p) => { p.Id = 5; return p; });

        var result = await h.Svc.CreateAsync(NewCreateRequest());

        Assert.Equal($"{ExpectedNcPrefix()}0001", result.MedicalRecordNumber);
        // The mint happens INSIDE the transaction, on the INSERT itself — no TEMP- stamp, no
        // post-insert UPDATE round trip.
        Assert.Equal(result.MedicalRecordNumber, atInsert!.MedicalRecordNumber);
        h.PatientRepo.Verify(r => r.UpdateAsync(It.IsAny<Patient>()), Times.Never);
        h.PatientRepo.Verify(r => r.GetMaxMrnSequenceAsync(ExpectedNcPrefix()), Times.Once);
    }

    // WP-36: sequence continues from the year's existing MAX, always {n:04d}-padded.
    [Theory]
    [InlineData(7, "0008")]
    [InlineData(41, "0042")]
    [InlineData(999, "1000")]
    [InlineData(9998, "9999")]
    public async Task CreateAsync_MintContinuesFromExistingMax_ZeroPadded(int existingMax, string expectedSeq)
    {
        var h = BuildCreateHarness(existingMax);

        var result = await h.Svc.CreateAsync(NewCreateRequest());

        Assert.Equal($"{ExpectedNcPrefix()}{expectedSeq}", result.MedicalRecordNumber);
    }

    // WP-36 (G3): a client-supplied MRN on create is IGNORED (logged, not 400) — old-UI
    // tolerance during the API-first deploy gap.
    [Fact]
    public async Task CreateAsync_SuppliedMrn_IsIgnoredAndWarned()
    {
        var h = BuildCreateHarness(maxMrnSequence: 3);

        Patient? atInsert = null;
        h.PatientRepo.Setup(r => r.AddAsync(It.IsAny<Patient>()))
            .Callback<Patient>(p => atInsert = p)
            .ReturnsAsync((Patient p) => { p.Id = 5; return p; });

        var result = await h.Svc.CreateAsync(NewCreateRequest(mrn: "L26-0099"));

        Assert.Equal($"{ExpectedNcPrefix()}0004", result.MedicalRecordNumber);
        Assert.Equal(result.MedicalRecordNumber, atInsert!.MedicalRecordNumber);
        VerifyWarningLogged(h.Logger, Times.Once());
    }

    [Fact]
    public async Task CreateAsync_NoSuppliedMrn_DoesNotWarn()
    {
        var h = BuildCreateHarness();

        await h.Svc.CreateAsync(NewCreateRequest());

        VerifyWarningLogged(h.Logger, Times.Never());
    }

    // WP-36 (G5): patients are ACTIVE at create — the inactive-until-MRN gate existed only
    // because MRNs could be missing, and the system now always mints one.
    [Fact]
    public async Task CreateAsync_PatientIsActiveAtCreate()
    {
        var h = BuildCreateHarness();

        User? atInsert = null;
        h.UserRepo.Setup(r => r.AddAsync(It.IsAny<User>()))
            .Callback<User>(u => atInsert = u)
            .ReturnsAsync((User u) => { u.Id = 10; return u; });

        var result = await h.Svc.CreateAsync(NewCreateRequest());

        Assert.True(atInsert!.ActiveStatus);
        Assert.True(result.IsActive);
    }

    // WP-36 (G1a): two stations can read the same MAX and mint the same NC{yy}-#### — on the
    // MRN unique-key collision the whole create transaction re-runs ONCE with a re-read
    // sequence.
    [Fact]
    public async Task CreateAsync_MrnDuplicateRace_RetriesOnceAndSucceeds()
    {
        var h = BuildCreateHarness();
        var maxValues = new Queue<int>([41, 42]); // second read sees the winner's committed row
        h.PatientRepo.Setup(r => r.GetMaxMrnSequenceAsync(It.IsAny<string>()))
            .ReturnsAsync(() => maxValues.Dequeue());
        var addCalls = 0;
        h.PatientRepo.Setup(r => r.AddAsync(It.IsAny<Patient>()))
            .ReturnsAsync((Patient p) =>
            {
                if (++addCalls == 1) throw DuplicateKeyException("Patient.MedicalRecordNumber");
                p.Id = 5;
                return p;
            });

        var result = await h.Svc.CreateAsync(NewCreateRequest());

        Assert.Equal($"{ExpectedNcPrefix()}0043", result.MedicalRecordNumber);
        // The WHOLE unit of work re-ran (the rolled-back user insert included), exactly twice.
        h.Uow.Verify(u => u.ExecuteAsync(It.IsAny<Func<Task<(User, Patient, UserRole)>>>()), Times.Exactly(2));
        h.UserRepo.Verify(r => r.AddAsync(It.IsAny<User>()), Times.Exactly(2));
    }

    // WP-36 (G1a): a SECOND collision propagates to the standard duplicate-key 409 path.
    [Fact]
    public async Task CreateAsync_MrnDuplicateRace_SecondCollision_Propagates()
    {
        var h = BuildCreateHarness();
        h.PatientRepo.Setup(r => r.AddAsync(It.IsAny<Patient>()))
            .ThrowsAsync(DuplicateKeyException("Patient.MedicalRecordNumber"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => h.Svc.CreateAsync(NewCreateRequest()));

        h.Uow.Verify(u => u.ExecuteAsync(It.IsAny<Func<Task<(User, Patient, UserRole)>>>()), Times.Exactly(2));
    }

    // WP-36 (G1a): ONLY an MRN-key collision is retryable — a cedula or email duplicate is a
    // genuine client 409 and must propagate on the first attempt.
    [Theory]
    [InlineData("uq_patient_cedula")]
    [InlineData("uq_systemuser_email")]
    public async Task CreateAsync_NonMrnDuplicate_DoesNotRetry(string violatedKey)
    {
        var h = BuildCreateHarness();
        h.PatientRepo.Setup(r => r.AddAsync(It.IsAny<Patient>()))
            .ThrowsAsync(DuplicateKeyException(violatedKey));

        await Assert.ThrowsAsync<InvalidOperationException>(() => h.Svc.CreateAsync(NewCreateRequest()));

        h.Uow.Verify(u => u.ExecuteAsync(It.IsAny<Func<Task<(User, Patient, UserRole)>>>()), Times.Once);
    }

    // WP-25 (F5): blank/missing cedula is now rejected upstream by [Required] model validation,
    // so the old blank→NULL theory cases are gone; MapToNewPatient keeps the normalization purely
    // as defense-in-depth. This test asserts the happy path: the value flows to the new entity.
    [Theory]
    [InlineData("001-1234567-8", "001-1234567-8")] // provided value flows to the new entity
    public async Task CreateAsync_MapsCedula_ValueFlowsToNewPatient(string requestCedula, string? expectedCedula)
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

    // WP-37 (SEN-1/SEN-2): the expiry is settable at CREATE by any patient-creating role —
    // ungated, same rule as the flag (the claim gates later edits only) — and flows to both
    // the new entity and the create response.
    [Fact]
    public async Task CreateAsync_MapsSenadisExpirationDate_ValueFlowsToNewPatientAndResponse()
    {
        // Arrange
        var fakeLogger = Mock.Of<ILogger<PatientProfileService>>();
        var mockProfileRepo = Mock.Of<IPatientProfileRepository>();
        var mockPatientRepo = new Mock<IPatientRepository>();
        var mockUserRepo = new Mock<IUserRepository>();
        var mockUserRoleRepo = new Mock<IUserRoleRepository>();

        var savedUser = new User { Id = 10, FirstName = "Jane", LastName = "Doe", MiddleName = "", Email = "j@d.com", PhoneNumber = "555", ActiveStatus = true };
        mockUserRepo.Setup(r => r.AddAsync(It.IsAny<User>())).ReturnsAsync(savedUser);

        Patient? captured = null;
        mockPatientRepo.Setup(r => r.AddAsync(It.IsAny<Patient>()))
            .Callback<Patient>(p => captured = p)
            .ReturnsAsync((Patient p) => { p.Id = 5; return p; });

        mockUserRoleRepo.Setup(r => r.AddAsync(It.IsAny<UserRole>())).ReturnsAsync(new UserRole { UserRoleId = 1 });

        var fakePatientCaretakerRepo = Mock.Of<IPatientCaretakerRepository>();
        var svc = new PatientProfileService(fakeLogger, mockProfileRepo, mockPatientRepo.Object, mockUserRepo.Object, mockUserRoleRepo.Object, fakePatientCaretakerRepo, FakeTherapySessionRepo(), PassThroughUow());
        var expiry = new DateTime(2027, 6, 30);
        var request = new PatientProfileRequest
        {
            FirstName = "Jane", LastName = "Doe", Email = "j@d.com", PhoneNumber = "555",
            Gender = "F", DateOfBirth = DateTime.Today, MedicalRecordNumber = "MRN-002",
            Cedula = "001-1234567-8", HasSenadisDiscount = true, SenadisExpirationDate = expiry,
        };

        // Act
        var result = await svc.CreateAsync(request);

        // Assert
        Assert.NotNull(captured);
        Assert.True(captured!.HasSenadisDiscount);
        Assert.Equal(expiry, captured.SenadisExpirationDate);
        Assert.Equal(expiry, result.SenadisExpirationDate);
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

    // B1 regression, WP-36-updated expectation: a blank/whitespace MRN must never reach the
    // INSERT as '' (a value under the unique key). Under WP-36 the INSERT always carries the
    // freshly minted NC{yy}-#### — no '', no NULL window, no TEMP- stamp, no blank warning.
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CreateAsync_BlankMrn_InsertsMintedMrn_NeverEmptyString(string blankMrn)
    {
        var h = BuildCreateHarness(maxMrnSequence: 0);

        // Capture the MRN VALUE at insert time — asserting on the mutable instance later
        // would miss what the INSERT actually carried.
        bool insertSeen = false;
        string? mrnAtInsert = null;
        h.PatientRepo.Setup(r => r.AddAsync(It.IsAny<Patient>()))
            .Callback<Patient>(p => { insertSeen = true; mrnAtInsert = p.MedicalRecordNumber; })
            .ReturnsAsync((Patient p) => { p.Id = 5; return p; });

        var result = await h.Svc.CreateAsync(NewCreateRequest(mrn: blankMrn));

        Assert.True(insertSeen);
        Assert.Equal($"{ExpectedNcPrefix()}0001", mrnAtInsert);
        Assert.Equal(mrnAtInsert, result.MedicalRecordNumber);
        // Blank is not a "supplied" MRN — no ignored-MRN warning noise on normal creates.
        VerifyWarningLogged(h.Logger, Times.Never());
    }
}