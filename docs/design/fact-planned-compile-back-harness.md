# ReturnToSender: fact-planned compile-back harness

## Summary

The next compile-back harness, working name **ReturnToSender**, should start
fresh alongside the current
`DecompilerHarness` and later cherry-pick proven pieces. The design point is not
"whole-module skeleton, but with a closure bias." It is a new architecture:
**fact-planned shell reconstruction**.

The important distinction is closure membership vs shell-shape fidelity:

- closure membership decides which target-assembly roots belong in the compile
  unit;
- shell-shape fidelity decides what declarations, interfaces, constraints, and
  members those roots must expose so the real decompiled method body compiles.

The existing `CB_CLUSTER=1` path already has a strong generic answer for closure
membership: compile, read the compiler's missing-symbol diagnostics, add the
named same-assembly roots, and repeat until the closure stops growing or hits a
budget. The new harness should preserve that compiler-driven membership loop and
replace ad hoc skeleton patching with typed shell-shape enrichment.

The new harness should:

1. gather typed facts from Metadata, Instructions, Analysis, and Research;
2. keep compile-driven closure growth as the generic membership algorithm;
3. turn facts and membership into a structured reconstruction plan;
4. produce structured module and type shells with typed signatures;
5. print those shells as C# declarations and assembly/module context;
6. insert the existing `CSharpPrinter` method body;
7. compile and compare opcode streams.

## Non-goals

- Do not rewrite the product decompiler.
- Do not put Roslyn, compile-back, or inspected-assembly loading in shipped
  product libraries.
- Do not make `ILInspector.Research` own compile-back orchestration.
- Do not make the harness a general source generator for arbitrary assemblies.
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
| `CSharpPrinter` | Keep rendering target method bodies. |
| opcode comparison | Keep as the compile-back fidelity oracle. |
| current `CB_CLUSTER` loop | Keep generic compiler-driven closure membership. |
| `ILInspector.Instructions` | Shared IL decode, block identity, typed stack (`StackType`) substrate. |
| `ILInspector.Analysis` | Whole-assembly IL facts, direct calls, body indexes, type/member references. |
| `ILInspector.Research` | Typed fact registry, joins, and projections only. |
| assembly/package resolution service | Generalize and reuse for CLI and harness reference closure. |
| generated fixtures | Keep as reduced, reviewable proof cases. |

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
        | ReturnToSenderPlanner     |
        | tools-only reconstruction |
        +-------------+-------------+
                      |
                      v
        +---------------------------+
        | ReconstructionPlan        |
        | roots + shell requirements|
        +-------------+-------------+
                      |
                      v
        +---------------------------+
        | TypeProducer              |
        | typed TypeShell records   |
        +-------------+-------------+
                      |
                      v
        +---------------------------+
        | TypePrinter               |
        | C# declarations           |
        +-------------+-------------+
                      |
                      v
        +---------------------------+
        | ModuleWriter              |
        | compilation-unit context  |
        +-------------+-------------+
                      |
                      v
