#!/usr/bin/env bash
# Restores the fixed real-world decompiler corpus from #1166 and emits one
# managed assembly path per line.
#
# Every assembly is a PINNED published package version, so the corpus is a
# stable target: the only thing that varies across a decompiler change is the
# tool, never the input. The dotnet-inspect self-assemblies come from the
# published, non-AOT `dotnet-inspect.any` package (its managed IL under
# tools/net10.0/any/) rather than the local `artifacts/bin` build — that removes
# corpus drift (#1404) and breaks the circularity where a decompiler change
# rebuilt both the tool and its own corpus at once. Bump SELF_VERSION (and
# re-emit the baseline) to advance the self-corpus deliberately.
set -euo pipefail

# Pinned dotnet-inspect.any version supplying the self-corpus managed assemblies.
SELF_VERSION="0.14.0"
# The TFM folder the non-AOT tool package ships its managed assemblies under
# (the release "reach" floor; see release.yml validate-reach-packaging).
SELF_TFM="net10.0"

out="${1:-}"
tmp="$(mktemp -d)"
trap 'rm -rf "$tmp"' EXIT

cat > "$tmp/corpus.csproj" <<EOF
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
    <PackageReference Include="dotnet-inspect.any" Version="$SELF_VERSION" />
  </ItemGroup>
</Project>
EOF

dotnet restore "$tmp/corpus.csproj" --verbosity quiet >/dev/null

packages="${NUGET_PACKAGES:-$HOME/.nuget/packages}"
self="$packages/dotnet-inspect.any/$SELF_VERSION/tools/$SELF_TFM/any"

declare -a assemblies=(
  "$packages/newtonsoft.json/13.0.4/lib/net6.0/Newtonsoft.Json.dll"
  "$packages/microsoft.codeanalysis.common/5.0.0/lib/net9.0/Microsoft.CodeAnalysis.dll"
  "$packages/microsoft.codeanalysis.csharp/5.0.0/lib/net9.0/Microsoft.CodeAnalysis.CSharp.dll"
  "$packages/system.commandline/3.0.0-preview.5.26302.115/lib/net10.0/System.CommandLine.dll"
  "$packages/nuget.versioning/7.3.0/lib/net8.0/NuGet.Versioning.dll"
  "$packages/microsoft.applicationinsights/2.23.0/lib/netstandard2.0/Microsoft.ApplicationInsights.dll"
  # dotnet-inspect self-corpus, from the pinned published `any` package.
  "$self/DotnetInspector.Core.dll"
  "$self/DotnetInspector.Packages.dll"
  "$self/DotnetInspector.Services.dll"
  "$self/ILInspector.Metadata.dll"
  "$self/ILInspector.MetadataPrimitives.dll"
  "$self/dotnet-inspect.dll"
  "$self/ILInspector.Analysis.dll"
  "$self/ILInspector.Decompiler.dll"
)

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
