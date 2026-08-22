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

`ImplementationDiff` is the product-side decompiled C# + IL/body + PDB Source
diff projection in
`ILInspector.Research`. It is the reusable implementation-diff component for
the CLI, ReturnToSender, harnesses, and other consumers that need one
member-centric change model instead of separate C# and IL renderers.

Terminology follows [Finding Nomenclature](finding-nomenclature.md):
`Finding<T>` is a one-version observation, `PairFinding<T>` is a two-version
transition, and evidence is the role either may play rather than a competing row
family.

## Ownership

- `ILInspector.Decompiler` owns C# body diff production and display rows through
  `CSharpBodyDiff` and `CSharpDiffPrinter`.
- `ILInspector.ILDiff` owns IL/body diff production and display rows
  through `IlBodyDiff`, `IlAssemblyDiff`, and `IlDiffPrinter`.
- `ILInspector.Research` owns the join. `ImplementationDiff` compares assemblies
  with decompiled C# and IL/body mechanisms, accepts checksum-gated PDB-source
  line inspections from Services, groups changes by `ResearchSubjectKey`, and
  exposes typed display rows and unified lines without reformatting producer
  wording.
- `ResearchComparison.RetainedComparisons` keeps the native
  `FindingComparison<CSharpCanonicalLine>` and
  `FindingComparison<CanonicalIlOperation>` envelopes when requested. PDB Source
  comparisons retain `FindingComparison<string>` with the `text.line`
  descriptor. Research
  cross-checks their exactness against the richer semantic projections for
  members present on both sides. A disagreement is retained as a per-member
  `Failed` diagnostic; it does not abort healthy members in the same diff.
- `DotnetInspector.ResearchQueries` owns the authorized asynchronous operation.
  It opens or borrows inspection sessions, projects every requested mechanism,
  enforces authored-source budgets, completes the comparison, and releases
  leases before returning a typed final result.
- The assembly/package `diff` CLI owns selection and presentation only. It
  supplies endpoint/target requests and capabilities, then renders the
  completed result. It never opens readers around Research, projects a
  mechanism, or enriches a finalized comparison. The direct
  `match --implementation` path already owns one live source while resolving
  its two exact selectors; it may create an invocation-scoped direct
  designation and render the product-issued direct result, but it does not
  fabricate an assembly-wide result.

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

The core model is:

```text
BodyEvidenceParticipantBinding
  Pairing                ArtifactParticipantPairing.Id
  Side                   Before | After
  AssemblyIdentity       name + culture + public-key token; version omitted
  Registration           side-local AssemblyAcquisitionRegistration
  Mvid                   side-local module identity

BodyEvidenceSelectionScope
  Id                     opaque comparison-scoped selection identity
  CorrelationId          opaque user/host question identity
  Participant            ArtifactParticipantPairing.Id
  Before/After           Selected(request ids) | Absent(proof) | Failed

BodyEvidenceTargetRequest
  Id                     opaque side-local target-request identity
  ScopeId                owning BodyEvidenceSelectionScope
  Participant            pairing id + side-local binding
  Target                 Exact(address, role) | Carried

BodyEvidenceTargetAttempt
  Id                     opaque plan identity
  RequestId              originating request
  Participant            pairing id + side-local binding
  Outcome                Resolved | Bodyless | Unavailable | Rejected |
                         Ambiguous | Failed

BodyEvidenceCoordinate
  Participant            ArtifactParticipantPairing.Id
  Key                    MemberBodyCorrespondenceKey
  Role                   Method | Getter | Setter | Adder | Remover

BodyEvidenceWorkItem
  Key                    Corresponded | DesignatedPair |
                         CounterpartUnavailable | SelectionFailed |
                         ResolutionFailed | ParticipantFailed
  AttemptIds             selected-target aliases; empty only for
                         selection/participant failure
  Corresponded           coordinate plus optional before/after resolved entry
  DesignatedPair         exact before/after direct entries
  CounterpartUnavailable resolved entry plus failed opposite scope/census
  SelectionFailed        scope side plus typed selection failure
  ResolutionFailed       attempt ids plus typed per-side failure
  ParticipantFailed      endpoint/acquisition/pairing failure

BodyEvidenceComparisonPlan (internal)
  SelectionScopes        sealed Selected/Absent/Failed side outcomes
  WorkItems              private resolved/failed union
  Ids                    opaque plan-scoped BodyEvidenceWorkItemId
  AttemptMap             every target-attempt id -> exactly one work-item id
  Before/After           private bindings, entries, and failures

BodyEvidenceComparisonSession (internal)
  Plan                    BodyEvidenceComparisonPlan
  RequestedMechanisms     closed set declared before projection
  Dependencies            acyclic same-work-item prerequisite graph
  Ledgers                 validated synchronous/asynchronous projections
  Project/ProjectAsync    total per-work-item mechanism callbacks
  Complete                one atomic finalization after every ledger exists

BodyEvidencePlanReceipt
  Revision                projection-free completed-plan identity
  WorkItemSet             internal validated set identity

BodyEvidenceMechanismLedger<T>
  Mechanism               requested mechanism descriptor
  Dispositions            exactly one Compared/Absent/Failed per work-item id

BodyEvidencePresentationMap
  Entries                 exactly one inert participant/member label group per
                         work-item id

DirectMemberPairingDesignation
  Pairing                 one direct-slot ArtifactParticipantPairing
  Before/After            exact live participant binding + MVID
  Authority               invocation-scoped explicit participant pairing
  Lifetime                cannot outlive either supplied source

DirectMemberComparisonInput
  Pairing                 DirectMemberPairingDesignation
  Before                  exact MetadataMethodAddress + relationship role
  After                   exact MetadataMethodAddress + relationship role
  Lowering                direct factory mints one internal
                         BodyEvidenceSelectionScope

ImplementationMemberDiffResult
  RequestKey              requested DesignatedMemberPairKey
  WorkItems               completed direct-session work-item results
  BeforeSubject           Resolved(ResearchSubjectKey) | Failed
  AfterSubject            Resolved(ResearchSubjectKey) | Failed
  Ledgers                 complete requested synchronous ledgers
  Native                  producer-owned direct comparison payloads
  Outcome                 Exact | Different | Unavailable
  HasFailures             derived from retained ledgers
  Diagnostics             typed retained failure details
```

