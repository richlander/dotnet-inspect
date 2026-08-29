---------------------- MODULE ArtifactSessionAdmission ----------------------
(***************************************************************************)
(* Design model of `ArtifactSetSession` admission, described in            *)
(* `docs/design/artifact-acquisition-and-workspaces.md`.                   *)
(*                                                                         *)
(* The model checks single-flight admission across concurrent demands,     *)
(* an incompatible-generation demand's inability to join or start          *)
(* duplicate work while a prior admission is active, cancellation before   *)
(* join and while in-flight or draining, disposal-forced cancellation, and *)
(* the rule that a late adapter result never publishes a session or group  *)
(* once disposal has begun. It says                                        *)
(* nothing about which adapter runs, budget arithmetic, content identity,  *)
(* assembly projection, or query-lease authorization.                     *)
(*                                                                         *)
(* This models the target design described in the doc's prose, not the    *)
(* current `ArtifactSetSession` implementation. As of this writing,       *)
(* `ArtifactSetSession`'s own doc comment states it "does not yet         *)
(* implement workspace-wide reservation, single-flight admission, or      *)
(* dependent-group quiescence": there is no multi-demand join, no         *)
(* incompatible-generation wait, and `Dispose()` releases every           *)
(* acquisition lease immediately rather than deferring release until      *)
(* dependent groups quiesce. Treat this model as a check on the doc's     *)
(* stated intent, not evidence about shipped behavior.                    *)
(*                                                                         *)
(* Product concept                          Model variable                 *)
(*   admission operation lifecycle           admission                    *)
(*   admitted context+policy generation      generation                   *)
(*   demands attached to the operation       waiters                      *)
(*   a demand's requested generation         pendingGeneration             *)
(*   caller cancellation requested            cancelRequested             *)
(*   reserved admission budget                reserved                    *)
(*   workspace disposal begun                disposed                    *)
(*   per-demand delivered outcome            outcomeOf                    *)
(*   published group awaiting release        groupActive                 *)
(*   dependent group reports quiescent       groupQuiescent               *)
(*   artifact leases released                leaseReleased                *)
(*                                                                         *)
(* Guard witnesses. `publishSafetyWitness`, `leaseSafetyWitness`, and      *)
(* `authorizedOutcomeWitness` are latching booleans. The step that         *)
(* publishes a group, releases its leases, or delivers a terminal outcome  *)
(* independently re-derives the exact condition the design requires from  *)
(* the pre-step state and conjoins it into the witness. The paired         *)
(* invariant then fails if a future weakening of an action's own guard     *)
(* lets the step happen without that condition -- a plain invariant over   *)
(* only post-step state cannot detect this, because the post-step state    *)
(* the action itself just built already looks self-consistent.             *)
(*                                                                         *)
(* Modeling simplification. Only one published group's lease lifecycle is *)
(* tracked at a time: a fresh admission cannot publish while the previous  *)
(* group is still awaiting lease release. The product does not serialize   *)
(* real groups this way; this bounds the state space for a check that is  *)
(* about one admission's publish-vs-disposal race, not concurrent groups.  *)
(* A demand's requested generation is fixed once it arrives; the model     *)
(* does not represent a caller re-deriving a different generation when it *)
(* replans after an incompatible admission terminates.                    *)
(***************************************************************************)
EXTENDS Naturals, FiniteSets, TLC

CONSTANTS
  Demands,      \* finite set of concurrent demand identifiers
  Generations,  \* finite set of distinct (context, policy) generations
  EnablePendingCancellation,
  EnableDrainingCancellation

ASSUME Cardinality(Demands) >= 2
ASSUME Cardinality(Generations) >= 2
ASSUME EnablePendingCancellation \in BOOLEAN
ASSUME EnableDrainingCancellation \in BOOLEAN

VARIABLES
  admission,
  generation,
  waiters,
  pendingGeneration,
  cancelRequested,
  reserved,
  disposed,
  outcomeOf,
  groupActive,
  groupQuiescent,
  leaseReleased,
  publishSafetyWitness,
  leaseSafetyWitness,
  outcomeStableWitness,
  authorizedOutcomeWitness,
  pendingCancellationWitness,
  drainingCancellationWitness

vars == << admission, generation, waiters, pendingGeneration, cancelRequested,
           reserved, disposed, outcomeOf, groupActive, groupQuiescent,
           leaseReleased, publishSafetyWitness, leaseSafetyWitness,
           outcomeStableWitness, authorizedOutcomeWitness,
           pendingCancellationWitness, drainingCancellationWitness >>

\* A demand's outcome may change only away from "none"; once terminal it
\* never changes again. Re-derived on every step that touches outcomeOf.
OutcomeChangeIsGuarded(before, after) ==
  \A d \in Demands : after[d] # before[d] => before[d] = "none"

\* Every demand that a step just resolved to a terminal outcome must have
\* been attached to the admission (in preWaiters) immediately beforehand.
\* Re-derived from pre-step state on every step that resolves outcomes.
ResolvedOnlyAttachedDemands(preWaiters, before, after) ==
  \A d \in Demands : (after[d] # before[d]) => d \in preWaiters

AdmissionStates == {"Idle", "InFlight", "Draining"}
Outcomes == {"none", "published", "failed", "rejected", "stale", "cancelled"}
NoGeneration == "none"

TypeOK ==
  /\ admission \in AdmissionStates
  /\ generation \in Generations \cup {NoGeneration}
  /\ waiters \subseteq Demands
  /\ pendingGeneration \in [Demands -> Generations \cup {NoGeneration}]
  /\ cancelRequested \subseteq Demands
  /\ reserved \in BOOLEAN
  /\ disposed \in BOOLEAN
  /\ outcomeOf \in [Demands -> Outcomes]
  /\ groupActive \in BOOLEAN
  /\ groupQuiescent \in BOOLEAN
  /\ leaseReleased \in BOOLEAN
  /\ publishSafetyWitness \in BOOLEAN
  /\ leaseSafetyWitness \in BOOLEAN
  /\ outcomeStableWitness \in BOOLEAN
  /\ authorizedOutcomeWitness \in BOOLEAN
  /\ pendingCancellationWitness \subseteq Demands
  /\ drainingCancellationWitness \subseteq Demands

\* Idle admission holds no generation, no waiters, and no reservation; any
\* active admission holds exactly a reservation and a real generation.
AdmissionCoherence ==
  /\ (admission = "Idle") <=> (generation = NoGeneration)
  /\ (admission = "Idle") => (waiters = {})
  /\ reserved <=> (admission # "Idle")

\* A demand can only be attached to the admission if it requested exactly
\* the admitted generation; an incompatible generation never joins.
WaiterGenerationMatches ==
  \A d \in Demands : (d \in waiters) => (pendingGeneration[d] = generation)

Init ==
  /\ admission = "Idle"
  /\ generation = NoGeneration
  /\ waiters = {}
  /\ pendingGeneration = [d \in Demands |-> NoGeneration]
  /\ cancelRequested = {}
  /\ reserved = FALSE
  /\ disposed = FALSE
  /\ outcomeOf = [d \in Demands |-> "none"]
  /\ groupActive = FALSE
  /\ groupQuiescent = FALSE
  /\ leaseReleased = FALSE
  /\ publishSafetyWitness = TRUE
  /\ leaseSafetyWitness = TRUE
  /\ outcomeStableWitness = TRUE
  /\ authorizedOutcomeWitness = TRUE
  /\ pendingCancellationWitness = {}
  /\ drainingCancellationWitness = {}

(***************************************************************************)
(* Demand arrival. A demand fixes the generation it requests once, before  *)
(* it can start or join any admission. This is what lets an incompatible   *)
(* generation persist as a genuinely blocked, waiting demand instead of    *)
(* being reinterpreted as a fresh, freely-choosable request every step.    *)
(***************************************************************************)
DemandArrives(d, g) ==
  /\ pendingGeneration[d] = NoGeneration
  /\ outcomeOf[d] = "none"
  /\ pendingGeneration' = [pendingGeneration EXCEPT ![d] = g]
  /\ UNCHANGED << admission, generation, waiters, cancelRequested, reserved,
                  disposed, outcomeOf, groupActive, groupQuiescent,
                  leaseReleased, publishSafetyWitness, leaseSafetyWitness,
                  outcomeStableWitness, authorizedOutcomeWitness,
                  pendingCancellationWitness, drainingCancellationWitness >>

(***************************************************************************)
(* Demand admission.                                                      *)
(*                                                                         *)
(* The first authorized demand for an idle, non-disposed session starts    *)
(* the admission and reserves budget. A compatible concurrent demand joins *)
(* an in-flight admission of the same generation and reserves nothing.     *)
(* An incompatible generation, or any demand against a draining operation, *)
(* is simply not enabled here; it waits for the terminal transition and    *)
(* replans by racing to start the next admission. Disposal rejects a new   *)
(* demand outright.                                                       *)
(***************************************************************************)
DemandStartsAdmission(d) ==
  /\ disposed = FALSE
  /\ admission = "Idle"
  /\ pendingGeneration[d] # NoGeneration
  /\ outcomeOf[d] = "none"
  /\ d \notin cancelRequested
  /\ admission' = "InFlight"
  /\ generation' = pendingGeneration[d]
  /\ waiters' = {d}
  /\ reserved' = TRUE
  /\ UNCHANGED << pendingGeneration, cancelRequested, disposed, outcomeOf,
                  groupActive, groupQuiescent, leaseReleased,
                  publishSafetyWitness, leaseSafetyWitness,
                  outcomeStableWitness, authorizedOutcomeWitness,
                  pendingCancellationWitness, drainingCancellationWitness >>

DemandJoinsAdmission(d) ==
  /\ admission = "InFlight"
  /\ pendingGeneration[d] = generation
  /\ outcomeOf[d] = "none"
  /\ d \notin cancelRequested
  /\ d \notin waiters
  /\ waiters' = waiters \cup {d}
  /\ UNCHANGED << admission, generation, pendingGeneration, cancelRequested,
                  reserved, disposed, outcomeOf, groupActive, groupQuiescent,
                  leaseReleased, publishSafetyWitness, leaseSafetyWitness,
                  outcomeStableWitness, authorizedOutcomeWitness,
                  pendingCancellationWitness, drainingCancellationWitness >>

DemandRejectedWhileDisposed(d) ==
  /\ disposed = TRUE
  /\ pendingGeneration[d] # NoGeneration
  /\ d \notin waiters
  /\ outcomeOf[d] = "none"
  /\ d \notin cancelRequested
  /\ outcomeOf' = [outcomeOf EXCEPT ![d] = "rejected"]
  /\ outcomeStableWitness' = (outcomeStableWitness /\ OutcomeChangeIsGuarded(outcomeOf, outcomeOf'))
  /\ UNCHANGED << admission, generation, waiters, pendingGeneration,
                  cancelRequested, reserved, disposed, groupActive,
                  groupQuiescent, leaseReleased, publishSafetyWitness,
                  leaseSafetyWitness, authorizedOutcomeWitness,
                  pendingCancellationWitness, drainingCancellationWitness >>

(***************************************************************************)
(* Voluntary cancellation. The caller first records cancellation under the *)
(* same owner gate used by start/join. Once recorded, that demand can no    *)
(* longer start or join admission and an adapter cannot resolve it first.   *)
(* An unattached demand cancels directly. An attached in-flight demand      *)
(* detaches; if it was the last waiter, the owner enters draining. A waiter *)
(* overtaken by disposal can still detach from the draining operation.      *)
(***************************************************************************)
CallerRequestsCancellation(d) ==
  /\ pendingGeneration[d] # NoGeneration
  /\ outcomeOf[d] = "none"
  /\ d \notin cancelRequested
  /\ cancelRequested' = cancelRequested \cup {d}
  /\ UNCHANGED << admission, generation, waiters, pendingGeneration, reserved,
                  disposed, outcomeOf, groupActive, groupQuiescent,
                  leaseReleased, publishSafetyWitness, leaseSafetyWitness,
                  outcomeStableWitness, authorizedOutcomeWitness,
                  pendingCancellationWitness, drainingCancellationWitness >>

PendingDemandCancels(d) ==
  /\ EnablePendingCancellation
  /\ d \in cancelRequested
  /\ d \notin waiters
  /\ outcomeOf[d] = "none"
  /\ pendingGeneration[d] # NoGeneration
  /\ pendingGeneration' = [pendingGeneration EXCEPT ![d] = NoGeneration]
  /\ outcomeOf' = [outcomeOf EXCEPT ![d] = "cancelled"]
  /\ pendingCancellationWitness' = pendingCancellationWitness \cup {d}
  /\ outcomeStableWitness' =
       outcomeStableWitness /\ OutcomeChangeIsGuarded(outcomeOf, outcomeOf')
  /\ UNCHANGED << admission, generation, waiters, cancelRequested, reserved,
                  disposed, groupActive, groupQuiescent, leaseReleased,
                  publishSafetyWitness, leaseSafetyWitness,
                  authorizedOutcomeWitness, drainingCancellationWitness >>

AttachedDemandCancels(d) ==
  /\ d \in cancelRequested
  /\ d \in waiters
  /\ admission \in {"InFlight", "Draining"}
  /\ admission = "InFlight" \/ EnableDrainingCancellation
  /\ waiters' = waiters \ {d}
  /\ admission' =
       IF admission = "InFlight" /\ waiters \ {d} = {}
       THEN "Draining"
       ELSE admission
  /\ pendingGeneration' = [pendingGeneration EXCEPT ![d] = NoGeneration]
  /\ outcomeOf' = [outcomeOf EXCEPT ![d] = "cancelled"]
  /\ drainingCancellationWitness' =
       IF admission = "Draining"
       THEN drainingCancellationWitness \cup {d}
       ELSE drainingCancellationWitness
  /\ outcomeStableWitness' =
       outcomeStableWitness /\ OutcomeChangeIsGuarded(outcomeOf, outcomeOf')
  /\ UNCHANGED << generation, cancelRequested, reserved, disposed,
                  groupActive, groupQuiescent, leaseReleased,
                  publishSafetyWitness, leaseSafetyWitness,
                  authorizedOutcomeWitness, pendingCancellationWitness >>

(***************************************************************************)
(* Disposal closes admission to new demands and forces any in-flight       *)
(* operation into the draining state so its eventual result cannot publish.*)
(***************************************************************************)
DisposalBegins ==
  /\ disposed = FALSE
  /\ disposed' = TRUE
  /\ admission' = IF admission = "InFlight" THEN "Draining" ELSE admission
  /\ UNCHANGED << generation, waiters, pendingGeneration, cancelRequested,
                  reserved, outcomeOf, groupActive, groupQuiescent,
                  leaseReleased, publishSafetyWitness, leaseSafetyWitness,
                  outcomeStableWitness, authorizedOutcomeWitness,
                  pendingCancellationWitness, drainingCancellationWitness >>

(***************************************************************************)
(* Adapter completion. A successful result publishes only from "InFlight", *)
(* which is reachable only while not disposed (AdmissionCoherence plus     *)
(* DisposalBegins together rule out "InFlight" while disposed). Draining   *)
(* covers both voluntary drain and disposal-forced drain, and never        *)
(* publishes: its result is discarded as a late/cancelled outcome instead. *)
(* Each action re-derives, from the pre-step `waiters`, that only demands  *)
(* attached to the admission receive its outcome.                         *)
(***************************************************************************)
AdapterSucceeds ==
  /\ admission = "InFlight"
  /\ groupActive = FALSE
  /\ waiters \cap cancelRequested = {}
  /\ admission' = "Idle"
  /\ generation' = NoGeneration
  /\ reserved' = FALSE
  /\ outcomeOf' = [d \in Demands |-> IF d \in waiters THEN "published" ELSE outcomeOf[d]]
  /\ outcomeStableWitness' = (outcomeStableWitness /\ OutcomeChangeIsGuarded(outcomeOf, outcomeOf'))
  /\ authorizedOutcomeWitness' = (authorizedOutcomeWitness /\ ResolvedOnlyAttachedDemands(waiters, outcomeOf, outcomeOf'))
  /\ waiters' = {}
  /\ groupActive' = TRUE
  /\ groupQuiescent' = FALSE
  /\ leaseReleased' = FALSE
  /\ publishSafetyWitness' = (publishSafetyWitness /\ (disposed = FALSE))
  /\ UNCHANGED << pendingGeneration, cancelRequested, disposed,
                  leaseSafetyWitness, pendingCancellationWitness,
                  drainingCancellationWitness >>

AdapterFails ==
  /\ admission = "InFlight"
  /\ waiters \cap cancelRequested = {}
  /\ admission' = "Idle"
  /\ generation' = NoGeneration
  /\ reserved' = FALSE
  /\ outcomeOf' = [d \in Demands |-> IF d \in waiters THEN "failed" ELSE outcomeOf[d]]
  /\ outcomeStableWitness' = (outcomeStableWitness /\ OutcomeChangeIsGuarded(outcomeOf, outcomeOf'))
  /\ authorizedOutcomeWitness' = (authorizedOutcomeWitness /\ ResolvedOnlyAttachedDemands(waiters, outcomeOf, outcomeOf'))
  /\ waiters' = {}
  /\ UNCHANGED << pendingGeneration, cancelRequested, disposed, groupActive,
                  groupQuiescent, leaseReleased, publishSafetyWitness,
                  leaseSafetyWitness, pendingCancellationWitness,
                  drainingCancellationWitness >>

AdapterDrains ==
  /\ admission = "Draining"
  /\ waiters \cap cancelRequested = {}
  /\ admission' = "Idle"
  /\ generation' = NoGeneration
  /\ reserved' = FALSE
  /\ outcomeOf' = [d \in Demands |-> IF d \in waiters THEN "stale" ELSE outcomeOf[d]]
  /\ outcomeStableWitness' = (outcomeStableWitness /\ OutcomeChangeIsGuarded(outcomeOf, outcomeOf'))
  /\ authorizedOutcomeWitness' = (authorizedOutcomeWitness /\ ResolvedOnlyAttachedDemands(waiters, outcomeOf, outcomeOf'))
  /\ waiters' = {}
  /\ UNCHANGED << pendingGeneration, cancelRequested, disposed, groupActive,
                  groupQuiescent, leaseReleased, publishSafetyWitness,
                  leaseSafetyWitness, pendingCancellationWitness,
                  drainingCancellationWitness >>

