# Browser Diff targets

This document owns the browser's Package-scoped comparison targets inherited
while navigating within that Package: the two compare scenarios, their depths,
the Specific version baseline, and which checked scenarios
[background analysis](inspect-web-background-analysis.md#default-baselines)
runs at depth 1. It does not own comparison execution, version window projection, or
acquisition authority. The production consumer is the Library Diff experience
tracked by
[#7213](https://github.com/richlander/dotnet-inspect/issues/7213).
The target-settings implementation is tracked by
[#6156](https://github.com/richlander/dotnet-inspect/issues/6156).

Clone candidate scope moved to the shared
[Structural Clone Search Scope](structural-clone-search-scope.md) owner under
[#6282](https://github.com/richlander/dotnet-inspect/issues/6282) and was
revised under [#6289](https://github.com/richlander/dotnet-inspect/issues/6289).
The current Package-specific Clone selector remains implemented only until
that contract's Inspect Web adoption stage retires it. It is not a target
contract or compatibility surface.

## Boundary

Package Compare owns the controls. Library, Type, and Member consumers read
the same Package settings, narrow their own queries, and offer **Change target**
back to Package Compare. They do not maintain independent coordinate controls.
This chooses one of #5083's proposed placements with a Package Compare inspector
for target setup.

Settings belong to the live browser Package model, not its display name or a
package-id-only cache key. Navigation within that model preserves settings.
Replacing its version or framework, removing it, or replacing the Workspace
discards them; a newly retained Package starts with defaults. Two retained
models do not share explicit choices merely because their coordinates match.
An unsuccessful Workspace replacement restores the prior settings along with
the browser's existing rollback snapshot, including associations to its
restored Package models.

Settings belong to the retained Package model. Shared Diff links capture the
effective exact baseline and content through the inert query attachment owned
by [Workspace definitions](workspace-definitions.md#exact-diff-query-attachment).
Clone scope remains session-local. Neither is retained product authority. They do not mint occurrence handles, establish
library correspondence, authorize a source, acquire comparison payloads, or
prove that a selected target can be inspected. Execution must consume the
appropriate product-owned acquisition and query outcomes.

## Diff

The website asks questions, not version pairs. Package Compare offers three
targets, each an independent request:

| Target | Default | Question | Compares |
| --- | --- | --- | --- |
| **Supply chain** | Checked, depth 1 | What changed in the releases I would take? | API and Body on every step |
| **API evolution** | Checked, depth 1 | How did the API reach its current shape? | API on every step |
| **Specific version** | Unchecked | How does this version differ from one I name? | API and Body on one pair |

Supply chain and API evolution are the `SupplyChain` and `ApiEvolution`
policies of the [version window
projection](version-resolution.md#version-window-projection). That owner
selects their points from the active version and the authoritative listing;
this owner chooses only which policies are checked and at what depth. Each has
its own depth, a native select from 1 to 6. Supply chain's depth counts
earlier releases, and API evolution's counts earlier majors, so one shared
depth control would mean two different things. With both at depth 6 and a
Specific version, the targets name at most thirteen earlier versions, which
bounds what they can ask Package acquisition to fetch.

A scenario whose window is shorter than its depth shows the depth it reached,
such as "2 of 6 earlier majors". A window with no earlier point, unknown
listing authority, and a failed inventory request are distinct visible
outcomes in that scenario's row. Unknown listing state cannot justify scenario
points, because the projection requires an authoritative listing. When no
target is checked, Compare shows that no comparison is selected rather than an
empty result.

The checked targets are separate requests whose composition belongs to [Query
Space composition](query-space-composition.md#request-sets-and-source-plan-collapse).
Neither result derives from another, and one target's failure leaves the
others' results visible.

### Specific version

Specific version keeps the exact pair that Package Compare has always offered.
It is unchecked by default, so the Library Compare a reader first sees answers
the two scenarios rather than one pair. Its automatic choice is the Supply chain point at depth 1: the highest
candidate strictly below the active version in NuGet release precedence, as
the projection defines it. That is the single definition of the automatic
previous version. Stable coordinates consider stable releases; preview
coordinates also consider previews. Build metadata does not create an earlier
release. This follows the existing preference for listed stable automatic
selection, rather than treating a release candidate as the default predecessor
of every stable release.

The version inventory and ordering come from managed code using NuGet's version
semantics. The browser does not implement a second semantic-version comparator.
The coordinate selector includes the active version even when the inventory
omits it, at its native release-order position. That display-only option does
not add an available exact Diff candidate. The inventory carries the native
insertion position separately, including when listing authority is unknown.
The first adopter uses the built-in Gallery authorization and bounded source
operation to settle a detached listing through PackageHouse. The inventory
consumes that owner-issued listing Document rather than a live Gallery result.
Other origins must not borrow Gallery's inventory for a same-named package.

An explicit choice can select any returned exact version, including an
unlisted version or the current version. Returning to the automatic choice
reinstates the default. No preceding candidate, unavailable listing authority,
and a failed inventory request are distinct visible outcomes. Unknown listing
state may leave exact choices usable but cannot justify an automatic default.

### Inventory and links

Inventory loading does not execute a comparison. It remains bounded by the
existing source and Browser package-operation owners. A completed request may
publish only into the same still-retained Package model and request entry;
discarding the model retires its pending publication. Failure is visible and
retry is explicit, not an automatic render/retry loop.

A Compare view of one pair, including one step of a scenario, keeps the
existing [exact Diff query
attachment](workspace-definitions.md#exact-diff-query-attachment): its
baseline is that pair's exact Before version. A shared link to a whole
scenario must pin the exact points the page showed, not the policy, so that a
later release does not change what the link opens. That attachment carries one
baseline today. Extending it to a scenario's points belongs to
[Workspace definitions](workspace-definitions.md), and the scenario controls
do not ship before it.

## Presentation and adoption

Controls use the browser's existing native form and DOM-rendering conventions.
This is deliberately host-specific rather than a Markout lowering: it edits
typed interaction state, not comparison evidence. Actual diff presentation
continues to consume the shared comparison and presentation owners.

Package Compare composes the settings as one task-oriented **Comparison
targets** work area, not as prose interrupted by a generic coordinate form.
Supply chain, API evolution, Specific version, and Clone search scope each
occupy one row that keeps its persistent task label, checkbox where it has one,
native selector, effective selection or failure state, and any recovery action
together. A scenario row's effective state names its exact points, so the
reader sees which versions the scenario compares. A wide row gives the selector the available
content width instead of inheriting the shell coordinate control's compact
maximum. A narrow row stacks that same label, selector, and state without
changing their DOM or focus order. Long selected values may elide only when
the viewport cannot contain them; the native control retains the complete
accessible option text.

The work area has no introductory paragraph that restates its labels and no
separate result-style paragraphs below the controls. One quiet policy line
states that shared Diff links preserve the exact versions shown, choosing a
target does not execute a comparison, and Clone scope remains session-local.
Loading, reached-depth, no-predecessor, partial-listing, unsupported-source,
removed Clone target, and failed-inventory outcomes remain visible in their
owning row rather than becoming general section prose.

The initial target-settings slice does not advertise working result inspectors.
Its Package controls identify that limitation. The immediate successor adds
the shared presentation adapter, then the feature facade and
[Compare](inspect-web-compare-experience.md) Library Diff drill-down. That
Browser consumer retires #6076's **Compare authored source** action and Source
Diff modal under a zero-compatibility plan rather than adding a second Diff
experience. Its paired Source evidence may feed the new on-demand annotated
comparison, but the manual version field and old result view do not remain.
The same Browser adoption retires the contextual **Compare method bodies**
action and Method Body Diff dialog under #6491 while preserving their managed
exact-pair evidence for an immersive Omni destination.
Type/Member narrowing and Clone execution remain follow-on work in #7213.

The Library API Diff delivery path is the selected-library query (#6128),
these Package settings, the shared
[Library API diff presentation](library-api-diff-presentation.md) contract and
adapter, then the facade with its immediate Library consumer. This browser
interaction owner is not a new shared product substrate. It requires no token
alias, redirect, state migration, or tombstone for the transient retired
interaction, and it does not change CLI coordinate selection.

## Evidence

`BrowserPackageVersionInventoryTests` exercises the real Browser PackageHouse
listing path over Gallery fixture responses and gates native release ordering,
stable/preview selection, normalized build metadata, missing predecessors,
unlisted choices, unknown listing authority, no payload acquisition, typed
package absence, and the current version's insertion position.

`catalog-requests.test.ts` gates retained-model request publication, explicit
retry, replacement, and rollback of completed inventory. The companion
production-selector cases gate option order and selectedness for current
versions missing above, within, or below the inventory, without changing the
available exact candidates.
`package-comparison-targets.test.ts` gates Diff defaults, exact choice
admission, separate same-coordinate settings, rollback associations, and form
rendering/binding. Its current Clone-selector cases characterize the
implementation that the shared Clone Browser adoption will remove; they do not
define the replacement breadth or candidate-discovery contract. The
production-root case
`Package comparison targets survive Library, Type, and Member navigation` in
`browser/library-hierarchy.workspace.spec.ts` exercises the actual navigation
and controls with deterministic facade responses, including retained keyboard
focus.
`saved-workspace-navigation.test.ts` composes the real target and inventory
coordinators with the production snapshot functions to gate successful
retirement and rollback after acquisition or view-selection failure.

Those gates cover Specific version and its inventory. Until the scenario
controls ship, Specific version remains the single active target, as those gates
define its defaults. The scenario targets are design only at this head; their
gates arrive with the controls under
[#9920](https://github.com/richlander/dotnet-inspect/issues/9920):

- scenario defaults, both checked at depth 1 and Specific version unchecked;
- each depth from 1 to 6, independent per scenario;
- a reached depth below the chosen depth, and no earlier point;
- unknown listing authority, which leaves Specific version usable and the
  scenarios without points;
- no checked target;
- one target's failure leaving the others visible; and
- a scenario link reopening its exact points after a later release.

These do not claim that a selected counterpart is inspectable or that a
comparison has run. Those require the successor's acquisition and query gates.
The source owner remains
[Browser package sources](browser-package-sources.md); navigation and portable
state remain with their existing owners.
