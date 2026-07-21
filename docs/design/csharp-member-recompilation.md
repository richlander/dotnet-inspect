# C# member recompilation and assembly patching

## Summary

This document proposes a tools-only capability for recompiling selected managed
members from C#, comparing the resulting implementation with the original IL,
and eventually transplanting approved method bodies into a copy of the original
assembly. It generalizes ReturnToSender's compiler-driven reconstruction closure
without making the decompiler harness the permanent owner of compilation,
comparison, or assembly mutation.

The motivating workflow is the managed equivalent of:

```text
ildasm -> edit IL -> ilasm
```

with C# available as the authoring language for selected members:

```text
original assembly
  -> select member set
  -> decompile or supply replacement C# bodies
  -> produce a compilable donor artifact
  -> compile with Roslyn
  -> compare original and donor implementations
  -> optionally transplant approved bodies into an original-assembly copy
```

The design has three independent axes:

| Axis | Values | Question answered |
| --- | --- | --- |
| Scope | `cluster`, `all` | Which declarations participate in donor compilation? |
| Body policy | `selected`, `full` | Which participating members receive real bodies? |
| Action | `compile`, `diff`, `patch` | What is done with the donor artifact? |

The first implementation target is `cluster + selected + diff`. `patch` starts
with same-shape method-body replacement and must reject metadata growth until a
separate import design proves it safe.

This is a design plan, not a commitment to a shipped command or syntax.

## Goals

- Start from one selected member or an explicit set of selected members.
- Compile replacement or decompiled C# in a context sufficient for Roslyn.
- Support both a minimal reconstruction closure and an assembly-wide artifact.
- Use one typed artifact-production pipeline for both scopes.
- Report exact C#, API, metadata, and IL/body differences.
- Prove that changes remain inside the selected member set before patching.
- Preserve the original assembly outside explicitly approved mutations.
- Reuse the capability from ReturnToSender, IL/C# differential tests, mutation
  tests, and future tools without introducing Roslyn into shipped product
  libraries.
- Keep every unsupported or unsafe case visible as a named failure.

## Non-goals

- Do not reconstruct a complete build project, source tree, or generator graph.
- Do not claim that compilable C# is semantically equivalent to the original IL.
- Do not make Roslyn, assembly loading, or mutation a dependency of product
  inspection paths.
- Do not make the harness compensate for missing product C# artifact behavior.
- Do not promise arbitrary metadata growth in the first patching implementation.
- Do not silently strip, regenerate, or invalidate strong-name signatures,
  Authenticode signatures, ReadyToRun data, or debug information.
- Do not execute an inspected or generated assembly as part of ordinary
  compilation or diffing.

## Core model

### Scope

`cluster` starts from the selected members and includes only the declarations
needed to compile them safely. The planner seeds typed evidence, compiles the
candidate artifact, interprets supported Roslyn diagnostics, adds same-assembly
roots or member requirements, and repeats within explicit budgets.

`all` includes every supported top-level type and nested declaration from the
target module in one compilation. It is not synonymous with full-body
decompilation. The body policy independently determines whether unselected
members are stubs, declaration-only members, or decompiled bodies.

Both scopes produce the same typed artifact request. Scope changes root
selection, not printing, compilation, comparison, or result shape.

### Body policy

`selected` gives real bodies only to explicitly selected members and to
inseparable companions required by C# syntax or semantics. Examples include the
other accessor of an explicitly implemented event or constructor initialization
that cannot be represented as an ordinary statement. Other concrete members use
typed stubs or skeleton policies.

`full` attempts a real body for every supported concrete member participating in
the selected scope. A failed or partial decompilation remains a named artifact
production failure; the planner must not silently replace it with a stub and
report success.

### Action

`compile` produces a donor compilation plus diagnostics and provenance. It does
not imply fidelity.

`diff` compares the original assembly and donor artifact. It classifies intended
selected-member changes, incidental donor differences, and unavailable
comparisons separately.

`patch` applies an approved replacement plan to a copy of the original assembly.
It is valid only when the mutation tier supports every required change and the
scoped-diff gate is green.

## Architecture

```text
                     selected members + C# bodies
                                  |
                                  v
                  +-------------------------------+
                  | Artifact request              |
                  | scope + body policy + targets |
                  +---------------+---------------+
                                  |
                                  v
 Metadata/CSharp/Decompiler  -> artifact provider
                                  |
                                  v
                  +-------------------------------+
                  | Closure compilation engine    |
                  | Roslyn + bounded feedback     |
                  +---------------+---------------+
                                  |
                                  v
                  +-------------------------------+
                  | Donor assembly + provenance   |
                  +---------------+---------------+
                                  |
                         +--------+--------+
                         |                 |
                         v                 v
                 product diff       patch planner
                 API + IL/body            |
                                           v
                                   method transplanter
```

