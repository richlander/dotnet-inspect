# Package source composition model

This TLA+ model is the executable interaction companion to the
[package source model](../../package-source-model.md). It checks the
package-owner interactions among two already-classified configured
authorities, three runtime routes, source-result association, complete versus
partial discovery, pinned payload success, request timeout fallback, and one
shared operation ceiling.

## Model shape

`AuthorityOne` has a primary and fallback route. `AuthorityTwo` has one peer
route. All three routes deliberately have the same abstract producer identity,
so their owner-issued association is the only permitted authority-recovery
fact. The routes can run concurrently; the fallback can start only after the
primary route reports a request timeout or transport failure.

Every found, authoritatively absent, request-timeout, or transport-failure
result carries a returned authority into package adoption. Each authority then
settles as found, authoritatively absent, or failed. Discovery becomes complete
only after both authorities provide authoritative evidence, becomes partial
when usable evidence survives a source failure, and otherwise fails. A pinned
payload can publish from either authorized authority without waiting for a
readable peer.

Operation timeout is an exogenous event. Under the specified policy it
immediately publishes a terminal failure and cancels running routes. Request
timeout is a route outcome and can expose the fallback while the operation
remains live.

Weak fairness applies to route start, route settlement, pinned publication, and
aggregate finalization. It does not force operation timeout. The liveness claim
therefore says that every modeled operation eventually publishes a complete,
partial, failed, or payload outcome whether or not the environment expires it.

## Assumptions and non-claims

The model assumes:

- exactly two already-active and package-ID-eligible configured authorities;
- one owner-issued association per authority;
- one primary/fallback route pair and one independent peer route;
- source operations eventually choose one modeled result under weak fairness;
- source results and caller cancellation are already typed; and
- a found pinned payload is valid for the requested exact coordinate.

Classification, URL and path canonicalization, package-source mapping,
authentication context internals, protocol discovery, retry mechanics,
credential-safe display, candidate contents, semantic-version selection,
persistent cache keys, payload bytes, stream lifetime, and implementation
correspondence are outside the model. The Release gates named in the owning
design establish those implementation boundaries.

TLC results establish properties of this finite state machine under these
assumptions and bounds, not properties of the shipped implementation.

## Checked properties

| Property | Claim |
| --- | --- |
| `AdoptedResultsKeepAssociation` | Same-producer equality cannot move a result between configured authorities. |
| `AbsentResultsKeepAssociation` | The authoritative-absence path specifically preserves exact association. |
| `TerminalFailureResultsKeepAssociation` | Final request-timeout and transport-failure paths specifically preserve exact association. |
| `CompleteRequiresEveryAuthority` | A complete aggregate contains authoritative evidence from every required authority. |
| `CompleteAbsenceIsAuthoritative` | Package-wide absence cannot be published while an authority is pending or failed. |
| `TerminalFailuresRemainVisible` | A final request timeout or transport failure does not become source absence. |
| `PartialResultsAreExplicitlyIncomplete` | A partial result retains both usable evidence and a failed authority. |
| `OperationTimeoutIsTerminal` | Once the shared operation expires, the only package outcome is failed. |
| `PayloadKeepsReportingAuthority` | A pinned payload remains associated with the route's configured authority. |
| `PayloadPublishesBeforeOperationTimeout` | Expired work cannot publish a successful payload. |
| `AggregateSettles` | Under weak fairness, every modeled operation reaches a terminal package outcome. |

## Configurations

| Configuration | Purpose |
| --- | --- |
| `PackageSourceCompositionDiscovery.cfg` | Complete discovery safety and liveness under exact association, all-authority completeness, visible failures, and terminal operation timeout. |
| `PackageSourceCompositionPinned.cfg` | Pinned-payload safety and liveness under the same policy. |
| `BrokenHealthySubsetComplete.cfg` | Lets a healthy subset publish complete and must violate `CompleteRequiresEveryAuthority`. |
| `BrokenProducerAssociation.cfg` | Lets same-producer equality replace exact association for any result and must violate `AdoptedResultsKeepAssociation`. |
| `BrokenProducerAbsenceAssociation.cfg` | Checks the producer-collapse mutation specifically on authoritative absence and must violate `AbsentResultsKeepAssociation`. |
| `BrokenProducerTerminalFailureAssociation.cfg` | Checks the producer-collapse mutation specifically on final request timeout or transport failure and must violate `TerminalFailureResultsKeepAssociation`. |
| `BrokenRestartedOperationCeiling.cfg` | Lets an expired operation continue and must violate `OperationTimeoutIsTerminal`. |
| `BrokenFailureAsAbsence.cfg` | Converts a final route failure into absence and must violate `TerminalFailuresRemainVisible`. |
| `PartialDiscoveryReachability.cfg` | Must reach usable discovery evidence plus a failed authority and violate `PartialAfterSourceFailureNotObserved`. |
| `RequestTimeoutFallbackReachability.cfg` | Must reach primary request timeout followed by fallback success and violate `RequestTimeoutFallbackNotObserved`. |
| `PinnedPeerFailureReachability.cfg` | Must reach a payload from one authority after the peer fails and violate `PinnedSuccessWithPeerFailureNotObserved`. |
| `OperationTimeoutReachability.cfg` | Must reach shared operation expiry and violate `OperationTimeoutNotObserved`. |

