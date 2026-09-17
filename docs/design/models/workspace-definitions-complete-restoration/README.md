# Workspace Definitions complete restoration

## Owner and claim

[Workspace Definitions](../../workspace-definitions.md#complete-restoration)
owns this model for
[#7027](https://github.com/richlander/dotnet-inspect/issues/7027).
It checks one inert request, its exact resource-free `WorkspacePlan`, one fresh
unpublished Workspace, ordered context evidence, the closed restoration result,
and host-mediated cleanup before a non-install result escapes.
The product admits only schema version 2 and canonical packet format 2 to this
transaction. The model abstracts schema-version-1 and format-1 inputs as
pre-construction rejection; codec syntax and long-form composition validation
remain product gates rather than state-machine behavior.

The model uses the product's join currencies:

- one host intent ordering each restoration;
- one request and plan pair retained for that exact intent;
- one fresh Workspace identity consumed by at most one attempt; and
- one ordered context-evidence sequence associated with that Workspace.

## Boundaries and assumptions

Two attempts permit a newer host intent to supersede an older attempt before or
after construction. Two Workspaces prove that fresh construction does not reuse
identity. Two context positions expose evidence reordering without modeling the
Artifact or Scope internals that issue each receipt.

`BeginConstruction` abstracts the host's existing candidate or invocation
lifetime. `AppendEvidence` abstracts ordinary context, Root, Scope, selector,
Registry, and Navigation work while retaining their declaration order.
`Activate` represents the host returning its opaque unpublished activation
handle paired with the exact Definitions result. That atomic return is the
handoff linearization point; a later intent belongs to the host's ordinary
candidate or active-realization lifecycle. Publication and incumbent
replacement remain with the host's realization model.

`Cleanup` represents the host releasing borrowed construction access and
settling the unpublished Workspace through its existing lifetime owner.
Cleanup failure details remain visible in the product result but do not alter
the obligation to attempt settlement exactly once. Weak fairness applies only
to stale-attempt closure and admitted cleanup; the model does not assume that
acquisition, resolution, or activation succeeds.

## Checked properties

`Safety` checks:

- every Workspace identity belongs to at most one restoration attempt;
- only `Activated` carries a Workspace value;
- an activation retains its exact request, plan, and Workspace association;
- unsupported requests fail before construction;
- context evidence remains in exact declaration order;
- stale completion cannot activate; and
- every closed non-install Workspace is cleaned exactly once.

`ClosingSettles` checks that every attempt entering host-mediated cleanup
eventually reaches terminal closure.

## Gates and negative controls

Every configuration is pinned in `eng/tla-expected-exit-codes.txt`.

| Configuration | Exit | Evidence |
| --- | --- | --- |
| `Safety.cfg` | 0 | Complete bounded safety state space |
| `Liveness.cfg` | 0 | Host-mediated cleanup progress |
| `BrokenWrongAssociation.cfg` | 12 | Activation cannot pair a foreign Workspace |
| `BrokenStaleActivation.cfg` | 12 | A superseded attempt cannot activate |
| `BrokenEvidenceOrder.cfg` | 12 | Context evidence cannot be reordered |
| `BrokenRejectedConstruction.cfg` | 12 | A rejected request constructs no Workspace |
| `BrokenDoubleCleanup.cfg` | 12 | Non-install cleanup is one-shot |
| `BrokenNeverCleanup.cfg` | 13 | Omitting host cleanup defeats liveness |
| `ReachabilityActivation.cfg` | 12 | Exact activation is reachable |
| `ReachabilityRejection.cfg` | 12 | Pre-construction rejection is reachable |
| `ReachabilityFailureCleanup.cfg` | 12 | Failure cleanup is reachable |
| `ReachabilitySupersessionCleanup.cfg` | 12 | Supersession cleanup is reachable |

Exit 12 is an expected invariant counterexample for a broken policy or negated
reachability witness. Exit 13 is the expected temporal-property counterexample
for omitted cleanup.

## Run

From the repository root:

```bash
model=docs/design/models/workspace-definitions-complete-restoration
mkdir -p "$model/.scratch"
TMPDIR="$PWD/$model/.scratch" \
  JAVA_TOOL_OPTIONS="-XX:ActiveProcessorCount=2 -Djava.io.tmpdir=$PWD/$model/.scratch" \
  bash eng/run-tla-checks.sh "$model"
npx --no-install markdownlint-cli "$model/README.md"
```

The focused run on 2026-09-15 used TLA Tools `2026.08.11.125311` with
OpenJDK `25.0.4.1`. Both complete configurations explored 1,034 generated states
and 539 distinct states to depth 13. All twelve configured semantic verdicts
matched the manifest.
