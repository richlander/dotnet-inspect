# Decompiler architecture

`ILInspector.Decompiler` recovers typed method bodies from IL and projects them
as C#, IL views, and source-coordinate data. CLI and browser consumers share
that engine; test executables and diagnostic harnesses exercise the same
implementation.

This is the decompiler's implementation and testing map, complementary to the
system [Architecture](architecture.md). It explains where work belongs and how
evidence flows, not a new specification for every component. Focused documents
retain their contracts:

| Document | Owns |
| --- | --- |
| [Decompiler design](decompiler.md) | Pipeline design rationale and composition policy. |
| [IR and importer design](decompiler-ir.md) | Import, symbolic types, and mutable-tree contracts. |
| [Decompiler taste](decompiler-taste.md) | Recovery and spelling policy. |
| [Decompiler quality](decompiler-quality.md) | Quality strategy, oracle interpretation, and target selection. |
| [Correctness pipeline](decompiler-correctness-pipeline.md) | Evidence levels, test selection, gates, and change-specific requirements. |
| [Raise-work discipline](decompiler-raise-discipline.md) | Lowering recognition, ownership proofs, and decline boundaries. |
| [Harness reference](../tools/DecompilerHarness/README.md) | Diagnostic and measurement commands. |

The named classes and paths below describe current implementation. The ordered
pass registry, project files, test host, and workflows are the executable
inventories; this page deliberately does not copy their complete lists.

## Position in the product

The engine is a producer and composer, not a host and not a replacement for
Metadata or Analysis. Its
[project references](../src/ILInspector.Decompiler/ILInspector.Decompiler.csproj)
connect it to these neighboring owners:

| Neighbor | What the decompiler consumes | What remains decompiler work |
| --- | --- | --- |
| `ILInspector.Metadata` and `MetadataPrimitives` | Metadata identities, API shapes, binding/resolution services, and SRM mechanics. | Import-time materialization into its own symbolic method/type model. |
| `ILInspector.Instructions` | IL decoding and shared instruction/block mechanics. | Evaluation-stack simulation and IL-to-IR semantics. |
| `ILInspector.ControlFlow` | Graph, dominance, and dataflow kernels. | Adapting IR terminators to edges and choosing structured replacements. |
| `CSharpText` | Model-free textual grammars, identifiers, and layout. | Body recovery and model-bound expression spelling. |
| `ILInspector.CSharp` | Typed declarations, signatures, type shells, and body/artifact contracts. | Supplying recovered bodies and declaration-relevant body facts. |
| `ILInspector.Findings`, `ILInspector.ILDiff`, and `ILInspector.Text` | Observation/comparison contracts and IL/text comparison services. | Decompiler-specific projections and C# structural comparison. |

Analysis independently produces IL-body evidence. Research composes Analysis
and Decompiler results rather than asking either producer to own the other's
truth. The [inspection-layer contract](design/inspection-layers.md) governs
that separation.

The existing product constraints remain SRM-based inspection,
NativeAOT-friendly reusable code, Browser/Wasm compatibility, and no execution
of inspected assemblies. Roslyn-assisted compilation and syntax oracles belong
to test/tool paths. See [dependency policy](dependency-policy.md) for repository
dependency enforcement; this document does not add a platform exception.

## Per-method flow

```text
Explicit assembly + MethodDef + binding policy + optional symbols
                              |
                  MetadataSource / MetadataContext
                              |
                     MethodImporter.Import
                  ImportedMethod + MethodBody
                              |
                       IrImporter.Build
                IrFunction: typed blocks and nodes
                              |
                 IrPasses.Default / PassContext
                   raised, still typed IR tree
                              |
                        CSharpPrinter
              DecompilerResult + optional range map
                    /                         \
         MemberBodyProducer              Research composition
       typed member/type source       C# + IL + independent facts
```

[`MetadataSource`](../src/ILInspector.Decompiler/Pipeline/MetadataSource.cs)
holds the target PE, metadata, and optional PDB readers.
[`MetadataContext`](../src/ILInspector.Decompiler/Pipeline/MetadataContext.cs)
provides a shareable referenced-assembly lifetime under the caller's binding
policy. Source acquisition and authorization remain upstream responsibilities;
the pipeline does not decide a host's network policy.

