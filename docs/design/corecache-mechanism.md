# CoreCache mechanism

This document owns the mechanism contract of `DotnetInspector.Core`'s
`CoreCache`: the process-wide cache root, the filesystem key scheme,
path-context containment, initialization lifecycle, versioned category
maintenance, and the `maxAge` freshness comparison. It does not own `AsyncCache`, an unrelated in-memory
single-flight coalescer in the same project with no lifecycle dependency on
`CoreCache` — see [Non-claims](#non-claims). It does not own cache
semantics — the complete key,
freshness, validation, publication, and concurrency behavior each cached
result requires is the calling owner's responsibility, per
[the `CoreCache` section of the inspection space](../inspection-space.md#corecache).
It also does not own package-content publication, single-flight, or
cross-process atomicity; that is
[cache concurrency and publication](cache-concurrency.md)'s `Directory.Move`
staging protocol, which is built on top of this mechanism rather than part of
it.

Retro-spec note: this document was written against the existing
`CoreCache.cs` implementation to give the mechanism an owning contract it
never had. Sections marked **Gap** describe behavior the current code exhibits
but does not enforce; closing a gap is follow-up work, not part of this
docs-only change.

## Responsibility

`CoreCache` supplies, for any caller-chosen category:

- a stable base directory scoped to the current application name and
  platform (or an override);
- a collision-resistant, filesystem-safe path for a caller-chosen key within
  a category;
- read, write, and background-maintenance operations whose *filesystem-access*
  failures are swallowed and observed only as a miss or as maintenance making
  no progress. This is not a blanket best-effort guarantee: path construction
  (which reads `AppName`, throwing before initialization) and miss-side
  telemetry both run outside the guarded region and propagate — see
  [Telemetry](#telemetry-is-fire-and-forget-but-not-exception-isolated) and
  the pre-initialization note in
  [Initialization lifecycle](#initialization-lifecycle);
- a `Clear` operation that surfaces most failures rather than swallowing them
  (see [Clear and concurrent writers](#clear-and-concurrent-writers)); and
- a path-context guard, `EnsurePathInCacheContext`/`IsPathInCacheContext`,
  that both `Clear` and other owners may call before their own deletions (see
  [Path-context containment](#path-context-containment)).

Everything else — what a category or key *means*, whether a hit is still
valid, and whether a miss may trigger network work — belongs to the caller.

## Trust boundary: category, key, and extension are not equally trusted

`GetFilePath(category, key, extension)` treats its three string parameters
differently, but the difference is enforced for only one of them:

- **`key`** is always SHA-256 hashed before it reaches the filesystem
  (`GetFilePath`'s `hashString[..2]/hashString[2..].{extension}` layout). A
  key built from untrusted content — a package id, a URL, source text — can
  never produce a path outside the two-level hash bucket.
- **`category`** is concatenated into the path verbatim (`GetCategoryPath` is
  `Path.Combine(GetBasePath(), category)`), and `GetCacheInfo(category)` uses
  the same unguarded path to recursively measure and enumerate files. Every
  traced producer ultimately restricts `category` to a fixed literal or
  `const string` (for example `SymbolMissCacheCategory`, `"effective-v28"`,
  `"sources"`) — but `CoreCache` does not enforce this at its own boundary,
  and at least one caller does not pass a literal at the `CoreCache` call
  expression itself: `CoreSourceLinkQueryCache` (`SourceLinkQueryCache.cs`)
  forwards its `category`/`extension` interface parameters straight through
  to `CoreCache.TryGet`/`Set`, so the literal-only property holds only
  transitively, at each producer's own call site, not at every direct call
  into `CoreCache`.
- **`extension`** is concatenated verbatim after the hash suffix. Most call
  sites pass a literal (`"json"`, `"forbidden"`, `"miss"`, `"md"`, `"tsv"`),
  but `SymbolPackageDownloader.Storage.cs` passes a local variable selected
  from a fixed two-value set, and the `CoreSourceLinkQueryCache` forwarding
  above applies to `extension` too — the same transitive-only caveat holds.
- **`appName`** (`Initialize`'s first parameter) is concatenated verbatim into
  every platform's default base path (`GetDefaultBasePath`'s
  `Path.Combine(..., AppName)` on each branch); `Initialize` only rejects a
  null or all-whitespace value. The current production caller passes one
  fixed literal at process startup.
- **`basePath`** (`Initialize`'s second, optional parameter) is returned
  verbatim by `GetBasePath()` with no validation at all — not even the
  null/whitespace check `appName` gets — and becomes the trusted root that
  `IsPathInCacheContext` anchors every containment decision to. A caller that
  passes an override derived from untrusted input controls the cache root
  itself, which is a strictly larger trust concession than `category` or
  `extension`.

The contract is: **`category`, `extension`, `appName`, and `basePath` are
caller-owned values that every current producer restricts to a fixed literal
or a bounded, hardcoded selection, never external content; `key` is the only
parameter this mechanism defends by construction.** This must hold for every
future caller, including one that builds a category name from a feed,
package, or platform identifier, or a `basePath` from a configuration value
of unknown provenance — those must be routed through `key`, not `category`,
`extension`, `appName`, or `basePath`.

**Gap:** the contract above is enforced only by code review convention today.
`GetCategoryPath`, `GetFilePath`, `GetDefaultBasePath`, `GetCacheInfo`,
`TryGet`, `TryGetBytes`, `Set`, and `SetBytes` do not reject a `category`,
`extension`, or `appName` containing a path separator or a `..` segment, and
`GetBasePath`/`Initialize` place no constraint on `basePath` at all, so a
future caller that violates the contract (for example, building a category
from a package id, or an `extension`/`basePath` from configuration) would
silently gain a path-traversal write/read/enumerate primitive outside the
intended cache root, undetected by any existing test. Closing this gap — for
example, asserting `category`/`extension`/`appName` contain no directory
separator and no `.` segment before every path computation, and constraining
`basePath` to a directory the process already controls — is recommended
follow-up work; this document records the invariant so that follow-up has a
contract to enforce against.

## Path-context containment

`EnsurePathInCacheContext`/`IsPathInCacheContext` are a public guard against
deleting outside the cache: a path is in context when it equals or is a
descendant of the active base path (`GetBasePath()`) or the legacy pre-XDG
path (`GetLegacyBasePath()`). `IsPathInCacheContext` fails closed — any
exception while resolving the full path (malformed path, denied access)
returns `false`, never `true`. `Clear` calls it internally, and other owners
call it directly before their own destructive filesystem operations —
package-content and staging deletion (`NuGetCache`), platform-pack target and
staging deletion (`PlatformPackService`), and legacy-cache deletion
(`PackageCacheService`) all invoke it. It is the mechanism's one exported
containment primitive; any future caller that deletes a path derived from
`GetBasePath()`/`GetCategoryPath()` should call it too.

**Gap:** the descendant check (`IsSameOrChildPath`) and the root-equality
check used to carry maintenance counters across re-`Initialize`
(`IsSamePath`) both compare paths with `StringComparison.OrdinalIgnoreCase`
unconditionally, on every platform. On a filesystem that is actually
case-sensitive — ordinary Linux ext4, or a case-sensitive-formatted
APFS/NTFS volume (the default macOS and Windows format is case-insensitive) —
a path that differs from the cache root only in case is accepted as a
descendant, or two re-`Initialize` roots differing only in case are treated
as the same root, even though each names a distinct location on disk. So
neither the "cannot escape the cache root" guarantee below, nor the
counter-carry-forward rule in
[Initialization lifecycle](#initialization-lifecycle), holds on such a
filesystem. This document's containment claims describe the guard's intended
behavior, not a verified guarantee on a case-sensitive filesystem; treat that
as an open, unenforced case, not as closed by `EnsurePathInCacheContext`/
`IsSamePath` alone.

**Gap:** `CoreCache`'s own read/write/statistics entry points (`TryGet`,
`TryGetBytes`, `Set`, `SetBytes`, `GetFilePath`, `GetCacheInfo`) never call
the guard themselves — only `Clear` and the external callers above do.
`GetCacheInfo(category)` in particular recursively measures and enumerates
every file under `GetCategoryPath(category)` with no containment check and
no exception handling of its own, so a category contract violation reaching
it is also an out-of-root enumeration read, not just a write/delete
primitive. Because `category` is contract-restricted to literals (see
above), a conforming caller never needs the guard on the read/write/stats
path today — but the guard's absence there means a
`category`/`extension`/`appName` contract violation reaching `TryGet`/`Set`/
`GetCacheInfo` directly (rather than through `Clear` or one of the callers
that already guards itself) is not caught by this mechanism at all. A future
defensive check on `category`/`extension`/`appName` (the trust-boundary gap
above) would close this without relying on every future caller remembering
to call the guard itself.

## Initialization lifecycle

`Initialize(appName, basePath)` is not an idempotent no-op past the first
call: it cancels and best-effort drains any in-flight versioned-category
maintenance, replaces the maintenance generation, and re-schedules cleanup
for every category registered so far — against the *new* base path if one was
given. Reclaimed-byte/directory counters carry forward only when the new base
path resolves to the same location as the old one (`IsSamePath` — subject to
the case-sensitivity gap in
[Path-context containment](#path-context-containment)); otherwise they reset,
because the counters describe one cache root's history and a root change
starts a new one.

`RegisterVersionedCategory` may be called before or after `Initialize`, and
repeated registration of the same prefix is idempotent only when `current`
is unchanged; a second registration with a different `current` for a
known prefix throws. Registered categories are never forgotten across a later
`Initialize` call — re-initializing to a different root replays cleanup for
every previously registered category under that new root.

**Gap:** `_appName`/`_basePathOverride` are written only inside `Initialize`'s
`lock (s_maintenanceLock)`, and every other method that also takes that lock
— `RegisterVersionedCategory`, `Clear`, `WaitForMaintenance` (and therefore
`CancelAndWaitForMaintenance`), and `RequestVersionedCategoryCleanupAsync` —
is safely serialized against a concurrent `Initialize`; none of these can
observe a partial field write. The unsynchronized race is narrower: the
lock-free path/read/write/statistics surface —`GetBasePath`, `GetDefaultBasePath`,
`GetCategoryPath`, `GetFilePath`, `TryGet`/`TryGetBytes`, `Set`/`SetBytes`, and
`GetCacheInfo` — reads `_appName`/`_basePathOverride` without the lock and
without `volatile`. **At most one `Initialize` call may be outstanding, and no
lock-free method may run concurrently with it.** Today's callers satisfy this
by calling `Initialize` once at process startup before any other cache use,
but the contract is not stated anywhere and not enforced by an assertion. A
production build that calls `Initialize` a second time for any reason (for
example, a hosted/long-lived process switching app identity) while a
concurrent `TryGet`/`Set` is in flight has a data race on
`_appName`/`_basePathOverride`.

Before the first `Initialize` call, every lock-free method throws
`InvalidOperationException` from the `AppName` property getter — this is not
best-effort and not a miss; `Set`/`SetBytes` only *look* silent because that
exception is caught by their own blanket `try`/`catch` (see
[Telemetry](#telemetry-is-fire-and-forget-but-not-exception-isolated)),
while `TryGet`/`TryGetBytes` construct the path (and so throw) before
entering any guarded region at all.

## Versioned category retirement

A versioned category family is identified by a `prefix` plus the current
member's own integer suffix (for example prefix `pkg-index-v`, current
`pkg-index-v8`). Retirement:

- deletes only sibling directories whose suffix parses as a non-negative
  integer strictly less than the current version;
- leaves alone any sibling whose suffix is unparsable, equal to, or greater
  than the current version — this is what lets an older executable run
  beside or before a newer one without destroying a contract version it
  does not recognize (see
  [versioned cache retirement](cache-concurrency.md#versioned-cache-retirement)
  for the cross-process rationale); and
- runs as fire-and-forget background `Task.Run` work scoped to a
  `CancellationTokenSource` generation; a new `Initialize` call cancels the
  previous generation's tasks, waits for them best-effort, and starts a new
  generation rather than waiting for the old one to finish on its own.

`CancelAndWaitForMaintenance`/`Clear` are the only **public** ways a caller
observes completed maintenance (the internal `RequestVersionedCategoryCleanupAsync`
is a third, assembly-internal path that test code uses directly to await the
real aggregate task). Both public APIs can also return *partial* progress
rather than confirmation that maintenance fully drained: `WaitForMaintenance`
waits for the timeout given, then — if the task has not completed — cancels
it and waits only another 25ms before returning whatever progress has been
recorded so far, regardless of whether the canceled task has actually
finished exiting. **Every** `Clear` call — not only `Clear(category:
null)` — unconditionally waits (`Timeout.InfiniteTimeSpan`) for the current
maintenance generation before deleting anything; only `Clear(null)` also
*consumes* the recorded byte counter into its return value (`Clear(category)`
waits the same way but reports `0` maintenance bytes, and neither overload's
return value includes the directory-deleted count — see
[Clear and concurrent writers](#clear-and-concurrent-writers)).
Routine (non-`Clear`) maintenance is silent: a caller that never calls
`Clear` or `CancelAndWaitForMaintenance` never learns whether background
retirement ran, succeeded, or was skipped because a prior run left the
directory absent.

## `Clear` and concurrent writers

`Clear` is not best-effort like the read/write paths: it takes
`s_maintenanceLock`, drains maintenance, validates the target path is in
cache context, measures the tree, and deletes it, catching only
`DirectoryNotFoundException` for the specific case where another process
already completed the same deletion. Any other failure — an authorization
error, an `IOException` from a file another process still has open, or a
directory-enumeration error while measuring size — propagates to `Clear`'s
caller rather than being swallowed. `Clear`'s `long` return value is bytes
freed only: `Clear(null)` consumes both the byte and directory counters from
maintenance (see [Versioned category retirement](#versioned-category-retirement))
but adds only the byte count to its return value — a caller that needs the
directory count must use `CancelAndWaitForMaintenance` instead.

**Gap:** `Clear` does not coordinate with concurrent `Set`/`SetBytes` calls,
which do not take `s_maintenanceLock` at all — and `s_maintenanceLock` is a
process-local, in-memory lock that provides no cross-process coordination
regardless. This is not a hypothetical future-caller concern: `dotnet-inspect
cache clear` (`CacheCommand.cs` → `PackageCacheService.ClearCache()` →
`CoreCache.Clear()`) is an independently invokable CLI command, so any other
concurrently running `dotnet-inspect` process performing ordinary cache reads
or writes already races with it today, in production, not only in a
hypothetical future caller within the same process. A `Set` that created its
category subdirectory before `Clear`'s `Directory.Delete` can produce three
outcomes, none of them a contract this mechanism enforces: a successful write
that `Clear` then removes (consistent with "clear empties the cache"); a
`WriteAtomically` `File.Move` into a directory `Clear` just deleted, caught by
`Set`'s blanket `try`/`catch` and silently dropped; or a filesystem error
surfaced *from `Clear` itself* (for example `Directory.Delete` encountering
the writer's still-open temporary file) that is not the narrow
already-deleted case `Clear` catches, and so propagates to `Clear`'s caller.
A caller relying on "the value I just set survives" immediately after a
concurrent `Clear`, or relying on `Clear` never throwing because reads and
writes elsewhere are best-effort, has no contract to rely on. No caller
within a single process clears a category while concurrently writing to it
today, but two separate `dotnet-inspect` invocations racing this way is
already possible; closing this gap (a future caller, in-process or
cross-process) must either serialize its own writes around a `Clear` or
accept both the silent-loss and the `Clear`-throws possibilities.

## Telemetry is fire-and-forget but not exception-isolated

`InfoTracker.RecordCacheHit`/`RecordCacheMiss` and `CacheTelemetry.Record` are
recorded on cache outcomes, but recording itself does nothing to protect a
cache result: `CacheTelemetry.Record` first adds an `ActivityEvent` to
`Activity.Current` unconditionally, then calls every subscribed
`IObserver<CacheObservation>.OnNext` synchronously and in registration order,
with no surrounding `try`/`catch`. The activity event and every subscriber
before a throwing one have already observed the outcome by the time an
exception surfaces; only subscribers *after* the throwing one are skipped,
and `Record` itself does not complete normally. A throwing subscriber then
changes cache behavior differently depending on which method and overload
observed it:

- **`TryGet`/`TryGetBytes` without `maxAge`:** the hit-telemetry call is
  inside the method's own blanket `catch`, so a throwing subscriber silently
  turns a hit into a miss (`null`); the miss path — including its own
  telemetry call — then runs unguarded, so a throwing subscriber there
  propagates to the caller instead.
- **`TryGet`/`TryGetBytes` *with* `maxAge`:** the hit-telemetry call is inside
  a `catch` that swallows the exception and falls through to the *same*
  unguarded miss-telemetry call below it. A subscriber that throws on the
  hit observation therefore still reaches the miss observation; if that
  subscriber (or another) also throws on the miss observation, the exception
  propagates out of the method — unlike the non-`maxAge` overloads, a
  throwing hit subscriber does not reliably degrade to a silent `null` here.
- **`Set`/`SetBytes`:** the telemetry call is inside the method's own blanket
  `catch`, so a throwing subscriber is swallowed the same way any other write
  failure is — the activity event and any subscribers ahead of the throwing
  one still observed the store; only the throwing subscriber and any after
  it do not.

This mechanism does not isolate telemetry from the operation it is
recording; "fire-and-forget" describes the caller's intent, not an enforced
boundary, and delivery to any given subscriber is not guaranteed once an
earlier subscriber throws. Telemetry may also undercount for reasons
unrelated to a subscriber (a `Set` that never reaches the `try` body's
`CacheTelemetry.Record` call because an earlier line threw is silently
uncounted) and must not be read as an audit trail of cache correctness —
only as an operational signal, and only when every subscriber is known not
to throw.

`CoreCache` also remaps the observed `category` for one family before
telemetry is recorded: `GetTelemetryCategory` rewrites a `category` that
case-insensitively equals `"symbol-misses"` to `"symbol-misses/{extension}"`
(so `TryGet("symbol-misses", key, extension: "forbidden")` reports
`symbol-misses/forbidden`, distinct from `symbol-misses/miss`), and passes
every other `category` through unchanged. This is an intentional, tested part
of the observable telemetry contract, not an undocumented side effect.

## `maxAge` freshness is a mechanism-owned rule, not a caller policy

The *choice* of freshness policy (what `maxAge` to pass, and whether staleness
should trigger a refetch) is the caller's responsibility, per
[the `CoreCache` section of the inspection space](../inspection-space.md#corecache).
But the `maxAge` overloads of `TryGet`/`TryGetBytes` implement one specific,
mechanism-owned comparison that every caller inherits: freshness is judged
solely by `FileInfo.LastWriteTimeUtc`, compared as
`DateTime.UtcNow - info.LastWriteTimeUtc < maxAge`. The comparison is strict,
so an entry written exactly `maxAge` ago is stale, not fresh; and because the
comparison is one-sided, a `LastWriteTimeUtc` in the future (a clock change, a
restored backup, a manipulated file) makes an entry read as fresh for
`maxAge`s of zero or even negative duration. Any failure while resolving
`FileInfo` or reading the file is caught by the same guarded region and
degrades to a miss, exactly like the non-`maxAge` overloads' hit path.

## Non-claims

This document does not define:

- what any specific category's key, freshness, or validation contract is
  (owned by each cache's caller — `SourceLinkQueryCache`, `MetadataFieldCache`,
  `PackageExtractor`'s listing cache, and others each own their own);
- package-content staging, single-flight, or cross-process publication
  (owned by [cache concurrency and publication](cache-concurrency.md)); or
- `AsyncCache`'s in-memory single-flight coalescing, which is a distinct,
  smaller mechanism in the same project and may get its own focused section
  if it accumulates enough undocumented behavior to warrant one.
