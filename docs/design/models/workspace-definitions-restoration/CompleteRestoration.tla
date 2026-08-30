------------------------- MODULE CompleteRestoration -------------------------
(***************************************************************************)
(* Design model of Workspace Definitions complete restoration coordination. *)
(*                                                                         *)
(* One canonical request starts every required participant against the same *)
(* immutable payload. Participant preparation is invisible. The coordinator *)
(* builds and canonicalizes one complete candidate, then publishes every    *)
(* participant fragment in one commit. Failure or non-projectability aborts *)
(* without changing the installed snapshot. A newer request makes every     *)
(* completion from an older attempt stale and discardable.                  *)
(*                                                                         *)
(* The intent token abstracts the retained Navigation session's one token;  *)
(* this coordinator does not introduce a second ordering authority.         *)
(*                                                                         *)
(* Participant internals are deliberately abstract. "workspace",            *)
(* "navigation", and "query" in the checked configuration stand for owners  *)
(* that independently return ready, changed-ready, or failed.               *)
(***************************************************************************)
EXTENDS FiniteSets, Naturals

CONSTANTS
  MaxIntent,
  Participants,
  PayloadA,
  PayloadB,
  NoPayload,
  NoToken,
  AllowCanonicalizationFailure,
  Mutation

Tokens == 1 .. MaxIntent
Payloads == {PayloadA, PayloadB}

NoMutation == "None"
EarlyCommit == "EarlyCommit"
PartialCommit == "PartialCommit"
CommitFailed == "CommitFailed"
CommitSuperseded == "CommitSuperseded"
AbortChangesInstalled == "AbortChangesInstalled"
StaleCompletionInstalls == "StaleCompletionInstalls"
WrongRelation == "WrongRelation"
WrongRequest == "WrongRequest"

Mutations ==
  { NoMutation,
    EarlyCommit,
    PartialCommit,
    CommitFailed,
    CommitSuperseded,
    AbortChangesInstalled,
    StaleCompletionInstalls,
    WrongRelation,
    WrongRequest }

ParticipantPhases == {"none", "working", "ready", "failed"}
CandidateStates ==
  { "none",
    "builtExact",
    "builtReplacement",
    "canonicalExact",
    "canonicalReplacement",
    "unprojectable" }
ResultOutcomes == {"none", "committed", "aborted", "discarded"}
ResultRelations == {"none", "exact", "replacement", "retained", "discarded"}

ASSUME
  /\ MaxIntent \in Nat
  /\ Cardinality(Participants) = 3
  /\ PayloadA # PayloadB
  /\ NoPayload \notin Payloads
  /\ NoToken \notin Tokens
  /\ AllowCanonicalizationFailure \in BOOLEAN
  /\ Mutation \in Mutations

NoParticipantPreparation == [phase |-> "none", changed |-> FALSE]
NewParticipantPreparation == [phase |-> "working", changed |-> FALSE]
NoPreparation == [p \in Participants |-> NoParticipantPreparation]
NewPreparation == [p \in Participants |-> NewParticipantPreparation]

NoRequest ==
  [ payload             |-> NoPayload,
    baseToken           |-> NoToken,
    baseRevision        |-> 0,
    basePayload         |-> NoPayload,
    baseChanged         |-> FALSE,
    baseParticipantToken |-> [p \in Participants |-> NoToken] ]

NoResult ==
  [ outcome             |-> "none",
    relation            |-> "none",
    requestedPayload    |-> NoPayload,
    installedToken      |-> NoToken,
    installedRevision   |-> 0,
    installedPayload    |-> NoPayload,
    installedChanged    |-> FALSE,
    participantToken    |-> [p \in Participants |-> NoToken] ]

InitialInstalled ==
  [ token            |-> NoToken,
    revision         |-> 0,
    sourcePayload    |-> NoPayload,
    changed          |-> FALSE,
    participantToken |-> [p \in Participants |-> NoToken] ]

OtherPayload(payload) == IF payload = PayloadA THEN PayloadB ELSE PayloadA
PartialParticipant == CHOOSE p \in Participants : TRUE

VARIABLES
  intent,
  requests,
  preparation,
  liveAttempts,
  candidate,
  results,
  installed,
  supersededAttempts,
  failedAttempts,
  staleCompletions,
  commitGuardWitness,
  requestCorrelationWitness,
  relationWitness,
  abortRetentionWitness,
  staleCompletionWitness

vars ==
  << intent,
     requests,
     preparation,
     liveAttempts,
     candidate,
     results,
     installed,
     supersededAttempts,
     failedAttempts,
     staleCompletions,
     commitGuardWitness,
     requestCorrelationWitness,
     relationWitness,
     abortRetentionWitness,
     staleCompletionWitness >>

TypeOK ==
  /\ intent \in 0 .. MaxIntent
  /\ liveAttempts \subseteq Tokens
  /\ supersededAttempts \subseteq Tokens
  /\ failedAttempts \subseteq Tokens
  /\ staleCompletions \subseteq Tokens
  /\ commitGuardWitness \in BOOLEAN
  /\ requestCorrelationWitness \in BOOLEAN
  /\ relationWitness \in BOOLEAN
  /\ abortRetentionWitness \in BOOLEAN
  /\ staleCompletionWitness \in BOOLEAN
  /\ installed.token \in Tokens \cup {NoToken}
  /\ installed.revision \in Nat
  /\ installed.sourcePayload \in Payloads \cup {NoPayload}
  /\ installed.changed \in BOOLEAN
  /\ \A p \in Participants :
       installed.participantToken[p] \in Tokens \cup {NoToken}
  /\ \A t \in Tokens :
       /\ requests[t].payload \in Payloads \cup {NoPayload}
       /\ requests[t].baseToken \in Tokens \cup {NoToken}
       /\ requests[t].baseRevision \in Nat
       /\ requests[t].basePayload \in Payloads \cup {NoPayload}
       /\ requests[t].baseChanged \in BOOLEAN
       /\ candidate[t] \in CandidateStates
       /\ results[t].outcome \in ResultOutcomes
       /\ results[t].relation \in ResultRelations
       /\ results[t].requestedPayload \in Payloads \cup {NoPayload}
       /\ results[t].installedToken \in Tokens \cup {NoToken}
       /\ results[t].installedRevision \in Nat
       /\ results[t].installedPayload \in Payloads \cup {NoPayload}
       /\ results[t].installedChanged \in BOOLEAN
       /\ \A p \in Participants :
            /\ preparation[t][p].phase \in ParticipantPhases
            /\ preparation[t][p].changed \in BOOLEAN
            /\ requests[t].baseParticipantToken[p] \in Tokens \cup {NoToken}
            /\ results[t].participantToken[p] \in Tokens \cup {NoToken}

Init ==
  /\ intent = 0
  /\ requests = [t \in Tokens |-> NoRequest]
  /\ preparation = [t \in Tokens |-> NoPreparation]
  /\ liveAttempts = {}
  /\ candidate = [t \in Tokens |-> "none"]
  /\ results = [t \in Tokens |-> NoResult]
  /\ installed = InitialInstalled
  /\ supersededAttempts = {}
  /\ failedAttempts = {}
  /\ staleCompletions = {}
  /\ commitGuardWitness = TRUE
  /\ requestCorrelationWitness = TRUE
  /\ relationWitness = TRUE
  /\ abortRetentionWitness = TRUE
  /\ staleCompletionWitness = TRUE

AllReady(t) ==
  \A p \in Participants : preparation[t][p].phase = "ready"

AnyReady(t) ==
  \E p \in Participants : preparation[t][p].phase = "ready"

AnyWorking(t) ==
  \E p \in Participants : preparation[t][p].phase = "working"

AnyFailed(t) ==
  \E p \in Participants : preparation[t][p].phase = "failed"

AnyChanged(t) ==
  \E p \in Participants : preparation[t][p].changed

CanonicalCandidate(t) ==
  candidate[t] \in {"canonicalExact", "canonicalReplacement"}

CommitGuardSatisfied(t) ==
  /\ t \in liveAttempts
  /\ t = intent
  /\ AllReady(t)
  /\ CanonicalCandidate(t)

BeginRestoration(payload) ==
  /\ intent < MaxIntent
  /\ intent' = intent + 1
  /\ requests' =
       [requests EXCEPT
          ![intent + 1] =
            [ payload              |-> payload,
              baseToken            |-> installed.token,
              baseRevision         |-> installed.revision,
              basePayload          |-> installed.sourcePayload,
              baseChanged          |-> installed.changed,
              baseParticipantToken |-> installed.participantToken ]]
  /\ preparation' = [preparation EXCEPT ![intent + 1] = NewPreparation]
  /\ liveAttempts' = liveAttempts \cup {intent + 1}
  /\ candidate' = [candidate EXCEPT ![intent + 1] = "none"]
  /\ supersededAttempts' = supersededAttempts \cup liveAttempts
  /\ UNCHANGED
       << results,
          installed,
          failedAttempts,
          staleCompletions,
          commitGuardWitness,
          requestCorrelationWitness,
          relationWitness,
          abortRetentionWitness,
          staleCompletionWitness >>

ParticipantReady(t, p, changed) ==
  /\ t \in liveAttempts
  /\ preparation[t][p].phase = "working"
  /\ preparation' =
       [preparation EXCEPT
          ![t][p] = [phase |-> "ready", changed |-> changed]]
  /\ UNCHANGED
       << intent,
          requests,
          liveAttempts,
          candidate,
          results,
          installed,
          supersededAttempts,
          failedAttempts,
          staleCompletions,
          commitGuardWitness,
          requestCorrelationWitness,
          relationWitness,
          abortRetentionWitness,
          staleCompletionWitness >>

ParticipantFailed(t, p) ==
  /\ t \in liveAttempts
  /\ preparation[t][p].phase = "working"
  /\ preparation' =
       [preparation EXCEPT
          ![t][p] = [phase |-> "failed", changed |-> FALSE]]
  /\ failedAttempts' = failedAttempts \cup {t}
  /\ UNCHANGED
       << intent,
          requests,
          liveAttempts,
          candidate,
          results,
          installed,
          supersededAttempts,
          staleCompletions,
          commitGuardWitness,
          requestCorrelationWitness,
          relationWitness,
          abortRetentionWitness,
          staleCompletionWitness >>

BuildCandidate(t) ==
  /\ t \in liveAttempts
  /\ AllReady(t)
  /\ candidate[t] = "none"
  /\ candidate' =
       [candidate EXCEPT
          ![t] = IF AnyChanged(t) THEN "builtReplacement" ELSE "builtExact"]
  /\ UNCHANGED
       << intent,
          requests,
          preparation,
          liveAttempts,
          results,
          installed,
          supersededAttempts,
          failedAttempts,
          staleCompletions,
          commitGuardWitness,
          requestCorrelationWitness,
          relationWitness,
          abortRetentionWitness,
          staleCompletionWitness >>

CanonicalizeCandidateSuccess(t) ==
  /\ t \in liveAttempts
  /\ candidate[t] \in {"builtExact", "builtReplacement"}
  /\ candidate' =
       [candidate EXCEPT
          ![t] =
            IF candidate[t] = "builtReplacement"
            THEN "canonicalReplacement"
            ELSE "canonicalExact"]
  /\ UNCHANGED
       << intent,
          requests,
          preparation,
          liveAttempts,
          results,
          installed,
          supersededAttempts,
          failedAttempts,
          staleCompletions,
          commitGuardWitness,
          requestCorrelationWitness,
          relationWitness,
          abortRetentionWitness,
          staleCompletionWitness >>

CanonicalizeCandidateFailure(t) ==
  /\ AllowCanonicalizationFailure
  /\ t \in liveAttempts
  /\ candidate[t] \in {"builtExact", "builtReplacement"}
  /\ candidate' = [candidate EXCEPT ![t] = "unprojectable"]
  /\ UNCHANGED
       << intent,
          requests,
          preparation,
          liveAttempts,
          results,
          installed,
          supersededAttempts,
          failedAttempts,
          staleCompletions,
          commitGuardWitness,
          requestCorrelationWitness,
          relationWitness,
          abortRetentionWitness,
          staleCompletionWitness >>

CommittedRelation(t) ==
  IF Mutation = WrongRelation
  THEN IF AnyChanged(t) THEN "exact" ELSE "replacement"
  ELSE IF AnyChanged(t) THEN "replacement" ELSE "exact"

CommittedPayload(t) ==
  IF Mutation = WrongRequest
  THEN OtherPayload(requests[t].payload)
  ELSE requests[t].payload

CommittedParticipantTokens(t) ==
  IF Mutation = PartialCommit
  THEN [installed.participantToken EXCEPT ![PartialParticipant] = t]
  ELSE [p \in Participants |-> t]

Commit(t) ==
  /\ CommitGuardSatisfied(t)
  /\ LET nextInstalled ==
           [ token            |-> t,
             revision         |-> installed.revision + 1,
             sourcePayload    |-> CommittedPayload(t),
             changed          |-> AnyChanged(t),
             participantToken |-> CommittedParticipantTokens(t) ]
         nextRelation == CommittedRelation(t)
     IN
       /\ installed' = nextInstalled
       /\ results' =
            [results EXCEPT
               ![t] =
                 [ outcome              |-> "committed",
                   relation             |-> nextRelation,
                   requestedPayload     |-> CommittedPayload(t),
                   installedToken       |-> nextInstalled.token,
                   installedRevision    |-> nextInstalled.revision,
                   installedPayload     |-> nextInstalled.sourcePayload,
                   installedChanged     |-> nextInstalled.changed,
                   participantToken     |-> nextInstalled.participantToken ]]
       /\ commitGuardWitness' =
            (commitGuardWitness /\ CommitGuardSatisfied(t))
       /\ requestCorrelationWitness' =
            (requestCorrelationWitness
              /\ CommittedPayload(t) = requests[t].payload)
       /\ relationWitness' =
            (relationWitness
              /\ nextRelation =
                   IF AnyChanged(t) THEN "replacement" ELSE "exact")
  /\ liveAttempts' = liveAttempts \ {t}
  /\ UNCHANGED
       << intent,
          requests,
          preparation,
          candidate,
          supersededAttempts,
          failedAttempts,
          staleCompletions,
          abortRetentionWitness,
          staleCompletionWitness >>

CommitWithoutRequiredGuard(t) ==
  /\ \/ /\ Mutation = EarlyCommit
        /\ t \in liveAttempts
        /\ t = intent
        /\ candidate[t] = "none"
        /\ AnyReady(t)
        /\ AnyWorking(t)
     \/ /\ Mutation = CommitFailed
        /\ t \in liveAttempts
        /\ t = intent
        /\ AnyFailed(t)
     \/ /\ Mutation = CommitSuperseded
        /\ t \in liveAttempts
        /\ t # intent
  /\ installed' =
       [ token            |-> t,
         revision         |-> installed.revision + 1,
         sourcePayload    |-> requests[t].payload,
         changed          |-> AnyChanged(t),
         participantToken |-> [p \in Participants |-> t] ]
  /\ results' =
       [results EXCEPT
          ![t] =
            [ outcome              |-> "committed",
              relation             |->
                IF AnyChanged(t) THEN "replacement" ELSE "exact",
              requestedPayload     |-> requests[t].payload,
              installedToken       |-> t,
              installedRevision    |-> installed.revision + 1,
              installedPayload     |-> requests[t].payload,
              installedChanged     |-> AnyChanged(t),
              participantToken     |-> [p \in Participants |-> t] ]]
  /\ liveAttempts' = liveAttempts \ {t}
  /\ commitGuardWitness' = FALSE
  /\ UNCHANGED
       << intent,
          requests,
          preparation,
          candidate,
          supersededAttempts,
          failedAttempts,
          staleCompletions,
          requestCorrelationWitness,
          relationWitness,
          abortRetentionWitness,
          staleCompletionWitness >>

Abort(t) ==
  /\ t \in liveAttempts
  /\ t = intent
  /\ (AnyFailed(t) \/ candidate[t] = "unprojectable")
  /\ installed' =
       IF Mutation = AbortChangesInstalled
       THEN
         [ token            |-> t,
           revision         |-> installed.revision + 1,
           sourcePayload    |-> requests[t].payload,
           changed          |-> TRUE,
           participantToken |-> [p \in Participants |-> t] ]
       ELSE installed
  /\ results' =
       [results EXCEPT
          ![t] =
            [ outcome              |-> "aborted",
              relation             |-> "retained",
              requestedPayload     |-> requests[t].payload,
              installedToken       |-> installed.token,
              installedRevision    |-> installed.revision,
              installedPayload     |-> installed.sourcePayload,
              installedChanged     |-> installed.changed,
              participantToken     |-> installed.participantToken ]]
  /\ liveAttempts' = liveAttempts \ {t}
  /\ abortRetentionWitness' = (abortRetentionWitness /\ installed' = installed)
  /\ UNCHANGED
       << intent,
          requests,
          preparation,
          candidate,
          supersededAttempts,
          failedAttempts,
          staleCompletions,
          commitGuardWitness,
          requestCorrelationWitness,
          relationWitness,
          staleCompletionWitness >>

