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
construction totality, bounds, identity, and answerable cases are
stated. A component can hold an
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
5. **Closed outcomes.** Every reachable disposition of the meaning, including
   failure, is expressible in the published outcome type without a consumer
   inspecting strings or inferring meaning from absence. *Of the meaning* is
   load-bearing: a bad handle, an unrequested bound, and an exhausted budget
   are facts about the request and about us, not dispositions of the meaning,
   and the second and third outcome-vocabulary rules keep them out of
   requirement 5 — the second the unrequested bound, the third the bad handle
   and the exhausted budget.
   Which of them a component must express depends on the surfaces and bounds
   it eventually chooses, so they belong to the publication contract.

   This is assessable from a proposed result algebra, before any component
   exists, which is what keeps the admission test decidable at admission time.
   Its converse — that every case the algebra *declares* can actually occur —
   is a property of public surfaces rather than of the meaning, so it is not
   an admission condition. It is stated as **answerable cases** in the
   publication contract below and discharged when the component is
   implemented.

Requirement 3 is deliberately empirical. The inventory below records demand
that exists, not demand a substrate might create.

### Worked admissions

**Property and event backing storage — admit; accessor association rides
along.** These are two meanings, and requirement 1 separates them. The
association between an accessor and its declaring property or event is
**structural**: `MethodSemantics` states it outright, so it is a one-row
decode and *not* independently admissible — requirement 1 excludes exactly
this. The association between a property and its compiler-generated backing
storage is **conventional**: it rests on the `<Prop>k__BackingField` name
grammar (`src/ILInspector.MetadataPrimitives/GeneratedNameGrammar.cs:57`), not
on any table that asserts the relationship, so no single row states it and (1)
holds. Both are metadata-only (2).

A substrate is admitted on the conventional meaning and publishes the
structural one alongside it, because the backing relationship cannot be stated
without naming the property the accessors belong to. It must publish the
conventional fact labelled as conventional, carrying the matched field name,
and must not let a consumer mistake it for a structural fact. This is what
requirement 1 doing real work looks like: the meaning that carries the
substrate is the one no row states. Demand is shown by duplication (3): the
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
every substrate to carry cases it cannot reach, which the answerable-cases
rule forbids.

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
- **Invalid coordinate** — the caller supplied a handle or key that does not
  address a row in this reader. This is a statement about the *request*, not
  about the artifact.

Three rules make the distinctions usable:

- **Never collapse a failure into an absence.** `Absent` and `Malformed` must
  not share a representation. Reporting unreadable metadata as "not present"
  produces success-shaped output for a broken artifact, which the
  repository-wide constraint on visible failure already forbids.
- **Admitted absence is distinct from unexamined.** When a substrate
  deliberately does not look — because a bound was not requested, or a role is
  optional — that must be distinguishable from having looked and found
  nothing.
- **Never publish a caller error, or a bound we imposed, as a claim about the
  artifact.** `Malformed` asserts that the artifact is broken. A bad handle
  and an exhausted budget are facts about the request and about us; both have
  their own distinction above.

**Answerable cases.** Requirement 5 makes every reachable disposition
expressible. The converse obligation belongs here: **the outcome type published
by an operation must declare exactly the dispositions that operation's question
can exhibit** — no case the question can never produce, and no case belonging
to a question it does not answer.

The unit is *the question the operation answers*, and that is deliberate. Three
earlier formulations of this rule failed, each because of its unit.
Quantifying over the publishing **component** is too weak: one component can
publish two unrelated families through a single union and satisfy it.
Quantifying over **surfaces** invites a wider type than the surface can
produce, excused by a doc comment or by adding a scope parameter and calling
the narrowing argument-indexed. Quantifying over the **published meaning** —
which this document fixes by *demand* — makes conformance depend on a consumer
census: a surface would become non-conforming when a second consumer appears
elsewhere in the tree, without the surface changing at all. That cannot be an
obligation an implementer discharges.

An operation's question is fixed by the operation, including any parameter or
construction state that changes what is being asked. `ProbeDefinition` asks
whether an exact TypeDef exists, so it may not publish a type declaring
`Forwarded`; a `Probe(name, scope)` overload asks a different question per
scope and owes each a type that fits it. Renaming two families as one meaning
changes nothing, because the question — not the label — decides what the type
may declare.

