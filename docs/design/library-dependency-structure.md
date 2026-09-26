# Library dependency structure

## Status and authority

Focused Research design for
[#8634](https://github.com/richlander/dotnet-inspect/issues/8634), the
architecture-narrative storyline of
[#8516](https://github.com/richlander/dotnet-inspect/issues/8516).

The **Library Dependency Structure** document is the single normative owner of
this claim:

> Given one exact library's whole-scope Analysis call evidence, issue the
> complete admitted type dependency graph, its projection onto the library's
> declared namespaces and referenced assemblies, and the namespace cycles and
> levels derived from that projection. Every exclusion and every qualification
> remains visible.

Analysis continues to own direct-call construction, method identity, physical
and logical body association, the whole-scope receipt, and body diagnostics.
Research owns admission, aggregation, derivation, and the portable document.
Queries and hosts keep selection, acquisition, execution, and presentation.

Supporting designs by role:

| Role | Document |
| --- | --- |
| Sibling Research owner over the same Analysis execution | [Library Metrics](library-structural-report.md) |
| Imported call evidence and whole-scope receipt | [Library body analysis service](library-body-analysis-service.md) |
| Consumer that clusters this graph | [#8406](https://github.com/richlander/dotnet-inspect/issues/8406) dependency communities |
| Cross-library call use, for multi-assembly questions | [Pairwise library direct-use clusters](pairwise-library-direct-use-clusters.md) |
| Complete-Content host boundary | `InspectionEnvelope<TContent>` ([#8517](https://github.com/richlander/dotnet-inspect/issues/8517) adoption pattern) |
| Section defaults | [Progressive disclosure](progressive-disclosure.md) |

## User question

> For this exact library release, how do its declared namespaces depend on one
> another and on other assemblies? Do those dependencies form a layered order
> or cycles, and which type relationships account for each dependency?

The question exists so that a human can check what was built, especially code
an agent produced, against compiled evidence rather than against the author's
description. In a well-designed library, the declared structure (namespace
and assembly names) and the actual structure (who calls whom) tell the same
story. This document issues the facts that establish agreement or divergence.
It does not narrate them.

## Motivating evidence

The [#8634 probe](https://github.com/richlander/dotnet-inspect/issues/8634#issuecomment-5847266377)
ran production commands over this repository's 65 product assemblies at
`daaad95a07` and over
[FluentValidation 12.1.1](https://www.nuget.org/packages/FluentValidation/12.1.1)
(net8.0):

- Across assemblies, the evidence carried a true and non-obvious story. The
  internal assembly references contain no upward edge across the declared name
  families. Exact pairwise call use aggregated by caller namespace separated
  a raw-metadata host architecture (`DotnetInspect.Cli.Inspectors`: 949 call
  sites into `ILInspector.Metadata`, 5 into `QuerySpace`) from the newer
  Sections and Queries substrate. That independently matched
  [#6998](https://github.com/richlander/dotnet-inspect/issues/6998).
- Within one assembly, nothing answered the same question. FluentValidation
  names its purpose clearly (`Validator` covers 55 of 111 public types), yet
  no command could say whether `FluentValidation.Internal`,
  `FluentValidation.Validators`, and `FluentValidation.TestHelper` form layers
  or a tangle. Most agent-built applications are this shape.
- The probe's cross-namespace matrix took about 200 lines of host-side awk
  and Python over TSV rows. That logic belongs in one host-neutral owner.

## Imported evidence

The document consumes one `LibraryBodyAnalysisExecution` whose receipt
establishes a whole-library method-evidence scope and whose call graph result
was requested. The document retains that receipt rather than reconstructing
library identity from a path or display name. A scoped execution, or one
without call evidence, yields a typed unavailable outcome that preserves the
receipt and the reason. It never yields an empty graph.

Library Metrics may share the same execution. Neither document recomputes the
other's facts.

## Admitted graph

### Nodes

A **type node** is one type definition declared in the inspected module,
identified by its exact metadata definition identity. Generic arity and
nesting are part of identity. A **namespace node** is one exact metadata
namespace string declared by at least one type node. The global namespace is
an explicit node, never an empty display label. A nested type belongs to its
outermost enclosing type's namespace.

An **external node** is one exact pair of referenced assembly identity and
namespace, taken from the callee's owner-issued type reference. External nodes
exist only as edge targets. The document does not enumerate or inspect the
referenced assembly.

### Relationship source

Each admitted relationship originates from one physical direct call issued by
Analysis. The source type is the declaring type of the call's **logical
caller**, the Analysis-issued declared-source association. A lambda,
iterator, async state machine, or local function therefore contributes to the
type that declared it, not to its compiler-created container. The physical
evidence body stays on the occurrence so the association remains auditable.

### Relationship kinds

- **Invocation:** `call`, `callvirt`, and `newobj`.
- **Function reference:** `ldftn` and `ldvirtftn`. Loading a method pointer
  is a compile-time dependency even though it is not an invocation.

Edges carry both counts separately. Cycles and levels are derived over their
union. `calli` has no static target and is counted as unresolved.

### Targets

- **Internal:** the callee definition token resolves to a method declared in
  the inspected module. The target is that method's declaring type node.
- **External:** the callee's declaring type reference names another assembly.
  The target is the external node for that assembly and the callee's
  declaring type namespace. Generic instantiation does not create an edge to a
  type argument's namespace: `List<Foo>.Add` is an edge to
  `System.Collections.Generic` only.
- **Unresolved:** anything else, including `calli`, a failed reference
  decode, or a token that resolves to no declared method. These are counted
  per reason in the receipt and never guessed.

A relationship whose source and target are the same type node is retained as
intra-type volume, not as an edge.

### Population receipt

The receipt preserves the Analysis receipt unchanged and adds exact counts:

- physical direct calls examined;
- admitted internal, admitted external, and unresolved occurrences, with
  unresolved counts by reason;
- physical bodies whose call evidence is incomplete, with Analysis
  diagnostics carried unchanged. The input is the receipt's method-keyed
  Analysis diagnostics. Every diagnosed body counts as incomplete for this
  document, whichever Analysis feature raised the diagnostic. This is
  conservative: it can over-qualify, but it never under-qualifies. A future
  Analysis-issued call-coverage receipt would replace this input; Research
  does not construct that receipt itself; and
- type, namespace, and external nodes.

A duplicate physical occurrence identity is invalid owner input. It fails
visibly and never coalesces.

## Issued document

The Research result is a typed `LibraryDependencyStructureResult`. Its
`Available` outcome carries one resource-free
`LibraryDependencyStructureDocument` containing:

1. the Analysis receipt, the population receipt, and a methodology version;
2. every type node with its namespace and its intra-type relationship count;
3. every type-to-type edge with invocation and function-reference counts;
4. every namespace node with type count, intra-namespace relationship count,
   and its derived cycle and level (below);
5. every namespace-to-namespace edge with invocation and function-reference
   counts, contributing type-edge count, and up to five **explaining type
   edges** selected by relationship count, then source identity, then target
   identity, with the exact count of the remaining contributors;
6. every namespace-to-external edge with the same counts and explanation; and
7. the Analysis diagnostics.

Research applies no display bound to nodes or edges. Hosts may page or limit
presentation, but the typed Content is complete. The only bounded collection
is the per-edge explanation list, and it carries an exact remainder count.

Every collection is ordered deterministically by exact identity. Display text
never establishes identity or order.

## Derived structure

**Namespace cycles** are the strongly connected components of the internal
namespace graph that contain two or more namespaces. A cycle's members are
ordered by namespace identity, and cycles are ordered by their first member.

**Levels** follow Lakos levelization over the condensation of the internal
namespace graph. A namespace with no internal outgoing edge is level 0. Every
other namespace is one more than the highest level it depends on. All members
of one cycle share one level. External edges do not affect levels.

### Absence claims require completeness

When the Analysis receipt establishes whole-library scope and the population
receipt shows no incomplete bodies and no unresolved occurrences, the document
may state that the namespace graph is acyclic or
that a namespace has no dependency on another. Otherwise the same facts are
issued as **qualified**: "no cycle among admitted evidence". The document
never states an unqualified absence over partial evidence. This is the
contract's central correctness property.

## Interpretation boundary

The document can say:

- which namespaces depend on which, and how strongly;
- which type relationships explain each dependency;
- which namespaces form a cycle; and
- each namespace's level.

It does not:

- label a cycle, level, edge, or weight as a defect, severity, or quality
  score;
- label an architecture as legacy, intended, or modern;
- compute instability, abstractness, or distance-from-main-sequence scores;
- infer communities (#8406 owns those); or
- infer authorship.

Those are interpretations for the narrative layer, which must label them as
interpretations under the #8516 narrative levels.

## Non-claims and residuals

- **Generated versus authored code.** Every physical body is evidence, the
  same stance as Library Metrics. Source-generated callers such as
  `JsonContext` dominated the #8634 probe. A typed generator-provenance
  classification belongs to the Metadata/PDB source owner
  ([#8643](https://github.com/richlander/dotnet-inspect/issues/8643)). This document neither filters nor classifies generated code.
- **Signature, field, attribute, and inheritance references** are not call
  evidence and are not admitted. They would need a Metadata-owned relationship
  producer and a new relationship kind in a later methodology version.
- **Folder structure** from PDB document paths is a second declared axis. It
  depends on the same source-provenance owner and is out of scope.
- **Library Metrics' bounded relationship projection** stays unchanged.
  Deriving it from this admitted graph, which would retire a parallel
  computation, is a later one-donor transfer.
- **Multi-library structure** stays with pairwise call use and a future
  Workspace-level composition.
- **Budget incompleteness**
  ([#8636](https://github.com/richlander/dotnet-inspect/issues/8636)) reaches
  this document only as Analysis-issued incomplete bodies. This document
  qualifies its results and does not change Analysis budgets.

## Analogous implementations

The survey informs vocabulary and boundaries only. No code or architecture
transfers.

| Tool | Relevant behavior | Transfer decision |
| --- | --- | --- |
| Lakos, *Large-Scale C++ Software Design* | Levelization over the component condensation | Adopted as the level definition |
| NDepend dependency matrix | Namespace dependencies, cycles, and explaining members | Adopted: edge explanation and cycle vocabulary. Declined: Instability/Abstractness scores and rule verdicts |
| Structure101 / Sonargraph | "Tangles" (strongly connected components) and levelized views | Adopted: cycles as strongly connected components. Declined: tangle severity metrics |
| JDepend | Package cycles plus Martin metrics | Declined: metrics are interpretations |
| ArchUnitNET / NetArchTest | User-authored layering rules asserted in tests | Declined here. Rules are a possible later consumer of this document |

These tools usually analyze all type references. Starting from call evidence
is a deliberate narrowing to evidence Analysis already owns and qualifies.
The methodology version makes a later broadening explicit.

## Validation gates

The implementation belongs in the Release `ILInspector.Research.Tests`
executable over a compiled fixture library under `fixtures/research/`:

- `LibraryDependencyStructure_RejectsScopedOrCallFreeExecution`: a scoped or
  call-free execution is unavailable and retains its receipt.
- `LibraryDependencyStructure_AttributesLiftedBodiesToLogicalOwner`: a lambda,
  async method, and iterator in namespace `A` calling into `B` produce an
  `A → B` edge. The occurrence retains the physical evidence body.
- `LibraryDependencyStructure_ProjectsNestedAndGlobalNamespaces`: a nested
  type uses its outermost type's namespace, and the global namespace is an
  explicit node.
- `LibraryDependencyStructure_SeparatesInvocationAndFunctionReference`:
  `ldftn` counts separately from invocation and still participates in cycles.
- `LibraryDependencyStructure_ProjectsExternalGenericTargetToDeclaringNamespace`:
  `List<Foo>.Add` yields an external edge to `System.Collections.Generic` and
  no edge to `Foo`'s namespace.
- `LibraryDependencyStructure_DerivesCyclesAndLevels`: a three-namespace
  cycle plus an acyclic tail produce one cycle and the expected levels.
- `LibraryDependencyStructure_QualifiesAbsenceUnderIncompleteEvidence`: an
  otherwise acyclic graph with one Analysis-issued incomplete body issues
  qualified absence, not unqualified acyclicity.
- `LibraryDependencyStructure_CountsUnresolvedAndRejectsDuplicateOccurrence`:
  `calli` is unresolved by reason, and a duplicated occurrence identity fails
  visibly.
- `LibraryDependencyStructure_BoundsExplanationWithExactRemainder`: an edge
  with seven contributing type edges retains five in the specified order and a
  remainder of two.

Real-asset probes are reproducible design evidence, not CI gates:
FluentValidation 12.1.1 (net8.0), `dotnet-inspect.dll` at a pinned commit,
and `ILInspector.Analysis.dll`, which is #8406's motivating asset. Each probe
records node, edge, and cycle counts, the receipt, and elapsed time on a warm
cache.

## Adoption plan

1. **Research:** the document, typed outcome, and fixture gates.
2. **Query:** a Research-backed query in `DotnetInspector.ResearchQueries`
   carries the completed document without rendering it. It shares the Analysis
   execution with `LibraryMetricsQuery` when both are selected.
3. **CLI:** an exact-name-only `library` section, `Dependency Structure`,
   outside the default `-v:m` view. It uses Markout for tables and the Mermaid
   graph lowering, and `--envelope` carries the complete Content with Share
   and diagnostics.
4. **Browser/Wasm:** the Library Metrics lens adds a levelized namespace view
   with cycles marked and drill-down from edge to explaining type edges to
   Type. It uses the same managed query and does no topology work in
   TypeScript.
5. **Skill:** the `project-analysis` workflow catalog
   ([#8518](https://github.com/richlander/dotnet-inspect/pull/8518)) gains an
   architecture-narrative workflow that consumes this document and labels
   interpretations as such.

Steps 1–2 are shared by both hosts. #8406 consumes step 1's admitted type
graph rather than defining a second graph population.
