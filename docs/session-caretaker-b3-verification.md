# B3 verification — caretaker contact info on SessionEvent

Every session endpoint now returns `caretakerName`, `caretakerPhone`, `caretakerEmail`
(primary caretaker; first link as fallback; `null` when the patient has no caretaker).
Contract: `patient-care-super/_contracts/sessions-api.md`.

```bash
# Login (MGR) and capture a token
TOKEN=$(curl -s -X POST http://localhost:5245/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{"email":"<mgr-email>","password":"<password>"}' | jq -r '.token')

# 1) Day list (feeds the Appointments views + dashboard panel)
curl -s http://localhost:5245/api/sessions/2026-07-11/all \
  -H "Authorization: Bearer $TOKEN" \
  | jq '.[0] | {sessionId, patient, caretakerName, caretakerPhone, caretakerEmail}'

# 2) Proposed/unconfirmed list (feeds the Proposed tab)
curl -s http://localhost:5245/api/sessions/unconfirmed \
  -H "Authorization: Bearer $TOKEN" \
  | jq '.[0] | {sessionId, patient, caretakerName, caretakerPhone, caretakerEmail}'

# 3) Single session
curl -s http://localhost:5245/api/sessions/1 \
  -H "Authorization: Bearer $TOKEN" \
  | jq '{sessionId, caretakerName, caretakerPhone, caretakerEmail}'
```

Expected:
- A patient with a primary caretaker → that caretaker's "Last, First" + phone + email.
- A patient whose links have no `PrimaryCaretaker=1` → the first linked caretaker.
- A patient with no caretaker links → all three fields `null` (JSON `null`, not omitted).
- Synthetic legacy placeholders (`<Name>-SH (LEGACY)`) appear by name with `null` email —
  that is correct data, not a bug.
