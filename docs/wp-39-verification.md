# WP-39B (PR-1/2/3) — Per-duration specialty price sheet: curl verification

Additive, **non-breaking** on the read side; NEW endpoints + NEW claim on the write side.
Contracts: `patient-care-super/_contracts/lookups-api.md` (per-type fields `offeredOnSite` +
`durationPrices`, price endpoints, resolution order, `amountSource`) and `sites-api.md`
(`onSiteTripChargeAmount`).

Endpoints touched:

- `GET /api/lookups/specialty-types[/{id}]` — items gain `offeredOnSite` (bool) and
  `durationPrices` = the **current-effective** sheet (per duration ∈ {30,45,60,90,120}, the row
  with the latest `effectiveFrom` ≤ today; durations with no effective row absent). Other lookup
  tables: `offeredOnSite: false`, `durationPrices: []`.
- `GET /api/lookups/specialty-types/{id}/prices` — **NEW**: full history (newest-first per
  duration, `isCurrent` flags). Rides `Admin.Lookups.SpecialtyType.View` (MGR, **AM — new
  grant**, OWN + SYSADMIN).
- `PUT /api/lookups/specialty-types/{id}/prices` — **NEW, append-only**: adds effective-dated
  rows; history is never mutated. Gated by the **NEW claim `Specialties.Prices.Edit`**
  (MGR, AM + SYSADMIN wildcard). 400 invalid duration/negative amount/missing effectiveFrom ·
  404 unknown specialty · **409** duplicate (durationMinutes, effectiveFrom) pair.
- `POST/PUT /api/lookups/specialty-types[/{id}]` — generic lookup write accepts `offeredOnSite`
  (bool; omitted/null = unchanged) under the structural SYSADMIN-only
  `Admin.Lookups.SpecialtyType.Manage`.
- `GET/POST/PUT /api/sites` — Site carries `onSiteTripChargeAmount` (decimal ≥ 0; default 0;
  null/omitted on PUT = unchanged; negative → 400). Rides `Admin.Sites.View`/`Manage`.

**MATRIX CHANGE — hash `7ae3aa5e3274` → `57b6150a350c`** (new claim `Specialties.Prices.Edit`
[MGR, AM]; AM also gains `Admin.View` + `Admin.Lookups.SpecialtyType.View`). Counts: MGR=46,
AM=37, FD=20, OWN=24, ACCT=14.

**Prereqs (in order):**
1. DB migration **V030** applied (SpecialtyDurationPrice + the three new columns).
2. **RoleClaim reseed** (`patient-care-db/scripts/seed-role-claims.sql`, manifest `57b6150a350c`).
3. This API build deployed.
4. **Operators re-login** — claims mint at login; the MGR/AM tokens below must be minted AFTER
   the reseed or the PUT will 403.

## Setup

```bash
API=http://neurocorp.k3s:30000   # or http://localhost:5245 for a local run
# Login field is `email`, NOT `username` (WP-22 lesson).
mint() { curl -s -X POST $API/api/auth/login -H 'Content-Type: application/json' \
  -d "{\"email\":\"$1\",\"password\":\"$2\"}" | jq -r .accessToken; }
MGR_TOKEN=$(mint '<mgr-user>' '<pass>')     # post-reseed login!
FD_TOKEN=$(mint '<fd-user>' '<pass>')
AM_TOKEN=$(mint '<am-user>' '<pass>')
ADMIN_TOKEN=$(mint '<sysadmin-user>' '<pass>')

# Pick a specialty id to exercise
SPEC=$(curl -s -H "Authorization: Bearer $MGR_TOKEN" \
  "$API/api/lookups/specialty-types" | jq -r '.[0].id')
echo "specialty id = $SPEC"
```

## 1) Lookup projection carries the new fields

```bash
curl -s -H "Authorization: Bearer $FD_TOKEN" "$API/api/lookups/specialty-types" \
  | jq '.[0] | {id, name, defaultAmount, offeredOnSite, durationPrices}'
# expect: offeredOnSite present (bool), durationPrices [] on an unseeded sheet

# Other lookup tables: flag false, sheet empty
curl -s -H "Authorization: Bearer $FD_TOKEN" "$API/api/lookups/payment-types" \
  | jq '.[0] | {offeredOnSite, durationPrices}'      # expect false / []
```

## 2) PUT prices as MGR → 204 (the PR-3 point: no structural-manage needed)

