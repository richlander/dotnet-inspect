# Inspect Web Shell Interaction

This document owns the persistent `dotnet-inspect` shell and the shared
transient and routed surfaces it launches: the workspace title bar and shell
actions, shared menu/modal semantics, Spotlight Search, Open, Settings entry,
the command palette, and the routed-versus-modal classification that governs
focus return and history interaction. It does not own which subject, target,
or lens is active, the contents of coordinate selectors, or the consumer
effect lifecycle that resolves focus after a navigation result installs; those
are separately owned.

## Ownership and boundaries

This owner defines:

- the persistent shell's visible `Search` and `Open` actions, the persistent
  Application menu containing Share, Settings, and Help, and the
  `dotnet-inspect` Home control;
- the title line's allocation among the product root, the navigation-
  presentation-owned inspected target, and trailing Search/history cluster;
- the generic modal-dialog contract (accessible name, initial focus, inert
  background, tab containment, Escape, one-modal-at-a-time, and
  ordinary-dismissal focus return) shared by Spotlight, Open, Settings, the
  narrow navigation drawer, and the full-bleed Annotated Source viewer;
- the classification that Home, Workspace, Package query, and Diagnostics are
  routed full-bleed surfaces rather than dialogs;
- Spotlight Search's input and package-scope behavior;
- the local-artifact Open overlay; and
- the command palette's keyboard-driven counterpart to the visible
  workspace, subject, inspector, and target controls.

It does not own:

- which coordinate, subject, or lens descriptor is rendered or how the
  coordinate/subject menus interact (owned by
  [Inspect Web Navigation Presentation](inspect-web-navigation-presentation.md));
- the consumer effect lifecycle: canonical location and refresh, browser
  history classification, effect-authority validation, and destination-
  lifetime focus resolution (owned by
  [Inspect Web Navigation Consumer](inspect-web-navigation-consumer.md));
- selector-pill visual states or progressive filter disclosure (owned by
  [Inspect Web Presentation Language](inspect-web-presentation-language.md));
- page-level placement, layout, or responsive composition of working
  surfaces, or Unified Settings' section content (owned by
  [Inspect Web Surface Composition](inspect-web-surface-composition.md));
- the Annotated Source viewer's internal transient layer and Escape handling
  (owned by
  [Annotated Source viewer interaction](annotated-source-viewer-interaction.md));
  and
- artifact-acquisition outcomes, accepted-input descriptors, or workspace
  composition (owned by
  [Artifact acquisition and workspaces](artifact-acquisition-and-workspaces.md)).

## Inputs or consumed contracts

This document consumes, without redefining:

- owner-issued accepted-input descriptors and outcomes from
  [Artifact acquisition and workspaces](artifact-acquisition-and-workspaces.md)
  for the Open overlay;
- [Untrusted data threat model](untrusted-data-threat-model.md) for rejection
  and failure behavior at local and network input boundaries; this shell
  presents the returned typed outcomes without redefining them;
- the same result identities and acquisition path Search and coordinate
  selection share with
  [Inspect Web Navigation Presentation](inspect-web-navigation-presentation.md);
  and
- the effect-authority and focus-resolution rules from
  [Inspect Web Navigation Consumer](inspect-web-navigation-consumer.md) that
  govern what a modal's committed navigation action actually focuses.

## Workspace title bar and shell actions

The first persistent row is one non-wrapping title line:

```text
dotnet-inspect  [package icon] Package > Type > Member  Back Forward  Search
```

It follows the product's CLI grammar without becoming an editable command:

1. `dotnet-inspect` is the stable product and Home control.
2. Navigation Presentation renders the icon-backed typed inspected target
   immediately after the product root. Package coordinate controls belong to
   the Package working surface.
3. Compact Back and Forward controls followed by flush-right Search occupy a
   trailing cluster that yields space before the target path. The product Home
   control remains.

The title line contains no workspace tabs, indexed workspace selectors, or
separate Platform workspace, active-package title, or package coordinate
selector. Most sessions contain one workspace, so retained coordinate
management belongs to the Workspace subject rather than permanent
high-distraction chrome. Platform libraries are capabilities or content of the
current workspace.

The title line shows the applicable Package, Library, Type, and Member identity
as one typed path.

