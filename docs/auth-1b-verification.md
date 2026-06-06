# Auth Verification — Remediation 2026, Chunk 1B (API Authentication)

This document contains copy-paste `curl` commands to verify the authentication and
claims-aware authorization foundation delivered in Chunk 1B. Keep it for re-testing after
future auth changes.

> Commands use `curl.exe` so they work in both PowerShell and bash. `jq` is optional (only
> used to pretty-print / extract the token). Replace values in `<angle brackets>`.

## Prerequisites

1. **Database** is up and migration **V017** has been applied (auth columns + RoleClaim/UserClaim).
2. **SA bootstrap** has been run: `patient-care-db/scripts/bootstrap-sa.sql`
   (see `patient-care-db/docs/runbooks/bootstrap-sa.md`). You know the SA's temporary password.
3. **API is running**:
   ```bash
   dotnet run --project src/Web/Web.csproj    # http://localhost:5245
   ```
   For local runs without a configured key, a development-only JWT signing key is used
   automatically. In any non-development environment set `JWT_SIGNING_KEY` first.

```bash
# Base URL used throughout
BASE=http://localhost:5245
```

---

## 1. Health endpoints are public (no token) → 200

```bash
curl.exe -i $BASE/api/health/live
```
**Expect:** `200 OK`, body `Alive`. (Health is marked `[AllowAnonymous]`.)

## 2. A protected endpoint with NO token → 401 + ProblemDetails

```bash
curl.exe -i $BASE/api/patients
```
**Expect:** `401 Unauthorized`, `Content-Type: application/problem+json`, body like:
```json
{"type":"https://httpstatuses.io/401","title":"Unauthorized","status":401,"detail":"Authentication is required to access this resource."}
```

## 3. SA login → access + refresh tokens

```bash
curl.exe -i -X POST $BASE/api/auth/login \
  -H "Content-Type: application/json" \
  -d "{\"email\":\"anibalvelarde+sa@gmail.com\",\"password\":\"<SA_TEMP_PASSWORD>\"}"
```
**Expect:** `200 OK` with:
```json
{"accessToken":"eyJ...","refreshToken":"eyJ...","tokenType":"Bearer","expiresIn":3600,"mustChangePassword":true}
```

Capture the access token into a variable:
```bash
SA_TOKEN=$(curl.exe -s -X POST $BASE/api/auth/login \
  -H "Content-Type: application/json" \
  -d "{\"email\":\"anibalvelarde+sa@gmail.com\",\"password\":\"<SA_TEMP_PASSWORD>\"}" \
  | jq -r .accessToken)
echo "$SA_TOKEN"
```

## 4. Confirm the token carries ('System','FullAccess')

Decode the JWT payload (middle segment). With `jq`:
```bash
echo "$SA_TOKEN" | cut -d. -f2 | tr '_-' '/+' | base64 -d 2>/dev/null | jq .
```
**Expect:** a `"System": "FullAccess"` claim (plus `uid`, `name`, `role`, etc.).
You can also paste the token into https://jwt.io to inspect it.

## 5. SA reaches the convenience identity endpoint

```bash
curl.exe -s $BASE/api/auth/me -H "Authorization: Bearer $SA_TOKEN" | jq .
```
**Expect:** `200 OK`, `"isSystemAdmin": true`, roles include `SystemAdmin`, and the claims
array contains `{"type":"System","value":"FullAccess"}`.

## 6. SA reaches protected endpoints across multiple controllers → 200

```bash
curl.exe -i $BASE/api/patients -H "Authorization: Bearer $SA_TOKEN"
curl.exe -i $BASE/api/sites    -H "Authorization: Bearer $SA_TOKEN"
curl.exe -i $BASE/api/therapists -H "Authorization: Bearer $SA_TOKEN"
```
**Expect:** `200 OK` for each (the wildcard satisfies the authenticated-user fallback policy).

## 7. SA passes the claims-gated endpoint → 200

```bash
curl.exe -i $BASE/api/auth/admin-check -H "Authorization: Bearer $SA_TOKEN"
```
**Expect:** `200 OK`, message confirming System.FullAccess access. This endpoint requires the
`System.Diagnostics` permission, which no role is granted by default — only the SA wildcard passes.

## 8. A non-SA authenticated user is correctly denied the claims-gated endpoint → 403

> Requires a second, non-SA user with a known password. Create one with the helper hash and a
> direct insert, or reuse an existing staff account once its password is set. Example setup:
> ```bash
> # 1) hash a password
> dotnet run --project tools/SaPasswordHasher -- "<StaffPassword>"
> # 2) set it on an existing active SystemUser (no SYSADMIN role), e.g. via mysql:
> #    UPDATE SystemUser SET PasswordHash='PBKDF2$...', MustChangePassword=0 WHERE Email='<staff email>';
> ```

```bash
STAFF_TOKEN=$(curl.exe -s -X POST $BASE/api/auth/login \
  -H "Content-Type: application/json" \
  -d "{\"email\":\"<STAFF_EMAIL>\",\"password\":\"<StaffPassword>\"}" | jq -r .accessToken)

# Authenticated, so a normal protected endpoint works:
curl.exe -i $BASE/api/patients -H "Authorization: Bearer $STAFF_TOKEN"      # → 200

# But the claims-gated endpoint is forbidden:
curl.exe -i $BASE/api/auth/admin-check -H "Authorization: Bearer $STAFF_TOKEN"   # → 403 problem+json
```
**Expect:** `200` for `/api/patients`, `403 Forbidden` (`application/problem+json`) for
`/api/auth/admin-check`.

