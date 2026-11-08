#!/usr/bin/env bash
# WP-59B: double-booking guard — curl test script
#
# Replays E2E finding F-2 (patient-care-super/planning/audits/e2e-prerollout-2026-11-08.md) and the
# owner rulings of 2026-11-08 (every status but Cancelled occupies a slot; patient side checked too;
# hard block). Expected results are on each step.
#
# ⚠ Creates real rows. Run against a LOCAL/test DB, never prod.
# Needs: TOKEN (MGR or SYSADMIN JWT) and IDs from your local copy:
#   PATIENT_A     a patient with a caretaker link (requiresDiscovery false, or with a completed discovery)
#   PATIENT_B     another such patient
#   THERAPIST_1   a therapist holding SPECIALTY_ID
#   THERAPIST_2   another therapist holding SPECIALTY_ID
#   SPECIALTY_ID  a treatment specialty with 30- and 60-min prices (e.g. TL)
#   DAY           a free day, e.g. 2026-11-23
#
# Usage:
#   TOKEN=$JWT PATIENT_A=922 PATIENT_B=140 THERAPIST_1=3 THERAPIST_2=7 SPECIALTY_ID=6 DAY=2026-11-23 \
#   ./test-scripts/wp-59b-double-booking.sh

set -euo pipefail
BASE_URL="${BASE_URL:-http://localhost:5245}"
: "${TOKEN:?}" "${PATIENT_A:?}" "${PATIENT_B:?}" "${THERAPIST_1:?}" "${THERAPIST_2:?}" "${SPECIALTY_ID:?}" "${DAY:?}"
H=(-H "Authorization: Bearer ${TOKEN}" -H "Content-Type: application/json")
call() { curl -s -w '\nHTTP %{http_code}\n' "${H[@]}" "$@"; }
book() { # patient therapist time duration
  call -X POST "$BASE_URL/api/sessions" -d "{\"patientId\":$1,\"therapistId\":$2,\"sessionDate\":\"$DAY\",\"sessionTime\":\"$3\",\"duration\":$4,\"specialtyTypeId\":$SPECIALTY_ID,\"therapyType\":\"\",\"notes\":\"wp59 curl\"}"
}
sid() { sed -n 's/.*"sessionId":\([0-9]*\).*/\1/p' | head -1; }

echo "== 1. book A with T1 at 10:00/30 → expect 201"
S1=$(book "$PATIENT_A" "$THERAPIST_1" "10:00:00" 30 | tee /dev/stderr | sid); echo "session $S1"

echo "== 2. F-2 repro: the exact same booking again → expect 409 'Therapist … already has a session … (session #$S1)'"
book "$PATIENT_A" "$THERAPIST_1" "10:00:00" 30

echo "== 3. B with T1 at 09:30/60 (overlaps 10:00–10:30 by duration) → expect 409 (therapist side)"
book "$PATIENT_B" "$THERAPIST_1" "09:30:00" 60

echo "== 4. A with T2 at 10:15/30 (other therapist, same patient) → expect 409 'This patient already has a session …'"
book "$PATIENT_A" "$THERAPIST_2" "10:15:00" 30

echo "== 5. back-to-back: B with T1 at 10:30/30 → expect 201"
S5=$(book "$PATIENT_B" "$THERAPIST_1" "10:30:00" 30 | tee /dev/stderr | sid); echo "session $S5"

echo "== 6. B with T2 at 10:00/30 (T2 free, B free at 10:00) → expect 201"
S6=$(book "$PATIENT_B" "$THERAPIST_2" "10:00:00" 30 | tee /dev/stderr | sid); echo "session $S6"
echo "   …wait: B is 10:00–10:30 with T2 and 10:30–11:00 with T1 — back-to-back, fine."

echo "== 7. reassign S5 (B, 10:30, T1) to T2 → T2 is free at 10:30 → expect 204"
call -X PUT "$BASE_URL/api/sessions/$S5" -d "{\"sessionTime\":\"10:30:00\",\"therapistId\":$THERAPIST_2,\"discount\":0,\"notes\":\"wp59 reassign\"}"

echo "== 8. reassign S6 (B, 10:00, T2) to T1 → T1 busy 10:00–10:30 with S1 → expect 409, therapist unchanged"
call -X PUT "$BASE_URL/api/sessions/$S6" -d "{\"sessionTime\":\"10:00:00\",\"therapistId\":$THERAPIST_1,\"discount\":0,\"notes\":\"wp59 reassign\"}"

echo "== 9. lengthen S6 (B, 10:00, T2) to 60 → runs into B's 10:30 session (S5, now T2) → expect 409"
call -X PUT "$BASE_URL/api/sessions/$S6" -d "{\"sessionTime\":\"10:00:00\",\"duration\":60,\"discount\":0,\"notes\":\"wp59 longer\"}"

echo "== 10. cancel S1, then book A with T1 10:00/30 again → slot is free now → expect 201"
call -X PUT "$BASE_URL/api/sessions/$S1/cancel" -d '{"reason":"wp59 curl"}'
S10=$(book "$PATIENT_A" "$THERAPIST_1" "10:00:00" 30 | tee /dev/stderr | sid); echo "session $S10"

echo "== 11. reinstate cancelled S1 (check-in) → its slot is taken by session $S10 → expect 409"
call -X PUT "$BASE_URL/api/sessions/$S1/checkin"

echo "== 12. money-only edit on S10 (discount echo, no therapist/duration change) → expect 204 (never checked)"
call -X PUT "$BASE_URL/api/sessions/$S10" -d '{"sessionTime":"10:00:00","discount":0,"notes":"wp59 note only"}'

echo "== SQL cross-check (local DB):"
echo "   SELECT SessionID, PatientID, TherapistID, SessionTime, Duration, AppointmentStatusID FROM TherapySession"
echo "   WHERE SessionDate = '$DAY' ORDER BY SessionTime;   -- no two busy rows overlap per therapist or patient"
