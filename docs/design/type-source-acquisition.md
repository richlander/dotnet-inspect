# Shared type source acquisition

## Owner and claim

`DotnetInspector.Queries` owns the retained-type authored acquisition handoff.
The focused delivery is tracked by #7522.

> Settle authored source for one independently resolved retained type through
> SourceHouse, retain the primary-document mapping and any partiality together
> with symbols needed by fallback, and publish only after owned cleanup and
> query-currency checks complete.

This adopts the existing SourceHouse type target; it does not extend the
House's supported source-policy matrix. SourceLink owns mapping and checksum
semantics, the Library adapter owns retained-content admission, and Library
and Artifact own resource settlement. The existing
[ordinary Source policy](source-finding-producers.md#consumer-boundaries)
still prefers verified authored source and otherwise uses
`CSharpDecompilerService`. Type source is not a member declaration or a promise
that all parts of a partial type are contained in one document.

## Acquisition and publication

Type and [member acquisition](member-source-acquisition.md) share the same
internal Library/House lifetime handoff rather than duplicate acquisition,
capabilities, or retirement. One upstream Portable PDB acquisition supplies
authored settlement and optional decompilation. External companions retain
independent provenance; embedded symbols remain embedded for Library admission.

The acquired SourceLink reader closes before Library admission. SourceHouse
consumes its operation lease; the query then retires the Library before its
Artifact session. Cleanup failure prevents publication. Caller cancellation
and binding-policy invalidation retain their existing terminal precedence.
The borrowed assembly-context group remains caller-owned.

Published results retain native House or terminal Library-admission evidence.
Fallback does not erase an unsuccessful authored attempt. The projection
preserves the selected type's primary document, mapping strength, partiality,
and additional-document references. The native mapping's resolved type and
homogeneous document collection, including each browse URL, resolution method,
and checksum facts, survive unchanged;
the query does not reconstruct them from paths. It does not fabricate a
complete type declaration from a document.

`TypeSourceLimits` and `TypeSourceTimeout` bound authored settlement independently
from the existing member and member-pair settings. Defaults are the same finite
authored bounds: 512 MiB per retained assembly/PDB, three source candidate
categories, 64 MiB source bytes/characters, finite target/mapping admission, and
five minutes after upstream PDB acquisition. These are not upstream transport,
process-memory, or decompiler bounds.

## Production adoption and retirement

`TypeSourceInspection.ExecuteAsync` is the completed host-neutral facade,
returning `InspectionEnvelope<AssemblyTypeSourceEntry>` with explicit
non-projectable Share. Browser Type Source consumes its content through the
existing browser projection and operation/cancellation bridge. No wire shape,
new source mode, viewer, or rendering substrate is introduced.

This is the sixth delivery in the adapter-first path:
PRs #7313, #7368, #7440, #7449, #7502, then retained-type acquisition.
It retires `AssemblyContextSourceQuery`'s type-side
`PdbSourceHouse.AcquireTypeAsync` composition, not that public legacy API's
remaining callers. Member and pair acquisition retain their current policies.

The overall twelve-step plan in [SourceHouse](source-house.md#production-adoption)
and #6512 still includes both CLI and Browser/Wasm adoption. This slice adopts
the existing browser type-query consumer. The CLI's broader source enrichment
uses a different contract and remains a separately reviewed delivery under
step 12; that delivery must consume shared completed inspection operations
rather than compose the House in the host. It must preserve the CLI's explicit
authored/decompiled selections and multi-document enrichment before retiring
the remaining direct path. No CLI type-source cutover or full legacy retirement
is claimed by this slice.

## Evidence

The real motivating asset is dotnet-inspect at commit
`fe85fb093d46cc9ec2215b2019839ce5a750d2d6`: its compiled
`CSharpText.MemberSlicing` assembly, matching Portable PDB, and
`src/CSharpText.MemberSlicing/MemberTextSlicer.cs` source.
Existing compiler-produced source fixtures supply
deterministic authored, missing-source, checksum, partial-document, and
decompiler neighbors. The analogous implementation is the landed member
acquisition handoff; no third-party code or new acquisition algorithm is used.

The following PR-fast Release gates define the delivery:

| Gate | Claim |
| --- | --- |
| `AssemblyContextSourceQueryTests`, including `TypeSourceInspection_*` | Authored preference, native House/Library evidence, symbols retained for fallback, independent finite bounds, type-document scope, and existing cancellation/currency/disposal behavior. |
| `BrowserSourceComparisonOperationTests.TypeSourceEnvelope_PreservesBrowserPreferenceAndFallback` | The production browser projection preserves authored source, missing-source/deadline fallback, provenance, and visible limitations. |
| `BrowserTypeSourceOperationTests` | The existing keyed operation, cancellation, expected failure, and scope-release contract remains intact. |
| Published `source-comparison-production.spec.ts` fixture scenario | Generated `queryTypeSource` consumes product-discovered type identity and returns authored source or visible decompiler fallback; member and pair neighbors remain intact. |

Use the existing published source-comparison gate after publishing Inspect Web.
Only package/source transport is substituted; the harness does not construct
or repair product source results. Normal CI supplies native Browser/Wasm build
evidence. These gates establish the stated behavior only when they pass.
