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
  commitWitness

vars == << intent, prep, visibleSubject, visibleLens, lastCommit,
           supersededPreparations, commitWitness >>

Tokens == 1 .. MaxIntent

NoPreparation == [subject |-> PriorSubject, lens |-> PriorLens,
                  subjectPrepared |-> FALSE, lensPrepared |-> FALSE,
                  peers |-> [p \in Peers |-> "none"], live |-> FALSE]

NewPreparation(subject, lens) ==
  [ subject         |-> subject,
    lens            |-> lens,
    subjectPrepared |-> FALSE,
    lensPrepared    |-> FALSE,
    peers           |-> [p \in Peers |-> "working"],
    live            |-> TRUE ]

\* Navigation's own participation is ready only when the subject and the lens
\* of the same preparation are both prepared: one snapshot, not two results.
NavigationReady(a) == a.subjectPrepared /\ a.lensPrepared

AllParticipantsReady(a) ==
  /\ NavigationReady(a)
  /\ \A p \in Peers : a.peers[p] = "ready"

SomeParticipantFailed(a) == \E p \in Peers : a.peers[p] = "failed"

TypeOK ==
  /\ intent \in 0 .. MaxIntent
  /\ visibleSubject.value \in Subjects \cup {PriorSubject}
  /\ visibleLens.value \in Lenses \cup {PriorLens}
  /\ visibleSubject.origin \in 0 .. MaxIntent
  /\ visibleLens.origin \in 0 .. MaxIntent
  /\ supersededPreparations \subseteq Tokens
  /\ commitWitness \in BOOLEAN
  /\ \A t \in Tokens :
       /\ prep[t].live \in BOOLEAN
       /\ prep[t].subject \in Subjects \cup {PriorSubject}
       /\ prep[t].lens \in Lenses \cup {PriorLens}
       /\ \A p \in Peers : prep[t].peers[p] \in {"none", "working", "ready", "failed"}

Init ==
  /\ intent = 0
  /\ prep = [t \in Tokens |-> NoPreparation]
  /\ visibleSubject = [value |-> PriorSubject, origin |-> 0]
  /\ visibleLens = [value |-> PriorLens, origin |-> 0]
  /\ lastCommit = [subject |-> PriorSubject, lens |-> PriorLens, token |-> 0]
  /\ supersededPreparations = {}
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
  /\ UNCHANGED << visibleSubject, visibleLens, lastCommit, commitWitness >>

\* Navigation resolves the subject half of the prepared snapshot.  Nothing is
\* installed: preparation is invisible until the transaction commits.
PrepareSubject(t) ==
  /\ prep[t].live
  /\ ~prep[t].subjectPrepared
  /\ prep' = [prep EXCEPT ![t].subjectPrepared = TRUE]
  /\ UNCHANGED << intent, visibleSubject, visibleLens, lastCommit,
                  supersededPreparations, commitWitness >>

\* Navigation resolves the lens half of the same prepared snapshot.
PrepareLens(t) ==
  /\ prep[t].live
  /\ ~prep[t].lensPrepared
  /\ prep' = [prep EXCEPT ![t].lensPrepared = TRUE]
  /\ UNCHANGED << intent, visibleSubject, visibleLens, lastCommit,
                  supersededPreparations, commitWitness >>

\* Another restoration participant reports readiness or failure.
PeerReady(t, p) ==
  /\ prep[t].live
  /\ prep[t].peers[p] = "working"
  /\ prep' = [prep EXCEPT ![t].peers[p] = "ready"]
  /\ UNCHANGED << intent, visibleSubject, visibleLens, lastCommit,
                  supersededPreparations, commitWitness >>

PeerFailed(t, p) ==
  /\ prep[t].live
  /\ prep[t].peers[p] = "working"
  /\ prep' = [prep EXCEPT ![t].peers[p] = "failed"]
  /\ UNCHANGED << intent, visibleSubject, visibleLens, lastCommit,
                  supersededPreparations, commitWitness >>

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
  /\ commitWitness' =
       /\ commitWitness
       /\ t = intent
       /\ NavigationReady(prep[t])
       /\ \A p \in Peers : prep[t].peers[p] = "ready"
  /\ UNCHANGED << intent, supersededPreparations >>

\* A participant failed.  The transaction aborts with no partial navigation
\* state; the previously visible pair stays exactly as it was.
AbortRestoration(t) ==
  /\ prep[t].live
  /\ SomeParticipantFailed(prep[t])
  /\ prep' = [prep EXCEPT ![t] = NoPreparation]
  /\ UNCHANGED << intent, visibleSubject, visibleLens, lastCommit,
                  supersededPreparations, commitWitness >>

\* A superseded preparation finishes late and is discarded unused.
DiscardSupersededPreparation(t) ==
  /\ prep[t].live
  /\ t # intent
  /\ prep' = [prep EXCEPT ![t] = NoPreparation]
  /\ UNCHANGED << intent, visibleSubject, visibleLens, lastCommit,
                  supersededPreparations, commitWitness >>

SettleAttempt(t) ==
  \/ CommitRestoration(t)
  \/ AbortRestoration(t)
  \/ DiscardSupersededPreparation(t)

Next ==
  \/ \E subject \in Subjects, lens \in Lenses : BeginRestoration(subject, lens)
  \/ \E t \in Tokens : PrepareSubject(t)
  \/ \E t \in Tokens : PrepareLens(t)
  \/ \E t \in Tokens, p \in Peers : PeerReady(t, p)
  \/ \E t \in Tokens, p \in Peers : PeerFailed(t, p)
  \/ \E t \in Tokens : SettleAttempt(t)

Fairness ==
  /\ \A t \in Tokens : WF_vars(PrepareSubject(t))
  /\ \A t \in Tokens : WF_vars(PrepareLens(t))
  /\ \A t \in Tokens : \A p \in Peers :
       WF_vars(PeerReady(t, p) \/ PeerFailed(t, p))
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

\* A commit happened only with every participant ready and with the
\* preparation's own token still the current restoration intent.
CommitRequiresReadyParticipantsAndCurrentIntent == commitWitness

\* A newer intent prevents an older preparation from committing: nothing that
\* was superseded is visible.
NoSupersededCommit ==
  /\ visibleSubject.origin \notin supersededPreparations
  /\ visibleLens.origin \notin supersededPreparations

\* Preparation is invisible.  A live preparation never leaks its subject or
\* lens into the visible pair before it commits.
PreparationIsInvisible ==
  \A t \in Tokens : prep[t].live => visibleSubject.origin # t

(***************************************************************************)
(* Liveness: every restoration attempt settles, by commit, abort, or        *)
(* discard, rather than leaving the transaction open forever.               *)
(***************************************************************************)
EveryAttemptSettles ==
  (\E t \in Tokens : prep[t].live) ~> (\A t \in Tokens : ~prep[t].live)

=============================================================================
