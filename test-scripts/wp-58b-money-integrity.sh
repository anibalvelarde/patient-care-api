#!/usr/bin/env bash
# WP-58B: money integrity — curl test script
#
# Replays the 2026-11-08 E2E findings (patient-care-super/planning/audits/e2e-prerollout-2026-11-08.md)
# and expects them FIXED:
#   F-4  payment with unknown caretaker           → 400 (was 500 FK)
#   F-3  allocation to an unlinked family session → 400 (was 201)
#   F-1  PUT payment that fails validation        → 400 AND the old allocations are still there
#   F-16 SENADIS session, discount > amount       → 400 (was 204, negative provider/GP)
#   F-17 session PUT without "amount"             → 204 and Amount unchanged (was set to 0)
#   F-18 session PUT carrying amountPaid: 0       → 204 and AmountPaid unchanged (was zeroed)
#   Q-1  no-show on a SENADIS $50 session         → fee $40 (was $50)
#   health: /api/health/checks ledgerReconcile    → Healthy
#
# ⚠ Creates and changes real rows. Run against a LOCAL/test DB, never prod.
# Needs: TOKEN (MGR or SYSADMIN JWT), and IDs from your local copy:
#   CARETAKER_ID        a caretaker
#   LINKED_SESSION_ID   an unpaid, non-SENADIS session of a patient linked to CARETAKER_ID
#   OTHER_SESSION_ID    an unpaid session of a patient NOT linked to CARETAKER_ID
#   SENADIS_SESSION_ID  a Proposed, unpaid session (amount 50, discount 10) of an active-SENADIS patient
#   PAYMENT_TYPE_ID     e.g. 4 (Cash)
#
# Usage:
#   TOKEN=$JWT CARETAKER_ID=1135 LINKED_SESSION_ID=12357 OTHER_SESSION_ID=12342 \
#   SENADIS_SESSION_ID=12358 ./test-scripts/wp-58b-money-integrity.sh

set -euo pipefail
BASE_URL="${BASE_URL:-http://localhost:5245}"
: "${TOKEN:?set TOKEN}" "${CARETAKER_ID:?}" "${LINKED_SESSION_ID:?}" "${OTHER_SESSION_ID:?}" "${SENADIS_SESSION_ID:?}"
PAYMENT_TYPE_ID="${PAYMENT_TYPE_ID:-4}"
H=(-H "Authorization: Bearer ${TOKEN}" -H "Content-Type: application/json")
call() { curl -s -w '\nHTTP %{http_code}\n' "${H[@]}" "$@"; }

echo "== F-4: payment for unknown caretaker 0 → expect 400 'Caretaker 0 not found'"
call -X POST "$BASE_URL/api/payments" -d "{\"amount\":5,\"paymentDate\":\"2026-11-08\",\"caretakerId\":0,\"paymentTypeId\":$PAYMENT_TYPE_ID,\"sessionAllocations\":[]}"

echo "== F-3: allocate to an unlinked family's session → expect 400 'not linked to caretaker'"
call -X POST "$BASE_URL/api/payments" -d "{\"amount\":5,\"paymentDate\":\"2026-11-08\",\"caretakerId\":$CARETAKER_ID,\"paymentTypeId\":$PAYMENT_TYPE_ID,\"sessionAllocations\":[{\"sessionId\":$OTHER_SESSION_ID,\"amountAllocated\":5}]}"

echo "== F-1 setup: pay \$5 on the linked session → expect 201"
R=$(curl -s "${H[@]}" -X POST "$BASE_URL/api/payments" -d "{\"amount\":5,\"paymentDate\":\"2026-11-08\",\"caretakerId\":$CARETAKER_ID,\"paymentTypeId\":$PAYMENT_TYPE_ID,\"sessionAllocations\":[{\"sessionId\":$LINKED_SESSION_ID,\"amountAllocated\":5}]}")
PID=$(echo "$R" | sed -n 's/.*"paymentId":\([0-9]*\).*/\1/p'); echo "payment $PID"

echo "== F-1: PUT it with a nonexistent session → expect 400, then GET shows the original allocation still there"
call -X PUT "$BASE_URL/api/payments/$PID" -d "{\"amount\":5,\"paymentDate\":\"2026-11-08\",\"caretakerId\":$CARETAKER_ID,\"paymentTypeId\":$PAYMENT_TYPE_ID,\"sessionAllocations\":[{\"sessionId\":999999,\"amountAllocated\":5}]}"
call "$BASE_URL/api/payments/$PID" | grep -o "\"sessionId\":$LINKED_SESSION_ID" || echo "!! allocation LOST"

echo "== F-1/F-4: PUT without caretakerId → expect 400 (was 500), allocation still there"
call -X PUT "$BASE_URL/api/payments/$PID" -d "{\"amount\":5,\"paymentDate\":\"2026-11-08\",\"paymentTypeId\":$PAYMENT_TYPE_ID,\"sessionAllocations\":[{\"sessionId\":$LINKED_SESSION_ID,\"amountAllocated\":5}]}"

echo "== F-18: session PUT carrying a stale amountPaid:0 → expect 204; the session's amountPaid stays 5"
call -X PUT "$BASE_URL/api/sessions/$LINKED_SESSION_ID" -d '{"sessionTime":"15:00:00","therapyType":"TL","amount":50,"amountPaid":0,"discount":10,"isPaidOff":false,"notes":"stale tab"}'
call "$BASE_URL/api/sessions/$LINKED_SESSION_ID/payments" | head -c 300; echo

echo "== F-17: session PUT without amount (notes only) → expect 204; amount unchanged"
call -X PUT "$BASE_URL/api/sessions/$SENADIS_SESSION_ID" -d '{"sessionTime":"16:00:00","discount":10,"notes":"note only"}'

echo "== F-16: SENADIS session, discount 60 > amount 50 → expect 400 'between 0 and the session amount'"
call -X PUT "$BASE_URL/api/sessions/$SENADIS_SESSION_ID" -d '{"sessionTime":"16:00:00","amount":50,"discount":60}'

echo "== Q-1: no-show the SENADIS \$50 session → expect amount 40.00 (what they would have paid)"
call -X PUT "$BASE_URL/api/sessions/$SENADIS_SESSION_ID/noshow" | grep -o '"amount":[0-9.]*' | head -1

echo "== health: ledgerReconcile → expect Healthy"
call "$BASE_URL/api/health/checks" | grep -o '"name":"ledgerReconcile","status":"[A-Za-z]*"'

echo "== SQL cross-check (local DB):"
echo "   SELECT SessionID, AmountPaid, LegacyAmountPaid, (SELECT IFNULL(SUM(AmountAllocated),0) FROM SessionPayment sp WHERE sp.SessionID=s.SessionID) alloc"
echo "   FROM TherapySession s WHERE SessionID IN ($LINKED_SESSION_ID,$SENADIS_SESSION_ID,$OTHER_SESSION_ID);"
