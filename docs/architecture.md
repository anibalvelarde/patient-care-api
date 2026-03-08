# Architecture

## Overview

The Patient Care API follows **clean architecture** (Onion Architecture). Dependencies flow inward: the Web layer depends on Core, Infrastructure depends on Core, but Core depends on nothing external.

```
┌─────────────────────────────────────────┐
│  Web (ASP.NET Core controllers, CORS,   │
│       Swagger, health checks)           │
│  ┌───────────────────────────────────┐  │
│  │  Infrastructure (EF Core,        │  │
│  │  MySQL repositories, DbContext)   │  │
│  │  ┌─────────────────────────────┐  │  │
│  │  │  Core (Entities, DTOs,      │  │  │
│  │  │  interfaces, services)      │  │  │
│  │  └─────────────────────────────┘  │  │
│  └───────────────────────────────────┘  │
└─────────────────────────────────────────┘
```

## Domain Model

The system manages a therapy clinic with these core concepts:

- **Patient** — A person receiving therapy. Has demographics (DOB, gender, MRN) and zero-or-more caretakers.
- **Therapist** — A person providing therapy sessions.
- **Caretaker** — A person responsible for a patient (parent, guardian). Linked via the `PatientCaretaker` join entity, which tracks the primary caretaker.
- **TherapySession** — A scheduled session between a patient and therapist on a given date/time. Tracks billing (amount, amount paid, discount, provider amount, gross profit) and has a 35-day past-due rule.
- **Payment / SessionPayment** — Payment tracking for sessions.
- **User / UserRole** — System user identity and role assignments.

## Key Relationships

```
Patient ──┬── TherapySession ──── Therapist
           │
           └── PatientCaretaker ── Caretaker

User ──── UserRole
```

## Layers in Detail

### Core (`src/Core/`)

- **Entities** — EF Core-mapped domain objects. `PersonBase` for people, `AuditableEntityBase` for audit timestamps.
- **BusinessObjects** — DTOs organized by domain area: `Patients/`, `Therapists/`, `Sessions/`, `Common/`. Each area has Profile, Request, and UpdateRequest types.
- **Interfaces** — `IRepository<T>` base plus specific repository and service interfaces.
- **Services** — Business logic. Profile services handle CRUD aggregation. `SessionEventHandler` manages session lifecycle including past-due calculations.

### Infrastructure (`src/Infrastructure/`)

- **Data** — `ApplicationDbContext` with Fluent API mappings, automatic audit timestamps on `SaveChanges`.
- **Repositories** — `EfRepository<T>` generic base; specific repositories for complex queries.
- **Health Checks** — Custom DB connectivity check.

### Web (`src/Web/`)

- **Controllers** — RESTful controllers for Patients, Therapists, Caretakers, Sessions, and Health.
- **Startup** — CORS policies (Neurocorp domains + localhost), Swagger, health check endpoints, DI wiring.
- **Health endpoints** — Kubernetes-style probes: `/api/health/live`, `/ready`, `/startup`, `/checks`.

## Configuration

Database connection is built from environment variables at startup (not from `appsettings.json` connection strings by default). See `Infrastructure/Configurations/Dependencies.cs` for the connection string template.

## CI/CD

- **PR Validation** — Build + test on every PR to `main`.
- **Main Build** — Full pipeline producing a versioned Docker image on push to `main`.
