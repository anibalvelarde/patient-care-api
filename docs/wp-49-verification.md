# WP-49 — Fee Policy: API verification

Curl commands for the three new endpoints plus both 403 paths, and the BR1 no-show sweep.

**Base URL:** `http://localhost:5245` locally · `http://neurocorp.k3s:30000` deployed.

> ⚠️ **Re-login before testing.** Claims are baked into the JWT, so a MGR token minted before the
> `seed-role-claims.sql` reseed will 403 on every `Sessions.Fee.Manage` route no matter how the
> matrix reads. If a 403 surprises you, check the token before checking the code.

```bash
BASE=http://localhost:5245
MGR=$(curl -s -X POST $BASE/api/auth/login -H 'Content-Type: application/json' \
  -d '{"email":"manager@neurocorp.local","password":"..."}' | jq -r .token)
AM=$(curl -s -X POST $BASE/api/auth/login -H 'Content-Type: application/json' \
  -d '{"email":"assistant@neurocorp.local","password":"..."}' | jq -r .token)
```

Decode a token to confirm the new claim actually arrived:

```bash
echo $MGR | cut -d. -f2 | base64 -d 2>/dev/null | jq '.Permission' | grep -i 'Sessions.Fee.Manage'
# expect: "Sessions.Fee.Manage"
```

---

## 1. Preview the late-fee batch — `GET /api/sessions/late-fees/preview`

Read-only. Charges nothing. Rides `Patients.Delinquent.View`, so **AM can run it**.

```bash
curl -s -H "Authorization: Bearer $MGR" \
  "$BASE/api/sessions/late-fees/preview" | jq
```

Expected shape:

```json
{
  "asOf": "2026-08-08",
  "ratePct": 30.00,
  "graceDays": 6,
  "sessionCount": 1,
  "totalUnpaidBalance": 125.00,
  "totalProposedFee": 37.50,
  "items": [
    { "sessionId": 11995, "sessionDate": "2026-05-20", "daysUnpaid": 80,
      "patientName": "…", "caretakerName": "…",
      "unpaidBalance": 125.00, "proposedFee": 37.50 }
  ]
}
```

**Against prod as measured 2026-08-02 the whole receivables book was 1 session / $125.00**, so
expect roughly one row and ~$37.50. A materially larger number means the book moved between
planning and cutover — stop and read it before anyone presses Apply.

Pin the evaluation date to check the grace boundary:

```bash
# a session dated 2026-08-02 is INSIDE grace on the 8th (6 days) …
curl -s -H "Authorization: Bearer $MGR" \
  "$BASE/api/sessions/late-fees/preview?asOf=2026-08-08" | jq '.items[].sessionId'
# … and chargeable on the 9th (7 days).
curl -s -H "Authorization: Bearer $MGR" \
  "$BASE/api/sessions/late-fees/preview?asOf=2026-08-09" | jq '.items[].sessionId'
```

---

## 2. Apply the batch — `POST /api/sessions/late-fees/batch`

Requires `Sessions.Fee.Manage`. Charges only the session ids you pass — never "everything
eligible right now" — so a row that becomes eligible between preview and apply is not swept in.

```bash
curl -s -X POST "$BASE/api/sessions/late-fees/batch" \
  -H "Authorization: Bearer $MGR" -H 'Content-Type: application/json' \
  -d '{"sessionIds":[11995],"asOf":"2026-08-08"}' | jq
```

```json
{
  "asOf": "2026-08-08",
  "appliedCount": 1,
  "skippedCount": 0,
  "totalFeeApplied": 37.50,
  "applied": [
    { "sessionId": 11995, "feeApplied": 37.50,
      "unpaidBalanceBefore": 125.00, "amountDueAfter": 162.50 }
  ],
  "skipped": []
}
```

**Re-run the exact same call.** The latch must make it a no-op with a stated reason — this is
the anti-compounding guarantee, and it is the single most important thing to verify by hand:

```json
{ "appliedCount": 0, "skippedCount": 1,
  "skipped": [{ "sessionId": 11995, "reason": "A late fee has already been applied to this session." }] }
```