The plan, session, entries, callbacks, and participant proofs remain internal.
The result retains only the projection-free receipt, complete ledgers, native
producer payloads, and the total presentation map.

### Target attempts and work-item totality

The host resolves user type/member selectors into sealed
`BodyEvidenceSelectionScope` values before plan construction. A scope binds one
user/host question to one participant pairing and records an independent
outcome for each side:

- `Selected` contains the complete non-empty set of side-local exact/carried
  target-request ids admitted by that side's inventory;
- `Absent` carries typed proof that the complete side-local inventory contains
  no selected target;
- `Failed` carries the selection, inventory, or participant diagnostic that
  prevented the side from proving either selected targets or absence.

`CorrelationId` groups scopes produced by one user's question but never acts as
MethodDef identity. One logical selector spanning several participant pairs
produces distinct scopes. Explicit unscoped `All` enumeration seals one
participant-local scope only after its full side-local MethodDef inventories
have been enumerated. A failed or prematurely ended enumeration is `Failed`,
never `Absent` or a shortened `Selected` set.

Every request belongs to exactly one `Selected` scope side and binds one target
to that participant side. The coordinator mints exactly one target-attempt id
for each request before exact/carried body resolution. It never fans one
request or strict target across sides or participants. A selection-scope
failure creates a `SelectionFailed` work item without inventing a target
request. This granularity is deliberate:

- corresponding before/after attempts in a failure-free scope whose
  version-neutral keys agree map to one two-sided work item;
- signature drift in a failure-free scope produces separate one-sided
  remove/add work items with separate side-local requests and attempt ids;
- a `Selected` side opposite an explicit `Absent` side produces proven
  one-sided work items;
- a `Selected` side opposite `Failed` produces no semantic one-sided work
  item;
- overlapping selectors resolving the same participant, exact address, strict
  key, correspondence key, and role may alias one work item while retaining
  every attempt id.

Each carried request resolves only inside its own selected artifact/version.
Before and after requests therefore carry independently minted strict keys.
Each exact request carries its own relationship role and validates that role
against its side-local metadata before entering the resolved index.
Only after both resolve does `MemberBodyCorrespondenceKey` decide whether they
share a work item. AssemblyRef-version-only drift reaches correspondence rather
than failing because one side was asked to resolve the other's strict key.

`BodyEvidenceWorkItemKey` is a closed union:

```text
CorrespondedKey
  ParticipantPairingId + MemberBodyCorrespondenceKey + RelationshipRole

DesignatedMemberPairKey
  DirectMemberPairingDesignation.Pairing.Id
  Before MetadataMethodAddress + RelationshipRole
  After MetadataMethodAddress + RelationshipRole

CounterpartUnavailableKey
  BodyEvidenceSelectionScope.Id + affected candidate coordinate

SelectionFailedKey
  BodyEvidenceSelectionScope.Id + side

ResolutionFailedKey
  BodyEvidenceTargetAttempt.Id

ParticipantFailedKey
  ComparisonEndpointPairingSlot.Id or participant-pairing-failure id
```

