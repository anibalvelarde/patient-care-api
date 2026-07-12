# WP-23B verification — F6 default pricing · F7 SENADIS floor · F10 caretaker guard

Post-deploy curls (run against the deployed API, e.g. `http://neurocorp.k3s:30000`, after
V026/V027 + the RoleClaim reseed are applied and the API ships this WP). All numbers below are
examples — substitute real IDs from your environment.

```bash
API=http://neurocorp.k3s:30000
```

## 0. Tokens

> ⚠️ The login field is `email`, NOT `username` (binds empty → 401).

```bash
# SYSADMIN (lookup price editing + everything)
SA_TOKEN=$(curl -s $API/api/auth/login -H 'Content-Type: application/json' \
  -d '{"email":"<sysadmin email>","password":"<pwd>"}' | jq -r .accessToken)

# MGR (holds Patients.SenadisDiscount.Edit after the reseed)
MGR_TOKEN=$(curl -s $API/api/auth/login -H 'Content-Type: application/json' \
  -d '{"email":"<mgr email>","password":"<pwd>"}' | jq -r .accessToken)

# FD (does NOT hold the gate claim)
FD_TOKEN=$(curl -s $API/api/auth/login -H 'Content-Type: application/json' \
  -d '{"email":"<fd email>","password":"<pwd>"}' | jq -r .accessToken)
```

## 1. F6 — specialty default price (SYSADMIN-only edit, per-type field)

```bash
# Set a default price on specialty 6 (TL) — expect 204
curl -s -o /dev/null -w '%{http_code}\n' -X PUT $API/api/lookups/specialty-types/6 \
  -H "Authorization: Bearer $SA_TOKEN" -H 'Content-Type: application/json' \
  -d '{"defaultAmount": 65.00}'

# Read it back — expect defaultAmount: 65.00 on TL, null on untouched rows
curl -s $API/api/lookups/specialty-types -H "Authorization: Bearer $SA_TOKEN" \
  | jq '.[] | {id, abbreviation, defaultAmount}'

# Per-type isolation: defaultAmount is null on every other lookup table
curl -s $API/api/lookups/payment-types -H "Authorization: Bearer $SA_TOKEN" \
  | jq '[.[].defaultAmount] | unique'          # expect [null]

# MGR cannot edit lookup prices (Manage claims are SYSADMIN-only) — expect 403
curl -s -o /dev/null -w '%{http_code}\n' -X PUT $API/api/lookups/specialty-types/6 \
  -H "Authorization: Bearer $MGR_TOKEN" -H 'Content-Type: application/json' \
  -d '{"defaultAmount": 1.00}'

# Validation: negative price — expect 400
curl -s -o /dev/null -w '%{http_code}\n' -X PUT $API/api/lookups/specialty-types/6 \
  -H "Authorization: Bearer $SA_TOKEN" -H 'Content-Type: application/json' \
  -d '{"defaultAmount": -5}'
```

(The booking-form pre-fill itself is UI behavior — see the WP-23C test scenarios.)

## 2. F7 — SENADIS flag + edit gate

```bash
# Flag a patient (MGR holds the gate claim) — expect 204
curl -s -o /dev/null -w '%{http_code}\n' -X PUT $API/api/patients/<patientId> \
  -H "Authorization: Bearer $MGR_TOKEN" -H 'Content-Type: application/json' \
  -d '{"hasSenadisDiscount": true}'

# FD tries to CHANGE the flag — expect 403 (field-level gate)
curl -s -o /dev/null -w '%{http_code}\n' -X PUT $API/api/patients/<patientId> \
  -H "Authorization: Bearer $FD_TOKEN" -H 'Content-Type: application/json' \
  -d '{"hasSenadisDiscount": false}'

# FD echoes the UNCHANGED value alongside a phone edit — expect 204 (not a change)
curl -s -o /dev/null -w '%{http_code}\n' -X PUT $API/api/patients/<patientId> \
  -H "Authorization: Bearer $FD_TOKEN" -H 'Content-Type: application/json' \
  -d '{"hasSenadisDiscount": true, "phoneNumber": "555-0199"}'

# Flag visible in the profile
curl -s $API/api/patients/<patientId> -H "Authorization: Bearer $FD_TOKEN" \
  | jq '{patientId, hasSenadisDiscount}'
```

## 3. F7 — SENADIS floor + fee-on-net at booking

```bash
# Book for the FLAGGED patient with a too-small discount; Amount 100, Discount 5.
# Expect 201 with discount FLOORED to 20.00 in the created JSON.
curl -s -X POST $API/api/sessions \
  -H "Authorization: Bearer $MGR_TOKEN" -H 'Content-Type: application/json' \
  -d '{"sessionDate":"<future date>","sessionTime":"10:00:00","patientId":<flagged>,
       "therapistId":<tid>,"specialtyTypeId":6,"duration":60,"amount":100.00,"discount":5.00}' \
  | jq '{sessionId, amount, discount}'         # discount == 20.00

# Fee-on-net (DB check, %-fee therapist): ProviderAmount = pct × (Amount − Discount),
# GrossProfit = net − ProviderAmount. For a 0.50-fee therapist on the row above:
#   ProviderAmount 40.00, GrossProfit 40.00 (pre-WP-23 would have been 50.00 / 30.00).
mysql ... -e "SELECT Amount, DiscountAmount, ProviderAmount, GrossProfit
              FROM TherapySession WHERE TherapySessionID = <sessionId>;"

# Unflagged patient, same request — expect discount to stay 5.00
```

## 4. F10 — caretaker hard block (both create paths)

```bash
# Patient with NO PatientCaretaker links — expect 400 with the guard message
curl -s -X POST $API/api/sessions \
  -H "Authorization: Bearer $MGR_TOKEN" -H 'Content-Type: application/json' \
  -d '{"sessionDate":"<future date>","sessionTime":"11:00:00","patientId":<caretakerless>,
       "therapistId":<tid>,"specialtyTypeId":6,"duration":60,"amount":100.00}' \
  | jq '{status, detail}'   # 400 / "A caretaker must be linked to this patient before booking."

# Bulk path: schedule a plan for the same patient — expect the same 400
curl -s -X POST $API/api/treatmentplans/<planId>/schedule \
  -H "Authorization: Bearer $MGR_TOKEN" -H 'Content-Type: application/json' \
  -d '{"startDate":"<future Monday>","siteId":1}' | jq '{status, detail}'
```

## Suite state

`dotnet test patient-care-api.sln` → **458 green** (Core 152 / Infrastructure 88 / Web 218;
was 429 pre-WP-23B). New coverage: fee-on-net (pct + flat), SENADIS floor
(below/at/above/zero/rounding), unflagged untouched, caretaker guard on both paths,
lookup per-type mapping (read/create/update + non-specialty isolation), controller edit-gate
(Forbid / delegate / wildcard / echo / omit), and end-to-end SensitiveCell rows
(FD+ACCT 403 on flag change; MGR/AM/SYSADMIN pass; FD echo passes).
