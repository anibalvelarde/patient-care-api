# WP-37B verification — SEN-1 SENADIS expiration date · expiry-aware 20% floor

Post-deploy curls (run against the deployed API, e.g. `http://neurocorp.k3s:30000`, after
**V031** is applied and the API ships this WP). All IDs/dates below are examples — substitute
real ones from your environment.

Additive, **non-breaking**. Contracts: `patient-care-super/_contracts/patients-api.md`
(`senadisExpirationDate` on PatientProfile + create/update requests, gate coupling) and
`sessions-api.md` (expiry-aware floor bullet).

**NO matrix change — hash `57b6150a350c` unchanged** (G4: the expiration date rides the
existing `Patients.SenadisDiscount.Edit` claim, MGR/AM + SYSADMIN wildcard). No new claim, no
RoleClaim reseed, no re-login required.

**Semantics being proven** (gates G1–G4, all defaults 2026-07-18):
- G1: `null` = no expiry (open-ended — all backfill-era flags start here).
- G2: expired when `expiration < the SESSION's date` (not "today") — boundary
  (session date == expiry) still floors.
- G3: expiry never auto-clears the flag; expired ⇒ simply no floor.
- G4: one claim governs both SENADIS fields; create stays ungated.
- Null-means-unchanged consequence: an expiry, once set, cannot be cleared back to null via
  PUT (extend with a later date instead).

**Prereqs (in order):**
1. DB migration **V031** applied (`Patient.SenadisExpirationDate date NULL`).
2. This API build deployed. (No reseed, no re-login — no claim change.)

## 0. Tokens

> ⚠️ The login field is `email`, NOT `username` (binds empty → 401).

```bash
API=http://neurocorp.k3s:30000   # or http://localhost:5245 for a local run
mint() { curl -s -X POST $API/api/auth/login -H 'Content-Type: application/json' \
  -d "{\"email\":\"$1\",\"password\":\"$2\"}" | jq -r .accessToken; }
MGR_TOKEN=$(mint '<mgr-email>' '<pwd>')   # holds Patients.SenadisDiscount.Edit
FD_TOKEN=$(mint '<fd-email>' '<pwd>')     # does NOT hold the gate claim
```

## 1. Set the expiration as MGR → 204; visible in the profile

```bash
# Pick a SENADIS-flagged patient (or flag one first — same claim governs both fields).
curl -s -o /dev/null -w 'MGR set expiry -> %{http_code}\n' -X PUT $API/api/patients/<pid> \
  -H "Authorization: Bearer $MGR_TOKEN" -H 'Content-Type: application/json' \
  -d '{"hasSenadisDiscount": true, "senadisExpirationDate": "2026-08-01"}'   # 204

curl -s $API/api/patients/<pid> -H "Authorization: Bearer $FD_TOKEN" \
  | jq '{patientId, hasSenadisDiscount, senadisExpirationDate}'
# expect: hasSenadisDiscount true, senadisExpirationDate "2026-08-01T00:00:00"
```

## 2. FD cannot change it → 403; FD echo-unchanged passes → 204

```bash
# FD tries to EXTEND the expiry — expect 403 (field-level gate, same claim as the flag)
curl -s -o /dev/null -w 'FD change expiry -> %{http_code}\n' -X PUT $API/api/patients/<pid> \
  -H "Authorization: Bearer $FD_TOKEN" -H 'Content-Type: application/json' \
  -d '{"senadisExpirationDate": "2027-12-31"}'                               # 403

# FD round-trips the UNCHANGED value alongside a phone edit — expect 204 (echo, not a change)
curl -s -o /dev/null -w 'FD echo expiry -> %{http_code}\n' -X PUT $API/api/patients/<pid> \
  -H "Authorization: Bearer $FD_TOKEN" -H 'Content-Type: application/json' \
  -d '{"senadisExpirationDate": "2026-08-01", "phoneNumber": "555-0199"}'    # 204

# FD omits the field entirely — expect 204, stored expiry survives (null = unchanged)
curl -s -o /dev/null -w 'FD omit expiry -> %{http_code}\n' -X PUT $API/api/patients/<pid> \
  -H "Authorization: Bearer $FD_TOKEN" -H 'Content-Type: application/json' \
  -d '{"phoneNumber": "555-0200"}'                                           # 204
curl -s $API/api/patients/<pid> -H "Authorization: Bearer $FD_TOKEN" \
  | jq '.senadisExpirationDate'                        # still "2026-08-01T00:00:00"
```

## 3. Create with an expiry as FD → 201 (create stays ungated, SEN-2)

```bash
curl -s -X POST $API/api/patients \
  -H "Authorization: Bearer $FD_TOKEN" -H 'Content-Type: application/json' \
  -d '{"firstName":"Probe","lastName":"Senadis","cedula":"<unique>","dateOfBirth":"2018-05-04",
       "email":"probe@x.com","phoneNumber":"555","gender":"Male",
       "hasSenadisDiscount":true,"senadisExpirationDate":"2027-06-30"}' \
  | jq '{patientId, hasSenadisDiscount, senadisExpirationDate}'              # 201, both set
```

