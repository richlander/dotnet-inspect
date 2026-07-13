#!/usr/bin/env bash
# Restores the pinned Aspire Dashboard 9.0.0 assembly used by the original
# Performance Triage investigation and emits its managed assembly path.
set -euo pipefail

out="${1:-}"
tmp="$(mktemp -d)"
trap 'rm -rf "$tmp"' EXIT
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

cat > "$tmp/corpus.csproj" <<'EOF'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net11.0</TargetFramework>
    <OutputType>Exe</OutputType>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Aspire.Dashboard.Sdk.linux-x64" Version="9.0.0" />
  </ItemGroup>
</Project>
EOF

dotnet restore "$tmp/corpus.csproj" --configfile "$root/nuget.config" --verbosity quiet >/dev/null

packages="${NUGET_PACKAGES:-$HOME/.nuget/packages}"
assembly="$packages/aspire.dashboard.sdk.linux-x64/9.0.0/tools/Aspire.Dashboard.dll"
if [ ! -f "$assembly" ]; then
  echo "Missing corpus assembly: $assembly" >&2
  exit 1
fi

if [ -n "$out" ]; then
  printf '%s\n' "$assembly" > "$out"
else
  printf '%s\n' "$assembly"
fi
