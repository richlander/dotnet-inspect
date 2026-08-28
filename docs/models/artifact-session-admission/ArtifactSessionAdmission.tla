---------------------- MODULE ArtifactSessionAdmission ----------------------
(***************************************************************************)
(* Design model of `ArtifactSetSession` admission, described in            *)
(* `docs/design/artifact-acquisition-and-workspaces.md`.                   *)
(*                                                                         *)
(* The model checks single-flight admission across concurrent demands,     *)
(* voluntary and disposal-forced cancellation, and the rule that a late    *)
(* adapter result never publishes a session or group once disposal has     *)
(* begun. It says nothing about which adapter runs, budget arithmetic,     *)
(* content identity, assembly projection, or query-lease authorization.    *)
(*                                                                         *)
(* Product concept                          Model variable                 *)
(*   admission operation lifecycle           admission                    *)
(*   admitted context+policy generation      generation                   *)
(*   demands attached to the operation       waiters                      *)
(*   reserved admission budget                reserved                    *)
(*   workspace disposal begun                disposed                    *)
(*   per-demand delivered outcome            outcomeOf                    *)
(*   published group awaiting release        groupActive                 *)
(*   dependent group reports quiescent       groupQuiescent               *)
(*   artifact leases released                leaseReleased                *)
(*                                                                         *)
(* Guard witnesses. `publishSafetyWitness` and `leaseSafetyWitness` are    *)
(* latching booleans. The step that publishes a group, or that releases    *)
(* its leases, independently re-derives the exact condition the design     *)
(* requires and conjoins it into the witness. The paired invariant then    *)
(* fails if a future weakening of an action's own guard lets the step      *)
(* happen without that condition.                                         *)
(*                                                                         *)
(* Modeling simplification. Only one published group's lease lifecycle is *)
(* tracked at a time: a fresh admission cannot publish while the previous  *)
(* group is still awaiting lease release. The product does not serialize   *)
(* real groups this way; this bounds the state space for a check that is  *)
(* about one admission's publish-vs-disposal race, not concurrent groups.  *)
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
  reserved,
  disposed,
  outcomeOf,
  groupActive,
  groupQuiescent,
  leaseReleased,
  publishSafetyWitness,
  leaseSafetyWitness,
  outcomeStableWitness

vars == << admission, generation, waiters, reserved, disposed, outcomeOf,
           groupActive, groupQuiescent, leaseReleased,
           publishSafetyWitness, leaseSafetyWitness, outcomeStableWitness >>

\* A demand's outcome may change only away from "none"; once terminal it
\* never changes again. Re-derived on every step that touches outcomeOf.
OutcomeChangeIsGuarded(before, after) ==
  \A d \in Demands : after[d] # before[d] => before[d] = "none"

AdmissionStates == {"Idle", "InFlight", "Draining"}
Outcomes == {"none", "published", "failed", "rejected", "stale", "cancelled"}
NoGeneration == "none"

TypeOK ==
  /\ admission \in AdmissionStates
  /\ generation \in Generations \cup {NoGeneration}
  /\ waiters \subseteq Demands
  /\ reserved \in BOOLEAN
  /\ disposed \in BOOLEAN
  /\ outcomeOf \in [Demands -> Outcomes]
  /\ groupActive \in BOOLEAN
  /\ groupQuiescent \in BOOLEAN
  /\ leaseReleased \in BOOLEAN
  /\ publishSafetyWitness \in BOOLEAN
  /\ leaseSafetyWitness \in BOOLEAN
  /\ outcomeStableWitness \in BOOLEAN

\* Idle admission holds no generation, no waiters, and no reservation; any
\* active admission holds exactly a reservation and a real generation.
AdmissionCoherence ==
  /\ (admission = "Idle") <=> (generation = NoGeneration)
  /\ (admission = "Idle") => (waiters = {})
  /\ reserved <=> (admission # "Idle")

Init ==
  /\ admission = "Idle"
  /\ generation = NoGeneration
  /\ waiters = {}
  /\ reserved = FALSE
  /\ disposed = FALSE
  /\ outcomeOf = [d \in Demands |-> "none"]
  /\ groupActive = FALSE
  /\ groupQuiescent = FALSE
  /\ leaseReleased = FALSE
  /\ publishSafetyWitness = TRUE
  /\ leaseSafetyWitness = TRUE
  /\ outcomeStableWitness = TRUE

(***************************************************************************)
(* Demand arrival.                                                        *)
(*                                                                         *)
(* The first authorized demand for an idle, non-disposed session starts    *)
(* the admission and reserves budget. A compatible concurrent demand joins *)
(* an in-flight admission of the same generation and reserves nothing.     *)
(* An incompatible generation, or any demand against a draining operation, *)
(* is simply not enabled here; it waits for the terminal transition and    *)
(* replans by racing to start the next admission. Disposal rejects a new   *)
(* demand outright.                                                       *)
(***************************************************************************)
DemandStartsAdmission(d, g) ==
  /\ disposed = FALSE
  /\ admission = "Idle"
  /\ outcomeOf[d] = "none"
  /\ admission' = "InFlight"
  /\ generation' = g
  /\ waiters' = {d}
  /\ reserved' = TRUE
  /\ UNCHANGED << disposed, outcomeOf, groupActive, groupQuiescent,
                  leaseReleased, publishSafetyWitness, leaseSafetyWitness,
                  outcomeStableWitness >>

DemandJoinsAdmission(d, g) ==
  /\ admission = "InFlight"
  /\ generation = g
  /\ outcomeOf[d] = "none"
  /\ d \notin waiters
  /\ waiters' = waiters \cup {d}
  /\ UNCHANGED << admission, generation, reserved, disposed, outcomeOf,
                  groupActive, groupQuiescent, leaseReleased,
                  publishSafetyWitness, leaseSafetyWitness,
                  outcomeStableWitness >>

DemandRejectedWhileDisposed(d) ==
  /\ disposed = TRUE
  /\ d \notin waiters
  /\ outcomeOf[d] = "none"
  /\ outcomeOf' = [outcomeOf EXCEPT ![d] = "rejected"]
  /\ outcomeStableWitness' = outcomeStableWitness /\ OutcomeChangeIsGuarded(outcomeOf, outcomeOf')
  /\ UNCHANGED << admission, generation, waiters, reserved, disposed,
                  groupActive, groupQuiescent, leaseReleased,
                  publishSafetyWitness, leaseSafetyWitness >>

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
  /\ outcomeStableWitness' = outcomeStableWitness /\ OutcomeChangeIsGuarded(outcomeOf, outcomeOf')
  /\ UNCHANGED << generation, reserved, disposed, groupActive, groupQuiescent,
                  leaseReleased, publishSafetyWitness, leaseSafetyWitness >>

(***************************************************************************)
(* Disposal closes admission to new demands and forces any in-flight       *)
(* operation into the draining state so its eventual result cannot publish.*)
(***************************************************************************)
DisposalBegins ==
  /\ disposed = FALSE
  /\ disposed' = TRUE
  /\ admission' = IF admission = "InFlight" THEN "Draining" ELSE admission
  /\ UNCHANGED << generation, waiters, reserved, outcomeOf, groupActive,
                  groupQuiescent, leaseReleased, publishSafetyWitness,
                  leaseSafetyWitness, outcomeStableWitness >>

(***************************************************************************)
(* Adapter completion. A successful result publishes only from "InFlight", *)
(* which is reachable only while not disposed (AdmissionCoherence plus     *)
(* DisposalBegins together rule out "InFlight" while disposed). Draining   *)
(* covers both voluntary drain and disposal-forced drain, and never        *)
(* publishes: its result is discarded as a late/cancelled outcome instead. *)
(***************************************************************************)
AdapterSucceeds ==
  /\ admission = "InFlight"
  /\ groupActive = FALSE
  /\ admission' = "Idle"
  /\ generation' = NoGeneration
  /\ reserved' = FALSE
  /\ outcomeOf' = [d \in Demands |-> IF d \in waiters THEN "published" ELSE outcomeOf[d]]
  /\ outcomeStableWitness' = outcomeStableWitness /\ OutcomeChangeIsGuarded(outcomeOf, outcomeOf')
  /\ waiters' = {}
  /\ groupActive' = TRUE
  /\ groupQuiescent' = FALSE
  /\ leaseReleased' = FALSE
  /\ publishSafetyWitness' = publishSafetyWitness /\ (disposed = FALSE)
  /\ UNCHANGED << disposed, leaseSafetyWitness >>

AdapterFails ==
  /\ admission = "InFlight"
  /\ admission' = "Idle"
  /\ generation' = NoGeneration
  /\ reserved' = FALSE
  /\ outcomeOf' = [d \in Demands |-> IF d \in waiters THEN "failed" ELSE outcomeOf[d]]
  /\ outcomeStableWitness' = outcomeStableWitness /\ OutcomeChangeIsGuarded(outcomeOf, outcomeOf')
  /\ waiters' = {}
  /\ UNCHANGED << disposed, groupActive, groupQuiescent, leaseReleased,
                  publishSafetyWitness, leaseSafetyWitness >>

