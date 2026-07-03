using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi.Models;
using Neurocorp.Api.Web.Tests.Authorization;
using Swashbuckle.AspNetCore.Swagger;

namespace Neurocorp.Api.Web.Tests.Swagger;

/// <summary>
/// Swagger JWT Bearer auth (intake 2026-06-13-001): the generated OpenAPI document must declare
/// the Bearer security scheme, and the per-operation requirement must mirror reality — stamped on
/// every secured operation (the fallback policy default) and absent from [AllowAnonymous] ones
/// (login/refresh), so the UI lock icons and Authorize button work as expected.
/// Asserts against the doc produced by the REAL host (via <see cref="AccessControlTestFactory"/>),
/// not a hand-built filter context.
/// </summary>
public class SwaggerBearerSecurityTests : IClassFixture<AccessControlTestFactory>
{
    private readonly OpenApiDocument _doc;

    public SwaggerBearerSecurityTests(AccessControlTestFactory factory)
    {
        var provider = factory.Services.GetRequiredService<ISwaggerProvider>();
        _doc = provider.GetSwagger("v1");
    }

    [Fact]
    public void Document_declares_the_bearer_security_scheme()
    {
        _doc.Components.SecuritySchemes.Should().ContainKey("Bearer");
        var scheme = _doc.Components.SecuritySchemes["Bearer"];
        scheme.Type.Should().Be(SecuritySchemeType.Http);
        scheme.Scheme.Should().Be("bearer");
        scheme.BearerFormat.Should().Be("JWT");
    }

    [Fact]
    public void Anonymous_login_operation_has_no_security_requirement()
    {
        var login = _doc.Paths["/api/Auth/login"].Operations[OperationType.Post];
        login.Security.Should().BeEmpty("[AllowAnonymous] endpoints must not demand a token");
    }

    [Fact]
    public void Secured_operations_carry_the_bearer_requirement()
    {
        var patients = _doc.Paths["/api/Patients"].Operations[OperationType.Get];
        patients.Security.Should().ContainSingle()
            .Which.Keys.Should().ContainSingle(s => s.Reference.Id == "Bearer");
    }

    [Fact]
    public void Every_operation_is_either_bearer_secured_or_deliberately_anonymous()
    {
        // The fallback policy secures everything that doesn't opt out, so each operation must
        // either carry the Bearer requirement or be a known-anonymous surface. Catches a future
        // endpoint slipping through the operation filter unlabeled.
        foreach (var (path, item) in _doc.Paths)
        {
            foreach (var (type, operation) in item.Operations)
            {
                var hasBearer = operation.Security
                    .Any(req => req.Keys.Any(s => s.Reference?.Id == "Bearer"));
                // HealthController is class-level [AllowAnonymous] (K8s probes).
                var isKnownAnonymous = path is "/api/Auth/login" or "/api/Auth/refresh"
                    || path.StartsWith("/api/Health/");
                (hasBearer || isKnownAnonymous).Should().BeTrue(
                    $"operation {type} {path} is neither Bearer-secured nor a known anonymous surface");
            }
        }
    }
}