(***************************************************************************)
(* Group quiescence and lease release. Disposal disposes published groups; *)
(* their artifact leases outlive `Dispose()` and release only once the     *)
(* dependent group reports quiescence, so release is part of the disposal  *)
(* cleanup path, not an ordinary-operation event. `ReleaseLeases` is       *)
(* therefore gated on disposal having begun as well as on quiescence.      *)
(***************************************************************************)
GroupBecomesQuiescent ==
  /\ groupActive = TRUE
  /\ groupQuiescent = FALSE
  /\ groupQuiescent' = TRUE
  /\ UNCHANGED << admission, generation, waiters, pendingGeneration,
                  cancelRequested, reserved, disposed, outcomeOf, groupActive,
                  leaseReleased, publishSafetyWitness, leaseSafetyWitness,
                  outcomeStableWitness, authorizedOutcomeWitness,
                  pendingCancellationWitness, drainingCancellationWitness >>

ReleaseLeases ==
  /\ groupActive = TRUE
  /\ groupQuiescent = TRUE
  /\ disposed = TRUE
  /\ leaseReleased' = TRUE
  /\ groupActive' = FALSE
  /\ leaseSafetyWitness' = (leaseSafetyWitness /\ (groupQuiescent = TRUE) /\ (disposed = TRUE))
  /\ UNCHANGED << admission, generation, waiters, pendingGeneration,
                  cancelRequested, reserved, disposed, outcomeOf,
                  groupQuiescent, publishSafetyWitness, outcomeStableWitness,
                  authorizedOutcomeWitness, pendingCancellationWitness,
                  drainingCancellationWitness >>

