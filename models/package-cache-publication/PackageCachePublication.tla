----------------------- MODULE PackageCachePublication -----------------------
EXTENDS Integers, FiniteSets, TLC

\* One exact package coordinate is shared within each process and published
\* independently across processes. The model deliberately separates protocol
\* observations from filesystem publication so TLC can explore the races
\* between the initial validity probe, the existence check, and the recheck.

CONSTANTS
    Processes,
    Callers,
    Process1,
    Process2,
    Process1Callers,
    Process2Callers,
    RetryCallers,
    MaxAttempts,
    NoOwner,
    PublicationMode,
    InitialTargetStates,
    EvictNonSuccess,
    ScriptedFirstAttempts,
    FirstAttemptOutcomes,
    AllowAttemptFailure,
    AllowFactoryCancellation,
    AllowCallerCancellation,
    AllowCrash,
    AllowRenameFailure

Owner(c) == IF c \in Process1Callers THEN Process1 ELSE Process2

ASSUME
    /\ Processes # {}
    /\ Callers # {}
    /\ Process1 \in Processes
    /\ Process2 \in Processes
    /\ Process1 # Process2
    /\ Processes = {Process1, Process2}
    /\ Process1Callers \subseteq Callers
    /\ Process2Callers \subseteq Callers
    /\ Process1Callers \cap Process2Callers = {}
    /\ Process1Callers \cup Process2Callers = Callers
    /\ RetryCallers \subseteq Callers
    /\ \A p \in Processes :
        /\ \E c \in RetryCallers : Owner(c) = p
        /\ \E c \in Callers \ RetryCallers : Owner(c) = p
    /\ MaxAttempts \in Nat \ {0}
    /\ NoOwner \notin Processes
    /\ PublicationMode \in {"AtomicRename", "DirectCopy"}
    /\ InitialTargetStates # {}
    /\ InitialTargetStates \subseteq {"Absent", "Invalid"}
    /\ EvictNonSuccess \in BOOLEAN
    /\ ScriptedFirstAttempts \subseteq Processes
    /\ FirstAttemptOutcomes # {}
    /\ FirstAttemptOutcomes \subseteq {"Failure", "FactoryCancelled"}
    /\ AllowAttemptFailure \in BOOLEAN
    /\ AllowFactoryCancellation \in BOOLEAN
    /\ AllowCallerCancellation \in BOOLEAN
    /\ AllowCrash \in BOOLEAN
    /\ AllowRenameFailure \in BOOLEAN

NoAttempt == 0
AttemptNumbers == 1..MaxAttempts
AttemptIds == Processes \X AttemptNumbers
NoJoinedAttempt == <<NoOwner, NoAttempt>>

CallerStates ==
    {"Dormant", "Idle", "Waiting", "Returned", "Failed",
     "FactoryCancelled", "CallerCancelled", "Crashed", "BoundedOut"}
TerminalCallerStates ==
    {"Returned", "Failed", "FactoryCancelled", "CallerCancelled",
     "Crashed", "BoundedOut"}
SettledWaitingStates ==
    {"Returned", "Failed", "FactoryCancelled", "CallerCancelled", "Crashed"}
SharedTerminalCallerStates == {"Returned", "Failed", "FactoryCancelled"}

Phases ==
    {"Idle", "Probe", "CheckExists", "Recheck", "Copying", "Validating",
     "Marking", "Rename", "DirectCopy", "Committed", "Resolving"}
TaskOutcomes ==
    {"Unused", "Pending", "Completing", "Success", "Failure",
     "FactoryCancelled"}
NonSuccessOutcomes == {"Failure", "FactoryCancelled"}
PendingResults == {"None", "Success", "Failure", "FactoryCancelled"}
StagingStates == {"Absent", "Partial", "Validated", "Marked", "Orphan"}
TargetStates == {"Absent", "Partial", "Complete", "Invalid"}

VARIABLES
    callerState,
    joined,
    attemptCount,
    registry,
    phase,
    outcome,
    pendingResult,
    staging,
    alive,
    targetState,
    targetOwner,
    publishers