The positive configurations must finish without error. Every broken-policy and
reachability configuration must fail on its named property.

## Running SANY and TLC

Follow the repository
[TLA+ setup runbook](../../../runbooks/tla-plus-setup.md) for the pinned
toolchain. Run configurations sequentially because TLC processes share the
default `states/` path.

```bash
TLA_TOOLS_JAR=/path/to/tla2tools.jar
cd docs/design/models/package-source-composition

java -cp "$TLA_TOOLS_JAR" tla2sany.SANY \
  PackageSourceComposition.tla

for config in PackageSourceCompositionDiscovery \
  PackageSourceCompositionPinned BrokenHealthySubsetComplete \
  BrokenProducerAssociation BrokenProducerAbsenceAssociation \
  BrokenProducerTerminalFailureAssociation \
  BrokenRestartedOperationCeiling BrokenFailureAsAbsence \
  PartialDiscoveryReachability \
  RequestTimeoutFallbackReachability PinnedPeerFailureReachability \
  OperationTimeoutReachability; do
  java -XX:+UseParallelGC -cp "$TLA_TOOLS_JAR" tlc2.TLC \
    -workers auto -seed 1 -fp 1 -cleanup -noGenerateSpecTE \
    -config "$config.cfg" PackageSourceComposition.tla
done
```

## Recorded results

Checked on Linux with OpenJDK `21.0.12` and the repository-pinned TLA+ `v1.8.0`
prerelease (`TLC2 2026.08.21.155922`, rev `9787e65`). The checked
`tla2tools.jar` has SHA-256
`eabd140a70f49eb9305a3bd3f3df944eddf87e5a90d329789085f8953a80533a`.
The runbook prefers Java 25, but it was not installed on this shared host.
Java 21 satisfies the Java 11-or-later requirement; no installation was
performed.

SANY parsed the module without error. All TLC runs used breadth-first search,
seed `1`, fingerprint seed `1`, and automatic parallel workers.
Counts for configurations that stop at a counterexample record this run and
may vary with worker scheduling; the named violation is the required result.

| Configuration | Result | Generated | Distinct | Depth |
| --- | --- | ---: | ---: | ---: |
| `PackageSourceCompositionDiscovery.cfg` | No error | 307 | 232 | 8 |
| `PackageSourceCompositionPinned.cfg` | No error | 322 | 247 | 8 |
| `BrokenHealthySubsetComplete.cfg` | `CompleteRequiresEveryAuthority` violated | 257 | 200 | 8 |
| `BrokenProducerAssociation.cfg` | `AdoptedResultsKeepAssociation` violated | 103 | 64 | 7 |
| `BrokenProducerAbsenceAssociation.cfg` | `AbsentResultsKeepAssociation` violated | 178 | 122 | 8 |
| `BrokenProducerTerminalFailureAssociation.cfg` | `TerminalFailureResultsKeepAssociation` violated | 249 | 172 | 8 |
| `BrokenRestartedOperationCeiling.cfg` | `OperationTimeoutIsTerminal` violated | 197 | 96 | 7 |
| `BrokenFailureAsAbsence.cfg` | `TerminalFailuresRemainVisible` violated | 137 | 108 | 8 |
| `PartialDiscoveryReachability.cfg` | `PartialAfterSourceFailureNotObserved` violated | 307 | 232 | 8 |
| `RequestTimeoutFallbackReachability.cfg` | `RequestTimeoutFallbackNotObserved` violated | 219 | 168 | 8 |
| `PinnedPeerFailureReachability.cfg` | `PinnedSuccessWithPeerFailureNotObserved` violated | 322 | 247 | 8 |
| `OperationTimeoutReachability.cfg` | `OperationTimeoutNotObserved` violated | 108 | 92 | 7 |

The discovery and pinned graphs completed without an invariant or liveness
violation. The reachability traces exhibit explicit partial discovery, request
timeout followed by same-authority fallback, pinned success after a peer
authority fails, and terminal shared-ceiling expiry. The six mutations
independently show the unsafe healthy-subset, found-result producer collapse,
authoritative-absence producer collapse, terminal-failure producer collapse,
deadline-restart, and failure-as-absence policies.

The positive discovery configuration also provides non-vacuity for both
category-specific association properties. Removing only the
authoritative-absence adoption write makes it violate
`AbsentResultsKeepAssociation`; removing only the request-timeout and
transport-failure adoption writes makes it violate
`TerminalFailureResultsKeepAssociation`.

These intentional violations are negative controls and reachability evidence,
not product defects. No unexpected counterexample was found.
