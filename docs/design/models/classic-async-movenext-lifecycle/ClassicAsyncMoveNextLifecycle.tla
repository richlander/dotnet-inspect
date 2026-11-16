------------------- MODULE ClassicAsyncMoveNextLifecycle -------------------
(***************************************************************************)
(* The MoveNext re-entry lifecycle of a Roslyn classic async state machine, *)
(* as docs/design/classic-async-reconstruction.md (General inverse) relies  *)
(* on it: the state moves -1 -> N -> -1 for each suspension and ends at -2; *)
(* a resume enters only through dispatch on a stored state whose awaiter    *)
(* field is set; the builder completes exactly once; nothing re-enters      *)
(* after -2; and an exit through an enclosing finally runs the guarded body *)
(* only when the state is -1.                                               *)
(*                                                                          *)
(* Lean owns the splice equivalence (docs/design/models/classic-async-      *)
(* splice/); this model owns only the lifecycle. The two share the role     *)
(* table's state constants (0..Awaits-1, -1 running, -2 done) and the two   *)
(* completion callbacks (SetResult, SetException).                          *)
(*                                                                          *)
(* Mutation selects a deliberately broken machine for a negative control:   *)
(*   None               the lowering as Roslyn emits it;                    *)
(*   ResumeWithoutStore a resume that does not require the awaiter field;   *)
(*   SkipLandingReset   a landing that leaves the state at N;               *)
(*   DoubleCompletion   a completion path that may run SetResult twice;     *)
(*   FinallyOnSuspend   a finally guard that runs the body while suspending.*)
(***************************************************************************)
EXTENDS Integers, TLC

CONSTANTS Awaits, Mutation

None == "None"
ResumeWithoutStore == "ResumeWithoutStore"
SkipLandingReset == "SkipLandingReset"
DoubleCompletion == "DoubleCompletion"
FinallyOnSuspend == "FinallyOnSuspend"
Mutations == {None, ResumeWithoutStore, SkipLandingReset, DoubleCompletion, FinallyOnSuspend}

ASSUME /\ Awaits \in Nat
       /\ Mutation \in Mutations

Idle == "Idle"
Running == "Running"
Suspended == "Suspended"
Done == "Done"
Phases == {Idle, Running, Suspended, Done}

Running_ == -1
Done_ == -2
States == {Running_, Done_} \cup (0 .. Awaits - 1)

VARIABLES
    phase,          \* where control is: before Start, inside MoveNext, returned suspended, or completed
    state,          \* the <>1__state field
    awaiter,        \* which <>u__k cache fields hold an awaiter
    next,           \* the next suspension site in program order (0..Awaits)
    inFinally,      \* MoveNext is currently exiting through an enclosing finally region
    finallyAt,      \* sites whose enclosing finally region has been exited once while running
    finallyRanSuspended, \* a finally body ran while the machine was returning suspended
    badResume,      \* a resume entered a site whose state was not stored with its awaiter
    completions     \* builder completion callbacks observed (SetResult or SetException), capped at 2

vars == <<phase, state, awaiter, next, inFinally, finallyAt, finallyRanSuspended, badResume, completions>>

TypeOK ==
    /\ phase \in Phases
    /\ state \in States
    /\ awaiter \in [0 .. Awaits - 1 -> BOOLEAN]
    /\ next \in 0 .. Awaits
    /\ inFinally \in BOOLEAN
    /\ finallyAt \in [0 .. Awaits -> BOOLEAN]
    /\ finallyRanSuspended \in BOOLEAN
    /\ badResume \in BOOLEAN
    /\ completions \in 0 .. 2

Init ==
    /\ phase = Idle
    /\ state = Running_
    /\ awaiter = [k \in 0 .. Awaits - 1 |-> FALSE]
    /\ next = 0
    /\ inFinally = FALSE
    /\ finallyAt = [k \in 0 .. Awaits |-> FALSE]
    /\ finallyRanSuspended = FALSE
    /\ badResume = FALSE
    /\ completions = 0

\* The kickoff's Start: the first MoveNext enters with the state the kickoff stored.
Start ==
    /\ phase = Idle
    /\ phase' = Running
    /\ UNCHANGED <<state, awaiter, next, inFinally, finallyAt, finallyRanSuspended, badResume, completions>>

\* The awaiter of site k had completed: the machine runs on past the site.
Complete(k) ==
    /\ phase = Running
    /\ ~inFinally
    /\ next = k
    /\ k < Awaits
    /\ next' = k + 1
    /\ UNCHANGED <<phase, state, awaiter, inFinally, finallyAt, finallyRanSuspended, badResume, completions>>

\* The awaiter of site k had not completed: store the state and the awaiter and return.
Suspend(k) ==
    /\ phase = Running
    /\ ~inFinally
    /\ next = k
    /\ k < Awaits
    /\ state' = k
    /\ awaiter' = [awaiter EXCEPT ![k] = TRUE]
    /\ phase' = Suspended
    /\ inFinally' = TRUE          \* the return now passes through enclosing finally regions
    /\ UNCHANGED <<next, finallyAt, finallyRanSuspended, badResume, completions>>

\* Exiting a finally region: Roslyn guards the body with `state < 0`.
ExitFinally ==
    /\ inFinally
    /\ inFinally' = FALSE
    /\ finallyRanSuspended' =
         IF (state < 0 \/ Mutation = FinallyOnSuspend) /\ state >= 0
            THEN TRUE
            ELSE finallyRanSuspended
    /\ UNCHANGED <<phase, state, awaiter, next, finallyAt, badResume, completions>>

\* A normal exit through a finally region while running: the guard holds and the body runs
\* (modeled once per site position so the state space stays finite).
RunFinally ==
    /\ phase = Running
    /\ ~inFinally
    /\ ~finallyAt[next]
    /\ finallyAt' = [finallyAt EXCEPT ![next] = TRUE]
    /\ inFinally' = TRUE
    /\ UNCHANGED <<phase, state, awaiter, next, finallyRanSuspended, badResume, completions>>

\* The continuation re-enters MoveNext: dispatch on the stored state to its landing.
Resume(k) ==
    /\ phase = Suspended
    /\ ~inFinally
    /\ ((state = k /\ awaiter[k]) \/ Mutation = ResumeWithoutStore)
    /\ badResume' = badResume \/ ~(state = k /\ awaiter[k])
    /\ awaiter' = [awaiter EXCEPT ![k] = FALSE]
    /\ state' = IF Mutation = SkipLandingReset THEN state ELSE Running_
    /\ next' = k + 1
    /\ phase' = Running
    /\ UNCHANGED <<inFinally, finallyAt, finallyRanSuspended, completions>>

\* Every site passed: the completion tail sets -2 and calls SetResult once.
SetResult ==
    /\ phase = Running
    /\ ~inFinally
    /\ next = Awaits
    /\ state' = IF Mutation = DoubleCompletion /\ completions = 0 THEN Running_ ELSE Done_
    /\ completions' = completions + 1
    /\ phase' = IF Mutation = DoubleCompletion /\ completions = 0 THEN Running ELSE Done
    /\ UNCHANGED <<awaiter, next, inFinally, finallyAt, finallyRanSuspended, badResume>>

\* The completion catch: any running MoveNext may fault, set -2, and call SetException once.
SetException ==
    /\ phase = Running
    /\ ~inFinally
    /\ state' = Done_
    /\ completions' = completions + 1
    /\ phase' = Done
    /\ UNCHANGED <<awaiter, next, inFinally, finallyAt, finallyRanSuspended, badResume>>

Next ==
    \/ Start
    \/ \E k \in 0 .. Awaits - 1 : Complete(k) \/ Suspend(k) \/ Resume(k)
    \/ RunFinally
    \/ ExitFinally
    \/ SetResult
    \/ SetException

Spec ==
    /\ Init
    /\ [][Next]_vars
    /\ WF_vars(Start)
    /\ \A k \in 0 .. Awaits - 1 : WF_vars(Complete(k)) /\ WF_vars(Resume(k))
    /\ WF_vars(ExitFinally)
    /\ WF_vars(SetResult)

(***************************************************************************)
(* Invariants: the lifecycle the general inverse relies on.                *)
(***************************************************************************)

\* While MoveNext runs user code, the state is the running state.
RunningStateIsRunning ==
    phase = Running => state = Running_

\* A suspended machine holds a site state whose awaiter field is set.
SuspendedStoresItsAwaiter ==
    phase = Suspended /\ ~inFinally => state \in 0 .. Awaits - 1 /\ awaiter[state]

\* At most one awaiter field is live, and only while suspended.
OneLiveAwaiter ==
    /\ \A j, k \in 0 .. Awaits - 1 : awaiter[j] /\ awaiter[k] => j = k
    /\ (phase \in {Idle, Running, Done} => \A k \in 0 .. Awaits - 1 : ~awaiter[k])

\* The builder completes exactly once, and only at the end.
CompletesAtMostOnce ==
    completions <= 1

DoneMeansCompletedOnce ==
    phase = Done => completions = 1 /\ state = Done_

\* Nothing runs after completion: every action's precondition excludes Done,
\* so Done is terminal by construction (CHECK_DEADLOCK is off for that reason).

\* A finally body ran only with the running state, never while suspending.
FinallyBodiesRanRunning ==
    ~finallyRanSuspended

\* Every resume entered through dispatch on the stored state with its awaiter set.
ResumesEnterStoredState ==
    ~badResume

Invariants ==
    /\ TypeOK
    /\ RunningStateIsRunning
    /\ SuspendedStoresItsAwaiter
    /\ OneLiveAwaiter
    /\ CompletesAtMostOnce
    /\ DoneMeansCompletedOnce
    /\ FinallyBodiesRanRunning
    /\ ResumesEnterStoredState

\* Under fairness every machine completes.
EventuallyDone == <>(phase = Done)

=============================================================================
