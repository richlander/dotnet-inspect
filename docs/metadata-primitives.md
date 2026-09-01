# Shared metadata primitives

> **Map:** [Type, member, and API representation](design/type-member-api-representation.md)
> is the entry point for choosing a type, member, or API identity shape. This
> document owns the mechanical SRM boundary below those shapes.

## Decision summary

The June 2026 decision to stop after the first three MetadataPrimitives
migration steps is superseded.

Resume consolidation in `ILInspector.MetadataPrimitives`, but consolidate
**mechanics, not semantic models**:

- MetadataPrimitives owns bounded SRM traversal, signature-decode admission,
  neutral name segments and method coordinates, neutral structural keys, work
  budgets, and typed mechanical rejection.
- A lossless raw-row reader is allowed only for a named table where public SRM
  APIs discard required evidence or allocate it before product charging. The
  first and only registered exception is `MethodSemantics`.
- MetadataPrimitives owns the reader-independent assembly-format classifier
  used before product metadata work. Its separately registered, fixed-prefix
  metadata-root admission guard is not a raw-table decoder. Windows Metadata
  (`WindowsMetadata` and `ManagedWindowsMetadata`) is outside project scope,
  not another semantic model this layer must normalize.
- Metadata, Analysis, Decompiler, Instructions, and ILDiff retain their own
  semantic models, signature providers, projections, and failure policy.
- Analysis and Decompiler keep separate `TypeRef` types. They answer different
  questions and have continued to diverge in useful, owner-specific ways.
- Analysis and Decompiler should replace their local TypeSpec recursion and
  byte accounting with the shared `TypeSpecGuard` while preserving their
  current 1,024-byte admission limit and rejection projection.
- Provider-facing wrappers remain local when they turn the same mechanical
  rejection into different owner-specific outcomes.
- Existing forwarded public identities in the `ILInspector.Metadata` namespace
  remain unchanged in the first slice. A source, test, and local-build corpus
  census should expose which names are pinned by repository behavior and
  require new neutral currencies to use `ILInspector.MetadataPrimitives`.
  Published-package corpus snapshots retain their historical identities until
  their separately reviewed package pin moves.

This document records the decision only. It does not authorize combining the
implementation slices or changing failure behavior without the focused
evidence described below.

## Why the previous stop decision expired

The old decision tested the proposed adoption against its first real Analysis
consumer. It correctly concluded that Analysis needed a semantic `TypeRef`, not
Metadata's display-oriented decoder, and that sharing the remaining
attribute-name walk would delete only about 15 stable lines. That
coupling-to-payoff result still holds.

What expired is the assumption that steps 4 and 5 were one all-or-nothing
choice. Analysis and Decompiler have since adopted several neutral mechanics,
and each now contains both the shared TypeSpec guard and an older local
TypeSpec policy. The new evidence is a concrete policy split below the semantic
models, not a reason to revisit the models themselves.

At `27f830dfb`:

- Analysis has six project references, including direct references to both
  Metadata and MetadataPrimitives.
- Decompiler directly references Metadata, MetadataPrimitives, Instructions,
  ControlFlow, CSharp, Findings, ILDiff, Text, and CSharpText.
- Analysis and Decompiler already consume MetadataPrimitives-owned
  `MetadataRelationshipTraversal`, `SignatureBlobGuard`, and related rejection
  types. `StructuralCloneAnalysis` and `IrImporter` also call
  `TypeSpecGuard.TryEnter` directly.
- Both separately consume Metadata-owned `MetadataTypeDefinitionName` and
  `AssemblyReferenceIdentity`; those are product identity currencies, not
  evidence of MetadataPrimitives adoption.
- ILDiff is another direct MetadataPrimitives consumer for
  `MethodStructuralSignature` keys and bounded metadata mechanics.
- Nineteen product `ISignatureTypeProvider<,>` implementations exist across
  the repository. The number is not itself a defect: the SRM provider pattern
  is how each owner projects one signature walk into its own result.

The question is therefore no longer whether a shared dependency is worth
introducing. The dependency and partial adoption already exist. The current
question is which remaining mechanics have one repository-wide answer and
which differences are intentional policy.

## Current boundary

`ILInspector.MetadataPrimitives` is currently an SRM-only leaf with no project
references. `LayeringTests.MetadataPrimitives_RemainsLeaf` in
`src/dotnet-inspect.Tests` gates that property.

```text
                     ILInspector.MetadataPrimitives
             (bounded SRM mechanics and neutral currencies)
                  /          |          |          \
          Metadata       Analysis   Decompiler   Instructions
                                                     |
                                                   ILDiff
```

The diagram shows ownership, not every direct project edge. ILDiff also
references MetadataPrimitives directly.

### Shared mechanical ownership

| Concern | MetadataPrimitives owns |
| --- | --- |
| Metadata relationships | Bounded TypeDef, TypeRef, ExportedType, and related handle walks; typed rejection |
| Signature admission | Structural blob prescan and cross-TypeSpec depth/byte budgets |
| Bounded name traversal | Root-to-leaf handle/segment walks, `MaxTypeNameCharacters`, and typed rejection; the assembled definition-name currency stays in Metadata |
| Method coordinates | `MetadataMethodAddress` and other neutral coordinates declared by this owner |
| Neutral structural identity | Bounded keys used for matching without display policy |
| Work budgets | Limits and typed exhaustion/rejection shared across consumers |
| Generic metadata context | Bounded generic parameter names and constraint flags |
| Neutral matching | Dependency-free name distance and similarity |
| Lossless `MethodSemantics` rows | Bounded mechanical decode of raw semantics, MethodDef, and HasSemantics columns where SRM exposes no lossless row API |

