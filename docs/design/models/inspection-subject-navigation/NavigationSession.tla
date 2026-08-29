-------------------------- MODULE NavigationSession --------------------------
(***************************************************************************)
(* Design model of the retained Inspection Subject Navigation session.     *)
(*                                                                         *)
(* The model checks the ordering, supersession, and authority rules of the *)
(* design in `docs/design/inspection-subject-navigation.md`.  It models    *)
(* unavailable revision behavior, but not descriptor classification,       *)
(* identity ranking, lens contents, rendering, or any implementation.       *)
(*                                                                         *)
(* Product concept                    Model variable                       *)
(*   installed navigation snapshot      installedSnapshot                  *)
(*   installed snapshot revision        installedRev                       *)
(*   consumer-installed snapshot         consumerSnapshot                   *)
(*   consumer-installed revision         consumerRev                        *)
(*   authority epoch used for install    consumerInstalledEpoch             *)
(*   acknowledged consumer snapshot      acknowledgedSnapshot               *)
(*   acknowledged consumer revision      acknowledgedRev                    *)
(*   product-issued explicit intent     currentIntent                      *)
(*   unresolved explicit operation      explicit                           *)
(*   superseded explicit operation      superseded                         *)
(*   owner-issued maintenance number    nextMaintenance                    *)
(*   standalone maintenance queue       maintenanceQueue (request order)   *)
(*   last admitted maintenance          lastAdmitted                       *)
(*   exact admitted maintenance IDs     admittedRequests                   *)
(*   last semantic navigation result    lastResult                         *)
(*   effect epoch                       effectEpoch                        *)
(*   unconsumed effect authority        effect                             *)
(*   authority held by a consumer       hostAuthority                      *)
(*   next synchronization request       nextSynchronization                *)
(*   pending synchronization request    synchronizationRequest             *)
(*   settled synchronization requests   settledSynchronizations            *)
(*                                                                         *)
(* Guard witnesses are latching booleans.  Each guarded result, admission, *)
(* or visible effect re-derives the condition the design requires rather   *)
(* than trusting the action's guard or assignments.                         *)
(***************************************************************************)
EXTENDS Naturals, Sequences, FiniteSets

CONSTANTS
  MaxIntent,        \* how many explicit intents one behaviour may issue
  MaxMaintenance,   \* how many standalone maintenance requests it may issue
  MaxSynchronization, \* how many external synchronization requests may issue
  IntentKinds,      \* subject, lens, coordinate, and canonical restoration
  SnapshotValues,   \* finite complete-snapshot contents
  InitialSnapshot,  \* content retained before the first modelled result
  SessionId,        \* the identity of this retained navigation session
  ForeignSessionId  \* some other session, used only for foreign authority

ASSUME MaxIntent \in Nat /\ MaxMaintenance \in Nat
ASSUME MaxSynchronization \in Nat /\ MaxSynchronization > 1
ASSUME InitialSnapshot \in SnapshotValues /\ Cardinality(SnapshotValues) > 1
ASSUME SessionId # ForeignSessionId

VARIABLES
  installedSnapshot,
  installedRev,
  consumerSnapshot,
  consumerRev,
  consumerInstalledEpoch,
  acknowledgedSnapshot,
  acknowledgedRev,
  currentIntent,
  explicit,
  superseded,
  nextMaintenance,
  maintenanceQueue,
  lastAdmitted,
  admittedRequests,
  lastResult,
  effectEpoch,
  effect,
  hostAuthority,
  nextSynchronization,
  synchronizationRequest,
  settledSynchronizations,
  admissionWitness,
  regatherWitness,
  revisionWitness,
  orderWitness,
  visibleWitness,
  consumerSyncWitness,
  consumerAckWitness,
  dispositionWitness,
  synchronizationWitness

vars == << installedSnapshot, installedRev, consumerSnapshot, consumerRev,
           consumerInstalledEpoch,
           acknowledgedSnapshot, acknowledgedRev,
           currentIntent, explicit,
           superseded, nextMaintenance, maintenanceQueue, lastAdmitted,
           admittedRequests, lastResult, effectEpoch, effect, hostAuthority,
           nextSynchronization, synchronizationRequest,
           settledSynchronizations,
           admissionWitness, regatherWitness, revisionWitness, orderWitness,
           visibleWitness, consumerSyncWitness, consumerAckWitness,
           dispositionWitness, synchronizationWitness >>

(***************************************************************************)
(* Currencies.                                                             *)
(*                                                                         *)
(* An effect authority is the four-part product currency: session          *)
(* identity, snapshot state revision, intent token, and effect epoch.  The *)
(* outcome class records which kind of result carried it.                  *)
(***************************************************************************)
Outcomes == {"applied", "retained", "aborted", "maintenance", "synchronize"}
SemanticOutcomes ==
  {"applied", "unavailable", "rejected", "failed", "aborted", "maintenance",
   "synchronize"}
Dispositions == {"current", "synchronizationRequired"}

NoResult ==
  [ outcome         |-> "none",
    disposition     |-> "none",
    receiptSnapshot |-> InitialSnapshot,
    receiptRev      |-> 0,
    snapshotChanged |-> FALSE,
    priorSnapshot   |-> InitialSnapshot,
    resultSnapshot  |-> InitialSnapshot,
    priorRev        |-> 0,
    resultRev       |-> 0 ]

ConsumerDisposition(resultSnapshot, resultRev) ==
  IF acknowledgedRev = resultRev /\ acknowledgedSnapshot = resultSnapshot
    THEN "current"
    ELSE "synchronizationRequired"

Result(outcome, disposition, receiptSnapshot, receiptRev,
       priorSnapshot, resultSnapshot, priorRev, resultRev) ==
  [ outcome         |-> outcome,
    disposition     |-> disposition,
    receiptSnapshot |-> receiptSnapshot,
    receiptRev      |-> receiptRev,
    snapshotChanged |-> resultSnapshot # priorSnapshot,
    priorSnapshot   |-> priorSnapshot,
    resultSnapshot  |-> resultSnapshot,
    priorRev        |-> priorRev,
    resultRev       |-> resultRev ]

CorrectDisposition(result) ==
  result.disposition =
    IF result.receiptRev = result.resultRev /\
         result.receiptSnapshot = result.resultSnapshot
      THEN "current"
      ELSE "synchronizationRequired"

ConsumerAcknowledgementLags ==
  acknowledgedRev # installedRev \/
    acknowledgedSnapshot # installedSnapshot

Authority(outcome, rev, intent, epoch) ==
  [ session |-> SessionId,
    outcome |-> outcome,
    rev     |-> rev,
    intent  |-> intent,
    epoch   |-> epoch ]

NoAuthority ==
  [ session |-> "none", outcome |-> "none", rev |-> 0, intent |-> 0, epoch |-> 0 ]

\* Authority minted by a different navigation session.  A consumer may be
\* handed one; the session must never treat it as current.
ForeignAuthority ==
  [ session |-> ForeignSessionId,
    outcome |-> "applied",
    rev     |-> 1,
    intent  |-> 1,
    epoch   |-> 1 ]

NoExplicitWork == [token |-> 0, kind |-> "none"]

Range(s) == { s[i] : i \in DOMAIN s }

HasMaintenance(n) == \E i \in DOMAIN maintenanceQueue : maintenanceQueue[i].seq = n
MaintenanceIndex(n) == CHOOSE i \in DOMAIN maintenanceQueue : maintenanceQueue[i].seq = n
MaintenanceEntry(n) == maintenanceQueue[MaintenanceIndex(n)]

TypeOK ==
  /\ installedSnapshot \in SnapshotValues
  /\ installedRev \in Nat
  /\ consumerSnapshot \in SnapshotValues
  /\ consumerRev \in Nat
  /\ consumerInstalledEpoch \in Nat
  /\ consumerInstalledEpoch <= effectEpoch
  /\ acknowledgedSnapshot \in SnapshotValues
  /\ acknowledgedRev \in Nat
  /\ acknowledgedRev <= consumerRev
  /\ consumerRev <= installedRev
  /\ currentIntent \in 0 .. MaxIntent
  /\ explicit.token \in 0 .. MaxIntent
  /\ explicit.kind \in IntentKinds \cup {"none"}
  /\ superseded \subseteq 1 .. MaxIntent
  /\ nextMaintenance \in 1 .. (MaxMaintenance + 1)
  /\ lastAdmitted \in 0 .. MaxMaintenance
  /\ admittedRequests \subseteq 1 .. MaxMaintenance
  /\ lastResult.outcome \in SemanticOutcomes \cup {"none"}
  /\ lastResult.disposition \in Dispositions \cup {"none"}
  /\ lastResult.receiptSnapshot \in SnapshotValues
  /\ lastResult.receiptRev \in Nat
  /\ lastResult.snapshotChanged \in BOOLEAN
  /\ lastResult.priorSnapshot \in SnapshotValues
  /\ lastResult.resultSnapshot \in SnapshotValues
  /\ lastResult.priorRev \in Nat
  /\ lastResult.resultRev \in Nat
  /\ effectEpoch \in Nat
  /\ effect.outcome \in Outcomes \cup {"none"}
  /\ hostAuthority.outcome \in Outcomes \cup {"none"}
  /\ nextSynchronization \in 1 .. (MaxSynchronization + 1)
  /\ synchronizationRequest \in 0 .. MaxSynchronization
  /\ settledSynchronizations \subseteq 1 .. MaxSynchronization
  /\ (synchronizationRequest # 0 =>
        /\ synchronizationRequest < nextSynchronization
        /\ synchronizationRequest \notin settledSynchronizations)
  /\ \A n \in settledSynchronizations : n < nextSynchronization
  /\ \A i \in DOMAIN maintenanceQueue :
       /\ maintenanceQueue[i].seq \in 1 .. MaxMaintenance
       /\ maintenanceQueue[i].ready \in BOOLEAN
       /\ maintenanceQueue[i].basis \in Nat
       /\ maintenanceQueue[i].needsRegather \in BOOLEAN
  /\ regatherWitness \in BOOLEAN
  /\ revisionWitness \in BOOLEAN
  /\ consumerSyncWitness \in BOOLEAN
  /\ consumerAckWitness \in BOOLEAN
  /\ dispositionWitness \in BOOLEAN
  /\ synchronizationWitness \in BOOLEAN

Init ==
  /\ installedSnapshot = InitialSnapshot
  /\ installedRev = 0
  /\ consumerSnapshot = InitialSnapshot
  /\ consumerRev = 0
  /\ consumerInstalledEpoch = 0
  /\ acknowledgedSnapshot = InitialSnapshot
  /\ acknowledgedRev = 0
  /\ currentIntent = 0
  /\ explicit = NoExplicitWork
  /\ superseded = {}
  /\ nextMaintenance = 1
  /\ maintenanceQueue = << >>
  /\ lastAdmitted = 0
  /\ admittedRequests = {}
  /\ lastResult = NoResult
  /\ effectEpoch = 0
  /\ effect = NoAuthority
  /\ hostAuthority = NoAuthority
  /\ nextSynchronization = 1
  /\ synchronizationRequest = 0
  /\ settledSynchronizations = {}
  /\ admissionWitness = TRUE
  /\ regatherWitness = TRUE
  /\ revisionWitness = TRUE
  /\ orderWitness = TRUE
  /\ visibleWitness = TRUE
  /\ consumerSyncWitness = TRUE
  /\ consumerAckWitness = TRUE
  /\ dispositionWitness = TRUE
  /\ synchronizationWitness = TRUE

(***************************************************************************)
(* Explicit intent.                                                        *)
(*                                                                         *)
(* Beginning an explicit subject, lens, coordinate, or canonical           *)
(* restoration operation issues a new monotonic token and immediately      *)
(* supersedes older explicit work, invalidates unconsumed authority and    *)
(* already gathered maintenance facts, and makes any later snapshot        *)
(* replacement force queued maintenance to rebuild.                        *)
(***************************************************************************)
BeginExplicitIntent(kind) ==
  /\ currentIntent < MaxIntent
  /\ currentIntent' = currentIntent + 1
  /\ explicit' = [token |-> currentIntent + 1, kind |-> kind]
  /\ superseded' = IF explicit = NoExplicitWork
                     THEN superseded
                     ELSE superseded \cup {explicit.token}
  /\ effect' = NoAuthority
  /\ maintenanceQueue' =
       [ i \in DOMAIN maintenanceQueue |->
           [maintenanceQueue[i] EXCEPT !.ready = FALSE] ]
  /\ UNCHANGED << installedSnapshot, installedRev,
                  consumerSnapshot, consumerRev, consumerInstalledEpoch,
                  acknowledgedSnapshot, acknowledgedRev, nextMaintenance,
                  lastAdmitted, admittedRequests, lastResult, effectEpoch,
                  hostAuthority, nextSynchronization,
                  synchronizationRequest, settledSynchronizations,
                  admissionWitness, regatherWitness, revisionWitness,
                  orderWitness, visibleWitness,
                  consumerSyncWitness, consumerAckWitness,
                  dispositionWitness, synchronizationWitness >>

\* An `Applied` outcome installs a semantically changed replacement snapshot
\* and returns fresh authority under its own intent token.
ExplicitResultInstalls(returnedSnapshot) ==
  /\ explicit # NoExplicitWork
  /\ explicit.token = currentIntent
  /\ returnedSnapshot # installedSnapshot
  /\ installedSnapshot' = returnedSnapshot
  /\ installedRev' = installedRev + 1
  /\ effectEpoch' = effectEpoch + 1
  /\ effect' = Authority("applied", installedRev + 1, currentIntent, effectEpoch + 1)
  /\ hostAuthority' = effect'
  /\ lastResult' =
       Result("applied",
              ConsumerDisposition(returnedSnapshot, installedRev + 1),
              acknowledgedSnapshot, acknowledgedRev,
              installedSnapshot, returnedSnapshot,
              installedRev, installedRev + 1)
  /\ dispositionWitness' =
       /\ dispositionWitness
       /\ CorrectDisposition(lastResult')
  /\ explicit' = NoExplicitWork
  /\ UNCHANGED << consumerSnapshot, consumerRev, consumerInstalledEpoch,
                  acknowledgedSnapshot, acknowledgedRev,
                  currentIntent, superseded, nextMaintenance,
                  maintenanceQueue, lastAdmitted, admittedRequests,
                  nextSynchronization, synchronizationRequest,
                  settledSynchronizations,
                  admissionWitness,
                  regatherWitness, revisionWitness, orderWitness,
                  visibleWitness, consumerSyncWitness, consumerAckWitness,
                  synchronizationWitness >>

\* An unavailable request returns a complete snapshot value.  Change is
\* derived by comparing that value with the installed snapshot, not supplied
\* as an independent choice.
ExplicitUnavailable(returnedSnapshot) ==
  /\ explicit # NoExplicitWork
  /\ explicit.token = currentIntent
  /\ LET changed == returnedSnapshot # installedSnapshot IN
       /\ installedSnapshot' = returnedSnapshot
       /\ installedRev' = IF changed THEN installedRev + 1 ELSE installedRev
       /\ effectEpoch' = effectEpoch + 1
       /\ effect' =
            Authority(IF changed THEN "applied" ELSE "retained",
                      installedRev', currentIntent, effectEpoch + 1)
       /\ hostAuthority' = effect'
       /\ lastResult' =
            Result("unavailable",
                   ConsumerDisposition(returnedSnapshot, installedRev'),
                   acknowledgedSnapshot, acknowledgedRev,
                   installedSnapshot, returnedSnapshot,
                   installedRev, installedRev')
       /\ dispositionWitness' =
            /\ dispositionWitness
            /\ CorrectDisposition(lastResult')
       /\ revisionWitness' =
            /\ revisionWitness
            /\ installedSnapshot' = lastResult'.resultSnapshot
            /\ installedRev' = lastResult'.resultRev
            /\ lastResult'.priorSnapshot = installedSnapshot
            /\ lastResult'.priorRev = installedRev
  /\ explicit' = NoExplicitWork
  /\ UNCHANGED << consumerSnapshot, consumerRev, consumerInstalledEpoch,
                  acknowledgedSnapshot, acknowledgedRev,
                  currentIntent, superseded, nextMaintenance,
                  maintenanceQueue, lastAdmitted, admittedRequests,
                  nextSynchronization, synchronizationRequest,
                  settledSynchronizations,
                  admissionWitness,
                  regatherWitness, orderWitness, visibleWitness,
                  consumerSyncWitness, consumerAckWitness,
                  synchronizationWitness >>

\* Rejected and failed navigation results retain the installed snapshot but
\* receive a fresh effect epoch so delayed outcome work cannot surface later.
ExplicitResultRetains(outcome) ==
  /\ outcome \in {"rejected", "failed"}
  /\ explicit # NoExplicitWork
  /\ explicit.token = currentIntent
  /\ effectEpoch' = effectEpoch + 1
  /\ effect' =
       Authority("retained", installedRev, currentIntent, effectEpoch + 1)
  /\ hostAuthority' = effect'
  /\ lastResult' =
       Result(outcome,
              ConsumerDisposition(installedSnapshot, installedRev),
              acknowledgedSnapshot, acknowledgedRev,
              installedSnapshot, installedSnapshot,
              installedRev, installedRev)
  /\ dispositionWitness' =
       /\ dispositionWitness
       /\ CorrectDisposition(lastResult')
  /\ explicit' = NoExplicitWork
  /\ UNCHANGED << installedSnapshot, installedRev,
                  consumerSnapshot, consumerRev, consumerInstalledEpoch,
                  acknowledgedSnapshot, acknowledgedRev,
                  currentIntent, superseded,
                  nextMaintenance, maintenanceQueue, lastAdmitted,
                  admittedRequests,
                  nextSynchronization, synchronizationRequest,
                  settledSynchronizations,
                  admissionWitness, regatherWitness, revisionWitness,
                  orderWitness, visibleWitness,
                  consumerSyncWitness, consumerAckWitness,
                  synchronizationWitness >>

\* Packet decoding, coordinate realization, or another prerequisite owner
\* failed before navigation could run.  The intent terminates with a typed
\* abort effect instead of inventing a navigation result, and the session is
\* not left waiting for a snapshot that cannot arrive.
ExternalPrerequisiteAbort ==
  /\ explicit # NoExplicitWork
  /\ explicit.token = currentIntent
  /\ effectEpoch' = effectEpoch + 1
  /\ effect' = Authority("aborted", installedRev, currentIntent, effectEpoch + 1)
  /\ hostAuthority' = effect'
  /\ lastResult' =
       Result("aborted",
              ConsumerDisposition(installedSnapshot, installedRev),
              acknowledgedSnapshot, acknowledgedRev,
              installedSnapshot, installedSnapshot,
              installedRev, installedRev)
  /\ dispositionWitness' =
       /\ dispositionWitness
       /\ CorrectDisposition(lastResult')
  /\ explicit' = NoExplicitWork
  /\ UNCHANGED << installedSnapshot, installedRev,
                  consumerSnapshot, consumerRev, consumerInstalledEpoch,
                  acknowledgedSnapshot, acknowledgedRev,
                  currentIntent, superseded,
                  nextMaintenance, maintenanceQueue, lastAdmitted,
                  admittedRequests,
                  nextSynchronization, synchronizationRequest,
                  settledSynchronizations,
                  admissionWitness, regatherWitness, revisionWitness,
                  orderWitness, visibleWitness,
                  consumerSyncWitness, consumerAckWitness,
                  synchronizationWitness >>

\* A superseded explicit operation returns late.  It produces no visible
\* effect and cannot install.
SupersededResultDiscarded(token) ==
  /\ token \in superseded
  /\ superseded' = superseded \ {token}
  /\ UNCHANGED << installedSnapshot, installedRev,
                  consumerSnapshot, consumerRev, consumerInstalledEpoch,
                  acknowledgedSnapshot, acknowledgedRev,
                  currentIntent, explicit,
                  nextMaintenance,
                  maintenanceQueue, lastAdmitted, admittedRequests,
                  effectEpoch, effect,
                  hostAuthority, lastResult, nextSynchronization,
                  synchronizationRequest, settledSynchronizations,
                  admissionWitness,
                  regatherWitness, revisionWitness, orderWitness,
                  visibleWitness, consumerSyncWitness, consumerAckWitness,
                  dispositionWitness, synchronizationWitness >>

(***************************************************************************)
(* Standalone maintenance.                                                 *)
(*                                                                         *)
(* Inventory refresh and reconciliation started independently of an        *)
(* explicit operation are snapshot maintenance, not new user intent.  They *)
(* are queued in owner-issued request order; their facts may complete in   *)
(* any order.                                                              *)
(***************************************************************************)
RequestMaintenance ==
  /\ nextMaintenance <= MaxMaintenance
  /\ maintenanceQueue' =
       Append(maintenanceQueue,
              [ seq           |-> nextMaintenance,
                ready         |-> FALSE,
                basis         |-> installedRev,
                needsRegather |-> FALSE ])
  /\ nextMaintenance' = nextMaintenance + 1
  /\ UNCHANGED << installedSnapshot, installedRev,
                  consumerSnapshot, consumerRev, consumerInstalledEpoch,
                  acknowledgedSnapshot, acknowledgedRev,
                  currentIntent, explicit,
                  superseded,
                  lastAdmitted, admittedRequests, lastResult, effectEpoch,
                  effect, hostAuthority, nextSynchronization,
                  synchronizationRequest, settledSynchronizations,
                  admissionWitness, regatherWitness, revisionWitness,
                  orderWitness, visibleWitness,
                  consumerSyncWitness, consumerAckWitness,
                  dispositionWitness, synchronizationWitness >>

\* Facts for one queued request finish gathering.  Any request may finish
\* first; completion timing must not select the final snapshot.
GatherMaintenanceFacts(n) ==
  /\ HasMaintenance(n)
  /\ LET i == MaintenanceIndex(n) IN
       /\ ~maintenanceQueue[i].ready
       /\ maintenanceQueue' =
            [maintenanceQueue EXCEPT ![i].ready = TRUE,
                                     ![i].needsRegather = FALSE]
  /\ UNCHANGED << installedSnapshot, installedRev,
                  consumerSnapshot, consumerRev, consumerInstalledEpoch,
                  acknowledgedSnapshot, acknowledgedRev,
                  currentIntent, explicit,
                  superseded,
                  nextMaintenance, lastAdmitted, admittedRequests, lastResult,
                  effectEpoch, effect, hostAuthority, nextSynchronization,
                  synchronizationRequest, settledSynchronizations,
                  admissionWitness,
                  regatherWitness,
                  revisionWitness, orderWitness, visibleWitness,
                  consumerSyncWitness, consumerAckWitness,
                  dispositionWitness, synchronizationWitness >>

\* A queued request whose basis is no longer the installed snapshot rebuilds
\* from the then-current snapshot instead of installing an older result.
RebuildMaintenance(n) ==
  /\ HasMaintenance(n)
  /\ LET i == MaintenanceIndex(n) IN
       /\ maintenanceQueue[i].basis # installedRev
       /\ maintenanceQueue' = [maintenanceQueue EXCEPT ![i].basis = installedRev,
                                                       ![i].ready = FALSE,
                                                       ![i].needsRegather = TRUE]
       /\ regatherWitness' =
            /\ regatherWitness
            /\ maintenanceQueue'[i].needsRegather
            /\ ~maintenanceQueue'[i].ready
  /\ UNCHANGED << installedSnapshot, installedRev,
                  consumerSnapshot, consumerRev, consumerInstalledEpoch,
                  acknowledgedSnapshot, acknowledgedRev,
                  currentIntent, explicit,
                  superseded,
                  nextMaintenance, lastAdmitted, admittedRequests, lastResult,
                  effectEpoch, effect, hostAuthority, nextSynchronization,
                  synchronizationRequest, settledSynchronizations,
                  admissionWitness,
                  revisionWitness,
                  orderWitness, visibleWitness,
                  consumerSyncWitness, consumerAckWitness,
                  dispositionWitness, synchronizationWitness >>

\* The design's admission predicate, stated once: only the oldest outstanding
\* request, only when it was rebuilt against the installed snapshot, only
\* with no unresolved explicit work, and only with no unconsumed effect.
MaintenanceAdmissible ==
  /\ maintenanceQueue # << >>
  /\ Head(maintenanceQueue).ready
  /\ Head(maintenanceQueue).basis = installedRev
  /\ ~Head(maintenanceQueue).needsRegather
  /\ explicit = NoExplicitWork
  /\ effect = NoAuthority

AdmitMaintenance ==
  /\ MaintenanceAdmissible
  /\ LET replacement ==
       CHOOSE snapshot \in SnapshotValues \ {installedSnapshot} : TRUE
     IN
       /\ installedSnapshot' = replacement
       /\ lastResult' =
            Result("maintenance",
                   ConsumerDisposition(replacement, installedRev + 1),
                   acknowledgedSnapshot, acknowledgedRev,
                   installedSnapshot, replacement,
                   installedRev, installedRev + 1)
       /\ dispositionWitness' =
            /\ dispositionWitness
            /\ CorrectDisposition(lastResult')
  /\ installedRev' = installedRev + 1
  /\ effectEpoch' = effectEpoch + 1
  /\ effect' = Authority("maintenance", installedRev + 1, currentIntent,
                         effectEpoch + 1)
  /\ hostAuthority' = effect'
  /\ lastAdmitted' = Head(maintenanceQueue).seq
  /\ admittedRequests' =
       admittedRequests \cup {Head(maintenanceQueue).seq}
  /\ maintenanceQueue' = Tail(maintenanceQueue)
  /\ admissionWitness' =
       /\ admissionWitness
       /\ explicit = NoExplicitWork
       /\ effect = NoAuthority
  /\ regatherWitness' =
       /\ regatherWitness
       /\ ~Head(maintenanceQueue).needsRegather
  /\ orderWitness' =
       /\ orderWitness
       /\ Head(maintenanceQueue).seq > lastAdmitted
       /\ \A e \in Range(maintenanceQueue) : Head(maintenanceQueue).seq <= e.seq
  /\ UNCHANGED << consumerSnapshot, consumerRev, consumerInstalledEpoch,
                  acknowledgedSnapshot, acknowledgedRev,
                  currentIntent, explicit, superseded, nextMaintenance,
                  nextSynchronization, synchronizationRequest,
                  settledSynchronizations,
                  revisionWitness, visibleWitness,
                  consumerSyncWitness, consumerAckWitness,
                  synchronizationWitness >>

\* Synchronization demand comes from the retained consumer.  Request identities
\* are bounded for model exploration; the product response path has no retry
\* ceiling.
RequestConsumerSynchronization ==
  /\ ConsumerAcknowledgementLags
  /\ nextSynchronization <= MaxSynchronization
  /\ synchronizationRequest = 0
  /\ effect = NoAuthority
  /\ hostAuthority = NoAuthority
  /\ synchronizationRequest' = nextSynchronization
  /\ nextSynchronization' = nextSynchronization + 1
  /\ UNCHANGED << installedSnapshot, installedRev,
                  consumerSnapshot, consumerRev, consumerInstalledEpoch,
                  acknowledgedSnapshot, acknowledgedRev,
                  currentIntent, explicit, superseded, nextMaintenance,
                  maintenanceQueue, lastAdmitted, admittedRequests,
                  lastResult, effectEpoch, effect, hostAuthority,
                  settledSynchronizations,
                  admissionWitness, regatherWitness, revisionWitness,
                  orderWitness, visibleWitness, consumerSyncWitness,
                  consumerAckWitness, dispositionWitness,
                  synchronizationWitness >>

\* A retained consumer that abandoned or lost authority while behind the
\* session can receive the complete current snapshot under fresh authority.
\* Pending maintenance drains first; any intervening current result can
\* discharge the same request when that result is acknowledged.
SynchronizeConsumer ==
  /\ synchronizationRequest # 0
  /\ ConsumerAcknowledgementLags
  /\ explicit = NoExplicitWork
  /\ maintenanceQueue = << >>
  /\ effect = NoAuthority
  /\ effectEpoch' = effectEpoch + 1
  /\ effect' =
       Authority("synchronize", installedRev, currentIntent, effectEpoch + 1)
  /\ hostAuthority' = effect'
  /\ lastResult' =
       Result("synchronize",
              ConsumerDisposition(installedSnapshot, installedRev),
              acknowledgedSnapshot, acknowledgedRev,
              installedSnapshot, installedSnapshot,
              installedRev, installedRev)
  /\ synchronizationRequest' = 0
  /\ settledSynchronizations' =
       settledSynchronizations \cup {synchronizationRequest}
  /\ dispositionWitness' =
       /\ dispositionWitness
       /\ CorrectDisposition(lastResult')
  /\ synchronizationWitness' =
       /\ synchronizationWitness
       /\ synchronizationRequest \notin settledSynchronizations
       /\ synchronizationRequest < nextSynchronization
       /\ synchronizationRequest' = 0
       /\ settledSynchronizations' =
            settledSynchronizations \cup {synchronizationRequest}
       /\ lastResult'.resultSnapshot = installedSnapshot
       /\ lastResult'.resultRev = installedRev
       /\ lastResult'.disposition = "synchronizationRequired"
  /\ UNCHANGED << installedSnapshot, installedRev,
                  consumerSnapshot, consumerRev, consumerInstalledEpoch,
                  acknowledgedSnapshot, acknowledgedRev,
                  currentIntent, explicit,
                  superseded, nextMaintenance, maintenanceQueue, lastAdmitted,
                  admittedRequests, nextSynchronization,
                  admissionWitness, regatherWitness,
                  revisionWitness, orderWitness, visibleWitness,
                  consumerSyncWitness, consumerAckWitness >>

(***************************************************************************)
(* Consumer side.                                                          *)
(*                                                                         *)
(* A consumer validates returned authority before each visible effect.      *)
(* Earlier validation is not continuing authority.                          *)
(***************************************************************************)
VisibleEffect ==
  /\ hostAuthority # NoAuthority
  /\ hostAuthority = effect
  /\ consumerSnapshot' = lastResult.resultSnapshot
  /\ consumerRev' = lastResult.resultRev
  /\ consumerInstalledEpoch' = hostAuthority.epoch
  /\ visibleWitness' =
       /\ visibleWitness
       /\ hostAuthority.session = SessionId
       /\ hostAuthority.intent = currentIntent
       /\ hostAuthority.epoch = effectEpoch
       /\ hostAuthority.rev = installedRev
  /\ consumerSyncWitness' =
       /\ consumerSyncWitness
       /\ consumerSnapshot' = lastResult.resultSnapshot
       /\ consumerRev' = lastResult.resultRev
       /\ consumerInstalledEpoch' = hostAuthority.epoch
       /\ lastResult.resultSnapshot = installedSnapshot
       /\ lastResult.resultRev = installedRev
       /\ hostAuthority.rev = lastResult.resultRev
  /\ UNCHANGED << installedSnapshot, installedRev, currentIntent, explicit,
                  acknowledgedSnapshot, acknowledgedRev, superseded,
                  nextMaintenance, maintenanceQueue, lastAdmitted,
                  admittedRequests,
                  lastResult, effectEpoch, effect, hostAuthority,
                  nextSynchronization, synchronizationRequest,
                  settledSynchronizations,
                  admissionWitness, regatherWitness, revisionWitness,
                  orderWitness, consumerAckWitness, dispositionWitness,
                  synchronizationWitness >>

\* The consumer completed the authority-guarded effect.  Acknowledgement
\* releases queued maintenance.
AcknowledgeEffect ==
  /\ hostAuthority # NoAuthority
  /\ hostAuthority = effect
  /\ consumerRev = installedRev
  /\ consumerSnapshot = installedSnapshot
  /\ consumerInstalledEpoch = effectEpoch
  /\ acknowledgedSnapshot' = consumerSnapshot
  /\ acknowledgedRev' = consumerRev
  /\ effect' = NoAuthority
  /\ hostAuthority' = NoAuthority
  /\ settledSynchronizations' =
       IF synchronizationRequest = 0
         THEN settledSynchronizations
         ELSE settledSynchronizations \cup {synchronizationRequest}
  /\ synchronizationRequest' = 0
  /\ consumerAckWitness' =
       /\ consumerAckWitness
       /\ consumerRev = installedRev
       /\ consumerSnapshot = installedSnapshot
       /\ consumerInstalledEpoch = effectEpoch
       /\ acknowledgedRev' = consumerRev
       /\ acknowledgedSnapshot' = consumerSnapshot
  /\ synchronizationWitness' =
       /\ synchronizationWitness
       /\ (synchronizationRequest = 0 \/
             /\ synchronizationRequest \notin settledSynchronizations
             /\ synchronizationRequest < nextSynchronization)
       /\ synchronizationRequest' = 0
       /\ settledSynchronizations' =
            IF synchronizationRequest = 0
              THEN settledSynchronizations
              ELSE settledSynchronizations \cup {synchronizationRequest}
  /\ UNCHANGED << installedSnapshot, installedRev,
                  consumerSnapshot, consumerRev, consumerInstalledEpoch,
                  currentIntent, explicit,
                  superseded,
                  nextMaintenance, maintenanceQueue, lastAdmitted,
                  admittedRequests,
                  lastResult, effectEpoch, nextSynchronization,
                  admissionWitness, regatherWitness,
                  revisionWitness, orderWitness, visibleWitness,
                  consumerSyncWitness, dispositionWitness >>

\* A consumer that cannot complete the effect abandons its authority.
\* Abandonment also releases queued maintenance.
AbandonEffect ==
  /\ hostAuthority # NoAuthority
  /\ hostAuthority' = NoAuthority
  /\ effect' = IF hostAuthority = effect THEN NoAuthority ELSE effect
  /\ UNCHANGED << installedSnapshot, installedRev,
                  consumerSnapshot, consumerRev, consumerInstalledEpoch,
                  acknowledgedSnapshot, acknowledgedRev,
                  currentIntent, explicit,
                  superseded,
                  nextMaintenance, maintenanceQueue, lastAdmitted,
                  admittedRequests,
                  lastResult, effectEpoch, nextSynchronization,
                  synchronizationRequest, settledSynchronizations,
                  admissionWitness, regatherWitness,
                  revisionWitness, orderWitness, visibleWitness,
                  consumerSyncWitness, consumerAckWitness,
                  dispositionWitness, synchronizationWitness >>

\* A consumer is handed authority minted by a different navigation session.
ForeignAuthorityOffered ==
  /\ hostAuthority = NoAuthority
  /\ hostAuthority' = ForeignAuthority
  /\ UNCHANGED << installedSnapshot, installedRev,
                  consumerSnapshot, consumerRev, consumerInstalledEpoch,
                  acknowledgedSnapshot, acknowledgedRev,
                  currentIntent, explicit,
                  superseded,
                  nextMaintenance, maintenanceQueue, lastAdmitted,
                  admittedRequests,
                  lastResult, effectEpoch, effect, nextSynchronization,
                  synchronizationRequest, settledSynchronizations,
                  admissionWitness,
                  regatherWitness, revisionWitness, orderWitness,
                  visibleWitness, consumerSyncWitness, consumerAckWitness,
                  dispositionWitness, synchronizationWitness >>

ResolveExplicit ==
  \/ \E returnedSnapshot \in SnapshotValues :
       ExplicitResultInstalls(returnedSnapshot)
  \/ \E returnedSnapshot \in SnapshotValues :
       ExplicitUnavailable(returnedSnapshot)
  \/ \E outcome \in {"rejected", "failed"} : ExplicitResultRetains(outcome)
  \/ ExternalPrerequisiteAbort

Next ==
  \/ \E kind \in IntentKinds : BeginExplicitIntent(kind)
  \/ ResolveExplicit
  \/ \E token \in 1 .. MaxIntent : SupersededResultDiscarded(token)
  \/ RequestMaintenance
  \/ \E n \in 1 .. MaxMaintenance : GatherMaintenanceFacts(n)
  \/ \E n \in 1 .. MaxMaintenance : RebuildMaintenance(n)
  \/ AdmitMaintenance
  \/ RequestConsumerSynchronization
  \/ SynchronizeConsumer
  \/ VisibleEffect
  \/ AcknowledgeEffect
  \/ AbandonEffect
  \/ ForeignAuthorityOffered

Fairness ==
  /\ WF_vars(ResolveExplicit)
  /\ \A n \in 1 .. MaxMaintenance : WF_vars(GatherMaintenanceFacts(n))
  /\ \A n \in 1 .. MaxMaintenance : WF_vars(RebuildMaintenance(n))
  /\ WF_vars(AdmitMaintenance)
  /\ WF_vars(SynchronizeConsumer)
  /\ WF_vars(VisibleEffect)
  /\ WF_vars(AcknowledgeEffect)
  /\ \A token \in 1 .. MaxIntent : WF_vars(SupersededResultDiscarded(token))

Spec == Init /\ [][Next]_vars /\ Fairness

(***************************************************************************)
(* Invariants.                                                             *)
(***************************************************************************)

\* Latest-intent safety: the newest explicit intent owns the session.  Any
\* unresolved explicit work carries the current token, every superseded
\* operation carries a strictly older one, and unconsumed authority is
\* always the current intent's.
LatestIntentSafety ==
  /\ (explicit # NoExplicitWork => explicit.token = currentIntent)
  /\ \A token \in superseded : token < currentIntent
  /\ (effect # NoAuthority => effect.intent = currentIntent)

\* Exact current authority: at most one unconsumed authority exists and all
\* four of its components match the retaining session right now.
ExactCurrentAuthority ==
  effect # NoAuthority =>
    /\ effect.session = SessionId
    /\ effect.rev = installedRev
    /\ effect.intent = currentIntent
    /\ effect.epoch = effectEpoch

\* No maintenance admission during unresolved explicit work or unconsumed
\* effects.
MaintenanceAdmissionDiscipline == admissionWitness

\* Maintenance is admitted in owner-issued request order, never completion
\* order, and the queue itself stays ordered and outstanding.
MaintenanceRequestOrder ==
  /\ orderWitness
  /\ \A i, j \in DOMAIN maintenanceQueue :
       i < j => maintenanceQueue[i].seq < maintenanceQueue[j].seq
  /\ \A i \in DOMAIN maintenanceQueue : maintenanceQueue[i].seq > lastAdmitted

\* Every consumer-visible effect executed under exactly the session's current
\* unconsumed authority.
NoStaleVisibleEffect == visibleWitness

\* A stale request cannot be admitted until rebuilding has explicitly required
\* and subsequent fact gathering has completed its re-gather.
MaintenanceRegatherDiscipline ==
  /\ regatherWitness
  /\ \A e \in Range(maintenanceQueue) : e.needsRegather => ~e.ready

\* An unavailable outcome advances the state revision exactly when the
\* complete returned snapshot changed.  The semantic outcome and change bit
\* are explicit model currencies rather than inferred from apply/retain class.
UnavailableRevisionMatchesSnapshotChange ==
  /\ revisionWitness
  /\ (lastResult.outcome = "unavailable" =>
        /\ lastResult.snapshotChanged =
             (lastResult.resultSnapshot # lastResult.priorSnapshot)
        /\ IF lastResult.snapshotChanged
             THEN lastResult.resultRev = lastResult.priorRev + 1
             ELSE lastResult.resultRev = lastResult.priorRev)

\* Consumer-installed and product-acknowledged state are separate currencies.
\* Neither may lead its authority source, and equal revisions mean equal
\* complete snapshots rather than merely matching counters.
ConsumerSynchronizationShape ==
  /\ acknowledgedRev <= consumerRev
  /\ consumerRev <= installedRev
  /\ (acknowledgedRev = consumerRev =>
        acknowledgedSnapshot = consumerSnapshot)
  /\ (consumerRev = installedRev =>
        consumerSnapshot = installedSnapshot)

\* Every consumer-visible effect installs the complete current snapshot carried
\* by the authority before acknowledgement can release the effect.
ConsumerVisibleEffectSynchronizes == consumerSyncWitness

\* Acknowledgement is never a success-shaped release while product and
\* consumer snapshots differ.
AcknowledgementRequiresConsumerSynchronization == consumerAckWitness

\* Every result-producing action derives the typed disposition from the
\* product-owned acknowledgement receipt, independently of semantic outcome.
CurrentResultDispositionIsExact ==
  /\ dispositionWitness
  /\ (lastResult.outcome = "none" \/ CorrectDisposition(lastResult))

\* Each bounded external request retains its exact identity until a current
\* result settles it; no response-side retry ceiling exists.
SynchronizationRequestDiscipline == synchronizationWitness

\* A synchronization result and its authority always name the complete current
\* product snapshot and revision.
SynchronizationAuthorityIsCurrent ==
  effect.outcome = "synchronize" =>
    /\ lastResult.outcome = "synchronize"
    /\ lastResult.disposition = "synchronizationRequired"
    /\ lastResult.resultSnapshot = installedSnapshot
    /\ lastResult.resultRev = installedRev
    /\ effect.rev = installedRev

(***************************************************************************)
(* Liveness.                                                               *)
(*                                                                         *)
(* Explicit intents are bounded, so after the last one the queue must       *)
(* drain.  Progress is stated per request, not only for the whole queue: a  *)
(* request that is blocked by unresolved explicit work, by an unconsumed    *)
(* effect, or by an owed rebuild must still be admitted once that blocker   *)
(* resolves.  Those are the properties that make abort, acknowledgement,    *)
(* abandonment, and rebuild real release paths instead of stated ones.      *)
(***************************************************************************)
ExplicitWorkEventuallyResolves ==
  (explicit # NoExplicitWork) ~> (explicit = NoExplicitWork)

\* The same claim per intent token.  The aggregate property above can be
\* discharged by a newer intent resolving, which says nothing about the older
\* operation.  This one names the token: the operation that carried it must
\* stop being in flight and must also stop being an outstanding superseded
\* result, so supersession is a settlement rather than an open end.
EveryExplicitIntentSettles ==
  \A token \in 1 .. MaxIntent :
    (explicit # NoExplicitWork /\ explicit.token = token)
      ~> (explicit.token # token /\ token \notin superseded)

EffectEventuallyConsumed ==
  (effect # NoAuthority) ~> (effect = NoAuthority)

MaintenanceEventuallyDrains ==
  (maintenanceQueue # << >>) ~> (maintenanceQueue = << >>)

\* Every queued request is eventually admitted, so the head advances and the
\* request leaves the queue rather than sitting at the front forever.
EveryQueuedRequestIsAdmitted ==
  \A n \in 1 .. MaxMaintenance : HasMaintenance(n) ~> (n \in admittedRequests)

\* Every synchronization request that the bounded environment issues receives
\* dedicated fresh authority or is discharged by acknowledgement of another
\* current result carrying the complete product snapshot.
EverySynchronizationRequestSettles ==
  \A n \in 1 .. MaxSynchronization :
    (n < nextSynchronization) ~> (n \in settledSynchronizations)

\* Blocked by unresolved explicit work or by an unconsumed effect: the
\* explicit operation must resolve and the effect must be acknowledged or
\* abandoned before this request can be admitted, and it still is.
BlockedMaintenanceResumes ==
  \A n \in 1 .. MaxMaintenance :
    (HasMaintenance(n) /\ (explicit # NoExplicitWork \/ effect # NoAuthority))
      ~> (n \in admittedRequests)

\* Blocked behind an external prerequisite abort specifically: the abort
\* effect must be acknowledged or abandoned before maintenance resumes.
MaintenanceResumesAfterAbort ==
  \A n \in 1 .. MaxMaintenance :
    (HasMaintenance(n) /\ effect.outcome = "aborted")
      ~> (n \in admittedRequests)

\* Blocked by a basis a newer snapshot invalidated: the request must rebuild
\* and re-gather before it can be admitted, and it still is.
StaleBasisMaintenanceResumes ==
  \A n \in 1 .. MaxMaintenance :
    (HasMaintenance(n) /\ MaintenanceEntry(n).basis # installedRev)
      ~> (n \in admittedRequests)

=============================================================================
