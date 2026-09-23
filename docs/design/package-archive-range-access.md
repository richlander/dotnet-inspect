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
requests one archive read needs, and the typed outcomes. It consumes, and
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
seekable file, and the same reader runs over it with no transport. The
central-directory and local-header reading that `LocalPackageArchiveReader`
implements today for the manifest becomes the shared reader; the local reader
consumes it rather than keeping a private copy, and keeps projecting the
reader's refusals into its own outcomes exactly as it does today.

The caller supplies the limits as a NuGetFetch-owned primitive record,
`PackageArchiveReadLimits`: the archive total in bytes, the directory caps
(entries and bytes), the per-entry expanded bound, and the entry-read slack
described below (at most 64 KiB). NuGetFetch cannot reference
`PackagePayloadLimits`, which lives in `DotnetInspector.Packages`, and unlike
the full fetch, whose limits that layer applies after the stream returns, a
ranged read must know its bounds before each transfer; so the Packages layer
maps its limits into this record at the lease step. Every refusal of a bound
settles as `ResponseRejected`, the kind every existing NuGetFetch bound uses;
`InvalidResponse` is reserved for malformed structure.

The memory that a source ignored `Range` lives on the source client
instance, which the package source model keeps for the invocation; a later
invocation probes again.

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
comment), so it is known wherever the record is, and it is cross-checked
against `Content-Range` and `Content-Length` wherever those are visible. The
record and its comment must end exactly at the end of the tail; trailing
bytes after them, a disagreement between the derived total and a visible
header, or a directory offset that does not lie before the record are
`InvalidResponse`. An archive shorter than the tail request arrives whole;
the reader recognizes that by the derived total and needs no second request.

The directory is bounded before it is parsed with the same caps the
[local-folder source](local-folder-package-source.md) already fixes: 50,000
entries and 16 MiB of central directory. The shared reader applies them for
every source; the local source keeps supplying them through its options. An
entry count or directory size above the caps, or a derived total above the
archive-total limit, is refused as `ResponseRejected` before any further
transfer. An archive whose end-of-central-directory record cannot be found,
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
entries across 35 packages (for example `Grpc.Net.Client` 2.80.0 and
`Grpc.Tools` 2.80.0) carry a longer local extra field than the directory
declares. So the first request is sized from the directory entry (header,
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
`ArchiveUnsupported`). Expansion stops at the caller's expanded bound: an
entry whose expanded size the directory declares above the bound is refused
as `ResponseRejected` before any transfer, and an entry whose actual
expansion exceeds the bound or the declared size is refused mid-stream as
`ResponseRejected` without retaining partial bytes. The CRC is checked on
completion; a mismatch is `InvalidResponse`. The expanded bytes are returned
as caller-owned content, never the response stream.

### Transport rules

- A ranged request asks for exactly the bytes the reader needs. The
  response must be `206` with a `Content-Range` whose range matches the
  request and whose total equals the archive length the directory read
  established.
- A `200` answer to a ranged request means the source ignored the range. The
  reader does not consume the body as a full download; it closes the response
  and reports `RangeIgnored` for that source, so the caller takes the full
  fetch path. The source is remembered as non-ranged for the rest of the
  invocation.
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
  own. Each ranged request is one request under the shared context.

### Browser host

In a browser, `fetch` performs the ranged request, but a CDN that does not
expose `Content-Range` to scripts (nuget.org exposes `ETag`, `Content-Length`,
and `Last-Modified`, not `Content-Range`) leaves the reader with the status
and the body. The rules above already make that sufficient: the archive total
comes from the end-of-central-directory record, not from `Content-Range`; a
tail body shorter than the request is the whole archive when the derived
total equals the body length (this match applies to the tail request only);
and every other `206` matches only when its body length equals the requested
length. Where a header is visible it is cross-checked as on other hosts. Both a suffix
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
archive cannot be read by range at all. The existing `Unsupported` source
failure keeps its meaning, that a client or source lacks an operation, and
is not reused for per-archive conditions. The capability never turns a
failure into an empty directory or a truncated entry.