## 9. Invalid credentials → 401 (and no user enumeration)

```bash
curl.exe -i -X POST $BASE/api/auth/login \
  -H "Content-Type: application/json" \
  -d "{\"email\":\"anibalvelarde+sa@gmail.com\",\"password\":\"wrong\"}"
```
**Expect:** `401`, generic detail `Invalid email or password.` (same message for unknown emails).

## 10. Refresh the access token

```bash
REFRESH=$(curl.exe -s -X POST $BASE/api/auth/login \
  -H "Content-Type: application/json" \
  -d "{\"email\":\"anibalvelarde+sa@gmail.com\",\"password\":\"<SA_TEMP_PASSWORD>\"}" | jq -r .refreshToken)

curl.exe -s -X POST $BASE/api/auth/refresh \
  -H "Content-Type: application/json" \
  -d "{\"refreshToken\":\"$REFRESH\"}" | jq .
```
**Expect:** `200 OK` with a new `accessToken` / `refreshToken` pair. An access token used here is
rejected (`401`) because it lacks the `typ=refresh` marker.

## 11. Change password (clears must-change + rotates tokens)

```bash
curl.exe -i -X POST $BASE/api/auth/change-password \
  -H "Authorization: Bearer $SA_TOKEN" \
  -H "Content-Type: application/json" \
  -d "{\"currentPassword\":\"<SA_TEMP_PASSWORD>\",\"newPassword\":\"<NewStrongPassword>\"}"
```
**Expect:** `200 OK` with a fresh token pair and `mustChangePassword: false`. Subsequent logins
must use the new password.

## 12. Evidence that `LastUpdatedByUserId` is now the real user

After authenticating as the SA, perform any write (e.g. update a patient) and inspect the row:

```bash
# Example write as the SA:
curl.exe -i -X PUT $BASE/api/patients/<id> \
  -H "Authorization: Bearer $SA_TOKEN" \
  -H "Content-Type: application/json" \
  -d "{ ...patient update body... }"
```
Then in the DB:
```sql
SELECT PatientID, LastUpdatedByUserId, LastUpdatedTimestamp
FROM Patient WHERE PatientID = <id>;
```
**Expect:** `LastUpdatedByUserId` equals the SA's `UserID` (previously always `0`).

## 13. Must-change-password is enforced server-side (not advisory)

Per the locked design (`remediation-2026-system-admin-bootstrap.md`): *"the first login must force a
password change before any other actions are allowed (this is an API + UI requirement)."* This
proves the API — not just the UI — restricts a must-change session to the password-change flow.

Pick a non-SA test user and set the flag directly in the DB:
```sql
UPDATE SystemUser SET MustChangePassword = 1 WHERE Email = '<test email>';
```

Log in and capture the token (note `"mustChangePassword": true` in the response):
```bash
MC_TOKEN=$(curl.exe -s -X POST $BASE/api/auth/login \
  -H "Content-Type: application/json" \
  -d "{\"email\":\"<test email>\",\"password\":\"<password>\"}" | jq -r .accessToken)
```

A normal protected endpoint is now BLOCKED:
```bash
curl.exe -i $BASE/api/patients -H "Authorization: Bearer $MC_TOKEN"
```
**Expect:** `403 Forbidden`, `application/problem+json`, body containing
`"code":"password_change_required"` and title `Password change required`.

But the allowlisted endpoints still work, so the user can resolve it:
```bash
curl.exe -i $BASE/api/auth/me            -H "Authorization: Bearer $MC_TOKEN"   # → 200
curl.exe -i -X POST $BASE/api/auth/change-password \
  -H "Authorization: Bearer $MC_TOKEN" -H "Content-Type: application/json" \
  -d "{\"currentPassword\":\"<password>\",\"newPassword\":\"<NewStrongPassword>\"}"   # → 200, new tokens
```
**Expect:** `/api/auth/me` → `200`; `change-password` → `200` with a fresh token pair and
`mustChangePassword: false`. The new access token has the marker dropped, so a protected call now
succeeds:
```bash
NEW_TOKEN=$(curl.exe -s -X POST $BASE/api/auth/login \
  -H "Content-Type: application/json" \
  -d "{\"email\":\"<test email>\",\"password\":\"<NewStrongPassword>\"}" | jq -r .accessToken)
curl.exe -i $BASE/api/patients -H "Authorization: Bearer $NEW_TOKEN"   # → 200
```
**Expect:** `200 OK` — the gate clears automatically once the password is changed.

---

## Summary of expected results

| # | Scenario | Expected |
|---|----------|----------|
| 1 | Health, no token | 200 |
| 2 | Protected endpoint, no token | 401 + problem+json |
| 3 | SA login | 200 + tokens |
| 4 | Token claims | contains `System=FullAccess` |
| 5 | `/api/auth/me` as SA | 200, `isSystemAdmin: true` |
| 6 | Protected endpoints as SA | 200 |
| 7 | `/api/auth/admin-check` as SA | 200 |
| 8 | `/api/auth/admin-check` as non-SA | 403; business endpoint 200 |
| 9 | Bad password | 401, generic message |
| 10 | Refresh | 200 + new tokens |
| 11 | Change password | 200, `mustChangePassword: false` |
| 12 | Audit stamping | `LastUpdatedByUserId` = real user id |
| 13 | Must-change-password enforced | protected → 403 `password_change_required`; `me`/`change-password` → 200; cleared after change |
