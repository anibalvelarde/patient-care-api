#!/usr/bin/env bash
# deploy.sh — Install or upgrade the Patient Care API Helm chart
# Reads database credentials from a .env file so you never need --set flags.
#
# Usage:
#   ./deploy.sh                    # uses .env in this directory
#   ./deploy.sh /path/to/.env      # uses a custom .env file
#   ./deploy.sh .env 54-1          # override image tag

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
CHART_DIR="$SCRIPT_DIR"

ENV_FILE="${1:-$SCRIPT_DIR/.env}"
IMAGE_TAG="${2:-}"

if [ ! -f "$ENV_FILE" ]; then
  echo "Error: .env file not found at $ENV_FILE"
  echo "Copy .env.example to .env and fill in your values."
  exit 1
fi

# Source the .env file
set -a
source "$ENV_FILE"
set +a

# Build the helm command
HELM_CMD="helm upgrade --install patient-care-api $CHART_DIR"
HELM_CMD="$HELM_CMD --set database.host=$DATABASE_HOST"
HELM_CMD="$HELM_CMD --set database.port=$DATABASE_PORT"
HELM_CMD="$HELM_CMD --set database.name=$DATABASE_NAME"
HELM_CMD="$HELM_CMD --set database.user=$DATABASE_USER"
HELM_CMD="$HELM_CMD --set database.password=$DATABASE_PASSWORD"

if [ -n "$IMAGE_TAG" ]; then
  HELM_CMD="$HELM_CMD --set image.tag=$IMAGE_TAG"
fi

# ────────────────────────────────────────────────────────────────
echo "============================================================="
echo "BEFORE deployment"
echo "Namespace: nc-k3s"
echo "-------------------------------------------------------------"
kubectl get pods -n nc-k3s -o wide--no-headers 2>/dev/null | sort || echo "(no pods found or namespace empty)"
echo ""

echo "Deploying Patient Care API..."
echo "Command: $HELM_CMD"
eval "$HELM_CMD"

echo ""
echo "Waiting 10 seconds for Kubernetes to start reconciling changes..."
sleep 10

echo ""
echo "============================================================="
echo "AFTER deployment"
echo "Namespace: nc-k3s"
echo "-------------------------------------------------------------"
kubectl get pods -n nc-k3s -o wide --no-headers 2>/dev/null | sort || echo "(no pods found or namespace empty)"
echo "============================================================="

echo ""
echo "Done. Compare the two lists above."
echo "Tip: run 'kubectl get pods -n nc-k3s -w' in another terminal to watch live updates."