vars ==
    <<callerState, joined, attemptCount, registry, phase, outcome,
      pendingResult, staging, alive, targetState, targetOwner, publishers>>

CurrentAttempt(p) == <<p, registry[p]>>

Active(p) ==
    /\ alive[p]
    /\ registry[p] # NoAttempt
    /\ outcome[CurrentAttempt(p)] = "Pending"

Init ==
    /\ callerState =
        [c \in Callers |-> IF c \in RetryCallers THEN "Dormant" ELSE "Idle"]
    /\ joined = [c \in Callers |-> NoJoinedAttempt]
    /\ attemptCount = [p \in Processes |-> 0]
    /\ registry = [p \in Processes |-> NoAttempt]
    /\ phase = [p \in Processes |-> "Idle"]
    /\ outcome = [a \in AttemptIds |-> "Unused"]
    /\ pendingResult = [a \in AttemptIds |-> "None"]
    /\ staging = [p \in Processes |-> "Absent"]
    /\ alive = [p \in Processes |-> TRUE]
    /\ targetState \in InitialTargetStates
    /\ targetOwner = NoOwner
    /\ publishers = {}

StartLeader(c) ==
    LET p == Owner(c)
        next == attemptCount[p] + 1
        a == <<p, next>>
    IN
    /\ callerState[c] = "Idle"
    /\ alive[p]
    /\ registry[p] = NoAttempt
    /\ attemptCount[p] < MaxAttempts
    /\ callerState' = [callerState EXCEPT ![c] = "Waiting"]
    /\ joined' = [joined EXCEPT ![c] = a]
    /\ attemptCount' = [attemptCount EXCEPT ![p] = next]
    /\ registry' = [registry EXCEPT ![p] = next]
    /\ phase' = [phase EXCEPT ![p] = "Probe"]
    /\ outcome' = [outcome EXCEPT ![a] = "Pending"]
    /\ pendingResult' = [pendingResult EXCEPT ![a] = "None"]
    /\ staging' = [staging EXCEPT ![p] = "Absent"]
    /\ UNCHANGED <<alive, targetState, targetOwner, publishers>>

JoinTask(c) ==
    LET p == Owner(c)
        a == CurrentAttempt(p)
    IN
    /\ callerState[c] = "Idle"
    /\ alive[p]
    /\ registry[p] # NoAttempt
    /\ callerState' = [callerState EXCEPT ![c] = "Waiting"]
    /\ joined' = [joined EXCEPT ![c] = a]
    /\ UNCHANGED
        <<attemptCount, registry, phase, outcome, pendingResult, staging, alive,
          targetState, targetOwner, publishers>>

Observe(c) ==
    LET taskResult == outcome[joined[c]]
    IN
    /\ callerState[c] = "Waiting"
    /\ taskResult \in {"Success", "Failure", "FactoryCancelled"}
    /\ callerState' =
        [callerState EXCEPT
            ![c] =
                CASE taskResult = "Success" -> "Returned"
                  [] taskResult = "Failure" -> "Failed"
                  [] OTHER -> "FactoryCancelled"]
    /\ UNCHANGED
        <<joined, attemptCount, registry, phase, outcome, pendingResult,
          staging, alive, targetState, targetOwner, publishers>>

CancelCallerWait(c) ==
    /\ AllowCallerCancellation
    /\ callerState[c] = "Waiting"
    /\ callerState' = [callerState EXCEPT ![c] = "CallerCancelled"]
    /\ UNCHANGED
        <<joined, attemptCount, registry, phase, outcome, pendingResult,
          staging, alive, targetState, targetOwner, publishers>>

BoundExhausted(c) ==
    LET p == Owner(c)
    IN
    /\ callerState[c] = "Idle"
    /\ registry[p] = NoAttempt
    /\ attemptCount[p] = MaxAttempts
    /\ callerState' = [callerState EXCEPT ![c] = "BoundedOut"]
    /\ UNCHANGED
        <<joined, attemptCount, registry, phase, outcome, pendingResult,
          staging, alive, targetState, targetOwner, publishers>>