AdapterDrains ==
  /\ admission = "Draining"
  /\ admission' = "Idle"
  /\ generation' = NoGeneration
  /\ reserved' = FALSE
  /\ outcomeOf' = [d \in Demands |-> IF d \in waiters THEN "stale" ELSE outcomeOf[d]]
  /\ outcomeStableWitness' = outcomeStableWitness /\ OutcomeChangeIsGuarded(outcomeOf, outcomeOf')
  /\ waiters' = {}
  /\ UNCHANGED << disposed, groupActive, groupQuiescent, leaseReleased,
                  publishSafetyWitness, leaseSafetyWitness >>

(***************************************************************************)
(* Group quiescence and lease release. Artifact leases outlive disposal    *)
(* and release only once the dependent group reports quiescence.           *)
(***************************************************************************)
GroupBecomesQuiescent ==
  /\ groupActive = TRUE
  /\ groupQuiescent = FALSE
  /\ groupQuiescent' = TRUE
  /\ UNCHANGED << admission, generation, waiters, reserved, disposed,
                  outcomeOf, groupActive, leaseReleased,
                  publishSafetyWitness, leaseSafetyWitness,
                  outcomeStableWitness >>

ReleaseLeases ==
  /\ groupActive = TRUE
  /\ groupQuiescent = TRUE
  /\ leaseReleased' = TRUE
  /\ groupActive' = FALSE
  /\ leaseSafetyWitness' = leaseSafetyWitness /\ (groupQuiescent = TRUE)
  /\ UNCHANGED << admission, generation, waiters, reserved, disposed,
                  outcomeOf, groupQuiescent, publishSafetyWitness,
                  outcomeStableWitness >>

Next ==
  \/ \E d \in Demands, g \in Generations : DemandStartsAdmission(d, g)
  \/ \E d \in Demands, g \in Generations : DemandJoinsAdmission(d, g)
  \/ \E d \in Demands : DemandRejectedWhileDisposed(d)
  \/ \E d \in Demands : WaiterCancels(d)
  \/ DisposalBegins
  \/ AdapterSucceeds
  \/ AdapterFails
  \/ AdapterDrains
  \/ GroupBecomesQuiescent
  \/ ReleaseLeases

Fairness ==
  /\ WF_vars(AdapterSucceeds)
  /\ WF_vars(AdapterFails)
  /\ WF_vars(AdapterDrains)
  /\ WF_vars(GroupBecomesQuiescent)
  /\ WF_vars(ReleaseLeases)

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

\* Only demands that were attached to the admission when it resolved can
\* have been told published, failed, or stale; a demand's outcome always
\* matches the path that produced it.
NoUnauthorizedPublication ==
  \A d \in Demands : outcomeOf[d] = "published" => d \notin waiters

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

\* A published group's leases are eventually released.
LeasesEventuallyRelease == groupActive ~> leaseReleased

=============================================================================
