# ReadyToRun image projection

## Status and owner

This document is the focused owner for discovering and describing the
ReadyToRun (R2R) envelope of one Portable Executable image. It is tracked by
[#5835](https://github.com/richlander/dotnet-inspect/issues/5835).

The implementation belongs in `ILInspector.Metadata`. It consumes an
already-open `PEReader` and returns immutable, presentation-free facts. It does
not choose an artifact, acquire content, load inspected code, decode native
methods, or render a host surface.

The contract is enforced in Release by
`ILInspector.Metadata.Tests.ReadyToRunImageInspectorTests`. The later metadata,
CLI, and Browser/Wasm adoptions remain tracked by #5835.

## Claim

Given one supported PE image, the projection returns:

- no R2R result when the image neither exports the canonical `RTR_HEADER`
  symbol nor carries a managed-native directory containing an R2R signature
  and complete fixed header;
- one R2R overview with standalone, composite-container, composite-component,
  or ambiguous role evidence; or
- a visible malformed-input failure when an advertisement, export lookup,
  header, section directory, or section extent cannot be validated within the
  projection's bounds.

An available overview preserves the discovery evidence, exact header location,
signature, version, flags, and every section entry in numeric type order.
Unknown versions, flag bits, and section types remain visible as raw values.
The projection identifies the `ManifestMetadata` section when present, but does
not decode its ECMA-335 payload. It also records whether that extent is exactly
the containing image's CLI metadata directory, because composite containers
commonly give the same bytes both identities.

## Why this is a separate owner

[Raw metadata-table projection](metadata-table-projection.md) owns the logical
ECMA-335 table and heap graph exposed by `System.Reflection.Metadata`. It
explicitly assigns PE/COFF and R2R facts to a sibling projection. R2R discovery
starts in the PE envelope, and its sections contain native-runtime structures
that are not ECMA-335 tables or streams.

The existing `AssemblyInspector` boolean recognizes a non-empty
managed-native-header directory. That is useful summary evidence, but it does
not retain the advertisement, distinguish composite discovery, validate the
header, enumerate sections, or identify the R2R manifest metadata extent.

This owner is not:

- a generic PE export browser;
- a ReadyToRun execution-compatibility checker;
- a native code, fixup, import-cell, GC-info, or runtime-function decoder;
- an ECMA-335 metadata-root projection; or
- a host command, section, renderer, browser DTO, or interaction.

## Consumers and adoption plan

The production consumers are the CLI and Inspect Web Browser/Wasm hosts. The
end-to-end tracker is #5835, whose current delivery plan has four ordered
slices:

1. **R2R image projection:** this owner and its `ILInspector.Metadata`
   implementation discover and validate the header and section directory,
   including standalone, composite-container, and composite-component roles.
2. **Manifest metadata-root adoption:** the raw metadata projection consumes
   the validated `ManifestMetadata` extent and gives it explicit R2R manifest
   provenance. When that extent aliases the CLI metadata directory, the
   projection reconciles both identities instead of manufacturing a second
   root.
3. **CLI adoption:** the library metadata lens exposes R2R image facts and an
   explicit manifest-root selection through Markout-backed output.
4. **Browser/Wasm adoption:** the managed facade carries the same typed facts,
   and Package Metadata and Metadata Explorer expose the R2R overview and
   manifest root without parsing PE bytes in TypeScript.

This document owns only step 1. Steps 2-4 are separate owner adoptions and may
land as later stack slices. The shared projection contains no CLI, Markout,
JSON, JavaScript, DOM, worker, or callback type.

## Format basis

The normative external format is the `dotnet/runtime` ReadyToRun definition.
The references below are pinned to runtime commit
[`de29e26a`](https://github.com/dotnet/runtime/commit/de29e26a4f69da5d446900a6ba9d68ba3ddc5ba9):

- [`readytorun.h`](https://github.com/dotnet/runtime/blob/de29e26a4f69da5d446900a6ba9d68ba3ddc5ba9/src/coreclr/inc/readytorun.h#L15-L155)
  defines the signature, header, flags, section entry, and section type values.
- [`PEDecoder::FindReadyToRunHeader`](https://github.com/dotnet/runtime/blob/de29e26a4f69da5d446900a6ba9d68ba3ddc5ba9/src/coreclr/utilcode/pedecoder.cpp#L1965-L1989)
  defines standalone managed-native discovery.
- [`OpenR2RFromPE`](https://github.com/dotnet/runtime/blob/de29e26a4f69da5d446900a6ba9d68ba3ddc5ba9/src/coreclr/vm/nativeimage.cpp#L194-L224)
  resolves the canonical export for PE composite images.
- [`PEReaderExtensions.GetExportAddress`](https://github.com/dotnet/runtime/blob/de29e26a4f69da5d446900a6ba9d68ba3ddc5ba9/src/coreclr/tools/aot/ILCompiler.Reflection.ReadyToRun/PEReaderExtensions.cs#L45-L97)
  is the managed export-table analogue.
- [`ReadyToRunHeaderNode`](https://github.com/dotnet/runtime/blob/de29e26a4f69da5d446900a6ba9d68ba3ddc5ba9/src/coreclr/tools/aot/ILCompiler.ReadyToRun/Compiler/DependencyAnalysis/ReadyToRun/ReadyToRunHeaderNode.cs#L143-L233)
  shows sorted section emission and the bytes at the exported header symbol.
- [`ReadyToRunReader`](https://github.com/dotnet/runtime/blob/de29e26a4f69da5d446900a6ba9d68ba3ddc5ba9/src/coreclr/tools/aot/ILCompiler.Reflection.ReadyToRun/ReadyToRunReader.cs#L768-L788)
  opens `ManifestMetadata` as one exact metadata-image slice.

These implementations are evidence, not imported architecture. The product
path remains SRM-only, NativeAOT-friendly, and independent of runtime tool
assemblies.

## Discovery

### Advertisements

The projection examines both canonical PE discovery paths:

1. the CLI header's `ManagedNativeHeaderDirectory`; and
2. the exact PE export name `RTR_HEADER`.

The CLI directory is absent only when both its RVA and size are zero. A
half-empty directory is malformed. The slot is generic: legacy native-image
formats also used it. It becomes an R2R advertisement only when its size can
hold the fixed header and its first four bytes are the R2R signature. A
well-bounded directory with another signature produces no R2R advertisement;
the sibling metadata-image overview still preserves the generic managed-native
directory fact.

Discovery reads the four-byte signature before validating the complete declared
extent. Once those bytes establish an R2R advertisement, the complete directory
must be raw-backed. If the RVA does not map even the signature bytes, discovery
fails visibly because the projection cannot establish whether the generic slot
contains R2R.

Single-file R2R images conventionally carry `CorFlags.ILLibrary`, but the
runtime's discovery routine does not require that bit. The projection therefore
reports the CLI flags through existing PE-header facts without adding a
stricter-than-runtime rejection.

The export lookup follows the PE names, name-ordinal, and export-address tables.
The selected export-address-table value is the R2R header RVA itself. The
projection does not dereference another RVA from the target bytes. A forwarded
`RTR_HEADER` export is malformed.

Crossgen2 can emit a non-canonical symbol name when explicitly configured.
Automatic discovery recognizes only `RTR_HEADER`; alternate names are outside
this contract.

### Discovery agreement

Current PE composite containers may populate both discovery paths:

| Managed-native directory | `RTR_HEADER` export | Result |
| --- | --- | --- |
| absent | absent | no R2R result |
| non-R2R native header | absent | no R2R result; generic directory remains visible elsewhere |
| R2R header | absent | R2R overview with managed-native discovery |
| absent or non-R2R | valid | R2R overview with export discovery |
| R2R header | valid, same RVA | R2R overview with both discovery facts |
| R2R header | valid, different RVA | malformed |

Current runtime definitions assign `0x0002` to `SkipTypeValidation`; older prose
that describes that bit as `Composite` is stale. That does not preclude role
classification from current evidence:

- `Component` flag `0x0020` or `OwnerCompositeExecutable` section 116 identifies
  a component assembly whose native code belongs to a composite;
- the canonical header export or `ComponentAssemblies` section 115 identifies a
  composite container; and
- an R2R header with neither kind of evidence is standalone.

Both kinds of role evidence remain independently meaningful. If an unusual
header carries both component and composite-container evidence, its role is
reported as ambiguous rather than turning otherwise valid structural facts into
an execution-compatibility judgment. Discovery facts and role are separate: a
component normally uses only the managed-native path, while a current PE
composite container commonly uses both paths.

An actual R2R advertisement is never treated as absence after a decode failure.
If a non-empty export directory cannot be bounded well enough to establish
whether `RTR_HEADER` is present, discovery itself fails visibly. Later host and
query adoptions must isolate that typed R2R inspection failure rather than
turning an otherwise inspectable artifact into empty success or terminating an
unrelated section.

## Header and section contract

The header is little-endian:

| Offset | Size | Field |
| ---: | ---: | --- |
| 0 | 4 | signature |
| 4 | 2 | major version |
| 6 | 2 | minor version |
| 8 | 4 | flags |
| 12 | 4 | section count |
| 16 | `12 * count` | section directory |

The signature must be `0x00525452` (`RTR\0`). Each section entry contains a
32-bit type, RVA, and size.

The projection computes the encoded header size with checked arithmetic. For a
CLI advertisement, that size must fit in the advertised directory and the
complete advertised directory must map to raw image bytes. For an export-only
advertisement, the encoded header must fit in the raw section containing the
export target.

Versions are reported, not judged against the current runtime's execution
window. R2R versions change faster than this inspection contract, while the
header and section-entry envelope has remained stable. An unknown version is
therefore visible rather than mislabeled as malformed.

Known flag names are additive display information over the raw 32-bit value.
Unknown bits are retained. The initial known set is:

```text
0x0001 PlatformNeutralSource
0x0002 SkipTypeValidation
0x0004 Partial
0x0008 NonSharedPInvokeStubs
0x0010 EmbeddedMSIL
0x0020 Component
0x0040 MultiModuleVersionBubble
0x0080 UnrelatedR2RCode
0x0100 PlatformNativeImage
0x0200 StrippedILBodies
0x0400 StrippedInliningInfo
0x0800 StrippedDebugInfo
```

The runtime format requires section types to be sorted. This projection adopts
the deliberately stricter inspection policy that they must be strictly
increasing: section type is the typed identity used by consumers, and retaining
duplicates would make a lookup such as `ManifestMetadata` ambiguous. Duplicate
or descending entries are therefore malformed; unknown increasing values are
preserved. The stricter rule is supported by all 193 measured framework R2R
images and both measured composite outputs.

A non-empty section must map completely to raw image bytes with checked
`RVA + Size` arithmetic. A zero-size section remains visible and imposes no
payload read, alignment, or RVA-mapping claim. Its retained RVA is unvalidated
and consumers must not dereference it.

The projection does not impose a universal payload-alignment rule. Crossgen2
aligns the header itself to pointer size, while individual section payloads
have their own formats and requirements.

## Bounds

The projection must reject before allocation or unbounded traversal when:

- the R2R section count exceeds 4,096;
- the PE export name count exceeds 65,536;
- a count-to-byte-size multiplication or RVA addition overflows;
- the export directory, complete name-pointer table, complete name-ordinal
  table, selected function entry, name bytes needed for comparison, header,
  advertised managed-native directory, or non-empty section extent is not fully
  backed by raw image bytes; or
- an exact export name cannot be read as `RTR_HEADER` followed by a null byte.

The export-name scan reads only the fixed target length needed for an exact
comparison. It does not materialize arbitrary export names or retain an export
catalog. It does not allocate or validate the complete export address table;
only the selected named export's ordinal and function entry are bounded.

These bounds are inspection policy, not claims about the maximum legal PE or
runtime format. Exceeding one is a visible `BadImageFormatException`, matching
the Metadata layer's existing direct-inspector behavior for unsupported
structural work.

## Manifest metadata boundary

`ReadyToRunSectionType.ManifestMetadata` has numeric value 112. Its bytes are a
complete ECMA-335 metadata root beginning with `BSJB`; they are not a PE, CLI
header, pointer, or length-prefixed wrapper.

This owner identifies and validates the section extent only. It also compares
that exact RVA and size with the CLI metadata directory and reports whether the
two identities alias. It does not open a `MetadataReader`, classify the root,
project tables or heaps, or assign assembly identity. The step-2 metadata
adoption must consume exactly the owner-issued extent, mark it as R2R manifest
metadata, and preserve an existing primary CLI identity when both names refer
to the same bytes.

The section is independent of composite classification. It may appear in
standalone, composite-container, or component R2R images, and its absence is a
valid reported fact.

## Failure and trust boundary

The actor is an untrusted package or explicitly supplied PE file. Its bytes
reach the projection through an already-open `PEReader`. The containment
invariant is that no file-controlled count, RVA, size, ordinal, or name pointer
causes an unchecked arithmetic operation, allocation above policy, or read
outside raw image bytes.

No broad catch converts a malformed advertisement into no R2R result. Expected
format and budget failures use `BadImageFormatException`; unexpected
exceptions propagate. Hosts and query owners may later map that exception to
their typed failure shapes, but may not replace it with empty success.

The projection uses explicit little-endian SRM reads. It does not cast image
bytes to native structs, memory-map files, invoke native symbol lookup, load an
inspected assembly, or depend on Roslyn.

## Evidence

### Runtime corpus

A local read-only sweep of official installed shared frameworks established the
standalone and component shapes before design:

| Runtime | DLLs | Managed-native R2R | No R2R advertisement | With `ManifestMetadata` | Header version |
| --- | ---: | ---: | ---: | ---: | --- |
| Microsoft.NETCore.App 10.0.10 | 172 | 92 | 80 | 92 | 16.0 |
| Microsoft.NETCore.App 11.0.0-preview.7.26381.103 | 181 | 101 | 80 | 101 | 25.0 |

All 193 observed standalone headers used strictly increasing unique section
types; the largest observed directory had 17 sections. A composite publish also
produced managed-native-only component assemblies with flag `Component` and
section 116 pointing to their owner composite. This is design evidence, not an
enforcing gate.

`dotnet publish` produced one .NET 10 and one .NET 11 composite PE from the same
minimal program. Both exported `RTR_HEADER`, populated the CLI
managed-native-header directory with the same RVA, and carried section 112.
In both, section 112 exactly aliased the CLI metadata directory. The .NET 10
header was version 16.0; the .NET 11 header was 25.0. This disproved three
tempting assumptions: current PE composites are not necessarily export-only,
manifest metadata is not composite-only, and manifest metadata is not always a
second physical root.

### Release gates

`ReadyToRunImageInspectorTests` proves:

- neither discovery path returns no R2R result;
- a non-R2R managed-native header remains a non-R2R result;
- the SDK-selected `System.Private.CoreLib` exercises the compiler-produced
  standalone path under the repository's exact SDK gate;
- managed-native-only, export-only, and matching dual advertisements preserve
  their distinct discovery evidence;
- component flags/owner sections and composite-container sections produce their
  distinct roles;
- conflicting component and composite-container evidence remains visible with
  an ambiguous role;
- mismatched dual advertisements fail;
- signature, count, ordering, duplicate, ordinal, forwarded-export, arithmetic,
  and raw-extent failures remain visible;
- unknown versions, flags, and increasing section types survive unchanged;
- a zero-size section remains visible without a payload read; and
- `ManifestMetadata` is identified by value 112 without being decoded by this
  owner, and exact CLI-directory aliasing is reported.

Synthetic mutations are appropriate for unreachable malformed states and
export-only isolation. The exact repository SDK makes its installed CoreLib a
stable compiler-produced standalone canary. The measured compiler-produced
composite remains reproducible design evidence rather than a checked-in
50-megabyte fixture or a crossgen2 dependency in ordinary PR CI. If a future
change needs repeated broad composite-corpus evidence, it belongs in a
purpose-built Deep Inspect probe rather than in the fast metadata suite.

## Non-claims

This contract does not claim:

- that an available R2R image can execute on the current runtime;
- that every section payload is internally valid;
- support for Webcil, ELF, Mach-O, or native ReadyToRun container parsing;
- discovery of a custom crossgen2 header symbol;
- native method entry points, code ranges, runtime functions, imports, fixups,
  exception data, debug data, profile data, or GC information;
- decoding or semantic validation of `ManifestMetadata`; or
- any CLI or browser output before the corresponding adoption slice lands.
