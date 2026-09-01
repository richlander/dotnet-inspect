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

- the persistent shell's visible text actions (`Home`, `Search`, `Open`,
  `Settings`);
- the workspace title bar's allocation among the product root, compact
  workspace switcher, broad workspace identity, coordinate selectors, and
  trailing shell actions;
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

The first persistent row is one non-wrapping workspace title bar:

```text
dotnet-inspect  [0:Platform  1:System.Text.Json*]  System.Text.Json@10.0.0  version 10.0.0  framework net10.0  Search Home Open Settings
```

It describes the broad inspection scope, not the deepest selected target:

1. `dotnet-inspect` is the stable product and Workspace root control.
2. The **workspace switcher** identifies retained open coordinates. Platform
   owns session-local index `0`; other coordinates receive stable session-local
   numeric indexes. Selecting a coordinate does not change its index. Replacing
   its version or framework preserves its index, and closing one coordinate
   does not renumber the others.
3. The **workspace identity** receives the elastic space. It renders an
   owner-issued workspace name when one exists. Otherwise it renders the
   active coordinate identity for a singular or provisionally unnamed
   workspace. A composite identity may be meaningful, such as an owner-issued
   package-prefix description; the shell does not derive one by parsing member
   or display text.
4. Applicable coordinate selectors, such as package version and target
   framework, immediately qualify that broad identity. Their descriptors and
   effects remain owned by navigation presentation and its product inputs.
5. Fixed shell actions remain reachable at the trailing edge.

Workspace selectors consume their natural width rather than stretching to
divide the row like browser tabs. The active coordinate is unmistakable in
both visual and accessibility state. When the switcher crowds the title bar,
the elastic workspace identity truncates first, coordinate selector labels may
compact next, and fixed shell actions remain reachable. Every workspace remains
available through horizontal scrolling.

This allocation uses tmux's indexed-window status line as structural evidence:
compact indexed workspaces spend only the width they need and return remaining
width to useful title or status information. Inspect Web does not copy tmux's
terminal styling, command model, pane management, key prefix, or status
variables.

The title bar does not show the fully qualified Library, Type, or Member
identity. [Inspect Web Navigation Presentation](inspect-web-navigation-presentation.md)
owns the subject/inspector row and target selector beneath it, and the active
working surface owns the visible heading for the exact target.

Search is a visible trailing action that opens Spotlight, not an
always-editable query input or a visually dominant command center. The global
shell exposes:

```text
Home   Search   Open   Settings
```

An optional decorative glyph does not replace any visible label.

### Incremental adoption

The shell may land before adjacent redesign owners. During that transition:

- currently supported Home, Search, and Settings actions may occupy the target
  top row before local-artifact Open is available;
- Open remains absent rather than appearing disabled or committing a
  success-shaped placeholder action;
- the `dotnet-inspect` root control may retain its current Home destination
  until the routed Workspace surface exists;
- existing Share and keyboard-help actions may remain at the trailing edge
  until their contextual replacements land; and
- retained packages may provisionally supply the indexed workspace switcher,
  active coordinate identity, version selector, and framework selector before
  product-issued Workspace and coordinate descriptors replace that data.

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

1. Load Platform and two packages and confirm that the workspace switcher renders
   indexes `0`, `1`, and `2` in retained-session order without stretching the
   selectors to equal widths.
2. Change the active package's version and framework and confirm that its
   workspace index is preserved.
3. Close the lower-index package and confirm that the remaining package is not
   renumbered.
4. Confirm that the active workspace is exposed visually and with
   `aria-selected`, and that pointer and keyboard activation use the same
   workspace-selection action.
5. Confirm that the broad workspace or active-coordinate identity appears in
   the elastic title region, followed by applicable version and framework
   selectors.
6. Select a Type or Member and confirm that its fully qualified identity does
   not replace the workspace title; the working surface heading identifies it.
7. Add workspaces until the switcher crowds the row and confirm that the title
   truncates before selectors or fixed shell actions disappear, with every
   workspace remaining reachable by horizontal scrolling.
8. Confirm that no persistent package-query input or centered command-center
   control appears, and that the visible Search action opens Spotlight.

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
