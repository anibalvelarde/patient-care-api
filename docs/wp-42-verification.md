# WP-42B Verification — Cancellation & No-Show Money Integrity

Curl test commands for the WP-42 money-at-transition semantics + covered-session lock.
Run against a deployed API (`http://neurocorp.k3s:30000`) or local (`http://localhost:5245`).

**Prereqs:**
- V032 applied (`Site.NoShowFeePct decimal(5,2) NOT NULL DEFAULT 30.00`).
- No claim/matrix change in this WP — no reseed, no re-login. The Site fee edit is gated by
  the **SYSADMIN role** (the `role` claims already in every token).

```bash
API=http://neurocorp.k3s:30000
# Login (field is "email", not "username")
TOKEN=$(curl -s -X POST $API/api/auth/login -H 'Content-Type: application/json' \
  -d '{"email":"<operator-email>","password":"<pwd>"}' | jq -r .token)
AUTH="Authorization: Bearer $TOKEN"
# Also mint $SA_AUTH (SYSADMIN login) and $MGR_AUTH (Manager) for the role-gate tests.
```

## 1. Site `noShowFeePct` — read, validate, role-gate

```bash
# GET carries the new field (default 30 after V032).
curl -s $API/api/sites/1 -H "$AUTH" | jq '{siteId, noShowFeePct, onSiteTripChargeAmount}'

# Out of range → 400 (0–100; automatic [Range] validation).
curl -s -o /dev/null -w '%{http_code}\n' -X PUT $API/api/sites/1 -H "$SA_AUTH" \
  -H 'Content-Type: application/json' -d '{"noShowFeePct": 100.01}'   # → 400
curl -s -o /dev/null -w '%{http_code}\n' -X PUT $API/api/sites/1 -H "$SA_AUTH" \
  -H 'Content-Type: application/json' -d '{"noShowFeePct": -0.01}'    # → 400

# CHANGED value as non-SYSADMIN (e.g. MGR with Admin.Sites.Manage) → 403, nothing changes.
curl -s -o /dev/null -w '%{http_code}\n' -X PUT $API/api/sites/1 -H "$MGR_AUTH" \
  -H 'Content-Type: application/json' -d '{"noShowFeePct": 15}'       # → 403

# ECHOED-unchanged value as non-SYSADMIN → 204 (field-gate: present-but-unchanged passes).
curl -s -o /dev/null -w '%{http_code}\n' -X PUT $API/api/sites/1 -H "$MGR_AUTH" \
  -H 'Content-Type: application/json' -d '{"siteName":"<current-name>","noShowFeePct": 30}'  # → 204

# CHANGED value as SYSADMIN → 204; GET shows the new pct.
curl -s -o /dev/null -w '%{http_code}\n' -X PUT $API/api/sites/1 -H "$SA_AUTH" \
  -H 'Content-Type: application/json' -d '{"noShowFeePct": 12.5}'     # → 204
curl -s $API/api/sites/1 -H "$SA_AUTH" | jq .noShowFeePct              # → 12.5

# POST: a non-default pct (≠ 30) likewise requires SYSADMIN → 403 for others; omitted → 30.
```

## 2. Cancel zeroes money + stamps the marker

```bash
# Book a throwaway session (status 1) and note its derived money, then cancel it.
SID=<new-session-id>
curl -s -X PUT $API/api/sessions/$SID/cancel -H "$AUTH" -H 'Content-Type: application/json' \
  -d '{"reason":"familia avisó"}' | jq '{appointmentStatusId, amount, discount, providerAmount, notes}'
# Expect: appointmentStatusId 3, amount/discount/providerAmount all 0.00, notes containing
#   "[Cancelled] familia avisó" AND "[CANCELLED-ZEROED yyyy-MM-dd: was A:.. D:.. P:.. G:..]"

# Idempotency: cancel the SAME session again → 200, still one single marker (no duplicate).
curl -s -X PUT $API/api/sessions/$SID/cancel -H "$AUTH" -H 'Content-Type: application/json' \
  -d '{"reason":null}' | jq .notes
```

## 3. No-show applies the site fee (30% default; PA=0, GP=fee)