DiscardSuperseded(t) ==
  /\ t \in liveAttempts
  /\ t # intent
  /\ results' =
       [results EXCEPT
          ![t] =
            [ outcome              |-> "discarded",
              relation             |-> "discarded",
              requestedPayload     |-> requests[t].payload,
              installedToken       |-> NoToken,
              installedRevision    |-> 0,
              installedPayload     |-> NoPayload,
              installedChanged     |-> FALSE,
              participantToken     |-> [p \in Participants |-> NoToken] ]]
  /\ liveAttempts' = liveAttempts \ {t}
  /\ UNCHANGED
       << intent,
          requests,
          preparation,
          candidate,
          installed,
          supersededAttempts,
          failedAttempts,
          staleCompletions,
          commitGuardWitness,
          requestCorrelationWitness,
          relationWitness,
          abortRetentionWitness,
          staleCompletionWitness >>

ObserveStaleCompletion(t, p) ==
  /\ results[t].outcome # "none"
  /\ t \notin staleCompletions
  /\ staleCompletions' = staleCompletions \cup {t}
  /\ installed' =
       IF Mutation = StaleCompletionInstalls
       THEN
         [ token            |-> t,
           revision         |-> installed.revision + 1,
           sourcePayload    |-> requests[t].payload,
           changed          |-> preparation[t][p].changed,
           participantToken |-> [q \in Participants |-> t] ]
       ELSE installed
  /\ staleCompletionWitness' =
       (staleCompletionWitness /\ installed' = installed)
  /\ UNCHANGED
       << intent,
          requests,
          preparation,
          liveAttempts,
          candidate,
          results,
          supersededAttempts,
          failedAttempts,
          commitGuardWitness,
          requestCorrelationWitness,
          relationWitness,
          abortRetentionWitness >>

ResolveParticipant(t, p) ==
  \/ \E changed \in BOOLEAN : ParticipantReady(t, p, changed)
  \/ ParticipantFailed(t, p)

CanonicalizeCandidate(t) ==
  \/ CanonicalizeCandidateSuccess(t)
  \/ CanonicalizeCandidateFailure(t)

SettleAttempt(t) ==
  \/ Commit(t)
  \/ Abort(t)
  \/ DiscardSuperseded(t)

Next ==
  \/ \E payload \in Payloads : BeginRestoration(payload)
  \/ \E t \in Tokens, p \in Participants : ResolveParticipant(t, p)
  \/ \E t \in Tokens : BuildCandidate(t)
  \/ \E t \in Tokens : CanonicalizeCandidate(t)
  \/ \E t \in Tokens : SettleAttempt(t)
  \/ \E t \in Tokens : CommitWithoutRequiredGuard(t)
  \/ \E t \in Tokens, p \in Participants : ObserveStaleCompletion(t, p)

Fairness ==
  /\ \A t \in Tokens, p \in Participants : WF_vars(ResolveParticipant(t, p))
  /\ \A t \in Tokens : WF_vars(BuildCandidate(t))
  /\ \A t \in Tokens : WF_vars(CanonicalizeCandidate(t))
  /\ \A t \in Tokens : WF_vars(SettleAttempt(t))

Spec == Init /\ [][Next]_vars /\ Fairness

(***************************************************************************)
(* Safety.                                                                 *)
(***************************************************************************)

CommitRequiresEveryParticipantAndCanonicalCandidate == commitGuardWitness

CommittedSnapshotCorrelatesExactRequest ==
  /\ requestCorrelationWitness
  /\ \A t \in Tokens :
       results[t].outcome = "committed" =>
         /\ results[t].requestedPayload = requests[t].payload
         /\ results[t].installedPayload = requests[t].payload

CommitRelationMatchesPreparedCandidate ==
  /\ relationWitness
  /\ \A t \in Tokens :
       results[t].outcome = "committed" =>
         /\ results[t].installedChanged = AnyChanged(t)
         /\ results[t].relation =
              IF AnyChanged(t) THEN "replacement" ELSE "exact"

InstalledSnapshotIsAtomic ==
  \A p \in Participants :
    installed.participantToken[p] = installed.token

EveryPublishedSnapshotIsAtomic ==
  \A t \in Tokens :
    results[t].outcome \in {"committed", "aborted"} =>
      \A p \in Participants :
        results[t].participantToken[p] = results[t].installedToken

FailedAttemptNeverCommits ==
  \A t \in Tokens :
    t \in failedAttempts => results[t].outcome \in {"none", "aborted", "discarded"}

SupersededAttemptNeverCommits ==
  \A t \in Tokens :
    t \in supersededAttempts => results[t].outcome \in {"none", "discarded"}

UnprojectableCandidateNeverCommits ==
  \A t \in Tokens :
    candidate[t] = "unprojectable" =>
      results[t].outcome \in {"none", "aborted", "discarded"}

PreparationIsInvisibleUntilCommit ==
  \A t \in Tokens :
    t \in liveAttempts => results[t].outcome = "none"

AbortRetainsInstalledSnapshotAndRevision ==
  /\ abortRetentionWitness
  /\ \A t \in Tokens :
       results[t].outcome = "aborted" =>
         /\ results[t].relation = "retained"
         /\ results[t].installedToken = requests[t].baseToken
         /\ results[t].installedRevision = requests[t].baseRevision
         /\ results[t].installedPayload = requests[t].basePayload
         /\ results[t].installedChanged = requests[t].baseChanged
         /\ results[t].participantToken = requests[t].baseParticipantToken

StaleCompletionCannotInstall == staleCompletionWitness

(***************************************************************************)
(* Progress.                                                               *)
(***************************************************************************)

EveryAttemptSettles ==
  \A t \in Tokens :
    t \in liveAttempts ~> results[t].outcome # "none"

EveryFailedAttemptSettlesWithoutCommit ==
  \A t \in Tokens :
    (t \in liveAttempts /\ AnyFailed(t))
      ~> results[t].outcome \in {"aborted", "discarded"}

=============================================================================