The shared member-anchor work ceiling is mechanical, but
`ILInspector.Metadata.ApiMemberIdentity` decides which semantic projection work
draws from it. Its cumulative overload charges the complete anchor projection
against one caller-owned counter rather than allowing repeated MethodDefs to
restart the limit. MetadataPrimitives does not construct the canonical
signature, selector, or fingerprint.

Metadata retains product-facing definition identities, including
`MetadataTypeDefinitionName` and `MetadataTypeDefinitionAddress`. Moving those
currencies is not part of this decision. That defining-assembly ownership is
**not gated**; the first implementation slice must add
`MetadataPrimitiveOwnershipTests.MetadataDefinitionCurrencies_RemainMetadataOwned`,
covering both types and the absence of a primitives-owned proxy or forwarder.

These mechanisms may expose handles, neutral values, typed results, or
disposable admission scopes. They must not select a consumer's display,
fallback, trust, correlation, or code-generation policy.

### Consumer ownership

| Owner | Retains |
| --- | --- |
| Metadata | API models, declaration identities, degraded metadata facts, and API/display projections |
| Analysis | Evidence `TypeRef`, trust evidence, call/member matching, catalog correspondence, and incomplete-analysis policy |
| Decompiler | Pipeline `TypeRef`, code-generation facts, custom modifiers, function-pointer spelling inputs, and fidelity policy |
| Instructions | Decode, stack-shape, and instruction-substrate projections |
| ILDiff | Canonical IL operands, member/body alignment, diff failures, Findings, and presentation |

Consumer-owned providers should call shared admission and traversal mechanics,
then construct their native result. A neutral primitive must not return a
plausible `object`, an empty signature, or a display string on rejection unless
that value is itself an explicit typed result arm.

### Supported assembly metadata format

`MetadataImageFormatClassifier` is the sole mechanical format gate for product
assembly metadata. It accepts the acquisition-owned `PEReader`, obtains that
owner's metadata block, and uses one bounded `BlobReader` to inspect only the
ECMA-335 root signature, fixed major/minor/reserved fields, signed version
length, and at most the declared 256-byte padded version field. ECMA-335 limits
the null-terminated version to 255 bytes and rounds the stored field length to
four-byte alignment. The classifier scans those bytes only through the first
null for the exact ordinal ASCII sequence
`WindowsRuntime`. Finding it produces typed `UnsupportedWindowsMetadata`;
absence produces `SupportedEcma335`. This is the same case-sensitive version
discriminator SRM consults before applying optional WinRT projections, without
constructing a `MetadataReader` whose table initialization may scan rows.

`PEReader.HasMetadata == false` produces typed `NoMetadata` without requesting
a metadata block. An unmappable metadata directory, block shorter than the
fixed root prefix, invalid signature, negative or over-256 padded length, or
length beyond the metadata block produces a typed malformed-root result. An
I/O failure while a lazy owner materializes the block remains its acquisition
failure rather than malformed metadata. SRM may accept a longer field when
enough bytes remain; the guard deliberately rejects it because that field is
outside the ECMA-335 bound and could carry an unexamined marker beyond the
fixed admission window.

The classifier does not decode or expose the version string, inspect stream
headers, heaps, table headers, row counts, or rows, construct any
`MetadataReader`, search for mscorlib, or create projected/raw handle
correspondence. It retains no reader, block, pointer, handle, or mutable state.
Supported images then use the ordinary SRM reader for all remaining root,
stream, heap, and table validation. Obtaining the block may materialize the
complete metadata directory for a lazy `PEReader`; that acquisition-owner cost
is visible and measured separately. Once the block is available, classifier
work and allocation are fixed by the root prefix and 256-byte ceiling and do
not scale with stream, heap, table, or row content.
An acquisition owner that relies on the classifier's typed mapping constructs
the assembly reader lazily rather than requesting
`PEStreamOptions.PrefetchMetadata`, because constructor-time metadata
materialization would surface a raw `BadImageFormatException` before admission
can classify an unmappable directory.
Acquisition or direct projection APIs whose established return shape has no
failure arm throw `UnsupportedMetadataFormatException` carrying no artifact
text for unsupported Windows Metadata and
`MalformedMetadataRootException : BadImageFormatException` with the same text
constraint for a malformed-root result. Typed query owners catch and preserve
those distinct mechanisms as unsupported-input and malformed-input results.
They must not translate either to `null`, an empty projection, or partial rows.
An admission owner that rejects an image disposes every reader and stream it
has not transferred, but a cleanup failure must not replace the admission
failure or turn a typed rejection into degraded success. When an owner retains
separate reader and stream handles, it leaves the stream open in the reader and
disposes each handle exactly once.
Workspace realization uses
`WorkspaceContextLoadFailureKind.UnsupportedMetadataFormat` consistently for
package, platform, and embedded members; grouped package preflight retains the
same unsupported-format reason instead of treating the image as unreadable.
Dependency snapshots use the same Metadata-owned admission helper before
identity decoding. Multi-library package commands scope unsupported and
malformed metadata to the rejected participant, emit a bounded failure, and
continue rendering valid neighboring assemblies. Package `type`, `member`, and
`depends` probes retain typed per-participant receipts while searching later
candidates, then render bounded warnings beside a healthy match or the direct
typed error when every selected participant is rejected. A single selected
package member retains the direct typed rejection used by single-library
inspection, including when grouped Integrations preflight discovers the
rejection.

