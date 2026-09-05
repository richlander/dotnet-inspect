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

`CSharpBodyDiff.CompareMemberEndpoints` is the total endpoint-topology entry
point for rendered-line and semantic body comparison. Its caller supplies a
`CSharpMemberDiffEndpoint.Present` with an exact method definition or an
explicit `CSharpMemberDiffEndpoint.SubjectAbsent`; the adapter performs no
selector resolution or cross-version correspondence. Each present endpoint
becomes a `Complete`, `NoApplicableInput`, or `Failed` Finding inspection. The
resulting `CSharpMemberEndpointComparison` always retains both Finding subjects
and the exact `FindingComparison<CSharpCanonicalLine>`. It contains a
`CSharpBodyDiffResult` only for `Complete`/`Complete`, which is the only
topology that invokes the pair-dependent semantic body differ.

This path does not infer absence from a null source, handle, or body. RVA-zero
methods are present but `NoApplicableInput`; only the explicit endpoint arm is
`SubjectAbsent`. The producer gates are
`CompareMemberEndpoints_BodyfulPair_RetainsFindingAndNativeResults`,
`CompareMemberEndpoints_BodylessAndBodyful_UsesNoApplicableInputWithoutBodyDiff`,
`CompareMemberEndpoints_BodyfulAndSubjectAbsent_RetainsExplicitAbsenceWithoutBodyDiff`,
`CompareMemberEndpoints_SubjectAbsentAndBodyful_RetainsAddedCSharpFindingsWithoutBodyDiff`,
`CompareMemberEndpoints_BothSubjectAbsent_IsExactWithoutBodyDiff`,
`CompareMemberEndpoints_FailedInspection_RetainsFailureWithoutBodyDiff`, and
`PresentEndpoint_RejectsNullAndNilEvidence` in
`CSharpMemberEndpointComparisonTests`. The one-sided addition gate proves the
product value of the explicit absent arm: Decompiler emits every canonical C#
line from the present method as an added Finding while the pair-dependent
`CSharpBodyDiffResult` remains absent.