[`MethodImporter`](../src/ILInspector.Decompiler/Pipeline/MethodImporter.cs)
materializes signatures, locals, IL bytes, exception regions, and available
debug facts. [`IrImporter`](../src/ILInspector.Decompiler/Pipeline/Ir/IrImporter.cs)
decodes instructions, simulates the evaluation stack, creates blocks and stack
slots, resolves import-time facts, and records IL origins on the typed tree.
`IrImporter.Import` is the higher-level import entry; `Build` operates on the
materialized method.

[`IrPasses`](../src/ILInspector.Decompiler/Pipeline/Passes/IrPass.cs) runs named
`IIrPass` implementations in explicit order. Broadly, early normalization and
expression recovery prepare control-flow structuring; later passes recover
source idioms, synthesized-body constructs, locals, and final coercions or
spellability diagnostics. This is not a strict one-pass-per-phase partition:
some transforms repeat after another transform exposes new opportunities.
Read the registry and its ordering comments before inserting a pass.

[`PassContext`](../src/ILInspector.Decompiler/Pipeline/PassContext.cs) carries
diagnostics, options, stepping, and explicit cross-method capabilities.
Lambda, local-function, iterator, and async work may need synthesized sibling
bodies; it is not always a single-body operation. Import and reconstruction
remain bounded by the supplied capabilities and evidence. In particular, the
[classic async inverse design](design/classic-async-reconstruction.md) describes
a target proof contract, not a claim that the current classic pass implements
all of it.

[`CSharpPrinter`](../src/ILInspector.Decompiler/Pipeline/Ir/CSharpPrinter.cs)
offers raised, lowered, and already-transformed printing paths. `PrintRaised`
runs the default passes and requested style lenses before printing; `Print`
prints the supplied tree. Printer options can affect spelling, naming, and
explicitly byte-divergent taste choices. Preserve the effective options when
comparing output.

[`MemberBodyProducer`](../src/ILInspector.Decompiler/MemberBodyProducer.cs)
is the reusable body/member/type composition entry. It adapts recovered body
facts to CSharp-owned bodies and declaration rendering rather than having each
host reassemble signatures and bodies.

## Implementation regions

Paths in this table are relative to
[`src/ILInspector.Decompiler`](../src/ILInspector.Decompiler).

| Region | Responsibility and starting points |
| --- | --- |
| Project root | Results and diagnostics (`DecompilerResult`), member/type production (`MemberBodyProducer`), body search, annotated documents, Findings, and C# comparison contracts. |
| `Pipeline/` | Metadata sessions, method import, `TypeRef`, `PassContext`, CFG adapters, type/conversion evidence, and stepping. |
| `Pipeline/Ir/` | `IrNode`/`IrFunction`, stack importer, printer partials, naming, IL projections, runtime invariants, and printed ranges. |
| `Pipeline/Passes/` | Individual transforms and their ordered registry in `IrPass.cs`; lowering and closure coverage ledgers. |
| `Pipeline/Facts/` | Additive lowering-coverage sidecars: mechanisms, proof prerequisites, and recovery coverage. These are not Analysis's runtime body observations. |
| `Pipeline/InverseArchitecture/` | Forward-lowering/inverse annotations and executable assumptions used by diagnostic tooling. |
| `Annotations/` | Decompiler-local classifiers, IL-origin anchoring, carets, and annotation layout. |

Directories organize the implementation; they are not separate project or
namespace boundaries. The
[substrate design](design/decompiler-substrate.md) explains shared proof atoms
such as `MemberIdentity`, `GeneratedCodeIdentity`, `PlaceIdentity`,
`ReferenceOwnership`, and `ProtectedRegionControlFlow`. Each pass composes
those atoms with its own lowering discriminator; a shared predicate does not
itself prove that a source idiom was recognized.

## Currencies, lifetime, and output

