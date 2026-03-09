# ADR-002: Helm Chart for K3s Deployment

## Status

Accepted

## Date

2026-03-08

## Context

The Patient Care API is built and published as a Docker image via GitHub Actions. To run it on a local K3s cluster, we need a repeatable, version-controlled deployment mechanism that manages Kubernetes resources (Deployment, Service, Secret, Namespace) and supports easy upgrades when new image versions are published.

## Decision

Add a Helm chart at `deploy/helm/patient-care-api/` that defines:

1. **Namespace** — `nc-k3s`, created by the chart.
2. **Secret** — Database credentials (`DATABASE_HOST`, `DATABASE_PORT`, `DATABASE_NAME`, `DATABASE_USER`, `DATABASE_PASSWORD`) managed as a Kubernetes Secret, injected into the pod via `envFrom`.
3. **Deployment** — Single replica with `imagePullPolicy: Always`, using the existing health endpoints (`/api/health/live`, `/api/health/ready`, `/api/health/startup`) for liveness, readiness, and startup probes.
4. **Service** — NodePort exposing the API on port 30000 for LAN access.
5. **Deploy script** — A `deploy.sh` wrapper that reads credentials from a `.env` file (git-ignored) so secrets never appear in command history or version control.

Image upgrades follow a semi-automatic workflow: after GitHub Actions publishes a new image tag, run `./deploy.sh .env <new-tag>` or `helm upgrade --set image.tag=<new-tag>`.

## Deploying a New Instance

1. **Prepare credentials** — copy the example env file and fill in your database values:

   ```bash
   cd deploy/helm/patient-care-api
   cp .env.example .env
   # Edit .env with actual DATABASE_HOST, DATABASE_PORT, DATABASE_NAME,
   # DATABASE_USER, and DATABASE_PASSWORD values
   ```

2. **Install the chart** — the deploy script reads `.env` and runs `helm upgrade --install`:

   ```bash
   chmod +x deploy.sh
   ./deploy.sh
   ```

   Or install directly with Helm:

   ```bash
   helm upgrade --install patient-care-api deploy/helm/patient-care-api \
     --set database.host=<host> \
     --set database.port=<port> \
     --set database.name=<db-name> \
     --set database.user=<user> \
     --set database.password=<password>
   ```

3. **Verify** — confirm the pod is running and healthy:

   ```bash
   kubectl get pods -n nc-k3s
   kubectl get svc -n nc-k3s
   curl http://<k3s-node-ip>:30000/api/health/checks
   ```

## Upgrading an Existing Instance

When GitHub Actions publishes a new Docker image tag (e.g., `54-1`):

1. **Using the deploy script** (recommended):

   ```bash
   cd deploy/helm/patient-care-api
   ./deploy.sh .env 54-1
   ```

2. **Using Helm directly** (if credentials haven't changed):

   ```bash
   helm upgrade patient-care-api deploy/helm/patient-care-api \
     --set image.tag=54-1
   ```

3. **Verify the rollout:**

   ```bash
   kubectl rollout status deployment -n nc-k3s -l app.kubernetes.io/name=patient-care-api
   kubectl get pods -n nc-k3s
   ```

4. **Roll back** if something goes wrong:

   ```bash
   helm rollback patient-care-api
   ```

## Consequences

- Deployment is repeatable and version-controlled alongside the application code.
- Secrets stay out of the repo (`.env` is git-ignored; only `.env.example` is committed).
- Health probes give K3s visibility into application state for automatic restarts.
- Upgrading to a new image version is a single command.
- Future enhancement: could add ArgoCD/Flux for fully automated GitOps deployments.
