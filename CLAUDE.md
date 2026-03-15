# Patient Care API — Claude Code Guide

## What is this?

A .NET 8 REST API for managing therapy clinic operations (patients, therapists, caretakers, sessions, billing) built for the Neurocorp platform. Part of a three-repo system: Vue UI, this API, and a MySQL database.

## Default Behavior

When starting a new conversation or after a `/clear`, follow this sequence before doing any work:

1. **Enter plan mode first.** For any non-trivial request (bug fix, new feature, refactor), enter plan mode before writing code. Only skip planning for single-line fixes, typos, or questions.
2. **Orient yourself.** Quickly review:
   - This file (`CLAUDE.md`) for project conventions
   - `docs/architecture.md` for system design context
   - `docs/adr/` for architectural decisions already made
   - `git log --oneline -10` for recent changes and active work
3. **Confirm the task.** Summarize your understanding of what the user wants and your proposed approach before implementing. Ask clarifying questions if the request is ambiguous.
4. **Run verification after changes.** Always run `npx vue-tsc --noEmit` and `npm run lint` from `app-ui/` before considering work complete.

## Architecture

Clean architecture with three layers — see [docs/architecture.md](docs/architecture.md) for details.

| Layer | Project | Namespace root |
|---|---|---|
| Domain | `src/Core/` | `Neurocorp.Api.Core` |
| Data access | `src/Infrastructure/` | `Neurocorp.Api.Infrastructure` |
| HTTP API | `src/Web/` | `Neurocorp.Api.Web` |

## Key paths

| What | Where |
|---|---|
| Solution file | `patient-care-api.sln` |
| Entities | `src/Core/Entities/` |
| Business objects / DTOs | `src/Core/BusinessObjects/` |
| Service interfaces | `src/Core/Interfaces/Services/` |
| Service implementations | `src/Core/Services/` |
| Repository interfaces | `src/Core/Interfaces/Repositories/` |
| Repository implementations | `src/Infrastructure/Repositories/` |
| EF Core DbContext | `src/Infrastructure/Data/ApplicationDbContext.cs` |
| API controllers | `src/Web/Controllers/` |
| DI registration (Core) | `src/Core/Configurations/Dependencies.cs` |
| DI registration (Infra) | `src/Infrastructure/Configurations/Dependencies.cs` |
| Tests | `tests/Core.Tests/`, `tests/Infrastructure.Tests/`, `tests/Web.Tests/` |
| CI/CD | `.github/workflows/` |
| Docker | `build/Dockerfile` |
| Helm chart | `deploy/helm/patient-care-api/` |
| K3s deploy script | `deploy/helm/patient-care-api/deploy.sh` |
| Docs | `docs/` |
| ADRs | `docs/decisions/` |
| Runbooks | `docs/runbooks/` |

## Build & test

```bash
dotnet restore patient-care-api.sln
dotnet build patient-care-api.sln
dotnet test patient-care-api.sln
dotnet run --project src/Web/Web.csproj   # starts on http://localhost:5245
```

## Conventions

- **Dependency flow**: Web -> Core <- Infrastructure (Core has zero infra dependencies)
- **DI**: All services are `Scoped`; registered via extension methods in each layer's `Configurations/Dependencies.cs`
- **Entities**: Inherit from `PersonBase` (people) or `AuditableEntityBase` (audited records)
- **DTOs**: Separate Request, UpdateRequest, and Profile objects per domain concept
- **Naming**: PascalCase for C# types/members; controllers named `{Resource}Controller`
- **Database**: MySQL via Pomelo EF Core provider; connection built from env vars (`DATABASE_HOST`, `DATABASE_PORT`, `DATABASE_NAME`, `DATABASE_USER`, `DATABASE_PASSWORD`)
- **Testing**: xUnit + Moq + FluentAssertions; test projects mirror `src/` structure
