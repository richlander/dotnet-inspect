# Implementation Diff Boundary

> **Map:** [Type, member, and API representation](type-member-api-representation.md) is the entry
> point for choosing a type, member, or API identity shape. This document owns
> the details below. [Member target resolution](member-target-resolution.md)
> owns exact/carried physical body addressing and cross-version body keys.
> [Artifact acquisition and workspace composition](artifact-acquisition-and-workspaces.md)
> owns endpoint realization and participant correspondence.

The structured comparison lifecycle in this document is a design proposal. Its
target boundaries and guarantees are **unverified** until the named gates
exist. Existing APIs that do not satisfy the target are listed under
[Current mismatches](#current-mismatches).

Implementation Diff is the product-side decompiled C# + IL/body + PDB Source
comparison. `ILInspector.Research` owns its Research-only evidence join;
`DotnetInspector.ResearchQueries` owns acquisition/evidence composition and
the query-level results. Together they provide the reusable component for the
CLI, ReturnToSender, harnesses, and other consumers that need one member-centric
change model instead of separate C# and IL renderers.

Terminology follows [Finding Nomenclature](finding-nomenclature.md):
`Finding<T>` is a one-version observation, `PairFinding<T>` is a two-version
transition, and evidence is the role either may play rather than a competing row
family.

## Ownership

- `ILInspector.Decompiler` owns C# body diff production and display rows through
  `CSharpBodyDiff` and `CSharpDiffPrinter`.
- `ILInspector.ILDiff` owns IL/body diff production and display rows
  through `IlBodyDiff`, `IlAssemblyDiff`, and `IlDiffPrinter`.
- `ILInspector.Research` owns the evidence join over Research-owned admission,
  work-item, mechanism, and subject currencies. It compares decompiled C# and
  IL/body mechanisms. For planned Source evidence, it receives bounded,
  checksum-verified raw Source-input snapshots only after query-owned
  acquisition, then constructs and lowers Finding comparisons inside its
  dependent producer callback after Research-owned preflight. It groups changes
  by Research work-item and `ResearchSubjectKey` and exposes typed evidence
  without receiving a core-Queries currency.
- `ResearchComparison.RetainedComparisons` keeps the native
  `FindingComparison<CSharpCanonicalLine>` and
  `FindingComparison<CanonicalIlOperation>` envelopes when requested. PDB Source
  comparisons retain `FindingComparison<string>` with the `text.line`
  descriptor. Research
  cross-checks their exactness against the richer semantic projections for
  members present on both sides. A disagreement is retained as a per-member
  `Failed` diagnostic; it does not abort healthy members in the same diff.
- `DotnetInspector.ResearchQueries` owns the authorized planned and direct
  operations. Each mints the aggregate plan/result budget ledger before any
  question, endpoint, pairing, or producer work, then lends only
  operation-stamped owner-local facets to the product-owned stages. It owns the
  bijective population projection from core-Queries currencies to
  Research-owned values, composes Research completion with query receipts, and
  constructs `ImplementationDiffResult` and
  `ImplementationMemberDiffResult`. The planned async operation opens or
  borrows inspection sessions, enforces authored-source budgets, publishes the
  comparison, and releases leases before returning a typed final query outcome.
  The synchronous direct operation retains the caller's live designation only
  for its lexical invocation and returns one inert typed member result.
- The assembly/package `diff` CLI owns selection and presentation only. It
  supplies endpoint/target requests and capabilities, then renders the
  completed result or typed query failure. It never opens readers around
  Research, projects a mechanism, or enriches a finalized comparison. The direct
  `match --implementation` path already owns one live source while resolving
  its two exact selectors; it may create an invocation-scoped direct
  designation and pass it to the Queries-owned direct operation, but it does
  not invoke a Research producer or fabricate an assembly-wide result.

### Structural body comparison

`CSharpBodyDiff.IssueCorrespondence` is the product correspondence owner for
two exact `AnnotatedSourceDocument` values describing the same physical method
body. Each product document carries assembly name, MVID, MethodDef token, body
fingerprint, and source-facing member label. Each supported C# node carries the
sorted IL-origin set retained by its contributing IR subtree. The issuer first
requires equal physical method provenance, hashes each exact document revision,
and then matches only origin sets unique on both sides. A repeated origin set is
ambiguous even when nested nodes happen to occupy corresponding depths: wrapper
substitution can shift those depths without preserving identity. A unique
one-sided set is `NoCounterpart` only when every opposite-side node carries
provenance; otherwise the incomplete population leaves it ambiguous. Equal
document-local ids, source coordinates, selected text, kind labels, and display
order never establish cross-document identity.

The producer checks every origin against an instruction boundary in the
fingerprinted physical body. A subtree that retains any offset imported from a
nested or reconstructed companion method is unsupported as a whole; foreign
offsets are never intersected into a plausible-looking partial identity.

The issued `CSharpNodeCorrespondenceResult` retains the exact documents and
their revision identities, document-scoped node identities, the
`IlOriginSet` provenance for every match, and explicit unmatched Before and
After nodes. Missing evidence is `Unsupported`, non-unique evidence is
`Ambiguous`, and a unique one-sided key is `NoCounterpart`. Unsupported or
ambiguous nodes never become guessed additions or removals.

`CSharpBodyDiff.CompareStructure` is the node/span consumer. Its product-issued
overload validates the complete result against the exact documents, projects
the mixed annotated-source documents to C# without re-parsing rendered syntax,
and maps matches plus `NoCounterpart` nodes into the existing selected-node
comparison. The explicit-input overload is an internal construction seam for
focused presentation tests; it is not a portable input contract.

The result is one `CSharpStructuralComparison` with explicit `Added`, `Removed`,
`Changed`, and `Moved` outcomes. Movement is orthogonal, so a node may be both
changed and moved. After provenance establishes identity, the issuer classifies
the smallest deterministic set outside the longest order-preserving match
sequence as moved; local order participates only in that classification, never
in identity. Stable kind ids and display labels come from
`AnnotatedSourceNodeKinds`; the comparison does not expose raw IR type names.
Each row retains exact absolute UTF-16 spans and the smallest enclosing region
role on both sides. Optional compile-back fidelity is separately supplied typed
evidence, not a conclusion inferred from C# text.

`CSharpStructuralDiffDocument` is the portable artifact paired with
`AnnotatedSourceDocument`. It carries schema and methodology versions, the
exact revision-bound `CSharpNodeCorrespondenceResult`, and optional independent
fidelity evidence. It also retains the generated structural rows so the JSON is
the diff artifact, not only a recipe for recreating one. Its top-level Before
and After values are the exact C#-only projections that own those row ids and
spans; the correspondence payload separately retains the original mixed
annotated-source documents. Construction and strict deserialization reissue
correspondence from those originals, derive both projections and the expected
rows, and require exact agreement before `ToComparison` exposes them. This
keeps the artifact product-issued rather than accepting caller-authored
mappings, projections, or rows.
`CSharpStructuralComparisonTests.StructuralDiffDocument_RejectsTamperedCorrespondence`
`CSharpStructuralComparisonTests.StructuralDiffDocument_RejectsTamperedProjection`,
and
`StructuralDiffDocument_RejectsTamperedRows` are the non-vacuity gates for that
replay check.
`StructuralDiffDocument_ProjectsInterleavedIlWithoutInferringFromText` gates
that the top-level projections own the serialized row coordinates.

The C# name is intentional. `AnnotatedSourceDocument` may interleave C# and IL,
but this artifact compares the C# node/span projection. Native IL comparison
remains owned by `ILInspector.ILDiff` through `IlBodyDiffResult`; a future
portable IL envelope should retain that typed result rather than manufacture a
parallel generic structural-row hierarchy. Research remains the owner of any
combined C# + IL implementation-diff document.

`CSharpStructuralDiffPrinter` projects that one result into complete-body caret
overlays and compact rich-diff rows. For a changed single-span node, each caret
annotation reads the exact counterpart text from the already-corresponded
document and names it as `changed to` or `changed from`. Text remains
presentation evidence and never participates in identity. Multiline,
multi-span, long, ill-formed UTF-16, and whitespace-lossy transitions use the
bounded `text changed` label symmetrically rather than allocating or injecting
unbounded or inexact text into an annotation. A display-unsafe counterpart also
uses that fallback; rendering a document whose own source is display-unsafe
remains rejected by the existing safety gate. It performs no correspondence.
`CSharpStructuralComparisonTests.RenderAnnotatedBody_WrappedExactTransitionReconstructsCounterpart`
gates the lossless wrapped-text claim.
DecompilerHarness `--structural-review` mode owns Markdown orchestration and
consumes the same result for both presentations. With two documents it invokes
the product issuer; `--json` emits the resulting
`CSharpStructuralDiffDocument`. The one-file form accepts only that generated
artifact, reissues its correspondence, and renders it later. Both forms read
untrusted input through Decompiler-owned `AnnotatedSourceJson`, so the CLI
document writer and harness reader share one model-owned contract while
retaining separate writer and strict-reader policies. Unsupported and ambiguous
nodes remain a separate correspondence-gap section. The default Markdown table
keeps change, structure, and region, adds fidelity only when populated, and
omits absolute spans. Gaps are grouped by side and reason with counts and at
most five node examples. Any gap marks the review partial because matched rows
cannot establish changes represented only by unsupported or ambiguous nodes.
That status appears before either body so a long artifact cannot bury the
evidence limit. The portable JSON remains exhaustive for spans, unmatched
nodes, and IL provenance. `AuthoredCorpusHarnessProcessTests.
Harness_BoundsStructuralReviewGapsWithoutDiscardingJsonEvidence` gates that
bounded-presentation/exhaustive-evidence boundary. An incomplete result is
never reported as "no structural changes."
This model exists only for node/span structure that the line-oriented
`CSharpDiffRow` cannot represent; it does not introduce another generic
diff-row hierarchy. Ordinary indented spans reuse the annotation comment gutter
and its stacking rules. Structural details render below their caret and start at
the first caret column while the comment marker remains in the shared gutter;
wrapped continuations keep that detail column. When a covered extent includes
indentation and a non-whitespace token, the display caret starts at that token;
whitespace-only extents preserve their exact geometry. Tab-indented extents
retain the source tab prefix in exact fallback caret and detail rows so the
renderer's tab stops remain aligned; a tabbed member indent also selects exact
fallback because it cannot establish a stable comment-gutter column for
differently indented lines. A resulting span too close to the left edge for the
gutter uses exact gutter-free caret and detail rows instead. Typed UTF-16 spans
are unchanged.
`CSharpStructuralComparisonTests.
RenderAnnotatedBody_IndentedExtentAlignsCaretToFirstCoveredToken`,
`RenderAnnotatedBody_TabIndentedExtentPreservesTabAlignment` and
`RenderAnnotatedBody_TabbedMemberIndentUsesExactFallback` gate this display-only
alignment; `AnnotationGestureTests.
AlignedDetailContinuationsShareTheFirstCaretColumn` gates continuation
alignment in the reusable renderer.

## Structured comparison lifecycle

Implementation evidence is one planned operation over a complete participant
and target population. C#, IL, body signals, retained Findings, authored
Source, and host-owned mechanisms do not build independent member lists and
join them through display identity afterward.

The ownership boundary is part of the model. Core-Queries acquisition
currencies never become Research currencies merely because one object contains
the same facts. In the target model, `DotnetInspector.ResearchQueries` is the
only production assembly authorized to compose both owners' internal
structured populations: core Queries already grants it friend access and
Research adds the reciprocal friend boundary. Other production assemblies may
reference both owners' public APIs, but those references confer no population-
construction authority. ResearchQueries owns the typed, bijective population
projection between the owners. The query-side model is:

```text
BodyEvidenceParticipantBinding
  Input                  exact ArtifactParticipantInputKey
  Side                   Before | After
  Authority              LogicalSlot(inert slot identity) |
                         DirectMemberDesignation(inert designation id)
  RoleManifest           AssemblyParticipantRoleManifest.Id
  AssemblyIdentity       name + culture + public-key token; version omitted
  Selection              immutable registration identity + MVID
  Body                   SameSelection |
                         Implementation(immutable registration identity + MVID) |
                         ReferenceOnly(inert proof)

BodyEvidenceParticipantDomain
  Key                    Admitted(ArtifactParticipantPairing.Id) |
                         EndpointAbsent(ComparisonEndpointPairingSlot.Id) |
                         Failed(EndpointSlotFailure(slot id) |
                         ParticipantPairingTerminal(endpoint-slot id,
                         pairing-outcome id, Ambiguous | Failed))
  EndpointSlot           originating ComparisonEndpointPairingSlot.Id
  Outcome                live admitted pairing | two-sided endpoint-absence
                         proof | typed terminal outcome with complete upstream
                         request/outcome or ambiguity/failure payload

BodyEvidenceParticipantPairingTerminalReceipt
  EndpointSlot           exact ComparisonEndpointPairingSlot.Id
  OutcomeId              exact slot-local pairing-outcome id
  Kind                   Ambiguous | Failed
  Inputs                 exact qualified participant-input-key set
  Payload                complete input-keyed
                         BodyEvidenceParticipantBinding summaries
  Reason/Diagnostic      exact inert terminal evidence

BodyEvidenceParticipantDomainReceipt
  Key                    exact BodyEvidenceParticipantDomain.Key
  EndpointSlot           originating ComparisonEndpointPairingSlot.Id
  Outcome                Admitted(pairing id + kind + exact before/after
                         BodyEvidenceParticipantBinding | proven Absent) |
                         EndpointAbsent(inert before/after proofs) |
                         Failed(inert EndpointSlotFailure |
                         BodyEvidenceParticipantPairingTerminalReceipt)

BodyEvidenceSelectionQuestionInput
  Id                     opaque user/host question identity
  Selection              product-owned typed selection input
  EndpointSlots          non-empty declared subset of the pairing plan

BodyEvidenceTargetSelectionRequest
  Request                Explicit(non-empty product-owned typed selectors) |
                         Enumerative(typed filters | All) |
                         Direct(designation id + exact before/after
                         DirectDesignatedMethodAddress values)
  Intent                 derived: Explicit | Enumerative

BodyEvidenceSelectionQuestion
  Id                     exact query-owned question identity
  Selection              exact BodyEvidenceTargetSelectionRequest
  EndpointSlots          non-empty sealed subset of the pairing plan

BodyEvidenceSelectionCorrelationId
  Value                  opaque query-owned correlation identity

BodyEvidenceSelectionCorrelationManifest
  Entries                 exact correlation id ->
                         question + non-empty query-domain-key set

BodyEvidenceSealedPopulation (internal)
  Operation              exact core-Queries owning operation id
  Questions               exact sealed query question set
  ParticipantDomains     exact owner-bound live query domain set
  ParticipantReceipts    exact inert query domain-receipt set
  Correlations            exact sealed query correlation manifest

BodyEvidencePopulationProjectionReceipt
  Operation              exact core-Queries operation id ->
                         ResearchBodyEvidenceOperationId bijection
  EndpointSlots          exact core slot-id -> Research endpoint-id bijection
  ParticipantInputs      exact core qualified-input-key ->
                         Research input-id bijection
  ParticipantBindings   exact core role-manifest id + side + input key ->
                         Research participant-id + binding-id bijection
  ParticipantDomains     exact query-domain key -> Research domain-id
                         bijection plus inert admitted pairing-kind and
                         before/after binding-summary-or-absence-proof
                         correspondence
  Questions              exact query-question-id ->
                         Research question-id bijection
  Correlations           exact query-correlation-id ->
                         Research correlation-id bijection
  DirectDesignation      direct-only core designation id ->
                         Research designation-id bijection
  TerminalPayloads       exact query terminal evidence ->
                         Research terminal-evidence-id bijection

BodyEvidencePlanReceipt
  Revision                projection-free completed-plan identity
  Questions               exact immutable query question set
  ParticipantDomains     exact immutable query domain-receipt set
  Correlations            exact immutable query correlation manifest
  PopulationProjection   exact inert projection correspondence receipt
  Research               exact ResearchBodyEvidencePlanReceipt
  Construction           fixed-size Publication wrapper over precharged,
                         already immutable collections
```

`ILInspector.Research` owns a disjoint set of non-convertible identities and
values. The internal admission plan may retain live Metadata sources only while
the Research session is current; its completed receipt is inert:

```text
ResearchBodyEvidenceOperationId
ResearchBodyEvidenceInputId
ResearchBodyEvidenceBindingId
ResearchBodyEvidenceParticipantId
ResearchBodyEvidenceEndpointId
ResearchBodyEvidenceDomainId
ResearchBodyEvidenceQuestionId
ResearchBodyEvidenceCorrelationId
ResearchBodyEvidenceTerminalEvidenceId
ResearchBodyEvidenceSelectionScopeId
ResearchBodyEvidenceTargetRequestId
ResearchBodyEvidenceTargetAttemptId
ResearchBodyEvidenceWorkItemId
ResearchBodyEvidenceDirectDesignationId
  Construction           non-defaultable Research-owned opaque identities
  Minting                Research internal factories only

ResearchBodyEvidenceLiveBinding
  Id                     exact ResearchBodyEvidenceBindingId
  Input                  exact ResearchBodyEvidenceInputId
  Side                   Before | After
  Participant            exact ResearchBodyEvidenceParticipantId
  Selection              live immutable registration + MetadataSource + MVID
  Body                   SameSelection |
                         Implementation(live registration + source + MVID) |
                         ReferenceOnly(inert proof)
  Lifetime               current Research session only

ResearchBodyEvidenceAdmittedSide
  Value                  Binding(exact ResearchBodyEvidenceLiveBinding) |
                         Absent(inert proof)

ResearchBodyEvidenceParticipantDomain
  Id                     exact ResearchBodyEvidenceDomainId
  Endpoint               exact ResearchBodyEvidenceEndpointId
  Outcome                Admitted(Paired | BeforeOnly | AfterOnly +
                         exact before/after
                         ResearchBodyEvidenceAdmittedSide) |
                         EndpointAbsent(inert proofs) |
                         ParticipantFailed(
                         ResearchBodyEvidenceTerminalEvidenceId +
                         inert terminal evidence)
  Admitted validity      Paired = Binding/Binding;
                         BeforeOnly = Binding/Absent;
                         AfterOnly = Absent/Binding

ResearchBodyEvidenceSelectionQuestion
  Id                     exact ResearchBodyEvidenceQuestionId
  Selection              Explicit(non-empty typed selectors) |
                         Enumerative(typed filters | All) |
                         Direct(Research participant/designation ids + exact
                         before/after addresses and relationship roles)
  Intent                 derived: Explicit | Enumerative
  Endpoints               exact non-empty Research endpoint-id set

ResearchBodyEvidenceSelectionCorrelation
  Id                     exact ResearchBodyEvidenceCorrelationId
  Question               exact Research question
  Domains                non-empty exact Research domain-id set

ResearchBodyEvidenceAdmissionPlan (internal)
  Operation              exact ResearchBodyEvidenceOperationId
  Questions               exact projected Research question set
  ParticipantDomains     exact projected Research domain set
  ParticipantReceipts    exact projected inert Research domain-receipt set
  Correlations            exact projected Research correlation set
  CorrespondenceToken    operation-stamped, projection-only validation token

ResearchBodyEvidenceSelectionScope
  Id                     opaque comparison-scoped Research identity
  Correlation            exact Research correlation id
  Domain                 exact Research domain id
  Before/After           for admitted domains only:
                         Selected(request ids) | Absent(proof) | Failed

ResearchBodyEvidenceSelectionScopeReceipt
  Id                     exact Research selection-scope id
  Correlation            exact Research correlation id
  Domain                 exact Research domain id
  Before/After           for admitted domains only, exact inert
                         Selected(request ids) | Absent(proof) | Failed

ResearchBodyEvidenceTargetRequest
  Id                     opaque side-local Research request identity
  Scope                  owning Research selection scope
  Participant            exact Research participant and binding ids
  Target                 Exact(address, role) | Carried

ResearchBodyEvidenceTargetAttempt
  Id                     opaque Research plan identity
  Request                originating Research request id
  Participant            exact Research participant and binding ids
  Outcome                Resolved | Bodyless |
                         Unavailable(ImplementationRoleUnavailable | other) |
                         Rejected | Ambiguous | Failed

ResearchBodyEvidenceCoordinate
  Domain                 ResearchBodyEvidenceDomainId
  Key                    MemberBodyCorrespondenceKey
  Role                   Method | Getter | Setter | Adder | Remover

ResearchBodyEvidenceWorkItem
  Id                     exact ResearchBodyEvidenceWorkItemId
  Key                    Corresponded | DesignatedPair |
                         CorrespondenceAmbiguous |
                         CounterpartUnavailable | SelectionFailed |
                         ResolutionFailed | ParticipantFailed
  AttemptIds             selected-target aliases; empty only for
                         selection/participant failure
  Evidence               exact projected bindings, resolved entries, proofs,
                         failures, and terminal payloads for the key arm

ResearchBodyEvidenceComparisonPlan (internal)
  Operation              exact ResearchBodyEvidenceOperationId
  Admission               exact ResearchBodyEvidenceAdmissionPlan
  SelectionScopes         sealed terminal or side-outcome scopes
  WorkItems               private resolved/failed union
  AttemptMap              every Research target-attempt id ->
                         exactly one Research work-item id
  Before/After           private Research bindings, entries, and failures

ResearchBodyEvidenceComparisonSession (internal)
  Operation              exact ResearchBodyEvidenceOperationId
  Plan                    ResearchBodyEvidenceComparisonPlan
  RequestedMechanisms     closed set declared before projection
  Dependencies            acyclic same-work-item prerequisite graph
  Ledgers                 validated inert, budget-charged
                         synchronous/asynchronous projections
  Project/ProjectAsync    total per-work-item callbacks that lower and charge
                         before retaining each disposition
  CompleteResearch        one atomic zero-copy Research validation/sealing
                         after every charged inert receipt, ledger, snapshot,
                         change, and presentation entry exists

ResearchBodyEvidenceParticipantBinding
  Id                     exact Research binding id
  Input                  exact Research input id
  Side                   Before | After
  Participant            exact Research participant id
  AssemblyIdentity       name + culture + public-key token; version omitted
  Selection              immutable registration identity + MVID
  Body                   SameSelection |
                         Implementation(immutable registration identity + MVID) |
                         ReferenceOnly(inert proof)

ResearchBodyEvidenceParticipantSideReceipt
  Value                  Binding(exact
                         ResearchBodyEvidenceParticipantBinding) |
                         Absent(inert proof)

ResearchBodyEvidenceParticipantDomainReceipt
  Id                     exact Research domain id
  Endpoint               exact Research endpoint id
  Outcome                Admitted(Paired | BeforeOnly | AfterOnly +
                         exact before/after
                         ResearchBodyEvidenceParticipantSideReceipt) |
                         EndpointAbsent(inert proofs) |
                         ParticipantFailed(exact terminal-evidence id +
                         inert terminal evidence)

ResearchBodyEvidencePlanReceipt
  Operation              exact inert ResearchBodyEvidenceOperationId
  Revision                exact Research completed-plan identity
  Questions               exact immutable Research question set
  ParticipantDomains     exact immutable Research domain receipt set
  Correlations            exact immutable Research correlation set
  SelectionScopes         complete immutable Research scope receipt set
  WorkItemSet             opaque immutable validated Research set identity
  Construction           fixed-size ResearchCompletion wrapper over
                         precharged, already immutable collections
```

Research owns mechanisms and evidence; ResearchQueries owns operation
orchestration and query publication:

```text
BodyEvidenceNativePayloadSnapshot<T>
  Mechanism               exact catalog mechanism id
  Value                   catalog-declared closed immutable typed DTO

BodyEvidenceProducerWorkEstimate
  Phase                  exact catalog-declared producer phase
  InputUnits              checked conservative admitted-input units
  WorkUnits               checked conservative producer operations
  PeakScratchBytes        checked maximum live producer-owned scratch

BodyEvidenceProducerWorkPlan
  Phase                  exact current producer phase
  Entries                 every phase-eligible
                         (Research work-item id, mechanism id) -> exact estimate
  InputUnits              checked complete entry sum
  WorkUnits               checked complete entry sum

BodyEvidenceProducerWorkReservation
  Mechanism               exact catalog mechanism id
  WorkItem                exact Research work-item id
  Estimate                exact charged estimate
  Lifetime                current callback through native-result release

BodyEvidenceComparedDisposition<T>
  Verdict                 Exact | Different
  Authority               Producer | SessionBodyPresence
  Native                  optional BodyEvidenceNativePayloadSnapshot<T>
  BodyPresence            None | BodyAdded | BodyRemoved

BodyEvidenceMechanismLedger<T>
  Mechanism               requested mechanism descriptor
  Dispositions            exactly one Compared/Absent/Failed per Research
                         work-item id

BodyEvidencePresentationMap
  Entries                 exactly one inert participant/member label group per
                         Research work-item id
  Construction           precharged entries freeze in place before
                         ResearchCompletion; completion performs no copy

ResearchBodyEvidenceProjectionBudgetFacet
  Operation              exact ResearchBodyEvidenceOperationId
  Backing                internal ResearchQueries adapter over the exact
                         query-owned concrete plan ledger
  Authority              Research-declared projection dimensions only
  Construction           minted with the Research operation id during
                         PopulationProjection; never accepts a core query id

BodyEvidencePlanBudgetLeaseSet
  QueryOperation          exact core-Queries owning operation id
  Endpoint                planned-only IComparisonEndpointBudgetLease
  DirectPairing           direct-only IDirectParticipantPairingBudgetLease
  SourceCleanup           planned-only
                         IQueryCleanupDiagnosticReservationBudgetLease
  Projection              planned/direct
                         ResearchBodyEvidenceProjectionBudgetFacet

BodyEvidenceOperationBudgetLeaseSet
  QueryOperation          exact core-Queries owning operation id
  Plan                    BodyEvidencePlanBudgetLeaseSet
  AuthoredSource          planned-only IAuthoredSourceBudgetLease

BodyEvidenceStageOperationFailure
  Value                  QueryFailure(exact
                         ImplementationComparisonQueryFailurePhase) |
                         DirectFailure(exact
                         DirectImplementationComparisonFailurePhase)

BodyEvidenceStageItemResult
  Value                  None |
                         DependentInputOutcome(Snapshot |
                         terminal Source ledger disposition) |
                         ProducerLedgerDisposition

BodyEvidenceStageCancellation
  Value                  NotApplicable | PropagateAfterCleanup |
                         PropagateIfCleanOrCleanupFailureSupersedes

BodyEvidenceCleanupDiagnosticSlot
  Resource               exact operation-local owned-resource identity
  Diagnostic             fixed typed code + bounded non-artifact text range
  Reservation            slot and complete text capacity charged before
                         ownership transfer
  Consumption            zero or one cleanup diagnostic; no growth/allocation

BodyEvidenceOperationStageDescriptor
  Id                     Admission | QuestionSealing |
                         EndpointRealization | ParticipantPairing |
                         PopulationSealing | PopulationProjection |
                         PlanExpansion |
                         PrerequisiteProducerPreflight |
                         PrerequisiteProjection |
                         DependentInputAcquisition |
                         DependentProducerPreflight |
                         DependentProjection | ResearchCompletion |
                         Publication | Cleanup
  AppliesTo              Planned | Direct | Both
  Owners                 exact operation-kind -> ResearchQueries |
                         EndpointOwner | AcquisitionCoordinator |
                         DirectPairingFactory | SourceAcquisitionOwner |
                         Research map
  BudgetAuthorities      exact operation-kind -> closed set of Ledger |
                         EndpointFacet | DirectPairingFacet |
                         SourceCleanupFacet | ProjectionFacet |
                         AuthoredSourceFacet authorities
  Dimensions             exact operation-kind ->
                         closed stage-authorized budget-dimension set map
  OperationFailures      exact operation-kind ->
                         BodyEvidenceStageOperationFailure map
  ItemResults            exact operation-kind ->
                         BodyEvidenceStageItemResult map
  Cancellation           exact operation-kind ->
                         BodyEvidenceStageCancellation map

BodyEvidenceOperationStageCatalog
  Entries                 exact descriptor set in required execution order
  Planned                 exact applicable ordered stage-id set
  Direct                  exact applicable ordered stage-id set

BodyEvidenceProducerPhase
  Id                     Prerequisite | Dependent
  Mechanisms             exact catalog-derived mechanism-id set
  Dependencies           exact completed-ledger prerequisites

DirectMemberDesignationId
  Value                  opaque core-Queries authority identity
  Construction           non-defaultable; internal core-Queries constructor
  Minting                ResearchQueries direct operation only

DirectDesignatedSourceId
  Value                  opaque core-Queries designation-local source identity
  Scope                  exact DirectMemberDesignationId + Before | After
  Construction           non-defaultable; internal core-Queries constructor
  Minting                ResearchQueries designation factory only

DirectMemberPairingDesignation
  Id                     exact DirectMemberDesignationId
  Before/After            exact DirectDesignatedSourceId +
                         live MetadataSource + MVID
  Authority               source-bounded explicit comparison grant
  Construction            sealed; only direct operation factory can mint
  Lifetime                cannot outlive either supplied source

DirectDesignatedMethodAddress
  Designation            exact DirectMemberDesignationId
  Source                 exact DirectDesignatedSourceId + Before | After
  Address                exact MetadataMethodAddress
  Role                   exact RelationshipRole
  Construction           designation factory only, from the exact designated
                         MetadataSource object + MethodDefinitionHandle
  Invariant              factory validates source object identity and row,
                         then creates Address from that source's reader

DirectMemberComparisonInput
  Pairing                 DirectMemberPairingDesignation
  Before                  exact Before DirectDesignatedMethodAddress
  After                   exact After DirectDesignatedMethodAddress
  Mechanisms             non-empty catalog-id subset of C# | IL only

DirectMemberComparisonRequestReceipt
  Designation            exact inert DirectMemberDesignationId
  Before                  exact inert DirectDesignatedSourceId +
                         MetadataMethodAddress + relationship role
  After                   exact inert DirectDesignatedSourceId +
                         MetadataMethodAddress + relationship role
  Mechanisms             exact requested C#/IL catalog-id set

DirectImplementationComparisonCompleted
  Request                 exact DirectMemberComparisonRequestReceipt
  Receipt                 exact BodyEvidencePlanReceipt
  Research                exact ResearchBodyEvidenceComparison
  BeforeSubject           Resolved(ResearchSubjectKey) | Failed
  AfterSubject            Resolved(ResearchSubjectKey) | Failed
  WorkItems               non-empty complete Research work-item results
  Ledgers                 complete non-empty requested C#/IL ledgers
  Native                  complete inert native-payload snapshot map

DirectImplementationComparisonFailurePhase
  Value                  Admission | QuestionSealing | ParticipantPairing |
                         PopulationSealing | PopulationProjection |
                         PlanExpansion | Projection | ResearchCompletion |
                         Publication | Cleanup

DirectImplementationComparisonFailure
  Phase                  exact DirectImplementationComparisonFailurePhase
  Reason                 closed typed operation-failure reason
  Budget                 optional fixed-size dimension, limit, and charge
  Diagnostics            fixed-size typed primary diagnostic plus fixed-size
                         view over precharged cleanup diagnostic slots;
                         no artifact text

ImplementationMemberDiffEvidence
  Completed              DirectImplementationComparisonCompleted
  Failed                 DirectImplementationComparisonFailure

ImplementationMemberDiffResult
  Operation              exact core-Queries owning operation id
  Evidence               exact closed evidence arm
  Outcome                Completed reduction |
                         Failed -> Unavailable
  HasFailures             Completed: derived from complete ledgers |
                         Failed: true
  AbsenceReasons          Completed-only typed non-failing details
  Diagnostics             Completed ledgers or Failed operation diagnostic
```

`BodyEvidenceParticipantBinding` is a completed-result snapshot, not an
`AssemblyContextParticipant` or role-binding object. Registration, MVID,
role-manifest id, logical-slot/designation authority identity, assembly
identity, qualified input key, and a `ReferenceOnly` proof are immutable
identity/evidence values; none opens bytes, resolves metadata, names a live
group, or carries a callback/lease. The retained authority identity records
origin only; it cannot reconstitute a logical-slot or direct-designation grant.

The internal `BodyEvidenceParticipantDomain` may retain a live admitted pairing
only while the query lease is current. `PopulationSealing` lowers every
internal query domain to a distinct
`BodyEvidenceParticipantDomainReceipt` before sealing the
`BodyEvidenceSealedPopulation`. An admitted receipt snapshots each non-absent
side into one `BodyEvidenceParticipantBinding`; a terminal participant-pairing
receipt snapshots every candidate/affected input into its exact input-keyed
binding summary. Endpoint failure and absence proofs must already be inert.
Lowering charges every new receipt entry and character before retention, and
rejects a missing, extra, substituted, wrong-side, or live-backed snapshot. The
sealed query receipt set is carried unchanged through projection and
publication.

`PopulationProjection` is the sole bridge from that query population into
Research. `DotnetInspector.ResearchQueries` consumes the complete
`BodyEvidenceSealedPopulation` and invokes an internal Research factory to mint
one non-defaultable `ResearchBodyEvidenceOperationId`. ResearchQueries first
charges the operation projection/correspondence entry and retains the exact
core-query-operation to Research-operation bijection. It then creates the
Research-owned projection facet carrying only that Research id; the
ResearchQueries adapter backs the facet with the exact query ledger but does
not expose the ledger or core operation id to Research.

The same projection invokes internal Research factories to mint a
`ResearchBodyEvidenceAdmissionPlan`. It copies each live admitted binding into
a Research-owned live binding, copies each admitted absent arm into a
Research-owned inert absence proof, preserves the exact admitted pairing kind,
and copies each terminal outcome into a Research-owned inert terminal value.
It simultaneously lowers each Research live domain into its distinct inert
`ResearchBodyEvidenceParticipantDomainReceipt`. It does so only after charging
every Research admission value, inert receipt entry, correspondence-map entry,
and retained request/receipt character in each new copy. It cannot pass through
an
`ArtifactParticipantPairing`, `ArtifactParticipantInputKey`,
`AssemblyParticipantRoleManifest.Id`, `EndpointSlotFailure`, or
`DirectMemberDesignationId` or `DirectDesignatedSourceId`.

The projection simultaneously seals
`BodyEvidencePopulationProjectionReceipt`. Exact set equality is required for
endpoint slots, qualified inputs, participant/binding pairs, role manifests,
domains, questions, correlations, terminal payloads, and the direct
designation when present. Each
map is a bijection in its declared target currency: no core identity may have
two images in one map, no Research identity may have two core antecedents, and
every Research admission value must have an entry. The participant/binding map
issues exactly one Research participant id and one Research binding id for each
projected core binding; neither identity is minted or charged separately from
that entry. The participant-domain map additionally proves the admitted pairing
kind and each side's exact binding-or-absence arm; a missing binding never
serves as absence evidence. Payload validation additionally proves side, MVID,
registration, assembly identity, role, terminal kind, reason, diagnostic,
proof, selection request, derived intent, and endpoint scope equality. For the direct request, each designation-bound source id must name the exact
query binding from which its Research participant was projected, and the exact
addresses and roles must also agree. The Research participant ids are the only
permitted enrichment: each must be the projected image of the exact query
binding named by the admitted direct pairing. Equal
text or equal numeric payloads never establish correspondence. The receipt
retains only both sides' inert identities and snapshots; it cannot reconstruct
either live currency.

`ILInspector.Research` defines the admission values and internal session but
references no Queries assembly. Its project grants
`InternalsVisibleTo("DotnetInspector.ResearchQueries")`; that one-way friend
access lets the already-downward `ResearchQueries -> Research` project edge
invoke internal factories without adding an upward project reference.
ResearchQueries separately uses its existing core-Queries friend access to
read the acquisition snapshots it must lower. Those two production friend
grants, not project-reference exclusivity, authorize composition. A source-
architecture gate rejects another production friend or call site for either
internal structured-population boundary, a public construction path that
bypasses those boundaries, an
`ILInspector.Research -> DotnetInspector.Queries` or
`ILInspector.Research -> DotnetInspector.ResearchQueries` reference, and any
Research-owned public or internal model whose field type is a core-Queries
currency.

The operation mapping follows the same boundary rule. Research receives only
`ResearchBodyEvidenceOperationId`; its admission plan, projection facet,
comparison plan, session, and completion receipt must all carry that exact id.
Publication is the only owner that can compare it with the core query operation
through the inert projection receipt. A missing, substituted, duplicated, or
wrong-operation mapping rejects before Research plan expansion or publication.

Producer-native comparison objects are callback-local values, not ledger or
completed-result payloads. Each mechanism catalog entry declares one typed
snapshot DTO, one internal lowering operation, one producer phase, and one
conservative typed work estimator. A prerequisite estimator uses only already
admitted bounded metadata/body size facts. A dependent estimator uses only
already admitted bounded dependent-input facts produced after its prerequisite
ledgers complete. Neither may decode, canonicalize, decompile, inspect
Findings, diff, or otherwise perform the work it estimates. Each estimate
includes every producer-owned decode and canonicalization allocation implied by
those raw facts.

After plan sealing and before invoking any prerequisite producer, the session
computes the complete prerequisite `BodyEvidenceProducerWorkPlan`. After every
prerequisite ledger is complete and the exact dependent population and bounded
dependent inputs exist, it separately computes the complete dependent work
plan before invoking any dependent producer. Each phase validates exact set
equality with its catalog-derived eligible population and atomically charges
the checked aggregate input/work totals against the same cumulative operation
ledger. Unknown, overflowing, omitted, duplicate, or rejected estimates fail
before that phase's first callback begins; each plan also rejects any entry
whose peak scratch exceeds the operation cap. Projection then uses the current Research-operation-stamped projection facet
to obtain one
`BodyEvidenceProducerWorkReservation` for the entry's peak scratch immediately
before invoking that producer. Concurrent execution waits for scratch capacity
or propagates cancellation; ordinary reservation contention does not become
budget exhaustion or alter dispositions.

The sealed work plan owns the cumulative input/work charges. Each reservation
holds its peak-scratch bytes against the operation's live total through callback
completion, typed lowering, and native-result release. Before a producer
disposition enters its ledger, projection invokes the catalog lowering,
measures and charges the snapshot, retains only
`BodyEvidenceNativePayloadSnapshot<T>`, and releases the native object. A
`finally` path releases the peak-scratch reservation after success, producer
failure, lowering failure, or cancellation; cumulative input/work charges are
never replenished. At most the current reserved callback's native result exists
outside an inert snapshot. `Complete` receives only already-lowered, budgeted
ledgers and performs no first-time payload lowering.

The catalog, not a host or producer callback, closes the permitted payload-type
set. Snapshot DTOs are immutable value-only evidence: they may contain typed
ids, enums, numbers, strings, immutable byte/value arrays, and nested approved
DTOs, but no reader, source, participant, role object, workspace/session,
callback, lease, stream, `ContentRef`, or other content-opening/selection
authority. A failed disposition's presentation-safe partial evidence uses the
same immediate snapshot boundary. The payload-closure gate is a build/test
source-architecture check; the product path uses compile-time typed lowering
and performs no reflection, dynamic loading, or runtime graph walk, preserving
NativeAOT and Browser/Wasm.

The query plan, Research admission plan/session, manifest entries, live
pairings and Research bindings, callbacks, participant objects, and
producer-native objects remain internal. Research completes one inert
`ResearchBodyEvidenceComparison`; it cannot publish a query result. Query
publication in ResearchQueries validates the already sealed query receipt and
Research receipt against the population-projection receipt, then atomically
publishes the outer
`ImplementationDiffResult`. The result retains the query receipt, correspondence
receipt, complete Research scope outcomes and ledgers, inert native payload
snapshots, and the total presentation map.

### Target attempts and work-item totality

Before acquisition, the host declares a non-empty
`BodyEvidenceSelectionQuestionInput` set against the endpoint-pairing plan. The
host remains authoritative for question identity, typed selection input, and
endpoint scope, but cannot construct an immutable question. The query-owned
sealer requires the current concrete ledger, charges every input and retained
payload, and only then copies the complete set into immutable
`BodyEvidenceSelectionQuestion` values. Each sealed question owns one exact
immutable typed selection request and a non-empty set of endpoint slot ids.
The request projects into the closed Research-owned union: explicit member
selectors, enumerative typed filters/`All`, or the internal direct
Research-participant/address/role selection. `Intent` is derived from that arm
rather than restated by the host. No parallel selector or pre-sealed host
question list exists.
The union of every question's endpoint-slot set must equal the pairing plan's
slot set. Several questions may name one slot, but no plan slot may be named by
zero questions. Validation rejects an unknown or uncovered slot and an
omitted, duplicate, rekeyed, or substituted question or request. A question
cannot disappear before acquisition, change the selection it asks, or acquire
a different endpoint scope after an endpoint fails.

After endpoint and participant pairing, ResearchQueries enters
`PopulationSealing` and seals one
`BodyEvidenceParticipantDomain` set by exhaustively lowering the acquisition-
owned `ComparisonEndpointPairingSlotOutcomeSet`. Every
`EndpointAbsent` slot outcome contributes one `EndpointAbsent` domain retaining
both proofs. Every failed slot outcome contributes one
`Failed(EndpointSlotFailure)` domain retaining the exact request-keyed outcome
map, exact side-keyed absent-arm proof map, every terminal reason/diagnostic,
and any realized opposite's tainted manifest revision/input-key summary. Inside
a `Participants` slot outcome, every
`ArtifactParticipantPairingOutcome.Admitted` contributes one `Admitted` domain
using its `ArtifactParticipantPairing.Id`; only `Paired`, `BeforeOnly`, and
`AfterOnly` can enter that arm. Each non-absent binding's qualified input-key
side must match its `Before`/`After` arm; query lowering revalidates this
acquisition invariant rather than trusting arm position. Every `Ambiguous` or
`Failed` participant outcome contributes one
`Failed(ParticipantPairingTerminal)` domain keyed by the exact
`(endpoint-slot id, pairing-outcome id, outcome kind)` composite and retaining
its typed reason, diagnostic, and complete upstream candidate/affected-input
payload. The slot component makes independently slot-local outcome ids
comparison-unique without changing acquisition's id scope. No downstream layer
reconstructs those facts from provenance or side bindings. Every domain
retains its originating endpoint-slot id, and every slot expands to at least
one domain.

Exact set equality connects the endpoint plan, slot-outcome set, each
participant outcome set's qualified input partition (the exact manifest union
on planned paths and the two side-qualified designation inputs on the direct
path), and participant domains. A failed slot's requested endpoint keys must
equal its request-keyed outcome map, and its terminal-key subset must be exact
and non-empty. Its side-keyed absent-arm map must equal exactly the slot's
`Absent(proof)` arms. Validation rejects an omitted, duplicated, rekeyed,
reparented, empty, overlapping, or success-shaped terminal outcome or a
missing, extra, substituted, or wrong-side absence proof. A failed endpoint or
ambiguous participant outcome therefore needs no invented participant binding
to remain in the question population.

