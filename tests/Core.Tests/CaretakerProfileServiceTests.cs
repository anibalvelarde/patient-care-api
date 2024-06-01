using Neurocorp.Api.Core.Interfaces.Repositories;
using Moq;
using Neurocorp.Api.Core.Services;
using Neurocorp.Api.Core.BusinessObjects.Patients;
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
        var svc = new CaretakerProfileService(fakeLogger, fakeRepo, fakeCaretakerRepo, fakeUserRepo, fakeUserRoleRepo);

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
        var svc = new CaretakerProfileService(fakeLogger, _mockRepository.Object, fakeCaretakerRepo, fakeUserRepo, fakeUserRoleRepo);

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
        var svc = new CaretakerProfileService(fakeLogger, _mockRepository.Object, fakeCaretakerRepo, fakeUserRepo, fakeUserRoleRepo);

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

        var svc = new CaretakerProfileService(fakeLogger, fakeRepo, fakeCaretakerRepo, fakeUserRepo, fakeUserRoleRepo);

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
        var svc = new CaretakerProfileService(fakeLogger, _mockRepository.Object, fakeCaretakerRepo, fakeUserRepo, fakeUserRoleRepo);

        // Act
        var result = await svc.GetAllAsync();

        // Assert
        Assert.NotNull(result);
        Assert.IsAssignableFrom<IEnumerable<CaretakerProfile>>(result);
    }
}