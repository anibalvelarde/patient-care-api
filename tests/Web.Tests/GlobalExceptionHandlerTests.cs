using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using Neurocorp.Api.Core.Exceptions;
using Neurocorp.Api.Web.Middleware;

namespace Web.Tests.Middleware;

public class GlobalExceptionHandlerTests
{
    public static IEnumerable<object[]> ExceptionCases() => new[]
    {
        new object[] { new ArgumentException("bad input"), StatusCodes.Status400BadRequest, "Bad Request" },
        new object[] { new ArgumentNullException("param"), StatusCodes.Status400BadRequest, "Bad Request" },
        new object[] { new NotFoundException("Session", 5), StatusCodes.Status404NotFound, "Not Found" },
        // WP-22: domain-state conflicts (merge blockers) map to 409 with the message intact.
        new object[] { new ConflictException("merge blocked"), StatusCodes.Status409Conflict, "Conflict" },
        new object[] { new InvalidOperationException("boom"), StatusCodes.Status500InternalServerError, "An unexpected error occurred." },
    };

    [Theory]
    [MemberData(nameof(ExceptionCases))]
    public async Task TryHandleAsync_maps_exception_to_problemdetails(Exception exception, int expectedStatus, string expectedTitle)
    {
        // Arrange — capture the ProblemDetailsContext the handler writes.
        ProblemDetailsContext? captured = null;
        var problemDetailsService = new Mock<IProblemDetailsService>();
        problemDetailsService
            .Setup(s => s.TryWriteAsync(It.IsAny<ProblemDetailsContext>()))
            .Callback<ProblemDetailsContext>(c => captured = c)
            .Returns(new ValueTask<bool>(true));

        var handler = new GlobalExceptionHandler(problemDetailsService.Object, Mock.Of<ILogger<GlobalExceptionHandler>>());
        var httpContext = new DefaultHttpContext();

        // Act
        var handled = await handler.TryHandleAsync(httpContext, exception, CancellationToken.None);

        // Assert
        handled.Should().BeTrue();
        httpContext.Response.StatusCode.Should().Be(expectedStatus);
        captured.Should().NotBeNull();
        captured!.ProblemDetails.Status.Should().Be(expectedStatus);
        captured.ProblemDetails.Title.Should().Be(expectedTitle);

        if (expectedStatus == StatusCodes.Status500InternalServerError)
        {
            // Internal details must not leak to the client on an unexpected error.
            captured.ProblemDetails.Detail.Should().BeNull();
        }
        else
        {
            captured.ProblemDetails.Detail.Should().Be(exception.Message);
        }
    }

    // B1 (intake 2026-07-07-001): the Patient MRN unique key is named 'MedicalRecordNumber'
    // (not uq_patient_mrn), so duplicate MRNs fell through to the unhelpful generic 409 text.
    [Theory]
    [InlineData("Duplicate entry 'MRN-001' for key 'Patient.MedicalRecordNumber'",
        "A patient with this Medical Record Number already exists.")]
    [InlineData("Duplicate entry 'a@b.com' for key 'SystemUser.uq_systemuser_email'",
        "A user with this email address already exists.")]
    [InlineData("Duplicate entry '8-123-456' for key 'Patient.uq_patient_cedula'",
        "A patient with this Cedula already exists.")]
    // WP-50: attaching a duplicate caretaker/patient identity to a SystemUser (self-caretaker race).
    [InlineData("Duplicate entry '10117' for key 'Caretaker.UserID_UNIQUE'",
        "This person already has a caretaker record.")]
    [InlineData("Duplicate entry '10117' for key 'Patient.UserID_UNIQUE'",
        "This person already has a patient record.")]
    [InlineData("Duplicate entry 'x' for key 'SomeTable.some_other_key'",
        "A record with this value already exists.")]
    public void DuplicateKeyMessageFor_maps_unique_key_to_friendly_message(string mysqlMessage, string expected)
    {
        GlobalExceptionHandler.DuplicateKeyMessageFor(mysqlMessage).Should().Be(expected);
    }
}
