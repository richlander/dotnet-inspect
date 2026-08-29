# CoreCache mechanism

This document owns the mechanism contract of `DotnetInspector.Core`'s
`CoreCache`/`AsyncCache`: the process-wide cache root, the filesystem key
scheme, path-context containment, initialization lifecycle, and versioned
category maintenance. It does not own cache semantics — the complete key,
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
- best-effort read/write/clear operations over that path;
- containment so cache deletion can never escape the cache root; and
- best-effort background retirement of superseded versioned category
  directories.

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

The contract is: **`category` and `extension` are caller-owned literals, never
derived from external content; `key` is the only parameter this mechanism
defends.** This must hold for every future caller, including one that builds a
category name from a feed, package, or platform identifier — those must be
routed through `key`, not `category` or `extension`.

**Gap:** the contract above is enforced only by code review convention today.
`GetCategoryPath`, `GetFilePath`, `TryGet`, `TryGetBytes`, `Set`, and
`SetBytes` do not reject a `category` or `extension` containing a path
separator or a `..` segment, so a future caller that violates the contract
(for example, building a category from a package id) would silently gain a
path-traversal write/read primitive outside the cache root, undetected by any
existing test. Closing this gap — for example, asserting both parameters
contain no directory separator and no `.` segment before every path
computation — is recommended follow-up work; this document records the
invariant so that follow-up has a contract to enforce against.

## Path-context containment

`EnsurePathInCacheContext`/`IsPathInCacheContext` are the only guards against
deleting outside the cache: a path is in context when it equals or is a
descendant of the active base path (`GetBasePath()`) or the legacy pre-XDG
path (`GetLegacyBasePath()`). `IsPathInCacheContext` fails closed — any
exception while resolving the full path (malformed path, denied access)
returns `false`, never `true`.

**Gap:** this guard runs only inside `Clear`, immediately before
`Directory.Delete`. It does not run on the write path (`Set`/`SetBytes`
create directories and move files into `GetFilePath`'s result without calling
it) or the read path. Because `category` is contract-restricted to literals
(see above), a conforming caller never needs the guard there today — but the
guard's absence means a `category`/`extension` contract violation on the
write path is not caught by this mechanism at all, only a violation reached
through `Clear`'s category argument is. A future defensive check on
`category`/`extension` (the gap above) would close both paths at once and
make this asymmetry moot.

## Initialization lifecycle

`Initialize(appName, basePath)` is not an idempotent no-op past the first
call: it cancels and best-effort drains any in-flight versioned-category
maintenance, replaces the maintenance generation, and re-schedules cleanup
for every category registered so far — against the *new* base path if one was
given. Reclaimed-byte/directory counters carry forward only when the new base
path resolves to the same location as the old one; otherwise they reset,
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

`CancelAndWaitForMaintenance`/an implicit wait inside `Clear(category: null)`
are the only ways a caller observes completed maintenance; both drain the
current generation (or cancel it after a timeout) and consume the recorded
byte/directory counters. Routine (non-`Clear`) maintenance is silent: a
caller that never calls `Clear` or `CancelAndWaitForMaintenance` never learns
whether background retirement ran, succeeded, or was skipped because a prior
run left the directory absent.

## `Clear` and concurrent writers

**Gap:** `Clear` takes `s_maintenanceLock`, drains maintenance, validates the
target path is in cache context, then deletes the directory tree. It does not
coordinate with concurrent `Set`/`SetBytes` calls, which do not take
`s_maintenanceLock` at all. A `Set` that created its category subdirectory
before `Clear`'s `Directory.Delete` observes either a successful write that
`Clear` then removes (consistent with "clear empties the cache") or a
`WriteAtomically` `File.Move` into a directory `Clear` just deleted — caught
by `Set`'s blanket `try`/`catch` and silently dropped. Neither outcome
corrupts the cache, but the mechanism gives no delivery guarantee for a write
that races a clear, and a caller relying on "the value I just set survives"
immediately after a concurrent `Clear` has no contract to rely on. Today no
caller clears a category while concurrently writing to it; a future one must
either serialize its own writes around a `Clear` or accept that the write may
be silently lost.

## Telemetry is not a correctness signal

`InfoTracker.RecordCacheHit`/`RecordCacheMiss` and `CacheTelemetry.Record`
are recorded on every `TryGet`/`TryGetBytes`/`Set`/`SetBytes` outcome, but
recording is fire-and-forget and never affects the return value or throws.
Telemetry may undercount (a `Set` that never reaches the `try` body's
`CacheTelemetry.Record` call because an earlier line threw is silently
uncounted) and must not be read as an audit trail of cache correctness — only
as an operational signal.

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
