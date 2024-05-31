using Neurocorp.Api.Core.Interfaces.Repositories;
using Moq;
using Neurocorp.Api.Core.Services;
using Neurocorp.Api.Core.BusinessObjects.Therapists;
using Microsoft.Extensions.Logging;

namespace Core.Tests;

public class TherapistProfileServiceTests
{
    [Fact]
    public void GoodConstructorTest()
    {
        // arrange
        var fakeRepo = Mock.Of<ITherapistProfileRepository>();
        var fakeTherapistRepo = Mock.Of<ITherapistRepository>();
        var fakeUserRepo = Mock.Of<IUserRepository>();
        var fakeUserRoleRepo = Mock.Of<IUserRoleRepository>();
        var fakeLogger = Mock.Of<ILogger<TherapistProfileService>>(); 

        // act
        var svc = new TherapistProfileService(fakeLogger, fakeRepo, fakeTherapistRepo, fakeUserRepo, fakeUserRoleRepo);

        // assert
        Assert.IsType<TherapistProfileService>(svc);
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsTherapistProfile_WhenTherapistExists()
    {
        // Arrange
        var fakeTherapistRepo = Mock.Of<ITherapistRepository>();
        var fakeUserRepo = Mock.Of<IUserRepository>();      
        var fakeUserRoleRepo = Mock.Of<IUserRoleRepository>();
        var fakeLogger = Mock.Of<ILogger<TherapistProfileService>>(); 
        int testId = 1;
        var expectedTherapist = new TherapistProfile { TherapistId = testId, TherapistName = "John Doe" };
        var _mockRepository = new Mock<ITherapistProfileRepository>(MockBehavior.Strict);
        _mockRepository.Setup(repo => repo.GetByIdAsync(testId)).ReturnsAsync(expectedTherapist);
        var svc = new TherapistProfileService(fakeLogger, _mockRepository.Object, fakeTherapistRepo, fakeUserRepo, fakeUserRoleRepo);

        // Act
        var result = await svc.GetByIdAsync(testId);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(expectedTherapist.TherapistId, result.TherapistId);
        Assert.Equal(expectedTherapist.TherapistName, result.TherapistName);
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsNull_WhenTherapistDoesNotExist()
    {
        // Arrange
        var fakeTherapistRepo = Mock.Of<ITherapistRepository>();
        var fakeUserRepo = Mock.Of<IUserRepository>();        
        var fakeUserRoleRepo = Mock.Of<IUserRoleRepository>();
        var fakeLogger = Mock.Of<ILogger<TherapistProfileService>>(); 
        int testId = 99;
        var expectedTherapist = new TherapistProfile { TherapistId = testId, TherapistName = "John Doe" };
        var _mockRepository = new Mock<ITherapistProfileRepository>(MockBehavior.Strict);
        _mockRepository.Setup(repo => repo.GetByIdAsync(testId)).ReturnsAsync((TherapistProfile?)null);
        var svc = new TherapistProfileService(fakeLogger, _mockRepository.Object, fakeTherapistRepo, fakeUserRepo, fakeUserRoleRepo);

        // Act
        var result = await svc.GetByIdAsync(testId);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task CreateProfileInstanceAsync_Throws()
    {
        // Arrange
        var fakeTherapistRepo = Mock.Of<ITherapistRepository>();
        var fakeUserRepo = Mock.Of<IUserRepository>();
        var fakeRepo = Mock.Of<ITherapistProfileRepository>();
        var fakeUserRoleRepo = Mock.Of<IUserRoleRepository>();
        var fakeLogger = Mock.Of<ILogger<TherapistProfileService>>(); 

        var svc = new TherapistProfileService(fakeLogger, fakeRepo, fakeTherapistRepo, fakeUserRepo, fakeUserRoleRepo);

        // Act & Assert
        await Assert.ThrowsAsync<NotImplementedException>(() => 
            svc.CreateAsync(new TherapistProfile()));
    }

    [Fact]
    public async Task GetListOfProfilesAsync_Throws()
    {
        // Arrange
        var fakeTherapistRepo = Mock.Of<ITherapistRepository>();
        var fakeUserRepo = Mock.Of<IUserRepository>();
        var fakeUserRoleRepo = Mock.Of<IUserRoleRepository>();
        var fakeLogger = Mock.Of<ILogger<TherapistProfileService>>(); 

        var _mockRepository = new Mock<ITherapistProfileRepository>(MockBehavior.Strict);
        _mockRepository.Setup(repo => repo.GetAllAsync())
            .ReturnsAsync([]);
        var svc = new TherapistProfileService(fakeLogger, _mockRepository.Object, fakeTherapistRepo, fakeUserRepo, fakeUserRoleRepo);

        // Act
        var result = await svc.GetAllAsync();

        // Assert
        Assert.NotNull(result);
        Assert.IsAssignableFrom<IEnumerable<TherapistProfile>>(result);
    }
}