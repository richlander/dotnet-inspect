# Cache concurrency and publication

This document owns the coordination model for immutable package contents and
derived platform packs. [Version resolution](version-resolution.md) owns which
package coordinate is selected and when network lookup occurs; this document
owns how content for an exact coordinate becomes visible safely.

## Source conformance

Content that dotnet-inspect downloaded is scoped to the source that supplied it.
A request is served only the exact bytes downloaded from a source the current
configuration lists. Content obtained from any other source is invisible to it,
even for an identical package id and version.

This is a correctness boundary, not an optimization. Two feeds may publish the
same coordinate with different content, so serving one feed's bytes for another
feed's request is package source confusion: the caller receives, and reports on,
a package it never asked for and may not be entitled to read. Avoiding that
outcome takes precedence over cache hit rate.

The consequences run through the whole model, and the rest of this document
should be read with them in mind:

- the source is part of the cache path, so one coordinate held by two feeds
  occupies two entries and the same bytes may be stored twice;
- a lookup consults only the entries its configuration entitles it to, and
  treats every other entry as absent; and
- in-flight acquisitions are shared only between callers with the same source
  configuration.

### The NuGet global folder is outside this boundary

`~/.nuget/packages` is read eagerly, ahead of source scoping, and a package
found there answers regardless of the configured sources. This is deliberate.
That folder holds what the user restored, not what dotnet-inspect fetched, and
binding to it eagerly is what makes an inspection agree with the bytes a build
actually consumed — the fidelity argument and the performance argument point the
same way. The scoping above governs content dotnet-inspect acquired, where the
tool chose the source and is accountable for the choice.

The rule is therefore *strict conformance to the sources dotnet-inspect fetched
from*, not *strict conformance to everything on the machine*. Callers that need
only configured sources to answer pass `--no-nuget-cache`, which drops the
folder from lookup. Note that NuGet does record an installed package's source in
`.nupkg.metadata`; the folder is treated as source-blind by choice, not for want
of the data.

A source is identified by a digest of its canonical URL, and canonicalization is
shared with the credential scope's `IsSameEndpoint` rather than reimplemented, so
one URL cannot mean two things in one tool. Scheme, host, default port,
percent-escape casing and a trailing path slash fold, because the URI grammar
defines them as equivalent; path and query case do not, because `/FeedA` and
`/feeda` can be different feeds on a case-sensitive server. The digest keeps
source URLs out of cache paths and makes every identity a valid path segment. It
is opacity rather than confidentiality: a feed URL is low entropy, and a local
reader who can see these paths can already see which packages were cached.

## Guarantees

The cache model provides:

- content scoped to the source that supplied it;
- one shared acquisition task per exact coordinate *and source configuration*
  within a process;
- complete-tree visibility through marked, atomic directory publication;
- convergence on one valid winner when processes publish concurrently; and
- no lock ordering between package coordinates.

It does not guarantee globally unique work. Separate processes may download and
extract the same immutable coordinate concurrently. It also does not provide a
power-loss-durable filesystem transaction.

## Precedents

The design follows familiar NuGet, Docker, and Git patterns without claiming to
implement any of those architectures exactly.

| Precedent | Pattern adopted by dotnet-inspect | Important difference |
| --- | --- | --- |
| NuGet global packages | Package id/version coordinates identify immutable entries, and readers require a completion marker. | NuGet.Client serializes installation with a cross-process file lock; dotnet-inspect allows independent staging and converges through atomic rename. NuGet's global folder is also source-blind, whereas dotnet-inspect's own cache is scoped by source. |
| Docker daemon | Concurrent requests for one exact package coordinate share one in-process task. | dotnet-inspect has no daemon, so separate CLI processes can still duplicate download and extraction work. |
| Git immutable objects | Writers build and validate complete content in a temporary sibling directory, then atomically rename it into place. | Entries are identified by the cache root, normalized package id, version, and source rather than by a content hash. |
| Git competing writers | One atomic rename wins; a losing publisher validates and uses the committed winner. | The loser may have performed duplicate work before converging. |
| Git mutable-state locks | Immutable entries do not require a long-lived cross-process lock when publishers can converge on one valid result. | Explicit locking remains appropriate for future mutable state that cannot use winner convergence. |

NuGet.Client is the closest domain comparison. Its global-packages installer
acquires a file lock for the target package, checks `.nupkg.metadata` to
recognize a completed installation, repairs an incomplete target while holding
the lock, and writes the metadata marker last. Processes waiting on that lock
then observe the marker and skip duplicate installation.

dotnet-inspect keeps NuGet's coordinate identity and marked-completion model but
chooses a different cross-process tradeoff. Each process may build a complete
candidate independently; whole-directory atomic rename selects one winner, and
losers validate and use it. This avoids holding a shared lock across package
work, but it permits duplicate download and extraction.