```bash
# Confirmed session with amount 85.00 at a 30% site → fee 25.50.
SID2=<confirmed-session-id>
curl -s -X PUT $API/api/sessions/$SID2/noshow -H "$AUTH" | \
  jq '{appointmentStatusId, amount, discount, providerAmount, notes}'
# Expect: status 5, amount 25.50 (= round(30% × 85.00, 2)), discount 0, providerAmount 0,
#   notes containing "[NOSHOW-FEE yyyy-MM-dd: was A:85.00 …]". GrossProfit = the fee
#   (clinic keeps 100% — therapist rendered nothing).
# With the site at 12.5%: 85.00 → 10.63 (half-cent rounds AWAY, matching MySQL ROUND).
# With the site at 0%: amount → 0.00 (no fee), originals still stamped.

# Fee waive/adjust afterwards = the normal gated discount edit (Sessions.Discount.Edit, AM/MGR).
```

## 4. Declined confirmation ≡ cancel (same choke point)

```bash
curl -s -X PUT $API/api/sessions/<SID3>/confirm -H "$AUTH" -H 'Content-Type: application/json' \
  -d '{"confirmationMethod":"Phone","confirmationResult":"Declined"}' | \
  jq '{appointmentStatusId, amount, notes}'
# Expect: status 3, amount 0.00, "[CANCELLED-ZEROED …]" marker.
# GET /api/sessions/<SID3>/confirmations shows the Declined attempt.
```

## 5. Guards — money moved / payroll covered (nothing changes on 400)

```bash
# a) Session with a caretaker payment recorded (amountPaid > 0) → 400 on cancel/noshow:
curl -s -X PUT $API/api/sessions/<PAID_SID>/cancel -H "$AUTH" \
  -H 'Content-Type: application/json' -d '{"reason":"x"}' | jq .detail
# → "Payments are recorded against this session; money-moved cancellations are refund territory."

# b) Session covered by a service payment (payroll ran) → 400 on cancel/noshow/Declined:
curl -s -X PUT $API/api/sessions/<COVERED_SID>/noshow -H "$AUTH" | jq .detail
# → "This session is covered by a service payment — reverse the covering payment first."

# After the block: GET the session — status AND money must be byte-identical (nothing changed);
# on the Declined path additionally GET /confirmations — the blocked attempt is NOT recorded.

# c) Reverse the covering payment (POST /api/servicepayments/{id}/reverse), then retry → 200.
```

## 6. Generic PUT — same choke point + covered-session lock (G5)

```bash
# a) PUT with appointmentStatusId → 3 zeroes exactly like /cancel (client-echoed money is
#    overridden by the zero write; the WP-40 derivation does NOT run — no SENADIS floor here):
curl -s -o /dev/null -w '%{http_code}\n' -X PUT $API/api/sessions/<SID4> -H "$AUTH" \
  -H 'Content-Type: application/json' -d '{"sessionTime":"10:00:00","amount":85.00,
  "discount":0,"amountPaid":0,"notes":"","appointmentStatusId":3}'    # → 204, then GET: all 0

# b) Covered session: amount/discount change OR therapist reassignment → 400 (lock runs
#    BEFORE the WP-40 recompute — the paid fee is never silently changed):
curl -s -X PUT $API/api/sessions/<COVERED_SID> -H "$MGR_AUTH" -H 'Content-Type: application/json' \
  -d '{"sessionTime":"10:00:00","amount":85.00,"discount":10.00,"amountPaid":0,"notes":"",
  "appointmentStatusId":4}' | jq .detail
# → "This session is covered by a service payment — reverse the covering payment first."

# c) Covered session, ECHOED-unchanged money + notes edit → 204 (non-money edits stay allowed).
# d) Covered session, status change to 3/5 via PUT → the same 400 as (b).
# e) Transitions OUT of 3/5 (e.g. back to Confirmed) restore nothing — originals stay in the
#    Notes marker; staff re-price via the gated edit or rebook.
```

## 7. Regression sweep

```bash
# /complete, /checkin, /start-therapy on a money-carrying session → money untouched.
# Booking, WP-40 derivation, discount-edit gate (BK-3), duration rules — unchanged
# (suite: 724 → 812 tests green; see tests/Core.Tests/SessionTransitionMoneyServiceTests.cs,
#  tests/Infrastructure.Tests/Wp42CancellationMoneyTests.cs).
```
