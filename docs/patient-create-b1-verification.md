# B1 — Patient Create Fix: Verification (curl)

Fix for intake 2026-07-07-001 B1: Gender validated → 400 (allowed: `Male`/`Female`/`Other`,
DB enum widened by V024), transactional create (no more orphaned SystemUsers), blank MRN
inserted as NULL before the `TEMP-{id}` stamp, and a specific 409 message for duplicate MRNs.

Prereqs: API running (`http://localhost:5245`), `$TOKEN` = a token holding `Patients.Edit`
(MGR). **V024 must be applied to the target DB first.**

```bash
API=http://localhost:5245
AUTH="Authorization: Bearer $TOKEN"
JSON="Content-Type: application/json"
```

## 1. Gender "Other" now creates (the B1 trigger — was an opaque 500)

```bash
curl -s -w '\n%{http_code}\n' -X POST "$API/api/patients" -H "$AUTH" -H "$JSON" -d '{
  "firstName":"B1","lastName":"VerifyOther","middleName":"",
  "medicalRecordNumber":"B1-VERIFY-001","cedula":"",
  "dateOfBirth":"2015-06-01","email":"b1-other@verify.test",
  "phoneNumber":"555-0100","gender":"Other"}'
# expect: 201 + PatientProfile JSON
```

## 2. Invalid Gender → 400 (was 500)

```bash
curl -s -w '\n%{http_code}\n' -X POST "$API/api/patients" -H "$AUTH" -H "$JSON" -d '{
  "firstName":"B1","lastName":"VerifyBad","middleName":"",
  "medicalRecordNumber":"B1-VERIFY-002","cedula":"",
  "dateOfBirth":"2015-06-01","email":"b1-bad@verify.test",
  "phoneNumber":"555-0100","gender":"Nonbinary"}'
# expect: 400 ValidationProblemDetails naming the Gender field
```

## 3. Atomic create — failed create leaves NO orphaned SystemUser

```bash
# Duplicate MRN forces the Patient insert to fail after the SystemUser write:
curl -s -w '\n%{http_code}\n' -X POST "$API/api/patients" -H "$AUTH" -H "$JSON" -d '{
  "firstName":"B1","lastName":"VerifyOrphan","middleName":"",
  "medicalRecordNumber":"B1-VERIFY-001","cedula":"",
  "dateOfBirth":"2015-06-01","email":"b1-orphan@verify.test",
  "phoneNumber":"555-0100","gender":"Male"}'
# expect: 409 with detail "A patient with this Medical Record Number already exists."
#         (specific message is also new — was the generic duplicate text)
```

Then against the DB — the rolled-back SystemUser must not exist:

```sql
SELECT * FROM SystemUser WHERE Email = 'b1-orphan@verify.test';  -- expect 0 rows
```

## 4. Blank MRN → NULL insert + TEMP stamp

```bash
curl -s -w '\n%{http_code}\n' -X POST "$API/api/patients" -H "$AUTH" -H "$JSON" -d '{
  "firstName":"B1","lastName":"VerifyTemp","middleName":"",
  "medicalRecordNumber":"","cedula":"",
  "dateOfBirth":"2015-06-01","email":"b1-temp@verify.test",
  "phoneNumber":"555-0100","gender":"Female"}'
# expect: 201, medicalRecordNumber = "TEMP-{patientId}", isActive = false
```

## 5. PUT with invalid Gender → 400; empty gender = unchanged

```bash
curl -s -w '\n%{http_code}\n' -X PUT "$API/api/patients/1" -H "$AUTH" -H "$JSON" \
  -d '{"gender":"X"}'
# expect: 400

curl -s -w '\n%{http_code}\n' -X PUT "$API/api/patients/1" -H "$AUTH" -H "$JSON" \
  -d '{"firstName":"Updated","gender":""}'
# expect: 204 (empty gender leaves the stored value unchanged)
```

## Cleanup

```sql
-- Remove the verification patients (children first). Find them:
SELECT p.PatientID, p.UserID FROM Patient p JOIN SystemUser su ON su.UserID = p.UserID
WHERE su.Email LIKE 'b1-%@verify.test';
-- Then: DELETE FROM UserRole WHERE UserID IN (...); DELETE FROM Patient WHERE PatientID IN (...);
--       DELETE FROM SystemUser WHERE UserID IN (...);
```
