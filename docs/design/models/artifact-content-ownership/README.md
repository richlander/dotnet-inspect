# ArtifactContentOwnership

`ArtifactContentOwnership.tla` models the Artifact-owned lifecycle introduced by
[Artifact Ownership and Borrowing](../../artifact-ownership-and-borrowing.md):
current query-policy issuance, a transferred per-content child lease,
authorization replacement, ownership-backed scoped borrowing, session
retirement, and backing acquisition-resource release.

## Boundary

The model preserves the contract distinction that motivates the design:

- a query lease must be current when the Artifact owner issues a content child;
- a successfully issued child remains usable after query authorization is
  replaced;
- retirement rejects new children but waits for issued children and admitted
  borrows; and
- backing acquisition resources release only after that wait completes.

One modeled content item is sufficient for the lifetime properties. `Holders`
contains at least two possible child owners so settlement cannot accidentally
depend on a single distinguished consumer. Exact Artifact-reference matching,
bytes, roles, digests, callbacks, cleanup exceptions, and aggregate Library
membership remain product-test obligations.

The model does not import
[`ArtifactGenerationAccess`](../artifact-generation-access/README.md). That
model owns opener/materialization registration and returned-stream quiescence.
This model owns the new child-obligation ordering. Product session settlement
must satisfy both independent contracts before releasing acquisition
resources.

## Configurations

Positive configurations:

- [`Safety.cfg`](Safety.cfg) checks current-policy issuance, independence of an
  issued child from later query-policy replacement, child-before-backing
  release, and live-borrow coherence.
- [`Liveness.cfg`](Liveness.cfg) checks that a requested retirement eventually
  settles when every live child is fairly released and every admitted borrow
  fairly completes.

Broken-policy configurations, each expected to report a violation:

- [`BrokenUngatedIssuance.cfg`](BrokenUngatedIssuance.cfg) permits child
  issuance through stale query authority or during retirement.
- [`BrokenQueryBoundChild.cfg`](BrokenQueryBoundChild.cfg) incorrectly
  revalidates current query policy when an already issued child borrows.
- [`BrokenImmediateRelease.cfg`](BrokenImmediateRelease.cfg) releases backing
  resources before live children settle.

Reachability probes, each expected to report a violation:

- [`ReachabilityBorrowAfterReplacement.cfg`](ReachabilityBorrowAfterReplacement.cfg)
  reaches an ownership-backed borrow after query authorization replacement.
- [`ReachabilityOldQueryRejection.cfg`](ReachabilityOldQueryRejection.cfg)
  reaches rejection of later child issuance through the stale query lease.
- [`ReachabilityRetirementAfterChild.cfg`](ReachabilityRetirementAfterChild.cfg)
  reaches successful retirement after a previously issued child releases.

## Fairness

Query replacement, child issuance, borrow requests, and retirement requests are
environment choices and are unfair. Once retirement is requested, beginning
retirement and releasing backing resources are weakly fair. Each admitted
borrow eventually completes. Child release is strongly fair: a child owner
cannot borrow forever in every interval where it could settle instead. Those
are explicit consumer obligations; an abandoned child leaves session
settlement incomplete rather than authorizing forced revocation.

## Checked results

TLC 2026.08.21.155922 (rev `9787e65`, from the pinned `tla2tools.jar`) with two
holders produced:

- `Safety.cfg`: 914 states generated, 333 distinct, depth 12, with all six
  invariants passing;
- `Liveness.cfg`: the same complete graph, with
  `RetirementEventuallySettles` passing;
- each broken-policy configuration fails its one intended invariant; and
- all three reachability probes reach their intended path.

These results establish bounded evidence about the model, not implementation
conformance.