The second persistent row contains only Navigation Presentation's Slideable
Subject Strip. Share, Settings, Help, and contextual actions do not occupy that
row. Contextual actions remain with their owning working surface. Persistent
application actions move into one `Application` menu placed at the trailing
edge of the data bar by Surface Composition.

Search is an input-like control in the title line that opens Spotlight. It is
not editable in place and does not become a dominant centered command control.
Back and Forward sit immediately to its left, following VS Code's bounded
ordering, and Search is flush with the right edge. As target identity consumes
width, the cluster progresses from full Search, to a `Search` button after the
arrows, to flush-right arrows alone, and finally to no visible controls. The
global title line exposes:

```text
dotnet-inspect (Home)   inspected target   Back   Forward   Search
```

The subject zone exposes:

```text
Workspace Package Type Member   inspectors
```

The data bar ends with one persistent `Application` menu button. Its menu
contains `Share`, `Settings`, and `Help` in that order. The button's visible
ellipsis is labelled `Application menu`; it does not replace the
`dotnet-inspect` Home action or join the title-line Search/history cluster.
Share submits the same canonical workspace action as before. Settings and Help
open the same shell-owned surfaces as their former direct buttons. The command
palette retains equivalent entries.

The button uses `aria-haspopup="menu"` and reports its expanded state. Enter,
Space, or Down Arrow opens the menu on Share; Up Arrow opens it on Help. Within
the menu, Up and Down Arrow move through items, Home and End move to the first
and last item, Escape closes and returns focus to the button, and Tab closes
without trapping focus. Share closes the menu, performs its existing copy
action, and returns focus to the button. An item that opens Settings or Help
closes the menu without returning focus before the destination applies its
existing initial-focus rule.

The dotnet-bot image is the product mark. The visible `dotnet-inspect` label
remains; the image does not replace it. The inspected target owns a separate
fixed-width root-mark slot immediately after the product control.

For a NuGet package, that root mark is the bounded embedded JPEG or PNG declared
by the package nuspec, with NuGet Gallery's default package icon as the
fallback. Legacy remote nuspec icon URLs are not fetched.

### Incremental adoption

The shell may land before adjacent redesign owners. During that transition:

- Open remains absent rather than appearing disabled or committing a
  success-shaped placeholder action;
- the `dotnet-inspect` root control is the sole persistent Home affordance;
- the current direct Share, Settings, and keyboard Help buttons may occupy the
  subject zone only until the Slideable Subject Strip and data-bar Application
  menu land together; and
- retained packages may provisionally supply Workspace entries and Package
  version/framework controls in Package content before product-issued
  descriptors replace that data.

This sequencing changes no package-selection or acquisition semantics and does
not claim completion of the redesign.

## Shared menu and modal semantics

When a menu item opens a modal, the menu closes without returning focus to its
invoker and the modal applies its initial-focus rule. The stable menu-button
invoker, not the removed menu item, becomes the modal's ordinary-dismissal
return target; dismissal does not reopen the menu.

Spotlight, Open, Settings, the narrow navigation drawer, and the full-bleed
Annotated Source viewer are modal dialogs:

- each has a visible accessible name and close action;
- opening moves focus to its primary input, current selection, or heading;
- background content is inert while it is open;
- Tab and Shift+Tab remain within the dialog;
- Escape closes it unless an owner-issued destructive confirmation is active;
  the Annotated Source viewer first offers Escape to the viewer-owned transient
  layer defined by
  [Annotated Source viewer interaction](annotated-source-viewer-interaction.md);
  and
- ordinary dismissal returns focus to the invoking control.

Only one modal is open at a time. A modal action that opens another modal closes
the first without returning focus, then applies the second modal's
initial-focus rule. Dismissing the second modal returns to the originating
non-modal inspection or routed surface and does not reopen the first modal.

Opening or closing a modal does not create a browser-history entry. When a
modal action commits navigation, the modal closes without applying its
ordinary-dismissal return rule and synchronously parks focus as defined by
the navigation consumer (see below). An inspection destination then focuses
its active-subject level-one heading;
Home, Workspace, or Diagnostics focuses the routed surface's level-one heading.
If the transition returns a typed failure, the prior surface and history remain
active, the failure is visible, and focus moves to the modal's stable invoking
control when it is still rendered, otherwise to the retained surface's
level-one heading. The failed modal does not reopen.
Browser Back or Forward while a modal is open first dismisses it without
returning focus to the invoker, then performs the history transition. History
navigation focuses the restored destination heading without reopening the
modal.

