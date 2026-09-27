#!/usr/bin/env bash
set -euo pipefail
export PATH="$HOME/.local/share/dotnet:$PATH" DOTNET_CLI_TELEMETRY_OPTOUT=1
W=/tmp/pp-bench; LINKER=""; command -v clang >/dev/null || LINKER="-p:CppCompilerAndLinker=gcc"
rm -rf $W/src-exp; cp -r $W/src-clean $W/src-exp; rm -rf $W/src-exp/tools/PresenceProbe $W/src-exp/artifacts
cd $W/src-exp && git apply $W/${PATCH:-exp.patch}
dotnet publish tools/PlanningProbe/PlanningProbe.csproj -c Release -r linux-x64 -p:IsPublishable=true $LINKER -o $W/${EXPOUT:-exp-probe} -tl:off -v:q > $W/publish-exp.log 2>&1 || { echo "exp publish failed"; tail -20 $W/publish-exp.log; exit 1; }
echo "exp ok $(stat -c %s $W/${EXPOUT:-exp-probe}/PlanningProbe)"
