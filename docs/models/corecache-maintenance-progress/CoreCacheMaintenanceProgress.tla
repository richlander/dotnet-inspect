--------------------- MODULE CoreCacheMaintenanceProgress ---------------------
(***************************************************************************)
(* Models CoreCache.CacheMaintenanceProgress (src/DotnetInspector.Core/     *)
(* CoreCache.cs), the counter object background maintenance tasks update   *)
(* (RecordDeletion) and that CoreCache.CancelAndWaitForMaintenance reads    *)
(* and resets, on a timed-out wait (TakeSnapshot; the non-destructive       *)
(* Snapshot is out of scope -- see the design doc and README).             *)
(*                                                                         *)
(* CoreCache.cs serializes every *control* operation (Register, Initialize, *)
(* Clear, CancelAndWaitForMaintenance, RequestVersionedCategoryCleanupAsync) *)
(* under one process-wide lock, so those never interleave with each other. *)
(* The genuine concurrency this model isolates is between that single      *)
(* lock-holding control thread and the independent background Task.Run     *)
(* bodies that delete directories and call RecordDeletion. This model does *)
(* not model registration, scheduling, or generation transitions'          *)
(* TakeSnapshot call against their *own* just-drained tasks (via an        *)
(* unbounded Task.WaitAll in WaitForMaintenanceTasksBestEffort), because    *)
(* that particular read/write pair cannot tear. A distinct exposure --     *)
(* a generation transition's TakeSnapshot racing an *outstanding*          *)
(* RequestVersionedCategoryCleanupAsync aggregate task's own Snapshot call, *)
(* which that transition does not wait for -- is a real, separate gap in   *)
(* this model's scope; see the design doc's "Maintenance progress          *)
(* accounting" section and the README's Non-claims.                        *)
(*                                                                         *)
(* The reader action models CancelAndWaitForMaintenance specifically, and   *)
(* only its timed-out case: Clear always waits for its triggered tasks     *)
(* with an effectively unbounded timeout before reading, so it always      *)
(* observes a guaranteed-quiescent state and cannot tear (see the design    *)
(* doc); a CancelAndWaitForMaintenance call whose bounded wait completes    *)
(* in time is equally unaffected.                                          *)
(*                                                                         *)
(* THIS MODEL CLAIMED CURRENT PRODUCT BEHAVIOR for AllowTornWrite = TRUE and *)
(* AllowTornRead = TRUE prior to the fix landed in                         *)
(* src/DotnetInspector.Core/CoreCache.cs: CacheMaintenanceProgress.        *)
(* RecordDeletion performed                                                *)
(*   Interlocked.Add(ref _bytesFreed, bytesFreed);                         *)
(*   Interlocked.Increment(ref _directoriesDeleted);                       *)
(* as two independent operations, and TakeSnapshot read the same two       *)
(* fields with two independent Interlocked.Exchange calls in the same      *)
(* order (the non-destructive Snapshot() used Interlocked.Read plus        *)
(* Volatile.Read and is out of scope -- see the design doc and README).    *)
(* CoreCache.WaitForMaintenance calls TakeSnapshot after only a *bounded*   *)
(* wait for the in-flight maintenance task (task.Wait(timeout), then, on    *)
(* timeout, a further 25ms grace period) -- not after a guaranteed-complete *)
(* drain -- so a background task's RecordDeletion could still be mid-flight *)
(* when a caller's TakeSnapshot fired.                                      *)
(*                                                                         *)
(* AllowTornWrite = FALSE and AllowTornRead = FALSE (Safety.cfg,            *)
(* Liveness.cfg) now match shipped product behavior: CacheMaintenanceProgress*)
(* guards RecordDeletion, Record, Snapshot, and TakeSnapshot with a single   *)
(* lock, so each one's read/update of both fields is one atomic step.       *)
(* AllowTornWrite = TRUE and/or AllowTornRead = TRUE (the Broken*.cfg        *)
(* configurations) no longer describe shipped behavior; they remain as      *)
(* negative controls proving the lock is load-bearing.                      *)
(***************************************************************************)
EXTENDS Naturals, FiniteSets

CONSTANTS
    MaxDeletions,
    AllowTornWrite,
    AllowTornRead

ASSUME
    /\ MaxDeletions \in Nat
    /\ MaxDeletions >= 2
    /\ AllowTornWrite \in BOOLEAN
    /\ AllowTornRead \in BOOLEAN

(* MaxDeletions >= 2 is required, not just conventional: a model of the *)
(* isolated single-deletion race (a read starting when no deletion *)
(* anywhere has pending work) needs at least two deletions so one *)
(* already-completed deletion can open the SomePendingReport guard while *)
(* a second deletion's own write races the read's two sub-steps; see the *)
(* comment on SomePendingReport below. At MaxDeletions = 1 that guard *)
(* prevents a read from ever starting until the sole deletion is fully *)
(* recorded, which would make BrokenTornReadOnly.cfg pass vacuously -- a *)
(* false result for a configuration whose whole point is that a second *)
(* deletion can complete between a reader's two sub-steps. Every .cfg *)
(* here uses MaxDeletions = 2.) *)
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
(* CancelAndWaitForMaintenance. Prior to the fix, production code exchanged  *)
(* _bytesFreed then _directoriesDeleted as two independent                  *)
(* Interlocked.Exchange calls, so a write could land between them; this is  *)
(* modeled as one read *episode* spanning two sub-steps, gated by           *)
(* activeReadEpoch so at most one episode is in flight at a time (matching  *)
(* the fact that CancelAndWaitForMaintenance holds s_maintenanceLock for its *)
(* whole body, so two reads never overlap each other -- only a background   *)
(* write can land inside one episode).                                      *)
(*                                                                         *)
(* SomePendingReport gates the *start* of an episode. It is a genuine        *)
(* restriction on which interleavings this model explores, not a claim that  *)
(* a real TakeSnapshot call can only ever start when something is pending --  *)
(* production really can call TakeSnapshot with nothing yet pending, and a    *)
(* background write can still land between its two Interlocked.Exchange      *)
(* calls. What the guard excludes is only the case where NO deletion         *)
(* anywhere has pending work at episode-start; with MaxDeletions >= 2 (every  *)
(* .cfg here uses 2), that exact single-deletion race is still fully          *)
(* explored whenever a *different* deletion is already pending -- the        *)
(* BrokenTornWriteAndRead.cfg counterexample is exactly this shape (deletion  *)
(* 2's directory count is captured in one episode while its byte count is    *)
(* captured in a later one, using deletion 1's already-done status to open    *)
(* the episode). A model configured with MaxDeletions = 1 would not explore   *)
(* the fully isolated case and should not be relied on for this property.    *)
(* Without the guard, weak fairness forces empty (nothing-pending) episodes   *)
(* to fire repeatedly forever (production never repeats an empty             *)
(* TakeSnapshot call without bound the way an always-enabled TLA+ action     *)
(* does), and the StateConstraint needed to keep the state space finite      *)
(* (see below) can then be exhausted by those empty episodes before genuine  *)
(* progress happens -- silently pruning unexplored fair behaviors rather      *)
(* than flagging them, since TLC treats a constraint-excluded successor as    *)
(* a dead end, not a counterexample, when CHECK_DEADLOCK is FALSE. With the   *)
(* guard, every episode that starts strictly reduces the number of           *)
(* not-yet-attributed writer steps, so the total number of episodes in ANY   *)
(* execution is bounded by 2*MaxDeletions regardless of scheduling, making    *)
(* the StateConstraint below a true bound rather than a lossy one.           *)
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