BeginResolution(p, result) ==
    LET a == CurrentAttempt(p)
    IN
    /\ Active(p)
    /\ result \in {"Success", "Failure", "FactoryCancelled"}
    /\ phase[p] # "Resolving"
    /\ phase' = [phase EXCEPT ![p] = "Resolving"]
    /\ pendingResult' = [pendingResult EXCEPT ![a] = result]
    /\ UNCHANGED
        <<callerState, joined, attemptCount, registry, outcome, staging, alive,
          targetState, targetOwner, publishers>>

\* The value factory has settled in Resolving, but the shared ResolveAsync task
\* remains pending and registered.
EvictSettledEntry(p) ==
    LET a == CurrentAttempt(p)
        result == pendingResult[a]
    IN
    /\ Active(p)
    /\ phase[p] = "Resolving"
    /\ result \in {"Success", "Failure", "FactoryCancelled"}
    /\ (result = "Success" \/ EvictNonSuccess)
    /\ registry' = [registry EXCEPT ![p] = NoAttempt]
    /\ phase' = [phase EXCEPT ![p] = "Idle"]
    /\ outcome' = [outcome EXCEPT ![a] = "Completing"]
    /\ staging' = [staging EXCEPT ![p] = "Absent"]
    /\ UNCHANGED
        <<callerState, joined, attemptCount, pendingResult, alive, targetState,
          targetOwner, publishers>>

ActivateRetryCallers(p, result) ==
    [c \in Callers |->
        IF /\ result \in NonSuccessOutcomes
           /\ c \in RetryCallers
           /\ Owner(c) = p
           /\ callerState[c] = "Dormant"
        THEN "Idle"
        ELSE callerState[c]]

\* Negative-control behavior: a settled non-success remains in the registry,
\* so later callers keep joining it instead of starting a fresh attempt.
RetainSettledEntry(p) ==
    LET a == CurrentAttempt(p)
        result == pendingResult[a]
    IN
    /\ Active(p)
    /\ phase[p] = "Resolving"
    /\ result \in NonSuccessOutcomes
    /\ ~EvictNonSuccess
    /\ callerState' = ActivateRetryCallers(p, result)
    /\ phase' = [phase EXCEPT ![p] = "Idle"]
    /\ outcome' = [outcome EXCEPT ![a] = result]
    /\ pendingResult' = [pendingResult EXCEPT ![a] = "None"]
    /\ staging' = [staging EXCEPT ![p] = "Absent"]
    /\ UNCHANGED
        <<joined, attemptCount, registry, alive, targetState, targetOwner,
          publishers>>

\* Removal has made the registry slot reusable. The old outer task completes
\* later, and its existing waiters observe this attempt rather than a replacement.
PublishOutcome(a) ==
    LET p == a[1]
        result == pendingResult[a]
    IN
    /\ alive[p]
    /\ outcome[a] = "Completing"
    /\ result \in {"Success", "Failure", "FactoryCancelled"}
    /\ callerState' = ActivateRetryCallers(p, result)
    /\ outcome' = [outcome EXCEPT ![a] = result]
    /\ pendingResult' = [pendingResult EXCEPT ![a] = "None"]
    /\ UNCHANGED
        <<joined, attemptCount, registry, phase, staging, alive, targetState,
          targetOwner, publishers>>

ScriptedFirstResolution(p, result) ==
    /\ Active(p)
    /\ phase[p] = "Probe"
    /\ registry[p] = 1
    /\ p \in ScriptedFirstAttempts
    /\ result \in FirstAttemptOutcomes
    /\ BeginResolution(p, result)

ProbeHit(p) ==
    /\ Active(p)
    /\ phase[p] = "Probe"
    /\ ~(registry[p] = 1 /\ p \in ScriptedFirstAttempts)
    /\ targetState = "Complete"
    /\ BeginResolution(p, "Success")

ProbeMissOrInvalid(p) ==
    /\ Active(p)
    /\ phase[p] = "Probe"
    /\ ~(registry[p] = 1 /\ p \in ScriptedFirstAttempts)
    /\ targetState # "Complete"
    /\ phase' = [phase EXCEPT ![p] = "CheckExists"]
    /\ UNCHANGED
        <<callerState, joined, attemptCount, registry, outcome, pendingResult,
          staging, alive, targetState, targetOwner, publishers>>