The key does not infer identity; it records the result of prior participant and
body correspondence. Before/after resolved attempts with the same
`CorrespondedKey` map to one work item. Signature drift has different
correspondence keys and therefore different work items. Overlapping selectors
that resolve to the same coordinate add attempt aliases to that item.
Resolution failures remain per-attempt and cannot collapse because two failures
have similar diagnostics.

Semantic correspondence requires a complete failure-free key census for both
selected sides of the scope. If selection fails on one side, or any selected
target on that side fails before producing a validated correspondence key, the
opposite census is incomplete. Every resolved candidate coordinate in that
scope whose correspondence, uniqueness, or absence claim depends on the
incomplete census becomes `CounterpartUnavailableKey`, retains the failed
scope/attempt diagnostics, and receives terminal
`Failed(CounterpartUnavailable)` dispositions. It cannot become a semantic
pair, `Compared` add/remove, or Source-eligible item. The selection or
resolution failure also retains its own first-class failure work item.
Selection owners may declare narrower independent scopes before execution to
bound this failure domain; the plan cannot split a failed scope afterward
based on guessed identity.

Only `DirectMemberComparisonInput` can mint a
`DesignatedMemberPairKey`. It records one caller-authorized exact pair without
asserting equal assembly identity, correspondence key, signature, or
relationship role. It therefore supports original-to-emitted comparisons and
`match --implementation` comparisons between arbitrary methods while retaining
both exact side-local identities. Endpoint, selector, and assembly-wide paths
cannot mint this key or use the direct designation as correspondence evidence.
Direct construction mints a real opaque `BodyEvidenceSelectionScope.Id`
internally and seals that scope with one exact request and attempt per side.
The caller neither supplies nor observes the scope. If both attempts resolve,
the direct factory maps them to the designated-pair key. If either fails, that
attempt maps to `ResolutionFailedKey` and the resolved opposite attempt maps to
`CounterpartUnavailableKey` using the internally minted scope id. `AttemptMap`
totality and address/role validation therefore remain identical to the planned
path without inventing caller authority or a parallel failure key.

The plan materializes an immutable `AttemptMap`. Every target-attempt id maps
to exactly one work-item id, and every resolved or resolution-failed work item
retains at least one target-attempt id. Selection- and participant-failed work
items instead retain their scope or endpoint/pairing failure identity. Every
request appears in exactly one selected scope side and names exactly one
attempt; the selected request-id set recorded on that side must equal the
requests that name it. Correlation ids may name several scopes but authorize no
cross-side matching. Set-equality validation among scope-side request ids,
requests, target-attempt ids, work-item keys, attempt aliases, and `AttemptMap`
rejects orphaned requests/attempts, multiply mapped attempts, unaliased work
items, and duplicate keys.

Index construction validates each selected exact address against its
participant and resolves each selected carried target through
`MemberBodyTargetResolver`. It records resolution failures before forming
correspondence and marks that scope side's key census incomplete. Only
failure-free scopes key resolved entries by participant pairing,
`MemberBodyCorrespondenceKey`, and role. Duplicate correspondence keys naming
different strict targets, addresses, or roles are ambiguous only within that
participant pair. Equal keys in different participant pairs remain distinct.

A `MemberBodyResolution.Bodyless` arm is a resolved entry, not a resolution
failure. It retains its address, keys, and role in the coordinate population.
For each local body-producing mechanism, both bodyful sides produce a normal
comparison; exactly one bodyful side produces `Compared` body-added or
body-removed evidence only when the opposite side is proven absent by a
complete failure-free scope; and no bodyful side produces `Absent(NoBody)`.
Thus a bodyless/bodyful transition cannot disappear as `Absent`, while a
failed or incomplete counterpart cannot become a semantic addition/removal.

The resolved index exposes no enumerable population. The internal plan forms
the union of corresponded/proven-one-sided coordinates, designated pairs,
counterpart-unavailable coordinates, selection/target-resolution failures,
failed endpoint-pairing slots, and participant-pairing failures, then assigns
one opaque work-item id to each. A failure that cannot mint a correspondence
key remains a first-class work item and taints every absence claim that depends
on its census; it cannot become empty or success-shaped output.

### Mechanisms and dependency-ordered projection

`ImplementationEvidenceMechanismCatalog` is the closed source of mechanism ids,
owners, sync/async execution kind, local-implementation eligibility, and
dependencies. The session selects its complete mechanism set from that catalog
and materializes the dependency graph before projection. The requested set must
be non-empty and dependency-closed. Unknown ids, an empty set, and a known
mechanism whose required catalog dependency is absent all reject before plan
projection; hosts cannot synthesize descriptors or an "all available" set.

