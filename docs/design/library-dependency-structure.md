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
| Row selection substrate | [QuerySpace library boundary](query-space-library.md), [Semantic row selection](semantic-row-selection.md) |

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

### Analysis prerequisite: same-module callee resolution

Internal-target admission needs an Analysis-issued answer to one question for
every direct call: which method definition declared in the inspected module
does this call bind to, if any? The raw definition token on a direct call
does not answer it. A call through a generic instantiation, such as a method
of `Box<T>` calling its own `B()`, is encoded as a member reference on a type
specification, and that token stays unresolved. Analysis publishes that resolution through
`LibraryCallGraphAnalysisResult.ResolveTarget`, first by token and then by
signature. Its typed failures are indirect, unsupported signature, malformed
signature, invalid generic declaration, unmatched, and ambiguous.
`LibraryCallGraphAnalysisResult.ResolveDeclaredMethod` publishes the
target-side declared-source association, including for methods that make no
calls. These Analysis-owned prerequisites landed in #8701 and #8704.
Nested lifted bodies are attributed exactly as that association issues them.
Research consumes the result. It never re-implements signature
matching, and it never treats a missing resolution as an external call.

### Modern infrastructure only

Every slice of this work builds only on the current architecture, and
superseded infrastructure is prohibited:

- **Inspector.Graph owns topology mechanics.** Research constructs one
  portable `GraphDocument` whose canonical nodes are internal types and exact
  external namespace targets, whose groups are internal namespaces and exact
  external nodes, and whose relationships distinguish invocation from function
  reference. `GraphDocumentExecution.GroupProjection` owns namespace
  contraction and complete canonical contributors.
  `GraphDocumentExecution.ComponentAnalysis` runs over the induced selection of
  internal namespace groups and exclusively owns strong components,
  condensation, contributor retention, and levels. Research retains those
  source-bound results with its document, lowers them into this owner's rows,
  and applies only domain meaning: internal/external selection, namespace cycle
  vocabulary, completeness, qualification, and presentation order. It never
  copies SCC or levelization logic.
- **`LibraryBodyIndex` is prohibited.** That includes its
  `CompatibilityIndex()` adapter on `LibraryBodyAnalysisExecution` and any
  API that returns or wraps it. Research, the query, and both hosts consume
  only Analysis's focused typed results from one
  `LibraryBodyAnalysisExecution`. When a needed fact exists only on the
  compatibility index, the resolution is to have Analysis publish it on a
  focused result (step 0). Never read through the index "for now".
- **QuerySpace is the encouraged selection substrate.** Filtering, ordering,
  counting, and limiting the issued rows (type, namespace, and external nodes;
  edges; cycles) use QuerySpace, with the row vocabulary declared
  host-neutrally beside this owner rather than in a host. Hosts do not
  hand-filter, re-sort, or re-count issued collections.
- **Selection runs after the graph is built.** This is a deliberate reference
  slice. Cycles, levels, and qualified absence are defined over the complete
  admitted graph, so the document is always derived in full from the
  whole-scope execution, and QuerySpace selection applies to the issued
  document's rows. Pushing selection into Analysis is a non-goal, because no
  predicate can narrow the population without changing a derived fact.
- **Other current substrate:** Content, Share, and diagnostics travel in
  `InspectionEnvelope<TContent>`, and the CLI lowers through Markout. No
  parallel envelope, row model, or renderer is introduced.

The prohibition is a design rule enforced by review of each slice, not by a
dedicated absence gate. That choice of coverage is recorded operator
direction.

## Admitted graph

### Nodes

A **type node** is one type definition in the inspected module that declares
at least one method in Analysis's declared-method population. Identity is the
exact metadata definition identity, including generic arity and nesting. Types
that declare no method (enums, member-less interfaces) are not nodes, because
no call can target or originate in them. A namespace that contains only such
types is therefore not a node either. This is a deliberate narrowing to
evidence the imported execution issues. Enumerating every type definition
would need a second, Metadata-owned input.

