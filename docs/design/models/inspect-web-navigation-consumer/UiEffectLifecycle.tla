------------------------- MODULE UiEffectLifecycle -------------------------
EXTENDS Naturals, FiniteSets, TLC

\* This model owns only the Inspect Web consumer lifecycle after the product
\* navigation session has accepted an intent. Product snapshot construction,
\* reconciliation, supersession, and facet recommendation remain abstract.

CONSTANTS
  MaxIntents,
  MaxSurfaceEpoch,
  EnforceCurrentEffect,
  EnforceCompleteAcknowledge,
  EnforceDestroyAbandon,
  EnforceInstallFirst

ASSUME MaxIntents >= 2
ASSUME MaxSurfaceEpoch >= 2
ASSUME EnforceCurrentEffect \in BOOLEAN
ASSUME EnforceCompleteAcknowledge \in BOOLEAN
ASSUME EnforceDestroyAbandon \in BOOLEAN
ASSUME EnforceInstallFirst \in BOOLEAN

Tokens == 1..MaxIntents
Statuses == {"unused", "inFlight", "returned", "acknowledged", "abandoned"}
Outcomes ==
  {"none",
   "applied",
   "unavailableWithSnapshot",
   "unavailableWithoutSnapshot",
   "rejected",
   "failed",
   "superseded"}
Effects == {"install", "focus", "announce"}
SnapshotOutcomes == {"applied", "unavailableWithSnapshot"}
TerminalStatuses == {"acknowledged", "abandoned"}

RequiredEffects(result) ==
  IF result = "superseded"
  THEN {}
  ELSE IF result \in SnapshotOutcomes
  THEN Effects
  ELSE {"focus", "announce"}

VARIABLES
  currentIntent,
  nextIntent,
  mounted,
  surfaceEpoch,
  operationSurface,
  status,
  outcome,
  required,
  completed,
  effectWitness,
  acknowledgeWitness,
  destructionWitness,
  orderingWitness

vars ==
  << currentIntent,
     nextIntent,
     mounted,
     surfaceEpoch,
     operationSurface,
     status,
     outcome,
     required,
     completed,
     effectWitness,
     acknowledgeWitness,
     destructionWitness,
     orderingWitness >>

Init ==
  /\ currentIntent = 0
  /\ nextIntent = 0
  /\ mounted = TRUE
  /\ surfaceEpoch = 1
  /\ operationSurface = [i \in Tokens |-> 0]
  /\ status = [i \in Tokens |-> "unused"]
  /\ outcome = [i \in Tokens |-> "none"]
  /\ required = [i \in Tokens |-> {}]
  /\ completed = [i \in Tokens |-> {}]
  /\ effectWitness = TRUE
  /\ acknowledgeWitness = TRUE
  /\ destructionWitness = TRUE
  /\ orderingWitness = TRUE

CurrentAuthority(i) ==
  /\ mounted
  /\ status[i] = "returned"
  /\ i = currentIntent
  /\ operationSurface[i] = surfaceEpoch

BeginIntent ==
  /\ mounted
  /\ nextIntent < MaxIntents
  /\ LET i == nextIntent + 1 IN
     /\ currentIntent' = i
     /\ nextIntent' = i
     /\ operationSurface' = [operationSurface EXCEPT ![i] = surfaceEpoch]
     /\ status' = [status EXCEPT ![i] = "inFlight"]
  /\ UNCHANGED << mounted,
                  surfaceEpoch,
                  outcome,
                  required,
                  completed,
                  effectWitness,
                  acknowledgeWitness,
                  destructionWitness,
                  orderingWitness >>

ReturnResult(i, result) ==
  /\ i \in Tokens
  /\ result \in Outcomes \ {"none"}
  /\ status[i] = "inFlight"
  /\ (result = "superseded" => i < currentIntent)
  /\ status' = [status EXCEPT ![i] = "returned"]
  /\ outcome' = [outcome EXCEPT ![i] = result]
  /\ required' = [required EXCEPT ![i] = RequiredEffects(result)]
  /\ completed' = [completed EXCEPT ![i] = {}]
  /\ UNCHANGED << currentIntent,
                  nextIntent,
                  mounted,
                  surfaceEpoch,
                  operationSurface,
                  effectWitness,
                  acknowledgeWitness,
                  destructionWitness,
                  orderingWitness >>

ReturnAnyResult(i) ==
  \E result \in Outcomes \ {"none"} : ReturnResult(i, result)

RunEffect(i, effect) ==
  /\ i \in Tokens
  /\ effect \in required[i] \ completed[i]
  /\ IF EnforceCurrentEffect THEN CurrentAuthority(i) ELSE TRUE
  /\ IF EnforceInstallFirst
     THEN
       \/ effect = "install"
       \/ "install" \notin required[i]
       \/ "install" \in completed[i]
     ELSE TRUE
  /\ completed' = [completed EXCEPT ![i] = @ \cup {effect}]
  /\ effectWitness' =
       (effectWitness
        /\ CurrentAuthority(i)
        /\ effect \in required[i]
        /\ effect \notin completed[i])
  /\ orderingWitness' =
       (orderingWitness
        /\ (effect = "install"
            \/ "install" \notin required[i]
            \/ "install" \in completed[i]))
  /\ UNCHANGED << currentIntent,
                  nextIntent,
                  mounted,
                  surfaceEpoch,
                  operationSurface,
                  status,
                  outcome,
                  required,
                  acknowledgeWitness,
                  destructionWitness >>

