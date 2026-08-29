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
  EnforceInstallFirst,
  EnforcePersistentFocus,
  EnforceDeferredFocus,
  EnforceReplacementAbandon

ASSUME MaxIntents >= 2
ASSUME MaxSurfaceEpoch >= 2
ASSUME EnforceCurrentEffect \in BOOLEAN
ASSUME EnforceCompleteAcknowledge \in BOOLEAN
ASSUME EnforceDestroyAbandon \in BOOLEAN
ASSUME EnforceInstallFirst \in BOOLEAN
ASSUME EnforcePersistentFocus \in BOOLEAN
ASSUME EnforceDeferredFocus \in BOOLEAN
ASSUME EnforceReplacementAbandon \in BOOLEAN

Tokens == 1..MaxIntents
Statuses ==
  {"unused", "inFlight", "returned", "acknowledged", "abandoned", "discarded"}
Outcomes ==
  {"none",
   "applied",
   "unavailableWithSnapshot",
   "unavailableWithoutSnapshot",
   "rejected",
   "failed",
   "aborted"}
Effects == {"install", "focus", "announce"}
FocusLocations == {"shell", "surface", "body"}
SnapshotOutcomes == {"applied", "unavailableWithSnapshot"}
AuthorityTerminalStatuses == {"acknowledged", "abandoned"}
OperationTerminalStatuses == AuthorityTerminalStatuses \cup {"discarded"}

RequiredEffects(result) ==
  IF result \in SnapshotOutcomes
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
  focusLocation,
  focusSurface,
  effectWitness,
  acknowledgeWitness,
  destructionWitness,
  orderingWitness,
  deferredFocusWitness,
  replacementWitness

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
     focusLocation,
     focusSurface,
     effectWitness,
     acknowledgeWitness,
     destructionWitness,
     orderingWitness,
     deferredFocusWitness,
     replacementWitness >>

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
  /\ focusLocation = "surface"
  /\ focusSurface = 1
  /\ effectWitness = TRUE
  /\ acknowledgeWitness = TRUE
  /\ destructionWitness = TRUE
  /\ orderingWitness = TRUE
  /\ deferredFocusWitness = TRUE
  /\ replacementWitness = TRUE

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
     /\ focusLocation' =
          IF EnforcePersistentFocus THEN "shell" ELSE focusLocation
     /\ focusSurface' =
          IF EnforcePersistentFocus THEN 0 ELSE focusSurface
  /\ UNCHANGED << mounted,
                  surfaceEpoch,
                  outcome,
                  required,
                  completed,
                  effectWitness,
                  acknowledgeWitness,
                  destructionWitness,
                  orderingWitness,
                  deferredFocusWitness,
                  replacementWitness >>

ReturnResult(i, result) ==
  /\ i \in Tokens
  /\ result \in Outcomes \ {"none"}
  /\ status[i] = "inFlight"
  /\ i = currentIntent
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
                  orderingWitness,
                  deferredFocusWitness,
                  replacementWitness,
                  focusLocation,
                  focusSurface >>

ReturnAnyResult(i) ==
  \E result \in Outcomes \ {"none"} : ReturnResult(i, result)

DiscardSuperseded(i) ==
  /\ i \in Tokens
  /\ status[i] = "inFlight"
  /\ i < currentIntent
  /\ status' = [status EXCEPT ![i] = "discarded"]
  /\ UNCHANGED << currentIntent,
                  nextIntent,
                  mounted,
                  surfaceEpoch,
                  operationSurface,
                  outcome,
                  required,
                  completed,
                  focusLocation,
                  focusSurface,
                  effectWitness,
                  acknowledgeWitness,
                  destructionWitness,
                  orderingWitness,
                  deferredFocusWitness,
                  replacementWitness >>

