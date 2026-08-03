#!/usr/bin/env bash
# Sourceable companion to eng/restore-iltools.sh: restores the external oracle
# tools and puts them on PATH in the *current* shell.
#
#   source eng/activate-iltools.sh --mdv
#
# A child process cannot change its parent's PATH, so restore-iltools.sh can
# only print the directories it restored and leave the caller to assemble them.
# That assembly is deceptively easy to get wrong, and every way of getting it
# wrong is silent:
#
#   * `export PATH="$(restore-iltools.sh)"` reports export's exit status, not
#     the script's, so a failed restore looks like success.
#   * Splitting the assignment out only helps under `set -e`; an interactive
#     shell runs the export anyway.
#   * Losing the trailing newline glues the last directory to the first
#     pre-existing PATH entry, corrupting both.
#   * Empty or whitespace-only output prepends an empty PATH entry, which means
#     the current directory.
#
# In each case PATH looks plausible, the oracles are missing, and the suites
# report a green run that proved nothing -- the exact failure restore-iltools.sh
# exists to prevent. This file is that assembly, written once and enforced by
# IlToolsActivationTests in src/dotnet-inspect.Tests, so the failure modes stay
# fixed instead of being retyped correctly by every caller.
#
# CI does not need this file: `eng/restore-iltools.sh >> "$GITHUB_PATH"` hands
# the lines to the runner, which does the joining itself.
#
# Note that `source file` with no arguments leaves the sourcing script's own
# positional parameters visible here, so pass arguments explicitly (or none
# from a script that has its own). A stray argument is rejected loudly by
# restore-iltools.sh rather than silently ignored.
#
# Deliberately does not `set -euo pipefail`: those would leak into the shell
# that sourced it. For the same reason the helper functions are unset on the
# way out; the one namespaced variable left behind, $__iltools_status, is the
# price of returning the real exit code without an eval.

if [ -z "${BASH_SOURCE[0]:-}" ]; then
    echo "error: activate-iltools.sh needs bash; source it from a bash shell." >&2
    return 2 2> /dev/null || exit 2
fi

if [ "${BASH_SOURCE[0]}" = "$0" ]; then
    echo "error: activate-iltools.sh must be sourced, not executed -- a child" >&2
    echo "       process cannot change this shell's PATH. Use:" >&2
    echo "           source eng/activate-iltools.sh [--rid <rid>] [--mdv]" >&2
    echo "       To capture the directories instead, run eng/restore-iltools.sh." >&2
    exit 2
fi

# Exact-element membership test against PATH. Deliberately avoids both `case`
# patterns and unquoted word splitting, because a directory containing a glob
# metacharacter would be matched as a pattern by the first and mangled by the
# second. Uses only builtins, like the rest of this file, so it still works
# when the PATH being repaired is itself unusable.
__iltools_path_has() {
    local needle="$1" element haystack
    haystack="${PATH:-}"
    while IFS= read -r element; do
        if [ "$element" = "$needle" ]; then
            return 0
        fi
    done <<< "${haystack//:/$'\n'}"
    return 1
}

__iltools_activate() {
    # A function body so `local` keeps the sourcing shell's namespace clean and
    # a failure can `return`; `exit` here would close an interactive shell.
    local self dir script_dir restore out status joined line saw_entry

    # `${var%/*}` rather than dirname(1): this file's whole job is fixing PATH,
    # so it must not need PATH to work.
    self="${BASH_SOURCE[0]}"
    dir="${self%/*}"
    if [ "$dir" = "$self" ]; then
        dir="."
    fi

    script_dir="$(cd "$dir" && pwd)" || return 1
    restore="$script_dir/restore-iltools.sh"

    if [ ! -x "$restore" ]; then
        printf 'error: %s is missing or not executable.\n' "$restore" >&2
        return 1
    fi

    # Capture, then test the status on its own line. The caller may not be
    # running under `set -e`, so nothing else will notice a failure for us --
    # and if it is, `|| status=$?` keeps errexit from killing the caller's
    # script before it can be told what went wrong.
    status=0
    out="$("$restore" "$@")" || status=$?
    if [ "$status" -ne 0 ]; then
        printf 'error: %s failed (exit %d); PATH left unchanged.\n' "$restore" "$status" >&2
        return "$status"
    fi

    joined=""
    saw_entry=0
    while IFS= read -r line; do
        # Drop blank and whitespace-only lines. restore-iltools.sh refuses to
        # emit them, but joining one would produce an empty PATH element, which
        # means the current directory -- too quiet a failure to depend on the
        # producer for.
        case "$line" in
            *[![:space:]]*) saw_entry=1 ;;
            *) continue ;;
        esac

        # Sourcing twice should not grow PATH without bound.
        if __iltools_path_has "$line"; then
            continue
        fi

        if [ -z "$joined" ]; then
            joined="$line"
        else
            joined="$joined:$line"
        fi
    done <<< "$out"

    if [ "$saw_entry" -eq 0 ]; then
        printf 'error: %s printed no directories; PATH left unchanged.\n' "$restore" >&2
        return 1
    fi

    if [ -z "$joined" ]; then
        # Every directory was already present. PATH is already correct.
        return 0
    fi

    if [ -n "${PATH:-}" ]; then
        export PATH="$joined:$PATH"
    else
        # Appending ':$PATH' here would leave a trailing empty element.
        export PATH="$joined"
    fi
}

__iltools_activate "$@"
__iltools_status=$?
unset -f __iltools_activate __iltools_path_has
return "$__iltools_status"
