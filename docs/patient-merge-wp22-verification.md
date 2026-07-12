# WP-22B verification — duplicate-patient merge endpoints

Both endpoints are gated by `Patients.Merge` — an **empty-grants** matrix row (hash
`6c42b5362fbc`): only the SYSADMIN `System/FullAccess` wildcard satisfies it, so **every**
granular role (MGR/AM/FD/OWN/ACCT) gets 403. Contract:
`patient-care-super/_contracts/patients-api.md` § WP-22.

⚠️ **These curls are the primary proof for the two `ExecuteUpdateAsync` bulk repoints**
(TherapySession / TreatmentPlan `PatientID`) — the InMemory test provider cannot execute them,
so they have no unit-test coverage by design. Run the full sequence against a **rehearsal DB
(prod dump in a `mysql:8` container)** first, exactly like the WP-19 promotion rehearsals.

## Setup — mint a SYSADMIN token

```bash
BASE=http://localhost:5245            # deployed: http://neurocorp.k3s:30000
TOKEN=$(curl -s -X POST $BASE/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{"username":"<sysadmin-user>","password":"<password>"}' | jq -r '.accessToken')
# NB: the field is .accessToken (NOT .token).
```

Pick a real duplicate pair (see `patient-care-db tools/legacy-import/caretaker-backfill-2024.md`
§3 — e.g. MATHIAS BULTRON PID 359 survives, MATIAS BULTRON PID 360 eliminated), or create two
throwaway patients via `POST /api/patients` on the rehearsal DB.

```bash
SURVIVOR=<survivorPatientId>
ELIMINATED=<eliminatedPatientId>
```

## 1. Access control

```bash
# No token → 401
curl -s -o /dev/null -w "%{http_code}\n" -X POST "$BASE/api/patients/merge/preview" \
  -H "Content-Type: application/json" -d "{\"survivorPatientId\":$SURVIVOR,\"eliminatedPatientId\":$ELIMINATED}"

# MGR token → 403 on BOTH routes (mint MGR_TOKEN via an MGR login)
curl -s -o /dev/null -w "%{http_code}\n" -X POST "$BASE/api/patients/merge/preview" \
  -H "Authorization: Bearer $MGR_TOKEN" -H "Content-Type: application/json" \
  -d "{\"survivorPatientId\":$SURVIVOR,\"eliminatedPatientId\":$ELIMINATED}"
curl -s -o /dev/null -w "%{http_code}\n" -X POST "$BASE/api/patients/merge" \
  -H "Authorization: Bearer $MGR_TOKEN" -H "Content-Type: application/json" \
  -d "{\"survivorPatientId\":$SURVIVOR,\"eliminatedPatientId\":$ELIMINATED}"
```

**VERIFY:** 401 / 403 / 403.

## 2. Validation errors

```bash
# Same IDs → 400
curl -s -X POST "$BASE/api/patients/merge/preview" -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" -d "{\"survivorPatientId\":$SURVIVOR,\"eliminatedPatientId\":$SURVIVOR}" | jq '{status, detail}'

# Missing patient → 404 (message names which side)
curl -s -X POST "$BASE/api/patients/merge/preview" -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" -d "{\"survivorPatientId\":$SURVIVOR,\"eliminatedPatientId\":999999}" | jq '{status, detail}'
```

## 3. Preview (side-effect-free)

```bash
curl -s -X POST "$BASE/api/patients/merge/preview" -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d "{\"survivorPatientId\":$SURVIVOR,\"eliminatedPatientId\":$ELIMINATED}" | jq .
```

**VERIFY:**
- `counts.sessionsToRemap` == `SELECT COUNT(*) FROM TherapySession WHERE PatientID = $ELIMINATED;`
- `counts.plansToRemap` == the same count on `TreatmentPlan`.
- `caretakers[]` dispositions make sense (synthetic `-SH (LEGACY)` → `retire-synthetic` when the
  survivor keeps a caretaker; shared caretaker → `dedupe-delete`).
- `blockers` is `[]` for a clean pair.
- Re-running the preview changes **nothing** (row counts identical before/after).

## 4. Execute — pre/post SQL proof of the ExecuteUpdate repoints

```sql
-- PRE (record these)
SELECT COUNT(*) FROM TherapySession WHERE PatientID = <ELIMINATED>;   -- expect N > 0
SELECT COUNT(*) FROM TherapySession WHERE PatientID = <SURVIVOR>;     -- expect M
SELECT COUNT(*) FROM TherapySession;                                  -- expect T (conservation)
SELECT COUNT(*) FROM TreatmentPlan  WHERE PatientID IN (<SURVIVOR>,<ELIMINATED>);
SELECT COUNT(*) FROM PatientMergeLog;                                 -- expect L
```

```bash
curl -s -X POST "$BASE/api/patients/merge" -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d "{\"survivorPatientId\":$SURVIVOR,\"eliminatedPatientId\":$ELIMINATED}" | jq .
```

```sql
-- POST
SELECT COUNT(*) FROM TherapySession WHERE PatientID = <ELIMINATED>;   -- expect 0
SELECT COUNT(*) FROM TherapySession WHERE PatientID = <SURVIVOR>;     -- expect M + N
SELECT COUNT(*) FROM TherapySession;                                  -- expect T (unchanged)
SELECT * FROM Patient    WHERE PatientID = <ELIMINATED>;              -- expect empty (hard-deleted)
SELECT * FROM SystemUser WHERE UserID   = <eliminated UserID>;        -- expect empty (retired)
SELECT * FROM UserRole   WHERE UserID   = <eliminated UserID>;        -- expect empty
SELECT COUNT(*) FROM PatientMergeLog;                                 -- expect L + 1
SELECT * FROM PatientMergeLog ORDER BY PatientMergeLogID DESC LIMIT 1;
  -- expect: counts == the curl result, Eliminated* snapshot populated, MergedByUserId = your user
SELECT Notes FROM Patient WHERE PatientID = <SURVIVOR>;               -- expect a [MERGED: ...] marker
-- Audit columns stamped by the bulk repoint (ExecuteUpdate sets them explicitly):
SELECT LastUpdatedByUserId, COUNT(*) FROM TherapySession
WHERE  PatientID = <SURVIVOR> AND LastUpdatedByUserId = <your UserID> GROUP BY 1;
```

## 5. API-level post-checks

```bash
curl -s -o /dev/null -w "%{http_code}\n" -H "Authorization: Bearer $TOKEN" "$BASE/api/patients/$ELIMINATED"   # 404
curl -s -H "Authorization: Bearer $TOKEN" "$BASE/api/patients/$SURVIVOR" | jq '{patientId, patientName, cedula, dateOfBirth}'
curl -s -H "Authorization: Bearer $TOKEN" "$BASE/api/patients/$SURVIVOR/sessions" | jq '.totalCount'   # M + N
curl -s -H "Authorization: Bearer $TOKEN" "$BASE/api/patients/$SURVIVOR/caretakers" | jq 'length'      # per preview
```

## 6. Blocker path (409)

On the rehearsal DB, give the eliminated patient's SystemUser an extra role
(`INSERT INTO UserRole (UserID, RoleID) VALUES (<eliminated UserID>, 3);`) and re-run preview →
`blockers` non-empty; execute → **409** with the same message. Remove the row afterwards.
