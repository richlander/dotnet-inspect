#!/usr/bin/env bash
# Restores the pinned six-library Performance Triage corpus from #1974 and
# emits one managed assembly path per line.
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
    <PackageReference Include="Aspire.Hosting" Version="13.4.6" />
    <PackageReference Include="System.Text.Json" Version="10.0.9" />
    <PackageReference Include="Newtonsoft.Json" Version="13.0.4" />
    <PackageReference Include="Serilog" Version="4.3.1" />
    <PackageReference Include="Polly" Version="8.7.0" />
    <PackageReference Include="AutoMapper" Version="16.1.1" />
  </ItemGroup>
</Project>
EOF

dotnet restore "$tmp/corpus.csproj" --configfile "$root/nuget.config" --verbosity quiet >/dev/null

packages="${NUGET_PACKAGES:-$HOME/.nuget/packages}"

select_asset() {
  local package="$1"
  local version="$2"
  local assembly="$3"
  local base="$packages/$package/$version/lib"
  local tfm
  for tfm in net11.0 net10.0 net9.0 net8.0 net7.0 net6.0 netstandard2.1 netstandard2.0; do
    if [ -f "$base/$tfm/$assembly" ]; then
      printf '%s\n' "$base/$tfm/$assembly"
      return
    fi
  done
  echo "Missing corpus assembly: $package@$version $assembly" >&2
  exit 1
}

assemblies=(
  "$(select_asset aspire.hosting 13.4.6 Aspire.Hosting.dll)"
  "$(select_asset system.text.json 10.0.9 System.Text.Json.dll)"
  "$(select_asset newtonsoft.json 13.0.4 Newtonsoft.Json.dll)"
  "$(select_asset serilog 4.3.1 Serilog.dll)"
  "$(select_asset polly 8.7.0 Polly.dll)"
  "$(select_asset automapper 16.1.1 AutoMapper.dll)"
)

if [ -n "$out" ]; then
  printf '%s\n' "${assemblies[@]}" > "$out"
else
  printf '%s\n' "${assemblies[@]}"
fi
