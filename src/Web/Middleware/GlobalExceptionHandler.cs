using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;
using Neurocorp.Api.Core.Exceptions;

namespace Neurocorp.Api.Web.Middleware;

/// <summary>
/// Central exception → RFC 7807 ProblemDetails translation for the whole API. Registered via
/// <c>AddExceptionHandler</c> + <c>UseExceptionHandler</c>. Known domain/validation/persistence
/// exceptions become specific 4xx responses; anything unmapped becomes a generic 500 (no internal
/// details leak to the client — the full exception is logged instead). 401/403 are produced
/// earlier by the auth layer (AuthProblemDetails / PasswordChangeRequiredMiddleware) and never
/// reach here. The shared ProblemDetails shape (type/instance/traceId) is applied centrally by
/// CustomizeProblemDetails in Startup.
///
/// This handler is the single source of truth for exception logging: it logs handled 4xx at
/// Information and only genuine 5xx at Error (below). The built-in <c>ExceptionHandlerMiddleware</c>
/// also logs every caught exception at Error ("An unhandled exception has occurred…") — redundant
/// and misleading for the 4xx we translate cleanly — so that one framework category is set to
/// "None" in appsettings. Real failures still surface via the LogError call here.
/// </summary>
public sealed class GlobalExceptionHandler : IExceptionHandler
{
    private const int MySqlDuplicateKey = 1062;

    private readonly IProblemDetailsService _problemDetailsService;
    private readonly ILogger<GlobalExceptionHandler> _logger;

    public GlobalExceptionHandler(IProblemDetailsService problemDetailsService, ILogger<GlobalExceptionHandler> logger)
    {
        _problemDetailsService = problemDetailsService;
        _logger = logger;
    }

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var (status, title, detail) = Map(exception);

        if (status >= StatusCodes.Status500InternalServerError)
        {
            _logger.LogError(exception, "Unhandled exception ({Type}) on {Method} {Path}",
                exception.GetType().Name, httpContext.Request.Method, httpContext.Request.Path);
        }
        else
        {
            _logger.LogInformation("Handled {Type} on {Method} {Path}: {Message}",
                exception.GetType().Name, httpContext.Request.Method, httpContext.Request.Path, exception.Message);
        }

        httpContext.Response.StatusCode = status;

        return await _problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = new ProblemDetails { Status = status, Title = title, Detail = detail },
        });
    }

    private static (int status, string title, string? detail) Map(Exception exception) => exception switch
    {
        NotFoundException => (StatusCodes.Status404NotFound, "Not Found", exception.Message),

        // Domain-state conflicts (e.g. WP-22 merge blockers) — the domain-driven sibling of
        // the duplicate-key 409 below.
        ConflictException => (StatusCodes.Status409Conflict, "Conflict", exception.Message),

        // Covers ArgumentNullException / ArgumentOutOfRangeException too (both derive from ArgumentException).
        ArgumentException => (StatusCodes.Status400BadRequest, "Bad Request", exception.Message),

        DbUpdateException dbEx when IsDuplicateKey(dbEx) =>
            (StatusCodes.Status409Conflict, "Conflict", DuplicateKeyMessage(dbEx)),

        // Unmapped: do not leak internals — generic message; full exception is logged above.
        _ => (StatusCodes.Status500InternalServerError, "An unexpected error occurred.", null),
    };

    private static bool IsDuplicateKey(DbUpdateException dbEx) =>
        dbEx.InnerException is MySqlException { Number: MySqlDuplicateKey };

    private static string DuplicateKeyMessage(DbUpdateException dbEx) =>
        DuplicateKeyMessageFor(dbEx.InnerException?.Message ?? string.Empty);

    /// <summary>Maps a MySQL 1062 message to a client-friendly conflict message by unique-key name.</summary>
    public static string DuplicateKeyMessageFor(string message)
    {
        if (message.Contains("uq_systemuser_email", StringComparison.OrdinalIgnoreCase))
        {
            return "A user with this email address already exists.";
        }
        if (message.Contains("uq_patient_cedula", StringComparison.OrdinalIgnoreCase))
        {
            return "A patient with this Cedula already exists.";
        }
        // The Patient MRN unique key is named after the column itself, not uq_patient_mrn (B1).
        if (message.Contains("MedicalRecordNumber", StringComparison.OrdinalIgnoreCase))
        {
            return "A patient with this Medical Record Number already exists.";
        }
        // WP-50: attaching a second identity to a SystemUser that already has it (e.g. two
        // concurrent "make self-caretaker" calls racing past the pre-check). The UNIQUE index on
        // the identity's UserID is the concurrency backstop; give the loser a clear message.
        if (message.Contains("Caretaker.UserID_UNIQUE", StringComparison.OrdinalIgnoreCase))
        {
            return "This person already has a caretaker record.";
        }
        if (message.Contains("Patient.UserID_UNIQUE", StringComparison.OrdinalIgnoreCase))
        {
            return "This person already has a patient record.";
        }
        return "A record with this value already exists.";
    }
}
