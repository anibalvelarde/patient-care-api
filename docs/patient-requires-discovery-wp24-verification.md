# WP-24B verification — F3/F4 Patient.RequiresDiscovery · Patients.StartDiscovery enforcement (audit F1)

Post-deploy curls (run against the deployed API, e.g. `http://neurocorp.k3s:30000`, after
**V028 + the RoleClaim reseed** are applied and the API ships this WP — hash `7ae3aa5e3274`).
All numbers below are examples — substitute real IDs from your environment.

```bash
API=http://neurocorp.k3s:30000
```

## 0. Tokens

> ⚠️ The login field is `email`, NOT `username` (binds empty → 401).

```bash
# MGR (holds Patients.RequiresDiscovery.Edit + Patients.StartDiscovery after the reseed)
MGR_TOKEN=$(curl -s $API/api/auth/login -H 'Content-Type: application/json' \
  -d '{"email":"<mgr email>","password":"<pwd>"}' | jq -r .accessToken)

# FD (holds Patients.StartDiscovery post-reseed, but NOT the edit-gate claim)
FD_TOKEN=$(curl -s $API/api/auth/login -H 'Content-Type: application/json' \
  -d '{"email":"<fd email>","password":"<pwd>"}' | jq -r .accessToken)
```

## 1. F3 — create omitting the flag → defaults to `requiresDiscovery: true`

```bash
curl -s -w '\n%{http_code}\n' -X POST "$API/api/patients" \
  -H "Authorization: Bearer $FD_TOKEN" -H 'Content-Type: application/json' -d '{
  "firstName":"Wp24","lastName":"DefaultTrue","middleName":"",
  "medicalRecordNumber":"WP24-VERIFY-001","cedula":"",
  "dateOfBirth":"2015-06-01","email":"wp24-default@verify.test",
  "phoneNumber":"555-0100","gender":"Other"}'
# expect: 201 + PatientProfile JSON with "requiresDiscovery": true
```

## 2. F3 — create with the flag explicitly false (any patient-creating role, incl. FD)

```bash
curl -s -w '\n%{http_code}\n' -X POST "$API/api/patients" \
  -H "Authorization: Bearer $FD_TOKEN" -H 'Content-Type: application/json' -d '{
  "firstName":"Wp24","lastName":"Waived","middleName":"",
  "medicalRecordNumber":"WP24-VERIFY-002","cedula":"",
  "dateOfBirth":"2015-06-01","email":"wp24-waived@verify.test",
  "phoneNumber":"555-0100","gender":"Other",
  "requiresDiscovery":false}'
# expect: 201 with "requiresDiscovery": false
# note the returned patientId — used as $WAIVED_ID below; patient 1's id from step 1 = $UNWAIVED_ID
```

## 3. F4 — PUT flag change as FD → 403 (field-level gate, nothing applied)

```bash
curl -s -o /dev/null -w '%{http_code}\n' -X PUT "$API/api/patients/$UNWAIVED_ID" \
  -H "Authorization: Bearer $FD_TOKEN" -H 'Content-Type: application/json' \
  -d '{"requiresDiscovery":false}'
# expect: 403 (FD lacks Patients.RequiresDiscovery.Edit)

# echoed-unchanged still passes for FD (round-tripping clients keep working):
curl -s -o /dev/null -w '%{http_code}\n' -X PUT "$API/api/patients/$UNWAIVED_ID" \
  -H "Authorization: Bearer $FD_TOKEN" -H 'Content-Type: application/json' \
  -d '{"requiresDiscovery":true,"phoneNumber":"555-0199"}'
# expect: 204
```

## 4. F4 — PUT flag change as MGR → 204

```bash
curl -s -o /dev/null -w '%{http_code}\n' -X PUT "$API/api/patients/$UNWAIVED_ID" \
  -H "Authorization: Bearer $MGR_TOKEN" -H 'Content-Type: application/json' \
  -d '{"requiresDiscovery":false}'
# expect: 204; GET the patient back → "requiresDiscovery": false
# (flip it back to true with another MGR PUT before running step 6.)
```

## 5. Audit F1 — POST discovery-specialty session as FD → 201

FD holds `Patients.StartDiscovery` as of `7ae3aa5e3274` — the newly ENFORCED gate lets front
desk book a new patient's first (discovery) session. Use a discovery specialty id (V012 seeds:
abbreviations `Obs-*`/`Eval-*`/`Vis-*` — check `GET /api/lookups/specialty-types` and pick one
with `"isDiscovery": true`). Patient must have a caretaker link (WP-23 F10) — link one first if
needed.

```bash
curl -s -w '\n%{http_code}\n' -X POST "$API/api/sessions" \
  -H "Authorization: Bearer $FD_TOKEN" -H 'Content-Type: application/json' -d '{
  "patientId": '$UNWAIVED_ID', "therapistId": 3,
  "sessionDate": "2026-07-20", "sessionTime": "10:00",
  "duration": 60, "amount": 50.0, "specialtyTypeId": 8}'
# expect: 201 (would be 403 only for a caller WITHOUT Patients.StartDiscovery —
#          post-reseed no seeded operator role lacks it while holding Appointments.Book)
```

## 6. Discovery-first rule still blocks an UNWAIVED patient with no completed discovery

```bash
# $UNWAIVED_ID has requiresDiscovery=true and no COMPLETED discovery session yet
# (step 5 created one but it is not in a completed status) — a treatment specialty must 400:
curl -s -w '\n%{http_code}\n' -X POST "$API/api/sessions" \
  -H "Authorization: Bearer $FD_TOKEN" -H 'Content-Type: application/json' -d '{
  "patientId": '$UNWAIVED_ID', "therapistId": 3,
  "sessionDate": "2026-07-21", "sessionTime": "10:00",
  "duration": 60, "amount": 50.0, "specialtyTypeId": 5}'
# expect: 400 "Patient requires a completed discovery session before treatment sessions
#          can be scheduled."
```

## 7. F3 payoff — the WAIVED patient books treatment directly → 201

```bash
# $WAIVED_ID (requiresDiscovery=false, zero discovery sessions) — same treatment specialty:
curl -s -w '\n%{http_code}\n' -X POST "$API/api/sessions" \
  -H "Authorization: Bearer $FD_TOKEN" -H 'Content-Type: application/json' -d '{
  "patientId": '$WAIVED_ID', "therapistId": 3,
  "sessionDate": "2026-07-21", "sessionTime": "11:00",
  "duration": 60, "amount": 50.0, "specialtyTypeId": 5}'
# expect: 201 — this is the legacy-patient unblock (post-backfill, all L24/L25/L26
#          patients behave like $WAIVED_ID)
```

## Cleanup

```sql
-- Remove verification patients (children first). Find them:
SELECT p.PatientID, p.UserID FROM Patient p JOIN SystemUser su ON su.UserID = p.UserID
WHERE su.Email LIKE 'wp24-%@verify.test';
-- Then: DELETE FROM TherapySession WHERE PatientID IN (...);
--       DELETE FROM PatientCaretaker WHERE PatientID IN (...);
--       DELETE FROM UserRole WHERE UserID IN (...); DELETE FROM Patient WHERE PatientID IN (...);
--       DELETE FROM SystemUser WHERE UserID IN (...);
```
