------------------------- MODULE UiEffectLifecycle -------------------------
EXTENDS Naturals, FiniteSets, TLC

\* This model owns only the Inspect Web consumer lifecycle after the product
\* navigation session has accepted an intent or synchronization request.
\* Product snapshot construction, reconciliation, supersession, and facet
\* recommendation remain abstract.

CONSTANTS
  MaxIntents,
  MaxSurfaceEpoch,
  MaxSynchronizationRequests,
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
  EnforceSynchronizationCorrelation

ASSUME MaxIntents >= 2
ASSUME MaxSurfaceEpoch >= 2
ASSUME MaxSynchronizationRequests >= 2
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
ASSUME EnforceSynchronizationCorrelation \in BOOLEAN

Tokens == 1..MaxIntents
SynchronizationRequests == 1..MaxSynchronizationRequests
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
SynchronizationResponseHandling ==
  {"none", "returned", "abandoned", "discharged"}
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
  nextSynchronizationRequest,
  pendingSynchronizationRequest,
  synchronizationRequestSurface,
  settledSynchronizationRequests,
  synchronizationResponseHandling,
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
  debtWitness,
  synchronizationCorrelationWitness

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
     nextSynchronizationRequest,
     pendingSynchronizationRequest,
     synchronizationRequestSurface,
     settledSynchronizationRequests,
     synchronizationResponseHandling,
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
     debtWitness,
     synchronizationCorrelationWitness >>

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
  /\ nextSynchronizationRequest = 0
  /\ pendingSynchronizationRequest = 0
  /\ synchronizationRequestSurface =
       [r \in SynchronizationRequests |-> 0]
  /\ settledSynchronizationRequests = {}
  /\ synchronizationResponseHandling =
       [r \in SynchronizationRequests |-> "none"]
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
  /\ synchronizationCorrelationWitness = TRUE

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
                  nextSynchronizationRequest,
                  pendingSynchronizationRequest,
                  synchronizationRequestSurface,
                  settledSynchronizationRequests,
                  synchronizationResponseHandling,
                  effectWitness,
                  acknowledgeWitness,
                  destructionWitness,
                  orderingWitness,
                  deferredFocusWitness,
                  replacementWitness,
                  dispositionWitness,
                  abandonmentWitness,
                  remountWitness,
                  debtWitness,
                  synchronizationCorrelationWitness >>

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
                  nextSynchronizationRequest,
                  pendingSynchronizationRequest,
                  synchronizationRequestSurface,
                  settledSynchronizationRequests,
                  synchronizationResponseHandling,
                  effectWitness,
                  acknowledgeWitness,
                  destructionWitness,
                  orderingWitness,
                  deferredFocusWitness,
                  replacementWitness,
                  abandonmentWitness,
                  remountWitness,
                  debtWitness,
                  synchronizationCorrelationWitness,
                  focusLocation,
                  focusSurface >>

ReturnAnyResult(i) ==
  \E result \in Outcomes \ {"none", "synchronized"},
     resultDisposition \in Dispositions \ {"none"} :
       ReturnResult(i, result, resultDisposition)

RequestSynchronization ==
  /\ mounted
  /\ synchronizationDebt
  /\ pendingSynchronizationRequest = 0
  /\ nextSynchronizationRequest < MaxSynchronizationRequests
  /\ \A i \in Tokens : status[i] # "returned"
  /\ LET r == nextSynchronizationRequest + 1 IN
     /\ nextSynchronizationRequest' = r
     /\ pendingSynchronizationRequest' = r
     /\ synchronizationRequestSurface' =
          [synchronizationRequestSurface EXCEPT ![r] = surfaceEpoch]
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
                  settledSynchronizationRequests,
                  synchronizationResponseHandling,
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
                  debtWitness,
                  synchronizationCorrelationWitness >>

