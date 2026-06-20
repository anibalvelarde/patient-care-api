#!/usr/bin/env bash
# WP-14B: Service Payments (therapist payroll) API curl test script
#
# Endpoints (all under /api/service-payments):
#   GET  /api/service-payments?therapistId=&from=&to=        -> ServicePayments.View (MGR, AM)
#   GET  /api/service-payments/{id}                          -> ServicePayments.View (MGR, AM)
#   GET  /api/service-payments/unpaid-sessions?therapistId=&from=&to=  -> ServicePayments.View
#   GET  /api/service-payments/quincena?date=YYYY-MM-DD      -> ServicePayments.View
#   POST /api/service-payments                               -> ServicePayments.Record (MGR only)
#
# Also re-run the therapist statement to confirm it flips to authoritative:
#   GET  /api/therapists/{id}/statement?from=&to=            -> isProForma=false once paid
#
# Auth: every endpoint is claim-gated. Provide a JWT in TOKEN (MGR token to exercise POST;
#   MGR/AM token for the GETs). Without a token the secured calls return 401, and an AM token
#   returns 403 on POST — both are expected negative cases shown below.
#
# Prereqs:
#   - API running locally on :5245 (`dotnet run --project src/Web/Web.csproj`)
#   - RoleClaim seed applied so MGR holds ServicePayments.View/.Record and AM holds .View
#   - jq installed for pretty printing (optional)
#
# Usage:
#   TOKEN=$MGR_JWT THERAPIST_ID=1 ./test-scripts/wp-14b-service-payments.sh

set -euo pipefail

BASE_URL="${BASE_URL:-http://localhost:5245}"
THERAPIST_ID="${THERAPIST_ID:-1}"
TOKEN="${TOKEN:-}"
PAYMENT_TYPE_ID="${PAYMENT_TYPE_ID:-1}"
FROM="${FROM:-2026-04-01}"
TO="${TO:-2026-04-15}"

PRETTY="cat"
if command -v jq >/dev/null 2>&1; then PRETTY="jq ."; fi

AUTH=()
if [ -n "$TOKEN" ]; then AUTH=(-H "Authorization: Bearer ${TOKEN}"); fi

echo "=================================================================="
echo "Test 1: Quincena helper for a mid-month date (expect 1st-15th)"
echo "  GET ${BASE_URL}/api/service-payments/quincena?date=2026-04-10"
echo "=================================================================="
curl -sS -w "\nHTTP %{http_code}\n" "${AUTH[@]}" \
    "${BASE_URL}/api/service-payments/quincena?date=2026-04-10" | $PRETTY
echo

echo "=================================================================="
echo "Test 2: Unpaid provider sessions for the period (drives the wizard)"
echo "  GET ${BASE_URL}/api/service-payments/unpaid-sessions?therapistId=${THERAPIST_ID}&from=${FROM}&to=${TO}"
echo "  Expect: completed sessions whose remainingProviderAmount > 0"
echo "=================================================================="
curl -sS -w "\nHTTP %{http_code}\n" "${AUTH[@]}" \
    "${BASE_URL}/api/service-payments/unpaid-sessions?therapistId=${THERAPIST_ID}&from=${FROM}&to=${TO}" | $PRETTY
echo

echo "=================================================================="
echo "Test 3: Issue a service payment (POST -> 201). Requires an MGR token."
echo "  Adjust sessionAllocations to real unpaid session IDs from Test 2."
echo "  POST ${BASE_URL}/api/service-payments"
echo "=================================================================="
curl -sS -w "\nHTTP %{http_code}\n" "${AUTH[@]}" \
    -H "Content-Type: application/json" \
    -X POST "${BASE_URL}/api/service-payments" \
    -d "{
          \"therapistId\": ${THERAPIST_ID},
          \"paymentDate\": \"2026-04-16T00:00:00\",
          \"amount\": 60.00,
          \"paymentTypeId\": ${PAYMENT_TYPE_ID},
          \"referenceNumber\": \"TX-1001\",
          \"notes\": \"April first-quincena\",
          \"sessionAllocations\": [ { \"sessionId\": 1, \"amountApplied\": 60.00 } ]
        }" | $PRETTY
echo

echo "=================================================================="
echo "Test 4: List service payments for the therapist"
echo "  GET ${BASE_URL}/api/service-payments?therapistId=${THERAPIST_ID}&from=${FROM}&to=2026-04-30"
echo "=================================================================="
curl -sS -w "\nHTTP %{http_code}\n" "${AUTH[@]}" \
    "${BASE_URL}/api/service-payments?therapistId=${THERAPIST_ID}&from=${FROM}&to=2026-04-30" | $PRETTY
echo

echo "=================================================================="
echo "Test 5: Statement is now authoritative (isProForma=false)"
echo "  GET ${BASE_URL}/api/therapists/${THERAPIST_ID}/statement?from=${FROM}&to=2026-04-30"
echo "  Expect: isProForma=false, summary.totalServicePaymentsApplied > 0,"
echo "          estimatedAmountDue = totalProviderAmount - applied, servicePayments[] populated"
echo "=================================================================="
curl -sS -w "\nHTTP %{http_code}\n" "${AUTH[@]}" \
    "${BASE_URL}/api/therapists/${THERAPIST_ID}/statement?from=${FROM}&to=2026-04-30" | $PRETTY
echo

echo "=================================================================="
echo "Test 6 (negative): POST with an AM token should be 403 (Record is MGR-only)"
echo "  Set TOKEN=\$AM_JWT and re-run; expect HTTP 403 on the POST above."
echo "Test 7 (negative): any secured call with no token should be 401."
echo "=================================================================="

echo "All WP-14B tests complete."
