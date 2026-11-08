#!/usr/bin/env bash
# WP-57B: email optional at patient/caretaker CREATE — curl test script
#
# Bug: WP-50C made email optional in the UI (blank → field omitted) but the API kept
# [Required] Email → POST /api/patients and POST /api/caretakers returned
# 400 "One or more validation errors occurred." for every email-less create.
# Fix: Email optional; blank/whitespace stored as NULL (SystemUser.Email is UNIQUE —
# a stored "" would make the SECOND email-less create collide on uq_systemuser_email).
#
# ⚠ Creates real rows. Run against a LOCAL/test DB, never prod.
# Auth: MGR/AM/FD JWT in TOKEN (Patients.Create / Caretakers.Edit).
#
# Usage:
#   TOKEN=$MGR_JWT ./test-scripts/wp-57b-email-optional-create.sh

set -euo pipefail

BASE_URL="${BASE_URL:-http://localhost:5245}"
TOKEN="${TOKEN:?set TOKEN to an MGR/AM/FD JWT}"
TAG="$(date +%s)"

post() { # $1=path $2=json → prints "HTTP <code>" then body
  curl -s -w '\nHTTP %{http_code}\n' -X POST "${BASE_URL}$1" \
    -H "Authorization: Bearer ${TOKEN}" -H "Content-Type: application/json" -d "$2"
}

echo "== Test 1: patient, email OMITTED → expect 201, email null"
post /api/patients "{\"firstName\":\"WP57\",\"lastName\":\"NoEmailA-${TAG}\",\"cedula\":\"WP57-A-${TAG}\",\"dateOfBirth\":\"2015-06-01\",\"phoneNumber\":\"555-0100\",\"gender\":\"Female\"}"

echo "== Test 2: SECOND patient, email blank \"\" → expect 201 (no uq_systemuser_email collision)"
post /api/patients "{\"firstName\":\"WP57\",\"lastName\":\"NoEmailB-${TAG}\",\"cedula\":\"WP57-B-${TAG}\",\"dateOfBirth\":\"2015-06-01\",\"email\":\"\",\"phoneNumber\":\"555-0100\",\"gender\":\"Male\"}"

echo "== Test 3: caretaker, email OMITTED → expect 201"
post /api/caretakers "{\"firstName\":\"WP57\",\"lastName\":\"NoEmailC-${TAG}\",\"phoneNumber\":\"555-0100\",\"isActive\":true}"

echo "== Test 4: SECOND caretaker, email whitespace → expect 201"
post /api/caretakers "{\"firstName\":\"WP57\",\"lastName\":\"NoEmailD-${TAG}\",\"email\":\"   \",\"phoneNumber\":\"555-0100\",\"isActive\":true}"

echo "== Test 5: caretaker with a real email (trimmed) → expect 201, email \"wp57-${TAG}@neurocorp.test\""
post /api/caretakers "{\"firstName\":\"WP57\",\"lastName\":\"WithEmail-${TAG}\",\"email\":\"  wp57-${TAG}@neurocorp.test \",\"phoneNumber\":\"555-0100\",\"isActive\":true}"

echo "== Test 6: duplicate of Test 5's email → expect 409 (uniqueness still enforced)"
post /api/caretakers "{\"firstName\":\"WP57\",\"lastName\":\"Dup-${TAG}\",\"email\":\"wp57-${TAG}@neurocorp.test\",\"phoneNumber\":\"555-0100\",\"isActive\":true}"

echo "== Verify (SQL): SELECT UserID, Email FROM SystemUser WHERE LastName LIKE '%-${TAG}';"
echo "   → the four NoEmail* rows have Email = NULL (not '')."
