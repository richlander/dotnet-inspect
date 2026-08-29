# Inspect Web Navigation Consumer Model

This directory models the Inspect Web UI's consumption of opaque effect
authority returned by
[Inspection Subject Navigation](../../inspection-subject-navigation.md). It
supports the interaction contract in
[Inspect Web UI](../../inspect-web-ui.md).

The model starts at the UI consumer boundary after the product navigation
session has accepted an explicit intent or the consumer has requested dedicated
synchronization. Product-owned snapshot construction, reconciliation,
recommendation, supersession rules, acknowledgement receipts, and facet
membership remain abstract. The model owns only the consumer sequence:

1. receive a typed semantic outcome, synchronization disposition, and
   authority;
2. install the complete snapshot and its UI location/history commit whenever
   the disposition is `Synchronization required`;
3. run deferred focus and status-announcement effects;
4. revalidate authority before every visible effect;
5. acknowledge authority after all required effects complete and clear
   synchronization debt; or
6. abandon returned authority without clearing synchronization debt when it
   becomes stale, returns after destruction, or belongs to a destroyed surface
   lifetime; and
7. request fresh synchronization authority after remount or another terminal
   abandonment leaves the debt outstanding.

`UiEffectLifecycle.tla` models two bounded consumer operations and two
mounted-surface lifetimes. An operation may be an explicit semantic result or a
dedicated synchronization result; assigning a model token to the latter does
not claim that the product creates a semantic navigation intent. Every explicit
semantic outcome may carry either disposition, proving that semantic outcome
does not decide installation; a returned dedicated synchronization result is
necessarily `Synchronization required`. Every effect is a separate transition,
so a newer intent or surface destruction may intervene between installation and
a later callback.

The model records only whether an unacknowledged synchronization obligation
exists. It never represents, compares, or orders product snapshot revisions.
`RequestSynchronization` and `ReturnSynchronization` abstract the dedicated
product protocol under fresh authority. A current explicit result may instead
discharge the same debt. The finite operation bound limits only model
exploration; it is not a UI or product retry ceiling.

Inspection Subject Navigation discards superseded work without publishing a
consumer result or effect authority. The model represents that terminal path as
`DiscardSuperseded`, separately from returned authority. It also allows
installation to replace the destination renderer while atomically abandoning
other returned authority from the outgoing lifetime and transferring later
callbacks to the new surface lifetime.

The model collapses the UI into one logical navigation-authority holder and one
current destination-surface lifetime. Actual modals and routed components may
coexist, but they do not independently consume the same returned navigation
authority. A persistent shell focus anchor and polite status region remain
mounted outside those lifetimes. The model represents the announcement effect
abstractly rather than encoding its DOM target. The session-scoped consumer
applies the modeled rule to the destination lifetime associated with each
result. The bounded model conservatively parks that anchor when an intent
begins; the UI prose requires parking only when the focused element may be
removed.

Because every modeled intent begins with focus parked on the shell, no reachable
model behavior starts a renderer-replacing installation with focus inside the
outgoing destination. Replacement-specific menu, modal, and control focus
handoffs are therefore carried by the named implementation gates, not by the
detached-focus mutation.

## Checked properties

The model states these required properties:

- every visible effect uses current authority for the current mounted surface;
- focus and announcement never precede a required snapshot installation;
- installation does not perform the separately deferred focus effect;
- acknowledgement occurs only after all required effects complete;
- surface destruction abandons every returned authority held by that surface;
- renderer replacement abandons other outgoing-lifetime authority before
  transfer;
- the typed synchronization disposition, not semantic outcome, decides whether
  installation is required;
- abandonment preserves synchronization debt;
- remount requests synchronization when debt remains;
- acknowledgement of a synchronization-required result clears the debt;
- an outstanding synchronization request belongs to the current mounted
  surface;
- focus remains on the persistent shell anchor or the current mounted surface;
- every returned authority is eventually acknowledged or abandoned;
- every submitted intent eventually returns and settles or is discarded by
  product supersession; and
- outstanding debt without returned authority eventually requests
  synchronization, unless the surface is destroyed, the debt is discharged, or
  another current result arrives first.

The primary configuration exhaustively checks state shape and the three
temporal properties while the required guards are enabled. Twelve mutation
configurations disable one safety guard at a time and retain an independent
witness invariant, demonstrating that each safety rule is non-vacuous. These
are model claims, not implementation-conformance claims. Required implementation
gates are named in the owning UI document.

## Model checking

The checked environment was:

