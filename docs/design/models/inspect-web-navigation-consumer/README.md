# Inspect Web Navigation Consumer Model

This directory models the Inspect Web UI's consumption of opaque effect
authority returned by
[Inspection Subject Navigation](../../inspection-subject-navigation.md). It
supports the interaction contract in
[Inspect Web UI](../../inspect-web-ui.md).

The model starts after the product navigation session has accepted an explicit
intent. Product-owned snapshot construction, reconciliation, recommendation,
supersession rules, and facet membership remain abstract. The model owns only
the consumer sequence:

1. receive a typed result and its authority;
2. install a replacement snapshot and its UI location/history commit when the
   result supplies one;
3. run deferred focus and status-announcement effects;
4. revalidate authority before every visible effect;
5. acknowledge authority after all required effects complete; or
6. abandon returned authority when it becomes stale, returns after
   destruction, or belongs to a destroyed surface lifetime.

`UiEffectLifecycle.tla` models two explicit intents and two mounted-surface
lifetimes. Applied outcomes and unavailable outcomes carrying a changed
snapshot require installation, focus, and announcement. Unavailable outcomes
without a replacement snapshot, rejected outcomes, failed outcomes, and typed
prerequisite aborts require only focus and announcement. Every effect is a
separate transition, so a newer intent or surface destruction may intervene
between installation and a later callback.

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

## Checked properties

The model states these required properties:

- every visible effect uses current authority for the current mounted surface;
- focus and announcement never precede a required snapshot installation;
- acknowledgement occurs only after all required effects complete;
- surface destruction abandons every returned authority held by that surface;
- renderer replacement abandons other outgoing-lifetime authority before
  transfer;
- focus remains on the persistent shell anchor or the current mounted surface;
- every returned authority is eventually acknowledged or abandoned; and
- every submitted intent eventually returns and settles or is discarded by
  product supersession.

The primary configuration exhaustively checks state shape and the two
settlement properties while the required guards are enabled. The six mutation
configurations disable one safety guard at a time and retain an independent
witness invariant, demonstrating that each safety rule is non-vacuous. These
are model claims, not implementation-conformance claims. Required
implementation gates are named in the owning UI document.

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

The complete breadth-first check generated 18,761 states, found 12,136 distinct
states, reached depth 16, and reported no errors. Action coverage was nonzero
for every modeled transition:

| Action | Distinct | Invocations |
| ------ | -------: | ----------: |
| `BeginIntent` | 107 | 121 |
| `ReturnResult` | 1,476 | 2,658 |
| `DiscardSuperseded` | 21 | 339 |
| `RunEffect` | 2,200 | 5,472 |
| `Acknowledge` | 660 | 972 |
| `AbandonStale` | 1,716 | 4,174 |
| `DestroySurface` | 4,589 | 7,462 |
| `MountSurface` | 1,366 | 1,570 |

The coverage figures use one worker so action counters are deterministic:

```bash
/opt/homebrew/opt/openjdk@25/bin/java \
  -cp "$HOME/.local/share/tlaplus/tla2tools.jar" \
  tlc2.TLC -workers 1 -coverage 1 \
  -config UiEffectLifecycle.cfg UiEffectLifecycle.tla
```

## Mutation probes

Six configurations disable one required guard while retaining an independent
witness invariant:

| Configuration | Disabled rule | Detected by |
| ------------- | ------------- | ----------- |
| `StaleEffectMutation.cfg` | authority revalidation before each effect | `NoUnauthorizedVisibleEffect` |
| `EarlyAcknowledgeMutation.cfg` | completion before acknowledgement | `AcknowledgeOnlyAfterEffects` |
| `DestroyWithoutAbandonMutation.cfg` | abandonment during destruction | `DestroyAbandonsReturnedAuthority` |
| `FocusBeforeInstallMutation.cfg` | installation before dependent effects | `SnapshotInstallsBeforeDependentEffects` |
| `DetachedFocusMutation.cfg` | persistent focus handoff during intent, installation, and destruction | `FocusRemainsOnMountedElement` |
| `ReplaceWithoutAbandonMutation.cfg` | outgoing-lifetime abandonment during renderer replacement | `ReplacementAbandonsOutgoingAuthority` |

TLC finds a counterexample for every mutation. Exact partial-state counts are
not recorded because parallel workers may discover the first counterexample in
a different order.

## Deliberate omissions

The model does not encode:

- subject or lens recommendation;
- registry descriptor membership, order, labels, or statuses;
- canonical packet decoding or restoration composition;
- exact ARIA roles, keyboard keys, or focus-target identities;
- modal layout and responsive rendering; or
- product work, maintenance ordering, or snapshot retention.

Product-initiated maintenance uses the same UI effect lifecycle in the owning
prose, but this model does not reproduce the product scheduler that admits or
orders maintenance. Its implementation conformance is covered by the named
maintenance consumer gate.

Those contracts remain in their owning product documents or in readable UI
prose. The model checks only the stateful consumer lifecycle for visible
effects.
