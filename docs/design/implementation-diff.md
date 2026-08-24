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
  leases before returning a typed final query outcome.
- The assembly/package `diff` CLI owns selection and presentation only. It
  supplies endpoint/target requests and capabilities, then renders the
  completed result or typed query failure. It never opens readers around
  Research, projects a mechanism, or enriches a finalized comparison. The direct
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

BodyEvidenceParticipantDomain
  Key                    Admitted(ArtifactParticipantPairing.Id) |
                         EndpointAbsent(ComparisonEndpointPairingSlot.Id) |
                         Failed(EndpointSlotFailure(slot id) |
                         ParticipantPairingTerminal(pairing-outcome id,
                         Ambiguous | Failed))
  EndpointSlot           originating ComparisonEndpointPairingSlot.Id
  Outcome                live admitted pairing | two-sided endpoint-absence
                         proof | typed terminal outcome with complete upstream
                         ambiguity/failure payload

BodyEvidenceSelectionQuestion
  Id                     opaque user/host question identity
  Intent                  Explicit | Enumerative
  EndpointSlots          non-empty sealed subset of the pairing plan

BodyEvidenceSelectionCorrelation
  Question               exact BodyEvidenceSelectionQuestion
  Scopes                  non-empty sealed map:
                         ParticipantDomain.Key -> SelectionScope.Id

BodyEvidenceSelectionCorrelationManifest
  Entries                 non-empty complete declared-question map

BodyEvidenceSelectionScope
  Id                     opaque comparison-scoped selection identity
  CorrelationId          owning selection correlation
  Domain                 Admitted(ArtifactParticipantPairing.Id) |
                         EndpointAbsent(before/after proofs) |
                         ParticipantFailed(failed-domain key + diagnostic)
  Before/After           for Admitted only:
                         Selected(request ids) | Absent(proof) | Failed

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
                         CorrespondenceAmbiguous |
                         CounterpartUnavailable | SelectionFailed |
                         ResolutionFailed | ParticipantFailed
  AttemptIds             selected-target aliases; empty only for
                         selection/participant failure
  Corresponded           coordinate plus optional before/after resolved entry
  DesignatedPair         exact before/after direct entries
  CorrespondenceAmbiguous
                         authoritative CorrespondenceAmbiguousKey plus every
                         colliding resolved entry
  CounterpartUnavailable resolved entry plus failed opposite scope/census
  SelectionFailed        scope side plus typed selection failure
  ResolutionFailed       attempt ids plus typed per-side failure
  ParticipantFailed      endpoint/acquisition failure or participant-pairing
                         ambiguity/failure

BodyEvidenceComparisonPlan (internal)
  Questions               sealed pre-acquisition question set
  Correlations            sealed non-empty correlation manifest
  ParticipantDomains     sealed admitted/endpoint-absent/failed domain set
  SelectionScopes        sealed terminal or side-outcome scopes
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
  Questions               exact immutable question set
  ParticipantDomains     exact immutable participant-domain set
  Correlations            exact immutable correlation manifest
  SelectionScopes         complete immutable
                         id/correlation/domain/outcome receipt map
  WorkItemSet             internal validated set identity

BodyEvidenceComparedDisposition<T>
  Verdict                 Exact | Different
  Authority               Producer | SessionBodyPresence
  Native                  optional producer-owned payload/display evidence
  BodyPresence            None | BodyAdded | BodyRemoved

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
  Lowering                direct factory mints one internal correlation/scope

ImplementationMemberDiffResult
  RequestKey              requested DesignatedMemberPairKey
  WorkItems               completed direct-session work-item results
  BeforeSubject           Resolved(ResearchSubjectKey) | Failed
  AfterSubject            Resolved(ResearchSubjectKey) | Failed
  Ledgers                 complete requested synchronous ledgers
  Native                  producer-owned direct comparison payloads
  Outcome                 Exact | Different | NotApplicable | Unavailable
  HasFailures             derived from retained ledgers
  AbsenceReasons          typed retained non-failing details
  Diagnostics             typed retained failure details
