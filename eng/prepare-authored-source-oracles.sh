#!/usr/bin/env bash
# Restores the exact assemblies consumed by the whole-file source-oracle gate
# and writes one managed assembly path per line.
set -euo pipefail

VERSION="10.0.10"
TFM="net10.0"
ASSEMBLY_SHA256="91f4b016890cfd5468d46d32c451931cac34096f869cc1c8077c902d9a7f5ccd"

out="${1:-}"
tmp="$(mktemp -d)"
trap 'rm -rf "$tmp"' EXIT

cat > "$tmp/oracles.csproj" <<EOF
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net11.0</TargetFramework>
    <OutputType>Exe</OutputType>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="System.Text.Encodings.Web" Version="$VERSION" />
  </ItemGroup>
</Project>
EOF

dotnet restore "$tmp/oracles.csproj" --verbosity quiet >&2

packages="${NUGET_PACKAGES:-$HOME/.nuget/packages}"
assembly="$packages/system.text.encodings.web/$VERSION/lib/$TFM/System.Text.Encodings.Web.dll"
if [ ! -f "$assembly" ]; then
  echo "Missing source-oracle assembly: $assembly" >&2
  exit 1
fi

actual_sha256="$(sha256sum "$assembly" | awk '{print $1}')"
if [ "$actual_sha256" != "$ASSEMBLY_SHA256" ]; then
  echo "Source-oracle assembly SHA-256 mismatch: $assembly" >&2
  echo "Expected: $ASSEMBLY_SHA256" >&2
  echo "Actual:   $actual_sha256" >&2
  exit 1
fi

if [ -n "$out" ]; then
  printf '%s\n' "$assembly" > "$out"
else
  printf '%s\n' "$assembly"
fi
