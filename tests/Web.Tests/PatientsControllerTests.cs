using Moq;
using Xunit;
using Microsoft.AspNetCore.Mvc;
using Neurocorp.Api.Core.BusinessObjects;
using Neurocorp.Api.Core.Interfaces;
using Neurocorp.Api.Web.Controllers;
using System.Collections.Generic;
using System.Threading.Tasks;

public class PatientsControllerTests
{
    private readonly Mock<IPatientProfileService> _mockService;
    private readonly PatientsController _controller;

    public PatientsControllerTests()
    {
        _mockService = new Mock<IPatientProfileService>();
        _controller = new PatientsController(_mockService.Object);
    }

    [Fact]
    public async Task GetAllPatients_ReturnsOkResult_WithPatients()
    {
        // Arrange
        var mockPatients = new List<PatientProfile>
        {
            new PatientProfile { /* properties */ },
            new PatientProfile { /* properties */ }
        };
        _mockService.Setup(service => service.GetAllAsync()).ReturnsAsync(mockPatients);

        // Act
        var result = await _controller.GetAllPatients();

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var returnedPatients = Assert.IsType<List<PatientProfile>>(okResult.Value);
        Assert.Equal(mockPatients.Count, returnedPatients.Count);
    }

    [Fact]
    public async Task GetPatient_ReturnsNotFound_WhenPatientDoesNotExist()
    {
        // Arrange
        PatientProfile? nullPatient = null;
        _mockService.Setup(service =>
            service.GetByIdAsync(It.IsAny<int>()))
        .ReturnsAsync(value: nullPatient);

        // Act
        var result = await _controller.GetPatient(1);

        // Assert
        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task GetPatient_ReturnsOkResult_WithPatient()
    {
        // Arrange
        var mockPatient = new PatientProfile { /* properties */ };
        _mockService.Setup(service => service.GetByIdAsync(1)).ReturnsAsync(mockPatient);

        // Act
        var result = await _controller.GetPatient(1);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var returnedPatient = Assert.IsType<PatientProfile>(okResult.Value);
        Assert.NotNull(returnedPatient);
    }
}