ObserveAbsentTarget(p) ==
    /\ Active(p)
    /\ phase[p] = "CheckExists"
    /\ targetState = "Absent"
    /\ phase' = [phase EXCEPT ![p] = "Copying"]
    /\ UNCHANGED
        <<callerState, joined, attemptCount, registry, outcome, pendingResult,
          staging, alive, targetState, targetOwner, publishers>>

ObserveExistingTarget(p) ==
    /\ Active(p)
    /\ phase[p] = "CheckExists"
    /\ targetState # "Absent"
    /\ phase' = [phase EXCEPT ![p] = "Recheck"]
    /\ UNCHANGED
        <<callerState, joined, attemptCount, registry, outcome, pendingResult,
          staging, alive, targetState, targetOwner, publishers>>

RecheckValidTarget(p) ==
    /\ Active(p)
    /\ phase[p] = "Recheck"
    /\ targetState = "Complete"
    /\ BeginResolution(p, "Success")

RecheckInvalidTarget(p) ==
    /\ Active(p)
    /\ phase[p] = "Recheck"
    /\ targetState \in {"Partial", "Invalid"}
    /\ BeginResolution(p, "Failure")

CopyCandidate(p) ==
    /\ Active(p)
    /\ phase[p] = "Copying"
    /\ staging[p] = "Absent"
    /\ phase' = [phase EXCEPT ![p] = "Validating"]
    /\ staging' = [staging EXCEPT ![p] = "Partial"]
    /\ UNCHANGED
        <<callerState, joined, attemptCount, registry, outcome, pendingResult,
          alive, targetState, targetOwner, publishers>>

ValidateCandidate(p) ==
    /\ Active(p)
    /\ phase[p] = "Validating"
    /\ staging[p] = "Partial"
    /\ phase' = [phase EXCEPT ![p] = "Marking"]
    /\ staging' = [staging EXCEPT ![p] = "Validated"]
    /\ UNCHANGED
        <<callerState, joined, attemptCount, registry, outcome, pendingResult,
          alive, targetState, targetOwner, publishers>>

WriteCommitMarker(p) ==
    /\ Active(p)
    /\ phase[p] = "Marking"
    /\ staging[p] = "Validated"
    /\ phase' = [phase EXCEPT ![p] = "Rename"]
    /\ staging' = [staging EXCEPT ![p] = "Marked"]
    /\ UNCHANGED
        <<callerState, joined, attemptCount, registry, outcome, pendingResult,
          alive, targetState, targetOwner, publishers>>

\* This action is the local-filesystem assumption made executable: moving the
\* marked, non-empty sibling makes the complete target visible in one step.
AtomicRename(p) ==
    /\ Active(p)
    /\ PublicationMode = "AtomicRename"
    /\ phase[p] = "Rename"
    /\ staging[p] = "Marked"
    /\ targetState = "Absent"
    /\ phase' = [phase EXCEPT ![p] = "Committed"]
    /\ staging' = [staging EXCEPT ![p] = "Absent"]
    /\ targetState' = "Complete"
    /\ targetOwner' = p
    /\ publishers' = publishers \cup {p}
    /\ UNCHANGED
        <<callerState, joined, attemptCount, registry, outcome, pendingResult,
          alive>>

\* Negative control: direct copying exposes the final path before its contents
\* are complete. PackageCachePublicationBrokenAtomic.cfg enables this action.
BeginDirectCopy(p) ==
    /\ Active(p)
    /\ PublicationMode = "DirectCopy"
    /\ phase[p] = "Rename"
    /\ staging[p] = "Marked"
    /\ targetState = "Absent"
    /\ phase' = [phase EXCEPT ![p] = "DirectCopy"]
    /\ targetState' = "Partial"
    /\ targetOwner' = p
    /\ publishers' = publishers \cup {p}
    /\ UNCHANGED
        <<callerState, joined, attemptCount, registry, outcome, pendingResult,
          staging, alive>>

