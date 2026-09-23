# Method Body Inspection

> Design north-star for raising `member` body sections and the
> `library coordinate` child onto one service model. This complements the
> assembly acquisition/session seam
> in the [assembly inspection query model](assembly-inspection-query.md):
> assembly inspection opens and identifies an assembly; method-body inspection
> explains one method body or one IL coordinate inside it.

## Problem

`member` and `library coordinate` expose peer facts about method bodies:

- source and decompiled source
- IL
- exception regions
- calls, callers, call graphs, and return addresses
- allocation, safety, and cost facts
- hidden facts and overlays

They reached that capability from different directions.

`member` is integrated into the API pipeline. It resolves a type/member/overload,
then `ApiOutputFormatter.PopulateIndexSections` opens analysis indexes and PDB
contexts to fill `MemberCodeView` sections. `MemberCodeProvider` separately
opens metadata/decompiler state for source, IL, attributes, overlays, and hidden
facts.

The IL-coordinate path started as a one-off source lookup. Its command helper
grew to resolve member, instruction, exception, callsite, return-address,
allocation, safety, and cost context, build CLI model rows, and own fallback
opcode heuristics. It was no longer just a source query.

Both paths have useful pieces, but neither is the target architecture:

- `member` uses the normal command pipeline, but its formatter constructs facts.
- `library coordinate` needs a thin command query over a Research-owned
  projection.
- Both paths construct overlapping method-body facts differently.

## Target

The shared abstraction is a command-configured **method body projection** over
an already-open metadata/source session:

```text
ResolvedAssemblyReference
  -> AssemblyInspectionSession
      -> Metadata / Instructions / Analysis producers
          -> Research projection producer
              -> view-compatible projection
                  -> CLI selection and rendering
```

The command chooses a **selector** (member or IL coordinate) and requested
capabilities. A focused Research `*ProjectionProducer` composes the owner
libraries into a top-level projection shape. `ResearchViews` is only a thin
forwarding/aggregation facade; it does not own request/result contracts or
weighty production logic.

The first implementation is `ILOffsetProjectionProducer`:

- `ILOffsetProjectionRequest` carries the already-open `SourceLinkService`,
  optional focused Analysis input, coordinate, and capability flags — never a
  path, `PEReader`, `LibraryBodyIndex`, or command options.
- Metadata exposes a session-bound `MethodBodySource` from both `PdbContext` and
  `AssemblyInspectionSession`. It returns copied `MethodBodyData` and implements
  operand-name resolution without exposing its owned readers.
- `MethodBodyData` lives in `MetadataPrimitives` because it is the neutral
  Metadata-to-Instructions contract; Instructions decodes the snapshot directly.
- `ILOffsetProjectionProducer.Produce` owns Metadata + Instructions + focused
  Analysis + SourceLink composition and returns `ILOffsetProjectionOutcome`;
  it never acquires or reopens Analysis.
- `ResearchViews.ProjectILOffset` forwards directly to the producer.
- `ILOffsetQuery` retains CLI parsing, capability selection, one scoped
  Analysis execution over the SourceLink session's prefetched authoritative
  image, failure/exit handling, and producer invocation. Coordinate-file
  execution unions the MethodDef tokens once and reuses one focused input for
  every row.

`ILOffsetAnalysisInput` joins allocation, safety, and call-graph results from
one Analysis execution receipt. The producer verifies that the input's module
version matches the already-open source session and that each requested result
family participated; missing, mixed, stale, or unrequested evidence remains a
typed visible failure rather than an empty context.

The capability replaces product friendship. Research and the CLI consume
explicit Metadata operations; neither receives `PEReader` or `MetadataReader`.
`LayeringTests.Metadata_FriendsOnlyTestAssemblies` enforces the complete
Metadata friend set. The source rejects resolver operations after its owning
session is disposed, while copied body data remains safe to retain.

`MemberProjectionProducer` applies that pattern to member inspection:

- top-level `MemberProjectionRequest` and `MemberProjectionResult` contracts
  carry already-open Metadata, optional focused Analysis input, and selected
  projection capabilities;
- the producer owns method import, one Finding census, overlays, portable
  source, tracing, and projection-specific failure shaping;
- `ResearchViews.ProjectMember` is a compatibility forwarder with no production
  logic;
- CLI member inspection and L1 Research queries invoke the producer directly;
  the CLI unions `ResearchFactRegistry` requirements into its existing Analysis
  execution, while the pathless Workspace query supplies its immutable-image
  context; and
- `MemberProjectionAnalysisInput` joins the exact allocation, safety,
  call-graph, and leverage results issued by one Analysis execution. CLI and
  Workspace/L1 composition supply it, so the producer never reopens Analysis.

`ResearchAssemblyContext` is no longer a member-projection input. The
Workspace/L1 query retains it only for residual query-owned callee evidence
that still uses the compatibility index. The focused member input is not a
universal Research result bag: its constructor names the four result families
used by the default registry and requires one shared execution receipt.