During the same `PopulationSealing` stage, ResearchQueries seals one
`BodyEvidenceSelectionCorrelationManifest` from the complete declared
question set and the sealed participant-domain set, and lowers every query
domain to its charged inert receipt. A correlation retains its
exact question, including its immutable selection request, rather than
restating its selector, intent, or endpoint scope. Its non-empty domain map must
equal every sealed participant domain whose originating slot is in that
question's endpoint-slot set. This mechanical expansion admits no host-chosen
“applicable subset”: an admitted pairing and a failed slot are equally
impossible to omit. Exact question/correlation equality rejects a question or
selection request that disappears or changes before query input. For a
well-behaved non-CLI host, the pre-acquisition question-input set is that host's
authoritative declaration. The query-sealed questions must be its exact
budgeted immutable copy. The product does not attempt to recover an input the
host never declared.

ResearchQueries then performs `PopulationProjection` over the complete
`BodyEvidenceSealedPopulation`. The resulting
`ResearchBodyEvidenceAdmissionPlan` has exactly one Research question, domain,
inert domain receipt, and correlation for every query-side antecedent
and no other values. Its
Research-operation-stamped correspondence token permits the Research session
to accept that plan once; it exposes no core id, pairing, role
manifest/binding, endpoint failure, direct designation, or designated source
id. The admission plan, token, and projection facet must carry the same
`ResearchBodyEvidenceOperationId`. A missing, extra, duplicated, wrong-side,
wrong-operation, rekeyed, or payload-divergent projection fails before Research
plan expansion.
Every admitted Research domain already carries the projected pairing kind and
an explicit `Binding | Absent(proof)` arm for each side; Research never infers
absence from a missing binding.