FinishDirectCopy(p) ==
    /\ Active(p)
    /\ phase[p] = "DirectCopy"
    /\ targetState = "Partial"
    /\ targetOwner = p
    /\ phase' = [phase EXCEPT ![p] = "Committed"]
    /\ staging' = [staging EXCEPT ![p] = "Absent"]
    /\ targetState' = "Complete"
    /\ UNCHANGED
        <<callerState, joined, attemptCount, registry, outcome, pendingResult,
          alive, targetOwner, publishers>>

FinishWinner(p) ==
    /\ Active(p)
    /\ phase[p] = "Committed"
    /\ targetState = "Complete"
    /\ targetOwner = p
    /\ BeginResolution(p, "Success")

ConvergeOnWinner(p) ==
    /\ Active(p)
    /\ phase[p] = "Rename"
    /\ staging[p] = "Marked"
    /\ targetState = "Complete"
    /\ BeginResolution(p, "Success")

AttemptFailure(p) ==
    /\ AllowAttemptFailure
    /\ Active(p)
    /\ phase[p] \in
        {"Probe", "CheckExists", "Recheck", "Copying", "Validating", "Marking"}
    /\ BeginResolution(p, "Failure")

FactoryCancellation(p) ==
    /\ AllowFactoryCancellation
    /\ Active(p)
    /\ phase[p] \in
        {"Probe", "CheckExists", "Recheck", "Copying", "Validating", "Marking"}
    /\ BeginResolution(p, "FactoryCancelled")

OtherRenameFailure(p) ==
    /\ AllowRenameFailure
    /\ Active(p)
    /\ phase[p] = "Rename"
    /\ targetState = "Absent"
    /\ BeginResolution(p, "Failure")

Crash(p) ==
    /\ AllowCrash
    /\ alive[p]
    /\ (\/ phase[p] # "Idle"
        \/ registry[p] # NoAttempt
        \/ \E a \in AttemptIds :
            a[1] = p /\ outcome[a] = "Completing")
    /\ callerState' =
        [c \in Callers |->
            IF Owner(c) = p /\ callerState[c] = "Waiting"
            THEN "Crashed"
            ELSE callerState[c]]
    /\ registry' = [registry EXCEPT ![p] = NoAttempt]
    /\ phase' = [phase EXCEPT ![p] = "Idle"]
    /\ staging' =
        [staging EXCEPT
            ![p] = IF staging[p] = "Absent" THEN "Absent" ELSE "Orphan"]
    /\ alive' = [alive EXCEPT ![p] = FALSE]
    /\ UNCHANGED
        <<joined, attemptCount, outcome, pendingResult, targetState,
          targetOwner, publishers>>

RequiredCallerStep(c) ==
    \/ StartLeader(c)
    \/ JoinTask(c)
    \/ Observe(c)
    \/ BoundExhausted(c)

ProtocolStep(p) ==
    \/ \E result \in FirstAttemptOutcomes :
        ScriptedFirstResolution(p, result)
    \/ ProbeHit(p)
    \/ ProbeMissOrInvalid(p)
    \/ ObserveAbsentTarget(p)
    \/ ObserveExistingTarget(p)
    \/ RecheckValidTarget(p)
    \/ RecheckInvalidTarget(p)
    \/ CopyCandidate(p)
    \/ ValidateCandidate(p)
    \/ WriteCommitMarker(p)
    \/ AtomicRename(p)
    \/ BeginDirectCopy(p)
    \/ FinishDirectCopy(p)
    \/ FinishWinner(p)
    \/ ConvergeOnWinner(p)
    \/ EvictSettledEntry(p)
    \/ RetainSettledEntry(p)

CompletionStep(a) ==
    PublishOutcome(a)

EnvironmentStep(p) ==
    \/ AttemptFailure(p)
    \/ FactoryCancellation(p)
    \/ OtherRenameFailure(p)
    \/ Crash(p)

Quiescent ==
    /\ \A p \in Processes : phase[p] = "Idle"
    /\ \A c \in Callers :
        \/ callerState[c] \notin {"Idle", "Waiting"}
        \/ ~alive[Owner(c)]

Quiesce ==
    /\ Quiescent
    /\ UNCHANGED vars