```

The plan, session, entries, callbacks, and participant proofs remain internal.
The result retains only the projection-free receipt, including its complete
question/domain/correlation/scope outcome maps, complete ledgers, native
producer payloads, and the total presentation map.

### Target attempts and work-item totality

Before acquisition, the host seals a non-empty
`BodyEvidenceSelectionQuestion` set against the endpoint-pairing plan. Each
question owns one authoritative `Intent` and a non-empty set of endpoint slot
ids. The CLI/host adapter requires exact set equality between its typed
requested-question ids and this set, and validation rejects an unknown,
duplicate, or omitted requested slot. A question cannot disappear before
acquisition or acquire a different endpoint scope after an endpoint fails.

After endpoint and participant pairing, the coordinator seals one
`BodyEvidenceParticipantDomain` set by exhaustively lowering the acquisition-
owned `ComparisonEndpointPairingSlotOutcomeSet`. Every
`EndpointAbsent` slot outcome contributes one `EndpointAbsent` domain retaining
both proofs. Every failed slot outcome contributes one
`Failed(EndpointSlotFailure)` domain retaining its typed failure and
diagnostic. Inside a `Participants` slot outcome, every
`ArtifactParticipantPairingOutcome.Admitted` contributes one `Admitted` domain
using its `ArtifactParticipantPairing.Id`; only `Paired`, `BeforeOnly`, and
`AfterOnly` can enter that arm. Every `Ambiguous` or `Failed` participant
outcome contributes one `Failed(ParticipantPairingTerminal)` domain keyed by
the exact pairing-outcome id and retaining its outcome kind, typed reason,
diagnostic, and complete upstream candidate/affected-input payload. No
downstream layer reconstructs those facts from provenance or side bindings.
Every domain retains its originating endpoint-slot id, and every slot expands
to at least one domain.

Exact set equality connects the endpoint plan, slot-outcome set, each
participant outcome set's manifest-entry partition, and participant domains.
It rejects an omitted, duplicated, rekeyed, reparented, empty, overlapping, or
success-shaped terminal outcome. A failed endpoint or ambiguous participant
outcome therefore needs no invented participant binding to remain in the
question population.

Before side-local selection, the coordinator seals one
`BodyEvidenceSelectionCorrelationManifest` from the complete declared
question set and the sealed participant-domain set. A correlation retains its
exact question rather than restating its intent or endpoint scope. Its non-empty
domain map must equal every sealed participant domain whose originating slot is
in that question's endpoint-slot set. This mechanical expansion admits no
host-chosen “applicable subset”: an admitted pairing and a failed slot are
equally impossible to omit. Exact question/correlation id equality rejects a
question that disappears before query input. For a well-behaved non-CLI host,
the pre-acquisition question set is that host's authoritative declaration. The
product does not attempt to recover a question the host never declared.

The coordinator mints a distinct scope for every
`(CorrelationId, ParticipantDomain.Key)` entry. This preserves question-local
failure and absence domains even when several selectors apply to the same
failed endpoint slot. Selection fills side outcomes only for an `Admitted`
scope. An `EndpointAbsent` domain seals a terminal successful-absence scope
with its two proofs. A `Failed` domain seals a `ParticipantFailed` scope with
the same typed terminal identity and diagnostic. Neither terminal arm
fabricates a participant, side inventory, or target request. An admitted scope
records an independent outcome for each side:

- `Selected` contains the complete non-empty set of side-local exact/carried
  target-request ids admitted by that side's inventory;
- `Absent` carries typed proof that the complete side-local inventory contains
  no selected target;
- `Failed` carries the selection, inventory, or participant diagnostic that
  prevented the side from proving either selected targets or absence.

The query, plan, and receipt require one bijection among the sealed questions,
participant domains, manifest correlations, their non-empty
`(ParticipantDomain.Key, ScopeId)` maps, and the scope map. Every scope appears
in exactly one correlation with the same domain; every correlation retains one
exact question; a scope cannot restate or disagree with its question or domain.
Empty correlations, an undeclared or wrong-slot domain, a missing expanded
domain, missing or extra scopes, duplicate correlation-local domains, altered
intent/endpoint scope, reparented scopes, and a receipt whose
question/domain/manifest/outcome sets differ from the sealed query input reject
before projection. An all-failed endpoint population is therefore a valid
non-empty correlation population whose scopes all retain participant failures,
not a reason to omit the query.

The receipt retains the exact question set, participant-domain set, and
correlation manifest plus every scope's domain arm and, for admitted scopes,
both side-outcome arms. An `EndpointAbsent` scope or admitted scope whose sides
are both `Absent(proof)` intentionally creates no body work item or mechanism
ledger because there is no selected body coordinate, but it remains a
completed absence proof in the immutable result. A `ParticipantFailed` scope
creates one terminal participant-failed work item under its question-local
scope and failed-domain key; the work item retains the exact terminal outcome,
complete upstream payload, reason, and diagnostic. Consumers never infer
absence from an empty work-item set.

`Explicit` means the originating typed selector is expected to name at least
one target across its correlation's complete scope map; `Enumerative` means a
set/filter request for which zero targets is a valid answer. Row filters,
formatters, and mechanism selection cannot change that manifest-owned intent.

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

`CorrelationId` groups scopes produced by one user's question but never acts as
MethodDef identity. One logical selector spanning several participant pairs
produces distinct scopes. Explicit unscoped `All` seals each admitted
participant-local scope only after its full side-local MethodDef inventories
have been enumerated; a failed domain is already terminal and is never
enumerated. A failed or prematurely ended admitted-domain enumeration is
`Failed`, never `Absent` or a shortened `Selected` set.

Every request belongs to exactly one `Selected` scope side and binds one target
to that participant side. The coordinator mints exactly one target-attempt id
for each request before exact/carried body resolution. It never fans one
request or strict target across sides or participants. A selection-scope
failure creates a `SelectionFailed` work item without inventing a target
request. This granularity is deliberate:

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
  BodyEvidenceSelectionScope.Id + ParticipantPairingId
  MemberBodyCorrespondenceKey + RelationshipRole

DesignatedMemberPairKey
  DirectMemberPairingDesignation.Pairing.Id
  Before MetadataMethodAddress + RelationshipRole
  After MetadataMethodAddress + RelationshipRole

CorrespondenceAmbiguousKey
  BodyEvidenceSelectionScope.Id + ArtifactParticipantPairing.Id + side
  MemberBodyCorrespondenceKey + RelationshipRole

CounterpartUnavailableKey
  BodyEvidenceSelectionScope.Id + affected BodyEvidenceTargetAttempt.Id

SelectionFailedKey
  BodyEvidenceSelectionScope.Id + side

ResolutionFailedKey
  BodyEvidenceTargetAttempt.Id

ParticipantFailedKey
  BodyEvidenceSelectionScope.Id +
  BodyEvidenceParticipantDomain.Failed.Key
```

