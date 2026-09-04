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
| `TypeDeclarationResult` and its probe | Type declaration, definition-kind classification, forwarding chains, and module exports. |

They already agree on more than they disagree on: each derives a relationship
rather than decoding a single row, each publishes immutable typed results
carrying the evidence behind them, and each stops a *declared* resource bound
from escaping as an exception. That qualifier is load-bearing: the declaration
probe honours the bounds it declares, but its whole-table construction takes no
bound at all, a gap recorded in **Known deviations** and tracked as
[#5731](https://github.com/richlander/dotnet-inspect/issues/5731).

They disagree on outcome vocabulary. Only type declaration models `Missing`
and `Ambiguous` as first-class declaration outcomes; the state-machine index
distinguishes an admitted optional-role absence from rejection; the
memory-safety index carries a separate evidence-state enum alongside its
result hierarchy. Settling that vocabulary is the main work below.

## Admission test

A meaning is **admissible** as a semantic substrate when all five hold.
Failing any one means it belongs in an ordinary Metadata helper, or in a
composition owned by a consumer.

Admission is necessary, not sufficient. It governs whether a *meaning* belongs
in a substrate; a component publishing that meaning must additionally satisfy
the publication contract below, which is where single-module scope,
construction totality, bounds, and identity are stated. A component can hold an
admissible meaning and still not be a substrate — `TypeResolutionContext` is
the clearest in-tree example, since it composes answers across assemblies,
which [a substrate must not do](#what-a-substrate-guarantees-and-what-it-leaves-alone).

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
3. **Independent multi-consumer demand.** At least two independent derivations
   or reads of the same meaning exist today, **and at least one of them lives
   above `ILInspector.Metadata`.** The evidence may be:

   - **consumption** — a layer already reads the substrate's result; or
   - **duplication** — two or more components each derive the same meaning
     independently today, or one reads a substrate while another derives it.

   Both halves are load-bearing, and they answer different questions. The
   count establishes that drift is *possible*, which is what a substrate
   prevents. The higher-layer requirement establishes that the meaning is not
   simply Metadata's own internal business: a fact that only Metadata ever
   derives can be refactored inside Metadata without a published contract.
   Several derivations inside Metadata plus one genuine higher-layer consumer
   therefore satisfies this requirement; several derivations inside Metadata
   and nothing above it does not.

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
5. **Closed outcomes.** The published outcome type matches the domain in both
   directions. Every reachable disposition, including failure, is expressible
   without a consumer inspecting strings or inferring meaning from absence;
   and every published case is reachable, so a consumer reading the type
   learns exactly what it may receive. A case it can never be handed is a
   false promise, and a shared result algebra spanning unrelated domains
   necessarily makes several.

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
every substrate to carry cases it cannot reach, which requirement 5's
reachability half forbids.

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
- **Unexamined** — the substrate deliberately did not look, because a bound
  was not requested or an optional role was not asked for. This is not an
  answer about the artifact at all.

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

**Caching belongs to the consumer.** A substrate is constructed from a reader,
and whether to build one per operation or hold one lazily for the lifetime of
a metadata source is the consumer's decision. A substrate must not introduce a
shared registry, nor process-wide state that outlives the reader it was
derived from or is shared between readers.

Reader-keyed memoization is not ambient state in that sense and is permitted:
it is observationally transparent, and its lifetime is bounded by the reader
rather than by the process. The precedents already rely on it —
`StateMachineRelationshipIndex` obtains both its assembly-reference projection
and its core-library answer from `static readonly
ConditionalWeakTable<MetadataReader, ...>` memoizers
(`src/ILInspector.Metadata/StateMachineRelationshipIndex.cs:1658`-`1662`,
`src/ILInspector.Metadata/AssemblyReferenceIdentity.cs:22`-`25` and `:133`-`137`,
`src/ILInspector.Metadata/CoreLibraryRootAuthentication.cs:7`-`21`). The
distinction that matters is keying and lifetime, not the `static` keyword.

## What a substrate guarantees, and what it leaves alone

This section states the substrate side of the protocol only. The pattern
guarantees certain properties of what it publishes, and deliberately declines
to decide several things so that each consumer keeps them. It does not oblige
any owner to adopt a substrate, does not decide when adoption happens, and
does not specify how an adopting owner arranges its internals afterwards —
that is the adopting owner's own effort, per
[Stage implementation after locking the design](../design-scope.md#stage-implementation-after-locking-the-design).

What a substrate guarantees:

- **It never requires a consumer's interpretation as input.** It is derivable
  from a reader alone. This is what lets two consumers use it without either
  depending on the other — the tie the pattern exists to break.
- **It publishes no consumer policy.** Spelling, filtering, severity,
  disclosure, and fidelity decisions are not its to make. A substrate
  publishing a consumer-shaped Boolean has failed requirement 4.
- **It answers only about the module its reader covers.** It does not silently
  reach beyond its declared acquisition scope, and it does not compose results
  across assemblies.

What a substrate deliberately leaves to each consumer:

- **Policy.** The substrate will not accept a policy decision pushed back into
  it, so a consumer cannot avoid making one.
- **Cross-assembly composition.** Because a substrate answers per module, a
  consumer spanning assemblies composes those answers and decides how to
  resolve disagreement between them.
- **Whether, when, and how to adopt** — including whether a private derivation
  is retired immediately, kept temporarily to demonstrate equivalence, or
  kept indefinitely.

The last point is a deliberate non-mandate rather than an oversight. Holding
both a consumed result and a private derivation reintroduces the divergence a
substrate exists to remove, so an adoption that leaves both in place
indefinitely gives up most of the benefit. But that consequence is a hazard
for the adopting owner to weigh, not a rule this document can impose: the
Gates section asks an adoption to *demonstrate* equivalence rather than assume
it, and demonstrating it requires running both derivations and comparing them.
A rule forbidding a consumer to hold both would forbid the only technique that
produces the evidence.

## Discovery

Substrates are discovered through this document and the naming they already
use, not through a registry or a resolver service. A registry would become the
kind of central god service the architecture avoids, and would create the
consumer coupling the pattern removes.

A new substrate registers by adding a row to the inventory below, in the same
change that introduces it.

## Inventory

### Established

These published meanings independently satisfy **requirement 3**: each shows
its own demand evidence, and the following subsection lists meanings that do
not. Membership here means exactly that and nothing more.

**Requirement 5 is not met by the tree today.** It is the one requirement the
existing components were written without. Auditing all six rows found four
failing rows, so requirement 5 is stated here as a forward contract, not as
something the precedents demonstrate. The requirement is two-sided, and the two
halves were audited separately: four rows fail the expressibility half, and one
of those four also fails the reachability half.

All four fail the expressibility half the same way: a reachable disposition has
**no case at all**.
In two of them the distinction survives in a `Detail` string, so a consumer
could recover it by parsing prose — which requirement 5 exists to forbid. In
the other two it is not recoverable by any means: member contracts publish the
*identical* literal for a budget stop and for unreadable metadata
(`src/ILInspector.Metadata/MemorySafetyMetadataIndex.cs:460`, `:516`), and the
TypeDef-kind row publishes `Unknown` with no detail at all.

**What gets published instead splits them again, and this is the sharper
axis.** Two of the four publish a *truthful but coarse* case: `Unknown` and
`AttributeUnavailable` say less than the substrate knows, but nothing they say
is false. The other two publish a **wrong** case — both report `Malformed`,
asserting the *artifact* is defective, when the real cause is an invalid caller
handle in one and a bound *we* imposed in the other. A coarse case makes a
substrate less useful; a false case makes it untrustworthy, because a consumer
acting on it reports a defect that does not exist.

That is two separate obligations — the vocabulary must *contain* the case, and
the implementation must *route to* it — and satisfying the first does not
discharge the second. Requirement 5 checks the published type, so it reaches
the first directly. Routing is a behavioural obligation, recorded with its
evidence posture under **Open questions**.

| Established meaning | Requirement 5 today |
| --- | --- |
| State-machine claims and kickoff/type pairing | **Fails — no case at all, and the wrong case published in its place.** "The caller supplied an invalid handle" is reachable, and the vocabulary has `BudgetExceeded` but no `InvalidHandle` (`src/ILInspector.Metadata/StateMachineRelationship.cs:186`-`194`), though the sibling index models exactly that case (`src/ILInspector.Metadata/MemorySafetyMetadataIndex.cs:102`-`104`). A nil or out-of-range handle instead routes through `MalformedHandle` (`src/ILInspector.Metadata/StateMachineRelationshipIndex.cs:174`-`180`) to the same `Rejected(Malformed)` a genuinely unreadable artifact produces (`:124`-`127`) — a claim about the *artifact*, when the artifact is fine. Adding the case would not by itself correct the routing; they are separate corrections. |
| Exact `MoveNext` execution-role selection | Conforming — role selection and rejection are typed. |
| Module memory-safety rules markers | Conforming — version, malformed, conflicting, unsupported, and budget states are each typed. |
| Member unsafe contracts | **Fails.** The attribute-row budget (`MemorySafetyMetadataIndex.cs:748`-`752`), a `MetadataBudgetException` (`:801`-`803`), and malformed metadata (`:805`-`810`) all return `Unavailable`, published as `AttributeUnavailable`; the failure enum has no budget case (`:102`-`110`). |
| Exact TypeDef declaration | **Fails both halves.** *Reachability:* two of the four declared mechanisms cannot be reached through this entry point ([#5750](https://github.com/richlander/dotnet-inspect/issues/5750), detailed below). *Expressibility:* **no case, and the wrong case published in its place.** `MetadataTypeNameFailureMechanism` declares only `Metadata`, `Relationship`, `Signature`, and `TypeSpecification` (`src/ILInspector.MetadataPrimitives/MetadataTypeNameResult.cs:8`-`14`), so budget exhaustion has no case. It is published through `Malformed`, which hard-codes `Mechanism.Metadata` (`:76`-`:82`) and surfaces `Kind` as `"MalformedMetadata"` (`:44`-`:46`) for a bound *we* imposed (`src/ILInspector.Metadata/MetadataTypeDeclarationProbe.cs:67`-`71`). This is the one row where the `Detail` string still carries the distinction — recoverable only by parsing prose. |
| TypeDef kind classification | **Fails.** `MetadataTypeDefinitionKind` offers only `Unknown`, `Class`, `Interface`, `ValueType` (`src/ILInspector.Metadata/TypeDeclaration.cs:14`-`20`), and `Unknown` is returned for bound exhaustion or a cycle (`MetadataTypeDeclarationProbe.cs:785`-`789`), an unsupported TypeSpec shape (`:818`-`857`), a rejected name read (`:864`-`881`), and malformed metadata (`:910`-`915`) — inside a success-shaped `Defined` result (`:640`-`648`). |

**The reachability half was audited separately, and one row fails it.** The
audit has to be run at each *publishing entry point*, not across the tree: the
question is not whether a case is constructed anywhere, but whether a consumer
holding this substrate's result can be handed it. Those are different
questions, and only the second is what requirement 5 promises.

Five rows pass. Every case of `StateMachineRelationshipFailureKind`,
`StateMachineMethodRole`, `MemorySafetyMetadataFailureKind`,
`MemorySafetyMemberContractFailureKind`, `MemorySafetyPointerEvidence`,
`MemorySafetyFixedBufferEvidence`, `RequiresUnsafeAttributeEvidenceState`, and
`MetadataTypeDefinitionKind` is constructed inside the component that publishes
it. The supporting vocabularies those rows carry — `MemorySafetyRulesState`,
`StateMachineClaimKind`, and the result case-records themselves — are total.

**The exact-TypeDef row fails.** `MetadataTypeNameFailure` is shared between
two unrelated domains: structural TypeDef declaration lookup and signature-blob
decoding. `Signature` and `TypeSpecification` are produced by exactly one
factory (`src/ILInspector.MetadataPrimitives/MetadataTypeNameResult.cs:60`-`73`),
which the declaration probe never calls — every rejection it publishes carries
`Metadata` or `Relationship`. A consumer reading
`TypeDeclarationResult.Rejected` (`src/ILInspector.Metadata/TypeDeclaration.cs:222`)
sees four possible mechanisms, two of which cannot occur. Tracked as
[#5750](https://github.com/richlander/dotnet-inspect/issues/5750).

**That is the generic-algebra argument, observed rather than hypothesized.**
One failure vocabulary spanning two domains forces each to declare cases the
other reaches — which is precisely why this document requires shared
*distinctions* instead of one shared result type. The other four failures run
the opposite way: vocabularies that grew from the states their authors actually
met, and so under-declare. Both directions are real, and requirement 5 is
two-sided because of it.

This audit is **`unverified`**. It is point-in-time inspection of routing code
with no enforcing gate, and a construction site can be disconnected without
changing any published type. The first version of this audit asked only whether
each case was constructed *somewhere* and concluded the tree was clean; asking
per entry point found #5750 immediately. A gate here would take the shape
`docs/evidence-and-validation.md` already names for wiring properties — a
non-vacuity test per published case that fails when its producer is removed.

Note the scope of the state-machine row precisely: it covers a **nil or
out-of-range** handle, the case a range check can see. An in-range handle from
a *different* module is not detected at all and yields a well-typed answer
about the wrong row — a separate limit of the raw-handle key type, recorded in
its own deviation and tracked under
[#5711](https://github.com/richlander/dotnet-inspect/issues/5711).

All four are recorded in **Known deviations** and tracked by
[#5730](https://github.com/richlander/dotnet-inspect/issues/5730) and
[#5708](https://github.com/richlander/dotnet-inspect/issues/5708). A row
carrying a recorded deviation is non-conforming and tracked, not silently
admitted, and **may not be cited as precedent for the requirement it fails**.

That four of the six rows mishandle the same distinction — a bound *we* imposed
versus a defect in the *artifact* — is the most useful thing this inventory
found. Each component chose its own failure vocabulary in isolation, and each
independently lost that distinction somewhere: all four omitted the case, two
then published a case asserting the artifact was at fault, and two left the
distinction recoverable only by parsing a `Detail` string. A shared contract is
the only thing that would have caught it, which is the argument for naming the
pattern.

| Published meaning (component) | Reading consumer | Second demand, and its evidence |
| --- | --- | --- |
| State-machine claims and kickoff/type pairing (`StateMachineRelationshipIndex`) | Decompiler, reads the result — constructed at `src/ILInspector.Decompiler/Pipeline/MetadataSource.cs:62`-`63` and consumed at `:109` | **Duplication, twice over.** Analysis never calls the index. It derives state-machine type membership itself at `src/ILInspector.Analysis/LibraryBodyPrimaryMetadataResolver.cs:823`-`854`, and independently pairs kickoff methods to state-machine types — with its own ambiguity handling — at `src/ILInspector.Analysis/LibraryBodyAsyncSourceResolver.cs:1161`-`1271`, decoding `AsyncStateMachineAttribute` at `:211`. |
| Exact `MoveNext` execution-role selection (`StateMachineRelationshipIndex`) | Decompiler, reads the result — `resolvedRelationship.TryGetMethod(StateMachineMethodRole.MoveNext, ...)` at `src/ILInspector.Decompiler/Pipeline/ClassicAsyncRequestAdapter.cs:193`-`194` | **Duplication, by the same mechanism.** Analysis resolves the same role from `MethodImpl` rather than from the index, for both machine kinds: `TryGetAsyncStateMachineMoveNext` at `src/ILInspector.Analysis/LibraryBodyAsyncSourceResolver.cs:1279`, admitting a declaration only through `IsAsyncStateMachineMoveNextDeclaration` at `:1545`-`:1562`, and `TryGetIteratorStateMachineMoveNext` at `:1347` using `IsIteratorMoveNextDeclaration` at `:1527`. Called from four sites — `:600`, `:978`, `:1127`, and `:1226`. The predicates test the name, `HasThis`, arity, signature header, declaring interface, and return type, so the evidence is metadata-only. |
| Module memory-safety rules markers (`MemorySafetyMetadataIndex`) | CLI Signals, reads a projection — `src/ILInspector.Metadata/AssemblyDetailScanner.cs:197` publishes `MemorySafetyRules`, read at `src/dotnet-inspect/Inspectors/AuditSignalBuilder.cs:372` | **Duplication, already observed diverging.** The decompiler printer decodes the same module marker at `src/ILInspector.Decompiler/Pipeline/Ir/IrImporter.cs:2826`, and the two derivations disagree on a legal ECMA-335 spelling — [#5670](https://github.com/richlander/dotnet-inspect/issues/5670). |
| Member unsafe contracts (`MemorySafetyMetadataIndex`) | No reader — `GetMemberContract` (`src/ILInspector.Metadata/MemorySafetyMetadataIndex.cs:363`) has no caller in product code | **Duplication by two higher layers.** Analysis computes a caller-unsafe mode from the same evidence — attribute on member or type, or a pointer in the signature, gated on module opt-in — at `src/ILInspector.Analysis/LibraryBodyPrimaryMetadataResolver.cs:248`-`270`. The Decompiler assembles the same composite from separate parts: member and type attributes at `src/ILInspector.Decompiler/Pipeline/MethodDefinitionFacts.cs:55`-`56` and `:58`-`59` (used at `src/ILInspector.Decompiler/Pipeline/CrossAssemblyTypeResolver.cs:873`), pointer-shape evidence at `src/ILInspector.Decompiler/Pipeline/Ir/CSharpPrinter.cs:3028`-`3030`, and module opt-in at `src/ILInspector.Decompiler/Pipeline/Ir/IrImporter.cs:646`. |
| Exact TypeDef declaration (`MetadataTypeDeclarationProbe`) | Queries, reads the result through the Metadata-owned session entry point — `src/DotnetInspector.Queries/InspectionGraphIntegrationsQuery.cs:1226`, `:1310`, `:2001` call `session.ProbeDeclaration(...)`, which returns the substrate's own `TypeDeclarationResult` (`src/ILInspector.Metadata/AssemblyInspectionSession.cs:371`) | **Consumption.** The Decompiler calls the probe class directly — `MetadataTypeDeclarationProbe.ProbeDefinition(reader, typeName)` at `src/ILInspector.Decompiler/Pipeline/Ir/IrImporter.cs:236`. |
| TypeDef kind classification (`MetadataTypeDeclarationProbe`) | Analysis, reads a projection — `TypeResolutionContext` carries `defined.Kind` into `ResolvedTypeDefinition` (`src/ILInspector.Metadata/TypeResolutionContext.cs:2055`), and Analysis reads `resolved.Definition.Kind` at `src/ILInspector.Analysis/CatalogMemberJoinProjector.cs:185` | **Consumption and duplication both.** Analysis also keeps its own check — `IsValueTypeDefinition`, commented "Authoritative in-assembly check", at `src/ILInspector.Analysis/LibraryBodyPrimaryMetadataResolver.cs:930`-`945` — so it consumes the substrate in one place and re-derives the same fact in another. The Decompiler classifies the family independently in `ClassifyShape` at `src/ILInspector.Decompiler/Pipeline/CrossAssemblyTypeResolver.cs:1333`, over a finer codomain that keeps the enum and delegate distinctions `MetadataTypeDefinitionKind` (`src/ILInspector.Metadata/TypeDeclaration.cs:14`-`20`) folds away. |

The state-machine duplication is not merely redundant, it is *weaker* than the
substrate it duplicates: Analysis identifies state-machine types with an ad-hoc
`">d__"` substring test (`LibraryBodyPrimaryMetadataResolver.cs:835`) rather
than the shared `GeneratedNameGrammar`. That is an observation about what the
duplicate evidence is worth, not a rule imposed on Analysis — Analysis is not
a substrate, the admission test does not govern it, and it is free to keep
that code. Duplication reproduces a derivation, not its quality.

### Published alongside, but not independently admitted

An admitted component may publish further families that would not pass the
admission test on their own evidence. They are bound by the publication
contract above — closed outcomes, immutability, identity, bounds — because
their component is a substrate. They are **not** evidence for the pattern, and
none may be cited as precedent for admitting a new substrate. Recording them
here rather than under **Established** is what keeps requirement 3's
per-meaning rule from being satisfied by a class-level sibling.

| Published meaning (component) | Why it does not independently satisfy requirement 3 |
| --- | --- |
| Remaining interface roles — `SetStateMachine`, `MoveNextAsync`, `Dispose`, `DisposeAsync` (`StateMachineRelationshipIndex`) | **No second demand, and no individual reader.** The Decompiler does not distinguish them: iterating role dispositions at `src/ILInspector.Decompiler/Pipeline/ClassicAsyncRequestAdapter.cs:234`-`245`, it collapses every role other than `MoveNext` to a single `Support` answer. Analysis derives none of them. They are published because `StateMachineRelationship` requires a complete role array — its constructor rejects any relationship that does not "account for every role" (`src/ILInspector.Metadata/StateMachineRelationship.cs:69`-`80`) against the per-kind role sets in `RolesFor` (`:154`-`175`) — not because a second layer needs them. |
| Core-library-root authentication (`MetadataTypeDeclarationProbe`) | **No demand above Metadata.** `DeclaringAssemblyDefinesCoreLibraryRoot` (`src/ILInspector.Metadata/TypeDeclaration.cs:179`) is copied into every successful `Defined` result and re-published on the `ResolvedTypeDefinition` projection (`src/ILInspector.Metadata/TypeResolution.cs:982`), but every reader is inside Metadata — `TypeResolutionContext.cs:2247`, `:2341`, `:2819` and `TypeParameterKindClassifier.cs:330`, `:397`. It fails requirement 3's higher-layer half outright, and is a good illustration of it: a fact only Metadata consumes needs no published contract to stay consistent. |
| Forwarding chains and module exports (`MetadataTypeDeclarationProbe`) | **Single reader.** `ProbeDefinition` explicitly "finds one exact TypeDef in the current image without considering exports or forwarders" (`src/ILInspector.Metadata/MetadataTypeDeclarationProbe.cs:11`), so the Decompiler is not a reader of this family; only Queries is. |

"Consumed" is used in two senses in the Established table and the column says
which applies. A consumer **reads the substrate's result** when it handles the
substrate's own typed outcome — whether it constructs the substrate itself, as
the Decompiler does, or obtains that same result from a Metadata-owned entry
point, as Queries does through `AssemblyInspectionSession.ProbeDeclaration`. A
consumer **reads a projection** when it reads a value the substrate produced
after an intermediate model has carried it, as CLI Signals does through
`MemorySafetyRules`. Both satisfy requirement 3, because both make the
consumer depend on the substrate's answer instead of its own derivation. The
distinction matters only for locating the call: a reader grepping Queries for
`MetadataTypeDeclarationProbe` will not find it. A value that merely travels
alongside a substrate's output is neither — `RequiresUnsafeCount` sits in the
same result object as `MemorySafetyRules` but is counted independently.

Only the exact-TypeDef row is satisfied by two readers. The other five
Established rows are satisfied by duplication: a second layer needs the same
meaning and derives it independently. **A row can be admitted with no reader at
all** — member unsafe contracts is published, has no caller in product code,
and is still admitted, because Analysis and the Decompiler each derive that
meaning privately. Demand is what the codebase needs, not what it currently
calls.

Be careful not to over-count that row. Analysis and the Decompiler derive the
full caller-unsafe contract; the CLI's `RequiresUnsafeCount` does not — it
counts raw attribute name matches and inspects no pointer shape, association,
or module mode. #5721 records a four-way disagreement about a *subfact*, which
attribute namespace counts, not four derivations of the whole meaning.

The TypeDef-kind row is the sharpest evidence the inventory has, because one
component does both things at once: Analysis consumes the substrate's `Kind`
through a projection in `CatalogMemberJoinProjector`, and keeps a private
`IsValueTypeDefinition` — commented "Authoritative in-assembly check" — a few
hundred lines away in `LibraryBodyPrimaryMetadataResolver`. Partial adoption is
the normal state, not a transitional one, which is why the pattern records
demand per meaning rather than per consumer.

A related habit turned up repeatedly while assembling this inventory: a layer
that needs a compiler name grammar writes its own. Analysis matches `">d__"`
by substring (`src/ILInspector.Analysis/LibraryBodyPrimaryMetadataResolver.cs:835`)
and the Decompiler builds `$"<{member.Name}>k__BackingField"` by hand
(`src/ILInspector.Decompiler/MemberBodyProducer.cs:1630`); both bypass the
shared `GeneratedNameGrammar`. Those layers are not substrates and this
document does not bind them. The relevance is narrower and is about this
pattern: a name grammar is exactly the kind of conventional evidence
requirement 2 admits only when it is centrally owned, so the spread of private
copies is the demand signal, not a violation.

Duplication is the point rather than an embarrassment: it has now produced two
shipped divergences, [#5670](https://github.com/richlander/dotnet-inspect/issues/5670)
and [#5721](https://github.com/richlander/dotnet-inspect/issues/5721), each
found in a pair that had no shared substrate. Closing them is the pattern's own
backlog. The memory-safety row's remaining adoption is tracked under
[#5555](https://github.com/richlander/dotnet-inspect/issues/5555); the
state-machine duplication is recorded here and has no tracker yet. None is
claimed as completed adoption.

Three of the nine meanings published by the established components do not
independently qualify. A component earns substrate status for the meaning that
justified it, and tends to accumulate neighbouring families afterwards. The
admission test governs the first; the publication contract governs both.

The role split shows why the unit has to be a published meaning rather than a
class or even an enum. `StateMachineMethodRole` has five members and
`StateMachineRelationship` resolves a complete role array before publishing,
so the roles look like one family in the code. By demand they are two: exact
`MoveNext` selection is derived independently by Analysis — for both async and
iterator machines, through the same `MethodImpl` mechanism the substrate uses —
and read by the Decompiler, while the four support roles have no second
derivation and are not even distinguished by their one reader, which collapses
them to `Support`.
Physical co-publication is not evidence of shared demand.

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
  declaration budget outcome,
  [#5730](https://github.com/richlander/dotnet-inspect/issues/5730) for the
  outcome collapses shared across all three components,
  [#5731](https://github.com/richlander/dotnet-inspect/issues/5731) for
  unbounded declaration-table construction,
  [#5750](https://github.com/richlander/dotnet-inspect/issues/5750) for the
  unreachable failure mechanisms, and
  [#5711](https://github.com/richlander/dotnet-inspect/issues/5711) for the
  identity rows. The raw-handle row is tracked in
  [#5711](https://github.com/richlander/dotnet-inspect/issues/5711) as well,
  since it is the input half of the same identity problem.
- A deviation never becomes the contract. If a deviation looks correct, the
  fix is to change this document through review, not to leave the row standing.

| Substrate | Deviation | Evidence |
| --- | --- | --- |
| Type declaration and forwarding resolution | Budget exhaustion is reported as `Malformed`, collapsing the **Budget-limited** distinction into malformed metadata. A consumer cannot tell a hostile-artifact bound from a broken artifact. | `src/ILInspector.Metadata/MetadataTypeDeclarationProbe.cs:67` returns `TypeDeclarationResult.Rejected(MetadataTypeNameFailure.Malformed(...))` with the message "exceeded its structural-name work budget". |
| `StateMachineRelationshipIndex` | **A missing case, and a wrong one in its place.** The vocabulary has `BudgetExceeded` but no `InvalidHandle`, so the correct disposition has nowhere to go; a nil or out-of-range caller handle is published instead as `Rejected(Malformed)` — a claim that the *artifact* is unreadable, when the artifact is fine. Adding the case and routing to it are separate corrections. An in-range handle from another module is a different limit, tracked under [#5711](https://github.com/richlander/dotnet-inspect/issues/5711). | `MalformedHandle` at `src/ILInspector.Metadata/StateMachineRelationshipIndex.cs:174`-`180`, reached from `:139` and `:156`, versus the recoverable-failure path at `:124`-`127`; vocabulary at `src/ILInspector.Metadata/StateMachineRelationship.cs:186`-`194`. Tracked as [#5730](https://github.com/richlander/dotnet-inspect/issues/5730). |
| `MemorySafetyMetadataIndex` | Attribute-row budget, name-work budget, and malformed metadata all publish as `AttributeUnavailable`; the failure enum has no budget case. | `src/ILInspector.Metadata/MemorySafetyMetadataIndex.cs:748`-`752`, `:801`-`803`, `:805`-`810`; enum at `:102`-`110`. Tracked as [#5730](https://github.com/richlander/dotnet-inspect/issues/5730). |
| Type declaration and forwarding resolution | TypeDef **kind** collapses bound exhaustion, unsupported TypeSpec shapes, rejected name reads, and malformed metadata into `Unknown`, published inside a success-shaped `Defined` result. | `src/ILInspector.Metadata/MetadataTypeDeclarationProbe.cs:785`-`789`, `:818`-`857`, `:864`-`881`, `:910`-`915`, published at `:640`-`648`; enum at `src/ILInspector.Metadata/TypeDeclaration.cs:14`-`20`. Tracked as [#5730](https://github.com/richlander/dotnet-inspect/issues/5730). |
| Type declaration and forwarding resolution | **Two declared failure mechanisms are unreachable through this entry point**, so the published type over-promises its codomain — the reachability half of requirement 5. `MetadataTypeNameFailure` is shared with signature decoding, and `Signature`/`TypeSpecification` come only from the signature factory, which the declaration probe never calls. This is the sole in-tree instance of the defect a shared result algebra would create everywhere. | Vocabulary at `src/ILInspector.MetadataPrimitives/MetadataTypeNameResult.cs:8`-`14`; sole producer of the two unreachable cases at `:60`-`73`; the probe's rejections carry only `Metadata` or `Relationship` — `src/ILInspector.Metadata/MetadataTypeDeclarationProbe.cs:41`, `:67`, `:79`, and the name reader's failures at `src/ILInspector.Metadata/MetadataTypeDefinitionName.cs:592`, `:629`, `:837`, `:846`, which produce only `Relationship` (`src/ILInspector.MetadataPrimitives/MetadataTypeNameResult.cs:48`-`58`) or `Metadata`. Tracked as [#5750](https://github.com/richlander/dotnet-inspect/issues/5750). |
| Type declaration and forwarding resolution | The whole-table paths take **no work bound at all**, so the construction rule above is unmet rather than merely misreported. Array sizes come straight from untrusted table counts. | `Probe` scans every TypeDef and ExportedType — `src/ILInspector.Metadata/MetadataTypeDeclarationProbe.cs:113`-`203`; the `Index` constructor allocates from `reader.TypeDefinitions.Count` and `reader.ExportedTypes.Count` and reads every entry — `:219`-`301`; reached lazily from `src/ILInspector.Metadata/AssemblyInspectionSession.cs:371`-`375`. Only `ProbeDefinition` has a budget. Tracked as [#5731](https://github.com/richlander/dotnet-inspect/issues/5731). |
| Type declaration and forwarding resolution | Publishes bare row coordinates throughout its result graph, so a retained result cannot be rebound to a module safely. The correction boundary is **every** published coordinate, not the examples cited. | `TypeDefinitionToken.Value` — `src/ILInspector.Metadata/TypeDeclaration.cs:27`; `ExportedTypeToken.Value` — `:50`; `MetadataTypeNameFailure.SubjectToken` — `src/ILInspector.MetadataPrimitives/MetadataTypeNameResult.cs:39`. Tracked as [#5711](https://github.com/richlander/dotnet-inspect/issues/5711). |
| `MemorySafetyMetadataIndex` | Same defect, same boundary. | `MemorySafetyMemberContractEvidence.MemberToken` and `.AssociatedMemberToken` — `src/ILInspector.Metadata/MemorySafetyMetadataIndex.cs:94`; `MemorySafetyRulesObservation.AttributeToken` — `:24`. Tracked as [#5711](https://github.com/richlander/dotnet-inspect/issues/5711). |
| `MemorySafetyMetadataIndex`, `StateMachineRelationshipIndex` | Accept raw handles, so an in-range handle from another module is undetectable at the boundary. This is a limit of the key type; the deviation is that neither documents it. | `GetMemberContract(EntityHandle)` (`src/ILInspector.Metadata/MemorySafetyMetadataIndex.cs:363`) admits any handle its `IsValidMemberHandle` accepts, and that check tests kind and row number only — `src/ILInspector.Metadata/MemorySafetyMetadataIndex.cs:841`; `IsValidMethodHandle` checks row range alone — `src/ILInspector.Metadata/StateMachineRelationshipIndex.cs:164`. |

The requirement is not invented for this document — but it is not satisfied by
any of the three, either, and the gap is instructive. Two of them already
*declare* the case: `src/ILInspector.Metadata/StateMachineRelationship.cs:192`
and `src/ILInspector.Metadata/MemorySafetyMetadataIndex.cs:32` both have
`BudgetExceeded`. Declaring the case is not the same as routing every
reachable state to it. The state-machine index has `BudgetExceeded` and still
reports an invalid handle as `Malformed`; the memory-safety index has
`BudgetExceeded` for module rules and still collapses the member-contract
budget into `AttributeUnavailable`.

So the defect is not one outlier component that forgot a vocabulary. It is
that each component extended its vocabulary only as far as the paths it
happened to think about, and no shared contract ever asked whether every
reachable disposition had a home. The exact-TypeDef row shows the same absence
from the other direction: a vocabulary shared with a second domain, never
checked against the domain it is published from, carrying two cases that domain
cannot produce. That is precisely the failure a named
pattern with a fixed admission test prevents, and it is the strongest
practical argument in this document.

### Candidates

Recorded as observed duplication, not as approved work. Each needs its own
issue, and must pass the admission test on its own evidence.

| Candidate | Observed demand |
| --- | --- |
| Property, event, accessor, and backing-storage association | Decoded separately inside Metadata by `src/ILInspector.Metadata/MetadataDeclarationQuery.cs`, `src/ILInspector.Metadata/ApiSurfaceExtractor.cs`, and `src/ILInspector.Metadata/MemorySafetyMetadataIndex.cs`. The higher-layer demand is the Decompiler, which derives accessor association at `src/ILInspector.Decompiler/Pipeline/MethodDefinitionFacts.cs:281`-`294` and backing-storage association at `src/ILInspector.Decompiler/MemberBodyProducer.cs:1630` — where it builds `$"<{member.Name}>k__BackingField"` by hand rather than through `GeneratedNameGrammar`. |
| Compiler-recognized type-use annotations (nullability, tuple names, dynamic, native integers, required members, ref-safety) | Separate decoders in Metadata, interpreted again for spelling in CSharp and in the Decompiler printer. |
| Generic constraint semantics | Decoded in Metadata's declaration query and again in Analysis; the Decompiler reconstructs constraints separately. |
| Interop and entry-point declaration contracts | Concentrated in surface extraction today, with no reusable typed result. |
| Type and field layout relationships | Duplicated inside Metadata between projection and surface extraction; cross-layer demand is weaker. |

The strongest next validation of the pattern is accessor and backing-storage
association: three independent decoders inside Metadata plus a fourth in the
Decompiler, which satisfies requirement 3's need for a higher layer, and which
constructs the backing-field name by hand. It needs `Absent` and
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

**Each adopting substrate must name the gate that carries its routing
obligations, or record them as `unverified`.** Several requirements here
constrain which case a substrate reaches for a given state rather than which
cases it declares. **The list is not exhaustive**, and an adoption must map
every normative routing clause it is subject to, not only these:

- requirement 2's rule that conventional evidence is labelled conventional and
  never presented as structural;
- requirement 5's reachability half;
- both outcome-vocabulary rules;
- correct selection *among* the required distinctions — the two wrong
  `Malformed` routes in the inventory above satisfy every declaration rule and
  still misroute;
- construction totality: malformed input becomes a typed failure rather than an
  escaping exception;
- bound exhaustion becomes **Budget-limited**, not `Malformed`;
- an invalid or out-of-range raw handle becomes a typed outcome.

None of them is checked by reading a published type. Per
[`docs/evidence-and-validation.md`](../evidence-and-validation.md), a soundness
claim must name its enforcing gate or say `unverified`.

That rule binds this document twice over. It introduces no behavior, so it has
no gate of its own to name — but **its factual claims about existing routing
are still claims**, and the Markdown-only exemption covers only documentation
that makes no measured behavior claim. The inventory's reachability audit is
therefore marked `unverified` where it is made, rather than presented as
settled precedent. The repository-wide posture is recorded under
**Open questions**.

## Non-goals

- No registry, resolver service, or shared generic outcome type.
- No sweep converting existing helpers into substrates.
- No movement of body-derived, source-derived, project-derived, or
  presentation semantics into Metadata.
- No new acquisition scope: a substrate answers about metadata a consumer has
  already acquired.

## Open questions

**Routing soundness is `unverified` repository-wide, and this document does
not close it.** Many requirements here constrain the *mapping* from an internal
state to a published case rather than the set of cases a type declares;
**Gates** lists them and states that the list is not exhaustive.

A component can satisfy every one of them by declaration and still route
wrongly, and two Established rows do exactly that: the state-machine index and
the exact-TypeDef probe both publish `Malformed` — a claim about the
*artifact* — for a caller error and for a bound we imposed. Nothing in a
published type distinguishes those from a correct implementation.

**This is not a category difference, and an earlier draft of this section was
wrong to call it one.** Requirement 5 already quantifies over *reachable*
dispositions, which is a behavioural property, and the inventory above
established reachability by reading routing code, not signatures. The real
obstacle is cost: reachability for a bounded set of published cases was
tractable by inspection, whereas proving that every state reaches its *correct*
case is a per-substrate testing obligation, not something a reviewer can
discharge while reading a design document.

Even the tractable half was got wrong once. The first version of that audit
asked whether each case was constructed anywhere in product code and concluded
every row conformed; asking instead whether each case is reachable *through the
entry point that publishes it* found
[#5750](https://github.com/richlander/dotnet-inspect/issues/5750). The cheaper
question is the one a reviewer naturally asks, and it is unsound. That is why
the audit is marked `unverified` rather than treated as settled.

So the repository-wide claim "substrates route correctly" is **`unverified`**,
and this document deliberately does not assert it. **Gates** places the
obligation on each adoption to name the gate that carries its routing
properties or to record them `unverified` in turn. This matters immediately
rather than theoretically: the next planned adoption,
[#5253](https://github.com/richlander/dotnet-inspect/issues/5253), turns on
exactly the structural-versus-conventional label that requirement 2 governs
and no published type can enforce.
