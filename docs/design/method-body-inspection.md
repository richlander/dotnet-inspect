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

`library --il-offset` started as a one-off source lookup. `ILOffsetSourceQuery`
now resolves member, instruction, exception, callsite, return-address,
allocation, safety, and cost context, builds CLI model rows, and owns fallback
opcode heuristics. It is no longer just a source query.

Both paths have useful pieces, but neither is the target architecture:

- `member` uses the normal command pipeline, but its formatter constructs facts.
- `library --il-offset` has a standalone query/helper file that should disappear.
- Both paths construct overlapping method-body facts differently.

## Target

The shared abstraction is **method body inspection**:

```text
ResolvedAssemblyReference
  -> AssemblyInspectionSession
      -> MethodBodyInspectionSession
          -> MemberQuery
          -> ILCoordinateQuery
          -> MethodBodyInspection
```

The command chooses a selector and requested facets. The service returns
section-ready inspection facts. Renderers format those facts; they do not open
indexes, classify opcodes, or assemble semantic rows.

## Query shapes

There are two entry points into the same method-body system.

### `MemberQuery`

Identifies a method by API identity:

```csharp
public sealed record MemberQuery(
    string TypeName,
    string MemberName,
    int OverloadIndex,
    bool PublicOnly);
```

This is the `member` command shape. It is selected from `ApiType`/`ApiMember`
and stable member selectors.

### `ILCoordinateQuery`

Identifies a method-body selector by metadata coordinates:

```csharp
public sealed record ILCoordinateQuery(
    int MethodToken,
    int ILOffset);
```

This is the `library --il-offset` shape. It should not be a separate command
architecture. It is another selector for the same method-body inspection
pipeline.

## Facets

Both query shapes request the same facets. A facet is a product capability, not a
CLI section name. Section selection maps to facets at the command boundary.

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

## Service shape

```csharp
public sealed class MethodBodyInspectionSession
{
    public MethodBodyInspection Inspect(MemberQuery query, MethodBodyFacetSet facets);
    public MethodBodyInspection Inspect(ILCoordinateQuery query, MethodBodyFacetSet facets);
}

public sealed record MethodBodyInspection(
    MethodBodyIdentity Method,
    InstructionFact? Instruction,
    SourceLocationFact? Source,
    IReadOnlyList<ExceptionRegionFact> ExceptionRegions,
    IReadOnlyList<CallSiteFact> Calls,
    IReadOnlyList<CallerSiteFact> Callers,
    CallGraphFact? CallGraph,
    CallGraphFact? CallerGraph,
    IReadOnlyList<UnsafeFact> Unsafe,
    IReadOnlyList<AllocationFact> Allocations,
    IReadOnlyList<SafetyFact> Safety,
    IReadOnlyList<CostFact> Cost,
    IReadOnlyList<HiddenFact> HiddenFacts,
    SourceBodyFact? DecompiledSource,
    SourceBodyFact? AnnotatedSource,
    SourceBodyFact? OriginalSource);
```

The exact record names can change. The boundary should not:

- input is a member identity or IL coordinate plus requested facets
- output is a product-model inspection shape
- the CLI does not open `LibraryBodyIndex`, `PdbContext`, `MetadataSource`, or
  `PEReader`
- the formatter does not construct facts

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
- build/reuse `LibraryBodyIndex`
- coordinate metadata, analysis, source acquisition, and Research
- return `MethodBodyInspection`

This layer must sit **above** Metadata, Analysis, Decompiler, and Research. It
must not be `DotnetInspector.Services`, because that project is a lower-level
shared services layer used by package/source/TFM infrastructure. Putting
Research or Decompiler orchestration there would invert the dependency graph and
pull R2 concerns into lower-layer consumers.

Initial implementations may live in `src/dotnet-inspect/Inspectors/` while the
service shape proves out. If this grows beyond CLI-local orchestration, prefer a
new high-level inspection/composition project over expanding
`DotnetInspector.Services`.

This composition layer is the natural home for caching and lazy facet execution.

### CLI

Owns only:

- parse command options
- map section selection to facets
- call the service
- render the returned shape
- write command-line diagnostics for invalid user input

## Relationship to assembly inspection

The assembly inspection session answers: "what assembly am I inspecting, how do
I open it once, and what assembly-level scanners are requested?"

The method-body session answers: "inside that assembly, which method body or IL
coordinate am I explaining, and which facets are requested?"

Method-body inspection should be able to start from today's `dllPath` while the
assembly session design settles, but the target constructor should consume the
assembly session or its `ResolvedAssemblyReference`, not introduce another
string-only seam.

This depends on the sibling assembly acquisition design in
[Assembly Inspection Query Model](assembly-inspection-query.md). Treat the
two docs as one program of work under #2122: assembly inspection owns resolution
and PE lifetime; method-body inspection owns member/coordinate facts inside the
resolved assembly.

## Migration

Move in reviewable slices.

1. **Design the shared model.** Land this doc and agree on the facet ownership
   table.
2. **Add a path-backed adapter.** Add `MethodBodyInspectionSession.Open(path)`
   or an internal composition-layer equivalent that can be swapped later for
   `AssemblyInspectionSession`. Keep this above Metadata, Analysis, Decompiler,
   and Research; do not add Research/Decompiler dependencies to
   `DotnetInspector.Services`.
3. **Raise semantic facts first.** Move `ILOffsetSourceQuery` allocation,
   safety, and cost construction to Analysis-backed method/coordinate APIs.
   Preserve the current instruction-only fallback behavior deliberately or
   remove it with an explicit behavior note.
4. **Raise metadata coordinate facts.** Move member, instruction, exception,
   callsite, and return-address context behind the shared method-body service.
5. **Delete `ILOffsetSourceQuery`.** Route `library --il-offset` through the
   service with `ILCoordinateQuery`.
6. **Raise member sections.** Move the fact construction currently in
   `ApiOutputFormatter.PopulateIndexSections` into the service. Leave the
   formatter as projection/rendering only.
7. **Unify overlays.** Keep `MemberCodeProvider` only as an adapter if needed,
   then route source/IL/decompiler/Research-backed sections through the same
   method-body session.

## Acceptance tests for the architecture

- Adding a new method-body fact requires changing one producer/service, not both
  `member` and `library --il-offset`.
- `library --il-offset` has no bespoke query/helper file.
- `ApiOutputFormatter` does not open `LibraryBodyIndex` or `PdbContext`.
- Member-level and coordinate-level allocation/safety/cost rows agree for the
  same method and offset.
- The CLI has no `System.Reflection.Metadata` or
  `System.Reflection.PortableExecutable` dependency for method-body inspection.
- Analysis remains SRM-only, NativeAOT-friendly, Roslyn-free, and free of
  decompiler dependencies.
- Research remains the only R1/R2 overlay bridge.

## Open questions

- Should `MethodBodyInspection` return all facts as canonical records with the
  CLI adapting to existing view rows, or should the service return
  view-compatible section records? Prefer canonical product records first, with
  thin render adapters.
- Should missing facts be represented as empty lists, diagnostics, or
  unavailable-facet reasons? `member` sections often render empty-state notes;
  `library --il-offset` currently returns command errors for required contexts.
- How much of caller-scope resolution belongs in this service versus the
  command? The scope is user input, but resolving assemblies for the scope is a
  service concern.
- Should `OriginalSource` be a method-body facet or remain a SourceLink service
  call that the method-body service composes? It is a facet from the user's
  perspective, even if SourceLink owns the fetch.
