# Reverse type-declaration locator

## Status and authority

The [cold query](#implemented-cold-query) is implemented under
[#6849](https://github.com/richlander/dotnet-inspect/issues/6849), following
the focused design in
[#6852](https://github.com/richlander/dotnet-inspect/issues/6852) of
[#6843](https://github.com/richlander/dotnet-inspect/issues/6843), within
[#6761](https://github.com/richlander/dotnet-inspect/issues/6761). Resident
inventories are available through the
[Workspace facade](workspace-live-locator.md#implemented-resident-context-facade)
for its admitted context population. CLI Find's pinned Package path and CLI
Router's default bare Platform path are adopted; Browser adoption and remaining
source adapters are pending.

**Reverse Type-Declaration Locator**, in `DotnetInspector.Queries`, owns:

> For one explicitly supplied finite assembly population, report matching type
> declarations as a vector of detached exact Library source coordinates with
> origin and structured Metadata names, preserving declaration kind,
> locally known definition category, observation context, and coverage. The
> consumer chooses; discovery never selects a candidate or binding.

This is one new query-composition owner. Metadata retains declaration decoding,
name identity, matching grammar, visibility, and type binding. Source Selection
retains Library coordinate construction and equality. Acquisition and Workspace
retain source authorization, realization, correspondence, and live authority.
The locator consumes their evidence; it does not redefine those contracts.

The basis is the conventional separation between symbol discovery and symbol
resolution. The user benefit is locating once, then inspecting the selected
Library and type without reconstructing identity from display. The necessary
additional structure distinguishes same-named declarations, source domains,
and incomplete searches. It is not a cache, universal resolver, or new
resource-ownership protocol.

## Inputs and adjacent contracts

An **exact population** means a fixed finite roster of selected assembly
occurrences for this attempt, not all assemblies in an ecosystem or a
binding-consistent union. Different packages, targets, and contexts may
contribute independent occurrences. Dependency-support assemblies are not
search members unless the supplied population explicitly includes them.

The input retains the selected population declaration and its owner's
realization/completion evidence. A closed roster of successfully acquired
assemblies does not erase upstream failures or prove that the requested
population was fully realized. Unknown omitted membership remains an
attributed upstream gap, not an invented count of missing assemblies.

Each roster member supplies the following typed associations, not parallel
lists zipped by source order:

| Input | Meaning and owner |
| --- | --- |
| Occurrence identity and order | Owner-issued identity and stable order of this assembly occurrence within its exact population/generation. Reordering enumeration does not reissue them. |
| Library coordinate or coordinate-unavailable outcome | Source Selection's `ExactLibrarySourceCoordinate`, attached through source-owner correspondence to the observed assembly. |
| Origin and observation context | Detached source-owner provenance and realization evidence identifying the Platform or package-feed source and the selected target/view; retained separately from the logical Library coordinate. |
| Declaration access or acquisition/admission failure | Existing operation/Workspace-authorized Metadata access to that occurrence, or its typed non-success. |

The query snapshots the finite roster for its attempt. It borrows through
existing authority, never reopens a path or widens acquisition. The output
contains none of that live access. A Workspace replacement cannot relabel an
old observation as belonging to the replacement; a direct operation can run
the same query without constructing a long-lived Workspace.

[Artifact acquisition](artifact-acquisition-and-workspaces.md#provenance-and-correspondence)
owns the association between source evidence, artifact occurrence, and assembly
projection. [Exact Library Source Coordinate](exact-library-source-coordinate.md)
owns the logical coordinate, whose closed arms are Package, Platform, Project,
and Local. No arm identifies selected bytes, a TFM, a RID, or a view; Platform
also omits the selected Platform version, while Project and Local omit project
and filesystem paths. Thus the coordinate alone is insufficient to reproduce
an observation.

The producer handoffs below are a prerequisite map, not new source contracts:

| Producer | Coordinate association required at the locator input |
| --- | --- |
| Package | Exact `PackageSourceCoordinate` plus Metadata-issued assembly definition identity from that package's selected asset. Retain the acquisition owner's exact producer/target/asset correspondence as observation context. |
| Platform | `PlatformLibraryPopulationDeclaration` plus Metadata-issued assembly definition identity attributed to that focus population. Preserve exact target/view and source realization separately. Package transport of a Platform pack does not turn its members into Package coordinates. |
| Restored project package asset | The project adapter's package provenance and exact selected-asset correspondence may supply the Package arm. A display package label or path under a package cache cannot. |
| Project output | Project arm over the Metadata-issued assembly definition identity, attached through the project owner's output association. Preserve project, target, output, and occurrence evidence as observation context. |
| Bare local assembly | Local arm over the Metadata-issued assembly definition identity, attached through the local-file owner's occurrence association. Preserve path and occurrence evidence as observation context. |

Likewise, a file copied from a Platform pack and supplied only as a local file
does not acquire Platform provenance from its filename or assembly identity.
The same bytes can legitimately be observed through distinct source domains.
Project and Local arms preserve that distinction without moving acquisition
paths into Library identity. A producer that cannot establish the required
source-owner occurrence association records `CoordinateUnavailable`; it does
not silently exclude the member or infer an arm from path shape.

## Request and matching

One attempt accepts a nonempty ordered sequence of typed name requests and an
explicit Metadata visibility policy. Empty or invalid requests are rejected
before scanning. The default discovery policy is the Metadata public surface;
all-declaration visibility is explicit. The declaration producer, not Queries,
decides public/nested visibility and special non-type rows such as `<Module>`.

There are three request meanings:

- **Exact declaration name:** a `MetadataTypeDefinitionName`, compared using
  Metadata's exact equality, preserving namespace, nested segments, case, and
  generic arity. It is not a constructed type or a user lookup pattern.
- **Name pattern:** Metadata's admitted type-filter request using
  `TypeMatcher` semantics, including case-insensitive simple/qualified names,
  nesting, generic notation, and `*`/`?` globs. Matching must use a
  Metadata-owned projection of the structured name, not a rendered C# label.
- **Namespace:** a nonempty namespace plus Metadata's exact, suffix, or
  exact-or-descendant namespace relation, evaluated with
  `MetadataTypeDefinitionName.IsInNamespace`. It is a separately identified
  request, not a wildcard spelling or an implicit retry of a Type pattern.

Each pattern is evaluated independently; a declaration can match more than one
request. Non-wildcard pattern matches are still discovery matches, not exact
type binding or proof of a unique full name. Explicit generic notation must
retain Metadata's arity behavior rather than match a same-base-name neighbor.

The query does not silently retry a namespace prefix or compute similarity
suggestions. Find may request those as separately identified fallback searches
under its own classification contract. A fallback must retain its effective
request and coverage and cannot replace or disguise an incomplete primary
answer. User-facing fallback migration belongs to Find, not this owner.

## Result currency: coordinates plus origin

Result currency and cardinality are separate contract choices:

| Currency | What the consumer receives |
| --- | --- |
| Coordinates only | A resource-free address, but not the origin evidence useful for choosing among available sources. |
| Coordinates plus origin | A resource-free address with source-owner provenance for consumer selection and display. This is the locator currency. |
| Live handle | Access under an owner's active lifetime and authority. A different workflow may need this, but discovery does not return it. |

Whether an owner computes or caches an answer does not determine its currency.
For this workflow, cold and any future warm answers expose the same
**vector of coordinates plus origin**. Zero, one, and many candidates use the
same shape. The query never turns a singleton into a scalar or chooses the
first candidate. This is the locator's contract, not a rule for every cache
or live-resource API in the repository.

Origin is typed, detached evidence supplied by the source owner. It tells the
consumer whether the declaration was observed through Platform or a particular
package-feed producer; a generic `NuGet` label is not a replacement for the
available feed provenance. Platform family and realization evidence remain
distinct from a pack's transport through NuGet. Within a Package coordinate,
different feed observations remain separate choices even when the package ID,
version, assembly identity, type, and displayed source label coincide.

[Package source model](package-source-model.md#identity-roles)
separates credential-free producer provenance from configured authority.
Preserve that distinction: origin may inform the consumer's policy but does
not authorize a feed or recreate a live source association. Source display
uses the owner's safe projection, such as `PackageSourceDisplay`, rather than
raw configured URLs or credentials. A display label is not identity. If an
owner cannot supply detailed origin, retain an explicit unavailable-origin
observation, not an inferred `nuget.org` origin; this alone does not invalidate
a coordinate/context already established by its owner.

## Detached candidate vector

The result is one immutable envelope containing the request sequence,
population evidence, per-member outcomes, and one answer per request. The
names below describe planned semantic roles, not existing public API types.

```text
LocatorResult
  population evidence + member outcomes
  answers, in request order
    request + completion
    candidates: vector, always present for an evaluated request
      ExactLibrarySourceCoordinate
      MetadataTypeDefinitionName
      Definition | Forwarder
      module version ID
      definition category? (class | interface | value type | enum | delegate)
      declaration inventory order
      origin
      observation occurrence + detached context
```

One vector entry is one observed declaration choice: **Library coordinate,
exact structured type name, declaration kind, and observation occurrence**,
with its origin and context attached. Repeating that same declaration from the
same occurrence does not add an entry. Distinct occurrences, origins or
target/view contexts remain separate entries even when their logical
coordinates are equal. There is no coordinate-only collapse followed by a
hidden origin/context choice.

Metadata also retains the high-level category of a local definition. A
forwarder has no category of its own: the locator does not bind its target or
guess a category. A consumer may reuse the category from a same-name
definition in the selected answer, but otherwise must use an owner-issued
binding or compatibility path before publishing a type-category claim.

The consumer may display the vector, group it without discarding entries, or
apply an explicit selection policy suited to its workflow. It owns that choice
even when only one entry is returned; a UI need not prompt merely because the
API leaves selection to the consumer.

Candidates also retain Metadata's
[definition discovery attributes](type-forwarding-resolution.md#definition-discovery-attributes)
unchanged as `DiscoveryAttributes`. These are additional evidence, not part
of coordinate equality, ordering, or locator filtering policy.
They also retain Metadata's nullable `IsDefinitionPublic` fact unchanged so a
consumer can apply a row-local definition visibility policy without redefining
the locator's enclosing-chain public-surface view.
`DeclarationOrder` retains the declaration's zero-based position in its
member's selected inventory view. It is consumer evidence, not part of
candidate identity or the locator's deterministic output ordering.
Declaration-discovery completeness does not assert target-attribute availability.
Cold and resident answers carry the same owner-issued facts.

A forwarder's coordinate identifies the Library declaring the forwarder, not
its target Library. A definition and a forwarder remain distinct even when
they have the same name. Duplicate physical declarations inside an image must
remain Metadata-issued ambiguity or rejected-inventory evidence, not be
laundered into a unique candidate by occurrence deduplication. Module-export
declarations are not silently relabeled as definitions or forwarders; an
unsupported declaration form leaves visible incomplete member evidence.

Candidate ordering is deterministic for the same population regardless of
producer enumeration order. Order by namespace and root-to-leaf metadata
segments (ordinal), then source arm (Package, Platform, Project, Local),
source-owner identity components, Metadata assembly identity components, and
declaration kind (Definition before Forwarder), then the population-issued
stable occurrence order. Project and Local have no additional source-owner
identity components. That final order belongs to the fixed population and is
not reassigned when producer enumeration is permuted. Source/assembly component
comparison uses owner-normalized equality components and stable ordinal/numeric
ordering, not culture, paths, display names, hashes, or acquisition timing.
Future coordinate arms need their own owner-defined stable ordering before
admission.

Ordering is not ranking or resolution precedence. In particular, a definition
does not suppress a forwarder, a namespace-prefix assembly name does not win,
and source order does not select a candidate. Multiple entries remain choices
for the consumer, even if their declarations could later bind to one terminal
type. The query does not switch to a separate `Ambiguous` result currency.

## Completion and failure

Completion has two separate dimensions: realization of the selected
population and evaluation of the request over its exact roster. A query answer
is complete only when both are complete for that request and visibility policy.
Completeness never extends to unselected populations, implementation views,
or dependency closure.

Each member has an attributed terminal outcome: searched completely, searched
partially with Metadata failures, acquisition/admission rejection, declaration
query failure, coordinate unavailable, or not evaluated due to an explicit
work bound. Preserve lower-owner failure kinds and occurrence association;
free-form diagnostics are not the failure identity. A whole-image rejection
contributes no guessed rows; healthy members, and rows a Metadata producer
explicitly certifies from a partial inventory, remain useful candidates.

| Coverage and observed candidates | Evaluated answer |
| --- | --- |
| Complete, zero | Empty vector plus complete coverage; the consumer can report a scoped miss. |
| Complete, one | One-element vector plus complete coverage; no scalar, selected candidate, or `ResolvedType`. |
| Complete, more than one | All candidate entries in the same vector shape plus complete coverage; consumer-owned choice. |
| Incomplete, any count | Vector of known candidates plus incomplete coverage and gaps. Zero is not absence; one is not uniqueness; more entries do not exhaust the alternatives. |

Invalid requests or unusable population associations produce typed `Rejected`
outcomes, not evaluated empty vectors. This admission failure is independent
of candidate cardinality. `Missing`, `SingleCandidate`, and `Ambiguous` are not
alternative query-answer shapes.

An explicitly empty, completely realized population can prove a scoped miss.
A failed attempt that happened to acquire no assemblies cannot. Likewise, a
failed member must not disappear merely because a healthy member matched.

Output row selection is downstream and cannot change query completion. If
upstream work is explicitly bounded and stops, unevaluated members/requests
remain visible; collecting one row does not certify uniqueness. Bounds use
the existing query/work-planning contracts rather than format-dependent
execution. A caller cancellation propagates as cancellation after ordinary
owner cleanup; it is not a completed envelope. Unexpected operation-wide
errors likewise propagate, not become empty successful vectors.

## Implemented cold query

`TypeDeclarationLocatorQuery.Execute` consumes the Workspace-issued
`WorkspaceDeclarationPopulation` and a nonempty immutable request sequence.
`TypeDeclarationLocatorRequest.Exact` carries a Metadata-issued definition
name; `Pattern` carries non-whitespace type-filter text; and `Namespace`
carries a non-whitespace namespace plus a valid
`MetadataNamespaceMatch`. Pattern interpretation remains
`TypeMatcher.MatchesTypeFilter`, including its explicit generic notation
behavior. Namespace interpretation remains
`MetadataTypeDefinitionName.IsInNamespace`; this query adds no wildcard
translation or fallback.

`TypeDeclarationLocatorResult` is either typed `Rejected` admission evidence
or an `Evaluated` result containing the detached population receipt, selected
visibility, optional work bound, attributed member outcomes, and ordered
answers. Every answer retains its request and an immutable candidate vector,
plus separate realization/evaluation completeness. The candidate's
`Observation` retains the existing Workspace member evidence, not a live
context or authority. Each candidate separately retains the exact MVID issued
by the declaration inventory so consumers can preserve assembly
correspondence without reopening the image.

Each eligible member is read once per call through the population's scoped
Metadata access, then evaluated against every request. Inventories are not
retained across calls. `Searched` preserves any visible unsupported
declarations; currently these are module exports. Such a member conservatively
makes evaluation incomplete for every request in the call, while its supported
matching declarations remain candidates. Metadata inventory rejections and
Workspace access failures keep their owner-issued kinds and occurrence.
Coordinate-unavailable members remain explicit and are not inventoried.

The query declares `InspectionCost.Unbounded`, like the existing
assembly-context inventory query. `maxInventoryReads` optionally bounds
attempted whole-image reads in population order, including rejected attempts.
It is not an intra-image Metadata limit, a pattern-comparison budget, or an
output-row window. Members beyond that bound become `NotEvaluated`; every
request still has a vector with incomplete evaluation. A null bound authorizes
the full selected roster. Invalid requests/bounds are rejected before reads.
An already closing/closed Workspace input is rejected at query admission;
later access loss is attributed to the affected member.

Ordering follows the contract above. Canonical Package ID and version text
compare ordinally; Platform family compares by its stable enum value.
Assembly names and tokens use ordinal-ignore-case comparison, versions compare
numerically, and culture uses Metadata's existing `NormalizeCulture`
projection before ordinal-ignore-case comparison. That helper is exposed by
Metadata rather than copying its equivalence rules into Queries. The final
occurrence tie-break preserves equal coordinates from different origins/views.

Current population producers are exactly those supported by
[explicit context projection](workspace-live-locator.md#implemented-explicit-context-projection).
This query does not add Artifact Root, local/project, or reference-pack
population adapters. Coordinate ordering recognizes the existing four source
arms, but that is not evidence that every producer already feeds this input.
The Workspace facade supplies prepared occurrence outcomes to the same query
core. Its work stops retain `NotEvaluated.Bound`; no second matching or ordering
policy is introduced. The result is an L1 prerequisite for that facade and
common Sections, not a completed host boundary. Their adoption supplies the common
[inspection envelope](inspection-envelope.md#boundary); this prerequisite
does not invent a Share result.

Release gates live in `WorkspaceContextLoaderTests`, with the `TypeLocator_`
prefix:

| Claim | Gate suffix |
| --- | --- |
| Zero/one/many vectors and independent request order | `ZeroOneManyAndRepeatedRequestsKeepVectors` |
| Structured identity, nesting, case and generic/glob semantics | `ExactNestingAndPatternArityUseMetadataSemantics` |
| Exact and descendant namespace semantics | `NamespaceRequestsUseTypedNamespaceSemantics` |
| Public/all policy and no implicit fallback | `PublicAndAllAreDistinctWithoutFallbackSearches` |
| Equal coordinates retain feeds, views and stable occurrences | `EqualCoordinatesKeepFeedAndTargetObservations` |
| Source/assembly ordering and facade identity without binding | `SourceAndAssemblyOrderingDoNotSelectDefinitionsOverForwarders` |
| Owner-normalized assembly equality components | `EquivalentAssemblyCulturesUseOccurrenceOrder` |
| Unsupported declaration and whole-inventory failures | `ModuleExportsAndRejectedInventoriesRemainAttributed` |
| Upstream failure versus complete empty population | `UpstreamFailuresDifferFromCompleteEmptyPopulations` |
| Unsupported coordinate remains visible | `UnsupportedCoordinateIsNotDroppedOrScanned` |
| Bounds do not certify absence or uniqueness | `WorkBoundNeverCertifiesAbsenceOrUniqueness` |
| Admission and cancellation | `RequestValidationPrecedesReadsAndCancellationPropagates` |
| Detached answers and visible loss of access | `DetachedAnswersSurviveCloseAndReleasedMembersRemainVisible` |
| Real Package/Platform and forwarder/definition vectors | `RealJsonChoicesAndRuntimeObjectForwarderRemainDistinct` |

The real-asset gate uses `System.Text.Json@10.0.0` and the actual Platform
implementation-pack producer for
`Microsoft.NETCore.App.Runtime.linux-x64@10.0.10`. It selects
`System.Text.Json`, `netstandard`, and `System.Private.CoreLib`, preserving two
`JsonSerializer` choices and both the `System.Object` forwarder and definition.
This supplements, rather than impersonates, the pinned reference-pack evidence
below: reference-view population production and reopening remain unverified
until their owners adopt that path. The whole-assembly real-asset gate is
`Speed=Slow` and stays in daily Deep Inspect's unfiltered Queries suite; the
small-fixture contract cases remain PR-fast.

## Reopening and lifetime boundary

The immediate output obligation is to preserve each candidate's coordinate,
origin, and observation context together. It is neither a live Workspace handle
nor authority to acquire from the recorded source. A result remains readable after
the originating operation or Workspace closes, but that historical evidence
does not entitle a new operation or promise the same local bytes.

The consumer supplies the coordinate, exact structured name, and selected
detached context to its ordinary source/Workspace and Metadata owners. Those
owners reacquire or admit under current authority and perform exact
declaration lookup and forwarding resolution. A missing target, changed view,
unavailable producer, or unbound forwarder remains their typed non-success;
the locator does not substitute another Library. Selecting a new view is a new
inspection, not proof of reference/implementation correspondence.

[Structured forwarding](type-forwarding-resolution.md) retains terminal
binding and ordered hop evidence. Member inspection first binds the selected
type, then applies its own exact member/overload selector. This type-declaration
locator neither locates overloads nor invents member identity.

The detached population/context projection needed for this handoff is a
Workspace adoption prerequisite, not an implemented property of today's
`TypeFindResult`. Portable encoding and restoration remain with
[Workspace Definitions](workspace-definitions.md); unsupported local or private
source sharing must remain visibly non-projectable.

Caching is outside this query contract. A direct cold query is sufficient.
Workspace memoization, if adopted, belongs to one exact realized
population/source generation. CoreCache persistence requires its owner's
complete immutable input identity and cold-path equivalence. Neither the
logical Library coordinate nor a filesystem path is a sufficient cache key.
There is no locator-owned process cache or third resource lifetime.
[Workspace Live Locator](workspace-live-locator.md) separately specifies the
operator-approved on-demand, resident, append-only Workspace consumption
scenario; it preserves this query's result and selection contract.
[#6756](https://github.com/richlander/dotnet-inspect/issues/6756) can independently
remove the current Platform catalog's unsafe retained state.

## Existing implementations and deliberate differences

At design baseline `c564d8726`:

| Evidence | Reuse and boundary |
| --- | --- |
| `ILInspector.Metadata.Corpus.SearchTypes` | Useful finite-population scan and per-member failures. Its source/version/TFM are explicitly display-only, it searches API definitions, and deterministic output follows supplied member order. Do not promote its rows into exact source authority. |
| `AssemblyTypeDeclarationInventoryReader` | Already issues structured definition and forwarder names, but opens a descriptor, includes all definitions without per-entry visibility, and rejects the whole image on malformed declaration evidence. Borrowed-session/public-surface adoption belongs to Metadata. |
| `AssemblyContextTypeInventoryQuery` | Already composes authorized per-participant outcomes, but its type rows are strings rather than structured declarations. Reuse query execution, not display-name reconstruction. |
| `PlatformTypeCatalog` | Retains structured names and declaration kinds, but returns descriptors, prefers definitions, may prefer a namespace-prefix assembly, and caches by path. Those selection/cache policies do not transfer to general discovery. |
| [Find service](find-search-service.md) | Keeps a useful direct/prefix/similarity classification boundary. Its current source-order deduplication and format-dependent early exit cannot establish complete exact-coordinate answers. Migration remains a CLI slice. |

These are analogous implementation evidence, not competing authority. No code
is transferred from another project. The deliberate change is exhaustive
declaration evidence instead of Platform routing's winner selection; retaining
all candidates is necessary for Find's inspect-the-selected-coordinate workflow.

## Real-asset demo and evidence

On 2026-09-13, released `dotnet-inspect 0.25.0+473d56a` located
`System.Text.Json.JsonSerializer` in `System.Text.Json@10.0.0`, `net10.0`.
Its current JSON contains `source`, `source_version`, `library`, and
`full_name` strings, not the proposed coordinate/context envelope.

The pinned `Microsoft.NETCore.App.Ref@10.0.10` archive
(SHA-256 `995e22bdc4f70db27044d152e55cf231a9712338e4e391a2b9b497a756a647c3`)
provides `ref/net10.0/System.Text.Json.dll` with the same type,
`ref/net10.0/System.Runtime.dll` defining `System.Object`, and
`ref/net10.0/netstandard.dll` forwarding `System.Object` to `System.Runtime`.
These are reference-pack observations, not inferred implementation equivalence.

Reproduce the production probes after extracting those three exact entries
from the pinned nuget.org archive into `./reference-pack`:

```sh
dnx dotnet-inspect -y -- find JsonSerializer \
  --package System.Text.Json@10.0.0 --tfm net10.0 --json
dnx dotnet-inspect -y -- find JsonSerializer \
  --library ./reference-pack/ref/net10.0/System.Text.Json.dll --json
dnx dotnet-inspect -y -- find System.Object \
  --library ./reference-pack/ref/net10.0/System.Runtime.dll --json
dnx dotnet-inspect -y -- library \
  ./reference-pack/ref/net10.0/netstandard.dll -S "Type Forwarders" --json
```

The local-file probes verify declarations only; they do not themselves issue
Platform coordinates. Implementation fixtures must use a Platform-owner
realization for the Platform arm and a package-owner realization for the
Package arm.

Proposed typed workflow, **not executable syntax or current output**:

```text
Locate JsonSerializer in exact package + Platform reference populations
  coverage: Complete
  candidates: [
    Package(System.Text.Json@10.0.0, exact assembly identity)
    { namespace: System.Text.Json, segments: [JsonSerializer] }, Definition
    origin: package feed (owner-issued nuget.org producer evidence)
    context: exact package producer, net10.0, selected asset/view
    Platform(DotNetRuntime, exact assembly identity)
    { namespace: System.Text.Json, segments: [JsonSerializer] }, Definition
    origin: Platform
    context: exact Platform 10.0.10 reference realization
  ]

Consumer selects the package entry -> Type inspection
  reuse coordinate + structured name + detached context; bind normally
Select a Serialize overload there -> Member inspection
  reuse that exact type context and Metadata-issued member selector
```

Neighboring query: `System.Object` over the reference `netstandard` and
`System.Runtime` members returns both the Forwarder and Definition candidates,
not a preferred winner. Reopening the forwarder without an available target
produces Metadata's unbound/unavailable outcome, not a different locator result.
If one member cannot be inventoried, return the one-element vector with
incomplete coverage. A complete single-source search also returns a
one-element vector, with complete coverage instead. Neither chooses the entry
for the consumer. A complete no-match search returns `candidates: []`.

## Required evidence and adoption

The [implemented cold-query gates](#implemented-cold-query) cover its current
input boundary. The remaining Workspace, reference-view, Sections and host
properties below are **unverified** until their adoption gates run in Release.
The production probes above motivate the design; they are not candidate
implementation tests.

| Claim | Required outcome-level gate |
| --- | --- |
| Stable vector currency and consumer choice | Queries/Sections: zero, one and many matches retain the same vector/array shape; no singleton unwrap or preferred entry. The same coordinate from two feeds remains two entries with owner-issued origins, including colliding display labels. |
| Deterministic identity-preserving discovery | Queries: permute one exact mixed-source population; compare candidate entries, occurrence associations, and failures, including same-named Libraries and two views of one coordinate. |
| Declaration fidelity | Metadata/Queries: definition categories, a pinned definition/forwarder pair, nested generic names, exact versus pattern matching, public/all visibility, and duplicate or unsupported declaration evidence. |
| Honest completeness | Queries: empty complete population, upstream omitted-member gap, invalid image, coordinate-unavailable local member, partial inventory, and early work stop alongside a healthy match. |
| Detached exact handoff | Workspace/host adoption: retain result after close, then reopen each selected package/Platform observation under new authority; preserve producer, target/view, Library and type. Include unavailable reopening and forwarder failure. |
| Format and host continuity | Sections/CLI/browser: compare typed coordinates, origin and coverage across output and navigation; consume owner-safe origin display without using it as identity or authority. Row windows never certify a partial census or pick a binding. |

No new TLA+ model is needed for this stateless query contract. Stateful
realization/replacement and optional cache interactions belong to their owners
and inherit their models; adoption must not treat a logical coordinate as a
generation receipt.

The [delivery map](reverse-type-locator-adoption.md) counts the focused path
through CLI and Browser/Wasm adoption, records prerequisites, and tracks
retirement. This document does not authorize changing adjacent owners in one
implementation PR.

## Non-goals

No ecosystem default selection, package acquisition inside Metadata, general
PlatformHouse policy, dependency closure, implementation correspondence,
member/signature discovery, new CLI flags, or replacement of Spotlight's
static Platform filename population. No inspected-assembly execution or WinMD
support is introduced. Existing platform and untrusted-data contracts apply.