A **namespace node** is one exact metadata namespace string of the outermost
enclosing type of at least one type node. A nested type therefore belongs to
its outermost enclosing type's namespace. The global namespace is an explicit
node, never an empty display label.

An **external node** is one exact pair of referenced assembly and namespace.
The assembly is the `AssemblyReferenceIdentity` recorded in the callee
declaring type's reference origin, exactly as referenced. Type forwarding is
not followed, and canonical core-library folding is not applied, so
`System.Runtime` and `System.Runtime.Extensions` remain distinct nodes. For a
generic instantiation, the declaring type is its generic definition. A
primitive declaring type with an intrinsic core-library origin maps to one
explicit **intrinsic core library** external node. External nodes exist only
as edge targets. The document does not enumerate or inspect the referenced
assembly.

### Relationship source

Each admitted relationship originates from one physical direct call issued by
Analysis. An **occurrence** is identified by Analysis's physical call-site
key: evidence body token, IL offset, and operand token. Two lifted bodies of
one declaring method may share an IL offset, and they remain distinct
occurrences.

The source type is the declaring type of the call's **logical caller**, the
Analysis-issued declared-source association. An internal target method that
carries the same association maps to its logical owner's declaring type.
Research uses that association only where Analysis issues it. Today that
covers lifted lambdas, local functions, and async `MoveNext` bodies. Every
other method keeps its physical declaring type, including sync iterator
`MoveNext`, state-machine and display-class constructors, and `<>c` static
constructors. The corresponding compiler containers are therefore ordinary
nested type nodes with their physical identity. Where the owner reaches a
container through a call (for example `newobj` of a display class or iterator),
that call is a type edge from the owner to the container. Namespace results
are unaffected,
because a nested container shares its outermost type's namespace. Widening
the association is Analysis's decision. Research never derives ownership from
compiler naming. Any Analysis diagnostic for a body qualifies the document
(see the population receipt). The physical evidence body stays on the
occurrence so the association remains auditable.

### Relationship kinds

- **Invocation:** `call`, `callvirt`, and `newobj`.
- **Function reference:** `ldftn` and `ldvirtftn`. Loading a method pointer
  is a compile-time dependency even though it is not an invocation.

Edges carry both counts separately. Cycles and levels are derived over their
union. `calli` has no static target and is counted as unresolved.

### Targets

- **Internal:** the Analysis same-module resolution binds the call to a method
  declared in the inspected module, including calls through generic
  instantiations. The target is that method's declaring type node, or its
  logical owner's type for a lifted body.
- **External:** the callee declaring type's reference origin names another
  assembly or the intrinsic core library. The target is the external node for
  that assembly and the declaring type's namespace. Type arguments never
  create edges: `List<Foo>.Add` is an edge to `System.Collections.Generic`
  only.
- **Runtime-provided:** methods the runtime supplies on array types (for
  example multidimensional `Get`/`Set`). They are counted in the receipt, are
  not edges, and do not qualify absence, because they cannot bind to a
  declared method.
- **Unresolved:** each remaining call, with a typed reason. The reasons are:
  `calli` (no static target); a current-module declaring type for which
  Analysis resolution reports unsupported signature, malformed signature,
  invalid generic declaration, unmatched, or ambiguous; and a module-reference
  origin. "Current-module" is Analysis's own current-module test, which also
  covers a self-referencing assembly reference and a same-module module
  reference. Such a reference is never classified as external.
  Reasons carry Analysis's typed resolution failure unchanged. Unresolved
  calls are counted per reason and never guessed.

A relationship whose source and target are the same type node is retained as
intra-type volume, not as an edge.

### Population receipt

The receipt preserves the Analysis receipt unchanged and adds exact counts:

- physical direct calls examined;
- admitted internal (including same-type), admitted external,
  runtime-provided, and unresolved occurrences, with unresolved counts by
  reason. These four categories partition the examined calls exactly, so
  their sum equals the examined count;
