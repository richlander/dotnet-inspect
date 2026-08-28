-------------------------- MODULE NavigationSession --------------------------
(***************************************************************************)
(* Design model of the retained Inspection Subject Navigation session.     *)
(*                                                                         *)
(* The model checks the ordering, supersession, and authority rules of the *)
(* design in `docs/design/inspection-subject-navigation.md`.  It says      *)
(* nothing about identity ranking, availability classification, lens       *)
(* contents, rendering, or any implementation.                             *)
(*                                                                         *)
(* Product concept                    Model variable                       *)
(*   installed navigation snapshot      installedRev (0 = none retained)   *)
(*   product-issued explicit intent     currentIntent                      *)
(*   unresolved explicit operation      explicit                           *)
(*   superseded explicit operation      superseded                         *)
(*   owner-issued maintenance number    nextMaintenance                    *)
(*   standalone maintenance queue       maintenanceQueue (request order)   *)
(*   last admitted maintenance          lastAdmitted                       *)
(*   effect epoch                       effectEpoch                        *)
(*   unconsumed effect authority        effect                             *)
(*   authority held by a consumer       hostAuthority                      *)
(*                                                                         *)
(* Guard witnesses.  `admissionWitness`, `orderWitness`, and               *)
(* `visibleWitness` are latching booleans.  Each step that admits          *)
(* maintenance or executes a visible effect re-derives, independently of   *)
(* the action's own guard, the exact condition the design requires and     *)
(* conjoins it into the witness.  The paired invariant then fails if any   *)
(* future weakening of an action guard lets a step happen without that     *)
(* condition.  They are model bookkeeping, not product state.              *)
(***************************************************************************)
EXTENDS Naturals, Sequences

CONSTANTS
  MaxIntent,        \* how many explicit intents one behaviour may issue
  MaxMaintenance,   \* how many standalone maintenance requests it may issue
  IntentKinds,      \* subject, lens, coordinate, and canonical restoration
  SessionId,        \* the identity of this retained navigation session
  ForeignSessionId  \* some other session, used only for foreign authority

ASSUME MaxIntent \in Nat /\ MaxMaintenance \in Nat
ASSUME SessionId # ForeignSessionId

VARIABLES
  installedRev,
  currentIntent,
  explicit,
  superseded,
  nextMaintenance,
  maintenanceQueue,
  lastAdmitted,
  effectEpoch,
  effect,
  hostAuthority,
  admissionWitness,
  orderWitness,
  visibleWitness

vars == << installedRev, currentIntent, explicit, superseded, nextMaintenance,
           maintenanceQueue, lastAdmitted, effectEpoch, effect, hostAuthority,
           admissionWitness, orderWitness, visibleWitness >>

(***************************************************************************)
(* Currencies.                                                             *)
(*                                                                         *)
(* An effect authority is the four-part product currency: session          *)
(* identity, snapshot state revision, intent token, and effect epoch.  The *)
(* outcome class records which kind of result carried it.                  *)
(***************************************************************************)
Outcomes == {"applied", "retained", "aborted", "maintenance"}

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
  /\ installedRev \in Nat
  /\ currentIntent \in 0 .. MaxIntent
  /\ explicit.token \in 0 .. MaxIntent
  /\ explicit.kind \in IntentKinds \cup {"none"}
  /\ superseded \subseteq 1 .. MaxIntent
  /\ nextMaintenance \in 1 .. (MaxMaintenance + 1)
  /\ lastAdmitted \in 0 .. MaxMaintenance
  /\ effectEpoch \in Nat
  /\ effect.outcome \in Outcomes \cup {"none"}
  /\ hostAuthority.outcome \in Outcomes \cup {"none"}
  /\ \A i \in DOMAIN maintenanceQueue :
       /\ maintenanceQueue[i].seq \in 1 .. MaxMaintenance
       /\ maintenanceQueue[i].ready \in BOOLEAN
       /\ maintenanceQueue[i].basis \in Nat

Init ==
  /\ installedRev = 0
  /\ currentIntent = 0
  /\ explicit = NoExplicitWork
  /\ superseded = {}
  /\ nextMaintenance = 1
  /\ maintenanceQueue = << >>
  /\ lastAdmitted = 0
  /\ effectEpoch = 0
  /\ effect = NoAuthority
  /\ hostAuthority = NoAuthority
  /\ admissionWitness = TRUE
  /\ orderWitness = TRUE
  /\ visibleWitness = TRUE

(***************************************************************************)
(* Explicit intent.                                                        *)
(*                                                                         *)
(* Beginning an explicit subject, lens, coordinate, or canonical           *)
(* restoration operation issues a new monotonic token and immediately      *)
(* supersedes older explicit work, invalidates unconsumed authority, and   *)
(* forces queued maintenance to rebuild rather than install an older       *)
(* result.                                                                 *)
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
  /\ UNCHANGED << installedRev, nextMaintenance, lastAdmitted, effectEpoch,
                  hostAuthority, admissionWitness, orderWitness,
                  visibleWitness >>

\* An `Applied` outcome: the explicit operation installs a replacement
\* snapshot and returns fresh authority under its own intent token.
ExplicitResultInstalls ==
  /\ explicit # NoExplicitWork
  /\ explicit.token = currentIntent
  /\ installedRev' = installedRev + 1
  /\ effectEpoch' = effectEpoch + 1
  /\ effect' = Authority("applied", installedRev + 1, currentIntent, effectEpoch + 1)
  /\ hostAuthority' = effect'
  /\ explicit' = NoExplicitWork
  /\ UNCHANGED << currentIntent, superseded, nextMaintenance,
                  maintenanceQueue, lastAdmitted, admissionWitness,
                  orderWitness, visibleWitness >>

\* An `Unavailable`, `Rejected`, or `Failed` outcome: the snapshot revision is
\* retained, but the result still gets its own effect epoch so an older
\* deferred outcome cannot surface under it.
ExplicitResultRetains ==
  /\ explicit # NoExplicitWork
  /\ explicit.token = currentIntent
  /\ effectEpoch' = effectEpoch + 1
  /\ effect' = Authority("retained", installedRev, currentIntent, effectEpoch + 1)
  /\ hostAuthority' = effect'
  /\ explicit' = NoExplicitWork
  /\ UNCHANGED << installedRev, currentIntent, superseded, nextMaintenance,
                  maintenanceQueue, lastAdmitted, admissionWitness,
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
  /\ explicit' = NoExplicitWork
  /\ UNCHANGED << installedRev, currentIntent, superseded, nextMaintenance,
                  maintenanceQueue, lastAdmitted, admissionWitness,
                  orderWitness, visibleWitness >>

\* A superseded explicit operation returns late.  It produces no visible
\* effect and cannot install.
SupersededResultDiscarded(token) ==
  /\ token \in superseded
  /\ superseded' = superseded \ {token}
  /\ UNCHANGED << installedRev, currentIntent, explicit, nextMaintenance,
                  maintenanceQueue, lastAdmitted, effectEpoch, effect,
                  hostAuthority, admissionWitness, orderWitness,
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
              [seq |-> nextMaintenance, ready |-> FALSE, basis |-> installedRev])
  /\ nextMaintenance' = nextMaintenance + 1
  /\ UNCHANGED << installedRev, currentIntent, explicit, superseded,
                  lastAdmitted, effectEpoch, effect, hostAuthority,
                  admissionWitness, orderWitness, visibleWitness >>

