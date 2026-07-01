# Fact-planned compile-back harness

## Summary

The next compile-back harness should start fresh alongside the current
`DecompilerHarness` and later cherry-pick proven pieces. The design point is not
"whole-module skeleton, but with a closure bias." It is a new architecture:
**fact-planned reconstruction**.

The new harness should:

1. gather typed facts from Metadata, Instructions, Analysis, and Research;
2. turn those facts into a structured reconstruction plan;
3. produce structured type shells;
4. print those shells as C# declarations;
5. insert the existing `CSharpPrinter` method body;
6. compile and compare opcode streams.

The current harness mixes planning, source generation, Roslyn diagnostics, and
opcode comparison in one place. That makes every improvement feel like another
string-emission patch. The new harness should separate those concerns so fixes
are typed, reviewable, and reusable.

## Non-goals

- Do not rewrite the product decompiler.
- Do not put Roslyn, compile-back, or inspected-assembly loading in
  `ILInspector.Decompiler`.
- Do not make the harness a general source generator for arbitrary assemblies.
- Do not create a permanent third type system if a shared type identity substrate
  can serve the role.
- Do not remove the current harness until the new path proves itself on real
  corpora.

## Existing assets to reuse

| Asset | Role in the new harness |
| --- | --- |
| `CSharpPrinter` | Keep rendering target method bodies. |
| opcode comparison | Keep as the compile-back fidelity oracle. |
| `ILInspector.Instructions` | Shared IL decode, block identity, typed stack (`StackType`) substrate. |
| `ILInspector.Analysis` | Whole-assembly IL facts, direct calls, body indexes, type/member references. |
| `ILInspector.Research` | Fact registry and projection layer; becomes the plan orchestration layer. |
| package/framework resolution | Keep and harden as reference closure. |
| generated fixtures | Keep as reduced, reviewable proof cases. |
| current `CB_CLUSTER` concept | Keep the idea of target closure; replace ad hoc planning. |

## Architecture

```text
                    +---------------------------+
                    |  CompileBackPlanner       |
                    |  (Research orchestration) |
                    +-------------+-------------+
                                  |
                                  v
Metadata / Instructions / Analysis / Research facts
                                  |
                                  v
                    +---------------------------+
                    |  ReconstructionPlan       |
                    +-------------+-------------+
                                  |
                                  v
                    +---------------------------+
                    |  TypeProducer             |
                    |  structured type shells   |
                    +-------------+-------------+
                                  |
                                  v
                    +---------------------------+
                    |  TypePrinter              |
                    |  C# declarations          |
                    +-------------+-------------+
                                  |
                                  v
 CSharpPrinter target body ---> compile shell ---> Roslyn ---> opcode compare
```

## Layer responsibilities

### Metadata and Services

Own package, assembly, reference, and dependency resolution.

Responsibilities:

- locate target assemblies;
- build exact NuGet dependency closure;
- choose one package version per id;
- locate shared frameworks;
- expose metadata handles and signature-level type facts.

This layer should stay SRM-only and should not depend on the decompiler.

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
- closure candidate discovery from actual target method evidence.

Analysis should tell us what the method body actually references before the
harness asks Roslyn to guess what is missing.

### Research

Own fact registration, joins, and planning.

Today Research projects facts into annotated source/IL/Facts views. In the new
harness, Research becomes constructive: facts do not merely annotate output; they
plan the reconstruction shell.

Responsibilities:

- register reconstruction fact producers;
- join Analysis facts with Metadata facts and decompiler projection facts;
- produce a `ReconstructionPlan`;
- record bail reasons and incomplete facts;
- expose plan diagnostics for reports.

This is the same architectural move #2033 proposes for integrations: move
ecosystem knowledge below the CLI, represent it as typed facts, and let multiple
consumers use it. The new harness is the first large constructive consumer of
that pattern.

### TypeProducer

Own structured type shell construction.

Input: `ReconstructionPlan`.

Output: structured declarations, not text.

Example shape:

```text
TypeShell
  identity: Aspire.DashboardService.Proto.V1.ApplicationInformationRequest
  kind: class
  base: object
  interfaces:
    Google.Protobuf.IMessage<ApplicationInformationRequest>
  explicitProperties:
    Google.Protobuf.IMessage.Descriptor : Google.Protobuf.Reflection.MessageDescriptor
  members:
    Parser
    Descriptor
    MergeFrom(...)
    WriteTo(...)
    CalculateSize()
```

