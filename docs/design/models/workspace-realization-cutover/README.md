# Workspace realization cutover

## Owner and claim

[Artifact acquisition and workspace
composition](../../artifact-acquisition-and-workspaces.md#active-workspace-realization-cutover)
owns this model for issue
[#6752](https://github.com/richlander/dotnet-inspect/issues/6752).
It checks one active Workspace realization, one unpublished replacement
candidate, candidate-construction admission and drainage, exact active-operation
admission, predecessor drainage, coordinator close, and visible terminal
settlement.

The model uses the product's join currencies:

- one fresh realization identity per Workspace;
- one immutable definition association per realization; and
- one operation lease associated with that exact realization and definition.

The fixed definition value abstracts the operation's captured exact Workspace,
registration-revision, and Scope-revision tuple. The model does not require an
additional `WorkspaceDefinitionSnapshotIdentity`. Dynamic append-only
definition publication is owned by issue
[#6751](https://github.com/richlander/dotnet-inspect/issues/6751) and is not
copied into this model.

## Boundaries and assumptions

Three realizations permit first activation, candidate failure or
supersession, one successful replacement, and another candidate while the
predecessor drains. Two construction operations permit overlapping candidate
work, and two active operations permit overlapping predecessor and successor
use. These are bounded checks, not an unbounded proof.

Candidate construction uses explicit admitted holders. Completion changes
`Preparing` to `Completing`, closes new construction admission, drains holders,
and only then reaches `Ready`. Artifact Roots, binding contexts, package bytes,
caches, and cleanup internals remain with their existing owners. `Settle`
represents the terminal result of ordinary awaited Workspace close. A failed
result remains recorded; the model does not classify the lower owner's report.
A superseding replacement closes construction admission and remains pending
until the displaced candidate's admitted construction drains and the candidate
settles; only then can the replacement enter `Preparing`.

`CutOver` is one atomic action. It closes predecessor admission and selects the
successor without waiting. A lease admitted before cutover remains associated
with the predecessor. Close is requested only after its final lease releases.
No action transfers an existing operation to the successor.

`CloseCoordinator` models the existing terminal `CloseAsync` transition. It
atomically changes the coordinator from `Open` to `Closing`, removes active and
candidate admission, cancels an unmaterialized pending candidate, and records
every issued realization identity whose settlement the close operation must
observe. Active and candidate realizations enter the existing `Draining`
lifecycle; already-draining predecessors remain there. Holder release,
`RequestClose`, and `Settle` remain the only drainage machinery.
`FinishCoordinatorClose` reaches `Closed` only after every recorded realization
is `Settled` or `Failed`. Failed settlement remains visible and is terminal for
close; no action reopens the coordinator or revives its authority.

`FairSpec` assumes each admitted construction or active operation eventually
releases and each enabled close request and settlement eventually runs. It does
not assume candidate success, guarantee admission, require coordinator close,
or prove recovery from a caller that abandons a lease. Once close begins, weak
fairness also schedules terminal close after all recorded settlements finish.
The actions require no blocking wait or worker thread, so the same progress
argument applies to single-threaded Browser/Wasm.

## Checked properties

`Safety` checks:

- exactly the selected active realization admits operations;
- only the current preparing candidate admits construction;
- a candidate is unpublished and has no active-operation authority;
- a ready candidate has no admitted construction authority;
- replacement construction begins only after its candidate-settlement barrier;
- admitted candidate construction retains exact candidate authority while it
  drains;
- every lease retains its exact realization and definition;
- no operation is newly admitted to a draining predecessor;
- close begins only after construction and active-operation drainage;
- settled realizations are detached; and
- failed settlement remains visible;
- coordinator close removes active, candidate, pending, and construction
  admission;
- every issued realization at close remains in the recorded settlement set;
  and
- `Closed` is reached only after every recorded settlement terminates.

`DrainageTerminates` checks conditional eventual settlement for every draining
realization.
`CoordinatorCloseTerminates` checks conditional eventual terminal close under
the same holder-release and settlement fairness.

## Gates and adversarial controls

Every configuration is pinned in `eng/tla-expected-exit-codes.txt`.

| Configuration | Exit | Evidence |
| --- | --- | --- |
| `Safety.cfg` | 0 | Complete bounded safety state space |
| `Liveness.cfg` | 0 | Conditional drainage progress |
| `CoordinatorCloseLiveness.cfg` | 0 | Conditional terminal coordinator-close progress |
| `BrokenCandidateSettlementBarrier.cfg` | 12 | Replacement construction waits for superseded-candidate settlement |
| `BrokenReadyWithConstruction.cfg` | 12 | Readiness waits for admitted construction to drain |
| `BrokenPostCutoverAdmission.cfg` | 12 | Draining predecessors cannot admit new operations |
| `BrokenEarlyClose.cfg` | 12 | Close cannot begin while a lease remains |
| `BrokenPrematureCoordinatorClose.cfg` | 12 | Coordinator close cannot finish before recorded holders and settlements terminate |
| `BrokenPostCloseAdmission.cfg` | 12 | Closing or closed coordinators cannot admit new operations |
| `BrokenReviveAfterClose.cfg` | 12 | Terminal close cannot revive retired realization authority |
| `BrokenStaleCandidatePublish.cfg` | 12 | A superseded candidate cannot become active |
| `BrokenTransferAuthority.cfg` | 12 | Cutover cannot move predecessor operations to the successor |
| `BrokenForgetFailure.cfg` | 12 | Failed settlement remains visible |
| `BrokenNeverSettle.cfg` | 13 | Omitting terminal settlement defeats liveness |
| `ReachabilityCloseActiveAndCandidate.cfg` | 12 | Close atomically retires a live candidate and active incumbent |
| `ReachabilityClosePendingCandidate.cfg` | 12 | Close cancels an unmaterialized pending candidate |
| `ReachabilityCloseWithDrainingPredecessor.cfg` | 12 | Close includes an already-draining predecessor |
| `ReachabilityCloseCompletedWithFailure.cfg` | 12 | Visible failed settlement still permits terminal close |
| `ReachabilityCandidateFailure.cfg` | 12 | Candidate failure leaves an observable drainage path |
| `ReachabilityCandidateSupersession.cfg` | 12 | A newer candidate supersedes the old candidate |
| `ReachabilityPredecessorDrainage.cfg` | 12 | A predecessor lease survives successful cutover |
| `ReachabilitySettlementFailure.cfg` | 12 | Terminal cleanup failure is reachable and recorded |

Exit 12 is an expected invariant counterexample for a broken policy or a
negated reachability witness. Exit 13 is the expected temporal-property
counterexample for omitted settlement.

## Run

From the repository root, with the pinned tools:

```bash
model=docs/design/models/workspace-realization-cutover
mkdir -p "$model/.scratch"
TMPDIR="$PWD/$model/.scratch" \
  JAVA_TOOL_OPTIONS="-XX:ActiveProcessorCount=2 -Djava.io.tmpdir=$PWD/$model/.scratch" \
  bash eng/run-tla-checks.sh "$model"
npx --no-install markdownlint-cli "$model/README.md"
```

The close lifecycle is a formal-evidence correction for the existing
`WorkspaceRealizationCoordinator.CloseAsync` contract under
[Active Workspace realization
cutover](../../artifact-acquisition-and-workspaces.md#active-workspace-realization-cutover).
It introduces no reusable product abstraction, host policy, compatibility
successor, or consumer completion behavior.

The focused changed-path run on 2026-09-20 used TLA Tools
`2026.08.11.125311`. Owner safety and both owner liveness configurations each
explored 52,221 generated states and 14,103 distinct states to depth 21. The
transitive Open-only consumer retained its prior complete result of 841,859
generated states and 236,408 distinct states to depth 28. All 34 selected
exact semantic verdicts matched the manifest.
