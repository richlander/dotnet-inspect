---------------------- MODULE ArtifactSessionAdmission ----------------------
(***************************************************************************)
(* Design model of `ArtifactSetSession` admission, described in            *)
(* `docs/design/artifact-acquisition-and-workspaces.md`.                   *)
(*                                                                         *)
(* The model checks single-flight admission across concurrent demands,     *)
(* an incompatible-generation demand's inability to join or start          *)
(* duplicate work while a prior admission is active, voluntary and         *)
(* disposal-forced cancellation, and the rule that a late adapter result   *)
(* never publishes a session or group once disposal has begun. It says     *)
(* nothing about which adapter runs, budget arithmetic, content identity,  *)
(* assembly projection, or query-lease authorization.                     *)
(*                                                                         *)
(* Product concept                          Model variable                 *)
(*   admission operation lifecycle           admission                    *)
(*   admitted context+policy generation      generation                   *)
(*   demands attached to the operation       waiters                      *)
(*   a demand's requested generation         pendingGeneration             *)
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
  Generations   \* finite set of distinct (context, policy) generations

ASSUME Cardinality(Demands) >= 2
ASSUME Cardinality(Generations) >= 2

VARIABLES
  admission,
  generation,
  waiters,
  pendingGeneration,
  reserved,
  disposed,
  outcomeOf,
  groupActive,
  groupQuiescent,
  leaseReleased,
  publishSafetyWitness,
  leaseSafetyWitness,
  outcomeStableWitness,
  authorizedOutcomeWitness

vars == << admission, generation, waiters, pendingGeneration, reserved,
           disposed, outcomeOf, groupActive, groupQuiescent, leaseReleased,
           publishSafetyWitness, leaseSafetyWitness, outcomeStableWitness,
           authorizedOutcomeWitness >>

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
  /\ UNCHANGED << admission, generation, waiters, reserved, disposed,
                  outcomeOf, groupActive, groupQuiescent, leaseReleased,
                  publishSafetyWitness, leaseSafetyWitness,
                  outcomeStableWitness, authorizedOutcomeWitness >>

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
  /\ admission' = "InFlight"
  /\ generation' = pendingGeneration[d]
  /\ waiters' = {d}
  /\ reserved' = TRUE
  /\ UNCHANGED << pendingGeneration, disposed, outcomeOf, groupActive,
                  groupQuiescent, leaseReleased, publishSafetyWitness,
                  leaseSafetyWitness, outcomeStableWitness,
                  authorizedOutcomeWitness >>

DemandJoinsAdmission(d) ==
  /\ admission = "InFlight"
  /\ pendingGeneration[d] = generation
  /\ outcomeOf[d] = "none"
  /\ d \notin waiters
  /\ waiters' = waiters \cup {d}
  /\ UNCHANGED << admission, generation, pendingGeneration, reserved,
                  disposed, outcomeOf, groupActive, groupQuiescent,
                  leaseReleased, publishSafetyWitness, leaseSafetyWitness,
                  outcomeStableWitness, authorizedOutcomeWitness >>

DemandRejectedWhileDisposed(d) ==
  /\ disposed = TRUE
  /\ pendingGeneration[d] # NoGeneration
  /\ d \notin waiters
  /\ outcomeOf[d] = "none"
  /\ outcomeOf' = [outcomeOf EXCEPT ![d] = "rejected"]
  /\ outcomeStableWitness' = (outcomeStableWitness /\ OutcomeChangeIsGuarded(outcomeOf, outcomeOf'))
  /\ UNCHANGED << admission, generation, waiters, pendingGeneration, reserved,
                  disposed, groupActive, groupQuiescent, leaseReleased,
                  publishSafetyWitness, leaseSafetyWitness,
                  authorizedOutcomeWitness >>

(***************************************************************************)
(* Voluntary cancellation. Detaching the last waiter asks the owner to     *)
(* cancel the adapter and enter the draining state, which never publishes. *)
(***************************************************************************)
WaiterCancels(d) ==
  /\ admission = "InFlight"
  /\ d \in waiters
  /\ waiters' = waiters \ {d}
  /\ admission' = IF waiters \ {d} = {} THEN "Draining" ELSE "InFlight"
  /\ outcomeOf' = [outcomeOf EXCEPT ![d] = "cancelled"]
  /\ outcomeStableWitness' = (outcomeStableWitness /\ OutcomeChangeIsGuarded(outcomeOf, outcomeOf'))
  /\ UNCHANGED << generation, pendingGeneration, reserved, disposed,
                  groupActive, groupQuiescent, leaseReleased,
                  publishSafetyWitness, leaseSafetyWitness,
                  authorizedOutcomeWitness >>

