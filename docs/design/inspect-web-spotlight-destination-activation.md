# Inspect Web Spotlight destination activation

## Status and owner

This is the proposed, unimplemented interaction contract for
[#6673](https://github.com/richlander/dotnet-inspect/issues/6673), under the
Workspace experience tracker
[#6012](https://github.com/richlander/dotnet-inspect/issues/6012). The operator
approved this focused Browser-specific capability: Spotlight preserves the
active Workspace for destinations already admitted or covered by its current
registration-bearing Scope revision, and creates a fresh curated Workspace
only for an uncovered package.

This document is the normative owner of **Spotlight destination activation**.
It owns exact-candidate classification, the Browser activation plan,
cross-phase current-authority validation at its handoffs, and the complete
settled Spotlight result. It consumes owner-issued Scope, Navigation,
Platform, Workspace Definitions, host-publication, and source outcomes without
redefining their operations or lifecycles.

## Demo

Assume the active Workspace contains the curated Platform, ASP.NET Core, and
Microsoft.Extensions registrations but has not realized either form of
`System.Text.Json`.

```text
Spotlight: System.Text.Json

In this Workspace
  System.Text.Json
  Platform Library - .NET 11
  Available through Platform

Open in a new Workspace
  System.Text.Json
  Package - System.Text.Json 11.0.0-preview.7 - net10.0
  NuGet package
```

The Platform Library is registration-covered, so selecting it preserves the
active Workspace and invokes the exact Browser Platform action. The package is
not covered merely because its assembly name matches the Platform Library, so
selecting it constructs a fresh curated Workspace and adds that exact package.

If the active Workspace also registers a package prefix that matches
`System.Text.Json`, the package row moves to the first group:

```text
In this Workspace
  System.Text.Json
  Package - System.Text.Json 11.0.0-preview.7 - net10.0
  Available through System.*
```

Selecting that row explicitly adds the exact package to the current Workspace
and focuses it there. Registration itself remains inert.

## Exact claim

Spotlight classifies one exact destination against one exact active Workspace
snapshot:

- an already admitted exact subject uses its existing current-Workspace
  activation action;
- a registration-covered package or package-origin Library explicitly adds
  its exact enclosing package to the current Workspace, then requests focus;
- a registration-covered or already-realized Platform Library delegates to
  the exact Browser Platform activation action while retaining the current
  Workspace;
- a Platform Type or Member emitted from the resident Platform surface uses
  its exact Browser Platform action in the current Workspace;
- an uncovered package constructs and activates a fresh Ecosystems-curated
  Workspace containing that package; and
- an uncovered Library is unavailable rather than being reinterpreted by name
  or silently converted into a package request.

The decision uses membership and registration as relevance evidence. It does
not make registration source authorization, traversal permission, acquisition,
or membership.

## Consumed currencies

Every selectable descriptor retains these owner-issued values:

- the exact Spotlight result generation and activation intent;
- the exact active `InspectionWorkspaceIdentity`;
- the exact Scope snapshot, logical revision, and publication base used for
  membership and registration classification;
- the exact destination identity and source family;
- zero or more exact membership or registration-coverage witnesses; and
- the owner-issued action or request payload required by the selected plan.

These values form one captured activation basis. Equal Workspace labels,
package coordinates, assembly simple names, Library display names, or
registration text do not substitute for any part of that basis.

Spotlight does not invent a parallel Workspace or registration identity.
Registration content is read only from the exact Scope snapshot that owns it;
later Scope or publication-base movement invalidates that captured basis.

## Destination classification

The closed activation-plan family is:

```text
SpotlightDestinationActivationPlan
  = NavigateCurrent(exact Navigation action)
  | ActivateCurrentPackageLibrary(exact occurrence, exact Library intent)
  | AddCurrentPackage(exact package request, optional exact Library intent)
  | ActivateCurrentPlatformDestination(exact Browser Platform action)
  | RestoreExternalPackageWorkspace(exact Definitions request)
  | Unavailable(exact reason)
```

The classification table is:

| Exact destination | Current evidence | Plan |
| --- | --- | --- |
| Package, Library, Type, or Member already admitted through shared Navigation | Exact current subject action | `NavigateCurrent` |
| Package-origin Library not yet realized beneath an exact current Package occurrence | Exact occurrence and Library intent | `ActivateCurrentPackageLibrary` |
| Package covered by a package-prefix contribution | Exact coverage witnesses | `AddCurrentPackage` |
| Package-origin Library covered by an exact-Library registration or a matching package-prefix contribution, without a current enclosing Package occurrence | Exact coverage witnesses | `AddCurrentPackage` with the Library intent |
| Platform Library already realized or covered by an exact-Library or Platform-population contribution | Exact realization or coverage witnesses and Platform action | `ActivateCurrentPlatformDestination` |
| Platform Type or Member emitted from the resident Platform surface | Exact Browser Platform action | `ActivateCurrentPlatformDestination` |
| Package with no current membership or registration coverage | Exact external package coordinate | `RestoreExternalPackageWorkspace` |
| Library with no current membership or registration coverage | Exact candidate and reason | `Unavailable` |

This slice does not introduce global external-Library activation. Library
results within a Workspace come from its admitted or registered populations.
Home-to-Platform opening and Platform catalog entry remain owned by the
existing Browser Platform experience.

## Registration coverage

Coverage is an exact, resource-free classification over the complete
registration projection in the captured Scope snapshot:

- an exact-Library registration covers only its exact source coordinate and
  never covers its enclosing package as a whole;
- a package-prefix contribution uses the prefix owner's match semantics over
  the candidate's exact package ID and may cover that package or one exact
  package-origin Library within it;
- an ecosystem registration covers only through one or more of its retained
  exact-Library, Platform-population, or package-prefix contributions; its
  label, namespace hints, core-package priorities, knowledge, and scanner
  identity do not independently establish coverage; and
- current Package membership covers the Package and exact Library candidates
  attributable to that retained occurrence.

Coverage is existential: one valid witness is sufficient. When several
registrations or nested contributions cover the same destination, Spotlight
retains every exact witness in Workspace registration order and authored
population order. Classification does not select an arbitrary first witness
as binding precedence.

Every witness binds the exact Workspace, Scope revision and publication base,
retained registration identity, nested population contribution when
applicable, exact candidate, and source family. A Platform witness for
`System.Text.Json` cannot cover the package-origin candidate with the same
visible name.

Coverage says that the current Workspace is the appropriate composition
boundary. The selected operation must still honor source availability,
credentials, offline policy, target compatibility, finite work bounds, and
the source owner's typed non-success outcomes.

## Current-Workspace activation

### Existing subjects

`NavigateCurrent` submits the exact current Navigation action already issued
for the destination. It performs no Scope mutation and does not reconstruct
ancestry from Spotlight text. Browser-local Platform actions use
`ActivateCurrentPlatformDestination` instead.

### Covered packages and package-origin Libraries

`AddCurrentPackage` is an explicit user membership action. It submits the
exact package against the captured current Scope base and identifies the
requested package occurrence. A package-origin Library additionally retains
its exact Library intent beneath that enclosing package.

Registration did not add the Package; the user's selection does. A committed
Scope result therefore remains durable current-Workspace membership even if
the later focus request fails or is superseded. Traversal-derived participants
remain governed by their existing operation contracts and do not become
explicitly selected membership merely because this shortcut exists.

When the exact enclosing Package occurrence is already current but the Library
is not yet realized, `ActivateCurrentPackageLibrary` consumes that occurrence
and the exact Library intent without adding duplicate Package membership.
Focus may proceed only through owner-issued ancestry for that occurrence.

The phases are deliberately not described as one atomic transaction:

1. Scope may reject, fail, cancel, supersede, produce no effect, or commit the
   exact Package occurrence.
2. Only a committed or already-current occurrence can supply exact ancestry to
   Navigation.
3. Navigation may then apply, reject, fail, or be superseded independently.

A post-commit focus failure reports both facts: membership changed, and the
requested destination did not become active. Spotlight never rolls back the
committed Package and never converts that failure into a new-Workspace
activation.

### Platform destinations

`ActivateCurrentPlatformDestination` preserves the active Workspace and
invokes the exact host-local Platform action for a Library, Type, or Member. It
never represents Platform as a Package, manufactures package ancestry, or
promotes a package merely because the Library has a package counterpart.

A Platform Library may receive this action because current registration
coverage permits its realization or because that exact Library is already
realized in the same Workspace. Registration-free repeat activation never
transfers to a replacement Workspace.

Platform Type and Member results are emitted only from an already-resident
Platform surface. Their descriptor carries the exact Browser Platform action;
that action binds the exact Platform target, Library ancestry, Type identity,
and Member identity as applicable. Spotlight does not reinterpret it as shared
Navigation. This owner classifies the current-Workspace effect and settles the
action without redefining Platform's target, catalog, or deep-focus mechanics.

Shared Scope and Navigation currently have no Platform structural subject.
Unifying Platform Library, Type, or Member activation with the shared
`Workspace -> Package -> Library -> Type -> Member` grammar requires a separate
focused Scope, Navigation, and inventory extension. This owner exposes the
present Browser boundary rather than hiding it behind generic subject
identity.

## Fresh-Workspace activation

`RestoreExternalPackageWorkspace` applies only to an uncovered package. It
submits the exact package and curated registration intent to the existing
[Workspace Definitions complete-restoration
boundary](workspace-definitions.md#complete-restoration). Definitions owns
fresh Workspace construction, complete preparation,
`CompleteWorkspaceActivation`, and every non-install cleanup. Artifact
Acquisition owns close and resource drainage. Spotlight neither reconstructs
those phases nor introduces another new-Workspace coordinator.

The retained host may publish the returned complete activation only while the
exact Spotlight intent and captured source-Workspace basis remain current.
An active-Workspace, Scope-revision, or publication-base change can alter
whether the same package is still uncovered. A stale complete activation is
not installed; its owner-issued non-install path retains cleanup authority.
Spotlight asks the user to select from current results rather than publishing
the old plan.

## Settlement and authority

Every attempt settles with one result correlated to its exact activation
intent:

```text
SpotlightDestinationActivationResult
  Activation basis
  Classification plan
  Workspace effect
    none
    current Package membership committed
    fresh Workspace activated
  Focus effect
    applied
    not attempted
    failed
    superseded
  Exact owner results and visible failures
```

This is a product result shape, not a replacement for the contributing owner
unions. Scope, Navigation, Platform, source, and curated-construction outcomes
remain embedded or referenced without being collapsed into success-shaped
Booleans.

Authority is checked before every effect-producing handoff:

- before current Scope mutation;
- before current Navigation, package-Library, or Platform activation;
- before using a committed occurrence for lower focus; and
- before handing a `CompleteWorkspaceActivation` to host publication.

Staleness before any committed effect produces no effect. Staleness after
current Package membership committed preserves that membership and suppresses
only the later focus effect. Staleness at any point before fresh-Workspace
publication takes the Definitions-owned non-install path, which closes the
unpublished Workspace.

Failure of a covered current-Workspace plan remains visible against that
Workspace. It never falls back to `RestoreExternalPackageWorkspace`.
Supersession likewise settles the exact older attempt rather than rebasing it
onto a later Workspace or revision.

## Presentation handoff

The activation owner supplies separate typed fields for:

- exact destination and source home;
- current membership or registration-coverage relationship;
- activation effect;
- availability or failure; and
- the opaque activation action.

Presentation may group current-Workspace effects separately from
new-Workspace effects and render concise labels such as:

```text
Platform Library - .NET 11
Package - System.Text.Json 11.0.0-preview.7 - net10.0
Available through Microsoft.Extensions
Open in a new Workspace
```

Those labels are disclosure, not identity. Presentation never infers an action
from a group, badge, package icon, Library name, or source text. The Inspect
Web presentation owner retains layout, accessibility, keyboard interaction,
and the final visual vocabulary.

## Real evidence

The motivating overlap uses the real package and Platform forms of
`System.Text.Json` already preserved by the Exact Library and binding designs.
Their ECMA-335 assembly identities may compare equivalently while their MVIDs,
source coordinates, and acquisition routes remain distinct.

The required walkthrough is:

1. Platform registration covers the Platform `System.Text.Json` Library.
2. It does not cover the package `System.Text.Json` coordinate by name.
3. Selecting the Platform row preserves the current Workspace.
4. Selecting the uncovered package row creates a fresh curated Workspace.
5. Adding a matching package-prefix registration changes only the package
   row's later classification to current-Workspace admission.
6. Package acquisition failure remains a visible current-Workspace failure
   and creates no fallback Workspace.

## Model

The focused model is
[`SpotlightDestinationActivation.tla`](models/inspect-web-spotlight-destination-activation/SpotlightDestinationActivation.tla).
It represents exact source-specific candidates, complete ordered coverage
witnesses, captured Workspace/Scope/publication-base association, exact Package
occurrences, current Package commit followed by independent focus, a
Definitions-issued complete fresh activation, active-Workspace replacement,
supersession, and visible failure.

The model checks:

- same-name Platform and package candidates retain different coverage;
- a realized Platform Library selected again retains its exact Browser-local
  Platform action even after its covering registration is removed from the
  same Workspace;
- Platform Type and Member results retain their exact Browser-local actions
  rather than flowing through shared Navigation;
- a never-realized Platform Library without current coverage remains
  unavailable;
- Platform realization in one Workspace does not make the same destination
  realized in a replacement Workspace;
- every overlapping registration contribution remains in the exact ordered
  coverage projection;
- package-origin Library focus consumes the exact retained or newly issued
  Package occurrence;
- one attempt cannot publish both a current-Workspace effect and a new
  Workspace;
- covered failure never falls back to a new Workspace;
- stale current admission, direct focus, post-commit focus, and fresh
  publication cannot apply;
- current membership committed before focus failure or supersession remains
  present;
- a failed or stale Definitions result is never host-published; and
- every bounded activation attempt settles under weakly fair owner
  completion.

The model abstracts each adjacent operation to its documented observable
commit or settlement boundary. It does not copy Scope, Navigation, Platform,
source, Artifact, or host-collection state machines. Its finite results are
design evidence, not implementation conformance.

## Ownership and adoption

| Participating owner | Responsibility retained |
| --- | --- |
| [Workspace registration and call-graph focal length](workspace-registration-and-call-graph-scope.md) | Experience-level raw/curated construction and inert registration purpose |
| [Workspace Scope and Expansion](workspace-scope-and-expansion.md) | Exact membership, registration and Scope revisions, mutation admission, Package occurrence issuance, and complete results |
| [Workspace Ecosystem Registration Handoff](workspace-ecosystem-registration-handoff.md) and source owners | Retained population contributions and their owner-defined matching or realization outcomes |
| [Inspection Subject Navigation](inspection-subject-navigation.md) | Shared Package/Library/Type/Member ancestry, focus, reconciliation, and exact action authority |
| [Inspect Web Navigation Consumer](inspect-web-navigation-consumer.md) | Canonical location, history, effect installation, and consumer synchronization |
| [Inspect Web Navigation Presentation](inspect-web-navigation-presentation.md) | Grouping, labels, accessibility, and opaque-action interaction |
| Browser Platform experience | Host-local Platform target, Library action, catalog, and source-specific focus |
| [Workspace Definitions](workspace-definitions.md), Artifact Acquisition, and retained Browser host | Complete external-package activation, non-install cleanup and drainage, collection publication, and active identity |

Production adoption is staged by owner:

1. Lock this focused contract and model, then correct stale composition
   statements without changing adjacent owner internals.
2. Add the resource-free exact coverage and activation-plan projection in the
   Inspect Web managed composition path.
3. Adopt current Package and package-origin Library admission through Scope
   and Navigation, preserving partial committed membership results.
4. Adopt the existing Platform action and typed home/effect presentation in
   Spotlight.
5. Adopt the Definitions-owned complete-restoration path and current-authority
   host publication for uncovered packages.
6. Run Browser original-host and Firefox acceptance gates, then include the
   capability in a separately authorized release and website deployment.

Each implementation stage receives its own focused issue and PR. This design
does not authorize one implementation change spanning all participating
owners.

## Acceptance and evidence

| Scenario | Required observation |
| --- | --- |
| Select an already admitted Package, Library, Type, or Member | The existing exact action runs in the current Workspace; membership does not change |
| Select a package matched by a current package-prefix registration | The exact package becomes current membership and focus is requested there |
| Select a package-origin Library covered only by an exact-Library registration | Its exact enclosing package is admitted in the current Workspace; no other package asset is inferred as the destination |
| Select the Platform and package forms of `System.Text.Json` with only Platform registered | The Platform row preserves the Workspace; the package row creates a new Workspace |
| Select the Platform `System.Text.Json` Library again after it is realized | The exact Browser Platform action runs again; shared Navigation is not substituted |
| Select a Platform Type or Member returned from the resident Platform surface | Its exact Browser Platform action runs in the current Workspace; shared Navigation is not substituted |
| Remove the covering Platform registration after the Platform `System.Text.Json` Library is realized, then select it again in the same Workspace | The exact Browser Platform action still runs because realization remains current; missing registration coverage does not make it unavailable |
| Select a never-realized Platform Library after its covering registration is removed | The result is unavailable; registration removal does not manufacture realization |
| Realize a Platform Library, replace the active Workspace, remove the replacement's covering registration, then select the same Library | The replacement settles it unavailable; realization from the prior Workspace does not transfer |
| Add a package-prefix contribution covering `System.Text.Json` and obtain fresh results | The exact package row now preserves the current Workspace; the Platform row remains distinct |
| Remove or replace the covering registration before selection or completion | The stale action does not mutate or publish and reports its exact stale outcome |
| Scope commits a selected Package and Navigation then fails | Membership remains committed; focus failure is visible; no fallback Workspace is created |
| Covered package acquisition fails before Scope commit | The current Workspace is unchanged and the failure is visible there |
| Definitions fails or supersedes uncovered-package restoration | Its exact non-install cleanup runs and no complete activation reaches host publication |
| Select an uncovered Library | The result is unavailable; Spotlight does not infer an enclosing package by name |
| Source authorization denies a registration-covered candidate | The owner denial remains visible; registration does not override it |

The TLA+ configurations registered in
`eng/tla-expected-exit-codes.txt` gate the model's exact semantic outcomes.
Future implementation gates must exercise the real `System.Text.Json`
package/Platform overlap through product-owned candidate construction, plus
current Scope commit followed by Navigation failure, stale revision,
supersession, and source denial. Workspace Definitions retains its separate
non-install cleanup gate for external-package restoration.

Presentation is Browser-native stateful interaction and does not require
Markout. The model and future Browser tests are the selected positive gates;
no source-prohibition or host-inventory absence claim is made.

## Non-claims

This design does not define:

- registration construction, validation, persistence, or mutation;
- package-prefix, ecosystem, or Platform-population enumeration;
- source authorization, credentials, offline policy, acquisition, or caching;
- Package or Library ranking;
- shared Platform Scope membership or Navigation subjects;
- Workspace editor Save, Add, or arbitrary multi-package composition;
- call-graph focal lengths, traversal, binding, or pruning;
- Spotlight package-search request and cache lifecycle;
- shell modal placement, keyboard behavior, visual styling, or accessibility;
- CLI behavior or a shared Spotlight abstraction; or
- release or website deployment authorization.
