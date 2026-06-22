#!/usr/bin/env bash
# Restores the fixed real-world decompiler corpus from #1166 and emits one
# managed assembly path per line. Run after `dotnet build src/dotnet-inspect -c
# Release` when the dotnet-inspect self corpus should be included.
set -euo pipefail

root="$(git rev-parse --show-toplevel)"
out="${1:-}"
tmp="$(mktemp -d)"
trap 'rm -rf "$tmp"' EXIT

cat > "$tmp/corpus.csproj" <<'EOF'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net11.0</TargetFramework>
    <OutputType>Exe</OutputType>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Newtonsoft.Json" Version="13.0.4" />
    <PackageReference Include="Microsoft.CodeAnalysis" Version="5.0.0" />
    <PackageReference Include="Microsoft.CodeAnalysis.CSharp" Version="5.0.0" />
    <PackageReference Include="System.CommandLine" Version="3.0.0-preview.5.26302.115" />
    <PackageReference Include="NuGet.Versioning" Version="7.3.0" />
    <PackageReference Include="Microsoft.ApplicationInsights" Version="2.23.0" />
  </ItemGroup>
</Project>
EOF

dotnet restore "$tmp/corpus.csproj" --verbosity quiet >/dev/null

packages="${NUGET_PACKAGES:-$HOME/.nuget/packages}"
declare -a assemblies=(
  "$packages/newtonsoft.json/13.0.4/lib/net6.0/Newtonsoft.Json.dll"
  "$packages/microsoft.codeanalysis.common/5.0.0/lib/net9.0/Microsoft.CodeAnalysis.dll"
  "$packages/microsoft.codeanalysis.csharp/5.0.0/lib/net9.0/Microsoft.CodeAnalysis.CSharp.dll"
  "$packages/system.commandline/3.0.0-preview.5.26302.115/lib/net10.0/System.CommandLine.dll"
  "$packages/nuget.versioning/7.3.0/lib/net8.0/NuGet.Versioning.dll"
  "$packages/microsoft.applicationinsights/2.23.0/lib/netstandard2.0/Microsoft.ApplicationInsights.dll"
)

declare -a self_assemblies=(
  "$root/artifacts/bin/DotnetInspector.Core/release/DotnetInspector.Core.dll"
  "$root/artifacts/bin/DotnetInspector.Packages/release/DotnetInspector.Packages.dll"
  "$root/artifacts/bin/DotnetInspector.Services/release/DotnetInspector.Services.dll"
  "$root/artifacts/bin/DotnetInspector.Services/release/ILInspector.Metadata.dll"
  "$root/artifacts/bin/DotnetInspector.Services/release/ILInspector.MetadataPrimitives.dll"
  "$root/artifacts/bin/dotnet-inspect/release/dotnet-inspect.dll"
  "$root/artifacts/bin/dotnet-inspect/release/ILInspector.Analysis.dll"
  "$root/artifacts/bin/dotnet-inspect/release/ILInspector.Decompiler.dll"
)
for assembly in "${self_assemblies[@]}"; do
  if [ -f "$assembly" ]; then
    assemblies+=("$assembly")
  fi
done

for assembly in "${assemblies[@]}"; do
  if [ ! -f "$assembly" ]; then
    echo "Missing corpus assembly: $assembly" >&2
    exit 1
  fi
done

if [ -n "$out" ]; then
  printf '%s\n' "${assemblies[@]}" > "$out"
else
  printf '%s\n' "${assemblies[@]}"
fi
