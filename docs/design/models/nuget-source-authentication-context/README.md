# NuGet source authentication-context model

`NuGetSourceAuthenticationContext.tla` is the executable interaction companion
to the source-scoped plugin authentication-context contract in
[NuGet feed authentication](../../nuget-authentication.md).

The model checks that two distinct configurable source authorities may share
one credential-resource scope without sharing plugin credential state. Each
context starts empty. Associated and target-authorized requests consult only
their context, send anonymously while it is empty, and may begin acquisition
only after an authorized 401 challenge. Acquisition is single-flight within a
context and independent across contexts. A successful live completion
publishes to its matching context; a completion overtaken by retirement does
not publish or replay. Later authorized requests may preemptively reuse only
their own context's credential. Retirement also clears an already-populated
idle context before a later request can consult or replay its cached
credential.

The finite request set also includes an unassociated request, an explicitly
plugin-ineligible request, an out-of-scope target, and the built-in Gallery.
Those requests may be sent and challenged, but cannot consult plugin cache
state, acquire, publish, or replay plugin credentials.

## Bounds and non-claims

The checked model has two configurable contexts, one shared resource scope,
one foreign scope, and nine one-shot requests: two concurrent challenge
opportunities and one later request for the first context, one challenge and
one later request for the second context, plus the four excluded request
classes. An acquisition attempt is identified by its leading request. Attempts
have success or failure outcomes, and either context may retire while its
attempt is pending or has an available outcome. To bound the additional
populated-idle case without duplicating a symmetric state space,
`privateSource` is the representative context that may also retire after its
acquisition has completed and its credential is cached. The same
`privateLater` request is explored in separate behaviors both before
retirement, where it reuses the credential, and after retirement, where it
sends anonymously without consulting plugin state.

The model deliberately abstracts credential bytes, plugin discovery and
protocol, HTTP retry counts, 403 opt-in, refresh after a rejected credential,
redirect mechanics, target-scope derivation, cancellation, and implementation
correspondence. `SharedScope` is an input fact, not a source identity:
`privateSource` and `anonymousSource` remain distinct authentication
authorities even though `ResourceScope` maps both to that value.

## Checked properties

| Property | Claim |
| --- | --- |
| `DistinctContextsShareResourceScope` | The bounded scenario explicitly contains two distinct contexts with one resource scope. |
| `ContextCredentialsAreIsolated` and `RetiredContextsHaveNoCredential` | A cached credential belongs to its matching live context; retirement leaves no credential. |
| `PopulatedRetirementIsSound` | A context recorded as retiring while populated and idle is no longer live and has no cached credential. |
| `PostRetirementRequestCannotUsePlugin` | The later request sent after populated retirement reads and replays no credential, starts no acquisition, and cannot become an authorized plugin challenge. |
| `CredentialUseIsAuthorized` and `CacheReadsStayContextBound` | Plugin cache lookup and replay require association, eligibility, target authorization, and the matching context. |
| `AcquisitionStartsAreAuthorized` | Only an associated, eligible, in-scope, non-Gallery 401 challenge starts acquisition. |
| `WaitersStayInTheirContext` | Concurrent challenges join only an attempt for their own context. |
| `PublicationIsAuthorized` | Successful publication occurs only for the matching live context. |
| `AtMostOneAcquisitionPerContext` | No context has more than one pending or outcome-available acquisition. |
| `CrossContextAcquisitionDoesNotBlock` | An active acquisition in the equal-scope peer context does not disable a challenged context's start. |
| `ContextTwoCannotConsumeContextOneCredential` | The second, initially anonymous source never reads or replays the first source's credential. |
| `ExcludedRequestsDoNotParticipate` and `GalleryDoesNotParticipate` | Unassociated, ineligible, out-of-scope, and Gallery requests cannot read, acquire, or replay plugin credentials. |
| `AvailableAcquisitionsEventuallyComplete` | Under weak fairness, every acquisition with an available outcome completes. |
| `AdmittedAuthorizedChallengesEventuallySettle` | Under weak fairness, every admitted authorized challenge settles after its joined acquisition outcome is available. |

The liveness claims do not assume that a server challenges, that the plugin
returns, or that an environment makes an outcome available. They begin only
after the named request or acquisition reaches the modeled admitted/available
state.

## Configurations