| Currency | Meaning and boundary |
| --- | --- |
| `MetadataSource`, `MetadataContext` | Live reader/resolution scopes. Keep them alive while import, nested recovery, or another borrowed operation needs them. |
| `ImportedMethod`, `MethodBody` | Materialized method/signature/IL/debug facts rather than a live reader. |
| `TypeRef`, `MethodRef` | Decompiler symbolic types and callees. They are not interchangeable with Analysis types or exact Metadata definition identities. |
| `IrFunction`, `IrNode` | Mutable per-import tree with parent/child links and typed values. Rewrites replace nodes; local and stack-slot meaning is body-scoped. |
| `DecompilerResult` | Materialized output, diagnostics, fidelity grade, effective options, and body facts such as constructor initialization and async/unsafe requirements. |
| `PrintedRangeMap` | Ephemeral mapping from IR object references to emitted ranges. |
| `PrintedBodyMap`, `AnnotatedSourceDocument` | Detached text extents, document-local node IDs, regions, facts, and available IL/physical-method provenance. Suitable for host composition and serialization. |

The [representation contract](design/type-member-api-representation.md) owns
identity and correspondence across representations. Equal names, node IDs, or
offsets alone do not identify the same member or prove correspondence between
two source documents. `CSharpBodyDiff.IssueCorrespondence` uses product-issued
origin evidence for structural comparison; unsupported or ambiguous matches
remain explicit.

An unsupported construct and a failed operation are distinct. The IR can retain
explicit unsupported nodes and diagnostics; `DecompilerResult` grades the
available projection as `Full`, `Partial`, `StructuredOnly`, `IlOnly`, or
`Failed`. The member-body production seam also distinguishes complete, absent,
and failed outcomes. Consumers must preserve the applicable result contract
rather than interpreting missing text as a successful empty body.

`Full` is a decompiler projection grade, **not an independent compiler verdict
or a guarantee of fully raised idiomatic C#**. Validity, preferred syntax shape,
and compile-back fidelity are measured separately below.

### Projections and consumers

[`IlProjection`](../src/ILInspector.Decompiler/Pipeline/Ir/IlProjection.cs)
provides raw, typed, structured, and annotated IL views. Typed import traces
reuse the importer rather than introducing a second evaluation-stack model.
`IrPasses.RunWithStages` and `RunWithSteps` expose intermediate trees and
rewrites; the harness consumes them through stage dumps.

| Consumer | Integration |
| --- | --- |
| `DotnetInspector.Queries` | Source queries compose acquired source and decompiled fallback; `BodyShapesQuery` delegates exact syntax-kind searches to `BodyShapeSearch`. |
| `ILInspector.Research` | `ResearchViews` joins producer-owned facts with printed C#/IL provenance and constructs annotated-source output. Decompiler owns the portable document types, not the complete cross-domain operation. |
| CLI | `MemberCodeProvider`, queries, and section/output adapters expose source, IL, annotated views, and comparisons. Some direct import/printer composition remains in the host. |
| Browser | `prototypes/inspect-web/engine.SourceExports` consumes Queries/Research and exports portable source documents rather than mutable IR. |
| Tests and harnesses | Exercise product import, passes, printing, body production, and comparison; add independent compiler/oracle observations. |

C# and annotated-source text are language artifacts with exact coordinates, so
their production printers are deliberately distinct from Markout report
rendering. CLI/report adapters and browser-native viewers present the resulting
typed artifacts; they do not establish body identity or fidelity from layout.

## Testing architecture

The [correctness pipeline](decompiler-correctness-pipeline.md) owns which
evidence a change requires. The following map locates that evidence and keeps
its different questions separate.

### Inputs and test hosts

| Location | Role |
| --- | --- |
| [`src/ILInspector.Decompiler.Tests`](../src/ILInspector.Decompiler.Tests) | Executable xUnit suite: importer, IR, passes, proof atoms, printer, annotation/document contracts, body production, and harness regression tests. |
| [`fixtures/decompiler`](../fixtures/decompiler) | Independently compiled inputs where compiler features, module attributes, assembly identity, or cross-assembly relationships matter. |
| [`tests/DotnetInspector.FixtureInfrastructure`](../tests/DotnetInspector.FixtureInfrastructure) | `FixtureCatalog` registration and resolution shared by tests and harnesses. |
| [`tools/DecompilerHarness`](../tools/DecompilerHarness) | Single-method diagnostics, compile-back, generated-fixture catalog, source oracles, and corpus measurements. |
| [`tools/RoundTripCompilation`](../tools/RoundTripCompilation) | Tools-side compilation and comparison support used by harness/tests. |
| [`tools/HarnessReportProtocol`](../tools/HarnessReportProtocol), [`tools/HarnessReportDiff`](../tools/HarnessReportDiff) | Stored typed reports and goal-aware before/after report comparison. |
| Adjacent suites | Metadata/Analysis/IL round-trip owner tests; Queries, CLI, and browser-engine tests for their integration boundaries. |

