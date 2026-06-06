# SessionsController Split Verification — Remediation 2026, Chunk 4

The god `SessionsController` was decomposed into four focused controllers. **Every route, method,
request/response shape, and status code is unchanged** — all still under the `api/sessions` prefix.
This doc maps each endpoint to its new home and gives a curl matrix to prove no regression.

```bash
BASE=http://localhost:5245          # or http://neurocorp.k3s:30000
TOKEN=...                           # access token (all endpoints require auth — see 1B)
```

## Endpoint → controller map (routes identical to before)

| Method & Route | Now served by |
|---|---|
| `GET  /api/sessions/{date}/all` | `SessionsController` |
| `GET  /api/sessions/pastdue` | `SessionsController` |
| `POST /api/sessions` | `SessionsController` |
| `PUT  /api/sessions/{id}` | `SessionsController` |
| `GET  /api/sessions/patient/{patientId}/discovery` | `SessionsController` |
| `GET  /api/sessions/{date}/schedule-matrix?siteId=` | `ScheduleController` |
| `GET  /api/sessions/{id}/payments` | `SessionPaymentsController` |
| `GET  /api/sessions/statuses` | `BookingController` |
| `PUT  /api/sessions/{id}/confirm` | `BookingController` |
| `PUT  /api/sessions/{id}/cancel` | `BookingController` |
| `PUT  /api/sessions/{id}/complete` | `BookingController` |
| `PUT  /api/sessions/{id}/noshow` | `BookingController` |
| `PUT  /api/sessions/{id}/checkin` | `BookingController` |
| `PUT  /api/sessions/{id}/start-therapy` | `BookingController` |
| `GET  /api/sessions/unconfirmed` | `BookingController` |
| `GET  /api/sessions/upcoming` | `BookingController` |
| `GET  /api/sessions/{id}/confirmations` | `BookingController` |

All four controllers declare `[Route("api/sessions")]` explicitly (no `[controller]` token), so the
URLs are byte-identical. No action-template collisions across them.

## Curl matrix (spot-check before/after the split — expect identical results)
```bash
H="-H \"Authorization: Bearer $TOKEN\""
curl.exe -s -o /dev/null -w "all          -> %{http_code}\n" $BASE/api/sessions/2026-06-06/all          -H "Authorization: Bearer $TOKEN"
curl.exe -s -o /dev/null -w "pastdue      -> %{http_code}\n" $BASE/api/sessions/pastdue                 -H "Authorization: Bearer $TOKEN"
curl.exe -s -o /dev/null -w "discovery    -> %{http_code}\n" $BASE/api/sessions/patient/1/discovery     -H "Authorization: Bearer $TOKEN"
curl.exe -s -o /dev/null -w "schedule-mtx -> %{http_code}\n" "$BASE/api/sessions/2026-06-06/schedule-matrix?siteId=1" -H "Authorization: Bearer $TOKEN"
curl.exe -s -o /dev/null -w "payments     -> %{http_code}\n" $BASE/api/sessions/1/payments              -H "Authorization: Bearer $TOKEN"
curl.exe -s -o /dev/null -w "statuses     -> %{http_code}\n" $BASE/api/sessions/statuses                -H "Authorization: Bearer $TOKEN"
curl.exe -s -o /dev/null -w "unconfirmed  -> %{http_code}\n" $BASE/api/sessions/unconfirmed             -H "Authorization: Bearer $TOKEN"
curl.exe -s -o /dev/null -w "upcoming     -> %{http_code}\n" $BASE/api/sessions/upcoming                -H "Authorization: Bearer $TOKEN"
curl.exe -s -o /dev/null -w "confirmations-> %{http_code}\n" $BASE/api/sessions/1/confirmations         -H "Authorization: Bearer $TOKEN"
# Booking actions (PUT): confirm/cancel/complete/noshow/checkin/start-therapy on a known session id
curl.exe -s -o /dev/null -w "complete     -> %{http_code}\n" -X PUT $BASE/api/sessions/1/complete       -H "Authorization: Bearer $TOKEN"
```
**Expect:** the same status codes you got before the split (200/204/400/404 per input), and error
bodies as RFC 7807 `application/problem+json` (from Chunk 3A). Swagger should list every route exactly
as before, just grouped under the new controllers.

## Automated coverage
- Existing `SessionsControllerTests` (session-event endpoints) — unchanged behavior, pass with the
  slimmed constructor.
- `SplitSessionControllersTests` — init/DI smoke tests for the three new controllers.
- Full suite green (199 tests); build clean.