`Project` and `ProjectAsync` privately walk all work items and require one
`Compared`, `Absent`, or `Failed` disposition per work-item id. A callback sees
only the current work item and already validated prerequisite dispositions for
that same id. It cannot enumerate another mechanism or the plan.

For selection-, target-, counterpart-, endpoint-, or participant-failed work
items, the session stamps the shared terminal failure into every requested
ledger without invoking a producer. For healthy items, each producer retains
its native payload and outcome semantics. Internal construction rejects
missing, extra, or duplicate dispositions.

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

### Query-owned asynchronous lifetime

`ImplementationComparisonQuery` is one
`InspectionQueryRegistry.AddAsync` operation. Begin, synchronous projections,
dependency-gated Source projection, completion, and strict cleanup execute
lexically under one current query access lease. The query owns or borrows every
`AssemblyInspectionSession`, PDB/source content lease, retained artifact
binding, and transport capability.

The CLI receives no plan or session. It supplies typed endpoint and target
requests, mechanism selection, capabilities, and budget, then receives only a
completed `ImplementationDiffResult`.

Owned resources and lease claims are released after success or failure.
Caller-owned borrowed sessions are never disposed. A cleanup failure alone is
a typed query failure and no completed result escapes. When another operation
failure already exists, that remains primary and the cleanup diagnostic is
retained beside it. Cancellation propagates as cancellation, drains owned
work, and returns no partial comparison or failure-shaped substitute.

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

Source dispositions are staged until the entire mechanism succeeds. If any
aggregate dimension is exhausted, the query cancels and drains owned Source
work, discards all staged eligible Source results, and records
`Failed(SourceBudgetExceeded)` for every eligible Source work item. The failure
retains dimension, limit, observed/requested charge, and any earlier per-item
diagnostic. Ineligible items remain `Absent(NotEligible)` and pre-existing
target/participant failures remain their original failures. Thus executor
ordering may change the reported exhausted dimension, observed/requested
charge, earlier diagnostic, and how early work stops, but never which eligible
items appear successful or failed.

### Completion and result boundary

`Complete` is the only final transition. It is one-shot and fails unless every
declared mechanism has one complete, disjoint ledger. Projection or completion
afterward fails visibly. Completion builds a
`BodyEvidencePresentationMap` keyed by work-item id. Each entry has inert
participant and member labels sufficient to distinguish two equal member
subjects in different TFM/RID/assembly participants. Labels contain no address,
target, participant proof, or correspondence key from which evidence could be
reselected.

`ResearchComparison` becomes a closed union:

```text
ResearchComparison
  UnscopedResearchComparison
  BodyEvidenceResearchComparison

BodyEvidenceResearchComparison
  Receipt                 BodyEvidencePlanReceipt
  Ledgers                 complete requested mechanism ledgers
  Presentation            total BodyEvidencePresentationMap
  Native                  producer-owned comparison payloads
  Changes                 work-item id + producer-owned ResearchChange
```

Standalone API comparison and explicit C#/IL presentation/test adapters return
the unscoped arm. It cannot feed Implementation Diff. Only Research-owned
session completion can create the body-evidence arm.
`ImplementationDiff.FromResearchComparison` accepts that arm only.
`CombineUnscoped` rejects a planned input; planned results never combine because
all body mechanisms belong to one session.

Planned public result types are sealed non-records with internal constructors
and get-only immutable collections. `ImplementationDiffResult` is issued only
from a completed body-evidence comparison; its `WorkItems` projection is keyed
by `BodyEvidenceWorkItemId` and joins that id's presentation entry, mechanism
dispositions, and native payloads. `ImplementationMemberDiffResult` is issued
only by the direct session. It retains separate before/after subjects rather
than inventing one shared member identity for an arbitrary designated pair.
Neither result exposes `init`, `with`-copy, or public member/result constructors
that can attach Source or synthesize a success without a receipt and complete
ledgers.

`ImplementationDiffResult.HasFailures` derives from retained ledgers and query
failure, not rendered or windowed rows. When Implementation Diff is selected,
every CLI text and structured output path returns nonzero for any target,
endpoint, participant, mechanism, budget, or cleanup failure. `Absent`,
including Source `NotEligible`, remains non-failing. API inspection failures
retain their existing independent exit behavior.

`ImplementationMemberDiffResult.HasFailures` likewise derives from its complete
direct ledgers and retains their typed diagnostics. It is not inferred from
row count or a nullable native payload. Its total outcome is `Exact` only when
every requested mechanism completed exactly, `Different` when complete
evidence proves a difference, and `Unavailable` when any ledger failed. A
direct failure therefore has no semantic exact/changed verdict.

## Research comparison model

