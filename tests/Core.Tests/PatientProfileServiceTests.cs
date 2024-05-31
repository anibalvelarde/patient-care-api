using Neurocorp.Api.Core.Interfaces.Repositories;
using Moq;
using Neurocorp.Api.Core.Services;
using Neurocorp.Api.Core.BusinessObjects.Patients;
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
        var svc = new PatientProfileService(fakeLogger, fakeRepo, fakePatientRepo, fakeUserRepo, fakeUserRoleRepo);

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
        var svc = new PatientProfileService(fakeLogger, _mockRepository.Object, fakePatientRepo, fakeUserRepo, fakeUserRoleRepo);

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
        var svc = new PatientProfileService(fakeLogger, _mockRepository.Object, fakePatientRepo, fakeUserRepo, fakeUserRoleRepo);

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

        var svc = new PatientProfileService(fakeLogger, fakeRepo, fakePatientRepo, fakeUserRepo, fakeUserRoleRepo);

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
        var svc = new PatientProfileService(fakeLogger, _mockRepository.Object, fakePatientRepo, fakeUserRepo, fakeUserRoleRepo);

        // Act
        var result = await svc.GetAllAsync();

        // Assert
        Assert.NotNull(result);
        Assert.IsAssignableFrom<IEnumerable<PatientProfile>>(result);
    }
}