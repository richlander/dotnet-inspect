#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

TASKS_FILE="${BUILD_EVENT_EVAL_TASKS:-$REPO_ROOT/samples/build-event-evals/tasks.tsv}"
EXPECTED_TYPES_DIR="${BUILD_EVENT_EVAL_EXPECTED_TYPES_DIR:-$REPO_ROOT/samples/build-event-evals/expected-types}"
VMR_DOTNET="${BUILD_EVENT_VMR_DOTNET:-/home/rich/git/dotnet-build-events-vmr-pure/artifacts/pure-sdk-test/dotnet}"
BAD_CODE_SOURCE_ROOT="${BAD_CODE_SOURCE_ROOT:-/home/rich/git/bad-code}"
BAD_CODE_EVAL_ROOT="${BAD_CODE_EVAL_ROOT:-/home/rich/git/bad-code-build-event-evals}"
HOST_DOTNET="${BUILD_EVENT_HOST_DOTNET:-${DOTNET:-dotnet}}"
INSPECT_PROJECT="${BUILD_EVENT_DOTNET_INSPECT_PROJECT:-$REPO_ROOT/src/dotnet-inspect}"

usage() {
    cat <<'USAGE'
Usage:
  scripts/build-event-eval.sh tasks
  scripts/build-event-eval.sh prepare <task-id> <channel> [run-id]
  scripts/build-event-eval.sh prompt <task-id> <channel> <run-root>
  scripts/build-event-eval.sh verify <task-id> <channel> <run-root> <final-event-log>

Channels:
  raw-build
  build-events
  build-events-explicit

Environment:
  BUILD_EVENT_VMR_DOTNET       source-built VMR dotnet path
  BAD_CODE_SOURCE_ROOT         source bad-code repo, default /home/rich/git/bad-code
  BAD_CODE_EVAL_ROOT           run root, default /home/rich/git/bad-code-build-event-evals
  BUILD_EVENT_HOST_DOTNET      host dotnet for running dotnet-inspect
USAGE
}

die() {
    echo "error: $*" >&2
    exit 1
}

json_escape() {
    local value="$1"
    value="${value//\\/\\\\}"
    value="${value//\"/\\\"}"
    value="${value//$'\n'/\\n}"
    value="${value//$'\r'/\\r}"
    printf '%s' "$value"
}

require_file() {
    [[ -f "$1" ]] || die "file not found: $1"
}

require_executable() {
    [[ -x "$1" ]] || die "not executable: $1"
}

load_task() {
    local id="$1"
    require_file "$TASKS_FILE"
    local row
    row="$(awk -F '\t' -v id="$id" 'NR > 1 && $1 == id { print; found=1; exit } END { if (!found) exit 1 }' "$TASKS_FILE")" ||
        die "unknown task '$id' in $TASKS_FILE"
    IFS=$'\t' read -r TASK_ID TASK_PROJECT TASK_SUCCESS_GATE TASK_DESCRIPTION <<<"$row"
    TASK_EXPECTED_TYPES="$EXPECTED_TYPES_DIR/$TASK_ID.tsv"
}

validate_channel() {
    case "$1" in
        raw-build|build-events|build-events-explicit) ;;
        *) die "unknown channel '$1'" ;;
    esac
}

run_inspect_build() {
    "$HOST_DOTNET" run --project "$INSPECT_PROJECT" -c Release --no-build -- build "$@"
}

extract_event_log_path() {
    local stderr_file="$1"
    grep -Eo '/[^[:space:]]+\.jsonl' "$stderr_file" | tail -1
}