CSharpPrinter body -> compile shell -> Roslyn -> opcode compare
```

`ReturnToSenderPlanner`, `ReconstructionPlan`, `TypeProducer`, `TypeShell`, and
`TypePrinter` should live in a tools-only reconstruction project under `tools/`
or another non-shipped harness assembly. `ModuleWriter` belongs there too if it
is harness-specific. They should not live in
`ILInspector.Research` or `ILInspector.Decompiler`.

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
ReturnToSender. Harness-only policy, such as "which closure roots should be
emitted as C# shells", belongs above this service.

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
for saying "this is protobuf type X."

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
reasons or emit source shells.

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
- host `TypeProducer`, `TypeShell`, or `TypePrinter`.

### ReturnToSenderPlanner

Own tools-only reconstruction planning.

Responsibilities:

1. select the target method;
2. run or reuse compiler-driven closure membership;
3. query Metadata, Analysis, and Research facts for the target closure;
4. build a `ReconstructionPlan`;
5. record missing facts and bail reasons for harness reports.

The planner is the boundary between reusable facts and compile-back-specific
decisions. It can use Research facts, but Research should not know that a
particular fact is required to turn `RecompileFail` into `Exact`.

### TypeProducer

Own structured type shell construction.

Input: `ReconstructionPlan`.

Output: typed declarations, not text.

TypeProducer's job is not merely to reshuffle the plan. It must resolve
requirements into complete shell records:

- type kind, nesting, accessibility, generic parameters, and constraints;
- base type and interfaces;
- fields, properties, methods, events, constructors, and explicit interface
  members needed by the target body;
- typed parameter, return, receiver, and generic argument signatures;
- stub behavior that is safe for compile-back.

If TypeProducer cannot produce a typed signature, it should fail with a named
planning reason rather than emit a stringly stub such as `MergeFrom(...)`.

### TypePrinter

Own C# declaration rendering for structured shells.

TypePrinter is conceptually paired with `CSharpPrinter`, but it should not live
beside `CSharpPrinter` in `ILInspector.Decompiler` if its input types live above
Decompiler. The likely owner is the same tools-only reconstruction project as
`TypeShell`.

TypePrinter prints:

- namespaces;
- type declarations;
- base/interface clauses;
- generic parameters and constraints;
- field/property/method signatures;
- generated-family explicit stubs;
- nested type declarations.

TypePrinter should be a renderer, not a planner. It should not query Research or
read Roslyn diagnostics.

### ModuleWriter

Own compilation-unit rendering above type declarations.

ReturnToSender needs a module-level writer if it wants to preserve assembly and
module context instead of smuggling that context into TypePrinter. This includes:

- assembly attributes;
- module attributes;
- file-scoped or block-scoped namespace strategy;
- usings, aliases, and nullable context if they become necessary for fidelity;
- unsafe and compiler-option context that affects whether emitted source binds;
- ordering of generated declarations and the target body.

ModuleWriter should be narrow. It should not become a second source generator or
own type/member shape decisions. Its job is to assemble a compilable C# unit from
module facts, TypePrinter output, and the CSharpPrinter method body.

Some assembly/module attributes are noise for compile-back and should be omitted.
Others can affect binding, diagnostics, or generated IL shape. ReturnToSender
needs an explicit allowlist of preservable attributes, with each attribute traced
to a metadata fact and a compile-back reason.

### ReturnToSender harness

Own orchestration and proof.

Responsibilities:

1. select target method;
2. ask `ReturnToSenderPlanner` for a reconstruction plan;
3. ask TypeProducer for type shells;
4. ask TypePrinter for declaration text;
5. ask ModuleWriter for assembly/module context and final compilation unit;
6. ask CSharpPrinter for the target method body;
7. compile;
8. compare opcodes;
9. classify failure if compile-back cannot run.

Roslyn diagnostics remain useful, but as membership growth and validation
feedback, not as the primary shell-shape architecture.

## Minimal contracts

These are intentionally conceptual, but they need this level of precision before
implementation starts.

```text
ReconstructionPlan
  TargetMethod: MethodIdentity
  AssemblyReferences: IReadOnlyList<AssemblyReference>
  ModuleRequirements: ModuleRequirement
  ClosureRoots: IReadOnlyList<TypeIdentity>
  TypeRequirements: IReadOnlyList<TypeRequirement>
  Diagnostics: IReadOnlyList<PlanningDiagnostic>
```

```text
ModuleRequirement
  AssemblyAttributes: IReadOnlyList<AttributeRequirement>
  ModuleAttributes: IReadOnlyList<AttributeRequirement>
  NullableContext: NullableContextRequirement?
  UnsafeContext: UnsafeContextRequirement?
  SourceFacts: IReadOnlyList<FactIdentity>
```

```text
TypeRequirement
  Type: TypeIdentity
  RequiredKind: class | struct | interface | enum | delegate
  RequiredBaseType: TypeSignature?
  RequiredInterfaces: IReadOnlyList<TypeSignature>
  RequiredMembers: IReadOnlyList<MemberRequirement>
  SourceFacts: IReadOnlyList<FactIdentity>
```

```text
TypeShell
  Identity: TypeIdentity
  Kind: TypeKind
  Accessibility: Accessibility
  GenericParameters: IReadOnlyList<GenericParameterShell>
  BaseType: TypeSignature?
  Interfaces: IReadOnlyList<TypeSignature>
  Members: IReadOnlyList<TypeMemberShell>
  SourceFacts: IReadOnlyList<FactIdentity>