The nullable `AssemblyDependencyResolver.Resolve`, `Acquire`, and
`AcquireTargetAssembly` compatibility entry points likewise rethrow exact
unsupported or malformed admission exceptions instead of representing them as
missing assemblies. `Resolve_FormatAdmissionFailureIsTyped`,
`Acquire_FormatAdmissionFailureIsTyped`, and
`AcquireTargetAssembly_FormatAdmissionFailureIsTyped` gate both snapshot and
live-path acquisition;
`ResolverEntryPoints_UnmappableMetadataDirectoryIsTyped` gates the
pre-admission mapping failure, while
`ResolveAndAcquire_NoMetadataRemainUnresolved` keeps the established
no-metadata nullable boundary.

`NoMetadata` preserves the acquisition or query owner's established typed
no-metadata boundary. Neither it nor a malformed-root result is translated to
`UnsupportedMetadataFormatException`.

Acquisition owners call it before exposing metadata sessions. Public or
reusable `PEReader` entry points that can bypass those owners call it directly.
Compatibility entry points that accept both a `PEReader` and a
`MetadataReader` derive the authoritative reader from the admitted PE; they do
not consult an independently supplied reader that could describe different
bytes.
The lower Instructions substrate exposes no raw `PEReader` entry point; its
internal helpers consume readers only through admitted higher-layer owners.
That closure includes `AssemblyImage`, `PdbContext`, Decompiler
`MetadataSource`, referenced-assembly context, and body production; Analysis
`LibraryBodyIndex` and its referenced-image consumers; Research and ILDiff
assembly comparison; Services platform and intrinsic-core-library probes;
TypeScript-generation acquisition; `MetadataImageInspector`; every
`MetadataTableProjector`
table/row/reference/heap operation; and the defensive
`MethodSemanticsRowReader` leaf check. `MDP017` in
[member inspection planning and Metadata
projection](design/member-inspection-planning-and-metadata-projection.md) gates
the inventory, reader independence, bounded root work, typed failure, and
no-work-before-reject properties.

The classifier's primitive-local contract is gated by
`MetadataImageFormatClassifierTests` and
`LayeringTests.MetadataPrimitives_MetadataRootClassifierIsIsolated`.
Metadata-owned session and projection adoption is separately gated by
`LayeringTests.Metadata_MetadataReadersRequireFormatAdmission` and the
admission cases in `MetadataImageFormatClassifierTests`.
`LayeringTests.Metadata_MetadataPredicatesRequireFormatAdmission` prevents a
raw `PEReader.HasMetadata` predicate from running before that admission in the
Metadata assembly.
`LayeringTests.Decompiler_MetadataSourceRequiresFormatAdmission` applies the
same compiled-IL closure to Decompiler `MetadataSource` predicates and reader
construction. The `Analysis_MetadataReadersRequireFormatAdmission` and
`Analysis_MetadataPredicatesRequireFormatAdmission` gates close the same raw
reader and predicate paths across the Analysis assembly.
`RemainingProduct_MetadataReadersRequireFormatAdmission` and
`RemainingProduct_MetadataPredicatesRequireFormatAdmission` close those paths
across Decompiler, Research, ILDiff, Queries, Services, and TypeScript
generation without treating wrapper state or portable-PDB readers as
assembly-metadata admission sites.
`Product_AssemblyReadersDoNotPrefetchMetadataBeforeAdmission` prevents
constructor-time assembly metadata materialization from bypassing the typed
classifier.
`Instructions_DoesNotExposeAssemblyImageEntryPoints` keeps the lower
Instructions layer from publishing a raw assembly-image bypass.
`MetadataAdmissionCleanupTests`,
`MetadataSourceFormatAdmissionTests`, and
`SignatureSpellabilityTests.InspectField_CleanupCannotDegradeFormatRejection`
gate cleanup precedence across the stream-backed Metadata and Decompiler
admission consumers, including no-metadata results from Metadata scanners and
descriptor-backed inspection, `AssemblyImage` disposal, constructor failures,
and prefetched-image ownership transfer. Typed snapshot,
declaration-inventory, and
structural-clone failure receipts retain the classifier's exact malformed-root
reason without changing `CandidateOpenFailure`'s two-position public record
contract.
Assembly-binding and workspace-load failures likewise retain the exact reason
in non-positional properties, while browser and command adapters include the
bounded enum reason without exposing artifact text.