The key does not infer identity; it records the result of prior participant and
body correspondence. Before/after resolved attempts with the same
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

Only `DirectMemberComparisonInput` can mint a
`DesignatedMemberPairKey`. It records one caller-authorized exact pair without
asserting equal assembly identity, correspondence key, signature, or
relationship role. It therefore supports original-to-emitted comparisons and
`match --implementation` comparisons between arbitrary methods while retaining
both exact side-local identities. Endpoint, selector, and assembly-wide paths
cannot mint this key or use the direct designation as correspondence evidence.
Direct construction mints one real opaque selection correlation with
`Intent = Explicit`, one internal endpoint slot in its sealed question, that
slot's acquisition-owned one-outcome admitted pairing receipt, one admitted
participant domain/scope, and one exact request and attempt per side. The caller
neither supplies nor observes the outcome receipt, question, correlation, or
scope. If both attempts resolve,
the direct factory maps them to the designated-pair key. If either fails, that
attempt maps to `ResolutionFailedKey` and the resolved opposite attempt maps to
its per-attempt `CounterpartUnavailableKey` using the internally minted scope
id. `AttemptMap` totality and address/role validation therefore remain
identical to the planned path without inventing caller authority or a parallel
failure key.

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

Index construction validates each selected exact address against its
participant and resolves each selected carried target through
`MemberBodyTargetResolver`. It records resolution failures before forming
correspondence and marks that scope side's key census incomplete. Only
complete scopes bucket resolved entries by scope, participant pairing, side,
`MemberBodyCorrespondenceKey`, and role. A bucket containing distinct strict
targets or addresses is emitted as correspondence ambiguity before any
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
dependencies. The session selects its complete mechanism set from that catalog
and materializes the dependency graph before projection. The requested set must
be non-empty and dependency-closed. Unknown ids, an empty set, and a known
mechanism whose required catalog dependency is absent all reject before plan
projection; hosts cannot synthesize descriptors or an "all available" set.

`Project` and `ProjectAsync` privately walk all work items and require one
`Compared`, `Absent`, or `Failed` disposition per work-item id. A callback sees
only the current work item and already validated prerequisite dispositions for
that same id. It cannot enumerate another mechanism or the plan.

For selection-, target-, correspondence-, counterpart-, endpoint-, or
participant-failed work items, the session stamps the shared terminal failure
into every requested ledger without invoking a producer. For healthy items,
each producer retains its native payload and outcome semantics. `Compared`
requires exactly one `BodyEvidenceComparedDisposition`. Producer authority
requires a two-body native exact-or-different verdict and no body-presence
value. Session body-presence authority requires `Different` plus exactly one
`BodyAdded` or `BodyRemoved` value; optional single-side native display evidence
does not carry another verdict. For two bodyful sides, a native unavailable,
decode failure, token-resolution failure, unsupported boundary, or other
verdict-less outcome becomes
`Failed(ComparisonUnavailable)` and may retain its native payload only as
diagnostic evidence. It never enters a `Compared` disposition. Native
`OldBodyMissing`/`NewBodyMissing` is a migration encoding, not
`ComparisonUnavailable`; the target session does not invoke that producer
shape for a resolved bodyless side. Internal construction rejects missing,
extra, duplicate, verdict-less `Compared`, or mechanism-invalid `Absent`
dispositions.

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

### Query-owned asynchronous lifetime

`ImplementationComparisonQuery` is one
`InspectionQueryRegistry.AddAsync` operation. Begin, synchronous projections,
dependency-gated Source projection, completion, and strict cleanup execute
lexically under one current query access lease. The query owns or borrows every
`AssemblyInspectionSession`, PDB/source content lease, retained artifact
binding, and transport capability.

The CLI receives no plan or session. It supplies typed endpoint and target
requests, mechanism selection, capabilities, and budget, then receives one
sealed outer outcome:

```text
ImplementationComparisonQueryOutcome
  Completed              ImplementationDiffResult
  Failed                 ImplementationComparisonQueryFailure

ImplementationComparisonQueryFailure
  Phase                  Acquisition | Planning | Projection | Completion |
                         Cleanup
  Primary                typed query diagnostic
  Cleanup                zero or more typed cleanup diagnostics
```

