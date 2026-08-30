--------------------- MODULE CoreCacheMaintenanceProgress ---------------------
(***************************************************************************)
(* Models CoreCache.CacheMaintenanceProgress (src/DotnetInspector.Core/     *)
(* CoreCache.cs), the counter object background maintenance tasks update   *)
(* (RecordDeletion) and that CoreCache.CancelAndWaitForMaintenance reads    *)
(* and resets, on a timed-out wait (Snapshot / TakeSnapshot).               *)
(*                                                                         *)
(* CoreCache.cs serializes every *control* operation (Register, Initialize, *)
(* Clear, CancelAndWaitForMaintenance, RequestVersionedCategoryCleanupAsync) *)
(* under one process-wide lock, so those never interleave with each other. *)
(* The only genuine concurrency left in the maintenance lifecycle is       *)
(* between that single lock-holding control thread and the independent     *)
(* background Task.Run bodies that delete directories and call              *)
(* RecordDeletion. This model isolates exactly that interaction; it does   *)
(* not model registration, scheduling, or generation transitions, because  *)
(* those already fully drain outstanding tasks (via an unbounded            *)
(* Task.WaitAll in WaitForMaintenanceTasksBestEffort) before touching the   *)
(* progress object, so they cannot tear.                                   *)
(*                                                                         *)
(* The reader action models CancelAndWaitForMaintenance specifically, and   *)
(* only its timed-out case: Clear always waits for its triggered tasks     *)
(* with an effectively unbounded timeout before reading, so it always      *)
(* observes a guaranteed-quiescent state and cannot tear (see the design    *)
(* doc); a CancelAndWaitForMaintenance call whose bounded wait completes    *)
(* in time is equally unaffected.                                          *)
(*                                                                         *)
(* THIS MODEL CLAIMS CURRENT PRODUCT BEHAVIOR for AllowTornWrite = TRUE and *)
(* AllowTornRead = TRUE: CacheMaintenanceProgress.RecordDeletion performs   *)
(*   Interlocked.Add(ref _bytesFreed, bytesFreed);                         *)
(*   Interlocked.Increment(ref _directoriesDeleted);                       *)
(* as two independent operations, and TakeSnapshot/Snapshot read the same   *)
(* two fields with two independent Interlocked calls in the same order.    *)
(* CoreCache.WaitForMaintenance calls TakeSnapshot after only a *bounded*   *)
(* wait for the in-flight maintenance task (task.Wait(timeout), then, on    *)
(* timeout, a further 25ms grace period) -- not after a guaranteed-complete *)
(* drain -- so a background task's RecordDeletion can still be mid-flight   *)
(* when a caller's TakeSnapshot fires.                                     *)
(*                                                                         *)
(* AllowTornWrite = FALSE and/or AllowTornRead = FALSE model a proposed FIX: *)
(* guarding CacheMaintenanceProgress's fields with a single lock so that    *)
(* RecordDeletion and the read+reset are each one atomic step, instead of   *)
(* two independent Interlocked calls.                                      *)
(***************************************************************************)
EXTENDS Naturals, FiniteSets

CONSTANTS
    MaxDeletions,
    AllowTornWrite,
    AllowTornRead

ASSUME
    /\ MaxDeletions \in Nat \ {0}
    /\ AllowTornWrite \in BOOLEAN
    /\ AllowTornRead \in BOOLEAN

Deletions == 1..MaxDeletions

VARIABLES
    bytesStepDone,   \* [Deletions -> BOOLEAN]: has this deletion's byte contribution been recorded?
    dirsStepDone,    \* [Deletions -> BOOLEAN]: has this deletion's directory contribution been recorded?
    bytesEpoch,      \* [Deletions -> Nat]: which consume call's report included this deletion's bytes (0 = still live)
    dirsEpoch,       \* [Deletions -> Nat]: which consume call's report included this deletion's directory count (0 = still live)
    nextEpoch,       \* Nat: the epoch number the next consume episode will use
    activeReadEpoch  \* Nat: the epoch of a torn-read episode in progress between its two sub-steps (0 = none in progress)

vars == <<bytesStepDone, dirsStepDone, bytesEpoch, dirsEpoch, nextEpoch, activeReadEpoch>>

TypeOK ==
    /\ bytesStepDone \in [Deletions -> BOOLEAN]
    /\ dirsStepDone \in [Deletions -> BOOLEAN]
    /\ bytesEpoch \in [Deletions -> Nat]
    /\ dirsEpoch \in [Deletions -> Nat]
    /\ nextEpoch \in Nat \ {0}
    /\ activeReadEpoch \in Nat

Init ==
    /\ bytesStepDone = [d \in Deletions |-> FALSE]
    /\ dirsStepDone = [d \in Deletions |-> FALSE]
    /\ bytesEpoch = [d \in Deletions |-> 0]
    /\ dirsEpoch = [d \in Deletions |-> 0]
    /\ nextEpoch = 1
    /\ activeReadEpoch = 0

(***************************************************************************)
(* Writer side: one RecordDeletion(bytesFreed) call per completed          *)
(* directory deletion. Production code always performs Add(bytes) then     *)
(* Increment(dirs) in that program order within a single call, so          *)
(* RecordDirsStep requires RecordBytesStep to already hold for the same d.  *)
(***************************************************************************)

RecordBytesStep(d) ==
    /\ ~bytesStepDone[d]
    /\ bytesStepDone' = [bytesStepDone EXCEPT ![d] = TRUE]
    /\ UNCHANGED <<dirsStepDone, bytesEpoch, dirsEpoch, nextEpoch, activeReadEpoch>>

RecordDirsStep(d) ==
    /\ bytesStepDone[d]
    /\ ~dirsStepDone[d]
    /\ dirsStepDone' = [dirsStepDone EXCEPT ![d] = TRUE]
    /\ UNCHANGED <<bytesStepDone, bytesEpoch, dirsEpoch, nextEpoch, activeReadEpoch>>

(* Fixed writer: a lock-guarded RecordDeletion flips both fields as one     *)
(* indivisible step. *)
AtomicRecordDeletion(d) ==
    /\ ~bytesStepDone[d]
    /\ bytesStepDone' = [bytesStepDone EXCEPT ![d] = TRUE]
    /\ dirsStepDone' = [dirsStepDone EXCEPT ![d] = TRUE]
    /\ UNCHANGED <<bytesEpoch, dirsEpoch, nextEpoch, activeReadEpoch>>

WriteStep(d) ==
    IF AllowTornWrite
    THEN RecordBytesStep(d) \/ RecordDirsStep(d)
    ELSE AtomicRecordDeletion(d)

(***************************************************************************)
(* Reader side: TakeSnapshot(), called by a timed-out                       *)
(* CancelAndWaitForMaintenance. Production code exchanges _bytesFreed then   *)
(* _directoriesDeleted as two independent Interlocked.Exchange calls, so a  *)
(* write can land between them; this is modeled as one read *episode*       *)
(* spanning two sub-steps, gated by activeReadEpoch so at most one episode  *)
(* is in flight at a time (matching the fact that CancelAndWaitForMaintenance*)
(* holds s_maintenanceLock for its whole body, so two reads never overlap    *)
(* each other -- only a background write can land inside one episode).      *)
(*                                                                         *)
(* SomePendingReport gates the *start* of an episode: production code only  *)
(* calls TakeSnapshot when a caller actually invokes                        *)
(* CancelAndWaitForMaintenance, so an episode that would report nothing new  *)
(* has no real-world analogue and exists in this model only as an artifact  *)
(* of making the reader always-enabled. Without this guard, weak fairness    *)
(* forces such empty episodes to fire repeatedly, and the StateConstraint    *)
(* needed to keep the state space finite (see below) can then be exhausted   *)
(* by empty episodes before genuine progress happens -- silently pruning    *)
(* unexplored fair behaviors rather than flagging them, since TLC treats a   *)
(* constraint-excluded successor as a dead end, not a counterexample, when   *)
(* CHECK_DEADLOCK is FALSE. With the guard, every episode that starts        *)
(* strictly reduces the number of not-yet-attributed writer steps, so the    *)
(* total number of episodes in ANY execution is bounded by 2*MaxDeletions    *)
(* regardless of scheduling, making the StateConstraint below a true bound   *)
(* rather than a lossy one.                                                  *)
(***************************************************************************)

SomePendingReport ==
    \E d \in Deletions :
        \/ (bytesStepDone[d] /\ bytesEpoch[d] = 0)
        \/ (dirsStepDone[d] /\ dirsEpoch[d] = 0)

ConsumeBytesStep ==
    /\ activeReadEpoch = 0
    /\ SomePendingReport
    /\ activeReadEpoch' = nextEpoch
    /\ nextEpoch' = nextEpoch + 1
    /\ bytesEpoch' = [d \in Deletions |->
                        IF bytesStepDone[d] /\ bytesEpoch[d] = 0
                        THEN nextEpoch
                        ELSE bytesEpoch[d]]
    /\ UNCHANGED <<bytesStepDone, dirsStepDone, dirsEpoch>>

ConsumeDirsStep ==
    /\ activeReadEpoch # 0
    /\ dirsEpoch' = [d \in Deletions |->
                        IF dirsStepDone[d] /\ dirsEpoch[d] = 0
                        THEN activeReadEpoch
                        ELSE dirsEpoch[d]]
    /\ activeReadEpoch' = 0
    /\ UNCHANGED <<bytesStepDone, dirsStepDone, bytesEpoch, nextEpoch>>

(* Fixed reader: a lock-guarded read+reset captures both fields as one      *)
(* indivisible step, so no write can land inside the episode. *)
AtomicConsume ==
    /\ activeReadEpoch = 0
    /\ SomePendingReport
    /\ bytesEpoch' = [d \in Deletions |->
                        IF bytesStepDone[d] /\ bytesEpoch[d] = 0
                        THEN nextEpoch
                        ELSE bytesEpoch[d]]
    /\ dirsEpoch' = [d \in Deletions |->
                        IF dirsStepDone[d] /\ dirsEpoch[d] = 0
                        THEN nextEpoch
                        ELSE dirsEpoch[d]]
    /\ nextEpoch' = nextEpoch + 1
    /\ UNCHANGED <<bytesStepDone, dirsStepDone, activeReadEpoch>>

