# Inspect Web Shell Interaction

This document owns the persistent `dotnet-inspect` shell and the shared
transient and routed surfaces it launches: the workspace title bar and shell
actions, the Application menu, shared menu/modal semantics, Spotlight Search,
Open, Settings entry, Keyboard help, the command palette, and the
routed-versus-modal classification that governs focus return and history
interaction. It does not own which subject, target, or lens is active, the
contents of coordinate selectors, or the consumer effect lifecycle that
resolves focus after a navigation result installs; those are separately owned.

## Ownership and boundaries

This owner defines:

- the persistent shell's visible `Search` and `Open` actions, one stable
  Application menu for `Share`, `Settings`, and `Keyboard help`, and the
  `dotnet-inspect` Home control;
- the identities, accessible behavior, and responsive visible states of the
  row-one Home, history, Search, and Application menu controls;
- the generic modal-dialog contract (accessible name, initial focus, inert
  background, tab containment, Escape, one-modal-at-a-time, and
  ordinary-dismissal focus return) shared by Spotlight, Open, Settings,
  Keyboard help, and the full-bleed Annotated Source viewer;
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

Surface Composition owns the two-row placement:

```text
row one: dotnet-inspect  Subject  Inspectors  Back Forward  Search  Application
row two: [package icon] Package > Type > Member          contextual actions
```

This document owns the row-one shell controls rather than the page-level
allocation:

1. `dotnet-inspect` is the stable product and Home control.
2. Compact Back and Forward controls occupy one quiet paired container.
3. Search follows history and opens Spotlight.
4. The Application menu is the stable inline-end action home.

Row one contains no workspace tabs, indexed workspace selectors, or
separate Platform workspace, active-package title, or package coordinate
selector. Most sessions contain one workspace, so retained coordinate
management belongs to the Workspace subject rather than permanent
high-distraction chrome. Platform libraries are capabilities or content of the
current workspace.

The Navigation Presentation-owned Subject and Inspector region shares row one
with those shell controls. `Share`, `Settings`, `Keyboard help`, and contextual
working-surface actions are not children of that region or items in either
SlideStrip.

The shell exposes one stable Application menu control separately from the
Subject and Inspector region. [Inspect Web Surface
Composition](inspect-web-surface-composition.md) owns its page-level placement,
its relationship to overflowing content, and the responsive allocation that
keeps it outside the subject and inspector region. The control's interaction
identity and menu inventory do not change with viewport width.

Search is an input-like control in row one that opens Spotlight. It is
not editable in place and does not become a dominant centered command control.
Back and Forward sit immediately to its left, following VS Code's bounded
ordering. The Application menu follows Search at the row's inline end. Surface
Composition owns the pressure order from full Search, to compact Search, to
arrows alone, to no visible history/Search controls before the Subject and
Inspector region starts reducing active identity. These states do not change
the controls' interaction semantics.

The separate Application menu exposes:

```text
Application
  Share
  Settings
  Keyboard help
```

The dotnet-bot image is the product mark. The visible `dotnet-inspect` label
remains; the image does not replace it. Navigation Presentation's row-two
inspected target owns a separate fixed-width root-mark slot.

For a NuGet package, that root mark is the bounded embedded JPEG or PNG declared
by the package nuspec, with NuGet Gallery's default package icon as the
fallback. Legacy remote nuspec icon URLs are not fetched.

### Incremental adoption

The shell may land before adjacent redesign owners. During that transition:

- currently supported Settings may remain a direct shell action before the
  Application menu is available;
- Open remains absent rather than appearing disabled or committing a
  success-shaped placeholder action;
