using System.Linq;
using Microsoft.AspNetCore.Authorization;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Neurocorp.Api.Web.Swagger;

/// <summary>
/// Stamps the Bearer security requirement on every operation that does NOT opt out via
/// <c>[AllowAnonymous]</c> (login, refresh, health checks). Because the app runs a fallback
/// policy requiring an authenticated user (see Startup), "not anonymous" == "secured", so the
/// Swagger UI lock icon reflects reality instead of decorating everything (or nothing).
/// </summary>
public class BearerSecurityOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var isAnonymous = context.ApiDescription.ActionDescriptor.EndpointMetadata
            .OfType<IAllowAnonymous>()
            .Any();
        if (isAnonymous) return;

        operation.Security.Add(new OpenApiSecurityRequirement
        {
            [new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            }] = System.Array.Empty<string>()
        });
    }
}
