# ReturnToSender: fact-planned compile-back harness

## Summary

The next compile-back harness, working name **ReturnToSender**, should start
fresh alongside the current `DecompilerHarness` and later cherry-pick proven
pieces. The design point is not "whole-module skeleton, but with a closure
bias." It is an **artifact round-trip harness** for product-generated source
artifacts.

The input is a built fixture library. ReturnToSender requests a product C#
artifact for a target such as `fixture.foo`, optionally with a declared artifact
shape or closure policy ("type artifact", "include these closure members",
"include target body"). Product code produces the artifact. ReturnToSender
compiles that artifact, then compares the original fixture assembly with the
compiled artifact assembly through product diff primitives. API diff and IL/body
diff should be first-class product capabilities, with Research eventually
offering a unified API/IL/C# diff projection. RTS consumes those product diffs as
an artifact-fidelity oracle.

The important distinction is artifact production vs proof:

- product code owns C# artifact production;
- ReturnToSender owns artifact requests, compilation, product diff invocation,
  and failure reporting.

The existing `CB_CLUSTER=1` path already has a strong generic answer for closure
membership: compile, read the compiler's missing-symbol diagnostics, add the
named same-assembly roots, and repeat until the closure stops growing or hits a
budget. The new harness may reuse that algorithm for closure policy feedback,
but it should not turn RTS into another C# shell generator.

The new harness should:

1. build fixture libraries as part of the normal repo build;
2. select a target artifact from a fixture assembly;
3. send a typed artifact request to product code;
4. compile the returned C# artifact;
5. invoke product API and IL/body diffs between original and artifact assemblies;
6. optionally consume C# artifact/source diffs when product support exists;
7. classify artifact production, compile, diff, and opcode failures.

## Non-goals

- Do not rewrite the product decompiler.
- Do not put Roslyn, compile-back, or inspected-assembly loading in shipped
  product libraries.
- Do not make `ILInspector.Research` own compile-back orchestration.
- Do not make the harness a source generator for arbitrary assemblies.
- Do not let RTS construct C# shells to compensate for missing product artifacts.
- Do not replace compiler-driven closure membership with speculative static
  closure.
- Do not let parallel type identity representations drift if a shared substrate
  can serve the role.
- Do not treat the current harness as the strategic architecture. Keep it only as
  a compatibility bridge, regression baseline, and source of reusable algorithms
  while ReturnToSender is built.

## Existing assets to reuse

| Asset | Role in the new harness |
| --- | --- |
| Product source/artifact pipeline | Source of C# artifacts under test. |
| `CSharpPrinter` | Product body renderer for artifacts that include method bodies. |
| API and IL/body diffs | Product diff primitives used as artifact-fidelity oracles. |
| Research unified diff | Future API/IL/C# evidence join for product UX and RTS proof. |
| current `CB_CLUSTER` loop | Keep generic compiler-driven closure membership. |
| `ILInspector.Instructions` | Shared IL decode, block identity, typed stack (`StackType`) substrate. |
| `ILInspector.Analysis` | Whole-assembly IL facts, direct calls, body indexes, type/member references. |
| `ILInspector.Research` | Typed fact registry, joins, and projections only. |
| assembly/package resolution service | Generalize and reuse for CLI and harness reference closure. |
| fixture libraries | Built repo artifacts that RTS uses as original metadata witnesses. |

## Strategic stance

ReturnToSender is a replacement track, not a "stay the course" refinement.

The current harness has already delivered enough value to expose the next class
of problems, but the skeleton architecture has plateaued. If it is already near
an 80/20 point, then the remaining 20 percent is exactly where ad hoc source
patching becomes expensive and low-leverage. If it is not near that point, then
the architecture is failing earlier than expected. Either way, the conclusion is
the same: keep useful algorithms, but stop treating the current harness shape as
the path forward.

That means:

- preserve the current harness for continuity and comparison;
- keep `CB_CLUSTER`'s compiler-driven membership algorithm because it is a good
  algorithm, not because the surrounding skeleton architecture is the target;
- avoid broad new investment in string-emitted skeleton patches unless they are
  needed to protect existing gates;
