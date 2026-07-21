# C# assembly round-trip testing

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
- **Instructions** owns `IlBodyDiff`, its normalization mechanics, and its total
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

`ArtifactIdentity` identifies the exact input bytes and acquisition provenance.
`ModuleIdentity` includes module name and MVID so a member anchor is never
interpreted without its physical metadata scope. Display text is not identity.
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

The assembly/package resolution service provides reference identity and closure.
The tools-side engine owns the compilation policy that consumes it.

## Failure model

| Layer | Example outcomes |
| --- | --- |
| Selection | target missing, ambiguous overload, unsupported member kind |
| Artifact production | unsupported declaration, partial body, missing typed fact |
| Closure | stalled, ambiguous candidate, root budget, iteration budget |
| Compilation | parse failure, bind failure, emit failure |
| Correspondence | rebuilt member missing, ambiguous, or wrong module |
| C# diff | exact, changed, unavailable |
| IL diff | exact, operand diff, opcode diff, unavailable |
| Scope A/B | same, different, unavailable |

No layer converts failure or unavailability into an empty successful result.

## Proposed milestones

### Milestone 1: extract round-trip compilation

- Define tools-only request and layered result contracts.
- Add the missing product-owned, handle-addressed `MemberBodyProducer` result that
  returns a typed `CSharpMemberBody` plus fidelity and failure provenance; adapt
  ReturnToSender away from its harness-side body conversion.
- Wire the product artifact pipeline explicitly: the tools planner builds neutral
  `CSharpTypeShellSpec` inputs, `TypeShellProducer` builds typed print requests,
  the new member-body seam supplies decompiled body increments, and
  `CSharpTypePrinter` renders source.
- Extract compiler-driven root growth from decompiler-specific comparison.
- Adapt ReturnToSender to consume the engine without changing its verdicts.
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

Documentation-only changes validate Markdown. Implementation milestones add the
smallest focused product and harness checks that prove their claims.

## Open decisions

1. Which tools-only project owns the round-trip request and result contracts?
2. Which existing `ImplementationDiff` results should be retained directly, and
   which need a round-trip-specific envelope for provenance?
3. Is cluster/all A/B opt-in per consumer or an explicit round-trip report mode?
4. Which declaration shapes are unsupported in `all`, and how should the typed
   incomplete-scope result group them?
5. Which corpus and fixture populations should establish the initial practical
   success baseline?

## Recommendation

Build reusable round-trip compilation and layered IL/C# comparison first. Keep
`cluster` and `all` as peer scopes, keep selected and full body policy
independent, and let existing product diff tools arbitrate only the evidence they
currently own. Strengthen metadata and PE comparison later from measured needs.
Do not let this initial proposal imply binary patch safety or whole-assembly byte
preservation.
