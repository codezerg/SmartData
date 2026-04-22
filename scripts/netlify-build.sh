#!/usr/bin/env bash
# Netlify build: install .NET + pwsh, pack SmartData libs, generate static
# NuGet v3 feed into site/public/nuget, then build the Astro site.
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$REPO_ROOT"

FEED_BASE_URL="${FEED_BASE_URL:-https://smartdata-apis.netlify.app}"
DOTNET_CHANNEL="${DOTNET_CHANNEL:-10.0}"

export DOTNET_NOLOGO=1
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1

echo "==> Installing .NET SDK (channel $DOTNET_CHANNEL)"
curl -sSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh
bash /tmp/dotnet-install.sh --channel "$DOTNET_CHANNEL" --install-dir "$HOME/.dotnet"
export PATH="$HOME/.dotnet:$HOME/.dotnet/tools:$PATH"
export DOTNET_ROOT="$HOME/.dotnet"
dotnet --version

echo "==> Installing PowerShell (dotnet global tool)"
dotnet tool install --global PowerShell
pwsh --version

echo "==> dotnet pack SmartData.slnx -c Release"
dotnet pack SmartData.slnx -c Release

echo "==> Generating static NuGet v3 feed at $FEED_BASE_URL/nuget/v3/"
pwsh scripts/publish-feed.ps1 -BaseUrl "$FEED_BASE_URL"

echo "==> Astro build"
cd site
npm ci
npm run build

echo "==> Done. Publish dir: site/dist"
