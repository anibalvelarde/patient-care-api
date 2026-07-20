# WP-36B verification — NP-1 system-managed MRN minting (`NC{yy}-####`)

Post-deploy curls (run against the deployed API, e.g. `http://neurocorp.k3s:30000`, after this
WP ships). All IDs/values below are examples — substitute real ones from your environment.

**No DB migration** (G1 default (a): `MAX+retry`, no sequence table). Contract:
`patient-care-super/_contracts/patients-api.md` (POST `/patients` "System-managed MRN" note —
⚠ behavior change for API clients: a client-supplied `medicalRecordNumber` on create is now
ignored; the UI is the only client).

**NO matrix change — hash `57b6150a350c` unchanged** (minting rides the existing
`Patients.Create` behavior under `Patients.Edit` policy; MRN edit keeps its current claim
surface). No new claim, no RoleClaim reseed, no re-login required.

**Semantics being proven** (gates G1–G6, all defaults 2026-07-18):
- Mint = `NC{yy}-{seq:04d}`; `yy` = **Panama-local** (server-of-record clinic) 2-digit year
  (G6); sequence = `MAX` over the year's `NC{yy}-%` prefix + 1, **resets each calendar year**.
  Prod has 0 `NC%` rows ⇒ the first-ever create mints `NC26-0001`.
- G1a: on a duplicate-key collision of the minted MRN (two stations creating simultaneously),
  the create transaction re-runs ONCE with a re-read sequence; a second collision (or any
  cedula/email duplicate) surfaces as the standard 409.
- G3: client-supplied MRN on create is **ignored** (server logs a warning — old-UI tolerance
  during the deploy gap); the response carries the minted value.
- G5: patients are **active at create** (no more TEMP ⇒ inactive-until-MRN dance).
- G4/B2: MRN on **PUT is unchanged** — editable for legacy corrections, duplicate → 409.
- Straggler `TEMP-` rows (0 on prod at build time) still cannot be activated until corrected
  (guard retained; retires with the G2 backfill).

**Prereqs:** just this API build deployed. (No migration, no reseed, no re-login.)

## 0. Tokens

> ⚠️ The login field is `email`, NOT `username` (binds empty → 401).

```bash
API=http://neurocorp.k3s:30000   # or http://localhost:5245 for a local run
mint() { curl -s -X POST $API/api/auth/login -H 'Content-Type: application/json' \
  -d "{\"email\":\"$1\",\"password\":\"$2\"}" | jq -r .accessToken; }
FD_TOKEN=$(mint '<fd-email>' '<pwd>')     # any patient-creating role works (FD/AM/MGR)
```

## 1. Two rapid creates → sequential `NC26-####`, both active immediately

```bash
# Create #1 — note: NO medicalRecordNumber in the payload (system-minted)
curl -s -X POST $API/api/patients \
  -H "Authorization: Bearer $FD_TOKEN" -H 'Content-Type: application/json' \
  -d '{"firstName":"Mint","lastName":"ProbeOne","cedula":"<unique-1>","dateOfBirth":"2019-03-01",
       "email":"mint1@x.com","phoneNumber":"555","gender":"Male"}' \
  | jq '{patientId, medicalRecordNumber, isActive}'
# expect: medicalRecordNumber "NC26-0001" (first-ever NC mint on prod), isActive true

# Create #2 — immediately after
curl -s -X POST $API/api/patients \
  -H "Authorization: Bearer $FD_TOKEN" -H 'Content-Type: application/json' \
  -d '{"firstName":"Mint","lastName":"ProbeTwo","cedula":"<unique-2>","dateOfBirth":"2019-03-02",
       "email":"mint2@x.com","phoneNumber":"555","gender":"Female"}' \
  | jq '{patientId, medicalRecordNumber, isActive}'
# expect: medicalRecordNumber "NC26-0002" (sequential), isActive true
```

## 2. Create with a supplied MRN → ignored, response shows the minted one

```bash
# Old-UI shape: still posts medicalRecordNumber — the server warns and mints anyway (G3)
curl -s -X POST $API/api/patients \
  -H "Authorization: Bearer $FD_TOKEN" -H 'Content-Type: application/json' \
  -d '{"firstName":"Mint","lastName":"ProbeThree","cedula":"<unique-3>","dateOfBirth":"2019-03-03",
       "email":"mint3@x.com","phoneNumber":"555","gender":"Male",
       "medicalRecordNumber":"HAND-TYPED-123"}' \
  | jq '{patientId, medicalRecordNumber, isActive}'
# expect: medicalRecordNumber "NC26-0003" — NOT "HAND-TYPED-123"; isActive true
# API log carries: "Ignoring client-supplied MRN 'HAND-TYPED-123' on patient create" (Warning)
```

## 3. New patient is active immediately — no activation two-step

```bash
# Confirm via the profile (created active; no TEMP-, no assign-then-activate dance)
curl -s "$API/api/patients/<pid-from-step-1>" -H "Authorization: Bearer $FD_TOKEN" \
  | jq '{medicalRecordNumber, isActive}'          # NC26-0001 / true
```

## 4. Edit-modal duplicate MRN still 409 (G4/B2 — PUT unchanged)

```bash
# Try to set ProbeTwo's MRN to ProbeOne's — the unique key still guards edits
curl -s -o /dev/null -w 'dup MRN edit -> %{http_code}\n' -X PUT $API/api/patients/<pid-2> \
  -H "Authorization: Bearer $FD_TOKEN" -H 'Content-Type: application/json' \
  -d '{"medicalRecordNumber":"NC26-0001"}'                                    # 409

# A legit correction (legacy-style value) still works — MRN stays editable (B2)
curl -s -o /dev/null -w 'legit MRN edit -> %{http_code}\n' -X PUT $API/api/patients/<pid-2> \
  -H "Authorization: Bearer $FD_TOKEN" -H 'Content-Type: application/json' \
  -d '{"medicalRecordNumber":"L26-0918"}'                                     # 204
```

## Expected results summary

| Check | Expect |
|---|---|
| POST create (no MRN in payload) | `201`, `medicalRecordNumber` = next `NC26-####`, `isActive: true` |
| Two rapid creates | strictly sequential `NC26-####` values |
| POST create with a supplied MRN | `201`, supplied value ignored, minted value in response, Warning in API log |
| First-ever mint on prod | `NC26-0001` (0 `NC%` rows at build time) |
| PUT duplicate MRN (edit modal) | `409` "A patient with this Medical Record Number already exists." |
| PUT legit MRN correction | `204` (B2 — unchanged) |
| Matrix | hash `57b6150a350c` unchanged, no reseed, no re-login |

## Suite state

`dotnet test patient-care-api.sln` → **661 green** (Core 266 / Infrastructure 122 / Web 273;
was 646 pre-WP-36B). Regression-test-first: 15 new/updated expectations were written and
watched fail red against the TEMP-flow behavior (mint format + `{n:04d}` padding, Panama-year
prefix, sequence-continues-from-max, first-of-year/empty-table ⇒ 0001, duplicate-race
retry-once + second-collision propagation + non-MRN duplicates never retry, supplied-MRN
ignored + warned, active-at-create, blank-MRN inserts the minted value, UoW tracker cleared on
failure so the retry starts clean, repository MAX scoped to the year prefix / ignores
non-numeric suffixes), then the mint + retry went in to green. The straggler-TEMP activation
guard and the PUT MRN-edit tests were kept as-is and stayed green throughout.