Owned resources and lease claims are released after success or failure.
Caller-owned borrowed sessions are never disposed. A cleanup failure alone is
a `Failed` query outcome and no completed result escapes. When another
operation failure already exists, that remains primary and the cleanup
diagnostic is retained beside it. The failed arm contains no partial result,
plan, session, work-item population, or forgeable receipt. Cancellation
propagates as cancellation, drains owned work, and returns neither outcome nor
a partial/failure-shaped comparison substitute.

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
target/participant ambiguity or failures remain their original failures. Thus
executor
ordering may change the reported exhausted dimension, observed/requested
charge, earlier diagnostic, and how early work stops, but never which eligible
items appear successful or failed.

### Completion and result boundary

`Complete` is the session's only final transition. It is one-shot and fails
unless every declared mechanism has one complete, disjoint ledger. Projection
or completion afterward fails visibly. Completion builds a provisional
`BodyEvidencePresentationMap` keyed by work-item id. Each entry has inert
participant and member labels sufficient to distinguish two equal member
subjects in different TFM/RID/assembly participants. Labels contain no address,
target, participant proof, or correspondence key from which evidence could be
reselected. The query publishes `Completed(result)` only after strict cleanup
succeeds; cleanup failure discards that provisional result and publishes only
the outer `Failed` arm.

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
  Changes                 complete typed BodyEvidenceResearchChange values

BodyEvidenceResearchChange
  WorkItem                BodyEvidenceWorkItemId
  Mechanism               requested mechanism id
  Evidence                Producer(ResearchChange) |
                         SessionBodyPresence(BodyAdded | BodyRemoved)
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

`ImplementationDiffResult.HasFailures` derives only from its retained completed
ledgers, not rendered or windowed rows. Query and cleanup failure instead
produce `ImplementationComparisonQueryOutcome.Failed`, whose formatter and
exit mapping do not require or fabricate an `ImplementationDiffResult`. When
Implementation Diff is selected, every CLI text and structured output path
returns nonzero for any target, endpoint, participant, mechanism, budget,
query, or cleanup failure. `Absent`, including Source `NotEligible`, remains
non-failing. API inspection failures retain their existing independent exit
behavior.

`ImplementationMemberDiffResult.HasFailures` likewise derives from its complete
direct ledgers and retains their typed diagnostics. It is not inferred from
row count or a nullable native payload. The non-empty requested mechanism set
makes this precedence total:

1. `Unavailable` when any ledger is `Failed`, even when another mechanism
   compared successfully;
2. `Different` when no ledger failed and at least one `Compared` disposition
   proves a difference;
3. `Exact` when no ledger failed, at least one disposition is `Compared`, and
   every `Compared` disposition is exact; any accompanying `Absent`
   dispositions retain their catalog-defined non-failing reasons;
4. `NotApplicable` when every requested disposition is `Absent`.

An all-bodyless designated pair therefore yields
`NotApplicable(Absent(NoBody))`: it is not a failure, an exactness proof, or a
semantic difference. Unknown or mechanism-invalid absence reasons reject
completion rather than entering this reduction. Because every `Compared`
disposition contains exactly one exact-or-different verdict, no fifth
verdict-less state remains. A direct failure has no semantic exact/changed
verdict, while a mixed exact/absent result is `Exact` only for the applicable
requested mechanisms and retains the absence reason.

## Research comparison model

`ResearchDiff` is the operation facade. `UnscopedResearchComparison` retains
the existing flat `ResearchChange` collection and subject grouping because it
makes no multi-participant population claim.
`BodyEvidenceResearchComparison` instead retains a closed
`BodyEvidenceResearchChange` union. Its producer arm wraps the unchanged
`ResearchChange`; its session arm carries the typed `BodyAdded` or
`BodyRemoved` evidence from a session-authoritative `Compared` disposition.
Completion requires exactly one session arm for every such
work-item/mechanism disposition and rejects a missing or duplicate arm. That
arm is not a `ResearchChange`, Finding, or synthetic producer row.
Completion likewise requires a non-empty complete producer-arm set for every
producer-authoritative `Compared(Different)` disposition and no producer change
arm for `Compared(Exact)`. The set is derived from the producer-owned native
comparison and rejects missing, extra, or duplicate changes; each retained
change can therefore supply its own typed fallback when it has no visible native
line. Failed producer arms and retained partial payloads carry no
exact/different verdict and are validated separately.
`ByWorkItem()` is the authoritative grouping; any member-centric convenience
projection groups producer changes by `(workItemId, ResearchSubjectKey)`, never
by `ResearchSubjectKey` alone. The planned arm additionally retains its
receipt, complete ledgers, presentation map, and native payloads.

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
producer payload with its owning work-item id and mechanism. The separate
session-body-presence arm carries no producer subject or Finding. Rendering and
grouping begin from the typed union and the presentation map, so equal
`StableSelector` values in two participants cannot collapse and a
producer-free body-presence difference cannot disappear.

## Consumer contract

