# C# assembly round-trip testing

> **Map:** [Type, member, and API representation](type-member-api-representation.md) is the entry
> point for choosing a type, member, or API identity shape. This document owns
> the details below.

## Summary

This document proposes a modest tools-only capability for compiling C# artifacts
reconstructed from a managed assembly and comparing the resulting assembly with
the original through the repository's existing C# and IL diff tools.

The first goal is evidence, not mutation:

```text
original assembly
  -> select member set and reconstruction scope
  -> produce a C# artifact
  -> compile the artifact with Roslyn
  -> compare original and rebuilt members through C# and IL diff
  -> report exact, different, unavailable, and unsupported outcomes
```

The proposal supports two reconstruction scopes:

- `cluster` reconstructs the selected members and the declarations needed to
  compile them;
- `all` attempts every supported declaration in the target module in one
  compilation.

It also supports two independent body policies:

- `selected` supplies real bodies only for the selected members and required C#
  companions;
- `full` attempts a real body for every supported concrete member in scope.

Compilation success, C# comparison, and IL comparison are separate results. The
current diff tools are the arbiters within their documented boundaries. This
proposal does not add PE patching, claim byte-for-byte assembly reproduction, or
strengthen metadata equality.

Issue [#4810](https://github.com/richlander/dotnet-inspect/issues/4810) adds the
focused tools-owned contract for compile-back reference closure, exact assembly
provenance, and product-whole-member admission. It closes two unsafe assumptions
exposed while hardening
[#4276](https://github.com/richlander/dotnet-inspect/pull/4276): assembly simple
names are not reference identities, and successful signature spelling alone
does not prove that an emitted body has a complete or correctly bound compile
context.

## Goals

- Start with one selected member or an explicit member set.
- Reconstruct enough typed C# context for Roslyn compilation.
- Support both focused `cluster` and broad `all` scopes through one artifact
  provider and result model.
- Compare original and rebuilt members through existing product-owned C# and IL
  diff capabilities.
- Preserve exact, different, unavailable, and unsupported as distinct outcomes.
- Record compiler, reference, scope, member, and source provenance.
- Reuse the capability from ReturnToSender, decompiler fidelity tests, mutation
  tests, and other tools-side consumers.
- Establish a measured base from which C#, IL, metadata, or PE comparison may be
  strengthened later.

## Non-goals

- Do not modify or emit a patched copy of the input assembly.
- Do not claim byte-for-byte PE or metadata equality.
- Do not claim semantic equivalence from compilation or diff equality.
- Do not reconstruct a complete build project, source tree, generator graph, or
  build-task environment.
- Do not add Roslyn, inspected-assembly loading, or round-trip orchestration to
  shipped product paths.
- Do not make the harness compensate for missing product C# artifact behavior.
- Do not execute inspected or rebuilt assemblies during ordinary round-trip
  testing.
- Do not require stricter metadata, exception-region, local-signature, PDB, or PE
  comparison before the first useful round-trip measurements.
- Do not add Metadata forwarding or accessibility semantics. Compile-back
  consumes the signature-spellability aggregate owned by
  [`type-forwarding-resolution.md`](type-forwarding-resolution.md).
- Do not expand C# lexical-precedence policy. Issue
  [#4721](https://github.com/richlander/dotnet-inspect/issues/4721) owns that
  concern; this contract only refuses to equate equal spellings with equal
  definitions.
- Do not add Analysis control-flow or data-flow interpretation. Body closure
  consumes the shared `ILInspector.Instructions` Layer-0 stream and method-body
  metadata only.
- Do not reinterpret C# or IL diff results. Compile-context proof is an
  additional prerequisite for an `Exact` round-trip result.

Assembly patching remains a possible later consumer. It requires a separate
design for PE writing, metadata preservation, signing, PDBs, output safety, and
patch authorization. No result defined here authorizes binary mutation.

## Proof levels

Round-trip results are layered. A higher layer never changes the meaning of a
lower result.

| Level | Result | What it proves | What it does not prove |
| --- | --- | --- | --- |
| Artifact | produced, failed, unsupported | The requested typed C# artifact was or was not produced. | That the source compiles or is faithful. |
| Compile | succeeded, failed | Roslyn emitted a donor assembly under the recorded context. | That bindings match a broader context or the original implementation. |
| C# diff | exact, changed, unavailable | Product-decompiled C# compares as reported by `CSharpBodyDiff`. | Source or runtime semantic equivalence. |
| IL diff | exact, operand diff, opcode diff, unavailable | Decoded operations compare as reported by `IlBodyDiff` under a named normalization. | Equality of EH, locals, metadata, PE layout, or runtime behavior. |
| Scope A/B | same, different, unavailable | `cluster` and `all` produced the same or different selected-member evidence. | That either scope reproduces the original build environment. |

An overall report composes these outcomes but does not collapse them into a
single success boolean. For example, `CompileSucceeded + CSharpChanged +
IlExact` is a useful and honest result.

## Scope

### Cluster

`cluster` begins with the top-level roots containing the selected members. The
planner seeds typed dependencies known from signatures and selected bodies, asks
the product artifact provider for source, compiles it, and may expand the typed
request from supported Roslyn diagnostics. Growth is bounded by root and
iteration budgets.

The current ReturnToSender/`CB_CLUSTER` compiler-driven membership algorithm is
the starting point. Its purpose is to find a compilable artifact, not to prove
that the reduced declaration universe has identical overload or extension
binding to the complete module.

Every included declaration records provenance such as:

- selected target;
- containing or nested declaration;
- signature, constraint, base-type, or interface dependency;
- typed body reference or required member surface;
- compiler-diagnostic feedback;
- inseparable C# companion.

### All

`all` seeds every supported top-level root in the target module in metadata
order and requests one coherent compilation artifact. It uses the same product
artifact provider, typed declaration requests, C# printer, compiler, and result
model as `cluster`.

An `all` artifact is declaration-complete only when every supported declaration
in the target module is represented. Unsupported declarations remain visible and
make the scope outcome incomplete; they are not silently omitted. Independently,
the body-policy outcome records whether every concrete member that requires a
real body under `selected` or `full` produced one. Declaration completeness and
body completeness are separate fields.

### Cross-scope comparison

Scope A/B is a controlled donor-to-donor comparison derived from one common
request and compilation context. Its typed pair key includes:

- exact artifact bytes and module identity;
- requested targets and supplied replacement bodies;
- body policy;
- compiler and parse options;
- a frozen, ordered reference set with artifact content hashes, module identities,
  aliases, and embed-interop roles;
- C# and IL diff policies and normalizations.

`Scope` is the only request field permitted to differ. Closure roots, included
declarations, and effective companion bodies are derived results rather than
pair-key inputs; the comparison retains differences in those results as
provenance. A pair-key mismatch produces `Unavailable` with a typed context
reason, never `Same` or `Different`.

For a selected member captured by both eligible donors, the harness compares the
cluster donor directly with the all donor:

```text
cluster donor <-> all donor
```

The comparison resolves exact cluster-donor and all-donor method handles through
a typed cross-reader correspondence result. That resolver is a planned product
capability; normalized `ResearchMemberIdentity` strings are correspondence input,
not a handle resolver by themselves. The direct donor comparison then applies
the existing C# and IL diff contracts. Separate original-to-cluster and
original-to-all correspondence and diff results remain available as fidelity
evidence, but they are not substituted for the direct scope comparison. A donor
difference may reveal context-sensitive binding, incomplete cluster membership,
different synthesized context, or an artifact-production gap. It is evidence,
not automatically a cluster defect. If either donor or member correspondence is
absent, ambiguous, or failed, the scope comparison is `Unavailable` and retains
the typed reason.

The initial `cluster` lane remains useful without a successful `all` lane. A
consumer making a stronger contextual-binding claim must require the scope A/B
result explicitly; ordinary compilation and fidelity measurements do not imply
it.

## Body policy

### Selected

`selected` supplies real bodies for requested targets. Some C# declarations
require companion bodies or declaration decisions, such as paired event
accessors or constructor initialization. These are recorded in a typed effective
body set with provenance and remain distinct from the explicitly requested target
set.

Other concrete members use product-owned skeleton or stub policies. A stub is
part of compilation context, not a fidelity result for that member.

### Full

`full` attempts a real body for every supported concrete member participating in
the selected scope. Each member retains its own artifact-production and
decompiler-fidelity status. Failed or partial bodies must not silently become
successful stubs.

`cluster + full` means full bodies for the final cluster. `all + full` is the
broad whole-module round-trip lane and is expected to expose a substantially
larger unsupported frontier.

The initial `full` harness lane accepts exactly one primary target. A caller
supplying more than one receives an explicit unsupported-operation failure;
the harness must not compile one donor per target and present those artifacts
as a coherent target-set round trip. A later target-set implementation must
plan all replacements into one typed artifact and compile one donor.

## Architecture

```text
 Metadata facts + tools-side scope/body plan
                       |
                       v
   +-----------------------------------------+
   | Product artifact pipeline                |
   | TypeShellProducer      (existing)         |
   | typed MemberBodyProducer seam (planned)  |
   | CSharpTypePrinter      (existing)         |
   +-------------------+---------------------+
                       |
                       v
   +-----------------------------------------+
   | Round-trip compile engine               |
   | compiler feedback + Roslyn              |
   +-------------------+---------------------+
                       |
                       v
   +-----------------------------------------+
   | Donor assembly + compile provenance     |
   +-------------------+---------------------+
                       |
                +------+------+
                |             |
                v             v
     C# body diff    IL body diff
          |             |
          +------+------+
                 v
        layered round-trip result
```

The reusable capability is a round-trip compile engine plus result composition
over the product artifact pipeline. The shell producer and printer already
exist; the typed member-body increment is an explicit prerequisite below.
ReturnToSender is one consumer, not the general abstraction.

## Ownership boundaries

### Product libraries

- **Metadata** owns metadata facts and declaration identities.
- **CSharp** owns `TypeShellProducer` for metadata-backed typed shell composition
  and `CSharpTypePrinter` for rendering typed requests as C# source.
- **Decompiler** owns `MemberBodyProducer` for selected-member C# body production
  and fidelity grade. Its current public API returns composed source; the plan
  must add a handle-addressed result that exposes a typed `CSharpMemberBody` plus
  fidelity and failure provenance before tools consume individual bodies.
- **ILDiff** owns `IlBodyDiff`, its normalization mechanics, and its total
  exact/different/unavailable outcome.
- **Research** owns `ImplementationDiff`, joining product C# and IL evidence.

The existing diff tools retain their documented limitations. The round-trip
engine consumes their typed results and must not strengthen or reinterpret them.

### Tools and tests

- The round-trip engine owns Roslyn options, reference selection, diagnostic
  feedback, budgets, compilation provenance, and orchestration.
- Scope policy owns `cluster` versus `all` root selection.
- Body policy owns selected targets, effective companion bodies, and full-body
  participation.
- The tools-side planner discovers roots and members and builds neutral
  `CSharpTypeShellSpec` inputs; it does not format declarations.
- Harnesses own fixtures, comparison policy, assertions, and reporting.

`TypeShellProducer` and `CSharpTypePrinter` already produce and render typed C#
declarations. The planned member-scoped `MemberBodyProducer` seam supplies typed
body increments without composing a parallel declaration. The harness may select
scope, members, and typed body policies, but it must not format declarations or
reconstruct decompiled bodies itself. Compiler feedback may expand a typed
request but must not trigger ad hoc source patches that compensate for missing
product behavior.

## Typed request and result

The exact API remains implementation work, but the request boundary should
resemble:

```csharp
public enum RoundTripScope
{
    Cluster,
    All,
}

public enum RoundTripBodyPolicy
{
    Selected,
    Full,
}

public sealed record RoundTripMethodReplacement(
    MemberAnchor Method,
    CSharpBlockBody Body);

public sealed record RoundTripRequest(
    ArtifactIdentity Artifact,
    ModuleIdentity Module,
    IReadOnlyList<MemberAnchor> Targets,
    RoundTripScope Scope,
    RoundTripBodyPolicy BodyPolicy,
    IReadOnlyList<RoundTripMethodReplacement> Replacements);
```

`ArtifactIdentity` identifies one artifact in a sealed artifact generation; it
does not by itself grant content access or carry acquisition provenance.
`ModuleIdentity` includes module name and MVID so a member anchor is never
interpreted without its physical metadata scope. The request resolves both
through the owning artifact session, whose acquisition registration and guarded
retained content supply the exact bytes and provenance. Display text and a
readable path are not identity or read authority.
For the first contract, supplied C# is a `CSharpBlockBody` addressed to one
metadata method definition: an ordinary method, constructor, or individual
property/event accessor. The product artifact pipeline maps that method body into
the containing declaration shape. `CSharpFieldInitializer`, aggregate
`CSharpPropertyBody`/`CSharpEventBody` replacements, and complete member or type
declarations are outside the first contract. Initializers require a later typed
lowering-correspondence design because their effects may span multiple instance
constructors or the type initializer.

A scope-pair key uses canonical method ordering and a versioned content digest
over source, async/unsafe modifiers, constructor-initializer kind, and initializer
arguments; object or collection reference equality is never used as replacement
identity.

The result should carry:

- the exact request and input identity;
- requested targets and the provenance-bearing effective body set;
- included roots, members, and reasons;
- generated C# source or source files;
- artifact-production diagnostics;
- exact compiler and parse options;
- the frozen ordered reference descriptors, including artifact hashes, module
  identities, aliases, and embed-interop roles;
- all Roslyn diagnostics, including those used for cluster growth;
- closure iterations and bail reasons;
- emitted donor PE and portable PDB bytes when compilation succeeds;
- typed C# and IL diff outcomes per selected or full-policy member;
- scope A/B evidence when both runs are available.

Artifact production, compilation, C# comparison, and IL comparison statuses are
separate fields.

## Diff arbiters

### C# arbiter

The C# arbiter is a total round-trip envelope over producer-native inspection and
diff evidence:

- `Exact` requires `Complete` body inspections at both endpoints, successful
  typed member correspondence, no producer failure rows, and an exact
  `CSharpBodyDiff`;
- `Changed` requires `Complete` body inspections at both endpoints and preserves
  the producer-owned diff rows;
- `Unavailable` retains the endpoint's `Absent` or `Failed` inspection, identity
  failure, or decompilation/diff failure reason.

This precondition is deliberate: `CSharpBodyDiffResult.IsExact` alone is not the
arbiter because an empty native diff can also arise when a body fingerprint is
absent. The round-trip envelope consumes the retained Finding inspection state
and preserves all producer-owned rows and failures.

C# equality is useful for spelling and decompiler stability. It does not prove
that authored source, reconstructed source, or compiled behavior is equivalent.

### IL arbiter

`IlBodyDiff` compares decoded operations and returns `Exact`, `OperandDiff`,
`OpcodeDiff`, or `Unavailable` under explicit normalization. ReturnToSender may
continue to compose this product result into its versioned harness fidelity
contract.

Current IL equality does not include explicit EH-region rows and does not prove
local-signature, metadata, PE-layout, source, or runtime semantic equality. Those
are named non-guarantees, not blockers for collecting current round-trip
evidence.

### Metadata and PE evidence

The first implementation may record coarse declaration/API changes already
available from product metadata comparison, but no strict metadata or PE verdict
is required for collecting the initial round-trip evidence.

Future measured needs may add:

- stricter declaration and metadata-graph comparison;
- local-signature and explicit EH-region evidence;
- module, resource, attribute, PDB, and debug-directory comparison;
- PE header, section, layout, signature, and byte-region comparison.

Each extension adds a new typed result or strengthens a versioned contract. It
must not retroactively change what an earlier `IlExact` result claimed.

## Compiler and reference provenance

Every run records the context needed to interpret a result:

- Roslyn/compiler version;
- language version and feature switches;
- optimization, overflow, nullable, unsafe, and deterministic options;
- target framework and platform references;
- resolved package and assembly references;
- reference aliases and embed-interop policy when relevant;
- assembly and module attributes that affect lowering;
- unresolved project-level inputs such as generators or build tasks.

The assembly/package resolution service provides candidate acquisition,
assembly identity, and Metadata binding. The tools-side engine owns compile
closure and the compilation policy that consumes those results.

### Focused owner and boundary

`tools/DecompilerHarness` owns compile-back closure planning, exact compiler
reference selection, and product-whole-member admission. The owner entry in
[`../overview.md`](../overview.md) is authoritative.

The owner consumes, but does not redefine:

- the source artifact, selected-member anchor, and declaration plan from the C#
  artifact producer;
- the signature-spellability aggregate, including its external definitions and
  local requirements, from `ILInspector.Metadata`;
- `MethodInstructions` and `DecodedInstruction` from the shared instruction
  substrate;
- guarded metadata name, signature, and resolution operations;
- compiler diagnostics and rebuilt PE bytes from the tools compiler adapter;
- C# and IL comparison results from their existing owners.

The closed tools-owned question is:

> Does this exact generated artifact have one complete, unambiguous compile
> context, and did the rebuilt member bind to the same source-local and external
> definitions required by the original member?

The planner does not publish body-analysis facts, infer source semantics, parse
or repair product C#, or create a second metadata identity model.

### Frozen reference inventory and selected set

Reference discovery and compiler selection are separate typed stages. Discovery
produces a `CompileReferenceInventory` containing every candidate considered.
Selection produces either one immutable `CompileReferenceSet` or a typed
failure. Discovery order is never a binding policy.

Each `CompileReferenceDescriptor` records:

- a stable inventory ID and selected ordinal;
- acquisition registration and provenance;
- the exact content digest of the bytes supplied to the compiler;
- module identity, including MVID;
- full assembly definition identity;
- the owner-issued retained-content registration;
- inert source-path or remote-location provenance, when available;
- aliases and embed-interop role;
- whether platform policy authorized it as a trusted platform reference.

The selected set records the current source artifact separately. The current
artifact contributes source-local declaration identities but is not silently
reintroduced as a metadata reference to satisfy its own generated source.

Selection follows these rules:

1. Exact repeated registrations of the same bytes and module may coalesce.
2. Assembly simple name is diagnostic data, never a uniqueness key.
3. Candidates with the same simple name but different full identity, content
   digest, or MVID remain distinct until policy selects one.
4. If a requested identity admits multiple non-corresponding candidates,
   selection returns `ReferenceSelectionAmbiguous`; enumeration order,
   platform-first order, and "first definition wins" are forbidden.
5. Trusted-platform preference applies only when acquisition and platform
   contracts authorize that exact candidate. It does not erase conflicting
   package or local candidates from the inventory.
6. Metadata resolution and Roslyn references use the same selected descriptors
   and owner-retained immutable snapshots under one current query lease.
   Neither consumer reopens the source path. If retained content cannot be
   opened, selection fails visibly rather than reacquiring replacement bytes.

Selection produces one canonical descriptor order after every selected snapshot
is retained. The set freezes before signature spellability, body closure,
artifact admission, or compilation. Its digest covers ordered descriptors and
every compiler-relevant role. Reordering discovery input without changing
policy or candidates cannot change the selected order, identity, or outcome.
The digest authenticates the retained snapshot; it is not a substitute for
retention, and bracketing hashes of a mutable path are insufficient.

### Compile-context definition identity

C# spelling is not assembly identity. `CompileDefinitionIdentity` is a closed
union:

- `SourceLocalDefinition` identifies an original `TypeDef` included in the
  generated source declaration plan;
- `ExternalDefinition` identifies an exact selected reference descriptor and
  resolved terminal `TypeDef`;
- `IntrinsicDefinition` identifies a compiler intrinsic for which Metadata
  requires no external definition;
- `CompilerSynthesizedDefinition` identifies a source-local generated
  definition that the artifact intentionally asks the compiler to regenerate;
- `UnresolvedDefinition` retains a typed failure and cannot participate in
  success.

The source-local arm uses original artifact/module identity and
`TypeDefinitionHandle`. A rebuilt declaration projects back to that identity
only through the typed declaration plan and rebuilt-member correspondence.
Equal namespace-qualified text is insufficient.

The compiler-synthesized arm retains the original module, token, and Metadata
evidence that the definition is generated, plus the emitted member whose C#
artifact can cause regeneration. It is a deferred binding obligation, not a
local declaration receipt. It completes only when the existing owner-issued
correspondence for that generated construct uniquely relates it to a rebuilt
definition. Different construct kinds may consume different owner-issued
correspondence results; tools select by typed construct evidence, not generated
name text. An unrecognized, forged, absent, or ambiguous generated shape makes
the compile-context receipt unavailable.

The external arm is local to the frozen compile context. It uses the selected
descriptor ID and terminal type-definition token, not an opaque catalog key
from another lifetime. Original and rebuilt resolution must map through the
same descriptor before definitions correspond.

A namespace-qualified C# name remains a spelling and diagnostic key. If one
spelling denotes both a source-local definition and a non-corresponding external
definition, both identities remain present. Without an owner-issued binding
proof, admission returns `DefinitionBindingAmbiguous`; it never chooses by full
name.

### Closure requirements

`CompileClosurePlan` is the immutable union of signature, declaration-shape,
local-declaration, and body requirements for the selected artifact. It covers
every declaration and every real body emitted by the effective body policy,
including selected targets, required companions, and `full`-policy members.
Each requirement records:

- its `CompileDefinitionIdentity` or unresolved state;
- original module and metadata handle;
- role and occurrence provenance;
- whether generated source, one selected reference, or an intrinsic must
  provide it;
- the exact selected descriptor for an external requirement.

Duplicates retain every occurrence. They may share one resolved definition but
cannot erase a stronger requirement or a failure.

Signature requirements come from the Metadata-owned immutable
signature-spellability aggregate. External definitions become exact selected
reference requirements. Every `LocalRequirement` needs a
`LocalDeclarationReceipt` proving that the exact source `TypeDef`, including its
containing declaration chain, is present and nameable in the generated
artifact. A same-named external definition cannot satisfy it.

Metadata's compatibility `CanSpell` projection may authorize this artifact only
after tools have discharged all local requirements. Metadata retains the
artifact-independent aggregate; tools own the artifact-specific receipt.

### Declaration-reference census

Member signatures are not the only declaration syntax that binds types. Tools
create a conservative `DeclarationReferenceCensus` for every source `TypeDef`
and member included by the tools-owned artifact plan. The closed metadata
surfaces that can feed declaration rendering include:

- type base clauses and implemented-interface clauses;
- type and method generic-parameter constraints;
- `MethodImpl` declarations and explicit-interface qualifiers;
- metadata-backed attributes selected for emission, including constructor
  declaring types and type- or enum-valued arguments;
- any later typed declaration component that the artifact plan opts into and
  that can contain a metadata-origin type occurrence.

The artifact plan supplies source declaration handles; tools enumerate the
closed surfaces from those handles and may conservatively retain an occurrence
that the current printer suppresses. Tools do not ask CSharp to expose its
rendering internals and do not rediscover declarations from rendered text. Each
occurrence resolves through the same guarded Metadata operations and frozen
context used by member signatures. If the artifact plan introduces a textual
declaration component with no typed origin, the census is `Incomplete`; equal
text cannot substitute for the missing identity.

Declaration occurrences become ordinary local, external, intrinsic, or
compiler-synthesized, or unresolved closure requirements. The rebuilt binding
receipt repeats the census against corresponding donor declarations. A base,
interface, constraint, explicit-interface, or attribute type cannot rebind
between source-local and external definitions merely because both use the same
C# full name.

### Conservative body-reference census

Signature closure is insufficient because generated C# may use types found only
in a method body. Tools create one closed, conservative
`BodyReferenceCensus` for every original member whose real body is emitted.
Each census comes from:

- the complete shared `MethodInstructions` decode;
- every metadata-token-bearing IL operand;
- the method local signature;
- exception-region catch types;
- referenced member, method-specification, type-specification, and standalone
  signatures needed to expose their named-type requirements.

The census uses guarded Metadata name/signature operations and the same
resolution context as signature spellability. It does not interpret control
flow, evaluation-stack meaning, reachability, or product C# text. It may retain
requirements that product C# later spells implicitly or eliminates. That
conservative over-approximation may decline an unsafe artifact but cannot
justify a false success. A source-local generated definition that is not emitted
as a declaration remains in the census as a
`CompilerSynthesizedDefinition` obligation; it is neither silently dropped nor
treated as a missing compiler reference.

Every relationship walk uses the owning Metadata or instruction safety bound.
An invalid token, incomplete instruction decode, unsupported signature,
exceeded bound, unsupported module reference, or unresolved named type makes
the census incomplete with exact provenance. Incompleteness cannot become an
empty requirement set.

Body occurrences retain IL offset, opcode, operand kind, metadata token, local
slot, exception-region ordinal, parent token, and signature path as applicable.
The census is a tools-only compile-closure artifact, not an Analysis Finding or
a reusable interpretation of method behavior.

### Closure outcome

Evaluating the plan against the frozen reference set produces one closed
`CompileClosureOutcome`:

- `Complete` maps every requirement to an intrinsic,
  `LocalDeclarationReceipt`, exact `CompileReferenceDescriptor`, or retained
  compiler-synthesized binding obligation;
- `Missing` retains every requirement for which no provider exists;
- `Ambiguous` retains every requirement with multiple non-corresponding
  providers;
- `Incomplete` retains decode, resolution, safety-bound, and unsupported-scope
  failures.

The evaluator does not add references after seeing compiler diagnostics and
does not retry with a different set. A missing body-only dependency therefore
becomes `Missing` before product-whole-member admission and compilation. A
neighboring complete artifact remains eligible under the same policy.

### Planning convergence and freeze

Reference selection happens once before declaration-cluster iteration and never
grows from compiler diagnostics. Declaration membership may still converge
through the existing bounded typed-root process.

For each proposed declaration plan, tools recompute signature requirements,
declaration-reference censuses, local receipts, and every effective real-body
census against the same frozen reference set. A missing local declaration may
contribute a typed declaration root. A missing or ambiguous external provider
stops closure; it cannot add a reference from a compiler search path.

Supported compiler diagnostics may contribute another typed declaration root.
Doing so invalidates that iteration's artifact, closure, compilation, and
binding receipts. The replacement declaration plan is rendered and evaluated
again from its immutable inputs. Only the converged declaration plan and final
compilation can contribute durable receipts or fidelity outcomes.

### Rebuilt binding receipt

Compilation success proves that Roslyn found a compilable binding, not that it
found the intended binding. After compilation, tools open the rebuilt PE with
SRM and create a `RebuiltBindingReceipt`.

The receipt resolves the rebuilt target through existing structural member
correspondence and compares:

- every selected declaration signature's original and rebuilt definition
  identities;
- original and rebuilt declaration-reference censuses;
- original and rebuilt body-reference censuses for every effective real body;
- each rebuilt local definition projected through the typed declaration plan;
- each rebuilt external definition through the exact frozen reference
  descriptor that supplied it;
- each deferred compiler-synthesized definition through the applicable
  owner-issued cross-reader correspondence.

For an `Exact` claim, the rebuilt census cannot replace an external definition
with a source-local definition, replace one external definition with another,
or leave a required occurrence unresolved. Equal C# full names and equal
assembly simple names do not establish correspondence. A conservative extra
requirement or shape without provable correspondence returns
`BindingReceiptUnavailable`; it does not weaken comparison.

The receipt materializes before compiler workspaces, semantic models, PE
readers, Metadata sessions, or other disposable owners are released. No Roslyn
or lifetime-bound Metadata object escapes into durable results.

### Compile-context receipt and fidelity gating

Every generated artifact policy, including the existing legacy shell and the
product-whole-member path, produces one closed `CompileContextOutcome`.
`Complete` carries a `CompileContextReceipt` binding the exact artifact digest
to its frozen reference set, converged closure, compilation, correspondence,
and rebuilt binding evidence. `Unavailable` retains the exact failed or
incomplete stage and cannot expose a receipt.

Any `CompileBackStatus.Exact` requires a complete receipt for the exact artifact
whose fidelity is being reported. This rule is independent of artifact policy.
A local/external same-FQN rebind therefore cannot become `Exact` by declining
the product artifact and routing through the legacy shell. A legacy artifact
may retain `Exact` only when its own context receipt is complete, and its report
must identify the legacy policy.

### Product-whole-member admission

Creating a product-owned artifact is provisional selection, not admission.
`ProductWholeMemberAdmission` is evaluated only after:

1. the exact artifact and typed declaration plan were produced;
2. the frozen reference set was selected without ambiguity;
3. signature, declaration, and body closure are `Complete`;
4. all Metadata `LocalRequirement` values have declaration receipts;
5. its exact artifact-specific `CompileContextReceipt` is complete.

The closed result is:

- `Admitted`, carrying artifact, compile-context digest, closure, compilation,
  and rebuilt-binding receipts;
- `Declined`, carrying typed pre-compilation closure or ambiguity reasons and
  the selected legacy policy, when permitted;
- `Failed`, carrying an artifact, compilation, rebuilt-resolution, or binding
  failure after provisional selection.

The current `UsedProductWholeMember` boolean cannot represent these states. It
may remain as a compatibility projection only if it means `Admission is
Admitted`; provisional, declined, and failed artifacts project false and retain
their richer outcome.

A pre-compilation `Declined` result may select the independently defined legacy
artifact policy, with the reason visible. A post-selection `Failed` result
remains a failure. A separately labelled legacy control cannot replace it or
be reported as the product result.

An overall result that attributes `Exact` to the product-whole-member artifact
requires `ProductWholeMemberAdmission.Admitted`. C# or IL equality without
complete context and binding receipts is `FidelityUnavailable`, never a
product-whole-member `Exact`. A legacy result cannot borrow the declined or
failed product artifact's receipts.

### Current explicit-member consumer

The explicit-member path under review in PR #4276 is a consumer of this contract,
not a separate policy:

1. targeted and batch modes create the same frozen reference inventory and
   selected set;
2. the current explicit member is provisionally rendered as a product-owned
   whole-member artifact;
3. its declaration signatures, declaration-shape references, local
   requirements, and every effective real body contribute to the
   artifact-specific closure plan;
4. a pre-compilation `Declined` outcome selects the existing legacy policy with
   visible reasons;
5. a provisional product artifact that later fails compilation or binding
   remains `Failed`;
6. only `Admitted` product evidence or a separately complete legacy context may
   contribute a fidelity verdict.

This gating does not admit another explicit-member shape and does not change
product spelling. Both public harness modes call the same planner and evaluator;
neither may retain the current simple-name reference map or an independent
signature-only shortcut.

## Failure model

| Layer | Example outcomes |
| --- | --- |
| Selection | target missing, ambiguous overload, unsupported member kind |
| Artifact production | unsupported declaration, partial body, missing typed fact |
| Reference selection | exact set, missing identity, ambiguous candidates, changed bytes |
| Closure | complete, missing requirement, ambiguous provider, incomplete census |
| Compilation | parse failure, bind failure, emit failure |
| Correspondence | rebuilt member missing, ambiguous, or wrong module |
| Binding | corresponding, rebound definition, incomplete receipt |
| C# diff | exact, changed, unavailable |
| IL diff | exact, operand diff, opcode diff, unavailable |
| Scope A/B | same, different, unavailable |

No layer converts failure or unavailability into an empty successful result.

## Proposed milestones

### Milestone 1: extract round-trip compilation

- Define tools-only request and layered result contracts.
- Replace simple-name/first-wins reference selection with the frozen exact
  inventory, canonical selected set, and typed ambiguity outcomes.
- Add artifact-specific signature, declaration-shape, local-declaration, and
  effective-body closure plus deferred compiler-generated correspondence and
  the rebuilt binding and compile-context receipts required by every `Exact`.
- Add typed product-whole-member provisional, declined, failed, and admitted
  outcomes without allowing a legacy artifact to borrow product receipts.
- Add the missing product-owned, handle-addressed `MemberBodyProducer` result that
  returns a typed `CSharpMemberBody` plus fidelity and failure provenance; adapt
  ReturnToSender away from its harness-side body conversion.
- Wire the product artifact pipeline explicitly: the tools planner builds neutral
  `CSharpTypeShellSpec` inputs, `TypeShellProducer` builds typed print requests,
  the new member-body seam supplies decompiled body increments, and
  `CSharpTypePrinter` renders source.
- Extract compiler-driven root growth from decompiler-specific comparison.
- Adapt ReturnToSender to consume the engine, preserving safe verdicts while
  reclassifying outcomes that lack complete compile-context evidence.
- Preserve current cluster budgets, provenance, and failure buckets.
- Add focused seam tests for exact and wrong-reader handles, body absence,
  decompilation failure, fidelity provenance, accessor methods, constructors and
  constructor initializers, and parity with current ReturnToSender output.

### Milestone 2: general selected-member comparison

- Support supplied replacement bodies independently of decompiler-produced
  bodies.
- Reject field-initializer and aggregate property/event replacement shapes in the
  first method-addressed contract; test each rejection explicitly.
- Add a product-owned cross-reader member-correspondence resolver that consumes
  typed/normalized identities and returns exact endpoint handles or total
  absent/ambiguous/failed outcomes.
- Resolve original/cluster-donor, original/all-donor, and
  cluster-donor/all-donor correspondence independently before invoking
  member-scoped diff APIs.
- Compare original and donor members through `ImplementationDiff` and typed IL
  results.
- Preserve requested targets and effective companion bodies separately.
- Emit machine-readable round-trip results.

### Milestone 3: add `all + selected`

- Seed all supported declarations in the target module.
- Report complete and incomplete artifact production.
- Derive cluster/all runs from one typed common-context request and reject any
  pair where more than scope differs.
- Resolve the reference set once, freeze its ordered exact artifact hashes,
  module identities, aliases, and embed-interop roles, and pass the same set to
  both compilations.
- Compare eligible cluster and all donors directly, retaining the separate
  original-to-donor fidelity results.
- Add clean-but-different binding fixtures.
- Measure focused and real-assembly costs before setting default caps.

### Milestone 4: add `full` body policy

- Attempt real bodies for every supported concrete member in scope.
- Preserve per-member artifact, compilation, C#, and IL outcomes.
- Establish a whole-module round-trip report without weakening selected-member
  evidence.

### Milestone 5: strengthen comparison when measured

- Use corpus results to identify which metadata, EH, local, PDB, or PE evidence
  would change decisions.
- Extend the owning product diff substrate rather than synthesizing stronger
  equality in the harness.
- Version strengthened contracts so historical reports retain their meaning.

Binary patching is not a milestone in this plan. It should begin with a separate
proposal only after round-trip evidence demonstrates a concrete use case and the
required preservation boundary.

## Validation strategy

- Focused fixtures prove target selection, cluster growth, and close negative
  cases.
- Real compiler-produced assemblies prove metadata and IL shapes.
- Cluster/all A/B fixtures include silent alternate overload, operator,
  conversion, and extension-method binding.
- Scope A/B fixtures change compiler options, references, replacements, body
  policy, normalization, and input identity one at a time and require a typed
  `Unavailable` context-mismatch result.
- A reference fixture supplies different binaries with the same assembly identity
  and requires their content-hash mismatch to make scope A/B unavailable.
- C# and IL tests retain producer-native unavailable and failed results.
- A C# regression fixture supplies an absent endpoint fingerprint whose native
  diff is empty and requires the round-trip envelope to report `Unavailable`,
  never `Exact`.
- Cross-reader correspondence fixtures cover API/metadata anchor spelling
  differences, signature collisions, and wrong-module near misses.
- `selected` tests distinguish requested targets from effective companion bodies.
- A scope A/B fixture derives asymmetric companion sets, retains that provenance,
  and proves an unavailable companion cannot collapse the selected-target or
  aggregate scope outcome.
- `full` tests preserve per-member failures instead of replacing them with stubs.
- Corpus measurements record pinned inputs, commands, caps, timing, compiler
  context, and unsupported buckets.
- The IL round-trip suite remains an independent IL-oriented oracle and may
  consume the shared compile/result capability without reconstructing it.

Issue #4810 adds these named gates:

1. `CompileReferenceSelectionDoesNotUseSimpleNameFirstWins` supplies two
   non-corresponding candidates with one simple name and proves that reversing
   discovery order produces the same typed ambiguity.
2. `CompileReferenceSelectionRejectsSameIdentityDifferentContent` proves equal
   assembly identity with a different digest or MVID is not coalesced.
3. `CompileReferenceSetBindsMetadataAndCompilerToSameSnapshot` replaces a
   source path after snapshot retention, including a W-to-S-to-W bracketing
   sequence, and proves Metadata and Roslyn still consume the retained bytes.
   Making the retained snapshot unavailable must fail visibly rather than
   reacquire.
4. `CompileClosureIncludesBodyOnlyExternalRequirement` uses a compiled member
   whose signature is self-contained and whose body alone references another
   assembly. With the exact reference present, closure is `Complete` and the
   artifact is admitted; removing only that reference produces `Missing` before
   compilation and admission.
5. `CompileClosureRetainsLocalSignatureAndCatchRequirements` proves locals and
   catch types participate without an instruction operand naming them.
6. `CompileClosureDoesNotTurnIncompleteCensusIntoEmptySuccess` rejects one body
   token or signature and proves `Incomplete`.
7. `CompileClosureDischargesMetadataLocalRequirementsByExactTypeDef` proves a
   same-named external definition cannot satisfy a source-local requirement and
   including the exact local declaration can.
8. `CompileClosureIncludesDeclarationShapeRequirements` uses body-free compiled
   fixtures for base, interface, generic-constraint, explicit-interface, and
   emitted-attribute types. Each exact provider produces `Complete`; removing
   only that provider produces `Missing`.
9. `RebuiltDeclarationBindingRejectsSameFqnRebind` proves each declaration-shape
   role rejects external-to-local rebinding and accepts the corresponding exact
   external definition.
10. `CompileDefinitionIdentityDistinguishesLocalAndExternalSameFqn` creates local
   and external definitions with one namespace-qualified C# name and proves
   they remain distinct.
11. `RebuiltSignatureBindingRejectsSameFqnRebind` reproduces the trivial-body
   false-`Exact` case for product and legacy artifacts and proves local
   rebinding is a typed mismatch against the original external definition.
12. `RebuiltBodyBindingRejectsSameFqnRebind` proves the same product/legacy
    boundary for a type used only by the body.
13. `RebuiltSynthesizedBindingUsesOwnerCorrespondence` uses compiler-produced
    async, iterator, and supported closure/local-function shapes to prove unique
    correspondence can complete the receipt; an absent, forged, unsupported, or
    ambiguous generated shape remains unavailable.
14. `ExactRequiresNonVacuousCompileContextReceipts` removes each closure,
    local-declaration, and rebuilt-binding check from each artifact policy in
    turn and proves the fixture cannot report `Exact`.
15. `CompleteNeighboringArtifactRemainsProductAdmitted` compiles an unambiguous
    neighboring member and proves `Admitted` plus the expected fidelity result.
16. `TargetedAndBatchUseIdenticalCompileContextPlanning` proves equal reference
    digests, closure outcomes, and admission outcomes for the same member.
17. `CompileBackResultRetainsReceiptsAfterOwnerDisposal` proves all reference,
    closure, admission, diagnostic, and binding evidence remains readable after
    disposable owners are gone.

Documentation-only changes validate Markdown. Implementation milestones add the
smallest focused product and harness checks that prove their claims.

## Open decisions

Issue #4810 closes the ownership decision for compile-back closure and admission:
`tools/DecompilerHarness` owns them. Extracting a reusable tools assembly remains
an implementation-layout choice and does not transfer architectural ownership.

1. Which existing `ImplementationDiff` results should be retained directly, and
   which need a round-trip-specific envelope for provenance?
2. Is cluster/all A/B opt-in per consumer or an explicit round-trip report mode?
3. Which declaration shapes are unsupported in `all`, and how should the typed
   incomplete-scope result group them?
4. Which corpus and fixture populations should establish the initial practical
   success baseline?

## Recommendation

Build reusable round-trip compilation and layered IL/C# comparison first. Keep
`cluster` and `all` as peer scopes, keep selected and full body policy
independent, and let existing product diff tools arbitrate only the evidence they
currently own. Strengthen metadata and PE comparison later from measured needs.
Do not let this initial proposal imply binary patch safety or whole-assembly byte
preservation.