- measure ReturnToSender against the current harness, then move the hard-row path
  to ReturnToSender when it demonstrates better leverage.

## Architecture

```text
Metadata / Instructions / Analysis / Research facts
                  |
                  v
        +---------------------------+
        | ArtifactRequest           |
        | target + shape policy     |
        +-------------+-------------+
                      |
                      v
        +---------------------------+
        | Product artifact provider |
        | C# artifact under test    |
        +-------------+-------------+
                      |
                      v
        +---------------------------+
        | Roslyn compile            |
        | artifact assembly         |
        +-------------+-------------+
                      |
                      v
        +---------------------------+
        | Product diff primitives   |
        | API + IL/body (+ C# later)|
        +-------------+-------------+
                      |
                      v
        +---------------------------+
        | Optional opcode compare   |
        | selected method bodies    |
        +---------------------------+
```

`ArtifactRequest` and oracle reporting should live in tools-only code under
`tools/` or another non-shipped harness assembly. Product artifact creation and
the API/IL/C# diff primitives belong in product code because they are the system
under test and useful CLI features. RTS may define request and comparison scopes,
but it should not own the C# artifact printer or a bespoke metadata comparator.

Research remains a product library. It can expose generic facts that other
product consumers also need, but compile-back planning is a harness concern.

## Layer responsibilities

### Assembly and package resolution service

Own package, assembly, reference, and dependency resolution.

Responsibilities:

- locate target assemblies;
- build exact NuGet dependency closure;
- choose one package version per id;
- locate shared frameworks;
- expose metadata handles and signature-level type facts.

This should be a general product service, not a ReturnToSender-only utility. The
CLI already needs the same answers for package, platform, framework, and source
selection, and the current harness has reimplemented enough of that behavior to
show the risk of divergence. ReturnToSender should consume this service for
reference closure and metadata identity instead of owning a parallel resolver.

The service should stay SRM-only and should sit below Decompiler, Research, and
ReturnToSender. Harness-only policy, such as "which closure roots belong in this
artifact request", belongs above this service.

### Instructions

Own shared IL identity and stack-shape substrate.

Responsibilities:

- decode IL;
- build EH-aware block graphs;
- provide offset-stable instruction/block identity;
- optionally interpret coarse evaluation-stack shape with `StackType` and
  `StackValue`.

`StackType` is not a full type identity. It is the CLI evaluation-stack lattice:
`Int32`, `Int64`, `NativeInt`, `Float`, `ManagedPointer`,
`UnmanagedPointer`, `ObjectReference`, `ValueType`, `TypedReference`, and
`Unknown`. It is useful for stack provenance and receiver/argument shape, not
for saying "this is protobuf type X." This describes the current substrate, not
a promise that the lattice is final; #1939 tracks related evidence and review
around whether the stack typing model should be trimmed.

### Analysis

Own whole-assembly IL facts.

Responsibilities:

- direct call and member-reference facts;
- method-body indexes;
- type/member evidence from IL;
- generated/synthesized classification inputs;
- StackType-backed callsite/receiver provenance when useful;
- closure candidate evidence from the target method body.

Analysis should tell us what the method body actually references before the
harness asks Roslyn what is missing. It should not decide compile-back bail
reasons or emit source artifacts.

### Research

Own generic fact registration, joins, and projections.

Today Research projects facts into annotated source/IL/Facts views. It can also
host reusable ecosystem facts such as "this type is an authentic protobuf
message" or "this member belongs to a generated family."

Responsibilities:

- register fact producers that are useful beyond the harness;
- join Analysis facts with Metadata facts;
- expose type/member/offset facts through a typed API;
- keep facts positive, inspectable, and reusable by multiple consumers.

Research should not:

- own `ReconstructionPlan`;
- decide compile-back closure membership;
- choose compile-back bail reasons;
- reference Roslyn;
- host artifact request, artifact production, or metadata-compare orchestration.

### ReturnToSenderPlanner

Own tools-only artifact requests and oracle planning.

Responsibilities:

1. select the target method;
2. choose the requested product artifact kind and closure/body policy;
3. invoke product artifact creation;
4. choose metadata and opcode comparison scopes;
5. record missing facts and bail reasons for harness reports.

