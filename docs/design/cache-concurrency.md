# Cache concurrency and publication

This document owns the coordination model for immutable package contents and
derived platform packs. [Version resolution](version-resolution.md) owns which
package coordinate is selected and when network lookup occurs; this document
owns how content for an exact coordinate becomes visible safely.

## Guarantees

The cache model provides:

- one shared acquisition task per exact coordinate within a process;
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
| NuGet global packages | Package id/version coordinates identify immutable entries, and readers require a completion marker. | NuGet.Client serializes installation with a cross-process file lock; dotnet-inspect allows independent staging and converges through atomic rename. |
| Docker daemon | Concurrent requests for one exact package coordinate share one in-process task. | dotnet-inspect has no daemon, so separate CLI processes can still duplicate download and extraction work. |
| Git immutable objects | Writers build and validate complete content in a temporary sibling directory, then atomically rename it into place. | Entries are identified by the cache root, normalized package id, and version rather than by a content hash. |
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

The process-wide in-flight registry is keyed by the final cache path for one
exact package coordinate. Concurrent callers receive the same task. The
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
   `package-content-v2/{id}/{version}` path.
7. If another publisher won, validate and use its committed directory.

Readers accept only final directories with the expected structure and marker;
they never inspect staging paths. Platform-pack projection applies the same
transaction separately under `packs-v2` with its own completion marker. It
copies from committed package content and never mutates that package directory.

The versioned `package-content-v2` and `packs-v2` namespaces fence these
transactions from older direct-copy writers.

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