Home, Workspace, Package query, and Diagnostics are routed full-bleed surfaces
rather than dialogs. Navigation places focus on their visible level-one heading
or, for Package query, its prefix input under that heading. Browser Back returns
to the prior routed surface and restores focus through the history transition.

The focus-parking step referenced above, and the effect-authority validation
that governs whether a result-derived destination actually receives focus, are
defined by
[Inspect Web Navigation Consumer](inspect-web-navigation-consumer.md#shell-and-menu-focus-resolution).
The coordinate and subject/hierarchy menus that may open these modals are
defined by
[Inspect Web Navigation Presentation](inspect-web-navigation-presentation.md#coordinate-and-subject-menu-interaction).

### Search

The persistent `Package or Package@version` input is removed. Search opens
Spotlight as the one search experience for:

- packages;
- loaded coordinates;
- Libraries;
- Types;
- Members;
- platform inputs; and
- commands.

The title-line Search control advertises the search scope in its expanded label
and transfers focus to Spotlight's editable input when activated. Its compact
`Search` label and hidden responsive states do not change the keyboard shortcut
or Spotlight behavior.

Coordinate activation always opens the coordinate menu. That menu contains an
explicit `Search packages` action that closes the menu and opens Spotlight in
package scope. Search and coordinate selection use the same result identities
and acquisition path.

Spotlight reacts to every supported way its input value changes, including
typing, paste, drag and drop of text, autofill where applicable, and input
method composition. Pasting a package coordinate updates results immediately;
it is not dependent on keyboard events that paste does not emit.

Spotlight exposes one visible `Package query` action. Activating it closes
Spotlight and requests the routed `/query` surface. When the current
package-search text is a valid package-ID prefix, the action preserves it as
the query surface's initial prefix; otherwise the query surface starts with an
empty prefix. Seeding the prefix does not start source work. `Run query` or a
facet selection dispatches the request under
[Package Query Experience](package-query-experience.md).
[Inspect Web Navigation Consumer](inspect-web-navigation-consumer.md#package-query-entry-and-return)
commits the route's history entry and destination focus.

### Open

Open admits user-provided artifacts rather than searching package sources. Its
overlay provides:

- a native file picker;
- a drag-and-drop target;
- a focused paste target for clipboard file items; and
- visible per-input progress, rejection, and failure results.

It may accept multiple related files when the artifact-acquisition owner
authorizes that input shape. The UI consumes owner-issued accepted-input
descriptors and outcomes; it does not infer supported extensions,
correspondence, or workspace composition. Arbitrary pasted text is not guessed
to be binary or base64.

### Settings

Settings opens the one shared configuration experience. Separate persistent
theme controls, a global Taste button, and duplicate settings popovers are
removed.

## Command palette

The existing command palette is the keyboard counterpart to the visible
workspace, subject, inspector, and target controls. It uses the same
product-issued coordinates, subjects, and lenses:

```text
package System.Text.Json
version 10.0.0
framework net10.0
library System.Text.Json.dll
type System.Text.Json.JsonSerializer
member DeserializeAsync
show source
share
```

Command execution uses the same state transitions as pointer interaction and
applies the same projectable, non-projectable, or failed canonical-state
classification after commit.

Persistent navigation controls are not an always-editable command input. The
site does not introduce a broad set of single-letter page shortcuts. One
discoverable palette shortcut plus ordinary control-specific keyboard behavior
is sufficient.

## Non-claims

This document does not decide which subject, coordinate, or lens is active,
does not define effect-authority validation or browser-history commitment
after a navigation result installs, and does not define page-level placement
or responsive composition.

## Acceptance scenarios

An implementation claiming this redesign is complete must satisfy these
outcomes.

### Workspace title bar

1. Confirm that the icon-backed typed Package, Type, and Member path follows
   `dotnet-inspect` in the title line, with no Package coordinate controls.
2. Confirm that the line contains no workspace tabs, numeric workspace
   selectors, or separate Platform workspace.
3. Open Workspace and confirm that retained coordinates move into its working
   surface with activation and Close actions.
4. Select a Type or Member and confirm that the title line advertises the
   Package, Type, and Member path in order while the full-width row below
   contains the subject and inspector strip.
5. Select Package and confirm that Version and Framework appear in its working
   surface. Confirm that every product-issued subject-path segment copies its
   own typed canonical name, there is no separate Copy name action, and Share
   remains with the exact identity. `dotnet-inspect` is the sole Home
   affordance.
6. Narrow the viewport or lengthen the inspected target and confirm that the
   title-line action cluster progresses from full Search, to `Search`, to
   arrows, to nothing. Confirm that the second row contains no application
   actions at any width.
7. Confirm that the product bot and inspected-target root mark retain distinct
   bounded icon slots and the current target leaf uses the shared accent.
8. Confirm that no persistent package-query input or centered command-center
   control appears, and that Back and Forward sit immediately left of the
   visible flush-right Search control, which opens Spotlight.
9. Open the data-bar `Application menu` and confirm that Share, Settings, and
   Help invoke their existing typed actions and focus behavior. Dismiss the
   menu and confirm that focus returns to its stable data-bar button.

### Search input

1. Activate the coordinate and confirm that its menu opens rather than
   Spotlight.
2. Activate an available non-search action and confirm that the menu closes and
   the returned active-subject heading receives focus.
3. Supply a typed failure for a coordinate action and confirm that the current
   surface remains visible, the failure is surfaced, and focus returns to the
   coordinate button.
4. Reopen the menu, activate `Search packages`, and confirm that the menu closes
   and
   package-scoped Spotlight receives initial focus.
5. Paste a complete package coordinate without pressing another key.
6. Confirm that results update immediately.
7. Dismiss Spotlight without navigating and confirm that focus returns to the
   coordinate button without reopening its menu.
8. Reopen package-scoped Spotlight, select the result, and confirm that the same
   acquisition transition is used as pointer-driven package selection.
9. Enter valid package-ID-prefix text, activate the visible `Package query`
   action, and confirm that Spotlight closes, `/query` is pushed, and the text
   becomes the initial prefix without starting source work.
10. Repeat with text that is not a valid package-ID prefix and confirm that the
    query surface starts with an empty prefix.

### Local Open

1. Open the local-artifact overlay.
2. Add supported files through the picker, drag and drop, and clipboard file
   paste.
3. Confirm that each path produces the same owner-issued workspace result.
4. Paste arbitrary text and confirm that the UI does not guess that it is
   binary content.

### Modal and routed surfaces

1. Open and close Spotlight, Open, and Settings by pointer, keyboard, and
   Escape.
2. Confirm accessible naming, initial focus, modal containment, inert
   background content, and focus return for each.
3. Launch Diagnostics from Settings and Spotlight and confirm that focus moves
   to the routed Diagnostics heading rather than back to the modal invoker.
4. Commit navigation from Search, Open, and the narrow drawer and confirm that
   focus moves to the resulting active-subject heading.
5. Return typed failures from Search, Open, and the narrow drawer and confirm
   that the prior surface and history remain active, the failure is visible,
   and focus moves to the surviving modal invoker or retained surface heading.
6. Keep a modal open across product-maintenance renderer replacement and confirm
   that focus remains inside it until dismissal, then moves to the replacement
   destination heading rather than the removed invoking control.
7. Open and close the full-bleed Annotated Source viewer and confirm the shared
   modal focus, Escape, containment, and history behavior.
8. From that viewer, open Decompiler style Settings and confirm that the viewer
   closes, Settings receives focus, and closing Settings returns to inline
   Annotated Source without reopening the viewer.
9. Navigate to Home, Workspace, Package query, and Diagnostics and confirm that
   each is a routed surface with one visible level-one heading, no
   coordinate/subject command, and a persistent `dotnet-inspect` control that
   opens Workspace. Confirm that Package query places initial focus on its
   prefix input under that heading.
10. Use Browser Back and Forward while a modal is open and confirm that the
   modal is dismissed, the restored destination heading receives focus, and the
   modal does not reopen.
