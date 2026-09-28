# Package archive range access

## Status, owner, and claim

This document is the normative owner for **ranged access to a package
archive**: reading a nupkg's central directory and selected entries from a
package source without acquiring the whole archive. It is the first slice of
[#8386](https://github.com/richlander/dotnet-inspect/issues/8386), whose goal
is that assemblies a person or an agent inspects all day do not cost network
all day. It adds one capability beside the existing full payload fetch; it
does not decide when a consumer uses which. That policy is a later slice of
the same issue and is a non-claim here.

Given one exact coordinate on one authorized source, the capability answers
two questions with bounded transfers:

- **What is in the archive?** The archive's directory: every entry's path,
  compressed and expanded sizes, compression method, CRC, and local-header
  offset.
- **Give me this entry.** One entry's expanded bytes, validated against the
  directory's declared size and CRC, under the expansion limit the caller
  supplies.

The capability owns the byte-range transport rules, the ZIP structure reading
shared with the local-folder source, the consistency rules across the several
requests one archive read needs, and the typed outcomes; it places the first
two in two host-neutral libraries below NuGetFetch (Library placement) and
owns their contracts; later consumers adopt them under their own designs
without moving ownership. It consumes, and
does not redefine, the source client contract of
[browser package sources](browser-package-sources.md#one-package-source-contract),
the operation context and deadline mechanics of the
[package source model](package-source-model.md#shared-operation-context-and-payload-lifetime),
the payload limits of
[package payload capacity](package-payload-capacity.md) and
`PackagePayloadLimits`, and the containment rules of the
[untrusted-data threat model](untrusted-data-threat-model.md).

## Basis

Measured on 2026-09-23 (owned probe, recorded on #8386):

| Asset | nupkg | managed libraries (expanded) | XML docs | native |
| --- | --- | --- | --- | --- |
| `Microsoft.NETCore.App.Ref` 9.0.18 | 6 MB | 10 MB, 235 files | 28 MB | 0 |
| `Microsoft.NETCore.App.Runtime.linux-x64` 9.0.17 | 37 MB | 56 MB, 169 files | 0 | 19 MB |

nuget.org's flat container answers an HTTP `Range` request with `206`. The
central directory of the ref pack is under 64 KB. An exploratory query such
as `find` needs the directory and a handful of `lib/<tfm>/*.dll` entries; the
complete download and the eager extraction of every entry to disk are the
cost it pays today. For native-heavy packages, where a few MB of managed
libraries sit inside hundreds of MB of native assets, the ratio is far
larger.

The complexity basis is therefore a user-observable one: an exploratory
command over a package set finishes in the time of a few small transfers
rather than the time of full downloads and extractions, and it does so
without changing what the inventory path caches.

## Contract

### Capability

A source client that can serve byte ranges exposes the capability as a
separate optional interface on the client implementation; `IPackageSourceClient`
is unchanged. The capability is obtained for one exact coordinate and one
authorized source result identity, and every transfer it makes carries that
identity, the caller's operation context, and the caller's credential exactly
as the full fetch does.

Local-folder sources satisfy the capability trivially: the archive is a
seekable file, and the same ZIP reader runs over it with no transport. The
central-directory and local-header reading that `LocalPackageArchiveReader`
implements today for the manifest moves into the ZIP library below; the local
reader consumes it rather than keeping a private copy, and keeps projecting
the reader's refusals into its own outcomes exactly as it does today.

The caller supplies the limits as a primitive record owned by the ZIP
library, `ZipReadLimits`: the archive total in bytes, the directory caps
(entries and bytes), the per-entry expanded bound, and the entry-read slack
described below (at most 64 KiB). Neither lower library can reference
`PackagePayloadLimits`, which lives in `DotnetInspector.Packages`, and unlike
the full fetch, whose limits that layer applies after the stream returns, a
ranged read must know its bounds before each transfer; so the Packages layer
maps its limits into this record at the lease step and chooses the slack,
which `PackagePayloadLimits` has no concept of (its default is that layer's
to set; the contract suite fixes the slack explicitly per case). Every
refusal of a bound settles as `ResponseRejected`, the kind every existing
NuGetFetch bound uses; `InvalidResponse` is reserved for malformed structure.

### Library placement

The capability is two host-neutral libraries below NuGetFetch and one
adapter inside it. NuGetFetch depends on the ZIP library, which depends on
the range library; neither lower library references NetworkAccess,
InertText, or anything NuGet.

- **`BinaryFetch`, range fetch over HTTP.** Owns byte access to one remote
  representation through a random-access contract with three operations: a
  **tail read** (the last *n* bytes, or the whole representation when it is
  shorter; records the visible total or "unknown" and the validator), an
  **exact range read** (exactly the requested bytes), and **confirm length**
  (the consumer hands back the length it derived from the bytes; the source
  refuses a length that disagrees with anything it observed, including a
  short tail body that is not the whole representation). The HTTP
  implementation owns every per-response protocol check — status, a visible
  `Content-Range` range against the request, `Content-Length` against the
  body, a non-tail body against the requested length, the byte unit — and the
  agreement of validator and visible total between later requests and the
  first (`If-Range`, `206` versus `200`). The seekable-stream implementation
  serves local files through the same contract. Its outcomes are about the
  representation: `RangeIgnored`, `RepresentationChanged`, `InvalidResponse`
  (a protocol violation), and transport status; exceptions the consumer's
  own client raises pass through unchanged. It knows nothing about ZIP and
  holds no length in headers-hidden mode until the consumer confirms one.
- **`ZipFetch`, ZIP read over a random-access source, remote or local.** Owns
  structure: the end-of-central-directory record and the **derived total**
  (directory offset plus directory length plus the record and its comment),
  which it confirms with the source before any further read, so the short
  tail and the tail's visible total are checked against the archive's own
  declaration, as malformed, `InvalidResponse`; the central directory with
  its caps; local headers; entry extent and clamping; bounded inflate; CRC;
  and the Zip64 and compression-method refusals. Its outcomes are about the
  archive: malformed, over a bound, unsupported. It knows nothing about HTTP;
  remote and local are the same code over different sources.
- **The NuGetFetch adapter** owns coordinates, flat-container URLs,
  credentials, source identity, the non-ranged memory on the client
  instance, and the mapping of both lower vocabularies into
  `PackageArchiveReadResult<T>` under the rules in Outcomes. The seam between
  the adapter and the HTTP source is a **per-request send delegate**
  (request and token in, response out) that the source calls for every
  ranged request; the adapter supplies one that applies credentials and its
  browser request options and wraps each send in the existing retry and
  per-request deadline mechanics exactly as the full fetch does, so each
  ranged request is one request under the shared context and a deadline
  surfaces as the typed timeout the adapter already maps.

Gate attribution follows: the per-response protocol checks (rows 4, 5, 6a,
6b's slice-length acceptance, and the headers-hidden protocol part of 15)
belong to the range library's suite; the derived-total rules (row 6's tail
mismatch, 6b's short-archive acceptance, 9, and the derived-total part of 15)
and every structural row belong to the ZIP library's suite; rows 12 and 13
and the adapter's mapping belong to the capability's suite in NuGetFetch.

This document owns the two libraries' contracts as its implementation
boundary. `SymbolPackageDownloader`, which fetches whole `.snupkg` archives
to extract one PDB, is the named second consumer and adopts both libraries
in a later slice under its own design; ownership of the library contracts
stays here, and that design consumes them.

The memory that a source ignored `Range` lives on the source client
instance. The client's lifetime belongs to the host's composition, which the
package source model does not own: a CLI invocation may build several
compositions and so may probe a non-ranged source once per client, and the
browser keeps its clients for the page.

### Reading the directory

The reader fetches the archive's tail (the end-of-central-directory record
plus the largest comment the format allows, capped at 64 KB plus the record),
locates the record, and reads the central directory from it. When the
directory does not fit in the tail, the reader makes exactly one more ranged
request for it. The result is the archive directory: an immutable list of
entries with path, compression method, compressed size, expanded size, CRC,
and local-header offset, plus the archive's total length and the validator
the source supplied (`ETag`, `Last-Modified`, or none).

The archive's total length is derived from the end-of-central-directory
record itself (directory offset plus directory size plus the record and its
comment), so it is known wherever the record is. Where `Content-Range` is
visible, its total is cross-checked against the derived total; `Content-Length`
on a `206` is the slice's length, never the archive's, and must equal the
body length. The body must equal the requested length on every request
except the tail request, where a shorter body is the whole archive and must
equal the derived total. The record and its comment must end exactly at the
end of the tail; trailing bytes after them, a tail-request `Content-Range`
total that differs from the derived total (on a later request that
difference is `ArchiveChanged`, under the transport rules), a visible
`Content-Range` range that does not match the request, a `Content-Length`
that differs from the body, a body length that matches neither rule, or a
directory offset that does not lie before the record are `InvalidResponse`. An archive shorter than the tail request arrives whole;
the reader recognizes that by the derived total and needs no second request.

The directory is bounded before it is parsed with the same caps the
[local-folder source](local-folder-package-source.md) already fixes: 50,000
entries and 16 MiB of central directory. The shared reader applies them for
every source; the local source keeps supplying them through its options. An
entry count or directory size above the caps, or a derived total above the
archive-total limit, is refused as `ResponseRejected` before any further
transfer. The derived total is confirmed with the source before the
archive-total limit is applied, so a total the source can refute (a known
length, a visible header, or fewer bytes than the tail already served) is
malformed rather than over a bound; the bound applies to a confirmed length.
A derived total shorter than the tail bytes read is malformed before the
source is consulted. An entry that lies within the retained tail is served
from it without another transfer, so a small archive costs one request. An archive whose end-of-central-directory record cannot be found,
or whose declared sizes do not agree with the transfer, is refused as
`InvalidResponse`. Zip64 archives are refused as `ArchiveUnsupported`; no
nupkg a supported source serves needs them (none of the 1,690 nupkgs in the
reviewer's local corpus used Zip64 or data descriptors), and the refusal is
visible and falls back to the full fetch rather than misreading.

### Reading an entry

An entry read fetches the entry's local header and compressed data starting
at the local-header offset. The local header's name and extra-field lengths
are known only once the header is read, and the local extra field is not
reliably the central one: in the reviewer's corpus of 1,690 nupkgs, 906
entries across 35 packages have a local extra field that differs from the
directory's, and in some (`PCLStorage` 1.0.2, `Microsoft.Spatial` 7.2.0) it
is longer. So the first request is sized from the directory entry (header,
name and extra lengths as the directory declares them, compressed size) plus
the slack from the limits record, and every entry request is clamped to end
at or before the central-directory offset, since no entry's data can lie
beyond it; the median distance from the last entry's data to the archive end
in the reviewer's corpus is 1,720 bytes, so an unclamped slack would overrun
routinely. When the local header shows that the compressed data extends past
what arrived, the reader makes exactly one follow-up request for the
remainder, rechecking the extent with the header's own lengths. Before any
request, the entry's extent must lie inside the archive: local-header offset
plus header plus compressed size at or before the central-directory offset,
otherwise `InvalidResponse`, so a crafted directory entry cannot drive a
large transfer.

The reader validates the local-header signature and the header's agreement
with the directory entry (name, method, and CRC when the header carries it),
then expands the data (stored or deflate; other methods are
`ArchiveUnsupported`). An entry whose expanded size the directory declares
above the caller's expanded bound is refused as `ResponseRejected` before any
transfer. Expansion then proceeds up to the bound, in the order the local
source already uses: crossing the bound mid-stream is `ResponseRejected`
without retaining partial bytes, and a finished expansion whose length
differs from the declared size is malformed and is `InvalidResponse`. The CRC
is checked on completion; a mismatch is `InvalidResponse`. The expanded bytes
are returned as caller-owned content, never the response stream.

### Reading several entries

A consumer that needs several entries of one archive reads them as a batch.
Every entry passes the checks above, and the entries' declared expansion
together must fit the caller's total bound, before any transfer; otherwise
the batch is refused as it would be for one entry. The reader then plans its
requests from the entries' first-request extents in archive order: extents
separated by at most the limits' merge gap become one request, which also
transfers the unselected bytes between them, and no request exceeds 64 MiB.
Up to the limits' concurrency, requests are in flight at once. Each entry is
then read exactly as a single entry is, over the fetched bytes, so a longer
local extra field still costs exactly one follow-up, and every check above
still applies. The batch returns every entry, in the order requested, or one
failure or refusal and no content.

The merge gap and the concurrency are caller-supplied bounds on the limits
record: the gap is at most 1 MiB and defaults to zero, joining only extents
that touch; the concurrency is 1 to 16 and defaults to 1. A concurrent batch
starts only after the directory read, so every batch request carries the
validator the first response supplied. Round trips, not bandwidth, dominate
a ranged read on a fast link: nuget.org answers a range in 40 to 80 ms here,
where the whole 10 MB `Avalonia` 12.1.2 archive arrives in 0.4 s, so 22
sequential entry requests cost more than the complete download they avoid.
The random-access sources accept concurrent reads: the HTTP source updates
its observed length and validator under a lock, and the stream source
serializes reads that share its position.

### Transport rules

- A ranged request asks for exactly the bytes the reader needs. The
  response must be `206`; where `Content-Range` is visible, its range must
  match the request (for a tail request larger than the archive, the whole
  archive, `0` to the end, as the HTTP range specification requires) and its
  total must equal the derived archive length.
- A `200` answer to a ranged request means the source ignored the range. The
  reader does not consume the body as a full download; it closes the response
  and reports `RangeIgnored` for that source, so the caller takes the full
  fetch path. The client instance remembers the source as non-ranged for its
  own lifetime and does not probe it again.
- Every request after the first carries `If-Range` with the validator (`ETag`
  or `Last-Modified`) the first response supplied. A `200` answer to such a
  request, or a `206` whose total or validator differs, means the archive
  changed between requests; the reader reports `ArchiveChanged` and retains
  nothing. nuget.org content is immutable, so this protects the private feeds
  and mirrors that are not.
- A source that supplies no validator is still readable when the derived
  totals agree across requests. The directory result records the validator
  kind as none, which is the outcome surface for "archive identity across
  requests unverified"; every entry is still checked against the directory
  through its local header and CRC, so mixed archive versions cannot pass
  undetected.
- Request deadlines, the operation ceiling, caller cancellation, retry, and
  credential application are the existing `NuGetOperationContext` and
  `NuGetSourceRequest` mechanics; this capability adds no deadline of its
  own. Each ranged request is one request under the shared context. Ranged
  requests retry transient statuses on both the v3 and gallery clients; the
  v3 client's complete fetch does not, and this capability does not change
  it.

### Browser host

In a browser, `fetch` performs the ranged request, but a CDN that does not
expose `Content-Range` to scripts (nuget.org exposes `ETag`, `Content-Length`,
and `Last-Modified`, not `Content-Range`) leaves the reader with the status
and the body. The rules above already make that sufficient: the archive total
comes from the end-of-central-directory record, not from `Content-Range`; a
tail body shorter than the request is the whole archive when the derived
total equals the body length (this match applies to the tail request only,
and a short tail whose derived total differs from its body length is
`InvalidResponse`); and every other `206` matches only when its body length
equals the requested length. `Content-Length`, which nuget.org does expose,
is the slice's length and is checked against the body as on other hosts;
where any other header is visible it is cross-checked the same way. Both a suffix
`Range` and `If-Range` trigger a CORS preflight, which nuget.org allows
(`access-control-allow-headers: range,if-range`, probed 2026-09-23). The
contract suite runs the reader in a headers-hidden mode to gate these rules;
the end-to-end browser read is `unverified` until the Inspect Web adoption
slice adds its Playwright gate.

### Outcomes

The capability owns its own closed result type, `PackageArchiveReadResult<T>`,
issued for the directory and for entry content, rather than extending the
source-result contract of [browser package sources](browser-package-sources.md),
whose operation-result container and failure kinds are closed and stay
unchanged. A read result holds exactly one of: the value; an ordinary
`PackageSourceFailure` issued by the existing factory for the transport class
of failures (`AuthenticationRequired`, `Timeout`, `Transport`,
`InvalidResponse`, `ResponseRejected`, `NotFound`), issued through the
factory's package-payload method (`FailedPackage(...).Failure`, which stamps
the package-payload capability and the exact coordinate), so authorization,
deadline, and credential-safety semantics are inherited verbatim; or one of
three range-specific reasons owned here — `RangeIgnored` (a `200` to a ranged
request), `ArchiveChanged` (validator or total changed between requests), and
`ArchiveUnsupported` (Zip64, or a compression method other than stored and
deflate). The first two mean "take the full fetch"; the third means the
archive cannot be read by range at all. The adapter maps the lower
vocabularies one to one: the range library's `RangeIgnored`,
`RepresentationChanged`, and `InvalidResponse` become `RangeIgnored`,
`ArchiveChanged`, and `InvalidResponse`, its transport statuses and the
passed-through client exceptions become the ordinary source failures, and
the ZIP library's malformed, over-a-bound, and unsupported outcomes become
`InvalidResponse`, `ResponseRejected`, and `ArchiveUnsupported`. The
existing `Unsupported` source failure keeps its meaning, that a client or
source lacks an operation, and is not reused for per-archive conditions. The
capability never turns a failure into an empty directory or a truncated
entry.

## Boundary

Inputs: an exact coordinate; an authorized `PackageSourceResultIdentity`; the
operation context; the caller's `ZipReadLimits` (archive total,
enforced against the derived total before the directory is parsed; directory
entry and byte caps; per-entry expanded bound; entry-read slack). Outputs:
the archive directory with its validator kind; per-entry expanded content;
`PackageArchiveReadResult<T>` failures.

Non-claims:

- when a consumer reads by range and when it fetches the whole archive, and
  the caching of directories and entries between invocations, which the
  [package cache policy](package-cache-policy.md) owns; the reader itself
  populates no persistent store;
- symbol packages and any archive other than the package's nupkg, as uses of
  this capability (the two libraries' contracts are archive-agnostic, and a
  later consumer such as the symbol-package downloader adopts them under its
  own design);
- Zip64, encrypted entries, and compression methods other than stored and
  deflate.

## Pathological cases and gates

The contract suite runs against a fake source whose handler serves a real
nupkg built by the harness, can ignore `Range`, can change the archive
between requests, can drop `Content-Range`, and can serve a crafted archive.
All gates run in Release.

| Case | Expected | Gate |
| --- | --- | --- |
| 1. Directory within the tail | one ranged request; directory equals `ZipArchive`'s view of the same file (oracle) | contract suite |
| 2. Directory larger than the tail | exactly two ranged requests; same equality | contract suite |
| 3. Entry read | one ranged request; expanded bytes equal the original file; CRC checked | contract suite |
| 4. `200` to a ranged request | `RangeIgnored`; no bytes retained; the same client instance does not range that source again | contract suite |
| 5. Archive replaced between requests (validator differs, or `200` to `If-Range`) | `ArchiveChanged`; nothing retained | contract suite |
| 6. Tail-request `Content-Range` total disagrees with the derived total; a visible `Content-Range` range that does not match the request | `InvalidResponse` | contract suite |
| 6a. `Content-Length` disagrees with the body; a non-tail body that differs from the requested length; a short tail body that differs from the derived total | `InvalidResponse` | contract suite |
| 6b. A correct `206` whose `Content-Length` is the slice's length; a short tail body equal to the derived total (real asset: `Microsoft.NETCore.Platforms` 1.0.1, 17,876 bytes) | accepted | contract suite |
| 7. Entry declared above the expanded bound (`ResponseRejected`); entry extent past the central-directory offset (`InvalidResponse`) | refused before any transfer | contract suite |
| 8. Entry crosses the caller's bound while expanding (declared size at or under the bound) | `ResponseRejected` mid-stream; no partial content | contract suite |
| 8a. Entry finishes expanding to other than its declared size, within the bound (crafted deflate) | `InvalidResponse`; no content | contract suite |
| 9. No end-of-central-directory record; trailing bytes after the record; directory offset not before the record | `InvalidResponse` | contract suite |
| 9a. Directory over the entry or byte caps; derived total over the archive-total limit | `ResponseRejected` before any further transfer | contract suite |
| 9b. A limits record with slack above 64 KiB | refused at construction (a caller argument, not a response bound) | contract suite |
| 10. Zip64 archive; unsupported method | `ArchiveUnsupported`, visible | contract suite |
| 11. Local-folder archive through the shared reader | directory and entries equal the `ZipArchive` oracle; the local source's manifest outcomes are unchanged as its owner specifies them (caps as `ResponseRejected`, malformed structure as invalid data) | contract suite (local) |
| 11a. Last entry whose data ends at the central directory | the clamped first request never runs past the directory offset; entry read completes with the CRC | contract suite |
| 12. Operation ceiling during an entry read | terminal typed timeout, no partial content | contract suite |
| 13. Motivating asset | `Microsoft.NETCore.App.Ref` 9.0.18 from nuget.org: directory in under 100 KB of transfer, `ref/net9.0/System.Runtime.dll` expanded and parseable by the metadata reader | Slow network gate, plus a preserved probe as design evidence |
| 14. Local extra field longer than the directory declares, slack fixed at 0 | `PCLStorage` 1.0.2 (real asset, preserved as `fixtures/nugetfetch/pclstorage.1.0.2.nupkg`; 40 of its entries carry longer local extra fields): first request short by the extra bytes, exactly one follow-up rechecked against the directory offset, CRC passes | contract suite |
| 14a. Same asset with the Packages layer's default slack | no follow-up; CRC passes | `PackageRangedRealizationTests.RangedRealize_RealAsset_ReadsTheSelectedEntriesInOneRequest`, where that layer sets the default ([package source model](package-source-model.md#ranged-payload-realization)) |
| 16. Batch of adjacent and separated entries | adjacent extents share one request, separated ones do not; contents equal the originals in requested order | contract suite |
| 16a. Batch merge gap | a gap within the merge gap is bridged in one request, a wider one is not | contract suite |
| 16b. Batch concurrency | the requests in flight never exceed the limit and reach it | contract suite |
| 16c. Batch over the total bound; one failed request | refused before any transfer; the batch fails with that request's failure and no content | contract suite |
| 16d. Batch over the real asset at slack 0 | follow-ups still read; contents equal the `ZipArchive` oracle | contract suite |
| 16e. Batch over HTTP | merged requests carry the credential and `If-Range`; a changed validator is `ArchiveChanged` with no content | contract suite (adapter) |
| 15. Headers hidden (browser mode) | archive smaller than the tail read whole from the derived total; `206` matched by body length; validator rules where visible | contract suite (headers-hidden mode); end-to-end browser read `unverified` until the Inspect Web slice |

## Adoption

1. This document; the `BinaryFetch` and `ZipFetch` libraries with their own
   contract suites (the ZIP suite against a `ZipArchive` oracle, the range
   suite against a fake handler), `LocalPackageArchiveReader` consuming the
   ZIP library, the NuGetFetch adapter on the v3 and gallery clients, and
   the capability's contract suite. No production consumer changes behavior
   at this head; the CLI still fetches whole archives.
2. The operation lease gains the ranged step under the package source
   model's ownership
   ([Ranged payload realization](package-source-model.md#ranged-payload-realization)),
   ranged content materializes only the entries a House realization selects,
   and the exact-package search Root (`find` member search, `implements`,
   `extensions`, and `depends` with one exact `--package` and `--tfm`) adopts
   it: the first production-host consumer and the CLI demo. A second part
   moves the remaining search scopes — `find` type search through the
   declaration locator, and multi-package, floating-version, and
   targetless searches through the assembly set — onto the House path with
   ranged access; that part is also Package Version Service adoption step 3
   (#8285), and the legacy `PackageExtractor` latest path retires there.
3. Inspect Web adopts the same content over the browser reader for package
   opens that today buffer the whole nupkg, under the browser-host rule above.
4. The [package cache policy](package-cache-policy.md) (#8386) decides which
   archives are acquired complete and cached and which are read by range. It
   is adopted before the second part of step 2.

Total: four slices, the second in two parts; both hosts named; the retired
path named.
