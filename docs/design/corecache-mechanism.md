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

- a base directory scoped to the current application name and platform (or
  an override) — not a *stable* one: `GetBasePath()` re-derives it on every
  call rather than caching a value from `Initialize`, so on Linux, absent an
  explicit override, changing `XDG_CACHE_HOME` in the process environment
  changes every subsequent path, containment, and clear decision without any
  `Initialize` call at all (see the trust-boundary and initialization-lifecycle
  sections below for the concrete consequences);
- a collision-resistant, filesystem-safe path for a caller-chosen
  well-formed key within a category — `GetFilePath` hashes
  `Encoding.UTF8.GetBytes(key)` using the standard replacement-fallback
  `Encoding.UTF8` instance, so a *malformed* UTF-16 key (for example, one
  containing a lone surrogate) is not rejected but is silently
  replacement-normalized before hashing; two distinct malformed keys that
  normalize to the same UTF-8 bytes collide. Containment still holds (the
  guarantee stays inside the hash bucket), but collision resistance is a
  claim about well-formed Unicode input, not every .NET string;
- read, write, and background-maintenance operations whose *filesystem-access*
  failures are swallowed and observed only as a miss or as maintenance making
  no progress. This is not a blanket best-effort guarantee, and it is not
  symmetric between reads and writes: on the read path, path construction
  (which reads `AppName`, throwing before initialization) and miss-side
  telemetry both run outside the guarded region and propagate; on the write
  path, `Set`/`SetBytes` wrap path construction in their own blanket `catch`,
  so the identical pre-initialization failure is swallowed there instead —
  see [Telemetry](#telemetry-is-fire-and-forget-but-not-exception-isolated)
  and the pre-initialization note in
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
  never produce a path outside the two-level hash bucket. The filesystem path
  is not `key`'s only sink, though: every read/write operation also forwards
  the original, unhashed `key` to `CacheTelemetry.Record` (see
  [Telemetry](#telemetry-is-fire-and-forget-but-not-exception-isolated)
  below) — that is a second, independent path for the same untrusted input,
  contained by a different owner's mechanism (`CacheTelemetry`'s own
  redaction), not by the hashing described here.
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
  fixed literal at process startup. `appName` also, independently of any
  `basePath` override, controls a *second* root `IsPathInCacheContext` can
  anchor to: on non-Windows platforms `GetLegacyBasePath` builds
  `{LocalApplicationData}/{AppName}` and — when that path is non-null —
  `IsPathInCacheContext` accepts a descendant of it, even when an explicit
  `basePath` override is active. `GetLegacyBasePath` returns `null`, though,
  whenever the legacy path string equals the *current* `GetDefaultBasePath()`
  result — not whichever root `basePath` overrides to. On Linux, for
  example, if `XDG_CACHE_HOME` happens to equal `LocalApplicationData`, the
  legacy and default paths coincide and the legacy check is suppressed
  entirely, even while a separate explicit `basePath` override is active and
  makes that coincidence irrelevant to the active root. So `appName` is not
  merely a default-path input; it is a live containment anchor of its own,
  but only when `GetLegacyBasePath` returns non-null — a condition that
  depends on the current default path, not on whether an override is
  active.
- **`basePath`** (`Initialize`'s second, optional parameter) is returned
  verbatim by `GetBasePath()` with no validation at all — not even the
  null/whitespace check `appName` gets — and becomes the *active-root* anchor
  `IsPathInCacheContext` checks a candidate path against; it is not
  necessarily the sole anchor, though — the legacy root derived from
  `appName` above is also checked whenever `GetLegacyBasePath` returns
  non-null (see the caveat above). Unlike
  `category`/`extension`/`appName`, this is **not** a fixed-literal value in
  production today: `dotnet-inspect`'s CLI entry point
  (`src/dotnet-inspect/Program.cs`) resolves it with explicit precedence —
  the `DOTNET_INSPECT_CACHE_DIR` environment variable wins verbatim if set;
  otherwise, when `--isolated`/`DOTNET_INSPECT_ISOLATED` selects an
  isolation session name (from either the `--isolated <name>` command-line
  argument or the `DOTNET_INSPECT_ISOLATED` environment variable — not only
  the environment variable), it builds a temp-directory path from that
  session name; otherwise `basePath` is `null` and `CoreCache` falls back to
  its own platform default. Whichever value results is forwarded unchanged
  through `NuGetCache.Initialize` to `CoreCache.Initialize`. A caller (here,
  an end user or their shell environment, or a `--isolated` command-line
  argument) that controls `basePath` controls the
  cache root itself, which is a strictly larger trust concession than
  `category` or `extension` — and it is exercised in production, not merely
  hypothetical. `basePath` is not the only environment-controlled root
  input, either: when no override is supplied at all,
  `GetDefaultBasePath`'s Linux branch reads `XDG_CACHE_HOME` directly and,
  when it is non-empty (`!string.IsNullOrEmpty`, so a whitespace-only value
  still counts as set), uses it verbatim as the parent of `appName` with no
  further validation — this is not quite "the same absence of validation" as
  `basePath`, though: an explicit `basePath` override is accepted whenever it
  is non-null, including an empty string, while a `null` or empty
  `XDG_CACHE_HOME` is treated as unset and falls back to `~/.cache`; only a
  non-empty value receives XDG's verbatim, unvalidated treatment. It is a
  narrower concession than an explicit `basePath` override (since `appName`
  is still appended beneath it), but it is a second, always-live
  environment-controlled root selector on Linux, not a hypothetical one.
  Neither environment-controlled root is the sole anchor `IsPathInCacheContext`
  checks against, either — see the `appName` bullet above and
  [Path-context containment](#path-context-containment) for the
  legacy root and the caveat that it is checked only when the active-root
  check completes without throwing and simply doesn't match, not truly
  independently.

The contract is: **`category`, `extension`, and `appName` are caller-owned
values that every current producer restricts to a fixed literal or a
bounded, hardcoded selection, never external content; `key` is the only
parameter this mechanism defends by construction; `basePath` is the one
exception — it is intentionally operator/environment-controlled configuration,
accepted verbatim with no sanitization or containment constraint of its
own.** This must hold for every future caller, including one that builds a
category name from a feed, package, or platform identifier — that must be
routed through `key`, not `category`, `extension`, or `appName`. A future
`basePath` source should still avoid deriving it from untrusted *content*
(as opposed to trusted operator configuration), since nothing here
constrains it once accepted.

**Gap:** the contract above is enforced only by code review convention today.
`GetCategoryPath`, `GetFilePath`, `GetDefaultBasePath`, `GetCacheInfo`,
`TryGet`, `TryGetBytes`, `Set`, `SetBytes`, and `Clear` do not reject a
`category`, `extension`, or `appName` containing a path separator or a `..`
segment, so a future caller that violates the contract (for example,
building a category from a package id, or an `extension` from
configuration) would silently gain a path-traversal write/read/enumerate
primitive outside the intended cache root, undetected by any existing test.
`Clear` in particular does not merely gain a wider read/write primitive from
a non-conforming `category` — an empty or entirely-`..`-normalizing
`category` can *alias a different, unintended deletion target that is still
inside the root*, which the containment guard cannot catch because the
resulting path is genuinely contained: `Clear("")` computes
`Path.Combine(GetBasePath(), "")`, which `Path.Combine` returns as the base
path itself (an empty path segment is dropped, not appended), so `Clear("")`
deletes the *entire* cache root rather than "the empty category" — indistinguishable
from `Clear(null)`'s target path but without `Clear(null)`'s "clear
everything" semantics being the caller's evident intent; and a `category`
like `"a/../b"` combines and normalizes to sibling category `b`, so
`Clear("a/../b")` clears an entirely different, existing category `b`
instead of failing or clearing a nonexistent `"a/../b"` entry. Neither case
escapes the cache root — the existing traversal test only covers the
root-escaping case (`Clear("..")`, correctly rejected) — so this is a
distinct failure mode from the path-traversal gap above: a non-conforming
`category` can silently retarget a destructive operation to a different
in-root location, not just widen it outside the root. That "correctly
rejected" traversal result is also configuration-dependent, not universal: the
guard accepts a path under *either* the active root or the legacy root (see
[Path-context containment](#path-context-containment) below), and those two
roots are derived independently — nothing prevents an operator-supplied
`basePath` override from placing the active root as a subdirectory *of* the
legacy root. In that configuration, `Clear("..")` resolves to the legacy
root itself, which the guard accepts as a legitimate containment target, and
the deletion proceeds — the traversal succeeds by landing on the *other*
accepted anchor rather than by escaping both. This is the same
"root-escaping" case, but its rejection depends on the active and legacy
roots not being nested inside one another, which nothing here enforces.
`basePath` is exempt from the path-traversal framing of this gap since it is already the accepted
exception above, but `GetBasePath`/`Initialize` still place no containment
constraint on it at all — a `basePath` sourced from untrusted *content*
rather than trusted operator configuration would be unconstrained in a
different way, as noted above. Closing the `category`/`extension`/`appName`
gap — for example, asserting each contains no directory separator and no
`.` segment before every path computation — is recommended follow-up work;
this document records the invariant so that follow-up has a contract to
enforce against.

## Path-context containment

`EnsurePathInCacheContext`/`IsPathInCacheContext` are a public guard that
confines a path to the cache root: a path is in context when it equals or is
a descendant of the active base path (`GetBasePath()`) or the legacy
pre-XDG path (`GetLegacyBasePath()`). This is a purely lexical check —
`IsPathInCacheContext` calls `Path.GetFullPath` and does string comparisons
only; it never opens, enumerates, or otherwise accesses the candidate path
on disk, so a lexically-contained path beneath a directory the process
cannot actually read or write is still reported as in-context, and
accessibility failures surface later, from whatever filesystem operation
the caller performs next. `IsPathInCacheContext` fails closed for a
different reason — an exception while resolving the full path itself
(a malformed path, or one exceeding platform path-length limits) is caught
and returns `false`, never `true`; so is a null, empty, or all-whitespace
*candidate* path, rejected before any resolution is attempted. That last
check has a consequence worth naming explicitly: an accepted, blank
`basePath` override (see the trust-boundary section above) is a valid
*root*, but `GetBasePath()`'s blank result is also *itself* rejected the
moment it (or a path derived from it) is passed as the *candidate* to
`IsPathInCacheContext` — so `Clear()` with a blank `basePath` override does
not silently no-op or contain some unexpected path; it fails
`EnsurePathInCacheContext`'s guard and throws, because the guard cannot
distinguish "root not supplied" from "candidate not supplied" once both
happen to be the same blank string. The legacy root is not evaluated
independently of the active-root check succeeding cleanly, either:
`IsPathInCacheContext` checks the active root (`IsSameOrChildPath(fullPath,
GetBasePath())`) first, and only evaluates `GetLegacyBasePath()`/the legacy
comparison if that first check *returns* `false` — normally, not
exceptionally. Because one `catch` wraps both stages, an exception while
resolving the *active* root (`IsSameOrChildPath` calls
`Path.GetFullPath(root)` on `GetBasePath()`'s result, which throws for a
malformed override — see the trust-boundary section above on `basePath`
having no format validation) is caught and returns `false` immediately,
without ever reaching the legacy-root comparison. So the legacy root is not
truly an "independent" second anchor in the sense of being checked
regardless of what happens to the active-root check; it is checked only
when active-root resolution completes without throwing and simply doesn't
match. `Clear` calls it internally before deleting;
other owners call it directly, but not exclusively before deletion. The
following description of `NuGetCache`'s and `PlatformPackService`'s call
sites is cited only as evidence for a `CoreCache`-owned claim — that this
guard's coverage of a reused local variable is point-in-time, not
re-verified at each use — not as a specification of those callers' own
publish/cleanup sequencing, which they own:
`NuGetCache` and `PlatformPackService` each guard both their commit
`targetPath` and their staging `stagingPath` once, before a *publish/write*
operation begins (`Directory.CreateDirectory`, copying staged contents in);
that same single guard call also covers the staging path's later, separate
best-effort cleanup delete in a `finally` block once publication succeeds or
fails, since the guarded local variable is reused rather than re-derived —
this coverage is point-in-time, though, not a re-verified guarantee at
delete time: `IsPathInCacheContext` resolves the guarded string with
`Path.GetFullPath` internally but returns only a `bool`, not the resolved
absolute path, so the *original*, possibly-relative string is what the
later delete actually uses. `GetBasePath()`/`basePath` are not guaranteed
absolute — production accepts `DOTNET_INSPECT_CACHE_DIR` verbatim with no
rootedness check (see the trust-boundary section above) — so if a relative
root or staging path is in play and the process's current directory changes
between the guard call and the later delete, the same string can resolve to
a different location than the one actually checked. No current caller
changes the working directory during a commit/cleanup cycle, but the guard
does not itself close this window; it assumes a stable current directory
and an absolute (or effectively unchanging) resolved path across the reused
call. This particular exposure (a guard's coverage going stale before the
guarded string is used) is not the only consequence of an unresolved
relative root, either: because `CoreCache` stores and forwards `basePath`
verbatim rather than resolving it to an absolute path once at
`Initialize` time, *every* operation that constructs a path from
`GetBasePath()` and then performs a filesystem call against that same
unresolved string — not just the guard-reuse case above — depends on the
process's current directory being the one in effect when that later call
actually runs, not when the string was built. `Set`/`SetBytes`'s
`WriteAtomically` builds a temp-file path and later moves it to the
original path in a separate operation on the same relative string; `Clear`
validates, measures, and deletes the same unresolved `targetPath` across
three separate filesystem calls; and versioned-category retirement (see
below) captures `root = GetBasePath()` synchronously when scheduling, then
resolves and deletes against that captured string later, from a background
`Task.Run` that can execute an arbitrary amount of time afterward — the
asynchronous case with the widest window of any of these. A stable current
directory is therefore an unstated precondition for the mechanism as a
whole whenever `basePath` is relative, not only for the NuGetCache/
PlatformPackService guard-reuse pattern described above.
Only `PlatformPackService`'s separate `destDir` guard (inside its
content-copy helper, before overwriting an existing destination
subdirectory) and `PackageCacheService`'s legacy-cache guard are dedicated
guards whose sole purpose is preceding an actual `Directory.Delete`. It is
the mechanism's one exported containment primitive; any future caller that
writes, deletes, or otherwise mutates a path derived from
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

**Gap:** both checks also rely on `Path.TrimEndingDirectorySeparator`, which
does not strip the separator from a filesystem root — `TrimEndingDirectorySeparator("/")`
returns `"/"` unchanged, confirmed directly. If `basePath` (or the legacy
base path) is ever configured as a bare root such as `/`,
`IsSameOrChildPath` compares an ordinary descendant path like `/tmp/x`
against `"//"` (the root with its separator re-appended for the prefix
check) rather than `"/"`, so the prefix match fails and a path that is in
fact under the root is reported as **not** contained — but this only
affects a *descendant* path, not the root itself: `IsSameOrChildPath`
checks exact equality first (`fullPath.Equals(fullRoot, ...)`), which is
unaffected by the separator-trimming issue, so an operation that targets the
root path directly — including `Clear()` (no category), whose target path
*is* `GetBasePath()` — is correctly recognized as in-context and proceeds
as designed (deleting the entire root), not incorrectly refused. Only a
narrower, category-scoped operation such as `Clear("category")` or
`EnsurePathInCacheContext` on some other descendant path would be
incorrectly refused when `basePath` is a bare root. This does not create a
traversal exposure either way — the guard still fails closed for the
descendant case, rejecting a legitimate path rather than admitting an
illegitimate one. No known caller currently configures `basePath` as a
filesystem root, but nothing in `Initialize`/`GetBasePath` prevents it —
`basePath` is accepted verbatim from an environment variable in production
(see the trust-boundary section above) — so this is an unverified,
narrow-but-reachable edge case, not a provably unreachable one.

**Gap:** `CoreCache`'s own read/write/statistics entry points (`TryGet`,
`TryGetBytes`, `Set`, `SetBytes`, `GetFilePath`, `GetCacheInfo`) never call
the guard themselves — only `Clear` and the external callers above do.
`GetCacheInfo(category)` in particular recursively measures and enumerates
every file under `GetCategoryPath(category)` with no containment check, and
its own exception handling is mixed rather than absent: it returns an empty
`CacheInfo(targetPath, 0, 0)` when its own `Directory.Exists(targetPath)`
check reports `false` (ambiguous, as elsewhere, between "does not exist" and
"error determining existence"); the size measurement it delegates to
(`GetDirectorySize`) performs a *second*, independent `Directory.Exists`
check with the same ambiguity and returns `0` rather than propagating; but
the file-count enumeration, `Directory.GetFiles(targetPath, "*",
SearchOption.AllDirectories)`, has no guard at all and propagates any
enumeration failure that occurs after both existence checks have already
passed. So a category contract violation reaching
it is also an out-of-root enumeration read, not just a write/delete
primitive, and separately, a filesystem error partway through can surface as
a zero/empty result at either existence check or as a propagated exception
from the unguarded file count, depending on exactly where the failure
occurs. Because `category` is contract-restricted to literals (see
above), a conforming caller never needs the guard on the read/write/stats
path today — but the guard's absence there means a
`category`/`extension`/`appName` contract violation reaching `TryGet`/`Set`/
`GetCacheInfo` directly (rather than through `Clear` or one of the callers
that already guards itself) is not caught by this mechanism at all. A future
defensive check on `category`/`extension`/`appName` (the trust-boundary gap
above) would close this without relying on every future caller remembering
to call the guard itself.

`Set`/`SetBytes` publish file content through a private `WriteAtomically`
helper: it writes the new content to a sibling temp file named
`{path}.{Guid.NewGuid():N}.tmp`, then calls `File.Move(tempPath, path,
overwrite: true)` to publish it, then unconditionally attempts
`File.Delete(tempPath)` in a `finally` block (a no-op after a successful
move, since the temp path no longer exists). This is `CoreCache`'s own
atomic single-file publication mechanism — a reader never observes a
partially-written file — and is distinct from
[`cache-concurrency.md`](cache-concurrency.md)'s package-*directory* staging
protocol (stage into a temp directory, then `Directory.Move` the whole tree
into place), which that document owns.

Publication is atomic per file, but `CoreCache` provides no serialization
*between* operations on the same category/key: `TryGet`/`TryGetBytes`/`Set`/
`SetBytes` take no lock of any kind (`s_maintenanceLock` guards only the
maintenance-related methods listed above). Two concurrent `Set` calls for
the same path each write their own temp file and each call
`File.Move(..., overwrite: true)`; whichever move lands last wins, and it is
not necessarily the call that was invoked last — only the call whose move
completes last. A concurrent reader can therefore observe either publisher's
complete content, never a mix of the two (each move is a single atomic
rename), but a `maxAge` read is not itself one atomic observation: the
existence/freshness check (`FileInfo.Exists`/`LastWriteTimeUtc`) and the
subsequent content read are two separate filesystem operations, so a
concurrent `Set` between them can mean the freshness decision was made
against one generation of the file while the bytes actually returned came
from a different, newer one.

## Initialization lifecycle

`Initialize(appName, basePath)` is not an idempotent no-op past the first
call: it cancels any in-flight versioned-category maintenance and then
synchronously waits, *without a timeout*, for every task in the old
generation to complete, cancel, or fault (`WaitForMaintenanceTasksBestEffort`'s
unbounded `Task.WaitAll` — "best-effort" describes that a task failure is
swallowed once the wait itself returns, not that the wait is bounded; a
maintenance task that never observes its cancellation token and never
returns can block `Initialize` indefinitely). Only after that drain
completes does `Initialize` replace the maintenance generation, and
re-schedule cleanup
for every category registered so far — against the *new* base path if one was
given. Reclaimed-byte/directory counters carry forward only when the new base
path resolves to the same location as the old one (`IsSamePath` — subject to
the case-sensitivity gap in
[Path-context containment](#path-context-containment)); otherwise they reset,
because the counters describe one cache root's history and a root change
starts a new one. Both sides of that comparison are *recomputed live*, not
the location actually recorded against by the earlier maintenance — and
that "recomputed" property has a sharper edge when `basePath` is a
*relative* path (see the trust-boundary and containment-guard notes above
on `basePath` not being guaranteed absolute): `IsSamePath` resolves each raw
override string via `Path.GetFullPath` only at comparison time, against
whatever the process's current directory is *then* — not the directory that
was current while the earlier maintenance generation actually ran. If the
same relative `basePath` string (for example `"cache"`) is passed to two
`Initialize` calls, but the process's current directory differs between
when the first generation's maintenance ran and when the second
`Initialize` call runs `IsSamePath`, the two calls can resolve to two
genuinely different absolute directories on disk while still comparing
equal — carrying counters from one physical root's history into a
different physical root's tally, not merely "carrying forward across a
root change neither call intended" as in the ambient-environment scenario
below, but across two locations that were never the same directory at any
point in time. Separately, and regardless of whether `basePath` is relative
or absolute, the "old" and "new" reads are not simultaneous either: the
"old" root (`GetBasePath()` evaluated
with the previous `appName`/`basePath`) is read first, before the
cancellation and best-effort drain of in-flight maintenance tasks; the
"new" root (`GetBasePath()` evaluated with the new values) is read only
afterward, once `_appName`/`_basePathOverride` have already been updated.
The description below treats both reads as observing one shared snapshot of
"the current process environment" — that holds only when no ambient input
`GetDefaultBasePath` reads (for example `XDG_CACHE_HOME`) changes *during*
the drain window between the two reads; if one does, the "old" and "new"
sides can genuinely disagree about the environment even with `appName`
unchanged, independent of the environment-change scenario discussed next.
If `appName` is unchanged across
the two calls, and no explicit `basePath` override was ever given, and
`XDG_CACHE_HOME` (or another ambient input `GetDefaultBasePath` reads)
changes between the first and second `Initialize` call (and stays stable
across the drain window in between), both sides of the
comparison observe the *same* changed environment and so still compare equal
— counters can carry forward across an actual root change that neither
`Initialize` call's caller intended, and conversely cannot be relied on to
always reset just because the ambient environment changed since the first
call. (If `appName` *does* change between calls, the two sides diverge — one
ends in the old `appName`, the other in the new one — so this specific
carry-forward-despite-environment-change case requires an unchanged
`appName`, not merely an unchanged `basePath` override.)

**Gap:** re-initialization is not exception-safe against a malformed root
on *either side* of the comparison, and the failure is destructive to more
than scheduling. `Initialize`
mutates `_appName`, `_basePathOverride`, the maintenance cancellation
source, the progress object, and the task dictionary to their new values —
and, before that, takes a destructive snapshot of the *old* generation's
reclaimed-byte/directory counters via `TakeSnapshot()` (which zeros them) —
all *before* calling `IsSamePath` to decide whether to carry that snapshot
forward into the new progress object. `IsSamePath` calls `Path.GetFullPath`
on *both* the old root and the new root, and it throws for a sufficiently
malformed path on either one (invalid characters, exceeding platform
path-length limits) — the failure is not limited to a malformed *new*
`basePath`. The old root can be malformed too: a first `Initialize` call
skips the `IsSamePath` comparison entirely (`previousRoot` is `null` when
`_appName` was previously unset), so a `basePath` that would make
`Path.GetFullPath` throw can be accepted, uncomplained-about, by that first
call and persist as `_basePathOverride` until a later, otherwise-unrelated
`Initialize` call finally exercises `IsSamePath` against it. And neither
side is limited to an explicit `basePath` override: `appName` receives only
a null/whitespace check, and `GetDefaultBasePath` concatenates it verbatim
into every platform's default path with `Path.Combine`, which does not
reject invalid path characters — so a default root derived from a
sufficiently unusual `appName` has the identical exposure, on either the
old or the new side of the comparison. Because `basePath`
receives no validation (see the trust-boundary section above), a
caller-supplied `basePath` that `Path.GetFullPath` rejects makes `Initialize`
throw *after* the new fields are already committed, *after* the old
counters have already been zeroed and captured only in a local variable,
and *before* the `foreach` loop re-schedules cleanup for previously
registered categories. The result is partial, not all-or-nothing: the new
root and app name take effect, no versioned category's cleanup is
rescheduled against it until some other call re-triggers scheduling, and the
old generation's reclaimed-byte/directory counts are lost permanently — the
captured local snapshot is discarded along with the exception, and the new
`s_maintenanceProgress` object was already installed with zeros. The
identical destructive-then-possibly-thrown pattern applies to
`Clear(category: null)`, which passes `consumeProgress: true` to
`WaitForMaintenance` (destructively zeroing the counters via the same
`TakeSnapshot()`) before validating `EnsurePathInCacheContext` and
performing the measurement/deletion — if either of those later steps
throws, the already-consumed counters are lost with no return value to
carry them.

**Gap:** a maintenance generation does not necessarily describe one cache
root's history, even though the counter-carry-forward rule above is framed
that way. `ScheduleVersionedCategoryCleanup` — called both from
`Initialize`'s `foreach` loop and from `RegisterVersionedCategory`/
`RequestVersionedCategoryCleanupAsync` outside of any `Initialize` call —
independently reads `GetBasePath()` as `root` on every invocation and keys
its per-category task by `(root, prefix, currentVersion)`, but every task
in the current generation, regardless of which root it was scheduled
against, records into the *same* shared `s_maintenanceProgress` object. On
Linux, an `XDG_CACHE_HOME` change between two calls that trigger scheduling
— with no intervening `Initialize` call at all — can leave one generation
containing cleanup tasks scheduled against two different roots, both
contributing to one aggregate byte/directory count. `Clear` later derives
its own deletion target by independently re-calling `GetBasePath()`/
`GetCategoryPath()` at the time it runs, which may be neither of the roots
that contributed to the aggregate it consumes — so `Clear`'s reported
maintenance-byte count is not guaranteed to describe the root it actually
deletes.

`RegisterVersionedCategory` may be called before or after `Initialize`, and
repeated registration of the same prefix is idempotent when `current`
is unchanged, comparing both `prefix` (the registry's dictionary key) and
`current` case-insensitively (`StringComparer.OrdinalIgnoreCase` and
`string.Equals(..., StringComparison.OrdinalIgnoreCase)` respectively) — so
a repeat registration that differs from the first only in casing is treated
as the same registration, not a conflicting one; a second registration
whose `current` differs in any other way (a different suffix, or a
different leading-zero spelling that parses to the same integer but is not
an exact case-insensitive string match) for a known prefix throws. Registered
categories are never forgotten across a later
`Initialize` call — re-initializing to a different root replays cleanup for
every previously registered category under that new root.

**Gap:** `_appName`/`_basePathOverride` are written only inside `Initialize`'s
`lock (s_maintenanceLock)`, and every other method that also takes that lock
— including concurrent `Initialize` calls themselves, `RegisterVersionedCategory`,
`Clear`, `WaitForMaintenance` (and therefore `CancelAndWaitForMaintenance`),
and `RequestVersionedCategoryCleanupAsync` — is safely serialized against
each other; none of these can observe a partial field write, and multiple
concurrent `Initialize` calls are not by themselves a data race (whichever
one takes the lock last determines the resulting root). The unsynchronized
race is narrower but wider than only the read/write path: the lock-free
surface — `GetBasePath`, `GetDefaultBasePath`, `GetLegacyBasePath`,
`GetCategoryPath`, `GetFilePath`, `TryGet`/`TryGetBytes`, `Set`/`SetBytes`,
`GetCacheInfo`, `IsPathInCacheContext`, and (transitively, since it calls
`IsPathInCacheContext`) `EnsurePathInCacheContext` — reads
`_appName`/`_basePathOverride` without the lock and without `volatile`. **No
lock-free method may run concurrently with any `Initialize` call** — this is
the actual race boundary, not a restriction on how many `Initialize` calls
may be outstanding. Today's callers satisfy this by calling `Initialize`
once at process startup before any other cache use, but the contract is not
stated anywhere and not enforced by an assertion. A production build that
calls `Initialize` a second time for any reason (for example, a
hosted/long-lived process switching app identity) while a
concurrent `TryGet`/`Set`/`IsPathInCacheContext` is in flight has a data race
on `_appName`/`_basePathOverride` — including the possibility that
`IsPathInCacheContext` combines an active-root check against one
initialization generation with a legacy-root check against another.

Before the first `Initialize` call, the lock-free surface does not behave
uniformly, because each method's own exception handling (or lack of it)
around the `AppName` property getter's `InvalidOperationException` differs:
`TryGet`/`TryGetBytes` construct the path (and so throw) before entering any
guarded region at all; `Set`/`SetBytes` only *look* silent because that same
exception is caught by their own blanket `try`/`catch` (see
[Telemetry](#telemetry-is-fire-and-forget-but-not-exception-isolated));
`IsPathInCacheContext` catches it too and returns `false` rather than
propagating, so `EnsurePathInCacheContext` throws its own refusal exception
instead of the pre-initialization one; and `GetLegacyBasePath` reads `AppName`
only on non-Windows platforms, so it throws there but returns `null` on
Windows without touching `AppName` at all. The methods above are not the
complete lock-free surface, though: `GetCategoryPath`, `GetFilePath`, and
`GetCacheInfo` all reach `AppName` transitively through `GetBasePath` →
`GetDefaultBasePath` and so throw the same pre-initialization exception —
except that `GetBasePath` itself has a bypass the others do not: when
`_basePathOverride` is non-null, `GetBasePath` returns it directly without
ever calling `GetDefaultBasePath` or touching `AppName`, so a process that
called `Initialize` with an explicit `basePath` override and then somehow
reached these methods before `_appName` was set (not reachable through
`Initialize` itself, which sets both fields together, but relevant to any
future caller that manipulates the fields independently) would not throw
from `GetBasePath` at all, only from whichever of `GetCategoryPath`/
`GetFilePath`/`GetCacheInfo` still needs `category`/`key` hashing or
enumeration — none of which touch `AppName` a second time once `GetBasePath`
has already returned. `Clear` reaches the same exception while deriving its
`targetPath` (after its own maintenance wait, which does not depend on
`AppName` — see below). `CancelAndWaitForMaintenance` does not propagate
this exception pre-initialization either, and for a distinct reason from
`Set`/`SetBytes`'s swallowing `catch` or `IsPathInCacheContext`'s
`false`-returning `catch` above: it calls
`WaitForMaintenance`, which calls `RequestVersionedCategoryCleanupAsync`,
which checks `_appName is null` explicitly and returns a completed
`default(CacheMaintenanceResult)` task rather than touching the throwing
`AppName` property — so `CancelAndWaitForMaintenance(timeout)` called before
`Initialize` returns a zeroed result instead of throwing, without ever
depending on a `catch` to do so. `RegisterVersionedCategory` similarly does
not require prior `Initialize`: it validates and stores its `prefix`/
`current` pair unconditionally, then calls
`ScheduleVersionedCategoryCleanup`, which itself checks `_appName is null`
and returns immediately without scheduling anything — so registering a
versioned category before `Initialize` succeeds and takes effect
retroactively once `Initialize` eventually runs (the category is already in
`s_versionedCategories` for `Initialize`'s own generation-scheduling pass to
pick up). There is no single
"every lock-free method does X" pre-initialization rule; each method's
documented behavior above already states its own case.

## Versioned category retirement

A versioned category family is identified by a `prefix` plus the current
member's own integer suffix (for example prefix `pkg-index-v`, current
`pkg-index-v8`). `RegisterVersionedCategory(prefix, current)` validates both
arguments before scheduling any retirement: `prefix` and `current` must each
be non-null and non-whitespace, and `current` must start with `prefix`
case-insensitively and have the remainder parse as a non-negative integer
via `int.TryParse(..., NumberStyles.None, ...)` (so no leading `+`/`-` sign,
no thousands separator, and no leading/trailing whitespace in the suffix) —
any violation throws synchronously from the call itself, before any
background work is scheduled, but not uniformly as `ArgumentException`: a
`null` `prefix` or `current` throws `ArgumentNullException` specifically
(`ArgumentException.ThrowIfNullOrWhiteSpace`'s own behavior for a `null`
argument), a whitespace-only value throws plain `ArgumentException`, and a
`current` that does not start with `prefix` or whose suffix does not parse
throws `ArgumentException` naming parameter `current`. A *second*
registration of an already-registered `prefix` is a separate case with its
own exception type: if the new call's `current`/parsed version disagrees
with what was already registered for that `prefix`, `RegisterVersionedCategory`
throws `InvalidOperationException` (not `ArgumentException`) — a
conflicting-registration error, distinct from an invalid-argument one — while
an identical repeat registration (same `prefix`, same `current`) succeeds
silently and reschedules cleanup for that generation instead of throwing.
Retirement:

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

**Gap:** the retired-directory byte count is itself only a best-effort
measurement, not a confirmed pre-deletion size — `CleanupVersionedCategory`
measures each obsolete directory via `GetDirectorySizeBestEffort`, which
catches any enumeration failure and returns `0` rather than propagating it.
A failed measurement does not, by itself, guarantee the directory is still
deleted and counted, though: after measuring, `CleanupVersionedCategory`
checks its `CancellationToken` again and returns without deleting or
recording anything at all if cancellation was requested in the interim
(the same token checked once already, before measurement began) — so a
directory that failed to measure *and* whose generation was canceled in
that same window is neither deleted nor counted, not "still deleted and
still counted with a zero size." Only when cancellation is not requested at
that second check does `CleanupVersionedCategory` proceed to
`Directory.Delete` the directory and record
a deletion with that (possibly `0`) size. A directory that fails to measure
(permissions, a concurrent modification during enumeration) but is not
racing a cancellation is still
deleted and still counted as one retired directory, but contributes `0` to
the accumulated byte total — so the maintenance byte counter that `Clear(null)`
later consumes into its return value (see
[Clear and concurrent writers](#clear-and-concurrent-writers)) can
undercount real reclaimed space for a reason distinct from the concurrent-write
undercounting already described there.

**Gap:** `CleanupVersionedCategory`'s own deletion step is not transactional
either — `Directory.Delete(directory, recursive: true)` and the subsequent
`progress.RecordDeletion(size)` are both wrapped in one blanket `catch` that
swallows any exception silently ("Cache cleanup is best-effort and retried
on the next initialization"). If the recursive delete removes some files
before encountering one it cannot remove (a concurrent writer holding the
file, a permissions error partway through), the directory is left
*partially* deleted and `RecordDeletion` never runs — no bytes, no
directory count, and no indication that the directory was touched at all;
the next `Initialize`/registration cycle will reattempt it (the surviving
directory still matches the retirement rule), but until then the on-disk
state is neither the pre-cleanup directory nor a cleanly retired one. That
retry claim needs a precise qualifier, though: a repeated
`RegisterVersionedCategory` call for the *same* prefix within the *same*
maintenance generation does not itself trigger a retry —
`ScheduleVersionedCategoryCleanup` keys each scheduled task by
`(root, prefix, currentVersion)` and returns immediately, without scheduling
anything, when that key is already present in `s_maintenanceTasks`
(regardless of whether the previously-scheduled task's *own* cleanup
attempt succeeded, partially failed, or fully failed — the task itself
still completed, since `CleanupVersionedCategory`'s blanket `catch` prevents
it from ever faulting). The key is only removed — and a fresh attempt
scheduled — by a subsequent `Initialize` call (which replaces
`s_maintenanceTasks` with a new, empty dictionary) or by
`StartNewMaintenanceGenerationIfCanceled` replacing a canceled generation; a
changed root (via a different `basePath`/`appName`, or the ambient-input
change discussed above) also produces a different key and so schedules a
fresh attempt against that new root, but this is really "different key
scheduled," not "same failed attempt retried."
`Clear`'s own `Directory.Delete(targetPath, recursive: true)` (see
[Clear and concurrent writers](#clear-and-concurrent-writers)) has the
equivalent exposure in the opposite direction: it catches only
`DirectoryNotFoundException` (and only when the directory no longer
exists), so any other exception from a partially-completed recursive delete
propagates out of `Clear` to the caller — a caller that catches and ignores
that exception, or does not catch it at all, cannot tell from the exception
alone whether the target survived untouched or was partially removed.

**Gap:** the two counters `CacheMaintenanceProgress` tracks —
`_bytesFreed` and `_directoriesDeleted` — are each individually
thread-safe, but not via the same primitive throughout, and the pair
is not updated or read atomically together. `RecordDeletion` performs two
separate `Interlocked` operations (`Interlocked.Add` for bytes, then
`Interlocked.Increment` for count); `TakeSnapshot()` similarly performs two
separate `Interlocked.Exchange` calls; but `Snapshot()` reads bytes via
`Interlocked.Read` and reads the directory count via a plain `Volatile.Read`
— still a thread-safe read of that one field, just not the same
`Interlocked` operation family the other methods use. Background cleanup (`CleanupVersionedCategory`)
calls `RecordDeletion` without holding `s_maintenanceLock`, while
`WaitForMaintenance` reads the pair via `Snapshot()`/`TakeSnapshot()` while
holding that lock — the lock does not prevent a concurrent, lock-free
`RecordDeletion` call from executing between the two separate operations
inside `Snapshot()`/`TakeSnapshot()`, regardless of which specific atomic
primitive each one uses. A caller can therefore
observe a `CacheMaintenanceResult` where one field reflects a deletion that
just completed and the other does not yet (or, symmetrically, where
`TakeSnapshot()`'s reset of one field races a deletion recorded between the
two exchanges) — the returned `(BytesFreed, DirectoriesDeleted)` pair is not
guaranteed to describe one consistent point in time.

**Gap:** the retirement rule above is scoped to one registered family (one
`prefix`) at a time; nothing prevents two *different* registered prefixes
from overlapping, and a directory that is the current member of one family
can parse as an obsolete member of another. For example, registering
`("foo-v", "foo-v20")` and `("foo-v2", "foo-v21")` both succeed —
`s_versionedCategories` keys the registry case-insensitively
(`StringComparer.OrdinalIgnoreCase`), but `"foo-v"` and `"foo-v2"` still
compare as distinct entries — but the second family's cleanup sees
directory `foo-v20`, matches its `"foo-v2"` prefix, parses the remainder
after the 6-character prefix (`"0"`) as suffix `0`, and deletes it as
obsolete (`0 < 1`, since `"foo-v21"`'s own suffix after that same
6-character prefix is `"1"`, making the second family's current version 1,
not 21) — even though `foo-v20` is the *current*,
still-referenced directory for the first family. `prefix`/`current` are
therefore not just validated strings but caller-controlled deletion
selectors whose safety depends on every registered family's prefix language
being disjoint from every other — an invariant this mechanism does not
enforce and today's callers satisfy only by using prefixes that do not
overlap in practice.

`CancelAndWaitForMaintenance`/`Clear` are the only **public** ways a caller
observes completed maintenance *and its accounting*. `Initialize` is also a
completion barrier of a kind for the *previous* generation — a subsequent
`Initialize` call cancels the outgoing generation's tasks and waits for them
(`WaitForMaintenanceTasksBestEffort`'s `Task.WaitAll`) before installing the
new one — but it does not return the drained `CacheMaintenanceResult` to its
caller at all (only carrying the byte/directory counters forward internally,
per [Initialization lifecycle](#initialization-lifecycle) above), so it is
not a way to *observe* completed maintenance in the sense this section
means. The internal `RequestVersionedCategoryCleanupAsync`
is a third, assembly-internal path that test code uses directly to await
maintenance — but calling what it returns "the real aggregate task"
overstates what it actually returns: the
task it returns is only an aggregate of whatever was in `s_maintenanceTasks`
*at the moment that call constructed it* (`AwaitMaintenanceAsync([..
s_maintenanceTasks.Values], ...)` is a one-time array snapshot, not a live
view), memoized in `s_maintenanceTask` so repeated calls return the same
task instance. A later `RegisterVersionedCategory` call, after this method
has released `s_maintenanceLock` and returned, can schedule a new cleanup
task and reset `s_maintenanceTask` to `null` — but that clears the memoized
field for the *next* caller of `RequestVersionedCategoryCleanupAsync`; it
does not, and cannot, add the new task into the array a previously returned
task already captured. That does not mean the earlier task's *result* is
unaffected by the later registration, though: `AwaitMaintenanceAsync`
captures the task array by reference but reads `progress.Snapshot()` only
after that array completes — and `progress` is the same
`CacheMaintenanceProgress` instance shared by every task in the generation,
including ones registered (against the same root, via
`ScheduleVersionedCategoryCleanup`) after the earlier call already captured
its array. So the earlier caller's `CacheMaintenanceResult` can include
byte/directory counts recorded by a task it never waited for, if that later
task happens to record its result before the earlier array finishes and
`Snapshot()` runs — the *waiting* is scoped to the captured array, but the
*reported counters* are scoped to the whole generation's shared progress
object, and those two scopes can diverge. Whoever is still awaiting that earlier task
therefore never learns about maintenance scheduled after they obtained it
*by waiting for its completion* — but may still observe its accounting
folded into the numbers the earlier task reports.
The public `WaitForMaintenance` path does not have this exposure: it holds
`s_maintenanceLock` for its entire wait, so no `RegisterVersionedCategory`
call can interleave and schedule additional work while it is in progress.
`CancelAndWaitForMaintenance(timeout)` can return
*partial* progress rather than confirmation that maintenance fully drained:
`WaitForMaintenance` waits for the timeout given, then — if the task has not
completed — cancels it and waits only another 25ms before returning whatever
progress has been recorded so far, regardless of whether the canceled task
has actually finished exiting. That timeout bound applies only to a call
that finds the current maintenance generation *not already canceled*: if a
prior timed-out call already canceled the generation and its tasks are still
exiting cooperatively, the next call that touches maintenance —
`WaitForMaintenance` (and so `CancelAndWaitForMaintenance` or `Clear`),
`RegisterVersionedCategory`, or internal cleanup scheduling — restarts the
generation via `StartNewMaintenanceGenerationIfCanceled`, which performs an
*unbounded* `Task.WaitAll` on the previous generation's tasks before doing
anything else. A finite-timeout `CancelAndWaitForMaintenance` call made
after such a restart is pending can therefore block for as long as the
still-exiting canceled tasks take, regardless of the timeout it was given.
`Clear` always passes `Timeout.InfiniteTimeSpan` to `WaitForMaintenance`
(see below), so `Clear` cannot observe the 25ms partial-progress path at
all — `task.Wait(Timeout.InfiniteTimeSpan)` cannot return `false`, so `Clear`
always waits for the full maintenance generation (including any pending
generation-restart drain) to either complete or fault before proceeding; it
does not return early with partial progress the way a finite-timeout
`CancelAndWaitForMaintenance` call can.

**Gap:** `CancelAndWaitForMaintenance`'s `timeout` parameter is not
validated, and a value `Task.Wait(TimeSpan)` rejects silently skips both the
cancellation
and the 25ms secondary wait rather than producing a documented error or the
documented cancel-and-wait behavior. The rejected range is narrower than "any
negative `TimeSpan` other than exactly `-1`ms": `Task.Wait(TimeSpan)`
truncates the `TimeSpan` to whole milliseconds via `(long)timeout.TotalMilliseconds`
*before* range-checking, and only throws `ArgumentOutOfRangeException` when
that truncated value is `< -1` or `> int.MaxValue`. Truncation is toward
zero, not toward negative infinity, so a `TimeSpan` between `-1ms` and `0ms`
exclusive (for example `-0.5ms`) truncates to `0` and is accepted as a
zero-length wait, while one between `-2ms` and `-1ms` exclusive (for
example `-1.5ms`) truncates to `-1` — the sentinel for
`Timeout.InfiniteTimeSpan` — and is accepted as an *infinite* wait, not
rejected. Only a truncated value at or below `-2`, or one whose
`TotalMilliseconds` exceeds `int.MaxValue` (about 24.8 days, and note a
fractionally-over value can itself truncate down to exactly `int.MaxValue`
and be accepted), actually throws. `WaitForMaintenance` wraps `task.Wait(timeout)` and the
subsequent cancel/25ms-wait in one blanket `try`/`catch`, so that exception
is caught by the *same* handler that also swallows a legitimate
cancellation-related exception; the method falls straight through to
returning whatever progress is currently recorded, having neither canceled
the task nor waited the documented 25ms — but only after
`RequestVersionedCategoryCleanupAsync` (called before the `try`, to obtain
the task being waited on) has already returned, which can itself block
indefinitely on the unbounded generation-restart drain described above; an
invalid timeout does not bound or bypass that pending drain, only the
subsequent cancel/wait step. A caller who passes a rejected timeout observes
a non-canceling progress read — once any pending generation-restart drain
has finished — that looks identical to any other best-effort outcome, not a
timeout error.

**Every** `Clear` call — not only `Clear(category:
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
already completed the same deletion. Any other failure thrown by the
directory-enumeration itself, or by `Directory.Delete`, propagates to
`Clear`'s caller rather than being swallowed — but the measurement step's
own existence check is a second, earlier place a filesystem error can be
absorbed as a false negative, not just the initial probe below: `GetDirectorySize`
(the measurement helper) calls its own `Directory.Exists(path)` and returns
`0` when that reports `false`, and `Directory.Exists` is documented to
return `false` both when the directory genuinely does not exist and when an
error occurs determining whether it exists. So an authorization or other
filesystem error surfacing between `Clear`'s initial existence check and the
measurement step can still be silently absorbed as "0 bytes" rather than
propagating, even though `Clear` had already begun measuring. Before that point, `Clear` calls `Directory.Exists(targetPath)`
and returns early (reporting only the maintenance byte counter, no tree
size) when it reports `false` — the same ambiguous-`false` caveat applies
here too, and also to the `DirectoryNotFoundException when
!Directory.Exists(targetPath)` filter around the delete call: that filter's
own `Directory.Exists` re-check cannot definitively prove another process
completed the deletion, only that the directory is not currently observable,
for whatever reason. An authorization or other
filesystem error at any of these existence checks is therefore indistinguishable from
"nothing to clear" (or "already deleted") and returns successfully with an undercounted result,
rather than propagating like an enumeration or delete failure does. `Clear`'s `long` return
value is not a confirmed bytes-freed count: it is the tree size *measured
before deletion*, plus (for `Clear(null)` only) the consumed maintenance byte
counter. That
measurement can diverge from what this `Clear` call actually removed in
either direction, for different reasons: a concurrent deletion of some or
all of the tree between the measurement and `Directory.Delete` (including
the `DirectoryNotFoundException` case) can only make the returned value an
*overcount*, since it reports bytes that this `Clear` no longer needed to
remove; a concurrent `Set`/`SetBytes` that adds or grows a file in the same
tree after the measurement but before `Directory.Delete` can make it an
*undercount*, since those bytes are swept into the same recursive delete
without ever being measured. It also omits the
directory-deleted count even when `Clear(null)` consumes it from maintenance
(see [Versioned category retirement](#versioned-category-retirement)) — a
caller that needs the directory count must use `CancelAndWaitForMaintenance`
instead.

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
writes elsewhere are best-effort, has no contract to rely on. There is a
fourth outcome the three above do not cover, and it is the one that leaves
`Clear` looking most misleadingly successful: a `Set`/`SetBytes` call whose
`Directory.CreateDirectory` and `WriteAtomically`/file-move both complete
*after* `Clear`'s `Directory.Delete` call has already finished (still inside
`Clear`'s `lock (s_maintenanceLock)`, since that lock never excludes
`Set`/`SetBytes`, which take no lock at all) recreates part of the tree
`Clear` just removed, and does so without raising any exception on either
side. `Clear` still returns its pre-deletion measurement successfully in
this case; nothing about that successful, non-throwing return means the
cache root was empty at the moment `Clear` returned, or even that it is
still empty by the time the caller next inspects it — `Clear`'s success is
not a quiescence barrier and does not guarantee an empty cache at return,
only that the deletion `Clear` itself performed did not fail. No caller
within a single process clears a category while concurrently writing to it
today, but two separate `dotnet-inspect` invocations racing this way is
already possible; closing this gap (a future caller, in-process or
cross-process) must either serialize its own writes around a `Clear` or
accept both the silent-loss and the `Clear`-throws possibilities.

## Telemetry is fire-and-forget but not exception-isolated

This section describes `CacheTelemetry`'s and `InfoTracker`'s *current*
implementation only as evidence for a claim this document does own — what
happens to a `CoreCache` read/write operation when telemetry recording
fails. `CacheTelemetry`'s subscriber-dispatch mechanics (ordering,
delivery guarantees, `ActivityEvent` behavior) and `InfoTracker`'s counter
implementation are those types' own contracts, specified by their owning
code, not normatively defined here; if either changes internally without
changing where its call sites sit relative to `CoreCache`'s own
`try`/`catch` blocks, this document's claims about `CoreCache`'s resulting
return/throw/counter behavior are unaffected.

`InfoTracker.RecordCacheHit`/`RecordCacheMiss` and `CacheTelemetry.Record` are
recorded on cache outcomes, but recording itself does nothing to protect a
cache result. `CoreCache` forwards the original, unhashed `key` (see the
trust-boundary section above) to `CacheTelemetry.Record` as plain caller
evidence — what happens to that call is the adjacent `CacheTelemetry`
owner's own contract, cited here only because it is the first place the
untrusted `key` is contained rather than hashed: `CacheObservation.Create`
constructs the observation *before* either `Activity.Current?.AddEvent` or
subscriber dispatch runs, and that construction routes `key` through
`RedactCacheKey`, which returns a redacted `UrlRedaction` result for a
URL-shaped key or wraps any other key in an `InertString(TextPolicy.Field,
key)` — so by the time `Record` first adds an `ActivityEvent` to
`Activity.Current` when one exists (`Activity.Current?.AddEvent(...)` — no
event is added when there is no current activity, which is the ordinary
state unless one has been established), then calls every subscribed
`IObserver<CacheObservation>.OnNext` synchronously and in registration order,
with no surrounding `try`/`catch`. When a current activity does exist, its
event and every subscriber before a throwing one have already observed the
outcome by the time an exception surfaces; only subscribers *after* the
throwing one are not invoked at all, and `Record` itself does not complete
normally. (The throwing subscriber's own `OnNext` *was* called — it received
the observation before throwing; "not invoked" describes only the
subscribers after it, not the throwing one itself.) A throwing subscriber
then changes cache behavior differently depending on which method and
overload observed it:

- **`TryGet`/`TryGetBytes` without `maxAge`:** the hit-telemetry call is
  inside the method's own blanket `catch`, so a throwing subscriber silently
  turns a hit into a `null` return — this `catch` returns directly, so the
  separate miss path (including its own telemetry call and
  `InfoTracker.RecordCacheMiss()`) never runs; a hit that fails this way is
  not counted as a miss either, but it is not left uncounted altogether:
  `InfoTracker.RecordCacheHit()` runs, unconditionally, *before*
  `CacheTelemetry.Record(..., Hit)` inside the same `try`, so the hit counter
  is already incremented by the time a throwing hit subscriber turns the
  return value into `null` — the caller observes a miss-shaped result
  (`null`) while `InfoTracker` still recorded a hit. The miss path has no
  equivalent guard at all: `CacheTelemetry.Record(..., Miss)` runs
  unconditionally before `RecordCacheMiss()`, and neither call is wrapped in
  any `try`/`catch` on this path — so a throwing miss subscriber propagates
  out of the method entirely (the caller gets an exception, not `null`), and
  `RecordCacheMiss()` never runs, leaving that access entirely uncounted
  rather than double-counted or undercounted in a bounded way.
- **`TryGet`/`TryGetBytes` *with* `maxAge`:** the hit-telemetry call is inside
  a `catch` that swallows the exception and falls through to the *same*
  unguarded miss-telemetry call below it. A subscriber that throws on the
  hit observation therefore still reaches the miss observation; if that
  subscriber (or another) also throws on the miss observation, the exception
  propagates out of the method — unlike the non-`maxAge` overloads, a
  throwing hit subscriber does not reliably degrade to a silent `null` here.
  This path also double-counts the `InfoTracker` counters, independent of
  whether the *hit* telemetry subscriber itself is what throws:
  `InfoTracker.RecordCacheHit()` runs, unconditionally, *before*
  `CacheTelemetry.Record(..., Hit)` inside the same `try`; if that
  `CacheTelemetry.Record` call throws for any reason (a hit subscriber, or
  the `Activity.Current?.AddEvent` call itself), the already-incremented hit
  counter is not rolled back, and control falls through to the shared
  miss-path code below, which unconditionally calls
  `InfoTracker.RecordCacheMiss()` too — so one logical read increments both
  the hit and the miss counter. (If the miss-telemetry call *also* throws,
  `RecordCacheMiss()` — which runs immediately after it, unguarded — never
  executes, and the exception propagates as described above; in that case
  only the hit counter is incremented, not both.)
- **`Set`/`SetBytes`:** the telemetry call is inside the method's own blanket
  `catch`, so a throwing subscriber is swallowed the same way any other write
  failure is — the activity event (if any) and any subscribers up to and
  including the throwing one were still invoked with the observation; only
  subscribers *after* the throwing one are not.

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
telemetry is recorded: `GetTelemetryCategory` *detects* a `category` that
case-insensitively equals `"symbol-misses"`, but *rewrites* it as
`$"{category}/{extension}"` — preserving the caller's original spelling and
casing rather than canonicalizing it. `TryGet("symbol-misses", key,
extension: "forbidden")` reports `symbol-misses/forbidden`, distinct from
`symbol-misses/miss` —
`HttpClientFactoryTests.CacheTelemetry_SymbolMissesIncludeExtensionInCategory`
exercises exactly this call sequence (`Set`, then `TryGet` for the
`"forbidden"` extension, then `TryGet` for the `"miss"` extension) and
asserts on the resulting `Set`-store and `TryGet`-miss telemetry categories,
but it does not assert a `TryGet`-hit observation at all — the test proves
the store and miss categories are remapped as described, not that a
*successful hit* under `"forbidden"` is reported as `symbol-misses/forbidden`
rather than plain `symbol-misses`. A caller passing `"SYMBOL-MISSES"` would
observe `SYMBOL-MISSES/forbidden`, not the lowercase form, per the
`$"{category}/{extension}"` interpolation above — this casing-preservation
consequence, and the hit-path remapping itself, follow from reading the
source and are not exercised by any existing test. Every other `category`
passes through unchanged. This is an intentional part of the observable
telemetry contract, not an undocumented side effect.

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
restored backup, a manipulated file) makes an entry read as fresh for any
non-negative `maxAge` regardless of how far in the future it is. For a
negative `maxAge` the same future-dated entry is fresh only conditionally:
with `LastWriteTimeUtc` `δ` ahead of `UtcNow`, the comparison becomes
`-δ < maxAge`, so a negative `maxAge` of magnitude `M` still reports the
entry fresh whenever the future skew `δ` exceeds `M` — a small future skew
can still be stale against a large-magnitude negative `maxAge`, but any
future skew is read as fresh once `maxAge` is zero or positive. Any failure while resolving
`FileInfo` or reading the file is caught by the same guarded region and
falls through toward a miss, exactly like the non-`maxAge` overloads' hit
path — but the miss is not otherwise *recorded* the same way, and the
*return value* is not unconditionally `null` either, unlike that comparison
might suggest: the
non-`maxAge` overloads' hit-path `catch` returns directly, so a read failure
there is invisible to telemetry (no hit or miss observation, no
`InfoTracker.RecordCacheMiss()`); the `maxAge` overloads' `catch` instead
falls through to the same unguarded miss-telemetry call the missing-entry
case uses, so a read failure there *is* recorded as a miss — normally.
"Normally" matters here: that miss-telemetry call is unguarded on this path
just as it is on the missing-entry path (see
[Telemetry](#telemetry-is-fire-and-forget-but-not-exception-isolated)
above), so a throwing miss-telemetry subscriber propagates out of the
`maxAge` overload the same way it would out of the plain overload's miss
path — the caller gets an exception, not `null`, in that case. So a read
failure under `maxAge` is observable in telemetry in a way the equivalent
failure under the plain overloads is not, and *usually* still returns `null`
to the caller identically to the plain overloads — but not in every case,
since reaching that return still passes through the same unguarded
miss-telemetry call the plain overloads' *miss* path (not hit-failure path)
shares.

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
