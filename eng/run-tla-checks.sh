#!/usr/bin/env bash
set -e -o pipefail

# Upholds the bar in docs/tla-plus-methodology.md: every checked-in TLA+
# model's module parses (SANY) and TLC runs each of its .cfg files to
# completion without an unexpected error.
#
# Deliberately NOT enforced here: whether TLC's verdict is "no violation
# found". Several .cfg files in this repository are committed negative
# controls or mutation probes that are EXPECTED to report a safety or
# liveness violation (see docs/tla-plus-methodology.md and each model's
# README "Checked configurations" table). There is no repository-wide,
# machine-readable convention recording which verdict a given .cfg expects,
# so this gate cannot safely fail a PR merely because TLC found a violation
# -- that would break the very configs whose job is to find one. Distinguish
# "TLC completed its job" from "TLC agrees with the model" by TLC's own exit
# status, using the codes documented in the tlc2.output.EC.ExitStatus enum:
#   0  SUCCESS
#   10 VIOLATION_ASSUMPTION
#   11 VIOLATION_DEADLOCK
#   12 VIOLATION_SAFETY
#   13 VIOLATION_LIVENESS
#   14 VIOLATION_ASSERT
# are all "TLC ran the model and reported a coherent verdict" -- some of
# those verdicts are violations by design. Every other exit code (parse
# errors, config errors, evaluation failures, state-space exhaustion, JVM
# crashes, or an unrecognized code) is treated as the "unexpected error"
# this gate exists to catch.
#
# Also deliberately NOT a CI failure: a .cfg that does not finish within
# TLA_CHECK_TIMEOUT_SECONDS. Some committed models are exhaustive checks over
# hundreds of millions of states and can legitimately run far longer than any
# per-PR budget this repository is willing to pay on a shared runner (see the
# timeout branch below). That config is reported unverified this run rather
# than failing the PR.
#
# A .cfg's module is normally inferred (the sole .tla in its directory, or a
# same-named one); eng/tla-module-overrides.txt overrides that for
# directories where a .cfg must instead run against a model-checking harness
# module.

if [ -z "${TLA_TOOLS_JAR:-}" ]; then
  echo "::error::TLA_TOOLS_JAR is not set." >&2
  exit 1
fi
if [ ! -f "$TLA_TOOLS_JAR" ]; then
  echo "::error::TLA_TOOLS_JAR ($TLA_TOOLS_JAR) does not exist." >&2
  exit 1
fi

# Per-invocation wall-clock bound. Some committed models are large exhaustive
# checks (hundreds of millions of states) that legitimately run far longer
# than this; see the timeout handling below for why that is a warning, not a
# CI failure. A model this budget cannot even start exploring within is still
# worth flagging loudly, so the bound stays generous rather than unbounded.
: "${TLA_CHECK_TIMEOUT_SECONDS:=600}"

OK_EXIT_CODES="0 10 11 12 13 14"

MODEL_ROOTS=(docs/design/models docs/models)
MODULE_OVERRIDES_FILE=eng/tla-module-overrides.txt

override_module_for() {
  local cfg_path="$1"
  if [ ! -f "$MODULE_OVERRIDES_FILE" ]; then
    return 1
  fi
  local line module
  while IFS= read -r line || [ -n "$line" ]; do
    case "$line" in
      ""|"#"*) continue ;;
    esac
    if [ "${line%%=*}" = "$cfg_path" ]; then
      module="${line#*=}"
      printf '%s\n' "$module"
      return 0
    fi
  done < "$MODULE_OVERRIDES_FILE"
  return 1
}

is_ok_exit_code() {
  local code="$1"
  local candidate
  for candidate in $OK_EXIT_CODES; do
    if [ "$candidate" = "$code" ]; then
      return 0
    fi
  done
  return 1
}

FAILURES=0
CHECKED_MODULES=0
CHECKED_CONFIGS=0
TIMEOUTS=0