A multi-candidate scan scopes each rejection to its own participant. An index
that publishes entries aliasing a reader transfers that reader's ownership
before indexing begins, so a later decode failure leaves the reader alive for
the whole walk instead of disposing one the index still references;
`MetadataAdmissionCleanupTests.ExtensionScanner_PartialIndexKeepsReaderAliveForWholeWalk`
gates that property in a child process because the regression terminates the
host. A retained candidate rejection is an established outcome, so intrinsic
core-library binding disposes without replacing it
(`MetadataFormatAdmissionTests.IntrinsicBinding_CleanupCannotReplaceRetainedCandidateFailure`).
Package type probing returns a healthy match alongside its per-participant
receipts and surfaces a typed rejection only when the scan matched nothing
(`MetadataFormatAdmissionTests.PackageTypeProbe_RejectedMemberDoesNotHideHealthyMatch`
and `PackageTypeProbe_SoleRejectedMemberSurfacesTypedFailure`). `depends`
applies the same rule across its selected assemblies rather than aborting the
scan: `CommandExecutionTests.DependsTypeProbe_RejectedLibraryDoesNotHideHealthyNeighbor`
gates the scoped scan, and `DependsTypeProbe_SoleRejectedSelectionUsesBoundedError`
gates the bounded typed error when the single selected target framework is
rejected. A participant that passes admission but whose metadata does not
decode is an ordinary invalid-image outcome rather than an admission failure,
and it stays visible on both sides of the same rule: it is recorded as a
per-participant receipt beside surviving neighbours
(`MetadataAdmissionCleanupTests.DependencyScan_InvalidImageDoesNotHideHealthyNeighbor`)
and remains the caller's exact outcome, rather than degrading into "type not
found", when no participant survives
(`MetadataAdmissionCleanupTests.DependencyScan_SoleInvalidImageStaysExact`).
Frozen `TypeResolutionContext` binding outcomes construct their public
`AssemblyBindingFailure` from the retained `CandidateOpenFailure`; selected,
multi-candidate, and requesting-origin failures therefore keep the candidate
kind and malformed-root reason after the discovery builder is discarded.
When every candidate fails, a resource-budget failure retains its established
precedence; otherwise a typed unsupported-format or malformed-root failure
outranks an earlier generic unreadable, no-metadata, or invalid-image failure
so candidate order cannot erase the admission receipt. Requesting-origin
binding projection consults the retained registration failure directly, so a
resolution-specific budget result does not erase its
`CandidateOpenFailureKind.ResourceBudget` binding receipt. Intrinsic
core-library facade selection applies the same ranking rather than choosing
the first unsuccessful facade.
Post-admission SRM validation failures such as an overflowing metadata stream
count remain ordinary invalid-image outcomes: package role realization retains
the rejected participant, declaration inventory and Corpus return typed
failures, path and assembly-set surface classification preserve healthy
neighbors, Research API comparison records the failed participant without
retrying it as a module, and TypeScript commands emit bounded diagnostics
rather than an unhandled exception. Direct `AssemblyReader` projections return
their established no-result outcome, `PdbContext` rejects before publishing an
invalid context, and the `mdi` metadata lens emits its bounded read diagnostic.
The defensive `MethodSemanticsRowReader` leaf maps the same SRM construction
failure to `MetadataReaderRejected`. Designated-overlay candidate aggregation
retains the deterministic first equal-precedence typed failure and its exact
malformed-root reason.
Platform type lookup appends distinct no-metadata, unsupported-format, and
malformed-root failure kinds and carries the exact malformed reason
non-positionally. Per-catalog and cross-framework aggregation prefer those
typed receipts over generic catalog failures without changing the existing
numeric values or positional record shape.
`MetadataFormatAdmissionTests`,
`CallerScopeReachabilityPlanTests.Candidate_PreservesUnmappableMetadataDirectory`,
and `AnalysisIndexCacheAdmissionTests` gate Analysis and Research propagation,
including lazy admission before metadata-directory materialization.
`IlAssemblyDiffTests.CompareStreams_RejectsWindowsMetadata`,
`IlAssemblyDiffTests.ReaderTakingOverloads_RejectWindowsMetadata`,
`IlAssemblyDiffTests.ReaderTakingOverloads_UseAdmittedImageReaders`, and the
Services `MetadataFormatAdmissionTests` gate ILDiff and Services propagation
and reader/image association. Services
`SelectAndResolve_MalformedDesignatedMetadataCannotFallBackToPlatform`,
workspace malformed-asset tests, browser
`MetadataProjection_PreservesFormatRejection`, and the mixed-package command
tests gate exact malformed-root reason retention through their adapters.
The `TypeResolutionContextTests` malformed candidate, origin, and ambiguous
binding cases gate frozen binding retention.
`PackageIntegrationsWorkspaceTests.MalformedMetadataPreflight_PreservesGroupedReason`
gates grouped multi-library Integration preflight projection through the same
bounded format-failure diagnostic used by ordinary library inspection.
`TypeScriptFacadeEmitterTests.SurfaceLoader_PreservesMalformedMetadataRoot`
gates TypeScript-generation propagation; the malformed-root command tests in
`TsJsExportCommandTests` gate bounded diagnostics and non-zero exits.
`CorpusTests.Searches_preserve_typed_metadata_admission_failures` gates typed
per-member no-metadata, unsupported-format, malformed-root, and invalid-image
receipts for both Corpus search operations while retaining
`SkippedAssemblies` as the path-only compatibility projection.
Browser projection
preservation is gated by
`BrowserMetadataOperationsTests.MetadataProjection_PreservesFormatRejection`.
These focused gates do not close `MDP017`'s separately planned cache,
PDB-retention, or cross-owner adoption.