**Packaging does not merge questions.** If a result is keyed — a collection
indexed by a semantic selector, or an envelope with one position per selector —
each keyed position is judged as its own question, exactly as the equivalent
selector overload would be. Returning `Roles` as one array rather than exposing
`GetRole(role)` changes the API shape, not what is being asked at each key.
Without this, an author could preserve the whole harm by bundling: the
aggregate can exhibit every case *somewhere*, while every individual key still
carries a case it can never produce.

Documenting a narrowing in prose is never sufficient. The consumer still binds
against a type carrying cases it cannot receive, and must write a branch that
can never be taken and can never be tested.

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

Substrates are discovered through the inventory in this document, not through
a registry or a resolver service. The three precedents share no naming
convention, and this document does not invent one; the inventory is the
discovery mechanism. A registry would become the
kind of central god service the architecture avoids, and would create the
consumer coupling the pattern removes.

A new substrate registers by adding a row to the inventory below, in the same
change that introduces it.

## Inventory

### Established

These published meanings are **independently admissible**: each qualifies as a
meaning on its own evidence under requirements 1 through 4, and the following
subsection lists meanings that do not. Requirement 5 is not a membership
criterion here, for the reason immediately below.

**Requirement 5 is a forward contract, not something the precedents
demonstrate.** It is the one requirement the existing components were written
without, and they do not all meet it. Each gap is a defect in the component,
not a licence to weaken the contract; **Known deviations** lists them with
their trackers.

