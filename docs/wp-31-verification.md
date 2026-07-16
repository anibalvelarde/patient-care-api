# WP-31B (U1) — Audit Info block: curl verification

Additive, **non-breaking**. Contracts: `patient-care-super/_contracts/{patients,caretakers,sessions}-api.md`.

Every `PatientProfile`, `CaretakerProfile`, and `SessionEvent` now carries an additive `audit`
block: `{ createdAt, updatedAt, updatedBy }`. `updatedBy` is a display string ("Lastname,
Firstname") resolved from `LastUpdatedByUserId`, or `"System"` for id 0 / unattributable ids (a
plain int, not an FK — best effort). No raw id is serialized. Visible to any caller holding the
row's `*.View` claim — **no matrix change (hash `7ae3aa5e3274`)**, no reseed, no re-login.

## Setup

```bash
API=http://neurocorp.k3s:30000   # or http://localhost:5245 for a local run
TOKEN=$(curl -s -X POST $API/api/auth/login -H 'Content-Type: application/json' \
  -d '{"email":"<user>","password":"<pass>"}' | jq -r .accessToken)
AUTH="Authorization: Bearer $TOKEN"
```

## 1) Patients — audit block on the paged list + by-id

```bash
# Paged list: every item has audit; names resolved (or "System")
curl -s -H "$AUTH" "$API/api/patients?pageSize=3" | jq '.items[] | {patientName, audit}'

# By id
curl -s -H "$AUTH" "$API/api/patients/49" | jq '{patientName, audit}'
```

Expect each `audit` = `{ "createdAt": "...", "updatedAt": "...", "updatedBy": "Lastname, Firstname | System" }`.

## 2) Caretakers — audit block on the paged list + by-id

```bash
curl -s -H "$AUTH" "$API/api/caretakers?pageSize=3" | jq '.items[] | {caretakerName, audit}'
curl -s -H "$AUTH" "$API/api/caretakers/1" | jq '{caretakerName, audit}'
```

## 3) Session Details — the popover surface (name resolved)

```bash
# Single session (details panel path) — updatedBy resolved to a display name
curl -s -H "$AUTH" "$API/api/sessions/<id>" | jq '{sessionId, patient, audit}'

# Appointments-by-date (feeds the details panel) — every event carries audit
curl -s -H "$AUTH" "$API/api/sessions/2026-07-15/all" | jq '.[0] | {sessionId, audit}'
```

## 4) Two-mapper proof — the booking path also carries the block

`SessionEvent` is produced by TWO mappers that must stay in sync (`ExtractSessionEvent` +
`BookingRepository.MapToSessionEvent`). Both now carry `audit`:

```bash
# Booking path (unconfirmed) — audit block present (timestamps; updatedBy defaults "System"
# on this non-popover path, resolved only on the detail/appointments paths above)
curl -s -H "$AUTH" "$API/api/booking/unconfirmed?from=2026-07-01&to=2026-07-31" \
  | jq '.[0] | {sessionId, audit}'
```

If either mapper is missed, this response lacks `audit` while §3 has it — the classic two-mapper drift.

## 5) Legacy / unattributable → "System"

```bash
# A legacy-imported patient (L24/L25 MRN) whose last write was a script (LastUpdatedByUserId 0)
curl -s -H "$AUTH" "$API/api/patients?search=L24" | jq '.items[0] | {patientName, updatedBy: .audit.updatedBy}'
# expect "System"
```

## Expected results summary

| Check | Expect |
|---|---|
| `GET /api/patients` items | each has `audit {createdAt, updatedAt, updatedBy}` |
| `GET /api/caretakers/{id}` | `audit` present |
| `GET /api/sessions/{id}` | `audit` present, `updatedBy` a resolved name (or "System") |
| booking `/unconfirmed` | `audit` present (two-mapper proof) |
| legacy/script-written row | `updatedBy: "System"` |
| no raw `updatedByUserId` in any JSON | confirmed (serialization-ignored) |

---

**Unit coverage (no live DB):** `Core.Tests/AuditInfoTests` (aggregate later-wins + FromEntity),
`AuditEnrichmentTests` (batched resolve, 0/miss → "System", single call), `AuditServiceEnrichmentTests`
(service resolves end-to-end / no-op without resolver); `Infrastructure.Tests/UserNameResolverTests`
(batched, excludes 0/unknown, dedupes), `AuditPopoverMapperTests` (**both** SessionEvent mappers +
Patient + Caretaker carry the block). Suite **547** green (533 baseline + 14).
