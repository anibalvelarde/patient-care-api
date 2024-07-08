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

namespace Neurocorp.Api.Web;

public class Startup(IConfiguration configuration)
{
    public IConfiguration Configuration { get; } = configuration;

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
            options.AddPolicy("AllowNeurocorp",
                builder => builder
                    .SetIsOriginAllowed(origin => new Uri(origin).Host.Contains("neurocorp"))                    
                    .AllowAnyHeader()
                    .AllowAnyMethod()
                    .AllowCredentials());
        });
        services.AddCors(options =>
        {
            options.AddPolicy("AllowLocalhost",
                builder => builder
                    .SetIsOriginAllowed(origin => new Uri(origin).Host.Contains("localhost"))
                    .AllowAnyHeader()
                    .AllowAnyMethod()
                    .AllowCredentials());
        });
        services.AddControllers();

        // Register the Swagger generator, defining one or more Swagger documents
        services.AddSwaggerGen(c =>
        {
            c.SwaggerDoc("v1", new OpenApiInfo { Title = "Neurocorp Web API", Version = "v1" });
        });

        // Register the hosted service
        services.AddHostedService<DbContextWarmupService>();

        // Register dependencies with DI framework
        services.AddCoreDependencies(Configuration);
        services.AddInfrastructureServices(Configuration);
    }

    // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
    public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
    {
        if (env.IsDevelopment())
        {
            app.UseDeveloperExceptionPage();
            app.UseSwagger();
            app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "Web API v1"));
        }

        app.UseHttpsRedirection();
        app.UseRouting();
        app.UseCors("AllowLocalhost");
        app.UseCors("AllowNeurocorp");
        app.Use(async (context, next) =>
        {
            if (context.Request.Method == "OPTIONS")
            {
                context.Response.Headers.Append("Access-Control-Allow-Origin", "*");
                context.Response.Headers.Append("Access-Control-Allow-Methods", "GET, POST, PUT, DELETE, OPTIONS");
                context.Response.Headers.Append("Access-Control-Allow-Headers", "Content-Type, Authorization");
                context.Response.StatusCode = 204;
                return;
            }
            await next();
        });

        app.UseAuthorization();

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
