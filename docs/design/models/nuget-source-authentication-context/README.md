# NuGet source authentication-context models

These TLA+ models are executable interaction companions to the source-scoped
plugin authentication-context contract in
[NuGet feed authentication](../../nuget-authentication.md).

- `NuGetSourceAuthenticationContext.tla` checks request-to-context binding,
  target authorization, context isolation, retirement, acquisition,
  publication, and replay.
- `NuGetSourceAuthenticationRefresh.tla` checks one bounded refresh episode
  after the server rejects a cached credential version.

The models check design interactions under finite bounds. They do not establish
implementation correspondence.

## Source-context model

### Source-context bounds and assumptions

The context model has:

- two distinct configurable source contexts, `privateSource` and
  `anonymousSource`;
- one shared credential-resource scope plus one foreign scope;
- nine one-shot requests: two challenge opportunities and one later request
  for the first context, one challenge and one later request for the second
  context, and unassociated, explicitly ineligible, out-of-scope, and Gallery
  requests;
- at most one request-led provider attempt active in each context; and
- success or failure outcomes, with outcome availability controlled by the
  unfair environment.

Both contexts map to `SharedScope`; resource scope is deliberately not context
identity. `ContextTwoSendMode` is `Unrestricted` in the complete graph so both
contexts can acquire concurrently. The focused source-isolation and
cross-context-reuse probes use `AfterContextOneCredential` to require the
second source's request to occur after the first source publishes.

`RequestContext` is an input mapping from each request to its already-created
authentication context. The model does not construct contexts or check that
several pipelines carrying one `PackageSourceAssociation` resolve to the same
context. The design names
`SharedAssociationPipelinesShareAuthenticationContext` as the required
implementation gate for that mapping.

`ResourceScope` and `TargetScope` are also input facts; this model does not
derive them from URIs. `OrdinaryResourceScopeUsesCanonicalOrigin` and the Azure
scope gate named by the design provide implementation correspondence for that
derivation. Provider-query identity is absent;
`ResourceFirstChallengeUsesConfiguredProviderQuery` establishes that a
resource-first challenge queries the plugin with the configured service-index
URI rather than the resource target. Pipeline creation and disposal are absent.
`RetireContext` denotes configured-authority retirement or replacement, never
disposal of an individual pipeline;
`SharedContextSurvivesIndividualPipelineDisposal` is the required
implementation gate for that lifetime boundary.

`ParticipationMode = "LiveOnly"` is the specified policy.
`"AllowRetired"` is a negative-control policy equivalent to removing the live
context gate. `CredentialSelectionMode` independently selects context-bound or
broken resource-scoped cache lookup. `PublicationMode` independently selects
live-only or broken stale publication.

Retirement is exogenous. The configured authority can be replaced at any time,
so `RetireContext(c)` is enabled for any live context in any state and is not
conditioned on that context having a credential, a challenged request, or
active provider work. Retirement clears the credential. Three
security-relevant retirement phases are therefore ordinary reachable states
rather than modeling preconditions, and each has its own reachability
configuration:

1. after an authorized challenge but before acquisition starts;
2. while an acquisition is pending or has an available outcome; and
3. after `privateSource`, used as the symmetric populated-idle representative,
   has cached a credential.

A challenge admitted while the context was live remains historical evidence and
is not reclassified as unauthorized when the context later retires. Event-time
witnesses record whether a cache read, challenge authorization, acquisition
start, acquisition join, credential consumption, or publication occurs while
the owning context is already retired.

The later-request witness latches on the send event itself once
`privateSource` has retired while populated, whatever that send selected. The
obligation that the send read no plugin state is expressed by
`PostRetirementRequestCannotUsePlugin` rather than built into the witness, so
the witness cannot hide an unsafe selection.

### Source-context checked properties