### Lossless `MethodSemantics` row boundary

`MethodSemanticsRowReader` is the sole registered exception to the normal rule
that product code uses SRM row accessors rather than decoding table rows. It
exists because SRM exposes table location and shape but no public lossless
`MethodSemantics` row API: its property/event convenience accessors allocate
all `Other` rows, overwrite duplicate standard roles, and omit unrecognized
combined role values.

The reader accepts the acquisition-owned `PEReader` and a
MetadataPrimitives-owned `MethodSemanticsReadBudget` that bounds retained
associations. Metadata creates that neutral budget only after
`MetadataOperationContext.AdmitImage` succeeds; the closure gate verifies this
wiring without making the leaf reference the higher-layer operation type. The
admission call is unconditional for every supported image: a compatibility
caller may use an explicit `Unbounded` policy, while product entry points must
supply a finite policy before semantic cutover. The reader obtains both the
`MetadataReader` and metadata block from the one PE owner. It must not accept
an independently supplied reader/block pair:
an in-bounds whole-PE offset can otherwise be mistaken for a metadata-relative
offset with no identity check capable of detecting the mismatch. The
acquisition owner retains the lease; the primitive does not reopen a path, own
or dispose the image, copy the whole metadata block, or retain a
`PEMemoryBlock`, `BlobReader`, or unmanaged pointer after the call.
The Metadata-owned `MethodSemanticsAssociationSession` must call its
`AssemblyImage.EnsureAlive()` liveness check immediately before each product
primitive invocation; passing a bare borrowed `PEReader` without that check is
a contract violation. It is the sole product invocation owner. Direct primitive
calls are confined to this leaf's boundary tests, where the test owns the
reader lifetime.

The Metadata-owned session calls `MetadataImageFormatClassifier` before image
admission, `MetadataReader` construction, or primitive invocation. A direct
boundary-test call reaches the same classifier from the leaf before it reads
table layout. Unsupported Windows Metadata is not reported as malformed
ECMA-335, and this boundary adds no projected-accessor fallback, dual-reader
correspondence, or compatibility adapter.

For a supported image, the implementation may use only public SRM layout facts
to locate the table: its metadata offset, row size, table row counts used to
derive ECMA-defined index widths, and
`PEMemoryBlock.GetReader(start, length)` over the same `PEReader`. It decodes
the table's complete three-column schema:

- raw `Semantics` bits;
- a `MethodDef` row identifier; and
- a `HasSemantics` coded index restricted to Property and Event tags.

Checked arithmetic and SRM-reported row counts bound every read and decoded row
identifier. Whole-image admission charges each declared row once; the census
records rows visited but does not debit `MaxMetadataRows` again. Before
retaining a neutral row, it separately charges
`MaxRetainedMethodSemanticsAssociations`. The reader must reach the physical end
of the table before a consumer can treat any association range as complete.
The neutral result preserves table row number, raw semantics bits,
`MethodDefinitionHandle`, association kind, and association row identifier. It
validates the computed column width against SRM's table row size, physical row
access, the non-nil MethodDef and HasSemantics row identifiers, target row
bounds, and records whether association values are actually nondecreasing. It
does not parse the metadata stream's sorted bit or decide whether nonmonotonic
ordering invalidates a declaration, which roles are legal for a property/event,
whether a standard role is duplicated, whether a method belongs to the
aggregate's declaring type, or how rejection is presented; those remain
Metadata semantics.

The retained-association budget protects the bytes held by the immutable
Metadata-owned operation index, independently of the broader row-admission
ceiling. Its corpus-derived ceiling may therefore be lower than
`MaxMetadataRows`. Exhaustion rejects the semantics census for every
property/event projection that depends on it; there is no unindexed streaming
fallback. Independent declaration kinds may continue under their normal
failure policy.

The leaf receives neither a `MetadataOperationContext` nor an image/cache
identity. It charges the supplied neutral budget before returning each retained
row and retains no state after the call. Metadata owns generation/operation
mapping, single-pass reuse, typed rejection caching, and both cold-pass and
cache-observation session liveness; `MDP006` gates accounting and `MDP009`
gates operation/liveness wiring.

This is not a reusable general coded-index decoder or table projector. No
public API accepts an arbitrary `TableIndex`, column schema, or coded-index
kind. Adding another table requires a design change to
[bounded metadata traversal](design/bounded-metadata-traversal.md), this
registry, and the owning consumer contract.

The primitive-local boundary is gated by
`LayeringTests.MetadataPrimitives_RemainsLeaf`,
`LayeringTests.MetadataPrimitives_MethodSemanticsReaderIsIsolated`, and
`MethodSemanticsRowReaderTests`. Those gates prove MetadataPrimitives remains
an SRM-only leaf, no other MetadataPrimitives type decodes raw ECMA table-row
bytes or table coded-index columns, and the primitive does not expose a general
table decoder. The separately registered
`MetadataImageFormatClassifier` may read only its fixed metadata-root admission
prefix and bounded version field; it may not call table-layout APIs. Blob and
heap `BlobReader` use is outside this table-layout closure. Existing
hand-parsed metadata stream/header code outside these two named leaves is
separate migration debt under the general bounded-traversal prohibition; these
exceptions neither legitimize nor expand it. `MDP016` in
[member inspection planning and Metadata
projection](design/member-inspection-planning-and-metadata-projection.md) owns
that boundary gate. Consumer migration is phased separately: `MDP011` closes
Metadata-owned paths at slice 6, and `MDP013` closes all product bypasses at
slice 8. Its outcome tests must establish:

- ordered-multiset equality with `ildasm` over association owner, semantic
  role, and method for conventional valid metadata in the required CI
  environment; construction-known `ilasm` fixtures run in that same
  external-tool-dependent group, and both may skip together locally;
  tool-independent `MetadataBuilder` and byte-patched fixtures whose expected
  physical row numbers and raw bits are fixed by construction provide the
  non-skipping floor; `mdv` is explicitly not this oracle because it folds the
  rows;
- aggregate equality with SRM convenience accessors for conventional valid
  property/event metadata;
- exact preservation of multiple `Other` and duplicate standard-role rows,
  zero/unknown/combined semantics values, physical row order, and observed
  nonmonotonic ordering; nil or out-of-range MethodDef or association row
  identifiers produce typed mechanical rejection, while a companion with the
  same physically out-of-order rows and the sorted bit clear fails during SRM
  reader construction; Metadata-semantic rejection of roles, duplicates,
  declaring types, and ordering policy belongs to `MDP004`;
- all four narrow/wide MethodDef and HasSemantics coded-index combinations,
  generated once per test run rather than stored as multi-megabyte binaries;
  each asserts decoded values, while SRM row-size equality separately checks
  the total width;
- bounded work and allocation before retention on oversized tables; and
- the same supported ECMA-335 result under Browser/Wasm and
  NativeAOT-compatible hosts, gated by
  `LayeringTests.MetadataPrimitives_MethodSemanticsPlatformProbesAreWired` and
  `eng/run-method-semantics-platform-probe.sh`; unsupported-format
  classification and its direct leaf close-negative belong to `MDP017`.

## Why `TypeRef` remains local

The Analysis and Decompiler models are not accidental copies of one canonical
type.

Analysis `TypeRef` carries evidence-facing concerns including:

- exact resolution provenance beside the structural shape;
- framework and protobuf trust evidence;
- catalog-correspondence payload for modifiers, function pointers, and array
  bounds;
- Analysis-specific incomplete and unsupported outcomes.

Decompiler `Pipeline.TypeRef` carries code-generation concerns including:

- value-type hints and inline-array facts;
- enclosing-type facts used by raising;
- function-pointer calling conventions, parameter ref kinds, and modifiers;
- fidelity-lowering unsupported shapes and printer inputs.

Those facts have different equality, lifetime, and failure semantics. A shared
`TypeRef` would either erase required evidence or become a union of unrelated
layer policy. The repository-wide representation map therefore remains
authoritative: type identity is a set of scoped currencies, not one universal
record.

The allowed sharing point is below both models:

1. admit the untrusted blob or relationship walk through shared bounds;
2. return neutral handles, exact names, structural coordinates, or typed
   rejection;
3. let the consuming provider construct its native model.

## Remaining convergence

### 1. Use one TypeSpec admission mechanism

Analysis and Decompiler currently duplicate:

- thread-static recursion depth;
- cumulative TypeSpec byte accounting;
- a local 1,024-byte per-TypeSpec limit;
- direct `SignatureBlobGuard` calls;
- cleanup of the active budget in `finally`.

MetadataPrimitives already owns `TypeSpecGuard`, with a 256-entry and
4,096-cumulative-byte contract plus the shared structural prescan. The local
decoders match those limits but add a 1,024-byte per-TypeSpec cap. They can
therefore only reject more input than the shared policy; they do not accept
anything the shared guard rejects.

The shared guard currently merges depth and cumulative-byte exhaustion into
one rejection kind, while the local decoders preserve separate reasons for
active recursion, per-TypeSpec bytes, cumulative bytes, and unsafe structural
nesting. Those reasons participate in rendered output and equality in both
owners and in Decompiler fidelity diagnostics. Analysis also includes the
reason in `TypeRef` hashing and graph identity; Decompiler currently omits it
from `TypeRef.GetHashCode`. Consolidation must preserve each owner's actual
behavior rather than making them incidentally uniform.
The current rejection precedence is active recursion depth, per-TypeSpec bytes,
cumulative bytes, then unsafe structural nesting; the shared guard must retain
that order for the configured semantic-decoder path.

The first implementation slice should:

1. extend `TypeSpecGuard` with a configured per-TypeSpec byte limit and typed
   rejection discriminators sufficient to preserve all four existing local
   outcomes, without moving owner-specific reason text into the primitive;
2. route Analysis and Decompiler `GetTypeFromSpecification` through that
   disposable scope;
3. configure both semantic decoders with their existing 1,024-byte limit and
   map each typed rejection back to its exact current owner result;
4. remove the local counters and direct structural prescan only after the
   shared scope preserves cleanup and nested-entry behavior;
5. preserve successful and rejected projections byte-for-byte, including
   reason text, equality, each owner's current hash behavior, and
   fidelity-diagnostic grouping.

