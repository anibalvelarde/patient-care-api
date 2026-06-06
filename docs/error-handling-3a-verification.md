# Error Handling Verification — Remediation 2026, Chunk 3A (ProblemDetails)

Copy-paste `curl` checks for the centralized RFC 7807 `application/problem+json` error handling.
Run against a running API; most endpoints require a bearer token (see `auth-1b-verification.md`).

```bash
BASE=http://localhost:5245          # or http://neurocorp.k3s:30000 on the cluster
TOKEN=...                           # an access token from /api/auth/login
```

All error responses now share this shape (fields may include `detail`, plus `traceId` and, for the
must-change gate, `code`):
```json
{ "type": "https://httpstatuses.io/<status>", "title": "...", "status": <status>,
  "detail": "...", "instance": "GET /api/...", "traceId": "..." }
```

## 400 — validation / bad argument
A bad request body or argument bubbles an `ArgumentException` to the global handler → 400.
```bash
curl.exe -i -X PUT $BASE/api/sessions/0 -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" -d "{}"
```
**Expect:** `400`, `Content-Type: application/problem+json`, `title: "Bad Request"`, a `detail`.
Model-validation failures (DataAnnotations) likewise return a `ValidationProblemDetails` 400 with the
same `type`/`traceId` shape (handled by `[ApiController]` + `CustomizeProblemDetails`).

## 401 — unauthenticated (from 1B, now uniform)
```bash
curl.exe -i $BASE/api/patients
```
**Expect:** `401`, `problem+json`, `title: "Unauthorized"`.

## 403 — authenticated but not permitted (from 1B)
```bash
curl.exe -i $BASE/api/auth/admin-check -H "Authorization: Bearer $NON_SA_TOKEN"
```
**Expect:** `403`, `problem+json`, `title: "Forbidden"`.

## 404 — resource not found
Endpoints/services that throw `NotFoundException` map to a 404 ProblemDetails (`title: "Not Found"`).
(The booking endpoints advertise 404 via `[ProducesResponseType]`; services can throw
`NotFoundException` to realize it.)

## 409 — duplicate key
A unique-constraint violation (MySQL 1062) maps to 409 with a friendly message — e.g. creating a
second `SystemUser` with an existing email:
```bash
# (depends on data) attempt a create that violates a unique index
```
**Expect:** `409`, `problem+json`, `title: "Conflict"`,
`detail: "A user with this email address already exists."` (or the generic duplicate message).

## 500 — unexpected
Any unmapped exception returns a generic 500 ProblemDetails — **no internal details leak**; the full
exception is written to the server logs with the matching `traceId`.
**Expect:** `500`, `title: "An unexpected error occurred."`, `detail` absent.

---

## Automated coverage
- `tests/Web.Tests/GlobalExceptionHandlerTests.cs` — asserts the 400 / 404 / 500 mapping, titles, and
  that 500 leaks no `detail`. Full suite green (196 tests).
- Mechanism: `AddProblemDetails` + `CustomizeProblemDetails` (shared shape) and a global
  `IExceptionHandler` (`GlobalExceptionHandler`) wired via `UseExceptionHandler` in `Startup`.
