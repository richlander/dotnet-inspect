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
2. install a replacement snapshot when the result supplies one;
3. run deferred focus and status-announcement effects;
4. revalidate authority before every visible effect;
5. acknowledge authority after all required effects complete; or
6. abandon returned authority when it becomes stale or its surface is
   destroyed.

`UiEffectLifecycle.tla` models two explicit intents and two mounted-surface
lifetimes. Applied and unavailable outcomes require installation, focus, and
announcement. Rejected and failed outcomes retain the current snapshot and
require only focus and announcement. Every effect is a separate transition, so
a newer intent or surface destruction may intervene between installation and a
later callback.

## Checked properties

The primary configuration checks:

- every visible effect uses current authority for the current mounted surface;
- focus and announcement never precede a required snapshot installation;
- acknowledgement occurs only after all required effects complete;
- surface destruction abandons every returned authority held by that surface;
- every returned authority is eventually acknowledged or abandoned; and
- every submitted intent eventually returns and reaches a terminal consumer
  state.

These properties are model claims, not implementation-conformance claims.
Implementation gates are named in the owning UI document.

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

The complete breadth-first check generated 10,418 states, found 6,310 distinct
states, reached depth 16, and reported no errors. Action coverage was nonzero
for every modeled transition:

| Action | Distinct | Invocations |
| ------ | -------: | ----------: |
| `BeginIntent` | 67 | 85 |
| `ReturnResult` | 1,120 | 2,256 |
| `RunEffect` | 1,050 | 2,376 |
| `Acknowledge` | 300 | 444 |
| `AbandonStale` | 828 | 2,904 |
| `DestroySurface` | 2,187 | 3,626 |
| `MountSurface` | 757 | 945 |

## Mutation probes

Four configurations disable one required guard while retaining an independent
witness invariant:

| Configuration | Disabled rule | Detected by |
| ------------- | ------------- | ----------- |
| `StaleEffectMutation.cfg` | authority revalidation before each effect | `NoUnauthorizedVisibleEffect` |
| `EarlyAcknowledgeMutation.cfg` | completion before acknowledgement | `AcknowledgeOnlyAfterEffects` |
| `DestroyWithoutAbandonMutation.cfg` | abandonment during destruction | `DestroyAbandonsReturnedAuthority` |
| `FocusBeforeInstallMutation.cfg` | installation before dependent effects | `SnapshotInstallsBeforeDependentEffects` |

TLC finds a counterexample for every mutation. The first violations appear
after 89, 78, 86, and 120 distinct states respectively.

## Deliberate omissions

The model does not encode:

- subject or lens recommendation;
- registry descriptor membership, order, labels, or statuses;
- canonical packet decoding or restoration composition;
- exact ARIA roles, keyboard keys, or focus-target identities;
- modal layout and responsive rendering; or
- product work, maintenance ordering, or snapshot retention.

Those contracts remain in their owning product documents or in readable UI
prose. The model checks only the stateful consumer lifecycle for visible
effects.