The detailed admission result is not permission to change the existing shared
string contract. `SignatureDecoder` and `GuardedSignatureDecoder` must continue
to expose the same coarse `SignatureDecodeRejectionKind` and detail text for
top-level and nested TypeSpec rejection. The shared adapter may deliberately
coarsen the new admission discriminator at that boundary.

`ProviderSignatureDecodeBoundaryTests` is the existing anti-ratchet gate for
top-level provider decodes and nested TypeSpec entry. The implementation slice
must update that gate so its accepted Analysis/Decompiler pattern is the shared
`TypeSpecGuard`, then mutation-prove that bypassing the guard in either decoder
fails. `TypeRefDecoderRecursionTests` in both owner suites must cover matching
close-negative cases at 1,025 and 4,097 bytes, active-depth exhaustion, and
unsafe structural nesting. A co-violation matrix must pin every earlier reason
against every later reason: depth with per-entry, cumulative, and structural;
per-entry with cumulative and structural; and cumulative with structural.
Outcome tests must pin each owner's reason text, `TypeRef` equality, and current
hash behavior before and after convergence. Exact top-level and nested
string-outcome tests must pin the existing coarse rejection kind, detail, and
`GetValueOrThrow` exception text.

Removing the 1,024-byte cap is explicitly deferred. A legal 4,095-byte generic
shape can amplify into thousands of resolved names and millions of rendered
characters. Before widening acceptance, each artifact operation must have
separately enforced item and text budgets with typed rejection, as required by
[bounded metadata traversal](design/bounded-metadata-traversal.md). Evidence
must include the worst-case wide generic shape with long resolved names, not
only a representative accepted fixture. A later decision must own any
acceptance, output, equality, or diagnostic-bucketing change.

### 2. Keep decode mechanics separate from failure policy

Metadata and Instructions have similarly named `GuardedProviderDecode`
adapters. Metadata returns values plus degraded state for row-level API
projection. Instructions and ILDiff use try-style results, typed diff failures,
or hash-derived unsupported identities. Decompiler's string composers throw at
their existing artifact-operation boundary, while Metadata's string producers
retain `SignatureDecodeResult<T>`.

Those outcomes are intentionally different. Do not move fallback construction
or throwing behavior into MetadataPrimitives merely to delete similarly shaped
methods. The shared owner is the prescan and TypeSpec admission mechanism;
the consuming owner decides what rejection means.

`StringSignatureDecodeBoundaryTests` currently scans Metadata and
MetadataPrimitives only. Decompiler has three string-producing metadata gateway
families:

- `GuardedSignatureText` terminates signature text through
  `GetValueOrThrow`;
- `CSharpBodyDiff.ResolveIdentityTypeName` terminates typed
  `TypeResolver.ResolveTypeName` failures through
  `MetadataIdentityResolutionException`;
- direct `MetadataReaderExtensions.GetFullTypeName` calls reach
  `TypeResolver.GetFullName`; some propagate its `BadImageFormatException`,
  while one `IrImporter` boundary explicitly degrades to `"<type>"`.

Closure across those families is **not verified**. The first implementation
slice must add `DecompilerStringGatewayBoundaryTests`, using resolved symbols
rather than owner-type text so extension-method calls cannot evade the census.
Every call site must be classified by gateway and expected owner failure
boundary. Mutations that remove, replace, bypass, or silently reclassify any
boundary must fail.

A generic decode helper belongs in MetadataPrimitives only if at least three
consumers need the same typed outcome contract. Line-count reduction alone is
not sufficient.

### 3. Distinguish ILDiff's two signature projections

ILDiff contains two `SignatureIdentityProvider` classes, but they answer
different questions:

- assembly/member pairing requires exact declaring-type identity and rejects a
  method from the correlation map when identity construction fails;
- operand canonicalization applies diff normalization, compiler-generated
  correspondence, and unsupported-signature identities for row evidence.

Do not merge them into one provider or move their string policy downward.
A focused ILDiff cleanup may rename them to make the two projections obvious
and may share genuinely byte-identical primitive spelling helpers, but it must
preserve their distinct failure and normalization contracts.
`ILInspector.ILDiff.Tests.SignatureDecoderSafetyTests` covers assembly/member
pairing rejection, but no named gate jointly proves that pairing and operand
canonicalization retain their distinct outcomes. That distinction is **not
verified** as a refactoring invariant. Any cleanup slice must first add focused
outcome tests for both providers and mutation-prove that merging their failure
or normalization paths fails.

### 4. Make the namespace split deliberate

Most files in `ILInspector.MetadataPrimitives` still declare
`namespace ILInspector.Metadata`, while newer neutral currencies use
`ILInspector.MetadataPrimitives`. The mismatch began as transitional. These
internal libraries have no external API-stability commitment, but some old
full type names are observable repository behavior:

- `ILInspector.Metadata` forwards
  `ILInspector.Metadata.SignatureBlobGuard` and
  `ILInspector.Metadata.MethodStructuralSignature` to the primitives assembly;
- `SignatureDecoderSafetyTests.SignatureBlobGuard_OldAssemblyIdentity_IsForwarded`,
  two `SectionPipelineTests` cases, and
  `LibraryFindingConsumerTests.TypeForwardersQueryProjection_RetainsFindingSemanticsAndDisplayProjection`
  pin the former name across runtime resolution, query output, and Finding
  projection;