- physical bodies whose call evidence is incomplete, with Analysis
  diagnostics carried unchanged. The input is the receipt's method-keyed
  Analysis diagnostics. Every diagnosed body counts as incomplete for this
  document, whichever Analysis feature raised the diagnostic. This is
  conservative: it can over-qualify, but it never under-qualifies. The count
  is a lower bound on affected bodies, because Analysis may report one
  diagnostic for a budget-limited group of lifted bodies. Any non-zero count
  qualifies the document. A future
  Analysis-issued call-coverage receipt would replace this input; Research
  does not construct that receipt itself; and
- type, namespace, and external nodes.

A duplicate physical call-site key is invalid owner input. It fails visibly
and never coalesces.

## Issued document

The Research result is a typed `LibraryDependencyStructureResult`. Its
`Available` outcome carries one resource-free
`LibraryDependencyStructureDocument` containing:

1. the Analysis receipt, the population receipt, and a methodology version;
2. every type node with its namespace and its intra-type relationship count;
3. every type-to-type edge and every type-to-external edge, each with
   invocation and function-reference counts;
4. every namespace node with type count, intra-namespace relationship count
   (type-to-type edges within the namespace plus its types' intra-type
   volume), and its derived cycle and level (below);
5. every namespace-to-namespace edge with invocation and function-reference
   counts, contributing type-edge count, and up to five **explaining type
   edges** drawn from item 3's type-to-type edges, selected by descending
   relationship count (invocation plus function reference), then source
   identity, then target identity, with the exact count of the remaining
   contributors;
6. every namespace-to-external edge with the same counts and explanation,
   drawn from item 3's type-to-external edges; and
7. the Analysis diagnostics.

Research applies no display bound to nodes or edges. Hosts may page or limit
presentation, but the typed Content is complete. The only bounded collection
is the per-edge explanation list, and it carries an exact remainder count.

Every collection is ordered deterministically by exact identity. Display text
never establishes identity or order.

## Derived structure

**Namespace cycles** are groups of two or more namespaces in the inspected
library that depend on each other through call edges: each namespace in the
group reaches every other one, directly or through other namespaces
(formally, the multi-member components issued by Graph for the induced
selection of internal namespace groups). For example, if `Foo.Validators`
calls `Foo.Internal` and
`Foo.Internal` calls back into `Foo.Validators`, both form one cycle.

Only internal namespaces take part. A namespace in another assembly is an
external node keyed by that assembly, even when it has the same name as a
local namespace, so a namespace declared across two assemblies is neither a
cycle nor detected here. A cycle's members are ordered by namespace identity,
and cycles are ordered by their first member.

**Levels** are the Graph-issued Lakos levels over the condensation of that same
internal-group selection. A namespace with no internal outgoing edge is level
0. Every other namespace is one more than the highest level it depends on. All
members of one cycle share one level. External edges do not affect levels.
Research does not reconstruct the condensation or settle levels.

### Absence claims require completeness

When the Analysis receipt establishes whole-library scope and the population
receipt shows no incomplete bodies and no unresolved occurrences, the document
may state that the namespace call graph is
acyclic or that a namespace has no call dependency on another. Otherwise the
same facts are issued as **qualified**: "no cycle among admitted call
evidence". The document never states an unqualified absence over partial
evidence. This is the contract's central correctness property.

Every absence is about **call** dependencies. Field access, casts, `ldtoken`,
signatures, and attributes can create dependencies this methodology does not
admit, so an issued absence never says "no dependency" without the word
"call".

## Shape and amplitude

This document issues the graph's **shape**: which nodes exist, how they
connect, and where the cycles and levels fall. A narrative also needs
**amplitude**, meaning how much weight each node carries. That is the third
dimension over the two-dimensional shape. Amplitude comes from other owners
and is joined onto this shape by exact identity. It is never computed here.

