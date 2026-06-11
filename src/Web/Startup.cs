using System;
using System.Diagnostics;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.OpenApi.Models;
using Neurocorp.Api.Core.Configurations;
using Neurocorp.Api.Infrastructure.Configurations;
using Neurocorp.Api.Web.Middleware.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Neurocorp.Api.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;
using Neurocorp.Api.Core.Interfaces.Services;
using Neurocorp.Api.Web.Authentication;
using Neurocorp.Api.Web.Authorization;
using Neurocorp.Api.Web.Middleware;

namespace Neurocorp.Api.Web;

public class Startup(IConfiguration configuration, IWebHostEnvironment environment)
{
    public IConfiguration Configuration { get; } = configuration;
    public IWebHostEnvironment Environment { get; } = environment;

    // This method gets called by the runtime. Use this method to add services to the container.
    public void ConfigureServices(IServiceCollection services)
    {
        // Register Primordial Services
        services.AddSingleton<StartupHealthCheck>();
        services.AddHealthChecks()
            .AddCheck<StartupHealthCheck>(
                "Startup",
                tags: ["ready"]
            );
        services.AddCors(options =>
        {
            options.AddPolicy("AllowedOrigins",
                builder => builder
                    .SetIsOriginAllowed(origin =>
                    {
                        var host = new Uri(origin).Host;
                        return host.Contains("neurocorp") || host.Contains("localhost");
                    })
                    .AllowAnyHeader()
                    .AllowAnyMethod()
                    .AllowCredentials());
        });
        services.AddControllers()
        .AddJsonOptions(options =>
        {
            options.JsonSerializerOptions.Converters.Add(new TimeOnlyJsonConverterFactory());
        });

        // Centralized RFC 7807 error handling (Chunk 3A). AddProblemDetails enables the shared
        // ProblemDetails service; CustomizeProblemDetails stamps a consistent shape (type/instance/
        // traceId) on EVERY ProblemDetails — model-validation 400s, auth Problem() results, and the
        // GlobalExceptionHandler below — matching the 1B auth error shape.
        services.AddProblemDetails(options =>
        {
            options.CustomizeProblemDetails = context =>
            {
                var status = context.ProblemDetails.Status ?? context.HttpContext.Response.StatusCode;
                context.ProblemDetails.Status = status;
                context.ProblemDetails.Type = $"https://httpstatuses.io/{status}";
                context.ProblemDetails.Instance ??= $"{context.HttpContext.Request.Method} {context.HttpContext.Request.Path}";
                context.ProblemDetails.Extensions["traceId"] = Activity.Current?.Id ?? context.HttpContext.TraceIdentifier;
            };
        });
        services.AddExceptionHandler<GlobalExceptionHandler>();

        // Register the Swagger generator, defining one or more Swagger documents
        services.AddSwaggerGen(c =>
        {
            c.SwaggerDoc("v1", new OpenApiInfo { Title = "Neurocorp Web API", Version = "v1" });
        });

        // Register the hosted service
        services.AddHostedService<DbContextWarmupService>();

        // --- Authentication & Authorization (Chunk 1B) ---
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUserService, CurrentUserService>();
        services.AddSingleton<ITokenService, JwtTokenService>();
        services.AddSingleton<IAuthorizationHandler, PermissionAuthorizationHandler>();
        // Dynamic policy provider so [Authorize(Policy = "claim:Type:Value")] works without
        // registering each permission policy explicitly.
        services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();

        var signingKey = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(JwtConfig.ResolveSigningKey(Configuration, Environment)));

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                // Keep custom claim types (e.g. "System", "uid") verbatim instead of the
                // default SOAP-style remapping, so policy handlers match what we issued.
                options.MapInboundClaims = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = JwtConfig.Issuer(Configuration),
                    ValidateAudience = true,
                    ValidAudience = JwtConfig.Audience(Configuration),
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = signingKey,
                    ClockSkew = TimeSpan.FromSeconds(30),
                    NameClaimType = "name",
                    RoleClaimType = "role"
                };
                options.Events = new JwtBearerEvents
                {
                    OnChallenge = AuthProblemDetails.OnChallenge,
                    OnForbidden = AuthProblemDetails.OnForbidden
                };
            });

        // Secure by default: every endpoint requires an authenticated user unless it opts
        // out with [AllowAnonymous] (login, refresh, health checks).
        // Granular [Authorize(Policy = "claim:Permission:<value>")] decorations are governed by
        // the access-control matrix anchored at AccessControlManifest.SemanticHash (WP-17);
        // AccessControlConformanceTests enforces code/manifest agreement.
        services.AddAuthorization(options =>
        {
            options.FallbackPolicy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .Build();
        });

        // Register dependencies with DI framework
        services.AddCoreDependencies(Configuration);
        services.AddInfrastructureServices(Configuration);
    }

    // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
    public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
    {
        // First in the pipeline so it converts ALL exceptions (every environment) into the
        // standard ProblemDetails via GlobalExceptionHandler. Full exception detail goes to logs,
        // not the response — so we intentionally do not use the developer exception page.
        app.UseExceptionHandler();

        if (env.IsDevelopment())
        {
            app.UseSwagger();
            app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "Web API v1"));
        }

        app.UseHttpsRedirection();
        app.UseRouting();
        app.UseCors("AllowedOrigins");

        app.UseAuthentication();
        app.UseAuthorization();

        // Restricts a must-change-password session to the password-change flow until resolved.
        app.UseMiddleware<PasswordChangeRequiredMiddleware>();

        app.UseEndpoints(endpoints =>
        {
            endpoints.MapControllers();
        });

        // K8s Readiness Probes...
        app.UseHealthChecks("/api/health/startup", new HealthCheckOptions {
            Predicate = healthCheck => healthCheck.Tags.Contains("ready")
        });
        app.UseHealthChecks("/api/health/ready", new HealthCheckOptions {
            Predicate = healthCheck => healthCheck.Tags.Contains("ready")
        });
        app.UseHealthChecks("/api/health/live", new HealthCheckOptions {
            Predicate = healthCheck => healthCheck.FailureStatus != HealthStatus.Healthy
        });
        app.UseHealthChecks("/api/health/checks", new HealthCheckOptions {
            Predicate = _ => true
        });
    }
}
