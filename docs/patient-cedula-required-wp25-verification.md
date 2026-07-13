# WP-25B verification — F5 "Cedula | Passport" required at create; blank-on-update rejected

Post-deploy curls (run against the deployed API, e.g. `http://neurocorp.k3s:30000`, once the API
ships this WP — no DB step, no reseed: the column stays `varchar(50) NULL` for the ~866
legacy-imported NULL-cedula patients). All IDs/values below are examples — substitute real ones.

```bash
API=http://neurocorp.k3s:30000
```

## 0. Token

> ⚠️ The login field is `email`, NOT `username` (binds empty → 401).

```bash
# Any patient-creating operator role works (FD/AM/MGR) — MGR shown.
MGR_TOKEN=$(curl -s $API/api/auth/login -H 'Content-Type: application/json' \
  -d '{"email":"<mgr email>","password":"<pwd>"}' | jq -r .accessToken)
```

## 1. Create WITHOUT cedula → 400

```bash
curl -s -w '\n%{http_code}\n' -X POST "$API/api/patients" \
  -H "Authorization: Bearer $MGR_TOKEN" -H 'Content-Type: application/json' -d '{
  "firstName":"Wp25","lastName":"NoCedula","middleName":"",
  "medicalRecordNumber":"WP25-VERIFY-001",
  "dateOfBirth":"2015-06-01","email":"wp25-nocedula@verify.test",
  "phoneNumber":"555-0100","gender":"Other"}'
# expect: 400 — model validation names the Cedula field; NO patient row is created
```

## 2. Create with BLANK cedula → 400

```bash
# empty string and whitespace-only both fail [Required] — the pre-WP-25 silent NULL insert is gone
curl -s -w '\n%{http_code}\n' -X POST "$API/api/patients" \
  -H "Authorization: Bearer $MGR_TOKEN" -H 'Content-Type: application/json' -d '{
  "firstName":"Wp25","lastName":"BlankCedula","middleName":"",
  "medicalRecordNumber":"WP25-VERIFY-002","cedula":"   ",
  "dateOfBirth":"2015-06-01","email":"wp25-blank@verify.test",
  "phoneNumber":"555-0100","gender":"Other"}'
# expect: 400 mentioning Cedula
```

## 3. Create WITH cedula → 201

```bash
curl -s -w '\n%{http_code}\n' -X POST "$API/api/patients" \
  -H "Authorization: Bearer $MGR_TOKEN" -H 'Content-Type: application/json' -d '{
  "firstName":"Wp25","lastName":"WithCedula","middleName":"",
  "medicalRecordNumber":"WP25-VERIFY-003","cedula":"WP25-8-999-001",
  "dateOfBirth":"2015-06-01","email":"wp25-with@verify.test",
  "phoneNumber":"555-0100","gender":"Other"}'
# expect: 201 + PatientProfile JSON with "cedula": "WP25-8-999-001"
# note the returned patientId — used as $PID below
```

## 4. PUT blank cedula → 400 "cannot be cleared" (erase removed)

```bash
curl -s -w '\n%{http_code}\n' -X PUT "$API/api/patients/$PID" \
  -H "Authorization: Bearer $MGR_TOKEN" -H 'Content-Type: application/json' \
  -d '{"cedula":""}'
# expect: 400 — "Cedula | Passport cannot be cleared - it is required once on file."
#         (WP-25 supersedes the WP-18-followup blank-erase; GET the patient back → value intact)
```

## 5. PUT with cedula OMITTED on a legacy NULL-cedula patient → 204 (tolerant ruling)

```bash
# Pick any legacy-imported patient with cedula NULL — e.g. an L24/L25/L26 MRN patient:
#   GET "$API/api/patients" and find one with "cedula": null; use its id as $LEGACY_ID.
# A quick front-desk fix (phone) that never mentions cedula must still save:
curl -s -o /dev/null -w '%{http_code}\n' -X PUT "$API/api/patients/$LEGACY_ID" \
  -H "Authorization: Bearer $MGR_TOKEN" -H 'Content-Type: application/json' \
  -d '{"phoneNumber":"555-0199"}'
# expect: 204 — omitted/null = unchanged, ALWAYS (legacy patients stay editable;
#         the Active toggle path rides this same tolerance)
```

## 6. Duplicate cedula → 409

```bash
curl -s -w '\n%{http_code}\n' -X POST "$API/api/patients" \
  -H "Authorization: Bearer $MGR_TOKEN" -H 'Content-Type: application/json' -d '{
  "firstName":"Wp25","lastName":"DupCedula","middleName":"",
  "medicalRecordNumber":"WP25-VERIFY-004","cedula":"WP25-8-999-001",
  "dateOfBirth":"2015-06-01","email":"wp25-dup@verify.test",
  "phoneNumber":"555-0100","gender":"Other"}'
# expect: 409 — "A patient with this Cedula already exists." (uq_patient_cedula)
```

## Cleanup

```sql
-- Remove verification patients (children first). Find them:
SELECT p.PatientID, p.UserID FROM Patient p JOIN SystemUser su ON su.UserID = p.UserID
WHERE su.Email LIKE 'wp25-%@verify.test';
-- Then: DELETE FROM PatientCaretaker WHERE PatientID IN (...);
--       DELETE FROM UserRole WHERE UserID IN (...); DELETE FROM Patient WHERE PatientID IN (...);
--       DELETE FROM SystemUser WHERE UserID IN (...);
-- (revert the $LEGACY_ID phone number if it matters)
```
