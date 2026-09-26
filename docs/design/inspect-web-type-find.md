# Inspect Web Type Find

## Status and ownership

This is the proposed focused Browser/Wasm adoption contract for
[#6851](https://github.com/richlander/dotnet-inspect/issues/6851), step 8 of
the [reverse type locator adoption](reverse-type-locator-adoption.md) path.
Implementation and its evidence are unverified.

**Inspect Web Type Find** owns one Browser-specific responsibility:

> In one exact active Workspace realization, execute Type discovery through
> the resident reverse locator, present its typed candidate vector and
> coverage, and turn an explicit candidate choice into the exact
> owner-issued Type activation for that observation.

The owner includes the narrow managed transport and Browser interaction needed
to preserve that association. It does not own declaration matching,
visibility, Workspace realization, Spotlight lifecycle, destination
activation, Navigation semantics, or Type and Member inspection.

## Demo

Assume the active Workspace contains `System.Text.Json@10.0.0` and the
corresponding installed framework population.

```text
Search types, members, packages
  System.Text.Json.JsonSerializer

Types
  JsonSerializer  System.Text.Json  System.Text.Json@10.0.0
  JsonSerializer  System.Text.Json  .NET 10
```

The two rows are distinct observations even when their logical Library and
assembly identities compare equivalently. Choosing the package row opens that
exact package Type. Once
[#7242](https://github.com/richlander/dotnet-inspect/issues/7242) supplies its
separately owned action, choosing the framework row uses that exact framework
observation while preserving the product direction that Spotlight has no
Platform scope, root, or Platform-labeled destination.

After the Type opens, selecting
`Serialize<TValue>(Stream, TValue, JsonSerializerOptions)` uses the exact
Member identity issued by the Type surface. Type Find does not search for,
rank, or infer an overload.

The zero-result neighbor remains honest:

```text
Types
  No confirmed matches

Coverage
  1 assembly could not be evaluated
```

Complete absence may use the ordinary concise empty state. Incomplete
realization, inventory, coordinate, visibility, or forwarder evidence remains
available from the result and is not presented as a complete miss.

## Basis and participating owners

The conventional baseline is search-result selection followed by typed
navigation. Search identifies candidates; it does not also become a resolver
or a navigation state store.

| Owner | Contract consumed |
| --- | --- |
| [Reverse Type-Declaration Locator](reverse-type-declaration-locator.md) | Structured Type matching, always-vector candidates, distinct observation context, deterministic order, and attributed coverage |
| [Workspace Live Locator](workspace-live-locator.md) | One lazy resident locator, append-only admitted declaration population, receipt-pinned answers, and close drainage |
| [Type declaration visibility](type-declaration-visibility.md) | Default public-surface, `EditorBrowsable`, obsolete, generated-name, and unknown-evidence policy |
| [Inspection envelope](inspection-envelope.md) | One completed host boundary carrying owner-issued content, Share outcome, and diagnostics |
| [Inspect Web retained Workspace realization](inspect-web-retained-workspace-realization.md) | Exact active realization and operation admission |
| [Inspect Web Spotlight destination activation](inspect-web-spotlight-destination-activation.md) | Captured destination plan, current-authority validation, and settled activation result |
| [Inspection Subject Navigation](inspection-subject-navigation.md) | Exact Package/Library/Type/Member subjects and action authority |
| [Inspect Web Navigation Consumer](inspect-web-navigation-consumer.md) | Snapshot installation, history, focus, announcement, and acknowledgement |
| [Inspect Web Shell Interaction](inspect-web-shell-interaction.md) | Spotlight opening, modal behavior, keyboard interaction, and search-scope presentation |

The [command transition model's location and result
cardinality](command-transition-model.md#location-and-result-cardinality),
established by
[#7223](https://github.com/richlander/dotnet-inspect/pull/7223),
provides a related CLI analogue, not a dependency or Browser owner. This
contract keeps the same distinctions: the active Workspace may contain many
observations, Type is one result identity family, locator answers remain
vectors for zero, one, and many candidates, and explicit row selection creates
a separate scalar Navigation intent.

## Active-Workspace operation

One nonempty Type search text creates one locator
`TypeDeclarationLocatorRequest.Pattern`. Empty Spotlight text does not activate
the locator. A later text change supersedes the earlier Browser operation
through the existing operation-authority lifecycle; it does not mutate or
reuse the earlier result as current. Empty text still advances that lifecycle
and retires the previous result without activating the locator.

The host-neutral operation:

1. enters the exact active Browser realization under owner-issued operation
   authority;
2. gets that Workspace's resident declaration locator;
3. executes one all-declaration locator request so shared visibility can
   evaluate its complete policy;
4. projects the result through
   `TypeDeclarationLocatorSection` with
   `TypeDeclarationVisibilityPlan.Default`; and
5. completes one `InspectionEnvelope<TypeDeclarationLocatorSectionResult>`.

The Browser does not implement another visibility filter. Unknown visibility
evidence remains attributed. A user-selected future visibility policy must use
the shared plan rather than reinterpret Browser row state.

Managed Browser composition then creates:

```text
BrowserTypeFindOperationResult
  Find: InspectionEnvelope<TypeDeclarationLocatorSectionResult>
  Activations
    one exact result-local candidate correspondence
    one captured Spotlight destination descriptor or typed non-success
```

`Find` is the unchanged shared baseline. `Activations` is a Browser-owned
composition around it, not another locator result. Construction requires one
activation entry for every projected candidate and rejects a missing,
duplicate, or foreign answer/candidate correspondence. The result-local answer
identity and candidate position associate already-produced values inside this
compound result; they never authorize Metadata access, action issuance, or a
later lookup.

The TypeScript boundary receives only that detached compound result. It
receives no Workspace, lease, reader, assembly image, source authority,
callback, or managed object. The generated facade and Worker operation
transport the complete typed result; a handwritten parallel DTO may not omit
coverage, separate an action from its candidate, or replace the coordinate
union with display strings.

The Share outcome uses Workspace Definitions' exact codec when it can preserve
the semantic plan. Until such a projection exists, it is visibly
`NonProjectable`; a public package URL, displayed source label, or partial
coordinate is not a substitute.

## Population admission and append

Logical Package Scope publication does not implicitly make declarations
searchable. After an exact Package occurrence is committed in the active
Workspace, the Browser composition requests
`AdmitPackageScopeDeclarationAsync` with that owner-issued occurrence
descriptor. Admission reuses the existing Root realization and retains its
typed failure; it does not reacquire the package or reconstruct an occurrence
from package ID, version, target framework, asset path, or presentation text.

The same active Workspace retains one resident locator across additions.
Successful admission schedules append maintenance without another search.
Earlier immutable results do not gain rows or change completion after a later
admission. A later search captures the newer population receipt. Existing
healthy inventories are reused rather than rescanned.

Package removal, occurrence replacement, and in-place refresh remain outside
the append-only locator. The active-Workspace owner must supply their complete
replacement behavior before Type Find may claim continuity across them.

Framework declaration population retains its source-owner association.
Production presentation is contingent on the still-open
[#7202](https://github.com/richlander/dotnet-inspect/pull/7202): framework
assemblies appear as ordinary Library/Type search results with `.NET` or
`ASP.NET Core` source disclosure, not as a Platform scope, root, prompt, or
Platform-labeled destination. Internal Platform provenance remains typed and
is not relabeled as Package evidence.

PR #7202 does not supply a locator-candidate Type action and explicitly declines
production adoption of the staged Platform activation path. The focused
[framework declaration activation](inspect-web-framework-declaration-activation.md)
prerequisite [#7242](https://github.com/richlander/dotnet-inspect/issues/7242)
must issue a Browser-local exact framework Library/Type action or typed
non-success without introducing a Platform user concept or pretending that
shared Navigation contains a Platform structural subject. #6851 must not
implement that missing owner contract locally.

## Candidate and activation association

One displayed Type row corresponds to one
`TypeDeclarationLocatorSectionCandidate`, including:

- exact Library coordinate;
- structured Metadata Type name and declaration kind;
- one exact observation context and occurrence;
- source-owner-safe origin evidence; and
- the answer's coverage and completion context.

Distinct observations remain distinct rows and distinct selection targets even
when their coordinates and display labels are equal. Grouping may reduce visual
repetition only when every selectable observation remains individually
reachable.

Each opaque action token includes an operation-unique issuer in addition to its
result generation and local ordinal. Result replacement retires the superseded
result's unselected backing Navigation actions. A selection already admitted
by Navigation remains Navigation-owned and may settle normally.

Before a package row is published as selectable, managed composition joins:

- the candidate's exact Workspace identity;
- its `PackageScope` occurrence and `PackageCompileAsset`;
- the Navigation package evaluation's exact occurrence, asset, participant,
  and assembly registration; and
- the candidate's structured `MetadataTypeDefinitionName`.

For a candidate in the active occurrence, that owner-issued join locates one
exact Type in the current Navigation inventory and obtains its already-issued
action. The locator also searches sibling retained Package occurrences, whose
Types are deliberately absent from the installed Navigation snapshot.
[#7243](https://github.com/richlander/dotnet-inspect/issues/7243) must supply
the Navigation-owned direct action for one exact discovered Type in such an
occurrence. Zero or several matches produce typed non-success. No coordinate,
assembly simple name, target framework, path, or ordinal substitutes for the
complete join.

A framework row consumes
[framework declaration activation](inspect-web-framework-declaration-activation.md)'s
separate exact action result. Other source arms remain typed-unavailable until
their source owner supplies an equivalent Browser activation contract.

Each activation association is captured with Spotlight's complete activation
basis, including the active Workspace, relevant Scope and registration
revisions, publication base, result generation, and owner-issued action.
Result-local answer, context, member, and declaration ordinals are
correspondence inside that result; none is an authority that TypeScript can
later send back to reopen Metadata.

Selection submits only the captured opaque action and its issuing authority.
It does not send coordinate fields back for reconstruction. A stale, foreign,
closed, unavailable, ambiguous, or failed binding settles visibly and applies
no navigation effect. The Browser may rerun Find to obtain current rows; it
does not silently reclassify the selected stale row.

A singleton candidate remains a one-element vector. The UI may let Enter
select it without displaying a choice dialog, but the result itself does not
select or navigate. Multiple candidates remain user choices unless an explicit
Browser workflow policy, outside locator matching, identifies one.

Definitions and forwarders remain distinct. A forwarder selection uses an
owner-issued exact target-resolution and activation path or remains visibly
unavailable; the Browser does not copy a category from a neighboring
definition, search by assembly name, or activate the forwarding Library as
though it defined the Type.

## Type and Member continuation

Type Find ends when the exact selected Type becomes the active inspection
subject. The existing Type surface then issues its Member inventory and exact
Member actions. A later Member selection uses its `MemberAnchor` and
owner-issued action, including containing and declaring Type context.

`stableSelector`, `anchorDigest`, and canonical signature remain owner-issued
binding and restoration evidence. Displayed signatures, group names, overload
indices, and row positions are not durable Member identity. Type Find does not
manufacture a Member action from the selected Type row.

This boundary makes Type Find an entry point into selection continuity without
owning that later policy:

```text
selected Type or Member subject
  -> exact replacement Package occurrence
  -> API declaration correspondence
  -> exact destination subject or typed non-success
```

The package Version/TFM correspondence and ancestor fallback above are
target-only work under
[#7061](https://github.com/richlander/dotnet-inspect/issues/7061),
[#5584](https://github.com/richlander/dotnet-inspect/issues/5584), and
[#5511](https://github.com/richlander/dotnet-inspect/issues/5511). They are not
implemented or simulated by #6851. Their eventual integration begins from the
exact Navigation subject produced here.

## Presentation

Type Find uses Spotlight's existing transient selection surface. It adds no
persistent Workspace tabs, locator panel, source hierarchy, or separate
search modal. Package, Library, Member, and command search retain their
existing owners and may compose beside locator-backed Type rows.

The normal row shows the shortest useful Type, namespace, Library, and source
description. Version, target framework, producer, and other context appear
when they disambiguate choices or when the person asks for details. Coverage
is quiet for complete successful results and visible for incomplete or failed
results, including an empty candidate vector.

This is the existing Browser-native interactive-rendering exception. The
input remains the shared typed Sections result. Browser rendering does not
change locator matching, candidate identity, visibility, coverage, or source
provenance.

## Adoption and retirement

Implementation follows these boundaries:

1. the one-active-realization Browser path in
   [#7028](https://github.com/richlander/dotnet-inspect/issues/7028) and
   current-subject operation admission in
   [#7030](https://github.com/richlander/dotnet-inspect/issues/7030) must be
   available;
2. [#7243](https://github.com/richlander/dotnet-inspect/issues/7243) must
   supply exact direct Navigation for a Type in a non-active retained Package
   occurrence;
3. #7202 must settle the production framework presentation direction, and
   [#7242](https://github.com/richlander/dotnet-inspect/issues/7242) must
   supply exact framework Type activation before framework rows are
   selectable;
4. the active realization explicitly admits current Package Scope occurrences
   into its resident locator;
5. one managed Metadata-facade operation projects the shared envelope and
   candidate activation associations;
6. the generated facade, Worker catalog, and page client transport that exact
   result;
7. Spotlight renders the typed rows and submits only captured actions; and
8. browser acceptance proves Find, append, selection, exact Type activation,
   and exact Member continuation.

[#6686](https://github.com/richlander/dotnet-inspect/issues/6686) owns the one
production Spotlight dispatcher and its broader Package/Library acceptance
matrix. #6851 may implement the managed Type Find result before that issue
lands, but its TypeScript selection adapter and Browser acceptance must follow
or stack on #6686's dispatcher. It must not create a temporary parallel
dispatcher to demonstrate Type activation. #6851 does not wait for future
Version/TFM correspondence to establish its initial Type handoff.

After parity, step 9 of the reverse-locator adoption path retires or narrows
the duplicate Platform lookup. Locator-backed Type rows also replace
Spotlight's client-owned ranking of already-loaded Type display names for the
admitted population. Package ranking and loaded Member-group search are not
silently removed by this Type-focused adoption.

No new state-machine model is required. This owner composes the existing
Workspace realization, resident-locator, operation-authority, captured
destination-action, and Navigation authority contracts without adding another
scheduler or concurrency protocol.

## Evidence

All claims below are unverified until their named implementation gates land.

Managed Browser/Wasm gates must prove:

- real `System.Text.Json@10.0.0` package and framework observations remain
  separate candidates with exact source context; package activation is gated
  here and framework activation is gated by #7242;
- Package addition admits the exact committed occurrence, a later Find sees
  the append, and earlier healthy inventories are not rescanned;
- empty, singleton, and multiple vectors retain the same result shape;
- complete empty and incomplete empty results have different visible evidence;
- equal coordinates from distinct origins or observations remain separate
  selectable actions;
- either of two retained Package occurrences can be selected directly without
  an intermediate Package/default-Type publication, as gated by #7243;
- default visibility and unknown evidence come from the shared Sections plan;
- stale result generation or active-realization replacement applies no action;
- unavailable forwarder target and binding failures remain typed; and
- close drains locator maintenance and rejects later work through existing
  owner outcomes.

Generated-facade and Worker tests must prove the operation is registered once,
preserves the coordinate union, candidate vectors, coverage, Share outcome,
diagnostics, and malformed-result rejection, and uses the ordinary operation
authority for supersession and cancellation.

TypeScript and browser tests must prove:

- Type rows use the existing Spotlight picker without parsing rendered text;
- a one-row vector can navigate directly only after explicit selection;
- package `JsonSerializer` opens its exact observation, while #7242 gates the
  framework neighbor without a Platform-labeled UI;
- adding a Package makes its Type rows available without replacing the active
  Workspace;
- stale selection is visible and does not navigate;
- the selected Type opens before an exact `Serialize` overload is chosen
  through its owner-issued Member action; and
- keyboard, focus, dismissal, narrow layout, Package, Member, and command
  search behavior remain intact.

When #7061/#5511 land, their separate integration evidence should begin from a
Find-originated exact subject and exercise exact, absent, and ambiguous
replacement correspondence. That future gate does not manufacture evidence
for this contract.

## Non-claims

This design does not:

- add Member discovery to the reverse Type locator;
- define package Version/TFM replacement, API correspondence, or Navigation
  fallback;
- add a Platform Spotlight scope, root, prompt, or Platform-labeled
  destination;
- define Package, Member, command, or package-query search semantics;
- add fuzzy Type ranking, source acquisition, Package removal, or locator
  replacement policy;
- make origin evidence into source authorization or portable identity;
- define a new Workspace codec or approximate an unsupported Share result;
- add persistent Workspace or locator chrome;
- change CLI Find output or command behavior; or
- authorize release or website deployment.
