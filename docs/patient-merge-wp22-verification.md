# WP-22B verification — duplicate-patient merge endpoints

Both endpoints are gated by `Patients.Merge` — an **empty-grants** matrix row (hash
`6c42b5362fbc`): only the SYSADMIN `System/FullAccess` wildcard satisfies it, so **every**
granular role (MGR/AM/FD/OWN/ACCT) gets 403. Contract:
`patient-care-super/_contracts/patients-api.md` § WP-22.

⚠️ **These curls are the primary proof for the two `ExecuteUpdateAsync` bulk repoints**
(TherapySession / TreatmentPlan `PatientID`) — the InMemory test provider cannot execute them,
so they have no unit-test coverage by design. Run the full sequence against a **rehearsal DB
(prod dump in a `mysql:8` container)** first, exactly like the WP-19 promotion rehearsals.

## Rehearsal environment — `wp19-rehearsal` (local Docker) ✅ USED 2026-07-11

A prod-dump `mysql:8` container on the owner's workstation. **Local-rehearsal-only
credentials — never valid anywhere else:**

| What | Value |
|---|---|
| Container | `wp19-rehearsal` (mysql:8, host port **33061** → 3306) |
| Database | `neurocorp` — root / `rehearse19` |
| App login (SA) | `anibalvelarde+sa@gmail.com` / `rehearse19` (hash reset in-container, see below) |
| API | `http://localhost:52455` (run from `feature/wp-22b-patient-merge`) |

**One-time DB prep** (all idempotent; already applied 2026-07-11 — re-run only on a fresh
dump). The dump predated V024/V025 and the OWN/ACCT RoleClaim seed:

```bash
cd patient-care-db
# 1. Migrations the dump is missing
docker exec -i wp19-rehearsal mysql -uroot -prehearse19 neurocorp \
  < migrations/V024__widen_patient_gender_enum.sql
git show origin/feature/wp-22a-patient-merge-log:migrations/V025__create_patient_merge_log.sql \
  | docker exec -i wp19-rehearsal mysql -uroot -prehearse19 neurocorp     # (plain path once 22A merges)
# 2. Current RoleClaim seed (adds OWN/ACCT; needed only for role-based 403 curls)
docker exec -i wp19-rehearsal mysql -uroot -prehearse19 neurocorp < scripts/seed-role-claims.sql
# 3. Known SA password for the rehearsal app login (hash from the committed helper):
cd ../patient-care-api && dotnet run --project tools/SaPasswordHasher -- "rehearse19"
docker exec wp19-rehearsal mysql -uroot -prehearse19 neurocorp -e \
  "UPDATE SystemUser SET PasswordHash='<paste PBKDF2$ hash>', PasswordSalt=NULL,
   MustChangePassword=0, FailedLoginAttempts=0, LockoutUntil=NULL
   WHERE Email='anibalvelarde+sa@gmail.com';"
```

**Start the API against the container** (from `patient-care-api`, branch
`feature/wp-22b-patient-merge`):

```bash
DATABASE_HOST=localhost DATABASE_PORT=33061 DATABASE_NAME=neurocorp \
DATABASE_USER=root DATABASE_PASSWORD=rehearse19 ASPNETCORE_ENVIRONMENT=Development \
dotnet run --project src/Web/Web.csproj --urls "http://localhost:52455"
# Health probe: curl http://localhost:52455/api/health/checks  → 200, DbChecks Healthy
```

> **Rehearsal executed 2026-07-11 on this container (BULTRON pair, 359 ← 360):** preview
> classified MATIAS's synthetic `-SH (LEGACY)` caretaker `retire-synthetic`, no blockers;
> execute → log #1, **2 sessions repointed (ExecuteUpdate proof: PID 360 → 0, PID 359 2 → 4,
> total conserved 9,429)**, Patient 360 + SystemUser 10614 + UserRole hard-deleted, MATIAS's
> synthetic caretaker + identity retired while **MATHIAS's own synthetic survived**, audit
> columns stamped `LastUpdatedByUserId=1`, `[MERGED: ...]` marker appended after the
> `[LEGACY-IMPORT: ...]` marker. All §4/§5 checks below passed.

## Setup — mint a SYSADMIN token

```bash
BASE=http://localhost:52455           # rehearsal (above) · local dev: :5245 · deployed: http://neurocorp.k3s:30000
TOKEN=$(curl -s -X POST $BASE/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{"email":"<sysadmin-email>","password":"<password>"}' | jq -r '.accessToken')
# NB: the request field is "email" (NOT "username") and the response field is
# .accessToken (NOT .token).
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