Use `ImplementationComparisonQuery` for assembly-, package-, project-,
platform-, directory-, or workspace-scoped implementation comparison. The host
supplies the acquisition-owned sealed endpoint plan/outcomes and complete
`ComparisonEndpointPairingSlotOutcomeSet`, the pre-acquisition
question/endpoint-slot set, typed target selectors, the mechanism set,
capabilities, and budget. The query coordinator derives the participant domains
by exhaustive typed lowering, then derives the exact non-empty correlation
expansion, pre-minted scopes, their terminal or side-local outcomes, and
admitted scopes' exact or carried target requests. The query returns one
completed `ImplementationDiffResult`; no enrichment step exists.

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
assemblies. Every caller switches exhaustively on the total direct outcome;
`IsExact`, when retained as a convenience, is exactly
`Outcome == Exact`. Authored rebuild maps `Unavailable` to `ContextFailed`;
round-trip and scope comparison map it to their typed unavailable state. A
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
Disposition             Compared | Absent | Failed
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
| `AssemblySetResolver` endpoint-to-flat-list comparison input | Sealed `ComparisonEndpointOutcomeSet` plus `ComparisonEndpointPairingSlotOutcomeSet`; exact request/outcome and plan-slot/outcome equality, 1:N participant manifests, and disjoint total participant-outcome partitions preserve admitted bindings and complete terminal payloads |
| `AssemblyResolutionProvenance` pattern matching in Research | Opaque adapter-/host-issued `ArtifactParticipantPairing.Id` plus side-local binding |
| Package/project/directory occurrence index | Logical participant slots; duplicate slots fail ambiguous |
| `ResearchDiffOptions.TypeFilters` / `MemberTargetIdentities` | A pre-acquisition question set with one authoritative intent and non-empty endpoint-slot scope per question; pairing then seals admitted, endpoint-absent, and terminal participant domains (including distinct ambiguous/failed pairing outcomes), and the non-empty correlation manifest expands each question's slots to exactly those domain/scope entries, followed by independent terminal or `Selected` / proven `Absent` / `Failed` scope outcomes and a completed receipt retaining the exact questions, domains, manifest, scopes, and proofs |
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
| `ImplementationDiff.FromResearchComparison(ResearchComparison)` | Accept only `BodyEvidenceResearchComparison` |
| `ImplementationDiff.CompareAssemblies` | Remove; assembly-scoped callers construct typed endpoints and execute `ImplementationComparisonQuery` |
| `ImplementationDiff.Compare(ResearchDiffInput, ResearchDiffInput, ...)` | Remove; the query/session owner forms one planned Research comparison |
| `ImplementationDiff.Compare(IReadOnlyList<ImplementationAssemblyInput>, ...)` | Remove with `ImplementationAssemblyInput`; typed endpoint outcomes and participant bindings replace reader-opening inputs |
| `ImplementationDiffOptions` | Split mechanism selection into catalog ids on the query/session input, target selection into pre-acquisition question/endpoint-slot declarations followed by exact admitted/endpoint-absent/terminal domain expansion (including ambiguous/failed pairings), complete scope outcomes, and side-local requests for admitted scopes, and changed/window options into presentation-only values |
| `ImplementationDiffMechanism` / `AllAvailable` | Retire in favor of the closed mechanism catalog; no context-free host-owned mechanism set |
| Public `ImplementationDiffResult` / `ImplementationDiffMember` constructors, `SourceComparison` init, and record copying | Sealed non-record results with internal constructors, get-only immutable state, and no post-completion enrichment |
| `ImplementationDiffResult.Members` and planned `ResearchComparison.BySubject()` | Work-item-keyed result projection and the producer/session `BodyEvidenceResearchChange` union; any producer-subject view retains its work-item id |
| `ImplementationDiff.CompareMembers` handle-only pairing and shared `ImplementationMemberDiffResult.Subject` | `DesignateMemberPair` plus `DirectMemberComparisonInput`; exact addresses and per-side roles lower to one `DesignatedMemberPairKey`, including explicitly designated cross-identity assemblies, and the result retains both side-local subjects |
| `MatchCommand.BuildImplementationDiffView` direct result construction and unconditional zero exit | Invocation-scoped direct designation plus a formatter overload for product-issued `ImplementationMemberDiffResult`; retained direct failure returns nonzero |
| Direct `IsExact`/empty-row inference | Exhaustive `Exact \| Different \| NotApplicable \| Unavailable` handling; all-absent results retain reasons and never masquerade as exact, changed, or failed |
| ReturnToSender and round-trip `CompareMembers` calls | Product-issued direct designations over original/emitted or emitted/emitted sources; differing assembly identities remain valid, failed ledgers map to context-failed/unavailable, and all-absent ledgers map to typed not-applicable rather than semantic difference |
| `CompareMembersWithPdbSource` | Remove; async acquisition belongs to `ImplementationComparisonQuery` |
| `WithPdbSourceComparisons` | Remove; finalized results accept no new ledger |
| CLI changed-row PDB-source enrichment | Dependency-gated Source projection inside one query lifetime |
| CLI API-only failure exit | Include assembly and direct result `HasFailures` in every selected output path |
| Query/cleanup failure without result currency | `ImplementationComparisonQueryOutcome.Failed` with primary and cleanup diagnostics, no partial result, typed output, and nonzero exit |
| Unified-line-only CLI rows | Ledger-first same-schema evidence/diagnostic records; after native filtering, every producer/session `Compared(Different)` with no visible line gets its own typed fallback, while presentation-safe failed partial evidence may accompany but never replace its diagnostic; semantic row windows count evidence but never count or suppress mandatory ledger/query diagnostics |
| Unqualified `options.Columns` / `options.Fields` forwarding | `ImplementationDiffSectionDescriptor.IntegrityColumns` drives both schema declaration and the effective Markout projection: user projection narrows ordinary evidence, while a row population with mandatory diagnostics retains the discriminator, failure identity/context, reason, and evidence columns in every table-derived structured mode |

