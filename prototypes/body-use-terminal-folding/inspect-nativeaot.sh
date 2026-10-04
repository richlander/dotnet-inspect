#!/usr/bin/env bash
set -euo pipefail

rid=${1:?usage: inspect-nativeaot.sh RID}
map=$(
    find obj/Release \
        -path "*/$rid/native/body-use-terminal-folding-probe.map.xml" \
        -print \
        -quit
)

if [[ -z "$map" || ! -f "$map" ]]; then
    echo "NativeAOT map not found for $rid." >&2
    exit 1
fi

for method in \
    Folding__DirectExists \
    Folding__DirectCount \
    Folding__DirectRows \
    Folding__StaticExists \
    Folding__StaticCount \
    Folding__StaticRows \
    Folding__Planned \
    Folding__Fused
do
    grep -q "Name=\"body_use_terminal_folding_probe_$method\"" "$map"
done

if grep -Eq \
    'ITerminalFold|Folding__Fold|ExistsFold__Admit|CountFold__Admit|RowsFold__Admit' \
    "$map"
then
    echo "Generic or per-operand fold dispatch remains in NativeAOT code." >&2
    exit 1
fi

grep -E \
    'Name="body_use_terminal_folding_probe_Folding__(Direct|Static|Planned|Fused)' \
    "$map"
