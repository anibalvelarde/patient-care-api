# WP-21B verification — patient session-history endpoints

Both endpoints are gated by `Patients.View` (ACCT/AM/FD/MGR/OWN + SYSADMIN wildcard) and return
the WP-21 `PagedResult<T>` envelope `{ items, page, pageSize, totalCount }`. Contract:
`patient-care-super/_contracts/patients-api.md`.

## Setup — mint a token (MGR or SYSADMIN via owner login)

```bash
BASE=http://localhost:5245            # deployed: http://neurocorp.k3s:30000
TOKEN=$(curl -s -X POST $BASE/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{"username":"<owner-user>","password":"<password>"}' | jq -r '.accessToken')
# NB: the field is .accessToken (NOT .token — see fix b3-verification-doc-token-field).
```

## 1. Summary list — paged, most-recent-session first

```bash
# Default page (30 rows, newest activity first; zero-session patients sort last)
curl -s -H "Authorization: Bearer $TOKEN" \
  "$BASE/api/patients/session-history" | jq '{totalCount, page, pageSize, first: .items[0]}'

# Page 2, and search by name/MRN (case-insensitive)
curl -s -H "Authorization: Bearer $TOKEN" \
  "$BASE/api/patients/session-history?page=2&pageSize=30" | jq '.items | length'
curl -s -H "Authorization: Bearer $TOKEN" \
  "$BASE/api/patients/session-history?search=bennet" | jq '.items[] | {patientName, lastSessionDate, totalSessions}'
```

**VERIFY:** `totalCount` ≈ the live patient census (866+); `items[0].lastSessionDate` is the most
recent session date in the DB; a searched patient's `totalSessions` matches
`SELECT COUNT(*) FROM TherapySession WHERE PatientID = <id>`.

## 2. Per-patient sessions — paged, newest first

```bash
PID=<patientId from step 1>
curl -s -H "Authorization: Bearer $TOKEN" \
  "$BASE/api/patients/$PID/sessions" | jq '{totalCount, page, count: (.items|length), newest: .items[0].sessionDate, oldest: .items[-1].sessionDate}'

# Page 2 + the pre-existing filters still compose
curl -s -H "Authorization: Bearer $TOKEN" \
  "$BASE/api/patients/$PID/sessions?page=2&pageSize=25" | jq '.items | length'
curl -s -H "Authorization: Bearer $TOKEN" \
  "$BASE/api/patients/$PID/sessions?status=Completed&pageSize=5" | jq '.items[].statusName'
```

**VERIFY:** items are `sessionDate DESC` (ties broken by `sessionTime`/`sessionId` DESC);
`totalCount` equals the patient's session count (or the filtered count when `status`/`isDiscovery`
is supplied); page beyond the end → `items: []` with the real `totalCount`.

## 3. Access control + ProviderAmount shaping

```bash
# No token → 401
curl -s -o /dev/null -w "%{http_code}\n" "$BASE/api/patients/session-history"

# MGR/OWN token → providerAmount present on items; FD/ACCT token → the key is absent entirely
curl -s -H "Authorization: Bearer $TOKEN" \
  "$BASE/api/patients/$PID/sessions?pageSize=1" | jq '.items[0] | has("providerAmount")'
```

**VERIFY:** 401 without a token. With an FD or ACCT token, `has("providerAmount")` is `false`
(field-shaped by `ProviderAmountResultFilter`, which unwraps the paged envelope); with MGR/OWN it
is `true`. ACCT/OWN role coverage is proven by integration tests
(`SensitiveCellAuthorizationTests`) since no ACCT/OWN users exist yet.