TypeProducer should be deterministic and auditable. It should not ask Roslyn
diagnostics what source to print. It should consume typed reconstruction facts.

### TypePrinter

Own C# declaration rendering for structured shells.

This is the type-level counterpart to `CSharpPrinter`, which should continue to
own method bodies. TypePrinter prints:

- namespaces;
- type declarations;
- base/interface clauses;
- generic parameters and constraints;
- field/property/method signatures;
- generated-family explicit stubs;
- nested type declarations.

TypePrinter should be a renderer, not a planner.

### CompileBackHarness vNext

Own orchestration and proof.

Responsibilities:

1. select target method;
2. ask Research for reconstruction plan;
3. ask TypeProducer for type shells;
4. ask TypePrinter for declaration text;
5. ask CSharpPrinter for the target method body;
6. compile;
7. compare opcodes;
8. classify failure if compile-back cannot run.

Roslyn diagnostics remain useful, but as validation/feedback, not as the primary
planning architecture.

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

For type identity, the likely path is:

1. short term: adapters between `Analysis.TypeRef` and decompiler/harness type
   structures;
2. medium term: extract a shared type identity substrate;
3. long term: keep consumer-specific facts outside that shared identity.

The shared substrate should carry identity:

- assembly identity;
- namespace;
- metadata name;
- generic instantiation;
- array/pointer/byref shape;
- generic parameter identity.

It should not absorb every consumer-specific adornment. Analysis can add trust
facts such as authentic protobuf/framework identity. Decompiler can add
rendering/provenance facts such as value-type hints, inline-array facts, custom
modifiers, and function-pointer rendering.

The new harness should not create a permanent third `ReconstructionTypeRef`
unless it is explicitly transitional. If adapters become painful or lossy, that
is evidence to continue the de-duplication path and extract shared type identity.

## StackType in the new design

`StackType` helps, but it is not the type identity carrier.

Use `StackType` and `StackValue` for:

- receiver kind at a callsite;
- managed-pointer vs object-reference distinctions;
- value-type vs object-reference evidence;
- producer-offset provenance;
- anchoring Research facts to IL offsets.

Do not use `StackType` for:

- protobuf `IMessage<TSelf>` identity;
- generic type arguments;
- base/interface clauses;
- member signatures.

Example:

```text
IL offset IL_0032 calls MessageParser<T>.ParseFrom(...)
StackType says arg0 is ObjectReference@IL_002A.
Metadata says T is ApplicationInformationRequest.
Research says ApplicationInformationRequest is protobuf-generated.
TypeProducer emits IMessage<ApplicationInformationRequest>.
```

StackType supplies flow/provenance evidence. Type identity supplies semantic
facts. Research joins them.

## Generated-family facts: protobuf pilot

Protobuf is a good first generated-family pilot because the shape is common,
structured, and metadata-visible.

### Detection facts

A protobuf producer should detect:

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

Research translates those facts into requirements:

```text
TypeRequirement
  type: ApplicationInformationRequest
  interfaces:
    IMessage<ApplicationInformationRequest>
  explicitProperties:
    IMessage.Descriptor : MessageDescriptor
  staticMembers:
    Parser
    Descriptor
  methods:
    MergeFrom(...)
    WriteTo(...)
    CalculateSize()
```

TypeProducer consumes requirements and emits the structured shell. TypePrinter
prints the declarations.

## Integrations and #2033 pattern

\#2033 proposes moving ecosystem integrations below the CLI and modeling them as
member/offset-keyed Research facts. The fact-planned harness should follow the
same pattern, with one addition: some reconstruction facts are type-level rather
than offset-level.

Shared pattern:

1. detection below CLI;
2. typed facts in Research;
3. multiple consumers;
4. no product-path contamination.

Consumers:

| Consumer | Uses facts for |
| --- | --- |
| CLI Integrations | assembly/member summaries and rollups |
| Analysis performance triage | amortization and setup-time heuristics |
| Offset/Facts views | per-offset explanation |
| Compile-back harness vNext | reconstruction requirements |

The harness is stricter than display-only consumers. If facts are too vague,
stringly typed, or incomplete, the shell will not compile. That makes it a good
forcing function for the Research architecture.

## Whole-module vs closure

Fact planning does not eliminate the scope distinction.

| Scope | Role with fact planning |
| --- | --- |
| Whole-module | Cheap smoke pass. Use TypeProducer facts where safe, but avoid broad speculative surfaces. |
| Closure | Authoritative hard-row path. Build a target-specific plan from Analysis/Research facts. |

