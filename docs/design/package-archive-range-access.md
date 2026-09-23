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
consumes it rather than keeping a private copy.

### Reading the directory

The reader fetches the archive's tail (the end-of-central-directory record
plus the largest comment the format allows, capped at 64 KB plus the record),
locates the record, and reads the central directory from it. When the
directory does not fit in the tail, the reader makes exactly one more ranged
request for it. The result is the archive directory: an immutable list of
entries with path, compression method, compressed size, expanded size, CRC,
and local-header offset, plus the archive's total length.

The directory is bounded before it is parsed: an entry count or directory
size above the reader's caps is refused as `InvalidResponse`, and an archive
whose end-of-central-directory record cannot be found, or whose declared
sizes do not agree with the transfer, is refused the same way. Zip64
archives are refused as `Unsupported`; no nupkg a supported source serves
needs them, and the refusal is visible rather than a silent misread.

### Reading an entry

An entry read fetches the entry's local header and compressed data in one
ranged request from the local-header offset, validates the local-header
signature and the header's agreement with the directory entry, and expands
the data (stored or deflate; other methods are `Unsupported`). Expansion
stops at the caller's `maxExpandedBytes`: an entry whose expanded size the
directory declares above the limit is refused before any transfer, and an
entry whose actual expansion exceeds the limit or the declared size is
refused mid-stream without retaining partial bytes. The CRC is checked on
completion; a mismatch is `InvalidResponse`. The expanded bytes are returned
as caller-owned content, never the response stream.

### Transport rules

- A ranged request asks for exactly the bytes the reader needs. The
  response must be `206` with a `Content-Range` whose range matches the
  request and whose total equals the archive length the directory read
  established.
- A `200` answer to a ranged request means the source ignored the range. The
  reader does not consume the body as a full download; it closes the response
  and reports `Unsupported` for that source, so the caller takes the full
  fetch path. The source is remembered as non-ranged for the rest of the
  invocation.
- Every request after the first carries `If-Range` with the validator (`ETag`
  or `Last-Modified`) the first response supplied. A `200` answer to such a
  request, or a `206` whose total or validator differs, means the archive
  changed between requests; the reader reports `ArchiveChanged` and retains
  nothing. nuget.org content is immutable, so this protects the private feeds
  and mirrors that are not.
- A source that supplies no validator is still readable when its
  `Content-Range` totals agree across requests; the reader records that the
  archive identity is unverified across requests.
- Request deadlines, the operation ceiling, caller cancellation, retry, and
  credential application are the existing `NuGetOperationContext` and
  `NuGetSourceRequest` mechanics; this capability adds no deadline of its
  own. Each ranged request is one request under the shared context.

### Browser host

In a browser, `fetch` performs the ranged request, but a CDN that does not
expose `Content-Range` to scripts (nuget.org exposes `ETag`, `Content-Length`,
and `Last-Modified`, not `Content-Range`) leaves the reader with the status
and the body. The browser reader accepts a `206` whose body length equals the
requested length as a matching range and applies the same validator and
total-length rules where the headers are visible. This degradation is named
here so the Wasm adoption slice does not rediscover it.

### Outcomes

The capability returns `PackageSourceOperationResult<T>` shapes from the
source-result contract with the existing failure kinds; it introduces two
typed reasons beside them, `Unsupported` (range ignored, Zip64, or an
unsupported compression method) and `ArchiveChanged`. It never turns a
failure into an empty directory or a truncated entry.

## Boundary

Inputs: an exact coordinate; an authorized `PackageSourceResultIdentity`; the
operation context; the caller's limits (`PackagePayloadLimits.MaxArchiveBytes`
for the archive total, `maxExpandedBytes` per entry, and the reader's
directory caps). Outputs: the archive directory; per-entry expanded content;
typed failures.

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
| 4. `200` to a ranged request | `Unsupported`; no bytes retained; the source is not ranged again in the invocation | contract suite |
| 5. Archive replaced between requests (validator differs, or `200` to `If-Range`) | `ArchiveChanged`; nothing retained | contract suite |
| 6. `Content-Range` total disagrees with the directory read | `InvalidResponse` | contract suite |
| 7. Entry declared above `maxExpandedBytes` | refused before any transfer | contract suite |
| 8. Entry expands past its declared size (crafted deflate) | refused mid-stream; no partial content | contract suite |
| 9. No end-of-central-directory record; directory over the caps | `InvalidResponse` | contract suite |
| 10. Zip64 archive; unsupported method | `Unsupported`, visible | contract suite |
| 11. Local-folder archive through the shared reader | directory and entries equal the `ZipArchive` oracle; the manifest read the local source performs today is unchanged | contract suite (local) |
| 12. Operation ceiling during an entry read | terminal typed timeout, no partial content | contract suite |
| 13. Motivating asset | `Microsoft.NETCore.App.Ref` 9.0.18 from nuget.org: directory in under 100 KB of transfer, `ref/net9.0/System.Runtime.dll` expanded and parseable by the metadata reader | Slow network gate, plus a preserved probe as design evidence |

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