ReadStep ==
    IF AllowTornRead
    THEN ConsumeBytesStep \/ ConsumeDirsStep
    ELSE AtomicConsume

Next == (\E d \in Deletions : WriteStep(d)) \/ ReadStep

Spec ==
    /\ Init
    /\ [][Next]_vars
    /\ WF_vars(ReadStep)
    /\ \A d \in Deletions : WF_vars(WriteStep(d))

(***************************************************************************)
(* Properties                                                             *)
(***************************************************************************)

(* Safety: once both a deletion's byte contribution and its directory-count *)
(* contribution have been attributed to *some* consume episode, they must   *)
(* be the *same* episode. A violation means one CancelAndWaitForMaintenance  *)
(* report undercounted directories (or bytes) for a deletion whose other     *)
(* half was already reported earlier, and no later report can ever correct   *)
(* it, because TakeSnapshot destructively resets both fields to zero. *)
NoTornAccounting ==
    \A d \in Deletions :
        (bytesEpoch[d] # 0 /\ dirsEpoch[d] # 0) => (bytesEpoch[d] = dirsEpoch[d])

(* Liveness: a deletion that has been fully recorded (both writer steps      *)
(* done) is eventually consumed by some report, rather than remaining live   *)
(* forever. This holds regardless of AllowTornWrite/AllowTornRead: the        *)
(* defect is about *which* report a deletion lands in, not about whether it   *)
(* is ever reported at all. *)
EventuallyConsumed ==
    \A d \in Deletions :
        (bytesStepDone[d] /\ dirsStepDone[d]) ~> (bytesEpoch[d] # 0 /\ dirsEpoch[d] # 0)

(***************************************************************************)
(* nextEpoch counts consume episodes and has no production analogue (the    *)
(* real code never numbers its TakeSnapshot calls). SomePendingReport gates  *)
(* every episode start on genuine unattributed work, so each episode         *)
(* strictly reduces the number of pending (bytesStepDone, bytesEpoch=0) or   *)
(* (dirsStepDone, dirsEpoch=0) facts -- there are at most 2*MaxDeletions of   *)
(* these across all deletions, and each is consumed exactly once. Therefore   *)
(* no execution can start more than 2*MaxDeletions episodes, and nextEpoch    *)
(* (which starts at 1) can never exceed 2*MaxDeletions + 1. StateConstraint   *)
(* uses exactly that bound: it is a true bound on every reachable state, not  *)
(* an artifact that could prune a genuine fair behavior before it completes. *)
(***************************************************************************)
StateConstraint == nextEpoch <= 2 * MaxDeletions + 1

=============================================================================