write_json_metadata() {
    local file="$1"
    local task_id="$2"
    local channel="$3"
    local run_root="$4"
    local source_commit="$5"
    local baseline_patch="$6"
    local baseline_sha="$7"
    local preflight_log="$8"
    local preflight_exit="$9"

    {
        printf '{\n'
        printf '  "schemaVersion": 1,\n'
        printf '  "taskId": "%s",\n' "$(json_escape "$task_id")"
        printf '  "project": "%s",\n' "$(json_escape "$TASK_PROJECT")"
        printf '  "channel": "%s",\n' "$(json_escape "$channel")"
        printf '  "successGate": "%s",\n' "$(json_escape "$TASK_SUCCESS_GATE")"
        printf '  "runRoot": "%s",\n' "$(json_escape "$run_root")"
        printf '  "sourceRoot": "%s",\n' "$(json_escape "$BAD_CODE_SOURCE_ROOT")"
        printf '  "sourceCommit": "%s",\n' "$(json_escape "$source_commit")"
        printf '  "baselinePatch": "%s",\n' "$(json_escape "$baseline_patch")"
        printf '  "baselinePatchSha256": "%s",\n' "$(json_escape "$baseline_sha")"
        printf '  "preflightEventLog": "%s",\n' "$(json_escape "$preflight_log")"
        printf '  "preflightExitCode": %s\n' "$preflight_exit"
        printf '}\n'
    } > "$file"
}

write_scorecard_json() {
    local file="$1"
    local task_id="$2"
    local channel="$3"
    local run_root="$4"
    local final_log="$5"
    local result="$6"
    local errors="$7"
    local warnings="$8"
    local types_rows="$9"

    {
        printf '{\n'
        printf '  "schemaVersion": 1,\n'
        printf '  "taskId": "%s",\n' "$(json_escape "$task_id")"
        printf '  "project": "%s",\n' "$(json_escape "$TASK_PROJECT")"
        printf '  "channel": "%s",\n' "$(json_escape "$channel")"
        printf '  "runRoot": "%s",\n' "$(json_escape "$run_root")"
        printf '  "finalEventLog": "%s",\n' "$(json_escape "$final_log")"
        printf '  "result": "%s",\n' "$(json_escape "$result")"
        printf '  "errors": %s,\n' "$errors"
        printf '  "warnings": %s,\n' "$warnings"
        printf '  "typesRows": %s\n' "$types_rows"
        printf '}\n'
    } > "$file"
}

tasks() {
    require_file "$TASKS_FILE"
    cat "$TASKS_FILE"
}

prompt() {
    local task_id="${1:-}"
    local channel="${2:-}"
    local run_root="${3:-}"
    [[ -n "$task_id" && -n "$channel" && -n "$run_root" ]] || die "prompt requires <task-id> <channel> <run-root>"
    validate_channel "$channel"
    load_task "$task_id"

    case "$channel" in
        raw-build)
            cat <<EOF
You are in an isolated bad-code worktree at $run_root. Fix all diagnostics for
$TASK_PROJECT: errors and warnings. Use the source-built VMR dotnet at
$VMR_DOTNET for every build command.

