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

`ImplementationDiff` is the product-side decompiled C# + IL/body + authored
Source diff projection in
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
  with decompiled C# and IL/body mechanisms, accepts checksum-gated authored
  line inspections from Services, groups changes by `ResearchSubjectKey`, and
  exposes typed display rows and unified lines without reformatting producer
  wording.
- `ResearchComparison.RetainedComparisons` keeps the native
  `FindingComparison<CSharpCanonicalLine>` and
  `FindingComparison<CanonicalIlOperation>` envelopes when requested. Authored
  Source comparisons retain `FindingComparison<string>` with the `text.line`
  descriptor. Research
  cross-checks their exactness against the richer semantic projections for
  members present on both sides. A disagreement is retained as a per-member
  `Failed` diagnostic; it does not abort healthy members in the same diff.
- `DotnetInspector.ResearchQueries` owns the authorized asynchronous operation.
  It opens or borrows inspection sessions, projects every requested mechanism,
  enforces authored-source budgets, completes the comparison, and releases
  leases before returning a typed final result.
- The CLI owns selection and presentation only. It supplies endpoint/target
  requests and capabilities, then renders the completed result. It never opens
  readers around Research, projects a mechanism, or enriches a finalized
  comparison.

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

BodyEvidenceTargetRequest
  Id                     opaque side-local target-request identity
  CorrelationId          opaque user/host selection identity
  Participant            pairing id + side-local binding
  Selection              Selected(Exact | Carried) | Failed(Diagnostics)

BodyEvidenceTargetAttempt
  Id                     opaque plan identity
  RequestId              originating request
  Participant            pairing id + side-local binding
  Selection              target attempted or typed selection failure

BodyEvidenceCoordinate
  Participant            ArtifactParticipantPairing.Id
  Key                    MemberBodyCorrespondenceKey
  Role                   Method | Getter | Setter | Adder | Remover

BodyEvidenceWorkItem
  Key                    Corresponded | ResolutionFailed | ParticipantFailed
  AttemptIds             complete target-attempt aliases
  Corresponded           coordinate plus optional before/after resolved entry
  ResolutionFailed       attempt ids plus typed per-side failure
  ParticipantFailed      endpoint/acquisition/pairing failure

BodyEvidenceComparisonPlan (internal)
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
  Labels                  exactly one inert label/group per work-item id

DirectMemberPairingDesignation
  Pairing                 one direct-slot ArtifactParticipantPairing
  Before/After            exact live participant binding + MVID
  Lifetime                cannot outlive either supplied source

DirectMemberComparisonInput
  Pairing                 DirectMemberPairingDesignation
  Before/After            exact MetadataMethodAddress
  Role                    Method | Getter | Setter | Adder | Remover
```

The plan, session, entries, callbacks, and participant proofs remain internal.
The result retains only the projection-free receipt, complete ledgers, native
producer payloads, and the total presentation map.

### Target attempts and work-item totality

The host resolves user type/member selectors into
`BodyEvidenceTargetRequest` values before plan construction. A target request
binds one independently selected exact/carried physical target to one
participant side. `CorrelationId` groups the requests produced by one user's
question but never acts as MethodDef identity. The selector owner seals the
side-local request set after resolving each side's own API/metadata surface:
one logical selector may produce requests for several participant pairs, while
an added or removed member produces a request only on the side where it exists.
A selector failure still produces a side-local request with `Failed`; the
attempt maps directly to a `ResolutionFailed` work item rather than becoming an
empty successful plan.

The coordinator mints exactly one target-attempt id for each target request
before exact/carried body resolution. A selection-failed attempt performs no
body resolution and retains that failure. The coordinator never fans one
request or one strict target across sides or participants. Explicit unscoped
`All` enumeration mints one side-local request and attempt as each live-reader
MethodDef is discovered. This granularity is deliberate:

- corresponding before/after attempts whose version-neutral keys agree map to
  one two-sided work item;
- signature drift produces separate one-sided remove/add work items with
  separate side-local requests and attempt ids;
- one selector spanning several participant pairs produces distinct requests,
  attempts, and work items per pair;
- overlapping selectors resolving the same participant, exact address, strict
  key, correspondence key, and role may alias one work item while retaining
  every attempt id.

Each carried request resolves only inside its own selected artifact/version.
Before and after requests therefore carry independently minted strict keys.
Only after both resolve does `MemberBodyCorrespondenceKey` decide whether they
share a work item. AssemblyRef-version-only drift reaches correspondence rather
than failing because one side was asked to resolve the other's strict key.

`BodyEvidenceWorkItemKey` is a closed union:

```text
CorrespondedKey
  ParticipantPairingId + MemberBodyCorrespondenceKey + RelationshipRole