Research mints one `ResearchBodyEvidenceSelectionScope` for every
`(Research correlation id, Research domain id)` entry. This preserves
question-local failure and absence domains even when several selectors apply
to the same failed endpoint slot. Selection fills side outcomes only for an
`Admitted` Research scope. An `EndpointAbsent` domain seals a terminal
successful-absence scope with its two proofs. A `ParticipantFailed` domain
seals a terminal scope with the same projected terminal identity and
diagnostic. Neither terminal arm fabricates a participant, side inventory, or
target request. An admitted scope records an independent outcome for each
side:

- `Selected` contains the complete non-empty set of side-local exact/carried
  target-request ids admitted by that side's inventory;
- `Absent` carries typed proof that the complete side-local inventory contains
  no selected target;
- `Failed` carries the selection, inventory, or participant diagnostic that
  prevented the side from proving either selected targets or absence.

During `PlanExpansion`, Research charges the live scope population before
selection. After every scope outcome and the complete work-item population are
known, it derives all inert
`ResearchBodyEvidenceSelectionScopeReceipt` values and base
`BodyEvidencePresentationMap` entries without retaining them, atomically
charges their complete entry and character counts, and only then retains them.
Each base presentation entry contains only the participant/member labels known
from the sealed plan. Prerequisite and dependent projection may retain
catalog-declared inert presentation details in their typed snapshot or
disposition entries only after separately charging those entries and
characters; neither completion nor publication copies those values.

The query and Research plans each require one correlation for every question
and one question for every correlation. Research scopes are separately
bijective with the projected `(Research correlation id, Research domain id)`
pairs. A query domain and its Research projection may therefore appear in
several correlations, but complete question-set slot coverage requires every
domain on both sides of the boundary to appear in at least one. The outer
receipt requires the same query and Research question/domain/correlation sets
through the projection bijections and the complete Research scope set. Every
scope appears in exactly one Research correlation with the same domain id; a
scope cannot restate or disagree with its question or domain. Empty
correlations, an undeclared or wrong-slot domain, a missing expanded domain,
missing or extra scopes, duplicate correlation-local domains, altered
selection/derived intent/endpoint scope, reparented scopes, and a receipt whose
query, projection, or Research sets differ reject before publication. An
all-failed endpoint population is therefore a valid non-empty Research
population whose scopes all retain participant failures, not a reason to omit
the query.

At completion, Research validates and seals a fixed-size
`ResearchBodyEvidencePlanReceipt` wrapper over the already charged immutable
question, participant-domain-receipt, correlation, scope-receipt, and work-item
sets. At publication, ResearchQueries validates the query/Research key and
payload equality and seals a fixed-size `BodyEvidencePlanReceipt` wrapper over
the already charged query, correspondence, and Research values. Neither stage
copies an entry, string, byte payload, or presentation value. The admitted core
pairing object, live query role manifests/bindings, live Research bindings, and
live terminal candidate entries enter neither completed receipt. The query
receipt intentionally retains only the inert pairing id, binding snapshots, and
terminal candidate/affected-input summaries needed for correspondence and
failure evidence. An `EndpointAbsent` scope or admitted scope whose sides are
both `Absent(proof)` intentionally creates no body work item or mechanism
ledger because there is no selected body coordinate, but it remains a completed
absence proof in the immutable result.
A `ParticipantFailed` scope creates one terminal participant-failed Research
work item under its question-local Research scope and failed-domain id; the
work item retains the exact projected terminal outcome, complete upstream
payload, every reason, and every diagnostic. Consumers never infer absence
from an empty work-item set.

An `Explicit` request means its retained typed selectors are expected to name
at least one target across its correlation's complete scope map. An
`Enumerative` request means its retained typed filters/`All` may validly select
zero targets. The internal direct arm is explicit and binds the projected Research question
to its Research participant/designation ids, exact before/after method
addresses and roles from `DirectMemberComparisonInput`. The requested C#/IL
set remains separate operation input: it is sealed in
`DirectMemberComparisonRequestReceipt` and becomes the Research session's
`RequestedMechanisms`, not query/Research selection currency. The
designation-bound source ids validate the query bindings but do not enter
Research.
Row filters, formatters, and mechanism selection cannot change the request or
its derived intent.

Absence disclosure follows the correlation's typed intent, not presentation row
count. The CLI handles an `Explicit` correlation through its typed
selector/no-match outcome only when every one of that correlation's scopes is
either `EndpointAbsent` or an admitted, two-sided `Absent` scope. One selected,
selection-failed, or participant-failed scope prevents a false no-match from a
successful empty sibling scope. An `Enumerative` correlation with only
successful empty scopes may remain silent while all of its proofs remain
queryable in the receipt; a failed domain is still a mandatory failure work
item and diagnostic. The evidence layer does not invent a body work item,
mechanism disposition, or semantic add/remove for a proven-empty scope.
A `ReferenceOnly` target remains `Selected` and ends in a typed unavailable
attempt, so it also prevents no-match and must retain its failure work item,
ledgers, proof, and diagnostic.

`CorrelationId` groups scopes produced by one user's question but never acts as
MethodDef identity. One logical selector spanning several participant pairs
produces distinct scopes. Explicit unscoped `All` seals each admitted
participant-local scope only after its full side-local MethodDef inventories
have been enumerated; a failed domain is already terminal and is never
enumerated. A failed or prematurely ended admitted-domain enumeration is
`Failed`, never `Absent` or a shortened `Selected` set.

Every request belongs to exactly one `Selected` scope side and binds one target
to that Research participant side's exact projected binding. Selection
enumerates only the binding's `Selection` participant. Research mints exactly
one target-attempt id for each request before exact/carried body resolution. It
never fans one request or strict target across sides, participants, or binding
generations. A selection-scope failure creates a `SelectionFailed` work item
without inventing a target request. This granularity is deliberate:

- corresponding before/after attempts in a complete, failure-free,
  unambiguous scope whose version-neutral keys agree map to one two-sided work
  item;
- signature drift in a complete, failure-free, unambiguous scope produces
  separate one-sided
  remove/add work items with separate side-local requests and attempt ids;
- a `Selected` side opposite an explicit `Absent` side produces proven
  one-sided work items;
- a `Selected` side opposite `Failed` produces no semantic one-sided work
  item;
- overlapping selectors resolving the same participant, exact address, strict
  key, correspondence key, and role **within the same scope** may alias one
  work item while retaining every attempt id. Independent scopes remain
  distinct proof domains even when they select the same physical methods.

Each carried request resolves only inside the exact body participant named by
its same-side role binding: either the selection participant itself or the
workspace-designated implementation participant. It never searches another
group or reconstructs a `ref`/`lib` relationship. A `ReferenceOnly` binding
still receives its pre-minted request and attempt, but the query completes that
attempt as `Unavailable(ImplementationRoleUnavailable)` with the retained
workspace proof and does not invoke Metadata. This is unavailable body
evidence, not selection `Absent`, `Bodyless`, or semantic body removal.

Before and after requests therefore carry independently minted strict keys.
An exact request is valid only for `SameSelection`; a distinct implementation
role uses `Carried` so a surface address never addresses the implementation
module. Each exact request carries its own relationship role and validates that
role against its side-local body participant before entering the resolved
index.
Only after both resolve does `MemberBodyCorrespondenceKey` decide whether they
share a work item. AssemblyRef-version-only drift reaches correspondence rather
than failing because one side was asked to resolve the other's strict key.

`ResearchBodyEvidenceWorkItem.Key` is a closed Research-owned union:

```text
CorrespondedKey
  ResearchBodyEvidenceSelectionScopeId +
  ResearchBodyEvidenceDomainId
  MemberBodyCorrespondenceKey + RelationshipRole

DesignatedMemberPairKey
  ResearchBodyEvidenceDirectDesignationId
  Before MetadataMethodAddress + RelationshipRole
  After MetadataMethodAddress + RelationshipRole

CorrespondenceAmbiguousKey
  ResearchBodyEvidenceSelectionScopeId +
  ResearchBodyEvidenceDomainId + side
  MemberBodyCorrespondenceKey + RelationshipRole

CounterpartUnavailableKey
  ResearchBodyEvidenceSelectionScopeId +
  affected ResearchBodyEvidenceTargetAttemptId

SelectionFailedKey
  ResearchBodyEvidenceSelectionScopeId + side

ResolutionFailedKey
  ResearchBodyEvidenceTargetAttemptId

ParticipantFailedKey
  ResearchBodyEvidenceSelectionScopeId +
  ResearchBodyEvidenceDomainId
```

The key does not infer identity; it records the result of prior participant and
body correspondence. For an admitted domain,
`ResearchBodyEvidenceDomainId` is the Research-owned two-sided or proven-one-
sided pair axis projected from the query's admitted pairing; side-local
`ResearchBodyEvidenceParticipantId` values remain only on bindings, requests,
and attempts. Before/after resolved attempts with the same
`CorrespondedKey` map to one work item only after each side's bucket contains
one unique strict target. Signature drift has different correspondence keys
and therefore different work items. Overlapping selectors that resolve to the
same exact address, strict key, correspondence key, and role add attempt
aliases to that item only inside one scope. The same coordinate selected by
another scope produces another work item; duplicate physical work is preferable
to letting one question's failure, ambiguity, or absence proof alter another
question. Resolution failures remain per-attempt and cannot collapse because
two failures have similar diagnostics.

Semantic correspondence requires a complete, failure-free, unambiguous key
census for both selected sides of the scope. If selection fails on one side,
any selected target fails before producing a validated correspondence key, or
distinct strict targets on one side collide on one correspondence key, the
dependent opposite census cannot prove correspondence, uniqueness, or absence.
Each side-local collision bucket becomes one
`CorrespondenceAmbiguousKey`, retains every colliding attempt id, exact
address, strict key, and diagnostic, and receives terminal
`Failed(CorrespondenceAmbiguous)` dispositions. Every other resolved candidate
whose claim depends on that failed or ambiguous census becomes its own
per-attempt `CounterpartUnavailableKey`, retains the failed
scope/attempt/ambiguity diagnostics, and receives terminal
`Failed(CounterpartUnavailable)` dispositions. It cannot become a semantic
pair, `Compared` add/remove, or Source-eligible item. The selection or
resolution failure also retains its own first-class failure work item. An
attempt already retained by a correspondence-ambiguity item is never mapped a
second time as counterpart-unavailable.
Selection owners may declare narrower independent scopes before execution to
bound this failure domain. The plan cannot split a failed scope afterward based
on guessed identity, merge two independent scopes into one proof domain, or
alias attempts across scopes.

Only a valid `DirectMemberComparisonInput` can authorize a
`DesignatedMemberPairKey`. The direct question records one caller-authorized
exact pair without asserting equal assembly identity, correspondence key,
signature, or relationship role. It therefore supports original-to-emitted
comparisons and `match --implementation` comparisons between arbitrary methods
while retaining both exact side-local identities. Endpoint, selector, and
assembly-wide paths cannot mint this key or use the direct designation as
correspondence evidence.

The Queries-owned direct operation first charges and seals one question whose
selection request is `Direct(designation id + exact before/after
designation-bound method-address values)` and whose derived intent is
`Explicit`. Only afterward does
the core factory create the internal slot's one-outcome admitted pairing and
one single-participant `SameSelection` role manifest per side. ResearchQueries
then projects the query question, pairing, domain, and correlation into
disjoint Research identities. The caller neither supplies nor observes the
role manifests, outcome receipt, query/Research correspondence maps, question,
correlation, or scope. Before the factory creates either participant, it
requires each method-address value to carry the exact source id minted for that
designation side. If both Research attempts resolve, the internal Research
session maps them to a designated-pair key containing the projected Research
designation id and both exact addresses and roles. If either fails, that
attempt maps to
`ResolutionFailedKey` and the resolved opposite attempt maps to its per-attempt
`CounterpartUnavailableKey` using the Research scope id. `AttemptMap` totality
and address/role validation therefore remain identical to the planned path
without inventing caller authority, passing a core designation to Research, or
adding a parallel failure key.

The plan materializes an immutable `AttemptMap`. Every target-attempt id maps
to exactly one work-item id, and every resolved or resolution-failed work item
retains at least one target-attempt id. Selection-failed work items instead
retain their scope-side identity. Every failed-domain scope maps to exactly one
question-local `ParticipantFailedKey` and retains both its scope and typed
endpoint-failure or participant-pairing ambiguity/failure identity; it cannot
share a work item with another correlation or disappear because the same
terminal domain applies to several questions.
Every request appears in exactly one selected scope side and names exactly one
attempt; the selected request-id set recorded on that side must equal the
requests that name it. Correlation ids may name several scopes but authorize no
cross-side matching. Set-equality validation among participant domains,
correlation scopes, scope-side request ids, requests, target-attempt ids,
work-item keys, attempt aliases, and `AttemptMap` rejects orphaned
domains/requests/attempts, multiply mapped attempts, unaliased work items, and
duplicate keys.

Index construction first lowers every `ReferenceOnly` role binding to its
pre-minted unavailable attempt without opening Metadata. It validates each
remaining exact address against its `SameSelection` participant and resolves
each carried target through `MemberBodyTargetResolver` in the exact supplied
body participant. It records resolution failures and role unavailability
before forming correspondence and marks that scope side's key census
incomplete. A resolved opposite attempt receives
`CounterpartUnavailable`; otherwise the unavailable attempt remains in a
`ResolutionFailed` work item with any opposite absence/census proof. Neither
path can become semantic one-sided evidence or producer/Source eligibility.

Only complete scopes bucket resolved entries by scope, participant pairing,
side, `MemberBodyCorrespondenceKey`, and role. A bucket containing distinct
strict targets or addresses is emitted as correspondence ambiguity before any
cross-side pairing. Equal keys in different scopes, participant pairs, scope
sides, or roles remain distinct. A collision split across independent scopes
is therefore two independent questions, not ambiguity; combining those
questions requires the selection owner to declare one common scope before
execution.

A `MemberBodyResolution.Bodyless` arm is a resolved entry, not a resolution
failure. It retains its address, keys, and role in the coordinate population.
For each local body-producing mechanism, the session determines body presence
from the resolved entries **before** invoking a two-body producer:

- two bodyful sides invoke the producer and require one exact-or-different
  verdict;
- one bodyful and one bodyless resolved side produces session-owned `Compared`
  body-added/body-removed evidence;
- a proven one-sided bodyful entry also produces session-owned `Compared`
  body-added/body-removed evidence;
- no bodyful side produces `Absent(NoBody)`, including a two-sided bodyless
  pair or proven-one-sided bodyless entry.

The coordinator does not invoke a two-body producer for the latter three
cases. Each add/remove disposition carries
`BodyEvidenceComparedDisposition` with `Verdict = Different`,
`Authority = SessionBodyPresence`, and exactly one typed `BodyAdded` or
`BodyRemoved` value. A producer may supply optional single-side display evidence
through a catalog-declared adapter, but it supplies no second verdict and its
current missing-body/unavailable payload cannot decide or replace the
session-owned disposition. Thus a
bodyless/bodyful transition cannot disappear as `Absent` or
`Failed(ComparisonUnavailable)`, while a failed or incomplete counterpart
cannot become a semantic addition/removal.

The resolved index exposes no enumerable population. The internal plan forms
the union of corresponded/proven-one-sided coordinates, designated pairs,
correspondence-ambiguity buckets, per-attempt counterpart-unavailable entries,
selection/target-resolution failures, and every question-local failed-domain
scope, then assigns one opaque work-item id to each. A failure or ambiguity
that cannot mint one unique semantic coordinate remains a first-class work item
and taints every absence claim that depends on its census; it cannot become
empty or success-shaped output.

### Mechanisms and dependency-ordered projection

`ImplementationEvidenceMechanismCatalog` is the closed source of mechanism ids,
owners, sync/async execution kind, local-implementation eligibility, and
dependencies. It also assigns every mechanism to the `Prerequisite` or
`Dependent` producer phase. The session selects its complete mechanism set from
that catalog and materializes the dependency graph and phase sets before
projection. The requested set must be non-empty and dependency-closed. Unknown
ids, an empty set, and a known mechanism whose required catalog dependency is
absent all reject before plan projection; hosts cannot synthesize descriptors,
phases, or an "all available" set.

`Project` and `ProjectAsync` privately walk all work items and require one
`Compared`, `Absent`, or `Failed` disposition per work-item id. A callback sees
only the current work item and already validated prerequisite dispositions for
that same id. It cannot enumerate another mechanism or the plan.

For selection-, target-, correspondence-, counterpart-, endpoint-, or
participant-failed work items, the session stamps the shared terminal failure
into every requested ledger without invoking a producer. For healthy items,
each producer returns one callback-local native payload and outcome. The
session immediately lowers and charges any retained evidence, releases the
native object, and only then stores the inert disposition. `Compared` requires
exactly one `BodyEvidenceComparedDisposition`. Producer authority
requires a two-body native exact-or-different verdict and no body-presence
value. Session body-presence authority requires `Different` plus exactly one
`BodyAdded` or `BodyRemoved` value; optional single-side native display evidence
does not carry another verdict. For two bodyful sides, a native unavailable,
decode failure, token-resolution failure, unsupported boundary, or other
verdict-less outcome becomes
`Failed(ComparisonUnavailable)` and may retain only its immediately lowered,
charged snapshot as diagnostic evidence. It never enters a `Compared`
disposition. Native
`OldBodyMissing`/`NewBodyMissing` is a migration encoding, not
`ComparisonUnavailable`; the target session does not invoke that producer
shape for a resolved bodyless side. Internal construction rejects missing,
extra, duplicate, verdict-less `Compared`, or mechanism-invalid `Absent`
dispositions.