Next ==
  \/ \E d \in Demands, g \in Generations : DemandArrives(d, g)
  \/ \E d \in Demands : DemandStartsAdmission(d)
  \/ \E d \in Demands : DemandJoinsAdmission(d)
  \/ \E d \in Demands : DemandRejectedWhileDisposed(d)
  \/ \E d \in Demands : CallerRequestsCancellation(d)
  \/ \E d \in Demands : PendingDemandCancels(d)
  \/ \E d \in Demands : AttachedDemandCancels(d)
  \/ DisposalBegins
  \/ AdapterSucceeds
  \/ AdapterFails
  \/ AdapterDrains
  \/ GroupBecomesQuiescent
  \/ ReleaseLeases

\* Per-demand fairness on admission start/join/rejection avoids one demand
\* starving another when several are perpetually eligible to act, and
\* ensures a demand pending under disposal is eventually rejected rather
\* than left unresolved.
Fairness ==
  /\ WF_vars(AdapterSucceeds)
  /\ WF_vars(AdapterFails)
  /\ WF_vars(AdapterDrains)
  /\ WF_vars(GroupBecomesQuiescent)
  /\ WF_vars(ReleaseLeases)
  /\ \A d \in Demands : WF_vars(DemandStartsAdmission(d))
  /\ \A d \in Demands : WF_vars(DemandJoinsAdmission(d))
  /\ \A d \in Demands : WF_vars(DemandRejectedWhileDisposed(d))
  /\ \A d \in Demands : WF_vars(PendingDemandCancels(d))
  /\ \A d \in Demands : WF_vars(AttachedDemandCancels(d))

