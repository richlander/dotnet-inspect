#!/usr/bin/env bash
# Prepare the four trial directories outside the repository and pin every input.
# Usage: prepare.sh <parent-dir> <dotnet-inspect-executable> <document.json>
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
parent="$1"; cli="$2"; doc="$3"
expected="f2bc17ce8ab7ebcdf8852a69504af46c3c0bcc85add04b74bc834d32ca6d69a6"
actual="$(shasum -a 256 "$doc" | cut -d' ' -f1)"
[ "$actual" = "$expected" ] || { echo "document SHA-256 $actual is not the pinned $expected" >&2; exit 1; }
"$cli" --version | grep -q '^0\.27\.' || { echo "expected dotnet-inspect 0.27" >&2; exit 1; }
mkdir -p "$parent"
cp "$doc" "$parent/System.Text.Json.symbols.json"
# Participants see a neutral recorder path, not this repository's trial name.
cp "$here/runner.py" "$parent/recorder.py"
runner="$parent/recorder.py"
task="$(cat "$here/task.txt")"
for arm in interactive document; do
  for n in 1 2; do
    t="$parent/$arm-$n"; mkdir "$t"
    if [ "$arm" = document ]; then
      cat "$here/guide-common.txt" "$here/guide-document.txt" > "$t/guide.md"
      document="\"$parent/System.Text.Json.symbols.json\""
      ops="python3 $runner $t document --jq '<your jq filter>'
python3 $runner $t document --grep '<fixed text>'
Document reads count as views.
"
    else
      cp "$here/guide-common.txt" "$t/guide.md"; document=null; ops=""
    fi
    printf '{"arm": "%s", "cli": "%s", "package": "System.Text.Json@10.0.14", "document": %s, "request_limit": 60, "view_limit": 150}\n' \
      "$arm" "$cli" "$document" > "$t/config.json"
    TRIAL="$t" RUNNER="$runner" TASK="$task" OPS="$ops" python3 -I -c '
import os, sys
text = open(sys.argv[1]).read()
for key, env in (("{trial}", "TRIAL"), ("{runner}", "RUNNER"), ("{task}", "TASK"), ("{document_operations}", "OPS")):
    text = text.replace(key, os.environ[env])
open(os.path.join(os.environ["TRIAL"], "prompt.txt"), "w").write(text)' "$here/participant.txt"
  done
done
python3 -I - "$here" "$parent" "$cli" <<'PY'
import hashlib, json, os, sys, subprocess
here, parent, cli = sys.argv[1:]
sha = lambda p: hashlib.sha256(open(p, "rb").read()).hexdigest()
inputs = ["task.txt", "guide-common.txt", "guide-document.txt", "participant.txt", "criteria.txt",
          "post-task-question.txt", "runner.py", "../command-bound/freeze.py", "oracle.json"]
dispatch = {
    "model": "gpt-6-sol", "reasoning": "high", "harness": "copilot",
    "cli": os.path.realpath(cli), "cli_version": subprocess.run([cli, "--version"], capture_output=True, text=True).stdout.strip(),
    "cli_sha256": sha(os.path.realpath(cli)),
    "document_sha256": sha(os.path.join(parent, "System.Text.Json.symbols.json")),
    "package": "System.Text.Json@10.0.14",
    "inputs": {name: sha(os.path.join(here, name)) for name in inputs},
    "recorder_copy_sha256": sha(os.path.join(parent, "recorder.py")),
    "trial_configs": {t: sha(os.path.join(parent, t, "config.json")) for t in ("interactive-1", "document-1", "interactive-2", "document-2")},
    "waves": [["interactive-1", "document-1"], ["interactive-2", "document-2"]],
}
open(os.path.join(parent, "dispatch.json"), "w").write(json.dumps(dispatch, indent=2) + "\n")
print(json.dumps(dispatch, indent=2))
PY