This adapter is the C# native-producer prerequisite consumed by the Research
producer session under
[#5441](https://github.com/richlander/dotnet-inspect/issues/5441) in the
user-approved focused decomposition tracked by
[#4706](https://github.com/richlander/dotnet-inspect/issues/4706). It is one
producer-owned adapter, not a shared substrate. Its endpoint sum type is the
simplest contract that preserves explicit presence and absence while preventing
the pair-dependent body algorithm from running outside `Complete`/`Complete`.
It adds no CLI, browser, presentation, or rendered-output surface, so host
enablement and rendering strategy are not applicable to this slice; later
Research and Queries consumers own those boundaries. The
`decompiler-dependencies` dependency-policy rule provides full project- and
assembly-graph coverage for the claim that this API remains below Research and
accepts no Research dependency.

The older assembly-wide and `CompareMembers` paths retain their current
missing-body compatibility behavior while existing consumers migrate. That
behavior is not emitted by `CompareMemberEndpoints`, where endpoint topology
owns the state.

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

Normal verbosity reports factual added, removed, changed, and moved line
counts from the producer-owned `AnalysisDiff<string>`. Changed and moved are
independent facets, so the same Before and After population can contribute to
both; unequal correspondence cardinalities remain explicit. Detailed
verbosity (`-v:d`) lowers that analysis to a complete Markout
`MappedTextDiff` through the host-neutral `DotnetInspector.Presentation`
adapter. Stable unchanged one-to-one correspondences become presentation
anchors; every other relation becomes conventional removal and addition text,
so movement identity is intentionally absent from the rendered patch while
remaining available to statistics. Both forms identify the PDB source location
and distinguish exact document-byte checksum agreement from agreement after
CR/LF normalization.
`TextFindingsTests` gates the complete source-line relation partition,
including unequal replacement populations, moved lines, line-ending
equivalence, and final-line terminators. `SourceTextDiffRendererTests` gates
factual summaries, overlapping changed/moved facets, and complete Markout
lowering.
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

**Status:** target design for #4771 with the Research target-resolution boundary
implemented through complete correspondence.
[Research admission and identity](#research-admission-and-identity) and
[Side-local requests and attempts](#side-local-requests-and-attempts), plus
complete census, correspondence keys and outcomes, positive absence proof, and
the retained producer-facing endpoint evidence under
[Complete census and correspondence](#complete-census-and-correspondence), are
implemented and verified by their named gates in
[Target-resolution migration and gates](#target-resolution-migration-and-gates)
and the owning sections. The native C# and IL producer adapters and their
inspection-topology classification are implemented and verified by their
owning sections. The Research local producer-session and completion boundary
is specified below but remains unimplemented.

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
[Complete census and correspondence](#complete-census-and-correspondence).
They are implemented downstream of terminal side-local attempts and exposed
through the complete `ResearchTargetResolution`; the named gates in that
section verify their construction and independent final validation.

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

**Status:** implemented and verified by the named gates under
[Target-resolution migration and gates](#target-resolution-migration-and-gates).
`ResearchTargetResolution` exposes complete terminal attempts, side-local
censuses, correspondence keys and outcomes, taint, and positive absence proofs.

After every attempt in a scope is terminal, Research performs one complete
census per domain and side. A domain-side census is complete when the admitted
question's exact input association accounts for every candidate occurrence. In
a non-ambiguous domain it proves either that the sole input attempt is terminal
or that no input occupies that domain and side. It is **healthy** only when
every required attempt is `Resolved` or `NotFound`. A domain is **blocked**
when either side contains an `Ambiguous`, `Rejected`, `Unavailable`, or
`Failed` attempt outcome, or when an admitted input is unevaluated because an
exact-address selection designated another input. That evidence may conceal a
target only inside its own domain because domain participates in every target
key. A blocked domain establishes no semantic pair, absence, addition, or
removal, but does not suppress a healthy domain in the same scope.

Only a healthy domain reaches key construction. Research derives typed
`ResearchStrictTargetKey` and `ResearchTargetCorrespondenceKey` values from
owner-issued target evidence:

- the strict key retains scope, domain, side-local admitted-input identity,
  relationship role, and the exact `MetadataMethodAddress` for a physical
  method. A non-method-like target instead retains its exact `MemberAnchor`
  with role `None`; and
- the correspondence key retains scope, domain, relationship role, and a
  Research-owned body identity projected from the exact Analysis-issued
  `MethodIdentity` for the resolved MethodDef. It erases side, admitted-input
  identity, assembly version, MVID, MethodDef token, and generic-parameter
  names. For role `None`, it retains the exact API `MemberAnchor` canonical
  identity because no body identity exists.

The Research body identity projects the structured physical declaring and
signature `TypeRef` shapes into Research-owned inert type identity. The
projection retains only the simple assembly name, exact metadata definition
name, structural element and argument shapes, generic kind and position, and
array rank; it does not retain Analysis resolution provenance or generic
parameter display names. The body identity also preserves the selected
declaration name and open parameter shape normalized for its accessor role,
generic arity, conversion return shape, and the Analysis-issued extension
projection. Analysis generic parameters participate by kind and position, and
exact metadata definition names preserve namespace
and nested-type segments separately. Distinct assembly domains, overload
shapes, relationship roles, extension bodies, and nested types therefore
remain distinct even when
a display name matches.

The key grammar and constructors are Research-owned. Metadata does not group
targets into Research correspondence domains, and callers do not author or
parse either key. Rendered assembly identities, list position, normalized
display text, selector strings, and `ResearchSubjectKey.Id` are not
correspondence keys.

If Metadata selection succeeds but the admitted Analysis index has no complete
structured `MethodIdentity` for that MethodDef, the attempt remains `Resolved`.
Research emits `CounterpartUnavailable` with `BodyIdentityUnavailable` taint
and no correspondence key rather than converting selection success into a
failure or comparing a lossy textual fallback.

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
domain-local blocking-attempt set and any exact unevaluated input
dispositions. If no target resolved, one
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
The shared Finding topology and both native producer adapters are implemented
and verified by their owning documents and named gates. The Research session
that will compose those typed results remains unimplemented and unverified.

### Resolution result and failure boundary

**Status:** implemented by `ResearchTargetResolver.Resolve`,
`ResearchTargetCorrespondenceBuilder`, and the construction boundary,
`ResearchTargetResolutionValidator`. The resolver publishes the complete
scope, domain, request, attempt, census, correspondence-key, absence-proof,
taint, and outcome result described in
[Complete census and correspondence](#complete-census-and-correspondence).
`ResearchTargetResolution_RetainsNoBorrowedResourcesOrPresentation`,
`ResearchTargetCancellation_ExposesNoPartialPopulationOrResult`, and
`ResearchTargetCancellation_RetryPreservesAdmissionAndMintsFreshTargets` are
the result-lifetime gates; the complete-census section names the correspondence
construction and validation gates.

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
   resolver and diagnostics remain unchanged. This step has landed.
3. Research adds complete domain-local census, correspondence, and absence
   proof. No producer is invoked and no inspection topology is classified.
   This step has landed.
4. The ResearchQueries companion consumes the admission API and constructs its
   Queries-owned receipt for both profiles. Body-signal target resolution
   remains on its compatibility path until Queries prerequisite #4777 supplies
   exact Metadata target evidence; Research does not compensate for that
   missing input.
5. The Findings topology and focused native-producer migrations have landed.
   Rank 4 under
   [#5441](https://github.com/richlander/dotnet-inspect/issues/5441)
   consumes complete correspondence outcomes to create work items. Its target
   design is specified below, but its Research implementation remains
   unimplemented. Producer adapters classify endpoint topology and retain their
   native typed results; Research adds no generic body disposition.
6. Rank 6 later migrates the implementation-comparison public path from string
   target identities and publishes the outer result. Body-signal migration
   follows #4777 independently.

The admission, scope, domain, request, and attempt gates have landed and are
listed under
[Research admission and identity](#research-admission-and-identity),
[Side-local requests and attempts](#side-local-requests-and-attempts), and
[Resolution result and failure boundary](#resolution-result-and-failure-boundary).
The census, key, absence, producer-handoff, and string-key contract is verified
by these named non-vacuity gates:

- `ResearchTargetKeys_AreOwnerIssuedAndNotDisplayDerived`
- `ResearchTargetKeys_EraseOnlyAddressAndSideLocalIdentity`
- `ResearchTargetKeys_PreserveDomainSignatureExtensionAndRelationshipRole`
- `ResearchTargetKeys_UseTupleErasedCanonicalTypes`
- `ResearchTargetKeys_UseTypedBodyIdentityForGenericAndNestedCollisions`
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
- direct-member comparison adapters, the separately specified
  [designated-pair admission](#research-designated-pair-admission), or
  ReturnToSender behavior;
- outer-result publication, CLI selection or presentation, row integrity, or
  exit behavior;
- Source, PDB, network, cache, retry, or authored-source acquisition; or
- a global stage catalog, shared attempt ledger, cross-component lifecycle, or
  end-to-end revival of the abandoned implementation-diff design.

## Research designated-pair admission

**Status:** implemented by `ResearchDesignatedPairAdmission` and the
designated-pair overload of `ResearchProducerSessionRequest`, tracked by
[#5877](https://github.com/richlander/dotnet-inspect/issues/5877).
The design was established by
[#5891](https://github.com/richlander/dotnet-inspect/pull/5891).
The named Release gates below cover the Research boundary; Queries and host
adoption remain separate deliveries.

`ILInspector.Research` owns one claim:

> An explicit designation associates exactly one resolved Before method and
> one resolved After method from the same admitted question with local
> producer work, without asserting that the methods correspond by identity.

The immediate consumer is the Queries
[direct-member adapter](direct-member-comparison.md). CLI `match`, round-trip,
authored rebuild, and browser comparison need to compare methods they already
selected, including differently named methods in different assemblies.
Metadata owns each selected target and physical address; Analysis owns module
and body evidence; Findings and the native C#/IL adapters own endpoint
classification and comparison. This section imports those contracts and
changes only Research's association and session work basis.

### Designation boundary

Admission consumes the exact implementation-comparison population, one
complete `ResearchTargetResolution` for that population, and two exact
`ResearchTargetAttempt` values from that resolution, explicitly ordered Before
then After. The population and resolution must satisfy the existing identity
closure. Both attempts belong to the same admitted operation and question,
and each request names its corresponding side-local implementation input.
Attempts from another resolution are not interchangeable even when their
addresses or labels agree.

Each endpoint must have a `Resolved` outcome with a non-`None` relationship
role and a physical `MetadataMethodAddress`. Its own domain-side census must
be healthy. These are the existing resolution facts, not a new lookup by
signature, token, label, or path. The two endpoints may have different scopes,
domains, declaring types, names, or relationship roles. Carried selections and
exact-address selections are both eligible after successful resolution; this
boundary does not select, forward, or retarget a member.

Only the selected domain-side censuses gate designation. An exact-address
selection requests its designated input and leaves other inputs
`NotRequested`; the opposite side of that scope may therefore be blocked.
That irrelevant opposite census does not block an independently resolved
endpoint on another scope. A blocked selected census, ambiguous selected
domain, failed attempt, or API-only target still prevents admission.
Unrelated domains and correspondence outcomes remain intact in the complete
resolution; designation neither filters them out nor reclassifies them.

No equal correspondence key or projectable structured body identity is
required. `SelectionDrift` and `BodyIdentityUnavailable` remain authoritative
for ordinary correspondence but do not invalidate otherwise eligible physical
designations. The ordinary correspondence builder and its `Paired`,
one-sided, absent, and unavailable outcomes remain unchanged. Designation is
an explicit request, never an automatic retry or fallback after unsuccessful
correspondence.

Two occurrences of the same physical method are valid. They still occupy
different Before/After admitted input identities; equal underlying content
does not collapse those occurrences. Requiring one question is deliberate:
the named consumer requests one pair, not a join across independent questions.

### Owner-issued association and non-success

Successful admission issues an inert `ResearchDesignatedPair` retaining the
exact resolution and both exact attempts in side order. It is the association
currency: its owner-issued reference identity distinguishes separate
admissions, while its retained operation, question, requests, admitted input
identities, roles, Metadata addresses, and module evidence identify what was
designated. Repeating admission may issue another pair over the same
predecessors; it does not replace or mutate them. No display string,
concatenated key, or fabricated correspondence stands in for this association.

An invalid association, such as foreign resolution, foreign attempt,
cross-question pairing, or reversed sides, returns typed rejection. An
associated endpoint that is unresolved, blocked, or lacks a physical method
returns typed unavailability retaining its side and original target/census
evidence or missing-address reason. If both endpoints are unavailable, both
causes are retained in Before/After order. Neither non-success issues a pair,
session, or work identity, nor invokes a producer. A missing designation is
not a one-sided comparison and cannot authorize `SubjectAbsent`.

Admission opens no content and adds no resource lifetime. The pair retains
only the existing inert resolution evidence, not the admitted population or
its borrowed descriptors, resolvers, or body indexes. A pair is admissible
input to a session, not evidence that later input access or native inspection
will succeed. The session still validates exact input access under
[its existing contract](#input-access-limits-and-cleanup).

For a workspace-selected pair, Queries consumes its own successful side-local
composition receipts before requesting this association. Research consumes
their exact Research attempts, not Queries receipt types. The existing
[two-sided handoff](research-workspace-target-composition.md#two-sided-comparison-handoff)
owns terminal selection and binding-policy validity; this admission grants no
permission to bypass either.

### Handoff to the local producer session

A session request chooses exactly one typed work basis: all correspondence
outcomes of its resolution, or one admitted `ResearchDesignatedPair` from that
exact resolution and population. There is no mixed roster or caller-supplied
work list. The existing correspondence request retains its meaning and
ordering; a designated request does not also schedule the resolution's
correspondence outcomes.

The designated work domain is that one pair crossed with requested local
producer kinds in catalog order. Every item retains the exact pair association
instead of a correspondence outcome, alongside its existing fresh session/work
identity and producer kind. Thus requesting C# and IL body yields exactly two
items even if the resolution contains other scopes or unavailable
correspondences. The completion validator derives the selected work basis,
order, endpoint binding, and native result kind; it does not merely check item
count or matching display text.

Both designated endpoints adapt as exact present physical methods, never
subject absence. Each native endpoint's subject key/identity is its own
Metadata anchor's `CanonicalSignature`, also used as its display/label.
Before and After subjects may differ. This is endpoint-local producer
attribution, not the pair's identity or a new correspondence key; role,
address, module, and occurrence association remain in the retained pair.
Native result validation checks each subject against its respective endpoint,
not against a shared invented member name. The native adapters already accept
separate subjects and perform no member correspondence.

The existing sequential coordinator, C#/IL catalog, input-stage validation and
reuse, native result arms, cleanup, cancellation, and atomic completion apply
unchanged. A bodyless MethodDef reaches the native adapter and may become
`NoApplicableInput`; Research does not reject it merely for lacking a body.
Access failure remains Research unavailability; native failure remains native
evidence. Cancellation after the final invocation still prevents completion.
This is an alternate work basis, not another executor, completion type,
producer topology, or cleanup protocol. Completion accounts for the requested
comparison, not API correspondence or runtime equivalence.

### Designated-pair demo and gates

Design mockup, not a new CLI spelling or runtime transcript:

```text
One admitted question
  Before occurrence: Alpha.dll, Left.Normalize(int), exact MethodDef
  After occurrence:  Beta.dll, Right.Clean(int), exact MethodDef
Two exact-address selections -> two successful side-local attempts
Explicit designation -> ResearchDesignatedPair(Before attempt, After attempt)
Local session, requested IL body + C#
  work 1: exact pair / C#      -> native C# endpoint comparison
  work 2: exact pair / IL body -> native IL endpoint comparison
```

Notice that the selected methods need not have the same identity and that the
session still uses catalog order. The neighboring same-method case retains
two occurrences; the bodyless case retains native inapplicability. Supplying
the same token from the wrong image must remain non-success, not another
method's comparison. An ordinary same-scope correspondence request with
divergent resolved keys must still report its existing `SelectionDrift`.

The following outcome gates run in `ResearchProducerSessionTests` in the
Release `ILInspector.Research.Tests` executable. The existing
`ResearchProducerCompletion_RetainsNoBorrowedResourcesOrPresentation` gate also
covers the pair, work-basis, and pair-admission result types.

| Gate | Required observation |
| --- | --- |
| `ResearchDesignatedPair_AdmitsExactSideLocalMethods` | Real compiled methods with different names, types, domains, and scopes admit; same-method occurrences remain distinct; exact-address opposite-side blocking is irrelevant. |
| `ResearchDesignatedPair_PreservesAssociationFailures` | A foreign-resolution attempt, cross-question or reversed pair, unresolved/API-only target, blocked selected census, and wrong-image address cannot issue a pair; both endpoint causes survive when applicable. |
| `ResearchDesignatedPair_DoesNotRequireCorrespondence` | Divergent roles/keys and missing structured body identity do not prevent otherwise valid physical designation; ordinary drift and absence results are unchanged. |
| `ResearchDesignatedSession_RetainsExactPairAndNativeResults` | C#/IL work has exact pair/catalog order and per-side native subjects; bodyless/native-failed neighbors preserve producer topology; unrelated correspondences cause no extra invocation or acquisition. |
| `ResearchDesignatedSession_PreservesAtomicTermination` | Reuse of the same pair mints fresh session/work identities; final-invocation cancellation and input/cleanup failure preserve existing terminal and exact-once cleanup behavior. |

Fixtures must use product-issued targets and native producer results, not
manufacture successful results or repair producer output. The same-image
neighbor and wrong-image fixture establish association behavior, not a new
mutable-file or local-actor threat model.

### Designated-pair basis and delivery

The baseline is explicit old/new endpoint comparison, already supported by
`ImplementationDiff.CompareMembers` and both native endpoint adapters.
Ordinary Research correspondence is useful analogous evidence precisely
because it refuses unequal keys: changing that refusal would answer the wrong
question. An owner-issued pair and a closed alternate work basis are the
simplest sufficient distinction. Admission is an immutable association check;
the sequential session and its lifetime protocol are unchanged. No new
stateful interaction calls for a TLA+ model.

This Research implementation is #5877, delivery step 18 of the counted
[#4706 adoption/retirement tracker](https://github.com/richlander/dotnet-inspect/issues/4706),
not completion of its Queries or host adoption. At design introduction the tracker had 18
remaining runtime deliveries: 9 on each local CLI/browser path, 13 on each
Source-enabled host path, and 7 on the comparison-tool path. Step 18 precedes
the Queries adapter (step 6), then CLI/browser/tools adopt at steps 8/9/10.
The tracker and the
[adapter ledger](direct-member-comparison.md#adoption-and-retirement-ledger)
own those paths and final legacy Queries/Research retirement at steps 16/17.
The tracker owns subsequent status/count changes.

Adding this substrate does not retire `ImplementationDiff.CompareMembers`,
its Source-dependent callers, or their projections. Queries population,
workspace composition, publication, host rendering, and Source adoption remain
separate work. This section adds no output schema: downstream CLI lowering
uses the planned shared Markout presentation path, and browser adoption
consumes typed evidence under its own host contract.

## Research local producer session and completion

**Status:** implemented by `ResearchProducerSession`,
`ResearchProducerSessionValidator`, and the Research-owned models in
`ResearchProducerSessionModels.cs`, tracked by
[#5820](https://github.com/richlander/dotnet-inspect/issues/5820).
`ResearchProducerSessionTests` contains the named implementation gates below.
The design was established by
[#5441](https://github.com/richlander/dotnet-inspect/issues/5441).
The ILDiff owner now supplies its focused typed-inspection adapter through
`IlAssemblyDiff.CompareMemberEndpoints`, gated by the owner-specific tests named
in [IL diff canonicalization boundary](il-diff-canonicalization.md). The C#
owner now supplies `CSharpBodyDiff.CompareMemberEndpoints`, gated by the
owner-specific tests named in
[Structural body comparison](#structural-body-comparison). Their shared
adoption contract remains
[Finding producer guidance](finding-producers.md#admit-body-topology-before-native-comparison).
The delivered prerequisites were tracked by
[#5443](https://github.com/richlander/dotnet-inspect/issues/5443) and
[#5444](https://github.com/richlander/dotnet-inspect/issues/5444).
The separately specified
[designated work basis](#research-designated-pair-admission) uses the same
coordinator and completion protocol; its additional gates are listed above.

This boundary is owned by `ILInspector.Research`. It turns one complete target
resolution into exact local C# and IL/body work, retains each producer's native
typed result, and publishes one inert Research completion. It does not make
target correspondence, endpoint topology, and comparison outcome into one
shared verdict.

### Imported contracts

The session consumes owner-issued contracts without redefining them:

| Owner | Imported contract |
| --- | --- |
| Research target resolution | One exact admitted operation, complete correspondence outcomes, resolved endpoints, positive absence proofs, and typed unavailability. |
| Findings | `Complete`, `SubjectAbsent`, `NoApplicableInput`, and `Failed` endpoint inspection topology and its transition. |
| `ILInspector.Decompiler` | One total C# endpoint adapter that returns its native typed result for every explicit endpoint combination, invoking its pair algorithm internally only for `Complete`/`Complete`. |
| `ILInspector.ILDiff` | One total IL endpoint adapter that returns its native typed result for every explicit endpoint combination, invoking its pair algorithm internally only for `Complete`/`Complete`. |

The Research session owns only catalog membership, requested producer
selection, work-item identity and accounting, invocation order, its borrowed
input access, its own limits and cleanup, and atomic completion. The imported
producer result remains authoritative even when its topology contains a failed
endpoint or its semantic and Finding projections disagree.

### Session request and catalog

One request carries the exact `ResearchAdmittedPopulation` and
`ResearchTargetResolution` for the same implementation-comparison operation,
and a non-empty set of requested local producer kinds. The resolution overload
uses all correspondence outcomes; the designated-pair overload chooses the
alternate work basis specified above. Research validates the
complete question, input, scope, domain, and correspondence identity closure
before it opens input content or invokes a producer. Equal-looking identities,
a foreign resolution, an unsupported profile, an empty producer selection, or
an unknown producer kind is a typed pre-execution rejection that exposes no
work-item identity or partial session.

The initial catalog is closed:

- **C#** binds the C# owner's typed endpoint adapter and native body
  comparison; and
- **IL body** binds the ILDiff owner's typed endpoint adapter and native body
  comparison.

Catalog membership is Research-owned and derives from one declaration. A
caller selects cataloged kinds but cannot supply a delegate, producer object,
service-located implementation, display name, or string identifier. Source,
body signals, API comparison, and ReturnToSender are not hidden catalog
entries. Adding one is a separate producer-adoption effort and changes the
declared catalog and its totality gates.

`ResearchProducerCatalog_AdmitsEveryDeclaredLocalKind` and
`ResearchProducerSession_RejectsForeignPopulationResolutionAndCatalogShapes`
are the implementation gates for this section.

### Exact work domain

For the correspondence work basis, Research derives one work item for every
pair in the Cartesian product of:

1. every correspondence outcome in the exact collection order carried by this
   target resolution; and
2. every requested kind in declared catalog order.

This product includes `CounterpartUnavailable` and `DomainUnavailable`.
Including them makes Research unavailability exactly accounted; those items
terminate without endpoint adaptation or producer invocation. Omitting an
item, adding an item, changing its correspondence, duplicating a kind, or
reordering by display text rejects completion.

Each work item has a fresh opaque Research identity parented by the admitted
operation and exact request. It retains its exact correspondence outcome and
declared producer kind. It is not identified by ordinal, member display,
selector text, `ResearchSubjectKey`, producer descriptor text, or a
concatenated key. Repeating the session request against the same admitted
population and target resolution mints fresh session and work-item identities
without changing those predecessor identities.

The correspondence collection's position has no identity meaning and need not
be stable across separately issued target resolutions; Research preserves only
the exact order in this input. Execution is strictly sequential in
correspondence order followed by catalog order. This is the normative
Browser/Wasm baseline and requires no threads.

`ResearchProducerWorkItems_DeriveExactOrderedCorrespondenceCatalogProduct`,
`ResearchProducerWorkItems_KeepUnavailableCorrespondenceWithoutInvocation`,
and `ResearchProducerWorkItemIdentities_AreFreshOwnerIssuedReferences` are the
implementation gates for this section.

### Endpoint adaptation and native results

For a healthy correspondence outcome, Research supplies the producer adapter
with only its exact retained endpoint evidence:

| Correspondence | Before evidence | After evidence |
| --- | --- | --- |
| `Paired` | resolved target | resolved target |
| `BeforeOnly` | resolved target | key-absence proof |
| `AfterOnly` | key-absence proof | resolved target |
| `Absent` | domain-absence proof | domain-absence proof |

Research supplies a positive absence proof through the producer's explicit
subject-absence input. It does not construct a Finding inspection or label a
resolved endpoint as `Complete`, `NoApplicableInput`, or `Failed`; the total
producer adapter owns endpoint classification and native result construction.
A null reader, handle, body, collection, or native result authorizes no state.
Input access or validation that prevents the adapter from receiving its exact
endpoint is retained as Research unavailability, not translated into subject
absence or no applicable input.

The body-producer subject for paired or one-sided correspondence is the exact
`ResearchTargetCorrespondenceKey.CanonicalIdentity` retained by that
correspondence. A both-absent domain uses its exact scope's declaring-type
intent and normalized Metadata selector. Both sides receive the same logical
subject. The subject is producer payload identity, not work-item identity.

A resolved API-only target whose relationship role is `None` has no
`MetadataMethodAddress` that either body-producer endpoint can admit. Research
retains that item as `EndpointAddressUnavailable` and invokes no producer. It
does not reinterpret the missing physical endpoint as producer-owned
`NoApplicableInput`; a bodyful/bodyless MethodDef pair still reaches both
native adapters, which classify the bodyless side themselves.

Research invokes only the producer's total endpoint adapter and never calls its
pair algorithm directly. The imported producer contract ensures that every
explicit endpoint combination returns a native result and that only
`Complete`/`Complete` reaches the pair algorithm. All other non-failed
combinations retain the native inspection transition without pair invocation;
a failed endpoint prevents pair invocation and remains visible in the native
result.

A cataloged work item returns one of three closed Research outcomes:

- **Produced** retains the exact producer-native result, including its endpoint
  topology, Finding comparison, semantic projection, and native failures; or
- **Research unavailable** retains the exact unavailable correspondence or
  Research-owned input-access failure and no producer result; or
- **Research failed** retains one bounded diagnostic for an escaped producer
  exception and no producer result.

These names describe orchestration, not semantic equality or producer success.
A `Produced` item may contain a native failed endpoint, non-exact comparison,
or cross-validation failure. Research does not translate those into a generic
body disposition, collapse C# and IL result shapes, manufacture
`PairFinding<T>` values, or reconstruct rows from producer messages.

One native failure or escaped producer exception is local to that work item and
does not suppress another item. A null result, wrong target, missing topology
transition, or result for another work item is a producer-contract violation
that fails the Research session rather than becoming a native failure-shaped
result.

`ResearchProducerAdapters_MapExactCorrespondenceEvidence`,
`ResearchProducerAdapters_ClassifyNoEndpointInsideResearch`,
`ResearchProducerSession_InvokesOnlyTotalNativeAdapters`,
`ResearchProducerResults_RetainExactNativeTopologyAndPayload`, and
`ResearchProducerException_IsLocalAndDoesNotSuppressIndependentWork` are the
implementation gates for this section. The first, third, and fourth require
owner-produced fixtures for paired bodyful bodies, one-sided subject absence,
paired bodyful/bodyless methods, both-absent targets, failed endpoint
inspection, escaped producer failure, and unavailable correspondence; mocks
that manufacture native results are insufficient. The C# and ILDiff adoption
issues own the gates proving pair-algorithm suppression.

### Input access, limits, and cleanup

The session may borrow admitted assembly descriptors, resolvers, and Analysis
body indexes only while it runs. Its closed owned-resource inventory contains
one Research-opened input stage for each admitted implementation input needed
by a healthy work item. An input stage owns the metadata/PE reader stack opened
from that exact descriptor and resolver; the borrowed descriptor, resolver,
and body index do not become owned resources. Research acquires no content
outside the admitted population.

Before an input stage can serve an item, Research revalidates its
assembly/module identity and body-index association against the exact admitted
input and resolved target evidence. A reopened or changed input that no longer
matches cannot serve a producer. Stage acquisition either transfers one
complete owned stage or releases its partial internals before returning typed
input-access failure.

The complete derived work roster is the Research execution budget: each work
item is attempted at most once, each required admitted input has at most one
live stage, and only one producer adapter invocation is active at a time.
Execution never truncates the roster, silently skips an item, or publishes a
prefix. Native producers retain their own decode, canonicalization, row,
scratch, and allocation limits; Research does not widen them or reinterpret
their bounded failure.

Research records one keyed cleanup outcome for every completely acquired input
stage. Cleanup is attempted after all work, after cancellation is observed,
and after a Research or producer-contract failure. Every stage is attempted
exactly once in reverse acquisition order; one cleanup failure does not prevent
attempts for the others. The record retains bounded Research diagnostics,
never raw exceptions or an unkeyed exception graph.

Any Research-owned cleanup failure prevents successful completion. Producer
results already materialized in memory are not published as a partial success.
The typed Research terminal outcome is the only handoff for this cleanup; an
adjacent consumer receives no authority to reopen a stage or reinterpret its
outcome.

`ResearchProducerSession_AccountsForTheExactWorkBudget`,
`ResearchProducerSession_UsesOnlyValidatedAdmittedInputAccess`,
`ResearchProducerSession_OwnsOnlyExactInputStages`,
`ResearchProducerCleanup_AccountsForEveryOwnedResourceExactlyOnce`, and
`ResearchProducerCleanup_FailurePreventsCompletionWithoutSuppressingCleanup`
are the implementation gates for this section.

### Atomic completion and cancellation

The session has four terminal arms:

- **Rejected** carries one typed pre-execution reason and no session or
  work-item identity;
- **Completed** carries one `ResearchProducerCompletion` after every work item
  is terminal and every owned input stage closed successfully;
- **Failed** carries one bounded Research validation, producer-contract, or
  cleanup failure plus the complete cleanup-outcome set, but no completion; or
- **Cancelled** carries the complete cleanup-outcome set, including any cleanup
  failure, but no completion or exposed work-item identity.

One successful `ResearchProducerCompletion` contains the exact admitted
operation and session identity, the complete ordered work-item roster, and
exactly one terminal outcome for every item. Its validator re-derives the
request, catalog selection, selected work product, parent identities,
endpoint-evidence binding, producer-result kind, result order, and cleanup
success before construction. Missing, duplicate, stale, foreign, or merely
name-equal evidence rejects publication.

Completion means Research finished and accounted for the requested work; it
does not mean every producer endpoint was complete or every native comparison
was exact. Consumers must inspect the retained native topology and result.

Every terminal arm is inert. A completion may retain opaque Research
identities, the exact inert target correspondence or designated-pair evidence,
Research input-access diagnostics, successful cleanup outcomes, and native producer
result values that their owners permit beyond execution. Failure and
cancellation may retain their bounded Research diagnostic and complete cleanup
outcomes. No arm retains an assembly descriptor, resolver, body index, metadata
or PE reader, stream, callback, producer, delegate, service provider, scratch
collection, lease, cleanup authority, raw exception, display-only row, or
mutable caller collection. Native typed display evidence already owned by a
producer result is native result data, not a Research-created presentation row.

Cancellation remains cancellation and is not a work-item outcome, failure, or
partial completion. The sequential coordinator observes it before resource
acquisition and between producer invocations. A synchronous native invocation
that has started may finish under its owner's contract; Research then observes
cancellation, performs complete cleanup, and returns the `Cancelled` arm.
Cleanup failure remains visible in that arm without changing cancellation into
completion or failure. Retrying mints fresh session and work-item identities.

`ResearchProducerCompletion_AccountsForEveryWorkItemExactlyOnce`,
`ResearchProducerCompletion_RejectsBrokenCrossLinksAndNativeKinds`,
`ResearchProducerCompletion_RetainsNoBorrowedResourcesOrPresentation`,
`ResearchProducerCancellation_ExposesNoPartialWorkOrCompletion`,
`ResearchProducerCancellation_RetainsEveryCleanupOutcome`, and
`ResearchProducerCancellation_RetryMintsFreshSessionAndWorkItems` are the
implementation gates for this section.

### Design basis and migration

This contract follows the repository's established finite-roster pattern:
owner-issued identity, complete deterministic work derivation, typed local
non-success, sequential execution, and publication only after final
validation. It deliberately diverges from a general task scheduler: the
catalog is closed, the baseline is single-threaded, and results are not streamed.
That smaller contract is sufficient for two local producers, preserves
Browser/Wasm operation, and prevents completion from racing cleanup.

The contract adds no mutable generation, concurrent publication, distributed
participant, or new close protocol. Its only state transition is a sequential
single-owner invocation that composes existing target, producer, and cleanup
outcomes before atomic publication. A TLA+ model would duplicate those owner
contracts without checking a new concurrency or lifecycle claim, so no model
is required for this design.

Implementation proceeds in focused owner order:

1. ILDiff and C# have adopted the shared Findings endpoint topology and exposed
   their typed adapters and native results under
   [#5444](https://github.com/richlander/dotnet-inspect/issues/5444) and
   [#5443](https://github.com/richlander/dotnet-inspect/issues/5443).
2. Research implements its catalog, exact work derivation, sequential session,
   input access, cleanup, and completion validator through
   `ResearchProducerSession`.
3. The Research completion becomes available to the separately owned rank-5
   direct-member and rank-6 publication efforts; this design specifies neither
   adoption.
4. Source remains a later dependent producer and body-signal target migration
   follows
   [#4777](https://github.com/richlander/dotnet-inspect/issues/4777)
   independently.

### Producer-session non-goals

This boundary does not define:

- Queries population sealing, correspondence receipts, acquisition planning,
  host authorization, outer-result publication, or cleanup for Queries-owned
  resources;
- Metadata selection, Analysis body identity, target correspondence,
  forwarding traversal, or workspace composition;
- Finding inspection states or transitions, C# or IL endpoint classification,
  native algorithms, result construction, internal limits, or failure
  semantics;
- API, body-signal, ReturnToSender, Source, PDB, network, cache, retry, or
  authored-source producers;
- the Queries direct-member adapter, string-key compatibility, CLI selection,
  presentation, row limits, exit behavior, or Markout changes;
- a caller-extensible plugin catalog, dynamic producer discovery, result
  streaming, concurrent or parallel work execution, shared cross-component
  budget, or global cleanup authority; or
- an end-to-end operation lifecycle or revival of the abandoned broad design.

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