Spec == Init /\ [][Next]_vars /\ Fairness

(***************************************************************************)
(* Safety.                                                                 *)
(***************************************************************************)

\* The headline property: disposal always wins the race. An admission can
\* never be "InFlight" (the only state from which a result publishes) once
\* disposal has begun.
DisposalPreventsPublication == disposed => admission # "InFlight"

\* Re-derived independently of AdapterSucceeds's own guard.
PublishSafetyWitnessHolds == publishSafetyWitness

\* Re-derived independently of ReleaseLeases's own guard.
LeaseSafetyWitnessHolds == leaseSafetyWitness

\* Re-derived independently of every action that writes outcomeOf: no
\* action ever overwrites a demand's already-terminal outcome.
OutcomeStableWitnessHolds == outcomeStableWitness

\* Re-derived independently of AdapterSucceeds/Fails/Drains: only a demand
\* attached to the admission immediately beforehand can be told published,
\* failed, or stale.
AuthorizedOutcomeWitnessHolds == authorizedOutcomeWitness

\* An attached demand always requested the admitted generation.
WaiterGenerationInvariant == WaiterGenerationMatches

\* A recorded cancellation request either still awaits owner processing or
\* has reached the one terminal outcome that request may produce.
CancellationRequestCoherence ==
  \A d \in cancelRequested :
    outcomeOf[d] \in {"none", "cancelled"}

