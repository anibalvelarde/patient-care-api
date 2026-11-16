#!/usr/bin/env bash
# WP-62B: contact & caretaker rules — curl test script (E2E findings F-5, F-6 + ruling Q-4)
#
#   Email on PUT: omitted = unchanged · "" / whitespace = CLEAR (NULL) · else trimmed;
#                 malformed → 400 (create always; PUT only when the address changes).
#   Primary caretaker: a patient keeps AT LEAST ONE primary (several allowed) —
#                 unlinking / un-flagging the last one → 409; the first link is always primary;
#                 new PUT /api/caretakers/{id}/patients/{patientId} {isPrimary}.
#
# ⚠ Creates real rows (synthetic names). Run against a LOCAL/test DB, never prod.
# Auth: MGR JWT in TOKEN (Patients.Create/Edit, Caretakers.Edit/LinkPatient). Needs jq.
#
# Usage:
#   TOKEN=$MGR_JWT ./test-scripts/wp-62b-contact-caretaker-rules.sh

set -uo pipefail

BASE_URL="${BASE_URL:-http://localhost:5245}"
TOKEN="${TOKEN:?set TOKEN to an MGR JWT}"
TAG="$(date +%s)"
PASS=0; FAIL=0

call() { # $1=method $2=path [$3=json] → sets CODE and BODY
  local out
  out=$(curl -s -w '\n%{http_code}' -X "$1" "${BASE_URL}$2" \
    -H "Authorization: Bearer ${TOKEN}" -H "Content-Type: application/json" ${3:+-d "$3"})
  CODE="${out##*$'\n'}"; BODY="${out%$'\n'*}"
}
expect() { # $1=label $2=expected code [$3=jq filter $4=expected value]
  local ok=1 got=""
  [[ "$CODE" == "$2" ]] || ok=0
  if [[ $# -ge 4 ]]; then got=$(echo "$BODY" | jq -r "$3"); [[ "$got" == "$4" ]] || ok=0; fi
  if [[ $ok == 1 ]]; then PASS=$((PASS+1)); echo "  ✅ $1"; else FAIL=$((FAIL+1)); echo "  ❌ $1 — HTTP $CODE ${got:+(got '$got')} $BODY"; fi
}

PATIENT_JSON() { # $1=email-json-fragment
  echo "{\"firstName\":\"WP62\",\"lastName\":\"Patient-${TAG}\",\"dateOfBirth\":\"1990-06-01\",${1}\"phoneNumber\":\"555-0162\",\"gender\":\"Female\",\"activeStatus\":true}"
}

echo "== Setup: one patient, two caretakers"
call POST /api/patients "{\"firstName\":\"WP62\",\"lastName\":\"Patient-${TAG}\",\"cedula\":\"WP62-${TAG}\",\"dateOfBirth\":\"1990-06-01\",\"email\":\"wp62-p-${TAG}@neurocorp.test\",\"phoneNumber\":\"555-0162\",\"gender\":\"Female\"}"
expect "create patient" 201; PID=$(echo "$BODY" | jq -r '.patientId')
call POST /api/caretakers "{\"firstName\":\"WP62\",\"lastName\":\"CareA-${TAG}\",\"phoneNumber\":\"555-0162\",\"isActive\":true}"
expect "create caretaker A" 201; CA=$(echo "$BODY" | jq -r '.caretakerId')
call POST /api/caretakers "{\"firstName\":\"WP62\",\"lastName\":\"CareB-${TAG}\",\"phoneNumber\":\"555-0162\",\"isActive\":true}"
expect "create caretaker B" 201; CB=$(echo "$BODY" | jq -r '.caretakerId')
echo "   patient=$PID caretakerA=$CA caretakerB=$CB"

echo "== F-5 email"
call POST /api/caretakers "{\"firstName\":\"WP62\",\"lastName\":\"Bad-${TAG}\",\"email\":\"not-an-email\",\"phoneNumber\":\"555-0162\",\"isActive\":true}"
expect "create with malformed email → 400" 400
call PUT "/api/patients/$PID" "$(PATIENT_JSON '')"
call GET "/api/patients/$PID"; expect "PUT without email keeps it" 200 '.email' "wp62-p-${TAG}@neurocorp.test"
call PUT "/api/patients/$PID" "$(PATIENT_JSON "\"email\":\"  WP62-Trim-${TAG}@Neurocorp.test  \",")"
call GET "/api/patients/$PID"; expect "PUT trims (case kept)" 200 '.email' "WP62-Trim-${TAG}@Neurocorp.test"
call PUT "/api/patients/$PID" "$(PATIENT_JSON '"email":"not-an-email",')"
expect "PUT changing to malformed → 400" 400
call PUT "/api/patients/$PID" "$(PATIENT_JSON '"email":"",')"
expect "PUT \"\" → 204" 204
call GET "/api/patients/$PID"; expect "… email is now null (cleared)" 200 '.email' "null"
call PUT "/api/caretakers/$CA" "{\"firstName\":\"WP62\",\"lastName\":\"CareA-${TAG}\",\"email\":\"   \",\"phoneNumber\":\"555-0162\",\"isActive\":true}"
expect "caretaker A: whitespace email → 204" 204
call PUT "/api/caretakers/$CB" "{\"firstName\":\"WP62\",\"lastName\":\"CareB-${TAG}\",\"email\":\"   \",\"phoneNumber\":\"555-0162\",\"isActive\":true}"
expect "caretaker B: whitespace email too → 204 (no 409: both NULL)" 204

echo "== F-6 primary caretaker"
call POST "/api/caretakers/$CA/patients" "{\"patientId\":$PID,\"isPrimary\":false,\"relationship\":\"Mother\"}"
expect "link A with isPrimary=false" 201
call GET "/api/patients/$PID/caretakers"; expect "… A is primary anyway (first link)" 200 ".[] | select(.caretakerId==$CA) | .isPrimaryCaretaker" "true"
call POST "/api/caretakers/$CB/patients" "{\"patientId\":$PID,\"isPrimary\":false,\"relationship\":\"Father\"}"
expect "link B as non-primary" 201
call DELETE "/api/caretakers/$CA/patients/$PID"
expect "unlink A (the only primary) → 409" 409
call PUT "/api/caretakers/$CA/patients/$PID" '{"isPrimary":false}'
expect "un-flag A (the only primary) → 409" 409
call PUT "/api/caretakers/$CB/patients/$PID" '{"isPrimary":true}'
expect "flag B primary → 204" 204
call PUT "/api/caretakers/$CA/patients/$PID" '{"isPrimary":false}'
expect "now un-flag A → 204" 204
call GET "/api/patients/$PID/caretakers"; expect "… B is the primary" 200 "[.[] | select(.isPrimaryCaretaker)] | map(.caretakerId) | join(\",\")" "$CB"
call PUT "/api/caretakers/$CA/patients/99999999" '{"isPrimary":true}'
expect "PUT on a missing link → 404" 404
call DELETE "/api/caretakers/$CA/patients/$PID"
expect "unlink A (not primary) → 204" 204

echo
echo "Result: $PASS passed, $FAIL failed"
[[ $FAIL == 0 ]]
