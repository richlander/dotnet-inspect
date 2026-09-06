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
  consumes per-occurrence resolution evidence from
  [`type-forwarding-resolution.md`](type-forwarding-resolution.md) and the
  separate terminal-accessibility result tracked by
  [#5302](https://github.com/richlander/dotnet-inspect/issues/5302).
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
a typed cross-reader correspondence result. Ordinary members use the existing
`MethodCorrespondenceResolver`; normalized `ResearchMemberIdentity` strings are
correspondence input, not a handle resolver by themselves. Compiler-generated
definitions additionally require the #4883 owner result. The direct donor
comparison then applies the existing C# and IL diff contracts. Separate
original-to-cluster and original-to-all correspondence and diff results remain
available as fidelity evidence, but they are not substituted for the direct
scope comparison. A donor difference may reveal context-sensitive binding,
incomplete cluster membership, different synthesized context, or an
artifact-production gap. It is evidence, not automatically a cluster defect. If
either donor or member correspondence is absent, ambiguous, or failed, the
scope comparison is `Unavailable` and retains
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
   | typed MemberBodyProducer seam (existing) |
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
over the product artifact pipeline. The shell producer, printer, and typed
member-body increment already exist. #4881, #4882, #4930, and #4931 add the
missing participant manifest, generated-fragment evidence, product-body
occurrence manifest, and owner-provenanced replacement artifact described under
[Focused owner and boundary](#focused-owner-and-boundary). ReturnToSender is one
consumer, not the general abstraction.

## Ownership boundaries

### Product libraries

- **Metadata** owns metadata facts and declaration identities.
- **CSharp** owns `TypeShellProducer` for metadata-backed typed shell composition
  and `CSharpTypePrinter` for rendering typed requests as C# source.
- **Decompiler** owns `MemberBodyProducer` for selected-member C# body production
  and fidelity grade. Its existing handle-addressed `ProduceBody` API returns
  `MemberBodyProductionResult` with a typed `CSharpBlockBody` and materialized
  projection. Issue #4930 adds the complete binding-occurrence manifest for that
  product body without replacing the body seam; #4882 separately adds
  generated-fragment dependency and destination evidence.
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
- The tools-side planner discovers roots and members and submits their typed
  identities and body policy to the product artifact provider. The provider
  constructs `CSharpTypeShellSpec` and declaration models internally.
- Harnesses own fixtures, comparison policy, assertions, and reporting.

`TypeShellProducer` and `CSharpTypePrinter` already produce and render typed C#
declarations. The existing member-scoped `MemberBodyProducer` seam supplies
typed body increments without composing a parallel declaration. The harness may
select scope, members, and typed body policies, but it must not format
product-policy declarations or reconstruct decompiled bodies itself. The
tools-owned `LegacyArtifactEmitter` is the explicit exception for the separately
labelled legacy policy; its output cannot become product admission evidence.
Compiler feedback may expand a typed request but must not trigger ad hoc source
patches that compensate for missing product behavior.

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

A source-only `CSharpBlockBody` may compile and participate in comparison, but
it cannot produce a complete compile-context receipt or `Exact` under this
contract. A caller-provided dependency list could prove that listed occurrences
are fresh, but not that no source-only dependency was omitted. The original
member's IL census and the rebuilt IL are likewise not evidence for dependencies
introduced only by supplied source. Tools do not parse that source to invent a
closure claim. Receipt-bearing supplied-body support therefore requires a
separate focused producer contract for a complete occurrence manifest.

For the first contract, supplied C# is a `CSharpBlockBody` addressed to one
metadata method definition: an ordinary method, constructor, or individual
property/event accessor. The product artifact pipeline maps that method body into
the containing declaration shape. `CSharpFieldInitializer`, aggregate
`CSharpPropertyBody`/`CSharpEventBody` replacements, and complete member or type
declarations are outside the first contract. Initializers require a later typed
lowering-correspondence design because their effects may span multiple instance
constructors or the type initializer; issue
[#4882](https://github.com/richlander/dotnet-inspect/issues/4882) owns that
prerequisite for Decompiler-produced fragments.

That replacement boundary does not exempt initializer text already emitted by
the existing artifact pipeline from compile closure. Such generated fragments
must contribute typed dependency evidence under the contract below or make the
artifact incomplete.

A scope-pair key uses canonical method ordering and a versioned content digest
over source, async/unsafe modifiers, `SuppressDestructorSyntax`,
constructor-initializer kind, and initializer arguments; object or collection
reference equality is never used as replacement identity.

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
  the producer-owned diff rows only after successful typed member
  correspondence, with no producer failure or identity rows and at least one
  actual change row;
- `Unavailable` retains the endpoint's `Absent` or `Failed` inspection, identity
  failure, or decompilation/diff failure reason.

This target arbiter is **unverified** until
`CSharpRoundTripChangedRejectsFailureRows` runs in Release. The shipping
round-trip envelope currently maps any non-exact diff with complete endpoint
inspections to `Changed`, including a diff whose only rows are failures.

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
- named signature occurrences, per-occurrence resolution outcomes, and required
  terminal-accessibility evidence from `ILInspector.Metadata`;
- `MethodInstructions` and `DecodedInstruction` from the shared instruction
  substrate;
- guarded metadata name, signature, and resolution operations;
- compiler diagnostics and rebuilt PE bytes from the tools compiler adapter;
- C# and IL comparison results from their existing owners.

The adjacent-owner prerequisites are explicit:

- [#4881](https://github.com/richlander/dotnet-inspect/issues/4881) is the
  `ILInspector.CSharp` design for an artifact-digest-bound participant manifest.
  Until that owner-issued manifest exists, tools can compare its expected plan
  only with itself and cannot issue a complete artifact-coverage receipt.
- [#4882](https://github.com/richlander/dotnet-inspect/issues/4882) is the
  `MemberBodyProducer` design for typed dependency and receiving-member evidence
  for generated fragments. Current source-string initializers are not that
  evidence.
- [#4883](https://github.com/richlander/dotnet-inspect/issues/4883) is the
  `ILInspector.ILDiff` design for typed cross-reader correspondence of supported
  compiler-generated definitions. Current per-side ordinal-normalized names do
  not identify counterpart definitions.
- [#4885](https://github.com/richlander/dotnet-inspect/issues/4885) implements
  the bounded single-signature occurrence decode owned by
  [`metadata-signature-decoding.md`](metadata-signature-decoding.md), landed in
  [#5927](https://github.com/richlander/dotnet-inspect/pull/5927). Tools adoption
  remains tracked by [#5890](https://github.com/richlander/dotnet-inspect/issues/5890).
  The legacy `SignatureSpellabilityResult` collapses the result to `CanSpell`
  plus decode status and is not a substitute for that per-occurrence evidence.
- [#5302](https://github.com/richlander/dotnet-inspect/issues/5302) owns terminal
  accessibility independently of resolution. Until its owner-issued result is
  available, tools cannot complete a signature requirement that needs external
  accessibility evidence. Source-local declaration inclusion and nameability
  are tools-owned obligations, not external-accessibility questions.
- [#4916](https://github.com/richlander/dotnet-inspect/issues/4916) implements
  the artifact owner's on-demand digest over retained immutable content,
  landed in [#5968](https://github.com/richlander/dotnet-inspect/pull/5968).
  `ArtifactSetSession.GetContentDigest` and its lease-bound
  `ArtifactContentReference` forwarding operation supply owner-issued results;
  tools consume those results rather than compute replacement content digests.
- [#4930](https://github.com/richlander/dotnet-inspect/issues/4930) is the
  `MemberBodyProducer` design for a complete typed occurrence manifest over each
  receipt-bearing product `CSharpBlockBody`. Original or rebuilt IL cannot prove
  source-only binding occurrences emitted by the product body.
- [#4931](https://github.com/richlander/dotnet-inspect/issues/4931) is the
  `ILInspector.CSharp` design for an owner-issued body-replacement artifact bound
  to the template digest, typed range, preserved policy, and result digest.
  Ordinary replacement text cannot prove owner derivation.

These prerequisites do not transfer their owners' construction, validation,
identity, or failure semantics into tools. The compile-back implementation may
land refusal paths before they do, but it cannot issue the corresponding
positive receipt until it consumes the owner-issued result.

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

Before descriptor construction or selection, discovery requests the
owner-issued retained-content digest from
[#4916](https://github.com/richlander/dotnet-inspect/issues/4916) for the source
artifact and every candidate. If any required digest capability or result is
unavailable, discovery returns `ReferenceDigestUnavailable` and no
`CompileReferenceSet` exists.

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

Every descriptor digest is computed over the same retained bytes later supplied
to Metadata and Roslyn. Tools do not hash a mutable path or independently opened
stream to fill the field.

The selected set records the current source artifact separately, including its
acquisition registration, retained snapshot, digest, and module identity. The
current artifact contributes source-local declaration identities but is not
silently reintroduced as a metadata reference to satisfy its own generated
source.

Selection follows these rules:

1. Repeated occurrences of the same owner-issued registration over the same
   retained bytes and module may coalesce. Distinct registrations remain
   distinct even when their bytes, digest, MVID, and assembly identity match;
   byte equality does not make their candidates correspond or choose between
   them.
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

#### Initial selection policy

The initial tools API accepts exact Metadata assembly identities with a required
version. Neutral culture and an absent public-key token mean neutral and
unsigned, not wildcard requests. An optional owner-issued artifact identity
pins one inventory candidate; a mismatching pin does not weaken identity
matching. Different registrations with equivalent full identities cannot both
enter a selected set, even through separate pins or aliases, because Metadata
resolution must remain unique.

Aliases are sorted and deduplicated; an omitted or empty alias list means
`global`. Repeated selection of one registration coalesces only when compiler
roles agree. This initial policy grants no platform authorization or preference
and performs no version roll-forward.

Inventory IDs are owner-issued artifact identities, not durable or displayed
addresses. Canonical order follows their deterministic generation-local order.
The set key is the artifact generation together with the set digest; its hex
value alone is not a cross-generation identity. The digest binds the separate
source association and ordered selected registrations, content digests, full
assembly identities, MVIDs, and compiler roles.

The caller owns the original session and query lease through discovery,
selection, and scoped `Use` operations. Each operation requires current
authority; a scoped context is not a replacement for that authority. Metadata
uses the selected guarded openers and Roslyn uses the matching retained
immutable images. Source locations remain inert provenance, not reopen paths.

`CompileReferenceSetTests` gates this initial policy through exact-identity and
ambiguity cases, generation-scoped keys, role-sensitive selection and compiler
binding, digest-before-descriptor ordering, source exclusion, stale authority,
and retained-snapshot consumption by both Metadata and Roslyn. These gates do
not establish the later closure, admission, or rebuilt-binding receipts.

#### Tools adoption

[#6005](https://github.com/richlander/dotnet-inspect/issues/6005) tracks the
frozen-reference implementation within the overall decoder-adoption tracker
[#5890](https://github.com/richlander/dotnet-inspect/issues/5890).
The frozen-reference adoption path has two steps:

1. Implement the inventory, selected set, and scoped context API. The immediate
   production host for this test infrastructure is the decompiler harness
   contract suite, which exercises Metadata and Roslyn with the selected images.
2. Migrate ReturnToSender's compiler-closure acquisition to the frozen context
   and retire its simple-name-first-wins reference enumeration on that path.

The user-approved tools-first scope defers CLI/browser production adoption.
The first step does not relabel the legacy ReturnToSender path as conforming,
adopt the signature decoder there, or complete closure and admission. These
steps are prerequisites within #5890's second decoder-adoption step, not a
claim that the entire compile-back architecture takes two slices.

### Compile-context definition identity

C# spelling is not assembly identity. `CompileDefinitionIdentity` is a closed
union:

- `SourceLocalDefinition` identifies an original `TypeDef` included in the
  generated source declaration plan;
- `ExternalDefinition` identifies one requirement's exact selected reference
  descriptor and durable terminal `TypeDef` address;
- `IntrinsicDefinition` identifies a compiler intrinsic for which Metadata
  requires no external definition;
- `CompilerSynthesizedDefinition` identifies a source-local generated
  definition that the artifact intentionally asks the compiler to regenerate;
- `UnresolvedDefinition` retains a typed failure and cannot participate in
  success.

The source-local arm uses original artifact identity plus the owner-issued
durable `MetadataTypeDefinitionAddress`; a raw `TypeDefinitionHandle` exists
only transiently while that address is validated against a live reader. A
rebuilt declaration projects back to that identity only through the typed
declaration plan and rebuilt-member correspondence. Equal namespace-qualified
text is insufficient.

The compiler-synthesized arm retains the original durable type/member address
and Metadata evidence that the definition is generated, plus the emitted member
whose C# artifact can cause regeneration. It is a deferred binding obligation,
not a local declaration receipt. It completes only when an owner-issued typed
cross-reader correspondence for that generated construct uniquely returns the
original and rebuilt definitions. The current
`CompilerGeneratedOrdinalCorrespondence` exposes per-side normalized names, not
counterpart definition handles, and does not satisfy this obligation.
[#4883](https://github.com/richlander/dotnet-inspect/issues/4883) owns the
missing result. Different construct kinds may consume different owner-issued
correspondence results; tools select by typed construct evidence, not generated
name text. Tools accept or refuse correspondence exactly within the issuing
owner's documented threat boundary and make no stronger provenance-
authentication claim. Until #4883 supplies the capability, a candidate carrying
one of these obligations is declined before `ProductAttemptCommit`. Once the
capability exists, an absent, unsupported, or ambiguous per-attempt result makes
the compile-context receipt unavailable and the attempted product result
`Failed`.

The external arm is requirement identity local to the frozen compile context;
it is not a cross-reader correspondence claim. While the catalog generation is
live, transient binding evaluation retains the original and rebuilt opaque
`ResolvedTypeDefinitionKey` values and asks the Metadata-owned comparison API
for `DefinitionCorrespondence`. Only `Same` discharges the binding obligation.
`Different`, `IndeterminateDuplicateArtifact`, `IncomparableCatalogs`, and
`StaleGeneration` remain visible non-success outcomes; tools never reconstruct
correspondence from descriptor IDs, candidates, MVIDs, tokens, or paths.

Before releasing the catalog, tools materialize the owner-issued correspondence
outcome, both durable `MetadataTypeDefinitionAddress` values, selected
descriptor, and exact artifact/reference digests into the receipt. The durable
addresses permit later re-location but do not themselves establish
cross-artifact correspondence.

A namespace-qualified C# name remains a spelling and diagnostic key. If one
spelling denotes both a source-local definition and a non-corresponding external
definition, both identities remain present. Without an owner-issued binding
proof, admission returns `DefinitionBindingAmbiguous`; it never chooses by full
name.

### Closure requirements

`CompileClosurePlan` is the immutable union of signature, declaration-shape,
local-declaration, generated-fragment, and body requirements for the selected
artifact. It covers every declaration, generated fragment, and real body emitted
by the effective body policy, including selected targets, required companions,
and `full`-policy members. Each requirement records:

- its `CompileDefinitionIdentity` or unresolved state;
- owner-issued durable type/member address;
- role and occurrence provenance;
- whether generated source, one selected reference, or an intrinsic must
  provide it;
- the exact selected descriptor for an external requirement.

Duplicates retain every occurrence. They may share one resolved definition but
cannot erase a stronger requirement or a failure.

Raw metadata handles may appear only in a transient candidate plan while its
reader is live. Every outcome or receipt that escapes the owner scope
materializes the corresponding durable address and validates it again before
re-resolution.

The tools-owned effective artifact plan also produces an immutable
`ArtifactParticipantPlan`. It assigns stable expected participant IDs to every
requested declaration, effective real body, and generated fragment. Each
planned census result is keyed by one expected participant ID.

Expected-plan coverage is not self-proving. After rendering, tools consume a
policy-specific, artifact-digest-bound participant manifest:

- the product-whole-member policy consumes the CSharp producer manifest from
  [#4881](https://github.com/richlander/dotnet-inspect/issues/4881);
- the legacy policy consumes a manifest issued by the tools-owned
  `LegacyArtifactEmitter` as it renders declarations, bodies, and fragments.
  The planner cannot fill this manifest, and legacy source construction cannot
  bypass the emitter's participant-bearing operations with direct
  `StringBuilder` writes.

`CompileArtifactCoverageReceipt` requires exact equality between the expected
plan and the selected policy's rendered-participant set, and then requires one
census for every matched participant. Missing, duplicate, stale, or unexpected
manifest entries or censuses retain their IDs and cannot produce a receipt. A
primary-only scan cannot cover an artifact that also renders companions or
`full`-policy members. Before a policy's manifest capability exists, its absence
is a pre-commit policy refusal; after that policy's attempt commit, a missing or
mismatched manifest is an artifact-production failure.

Signature requirements compose Metadata-owned named occurrences from
[#4885](https://github.com/richlander/dotnet-inspect/issues/4885) with
per-occurrence resolution outcomes under the frozen compile context. Tools
retain an explicit missing-decode capability reason and decline before
`ProductAttemptCommit` until that occurrence surface exists. The current
`CanSpell` boolean supplies neither the missing identities nor typed failure
evidence.

For each occurrence resolved to the source candidate that is not covered by a
`CompilerSynthesizedDefinition` obligation, tools check that the exact source
`TypeDef`, including its containing declaration chain, is present and nameable
in the artifact's declaration plan. A same-named external definition cannot
satisfy this obligation. The tools-owned
`LocalDeclarationReceipt` records that obligation's discharge; resolution alone
does not prove it. A source-local definition need not be externally accessible,
but it must be nameable from the occurrence's generated context. An undischarged
obligation prevents complete closure and, if declaration planning cannot
resolve it, produces pre-commit `Declined`. Rendered inclusion remains subject
to the existing artifact-manifest coverage and rebuilt-binding requirements;
the planner's receipt is not evidence that rendering occurred.

For a resolved external occurrence participating in the emitted signature,
tools consume the terminal-accessibility evidence tracked by
[#5302](https://github.com/richlander/dotnet-inspect/issues/5302). An accessible
terminal definition becomes an exact selected-reference requirement.
Metadata's authoritative `Inaccessible` outcome becomes `Unspellable` with the
original terminal-definition and accessibility evidence; it cannot become an
exact reference requirement. Unresolved or rejected decode, resolution, or
accessibility outcomes retain their exact Metadata failure as `Incomplete`.
Missing required accessibility capability likewise prevents complete closure
and produces pre-commit `Declined` with its own capability reason, not
`Unspellable`. Occurrences not spelled by the artifact still retain their
resolution outcomes; non-participation cannot erase a resolution failure.

Tools compose these results into closure and artifact admission. Metadata does
not authorize the artifact through a compatibility `CanSpell` projection or a
local proof object. This consumer contract remains design-only and unverified
until its named gates below are implemented; it neither recreates the removed
aggregate protocol nor defines the adjacent Metadata operations.

### Declaration-reference census

Member signatures are not the only declaration syntax that binds types. Tools
create a conservative `DeclarationReferenceCensus` for every source `TypeDef`
and member included by the tools-owned artifact plan. The closed metadata
surfaces that can feed declaration rendering include:

- type base clauses and implemented-interface clauses;
- type and method generic-parameter constraints;
- `MethodImpl` declarations and explicit-interface qualifiers;
- metadata-backed attributes on included declarations, including constructor
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

Declaration occurrences become local, external, intrinsic,
compiler-synthesized, or unresolved closure requirements. The rebuilt binding
receipt repeats the census against corresponding donor declarations. A base,
interface, constraint, explicit-interface, or attribute type cannot rebind
between source-local and external definitions merely because both use the same
C# full name.

### Generated-fragment reference census

Some emitted C# expressions are neither declaration metadata nor real method
bodies. Tools create a `GeneratedFragmentReferenceCensus` for each such
participant, including:

- reconstructed field and property initializers;
- constructor- or primary-constructor initializer arguments supplied outside a
  `CSharpMemberBody`;
- later non-body expression fragments explicitly included by the artifact plan.

Each Decompiler-produced fragment consumes the owner-issued typed dependency and
complete lowering-destination set from
[#4882](https://github.com/richlander/dotnet-inspect/issues/4882). The dependency
evidence is tied to the policy-specific owner occurrence: #4881's CSharp
participant manifest for a product artifact, or the participant-bearing
fragment emission from `LegacyArtifactEmitter` for a legacy artifact. Source
text alone is not dependency or occurrence evidence, and tools do not parse or
repair the fragment to reconstruct either. Current `CSharpFieldInitializer` and
constructor-initializer strings therefore make the census `Incomplete`; the
initial implementation may omit that fragment through its existing typed
artifact policy or decline the artifact, but cannot admit it with an empty
requirement set.

Any fragment introduced by another product owner likewise needs that owner's
typed dependency and receiving-member result. Until one exists, the fragment is
`Incomplete`; #4882 does not claim other owners' fragment semantics.

Resolved fragment occurrences become the same local, external, intrinsic,
compiler-synthesized, or unresolved requirements as declaration and body
occurrences. Their provenance retains the participant ID, source member,
owner-issued occurrence identity, and complete destination set. A singular
receiver cannot stand in for an initializer that may lower into multiple
instance constructors or a type initializer.

### Conservative body-reference census

Signature closure is insufficient because generated C# may use types found only
in a method body. Tools create one closed, conservative
`BodyReferenceCensus` for every original member whose real body is emitted.
Each census comes from:

- the complete Decompiler-issued product-body occurrence manifest from
  [#4930](https://github.com/richlander/dotnet-inspect/issues/4930);
- the complete shared `MethodInstructions` decode;
- every metadata-token-bearing IL operand;
- the method local signature;
- exception-region catch types;
- referenced member, method-specification, type-specification, and standalone
  signatures needed to expose their named-type requirements.

The census composes the owner-issued source occurrences with guarded Metadata
name/signature operations over the original IL under the same resolution
context as signature spellability. It does not interpret control flow,
evaluation-stack meaning, reachability, or product C# text. Until #4930 exists,
every receipt-bearing product body makes the census `Incomplete`; an empty
source-occurrence set inferred from IL is not complete. The IL side may retain
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

### Supplied-body comparison boundary

A supplied replacement body does not inherit the original member's
`BodyReferenceCensus` and cannot contribute a receipt under this design. Its
artifact may still produce compilation, C#, and IL comparison observations, but
`CompileContextOutcome` is
`Unavailable(SuppliedBodyOccurrenceCompletenessUnverified)` and the verdict
classifier returns `FidelityUnavailable`. This comparison-only path does not
project as `ProductWholeMemberAdmission.Admitted`.

Any request with a non-empty `Replacements` list is a comparison-only request.
All supplied replacements in that request still compile into one donor, but
they do not share an artifact with receipt-bearing decompiler-produced bodies.
A caller that needs both runs a separate product/decompiler request so the
supplied source cannot make an otherwise provable member's artifact receipt
unavailable.

After #4931 lands, an authored-source control consumes the owner-issued
replacement artifact derived from the corresponding decompiler-produced
`CSharpSourceArtifact`. The result proves the template digest, typed replacement
range, preserved rendering policy and non-target bytes, and distinct result
digest. It does not inherit the template artifact's closure, participant
coverage, admission, or receipt evidence. Until #4931 exists, or when its result
is missing, stale, mismatched, or reused with any receipt, the control is
unavailable and cannot make causal attribution.

No caller-provided completeness flag or occurrence list can upgrade that result.
Source parsing, name lookup, and emitted-IL absence cannot repair the missing
evidence; a source-only dependency such as a compile-time name expression may
intentionally leave no rebuilt IL operand. A future receipt-bearing capability
must be a separately owned complete-occurrence producer contract, not another
field on `RoundTripMethodReplacement`.

### Closure outcome

Evaluating the candidate plan against the frozen reference set, before product
artifact production, produces one closed `CompileClosureOutcome`:

- `Complete` maps every requirement to an intrinsic,
  `LocalDeclarationReceipt`, exact `CompileReferenceDescriptor`, or retained
  compiler-synthesized binding obligation;
- `Unspellable` retains authoritative Metadata `Inaccessible` outcomes;
- `Missing` retains every requirement for which no provider exists;
- `Ambiguous` retains every requirement with multiple non-corresponding
  providers;
- `Incomplete` retains decode, resolution, rejected accessibility, safety-bound,
  unsupported-scope, and missing adjacent-owner evidence.

`Complete` means the candidate is statically ready to cross
`ProductAttemptCommit` under its mapped providers. It is not rendered-artifact
coverage or final binding success: the producer manifest must still match the
expected participant plan, and every retained compiler-synthesized obligation
must still be discharged before `CompileContextOutcome.Complete`.

Closure is artifact-scoped because Roslyn compiles one artifact. A
`MemberClosureProjection` separately retains the requirements and local
coverage contributed by each target, companion, and generated-fragment
participant, plus every artifact-wide blocker and its originating participant.
The projection never converts an artifact `Unspellable`, `Missing`,
`Ambiguous`, or `Incomplete` outcome into member success.

Targeted and batch runs may therefore have different artifact closure and
admission outcomes when batch includes another broken participant. For the same
member and artifact policy, they must use the same reference-set digest,
definition identities, and member-local requirement projection. Any
artifact-level difference remains visible with the causative participant
rather than being called a parity failure or silently attributed to the target.

The evaluator does not add references after seeing compiler diagnostics and
does not retry with a different set. A missing body-only dependency therefore
becomes `Missing` before product-whole-member admission and compilation. A
neighboring complete artifact remains eligible under the same policy.

### Planning convergence and attempt commit

Reference selection happens once before declaration-cluster iteration and never
grows from compiler diagnostics. Declaration membership may still converge
through the existing bounded typed-root process.

Each iteration follows one ordered transition:

1. Build an immutable typed candidate plan without treating it as product
   evidence.
2. Recompute signature requirements, declaration-reference censuses, local
   receipts, generated-fragment censuses, and every effective real-body census
   against the frozen reference set.
3. Add any supported source-local declaration roots and repeat planning. A
   final unspellable, missing, ambiguous, or incomplete provider is a policy
   refusal and produces `Declined`; it cannot add a reference from a compiler
   search path.
4. Check the remaining pre-attempt owner capabilities required by this exact
   plan: the artifact manifest and generated-definition correspondence when the
   plan carries those obligations. If one is unavailable, produce pre-commit
   `Declined` with that exact capability reason; this is distinct from static
   closure `Incomplete`. Otherwise, after static closure is `Complete`, cross
   `ProductAttemptCommit` for that exact plan and invoke product artifact
   production.
5. Bind the returned participant manifest to the exact source-artifact digest
   and compare it with `ArtifactParticipantPlan`. A missing or mismatched
   manifest produces `Failed`. Exact coverage issues
   `CompileArtifactCoverageReceipt`; only then may the compiler run.
6. A supported compiler diagnostic may contribute one typed declaration root.
   When root and iteration budgets permit growth, that explicitly supersedes
   the in-flight attempt without producing a terminal admission arm. Discard
   its artifact, coverage, closure, compilation, and binding evidence, then
   begin a replacement iteration.
7. A terminal product, coverage, compiler, rebuilt-resolution, or binding
   failure after `ProductAttemptCommit` produces `Failed`. A stalled diagnostic,
   root-budget exhaustion, or iteration-budget exhaustion is terminal at this
   boundary, retains its exact bail reason, and cannot select legacy replacement
   evidence.
8. A successful compile plus complete rebuilt binding produces `Admitted`.

`ProductAttemptCommit` is the only boundary that permits product evidence to
become authoritative. `Provisional` describes in-flight planning state only; it
is not a closed result arm and never appears in persisted output. Only the
converged attempt can contribute durable receipts or fidelity outcomes.

#### Admission interaction model

[`CompileBackAdmission.tla`](../models/compile-back-admission/CompileBackAdmission.tla),
checked with
[`CompileBackAdmission.cfg`](../models/compile-back-admission/CompileBackAdmission.cfg),
models the
tools-owned planning, product-attempt, legacy-attempt, supersession, receipt,
and verdict interaction. It deliberately abstracts reference/closure internals
as nondeterministic transition choices; their typed meanings remain in this
document.

The model assumes one sequential planner, a finite product iteration budget,
one product policy and one independently attempted legacy policy, and no process
crash between a transition and its durable result. The checked configuration
uses `MaxIterations = 2`; increasing the bound adds equivalent expansion states
without changing the transition shape.

TLC checks:

- every fair execution terminates;
- `Exact` has the current product-attempt receipt or an independently admitted
  legacy receipt;
- admitted product and legacy attempts may still report an unavailable
  comparison verdict;
- product `Failed` never transitions through legacy evidence;
- a supplied-body comparison never reports `Exact`;
- supersession clears attempt-bound coverage receipts and partial evidence; and
- receipts exist only for the matching admitted policy.

The model result is evidence about this interaction contract, not the
implementation. The named Release gates below remain the implementation proof.

TLC `2026.08.21.155922` from pinned `tla2tools.jar` v1.8.0 (rev `9787e65`)
under OpenJDK 25.0.4.1 checked the configuration with no errors: 85 states
generated, 50 distinct states, and a maximum depth of 10. Action coverage reached
`ProductCoverageReceipt`, `ProductAdmitUnavailable`, and
`LegacyAdmitUnavailable` three times each and `ProductExpand` twice. A mutation
that preserves the earlier coverage receipt across `ProductExpand` violates
`CoverageReceiptMatchesAttempt`.

The first model pass exposed that the terminal `Done` state needed an explicit
stutter action for TLC's deadlock check. A later review rejected the 38-state
model because it created product receipts only at terminal admission, making
receipt invalidation on supersession vacuous. The current model adds the
non-terminal attempt-bound coverage receipt and the load-bearing mutation above.
No safety or liveness counterexample remains.

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
- each generated-fragment dependency with the compiler binding at its
  owner-issued source occurrence and every corresponding rebuilt destination;
- each rebuilt local definition projected through the typed declaration plan;
- each rebuilt external definition through Metadata's live
  `DefinitionCorrespondence`, retaining the supplying descriptor and durable
  addresses only after that comparison;
- each deferred compiler-synthesized definition through the applicable
  owner-issued cross-reader correspondence.

Generated-fragment evidence names the complete original lowering-destination
set. Every destination must be an effective real body in the artifact, and
every rebuilt counterpart must be exact; an omitted, extra, stubbed, ambiguous,
or non-corresponding destination returns `BindingReceiptUnavailable`. While the
compiler workspace is alive, tools resolve each owner-issued source occurrence
and compare its bound definition with the planned identity. This is binding of
a typed coordinate, not tools-side parsing or dependency discovery. An external
fragment occurrence that binds to a same-FQN source-local declaration is
therefore a mismatch even when one receiving method later compares equal.

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
to its frozen reference set, converged closure, producer-issued artifact
coverage, compilation, correspondence, and rebuilt binding evidence.
`Unavailable` retains the exact failed or incomplete stage and cannot expose a
receipt.

For the product policy, producer-issued coverage means the #4881 CSharp
manifest. For the legacy policy, it means the manifest created by
`LegacyArtifactEmitter` during source emission, not an inventory copied from the
tools planner. The two manifests share the receipt shape but not an issuer.

The legacy policy applies the same frozen-reference, static-closure,
capability-check, rendering, coverage, compilation, and rebuilt-binding order
under a distinct `LegacyAttemptCommit`. Its admission does not project as
`ProductWholeMemberAdmission`, and its failure cannot replace or relabel a
failed product attempt. A product `Declined` result may select this policy only
before `ProductAttemptCommit`; the legacy result then succeeds or fails on its
own evidence.

Any `CompileBackStatus.Exact` requires a complete receipt for the exact artifact
whose fidelity is being reported. This rule is independent of artifact policy.
A local/external same-FQN rebind therefore cannot become `Exact` by declining
the product artifact and routing through the legacy shell. A legacy artifact
may retain `Exact` only when its own context receipt is complete, and its report
must identify the legacy policy.

One tools-owned receipt-bearing classifier is the only authority that can
construct an `Exact` compile-back verdict. Primary targets, sibling accessors,
effective companions, nested ReturnToSender rows, and future verdict producers
all call it; an enum assignment or opcode-only helper is not authoritative.

Result contracts carry a `CompileBackVerdict`, not a caller-supplied raw status.
Its `Exact` constructor is private to the classifier; `CompileBackStatus` remains
a read-only serialization/reporting projection. Adding a producer therefore
cannot create `Exact` without providing the classifier's artifact receipt and
member entry.

Each verdict retains the exact artifact and compile-context receipt IDs plus its
member-binding entry. A companion may share the artifact-level receipt only
when it was compiled in that exact artifact/context and its own member entry is
complete. A missing or mismatched receipt produces `FidelityUnavailable`, even
when opcode and C# comparers independently return equality.

### Product-whole-member admission

`ProductWholeMemberAdmission` is the terminal projection of the ordered
planning transition:

- `Declined` is the pre-`ProductAttemptCommit` policy refusal from reference
  discovery or selection, declaration planning, closure, tools-owned local
  declaration obligations, signature-decode or required terminal-accessibility
  capability, retained-content digest capability, artifact-manifest capability,
  product-body occurrence capability, or required generated-correspondence
  capability. It carries the typed reasons and selected legacy policy, when
  permitted.
- `Failed` when artifact production, compilation, rebuilt resolution, or
  binding fails after `ProductAttemptCommit`, including participant-manifest
  mismatch, a stalled post-commit diagnostic, and root/iteration budget
  exhaustion. It retains every product artifact, bail reason, and partial
  receipt that exists.
- `Admitted` only after the exact artifact and typed declaration plan exist, the
  frozen reference set is unambiguous, signature/declaration/generated-fragment/
  body closure is `Complete`, every tools-owned source-local declaration
  obligation has a `LocalDeclarationReceipt`, artifact coverage exactly matches
  the producer manifest, and the exact artifact-specific `CompileContextReceipt`
  is complete. It carries artifact, compile-context digest, closure, coverage,
  compilation, and rebuilt-binding receipts.

The current `UsedProductWholeMember` boolean cannot represent these states. It
may remain as a compatibility projection only if it means `Admission is
Admitted`; declined and failed artifacts project false and retain their richer
outcome.

A `Declined` result may select the independently defined legacy artifact policy,
with the reason visible. A `Failed` result remains a failure. A separately
labelled legacy control cannot replace it or be reported as the product result.
An expandable planning diagnostic produces neither arm and cannot select a
legacy result while its replacement iteration remains viable.

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
2. tools build the current explicit member's typed candidate plan without
   treating it as product evidence;
3. its declaration signatures, declaration-shape references, local
   requirements, generated fragments, and every effective real body contribute
   to the artifact-specific closure plan;
4. a policy refusal before `ProductAttemptCommit` selects the existing legacy
   policy with visible `Declined` reasons;
5. after the commit, product rendering, participant-manifest validation, and
   compiler probes run; a supported declaration-root diagnostic supersedes that
   iteration without producing an admission arm;
6. a terminal attempted-product failure remains `Failed`;
7. only `Admitted` product evidence or a separately complete legacy context may
   contribute a receipt-bearing fidelity verdict.

This gating does not admit another explicit-member shape and does not change
product spelling. Both public harness modes call the same planner and evaluator;
neither may retain the current simple-name reference map or an independent
signature-only shortcut.

## Failure model

| Layer | Example outcomes |
| --- | --- |
| Selection | target missing, ambiguous overload, unsupported member kind |
| Artifact production | unsupported declaration, partial body, missing manifest, participant mismatch |
| Reference selection | exact set, missing identity, owner digest unavailable, ambiguous candidates, changed bytes |
| Closure | complete, unspellable, missing requirement, ambiguous provider, incomplete census |
| Post-commit convergence | expandable diagnostic, stalled closure, root budget, iteration budget |
| Compilation | parse failure, bind failure, emit failure |
| Correspondence | rebuilt member missing, ambiguous, wrong module, unsupported generated construct |
| Binding | corresponding, rebound definition or fragment occurrence, incomplete receipt |
| C# diff | exact, changed, unavailable |
| IL diff | exact, operand diff, opcode diff, unavailable |
| Scope A/B | same, different, unavailable |

No layer converts failure or unavailability into an empty successful result.

## Proposed milestones

### Adjacent prerequisites

- [#4881](https://github.com/richlander/dotnet-inspect/issues/4881) defines the
  CSharp artifact participant manifest.
- [#4882](https://github.com/richlander/dotnet-inspect/issues/4882) defines
  typed generated-fragment dependency and receiver evidence.
- [#4883](https://github.com/richlander/dotnet-inspect/issues/4883) defines
  compiler-generated cross-reader definition correspondence.
- [#4885](https://github.com/richlander/dotnet-inspect/issues/4885) implements
  the Metadata bounded single-signature occurrence decode.
- [#5302](https://github.com/richlander/dotnet-inspect/issues/5302) defines the
  separate Metadata terminal-accessibility result for external requirements.
- [#4916](https://github.com/richlander/dotnet-inspect/issues/4916) implements
  owner-mediated retained-content digests.
- [#4930](https://github.com/richlander/dotnet-inspect/issues/4930) defines
  complete Decompiler-issued binding occurrences for receipt-bearing product
  bodies.
- [#4931](https://github.com/richlander/dotnet-inspect/issues/4931) defines an
  owner-issued CSharp replacement artifact with template/result provenance.

Milestone 1 may add explicit `Declined`/unavailable arms before these issues
land. Its positive artifact-coverage, generated-fragment, and
compiler-synthesized receipts and differentiated signature outcomes remain
blocked on their respective owner results. Every receipt-bearing real-body
positive path is blocked on #4930. All digest-bound positive paths are blocked
on #4916, and causal authored-control attribution is blocked on #4931.

### Milestone 1: extract round-trip compilation

- Define tools-only request and layered result contracts.
- Replace simple-name/first-wins reference selection with the frozen exact
  inventory, canonical selected set, and typed ambiguity outcomes.
- Add artifact-specific signature, declaration-shape, local-declaration,
  generated-fragment, and effective-body closure plus participant coverage,
  deferred compiler-generated correspondence, and the rebuilt binding and
  compile-context receipts required by every `Exact`.
- Centralize primary, companion, accessor, and nested fidelity verdicts through
  the receipt-bearing classifier.
- Model diagnostic-driven cluster growth through the ordered
  `ProductAttemptCommit` transition and discard every superseded attempt.
- Add typed product-whole-member declined, failed, and admitted outcomes without
  allowing an in-flight planning state or legacy artifact to borrow product
  receipts.
- Consume the existing product-owned, handle-addressed
  `MemberBodyProducer.ProduceBody` result and preserve its typed body, projection,
  and failure provenance.
- Wire the product artifact pipeline explicitly: the tools planner supplies
  artifact/member identities, roots, and body policy; the product provider
  constructs `CSharpTypeShellSpec` and typed print requests; the existing
  member-body seam supplies decompiled body increments; and `CSharpTypePrinter`
  renders source.
- Remove product-policy tools rewrites, including
  `TryForcePublicConstructorAccessibility` and `EmitPrerenderedMember`
  re-indentation. Product accessibility and source formatting remain
  owner-issued.
- Refactor legacy source construction through `LegacyArtifactEmitter` so every
  declaration, body, and fragment emission contributes to its digest-bound
  participant manifest.
- Extract compiler-driven root growth from decompiler-specific comparison.
- Adapt ReturnToSender to consume the engine, preserving safe verdicts while
  reclassifying outcomes that lack complete compile-context evidence.
- Preserve current cluster budgets, provenance, and failure buckets.
- Add focused seam tests for exact and wrong-reader handles, body absence,
  decompilation failure, fidelity provenance, accessor methods, constructors and
  constructor initializers, and parity with current ReturnToSender output.

### Milestone 2: general selected-member comparison

- Support supplied replacement bodies independently of decompiler-produced
  bodies as comparison-only evidence. They cannot issue a compile-context
  receipt or `Exact` under this design.
- Reject field-initializer and aggregate property/event replacement shapes in the
  first method-addressed contract; test each rejection explicitly.
- Consume the existing `MethodCorrespondenceResolver` for ordinary structural
  member correspondence. Issue #4883 remains the separate prerequisite for
  compiler-generated definitions whose normalized ordinals do not preserve
  strict names.
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
- Replacement-identity fixtures change source, async/unsafe modifiers,
  `SuppressDestructorSyntax`, constructor-initializer kind, and initializer
  arguments one at a time.
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

1. `CompileReferenceSelectionDoesNotUseSimpleNameFirstWins` supplies
   non-corresponding candidates with one simple name and proves that exact
   identity selection is independent of discovery order. Requests admitting
   multiple candidates produce the same typed ambiguity in either order.
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
7. `CompileClosureDischargesLocalDeclarationsByExactTypeDef` uses a source-local
   nested type. An omitted local declaration, missing containing declaration,
   or declaration not nameable from the generated context prevents complete
   closure and produces pre-commit `Declined` when planning cannot resolve it.
   A same-named external definition cannot satisfy the obligation. Including the
   exact source declaration and its containing chain in a nameable context
   discharges the tools-owned receipt, including for a source-local type that
   is not externally accessible. Resolution alone cannot issue the receipt;
   rendered coverage and rebuilt binding remain separate gates.
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
    async, iterator, and supported closure/local-function shapes. Until #4883
    lands, every candidate carrying a generated-definition obligation is
    pre-commit `Declined`. After the prerequisite lands, its unique result can
    complete the receipt while an absent, unsupported, or ambiguous
    per-attempt owner result produces post-commit `Failed`. The gate makes no
    provenance authentication claim beyond the issuing correspondence
    contract.
14. `ExactRequiresNonVacuousCompileContextReceipts` uses the same product and
    legacy fixtures for both arms. With complete closure, artifact-coverage,
    local-declaration, and rebuilt-binding receipts each fixture reports
    `Exact`; withholding or mismatching each required component in turn changes
    that result away from `Exact`. Deleting the corresponding gating condition
    is mutation-verified to make its negative arm fail by incorrectly reporting
    `Exact`.
15. `ClusterConvergenceDiscardsSupersededReceipts` uses a compiled fixture whose
    first iteration contributes a typed declaration root and whose replacement
    plan converges. Its first-iteration artifact, coverage, closure, and
    failed-compilation evidence is discarded; only receipts carrying the
    converged artifact digest may survive. A typed seam separately injects a
    binding receipt carrying the earlier digest. Reusing any earlier evidence
    produces a context mismatch and cannot report `Exact`; the converged
    replacement reaches `Admitted`.
    Bypassing invalidation or terminating on the expandable diagnostic is
    mutation-verified to fail the gate.
16. `CompleteNeighboringArtifactRemainsProductAdmitted` compiles an unambiguous
    neighboring member and proves `Admitted` plus the expected fidelity result.
17. `TargetedAndBatchRetainMemberLocalPlanningParity` proves equal reference
    digests, definition identities, and member-local requirement projections
    for the same member. A one-member batch and targeted artifact produce equal
    closure/admission outcomes. Adding a batch-only sibling with a missing
    dependency makes only the batch artifact unavailable and retains that
    sibling as the cause; the target's local projection remains equal and is
    never promoted over the artifact failure.
18. `CompileBackResultRetainsReceiptsAfterOwnerDisposal` proves all reference,
    closure, artifact-coverage, admission, diagnostic, and binding evidence
    remains readable after disposable owners are gone. Every retained
    type/member location is a durable address; reopening the exact module
    revalidates MVID, token table, and row bounds before producing a transient
    handle, while a wrong-MVID or out-of-range address fails visibly.
19. `CompileReferenceSelectionPreservesDistinctIdenticalRegistrations` creates
    two fresh owner-issued registrations over identical bytes, digest, MVID, and
    assembly identity. Repeated occurrences of either registration may
    coalesce, but the two registrations remain distinct and ambiguous when no
    policy selects one.
20. `EveryExactProducerRequiresMatchingContextReceipt` covers the primary
    target, a sibling accessor, an effective companion, and a nested
    ReturnToSender row. Each reports `Exact` with its matching artifact receipt
    and complete member entry; withholding or mismatching either changes only
    that verdict to `FidelityUnavailable`. Removing the central classifier call
    from any producer is mutation-verified to fail its negative arm.
    `CompileBackExactConstructionIsCentralized` additionally derives the
    allowed result constructors and exact-producing factory from the result
    declaration; an accessible raw-status constructor or another `Exact`
    factory fails the architecture gate.
21. `ProductAdmissionSeparatesDeclineAndFailure` proves both terminal arms and
    their fallback boundary. A pre-commit reference or closure refusal produces
    `Declined` and may select the labelled legacy policy. A terminal product or
    participant-manifest, compiler, stalled diagnostic, root/iteration budget,
    rebuilt-resolution, correspondence, or binding failure after
    `ProductAttemptCommit` produces `Failed`, retains available product evidence
    and exact bail provenance, and cannot be replaced by the legacy control.
22. `CompileArtifactCoverageMatchesEffectiveParticipantPlan` derives the expected
    census keys from an artifact containing a primary target, a companion, and
    multiple `full`-policy members with distinct body-only and same-FQN
    dependencies. Before #4881, missing manifest capability produces
    pre-commit `Declined`. With the owner-issued manifest, exact
    digest-bound coverage can complete; removing any declaration, body, or
    fragment census, injecting a stale manifest participant, or returning a
    primary-only manifest produces post-commit `Failed`. For a product artifact,
    a tools-owned observed set substituted for the CSharp producer manifest
    fails the architecture arm.
23. `CompileClosureRequiresGeneratedFragmentEvidence` uses a reconstructed
    initializer. Until #4882 and #4881 land, retaining only initializer source
    text produces `Incomplete`. With the owner-issued typed external requirement
    and occurrence coordinate, the exact reference produces `Complete`, removing
    it produces `Missing`, and binding the occurrence to a same-FQN local
    definition produces `BindingReceiptUnavailable`. A fixture whose initializer
    lowers into multiple constructors requires the complete destination set;
    omitting or stubbing any receiver is also unavailable. No arm parses the
    generated expression.
24. `CompileClosureComposesMetadataSignatureEvidence` uses forwarded signatures.
    Missing #4885 decode capability or required #5302 accessibility capability
    produces pre-commit `Declined` with the specific missing-capability reason;
    neither `CanSpell` nor a missing result can substitute for the evidence.
    With both owner results, an accessible external terminal contributes the
    exact selected-reference requirement. An inaccessible terminal retains
    Metadata's definition and accessibility evidence as `Unspellable`, produces
    pre-commit `Declined`, and cannot become `Complete` through direct-name
    lookup or permissive compiler binding. Unresolved or rejected occurrences
    remain `Incomplete` with the exact Metadata reason, including a
    non-participating occurrence whose resolution fails.
25. `SuppliedBodyCannotIssueReceiptWithoutCompleteOccurrenceOwner` uses a
    replacement body with a source-only same-FQN dependency that is absent from
    emitted IL. Its isolated comparison-only artifact compiles and both C#/IL
    comparers report equality, yet the context remains
    `Unavailable(SuppliedBodyOccurrenceCompletenessUnverified)` and the verdict
    is `FidelityUnavailable`. No arm parses the supplied body or mixes it into a
    receipt-bearing donor.
26. `LegacyArtifactCoverageIsEmitterIssued` renders a legacy artifact with a
    primary target, sibling stub, declaration shape, and generated fragment.
    The emitter manifest exactly covers them and can support gate 14's legacy
    positive arm. Removing a participant-bearing emission, adding a raw source
    append outside `LegacyArtifactEmitter`, copying the planner's expected set,
    or reusing a manifest under another artifact digest fails the gate.
27. `ExternalDefinitionBindingUsesMetadataCorrespondence` resolves original and
    rebuilt same-FQN definitions under one live catalog and accepts only the
    owner-issued `DefinitionCorrespondence.Same` arm. Different definitions,
    duplicate-indeterminate candidates, incomparable catalogs, and stale
    generations remain unavailable even when descriptor fields, MVIDs, or
    tokens are copied. Retained durable addresses can re-locate definitions
    after disposal but cannot make this gate pass without the captured owner
    outcome.
28. `CompileReferenceDigestComesFromRetainedArtifactOwner` declines before
    descriptor construction or selection if any required owner digest is
    unavailable. With every required owner result, descriptor construction may
    begin and each digest matches the retained bytes opened under the same query
    lease. Hashing a mutable path, hashing an independently reopened stream, or
    supplying a consumer-computed digest fails the architecture arm; replacing
    the source after retention does not change the owner digest. Moving
    descriptor construction before owner digest acquisition is mutation-verified
    to fail the ordering arm.
29. `ScopePairDigestIncludesEveryBodyRenderingField` derives the expected
    replacement-identity fields from the `CSharpMemberBody`/`CSharpBlockBody`
    contract. Toggling source, async/unsafe modifiers,
    `SuppressDestructorSyntax`, constructor-initializer kind, or one initializer
    argument changes the pair key and produces typed `Unavailable`. Adding a new
    body rendering field without adding its digest projection fails the
    architecture arm.
30. `CompileBackPlanningOwnershipMatchesComponentBoundary` derives the tools
    project that declares reference selection, same-assembly root selection,
    closure censuses, admission, receipts, and verdict composition, and the
    product projects that declare their evidence and rendering request/result
    types. Product APIs cannot consume the tools-only plan or return
    `CompileClosureOutcome`, `CompileContextReceipt`, or `CompileBackVerdict`;
    tools cannot format or synthesize product-policy C#, flatten its
    accessibility, or substitute a tools-observed participant set for an owner
    manifest. Moving one planner surface into a product component, adding
    product-side compiler-reference selection, adding a product API that chooses
    compile-back roots or closure, or adding product-policy tools-side
    accessibility flattening, declaration synthesis, or rendering is
    mutation-verified to fail the architecture arm. Product-side candidate
    acquisition, package dependency resolution, identity and binding, typed
    evidence, and rendering remain permitted. The tools-owned
    `LegacyArtifactEmitter` is explicitly allowed to render the labelled legacy
    artifact but cannot produce product admission or product-policy evidence.
31. `AuthoredBodyControlPreservesProductRenderedShell` declines causal control
    attribution while #4931 is unavailable. With its owner-issued derived
    artifact, fixtures covering non-target siblings, async/unsafe modifiers,
    finalizer spelling, and a constructor initializer prove the template digest,
    typed range, preserved policy, non-target byte equality, and distinct result
    digest. The comparison key binds the base request's `ArtifactIdentity`,
    `ModuleIdentity`, canonical target set, scope, body policy, exact
    compiler/parse-policy digest, and frozen reference-set digest, including
    reference content, aliases, and embed-interop roles; only the authored
    replacement payload differs. It has no closure, coverage, admission, or
    compile-context receipt. Supplying ordinary text, independently re-rendering
    the control, changing any non-target byte or preserved policy, changing the
    artifact generation, module MVID, target set, scope, body policy, option,
    reference, alias, or embed-interop role, reusing the template digest, or
    copying any receipt fails the gate.
32. `ProductBodyClosureRequiresOwnerOccurrenceManifest` uses a product-generated
    real body containing a binding-relevant source occurrence that is erased
    from original and rebuilt IL. Until #4930 lands, the missing complete
    occurrence capability produces pre-commit `Declined`. With the owner result,
    the exact definition enters closure and rebuilt binding; removing or
    rebinding only that occurrence makes the result unavailable. No arm parses
    product C# or infers an empty occurrence set from IL equality.
33. `CSharpRoundTripChangedRejectsFailureRows` supplies complete endpoint
    inspections for exact, changed, producer-failure, identity-failure, and
    correspondence-failure cases. Only successful correspondence with at least
    one actual diff row and no failure/identity rows produces `Changed`; every
    failure case is `Unavailable`. Treating `IsExact: false` alone as `Changed`
    is mutation-verified to fail the gate.

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