The new design makes closure more principled:

```text
Current hard-row loop:
  compile -> Roslyn error -> patch skeleton -> repeat

Fact-planned loop:
  target method evidence -> typed facts -> reconstruction plan -> type shell -> compile -> validate
```

Roslyn diagnostics become feedback:

- if a planned fact is missing, add a producer or requirement;
- if growth becomes unsafe, emit a named bail reason;
- if the body itself is invalid, classify as product/source-shape frontier.

## MVP design

Build the new harness alongside the current one. Do not mutate the current
whole-module skeleton into the new architecture. Cherry-pick reusable pieces when
the new path proves itself.

### MVP scope

1. Choose a small target population:
   - one generated protobuf fixture;
   - one Aspire resource/builder fixture;
   - one real Aspire witness method from the cap-25 run.
2. Define `ReconstructionPlan`.
3. Define `TypeShell` and `TypeMemberShell` records.
4. Implement TypePrinter for a minimal subset:
   - class/interface declarations;
   - namespace nesting;
   - base/interface clauses;
   - generic constraints;
   - properties;
   - methods as throwing stubs;
   - explicit interface property stubs.
5. Add fact producers:
   - protobuf message producer;
   - Aspire resource collection/resource annotation producer;
   - generic base/interface constraint producer;
   - reference closure producer.
6. Compile shell + method body.
7. Compare opcode stream.
8. Emit plan report.

### MVP success criteria

The MVP is successful if:

- it matches or beats the current harness on the chosen fixtures;
- it converts at least one real Aspire `RecompileFail` into `Exact` or
  `OpcodeDiff`;
- every emitted type shell can be traced back to a typed fact;
- every failure has a named layer and reason;
- no product assembly references Roslyn;
- no product-path code depends on the new harness.

## Reporting contract

The new harness should report both proof and planning:

```text
Compile-back vNext over N targets

  exact opcode match      : X
  opcode diff             : Y
  not safely capturable   : Z
  product-body failure    : A
  reconstruction failure  : B

Plan layers:
  reference closure       : resolved / failed
  type identity closure   : resolved / failed
  member surface closure  : resolved / failed
  generated-family facts  : resolved / failed

Examples:
  Target::Method
    status: not safely capturable
    layer : generated-family
    reason: protobuf message detected but self-interface fact missing
```

## Migration plan

### Phase 1: spec and data model

- Land this design.
- Define `ReconstructionPlan`, `TypeShell`, `TypePrinter` shape.
- Decide short-term TypeRef adapter vs shared type identity extraction.

### Phase 2: protobuf pilot

- Add protobuf generated-family fact producer.
- Add TypeProducer/TypePrinter support for protobuf shell.
- Prove one fixture and one Aspire witness.

### Phase 3: Aspire collection/resource pilot

- Add typed Aspire resource facts.
- Replace hardcoded collection skeleton surfaces with fact-produced shells.
- Measure cap-25 movement against #2015/#2025 baselines.

### Phase 4: closure integration

- Use the fact-planned shell as a new harness mode, not a replacement yet.
- Compare whole-module, current closure, and fact-planned closure on the same
  target set.
- Decide whether fact-planned closure becomes default hard-row path.

### Phase 5: TypeRef de-duplication

- If adapters are stable, keep them.
- If adapters are lossy or duplicated, extract shared type identity substrate,
  following the ILReader de-dup pattern.

## Open questions

1. Where should the shared type identity substrate live: `ILInspector.Instructions`,
   a new shared metadata library, or `ILInspector.MetadataPrimitives`?
2. Should TypeProducer live in Research, a new harness library, or a new
   reconstruction library below the harness?
3. Which facts are type-level vs offset-level in Research?
4. Should #2033 integration facts and compile-back reconstruction facts share one
   fact registry or use sibling registries?
5. What is the first real witness set: protobuf-only, Aspire-only, or both?
6. How should the new harness report bails so they are actionable without becoming
   another giant diagnostic wall?

## Recommendation

Build a **fact-planned compile-back harness** alongside the current
DecompilerHarness. Use it as the first large constructive consumer of the
Research architecture. Start with protobuf and Aspire because they expose the
right kind of structured, metadata-visible generated-family problems.

Use adapters at first, but expect the TypeRef duplication to motivate a shared
type identity substrate if the adapters become noisy. Keep StackType as flow and
provenance evidence, not as type identity.

This is a new harness architecture, not another round of skeleton patching.
