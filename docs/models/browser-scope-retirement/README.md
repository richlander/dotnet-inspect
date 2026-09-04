# Browser scope retirement

This bounded adversarial model checks the **post-binding Browser registry**:
removing an entry from reuse must not return its reservation while construction,
protected use, or cleanup still owns resources. It is evidence for the planned
[Browser artifact-backed scope contract](../../../prototypes/inspect-web/README.md#artifact-backed-package-scope-adoption),
not another normative owner or a production implementation.

The production consumer is [#5576](https://github.com/richlander/dotnet-inspect/issues/5576),
within the existing adoption step of
[#5577](https://github.com/richlander/dotnet-inspect/issues/5577). The separate
awaited package-capacity prerequisite remains
[#5849](https://github.com/richlander/dotnet-inspect/issues/5849).

## Claim and boundary

The model checks charged lifetime, not allocation internals. One reservation
stands for one counted scope slot and its fixed image allowance. Pending,
ready, retiring, and failed-cleanup entries all retain that reservation.
Successful terminal cleanup alone returns it. Each exact operation has at most
one eligible entry; retiring entries may coexist with a replacement without
being revived or publishing an old result.

Three caller identities each make at most one request. Up to three fresh entry
identities are allocated, with one- and two-slot capacity configurations.
Retired identities are never recycled. These small bounds permit a joined
caller, cancellation, replacement, and delayed old completion; they are not an
unbounded proof or a check of the production four-slot constant.

Each input key is a tuple of acquired coordinate, content generation, and
selection identity. The fixed coordinate includes authoritative producer and
framework-request context. Three keys share its display labels but differ in
generation or independently issued selection identity. The model consumes
already-issued, immutable binding keys; it does not model selection-token
issuance or the unbound-request association that must occur before issuance.

`Remove` represents explicit invalidation, including during a protected query.
`Evict` chooses an idle least-recently-used ready entry at capacity; both end
eligibility immediately. `Waiting`, `Returning`, and `Using` callers hold the
same exact entry. The separate return step exposes the await-to-query gap.
Last-waiter cancellation retires construction, whereas another waiting caller
can still receive its shared result.

The current synchronous anchors are
`BrowserPackageWorkspace.ScopeEntry`, `LeaseScope`, `ReleaseScopeLease`,
`DisposeRegisteredScope`, and `BrowserScopeLease`. They explain the existing
whole-scope topology, but the model is **not** a refinement of that code.
Browser artifact adoption and its Release outcome gates remain **unverified**.

## Adjacent-owner abstraction

`Finish` supplies a factory's terminal success or failure. `Settle` supplies
the host's observation of an authoritative terminal retirement outcome for
that exact entry, including group results and artifact cleanup failures.
It is not request-only `Dispose`, and task completion without inspecting the
outcome does not correspond to successful `Settle`.

The existing
[artifact/session group-release model](../artifact-session-group-release/README.md)
owns lower-layer exact receipt joins and cleanup ordering. This model neither
imports that entire state machine nor copies its transitions: its boundary is
the Browser's interpretation of a completed scope outcome. No lower-owner
currency is minted, advanced, or joined here. Consequently this is **not**
composition evidence for group receipts, factory-to-workspace transfer, or
artifact cleanup itself.

The failure set is the retained bounded diagnostic record. The model checks
that failed cleanup stays recorded and charged; it does not model exception
aggregation, diagnostic delivery to each caller, or browser rendering.
Archive/download accounting, selection, shared image-budget subdivision,
Platform and multi-package realization, synchronous admission, and precise LRU
ordering are outside the checked claims. `Evict` is a modeled policy choice,
not a separately asserted LRU guarantee.

## Progress assumptions

`Spec` permits arbitrary interleavings and stuttering. `FairSpec` adds weak
fairness for each factory's completion, each eligible retirement settlement,
and each caller's delivery and query release. Success is not assumed: factory
and cleanup failures are both explored. These assumptions abstract terminating
external operations and callers that eventually release use; they do not
prove wall-clock deadlines, browser scheduling, or forced reclamation of a
query that never releases.

Under these assumptions, every retiring entry eventually reaches `Closed` or
`Failed`. There is deliberately no claim that every admission succeeds or that
failed cleanup recovers in place. Exhausting the finite caller/entry supply is
an intentional terminal harness state, so deadlock checking is disabled.

## Gates and adversarial controls

Every configuration is pinned in `eng/tla-expected-exit-codes.txt`.

| Configuration | Expected exit | Evidence |
| --- | --- | --- |
| `Safety.cfg` | 0 | Charged ownership, capacity, exact single-flight, current publication, protected use, irreversible retirement, failure quarantine |
| `SingleSlotSafety.cfg` | 0 | Same properties under one-slot pressure |
| `Liveness.cfg` | 0 | Retirement terminates under the stated fairness |
| `BrokenEarlyUncharge.cfg` | 12 | Returning capacity on removal violates charged ownership |
| `BrokenLatePublish.cfg` | 12 | Publishing a retiring factory result violates current publication |
| `BrokenUnprotectedReturn.cfg` | 12 | Releasing protection at factory completion exposes the return-to-query gap |
| `BrokenForgetFailure.cfg` | 12 | Returning a failed cleanup's reservation violates quarantine |
| `BrokenNeverSettle.cfg` | 13 | Omitting retirement settlement defeats progress despite the same fairness clauses |
| `ReachabilityJoinedCancellation.cfg` | 12 | One joiner cancels and another receives shared success |
| `ReachabilityLateReplacement.cfg` | 12 | An old retiring factory completes after a same-key replacement becomes eligible |
| `ReachabilityCapacityReuse.cfg` | 12 | A later admission reuses capacity after successful cleanup at a one-slot limit |
| `ReachabilityCleanupFailure.cfg` | 12 | Failed cleanup remains recorded and charged |

Reachability configurations negate a required witness. Exit 12 means TLC found
that witness, not a defect in the correct policy. The corresponding complete
safety configurations check the same bounds; negative-control configurations
instead select one explicit broken policy and its targeted property.

## Run

From the repository root, with the
[pinned tools](../../runbooks/tla-plus-setup.md):

```bash
TLA_TOOLS_JAR=/absolute/path/to/tla2tools.jar \
  ./eng/run-tla-checks.sh docs/models/browser-scope-retirement
```

The initial two-slot run explored 284,521 distinct states (1,055,596 generated)
to depth 16. Tool identity: TLC `2026.08.11.125311`, SHA-256
`ab323b79802aedc3203b3f9af37c6aca3ed43f4e0225b36f2aa77b26de46c05f`.
Local Java was OpenJDK 21, satisfying the tool's Java 11 minimum but differing
from the runbook's preferred OpenJDK 25; CI uses the repository-selected JVM.

For an individual counterexample trace:

```bash
java -jar "$TLA_TOOLS_JAR" -cleanup -workers 1 \
  -config docs/models/browser-scope-retirement/BrokenEarlyUncharge.cfg \
  docs/models/browser-scope-retirement/BrowserScopeRetirement.tla
```

The short counterexample is: request, reserve and start construction, then
remove the entry while its factory is still running. The broken policy makes
`charged = FALSE` while `phase = "Retiring"`. The neighboring capacity-reuse
witness uses the correct policy: the old factory settles, terminal cleanup
succeeds, and only then can a new entry take that slot.
