# Library-Metadata correspondence

## Status

This design owns
[issue #7238](https://github.com/richlander/dotnet-inspect/issues/7238) and
the projection-ownership repair in
[#7270](https://github.com/richlander/dotnet-inspect/issues/7270).
It is the focused precursor selected during PR #7201 scope recovery.

## Authority and exact claim

`DotnetInspector.LibraryMetadata` owns this claim:

> Given one exact realized Library, its active operation authority, an explicit
> API-surface scope, and finite extraction bounds, inspect the owner-attested
> API-assembly bytes synchronously and issue resource-free correspondence
> between the resulting Metadata surface and that exact Library content, or
> return typed non-success.

This owner composes existing Library borrowing and Metadata extraction
contracts. It does not redefine either.

## Basis

[Library ownership and borrowing](library-ownership-and-borrowing.md) supplies:

- one exact `LibraryReference`;
- its exact API-assembly `LibraryContentReference`;
- one caller-owned `LibraryOperationLease`; and
- a synchronous owner-attested content snapshot.

[Assembly inspection query](assembly-inspection-query.md) and
`ArtifactAssemblyInspection` supply:

- SRM-only Metadata admission;
- managed assembly identity and module-version identity;
- bounded API-surface extraction; and
- the rule that `ArtifactAssemblyProjection` is minted only by Metadata during
  Artifact admission and is not reconstructed by consumers.

Assembly identity alone is not exact correspondence. Two independently
realized Libraries may legitimately contain assemblies with equivalent name,
version, culture, and public-key token. Exact correspondence therefore retains
the Library content reference that supplied the bytes, including its existing
Artifact generation and identity, plus the module-version identity extracted
during that same borrow.

## Typed boundary

One request names:

- the exact `LibraryReference`;
- one `ApiSurfaceExtractionScope`;
- finite `ApiSurfaceExtractionBounds`; and
- the existing `typesOnly` and compiler-generated inclusion choices.

Execution receives a matching active `LibraryOperationLease` without consuming
it. The operation borrows only `LibraryReference.ApiAssembly`.

A completed result contains one `LibraryApiSurfaceCorrespondence`:

- the exact API `LibraryContentReference`;
- the non-empty module-version identity extracted from those bytes;
- the exact `ApiSurface` instance extracted during the same borrow;
- the extraction choices; and
- Metadata-row and retained-text work evidence.

The correspondence is constructed only by this owner. Consumers select types
and members from its surface rather than supplying an independently acquired
surface and reconstructing association from assembly names.

## Completion and failure

The closed result distinguishes:

- **completed** — the whole requested surface fit the declared bounds;
- **incomplete** — Metadata named the extraction bound that prevented a whole
  surface;
- **rejected** — the lease names another Library or the bytes disagree with
  the Library's managed assembly identity; and
- **failed** — the content is not a supported managed assembly, its Metadata is
  malformed, or its module identity is empty.

Cancellation propagates as cancellation before borrowing, before Metadata
extraction, or after extraction. Unexpected implementation failures remain
exceptions rather than becoming success-shaped or generic failed results.

## Lifetime and identity

The caller keeps ownership of the operation lease. Synchronous execution opens
and closes one Library snapshot; no span, PE reader, Metadata reader, callback,
stream, or release obligation escapes it.

The request, correspondence, surface, and every terminal outcome are
resource-free. A completed correspondence may outlive the Library owner. It is
evidence about the exact content that was inspected, not authority to reopen
it.

Correspondence uses reference identity for the realized Library, content,
Artifact generation, Artifact, and surface instance. Managed assembly
equivalence remains a validation fact and never substitutes for those exact
identities.

## Security and platform compatibility

Assembly bytes may originate from untrusted internet content. Metadata
admission and bounded extraction run before a result is published. Malformed
or unsupported Metadata remains visible.

The operation is host-neutral, pathless, SRM-only, Roslyn-free, and does not
load or execute the inspected assembly. Its synchronous snapshot and detached
result preserve NativeAOT and single-threaded Browser/Wasm compatibility.

## Pathological demonstration

The contract-defining gate registers the same real
`System.Text.Json` 10.0.0 assembly bytes as two distinct Artifacts, then
realizes two distinct Libraries with equivalent managed assembly identity.

Inspection of each Library must issue a different correspondence:

- each names its own exact Library and API content reference;
- each API content reference names its own Artifact identity;
- each retains the non-empty module-version identity extracted during its
  borrow;
- neither surface instance is reused; and
- equivalent assembly identity does not collapse the two realizations.

Neighboring gates cover real successful extraction, a foreign Library lease,
assembly-identity mismatch, extraction-bound exhaustion, malformed Metadata,
cancellation, and resource-free closure.

## Production adoption

This focused owner is the prerequisite for DocumentationHouse project 6:

1. this issue locks and implements Library-bound Metadata correspondence;
2. PR #7201 adopts it so a documentation subject no longer accepts an unbound
   `ApiSurface`; and
3. #6579 carries the same exact subject through its already-planned package,
   direct-library, platform, Queries, CLI, and Browser/Wasm slices.

There is no alternate architecture to retire. Rendering is not applicable:
the result is typed producer evidence and presentation remains with downstream
owners.

## Non-claims

This owner does not define:

- Library realization, content roles, borrowing, or retirement;
- Metadata grammar, API identity, extraction, or bounds;
- package, Platform, direct-library, or Workspace selection;
- Metadata forwarding or runtime-definition equivalence;
- DocumentationHouse subjects, channels, settlement, or receipts;
- serialization, rendering, paths, source acquisition, or host policy; or
- defenses against misuse by trusted in-process callers outside the
  owner-issued construction path.

## Evidence

The Release `DotnetInspector.LibraryMetadata.Tests` gate verifies:

- real bounded `System.Text.Json` extraction issues exact detached
  correspondence;
- equal-identity Libraries backed by different Artifacts remain distinct;
- a foreign Library lease is rejected before content access;
- managed assembly identity mismatch is rejected;
- extraction-bound exhaustion is incomplete rather than a shortened surface;
- malformed Metadata and cancellation remain visible; and
- the correspondence has no public constructor or resource obligation.
