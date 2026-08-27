# Member fidelity evidence composition

## Status and scope

This is an explicitly approved broad **composition map** for the member-fidelity
path exercised by PRs #4142, #4146, and #4143. It connects existing owners; it
does not create an umbrella owner or transfer their responsibilities.

The map covers four questions:

1. Which owner establishes each fact?
2. Which typed distinctions must cross each handoff?
3. Which evidence permits a fidelity or `Exact` claim?
4. Which focused owner contracts must land before implementation resumes?

The target invariants below are **unverified** until the focused owner changes
and named gates in [Enforcement plan](#enforcement-plan) land. The three
implementation PRs are motivating evidence, not authority for this design and
not merge-ready substitutes for the missing contracts.

See [Type, member, and API representation](type-member-api-representation.md)
for the repository currency map,
[C# assembly round-trip testing](csharp-member-recompilation.md) for artifact
and comparison ownership, and
[ReturnToSender: fact-planned compile-back harness](fact-planned-compile-back-harness.md)
for the tools-only planner boundary.

## Problem

The three-PR stack repeatedly lost evidence at owner boundaries:

- an `op_*` metadata name or display spelling stood in for proven operator
  identity;
- canonical MethodImpl matching erased assembly scope needed to distinguish
  legal declarations;
- reconstructed member presence stood in for the exact interface edge and
  MethodImpl relationship required by the donor;
- unavailable or incomplete evidence became a successful `Full` or `Exact`
  result;
- filtered metadata rows spent decode work before selection, so a bounded
  operation was bounded only after expensive work had happened.

Each defect was locally plausible because its input still looked like the
answer. The missing design was not another universal identity type. It was an
explicit composition contract saying which dimensions each owner must preserve,
when an owner may erase one, and how refusal propagates.

## Component map

No row delegates its internal construction, validation, lifetime, or failure
policy to this document.

| Owner | Existing authority | Immediate composition obligation | Non-claim |
| --- | --- | --- | --- |
| Acquisition/session owner | Exact assembly content, PE lifetime, module identity, and reader coherence | Supply one coherent artifact/module context to Metadata consumers | Does not classify members or choose C# |
| `ILInspector.MetadataPrimitives` | Bounded SRM mechanics, neutral structural identity, raw MethodSemantics rows, and work budgets | Return typed rejection or exhaustion without display or fallback policy | Does not own API identity, fidelity, or reconstruction |
| `ILInspector.Metadata` | Metadata facts, API models, declaration identity, operator classification, MethodImpl relationships, and PDB correlations | Materialize reader-local evidence into owner-issued facts carrying every discriminator required by declared consumers | Does not decide body fidelity or render C# |
| `ILInspector.Decompiler` | Method-body import, typed IR, C# body production, spellability, and body fidelity | Consume owner-issued metadata facts and report unsupported or unspellable projections without inferring identity from text | Does not own API extraction or artifact closure |
| `ILInspector.CSharp` | Model-bound declaration spelling and typed type/member request rendering | Render the request it receives and reject missing declaration facts | Does not discover relationships or repair a plan |
| `ILInspector.Research` | Body identity, cross-representation correspondence, and composed evidence | Preserve owner-issued API/body identities and the provenance of correspondence | Does not turn a display match into identity |
| ReturnToSender planner | Tools-only closure, reconstruction obligations, body policy, and fidelity classification | Track every relationship needed by the requested donor and decline `Exact` when any obligation is unresolved | Does not construct or patch C# |
| Round-trip harness | Compilation, fixtures, independent oracles, comparison, and reporting | Keep artifact, compilation, correspondence, and diff outcomes separate | Does not compensate for missing product facts |
| CLI/output | Command and presentation policy | Render typed classifications and diagnostics at the terminal boundary | Does not classify `op_*` names or recover identity from display |

The product dependency direction remains the one in
[Inspection layers and consumer boundaries](inspection-layers.md). The
composition sequence below is a data-flow ordering, not permission for a lower
layer to reference a higher one.

## Composition sequence

### 1. Establish physical evidence

The acquisition owner opens exact content and establishes the module context.
Reader-local handles are valid only inside that context. MetadataPrimitives
performs bounded mechanical reads; Metadata turns those reads into native
facts before the reader lifetime ends.

Any value that leaves the reader scope carries enough owner-issued context to
prevent a handle, token, or structural signature from being interpreted in
another module. The required dimensions depend on the question:

- MethodDef coordinates require module identity, including MVID;
- cross-assembly named types require the metadata scope that names the
  definition;
- constructed types and custom modifiers preserve that scope recursively;
- API selectors and body identities remain separate currencies.

This is a target invariant and is currently unverified as an end-to-end
contract.

### 2. Classify without spelling

Metadata decides what the rows prove: operator declaration classification,
method kind, accessibility, MethodImpl ownership, interface declarations,
accessor relationships, and malformed or incomplete evidence.

Decompiler and CLI consumers may project those facts, but do not repeat the
classification:

- `SpecialName` plus an `op_*` prefix is candidate evidence, not sufficient
  operator identity;
- a known-framework exemption applies only to the exact fact it establishes;
- ordinary methods whose metadata names resemble operators stay ordinary;
- display spelling happens after classification and cannot strengthen it.

Decompiler separately decides whether a proven semantic operation is spellable
in the current C# projection. A valid operator call and an operator method group
have different spelling constraints. A body can therefore have exact operator
identity while its requested C# form remains unsupported.

This is a target invariant and is currently unverified as an end-to-end
contract.

### 3. Preserve relationships as obligations

ReturnToSender asks Metadata/CSharp/Decompiler for facts and artifacts, then
owns the tools-side question: which relationships must survive in the donor for
the requested fidelity level?

Member presence is not relationship evidence. The plan records separate
obligations for:

- the emitted direct, inherited, or constructed interface edge;
- the implementing member;
- the MethodImpl declaration when metadata identity requires one;
- instance versus static and reference-type versus value-type behavior;
- declaration shape and body projection;
- correspondence from the requested original member to the donor member.

The exact owner-local shape of that proof ledger belongs in a focused
ReturnToSender design. This composition map requires its result to distinguish
proved, disproved, unavailable, and failed obligations. Those words are shared
vocabulary here, not a proposed repository-wide enum.

This is a target invariant and is currently unverified.

### 4. Render and verify

CSharp renders typed declaration requests. Decompiler supplies product-owned
method bodies. ReturnToSender does not add source patches. The harness compiles
the artifact and invokes product-owned comparison capabilities.

The final classification composes, but does not collapse:

1. artifact-production status;
2. compiler status;
3. original-to-donor correspondence;
4. requested metadata relationship evidence;
5. body and API diff outcomes.

`Exact` requires every obligation selected by the versioned fidelity contract
to be proved. A successful compilation is necessary but insufficient, and an
empty diff is not proof when acquisition, correspondence, or comparison was
unavailable.

This is a target invariant and is currently unverified.

## Cross-boundary invariants

### Identity dimensions are monotonic

A projection may erase an identity dimension only when its owner documents the
erasure and no declared downstream question requires it. Assembly scope cannot
be erased from MethodImpl matching while extern-alias-distinct types remain
legal candidates. Module identity cannot be detached from a MethodDef token.
API and body identities cannot be replaced by their coincident display text.

The target rule is not "retain everything forever." It is "erase deliberately,
after the last consumer." This property is unverified.

### Evidence cannot become stronger in transit

At every handoff:

| Incoming evidence | Permitted downstream meaning |
| --- | --- |
| Proved | May authorize only the fact that was proved |
| Disproved | May reject that candidate or obligation |
| Unavailable or ambiguous | Must remain unavailable, decline fidelity, or request more evidence |
| Failed or exhausted | Must remain a visible failure |

An unavailable operator classification cannot become "ordinary method." An
unresolved interface edge cannot become "not required." An exhausted bounded
read cannot become an empty successful inventory. This property is unverified.

### Classification, identity, correspondence, and display stay separate

These are different questions:

- **Classification:** what kind of declaration or operation is this?
- **Identity:** which metadata/API/body subject does this denote?
- **Correspondence:** do two owner-issued identities denote the compared
  subjects under this operation?
- **Display:** how should an established fact be shown?

No owner may answer one by parsing another owner's display. This property is
partially enforced by existing owner tests but is unverified across this
composition.

### Work is charged before materialization

Selection and budget admission happen before expensive name, signature, base
chain, or relationship materialization whenever those facts are not needed for
the selected operation. Shared walks remain bounded; indexes are operation
scoped and reused instead of rebuilt per member.

A filter that omits a private type from public `typesOnly` extraction must do so
before decoding that type's long name. A finalizer lookup must not rescan every
TypeDef for every method. This property is unverified on `main`.

### Harnesses observe; product owners construct

The harness may compile, inspect diagnostics, and compare donor metadata. It
must not infer a missing interface edge, rewrite an operator declaration, or
patch a body into compilable C#. Such a repair would test the harness rather
than the product. This boundary is owned by
[C# assembly round-trip testing](csharp-member-recompilation.md) and the
repository harness rules.

## Decision placement

| Decision | Owner |
| --- | --- |
| Whether metadata proves a C# operator declaration | Metadata |
| Whether a proven operator operation is spellable in a requested body form | Decompiler |
| How a typed operator declaration is rendered | CSharp |
| How a classified member is displayed in CLI output | CLI/output |
| Whether a MethodImpl body and declaration belong to the same canonical relationship | Metadata |
| Whether a reconstructed donor must retain an interface edge or MethodImpl | ReturnToSender planner, consuming Metadata facts |
| Whether original and donor members correspond | The comparison owner named by the requested proof |
| Whether all requested evidence permits `Exact` | ReturnToSender's versioned fidelity contract |

If implementation needs a decision not present in this table, the next step is
to identify its focused owner, not add the decision to the nearest consumer.

## Focused successor designs

Implementation resumes through owner-sized contracts. The order reflects data
dependencies; independent documentation and prototypes may proceed in parallel.

### 1. Metadata relationship evidence

**Owner:** `ILInspector.Metadata`.

**Owning documents:** this map points to
[Type, member, and API representation](type-member-api-representation.md) and
[Shared metadata primitives](../metadata-primitives.md); the focused effort
must choose one authoritative contract location.

**Claim:** define the owner-issued operator, MethodImpl, interface-edge, scope,
and completeness facts required by declared consumers, including operation
budgets and typed refusal.

**Non-claims:** body spellability, donor planning, C# rendering, and `Exact`.

### 2. Decompiler operator consumption

**Owner:** `ILInspector.Decompiler`.

**Owning documents:** the decompiler correctness and raise-discipline
documents.

**Claim:** define how the importer, raisers, and printer consume Metadata-issued
operator evidence, how call and method-group spellability differ, and how
unsupported evidence changes fidelity.

**Non-claims:** metadata classification, API display, and artifact closure.

### 3. ReturnToSender proof ledger

**Owner:** ReturnToSender planner.

**Owning document:**
[ReturnToSender: fact-planned compile-back harness](fact-planned-compile-back-harness.md),
or a focused successor that document explicitly delegates to.

**Claim:** define versioned donor obligations, obligation states, and the total
composition that permits `Exact`.

**Non-claims:** product C# construction, Metadata relationship construction,
and diff semantics.

### 4. Composition integration

**Owner:** no new component; this document remains the map.

**Claim:** prove that the focused owner outputs connect without erasing scope,
relationship, refusal, or failure evidence.

**Non-claims:** changing an owner-internal contract. Any missing internal
capability returns to that owner's focused effort.

## Enforcement plan

The current implementation branches contain useful candidate tests, but none is
an enforcement gate on `main`. They are listed to preserve measured evidence,
not to approve their implementation.

| Target invariant | Candidate gates in the frozen stack | Status |
| --- | --- | --- |
| Operator identity and spelling stay separate | `UnresolvedKnownFrameworkOperatorDelegateTarget_DegradesToPartial`, `UnresolvedKnownFrameworkOperatorCall_StaysFull`, `PopulateMemberSections_FormatsOnlyTypedOperatorsAsOperators` | Candidate only; unverified on `main` |
| Receiver lowering preserves valid lambda shape | `UncheckedInstanceAssignment_MaterializedReceiverVoidLambdaStaysBlockBodied` | Candidate only; unverified on `main` |
| C# 14 instance assignment modifiers retain operator identity | `CSharpOperatorDeclaration_AcceptsVirtualInstanceAssignmentOperators`, `ApiSurface_ReportsVirtualInstanceAssignmentOperatorsAsDeclarations` | Candidate only; unverified on `main` |
| MethodImpl identity preserves recursive assembly scope | `Extract_PreservesExternAliasMethodImplDeclarationsWithDistinctScopes` | Candidate only; unverified on `main` |
| Metadata work is bounded before materialization | `FinalizerOwnerClassification_DoesNotRematerializeBaseNamesPerMethod`, `PdbFinalizerClassifier_UsesIndexedModuleScopedLookup`, `TypesOnlyExtraction_DoesNotBuildMemberOrLocalTypeIndexes` | Candidate only; unverified on `main` |
| `Exact` requires exact interface and MethodImpl obligations | `CompileBackTargets_FullRoundTripsInheritedSameAssemblyInterfacePath`, `CompileBackTargets_ValueTypeImplicitInterfaceMethodsAndAccessorsDeclineWhenOmitted`, `CompileBackTargets_FullRoundTripsImplicitStaticAbstractInterfaceMethod` | Candidate only; unverified on `main` |

Each focused successor must:

1. move or recreate only its owner-local gates against its final contract;
2. pair compiler-produced or real-artifact evidence with close negative cases;
3. name the gate for every claimed safety, soundness, or fidelity property;
4. add one non-vacuity test for each handoff whose wiring could silently die;
5. keep failure, ambiguity, and budget exhaustion visibly distinct.

The composition integration must include at least one end-to-end specimen for
each of these paths:

- metadata operator fact to body fidelity and display;
- scoped MethodImpl relationship to reconstructed donor metadata;
- unavailable interface relationship to an honest non-`Exact` result;
- bounded filtered extraction that proves omitted inputs do not spend the
  excluded decode budget.

## Implementation-stack disposition

PRs #4142, #4146, and #4143 remain frozen evidence while focused design work
proceeds. Green CI and mergeability establish that their current heads are
coherent; they do not establish review-clean architecture. Do not resume their
adversarial loop, merge them, or treat their candidate types as contracts
without a new explicit decision after the focused designs land.

Future implementation may reuse a branch's measured fixture or local mechanism
only after the owning design accepts the contract it enforces. A focused design
may instead supersede that mechanism. Preserving the finding does not require
preserving the patch.

## Non-goals

- A repository-wide `TypeRef`, member identity, or evidence enum.
- Moving ReturnToSender or Roslyn into a product assembly.
- Defining Metadata, Decompiler, CSharp, Research, or CLI internals here.
- Adding WinMD support.
- Proving runtime semantic equivalence from C# or IL equality.
- Making the frozen implementation stack merge-ready.
- Treating hostile in-process callers that bypass owner APIs as part of the
  contract.
