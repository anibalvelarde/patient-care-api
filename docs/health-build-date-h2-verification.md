# H2 — API build date on `/api/health/checks` — curl verification

Punch-list item H2 (2026-07-07 meeting): Admin › About › Build Info was missing the REST API
build date. The Web assembly is now stamped at compile time with an ISO-8601 UTC timestamp
(MSBuild `AssemblyMetadata`, key `BuildTimestampUtc`, in `src/Web/Web.csproj`), exposed as
`buildTimeUtc` on the existing anonymous health endpoint. Contract: `health-api.md`.

## 1. Field present and parseable

```bash
API=http://localhost:5245   # or the deployed base URL

curl -s "$API/api/health/checks" | jq '{version, buildTimeUtc}'
```

**Expect:** both fields non-null, e.g.

```json
{
  "version": "121.0.0.0",
  "buildTimeUtc": "2026-07-11T18:24:03.1234567Z"
}
```

- `buildTimeUtc` parses as ISO-8601 (`date -d` / `DateTimeOffset.Parse` round-trip).
- `"unknown"` would mean the assembly was built without the csproj metadata (should never
  happen once this change is in — the stamp needs no CI/build-arg wiring).

## 2. No auth required (unchanged)

```bash
curl -s -o /dev/null -w "%{http_code}\n" "$API/api/health/checks"
```

**Expect:** `200` with no Authorization header (HealthController is `[AllowAnonymous]`, K8s probes).

## 3. Sanity: build date coheres with the deployed version

After the next deploy, the timestamp should match the CI run that produced the image tag:

```bash
curl -s "$API/api/health/checks" | jq -r '.buildTimeUtc'
# compare against the GitHub Actions run timestamp for the deployed build number
```

## Rollout note

Older deployed builds (≤ v120) omit `buildTimeUtc` entirely; the UI About panel renders "—"
for the API-built row until the API is redeployed with this change.

## Automated coverage

- `Web.Tests/HealthControllerTests.cs`
  - `BuildInfo_TimestampIsStampedAndParseableIso8601Utc` — assembly stamp exists and parses.
  - `GetHealthChecks_IncludesVersionAndBuildTimeUtc` — controller payload carries it.
  - `HealthChecksEndpointTests.GetHealthChecks_SerializesBuildTimeUtcInCamelCase` — wire-level
    JSON through the real host (`AccessControlTestFactory`): camelCase `buildTimeUtc`.