ReturnSynchronization(r) ==
  /\ r \in SynchronizationRequests
  /\ pendingSynchronizationRequest = r
  /\ \A i \in Tokens : status[i] \notin {"inFlight", "returned"}
  /\ LET i == nextIntent + 1
         currentLifetime ==
           mounted /\ synchronizationRequestSurface[r] = surfaceEpoch
         consume ==
           currentLifetime \/ ~EnforceSynchronizationCorrelation
     IN
     /\ (consume => nextIntent < MaxIntents)
     /\ currentIntent' = IF consume THEN i ELSE currentIntent
     /\ nextIntent' = IF consume THEN i ELSE nextIntent
     /\ operationSurface' =
          IF consume
          THEN [operationSurface EXCEPT ![i] = surfaceEpoch]
          ELSE operationSurface
     /\ status' =
          IF consume
          THEN [status EXCEPT ![i] = "returned"]
          ELSE status
     /\ outcome' =
          IF consume
          THEN [outcome EXCEPT ![i] = "synchronized"]
          ELSE outcome
     /\ disposition' =
          IF consume
          THEN [disposition EXCEPT ![i] = "synchronizationRequired"]
          ELSE disposition
     /\ required' =
          IF consume
          THEN [required EXCEPT ![i] = Effects]
          ELSE required
     /\ completed' =
          IF consume
          THEN [completed EXCEPT ![i] = {}]
          ELSE completed
     /\ synchronizationResponseHandling' =
          [synchronizationResponseHandling EXCEPT
             ![r] = IF consume THEN "returned" ELSE "abandoned"]
     /\ dispositionWitness' =
          IF consume
          THEN
            /\ dispositionWitness
            /\ required'[i] =
                 DispositionRequiredEffects(disposition'[i])
          ELSE dispositionWitness
     /\ synchronizationCorrelationWitness' =
          /\ synchronizationCorrelationWitness
          /\ (currentLifetime
              => /\ status'[i] = "returned"
                 /\ currentIntent' = i)
          /\ (~currentLifetime
              => /\ status' = status
                 /\ currentIntent' = currentIntent)
  /\ pendingSynchronizationRequest' = 0
  /\ settledSynchronizationRequests' =
       settledSynchronizationRequests \cup {r}
  /\ UNCHANGED << mounted,
                  surfaceEpoch,
                  nextSynchronizationRequest,
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
                  nextSynchronizationRequest,
                  pendingSynchronizationRequest,
                  synchronizationRequestSurface,
                  settledSynchronizationRequests,
                  synchronizationResponseHandling,
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
                  debtWitness,
                  synchronizationCorrelationWitness >>

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
                  disposition,
                  required,
                  synchronizationDebt,
                  nextSynchronizationRequest,
                  pendingSynchronizationRequest,
                  synchronizationRequestSurface,
                  settledSynchronizationRequests,
                  synchronizationResponseHandling,
                  acknowledgeWitness,
                  destructionWitness,
                  dispositionWitness,
                  abandonmentWitness,
                  remountWitness,
                  debtWitness,
                  synchronizationCorrelationWitness >>

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
  /\ pendingSynchronizationRequest' =
       IF disposition[i] = "synchronizationRequired"
       THEN 0
       ELSE pendingSynchronizationRequest
  /\ settledSynchronizationRequests' =
       IF disposition[i] = "synchronizationRequired"
          /\ pendingSynchronizationRequest # 0
       THEN
         settledSynchronizationRequests \cup {pendingSynchronizationRequest}
       ELSE settledSynchronizationRequests
  /\ synchronizationResponseHandling' =
       IF disposition[i] = "synchronizationRequired"
          /\ pendingSynchronizationRequest # 0
       THEN
         [synchronizationResponseHandling EXCEPT
            ![pendingSynchronizationRequest] = "discharged"]
       ELSE synchronizationResponseHandling
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
                  nextSynchronizationRequest,
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
                  remountWitness,
                  synchronizationCorrelationWitness >>

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
                  nextSynchronizationRequest,
                  pendingSynchronizationRequest,
                  synchronizationRequestSurface,
                  settledSynchronizationRequests,
                  synchronizationResponseHandling,
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
                  debtWitness,
                  synchronizationCorrelationWitness >>

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
                  nextSynchronizationRequest,
                  pendingSynchronizationRequest,
                  synchronizationRequestSurface,
                  settledSynchronizationRequests,
                  synchronizationResponseHandling,
                  effectWitness,
                  acknowledgeWitness,
                  orderingWitness,
                  deferredFocusWitness,
                  replacementWitness,
                  dispositionWitness,
                  remountWitness,
                  debtWitness,
                  synchronizationCorrelationWitness >>

MountSurface ==
  /\ ~mounted
  /\ surfaceEpoch < MaxSurfaceEpoch
  /\ mounted' = TRUE
  /\ surfaceEpoch' = surfaceEpoch + 1
  /\ nextSynchronizationRequest' =
       IF EnforceRemountSynchronization
          /\ synchronizationDebt
          /\ pendingSynchronizationRequest = 0
          /\ nextSynchronizationRequest < MaxSynchronizationRequests
       THEN nextSynchronizationRequest + 1
       ELSE nextSynchronizationRequest
  /\ pendingSynchronizationRequest' =
       IF EnforceRemountSynchronization
          /\ synchronizationDebt
          /\ pendingSynchronizationRequest = 0
          /\ nextSynchronizationRequest < MaxSynchronizationRequests
       THEN nextSynchronizationRequest + 1
       ELSE pendingSynchronizationRequest
  /\ synchronizationRequestSurface' =
       IF EnforceRemountSynchronization
          /\ synchronizationDebt
          /\ pendingSynchronizationRequest = 0
          /\ nextSynchronizationRequest < MaxSynchronizationRequests
       THEN
         [synchronizationRequestSurface EXCEPT
            ![nextSynchronizationRequest + 1] = surfaceEpoch + 1]
       ELSE synchronizationRequestSurface
  /\ remountWitness' =
       /\ remountWitness
       /\ (~(synchronizationDebt
             /\ pendingSynchronizationRequest = 0
             /\ nextSynchronizationRequest < MaxSynchronizationRequests)
           \/ /\ pendingSynchronizationRequest' # 0
              /\ synchronizationRequestSurface'
                   [pendingSynchronizationRequest'] = surfaceEpoch')
  /\ UNCHANGED << currentIntent,
                  nextIntent,
                  operationSurface,
                  status,
                  outcome,
                  disposition,
                  required,
                  completed,
                  synchronizationDebt,
                  settledSynchronizationRequests,
                  synchronizationResponseHandling,
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
                  debtWitness,
                  synchronizationCorrelationWitness >>

Next ==
  \/ BeginIntent
  \/ RequestSynchronization
  \/ \E r \in SynchronizationRequests : ReturnSynchronization(r)
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
  /\ nextSynchronizationRequest \in 0..MaxSynchronizationRequests
  /\ pendingSynchronizationRequest \in 0..MaxSynchronizationRequests
  /\ synchronizationRequestSurface \in
       [SynchronizationRequests -> 0..MaxSurfaceEpoch]
  /\ settledSynchronizationRequests \subseteq SynchronizationRequests
  /\ synchronizationResponseHandling \in
       [SynchronizationRequests -> SynchronizationResponseHandling]
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
  /\ synchronizationCorrelationWitness \in BOOLEAN

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
  /\ settledSynchronizationRequests \subseteq
       1..nextSynchronizationRequest
  /\ (pendingSynchronizationRequest = 0
      \/ /\ pendingSynchronizationRequest <= nextSynchronizationRequest
         /\ pendingSynchronizationRequest
              \notin settledSynchronizationRequests
         /\ synchronizationRequestSurface[pendingSynchronizationRequest] # 0)
  /\ \A r \in SynchronizationRequests :
       (r \in settledSynchronizationRequests)
       = (synchronizationResponseHandling[r]
            \in {"returned", "abandoned", "discharged"})

SynchronizationResponseMatchesRequestLifetime ==
  synchronizationCorrelationWitness

StaleResponseRecoveryComplete ==
  /\ synchronizationResponseHandling[1] = "abandoned"
  /\ synchronizationResponseHandling[2] = "returned"

NoStaleResponseRecoveryObserved ==
  ~StaleResponseRecoveryComplete

NeedsSynchronizationRequest ==
  /\ mounted
  /\ synchronizationDebt
  /\ pendingSynchronizationRequest = 0
  /\ nextSynchronizationRequest < MaxSynchronizationRequests
  /\ \A i \in Tokens : status[i] # "returned"

EveryReturnedAuthoritySettles ==
  \A i \in Tokens :
    status[i] = "returned" ~> status[i] \in AuthorityTerminalStatuses

EverySubmittedIntentSettles ==
  \A i \in Tokens :
    status[i] = "inFlight" ~> status[i] \in OperationTerminalStatuses

EveryOutstandingDebtRequestsSynchronization ==
  NeedsSynchronizationRequest
  ~> \/ pendingSynchronizationRequest # 0
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
  /\ \A r \in SynchronizationRequests : WF_vars(ReturnSynchronization(r))

Spec == Init /\ [][Next]_vars /\ Fairness

=============================================================================
