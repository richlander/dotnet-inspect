#!/usr/bin/env bash
# Prepare the four participant directories, each with its own Copilot home and
# a PATH without dotnet-inspect, and pin every input.
# Usage: prepare.sh <parent-dir> <document.json>
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
parent="$1"; doc="$2"
# Participants see their own paths; none may name the tool, the document, or the repository.
case "$parent" in *inspect*|*symbol*|*git*) echo "choose a neutral parent path, such as /tmp/cli-trial-YYYYMMDD" >&2; exit 1;; esac
expected="f2bc17ce8ab7ebcdf8852a69504af46c3c0bcc85add04b74bc834d32ca6d69a6"
[ "$(shasum -a 256 "$doc" | cut -d' ' -f1)" = "$expected" ] || { echo "document is not the pinned $expected" >&2; exit 1; }
# PATH without any directory that holds dotnet-inspect.
clean_path="$(python3 -I -c '
import os
print(os.pathsep.join(d for d in os.environ["PATH"].split(os.pathsep)
                      if d and not os.path.exists(os.path.join(d, "dotnet-inspect"))))')"
if PATH="$clean_path" command -v dotnet-inspect >/dev/null; then echo "dotnet-inspect still on PATH" >&2; exit 1; fi
mkdir -p "$parent"
for arm in baseline document; do
  for n in 1 2; do
    t="$parent/$arm-$n"; mkdir "$t"
    home="$parent/.copilot-home-$arm-$n"; mkdir "$home"
    if [ "$arm" = document ]; then
      cp "$doc" "$t/System.Text.Json.symbols.json"
      guide="$(cat "$here/guide-baseline.txt" "$here/guide-document.txt")"
    else
      guide="$(cat "$here/guide-baseline.txt")"
    fi
    uuidgen | tr 'A-Z' 'a-z' > "$t/session-id"
    TRIAL="$t" GUIDE="$guide" TASK="$(cat "$here/task.txt")" python3 -I -c '
import os, sys
text = open(sys.argv[1]).read()
for key, env in (("{trial}", "TRIAL"), ("{guide}", "GUIDE"), ("{task}", "TASK")):
    text = text.replace(key, os.environ[env])
open(os.path.join(os.environ["TRIAL"], "prompt.txt"), "w").write(text)' "$here/participant.txt"
    cat > "$t/run.sh" <<RUN
#!/usr/bin/env bash
# Run this participant; extra arguments (such as a resume and a prompt) replace the task prompt.
cd "$t"
export COPILOT_HOME="$home" PATH="$clean_path"
if [ \$# -eq 0 ]; then set -- -p "\$(cat prompt.txt)" --session-id "\$(cat session-id)" --share session.md; fi
exec copilot "\$@" --model gpt-6-sol --reasoning-effort high --allow-all-tools --allow-all-paths -s --no-color
RUN
    chmod +x "$t/run.sh"
  done
done
python3 -I - "$here" "$parent" <<'PY'
import hashlib, json, os, subprocess, sys
here, parent = sys.argv[1:]
sha = lambda p: hashlib.sha256(open(p, "rb").read()).hexdigest()
inputs = ["task.txt", "guide-baseline.txt", "guide-document.txt", "participant.txt", "criteria.txt",
          "post-task-question.txt", "oracle.json", "measure.py", "freeze.py", "prepare.sh"]
dispatch = {
    "model": "gpt-6-sol", "reasoning": "high", "harness": "copilot",
    "copilot_version": subprocess.run(["copilot", "--version"], capture_output=True, text=True).stdout.splitlines()[0],
    "dotnet_sdk": subprocess.run(["dotnet", "--version"], capture_output=True, text=True).stdout.strip(),
    "document_sha256": sha(os.path.join(parent, "document-1", "System.Text.Json.symbols.json")),
    "package": "System.Text.Json@10.0.14",
    "inputs": {name: sha(os.path.join(here, name)) for name in inputs},
    "prompts": {t: sha(os.path.join(parent, t, "prompt.txt")) for t in ("baseline-1", "document-1", "baseline-2", "document-2")},
    "waves": [["baseline-1", "document-1"], ["baseline-2", "document-2"]],
}
open(os.path.join(parent, "dispatch.json"), "w").write(json.dumps(dispatch, indent=2) + "\n")
print(json.dumps(dispatch, indent=2))
PY
