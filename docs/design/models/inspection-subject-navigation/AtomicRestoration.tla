-------------------------- MODULE AtomicRestoration --------------------------
(***************************************************************************)
(* Design model of canonical navigation preparation.                       *)
(*                                                                         *)
(* Inspection Subject Navigation receives one exact subject+lens request   *)
(* and resolves its two halves into one prepared snapshot.  It publishes   *)
(* that pair only when both halves are ready, aborts it when either half    *)
(* fails, and discards it when a newer intent supersedes it.  Complete      *)
(* restoration coordination and installation belong to issue #4787 and    *)
(* are not modelled here.                                                   *)
(*                                                                         *)
(* Product concept                         Model variable                  *)
(*   current restoration intent token        intent                        *)
(*   exact requested subject+lens payload     requests                      *)
(*   in-flight navigation preparation         prep                          *)
(*   atomic prepared result or settlement     results                       *)
(*   preparations a newer intent replaced     supersededPreparations        *)
(*   preparations that failed                 failedPreparations            *)
(*                                                                         *)
(* Requests and results are independent records.  `readinessWitness` and   *)
(* `payloadWitness` re-derive the publish conditions instead of trusting   *)
(* the action's guard or assignments.                                       *)
(***************************************************************************)
EXTENDS Naturals

CONSTANTS
  MaxIntent,
  Subjects,
  Lenses,
  NoSubject,
  NoLens

ASSUME MaxIntent \in Nat
ASSUME NoSubject \notin Subjects /\ NoLens \notin Lenses

VARIABLES
  intent,
  requests,
  prep,
  results,
  supersededPreparations,
  failedPreparations,
  readinessWitness,
  payloadWitness

vars == << intent, requests, prep, results, supersededPreparations,
           failedPreparations, readinessWitness, payloadWitness >>

Tokens == 1 .. MaxIntent
PreparationStates == {"none", "working", "ready", "failed"}
ResultStates == {"none", "prepared", "aborted", "discarded"}

NoRequest == [subject |-> NoSubject, lens |-> NoLens]

NoPreparation ==
  [ subjectState |-> "none",
    lensState    |-> "none",
    live         |-> FALSE ]

NewPreparation ==
  [ subjectState |-> "working",
    lensState    |-> "working",
    live         |-> TRUE ]

NoResult ==
  [ state   |-> "none",
    subject |-> NoSubject,
    lens    |-> NoLens ]

NavigationReady(a) ==
  a.subjectState = "ready" /\ a.lensState = "ready"

NavigationFailed(a) ==
  a.subjectState = "failed" \/ a.lensState = "failed"

TypeOK ==
  /\ intent \in 0 .. MaxIntent
  /\ supersededPreparations \subseteq Tokens
  /\ failedPreparations \subseteq Tokens
  /\ readinessWitness \in BOOLEAN
  /\ payloadWitness \in BOOLEAN
  /\ \A t \in Tokens :
       /\ requests[t].subject \in Subjects \cup {NoSubject}
       /\ requests[t].lens \in Lenses \cup {NoLens}
       /\ prep[t].live \in BOOLEAN
       /\ prep[t].subjectState \in PreparationStates
       /\ prep[t].lensState \in PreparationStates
       /\ results[t].state \in ResultStates
       /\ results[t].subject \in Subjects \cup {NoSubject}
       /\ results[t].lens \in Lenses \cup {NoLens}

Init ==
  /\ intent = 0
  /\ requests = [t \in Tokens |-> NoRequest]
  /\ prep = [t \in Tokens |-> NoPreparation]
  /\ results = [t \in Tokens |-> NoResult]
  /\ supersededPreparations = {}
  /\ failedPreparations = {}
  /\ readinessWitness = TRUE
  /\ payloadWitness = TRUE

(***************************************************************************)
(* One immutable requested payload is captured before either half begins.  *)
(***************************************************************************)
BeginRestoration(subject, lens) ==
  /\ intent < MaxIntent
  /\ intent' = intent + 1
  /\ requests' =
       [requests EXCEPT ![intent + 1] = [subject |-> subject, lens |-> lens]]
  /\ prep' = [prep EXCEPT ![intent + 1] = NewPreparation]
  /\ supersededPreparations' =
       supersededPreparations \cup {t \in Tokens : prep[t].live}
  /\ UNCHANGED << results, failedPreparations, readinessWitness,
                  payloadWitness >>

PrepareSubject(t) ==
  /\ prep[t].live
  /\ prep[t].subjectState = "working"
  /\ prep' = [prep EXCEPT ![t].subjectState = "ready"]
  /\ UNCHANGED << intent, requests, results, supersededPreparations,
                  failedPreparations, readinessWitness, payloadWitness >>

FailSubjectPreparation(t) ==
  /\ prep[t].live
  /\ prep[t].subjectState = "working"
  /\ prep' = [prep EXCEPT ![t].subjectState = "failed"]
  /\ failedPreparations' = failedPreparations \cup {t}
  /\ UNCHANGED << intent, requests, results, supersededPreparations,
                  readinessWitness, payloadWitness >>

PrepareLens(t) ==
  /\ prep[t].live
  /\ prep[t].lensState = "working"
  /\ prep' = [prep EXCEPT ![t].lensState = "ready"]
  /\ UNCHANGED << intent, requests, results, supersededPreparations,
                  failedPreparations, readinessWitness, payloadWitness >>

FailLensPreparation(t) ==
  /\ prep[t].live
  /\ prep[t].lensState = "working"
  /\ prep' = [prep EXCEPT ![t].lensState = "failed"]
  /\ failedPreparations' = failedPreparations \cup {t}
  /\ UNCHANGED << intent, requests, results, supersededPreparations,
                  readinessWitness, payloadWitness >>

(***************************************************************************)
(* Navigation publishes one prepared snapshot to the adjacent coordinator. *)
(* The result carries the exact independently retained request payload.     *)
(***************************************************************************)
PublishPreparation(t) ==
  /\ prep[t].live
  /\ t = intent
  /\ NavigationReady(prep[t])
  /\ results' =
       [results EXCEPT ![t] =
          [ state   |-> "prepared",
            subject |-> requests[t].subject,
            lens    |-> requests[t].lens ]]
  /\ prep' = [prep EXCEPT ![t] = NoPreparation]
  /\ readinessWitness' =
       /\ readinessWitness
       /\ t = intent
       /\ prep[t].subjectState = "ready"
       /\ prep[t].lensState = "ready"
  /\ payloadWitness' =
       /\ payloadWitness
       /\ results'[t].subject = requests[t].subject
       /\ results'[t].lens = requests[t].lens
  /\ UNCHANGED << intent, requests, supersededPreparations,
                  failedPreparations >>

AbortPreparation(t) ==
  /\ prep[t].live
  /\ NavigationFailed(prep[t])
  /\ results' =
       [results EXCEPT ![t] =
          [state |-> "aborted", subject |-> NoSubject, lens |-> NoLens]]
  /\ prep' = [prep EXCEPT ![t] = NoPreparation]
  /\ UNCHANGED << intent, requests, supersededPreparations,
                  failedPreparations, readinessWitness, payloadWitness >>

DiscardSupersededPreparation(t) ==
  /\ prep[t].live
  /\ t # intent
  /\ ~NavigationFailed(prep[t])
  /\ results' =
       [results EXCEPT ![t] =
          [state |-> "discarded", subject |-> NoSubject, lens |-> NoLens]]
  /\ prep' = [prep EXCEPT ![t] = NoPreparation]
  /\ UNCHANGED << intent, requests, supersededPreparations,
                  failedPreparations, readinessWitness, payloadWitness >>

ResolveSubjectHalf(t) == PrepareSubject(t) \/ FailSubjectPreparation(t)
ResolveLensHalf(t) == PrepareLens(t) \/ FailLensPreparation(t)

SettleAttempt(t) ==
  \/ PublishPreparation(t)
  \/ AbortPreparation(t)
  \/ DiscardSupersededPreparation(t)

Next ==
  \/ \E subject \in Subjects, lens \in Lenses :
       BeginRestoration(subject, lens)
  \/ \E t \in Tokens : ResolveSubjectHalf(t)
  \/ \E t \in Tokens : ResolveLensHalf(t)
  \/ \E t \in Tokens : SettleAttempt(t)

Fairness ==
  /\ \A t \in Tokens : WF_vars(ResolveSubjectHalf(t))
  /\ \A t \in Tokens : WF_vars(ResolveLensHalf(t))
  /\ \A t \in Tokens : WF_vars(SettleAttempt(t))

Spec == Init /\ [][Next]_vars /\ Fairness

(***************************************************************************)
(* Invariants.                                                             *)
(***************************************************************************)

PreparationRequiresReadyPairAndCurrentIntent == readinessWitness

PreparedPairEqualsRequestedPayload ==
  /\ payloadWitness
  /\ \A t \in Tokens :
       results[t].state = "prepared" =>
         /\ results[t].subject = requests[t].subject
         /\ results[t].lens = requests[t].lens

NoSupersededPreparationResult ==
  \A t \in Tokens :
    results[t].state = "prepared" => t \notin supersededPreparations

FailedPreparationNeverPrepared ==
  \A t \in Tokens :
    t \in failedPreparations => results[t].state \in {"none", "aborted"}

PreparationIsInvisibleUntilPublished ==
  \A t \in Tokens : prep[t].live => results[t].state = "none"

(***************************************************************************)
(* Liveness.                                                               *)
(***************************************************************************)

EveryAttemptSettles ==
  \A t \in Tokens :
    prep[t].live ~> (~prep[t].live /\ results[t].state # "none")

FailedAttemptsAbort ==
  \A t \in Tokens :
    (prep[t].live /\ NavigationFailed(prep[t]))
      ~> (~prep[t].live /\ results[t].state = "aborted")

=============================================================================