RunEffect(i, effect, replaceSurface) ==
  /\ i \in Tokens
  /\ effect \in required[i] \ completed[i]
  /\ replaceSurface \in BOOLEAN
  /\ (replaceSurface => effect = "install" /\ surfaceEpoch < MaxSurfaceEpoch)
  /\ IF EnforceCurrentEffect THEN CurrentAuthority(i) ELSE TRUE
  /\ IF EnforceInstallFirst
     THEN
       \/ effect = "install"
       \/ "install" \notin required[i]
       \/ "install" \in completed[i]
     ELSE TRUE
  /\ completed' = [completed EXCEPT ![i] = @ \cup {effect}]
  /\ status' =
       IF replaceSurface /\ EnforceReplacementAbandon
       THEN
         [j \in Tokens |->
            IF j # i
               /\ status[j] = "returned"
               /\ operationSurface[j] = surfaceEpoch
            THEN "abandoned"
            ELSE status[j]]
       ELSE status
  /\ surfaceEpoch' =
       IF replaceSurface THEN surfaceEpoch + 1 ELSE surfaceEpoch
  /\ operationSurface' =
       IF replaceSurface
       THEN [operationSurface EXCEPT ![i] = surfaceEpoch + 1]
       ELSE operationSurface
  /\ focusLocation' =
       CASE effect = "install" ->
              IF EnforceDeferredFocus THEN focusLocation ELSE "surface"
         [] effect = "focus" -> "surface"
         [] OTHER -> focusLocation
  /\ focusSurface' =
       CASE effect = "install" ->
              IF EnforceDeferredFocus
              THEN focusSurface
              ELSE IF replaceSurface THEN surfaceEpoch + 1 ELSE surfaceEpoch
         [] effect = "focus" -> surfaceEpoch
         [] OTHER -> focusSurface
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
  /\ deferredFocusWitness' =
       (deferredFocusWitness
        /\ (effect = "focus"
            \/ /\ focusLocation' = focusLocation
               /\ focusSurface' = focusSurface))
  /\ replacementWitness' =
       (replacementWitness
        /\ (~replaceSurface
            \/ \A j \in Tokens :
                 j = i
                 \/ operationSurface[j] # surfaceEpoch
                 \/ status'[j] # "returned"))
  /\ UNCHANGED << currentIntent,
                  nextIntent,
                  mounted,
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
                  focusLocation,
                  focusSurface,
                  effectWitness,
                  destructionWitness,
                  orderingWitness,
                  deferredFocusWitness,
                  replacementWitness >>

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
                  focusLocation,
                  focusSurface,
                  effectWitness,
                  acknowledgeWitness,
                  destructionWitness,
                  orderingWitness,
                  deferredFocusWitness,
                  replacementWitness >>

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
  /\ focusLocation' =
       IF focusLocation = "surface"
       THEN IF EnforcePersistentFocus THEN "shell" ELSE "body"
       ELSE focusLocation
  /\ focusSurface' =
       IF focusLocation = "surface" THEN 0 ELSE focusSurface
  /\ UNCHANGED << currentIntent,
                  nextIntent,
                  surfaceEpoch,
                  operationSurface,
                  outcome,
                  required,
                  completed,
                  effectWitness,
                  acknowledgeWitness,
                  orderingWitness,
                  deferredFocusWitness,
                  replacementWitness >>

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
                  focusLocation,
                  focusSurface,
                  effectWitness,
                  acknowledgeWitness,
                  destructionWitness,
                  orderingWitness,
                  deferredFocusWitness,
                  replacementWitness >>

Next ==
  \/ BeginIntent
  \/ \E i \in Tokens : ReturnAnyResult(i)
  \/ \E i \in Tokens : DiscardSuperseded(i)
  \/ \E i \in Tokens, effect \in Effects, replace \in BOOLEAN :
       RunEffect(i, effect, replace)
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
  /\ focusLocation \in FocusLocations
  /\ focusSurface \in 0..MaxSurfaceEpoch
  /\ effectWitness \in BOOLEAN
  /\ acknowledgeWitness \in BOOLEAN
  /\ destructionWitness \in BOOLEAN
  /\ orderingWitness \in BOOLEAN
  /\ deferredFocusWitness \in BOOLEAN
  /\ replacementWitness \in BOOLEAN

ReturnedShape ==
  \A i \in Tokens :
    /\ (status[i] = "unused" => outcome[i] = "none")
    /\ (status[i] = "inFlight" => outcome[i] = "none")
    /\ (status[i] = "discarded" =>
          /\ outcome[i] = "none"
          /\ required[i] = {}
          /\ completed[i] = {})
    /\ (status[i] \in {"returned"} \cup AuthorityTerminalStatuses =>
          /\ outcome[i] # "none"
          /\ required[i] = RequiredEffects(outcome[i])
          /\ completed[i] \subseteq required[i])

NoUnauthorizedVisibleEffect == effectWitness

AcknowledgeOnlyAfterEffects == acknowledgeWitness

DestroyAbandonsReturnedAuthority == destructionWitness

SnapshotInstallsBeforeDependentEffects == orderingWitness

DeferredFocusRunsOnlyInFocusEffect == deferredFocusWitness

FocusRemainsOnMountedElement ==
  /\ focusLocation # "body"
  /\ (focusLocation = "shell"
      \/ (mounted /\ focusLocation = "surface" /\ focusSurface = surfaceEpoch))

ReplacementAbandonsOutgoingAuthority == replacementWitness

EveryReturnedAuthoritySettles ==
  \A i \in Tokens :
    status[i] = "returned" ~> status[i] \in AuthorityTerminalStatuses

EverySubmittedIntentSettles ==
  \A i \in Tokens :
    status[i] = "inFlight" ~> status[i] \in OperationTerminalStatuses

Fairness ==
  /\ \A i \in Tokens : WF_vars(ReturnAnyResult(i))
  /\ \A i \in Tokens : WF_vars(DiscardSuperseded(i))
  /\ \A i \in Tokens, effect \in Effects, replace \in BOOLEAN :
       WF_vars(RunEffect(i, effect, replace))
  /\ \A i \in Tokens : WF_vars(Acknowledge(i))
  /\ \A i \in Tokens : WF_vars(AbandonStale(i))

Spec == Init /\ [][Next]_vars /\ Fairness

=============================================================================