The decompiler test project links selected harness source files so xUnit gates
exercise the same measurement implementation. It also builds the harness
executable for process-level tests. Roslyn belongs to this tools/test graph,
not to the production decompilation algorithm.

[Fixture governance](fixture-governance.md) separates independently compiled
inputs from feature-focused `*Samples.cs` files compiled into a test assembly.
Use compiler-produced specimens to pin real lowerings and hand-built IR for
precise boundary/decline cases. Keep the intended compiler configuration:
changing Debug/Release or feature switches can change the IL being inspected.
Build cataloged inputs through the solution rather than inventing paths to
missing fixture binaries.

### Evidence layers

| Question | Representative implementation | Limit |
| --- | --- | --- |
| Did import/rewrite preserve the tree contract? | `IrInvariantCheckTests`, `IrInvariantsHostContractTests`, importer and pass tests. | Structural/declared-slot integrity does not prove C# validity or behavior. |
| Was the intended lowering recognized without consuming a near miss? | Pass tests and substrate-atom tests; compiler-produced positive and decline fixtures. | A local shape proof is not corpus coverage. |
| Is the output valid and at the desired C# altitude? | `ValidityCheck`, `TypeSourceCheck`, `TypeBindCheck`, `IdiomShapeScorecardTests`. | Binding and preferred syntax do not establish compile-back fidelity. |
| Do annotations agree with IL witnesses? | `AnnotationCheck`, `AnnotationGateTests`, printed-range/document tests. | Annotation correctness is separate from correctness of the C# body. |
| Does the rendered body compile back to the contract body? | `FidelityCheck`, ReturnToSender, `FidelityGateTests`, `LoweredFidelityGateTests`. | Uncheckable methods remain named outcomes; current comparison is EH-blind and is not semantic equivalence. |
| Does output correspond to independently acquired authored source? | Source-oracle manifest, authored-source/rebuild harness modes and tests. | Source correspondence and compile-back are independent; PDB mapping is not fault-attribution authority. |
| What moved across real inputs, especially changed methods? | Corpus sensor, render A/B, corpus delta, changed-method fidelity. | Aggregate gains cannot establish correctness of an unchecked changed method. |