Next ==
    \/ \E c \in Callers : RequiredCallerStep(c)
    \/ \E c \in Callers : CancelCallerWait(c)
    \/ \E p \in Processes : ProtocolStep(p)
    \/ \E a \in AttemptIds : CompletionStep(a)
    \/ \E p \in Processes : EnvironmentStep(p)
    \/ Quiesce

Fairness ==
    /\ \A c \in Callers : WF_vars(RequiredCallerStep(c))
    /\ \A p \in Processes : WF_vars(ProtocolStep(p))
    /\ \A a \in AttemptIds : WF_vars(CompletionStep(a))

Spec ==
    /\ Init
    /\ [][Next]_vars
    /\ Fairness

TypeOK ==
    /\ callerState \in [Callers -> CallerStates]
    /\ joined \in [Callers -> ({NoJoinedAttempt} \cup AttemptIds)]
    /\ attemptCount \in [Processes -> 0..MaxAttempts]
    /\ registry \in [Processes -> 0..MaxAttempts]
    /\ phase \in [Processes -> Phases]
    /\ outcome \in [AttemptIds -> TaskOutcomes]
    /\ pendingResult \in [AttemptIds -> PendingResults]
    /\ staging \in [Processes -> StagingStates]
    /\ alive \in [Processes -> BOOLEAN]
    /\ targetState \in TargetStates
    /\ targetOwner \in Processes \cup {NoOwner}
    /\ publishers \subseteq Processes

FinalPathIsAtomic ==
    targetState # "Partial"

TargetOwnerIsConsistent ==
    /\ ((targetState \in {"Absent", "Invalid"}) => (targetOwner = NoOwner))
    /\ ((targetState \in {"Partial", "Complete"}) =>
            (targetOwner \in Processes))

AtMostOnePublisher ==
    Cardinality(publishers) <= 1

ReplacementOverlapsCompletion ==
    \E p \in Processes :
        /\ registry[p] # NoAttempt
        /\ \E a \in AttemptIds :
            /\ a[1] = p
            /\ a # CurrentAttempt(p)
            /\ outcome[a] = "Completing"

NoReplacementCompletionOverlap ==
    ~ReplacementOverlapsCompletion

RegistryContainsOnlyPendingTasks ==
    \A p \in Processes :
        registry[p] # NoAttempt => outcome[CurrentAttempt(p)] = "Pending"

SingleRegisteredAcquisitionPerProcess ==
    \A p \in Processes :
        Cardinality(
            {a \in AttemptIds : a[1] = p /\ outcome[a] = "Pending"}) <= 1

ObservedOutcomeMatchesJoinedTask ==
    \A c \in Callers :
        /\ ((callerState[c] = "Returned") =>
                (outcome[joined[c]] = "Success"))
        /\ ((callerState[c] = "Failed") =>
                (outcome[joined[c]] = "Failure"))
        /\ ((callerState[c] = "FactoryCancelled") =>
                (outcome[joined[c]] = "FactoryCancelled"))

ReturnedOnlyFromCompleteTarget ==
    \A c \in Callers :
        callerState[c] = "Returned" => targetState = "Complete"

JoinedCallersAgree ==
    \A c1, c2 \in Callers :
        /\ joined[c1] # NoJoinedAttempt
        /\ joined[c1] = joined[c2]
        /\ callerState[c1] \in SharedTerminalCallerStates
        /\ callerState[c2] \in SharedTerminalCallerStates
        => callerState[c1] = callerState[c2]

WaitingCallersSettle ==
    \A c \in Callers :
        (callerState[c] = "Waiting")
            ~> (callerState[c] \in SettledWaitingStates)

NonSuccessAttemptEventuallySucceeds ==
    \A p \in Processes :
        (outcome[<<p, 1>>] \in NonSuccessOutcomes)
            ~> (\E c \in Callers :
                    Owner(c) = p /\ callerState[c] = "Returned")

PublisherConvergesOnWinner ==
    \A a \in AttemptIds :
        (/\ registry[a[1]] = a[2]
         /\ phase[a[1]] = "Rename"
         /\ targetState = "Complete")
            ~> (outcome[a] = "Success" \/ ~alive[a[1]])

=============================================================================