| Configuration | Purpose |
| --- | --- |
| `NuGetSourceAuthenticationContext.cfg` | Checks the complete bounded safety and conditional liveness set with context-bound cache selection and live-only publication. |
| `PopulatedRetirementReachability.cfg` | Negates the populated-retirement/later-send witness. It must fail only after a credential is published, its now-idle context retires and clears it, and `privateLater` sends without plugin state. |
| `BrokenCrossContextReuse.cfg` | Replaces context-bound lookup with resource-scope lookup. It must violate `CacheReadsStayContextBound` when the second context reads the first context's credential from their shared scope. |
| `BrokenStalePublication.cfg` | Lets a successful completion publish after its context retires. It must violate `PublicationIsAuthorized`. |

## Running SANY and TLC

Follow the repository
[TLA+ setup runbook](../../../runbooks/tla-plus-setup.md) for the pinned
toolchain. Run configurations sequentially because TLC processes share the
default `states/` path.

```bash
TLA_TOOLS_JAR=/path/to/tla2tools.jar
cd docs/design/models/nuget-source-authentication-context

java -cp "$TLA_TOOLS_JAR" tla2sany.SANY \
  NuGetSourceAuthenticationContext.tla

java -XX:+UseParallelGC -cp "$TLA_TOOLS_JAR" tlc2.TLC \
  -workers auto -seed 1 -fp 1 -cleanup -coverage 1 \
  -config NuGetSourceAuthenticationContext.cfg \
  NuGetSourceAuthenticationContext.tla

for config in PopulatedRetirementReachability BrokenCrossContextReuse \
  BrokenStalePublication; do
  java -XX:+UseParallelGC -cp "$TLA_TOOLS_JAR" tlc2.TLC \
    -workers 1 -seed 1 -fp 1 -cleanup -noGenerateSpecTE \
    -config "$config.cfg" \
    NuGetSourceAuthenticationContext.tla
done
```

The normal configuration must complete without error. The reachability probe
and each mutation must exit unsuccessfully on its named invariant.

## Recorded result

Checked on Linux with OpenJDK `21.0.12` and the repository-pinned TLA+ `v1.8.0`
prerelease (`TLC2 2026.08.21.155922`, rev `9787e65`). The checked
`tla2tools.jar` has SHA-256
`eabd140a70f49eb9305a3bd3f3df944eddf87e5a90d329789085f8953a80533a`.
The runbook prefers Java 25, but it was not installed on this shared host.
Java 21 satisfies the Java 11-or-later tool requirement, and no installation
was performed.

SANY parsed the module without error. The complete normal graph used automatic
parallel workers; the reachability and mutation probes used one worker. Every
run used seed `1`, fingerprint seed `1`, breadth-first search, and the exact
two-context, nine-request bounds described above.

| Configuration | Result | Generated states | Distinct states | Maximum depth |
| --- | --- | ---: | ---: | ---: |
| `NuGetSourceAuthenticationContext.cfg` | No error | 3,908,973 | 850,544 | 28 |
| `PopulatedRetirementReachability.cfg` | `PopulatedRetirementAndLaterSendNotObserved` violated | 845 | 413 | 8 |
| `BrokenCrossContextReuse.cfg` | `CacheReadsStayContextBound` violated | 390 | 200 | 7 |
| `BrokenStalePublication.cfg` | `PublicationIsAuthorized` violated | 389 | 199 | 7 |

The normal graph reached a state with acquisitions active in both distinct
contexts while both map to `SharedScope`; the
`independentFlightWitness` state bit changed from false to true. The
`retiredPopulated` set became non-empty and
`postRetirementSendWitness` changed from false to true, making the new safety
properties non-vacuous in the complete graph. It also explored success,
failure, same-context joining, successful replay, anonymous completion without
a challenge, excluded challenges, and retirement before an outcome completed.
No unexpected counterexample was found.

The populated-retirement reachability trace sends and challenges
`privateFirst`, publishes its successful credential, retires the now-idle
`privateSource`, and then sends `privateLater`. The retirement clears the
credential and the later request is `SentAnonymous` with `noContext` cache
lookup, `noCredential` use, no joined attempt, and no acquisition. The normal
configuration checks those facts with
`PostRetirementRequestCannotUsePlugin`; a later challenge remains ineligible
because the context is no longer live.

The cross-context mutation trace sends and challenges `privateFirst`, starts
and successfully completes its acquisition, then sends `anonymousFirst`.
Resource-scoped lookup reads `privateSource`'s credential for that distinct
source context, violating `CacheReadsStayContextBound`.

The stale-publication mutation trace sends and challenges `privateFirst`,
makes its successful outcome available, retires `privateSource`, and then
executes the broken completion. Publishing into the retired context violates
`PublicationIsAuthorized`.

These mutations are negative controls, not implementation defects. The model
checks the design interaction under the recorded assumptions and finite
bounds; it does not establish implementation correspondence.