```

```text
TypeMemberShell
  Identity: MemberIdentity
  Kind: field | property | method | event | constructor
  Accessibility: Accessibility
  IsStatic: bool
  ExplicitInterface: TypeSignature?
  ReturnType: TypeSignature?
  Parameters: IReadOnlyList<ParameterShell>
  GenericParameters: IReadOnlyList<GenericParameterShell>
  Constraints: IReadOnlyList<GenericConstraintShell>
  StubBodyKind: none | throw | default-return | auto-property
  SourceFacts: IReadOnlyList<FactIdentity>
```

`TypeShell` and `TypeMemberShell` must carry typed signatures. They should never
store printable fragments as the source of truth.

`ModuleRequirement` should be just as evidence-backed as type requirements. A
module or assembly attribute should not be preserved because it existed in the
input assembly; it should be preserved because it affects compile-back binding,
diagnostics, generated IL, or a named fidelity experiment.

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
tools/CompileBackReconstruction
  -> ILInspector.Research
  -> ILInspector.Analysis
  -> ILInspector.Decompiler
  -> Roslyn
```

No product assembly should reference the tools-only reconstruction project.

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
TypeProducer emits IMessage<ApplicationInformationRequest>.
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

### Reconstruction requirements

The harness-side planner translates those facts into requirements:

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

TypeProducer consumes typed requirements and emits structured shells. TypePrinter
prints declarations.

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
| ReturnToSender | shell-shape requirements |

The harness is stricter than display-only consumers. If facts are too vague,
stringly typed, or incomplete, the shell will not compile. That makes the harness
a useful stress test for Research facts, but not the owner of Research itself.

## Relationship to issue 2030

Issue 2030 documents the current compile-back reconstruction problem space and
the existing closure-vs-whole-module tradeoff. This spec is narrower and newer:
it describes a fresh harness architecture for typed shell reconstruction.

The two documents should not conflict:

- issue 2030 explains why the current whole-module skeleton and current closure
  path exist;
- this spec keeps the current `CB_CLUSTER` membership algorithm and adds a
  typed shell-shape architecture beside it.

If both docs land, issue 2030 should point here as the proposed ReturnToSender
direction
rather than duplicate the same architecture.

## Whole-module vs closure

Fact planning does not eliminate the scope distinction.

| Scope | Role with fact planning |
| --- | --- |
| Whole-module | Cheap smoke pass. Use safe shell facts only; avoid broad speculative surfaces. |
| Current `CB_CLUSTER` | Generic hard-row membership path. Keep compiler-driven root growth. |
| Fact-planned closure | Hard-row shell-shape enrichment for roots selected by target evidence and compiler diagnostics. |

The new design makes shell shape more principled:

```text
Current hard-row loop:
  compile -> Roslyn missing-symbol error -> add root -> emit generic skeleton

Fact-planned loop:
  compile -> Roslyn missing-symbol error -> add root
  target evidence + typed facts -> reconstruction plan -> typed shell -> compile
```

Roslyn diagnostics remain feedback:

- if a target-assembly root is missing, add it through the generic closure loop;
- if a shell-shape fact is missing, add a producer or requirement;
- if growth becomes unsafe, emit a named bail reason;
- if the body itself is invalid, classify as product/source-shape frontier.

During ramp-up, targets without a matching fact producer can route through the
current generic cluster path for continuity rather than straight to
`RecompileFail`. That is a compatibility bridge, not a strategic endpoint. The
desired end state is that ReturnToSender has a generic shell-shape baseline plus
fact-produced enrichment, with the old skeleton path retained only for comparison
or emergency fallback.

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
2. Define the tools-only `ReconstructionPlan`.
3. Define `TypeShell`, `TypeMemberShell`, `TypeSignature`, and related identity
   records.
4. Define `ModuleRequirement` and a minimal `ModuleWriter`.
5. Keep current `CB_CLUSTER` membership growth as the closure source.
6. Implement TypePrinter for a minimal subset:
   - class/interface declarations;
   - namespace nesting;
   - base/interface clauses;
   - generic constraints;
   - properties;
   - methods as throwing stubs;
   - explicit interface property stubs.
7. Implement ModuleWriter for a minimal subset:
   - assembly/module attributes from an explicit compile-back allowlist;
   - nullable/unsafe context only when needed by a witness;
   - deterministic ordering of declarations and the target body.
8. Add reusable fact producers where they belong:
   - protobuf message facts in Research if reusable by CLI/Analysis views;
   - Aspire resource collection/resource annotation facts only if they are useful
     beyond the harness; otherwise keep them harness-local.
