# Implementation Diff Boundary

> **Map:** [Type, member, and API representation](type-member-api-representation.md) is the entry
> point for choosing a type, member, or API identity shape. This document owns
> the details below.

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

### PDB-source convergence

`member -S "Source Diff"` is the PDB Source → After reviewer lens. It compares
the Portable-PDB-selected, checksum-matching declaration with the candidate
decompiled member as line-oriented text. It does not reuse structural
correspondence: PDB-mapped C# has no product-issued IL-origin node identity, so
this lens reports text convergence and never claims that source syntax nodes
correspond to decompiler nodes. Checksum agreement proves that the bytes match
the Portable PDB declaration, not that they were the physical syntax tree that
produced the MethodDef.

Normal verbosity renders standard unified hunks with three context lines,
retains at most five emitted hunk examples and 80 lines per logical hunk, and
reports `Partial` with the omitted counts when either bound is crossed. A
logical hunk split around omitted middle lines consumes two emitted examples;
the five-example budget still applies. Detailed
verbosity (`-v:d`) retains the complete line stream. Both forms identify the
PDB source location and distinguish exact document-byte checksum agreement
from agreement after CR/LF normalization.
`SourceTextDiffRendererTests.
ReviewerSizedDiff_OmitsDistantUnchangedLinesButRetainsEveryChange` and
`ReviewerSizedDiff_BoundsHunksAndLargeHunksWithVisibleDisclosure`, plus
`ReviewerSizedDiff_BoundsTheNumberOfHunkExamples` and
`ReviewerSizedDiff_BoundsEmittedFragmentsFromOversizedHunks`, gate the bounded
projection.
`CommandExecutionTests.
Member_SourceDiff_DetailedVerbosityPreservesCompleteLineEvidence` gates the
normal/detailed boundary, and
`Member_SourceDiff_UsesRequestedVerbosityBeforeSectionPromotion` gates the real
CLI path after explicit section selection promotes effective verbosity.
`Member_SelectedOverload_SelectSourceDiff_RendersPdbSourceVsDecompiledDiff`
gates visible PDB source identity and exact/normalized checksum evidence. The
acquisition side is gated by
`VerifiedLocalSourceReadTests.ReturnsBytes_WhenChecksumMatches`,
`VerifiedLocalSourceReadTests.ReturnsNull_WhenChecksumMismatches`, and
`PdbSourceAcquisitionTests.
FromContent_MismatchedChecksumProducesFailedInspection`, while
`FetchVerifiedSourceText_PreservesLineEndingNormalizationEvidence` gates the
network result's typed verification. A source context is
published only after one of the local, repository, or fetched paths accepts the
content against the portable-PDB checksum.

This lens does not infer validity, behavior, or compile-back fidelity from
text. Decompiler raise reviews place the independently measured compile-back
status beside it and keep the structural Before → After lens available when
PDB source is unavailable.

## Research admission and target-correspondence boundary

