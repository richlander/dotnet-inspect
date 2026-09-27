#!/usr/bin/env bash
# Rebuild probe-final3 with inert padding methods of different sizes to shift code layout.
set -euo pipefail
export PATH="$HOME/.local/share/dotnet:$PATH" DOTNET_CLI_TELEMETRY_OPTOUT=1
W=/tmp/pp-bench; LINKER=""; command -v clang >/dev/null || LINKER="-p:CppCompilerAndLinker=gcc"
for n in 40 97; do
  D=$W/src-pad$n; rm -rf $D; cp -r $W/src-final3 $D; rm -rf $D/artifacts
  P=$D/tools/PresenceProbe/Program.cs
  { echo 'static class Padding { [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)] public static long Run(long x) {'
    for i in $(seq 1 $n); do echo "  x = x * 6364136223846793005L + $i; if ((x & $i) == 7) x ^= x >> $((i % 31 + 1));"; done
    echo '  return x; } }'; } >> $P
  sed -i 's#^int budgetMs = int.Parse(args\[0\]);#if (Environment.GetEnvironmentVariable("PAD_NEVER") == "1") Console.WriteLine(Padding.Run(args.Length));\nint budgetMs = int.Parse(args[0]);#' $P
  grep -c "Padding.Run" $P
  (cd $D && dotnet publish tools/PresenceProbe/PresenceProbe.csproj -c Release -r linux-x64 -p:ProbeSide=head -p:IsPublishable=true $LINKER -o $W/probe-pad$n -tl:off -v:q > $W/publish-pad$n.log 2>&1) || { tail -20 $W/publish-pad$n.log; exit 1; }
  echo "pad$n ok $(md5sum $W/probe-pad$n/PresenceProbe | cut -c1-12)"
done
