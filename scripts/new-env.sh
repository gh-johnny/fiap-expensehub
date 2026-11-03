#!/usr/bin/env bash
# Creates .env from .env.example, replacing each CHANGE_ME with an independent random secret.
set -euo pipefail
cd "$(dirname "$0")/.."

if [[ -f .env ]]; then
  echo ".env already exists; leaving it untouched."
  exit 0
fi

while IFS= read -r line; do
  while [[ "$line" == *CHANGE_ME* ]]; do
    secret="$(head -c 24 /dev/urandom | base64 | tr -d '/+=')aA1!"
    line="${line/CHANGE_ME/$secret}"
  done
  printf '%s\n' "$line"
done < .env.example > .env

chmod 600 .env
echo "Created .env with random local secrets."