The planner is the boundary between product artifacts and compile-back-specific
proof. It can ask product code for an artifact, but it should not construct C#
itself. It can use Research facts, but Research should not know that a particular
fact is required to turn `RecompileFail` into `Exact`.

### Product artifact provider

Own C# artifact production.

Input: `ArtifactRequest`.

Output: C# artifact plus structured provenance.

The artifact provider is product code. It may use Metadata, Analysis, Research,
and Decompiler internals, but it must remain SRM-only, NativeAOT-friendly,
Roslyn-free, and free of inspected-assembly loading. It answers product
questions such as:

- What type or member artifact should be emitted for this metadata target?
- Which closure members belong to the artifact under the requested policy?
- Which declarations are product-owned source shape rather than RTS scaffolding?
- Which base/interface/constructor relationships are real and spellable?
- Which method bodies are included, and which members are declaration-only?

The artifact provider should return structured diagnostics when it cannot
produce an artifact. RTS should surface those diagnostics; it should not patch
the artifact with local C#.

The product/shared side should own truthful declaration facts that are useful
beyond ReturnToSender: namespaces, type/member signatures, base/interface
relationships, constructor signatures, generic constraints, explicit interface
shapes, and generated-family shape evidence. Leaving those capabilities as
harness-only code means RTS tests the harness, not the product.

### Artifact printer

Own product C# rendering for the requested artifact.

The printer is product code, not RTS code. It is conceptually paired with
`CSharpPrinter`, but it emits a type/member/module artifact instead of only a
method body.

The artifact printer may render:

- namespaces;
- type declarations;
- base/interface clauses;
- generic parameters and constraints;
- field/property/method signatures;
- constructors and constructor initializers;
- generated-family explicit stubs;
- nested type declarations.

It should be a renderer, not a planner. It should not query Research or read
Roslyn diagnostics. RTS should not contain a fallback printer.

Namespace handling is the canonical example of why this should be reusable:
product artifact creation can determine namespace usage from metadata and closure
policy, and a shared writer can render block-scoped or file-scoped namespace
declarations from that usage. ReturnToSender needs that for artifact compilation;
whole-type decompiler output needs the same capability to write coherent type
files.

Constructor/base-chain handling has the same split:

- product/shared declaration facts answer **what exists and how it is spelled**:
  constructor signatures, base type, base constructors, generic constraints,
  type kind, and spellability;
- the shared writer renders those facts, such as `class C : Base`,
  `public C(int x) : base(x)`, and `static C()`;
- ReturnToSender owns only **artifact request policy** and proof: which artifact
  shape is requested, which product diff scopes are used, and how failures are
  reported. Synthetic constructors or omitted base clauses, if ever needed, must
  be product artifact decisions with provenance, not RTS patches.

### Product diffs

Own API, IL/body, and eventually C# source/artifact comparison.

RTS should not introduce a private metadata deep-compare engine. Product diff
capabilities should answer both user-facing questions and RTS artifact-fidelity
questions:

| Product diff | Owner | Example question |
| --- | --- | --- |
| API diff | Metadata | Did the public/selected callable surface change? |
| IL/body diff substrate | Instructions | Which canonical IL operations or body coordinates changed? |
| Body-signal diff | Analysis | Was unsafe code added? Did calls, allocations, or throws change? |
| C# artifact/source diff | Decompiler/product artifact layer | Did source shape change, such as a new switch case? |
| Unified diff projection | Research | How do API, IL, and C# evidence line up for one member? |

The first implementation assignment is the low-level/high-level product split:
Instructions owns canonical IL/body diff plumbing, and Analysis owns the nice
body-signal UX API. Research can later join API, IL, and C# evidence into unified
rows such as `switch-case-added`. RTS consumes those product diffs after it
compiles a product artifact.

### Module context

Module-level context belongs to the product artifact when it affects the emitted
C# or compiled metadata. RTS can request module context, but it should not
assemble it from scratch. Assembly/module attributes should be preserved because
they affect binding, diagnostics, generated IL, or a named metadata comparison
scope, not merely because they exist in the fixture assembly.

