# Inspect Web navigation location-intent model

## Owner and claim

[Inspect Web Navigation
Consumer](../../inspect-web-navigation-consumer.md#location-intent-and-publication-ownership)
owns one page-session location-intent identity and the pure classification that
permits a current result to push, replace, adopt, realign, or leave browser
history unchanged.

The model checks that a browser-selected entry is captured before any wait,
that only the current location intent may publish, and that asynchronous
completion retains its originating identity. A newer non-browser intent is
admitted only after an unresolved selected entry is realigned to the installed
incumbent. A still-current failed traversal performs that same repair.

## Boundary and join currency

The location-intent identity is the modeled join currency. It associates one
captured source event, target, history policy, installed result association,
and permitted history effect. It is consumer-issued and page-session local.
The installed result association is modeled as a separate opaque value so the
model can distinguish same-location postings and reject an effect carrying a
foreign association.

The model consumes retained-realization cutover as an irreversible
`CompleteIrreversible` event associated with the originating location intent.
That event abstracts the exact retained-definition ID, realization ID,
publication ordinal, effect authority, and completion receipt already owned
and checked by the retained-realization designs. It does not reproduce their
lifecycle or imply that the location-intent identity grants managed or
Navigation authority.

When a browser traversal becomes current during an older accepted cutover, the
cutover still installs its successor but yields its history effect. The
captured traversal remains the only owner that may later adopt, replace, or
realign its selected entry.

## Modeled behavior

Three bounded intent identities and three locations are enough to exercise:

- synchronous browser selection during an accepted cutover;
- exact, changed, and failed traversal outcomes;
- explicit push and replace policies;
- a failed post-cutover history write that leaves an unresolved location
  obligation without rolling back the installed successor;
- pre-admission realignment before a newer non-browser intent;
- failed pre-admission realignment that leaves failure visible and issues no
  newer intent;
- a later browser traversal replacing an older unresolved traversal; and
- a delayed ordinary asynchronous completion after a newer intent is current.

The model treats a successful history operation as synchronous, matching the
browser History API. A failed operation changes no browser entry and leaves
the current association unresolved. It does not model browser persistence,
cross-page history identity, packet decoding, acquisition, presentation
content, focus, announcement, synchronization debt, or managed settlement.

This is a safety model. Navigation request progress remains owned by Inspection
Subject Navigation, and accepted-cutover completion remains owned by Inspect
Web Retained Workspace Realization, so this model states no independent
liveness claim for either.

## Checked properties

`Safety` checks:

- an aligned selected entry identifies the installed location;
- a current waiting browser intent captured the exact selected entry;
- every successful history effect belonged to the current location intent;
- every successful history effect carried the exact installed result
  association;
- a newer non-browser intent repaired an unresolved entry before admission;
- a failed pre-admission repair remained visible and admitted no newer intent;
- a current failed traversal realigned to the installed incumbent;
- delayed completion did not mint or reacquire location ownership;
- each push, replace, adopt, or realign effect was allowed by its source
  declaration; and
- an older irreversible cutover did not overwrite a newer browser-selected
  entry.

Five mutation configurations represent the historical findings assigned to
[#7705](https://github.com/richlander/dotnet-inspect/issues/7705). F10, F12,
and F13 share one mutation because compatibility selection and active deletion
with either successor are all the same stale Workspace publication defect at
this owner boundary. A sixth contract mutation substitutes a foreign installed
association while preserving the same location.

Six reachability configurations prove the pathological paths are present:
cutover yielding to a traversal, failed-traversal realignment, successful and
failed pre-admission realignment, stale asynchronous completion, and
post-cutover history failure.

## Configurations

Every configuration is pinned in
[`eng/tla-expected-exit-codes.txt`](../../../../eng/tla-expected-exit-codes.txt).

| Configuration | Exit | Evidence |
| ------------- | ---: | -------- |
| `Safety.cfg` | 0 | Complete bounded safety state space |
| `BrokenF08LateTraversalCapture.cfg` | 12 | Traversal target must be captured synchronously |
| `BrokenF10F12F13StaleWorkspacePublication.cfg` | 12 | Selection or deletion cannot overwrite a newer selected entry |
| `BrokenF14FailedTraversalNotRealigned.cfg` | 12 | Current failed traversal must repair its selected entry |
| `BrokenF15AdmissionBeforeRealignment.cfg` | 12 | Non-browser admission cannot strand an older selected entry |
| `BrokenF16CompletionMintsIntent.cfg` | 12 | Delayed completion cannot reacquire current ownership |
| `BrokenForeignInstalledAssociationEffect.cfg` | 12 | A history effect must carry the exact installed association |
| `ReachabilityTraversalDuringCommit.cfg` | 12 | An accepted cutover yields history to a newer traversal |
| `ReachabilityFailedTraversalRealignment.cfg` | 12 | Failed traversal repairs to the installed incumbent |
| `ReachabilityPreAdmissionRealignment.cfg` | 12 | Repair occurs before newer non-browser admission |
| `ReachabilityPreAdmissionRealignmentFailure.cfg` | 12 | Failed repair rejects newer non-browser admission visibly |
| `ReachabilityStaleAsyncCompletion.cfg` | 12 | Older asynchronous completion returns after supersession |
| `ReachabilityPostCutoverHistoryFailure.cfg` | 12 | Installed successor survives a failed history write |

Exit 12 is the expected invariant counterexample for a broken policy or
negated reachability witness.

## Run

From the repository root, with the pinned tools:

```bash
model=docs/design/models/inspect-web-navigation-location-intent
mkdir -p "$model/.scratch"
TLA_TOOLS_JAR="$HOME/.local/share/tlaplus/tla2tools.jar" \
TMPDIR="$PWD/$model/.scratch" \
JAVA_TOOL_OPTIONS="-XX:ActiveProcessorCount=2 -Djava.io.tmpdir=$PWD/$model/.scratch" \
  bash eng/run-tla-checks.sh "$model"
npx --no-install markdownlint-cli "$model/README.md"
```

The focused run on 2026-09-20 used TLA Tools
`2026.08.11.125311` (`ab323b79802aedc3203b3f9af37c6aca3ed43f4e0225b36f2aa77b26de46c05f`)
with OpenJDK `21.0.12`. `Safety.cfg` explored 441,274 generated states and
179,401 distinct states to depth 11. All six mutations produced their expected
counterexample, all six pathological paths were reachable, and all thirteen
configured semantic verdicts matched the manifest.
