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
  EnforceReplacementAbandon,
  EnforceDispositionInstall,
  EnforceAbandonPreservesDebt,
  EnforceRemountSynchronization,
  EnforceDebtClearOnAcknowledge,
  EnforceRequestRetirement

ASSUME MaxIntents >= 2
ASSUME MaxSurfaceEpoch >= 2
ASSUME EnforceCurrentEffect \in BOOLEAN
ASSUME EnforceCompleteAcknowledge \in BOOLEAN
ASSUME EnforceDestroyAbandon \in BOOLEAN
ASSUME EnforceInstallFirst \in BOOLEAN
ASSUME EnforcePersistentFocus \in BOOLEAN
ASSUME EnforceDeferredFocus \in BOOLEAN
ASSUME EnforceReplacementAbandon \in BOOLEAN
ASSUME EnforceDispositionInstall \in BOOLEAN
ASSUME EnforceAbandonPreservesDebt \in BOOLEAN
ASSUME EnforceRemountSynchronization \in BOOLEAN
ASSUME EnforceDebtClearOnAcknowledge \in BOOLEAN
ASSUME EnforceRequestRetirement \in BOOLEAN

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
   "aborted",
   "synchronized"}
Dispositions == {"none", "current", "synchronizationRequired"}
Effects == {"install", "focus", "announce"}
FocusLocations == {"shell", "surface", "body"}
SnapshotOutcomes == {"applied", "unavailableWithSnapshot"}
AuthorityTerminalStatuses == {"acknowledged", "abandoned"}
OperationTerminalStatuses == AuthorityTerminalStatuses \cup {"discarded"}

OutcomeRequiredEffects(result) ==
  IF result \in SnapshotOutcomes
  THEN Effects
  ELSE {"focus", "announce"}

DispositionRequiredEffects(resultDisposition) ==
  IF resultDisposition = "synchronizationRequired"
  THEN Effects
  ELSE {"focus", "announce"}

AssignedRequiredEffects(result, resultDisposition) ==
  IF EnforceDispositionInstall
  THEN DispositionRequiredEffects(resultDisposition)
  ELSE OutcomeRequiredEffects(result)

ValidResultPair(result, resultDisposition) ==
  /\ result \in Outcomes \ {"none"}
  /\ resultDisposition \in Dispositions \ {"none"}
  /\ (result = "synchronized"
      => resultDisposition = "synchronizationRequired")

VARIABLES
  currentIntent,
  nextIntent,
  mounted,
  surfaceEpoch,
  operationSurface,
  status,
  outcome,
  disposition,
  required,
  completed,
  synchronizationDebt,
  synchronizationRequested,
  synchronizationRequestSurface,
  focusLocation,
  focusSurface,
  effectWitness,
  acknowledgeWitness,
  destructionWitness,
  orderingWitness,
  deferredFocusWitness,
  replacementWitness,
  dispositionWitness,
  abandonmentWitness,
  remountWitness,
  debtWitness

vars ==
  << currentIntent,
     nextIntent,
     mounted,
     surfaceEpoch,
     operationSurface,
     status,
     outcome,
     disposition,
     required,
     completed,
     synchronizationDebt,
     synchronizationRequested,
     synchronizationRequestSurface,
     focusLocation,
     focusSurface,
     effectWitness,
     acknowledgeWitness,
     destructionWitness,
     orderingWitness,
     deferredFocusWitness,
     replacementWitness,
     dispositionWitness,
     abandonmentWitness,
     remountWitness,
     debtWitness >>

Init ==
  /\ currentIntent = 0
  /\ nextIntent = 0
  /\ mounted = TRUE
  /\ surfaceEpoch = 1
  /\ operationSurface = [i \in Tokens |-> 0]
  /\ status = [i \in Tokens |-> "unused"]
  /\ outcome = [i \in Tokens |-> "none"]
  /\ disposition = [i \in Tokens |-> "none"]
  /\ required = [i \in Tokens |-> {}]
  /\ completed = [i \in Tokens |-> {}]
  /\ synchronizationDebt = FALSE
  /\ synchronizationRequested = FALSE
  /\ synchronizationRequestSurface = 0
  /\ focusLocation = "surface"
  /\ focusSurface = 1
  /\ effectWitness = TRUE
  /\ acknowledgeWitness = TRUE
  /\ destructionWitness = TRUE
  /\ orderingWitness = TRUE
  /\ deferredFocusWitness = TRUE
  /\ replacementWitness = TRUE
  /\ dispositionWitness = TRUE
  /\ abandonmentWitness = TRUE
  /\ remountWitness = TRUE
  /\ debtWitness = TRUE

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
                  disposition,
                  required,
                  completed,
                  synchronizationDebt,
                  synchronizationRequested,
                  synchronizationRequestSurface,
                  effectWitness,
                  acknowledgeWitness,
                  destructionWitness,
                  orderingWitness,
                  deferredFocusWitness,
                  replacementWitness,
                  dispositionWitness,
                  abandonmentWitness,
                  remountWitness,
                  debtWitness >>