| Property | Claim |
| --- | --- |
| `ContextCredentialsAreIsolated`, `CredentialUseIsAuthorized`, `CacheReadsStayContextBound`, and `ContextTwoCannotConsumeContextOneCredential` | Equal resource scope cannot collapse context authority, lookup, or replay. |
| `AcquisitionStartsAreAuthorized` and `WaitersStayInTheirContext` | Only associated, eligible, in-scope, non-Gallery challenges acquire, and waiters join only their context. |
| `AtMostOneAcquisitionPerContext` and `CrossContextAcquisitionDoesNotBlock` | Acquisition is single-flight within a context and independent across contexts. |
| `RetiredContextsHaveNoCredential` and `PopulatedRetirementIsSound` | Retirement clears cached state. |
| `PostRetirementRequestCannotUsePlugin` | A request sent after a populated context retired reads no cache, uses no credential, is not admitted as an authorized challenge, and starts or joins no provider work. |
| `NoRetiredCacheRead` | A cache-read event cannot be authorized by a retired context. |
| `NoRetiredChallengeAuthorization` | A 401 challenge cannot be admitted after its context retires. |
| `NoRetiredAcquisitionStart` and `NoRetiredAcquisitionJoin` | A retired context cannot start or join provider work. |
| `NoRetiredCredentialUse` | A retired context cannot consume or replay a plugin credential. |
| `NoRetiredPublication` and `PublicationIsAuthorized` | A completion cannot publish into a retired context. |
| `ExcludedRequestsDoNotParticipate` and `GalleryDoesNotParticipate` | Unassociated, ineligible, out-of-scope, and built-in Gallery requests cannot read, acquire, or replay plugin credentials. |
| `AvailableAcquisitionsEventuallyComplete` | Under weak fairness, each acquisition completes after an outcome is available. |
| `AdmittedAuthorizedChallengesEventuallySettle` | Under weak fairness, each admitted waiter settles after its joined outcome is available. |

The six retired-event properties use latching event witnesses. They therefore
reject only authority exercised while retired; they do not reject a request,
challenge, or acquisition merely because a later step retires its context. The
committed evidence shows that `AllowRetired` reaches all six categories in one
behavior; it does not establish that the six witnesses are separately
falsifiable, and no claim of that kind is made here.

## Refresh model

### Refresh bounds and assumptions

The refresh model has one already-authorized live context, two requests, an
initial cached credential version `1`, and distinct possible provider results
`2` and `3`.

The model bounds itself to **one refresh episode**: the version the server
rejects is the initial version. Within that bound the interleaving is
unconstrained.

- Either request may attach the cached version before, during, or after the
  provider flight; sending is not gated on the other request's progress.
- The rejection of an observed initial version may arrive before or after the
  flight publishes the newer version.
- A request whose observation the episode has already superseded consumes the
  newer cached version and cannot start or join provider work.
- A request that is still at the episode's version when it rejects, and finds
  a flight running, joins that flight, later rechecks, and consumes the
  published version.
- A request that first observes a version this episode published is outside
  the episode: the server accepts it and it owes no refresh. Whether such a
  request is later rejected is a further episode and is deliberately not
  modeled.

`Done` means the request consumed the current version. Provider outcomes are
successful; provider failure, cancellation, source retirement, HTTP retry
bounds, and plugin protocol are owned elsewhere.

Four independent modes carry the negative controls, and the normal
configuration sets all four to the specified policy:

| Constant | Specified value | Negative-control value |
| --- | --- | --- |
| `RefreshMode` | `"SingleFlight"` | `"DuplicateAcquisition"` — a follower starts its own provider work instead of joining. |
| `AcquisitionMode` | `"CurrentVersionOnly"` | `"StaleMayAcquire"` — a request may enter provider work carrying a superseded observation. |
| `ConsumptionMode` | `"ReadOnly"` | `"ConsumeWritesObservedVersion"` — consuming the newer version writes the consumer's older observation back over the cache. |
| `PublicationMode` | `"MonotonicPublish"` | `"ClobberPublish"` — a completion publishes its own candidate verbatim. |

### Refresh checked properties

| Property | Claim |
| --- | --- |
| `AtMostOneProviderAcquisition` | At most one same-context provider refresh is pending or outcome-available. |
| `AtMostOneProviderCompletion` | One episode consults the provider once, because a follower joins the running flight and a superseded request consumes instead of starting a second flight. |
| `WaitingRequestsShareActiveAcquisition` | Every waiter is attached to the one active attempt. |
| `FollowersDoNotRunTheirOwnProviderWork` | A request that reached the refresh decision while a flight was running owns no attempt and is served by an attempt another request started. |
| `DoneRequestsConsumedCurrentVersion` | A terminal request has observed the current published version. |
| `StaleObservedRequestCannotAcquire` | No transition into provider work carries an observation older than the cached version, whether that transition would start or join the work. |
| `StaleObservedConsumptionIsReadOnly` | A transition in which a superseded request becomes terminal leaves the cached version unchanged. |
| `CredentialVersionNeverRegresses` | No transition lowers the cached version. |
| `AvailableRefreshEventuallyCompletes` | Under weak fairness, an outcome-available refresh completes. |
| `StaleObservedRequestsEventuallyConsume` | Under weak fairness, a superseded rejected request or joined waiter consumes the newer version. |

