using Neurocorp.Api.Core.Interfaces.Repositories;
using Moq;
using Neurocorp.Api.Core.Services;
using Neurocorp.Api.Core.BusinessObjects.Lookups;
using Neurocorp.Api.Core.BusinessObjects.Payments;
using Neurocorp.Api.Core.Entities;
using Microsoft.Extensions.Logging;

namespace Core.Tests;

public class PaymentTypeServiceTests
{
    [Fact]
    public async Task GetAllAsync_ReturnsAllTypes()
    {
        // Arrange
        var fakeLogger = Mock.Of<ILogger<PaymentTypeService>>();
        var types = new List<PaymentType>
        {
            new() { Id = 1, PmtTypeAbbreviation = "CSH", PmtTypeName = "Cash" },
            new() { Id = 2, PmtTypeAbbreviation = "CHK", PmtTypeName = "Check" }
        };
        var mockRepo = new Mock<IRepository<PaymentType>>(MockBehavior.Strict);
        mockRepo.Setup(repo => repo.GetAllAsync()).ReturnsAsync(types);
        var svc = new PaymentTypeService(fakeLogger, mockRepo.Object);

        // Act
        var result = await svc.GetAllAsync();

        // Assert
        Assert.NotNull(result);
        Assert.IsAssignableFrom<IEnumerable<PaymentTypeInfo>>(result);
        Assert.Equal(2, result.Count());
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsType_WhenExists()
    {
        // Arrange
        var fakeLogger = Mock.Of<ILogger<PaymentTypeService>>();
        var expected = new PaymentType { Id = 1, PmtTypeAbbreviation = "CSH", PmtTypeName = "Cash" };
        var mockRepo = new Mock<IRepository<PaymentType>>(MockBehavior.Strict);
        mockRepo.Setup(repo => repo.GetByIdAsync(1)).ReturnsAsync(expected);
        var svc = new PaymentTypeService(fakeLogger, mockRepo.Object);

        // Act
        var result = await svc.GetByIdAsync(1);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(1, result.PaymentTypeId);
        Assert.Equal("CSH", result.Abbreviation);
        Assert.Equal("Cash", result.Name);
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsNull_WhenNotExists()
    {
        // Arrange
        var fakeLogger = Mock.Of<ILogger<PaymentTypeService>>();
        var mockRepo = new Mock<IRepository<PaymentType>>(MockBehavior.Strict);
        mockRepo.Setup(repo => repo.GetByIdAsync(99)).ReturnsAsync((PaymentType?)null);
        var svc = new PaymentTypeService(fakeLogger, mockRepo.Object);

        // Act
        var result = await svc.GetByIdAsync(99);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task CreateAsync_ReturnsCreatedType()
    {
        // Arrange
        var fakeLogger = Mock.Of<ILogger<PaymentTypeService>>();
        var request = new PaymentTypeCreateRequest { Abbreviation = "ZEL", Name = "Zelle" };
        var created = new PaymentType { Id = 10, PmtTypeAbbreviation = "ZEL", PmtTypeName = "Zelle" };
        var mockRepo = new Mock<IRepository<PaymentType>>(MockBehavior.Strict);
        mockRepo.Setup(repo => repo.AddAsync(It.IsAny<PaymentType>())).ReturnsAsync(created);
        var svc = new PaymentTypeService(fakeLogger, mockRepo.Object);

        // Act
        var result = await svc.CreateAsync(request);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(10, result.PaymentTypeId);
        Assert.Equal("ZEL", result.Abbreviation);
    }

    [Fact]
    public async Task UpdateAsync_ReturnsTrue_WhenExists()
    {
        // Arrange
        var fakeLogger = Mock.Of<ILogger<PaymentTypeService>>();
        var existing = new PaymentType { Id = 1, PmtTypeAbbreviation = "CSH", PmtTypeName = "Cash" };
        var request = new PaymentTypeUpdateRequest { Name = "Cash Payment" };
        var mockRepo = new Mock<IRepository<PaymentType>>(MockBehavior.Strict);
        mockRepo.Setup(repo => repo.GetByIdAsync(1)).ReturnsAsync(existing);
        mockRepo.Setup(repo => repo.UpdateAsync(It.IsAny<PaymentType>())).Returns(Task.CompletedTask);
        var svc = new PaymentTypeService(fakeLogger, mockRepo.Object);

        // Act
        var result = await svc.UpdateAsync(1, request);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task UpdateAsync_ReturnsFalse_WhenNotExists()
    {
        // Arrange
        var fakeLogger = Mock.Of<ILogger<PaymentTypeService>>();
        var request = new PaymentTypeUpdateRequest { Name = "Updated" };
        var mockRepo = new Mock<IRepository<PaymentType>>(MockBehavior.Strict);
        mockRepo.Setup(repo => repo.GetByIdAsync(99)).ReturnsAsync((PaymentType?)null);
        var svc = new PaymentTypeService(fakeLogger, mockRepo.Object);

        // Act
        var result = await svc.UpdateAsync(99, request);

        // Assert
        Assert.False(result);
    }
}