The precedents' reachability is **`unverified`**. Auditing it turned out to be
an unbounded exercise in reading pre-existing code rather than a property this
document can settle, and that audit is recorded in
[#5754](https://github.com/richlander/dotnet-inspect/issues/5754), which owns
it. What this document owns is the contract each new substrate must meet: the
admission test when its meaning is proposed, and the publication contract when
the component is built.

| Published meaning (component) | Reading consumer | Second demand, and its evidence |
| --- | --- | --- |
| State-machine claims and kickoff/type pairing (`StateMachineRelationshipIndex`) | Decompiler, reads the result — constructed at `src/ILInspector.Decompiler/Pipeline/MetadataSource.cs:62`-`63` and consumed at `:109` | **Duplication, twice over.** Analysis never calls the index. It derives state-machine type membership itself at `src/ILInspector.Analysis/LibraryBodyPrimaryMetadataResolver.cs:823`-`854`, and independently pairs kickoff methods to state-machine types — with its own ambiguity handling — at `src/ILInspector.Analysis/LibraryBodyAsyncSourceResolver.cs:1161`-`1271`, decoding `AsyncStateMachineAttribute` at `:211`. |
| Exact `MoveNext` execution-role selection (`StateMachineRelationshipIndex`) | Decompiler, reads the result — `resolvedRelationship.TryGetMethod(StateMachineMethodRole.MoveNext, ...)` at `src/ILInspector.Decompiler/Pipeline/ClassicAsyncRequestAdapter.cs:193`-`194` | **Duplication. Both derivations have two paths, and only the first path matches.** Analysis resolves the same role without the index, for both machine kinds: `TryGetAsyncStateMachineMoveNext` at `src/ILInspector.Analysis/LibraryBodyAsyncSourceResolver.cs:1279` and `TryGetIteratorStateMachineMoveNext` at `:1347`. Each first walks `MethodImpl`, admitting a declaration through `IsAsyncStateMachineMoveNextDeclaration` at `:1545`-`:1562` or `IsIteratorMoveNextDeclaration` at `:1527`; these test the name, `HasThis`, arity, signature header, declaring interface, and return type. Each then falls back, when no explicit implementation was found, to a unique method scan keyed on name and body shape that applies neither predicate (`:1327`-`1344` and `:1392`-`1409`). The substrate is also two-path — explicit `MethodImpl` at `src/ILInspector.Metadata/StateMachineRelationshipIndex.cs:1133`, then implicit candidates at `:1183`-`1210`, gated by `IsImplementationCandidate` with `requireImplicitVisibility` — but its second path is not the same test. The evidence for the same exact meaning is therefore the explicit path on both sides; the fallbacks agree in intent and not in rule. Analysis is called from five sites — the async variant at `:600`, `:978`, `:1127`, and `:1226`, the iterator variant at `:982`. All evidence is metadata-only. |
| Module memory-safety rules markers (`MemorySafetyMetadataIndex`) | CLI Signals, reads a projection — `src/ILInspector.Metadata/AssemblyDetailScanner.cs:197` publishes `MemorySafetyRules`, read at `src/dotnet-inspect/Inspectors/AuditSignalBuilder.cs:372` | **Duplication, already observed diverging.** Analysis decodes it independently at `src/ILInspector.Analysis/LibraryBodyPrimaryMetadataResolver.cs:108`-`118`, checking module and assembly scope itself, and the decompiler printer decodes the same module marker at `src/ILInspector.Decompiler/Pipeline/Ir/IrImporter.cs:2826`, and the two derivations disagree on a legal ECMA-335 spelling — [#5670](https://github.com/richlander/dotnet-inspect/issues/5670). |
| Member unsafe contracts, for methods (`MemorySafetyMetadataIndex`) | No reader — `GetMemberContract` (`src/ILInspector.Metadata/MemorySafetyMetadataIndex.cs:363`) has no caller in product code | **Duplication by two higher layers.** Analysis computes a caller-unsafe mode from the same evidence — attribute on member or type, or a pointer in the signature, gated on module opt-in — at `src/ILInspector.Analysis/LibraryBodyPrimaryMetadataResolver.cs:248`-`270`. The Decompiler assembles the same composite from separate parts: member and type attributes at `src/ILInspector.Decompiler/Pipeline/MethodDefinitionFacts.cs:55`-`56` and `:58`-`59` (used at `src/ILInspector.Decompiler/Pipeline/CrossAssemblyTypeResolver.cs:873`), pointer-shape evidence at `src/ILInspector.Decompiler/Pipeline/Ir/CSharpPrinter.cs:3028`-`3030`, and module opt-in at `src/ILInspector.Decompiler/Pipeline/Ir/IrImporter.cs:646`. |
| Exact TypeDef declaration (`MetadataTypeDeclarationProbe`) | Queries, reads the result through the Metadata-owned session entry point — `src/DotnetInspector.Queries/InspectionGraphIntegrationsQuery.cs:1226`, `:1310`, `:2001` call `session.ProbeDeclaration(...)`, which returns the substrate's own `TypeDeclarationResult` (`src/ILInspector.Metadata/AssemblyInspectionSession.cs:371`) | **Consumption.** The Decompiler calls the probe class directly — `MetadataTypeDeclarationProbe.ProbeDefinition(reader, typeName)` at `src/ILInspector.Decompiler/Pipeline/Ir/IrImporter.cs:236`. |
| Forwarding chains and module exports (`MetadataTypeDeclarationProbe`) | Queries, reads the result through the Metadata-owned session entry point — `src/DotnetInspector.Queries/InspectionGraphIntegrationsQuery.cs:1998`-`2001` calls `session.ProbeDeclaration(...)` and handles `Forwarded` and `ExportedFromModule` at `:2030`-`2033` | **Consumption, by a second independent reader.** Metadata's own cross-assembly composition reads the same typed outcomes from the same surface — `TypeResolutionContext.cs:2043` calls `ready.Session.ProbeDeclaration(...)`, rejects `ExportedFromModule` at `:2276`-`2281`, and follows `Forwarded` while retaining a typed `TypeForwardingHop` at `:2284`-`2290`. The Decompiler is *not* a reader: `ProbeDefinition` "finds one exact TypeDef in the current image without considering exports or forwarders" (`src/ILInspector.Metadata/MetadataTypeDeclarationProbe.cs:11`). |
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
contract above — single-module scope, construction totality, bounds, identity,
and answerable cases — because their component is a substrate, and by
requirement 5, which governs expressibility for every family a substrate
publishes whether or not the family independently qualified. They are **not** evidence for the pattern, and
none may be cited as precedent for admitting a new substrate. Recording them
here rather than under **Established** is what keeps the per-meaning rule from
being satisfied by a class-level sibling. A family lands here when it fails
*any* of requirements 1 through 4 on its own evidence, not requirement 3
alone: a meaning with
ample demand that no substrate could be admitted on — because a single row
states it outright, and requirement 1 excludes it — belongs here too.

| Published meaning (component) | Why it is not independently admissible |
| --- | --- |
| Field, property, and event unsafe contracts, including an accessor's contract resolved through its declaring property or event (`MemorySafetyMetadataIndex`) | **Second derivations exist, but only of a subfact.** Analysis's `ComputeCallerUnsafeMode` (`src/ILInspector.Analysis/LibraryBodyPrimaryMetadataResolver.cs:248`-`270`) and the Decompiler's composite (`src/ILInspector.Decompiler/Pipeline/MethodDefinitionFacts.cs:55`-`59`) classify methods only. Property-level `unsafe` is derived twice more — `src/ILInspector.Metadata/ApiSurfaceExtractor.cs:1345` and `src/ILInspector.Decompiler/MemberBodyProducer.cs:1836` — but both test pointer shape in signature text, not the attribute-and-module-mode contract, so they are the same kind of subfact demand as `RequiresUnsafeCount` below. These families are published because an accessor's contract cannot be answered without them. |
| Remaining interface roles — `SetStateMachine`, `MoveNextAsync`, `Dispose`, `DisposeAsync` (`StateMachineRelationshipIndex`) | **No second demand, and no individual reader.** The Decompiler does not distinguish them: iterating role dispositions at `src/ILInspector.Decompiler/Pipeline/ClassicAsyncRequestAdapter.cs:234`-`245`, it collapses every role other than `MoveNext` to a single `Support` answer. Analysis derives none of them. They are published because `StateMachineRelationship` requires a complete role array — its constructor rejects any relationship that does not "account for every role" (`src/ILInspector.Metadata/StateMachineRelationship.cs:69`-`80`) against the per-kind role sets in `RolesFor` (`:154`-`175`) — not because a second layer needs them. |
| Core-library-root authentication (`MetadataTypeDeclarationProbe`) | **No demand above Metadata.** `DeclaringAssemblyDefinesCoreLibraryRoot` (`src/ILInspector.Metadata/TypeDeclaration.cs:179`) is copied into every successful `Defined` result and re-published on the `ResolvedTypeDefinition` projection (`src/ILInspector.Metadata/TypeResolution.cs:982`), but every reader is inside Metadata — `TypeResolutionContext.cs:2247`, `:2341`, `:2819` and `TypeParameterKindClassifier.cs:330`, `:397`. It fails requirement 3's higher-layer half outright, and is a good illustration of it: a fact only Metadata consumes needs no published contract to stay consistent. |

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

Two rows are satisfied by two readers: exact TypeDef, and forwarding chains
and module exports — the latter by one reader inside Metadata and one above
it, which requirement 3's second half permits because the higher-layer reader
is what makes the meaning a published contract rather than Metadata's internal
business. The other five Established rows are satisfied by duplication: a
second layer needs the same meaning and derives it independently. **A row can be admitted with no reader at
all** — member unsafe contracts is published, has no caller in product code,
and is still admitted, because Analysis and the Decompiler each derive that
meaning privately. Demand is what the codebase needs, not what it currently
calls.

Be careful not to over-count that row, in two directions. Analysis and the
Decompiler derive the caller-unsafe contract **for methods**, which is the
meaning the row admits. `GetMemberContract` accepts field, property, and event
handles too, and resolves an accessor's contract through its declaring property
or event; no higher layer derives those, so they are published alongside rather
than established. The CLI's `RequiresUnsafeCount` does not derive the meaning
at all — it
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

Three of the ten meanings published by the established components do not
independently qualify. A component earns substrate status for the meaning that
justified it, and tends to accumulate neighbouring families afterwards. The
admission test governs the first; the publication contract governs both.

The role split shows why the unit *of admission* has to be a published meaning
rather than a class or even an enum. (**Answerable cases** uses a different
unit, for reasons stated there; demand decides what is admitted, not what a
type may declare.) `StateMachineMethodRole` has five members and
`StateMachineRelationship` resolves a complete role array before publishing,
so the roles look like one family in the code. By demand they are two: exact
`MoveNext` selection is derived independently by Analysis — for both async and
iterator machines, through the same explicit `MethodImpl` mechanism the
substrate uses, with a differing fallback recorded in the row — and read by the
Decompiler, while the four support roles have no second
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
  lands must pass the admission test when its meaning is proposed and satisfy
  the publication contract when it is built. Only the three precedents that
  predate the pattern may appear here.
- Every row names an **existing** deviation and the tracker that carries its
  `path:line` evidence —
  [#5708](https://github.com/richlander/dotnet-inspect/issues/5708) for the
  declaration budget outcome,
  [#5730](https://github.com/richlander/dotnet-inspect/issues/5730) for the
  outcome collapses shared across all three components,
  [#5731](https://github.com/richlander/dotnet-inspect/issues/5731) for
  unbounded declaration-table construction,
  [#5750](https://github.com/richlander/dotnet-inspect/issues/5750) for the
  unreachable failure mechanisms,
  [#5754](https://github.com/richlander/dotnet-inspect/issues/5754) for the
  entry points whose codomains are narrower than their published types, and
  [#5711](https://github.com/richlander/dotnet-inspect/issues/5711) for the
  identity rows, whose row also covers the raw-handle inputs that are the
  input half of the same identity problem.
- A deviation never becomes the contract. If a deviation looks correct, the
  fix is to change this document through review, not to leave the row standing.

| Gap | Tracker |
| --- | --- |
| Budget exhaustion published as `Malformed` by the declaration probe, collapsing a bound *we* imposed into a claim about the artifact | [#5708](https://github.com/richlander/dotnet-inspect/issues/5708) |
| Outcome collapses shared across all three components — a reachable disposition with no case, in the state-machine index, the memory-safety index, and TypeDef kind classification | [#5730](https://github.com/richlander/dotnet-inspect/issues/5730) |
| Unbounded declaration-table construction: the whole-table paths take no work bound, so the construction rule is unmet rather than deviated from | [#5731](https://github.com/richlander/dotnet-inspect/issues/5731) |
| Bare row coordinates published throughout the result graphs, so a retained result cannot be rebound to a reader safely — the identity rows, and the raw-handle inputs that are the same problem from the other side | [#5711](https://github.com/richlander/dotnet-inspect/issues/5711) |
| `MetadataTypeNameFailure` shared across two unrelated domains, so two of its four mechanisms belong to a question the declaration probe does not answer — answerable cases | [#5750](https://github.com/richlander/dotnet-inspect/issues/5750) |
| Operations publishing a type that declares cases their question can never produce, including where the narrowing is recorded only in prose — answerable cases | [#5754](https://github.com/richlander/dotnet-inspect/issues/5754) |

The gap is instructive in one respect worth keeping. Two of the three
components already *declare* the case they fail to route to:
`src/ILInspector.Metadata/StateMachineRelationship.cs:192` and
`src/ILInspector.Metadata/MemorySafetyMetadataIndex.cs:32` both have
`BudgetExceeded`, and both still publish something else for a reachable budget
stop. Each component extended its vocabulary only as far as the paths it
happened to think about, and no shared contract ever asked whether every
reachable disposition had a home. That is the failure a named pattern with a
fixed admission test prevents, and it is the strongest practical argument in
this document.

### Candidates

Recorded as observed duplication, not as approved work. Each needs its own
issue, and must pass the admission test on its own evidence.

| Candidate | Observed demand |
| --- | --- |
| Property, event, accessor, and backing-storage association | Decoded separately inside Metadata by `src/ILInspector.Metadata/MetadataDeclarationQuery.cs`, `src/ILInspector.Metadata/ApiSurfaceExtractor.cs`, and `src/ILInspector.Metadata/MemorySafetyMetadataIndex.cs`. The higher-layer demand is the Decompiler, which derives accessor association at `src/ILInspector.Decompiler/Pipeline/MethodDefinitionFacts.cs:281`-`294` and backing-storage association at `src/ILInspector.Decompiler/MemberBodyProducer.cs:1630` — where it builds `$"<{member.Name}>k__BackingField"` by hand rather than through `GeneratedNameGrammar`. |
| `dynamic` type-use annotation | The only annotation family with a derivation outside Metadata. `DynamicReader` is read 17 times in `src/ILInspector.Metadata/ApiSurfaceExtractor.cs`, and the Decompiler decodes `DynamicAttribute` itself, including the top-level/`Unknown` distinction, at `src/ILInspector.Decompiler/Pipeline/MethodDefinitionFacts.cs:134`-`146`. |
| Remaining type-use annotations (nullability, tuple names, required members, ref-safety) | **Weak candidate — no second derivation observed.** Each attribute is named only in `src/ILInspector.Metadata/KnownAttributeNames.cs` and decoded only inside Metadata; higher layers *spell* the decoded model rather than re-derive it, which requirement 3 does not count. Listed to record that they were checked separately, not as one bundled family. |
| Complete generic constraint semantics | Decoded twice inside Metadata — `src/ILInspector.Metadata/MetadataDeclarationQuery.cs:878`-`923` and `src/ILInspector.Metadata/ApiSurfaceExtractor.cs:2168`-`2252`. No higher layer derives it: the Decompiler *spells* the already-decoded model (`src/ILInspector.Decompiler/MemberBodyProducer.cs:951`-`957`, and see its stated ownership at `:48`-`56`) and reads no constraint row itself. |
| Analysis's generic value-type admissibility predicate | A narrower question over the same rows — `HasGenericConstraints` and `GenericParameterCanBeValueType` in `src/ILInspector.Analysis/LibraryBodyGenericConstraintClassifier.cs:18`-`73`. Recorded separately because it is subfact demand, not a second derivation of the row above. |
| Interop declaration contracts (P/Invoke) | **Already typed and consumed — the open question is whether more is wanted.** The classification and import module name are published as `ClassifiedMethodInfo` (`src/ILInspector.Metadata/MethodClassificationScanner.cs:11`-`20`) through `AssemblyInspectionSession.ClassifiedMethods()` (`src/ILInspector.Metadata/AssemblyInspectionSession.cs:202`), projected by `src/DotnetInspector.Queries/ClassifiedMethodsQuery.cs:21`-`35`, and rendered by the CLI (`src/dotnet-inspect/Views/LibraryInspectionView.cs:245`-`261`). What no one derives is the fuller `MethodImport` contract — entry-point name, charset, calling convention, error handling — so any candidate here must name those fields and show demand for them, not reuse the demand for classification. |
| Authenticated managed entry point | **Two different questions, not one duplicated fact.** `MetadataCorHeaderSummary` (`src/ILInspector.Metadata/MetadataImageOverview.cs:231`-`270`) copies the COR-header flags and raw token and is rendered at `src/DotnetInspector.MetadataRendering/MetadataProjectionRenderer.cs:446`-`454`; that is a flag-plus-field read, which requirement 1 classifies as an ordinary helper. Analysis asks something else at `src/ILInspector.Analysis/LibraryBodyLiftedSourceOwnerResolver.cs:794`-`900`: it authenticates the token as a MethodDef, validates an analyzable static signature and body, and correlates a top-level owner. Only the second is a candidate meaning, and it has one derivation, so requirement 3 is not met today. |
| Type and field layout relationships | **Weakest candidate — no observed duplication.** Metadata derives no layout relationship today. Both derivations sit in the Decompiler, and they are not the same sub-fact: `src/ILInspector.Decompiler/MemberBodyProducer.cs:974`-`1003` spells `StructLayout`/`FieldOffset`, while `src/ILInspector.Decompiler/Pipeline/Ir/IrImporter.cs:3189` reads only `GetLayout().Size`. Listed for discovery; it does not satisfy requirement 3 today. |

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
- all three outcome-vocabulary rules;
- correct selection *among* the required distinctions — the state-machine
  index declares `BudgetExceeded` and still publishes `Malformed` when a name
  exceeds its byte budget, so it satisfies every declaration rule and
  misroutes anyway;
- construction totality: malformed input becomes a typed failure rather than an
  escaping exception;
- bound exhaustion becomes **Budget-limited**, not `Malformed`;
- an invalid or out-of-range raw handle becomes a typed outcome.

None of them is checked by reading a published type. Per
[`docs/evidence-and-validation.md`](../evidence-and-validation.md), a soundness
claim must name its enforcing gate or say `unverified`.

That rule binds this document twice over. It introduces no behavior, so it has
no gate of its own to name — but **its factual claims about existing code are
still claims**, and the Markdown-only exemption covers only documentation that
makes no measured behavior claim. This document therefore keeps its factual
surface small, concentrated in the inventory's demand evidence, which is
load-bearing for requirement 3. The precedents' conformance to requirement 5
and to answerable cases is `unverified` and is owned by the linked
trackers, not asserted here. The
repository-wide routing posture is recorded under **Open questions**.

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

Requirement 5 constrains the *set of cases a type declares*, and per-meaning
reachability the surfaces that publish them. Proving that every internal state reaches its *correct*
case is a different and larger obligation: a per-substrate testing burden, not
something a reviewer can discharge while reading a design document.

So the repository-wide claim "substrates route correctly" is **`unverified`**,
and this document deliberately does not assert it. **Gates** places the
obligation on each adoption to name the gate that carries its routing
properties or to record them `unverified` in turn. This matters immediately
rather than theoretically: the next planned adoption,
[#5253](https://github.com/richlander/dotnet-inspect/issues/5253), turns on
exactly the structural-versus-conventional label that requirement 2 governs
and no published type can enforce.
