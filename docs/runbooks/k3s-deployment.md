# K3s Deployment Runbook

## Prerequisites

- `kubectl` configured to talk to your K3s cluster
- `helm` v3 installed
- Docker image available at `anibalvelarde/patient-care-api`

## First-time setup

1. **Create your `.env` file** with database credentials:

   ```bash
   cd deploy/helm/patient-care-api
   cp .env.example .env
   # Edit .env with your actual database values
   ```

2. **Install the chart:**

   ```bash
   chmod +x deploy.sh
   ./deploy.sh
   ```

3. **Verify the deployment:**

   ```bash
   kubectl get pods -n nc-k3s
   kubectl get svc -n nc-k3s
   ```

4. **Access the API** from any machine on your LAN:

   ```
   http://<k3s-node-ip>:30000/api/health/checks
   ```

## Upgrading to a new image version

When GitHub Actions publishes a new image (e.g., tag `54-1`):

```bash
cd deploy/helm/patient-care-api
./deploy.sh .env 54-1
```

Or directly with helm:

```bash
helm upgrade patient-care-api deploy/helm/patient-care-api \
  --set image.tag=54-1
```

## Checking logs

```bash
kubectl logs -n nc-k3s -l app.kubernetes.io/name=patient-care-api --tail=100
```

## Uninstalling

```bash
helm uninstall patient-care-api
kubectl delete namespace nc-k3s
```

## Troubleshooting

| Symptom | Check |
|---|---|
| Pod in `CrashLoopBackOff` | `kubectl logs -n nc-k3s <pod-name>` — likely bad DB credentials |
| Pod stuck in `Pending` | `kubectl describe pod -n nc-k3s <pod-name>` — resource constraints |
| Health probes failing | Verify the DB is reachable from the K3s node network |
| NodePort not accessible | Ensure firewall allows traffic on port 30000 |
| Namespace stuck in `Terminating` | See "Stuck Namespaces" section below |
| Pod stuck in `ContainerCreating` | Likely containerd issue — see "Containerd Reset" below |

### Stuck Namespaces

If a namespace is stuck in `Terminating`, check for orphaned pods:

```bash
kubectl get pods -n <namespace> --no-headers
```

Force-delete any stuck pods, then remove the namespace finalizer:

```bash
kubectl delete pod <pod-name> -n <namespace> --force --grace-period=0
kubectl proxy --port=8099 &
curl -s -X PUT -H "Content-Type: application/json" \
  -d "$(kubectl get namespace <namespace> -o json | jq '.spec.finalizers = []')" \
  http://localhost:8099/api/v1/namespaces/<namespace>/finalize
kill %1
```

### Containerd Reset

If pods are stuck in `ContainerCreating` with snapshotter errors, SSH into the K3s node and reset containerd. Note: K3s nodes run Alpine Linux, so use `rc-service` (not `systemctl`):

```bash
sudo rc-service k3s stop
sudo rm -rf /var/lib/rancher/k3s/agent/containerd/
sudo rc-service k3s start
```

This wipes the containerd state and forces K3s to re-pull all images on startup.
