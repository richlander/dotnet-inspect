#!/usr/bin/env bash
# Regenerates the prototype Library symbol document for one nuget.org package.
# Usage: generate.sh <work-dir> [package-id] [version] [tfm]
# Downloads the package from nuget.org and its portable PDB from the Microsoft
# symbol server into <work-dir>, then writes <work-dir>/<id>.symbols.json.
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
work="$1"; id="${2:-System.Text.Json}"; version="${3:-10.0.14}"; tfm="${4:-net10.0}"
lower="$(printf '%s' "$id" | tr '[:upper:]' '[:lower:]')"
mkdir -p "$work/pkg"
curl -fsSL -o "$work/package.nupkg" "https://api.nuget.org/v3-flatcontainer/$lower/$version/$lower.$version.nupkg"
unzip -o -q "$work/package.nupkg" "lib/$tfm/$id.dll" "lib/$tfm/$id.xml" -d "$work/pkg"
dll="$work/pkg/lib/$tfm/$id.dll"
key="$(dotnet run "$here/extract.cs" -- pdb-key "$dll" 2>/dev/null | tail -1)"
pdb="-"
if curl -fsSL -o "$work/$id.pdb" "https://msdl.microsoft.com/download/symbols/$key"; then pdb="$work/$id.pdb"; fi
dotnet run "$here/extract.cs" -- extract "$dll" "$work/pkg/lib/$tfm/$id.xml" "$pdb" "$id" "$version" "lib/$tfm/$id.dll" "$tfm" "$work/records.json" 2>&1 | grep -v ' warning '
python3 -I "$here/layout.py" "$work/records.json" "$work/$id.symbols.json"
shasum -a 256 "$work/$id.symbols.json"