`StaleObservedRequestCannotAcquire`,
`StaleObservedConsumptionIsReadOnly`, and
`CredentialVersionNeverRegresses` are action properties stated over a
transition rather than a latched witness. Each has a dedicated mutation mode
and configuration that falsifies it. `AvailableRefreshEventuallyCompletes`
and `StaleObservedRequestsEventuallyConsume` are conditional leads-to
properties checked under weak fairness by the normal configuration; no
dedicated mutation configuration is claimed for either.

## Configurations

### Source-context configurations

| Configuration | Purpose |
| --- | --- |
| `NuGetSourceAuthenticationContext.cfg` | Complete bounded safety and conditional liveness with live-only participation, context-bound lookup, and live-only publication. |
| `ChallengedRetirementReachability.cfg` | Requires the live challenge then pre-acquisition retirement phase. |
| `ActiveRetirementReachability.cfg` | Requires retirement with active provider work. |
| `PopulatedRetirementReachability.cfg` | Requires populated-idle retirement, clearing, and a later send through the retired context. |
| `IndependentFlightsReachability.cfg` | Requires simultaneous active acquisitions in the two equal-scope contexts. |
| `SourceIsolationReachability.cfg` | Requires the second context to send anonymously after the first context publishes. |
| `ExcludedAndGalleryReachability.cfg` | Requires all four excluded request classes to receive challenges without plugin participation. |
| `BrokenRetiredParticipation.cfg` | Allows retired participation and must violate `AllRetiredParticipationViolationsNotObserved` after reaching every retired event category. |
| `BrokenRetiredLaterRequest.cfg` | Allows retired participation and must violate `PostRetirementRequestCannotUsePlugin` on the later send. |
| `BrokenCrossContextReuse.cfg` | Uses resource-scoped lookup and must violate `CacheReadsStayContextBound`. |
| `BrokenStalePublication.cfg` | Publishes a retired completion and must violate `PublicationIsAuthorized`. |

### Refresh configurations

| Configuration | Purpose |
| --- | --- |
| `NuGetSourceAuthenticationRefresh.cfg` | Checks the bounded single-flight refresh episode and conditional liveness under the specified value of all four modes. |
| `JoinedFlightReachability.cfg` | Requires a same-version rejected request to join the running flight and then consume the published version. |
| `LateRejectionReachability.cfg` | Requires the rejection of an observed version to arrive after the flight already published a newer one. |
| `PostRefreshAcceptReachability.cfg` | Requires a request that first observes the published version to be accepted without a rejection. |
| `BrokenDuplicateRefreshAcquisition.cfg` | Removes single-flight admission and must violate `AtMostOneProviderAcquisition`. |
| `BrokenDuplicateRefreshCompletion.cfg` | Removes single-flight admission and, checking only the completion claim, must violate `AtMostOneProviderCompletion`. |
| `BrokenStaleRefreshAcquisition.cfg` | Admits superseded requests to provider work and must violate `StaleObservedRequestCannotAcquire`. |
| `BrokenConsumptionWritesBack.cfg` | Lets consumption write back the consumer's observation and must violate `StaleObservedConsumptionIsReadOnly`. |
| `BrokenRegressingRefreshPublication.cfg` | Combines duplicate flights with verbatim publication and must violate `CredentialVersionNeverRegresses`. |

## Running SANY and TLC

Follow the repository
[TLA+ setup runbook](../../../runbooks/tla-plus-setup.md) for the pinned
toolchain. Run configurations sequentially because TLC processes share the
default `states/` path.

