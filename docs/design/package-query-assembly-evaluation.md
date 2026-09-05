# Package Query assembly-pattern evaluation

## Status and owner

This document is the focused owner for evaluating one already-acquired package
candidate against one product-issued assembly pattern. It is tracked by
[#5785](https://github.com/richlander/dotnet-inspect/issues/5785) under the
end-to-end Package Query tracker
[#5766](https://github.com/richlander/dotnet-inspect/issues/5766).

The owning implementation belongs in the package-aware L1 companion,
`DotnetInspector.PackageQueries`. The evaluator consumes package selection and
package-neutral Metadata or Analysis results. It does not choose a renderer,
parse a host gesture, acquire a package, schedule a corpus, or expose an
inspection reader.

The contract and all target gates below are design-only and unverified until
their named Release gates land.

## Claim

Given one exact acquisition-issued `PackageRootBinding`, one separate
resource-free product-issued assembly-pattern request, and explicit
selected-entry, retained-image, producer-working-set, semantic-work, and
deadline bounds, the evaluator:

1. maps the frozen package selection to a typed non-applicable or failure
   outcome, or resolves exactly one canonical selected asset for the pattern's
   declared role;
2. asks the package adapter to attempt projection of only that selected package
   entry and, on its `Available` arm, transfer one neutral artifact-backed
   participant and group;
3. enters the Metadata owner's artifact query validation before any rejecting
   prefilter or semantic producer;
4. may reject the validated image through an owner-proved conservative byte
   prefilter;
5. otherwise obtains the semantic producer's typed verdict;
6. returns a resource-free match, non-match, non-applicable outcome, or visible
   item failure with the Artifact Acquisition owner's exact package Root
   reacquisition request; and
7. closes the candidate workspace, artifact generation, image, metadata or
   analysis session, and borrowed capability before returning or propagating
   cancellation or an unexpected exception, and never returns success when
   close reports incomplete cleanup.

The result is scoped to the exact package plus selected asset. It preserves the
retained-content generation, frozen selection, pattern, and producer evidence
that participated in the verdict. Display text is never used to reconstruct
any of those joins or the later Workspace opening request.

## Why this is a separate owner

The current Package Query tiers answer questions from source metadata, exact
manifests, and package archive paths. Promoted assembly evaluation adds a new
boundary: package-aware selection must feed one package-neutral semantic
producer without opening every assembly or turning a transient reader into a
query result.

The existing full package-role realization is deliberately not the default
mechanism. It realizes all selected role assets so a workspace can answer
cross-assembly questions. A sparse corpus query usually needs one primary
assembly and one predicate. Realizing every compile and implementation asset
would decompress, classify, and retain unrelated images before a candidate can
be rejected.

This owner therefore composes one selected package artifact with one semantic
producer. It depends on a sparse package-adapter projection that admits only
the chosen asset into a candidate-scoped workspace. The projection contract
was locked by
[#5798](https://github.com/richlander/dotnet-inspect/issues/5798) and
[#5807](https://github.com/richlander/dotnet-inspect/pull/5807), remains owned
by
[Artifact acquisition and workspace composition](artifact-acquisition-and-workspaces.md),
and is not yet implemented.
`PackageWorkspaceIntegrationsQuery` is the existing all-role package-grain
precedent: it evaluates implementation assets in role order and then unmatched
surface assets. This evaluator instead has **package + selected asset** grain.
Full role realization remains the right path when the question requires a
complete role, dependency binding, or cross-assembly relationship.

### Implementation prerequisites

This design can lock before its dependencies implement, but evaluator
execution cannot land until all of these owner contracts are available:

- the #5798 sparse selected-assembly projection and its Release gates;
- the Artifact Acquisition-owned pre-transfer cleanup receipt tracked by
  [#5842](https://github.com/richlander/dotnet-inspect/issues/5842), completing
  the sparse projection's promised secondary-cleanup boundary;
- the Metadata-owned admission and query-validation seam designed by
  [#5143](https://github.com/richlander/dotnet-inspect/issues/5143) and tracked
  for implementation by
  [#4857](https://github.com/richlander/dotnet-inspect/issues/4857), including
  the named gates that reject both Windows Metadata kinds before producer
  execution;
- the sparse projection's composition with that Metadata admission result,
  tracked by
  [#5843](https://github.com/richlander/dotnet-inspect/issues/5843), so a
  compatibility rejection carrier cannot substitute for an
  `ArtifactAssemblyProjection`; and
- the Artifact Acquisition-owned exact package Root reacquisition request
  tracked by
  [#5837](https://github.com/richlander/dotnet-inspect/issues/5837).

The current compatibility snapshot path does not invoke
`MetadataImageFormatClassifier`, and `RealizedMemberCoordinate.Package`
preserves the acquisition framework rather than every selection target.
Neither current path is a substitute for these prerequisites. The dependent
format-admission, exact-opening, and pre-transfer-cleanup claims below remain
explicitly **unverified** until their named owner gates land.

## Consumers and host plan

The named consumers are:

- the CLI Package Query surface described by
  [The package query CLI](package-query-cli.md); and
- the Inspect Web Browser/Wasm Package Query surface described by
  [The package query experience](package-query-experience.md).

Both hosts consume the same request and outcome meanings. A host owns the
explicit cost gesture, operation deadline, cancellation source, and
presentation. The evaluator contains no CLI, Markout, DOM, JavaScript, worker,
or callback types.

The later streaming pipeline places this evaluator inside the shared
[Engine-to-browser async event stream](engine-browser-async-event-stream.md).
That composition will own candidate scheduling, progress, item-failure
publication, completion accounting, and bounded concurrency. This document
does not pre-empt those decisions.

The end-to-end tracker #5766 carries the production-host adoption path. Its 13
steps are enumerated under [Delivery sequence](#delivery-sequence), ending in
the separate CLI and Browser gestures and exact result-opening adoptions.

## Adjacent owners

| Owner | Supplies to this evaluator | Remains outside this contract |
| --- | --- | --- |
| [Package adapter](artifact-acquisition-and-workspaces.md#sparse-selected-assembly-projection) | Exact `PackageRootBinding`, frozen compile and implementation asset selection, content-generation identity, selection identity, and the closed sparse-projection outcome. Its `Available` arm transfers one canonical asset, one-participant group, and exact participant; #5843 supplies the closed Metadata admission variant, and #5842 supplies a resource-free owner cleanup receipt only for incomplete pre-transfer cleanup | Source authorization, download, archive admission, cache policy, TFM/RID reduction, asset ordering, package provenance, artifact materialization, and pre-transfer cleanup mechanics |
| [Artifact Root reacquisition](https://github.com/richlander/dotnet-inspect/issues/5837) | One owner-issued resource-free request preserving the binding's exact realized coordinate and selection target for later Workspace opening | Destination Workspace mutation, source authorization, reacquired generation, and opening presentation |
| [NuGet package structure](../nuget-package-structure.md) | Compile and implementation role meanings | A new package-layout interpretation |
| [Assembly inspection query](assembly-inspection-query.md) | Package-neutral `ArtifactAssemblyQueryOutcome<TResult>`, managed-image format and identity validation, and callback-scoped Metadata query execution | Package identity, package selection, or corpus policy |
| [Inspection layers](inspection-layers.md) | Metadata ownership of metadata facts and Analysis ownership of IL-body evidence | Reclassifying one layer's evidence as another layer's fact |
| [Engine event streams](engine-browser-async-event-stream.md) | Durable item/failure and completion meanings for later composition | Pattern evaluation, candidate scheduling, or host rendering |
| [Progressive disclosure](progressive-disclosure.md) | Explicit capability and cost-gesture requirements | CLI or Browser gesture spelling |

## Request boundary

The public evaluation request carries two resource-free parts:

- one product-issued pattern identity plus its validated operand; and
- one admitted evaluation budget.

Execution receives one exact `PackageRootBinding` separately as its
package-owner-issued context. That context is the one intentional live route
to package-owned selection and retained-content authority; it is not part of
the public request or outcome object graph. The evaluator chooses an asset
from the binding's frozen selection, then asks the package adapter to validate
and project that exact asset. It does not dereference `IPackageContent` or
construct an artifact registration itself.

Before candidate resources are created, the evaluator also asks Artifact
Acquisition to issue #5837's exact package Root reacquisition request from that
same binding. The request is resource-free and contains no candidate Workspace
identity or physical-generation authority. It enters every completed outcome
as opaque opening intent; the evaluator does not reconstruct it from the
coordinate, selected asset, TFM, RID, or presentation evidence.

Planning resolves the opaque pattern identity to an internal static executable
binding. The public request accepts no package ID plus file-name pair, raw
package path, arbitrary delegate, executable binding, `PEReader`,
`MetadataReader`, open stream, or package-content handle.

### Pattern vocabulary

The product vocabulary is finite and statically registered. Each descriptor
states:

- stable opaque pattern identity;
- user-facing label and summary;
- operand shape and validation owner;
- required assembly role;
- semantic producer owner;
- whether a conservative byte prefilter exists;
- semantic work-budget kind;
- semantic working-set policy; and
- evidence shape returned on a match.

The request operand is one closed union:

- **None** for a fixed product characteristic;
- **Literal text** for an exact, ordinal, bounded text value; or
- **Typed API identity** for an owner-issued type or member selector.

The descriptor decides which operand arm is legal. A literal is inert data,
not a regular expression, glob, query fragment, or byte sequence. A typed API
identity is validated by its owning identity grammar before Package Query sees
it.

The first planned producer is the Analysis-owned exact ordinal substring over
decoded `ldstr` uses tracked by
[#5795](https://github.com/richlander/dotnet-inspect/issues/5795). Later
vocabulary may include metadata-text, API-identity, or fixed
IL-characteristic questions, but those names do not transfer semantic
ownership. Metadata defines metadata-name or API-declaration evidence.
Analysis defines method-body, instruction, call, allocation, and literal-use
evidence. Each producer adoption is a focused change that issues the binding
and typed verdict consumed here.

A host discovers descriptors and submits their opaque identities. It does not
recreate predicates or send executable expressions. A textual host grammar may
lower user input to a descriptor and validated operand, but that grammar is a
host or L2 concern.

Open regular expressions and arbitrary byte patterns are not part of the first
contract. They make semantic confirmation, work accounting, and cross-host
equivalence substantially less predictable than bounded product-issued
patterns.

### Pattern binding

The internal executable bindings are statically rooted and noncapturing. Only
Package Query orchestration can invoke them. Public discovery exposes
descriptors, not delegates, target methods, metadata handles, or analysis
contexts.

An optional prefilter binding receives a callback-scoped
`AssemblyImageView`. The semantic binding receives a callback-scoped
`AssemblyInspectionSession` and an admitted semantic budget. Orchestration
obtains both views inside one Metadata-owned
`ArtifactAssemblyQueryOutcome<TResult>.Validated` callback over the same
query-authorized retained image; neither binding may retain its view or
session. `NotAssembly` and `Rejected` outcomes are mapped before either
binding runs.

The semantic binding returns one closed producer-owned verdict:

- `Match`, with non-empty typed evidence;
- `NoMatch`;
- `Rejected`, with a producer-owned bounded-decode or unsupported-input
  failure; or
- `WorkLimitExceeded`.

The binding neither opens package content nor retains its input. It cannot
change asset selection, acquire dependencies, or publish host events.

The evaluator maps the semantic producer's closed verdict without
reinterpreting its evidence:

| Producer verdict | Evaluator outcome |
| --- | --- |
| `Match` with non-empty evidence | `Matched` with that exact typed evidence. |
| `Match` with empty or structurally invalid evidence | `Failure(SemanticProducerContractViolation)`. |
| `NoMatch` | `NoMatch(SemanticallyConfirmed)`. |
| `Rejected(BoundedDecode)` | `Failure(SemanticDecode)`. |
| `Rejected(UnsupportedInput)` | `Failure(UnsupportedProducerInput)`. |
| `WorkLimitExceeded` | `Failure(SemanticWorkLimit)`. |

Cancellation and unexpected producer exceptions remain cancellation and
exceptional failure respectively; neither becomes a producer verdict or
completed evaluation outcome. Evaluation still releases its candidate in a
`finally` path before either condition propagates.

Producer evidence separates semantic occurrence identity from presentation.
Public occurrence identity uses non-text coordinates such as metadata tokens,
row identities, IL offsets, or another owner-issued type whose public closure
contains no artifact-authored strings. Existing identity types that expose raw
metadata or assembly names remain operation-internal. Every artifact-authored
label, name, decoded literal excerpt, path, or explanatory value enters public
evidence only as an `InertString` constructed under the appropriate field or
prose policy. A producer cannot leave that containment to Package Query or a
host renderer.

The assembly-pattern registry is distinct from the existing
`PackageQuery.Facets` registry. Existing facets remain source-, manifest-, or
archive-path predicates. A host or L2 adapter maps an explicitly promoted
gesture to one assembly-pattern identity and composes that request after the
ordinary facet funnel; it does not dynamically translate a facet identity
into executable pattern semantics.

### Asset-selection intent

Every pattern declares one role:

- **Compile surface** evaluates the selector-issued primary compile asset.
- **Implementation body** evaluates the implementation counterpart of that
  primary compile asset.

The primary compile asset is
`PackageCompileAssetSelection.DefaultAsset`. The package selector currently
chooses a same-named assembly when one exists and otherwise uses its
deterministic first-asset fallback. That policy, including its limitations,
belongs to package selection. Package Query neither repeats nor disguises
the heuristic. Outcomes call this the selector-issued default and preserve its
exact asset identity; they do not relabel it as proof of package-wide
representativeness.

The implementation asset is the result of `FindImplementationAsset` for that
exact compile asset. The evaluator does not independently pick an archive
entry, the largest assembly, another target framework, or another member of
the selected role. The role label records selection intent, not distinct
bytes: for a `lib`-only package, compile-surface and implementation-body
selection may resolve to the same asset.

After choosing the asset, the evaluator supplies the exact canonical
`PackageCompileAsset` object from the binding's frozen selected sequence to
the #5798 sparse package-adapter projection. It consumes that owner's
available projection or typed failure without reconstructing an asset from ID
or path. The evaluator never opens the asset path as an ambient filesystem or
archive location.

The canonical `PackageCompileAsset` remains execution context and never enters
a public outcome. The evaluator issues a resource-free occurrence identity
from the exact frozen asset sequence (`Assets` or `ImplementationAssets`) and
zero-based ordinal established by reference-identity lookup. Combined with
`PackageRootSelectionIdentity`, that occurrence identifies the canonical
selected object without using its path-derived `Id` as authority. The
evaluation role remains a separate field, so a missing implementation
counterpart can identify its primary compile occurrence without pretending
that occurrence belongs to the implementation sequence.

Public selected-asset evidence may expose the asset kind plus assembly name,
target framework, and package-relative path for explanation. Each
archive-authored string is converted at result construction to
`InertString(TextPolicy.Field)`. The raw `Id`, `Path`, `AssemblyName`, and
`TargetFramework` strings are not copied into the outcome graph. Hosts consume
the contained values opaquely; they do not reconstruct an opening path or
asset identity from display text.

The first contract accepts no ecosystem override. A later ecosystem owner may
issue an opaque role-selection binding, but only after a focused prerequisite
defines how Package Query executes that binding against one exact
`PackageRootBinding` and returns an asset from its frozen selection. The
catalog may carry the binding but may not own package-layout reduction or
expose a raw asset path. Until that currency exists, the general fallback is
only the selector-issued default.

## Correspondence invariant

One successful or semantic non-match outcome is valid only for the exact tuple:

```text
exact package Root reacquisition request
+ package coordinate
+ PackageContentGenerationIdentity
+ PackageRootSelectionIdentity
+ selected PackageCompileAsset identity
+ pattern identity and validated operand
+ semantic producer identity
```

The sparse projection owner validates that the selected asset, artifact
provenance, and participant all derive from the binding's Root, content
generation, and frozen selection. Evaluation does not query a current cache
slot or reacquire by coordinate. It accesses selection only through that
binding's frozen Root and carries the binding's selection identity unchanged;
it does not repeat selection to manufacture an equality check. A sparse
projection binding or selection failure becomes a typed candidate failure, not
reselection.

The artifact registration and participant are execution-time checks, never
receipt fields. Every post-selection outcome carries one resource-free
selected-asset context containing the Artifact Acquisition owner's exact Root
reacquisition request, exact realized package coordinate, opaque
`PackageContentGenerationIdentity`, `PackageRootSelectionIdentity`, requested
runtime identifier, selection-relative asset occurrence identity, contained
selected-asset evidence, role, pattern identity and validated operand, semantic
producer identity, and the number of selected siblings that were not
evaluated. Match evidence and non-match discrimination compose with that
context rather than repeating or reconstructing it.

The two process-local identities contain no content or opening authority; they
preserve current-run correspondence only. For compile-surface evaluation the
unevaluated-sibling count is
`Assets.Count - 1`; for implementation-body evaluation it is
`ImplementationAssets.Count - 1`. Each count is scoped to its selector-issued
role sequence: compile assets to the selected TFM, and implementation assets to
the selected TFM and RID. Neither count includes assets outside that sequence.
The receipt therefore cannot be read as an exhaustive package-wide verdict.

Those `Count - 1` formulas apply only after an asset from the corresponding
sequence has been selected for evaluation. When the primary compile asset has
no implementation counterpart, no implementation asset was evaluated, so the
non-applicable outcome reports the full `ImplementationAssets.Count`.

The process-local identities are receipts for current-run correspondence, not
portable cache keys. A later Workspace transition consumes #5837's exact Root
reacquisition request, reapplies destination-host authorization, and reacquires
or independently reuses authorized content. It does not adopt a disposed
evaluation stream, use the candidate Workspace's
`PackageArtifactRootCorrespondence`, or reconstruct a generation token or
selection target.
[Durable package-content identity](https://github.com/richlander/dotnet-inspect/issues/5484)
remains a prerequisite only for persistent derived-result reuse across
processes.

## Primary-asset outcomes

Asset resolution is explicit and typed:

| Selection state | Evaluation outcome |
| --- | --- |
| No compile assets | `NotApplicable(NoCompileAssets)` |
| No matching target framework | `NotApplicable(NoMatchingTargetFramework)` |
| Explicit empty compile group | `NotApplicable(EmptyCompileGroup)` |
| Invalid implementation selection | `Failure(InvalidAssetSelection)` for either role |
| Compile-surface pattern with a selected default | Evaluate the default compile asset |
| Implementation-body pattern with a selected counterpart | Evaluate that counterpart |
| Implementation-body pattern without a counterpart | `NotApplicable(NoImplementationCounterpart)` with the selected primary compile occurrence |

`NotApplicable` is not a match and is not malformed-content failure. The later
streaming owner decides whether it remains an explicit durable candidate event
or contributes only to final accounting. It must not be presented as a
semantic `NoMatch`.

The non-applicable algebra has two resource-free shapes:

- **Selection not applicable** carries the exact subject,
  owner-issued Root reacquisition request, `PackageContentGenerationIdentity`,
  `PackageRootSelectionIdentity`, pattern request, declared role, and typed
  `NoCompileAssets`,
  `NoMatchingTargetFramework`, or `EmptyCompileGroup` reason. It carries no
  selected asset because none was issued.
- **Implementation counterpart not applicable** carries those same fields plus
  the primary compile asset's selection-relative occurrence identity and
  contained selected-asset evidence. It reports the count of implementation
  assets that remained unevaluated; it does not invent an implementation asset
  or claim that the compile image was semantically evaluated.

The current selector couples compile and implementation selection: an invalid
implementation universe returns no selected default even when discovered
reference assets might otherwise be usable. This evaluator inherits that
typed package-adapter outcome and does not bypass it for compile-surface
patterns.

The first contract evaluates one primary asset. "Search every assembly in this
package" is a different cost and result-cardinality contract and requires a
separate product-issued pattern or scope.

An explicit empty compile group remains `NotApplicable` for an
implementation-body pattern even when implementation assets exist. The package
selector has not issued a primary compile asset from which to derive an exact
implementation counterpart. A future direct implementation-primary policy
belongs to package selection rather than to an alphabetical fallback here.

### Sparse-projection outcome mapping

The evaluator consumes the package owner's closed projection algebra without
reopening content or inferring a failure from exception text:

| Sparse projection outcome | Evaluator action |
| --- | --- |
| #5843 `Projected` plus an `Available` sparse realization | Enter the Metadata owner's artifact query validation with the exact `ArtifactAssemblyProjection`. `NotAssembly` or `Rejected` becomes `Failure(ImageAdmission)` with the exact owner-typed query reason. Only the `Validated` callback may reach the prefilter or semantic producer. |
| #5843 `NotAssembly` or `Rejected` | Return `Failure(ImageAdmission)` with the exact Metadata admission reason and do not invoke query validation, the prefilter, or the semantic producer. If #5843's owner shape transfers a compatibility carrier, it remains cleanup authority only; otherwise the candidate workspace remains empty. |
| Missing, contradictory, or unknown #5843 Metadata admission evidence, including `Projected` without an actual `Available` transfer | Return `Failure(ProjectionContractViolation)`. `IdentityDecoded` compatibility evidence never repairs or replaces the owner-issued admission variant. |
| `InvalidBinding` | Return `Failure(InvalidBinding)`. This remains a defensive parent outcome even if the first immutable binding implementation cannot currently produce it. |
| `InvalidSelectedAsset` | Return `Failure(ProjectionContractViolation)`. The evaluator supplied a canonical object from the same binding, so this arm indicates a composition defect rather than package-authored content. |
| `SelectedEntryUnavailable` | Return `Failure(SelectedEntryUnavailable)`. |
| `EntryByteLimitExceeded` | Return `Failure(SelectedEntryByteLimit)`. |
| `ArtifactPublicationFailed` | Return `Failure(ArtifactPublication)` while preserving each artifact-owner failure kind and diagnostic code. A bounded presentation diagnostic is derived from that typed evidence. |

The evaluator does not reproduce the package owner's missing-entry sentinel,
manifest preflight, observed-copy limit mapping, aggregate byte partition, or
cleanup rules. Those mechanics and their gates remain wholly with the sparse
projection. If a non-`Available` projection performed owner-local cleanup
before ownership transfer and reports that cleanup as incomplete, #5842
supplies the exact resource-free owner-issued receipt beside the primary
projection outcome. Successful or unnecessary cleanup supplies no receipt. The
evaluator copies an issued receipt opaquely as secondary `ProjectionCleanup`
evidence. It neither defines the receipt's internal shape nor copies an
exception, invents cleanup failure from an empty workspace report, or replaces
the primary projection reason.

## Selected-entry and image lifetime

The package adapter has already admitted the archive and frozen the package
selection. Evaluation adds a narrower candidate-scoped realization:

1. Validate the binding, pattern, asset intent, and budget before requesting
   artifact materialization.
2. Resolve the selected asset only through the frozen selection.
3. Create a candidate-scoped asynchronous workspace.
4. Ask the package adapter to attempt projection of only that asset under the
   bounded artifact generation. Only `Available` transfers a one-participant
   group to the candidate workspace.
5. Record whether #5843's final owner shape completed the existing `Available`
   ownership transfer, then consume its exact Metadata admission variant.
   `NotAssembly` or `Rejected` becomes image-admission failure without invoking
   query validation or either executable binding. A missing or contradictory
   variant is a projection contract violation; `IdentityDecoded` is
   compatibility evidence only.
6. For `Projected`, require the `Available` transfer and enter Metadata query
   validation with the exact owner-issued projection. Preserve query-time
   `NotAssembly` or `Rejected` as image-admission failure. Inside the one
   `Validated` callback, give the optional prefilter a borrowed
   `AssemblyImageView` and the semantic producer an
   `AssemblyInspectionSession` over that same retained image.
7. Copy all evidence needed by the outcome into resource-free values.
8. In a `finally` path, dispose the query session and candidate workspace,
   then inspect the shared close report before returning an outcome or
   propagating cancellation or an unexpected exception.

The operation does not extract a package tree, open sibling entry bodies, or
realize every package role. At most one selected package assembly is live in
the candidate workspace. Artifact and Metadata owners may retain separate
bounded snapshots while enforcing their existing lifetime contracts; the
retained-image budget accounts for both copies rather than pretending there is
only one buffer.

The workspace is deliberately per candidate. Artifact-session release is tied
to workspace close rather than realization disposal, so reusing one workspace
across the corpus would retain prior candidate images and violate the sparse
retention bound. The later pipeline may optimize construction only if a
workspace owner first supplies an equivalent candidate-retirement contract.

An entry that disappears from an otherwise retained content handle is a
visible package-adapter failure. It does not trigger reselection against a
different package generation.

## Conservative byte prefilter

A byte prefilter is an optimization, never match evidence.

One pattern binding may declare a prefilter only when the semantic owner can
state and gate this implication:

```text
semantic match => prefilter admits
```

The prefilter may return:

- **reject**: semantic evaluation is skipped and the result is
  `NoMatch(PrefilterRejected)`;
- **admit**: semantic evaluation is required; or
- **not applicable**: semantic evaluation is required.

An admit result never becomes a match. Bytes may contain the operand in an
unrelated heap, compressed payload, debug record, resource, or dead metadata
row. Only the semantic producer can identify the promised occurrence.

The implication proves match preservation, not failure discovery. A
`PrefilterRejected` candidate deliberately does not run the semantic producer,
so it cannot reveal malformed structures or semantic work-limit exhaustion
that only that traversal would encounter. Completion for a prefilter-bearing
pattern may claim semantic no-match coverage under the prefilter proof; it
must not claim exhaustive producer-failure discovery.

Metadata-owned artifact query validation runs before the prefilter, so native
content, managed modules, unsupported Windows Metadata, and malformed PE/CLR
headers cannot become byte-level `NoMatch` outcomes. In particular, both
Windows Metadata kinds must reach the owner-issued
`Rejected(UnsupportedWindowsMetadata)` arm before either executable binding.
This property is **unverified** and evaluator implementation is blocked until
the #5143/#4857 validation path and its named unsupported-format gates land.
Deeper malformed structures are visible when the selected semantic traversal
encounters them; the evaluator does not claim to exhaustively validate
unrelated metadata that a proven prefilter skips.

Each prefilter-bearing descriptor defines a closed representation set for its
operand inside one admitted image. Its Release fixtures are derived from that
set, including alignment, encoding, indirection, and close negative cases.
When the producer cannot enumerate the complete representation set, that
pattern has no rejecting prefilter. A pattern whose semantics may consult
another image is prefilter-ineligible in this one-image contract.

Patterns whose encodings, normalization, indirection, or decoding rules cannot
support a no-false-negative byte predicate use no prefilter. Performance goals
do not justify a lossy rejection path.

## Bounds and cancellation

Every request has finite, positive bounds for:

- selected assembly expanded bytes;
- candidate retained-image bytes across package-artifact and Metadata
  snapshots;
- semantic work in the producer's declared unit;
- producer temporary bytes when the selected binding requires a distinct
  working-set budget; and
- the enclosing operation deadline.

The design does not set product defaults before measurement. The
implementation issue must record a pinned package corpus, commands, baseline,
peak live bytes, and elapsed time before choosing defaults and maxima.
The caller-visible peak bound covers the combined live copies retained by the
package artifact and Metadata workspace owners; it is not merely a selected
entry-size limit. The merged sparse-projection owner defines the concrete
budget partition and its typed rejection; this evaluator supplies the admitted
bounds and preserves that outcome.

The semantic producer owns its work unit and checkpoints. Examples include
metadata rows, decoded signatures, method bodies, or instructions. Package
Query validates that the request supplies the budget kind required by the
selected binding; it does not translate one producer's unit into another.

Each producer adoption also declares and gates one of two working-set forms:

- a finite temporary-byte budget carried by the request and enforced by the
  producer; or
- a finite live-working-set bound expressed as a documented function of the
  selected-image and operand bounds.

The retained-image bound is not presented as a whole-operation allocation
ceiling. The later stream owner uses the producer's working-set declaration
with the retained-image bound when choosing candidate concurrency.

Cancellation is not a match, non-match, or item failure. The evaluator observes
the operation token before sparse projection, after bounded materialization,
before semantic evaluation, at producer-owned traversal checkpoints, and at
the terminal publication boundary. Every match, non-match, non-applicable
outcome, or typed failure remains provisional until required candidate cleanup
finishes and its report is inspected. Immediately before returning that
completed outcome, the evaluator observes the token once more. Cancellation at
that point discards the provisional outcome and propagates to the enclosing
stream or host operation; any non-empty
`PackageAssemblyEvaluationCleanupEvidence` is attached to the propagated
cancellation as ancillary evidence. No result is published after cancellation
is observed.

An unexpected exception from a prefilter or semantic binding follows the same
cleanup rule. Candidate cleanup runs in `finally`; a cleanup failure is
attached through the same typed ancillary-evidence contract and cannot replace
the original exception.

`InspectionWorkspace.CloseAsync()` may complete normally while reporting
direct-group release failure in `Groups` or artifact-session release failure in
`ArtifactSessionCleanupFailures`. The evaluator tracks whether the sparse
projection completed its `Available` ownership transfer:

- before `Available`, a successful close report has no `Groups` and no
  artifact-session cleanup failures because every non-available projection
  publishes no participant and retains owner-local cleanup responsibility; and
- after `Available`, a successful close report has exactly one
  `InspectionWorkspaceDirectGroupCloseResult` with `Succeeded == true` and no
  artifact-session cleanup failures.

Before `Available`, any workspace group result or artifact-session cleanup
failure is a close-report contract failure. After `Available`, a missing,
additional, coordinated, or otherwise unexpected group result is a
close-report contract failure rather than a result the evaluator tries to
reinterpret. The empty pre-publication report is not a cleanup failure; any
package-owner pre-transfer cleanup receipt is preserved separately as
`ProjectionCleanup`.

The evaluator applies one precedence rule to the whole report before the
terminal token observation:

- after a would-be `Matched` or `NoMatch`, any failed or unexpected
  post-`Available` group result or artifact-session cleanup failure replaces
  the success with
  `Failure(CandidateCleanup)`;
- after a typed evaluation failure, cleanup evidence is appended as secondary
  evidence without replacing the primary failure stage; and
- during cancellation or unexpected exception propagation, the resource-free
  cleanup bundle is attached as secondary evidence to the primary condition.

After report interpretation, terminal cancellation supersedes any provisional
completed outcome, including a typed failure. An unexpected exception already
being propagated remains primary; it is not converted into a cancellation
outcome.

`PackageAssemblyEvaluationCleanupEvidence` is immutable, resource-free, and
product-authored. The same type is used by typed outcomes and propagated
exceptions. It contains the optional opaque #5842 `ProjectionCleanup` receipt
and a bounded sequence of distinct candidate-cleanup stages and counts:

- **GroupRelease** for an unsuccessful direct-group result;
- **ArtifactSessionRelease** for reported artifact-session cleanup failures;
- **CloseReportContract** for any group or artifact cleanup entry before
  `Available`, or a missing, additional, coordinated, or unknown group result
  afterward; and
- **CloseOrchestration** when `CloseAsync()` faults after attempting the
  workspace owner's terminal release protocol.

It carries no exception instances, messages, paths, or stack traces.
Report-valued direct-group failure is not described as a thrown exception.
`CloseAsync()` faults only when group-close orchestration itself faults; after
the workspace owner attempts all possible artifact-session releases, that
close exception propagates as the primary condition or contributes the
`CloseOrchestration` stage to an already-propagating cancellation or exception.
In either case the evaluator reads the terminal `CloseReport`, when present, so
report-valued group and artifact-session evidence is not lost merely because
the close task faulted.

### Propagated exception cleanup evidence

The evaluator exposes one typed host-neutral accessor:

```text
PackageAssemblyEvaluationExceptionEvidence.TryGetCleanup(
    Exception primary,
    out PackageAssemblyEvaluationCleanupEvidence cleanup)
```

The evaluator attaches a non-empty cleanup bundle to the exact primary
exception under one private product-owned `Exception.Data` key. Consumers use
the accessor rather than parsing that key or casting an untyped value. This
follows the repository's existing ancillary-cleanup attachment convention
while replacing raw cleanup exceptions with this evaluator's inert typed
evidence.

An unexpected prefilter, producer, or close-orchestration exception preserves
its exact instance, runtime type, stack, and existing data; the evaluator
reattaches it through `ExceptionDispatchInfo` after cleanup. If another
exception was already primary, a close-orchestration fault is represented only
by the inert `CloseOrchestration` stage and does not replace or wrap that
primary exception.

Cancellation observed before cleanup preserves the caught
`OperationCanceledException` and its token. Terminal cancellation first
observed after cleanup throws an `OperationCanceledException` carrying the
operation token. Either cancellation exception carries the same typed cleanup
bundle when pre-transfer or candidate cleanup was incomplete. When no cleanup
evidence exists, the accessor returns `false`; an empty attachment is never
manufactured.

The operation deadline is implemented by cancelling that same operation token.
Deadline expiry is therefore cancellation, not a separate candidate failure.
The terminal token observation is the completion linearization point:
completion wins only after cleanup has finished and that observation succeeds.
`InspectionWorkspace.CloseAsync()` itself is not shortened or abandoned when
the deadline expires; resource release completes before cancellation
propagates.

This linear one-candidate operation adds no independent concurrent state
machine. Bounded concurrency and result ordering belong to the later streaming
pipeline; no TLA+ model is required for this contract.

## Outcome algebra

One completed evaluation returns exactly one resource-free outcome. Every arm
carries #5837's owner-issued exact package Root reacquisition request so a host
never rebuilds opening intent from result display fields:

- **Matched**: selected-asset context plus non-empty producer evidence.
- **NoMatch**: selected-asset context plus a `PrefilterRejected` or
  `SemanticallyConfirmed` discriminator.
- **NotApplicable**: one of the selection or implementation-counterpart shapes
  defined above, preserving exact frozen-selection correspondence.
- **Failure**: one of the closed preselection or selected-asset failure shapes
  below.

Pattern construction rejects an unknown identity, mismatched operand arm,
invalid operand, missing producer budget, or non-positive bound before
execution. Nulls and other caller contract violations retain argument
exceptions rather than becoming candidate outcomes.

Failures before an asset can be selected carry the exact subject,
owner-issued Root reacquisition request, content-generation identity, selection
identity, pattern request, declared role, and typed package-selection reason.
They do not invent an asset context.

Every failure after asset selection carries the complete selected-asset
context plus one stage-specific resource-free payload:

- invalid binding, projection-contract, and semantic-producer-contract
  failures carry a stable product-authored failure code;
- selected-entry unavailable carries its exact package-projection reason;
- selected-entry byte limit carries the admitted entry and aggregate
  retained-image bounds;
- artifact publication carries each owner-issued
  `ArtifactSetAdmissionFailureKind` and diagnostic code, copied without the
  diagnostic implementation object or cleanup exception;
- image admission carries the exact Metadata-owned
  `ArtifactNonAssemblyKind`, `ArtifactAssemblyProjectionFailure`, or
  `ArtifactAssemblyQueryFailure`, retaining whether the reason came from
  sparse admission or later query validation;
- semantic decode and unsupported-input failures carry the producer-owned
  typed failure;
- semantic work limit carries the producer-owned budget kind, admitted limit,
  and charged work when available; and
- candidate cleanup carries the bounded sequence of close stages and reported
  counts.

A sparse projection failure may additionally carry the package owner's
exact #5842 resource-free pre-transfer cleanup receipt as secondary
`ProjectionCleanup` evidence. The evaluator preserves that opaque receipt
without translating an exception or replacing the primary failure.

The optional projection receipt and candidate-close stages compose into the
same `PackageAssemblyEvaluationCleanupEvidence` used by failure outcomes and
propagated exceptions. Terminal cancellation may discard a provisional failure
outcome, but it does not discard that outcome's already-issued cleanup
evidence.

A bounded product-authored presentation diagnostic may accompany a failure,
but it is derived from the typed payload and is never the only durable cause.
Package-authored text and exception messages remain excluded.

Failure stages distinguish:

- invalid asset selection;
- invalid binding;
- sparse-projection contract violation;
- selected-entry unavailable;
- selected-entry byte limit;
- artifact publication;
- Metadata-owned image admission;
- semantic decode;
- unsupported producer input;
- semantic work limit;
- candidate cleanup; and
- semantic-producer contract violation.

Package-authored strings, paths, metadata names, and exception text do not
become diagnostics. Unexpected implementation exceptions remain exceptional;
after candidate cleanup, the evaluator propagates them rather than converting
them into `NoMatch` or a generic successful outcome.

`Failure(InvalidAssetSelection)` records the typed selection status only. It
does not propagate the selector's package-derived `Message`.

No outcome carries a workspace, artifact registration, participant, stream,
package-content handle, image buffer, `ZipArchive`, `PEReader`,
`MetadataReader`, assembly session, analysis index, lease, callback, or
executable binding.

## Evidence and result opening

A match explains why it matched. Producer evidence is typed before either host
renders it and includes the semantic occurrence needed by that pattern, such
as a metadata identity, API target, method address, or instruction occurrence.
Package Query adds the exact package and selected-asset context; it does not
rephrase the semantic fact from display text. Artifact-authored explanatory
text is already contained by the asset or producer owner before either host
receives it.

Opening a match starts the standard typed Workspace transition with #5837's
Artifact Acquisition-owned exact package Root reacquisition request. That
request preserves both the producer-bound acquisition coordinate and the
selection target that formed the evaluated Root, including a framework-neutral
coordinate or a selection target that differs from the acquisition framework.
The destination host reapplies current source authorization and may reacquire
content or use an independently authorized cache generation. It never adopts
the evaluator's disposed image, carries the candidate Workspace's
`PackageArtifactRootCorrespondence`, assumes that a process-local generation
identity is durable, or infers the selection target from contained display
text.

A later corpus completion counts `NotApplicable`, work-limit, and other
candidate failures separately from semantic non-matches. Evaluating every
declared candidate can prove completion over the selected-primary-asset scope;
it cannot claim that every assembly in those packages was evaluated.

## Failure visibility

The package is untrusted internet-origin data. Archive admission remains the
package owner's containment gate. This evaluator adds bounded handling for the
selected expanded assembly and delegates PE/metadata/IL validity to their
existing owners.

Malformed archives, unsafe entry paths, and archive bombs do not reach this
owner because package admission rejects them. A selected entry can still be
oversized, disappear, contain native content, contain a managed module rather
than an assembly, use unsupported Windows Metadata, or carry malformed
ECMA-335 structures. Image-admission failures and malformed structures
encountered by the selected semantic path are visible typed outcomes or
failures, never an empty match set.

Incomplete projection or candidate cleanup is likewise visible either in the
typed outcome's `PackageAssemblyEvaluationCleanupEvidence` or, when cancellation
or an unexpected exception is primary, through
`PackageAssemblyEvaluationExceptionEvidence.TryGetCleanup`.

Unsupported Windows Metadata visibility depends on the unimplemented
Metadata artifact query-validation seam in #5143/#4857 and remains
**unverified**. The evaluator must not ship a compatibility fallback that lets
either Windows Metadata kind reach a prefilter or semantic producer.

The tool never loads or executes the inspected assembly.

## Rendering strategy

This owner returns typed data only. The CLI may lower durable package match
rows through its existing Sections and Markout path. Inspect Web lowers the
same typed evidence through its host-specific Package Query renderer. Neither
host parses a formatted evidence string to recover identity or semantics.

This is not a new broad information domain: the row grain remains one package
candidate and the pattern producer supplies its existing typed evidence.

## Pathological cases

The implementation must preserve focused fixtures for:

1. a package whose ID differs from its selector-issued default assembly name;
2. a multi-assembly package whose default compile asset has a distinct
   RID-specific implementation counterpart;
3. a reference-only package with no implementation counterpart;
4. a framework-neutral acquisition coordinate with a non-null selection target,
   and an acquisition framework that differs from the selection target;
5. raw bytes containing the operand in an unrelated region while semantic
   evaluation returns `NoMatch`;
6. a semantic match in every encoding or indirection covered by an enabled
   prefilter;
7. a selected assembly at the exact byte limit and one byte above it;
8. a selected asset whose assembly name, TFM, and package path contain control,
   format, bidirectional, or markup-significant text;
9. producer evidence whose metadata name or decoded literal excerpt contains
   the same hostile text classes;
10. native, module, unsupported Windows Metadata, and malformed managed input;
11. a selected entry that cannot be materialized from the retained content
   generation;
12. a non-`Available` sparse projection whose empty candidate workspace closes
    cleanly, and whose owner-local pre-transfer cleanup separately fails;
13. semantic work reaching its exact limit and exceeding it;
14. cancellation during sparse projection and semantic traversal;
15. deadline expiry after a provisional producer result while candidate close
    is pending;
16. a successful or throwing prefilter or producer whose candidate close also
    reports direct-group or artifact-session cleanup failure; and
17. an early cancellation or producer exception whose candidate close faults
    after storing its report, preserving the primary condition while exposing
    only typed inert cleanup evidence.

The package-ID/default-asset and byte-prefilter cases are contract-defining and
must run in the ordinary Release suite. Performance and peak-memory corpus
measurements may remain a reproducible non-CI probe, but they are evidence for
choosing bounds rather than gates for semantic correctness.

## Required gates

The target Release suite is `DotnetInspector.Queries.Tests`, which already
references `DotnetInspector.PackageQueries`, with producer-specific focused
gates where a Metadata or Analysis binding is adopted.

| Gate | Property |
| --- | --- |
| `PackageAssemblyEvaluation_PrimaryAssetIsSelectorIssuedAndAssetScoped` | General primary selection consumes the selector-issued default, preserves its exact asset identity and the role-sequence sibling count, and never claims that the heuristic proves a package-wide representative. |
| `PackageAssemblyEvaluation_ImplementationUsesExactSelectedCounterpart` | An implementation-body pattern uses only the counterpart of the selected compile asset, including RID replacement and the `lib`-only case where both role intents resolve to the same asset. |
| `PackageAssemblyEvaluation_MissingRoleIsDistinctFromNoMatch` | Empty selection and missing implementation correspondence return typed `NotApplicable` outcomes with the exact Root reacquisition request plus generation and selection identities; counterpart absence also preserves the primary compile occurrence without inventing an implementation asset. |
| `PackageAssemblyEvaluation_SelectedAssetEvidenceIsContained` | The complete public outcome closure rejects `PackageCompileAsset`; hostile package-authored assembly name, TFM, and path text become `InertString(TextPolicy.Field)` at result construction, while exact occurrence identity uses only the frozen asset-sequence kind, ordinal, and selection identity; evaluation role remains separate. |
| `PackageAssemblyEvaluation_UsesSparsePackageArtifactProjection` | One evaluation calls the sparse selected-asset projection rather than the full package-role realization path. |
| `PackageAssemblyEvaluation_MapsSparseProjectionOutcomesExactly` | Every package-owned projection arm maps as declared without reopening content, parsing diagnostics, or turning projection failure into semantic no-match. The exact #5842 resource-free owner-issued pre-transfer cleanup receipt remains secondary to the projection reason. |
| `PackageAssemblyEvaluation_SparseMetadataAdmissionControlsSemanticBinding` | #5843's `Projected` variant with an actual `Available` transfer is the only sparse result that may enter query validation. `Projected` without transfer, `NotAssembly`, `Rejected`, and missing or contradictory variants map exactly as declared; `IdentityDecoded` or a compatibility carrier cannot authorize either executable binding. This gate remains unverified until #5843 lands. |
| `PackageAssemblyEvaluation_MetadataValidationPrecedesSemanticBinding` | A projected participant defensively maps exact query-time `NotAssembly` or `ArtifactAssemblyQueryFailure` evidence rather than assuming admission makes those owner-issued outcomes impossible. Both Windows Metadata kinds produce `Rejected(UnsupportedWindowsMetadata)` during #5843 admission before the prefilter or producer; no compatibility fallback can return semantic no-match. This gate remains unverified until #5143/#4857 and #5843 land. |
| `PackageAssemblyEvaluation_SuppliesSparseProjectionBounds` | The evaluator passes the admitted entry and aggregate retained-image bounds unchanged and maps the package owner's typed limit outcome without restating its partition mechanics. |
| Conditional producer prefilter gate | Each prefilter-bearing adoption derives fixtures from its complete declared representation set, obtains byte and semantic views inside one Metadata-validated callback over the same retained image, admits every semantic match, and requires semantic confirmation for false byte positives. A prefilter-free adoption needs no such gate. |
| `PackageAssemblyEvaluation_MapsProducerVerdictsExactly` | Match, no-match, bounded-decode rejection, unsupported input, work limit, and invalid match evidence map to their declared distinct outcomes without collapsing failure into semantic no-match. |
| `PackageAssemblyEvaluation_PreservesExactCorrespondence` | Execution consumes #5798's exact selected-asset projection for the coordinate, content generation, selection, and canonical asset; the resource-free receipt preserves #5837's owner-issued Root reacquisition request and both process-local correspondence identities with the package/asset context, sibling count, pattern, and producer evidence. |
| `PackageAssemblyEvaluation_PreservesExactRootReacquisitionRequest` | Framework-neutral acquisition with a non-null selection target and differing acquisition/selection frameworks both retain #5837's exact owner-issued request; later Workspace opening repeats that selection intent without parsing display text. This gate remains unverified until #5837 lands. |
| `PackageAssemblyEvaluation_FailureCarriesTypedContext` | Preselection failure carries the owner-issued Root reacquisition request and exact selection context without an invented asset; every post-selection failure carries the complete selected-asset context and its declared owner-typed stage payload rather than relying on presentation text. |
| `PackageAssemblyEvaluation_ReleasesResourcesOnEveryOutcome` | Preselection outcomes create no candidate resources. Every workspace-bearing sparse failure, match, non-match, semantic failure, work-limit, cancellation, and unexpected binding-exception path enters `finally`; a pre-`Available` path closes an empty workspace while a post-`Available` path attempts participant and artifact release. Throwing prefilter and producer fixtures prove preservation of the exact primary exception when report-valued or faulted close also supplies typed ancillary cleanup evidence. |
| `PackageAssemblyEvaluation_CloseReportCannotReturnSuccess` | Before sparse ownership transfer, the fresh candidate close report must contain no group or artifact cleanup entry; after `Available`, it must contain exactly one successful direct-group result and no artifact-session cleanup failures. Fixtures independently cover the legitimate empty report, direct-group failure, artifact-session failure, close-orchestration fault with a stored report, and unexpected result shape. Existing typed failure retains its primary stage with bounded secondary cleanup evidence; cancellation and unexpected exceptions retain their primary propagated condition. |
| `PackageAssemblyEvaluation_PropagatedCleanupEvidenceIsTypedAndVisible` | A producer exception remains the exact propagated exception and an early cancellation preserves its token while report-valued group failure, artifact-session failure, and close-orchestration fault are retrievable only through `PackageAssemblyEvaluationExceptionEvidence.TryGetCleanup`. The returned immutable bundle contains the expected #5842 receipt and candidate stages without raw exceptions or messages; no-cleanup paths have no attachment. |
| `PackageAssemblyEvaluation_TerminalCancellationCannotPublishOutcome` | A provisional producer match is held while direct-group release remains pending; deadline expiry cancels the operation, cleanup completes, and the terminal token observation propagates cancellation with the operation token and no completed outcome. Failed release is retrievable through the typed ancillary-evidence accessor. The same terminal check covers provisional non-match, non-applicable, and typed-failure outcomes, including preservation of already-issued cleanup evidence. |
| `PackageAssemblyEvaluation_FailuresRemainVisibleAndInert` | Malformed, unsupported, oversized, and disappearing selected entries produce typed inert failures rather than empty success or package-authored diagnostics. |
| `PackageAssemblyEvaluation_ResultClosureIsResourceFree` | The full gate reflects the public transitive closure of every request, outcome, cleanup bundle, and exception-accessor result and rejects prohibited resource or authority types. |
| `PackageAssemblyEvaluation_OneRequestProducesOneOutcome` | Normal completion returns exactly one outcome only after cleanup and the terminal token observation; cancellation or unexpected failure cannot also publish one. |
| Producer-specific semantic gate | Each adopted pattern proves its exact semantic meaning, work bound, working-set declaration, and optional prefilter implication in the owning Metadata or Analysis Release suite. Its evidence-closure gate rejects artifact-authored raw `string` fields and identity types that publicly expose them, while admitting non-text occurrence coordinates and contained `InertString` presentation fields. |
| CLI and Browser consumer canaries | Both hosts can plan and consume the same descriptors and outcomes without duplicating pattern semantics. |

### Resource-free absence-claim coverage

The user selected full coverage for the composition absence claim that request
and outcome types expose no resource or authority route. The implementation
gate reflects the complete public transitive closure of request, descriptor,
receipt, evidence, and outcome types. It rejects streams, archives, readers,
metadata handles, sessions, image buffers, package-content handles, leases,
workspaces, artifact registrations, participants, callbacks, delegates, and
executable bindings wherever nested.

The #5837 Root reacquisition request is expressly admissible only when its owner
gate proves that the full public closure contains no Workspace identity,
physical-generation identity, content authority, opener, or lease. It is
opening intent consumed by a later authorized Workspace transition, not an
authority to access the disposed candidate.

This gate does not prohibit operation-internal resource use. Entry streams,
image buffers, artifact registrations, participants, readers, and sessions may
exist inside evaluation while their owning scope is active; they cannot enter
the public request or result object graph.

## Analogous evidence

No surveyed implementation provides the complete package-selection,
candidate-lifetime, semantic-confirmation, and Browser/Wasm contract.
The following boundaries transfer; their broader architectures do not:

- NuGet.Client separates compile `ref`/`lib` selection from runtime and
  RID-specific implementation selection, and chooses a deterministic best
  group from ordered criteria. This contract reuses the product's existing
  selector rather than importing restore or dependency-closure behavior.
  ([patterns](https://github.com/NuGet/NuGet.Client/blob/e6aaa9af1e451d6909bbf4be933cb96ad11da535/src/NuGet.Core/NuGet.Packaging/ContentModel/ManagedCodeConventions.cs#L517-L567),
  [RID criteria](https://github.com/NuGet/NuGet.Client/blob/e6aaa9af1e451d6909bbf4be933cb96ad11da535/src/NuGet.Core/NuGet.Packaging/ContentModel/ManagedCodeConventions.cs#L396-L418),
  [ordered selection](https://github.com/NuGet/NuGet.Client/blob/e6aaa9af1e451d6909bbf4be933cb96ad11da535/src/NuGet.Core/NuGet.Packaging/ContentModel/ContentItemCollection.cs#L141-L245))
- NuGet Insights preflights package contents, copies one ZIP entry into a
  seekable temporary representation, constructs an SRM reader, and disposes the
  candidate buffer in `finally`. Its full-package download, all-DLL scan,
  accumulated result list, and server temp-file policy do not transfer.
  ([driver](https://github.com/NuGet/Insights/blob/c449aa472b10aea098bf46e94767f9952fd16a60/src/Worker.Logic/Drivers/PackageAssemblyToCsv/PackageAssemblyToCsvDriver.cs#L73-L245),
  [buffer policy](https://github.com/NuGet/Insights/blob/c449aa472b10aea098bf46e94767f9952fd16a60/src/Logic/TempStream/TempStreamWriter.cs#L91-L161))
- SRM's `PEReader` contract requires readable seekable input and makes
  ownership, lazy reading, and metadata prefetch explicit. The transferable
  rule is a bounded immutable candidate lifetime; the package artifact and
  workspace owners remain responsible for supplying it.
  ([constructor and ownership](https://github.com/dotnet/runtime/blob/bdec678032fd579854e525c5c309eac1c1dd22c8/src/libraries/System.Reflection.Metadata/src/System/Reflection/PortableExecutable/PEReader.cs#L91-L128),
  [prefetch behavior](https://github.com/dotnet/runtime/blob/bdec678032fd579854e525c5c309eac1c1dd22c8/src/libraries/System.Reflection.Metadata/src/System/Reflection/PortableExecutable/PEReader.cs#L156-L220))
- Ripgrep's push sink, immediate stop, match cap, and separate allocation limit
  support the later event-pipeline choice to stop without retaining a corpus.
  Its line, binary, regex, and whole-input multiline semantics do not define
  metadata or IL matching.
  ([sink contract](https://github.com/BurntSushi/ripgrep/blob/3fce3b5bb0236da2df6d99672afb8a719642eca7/crates/searcher/src/sink.rs#L62-L123),
  [limits](https://github.com/BurntSushi/ripgrep/blob/3fce3b5bb0236da2df6d99672afb8a719642eca7/crates/searcher/src/searcher/mod.rs#L440-L494))

The survey also exposed one important limit rule: a declared ZIP length is a
preflight hint, not proof of actual consumption. NuGet Insights deliberately
accepts unknown and mismatched advertised lengths, supporting the sparse
projection owner's hard observed-byte gate during materialization.
([tests](https://github.com/NuGet/Insights/blob/c449aa472b10aea098bf46e94767f9952fd16a60/test/Logic.Test/TempStream/TempStreamWriterTest.cs#L33-L120))

Further producer-specific comparison belongs in the first producer adoption.
This design does not authorize copying code or architecture from an analogous
implementation.

## Non-goals

- No source discovery, version resolution, package download, archive
  admission, cache, or corpus scheduling.
- No candidate concurrency, event batching, progress cadence, completion
  accounting, or worker protocol.
- No CLI option spelling, Browser control, route, focus, or rendering change.
- No arbitrary predicate language, regex engine, or executable plug-in model.
- No all-assembly package scan in the first contract.
- No dependency resolution or cross-assembly semantic question.
- No persistent result cache or durable cross-process content identity.
- No Windows Metadata support.
- No defense against same-machine actors, local file mutation during one
  operation, or deliberate corruption of trusted in-process state beyond
  existing owner contracts.

## Delivery sequence

The production-host adoption path has 13 steps:

1. Lock this focused design and transfer the promoted-tier responsibility from
   the Package Query CLI proposal.
2. Implement #5884's Artifact Acquisition-owned phase-scoped retained byte
   access before and after artifact publication.
3. Implement the #5143/#4857 Metadata-owned artifact admission and query
   validation path, including both unsupported-Windows-Metadata gates.
4. Define #5843's sparse composition with the Metadata admission result and
   query-validation registration.
5. Define and implement #5837's Artifact Acquisition-owned exact package Root
   reacquisition request and destination Workspace transition.
6. Define #5842's Artifact Acquisition-owned resource-free pre-transfer cleanup
   receipt.
7. Implement the merged #5798 sparse-projection contract and its named gates,
   preserving the exact Root binding, issuing #5842 receipts when applicable,
   composing #5843's Metadata result, and avoiding full role realization.
8. Define the first concrete producer under #5795, including its exact
   semantic occurrence, bounded operand, and optional UTF-16LE prefilter proof.
9. Implement the one-candidate evaluator and its structural gates in
   `DotnetInspector.PackageQueries`.
10. Adopt the first producer, adding its semantic gate and a prefilter gate only
   when that producer declares a complete representation set.
11. Measure a pinned package corpus and choose explicit selected-entry,
   retained-image, producer-working-set, and semantic-work bounds.
12. Compose the evaluator into the bounded Package Query event stream, with
   candidate scheduling and disposal owned by a separate focused pipeline
   slice.
13. Add CLI and Browser gestures and exact result opening in their respective
    owners.
