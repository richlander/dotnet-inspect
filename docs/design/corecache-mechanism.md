# CoreCache mechanism

This document owns the mechanism contract of `DotnetInspector.Core`'s
`CoreCache`: the process-wide cache root, the filesystem key scheme,
path-context containment, initialization lifecycle, and versioned category
maintenance. It does not own `AsyncCache`, an unrelated in-memory
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
- best-effort read, write, and background-maintenance operations over that
  path — failures are swallowed and observed only as a miss or as maintenance
  making no progress;
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
  `Path.Combine(GetBasePath(), category)`). Every current call site passes a
  literal or a `const string` (for example `SymbolMissCacheCategory`,
  `"effective-v28"`, `"sources"`); none derive `category` from downloaded or
  otherwise untrusted content.
- **`extension`** is concatenated verbatim after the hash suffix. Every
  current call site passes a literal (`"json"`, `"forbidden"`, `"miss"`,
  `"md"`, `"tsv"`).
- **`appName`** (`Initialize`'s first parameter) is concatenated verbatim into
  every platform's default base path (`GetDefaultBasePath`'s
  `Path.Combine(..., AppName)` on each branch); `Initialize` only rejects a
  null or all-whitespace value. The current production caller passes one
  fixed literal at process startup.

The contract is: **`category`, `extension`, and `appName` are caller-owned
literals, never derived from external content; `key` is the only parameter
this mechanism defends.** This must hold for every future caller, including
one that builds a category name from a feed, package, or platform identifier
— those must be routed through `key`, not `category`, `extension`, or
`appName`.

**Gap:** the contract above is enforced only by code review convention today.
`GetCategoryPath`, `GetFilePath`, `GetDefaultBasePath`, `TryGet`,
`TryGetBytes`, `Set`, and `SetBytes` do not reject a `category`, `extension`,
or `appName` containing a path separator or a `..` segment, so a future
caller that violates the contract (for example, building a category from a
package id) would silently gain a path-traversal write/read primitive outside
the cache root, undetected by any existing test. Closing this gap — for
example, asserting all three parameters contain no directory separator and no
`.` segment before every path computation — is recommended follow-up work;
this document records the invariant so that follow-up has a contract to
enforce against.

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

**Gap:** `CoreCache`'s own read/write entry points (`TryGet`, `TryGetBytes`,
`Set`, `SetBytes`, `GetFilePath`) never call the guard themselves — only
`Clear` and the external callers above do. Because `category` is
contract-restricted to literals (see above), a conforming caller never needs
the guard on the read/write path today — but the guard's absence there means
a `category`/`extension`/`appName` contract violation reaching `TryGet`/`Set`
directly (rather than through `Clear` or one of the callers that already
guards itself) is not caught by this mechanism at all. A future defensive
check on `category`/`extension`/`appName` (the trust-boundary gap above)
would close this without relying on every future write-path caller
remembering to call the guard itself.

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

**Gap:** `Initialize` is the only entry point that takes `s_maintenanceLock`
around a change to `_appName`/`_basePathOverride`; every other method
(`GetBasePath`, `TryGet`, `Set`, `GetFilePath`, ...) reads those two fields
without the lock and without `volatile`. The mechanism is therefore
single-writer: **at most one `Initialize` call may be outstanding, and no
other `CoreCache` method may run concurrently with it.** Today's callers
satisfy this by calling `Initialize` once at process startup before any
other cache use, but the contract is not stated anywhere and not enforced by
an assertion. A production build that calls `Initialize` a second time for
any reason (for example, a hosted/long-lived process switching app identity)
while a concurrent `TryGet`/`Set` is in flight has a data race on
`_appName`/`_basePathOverride`.

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

`CancelAndWaitForMaintenance`/`Clear` are the only ways a caller observes
completed maintenance. **Every** `Clear` call — not only `Clear(category:
null)` — unconditionally waits (`Timeout.InfiniteTimeSpan`) for the current
maintenance generation before deleting anything; only `Clear(null)` also
*consumes* the recorded byte/directory counters into its return value
(`Clear(category)` waits the same way but reports `0` maintenance bytes).
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
caller rather than being swallowed.

**Gap:** `Clear` does not coordinate with concurrent `Set`/`SetBytes` calls,
which do not take `s_maintenanceLock` at all. A `Set` that created its
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
writes elsewhere are best-effort, has no contract to rely on. Today no caller
clears a category while concurrently writing to it; a future one must either
serialize its own writes around a `Clear` or accept both the silent-loss and
the `Clear`-throws possibilities.

## Telemetry is fire-and-forget but not exception-isolated

`InfoTracker.RecordCacheHit`/`RecordCacheMiss` and `CacheTelemetry.Record` are
recorded on cache outcomes, but recording itself does nothing to protect a
cache result: `CacheTelemetry.Record` calls every subscribed
`IObserver<CacheObservation>.OnNext` synchronously, with no surrounding
`try`/`catch`. A throwing subscriber changes cache behavior differently
depending on which method and overload observed it:

- **`TryGet`/`TryGetBytes` without `maxAge`:** the hit-telemetry call is
  inside the method's own blanket `catch`, so a throwing subscriber silently
  turns a hit into a miss (`null`) with no miss recorded; a throwing
  subscriber on the (separate, unguarded) miss path propagates to the caller.
- **`TryGet`/`TryGetBytes` *with* `maxAge`:** the hit-telemetry call is inside
  a `catch` that swallows the exception and falls through to the *same*
  unguarded miss-telemetry call below it. A subscriber that throws on the
  hit observation therefore still reaches the miss observation; if that
  subscriber (or another) also throws on the miss observation, the exception
  propagates out of the method — unlike the non-`maxAge` overloads, a
  throwing hit subscriber does not reliably degrade to a silent `null` here.
- **`Set`/`SetBytes`:** the telemetry call is inside the method's own blanket
  `catch`, so a throwing subscriber is swallowed the same way any other write
  failure is, and the write telemetry is simply never recorded.

This mechanism does not isolate telemetry from the operation it is
recording; "fire-and-forget" describes the caller's intent, not an enforced
boundary, and it is not true that every outcome is guaranteed to be recorded.
Telemetry may also undercount for reasons unrelated to a subscriber (a `Set`
that never reaches the `try` body's `CacheTelemetry.Record` call because an
earlier line threw is silently uncounted) and must not be read as an audit
trail of cache correctness — only as an operational signal, and only when
every subscriber is known not to throw.

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
