# WP-41B Verification — Admin User Management API (SA-only v1)

Curl test commands for `/api/admin/users` (contract: `patient-care-super/_contracts/admin-users-api.md`).
Run against a deployed API (`http://neurocorp.k3s:30000`) or local (`http://localhost:5245`).

**Prereqs:**
- NO DB migration, NO claim/matrix change in this WP (hash `f82cab8c9efd` unchanged).
  `Admin.Users.View` (MGR + OWN) and `Admin.Users.Manage` (empty grants ⇒ SYSADMIN wildcard
  only) have been seeded since WP-17 — no reseed, no re-login needed.
- Role ids below (`<mgrRoleId>` etc.) come from `GET /api/lookups/role-types` or the RoleType
  table. Identity roles = Patient / Therapist / Caretaker; everything else is an operator role.

```bash
API=http://neurocorp.k3s:30000
# Login (field is "email", not "username")
SA_TOKEN=$(curl -s -X POST $API/api/auth/login -H 'Content-Type: application/json' \
  -d '{"email":"<sysadmin-email>","password":"<pwd>"}' | jq -r .accessToken)
SA="Authorization: Bearer $SA_TOKEN"
# Also mint $MGR (Manager) and $FD (FrontDesk) the same way for the gating tests.
```

## 1. Claim gating (View = MGR/OWN; Manage = SYSADMIN-only)

```bash
# Reads: MGR/OWN/SYSADMIN → 200; FD/AM/ACCT → 403; anonymous → 401.
curl -s -o /dev/null -w '%{http_code}\n' $API/api/admin/users -H "$MGR"   # → 200
curl -s -o /dev/null -w '%{http_code}\n' $API/api/admin/users -H "$FD"    # → 403
curl -s -o /dev/null -w '%{http_code}\n' $API/api/admin/users             # → 401

# Writes: even MGR (who can View) is denied; only SYSADMIN passes.
curl -s -o /dev/null -w '%{http_code}\n' -X POST $API/api/admin/users -H "$MGR" \
  -H 'Content-Type: application/json' -d '{}'                              # → 403
curl -s -o /dev/null -w '%{http_code}\n' -X PUT $API/api/admin/users/1 -H "$MGR" \
  -H 'Content-Type: application/json' -d '{}'                              # → 403
```

## 2. Operators-only paged list + search (WP-21 envelope / WP-30 rule)

```bash
# Envelope { items, page, pageSize, totalCount }; default pageSize 25, cap 100.
curl -s "$API/api/admin/users" -H "$SA" | jq '{page, pageSize, totalCount, n: (.items|length)}'

# Identity-only users (the ~866 patients/caretakers) are INVISIBLE: totalCount stays small
# (operator accounts only). Mixed-role users appear WITH identityRoles hints:
curl -s "$API/api/admin/users?search=<mixed-role-name>" -H "$SA" | jq '.items[0]'
# → { userId, firstName, lastName, email, isActive, mustChangePassword,
#     operatorRoles: [ { roleTypeId, name } ], identityRoles: [ "Patient" ] }

# Search is case-insensitive contains on first/last name and email:
curl -s "$API/api/admin/users?search=ADMIN" -H "$SA" | jq '.totalCount'
# pageSize is silently clamped to 100:
curl -s "$API/api/admin/users?pageSize=100000" -H "$SA" | jq '.pageSize'   # → 100
```

## 3. GET by id — 404 outside the operator surface

```bash
curl -s $API/api/admin/users/<operatorUserId> -H "$SA" | jq .              # → 200 summary
# Identity-only user (a patient's SystemUser id) → 404 (out of v1 scope, G1):
curl -s -o /dev/null -w '%{http_code}\n' $API/api/admin/users/<patientUserId> -H "$SA"  # → 404
```

## 4. Create operator (active + must-change, G3)

```bash
curl -s -X POST $API/api/admin/users -H "$SA" -H 'Content-Type: application/json' -d '{
  "firstName":"Dana","lastName":"Ops","email":"dana.ops@neurocorp.test",
  "tempPassword":"Temp1234!","operatorRoleTypeIds":[<mgrRoleId>]}' | jq .
# → 201; isActive true, mustChangePassword true, operatorRoles [Manager].
# Login as dana.ops → token carries mcp; all endpoints except change-password are blocked
# until the password is changed (existing PasswordChangeRequiredMiddleware flow).

# Identity role id in the list → 400:
#   "Identity roles (Patient, Therapist, Caretaker) cannot be assigned or removed here; ..."
curl -s -o /dev/null -w '%{http_code}\n' -X POST $API/api/admin/users -H "$SA" \
  -H 'Content-Type: application/json' -d '{"firstName":"X","lastName":"Y","email":"x@y.z",
  "tempPassword":"Temp1234!","operatorRoleTypeIds":[<patientRoleId>]}'      # → 400

# Duplicate email → 409 "A user with this email address already exists."
# Temp password < 8 chars → 400 (same policy as ChangePassword).
```

## 5. PUT — replace role set / active status (omitted = unchanged)

```bash
# Replace the operator-role set (identity links untouched):
curl -s -o /dev/null -w '%{http_code}\n' -X PUT $API/api/admin/users/<id> -H "$SA" \
  -H 'Content-Type: application/json' -d '{"operatorRoleTypeIds":[<ownRoleId>]}'   # → 204

# Deactivate only (roles unchanged):
curl -s -o /dev/null -w '%{http_code}\n' -X PUT $API/api/admin/users/<id> -H "$SA" \
  -H 'Content-Type: application/json' -d '{"isActive":false}'                       # → 204

# Empty role list → 400 (deactivate instead of stripping):
curl -s -X PUT $API/api/admin/users/<id> -H "$SA" -H 'Content-Type: application/json' \
  -d '{"operatorRoleTypeIds":[]}' | jq .detail                                      # → 400
```

## 6. G5 guard rails (hard 400s)

```bash
# Self-deactivate (caller's own id) → 400 "You cannot deactivate your own account."
curl -s -X PUT $API/api/admin/users/<callerUserId> -H "$SA" \
  -H 'Content-Type: application/json' -d '{"isActive":false}' | jq .detail

# Self-demote → 400 "You cannot remove the SystemAdmin role from your own account."
curl -s -X PUT $API/api/admin/users/<callerUserId> -H "$SA" \
  -H 'Content-Type: application/json' -d '{"operatorRoleTypeIds":[<mgrRoleId>]}' | jq .detail

# Last-SYSADMIN protection (target = the ONLY active SYSADMIN, caller ≠ target):
# both the deactivate AND the role-removal paths → 400
#   "This change would leave the system with no active SystemAdmin account."
# With a second ACTIVE SYSADMIN present, the same calls → 204.
```

## 7. Reset password (existing must-change flow)

```bash
curl -s -o /dev/null -w '%{http_code}\n' -X POST $API/api/admin/users/<id>/reset-password \
  -H "$SA" -H 'Content-Type: application/json' -d '{"tempPassword":"NewTemp99"}'    # → 204
# Then: login with NewTemp99 → mustChangePassword true → forced through
# POST /api/auth/change-password (clears the flag, reissues tokens). Lockout counters reset.

curl -s -o /dev/null -w '%{http_code}\n' -X POST $API/api/admin/users/<id>/reset-password \
  -H "$SA" -H 'Content-Type: application/json' -d '{"tempPassword":"short"}'        # → 400
```