`ResearchDiff` is the operation facade. `UnscopedResearchComparison` retains
the existing flat `ResearchChange` collection and subject grouping because it
makes no multi-participant population claim.
`BodyEvidenceResearchComparison` instead retains
`BodyEvidenceResearchChange(workItemId, change)` values. `ByWorkItem()` is its
authoritative grouping; any member-centric convenience projection groups by
`(workItemId, ResearchSubjectKey)`, never by `ResearchSubjectKey` alone. The
planned arm additionally retains its receipt, complete ledgers, presentation
map, and native payloads.

Each `ResearchChange` carries one mechanism, a `FindingDescriptor`, an
added/removed/changed/failed classification, its subject, and any native producer
payload needed for typed presentation. It is deliberately not a
`PairFinding<T>`. Metadata now exposes genuine API type/member comparisons and
`ResearchComparison.ApiComparison` retains that producer-owned envelope. C#,
IL/body, body-signal, and ReturnToSender mechanisms do not all expose equivalent
old/new Finding censuses yet, so the cross-mechanism `ResearchChange` projection
must not manufacture Finding atoms or misuse `PairKind`. `ResearchChange` is a Research-owned migration projection, not the seed of a
parallel generic `EvidenceRow` spine. C# and IL now have native comparisons;
their semantic rows remain because they carry richer producer-owned evidence,
while retained comparisons expose the exact census transitions. The `Source`
mechanism never replaces or changes the meaning of `CSharp`: one describes
checksum-verified PDB-mapped text and the other describes product-decompiled
text. `ResearchSubjectKey` remains producer-local member currency;
`BodyEvidenceWorkItemId` is the separate participant-aware planned-result
currency and is never reconstructed from the subject.

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
comparison, and divergence becomes a visible per-member `Failed` diagnostic.
If the Finding producers later carry equivalent aligned hunk and typed display
payloads, the semantic projections should be deleted rather than matched a
third time.

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

`ResearchChange` binds one native producer payload — `ApiChange` or
`CSharpRow` (anchor-carrying) or `IlRow` and the analysis signal fields
(body-substrate) — to one `ResearchSubjectKey` whose `Id` is the anchor
`StableSelector`, and to a cross-mechanism product `ChangeId` via
`FindingDescriptor`. It never erases the lower-layer typed payload and never
requires consumers to parse `Message`. Machine consumers query by `ChangeId`
through `HasChange`, `HasChangePrefix`, and `HasChangeCategory`; product
`ChangeId`s use fact concepts (`unsafe.stackalloc.added`, `il.hunk.changed`,
`csharp.return-expression.changed`), not incidental detail fields. `Message`
stays producer-owned presentation on either side of the join.
For a planned comparison, `BodyEvidenceResearchChange` wraps this unchanged
producer payload with its owning work-item id. Rendering and grouping begin
from that wrapper and the presentation map, so equal `StableSelector` values in
two participants cannot collapse.

## Consumer contract

Use `ImplementationComparisonQuery` for assembly-, package-, project-,
platform-, directory-, or workspace-scoped implementation comparison. The host
supplies a sealed endpoint pairing/outcome set, participant pairings, sealed
per-participant selection scopes, their side-local exact or carried target
requests, the mechanism set, capabilities, and budget. The query returns one completed
`ImplementationDiffResult`; no enrichment step exists.

Use `ImplementationDiff.DesignateMemberPair` when the caller already owns two
live `MetadataSource` values. The factory explicitly authorizes those two
participants for this invocation and captures exact live bindings plus MVIDs in a
`DirectMemberPairingDesignation`. The designation privately wraps one
direct-slot `ArtifactParticipantPairing` using the same pairing id/bindings as
the planned lifecycle; it is not a second participant currency. Assembly
identity may differ because the designation is explicit comparison authority,
not inferred correspondence. It accepts no paths, handles, tokens, or display
identity. The designation cannot outlive either source.

`ImplementationDiff.CompareMembers` accepts only
`DirectMemberComparisonInput`: that designation, exact old/new
`MetadataMethodAddress` values, and a relationship role for each side. It
validates both
addresses against the designated participant bindings, completes one
synchronous work-item session by lowering the wrapped pairing into one
`DesignatedMemberPairKey` work item, and returns
`ImplementationMemberDiffResult` before either source can expire. It accepts
only Research-owned synchronous mechanisms and cannot feed an assembly-wide
Implementation Diff. The current handle-only overload is removed.

`match --implementation` retains its command-owned selector source only long
enough to designate and compare the two exact methods. It passes the direct
result to a formatter overload for `ImplementationMemberDiffResult`; it never
constructs `ImplementationDiffMember`, `ImplementationDiffResult`, or a
placeholder `ResearchComparison`, and the direct result retains both selected
subjects when their signatures differ. It renders retained direct diagnostics
and exits nonzero when `HasFailures` is true.