## Process-local single-flight

The process-wide in-flight registry is keyed by one exact package coordinate
together with the caller's ordered source list. Concurrent callers receive the
same task only when both match. Callers configured for different sources would
otherwise be handed each other's bytes, which is the same confusion the cache
scoping prevents, arriving by a different route. The
registry entry is removed after completion, whether acquisition succeeds or
fails, because the committed filesystem entry remains authoritative and is
revalidated by later requests.

The acquisition factory only downloads, extracts, validates, and commits its
own coordinate. It does not resolve dependencies or wait on another registry
key. Follow-on work such as dependency traversal, tool-package redirection, and
platform-pack projection starts only after that exact-coordinate task has
completed. Tool-package redirection is iterative and fails visibly when a
package id repeats in one redirect chain.

## Transactional publication

Package content publication follows this sequence:

1. Download and extract into temporary storage.
2. Copy the complete result into a unique sibling staging directory under the
   final directory's parent.
3. Validate the extracted package structure.
4. Write `.dotnet-inspect.complete` inside the staging directory.
5. Close all files opened by dotnet-inspect.
6. Move the staging directory atomically to its final
   `package-content-v4/{id}/{version}/{source}` path.
7. If another publisher won, validate and use its committed directory.

Readers accept only final directories with the expected structure and marker;
they never inspect staging paths. Platform-pack projection applies the same
transaction separately under `packs-v2` with its own completion marker. It
copies from committed package content and never mutates that package directory.

The versioned `package-content-v4` and `packs-v2` namespaces fence these
transactions from older direct-copy writers, and from earlier layouts that did
not scope entries by source.

## Filesystem coordination

`Directory.Move(stagingPath, targetPath)` delegates coordination to the
filesystem. Renaming a directory changes a parent directory's name-to-object
mapping; it does not copy the directory tree. The filesystem serializes that
metadata operation across processes, so callers observe one order of competing
moves without an application lock file.

The staging directory is a sibling of the final directory so both paths are on
the same filesystem or volume. This is required for atomic directory rename.
The relevant .NET behavior is consistent across supported desktop platforms:

| Platform | Primitive and losing-writer behavior |
| --- | --- |
| Windows | `Directory.Move` uses a non-overwriting `MoveFile` operation. One move succeeds; later moves fail because the destination exists. |
| Linux | `Directory.Move` uses Unix `rename` semantics. A committed target is non-empty, so a competing directory rename cannot replace it and fails. |
| macOS | Uses the same Unix directory-rename model as Linux; the committed non-empty destination prevents replacement. |

Before the winning rename, the final path is absent. After it, that path names
the complete tree that already contained its marker. A competing
`Directory.Move` reports an `IOException`; dotnet-inspect treats it as a lost
race only when the final directory validates, otherwise it surfaces the error.
Closing dotnet-inspect's own file handles before the move is especially
important on Windows, where incompatible open handles can prevent a rename.

These guarantees assume a local filesystem with normal rename semantics, such
as NTFS, APFS, ext4, or similar filesystems. A cache redirected to a network,
FUSE, or other unusual filesystem is limited by that filesystem's rename and
cache-coherency guarantees.

## Overlapping dependency work

Two in-process dependency graphs that overlap on a package either await the
same task for that exact coordinate or perform independent manifest reads.
Tasks for different coordinates do not wait on one another, so they cannot form
a wait cycle. Dependency traversal fetches only dependency nuspecs and uses a
traversal-local seen set to terminate dependency cycles.

Across processes there is no coordination wait. Publishers use unique staging
directories, and the final rename succeeds or reports a conflict without
acquiring a cross-process lock. The losing process validates the winner rather
than waiting for it while holding another resource.

This is safe because exact package coordinates are immutable, different
coordinates have disjoint final paths, and readers accept only complete,
committed directories. Duplicate cross-process work can cost network and disk
I/O, but it cannot expose partial content or create a cache-coordination
deadlock.

The single-coordinate factory is a required invariant. Future acquisition code
must not recursively await another package while its own in-flight entry is
active. A future mutable operation spanning multiple entries would instead
need an explicit ordering and cycle policy.

## Crash and failure boundaries

Atomic rename provides atomic visibility, not full transaction durability:

- A process crash before rename leaves the final path absent and may leave an
  unreferenced staging directory.
- A process crash after rename leaves a final directory that later readers
  validate before use.
- dotnet-inspect closes its files but does not `fsync` every file and parent
  directory as a power-loss transaction. Storage failure or power loss can
  therefore leave an invalid final entry.

An invalid final entry is preserved and acquisition fails visibly. Deleting it
without a lock could race and remove a newly committed winner. A later retry can
proceed after the cache is explicitly cleared.
