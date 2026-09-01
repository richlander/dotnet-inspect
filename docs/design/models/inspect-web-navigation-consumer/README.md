# Inspect Web Navigation Consumer Model

This directory models the Inspect Web UI's consumption of opaque effect
authority returned by
[Inspection Subject Navigation](../../inspection-subject-navigation.md). It
supports the interaction contract in
[Inspect Web Navigation Consumer](../../inspect-web-navigation-consumer.md).

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
7. retain one pending synchronization request's identity and originating
   lifetime through product settlement, abandon an old-lifetime response, and
   request fresh authority only after the prior request settles.

`UiEffectLifecycle.tla` models two bounded consumer operations and three
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
It does retain bounded opaque synchronization-request identities and their
originating surface lifetimes so a late response cannot be mistaken for a
fresh request after remount. `RequestSynchronization` and
`ReturnSynchronization` abstract the dedicated product protocol under fresh
authority. Only one request is pending; a current explicit result may settle it
while discharging the same debt. Request creation remains a separate transition
after remount rather than an atomic part of mounting. The model admits a request
only while one bounded operation token remains available to represent a
current-lifetime response. That capacity rule prevents a finite-state
exploration artifact; the bounds of two operations, three surface lifetimes,
and two request identities are not a UI or product retry ceiling.

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
- eligible debt after remount eventually requests synchronization;
- acknowledgement of a synchronization-required result clears the debt;
- one synchronization request remains pending until product settlement;
- a synchronization response is consumed only by its originating mounted
  lifetime and is otherwise abandoned;
- focus remains on the persistent shell anchor or the current mounted surface;
- every returned authority is eventually acknowledged or abandoned;
- every submitted intent eventually returns and settles or is discarded by
  product supersession; and
- outstanding debt without returned authority eventually requests
  synchronization, unless the surface is destroyed, the debt is discharged, or
  another current result arrives first; and
- every exact synchronization request eventually reaches product settlement.

The primary configuration exhaustively checks state shape and four temporal
properties while the required guards are enabled. Eleven mutation
configurations disable one safety guard at a time and retain an independent
witness invariant. Two more disable liveness guards and violate the matching
temporal property. Together they demonstrate that all thirteen checked rules
are non-vacuous. These are model claims, not implementation-conformance claims.
Required implementation gates are named in the owning UI document.

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

The complete breadth-first check generated 166,998 states, found 117,928
distinct states, reached depth 20, and reported no errors. Action coverage was
nonzero for every modeled transition:

| Action | Distinct | Invocations |
| ------ | -------: | ----------: |
| `BeginIntent` | 621 | 900 |
| `ReturnResult` | 12,876 | 22,296 |
| `RequestSynchronization` | 120 | 240 |
| `ReturnSynchronization` | 4,560 | 6,720 |
| `DiscardSuperseded` | 78 | 1,706 |
| `RunEffect` | 20,130 | 42,522 |
| `Acknowledge` | 5,406 | 6,810 |
| `AbandonStale` | 8,154 | 29,976 |
| `DestroySurface` | 46,145 | 65,816 |
| `MountSurface` | 19,837 | 22,549 |

The coverage figures use one worker so action counters are deterministic:

```bash
/opt/homebrew/opt/openjdk@25/bin/java \
  -cp "$HOME/.local/share/tlaplus/tla2tools.jar" \
  tlc2.TLC -workers 1 -coverage 1 \
  -config UiEffectLifecycle.cfg UiEffectLifecycle.tla
```

## Mutation probes

Thirteen configurations disable one required guard. Eleven retain an
independent witness invariant; the two liveness mutations violate their
matching temporal property:

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
| `RemountWithoutSynchronizationMutation.cfg` | eventual synchronization request for eligible debt after remount | `EveryOutstandingDebtRequestsSynchronization` |
| `AcknowledgeLeavesDebtMutation.cfg` | clearing synchronization debt only after complete acknowledgement | `AcknowledgeClearsSynchronizationDebt` |
| `StaleSynchronizationResponseMutation.cfg` | exact request/lifetime correlation before consuming a synchronization response | `SynchronizationResponseMatchesRequestLifetime` |
| `StrandedSynchronizationRequestMutation.cfg` | request admission only while bounded response capacity remains | `EverySynchronizationRequestSettles` |

TLC finds a counterexample for every mutation. Exact partial-state counts are
not recorded because parallel workers may discover the first counterexample in
a different order.

## Reachability probe

`StaleResponseRecovery.cfg` is expected to violate
`NoStaleResponseRecoveryObserved`. Its counterexample proves the complete
recovery sequence is reachable within the primary bounds: a request from an
old surface lifetime returns after destruction and is abandoned, then a second
request from the current lifetime returns and is consumed. Three surface
lifetimes are required to exercise the initial debt, the pending old-lifetime
request, and the fresh current-lifetime response in one trace.

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
