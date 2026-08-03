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
# positional parameters visible here, and bash gives no way to tell those apart
# from arguments genuinely passed to `source`. Empty arguments are dropped, so
# a script with its own parameters can ask for none deterministically:
#
#   source eng/activate-iltools.sh ""
#
# Anything else that leaks through is rejected by restore-iltools.sh with a
# hint naming this trap, rather than silently ignored.
#
# Deliberately does not `set -euo pipefail`: those would leak into the shell
# that sourced it. For the same reason the helper functions are unset on the
# way out; the one namespaced variable left behind, $__iltools_status, is the
# price of returning the real exit code without an eval.

# `${BASH_VERSION:-}` rather than `${BASH_SOURCE[0]:-}`: array syntax is a
# substitution error in dash and other POSIX shells, which would abort with
# "Bad substitution" before this guard could print anything useful.
if [ -z "${BASH_VERSION:-}" ]; then
    echo "error: activate-iltools.sh needs bash; source it from a bash shell." >&2
    return 2 2> /dev/null || exit 2
fi

# Refuse to clobber a caller's function of the same name. Without this, a
# readonly collision leaves the caller's function running in place of ours and
# reports success having restored nothing -- the silent-green failure this file
# exists to prevent, wearing a different hat. Re-sourcing is unaffected: the
# helpers are unset on the way out.
if declare -F __iltools_activate > /dev/null 2>&1 ||
   declare -F __iltools_path_has > /dev/null 2>&1 ||
   declare -F __iltools_hint_arguments > /dev/null 2>&1; then
    echo "error: this shell already defines one of the __iltools_* helper functions;" >&2
    echo "       activate-iltools.sh will not overwrite them. Unset them and retry." >&2
    return 2
fi

if [ "${BASH_SOURCE[0]}" = "$0" ]; then
    echo "error: activate-iltools.sh must be sourced, not executed -- a child" >&2
    echo "       process cannot change this shell's PATH. Use:" >&2
    echo "           source eng/activate-iltools.sh [--rid <rid>] [--mdv]" >&2
    echo "       To capture the directories instead, run eng/restore-iltools.sh." >&2
    exit 2
fi

# Exact-element membership test against a PATH-shaped string. Deliberately
# avoids both `case` patterns and unquoted word splitting, because a directory
# containing a glob metacharacter would be matched as a pattern by the first
# and mangled by the second. Uses only builtins, like the rest of this file, so
# it still works when the PATH being repaired is itself unusable.
# Exact-element membership test against a PATH-shaped string. Deliberately
# avoids both `case` patterns and unquoted word splitting, because a directory
# containing a glob metacharacter would be matched as a pattern by the first
# and mangled by the second. Uses only builtins, like the rest of this file, so
# it still works when the PATH being repaired is itself unusable.
#
# Walks the string colon by colon rather than translating colons to newlines and
# reading lines, matching the PATH rebuild below. There the newline form was a
# live defect: it tore a caller's directory containing a newline into two
# entries, and a trailing one produced an empty element. Here the haystack is
# always $joined, whose elements come from the producer's line-oriented output
# and so can contain neither a newline nor a colon -- this form is consistency
# and hardening for a future caller, not a property any test can currently
# reach.
__iltools_path_has() {
    local needle="$1" rest="${2-}" element

    while :; do
        element="${rest%%:*}"

        if [ "$element" = "$needle" ]; then
            return 0
        fi

        case "$rest" in
            *:*) rest="${rest#*:}" ;;
            *) return 1 ;;
        esac
    done
}

# Printed on every failure that could have been caused by arguments, because
# `source` makes the caller's own parameters indistinguishable from real ones.
# Without this, a script that takes `--fast` gets "unknown argument '--fast'"
# from a script it never invoked.
__iltools_hint_arguments() {
    local self="$1" count="$2"

    if [ "$count" -eq 0 ]; then
        return 0
    fi

    printf 'hint: %d argument(s) were forwarded. If you did not pass them, they are\n' "$count" >&2
    printf '      your own script'"'"'s positional parameters, which `source` leaves\n' >&2
    printf '      visible here. Use `source %s ""` to invoke it with none.\n' "$self" >&2
}

