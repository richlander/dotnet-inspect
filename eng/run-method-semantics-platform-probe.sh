#!/usr/bin/env bash
set -euo pipefail

readonly root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
readonly probe="$root/tests/ILInspector.MetadataPrimitives.PlatformProbe"
readonly expected="method-semantics-platform-probe: supported"

case "${1:-}" in
  nativeaot)
    readonly rid="${2:?nativeaot requires a runtime identifier}"
    readonly output="$root/artifacts/method-semantics-platform-probe/nativeaot"
    rm -rf "$output"
    dotnet publish \
      "$probe/MethodSemanticsPlatformProbe.csproj" \
      -c Release \
      -r "$rid" \
      -o "$output" \
      --nologo
    test "$("$output/MethodSemanticsPlatformProbe")" = "$expected"
    ;;
  browser)
    readonly output="$root/artifacts/method-semantics-platform-probe/browser"
    rm -rf "$output"
    dotnet publish \
      "$probe/MethodSemanticsBrowserProbe.csproj" \
      -c Release \
      -o "$output" \
      --nologo
    readonly site="$output/wwwroot"
    readonly dotnet_js="$(
      find "$site/_framework" \
        -maxdepth 1 \
        -type f \
        -name 'dotnet.*.js' \
        ! -name 'dotnet.native.*' \
        ! -name 'dotnet.runtime.*' \
        -print -quit
    )"
    readonly main_js="$(
      find "$site" \
        -maxdepth 1 \
        -type f \
        -name 'main.*.js' \
        -print -quit
    )"
    test -n "$dotnet_js"
    test -n "$main_js"
    cp "$dotnet_js" "$site/_framework/dotnet.js"
    test "$(
      cd "$site"
      node "$main_js"
    )" = "$expected"
    ;;
  *)
    echo "usage: $0 nativeaot <rid> | browser" >&2
    exit 2
    ;;
esac