### ReturnToSender harness

Own orchestration and proof.

Responsibilities:

1. select target method;
2. construct an `ArtifactRequest`;
3. ask product code for a C# artifact;
4. compile the artifact;
5. invoke product API and IL/body diffs for original vs artifact assemblies;
6. optionally invoke product C# artifact/source diffs when available;
7. classify failure if artifact production, compilation, or product diff
   comparison cannot run.

Roslyn diagnostics remain useful, but as membership growth and validation
feedback, not as the primary shape architecture.

## Minimal contracts

These are intentionally conceptual, but they need this level of precision before
implementation starts.

```text
ArtifactRequest
  OriginalAssembly: AssemblyIdentity
  Target: TypeIdentity | MemberIdentity
  ArtifactKind: type | member | module | source-file
  ClosurePolicy: explicit-members | referenced-members | product-default
  BodyPolicy: declarations-only | include-target-body | include-selected-bodies
  ApiDiffScope: ApiDiffScope
  IlDiffScope: IlDiffScope
  CSharpDiffScope: CSharpDiffScope?
```

```text
ProductArtifact
  Request: ArtifactRequest
  Source: string
  SourceFacts: IReadOnlyList<FactIdentity>
  Diagnostics: IReadOnlyList<ProductArtifactDiagnostic>
```

```text
ArtifactDiffResult
  Api: ApiDiff?
  IL: IlBodyDiff?
  BodySignals: BodySignalDiff?
  CSharp: CSharpArtifactDiff?
  Unified: ResearchUnifiedDiff?
```

`ProductArtifact.Source` is a product output, not an RTS construction. RTS
compiles it and feeds original/artifact assemblies into product diff APIs.

## Project-under-test and consumption boundary

The boundary is not "Research vs Roslyn." Decompiler, Analysis, Research, and
shared services are all project code. ReturnToSender is allowed to depend on
that project code because it is the thing being tested. Roslyn is different: it
is the consumption oracle used to compile the reconstructed source and judge
whether a normal C# consumer could bind it.

Project code should remain SRM-only, NativeAOT-friendly, and free of
inspected-assembly loading. The Roslyn dependency belongs in ReturnToSender and
other harness-only tooling, not in the libraries that implement inspection,
analysis, research facts, or decompiler output.

Project code dependencies:

```text
dotnet-inspect CLI
  -> ILInspector.Research
  -> ILInspector.Analysis
  -> ILInspector.Instructions
```

ReturnToSender dependencies:

```text
tools/ReturnToSender
  -> ILInspector.Research
  -> ILInspector.Analysis
  -> ILInspector.Decompiler
  -> Roslyn
```

No product assembly should reference the tools-only ReturnToSender project.

## Type identity and the TypeRef duplication

There are currently separate `TypeRef` models in Analysis and Decompiler. That
was understandable while those libraries evolved independently, but a
fact-planned harness will stress the duplication.

The recent ILReader de-duplication suggests the right direction:

```text
shared substrate first
Analysis and Decompiler consume it
specialized layers add interpretation above it
```

For type identity, Phase 1 should either extract a shared substrate or use
explicit adapters while measuring whether the duplicate models are becoming
friction. The goal is substrate convergence like the shared ILReader work, not a
new harness-owned type system.

The shared substrate should carry identity:

- assembly identity;
- namespace;
- metadata name;
- metadata definition token or equivalent stable type-definition identity;
- generic owner identity for generic parameters;
- generic parameter index and kind;
- generic instantiation;
- array/pointer/byref shape;
- function-pointer shape if the substrate must serve Decompiler parity.

It should not absorb every consumer-specific adornment. Analysis can add trust
facts such as authentic protobuf/framework identity. Decompiler can add
rendering/provenance facts such as value-type hints, inline-array facts, custom
modifiers, and printer-specific display rules.

Placement is not free. `ILInspector.Instructions` is the current shared ancestor
for Analysis and Decompiler, but it is intentionally coarse. Putting rich type
identity there risks bloating the instruction substrate. `MetadataPrimitives` is
another candidate, but using it would require new dependency edges. A new shared
metadata-identity library may be cleaner if the substrate grows beyond simple
identity records.