__iltools_activate() {
    # A function body so `local` keeps the sourcing shell's namespace clean and
    # a failure can `return`; `exit` here would close an interactive shell.
    local self dir script_dir restore out status joined line saw_entry
    local element tail first arg rest
    local -a args=()

    # `source file` with no arguments leaves the sourcing script's own
    # positional parameters visible here, and bash offers no way to tell those
    # apart from arguments genuinely passed to `source`. Dropping empty
    # arguments gives a script with its own parameters a deterministic way to
    # ask for none: `source eng/activate-iltools.sh ""`.
    for arg in "$@"; do
        if [ -n "$arg" ]; then
            args+=("$arg")
        fi
    done

    # `${var%/*}` rather than dirname(1): this file's whole job is fixing PATH,
    # so it must not need PATH to work.
    self="${BASH_SOURCE[0]}"
    dir="${self%/*}"
    if [ "$dir" = "$self" ]; then
        dir="."
    fi

    # CDPATH= disables `cd`'s alternate-directory search. With CDPATH set -- a
    # common interactive setting, and this file is meant to be sourced
    # interactively -- a match there makes `cd` print the resolved directory on
    # stdout, which command substitution then captures ahead of `pwd`. The
    # result is a two-line script_dir that resolves to nothing. `--` keeps a
    # directory starting with `-` from being read as an option.
    script_dir="$(CDPATH= cd -- "$dir" && pwd)" || return 1
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
    out="$("$restore" ${args[@]+"${args[@]}"})" || status=$?
    if [ "$status" -ne 0 ]; then
        printf 'error: %s failed (exit %d); PATH left unchanged.\n' "$restore" "$status" >&2
        __iltools_hint_arguments "$self" "${#args[@]}"
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

        # A directory containing ':' cannot be represented in PATH at all: it
        # would split into two elements, and a colon-only line would produce
        # empty ones, which mean the current directory. Neither is something to
        # paper over -- refuse rather than silently corrupt PATH or silently
        # drop the directory.
        case "$line" in
            *:*)
                printf 'error: %s emitted a directory containing ":" (%s), which\n' "$restore" "$line" >&2
                printf '       cannot be represented in PATH; PATH left unchanged.\n' >&2
                return 1
                ;;
        esac

        # Guards only against the producer emitting one directory twice.
        # Directories already on PATH are handled below by moving them, not by
        # skipping them.
        if __iltools_path_has "$line" "$joined"; then
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
        # `--help` leaking in as an inherited parameter lands here rather than
        # on the usage-error path: restore exits 0 having printed only usage.
        __iltools_hint_arguments "$self" "${#args[@]}"
        return 1
    fi

    if [ -z "${PATH:-}" ]; then
        # Appending ':$PATH' here would leave a trailing empty element.
        export PATH="$joined"
        return 0
    fi

    # Rebuild the tail without the restored directories rather than skipping a
    # directory that is already present. Skipping keeps PATH from growing on a
    # repeat source, but it also leaves a stale or broken copy earlier in PATH
    # shadowing the one just restored: success reported, wrong tool resolved.
    # Removing and re-prepending gets both. Every other element is preserved
    # verbatim and in order, including empty ones -- this file must not
    # introduce an empty PATH element, but it is not in the business of
    # deleting the caller's.
    tail=""
    first=1
    rest="$PATH"
    while :; do
        element="${rest%%:*}"

        if ! __iltools_path_has "$element" "$joined"; then
            if [ "$first" -eq 1 ]; then
                tail="$element"
                first=0
            else
                tail="$tail:$element"
            fi
        fi

        case "$rest" in
            *:*) rest="${rest#*:}" ;;
            *) break ;;
        esac
    done

    if [ "$first" -eq 1 ]; then
        export PATH="$joined"
    else
        export PATH="$joined:$tail"
    fi
}

__iltools_activate "$@"
__iltools_status=$?
unset -f __iltools_activate __iltools_path_has __iltools_hint_arguments
return "$__iltools_status"
