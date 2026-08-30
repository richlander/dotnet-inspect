# Workspace Definitions complete restoration model

This directory model-checks the complete-restoration coordinator defined by
[`workspace-definitions.md`](../../workspace-definitions.md#complete-restoration).
It supplements that readable specification; it does not define packet bytes,
query payloads, Navigation behavior, or browser-history effects and does not
prove an implementation.

## Scope

`CompleteRestoration.tla` models two canonical restoration requests and three
abstract required participants. The checked participant names are `workspace`,
`navigation`, and `query`, but the model gives them identical mechanics: each
independently returns exact-ready, replacement-ready, or failed.

One request captures its payload and the complete prior installed snapshot.
The model's coordinator token abstracts the one intent token issued by the
retained Navigation session; it is not a second authority source. Every
participant prepares privately against that request. Once all are ready, the
coordinator builds an exact or replacement candidate and either canonicalizes
it or marks it non-projectable. Only a current canonical candidate can commit.
Commit changes every installed participant token, the revision, request
correlation, and exact/replacement relation in one transition.

The model also covers:

- a participant failing before or after peers become ready;
- a second request superseding the first at any preparation phase;
- abort retaining the complete prior snapshot and revision;
- superseded work being discarded without a consumer result;
- late completion after commit, abort, or discard;
- exact versus replacement result classification; and
- finite progress from every started attempt to commit, abort, or discard.

Participant internals, coordinate acquisition, structural-subject resolution,
Registry availability, query payload validation, and effect-authority
consumption are abstract. Those semantics belong to their adjacent owners and
to the implementation gates in the owning document.

## Assumptions

- The retained Navigation session issues monotonically increasing bounded
  intent tokens, and the coordinator uses each exact token as its attempt ID.
- Every attempt requires all three abstract participants. The implementation
  derives a finite exact participant set from the complete committed view; the
  three-participant instance exercises the coordination mechanism without
  claiming an arbitrary-cardinality proof.
- Participant preparation and candidate canonicalization eventually resolve
  when continuously enabled. New request arrival is deliberately unfair and
  bounded, so an attempt can settle once requests stop arriving.
- A replacement-ready participant supplies an owner-issued complete fragment.
  The coordinator does not synthesize a replacement from failure.
- Prepared fragments have no installed or consumer-visible effect. Cache
  population beneath a participant is outside the modeled state.
- A stale completion can arrive once per settled attempt. Which participant
  produced it is immaterial to the stale-token rule.

## Checked properties

| Design property | Model property |
| --- | --- |
| State remains within the declared shape | `TypeOK` |
| Commit requires the current token, every participant ready, and a canonical candidate | `CommitRequiresEveryParticipantAndCanonicalCandidate` |
| A committed snapshot is derived from the exact retained request | `CommittedSnapshotCorrelatesExactRequest` |
| Exact versus replacement relation matches prepared participant evidence | `CommitRelationMatchesPreparedCandidate` |
| Every installed participant fragment changes in one commit | `InstalledSnapshotIsAtomic` |
| Every returned installed snapshot is internally complete | `EveryPublishedSnapshotIsAtomic` |
| A failed attempt never commits | `FailedAttemptNeverCommits` |
| A superseded attempt never commits | `SupersededAttemptNeverCommits` |
| A non-projectable candidate never commits | `UnprojectableCandidateNeverCommits` |
| Preparation publishes no partial result | `PreparationIsInvisibleUntilCommit` |
| Abort retains the exact prior snapshot and revision | `AbortRetainsInstalledSnapshotAndRevision` |
| Completion after settlement cannot install | `StaleCompletionCannotInstall` |
| Every started attempt settles | `EveryAttemptSettles` |
| Every failed attempt settles as abort or discard | `EveryFailedAttemptSettlesWithoutCommit` |

The request payload and result are independently retained. Result correlation
therefore does not trust the current intent token, mutable preparation state,
or the commit action's guard. The exact/replacement relation likewise derives
from every prepared participant's retained `changed` evidence.

## Running TLC

Follow the repository
[TLA+ setup runbook](../../../runbooks/tla-plus-setup.md). The recorded run
used OpenJDK 21.0.12 and TLA+ tools 1.8.0 (`TLC2
2026.08.21.155922`, revision `9787e65`) with one worker:

```bash
TLA_TOOLS_JAR=/path/to/tla2tools.jar
cd docs/design/models/workspace-definitions-restoration
java -XX:+UseParallelGC -cp "$TLA_TOOLS_JAR" tlc2.TLC \
  -workers 1 -cleanup -coverage 1 \
  -config CompleteRestoration.cfg CompleteRestoration.tla
```

| Configuration | Generated states | Distinct states | Depth | Result |
| --- | ---: | ---: | ---: | --- |
| `CompleteRestoration.cfg` | 1,107,145 | 287,509 | 17 | No error |

The run enabled canonicalization failure and exact or replacement readiness
for each participant. Action coverage was nonzero for request start,
exact/replacement readiness, participant failure, candidate build,
canonicalization success and failure, commit, abort, discard, and stale
completion.

Generic deadlock checking is disabled because exhausting the two-request bound
is an expected finite endpoint. The two named liveness properties state the
applicable progress requirements directly.

## Counterexample mutations

Eight opt-in configurations each enable one incorrect coordinator transition
and must fail with the named invariant:

| Configuration | Deliberate defect | Expected violation |
| --- | --- | --- |
| `EarlyCommit.cfg` | Commits while one participant is ready and another is still working | `CommitRequiresEveryParticipantAndCanonicalCandidate` |
| `PartialCommit.cfg` | Updates only one installed participant token | `InstalledSnapshotIsAtomic` |
| `CommitFailed.cfg` | Commits after a participant failed | `FailedAttemptNeverCommits` |
| `CommitSuperseded.cfg` | Commits an older live attempt after a new request starts | `SupersededAttemptNeverCommits` |
| `AbortChangesInstalled.cfg` | Advances installed state while publishing abort | `AbortRetainsInstalledSnapshotAndRevision` |
| `StaleCompletionInstalls.cfg` | Lets a completion for a settled token replace installed state | `StaleCompletionCannotInstall` |
| `WrongRelation.cfg` | Swaps exact and replacement result classification | `CommitRelationMatchesPreparedCandidate` |
| `WrongRequest.cfg` | Publishes the other request's payload | `CommittedSnapshotCorrelatesExactRequest` |

Run a mutation by substituting its configuration name in the command above and
adding `-noGenerateSpecTE`. Each mutation was checked with one worker and
produced its named invariant violation. Mutation state counts are not recorded
because the first violating trace, rather than complete traversal, is the
evidence.
