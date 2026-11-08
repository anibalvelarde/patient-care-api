#!/usr/bin/env bash
# WP-60B: dates & time zones — curl test script
#
# Findings from patient-care-super/planning/audits/e2e-prerollout-2026-11-08.md, expected FIXED:
#   F-15a change-log times carry a trailing Z (UTC instant)            → browsers show the wall-clock time
#   F-15b changedFields lists only columns whose value changed
#   F-19  "today" defaults are the PANAMA date (try it after 7 PM Panama)
#   Q-3   schedule matrix for a Sunday returns the COMING week
#
# Read-mostly; step 4 edits one session's notes. Run against a LOCAL/test DB.
# Needs: TOKEN (SYSADMIN JWT — the change log is SYSADMIN-only), SITE_ID, SESSION_ID (any session).
# Usage: TOKEN=$JWT SITE_ID=1 SESSION_ID=1 ./test-scripts/wp-60b-dates-timezones.sh

set -euo pipefail
BASE_URL="${BASE_URL:-http://localhost:5245}"
: "${TOKEN:?}" "${SITE_ID:?}" "${SESSION_ID:?}"
H=(-H "Authorization: Bearer ${TOKEN}" -H "Content-Type: application/json")

echo "== Q-3: schedule matrix for Sunday 2026-11-08 → expect weekStart 2026-11-09 (the coming Monday)"
curl -s "${H[@]}" "$BASE_URL/api/sessions/2026-11-08/schedule-matrix?siteId=$SITE_ID" | grep -o '"weekStart":"[^"]*"'
echo "== Q-3: Thursday 2026-11-12 → expect weekStart 2026-11-09"
curl -s "${H[@]}" "$BASE_URL/api/sessions/2026-11-12/schedule-matrix?siteId=$SITE_ID" | grep -o '"weekStart":"[^"]*"'

echo "== F-19: an unparseable route date falls back to TODAY in Panama → expect today's Panama date (after 7 PM too)"
echo "   UTC now: $(date -u '+%Y-%m-%d %H:%M') (Panama = UTC−5; after 19:00 Panama the UTC date is already tomorrow)"
curl -s "${H[@]}" "$BASE_URL/api/sessions/not-a-date/schedule-matrix?siteId=$SITE_ID" | grep -o '"weekStart":"[^"]*"'

echo "== F-15b: edit ONLY the notes of session $SESSION_ID"
curl -s -o /dev/null -w "HTTP %{http_code}\n" "${H[@]}" -X PUT "$BASE_URL/api/sessions/$SESSION_ID" \
  -d "{\"sessionTime\":\"10:00:00\",\"therapyType\":\"TL\",\"discount\":0,\"notes\":\"wp60 $(date +%s)\"}"
echo "   (use a TL row with discount 0: the PUT also writes therapyType + discount — an omitted therapyType blanks it, pre-existing, logged as a finding)"

echo "== F-15a/b: newest change-log entry → expect occurredAtUtc ending in Z, changedFields = [\"Notes\"]"
curl -s "${H[@]}" "$BASE_URL/api/admin/change-log?page=1&pageSize=1" | grep -oE '"occurredAtUtc":"[^"]*"|"changedFields":\[[^]]*\]'