Direct subject projection runs before direct mechanism producers. If either
side cannot produce its inert `ResearchSubjectKey`, the session retains that
side's typed `Failed` subject and stamps
`Failed(SubjectProjectionFailed)` into every requested ledger for the direct
work item without invoking a producer. Multiple subject failures remain one
complete typed failure payload rather than competing for the ledger slot.
Subject failure therefore necessarily makes `HasFailures` true and the direct
outcome `Unavailable`; it cannot coexist with an `Exact` or `Different`
outcome.

One mechanism's semantic projection, native Finding comparison, and required
cross-validation form one atomic ledger disposition. If Finding acquisition
fails after another projection succeeds, the disposition is
`Failed(FindingInspectionFailed)`. If both projections succeed but disagree,
it is `Failed(CrossValidationFailed)`. The ledger may retain independently
decoded semantic or Finding payload as partial diagnostic evidence, but that
payload supplies no exact/different verdict, no local-change eligibility, and
no replacement `Compared` disposition. The corresponding
`ResearchChangeKind.Failed` row is projected from this failed ledger rather
than becoming a second outcome beside it.

The catalog marks Source as requiring local implementation evidence. Session
validation therefore requires a Source request to include at least one
descriptor marked local implementation evidence and derives its prerequisites
as **every requested descriptor with that mark**; today those descriptors are
C# and IL. `{Source}` alone rejects rather than completing as all
`Absent(NotEligible)`. Once all derived prerequisite ledgers validate for an
item:

- any prerequisite `Failed` yields `Failed(PrerequisiteFailed)` without PDB,
  SourceLink, document, or network work;
- otherwise, any prerequisite that reports a local implementation change makes
  Source eligible;
- if no prerequisite reports a local change, Source yields
  `Absent(NotEligible)` without I/O.

This is the non-empty union of requested local mechanisms, validated when the
mechanism set is sealed rather than caller-selected after projection. C#-only
and IL-only requests are valid and changes from either are eligible.
Presentation filters, changed-only windows, subject groups, and row limits
cannot affect eligibility.

The Research-owned synchronous default is explicitly API, Body Signals, IL,
and C#. `ResearchChangeMechanism.AllAvailable` is retired because availability
depends on a host lifetime. Synchronous `ResearchDiff.Compare` rejects Source,
ReturnToSender, and unknown mechanism flags rather than silently omitting
their ledgers. The async query and harness runner declare host-owned mechanisms
under the lifetime that owns them.

### Planned-population and completed-result budget

Source is not the only input-amplified operation. Enumerative `All` can expand
untrusted MethodDef inventories into scopes, target requests, attempts, work
items, per-mechanism dispositions, native snapshots, and presentation values
before Source becomes eligible. Queries therefore owns one non-optional
`BodyEvidencePlanBudget` and mutable `BodyEvidencePlanBudgetLedger` for the
entire planned or direct operation. The ledger mints one
applicable `BodyEvidencePlanBudgetLeaseSet`: a planned
`IComparisonEndpointBudgetLease` declared with endpoint/coordinator contracts
in core Queries, a direct-only
`IDirectParticipantPairingBudgetLease` declared with its internal direct-pairing
factory, a planned-only
`IQueryCleanupDiagnosticReservationBudgetLease` declared in core Queries for
Source-acquisition cleanup reservations, and a planned/direct
`IBodyEvidenceProjectionBudgetLease` declared with the internal Research
session. The planned operation also mints one `IAuthoredSourceBudgetLease` from
its separate authored-source ledger.
ResearchQueries uses the plan ledger directly for plan-slot preflight, question
sealing, population sealing, and population projection. Publication retains
that concrete-ledger authority only to validate the operation identity and
closed zero-dimension edge; it performs no charge. ResearchQueries lends only the owner-local facets to endpoint
realizers/coordinator, the direct-pairing factory, or Source acquisition
owners. Those query-side facets carry the exact core query operation id. During `PopulationProjection`, ResearchQueries adapts
the same ledger into the Research-declared projection facet carrying the
bijectively mapped `ResearchBodyEvidenceOperationId`; Research code receives
neither the core id nor another subsystem's lease contract. Every facet can
reserve only its declared dimensions.

`BodyEvidenceOperationStageCatalog` is the single source of truth for the
operation boundary rather than a documentation-only checklist. The planned
pipeline contains every catalog stage. The direct pipeline omits only
`EndpointRealization` and the three planned-only dependent stages because its
closed synchronous mechanism set contains no dependent mechanism. Its
internally authorized designation still passes through admission, question
sealing, participant pairing, population sealing, population projection, plan expansion,
prerequisite producer preflight, prerequisite projection, Research completion,
query publication, and cleanup. Stage descriptors assign orchestration, facet,
budget dimensions, operation-failure routing, item-result routing, and
cancellation policy, not pairing or mechanism semantics. Their maps are per
operation kind: for example, qualified participant inputs belong to planned
endpoint realization but direct participant pairing.
Source-architecture and set-equality gates derive the expected planned/direct
stage registrations, lease-facet adapters, budget methods,
`(operation kind, stage, dimension)` edges, public failure-phase enum images,
operation-failure switches, item-result switches, cancellation switches, and
boundary fixtures from this catalog. A missing or extra implementation stage,
an unowned dimension/result route, or a stage reordered across its prerequisite
fails the gate.

The catalog entries are:

| Stage | Applies to | Owner by operation kind | Budget authority by operation kind | Authorized dimensions by operation kind |
| --- | --- | --- | --- | --- |
| `Admission` | Both | planned/direct: ResearchQueries | planned/direct: concrete ledger | endpoint slots, retained request/coordinate characters, and cleanup diagnostic reservations for operation-root resources |
| `QuestionSealing` | Both | planned/direct: ResearchQueries | planned/direct: concrete ledger | declared questions, selection-request entries, retained result entries, and retained request characters |
| `EndpointRealization` | Planned | planned: endpoint owner | planned: endpoint facet | qualified participant inputs, retained request/receipt characters, and cleanup diagnostic reservations for owned sessions/groups/leases |
| `ParticipantPairing` | Both | planned: acquisition coordinator; direct: core-Queries direct-pairing factory | planned: endpoint facet; direct: direct-pairing facet | planned: retained request/receipt characters; direct: qualified participant inputs and retained request/receipt characters |
| `PopulationSealing` | Both | planned/direct: ResearchQueries | planned/direct: concrete ledger | query participant domains, query correlation-domain entries, retained result entries, and retained request/receipt characters |
| `PopulationProjection` | Both | planned/direct: ResearchQueries | planned/direct: concrete ledger | population-projection entries, retained result entries, and retained request/receipt characters for every Research-owned admission/receipt copy |
| `PlanExpansion` | Both | planned/direct: Research | planned/direct: projection facet | correlation-scope entries, target requests/attempts, work items, ledger dispositions, retained result entries, retained request/receipt characters, and retained presentation characters for base work-item labels |
| `PrerequisiteProducerPreflight` | Both | planned/direct: Research | planned/direct: projection facet | producer input units and producer work units |
| `PrerequisiteProjection` | Both | planned/direct: Research | planned/direct: projection facet | live producer scratch, retained result entries, retained snapshot bytes, retained presentation characters, and cleanup diagnostic reservations for callback-local native resources |
| `DependentInputAcquisition` | Planned | planned: Source acquisition owner | planned: authored-source facet + Source-cleanup facet | authored-source facet: every authored-source budget dimension; Source-cleanup facet: cleanup diagnostic reservations for owned PDB/Source/content/transport resources |
| `DependentProducerPreflight` | Planned | planned: Research | planned: projection facet | producer input units and producer work units |
| `DependentProjection` | Planned | planned: Research | planned: projection facet | live producer scratch, retained result entries, retained snapshot bytes, retained presentation characters, and cleanup diagnostic reservations for callback-local native resources |
| `ResearchCompletion` | Both | planned/direct: Research | planned/direct: projection facet | none; validates already charged Research receipts/ledgers/presentation and seals a fixed-size wrapper only |
| `Publication` | Both | planned/direct: ResearchQueries | planned/direct: concrete ledger | none; validates already charged query/Research correspondence and seals a fixed-size provisional outer-result wrapper only; no external escape |
| `Cleanup` | Both | planned/direct: ResearchQueries | planned/direct: none | none |

The same catalog entries carry these exact result policies. An operation
failure means a stage invariant, budget, owner, or boundary failure; an item
result is a declared successful callback return, including a typed failed
ledger disposition. Item failure never substitutes for the operation-failure
route.

| Stage | Planned operation failure | Direct operation failure | Item result | Planned cancellation | Direct cancellation |
| --- | --- | --- | --- | --- | --- |
| `Admission` | `QueryFailure(Planning)` | `DirectFailure(Admission)` | none | propagate after cleanup | not applicable |
| `QuestionSealing` | `QueryFailure(Planning)` | `DirectFailure(QuestionSealing)` | none | propagate after cleanup | not applicable |
| `EndpointRealization` | `QueryFailure(Acquisition)` | not applicable | none | propagate after cleanup | not applicable |
| `ParticipantPairing` | `QueryFailure(Acquisition)` | `DirectFailure(ParticipantPairing)` | none | propagate after cleanup | not applicable |
| `PopulationSealing` | `QueryFailure(PopulationSealing)` | `DirectFailure(PopulationSealing)` | none | propagate after cleanup | not applicable |
| `PopulationProjection` | `QueryFailure(PopulationProjection)` | `DirectFailure(PopulationProjection)` | none | propagate after cleanup | not applicable |
| `PlanExpansion` | `QueryFailure(Planning)` | `DirectFailure(PlanExpansion)` | none | propagate after cleanup | not applicable |
| `PrerequisiteProducerPreflight` | `QueryFailure(Projection)` | `DirectFailure(Projection)` | none | propagate after cleanup | not applicable |
| `PrerequisiteProjection` | `QueryFailure(Projection)` | `DirectFailure(Projection)` | producer ledger disposition | propagate after cleanup | not applicable |
| `DependentInputAcquisition` | `QueryFailure(Projection)` | not applicable | dependent input outcome | propagate after cleanup | not applicable |
| `DependentProducerPreflight` | `QueryFailure(Projection)` | not applicable | none | propagate after cleanup | not applicable |
| `DependentProjection` | `QueryFailure(Projection)` | not applicable | producer ledger disposition | propagate after cleanup | not applicable |
| `ResearchCompletion` | `QueryFailure(ResearchCompletion)` | `DirectFailure(ResearchCompletion)` | none | propagate after cleanup | not applicable |
| `Publication` | `QueryFailure(Publication)` | `DirectFailure(Publication)` | none | propagate after cleanup | not applicable |
| `Cleanup` | `QueryFailure(Cleanup)` | `DirectFailure(Cleanup)` | none | propagate if clean; cleanup failure supersedes | not applicable |

The planned and direct public phase enums are the exact set images of
the applicable `OperationFailures` entries. Every applicable stage has exactly
one operation-failure route. Every item-producing stage has exactly one
declared item-result switch, while every other stage rejects an item result.
The planned executor has exactly one cancellation switch per stage; the direct
executor is synchronous and exposes no cancellation input.

The concrete plan ledger backs the endpoint, direct-pairing, Source-cleanup,
and Research projection facets; the authored-source ledger backs only its
Source-work facet.
Query-side facets carry the exact core query operation id. The Research
projection facet carries the exact Research operation id whose bijection to
that query id is sealed in the projection receipt. Core Queries declares the
opaque `DirectMemberDesignationId`, opaque `DirectDesignatedSourceId`, the
direct-pairing facet, and the internal direct-pairing factory. Its existing
friend boundary permits only `DotnetInspector.ResearchQueries` to mint a public
designation and its source ids and to invoke that factory; neither public
callers nor `ILInspector.Research` can mint either authority.

| Dimension | Default | Accounting |
| --- | ---: | --- |
| Endpoint slots | 256 | Every sealed pairing-plan slot |
| Qualified participant inputs | 4,096 | Exact planned manifest union or two designation/source-bound direct inputs |
| Declared questions | 1,024 | Every pre-acquisition selection question |
| Selection-request entries | 65,536 | Every explicit selector, enumerative filter operand, or direct address/role entry |
| Query participant domains | 16,384 | Every admitted, endpoint-absent, or failed logical query domain |
| Query correlation-domain entries | 16,384 | Every required query `(correlation, participant domain)` pair |
| Population-projection entries | 131,072 | The projected operation identity and every projected endpoint, participant input, participant/binding pair, domain, question, correlation, direct designation/source binding, or terminal-evidence value together with its correspondence-map entry |
| Correlation-scope entries | 16,384 | Every required live Research `(correlation, participant domain)` scope |
| Target requests and attempts | 65,536 | One charge when each request/attempt identity is minted |
| Work items | 65,536 | Every healthy, absent, ambiguous, or failed work-item identity |
| Ledger dispositions | 262,144 | Requested mechanism/work-item product, preflighted before projection |
| Producer input units | 4,194,304 | Complete catalog-declared admitted-input population for each phase, including bounded Source snapshots |
| Producer work units | 64,000,000 | Catalog-declared conservative operations; ordered matching charges checked matrix cells |
| Live producer scratch bytes | 256 MiB | Scoped conservative reservation held through native-result release |
| Retained result entries | 524,288 | Every root or nested immutable query, correspondence, Research receipt, native-snapshot, or presentation entry that may survive into a completed result; fixed-size sealing wrappers are excluded |
| Retained request/receipt characters | 8,388,608 | Every selector, filter, structural identity, opaque id, proof, and diagnostic string copied into plan input, endpoint outcome, or receipt |
| Retained snapshot bytes | 256 MiB | Immutable native byte/value arrays plus checked UTF-16 string storage |
| Retained presentation characters | 8,388,608 | Every inert label, detail, and diagnostic character retained by the result |
| Cleanup diagnostic slots | 65,536 | One fixed typed slot reserved before each owned resource, lease, scratch reservation, or native callback result transfers into the operation lifetime |
| Cleanup diagnostic characters | 4,194,304 | Fixed bounded non-artifact text storage reserved with each cleanup slot before ownership transfer |

Entry limits bound object overhead; byte/character limits bound variable-sized
payloads. Each retained copy is charged in its own owning dimension; reusing
one host string does not make two retained question/receipt or presentation
copies free. Producer input/work units are cumulative; live scratch is a
scoped concurrent reservation, so serial execution releases capacity but never
restores consumed work. Existing per-artifact metadata, body-decode, and
workspace-retention limits remain active and do not substitute for this
aggregate operation budget.
Cleanup slots and their character ranges are reservations, not after-the-fact
charges. The resource-acquiring stage charges them before ownership transfer.
Cleanup writes only into those ranges and freezes a bounded view; it cannot
append, resize, retain an exception graph, or request budget. A reservation
failure prevents acquisition. Borrowed resources receive no slot because the
operation must not dispose them.
Hosts may lower any default. Raising one requires an invocation-scoped
`BodyEvidencePlanBudgetOverrideCapability` minted by the composition root from
an explicit user/host gesture; `InspectionCost`, an exhaustive selector, and a
Source budget override are not that grant. Browser/Wasm hosts may choose lower
defaults without changing result semantics.

The planned query receives the already sealed endpoint-pairing plan and mints
the ledger before sealing questions or requesting endpoint realization. It
first preflights and charges the plan's exact endpoint-slot count and every
query-owned copy of its variable request/coordinate identity; failure starts no
acquisition. Question-set sealing then requires the lease and incrementally
charges the question, every retained-result and selection-request entry, and
every variable-sized selector/filter/identity payload before copying it into
the immutable request.
A single explicit question that matches nothing is therefore still bounded.

Every endpoint realizer receives the non-optional endpoint facet. As the
source-specific acquisition discovers its complete selected inventory, the
workspace admission budget continues to bound download, archive, artifact, and
retained-byte work. Before the endpoint owner materializes one role binding,
participant-manifest entry, endpoint-outcome payload, or query-owned copy of a
variable identity/proof/diagnostic, it reserves and charges the corresponding
participant-input count and retained character storage. It returns its outcome
with an internal query-owned budget receipt. Overflow or exhaustion produces
one fixed-size outer
`ImplementationComparisonQueryOutcome.Failed(Acquisition,
PlanBudgetExceeded)` with no artifact-derived text and discards every staged
manifest, outcome, or receipt. The pairing coordinator requires the same
endpoint facet and the complete budget-receipt set. Before qualifying keys or
allocating candidate/affected payloads, input maps, or pairing outcomes, it
checks that the receipt counts equal the exact realized-manifest union and that
receipt character charges equal the independently measured query-owned outcome
payloads. It validates rather than double-charging participant entries or
characters already retained by endpoint realization. Pairing-authored
candidate/affected maps, ambiguity/failure payloads, reasons, and diagnostics
are new retained copies: the coordinator measures and charges those characters
at `ParticipantPairing` before retaining them.

`PopulationSealing` first derives the exact live query-domain, inert
query-domain-receipt, and query correlation-domain populations without
retaining them. It atomically charges the complete logical-domain,
correlation-domain, and retained-result-entry counts, then charges each
query-owned variable payload before sealing the
`BodyEvidenceSealedPopulation`.
`PopulationProjection` derives the complete Research
endpoint/input/participant-binding/domain/domain-receipt/question/correlation/
designation/terminal population and correspondence-key set, atomically charges
the complete projection and retained-result-entry counts, and then separately
measures and charges every Research-owned request/receipt character before
retaining the Research admission values, inert domain receipts, and
correspondence maps.
Neither owner may treat an earlier charge as a reservation for its later copy.

Later known counts are preflighted before their next phase. Research charges
each live correlation scope before selection, then each request, attempt, work
item, ledger disposition, and selection/resolution diagnostic before retaining
it during `PlanExpansion`. Once scope outcomes and the work-item set are
complete, Research derives the scope-receipt and base-presentation populations
without retaining them, atomically charges their retained-result entries and
every retained character, and only then commits those values. Pairing-owned and
population-projection-owned diagnostic copies do not prepay this Research-
owned retention. Each
prerequisite mechanism catalog entry maps its producer's already bounded input
facts to checked `InputUnits`, `WorkUnits`, and `PeakScratchBytes`. The session
seals the complete prerequisite work plan and atomically charges its input/work
totals before the first prerequisite callback.

After the prerequisite ledgers determine the exact Source-eligible population,
the planned-only `DependentInputAcquisition` stage acquires and checksum
verifies Source under the operation-stamped authored-source facet. It may
perform bounded linear decoding, exact member-body slicing, and a raw line
census, but returns only a bounded verified Source-input snapshot; it does not
invoke `TextFindings.Inspect`, construct Finding keys, or compare bodies. The
dependent estimator uses only each admitted snapshot's checked text length and
line census to conservatively cover Source Finding inspection, key arrays,
canonicalization, and matching. The session then seals and atomically charges
the complete Source producer-work plan and checks every peak before the first
Source producer callback. For IL/Finding ordered matching in either phase, the
work estimate includes the checked `(oldCount + 1) * (newCount + 1)` cell
product and scratch includes the complete matrix and producer-owned arrays. A
catalog entry without a finite conservative estimator cannot run.

Each projection phase acquires its entry's live-scratch reservation before its
callback. Each producer result is then typed-lowered and measured; its retained
result entries, snapshot bytes, and presentation characters are charged before
its inert disposition enters a ledger. The native object and scratch
reservation are released before another callback runs. Source evidence that
survives its own
acquisition budget also charges this final-result budget before ledger
retention. Checked arithmetic rejects overflow. A charge that would exceed the
limit invokes the current stage descriptor's exact `OperationFailures` route.
For the planned path this produces
`ImplementationComparisonQueryOutcome.Failed(<catalog-derived phase>,
PlanBudgetExceeded)` with dimension, limit, observed/requested charge, and no
partial plan or completed result. The distinct planned phase set is derived
from the catalog rather than repeated at the charge site. Exhaustion never
truncates an inventory, shortens a correlation, drops a work item/disposition,
or publishes a partially retained receipt. Staged inert dispositions from a
failed planned projection are discarded; no native result remains accumulated
behind them.

`DirectImplementationComparisonOperation` is the sole direct designation
factory and executor. `DesignateMemberPair` mints one opaque designation id,
one opaque source id per side, and retains the two caller-owned live sources
and their MVIDs for the lexical operation lifetime. Each source id is bound to
one exact source object and side even when both sides use one object. The
designation creates no role manifest, binding, qualified key, participant
pairing, outcome, question, or work item. Its factory is also the sole way to
mint a `DirectDesignatedMethodAddress`: it accepts the exact designated source
object and a `MethodDefinitionHandle`, validates object identity and row
bounds, and creates the `MetadataMethodAddress` from that source's reader.
It never accepts a naked cross-source `MetadataMethodAddress`.
`Execute` accepts only a non-empty C#/IL mechanism subset, mints the same
concrete ledger in `DotnetInspector.ResearchQueries`, and executes this exact
order:

1. `Admission` charges one internal endpoint slot and the fixed request
   coordinate.
2. `QuestionSealing` validates the non-empty C#/IL set and charges its retained
   request-receipt entries, then charges and seals one direct question from the
   designation id, internal slot, and exact before/after designation-bound
   method-address values. The mechanism set later becomes the Research
   session's `RequestedMechanisms`; it is not part of the selection question.
   This occurs before any participant, manifest, binding, or qualified key
   exists. An empty set or API, Body Signals, Source, ReturnToSender, or unknown
   mechanism rejects rather than being ignored.
3. `ParticipantPairing` lends the operation-stamped direct-pairing facet to the
   core-Queries factory. The factory charges both qualified designation inputs
   and every pairing-authored retained character before creating two
   invocation-scoped `DirectSourceParticipant` values keyed by the exact
   designation-bound source ids, one
   single-participant `SameSelection` role manifest per side, the two
   side-qualified input keys, their bindings, and one admitted
   `ArtifactParticipantPairing`. The pairing id and
   `DirectMemberDesignation` authority are exactly the designation id.
4. `PopulationSealing` exhaustively lowers the admitted pairing into the exact
   query domain, inert domain receipt, and correlation required by the already
   sealed question.
5. `PopulationProjection` exhaustively lowers the sealed query question,
   domain, correlation, and admitted pairing into Research-owned question,
   participant, binding, domain, correlation, terminal, and
   direct-designation values. The Research factory mints one
   `ResearchBodyEvidenceOperationId`; ResearchQueries charges and seals its
   bijection with the core query operation and creates the Research-owned
   projection facet carrying that id. It seals the exact query-to-Research
   correspondence receipt before any Research selection.
6. `PlanExpansion` invokes the internal Research session with only the
   `ResearchBodyEvidenceAdmissionPlan` and projection facet stamped with the
   exact Research operation id. Research creates its scope, exact
   requests/attempts, and non-empty work-item population before producer
   preflight.

No Research type constructs, accepts, or retains a workspace/acquisition
currency. A failure at any direct stage before a complete work-item population
cannot stamp a work-item ledger because no charged work-item id exists. It
returns `ImplementationMemberDiffEvidence.Failed` with one fixed-size
`DirectImplementationComparisonFailure`, `Outcome = Unavailable`, and
`HasFailures = true`. A later direct plan-budget failure discards every staged
Research ledger and returns the same failed arm; it does not fabricate missing
dispositions, a sentinel work item, or an empty completed population. The
fixed failure envelope contains no artifact-derived text. Ordinary outcome
reduction runs only for `Completed`, whose work-item population and every
requested ledger are non-empty and complete. An all-absent/vacuous reduction
is therefore impossible. A one-item direct population does not create a budget
bypass. `ILInspector.Research` can accept only its operation-stamped internal
admission plan and projection facet; it cannot mint, accept from a caller,
replenish, or substitute the concrete ledger.

### Query-owned operation lifetimes

`ImplementationComparisonQuery` is one
`InspectionQueryRegistry.AddAsync` operation. Begin, synchronous projections,
dependency-gated Source projection, completion, and strict cleanup execute
lexically under one current query access lease. The query owns or borrows every
`AssemblyInspectionSession`, PDB/source content lease, retained artifact
binding, and transport capability.

The CLI receives no plan or session. It supplies typed endpoint and target
requests, mechanism selection, capabilities, and plan/Source budgets, then
receives one sealed outer outcome:

```text
ImplementationComparisonQueryOutcome
  Completed              ImplementationDiffResult
  Failed                 ImplementationComparisonQueryFailure

ImplementationComparisonQueryFailurePhase
  Value                  Acquisition | Planning | PopulationSealing |
                         PopulationProjection | Projection |
                         ResearchCompletion | Publication | Cleanup

ImplementationComparisonQueryFailure
  Phase                  exact ImplementationComparisonQueryFailurePhase
  Primary                typed query diagnostic
  Cleanup                bounded immutable view over precharged
                         BodyEvidenceCleanupDiagnosticSlot values
```

`DirectImplementationComparisonOperation.Execute` is a synchronous
ResearchQueries operation over caller-owned live sources. It mints its ledger
before direct participant pairing or lowering, retains the designation and
sources only through the lexical call, and returns one inert
`ImplementationMemberDiffResult`.
`ILInspector.Research` exposes only the internal admission-plan- and
lease-requiring session used by this operation; it has no public direct
executor. Direct operations do not enter `InspectionQueryRegistry` and can request only
C# and IL, but their budget, producer-work, lowering, Research completion,
query publication, and failure contracts are identical to the applicable
synchronous portion of the planned query.

Owned resources and lease claims are released after success, failure, or
cancellation.
Caller-owned borrowed sessions are never disposed. A cleanup failure alone is
a `Failed` query outcome and no completed result escapes. When another
operation failure already exists, that remains primary and the cleanup
diagnostic is retained beside it in the resource's precharged slot. Each
resource-acquiring stage reserves the fixed typed slot and complete bounded
non-artifact text range before ownership transfer. Cleanup writes into those
slots and freezes one bounded view; it allocates no variable collection,
retains no exception graph, and has no charging edge. The failed arm contains
no partial result, plan, session, work-item population, or forgeable receipt.
Cancellation stops and drains owned work before cleanup. When cleanup succeeds,
cancellation propagates as cancellation and returns neither outcome nor a
partial/failure-shaped comparison substitute. If cleanup fails while unwinding
cancellation, the cleanup failure supersedes cancellation as
`ImplementationComparisonQueryOutcome.Failed(Cleanup)` and retains every typed
cleanup diagnostic; it still exposes no partial or completed result.

The lifetime requires no threads or blocking synchronization. A
single-threaded Browser/Wasm executor may await each operation sequentially;
a native executor may run independent I/O concurrently within the same budget.
Ordering may change observed charge totals and earlier diagnostics, but every
eligible work item has the same final success/failure disposition kind. No
executor may publish a partial successful Source population after aggregate
exhaustion.

### Authored-source evidence budget

Source is explicit, network-capability-gated, and finite. Queries owns
`AuthoredSourceEvidenceBudget` and one mutable
`AuthoredSourceEvidenceBudgetLedger` scoped to the current query lease;
producer defaults are:

| Dimension | Default | Accounting |
| --- | ---: | --- |
| Eligible work items | 1,024 | Healthy work items made eligible by local prerequisite ledgers |
| Unique Portable PDBs | 64 | One validated PDB identity per participant; embedded and external both count |
| Unique source documents | 4,096 | Canonical PDB document identity per validated PDB |
| Outbound operations | 4,096 | Every query-issued HTTP operation, including retries; a platform-managed redirect chain belongs to one operation |
| Transferred bytes | 512 MiB | Symbol packages, PDBs, and source response bodies actually read from transport |
| Retained evidence bytes | 256 MiB | PDB/source bytes and decoded text retained or staged for this query |
| Concurrent operations | 4 | Active PDB/source I/O operations; a sequential executor uses one |

Hosts may lower any limit. Raising a default requires an invocation-scoped
`AuthoredSourceBudgetOverrideCapability` minted by the composition root only
from an explicit user/host gesture, in addition to SourceContent and any
required Network capability. The query's static
`InspectionCost.Unbounded` classification controls automatic disclosure and
does not grant a budget override. Per-artifact protections such as
symbol-package limits, PDB expansion limits, SourceLink map limits, transport
redirect-chain limits, and the 16 MB per-document source download limit remain
active and do not substitute for the aggregate query budget.

The ledger is the sole aggregate accounting authority. Query-time PDB,
SourceLink, source-fetch, cache-materialization, and retry paths receive a
non-optional scoped `IAuthoredSourceBudgetLease`; they cannot construct or
substitute a second ledger. Its operations are:

```text
RegisterEligibleSet(count)
ReservePortablePdb(participant, identity)
ReserveDocument(pdbIdentity, canonicalDocumentIdentity)
BeginOutboundOperation(transportCoordinate)
ChargeTransferredBytes(operationLease, count)
ChargeRetainedBytes(contentIdentity, exactByteCount)
EnterOperation()
```

`BeginOutboundOperation` returns the only stream/read authority for one
query-visible send. A retry requests a new operation lease. A redirect chain
performed inside the platform transport remains one operation because Browser
Fetch does not expose every physical hop; native transports use the same
logical accounting and a separately bounded redirect policy. Helpers that
manually issue a follow-up request start another operation. Response reads
charge the operation lease before bytes become visible to PDB/source parsers.
Cache reads skip outbound and transfer charges but charge retained bytes before
materialization. Retained binary bytes charge their exact length; decoded text
additionally charges checked UTF-16 storage (`Length * sizeof(char)`).
Entry/document caps bound the remaining per-object overhead.

`InspectionCost` remains a coarse execution/disclosure classification.
Per-request `SourceFetcher`, symbol-package, PDB expansion, and SourceLink
limits remain local defenses. None can authorize, replenish, or bypass the
query ledger.

The query computes the complete eligible work-item set before Source I/O.
Participant-coalesced PDB setup runs once per participant. Document identity,
cache lookup, and acquisition are shared across eligible members. A cache hit
uses no outbound-operation or transferred-byte budget, but bytes retained by
this query still count.

Preflight rejects an already-known dimension above its limit before I/O.
Unknown dimensions reserve and charge atomically as they are discovered.
Transport charges operations before sending and bytes while reading; reading
stops before the aggregate cap is exceeded. No retry, manually issued redirect
follow-up, cache materialization, or decoded string bypasses accounting.
Successful per-member acquisition retains one bounded checksum-verified raw
Source-input snapshot with checked text length and line census. It performs no
Finding inspection or comparison. After every eligible acquisition reaches a terminal input outcome, the query
validates exact outcome-set equality with the eligible population. Acquisition
failures receive typed Source dispositions without producer callbacks.
ResearchQueries derives the exact Source producer population from the
successful input-outcome subset, then lends the Research-operation-stamped
projection facet and bounded input snapshots to the Research session. That
session seals and cumulatively charges the complete dependent work plan and
checks every peak before invoking the first Source producer. An acquisition
failure therefore cannot disappear, become producer absence, or let another
eligible item start comparison before the complete dependent input and
preflight boundaries.

Source dispositions are staged until the entire mechanism succeeds. If any
aggregate dimension is exhausted, the query cancels and drains owned Source
work, discards all staged eligible Source results, and records
`Failed(SourceBudgetExceeded)` for every eligible Source work item. The failure
retains dimension, limit, observed/requested charge, and any earlier per-item
diagnostic. Ineligible items remain `Absent(NotEligible)` and pre-existing
target/participant ambiguity or failures remain their original failures. Thus
executor
ordering may change the reported exhausted dimension, observed/requested
charge, earlier diagnostic, and how early work stops, but never which eligible
items appear successful or failed.

### Completion and result boundary

`ResearchCompletion` and `Publication` are distinct one-shot transitions.
Research completion fails unless every declared mechanism has one complete,
disjoint ledger over the exact Research work-item set. Projection or Research
completion afterward fails visibly. It validates the already charged inert
participant-domain and scope receipts, ledgers, native snapshots, changes, and
total `BodyEvidencePresentationMap`, and requires the admission plan, comparison
plan, session, projection facet, and completion builder to carry one exact
`ResearchBodyEvidenceOperationId`. It then seals one fixed-size provisional
`ResearchBodyEvidencePlanReceipt` wrapper carrying that inert id without
copying a variable payload.
Each presentation entry was created under `PlanExpansion` and the applicable
producer-projection stages and has inert participant and member labels
sufficient to distinguish two equal member subjects in different
TFM/RID/assembly participants. Labels contain no address, target, participant
proof, or correspondence key from which evidence could be reselected.
The owning stages populate operation-local bounded builders only after their
entry and payload charges succeed. Research completion freezes those builders
in place, revokes mutation, and transfers their exact backing collections into
the fixed-size receipt/comparison wrappers; it does not enumerate them into new
arrays, maps, strings, or DTOs. Failure discards the builders.

Research completion accepts and retains only Research-owned identities,
snapshots, ledgers, and catalog-declared native DTOs. It has no query receipt,
core participant identity, acquisition outcome, or outer result constructor.
Its one product is:

`ResearchComparison` becomes a closed union:

```text
ResearchComparison
  UnscopedResearchComparison
  ResearchBodyEvidenceComparison

ResearchBodyEvidenceComparison
  Receipt                 ResearchBodyEvidencePlanReceipt
  Ledgers                 complete requested mechanism ledgers
  Presentation            total BodyEvidencePresentationMap
  Native                  complete inert native-payload snapshot map
  Changes                 complete typed ResearchBodyEvidenceChange values

ResearchBodyEvidenceChange
  WorkItem                ResearchBodyEvidenceWorkItemId
  Mechanism               requested mechanism id
  Evidence                Producer(ResearchChange) |
                         SessionBodyPresence(BodyAdded | BodyRemoved)
```

Standalone API comparison and explicit C#/IL presentation/test adapters return
the unscoped arm. It cannot feed Implementation Diff. Only Research-owned
session completion can create the Research body-evidence arm.
`CombineUnscoped` rejects a planned input; planned results never combine
because all body mechanisms belong to one session.

`Publication` belongs to `DotnetInspector.ResearchQueries`. It accepts the
complete inert query receipt, the
`BodyEvidencePopulationProjectionReceipt`, and one
`ResearchBodyEvidenceComparison`. It proves:

- the projection receipt maps the exact owning core query operation to the
  exact Research operation carried by the Research receipt and every
  Research-owned plan/session/facet antecedent;
- every query question/domain/correlation and direct designation has exactly
  one Research image in its correspondence map and vice versa, and every
  designated source id maps to the exact query binding whose image is the
  corresponding Research participant;
- every projected Research identity used by a scope, work item, ledger,
  native snapshot, change, or presentation entry belongs to that exact
  Research receipt;
- each projected binding and terminal payload remains equal to its inert
  query-side snapshot for side, role, MVID, registration, assembly identity,
  terminal kind, reason, diagnostic, proof, selection, derived intent,
  endpoint scope, and direct source/address/role where applicable;
- no live query or Research value remains in the object graph.

Only then may ResearchQueries compose one `BodyEvidencePlanReceipt` and issue
`ImplementationDiffResult` or the completed arm of
`ImplementationMemberDiffResult`. This composition is typed correspondence,
not display joining, reconstruction, or variable-payload copying. Publication
seals only fixed-size wrappers over the exact immutable collections already
constructed and charged by their owning stages. `ImplementationDiffResult`,
`ImplementationMemberDiffResult`, `ImplementationDiffMember`, and their
internal constructors move from `ILInspector.Research` to
`DotnetInspector.ResearchQueries`. Research exposes no outer-result factory,
and the compatibility `ImplementationDiff.FromResearchComparison` path is
removed rather than moved.

The stage name denotes publication into one sealed provisional outer-result
value inside the operation. That value cannot escape while the head lease or
owned resources remain current. `Cleanup` runs next; only successful cleanup
allows the operation to publish `Completed(result)` to its caller. Cleanup
failure discards the provisional value and publishes only the typed failure
arm.

Completed-result constructors accept only the closed inert receipt/result
types and catalog-declared native snapshot DTOs; generic `T` is not a public or
host-chosen escape hatch. No constructor parameter or retained field can carry
an `ArtifactParticipantPairing`, role manifest/binding,
`AssemblyContextParticipant`, live Research binding, source, reader, group,
workspace/session, plan, callback, lease, or other content-opening authority.
Publication validates the explicit query-to-Research equalities before cleanup
rather than reflecting over an object graph. The query returns
`Completed(result)` only after strict cleanup succeeds and a post-disposal
consumer can enumerate the full query receipt, correspondence receipt,
Research receipt, ledgers, native snapshots, changes, and presentation map
without touching disposed state. Cleanup failure discards that provisional
result and publishes only the outer `Failed` arm.

Planned public result types are sealed non-records with internal
ResearchQueries constructors and get-only immutable collections.
`ImplementationDiffResult.WorkItems` is keyed by
`ResearchBodyEvidenceWorkItemId` and is a non-materializing read-only view over
that id's already charged Research presentation entry, mechanism dispositions,
and native snapshots. Enumeration may create an ephemeral item view but cannot
retain another list, map, string, or payload copy; all query-owned labels needed
by the view already exist in the inert plan and correspondence receipts.
`ImplementationMemberDiffResult` retains separate before/after subjects rather
than inventing one shared member identity for an arbitrary designated pair. Its
`Completed` arm has the same non-forgeable query/Research correspondence and a
non-empty work-item/ledger population. Neither result exposes `init`,
`with`-copy, or public member/result constructors that can attach Source or
synthesize a success without both receipts and complete ledgers.

`ImplementationDiffResult.HasFailures` derives only from its retained completed
ledgers, not rendered or windowed rows. Query and cleanup failure instead
produce `ImplementationComparisonQueryOutcome.Failed`, whose formatter and
exit mapping do not require or fabricate an `ImplementationDiffResult`. When
Implementation Diff is selected, every CLI text and structured output path
returns nonzero for any target, endpoint, participant, mechanism, budget,
query, or cleanup failure. `Absent`, including Source `NotEligible`, remains
non-failing. API inspection failures retain their existing independent exit
behavior.

`ImplementationMemberDiffResult.HasFailures` is always true for
`Evidence.Failed`. That arm always yields `Unavailable`, retains its fixed-size
typed operation diagnostic, and has no work-item ledgers to reduce. For
`Evidence.Completed`, `HasFailures` derives from the complete direct ledgers
and retains their typed diagnostics. It is not inferred from row count or a
nullable native payload. The non-empty work-item and requested-mechanism sets
make this completed-arm precedence total:

1. `Unavailable` when any ledger is `Failed`, even when another mechanism
   compared successfully;
2. `Different` when no ledger failed and at least one `Compared` disposition
   proves a difference;
3. `Exact` when no ledger failed, at least one disposition is `Compared`, and
   every `Compared` disposition is exact; any accompanying `Absent`
   dispositions retain their catalog-defined non-failing reasons;
4. `NotApplicable` when every requested disposition is `Absent`.

A designated pair for which every requested C#/IL mechanism returns
`Absent(NoBody)` therefore yields `NotApplicable`. API and Body Signals are
not direct mechanisms; their unscoped or planned evidence cannot override this
result. Unknown or mechanism-invalid absence reasons reject completion rather
than entering this reduction. Because every `Compared` disposition contains
exactly one exact-or-different verdict, no fifth verdict-less state remains. A
direct failure has no semantic exact/changed verdict, while a mixed
exact/absent result is `Exact` only for the applicable requested C#/IL
mechanisms and retains every absence reason.

## Research comparison model

`ResearchDiff` is the operation facade. `UnscopedResearchComparison` retains
the existing flat `ResearchChange` collection and subject grouping because it
makes no multi-participant population claim.
`ResearchBodyEvidenceComparison` instead retains a closed
`ResearchBodyEvidenceChange` union. Its producer arm wraps the unchanged
`ResearchChange`; its session arm carries the typed `BodyAdded` or
`BodyRemoved` evidence from a session-authoritative `Compared` disposition.
Research completion requires exactly one session arm for every such
work-item/mechanism disposition and rejects a missing or duplicate arm. That
arm is not a `ResearchChange`, Finding, or synthetic producer row.
Research completion likewise requires a non-empty complete producer-arm set
for every producer-authoritative `Compared(Different)` disposition and no
producer change arm for `Compared(Exact)`. The set is derived from the
producer-owned native comparison and rejects missing, extra, or duplicate
changes; each retained change can therefore supply its own typed fallback when
it has no visible native line. Failed producer arms and retained partial
payloads carry no exact/different verdict and are validated separately.
`ByWorkItem()` is the authoritative grouping; any member-centric convenience
projection groups producer changes by `(workItemId, ResearchSubjectKey)`, never
by `ResearchSubjectKey` alone. The planned arm additionally retains its receipt,
complete ledgers, presentation map, and native-payload snapshots.

Each `ResearchChange` carries one mechanism, a `FindingDescriptor`, an
added/removed/changed/failed classification, its subject, and any native producer
payload snapshot needed for typed presentation. `ResearchChange` participates
in the same catalog-declared inert type closure; a planned
`ResearchBodyEvidenceChange` producer arm never wraps the query-lifetime native
comparison object. It is deliberately not a
`PairFinding<T>`. Metadata now exposes genuine API type/member comparisons and
`ResearchComparison.ApiComparison` retains that producer-owned envelope. C#,
IL/body, body-signal, and ReturnToSender mechanisms do not all expose equivalent
old/new Finding censuses yet, so the cross-mechanism `ResearchChange` projection
must not manufacture Finding atoms or misuse `PairKind`. `ResearchChange` is a
Research-owned migration projection, not the seed of a parallel generic
`EvidenceRow` spine. C# and IL now have native comparisons;
their semantic rows remain because they carry richer producer-owned evidence,
while retained comparisons expose the exact census transitions. The `Source`
mechanism never replaces or changes the meaning of `CSharp`: one describes
checksum-verified PDB-mapped text and the other describes product-decompiled
text. `ResearchSubjectKey` remains producer-local member currency;
`ResearchBodyEvidenceWorkItemId` is the separate participant-aware
planned-result currency and is never reconstructed from the subject.

