#!/usr/bin/env bash
set -euo pipefail
W=/tmp/pp-bench; OUT=$W/results-pad.tsv; cd $W
DLLS=(inputs/CommandLine.dll inputs/Humanizer.dll inputs/Mono.Cecil.dll inputs/Newtonsoft.Json.dll inputs/System.Text.Json.dll inputs/NuGet.Packaging.dll inputs/Microsoft.CodeAnalysis.CSharp.dll inputs/System.Private.CoreLib.dll)
S=(base final2 final3 pad40 pad97); : > $OUT; n=${#S[@]}
for r in 1 2 3 4 5 6; do for ((i=0;i<n;i++)); do if ((r%2)); then s=${S[$(( (i+r) % n ))]}; else s=${S[$(( (n-1-i+r) % n ))]}; fi
  ./probe-$s/PresenceProbe 3000 "${DLLS[@]}" | sed "s/^/$s\t$r\t/" >> $OUT; done; done
echo "rows=$(wc -l < $OUT) load=$(cut -d' ' -f1-3 /proc/loadavg)"