- `tools/DecompilerHarness/corpus/pr-quick-baseline.json` inspects the local
  build and pins forwarded and non-forwarded MetadataPrimitives types whose
  full names still use `ILInspector.Metadata`;
- `tools/DecompilerHarness/corpus/real-world-baseline.json` instead inspects
  published `dotnet-inspect.any` 0.14.0. Its legacy names are a frozen external
  snapshot, not evidence about current source, and change only when
  `SELF_VERSION` is deliberately re-pinned and the corpus is re-harvested;
- no test independently pins the forwarded
  `ILInspector.Metadata.MethodStructuralSignature` name;
- a CLR type forwarder cannot preserve an old full type name while changing its
  namespace.

A blanket namespace move would therefore change self-inspection output and
repo-local expectations; it is not a mechanical cleanup. This is repository
coupling, not an external compatibility promise. The dedicated namespace slice
should instead:

1. inventory every public primitive as a repo-pinned legacy name, an ungated
   forwarded name, or owner-native `ILInspector.MetadataPrimitives`;
2. retain, migrate, or retire each old name deliberately, updating all exact
   string and local-build corpus expectations and adding or removing
   forwarding tests to match;
3. require new neutral currencies to use the owner-native namespace;
4. add a source/forwarder/test/local-corpus census that fails on an
   unclassified public type, stale forwarding contract, or exact local-build
   expectation absent from the classification. The gate must also classify
   every committed `tools/DecompilerHarness/corpus/**/*baseline.json` by input
   provenance and fail on an unknown class. Published-package snapshots are
   excluded from current-source name matching and must not be rewritten outside
   their package-version re-harvest.

This makes the mixed namespace deliberate without adding duplicate wrapper
types. The classification property is currently **not gated**.

## Existing safety enforcement

The current boundary is protected by:

- `ProviderSignatureDecodeBoundaryTests` for guarded provider decodes and
  bounded nested TypeSpec re-entry;
- `StringSignatureDecodeBoundaryTests` for Metadata and MetadataPrimitives
  string-producing signature paths; Decompiler policy is not yet covered;
- `MetadataRelationshipTraversalTests` for bounded relationship mechanics;
- `SignatureBlobGuardTests` and `SignatureDecoderSafetyTests` for malformed and
  adversarial signature shapes;
- `MethodSemanticsRowReaderTests` for lossless physical rows, raw bits, index
  widths, malformed bounds, independent IL-oracle parity, and retained-row
  budgeting;
- `LayeringTests.MetadataPrimitives_MethodSemanticsReaderIsIsolated` and
  `LayeringTests.MetadataPrimitives_MethodSemanticsPlatformProbesAreWired` for
  raw-layout/API closure and executable NativeAOT/Browser wiring;
- `LayeringTests.MetadataNameMatching_DoesNotDependOnFindingBackedText` for the
  MetadataPrimitives owner of neutral name matching.

`LayeringTests.MetadataPrimitives_RemainsLeaf` enforces that
MetadataPrimitives has zero project references.

An implementation that changes a stated safety or ownership property must
extend the owning gate rather than relying on a green broad suite.

## Sequencing

Keep the work in independently reviewable slices:

1. **TypeSpec admission and boundary gates** — add the leaf and Decompiler
   string-gateway gates, converge Analysis and Decompiler on the shared guard,
   and preserve the current 1,024-byte admission and rejection projections.
2. **Namespace ownership classification** — inventory legacy forwarded
   identities and owner-native types, then add the
   source/forwarder/test/local-corpus expectation and baseline-provenance
   census.
3. **Optional local clarity** — rename ILDiff's two providers if the names
   continue to obscure their distinct projections.
4. **Lossless `MethodSemantics` row boundary** — implemented by
   `MethodSemanticsRowReader` and its raw-oracle, malformed-row, budget,
   platform, and architecture-closure gates. Product activation still belongs
   to the member-inspection plan's Metadata admission slice, not a general
   table-projection dependency.

Do not combine these slices with a `TypeRef` redesign, provider-policy rewrite,
rendering change, or TypeSpec acceptance widening. Each slice must preserve
product output and failure visibility.

## Non-goals

- A repository-wide `TypeRef`.
- One canonical type spelling.
- Moving rendering, trust, analysis, correlation, or fidelity policy into
  MetadataPrimitives.
- Hiding mechanical rejection behind empty or plausible values.
- Adding a dependency from Analysis to Decompiler or from Decompiler to
  Analysis.
- Deduplicating provider classes solely because they implement the same SRM
  interface.
- A blanket namespace rename that silently changes forwarded full type names.

## Superseded decision

The June 2026 "stop after step 3" decision correctly rejected a unified
display-oriented `TypeRef` and declined to share a two-consumer
attribute-name walk whose payoff was only about 15 lines. It treated all
further adoption as one choice.

The durable part remains: semantic models stay local and consumers pull only
demonstrably shared primitives downward. The superseded part is the prohibition
on steps 4 and 5. Analysis and Decompiler have already adopted shared
relationship and identity mechanics; they should complete that convergence
where one bounded SRM mechanism has one correct answer.
