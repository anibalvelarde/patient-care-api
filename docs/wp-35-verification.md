# WP-35B verification — SH-1 firstSessionDate · from/to date-range params

Post-deploy curls (run against the deployed API, e.g. `http://neurocorp.k3s:30000`, after this
WP ships). All IDs/dates below are examples — substitute real ones from your environment.

Additive, **non-breaking**. Contract: `patient-care-super/_contracts/patients-api.md`
(`firstSessionDate` on `PatientSessionHistorySummary`; `from`/`to` query params on
`GET /api/patients/{patientId}/sessions`).

**NO matrix change** — both endpoints keep riding the existing `Patients.View` claim
(ACCT/AM/FD/MGR/OWN + SYSADMIN wildcard). No new claim, no reseed, no re-login.

**Semantics being proven** (owner rulings 2026-07-18):
- G1: `firstSessionDate` = earliest session of **ANY status** (no status filter — consistent
  with `totalSessions`); `null` for zero-session patients.
- `from`/`to` are **date-only, INCLUSIVE** bounds on `sessionDate` (`>= from`, `<= to`),
  applied in SQL ahead of count/paging — `totalCount` reflects the ranged set.
- Omitted `from`/`to` = unchanged behavior (full set). Params combine with the existing
  `status`/`isDiscovery` filters.

**Prereqs:** this API build deployed. (No DB migration — SH-1 is a query-shape change only.)

> ⚠️ `/health` and `/version` now require auth (post-WP-37) — don't use them unauthenticated
> as a smoke test.

## 0. Tokens

> ⚠️ The login field is `email`, NOT `username` (binds empty → 401).

```bash
API=http://neurocorp.k3s:30000   # or http://localhost:5245 for a local run
mint() { curl -s -X POST $API/api/auth/login -H 'Content-Type: application/json' \
  -d "{\"email\":\"$1\",\"password\":\"$2\"}" | jq -r .accessToken; }
TOKEN=$(mint '<any-Patients.View-holder>' '<pwd>')   # FD/AM/MGR/ACCT/OWN all qualify
```

## 1. Summary rows carry firstSessionDate (SH-1)

```bash
# Multi-session patient: firstSessionDate <= lastSessionDate, both yyyy-MM-dd.
curl -s "$API/api/patients/session-history?pageSize=5" -H "Authorization: Bearer $TOKEN" \
  | jq '.items[] | {patientId, patientName, firstSessionDate, lastSessionDate, totalSessions}'
# expect on every row with totalSessions > 0: firstSessionDate non-null and <= lastSessionDate

# Zero-session patient (they sort LAST — grab the final page): both dates null, count 0.
TOTAL=$(curl -s "$API/api/patients/session-history?pageSize=1" -H "Authorization: Bearer $TOKEN" | jq .totalCount)
curl -s "$API/api/patients/session-history?page=$TOTAL&pageSize=1" -H "Authorization: Bearer $TOKEN" \
  | jq '.items[0] | {firstSessionDate, lastSessionDate, totalSessions}'
# expect (if any zero-session patient exists): {firstSessionDate: null, lastSessionDate: null, totalSessions: 0}

# Single-session patient sanity: firstSessionDate == lastSessionDate.
```

## 2. from-only / to-only filtering (inclusive)

Pick a patient `<pid>` whose summary row shows a known span, e.g. first `2024-03-15`,
last `2026-07-02`.

```bash
# from-only: nothing older than the bound; the bound date itself still included.
curl -s "$API/api/patients/<pid>/sessions?from=2026-01-01&pageSize=100" \
  -H "Authorization: Bearer $TOKEN" | jq '{totalCount, oldest: (.items | last | .sessionDate)}'
# expect: oldest >= 2026-01-01

# to-only: nothing newer than the bound.
curl -s "$API/api/patients/<pid>/sessions?to=2025-12-31&pageSize=100" \
  -H "Authorization: Bearer $TOKEN" | jq '{totalCount, newest: .items[0].sessionDate}'
# expect: newest <= 2025-12-31

# BOUNDARY: from == an actual session date — that session must be IN the result (inclusive).
curl -s "$API/api/patients/<pid>/sessions?from=<a-known-sessionDate>&to=<same-date>" \
  -H "Authorization: Bearer $TOKEN" | jq '{totalCount, dates: [.items[].sessionDate]}'
# expect: totalCount >= 1, every date == the bound (single-day range works)
```

## 3. Both bounds + omitted = unchanged

```bash
# Ranged: totalCount reflects the RANGE (not the patient's full set).
curl -s "$API/api/patients/<pid>/sessions?from=2026-01-01&to=2026-06-30&pageSize=100" \
  -H "Authorization: Bearer $TOKEN" | jq '{totalCount, dates: [.items[].sessionDate]}'
# expect: every date within [2026-01-01, 2026-06-30]

# Omitted from/to: identical to pre-WP-35 behavior (full set, newest first).
curl -s "$API/api/patients/<pid>/sessions?pageSize=100" -H "Authorization: Bearer $TOKEN" \
  | jq .totalCount
# expect: the patient's full session count (matches totalSessions on the summary row)

# Combines with existing filters:
curl -s "$API/api/patients/<pid>/sessions?status=Completed&from=2026-01-01" \
  -H "Authorization: Bearer $TOKEN" | jq .totalCount
```

## 4. Bad date input → 400 (model binding)

```bash
curl -s -o /dev/null -w 'garbage from -> %{http_code}\n' \
  "$API/api/patients/<pid>/sessions?from=not-a-date" -H "Authorization: Bearer $TOKEN"   # 400
```

## Expected results summary

| Check | Expect |
|---|---|
| Summary row, sessions > 0 | `firstSessionDate` non-null, `<= lastSessionDate`, `yyyy-MM-dd` |
| Summary row, zero sessions | `firstSessionDate: null` (alongside null last / 0 total) |
| `from=` / `to=` | date-only inclusive bounds; `totalCount` = ranged set |
| `from` == a session's date | that session included (boundary inclusive) |
| `from`/`to` omitted | byte-identical pre-WP-35 behavior |
| `from=not-a-date` | `400` (DateOnly model binding) |
| Matrix | unchanged — `Patients.View` only, no reseed, no re-login |

## Suite state

`dotnet test patient-care-api.sln` → **666 green** (Core 266 / Infrastructure 126 / Web 274;
was 661 pre-WP-35B). Regression-test-first: the 5 new tests (summary first+last dates +
zero-session null-safety; from-only / to-only / both+boundary+omitted range filtering;
controller forwards from/to) were written first and watched fail red, then the
`MIN(SessionDate)` correlated subquery, the SQL range predicates, and the controller
threading went in to green. `dotnet build -c Release --no-incremental` → 0 warnings
(A1 ratchet clean).
