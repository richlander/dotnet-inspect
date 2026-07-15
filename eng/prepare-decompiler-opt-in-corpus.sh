#!/usr/bin/env bash
# Builds the net11 compiler-feature corpus with either its pinned SDK or the
# repository-selected current SDK, then emits one assembly path per line.
set -euo pipefail

PINNED_SDK="11.0.100-preview.7.26357.101"

mode="${1:-pinned}"
out="${2:-}"
repo="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
nuget_config="${NIGHTLY_NUGET_CONFIG:-$repo/nuget.config}"

if [ "$mode" = "print-pinned-sdk" ]; then
  printf '%s\n' "$PINNED_SDK"
  exit 0
fi

case "$mode" in
  pinned)
    sdk="$PINNED_SDK"
    if ! dotnet --list-sdks | awk '{ print $1 }' | grep -Fxq "$sdk"; then
      echo "Pinned opt-in corpus SDK $sdk is not installed." >&2
      echo "Install it with: dotnetup sdk install $sdk --interactive false" >&2
      exit 1
    fi
    ;;
  current)
    sdk="$(dotnet --version)"
    ;;
  *)
    echo "Usage: $0 [pinned|current|print-pinned-sdk] [output-file]" >&2
    exit 2
    ;;
esac

tmp="$(mktemp -d)"
trap 'rm -rf "$tmp"' EXIT

printf '{\n  "sdk": {\n    "version": "%s",\n    "rollForward": "disable"\n  }\n}\n' \
  "$sdk" > "$tmp/global.json"

declare -a projects=(
  "src/ILInspector.Decompiler.Fixtures.OptInNet11/ILInspector.Decompiler.Fixtures.OptInNet11.csproj"
  "src/ILInspector.Decompiler.Fixtures.NewUnsafe/ILInspector.Decompiler.Fixtures.NewUnsafe.csproj"
)

destination="$repo/artifacts/corpus/opt-in-net11/$mode"
mkdir -p "$destination"

declare -a assemblies=()
for relative_project in "${projects[@]}"; do
  project="$repo/$relative_project"
  project_name="$(basename "$relative_project" .csproj)"
  (
    cd "$tmp"
    dotnet restore "$project" --configfile "$nuget_config" \
      -p:DefaultTargetFramework=net11.0 --verbosity quiet >/dev/null
    dotnet build "$project" -c Release --no-restore \
      -p:DefaultTargetFramework=net11.0 --verbosity quiet >/dev/null
  )

  source="$repo/artifacts/bin/$project_name/release/$project_name.dll"
  if [ ! -f "$source" ]; then
    echo "Missing built corpus assembly: $source" >&2
    exit 1
  fi

  assembly="$destination/$project_name.dll"
  cp "$source" "$assembly"
  assemblies+=("$assembly")
done

printf 'profile=opt-in-net11\nmode=%s\nsdk=%s\n' "$mode" "$sdk" \
  > "$destination/provenance.txt"

if [ -n "$out" ]; then
  printf '%s\n' "${assemblies[@]}" > "$out"
else
  printf '%s\n' "${assemblies[@]}"
fi