Other skip reasons worth provoking: pay the session off first (`"…no unpaid balance…"`), pass a
session inside grace (`"…grace period"`), pass a cancelled session (`"…not billable."`), pass a
nonexistent id (`"Session not found."`).

Confirm the money landed where it should:

```sql
SELECT SessionID, Amount, DiscountAmount, AmountPaid, LateFeeAmount, LateFeeAppliedOn,
       GrossProfit, IsPaidOff, Notes
FROM   TherapySession WHERE SessionID = 11995\G
-- LateFeeAmount 37.50 · LateFeeAppliedOn = asOf · GrossProfit up by exactly 37.50
-- Notes gains: [LATE-FEE 2026-08-08: 30% of 125.00 = 37.50; 80 days unpaid]
```

---

## 3. Waive a fee — `POST /api/sessions/{id}/waive-fee`

Requires `Sessions.Fee.Manage`. `feeKind` is **required** (`Late` | `NoShow` | `Both`) because a
session can carry both.

```bash
curl -s -X POST "$BASE/api/sessions/11995/waive-fee" \
  -H "Authorization: Bearer $MGR" -H 'Content-Type: application/json' \
  -d '{"feeKind":"Late","reason":"caretaker hospitalized"}' | jq
```

```json
{ "sessionId": 11995, "feeKind": "Late", "lateFeeWaived": 37.50, "noShowFeeWaived": 0,
  "amountDueAfter": 125.00, "grossProfitAfter": 125.00,
  "waivedOn": "2026-08-08", "waivedByUserId": 3 }
```

Then verify the two properties that matter most:

```sql
SELECT LateFeeAmount, LateFeeAppliedOn, FeeWaivedOn, FeeWaivedByUserId
FROM   TherapySession WHERE SessionID = 11995;
-- LateFeeAmount 0.00  (the live fee is gone)
-- LateFeeAppliedOn STILL SET  ← the latch survives, so the batch cannot re-charge it
```

**Re-run the batch on a waived session.** It must skip, not re-charge:

```bash
curl -s -X POST "$BASE/api/sessions/late-fees/batch" \
  -H "Authorization: Bearer $MGR" -H 'Content-Type: application/json' \
  -d '{"sessionIds":[11995]}' | jq '.appliedCount, .skipped[0].reason'
# 0, "A late fee has already been applied to this session."
```

Expected 400s:

```bash
# no reason
-d '{"feeKind":"Late"}'                       # → "A reason is required to waive a fee."
# no kind
-d '{"reason":"which one?"}'                  # → "Specify which fee to waive: Late, NoShow, or Both."
# already waived
-d '{"feeKind":"Late","reason":"again"}'      # → "The late fee on this session has already been waived."
# no such fee
-d '{"feeKind":"NoShow","reason":"nope"}'     # → "This session has no no-show fee to waive."
```

**The credit guard (D3)** — waiving is allowed on a partially paid session (the normal case) and
blocked only when it would leave the caretaker owed money:

```bash
# on a session where AmountPaid exceeds what would remain after the waive:
-d '{"feeKind":"Late","reason":"overpaid"}'
# → 400 "Waiving this fee would leave the caretaker with a credit — record a refund instead."
```

**Marker sanitizing** — a `]` in the reason would otherwise break the bracket-bounded shape that
the print sanitizer matches on:

```bash
-d '{"feeKind":"Late","reason":"closed] [LEGACY-IMPORT: fake"}'
# Notes marker must contain exactly one [ and one ]:
#   [FEE-WAIVED 2026-08-08: late 37.50 waived by u#3; reason: closed LEGACY-IMPORT: fake]
```

---

## 4. Both 403 paths

```bash
# (a) AM hits APPLY — has Patients.Delinquent.View, lacks Sessions.Fee.Manage
curl -s -o /dev/null -w '%{http_code}\n' -X POST "$BASE/api/sessions/late-fees/batch" \
  -H "Authorization: Bearer $AM" -H 'Content-Type: application/json' \
  -d '{"sessionIds":[11995]}'
# expect 403

# (b) AM hits WAIVE — this is BR4's whole point: AM loses waiver authority
curl -s -o /dev/null -w '%{http_code}\n' -X POST "$BASE/api/sessions/11995/waive-fee" \
  -H "Authorization: Bearer $AM" -H 'Content-Type: application/json' \
  -d '{"feeKind":"Late","reason":"AM should not be able to do this"}'
# expect 403

# (c) …but AM CAN still read the preview
curl -s -o /dev/null -w '%{http_code}\n' \
  -H "Authorization: Bearer $AM" "$BASE/api/sessions/late-fees/preview"
# expect 200
```