# Validate the whole override file up front, before any model checking
# begins, so a config mistake in it fails loudly and specifically instead of
# silently degrading to "no override" (which falls through to the sole-.tla
# / same-basename fallback and can pair a .cfg against the wrong module) or
# aborting this script with no diagnostic at all.
if [ -f "$MODULE_OVERRIDES_FILE" ]; then
  seen_keys_file=$(mktemp)
  line_no=0
  while IFS= read -r line || [ -n "$line" ]; do
    line_no=$((line_no + 1))
    case "$line" in
      ""|"#"*) continue ;;
    esac
    case "$line" in
      *=*) ;;
      *)
        echo "::error::$MODULE_OVERRIDES_FILE:$line_no is malformed ('$line'): expected '<cfg-path>=<module>'." >&2
        FAILURES=$((FAILURES + 1))
        continue
        ;;
    esac
    key="${line%%=*}"
    value="${line#*=}"
    trimmed_key="${key#"${key%%[![:space:]]*}"}"
    trimmed_key="${trimmed_key%"${trimmed_key##*[![:space:]]}"}"
    trimmed_value="${value#"${value%%[![:space:]]*}"}"
    trimmed_value="${trimmed_value%"${trimmed_value##*[![:space:]]}"}"
    if [ "$key" != "$trimmed_key" ] || [ "$value" != "$trimmed_value" ]; then
      echo "::error::$MODULE_OVERRIDES_FILE:$line_no has leading or trailing whitespace around '=' ('$line'); remove it so the mapping is unambiguous." >&2
      FAILURES=$((FAILURES + 1))
      continue
    fi
    if [ -z "$key" ] || [ -z "$value" ]; then
      echo "::error::$MODULE_OVERRIDES_FILE:$line_no has an empty cfg path or module name ('$line')." >&2
      FAILURES=$((FAILURES + 1))
      continue
    fi
    if [ ! -f "$key" ]; then
      # A stale/typo'd key never matches any real .cfg, so it silently does
      # nothing -- the override is dropped and, if a .cfg later exists at a
      # path close enough to fall into the same-basename/sole-.tla fallback,
      # it can be silently checked against the wrong module instead.
      echo "::error::$MODULE_OVERRIDES_FILE:$line_no names '$key', which does not exist. Fix the path or remove the stale entry." >&2
      FAILURES=$((FAILURES + 1))
      continue
    fi
    printf '%s\n' "$key" >> "$seen_keys_file"
  done < "$MODULE_OVERRIDES_FILE"

  # override_module_for() returns on the first key match, so a second,
  # conflicting entry for the same cfg path would otherwise be silently
  # ignored and the cfg checked against whichever module happened to be
  # listed first, rather than the file being treated as a config error.
  # sort/uniq on a file with zero well-formed lines (e.g. comment-only, or
  # entirely malformed) is not itself an error -- there is simply nothing to
  # deduplicate -- so this must not raise a spurious failure in that case.
  duplicate_keys=$(sort "$seen_keys_file" | uniq -d)
  if [ -n "$duplicate_keys" ]; then
    while IFS= read -r key; do
      echo "::error::$MODULE_OVERRIDES_FILE has more than one entry for '$key'. Remove the duplicate so the module mapping is unambiguous." >&2
      FAILURES=$((FAILURES + 1))
    done <<< "$duplicate_keys"
  fi
  rm -f "$seen_keys_file"
fi

# The discovery loop below only picks up .tla/.cfg files that live exactly
# one directory level under each model root (docs/design/models/<model>/*.tla).
# That matches every model directory's actual layout today, but a file placed
# directly in the root itself (mindepth 1, no model subdirectory) or nested
# any deeper (mindepth 3+) would be silently invisible to that loop, while
# still matching eng/ci-detect-changes.sh's classification (its `case`
# patterns span `/`, so they are deliberately broader than this script's
# layout assumption). Fail loudly rather than silently skip such a file.
for root in "${MODEL_ROOTS[@]}"; do
  [ -d "$root" ] || continue
  while IFS= read -r -d '' misplaced; do
    echo "::error::$misplaced is not in the layout eng/run-tla-checks.sh supports (a model directory exactly one level under $root). Move it into its own model directory directly under $root, or extend this script's discovery to handle this layout." >&2
    FAILURES=$((FAILURES + 1))
  done < <(find "$root" -mindepth 1 -maxdepth 1 \( -iname "*.tla" -o -iname "*.cfg" \) -print0)
  while IFS= read -r -d '' nested; do
    echo "::error::$nested is nested deeper than eng/run-tla-checks.sh supports (one directory level under $root). Move it into a model directory directly under $root, or extend this script's discovery to handle nesting." >&2
    FAILURES=$((FAILURES + 1))
  done < <(find "$root" -mindepth 3 \( -iname "*.tla" -o -iname "*.cfg" \) -print0)
  # find's directory/file discovery below does not follow symlinks (no -L),
  # so a symlinked model directory -- or a symlinked file inside one -- is
  # invisible to every check in this script: it is not -type d, so the main
  # loop's directory discovery skips it entirely, and it is skipped by the
  # rejection scans above for the same reason. Reject any symlink under a
  # model root outright rather than silently ignoring whatever it hides.
  while IFS= read -r -d '' link; do
    echo "::error::$link is a symlink; eng/run-tla-checks.sh does not follow symlinks under $root and cannot verify what it points to. Replace it with a real file or directory." >&2
    FAILURES=$((FAILURES + 1))
  done < <(find "$root" -type l -print0)
done

for root in "${MODEL_ROOTS[@]}"; do
  [ -d "$root" ] || continue
  while IFS= read -r -d '' dir; do
    tla_files=()
    while IFS= read -r -d '' file; do
      tla_files+=("$file")
    done < <(find "$dir" -maxdepth 1 -iname "*.tla" -print0 | sort -z)

    cfg_files=()
    while IFS= read -r -d '' file; do
      cfg_files+=("$file")
    done < <(find "$dir" -maxdepth 1 -iname "*.cfg" -print0 | sort -z)

    if [ "${#tla_files[@]}" -eq 0 ] && [ "${#cfg_files[@]}" -eq 0 ]; then
      continue
    fi

    echo "::group::$dir"

    for tla in "${tla_files[@]}"; do
      module=$(basename "$tla" .tla)
      echo "-- SANY: $module"
      if ! (cd "$dir" && java -cp "$TLA_TOOLS_JAR" tla2sany.SANY "$module.tla" </dev/null); then
        echo "::error::SANY could not parse $dir/$module.tla" >&2
        FAILURES=$((FAILURES + 1))
        continue
      fi
      CHECKED_MODULES=$((CHECKED_MODULES + 1))
    done

    if [ "${#tla_files[@]}" -eq 0 ]; then
      # A directory with .cfg files but no .tla module: every .cfg here is
      # orphaned and must fail loud rather than silently go unchecked (a
      # bare `continue` before this point, without also considering
      # cfg_files, would let a PR delete the sole module out from under
      # its configs and still report success).
      for cfg in "${cfg_files[@]}"; do
        echo "::error::$dir/$(basename "$cfg") has no .tla module in $dir to check it against." >&2
        FAILURES=$((FAILURES + 1))
      done
      echo "::endgroup::"
      continue
    fi

    for cfg in "${cfg_files[@]}"; do
      cfg_name=$(basename "$cfg" .cfg)
      module=$(override_module_for "$dir/$cfg_name.cfg" || true)
      if [ -z "$module" ]; then
        if [ "${#tla_files[@]}" -eq 1 ]; then
          module=$(basename "${tla_files[0]}" .tla)
        else
          for tla in "${tla_files[@]}"; do
            candidate=$(basename "$tla" .tla)
            if [ "$candidate" = "$cfg_name" ]; then
              module="$candidate"
              break
            fi
          done
        fi
      fi

      if [ -z "$module" ]; then
        echo "::error::$dir/$cfg_name.cfg does not match any single module in $dir, has no matching-name module, and has no entry in $MODULE_OVERRIDES_FILE. Add one instead of letting this cfg go unchecked." >&2
        FAILURES=$((FAILURES + 1))
        continue
      fi
      if [ ! -f "$dir/$module.tla" ]; then
        echo "::error::$MODULE_OVERRIDES_FILE names module '$module' for $dir/$cfg_name.cfg, but $dir/$module.tla does not exist." >&2
        FAILURES=$((FAILURES + 1))
        continue
      fi

      echo "-- TLC: $cfg_name.cfg against $module (timeout ${TLA_CHECK_TIMEOUT_SECONDS}s)"
      log=$(mktemp)
      set +e
      (cd "$dir" && timeout "${TLA_CHECK_TIMEOUT_SECONDS}s" \
        java -XX:+UseParallelGC -cp "$TLA_TOOLS_JAR" tlc2.TLC \
        -workers auto -cleanup -noGenerateSpecTE \
        -config "$cfg_name.cfg" "$module") < /dev/null > "$log" 2>&1
      exit_code=$?
      set -e
      CHECKED_CONFIGS=$((CHECKED_CONFIGS + 1))

      if [ "$exit_code" -eq 124 ]; then
        # Not a CI failure: some committed models are exhaustive checks over
        # hundreds of millions of states (see PackageCachePublicationSafety's
        # recorded state count) that legitimately run far longer than this
        # per-invocation budget on a shared runner. Failing PRs on that would
        # make an unrelated change's CI time depend on the slowest model in
        # the repository. This config was not verified this run; the
        # repository's Deep Inspect lane is the place for a full run.
        echo "::warning::TLC did not finish $dir/$cfg_name.cfg within ${TLA_CHECK_TIMEOUT_SECONDS}s; not verified this run. Run it directly (see the model's README) or via Deep Inspect for full verification." >&2
        TIMEOUTS=$((TIMEOUTS + 1))
      elif ! is_ok_exit_code "$exit_code"; then
        echo "::error::TLC reported an unexpected error (exit $exit_code) for $dir/$cfg_name.cfg" >&2
        tail -n 60 "$log" >&2
        FAILURES=$((FAILURES + 1))
      else
        tail -n 5 "$log"
      fi
      rm -f "$log"
    done

    echo "::endgroup::"
  done < <(find "$root" -mindepth 1 -maxdepth 1 -type d -print0 | sort -z)
done

echo "Checked $CHECKED_MODULES module(s) and $CHECKED_CONFIGS configuration(s) ($TIMEOUTS not verified within budget)."

if [ "$FAILURES" -gt 0 ]; then
  echo "::error::$FAILURES TLA+ check(s) failed." >&2
  exit 1
fi