9. Add harness-side shell-shape producers:
   - protobuf shell producer;
   - Aspire resource collection/resource annotation shell producer;
   - generic base/interface constraint producer.
10. Compile shell + method body.
11. Compare opcode stream.
12. Emit plan report.

### MVP success criteria

The MVP is successful if:

- it matches or beats the current harness on the chosen fixtures;
- it explains any no-producer target through a named ReturnToSender baseline,
  current-harness bridge, or missing-producer reason;
- it converts at least one real Aspire `RecompileFail` into `Exact` or
  `OpcodeDiff`;
- every emitted type shell can be traced back to a typed fact, metadata fact, or
  compiler-requested closure root;
- every failure has a named layer and reason;
- no product assembly references Roslyn;
- no product-path code depends on the new harness.

## Reporting contract

ReturnToSender should report both proof and planning, but it must still map to
the existing compile-back status buckets.

| ReturnToSender detail | Existing status |
| --- | --- |
| exact opcode match | `Exact` |
| opcode stream mismatch after successful compile | `OpcodeDiff` |
| shell compilation failed | `RecompileFail` |
| target or references cannot be made compile-ready | `ContextFail` |
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
  closure membership    : resolved / failed
  reference closure     : resolved / failed
  module context        : resolved / failed
  type identity         : resolved / failed
  member surface        : resolved / failed
  generated-family facts: resolved / failed

Examples:
  Target::Method
    status: RecompileFail
    layer : generated-family
    reason: protobuf message detected but self-interface fact missing
```

## Migration plan

### Phase 1: spec, identity, and data model

- Land this design.
- Decide shared TypeIdentity extraction vs explicit adapters with measured
  friction points.
- Define `ReconstructionPlan`, `TypeShell`, `TypeMemberShell`, `TypeSignature`,
  `ModuleRequirement`, TypePrinter, and ModuleWriter shape in a tools-only
  project.
- Keep Research scoped to reusable facts.

### Phase 2: protobuf pilot

- Add protobuf generated-family fact producer if the facts are reusable by
  non-harness consumers.
- Add harness-side TypeProducer/TypePrinter support for protobuf shells.
- Prove one fixture and one Aspire witness.

### Phase 3: Aspire collection/resource pilot

- Add typed Aspire resource facts only where they are reusable.
- Replace hardcoded collection skeleton surfaces with fact-produced shells in the
  new harness path.
- Measure cap-25 movement against the post-2015 and post-2025 baselines.

### Phase 4: replacement integration

- Use the fact-planned shell as the hard-row replacement candidate.
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
| source-shape validity | Is the reconstructed shell sufficient and not speculative? |
| API/platform resolution | Did package/framework/shared-service resolution select the intended asset? |
| performance-signal precision | Does a signal identify useful targets without noisy over-reporting? |

This taxonomy should keep product failures separate from harness reconstruction
failures. A body that the decompiler prints incorrectly is a different class of
bug from a shell that failed to provide the right generic constraint or assembly
attribute.

### Typed facts before projection

Analysis and Research tests should validate typed facts before validating text
views. CLI text, annotated source, and ReturnToSender shells should be consumers
of the facts, not the only proof that the facts exist.

This suggests a common pattern:

1. prove a fact producer on reduced IL/source;
2. prove fact joins against metadata and instruction offsets;
3. prove one or more projections, such as CLI output, source annotations, or
   ReturnToSender shell requirements.

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
layer : module context
reason: required assembly attribute omitted by ModuleWriter
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
- TypeProducer and TypePrinter have explicit contracts and typed signatures.
- TypeRef de-duplication has stronger transition criteria and identity fields.
- StackType is bounded to stack-shape/provenance evidence.
- New reporting reasons map back to existing compile-back status buckets.

## Recommendation

Build **ReturnToSender** alongside the current DecompilerHarness as the
replacement track. Keep compiler-driven closure membership as a reusable
algorithm and make fact producers opt-in shell-shape enrichment. Start with
protobuf and Aspire because they expose structured, metadata-visible
generated-family problems.

Use adapters deliberately while watching for friction, and expect the TypeRef
duplication to motivate a shared type identity substrate if the adapters become
noisy. Keep StackType as flow and provenance evidence, not as type identity.

This is a new harness architecture, not another round of skeleton patching.