\* Cancellation removes both pending and attached eligibility permanently.
CancelledDemandsAreDetached ==
  \A d \in Demands :
    outcomeOf[d] = "cancelled"
      => /\ pendingGeneration[d] = NoGeneration
         /\ d \notin waiters

\* Independent witnesses prove both newly modeled cancellation paths preserve
\* the terminal detached state at the step that takes each path.
PendingCancellationWitnessHolds ==
  \A d \in pendingCancellationWitness :
    /\ outcomeOf[d] = "cancelled"
    /\ pendingGeneration[d] = NoGeneration
    /\ d \notin waiters

DrainingCancellationWitnessHolds ==
  \A d \in drainingCancellationWitness :
    /\ outcomeOf[d] = "cancelled"
    /\ pendingGeneration[d] = NoGeneration
    /\ d \notin waiters

\* Reachability probes negate these predicates in dedicated configurations.
PendingCancellationReached == pendingCancellationWitness # {}
DrainingCancellationReached == drainingCancellationWitness # {}
PendingCancellationNotReached == ~PendingCancellationReached
DrainingCancellationNotReached == ~DrainingCancellationReached

(***************************************************************************)
(* Liveness.                                                               *)
(***************************************************************************)

\* Every admission attempt eventually reaches a terminal (idle) state; the
\* adapter cannot stay in flight or draining forever.
EveryAdmissionEventuallyTerminates == (admission # "Idle") ~> (admission = "Idle")

\* A demand that is currently attached to the admission eventually receives
\* a terminal outcome.
WaitingDemandsEventuallyResolve ==
  \A d \in Demands : (d \in waiters) ~> (outcomeOf[d] # "none")

\* A demand blocked on an incompatible generation (or a draining operation)
\* eventually either attaches to a later-compatible admission or resolves.
PendingDemandsEventuallyAttachOrResolve ==
  \A d \in Demands :
    (pendingGeneration[d] # NoGeneration /\ outcomeOf[d] = "none")
      ~> (d \in waiters \/ outcomeOf[d] # "none")

\* Once a caller records cancellation under the owner gate, that demand
\* eventually reaches cancelled whether it was pending, in flight, or
\* overtaken by disposal into draining.
CancellationRequestsEventuallyCancel ==
  \A d \in Demands :
    (d \in cancelRequested /\ outcomeOf[d] = "none")
      ~> (outcomeOf[d] = "cancelled")

\* Once disposal begins and a group is still active, its leases eventually
\* release; lease release is scoped to the disposal cleanup path.
DisposalEventuallyReleasesLeases == (disposed /\ groupActive) ~> leaseReleased

=============================================================================
