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
2. asks the package adapter to project only that selected package entry into
   one neutral artifact-backed participant;
3. may reject it through an owner-proved conservative byte prefilter;
4. otherwise obtains the semantic producer's typed verdict;
5. returns a resource-free match, non-match, non-applicable outcome, or visible
   item failure; and
6. closes the candidate workspace, artifact generation, image, metadata or
   analysis session, and borrowed capability before returning or propagating
   cancellation or an unexpected exception, and never returns success when
   close reports incomplete cleanup.

The result is scoped to the exact package plus selected asset. It preserves the
retained-content generation, frozen selection, pattern, and producer evidence
that participated in the verdict. Display text is never used to reconstruct
any of those joins.

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

## Adjacent owners

| Owner | Supplies to this evaluator | Remains outside this contract |
| --- | --- | --- |
| [Package adapter](artifact-acquisition-and-workspaces.md#sparse-selected-assembly-projection) | Exact `PackageRootBinding`, frozen compile and implementation asset selection, content-generation identity, selection identity, and the closed sparse-projection outcome carrying one canonical asset, one-participant group, exact participant, and `IdentityDecoded` signal when available | Source authorization, download, archive admission, cache policy, TFM/RID reduction, asset ordering, package provenance, and artifact materialization |
| [NuGet package structure](../nuget-package-structure.md) | Compile and implementation role meanings | A new package-layout interpretation |
| [Assembly inspection query](assembly-inspection-query.md) | Package-neutral managed-image classification and typed Metadata query results | Package identity, package selection, or corpus policy |
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
obtains both views from the same retained `AssemblyImageSnapshot`; neither
binding may retain its view or session.

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
package coordinate
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
selected-asset context containing the exact realized package coordinate,
opaque `PackageContentGenerationIdentity`, `PackageRootSelectionIdentity`,
requested runtime identifier, selection-relative asset occurrence identity,
contained selected-asset evidence, role, pattern identity and validated
operand, semantic producer identity, and the number of selected siblings that
were not evaluated. Match evidence and non-match discrimination compose with
that context rather than repeating or reconstructing it.

The two process-local identities contain no content or opening authority; they
preserve current-run correspondence only. For compile-surface evaluation the
unevaluated-sibling count is
`Assets.Count - 1`; for implementation-body evaluation it is
`ImplementationAssets.Count - 1`. It is scoped to the selected TFM and RID and
does not count assets outside those selected role sequences. The receipt
therefore cannot be read as an exhaustive package-wide verdict.

Those `Count - 1` formulas apply only after an asset from the corresponding
sequence has been selected for evaluation. When the primary compile asset has
no implementation counterpart, no implementation asset was evaluated, so the
non-applicable outcome reports the full `ImplementationAssets.Count`.

The process-local identities are receipts for current-run correspondence, not
portable cache keys. A later Workspace transition uses the exact package
coordinate and reacquires or independently reuses authorized content. It does
not adopt a disposed evaluation stream or reconstruct a generation token.
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
  `PackageContentGenerationIdentity`, `PackageRootSelectionIdentity`, pattern
  request, declared role, and typed `NoCompileAssets`,
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
| `Available` with `IdentityDecoded` | Attempt ordinary snapshot acquisition through the exact returned group and participant. A typed rejection becomes `Failure(ImageAdmission)`; only a ready snapshot may reach the prefilter or semantic producer. |
| `Available` without `IdentityDecoded` | Attempt ordinary snapshot acquisition with an evaluator no-op callback, map its typed rejection to `Failure(ImageAdmission)`, and do not invoke the prefilter or semantic producer. An unexpectedly ready snapshot is `Failure(ProjectionContractViolation)` because the rejection-carrier identity is not semantic evidence. |
| `InvalidBinding` | Return `Failure(InvalidBinding)`. This remains a defensive parent outcome even if the first immutable binding implementation cannot currently produce it. |
| `InvalidSelectedAsset` | Return `Failure(ProjectionContractViolation)`. The evaluator supplied a canonical object from the same binding, so this arm indicates a composition defect rather than package-authored content. |
| `SelectedEntryUnavailable` | Return `Failure(SelectedEntryUnavailable)`. |
| `EntryByteLimitExceeded` | Return `Failure(SelectedEntryByteLimit)`. |
| `ArtifactPublicationFailed` | Return `Failure(ArtifactPublication)` while preserving each artifact-owner failure kind and diagnostic code. A bounded presentation diagnostic is derived from that typed evidence. |

The evaluator does not reproduce the package owner's missing-entry sentinel,
manifest preflight, observed-copy limit mapping, aggregate byte partition, or
cleanup rules. Those mechanics and their gates remain wholly with the sparse
projection.

## Selected-entry and image lifetime

The package adapter has already admitted the archive and frozen the package
selection. Evaluation adds a narrower candidate-scoped realization:

1. Validate the binding, pattern, asset intent, and budget before requesting
   artifact materialization.
2. Resolve the selected asset only through the frozen selection.
3. Create a candidate-scoped asynchronous workspace.
4. Ask the package adapter to project only that asset into one bounded artifact
   generation and one-participant group.
5. Consume the projection's exact participant and `IdentityDecoded` signal.
   When identity was not decoded, attempt ordinary snapshot acquisition,
   preserve `AssemblyImageSnapshotResult.Rejected.Failure`, and stop without
   invoking either executable binding. An unexpectedly ready snapshot is a
   projection contract violation.
6. When identity was decoded, request one workspace snapshot scope. Preserve
   any typed snapshot rejection as image-admission failure. For a ready
   snapshot, give the optional prefilter a borrowed `AssemblyImageView` and the
   semantic producer an `AssemblyInspectionSession` over that same
   `AssemblyImageSnapshot`.
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

Base assembly admission runs before the prefilter, so native content, managed
modules, unsupported Windows Metadata, and malformed PE/CLR headers cannot
become byte-level `NoMatch` outcomes. Deeper malformed structures are visible
when the selected semantic traversal encounters them; the evaluator does not
claim to exhaustively validate unrelated metadata that a proven prefilter
skips.

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
- semantic work in the producer's declared unit; and
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
before semantic evaluation, and at producer-owned traversal checkpoints. It
closes the candidate and inspects the cleanup report, then propagates
cancellation to the enclosing stream or host operation. No result is published
after cancellation is observed.

An unexpected exception from a prefilter or semantic binding follows the same
cleanup rule. Candidate cleanup runs in `finally`; a cleanup failure is
attached as secondary evidence and cannot replace the original exception.

`InspectionWorkspace.CloseAsync()` may complete normally while reporting
direct-group release failure in `Groups` or artifact-session release failure in
`ArtifactSessionCleanupFailures`. The evaluator owns a fresh workspace with
exactly the one direct group returned by the sparse projection, so a successful
close report must contain exactly one
`InspectionWorkspaceDirectGroupCloseResult` with `Succeeded == true` and no
artifact-session cleanup failures. A missing, additional, coordinated, or
otherwise unexpected group result is a close-report contract failure rather
than a result the evaluator tries to reinterpret.

The evaluator applies one precedence rule to the whole report:

- after a would-be `Matched` or `NoMatch`, any failed or unexpected group result
  or artifact-session cleanup failure replaces the success with
  `Failure(CandidateCleanup)`;
- after a typed evaluation failure, cleanup evidence is appended as secondary
  evidence without replacing the primary failure stage; and
- during cancellation or unexpected exception propagation, cleanup exceptions
  are attached as secondary evidence to the primary condition.

Candidate-cleanup evidence is resource-free and product-authored. It contains a
bounded sequence of distinct cleanup stages and counts:

- **GroupRelease** for an unsuccessful direct-group result;
- **ArtifactSessionRelease** for reported artifact-session cleanup failures; and
- **CloseReportContract** for a missing, additional, coordinated, or unknown
  group result in this one-direct-group workspace.

It carries no exception instances, messages, paths, or stack traces.
Report-valued direct-group failure is not described as a thrown exception.
`CloseAsync()` faults only when group-close orchestration itself faults; after
the workspace owner attempts all possible artifact-session releases, that
close exception propagates as the primary condition or attaches secondarily to
an already-propagating cancellation or exception.

The operation deadline is implemented by cancelling that same operation token.
Deadline expiry is therefore cancellation, not a separate candidate failure.

This linear one-candidate operation adds no independent concurrent state
machine. Bounded concurrency and result ordering belong to the later streaming
pipeline; no TLA+ model is required for this contract.

## Outcome algebra

One completed evaluation returns exactly one resource-free outcome:

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
content-generation identity, selection identity, pattern request, declared
role, and typed package-selection reason. They do not invent an asset context.

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
- image admission carries the exact Metadata-owned `CandidateOpenFailure`;
- semantic decode and unsupported-input failures carry the producer-owned
  typed failure;
- semantic work limit carries the producer-owned budget kind, admitted limit,
  and charged work when available; and
- candidate cleanup carries the bounded sequence of close stages and reported
  counts.

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

Opening a match starts the standard typed Workspace transition from its exact
realized package coordinate. The transition may reacquire content or use an
independently authorized cache generation. It never adopts the evaluator's
disposed image or assumes that a process-local generation identity is durable.

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
4. raw bytes containing the operand in an unrelated region while semantic
   evaluation returns `NoMatch`;
5. a semantic match in every encoding or indirection covered by an enabled
   prefilter;
6. a selected assembly at the exact byte limit and one byte above it;
7. a selected asset whose assembly name, TFM, and package path contain control,
   format, bidirectional, or markup-significant text;
8. producer evidence whose metadata name or decoded literal excerpt contains
   the same hostile text classes;
9. native, module, unsupported Windows Metadata, and malformed managed input;
10. a selected entry that cannot be materialized from the retained content
   generation;
11. semantic work reaching its exact limit and exceeding it;
12. cancellation during sparse projection and semantic traversal; and
13. a successful or throwing prefilter or producer whose candidate close also
    reports direct-group or artifact-session cleanup failure.

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
| `PackageAssemblyEvaluation_MissingRoleIsDistinctFromNoMatch` | Empty selection and missing implementation correspondence return typed `NotApplicable` outcomes with the exact generation and selection identities; counterpart absence also preserves the primary compile occurrence without inventing an implementation asset. |
| `PackageAssemblyEvaluation_SelectedAssetEvidenceIsContained` | The complete public outcome closure rejects `PackageCompileAsset`; hostile package-authored assembly name, TFM, and path text become `InertString(TextPolicy.Field)` at result construction, while exact occurrence identity uses only the frozen asset-sequence kind, ordinal, and selection identity; evaluation role remains separate. |
| `PackageAssemblyEvaluation_UsesSparsePackageArtifactProjection` | One evaluation calls the sparse selected-asset projection rather than the full package-role realization path. |
| `PackageAssemblyEvaluation_MapsSparseProjectionOutcomesExactly` | Every package-owned projection arm maps as declared without reopening content, parsing diagnostics, or turning projection failure into semantic no-match. |
| `PackageAssemblyEvaluation_RequiresDecodedIdentityBeforeSemanticBinding` | A rejection-carrier participant is opened only through ordinary snapshot acquisition with a no-op evaluator callback; its typed rejection is preserved, an unexpectedly ready snapshot is a contract failure, and neither executable binding runs. |
| `PackageAssemblyEvaluation_ReadyIdentityCanStillRejectSnapshot` | A decoded participant whose snapshot acquisition later rejects preserves the exact `CandidateOpenFailure` and cannot become semantic no-match. |
| `PackageAssemblyEvaluation_SuppliesSparseProjectionBounds` | The evaluator passes the admitted entry and aggregate retained-image bounds unchanged and maps the package owner's typed limit outcome without restating its partition mechanics. |
| Conditional producer prefilter gate | Each prefilter-bearing adoption derives fixtures from its complete declared representation set, obtains byte and semantic views from the same snapshot, admits every semantic match, and requires semantic confirmation for false byte positives. A prefilter-free adoption needs no such gate. |
| `PackageAssemblyEvaluation_MapsProducerVerdictsExactly` | Match, no-match, bounded-decode rejection, unsupported input, work limit, and invalid match evidence map to their declared distinct outcomes without collapsing failure into semantic no-match. |
| `PackageAssemblyEvaluation_PreservesExactCorrespondence` | Execution consumes #5798's exact selected-asset projection for the coordinate, content generation, selection, and canonical asset; the resource-free receipt preserves both process-local correspondence identities with the package/asset context, sibling count, pattern, and producer evidence. |
| `PackageAssemblyEvaluation_FailureCarriesTypedContext` | Preselection failure carries exact selection context without an invented asset; every post-selection failure carries the complete selected-asset context and its declared owner-typed stage payload rather than relying on presentation text. |
| `PackageAssemblyEvaluation_ReleasesResourcesOnEveryOutcome` | Preprojection outcomes create no candidate resources; every resource-bearing match, non-match, failure, work-limit, cancellation, and unexpected binding-exception path enters `finally`, closes its query and workspace, and attempts participant and artifact release. Throwing prefilter and producer fixtures prove preservation of the primary exception when close also fails. |
| `PackageAssemblyEvaluation_CloseReportCannotReturnSuccess` | The fresh candidate close report must contain exactly one successful direct-group result and no artifact-session cleanup failures. Successful producer fixtures independently cover direct-group failure, artifact-session failure, and unexpected group-result shape; each becomes `Failure(CandidateCleanup)` with distinct stage evidence. An existing typed failure retains its primary stage with bounded secondary cleanup evidence; cancellation and unexpected exceptions retain their primary propagated condition. |
| `PackageAssemblyEvaluation_FailuresRemainVisibleAndInert` | Malformed, unsupported, oversized, and disappearing selected entries produce typed inert failures rather than empty success or package-authored diagnostics. |
| `PackageAssemblyEvaluation_ResultClosureIsResourceFree` | The full gate reflects the public transitive closure of every request and outcome and rejects prohibited resource or authority types. |
| `PackageAssemblyEvaluation_OneRequestProducesOneOutcome` | Normal completion returns exactly one outcome and cancellation or unexpected failure cannot also publish one. |
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

1. Lock this focused design and transfer the promoted-tier responsibility from
   the Package Query CLI proposal.
2. Implement the merged #5798 sparse-projection contract and its named gates,
   preserving the exact Root binding and avoiding full role realization.
3. Define the first concrete producer under #5795, including its exact
   semantic occurrence, bounded operand, and optional UTF-16LE prefilter proof.
4. Implement the one-candidate evaluator and its structural gates in
   `DotnetInspector.PackageQueries`.
5. Adopt the first producer, adding its semantic gate and a prefilter gate only
   when that producer declares a complete representation set.
6. Measure a pinned package corpus and choose explicit selected-entry,
   retained-image, producer-working-set, and semantic-work bounds.
7. Compose the evaluator into the bounded Package Query event stream, with
   candidate scheduling and disposal owned by a separate focused pipeline
   slice.
8. Add CLI and Browser gestures in their respective owners.