## Gates

The target lifecycle is unverified until these gates exist:

| Gate | Surface | Fails if |
| --- | --- | --- |
| Endpoint-manifest totality | Artifact adapters + Queries over package `Preferred`, explicit/all TFM/RID, project outputs/dependencies, platform, directory, two-bundle embedded workspace, cross-source, explicit endpoint absence, and failed endpoints | Request and outcome `(Side, Id)` sets differ; the pairing plan omits or repeats a request; a duplicate/rekeyed/cross-side outcome occupies another request; failed/omitted acquisition is treated as `Absent`; a failed endpoint makes an opposite manifest one-sided; one endpoint is forced to one participant; a realized endpoint has an empty/unsealed manifest; a real selected inventory differs from its manifest; an embedded pair lacks a host-issued paired designation or uses workspace context/`ContentRef` as cross-side identity; or pairing differs from a failure-free manifest union |
| Participant correspondence | Adapter + Queries repeated/equal-identity, duplicate-slot, and reordered-input fixtures | The acquisition slot-outcome set differs from the endpoint plan; a participant outcome set does not partition its exact manifest union; Research interprets provenance; path/version/TFM/RID/`ContentRef`/digest/MVID/registration, physical method/body address, or occurrence becomes pairing identity/evidence; duplicate logical slots select one; an `Ambiguous` or `Failed` pairing outcome exposes an admitted binding or is omitted, reconstructed downstream, or loses its exact outcome id/kind, complete upstream payload, reason, or diagnostic when lowered to a terminal domain; or adding a participant renumbers another |
| Selection-correlation totality | CLI/host + Queries over explicit/enumerative questions spanning one/multiple endpoint slots and admitted pairings, two-sided endpoint absence, all-failed and mixed admitted/failed endpoint populations, all-ambiguous and mixed admitted/ambiguous participant-pairing outcomes, multiple questions over one ambiguous slot, zero-result filters, reordered inputs, omitted/mixed/reparented questions/scopes/domains, and direct lowering | The pre-acquisition typed requested-question ids or endpoint-slot scopes differ from the sealed question set; a question names no slot or an unknown slot; an endpoint slot expands to no domain; any `Admitted(Paired \| BeforeOnly \| AfterOnly)`, `Ambiguous`, or `Failed` participant outcome lacks exactly one matching admitted/terminal participant domain; pairing outcomes differ from the sealed slot-owned admitted/endpoint-absent/failed participant-domain set; a correlation changes its question/intent/slot scope, lacks the exact domain expansion of those slots, omits an endpoint-absent, ambiguous, or failed domain, or duplicates a domain; a scope is missing, extra, belongs to multiple correlations, disagrees on correlation/domain, or is omitted before selection; a terminal domain fabricates a participant/side inventory/request; a terminal pairing domain loses its exact outcome id/kind/payload/reason/diagnostic or lacks a question-local participant-failed work item; query input, plan, and receipt question/domain/manifest/scope sets differ; a selected or failed correlated scope still yields no-match; a correlation made entirely of endpoint-absent/admitted-two-sided-absent scopes does not yield typed no-match; an all-ambiguous or all-failed correlation cannot complete with retained failures; an enumerative terminal failure becomes silent; an enumerative proven-empty correlation invents evidence; or direct lowering lacks its one internal slot outcome/question/admitted-domain/scope |
| Body-target attempt totality | Research + Queries over AssemblyRef-version-only drift, accessor roles, signature drift, same-scope and split-across-scope correspondence-key collisions, multi-participant selectors, overlapping selectors, endpoint/two-sided participant absence, selection/resolution failure, incomplete `All`, bodyless methods, and participant ambiguity/failure | An admitted scope side is not exactly `Selected`, proven `Absent`, or `Failed`; an endpoint-absent or participant-failed scope has side outcomes or target requests; an endpoint-absent scope invents a work item or loses either proof; an ambiguous/failed participant domain lacks exactly one question-local terminal work item and complete failure ledgers or invokes selection/resolution/producers; the completed participant-domain/scope outcome set differs from the sealed manifest/query input or plan; a two-sided absent admitted scope disappears, invents a work item, or is inferred from an empty work-item set; selected request ids differ from their requests; a failed/incomplete/ambiguous census becomes absence or a shortened selected set; a target request is not already scope/side/participant scoped; an exact target omits or bypasses relationship-role validation; one strict target is fanned across versions/participants; a selection failure invokes body resolution; one request lacks exactly one attempt; one attempt maps to zero/multiple work items; a same-scope collision lacks one side-scoped ambiguity work item keyed by the authoritative `CorrespondenceAmbiguousKey` and retaining all colliding attempts; a split-across-scope collision creates ambiguity, duplicate-key rejection, or cross-scope aliasing; a dependent opposite attempt lacks its own counterpart-unavailable item; a work item lacks attempts or question-local failure identity; correlation ids authorize matching; `AttemptMap`, aliases, and discriminated keys differ; remove/add shares one request/key/attempt; bodyless becomes a resolution failure; or aliases weaken exact/strict/correspondence validation |
| Counterpart and body-presence disposition | Research C#/IL/body-signal tests over bodyful/bodyful, resolved bodyless/bodyful and bodyful/bodyless, proven-one-sided bodyful/bodyless, failed selector, failed body-key resolution, correspondence ambiguity, ambiguous/failed participant pairing, failed endpoint, and bodyless/bodyless scopes | Two bodyful sides bypass the producer; producer authority lacks one native exact/different verdict or carries body-presence evidence; a resolved or proven-one-sided exactly-one-bodyful item lacks a session-authoritative `Compared(Different, BodyAdded \| BodyRemoved)` value or exactly one matching `BodyEvidenceResearchChange` session arm; optional single-side display evidence supplies another verdict; a missing-body native result becomes `Failed(ComparisonUnavailable)`; a failed/incomplete/ambiguous counterpart or terminal participant domain produces semantic pair/add/remove or Source eligibility instead of its typed terminal failure; an attempt appears in both failure items; a failure-free unambiguous matched coordinate is tainted; no bodyful side is not `Absent(NoBody)`; or bodyless becomes a target failure |
| Planned population ownership | Source-architecture + non-vacuity mutations | A plan/session/projector escapes Research/Queries; a producer enumerates or filters its own population; a completed result retains a callback/plan; a public constructor, `init`, or record copy fabricates/mutates a planned result; or removing, adding, or duplicating one disposition does not reject completion |
| Mechanism dependency totality | Research + Queries with empty selection, Source-only selection, C#-only change, IL-only change, both exact, proven one-sided change, failed/ambiguous counterpart, native comparison unavailable, Finding inspection/cross-validation failure, and presentation-filter mutations | An empty set or Source without a requested local mechanism is accepted; a known required dependency is absent; Source omits a requested local prerequisite; a failed prerequisite, counterpart, correspondence, native comparison, Finding inspection, or cross-validation performs I/O; a proven one-sided change becomes `NotEligible`; no-change performs I/O; or presentation affects eligibility |
| Synchronous mechanism ownership | Research API and harness tests over `ResearchChangeMechanism` and `ImplementationDiffMechanism` | Either default/context-free `AllAvailable` includes a host mechanism; synchronous `Compare` accepts/ignores Source, ReturnToSender, or unknown flags; a retired assembly overload remains; or a host runner does not declare its complete set |
| Async query lifetime | Queries + CLI with revoked authorization, borrowed sessions, completion validation failure, success-plus-cleanup failure, primary-plus-cleanup failure, cancellation, and single-threaded awaited reentrancy | Begin/project/complete escape one current lease; the assembly/package CLI opens a reader/session around Research; direct `match` use escapes its selector source/designation lifetime; a borrowed session is disposed; an owned lease leaks; a failed query lacks the typed outer arm or exposes a partial/completed result; cleanup replaces a primary failure or loses its diagnostic; `ImplementationDiffResult.HasFailures` absorbs query failure; cancellation returns an outcome or partial/failure-shaped result; or Browser/Wasm requires threads/blocking |
| Authored-source budget | Queries boundary tests at one below/equal/one above every default, cached/uncached, retry/redirect, embedded/external PDB, shared documents, native/Browser transport-visible operations, varied scheduling, and raised-limit authorization | Any query-time PDB/source path lacks the same non-optional ledger lease; any operation/byte/decoded-text/retention/concurrency path bypasses accounting; a host raises a default without an invocation-scoped `AuthoredSourceBudgetOverrideCapability`; static `InspectionCost` is accepted as that grant; per-item/redirect limits replace the aggregate; exhaustion publishes any eligible success or scheduling changes an eligible item's disposition kind; or failure omits dimension/limit/charge |
| Direct-member pairing authority | Research, `match --implementation`, ReturnToSender, and round-trip exact-address tests with equal/different assembly names, unequal correspondence keys/roles, same path/different MVID, same token/different module, and designation lifetime expiry | `CompareMembers` accepts raw sources/handles instead of `DirectMemberComparisonInput`; designation creates a parallel pairing id rather than wrapping one direct-slot `ArtifactParticipantPairing`; the participant pairing/designation carries a physical method address instead of receiving it only through `DirectMemberComparisonInput`; direct lowering lacks its own internal selection scope; a designated pair requires assembly/key/role equality or invents one shared subject; cannot lower to one `DesignatedMemberPairKey`; an endpoint path can mint that key; pairing derives from path, occurrence, display, token, or reader equality; it outlives a source; feeds assembly-wide comparison; or bypasses address/role validation |
| Finding cross-validation totality | Research + Queries over semantic success plus Finding acquisition failure, semantic/Finding disagreement, duplicate generic diagnostics, partial IL payload, and Source requested | The final mechanism ledger remains `Compared`; the same work item/mechanism has both `Compared` and `Failed` outcomes; `HasFailures` is false; partial payload supplies a semantic verdict or Source eligibility; Source performs I/O; the selected CLI exits zero; or the failed row is missing/duplicated |
| Direct-consumer outcome totality | Research + `match --implementation` + authored rebuild + round-trip/scope tests with failed C#, IL, address, and role dispositions, two-body native-unavailable results, resolved bodyless/bodyful transitions, all-bodyless mechanisms, and mixed exact/absent ledgers | `Compared` lacks exactly one exact-or-different verdict; two-body native unavailable is not `Failed(ComparisonUnavailable)`; missing-body native unavailable becomes failure; reduction does not follow `Unavailable` then `Different` then `Exact` then `NotApplicable` precedence; a direct result lacks ledger-derived `HasFailures`, typed diagnostics, or retained absence reasons; `match` exits zero for failure or nonzero for not-applicable; authored rebuild reports `IlDifferent` for failed/not-applicable evidence; round-trip reports `Changed`/`Exact` for either; or a consumer drops the diagnostic/absence reason instead of mapping it to nonzero/context-failed/unavailable or typed not-applicable |
| Ledger output visibility | CLI text/Markdown/table/TSV/JSON/JSONL over producer-lined evidence, multiple producer changes, producer-different Body Signals/no-line evidence, session body-presence evidence with no adapter and with every optional line filtered, producer exact/no-line controls, presentation-safe failed partial IL evidence, pre-producer endpoint/participant/selection/target/correspondence failure, native comparison unavailable, Source missing mapping, Source not eligible, query/cleanup failure, changed/native-line filtering, row windows including `--jsonl --rows 1`, and `Member`-/empty-field-only projections over ledger and query failures | A completed or failed query cannot use the one schema; a producer `Compared(Different)` has an empty/incomplete/duplicate change set or any retained producer change with no post-filter line lacks exactly one native typed fallback; a session `Compared(Different)` with no post-filter line lacks exactly one body-presence fallback; `Compared(Exact)` emits a fallback; optional lines losing a filter erase a difference; a failed disposition lacks one mandatory diagnostic, partial evidence supplies a verdict/replaces that diagnostic, or presentation-safe partial evidence has no defined ordinary row; Source `MissingMapping` is hidden or `NotEligible` is forced visible; structured output loses `recordKind`, disposition, typed producer/body-presence change, partial-evidence classification, or typed reason; evidence does not count toward the semantic row window; diagnostic records count against or are suppressed by that window; the integrity descriptor and section schema differ; a user projection removes any required integrity column when diagnostics exist or changes successful diagnostic-free projection; JSONL emits a second table/ad hoc object; or ordering is nondeterministic |
| Result and exit totality | Research + CLI text/Markdown/table/TSV/JSON/JSONL with public-construction attempts, duplicate member subjects across participants, complete/omitted explicit/enumerative selection correlations over admitted/endpoint-absent/terminal domains, empty-mechanism rejection, producer/session difference with and without visible lines, bodyless `Absent`, mixed exact/absent, all-absent direct results, hidden/windowed/projected target, participant, correspondence, mechanism, Finding acquisition/cross-validation, budget, query, cleanup, and direct-result failures plus other `Absent` controls | A planned result can be publicly constructed, copied, or enriched; its receipt loses a sealed question/endpoint-slot scope, participant domain, correlation manifest, expected scope, side outcome, or absence proof; an endpoint-absent domain loses proof or an ambiguous/failed participant domain is omitted, collapsed, or permits explicit no-match; equal subjects in distinct work items collapse; a producer/session difference lacks its required `BodyEvidenceResearchChange` or post-filter evidence row; a presentation row loses its work-item/participant context; a planned empty mechanism set completes; a failure disappears from retained ledgers/query outcome; completed-result `HasFailures` absorbs outer failure; selected Implementation Diff exits zero for a failure; `Absent` exits nonzero; all-absent has no `NotApplicable` arm; mixed exact/absent loses its absence reason; or ledger/query failure produces empty or contentless projected output |

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
- Side-local target requests have no pre-acquisition question/endpoint-slot
  set, sealed admitted/endpoint-absent/terminal participant-domain expansion
  (including participant-pairing ambiguity/failure), correlation
  manifest, or selection scope distinguishing omitted questions, endpoint
  failure, proven absence, and failed/incomplete selection or body-key census,
  so a missing scope or failed counterpart can become silence or a semantic
  one-sided change.
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
