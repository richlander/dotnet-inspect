#!/usr/bin/env bash
# Run this participant; extra arguments (such as a resume and a prompt) replace the task prompt.
cd "/tmp/cli-trial-20261106/document-2"
export COPILOT_HOME="/tmp/cli-trial-20261106/.copilot-home-document-2" PATH="/Users/rich/.local/bin:/Users/rich/.local/bin:/Users/rich/.local/bin:/opt/homebrew/bin:/opt/homebrew/sbin:/usr/local/bin:/System/Cryptexes/App/usr/bin:/usr/bin:/bin:/usr/sbin:/sbin:/var/run/com.apple.security.cryptexd/codex.system/bootstrap/usr/local/bin:/var/run/com.apple.security.cryptexd/codex.system/bootstrap/usr/bin:/var/run/com.apple.security.cryptexd/codex.system/bootstrap/usr/appleinternal/bin:/pkg/env/global/bin:/usr/local/share/dotnet:~/.dotnet/tools:/opt/homebrew/bin:/Users/rich/.cargo/bin:/Applications/Ghostty.app/Contents/MacOS:/Users/rich/.dotnet/tools"
if [ $# -eq 0 ]; then set -- -p "$(cat prompt.txt)" --session-id "$(cat session-id)" --share session.md; fi
exec copilot "$@" --model gpt-6-sol --reasoning-effort high --allow-all-tools --allow-all-paths -s --no-color
