using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Neurocorp.Api.Web.Authentication;

/// <summary>
/// Renders authentication (401) and authorization (403) failures as RFC 7807
/// <c>application/problem+json</c> instead of the bearer middleware's default empty body.
/// </summary>
public static class AuthProblemDetails
{
    public static Task OnChallenge(JwtBearerChallengeContext context)
    {
        // Suppress the default empty 401 response and emit ProblemDetails instead.
        context.HandleResponse();
        return WriteProblem(
            context.Response,
            StatusCodes.Status401Unauthorized,
            "Unauthorized",
            "Authentication is required to access this resource.");
    }

    public static Task OnForbidden(ForbiddenContext context)
    {
        return WriteProblem(
            context.Response,
            StatusCodes.Status403Forbidden,
            "Forbidden",
            "You do not have permission to perform this action.");
    }

    private static async Task WriteProblem(HttpResponse response, int status, string title, string detail)
    {
        if (response.HasStarted)
        {
            return;
        }

        response.StatusCode = status;
        response.ContentType = "application/problem+json";

        var problem = new ProblemDetails
        {
            Status = status,
            Title = title,
            Detail = detail,
            Type = $"https://httpstatuses.io/{status}"
        };

        await response.WriteAsync(JsonSerializer.Serialize(problem));
    }
}
