# Inspect Web Workspace Add package

## Claim and scope

The Workspace page can append one NuGet package selected through existing
package search without replacing or evicting its current members. It remains
on Workspace and preserves the active coordinate and inspection selection.
This document owns the focused Browser Add-package interaction.

The consumer is Inspect Web, tracked by #5965 under #5697. The user approved
this Browser-only slice after #5933 merged. Production adoption has two steps:
land the complete UI slice, then include it in a separately authorized normal
release and website deployment. No alternative architecture is introduced.
The transitional Browser membership authority is replaced by the later
product Scope adoption under #5821; this slice does not implement that owner.

[Shell Interaction](inspect-web-shell-interaction.md#search) supplies Search
and modal behavior; existing package acquisition supplies resolved coordinates;
[Navigation Consumer](inspect-web-navigation-consumer.md) supplies navigation
generations, history, and result focus; [Navigation
Presentation](inspect-web-navigation-presentation.md#workspace-surface) supplies
placement. [Saved Workspaces](inspect-web-saved-workspaces.md) continues to own
local definitions. These are consumed contracts, not additional owners.

## Behavior

- Add package is available on the ready Workspace page, including its empty
  state. It opens the existing Search dialog as a package-only picker with
  an explicit Add purpose and Cancel action, not another search implementation.
- Search discovery and recent entries use the existing source and resolved
  version/framework policy. The chosen package is retained by its exact
  resolved coordinate. This slice does not add a version/framework editor.
- Loaded NuGet entries are marked already present. Choosing one does not
  acquire, duplicate, replace, or activate it.
- The picker does not offer commands, Types, Members, Platform acquisition,
  Package query, or package removal. Ordinary Search keeps those behaviors.
  Closing or reopening ordinary Search ends the Add purpose.
- Adding a new package preserves existing membership and its order. The first
  package becomes active only when the Workspace was empty; otherwise the
  active coordinate, subject selection, and filters remain unchanged.
  Membership-dependent derived results are invalidated, not treated as current.
- At the existing Workspace coordinate limit, Add refuses visibly rather than
  taking the ordinary loader's eviction path. An already-full Workspace starts
  no acquisition; an in-flight request rechecks capacity before retention.
- Search failures are visible in the picker. A failed or superseded
  acquisition cannot install new Browser membership or overwrite a later
  navigation. Failure leaves the prior Workspace and source history available,
  reports a retryable notice on Workspace, and does not reopen the picker.
- Successful Add uses the existing canonical URL projection and focuses the
  Workspace heading. Dismissal and failure return focus to Add package when
  still available. The action remains a stable focus target during rerenders.
- Saved definitions do not change when live membership changes. A subsequent
  explicit Save captures the new scope through the existing packet path.

Prefixes, Clear, saved-entry updates, local-file Open, new source selection,
product Scope implementation, and paused #5812 remain separate work.

## Convention, rendering, and evidence

The baseline is a collection's Add action opening a focused selection dialog.
The existing Search is reused with a temporary purpose rather than adding a
parallel catalog or a persistent editor. No analogous implementation code is
copied. This small interaction is sufficient for visible membership editing;
it is not a new shared product substrate.

Typed package results reach the existing Browser HTML renderer and keybinding
registry. Markout is deliberately not used for these interactive Browser
controls; no broad multi-format information domain is introduced.

The existing Node runner and Firefox gates cover picker routing and reset,
empty/existing/full membership, exact duplicate selection, preserved active
inspection, source/acquisition failure, stale completion, history, focus,
and neighboring Save/removal behavior. Original-host tests exercise application
declarations with the actual acquisition/retention helpers and isolated
producer collaborators. Focused Browser fixtures do not claim full Wasm-site
or production-network evidence.
