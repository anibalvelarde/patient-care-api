# WP-30B (U2) — Paged Lists + Lookup Typeahead: curl verification

Endpoints changed/added (contract: `patient-care-super/_contracts/patients-api.md` +
`caretakers-api.md`):

- `GET /api/patients` — ⚠ **BREAKING**: now `PagedResult<PatientProfile>` (was bare array)
- `GET /api/caretakers` — ⚠ **BREAKING**: now `PagedResult<CaretakerProfile>` (was bare array)
- `GET /api/patients/lookup?q=` — NEW typeahead (cap 20)
- `GET /api/caretakers/lookup?q=` — NEW typeahead (cap 20)

**Deploy rule:** API and UI (WP-30C) ship as ONE event — old UI breaks against this API and
vice versa.

## Setup

```bash
API=http://neurocorp.k3s:30000   # or http://localhost:5245 for a local run
# Login field is `email`, NOT `username` (WP-22 lesson):
TOKEN=$(curl -s -X POST $API/api/auth/login \
  -H 'Content-Type: application/json' \
  -d '{"email":"<user>","password":"<pass>"}' | jq -r .accessToken)
AUTH="Authorization: Bearer $TOKEN"
```

## Paged patients list

```bash
# 1) Default page: envelope shape, 30 items, totalCount ≈ 869
curl -s -H "$AUTH" "$API/api/patients" | jq '{page, pageSize, totalCount, items: (.items|length), first: .items[0].patientName}'

# 2) Page N + stability: no overlap with page 1, same totalCount
curl -s -H "$AUTH" "$API/api/patients?page=2" | jq '{page, totalCount, first: .items[0].patientName}'

# 3) Search hit — name, MRN, and cedula|passport all match (gate G2)
curl -s -H "$AUTH" "$API/api/patients?search=L24-0001" | jq '{totalCount, names: [.items[].patientName]}'

# 4) Search miss — empty items, totalCount 0 (never an error)
curl -s -H "$AUTH" "$API/api/patients?search=zzzznotapatient" | jq '{totalCount, items: (.items|length)}'

# 5) isActive tabs
curl -s -H "$AUTH" "$API/api/patients?isActive=true"  | jq .totalCount
curl -s -H "$AUTH" "$API/api/patients?isActive=false" | jq .totalCount
# (the two must sum to the unfiltered totalCount)

# 6) pageSize clamp — 100000 silently clamps to 100 (never 400)
curl -s -H "$AUTH" "$API/api/patients?pageSize=100000" | jq '{pageSize, items: (.items|length)}'

# 7) Ordering: by name, ascending
curl -s -H "$AUTH" "$API/api/patients?pageSize=5" | jq '[.items[].patientName]'
```

## Paged caretakers list

```bash
curl -s -H "$AUTH" "$API/api/caretakers" | jq '{page, pageSize, totalCount, items: (.items|length)}'
curl -s -H "$AUTH" "$API/api/caretakers?search=@" | jq .totalCount          # email search (gate G2)
curl -s -H "$AUTH" "$API/api/caretakers?isActive=true&page=2" | jq '{page, totalCount}'
```

## Lookup typeahead (cap 20, slim shape)

```bash
# ≤ 20 rows, only {patientId, patientName, medicalRecordNumber}
curl -s -H "$AUTH" "$API/api/patients/lookup?q=a" | jq 'length, .[0]'

# caretakers: {caretakerId, caretakerName}
curl -s -H "$AUTH" "$API/api/caretakers/lookup?q=a" | jq 'length, .[0]'

# q required — 400 ProblemDetails, not a full-census dump
curl -s -o /dev/null -w '%{http_code}\n' -H "$AUTH" "$API/api/patients/lookup"
curl -s -o /dev/null -w '%{http_code}\n' -H "$AUTH" "$API/api/caretakers/lookup?q=%20"
```

## Access control (unchanged — no matrix change, hash `7ae3aa5e3274`)

```bash
# Unauthenticated → 401 on all four
for p in patients caretakers patients/lookup?q=a caretakers/lookup?q=a; do
  curl -s -o /dev/null -w "$p %{http_code}\n" "$API/api/$p"
done
```

## Expected results summary

| Check | Expect |
|---|---|
| `GET /api/patients` | `{items[30], page:1, pageSize:30, totalCount:~869}` |
| `?search=<MRN\|cedula\|name>` | matching rows only, count = matches |
| `?isActive=true` + `?isActive=false` | counts sum to unfiltered total |
| `?pageSize=100000` | `pageSize:100`, 100 items |
| `lookup?q=a` | ≤20 slim rows |
| `lookup` (no q) | `400` |
| all four unauthenticated | `401` |