Temporary adapters are acceptable only if:

- they are owned by the tools-only harness layer;
- every lossy conversion records a diagnostic;
- no new product API exposes adapter-specific types;
- the MVP includes a decision point to extract shared identity if adapters become
  noisy or incomplete.

## StackType in the new design

`StackType` helps, but it is not the type identity carrier.

Use `StackType` and `StackValue` for:

- receiver kind at a callsite;
- managed-pointer vs object-reference distinctions;
- value-type vs object-reference evidence;
- producer-offset provenance;
- stack-shape evidence when classifying a callsite.

Do not use `StackType` for:

- protobuf `IMessage<TSelf>` identity;
- generic type arguments;
- base/interface clauses;
- member signatures;
- deciding whether a metadata type is trusted framework or generated protobuf.

Example:

```text
IL offset IL_0032 calls MessageParser<T>.ParseFrom(...)
StackType says arg0 is ObjectReference produced at IL_002A.
Metadata says T is ApplicationInformationRequest.
Research says ApplicationInformationRequest is protobuf-generated.
Product artifact includes IMessage<ApplicationInformationRequest>.
```

StackType supplies flow/provenance evidence. Instruction offsets and metadata
handles anchor facts. Type identity supplies semantic facts. The planner joins
them for compile-back.

## Generated-family facts: protobuf pilot

Protobuf is a good first generated-family pilot because the shape is common,
structured, and metadata-visible.

### Detection facts

A reusable protobuf fact producer should detect:

- authentic `Google.Protobuf` reference;
- type implements `Google.Protobuf.IMessage<TSelf>`;
- static `Parser`;
- static `Descriptor`;
- `MergeFrom`;
- `WriteTo`;
- `CalculateSize`;
- `Clone`;
- optional nested generated types.

These should become typed facts, not strings:

```text
GeneratedFamilyFact
  family: protobuf-message
  type: Aspire.DashboardService.Proto.V1.ApplicationInformationRequest
  selfInterface: Google.Protobuf.IMessage<ApplicationInformationRequest>
  trustedProtobuf: true
```

### Artifact requirements

The harness-side planner translates those facts into artifact requirements:

```text
TypeRequirement
  type: ApplicationInformationRequest
  interfaces:
    IMessage<ApplicationInformationRequest>
  explicitProperties:
    IMessage.Descriptor : MessageDescriptor
  staticProperties:
    Parser : MessageParser<ApplicationInformationRequest>
    Descriptor : MessageDescriptor
  methods:
    MergeFrom(ApplicationInformationRequest other) : void
    WriteTo(CodedOutputStream output) : void
    CalculateSize() : int
```

Product artifact creation consumes typed requirements and emits C# artifacts.
ReturnToSender compiles those artifacts and compares metadata.

## Integrations and issue 2033 pattern

Issue 2033 proposes moving ecosystem integration knowledge below the CLI and
modeling it as typed Research facts keyed by member, type, or IL offset. The
fact-planned harness should follow the same reusable-fact pattern, with one
important boundary: the harness consumes facts constructively, but the
compile-back plan remains outside Research.

Shared pattern:

1. detection below CLI;
2. typed facts in Research when they are reusable;
3. multiple consumers;
4. no product-path contamination.

Consumers:

| Consumer | Uses facts for |
| --- | --- |
| CLI Integrations | assembly/member summaries and rollups |
| Analysis performance triage | amortization and setup-time heuristics |
| Offset/Facts views | per-offset explanation |
| ReturnToSender | product artifact requests and metadata-compare scopes |

The harness is stricter than display-only consumers. If facts are too vague,
stringly typed, or incomplete, the product artifact will not compile or will not
match the fixture metadata. That makes the harness a useful stress test for
Research facts, but not the owner of Research itself.

## Relationship to PR 2030

PR 2030 was the earlier compile-back reconstruction design proposal. It is now
closed and superseded by this spec.

The useful parts that carry forward are:

- the problem framing for whole-module skeletons vs target closure;
- the evidence that hard rows need more than broad skeleton patches;
- the current `CB_CLUSTER` compiler-driven membership algorithm.

What changes here is the strategic answer. ReturnToSender replaces the older
"continue the reconstruction path" framing with a fresh, tools-side architecture
for requesting product C# artifacts and comparing their compiled metadata back
to fixture metadata.

## Whole-module vs closure

Fact planning does not eliminate the scope distinction.

| Scope | Role with artifact round-trip |
| --- | --- |
| Whole-module | Cheap smoke pass. Request product-default artifacts only; avoid broad speculative surfaces. |
| Current `CB_CLUSTER` | Generic hard-row membership path. Keep compiler-driven root growth. |
| Product artifact closure | Hard-row artifact request for roots selected by target evidence and compiler diagnostics. |

The new design makes artifact shape more principled:

```text
Current hard-row loop:
  compile -> Roslyn missing-symbol error -> add root -> emit generic skeleton

Artifact round-trip loop:
  compile -> Roslyn missing-symbol error -> add root
  target evidence + typed facts -> product artifact request -> product C# artifact
  -> compile -> product diffs
```

Roslyn diagnostics remain feedback:

- if a target-assembly root is missing, add it through the generic closure loop;
- if a product artifact fact is missing, add a product producer or requirement;
- if growth becomes unsafe, emit a named bail reason;
- if the body itself is invalid, classify as product/source-shape frontier.

During ramp-up, targets without a product artifact producer can route through the
current generic cluster path for continuity rather than straight to
`RecompileFail`. That is a compatibility bridge, not a strategic endpoint. The
desired end state is that ReturnToSender requests product artifacts and compares
compiled metadata, with the old skeleton path retained only for comparison or
emergency fallback.

## Performance and budgets

The planner should be lazy and bounded.

Rules:

- query facts for the target method and current closure roots, not the whole
  module by default;
- preserve root and iteration budgets from the current cluster path;
- cap producer expansion per target and report when a cap is hit;
- avoid whole-corpus eager Research scans unless an explicit diagnostic mode asks
  for them;
- cache metadata and fact lookups by assembly and target root.

A producer that requires whole-module precomputation needs an explicit cost
model and should be opt-in until measured.

## MVP design

Build ReturnToSender alongside the current harness so the replacement can be
measured without destabilizing existing gates. Do not mutate the current
whole-module skeleton into the new architecture. Cherry-pick reusable pieces
rather than carrying forward the old skeleton shape.

### MVP scope

1. Choose a small target population:
   - one generated protobuf fixture;
   - one Aspire resource/builder fixture;
   - one real Aspire witness method from the cap-25 run.
2. Define tools-only `ArtifactRequest` and `ProductArtifact`.
3. Define the product artifact API that accepts an `ArtifactRequest` and returns
   C# source plus provenance.
4. Define the first product diff primitive needed by RTS:
   - `Instructions`: canonical IL/body diff substrate;
   - `Analysis`: body-signal diff UX, starting with unsafe added/removed.
5. Keep current `CB_CLUSTER` membership growth only as request-policy feedback,
   not as a source generator.
6. Add reusable fact producers where they belong:
   - protobuf message facts in Research if reusable by CLI/Analysis views;
   - Aspire resource collection/resource annotation facts only if they are useful
     beyond the harness; otherwise keep them harness-local.
7. Request a product artifact for each MVP target.
8. Compile the product artifact.
9. Invoke product API and IL/body diffs between original and artifact assemblies.
10. Compare selected opcode streams through the product IL diff substrate.
11. Emit artifact-request and product-diff reports.

### MVP success criteria

The MVP is successful if:

- it can request a product artifact for the chosen fixtures;
- it compiles the returned artifact without RTS-side C# construction;
- product diffs identify matching and mismatching API/IL/body shape;
- selected bodies compare through the IL/body diff oracle;
- every emitted product artifact can be traced back to typed metadata, Research,
  Analysis, or Decompiler facts;
- every failure has a named layer and reason;
- no product assembly references Roslyn;
- no product-path code depends on the new harness.

## Reporting contract