Runtime `IrInvariants` checks run in Release as well as Debug. They are enabled
by default; the shipped CLI opts out unless an explicit environment setting
overrides it. Direct pass-unit calls need their own `CheckInvariant()` because
they bypass the runner's per-pass hook. The
[invariant host contract](decompiler-correctness-pipeline.md#ir-invariant-checks-hosts-levels-and-fixtures)
defines the structural and semantic levels.

The current compile-back `Exact` result is bounded by its
[comparison contract](decompiler-correctness-pipeline.md#vocabulary), including
normalization and coverage limits. Keep projection grade, validity, source
correspondence, compile-back result, and capture failures separate in reports.
A harness must compile product-constructed artifacts, not repair output until
it compiles; [artifact ownership](decompiler-correctness-pipeline.md#harness-artifact-ownership)
defines that boundary and the still-unimplemented receipt strengthening.

### Running a focused slice

Run from the repository/worktree root with the
[repository development SDK](../README.md#repository-development-sdk). Inspect
`dotnet --list-sdks` and `dotnet --version` first. Use Release and the executable
test host via `dotnet run`, not `dotnet test`.

```bash
# Build the product, test hosts, and cataloged fixture binaries.
dotnet build dotnet-inspect.slnx -c Release

# Discover the current named lanes.
dotnet run --project src/ILInspector.Decompiler.Tests -c Release --no-build -- \
  --gate list

# Iterate on one pass's positive and decline cases.
dotnet run --project src/ILInspector.Decompiler.Tests -c Release --no-build -- \
  -class ILInspector.Decompiler.Tests.UsingStatementPassTests

# Run the fidelity area, including its slow tests.
dotnet run --project src/ILInspector.Decompiler.Tests -c Release --no-build -- \
  --gate fidelity
```

The current decompiler host's
[`Program.cs`](../src/ILInspector.Decompiler.Tests/Program.cs) expands `--gate`
before invoking xUnit. `Speed=Slow` is a cost classification; `Area` selects
functional slices. Area tags are not an exhaustive inventory of all tests.
The decompiler still uses its transitional native selectors such as `-class`
and `-trait`; do not assume another suite's MTP filter spelling applies.
The [test-host migration contract](design/xunit-test-host.md) and
[gate reference](decompiler-correctness-pipeline.md#--gate-preset-flag-discoverable-trait-bundles)
own the transition and full preset list.

### CI and broader evidence

| Lane | Current composition |
| --- | --- |
| PR fast tests | `ci.yml` excludes `Speed=Slow` from the decompiler suite. |
| PR decompiler gates | Change-selected `decompiler-gates` runs `--gate pre-merge`, independently discovers selected cases, and checks execution evidence against the expected classes/cases and pinned known-red docket. |
| PR quick corpus | Bounded corpus measurement in `ci.yml`; not the full corpus or all changed-method fidelity evidence. |
| Deep Inspect test lane | Runs `--gate no-corpus`, including slow tests outside the Corpus area. |
| Deep Inspect corpus lane | Separately runs `--gate corpus`; census/feature/authored-corpus jobs provide other targeted measurements. |

The executable wiring lives in [PR CI](../.github/workflows/ci.yml),
[Deep Inspect](../.github/workflows/deep-inspect.yml), and
[`eng/check-decompiler-gate.cs`](../eng/check-decompiler-gate.cs).
The [pre-merge gate contract](decompiler-correctness-pipeline.md#pre-merge-gate-and-the-known-red-pin)
explains why complete execution receipts and known-red accounting matter:
green CI is neither a claim that every test passed nor that every slow lane ran.

`--gate no-corpus` and `--gate corpus` partition the full suite; an unfiltered
run includes both. Choose the smallest useful iteration lane, then meet the
change-specific evidence requirements rather than treating a focused slice as
the full pre-review result. Documentation-only changes require Markdown lint,
not a decompiler corpus run.

## Finding the right change surface

| Change | Start here | Evidence to locate next |
| --- | --- | --- |
| Wrong imported type, slot, or opcode meaning | `MethodImporter`, `IrImporter`, `TypeRefDecoder`; [IR design](decompiler-ir.md). | Importer tests and a compiler-produced or precise IL witness. |
| Over-raise or missing source idiom | Owning pass, shared proof atoms, `IrPasses.Default`; [raise discipline](decompiler-raise-discipline.md). | Positive/decline family, validity, render A/B, and affected-method fidelity. |
| Branch, loop, or EH reconstruction | `Cfg`, structuring passes; [control-flow design](design/control-flow-structuring.md). | Successor/transfer ownership cases and relevant fidelity limits. |
| Invalid expression spelling or lost conversion | Printer partials, conversion rules, coercion pass. | Printer and binding cases plus independent compile-back outcomes. |
| Member/type shell or body integration | `MemberBodyProducer` and CSharp-owned producers/printers. | Member-body, type/bind, and ReturnToSender tests. |
| Wrong annotated range or structural correspondence | Printed maps, document contracts, Research composition. | Range/document/provenance tests and host integration cases. |
| Harness result or corpus regression | `tools/DecompilerHarness` and linked harness tests. | Measurement/report tests and the exact input population, not a substitute global sample. |

For a single bad body, use the harness's per-pass dump and
[IR-dump reading guide](decompiler-ir-dumps.md) to find the first incorrect
transition. For behavior-changing work, follow the correctness pipeline and
[decompiler PR template](templates/decompiler-pr.md); this map locates their
implementation, but does not replace their evidence requirements.
