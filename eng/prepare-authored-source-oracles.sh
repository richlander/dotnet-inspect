#!/usr/bin/env bash
# Restores the exact assemblies consumed by the whole-file source-oracle gate
# and writes one managed assembly path per line.
set -euo pipefail

VERSION="10.0.10"

# package ID | NuGet cache directory | package asset | assembly SHA-256
declare -a oracle_specs=(
  "System.Text.Encodings.Web|system.text.encodings.web|lib/net10.0/System.Text.Encodings.Web.dll|91f4b016890cfd5468d46d32c451931cac34096f869cc1c8077c902d9a7f5ccd"
  "System.Runtime.Serialization.Formatters|system.runtime.serialization.formatters|lib/net8.0/System.Runtime.Serialization.Formatters.dll|33693c0971e95d158efc64307e6ef379a9dc322f1642178e3c29c8e1d4db255e"
  "System.Reflection.Context|system.reflection.context|lib/net10.0/System.Reflection.Context.dll|94da27080f9aaa03e3719828976838ba39b0d8d7299fe9bd6130b1c822014f3b"
  "System.Reflection.Metadata|system.reflection.metadata|lib/net10.0/System.Reflection.Metadata.dll|2a8c49aa47e910f4e690bce79be3986d3cfb0df8d8e978bbdf51b76d594a378d"
)

out="${1:-}"
tmp="$(mktemp -d)"
trap 'rm -rf "$tmp"' EXIT

{
  cat <<EOF
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net11.0</TargetFramework>
    <OutputType>Exe</OutputType>
  </PropertyGroup>
  <ItemGroup>
EOF
  for spec in "${oracle_specs[@]}"; do
    package_id="${spec%%|*}"
    printf '    <PackageReference Include="%s" Version="%s" />\n' "$package_id" "$VERSION"
  done
  cat <<'EOF'
  </ItemGroup>
</Project>
EOF
} > "$tmp/oracles.csproj"

dotnet restore "$tmp/oracles.csproj" --verbosity quiet >&2

packages="${NUGET_PACKAGES:-$HOME/.nuget/packages}"
declare -a assemblies=()
for spec in "${oracle_specs[@]}"; do
  IFS='|' read -r package_id package_directory asset_path expected_sha256 <<< "$spec"
  assembly="$packages/$package_directory/$VERSION/$asset_path"
  if [ ! -f "$assembly" ]; then
    echo "Missing source-oracle assembly for $package_id: $assembly" >&2
    exit 1
  fi

  actual_sha256="$(sha256sum "$assembly" | awk '{print $1}')"
  if [ "$actual_sha256" != "$expected_sha256" ]; then
    echo "Source-oracle assembly SHA-256 mismatch for $package_id: $assembly" >&2
    echo "Expected: $expected_sha256" >&2
    echo "Actual:   $actual_sha256" >&2
    exit 1
  fi
  assemblies+=("$assembly")
done

if [ -n "$out" ]; then
  printf '%s\n' "${assemblies[@]}" > "$out"
else
  printf '%s\n' "${assemblies[@]}"
fi
