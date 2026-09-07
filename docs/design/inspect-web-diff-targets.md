# Browser Diff targets

This document owns the browser's Package-scoped Diff baseline inherited while
navigating within that Package. It does not own comparison execution or
acquisition authority. The production consumer is the Library Diff experience
tracked by
[#5083](https://github.com/richlander/dotnet-inspect/issues/5083).
The target-settings implementation is tracked by
[#6156](https://github.com/richlander/dotnet-inspect/issues/6156).

Clone candidate scope moved to the shared
[Structural Clone Search Scope](structural-clone-search-scope.md) owner under
[#6282](https://github.com/richlander/dotnet-inspect/issues/6282). The current
Package-specific Clone selector remains implemented only until that contract's
Inspect Web adoption stage retires it. It is not a target contract or
compatibility surface.

## Boundary

Package Overview owns the controls. Library, Type, and Member consumers read
the same Package settings, narrow their own queries, and offer **Change target**
back to Package Overview. They do not maintain independent coordinate controls.
This chooses one of #5083's proposed placements without adding an inspector
identifier to the navigation vocabulary.

Settings belong to the live browser Package model, not its display name or a
package-id-only cache key. Navigation within that model preserves settings.
Replacing its version or framework, removing it, or replacing the Workspace
discards them; a newly retained Package starts with defaults. Two retained
models do not share explicit choices merely because their coordinates match.
An unsuccessful Workspace replacement restores the prior settings along with
the browser's existing rollback snapshot, including associations to its
restored Package models.

These are session-local settings, not portable Workspace Definition fields or
retained product authority. They do not mint occurrence handles, establish
library correspondence, authorize a source, acquire comparison payloads, or
prove that a selected target can be inspected. Execution must consume the
appropriate product-owned acquisition and query outcomes.

## Diff

The default is the highest published, listed version strictly below the active
version in NuGet release precedence. Stable coordinates consider stable
releases; preview coordinates also consider previews. Build metadata does not
create an earlier release. This follows the existing preference for listed
stable automatic selection, rather than treating a release candidate as the
default predecessor of every stable release.

The version inventory and ordering come from managed code using NuGet's version
semantics. The browser does not implement a second semantic-version comparator.
The coordinate selector includes the active version even when the inventory
omits it, at its native release-order position. That display-only option does
not add an available exact Diff candidate. The inventory carries the native
insertion position separately, including when listing authority is unknown.
The first adopter uses the existing built-in Gallery acquisition path. Other
origins must not borrow Gallery's inventory for a same-named package.

An explicit choice can select any returned exact version, including an
unlisted version or the current version. Returning to the automatic choice
reinstates the default. No preceding candidate, unavailable listing authority,
and a failed inventory request are distinct visible outcomes. Unknown listing
state may leave exact choices usable but cannot justify an automatic default.

Inventory loading does not execute a comparison. It remains bounded by the
existing source and Browser package-operation owners. A completed request may
publish only into the same still-retained Package model and request entry;
discarding the model retires its pending publication. Failure is visible and
retry is explicit, not an automatic render/retry loop.

## Presentation and adoption

Controls use the browser's existing native form and DOM-rendering conventions.
This is deliberately host-specific rather than a Markout lowering: it edits
typed interaction state, not comparison evidence. Actual diff presentation
continues to consume the shared comparison and presentation owners.

The initial target-settings slice does not advertise working result inspectors.
Its Package controls identify that limitation. The immediate successor adds
the shared presentation adapter, then the feature facade and Library API Diff
inventory/details. That Browser consumer retires #6076's **Compare authored
source** action and Source Diff modal under a zero-compatibility plan rather
than adding a second Diff experience. Its paired Source evidence may feed the
new on-demand annotated comparison, but the manual version field and old result
view do not remain. Type/Member narrowing and Clone execution remain follow-on
work in #5083.

The Library API Diff delivery path is the selected-library query (#6128),
these Package settings, the shared
[Library API diff presentation](library-api-diff-presentation.md) contract and
adapter, then the facade with its immediate Library consumer. This browser
interaction owner is not a new shared product substrate. It requires no token
alias, redirect, state migration, or tombstone for the transient retired
interaction, and it does not change CLI coordinate selection.

## Evidence

`BrowserPackageVersionInventoryTests` exercises the real Gallery source owner
with fixture responses and gates native release ordering, stable/preview
selection, normalized build metadata, missing predecessors, unlisted choices,
unknown listing authority, and the current version's insertion position.

`catalog-requests.test.ts` gates retained-model request publication, explicit
retry, replacement, and rollback of completed inventory. The companion
production-selector cases gate option order and selectedness for current
versions missing above, within, or below the inventory, without changing the
available exact candidates.
`package-comparison-targets.test.ts` gates Diff defaults, exact choice
admission, separate same-coordinate settings, rollback associations, and form
rendering/binding. Its current Clone-selector cases characterize the
implementation that #6282's Browser adoption will remove; they do not define
the replacement focal-length contract. The production-root case
`Package comparison targets survive Library, Type, and Member navigation` in
`browser/library-hierarchy.spec.ts` exercises the actual navigation and controls
with deterministic facade responses, including retained keyboard focus.
`saved-workspace-navigation.test.ts` composes the real target and inventory
coordinators with the production snapshot functions to gate successful
retirement and rollback after acquisition or view-selection failure.

These do not claim that a selected counterpart is inspectable or that a
comparison has run. Those require the successor's acquisition and query gates.
The source owner remains
[Browser package sources](browser-package-sources.md); navigation and portable
state remain with their existing owners.