\* Facts for one queued request finish gathering.  Any request may finish
\* first; completion timing must not select the final snapshot.
GatherMaintenanceFacts(n) ==
  /\ HasMaintenance(n)
  /\ LET i == MaintenanceIndex(n) IN
       /\ ~maintenanceQueue[i].ready
       /\ maintenanceQueue' = [maintenanceQueue EXCEPT ![i].ready = TRUE]
  /\ UNCHANGED << installedRev, currentIntent, explicit, superseded,
                  nextMaintenance, lastAdmitted, effectEpoch, effect,
                  hostAuthority, admissionWitness, orderWitness,
                  visibleWitness >>

\* A queued request whose basis is no longer the installed snapshot rebuilds
\* from the then-current snapshot instead of installing an older result.
RebuildMaintenance(n) ==
  /\ HasMaintenance(n)
  /\ LET i == MaintenanceIndex(n) IN
       /\ maintenanceQueue[i].basis # installedRev
       /\ maintenanceQueue' = [maintenanceQueue EXCEPT ![i].basis = installedRev,
                                                       ![i].ready = FALSE]
  /\ UNCHANGED << installedRev, currentIntent, explicit, superseded,
                  nextMaintenance, lastAdmitted, effectEpoch, effect,
                  hostAuthority, admissionWitness, orderWitness,
                  visibleWitness >>

\* The design's admission predicate, stated once: only the oldest outstanding
\* request, only when it was rebuilt against the installed snapshot, only
\* with no unresolved explicit work, and only with no unconsumed effect.
MaintenanceAdmissible ==
  /\ maintenanceQueue # << >>
  /\ Head(maintenanceQueue).ready
  /\ Head(maintenanceQueue).basis = installedRev
  /\ explicit = NoExplicitWork
  /\ effect = NoAuthority

