# API Reference

## Base URL

`http://localhost:5245/api` (Development)

## Resources

### Patients — `/api/patients`

| Method | Route | Description | Request Body | Response |
|---|---|---|---|---|
| GET | `/api/patients` | List all patients | — | `PatientProfile[]` |
| GET | `/api/patients/{id}` | Get patient by ID | — | `PatientProfile` |
| GET | `/api/patients/{id}/pastdue` | Past-due billing summary for a patient | — | `PatientPastDueInfo` |
| POST | `/api/patients` | Create a new patient | `PatientProfileRequest` | `PatientProfile` (201) |
| PUT | `/api/patients/{id}` | Update an existing patient | `PatientProfileUpdateRequest` | 204 No Content |

### Therapists — `/api/therapists`

| Method | Route | Description | Request Body | Response |
|---|---|---|---|---|
| GET | `/api/therapists` | List all therapists | — | `TherapistProfile[]` |
| GET | `/api/therapists/{id}` | Get therapist by ID | — | `TherapistProfile` |
| GET | `/api/therapists/{id}/pastdue` | Past-due billing summary for a therapist | — | `TherapistPastDueInfo` |
| POST | `/api/therapists` | Create a new therapist | `TherapistProfileRequest` | `TherapistProfile` (201) |
| PUT | `/api/therapists/{id}` | Update an existing therapist | `TherapistProfileUpdateRequest` | 204 No Content |

### Caretakers — `/api/caretakers`

| Method | Route | Description | Request Body | Response |
|---|---|---|---|---|
| GET | `/api/caretakers` | List all caretakers | — | `CaretakerProfile[]` |
| GET | `/api/caretakers/{id}` | Get caretaker by ID | — | `CaretakerProfile` |
| POST | `/api/caretakers` | Create a new caretaker | `CaretakerProfileRequest` | `CaretakerProfile` (201) |
| PUT | `/api/caretakers/{id}` | Update an existing caretaker | `CaretakerProfileUpdateRequest` | 204 No Content |

### Sessions — `/api/sessions`

| Method | Route | Description | Request Body | Response |
|---|---|---|---|---|
| GET | `/api/sessions/{date}/all` | List all sessions for a date (format: `yyyy-MM-dd`) | — | `SessionEvent[]` |
| GET | `/api/sessions/pastdue` | List all past-due sessions | — | `SessionEvent[]` |
| POST | `/api/sessions` | Create a new session | `SessionEventRequest` | `SessionEvent` (201) |
| PUT | `/api/sessions/{id}` | Update an existing session | `SessionEventUpdateRequest` | 204 No Content |

### Health — `/api/health`

| Method | Route | Description |
|---|---|---|
| GET | `/api/health/live` | Liveness probe — is the process running? |
| GET | `/api/health/ready` | Readiness probe — is the app ready for traffic? |
| GET | `/api/health/startup` | Startup probe — has initialization completed? |
| GET | `/api/health/checks` | Full health report including DB connectivity |

## Business Rules

- **Past-due threshold**: A session is considered past-due when payment is outstanding and > 35 days have elapsed since the session date.
- **Amount due**: `Amount - DiscountAmount - AmountPaid`
- **CORS**: Allows origins matching `*.neurocorp.*` and `localhost`.
- **Swagger UI**: Available at `/swagger` in the Development environment only.
