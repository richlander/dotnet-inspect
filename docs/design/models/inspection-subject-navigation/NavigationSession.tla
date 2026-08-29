-------------------------- MODULE NavigationSession --------------------------
(***************************************************************************)
(* Design model of the retained Inspection Subject Navigation session.     *)
(*                                                                         *)
(* The model checks the ordering, supersession, and authority rules of the *)
(* design in `docs/design/inspection-subject-navigation.md`.  It models    *)
(* non-success revision behavior, but not descriptor classification,       *)
(* identity ranking, lens contents, rendering, or any implementation.       *)
(*                                                                         *)
(* Product concept                    Model variable                       *)
(*   installed navigation snapshot      installedSnapshot                  *)
(*   installed snapshot revision        installedRev                       *)
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
(*                                                                         *)
(* Guard witnesses are latching booleans.  Each guarded result, admission, *)
(* or visible effect re-derives the condition the design requires rather   *)
(* than trusting the action's guard or assignments.                         *)
(***************************************************************************)
EXTENDS Naturals, Sequences, FiniteSets

CONSTANTS
  MaxIntent,        \* how many explicit intents one behaviour may issue
  MaxMaintenance,   \* how many standalone maintenance requests it may issue
  IntentKinds,      \* subject, lens, coordinate, and canonical restoration
  SnapshotValues,   \* finite complete-snapshot contents
  InitialSnapshot,  \* content retained before the first modelled result
  SessionId,        \* the identity of this retained navigation session
  ForeignSessionId  \* some other session, used only for foreign authority

ASSUME MaxIntent \in Nat /\ MaxMaintenance \in Nat
ASSUME InitialSnapshot \in SnapshotValues /\ Cardinality(SnapshotValues) > 1
ASSUME SessionId # ForeignSessionId

VARIABLES
  installedSnapshot,
  installedRev,
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
  admissionWitness,
  regatherWitness,
  revisionWitness,
  orderWitness,
  visibleWitness

vars == << installedSnapshot, installedRev, currentIntent, explicit,
           superseded, nextMaintenance, maintenanceQueue, lastAdmitted,
           admittedRequests, lastResult, effectEpoch, effect, hostAuthority,
           admissionWitness, regatherWitness, revisionWitness, orderWitness,
           visibleWitness >>

(***************************************************************************)
(* Currencies.                                                             *)
(*                                                                         *)
(* An effect authority is the four-part product currency: session          *)
(* identity, snapshot state revision, intent token, and effect epoch.  The *)
(* outcome class records which kind of result carried it.                  *)
(***************************************************************************)
Outcomes == {"applied", "retained", "aborted", "maintenance"}
SemanticOutcomes ==
  {"applied", "unavailable", "rejected", "failed", "aborted", "maintenance"}
ResultSources == {"none", "evaluation", "navigationPreparation"}

NoResult ==
  [ outcome         |-> "none",
    source          |-> "none",
    snapshotChanged |-> FALSE,
    priorSnapshot   |-> InitialSnapshot,
    resultSnapshot  |-> InitialSnapshot,
    priorRev        |-> 0,
    resultRev       |-> 0 ]

Result(outcome, source, priorSnapshot, resultSnapshot, priorRev, resultRev) ==
  [ outcome         |-> outcome,
    source          |-> source,
    snapshotChanged |-> resultSnapshot # priorSnapshot,
    priorSnapshot   |-> priorSnapshot,
    resultSnapshot  |-> resultSnapshot,
    priorRev        |-> priorRev,
    resultRev       |-> resultRev ]

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
  /\ currentIntent \in 0 .. MaxIntent
  /\ explicit.token \in 0 .. MaxIntent
  /\ explicit.kind \in IntentKinds \cup {"none"}
  /\ superseded \subseteq 1 .. MaxIntent
  /\ nextMaintenance \in 1 .. (MaxMaintenance + 1)
  /\ lastAdmitted \in 0 .. MaxMaintenance
  /\ admittedRequests \subseteq 1 .. MaxMaintenance
  /\ lastResult.outcome \in SemanticOutcomes \cup {"none"}
  /\ lastResult.source \in ResultSources
  /\ lastResult.snapshotChanged \in BOOLEAN
  /\ lastResult.priorSnapshot \in SnapshotValues
  /\ lastResult.resultSnapshot \in SnapshotValues
  /\ lastResult.priorRev \in Nat
  /\ lastResult.resultRev \in Nat
  /\ effectEpoch \in Nat
  /\ effect.outcome \in Outcomes \cup {"none"}
  /\ hostAuthority.outcome \in Outcomes \cup {"none"}
  /\ \A i \in DOMAIN maintenanceQueue :
       /\ maintenanceQueue[i].seq \in 1 .. MaxMaintenance
       /\ maintenanceQueue[i].ready \in BOOLEAN
       /\ maintenanceQueue[i].basis \in Nat
       /\ maintenanceQueue[i].needsRegather \in BOOLEAN
  /\ regatherWitness \in BOOLEAN
  /\ revisionWitness \in BOOLEAN

Init ==
  /\ installedSnapshot = InitialSnapshot
  /\ installedRev = 0
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
  /\ admissionWitness = TRUE
  /\ regatherWitness = TRUE
  /\ revisionWitness = TRUE
  /\ orderWitness = TRUE
  /\ visibleWitness = TRUE

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
  /\ UNCHANGED << installedSnapshot, installedRev, nextMaintenance,
                  lastAdmitted, admittedRequests, lastResult, effectEpoch,
                  hostAuthority,
                  admissionWitness, regatherWitness, revisionWitness,
                  orderWitness, visibleWitness >>

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
       Result("applied", "none", installedSnapshot, returnedSnapshot,
              installedRev, installedRev + 1)
  /\ explicit' = NoExplicitWork
  /\ UNCHANGED << currentIntent, superseded, nextMaintenance,
                  maintenanceQueue, lastAdmitted, admittedRequests,
                  admissionWitness,
                  regatherWitness, revisionWitness, orderWitness,
                  visibleWitness >>

\* A completed unavailable or failed result returns a complete snapshot value.
\* Change is derived by comparing that value with the installed snapshot, not
\* supplied as an independent choice.
ExplicitNonSuccess(outcome, returnedSnapshot) ==
  /\ outcome \in {"unavailable", "failed"}
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
            Result(outcome,
                   IF outcome = "failed" THEN "evaluation" ELSE "none",
                   installedSnapshot, returnedSnapshot,
                   installedRev, installedRev')
       /\ revisionWitness' =
            /\ revisionWitness
            /\ installedSnapshot' = lastResult'.resultSnapshot
            /\ installedRev' = lastResult'.resultRev
            /\ lastResult'.priorSnapshot = installedSnapshot
            /\ lastResult'.priorRev = installedRev
  /\ explicit' = NoExplicitWork
  /\ UNCHANGED << currentIntent, superseded, nextMaintenance,
                  maintenanceQueue, lastAdmitted, admittedRequests,
                  admissionWitness,
                  regatherWitness, orderWitness, visibleWitness >>

\* Navigation preparation can fail after Registry evaluation succeeds.  It
\* returns a distinguishable failed result and retains the complete snapshot
\* and revision because it has no installable replacement snapshot.
NavigationPreparationFailure ==
  /\ explicit # NoExplicitWork
  /\ explicit.token = currentIntent
  /\ installedSnapshot' = installedSnapshot
  /\ installedRev' = installedRev
  /\ effectEpoch' = effectEpoch + 1
  /\ effect' =
       Authority("retained", installedRev, currentIntent, effectEpoch + 1)
  /\ hostAuthority' = effect'
  /\ lastResult' =
       Result("failed", "navigationPreparation",
              installedSnapshot, installedSnapshot,
              installedRev, installedRev)
  /\ revisionWitness' =
       /\ revisionWitness
       /\ installedSnapshot' = installedSnapshot
       /\ installedRev' = installedRev
       /\ lastResult'.priorSnapshot = installedSnapshot
       /\ lastResult'.resultSnapshot = installedSnapshot'
       /\ lastResult'.priorRev = installedRev
       /\ lastResult'.resultRev = installedRev'
       /\ lastResult'.outcome = "failed"
       /\ lastResult'.source = "navigationPreparation"
       /\ effect' =
            Authority("retained", installedRev, currentIntent, effectEpoch + 1)
       /\ hostAuthority' = effect'
  /\ explicit' = NoExplicitWork
  /\ UNCHANGED << currentIntent, superseded, nextMaintenance,
                  maintenanceQueue, lastAdmitted,
                  admittedRequests,
                  admissionWitness, regatherWitness, orderWitness,
                  visibleWitness >>

\* A rejected navigation result retains the installed snapshot but receives a
\* fresh effect epoch so delayed outcome work cannot surface later.
ExplicitRejected ==
  /\ explicit # NoExplicitWork
  /\ explicit.token = currentIntent
  /\ effectEpoch' = effectEpoch + 1
  /\ effect' =
       Authority("retained", installedRev, currentIntent, effectEpoch + 1)
  /\ hostAuthority' = effect'
  /\ lastResult' =
       Result("rejected", "none", installedSnapshot, installedSnapshot,
              installedRev, installedRev)
  /\ explicit' = NoExplicitWork
  /\ UNCHANGED << installedSnapshot, installedRev, currentIntent, superseded,
                  nextMaintenance, maintenanceQueue, lastAdmitted,
                  admittedRequests,
                  admissionWitness, regatherWitness, revisionWitness,
                  orderWitness, visibleWitness >>

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
       Result("aborted", "none", installedSnapshot, installedSnapshot,
              installedRev, installedRev)
  /\ explicit' = NoExplicitWork
  /\ UNCHANGED << installedSnapshot, installedRev, currentIntent, superseded,
                  nextMaintenance, maintenanceQueue, lastAdmitted,
                  admittedRequests,
                  admissionWitness, regatherWitness, revisionWitness,
                  orderWitness, visibleWitness >>

\* A superseded explicit operation returns late.  It produces no visible
\* effect and cannot install.
SupersededResultDiscarded(token) ==
  /\ token \in superseded
  /\ superseded' = superseded \ {token}
  /\ UNCHANGED << installedSnapshot, installedRev, currentIntent, explicit,
                  nextMaintenance,
                  maintenanceQueue, lastAdmitted, admittedRequests,
                  effectEpoch, effect,
                  hostAuthority, lastResult, admissionWitness,
                  regatherWitness, revisionWitness, orderWitness,
                  visibleWitness >>

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
  /\ UNCHANGED << installedSnapshot, installedRev, currentIntent, explicit,
                  superseded,
                  lastAdmitted, admittedRequests, lastResult, effectEpoch,
                  effect, hostAuthority,
                  admissionWitness, regatherWitness, revisionWitness,
                  orderWitness, visibleWitness >>

\* Facts for one queued request finish gathering.  Any request may finish
\* first; completion timing must not select the final snapshot.
GatherMaintenanceFacts(n) ==
  /\ HasMaintenance(n)
  /\ LET i == MaintenanceIndex(n) IN
       /\ ~maintenanceQueue[i].ready
       /\ maintenanceQueue' =
            [maintenanceQueue EXCEPT ![i].ready = TRUE,
                                     ![i].needsRegather = FALSE]
  /\ UNCHANGED << installedSnapshot, installedRev, currentIntent, explicit,
                  superseded,
                  nextMaintenance, lastAdmitted, admittedRequests, lastResult,
                  effectEpoch, effect, hostAuthority, admissionWitness,
                  regatherWitness,
                  revisionWitness, orderWitness, visibleWitness >>

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
  /\ UNCHANGED << installedSnapshot, installedRev, currentIntent, explicit,
                  superseded,
                  nextMaintenance, lastAdmitted, admittedRequests, lastResult,
                  effectEpoch, effect, hostAuthority, admissionWitness,
                  revisionWitness,
                  orderWitness, visibleWitness >>

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
            Result("maintenance", "none", installedSnapshot, replacement,
                   installedRev, installedRev + 1)
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
  /\ UNCHANGED << currentIntent, explicit, superseded, nextMaintenance,
                  revisionWitness, visibleWitness >>

(***************************************************************************)
(* Consumer side.                                                          *)
(*                                                                         *)
(* A consumer validates returned authority before each visible effect.      *)
(* Earlier validation is not continuing authority.                          *)
(***************************************************************************)
VisibleEffect ==
  /\ hostAuthority # NoAuthority
  /\ hostAuthority = effect
  /\ visibleWitness' =
       /\ visibleWitness
       /\ hostAuthority.session = SessionId
       /\ hostAuthority.intent = currentIntent
       /\ hostAuthority.epoch = effectEpoch
       /\ hostAuthority.rev = installedRev
  /\ UNCHANGED << installedSnapshot, installedRev, currentIntent, explicit,
                  superseded,
                  nextMaintenance, maintenanceQueue, lastAdmitted,
                  admittedRequests,
                  lastResult, effectEpoch, effect, hostAuthority,
                  admissionWitness, regatherWitness, revisionWitness,
                  orderWitness >>

\* The consumer completed the authority-guarded effect.  Acknowledgement
\* releases queued maintenance.
AcknowledgeEffect ==
  /\ hostAuthority # NoAuthority
  /\ hostAuthority = effect
  /\ effect' = NoAuthority
  /\ hostAuthority' = NoAuthority
  /\ UNCHANGED << installedSnapshot, installedRev, currentIntent, explicit,
                  superseded,
                  nextMaintenance, maintenanceQueue, lastAdmitted,
                  admittedRequests,
                  lastResult, effectEpoch, admissionWitness, regatherWitness,
                  revisionWitness, orderWitness, visibleWitness >>

\* A consumer that cannot complete the effect abandons its authority.
\* Abandonment also releases queued maintenance.
AbandonEffect ==
  /\ hostAuthority # NoAuthority
  /\ hostAuthority' = NoAuthority
  /\ effect' = IF hostAuthority = effect THEN NoAuthority ELSE effect
  /\ UNCHANGED << installedSnapshot, installedRev, currentIntent, explicit,
                  superseded,
                  nextMaintenance, maintenanceQueue, lastAdmitted,
                  admittedRequests,
                  lastResult, effectEpoch, admissionWitness, regatherWitness,
                  revisionWitness, orderWitness, visibleWitness >>

\* A consumer is handed authority minted by a different navigation session.
ForeignAuthorityOffered ==
  /\ hostAuthority = NoAuthority
  /\ hostAuthority' = ForeignAuthority
  /\ UNCHANGED << installedSnapshot, installedRev, currentIntent, explicit,
                  superseded,
                  nextMaintenance, maintenanceQueue, lastAdmitted,
                  admittedRequests,
                  lastResult, effectEpoch, effect, admissionWitness,
                  regatherWitness, revisionWitness, orderWitness,
                  visibleWitness >>

ResolveExplicit ==
  \/ \E returnedSnapshot \in SnapshotValues :
       ExplicitResultInstalls(returnedSnapshot)
  \/ \E outcome \in {"unavailable", "failed"},
          returnedSnapshot \in SnapshotValues :
       ExplicitNonSuccess(outcome, returnedSnapshot)
  \/ NavigationPreparationFailure
  \/ ExplicitRejected
  \/ ExternalPrerequisiteAbort

Next ==
  \/ \E kind \in IntentKinds : BeginExplicitIntent(kind)
  \/ ResolveExplicit
  \/ \E token \in 1 .. MaxIntent : SupersededResultDiscarded(token)
  \/ RequestMaintenance
  \/ \E n \in 1 .. MaxMaintenance : GatherMaintenanceFacts(n)
  \/ \E n \in 1 .. MaxMaintenance : RebuildMaintenance(n)
  \/ AdmitMaintenance
  \/ VisibleEffect
  \/ AcknowledgeEffect
  \/ AbandonEffect
  \/ ForeignAuthorityOffered

Fairness ==
  /\ WF_vars(ResolveExplicit)
  /\ \A n \in 1 .. MaxMaintenance : WF_vars(GatherMaintenanceFacts(n))
  /\ \A n \in 1 .. MaxMaintenance : WF_vars(RebuildMaintenance(n))
  /\ WF_vars(AdmitMaintenance)
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

\* A completed unavailable or failed outcome advances the state revision
\* exactly when the complete returned snapshot changed.  The semantic outcome
\* and change bit are explicit model currencies rather than inferred from
\* apply/retain class.
NonSuccessRevisionMatchesSnapshotChange ==
  /\ revisionWitness
  /\ (lastResult.outcome \in {"unavailable", "failed"} =>
        /\ lastResult.snapshotChanged =
             (lastResult.resultSnapshot # lastResult.priorSnapshot)
        /\ IF lastResult.snapshotChanged
             THEN lastResult.resultRev = lastResult.priorRev + 1
             ELSE lastResult.resultRev = lastResult.priorRev)

\* Navigation preparation failure has no complete replacement snapshot to
\* install.  Its distinguishable result therefore records identical
\* before/after state, and the live result authority names that retained
\* revision.  The pre-state witness also independently latches the source and
\* full returned authority.
PreparationFailureRetainsSnapshotAndRevision ==
  /\ revisionWitness
  /\ (lastResult.source = "navigationPreparation" =>
        /\ ~lastResult.snapshotChanged
        /\ lastResult.resultSnapshot = lastResult.priorSnapshot
        /\ lastResult.resultRev = lastResult.priorRev
        /\ (effect # NoAuthority =>
              /\ effect.outcome = "retained"
              /\ effect.rev = lastResult.resultRev
              /\ installedSnapshot = lastResult.resultSnapshot
              /\ installedRev = lastResult.resultRev))

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