## Boundary

Inputs: an exact coordinate; an authorized `PackageSourceResultIdentity`; the
operation context; the caller's `PackageArchiveReadLimits` (archive total,
enforced against the derived total before the directory is parsed; directory
entry and byte caps; per-entry expanded bound; entry-read slack). Outputs:
the archive directory with its validator kind; per-entry expanded content;
`PackageArchiveReadResult<T>` failures.

Non-claims:

- when a consumer reads by range and when it fetches the whole archive, and
  the size-differentiated cache policy (#8386, later slices);
- the background completion of a surgical read into the cache (the warm
  queue, #8386);
- caching of directories or entries between invocations: a ranged read
  populates no persistent store at this head;
- symbol packages and any archive other than the package's nupkg;
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
| 4. `200` to a ranged request | `RangeIgnored`; no bytes retained; the source is not ranged again in the invocation | contract suite |
| 5. Archive replaced between requests (validator differs, or `200` to `If-Range`) | `ArchiveChanged`; nothing retained | contract suite |
| 6. `Content-Range` or `Content-Length` disagrees with the derived total | `InvalidResponse` | contract suite |
| 7. Entry declared above the expanded bound (`ResponseRejected`); entry extent past the central-directory offset (`InvalidResponse`) | refused before any transfer | contract suite |
| 8. Entry expands past its declared size (crafted deflate) | `ResponseRejected` mid-stream; no partial content | contract suite |
| 9. No end-of-central-directory record; trailing bytes after the record | `InvalidResponse` | contract suite |
| 9a. Directory over the entry or byte caps; derived total over the archive-total limit | `ResponseRejected` before any further transfer | contract suite |
| 10. Zip64 archive; unsupported method | `ArchiveUnsupported`, visible | contract suite |
| 11. Local-folder archive through the shared reader | directory and entries equal the `ZipArchive` oracle; the local source's manifest outcomes are unchanged as its owner specifies them (caps as `ResponseRejected`, malformed structure as invalid data) | contract suite (local) |
| 11a. Last entry whose data ends at the central directory | the clamped first request never runs past the directory offset; entry read completes with the CRC | contract suite |
| 12. Operation ceiling during an entry read | terminal typed timeout, no partial content | contract suite |
| 13. Motivating asset | `Microsoft.NETCore.App.Ref` 9.0.18 from nuget.org: directory in under 100 KB of transfer, `ref/net9.0/System.Runtime.dll` expanded and parseable by the metadata reader | Slow network gate, plus a preserved probe as design evidence |
| 14. Local extra field longer than the directory declares | `Grpc.Net.Client` 2.80.0 (real asset, preserved as a fixture): first request short by the extra bytes, exactly one follow-up, CRC passes | contract suite |
| 15. Headers hidden (browser mode) | archive smaller than the tail read whole from the derived total; `206` matched by body length; validator rules where visible | contract suite (headers-hidden mode); end-to-end browser read `unverified` until the Inspect Web slice |

## Adoption

1. This document; the capability, the shared archive reader (with
   `LocalPackageArchiveReader` consuming it), the HTTP implementation on the
   v3 and gallery clients, and the contract suite. No production consumer
   changes behavior at this head; the CLI still fetches whole archives.
2. The operation lease gains the ranged steps under the package source
   model's ownership, an `IPackageContent` over ranged access materializes
   only the entries a consumer opens, and `find` with the search scopes adopt
   it on the House path with stream-based assembly reading. This is the first
   production-host consumer and the CLI demo; it is also Package Version
   Service adoption step 3 (#8285), and the legacy `PackageExtractor` latest
   path retires there.
3. Inspect Web adopts the same content over the browser reader for package
   opens that today buffer the whole nupkg, under the browser-host rule above.
4. The warm queue and the size-differentiated cache policy (#8386) decide
   when a ranged read is followed by a complete download.

Total: four slices; both hosts named; the retired path named.