(***************************************************************************)
(* Disposal closes admission to new demands and forces any in-flight       *)
(* operation into the draining state so its eventual result cannot publish.*)
(***************************************************************************)
DisposalBegins ==
  /\ disposed = FALSE
  /\ disposed' = TRUE
  /\ admission' = IF admission = "InFlight" THEN "Draining" ELSE admission
  /\ UNCHANGED << generation, waiters, pendingGeneration, reserved, outcomeOf,
                  groupActive, groupQuiescent, leaseReleased,
                  publishSafetyWitness, leaseSafetyWitness,
                  outcomeStableWitness, authorizedOutcomeWitness >>

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
  /\ UNCHANGED << pendingGeneration, disposed, leaseSafetyWitness >>

AdapterFails ==
  /\ admission = "InFlight"
  /\ admission' = "Idle"
  /\ generation' = NoGeneration
  /\ reserved' = FALSE
  /\ outcomeOf' = [d \in Demands |-> IF d \in waiters THEN "failed" ELSE outcomeOf[d]]
  /\ outcomeStableWitness' = (outcomeStableWitness /\ OutcomeChangeIsGuarded(outcomeOf, outcomeOf'))
  /\ authorizedOutcomeWitness' = (authorizedOutcomeWitness /\ ResolvedOnlyAttachedDemands(waiters, outcomeOf, outcomeOf'))
  /\ waiters' = {}
  /\ UNCHANGED << pendingGeneration, disposed, groupActive, groupQuiescent,
                  leaseReleased, publishSafetyWitness, leaseSafetyWitness >>

AdapterDrains ==
  /\ admission = "Draining"
  /\ admission' = "Idle"
  /\ generation' = NoGeneration
  /\ reserved' = FALSE
  /\ outcomeOf' = [d \in Demands |-> IF d \in waiters THEN "stale" ELSE outcomeOf[d]]
  /\ outcomeStableWitness' = (outcomeStableWitness /\ OutcomeChangeIsGuarded(outcomeOf, outcomeOf'))
  /\ authorizedOutcomeWitness' = (authorizedOutcomeWitness /\ ResolvedOnlyAttachedDemands(waiters, outcomeOf, outcomeOf'))
  /\ waiters' = {}
  /\ UNCHANGED << pendingGeneration, disposed, groupActive, groupQuiescent,
                  leaseReleased, publishSafetyWitness, leaseSafetyWitness >>

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
  /\ UNCHANGED << admission, generation, waiters, pendingGeneration, reserved,
                  disposed, outcomeOf, groupActive, leaseReleased,
                  publishSafetyWitness, leaseSafetyWitness,
                  outcomeStableWitness, authorizedOutcomeWitness >>

ReleaseLeases ==
  /\ groupActive = TRUE
  /\ groupQuiescent = TRUE
  /\ disposed = TRUE
  /\ leaseReleased' = TRUE
  /\ groupActive' = FALSE
  /\ leaseSafetyWitness' = (leaseSafetyWitness /\ (groupQuiescent = TRUE) /\ (disposed = TRUE))
  /\ UNCHANGED << admission, generation, waiters, pendingGeneration, reserved,
                  disposed, outcomeOf, groupQuiescent, publishSafetyWitness,
                  outcomeStableWitness, authorizedOutcomeWitness >>

Next ==
  \/ \E d \in Demands, g \in Generations : DemandArrives(d, g)
  \/ \E d \in Demands : DemandStartsAdmission(d)
  \/ \E d \in Demands : DemandJoinsAdmission(d)
  \/ \E d \in Demands : DemandRejectedWhileDisposed(d)
  \/ \E d \in Demands : WaiterCancels(d)
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

\* Once disposal begins and a group is still active, its leases eventually
\* release; lease release is scoped to the disposal cleanup path.
DisposalEventuallyReleasesLeases == (disposed /\ groupActive) ~> leaseReleased

=============================================================================