```bash
curl -s -o /dev/null -w 'MGR PUT -> %{http_code}\n' -X PUT \
  "$API/api/lookups/specialty-types/$SPEC/prices" \
  -H "Authorization: Bearer $MGR_TOKEN" -H 'Content-Type: application/json' \
  -d '{"prices":[
        {"durationMinutes":30,"amount":25.00,"effectiveFrom":"2026-07-01"},
        {"durationMinutes":60,"amount":45.00,"effectiveFrom":"2026-07-01"},
        {"durationMinutes":60,"amount":50.00,"effectiveFrom":"2026-09-01"}
      ]}'                                             # expect 204 (third row is future-dated)
# Repeat with AM_TOKEN and different dates → also 204.
```

## 3) GET history — isCurrent flags, newest-first per duration

```bash
curl -s -H "Authorization: Bearer $AM_TOKEN" \
  "$API/api/lookups/specialty-types/$SPEC/prices" | jq
# expect: {specialtyTypeId, defaultAmount, offeredOnSite, prices:[...]} — the 2026-09-01 row
# has isCurrent:false (future); the 2026-07-01 rows are isCurrent:true.
# NB: the AM read itself proves the new Admin.Lookups.SpecialtyType.View grant (pre-reseed AM was 403).

# Projection now shows only the CURRENT-EFFECTIVE set (future 60-min row excluded):
curl -s -H "Authorization: Bearer $FD_TOKEN" "$API/api/lookups/specialty-types/$SPEC" \
  | jq '.durationPrices'    # expect the two 2026-07-01 rows only
```

## 4) Validation → 400; unknown id → 404; duplicate → 409

```bash
put() { curl -s -o /dev/null -w "$1 -> %{http_code}\n" -X PUT \
  "$API/api/lookups/specialty-types/$2/prices" \
  -H "Authorization: Bearer $MGR_TOKEN" -H 'Content-Type: application/json' -d "$3"; }

put 'invalid duration 75 ' $SPEC '{"prices":[{"durationMinutes":75,"amount":45,"effectiveFrom":"2026-08-01"}]}'   # 400
put 'negative amount     ' $SPEC '{"prices":[{"durationMinutes":60,"amount":-5,"effectiveFrom":"2026-08-01"}]}'   # 400
put 'missing effectiveFrom' $SPEC '{"prices":[{"durationMinutes":60,"amount":45}]}'                               # 400
put 'empty prices list   ' $SPEC '{"prices":[]}'                                                                  # 400
put 'unknown specialty   ' 99999 '{"prices":[{"durationMinutes":60,"amount":45,"effectiveFrom":"2026-08-01"}]}'   # 404
put 'duplicate row on file' $SPEC '{"prices":[{"durationMinutes":60,"amount":47,"effectiveFrom":"2026-07-01"}]}'  # 409
put 'in-request duplicate ' $SPEC '{"prices":[{"durationMinutes":90,"amount":80,"effectiveFrom":"2026-08-01"},
                                              {"durationMinutes":90,"amount":85,"effectiveFrom":"2026-08-01"}]}'  # 409
```

## 5) Access control — FD 403 on both price endpoints; structural manage untouched

```bash
curl -s -o /dev/null -w 'FD GET prices -> %{http_code}\n' \
  -H "Authorization: Bearer $FD_TOKEN" "$API/api/lookups/specialty-types/$SPEC/prices"   # 403
curl -s -o /dev/null -w 'FD PUT prices -> %{http_code}\n' -X PUT \
  -H "Authorization: Bearer $FD_TOKEN" -H 'Content-Type: application/json' \
  "$API/api/lookups/specialty-types/$SPEC/prices" \
  -d '{"prices":[{"durationMinutes":60,"amount":45,"effectiveFrom":"2026-08-01"}]}'      # 403

# MGR still cannot do STRUCTURAL lookup edits (rename etc. stays SYSADMIN-only):
curl -s -o /dev/null -w 'MGR structural PUT -> %{http_code}\n' -X PUT \
  -H "Authorization: Bearer $MGR_TOKEN" -H 'Content-Type: application/json' \
  "$API/api/lookups/specialty-types/$SPEC" -d '{"description":"probe"}'                  # 403
```

## 6) offeredOnSite via the structural manage claim (SYSADMIN)