ResolutionFailedKey
  BodyEvidenceTargetAttempt.Id

ParticipantFailedKey
  endpoint-outcome id or participant-pairing-failure id
```

The key does not infer identity; it records the result of prior participant and
body correspondence. Before/after resolved attempts with the same
`CorrespondedKey` map to one work item. Signature drift has different
correspondence keys and therefore different work items. Overlapping selectors
that resolve to the same coordinate add attempt aliases to that item.
Resolution failures remain per-attempt and cannot collapse because two failures
have similar diagnostics.

The plan materializes an immutable `AttemptMap`. Every target-attempt id maps
to exactly one work-item id, and every resolved or resolution-failed work item
retains at least one target-attempt id. Participant-failed work items instead
retain their endpoint/pairing failure identity. Every side-local request names
exactly one attempt; correlation ids may name several side-local requests.
Neither currency authorizes cross-side matching. Set-equality validation among
request ids, target-attempt ids, work-item keys, attempt aliases, and
`AttemptMap` rejects orphaned requests/attempts, multiply mapped attempts,
unaliased work items, and duplicate keys.

Index construction maps a selection-failed request directly to its
per-attempt failure, validates each selected exact address against its
participant, and resolves each selected carried target through
`MemberBodyTargetResolver`. It then keys resolved entries by participant
pairing, `MemberBodyCorrespondenceKey`, and role.
Duplicate correspondence keys naming different strict targets, addresses, or
roles are ambiguous only within that participant pair. Equal keys in different
participant pairs remain distinct.

A `MemberBodyResolution.Bodyless` arm is a resolved entry, not a resolution
failure. It retains its address, keys, and role in the coordinate population.
Body-producing mechanisms return `Absent` for that work item according to
their native semantics.

The resolved index exposes no enumerable population. The internal plan forms
the union of resolved coordinates, target-resolution failures, non-realized
endpoint failures, and participant-pairing failures, then assigns one opaque
work-item id to each. A failure that cannot mint a correspondence key remains a
first-class work item; it cannot become empty successful output.

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

For target-, endpoint-, or participant-failed work items, the session stamps
the shared terminal failure into every requested ledger without invoking a
producer. For healthy items, each producer retains its native payload and
outcome semantics. Internal construction rejects missing, extra, or duplicate
dispositions.

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
`BodyEvidencePresentationMap` keyed by work-item id; labels contain no address,
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
```

Standalone API comparison and explicit C#/IL presentation/test adapters return
the unscoped arm. It cannot feed Implementation Diff. Only Research-owned
session completion can create the body-evidence arm.
`ImplementationDiff.FromResearchComparison` accepts that arm only.
`CombineUnscoped` rejects a planned input; planned results never combine because
all body mechanisms belong to one session.

`ImplementationDiffResult.HasFailures` derives from retained ledgers and query
failure, not rendered or windowed rows. When Implementation Diff is selected,
every CLI text and structured output path returns nonzero for any target,
endpoint, participant, mechanism, budget, or cleanup failure. `Absent`,
including Source `NotEligible`, remains non-failing. API inspection failures
retain their existing independent exit behavior.

## Research comparison model

`ResearchDiff` is the operation facade. Both `ResearchComparison` arms retain a
flat `Changes` collection. `BySubject()` computes member- and type-centric
groups from that collection; grouped and flat consumers therefore cannot
observe divergent copies of the same result. The body-evidence arm additionally
retains its receipt, complete ledgers, presentation map, and native payloads;
the unscoped arm makes no population claim.

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
while retained comparisons expose the exact census transitions. `Source` never
replaces or changes the meaning of `CSharp`: one describes checksum-verified
authored text and the other describes product-decompiled text.

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

## Consumer contract

Use `ImplementationComparisonQuery` for assembly-, package-, project-,
platform-, directory-, or workspace-scoped implementation comparison. The host
supplies endpoint outcomes/pairings, independently selected per-side and
per-participant exact or carried target requests, the mechanism set,
capabilities, and budget. The query returns one completed
`ImplementationDiffResult`; no enrichment step exists.

Use `ImplementationDiff.DesignateMemberPair` when the caller already owns two
live `MetadataSource` values. The factory validates version-neutral assembly
identity and captures exact live participant bindings plus MVIDs in a
`DirectMemberPairingDesignation`. The designation privately wraps one
direct-slot `ArtifactParticipantPairing` using the same pairing id/bindings as
the planned lifecycle; it is not a second participant currency. It accepts no
paths, handles, tokens, or display identity. The designation cannot outlive
either source.

