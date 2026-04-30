#!/usr/bin/env bash
# WP-13B: Therapist Statement API curl test script
#
# Endpoint:
#   GET /api/therapists/{id}/statement?from=YYYY-MM-DD&to=YYYY-MM-DD
#
# Behavior:
#   - from/to optional; defaults: from = today - 90d, to = today
#   - 404 if therapist does not exist
#   - 400 if to < from or dates are malformed
#   - Status filter: Completed | Cancelled | NoShow only
#   - Therapy Type fallback: SpecialtyType.Name -> TherapyTypes -> "N/A"
#   - IsProForma always true; ServicePayments[] always empty (until WP-14)
#
# Prereqs:
#   - API running locally on :5245 (`dotnet run --project src/Web/Web.csproj`)
#   - jq installed for pretty printing (optional)
#
# Usage:
#   THERAPIST_ID=1 ./test-scripts/wp-13b-therapist-statement.sh
#   THERAPIST_ID=2 BASE_URL=http://localhost:5245 ./test-scripts/wp-13b-therapist-statement.sh

set -euo pipefail

BASE_URL="${BASE_URL:-http://localhost:5245}"
THERAPIST_ID="${THERAPIST_ID:-1}"
NONEXISTENT_THERAPIST_ID="${NONEXISTENT_THERAPIST_ID:-99999}"
MIXED_THERAPIST_ID="${MIXED_THERAPIST_ID:-$THERAPIST_ID}"
FALLBACK_THERAPIST_ID="${FALLBACK_THERAPIST_ID:-$THERAPIST_ID}"

FROM="${FROM:-2026-01-01}"
TO="${TO:-2026-04-30}"

EMPTY_FROM="2099-01-01"
EMPTY_TO="2099-01-31"

PRETTY="cat"
if command -v jq >/dev/null 2>&1; then
    PRETTY="jq ."
fi

echo "=================================================================="
echo "Test 1: Happy path with default date range (no from/to)"
echo "  GET ${BASE_URL}/api/therapists/${THERAPIST_ID}/statement"
echo "=================================================================="
curl -sS -w "\nHTTP %{http_code}\n" \
    "${BASE_URL}/api/therapists/${THERAPIST_ID}/statement" | $PRETTY
echo

echo "=================================================================="
echo "Test 2: Happy path with explicit from/to"
echo "  GET ${BASE_URL}/api/therapists/${THERAPIST_ID}/statement?from=${FROM}&to=${TO}"
echo "=================================================================="
curl -sS -w "\nHTTP %{http_code}\n" \
    "${BASE_URL}/api/therapists/${THERAPIST_ID}/statement?from=${FROM}&to=${TO}" | $PRETTY
echo

echo "=================================================================="
echo "Test 3: Empty range (no sessions in window)"
echo "  GET ${BASE_URL}/api/therapists/${THERAPIST_ID}/statement?from=${EMPTY_FROM}&to=${EMPTY_TO}"
echo "  Expect: 200 OK, patients=[], summary all zeros, isProForma=true"
echo "=================================================================="
curl -sS -w "\nHTTP %{http_code}\n" \
    "${BASE_URL}/api/therapists/${THERAPIST_ID}/statement?from=${EMPTY_FROM}&to=${EMPTY_TO}" | $PRETTY
echo

echo "=================================================================="
echo "Test 4: Mixed-status therapist"
echo "  GET ${BASE_URL}/api/therapists/${MIXED_THERAPIST_ID}/statement?from=${FROM}&to=${TO}"
echo "  Expect: Cancelled/NoShow rows present with providerAmount=0,"
echo "          isBillable=false; estimatedAmountDue sums Completed only."
echo "=================================================================="
curl -sS -w "\nHTTP %{http_code}\n" \
    "${BASE_URL}/api/therapists/${MIXED_THERAPIST_ID}/statement?from=${FROM}&to=${TO}" | $PRETTY
echo

echo "=================================================================="
echo "Test 5: 404 nonexistent therapist"
echo "  GET ${BASE_URL}/api/therapists/${NONEXISTENT_THERAPIST_ID}/statement"
echo "  Expect: HTTP 404"
echo "=================================================================="
curl -sS -w "\nHTTP %{http_code}\n" \
    "${BASE_URL}/api/therapists/${NONEXISTENT_THERAPIST_ID}/statement" || true
echo

echo "=================================================================="
echo "Test 6: 400 to < from"
echo "  GET ${BASE_URL}/api/therapists/${THERAPIST_ID}/statement?from=2026-04-30&to=2026-01-01"
echo "  Expect: HTTP 400"
echo "=================================================================="
curl -sS -w "\nHTTP %{http_code}\n" \
    "${BASE_URL}/api/therapists/${THERAPIST_ID}/statement?from=2026-04-30&to=2026-01-01" || true
echo

echo "=================================================================="
echo "Test 7: 400 malformed date"
echo "  GET ${BASE_URL}/api/therapists/${THERAPIST_ID}/statement?from=not-a-date&to=2026-04-30"
echo "  Expect: HTTP 400 (model binding rejects)"
echo "=================================================================="
curl -sS -w "\nHTTP %{http_code}\n" \
    "${BASE_URL}/api/therapists/${THERAPIST_ID}/statement?from=not-a-date&to=2026-04-30" || true
echo

echo "=================================================================="
echo "Test 8: Therapy Type fallback verification"
echo "  GET ${BASE_URL}/api/therapists/${FALLBACK_THERAPIST_ID}/statement?from=${FROM}&to=${TO}"
echo "  Inspect: rows should show therapyType resolved per fallback chain:"
echo "    1. SpecialtyType.Name (when SpecialtyTypeId is set)"
echo "    2. TherapyTypes legacy text (when SpecialtyTypeId is null)"
echo "    3. \"N/A\" (when both are null/empty)"
echo "=================================================================="
curl -sS -w "\nHTTP %{http_code}\n" \
    "${BASE_URL}/api/therapists/${FALLBACK_THERAPIST_ID}/statement?from=${FROM}&to=${TO}" | $PRETTY
echo

echo "=================================================================="
echo "All WP-13B tests complete."
echo "=================================================================="
