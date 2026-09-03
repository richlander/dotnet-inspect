# Metadata semantic substrates

A **Metadata semantic substrate** authenticates higher-level structural meaning
from physical metadata and publishes immutable typed outcomes that several
higher layers can consume independently. It sits above raw row and blob
decoding and below consumer semantics: it establishes what the metadata
structurally asserts, and takes no position on IL-body attribution, source
reconstruction, project policy, recommendation, or presentation.

The pattern exists to break a tie. Without it, Analysis and Decompiler each
decode the same metadata into their own private shape, or one consumes the
other's interpretation and inherits a dependency that the layering forbids. A
substrate gives both a single authenticated answer that neither owns.

## Status and decision

Proposed by
[#5273](https://github.com/richlander/dotnet-inspect/issues/5273). The
normative owner is `ILInspector.Metadata`.

This document defines only the pattern's own contract — what a substrate
must derive, publish, distinguish, and refuse. It does not specify how any
existing owner changes to adopt it. Each adoption is a separate focused
effort naming this document, one owner at a time, per
[Stage implementation after locking the design](../design-scope.md#stage-implementation-after-locking-the-design).

The pattern is descriptive before it is prescriptive: three components already
satisfy most of it. Their disagreements are what this document settles.

## Precedents

Three existing components establish the shape.

| Component | Meaning authenticated |
| --- | --- |
| `StateMachineRelationshipIndex` | Compiler state-machine claims, kickoff/state-machine pairing, and exact interface roles. |
| `MemorySafetyMetadataIndex` | Module memory-safety rules markers and member unsafe-contract evidence. |
| `TypeDeclarationResult` and its probe | Type declaration, forwarding chains, and module exports. |

They already agree on more than they disagree on: each derives a relationship
rather than decoding a single row, each publishes immutable typed results
carrying the evidence behind them, and each stops a resource bound from
escaping as an exception.

They disagree on outcome vocabulary. Only type declaration models `Missing`
and `Ambiguous` as first-class declaration outcomes; the state-machine index
distinguishes an admitted optional-role absence from rejection; the
memory-safety index carries a separate evidence-state enum alongside its
result hierarchy. Settling that vocabulary is the main work below.

## Admission test

A component is a semantic substrate when **all five** hold. Failing any one
means it is an ordinary Metadata helper, or a composition that belongs to a
consumer.

1. **Derived meaning.** It establishes a relationship, contract, or
   disposition that no single metadata row states outright. Decoding one
   attribute blob, or reading one table column, is a helper.
2. **Metadata-only evidence.** Every published fact follows from metadata
   inside the declared acquisition scope. If the answer needs an IL body,
   reconstructed control flow, a PDB, source text, or a project file, it is
   not a substrate fact.

   Metadata evidence comes in two grades, and a substrate must not silently
   mix them:

   - **Structural** — the metadata tables state the relationship directly, as
     `MethodSemantics` states that a method is a property's getter.
   - **Conventional** — the relationship follows from a compiler name grammar,
     as `<Prop>k__BackingField` marks an auto-property's storage. The evidence
     is still entirely in metadata, but it records a compiler convention
     rather than an assertion of the format.

   Conventional evidence is admissible only when the published result labels
   it as conventional and carries the matched name as evidence, so a consumer
   can decide whether to trust it. A substrate must never present a
   convention match as a structural fact. Name grammars must come from the
   shared `GeneratedNameGrammar`, never from an ad-hoc string test.
3. **Independent multi-consumer demand.** At least two higher layers need the
   same meaning. The evidence may be:

   - **consumption** — two layers already read the substrate's result; or
   - **duplication** — two or more layers each derive the same meaning
     independently today, or one reads a substrate while another derives it.

   Duplication is the stronger signal, because a second derivation is the
   drift the substrate exists to prevent. No substrate need already exist:
   a candidate is admitted on the derivations that exist without it. Naming a
   layer that neither reads nor derives the meaning today is intent, not
   demand, and does not satisfy this requirement.

   **The unit of admission is a published meaning, not a class.** A component
   may publish several semantic families, and each family carries its own
   demand evidence. Bundling a family with no independent demand alongside one
   that has it does not admit the bundle. The inventory records demand per
   meaning, and a row that covers several families must be able to show
   evidence for each it claims.
4. **Policy neutrality.** It publishes what the metadata asserts, not what a
   consumer should do about it. Rendering choices, fidelity trade-offs,
   severity, and recommendations belong to consumers.
5. **Closed outcomes.** Every reachable disposition, including failure, is
   expressible in its published types without a consumer inspecting strings
   or inferring meaning from absence.

Requirement 3 is deliberately empirical. The inventory below records demand
that exists, not demand a substrate might create.

### Worked admissions

**Property, event, and accessor association — admit, with a split evidence
grade.** The association between an accessor and its declaring property or
event is derived (1) and **structural**: `MethodSemantics` states it outright
(2). The association between a property and its compiler-generated backing
storage is **conventional**: it rests on the `<Prop>k__BackingField` name
grammar (`src/ILInspector.MetadataPrimitives/GeneratedNameGrammar.cs:57`), not
on any table that asserts the relationship. Both are metadata-only, so both
are admissible — but a substrate here must publish the second labelled as
conventional, carrying the matched field name, and must not let a consumer
mistake it for a structural fact. Demand is shown by duplication (3): the
decoders listed in the candidate inventory below each derive some of this
today. It asserts association, not spelling (4), and must distinguish an
ordinary backing association from an absent one and from an ambiguous one (5).

**Lambda and local-function raising — reject.** It fails (2) outright: the
meaning depends on IL patterns, captured-variable flow, and reconstructed
control flow. Metadata can authenticate that a type is compiler-generated; it
cannot establish what source construct produced it. This belongs to the
Decompiler, and the substrate boundary is exactly what keeps it there.

## Outcome vocabulary

Substrates share **required distinctions**, not one shared generic result
type. A single closed generic outcome across unrelated domains would force
every substrate to carry cases it cannot reach, which requirement 5 already
forbids.

A substrate must distinguish, whenever the distinction is reachable in its
domain:

- **Resolved** — the meaning was established, with its supporting evidence.
- **Absent** — the metadata is well formed and the meaning genuinely does not
  apply. Absence is an answer, not a failure.
- **Malformed** — the metadata is present but does not decode.
- **Ambiguous** — more than one candidate satisfies the structural test and
  no rule selects between them.
- **Unsupported** — the shape decodes but names something outside the
  substrate's declared scope, such as an unrecognized version.
- **Budget-limited** — a declared work or resource bound stopped the
  derivation before it could answer.

Two rules make the distinctions usable:

- **Never collapse a failure into an absence.** `Absent` and `Malformed` must
  not share a representation. Reporting unreadable metadata as "not present"
  produces success-shaped output for a broken artifact, which the
  repository-wide constraint on visible failure already forbids.
- **Admitted absence is distinct from unexamined.** When a substrate
  deliberately does not look — because a bound was not requested, or a role is
  optional — that must be distinguishable from having looked and found
  nothing.

A substrate may model additional domain distinctions. It must not add a case
whose only consumer meaning is presentational.

## Identity, evidence, and reader lifetime

**Published identities should be durable, and today mostly are not.** The
target is that a result's identities remain meaningful without the reader that
produced them and can detect misuse against a different reader. Only an
identity that carries its module — `MetadataMethodAddress`, a
`readonly record struct` pairing the module version id with the handle — can
do that, and its `BelongsTo` check is a misuse guard, not a cryptographic
identity. A consumer still validates a row against the target reader's table
before dereferencing.

A bare token cannot do it. `MemorySafetyMemberContractEvidence` publishes
`int MemberToken`
(`src/ILInspector.Metadata/MemorySafetyMetadataIndex.cs:94`) and
`TypeDefinitionToken` stores only `int Value`
(`src/ILInspector.Metadata/TypeDeclaration.cs:27`). Both are validated against
the producing reader at construction, so they are sound *within* that reader,
but neither survives separation from it. This document states the target and
records the gap in [Known deviations](#known-deviations) rather than
pretending the precedents already meet it. A new substrate publishes
module-scoped identities.

**Results outlive the reader; the index need not.** A substrate's published
result values must be safe to retain after the reader closes. The substrate
object itself may retain the reader to serve later handle-keyed queries. These
are different lifetimes and a substrate must not blur them.

**Evidence travels with the outcome.** A result carries the physical evidence
supporting it — the tokens, observations, or chain steps that produced the
answer — so a consumer can explain the fact without re-deriving it. This is
what lets a consumer render an unrecognized marker version, or a forwarding
chain, without decoding the metadata a second time.

**Handle inputs are validated, not trusted, and the guarantee depends on the
key type.** A caller-supplied coordinate is untrusted, but what a substrate
can detect differs by what it accepts:

- A **raw handle** (`MethodDefinitionHandle`, `TypeDefinitionHandle`) carries
  only a table and a row. A substrate must range-check it against the target
  reader's row count and return a typed outcome for an out-of-range row —
  never an out-of-range read and never an exception. It **cannot** detect a
  handle from a different module whose row happens to be in range; such a
  handle yields a well-typed answer about the wrong row. This is a real limit
  of the key type, not an implementation gap.
- A **scoped identity** such as `MetadataMethodAddress`, which carries the
  module version id alongside the handle, can additionally reject a foreign
  coordinate through its `BelongsTo` check.

A substrate whose consumers can plausibly hold coordinates from more than one
module must therefore accept a scoped identity rather than a raw handle. A
substrate that accepts raw handles must document that in-range foreign
handles are undetectable at its boundary. Neither form may promise more than
its key can deliver.

## Construction and bounds

**Construction is total over admissible arguments.** A substrate's entry point
does not throw for hostile, truncated, or malformed metadata; it returns a
substrate whose outcomes report the failure. Argument validation is separate:
a null reader or an invalid caller-supplied bound is a programming error and
may throw.

**Bounds are declared and their exhaustion is an outcome.** Derivations that
scan tables, resolve names, or follow chains take explicit work bounds so a
hostile artifact cannot make inspection unbounded. Exhausting a bound produces
the budget-limited outcome. A budget exception must not escape the entry
point, because an escaping exception is indistinguishable to a consumer from a
tool defect.

**Caching belongs to the consumer.** A substrate is constructed from a reader
and is free of ambient state; whether to build one per operation, or hold one
lazily for the lifetime of a metadata source, is the consumer's decision.
Substrates must not introduce process-wide caches or a shared registry.

## Consumer rules

These are the pattern's own protocol: what a substrate guarantees, and what
follows *for a consumer that chooses to consume one*. They do not oblige any
existing owner to adopt a substrate, do not remove an owner's authority to
keep its current derivation, and do not decide when adoption happens. Each
owner's adoption is its own effort, per
[Stage implementation after locking the design](../design-scope.md#stage-implementation-after-locking-the-design).

Substrate-side guarantees:

- **A substrate never requires a consumer's interpretation as input.** It is
  derivable from a reader alone. This is what lets two consumers use it
  without either depending on the other — the tie the pattern exists to break.
- **A substrate publishes no consumer policy.** Spelling, filtering, severity,
  disclosure, and fidelity decisions are not its to make. A substrate
  publishing a consumer-shaped Boolean has failed requirement 4.
- **A substrate answers only about the module its reader covers.** It does not
  silently reach beyond its declared acquisition scope.

For a consumer that consumes a substrate:

- **It does not also privately re-derive the same fact.** Holding both a
  consumed result and a private derivation reintroduces exactly the divergence
  the substrate removes, and the two will disagree on some artifact. A
  consumer that has not adopted a substrate is not in violation of this — its
  existing derivation is evidence of demand under requirement 3, not a defect.
- **It owns its own policy**, and does not push policy back into the
  substrate to avoid making a decision.
- **It owns cross-assembly composition.** A consumer spanning assemblies
  composes per-module results and decides how to resolve disagreement across
  modules. The substrate does not make that choice for it.

## Discovery

Substrates are discovered through this document and the naming they already
use, not through a registry or a resolver service. A registry would become the
kind of central god service the architecture avoids, and would create the
consumer coupling the pattern removes.

A new substrate registers by adding a row to the inventory below, in the same
change that introduces it.

## Inventory

### Established

| Published meaning (component) | Reading consumer | Second demand, and its evidence |
| --- | --- | --- |
| State-machine relationships (`StateMachineRelationshipIndex`) | Decompiler, reads the result — `src/ILInspector.Decompiler/Pipeline/MetadataSource.cs:47` | **Duplication.** Analysis never calls the index. It derives the same facts itself, twice over: async state-machine type membership at `src/ILInspector.Analysis/LibraryBodyPrimaryMetadataResolver.cs:810`, and `AsyncStateMachineAttribute` decoding at `src/ILInspector.Analysis/LibraryBodyAsyncSourceResolver.cs:211`. |
| Module memory-safety rules markers (`MemorySafetyMetadataIndex`) | CLI Signals, reads a projection — `src/ILInspector.Metadata/AssemblyDetailScanner.cs:197` publishes `MemorySafetyRules`, read at `src/dotnet-inspect/Inspectors/AuditSignalBuilder.cs:372` | **Duplication, already observed diverging.** The decompiler printer decodes the same module marker at `src/ILInspector.Decompiler/Pipeline/Ir/IrImporter.cs:2826`, and the two derivations disagree on a legal ECMA-335 spelling — [#5670](https://github.com/richlander/dotnet-inspect/issues/5670). |
| Member unsafe contracts (`MemorySafetyMetadataIndex`) | CLI Signals, reads a projection — `RequiresUnsafeCount` at `src/dotnet-inspect/Inspectors/AuditSignalBuilder.cs:379` | **Not independently satisfied.** The only other derivation is `AttributeReader.HasRequiresUnsafeAttribute` used by `src/ILInspector.Metadata/ApiSurfaceExtractor.cs:1131` — inside Metadata, not a higher layer. This family is published by the same component as the row above but rides on it rather than showing its own demand. |
| Exact TypeDef declaration (`MetadataTypeDeclarationProbe`) | Queries, reads the result through the Metadata-owned session entry point — `src/DotnetInspector.Queries/InspectionGraphIntegrationsQuery.cs:1226`, `:1310`, `:2001` call `session.ProbeDeclaration(...)`, which returns the substrate's own `TypeDeclarationResult` (`src/ILInspector.Metadata/AssemblyInspectionSession.cs:371`) | **Consumption.** The Decompiler calls the probe class directly — `MetadataTypeDeclarationProbe.ProbeDefinition(reader, typeName)` at `src/ILInspector.Decompiler/Pipeline/Ir/IrImporter.cs:236`. |
| Forwarding chains and module exports (`MetadataTypeDeclarationProbe`) | Queries only, through the same session entry point | **Single reader today.** `ProbeDefinition` explicitly "finds one exact TypeDef in the current image without considering exports or forwarders" (`src/ILInspector.Metadata/MetadataTypeDeclarationProbe.cs:11`), so the Decompiler is not a reader of this family. It is published by the same component as the row above but does not independently satisfy requirement 3. |

"Consumed" is used in two senses above and the column says which applies. A
consumer **reads the substrate's result** when it handles the substrate's own
typed outcome — whether it constructs the substrate itself, as the Decompiler
does, or obtains that same result from a Metadata-owned entry point, as
Queries does through `AssemblyInspectionSession.ProbeDeclaration`. A consumer
**reads a projection** when it reads a value the substrate produced after an
intermediate model has carried it, as CLI Signals does through
`MemorySafetyRules`. Both satisfy requirement 3, because both make the
consumer depend on the substrate's answer instead of its own derivation. The
distinction matters only for locating the call: a reader grepping Queries for
`MetadataTypeDeclarationProbe` will not find it. Analysis appears in no row as
a consumer in either sense.

Only the exact-TypeDef row is satisfied by two readers today. The
state-machine and memory-safety rows are satisfied by duplication: a second
layer needs the same meaning and derives it independently. The forwarding row
is satisfied by neither and is recorded as such. That is the honest state, and
the duplication is also the point — #5670 is a shipped instance of exactly the
drift this pattern exists to prevent, found in the pair that had no shared
substrate. Closing that duplication is the pattern's own backlog. The
memory-safety row's remaining adoption is tracked under
[#5555](https://github.com/richlander/dotnet-inspect/issues/5555); the
state-machine duplication is recorded here and has no tracker yet. Neither is
claimed as completed adoption.

### Known deviations

The established substrates do not all satisfy this document yet. Recording the
gaps is part of locking the pattern; each is a defect in the component, not a
licence to weaken the contract.

This section is bounded, and deliberately cannot be used to admit new
non-conforming work:

- It is **closed to new substrates.** A component admitted after this document
  lands must conform on admission. Only the three precedents that predate the
  pattern may appear here.
- Every row names an **existing** deviation with `path:line` evidence. Rows
  whose correction is a code change name their tracker —
  [#5708](https://github.com/richlander/dotnet-inspect/issues/5708) for the
  budget outcome and
  [#5711](https://github.com/richlander/dotnet-inspect/issues/5711) for the
  identity rows. The raw-handle row is tracked in
  [#5711](https://github.com/richlander/dotnet-inspect/issues/5711) as well,
  since it is the input half of the same identity problem.
- A deviation never becomes the contract. If a deviation looks correct, the
  fix is to change this document through review, not to leave the row standing.

| Substrate | Deviation | Evidence |
| --- | --- | --- |
| Type declaration and forwarding resolution | Budget exhaustion is reported as `Malformed`, collapsing the **Budget-limited** distinction into malformed metadata. A consumer cannot tell a hostile-artifact bound from a broken artifact. | `src/ILInspector.Metadata/MetadataTypeDeclarationProbe.cs:67` returns `TypeDeclarationResult.Rejected(MetadataTypeNameFailure.Malformed(...))` with the message "exceeded its structural-name work budget". |
| Type declaration and forwarding resolution | Publishes bare row coordinates throughout its result graph, so a retained result cannot be rebound to a module safely. The correction boundary is **every** published coordinate, not the examples cited. | `TypeDefinitionToken.Value` — `src/ILInspector.Metadata/TypeDeclaration.cs:27`; `ExportedTypeToken.Value` — `:50`; `MetadataTypeNameFailure.SubjectToken` — `src/ILInspector.MetadataPrimitives/MetadataTypeNameResult.cs:39`. Tracked as [#5711](https://github.com/richlander/dotnet-inspect/issues/5711). |
| `MemorySafetyMetadataIndex` | Same defect, same boundary. | `MemorySafetyMemberContractEvidence.MemberToken` and `.AssociatedMemberToken` — `src/ILInspector.Metadata/MemorySafetyMetadataIndex.cs:94`; `MemorySafetyRulesObservation.AttributeToken` — `:24`. Tracked as [#5711](https://github.com/richlander/dotnet-inspect/issues/5711). |
| `MemorySafetyMetadataIndex`, `StateMachineRelationshipIndex` | Accept raw handles, so an in-range handle from another module is undetectable at the boundary. This is a limit of the key type; the deviation is that neither documents it. | `GetMemberContract(EntityHandle)` validates kind and row only — `src/ILInspector.Metadata/MemorySafetyMetadataIndex.cs:841`; `IsValidMethodHandle` checks row range only — `src/ILInspector.Metadata/StateMachineRelationshipIndex.cs:164`. |

The requirement is not invented for this document: the other two substrates
already model the distinction as a first-class case —
`src/ILInspector.Metadata/StateMachineRelationship.cs:192` and
`src/ILInspector.Metadata/MemorySafetyMetadataIndex.cs:32` both declare
`BudgetExceeded`. Type declaration is the outlier, and correcting it is
tracked as [#5708](https://github.com/richlander/dotnet-inspect/issues/5708).

### Candidates

Recorded as observed duplication, not as approved work. Each needs its own
issue, and must pass the admission test on its own evidence.

| Candidate | Observed demand |
| --- | --- |
| Property, event, accessor, and backing-storage association | Decoded separately in `src/ILInspector.Metadata/MetadataDeclarationQuery.cs`, `src/ILInspector.Metadata/ApiSurfaceExtractor.cs`, `src/ILInspector.Metadata/MemorySafetyMetadataIndex.cs`, and again for spelling in `src/ILInspector.CSharp/CSharpDeclarationWriter.cs`. |
| Compiler-recognized type-use annotations (nullability, tuple names, dynamic, native integers, required members, ref-safety) | Separate decoders in Metadata, interpreted again for spelling in CSharp and in the Decompiler printer. |
| Generic constraint semantics | Decoded in Metadata's declaration query and again in Analysis; the Decompiler reconstructs constraints separately. |
| Interop and entry-point declaration contracts | Concentrated in surface extraction today, with no reusable typed result. |
| Type and field layout relationships | Duplicated inside Metadata between projection and surface extraction; cross-layer demand is weaker. |

The strongest next validation of the pattern is accessor and backing-storage
association: it has four independent existing decoders, needs `Absent` and
`Ambiguous` as genuine outcomes, and is required by
[#5253](https://github.com/richlander/dotnet-inspect/issues/5253), which needs
to know whether a property or event is backed by ordinary instance storage.

### Counterexamples

These remain outside Metadata because their meaning is not metadata-only.
They are listed so the boundary stays legible.

| Concept | Why it is not a substrate |
| --- | --- |
| Async, iterator, lambda, and local-function source reconstruction | Requires IL bodies, captured-variable flow, and reconstructed control flow. |
| Control-flow-derived source forms (switch raising, initializers, `with` expressions) | Requires the IR and CFG; the output is reconstruction policy. |
| Allocation, span, clone, and caller-reachability analysis | Derived from instructions, call sites, and graph traversal. |
| Source and PDB provenance | Requires evidence outside the assembly. |
| Compiler-generated naming grammar as a semantic claim in its own right | A name match alone does not establish what a construct *is*. Lookalike names and compiler differences defeat it. Requirement 2 admits a name grammar only as **conventional evidence for a narrow, labelled association** — that a field is a given property's backing storage — never as the basis for a claim about origin or kind. |
| C# spelling and printer fidelity choices | Consumer policy, even when it consumes authenticated facts. |

The distinction is sharpest for compiler-generated code: Metadata can
authenticate a *relationship* between a kickoff method and its generated type,
because the metadata asserts it. It cannot authenticate that a display class
came from a lambda, because only naming grammar and body evidence say so.

## Gates

This document defines a pattern; it introduces no behavior and therefore
carries no gate of its own. Each substrate names its own gates for the
properties it asserts, as the established substrates already do for exact role
selection, admitted absence, typed rejection, and budget propagation.

An adoption that replaces a consumer's private derivation with a substrate
changes that consumer's observable behavior wherever the two disagreed.
`dotnet-inspect` has already shipped one such divergence
([#5670](https://github.com/richlander/dotnet-inspect/issues/5670)), so an
adoption that assumes equivalence rather than demonstrating it is unsafe. What
that evidence looks like, and which gate carries it, belongs to the adopting
owner's effort — this document does not specify another owner's gates.

## Non-goals

- No registry, resolver service, or shared generic outcome type.
- No sweep converting existing helpers into substrates.
- No movement of body-derived, source-derived, project-derived, or
  presentation semantics into Metadata.
- No new acquisition scope: a substrate answers about metadata a consumer has
  already acquired.
