# Inspect Web package-row removal

## Claim and scope

Home Search, modal Spotlight, and the Workspace page expose the same trailing
`x` button for removing an exact retained NuGet package. Recent-package rows
expose the same control to forget that history entry. Removal never activates
the row. This document owns this focused Browser interaction; Navigation
Presentation supplies Workspace placement, Shell Interaction supplies Search,
and Navigation Consumer retains ownership of navigation effects.

The consumer is the existing Inspect Web application, tracked by #5887. This
user-approved Browser-only slice advances the one-live-Workspace viewer/editor in #5697
without depending on paused #5812. Production-host adoption takes two steps:
land this complete UI slice, then include it in a separately authorized normal
release and website deployment. It adds no alternative Workspace architecture.
Saving Workspaces, Add, prefix expansion, and Clear are separate work.

## Behavior

- The button has a visible close glyph and a complete accessible name,
  including the package coordinate for an open row. It is a sibling of the
  activation control, never a nested button.
- Forgetting a recent package removes its case-insensitive package-ID entry
  from browser-local history. An explicit later open may remember it again.
- Removing an open row removes only that exact package coordinate and also
  forgets its recent package-ID entry. Other versions and frameworks remain
  loaded. Platform is not removable through these controls.
- Persistence failure is visible and leaves the entry and membership unchanged.
- Removing an inactive package preserves the active inspection selection.
  Removing the active package uses the existing retained-package successor
  order and resets package-bound selection rather than applying the old
  package's filters or member selection to its successor.
- Home remains Home. Modal Search remains open. Workspace remains Workspace;
  removing its last coordinate uses the existing empty `/demos` surface.
  Removal does not select another search result or acquire anything.
- Search preserves its input and selection, moving selection to the next row
  or preceding row when necessary, with focus on the input. Shift+Delete
  removes the selected removable row. The removed ID is suppressed from
  discovery hits until the query changes or Search resets, not permanently
  blocked from NuGet discovery.
- Workspace focus moves to the next removal control, then the preceding one,
  then its heading. Occurrence-query refresh preserves these controls and
  their focus; unavailable Inspect actions do not hide removable membership.
- Package-bound browser caches and obsolete occurrence actions are invalidated
  when membership changes. This does not promise deletion from HTTP caches,
  package sources, or saved browser history.

## Convention, rendering, and evidence

The baseline is a browser address-bar suggestion's trailing removal button:
remove the suggestion without navigating. The deliberate difference for a
loaded package is that removal also releases its live Browser membership.
No browser implementation code is copied.

The existing typed package and recent-entry models flow into the Browser's
HTML renderers. A shared small button renderer supplies the visual and
accessible control; Markout is not used for these interactive DOM controls.
This is not a new multi-format rendering domain or shared product substrate.

The enforcing gates are the existing Node test runner's package-removal,
Spotlight, and Workspace tests and the Firefox package-removal browser tests.
They cover persistence failure, exact identity, active/inactive/last removal,
click-versus-activation, keyboard removal, query preservation, and membership
visibility while Inspect actions are loading or unavailable.