This migration is tracked by
[#2786](https://github.com/richlander/dotnet-inspect/issues/2786).

## Selector shapes

There are two entry points into the same method-body system.

### `MemberSelector`

Identifies a method by API identity:

```csharp
public sealed record MemberSelector(
    string TypeName,
    string MemberName,
    int OverloadIndex,
    bool PublicOnly);
```

This is the `member` command shape. It is selected from `ApiType`/`ApiMember`
and stable member selectors.

### `ILCoordinateSelector`

Identifies a method-body selector by metadata coordinates:

```csharp
public sealed record ILCoordinateSelector(
    int MethodToken,
    int ILOffset);
```

This is the `library coordinate` selector shape. It is not a separate command
architecture; it establishes the child request while remaining another
selector for the same method-body inspection pipeline.
[Coordinate child command](coordinate-child-command.md) owns that CLI
placement.

## Facets

Both query shapes request the same facets. A facet is a product capability, not a
CLI section name. Section selection maps to index capabilities and owner queries
at the command boundary.

| Facet | Facts returned | Owner |
| --- | --- | --- |
| `Member` | assembly, type, member, signature, visibility, async, selected token | Metadata |
| `Instruction` | IL offset, opcode, operand, block, branches, next offset | Metadata / Instructions |
| `Source` | source file, line, SourceLink URL, browsable URL | Metadata / SourceLink |
| `ExceptionRegions` | region, clause, try/handler/filter ranges, caught type | Metadata / PDB context |
| `Calls` | direct call sites, call kind, callee, operand token, return address | Analysis |
| `Callers` | inbound caller sites and caller-scope provenance | Analysis / composition |
| `Graphs` | call graph and caller graph nodes with signal annotations | Analysis |
| `Unsafe` | unsafe API/member evidence and unsafe operations | Analysis |
| `AllocationFacts` | allocation facts at method or coordinate scope | Analysis |
| `SafetyFacts` | safety facts at method or coordinate scope | Analysis |
| `CostFacts` | dispatch/delegate/function-pointer cost facts | Analysis |
| `HiddenFacts` | offset-keyed annotations used by Facts/overlays | Research |
| `DecompiledSource` | raised/lowered source and diagnostics | Decompiler / Research |
| `AnnotatedSource` | raised source plus hidden facts and IL | Research |
| `PdbSource` | checksum-verified PDB-mapped source slice | Services / SourceLink |

The important rule: a facet has one canonical owner. CLI sections such as
`Allocation Facts`, `Context: Allocation`, `Facts`, or `Annotated Source` may
render different projections, but they should not compute the underlying facts
independently.

Facet identity may become typed where a closed, product-owned catalog needs it —
see the *facet-identity design axis* (string vs typed vs generic type-as-key) in the
[Assembly Inspection Query Model](assembly-inspection-query.md) for when each applies.
That does not require an omnibus session facade. Each owning layer exposes its
canonical query surface; the CLI composes those results. Cross-layer overlays
still belong in `ILInspector.Research`, whose `IResearchFactProducer` /
`ResearchFactRegistry` is the appropriate producer-registry prior art.

## Service shape

```csharp
public sealed class MethodBodyInspectionSession
{
    public string SourceName { get; }
    public LibraryBodyAnalysisExecution AnalysisExecution { get; }
    public LibraryCallGraphAnalysisResult CallGraphAnalysis { get; }
    public LibraryBodyIndex BodyIndex { get; } // compatibility only
}
```

`InspectionQueryContext.BodyAnalysis()` shares `AnalysisExecution` across
migrated queries. The boundary:

- `Open` captures command-selected capability and body-scope policy, creates a
  `LibraryBodyAnalysisRequest`, and delegates path or prefetched-image
  execution to `LibraryBodyAnalysisService`
- one session builds and reuses one Analysis service execution per command
- migrated neutral Analysis queries consume focused Analysis-owned safety,
  implementation-profile, optimization, leverage, and call-graph results
- local and catalog member graphs compose
  `LibraryCallGraphAnalysisResult` values, while optional graph annotations
  consume `LibraryOptimizationAnalysisResult`
- `LibraryBodyIndex` remains only for explicitly unmigrated compatibility paths
- session methods exist only for composition requiring session-owned state,
  such as source attribution or multiple assembly scopes
- the CLI composes and renders; it does not classify or infer Analysis facts
- PE, PDB, metadata, and decompiler lifetime should remain behind their owning
  layers as those seams converge

## Layer ownership

### `ILInspector.Metadata`

Owns metadata-local method-body facts:

- MethodDef token and overload resolution
- instruction context
- exception regions
- portable-PDB document names, checksums, and sequence-point coordinates

It stays SRM-only and does not load inspected assemblies.

### `DotnetInspector.Services` / `ILInspector.SourceLink`

Owns PDB-source acquisition and verification:

- host-authorized local or SourceLink source acquisition
- portable-PDB checksum verification
- exact member/type body slicing

It consumes Metadata-owned PDB document and coordinate facts without making
Metadata own textual C#. `PdbSourceHouseTests.FromContent_VerifiedSourceProducesCompleteLineCensus`,
`PdbSourceHouseTests.FromContent_UsesSequencePointEvidenceToSelectAConditionalMember`,
and
`AssemblyContextSourceQueryTests.LocalPdbSource_DoesNotRequireSourceLinkMap`
gate that boundary.

### `ILInspector.Analysis`

Owns IL analysis facts:

- direct calls and return addresses
- callers and caller graphs
- allocation, safety, and cost facts
- unsafe operations and unsafe API evidence

`LibraryBodyIndex` remains a temporary compatibility facade over one shared
body acquisition. The
[library body Analysis service](library-body-analysis-service.md) owns
stateless path and immutable-image execution plus publication of focused
detached results. `LibraryBodyAnalysisPlan` owns producer dependencies and
scope; execution publishes separately typed safety, implementation-profile,
optimization, leverage, and call-graph results, with resource-occurrence and
resource-lifecycle results following in their owning slices. Section queries
and topic-specific Analysis services consume those typed results rather than
adding more properties or algorithms to the facade.
For each decoded method, `MethodBodyAnalysisContext` packages the method
identity, exception regions, the shared Layer-0 `MethodInstructions`, and
Analysis-owned loop regions and decoded local types, together with the neutral
navigation over them (instruction-at-offset, next-non-`nop` index, loop-region
membership) that every topic producer shares. Local signatures are
decoded once during acquisition rather than independently by safety evidence
and occurrence scans. Raw IL, generic decoding scope, metadata readers, and
reader-bound method bodies remain outside the context so a topic producer
cannot create a second decode or metadata traversal path.
Allocation path contexts, confidence, and post-dominance remain Layer-1
interpretations rather than becoming neutral context. Analysis may publish
those interpretations through the typed `AllocationOccurrence` result; a
consumer must preserve that owner-issued value rather than infer path context
from rendered detail text or source syntax. The first Annotated Source adoption
projects only positive exception-related allocation paths:

- `Escape == ThrowPath` means the allocation constructs the value used by a
  `throw`;
- otherwise `PathContext == ErrorPath` means the allocation occurs in a
  `catch`, filter, or fault handler.

The two cases are structural compiled-code evidence. They do not claim that an
exception occurred, a handler ran, the path is cold or rare, or how often the
allocation executed. Branch and switch-arm contexts are not called fallback
paths because Analysis does not identify the branch's semantic role.

`BodySignalAnalysis` owns array, throw, exception-region, allocating-box, and
throw-path object signals; it receives the metadata-dependent box judgment
through a narrow callback. `MethodBodyFlowProbe` owns the bounded throw-path
probes reused by body-signal and allocation analysis.
`MethodSafetyAnalysis` owns unsafe API/signature/local classification, body
opcode and call evidence, and the pointer-stack interpretation that emits
unsafety occurrences. The assembly reader retains calli-signature resolution
and passes only the display detail into the producer.
`MethodCallAnalysis` owns one instruction traversal that projects direct and
indirect calls, return addresses, same-assembly definition tokens, call kinds,
opcodes, loop membership, and allocation-derived multiplicity. Metadata facts
arrive through `IMethodCallResolver`; the reader retains member and calli
signature resolution plus MethodSpec peeling without exposing its reader or
generic scope. The producer appends to caller-owned call and safety-evidence
builders so results emitted before a later recoverable metadata failure survive,
and delegates unsafe-call/opcode classification to `MethodSafetyAnalysis`
without a second body scan.
[`MethodThrowAnalysis`](analysis-local-throw-evidence.md) owns opt-in physical
local-throw evidence. It consumes the shared value provenance and
acquisition-scoped exception-type qualification, retaining unresolved sites,
unavailable bodies, and the qualified TypeDef address. It does not reinterpret
throw counts, construction signals, or declared-source attribution as typed
throw evidence.
`MethodAllocationFacts` owns the allocation topic for one decoded body:
allocation occurrence discovery, allocation-shape classification, escape
classification, and the private path-context, path-confidence, and
post-dominance indexes behind its multiplicity reading. It consumes the shared
context and never decodes IL, builds blocks, recomputes loop regions, or decodes
a local signature again. Metadata judgments (type/member token resolution,
delegate-constructor, value-type-box, non-heap-construction, in-assembly
element, field-owner) and the raw-IL reaching-definitions analysis arrive
through the narrow `IMethodAllocationResolver` contract implemented by the
assembly reader, so no metadata reader, `PEReader`, generic scope, IL buffer, or
reader-bound body reaches allocation analysis. One `MethodAllocationFacts`
object binds the canonical context and Layer-1 query methods before other topic
producers run. When allocation collection is selected, one scan populates that
same object with both the discovered and escape-refined occurrences. The
published allocation facts take the classified occurrences, and
`OptimizationOpportunityAnalysis` reuses the discovered occurrences plus the
query methods
(`PathContextAt`, `PathConfidenceAt`, `PostDominanceAt`, `MultiplicityAt`).
`FactsBundlesBindContextOccurrencesAndQueries` gates the bundle's context,
occurrence, and query coherence.
`OptimizationOpportunityAnalysis` owns the per-method optimization instruction
walk, shape
classification, lazy memoized reaching-definitions use, and allocation metadata
projection without opening another allocation or decode path. Its traversal may
repeat member and type resolution. It retains opportunity ordering and deferred
state while delegating array/span/materializer flow,
StackGuard-fallback classification, and string-concat accumulation evidence to
focused recognizers. Those recognizers consume the canonical context and
factual resolver but do not emit opportunities or own another top-level body
traversal. They may perform focused sub-scans around one candidate. The
reaching-definitions result is
memoized within one collection rather than shared with allocation analysis.
Those reader- and raw-IL-dependent facts arrive through
`IOptimizationOpportunityResolver`; the producer does not own the metadata
reader, generic scope, or raw IL. Call-site acquisition uses the same
`MultiplicityAt` reading for direct-call multiplicity.
`LibraryMethodAnalysisRunner` owns the ordered per-method lifecycle: PE body
acquisition, the intentionally throwing `InstructionDecoder.Decode` +
`BlockGraph.Build` decode, loop-region and local-type construction for the
canonical context, topic-producer sequencing, leak-only handling, recoverable
diagnostics, and method-local result publication. It receives one
`ILibraryMethodAnalysisInfrastructure` from the assembly builder for the
caller-owned primary-image reader/PE pair. The builder delegates method
identity, generic scope, and the existing allocation/optimization/call
metadata resolvers to `LibraryBodyPrimaryMetadataResolver`. Topic producers
still receive only their narrow contracts.
`BuildCallTree_PreservesRecoverableBodyAnalysisFailure` gates calls, safety
evidence, and diagnostics surviving a later recoverable failure;
`LibraryBodyIndex_PrefetchedImageScopeSkipsMalformedUnselectedBody` gates
scoped decode with an excluded malformed-body close negative. The assembly
builder retains the metadata-ordered work list, parallel scheduling,
and service lifetime composition. `LibraryBodyAnalysisAccumulator` receives
the completed method-local result array in metadata order, merges all topic
collections and partial diagnostics, computes the call-derived non-heap and
exception-type assembly projections, and constructs the immutable
`LibraryBodyAnalysisResult`.
`ParallelBuild_IsOrderStable_AcrossRepeatedOpens` gates deterministic ordered
output, while `BuildCallTree_PreservesRecoverableBodyAnalysisFailure` also
gates partial-result accumulation.
`LibraryBodyPrimaryMetadataResolver` owns primary-image method identity,
memory-safety caller-contract and generated-attribute judgments,
token/member/type/field/calli/value-type and delegate facts,
async-state-machine caching, and the narrow resolver adapters. The assembly
builder consumes its method identities, while the result accumulator publishes
the same memory-safety judgment.

### Primary-image caller-contract normalization

**Owner and claim:** Analysis classifies every primary-image MethodDef caller
contract from Metadata's normalized `MemorySafetyMetadataIndex`, including
constructors and property/event accessors. Legacy pointer compatibility maps to
`Implicit`, an updated explicit contract maps to `Explicit`, and an updated
pointer-only signature maps to `None`. Unsupported and malformed module markers
retain their exact module state while Metadata supplies their legacy-compatible
member result. Conflicting markers and other unavailable module/member evidence
map to `Unavailable`; they are not counted as propagating methods and do not
enter leverage, Opaque, or Hollow populations.

The module rules result and per-method contract remain separate typed facts.
Structural pointer declarations and locals remain body evidence rather than a
substitute caller contract. This slice does not classify call targets or fields,
infer inner unsafe contexts, or reconstruct operation meaning.

`CallerUnsafeMode_PointerSignatureIsImplicitWhenModuleNotOptedIn`,
`CallerUnsafeMode_UsesNormalizedUpdatedContracts`, and
`CallerUnsafeMode_UnavailableContractIsNotAPropagator` gate the primary-image
contract. The wider #5254 adoption continues with call targets, fields,
operation evidence, and inner-unsafe roles before #5270 composes CLI and browser
audit paths.

### Same-image call-target contracts

**Owner and claim:** Analysis joins each `call`, `callvirt`, or `newobj`
operand that corresponds to a primary-image MethodDef to that definition's
normalized caller contract. The MethodDef token is the join currency:
`MethodDefinitionMap` resolves direct MethodDef operands, MethodSpec operands,
local MemberRef aliases, and constructed-generic targets before
`MethodSafetyAnalysis` classifies the call. The resolved MethodDef remains the
internal join currency; `DirectCall` preserves its `None`, `Implicit`,
`Explicit`, or `Unavailable` contract without changing the established operand
and peeled-token identities.

Definition correspondence compares open signatures before generic substitution:
`Invoke(!0)` and `Invoke(int)` remain distinct even on a constructed `Target<int>`.
Constructed parameter and return views do not replace that identity. Generic
variables in a declaring TypeSpec and optional vararg arguments belong to the
caller's scope; variables in the open return and required-parameter signature
belong to the target's scope. Nested function-pointer signatures retain their
enclosing type and method generic scope. For source-attributed calls, caller
scope comes from `DirectCall.EvidenceMethod`, whose physical body contains the
operand, rather than the projected source `Caller`. A constructed declaring
type must supply exactly the generic arity declared by its canonical
metadata-name segments. Arity is summed from retained root-to-leaf
metadata-name segments, not reconstructed from flattened display text, so a
literal `+` within one segment is not mistaken for nesting. Full analysis
leaves malformed correspondence unresolved, while the bounded presence query
fails visibly rather than using an argument count as a substitute for
declaration arity.

Call-contract composition uses the primary image's declared module name to
recognize same-module `ModuleRef` aliases, including aliases nested in signature
types. Full analysis and the bounded presence resolver share
`SameImageSignatureComparer` for exact signature provenance. Module names
compare ordinally ignoring case; foreign module scopes do not bind to
primary-image definitions.
Data-only method maps without that module identity retain their conservative
unresolved result for these aliases.

The body index retains two module-aware maps. Correspondence resolves exactly
once against the declaration map, which admits every MethodDef and therefore
preserves ambiguity across body availability. Traversal, propagation,
allocation, repeated-scan, caller-loop, leverage, fan-in, inbound resolution,
root-path, implementation-profile, and overload consumers then use the body
map or their body-method inventory only to test whether the resolved
declaration has analyzable code. They never rerun correspondence against the
body-only subset. Outbound call trees likewise resolve identity through
declarations and body availability through the body map, so a matching alias
to an abstract, interface, extern, or runtime declaration is `Bodiless`, not
`External`.

Candidate lookup preserves `TypeRef` identity: assembly names compare
ordinally ignoring case, while namespace, type, and member names remain
case-sensitive. Equivalent `AssemblyRef` aliases share the candidate key
without weakening the separate full assembly-identity check; a matching name
alone does not establish that the target belongs to the primary image.

For a resolved same-image invocation, `Implicit` and `Explicit` produce
`Unsafe call` evidence, `None` does not, and `Unavailable` remains visible on
the call without being recast as safe or unsafe evidence. Calls to
`System.Runtime.CompilerServices.Unsafe` remain independent positive body-risk
evidence. External or unresolved targets retain the existing structural
pointer fallback until cross-assembly mixed-model enforcement has an owner.

`SameImageCalls_UseNormalizedCallerContracts`,
`SameImageCalls_LegacyPointerContractRemainsImplicit`,
`SameImageCalls_UnavailableContractRemainsVisible`,
`UnsafeEvidencePresence_UsesNormalizedSameImageCallerContract`, and
`UnsafeEvidence_FindsSignatureOperationsAndUnsafeCalls` gate the contract with
compiler-produced updated and legacy controls plus a generated conflicting-
marker image. The constructed-generic control includes same-signature method
overloads with different generic arity and caller contracts.
`MethodDefinitionMap_VarArgFallbackMatchesRequiredPrefix` gates vararg
correspondence. `UnsafeEvidencePresence_ResolvesPointerFreeLocalTypeReferenceAlias`
and `UnsafeEvidencePresence_DoesNotBindExternalSameNameReference` gate local
alias provenance.
`SameImageCalls_ModuleReferenceAliasesMatchPresence` gates full-index and
presence agreement for matching, case-variant, and foreign module scopes,
including local array-element aliases and a foreign signature under a local
declaring type. The same rows gate downstream overload relationships,
implementation profiles, leverage, and outbound call-tree identity.
`BuildCallTree_ClassifiesModuleAliasBodilessCallee` gates the declaration/body
separation. These tiny generated-image cases are PR-fast, not corpus scans.
`SameImageCalls_AssemblyReferenceAliasesPreserveIdentity` gates equivalent
case-variant assembly aliases against different assembly names, versions,
cultures, and keys, plus case-sensitive namespace/type/member neighbors.
Its ten tiny generated-image rows are PR-fast.
`SameImageCalls_PreserveOpenIdentityAndGenericScope` gates distinct open
overloads that collapse after construction, caller-scoped TypeSpec and optional
vararg arguments, and enclosing method parameters inside function-pointer
signatures. The three
cataloged compiler fixtures stay separate because the public presence query
short-circuits at the first positive: combining their assemblies would mask
negative or malformed-result regressions. Their ordinary build and tiny-image
analysis are PR-fast; the function-pointer control retains structural signature
evidence while rejecting a false `Unsafe call`.
`UnsafeEvidencePresence_RejectsSameImageCorrespondenceAboveBudget` and
`UnsafeEvidencePresence_RejectsAggregateTypeSpecAndMethodSpecWork` gate the
public query's bounded failure paths. The presence matcher examines only the
resolved local declaring type, compares candidate names without materializing
them, decodes only same-name signatures, charges operand and candidate metadata
rows plus signature, type-name, and transitive TypeSpec/MethodSpec work, and
rejects malformed or ambiguous matches. Preliminary classification preserves
raw current-module TypeRef scope when structured decoding rejects the type, so
malformed local metadata cannot be reclassified as an ordinary foreign
reference. A corresponding MethodDef target and the physical
`EvidenceMethod` supplying generic scope must each have `GenericParam` rows
that exactly declare the signature generic parameters by count and zero-based
contiguous index. This applies equally to direct MethodDef tokens, peeled
MethodSpec tokens, and signature-matched MemberRefs. Every `GenericParam` row
visited by the bounded resolver is charged to its aggregate correspondence-row
budget.
`SameImageCalls_MalformedTargetGenericDeclarationDoesNotBind` gates that rule
for full analysis with a well-formed neighboring control, while
`UnsafeEvidencePresence_InvalidTargetGenericDeclarationFailsVisibly` gates
the bounded absence claim.
`SameImageCalls_MalformedDirectTargetGenericDeclarationDoesNotBind`,
`SameImageCalls_MalformedPhysicalCallerGenericDeclarationDoesNotBind`,
`SameImageCalls_GuardRejectedPhysicalCallerRetainsInvalidDeclaration`,
`UnsafeEvidencePresence_InvalidDirectTargetGenericDeclarationFailsVisibly`,
`UnsafeEvidencePresence_InvalidPhysicalCallerGenericDeclarationFailsVisibly`,
and `UnsafeEvidencePresence_ChargesRepeatedTargetGenericParameterRows` gate
the direct token, physical scope, and bounded-work paths.
`UnsafeEvidencePresence_ReusesValidatedLookalikeCallerGenericRows` and
`UnsafeEvidencePresence_RejectsLookalikeCallerGenericRowsAboveBudget` gate
presence-mode caller identity at and beyond the aggregate row boundary without
repeating the validated scope's generic-row traversal.
`UnsafeEvidencePresence_AccountsLookalikeCallerAttributeRowsWithinBudget` and
`UnsafeEvidencePresence_RejectsLookalikeCallerAttributeRowsAboveBudget` gate
the same bounded identity path when extension-method classification traverses
declaring-type custom attributes: each visited attribute row and materialized
attribute type name consumes the aggregate correspondence budget.
`UnsafeEvidencePresence_AccountsLookalikeCallerTypeSpecAttributeNamesWithinBudget`
and
`UnsafeEvidencePresence_RejectsLookalikeCallerTypeSpecAttributeNamesAboveBudget`
gate visible byte-budget failure when a TypeSpec-backed attribute constructor
converts structural decode rejection into an absent type name.
`UnsafeEvidencePresence_AmbiguousLocalDeclaringTypeFailsVisibly` and
`UnsafeEvidencePresence_AmbiguousLocalMethodFailsVisibly` gate visible
ambiguity rather than successful absence.
`MethodDefinitionMap_MalformedConstructedDeclaringTypeArityDoesNotBind`,
`MethodDefinitionMap_OutOfRangeDeclaringTypeParameterDoesNotBind`, and
`UnsafeEvidencePresence_MalformedConstructedDeclaringTypeArityFailsVisibly`
gate exact declaration arity in full and bounded paths.
`MethodDefinitionMap_DeclaringTypeMethodVariableOutsideCallerScopeDoesNotBind`
gates caller-owned generic scope in full correspondence.
`MethodDefinitionMap_AttributedCallUsesPhysicalGenericScope` and
`SameImageCalls_AttributedLocalUsesPhysicalGenericScope` gate physical generic
scope through source attribution and downstream call-tree/leverage consumers.
`MethodDefinitionMap_LiteralPlusSegmentPreservesDeclaredArity` and
`SameImageCalls_LiteralPlusSegmentPreservesGenericArity` gate structured-name
arity and full/bounded agreement for a literal `+` segment.
`UnsafeEvidencePresence_MalformedOpenMemberSignatureFailsVisibly` and
`UnsafeEvidencePresence_MalformedTargetSignatureFailsVisibly` gate visible
bounded failure for malformed reference and candidate signatures.
`UnsafeEvidencePresence_MismatchedTargetGenericDeclarationFailsVisibly` gates
signature-declared generic count against MethodDef generic rows, and
`UnsafeEvidencePresence_MalformedLocalTypeReferenceFailsVisibly` gates raw
current-module scope through TypeRef decode rejection.
`UnsafeLeverage_AmbiguousFallbackDoesNotSelectUnsafeSubset` gates resolution
against the complete declaration population before the unsafe subset is
ranked. `MethodLeverage_ResolvesBeforeFilteringBodilessDeclarations` and
`TopLeverage_DoesNotResolveAgainstBodyOnlySubset` gate the same invariant
across body availability.
`FindNearest_FiltersResolvedBodilessDeclarations` and
`Analyze_TreatsResolvedBodilessTargetsAsOpaque` gate post-resolution body
filtering for caller-loop and allocation composition.
`SameImageCalls_ResolveMethodDefinitionParentVarArg` gates the authoritative
MethodDef-parent form used by same-module vararg call sites, and
`UnsafeEvidencePresence_MalformedTypeSpecParentFailsVisibly` gates malformed
local operand failure. This slice does not define cross-assembly enforcement,
field contracts, inner-unsafe or safe-boundary roles, reconstructed operation
meaning, function-load enforcement, or #5270 CLI/browser composition.

`LibraryBodyStableReceiverGetterClassifier` owns the
narrow PE-backed readonly-field getter judgment and its acquisition-scoped
cache;
`OptimizationOpportunities_StableReceiverGetter_IsClassifiedOnce` gates that
the optimization adapter shares one classification. The classifier does not
own optimization policy or general method-body scheduling.
`LibraryBodyGenericConstraintClassifier` owns generic-constraint presence for
reader-relative async-sibling analysis and primary-image generic-parameter
value-type eligibility for optimization analysis. It does not own sibling
selection or opportunity policy.
`OptimizationOpportunities_GenericObjectEqualsBox_IsReported`,
`OptimizationOpportunities_GenericObjectEqualsNearMiss_NotReported`, and
`OptimizationOpportunities_FindSyncCallsWithAsyncSiblings` gate those
judgments.
`LibraryBodyGeneratedProvenanceClassifier` owns primary-image
source-generated type/enclosing-type classification and its acquisition-scoped
ancestry cache. It consumes the primary resolver's generated-code attribute
judgment, while the assembly builder retains scheduling and the async-source
resolver retains source mapping.
`OptimizationOpportunities_SuppressesSourceGeneratedTypes` and
`OptimizationOpportunities_SourceGeneratedAncestryIsClassifiedOncePerType`
gate suppression and shared classification.
`LibraryBodyMethodReferenceResolver` owns the acquisition-scoped
structural signature and generic-scope identities, canonical
`MemberRef`/`MethodSpec` resolution caches, and their shared assembly work
budgets. The primary resolver adapters and
`LibraryBodyLiftedSourceOwnerResolver` consume that same resolution authority.
The lifted-source-owner resolver owns acquisition-scoped local-function/lambda
owner correlation, memoized owner-body reference evidence, top-level
entry-point authentication, and classic async state-machine type-name
resolution. Its authenticated owner evidence pairs method identity with
method- and enclosing-type generated provenance from the primary metadata
resolver, so malformed-name authentication uses the provenance captured with
the resolved owner instead of re-deriving a narrower attribute subset.
Ultimate-owner traversal preserves that typed provenance for recommendation
suppression instead of projecting identity alone.
`LibraryBodyDeclaredSourceResolver` composes that lifted ownership with async
source mapping. It owns bounded ultimate-owner traversal, declared-method
resolution, the async/lifted/async scoped-evidence expansion sequence, and
final declared-source publication with recoverable diagnostics, without
owning metadata lifetime.
`OptimizationOpportunities_DuplicateMemberRefsResolveStructuralIdentityOnce`,
`OptimizationOpportunities_SharedMemberRefDecodesOnceAcrossOwnerBodies`, and
`LiftedOwnerMemberIdentity_RetainsExactAssemblyReferenceScope` gate cache
sharing and scope-aware identity.
`OptimizationOpportunities_LiftedOwnerBody_IsIndexedOnce`,
`OptimizationOpportunities_ClassicAsyncTypeDefinitionsAreIndexedOnce`, and the
top-level local-function tests gate the lifted-owner caches and execution
mapping.
`LibraryBodyAsyncSourceResolver` owns acquisition-scoped runtime/classic async
source resolution, classic source-to-`MoveNext` mapping, state-machine
attribute authentication, and scoped evidence expansion. It consumes primary
metadata identity and generated-code judgments plus the builder-owned local
type-definition index; full builds prewarm its snapshots before parallel
method analysis.
For value-flow consumers, `AsyncBodyAttribution` projects the exact
Analysis-authenticated source method together with an explicit `Runtime` or
`StateMachine` lowering. Runtime-async evidence retains the source as its own
physical method; state-machine evidence retains a distinct physical execution
method and kickoff source. This keeps lowering independent of identity-equality
sentinels and display names. `SourceMethod` uses the same exact
`MethodIdentity` currency as the attributed sink caller, so consumers can
require identity equality without reconstructing correspondence.
`ResultSinks_PublishRuntimeAsyncBodyAttribution`,
`ResultSinks_PublishStateMachineAsyncBodyAttribution`, and
`ResultSinks_DoNotAttributeSynchronousIteratorBodiesAsAsync` gate the typed
projection, mixed runtime/state-machine assembly behavior, and the close
negative.
After that authentication, Analysis may compose the existing result-sink,
resolved-value, field-access, and suspension facts into
`AsyncStateMachineFieldResultSource`. This preserves direct-call provenance
across one exact compiler state-machine field without relying on generated
field names. The source store must dominate the initial suspension, the result
field must have neither a possible-alias store nor a possible-alias address
escape outside the physical state-machine body, and the whole-assembly
field-access census must be complete. Every recognizable trusted framework
builder suspension must use the same exact local builder field, match the
kickoff source's task/value-task family and result type, pass the current state
machine as its by-ref state-machine argument, and have no control-flow path to
the selected result load. The suspension census enumerates generic,
non-generic, pooled, void, and iterator framework builder families so an
incompatible family rejects the proof instead of disappearing; only the exact
result-compatible generic builder can qualify. Every recognizable
reachable-or-unknown trusted framework `SetResult` completion must have the
exact compatible result signature and use that same builder field, or Analysis
withholds every field source for the body. A reference-type state-machine local
remains the current
instance only when no earlier address use that can reach its selected
registration may replace it. A whole-current-instance indirect write or
unrecognized by-ref escape in any analyzed method on the physical state-machine
type invalidates its candidates. Result-field address escapes inside the body,
custom or spoofed builders, re-entering null cleanup, and every ambiguous
identity, store, census, or reachability case also remain unresolved. Scoped
body indexes withhold this whole-assembly absence proof. The shared
exception-aware block graph conservatively joins a finally handler's possible
leave continuations, so a suspension enclosed by `try`/`finally` may remain
unresolved when that join can reach the result load.
`ResultSinks_WithholdFieldSourceForConservativeFinallyFlow` gates this
fail-closed boundary.
`ResultSinks_PreserveCallSourceAcrossAsyncStateMachineField` and
`ResultSinks_RejectAmbiguousAsyncStateMachineFieldSources` and
`ResultSinks_RejectAddressMutatedReferenceStateMachineArgument`,
`ResultSinks_RejectWholeStateMachineInstanceWrite`,
`ResultSinks_InventoryNonGenericFrameworkBuilderSuspensions`,
`ResultSinks_RejectUnresolvedStateMachineFieldStoreAlias` and
`ResultSinks_RejectUnresolvedExternalFieldStoreAlias`,
`ResultSinks_AuthenticateStateMachineCompletionBuilderField`,
`ResultSinks_SuppressStateMachineFieldSourceForScopedCensus`,
`ResultSinks_SuppressFieldSourceWhenAssemblyCensusIsIncomplete`,
`ResultSinks_SuppressFieldSourceWhenBodyClassificationFails`,
`ResultSinks_WithStateMachineFieldSourceRemainEqualityStable`, and
`AsyncFrameworkResultAndBuilder_RequireTrustedMatchingIdentity` gate that
composition.
`OptimizationOpportunities_ClassicAsyncUsesMoveNextEvidenceCoordinate`,
`AsyncStateMachineAttribute_RequiresFrameworkOrigin`,
`ScopedStateMachineExpansion_RequiresTrustedClassicSource`, and
`OptimizationOpportunities_AsyncStateMachineTypesArePrewarmedBeforeParallelAnalysis`
gate projection, authentication, close-negative scope behavior, and
read-only parallel cache consumption.
Unscoped declared-source publication retains an authenticated immediate async
source when ultimate lifted-owner resolution fails; scoped publication and
ownership-derived recommendations remain fail-closed.
Async execution sources and owner chains reject malformed generated-like names
while ordinary compiler-generated owners, including async owners, retain
established attribution. Rejected identities and incomplete lifted-owner
chains cannot expand scoped acquisition.
Lifted local-function and lambda names require canonical compiler ordinal tails
before they can authenticate an owner. A display-class-hosted lifted method
typically carries one ordinal, while a containing-type or shared-holder method
typically carries two. The pre-Roslyn native C# compiler also emits
one-ordinal lambdas directly on containing types, so ordinal count is not
treated as compiler provenance. Roslyn ordinals are decimal; native-csc
one-ordinal lambda counters may be lowercase hexadecimal. A local-function name
carries exactly one name-to-ordinal delimiter. Generic metadata arity is
removed before that grammar and before async-local or async-lambda
state-machine leaves are classified as owner-required. A special state-machine
leaf whose embedded lifted name or raw outer arity is malformed is `Rejected`:
its physical intrinsic evidence remains visible in an unscoped inspection,
while scoped attribution and recommendations fail closed.
Owner-required admission recognizes an authenticated
`IAsyncStateMachine.MoveNext` `MethodImpl` body even when its physical metadata
name differs from `MoveNext`.
Ultimate-owner traversal distinguishes an incomplete canonical chain
(`Unresolved`) from malformed, ambiguous, cyclic, or invalid relationships
(`Rejected`). Canonical generated bodies without an authenticated claimant,
including Roslyn's async-lambda and async-local-function state-machine
spellings, remain owner-unresolved in every scope. Authored bodies retain
intrinsic findings when malformed or ambiguous state-machine metadata prevents
ownership resolution.
Allocation fanout classifies calls into owner-excluded bodies as opaque before
transitive composition.
`OptimizationOpportunities_UnresolvedLiftedSourceFailsClosedAcrossScopes`
and
`OptimizationOpportunities_UnresolvedAsyncOwnerDoesNotProjectGenericBoxingAcrossScopes`
gate fail-closed allocation-fanout and async-state-machine generic-box
projection while retaining body-intrinsic opportunities, and
`OptimizationOpportunities_OrphanGeneratedBodyFailsClosedAcrossScopes`,
`OptimizationOpportunities_MalformedLiftedStateMachineFailsClosedInScopedViews`,
`OptimizationOpportunities_MethodImplMoveNextMappingControlsMalformedStateMachineScopeAdmission`,
`OptimizationOpportunities_CompiledAsyncLambdaStateMachineIsScopeInvariant`,
`OptimizationOpportunities_CompiledGenericAsyncLocalStateMachineIsScopeInvariant`,
`OptimizationOpportunities_AuthoredIntrinsicRowsSurviveMalformedOwnershipAcrossScopes`,
and `AllocationFanoutTests.Analyze_TreatsExcludedTargetsAsOpaque` gate the
orphan-generated spelling parity, malformed special-state-machine admission,
compiled async-lambda and generic async-local scope parity, authored-intrinsic,
and transitive-fanout close negatives.
`OptimizationOpportunities_UnresolvedLiftedOwnerDoesNotProjectGeneratedBoxing`
gates that boundary for generated generic-box recommendations,
while
`DirectCalls_UnresolvedNestedLiftedSourceRetainsPhysicalCaller` and
`DirectCalls_RecoverableUltimateOwnerFailureRetainsPhysicalCaller` gate
multi-hop caller projection for unresolved and recoverable-failure paths;
`ResolveDeclaredMethod_MalformedLiftedSourceNameFailsClosed` and
`ResolveDeclaredMethod_RejectsMalformedLiftedOrdinalSuffix`,
`ResolveDeclaredMethod_MalformedNestedLiftedOwnerDoesNotBecomeUltimateOwner`
and
`ResolveUltimateDeclaredMethod_PreservesRejectedFirstLiftedHop`
gate canonical generated-name admission and typed rejection preservation at
immediate and intermediate hops.
`ResolveDeclaredMethod_CompiledCapturedAsyncLocalMapsToAuthoredOwner` gates the
one-ordinal local-function form with current Release compiler output.
`ResolveDeclaredMethod_LegacyHexLambdaOrdinalOnContainingHostMapsToAuthoredOwner`
gates legacy native-csc compatibility, including the containing-host form that
prevents ordinal count from serving as provenance.
`ResolveDeclaredMethod_CompilerGeneratedOwnersRetainAttribution`,
`DirectCalls_CompilerGeneratedAsyncOwnerRetainsAttributionAcrossScopes`,
`ResolveDeclaredMethod_TypeGeneratedMalformedAsyncSourceFailsClosedAcrossScopes`,
`ResolveDeclaredMethod_TypeGeneratedMalformedOwnerFailsClosedAcrossScopes`,
`Scopes_MalformedGeneratedOwnersDoNotAdmitStateMachineBodies`, and
`ResolveDeclaredMethod_TerminalMalformedOwnerFailsClosed` plus
`OptimizationOpportunities_TerminalMalformedOwnerFailsClosedWhenEvidenceIsSelected`
gate compiled owner compatibility, generated provenance, async scope admission,
terminal-hop authentication, and the `Rejected`/`Unresolved` recommendation
boundary.
`OptimizationOpportunities_CompilerGeneratedAsyncOwnerIsScopeInvariant`,
`OptimizationOpportunities_TypeGeneratedAsyncOwnerSuppressesSiblingAcrossScopes`,
and
`ResolveUltimateDeclaredMethod_AuthenticatesTopLevelEntryPoint` gate
body-intrinsic scope parity, ordinary async-source suppression, and the
authenticated top-level exception.
`ScopedAsyncAdmission_DoesNotIndexUnselectedTopLevelEntryPoint` gates
metadata-only scope admission before top-level body authentication.
`OptimizationOpportunities_ResolvedNestedLiftedOwnerProjectsUltimateOwnerAcrossScopes`
gates ultimate-owner recommendation and caller attribution across full, method,
and type scopes.
`OptimizationOpportunities_GeneratedUltimateSuppressesNestedBoxAcrossScopes`
and
`OptimizationOpportunities_GeneratedUltimateSuppressesNestedAsyncAcrossScopes`
gate generated ultimate-owner suppression for generic-box and async-sibling
recommendations across those scopes.
`ResolveDeclaredMethod_MapsAsyncOwnerLocalFunctionToOwner` and
`ResolveDeclaredMethod_MapsAsyncOwnerLambdaToOwner` gate compiled Release
async-owner shapes.
`LibraryBodyAsyncSiblingSignatureMatcher` supplies the async-sibling
subsystem's stateless signature decoding, exact identity/comparison, async
return compatibility, optional cancellation matching, and bounded finding
display. `LibraryBodyAsyncSiblingDispatchAnalyzer` owns reader-relative type
relationships, constructed generic projection, virtual-slot and MethodImpl
correspondence, constrained-method suppression, and conservative unknown
handling. It consumes assembly-builder callbacks for synchronized external
type resolution and the shared per-type method-name index.
`LibraryBodyAsyncSiblingAccessibilityAnalyzer` owns CLR member-access,
protected-receiver, friend-assembly identity, and directional nested-private
access policy. It consumes the primary reader and assembly identity plus
dispatch relationship proofs without owning metadata resolution or caches.
`LibraryBodyAsyncSiblingMethodIndex` publishes the synchronized per-type
method-name cache shared by dispatch and candidate analysis.
`LibraryBodyAsyncSiblingCandidateResolver` owns reader-relative synchronous
definition and sibling-candidate resolution, exact-callee caching, inherited
name traversal, ambiguity selection, and source-dependent accessibility and
dispatch filtering. It consumes builder callbacks for synchronized external
resolution and the shared local type-definition index without owning metadata
lifetime. Orchestration, diagnostics, and result ordering remain
assembly-builder policy.
Every positive `sync-call-in-async` opportunity publishes
`AsyncSiblingOpportunityEvidence`: the exact physical `DirectCall` selected by
the orchestrator and resolver-issued async-candidate `MemberRef`. The parent
opportunity's `Method` remains the authenticated async source, keeping the
authored caller distinct from a lowered execution body while retaining the
synchronous callee, call-site coordinate, and callable alternative without
parsing finding text or rerunning candidate resolution. Other opportunity
shapes carry no async-sibling evidence.
`OptimizationOpportunities_FindSyncCallsWithAsyncSiblings`,
`OptimizationOpportunities_InheritedSiblingUsesNearestNameLevel`, and
`OptimizationOpportunities_ClassicAsyncUsesMoveNextEvidenceCoordinate` gate
the same-image, framework, inherited-generic, and lowered-body contracts.
`CallerUnsafeMode_PointerSignatureIsImplicitWhenModuleNotOptedIn`,
`OptimizationOpportunities_AsyncStateMachine_IsAmortized`, and
`Allocations_ClassifiesCrossAndInAssemblyValueTypeNewobj_ByShape` gate
representative identity, cached classification, and token-shape behavior.
`LibraryBodyReferenceMetadataResolver` separately owns
cross-assembly type-definition binding, referenced-image metadata lifetime,
and its registration-keyed cache for that acquisition. It builds on
`AssemblyReferenceBindingPolicy` and `TypeResolutionCatalog` rather than adding
another binding engine. Topic producers may each traverse the canonical decoded
instructions for their own policy; this ownership split does not claim one
instruction traversal overall.
`MethodInstructionFacts` owns the metadata-free local/argument-slot, operand,
and single-branch-target grammar shared by safety and allocation interpretation,
and `CompilerGeneratedNames` owns the unspeakable-name grammar and conservative
containing-type projection shared by allocation escape classification and
optimization-opportunity classification.
`SemanticFactProjection` remains the coordinate projection substrate.
Coordinate scope should be added in Analysis, not rebuilt in CLI code.

### `ILInspector.Research`

Owns offset-keyed overlays that join Analysis (R1) and Decompiler (R2):

- hidden fact registry
- Facts rows
- annotated source
- cost and semantics overlays

Research remains the bridge. Analysis should not depend on Decompiler or
Research.

### Composition layer

Owns composition:

- open or receive the assembly inspection session
- build/reuse one command-configured `LibraryBodyAnalysisService` execution
- retain source attribution and compose cross-assembly caller data
- coordinate metadata, analysis, source acquisition, and Research without
  re-exporting their neutral query surfaces

This layer must sit **above** Metadata, Analysis, Decompiler, and Research. It
must not be `DotnetInspector.Services`, because that project is a lower-level
shared services layer used by package/source/TFM infrastructure. Putting
Research or Decompiler orchestration there would invert the dependency graph and
pull R2 concerns into lower-layer consumers.

Initial implementations may live in `src/DotnetInspect.Cli/Inspectors/` while the
service shape proves out. If this grows beyond CLI-local orchestration, prefer a
new high-level inspection/composition project over expanding
`DotnetInspector.Services`.

This composition layer is the natural home for command-scoped caching and lazy
index construction. Analysis remains the home for query semantics.

### CLI

Owns only:

- parse command options
- map section selection to capabilities and body scope
- open/reuse the method-body session
- call canonical owner queries and compose presentation rows
- render the resulting shape
- write command-line diagnostics for invalid user input

The CLI may depend on `LibraryBodyIndex` only for compatibility consumers
named by the migration plan. New and migrated sections consume focused
Analysis result types. The CLI must not copy Analysis classification, matching,
or aggregation rules into formatters.

## Relationship to assembly inspection

The assembly inspection session answers: "what assembly am I inspecting, how do
I open it once, and what assembly-level scanners are requested?"

The method-body session answers: "which Analysis body scope and capabilities
does this command need, which assembly produced the evidence, and which sibling
assemblies participate in caller composition?" Member and IL-coordinate
selectors remain command inputs applied to the canonical owner queries.

Method-body inspection can start from today's `dllPath` for early slices, but the
target constructor consumes the assembly session or its `ResolvedAssemblyReference`,
not another string-only seam. That assembly session is **no longer pending**:
`AssemblyInspectionSession` and its `AssemblyImage` shipped in #2156–#2162 (see the
[Assembly Inspection Query Model](assembly-inspection-query.md)), so the method-body
session can consume the real type from the start rather than a placeholder.

