# Patient Care API

A REST API for managing therapy clinic operations — patients, therapists, caretakers, therapy sessions, and billing — built for the Neurocorp platform.

## Architecture

The solution follows **clean architecture** with three layers:

```
src/
├── Core/             # Domain entities, business objects, service interfaces & implementations
├── Infrastructure/   # EF Core repositories, MySQL DbContext, health checks
└── Web/              # ASP.NET Core API controllers, Swagger, CORS, middleware
```

- **Core** — No infrastructure dependencies. Contains entities (`Patient`, `Therapist`, `Caretaker`, `TherapySession`, `Payment`), DTOs (profile objects, request/update models), and service logic including past-due billing rules.
- **Infrastructure** — Entity Framework Core with the [Pomelo MySQL provider](https://github.com/PomeloFoundation/Pomelo.EntityFrameworkCore.MySql). Implements the repository interfaces defined in Core.
- **Web** — API controllers, Swagger/OpenAPI documentation, CORS policies, and health check endpoints.

## Tech Stack

| Component | Technology |
|---|---|
| Framework | .NET 8 / ASP.NET Core |
| Database | MySQL (via Pomelo EF Core) |
| API Docs | Swagger / Swashbuckle |
| Testing | xUnit, Moq, FluentAssertions, Coverlet |
| CI/CD | GitHub Actions |
| Container | Docker (multi-stage build) |
| License | GPL-3.0 |

## Getting Started

### Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- A MySQL instance (local or remote)

### Configuration

The app reads database connection details from environment variables:

| Variable | Description |
|---|---|
| `DATABASE_HOST` | MySQL server hostname |
| `DATABASE_PORT` | MySQL server port |
| `DATABASE_NAME` | Database name |
| `DATABASE_USER` | Database username |
| `DATABASE_PASSWORD` | Database password |

### Run Locally

```bash
# Restore dependencies
dotnet restore patient-care-api.sln

# Build
dotnet build patient-care-api.sln

# Run
dotnet run --project src/Web/Web.csproj
```

The API will start on `http://localhost:5245` by default.

### Run with Docker

```bash
# Build the image
docker build -f build/Dockerfile -t patient-care-api .

# Run the container
docker run -p 5245:5245 \
  -e DATABASE_HOST=your-host \
  -e DATABASE_PORT=3306 \
  -e DATABASE_NAME=your-db \
  -e DATABASE_USER=your-user \
  -e DATABASE_PASSWORD=your-password \
  patient-care-api
```

The Docker image uses a multi-stage build that restores, builds, tests, and publishes the app. The container port varies by environment: `5245` (default), `8000` (QA), `80` (PROD) — controlled by `ASPNETCORE_ENVIRONMENT`.

## API Overview

The API exposes the following resource endpoints under `/api`:

| Resource | Base Route | Operations |
|---|---|---|
| Patients | `/api/patients` | CRUD + past-due billing summary |
| Therapists | `/api/therapists` | CRUD + past-due billing summary |
| Caretakers | `/api/caretakers` | CRUD |
| Sessions | `/api/sessions` | Create, update, list by date, list past-due |
| Health | `/api/health` | Liveness, readiness, startup probes, full health report |

For full endpoint details including request/response schemas, visit the **Swagger UI** at `/swagger` when running in the Development environment.

## Health Checks

The API provides Kubernetes-style health probes:

| Endpoint | Purpose |
|---|---|
| `GET /api/health/live` | Liveness — is the process running? |
| `GET /api/health/ready` | Readiness — is the app ready to serve traffic? |
| `GET /api/health/startup` | Startup — timestamp and uptime |
| `GET /api/health/checks` | Full report including database connectivity |

## Testing

```bash
dotnet test patient-care-api.sln
```

Tests are organized to mirror the source structure:

| Project | Scope |
|---|---|
| `Core.Tests` | Domain services and business object logic |
| `Infrastructure.Tests` | Repository integration tests (EF Core InMemory) |
| `Web.Tests` | Controller unit tests |

## CI/CD

GitHub Actions workflows are defined in `.github/workflows/`:

- **PR Validation** — Restores, builds, and tests on every pull request to `main`.
- **Main Build** — On push to `main`, runs the full pipeline and publishes a versioned Docker image to Docker Hub.