ReturnResult(i, result, resultDisposition) ==
  /\ i \in Tokens
  /\ ValidResultPair(result, resultDisposition)
  /\ result # "synchronized"
  /\ status[i] = "inFlight"
  /\ i = currentIntent
  /\ (synchronizationDebt =>
        resultDisposition = "synchronizationRequired")
  /\ status' = [status EXCEPT ![i] = "returned"]
  /\ outcome' = [outcome EXCEPT ![i] = result]
  /\ disposition' = [disposition EXCEPT ![i] = resultDisposition]
  /\ required' =
       [required EXCEPT
          ![i] = AssignedRequiredEffects(result, resultDisposition)]
  /\ completed' = [completed EXCEPT ![i] = {}]
  /\ synchronizationDebt' =
       (synchronizationDebt
        \/ resultDisposition = "synchronizationRequired")
  /\ dispositionWitness' =
       /\ dispositionWitness
       /\ required'[i] = DispositionRequiredEffects(resultDisposition)
  /\ UNCHANGED << currentIntent,
                  nextIntent,
                  mounted,
                  surfaceEpoch,
                  operationSurface,
                  synchronizationRequested,
                  synchronizationRequestSurface,
                  effectWitness,
                  acknowledgeWitness,
                  destructionWitness,
                  orderingWitness,
                  deferredFocusWitness,
                  replacementWitness,
                  abandonmentWitness,
                  remountWitness,
                  debtWitness,
                  focusLocation,
                  focusSurface >>

ReturnAnyResult(i) ==
  \E result \in Outcomes \ {"none", "synchronized"},
     resultDisposition \in Dispositions \ {"none"} :
       ReturnResult(i, result, resultDisposition)

RequestSynchronization ==
  /\ mounted
  /\ synchronizationDebt
  /\ ~synchronizationRequested
  /\ \A i \in Tokens : status[i] # "returned"
  /\ synchronizationRequested' = TRUE
  /\ synchronizationRequestSurface' = surfaceEpoch
  /\ UNCHANGED << currentIntent,
                  nextIntent,
                  mounted,
                  surfaceEpoch,
                  operationSurface,
                  status,
                  outcome,
                  disposition,
                  required,
                  completed,
                  synchronizationDebt,
                  focusLocation,
                  focusSurface,
                  effectWitness,
                  acknowledgeWitness,
                  destructionWitness,
                  orderingWitness,
                  deferredFocusWitness,
                  replacementWitness,
                  dispositionWitness,
                  abandonmentWitness,
                  remountWitness,
                  debtWitness >>

ReturnSynchronization ==
  /\ mounted
  /\ synchronizationRequested
  /\ synchronizationRequestSurface = surfaceEpoch
  /\ \A i \in Tokens : status[i] \notin {"inFlight", "returned"}
  /\ nextIntent < MaxIntents
  /\ LET i == nextIntent + 1 IN
     /\ currentIntent' = i
     /\ nextIntent' = i
     /\ operationSurface' = [operationSurface EXCEPT ![i] = surfaceEpoch]
     /\ status' = [status EXCEPT ![i] = "returned"]
     /\ outcome' = [outcome EXCEPT ![i] = "synchronized"]
     /\ disposition' =
          [disposition EXCEPT ![i] = "synchronizationRequired"]
     /\ required' = [required EXCEPT ![i] = Effects]
     /\ completed' = [completed EXCEPT ![i] = {}]
  /\ synchronizationRequested' = FALSE
  /\ dispositionWitness' =
       /\ dispositionWitness
       /\ required'[currentIntent'] =
            DispositionRequiredEffects(disposition'[currentIntent'])
  /\ UNCHANGED << mounted,
                  surfaceEpoch,
                  synchronizationRequestSurface,
                  synchronizationDebt,
                  focusLocation,
                  focusSurface,
                  effectWitness,
                  acknowledgeWitness,
                  destructionWitness,
                  orderingWitness,
                  deferredFocusWitness,
                  replacementWitness,
                  abandonmentWitness,
                  remountWitness,
                  debtWitness >>

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
                  disposition,
                  required,
                  completed,
                  synchronizationDebt,
                  synchronizationRequested,
                  synchronizationRequestSurface,
                  focusLocation,
                  focusSurface,
                  effectWitness,
                  acknowledgeWitness,
                  destructionWitness,
                  orderingWitness,
                  deferredFocusWitness,
                  replacementWitness,
                  dispositionWitness,
                  abandonmentWitness,
                  remountWitness,
                  debtWitness >>

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
  /\ synchronizationRequested' =
       IF replaceSurface /\ EnforceRequestRetirement
       THEN FALSE
       ELSE synchronizationRequested
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
                  disposition,
                  required,
                  synchronizationDebt,
                  synchronizationRequestSurface,
                  acknowledgeWitness,
                  destructionWitness,
                  dispositionWitness,
                  abandonmentWitness,
                  remountWitness,
                  debtWitness >>