ReturnToSender should report both proof and planning, but it must still map to
the existing compile-back status buckets.

| ReturnToSender detail | Existing status |
| --- | --- |
| artifact compiles, product diffs match requested scopes | `Exact` |
| artifact compiles, IL/body diff mismatches selected bodies | `OpcodeDiff` |
| artifact compiles, API/body-signal/C# diff mismatches requested scope | `OpcodeDiff` or diff detail under the selected top-level status |
| product artifact cannot be produced | `ContextFail` |
| artifact source does not compile | `RecompileFail` |
| unsupported product body shape before compile | `RecompileFail` or `ContextFail`, with explicit reason |

The existing corpus sensor gates on `Exact`, `OpcodeDiff`, `RecompileFail`, and
`ContextFail`. ReturnToSender planning reasons should be structured details
underneath those statuses, not replacement top-level metrics.

Example report:

```text
ReturnToSender over N targets

  Exact         : X
  OpcodeDiff    : Y
  RecompileFail : Z
  ContextFail   : A

Plan layers:
  artifact request      : resolved / failed
  product artifact      : resolved / failed
  reference closure     : resolved / failed
  artifact compilation  : resolved / failed
  API diff              : matched / mismatched / failed / not requested
  IL/body diff          : matched / mismatched / failed / not requested
  C# diff               : matched / mismatched / failed / not requested
  unified Research diff : matched / mismatched / failed / not requested
  generated-family facts: resolved / failed

Examples:
  Target::Method
    status: RecompileFail
    layer : product artifact
    reason: requested type artifact did not include required generic constraint
```

## Migration plan

### Phase 1: spec, identity, and data model

- Land this design.
- Decide shared TypeIdentity extraction vs explicit adapters with measured
  friction points.
- Define `ArtifactRequest`, `ProductArtifact`, diff scopes, and the product
  artifact API.
- Keep Research scoped to reusable facts.

### Phase 2: protobuf pilot

- Add protobuf generated-family fact producer if the facts are reusable by
  non-harness consumers.
- Add product artifact support for protobuf type artifacts.
- Prove one fixture and one Aspire witness.

### Phase 3: Aspire collection/resource pilot

- Add typed Aspire resource facts only where they are reusable.
- Replace hardcoded collection skeleton surfaces with product artifacts consumed
  by the new harness path.
- Measure cap-25 movement against the post-2015 and post-2025 baselines.

### Phase 4: replacement integration

- Use product artifact round-trip as the hard-row replacement candidate.
- Compare whole-module, current closure, and fact-planned closure on the same
  target set.
- Promote ReturnToSender to the default hard-row path once it shows better
  leverage than the current skeleton architecture.

### Phase 5: TypeRef de-duplication

- If adapters are stable, keep them local and transitional.
- If adapters are lossy or duplicated, extract shared type identity substrate,
  following the ILReader de-dup pattern.

## Open questions

1. Where should the shared type identity substrate live: a new shared
   metadata-identity library, `ILInspector.MetadataPrimitives` with new edges, or
   a carefully bounded addition to `ILInspector.Instructions`?
2. Which generated-family facts are broadly reusable enough for Research vs
   harness-local?
3. Which facts are type-level vs offset-level in Research?
4. Should issue 2033 integration facts and compile-back reconstruction facts
   share one fact registry or use sibling registries?
5. What is the first real witness set: protobuf-only, Aspire-only, or both?
6. How should the new harness report bails so they are actionable without
   becoming another giant diagnostic wall?
7. Should the working name stay ReturnToSender or switch to LoopBack?

## Appendix: future test-stack considerations

ReturnToSender should also help reframe the broader decompiler test stack in a
way that looks familiar to JIT and Roslyn contributors. The goal is not to copy
their infrastructure, but to make this repository's testing vocabulary more
modern and recognizable: layered fixtures, explicit oracles, typed intermediate
artifacts, and corpus gates with named failure modes.

### Fixture ladder

Fixtures should form a progressive ladder:

1. minimal synthetic source or IL fixture;
2. generated-family fixture, such as protobuf or Aspire resource patterns;
3. real package witness;
4. capped corpus run;
5. daily corpus gate.