### Deliberate dual-representation decision

This design revises the earlier plan to retire the C# and IL semantic
projections immediately after Finding adoption. The two retained
representations have different durable payloads:

- native Finding comparisons own inspection outcomes, stable census identity,
  and added/removed/present/changed transitions;
- `CSharpBodyDiff` and `IlBodyDiff` own aligned hunks, typed display failures,
  old/new offsets, and richer producer-formatted evidence.

The semantic projections therefore remain deliberately rather than by
accretion. Every overlapping member is cross-validated against the native
comparison. Acquisition failure or divergence makes that mechanism's ledger
`Failed(FindingInspectionFailed | CrossValidationFailed)`; the visible
per-member `Failed` diagnostic is derived from that disposition. Any
independently decoded semantic payload remains available only as partial
diagnostic evidence, so it cannot make `HasFailures` false, authorize Source
work, or coexist with a `Compared` disposition for the same work item and
mechanism. If the Finding producers later carry equivalent aligned hunk and
typed display payloads, the semantic projections should be deleted rather than
matched a third time.

## Row currency contract

Every diff row across MetadataDiff, ILDiff, Analysis/body-signal diff, C#Diff,
and ResearchDiff must be reachable back to its owning API/member through stable
member currency, then locatable inside its own mechanism through native row
coordinates. The two obligations are separate: currency answers "which member,"
native coordinates answer "which row within that member." This section states
which layer supplies each obligation. It does not impose a universal
`Before`/`After` handle: IL rows already carry side as row polarity
(`Add`/`Remove`/`Context`), and forcing an explicit old/new pair onto that shape
would duplicate the native model. See [Finding Coordinates](finding-coordinates.md)
for the underlying subject / correspondence / provenance axes; this contract is
the diff-row application of those axes.

### Two carrier classes

Rows fall into exactly one of two classes by the altitude at which they are
produced:

