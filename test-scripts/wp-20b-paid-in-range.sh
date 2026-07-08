#!/usr/bin/env bash
# WP-20B: Payroll "paid in range" visibility — curl test script
#
# Endpoints (all under /api/service-payments):
#   GET /api/service-payments/unpaid-sessions?therapistId=&from=&to=  -> ServicePayments.View (MGR, AM)
#       Response is now the WP-20 envelope: { sessions: [...], paidInRange: {...} }
#       (breaking shape change — ships in lockstep with the UI, WP-20C)
#   GET /api/service-payments/payroll-preview?from=&to=               -> ServicePayments.View (MGR, AM)
#       Rows gain two additive fields: paidSessionCount, paidTotal
#
# Reconciliation invariant (over Completed sessions in range):
#   Σ providerAmount = Σ remainingProviderAmount (sessions) + paidInRange.totalApplied
#
# Auth: every endpoint is claim-gated. Provide a JWT in TOKEN (MGR/AM token).
#   Without a token the secured calls return 401 (expected negative case).
#
# Prereqs:
#   - API running locally on :5245 (`dotnet run --project src/Web/Web.csproj`)
#   - RoleClaim seed applied so MGR/AM hold ServicePayments.View
#   - jq installed for pretty printing (optional)
#
# Usage:
#   TOKEN=$MGR_JWT THERAPIST_ID=23 ./test-scripts/wp-20b-paid-in-range.sh

set -euo pipefail

BASE_URL="${BASE_URL:-http://localhost:5245}"
THERAPIST_ID="${THERAPIST_ID:-23}"
TOKEN="${TOKEN:-}"
FROM="${FROM:-2026-05-30}"
TO="${TO:-2026-06-12}"

PRETTY="cat"
if command -v jq >/dev/null 2>&1; then PRETTY="jq ."; fi

AUTH=()
if [ -n "$TOKEN" ]; then AUTH=(-H "Authorization: Bearer ${TOKEN}"); fi

echo "=================================================================="
echo "Test 1 (acceptance / origin case): unpaid-sessions envelope"
echo "  GET ${BASE_URL}/api/service-payments/unpaid-sessions?therapistId=${THERAPIST_ID}&from=${FROM}&to=${TO}"
echo ""
echo "  With THERAPIST_ID=23, FROM=2026-05-30, TO=2026-06-12 against the"
echo "  production dataset, expect (the 2026-07-07 discrepancy case):"
echo "    - sessions[]: 16 payable rows, Σ remainingProviderAmount = 363.40"
echo "    - paidInRange.fullyPaidSessionCount = 4"
echo "    - paidInRange.fullyPaidTotal        = 85.00"
echo "    - paidInRange.totalApplied          = 85.00"
echo "    - paidInRange.sessions[]: 4 rows, each remainingProviderAmount = 0.00"
echo "      with paymentReferences[].referenceNumber = \"PAYROLL-2026-05\""
echo "    - Invariant: 448.40 (Σ provider) = 363.40 (Σ remaining) + 85.00 (totalApplied)"
echo "=================================================================="
curl -sS -w "\nHTTP %{http_code}\n" "${AUTH[@]}" \
    "${BASE_URL}/api/service-payments/unpaid-sessions?therapistId=${THERAPIST_ID}&from=${FROM}&to=${TO}" | $PRETTY
echo

echo "=================================================================="
echo "Test 2: payroll-preview rows carry the new additive paid context"
echo "  GET ${BASE_URL}/api/service-payments/payroll-preview?from=${FROM}&to=${TO}"
echo "  Expect: each row includes paidSessionCount (fully-paid count) and"
echo "          paidTotal (Σ amountApplied across ALL in-range sessions)."
echo "          For therapist 23 in the origin range: paidSessionCount=4, paidTotal=85.00."
echo "          Therapists with ONLY paid sessions in range are still skipped."
echo "=================================================================="
curl -sS -w "\nHTTP %{http_code}\n" "${AUTH[@]}" \
    "${BASE_URL}/api/service-payments/payroll-preview?from=${FROM}&to=${TO}" | $PRETTY
echo

echo "=================================================================="
echo "Test 3 (validation): to before from -> 400 Bad Request"
echo "  GET ${BASE_URL}/api/service-payments/unpaid-sessions?therapistId=${THERAPIST_ID}&from=2026-06-12&to=2026-05-30"
echo "=================================================================="
curl -sS -w "\nHTTP %{http_code}\n" "${AUTH[@]}" \
    "${BASE_URL}/api/service-payments/unpaid-sessions?therapistId=${THERAPIST_ID}&from=2026-06-12&to=2026-05-30" | $PRETTY
echo

echo "=================================================================="
echo "Test 4 (negative): any secured call with no token should be 401."
echo "  Unset TOKEN and re-run Test 1; expect HTTP 401."
echo "=================================================================="

echo "All WP-20B tests complete."