- TLA+ Tools v1.8.0, TLC build `2026.08.21.155922`, revision `9787e65`;
- OpenJDK `25.0.4.1`, Homebrew build; and
- macOS 26.6.2 on Apple silicon.

Run the primary model from this directory:

```bash
/opt/homebrew/opt/openjdk@25/bin/java \
  -cp "$HOME/.local/share/tlaplus/tla2tools.jar" \
  tlc2.TLC -workers auto \
  -config UiEffectLifecycle.cfg UiEffectLifecycle.tla
```

The complete breadth-first check generated 72,799 states, found 49,106 distinct
states, reached depth 17, and reported no errors. Action coverage was nonzero
for every modeled transition:

| Action | Distinct | Invocations |
| ------ | -------: | ----------: |
| `BeginIntent` | 221 | 265 |
| `ReturnResult` | 5,610 | 9,432 |
| `RequestSynchronization` | 60 | 588 |
| `ReturnSynchronization` | 30 | 30 |
| `DiscardSuperseded` | 39 | 713 |
| `RunEffect` | 10,572 | 22,116 |
| `Acknowledge` | 2,826 | 3,762 |
| `AbandonStale` | 5,712 | 14,502 |
| `DestroySurface` | 19,483 | 29,814 |
| `MountSurface` | 4,552 | 6,478 |

The coverage figures use one worker so action counters are deterministic:

```bash
/opt/homebrew/opt/openjdk@25/bin/java \
  -cp "$HOME/.local/share/tlaplus/tla2tools.jar" \
  tlc2.TLC -workers 1 -coverage 1 \
  -config UiEffectLifecycle.cfg UiEffectLifecycle.tla
```

## Mutation probes

Twelve configurations disable one required guard while retaining an independent
witness invariant:

| Configuration | Disabled rule | Detected by |
| ------------- | ------------- | ----------- |
| `StaleEffectMutation.cfg` | authority revalidation before each effect | `NoUnauthorizedVisibleEffect` |
| `EarlyAcknowledgeMutation.cfg` | completion before acknowledgement | `AcknowledgeOnlyAfterEffects` |
| `DestroyWithoutAbandonMutation.cfg` | abandonment during destruction | `DestroyAbandonsReturnedAuthority` |
| `FocusBeforeInstallMutation.cfg` | installation before dependent effects | `SnapshotInstallsBeforeDependentEffects` |
| `InstallMovesFocusMutation.cfg` | installation preserving the separately deferred focus effect | `DeferredFocusRunsOnlyInFocusEffect` |
| `DetachedFocusMutation.cfg` | persistent focus handoff during intent begin and destruction | `FocusRemainsOnMountedElement` |
| `ReplaceWithoutAbandonMutation.cfg` | outgoing-lifetime abandonment during renderer replacement | `ReplacementAbandonsOutgoingAuthority` |
| `OutcomeDrivenInstallMutation.cfg` | disposition-driven installation independent of semantic outcome | `DispositionControlsInstallation` |
| `AbandonClearsDebtMutation.cfg` | preservation of synchronization debt during abandonment | `AbandonmentPreservesSynchronizationDebt` |
| `RemountWithoutSynchronizationMutation.cfg` | fresh synchronization request after remount with debt | `RemountRequestsSynchronization` |
| `AcknowledgeLeavesDebtMutation.cfg` | clearing synchronization debt only after complete acknowledgement | `AcknowledgeClearsSynchronizationDebt` |
| `RequestSurvivesLifetimeMutation.cfg` | retirement of a synchronization request when its surface lifetime ends | `SynchronizationRequestShape` |

TLC finds a counterexample for every mutation. Exact partial-state counts are
not recorded because parallel workers may discover the first counterexample in
a different order.

## Deliberate omissions

The model does not encode:

- subject or lens recommendation;
- registry descriptor membership, order, labels, or statuses;
- canonical packet decoding or restoration composition;
- exact ARIA roles, keyboard keys, or focus-target identities;
- push, replace, and adopt history classification, Back/Forward initiation, and
  non-installing history-entry realignment;
- modal layout and responsive rendering; or
- product work, maintenance ordering, synchronization request scheduling,
  acknowledgement receipt contents, or snapshot retention.

Product-initiated maintenance and dedicated synchronization use the same UI
effect lifecycle in the owning prose, but this model does not reproduce the
product scheduler that admits, orders, or settles those requests. It assumes
the product contract that any current result issued while acknowledgement lags
carries `Synchronization required`. Implementation conformance is covered by
the named maintenance and synchronization consumer gates.

Those contracts remain in their owning product documents or in readable UI
prose. The model checks only the stateful consumer lifecycle for visible
effects.