`ImplementationDiff.CompareMembers` accepts only
`DirectMemberComparisonInput`: that designation, exact old/new
`MetadataMethodAddress` values, and relationship role. It validates both
addresses against the designated participant bindings, completes one
synchronous work-item session by lowering the wrapped pairing into a one-item
plan, and returns
`ImplementationMemberDiffResult` before either source can expire. It accepts
only Research-owned synchronous mechanisms and cannot feed an assembly-wide
Implementation Diff. The current handle-only overload is removed.

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
unified line with `Member`, `Mechanism`, `Difference`, `Change`, and `Evidence`
columns. `Difference` contains the IL body outcome for IL rows and is empty for
C# rows, keeping mechanism, result, edit kind, and evidence as separate
dimensions.
The section binds `ImplementationComparisonQuery`. With `--authored-source`,
the query acquires eligible members' endpoint PDB and SourceLink bodies,
verifies document checksums, and adds a separately labeled Source ledger.
Missing mappings, acquisition failures, and budget exhaustion remain visible
rather than falling back to decompiled C#.
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
| `AssemblySetResolver` endpoint-to-flat-list comparison input | Typed endpoint outcome with sealed 1:N participant manifest; pairing validates the before/after manifest union |
| `AssemblyResolutionProvenance` pattern matching in Research | Opaque adapter-/host-issued `ArtifactParticipantPairing.Id` plus side-local binding |
| Package/project/directory occurrence index | Logical participant slots; duplicate slots fail ambiguous |
| `ResearchDiffOptions.TypeFilters` / `MemberTargetIdentities` | Independently selected per-side/per-participant `BodyEvidenceTargetRequest` values resolved before plan construction |
| `CSharpBodyDiff` raw-key/occurrence index | Participant pairing plus `MemberBodyCorrespondenceKey` and role |
| Independent C#, IL, body-signal, Finding, and Source populations | One internal work-item plan projected to complete mechanism ledgers |
| Public `ResearchComparison` constructors and standalone C#/IL adapters | `UnscopedResearchComparison`; cannot feed Implementation Diff |
| `ResearchChangeMechanism.AllAvailable` default | Explicit API + Body Signals + IL + C# synchronous default |
| `ResearchComparison.Combine` | `CombineUnscoped`; planned inputs reject |
| `ImplementationDiff.FromResearchComparison(ResearchComparison)` | Accept only `BodyEvidenceResearchComparison` |
| `ImplementationDiff.CompareAssemblies` | Remove; assembly-scoped callers construct typed endpoints and execute `ImplementationComparisonQuery` |
| `ImplementationDiff.Compare(ResearchDiffInput, ResearchDiffInput, ...)` | Remove; the query/session owner forms one planned Research comparison |
| `ImplementationDiff.Compare(IReadOnlyList<ImplementationAssemblyInput>, ...)` | Remove with `ImplementationAssemblyInput`; typed endpoint outcomes and participant bindings replace reader-opening inputs |
| `ImplementationDiffOptions` | Split mechanism selection into catalog ids on the query/session input, target selection into side-local target requests before planning, and changed/window options into presentation-only values |
| `ImplementationDiffMechanism` / `AllAvailable` | Retire in favor of the closed mechanism catalog; no context-free host-owned mechanism set |
| `ImplementationDiff.CompareMembers` handle-only pairing | `DesignateMemberPair` plus `DirectMemberComparisonInput`; exact-address invocation-scoped pairing only |
| `CompareMembersWithAuthoredSource` | Remove; async acquisition belongs to `ImplementationComparisonQuery` |
| `WithAuthoredSourceComparisons` | Remove; finalized results accept no new ledger |
| CLI changed-row authored-source enrichment | Dependency-gated Source projection inside one query lifetime |
| CLI API-only failure exit | Include `ImplementationDiffResult.HasFailures` in every selected output path |

## Gates

The target lifecycle is unverified until these gates exist:

| Gate | Surface | Fails if |
| --- | --- | --- |
| Endpoint-manifest totality | Artifact adapters + Queries over package `Preferred`, explicit/all TFM/RID, project outputs/dependencies, platform, directory, two-bundle embedded workspace, cross-source, and failed endpoints | One endpoint is forced to one participant; a realized endpoint has an empty/unsealed manifest; a non-realized endpoint disappears; a real selected inventory differs from its manifest; an embedded pair lacks a host-issued paired designation or uses workspace context/`ContentRef` as cross-side identity; or pairing differs from the manifest union |
| Participant correspondence | Adapter + Queries repeated/equal-identity and reordered-input fixtures | Research interprets provenance; path/version/TFM/RID/`ContentRef`/digest/MVID/registration or occurrence becomes pairing identity; duplicate logical slots select one; or adding a participant renumbers another |
| Body-target attempt totality | Research + Queries over AssemblyRef-version-only drift, signature drift, multi-participant selectors, overlapping selectors, selection/resolution failure, bodyless methods, and participant failure | A target request is not already side/participant scoped; one strict target is fanned across versions/participants; a selection failure becomes empty success or invokes body resolution; one request lacks exactly one attempt; one attempt maps to zero/multiple work items; a work item lacks attempts or failure identity; correlation ids authorize matching; `AttemptMap`, aliases, and discriminated keys differ; remove/add shares one request/key/attempt; bodyless becomes a resolution failure; or aliases weaken exact/strict/correspondence validation |
| Planned population ownership | Source-architecture + non-vacuity mutations | A plan/session/projector escapes Research/Queries; a producer enumerates or filters its own population; a completed result retains a callback/plan; or removing, adding, or duplicating one disposition does not reject completion |
| Mechanism dependency totality | Research + Queries with empty selection, Source-only selection, C#-only change, IL-only change, both exact, one failed, and presentation-filter mutations | An empty set or Source without a requested local mechanism is accepted; a known required dependency is absent; Source omits a requested local prerequisite; a failed prerequisite performs I/O; either one-sided change becomes `NotEligible`; no-change performs I/O; or presentation affects eligibility |
| Synchronous mechanism ownership | Research API and harness tests over `ResearchChangeMechanism` and `ImplementationDiffMechanism` | Either default/context-free `AllAvailable` includes a host mechanism; synchronous `Compare` accepts/ignores Source, ReturnToSender, or unknown flags; a retired assembly overload remains; or a host runner does not declare its complete set |
| Async query lifetime | Queries + CLI with revoked authorization, borrowed sessions, primary-plus-cleanup failure, cancellation, and single-threaded awaited reentrancy | Begin/project/complete escape one current lease; CLI opens a reader/session; a borrowed session is disposed; an owned lease leaks; cleanup replaces a primary failure; cancellation returns a partial/failure-shaped result; or Browser/Wasm requires threads/blocking |
| Authored-source budget | Queries boundary tests at one below/equal/one above every default, cached/uncached, retry/redirect, embedded/external PDB, shared documents, native/Browser transport-visible operations, varied scheduling, and raised-limit authorization | Any query-time PDB/source path lacks the same non-optional ledger lease; any operation/byte/decoded-text/retention/concurrency path bypasses accounting; a host raises a default without an invocation-scoped `AuthoredSourceBudgetOverrideCapability`; static `InspectionCost` is accepted as that grant; per-item/redirect limits replace the aggregate; exhaustion publishes any eligible success or scheduling changes an eligible item's disposition kind; or failure omits dimension/limit/charge |
| Direct-member pairing authority | Research exact-address tests with same path/different MVID, same token/different module, version-only assembly drift, invalid assembly identity, and designation lifetime expiry | `CompareMembers` accepts raw sources/handles instead of `DirectMemberComparisonInput`; designation creates a parallel pairing id rather than wrapping one direct-slot `ArtifactParticipantPairing`; cannot lower to a one-item plan; derives pairing from path, occurrence, display, token, or reader equality; outlives a source; feeds assembly-wide comparison; or bypasses address/role validation |
| Result and exit totality | Research + CLI text/Markdown/table/TSV/JSON/JSONL with empty-mechanism rejection, bodyless `Absent`, hidden/windowed target, participant, mechanism, budget, and cleanup failures plus other `Absent` controls | A planned empty mechanism set completes; bodyless becomes failure/nonzero; a failure disappears from retained ledgers; `HasFailures` depends on rendered rows; selected Implementation Diff exits zero for a failure; or `Absent` exits nonzero |

## Current mismatches

- `ResearchDiffOptions` defaults to `ResearchChangeMechanism.AllAvailable`,
  which includes host-owned Source and ReturnToSender while synchronous
  `ResearchDiff.Compare` implements only Research-owned mechanisms.
- `ImplementationDiffMechanism.AllAvailable` likewise includes host-owned
  Source, and the public assembly `CompareAssemblies`/`Compare` overloads plus
  harness callers bypass the target query/session ownership.
- `ResearchComparison` is publicly constructible and combines body projections
  that did not share one complete population.
- `ImplementationDiff.CompareMembers` receives two live readers and handles but
  has no typed pairing designation or exact MVID-scoped address contract.
- `CompareMembersWithAuthoredSource` and
  `WithAuthoredSourceComparisons` attach Source after comparison rather than as
  one declared mechanism.
- Assembly comparison filters and joins C#, IL, body-signal, retained Finding,
  and Source evidence through presentation-shaped subject identities and
  independently constructed populations.
- The CLI derives authored-source targets from already-rendered changed members
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