- **Anchor-carrying rows** are produced after member alignment, so the row owns
  the stable member currency directly. `ApiChange` (MetadataDiff) carries
  `MemberAnchor` through `ApiChangeSubject`; `CSharpDiffRow` (C#Diff) carries
  `MemberAnchor` plus its `StableMemberKey` on the row. Both expose
  `CanonicalSignature`, `StableSelector` (`Name~digest`), and the member digest
  as typed fields. `MemberAnchor` lives in `ILInspector.MetadataPrimitives`, a
  lightweight primitive assembly, so carrying it does not pull in the heavy
  Metadata layer.
- **Body-substrate rows** are produced below member selection: they compare one
  already-resolved body's operation or signal stream and hold no member
  identity. `IlDiffRow`/`CanonicalIlOperation` in `ILInspector.ILDiff`
  carries only `HunkId`, `IlDiffKind` polarity, the operation, and a
  producer-owned `Message`; analysis/body-signal facts sit at the same altitude.
  The caller that already resolved the enclosing member supplies the stable
  currency by wrapping — `IlAssemblyDiff.CompareMembers` returns an
  `IlMemberDiffSubject`, and Research attaches a `ResearchSubjectKey` from
  `ResearchMemberIdentity.SubjectFromMethod`. This is altitude, not a
  Metadata-dependency ban: `ILInspector.ILDiff` already references
  `MetadataPrimitives`, so it *could* embed a `MemberAnchor`; it deliberately
  does not, because a body substrate that diffs a single pre-resolved body pair
  has no member to name and the enclosing member subject already owns that fact.

A row is never both. Adding member identity to a body-substrate row, or
reconstructing it there from display text, would duplicate identity the wrapper
already owns and violate the layer-ownership rule.

### Native coordinates each mechanism preserves

The wrapper preserves, not flattens, the native coordinates so a consumer can
replay or locate the row after the member is known:

| Mechanism | Currency carrier | Native row coordinates |
| --- | --- | --- |
| MetadataDiff | row (`ApiChange` → `MemberAnchor` on `ApiChangeSubject`) | `ApiChangeSubjectKind`, old/new member handles, category |
| C#Diff | row (`CSharpDiffRow` → `MemberAnchor` + `StableMemberKey`) | `ChangeId` / `CSharpDiffKind`, source line / `SourceCoordinate`, related IL offsets as evidence, fidelity |
| ILDiff | wrapper (`ResearchSubjectKey` via `SubjectFromMethod`) | `HunkId`, `IlDiffKind` polarity, `CanonicalIlOperation`, IL offset (hint) |
| Analysis/body-signal | wrapper (`ResearchSubjectKey` via `SubjectFromMethod`) | signal / shape, added/removed/changed kind, IL offset(s) as evidence |

IL offsets, operation-array ordinals, and source spans are local evidence and
display hints, never the durable selector. The durable selector is always the
`MemberAnchor`-derived `StableSelector` / canonical signature / digest carried by
the anchor-carrying row or supplied by the wrapper.

### ResearchDiff projection

`ResearchChange` binds one catalog-declared inert native payload snapshot —
`ApiChange` or `CSharpRow` (anchor-carrying) or `IlRow` and the analysis signal
fields (body-substrate) — to one `ResearchSubjectKey` whose `Id` is the anchor
`StableSelector`, and to a cross-mechanism product `ChangeId` via
`FindingDescriptor`. It never erases the lower-layer typed payload and never
requires consumers to parse `Message`. Machine consumers query by `ChangeId`
through `HasChange`, `HasChangePrefix`, and `HasChangeCategory`; product
`ChangeId`s use fact concepts (`unsafe.stackalloc.added`, `il.hunk.changed`,
`csharp.return-expression.changed`), not incidental detail fields. `Message`
stays producer-owned presentation on either side of the join.
For a planned comparison, `ResearchBodyEvidenceChange` wraps this unchanged
payload snapshot with its owning work-item id and mechanism. The separate
session-body-presence arm carries no producer subject or Finding. Rendering and
grouping begin from the typed union and the presentation map, so equal
`StableSelector` values in two participants cannot collapse and a
producer-free body-presence difference cannot disappear.

## Consumer contract

Use `ImplementationComparisonQuery` for assembly-, package-, project-,
platform-, directory-, or workspace-scoped implementation comparison. The host
supplies the acquisition-owned sealed endpoint plan, typed pre-acquisition
question inputs, mechanism set, capabilities, and plan/Source budgets. The
query mints its plan-budget ledger first, charges the complete endpoint-slot
population, and seals the questions against that ledger before asking endpoint
owners to realize the plan under its endpoint facet. It then asks the
acquisition-owned comparison coordinator to validate the endpoint budget
receipts and produce the complete
`ComparisonEndpointPairingSlotOutcomeSet` from those endpoint outcomes through
that same endpoint facet. During `PopulationSealing`, ResearchQueries derives
query participant domains by exhaustive typed lowering and seals the exact
non-empty query correlation expansion. It then performs the only
`PopulationProjection`, proving bijective correspondence while lowering the
query questions, domains, correlations, bindings, and terminal evidence into one
`ResearchBodyEvidenceAdmissionPlan`. Research alone expands that plan into
Research scopes, terminal or side-local outcomes, exact/carried target
requests, attempts, and work items. After Research completion, ResearchQueries
validates the query/Research correspondence receipt and publishes the outer
result.
Every admitted side binding already contains the workspace-issued
selection/body role outcome. The query consumes it; it does not ask the host,
adapter, Metadata, or Research to reconstruct same-side role correspondence.
The query returns one ResearchQueries-owned completed
`ImplementationDiffResult`; no enrichment step exists.

Use `DirectImplementationComparisonOperation.DesignateMemberPair` when the
caller already owns two live `MetadataSource` values. The ResearchQueries-owned
factory explicitly authorizes only those two sources for this invocation and
captures their exact MVIDs and mints a distinct opaque source id for each side
in a `DirectMemberPairingDesignation`. It retains no workspace participant,
role manifest/binding, qualified input key, or `ArtifactParticipantPairing`;
those acquisition currencies do not exist until the operation admits the
designation under its ledger. Assembly identity may differ because the
designation is explicit comparison authority, not inferred correspondence.
It accepts no path, handle, method token/address, or display identity during
designation. The source ids are designation-local object bindings, not
participants or cross-version pairing evidence. The designation cannot outlive
either source.

Before execution, the caller asks that designation to bind each exact
`MethodDefinitionHandle` and relationship role to its exact designated
`MetadataSource` object. The factory rejects a foreign or wrong-side source
even when it is byte-distinct but has the same MVID, then creates an inert
`DirectDesignatedMethodAddress` from the designated reader. The caller passes
the designation, its old/new bound address values, a non-empty subset of the C#
and IL catalog mechanisms, and its budget to
`DirectImplementationComparisonOperation.Execute`. The Queries-owned operation
admits the slot, seals the direct question, and only then constructs the role
manifests, bindings, qualified keys, and admitted direct pairing under the
core-Queries direct-pairing factory. It requires each bound address's source id
to equal the exact source id from the matching operation-issued participant
binding. ResearchQueries then projects the complete query population into
Research-owned identities and gives the internal Research session only its
`ResearchBodyEvidenceAdmissionPlan`. The Research session lowers that plan into
one designated-pair work item keyed by the projected Research designation id
plus both exact addresses and roles. The core pairing id remains the exact core
designation id, and the correspondence receipt is the only connection between
it and the Research designation id.

The operation returns `ImplementationMemberDiffResult` before either source
can expire and cannot feed an assembly-wide Implementation Diff. Its completed
arm retains only inert query/Research correspondence, subjects, ledgers, native
snapshots, outcomes, absence reasons, and diagnostics; its failed arm retains
only the fixed-size operation failure. The completed receipt intentionally
retains the inert core designation id and participant-binding snapshots needed
to validate its query/Research correspondence. Neither arm retains the live
designation grant, live pairing or binding, either source, producer-native
object, or budget lease.
Public `ImplementationDiff.CompareMembers` and the current handle-only overload
are removed.

`match --implementation` retains its command-owned selector source only long
enough to designate the two exact methods and invoke the direct Queries
operation. It passes the returned result to a formatter overload for
`ImplementationMemberDiffResult`; it never invokes Research directly,
constructs `ImplementationDiffMember`, `ImplementationDiffResult`, or a
placeholder `ResearchComparison`, and the direct result retains both selected
subjects when their signatures differ. It renders retained direct diagnostics
and exits nonzero when `HasFailures` is true.

ReturnToSender and round-trip tools use the same designation plus Queries-owned
operation for original-to-emitted or emitted-to-emitted methods, including
differently named assemblies. Every caller first switches exhaustively on
`ImplementationMemberDiffEvidence`; a failed arm maps to its typed unavailable
state without enumerating ledgers. A completed arm then switches exhaustively
on the total direct outcome. `IsExact`, when retained as a convenience, is
exactly `Evidence is Completed && Outcome == Exact`. Authored rebuild maps
`Unavailable` to `ContextFailed`; round-trip and scope comparison map it to
their typed unavailable state. A
`NotApplicable` result maps to a typed no-implementation/not-applicable state,
not `ContextFailed`, `IlDifferent`, `Changed`, or `Exact`. `match
--implementation` renders the retained absence reasons and exits zero for
`NotApplicable`; it renders retained failure diagnostics and exits nonzero for
`Unavailable`. No consumer infers either state from empty rows or
`HasFailures` alone.

There is no direct authored-source overload. A caller that needs PDB,
SourceLink, or network work uses `ImplementationComparisonQuery`; a harness
with independently acquired source evidence owns its own typed result rather
than rewriting the product comparison.

Finding acquisition and cross-validation failures set the affected
work-item/mechanism ledger to
`Failed(FindingInspectionFailed | CrossValidationFailed)`.
`ResearchChangeKind.Failed` is the row projection of that operational
diagnostic, never a semantic `Changed` row in table, TSV, JSONL, or
programmatic consumers. `HasFailures` is therefore true, Source sees a failed
prerequisite and performs no I/O, and every selected CLI path exits nonzero.
When a semantic projection carries the corresponding typed failure, Research
keeps that richer row and suppresses the duplicate generic Finding failure.
Synthetic add/remove rows from the same failed C# hunk are omitted; genuine
body absence and independently decoded partial IL evidence remain visible as
non-verdict evidence attached to the failed ledger.

The `diff --finding csharp.line` and `diff --finding il.op` focused lenses read
those retained comparisons and render native `PairFinding` cases. Missing
members and methods without bodies remain distinct inspection states. IL
retention pairs the union of declared method identities, so added, removed, and
signature-changed methods are not lost by the semantic body-diff intersection.
Failed inspections render explicit failure rows instead of becoming empty
comparisons.

Use `ImplementationDiff.ToIlChanges` when a caller already has a scoped
`IlMemberDiffResult`, such as ReturnToSender comparing one original method to a
recompiled artifact method. This preserves typed IL diff data and projects the
same `ResearchChange` model used by assembly-wide Research diffs. Exact typed
diffs produce no IL changes, but callers may still retain the typed
diff in their own result model when exact proof matters.

Each IL change also retains its `IlBodyDiffResult`. Its total outcome is
`Exact`, `OperandDiff`, `OpcodeDiff`, or `Unavailable`. Exact means both bodies
are equal after the requested normalization. Unavailable means no comparison
verdict exists. For two bodyful sides, a planned session therefore records
`Failed(ComparisonUnavailable)` and retains the typed failure payload and
reason. For a resolved bodyless side, the session-owned body-presence
disposition applies before this producer outcome, so current
`OldBodyMissing`/`NewBodyMissing` payloads never turn body added/removed or
`Absent(NoBody)` into failure. Non-IL mechanisms do not carry this payload.

Use `ImplementationDiff.UnifiedLines(change)` only at presentation boundaries.
The durable model keeps the producer-owned typed display rows rather than a
third implementation-specific row family.

The `diff` command exposes this component through the explicit-only
`Implementation Diff` section. Its table schema is shared by Markdown, table,
TSV, JSONL, and lowered JSON:

```text
Record Kind             Evidence | Ledger Diagnostic | Query Diagnostic
Work Item               result-local id; empty for query diagnostic
Participant             inert label; empty for query diagnostic
Member                  inert label; empty for query diagnostic
Mechanism               catalog label or Query
Disposition             Compared | Absent | Failed; empty for query diagnostic
Difference              native difference when applicable
Change                  native change, Body Added, Body Removed,
                        Partial Evidence, or Diagnostic
Reason                  typed absence/failure/query reason
Evidence                producer line/fallback, session body-presence detail,
                        partial evidence, or diagnostic detail
```

For a `Completed` query outcome, logical projection begins from every
work-item/mechanism ledger disposition, not from the presence of producer
lines. Projection first forms typed native-line candidates, applies
changed/native-line filters, and only then decides whether a fallback is
required:

- `Compared(Exact, Producer)` intentionally projects no `Evidence` row;
- `Compared(Different, Producer)` projects one row per visible native line for
  each retained producer change. For any retained change with no line remaining
  — including a Body Signals change that supplies no unified line, or a filter
  that removes every candidate — it projects exactly one ordinary fallback
  `Evidence` row. The row retains that change's native classification and the
  ledger's difference classification and uses the typed producer detail/title
  projection as evidence. The non-empty complete producer-change set ensures
  the disposition always has at least one row;
- `Compared(Different, SessionBodyPresence)` projects one row per visible
  optional single-side display line. If none remains, it projects exactly one
  fallback `Evidence` row. Every row retains
  `SessionBodyPresence(BodyAdded | BodyRemoved)`: `Change` is `Body Added` or
  `Body Removed` and `Difference` is empty;
- every `Failed` disposition projects one mandatory `Ledger Diagnostic` row
  from its typed diagnostic. Presentation-safe native partial evidence may
  additionally project ordinary `Evidence` rows with
  `Disposition = Failed`, `Difference` empty, and `Change = Partial Evidence`;
  it supplies no semantic verdict, may be filtered/windowed, and cannot replace
  or suppress the mandatory diagnostic;
- every `Absent` reason the mechanism catalog marks visible projects one
  mandatory `Ledger Diagnostic` row. Source
  `MissingMapping` is visible; Source `NotEligible` is not. An explicit direct
  request renders all of its retained absence reasons.

For a `Failed` query outcome, the same section projects one mandatory
`Query Diagnostic` row for the primary failure and one for each cleanup
diagnostic. It does not invent a work item or completed result.

Producer and session fallbacks are ordinary typed evidence, not diagnostics or
synthetic producer lines. Because fallback selection happens after native-line
filters, filtering cannot erase a difference-bearing `Compared` disposition.
Diagnostic rows are views over ledger/query identity and reason, not
`ResearchChange` or synthetic producer evidence. Changed-only and native-line
filters cannot suppress diagnostics. `--rows` and `--tail` then select the
resulting `Evidence` records, including fallbacks and any visible partial
evidence; every mandatory diagnostic record is appended afterward in stable
work-item/mechanism/reason order and does not count toward that semantic row
window. JSONL uses the same columns and serializes `recordKind` as
`evidence`, `ledger-diagnostic`, or `query-diagnostic`; it emits no second
table or ad hoc object. Thus `--jsonl --rows 1` emits at most one evidence
record plus every mandatory diagnostic record. Raw rendered-line limits such
as `-n` remain presentation truncation and do not change exit status. A
nonzero result can therefore never become success-shaped through semantic row
selection.

Column projection is integrity-aware. The user-requested `--fields` or
`--columns` set narrows ordinary evidence columns, but when the typed row
population contains a mandatory diagnostic the effective projection is the
requested set union `ImplementationDiffSectionDescriptor.IntegrityColumns`:
`Record Kind`, `Work Item`, `Participant`, `Member`, `Mechanism`,
`Disposition`, `Change`, `Reason`, and `Evidence`. That ordered descriptor is
also the source for those columns' section-schema declarations; a set/order
equality gate prevents the two uses from drifting. The CLI output adapter
computes the effective projection before constructing
`MarkoutWriterOptions.Projection`; Markout remains a renderer and does not
infer required columns from cell text. This may promote a requested Vector to
a Table for an incomplete comparison. It is the same typed incomplete result,
not an ad hoc second shape. Successful diagnostic-free output keeps ordinary
projection behavior. TSV, JSONL, and lowered JSON retain the same effective
columns/property names, so projecting an empty diagnostic field such as
`Member` cannot strip the discriminator, failure identity, reason, or detail.

Every format begins from `ImplementationDiffResult.WorkItems`; repeated display
values never merge distinct work-item ids, and structured forms retain the
result-local opaque id plus participant label. `Difference` contains the IL
body outcome for IL rows and is empty for C# rows, keeping participant,
mechanism, result, edit kind, and evidence as separate dimensions.
The section binds `ImplementationComparisonQuery`. During migration, its
current input still carries retained assembly descriptors, reference resolvers,
and body indexes; the query opens those descriptors for the offline C# and IL
producers. The target input replaces that reader-opening shape with the sealed
endpoint plan and typed question inputs above. Endpoint outcomes, budget
receipts, pairings, and target requests are internal staged results of the
query-owned operation, never host-supplied target input. The CLI adapter's
current path-backed descriptors are an acquisition boundary, not part of the
target query contract.
With `--pdb-source`, the query acquires eligible members' endpoint PDB and
SourceLink bodies, verifies document checksums, and adds a separately labeled
`PDB Source` ledger. Missing mappings, acquisition failures, and budget
exhaustion remain visible rather than falling back to decompiled C#.
The authored A→IL lane reuses the final RTS shell/request but compiles with
portable-PDB-recorded options when available; the decompiled B→IL lane uses the
RTS compile context. `BuildContext` and determinism verdicts therefore remain
part of interpreting any Exact/IlDifferent disagreement.
Package, platform, and local-library ranges use the same acquisition path as the
default API diff; `--type`, `--member`, row limits, table, TSV, and JSONL
projection continue to apply. The CLI consumes this product component and does
not invoke or reconstruct the C# and IL producers independently.

## Migration

| Current surface | Target |
| --- | --- |
| `AssemblySetResolver` endpoint-to-flat-list comparison input | Sealed `ComparisonEndpointOutcomeSet` plus `ComparisonEndpointPairingSlotOutcomeSet`; exact request/outcome and plan-slot/outcome equality, required endpoint-key/slot-arm side agreement, 1:N participant manifests, and disjoint total participant-outcome partitions preserve admitted bindings and complete terminal payloads |
| `PackageAssemblyContextRoles` nullable surface lookup | Extend this product-owned same-side role authority to issue one sealed `AssemblyParticipantRoleManifest` directly from its complete role sets and exact correspondence input, with `SameSelection`, `Implementation`, or typed `ReferenceOnly` bindings before cross-version pairing; the nullable compatibility lookup is not a comparison input |
| `AssemblyResolutionProvenance` pattern matching in Research | Query-owned opaque `ArtifactParticipantPairing.Id` plus side-local binding, then one ResearchQueries-owned bijective `PopulationProjection` into disjoint Research participant/binding/domain identities; Research receives no core-Queries currency |
| Package/project/directory occurrence index | Logical participant slots; duplicate slots fail ambiguous |
| `ResearchDiffOptions.TypeFilters` / `MemberTargetIdentities` | Typed pre-acquisition question inputs declared by the host, charged and sealed only by ResearchQueries into query questions with exact selection request and endpoint scope. Pairing emits complete core slot outcomes; `PopulationSealing` seals query-owned admitted/absent/terminal domains, inert domain receipts, and correlations; `PopulationProjection` bijectively lowers that complete population into a Research-owned admission plan and inert Research domain receipts. Research independently expands Research scopes, scope receipts, requests, attempts, work items, and base presentation entries. The outer receipt retains query, correspondence, and Research receipts without a live pairing or cross-layer currency |
| `CSharpBodyDiff` raw-key/occurrence index | Scope-local participant pairing plus `MemberBodyCorrespondenceKey` and role |
| Correspondence collision rejected or selected by occurrence | `CorrespondenceAmbiguousKey` retaining every colliding side-local attempt, plus per-attempt counterpart-unavailable work items |
| Scope-free `CorrespondedKey` and cross-scope aliases | Scope-local corresponded keys, collision buckets, aliases, and taint; independent scopes remain independent proof domains |
| Independent C#, IL, body-signal, Finding, and Source populations | One internal work-item plan projected to complete mechanism ledgers |
| Missing-body producer result treated as operational failure | Session-owned body-presence lowering before producer invocation, with typed `BodyAdded` / `BodyRemoved` comparison evidence retained through Research and output; only two-body verdict-less results become `Failed(ComparisonUnavailable)` |
| Native unavailable result retained as a two-body comparison | `Failed(ComparisonUnavailable)`; `Compared` requires exactly one exact-or-different verdict |
| Finding failure/divergence emitted beside a successful semantic result | One atomic mechanism disposition: `Failed(FindingInspectionFailed \| CrossValidationFailed)` with partial payload retained only as non-verdict diagnostic evidence |
| Public `ResearchComparison` constructors and standalone C#/IL adapters | Public factories can issue only `UnscopedResearchComparison`; only internal session completion issues the body-evidence arm |
| `ResearchChangeMechanism.AllAvailable` default | Explicit API + Body Signals + IL + C# synchronous default |
| `ResearchComparison.Combine` | `CombineUnscoped`; planned inputs reject |
| `ImplementationDiff.FromResearchComparison(ResearchComparison)` | Remove; ResearchQueries alone composes a `ResearchBodyEvidenceComparison` with query acquisition and population-projection receipts into an outer result |
| `ImplementationDiff.CompareAssemblies` | Remove; assembly-scoped callers construct typed endpoints and execute `ImplementationComparisonQuery` |
| `ImplementationDiff.Compare(ResearchDiffInput, ResearchDiffInput, ...)` | Remove; the query/session owner forms one planned Research comparison |
| `ImplementationDiff.Compare(IReadOnlyList<ImplementationAssemblyInput>, ...)` | Remove with `ImplementationAssemblyInput`; typed endpoint outcomes and participant bindings replace reader-opening inputs |
| `ImplementationDiffOptions` | Split mechanism selection into catalog ids on the query/session input, target selection into immutable product-owned typed requests embedded in pre-acquisition question/endpoint-slot declarations followed by exact admitted/endpoint-absent/terminal domain expansion (including ambiguous/failed pairings), complete scope outcomes, and side-local requests for admitted scopes, and changed/window options into presentation-only values |
| `ImplementationDiffMechanism` / `AllAvailable` | Retire in favor of the closed mechanism catalog; no context-free host-owned mechanism set |
| Public `ImplementationDiffResult` / `ImplementationDiffMember` constructors, `SourceComparison` init, and record copying | Move query-level result ownership to ResearchQueries; sealed non-record results have internal constructors, get-only immutable state, and no post-completion enrichment |
| `ImplementationDiffResult.Members` and planned `ResearchComparison.BySubject()` | Work-item-keyed result projection and the producer/session `ResearchBodyEvidenceChange` union; any producer-subject view retains its Research work-item id |
| `ImplementationDiff.CompareMembers` handle-only pairing and shared `ImplementationMemberDiffResult.Subject` | ResearchQueries-owned `DirectImplementationComparisonOperation.DesignateMemberPair` plus `Execute`; the designation retains two live sources/MVIDs and one opaque object-bound source id per side, while its factory mints the exact side/source-bound method addresses separately. `Execute` admits the slot, seals the C#/IL-only question, then creates the direct pairing, role manifests/bindings, and side-qualified keys. `PopulationSealing` seals the query domain/correlation and inert domain receipt before `PopulationProjection` lowers that query population into Research-owned identities and Research plan expansion begins. Completed evidence retains separate subjects and non-empty ledgers; any pre-work-item or budget failure returns the fixed-size failed evidence arm, never fabricated dispositions |
| `MatchCommand.BuildImplementationDiffView` direct result construction and unconditional zero exit | Invocation-scoped direct designation passed to the Queries-owned direct operation plus a formatter overload for its product-issued `ImplementationMemberDiffResult`; retained direct failure returns nonzero |
| Direct `IsExact`/empty-row inference | Exhaustive `Completed \| Failed` evidence handling followed by `Exact \| Different \| NotApplicable \| Unavailable` reduction only for completed non-empty ledgers; failed evidence is always unavailable, all-absent completed results retain reasons, and neither masquerades as exact or changed |
| ReturnToSender and round-trip `CompareMembers` calls | Product-issued direct designations passed through the Queries-owned direct operation over original/emitted or emitted/emitted sources; differing assembly identities remain valid, failed ledgers map to context-failed/unavailable, and all-absent ledgers map to typed not-applicable rather than semantic difference |
| `CompareMembersWithPdbSource` | Remove; async acquisition belongs to `ImplementationComparisonQuery` |
| `WithPdbSourceComparisons` | Remove; finalized results accept no new ledger |
| CLI changed-row PDB-source enrichment | Dependency-gated Source projection inside one query lifetime |
| CLI API-only failure exit | Include assembly and direct result `HasFailures` in every selected output path |
| Query/cleanup failure without result currency | `ImplementationComparisonQueryOutcome.Failed` with primary and cleanup diagnostics, no partial result, typed output, and nonzero exit |
| Ad hoc planned/direct lifecycle sequencing | Catalog-declared operation stages with exact per-kind order, including `PopulationSealing`, `PopulationProjection`, distinct `ResearchCompletion`, and query `Publication`, plus exact owner, facet, authorized-dimension, operation-failure, item-result, cancellation, implementation-registration, and boundary-fixture set equality |
| Unbounded endpoint, planning, producer, retained-result, and cleanup-diagnostic work | Queries-owned `BodyEvidencePlanBudgetLedger` minted before endpoint-slot/question/endpoint realization, shared by planned and direct operations, and used for pairing-, query-population-sealing-, population-projection-, plan-expansion-, retained-result-entry-, producer-input/work/scratch-, native-lowering-, and pre-ownership cleanup-reservation charges; planned exhaustion returns one catalog-routed typed outer failure, while direct exhaustion discards staged ledgers and returns catalog-routed fixed failed evidence |
| `ILInspector.Research` reaching into core Queries currencies or owning query results | Keep the one-way `ResearchQueries -> Research` project edge, add `InternalsVisibleTo("DotnetInspector.ResearchQueries")` in Research, prohibit any upward Research project reference, expose internal Research admission/session factories only to ResearchQueries, and move outer query results to ResearchQueries |
| Unified-line-only CLI rows | Ledger-first same-schema evidence/diagnostic records; after native filtering, every producer/session `Compared(Different)` with no visible line gets its own typed fallback, while presentation-safe failed partial evidence may accompany but never replace its diagnostic; semantic row windows count evidence but never count or suppress mandatory ledger/query diagnostics |
| Unqualified `options.Columns` / `options.Fields` forwarding | `ImplementationDiffSectionDescriptor.IntegrityColumns` drives both schema declaration and the effective Markout projection: user projection narrows ordinary evidence, while a row population with mandatory diagnostics retains the discriminator, failure identity/context, reason, and evidence columns in every table-derived structured mode |

## Gates

The target lifecycle is unverified until these gates exist:

| Gate | Surface | Fails if |
| --- | --- | --- |
| Endpoint-manifest totality | Artifact adapters + Queries over package `Preferred`, explicit/all TFM/RID, distinct/shared/absent implementation roles, project outputs/dependencies, platform, directory, two-bundle embedded workspace, cross-source, explicit endpoint absence, wrong-arm requests, and single/mixed/two-sided failed endpoints | Request and outcome `(Side, Id)` sets differ; the pairing plan omits or repeats a request or places it in an arm that disagrees with `ComparisonEndpointKey.Side`; a duplicate/rekeyed/cross-side outcome occupies another request; failed/omitted acquisition is treated as `Absent`; a failed slot's outcome map differs from its exact requested arms, its absent-arm map differs from its exact `Absent(proof)` arms, any outcome/absence key disagrees with its source arm, its terminal-key set is empty/inexact, one terminal reason/diagnostic/absence proof is lost, or a realized opposite is reclassified one-sided instead of retained as tainted input summary; one endpoint is forced to one participant; a realized endpoint has an empty/unsealed manifest; a real selected inventory differs from its manifest; the manifest differs from the workspace role generation, loses or duplicates a role participant, treats shared-group reuse as correspondence, fabricates an implementation for a reference-only surface, or reconstructs role correspondence from asset layout; an embedded pair lacks a host-issued paired designation or uses workspace context/`ContentRef` as cross-side identity; or pairing differs from a failure-free manifest union |
| Participant correspondence | Workspace roles + adapters + Queries over same/distinct/reference-only selection/body roles, repeated/equal-identity, colliding/reordered manifest-local input ids, duplicate-slot, wrong-arm/swapped/foreign/missing/extra payload-identity, same-source direct inputs, absent/available content-digest, and reordered-input fixtures | The acquisition slot-outcome set differs from the endpoint plan; a planned participant key omits the endpoint key, side, manifest revision, or local id; a direct participant key omits the designation id or side; a qualified key's side disagrees with its pairing binding arm; equal local ids across manifests/direct sides collapse, make valid outcomes overlap, or permit silent shortening; a participant outcome set does not partition its exact qualified manifest union; an outcome's input set differs from its admitted binding identities or complete ambiguity/failure payload identities; participant pairing requests or requires a content digest; the adapter, Implementation Diff, Metadata, or Research reconstructs same-side role correspondence or translates a nullable compatibility lookup; path/version/TFM/RID/`ContentRef`/digest/MVID/registration/role-manifest id, physical method/body address, or occurrence becomes cross-version pairing identity/evidence; duplicate logical slots select one; an `Ambiguous` or `Failed` pairing outcome exposes an admitted binding or is omitted, reconstructed downstream, or loses its exact slot/outcome id/kind, complete upstream payload, reason, or diagnostic when lowered to a terminal domain; direct admitted keys differ from the two side-qualified designation inputs or bindings do not use `SameSelection`; or adding a participant renumbers another |
| Population-projection separation | Project-reference/friend-access/forbidden-call-site/source-architecture inventory + planned/direct bijection fixtures with reordered, missing, extra, duplicate, wrong-side, wrong-operation, rekeyed, and payload-divergent endpoint/input/binding/domain/question/correlation/terminal/designation values | `ILInspector.Research` references core Queries or ResearchQueries; Research owns an outer query result; ResearchQueries lacks either owner's exact production friend access, another production assembly receives that access or invokes an internal structured-population constructor, or either owner exposes a public construction path that bypasses the friend boundaries; a Research model field or session parameter uses `ArtifactParticipantPairing`, `ArtifactParticipantInputKey`, `AssemblyParticipantRoleManifest.Id`, `EndpointSlotFailure`, `DirectMemberDesignationId`, `DirectDesignatedSourceId`, a core query operation id, or another core-Queries currency; the population projection occurs after Research selection; the Research operation id is defaultable, caller-mintable, uncharged, or lacks exactly one core query-operation antecedent; a core operation has two Research images; the Research admission plan, projection facet, comparison plan, session, or completion receipt omits or substitutes that Research operation id; any other projection map is not bijective; a Research endpoint, input, participant, binding, domain, question, correlation, terminal-evidence, or direct-designation identity lacks one exact query antecedent or has two; a participant/binding pair is not issued and charged by exactly one projected core-binding entry; an admitted Research domain loses or changes its pairing kind or either side's exact binding-or-absence arm, or Research infers absence from a missing binding; side, role, MVID, registration, assembly identity, terminal kind, reason, diagnostic, proof, selection request, derived intent, endpoint scope, or direct address/role changes across the boundary; a direct source id does not map to the exact query binding whose image is the Research participant; a direct Research participant id is not the projected image for that exact binding; a correspondence is reconstructed from text/numeric equality; a live core value enters Research; a population-projection entry or retained Research copy bypasses its own entry/character charge; publication accepts mismatched operation, query, correspondence, and Research receipts; or a completed outer result is constructible inside Research |
| Selection-correlation totality | CLI/host + Queries and Research over explicit/enumerative questions spanning one/multiple endpoint slots and admitted pairings, two-sided endpoint absence, single/mixed/two-sided endpoint failure, all-failed and mixed admitted/failed endpoint populations, all-ambiguous and mixed admitted/ambiguous participant-pairing outcomes, one question over multiple slots with colliding slot-local terminal ids, multiple questions over one ambiguous slot, zero-result filters, reordered inputs, omitted/substituted/mixed/reparented questions/requests/scopes/domains, inert receipt lowering, and direct lowering | A query question does not embed its exact immutable typed selection request; a request is omitted, rekeyed, substituted, or disagrees with its derived intent; the pre-acquisition question ids or endpoint-slot scopes differ from the sealed query question set; a question names no slot or an unknown slot; the union of question slot sets differs from the pairing-plan slot set; an endpoint slot expands to no query domain or a sealed domain enters no query correlation; any `Admitted(Paired \| BeforeOnly \| AfterOnly)`, `Ambiguous`, or `Failed` participant outcome lacks exactly one matching admitted/terminal query domain; terminal participant domain keys omit the endpoint-slot id or collide when different slots reuse one local outcome id; pairing outcomes differ from the sealed slot-owned admitted/endpoint-absent/failed query-domain set; a failed endpoint domain loses any requested outcome key, absent-arm proof, terminal payload/diagnostic, or realized tainted-input summary; a query question lacks exactly one query correlation; a query correlation changes its question/selection/derived intent/slot scope, lacks the exact domain expansion of those slots, omits an endpoint-absent, ambiguous, or failed domain, or duplicates a domain; Research scopes are not bijective with the projected `(Research correlation, Research domain)` pairs; a scope is missing, extra, belongs to multiple Research correlations, disagrees on correlation/domain, or is omitted before selection; a terminal Research domain fabricates a participant/side inventory/request; a terminal pairing domain loses its projected payload/reason/diagnostic or lacks a question-local participant-failed work item; query, correspondence, and Research receipt question/domain/correlation/scope sets differ; a selected or failed correlated scope still yields no-match; a correlation made entirely of endpoint-absent/admitted-two-sided-absent scopes does not yield typed no-match; an all-ambiguous or all-failed correlation cannot complete with retained failures; an enumerative terminal failure becomes silent; an enumerative proven-empty correlation invents evidence; or direct lowering lacks its sealed pre-pairing question, query slot outcome/domain/correlation, projection maps, and Research scope |
| Body-target attempt totality | Research + Queries over same/distinct/reference-only selection/body roles, AssemblyRef-version-only drift, accessor roles, signature drift, same-scope and split-across-scope correspondence-key collisions, multi-participant selectors, overlapping selectors, endpoint/two-sided participant absence, selection/resolution failure, incomplete `All`, bodyless methods, and participant ambiguity/failure | An admitted scope side is not exactly `Selected`, proven `Absent`, or `Failed`; an endpoint-absent or participant-failed scope has side outcomes or target requests; an endpoint-absent scope invents a work item or loses either proof; an ambiguous/failed participant domain lacks exactly one question-local terminal work item and complete failure ledgers or invokes selection/resolution/producers; the completed participant-domain/scope outcome set differs from the sealed manifest/query input or plan; a two-sided absent admitted scope disappears, invents a work item, or is inferred from an empty work-item set; selected request ids differ from their requests; a failed/incomplete/ambiguous census becomes absence or a shortened selected set; a target request is not already scope/side/participant/role-generation scoped; selection enumerates the implementation role or body resolution uses the selection surface when a distinct implementation was designated; a reference-only binding invokes Metadata, becomes `Absent`/`Bodyless`/semantic add-remove, or lacks one unavailable attempt and complete failure ledgers; an exact target omits or bypasses relationship-role validation; one strict target is fanned across versions/participants; a selection failure invokes body resolution; one request lacks exactly one attempt; one attempt maps to zero/multiple work items; a same-scope collision lacks one side-scoped ambiguity work item keyed by the authoritative `CorrespondenceAmbiguousKey` and retaining all colliding attempts; a split-across-scope collision creates ambiguity, duplicate-key rejection, or cross-scope aliasing; a dependent opposite attempt lacks its own counterpart-unavailable item; a work item lacks attempts or question-local failure identity; correlation ids authorize matching; `AttemptMap`, aliases, and discriminated keys differ; remove/add shares one request/key/attempt; bodyless becomes a resolution failure; or aliases weaken exact/strict/correspondence validation |
| Counterpart and body-presence disposition | Research C#/IL/body-signal tests over bodyful/bodyful, resolved bodyless/bodyful and bodyful/bodyless, proven-one-sided bodyful/bodyless, failed selector, failed body-key resolution, correspondence ambiguity, ambiguous/failed participant pairing, failed endpoint, and bodyless/bodyless scopes | Two bodyful sides bypass the producer; producer authority lacks one native exact/different verdict or carries body-presence evidence; a resolved or proven-one-sided exactly-one-bodyful item lacks a session-authoritative `Compared(Different, BodyAdded \| BodyRemoved)` value or exactly one matching `ResearchBodyEvidenceChange` session arm; optional single-side display evidence supplies another verdict; a missing-body native result becomes `Failed(ComparisonUnavailable)`; a failed/incomplete/ambiguous counterpart or terminal participant domain produces semantic pair/add/remove or Source eligibility instead of its typed terminal failure; an attempt appears in both failure items; a failure-free unambiguous matched coordinate is tainted; no bodyful side is not `Absent(NoBody)`; or bodyless becomes a target failure |
| Planned population ownership | Source-architecture, project-reference, completed-result type-closure, and non-vacuity mutations | A query plan, Research admission plan/session/projector, or live correspondence token escapes its owner; a producer enumerates or filters its own population; Research exposes a public planned/direct executor, concrete budget ledger, caller-provided budget authority, core-Queries currency, or outer query-result constructor; ResearchQueries does not own outer results and publication; a completed-result constructor or retained field can carry a live pairing, role manifest/binding, participant, source, reader, group, workspace/session, plan, callback, lease, producer-native object, or content-opening authority; a public constructor, `init`, or record copy fabricates/mutates a planned result; or removing, adding, or duplicating one Research disposition or one query/Research correspondence entry does not reject Research completion or publication |
| Completed payload inertness | Source-architecture + mechanism-catalog payload-type set equality + NativeAOT/Browser-compatible lowering inventory + success/failure/partial-evidence post-disposal fixtures | A mechanism lacks one catalog-declared typed snapshot DTO/lowering; a host or producer can add an unregistered payload type; a snapshot DTO field/type closure can reference a reader, source, participant, role object, workspace/session, callback, lease, stream, `ContentRef`, or other opening/selection authority; lowering references `System.Reflection`, dynamic loading/code generation, or a runtime payload-graph walker instead of its catalog-declared typed operation; the lowering closure introduces a NativeAOT trim warning or a Browser/Wasm-incompatible dependency; a producer-native object enters a ledger, survives beyond its callback, accumulates while later work runs, or reaches `Complete`; lowering/charging is deferred until completion; failed partial evidence skips immediate snapshotting; a snapshot loses typed native evidence needed by a ledger/change; or full result enumeration after disposal touches producer state |
| Mechanism dependency totality | Research + Queries with empty selection, Source-only selection, C#-only change, IL-only change, both exact, proven one-sided change, failed/ambiguous counterpart, native comparison unavailable, Finding inspection/cross-validation failure, and presentation-filter mutations | An empty set or Source without a requested local mechanism is accepted; a known required dependency is absent; Source omits a requested local prerequisite; a failed prerequisite, counterpart, correspondence, native comparison, Finding inspection, or cross-validation performs I/O; a proven one-sided change becomes `NotEligible`; no-change performs I/O; or presentation affects eligibility |
| Synchronous mechanism ownership | Research API, Queries-owned direct-operation, and harness tests over `ResearchChangeMechanism` and `ImplementationDiffMechanism` | Either default/context-free `AllAvailable` includes a host mechanism; synchronous `Compare` accepts/ignores Source, ReturnToSender, or unknown flags; a direct request is empty, accepts/ignores API, Body Signals, Source, ReturnToSender, or an unknown mechanism, or selects anything outside C#/IL; a retired assembly/direct overload remains; a caller bypasses `DirectImplementationComparisonOperation` to invoke a Research producer; or a host runner does not declare its complete set |
| Operation lifetime | Queries + CLI with revoked authorization, borrowed sessions, direct designation expiry, package roles with absent/shared/separate implementation groups, Research-completion/publication validation failure, success-plus-cleanup failure, primary-plus-cleanup failure, cancellation with successful/failing cleanup, post-disposal full-result enumeration, and single-threaded awaited reentrancy | Admission/question sealing/endpoint realization/participant pairing/population sealing/population projection/Research plan expansion/projection/Research completion/publication escape one current planned lease; a direct operation escapes one current direct lease or its designation/source lifetime; the assembly/package CLI opens a reader/session around Research; direct `match`, ReturnToSender, or round-trip use invokes Research without the Queries-owned operation; a borrowed session is disposed; a package realization plan's exact distinct group identities, cleanup reservations, and actually owned distinct groups differ for an absent, shared, or separate implementation role; an owned resource, lease, scratch reservation, or native result transfers into the operation before its fixed cleanup slot and complete bounded diagnostic text range are charged; cleanup allocates/grows a diagnostic collection, retains an exception graph, writes outside a reservation, or requests budget; an owned resource leaks on success, failure, or cancellation; a completed result reaches disposed participant/group/source/reader state or cannot enumerate every query/correspondence/Research receipt, ledger, native snapshot, change, and presentation value after cleanup; a failed query lacks the typed outer arm or exposes a partial/completed result; cleanup replaces a non-cancellation primary failure or loses a reserved diagnostic; successful cancellation cleanup returns an outcome instead of propagating cancellation; failed cancellation cleanup does not supersede cancellation with `Failed(Cleanup)` and every bounded typed cleanup diagnostic; `ImplementationDiffResult.HasFailures` absorbs query failure; or Browser/Wasm requires threads/blocking |
| Operation-stage inventory | Catalog-derived source-architecture/set-equality tests + planned/direct order, public-phase image, result-switch, cancellation-switch, and stage-removal non-vacuity mutations | The descriptor catalog, planned pipeline, direct pipeline, owner registrations, lease-facet adapters, allowed `(operation kind, stage, dimension)` edge set, budget methods, operation-failure routes, item-result routes, cancellation routes, or boundary-fixture sets differ; a planned stage is omitted/duplicated/reordered; direct omits a stage other than endpoint realization or the catalog-declared planned-only dependent stages; a stage lacks its exact per-kind owner, budget-authority set, dimension set, operation-failure route, item-result route, or cancellation policy; the exact set image of applicable planned/direct failure routes differs from its public phase enum; an applicable stage maps to zero or multiple public phases; an item callback returns from a stage that declares no item result, or an operation failure is lowered as an item disposition; a planned stage lacks its cancellation switch or the synchronous direct path accepts cancellation; a dimension is authorized by no stage for an applicable operation kind or a stage charges a dimension absent from that exact kind/stage edge; a host/harness registers a stage; direct participant pairing precedes question sealing; query domains/correlations/receipts are sealed outside `PopulationSealing`; Research domain receipts are retained outside `PopulationProjection`; Research scope receipts or base presentation entries are retained outside `PlanExpansion`; population projection does not occur after population sealing and before Research plan expansion; Research owns query population sealing/publication or ResearchQueries owns Research completion; Research completion or publication charges or copies a variable entry, string, byte payload, or presentation value instead of validating and fixed-size sealing the already charged immutable collections; removing endpoint realization, direct admission/question sealing/participant pairing, population sealing, population projection, plan expansion, either producer preflight/projection boundary, dependent input acquisition, Research completion, publication, or cleanup remains green; or stage metadata is inferred from display text/reflection rather than the closed typed catalog |
| Planned population budget | Queries + endpoint-realizer + acquisition-coordinator + population-sealing + population-projection + Research plan-expansion/completion + query-publication boundary tests at one below/equal/one above every default, count multiplication, integer overflow, direct/planned, native/Browser, and raised-limit authorization | The budget ledger is minted after endpoint-slot preflight, question sealing, endpoint realization, or participant pairing; a crossing stage can run without its complete owner-local operation-stamped authority set; a query-side endpoint/direct-pairing/Source-cleanup/authored-source facet names a different core query operation, the Research projection facet names a Research operation not bijectively mapped to that query operation, any facet exposes foreign dimensions, any plan-budget facet is backed by a different concrete plan ledger, or an authored-source facet is backed by a different authored-source ledger; endpoint slots are not charged before acquisition; a host seals immutable questions; a question input is copied before its entry/variable payload charge; an endpoint materializes a role binding, manifest entry, outcome payload, or query-owned variable copy before reservation; a pairing owner retains pairing-authored candidate/affected/failure payload or diagnostic characters without a `ParticipantPairing` charge; a direct factory materializes a role manifest, binding, qualified key, pairing, or outcome before its two participant inputs are charged; any endpoint receipt differs from its own endpoint's realized manifest count or independently measured retained outcome characters, aggregate receipt sums differ from the exact realized union/payload sum, or compensating endpoint-local errors pass; a realized union is qualified or partitioned before those equalities are validated; population sealing retains a live query domain, inert domain receipt, correlation-domain entry, or variable payload without its complete multiplied count/character charge; population projection retains the Research operation mapping, any Research admission value, inert domain receipt, correspondence-map entry, or variable payload without its own entry/character charge, or treats an earlier query charge as prepayment; Research plan expansion retains a live scope, inert scope receipt, request, attempt, work item, ledger, base presentation entry, diagnostic, or variable payload without its own charge; a producer projection retains a snapshot or presentation detail before its own charge; any resource transfers into ownership before its cleanup slot and complete bounded diagnostic text range are charged; cleanup creates a new collection or variable payload; Research completion or query publication copies a variable payload instead of zero-copy sealing already charged immutable values; any endpoint-slot/input/question/selection-request/query-domain/query-domain-receipt/query-correlation/projection entry/request-or-receipt character/correlation-scope/scope-receipt/target-request/attempt/work-item/disposition/snapshot/presentation/cleanup-reservation path bypasses its declared ledger; a Source-owned resource reserves cleanup storage from the authored-source ledger instead of the concrete plan ledger; a zero-match question retains uncharged selectors/filter operands; planned/direct operations mint different concrete ledger types or Research/callers can mint, replenish, or substitute one; a host raises a default without `BodyEvidencePlanBudgetOverrideCapability`; an exhaustive selector, Source override, per-artifact limit, or `InspectionCost` is accepted as that grant; overflow wraps; exhaustion truncates a census, correlation, endpoint outcome, correspondence map, Research plan, ledger, snapshot, cleanup diagnostics, or output; a partial manifest/pairing/plan/result escapes; a direct failure before work-item creation fabricates a ledger disposition or completed empty population instead of the fixed-size failed arm; or failure omits phase/dimension/limit/charge |
| Producer work budget | Mechanism-catalog phase/estimator set equality + planned/direct exact-boundary fixtures + IL/Finding/Source ordered-match canaries + cancellation/failure allocation instrumentation | A mechanism lacks one compile-time typed phase or conservative estimator; the complete `(work item, mechanism)` estimate-key set for either phase differs from that phase's exact producer-eligible population; prerequisite aggregate input/work totals and every peak are not preflighted before the first prerequisite callback; dependent eligibility/input acquisition does not follow complete prerequisite ledgers; Source acquisition invokes `TextFindings.Inspect`, constructs comparison keys, or compares before dependent preflight; dependent aggregate input/work totals and every peak are not preflighted before the first dependent callback; an estimator decodes/canonicalizes/decompiles/inspects Findings/diffs or uses unadmitted content; input/work/scratch arithmetic wraps; ordered matching omits its full checked cell product, matrix, or producer-owned arrays; a producer starts without the current operation-stamped projection facet or before its live-scratch reservation succeeds; ordinary concurrent scratch contention becomes exhaustion instead of bounded waiting; actual catalog-declared scratch can exceed its reservation; failure/cancellation leaks live scratch, replenishes cumulative work, or starts another callback before native-result release; a snapshot charge substitutes for producer-work preflight; an unestimable producer runs; planned exhaustion returns partial success; direct exhaustion does not discard staged ledgers and return `ImplementationMemberDiffEvidence.Failed`; a failed direct arm carries a work item, ledger, native snapshot, or artifact text; scheduling changes which items are successful; or estimator/lowering code introduces reflection, trim warnings, or a Browser/Wasm-incompatible dependency |
| Authored-source budget | Queries boundary tests at one below/equal/one above every default, cached/uncached, retry/redirect, embedded/external PDB, shared documents, native/Browser transport-visible operations, varied scheduling, and raised-limit authorization | Any query-time PDB/source path lacks the same non-optional ledger lease; any operation/byte/decoded-text/retention/concurrency path bypasses accounting; a host raises a default without an invocation-scoped `AuthoredSourceBudgetOverrideCapability`; static `InspectionCost` is accepted as that grant; per-item/redirect limits replace the aggregate; exhaustion publishes any eligible success or scheduling changes an eligible item's disposition kind; or failure omits dimension/limit/charge |
| Direct-member pairing authority | ResearchQueries operation, core-Queries direct-pairing factory, `match --implementation`, ReturnToSender, and round-trip exact-address tests with equal/different assembly names, unequal correspondence keys/roles, same path/different MVID, same token/different module, byte-distinct same-MVID sources, same-source opposite sides, wrong-source/wrong-side direct keys, pre-work-item failures, and designation lifetime expiry | A public `ImplementationDiff.CompareMembers` remains or accepts raw sources/handles; a caller bypasses the Queries-owned direct operation; Research or a caller mints a core designation/source id; the pre-operation designation contains a direct/workspace participant, role manifest/binding, qualified input key, or `ArtifactParticipantPairing`; its source ids are forgeable, not distinct per side, or not bound to the exact source objects; a method-address factory accepts a naked `MetadataMethodAddress`, foreign source, wrong side, invalid row, or same-MVID substitute instead of minting from the designated source's reader; the operation creates the direct participant/pairing before ledger admission or question sealing, or mints a pairing id different from the designation id; the participant pairing/designation carries a physical method address instead of receiving a designation-bound value only through `DirectMemberComparisonInput`; a direct participant/input binding does not carry the exact designated source id; direct lowering lacks its own charged slot, pre-pairing sealed question, query population-sealing domain/correlation, population-projection correspondence, Research scope, or non-empty completed work item; the core designation/source id or pairing enters a Research model; a designated pair requires assembly/key/role equality or invents one shared subject; cannot lower to one Research-owned designated-pair key containing the projected designation plus both exact addresses/roles; an endpoint path can mint that key; a direct input key's side/source id disagrees with its binding arm; pairing derives from path, occurrence, display, token, MVID, or reader equality; it outlives a source; the completed direct result retains a live designation grant, live participant/pairing/binding, source, or lease, or loses the inert designation/source/binding snapshots required by its correspondence receipt; the failed arm fabricates a work-item/ledger disposition or contains artifact text; feeds assembly-wide comparison; or bypasses source/address/role validation |
| Finding cross-validation totality | Research + Queries over semantic success plus Finding acquisition failure, semantic/Finding disagreement, duplicate generic diagnostics, partial IL payload, and Source requested | The final mechanism ledger remains `Compared`; the same work item/mechanism has both `Compared` and `Failed` outcomes; `HasFailures` is false; partial payload supplies a semantic verdict or Source eligibility; Source performs I/O; the selected CLI exits zero; or the failed row is missing/duplicated |
| Direct-consumer outcome totality | Research + ResearchQueries + `match --implementation` + authored rebuild + round-trip/scope tests with admission/question/pairing/population/plan budget failures, rejected non-C#/IL mechanism sets, failed C#, IL, source-bound address, role, and subject projection; two-body native-unavailable results; resolved bodyless/bodyful transitions; all requested C#/IL mechanisms absent; and mixed exact/absent C#/IL ledgers | A consumer reduces a failed evidence arm as an empty ledger population; failed evidence does not force `HasFailures = true` and `Outcome = Unavailable`; a completed arm has zero work items or an empty/incomplete requested ledger; `Compared` lacks exactly one exact-or-different verdict; two-body native unavailable is not `Failed(ComparisonUnavailable)`; missing-body native unavailable becomes failure; subject projection failure invokes a producer, lacks complete failed ledgers/diagnostics, leaves `HasFailures` false, or yields `Exact`/`Different`; completed reduction does not follow `Unavailable` then `Different` then `Exact` then `NotApplicable` precedence; an API, Body Signals, Source, ReturnToSender, or unknown ledger enters direct reduction; a direct result lacks typed diagnostics or retained completed-arm absence reasons; `match` exits zero for failure or nonzero for not-applicable; authored rebuild reports `IlDifferent` for failed/not-applicable evidence; round-trip reports `Changed`/`Exact` for either; or a consumer drops the diagnostic/absence reason instead of mapping it to nonzero/context-failed/unavailable or typed not-applicable |
| Ledger output visibility | CLI text/Markdown/table/TSV/JSON/JSONL over producer-lined evidence, multiple producer changes, producer-different Body Signals/no-line evidence, session body-presence evidence with no adapter and with every optional line filtered, producer exact/no-line controls, presentation-safe failed partial IL evidence, pre-producer single/multi-outcome endpoint/participant/selection/target/correspondence failure, native comparison unavailable, Source missing mapping, Source not eligible, query/cleanup failure, changed/native-line filtering, row windows including `--jsonl --rows 1`, and `Member`-/empty-field-only projections over ledger and query failures | A completed or failed query cannot use the one schema; a producer `Compared(Different)` has an empty/incomplete/duplicate change set or any retained producer change with no post-filter line lacks exactly one native typed fallback; a session `Compared(Different)` with no post-filter line lacks exactly one body-presence fallback; `Compared(Exact)` emits a fallback; optional lines losing a filter erase a difference; a failed disposition lacks one mandatory diagnostic, a failed endpoint slot omits any terminal request's diagnostic/outcome/absent-arm context, partial evidence supplies a verdict/replaces that diagnostic, or presentation-safe partial evidence has no defined ordinary row; Source `MissingMapping` is hidden or `NotEligible` is forced visible; structured output loses `recordKind`, disposition, typed producer/body-presence change, partial-evidence classification, or typed reason; a query diagnostic fabricates a ledger disposition; evidence does not count toward the semantic row window; diagnostic records count against or are suppressed by that window; the integrity descriptor and section schema differ; a user projection removes any required integrity column when diagnostics exist or changes successful diagnostic-free projection; JSONL emits a second table/ad hoc object; or ordering is nondeterministic |
| Result and exit totality | Research + ResearchQueries + CLI text/Markdown/table/TSV/JSON/JSONL with public-construction attempts, duplicate member subjects across participants, complete/omitted explicit/enumerative selection correlations over admitted/endpoint-absent/terminal domains, missing/extra query-to-Research correspondence, empty-mechanism rejection, producer/session difference with and without visible lines, bodyless `Absent`, mixed exact/absent, all-absent direct results, direct failed evidence, hidden/windowed/projected target, participant, correspondence, mechanism, Finding acquisition/cross-validation, plan/Source budget, query, cleanup, and direct-result failures plus other `Absent` controls | A planned result can be publicly constructed in Research or by a host, copied, or enriched; its query, population-projection, or Research receipt loses a question, selection request/endpoint scope, domain snapshot, correlation, expected scope, side outcome, work item, or absence proof, or retains a live query/Research domain, pairing, role, or source object; publication accepts missing, extra, substituted, or mismatched correspondence; an endpoint-absent domain loses proof, a failed endpoint domain loses any request outcome/absent-arm proof/terminal diagnostic/tainted realized summary, or an ambiguous/failed participant domain is omitted, collapsed, or permits explicit no-match; equal subjects in distinct Research work items collapse; a producer/session difference lacks its required `ResearchBodyEvidenceChange` or post-filter evidence row; a presentation row loses its work-item/participant context; a planned empty mechanism set completes; a failure disappears from retained ledgers/query outcome/direct failed arm; completed-result `HasFailures` absorbs outer failure; a direct failed arm exposes completed ledgers or reduces vacuously; selected Implementation Diff exits zero for a failure; `Absent` exits nonzero; all-absent completed evidence has no `NotApplicable` arm; mixed exact/absent loses its absence reason; or ledger/query failure produces empty or contentless projected output |

## Current mismatches

- `ResearchDiffOptions` defaults to `ResearchChangeMechanism.AllAvailable`,
  which includes host-owned Source and ReturnToSender while synchronous
  `ResearchDiff.Compare` implements only Research-owned mechanisms.
- `ImplementationDiffMechanism.AllAvailable` likewise includes host-owned
  Source, and the public assembly `CompareAssemblies`/`Compare` overloads plus
  harness callers bypass the target query/session ownership.
- `ResearchComparison` is publicly constructible and combines body projections
  that did not share one complete population.
- `ImplementationDiffResult` and `ImplementationDiffMember` are public records;
  callers and tests construct, copy, and enrich success-shaped results without
  a completed receipt or complete ledgers.
- Those query-level result types currently live in `ILInspector.Research`, and
  no typed population-projection boundary separates core-Queries acquisition
  identities from Research-owned evidence identities. The target friend edge,
  disjoint Research admission model, correspondence receipt, and
  ResearchQueries-owned result composition do not exist.
- Planned `ResearchChange` grouping and `ImplementationDiffResult.Members` use
  `ResearchSubjectKey` without work-item identity, so equal subjects in
  different participant pairings can collapse.
- Side-local target requests have no pre-acquisition question/endpoint-slot
  set, sealed admitted/endpoint-absent/terminal participant-domain expansion
  (including participant-pairing ambiguity/failure), correlation
  manifest, or selection scope distinguishing omitted questions, endpoint
  failure, proven absence, and failed/incomplete selection or body-key census,
  so a missing scope or failed counterpart can become silence or a semantic
  one-sided change.
- `ImplementationDiff.CompareMembers` receives two live readers and handles but
  has no typed pairing designation, exact designation/side/source-bound
  address/role contract, or designated-pair work-item key, and executes below
  the Queries-owned budget operation. `match --implementation` also fabricates
  an assembly-wide result to render its arbitrary direct pair.
- Direct results expose no ledger-derived failure summary; `match`, authored
  rebuild, and round-trip callers can convert failed evidence into zero exit or
  semantic `Changed`/`IlDifferent`.
- `CompareMembersWithPdbSource` and
  `WithPdbSourceComparisons` attach Source after comparison rather than as
  one declared mechanism.
- Assembly comparison filters and joins C#, IL, body-signal, retained Finding,
  and Source evidence through presentation-shaped subject identities and
  independently constructed populations.
- C# and IL producers have no query-owned pre-invocation work/scratch
  reservation. `IlBodyDiff` can allocate its quadratic LCS matrix for one item
  before any aggregate operation charge.
- The CLI derives PDB-source targets from already-rendered changed members
  and owns the async acquisition loop outside one Queries-owned operation.
- `PdbSourceAcquisition` constructs `TextFindings` during acquisition, before a
  complete Source producer population can be derived or its comparison
  work/scratch can be preflighted.
- Source acquisition has per-item protections but no aggregate eligible-item,
  PDB, document, outbound-operation, transferred-byte, retained-byte, or
  concurrency
  budget.
- Implementation failures do not currently contribute to CLI exit status;
  only API-surface inspection failures do.

## Non-goals

- It does not prove semantic equivalence; IL/body rows are evidence, not a
  verifier.
- It does not own API compatibility rows. Metadata owns API observations,
  matching, and compatibility classification; Research retains and projects
  that comparison separately from `ImplementationDiff`.
- It does not compile source artifacts or plan closure. ReturnToSender and other
  harnesses own artifact requests and compilation.
- It does not define endpoint acquisition protocols or storage. Artifact source
  adapters and the workspace own those concerns.
- It does not infer participant or member correspondence from provenance,
  equality, or presentation.
- It does not widen any decompiler raise policy. Decompiler consumers use the
  completed physical evidence contract independently of reconstruction policy.
