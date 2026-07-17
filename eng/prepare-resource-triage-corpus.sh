#!/usr/bin/env bash
# Restores the pinned ArrayPool-heavy community corpus and emits one managed
# runtime assembly path per line.
set -euo pipefail

out="${1:-}"
tmp="$(mktemp -d)"
trap 'rm -rf "$tmp"' EXIT
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
destination="$root/artifacts/resource-triage-corpus"

cat > "$tmp/corpus.csproj" <<'EOF'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net11.0</TargetFramework>
    <OutputType>Library</OutputType>
    <CopyLocalLockFileAssemblies>true</CopyLocalLockFileAssemblies>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="QuanTAlib" Version="0.1.0" />
    <PackageReference Include="System.Text.Json" Version="5.0.2" />
    <PackageReference Include="MessagePack" Version="2.5.192" />
    <PackageReference Include="MimeKit" Version="4.8.0" />
    <PackageReference Include="ZLinq" Version="1.4.9" />
    <PackageReference Include="Pipelines.Sockets.Unofficial" Version="2.2.8" />
    <PackageReference Include="Npgsql" Version="8.0.4" />
    <PackageReference Include="prometheus-net" Version="8.2.1" />
    <PackageReference Include="TouchSocket" Version="3.1.5" />
  </ItemGroup>
</Project>
EOF

rm -rf "$destination"
dotnet build "$tmp/corpus.csproj" \
  -c Release \
  --configfile "$root/nuget.config" \
  --output "$destination" \
  --verbosity quiet >/dev/null

mapfile -t assemblies < <(
  find "$destination" \
    -maxdepth 1 \
    -type f \
    -name '*.dll' \
    ! -name 'corpus.dll' \
    | sort
)

if ((${#assemblies[@]} == 0)); then
  echo "Resource Triage corpus produced no managed assemblies." >&2
  exit 1
fi

if [ -n "$out" ]; then
  printf '%s\n' "${assemblies[@]}" > "$out"
else
  printf '%s\n' "${assemblies[@]}"
fi
