using Neurocorp.Api.Core.Interfaces;
using Neurocorp.Api.Core.Interfaces.Repositories;
using Moq;
using Neurocorp.Api.Core.Services;
using Neurocorp.Api.Core.BusinessObjects.Patients;
using Neurocorp.Api.Core.Entities;
using Neurocorp.Api.Core.Exceptions;
using Microsoft.Extensions.Logging;

namespace Core.Tests;

public class CaretakerProfileServiceTests
{
    [Fact]
    public void GoodConstructorTest()
    {
        // arrange
        var fakeRepo = Mock.Of<ICaretakerProfileRepository>();
        var fakeCaretakerRepo = Mock.Of<ICaretakerRepository>();
        var fakeUserRepo = Mock.Of<IUserRepository>();
        var fakeUserRoleRepo = Mock.Of<IUserRoleRepository>();
        var fakeLogger = Mock.Of<ILogger<CaretakerProfileService>>(); 

        // act
        var fakePatientCaretakerRepo = Mock.Of<IPatientCaretakerRepository>();
        var svc = new CaretakerProfileService(fakeLogger, fakeRepo, fakeCaretakerRepo, fakeUserRepo, fakeUserRoleRepo, fakePatientCaretakerRepo);

        // assert
        Assert.IsType<CaretakerProfileService>(svc);
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsCaretakerProfile_WhenCaretakerExists()
    {
        // Arrange
        var fakeCaretakerRepo = Mock.Of<ICaretakerRepository>();
        var fakeUserRepo = Mock.Of<IUserRepository>();      
        var fakeUserRoleRepo = Mock.Of<IUserRoleRepository>();
        var fakeLogger = Mock.Of<ILogger<CaretakerProfileService>>(); 
        int testId = 1;
        var expectedCaretaker = new CaretakerProfile { CaretakerId = testId, CaretakerName = "John Doe" };
        var _mockRepository = new Mock<ICaretakerProfileRepository>(MockBehavior.Strict);
        _mockRepository.Setup(repo => repo.GetByIdAsync(testId)).ReturnsAsync(expectedCaretaker);
        var fakePatientCaretakerRepo = Mock.Of<IPatientCaretakerRepository>();
        var svc = new CaretakerProfileService(fakeLogger, _mockRepository.Object, fakeCaretakerRepo, fakeUserRepo, fakeUserRoleRepo, fakePatientCaretakerRepo);

        // Act
        var result = await svc.GetByIdAsync(testId);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(expectedCaretaker.CaretakerId, result.CaretakerId);
        Assert.Equal(expectedCaretaker.CaretakerName, result.CaretakerName);
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsNull_WhenCaretakerDoesNotExist()
    {
        // Arrange
        var fakeCaretakerRepo = Mock.Of<ICaretakerRepository>();
        var fakeUserRepo = Mock.Of<IUserRepository>();        
        var fakeUserRoleRepo = Mock.Of<IUserRoleRepository>();
        var fakeLogger = Mock.Of<ILogger<CaretakerProfileService>>(); 
        int testId = 99;
        var expectedCaretaker = new CaretakerProfile { CaretakerId = testId, CaretakerName = "John Doe" };
        var _mockRepository = new Mock<ICaretakerProfileRepository>(MockBehavior.Strict);
        _mockRepository.Setup(repo => repo.GetByIdAsync(testId)).ReturnsAsync((CaretakerProfile?)null);
        var fakePatientCaretakerRepo = Mock.Of<IPatientCaretakerRepository>();
        var svc = new CaretakerProfileService(fakeLogger, _mockRepository.Object, fakeCaretakerRepo, fakeUserRepo, fakeUserRoleRepo, fakePatientCaretakerRepo);

        // Act
        var result = await svc.GetByIdAsync(testId);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task CreateProfileInstanceAsync_Throws()
    {
        // Arrange
        var fakeCaretakerRepo = Mock.Of<ICaretakerRepository>();
        var fakeUserRepo = Mock.Of<IUserRepository>();
        var fakeRepo = Mock.Of<ICaretakerProfileRepository>();
        var fakeUserRoleRepo = Mock.Of<IUserRoleRepository>();
        var fakeLogger = Mock.Of<ILogger<CaretakerProfileService>>(); 

        var fakePatientCaretakerRepo = Mock.Of<IPatientCaretakerRepository>();
        var svc = new CaretakerProfileService(fakeLogger, fakeRepo, fakeCaretakerRepo, fakeUserRepo, fakeUserRoleRepo, fakePatientCaretakerRepo);

        // Act & Assert
        await Assert.ThrowsAsync<NotImplementedException>(() => 
            svc.CreateAsync(new CaretakerProfile()));
    }

    [Fact]
    public async Task GetListOfProfilesAsync_Throws()
    {
        // Arrange
        var fakeCaretakerRepo = Mock.Of<ICaretakerRepository>();
        var fakeUserRepo = Mock.Of<IUserRepository>();
        var fakeUserRoleRepo = Mock.Of<IUserRoleRepository>();
        var fakeLogger = Mock.Of<ILogger<CaretakerProfileService>>(); 

        var _mockRepository = new Mock<ICaretakerProfileRepository>(MockBehavior.Strict);
        _mockRepository.Setup(repo => repo.GetAllAsync())
            .ReturnsAsync([]);
        var fakePatientCaretakerRepo = Mock.Of<IPatientCaretakerRepository>();
        var svc = new CaretakerProfileService(fakeLogger, _mockRepository.Object, fakeCaretakerRepo, fakeUserRepo, fakeUserRoleRepo, fakePatientCaretakerRepo);

        // Act
        var result = await svc.GetAllAsync();

        // Assert
        Assert.NotNull(result);
        Assert.IsAssignableFrom<IEnumerable<CaretakerProfile>>(result);
    }

    // ---- WP-50B: MakeSelfCaretakerAsync ------------------------------------------------------

    /// <summary>Unit of work that just runs the operation — commit/rollback is the real impl's concern.</summary>
    private static IUnitOfWork PassThroughUow()
    {
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.ExecuteAsync(It.IsAny<Func<Task<CaretakerProfile>>>()))
           .Returns((Func<Task<CaretakerProfile>> op) => op());
        return uow.Object;
    }

    private static Patient PatientWithUser(int patientId, int userId) => new()
    {
        Id = patientId,
        User = new User { Id = userId, FirstName = "Susana", LastName = "Saiz", Email = null, PhoneNumber = "555" }
    };

    [Fact]
    public async Task MakeSelfCaretakerAsync_AttachesRoleAndSelfLinks_NoNewUser()
    {
        // Arrange — a brand-new adult patient (user 12500) with no existing caretaker links.
        var fakeLogger = Mock.Of<ILogger<CaretakerProfileService>>();
        var fakeRepo = Mock.Of<ICaretakerProfileRepository>();
        var fakeUserRepo = Mock.Of<IUserRepository>();

        var mockCaretakerRepo = new Mock<ICaretakerRepository>();
        mockCaretakerRepo.Setup(r => r.AddAsync(It.IsAny<Caretaker>()))
            .ReturnsAsync((Caretaker c) => { c.Id = 777; return c; }); // preserves c.User

        var mockUserRoleRepo = new Mock<IUserRoleRepository>();
        mockUserRoleRepo.Setup(r => r.AddAsync(It.IsAny<UserRole>()))
            .ReturnsAsync((UserRole ur) => { ur.UserRoleId = 999; return ur; });

        PatientCaretaker? captured = null;
        var mockPcRepo = new Mock<IPatientCaretakerRepository>();
        mockPcRepo.Setup(r => r.GetByPatientIdAsync(42)).ReturnsAsync(new List<PatientCaretaker>());
        mockPcRepo.Setup(r => r.AddAsync(It.IsAny<PatientCaretaker>()))
            .Callback((PatientCaretaker pc) => captured = pc)
            .Returns(Task.CompletedTask);

        var mockPatientRepo = new Mock<IPatientRepository>();
        mockPatientRepo.Setup(r => r.GetByIdWithUserAsync(42)).ReturnsAsync(PatientWithUser(42, 12500));

        var svc = new CaretakerProfileService(fakeLogger, fakeRepo, mockCaretakerRepo.Object, fakeUserRepo,
            mockUserRoleRepo.Object, mockPcRepo.Object, patientRepo: mockPatientRepo.Object, unitOfWork: PassThroughUow());

        // Act
        var result = await svc.MakeSelfCaretakerAsync(42, isPrimary: true);

        // Assert — one caretaker minted on the SAME user, a Caretaker role (RoleId 4), one Self link.
        mockCaretakerRepo.Verify(r => r.AddAsync(It.Is<Caretaker>(c => c.User!.Id == 12500)), Times.Once);
        mockUserRoleRepo.Verify(r => r.AddAsync(It.Is<UserRole>(ur => ur.RoleId == 4 && ur.UserId == 12500)), Times.Once);
        Assert.NotNull(captured);
        Assert.Equal(42, captured!.PatientId);
        Assert.Equal(777, captured.CaretakerId);
        Assert.True(captured.PrimaryCaretaker);
        Assert.Equal("Self", captured.RelationshipToPatient);
        Assert.Equal(777, result.CaretakerId);
        Assert.Equal(12500, result.UserId);
    }

    [Fact]
    public async Task MakeSelfCaretakerAsync_Throws409_WhenAlreadySelfCaretaker()
    {
        // Arrange — patient already has a caretaker link backed by their OWN user (idempotency).
        var fakeLogger = Mock.Of<ILogger<CaretakerProfileService>>();
        var fakeRepo = Mock.Of<ICaretakerProfileRepository>();
        var fakeUserRepo = Mock.Of<IUserRepository>();
        var mockCaretakerRepo = new Mock<ICaretakerRepository>();
        var mockUserRoleRepo = new Mock<IUserRoleRepository>();

        var selfLink = new PatientCaretaker
        {
            PatientId = 42,
            CaretakerId = 777,
            Caretaker = new Caretaker { Id = 777, User = new User { Id = 12500 } }
        };
        var mockPcRepo = new Mock<IPatientCaretakerRepository>();
        mockPcRepo.Setup(r => r.GetByPatientIdAsync(42)).ReturnsAsync(new List<PatientCaretaker> { selfLink });

        var mockPatientRepo = new Mock<IPatientRepository>();
        mockPatientRepo.Setup(r => r.GetByIdWithUserAsync(42)).ReturnsAsync(PatientWithUser(42, 12500));

        var svc = new CaretakerProfileService(fakeLogger, fakeRepo, mockCaretakerRepo.Object, fakeUserRepo,
            mockUserRoleRepo.Object, mockPcRepo.Object, patientRepo: mockPatientRepo.Object, unitOfWork: PassThroughUow());

        // Act & Assert
        await Assert.ThrowsAsync<ConflictException>(() => svc.MakeSelfCaretakerAsync(42, true));
        mockCaretakerRepo.Verify(r => r.AddAsync(It.IsAny<Caretaker>()), Times.Never);
        mockPcRepo.Verify(r => r.AddAsync(It.IsAny<PatientCaretaker>()), Times.Never);
    }

    [Fact]
    public async Task UnlinkPatientAsync_Throws409_ForSelfLink_AndDoesNotDelete()
    {
        // Arrange — the existing link is a "Self" relationship.
        var selfLink = new PatientCaretaker { PatientId = 42, CaretakerId = 777, RelationshipToPatient = "Self" };
        var mockPcRepo = new Mock<IPatientCaretakerRepository>();
        mockPcRepo.Setup(r => r.GetByCompositeKeyAsync(42, 777)).ReturnsAsync(selfLink);

        var svc = new CaretakerProfileService(Mock.Of<ILogger<CaretakerProfileService>>(),
            Mock.Of<ICaretakerProfileRepository>(), Mock.Of<ICaretakerRepository>(), Mock.Of<IUserRepository>(),
            Mock.Of<IUserRoleRepository>(), mockPcRepo.Object);

        // Act & Assert — blocked, and nothing deleted.
        await Assert.ThrowsAsync<ConflictException>(() => svc.UnlinkPatientAsync(777, 42));
        mockPcRepo.Verify(r => r.DeleteAsync(It.IsAny<PatientCaretaker>()), Times.Never);
    }

    [Fact]
    public async Task UnlinkPatientAsync_Deletes_ForNonSelfLink()
    {
        // Arrange — an ordinary (e.g. Mother) link is still removable.
        var link = new PatientCaretaker { PatientId = 42, CaretakerId = 777, RelationshipToPatient = "Mother" };
        var mockPcRepo = new Mock<IPatientCaretakerRepository>();
        mockPcRepo.Setup(r => r.GetByCompositeKeyAsync(42, 777)).ReturnsAsync(link);
        mockPcRepo.Setup(r => r.DeleteAsync(It.IsAny<PatientCaretaker>())).Returns(Task.CompletedTask);

        var svc = new CaretakerProfileService(Mock.Of<ILogger<CaretakerProfileService>>(),
            Mock.Of<ICaretakerProfileRepository>(), Mock.Of<ICaretakerRepository>(), Mock.Of<IUserRepository>(),
            Mock.Of<IUserRoleRepository>(), mockPcRepo.Object);

        // Act
        var result = await svc.UnlinkPatientAsync(777, 42);

        // Assert
        Assert.True(result);
        mockPcRepo.Verify(r => r.DeleteAsync(link), Times.Once);
    }

    [Fact]
    public async Task MakeSelfCaretakerAsync_Throws404_WhenPatientMissing()
    {
        // Arrange
        var fakeLogger = Mock.Of<ILogger<CaretakerProfileService>>();
        var fakeRepo = Mock.Of<ICaretakerProfileRepository>();
        var mockPatientRepo = new Mock<IPatientRepository>();
        mockPatientRepo.Setup(r => r.GetByIdWithUserAsync(999)).ReturnsAsync((Patient?)null);

        var svc = new CaretakerProfileService(fakeLogger, fakeRepo, Mock.Of<ICaretakerRepository>(),
            Mock.Of<IUserRepository>(), Mock.Of<IUserRoleRepository>(), Mock.Of<IPatientCaretakerRepository>(),
            patientRepo: mockPatientRepo.Object, unitOfWork: PassThroughUow());

        // Act & Assert
        await Assert.ThrowsAsync<NotFoundException>(() => svc.MakeSelfCaretakerAsync(999, true));
    }
}