Acknowledge(i) ==
  /\ i \in Tokens
  /\ status[i] = "returned"
  /\ CurrentAuthority(i)
  /\ IF EnforceCompleteAcknowledge
     THEN completed[i] = required[i]
     ELSE TRUE
  /\ status' = [status EXCEPT ![i] = "acknowledged"]
  /\ acknowledgeWitness' =
       (acknowledgeWitness /\ (completed[i] = required[i]))
  /\ UNCHANGED << currentIntent,
                  nextIntent,
                  mounted,
                  surfaceEpoch,
                  operationSurface,
                  outcome,
                  required,
                  completed,
                  effectWitness,
                  destructionWitness,
                  orderingWitness >>

AbandonStale(i) ==
  /\ i \in Tokens
  /\ status[i] = "returned"
  /\ ~CurrentAuthority(i)
  /\ status' = [status EXCEPT ![i] = "abandoned"]
  /\ UNCHANGED << currentIntent,
                  nextIntent,
                  mounted,
                  surfaceEpoch,
                  operationSurface,
                  outcome,
                  required,
                  completed,
                  effectWitness,
                  acknowledgeWitness,
                  destructionWitness,
                  orderingWitness >>

DestroySurface ==
  /\ mounted
  /\ mounted' = FALSE
  /\ status' =
       IF EnforceDestroyAbandon
       THEN
         [i \in Tokens |->
            IF status[i] = "returned" THEN "abandoned" ELSE status[i]]
       ELSE status
  /\ destructionWitness' =
       (destructionWitness
        /\ \A i \in Tokens : status'[i] # "returned")
  /\ UNCHANGED << currentIntent,
                  nextIntent,
                  surfaceEpoch,
                  operationSurface,
                  outcome,
                  required,
                  completed,
                  effectWitness,
                  acknowledgeWitness,
                  orderingWitness >>

MountSurface ==
  /\ ~mounted
  /\ surfaceEpoch < MaxSurfaceEpoch
  /\ mounted' = TRUE
  /\ surfaceEpoch' = surfaceEpoch + 1
  /\ UNCHANGED << currentIntent,
                  nextIntent,
                  operationSurface,
                  status,
                  outcome,
                  required,
                  completed,
                  effectWitness,
                  acknowledgeWitness,
                  destructionWitness,
                  orderingWitness >>

Next ==
  \/ BeginIntent
  \/ \E i \in Tokens : ReturnAnyResult(i)
  \/ \E i \in Tokens, effect \in Effects : RunEffect(i, effect)
  \/ \E i \in Tokens : Acknowledge(i)
  \/ \E i \in Tokens : AbandonStale(i)
  \/ DestroySurface
  \/ MountSurface

TypeOK ==
  /\ currentIntent \in 0..MaxIntents
  /\ nextIntent \in 0..MaxIntents
  /\ currentIntent <= nextIntent
  /\ mounted \in BOOLEAN
  /\ surfaceEpoch \in 1..MaxSurfaceEpoch
  /\ operationSurface \in [Tokens -> Nat]
  /\ status \in [Tokens -> Statuses]
  /\ outcome \in [Tokens -> Outcomes]
  /\ required \in [Tokens -> SUBSET Effects]
  /\ completed \in [Tokens -> SUBSET Effects]
  /\ effectWitness \in BOOLEAN
  /\ acknowledgeWitness \in BOOLEAN
  /\ destructionWitness \in BOOLEAN
  /\ orderingWitness \in BOOLEAN

ReturnedShape ==
  \A i \in Tokens :
    /\ (status[i] = "unused" => outcome[i] = "none")
    /\ (status[i] = "inFlight" => outcome[i] = "none")
    /\ (status[i] \in {"returned"} \cup TerminalStatuses =>
          /\ outcome[i] # "none"
          /\ required[i] = RequiredEffects(outcome[i])
          /\ completed[i] \subseteq required[i])

NoUnauthorizedVisibleEffect == effectWitness

AcknowledgeOnlyAfterEffects == acknowledgeWitness

DestroyAbandonsReturnedAuthority == destructionWitness

SnapshotInstallsBeforeDependentEffects == orderingWitness

EveryReturnedAuthoritySettles ==
  \A i \in Tokens :
    status[i] = "returned" ~> status[i] \in TerminalStatuses

EverySubmittedIntentSettles ==
  \A i \in Tokens :
    status[i] = "inFlight" ~> status[i] \in TerminalStatuses

Fairness ==
  /\ \A i \in Tokens : WF_vars(ReturnAnyResult(i))
  /\ \A i \in Tokens, effect \in Effects : WF_vars(RunEffect(i, effect))
  /\ \A i \in Tokens : WF_vars(Acknowledge(i))
  /\ \A i \in Tokens : WF_vars(AbandonStale(i))

Spec == Init /\ [][Next]_vars /\ Fairness

=============================================================================