Acknowledge(i) ==
  /\ i \in Tokens
  /\ status[i] = "returned"
  /\ CurrentAuthority(i)
  /\ IF EnforceCompleteAcknowledge
     THEN completed[i] = required[i]
     ELSE TRUE
  /\ status' = [status EXCEPT ![i] = "acknowledged"]
  /\ synchronizationDebt' =
       IF disposition[i] = "synchronizationRequired"
          /\ EnforceDebtClearOnAcknowledge
       THEN FALSE
       ELSE synchronizationDebt
  /\ synchronizationRequested' =
       IF disposition[i] = "synchronizationRequired"
       THEN FALSE
       ELSE synchronizationRequested
  /\ acknowledgeWitness' =
       (acknowledgeWitness /\ (completed[i] = required[i]))
  /\ debtWitness' =
       /\ debtWitness
       /\ (disposition[i] # "synchronizationRequired"
           \/ ~synchronizationDebt')
  /\ UNCHANGED << currentIntent,
                  nextIntent,
                  mounted,
                  surfaceEpoch,
                  operationSurface,
                  outcome,
                  disposition,
                  required,
                  completed,
                  synchronizationRequestSurface,
                  focusLocation,
                  focusSurface,
                  effectWitness,
                  destructionWitness,
                  orderingWitness,
                  deferredFocusWitness,
                  replacementWitness,
                  dispositionWitness,
                  abandonmentWitness,
                  remountWitness >>

AbandonStale(i) ==
  /\ i \in Tokens
  /\ status[i] = "returned"
  /\ ~CurrentAuthority(i)
  /\ status' = [status EXCEPT ![i] = "abandoned"]
  /\ synchronizationDebt' =
       IF disposition[i] = "synchronizationRequired"
          /\ ~EnforceAbandonPreservesDebt
       THEN FALSE
       ELSE synchronizationDebt
  /\ abandonmentWitness' =
       /\ abandonmentWitness
       /\ (disposition[i] # "synchronizationRequired"
           \/ synchronizationDebt' = synchronizationDebt)
  /\ UNCHANGED << currentIntent,
                  nextIntent,
                  mounted,
                  surfaceEpoch,
                  operationSurface,
                  outcome,
                  disposition,
                  required,
                  completed,
                  synchronizationRequested,
                  synchronizationRequestSurface,
                  focusLocation,
                  focusSurface,
                  effectWitness,
                  acknowledgeWitness,
                  destructionWitness,
                  orderingWitness,
                  deferredFocusWitness,
                  replacementWitness,
                  dispositionWitness,
                  remountWitness,
                  debtWitness >>

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
  /\ synchronizationDebt' =
       IF ~EnforceAbandonPreservesDebt
          /\ \E i \in Tokens :
               status[i] = "returned"
               /\ disposition[i] = "synchronizationRequired"
       THEN FALSE
       ELSE synchronizationDebt
  /\ synchronizationRequested' =
       IF EnforceRequestRetirement THEN FALSE ELSE synchronizationRequested
  /\ abandonmentWitness' =
       /\ abandonmentWitness
       /\ (~(\E i \in Tokens :
                status[i] = "returned"
                /\ disposition[i] = "synchronizationRequired")
           \/ synchronizationDebt' = synchronizationDebt)
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
                  disposition,
                  required,
                  completed,
                  synchronizationRequestSurface,
                  effectWitness,
                  acknowledgeWitness,
                  orderingWitness,
                  deferredFocusWitness,
                  replacementWitness,
                  dispositionWitness,
                  remountWitness,
                  debtWitness >>

MountSurface ==
  /\ ~mounted
  /\ surfaceEpoch < MaxSurfaceEpoch
  /\ mounted' = TRUE
  /\ surfaceEpoch' = surfaceEpoch + 1
  /\ synchronizationRequested' =
       IF EnforceRemountSynchronization
       THEN synchronizationDebt
       ELSE FALSE
  /\ synchronizationRequestSurface' =
       IF EnforceRemountSynchronization /\ synchronizationDebt
       THEN surfaceEpoch + 1
       ELSE synchronizationRequestSurface
  /\ remountWitness' =
       /\ remountWitness
       /\ (~synchronizationDebt
           \/ /\ synchronizationRequested'
              /\ synchronizationRequestSurface' = surfaceEpoch')
  /\ UNCHANGED << currentIntent,
                  nextIntent,
                  operationSurface,
                  status,
                  outcome,
                  disposition,
                  required,
                  completed,
                  synchronizationDebt,
                  focusLocation,
                  focusSurface,
                  effectWitness,
                  acknowledgeWitness,
                  destructionWitness,
                  orderingWitness,
                  deferredFocusWitness,
                  replacementWitness,
                  dispositionWitness,
                  abandonmentWitness,
                  debtWitness >>

Next ==
  \/ BeginIntent
  \/ RequestSynchronization
  \/ ReturnSynchronization
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
  /\ disposition \in [Tokens -> Dispositions]
  /\ required \in [Tokens -> SUBSET Effects]
  /\ completed \in [Tokens -> SUBSET Effects]
  /\ synchronizationDebt \in BOOLEAN
  /\ synchronizationRequested \in BOOLEAN
  /\ synchronizationRequestSurface \in 0..MaxSurfaceEpoch
  /\ focusLocation \in FocusLocations
  /\ focusSurface \in 0..MaxSurfaceEpoch
  /\ effectWitness \in BOOLEAN
  /\ acknowledgeWitness \in BOOLEAN
  /\ destructionWitness \in BOOLEAN
  /\ orderingWitness \in BOOLEAN
  /\ deferredFocusWitness \in BOOLEAN
  /\ replacementWitness \in BOOLEAN
  /\ dispositionWitness \in BOOLEAN
  /\ abandonmentWitness \in BOOLEAN
  /\ remountWitness \in BOOLEAN
  /\ debtWitness \in BOOLEAN

ReturnedShape ==
  \A i \in Tokens :
    /\ (status[i] = "unused" =>
          /\ outcome[i] = "none"
          /\ disposition[i] = "none")
    /\ (status[i] = "inFlight" =>
          /\ outcome[i] = "none"
          /\ disposition[i] = "none")
    /\ (status[i] = "discarded" =>
          /\ outcome[i] = "none"
          /\ disposition[i] = "none"
          /\ required[i] = {}
          /\ completed[i] = {})
    /\ (status[i] \in {"returned"} \cup AuthorityTerminalStatuses =>
          /\ outcome[i] # "none"
          /\ disposition[i] # "none"
          /\ ValidResultPair(outcome[i], disposition[i])
          /\ required[i] =
               AssignedRequiredEffects(outcome[i], disposition[i])
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

DispositionControlsInstallation == dispositionWitness

AbandonmentPreservesSynchronizationDebt == abandonmentWitness

RemountRequestsSynchronization == remountWitness

AcknowledgeClearsSynchronizationDebt == debtWitness

SynchronizationRequestShape ==
  synchronizationRequested =>
    /\ mounted
    /\ synchronizationDebt
    /\ synchronizationRequestSurface = surfaceEpoch

NeedsSynchronizationRequest ==
  /\ mounted
  /\ synchronizationDebt
  /\ ~synchronizationRequested
  /\ \A i \in Tokens : status[i] # "returned"

EveryReturnedAuthoritySettles ==
  \A i \in Tokens :
    status[i] = "returned" ~> status[i] \in AuthorityTerminalStatuses

EverySubmittedIntentSettles ==
  \A i \in Tokens :
    status[i] = "inFlight" ~> status[i] \in OperationTerminalStatuses

EveryOutstandingDebtRequestsSynchronization ==
  NeedsSynchronizationRequest
  ~> \/ synchronizationRequested
      \/ ~mounted
      \/ ~synchronizationDebt
      \/ \E i \in Tokens : status[i] = "returned"

Fairness ==
  /\ \A i \in Tokens : WF_vars(ReturnAnyResult(i))
  /\ \A i \in Tokens : WF_vars(DiscardSuperseded(i))
  /\ \A i \in Tokens, effect \in Effects, replace \in BOOLEAN :
       WF_vars(RunEffect(i, effect, replace))
  /\ \A i \in Tokens : WF_vars(Acknowledge(i))
  /\ \A i \in Tokens : WF_vars(AbandonStale(i))
  /\ WF_vars(RequestSynchronization)
  /\ WF_vars(ReturnSynchronization)

Spec == Init /\ [][Next]_vars /\ Fairness

=============================================================================
