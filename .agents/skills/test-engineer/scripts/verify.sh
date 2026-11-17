#!/usr/bin/env bash
# Fast verification gate: build, unit + architecture tests, frontend typecheck.
# Usage: .agents/skills/test-engineer/scripts/verify.sh [--integration]
set -uo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../../.." && pwd)"
cd "$ROOT"

export DOTNET_CLI_HOME="${DOTNET_CLI_HOME:-/tmp}"
export DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER=1
export DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
export DOTNET_CLI_TELEMETRY_OPTOUT=1

status=0
step() {
  echo
  echo "==> $1"
  shift
  if "$@"; then echo "✅ passed"; else echo "❌ failed"; status=1; fi
}

step "dotnet build" dotnet build --no-restore -m:1 -v quiet
step "unit tests" dotnet test tests/AppointmentBooking.UnitTests --no-build -v quiet
step "architecture tests" dotnet test tests/AppointmentBooking.ArchitectureTests --no-build -v quiet

if [[ "${1:-}" == "--integration" ]]; then
  step "integration tests (needs Postgres + Redis)" dotnet test tests/AppointmentBooking.IntegrationTests --no-build -v quiet
fi

if [[ -x frontend/node_modules/.bin/tsc ]]; then
  step "frontend typecheck" ./frontend/node_modules/.bin/tsc -p frontend/tsconfig.json --noEmit
else
  echo "⚠️  frontend/node_modules missing; skipped typecheck"
fi

exit $status
