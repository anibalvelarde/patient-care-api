# Runbook: Local Development Setup

## Prerequisites

- .NET 8 SDK installed
- Access to a MySQL instance (local or remote)

## Steps

### 1. Set environment variables

The API reads database credentials from environment variables. Set them for your shell session:

```bash
export DATABASE_HOST=localhost
export DATABASE_PORT=3306
export DATABASE_NAME=neurocorp_patient_care
export DATABASE_USER=your_user
export DATABASE_PASSWORD=your_password
```

Or create a `.env` file (git-ignored) and source it:

```bash
source .env
```

### 2. Restore and build

```bash
dotnet restore patient-care-api.sln
dotnet build patient-care-api.sln
```

### 3. Run tests

```bash
dotnet test patient-care-api.sln
```

### 4. Start the API

```bash
dotnet run --project src/Web/Web.csproj
```

The API starts on `http://localhost:5245`. Swagger UI is available at `/swagger` in the Development environment.

### 5. Docker alternative

```bash
docker build -f build/Dockerfile -t patient-care-api .
docker run -p 5245:5245 \
  -e DATABASE_HOST=host.docker.internal \
  -e DATABASE_PORT=3306 \
  -e DATABASE_NAME=neurocorp_patient_care \
  -e DATABASE_USER=your_user \
  -e DATABASE_PASSWORD=your_password \
  patient-care-api
```

## Troubleshooting

| Symptom | Likely cause | Fix |
|---|---|---|
| `MySqlException` on startup | Missing or wrong env vars | Verify `DATABASE_*` env vars are set |
| Port 5245 in use | Another instance running | Stop the other process or change port in `launchSettings.json` |
| Tests fail with DB errors | Integration tests need a live DB | `Infrastructure.Tests` may need a running MySQL instance |