### The loophole guard (ruling 4)

The reason one claim covers three actions. On a **fee-bearing** session, an AM holding only
`Sessions.Discount.Edit` cannot change the discount — otherwise it could erase the fee by
setting discount = amount, moving the same money as a waiver with no reason and no audit record.

```bash
# AM edits the discount on a session that carries a fee → 403
curl -s -o /dev/null -w '%{http_code}\n' -X PUT "$BASE/api/sessions/11995" \
  -H "Authorization: Bearer $AM" -H 'Content-Type: application/json' \
  -d '{"amount":125,"discount":125,"amountPaid":0,"providerAmount":0,"appointmentStatusId":4,"notes":"","sessionTime":"10:00:00"}'
# expect 403

# the same AM editing the discount on a FEE-FREE session still works → 204
curl -s -o /dev/null -w '%{http_code}\n' -X PUT "$BASE/api/sessions/<fee-free-id>" \
  -H "Authorization: Bearer $AM" -H 'Content-Type: application/json' \
  -d '{"amount":100,"discount":15,"amountPaid":0,"providerAmount":0,"appointmentStatusId":4,"notes":"","sessionTime":"10:00:00"}'
# expect 204
```

---

## 5. BR1 — no-show now bills the full session price

⚠️ Only true **after V033 lands**, which is deliberately the LAST deploy step.

```bash
# book/confirm a session at a known amount, then no-show it
curl -s -X POST "$BASE/api/sessions/<id>/noshow" -H "Authorization: Bearer $MGR" | jq
```

```sql
SELECT Amount, DiscountAmount, ProviderAmount, GrossProfit, Notes
FROM   TherapySession WHERE SessionID = <id>\G
-- Amount == the ORIGINAL booked amount (100% of it, not 30%)
-- ProviderAmount 0 · GrossProfit == Amount · Notes has [NOSHOW-FEE … was A:…]
```

Sites admin gate — the privileged/unprivileged pivot **inverted** with the default:

```bash
# MGR (non-SYSADMIN) creating a site at the new default 100 → 201
-d '{"siteName":"X","inceptionDate":"2026-08-01","noShowFeePct":100}'
# the same MGR trying 30 (now a deliberate override) → 403
-d '{"siteName":"X","inceptionDate":"2026-08-01","noShowFeePct":30}'
```

---

## 6. Statement itemization

The caretaker statement must **explain** the larger balance rather than just showing it:

```bash
curl -s -H "Authorization: Bearer $MGR" \
  "$BASE/api/statements/caretaker/<caretakerId>?from=2026-01-01&to=2026-12-31" | jq '.charges[], .summary'
```

Each charge now carries `lateFeeAmount` and `onSiteChargeAmount`, and the arithmetic must close:

```
netCharge + onSiteChargeAmount + lateFeeAmount − amountPaid == amountDue
```

The summary gains `totalLateFees` and `totalOnSiteCharges`.

---

## 7. Regression checks worth running by hand

| Check | Why it matters |
|---|---|
| Apply a fee, then **edit the discount**, then re-read `GrossProfit` | Finding 1 — the recompute must keep the fee. Silent and delayed if wrong. |
| Apply a fee to a **paid-off** session | `IsPaidOff` must flip back to false; the fee is collectible. |
| Record a payment against a fee-bearing session | The allocation cap must allow up to the fee-inclusive `AmountDue`. |
| A session whose **only** balance is a late fee | Must appear on the delinquent list after 35 days (the SQL mirror). |
| **Cancel** a fee-bearing session | The fee survives (BR2 voids the service, not the penalty) and stays in `GrossProfit`. |

## Suite baselines

`dotnet build && dotnet test` — **938 passing** (Core 435 · Infrastructure 169 · Web 334).
Release build is clean under `TreatWarningsAsErrors`.
