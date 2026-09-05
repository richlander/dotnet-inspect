# Inspect Web Workspace editing

## Status and authority

This is the proposed Browser interaction contract for #6024, following #6015
under the experience tracker #6012 and Workspace tracker #5697. It is
**unimplemented and unverified**. The operator approved this focused
Browser-only slice: inspection terminology, the edit/save boundary, and
ordinary in-app navigation away from unsaved edits.

This owner defines the editor's execution eligibility and leave decisions.
It does not own Scope admission, canonical definitions, saved-entry storage,
product navigation outcomes, history, focus, or layout. Their implementation
and any required contract changes remain focused owner adoption.

## Claim

An editor draft is not an inspection Workspace. Inspecting or navigating must
not implicitly save, discard, or execute its changes. Execution becomes
available only after editing ends with an explicit successful Save or discard,
using the resulting committed Workspace.

An ordinary Home or Spotlight opening may create a ready, unnamed Workspace.
Inspection does not require saving a named definition before entering Edit.
Here, **committed** means the live configuration outside an unfinished edit;
it does not assert that every live Workspace has a persisted named entry.

## Verbs and surfaces

**Inspect** selects content for inspection within the current Workspace. In
the Workspace inventory it selects an admitted package without replacing other
members or traversal permissions. Prefer it to an ambiguous inspection-only
Open label. An actual saved-definition Open still means replacement and
restoration; this contract does not rename that operation.

The editor does not offer Inspect or Run against its draft. Ordinary shell,
history, and subject-navigation departure requests remain reachable, but they
cannot execute their destination until the editor's leave decision completes.
These are guarded departures, not a way to execute the draft.
Configuration discovery, such as package search or listing curated package
names, can remain available.
It must not execute inspection or materialize the draft as a side effect.
Package and eager-loading selections describe the requested configuration;
they do not apply changes to the committed Workspace as controls are toggled.

Viewer actions remain distinct from editor controls:

```text
Workspace                                                   [Edit]
  Packages
    Example.Core                                            [Inspect]

Edit Workspace
  Packages and planned loading
  Allowed traversal
  Save destination
                                               [Cancel]     [Save]
```

These are informative sketches, not a new subject-strip or layout contract.

## Save, Cancel, and failure

Editor **Save** is an explicit persistence action, not an in-memory Apply
disguised as saving. Its requested result is the edited definition persisted
at an explicitly chosen local save destination and the corresponding committed
Workspace ready for inspection. The editor may announce completion and leave
editing only after the responsible owners supply that complete outcome.

The save destination is explicit. A demo title, matching package contents, or
the fact that a saved definition was opened does not authorize overwriting an
entry. Existing name validation and no-silent-replacement rules remain owned
by [Saved Workspaces](inspect-web-saved-workspaces.md). Updating an existing
entry is unavailable until that owner supplies the separately supported action;
this contract does not add overwrite or rename semantics.

An unnamed Workspace may enter Edit. Choosing Save then requests a new local
name; Cancel creates no saved entry. Requiring a destination for this explicit
Save does not require a named save for ordinary inspection outside Edit.

Valid intent-only drafts, such as no loaded packages and an allowed prefix,
also require a supported Save path without package-content acquisition.
The current named Save's nonempty-Workspace restriction cannot implement that
path. Intent-only saving is a specific prerequisite of the focused
Definitions/Saved Workspaces adoption, not a capability supplied by this
document or a reason to silently discard those edits.

The current named Save snapshots an already-ready Workspace and does not
apply a draft. It is not, by itself, an implementation of editor Save.
Persisting one packet and exposing a different edited configuration is not
completion. Nor is a successful storage write alone proof that the requested
Workspace is ready.

An unsuccessful Save keeps the draft available, exposes the owner-issued
failure, and does not release inspection or pending navigation. **Cancel**
discards the draft and returns to the previously committed Workspace without
saving it. A clean editor can be left without a dirty-edit decision.
While Save is pending, additional saves, draft changes, discard, and departures
wait for its terminal outcome rather than racing it.

These are requirements on the completion consumed by this interaction owner,
not a persistence/admission transaction defined here. Before enabling editor
Save, focused owner work must supply a completion that associates the requested
edit, persisted definition, and ready Workspace, and preserves the prior
committed Workspace on failure. Unsupported or partial completion must not be
presented as success. This prerequisite remains unimplemented under #6012;
there is no new save coordinator, packet format, or rollback algorithm here.

## Leaving an editor with changes

An ordinary in-app request to leave a dirty editor requires one explicit
decision before the request can proceed:

| Choice | Result |
| --- | --- |
| Save | Complete editor Save, then release the requested navigation |
| Discard | Discard the draft, then release the requested navigation |
| Stay | Keep editing; do not save, discard, or navigate |

The decision applies to ordinary shell, history, and subject-navigation entry
points, not only a package button in the editor. Hiding that button is not a
navigation guard. An unchanged draft needs no unsaved-changes prompt, but
editing still ends before inspection begins.

The released request retains its owner-issued destination and undergoes normal
navigation validation. A successful Save does not guarantee that an earlier
destination remains available; a subsequent navigation failure is not a Save
failure and must not resurrect an unsaved draft. Existing Navigation outcomes
determine installation and visible failure.

Only completion for the current edit and its pending action can release that
action. A late outcome must not close a later editor or authorize a different
departure. Correlation must consume the participating owners' actual
identities; it must not be inferred from package names or rendered content.

Tab close, reload, crashes, and recovery of abandoned drafts are outside this
in-app decision contract. No draft autosave or crash-recovery guarantee is
introduced. A browser unload warning is not a substitute for the in-app
Save/Discard/Stay decision.

## Ownership and adoption

The consumer is Inspect Web's Workspace editor and its ordinary navigation
entry points. This Browser-only interaction does not add a shared product
substrate or change the stateless CLI. Shared prerequisites retain the CLI
and Browser adoption plan in #6012.

| Supporting owner | Retained responsibility |
| --- | --- |
| [Workspace Scope](workspace-scope-and-expansion.md) | Membership, traversal policy, admission, and publication |
| [Workspace Definitions](workspace-definitions.md) | Canonical intent, packet identity, projection, and restoration |
| [Saved Workspaces](inspect-web-saved-workspaces.md) | Local named-entry storage, write results, and supported save destinations |
| [Navigation](inspection-subject-navigation.md) and its [Browser consumer](inspect-web-navigation-consumer.md) | Destination validity, installation, history, focus, and effect authority |
| [Shell](inspect-web-shell-interaction.md) and [Presentation](inspect-web-navigation-presentation.md) | Controls, dialogs, placement, and rendering |

There are four delivery stages for #6024, refining the existing eight-stage
experience plan rather than replacing it:

1. Lock this interaction contract and correct the experience mockups.
2. Supply the missing owner-backed edit-save completion through focused
   Scope, Definitions, and Saved Workspaces adoption under #6012. This stage
   must support explicit unnamed-save destinations and valid intent-only
   saving, and close persistence/admission correlation and failure behavior
   before editor Save can be offered; this document does not prescribe their
   internals.
3. Adopt the editor eligibility and leave decisions in Browser controls and
   navigation through focused work in #6012's Browser stage. Model the
   asynchronous edit/save/departure interaction with owner-issued identities
   before implementation, retaining the Navigation owner's model authority.
4. Include the supported experience in a separately authorized release and
   website deployment.

No existing architecture is replaced. Existing viewer Add and named Save
behavior remain unchanged until their focused adoption; there is no
repository-wide Open-to-Inspect rename in this slice. Platform opening stays
with #6013. Package Query "Load as workspace" remains an undeveloped note.

## Acceptance and evidence

The current evidence is a contract walkthrough and informative mockups, not
runtime or model conformance. Future Browser Node/original-host and Firefox
gates must exercise these outcomes through product-owned operations:

| Scenario | Required observation |
| --- | --- |
| Edit permissions or package choices | The draft changes without applying it to the committed Workspace; draft inspection is unavailable |
| Request inspection through another in-app entry point | Dirty edits receive Save/Discard/Stay before departure; no draft is executed |
| Save an unnamed or valid intent-only draft | A new local name is requested explicitly; valid intent-only saving requires no package-content acquisition |
| Save succeeds | Persisted intent and the ready Workspace match the requested edit before inspection is enabled |
| Save fails or only part of its requested work succeeds | The draft and error remain visible; no completed-save claim or departure occurs |
| Cancel, Discard, or Stay | Each has the declared effect on draft retention, persistence, and departure |
| Save succeeds but the pending destination is no longer valid | The save remains completed; Navigation supplies the failure without recreating a dirty draft |
| A late completion arrives after the owning edit is no longer current | It cannot release a different action or close a later editor |
| Inspect a package directly from Home without entering Edit | Ordinary inspection does not require a named save |

Interactive controls use existing Browser typed view models and DOM bindings,
not Markout; they are stateful forms and navigation decisions rather than a
new multi-format result domain. Component Release gates remain with their
owners. No runtime property above is claimed enforced by this docs-only slice.