```bash
curl -s -o /dev/null -w 'flag on -> %{http_code}\n' -X PUT \
  -H "Authorization: Bearer $ADMIN_TOKEN" -H 'Content-Type: application/json' \
  "$API/api/lookups/specialty-types/$SPEC" -d '{"offeredOnSite":true}'                   # 204
curl -s -H "Authorization: Bearer $FD_TOKEN" "$API/api/lookups/specialty-types/$SPEC" \
  | jq '.offeredOnSite'                                                                  # true
# Omitting the field leaves it unchanged:
curl -s -o /dev/null -X PUT -H "Authorization: Bearer $ADMIN_TOKEN" \
  -H 'Content-Type: application/json' \
  "$API/api/lookups/specialty-types/$SPEC" -d '{"sortOrder":1}'
curl -s -H "Authorization: Bearer $FD_TOKEN" "$API/api/lookups/specialty-types/$SPEC" \
  | jq '.offeredOnSite'                                                                  # still true
```

## 7) Site trip charge (rides Admin.Sites.Manage, same as idleLogoffMinutes)

```bash
SITE=$(curl -s -H "Authorization: Bearer $ADMIN_TOKEN" "$API/api/sites" | jq -r '.[0].siteId')
curl -s -o /dev/null -w 'PUT 15.50 -> %{http_code}\n' -X PUT "$API/api/sites/$SITE" \
  -H "Authorization: Bearer $ADMIN_TOKEN" -H 'Content-Type: application/json' \
  -d '{"onSiteTripChargeAmount":15.50}'                                                  # 204
curl -s -H "Authorization: Bearer $ADMIN_TOKEN" "$API/api/sites/$SITE" \
  | jq '{onSiteTripChargeAmount}'                                                        # 15.50
curl -s -o /dev/null -w 'PUT -1 -> %{http_code}\n' -X PUT "$API/api/sites/$SITE" \
  -H "Authorization: Bearer $ADMIN_TOKEN" -H 'Content-Type: application/json' \
  -d '{"onSiteTripChargeAmount":-1}'                                                     # 400
curl -s -o /dev/null -X PUT "$API/api/sites/$SITE" -H "Authorization: Bearer $ADMIN_TOKEN" \
  -H 'Content-Type: application/json' -d '{"siteName":"unchanged-probe"}'
curl -s -H "Authorization: Bearer $ADMIN_TOKEN" "$API/api/sites/$SITE" \
  | jq '{onSiteTripChargeAmount}'                                                        # still 15.50
```

> Clean up any probe rows before handing the sheet to the customer: price history is
> append-only by design — remove rehearsal rows via SQL if needed
> (`DELETE FROM SpecialtyDurationPrice WHERE ...`), or use a throwaway specialty.

## Expected results summary

| Check | Expect |
|---|---|
| lookup items (specialty-types) | `offeredOnSite` + `durationPrices` (current-effective only) |
| lookup items (other tables) | `offeredOnSite: false`, `durationPrices: []` |
| `PUT …/prices` MGR / AM (post-reseed) | `204` |
| `PUT …/prices` FD / OWN / ACCT | `403` |
| `GET …/prices` MGR / AM / OWN | `200` with `isCurrent` flags, newest-first per duration |
| `GET …/prices` FD / ACCT | `403` |
| invalid duration / negative amount / missing date / empty list | `400` |
| unknown specialty id | `404` |
| duplicate (duration, effectiveFrom) — on file or in-request | `409` |
| MGR structural lookup PUT | `403` (unchanged — SYSADMIN only) |
| Site PUT `onSiteTripChargeAmount` ≥ 0 / negative / omitted | `204` / `400` / unchanged |

---

**Unit coverage (runs without a live DB):** `Core.Tests/SpecialtyPriceServiceTests` (history
shape + isCurrent, append happy/404/409 both dup kinds, **ResolvePrice temporal cases** —
latest-≤-date wins, boundary date counts, future rows ignored, DefaultAmount fallback, none,
retroactive row does not affect earlier session dates), `SpecialtyPriceValidationTests`
(durations {30,45,60,90,120}, amount ≥ 0, effectiveFrom required, non-empty list),
`LookupServiceTests` (projection current-effective + future-excluded + single repo call,
offeredOnSite create/update/ignored-elsewhere), `SiteProfileServiceTests` +
`SiteProfileValidationTests` (trip-charge map/default/set/unchanged/negative),
`Infrastructure.Tests/SpecialtyPriceRepositoryTests` (single-call Include hydration,
append-only + audit stamp via mocked ICurrentUserService),
`Web.Tests/SpecialtyPricesControllerTests` (200/404/204/409 mapping) and
`SensitiveCellAuthorizationTests` (403/allowed/401 cells for both new endpoints).
Suite **621** green (547 baseline + 74).
