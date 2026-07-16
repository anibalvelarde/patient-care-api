# WP-32B (U4) — Idle Auto-Logoff setting: curl verification

Additive, **non-breaking**. Contracts: `patient-care-super/_contracts/sites-api.md`
(`idleLogoffMinutes` on Site GET/POST/PUT, 0–480) + `auth-api.md` (additive
`idleLogoffMinutes` on `/api/auth/me`).

Endpoints touched:

- `GET /api/auth/me` — response gains `idleLogoffMinutes` (int; 0 = disabled). Sourced from the
  first/only `Site` row; `60` if no Site exists. Read by every authenticated operator — no new
  claim, editing stays admin-gated.
- `GET/POST/PUT /api/sites` — Site now carries `idleLogoffMinutes`; PUT/POST clamp **0–480**
  (gate G3), out-of-range → `400`. Still gated on `Admin.Sites.View` / `Admin.Sites.Manage`.

**No matrix change — hash stays `7ae3aa5e3274`.** No reseed, no re-login required (the value
flows from `/auth/me` on the client's next login/refresh).

**Prereq:** DB migration **V029** (`Site.IdleLogoffMinutes INT NOT NULL DEFAULT 60`) applied to
the target DB. Run these after the WP-32A migration is applied and this API build is deployed.

## Setup

```bash
API=http://neurocorp.k3s:30000   # or http://localhost:5245 for a local run
# Login field is `email`, NOT `username` (WP-22 lesson). Use an admin (Admin.Sites.Manage) token.
TOKEN=$(curl -s -X POST $API/api/auth/login \
  -H 'Content-Type: application/json' \
  -d '{"email":"<admin-user>","password":"<pass>"}' | jq -r .accessToken)
AUTH="Authorization: Bearer $TOKEN"

# Discover the (single) site id
SITE=$(curl -s -H "$AUTH" "$API/api/sites" | jq -r '.[0].siteId')
echo "site id = $SITE"
```

## 1) /auth/me exposes the effective value

```bash
# Field present, integer; default 60 on an untouched DB
curl -s -H "$AUTH" "$API/api/auth/me" | jq '{idleLogoffMinutes}'
```

Expect: `{ "idleLogoffMinutes": 60 }` (or whatever the site row currently holds).

## 2) PUT a new value → reflected on both Site GET and /auth/me

```bash
# Set 90 minutes
curl -s -o /dev/null -w 'PUT 90 -> %{http_code}\n' -X PUT "$API/api/sites/$SITE" \
  -H "$AUTH" -H 'Content-Type: application/json' \
  -d '{"idleLogoffMinutes":90}'                       # expect 204

curl -s -H "$AUTH" "$API/api/sites/$SITE" | jq '{idleLogoffMinutes}'   # expect 90
curl -s -H "$AUTH" "$API/api/auth/me"    | jq '{idleLogoffMinutes}'    # expect 90
```

## 3) Range guard (gate G3) — 400 outside 0–480

```bash
curl -s -o /dev/null -w 'PUT -5   -> %{http_code}\n' -X PUT "$API/api/sites/$SITE" \
  -H "$AUTH" -H 'Content-Type: application/json' -d '{"idleLogoffMinutes":-5}'    # expect 400
curl -s -o /dev/null -w 'PUT 9999 -> %{http_code}\n' -X PUT "$API/api/sites/$SITE" \
  -H "$AUTH" -H 'Content-Type: application/json' -d '{"idleLogoffMinutes":9999}'  # expect 400
curl -s -o /dev/null -w 'PUT 481  -> %{http_code}\n' -X PUT "$API/api/sites/$SITE" \
  -H "$AUTH" -H 'Content-Type: application/json' -d '{"idleLogoffMinutes":481}'   # expect 400
```

## 4) Boundaries valid; 0 = disabled passes through

```bash
curl -s -o /dev/null -w 'PUT 0   -> %{http_code}\n' -X PUT "$API/api/sites/$SITE" \
  -H "$AUTH" -H 'Content-Type: application/json' -d '{"idleLogoffMinutes":0}'     # expect 204
curl -s -H "$AUTH" "$API/api/auth/me" | jq '{idleLogoffMinutes}'                  # expect 0

curl -s -o /dev/null -w 'PUT 480 -> %{http_code}\n' -X PUT "$API/api/sites/$SITE" \
  -H "$AUTH" -H 'Content-Type: application/json' -d '{"idleLogoffMinutes":480}'   # expect 204
```

## 5) Omitted on PUT = unchanged

```bash
# Restore the default and confirm a PUT that omits the field leaves it alone
curl -s -o /dev/null -X PUT "$API/api/sites/$SITE" \
  -H "$AUTH" -H 'Content-Type: application/json' -d '{"idleLogoffMinutes":60}'    # back to 60
curl -s -o /dev/null -X PUT "$API/api/sites/$SITE" \
  -H "$AUTH" -H 'Content-Type: application/json' -d '{"siteName":"unchanged-probe"}'
curl -s -H "$AUTH" "$API/api/auth/me" | jq '{idleLogoffMinutes}'                  # still 60
```

## 6) Access control unchanged

```bash
# Site edit still admin-gated: a non-admin token → 403 on PUT; unauth → 401
curl -s -o /dev/null -w 'unauth me    -> %{http_code}\n' "$API/api/auth/me"          # 401
curl -s -o /dev/null -w 'unauth sites -> %{http_code}\n' "$API/api/sites"            # 401
```

## Expected results summary

| Check | Expect |
|---|---|
| `GET /api/auth/me` | body includes `idleLogoffMinutes` (int) |
| `PUT /api/sites/{id}` `{idleLogoffMinutes:90}` | `204`; me + site GET both show `90` |
| `PUT` `-5` / `481` / `9999` | `400` |
| `PUT` `0` / `480` | `204` (0 disables, passes through) |
| `PUT` omitting the field | value unchanged |
| unauth `me` / `sites` | `401` |

> Restore the site to your intended production value (default `60`) after testing.

---

**Unit coverage (runs without a live DB):** `Core.Tests/SiteProfileServiceTests` (map/create-default/
update-set/update-unchanged), `Core.Tests/AuthServiceTests` (me-payload: first-row value, no-row 60,
0 passes through, null user), `Core.Tests/SiteProfileValidationTests` (Range 0–480 on both request
DTOs). Suite **533** green (512 baseline + 21).