AdmitMaintenance ==
  /\ MaintenanceAdmissible
  /\ installedRev' = installedRev + 1
  /\ effectEpoch' = effectEpoch + 1
  /\ effect' = Authority("maintenance", installedRev + 1, currentIntent,
                         effectEpoch + 1)
  /\ hostAuthority' = effect'
  /\ lastAdmitted' = Head(maintenanceQueue).seq
  /\ maintenanceQueue' = Tail(maintenanceQueue)
  /\ admissionWitness' =
       /\ admissionWitness
       /\ explicit = NoExplicitWork
       /\ effect = NoAuthority
  /\ orderWitness' =
       /\ orderWitness
       /\ Head(maintenanceQueue).seq > lastAdmitted
       /\ \A e \in Range(maintenanceQueue) : Head(maintenanceQueue).seq <= e.seq
  /\ UNCHANGED << currentIntent, explicit, superseded, nextMaintenance,
                  visibleWitness >>

(***************************************************************************)
(* Consumer side.                                                          *)
(*                                                                         *)
(* A consumer validates the returned authority through the session before   *)
(* rendering and again inside every deferred focus or outcome effect.       *)
(* Installing a result is not continuing authority.                         *)
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
  /\ UNCHANGED << installedRev, currentIntent, explicit, superseded,
                  nextMaintenance, maintenanceQueue, lastAdmitted,
                  effectEpoch, effect, hostAuthority, admissionWitness,
                  orderWitness >>

\* Installation and every required focus and outcome effect completed.
\* Acknowledgement releases queued maintenance.
AcknowledgeEffect ==
  /\ hostAuthority # NoAuthority
  /\ hostAuthority = effect
  /\ effect' = NoAuthority
  /\ hostAuthority' = NoAuthority
  /\ UNCHANGED << installedRev, currentIntent, explicit, superseded,
                  nextMaintenance, maintenanceQueue, lastAdmitted,
                  effectEpoch, admissionWitness, orderWitness,
                  visibleWitness >>

\* The owning surface was destroyed, or revalidation failed and the consumer
\* drops authority it can no longer use.  Abandonment also releases queued
\* maintenance.
AbandonEffect ==
  /\ hostAuthority # NoAuthority
  /\ hostAuthority' = NoAuthority
  /\ effect' = IF hostAuthority = effect THEN NoAuthority ELSE effect
  /\ UNCHANGED << installedRev, currentIntent, explicit, superseded,
                  nextMaintenance, maintenanceQueue, lastAdmitted,
                  effectEpoch, admissionWitness, orderWitness,
                  visibleWitness >>

\* A consumer is handed authority minted by a different navigation session.
ForeignAuthorityOffered ==
  /\ hostAuthority = NoAuthority
  /\ hostAuthority' = ForeignAuthority
  /\ UNCHANGED << installedRev, currentIntent, explicit, superseded,
                  nextMaintenance, maintenanceQueue, lastAdmitted,
                  effectEpoch, effect, admissionWitness, orderWitness,
                  visibleWitness >>

ResolveExplicit ==
  \/ ExplicitResultInstalls
  \/ ExplicitResultRetains
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

\* No stale visible effect: every render, focus, or outcome effect executed
\* under exactly the session's current unconsumed authority.
NoStaleVisibleEffect == visibleWitness

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
  \A n \in 1 .. MaxMaintenance : HasMaintenance(n) ~> (lastAdmitted >= n)

\* Blocked by unresolved explicit work or by an unconsumed effect: the
\* explicit operation must resolve and the effect must be acknowledged or
\* abandoned before this request can be admitted, and it still is.
BlockedMaintenanceResumes ==
  \A n \in 1 .. MaxMaintenance :
    (HasMaintenance(n) /\ (explicit # NoExplicitWork \/ effect # NoAuthority))
      ~> (lastAdmitted >= n)

\* Blocked behind an external prerequisite abort specifically: the abort
\* effect must be acknowledged or abandoned before maintenance resumes.
MaintenanceResumesAfterAbort ==
  \A n \in 1 .. MaxMaintenance :
    (HasMaintenance(n) /\ effect.outcome = "aborted") ~> (lastAdmitted >= n)

\* Blocked by a basis a newer snapshot invalidated: the request must rebuild
\* and re-gather before it can be admitted, and it still is.
StaleBasisMaintenanceResumes ==
  \A n \in 1 .. MaxMaintenance :
    (HasMaintenance(n) /\ MaintenanceEntry(n).basis # installedRev)
      ~> (lastAdmitted >= n)

=============================================================================