ReturnToSender and round-trip tools use the same designation/input path for
original-to-emitted or emitted-to-emitted methods, including differently named
assemblies. Every caller checks `HasFailures` before `IsExact` or semantic
changed outcomes. Authored rebuild maps `Unavailable` to `ContextFailed`;
round-trip and
scope comparison map it to their typed unavailable state. They retain the
direct diagnostics and never classify a failed ledger as `IlDifferent`,
`Changed`, or `Exact`.

There is no direct authored-source overload. A caller that needs PDB,
SourceLink, or network work uses `ImplementationComparisonQuery`; a harness
with independently acquired source evidence owns its own typed result rather
than rewriting the product comparison.

Finding acquisition and cross-validation failures use
`ResearchChangeKind.Failed`; they are operational diagnostics, never semantic
`Changed` rows in table, TSV, JSONL, or programmatic consumers.
When a semantic projection carries the corresponding typed failure, Research
keeps that richer row and suppresses the duplicate generic Finding failure.
Synthetic add/remove rows from the same failed C# hunk are omitted; genuine
body absence and independently decoded partial IL evidence remain visible.

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
verdict exists; typed failure rows retain the reason. Non-IL mechanisms do not
carry this payload.

Use `ImplementationDiff.UnifiedLines(change)` only at presentation boundaries.
The durable model keeps the producer-owned typed display rows rather than a
third implementation-specific row family.

The `diff` command exposes this component through the explicit-only
`Implementation Diff` section. The CLI projects one row per producer-owned
unified line with `Work Item`, `Participant`, `Member`, `Mechanism`,
`Difference`, `Change`, and `Evidence` fields. Every format begins from
`ImplementationDiffResult.WorkItems`; repeated display values never merge
distinct work-item ids, and structured forms retain the result-local opaque id
plus participant label. `Difference` contains the IL body outcome for IL rows
and is empty for C# rows, keeping participant, mechanism, result, edit kind,
and evidence as separate dimensions.
The section binds `ImplementationComparisonQuery`. During migration, its
current input still carries retained assembly descriptors, reference resolvers,
and body indexes; the query opens those descriptors for the offline C# and IL
producers. The target input replaces that reader-opening shape with the sealed
endpoint outcomes, pairings, and target requests above. The CLI adapter's
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
| `AssemblySetResolver` endpoint-to-flat-list comparison input | Sealed `ComparisonEndpointOutcomeSet` with exact request/outcome-key equality and 1:N participant manifests; pairing validates the before/after manifest union |
| `AssemblyResolutionProvenance` pattern matching in Research | Opaque adapter-/host-issued `ArtifactParticipantPairing.Id` plus side-local binding |
| Package/project/directory occurrence index | Logical participant slots; duplicate slots fail ambiguous |
| `ResearchDiffOptions.TypeFilters` / `MemberTargetIdentities` | Sealed per-participant `BodyEvidenceSelectionScope` values with independent `Selected` / proven `Absent` / `Failed` side outcomes and side-local target requests |
| `CSharpBodyDiff` raw-key/occurrence index | Participant pairing plus `MemberBodyCorrespondenceKey` and role |
| Independent C#, IL, body-signal, Finding, and Source populations | One internal work-item plan projected to complete mechanism ledgers |
| Public `ResearchComparison` constructors and standalone C#/IL adapters | Public factories can issue only `UnscopedResearchComparison`; only internal session completion issues the body-evidence arm |
| `ResearchChangeMechanism.AllAvailable` default | Explicit API + Body Signals + IL + C# synchronous default |
| `ResearchComparison.Combine` | `CombineUnscoped`; planned inputs reject |
| `ImplementationDiff.FromResearchComparison(ResearchComparison)` | Accept only `BodyEvidenceResearchComparison` |
| `ImplementationDiff.CompareAssemblies` | Remove; assembly-scoped callers construct typed endpoints and execute `ImplementationComparisonQuery` |
| `ImplementationDiff.Compare(ResearchDiffInput, ResearchDiffInput, ...)` | Remove; the query/session owner forms one planned Research comparison |
| `ImplementationDiff.Compare(IReadOnlyList<ImplementationAssemblyInput>, ...)` | Remove with `ImplementationAssemblyInput`; typed endpoint outcomes and participant bindings replace reader-opening inputs |
| `ImplementationDiffOptions` | Split mechanism selection into catalog ids on the query/session input, target selection into sealed side-outcome scopes and side-local requests before planning, and changed/window options into presentation-only values |
| `ImplementationDiffMechanism` / `AllAvailable` | Retire in favor of the closed mechanism catalog; no context-free host-owned mechanism set |
| Public `ImplementationDiffResult` / `ImplementationDiffMember` constructors, `SourceComparison` init, and record copying | Sealed non-record results with internal constructors, get-only immutable state, and no post-completion enrichment |
| `ImplementationDiffResult.Members` and planned `ResearchComparison.BySubject()` | Work-item-keyed result projection and `BodyEvidenceResearchChange`; any subject view retains its work-item id |
| `ImplementationDiff.CompareMembers` handle-only pairing and shared `ImplementationMemberDiffResult.Subject` | `DesignateMemberPair` plus `DirectMemberComparisonInput`; exact addresses and per-side roles lower to one `DesignatedMemberPairKey`, including explicitly designated cross-identity assemblies, and the result retains both side-local subjects |
| `MatchCommand.BuildImplementationDiffView` direct result construction and unconditional zero exit | Invocation-scoped direct designation plus a formatter overload for product-issued `ImplementationMemberDiffResult`; retained direct failure returns nonzero |
| ReturnToSender and round-trip `CompareMembers` calls | Product-issued direct designations over original/emitted or emitted/emitted sources; differing assembly identities remain valid and failed ledgers map to context-failed/unavailable rather than semantic difference |
| `CompareMembersWithPdbSource` | Remove; async acquisition belongs to `ImplementationComparisonQuery` |
| `WithPdbSourceComparisons` | Remove; finalized results accept no new ledger |
| CLI changed-row PDB-source enrichment | Dependency-gated Source projection inside one query lifetime |
| CLI API-only failure exit | Include assembly and direct result `HasFailures` in every selected output path |

