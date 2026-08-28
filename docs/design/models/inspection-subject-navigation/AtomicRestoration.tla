-------------------------- MODULE AtomicRestoration --------------------------
(***************************************************************************)
(* Design model of canonical navigation restoration.                       *)
(*                                                                         *)
(* Inspection Subject Navigation prepares one subject and one lens together *)
(* under one explicit restoration intent, and the canonical-state          *)
(* coordinator commits that preparation only when every restoration        *)
(* participant is ready.  The model checks that transaction.  It says       *)
(* nothing about packet encoding, identity resolution, lens contents, or    *)
(* how a host renders the result.                                           *)
(*                                                                         *)
(* Product concept                      Model variable                     *)
(*   current restoration intent token     intent                           *)
(*   in-flight prepared snapshots         prep (keyed by intent token)      *)
(*   the visible installed subject        visibleSubject                   *)
(*   the visible installed lens           visibleLens                      *)
(*   the last committed subject+lens      lastCommit                       *)
(*   preparations a newer intent replaced supersededPreparations           *)
(*   preparations that failed             failedPreparations               *)
(*   how each attempt ended               settlement                       *)
(*                                                                         *)
(* Navigation's own preparation has two explicitly tracked halves.  The     *)
(* subject half and the lens half are each working, ready, or failed, so    *)
(* navigation can fail before either half resolves, after the subject half  *)
(* alone, or after the lens half alone.  Every one of those failures has to *)
(* settle through abort without either half ever becoming visible.          *)
(*                                                                         *)
(* The visible subject and the visible lens are deliberately modelled as    *)
(* two independent variables, each carrying the intent token that installed *)
(* it.  That is the shape a host reaches for when it stores subject levels  *)
(* separately, so `NoPartialInstallation` is a real check rather than a     *)
(* restatement of a single assignment.                                     *)
(*                                                                         *)
(* `commitWitness` is a latching boolean that re-derives, independently of  *)
(* the commit action's own guard, the exact condition the design requires   *)
(* for a commit.  Weakening that guard later breaks the paired invariant.   *)
(***************************************************************************)
EXTENDS Naturals

CONSTANTS
  MaxIntent,      \* how many restoration intents one behaviour may issue
  Subjects,       \* candidate exact subjects supplied by canonical state
  Lenses,         \* candidate exact navigation lenses supplied with them
  Peers,          \* the other restoration participants in the transaction
  PriorSubject,   \* the subject visible before any restoration commits
  PriorLens       \* the lens visible before any restoration commits

ASSUME MaxIntent \in Nat
ASSUME Peers # {}
ASSUME PriorSubject \notin Subjects /\ PriorLens \notin Lenses

VARIABLES
  intent,
  prep,
  visibleSubject,
  visibleLens,
  lastCommit,
  supersededPreparations,
  failedPreparations,
  settlement,
  commitWitness

vars == << intent, prep, visibleSubject, visibleLens, lastCommit,
           supersededPreparations, failedPreparations, settlement,
           commitWitness >>

Tokens == 1 .. MaxIntent
PreparationStates == {"none", "working", "ready", "failed"}

\* How each restoration attempt ended.  Every attempt that starts reaches one
\* of these reasons, and the reason names which path it took.
SettlementReasons == {"none", "committed", "aborted", "discarded"}

NoPreparation == [subject |-> PriorSubject, lens |-> PriorLens,
                  subjectState |-> "none", lensState |-> "none",
                  peers |-> [p \in Peers |-> "none"], live |-> FALSE]

NewPreparation(subject, lens) ==
  [ subject      |-> subject,
    lens         |-> lens,
    subjectState |-> "working",
    lensState    |-> "working",
    peers        |-> [p \in Peers |-> "working"],
    live         |-> TRUE ]

\* Navigation's own participation is ready only when the subject half and the
\* lens half of the same preparation are both ready: one snapshot, not two
\* independently installable results.
NavigationReady(a) == a.subjectState = "ready" /\ a.lensState = "ready"

NavigationFailed(a) == a.subjectState = "failed" \/ a.lensState = "failed"

AllParticipantsReady(a) ==
  /\ NavigationReady(a)
  /\ \A p \in Peers : a.peers[p] = "ready"

SomeParticipantFailed(a) == \E p \in Peers : a.peers[p] = "failed"

AttemptFailed(a) == NavigationFailed(a) \/ SomeParticipantFailed(a)

TypeOK ==
  /\ intent \in 0 .. MaxIntent
  /\ visibleSubject.value \in Subjects \cup {PriorSubject}
  /\ visibleLens.value \in Lenses \cup {PriorLens}
  /\ visibleSubject.origin \in 0 .. MaxIntent
  /\ visibleLens.origin \in 0 .. MaxIntent
  /\ supersededPreparations \subseteq Tokens
  /\ failedPreparations \subseteq Tokens
  /\ \A t \in Tokens : settlement[t] \in SettlementReasons
  /\ commitWitness \in BOOLEAN
  /\ \A t \in Tokens :
       /\ prep[t].live \in BOOLEAN
       /\ prep[t].subject \in Subjects \cup {PriorSubject}
       /\ prep[t].lens \in Lenses \cup {PriorLens}
       /\ prep[t].subjectState \in PreparationStates
       /\ prep[t].lensState \in PreparationStates
       /\ \A p \in Peers : prep[t].peers[p] \in PreparationStates

Init ==
  /\ intent = 0
  /\ prep = [t \in Tokens |-> NoPreparation]
  /\ visibleSubject = [value |-> PriorSubject, origin |-> 0]
  /\ visibleLens = [value |-> PriorLens, origin |-> 0]
  /\ lastCommit = [subject |-> PriorSubject, lens |-> PriorLens, token |-> 0]
  /\ supersededPreparations = {}
  /\ failedPreparations = {}
  /\ settlement = [t \in Tokens |-> "none"]
  /\ commitWitness = TRUE

(***************************************************************************)
(* Beginning a restoration issues a new explicit intent token.  Any older   *)
(* live preparation is superseded at once; it may still finish gathering    *)
(* facts, but it can never commit.                                          *)
(***************************************************************************)
BeginRestoration(subject, lens) ==
  /\ intent < MaxIntent
  /\ intent' = intent + 1
  /\ prep' = [prep EXCEPT ![intent + 1] = NewPreparation(subject, lens)]
  /\ supersededPreparations' =
       supersededPreparations \cup {t \in Tokens : prep[t].live}
  /\ UNCHANGED << visibleSubject, visibleLens, lastCommit, failedPreparations,
                  settlement, commitWitness >>

\* Navigation resolves the subject half of the prepared snapshot.  Nothing is
\* installed: preparation is invisible until the transaction commits.
PrepareSubject(t) ==
  /\ prep[t].live
  /\ prep[t].subjectState = "working"
  /\ prep' = [prep EXCEPT ![t].subjectState = "ready"]
  /\ UNCHANGED << intent, visibleSubject, visibleLens, lastCommit,
                  supersededPreparations, failedPreparations, settlement,
                  commitWitness >>

\* The subject half fails.  Reachable while the lens half is still working and
\* also after the lens half is already ready.
FailSubjectPreparation(t) ==
  /\ prep[t].live
  /\ prep[t].subjectState = "working"
  /\ prep' = [prep EXCEPT ![t].subjectState = "failed"]
  /\ failedPreparations' = failedPreparations \cup {t}
  /\ UNCHANGED << intent, visibleSubject, visibleLens, lastCommit,
                  supersededPreparations, settlement, commitWitness >>

\* Navigation resolves the lens half of the same prepared snapshot.
PrepareLens(t) ==
  /\ prep[t].live
  /\ prep[t].lensState = "working"
  /\ prep' = [prep EXCEPT ![t].lensState = "ready"]
  /\ UNCHANGED << intent, visibleSubject, visibleLens, lastCommit,
                  supersededPreparations, failedPreparations, settlement,
                  commitWitness >>

\* The lens half fails.  Reachable while the subject half is still working and
\* also after the subject half is already ready.
FailLensPreparation(t) ==
  /\ prep[t].live
  /\ prep[t].lensState = "working"
  /\ prep' = [prep EXCEPT ![t].lensState = "failed"]
  /\ failedPreparations' = failedPreparations \cup {t}
  /\ UNCHANGED << intent, visibleSubject, visibleLens, lastCommit,
                  supersededPreparations, settlement, commitWitness >>

\* Another restoration participant reports readiness or failure.
PeerReady(t, p) ==
  /\ prep[t].live
  /\ prep[t].peers[p] = "working"
  /\ prep' = [prep EXCEPT ![t].peers[p] = "ready"]
  /\ UNCHANGED << intent, visibleSubject, visibleLens, lastCommit,
                  supersededPreparations, failedPreparations, settlement,
                  commitWitness >>

PeerFailed(t, p) ==
  /\ prep[t].live
  /\ prep[t].peers[p] = "working"
  /\ prep' = [prep EXCEPT ![t].peers[p] = "failed"]
  /\ failedPreparations' = failedPreparations \cup {t}
  /\ UNCHANGED << intent, visibleSubject, visibleLens, lastCommit,
                  supersededPreparations, settlement, commitWitness >>

(***************************************************************************)
(* The coordinator commits one prepared snapshot.  Subject and lens become  *)
(* visible in the same step, tagged with the intent that produced them.     *)
(***************************************************************************)
CommitRestoration(t) ==
  /\ prep[t].live
  /\ t = intent
  /\ AllParticipantsReady(prep[t])
  /\ visibleSubject' = [value |-> prep[t].subject, origin |-> t]
  /\ visibleLens' = [value |-> prep[t].lens, origin |-> t]
  /\ lastCommit' = [subject |-> prep[t].subject, lens |-> prep[t].lens,
                    token |-> t]
  /\ prep' = [prep EXCEPT ![t] = NoPreparation]
  /\ settlement' = [settlement EXCEPT ![t] = "committed"]
  /\ commitWitness' =
       /\ commitWitness
       /\ t = intent
       /\ prep[t].subjectState = "ready"
       /\ prep[t].lensState = "ready"
       /\ \A p \in Peers : prep[t].peers[p] = "ready"
  /\ UNCHANGED << intent, supersededPreparations, failedPreparations >>

\* Navigation's own preparation failed, or a peer failed.  The transaction
\* aborts with no partial navigation state; the previously visible pair stays
\* exactly as it was.
AbortRestoration(t) ==
  /\ prep[t].live
  /\ AttemptFailed(prep[t])
  /\ prep' = [prep EXCEPT ![t] = NoPreparation]
  /\ settlement' = [settlement EXCEPT ![t] = "aborted"]
  /\ UNCHANGED << intent, visibleSubject, visibleLens, lastCommit,
                  supersededPreparations, failedPreparations, commitWitness >>

\* A superseded preparation that did not fail finishes late and is discarded
\* unused.  A failed preparation is excluded here: supersession does not
\* relabel a failure, so it still settles through abort.
DiscardSupersededPreparation(t) ==
  /\ prep[t].live
  /\ t # intent
  /\ ~AttemptFailed(prep[t])
  /\ prep' = [prep EXCEPT ![t] = NoPreparation]
  /\ settlement' = [settlement EXCEPT ![t] = "discarded"]
  /\ UNCHANGED << intent, visibleSubject, visibleLens, lastCommit,
                  supersededPreparations, failedPreparations, commitWitness >>

ResolveSubjectHalf(t) == PrepareSubject(t) \/ FailSubjectPreparation(t)
ResolveLensHalf(t) == PrepareLens(t) \/ FailLensPreparation(t)
ResolvePeer(t, p) == PeerReady(t, p) \/ PeerFailed(t, p)

SettleAttempt(t) ==
  \/ CommitRestoration(t)
  \/ AbortRestoration(t)
  \/ DiscardSupersededPreparation(t)

Next ==
  \/ \E subject \in Subjects, lens \in Lenses : BeginRestoration(subject, lens)
  \/ \E t \in Tokens : ResolveSubjectHalf(t)
  \/ \E t \in Tokens : ResolveLensHalf(t)
  \/ \E t \in Tokens, p \in Peers : ResolvePeer(t, p)
  \/ \E t \in Tokens : SettleAttempt(t)

Fairness ==
  /\ \A t \in Tokens : WF_vars(ResolveSubjectHalf(t))
  /\ \A t \in Tokens : WF_vars(ResolveLensHalf(t))
  /\ \A t \in Tokens : \A p \in Peers : WF_vars(ResolvePeer(t, p))
  /\ \A t \in Tokens : WF_vars(SettleAttempt(t))

Spec == Init /\ [][Next]_vars /\ Fairness

(***************************************************************************)
(* Invariants.                                                             *)
(***************************************************************************)

\* No partial or intermediate installation: the visible subject and the
\* visible lens always come from the same restoration.
NoPartialInstallation == visibleSubject.origin = visibleLens.origin

\* Failure, abort, and supersession retain the prior visible pair.
\* `lastCommit` is written only by CommitRestoration, so any other action
\* that touched the visible pair would break this.
VisiblePairIsLastCommit ==
  /\ visibleSubject.value = lastCommit.subject
  /\ visibleLens.value = lastCommit.lens
  /\ visibleSubject.origin = lastCommit.token
  /\ visibleLens.origin = lastCommit.token

\* A commit happened only with both navigation halves ready, every peer
\* ready, and the preparation's own token still the current intent.
CommitRequiresReadyParticipantsAndCurrentIntent == commitWitness

\* A newer intent prevents an older preparation from committing: nothing that
\* was superseded is visible.
NoSupersededCommit ==
  /\ visibleSubject.origin \notin supersededPreparations
  /\ visibleLens.origin \notin supersededPreparations

\* A preparation that failed in either navigation half or in a peer never
\* becomes visible, whichever half failed and whatever was already prepared.
FailedPreparationNeverVisible ==
  /\ visibleSubject.origin \notin failedPreparations
  /\ visibleLens.origin \notin failedPreparations

\* Preparation is invisible.  A live preparation never leaks its subject or
\* lens into the visible pair before it commits.
PreparationIsInvisible ==
  \A t \in Tokens :
    prep[t].live => (visibleSubject.origin # t /\ visibleLens.origin # t)

\* An attempt that is still live has not settled, so a settlement reason is
\* recorded once, by the step that ended the attempt.
LiveAttemptHasNoSettlement ==
  \A t \in Tokens : prep[t].live => settlement[t] = "none"

\* A failed preparation settles as aborted or has not settled yet.  It is
\* never relabelled as committed or as an ordinary superseded discard, even
\* when a newer intent superseded it as well.
FailedAttemptSettlesAsAborted ==
  \A t \in Tokens :
    t \in failedPreparations => settlement[t] \in {"none", "aborted"}

\* A committed attempt neither failed nor was superseded.  This correlates the
\* recorded reason with the failure and supersession histories rather than
\* trusting the label on its own.
CommittedAttemptWasNeitherFailedNorSuperseded ==
  \A t \in Tokens :
    settlement[t] = "committed" =>
      /\ t \notin failedPreparations
      /\ t \notin supersededPreparations

\* The visible pair came from the one attempt recorded as committed, or from
\* the prior pair when nothing has committed yet.
VisiblePairComesFromACommittedAttempt ==
  visibleSubject.origin # 0 => settlement[visibleSubject.origin] = "committed"

(***************************************************************************)
(* Liveness.                                                               *)
(***************************************************************************)

\* Every restoration attempt settles with its own recorded reason rather than
\* leaving the transaction open or ending anonymously.  The consequent names
\* the same token as the antecedent, so another attempt settling does not
\* discharge this one.
EveryAttemptSettles ==
  \A t \in Tokens :
    prep[t].live ~> (~prep[t].live /\ settlement[t] # "none")

\* A failed attempt settles specifically through abort: whichever half or
\* participant failed, the transaction closes with the aborted reason, not
\* merely as no longer live and not as a discard.
FailedAttemptsAbort ==
  \A t \in Tokens :
    (prep[t].live /\ AttemptFailed(prep[t]))
      ~> (~prep[t].live /\ settlement[t] = "aborted")

\* A navigation half-failure reaches the same aborted settlement, including
\* when the other half is already prepared.
HalfFailedAttemptsSettle ==
  \A t \in Tokens :
    (prep[t].live /\ NavigationFailed(prep[t]))
      ~> (~prep[t].live /\ settlement[t] = "aborted")

=============================================================================