One caveat on "open the image once": true single-open convergence — sharing the
assembly's `AssemblyImage` with `PdbContext`, `MetadataSource`, and `LibraryBodyIndex`
— depends on the shared-PE-owner composition that is **still pending** (the `PdbContext`
/ `MetadataSource` work called out as Symptom 3 in the assembly design). Until it lands,
early method-body slices will still open their own readers for the decompiler/analysis
paths; the single-open convergence arrives with that composition, not this doc.

This depends on the sibling assembly acquisition design in
[Assembly Inspection Query Model](assembly-inspection-query.md). Treat the
two docs as one program of work under #2122: assembly inspection owns resolution
and PE lifetime; Analysis owns method-body semantics inside the resolved
assembly; method-body composition owns command policy and multi-assembly joins.
In request terms, the CLI builds one `InspectionQuery` whose `Target.Selector`
is a `MemberSelector` / `ILCoordinateSelector`, then maps that selector and the
requested facets onto the relevant owner queries.

## Migration

Move in reviewable slices.

1. **Define owner queries.** Keep method/coordinate allocation, safety, cost,
   calls, and graph semantics in Analysis; keep metadata, source, decompiler,
   and overlay semantics in their owning layers.
2. **Centralize command policy.** Use `MethodBodyInspectionSession.Open` for
   capability flags, body scope, source attribution, and one service execution
   per command.