## Gates

The target lifecycle is unverified until these gates exist:

| Gate | Surface | Fails if |
| --- | --- | --- |
| Endpoint-manifest totality | Artifact adapters + Queries over package `Preferred`, explicit/all TFM/RID, project outputs/dependencies, platform, directory, two-bundle embedded workspace, cross-source, explicit endpoint absence, and failed endpoints | Request and outcome `(Side, Id)` sets differ; the pairing plan omits or repeats a request; a duplicate/rekeyed/cross-side outcome occupies another request; failed/omitted acquisition is treated as `Absent`; a failed endpoint makes an opposite manifest one-sided; one endpoint is forced to one participant; a realized endpoint has an empty/unsealed manifest; a real selected inventory differs from its manifest; an embedded pair lacks a host-issued paired designation or uses workspace context/`ContentRef` as cross-side identity; or pairing differs from a failure-free manifest union |
| Participant correspondence | Adapter + Queries repeated/equal-identity and reordered-input fixtures | Research interprets provenance; path/version/TFM/RID/`ContentRef`/digest/MVID/registration or occurrence becomes pairing identity; duplicate logical slots select one; or adding a participant renumbers another |
| Body-target attempt totality | Research + Queries over AssemblyRef-version-only drift, accessor roles, signature drift, multi-participant selectors, overlapping selectors, explicit absence, selection/resolution failure, incomplete `All`, bodyless methods, and participant failure | A scope side is not exactly `Selected`, proven `Absent`, or `Failed`; selected request ids differ from their requests; a failed/incomplete census becomes absence or a shortened selected set; a target request is not already scope/side/participant scoped; an exact target omits or bypasses relationship-role validation; one strict target is fanned across versions/participants; a selection failure invokes body resolution; one request lacks exactly one attempt; one attempt maps to zero/multiple work items; a work item lacks attempts or failure identity; correlation ids authorize matching; `AttemptMap`, aliases, and discriminated keys differ; remove/add shares one request/key/attempt; bodyless becomes a resolution failure; or aliases weaken exact/strict/correspondence validation |
| Counterpart and body-presence disposition | Research C#/IL/body-signal tests over bodyful/bodyful, bodyless/bodyful, bodyful/bodyless, proven-one-sided bodyful/bodyless, failed selector, failed body-key resolution, failed endpoint, and bodyless/bodyless scopes | Exactly one bodyful side with proven opposite absence is not `Compared` as body-added/removed; a failed/incomplete counterpart produces semantic add/remove or Source eligibility instead of `Failed(CounterpartUnavailable)`; a failure-free matched coordinate is tainted; no bodyful side is not `Absent(NoBody)`; or bodyless becomes a target failure |
| Planned population ownership | Source-architecture + non-vacuity mutations | A plan/session/projector escapes Research/Queries; a producer enumerates or filters its own population; a completed result retains a callback/plan; a public constructor, `init`, or record copy fabricates/mutates a planned result; or removing, adding, or duplicating one disposition does not reject completion |
| Mechanism dependency totality | Research + Queries with empty selection, Source-only selection, C#-only change, IL-only change, both exact, proven one-sided change, failed counterpart, and presentation-filter mutations | An empty set or Source without a requested local mechanism is accepted; a known required dependency is absent; Source omits a requested local prerequisite; a failed prerequisite or counterpart performs I/O; a proven one-sided change becomes `NotEligible`; no-change performs I/O; or presentation affects eligibility |
| Synchronous mechanism ownership | Research API and harness tests over `ResearchChangeMechanism` and `ImplementationDiffMechanism` | Either default/context-free `AllAvailable` includes a host mechanism; synchronous `Compare` accepts/ignores Source, ReturnToSender, or unknown flags; a retired assembly overload remains; or a host runner does not declare its complete set |
| Async query lifetime | Queries + CLI with revoked authorization, borrowed sessions, primary-plus-cleanup failure, cancellation, and single-threaded awaited reentrancy | Begin/project/complete escape one current lease; the assembly/package CLI opens a reader/session around Research; direct `match` use escapes its selector source/designation lifetime; a borrowed session is disposed; an owned lease leaks; cleanup replaces a primary failure; cancellation returns a partial/failure-shaped result; or Browser/Wasm requires threads/blocking |
| Authored-source budget | Queries boundary tests at one below/equal/one above every default, cached/uncached, retry/redirect, embedded/external PDB, shared documents, native/Browser transport-visible operations, varied scheduling, and raised-limit authorization | Any query-time PDB/source path lacks the same non-optional ledger lease; any operation/byte/decoded-text/retention/concurrency path bypasses accounting; a host raises a default without an invocation-scoped `AuthoredSourceBudgetOverrideCapability`; static `InspectionCost` is accepted as that grant; per-item/redirect limits replace the aggregate; exhaustion publishes any eligible success or scheduling changes an eligible item's disposition kind; or failure omits dimension/limit/charge |
| Direct-member pairing authority | Research, `match --implementation`, ReturnToSender, and round-trip exact-address tests with equal/different assembly names, unequal correspondence keys/roles, same path/different MVID, same token/different module, and designation lifetime expiry | `CompareMembers` accepts raw sources/handles instead of `DirectMemberComparisonInput`; designation creates a parallel pairing id rather than wrapping one direct-slot `ArtifactParticipantPairing`; direct lowering lacks its own internal selection scope; a designated pair requires assembly/key/role equality or invents one shared subject; cannot lower to one `DesignatedMemberPairKey`; an endpoint path can mint that key; pairing derives from path, occurrence, display, token, or reader equality; it outlives a source; feeds assembly-wide comparison; or bypasses address/role validation |
| Direct-consumer failure totality | Research + `match --implementation` + authored rebuild + round-trip/scope tests with failed C#, IL, address, and role dispositions | A direct result lacks ledger-derived `HasFailures` or typed diagnostics; `match` exits zero; authored rebuild reports `IlDifferent`; round-trip reports `Changed`/`Exact`; or a consumer drops the diagnostic instead of mapping failure to nonzero/context-failed/unavailable |
| Result and exit totality | Research + CLI text/Markdown/table/TSV/JSON/JSONL with public-construction attempts, duplicate member subjects across participants, empty-mechanism rejection, bodyless `Absent`, hidden/windowed target, participant, mechanism, budget, cleanup, and direct-result failures plus other `Absent` controls | A planned result can be publicly constructed, copied, or enriched; equal subjects in distinct work items collapse; a presentation row loses its work-item/participant context; a planned empty mechanism set completes; a failure disappears from retained ledgers; assembly or direct `HasFailures` depends on rendered rows; selected Implementation Diff exits zero for a failure; or `Absent` exits nonzero |

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
- Planned `ResearchChange` grouping and `ImplementationDiffResult.Members` use
  `ResearchSubjectKey` without work-item identity, so equal subjects in
  different participant pairings can collapse.
- Side-local target requests have no sealed selection scope distinguishing
  proven absence from failed/incomplete selection or body-key census, so a
  failed counterpart can become a semantic one-sided change.
- `ImplementationDiff.CompareMembers` receives two live readers and handles but
  has no typed pairing designation, exact MVID-scoped address/role contract, or
  designated-pair work-item key. `match --implementation` also fabricates an
  assembly-wide result to render its arbitrary direct pair.
- Direct results expose no ledger-derived failure summary; `match`, authored
  rebuild, and round-trip callers can convert failed evidence into zero exit or
  semantic `Changed`/`IlDifferent`.
- `CompareMembersWithPdbSource` and
  `WithPdbSourceComparisons` attach Source after comparison rather than as
  one declared mechanism.
- Assembly comparison filters and joins C#, IL, body-signal, retained Finding,
  and Source evidence through presentation-shaped subject identities and
  independently constructed populations.
- The CLI derives PDB-source targets from already-rendered changed members
  and owns the async acquisition loop outside one Queries-owned operation.
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
