# Package Source operation concurrency

Executable interaction companion to the normative
[Package Source model](../../package-source-model.md#bounded-concurrent-steps).
Motivated by the repeated dependencies in
`Microsoft.Azure.SignalR@1.33.1` / `net8.0`; implementation/adoption tracking is
[#10417](https://github.com/richlander/dotnet-inspect/issues/10417) under
[#10404](https://github.com/richlander/dotnet-inspect/issues/10404).

## Scope

One root can issue one operation before retirement. The issued operation can
admit three finite source steps with capacity one or two. Two steps request the
same authority/runtime/package tuple; the third requests a different package.
Shared fetches settle as success or failure. Each caller aggregates separately
and may decline to retain successful source evidence when its aggregate is
incomplete. The last caller removes unretained evidence so a later step retries.
Operation expiry is monotonic and does not instantly release pending work.

This is a finite abstraction of ordinary source steps, not the existing Package
Version Service bounded-refresh exception. Authorization, source correspondence,
authority/runtime replacement, multiple root generations, range selection,
transport/protocol behavior, and inspected graph fidelity are outside its scope.
The adjacent
[source-composition model](../package-source-composition/README.md) supplies the
association and complete/partial aggregate contracts; this model does not
reimplement those protocols. Scoped tuple identity is an input, not reconstructed
from producer equality. Release gates establish implementation correspondence.

The model assumes source calls eventually settle under weak fairness. Fair
step cleanup, operation release, and root retirement then establish root
quiescence. It does not promise termination for an uncooperative source client.

## Gates

Run with the pinned jar from the
[TLA+ setup runbook](../../../runbooks/tla-plus-setup.md):

```sh
TLA_TOOLS_JAR=/path/to/tla2tools.jar \
  bash eng/run-tla-checks.sh \
  docs/design/models/package-source-operation-concurrency
```

| Configuration | Exact verdict |
| --- | --- |
| `Sequential.cfg` | Exit 0: safety and eventual root settlement at capacity one. |
| `Concurrent.cfg` | Exit 0: the same properties at capacity two, with one producer per shared key. |
| `BrokenAdmission.cfg` | Exit 12: `CapacityIsBounded` fails when admission ignores capacity. |
| `BrokenRelease.cfg` | Exit 12: `ReleaseRequiresSettledWork` fails when active work can be released. |
| `BrokenDuplicateFetch.cfg` | Exit 12: `OneProducerPerKey` fails when a second caller starts duplicate work. |
| `BrokenRestartCeiling.cfg` | Exit 12: `NoLateSuccess` fails when a caller can outlive the shared ceiling. |
| `ParallelReachability.cfg` | Exit 12: `ParallelNotObserved` fails, demonstrating actual overlapping admission. |

The manifest `eng/tla-expected-exit-codes.txt` enforces these exact outcomes.
Negative controls and reachability failures are expected, not product defects.
The local check uses the pinned jar with SHA-256
`ab323b79802aedc3203b3f9af37c6aca3ed43f4e0225b36f2aa77b26de46c05f`
on OpenJDK 21.0.12.1, which satisfies the tool's Java 11-or-later requirement.