**Status:** target design for #4771.
[Research admission and identity](#research-admission-and-identity) and
[Side-local requests and attempts](#side-local-requests-and-attempts) are
implemented and verified. Complete census, correspondence keys, absence proof,
and producer handoff remain unimplemented and unverified until their named
gates in
[Target-resolution migration and gates](#target-resolution-migration-and-gates)
land.

This design proposes one place to answer the target question before comparison
work begins: which member, if any, did each side select; can those targets
safely correspond; and is a missing counterpart proven absent or merely
unavailable? Keeping that decision in Research preserves side and failure
evidence instead of flattening targets into strings and asking each producer to
rediscover cross-input correspondence.

`ILInspector.Research` and this document own that boundary. For implementation
comparison, Research turns one admitted population into side-local target
attempts and establishes correspondence from complete domain-local evidence.
The same admission contract supplies the Research identities required by both
rank-1 profiles.

The adjacent
[Queries-to-Research population boundary](inspection-layers.md#queries-to-research-population-boundary)
owns Queries population sealing and the correspondence receipt.
`ILInspector.Metadata` remains the owner of single-surface member selection,
`MemberAnchor`, exact metadata addresses, and target diagnostics. Research
imports those typed facts; it does not parse their display text, reconstruct
their identity, or change their failure meaning.

### Current target-resolution gap

The current CLI resolves one member selector independently against the old and
new `ApiSurface` values, then flattens stable selectors, canonical signatures,
and Research body aliases into one `HashSet<string>`.
`ImplementationComparisonInput`, `ImplementationDiffOptions`, and
`ResearchDiffOptions` pass that untyped set through to mechanism-specific
filters. This loses the selection occurrence, side, admitted input, resolution
attempt, typed diagnostic, accessor role, and proof that a one-sided target is
absent rather than unavailable.

The body paths then disagree about missing evidence. Semantic IL comparison
intersects method keys and can omit a one-sided member; retained IL Finding
comparison unions them and synthesizes absence. A method whose RVA is zero is
reported as a decode failure in one path, while another path translates
old-body-missing and new-body-missing failures into added and removed changes.
Properties and events are excluded from the body-identity bridge rather than
preserving the selected accessor.

Those body decisions are producer inspection topology, not cross-input target
correspondence. The shared
[Finding inspection topology](finding-nomenclature.md#typed-inspection-topology)
distinguishes a proven missing subject from an existing subject with no
applicable producer input. The target contract supplies exact endpoint or
absence evidence to that lower contract; it does not replace the current result
or producer models in this slice.

### Research admission and identity

**Status:** implemented by `ResearchComparisonAdmission.Admit`.
`ResearchAdmission_MintsFreshParentedIdentitiesForEveryOccurrence` and
`ResearchAdmission_ReturnsAtomicExactInputAssociations` are the named gates;
`ResearchAdmission_AdmitsEveryDeclaredProfile`,
`ResearchAdmission_RepeatedBorrowedValuesRetainDistinctOccurrences`,
`ResearchAdmission_CopiesCallerOwnedCollections`,
`ResearchAdmission_InvalidProfileInputExposesNoPartialPopulation`,
`ResearchAdmission_RejectsEveryDeclaredInvalidShape`,
`ResearchAdmissionIdentities_AreOwnerIssuedReferenceIdentities`,
`ResearchAdmission_NewAdmissionMintsFreshOperationAndPopulation`,
`ResearchAdmittedPopulation_RetainsOnlyImmutableState`,
`ResearchAdmissionRequests_SeparateConstructionAndAdmissionNullContracts`,
`ResearchAdmission_ImplementationOccurrenceValidatesEveryDirectArgument`, and
`ResearchAdmission_DoesNotOpenOrInspectBorrowedInputs` gate the remaining
properties below. Target scopes, domains, requests, and attempts have no
representation in this slice.

Research admission returns one immutable `ResearchAdmittedPopulation`. It owns
opaque identities for:

- one `ResearchComparisonOperationId`;
- each `ResearchComparisonQuestionId`, parented by that operation; and
- each side-local `ResearchComparisonInputId`, parented by one operation and
  question and carrying an explicit `Before` or `After` side.

These are sealed owner-issued reference identities with non-public
constructors. They are not strings, ordinals, MVIDs, assembly names, paths,
`ResearchSubjectKey` values, or conversions from Queries identities. Repeating
the same borrowed input value mints a distinct Research input identity for each
admitted occurrence.

The admission API returns each Research identity together with the exact input
occurrence for which it was issued in the same atomic result. This is the
Research-owned association required by the Queries companion to build its
receipt; the companion does not recover correspondence by ordinal, content,
display value, or structural equality. Admission either returns the complete
operation, question, and input population or exposes none of it.

Admission copies all caller-owned collections, and the admitted population
retains its occurrence-to-identity association as a frozen private copy keyed
by occurrence reference identity rather than a mutable dictionary. It may
borrow the exact profile-specific assembly descriptor, resolver, body index,
and typed selection intent while target resolution is active, but those values
are evidence rather than identity. Invalid profile input produces a typed
Research admission rejection that exposes no identity and no partial
population. No target request can be minted from this slice at all, because it
contains no target path.

Null contracts split by where the value enters. A direct occurrence, question,
or request constructor argument is validated at construction, so
`BodySignalComparisonInputOccurrence` rejects a null `LibraryBodyIndex` there,
`ImplementationComparisonInputOccurrence` rejects a null assembly descriptor,
resolver, or body index passed to its three-argument constructor, and neither
can report missing evidence later. Nested borrowed evidence supplied as an
already-constructed `ImplementationAssemblyInput`, and null collection
elements, are deliberately retained instead: an incomplete input or a null
occurrence becomes a typed admission rejection that exposes no identity and no
partial population, rather than a construction-time exception.

Admission borrows its inputs without reading them. It calls no member of
`ResolvedAssemblyReference`, `IAssemblyReferenceResolver`, or
`LibraryBodyIndex`, so it never opens an assembly, reads a path, resolves a
reference, or inspects body-index content.
`ResearchAdmission_DoesNotOpenOrInspectBorrowedInputs` gates this both
behaviorally, with borrowed capabilities that throw when used, and structurally,
with an IL call-reference walk over every admission-reachable product method for
both rank-1 profiles.

The identity and atomic-association contract applies to both rank-1 profiles.
The target-resolution path in this design initially applies only to the
implementation-comparison profile, whose admitted assembly content can supply
Metadata-owned target evidence. The body-signal profile admits only
`LibraryBodyIndex` today. Research does not open `LibraryBodyIndex.Path`,
manufacture an `ApiType`, or reimplement Metadata selection over Analysis
identity. Queries prerequisite #4777 must add exact typed Metadata target
evidence to that profile before body-signal target requests migrate from the
string-keyed compatibility path.

### Side-local requests and attempts

**Status:** implemented by `ResearchTargetResolver.Resolve` for the
implementation-comparison profile.
`ResearchTargetRequests_AreStrictlySideInputAndScopeLocal`,
`ResearchTargetAttempts_AccountForEveryRequestExactlyOnce`, and
`ResearchTargetScopes_DeriveBijectivelyFromSelectionOccurrences` are the named
gates;
`ResearchTargetRequests_CarriedRoleIsDerivedOnlyAfterResolution`,
`ResearchTargetAttempts_MapEveryMetadataDiagnosticKind`,
`ResearchTargetFinalValidation_RejectsBrokenSemanticBindings`,
`ResearchTargetMethodAddressBinding_RequiresAnInRangeMethodDef`,
`ResearchTargetResolution_PreservesMetadataDiagnosticsAndAccessorRoles`,
`ResearchTargetResolution_RejectsNonUniqueAccessorRoles`,
`ResearchTargetRejectedSelector_PreservesDiagnosticAndBlocksAbsence`,
`ResearchTargetDomains_EraseOnlyAssemblyVersion`,
`ResearchTargetDomains_RejectDuplicateSameSideCandidates`,
`ResearchTargetDomains_BlockOnlyTheirOwnCensus`,
`ResearchTargetAttempt_AddressEvidenceMismatchBlocksBeforeCensus`,
`ResearchTargetAbsence_FailedExtensionContainerBlocksProjectedMember`,
`ResearchTargetAbsence_UnscopedForwarderFailureBlocksOnlyAbsence`,
`ResearchTargetDeclaringType_DistinguishesAbsentFromForwarded`,
`ResearchTargetDeclaringType_DoesNotInferAbsenceUnderForwarder`,
`ResearchTargetDeclaringType_DoesNotInferAbsenceFromMalformedExport`,
`ResearchTargetDeclaringType_RejectsFailedExactDuplicate`,
`ResearchTargetForwarder_RetainedEvidencePrecedesUnscopedFailure`,
`ResearchTargetReferenceOnlyInput_TerminatesWithoutOpening`,
`ResearchTargetInputValidation_RejectsMismatchedModuleEvidence`,
`ResearchTargetResolution_StagesEachAdmittedInputOnce`,
`ResearchTargetPlanning_RejectsEveryDeclaredInvalidShape`,
`ResearchTargetIdentities_AreOwnerIssuedReferenceIdentities`,
`ResearchTargetResolution_RetainsNoBorrowedResourcesOrPresentation`,
`ResearchTargetCancellation_ExposesNoPartialPopulationOrResult`, and
`ResearchTargetCancellation_RetryPreservesAdmissionAndMintsFreshTargets` gate
the remaining properties below. The correspondence-key, absence-proof, and
census obligations this section mentions belong to
[Complete census and correspondence](#complete-census-and-correspondence) and
remain unimplemented; nothing in this slice constructs or consumes them.

Planning input is caller-authored and Research-owned. One
`ResearchTargetPlanningRequest` carries the admitted population, an explicit
role assignment for every admitted input, and the immutable member-selection
occurrences. The role is `Implementation` or `ReferenceOnly` and lives on the
planning request, not on the legacy `ImplementationAssemblyInput`, because it is
a Research planning decision rather than acquisition evidence. A foreign
question or input reference, a duplicate selection-occurrence instance, a
missing, duplicated, foreign, or undeclared role, and a non-implementation
profile are typed rejections that expose no identity and no partial plan.

Each immutable member-selection occurrence within one admitted question mints
one `ResearchTargetScopeId`. The scope contains the exact admitted Before and
After input populations against which that one selection intent may be
resolved. A type filter or member selector is intent inside the scope, never
the scope identity.

Within that scope Research establishes one opaque `ResearchTargetDomainId` per
logical assembly comparison domain. Its owner-issued domain key retains the
Metadata-owned `AssemblyReferenceIdentity` with only `Version` erased and
compares those values through Metadata's exact equivalence semantics. Research
does not renormalize name, culture, or public-key-token fields itself. An
admitted `ResolvedAssemblyReference` identifies one assembly definition and
therefore one physical module; this boundary does not invent a second logical
module coordinate that acquisition does not supply. The key allows two
versions of one assembly to share a domain without letting a same-named
assembly with different signing identity or another scope correspond. Domain
evidence comes from admitted `ResolvedAssemblyReference` values, not formatted
assembly names or body-index paths.

The admitted question is the authority that asks its Before and After input
sets to correspond. Within that question, one domain key may contain at most
one admitted input occurrence per side. Multiple same-side candidates,
including candidates from different acquisition registrations or provenance,
produce a blocking `DomainAmbiguous` outcome for every affected request rather
than an arbitrary pairing. The outcome blocks only that domain; it cannot taint
another domain in the same scope. Acquisition registration and provenance
distinguish the strict admitted inputs during validation but are intentionally
not domain equality: Before and After versions normally come from distinct
acquisitions. The inert result retains the Research input identity and domain,
not those borrowed acquisition values.

Domain-side planning is total. Every admitted input of the scope's question
carries exactly one closed `Requested` or `NotRequested` disposition inside
exactly one domain, so a side with no admitted input is distinguishable from a
side whose input the scope deliberately left unevaluated. An ambiguous domain
retains the complete conflicting input-ID set. This planning evidence is inert:
it reads the acquisition-owned `AssemblyReferenceIdentity` of each admitted
descriptor and opens nothing.

Research then mints one `ResearchTargetRequestId` and one
`ResearchTargetAttemptId` for each required side-local input evaluation. The
request retains:

- its operation, question, scope, domain, side-local admitted-input identity,
  and side;
- the exact typed `MemberTargetSelector` and declaring-type intent;
- the pinned Metadata API-surface scope it evaluates: public and non-public
  members, Metadata-supported compiler-generated types and fields, Metadata's
  exclusion of synthesized methods, and no member-kind filter;
- whether the target is exact or carried from API selection to a physical body
  coordinate; and
- an optional asserted address and relationship-role intent for an
  exact-address request.

It retains no `ResearchAdmittedInput`, selection occurrence, acquisition
descriptor, reference resolver, or body index.

Side participates in request identity. A carried selector has no resolved
relationship role before Metadata selection and does not borrow one from the
opposite side. An exact address is evaluated only in its designated side-local
input. A carried selector is resolved only through the Metadata surface and
body participant admitted for that request. No request, selector, address, or
successful target fans across sides, inputs, questions, operations, or scopes.

The following correspondence obligation is target design for the later census
slice, not behavior this slice implements. When only one side resolves, that
target's derived role participates in its correspondence key. The opposite
key-local absence proof requires positive absence-safe evidence that covers
that exact target and role without assigning a role to a missing request. A
request that instead resolves another role or another body identity proves
presence of that different target, not absence of the requested counterpart.

Exactly one terminal attempt outcome exists for every request in a completed
resolution:

- `Resolved` retains the exact Metadata-issued `ResolvedMemberTarget`,
  `MemberAnchor`, durable method address when one exists, and relationship
  role. The role is `None`, `Method`, `Getter`, `Setter`, `Adder`, or `Remover`
  and is derived only after successful Metadata selection. `None` is valid only
  for a non-method-like member with no physical method relationship. An
  asserted exact-address role must match this derived role. A selected
  MethodDef must occupy exactly one physical relationship role; no matching
  role or multiple matching roles fail validation;
- `NotFound` retains the exact Metadata-owned missing-member diagnostic when
  the declaring type exists, the exact Metadata-owned `DigestNotFound`
  diagnostic when no candidate has the requested stable fingerprint, or one
  bounded Research-owned `DeclaringTypeAbsent` diagnostic when that type is
  absent from the admitted input;
- `Ambiguous` retains the exact Metadata-owned ambiguity diagnostic and
  candidate evidence;
- `Rejected` retains the exact Metadata-owned `ConflictingSelectors` or
  `OverloadOutOfRange` diagnostic for an invalid or unstable positional
  selection;
- `Unavailable` retains one bounded Research diagnostic when the admitted
  input cannot supply an implementation target, including a reference-only
  role or `DomainAmbiguous`; or
- `Failed` retains one bounded Research diagnostic for Research validation or
  resolution failure.

Resolution may borrow an implementation input only for the duration of the
call, and must evaluate all requests for that input from one staged read. Live
assembly and module identity must agree with the acquisition descriptor and
Analysis-issued module identity, including the descriptor-bound MVID when one
is present. Member selection remains Metadata-owned, uses its existing API
surface including its synthesized-method exclusions, and matches the exact
declaring-type metadata full name. A potentially covering Metadata inspection
failure, including an unscoped forwarder failure, prevents Research from
asserting absence but does not suppress otherwise established local or
forwarding evidence. Because Metadata projects local extension methods onto
their receiver types, an owner-scoped failed TypeDef may cover member absence
on another retained type even though it does not cover that type's declaration
absence. Retained TypeDefs, exact forwarders, and exact owner-scoped failed
TypeDefs participate in the declaration census; duplicate exact declarations
fail as ambiguous. An exact type forwarder, or an intent nested beneath a
retained root forwarder, makes the target unavailable rather than absent. A
durable address requires an in-range `MethodDefinition` handle of the validated
module.

`DeclaringTypeForwarded` is terminal only for this exact input-local attempt.
It does not decide whether a later workspace-composition step treats the
forwarder as the compared endpoint or follows it to a terminal implementation
participant already admitted, or explicitly authorized for supplemental
admission, by the workspace. That later composition owns the effective
endpoint choice and retains the Metadata-issued forwarding path as
supplementary query evidence; it is not implemented or verified by this slice.

`Resolved` is terminal only after Research validates that the selected target
and durable address belong to the same admitted assembly and module. A
mismatched MVID, MethodDef address, or borrowed input becomes `Failed` before
the census runs; it cannot invalidate an already-issued correspondence
outcome.

`NotFound` is an input-local fact, not yet semantic absence. An exception,
diagnostic message, candidate display string, or empty result never substitutes
for one of these typed arms. Expected resolution outcomes do not throw.
`DigestNotFound` is absence-safe only for the exact stable target because the
Metadata diagnostic's complete candidate set proves that no candidate has that
fingerprint. `OverloadOutOfRange` is not absence-safe because ordinal movement
can leave the same stable target at another position.

### Complete census and correspondence

**Status:** unimplemented and unverified. Its named gates are listed under
[Target-resolution migration and gates](#target-resolution-migration-and-gates).
`ResearchTargetResolution` exposes complete terminal attempts today; it exposes
no census, correspondence key, correspondence outcome, or absence proof.

After every attempt in a scope is terminal, Research performs one complete
census per domain and side. A domain-side census is complete when the admitted
question's exact input association accounts for every candidate occurrence. In
a non-ambiguous domain it proves either that the sole input attempt is terminal
or that no input occupies that domain and side. It is **healthy** only when
every required attempt is `Resolved` or `NotFound`. A domain is **blocked**
when either side contains an `Ambiguous`, `Rejected`, `Unavailable`, or
`Failed` attempt outcome. Those outcomes may conceal a target only inside their
own domain because domain participates in every target key. A blocked domain
establishes no semantic pair, absence, addition, or removal, but does not
suppress a healthy domain in the same scope.

Only a healthy domain reaches key construction. Research derives typed
`ResearchStrictTargetKey` and `ResearchTargetCorrespondenceKey` values from
owner-issued target evidence:

- the strict key retains scope, domain, side-local admitted-input identity,
  relationship role, and the exact `MetadataMethodAddress` for a physical
  method. A non-method-like target instead retains its exact `MemberAnchor`
  with role `None`; and
- the correspondence key retains scope, domain, relationship role, and the
  canonical body identity produced by `ResearchMemberIdentity` from the exact
  `ResolvedMemberTarget`. It erases side, admitted-input identity, assembly
  version, MVID, and MethodDef token. For role `None`, it retains the exact API
  `MemberAnchor` canonical identity because no body identity exists.

The Research body identity preserves physical declaring type, member name,
generic arity, open parameter types, conversion return shape, and projected
extension body target. Nested-type spelling flows through the existing
API-to-body bridge. Distinct assembly domains, overload shapes, relationship
roles, extension bodies, and nested types therefore remain distinct even when
a display name matches.

The key grammar and constructors are Research-owned. Metadata does not group
targets into Research correspondence domains, and callers do not author or
parse either key. Rendered assembly identities, list position, normalized
display text, selector strings, and `ResearchSubjectKey.Id` are not
correspondence keys.

Correspondence is scope-local and has these closed outcomes:

- `Paired` names exactly one Before and one After strict target with the same
  correspondence key and relationship role;
- `BeforeOnly` names one Before target plus a complete After-side
  `ResearchTargetKeyAbsenceProof` for that key and role;
- `AfterOnly` names one After target plus a complete Before-side key absence
  proof;
- `Absent` retains one complete `ResearchTargetDomainAbsenceProof` for each
  side when neither side resolves any target in that domain;
- `CounterpartUnavailable` retains one otherwise-resolved target plus complete
  `ResearchTargetTaintEvidence`; or
- `DomainUnavailable` retains every blocking attempt when a blocked domain has
  no resolved target on either side.

A `ResearchTargetKeyAbsenceProof` is keyed by scope, side, correspondence key,
and relationship role. It is minted only from a complete healthy domain-side
census plus positive absence-safe evidence that covers the exact opposite
target and role. That evidence is either the question's exact association
proving no admitted input occupies that domain and side, or a `NotFound`
attempt whose typed selector and declaring-type scope cover the opposite
target. Merely having no resolved target with that key is insufficient. A
resolved target with another key or relationship role cannot prove absence. A
`ResearchTargetDomainAbsenceProof` is minted only when a complete healthy
domain-side census has no admitted input or its complete attempt set is
`NotFound`.

A missing type or member in one input is insufficient while that domain-side
input remains unevaluated. A Metadata-level ambiguous or rejected selector,
reference-only input, failed resolution, or incomplete attempt blocks both
proof kinds in its domain. Cancellation aborts the whole invocation before any
census or resolution result is exposed.

Blocked-domain handling has precedence over key construction in that domain.
When a domain is blocked, no `Paired`, `BeforeOnly`, `AfterOnly`, or `Absent`
outcome forms there. Every resolved target in that domain becomes exactly one
`CounterpartUnavailable` outcome whose taint evidence retains the complete
domain-local blocking-attempt set. If no target resolved, one
`DomainUnavailable` outcome keeps the failure visible instead of producing an
empty correspondence-outcome set. Other domains proceed from their own census.

In a healthy domain, two resolved targets with different correspondence keys
or roles do not become `BeforeOnly` and `AfterOnly`. Each becomes exactly one
`CounterpartUnavailable` outcome with bounded Research-owned `SelectionDrift`
taint that retains both attempts and strict keys. The same outcome is used for
a resolved target whose opposite `NotFound` attempt does not positively cover
that exact target and role; its taint retains the resolved attempt and strict
key plus the opposite attempt and typed diagnostic. Research does not guess
signature or accessor-role correspondence from similar names, position, or
display text.

### Producer handoff and inspection topology

Research target resolution ends with correspondence. It does not classify
whether a resolved target has input applicable to C#, IL, Source, or another
producer.

The complete healthy correspondence outcomes retain the evidence a later
producer-specific adapter needs:

| Correspondence outcome | Retained endpoint evidence |
| --- | --- |
| `Paired` | exact Before and After resolved targets |
| `BeforeOnly` | exact Before target and After key-absence proof |
| `AfterOnly` | Before key-absence proof and exact After target |
| `Absent` | exact selection intent and both domain-absence proofs |
| `CounterpartUnavailable` or `DomainUnavailable` | no completed endpoint set; typed Research unavailability remains visible |

These are correspondence results, not work items, producer eligibility, or
comparison verdicts. `ResearchTargetResolution` does not contain generic
`Bodyful`, `Bodyless`, `NotMethodLike`, `BodyAdded`, `BodyRemoved`, `NoBody`,
`TargetAbsent`, or `ProducerEligible` states.

A later producer-specific adapter consumes the exact target and absence
evidence under
[Finding producer guidance](finding-producers.md#admit-body-topology-before-native-comparison).
That adapter owns whether each endpoint is `Complete`, `SubjectAbsent`,
`NoApplicableInput`, or `Failed` for its operation. Every non-failed endpoint
combination is a completed inspection-topology result; only
`Complete`/`Complete` permits a native pair algorithm to run. Research may
retain and compose the typed producer result later, but it does not reinterpret
the transition as a Research-owned body verdict.

`CounterpartUnavailable` and `DomainUnavailable` never manufacture a completed
producer endpoint set. The later session keeps the Research unavailability
visible and does not turn it into absence, inapplicability, or producer failure.
The shared Finding topology is implemented and verified by its owning
document. The adjacent producer obligations remain unimplemented and
unverified; each native producer migration supplies its own gates.

### Resolution result and failure boundary

**Status:** the scope, domain, request, and attempt half is implemented by
`ResearchTargetResolver.Resolve` and its construction boundary,
`ResearchTargetResolutionValidator`.
`ResearchTargetResolution_RetainsNoBorrowedResourcesOrPresentation`,
`ResearchTargetCancellation_ExposesNoPartialPopulationOrResult`, and
`ResearchTargetCancellation_RetryPreservesAdmissionAndMintsFreshTargets` are
the named gates. The correspondence-outcome half of this section is
unimplemented; `ResearchTargetResolution` exposes no correspondence outcome,
key, or absence proof today.

One `ResearchTargetResolution` accounts for the complete admitted operation. It
contains immutable operation, question, scope, domain, input, request, and
attempt identities; every terminal attempt outcome; every correspondence
outcome; and the exact endpoint or absence evidence retained by that outcome.
Its expected identity and result domains are derived from the admitted
population and target scopes, so both missing and stale entries reject
construction. That construction boundary re-derives the expected scope, domain,
request, and attempt sets from the planning request rather than trusting the
built result's nullable field shape, and re-runs parent identity, exact-once
accounting, module, address, token, relationship-role, diagnostic-kind, and
candidate binding.

Every request has exactly one attempt and every attempt has exactly one
terminal outcome. Every resolved attempt appears in exactly one domain-local
correspondence outcome. Repeated or distinct selection occurrences that resolve
to the same physical target retain distinct scope, request, and attempt
identities. An implementation may share immutable payload storage, but that
sharing is unobservable and cannot merge correspondence domains or erase an
occurrence.

The result is inert. It may retain opaque Research identities, side,
relationship role, exact owner-issued Metadata target and diagnostic values,
durable metadata addresses, typed keys, absence proofs, and Research
diagnostics. It retains no metadata reader, PE reader, stream, assembly group,
workspace callback, producer, scratch state, lease, cleanup authority, raw
exception, display row, or rendered diagnostic text.

Cancellation remains cancellation and is not an attempt outcome. If it is
observed after internal identities or partial attempt evidence have been
created, the invocation exposes none of them and returns no
`ResearchTargetResolution`. Retrying the same admitted population preserves
its already-exposed operation, question, and input identities while minting
fresh scope, domain, request, and attempt identities. Only a new admission
mints a fresh operation. There is no resource cleanup or competing
terminal-primary policy in this boundary because all readers, snapshots, and
body indexes are borrowed for the resolution call and remain owned by their
admitting component.

### Target-resolution migration and gates

Migration preserves owner and dependency direction:

1. Research adds the opaque admission identities, atomic
   `ResearchAdmittedPopulation`, and purpose-built fixtures for both rank-1
   profiles. It adds side-local target scopes, requests, and attempts for the
   implementation-comparison profile. This step has landed.
2. Research adds that profile's Metadata-target adapter, exact relationship
   roles, durable target keys, and typed expected-failure outcomes. Metadata's
   resolver and diagnostics remain unchanged. The adapter, relationship roles,
   and typed expected-failure outcomes have landed; the durable target keys
   have not.
3. Research adds complete domain-local census, correspondence, and absence
   proof. No producer is invoked and no inspection topology is classified.
4. The ResearchQueries companion consumes the admission API and constructs its
   Queries-owned receipt for both profiles. Body-signal target resolution
   remains on its compatibility path until Queries prerequisite #4777 supplies
   exact Metadata target evidence; Research does not compensate for that
   missing input.
5. After the Findings topology and focused native-producer migrations land,
   rank 4 consumes complete correspondence outcomes to create work items.
   Producer adapters classify endpoint topology and retain their native typed
   results; Research adds no generic body disposition.
6. Rank 6 later migrates the implementation-comparison public path from string
   target identities and publishes the outer result. Body-signal migration
   follows #4777 independently.

The admission, scope, domain, request, and attempt gates have landed and are
listed under
[Research admission and identity](#research-admission-and-identity),
[Side-local requests and attempts](#side-local-requests-and-attempts), and
[Resolution result and failure boundary](#resolution-result-and-failure-boundary).
The census, key, absence, producer-handoff, and string-key contract remains
unimplemented until these named non-vacuity gates land:

- `ResearchTargetKeys_AreOwnerIssuedAndNotDisplayDerived`
- `ResearchTargetKeys_EraseOnlyAddressAndSideLocalIdentity`
- `ResearchTargetKeys_PreserveDomainSignatureExtensionAndRelationshipRole`
- `ResearchTargetCensus_DerivesCompleteAttemptAndCorrespondenceDomains`
- `ResearchTargetCensus_BlockedDomainTaintsResolvedTargetsOnBothSides`
- `ResearchTargetCensus_BlockedDomainWithoutResolvedTargetsIsVisible`
- `ResearchTargetCensus_BlockedDomainPrecedesKeyConstruction`
- `ResearchTargetCensus_DivergentResolvedKeysAreUnavailable`
- `ResearchTargetKeyAbsence_RequiresCompleteHealthyKeyLocalCensus`
- `ResearchTargetKeyAbsence_RequiresPositiveSelectorCoverage`
- `ResearchTargetDomainAbsence_RequiresCompleteHealthyEmptySide`
- `ResearchTargetFailure_NeverBecomesAbsenceEvidence`
- `ResearchProducerHandoff_CompleteOutcomesRetainExactEndpointOrAbsenceEvidence`
- `ResearchProducerHandoff_BlockedOutcomesExposeNoCompletedEndpointSet`
- `ResearchProducerHandoff_DoesNotClassifyInspectionTopology`
- `ResearchImplementationTargetPath_HasNoStringKeyedIdentityBag`

The expected admission, scope, domain, request, attempt, and correspondence sets
must be derived from their declarations. The totality gates fail for both
missing and extra entries. The scope gate derives its expected set from the
immutable member-selection occurrences and proves an exact bijection even when
a scope has no admitted input on either side. The Metadata-diagnostic gate
derives its expected set from `MemberTargetDiagnosticKind` and fixes the
mapping:
`MissingMember`/`DigestNotFound` to `NotFound`,
`AmbiguousMember`/`DigestAmbiguous` to `Ambiguous`, and
`ConflictingSelectors`/`OverloadOutOfRange` to `Rejected`. It exercises every
one of those kinds against a real compiler-produced image rather than proving
the mapping table alone.

The key gates pair one body across changed MVID/token/version evidence and keep
distinct assembly domains, overloads, accessor roles, projected extension
bodies, and nested types separate. Census fixtures put a blocked domain beside
a healthy domain and pair positional-selector drift across overload and
accessor-role changes. They prove that local failure does not suppress the
healthy domain and that an unevaluated target never becomes absence evidence.

The producer-handoff gates derive the expected endpoint-evidence shape from the
correspondence declarations. Purpose-built fixtures prove that paired,
one-sided, and both-absent outcomes retain their exact resolved targets,
selection intent, and positive absence proofs; blocked outcomes expose no
completed endpoint set; and no Research signature introduces a Finding
inspection state or generic body disposition. A cross-module address fixture
must become a blocking attempt before any key or correspondence is exposed. The
string-key gate inspects the new target-path public and internal signatures
rather than only exercising a successful fixture; explicit compatibility
adapters remain until the dependent migration removes them. Purpose-built
profile fixtures also prove that a body-signal admission cannot enter the
target path without the typed prerequisite from
[#4777](https://github.com/richlander/dotnet-inspect/issues/4777).

### Target-resolution non-goals

This boundary does not define:

- Queries population identity, sealing, projection receipt, or public result
  shape;
- Metadata selector parsing, single-surface resolution, anchor/address
  construction, or diagnostic semantics;
- the Queries-owned body-signal target-evidence prerequisite tracked by #4777;
- package acquisition, role planning, workspace composition, resource cleanup,
  or budgets;
- Finding inspection-state definition or producer-specific applicability and
  body-topology classification;
- work-item construction, producer cataloging or execution, native C#/IL
  comparison, scratch state, or Research completion;
- direct-member comparison adapters, designated arbitrary method pairing, or
  ReturnToSender behavior;
- outer-result publication, CLI selection or presentation, row integrity, or
  exit behavior;
- Source, PDB, network, cache, retry, or authored-source acquisition; or
- a global stage catalog, shared attempt ledger, cross-component lifecycle, or
  end-to-end revival of the abandoned implementation-diff design.

## Research comparison model

`ResearchDiff` is the operation facade. It returns one `ResearchComparison`
containing a flat `Changes` collection. `BySubject()` computes member- and
type-centric groups from that collection; grouped and flat consumers therefore
cannot observe divergent copies of the same result.

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
text.

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

Use `ImplementationDiff.CompareAssemblies` or `ImplementationDiff.Compare` when
the input is a pair of assemblies or `ResearchDiffInput` values. The result is a
list of changed implementation members. Each member can carry C# changes, IL
changes, or both; exact members are omitted.

Use `ImplementationDiff.CompareMembers` when the caller already resolved exact
old/new `MethodDefinitionHandle` values in live `MetadataSource` instances. The
member result keeps the typed C# diff, typed IL diff, joined implementation
changes, and a single `ResearchSubjectKey`; exact members return an empty
change list with `IsExact` set.

Use `CompareMembersWithPdbSource` when the caller also has old/new
`FindingInspection<string>` envelopes from Services. Use
`WithPdbSourceComparisons` to enrich an assembly comparison. These APIs
preserve `Complete`, `Absent`, and `Failed` independently and retain the native
line comparison. Research does not fetch source.

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
The section binds `ImplementationComparisonQuery`, whose input carries retained
assembly descriptors, reference resolvers, and body indexes. The query opens
those descriptors for the offline C# and IL producers and returns
`ImplementationDiffResult`; the CLI adapter's current path-backed descriptors
are an acquisition boundary, not part of the query contract.
With `--pdb-source`, it acquires each changed implementation member's
endpoint PDB and
SourceLink body, verifies the document checksum, and adds a separately labeled
`PDB Source` lane. Missing mappings and acquisition failures remain visible rather
than falling back to decompiled C#.
The authored A→IL lane reuses the final RTS shell/request but compiles with
portable-PDB-recorded options when available; the decompiled B→IL lane uses the
RTS compile context. `BuildContext` and determinism verdicts therefore remain
part of interpreting any Exact/IlDifferent disagreement.
Package, platform, and local-library ranges use the same acquisition path as the
default API diff; `--type`, `--member`, row limits, table, TSV, and JSONL
projection continue to apply. The CLI consumes this product component and does
not invoke or reconstruct the C# and IL producers independently.

## Non-goals

- It does not prove semantic equivalence; IL/body rows are evidence, not a
  verifier.
- It does not own API compatibility rows. Metadata owns API observations,
  matching, and compatibility classification; Research retains and projects
  that comparison separately from `ImplementationDiff`.
- It does not compile source artifacts or plan closure. ReturnToSender and other
  harnesses own artifact requests and compilation.