3. **Migrate section inputs.** In the sequence owned by
   [Library Body Analysis Service](library-body-analysis-service.md), make
   library sections consume focused Analysis result types and remove their
   `BodyIndex` dependency in the same slice.
4. **Raise remaining semantic construction.** Move any classification,
   matching, or aggregation still implemented in CLI code to its canonical
   owner. Thin CLI row mapping is presentation, not a second semantic surface.
5. **Converge selectors.** Route member and `library coordinate` selection
   through shared metadata/Analysis query identities while preserving their
   command-specific error behavior.
6. **Unify overlays and lifetime.** Compose Research/source/decompiler facts
   above the owner queries, and adopt the shared PE owner when that pending seam
   lands.

The command-owned path-backed acquisitions for pairwise `diff` body-signal
comparison, implementation comparison, and PDB-source target indexing remain
named compatibility consumers. Diff History Analysis uses shared PackageHouse
cell inspection and the method-body session path.
Separate `diff` phases may retain distinct executions and capability policies;
`diff --finding analysis.*` still delegates path-backed acquisition to
`ResearchDiff` until its focused migration.

## Acceptance tests for the architecture

- Adding a new method-body fact requires changing one producer/service, not both
  `member` and `library coordinate`.
- Adding a neutral Analysis query does not require a
  `MethodBodyInspectionSession` forwarding method.
- One command performs one service execution with the requested capability and
  body scope even when several migrated sections consume different result
  types.
- A migrated section accepts no `LibraryBodyIndex` and does not run unrelated
  producers.
- Cross-assembly caller results retain source attribution.
- Member-level and coordinate-level allocation/safety/cost rows agree for the
  same method and offset.
- CLI formatters do not reimplement Analysis classification, matching, or
  aggregation semantics.
- Analysis remains SRM-only, NativeAOT-friendly, Roslyn-free, and free of
  decompiler dependencies.
- Research remains the only R1/R2 overlay bridge.

## Open questions

- Should missing facts be represented as empty lists, diagnostics, or
  unavailable-facet reasons? `member` sections often render empty-state notes;
  `library coordinate` returns command errors for required contexts while its
  bare child requires a useful bounded result.
- How should caller-scope assembly resolution move behind assembly inspection
  while source attribution and cross-index composition remain session concerns?
- Should `PdbSource` be a method-body facet or remain a SourceLink service
  call that CLI composition joins? It is a facet from the user's perspective,
  even if SourceLink owns the fetch.