- the `dotnet-inspect` root control is the sole persistent Home affordance;
- existing direct Share, Settings, and keyboard Help controls may remain in the
  shell until
  [Surface Composition's placement contract](inspect-web-surface-composition.md#shell-navigation-and-application-actions)
  is implemented;
- that adoption replaces the direct controls atomically with the one
  Application menu rather than rendering both action homes; and
- retained packages may provisionally supply Workspace entries and Package
  version/framework controls in Package content before product-issued
  descriptors replace that data.

This sequencing changes no package-selection or acquisition semantics and does
not claim completion of the redesign.

## Convention and comparison evidence

The Application menu starts from three established patterns:

- Firefox's application menu provides one stable, compact home for
  application-level actions such as Settings and Help instead of distributing
  them across content-specific chrome. The useful transfer is the persistent
  action home, not Firefox's broad inventory or nested navigation
  ([Mozilla menu reference](https://support.mozilla.org/en-US/kb/menus-reference)).
- The WAI-ARIA Authoring Practices
  [Menu Button Pattern](https://www.w3.org/WAI/ARIA/apg/patterns/menu-button/)
  supplies the button, menu, focus, and keyboard baseline.
- VS Code keeps commands reachable through both visible menus and the Command
  Palette. The useful transfer is action parity, not VS Code's desktop
  application menu bar or user-editable keybinding system
  ([VS Code keyboard shortcuts](https://code.visualstudio.com/docs/configure/keybindings)).

The deliberate divergence is that this menu remains small and non-navigational.
It contains only the shell-owned Share, Settings, and Keyboard help actions.
Search, Open, browser history, subjects, inspectors, coordinates, and
contextual working-surface actions keep their existing dedicated owners and
locations. The button is the canonical action home at every supported width,
not an overflow fallback whose inventory changes when space becomes scarce.

## Application menu

The Application menu is one persistent shell control wherever the inspection
shell is rendered. Its visible control uses the conventional three-line
application-menu glyph with the complete accessible name and title
`Application menu`. It has one stable logical identity across shell
replacement so modal dismissal resolves the current rendered button rather
than retaining an element reference from an older shell lifetime.

The button follows the Menu Button Pattern:

- it is a button with `aria-haspopup="menu"`, `aria-expanded`, and
  `aria-controls`;
- Enter, Space, or Down Arrow opens the menu and focuses its first item;
- Up Arrow opens the menu and focuses its last item;
- the menu has `role="menu"`, and its actions have `role="menuitem"` and are
  outside the page Tab sequence while the menu is closed;
- Up and Down Arrow wrap through the menu, while Home and End move to its
  bounds;
- Enter or Space activates the focused action;
- Escape closes the menu and returns focus to the Application menu button;
- Tab or Shift+Tab closes the menu and continues through ordinary document
  order rather than trapping focus; and
- outside-pointer dismissal leaves focus at the pointer's resulting target
  rather than moving it back to the button.

The menu inventory is stable among the actions applicable to the current
surface:

1. `Share` appears whenever a retained inspection workspace supplies an
   explicit Share outcome. A non-projectable workspace retains the item because
   activation must present the owner-issued reason; a surface with no Share
   action omits it rather than rendering a disabled placeholder.
2. A separator divides the current-workspace action from the application-wide
   actions.
3. `Settings` opens Unified Settings.
4. `Keyboard help` opens the shared keyboard-reference dialog.

Changing viewport width does not move an action between direct and menu forms,
reorder the inventory, or remove the Application menu button. Contextual
actions such as Copy, Explore, graph controls, source actions, and
`Open in workspace` never enter this menu.

Action activation closes the menu before dispatch:

- Share returns focus to the Application menu button, submits the same explicit
  canonical Share operation used by the command palette, and announces copy
  success without moving focus. A non-projectable outcome or clipboard failure
  is visibly surfaced and leaves focus on the button.
- Settings and Keyboard help close the menu without an intermediate focus
  return, then apply their modal initial-focus rules. Ordinary dismissal
  returns to the current Application menu button without reopening the menu.
- A committed navigation action, browser-history transition, or route change
  that removes the inspection shell follows Navigation Consumer's destination
  focus contract instead of trying to restore the removed button.

If shell maintenance replaces the open menu without navigation, the menu
closes and focus moves to the replacement Application menu button. If
maintenance replaces the shell while Settings or Keyboard help is open, the
modal remains focused and its eventual dismissal resolves the replacement
button by logical identity.

## Shared menu and modal semantics

When a menu item opens a modal, the menu closes without returning focus to its
invoker and the modal applies its initial-focus rule. The stable menu-button
invoker, not the removed menu item, becomes the modal's ordinary-dismissal
return target; dismissal does not reopen the menu.

Spotlight, Open, Settings, Keyboard help, and the full-bleed Annotated Source
viewer are modal dialogs:

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

The row-one Search control uses the expanded label
`Search types, members, packages` and transfers focus to Spotlight's editable
input when activated. The control does not carry a visible shortcut badge;
Spotlight's footer lists `Ctrl P` alongside its existing navigation guidance
and wraps that guidance rather than clipping it at narrow supported widths. Its
compact `Search` label and hidden responsive states do not change the keyboard
shortcut or Spotlight behavior.

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

The Application menu's Settings action opens the one shared configuration
experience. Separate persistent theme controls, a global Taste button, and
duplicate settings popovers are removed.

### Keyboard help

The Application menu's `Keyboard help` action opens one dialog named
`Keyboard help`. It presents the current shell, navigation, inspection, and
working-surface shortcuts using the same command names exposed through the
command palette and visible controls. It does not create a second command
registry, define another owner's shortcut semantics, or list unavailable
commands as though they were supported.

Initial focus moves to the dialog's visible heading. The dialog follows the
shared modal containment, Escape, close, one-modal-at-a-time, and
ordinary-dismissal focus-return rules.

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
settings
keyboard help
```

Command execution uses the same state transitions as pointer interaction and
applies the same projectable, non-projectable, or failed canonical-state
classification after commit.

Persistent navigation controls are not an always-editable command input. The
site does not introduce a broad set of single-letter page shortcuts. One
discoverable palette shortcut plus ordinary control-specific keyboard behavior
is sufficient.

## Implementation gates

Before implementation claims this application-control contract, it must add
and pass these named Inspect Web tests:

- `shell-controls.test.ts`:
  `application menu owns Share Settings and Keyboard help` proves the exact
  menu inventory and order, conditional Share omission, non-projectable Share
  reason, ARIA relationships, menu-button keyboard behavior, outside-pointer
  dismissal, and the absence of contextual or navigation actions.
- `workspace-titlebar.spec.ts`:
  `application menu preserves action and focus continuity` proves Share
  success and failure focus, Settings and Keyboard help initial focus and
  dismissal return, shell replacement while the menu or a launched modal is
  open, and stable action inventory across wide and narrow viewports.
- `command-bar.test.ts`:
  `application actions share one dispatch path with the command palette`
  proves Share, Settings, and Keyboard help parity without a second action
  registry.

These gates exercise the Shell Interaction-owned control in a focused harness.
Surface Composition's later placement adoption owns the geometry proving that
the control remains outside the subject and inspector region and visible
beside overflowing content.

## Non-claims

This document does not decide which subject, coordinate, or lens is active,
does not define effect-authority validation or browser-history commitment
after a navigation result installs, and does not define page-level placement
or responsive composition. It does not turn the Application menu into a
generic overflow-control substrate, place contextual working-surface actions,
or redefine another owner's command or keyboard semantics.

## Acceptance scenarios

An implementation claiming this redesign is complete must satisfy these
outcomes.

### Workspace title bar

1. Confirm that row one contains the `dotnet-inspect` Home control, Subject and
   Inspector navigation, Back and Forward, Search, and the Application menu,
   with no Package coordinate controls.
2. Confirm that row one contains no workspace tabs, numeric workspace
   selectors, separate Platform workspace, or reconstructed inspected target.
3. Open Workspace and confirm that retained coordinates move into its working
   surface with activation and Close actions.
4. Select a Type or Member and confirm that row two advertises the Package,
   Type, and Member path in order while row one retains the subject and
   inspector strip.
5. Select Package and confirm that Version and Framework appear in its working
   surface. Confirm that every product-issued subject-path segment copies its
   own typed canonical name, there is no separate Copy name action, and the
   Application menu's Share action retains the exact workspace identity.
   `dotnet-inspect` is the sole Home affordance.
6. Narrow the viewport and confirm that the row-one action cluster progresses
   from full Search, to `Search`, to arrows, to nothing before Subject or
   Inspector navigation reduces active identity. Lengthen the row-two target
   and confirm that the row-one Search state does not change. Narrowing does
   not change the Application menu's action inventory.
7. Confirm that the product bot and inspected-target root mark retain distinct
   bounded icon slots in their respective rows and the current target leaf uses
   the shared accent.
8. Confirm that no persistent package-query input or centered command-center
   control appears, and that Back and Forward sit immediately left of the
   visible Search control, which precedes the Application menu and opens
   Spotlight.

### Application menu scenarios

1. Confirm that one `Application menu` button exists separately from the
   subject and inspector tablists and retains one logical identity across shell
   replacement.
2. Open it by pointer, Enter, Space, Down Arrow, and Up Arrow. Confirm its ARIA
   relationships, initial item, wrapped Arrow navigation, Home and End bounds,
   Escape return, Tab continuation, and outside-pointer dismissal.
3. In a retained projectable workspace, confirm that the menu contains Share,
   a separator, Settings, and Keyboard help in that order. On a surface with no
   Share action, confirm that Share and the now-unnecessary separator are
   absent. Supply a non-projectable workspace and confirm that Share remains
   present and activation surfaces its owner-issued reason.
4. Activate Share and confirm that the menu closes, focus returns to the
   Application menu button, the canonical link is copied once, and success is
   announced without moving focus. Reject clipboard access and confirm a
   visible failure with the same focus.
5. Activate Settings and Keyboard help. Confirm that each closes the menu
   without intermediate focus return, applies its modal initial-focus rule, and
   returns to the current Application menu button on ordinary dismissal without
   reopening the menu.
6. Replace the shell while the menu is open and confirm that it closes and
   focus moves to the replacement button. Replace the shell while Settings or
   Keyboard help is open and confirm that modal focus remains contained and
   dismissal resolves the replacement button.
7. Resize repeatedly across the supported range and confirm that the button and
   applicable action inventory remain stable rather than changing between
   direct and overflow forms.
8. Confirm that Search, Open, history, subjects, inspectors, coordinates,
   Copy, Explore, graph actions, source actions, and `Open in workspace` do not
   enter the menu.
9. Invoke Share, Settings, and Keyboard help through the command palette and
   confirm that each uses the same dispatch and outcome path as its menu item.

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
11. Open general and command-scoped Spotlight at the narrow supported width and
    confirm that every footer-guidance item, including `Ctrl P search`, remains
    visible within the modal.

### Local Open

1. Open the local-artifact overlay.
2. Add supported files through the picker, drag and drop, and clipboard file
   paste.
3. Confirm that each path produces the same owner-issued workspace result.
4. Paste arbitrary text and confirm that the UI does not guess that it is
   binary content.

### Modal and routed surfaces

1. Open and close Spotlight, Open, Settings, and Keyboard help by pointer,
   keyboard, and Escape.
2. Confirm accessible naming, initial focus, modal containment, inert
   background content, and focus return for each.
3. Launch Diagnostics from Settings and Spotlight and confirm that focus moves
   to the routed Diagnostics heading rather than back to the modal invoker.
4. Commit navigation from Search and Open and confirm that
   focus moves to the resulting active-subject heading.
5. Return typed failures from Search and Open and confirm
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
