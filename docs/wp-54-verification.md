# WP-54B — API verification (SYSADMIN entity change log)

Contract: `patient-care-super/_contracts/admin-change-log-api.md`. Field NAMES only, no values (G2).
Prereqs: DB migration **V035** applied (table exists) + the WP-54 matrix cycle deployed (both
claims seeded). Base URL `http://localhost:5245` (local) or `http://neurocorp.k3s:30000` (K3s).

Get a SYSADMIN token from `POST /api/auth/login`, then `TOKEN=…`.

## Automated coverage (already green)

- **Interceptor** (`tests/Infrastructure.Tests/ChangeLogInterceptorTests.cs`, 7): Insert/Update/Delete
  each write one row; Insert reads back the generated key; Update logs only the changed field; an
  update touching only excluded columns logs nothing; the kill-switch and no-current-user paths;
  **the value never appears in the row** (names only); the log never audits itself.
- **Service** (`tests/Core.Tests/ChangeLogServiceTests.cs`, 6): day→period rollup (month/day,
  zero-filled), `byPeriodOmitted` guard, by-user name resolution, list mapping + field-name JSON,
  purge delegation.
- **Authorization** (`tests/Web.Tests/Authorization/ChangeLogAuthorizationTests.cs`, 23): every
  granular role → 403 on all four routes; SYSADMIN wildcard not blocked; anonymous → 401; and
  **View can read but not purge / Purge can purge but not read** (the two policies are distinct).

Full suite: **1,030 pass, 0 fail**; Release build clean (warnings-as-errors).

## Manual curls (run against the deployed/local API)

### 1. As SYSADMIN, edit a patient's phone, then read the change back — names only, no value
```bash
# (make some change through the app or another endpoint first, then:)
curl -s -H "Authorization: Bearer $TOKEN" \
  "http://localhost:5245/api/admin/change-log?entityType=User&page=1&pageSize=5" | jq
# Expect: items[].changedFields is an array of field NAMES (e.g. ["PhoneNumber"]);
#         no phone number appears anywhere in the response.
```

### 2. Summary — by-user leads, monthly buckets in the observer tz (Panama = -300)
```bash
curl -s -H "Authorization: Bearer $TOKEN" \
  "http://localhost:5245/api/admin/change-log/summary?groupBy=month&tzOffsetMinutes=-300&from=2026-06-01T00:00:00Z&to=2026-09-01T00:00:00Z" | jq
# Expect: { totals, byUser:[…], byEntityType:[…], byPeriod:[{label:"2026-06",…}], byPeriodOmitted:false }
```

### 3. Entity types (stable filter chips)
```bash
curl -s -H "Authorization: Bearer $TOKEN" \
  http://localhost:5245/api/admin/change-log/entity-types | jq
# Expect: [ {entityType:"Patient",displayName:"Patient"}, … ]  (EntityChangeLog itself excluded)
```

### 4. A Manager token → 403 on every route
```bash
curl -s -o /dev/null -w "%{http_code}\n" -H "Authorization: Bearer $MGR_TOKEN" \
  http://localhost:5245/api/admin/change-log/summary          # 403
```

### 5. Purge — dry-run first, then confirm (SYSADMIN, API-only)
```bash
curl -s -X DELETE -H "Authorization: Bearer $TOKEN" \
  "http://localhost:5245/api/admin/change-log?olderThanDays=1100" | jq
# Expect: { olderThanDays:1100, cutoffUtc:…, matched:N, deleted:0, dryRun:true }
curl -s -X DELETE -H "Authorization: Bearer $TOKEN" \
  "http://localhost:5245/api/admin/change-log?olderThanDays=1100&confirm=true" | jq
# Expect: { …, deleted:N, dryRun:false }
curl -s -o /dev/null -w "%{http_code}\n" -X DELETE -H "Authorization: Bearer $TOKEN" \
  "http://localhost:5245/api/admin/change-log?olderThanDays=0"  # 400 (must be >= 1)
```

### 6. Health check reports the change log
```bash
curl -s http://localhost:5245/api/health/checks | jq '.Statuses[] | select(.Name=="changelog")'
# Expect: { Name:"changelog", Status:"Healthy", Description:"Change log reachable; capture enabled." }
```

### 7. Kill-switch drill (do once post-deploy)
```bash
# set ChangeLog__Enabled=false, restart the API, make a change, confirm NO new row, then set back to true.
```

## Notes for the local MySQL rehearsal
- The per-day summary grouping applies `tzOffsetMinutes` in the query (`OccurredAtUtc.AddMinutes(offset).Date`).
  Confirm Pomelo translates it against real MySQL (InMemory unit tests evaluate client-side); if a
  translation error surfaces, switch `ByDayAsync` to a parameterized raw SQL day bucket.
- The purge uses `ExecuteDeleteAsync` (bulk, bypasses the interceptor by design) — exercise the
  confirm=true path here (InMemory does not support ExecuteDelete).
