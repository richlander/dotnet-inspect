# Resolved Resource Effects

## Status and approved scope

This document is the normative owner for Analysis resolution of admitted
resource-effect declarations against concrete .NET metadata. It is tracked by
[#6728](https://github.com/richlander/dotnet-inspect/issues/6728) as step 5 of
the 23-step production-adoption plan in
[#6544](https://github.com/richlander/dotnet-inspect/issues/6544).

The first production consumer is the existing
`LibraryMethodAnalysisRunner` path. Implementation is assigned to
[#6729](https://github.com/richlander/dotnet-inspect/issues/6729).

[Resource Effect Language](resource-effect-language.md) owns admitted
selectors, effects, provenance, and the admission receipt. Metadata owns
assembly binding, type forwarding, definition correspondence, and catalog
generation. This owner consumes those contracts and publishes
occurrence-bound resource effects. It does not redefine either owner.

## Authority and exact claim

**Resolved Resource Effects** owns:

> Given one immutable resource-effect admission, one exact Metadata catalog
> generation, and one finite Analysis metadata-occurrence population, bind
> admitted structural selectors to exact type, member, field, and direct-call
> occurrences; substitute occurrence-local generic bindings; coalesce equal
> applicable effects; reject the closed set of contradictory effects that meet
> at one occurrence; and publish deterministic positive, unmatched,
> ambiguous, unsupported, incomplete, or conflicting evidence without
> inferring identity from display text.

The owner defines:

- the resolution request and its required owner-issued receipts;
- exact definition, physical invocation-site, and resolved-invocation
  currencies;
- one-sided structural selector matching against actual SRM facts;
- occurrence-local generic binding and effect substitution;
- resolution of structural field, callback, outcome, and operation-slot
  references into occurrence-local forms;
- finite applicability overlap and version-1 compatibility;
- atomic conflict behavior;
- positive and incomplete result shapes;
- deterministic ordering and resolved-content identity; and
- the handoff from admitted declarations to later resource-flow Analysis.

It does not define:

- declaration syntax, admission, source authority, or admission policy;
- Metadata assembly binding, type forwarding, catalog construction, or
  definition correspondence;
- IL decoding, stack or local value flow, alias analysis, CFGs, reaching
  definitions, exception paths, or interprocedural lifecycle composition;
- Finding severity, confidence, remediation, Resource Triage policy, or
  presentation;
- attribute extraction, product JSON intake, or external model discovery; or
- a context-free selector-overlap or declaration-satisfiability solver.

## Purpose

Admission proves that each model is locally well formed. It intentionally does
not prove that a selector applies to an inspected program or that independent
models are mutually compatible.

The previous unresolved-catalog approach attempted those questions before
opening metadata. Seven reviewed candidates produced twenty accepted defects
around selector unification, resource-kind binding, operation lineage,
predicate overlap, and assembly policy. The replacement architecture follows
the existing ArrayPool path:

```text
admitted declarations
        +
exact metadata generation
        +
exact Analysis occurrence population
        |
        v
one-sided selector resolution
        |
        v
occurrence-local generic and reference binding
        |
        v
finite compatibility at each exact occurrence
        |
        v
resolved effects, visible incompleteness, or conflict
```

The important change is the direction of proof. The resolver does not ask
whether two unresolved selector patterns might intersect in some possible
program. It asks whether each admitted selector matches an actual metadata
occurrence in this generation. Only effects that reach the same exact
occurrence are compared.

## Design basis

### Existing ArrayPool analysis

The current ArrayPool analyzer first resolves concrete call operands and then
classifies `Shared`, `Rent`, and `Return`. `ArrayPoolOwnershipFlow` and
`LeakTriageAnalyzer` consume those resolved calls together with existing
instruction, CFG, reaching-definition, and exception-region evidence.

That ordering is the implementation oracle:

- exact call and definition identity precede lifecycle interpretation;
- unresolved calls remain incomplete;
- physical call-site evidence remains distinct from attributed source methods;
- local and interprocedural flow use compact resolved evidence rather than
  reparsing metadata names; and
- Findings remain downstream of resolution and flow.

The API-specific predicates are the part to replace. The metadata-first
architecture is retained.

### Existing metadata and occurrence infrastructure

The design composes existing owner-issued contracts:

- `TypeResolutionCatalog` and `TypeResolutionContext` provide exact catalog
  generation, assembly binding, forwarding, ambiguity, unavailability, and
  rejection outcomes.
- `ResolvedTypeDefinitionKey` and `DefinitionJoinToken` provide
  generation-scoped type-definition correspondence. They are not reminted as
  operation or call-site identity.
- `TypeRefDecoder`, `MemberResolver`, and
  `LibraryBodyMethodReferenceResolver` decode structural type and member
  signatures, including `MemberRef` and `MethodSpec` generic instantiation.
- `MethodCallAnalysis` and `DirectCall` provide physical `call`, `callvirt`,
  and `newobj` occurrences with attributed caller, evidence method, IL offset,
  operand token, selected member shape, call kind, and exact-target state.
- `CallGraphMemberResolver` demonstrates structural member correspondence
  without parsing display signatures.

Resolved Resource Effects uses these facts. It does not create another
metadata graph, binding policy, call census, or display-name resolver.

### Simplicity and modeling

The operation is a deterministic function over immutable, generation-bound
inputs. It has no concurrent mutation, scheduling, retry, or distributed
state. A TLA+ model would restate ordinary functional invariants without
adding useful evidence, so this design uses typed receipts, deterministic
fixtures, and Release gates instead.

The only compatibility work is finite comparison of already-bound effects at
one exact occurrence. A broader symbolic mechanism remains unjustified unless
a minimal fixture or pinned corpus case demonstrates that this contract cannot
represent required behavior soundly.

## Real assets and oracle

The motivating real assets and current product oracle are inherited from
[Resource Effect Language](resource-effect-language.md#real-assets-and-existing-oracle):

- MessagePack 2.5.192;
- Npgsql 8.0.4; and
- Pipelines.Sockets.Unofficial 2.2.8.

Their pinned Resource Triage observations exercise the current ArrayPool path.
This design does not claim lifecycle equivalence by itself. Issue #6729 must
resolve the shipped typed ArrayPool model to the same exact framework
operations, and issues #6730-#6731 must preserve the existing fixture and
corpus outcomes when the generic engine adopts those resolved effects.

## Consumer and production adoption

`LibraryMethodAnalysisRunner` is the first consumer. It already receives the
method identity, resolved direct-call evidence, and optional external Metadata
resolution context needed to request occurrence-bound effects without a second
body or metadata traversal.

The production path remains the 23-step #6544 plan:

1. #6728 locks this resolution contract.
2. #6729 implements it and the shipped typed ArrayPool mapping.
3. #6730 replaces hard-coded ArrayPool operation recognition and generalizes
   method-local ownership evidence.
4. #6731 migrates lifecycle and leak analysis while preserving the pinned
   oracle.
5. #6732 migrates Research ownership-flow consumers.
6. Later focused slices adopt repository ownership declarations, the CLI, and
   Inspect Web Browser/Wasm.

This host-neutral Analysis boundary therefore reaches both production hosts
through the already enumerated adoption plan. It adds no rendering surface;
Markout and host-specific lowering are not applicable to this slice.

## Planning and generation formation

Resolution has a planning phase before its immutable execution request exists.
The Analysis coordinator:

1. derives the required Metadata type-resolution requests from admitted target
   selectors, structural field selectors, signature locations, and the
   occurrence population's decoded member shapes;
2. reuses `CatalogMemberCorrespondencePlan` where the existing member
   correspondence boundary already describes the required open signature, and
   requests exact interface-member correspondence facts for interface targets
   that may apply to concrete member occurrences;
3. unions those requests with the inspection operation's existing Metadata
   discovery manifest;
4. asks the Metadata-owned builder to reach its bounded fixed point and freeze
   one generation; and
5. forms or reissues the Analysis occurrence-population receipt against that
   exact generation.

Metadata retains ownership of request manifests, fixed-point discovery,
generation replacement, and `PlanExpansionRequired`. Analysis owns
contributing the resource-effect requests it needs and associating the
resulting generation with its occurrence population.

If planning discovers another required request, the coordinator may perform a
bounded replacement cycle before publishing the resolution request. It
discards the prior population receipt and rebuilds against the replacement
generation. Once the immutable request reaches the resolver, the resolver
neither expands nor mutates the generation. An unexpected
`PlanExpansionRequired` at that point is typed incomplete evidence, never
`Unmatched`.

## Resolution request

One resolution request carries:

- one initialized `ResourceEffectAdmission` and its exact
  `ResourceEffectAdmissionReceipt`;
- one Metadata catalog identity and exact generation;
- the owner-issued binding policy and type-resolution context for that
  generation;
- one finite Analysis occurrence-population receipt;
- the exact root participants and retained metadata images represented by that
  population;
- one finite resolution work ledger; and
- caller cancellation.

The occurrence-population receipt states which definitions and physical
direct-call operands are in scope and whether their metadata and method-body
enumeration completed. It is not reconstructed from assembly names, MVIDs,
paths, or the current contents of a mutable collection.

The request is invalid when:

- the admission or either receipt is default or uninitialized;
- an occurrence belongs to another catalog or generation;
- a retained image no longer corresponds to its owner-issued participant;
- the population and Metadata generation were not formed from the same
  immutable inspection snapshot; or
- a required owner-issued binding policy is absent.

Invalid or stale association is rejected before selector matching. The
resolver never retries against a newer generation and never transfers results
across generations.

## Exact currencies

The resolver preserves five independent currencies:

| Currency | Meaning |
| --- | --- |
| Admission receipt | Which exact admitted models, semantic declarations, and provenance associations are available. |
| Metadata generation | Which immutable binding, forwarding, and definition universe is authoritative. |
| Definition occurrence | Which exact TypeDef, MethodDef, accessor MethodDef, or FieldDef in that generation matched a selector. |
| Physical invocation site | Which exact direct-call instruction exists in one participant, whether or not its target resolves. |
| Resolved invocation | Which selected member definition and occurrence-local generic environment are associated with that physical site. |

No currency substitutes for another.

### Definition occurrence

A definition occurrence identifies:

- catalog identity and generation;
- owner-issued participant or assembly-candidate identity;
- exact assembly identity and module version ID;
- metadata table kind and row handle;
- the selected type or member definition; and
- the definition's structural signature facts.

Type definitions may retain Metadata's generation-scoped correspondence token.
Member definitions retain their exact participant, MVID, and metadata handle.
A display name, simple assembly name, source path, or independently rebuilt
signature is not a definition occurrence.

An interface application association retains the exact interface declaration
definition, exact concrete implementation definition, and Metadata-owned
correspondence evidence. It does not collapse those definitions into one
identity or infer a relationship from equal signatures.

Property getters and setters are selected through Metadata method semantics,
not a `get_` or `set_` spelling convention. Constructors are selected through
constructor metadata semantics, not name text alone. Fields remain FieldDef
occurrences and are not represented as methods.

### Invocation occurrence

A physical invocation site identifies one `call`, `callvirt`, or `newobj`
instruction independently of target resolution. Its total key contains:

- the containing participant's acquisition registration;
- containing assembly identity and module version ID;
- physical evidence method token;
- IL offset;
- original operand token; and
- call kind.

This follows the existing `GraphNodeStorageKey` distinction between physical
storage and logical correspondence. It keeps an unresolved operand locatable
and prevents two retained copies with equal assembly/MVID/token values from
collapsing across participant registrations.

A resolved invocation associates that physical site with:

- its selected definition occurrence;
- attributed caller identity;
- exact-target state; and
- the occurrence-local generic binding environment.

The evidence method and attributed caller may differ for compiler-generated
bodies. Neither coordinate is rewritten to look like the other.

`CalleeDefinitionToken` alone is neither physical-site nor resolved-invocation
identity. It is reader-local, may be absent for an external target, and does
not identify the containing participant or physical use site.

`ldftn`, `ldvirtftn`, `calli`, reflection, dynamic invocation, and inferred
runtime dispatch targets are outside the version-1 invocation population. An
encountered unsupported call form that could carry a modeled effect is visible
as unsupported or incomplete; it is not relabeled as a direct call.

## Selector resolution

Resolution is one-sided pattern matching:

```text
admitted structural selector
            against
actual decoded occurrence facts
```

It is not selector-to-selector unification.

### Assembly and type selection

The assembly selector applies its language-owned simple-name, public-key-token,
version-policy, and core-library-facade rules to the exact binding origin and
selected Metadata candidate.

Type selection compares:

- exact namespace;
- every nested metadata-name segment;
- generic arity per segment;
- structural generic arguments;
- arrays and ranks;
- by-reference and pointer shape; and
- selector-local generic variables.

Type forwarding follows Metadata's result and retains its hop evidence. The
resolver does not search another assembly after a terminal owned miss,
ambiguity, unavailability, or rejection.

A same-named type under another defining assembly is unmatched. Display
spelling never repairs missing identity.

### Member selection

Member selection compares the actual selected definition's:

- declaring type;
- member kind;
- metadata name where name is part of metadata identity;
- staticness;
- generic arity;
- calling convention, `hasthis`, and `explicitthis`;
- ordered parameter types and ref kinds; and
- return type.

Custom modifiers and function-pointer structure remain exact where the
underlying decoder supplies them. A signature shape that the selector
vocabulary or existing decoder cannot faithfully represent is unsupported,
not approximately matched.

An overload set is not ambiguity after one invocation operand has selected one
definition. Ambiguity means Metadata could not select one authoritative
definition for the actual occurrence.

### Generic binding

Selector-local `type[N]` and `method[N]` variables bind from one actual
operation occurrence:

- declaring-type instantiation binds type variables;
- `MethodSpec` instantiation binds method variables;
- an open definition may bind to its exact scope-owned generic parameter; and
- repeated uses of one variable must bind to equal exact structural types.

Bindings retain type origin and generic scope. Equal display text from
different assemblies or generic scopes is not equal binding.

The binding substitutes every occurrence of the variable in:

- resource-kind arguments;
- structural field selectors;
- effect locations;
- correspondence and lender locations;
- completion predicates; and
- exact-runtime-type guards.

Failure to decode, bind, or consistently substitute a potentially applicable
occurrence is incomplete. It is not an unmatched selector.

## Occurrence-local reference resolution

Admission resolves model-local aliases enough to prove one model is
self-consistent. Metadata resolution finishes the concrete association:

- each `StructuralField` resolves to one exact FieldDef reachable from the
  bound root type;
- each callback scope resolves to its exact delegate parameter and callback
  contract at this occurrence;
- each outcome case retains its resolved source location and finite test;
- each operation slot retains its bound source and optional concrete
  resource-kind arguments; and
- each signature location in a guard resolves to the occurrence's exact
  receiver, parameter, or return type.

Model-local callback and outcome labels do not become cross-model identity.
Occurrence-bound equality uses their resolved delegate, source, test, and
location facts.

An unresolved field, delegate signature, outcome source, or operation binding
that could affect an otherwise matching occurrence is incomplete. A consumer
must not reinterpret the original local label.

## Occurrence-bound effects

A resolved effect retains:

- the exact declaration definition, applied definition, or invocation
  occurrence;
- any Metadata-owned correspondence evidence that carries an interface
  declaration to an exact concrete implementation;
- the fully occurrence-substituted effect;
- every exact declaration provenance that produced it;
- the source model identity and semantic model receipt;
- the admission receipt;
- the Metadata generation; and
- its applicability domain.

Equal occurrence-bound effects coalesce. Coalescence unions and
deterministically orders provenance; it grants no authority precedence and
does not erase the model-to-declaration association.

Different resource kinds and orthogonal effects may coexist at one operation.
A declaration source, authority class, or resource kind never selects a
separate Analysis algorithm.

## Applicability domains

Compatibility is checked only for effects bound to the same exact occurrence.
Version 1 has two finite applicability axes.

### Completion applicability

Completion domains overlap as follows:

- `entry` overlaps every later terminal claim for the same obligation because
  an entry transition changes what remains available later;
- `exceptional-exit` overlaps only `exceptional-exit`;
- `normal-return` and `successful-await` overlap each other and
  outcome-dependent normal completions;
- two outcome tests on different source locations may both hold and therefore
  overlap;
- on the same source, opposite booleans, distinct exact enum constants,
  `null` versus `non-null`, distinct exact runtime types, and `null` versus an
  exact runtime type are disjoint; and
- every other pair is conservatively overlapping.

Disjoint completion domains are not contradictory.

### Guard applicability

An absent guard overlaps every guard. Two exact-runtime-type guards are
disjoint only when they test the same resolved subject against different exact
resolved types. Guards over different subjects may both hold and overlap.

The resolver does not prove arbitrary predicate satisfiability.

## Version-1 compatibility

Compatibility is a closed finite relation over already-bound facts. The
resolver rejects only the contradictions below; unlisted unequal effects are
additive obligations that later Analysis must satisfy.

### Obligation-kind domains

Compatibility compares a resolved source and an **obligation-kind domain**.
A concrete `kind=K<...>` denotes that exact bound kind. An omitted kind denotes
every resource obligation carried by the resolved source; it is not a separate
anonymous kind.

An `OperationSlot` kind narrows the source's domain. When both the effect and
slot specify kinds, their intersection is that exact kind only when the bound
kinds are equal; otherwise the domain is empty. An omitted effect or slot kind
leaves the other constraint in force. Two effects can conflict only on the
intersection of their source and kind domains.

Compatibility compares transition meaning on that intersection. Therefore an
all-kinds move and a kind-specific move to the same target may coexist, while
an all-kinds move and a kind-specific release conflict for the intersecting
kind.

### Entry ownership

`borrow`, `consume`, and `move`, `release`, or `accept` at `entry` are entry
ownership claims.

For overlapping resolved source and obligation-kind domains:

- several borrow effects are additive;
- a borrow conflicts with any consuming, moving, releasing, or accepting entry
  transition; and
- two non-borrow entry transitions are compatible only when their complete
  transition facts are equal.

Disjoint source or kind domains are not compared by this rule.

### Terminal ownership

`move`, `release`, and `accept` are terminal ownership claims.

For overlapping resolved source and obligation-kind domains, two terminal
claims conflict when their completion domains overlap and either:

- the completion meaning differs on that overlap; or
- their complete transition facts differ.

Complete transition facts include transition kind, destination,
correspondence, observation, and acceptance order. The obligation-kind domain
selects what is compared; it is not itself a transition difference on the
overlap.

### Operation facts

Two `operation` effects conflict when their guards overlap and their
`boundary` or `throws` facts differ.

### Independence

An `independent(source,target)` effect conflicts with a `borrow`, `derive`, or
`pass` effect over the same resolved source and target. Relationships through
a different source or target are independent claims and may coexist.

### Callback facts

Callback indices are model-local. After binding, two callback declarations for
the same delegate parameter conflict when execution or cardinality differs.
Equal callback facts coalesce.

### Non-claims of compatibility

Version 1 does not attempt general logical implication among unequal positive
facts. In particular:

- stronger and weaker declarations may both remain visible;
- multiple acquisition, authority, derivation, pass, outcome, or resource
  declarations are not rejected merely because they differ;
- distinct outcome and guard domains are not globally simplified; and
- the resolver does not decide whether an implementation fulfills every
  compatible declaration.

Those effects all reach later Analysis. A future compatibility rule requires a
language/design revision and a concrete supported case; it is not added as an
unresolved symbolic heuristic.

## Result algebra and atomicity

Each admitted target receives one deterministic evaluation:

| Evaluation | Meaning |
| --- | --- |
| `Resolved` | One or more exact occurrences matched, every required binding completed, and exhaustive population coverage proved no additional applicable occurrence is unknown. |
| `Unmatched` | Complete population evidence proves no occurrence matched. The declaration is inert for this request. |
| `Ambiguous` | Metadata supplied more than one authoritative candidate for a potentially applicable occurrence. |
| `Unsupported` | The occurrence uses a metadata or signature shape outside the version-1 resolver contract. |
| `Incomplete` | Required image, body, definition, forwarding, interface correspondence, signature, bounded-work, or exhaustive-population evidence is unavailable or incomplete; any exact positive matches remain attached. |

The whole operation returns one of:

- **Complete** — the occurrence population is complete, every target was
  exhaustively evaluated as `Resolved` or `Unmatched`, all bound effects are
  compatible, and the result publishes one immutable resolved snapshot;
- **Incomplete** — positive resolved effects and every unmatched result remain
  available beside one or more ambiguous, unsupported, or incomplete
  evaluations;
- **Conflict** — known resolved effects contradict at one or more exact
  occurrences; no composed resolved snapshot is published; or
- **Rejected** — request receipts are invalid, foreign, or stale.

Known conflict takes precedence over incompleteness because missing evidence
cannot make already contradictory declarations compatible. Conflict retains
every exact occurrence and provenance participating in each contradiction.

An incomplete result may support positive observations, but it cannot support
a complete-clean or absence claim for any scope affected by its gaps.
Downstream consumers must retain the incomplete evidence.

A positive match never upgrades incomplete coverage to `Resolved`. When some
occurrences match and another relevant image, body, operand, or candidate
cannot be evaluated, the target evaluation is `Incomplete` with its exact
positive matches and gaps.

No model or declaration has precedence. Conflict is not resolved by source
order, authority class, last writer, or model identity.

## Unmatched and inert declarations

A declaration is unmatched only relative to the exact complete occurrence
population in the request.

Examples:

- a model for an API absent from every admitted participant is unmatched and
  inert;
- a same-named API under another defining assembly is unmatched;
- a different overload is unmatched; and
- a declaration whose assembly may be present but whose defining metadata
  could not be opened is incomplete, not unmatched.

An unmatched declaration does not fail unrelated analysis and does not enter
compatibility checks.

## Interfaces, forwarding, and dispatch

Type forwarding follows Metadata and retains forwarding evidence.

An interface or virtual call may resolve the exact static operand and receive
effects declared on that selected contract occurrence. `DirectCall.ExactTarget`
continues to disclose whether the runtime target is fixed.

An effect declared on an interface member also applies to each exact concrete
implementation occurrence only when Metadata-owned correspondence proves that
the concrete MethodDef implements that exact closed interface slot through the
normal interface relationship or `MethodImpl` evidence. The resolved effect
retains both definition occurrences and the correspondence evidence.
The target evaluation is not complete until every potentially corresponding
concrete occurrence in the exact population has a terminal correspondence
outcome. Ambiguous, unavailable, rejected, or unsupported correspondence is
incomplete; a same-signature method with an authoritative no-relationship
outcome receives no propagated effect.

This propagation is definition correspondence, not runtime dispatch
expansion. The resolver does not copy effects between unrelated virtual
declarations and overrides or infer reflection, dynamic, or additional runtime
targets. When a consumer requires effects from runtime targets outside the
exact static operand or proven interface implementation, that need remains
visibly incomplete until a separately owned dispatch-expansion contract
supplies those targets.

## Bounds and failure

Resolution runs under a finite owner-dimensioned work ledger. At minimum it
charges:

- selector evaluations;
- candidate definitions examined;
- decoded signature nodes;
- field and nested-type resolution steps;
- invocation bindings;
- occurrence-bound effects;
- compatibility comparisons; and
- retained diagnostics and provenance associations.

Candidate indexes may conservatively admit extra work, but a complete
`Unmatched` result requires complete final matching over the request
population. Exhaustion produces typed incomplete evidence with the exact
dimension, limit, required work when known, target, occurrence when known, and
declaration provenance.

Malformed metadata, unsupported handles, unavailable images, forwarding
cycles, hop exhaustion, and `BadImageFormatException`-class decoding failures
are translated into typed resolution evidence. They do not escape as a
success-shaped empty result.

Cancellation remains cancellation and does not mint a receipt.

## Ordering, equality, and receipt

Resolved output is ordered by:

1. occurrence kind;
2. containing or defining participant identity;
3. module version ID;
4. metadata table kind and row;
5. physical evidence method, IL offset, operand token, and call kind for
   invocation sites;
6. canonical occurrence-bound effect; and
7. canonical provenance.

The resolved receipt is derived from:

- the exact admission receipt;
- Metadata catalog identity and generation;
- the occurrence-population receipt;
- ordered target evaluations;
- ordered occurrence-bound effects and their exact provenance associations;
- completion state; and
- ordered incomplete or conflict evidence.

The receipt does not hash display text. Equal receipts require equal exact
inputs, associations, completion, and output. A new Metadata generation, even
over byte-identical display data, produces a different receipt unless the
Metadata owner explicitly supplies a correspondence operation; this owner does
not transfer receipt identity across generations.

## Worked examples

### ArrayPool and a lookalike

Assume one generation contains:

- the selected framework `System.Buffers.ArrayPool<byte>.Shared` getter;
- one `Rent(int)` call occurrence on that authority;
- one `Return(byte[])` call occurrence on that authority; and
- `Example.ArrayPool<byte>.Rent(int)`, a same-named lookalike in another
  defining assembly.

The shipped model resolves only the framework occurrences:

```text
get_Shared definition
  -> authority(dotnet.array-pool.buffer<byte>,
       target=return,key=singleton<byte>)

Rent call at Method M, IL_0012
  -> acquire(dotnet.array-pool.buffer<byte>,
       target=return,when=normal-return,correspondence=receiver)

Return call at Method M, IL_0048
  -> release(dotnet.array-pool.buffer<byte>,
       source=parameter[0],when=normal-return,
       correspondence=receiver)

Example.ArrayPool<byte>.Rent
  -> unmatched
```

The result retains each physical call coordinate, selected definition,
`byte` generic binding, declaration provenance, admission receipt, and
Metadata generation. It does not decide whether the rented value reaches the
return, whether release occurs on every path, or whether the receiver is the
same runtime authority. Those are later flow and lifecycle questions.

### Latent conflict becomes concrete

Two admitted models declare the same parameter on the same structural method:

```text
borrow(source=parameter[0],target=receiver,access=read,scope=call)
consume(source=parameter[0],target=operation[0])
```

Admission retains both because no program has been selected. If only one
selector resolves, the other is unmatched and the resolved effect remains
usable. If both resolve to one exact method occurrence with overlapping entry
applicability and the same bound resource kind, entry-ownership compatibility
returns `Conflict` with both provenances. No partial composed snapshot is
published.

## Evidence plan

This specification is design-only. Its behavioral properties remain
**unverified** until #6729 adds Release gates.

The implementation gate must cover:

- exact framework ArrayPool `Shared`, `Rent`, and both `Return` overloads;
- a same-named lookalike under another defining assembly remaining unmatched;
- one nested type and one forwarded type through Metadata-owned resolution;
- exact method kind, staticness, generic arity, parameter ref kind, and return
  shape;
- one constructed generic call with type and method arguments substituted into
  resource kinds and structural locations;
- one open generic binding retaining exact scope rather than display identity;
- a kindless terminal effect overlapping each concrete carried resource kind,
  including compatible and conflicting kind-specific controls;
- property accessor, constructor, and field definition occurrences;
- callback and outcome local labels normalizing to occurrence-local facts;
- equal effects coalescing with every provenance association retained;
- borrow-versus-consume, unequal terminal transition, overlapping operation
  fact, borrow-versus-independent, derivation/pass-versus-independent, and
  callback conflicts;
- disjoint completion and guard controls remaining compatible;
- an absent API remaining inert;
- ambiguous definition, unavailable image, forwarding failure, unsupported
  signature, malformed metadata, and work exhaustion remaining visible;
- request planning contributing every required Metadata lookup before freeze,
  with unexpected `PlanExpansionRequired` remaining incomplete;
- an interface-member effect applying to exact implicit and explicit
  implementations through Metadata-owned correspondence, while a
  same-signature non-implementation receives no propagated effect and unresolved
  correspondence remains incomplete;
- separately retained copies with equal assembly/MVID/token values producing
  distinct participant-qualified physical invocation sites;
- unresolved calls retaining physical site identity without a selected
  definition;
- incomplete positive evidence never becoming `Resolved`, `Unmatched`, or a
  complete absence result;
- receipt inequality for changed admission, generation, occurrence
  population, provenance association, completion, or conflict evidence;
- no inspected-assembly loading; and
- non-vacuity by removing the shipped ArrayPool model or one required
  resolution adapter and observing the exact gate fail.

The focused gate belongs in the Release
`ILInspector.Analysis.Tests` executable. Metadata forwarding behavior may reuse
the existing Metadata fixtures and tests rather than duplicate their owner
contract.

The claim that no parallel metadata graph or hidden ArrayPool semantic path
exists receives no new source-policing absence gate. The former is enforced by
design review and dependency ownership; the latter retains the operator's
no-dedicated-gate choice from #6631.

## Implementation handoff

Issue #6729 implements this contract and the shipped typed C# ArrayPool model.
Its first integration point is the existing method-analysis construction path
over retained metadata and `DirectCall` evidence.

The implementation may add owner-appropriate immutable types and indexes, but
must not:

- perform a second IL body scan when the existing occurrence census is
  available;
- create a second assembly-binding or type-forwarding engine;
- use display names as semantic identity;
- make ArrayPool names part of the generic resolver;
- consume unresolved selectors directly in lifecycle Analysis; or
- silently downgrade ambiguity, unsupported metadata, or incomplete
  definition resolution to unmatched.

Issue #6730 consumes occurrence-bound effects to generalize method-local
ownership flow. #6731 owns lifecycle and Finding migration. #6732 owns
Research migration. Their algorithms and public evidence shapes are not
specified here.

## Non-claims

This design does not claim:

- that every valid declaration matches an inspected API;
- that every exact static operand identifies every runtime dispatch target;
- support for function pointers, reflection, dynamic dispatch, module
  references, or every CLI metadata signature;
- that compatible declarations are truthful or implemented correctly;
- that an unmatched declaration proves an API absent outside the exact
  occurrence-population receipt;
- complete resource value flow, aliasing, field flow, callback invocation,
  async state-machine, unsafe, interop, exception-path, or interprocedural
  reasoning;
- lifecycle violations, clean lifecycles, actionability, impact, confidence,
  remediation, or Finding production;
- a product JSON ingestion surface;
- cross-generation receipt stability; or
- general satisfiability of unresolved declarations.
