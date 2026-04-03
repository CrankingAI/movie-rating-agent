#!/usr/bin/env bash
set -euo pipefail

if docker info &>/dev/null; then
  echo "Docker is already running."
  exit 0
fi

echo "==> Starting Docker Desktop..."
open -a Docker
echo "    Waiting for Docker daemon..."
until docker info &>/dev/null; do sleep 2; done
echo "==> Docker is ready."