| Amplitude | Owner | Grain and qualification |
| --- | --- | --- |
| Implementation volume and complexity | [Library Metrics](library-structural-report.md) type summaries | Per type, over complete physical profiles |
| Leverage (distinct callers, fan-out, depth, loops) | Analysis `LibraryLeverageAnalysisResult` | Per method; currently a bounded top-N ranking, so an overlay must disclose that it is partial |
| Communities | [#8406](https://github.com/richlander/dotnet-inspect/issues/8406) over this graph | Grouping, not weight; it colors the shape |

This owner's single obligation to amplitude is **join currency**:

- A type node's identity is the same exact metadata type key that Library
  Metrics issues.
- Every type node and edge can be reached from Analysis method identities
  through their declaring type.

No host joins by display name. A composed "three-dimensional" view is a
consumer of this document and of each amplitude owner. It is not a new field
here. Any amplitude that requires a complete population (for example leverage
on every node) is that owner's focused effort.

## End-to-end arc

This section is context, not normative. The production scenario is an
**agent** explaining what a library or repository is built from, working in two
passes over tool output with `jq`:

1. **Vocabulary.** The agent learns the library's name families: which word
   patterns exist, how widespread they are, how distinctive they are against a
   corpus, and how much amplitude they carry. It then decides the semantic
   categories. For example, it reads `Scope` + `Lifetime` + `Site` as
   dependency injection, or treats `*SR` as infrastructure.
2. **Numbers.** The agent maps those categories onto the numbers with `jq`:
   which family depends on which namespace or assembly, and how strongly.

Raw `jq` over thousands of types and edges is impractical for an agent. It
strains the token budget, forces string parsing, and makes every agent
tokenize names differently. The tool therefore does the deterministic work:
word breaking against the runtime oracle, and **summing** by name family. The
agent's `jq` then maps categories over tens to hundreds of summed rows, not
thousands of raw ones. Name-family summaries are a separate focused owner
([#8698](https://github.com/richlander/dotnet-inspect/issues/8698)). They
consume this document's type edges through the join currency above, which
lets them sum edges by family (for example `*Command` → `ILInspector.Metadata`)
without any parsing of display names.

The arc is a partial map, not a parser with an accuracy guarantee. The
[#8634 survey](https://github.com/richlander/dotnet-inspect/issues/8634)
measured it:

- 94% of names in the top-100 NuGet corpus break by case alone;
- a runtime-oracle breaker reaches about 97% on held-out names, and about 99%
  on ASP.NET Core and Microsoft.Extensions;
- the families that stand out against the corpus name each stack's
  architecture.

This document's part of the arc is the shape the numbers are read from.

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
  ([#8643](https://github.com/richlander/dotnet-inspect/issues/8643)). This
  document neither filters nor classifies generated code. The same applies to
  compiler-synthesized global-namespace types such as
  `<PrivateImplementationDetails>` and `<Module>`. Their calls can form a
  cycle through the global namespace, and a narrative consumer must not read
  that cycle as authored structure.
- **Signature, field-access, cast, `ldtoken`, attribute, and inheritance
  references** are not call evidence and are not admitted. `jmp` is not a
  direct call in Analysis and is likewise outside the methodology. The C#
  compiler does not emit it. They would need a Metadata-owned relationship
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
  local function, and async method of `A.C` calling into `B` produce a
  `A.C → B.*` type edge from `A.C` itself. The occurrence retains the physical
  evidence body. A sync iterator of `A.C` produces its edge from the physical
  container `A.C.<Iter>d__N`, and the namespace edge is `A → B` in both
  cases.
- `LibraryDependencyStructure_AcceptsDistinctLiftedBodiesAtEqualOffsets`: two
  lambdas in one method with calls at the same IL offset are two occurrences,
  not a duplicate.
- `LibraryDependencyStructure_ResolvesCallsThroughGenericInstantiations`: a
  method of `Box<T>` calling its own member, and a call to another internal
  generic type through an instantiation, produce internal edges rather than
  unresolved counts.
- `LibraryDependencyStructure_KeysExternalNodesByExactReference`: calls into
  types referenced through `System.Runtime` and `System.Collections` yield
  distinct external nodes. A type the runtime forwards to the core library
  stays with the referenced facade (`System.Runtime`). The intrinsic
  core-library node is **unverified**, because C# does not emit a member
  reference whose parent has an intrinsic origin.
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
- `LibraryDependencyStructure_GraphComponentsMatchIndependentOracle`: every
  internal namespace group and no external group reaches Graph component
  analysis; its cycles and levels agree with an independent Research test
  oracle over the issued namespace edges; and every Graph condensation
  contributor crosses the component boundary it claims.
- `LibraryDependencyStructure_QualifiesAbsenceUnderIncompleteEvidence`: a
  graph with an unresolved `calli` is `Qualified`, and a fixture with no
  unresolved calls and no diagnosed bodies is `Complete`. The same rule
  qualifies on Analysis-diagnosed bodies. That branch is **unverified** by a
  fixture, because no current compiler output yields a diagnosed body except
  the budget case tracked by #8636.
- `LibraryDependencyStructure_PartitionsExaminedCallsExactly`: `calli` is
  unresolved by reason, a multidimensional array accessor is runtime-provided,
  the four receipt categories sum to the examined count, and the type-level
  edges and intra-type counts reconcile with the receipt. Rejecting a
  duplicated physical call-site key is enforced in code and is **unverified**
  by a gate, because Analysis never publishes duplicates.
- `LibraryDependencyStructure_BoundsExplanationWithExactRemainder`: an edge
  with seven contributing type edges retains five in the specified order and a
  remainder of two.
- `LibraryDependencyStructure_ExternalKeysNeverCollideAcrossSeparators`:
  length-prefixed external identity components keep distinct
  `(assembly, namespace)` pairs distinct even when untrusted metadata contains
  delimiter-like text.
- `LibraryDependencyStructure_UsesLibraryMetricsTypeKeys`: every type node's
  key is `LibraryStructuralReport.TypeKey`, and every type edge endpoint is a
  node.

Graph's
`ComponentAnalysis_DeepChainAndGiantCycleRemainIterative` gate owns
stack-safe pathological component derivation. Research does not duplicate that
algorithm or its stress gate.

Real-asset probes are reproducible design evidence, not CI gates:
FluentValidation 12.1.1 (net8.0), `dotnet-inspect.dll` at a pinned commit,
and `ILInspector.Analysis.dll`, which is #8406's motivating asset. Each probe
records node, edge, and cycle counts, the receipt, and elapsed time on a warm
cache.

## Adoption plan

0. **Analysis prerequisite (complete):** #8701 and #8704 publish the
   same-module callee resolution and declared-source association described
   under
   [Imported evidence](#analysis-prerequisite-same-module-callee-resolution),
   owned by
   [Library body analysis service](library-body-analysis-service.md).
1. **Research:** the document, typed outcome, and fixture gates.
2. **Query:** a Research-backed query in `DotnetInspector.ResearchQueries`
   carries the completed document without rendering it. It shares the Analysis
   execution with `LibraryMetricsQuery` when both are selected, consumes only
   focused Analysis results, and exposes the issued rows through QuerySpace
   (see [Modern infrastructure only](#modern-infrastructure-only)).
3. **CLI:** an exact-name-only `library` section, `Dependency Structure`,
   outside the default `-v:m` view. It uses Markout for tables and the Mermaid
   graph lowering, and `--envelope` carries the complete Content with Share
   and diagnostics.
4. **Browser/Wasm:** the Library Analysis inspector's Metrics tab adds a
   levelized namespace view with cycles marked and drill-down from edge to
   explaining type edges to Type. It uses the same managed query and does no
   topology work in TypeScript.
5. **Skill:** the `project-analysis` workflow catalog
   ([#8518](https://github.com/richlander/dotnet-inspect/pull/8518)) gains an
   architecture-narrative workflow that consumes this document and labels
   interpretations as such. The workflow runs the two-pass `jq` arc in [End-to-end
   arc](#end-to-end-arc), together with the name-family summaries from #8698.

Steps 0–2 are shared by both hosts. The CLI reaches observable behavior in
four steps (0–3), and the skill adds a fifth. Browser/Wasm reaches it in four
steps (0, 1, 2, 4). #8406 consumes step 1's admitted type graph rather than
defining a second graph population.
