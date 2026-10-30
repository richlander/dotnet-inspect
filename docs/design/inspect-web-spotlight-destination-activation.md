# Inspect Web Spotlight destination activation

## Status and owner

This is the approved, staged interaction contract for
[#6673](https://github.com/richlander/dotnet-inspect/issues/6673), under the
Workspace experience tracker
[#6012](https://github.com/richlander/dotnet-inspect/issues/6012). The operator
approved this focused Browser-specific capability: Spotlight preserves the
active Workspace for destinations already admitted or covered by its current
Scope and registration revisions, and creates a fresh curated Workspace only
for an uncovered package. The resource-free coverage and activation-plan
projection is implemented by `BrowserSpotlightDestinationProjection`.
Current-Workspace Package and package-origin Library execution is implemented by
`BrowserSpotlightCurrentPackageActivation`. Package-backed Type destinations
now retain exact package, Library, and structured metadata Type intent through
already-current focus or package admission. Fresh-Workspace managed execution
accepts either an exact schema-version-3 Package request or a nonprojectable
Definitions package-Type request carried by the rendered descriptor and
composes it with the real retained Browser owner;
production Spotlight now consumes managed Type Find candidates, submits only
their opaque actions through the shared dispatcher, and installs exact Package
or framework Type effects. Package and package-origin Library result
publication still require the remaining interaction binding. The previously
implemented
`BrowserSpotlightCurrentPlatformActivation` and
`BrowserSpotlightDestinationPresentation` types are retained staged artifacts,
not production Browser adoption. Projection and execution must consume the
exact active realization rather than a Workspace reconstructed from frontend
state.
The prerequisite split is tracked by
[#7027](https://github.com/richlander/dotnet-inspect/issues/7027),
[#7028](https://github.com/richlander/dotnet-inspect/issues/7028),
[#7031](https://github.com/richlander/dotnet-inspect/issues/7031),
and [#7030](https://github.com/richlander/dotnet-inspect/issues/7030);
[#6686](https://github.com/richlander/dotnet-inspect/issues/6686) remains the
final thin Browser integration and acceptance slice.
The focused package-backed indexed Type extension is tracked by
[#10560](https://github.com/richlander/dotnet-inspect/issues/10560); its
projection and execution contract is implemented. Indexed Type Spotlight
adoption is tracked by
[#10778](https://github.com/richlander/dotnet-inspect/issues/10778).
Package-backed Member activation remains a separate later owner effort.

This document is the normative owner of **Spotlight destination activation**.
It owns exact-candidate classification, the Browser activation plan,
cross-phase current-authority validation at its handoffs, and the complete
settled Spotlight result. It consumes owner-issued Scope, Navigation,
Platform, Workspace Definitions, host-publication, and source outcomes without
redefining their operations or lifecycles.

## Spotlight presentation boundary

Spotlight does not offer Platform as a scope, component, or root destination.
Installed framework assemblies may be presented as ordinary Library results,
with `.NET` or `ASP.NET Core` source disclosure, and the Browser may use
Platform-owned realization internally when such a Library is selected.
Platform provenance does not create a Platform result.

The Platform-specific projection, execution types, and model branches were
implemented as a staged managed capability before this product decision. They
are historical, non-normative artifacts and remain outside production Browser
adoption. [#7029](https://github.com/richlander/dotnet-inspect/issues/7029) is
closed as not planned, and
[#6686](https://github.com/richlander/dotnet-inspect/issues/6686) must not
connect those paths. The presentation boundary above is authoritative for
user-visible Spotlight behavior.

## Demo

Search for `System.Text.Json` while both its NuGet package and framework
assembly are available.

```text
Spotlight: System.Text.Json

Libraries
  System.Text.Json
  .NET library - net11.0 - 11.0.0

Packages
  System.Text.Json
  NuGet - 11.0.0-preview.7
```

Selecting the framework Library uses Browser-owned realization internally and
opens the Library without exposing a Platform destination before, during, or
after activation. Selecting the package follows the activation plan described
below. Registration itself remains inert.

## Exact claim

Spotlight classifies one exact destination against one exact active Workspace
snapshot:

- an already admitted exact subject uses its existing current-Workspace
  activation action;
- a registration-covered package or package-origin Library explicitly adds
  its exact enclosing package to the current Workspace, then requests focus;
- an uncovered package constructs and activates a fresh Ecosystems-curated
  Workspace containing that package;
- a package-backed Type not already admitted retains its exact package,
  Library, and structured Metadata Type intent through current-package
  admission or fresh-Workspace restoration and opens that exact declaration;
  and
- an uncovered Library is unavailable rather than being reinterpreted by name
  or silently converted into a package request.

Framework Library results are outside this managed activation-plan family.
The Browser opens them through
[framework declaration activation](inspect-web-framework-declaration-activation.md)
and its source-owner realization path under the
[Spotlight presentation boundary](#spotlight-presentation-boundary).

The decision uses membership and registration as relevance evidence. It does
not make registration source authorization, traversal permission, acquisition,
or membership.

## Consumed currencies

Every selectable descriptor retains these owner-issued values:

- the exact Spotlight result generation;
- the exact active `InspectionWorkspaceIdentity`;
- the exact Scope snapshot, logical revision, and publication base used for
  membership classification;
- the exact separate `WorkspaceRegistrationRevision` used for registration
  classification;
- the exact destination identity and source family;
- for a package-backed Type, its exact package request,
  `ExactLibrarySourceCoordinate.Package`, and
  `MetadataTypeDefinitionName`;
- zero or more exact membership or registration-coverage witnesses; and
- the source-bound package request or Workspace-bound owner action required by
  the selected plan.

These values form one captured activation basis. Equal Workspace labels,
package coordinates, assembly simple names, Library display names, or
registration text do not substitute for any part of that basis.

Spotlight does not invent a parallel Workspace or registration identity. It
accepts Scope and registration snapshots only when both belong to the same
exact Workspace and preserves their independent revisions. Later registration,
Scope, or publication-base movement invalidates that captured basis; replacing
registrations does not manufacture a new Scope revision.

Classification occurs when Spotlight renders the selectable result descriptor,
not when the user later selects it. Selection issues a new activation intent
and copies the descriptor's captured basis and plan into that attempt. It must
not reclassify the old row from live Workspace or Scope state. If the captured
basis is no longer current, the attempt settles stale before any current- or
fresh-Workspace effect.

The attempt retains whether the descriptor was current at selection and, when
stale, which captured basis dimension first differed: Workspace identity,
registration profile, Scope revision, or publication base. This retained
selection fact distinguishes a row that was already stale when clicked from an
attempt that became stale only after selection.

A later result generation may replace an unselected stale descriptor with a
freshly classified descriptor. Rendering that newer result does not supersede
an activation that already started from an earlier descriptor; activation
intent remains the post-selection supersession currency.

## Destination classification

The closed activation-plan family is:

```text
SpotlightDestinationActivationPlan
  = NavigateCurrent(exact Navigation action)
  | ActivateCurrentPackageLibrary(exact occurrence, exact Library intent)
  | ActivateCurrentPackageType(exact occurrence, exact Type intent)
  | AddCurrentPackage(exact package request, optional exact Library intent)
  | AddCurrentPackageType(exact package request, exact Type intent)
  | RestoreExternalPackageWorkspace(exact Definitions request)
  | RestoreExternalPackageTypeWorkspace(
      exact Definitions request carrying an exact Type preparation intent)
  | Unavailable(exact reason)
```

The classification table is:

| Exact destination | Current evidence | Plan |
| --- | --- | --- |
| Package, Library, Type, or Member already admitted through shared Navigation | Exact current subject action | `NavigateCurrent` |
| Package-origin Library not yet realized beneath an exact current Package occurrence | Exact occurrence and Library intent | `ActivateCurrentPackageLibrary` |
| Package-backed Type not yet admitted beneath an exact current Package occurrence | Exact occurrence and Type intent | `ActivateCurrentPackageType` |
| Package covered by a package-prefix contribution | Exact coverage witnesses | `AddCurrentPackage` |
| Package-origin Library covered by an exact-Library registration or a matching package-prefix contribution, without a current enclosing Package occurrence | Exact coverage witnesses | `AddCurrentPackage` with the Library intent |
| Package-backed Type whose package is covered by a matching package-prefix contribution, without a current enclosing Package occurrence | Exact coverage witnesses and Type intent | `AddCurrentPackageType` |
| Package with no current membership or registration coverage | Exact external package coordinate | `RestoreExternalPackageWorkspace` |
| Package-backed Type whose package has no current membership or registration coverage | Exact external package coordinate and Type intent | `RestoreExternalPackageTypeWorkspace` |
| Library with no current membership or registration coverage | Exact candidate and reason | `Unavailable` |

This slice does not introduce global external-Library activation. Library
results within a Workspace come from its admitted or registered populations.
Framework Library discovery and Browser-owned realization remain outside this
managed activation plan.

## Registration coverage

Coverage is an exact, resource-free classification over the complete captured
registration revision, joined to the captured Scope through their shared exact
Workspace identity:

- an exact-Library registration covers only its exact source coordinate and
  never covers its enclosing package as a whole;
- a package-prefix contribution uses the prefix owner's match semantics over
  the candidate's exact package ID and may cover that package or one exact
  package-origin Library within it;
- an ecosystem registration covers only through one or more of its retained
  exact-Library or package-prefix contributions; its
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
registration revision, registration position, nested population contribution
and authored position when applicable, exact candidate, and source family.

Coverage says that the current Workspace is the appropriate composition
boundary. The selected operation must still honor source availability,
credentials, offline policy, target compatibility, finite work bounds, and
the source owner's typed non-success outcomes.

## Current-Workspace activation

### Existing subjects

`NavigateCurrent` submits the exact current Navigation action already issued
for the destination. It performs no Scope mutation and does not reconstruct
ancestry from Spotlight text.

### Covered packages and package-origin Libraries

`AddCurrentPackage` is an explicit user membership action. It submits the
exact package against the captured current Scope base and identifies the
requested package occurrence. A package-origin Library additionally retains
its exact Library intent beneath that enclosing package.

The managed execution boundary acquires the resource-bearing
`PackageRootBinding` through a caller-supplied source operation, then submits it
through Scope's publication-base-guarded add operation. Scope rejects the
handoff when either the logical revision or publication base no longer matches
the captured basis. A committed or no-effect result is resolved back to the
exact Scope-issued occurrence by complete Package artifact-root
correspondence; package ID, version, display text, or list position cannot
substitute for that correspondence.

Registration did not add the Package; the user's selection does. A committed
Scope result therefore remains durable current-Workspace membership even if
the later focus request fails or is superseded. Traversal-derived participants
remain governed by their existing operation contracts and do not become
explicitly selected membership merely because this shortcut exists.

When the exact enclosing Package occurrence is already current but the Library
is not yet realized, `ActivateCurrentPackageLibrary` consumes that occurrence
and the exact Library intent without adding duplicate Package membership. The
package request must identify the occurrence's complete realized coordinate,
including producer and target, rather than merely matching its package ID and
version. Focus may proceed only through owner-issued ancestry for that
occurrence.

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

The execution boundary carries the exact occurrence, optional Library intent,
successful Scope result, and source request into a narrow Navigation-operation
port. The port returns Navigation's existing complete operation result; this
owner does not reconstruct Navigation actions or collapse rejection, failure,
or supersession. General protected consumption of Workspace membership results
by Navigation remains owned by
[#5584](https://github.com/richlander/dotnet-inspect/issues/5584), not this
stage.

### Package-backed Types

`ActivateCurrentPackageType` and `AddCurrentPackageType` retain one exact Type
intent composed from the exact package request,
`ExactLibrarySourceCoordinate.Package`, and
`MetadataTypeDefinitionName`. Display namespace, Type spelling, assembly
simple name, package ID, version, asset path, and row position are not
independently sufficient identity.

When the exact enclosing Package occurrence is already current, activation
passes that occurrence and Type intent to a narrow Navigation-operation port.
When Scope first commits the Package, activation passes only the exact
Scope-issued occurrence returned by that successful result. The port binds the
Type against owner-issued ancestry for that occurrence and returns
Navigation's exact complete operation result. Zero or multiple bindings,
incomplete inventory, a foreign occurrence, stale authority, and Navigation
non-success remain typed outcomes; this owner does not search by display name
or select an arbitrary candidate.

Current Package membership remains durable when later Type focus fails or is
superseded, exactly as for package-origin Library focus. Failure never falls
back to a fresh Workspace.

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

`RestoreExternalPackageTypeWorkspace` carries the exact Type intent into the
Definitions-owned registration-only request. The intent remains a nonportable
in-process input: it is not encoded into or reconstructed from a Definitions
packet. During private Workspace construction, Definitions resolves that
intent against the exact restored Package occurrence and Library asset and
uses ordinary Navigation construction to prepare the structured Type subject.
The returned typed result includes both the complete activation and its
owner-issued exact selected-Type preparation. This contract is the focused
Workspace Definitions prerequisite tracked by
[#10566](https://github.com/richlander/dotnet-inspect/issues/10566).

Spotlight performs no operation against the candidate after Definitions
returns. Host publication is eligible only when the Definitions result proves
that the prepared selected Type exactly matches the retained intent.
Unavailable or ambiguous Type binding, Navigation failure, cancellation, or
supersession therefore fails complete restoration before construction access
is released and takes the Definitions-owned non-install path. Stale source
authority detected after completion also takes that path. The fresh Workspace
is not published first and corrected afterward, and the package root is not
presented as a success-shaped fallback. Publication rejection after successful
Type preparation likewise retains the existing non-install cleanup authority.

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
    prepared by Definitions for fresh publication
    not attempted
    failed
    superseded
  Exact owner results and visible failures
```

This is a product result shape, not a replacement for the contributing owner
unions. Scope, Navigation, source, and curated-construction outcomes remain
embedded or referenced without being collapsed into success-shaped Booleans.

Authority is checked before every effect-producing handoff:

- before current Scope mutation;
- before current Navigation or package-Library activation;
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
Library - Example.Package 1.0.0 - net10.0
Package - System.Text.Json 11.0.0-preview.7 - net10.0
Available through Microsoft.Extensions
Open in a new Workspace
```

Those labels are disclosure, not identity. Presentation never infers an action
from a group, badge, package icon, Library name, or source text. The Inspect
Web presentation owner retains layout, accessibility, keyboard interaction,
and the final visual vocabulary.

## Real evidence

The motivating overlap uses the real package and framework-Library forms of
`System.Text.Json`. Their ECMA-335 assembly identities may compare equivalently
while their source coordinates and acquisition routes remain distinct.

The motivating package-backed Type is
`Microsoft.Extensions.Hosting.AspireRedisExtensions` from
`Aspire.StackExchange.Redis@13.6.3`,
`lib/net10.0/Aspire.StackExchange.Redis.dll`. The checked-in Aspire Ecosystem
Find Index retains that exact package, Library, asset, assembly, and structured
Metadata Type identity.

The required walkthrough is:

1. Spotlight presents the framework assembly only as a `.NET` Library.
2. It presents the package `System.Text.Json` coordinate independently.
3. Selecting the framework Library exposes no Platform scope or root.
4. Selecting the uncovered package row creates a fresh curated Workspace.
5. Adding a matching package-prefix registration changes only the package
   row's later classification to current-Workspace admission.
6. Package acquisition failure remains a visible current-Workspace failure
   and creates no fallback Workspace.
7. Selecting the indexed Aspire Type through a registered Aspire population
   admits its exact Package occurrence and opens that Type in the current
   Workspace.
8. Selecting the same Type through request-scoped Aspire admission carries its
   exact intent into Definitions, which prepares the Type during construction
   and returns it with the fresh curated Workspace before host publication.

## Model

The focused model is
[`SpotlightDestinationActivation.tla`](models/inspect-web-spotlight-destination-activation/SpotlightDestinationActivation.tla).
It represents exact source-specific candidates, complete ordered coverage
witnesses, rendered result generations with captured
Workspace/Scope/publication-base association, exact Package occurrences,
selection-time transfer into activation intent, current Package commit followed
by independent focus, a Definitions-issued complete fresh activation,
active-Workspace replacement, supersession, and visible failure.
It also represents a package-backed Aspire Type across current Package
admission, already-current Package focus, Definitions construction-time exact
Type preparation, and fresh publication.

The model predates the
[Spotlight presentation boundary](#spotlight-presentation-boundary). Its
Platform-specific branches are retained as historical evidence for the staged
managed types and are not production requirements. The active model claims
check that:

- every overlapping registration contribution remains in the exact ordered
  coverage projection;
- selection copies the rendered descriptor's captured plan and basis rather
  than reclassifying it from live Workspace state;
- registration change or active-Workspace replacement before selection makes
  the old descriptor settle stale without membership, focus, or fresh
  publication;
- the preselection evidence distinguishes registration-stale and
  Workspace-stale selection from ordinary post-selection staleness;
- package-origin Library focus consumes the exact retained or newly issued
  Package occurrence;
- one attempt cannot publish both a current-Workspace effect and a new
  Workspace;
- covered failure never falls back to a new Workspace;
- stale current admission, direct focus, post-commit focus, and fresh
  publication cannot apply;
- current membership committed before focus failure or supersession remains
  present;
- a failed or stale Definitions result is never host-published;
- a fresh package-backed Type Workspace is never published without an exact
  selected-Type preparation returned by Definitions; and
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
| [Workspace Scope and Expansion](workspace-scope-and-expansion.md) | Exact membership and Scope revisions, mutation admission, Package occurrence issuance, and complete results |
| [Workspace Ecosystem Registration Handoff](workspace-ecosystem-registration-handoff.md) and source owners | Retained population contributions and their owner-defined matching or realization outcomes |
| [Inspection Subject Navigation](inspection-subject-navigation.md) | Shared Package/Library/Type/Member ancestry, focus, reconciliation, and exact action authority |
| [Inspect Web Navigation Consumer](inspect-web-navigation-consumer.md) | Canonical location, history, effect installation, and consumer synchronization |
| [Inspect Web Navigation Presentation](inspect-web-navigation-presentation.md) | Grouping, labels, accessibility, and opaque-action interaction |
| [Inspect Web framework declaration activation](inspect-web-framework-declaration-activation.md) | Exact framework occurrence binding, Browser-local Library/Type actions, and detached effects |
| PlatformHouse and Browser framework realization | Internal target, view, Library acquisition, catalog, projection, and provenance |
| [Workspace Definitions](workspace-definitions.md), Artifact Acquisition, and retained Browser host | Complete external-package activation, construction-time selected-subject preparation, non-install cleanup and drainage, collection publication, and active identity |

Production adoption is staged by owner:

1. Lock this focused contract and model, then correct stale composition
   statements without changing adjacent owner internals.
2. Add the resource-free exact coverage and activation-plan projection in the
   Inspect Web managed composition path.
3. Adopt current Package and package-origin Library admission through Scope
   and Navigation, preserving partial committed membership results.
4. Keep the staged Platform action and typed presentation types outside
   production Spotlight; #7029 is retired.
5. Adopt the Definitions-owned complete-restoration path and current-authority
   host publication for uncovered packages.
6. Add Definitions-owned construction-time selected-Type preparation for the
   nonportable registration-only request in #10566.
7. Carry exact package-backed Type intents through current Package admission
   or consume that fresh-restoration result in #10560.
8. Run Browser original-host and Firefox acceptance gates, then include the
   capability in a separately authorized release and website deployment.

Stage 2 is implemented in the managed Inspect Web composition layer. Its
source-bound package requests, Workspace-bound generic action payloads, and
exact Library intents preserve owner-issued values opaquely; the projection
never reconstructs or executes them.

Stage 3 is implemented in the managed Inspect Web composition layer for
`AddCurrentPackage` and `ActivateCurrentPackageLibrary`. It validates the
captured Workspace, registration revision, Scope revision, and publication
base before acquisition, after acquisition, and after successful Scope
settlement. The final pre-Scope handoff is atomically guarded by Scope's exact
publication base. Acquisition, membership, and focus remain independently
typed results; committed or already-current membership survives Navigation
failure or supersession. The boundary neither creates a fallback Workspace nor
publishes host state.

Stage 3's retained-host adoption enters `NavigateCurrent`,
`AddCurrentPackage`, and `ActivateCurrentPackageLibrary` through the exact
active realization. The admitted operation lease spans Navigation or the
complete Package membership-and-focus operation. Admission unavailability
remains distinct from the existing detailed Package result, and an acquisition
failure cannot select the fresh-Workspace path. A predecessor operation may
finish after replacement; final presentation still rejects its stale
realization association under #6686.

Stage 4's `BrowserSpotlightCurrentPlatformActivation` and
`BrowserSpotlightDestinationPresentation` implementation is retained only as
historical staged capability. It has no production adoption step and must not
be connected to Spotlight.

Stage 5 is implemented at the managed Definitions and retained-host
composition boundary. `BrowserSpotlightExternalPackageActivation` accepts
`RestoreExternalPackageWorkspace` or
`RestoreExternalPackageTypeWorkspace`, validates the source activation basis,
obtains one opaque retained-host intent authority, and submits the exact
Definitions request captured in the rendered Package descriptor. The request
combines the exact Package coordinate with the exact Ecosystems-owned curated
`WorkspacePlan`; a Type request additionally retains the exact Library and
structured metadata Type identity. The shared Core accepts that plan from its
caller and does not reach into the facade-only Ecosystems catalog.
This is fresh Workspace construction rather than an isolated package-local
query, so the plan's traversal target supplies the context acquisition
framework. Because schema version 3 does not encode a configured traversal
target, this path accepts only the curated plan's product-default policy and
retains that exact policy after restoration.

The production Type slice consumes the managed locator envelope without
reconstructing destination authority in TypeScript. Result-local answer and
candidate ordinals join each displayed row to one opaque action, while stable
typed candidate evidence owns rendered identity across asynchronous refresh.
Package Type Navigation uses the active retained Workspace's exact effect
authority lifecycle and selected declaration coordinate; framework Type
activation installs the framework owner's exact detached surface. Package and
package-origin Library Spotlight rows remain outside this production cutover.

Scanner-bearing ecosystem registrations may make the completed definition
nonprojectable, so the retained record preserves the Definitions request and
typed projection evidence rather than fabricating a packet. Only a typed
complete Definitions result can reach synchronous retained-host publication.
A source-basis or host-authority rejection after completion invokes the
one-shot non-install operation exactly once; a Definitions failure is retained
without publication or duplicate cleanup. The live retained-Workspace
collection, TypeScript selection intent, HTML effects, and end-to-end Browser
acceptance remain owned by
[#6686](https://github.com/richlander/dotnet-inspect/issues/6686).

Stage 6 is implemented by the Definitions-owned registration-only
package-Type request. Stage 7 is implemented by the closed
`CurrentPackageType`, `PackageType`, `ActivateCurrentPackageType`,
`AddCurrentPackageType`, and `RestoreExternalPackageTypeWorkspace` arms.
Current admission retains committed Package membership independently from
later Type focus; fresh publication validates the Definitions-issued exact
selected-Type preparation before cutover. Indexed Type Spotlight composition
now retains current package-scope Navigation evidence and submits only the
opaque action associated with the exact indexed package, Library, and Type.
Published Firefox/Wasm acceptance, complete Ecosystem Find continuation, and
Member activation remain the separately counted successors in
[#10090](https://github.com/richlander/dotnet-inspect/issues/10090).

Each implementation stage receives its own focused issue and PR. This design
does not authorize one implementation change spanning all participating
owners.

Production adoption follows this dependency order:

1. Workspace Definitions implements complete fresh-Workspace restoration and
   non-install cleanup in
   [#7027](https://github.com/richlander/dotnet-inspect/issues/7027).
2. The retained Browser host consumes that result as its one definition
   activation transaction in
   [#7028](https://github.com/richlander/dotnet-inspect/issues/7028).
3. Fresh-Workspace producers, including an uncovered Spotlight Package, adopt
   that transaction in
   [#7031](https://github.com/richlander/dotnet-inspect/issues/7031).
4. Current Package and package-origin Library actions enter through the exact
   active realization in
   [#7030](https://github.com/richlander/dotnet-inspect/issues/7030).
5. [#10566](https://github.com/richlander/dotnet-inspect/issues/10566) prepares
   an exact package-backed Type during Definitions-owned private construction
   and returns the owner-issued selected-Type result.
6. [#10560](https://github.com/richlander/dotnet-inspect/issues/10560) carries
   exact package-backed Type intents through current Workspace settlement or
   consumes that fresh-Workspace result.
7. [#6686](https://github.com/richlander/dotnet-inspect/issues/6686) connects
   the remaining owner-issued paths to Spotlight interaction and runs the
   complete Package and Library Browser acceptance matrix without Platform
   actions.

The final slice may enumerate finite exact-Library registrations and ecosystem
exact-Library populations for projection. Package-prefix registrations classify
known candidates but do not create an enumerable Library population. No stage
may construct a shadow Workspace, reconstruct a managed activation from display
fields, or treat a frontend-retained record as live Workspace authority.

## Acceptance and evidence

| Scenario | Required observation |
| --- | --- |
| Select an already admitted Package, Library, Type, or Member | The existing exact action runs in the current Workspace; membership does not change |
| Select a package matched by a current package-prefix registration | The exact package becomes current membership and focus is requested there |
| Select a package-origin Library covered only by an exact-Library registration | Its exact enclosing package is admitted in the current Workspace; no other package asset is inferred as the destination |
| Search for framework and package forms of `System.Text.Json` | The framework assembly is an ordinary Library row, the package remains distinct, and no Platform affordance appears |
| Select a framework Library whose implementation is not resident | The prior surface remains visible during acquisition and success opens the Library directly |
| Fail framework Library acquisition | The prior surface is restored with a Library-only failure; no Platform root or status appears |
| Add a package-prefix contribution covering `System.Text.Json` and obtain fresh results | The exact package row now preserves the current Workspace; the framework Library row remains independently sourced |
| Render an uncovered package row, add a covering registration, then select the old row | The captured external-Package plan settles stale; selection does not reclassify it into current membership |
| Remove or replace the covering registration during activation | The stale action does not mutate or publish and reports its exact stale outcome |
| Scope commits a selected Package and Navigation then fails | Membership remains committed; focus failure is visible; no fallback Workspace is created |
| Covered package acquisition fails before Scope commit | The current Workspace is unchanged and the failure is visible there |
| Definitions fails or supersedes uncovered-package restoration | Its exact non-install cleanup runs and no complete activation reaches host publication |
| Select an uncovered Library | The result is unavailable; Spotlight does not infer an enclosing package by name |
| Select an indexed Type whose package is covered but absent | The exact package becomes current membership and the exact Type opens through its Scope-issued occurrence |
| Select an indexed Type whose package is already current | The existing exact occurrence is retained and the exact Type opens without duplicate Package admission |
| Select an indexed Type from a request-scoped uncovered Ecosystem | Definitions prepares that exact Type during private construction and the fresh curated Workspace is published only with its owner-issued selected-Type result |
| Exact indexed Type binding is unavailable or ambiguous during fresh restoration | Definitions returns typed non-success before releasing candidate access; no Workspace is published and cleanup runs |
| Source authorization denies a registration-covered candidate | The owner denial remains visible; registration does not override it |

The TLA+ configurations registered in
`eng/tla-expected-exit-codes.txt` gate the model's exact semantic outcomes.
`BrowserSpotlightDestinationProjectionTests` gates every projection plan arm,
ordered overlapping witnesses for admitted and registration-covered Package
and Library subjects, exact-Library-only coverage, independent Scope and
registration basis validation, stale and foreign Workspace evidence,
same-ID/version source-request separation, stale Package occurrences, and the
real package and framework-Library `System.Text.Json` overlap.
`BrowserSpotlightPackageActivationTests` gates prefix-covered and
exact-Library-only Package admission, already-current Library activation,
duplicate no-effect admission, source denial, Scope failure, stale
registration before and during activation, exact occurrence propagation, and
committed membership followed by Navigation failure or supersession.
`BrowserSpotlightPlatformActivationTests` preserves historical evidence for
the staged, non-production Platform activation types; it is not a production
Spotlight acceptance gate.
`BrowserSpotlightRetainedWorkspaceActivationTests` gates exact curated
Definitions capture, real retained-owner publication of a nonprojectable
Workspace, stale source-registration settlement without cutover, and
exact-realization operation admission.
The #10566 implementation gates Definitions-owned construction-time
preparation for the real
`Aspire.StackExchange.Redis@13.6.3` Type intent, exact restored occurrence and
Library identity, unavailable and ambiguous binding, and failure before
candidate release. The #10560 implementation adds focused destination,
current-Package, and prepared-result consumption gates for the same
`Aspire.StackExchange.Redis@13.6.3` Type intent, same-spelled cross-source
identity, unavailable and ambiguous binding, post-membership focus failure,
prepublication prepared-subject refusal, and stale settlement.
`WorkspaceScopeTests` gates publication-base-guarded admission and exact
binding-to-occurrence resolution. Workspace Definitions retains its separate
non-install cleanup gate for external-package restoration. Selection-intent
authority, active-Workspace publication, and Browser end-to-end presentation
remain end-to-end adoption work under
[#6686](https://github.com/richlander/dotnet-inspect/issues/6686).

Presentation is Browser-native stateful interaction and does not require
Markout. The model and future Browser tests are the selected positive gates;
no source-prohibition or host-inventory absence claim is made.

The package-backed Type intent is a new join currency, and
Definitions-returned selected-Type preparation is a new lifecycle state in the
focused model. The package-Type safety, liveness, reachability, and mutation
configurations gate that extension alongside the existing source-basis,
publication, and non-install transitions.

## Non-claims

This design does not define:

- registration construction, validation, persistence, or mutation;
- package-prefix, ecosystem, or Platform-population enumeration;
- source authorization, credentials, offline policy, acquisition, or caching;
- Package or Library ranking within a current- or new-Workspace group;
- shared Platform Scope membership or Navigation subjects;
- Workspace editor Save, Add, or arbitrary multi-package composition;
- call-graph focal lengths, traversal, binding, or pruning;
- Spotlight package-search request and cache lifecycle;
- Ecosystem Find artifact admission, semantic evaluation, Browser transport,
  phase composition, ranking, or result presentation;
- package-backed Member destination or Member activation;
- shell modal placement, keyboard behavior, visual styling, or accessibility;
- CLI behavior or a shared Spotlight abstraction; or
- release or website deployment authorization.
