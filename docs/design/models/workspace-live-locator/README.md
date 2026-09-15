# Workspace live-locator interaction model

Design evidence for [Workspace Live Locator](../../workspace-live-locator.md),
not implementation certification.

## Scope and currencies

One Workspace, two immutable assembly occurrences in distinct retained groups,
two callers, and one committed append exercise the initialization/notification,
query/index, cancellation, and close/read interactions. The initial occurrence
is Package-origin and the addition Platform-origin. Each has fixed `Ready` or
`Rejected` Metadata evidence; all four assignments are explored.

`Receipt(n)` represents the association among issuing Workspace, exact
committed publication, and its ordered occurrence roster. `n` is a finite
abstraction of distinct owner-issued publication evidence, not an event count
or an implementation counter protocol. `PublishAppend` supplies an already
committed append at the Workspace observation boundary; it does not model or
reimplement Artifact acquisition/publication or logical Scope revision rules.
The facade captures this receipt at request admission and never joins later
occurrences into that request.

Inventories are keyed by occurrence; returned evidence retains coordinate,
origin, and context. Candidate **sets** abstract the stateless locator's
ordered vectors because this model checks population association, not the
already-owned matching, ordering, or cardinality contract. Detached answers
retain their values when the resident inventory is cleared on close.

`Starting` separates initial demand from the coherent snapshot/subscription
handoff. An append in that interval must appear at activation. An active
append sets a coalescing invalidation, and `Observe` reads the authoritative
receipt. Active request admission independently reconciles that receipt.
Work is single-flight and serialized in this bounded model; no thread-pool
or wall-clock assumption is made. Caller cancellation changes only its
attachment, not the shared read.

## Imported release boundary

Named instances `One` and `Two` consume
[`AssemblyContextGroupReleaseLifecycle`](../../../models/assembly-context-group-lifecycle/AssemblyContextGroupReleaseLifecycle.tla).
Each binds the exact `<<Workspace, occurrence>>` group, its request/completion
state, and a result from `{Released, ReleaseFailed}`. Both inherit the owner's
distinct group/sentinel and nonempty result-domain assumptions.

`BeginClose` uses the owner's release-request action for admitted groups.
`ReleaseOne`/`ReleaseTwo` use its completion action with the corresponding
read's quiescence predicate. Both owner safety specifications and their
identity/result invariants are rechecked under this composition. Release
failure remains a result, not successful release.

The early-release negative control weakens the supplied quiescence predicate,
demonstrating that the consumer's read/release association is necessary.
The bootstrap reachability control demonstrates that adding the owner guards
does not make append-during-initialization and an old-revision answer
unreachable.

## Gates and limits

| Configuration | Required exit | Evidence |
| --- | --- | --- |
| `Safety.cfg` | 0 | Exact answers, lazy/single construction, detached results after close, group release association and owner refinement. |
| `Liveness.cfg` | 0 | Under weak fairness, active additions settle, admitted requests terminate, and requested close drains. |
| `BrokenBootstrap.cfg` | 13 | Snapshot-before-subscribe loses an append if no later request rescues it. |
| `BrokenRetagAnswer.cfg` | 12 | Labeling an old request with the newer population mixes receipt/evidence. |
| `BrokenEarlyRelease.cfg` | 12 | Releasing a group during its inventory borrow violates quiescence. |
| `ReachabilityBootstrapRace.cfg` | 12 | A correct bootstrap catches the racing append while the first query retains its earlier receipt. |

The negative and reachability configurations deliberately violate their named
properties. Exact verdicts are required by `eng/tla-expected-exit-codes.txt`;
another coherent verdict is not success.

Progress assumes fair scheduling of enabled activation, observation,
inventory completion, response, and release actions. It does not promise
termination of an uncooperative external opener. Close or explicit Metadata
rejection can terminate the corresponding modeled obligation.

Unmodeled: concrete producer correspondence and admission transactions,
Metadata decoding/matching/visibility, unknown upstream membership gaps,
explicit work bounds, retry of operational failures, fatal worker recovery,
multiple appends, content sharing across occurrences, and concrete C#/Wasm
scheduling. These remain owner contracts and required implementation evidence,
not properties proved by this model. In particular, fixed `Rejected` evidence
is not a model of caching transient operational failures.

Run with the repository-pinned TLA Tools jar:

```sh
TLA_TOOLS_JAR=/path/to/pinned/tla2tools.jar \
  bash eng/run-tla-checks.sh docs/design/models/workspace-live-locator
```

Recorded local run: pinned TLA Tools `2026.08.11.125311`, OpenJDK 21.0.12,
two workers. Both positive configurations completed with 7,784 distinct states
and 13,276 generated states; all six configurations produced their exact
required verdicts. This records a bounded design check, not product conformance.