## 4. Expired ⇒ NO floor at booking (single path)

Patient from step 1 has expiry **2026-08-01**.

```bash
# Session AFTER the expiry (G2: compared against the SESSION date) — discount stays 5.00
curl -s -X POST $API/api/sessions \
  -H "Authorization: Bearer $MGR_TOKEN" -H 'Content-Type: application/json' \
  -d '{"sessionDate":"2026-08-02","sessionTime":"10:00:00","patientId":<pid>,
       "therapistId":<tid>,"specialtyTypeId":6,"duration":60,"amount":100.00,"discount":5.00}' \
  | jq '{sessionId, amount, discount}'                 # discount == 5.00 (NO floor)

# Flag is NOT auto-cleared by the expired booking (G3):
curl -s $API/api/patients/<pid> -H "Authorization: Bearer $MGR_TOKEN" \
  | jq '{hasSenadisDiscount, senadisExpirationDate}'   # still true / 2026-08-01
```

## 5. Unexpired / boundary / null-expiry ⇒ floor applies

```bash
# Session BEFORE the expiry — floored to 20.00
curl -s -X POST $API/api/sessions \
  -H "Authorization: Bearer $MGR_TOKEN" -H 'Content-Type: application/json' \
  -d '{"sessionDate":"2026-07-25","sessionTime":"10:00:00","patientId":<pid>,
       "therapistId":<tid>,"specialtyTypeId":6,"duration":60,"amount":100.00,"discount":5.00}' \
  | jq '.discount'                                     # 20.00

# BOUNDARY: session date == expiry date — still floored (G2: on/before the expiry counts)
curl -s -X POST $API/api/sessions \
  -H "Authorization: Bearer $MGR_TOKEN" -H 'Content-Type: application/json' \
  -d '{"sessionDate":"2026-08-01","sessionTime":"11:00:00","patientId":<pid>,
       "therapistId":<tid>,"specialtyTypeId":6,"duration":60,"amount":100.00,"discount":5.00}' \
  | jq '.discount'                                     # 20.00

# NULL expiry (a backfill-era flagged patient, expiry never set) — floored, any date
curl -s -X POST $API/api/sessions \
  -H "Authorization: Bearer $MGR_TOKEN" -H 'Content-Type: application/json' \
  -d '{"sessionDate":"2027-01-15","sessionTime":"10:00:00","patientId":<null-expiry-pid>,
       "therapistId":<tid>,"specialtyTypeId":6,"duration":60,"amount":100.00,"discount":5.00}' \
  | jq '.discount'                                     # 20.00
```

## 6. Bulk path — expiry evaluated per session date

```bash
# Active plan for the step-1 patient (expiry 2026-08-01), 2 weekly sessions starting the
# Monday before the expiry: session 1 on/before expiry is floored, session 2 after it is not.
curl -s -X POST $API/api/treatmentplans/<planId>/schedule \
  -H "Authorization: Bearer $MGR_TOKEN" -H 'Content-Type: application/json' \
  -d '{"startDate":"2026-07-27","siteId":1}' \
  | jq '.sessions[] | {sessionDate, amount, discountAmount}'
# expect: 2026-07-27 row discountAmount == 13.00 (20% of 65.00); 2026-08-03 row == 0
```

## Expected results summary

| Check | Expect |
|---|---|
| MGR PUT `senadisExpirationDate` | `204`, value in profile |
| FD PUT changed `senadisExpirationDate` | `403`, nothing applied |
| FD PUT echoed-unchanged / omitted | `204`, stored value survives |
| FD POST create with expiry | `201`, both SENADIS fields set (ungated) |
| Booking dated AFTER expiry | discount untouched (no floor); flag stays `true` |
| Booking dated before / ON expiry / null expiry | discount floored to `round(0.20 × amount, 2)` |
| Bulk schedule spanning expiry | only sessions on/before expiry floored |
| Matrix | hash `57b6150a350c` unchanged, no reseed, no re-login |

## Suite state

`dotnet test patient-care-api.sln` → **646 green** (Core 256 / Infrastructure 117 / Web 273;
was 626 pre-WP-37B). Regression-test-first: the expired-⇒-no-floor tests (both paths), the
mid-schedule split, the edit-gate 403, and the create/update mappings were written first and
watched fail red against the WP-23 always-floor behavior, then the shared predicate
(`Patient.IsSenadisDiscountActive` / `HasActiveSenadisDiscount(sessionDate)`) went in to green.
New coverage: expired ⇒ no floor (single + bulk), boundary session==expiry floors, unexpired +
null-expiry floor, per-session-date evaluation across a schedule spanning the expiry, entity
predicate truth table, controller gate (Forbid / delegate / wildcard / echo / omit), create
maps expiry ungated, repository null-unchanged + value-set round trip.