```bash
TLA_TOOLS_JAR=/path/to/tla2tools.jar
cd docs/design/models/nuget-source-authentication-context

java -cp "$TLA_TOOLS_JAR" tla2sany.SANY \
  NuGetSourceAuthenticationContext.tla \
  NuGetSourceAuthenticationRefresh.tla

java -XX:+UseParallelGC -cp "$TLA_TOOLS_JAR" tlc2.TLC \
  -workers auto -seed 1 -fp 1 -cleanup \
  -config NuGetSourceAuthenticationContext.cfg \
  NuGetSourceAuthenticationContext.tla

java -XX:+UseParallelGC -cp "$TLA_TOOLS_JAR" tlc2.TLC \
  -workers 1 -seed 1 -fp 1 -cleanup \
  -config NuGetSourceAuthenticationRefresh.cfg \
  NuGetSourceAuthenticationRefresh.tla

for config in ChallengedRetirementReachability \
  ActiveRetirementReachability PopulatedRetirementReachability \
  IndependentFlightsReachability SourceIsolationReachability \
  ExcludedAndGalleryReachability BrokenRetiredParticipation \
  BrokenRetiredLaterRequest BrokenCrossContextReuse \
  BrokenStalePublication; do
  java -XX:+UseParallelGC -cp "$TLA_TOOLS_JAR" tlc2.TLC \
    -workers 1 -seed 1 -fp 1 -cleanup -noGenerateSpecTE \
    -config "$config.cfg" \
    NuGetSourceAuthenticationContext.tla
done

for config in JoinedFlightReachability LateRejectionReachability \
  PostRefreshAcceptReachability BrokenDuplicateRefreshAcquisition \
  BrokenDuplicateRefreshCompletion BrokenStaleRefreshAcquisition \
  BrokenConsumptionWritesBack BrokenRegressingRefreshPublication; do
  java -XX:+UseParallelGC -cp "$TLA_TOOLS_JAR" tlc2.TLC \
    -workers 1 -seed 1 -fp 1 -cleanup -noGenerateSpecTE \
    -config "$config.cfg" \
    NuGetSourceAuthenticationRefresh.tla
done
```

Both normal configurations must complete without error. Every reachability and
mutation configuration must exit unsuccessfully on its own named property and
on no other property.

## Recorded results

Checked on Linux with OpenJDK `21.0.12` and the repository-pinned TLA+ `v1.8.0`
prerelease (`TLC2 2026.08.21.155922`, rev `9787e65`). The checked
`tla2tools.jar` has SHA-256
`eabd140a70f49eb9305a3bd3f3df944eddf87e5a90d329789085f8953a80533a`.
The runbook prefers Java 25, but it was not installed on this shared host.
Java 21 satisfies the Java 11-or-later tool requirement; no installation was
performed.

SANY parsed both modules without error. All TLC runs used breadth-first search,
seed `1`, and fingerprint seed `1`. The complete context graph used automatic
parallel workers; every other run used one worker. For a run that stops on a
counterexample, the state counts are what the search had explored when it
stopped and the depth is the search depth reached, not a complete graph.

### Source-context results

| Configuration | Result | Generated | Distinct | Depth |
| --- | --- | ---: | ---: | ---: |
| `NuGetSourceAuthenticationContext.cfg` | No error | 6,794,613 | 1,485,245 | 29 |
| `ChallengedRetirementReachability.cfg` | `ChallengedRetirementNotObserved` violated | 50 | 40 | 4 |
| `ActiveRetirementReachability.cfg` | `ActiveRetirementNotObserved` violated | 209 | 128 | 5 |
| `PopulatedRetirementReachability.cfg` | `PopulatedRetirementAndLaterSendNotObserved` violated | 3,144 | 1,391 | 8 |
| `IndependentFlightsReachability.cfg` | `IndependentFlightsNotObserved` violated | 1,327 | 635 | 7 |
| `SourceIsolationReachability.cfg` | `SourceIsolationNotObserved` violated | 439 | 240 | 7 |
| `ExcludedAndGalleryReachability.cfg` | `ExcludedAndGalleryNonParticipationNotObserved` violated | 243,288 | 76,051 | 14 |
| `BrokenRetiredParticipation.cfg` | `AllRetiredParticipationViolationsNotObserved` violated | 18,795 | 7,463 | 10 |
| `BrokenRetiredLaterRequest.cfg` | `PostRetirementRequestCannotUsePlugin` violated | 4,023 | 1,710 | 8 |
| `BrokenCrossContextReuse.cfg` | `CacheReadsStayContextBound` violated | 439 | 240 | 7 |
| `BrokenStalePublication.cfg` | `PublicationIsAuthorized` violated | 1,505 | 713 | 7 |

The complete graph carries exogenous retirement and reaches every named
source-context witness. Most reachability configurations differ only in which
invariant they check. `SourceIsolationReachability.cfg` additionally restricts
the second context's first send until after the first context publishes, which
selects a subset of the complete graph. Each retired-event violation witness
remained false throughout the complete graph.

The retirement reachability traces respectively:

- send and authorize `privateFirst`, then retire before acquisition;
- send, authorize, and start `privateFirst`, then retire while active; and
- successfully publish `privateFirst`, retire the populated idle context, then
  send `privateLater` through it.

The independent-flight trace starts one acquisition in each equal-scope
context. The source-isolation trace publishes `privateSource` first, then sends
`anonymousFirst` with its own empty cache slot. The excluded/Gallery trace
publishes the private credential, then sends and challenges all four excluded
request classes without cache reads, credential use, or acquisition.

The retired-participation mutation authorizes `privateFirst` while live,
retires its context before acquisition, then sends and authorizes
`privateConcurrent`. It starts `privateFirst` against the retired context,
joins `privateConcurrent`, and completes successfully. The final state has all
six violation witnesses true: retired cache read, challenge authorization,
acquisition start, acquisition join, credential use, and publication. This is
event-time evidence; the earlier legitimate challenge remains authorized
history.

The retired-later-request mutation publishes `privateSource`, retires that
populated idle context, and then sends `privateLater`. Because
`AllowRetired` removes the live gate, that send selects `privateSource` as its
cache-read context, which is exactly what
`PostRetirementRequestCannotUsePlugin` forbids. Under the specified
`LiveOnly` policy the same send reaches the same witness and selects nothing,
which is why the property is checked and not assumed.

The cross-context mutation publishes `privateSource` and then lets
`anonymousFirst` read that credential through shared resource scope. The stale
publication mutation retires an outcome-available acquisition and publishes
its completion into the retired context.

### Refresh results

| Configuration | Result | Generated | Distinct | Depth |
| --- | --- | ---: | ---: | ---: |
| `NuGetSourceAuthenticationRefresh.cfg` | No error | 91 | 65 | 11 |
| `JoinedFlightReachability.cfg` | `JoinAndFollowerConsumptionNotObserved` violated | 77 | 57 | 10 |
| `LateRejectionReachability.cfg` | `LateRejectionNotObserved` violated | 50 | 36 | 8 |
| `PostRefreshAcceptReachability.cfg` | `PostRefreshAcceptNotObserved` violated | 54 | 40 | 8 |
| `BrokenDuplicateRefreshAcquisition.cfg` | `AtMostOneProviderAcquisition` violated | 32 | 22 | 7 |
| `BrokenDuplicateRefreshCompletion.cfg` | `AtMostOneProviderCompletion` violated | 105 | 75 | 11 |
| `BrokenStaleRefreshAcquisition.cfg` | `StaleObservedRequestCannotAcquire` violated | 72 | 46 | 9 |
| `BrokenConsumptionWritesBack.cfg` | `StaleObservedConsumptionIsReadOnly` violated | 41 | 29 | 7 |
| `BrokenRegressingRefreshPublication.cfg` | `CredentialVersionNeverRegresses` violated | 107 | 77 | 11 |

The three action-property violations are reported by TLC as action-property
violations naming the same property the configuration lists.

The reachability traces are:

- **Join and follower consumption.** Both requests attach version `1`,
  `requestOne` rejects and starts the flight, `requestTwo` rejects and joins
  it, the flight publishes version `2`, and the follower consumes it.
- **Late rejection.** `requestOne` starts and completes the flight while
  `requestTwo` is still waiting on its version `1` response; `requestTwo`'s
  rejection then arrives against a version the cache has already replaced.
- **Post-refresh accept.** `requestTwo` sends only after the flight published
  version `2`, observes that version, and is accepted. It is outside the
  episode and owes no refresh.

The mutation traces are:

- **Duplicate acquisition.** Both requests reject version `1`; `requestOne`
  starts the flight and `requestTwo` starts a second one instead of joining.
  TLC stops at the second start.
- **Duplicate completion.** The same duplicate start is allowed to run to
  completion: both flights publish, giving two provider completions for one
  episode. This is the behavior the per-episode completion claim excludes, and
  it is reachable exactly when single-flight admission is removed.
- **Stale acquisition.** `requestOne` completes the flight and publishes
  version `2` while `requestTwo` is still rejected at version `1`;
  `requestTwo` then starts provider work carrying that superseded
  observation.
- **Consumption write-back.** `requestOne` publishes version `2`, then
  consumes it and writes its own observed version `1` back over the cache.
- **Regressing publication.** Two overlapping flights complete out of order:
  the version `3` flight publishes first and the version `2` flight then
  lowers the cached version to `2`.

These reachability failures and mutations are intentional negative controls,
not product defects. No unexpected counterexample was found in either normal
configuration.
