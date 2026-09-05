# Inspect Web saved Workspaces

## Claim and scope

The Workspace page can save the current nonempty Workspace under a local name,
open that saved definition later, and forget it with the shared trailing close
control. There remains exactly one live Workspace. A saved name identifies a
definition, not another live Workspace or the owner of the current packages.
This document owns the focused Browser interaction and local saved-entry store.

The consumer is the existing Inspect Web application, tracked by #5932 and
the end-to-end tracker #5697. The user approved this Browser-only
Save/Open/Forget slice after #5889.
Production-host adoption has two steps: land the complete UI integration, then
include it in a separately authorized normal release and website deployment.
There is no alternative architecture to retire.

[Workspace Definitions](workspace-definitions.md) owns canonical packet shape
and projection. [Navigation Consumer](inspect-web-navigation-consumer.md) owns
existing restoration, history, and result focus. [Navigation
Presentation](inspect-web-navigation-presentation.md) supplies the Workspace
page; [Package-row removal](inspect-web-package-removal.md) supplies the close
control convention. These contracts are consumed, not redefined.

Add, prefixes, Clear, renaming or updating saves, import/export, synchronization,
server storage, and the paused editor in #5812 are separate work.

## Behavior

- Save is offered on the Workspace page, enabled for a nonempty ready Workspace.
  A transient inline name form avoids a new persistent editor or modal.
- Saving captures the current Workspace through the existing canonical share
  projection, including its exact coordinates and Workspace presentation.
  A save pins resolved versions and frameworks even when the opened share
  definition originally floated them, retaining packet-local identities and
  context associations without changing the live share intent.
  It does not change live membership, selection, URL, or clipboard.
  A non-projectable Workspace fails visibly rather than saving partial scope.
- Names are trimmed, nonempty, and unique case-insensitively. Saving never
  silently replaces another entry. The UI and store limit names to 120
  characters; packet limits remain the packet owner's responsibility.
- The compact saved list appears on the Workspace page, including when no
  packages are loaded. Listing saved entries performs no acquisition or packet
  decoding. Entries retain their insertion order.
- Open is an explicit replacement through the existing transactional
  replace-and-restore path. The canonical decoder remains authoritative;
  unsupported or unavailable saved packets fail through that path, retain the
  prior Workspace and source history entry, and keep the saved entry available.
  Successful Open uses the existing result-focus and history classification.
- The saved entry's close control is separate from Open. It forgets only the
  named definition, not the live Workspace, recent packages, or browser history.
  Focus moves to the next close control, then the preceding one, then Save
  when available or the Workspace heading.
- Name input, selection, and saved-row focus survive ordinary Workspace
  rerenders. Save completion returns focus to the newly saved Open action;
  cancel returns focus to Save.
- Saves live in this browser's origin-local storage. They survive page refresh
  but are not a backup, cloud account, or package-content cache.
- Writes complete before in-memory entries change. Storage errors leave
  entries unchanged and remain visible. Unreadable or unsupported stored data
  is not replaced with an empty success or automatically overwritten; Retry
  rereads it. An invalid packet in an otherwise readable entry can still be
  forgotten without decoding it.

## Convention and evidence

The baseline is a named browser bookmark: a durable local reference with
separate Open and Forget actions. Unlike a URL bookmark, the record retains
only the owner-issued Workspace packet, so the current host constructs the
destination and reapplies its own acquisition policy. No browser implementation
code is copied.

The Browser retains typed saved-entry and view models through its interactive
HTML renderer, with the existing escaped-text and close-control helpers.
Markout is not used for these Browser form and button controls. This is not a
new shared product substrate or broad multi-format information domain.

The enforcing gates are the existing Node runner's saved-workspace storage and
original-host integration cases, the Workspace renderer/focus cases, and the
Firefox saved-workspace scenarios. They exercise save/open/forget across
refresh, exact opaque packet retention, duplicate names, read/write/projection
failure, failed or superseded restoration, and the neighboring package-removal
interaction. They are focused Browser evidence, not a claim that a Wasm
deployment was exercised.
