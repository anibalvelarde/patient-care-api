#!/usr/bin/env bash
# deploy-remote.sh — Deploy Patient Care API via Cloudflare Tunnel (off-LAN)
# Requires remote-connect.sh to be running first.
#
# Usage:
#   ./deploy-remote.sh                    # uses .env in this directory
#   ./deploy-remote.sh /path/to/.env      # uses a custom .env file
#   ./deploy-remote.sh .env 54-1          # override image tag

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
CHART_DIR="$SCRIPT_DIR"
REPO_ROOT="$(cd "$SCRIPT_DIR/../../../.." && pwd)"

ENV_FILE="${1:-$SCRIPT_DIR/.env}"
IMAGE_TAG="${2:-}"

# ── Load tunnel config ──────────────────────────────────────────
CONF_FILE="$REPO_ROOT/remote-tunnel.conf"
if [ ! -f "$CONF_FILE" ]; then
  echo "Error: $CONF_FILE not found."
  echo "Copy remote-tunnel.conf.example to remote-tunnel.conf and fill in your values."
  exit 1
fi
source "$CONF_FILE"

# ── Load database credentials ───────────────────────────────────
if [ ! -f "$ENV_FILE" ]; then
  echo "Error: .env file not found at $ENV_FILE"
  echo "Copy .env.example to .env and fill in your values."
  exit 1
fi

set -a
source "$ENV_FILE"
set +a

# ── Set remote kubeconfig ───────────────────────────────────────
export KUBECONFIG="$KUBECONFIG_REMOTE"

if [ ! -f "$KUBECONFIG" ]; then
  echo "Error: Remote kubeconfig not found at $KUBECONFIG"
  echo "See kubeconfig-remote.template in the repo root."
  exit 1
fi

# ── Verify tunnel is up ─────────────────────────────────────────
echo "Checking K3s connectivity via tunnel..."
if ! kubectl get nodes --request-timeout=10s > /dev/null 2>&1; then
  echo "Error: Cannot reach K3s cluster. Is remote-connect.sh running?"
  exit 1
fi
echo "  K3s cluster reachable."
echo ""

# Fail early if the JWT signing key is missing — the API crash-loops without it outside Development.
if [ -z "${JWT_SIGNING_KEY:-}" ]; then
  echo "Error: JWT_SIGNING_KEY is not set. Generate one with 'openssl rand -base64 48' and add"
  echo "  'JWT_SIGNING_KEY=<value>' to your .env."
  exit 1
fi

# ── Build helm command ───────────────────────────────────────────
HELM_CMD="helm upgrade --install patient-care-api $CHART_DIR"
HELM_CMD="$HELM_CMD --set database.host=$DATABASE_HOST"
HELM_CMD="$HELM_CMD --set database.port=$DATABASE_PORT"
HELM_CMD="$HELM_CMD --set database.name=$DATABASE_NAME"
HELM_CMD="$HELM_CMD --set database.user=$DATABASE_USER"
HELM_CMD="$HELM_CMD --set database.password=$DATABASE_PASSWORD"
HELM_CMD="$HELM_CMD --set jwt.signingKey=$JWT_SIGNING_KEY"

if [ -n "$IMAGE_TAG" ]; then
  HELM_CMD="$HELM_CMD --set image.tag=$IMAGE_TAG"
fi

# ── Deploy ───────────────────────────────────────────────────────
echo "============================================================="
echo "BEFORE deployment (remote)"
echo "Namespace: nc-k3s"
echo "-------------------------------------------------------------"
kubectl get pods -n nc-k3s -o wide --no-headers 2>/dev/null | sort || echo "(no pods found or namespace empty)"
echo ""

echo "Deploying Patient Care API (via tunnel)..."
echo "Command: $HELM_CMD"
eval "$HELM_CMD"

echo ""
echo "Waiting 10 seconds for Kubernetes to start reconciling changes..."
sleep 10

echo ""
echo "============================================================="
echo "AFTER deployment (remote)"
echo "Namespace: nc-k3s"
echo "-------------------------------------------------------------"
kubectl get pods -n nc-k3s -o wide --no-headers 2>/dev/null | sort || echo "(no pods found or namespace empty)"
echo "============================================================="

echo ""
echo "Done. Compare the two lists above."
echo "Tip: run 'kubectl --kubeconfig $KUBECONFIG get pods -n nc-k3s -w' to watch live updates."
echo "API health: $CF_HOSTNAME_API/api/health/checks"
