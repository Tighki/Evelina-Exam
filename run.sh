#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "$0")" && pwd)"
export DOTNET_ROOT="$ROOT/.dotnet"
export PATH="$DOTNET_ROOT:$PATH"
export NUGET_PACKAGES="$ROOT/.nuget"
export DOTNET_CLI_HOME="$ROOT/.dotnet-home"
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1
cd "$ROOT/RestaurantOrders"
if [[ "${1:-}" == "test" ]]; then
  dotnet run -- --self-test
else
  dotnet run
fi