The system consists of three reusable tools-side capabilities:

1. **Closure compilation engine** — requests artifacts, compiles them, grows
   closure policy from typed evidence and compiler diagnostics, and returns
   bounded outcomes.
2. **Assembly differ** — compares typed member identity, API shape, metadata
   dependencies, and canonical method bodies between original and donor.
3. **Method transplanter** — applies only a previously validated replacement
   plan and verifies the emitted assembly afterward.

ReturnToSender consumes closure compilation and diffing. It remains a fidelity
consumer rather than the general capability's public abstraction.

## Ownership boundaries

### Product libraries

- **Metadata** owns metadata facts, declaration identities, and SRM-only reads.
- **CSharp** owns typed declaration composition and C# spelling.
- **Decompiler** owns selected-member C# body production and its fidelity grade.
- **Instructions** owns decoded IL identity used by body comparison.
- **Research** may compose product-owned API, C#, and IL evidence; it does not
  own Roslyn orchestration or patching.
- Existing implementation-diff primitives remain the preferred comparison
  substrate instead of a patcher-specific parallel diff model.

### Tools and tests

- The closure compilation engine owns Roslyn options, reference selection,
  diagnostic feedback, budgets, and compilation provenance.
- Artifact request policy owns `cluster` versus `all` root selection and
  `selected` versus `full` body selection.
- The patch planner owns mutation eligibility and rejects unsupported changes.
- The transplanter owns binary writes and post-write structural validation.
- Harnesses own fixtures, orchestration, independent assertions, and reporting.

The artifact provider, not the closure engine, must produce C# declarations.
Compiler feedback may expand a typed request; it must not trigger ad hoc source
patches inside the harness.

## Typed contracts

The exact API remains implementation work, but the boundary should resemble:

```csharp
public enum ArtifactScope
{
    Cluster,
    All,
}

public enum ArtifactBodyPolicy
{
    Selected,
    Full,
}

public enum ArtifactAction
{
    Compile,
    Diff,
    Patch,
}

public sealed record ArtifactRequest(
    AssemblyIdentity Assembly,
    IReadOnlyList<MemberAnchor> Targets,
    ArtifactScope Scope,
    ArtifactBodyPolicy BodyPolicy,
    IReadOnlyDictionary<MemberAnchor, CSharpMemberBody> Replacements);
```

An artifact response must carry typed declarations, source provenance, included
roots and members, body policy decisions, and production failures. Display text
must not be used to recover member identity.

A closure compilation result should include:

- the final typed artifact request;
- generated C# source or source files;
- exact compiler and parse options;
- resolved metadata references and their identities;
- all Roslyn diagnostics, including diagnostics used for growth;
- closure iterations, roots, member requirements, and bail reasons;
- donor PE and portable PDB bytes when emission succeeds;
- compilation and artifact-production status as separate fields.

## Cluster algorithm

The current `CB_CLUSTER` strategy is the starting algorithm:

1. Seed the selected member's top-level root.
2. Add same-assembly roots and member requirements known from typed body facts.
3. Ask the artifact provider for the current typed request.
4. Compile with Roslyn.
5. On supported missing-symbol or accessibility diagnostics, resolve the named
   same-assembly identity and grow the request.
6. Stop on success, no growth, ambiguous unsafe growth, root budget, or iteration
   budget.

The general engine should preserve compiler-driven membership while separating
it from decompiler-specific body comparison. Diagnostic identifiers are evidence,
not identities: syntax and semantic-model evidence must resolve a typed metadata
candidate before the request grows.

Cluster compilation must record why every declaration was included:

- selected target;
- typed body reference;
- signature or constraint dependency;
- containing or nested declaration;
- base type or implemented interface;
- required member surface;
- compiler-diagnostic feedback;
- inseparable C# companion.

## All algorithm

`all` seeds every supported top-level root in metadata order and requests one
coherent compilation artifact. It uses the same declaration producer and
printer as `cluster`.

Unsupported declarations are reported individually. Whether one unsupported
declaration blocks the complete artifact is an explicit policy; the default
proof mode should fail the `all` claim rather than omit the declaration and call
the result complete.