Each rung should answer a different question. A tiny fixture proves the
mechanism. A generated-family fixture proves the shape family. A real witness
proves the family occurs outside the lab. Corpus and daily gates prove the change
does not regress broad behavior.

### Oracle taxonomy

Tests should name the oracle they exercise:

| Oracle | Question answered |
| --- | --- |
| parse | Is emitted C# syntactically valid? |
| bind | Can a normal C# consumer resolve the source? |
| opcode parity | Does compile-back preserve the IL opcode stream? |
| fact agreement | Do independently produced facts agree on the same IL/member/type evidence? |
| product diff parity | Do product API/IL/C# diffs match for the requested artifact scope? |
| source artifact validity | Does the product artifact compile without RTS-side C# construction? |
| API/platform resolution | Did package/framework/shared-service resolution select the intended asset? |
| performance-signal precision | Does a signal identify useful targets without noisy over-reporting? |

This taxonomy should keep product failures separate from harness orchestration
failures. A body that the decompiler prints incorrectly is a different class of
bug from a product artifact that omitted the right generic constraint or assembly
attribute, and both are different from RTS failing to request the intended
product diff scope.

### Typed facts before projection

Analysis and Research tests should validate typed facts before validating text
views. CLI text, annotated source, and product artifacts consumed by
ReturnToSender should be consumers of the facts, not the only proof that the
facts exist.

This suggests a common pattern:

1. prove a fact producer on reduced IL/source;
2. prove fact joins against metadata and instruction offsets;
3. prove one or more projections, such as CLI output, source annotations, or
   product artifacts consumed by ReturnToSender.

### Shared service tests

Assembly, package, framework, and source selection should have a dedicated
service test suite. The CLI, Analysis, Research, and ReturnToSender all need the
same answers. If each layer reimplements resolution locally, test failures become
hard to classify because the fixture may be testing resolver drift rather than
decompiler or analysis behavior.

The same principle applies to any future shared substrate, including type
identity. Shared substrates should have direct tests before they are consumed by
printers, writers, or CLI projections.

### Modern failure reporting

Large test systems age better when failures include both the top-level status and
the failing layer. ReturnToSender should model this, but the pattern can apply
elsewhere:

```text
status: RecompileFail
layer : product artifact
reason: required assembly attribute omitted from source artifact
oracle: bind
```

That shape is easier to route than a wall of compiler diagnostics or a text diff
without provenance.

### Broader implication

ReturnToSender is the first large-scale use of this architecture, but the same
testing style should influence other infrastructure:

- decompiler fixtures should use the fixture ladder and oracle taxonomy;
- Analysis and Research tests should prefer typed fact assertions over projected
  text assertions;
- CLI tests should focus on rendering and user-facing contract after lower
  layers have proved facts;
- daily/corpus tests should report movements in the same named statuses and
  layers that targeted tests use.

The desired end state is a coherent test stack: small fixtures explain why a
behavior is correct, real witnesses prove it matters, and corpus gates prove it
scales.

## Adversarial review results incorporated

This revision incorporates adversarial feedback from Claude Opus 4.8,
Gemini 3.1 Pro, and MAI-Code-1 Flash:

- Research is fact registry/projection only; the planner is tools-only.
- Current `CB_CLUSTER` membership growth is preserved as an algorithm, not as the
  long-term architecture.
- Product artifact requests and metadata comparison have explicit contracts and
  typed identities.
- TypeRef de-duplication has stronger transition criteria and identity fields.
- StackType is bounded to stack-shape/provenance evidence.
- New reporting reasons map back to existing compile-back status buckets.

## Recommendation

Build **ReturnToSender** alongside the current DecompilerHarness as the
replacement track. Keep compiler-driven closure membership as a reusable
algorithm and make fact producers support product artifact requests. Start with
protobuf and Aspire because they expose structured, metadata-visible
generated-family problems.

Use adapters deliberately while watching for friction, and expect the TypeRef
duplication to motivate a shared type identity substrate if the adapters become
noisy. Keep StackType as flow and provenance evidence, not as type identity.

This is a new harness architecture, not another round of skeleton patching.
