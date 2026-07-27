# WP-40B Verification — Booking Auto-Money (BK-1/BK-2/BK-3)

Curl test commands for the WP-40 booking money derivation. Run against a deployed API
(`http://neurocorp.k3s:30000`) or local (`http://localhost:5245`).

**Prereqs:**
- Matrix hash `f82cab8c9efd` seeded (`patient-care-db/scripts/seed-role-claims.sql`) and the
  operator **re-logged-in** (claims mint at login).
- WP-39 price sheet loaded (26 types / 27 duration rows — prod-verified 2026-07-27).

```bash
API=http://neurocorp.k3s:30000
# Login (field is "email", not "username")
TOKEN=$(curl -s -X POST $API/api/auth/login -H 'Content-Type: application/json' \
  -d '{"email":"<operator-email>","password":"<pwd>"}' | jq -r .token)
AUTH="Authorization: Bearer $TOKEN"
```

## 1. BK-2 — booking derives all money (client sends none)

```bash
# Book touching zero money fields — expect 201 with derived amount/discount/providerAmount,
# amountSource ("durationPrice"), and correct amountDue.
curl -s -X POST $API/api/sessions -H "$AUTH" -H 'Content-Type: application/json' -d '{
  "sessionDate":"2026-08-03","sessionTime":"10:00:00","patientId":<PID>,"therapistId":<TID>,
  "duration":60,"specialtyTypeId":<SID>,"appointmentStatusId":1}' | jq \
  '{amount, discount, providerAmount, amountSource, onSiteChargeAmount, amountDue}'
```

## 2. Junk client money is provably ignored

```bash
# Same booking but with absurd client money — the response must show the SAME derived values
# as test 1 (amount from the price sheet, discount derived, providerAmount recomputed).
curl -s -X POST $API/api/sessions -H "$AUTH" -H 'Content-Type: application/json' -d '{
  "sessionDate":"2026-08-03","sessionTime":"11:00:00","patientId":<PID>,"therapistId":<TID>,
  "duration":60,"specialtyTypeId":<SID>,"appointmentStatusId":1,
  "amount":9999,"discount":5000,"providerAmount":7777}' | jq '{amount, discount, providerAmount}'
```

## 3. BK-1 — duration validation (400 on values ∉ {30, 40, 45, 60, 90, 120})

```bash
# 50 min → 400 "not bookable"
curl -s -o /dev/null -w '%{http_code}\n' -X POST $API/api/sessions -H "$AUTH" \
  -H 'Content-Type: application/json' -d '{"sessionDate":"2026-08-03","sessionTime":"12:00:00",
  "patientId":<PID>,"therapistId":<TID>,"duration":50,"specialtyTypeId":<SID>}'

# 40 min → 201 (2026-07-27 addendum — Ent-TC 40→$35 / Ent-TC-P 40→$60 are live rows)
curl -s -X POST $API/api/sessions -H "$AUTH" -H 'Content-Type: application/json' -d '{
  "sessionDate":"2026-08-03","sessionTime":"12:00:00","patientId":<PID>,"therapistId":<ENT_TID>,
  "duration":40,"specialtyTypeId":<ENT_TC_ID>}' | jq '{amount, amountSource}'   # 35.00 / durationPrice
```

## 4. SENADIS exact-20% (expiry-aware) + G4 missing-price block

```bash
# Active-SENADIS patient → discount EXACTLY round(0.20 × amount, 2); expired/unflagged → 0.
# (Use a patient with hasSenadisDiscount=true and unexpired senadisExpirationDate.)

# Eval-Neuro (deliberately unpriced) → 400 with the exact G4 copy:
# "No price configured for {specialty} at {duration} min — ask a manager to set it in Admin."
curl -s -X POST $API/api/sessions -H "$AUTH" -H 'Content-Type: application/json' -d '{
  "sessionDate":"2026-08-03","sessionTime":"13:00:00","patientId":<PID>,"therapistId":<TID>,
  "duration":60,"specialtyTypeId":<EVAL_NEURO_ID>}'

# NM / EEG / EEG-REP / PSICOT (provisional DefaultAmount-only) → 201 with
# amountSource = "defaultAmount" (the UI badge path — real traffic day one).
```

## 5. BK-3 — gated discount edit (`Sessions.Discount.Edit`, AM+MGR)

```bash
# As FD (Appointments.Book only): change discount → 403, nothing changes.
curl -s -o /dev/null -w '%{http_code}\n' -X PUT $API/api/sessions/<SID2> -H "$FD_AUTH" \
  -H 'Content-Type: application/json' -d '{"sessionTime":"10:00:00","amount":60.00,
  "discount":15.00,"amountPaid":0,"notes":"","appointmentStatusId":1}'          # → 403

# As FD: same PUT with the discount ECHOED unchanged → 204 (FD keeps non-money edits).
# As MGR: discount edit → 204; then GET the session — providerAmount/grossProfit recomputed.
curl -s -X PUT $API/api/sessions/<SID2> -H "$MGR_AUTH" -H 'Content-Type: application/json' \
  -d '{"sessionTime":"10:00:00","amount":60.00,"discount":15.00,"amountPaid":0,"notes":"",
  "appointmentStatusId":1}' -o /dev/null -w '%{http_code}\n'                    # → 204

# Active-SENADIS session, discount below the 20% floor → 400 (floor holds on edits).
# Non-SENADIS: discount > amount or < 0 → 400.
# Duration OMITTED from the PUT body → stored duration preserved (legacy 50-min stays 50).
```

## 6. On-site leg (DORMANT at ship — all OfferedOnSite flags are 0)

```bash
# isOnSiteVisit=true on any specialty today → 400 "not offered as an on-site visit" (correct).
# When a flag flips later: expect onSiteChargeAmount = Site.OnSiteTripChargeAmount snapshot,
# providerAmount UNCHANGED by the charge, grossProfit and amountDue increased by it.
```

## 7. Bulk parity (G5)

```bash
# POST /api/treatmentplans/<planId>/schedule — created sessions carry price-sheet amounts
# (NOT $65/h pro-rate), derived exact-20%/0 discounts (lineOverrides[].discountAmount ignored),
# fee on net. A line with an unpriced specialty+duration or non-bookable duration → conflict,
# nothing persisted (atomic).
```

## Test suite

`dotnet test patient-care-api.sln` → **724 passed** (baseline 674; +50 WP-40 specs: derivation
rules, duration set incl. 40, G4 block, SENADIS exact-20 create ↔ floor-on-edit, on-site
snapshot/fee-exclusion, bulk parity incl. missing-price conflicts, BK-3 403/floor/recompute,
duration-omitted pass-through).
