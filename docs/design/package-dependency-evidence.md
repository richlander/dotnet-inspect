# Package input and dependency evidence

This document owns the host-neutral L1 package input shape. It preserves the
common declarations and produced relationships stated by authored projects,
package manifests, restored project graphs, and runtime dependency manifests,
without making transport or policy learn each artifact format.

**Status:** evolution contract for
[#6266](https://github.com/richlander/dotnet-inspect/issues/6266). The
package-manifest and restored-project declaration, comparison, graph,
package-prefix admission, and failure core is implemented under #5533 as
`PackageDependencyEvidenceQuery`. The current query also implements the larger
input-kind, declaration-basis, authorship, produced-relationship, positive
processing, and independent phase-count vocabulary for package-manifest and
restored-project inputs. The authored-project facts provider and normalized
adapter are implemented by `AuthoredProjectDependencyFactsQuery` and this
query. The runtime provider and normalized adapter are implemented by
`RuntimeDependencyFactsQuery` and this query. Policy composition and host
adoption remain staged work. Restored-project inputs consume typed
pruning-processing evidence from their artifact owner. Optional owner
observations remain dependent on issue #5315.

## Owner

The **Package Input and Dependency Evidence Query** in
`DotnetInspector.Queries` owns:

- semantic input kind and declaration basis;
- normalization of owner-issued declared-dependency observations;
- declaration authorship;
- additive produced package relationships associated through owner-issued
  identities;
- separation of requested constraints from resolved coordinates;
- positive evidence that upstream package or runtime processing ran;
- optional owner observations associated with canonical package identity;
- stable evidence identity and deterministic result ordering;
- a closed result algebra that preserves owner-issued failures and defines
  normalization failures;
- independent phase and admitted-root-set completion; and
- the equivalence projection shared by package, nuspec, authored-project,
  restored-project, runtime-dependency, and package-prefix adapters where their
  evidence is comparable.

This is an in-place evolution of the existing owner and result, not a second
normalization layer. `PackageDependencyEvidenceQuery` and its public types
remain the implemented subset until staged changes broaden them. No parallel
package-input snapshot may duplicate their declaration, relationship,
identity, failure, or completion semantics.

The query consumes owner-issued typed input. It does not accept a filesystem
path, package source client, cache, CLI option, Markout model, or output
callback.

The query has no external effects. Its work is bounded by the admitted roots,
declarations, relationships, processing observations, and supplied
enrichments. Network, filesystem, parsing, project evaluation, restore, and
retry costs are declared by the owners that construct those inputs.

## Consumers and delivery

The existing concrete product consumers are the CLI dependency experience
tracked by issue #5534 and the inspect-web Browser/Wasm dependency experience
tracked by issue #5535. The focused query implementation is #5533, and #5532
joins those consumers to restored-project facts from #5314, direct-nuspec
identity from #5316, and optional package-owner evidence from issue #5315.

The first policy consumer of the larger shape is the package-pruning
composition required by
[#6228](https://github.com/richlander/dotnet-inspect/issues/6228). That policy
combines this owner-issued shape with the independently owned platform prune
inventory. It does not move pruning policy into this owner.

Issue #6266 is the end-to-end tracker for the larger shape. Its eight delivery
steps are:

1. Lock this evolution of the existing owner (complete in #6270).
2. Add input kind, declaration basis, authorship, normalized produced
   relationships, processing evidence, and independent phase counts to the
   immutable result (implemented by the current query).
3. Add typed pruning-processing evidence to restored-project facts
   (implemented by the current query).
4. Add typed authored-project facts and their normalized adapter.
5. Add a typed runtime-dependency provider for `.deps.json` and adapt its
   facts into this owner.
6. Adopt the shape in package-pruning policy.
7. Adopt the composed policy result in the CLI dependency experience.
8. Adopt the same result in inspect-web Browser/Wasm.

Each provider and consumer adoption remains a focused effort owned by that
component. This document specifies only this owner's immediate typed input and
output obligations.

This query is shared host-neutral substrate, so no single-consumer or
single-host exception applies. Its complexity is justified by preserving one
policy input across package, nuspec, authored-project, restored-project,
runtime-dependency, and package-prefix inputs without separate identity,
completion, failure, authorship, or processing semantics in each host.

Issue #5533 does not itself create a production-visible command. The first
production user is the CLI adoption in #5534; #5533 remains incomplete as a
delivered product experience until that consumer lands.

The [Package Dependency Traversal Query](package-dependency-traversal.md)
consumes one root's selected normalized group and declaration identities. For
transitive manifests it invokes this evidence owner again after exact
acquisition and projection. Those traversal-local subjects do not mutate the
explicit-root evidence outcome or add rows to its root-scoped declaration
universe. A traversal adapter carries typed framework intent and expansion
authority separately; it never reuses this result's inert requested-framework
spelling as structural input.

The query preserves structured typed evidence rather than rendering it. The
CLI consumer uses Markout as the default host-neutral lowering and projects
JSON-family formats from the same typed information. The browser consumer
bypasses Markout only for its host-specific interactive DOM presentation,
consuming the same typed/wire information through the Browser/Wasm boundary.
That browser path owns gestures and component state, not dependency
normalization or identity.

## The question

The query answers:

> For these admitted package inputs, which package dependencies are declared,
> which produced package relationships remain, what target, authorship,
> requested-constraint, resolved-coordinate, and processing evidence is
> available, and how complete is each part of that answer?

That question is intentionally neutral. An owner set that does not contain
`Microsoft` is evidence about one owner predicate; it is not an intrinsic
classification of the dependency as "third party." Unknown owner data is not
evidence that an owner is absent.

## Contract shape

```text
typed admitted roots
  + semantic input kind and declaration basis
  + declared dependency observations
  + optional produced package relationships
  + positive processing observations
  + optional owner observations (after #5315)
  + acquisition completion and failures
        |
        v
Package Dependency Evidence Query
        |
        v
immutable outcome
  - roots
  - input kind and declaration basis
  - declared dependency evidence
  - declaration authorship
  - additive produced relationships
  - independent requested constraints and resolved coordinates
  - processing evidence
  - independent phase availability and completion
  - owner observations (after #5315)
  - typed failures
  - completion
```

The result is one immutable snapshot. A consumer may project package rows,
declaration rows, produced relationships, processing observations,
unknown-owner rows, failures, or summary data from that same snapshot.
Selecting one projection must not rerun acquisition or change the meaning of
another.

## Immediate typed inputs

An adapter admits a root only after its owning component has established the
input's identity and typed facts.

### Input kind and declaration basis

Input kind states the semantic altitude of the admitted evidence:

| Kind | Provider-issued evidence | Evidence altitude |
| --- | --- | --- |
| Authored project | Provider-issued project declarations projected from syntax or evaluation | Before restore or pruning |
| Package manifest | Validated direct-nuspec, package-archive, or source-manifest facts | Before application restore or pruning |
| Restored project | Exact `project.assets.json` facts | After package restore processing |
| Runtime dependency manifest | Exact `app.deps.json` facts | After runtime dependency projection |

Input kind is not acquisition provenance. A direct nuspec and a nuspec read
from a package archive have the same semantic kind while retaining distinct
source provenance. An authored-project input consists of typed project
declarations, not a `.csproj` path.

Separately, the current L3 dependency-evidence gesture may accept a project
path solely to locate existing `project.assets.json`. On that path the project
file is not interpreted as package input: only the selected assets bytes enter
L1, and the result is a restored-project input. The project path remains
locator provenance outside this shape.

Declaration basis states what fidelity the declaration phase can claim:

- **Package manifest** -- package-authored declarations from validated manifest
  facts;
- **Authored project syntax** -- declarations projected from supported project
  syntax without claiming effective MSBuild evaluation;
- **Evaluated project** -- declarations from an owner-issued project
  evaluation;
- **Restored project** -- project declarations retained in restore output; or
- **Not applicable** -- the input kind does not carry declaration evidence.

Completeness is relative to the stated basis. A complete authored-syntax
projection does not become a complete effective project declaration set.
Policies that require effective declarations accept only an adequate basis or
return their own unknown or not-applicable result.

Input kind and declaration basis are owner-issued structural currency. They are
not reconstructed from a path extension, source label, or the presence of a
particular output row.

The implemented CLR vocabulary keeps one `PackageDependencyEvidence*` family:
`PackageDependencyEvidenceInputKind`,
`PackageDependencyEvidenceAcquisitionForm`,
`PackageDependencyEvidenceDeclarationBasis`,
`PackageDependencyEvidenceAuthorship`,
`PackageDependencyEvidenceRelationship`,
`PackageDependencyEvidenceRelationshipResult`,
`PackageDependencyEvidenceProcessingObservation`,
`PackageDependencyEvidenceProcessingResult`, and
`PackageDependencyEvidencePhaseCounts`. Input kind and declaration basis are
derived from matching root identity and provenance rather than independently
settable discriminators.

### Package manifest

The package adapter supplies:

- the validated `PackageSourceCoordinate`;
- a `PackageDependencyGroupsQuery` outcome over validated
  `PackageManifestFacts`;
- source and acquisition provenance when available; and
- the typed completion or failure that governed admission.

One admitted `PackageProfileMatch` supplies the same package-manifest input
with its `PackageSourceResultIdentity` retained as source provenance.

Package archive and direct nuspec inputs produce the same dependency evidence
when the archive-extracted and direct nuspec content produce the same
manifest facts, group selection, and selection status. Their acquisition and
identity trust provenance may differ. #5316 extends the manifest-facts owner
with typed self-attested identity for direct nuspec content; the adapter does
not parse nuspec identity independently.

`PackageManifestFactsQuery` remains the owner of bounded XML projection,
manifest identity validation, dependency-contract validation, and
`PackageManifestFailure`. `PackageDependencyGroupsQuery` remains the owner of
declared groups, exact target-framework selection, implicit manifest-group
identity, and the distinction among selected, no dependency groups, and no
matching target framework. The evidence query preserves those states; it does
not turn either absence state into an empty selected group. Its
`DependencyFrameworkScopeIdentity` never recomputes that selection:
`SelectedGroupIndex` identifies the exact owner-issued source occurrence,
which normalization maps to one logical group. The canonical framework
identity serves result comparison.

When no target framework is requested,
`PackageDependencyGroupsQuery` selects one group by its package-group priority,
using source order to break equal-priority ties. That is a per-manifest
default, not a claim that several manifests share one target framework.
This exact no-request query path is currently unverified. The package traversal
implementation must add
`Traversal_ManifestDefaultUsesOwnerNoRequestSelection` before adopting it.

### Restored project graph

The restored-project adapter supplies owner-issued typed facts from one
already-acquired `project.assets.json` selection:

- one owner-issued restored-project selection identity;
- the selected target framework and optional runtime identifier;
- admitted root identities;
- owner-issued logical declaration-group identities and order;
- project-authored direct dependency observations grouped by their authored
  project target framework;
- optional resolved coordinates and direct/transitive graph roles; and
- independent typed declaration-projection and restored-graph
  availability/completion/failure.

The adapter never supplies a project path to L1. A host may accept a project
path solely to locate existing `project.assets.json`, but only the selected
assets bytes are restored-project input. Locating and reading that file, and
reporting not-restored or not-found states, remain upstream responsibilities.
The query neither interprets the project file, evaluates MSBuild, nor initiates
restore or build. #5314 owns the claim that a project-path locator and a direct
assets path selecting the same bytes produce equivalent restored facts.

The current mutable, path-taking `ProjectAssetsParser` result does not satisfy
this query's input obligation. The construction, validation, identity, and
failure semantics of the replacement input belong to the focused Restored
Project Dependency Facts Query in #5314.

### Authored project

The authored-project adapter consumes one complete
`AuthoredProjectDependencyFactsResult`, not project XML or a path. Its sole
acquisition form is `ProjectXml`, meaning already-acquired XML content was
projected by the authored-project owner. `ProjectLocator` remains exclusive to
the convenience path that locates `project.assets.json` and therefore produces
a restored-project input.

An available or incomplete provider result becomes one admitted root:

- root identity is the provider-issued `AuthoredProjectIdentity`;
- content provenance is the provider-issued
  `AuthoredProjectContentProvenance`;
- input kind is `AuthoredProject`;
- declaration basis is `AuthoredProjectSyntax`;
- selection is unavailable because this adapter performs no target selection;
- produced relationships are not applicable; and
- processing is not applicable because authored syntax is pre-processing
  evidence.

A failed provider result has no established project identity or provenance. It
therefore becomes one typed failed root and never a successful empty,
unavailable, or anonymous admitted root.

The adapter forms logical declaration groups from owner-issued target
observations and declaration conditions:

- unconditional declarations occupy one any-framework group;
- exact literal targets and exact target conditions occupy their canonical
  exact-framework group;
- unrecognized literal targets occupy an opaque unrecognized-framework group;
- expression-bearing targets and unresolved conditions occupy an opaque
  unresolved-framework group; and
- every target observation contributes its source occurrence even when its
  group has zero declarations.

Groups with equal semantic scope coalesce while retaining every owner-issued
target or declaration occurrence. Unconditional declarations are not copied
into observed target groups: doing so would infer evaluated MSBuild
applicability. No requested target is selected.

Only declarations for which the provider established both canonical package
identity and canonical requested version constraint become normalized rows.
They retain provider source spellings and occurrence counts and are
`ApplicationAuthored`. The adapter does not reparse package IDs, version
constraints, target expressions, or conditions. Provider limitations become
typed declaration failures, and opaque unresolved dependency-syntax
identities remain typed failure evidence. Independently usable rows therefore
survive while the declaration phase remains incomplete. A complete provider
result produces a complete declaration phase, including valid complete-empty
evidence.

This adapter implements authored **syntax** only. `EvaluatedProject` remains a
distinct declaration basis for a future owner-issued evaluated-project
provider. This query does not evaluate MSBuild, initiate restore, or upgrade a
syntax projection to evaluated evidence.

### Runtime dependency manifest

The runtime-dependency adapter consumes one complete
`RuntimeDependencyFactsResult`, not bytes or a path. Its sole acquisition form
is `RuntimeDependencyManifest`, meaning already-acquired `.deps.json` bytes
were projected by the runtime-dependency owner.

An available provider result becomes one admitted root:

- root identity is the provider-issued `RuntimeDependencyRootIdentity`;
- content provenance is the provider-issued
  `RuntimeDependencyContentProvenance`;
- the exact provider-issued `RuntimeDependencyTarget` remains associated with
  the root, is mutually exclusive with restored-target evidence, and must agree
  with the target identity carried by the runtime-manifest identity;
- input kind is `RuntimeDependencyManifest`;
- declaration basis is `NotApplicable`;
- declaration-group selection is unavailable;
- package nodes and package-resolving relationships retain provider-issued
  identities and exact resolved coordinates;
- requested constraints, direct/transitive roles, and declaration
  associations remain absent;
- a package parent is `LibraryDeclared`, while an opaque non-package parent is
  `Unattributed`;
- graph failures remain typed relationship failures and independently make the
  relationship phase incomplete; and
- processing is complete positive evidence of
  `RuntimeDependencyProjection`, independently of relationship completion.

A failed provider result has no established runtime-manifest identity, target,
or provenance. It therefore becomes one typed failed root and never a
successful empty, unavailable, or anonymous admitted root.

The adapter does not reparse package IDs, versions, runtime targets, parent
identities, or failure evidence. A valid empty provider graph remains a
complete-empty relationship phase, distinct from provider failure or
incomplete usable graph evidence.

Framework assemblies absent from a framework-dependent application's runtime
manifest are outside that manifest's package-library set. Their absence is not
declaration evidence, restored-graph evidence, or proof that package pruning
removed them.

### Package-prefix root set

The package-prefix adapter supplies admitted package roots and the terminal
completion from `PackageProfileQuery`. Search, manifest acquisition, candidate
bounds, source pagination, and producer contract validation remain owned by
the package source and profile query.

A truncated root set is usable bounded evidence, not an exhaustive package
universe. The evidence query preserves that completion and never manufactures
an exact prefix total. The normalized root-set summary retains the package
source identity, inert prefix spelling, candidate/match/failure counts, and
exact `PackageSearchTruncationReason`.

Package-profile failures retain the owner-issued failure kind, source identity,
and optional manifest-failure reason. Package ID, version, and diagnostic text
are contained as `InertString` during adaptation rather than crossing the
result boundary as raw producer text.

### Owner evidence

The future #5315 adoption supplies optional owner observations through its own
typed input owner. This query does not call a metadata source or perform owner
lookups. #5315 owns the separate bounded Package Owner Evidence Query needed to
construct those observations by canonical package identity.

## Common declared evidence

One normalized declared observation states:

- which admitted root made the declaration;
- which normalized logical declaration group contains it;
- the canonical dependency package identity;
- available declaration target-framework scope;
- the NuGet version constraint; and
- application, library, or unattributed authorship; and
- retained source spellings and duplicate-count provenance.

Canonical package identity, the framework-scope identity defined below, and
NuGet version semantics are used for joins and equivalence. Display spellings
are evidence, not identity.

The query emits at most one successful row per root, logical group, and
dependency identity. Semantically duplicate declarations with the same
constraint inside one logical group collapse into that row and retain their
source occurrence count as provenance. Repeated declarations with conflicting
constraints inside one logical group produce a typed
conflicting-declaration failure; they are not ordered into apparently valid
rows.

Every owner-issued declaration-group occurrence contributes to normalized
evidence. A requested framework selection maps one owner-issued source
occurrence to its logical group within that complete set; it does not mark
every group with an equal canonical scope and does not discard unselected
groups. The result preserves selected, no dependency groups, and no matching
target framework as separate selection states.

Authorship is one of:

- **Application-authored** when the provider proves that a project owns the
  declaration;
- **Library-declared** when a package manifest or equivalent package-owned
  declaration owns it; or
- **Unattributed** when usable declaration evidence cannot prove either
  classification.

Unattributed is not silently treated as either known class. Direct and
transitive are produced-graph roles, not authorship. A root relationship is not
application-authored merely because it is direct, and a relationship reached
through a project node is not library-declared merely because it is transitive.

### Logical declaration groups

Package manifest parsing may split top-level ungrouped dependencies into
multiple implicit occurrences when explicit groups are interleaved. Those
occurrences are one logical implicit universal group. The query coalesces every
`IsImplicitManifestGroup` occurrence for one root before duplicate collapse,
conflict detection, row construction, and equivalence. It retains constituent
source occurrence identities and order as provenance.

If `SelectedGroupIndex` names any constituent implicit occurrence, selection
maps to the coalesced logical implicit group while retaining the selected source
occurrence. This normalization rule is independent of the current package
selection implementation; XML interleaving cannot change success, failure, or
selected declarations.

Each explicit manifest group remains a distinct logical group occurrence even
when two explicit groups or an explicit and implicit group have equal framework
semantics. They may therefore declare the same dependency under different
constraints without becoming a normalization conflict.

Restored-project facts supply their already-logical authored framework groups.
This query does not merge those groups merely because their declaration sets
or framework scopes compare equal. One logical group is supplied for every
authored project target framework, including a framework with zero direct
package declarations.

The common projection contains only facts both package manifests and restored
project graphs can state as declarations. It excludes:

- a resolved dependency version;
- direct versus transitive graph role;
- selected runtime identifier;
- package-cache or source path;
- compile, runtime, resource, analyzer, or build asset selection; and
- transitive closure.

Those facts may be valuable, but they are additive resolution evidence rather
than a reason for equivalent declarations to produce different common rows.

### Framework scope

Declaration scope is one of:

- **Any framework** — a scope with `NuGetFramework.AnyFramework` semantics,
  whether represented by an implicit manifest group or an explicit universal
  group;
- **Exact framework** — a parseable full target framework, including platform
  and platform version when present;
- **Unrecognized framework** — a retained owner-issued token that cannot be
  assigned NuGet framework semantics; or
- **Unresolved framework** — an owner-issued target expression or declaration
  condition whose framework applicability cannot be established without
  evaluation.

An explicit manifest group whose target-framework attribute is present but
empty has `Any framework` semantics, matching NuGet's universal dependency-
group behavior. The empty authored spelling remains separate presentation
evidence.

This query owns construction of `DependencyFrameworkScopeIdentity` for its
normalized rows. Exact identities use NuGet target-framework parsing semantics
and canonical short-folder spelling that retains platform and
platform-version identity. Alternate casing and long/short spellings therefore
compare by framework semantics. Platform-qualified identities remain distinct.
The package adapter recognizes the owner-issued universal tokens `any` and the
empty explicit group before framework parsing. All other exact-framework
construction reuses the restored-facts owner's
`NuGetTargetFrameworkIdentity` admission boundary; the composition query does
not repair a second framework identity from display text.
Unrecognized and unresolved scopes retain distinct opaque identity and inert
display evidence but are comparable only within one evidence family: matching
kind and opaque identity compare equal under same-owner parity, while neither
opaque identity is comparable across declaration-facts owners. Whether a
universal group was implicit or explicit is retained as group provenance, not
framework identity. This contract names semantics, not a package dependency;
implementation remains NativeAOT- and Browser-Wasm-compatible.

An unrecognized or unresolved scope's opaque identity is internal comparison
state, not renderable artifact text. Sinks receive its kind and `InertString`
display evidence; they do not serialize or render the raw identity token.
Presentation projections replace an order key containing opaque identity with
a document-stable ordinal key while retaining normalized ordering internally.

The selected restored target framework is resolution context, not a substitute
for an authored declaration scope the input owner did not supply. Such an input
has incomplete declaration projection. This query does not infer framework
compatibility or claim that a `netstandard2.0` declaration has `net8.0`
authored scope because it participated in a `net8.0` restore.

For one explicitly paired root from each outcome, the common result supports
two projections:

- **Core declaration** — the multiset of that root's logical-group canonical
  declaration-set signatures with framework scope omitted; and
- **Scoped declaration** — the multiset of that root's logical-group canonical
  declaration-set signatures paired with any/exact framework scope.

Both comparisons return **Equal**, **Unequal**, or **Not comparable**, with a
typed reason. If either paired root has incomplete declaration projection,
including any typed declaration failure, both comparisons return
**Not comparable: declaration projection incomplete**.

Declaration projection is complete when every owner-issued logical group,
including an empty group, is represented; every owner-issued declaration
contributes a normalized row; and no typed declaration failure occurs.
Unrecognized framework scope is a complete declaration projection with
context-dependent scope comparison; it does not prevent core comparison.
Unresolved framework scope appears only with provider-issued incompleteness,
so the declaration comparison is already not comparable. An input that cannot
associate a declaration with any logical group has incomplete declaration
projection instead of an unavailable scope.

Otherwise, core comparison retains logical-group multiplicity and returns
equal or unequal. Scoped comparison returns:

- **Not comparable: framework scope** if the selected scope-comparison family
  cannot compare an unrecognized logical-group identity;
- **Equal** when every scope is comparable and the scoped-signature multisets
  are equal; or
- **Unequal** when every scope is comparable and those multisets differ.

Not comparable conservatively dominates any differences visible in that
paired root's comparable subset. Those subset differences may remain
diagnostic evidence but cannot upgrade the overall result. Unrelated roots in
the same package-prefix outcome and root-set truncation do not participate in
the paired-root comparison.

The query returns one independent comparison result per explicitly paired root.
It defines no outcome-wide aggregate equivalence across multiple root pairs;
that composition belongs to the caller.

Scope comparison names its evidence family:

- **Same-owner parity** compares roots produced by the same declaration-facts
  owner, including package archive versus direct nuspec and a project-path
  assets locator versus direct-assets composition. Matching unrecognized opaque
  identities compare equal; differing unrecognized identities are not
  comparable.
- **Cross-owner declaration comparison** compares package-manifest and
  restored-project facts. Any unrecognized scope makes scoped comparison not
  comparable because the two owners cannot establish shared framework
  semantics.

The query selects the family from the paired roots' owner provenance, never
from a caller flag. This family distinction affects only scoped comparison.
Core declaration comparison remains independent of framework scope. #5314
separately owns whether project locator and direct assets produce the same
restored facts; this query only normalizes those same-owner facts
deterministically.

## Additive produced-relationship evidence

A restored or runtime graph may supply a separate immutable relationship
collection:

- owner-issued stable edge identity;
- parent and dependency identities;
- requested constraint on that relationship when available;
- exact resolved dependency coordinate;
- selected target framework and runtime identifier; and
- direct or transitive role when the provider owns that fact;
- application, library, or unattributed origin; and
- an owner-issued declaration association when that association proves the
  origin.

Absence of an additive fact means unavailable for that input, not false. A
package manifest therefore does not claim that a dependency was unresolved or
non-transitive merely because it cannot provide a produced relationship.

Multiple parents may carry different constraints to the same resolved
dependency. Those edges remain distinct and never become conflicting
root-authored declarations. A direct edge may be correlated with a normalized
declaration through typed identities when the restored-facts owner establishes
that correspondence. The query does not infer it from package labels, rendered
version text, row positions, or local paths.

Requested constraint and resolved coordinate remain separate typed values. The
same relationship may therefore retain `[8.0.0, 9.0.0)` and selected
coordinate `contoso.logging@8.0.5`. A provider that has only one fact does not
synthesize the other.

The current restored-project facts prove root-edge correspondence with the
root declaration and prove that package-parent relationships come from package
dependency entries. A relationship leaving a project node remains
unattributed unless a future restored-facts adoption issues a declaration
association for it. This owner does not infer application authorship from the
project parent kind.

Each root carries one produced-relationship state:

- **Not applicable** — the query assigns this to a root with no
  produced-relationship capability, including package/nuspec input;
- **Available, complete** — the full owner-issued graph is present, including
  a valid empty edge collection;
- **Available, incomplete** — bounded edges are usable but the typed completion
  reason proves the graph is partial;
- **Unavailable** — a restored input supplied declarations but its
  provider cannot supply graph evidence under the available capabilities; or
- **Failed** — graph projection failed with typed failure evidence.

The query assigns **Not applicable** only when the semantic input kind excludes
the capability. Otherwise, it preserves the available, unavailable,
incomplete, or failed state supplied by that owner. Relationship
unavailability, incompleteness, or failure never downgrades complete
declaration comparison.

## Processing evidence

Processing stage is **pre-processing** for authored-project and
package-manifest inputs and **post-processing** for restored-project and
runtime-dependency inputs.

A post-processing input retains positive, provider-issued observations. The
initial semantic vocabulary is:

| Semantic | Positive evidence |
| --- | --- |
| Restore resolution | A selected restored target |
| Package-pruning evaluation | The restored target's typed `packagesToPrune` evidence |
| Runtime dependency projection | A typed runtime target graph |

An observation means that the named semantic ran for the associated input and
target on the provider's stated evidence. It does not mean that the semantic
changed an edge. In particular, package-pruning evaluation does not identify
which absent package was pruned.

Observation absence means **not evidenced**, never **not applied**. An assets
file without typed `packagesToPrune` evidence cannot be read as an unpruned
graph. A `.deps.json` graph cannot use missing framework assemblies as pruning
evidence. A provider may state a stronger negative only when its own contract
has positive artifact evidence for that negative state.

Prunable and processed are independent. Declaration authorship and the
consuming policy determine whether an edge may receive a transformation or
exemption; processing observations state which semantics already ran. This
owner therefore defines no `IsPruned` or `IsPrunable` bit.

The restored-project provider associates its pruning evidence with the exact
selected declaration-group identity. A valid empty `packagesToPrune` object is
positive evidence. An absent member remains unavailable, while malformed,
ambiguous, or over-limit evidence becomes a typed provider failure. The
normalized result preserves restore resolution independently: provider failure
makes processing available but incomplete, with the owner-issued failure
retained and no pruning observation manufactured.

Processing retains the same closed state as the other phases:

- **Not applicable** for a pre-processing input;
- **Available, complete** when every processing observation exposed by the
  provider was projected;
- **Available, incomplete** when usable observations remain with typed
  failures;
- **Unavailable** when a post-processing input cannot establish which
  semantics ran; or
- **Failed** when processing evidence cannot be associated soundly with the
  admitted input and target.

## Owner observations

Owner metadata is an optional enrichment over canonical package identity. The
same Package Owner Evidence Query contract supplies it for every input form.

An owner observation is one of:

- **Known** — the producer authoritatively returned an owner set, including a
  known empty set;
- **Unknown** — the selected producer cannot establish owner metadata for that
  identity; or
- **Failed** — an attempted lookup failed with typed producer and failure
  evidence.

Known, unknown, and failed are distinct. Neither unknown nor failed may be
projected as an empty owner set.

After #5315 adoption, the result retains root-owner and dependency-owner
observations independently. It does not apply an owner predicate or emit
`first-party`/`third-party` labels. A later typed predicate may compare a
requested owner identity with these observations; that later operation must
preserve unknown and failed states.

An owner value contains canonical owner identity separately from its
`InertString` display spelling. #5315 supplies an immutable mapping with at
most one observation per canonical package identity, so conflicting
observations are unrepresentable at this boundary. Resolver batching, caching,
source selection, retry, and network policy remain outside this owner.

## Equivalence

Equivalence is defined over typed projections, not rendered JSON or table text.

### Package and nuspec

Package archive and direct nuspec inputs are dependency-equivalent when the
archive-extracted and direct nuspec bytes produce the same manifest facts and
group-selection outcome. The package path uses an independently expected
coordinate; the #5316 direct-content path uses typed self-attested identity.
Acquisition and identity-trust provenance may differ and is compared
separately. Scoped equivalence uses same-owner parity, so matching
unrecognized framework identities remain equal.

### Restored input determinism

Identical restored facts produce the same evidence outcome regardless of
locator provenance. After #5315 adoption, identical supplied owner observations
preserve that result. #5314 separately owns and gates project-path assets
locator versus direct-assets equivalence. If the locator finds no existing
assets file, its adapter supplies a typed upstream failure rather than
permission to interpret, restore, or evaluate the project.

### Package manifest and restored graph

Package/nuspec and restored-project inputs are compared only after a caller
pairs one admitted root from each outcome. The query never infers root
correspondence from display labels. Paired roots are equivalent under a common
declared-evidence projection when their owner-issued typed inputs describe the
same logical declarations.

The comparison:

- uses canonical package identity rather than casing or display spelling;
- uses NuGet version-constraint semantics rather than raw range text;
- applies the core and scoped aggregate rules above;
- preserves logical-group multiplicity;
- distinguishes unequal from not comparable;
- ignores additive resolution evidence and input provenance.

A declaration-set signature is the deterministic set of canonical package
identity and NuGet constraint pairs in one logical group. Logical-group
identity, constituent source occurrence identity, and group order are retained
in each outcome but excluded from cross-input semantic equality.
Selected-group equivalence compares the selected logical group's core or scoped
signature and selection status, not the incidental source ordinal. It returns
not comparable when either paired root's declaration projection is incomplete;
not comparable with **selection status unavailable** unless both roots carry an
owner-issued selection status; equal for matching absence statuses; unequal
for differing statuses; and, when both are selected, applies the same core or
scoped signature rules. Package/nuspec pairs carry that status.
Restored-project roots currently do not, so package/restored selected-group
comparison is not comparable even when their full core or scoped declarations
are equal.

A runtime-dependency root has declaration basis `NotApplicable`. Comparing it
through the declaration-equivalence operation is therefore not comparable with
reason `DeclarationNotApplicable`, distinct from an applicable but incomplete
declaration projection.

Input-specific evidence is asserted separately. A restored graph may therefore
be equal under the declared projection while also reporting resolved versions
and transitive relationships unavailable from the nuspec.

## Identity and ordering

Stable evidence identity is constructed from owner-issued root identity,
normalized logical-group identity, and canonical dependency identity. It is
independent of presentation labels, rendered row position, and duplicate
occurrence count. A conflicting constraint has failure identity rather than
successful row identity.

Package root identity is semantic package coordinate, not a unique collection
occurrence. The same coordinate admitted through package archive, direct
nuspec, or package-source manifest therefore retains equal root identity and
distinct provenance. A sink must retain the root collection occurrence and
provenance rather than key rows only by semantic root identity.

The outcome retains admitted-root, logical-group, and constituent source
occurrence order as provenance and uses a deterministic order within each
logical group:

1. dependency package identity;
2. NuGet version-constraint identity.

Each logical group has a deterministic normalized order key. An explicit group
uses its source occurrence position. The coalesced implicit universal group
uses the minimum position of its constituent source occurrences. Restored facts
supply their owner-issued logical-group order key. Constituent positions remain
separate provenance.

The equivalence projection compares canonical group signatures as a multiset.
It does not rely on XML element order, JSON property order, source relevance
order, logical-group order, or serializer behavior.

## Failure and completion

Failure stays visible at the smallest truthful scope:

- a rejected root does not become an empty root;
- a malformed or invalid manifest retains `PackageManifestFailure`;
- a package-profile search, acquisition, producer-contract, or manifest
  failure retains typed source-scoped failure evidence;
- a missing, unrestored, unavailable, or failed acquisition retains a typed
  content-free root failure;
- an unavailable restored-project selection does not become a project with no
  dependencies;
- an invalid declaration becomes typed declaration failure rather than being
  dropped;
- an invalid or unassociated produced relationship becomes typed relationship
  failure rather than being dropped;
- missing processing evidence does not become proof that processing did not
  run;
- a supplied failed owner observation remains failed for every row associated
  with that canonical identity; and
- a root-set acquisition failure remains separate from enrichment failure.

When a canonical package coordinate is independently known, a failed root
retains it. A failure before package identity is established retains its inert
source label and provenance instead; composition does not invent a coordinate.

The outcome separately reports:

- root-set completion;
- admitted, rejected, and failed root counts;
- package-prefix terminal source, counts, and truncation reason when present;
- per-root semantic input kind and declaration basis;
- per-root declaration projection completion and its aggregate;
- per-root produced-relationship availability, completion, and failure, plus
  its aggregate;
- per-root processing availability, completion, and observations, plus its
  aggregate; and
- owner-enrichment completion after #5315 adoption.

One phase cannot upgrade another phase's completion. In particular, complete
owner enrichment over a truncated package-prefix root set does not make the
prefix exhaustive. Root-set incompleteness does not downgrade declaration
comparison between two already-admitted, individually complete roots.

## `InertString` boundary

Canonical typed identities remain suitable for matching, parsing, and control
flow. `InertString` does not replace them.

Every artifact- or source-derived value intended to cross from the query result
to a sink is an `InertString` no later than result construction, under its
declared `TextPolicy`. Already-treated input is retained rather than treated
again. Package and owner display spellings, original target-framework and
version-range spellings, and source labels use `TextPolicy.Field`. Safe
explanatory evidence uses `TextPolicy.Prose`.

The immutable outcome carries those `InertString` values through L2, JSON
projection, Markout, and other sinks without reconstructing them from raw text.
A sink may tighten policy with `EnsurePermitted`; it must not reacquire the raw
artifact string. Containment metadata and model-field location remain
available for audit.

Identity, provenance, and presentation stay separate:

- joins use canonical owner-issued identity;
- provenance states where the observation came from;
- `InertString` carries the display evidence safely; and
- no identity is inferred from the inert spelling.

Package IDs and other identifiers remain eligible for the separate identifier
confusion audit. Visual containment does not assert that two identifiers are
the same or different.

## Demo

The command spellings below are target mockups. This design does not assign
them to L3.

One fixture expresses the same declarations as a package manifest and a
restored project graph. This mockup uses a direct assets path so the L1 input
is explicit; the current host's separate project-path locator convenience is
not an authored-project input:

```console
$ dotnet-inspect <dependency-evidence> --package Contoso.Root@1.0 --json \
    | jq '.dependencies | map({
        framework: .framework.id,
        dependency: .package.id,
        constraint: .declaredConstraint.canonical
      })'
$ dotnet-inspect <dependency-evidence> --nuspec ./Contoso.Root.nuspec --json \
    | jq '.dependencies | map({
        framework: .framework.id,
        dependency: .package.id,
        constraint: .declaredConstraint.canonical
      })'
$ dotnet-inspect <dependency-evidence> \
    --project ./obj/project.assets.json --json \
    | jq '.dependencies | map({
        framework: .framework.id,
        dependency: .package.id,
        constraint: .declaredConstraint.canonical
      })'
```

```json
[
  {
    "framework": "net8.0",
    "dependency": "contoso.logging",
    "constraint": "[2.0.0, 3.0.0)"
  },
  {
    "framework": "net8.0",
    "dependency": "contoso.options",
    "constraint": "[2.1.0, )"
  }
]
```

The three canonical scoped-declaration projections are equal in the composed
target. Original range spellings remain separate `InertString` evidence and
need not be textually equal. #5314 separately gates that a project-path locator
and a direct assets path selecting the same bytes supply the same restored
facts. The direct-assets outcome also carries produced-relationship evidence
in the same snapshot:

```json
{
  "dependencies": [
    {
      "group": {
        "id": "project:net8.0"
      },
      "framework": {
        "kind": "exact",
        "id": "net8.0",
        "display": "net8.0"
      },
      "package": {
        "id": "contoso.logging",
        "display": "Contoso.Logging"
      },
      "declaredConstraint": {
        "canonical": "[2.0.0, 3.0.0)",
        "display": "[2.0,3.0)"
      }
    }
  ],
  "restoredEdges": [
    {
      "parent": "contoso.root@1.0.0",
      "dependency": "contoso.logging@2.4.1",
      "constraint": "[2.0.0, 3.0.0)",
      "role": "direct",
      "selectedTargetFramework": "net8.0"
    }
  ]
}
```

A broad prefix returns neutral evidence suitable for downstream predicates:

```console
$ dotnet-inspect <dependency-evidence> \
    --package-prefix Microsoft --json > evidence.json

$ jq '
    .dependencies[]
    | select(.rootOwners.state == "known")
    | select(any(.rootOwners.values[]; .id == "microsoft"))
    | select(.dependencyOwners.state == "known")
    | select(any(.dependencyOwners.values[]; .id == "microsoft") | not)
  ' evidence.json
```

The query does not call those rows third party. Unknown and failed owner
lookups remain separately selectable:

```json
{
  "package": {
    "id": "example.unknown",
    "display": "Example.Unknown"
  },
  "dependencyOwners": {
    "state": "failed",
    "producer": "nuget.org",
    "failure": "MetadataAcquisition"
  }
}
```

The larger shape adds input altitude and processing without flattening the
existing declaration and relationship lanes. For an assets file whose selected
target carries typed `packagesToPrune` evidence:

```json
{
  "inputKind": "restored-project",
  "declarationBasis": "restored-project",
  "processing": {
    "stage": "post-processing",
    "observations": [
      "restore-resolution",
      "package-pruning-evaluation"
    ]
  },
  "declarations": [
    {
      "package": "azure.identity",
      "constraint": "[1.17.0, )",
      "authorship": "application-authored"
    }
  ],
  "relationships": [
    {
      "package": "azure.core@1.47.1",
      "origin": "library-declared"
    }
  ]
}
```

`System.Text.Json` may be absent from that produced graph. The shape does not
manufacture a removed edge or claim that its absence proves which relationship
pruning removed. An otherwise equal assets input without typed
`packagesToPrune` evidence retains restore resolution and reports pruning
processing as not evidenced.

**What to notice:** currently implemented input forms feed one declaration
shape; restored inputs add produced relationships without replacing requested
constraints; the larger contract adds basis, authorship, and positive
processing evidence; owner predicates remain explicit; and no missing fact is
laundered into empty or false.

## Evidence and gates

Implementation must establish:

- one normal solution-graph fixture, registered through `FixtureCatalog`,
  whose built package manifest and restored graph express the same declaration
  seed without checking in environment-bound assets;
- deterministic normalization of identical restored facts and enrichments,
  while #5314 gates project-path assets locator and direct-assets equivalence;
- dependency equivalence between package-extracted and #5316 direct nuspec
  facts with distinct identity-trust provenance;
- core and scoped common-projection equivalence between package/nuspec and
  restored inputs;
- equivalent alternate TFM spellings, distinct platform-qualified TFMs, and
  explicit implicit-versus-explicit any-framework, unrecognized, unequal, and
  not comparable cases;
- identical unrecognized framework identities comparing equal for
  package/nuspec same-owner parity but not comparable across
  package/restored owners;
- separate assertions for provenance, resolution, capability, and completion;
- non-vacuity by mutating one declared package identity or version constraint
  and observing core and scoped inequality;
- a framework-identity mutation producing scoped inequality while core
  equality remains unchanged;
- a conflicting duplicate producing both typed declaration failure and
  not-comparable core/scoped results against an otherwise matching complete
  root;
- distinct implicit and explicit universal groups with conflicting constraints
  remaining separate while exact selected-group correspondence is preserved;
- package/nuspec selected-group equivalence and package/restored
  not-comparable selection status;
- interleaved and adjacent implicit dependency runs producing the same logical
  group, normalized group order, conflict outcome, and selected declarations;
- empty logical groups retained in completion and group-signature multiplicity,
  including repeated empty groups and cross-input empty-framework equivalence;
- repeated declaration-set signatures with mixed exact and cross-owner
  unrecognized scopes producing deterministic not-comparable scoped evidence;
- a multi-root prefix outcome whose unrelated incomplete or unrecognized root
  does not poison comparison of two individually complete paired roots;
- root-set truncation retained separately from comparison of already-admitted
  complete roots;
- a diamond restored graph retaining distinct parent edges and constraints;
- complete-empty, incomplete, unavailable, and failed restored-graph states
  remaining visible without changing complete declaration comparison;
- requested constraints and resolved coordinates remaining independent when a
  restored relationship carries both;
- package-manifest declarations classified as library-declared;
- restored root relationships classified as application-authored only through
  owner-issued root-declaration correspondence;
- package-parent relationships classified as library-declared and
  project-parent relationships remaining unattributed without an owner-issued
  declaration association;
- authored-project syntax and evaluated-project declaration bases remaining
  distinct, with completeness interpreted relative to the stated basis;
- authored-project available, valid complete-empty, incomplete, and failed
  provider outcomes remaining distinct;
- authored target-only groups, declaration conditions, source occurrence
  counts, application authorship, and requested constraints remaining visible;
- unconditional authored declarations remaining in their any-framework group
  rather than being copied into observed targets;
- unresolved authored targets and conditions remaining opaque and incomplete
  rather than becoming literal framework or evaluated applicability claims;
- authored exact-framework declarations comparing with equivalent package
  manifest declarations without manufacturing selected-group evidence;
- package-pruning evaluation emitted only from typed `packagesToPrune`
  evidence, with evidence absence remaining unknown;
- `.deps.json` framework-assembly absence never becoming package-pruning
  evidence;
- not-applicable phases remaining distinct from unavailable and complete-empty
  phases;
- hostile text containment at query-result construction, with sink retention
  gated by each later JSON or Markout adopter, including no raw unrecognized
  framework identity at a sink; and
- visible root, declaration, and enrichment failures.

The declaration, comparison, graph, package-profile, root-failure, containment,
and current larger-shape properties are gated in Release by
`PackageDependencyEvidenceQueryTests`. The current larger-shape gates are:

- `Execute_CurrentInputKindsAndDeclarationBasesAreExplicit`;
- `PackageInput_AssetsPruningObservationRequiresTypedPackagesToPruneEvidence`;
- `PackageInput_AssetsWithoutPruneEvidenceRemainProcessingUnknown`;
- `PackageInput_InvalidPruneEvidencePreservesRestoreAsIncompleteProcessing`;
- `Execute_PreservesSelectedRestoredTargetWhenGraphIsUnavailable`;
- `PackageInput_InputKindAndBasisRequireMatchingIdentityAndProvenance`;
- `PackageInput_RequestedConstraintAndResolvedCoordinateRemainIndependent`;
- `PackageInput_PackageManifestDeclarationsAreLibraryDeclared`;
- `PackageInput_RestoredRelationshipOriginRequiresOwnerAssociation`;
- `PackageInput_ProjectNodeRelationshipRemainsUnattributedWithoutAssociation`;
- `PackageInput_NotApplicableIsNotUnavailableOrCompleteEmpty`;
- `Execute_AuthoredSyntaxRetainsTargetsDeclarationsAndProvenance`;
- `Execute_AuthoredCompleteEmptyProjectIsNotUnavailable`;
- `Execute_AuthoredDuplicateSyntaxRetainsSourceOccurrenceCount`;
- `Execute_AuthoredIncompleteFactsRetainUsableAndOpaqueEvidence`;
- `Execute_AuthoredUnprojectedSyntaxRetainsOpaqueIdentity`;
- `Execute_AuthoredProviderFailureBecomesFailedRoot`;
- `Compare_AuthoredExactScopeMatchesEquivalentPackageManifest`; and
- `CreateAuthoredProjectInput_RequiresProjectXmlAcquisition`;
- `Create_RetainsAuthoredIdentityProvenanceOccurrencesAndFailures`; and
- `Create_OpaqueAuthoredScopesExposeOnlyInertDisplayEvidence`;
- `Execute_CurrentRuntimeManifestRetainsProviderIdentityTargetAndProvenance`;
- `PackageInput_RuntimeRelationshipsDoNotInventConstraintsRolesOrPruning`;
- `Execute_RuntimeIncompleteFactsRetainUsableGraphAndFailures`;
- `Execute_RuntimeCompleteEmptyGraphIsNotUnavailable`;
- `Compare_RuntimeDeclarationNotApplicableIsDistinctFromIncomplete`;
- `Execute_RuntimeProviderFailureBecomesFailedRoot`; and
- `CreateRuntimeDependencyManifestInput_RequiresRuntimeManifestAcquisition`;
  and
- `RuntimeRoot_RequiresExclusiveMatchingTargetEvidence`.

Owner-enrichment behavior remains `unverified` until #5315 supplies its typed
input and focused gates. Cross-host retention remains `unverified` until this
Release gate lands:

- `PackageInput_CliAndBrowserConsumeTheSameTypedSnapshot`.

## Existing dependency-evidence adoption sequence

1. Lock this result and equivalence contract under #5312.
2. Land typed self-attested direct nuspec identity in #5316.
3. Land the focused Restored Project Dependency Facts Query tracked by #5314.
4. Under #5533, implement the package/nuspec and package-prefix adapters over
   `PackageManifestFacts`, `PackageProfileMatch`, and
   `PackageDependencyGroupsQuery`.
5. Under #5533, implement the restored-project adapter, typed root failures,
   and the cross-input equivalence fixture.
6. Land the focused Package Owner Evidence Query tracked by #5315, then admit
   its owner observations as optional input.
7. Under #5534, adopt the focused
   [Dependency Evidence CLI](dependency-evidence-cli.md) command, L2 sections,
   Markout and JSON-family projections, input spellings, and later
   product-owned predicates.
8. Under #5535, export the same typed outcome through the Browser/Wasm boundary
   and adopt it in inspect-web without duplicating dependency semantics.

Each adoption is independently reviewable. Later syntax must not move owner
filtering ahead of required evidence acquisition unless source delegation
proves exact equivalence and honest completion.

The existing `depends`/`DependencyGraphService` path remains authoritative
until a separate parity-gated adoption replaces its dependency projection.
The #5314 implementation should replace or become the shared basis for its
existing assets projection after parity rather than leave two independent
parsers.

The #6266 larger-shape sequence is the eight-step delivery plan in
[Consumers and delivery](#consumers-and-delivery). It evolves this owner and
its existing result in place. No step introduces a parallel normalization
query, and each provider retains its parser, identity, bounds, failure, and
containment contract.

## Non-claims

This owner does not:

- locate files, read paths, evaluate MSBuild, restore, or build;
- parse CLI options or choose command names and aliases;
- select package sources, authenticate, cache, retry, or define network policy;
- redefine nuspec parsing, package search, project-assets parsing, target
  framework compatibility, or NuGet version semantics;
- evaluate package pruning, decide an application-authored exemption, or infer
  which absent relationship pruning removed;
- infer processing from absence, input kind from a path suffix, or authorship
  from direct/transitive graph role;
- claim that `.deps.json` is a restore artifact or that its missing framework
  assemblies are package-pruning evidence;
- define L2 row queries, Count, item windows, field projection, or ordering
  syntax;
- define Markout, JSON, JSONL, TSV, or plaintext rendering;
- classify a package as first party or third party;
- replace the current `depends` command or `DependencyGraphService` without a
  focused parity adoption;
- promise an exact total for bounded package-prefix discovery; or
- acquire package archives, assemblies, metadata, PDBs, source, or IL.
