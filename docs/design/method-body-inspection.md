# Method Body Inspection

> Design north-star for raising `member` body sections and `library --il-offset`
> onto one service model. This complements the assembly acquisition/session seam
> in the [assembly inspection query model](assembly-inspection-query.md):
> assembly inspection opens and identifies an assembly; method-body inspection
> explains one method body or one IL coordinate inside it.

## Problem

`member` and `library --il-offset` now expose peer facts about method bodies:

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

`library --il-offset` started as a one-off source lookup. Its command helper
grew to resolve member, instruction, exception, callsite, return-address,
allocation, safety, and cost context, build CLI model rows, and own fallback
opcode heuristics. It was no longer just a source query.

Both paths have useful pieces, but neither is the target architecture:

- `member` uses the normal command pipeline, but its formatter constructs facts.
- `library --il-offset` needs a thin command query over a Research-owned projection.
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
  coordinate, and capability flags — never a path, `PEReader`, or command options.
- `ILOffsetProjectionProducer.Produce` owns Metadata + Instructions + Analysis +
  SourceLink composition and returns `ILOffsetProjectionOutcome`.
- `ResearchViews.ProjectILOffset` forwards directly to the producer.
- `ILOffsetQuery` retains only CLI parsing, capability selection, symbol
  acquisition, failure/exit handling, and producer invocation.

This establishes the migration pattern for the existing member projection:
top-level contracts, a focused `MemberProjectionProducer`, and a thin
`ResearchViews.ProjectMember` forwarder.

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

This is the `library --il-offset` shape. It should not be a separate command
architecture. It is another selector for the same method-body inspection
pipeline.

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
| `OriginalSource` | fetched source slice | Metadata / SourceLink |

The important rule: a facet has one canonical owner. CLI sections such as
`Allocation Facts`, `Allocation Context`, `Facts`, or `Annotated Source` may
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
    public LibraryBodyIndex BodyIndex { get; }
    public string SourceName { get; }

    public static MethodBodyInspectionSession Open(
        string assemblyPath,
        IAssemblyReferenceResolver? resolver = null,
        bool includeAllocations = true,
        bool includeOpportunities = true,
        IReadOnlySet<int>? bodyScope = null,
        Func<TypeRef, bool>? bodyTypeScope = null);

    public CallTreeNode CallerTree(
        int methodToken,
        IReadOnlyList<MethodBodyInspectionSession> scopes);

    public ImmutableArray<CallerEdge> CallerEdges(
        int targetToken,
        IReadOnlyList<MethodBodyInspectionSession>? scopes = null);
}
```

The exact method names can change. The boundary should not:

- `Open` captures command-selected capability and body-scope policy
- one session builds and reuses one Analysis index per command
- neutral Analysis queries stay on `LibraryBodyIndex` or Analysis projections
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
- SourceLink/source-line coordinate resolution
- original-source body/slice acquisition helper APIs

It stays SRM-only and does not load inspected assemblies.

### `ILInspector.Analysis`

Owns IL analysis facts:

- direct calls and return addresses
- callers and caller graphs
- allocation, safety, and cost facts
- unsafe operations and unsafe API evidence

The existing `LibraryBodyIndex` and `SemanticFactProjection` are the right
substrates. Coordinate scope should be added there, not rebuilt in CLI code.

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
- build/reuse one command-configured `LibraryBodyIndex`
- retain source attribution and compose cross-assembly caller data
- coordinate metadata, analysis, source acquisition, and Research without
  re-exporting their neutral query surfaces

This layer must sit **above** Metadata, Analysis, Decompiler, and Research. It
must not be `DotnetInspector.Services`, because that project is a lower-level
shared services layer used by package/source/TFM infrastructure. Putting
Research or Decompiler orchestration there would invert the dependency graph and
pull R2 concerns into lower-layer consumers.

Initial implementations may live in `src/dotnet-inspect/Inspectors/` while the
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

The CLI may depend on `LibraryBodyIndex` as an Analysis query type. It must not
copy Analysis classification, matching, or aggregation rules into formatters.

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
   capability flags, body scope, source attribution, and one index build per
   command.
3. **Delete neutral forwarders.** Let CLI consumers query `BodyIndex` and
   Analysis projections directly instead of mirroring the Analysis API on the
   session.
4. **Raise remaining semantic construction.** Move any classification,
   matching, or aggregation still implemented in CLI code to its canonical
   owner. Thin CLI row mapping is presentation, not a second semantic surface.
5. **Converge selectors.** Route member and `library --il-offset` selection
   through shared metadata/Analysis query identities while preserving their
   command-specific error behavior.
6. **Unify overlays and lifetime.** Compose Research/source/decompiler facts
   above the owner queries, and adopt the shared PE owner when that pending seam
   lands.

## Acceptance tests for the architecture

- Adding a new method-body fact requires changing one producer/service, not both
  `member` and `library --il-offset`.
- Adding a neutral Analysis query does not require a
  `MethodBodyInspectionSession` forwarding method.
- One command builds one index with the requested capability and body scope.
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
  `library --il-offset` currently returns command errors for required contexts.
- How should caller-scope assembly resolution move behind assembly inspection
  while source attribution and cross-index composition remain session concerns?
- Should `OriginalSource` be a method-body facet or remain a SourceLink service
  call that CLI composition joins? It is a facet from the user's perspective,
  even if SourceLink owns the fetch.
