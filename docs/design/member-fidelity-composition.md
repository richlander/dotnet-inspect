# Member fidelity evidence composition

## Status and scope

This is an explicitly approved broad **composition map** for the member-fidelity
path exercised by PRs #4142, #4146, and #4143. It connects the existing
acquisition, MetadataPrimitives, Metadata, Analysis, Decompiler, CSharp,
Research, ReturnToSender planner/harness, and CLI owners. It does not create an
umbrella owner or transfer their responsibilities.

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
[Artifact acquisition and workspace composition](artifact-acquisition-and-workspaces.md)
for the upstream artifact, resolved-assembly, and PE-session owners,
[Member inspection planning and Metadata projection](member-inspection-planning-and-metadata-projection.md)
for Metadata declaration facts and the CSharp representability boundary,
[C# assembly round-trip testing](csharp-member-recompilation.md) for the
current tools-only engine design and its unresolved request/result owner, and
[ReturnToSender: fact-planned compile-back harness](fact-planned-compile-back-harness.md)
for the current tools-only request, proof, and reporting boundary.

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
| Resolved assembly reference | Selected managed-assembly identity and guarded repeatable content access | Supply the exact selected assembly and owner-issued provenance to the inspection session | Does not own artifact acquisition or PE lifetime |
| Assembly inspection session | One opened PE lifetime and session-scoped operations | Keep the reader/image coherent and live while Metadata materializes escaping facts | Does not own artifact identity, assembly selection, or Metadata facts |
| `ILInspector.MetadataPrimitives` | Bounded SRM mechanics, neutral structural identity, raw MethodSemantics rows, and work budgets | Return typed rejection or exhaustion without display or fallback policy | Does not own API identity, fidelity, or reconstruction |
| `ILInspector.Metadata` | Metadata facts, API models, declaration identity, operator classification, MethodImpl relationships, cross-reader method correspondence, and PDB correlations | Materialize reader-local evidence into owner-issued facts carrying every discriminator required by declared consumers | Does not decide body fidelity or render C# |
| `ILInspector.Analysis` | Whole-assembly IL, body, call-site, `MethodIdentity`, and `MemberRef` evidence | Preserve definition-versus-reference provenance and explicit unknown evidence in body/call-site facts | Does not own API identity, C# representability, or compile-back fidelity |
| `ILInspector.Decompiler` | Method-body import, typed IR, C# body production, spellability, and body fidelity | Consume owner-issued metadata facts and report unsupported or unspellable projections without inferring identity from text | Does not own API extraction or artifact closure |
| `ILInspector.CSharp` | Model-bound C# representability, declaration spelling, and typed type/member request rendering | Decide representability from Metadata-issued facts, then render accepted requests or return typed refusal | Does not discover metadata relationships or repair a plan |
| `ILInspector.Research` | Body-identity canonicalization, cross-representation correspondence, and composed evidence | Preserve Analysis- and Metadata-issued identities, including body/call-site evidence, and the provenance of correspondence | Does not establish Analysis facts or turn a display match into identity |
| ReturnToSender | Tools-only artifact requests, closure and obligation planning, compilation, product-diff invocation, and current versioned fidelity reporting | Keep planner obligation output distinct from harness proof while preserving one owner-local result contract | Does not construct product C# or redefine producer diff semantics |
| CLI/output | Command and presentation policy | Render typed classifications and diagnostics at the terminal boundary | Does not classify `op_*` names or recover identity from display |

The product dependency direction remains the one in
[Inspection layers and consumer boundaries](inspection-layers.md). The
composition sequence below is a data-flow ordering, not permission for a lower
layer to reference a higher one.

## Composition sequence

### 1. Establish physical evidence

The upstream artifact and workspace contracts resolve one managed assembly
without being restated here. This map begins with their
`ResolvedAssemblyReference`. An `AssemblyInspectionSession` opens that exact
content and owns the PE lifetime. Reader-local handles are valid only inside
that session. MetadataPrimitives performs bounded mechanical reads; Metadata
turns those reads into native facts before the reader lifetime ends.

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

Metadata decides what the rows prove: operator semantic evidence, method kind,
accessibility, MethodImpl ownership, interface declarations, accessor
relationships, and malformed or incomplete evidence. Analysis establishes its
own body/call-site evidence from resolved MethodDefs and MemberRefs. That
Analysis currency preserves whether operator identity is proved, disproved, or
unknown; Research may project it but does not establish it.

Consumers may project owner-issued facts, but do not repeat their
classification:

- `SpecialName` plus an `op_*` prefix is candidate evidence, not sufficient
  operator identity;
- a known-framework exemption applies only to the exact fact it establishes;
- an unresolved cross-assembly MemberRef remains unknown body/call-site
  evidence;
- ordinary methods whose metadata names resemble operators stay ordinary;
- display spelling happens after classification and cannot strengthen it.

CSharp decides whether Metadata-issued semantic declaration facts are
representable in C# and renders accepted declaration requests. Decompiler
separately decides whether a proven body operation is spellable in the current
C# body projection. A valid operator call and an operator method group have
different body-form constraints. A method can therefore have exact operator
identity while either its declaration or requested body form remains
unsupported.

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

The exact owner-local shape of that obligation ledger belongs in a focused
ReturnToSender design. This composition map requires the planner output to
distinguish proved, disproved, unavailable, and failed planning states. The
ReturnToSender harness consumes those states with compilation, correspondence,
and product diff evidence; only ReturnToSender's versioned fidelity contract
issues the final verdict. Those state words are shared vocabulary here, not a
proposed repository-wide enum.

This is a target invariant and is currently unverified.

### 4. Render and verify

CSharp renders accepted typed declaration requests. Decompiler supplies
product-owned method bodies. ReturnToSender does not add source patches. The
ReturnToSender harness compiles the artifact, invokes product-owned comparison
capabilities, and owns the current versioned fidelity verdict.

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

An unknown Analysis operator fact cannot become "ordinary method." An
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

### Bounded metadata failures remain visible

[Bounded metadata traversal](bounded-metadata-traversal.md) owns graph, text,
collection, and materialization budgets.
[Member inspection planning and Metadata projection](member-inspection-planning-and-metadata-projection.md)
owns projection admission and safety accounting. This map does not prescribe
filter order, index lifetime, cache reuse, or lookup strategy.

The composition obligation begins at their typed result: budget exhaustion or
rejection remains visible to every consumer and cannot become an ordinary empty
inventory, negative relationship, or successful artifact. This handoff property
is unverified.

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
| Whether metadata rows establish operator semantic evidence | Metadata |
| Whether body/call-site evidence proves, disproves, or cannot resolve operator identity | Analysis |
| Whether Metadata-issued operator facts form a representable C# declaration | CSharp |
| Whether a proven operator operation is spellable in a requested body form | Decompiler |
| How a typed operator declaration is rendered | CSharp |
| How a classified member is displayed in CLI output | CLI/output |
| Whether a MethodImpl body and declaration belong to the same canonical relationship | Metadata |
| Which interface-edge and MethodImpl obligations the requested donor must retain | ReturnToSender planner, consuming Metadata facts |
| Whether original and donor method addresses correspond across readers | Metadata's total `MethodCorrespondenceResolver` |
| Whether all requested evidence permits current compile-back `Exact` | ReturnToSender's versioned harness fidelity contract |

If implementation needs a decision not present in this table, the next step is
to identify its focused owner, not add the decision to the nearest consumer.

## Focused successor designs

Implementation resumes through owner-sized contracts. The order reflects data
dependencies; independent documentation and prototypes may proceed in parallel.

### 1. Assembly inspection content receipt

**Owner:** `AssemblyInspectionSession`.

**Owning document:**
[Artifact acquisition and workspace composition](artifact-acquisition-and-workspaces.md).

**Claim:** define how a session receives a `ResolvedAssemblyReference` and
retains or verifies the binding among selected assembly identity, owner-issued
provenance, and the exact opened content, with a non-vacuity gate that rejects
content substitution.

**Non-claims:** artifact storage, source selection, acquisition authorization,
or Metadata fact construction.

### 2. Metadata relationship evidence

**Owner:** `ILInspector.Metadata`.

**Owning document:**
[Member inspection planning and Metadata projection](member-inspection-planning-and-metadata-projection.md).

**Claim:** extend the existing shared-declaration-fact contract with the
owner-issued operator, MethodImpl, interface-edge, scope, and completeness facts
required by declared consumers, including typed semantic refusal.

**Non-claims:** body spellability, donor planning, C# rendering, and `Exact`.

### 3. MetadataPrimitives bounded rejection

**Owner:** `ILInspector.MetadataPrimitives`.

**Owning document:**
[Bounded metadata traversal](bounded-metadata-traversal.md).

**Claim:** define the neutral budget and typed rejection result issued to
Metadata consumers.

**Non-claims:** Metadata projection receipt, semantic classification, and
consumer presentation.

### 4. Metadata bounded-rejection receipt

**Owner:** `ILInspector.Metadata`.

**Owning document:**
[Member inspection planning and Metadata projection](member-inspection-planning-and-metadata-projection.md).

**Claim:** define how Metadata projection and degraded-fact results consume
MetadataPrimitives rejection without turning exhaustion into ordinary absence.

**Non-claims:** traversal mechanics, budget construction, filtering strategy,
index implementation, and higher-consumer presentation.

### 5. Analysis body and call-site operator evidence

**Owner:** `ILInspector.Analysis`.

**Owning document:** a focused Analysis member-identity document established by
that effort; [Type, member, and API representation](type-member-api-representation.md)
remains only the currency map.

**Claim:** define how resolved MethodDefs and MemberRefs establish body/call-site
operator evidence, preserve `Unknown`, and reach Research projections without
changing `MethodIdentity` or `MemberRef` equality accidentally.

**Non-claims:** API classification, C# representability, decompiler
spellability, and compile-back fidelity.

### 6. Research body-identity receipt

**Owner:** `ILInspector.Research`.

**Owning document:**
[Member target resolution](member-target-resolution.md).

**Claim:** define how Research body identity and projections receive
Analysis-issued `MethodIdentity` and `MemberRef` operator evidence without
reconstructing, strengthening, or dropping its unknown state.

**Non-claims:** Analysis fact construction, API identity, and compile-back
fidelity.

### 7. CSharp declaration representability

**Owner:** `ILInspector.CSharp`.

**Owning document:**
[Member inspection planning and Metadata projection](member-inspection-planning-and-metadata-projection.md),
limited to its CSharp representability boundary.

**Claim:** define how Metadata-issued operator and MethodImpl facts authorize,
reject, or decline a C# declaration request, including language-version-sensitive
forms.

**Non-claims:** Metadata fact construction, body projection, and donor closure.

### 8. Decompiler operator consumption

**Owner:** `ILInspector.Decompiler`.

**Owning document:** [Decompiler correctness pipeline](../decompiler-correctness-pipeline.md).

**Claim:** define how the importer, raisers, and printer consume
Metadata-issued operator evidence, how call and method-group body spellability
differ, and how unsupported evidence changes body fidelity. The raise-discipline
document remains supporting mechanics, not a second owner.

**Non-claims:** Metadata or Analysis classification, declaration
representability, API display, and artifact closure.

### 9. CLI classified-member projection

**Owner:** CLI/output.

**Owning document:** a focused API-member output-projection document established
by that effort.

**Claim:** define how owner-issued member classification selects grouping and
display spelling without parsing `op_*` names.

**Non-claims:** member classification, C# declaration representability, and
body fidelity.

### 10. Cross-reader method correspondence

**Owner:** `ILInspector.Metadata`.

**Owning document:** a focused Metadata method-correspondence document
established by that effort.

**Claim:** document and gate the shipped `MethodCorrespondenceResolver` as the
single total resolver from one module-scoped method address to an exact target
address or `Absent`, `Ambiguous`, or `Failed`.

**Non-claims:** changing API or body identity construction, round-trip receipt,
diff semantics, and final fidelity classification.

### 11. Round-trip correspondence receipt

**Owner:** current `DotnetInspector.RoundTripCompilation` consumer.

**Owning document:**
[C# assembly round-trip testing](csharp-member-recompilation.md).

**Claim:** define how the current tools-only comparison consumes Metadata's
total correspondence result before invoking product C# and IL diff arbiters,
preserving every non-exact state.

**Non-claims:** Metadata correspondence semantics, long-term request/result
ownership, product diff semantics, and ReturnToSender's final verdict.

### 12. ReturnToSender obligations and verdict

**Owner:** ReturnToSender.

**Owning document:**
[ReturnToSender: fact-planned compile-back harness](fact-planned-compile-back-harness.md).

**Claim:** define versioned donor obligations and typed planner states, then
compose them with typed correspondence, compilation, and product-diff outcomes
under the existing ReturnToSender status contract.

**Non-claims:** product C# construction, Metadata relationship construction,
correspondence construction, and product diff semantics.

The reusable round-trip engine already exists in
`tools/RoundTripCompilation` and is consumed by ReturnToSender. Its owning
document still has an open decision for long-term request/result ownership.
This map does not resolve that decision or transfer ReturnToSender's current
verdict to the engine.

There is no ownerless integration successor. Each immediate consumer owns the
receipt test for its incoming handoff:

- AssemblyInspectionSession gates the binding from resolved assembly identity
  and provenance to opened content;
- Metadata gates MetadataPrimitives/bounded-traversal rejection into its
  projection and degraded-fact results;
- Analysis gates Metadata-to-Analysis fact adaptation;
- Research gates Analysis-to-Research body-identity and evidence receipt;
- CSharp gates Metadata-to-CSharp representability input;
- Decompiler gates its Metadata/owner-fact consumption;
- CLI gates Metadata/API classification projection;
- ReturnToSender gates Metadata/CSharp/Decompiler inputs to its obligation
  ledger;
- RoundTripCompilation gates its receipt of Metadata-issued cross-reader
  correspondence;
- ReturnToSender gates planner obligations, correspondence, compilation, and
  product results into its current verdict.

## Enforcement plan

The table distinguishes candidate evidence on the frozen implementation
branches, gates already present on `main`, and planned gaps with no gate. The
per-row status is authoritative.

| Target invariant | Evidence or required gate | Status |
| --- | --- | --- |
| Resolved assembly identity and provenance stay bound to session-opened content | None; the AssemblyInspectionSession receipt successor must add a content-substitution non-vacuity gate | Planned and unverified |
| Analysis operator evidence preserves unknown MemberRefs | `MetadataOperatorFactTests.CrossAssemblyMemberReferences_StayUnknown` | Candidate only; unverified on `main` |
| Body operator identity and spelling stay separate | `UnresolvedKnownFrameworkOperatorDelegateTarget_DegradesToPartial`, `UnresolvedKnownFrameworkOperatorCall_StaysFull` | Candidate only; unverified on `main` |
| CLI operator display consumes typed classification | `PopulateMemberSections_FormatsOnlyTypedOperatorsAsOperators` | Candidate only; unverified on `main` |
| Receiver lowering preserves valid lambda shape | `UncheckedInstanceAssignment_MaterializedReceiverVoidLambdaStaysBlockBodied` | Candidate only; unverified on `main` |
| C# 14 instance assignment modifiers retain operator identity | `CSharpOperatorDeclaration_AcceptsVirtualInstanceAssignmentOperators`, `ApiSurface_ReportsVirtualInstanceAssignmentOperatorsAsDeclarations` | Candidate only; unverified on `main` |
| MethodImpl identity preserves recursive assembly scope | `Extract_PreservesExternAliasMethodImplDeclarationsWithDistinctScopes` | Candidate only; unverified on `main` |
| Metadata cross-reader correspondence returns `Exact`, `Absent`, and wrong-module `Failed` | `MethodCorrespondenceResolverTests.Resolve_ReturnsExactAddressAcrossReadersOfSameArtifact`, `Resolve_ReturnsAbsentForNearMissInAnotherModule`, `Resolve_ReturnsFailedForSourceAddressFromWrongModule` | Enforced on `main` for the named arms |
| Metadata cross-reader correspondence returns bounded `Ambiguous` evidence | None; the focused Metadata correspondence successor must add an in-budget duplicate-candidate gate | Planned and unverified |
| RoundTripCompilation preserves `Absent` correspondence as unavailable | `RoundTripComparisonTests.Compare_PreservesAbsentCorrespondenceAsUnavailable` | Enforced on `main` for `Absent` |
| RoundTripCompilation preserves `Ambiguous` and `Failed` correspondence as unavailable | None; the focused receipt successor must add both result-arm gates | Planned and unverified |
| Bounded Metadata work avoids repeated materialization and indexes reusable lookup | `FinalizerOwnerClassification_DoesNotRematerializeBaseNamesPerMethod`, `PdbFinalizerClassifier_UsesIndexedModuleScopedLookup`, `TypesOnlyExtraction_DoesNotBuildMemberOrLocalTypeIndexes` | Candidate only; unverified on `main` |
| Metadata preserves forced budget rejection at its projection boundary | None; the focused Metadata receipt successor must add a forced-exhaustion projection gate | Planned and unverified |
| `Exact` requires exact interface and MethodImpl obligations | `CompileBackTargets_FullRoundTripsInheritedSameAssemblyInterfacePath`, `CompileBackTargets_ValueTypeImplicitInterfaceMethodsAndAccessorsDeclineWhenOmitted`, `CompileBackTargets_FullRoundTripsImplicitStaticAbstractInterfaceMethod` | Candidate only; unverified on `main` |

Each focused successor must:

1. move or recreate only its owner-local gates against its final contract;
2. pair compiler-produced or real-artifact evidence with close negative cases;
3. name the gate for every claimed safety, soundness, or fidelity property;
4. add one non-vacuity test for each handoff whose wiring could silently die;
5. keep failure, ambiguity, and budget exhaustion visibly distinct.

The named immediate consumers must collectively include at least one seam
specimen for each of these paths:

- resolved assembly identity/provenance to exact session-opened content;
- Metadata operator fact to CSharp declaration representability;
- Analysis operator fact to Research/body projection;
- owner-issued operator fact to Decompiler body fidelity;
- Metadata/API classification to CLI display;
- Metadata method correspondence to a non-exact RoundTripCompilation result;
- scoped MethodImpl relationship to reconstructed donor metadata;
- unavailable interface relationship to an honest non-`Exact` result;
- bounded extraction rejection to a visible non-success result.

## Implementation-stack disposition

At approval of this map, PRs #4142, #4146, and #4143 were frozen as evidence
while focused design work proceeded. Their green CI and mergeability established
that those heads were coherent; they did not establish review-clean
architecture. Resuming their adversarial loop, merging them, or treating their
candidate types as contracts requires a new explicit decision after the focused
designs land.

Future implementation may reuse a branch's measured fixture or local mechanism
only after the owning design accepts the contract it enforces. A focused design
may instead supersede that mechanism. Preserving the finding does not require
preserving the patch.

## Non-goals

- A repository-wide `TypeRef`, member identity, or evidence enum.
- Moving ReturnToSender or Roslyn into a product assembly.
- Defining Metadata, Analysis, Decompiler, CSharp, Research, or CLI internals
  here.
- Adding WinMD support.
- Proving runtime semantic equivalence from C# or IL equality.
- Making the frozen implementation stack merge-ready.
- Treating hostile in-process callers that bypass owner APIs as part of the
  contract.
