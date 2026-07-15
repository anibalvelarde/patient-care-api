# WP-29 (U3) — Dashboard Money-Tile Performance: Verification

**Date:** 2026-07-14/15 · **Branch:** `feature/wp-29b-tile-perf` · **Plan:**
`patient-care-super/planning/active/wp-29-dashboard-tile-perf.md`

Measured on a **fresh prod dump** (2026-07-14, 10,930 sessions / 869 patients / 23 therapists /
90 service payments) in a local `mysql:8` container; API local Release build. Server-side ms
from the new `RequestTimingMiddleware` log lines. Baseline = pre-change build, same DB, same day.

## Timings (server-side, 3 runs; first run includes EF query-plan compilation)

| Endpoint | Baseline (ms) | After (ms) | Target |
|---|---|---|---|
| `GET /api/patients/pastdue` (tile) | 2613 / 1518 / 1368 | 580 / 61 / **55** | <300 ✅ |
| `GET /api/sessions/pastdue` | 1214 / 1111 / 1283 | 36 / 30 / **31** | <300 ✅ |
| `GET /api/service-payments/pending-summary` (tile) | 966 / 875 / 850 | 159 / 80 / **78** | <300 ✅ |
| `GET /api/service-payments/pending-pay-report` | 737 / 672 / 693 | 93 / 85 / **87** | <300 ✅ |

**Phase 2 (cache) not triggered** — gate G2 (<300 ms) met by query fixes alone; zero staleness
surface added.

## Value identity (the money-regression check)

All four endpoints' JSON responses captured before and after on the same container:
**byte-identical** (`cmp`) for patients/pastdue, sessions/pastdue, pending-summary,
pending-pay-report.

## What changed

1. **Past-due predicate now runs in SQL** (`SessionEventRepository.GetAllPastDueAsync`):
   `(Amount − DiscountAmount − AmountPaid) > 0 AND SessionDate <= today−35d` (date-only —
   a strict superset of the exact date+time `GetPastDue()`; `SessionEventHandler` still applies
   the exact filter, so boundary rows resolve identically). Previously the WHOLE TherapySessions
   table was materialized with 6 Include chains and filtered in memory.
2. **Patient N+1 killed** (`SessionEventHandler.GetAllPatientsPastDueAsync`): one batched
   `IPatientProfileService.GetByIdsAsync` (profiles + one grouped discovery-status query)
   instead of 2 queries per past-due patient.
3. **Party-scoped past-due** (`GetPastDueByPatientAsync` / `GetPastDueByTherapistAsync`):
   `/api/patients/{id}/pastdue` and `/api/therapists/{id}/pastdue` no longer load everything
   and filter in the controller.
4. **Pending-summary/report single-pass** (`ServicePaymentService`): one slim SQL query
   (`TherapySessionRepository.GetOwedProviderSessionRowsAsync` — no includes, allocation sum as
   a correlated subquery, owing-filter in SQL) replaces 2 round trips × ~22 therapists.
   `GetProviderSessionsBreakdownAsync` (per-therapist views, batch payroll) is untouched.
5. **`RequestTimingMiddleware`** (gate G1): permanent per-request `method path status ms` log
   line, health probes excluded.

## EXPLAIN evidence (gate G3 — no index migration warranted)

- Past-due scan: `type=ALL, rows=10930` — but the selective predicate is the money expression
  (not indexable); a SessionDate index would match ~95% of rows and be ignored. Endpoint at
  55–61 ms warm.
- Owed-rows subquery: `type=ref` on unique index `SessionID_ServicePaymentID`, 1 row per
  lookup — optimal. Outer scan `ALL` is correct (Completed ≈ most rows).

## Repro curls

```bash
TOKEN=... # login as any holder of Patients.Delinquent.View / ServicePayments.View
curl -s -H "Authorization: Bearer $TOKEN" http://localhost:5245/api/patients/pastdue
curl -s -H "Authorization: Bearer $TOKEN" http://localhost:5245/api/sessions/pastdue
curl -s -H "Authorization: Bearer $TOKEN" http://localhost:5245/api/patients/49/pastdue
curl -s -H "Authorization: Bearer $TOKEN" http://localhost:5245/api/therapists/23/pastdue
curl -s -H "Authorization: Bearer $TOKEN" http://localhost:5245/api/service-payments/pending-summary
curl -s -H "Authorization: Bearer $TOKEN" http://localhost:5245/api/service-payments/pending-pay-report
# timing: watch the RequestTimingMiddleware lines in the API log
```

## Test suite

`dotnet test -c Release`: **483 passed / 0 failed** (156 Core / 92 Infrastructure / 235 Web) —
baseline was 477; +6 new (past-due SQL candidates ×2, party filters, batched GetByIds,
owed-rows query, scoped-handler specs).