This is the raw-build control arm. Do NOT use \`dotnet build --view\`, event logs,
or \`dotnet-inspect build\` as the primary diagnostic source. Use normal build
output and local source inspection.

Start with:

\`$VMR_DOTNET build $TASK_PROJECT --no-restore --no-incremental\`

Success criterion: final build has 0 errors and 0 warnings. Do not modify
unrelated projects. Do not commit changes.

Report final result, files changed/rationale, diagnostics fixed or remaining,
elapsed time if available, build count, and whether you parsed raw logs or hit
retry loops.
EOF
            ;;
        build-events)
            cat <<EOF
You are in an isolated bad-code worktree at $run_root. Fix the build for
$TASK_PROJECT. Use the source-built VMR dotnet at $VMR_DOTNET for every build
command.

Start with:

\`$VMR_DOTNET build $TASK_PROJECT --no-restore --no-incremental --view types --event-log-stderr\`

Use the emitted event log with this worktree's \`dotnet-inspect build\`
implementation:

\`$HOST_DOTNET run --project $INSPECT_PROJECT -c Release --no-build -- build <log> -S Types --tsv\`

Use \`Types\` first, then filtered \`Errors\`/\`Diagnostics\`, \`Projects\`, or
\`Details\` as needed. Do not parse raw build logs as the primary diagnostic
source. Do not modify unrelated projects. Do not commit changes.

Report before counts, final result, files changed/rationale, after counts, final
event-log path or ID, elapsed time if available, build count, and whether
build-event views changed the path you took.
EOF
            ;;
        build-events-explicit)
            cat <<EOF
You are in an isolated bad-code worktree at $run_root. Fix all diagnostics for
$TASK_PROJECT: errors and warnings. Use the source-built VMR dotnet at
$VMR_DOTNET for every build command.

You must use the build-event workflow. Start with:

\`$VMR_DOTNET build $TASK_PROJECT --no-restore --no-incremental --view types --event-log-stderr\`

Use the emitted event log with this worktree's \`dotnet-inspect build\`
implementation:

\`$HOST_DOTNET run --project $INSPECT_PROJECT -c Release --no-build -- build <log> -S Types --tsv\`

Then use \`Diagnostics\`, \`Errors\`, \`Warnings\`, \`Projects\`, or \`Details\` as
needed. Do not parse raw build logs as the primary diagnostic source. Rebuild and
query the final event log to prove the final \`Types\` view is empty.

Success criterion: final build-event \`Types\` view is empty: 0 errors and 0
warnings. Do not modify unrelated projects. Do not commit changes.

Report before counts, files changed/rationale, after counts, final event-log path
or ID, final result, elapsed time if available, build count, skipped diagnostics
if any, and quality concerns.
EOF
            ;;
    esac
}

prepare() {
    local task_id="${1:-}"
    local channel="${2:-}"
    local run_id="${3:-}"
    [[ -n "$task_id" && -n "$channel" ]] || die "prepare requires <task-id> <channel> [run-id]"
    validate_channel "$channel"
    load_task "$task_id"
    require_executable "$VMR_DOTNET"
    [[ -d "$BAD_CODE_SOURCE_ROOT/.git" ]] || die "bad-code repo not found: $BAD_CODE_SOURCE_ROOT"

    "$HOST_DOTNET" build "$INSPECT_PROJECT" -c Release --nologo >/dev/null

    mkdir -p "$BAD_CODE_EVAL_ROOT"
    local baseline_patch="$BAD_CODE_EVAL_ROOT/bad-code-baseline.patch"
    git -C "$BAD_CODE_SOURCE_ROOT" diff --binary > "$baseline_patch"
    local baseline_sha
    baseline_sha="$(sha256sum "$baseline_patch" | awk '{ print $1 }')"

    if [[ -z "$run_id" ]]; then
        run_id="$(date -u +%Y%m%dT%H%M%SZ)-$task_id-$channel"
    fi
    local run_root="$BAD_CODE_EVAL_ROOT/$run_id"
    [[ ! -e "$run_root" ]] || die "run root already exists: $run_root"

    git -C "$BAD_CODE_SOURCE_ROOT" worktree add --detach "$run_root" HEAD >/dev/null
    if [[ -s "$baseline_patch" ]]; then
        git -C "$run_root" apply "$baseline_patch"
    else
        echo "warning: baseline patch is empty; preflight must still match expected Types" >&2
    fi

    (
        cd "$run_root"
        "$VMR_DOTNET" restore "$TASK_PROJECT" --nologo >/dev/null
    )

    local eval_dir="$run_root/.build-event-eval"
    local preflight_dir="$eval_dir/preflight"
    mkdir -p "$preflight_dir"

    local build_exit=0
    (
        cd "$run_root"
        "$VMR_DOTNET" build "$TASK_PROJECT" --no-restore --no-incremental --view types --event-log-stderr \
            > "$preflight_dir/stdout.txt" 2> "$preflight_dir/stderr.txt"
    ) || build_exit=$?

    local preflight_log
    preflight_log="$(extract_event_log_path "$preflight_dir/stderr.txt")"
    [[ -n "$preflight_log" ]] || die "could not find preflight event log path in $preflight_dir/stderr.txt"

    run_inspect_build "$preflight_log" -S Types --tsv -n 200 > "$preflight_dir/types.tsv"

    if [[ -f "$TASK_EXPECTED_TYPES" ]]; then
        diff -u "$TASK_EXPECTED_TYPES" "$preflight_dir/types.tsv" > "$preflight_dir/types.diff" ||
            die "preflight Types did not match $TASK_EXPECTED_TYPES; see $preflight_dir/types.diff"
    fi

    local source_commit
    source_commit="$(git -C "$BAD_CODE_SOURCE_ROOT" rev-parse HEAD)"
    write_json_metadata "$eval_dir/metadata.json" "$task_id" "$channel" "$run_root" \
        "$source_commit" "$baseline_patch" "$baseline_sha" "$preflight_log" "$build_exit"
    prompt "$task_id" "$channel" "$run_root" > "$eval_dir/prompt.md"

    printf 'run_root=%s\n' "$run_root"
    printf 'prompt=%s\n' "$eval_dir/prompt.md"
    printf 'preflight_event_log=%s\n' "$preflight_log"
    printf 'preflight_types=%s\n' "$preflight_dir/types.tsv"
}

verify() {
    local task_id="${1:-}"
    local channel="${2:-}"
    local run_root="${3:-}"
    local final_log="${4:-}"
    [[ -n "$task_id" && -n "$channel" && -n "$run_root" && -n "$final_log" ]] ||
        die "verify requires <task-id> <channel> <run-root> <final-event-log>"
    validate_channel "$channel"
    load_task "$task_id"
    require_file "$final_log"

    local final_dir="$run_root/.build-event-eval/final"
    mkdir -p "$final_dir"

    run_inspect_build "$final_log" -S Summary --tsv > "$final_dir/summary.tsv"
    run_inspect_build "$final_log" -S Types --tsv -n 200 > "$final_dir/types.tsv"
    run_inspect_build "$final_log" -S Projects --tsv > "$final_dir/projects.tsv"

    local errors warnings event_id
    errors="$(awk -F '\t' 'NR == 2 { print $4 }' "$final_dir/summary.tsv")"
    warnings="$(awk -F '\t' 'NR == 2 { print $5 }' "$final_dir/summary.tsv")"
    event_id="$(awk -F '\t' 'NR == 2 { print $6 }' "$final_dir/summary.tsv")"
    errors="${errors:-0}"
    warnings="${warnings:-0}"

    local types_rows
    types_rows="$(awk 'NF { count++ } END { if (count > 0) print count - 1; else print 0 }' "$final_dir/types.tsv")"

    local result="fail"
    if [[ "$TASK_SUCCESS_GATE" == "types-empty" && "$errors" == "0" && "$warnings" == "0" && "$types_rows" == "0" ]]; then
        result="pass"
    fi

    local score_tsv="$final_dir/scorecard.tsv"
    {
        printf 'task\tproject\tchannel\tresult\terrors\twarnings\ttypes_rows\tevent_id\tfinal_event_log\trun_root\n'
        printf '%s\t%s\t%s\t%s\t%s\t%s\t%s\t%s\t%s\t%s\n' \
            "$task_id" "$TASK_PROJECT" "$channel" "$result" "$errors" "$warnings" "$types_rows" \
            "$event_id" "$final_log" "$run_root"
    } > "$score_tsv"

    write_scorecard_json "$final_dir/scorecard.json" "$task_id" "$channel" "$run_root" \
        "$final_log" "$result" "$errors" "$warnings" "$types_rows"

    local aggregate="$BAD_CODE_EVAL_ROOT/scorecard.tsv"
    if [[ ! -f "$aggregate" ]]; then
        head -1 "$score_tsv" > "$aggregate"
    fi
    tail -1 "$score_tsv" >> "$aggregate"

    cat "$score_tsv"
}

cmd="${1:-}"
shift || true

case "$cmd" in
    tasks) tasks "$@" ;;
    prepare) prepare "$@" ;;
    prompt) prompt "$@" ;;
    verify) verify "$@" ;;
    -h|--help|help|"") usage ;;
    *) usage >&2; die "unknown command '$cmd'" ;;
esac