`all + selected` is the practical broad binding mode: all supported declarations
participate, but only selected members have real bodies. `all + full` is the
whole-assembly decompiler-quality boss and is expected to expose a substantially
larger frontier.

## Cross-scope invariant

For a member that is safely capturable in both scopes, identical selected C# and
compiler settings should bind to the same member and produce the same normalized
selected body:

```text
cluster(targets, selected bodies) == all(targets, selected bodies)
```

Equality here means typed target correspondence plus the selected IL/body diff
contract, not byte-for-byte donor assembly equality. A mismatch indicates an
incomplete cluster, context-sensitive binding, different synthesized context, or
an inadequately specified normalization. It must be reported, never resolved by
choosing the more convenient result.

## Diff contract

Diffing precedes patching and answers four separate questions:

1. Did every selected source body bind to its intended metadata member?
2. How did each selected method body change?
3. What metadata dependencies would the replacement introduce?
4. Did anything outside the approved target set change?

At minimum the report should include:

- selected C# source before and after when both are available;
- canonical opcode and operand differences;
- local signature and initialization changes;
- exception-region changes;
- maximum-stack and method-header changes;
- referenced type, member, method-specification, standalone-signature, and user
  string dependencies;
- API and declaration-shape changes;
- target correspondence and provenance;
- intended, incidental, unavailable, and unsupported classifications.

Donor assemblies naturally differ in unrelated metadata layout and tokens.
Scoped diffing therefore compares typed identities and normalized bodies rather
than raw token values, while the patch planner separately proves that every
donor dependency can be represented in the output assembly.

## Patching tiers

Patching should advance through explicit tiers.

### Tier 0: donor compile and diff

No binary mutation. This proves artifact production, Roslyn binding, and diff
contracts and is the first implementation milestone.

### Tier 1: same-shape body replacement

Replace method bodies only when:

- the target signature and declaration are unchanged;
- every referenced metadata identity already has an unambiguous compatible row
  in the original module;
- the local signature can be expressed with existing metadata;
- the exception regions and method header are structurally valid;
- no new fields, methods, types, generic parameters, attributes, or resources
  are required;
- the post-write scoped diff shows no unapproved change.

A missing metadata dependency is a rejection with an import explanation, not an
implicit escalation.

### Tier 2: bounded metadata import

A later design may import new `TypeRef`, `MemberRef`, `TypeSpec`, `MethodSpec`,
`StandAloneSig`, and user-string entries. That work requires explicit token
remapping, deduplication, identity, malformed-input, and post-write validation
contracts. Tier 2 is not implied by this plan.

### Tier 3: structural changes

Adding or changing declarations, layout, interfaces, generic arity, resources,
or module structure is whole-assembly rewriting. It requires a separate design
and is outside the initial member-patching capability.

## Assembly integrity and output safety

`patch` always writes a new explicit output path. It never modifies the input in
place.

Before writing, the tool must detect and report:

- strong-name and Authenticode signatures;
- ReadyToRun or other native-image content;
- mixed-mode modules;
- portable, embedded, Windows, or absent PDBs;
- multi-module assemblies;
- malformed or unsupported method bodies;
- metadata growth required beyond the selected patch tier.

Signed inputs require an explicit signing policy. The initial safe behavior is
to reject patching signed or ReadyToRun assemblies while still allowing
`compile` and `diff`. Removing a signature is itself an observable mutation and
must never be an implicit convenience.

The output is re-opened with SRM and independently checked for:

- readable PE and metadata;
- resolvable patched method bodies;
- structurally valid instruction and exception-region boundaries;
- preserved non-target body hashes;
- preserved API shape at Tier 1;
- the exact approved selected-member diff.

Execution-based validation is a separate, explicit test policy because inspected
and generated assemblies are untrusted.

## PDB policy

Debug information is not incidental. Each result must state whether the output:

- preserves an unchanged PDB;
- emits a donor PDB that describes only the donor artifact;
- rewrites selected method debug information;
- omits debug information;
- cannot preserve valid source correspondence.

Tier 1 may initially emit a patched assembly without a corresponding patched PDB,
but only under an explicit option and with a visible result. It must not copy the
original PDB unchanged while claiming that it describes replacement bodies.

## Reference and compiler provenance

Recompilation fidelity depends on compiler and reference context. Every run must
record:

- Roslyn/compiler version;
- language version and feature switches;
- optimization, overflow, nullable, unsafe, and deterministic options;
- target framework and platform references;
- resolved package and assembly references;
- reference aliases and embed-interop policy when relevant;
- assembly and module attributes that affect lowering;
- unresolved project-level inputs such as generators or build tasks.

The assembly/package resolution service should provide reference identity and
closure. The tools-side engine owns the compilation policy that consumes it.

## Failure model

Failures remain layered so a source defect cannot be mistaken for a patcher
defect:

| Layer | Example outcomes |
| --- | --- |
| Selection | target missing, ambiguous overload, unsupported member kind |
| Artifact production | unsupported declaration, partial body, missing typed fact |
| Closure | stalled, ambiguous candidate, root budget, iteration budget |
| Compilation | parse failure, bind failure, emit failure |
| Correspondence | selected donor member missing or ambiguous |
| Diff | comparison unavailable, API drift, incidental body drift |
| Patch planning | metadata import required, signed input, unsupported PE shape |
| Transplant | write failure, token remap failure, invalid body layout |
| Verification | non-target drift, unreadable output, approved diff mismatch |

No layer converts a failure into an empty successful result.

## Proposed milestones

### Milestone 1: extract closure compilation

- Define tools-only request and result contracts.
- Extract compiler-driven root growth from decompiler-specific comparison.
- Adapt ReturnToSender to consume the engine without changing its verdicts.
- Preserve current cluster budgets, provenance, and failure buckets.

### Milestone 2: add `all + selected + compile`

- Seed all supported declaration roots.
- Use the existing typed C# declaration pipeline.
- Report complete versus incomplete assembly artifact production.
- Add cluster/all selected-body equivalence fixtures.

### Milestone 3: general diff mode

- Compare selected original and donor bodies through product diff primitives.
- Report metadata dependency and non-target drift.
- Support supplied replacement C# independently of decompiler-produced bodies.
- Produce machine-readable result artifacts.

### Milestone 4: Tier 1 patching

- Plan same-shape replacements against existing metadata identities.
- Write a new assembly and verify it independently.
- Reject signing, ReadyToRun, PDB, and metadata-growth cases not yet supported.
- Add real compiled fixtures and close negative cases for every eligibility rule.

### Milestone 5: `full` body policy

- Attempt full bodies under both scopes.
- Preserve per-member fidelity and unsupported frontiers.
- Establish an explicit whole-assembly quality gate without weakening selected
  member proof.

Each milestone should be independently useful. Later mutation work must not be
required to land the compilation and diff engine.

## Validation strategy

- Focused fixtures prove target selection, closure growth, and near-miss
  rejection.
- Real compiler-produced assemblies prove metadata and IL shapes.
- Cluster/all A/B tests prove selected-body correspondence and normalized IL
  equality.
- Diff tests distinguish operand, opcode, local, exception-region, metadata,
  API, and unrelated-member changes.
- Tier 1 tests hash every non-target body before and after patching.
- Negative tests cover new metadata dependencies, signing, ReadyToRun,
  multi-module inputs, generated members, ambiguous identities, and malformed IL.
- The IL round-trip suite remains an independent byte/assembly-oriented oracle;
  it should consume the shared transplanter capability rather than reconstruct
  mutation behavior in the harness.
- Corpus measurements report pinned inputs, commands, caps, timing, and named
  unsupported buckets. They do not replace targeted correctness fixtures.

## Open decisions

1. Which tools-only project owns the general closure compilation contracts?
2. Should supplied C# be a body fragment, a typed member declaration, or both?
3. Which existing implementation-diff contracts are sufficient for scoped
   patch approval, and which require extension?
4. What PE-writing substrate can preserve original metadata most faithfully at
   Tier 1 without importing a parallel inspection model?
5. Is local-signature growth permitted in Tier 1 when every referenced type
   already exists, or should the first tier require reuse of an existing
   standalone signature?
6. What exact PDB behavior is required before `patch` is useful outside tests?
7. Should cluster escalation to `all` be an explicit caller policy or a separate
   run whose provenance cannot be confused with cluster success?
8. Which unsupported declaration makes an `all` artifact incomplete versus
   representable with an explicit preservation boundary?

## Recommendation

Build the closure compilation and diff capabilities first as reusable tools-only
infrastructure. Preserve `cluster` and `all` as peer scopes over one typed
artifact pipeline, keep body policy and action orthogonal, and require a green
scoped diff before any binary write. Introduce patching only at the same-shape
body tier, where every unsupported dependency is rejected explicitly and the
original assembly remains the preservation authority.
