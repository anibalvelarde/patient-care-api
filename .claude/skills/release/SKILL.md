---
name: release
description: Validate API release readiness
user-invocable: true
---

# Release — API

## Build & Test
- [ ] `dotnet restore patient-care-api.sln` — all packages resolve
- [ ] `dotnet build patient-care-api.sln` — no errors, no new warnings
- [ ] `dotnet test patient-care-api.sln` — all tests pass

## API Verification
- [ ] All routes documented in `docs/api-reference.md`
- [ ] No breaking changes to existing endpoints
- [ ] Swagger UI loads in Development mode
- [ ] Health checks respond (`/api/health/live`, `/ready`, `/startup`, `/checks`)
- [ ] Local stack: `../patient-care-db/tools/local-dev/local-dev.sh up` with this branch checked out.
      The API starts against the synthetic DB, never a bare `dotnet run`, which hits **prod**.
      Then `local-dev.sh walk` exits 0 (no 5xx), then `down`.

## Infrastructure
- [ ] Docker build succeeds: `docker build -f build/Dockerfile -t patient-care-api .`
- [ ] Env var requirements unchanged (or documented)

## Code Quality
- [ ] No TODO/HACK in production code
- [ ] No hardcoded credentials
- [ ] CORS policies appropriate

## Documentation
- [ ] `README.md` reflects new setup steps
- [ ] `docs/architecture.md` updated if architecture changed
- [ ] ADRs created for significant decisions
- [ ] `CLAUDE.md` updated if paths/conventions changed
