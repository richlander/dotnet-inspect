#!/usr/bin/env bash
set -euo pipefail

readonly root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
readonly rid="${1:?usage: test-runtime-flavor.sh <runtime-identifier>}"
readonly project="$root/tests/DotnetInspector.RuntimeFlavorProbe/RuntimeFlavorProbe.csproj"
readonly artifacts="$root/artifacts/runtime-flavor-probe"
executable="RuntimeFlavorProbe"
if [[ "$rid" == win-* ]]; then
  executable="$executable.exe"
fi

for mode in coreclr single-file nativeaot; do
  publish_args=(-p:PublishAot=false -p:PublishSingleFile=false --self-contained false)
  expected="CoreCLR"
  case "$mode" in
    single-file)
      publish_args=(-p:PublishAot=false -p:PublishSingleFile=true --self-contained true)
      ;;
    nativeaot)
      publish_args=(-p:PublishAot=true -p:PublishSingleFile=false --self-contained true)
      expected="NativeAOT"
      ;;
  esac

  output="$artifacts/$mode/publish"
  dotnet publish "$project" -c Release -r "$rid" \
    --artifacts-path "$artifacts/$mode" -o "$output" \
    --nologo -v:quiet "${publish_args[@]}"
  "$output/$executable" "$expected"
  if [[ "$expected" == CoreCLR ]]; then
    "$output/$executable" "$expected" --disable-dynamic-code
  fi
done
