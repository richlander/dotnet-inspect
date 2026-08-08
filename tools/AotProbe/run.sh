#!/usr/bin/env bash
# Publishes tools/AotProbe as Native AOT across every combination of instruction-set
# baseline and OptimizationPreference available on this platform, then runs each one.
#
#   ./run.sh
#
# The instruction-set list is chosen from the host architecture, so the same script
# produces the x64 sweep and the arm64 sweep without editing. Close other work before
# running: figures are best-of-9, but heavy background load still perturbs them.
set -euo pipefail

cd "$(dirname "$0")"

case "$(uname -s)-$(uname -m)" in
  Darwin-arm64)  RID=osx-arm64;   ISAS=(default armv8.1-a armv8.2-a apple-m1) ;;
  Linux-aarch64) RID=linux-arm64; ISAS=(default armv8.1-a armv8.2-a) ;;
  Linux-x86_64)  RID=linux-x64;   ISAS=(x86-64-v2 x86-64-v3 x86-64-v4) ;;
  Darwin-x86_64) RID=osx-x64;     ISAS=(x86-64-v2 x86-64-v3 x86-64-v4) ;;
  Windows*|MINGW*|MSYS*) RID=win-x64; ISAS=(x86-64-v2 x86-64-v3 x86-64-v4) ;;
  *) echo "unrecognised platform: $(uname -s)-$(uname -m)" >&2; exit 1 ;;
esac

echo "# host $(uname -s) $(uname -m), rid=$RID"
echo "# baselines: ${ISAS[*]}"

for isa in "${ISAS[@]}"; do
  for opt in Size Speed; do
    tag="$isa-$opt"
    args=(-c Release -r "$RID" -p:PublishAot=true -p:OptimizationPreference="$opt"
          -o "out-$tag" -v q --nologo)
    # "default" means leave the baseline unpinned, which is what ships today.
    if [[ "$isa" != "default" ]]; then
      args+=(-p:IlcInstructionSet="$isa")
    fi
    echo "# publishing $tag" >&2
    dotnet publish "${args[@]}" >/dev/null
    printf '# %-16s binary=%s bytes\n' "$tag" "$(wc -c < "out-$tag/aot-probe" | tr -d ' ')"
  done
done

for isa in "${ISAS[@]}"; do
  for opt in Size Speed; do
    "./out-$isa-$opt/aot-probe" "$isa-$opt"
  done
done

echo "# done. remove the published output with: rm -rf $(dirname "$0")/out-*"
