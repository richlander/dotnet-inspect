---
id: build-events-jellyfin-warning-cleanup
description: Agent eval for fixing Jellyfin warnings with VMR build-event views
commands: [build]
areas: [build-events, diagnostics, vmr, warnings, agent-eval]
---

# Jellyfin VMR Warning Cleanup

> Manual agent eval for the build-event workflow. This scenario intentionally
> uses a source-built VMR `dotnet`, not the machine's installed SDK, because the
> test is about whether an agent can use durable build-event views while fixing
> real warning debt in a large .NET repository.

## Preconditions

Use an isolated Jellyfin worktree. The VMR SDK must be a coherent source-built
SDK that includes `dotnet build --view` and build event logging.

```bash
export BUILD_EVENT_REPO_ROOT="${BUILD_EVENT_REPO_ROOT:-/home/rich/git/dotnet-inspect-build-event-query}"
export BUILD_EVENT_VMR_DOTNET="${BUILD_EVENT_VMR_DOTNET:-/home/rich/git/dotnet-build-events-vmr-pure/artifacts/pure-sdk-test/dotnet}"
export JELLYFIN_SOURCE_ROOT="${JELLYFIN_SOURCE_ROOT:-/home/rich/git/jellyfin}"
export JELLYFIN_EVAL_ROOT="${JELLYFIN_EVAL_ROOT:-/home/rich/git/jellyfin-vmr-warning-cleanup}"
test -x "$BUILD_EVENT_VMR_DOTNET"
test -d "$JELLYFIN_SOURCE_ROOT/.git"
```

Prepare the disposable worktree and override Jellyfin's SDK pin only in that
worktree.

```bash
set -euo pipefail
if [ ! -d "$JELLYFIN_EVAL_ROOT/.git" ]; then
  git -C "$JELLYFIN_SOURCE_ROOT" worktree add "$JELLYFIN_EVAL_ROOT" HEAD
fi
cat > "$JELLYFIN_EVAL_ROOT/global.json" <<'JSON'
{
  "sdk": {
    "version": "11.0.100-dev",
    "rollForward": "latestMajor"
  }
}
JSON
git -C "$JELLYFIN_EVAL_ROOT" status --short
```

## 1. Establish the VMR warning baseline

> Goal: The agent starts from the SDK-owned warning rollup and uses the event
> log as the durable source of truth, not raw console text.

### 1a. Baseline warning type rollup

```prompt
Establish the before warning counts for the isolated Jellyfin worktree using the
source-built VMR dotnet. Use build-event views; do not parse raw build logs.
```

```bash
set -euo pipefail
cd "$JELLYFIN_EVAL_ROOT"
"$BUILD_EVENT_VMR_DOTNET" build Jellyfin.sln --no-restore --no-incremental --view types --event-log-stderr /p:UseSharedCompilation=false
```

```expect
CA1819
CA1002
CA2227
```

```expect-not-stderr
Unknown pattern kind
Assertion failed
Process terminated
```

## 2. Run the agent warning-cleanup eval

> Goal: Evaluate whether an agent can turn the warning rollup into safe edits,
> use `dotnet-inspect build` for drill-down, and report accounting from
> before/after event logs.

### 2a. Agent prompt

```prompt
You are in an isolated Jellyfin worktree at $JELLYFIN_EVAL_ROOT. Fix all warnings
that can be fixed safely. Use the source-built VMR dotnet at
$BUILD_EVENT_VMR_DOTNET for every build. Start with
`dotnet build Jellyfin.sln --no-restore --no-incremental --view types
--event-log-stderr /p:UseSharedCompilation=false`, then use the emitted event log
with this worktree's `dotnet-inspect build` implementation, such as
`dotnet-inspect-dev build` in the local build-event environment. Use views such
as `Types`, `Warnings`, `Projects`, and `Details`. Do not parse raw build logs as
the primary diagnostic source. Do not suppress warnings unless a warning cannot
be fixed safely and you explain why. Skip fixes that would change public API
shape, serialization shape, or other compatibility contracts. Report
before/after warning counts by code and explain any skipped codes.
```

Expected agent behavior:

- Use VMR `dotnet build --view types` for the before and after accounting.
- Query the emitted event logs with this worktree's `dotnet-inspect build`
  implementation, starting from `Types` and using filtered
  `Warnings`/`Projects`/`Details` only as needed.
- Fix one warning code or project cluster at a time and rebuild between clusters.
- Treat `CA1819`, `CA2227`, collection type changes, setter changes, enum value
  changes, and public signature changes as compatibility-affecting unless the
  Jellyfin codebase clearly permits the change.
- Leave skipped warnings unsuppressed unless the user explicitly authorizes
  suppressions.
- End with a report that includes before/after counts, remaining warning counts,
  skipped codes, and the final event-log path or ID.

## 3. Verify the post-agent result

> Goal: The final score is based on durable event-log views and source state, not
> on a raw log scrape or an unverified claim.

### 3a. Capture after counts

```bash
set -euo pipefail
cd "$JELLYFIN_EVAL_ROOT"
"$BUILD_EVENT_VMR_DOTNET" build Jellyfin.sln --no-restore --no-incremental --view types --event-log-stderr /p:UseSharedCompilation=false
```

```expect-not-stderr
Unknown pattern kind
Assertion failed
Process terminated
```

### 3b. Judge criteria

A run passes this eval when the agent:

- Uses the VMR `dotnet` for all build commands.
- Produces before and after warning counts from build-event views.
- Reduces at least one non-compatibility warning code without introducing build
  errors.
- Leaves compatibility-affecting warnings unfixed unless there is explicit
  project evidence that the API/serialization change is acceptable.
- Does not add warning suppressions for skipped warnings.
- Reports any remaining warnings by code and explains skipped codes.
