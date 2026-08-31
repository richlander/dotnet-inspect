--------------------- MODULE InspectWebWorkerLifecycle ----------------------
(***************************************************************************)
(* Finite lifecycle model for docs/design/inspect-web-worker-runtime.md.    *)
(*                                                                         *)
(* The model covers one worker epoch, two assigned operations, startup      *)
(* active time, matching readiness, bounded and unbounded silence, probes,  *)
(* lifecycle suspension, main-loop gaps, planned and unexpected draining,   *)
(* hard realm destruction, and producer quiescence.                         *)
(*                                                                         *)
(* It deliberately abstracts protocol payload parsing, operation replay,    *)
(* managed body behavior, feature publication, and real browser clocks.     *)
(***************************************************************************)
EXTENDS FiniteSets, Naturals, TLC

CONSTANTS
    OperationA,
    OperationB,
    MaxStartupBudget,
    MaxSilenceBudget,
    MaxDrainBudget,
    Mutation

Operations == {OperationA, OperationB}

BoundedAllowance == "BoundedAllowance"
UnboundedAllowance == "UnboundedAllowance"
Allowances == {BoundedAllowance, UnboundedAllowance}

OperationAllowance(o) ==
    IF o = OperationA THEN BoundedAllowance ELSE UnboundedAllowance

Starting == "Starting"
Ready == "Ready"
Suspect == "Suspect"
Draining == "Draining"
Closed == "Closed"
EpochStates == {Starting, Ready, Suspect, Draining, Closed}

NoCause == "NoCause"
PlannedCause == "PlannedCause"
UnexpectedCause == "UnexpectedCause"
StartupFailureCause == "StartupFailureCause"
WorkerDeclaredCause == "WorkerDeclaredCause"
UnexpectedCauses ==
    {UnexpectedCause, StartupFailureCause, WorkerDeclaredCause}
ClosureCauses == {NoCause, PlannedCause} \cup UnexpectedCauses

NoOutcome == "NoOutcome"
SucceededOutcome == "SucceededOutcome"
FailedOutcome == "FailedOutcome"
CanceledOutcome == "CanceledOutcome"
Outcomes == {NoOutcome, SucceededOutcome, FailedOutcome, CanceledOutcome}

NoWork == "NoWork"
BoundedWork == "BoundedWork"
UnboundedWork == "UnboundedWork"
WorkStates == {NoWork, BoundedWork, UnboundedWork}

NoMutation == "None"
RenewStartupFromMessage == "RenewStartupFromMessage"
ResetStartupOnResume == "ResetStartupOnResume"
ProbeSatisfiesStartup == "ProbeSatisfiesStartup"
AcceptMismatchedReady == "AcceptMismatchedReady"
TerminateAtFirstExpiry == "TerminateAtFirstExpiry"
TerminateWhileUnbounded == "TerminateWhileUnbounded"
TerminateAcrossMainGap == "TerminateAcrossMainGap"
AcceptDuringDrain == "AcceptDuringDrain"
PlannedAsFailure == "PlannedAsFailure"
UnexpectedAsCancellation == "UnexpectedAsCancellation"
QuiesceBeforeRelease == "QuiesceBeforeRelease"
CallbackAfterRelease == "CallbackAfterRelease"
DrainNeverCloses == "DrainNeverCloses"
NonTaskMessageRenews == "NonTaskMessageRenews"
BoundedSilenceRun == "BoundedSilenceRun"
AllowanceChurnRenews == "AllowanceChurnRenews"
BootstrapFailureDrains == "BootstrapFailureDrains"
WorkerDeclaredAsCancellation == "WorkerDeclaredAsCancellation"
AcceptReadyAfterStartupExpiry == "AcceptReadyAfterStartupExpiry"
Mutations ==
    {NoMutation,
     RenewStartupFromMessage,
     ResetStartupOnResume,
     ProbeSatisfiesStartup,
     AcceptMismatchedReady,
     TerminateAtFirstExpiry,
     TerminateWhileUnbounded,
     TerminateAcrossMainGap,
     AcceptDuringDrain,
     PlannedAsFailure,
     UnexpectedAsCancellation,
     QuiesceBeforeRelease,
     CallbackAfterRelease,
     DrainNeverCloses,
     NonTaskMessageRenews,
     BoundedSilenceRun,
     AllowanceChurnRenews,
     BootstrapFailureDrains,
     WorkerDeclaredAsCancellation,
     AcceptReadyAfterStartupExpiry}

BoundedSilenceScenario ==
    Mutation \in {BoundedSilenceRun, AllowanceChurnRenews}

ASSUME
    /\ Cardinality(Operations) = 2
    /\ OperationA # OperationB
    /\ MaxStartupBudget \in Nat
    /\ MaxStartupBudget > 0
    /\ MaxSilenceBudget \in Nat
    /\ MaxSilenceBudget > 0
    /\ MaxDrainBudget \in Nat
    /\ MaxDrainBudget > 0
    /\ Mutation \in Mutations

VARIABLES
    epochState,
    closureCause,
    startupRemaining,
    startupRenewed,
    readyMatched,
    lifecycleActive,
    suspensionBudget,
    mainLoopContinuous,
    gapBudget,
    assigned,
    accepted,
    released,
    outcome,
    quiesced,
    workState,
    silenceRemaining,
    probeOutstanding,
    probeWasSent,
    firstExpiryObserved,
    drainRemaining,
    realmDestroyed,
    sourceRevoked,
    assignedAtClosure,
    startDuringDrain,
    mismatchedReadyAccepted,
    probeSatisfiedStartup,
    watchdogWithoutProbe,
    unboundedWatchdogFailure,
    mainGapWatchdogFailure,
    plannedOutcomeMismatch,
    unexpectedOutcomeMismatch,
    quiescedBeforeRelease,
    callbackAfterReleaseObserved,
    nonTaskMessageRenewed

vars ==
    <<epochState,
      closureCause,
      startupRemaining,
      startupRenewed,
      readyMatched,
      lifecycleActive,
      suspensionBudget,
      mainLoopContinuous,
      gapBudget,
      assigned,
      accepted,
      released,
      outcome,
      quiesced,
      workState,
      silenceRemaining,
      probeOutstanding,
      probeWasSent,
      firstExpiryObserved,
      drainRemaining,
      realmDestroyed,
      sourceRevoked,
      assignedAtClosure,
      startDuringDrain,
      mismatchedReadyAccepted,
      probeSatisfiedStartup,
      watchdogWithoutProbe,
      unboundedWatchdogFailure,
      mainGapWatchdogFailure,
      plannedOutcomeMismatch,
      unexpectedOutcomeMismatch,
      quiescedBeforeRelease,
      callbackAfterReleaseObserved,
      nonTaskMessageRenewed>>

Init ==
    /\ epochState = Starting
    /\ closureCause = NoCause
    /\ startupRemaining = MaxStartupBudget
    /\ startupRenewed = FALSE
    /\ readyMatched = FALSE
    /\ lifecycleActive = TRUE
    /\ suspensionBudget = 1
    /\ mainLoopContinuous = TRUE
    /\ gapBudget = 1
    /\ assigned = {}
    /\ accepted = {}
    /\ released = {}
    /\ outcome = [o \in Operations |-> NoOutcome]
    /\ quiesced = {}
    /\ workState = NoWork
    /\ silenceRemaining = MaxSilenceBudget
    /\ probeOutstanding = FALSE
    /\ probeWasSent = FALSE
    /\ firstExpiryObserved = FALSE
    /\ drainRemaining = MaxDrainBudget
    /\ realmDestroyed = FALSE
    /\ sourceRevoked = FALSE
    /\ assignedAtClosure = {}
    /\ startDuringDrain = FALSE
    /\ mismatchedReadyAccepted = FALSE
    /\ probeSatisfiedStartup = FALSE
    /\ watchdogWithoutProbe = FALSE
    /\ unboundedWatchdogFailure = FALSE
    /\ mainGapWatchdogFailure = FALSE
    /\ plannedOutcomeMismatch = FALSE
    /\ unexpectedOutcomeMismatch = FALSE
    /\ quiescedBeforeRelease = FALSE
    /\ callbackAfterReleaseObserved = FALSE
    /\ nonTaskMessageRenewed = FALSE

UnchangedMutationFlags ==
    UNCHANGED
        <<startDuringDrain,
          mismatchedReadyAccepted,
          probeSatisfiedStartup,
          watchdogWithoutProbe,
          unboundedWatchdogFailure,
          mainGapWatchdogFailure,
          plannedOutcomeMismatch,
          unexpectedOutcomeMismatch,
          quiescedBeforeRelease,
          callbackAfterReleaseObserved,
          nonTaskMessageRenewed>>

AssignOperation(o) ==
    /\ epochState \in {Starting, Ready, Suspect}
    /\ o \notin assigned
    /\ assigned' = assigned \cup {o}
    /\ UNCHANGED
        <<epochState,
          closureCause,
          startupRemaining,
          startupRenewed,
          readyMatched,
          lifecycleActive,
          suspensionBudget,
          mainLoopContinuous,
          gapBudget,
          accepted,
          released,
          outcome,
          quiesced,
          workState,
          silenceRemaining,
          probeOutstanding,
          probeWasSent,
          firstExpiryObserved,
          drainRemaining,
          realmDestroyed,
          sourceRevoked,
          assignedAtClosure>>
    /\ UnchangedMutationFlags

AcceptOperation(o) ==
    /\ ~(BoundedSilenceScenario
          /\ OperationAllowance(o) = UnboundedAllowance)
    /\ epochState \in {Ready, Suspect}
    /\ o \in assigned
    /\ o \notin accepted
    /\ o \notin released
    /\ accepted' = accepted \cup {o}
    /\ epochState' = Ready
    /\ silenceRemaining' = MaxSilenceBudget
    /\ UNCHANGED probeOutstanding
    /\ UNCHANGED
        <<closureCause,
          startupRemaining,
          startupRenewed,
          readyMatched,
          lifecycleActive,
          suspensionBudget,
          mainLoopContinuous,
          gapBudget,
          assigned,
          released,
          outcome,
          quiesced,
          workState,
          probeWasSent,
          firstExpiryObserved,
          drainRemaining,
          realmDestroyed,
          sourceRevoked,
          assignedAtClosure>>
    /\ UnchangedMutationFlags

AcceptDuringDrainMutation(o) ==
    /\ Mutation = AcceptDuringDrain
    /\ epochState = Draining
    /\ o \notin assigned
    /\ assigned' = assigned \cup {o}
    /\ accepted' = accepted \cup {o}
    /\ startDuringDrain' = TRUE
    /\ UNCHANGED
        <<epochState,
          closureCause,
          startupRemaining,
          startupRenewed,
          readyMatched,
          lifecycleActive,
          suspensionBudget,
          mainLoopContinuous,
          gapBudget,
          released,
          outcome,
          quiesced,
          workState,
          silenceRemaining,
          probeOutstanding,
          probeWasSent,
          firstExpiryObserved,
          drainRemaining,
          realmDestroyed,
          sourceRevoked,
          assignedAtClosure,
          mismatchedReadyAccepted,
          probeSatisfiedStartup,
          watchdogWithoutProbe,
          unboundedWatchdogFailure,
          mainGapWatchdogFailure,
          plannedOutcomeMismatch,
          unexpectedOutcomeMismatch,
          quiescedBeforeRelease,
          callbackAfterReleaseObserved,
          nonTaskMessageRenewed>>

StartupTick ==
    /\ epochState = Starting
    /\ lifecycleActive
    /\ mainLoopContinuous
    /\ startupRemaining > 0
    /\ startupRemaining' = startupRemaining - 1
    /\ UNCHANGED
        <<epochState,
          closureCause,
          startupRenewed,
          readyMatched,
          lifecycleActive,
          suspensionBudget,
          mainLoopContinuous,
          gapBudget,
          assigned,
          accepted,
          released,
          outcome,
          quiesced,
          workState,
          silenceRemaining,
          probeOutstanding,
          probeWasSent,
          firstExpiryObserved,
          drainRemaining,
          realmDestroyed,
          sourceRevoked,
          assignedAtClosure>>
    /\ UnchangedMutationFlags

StartupMessage ==
    /\ epochState = Starting
    /\ IF Mutation = RenewStartupFromMessage
       THEN
           /\ startupRemaining' = MaxStartupBudget
           /\ startupRenewed' = TRUE
       ELSE
           /\ UNCHANGED <<startupRemaining, startupRenewed>>
    /\ UNCHANGED
        <<epochState,
          closureCause,
          readyMatched,
          lifecycleActive,
          suspensionBudget,
          mainLoopContinuous,
          gapBudget,
          assigned,
          accepted,
          released,
          outcome,
          quiesced,
          workState,
          silenceRemaining,
          probeOutstanding,
          probeWasSent,
          firstExpiryObserved,
          drainRemaining,
          realmDestroyed,
          sourceRevoked,
          assignedAtClosure>>
    /\ UnchangedMutationFlags

StartupProbeAcknowledged ==
    /\ epochState = Starting
    /\ IF Mutation = ProbeSatisfiesStartup
       THEN
           /\ epochState' = Ready
           /\ readyMatched' = FALSE
           /\ probeSatisfiedStartup' = TRUE
       ELSE
           /\ UNCHANGED <<epochState, readyMatched>>
           /\ probeSatisfiedStartup' = FALSE
    /\ UNCHANGED
        <<closureCause,
          startupRemaining,
          startupRenewed,
          lifecycleActive,
          suspensionBudget,
          mainLoopContinuous,
          gapBudget,
          assigned,
          accepted,
          released,
          outcome,
          quiesced,
          workState,
          silenceRemaining,
          probeOutstanding,
          probeWasSent,
          firstExpiryObserved,
          drainRemaining,
          realmDestroyed,
          sourceRevoked,
          assignedAtClosure,
          startDuringDrain,
          mismatchedReadyAccepted,
          watchdogWithoutProbe,
          unboundedWatchdogFailure,
          mainGapWatchdogFailure,
          plannedOutcomeMismatch,
          unexpectedOutcomeMismatch,
          quiescedBeforeRelease,
          callbackAfterReleaseObserved,
          nonTaskMessageRenewed>>

ReceiveReady ==
    /\ epochState = Starting
    /\ startupRemaining > 0
    /\ epochState' = Ready
    /\ readyMatched' = TRUE
    /\ silenceRemaining' = MaxSilenceBudget
    /\ UNCHANGED
        <<closureCause,
          startupRemaining,
          startupRenewed,
          lifecycleActive,
          suspensionBudget,
          mainLoopContinuous,
          gapBudget,
          assigned,
          accepted,
          released,
          outcome,
          quiesced,
          workState,
          probeOutstanding,
          probeWasSent,
          firstExpiryObserved,
          drainRemaining,
          realmDestroyed,
          sourceRevoked,
          assignedAtClosure>>
    /\ UnchangedMutationFlags

AcceptReadyAfterStartupExpiryMutation ==
    /\ Mutation = AcceptReadyAfterStartupExpiry
    /\ epochState = Starting
    /\ startupRemaining = 0
    /\ epochState' = Ready
    /\ readyMatched' = TRUE
    /\ silenceRemaining' = MaxSilenceBudget
    /\ UNCHANGED
        <<closureCause,
          startupRemaining,
          startupRenewed,
          lifecycleActive,
          suspensionBudget,
          mainLoopContinuous,
          gapBudget,
          assigned,
          accepted,
          released,
          outcome,
          quiesced,
          workState,
          probeOutstanding,
          probeWasSent,
          firstExpiryObserved,
          drainRemaining,
          realmDestroyed,
          sourceRevoked,
          assignedAtClosure>>
    /\ UnchangedMutationFlags

ClosePartialRealm ==
    /\ epochState = Starting
    /\ epochState' = Closed
    /\ closureCause' = StartupFailureCause
    /\ assignedAtClosure' = assigned \ released
    /\ outcome' =
        [o \in Operations |->
            IF o \in assigned \ released
            THEN FailedOutcome
            ELSE outcome[o]]
    /\ released' = released \cup (assigned \ released)
    /\ quiesced' = quiesced \cup (assigned \ released)
    /\ accepted' = {}
    /\ workState' = NoWork
    /\ realmDestroyed' = TRUE
    /\ sourceRevoked' = TRUE
    /\ probeOutstanding' = FALSE
    /\ UNCHANGED
        <<startupRemaining,
          startupRenewed,
          readyMatched,
          lifecycleActive,
          suspensionBudget,
          mainLoopContinuous,
          gapBudget,
          assigned,
          silenceRemaining,
          probeWasSent,
          firstExpiryObserved,
          drainRemaining>>
    /\ UnchangedMutationFlags

ReceiveMismatchedReady ==
    /\ Mutation # AcceptMismatchedReady
    /\ ClosePartialRealm

AcceptMismatchedReadyMutation ==
    /\ Mutation = AcceptMismatchedReady
    /\ epochState = Starting
    /\ epochState' = Ready
    /\ readyMatched' = FALSE
    /\ mismatchedReadyAccepted' = TRUE
    /\ drainRemaining' = MaxDrainBudget
    /\ probeOutstanding' = FALSE
    /\ UNCHANGED
        <<closureCause,
          startupRemaining,
          startupRenewed,
          lifecycleActive,
          suspensionBudget,
          mainLoopContinuous,
          gapBudget,
          assigned,
          accepted,
          released,
          outcome,
          quiesced,
          workState,
          silenceRemaining,
          probeWasSent,
          firstExpiryObserved,
          realmDestroyed,
          sourceRevoked,
          assignedAtClosure,
          startDuringDrain,
          probeSatisfiedStartup,
          watchdogWithoutProbe,
          unboundedWatchdogFailure,
          mainGapWatchdogFailure,
          plannedOutcomeMismatch,
          unexpectedOutcomeMismatch,
          quiescedBeforeRelease,
          callbackAfterReleaseObserved,
          nonTaskMessageRenewed>>

StartupExpires ==
    /\ startupRemaining = 0
    /\ ClosePartialRealm

ReceiveBootstrapFailure ==
    /\ Mutation # BootstrapFailureDrains
    /\ ClosePartialRealm

BootstrapFailureDrainsMutation ==
    /\ Mutation = BootstrapFailureDrains
    /\ epochState = Starting
    /\ epochState' = Draining
    /\ closureCause' = StartupFailureCause
    /\ assignedAtClosure' = assigned \ released
    /\ outcome' =
        [o \in Operations |->
            IF o \in assigned \ released
            THEN FailedOutcome
            ELSE outcome[o]]
    /\ drainRemaining' = MaxDrainBudget
    /\ probeOutstanding' = FALSE
    /\ UNCHANGED
        <<startupRemaining,
          startupRenewed,
          readyMatched,
          lifecycleActive,
          suspensionBudget,
          mainLoopContinuous,
          gapBudget,
          assigned,
          accepted,
          released,
          quiesced,
          workState,
          silenceRemaining,
          probeWasSent,
          firstExpiryObserved,
          realmDestroyed,
          sourceRevoked>>
    /\ UnchangedMutationFlags

SuspendLifecycle ==
    /\ ~BoundedSilenceScenario
    /\ lifecycleActive
    /\ suspensionBudget > 0
    /\ epochState \in {Starting, Ready, Suspect, Draining}
    /\ lifecycleActive' = FALSE
    /\ suspensionBudget' = suspensionBudget - 1
    /\ UNCHANGED
        <<epochState,
          closureCause,
          startupRemaining,
          startupRenewed,
          readyMatched,
          mainLoopContinuous,
          gapBudget,
          assigned,
          accepted,
          released,
          outcome,
          quiesced,
          workState,
          silenceRemaining,
          probeOutstanding,
          probeWasSent,
          firstExpiryObserved,
          drainRemaining,
          realmDestroyed,
          sourceRevoked,
          assignedAtClosure>>
    /\ UnchangedMutationFlags

ResumeLifecycle ==
    /\ ~lifecycleActive
    /\ lifecycleActive' = TRUE
    /\ IF Mutation = ResetStartupOnResume /\ epochState = Starting
       THEN
           /\ startupRemaining' = MaxStartupBudget
           /\ startupRenewed' = TRUE
       ELSE
           /\ UNCHANGED <<startupRemaining, startupRenewed>>
    /\ IF epochState \in {Ready, Suspect}
       THEN
           /\ epochState' = Ready
           /\ silenceRemaining' = MaxSilenceBudget
           /\ UNCHANGED probeOutstanding
       ELSE
           /\ UNCHANGED <<epochState, silenceRemaining, probeOutstanding>>
    /\ UNCHANGED
        <<closureCause,
          readyMatched,
          suspensionBudget,
          mainLoopContinuous,
          gapBudget,
          assigned,
          accepted,
          released,
          outcome,
          quiesced,
          workState,
          probeWasSent,
          firstExpiryObserved,
          drainRemaining,
          realmDestroyed,
          sourceRevoked,
          assignedAtClosure>>
    /\ UnchangedMutationFlags

DetectMainLoopGap ==
    /\ ~BoundedSilenceScenario
    /\ mainLoopContinuous
    /\ gapBudget > 0
    /\ epochState \in {Starting, Ready, Suspect, Draining}
    /\ mainLoopContinuous' = FALSE
    /\ gapBudget' = gapBudget - 1
    /\ UNCHANGED
        <<epochState,
          closureCause,
          startupRemaining,
          startupRenewed,
          readyMatched,
          lifecycleActive,
          suspensionBudget,
          assigned,
          accepted,
          released,
          outcome,
          quiesced,
          workState,
          silenceRemaining,
          probeOutstanding,
          probeWasSent,
          firstExpiryObserved,
          drainRemaining,
          realmDestroyed,
          sourceRevoked,
          assignedAtClosure>>
    /\ UnchangedMutationFlags

ResumeMainLoop ==
    /\ ~mainLoopContinuous
    /\ mainLoopContinuous' = TRUE
    /\ IF epochState \in {Ready, Suspect}
       THEN
           /\ epochState' = Ready
           /\ silenceRemaining' = MaxSilenceBudget
           /\ UNCHANGED probeOutstanding
       ELSE
           /\ UNCHANGED <<epochState, silenceRemaining, probeOutstanding>>
    /\ UNCHANGED
        <<closureCause,
          startupRemaining,
          startupRenewed,
          readyMatched,
          lifecycleActive,
          suspensionBudget,
          gapBudget,
          assigned,
          accepted,
          released,
          outcome,
          quiesced,
          workState,
          probeWasSent,
          firstExpiryObserved,
          drainRemaining,
          realmDestroyed,
          sourceRevoked,
          assignedAtClosure>>
    /\ UnchangedMutationFlags

HasUnboundedAllowance ==
    \/ \E o \in accepted: OperationAllowance(o) = UnboundedAllowance
    \/ workState = UnboundedWork

TaskLoopEvidence ==
    /\ ~BoundedSilenceScenario
    /\ epochState \in {Ready, Suspect}
    /\ epochState' = Ready
    /\ silenceRemaining' = MaxSilenceBudget
    /\ UNCHANGED probeOutstanding
    /\ UNCHANGED
        <<closureCause,
          startupRemaining,
          startupRenewed,
          readyMatched,
          lifecycleActive,
          suspensionBudget,
          mainLoopContinuous,
          gapBudget,
          assigned,
          accepted,
          released,
          outcome,
          quiesced,
          workState,
          probeWasSent,
          firstExpiryObserved,
          drainRemaining,
          realmDestroyed,
          sourceRevoked,
          assignedAtClosure>>
    /\ UnchangedMutationFlags

ProbeAcknowledgmentEvidence ==
    /\ ~BoundedSilenceScenario
    /\ epochState \in {Ready, Suspect}
    /\ probeOutstanding
    /\ epochState' = Ready
    /\ silenceRemaining' = MaxSilenceBudget
    /\ probeOutstanding' = FALSE
    /\ UNCHANGED
        <<closureCause,
          startupRemaining,
          startupRenewed,
          readyMatched,
          lifecycleActive,
          suspensionBudget,
          mainLoopContinuous,
          gapBudget,
          assigned,
          accepted,
          released,
          outcome,
          quiesced,
          workState,
          probeWasSent,
          firstExpiryObserved,
          drainRemaining,
          realmDestroyed,
          sourceRevoked,
          assignedAtClosure>>
    /\ UnchangedMutationFlags

NonTaskMessage ==
    /\ epochState \in {Ready, Suspect}
    /\ IF Mutation = NonTaskMessageRenews
       THEN
           /\ epochState' = Ready
           /\ silenceRemaining' = MaxSilenceBudget
           /\ probeOutstanding' = FALSE
           /\ nonTaskMessageRenewed' = TRUE
       ELSE
           /\ UNCHANGED
               <<epochState,
                 silenceRemaining,
                 probeOutstanding,
                 nonTaskMessageRenewed>>
    /\ UNCHANGED
        <<closureCause,
          startupRemaining,
          startupRenewed,
          readyMatched,
          lifecycleActive,
          suspensionBudget,
          mainLoopContinuous,
          gapBudget,
          assigned,
          accepted,
          released,
          outcome,
          quiesced,
          workState,
          probeWasSent,
          firstExpiryObserved,
          drainRemaining,
          realmDestroyed,
          sourceRevoked,
          assignedAtClosure,
          startDuringDrain,
          mismatchedReadyAccepted,
          probeSatisfiedStartup,
          watchdogWithoutProbe,
          unboundedWatchdogFailure,
          mainGapWatchdogFailure,
          plannedOutcomeMismatch,
          unexpectedOutcomeMismatch,
          quiescedBeforeRelease,
          callbackAfterReleaseObserved>>

SilenceTick ==
    /\ epochState \in {Ready, Suspect}
    /\ lifecycleActive
    /\ mainLoopContinuous
    /\ ~HasUnboundedAllowance
    /\ silenceRemaining > 0
    /\ silenceRemaining' = silenceRemaining - 1
    /\ UNCHANGED
        <<epochState,
          closureCause,
          startupRemaining,
          startupRenewed,
          readyMatched,
          lifecycleActive,
          suspensionBudget,
          mainLoopContinuous,
          gapBudget,
          assigned,
          accepted,
          released,
          outcome,
          quiesced,
          workState,
          probeOutstanding,
          probeWasSent,
          firstExpiryObserved,
          drainRemaining,
          realmDestroyed,
          sourceRevoked,
          assignedAtClosure>>
    /\ UnchangedMutationFlags

FirstSilenceExpiry ==
    /\ epochState = Ready
    /\ lifecycleActive
    /\ mainLoopContinuous
    /\ ~HasUnboundedAllowance
    /\ silenceRemaining = 0
    /\ IF Mutation = TerminateAtFirstExpiry
       THEN
           /\ epochState' = Draining
           /\ closureCause' = UnexpectedCause
           /\ assignedAtClosure' = assigned \ released
           /\ outcome' =
               [o \in Operations |->
                   IF o \in assigned /\ outcome[o] = NoOutcome
                   THEN FailedOutcome
                   ELSE outcome[o]]
           /\ drainRemaining' = MaxDrainBudget
           /\ watchdogWithoutProbe' = TRUE
           /\ probeOutstanding' = FALSE
           /\ UNCHANGED probeWasSent
       ELSE
           /\ epochState' = Suspect
           /\ closureCause' = closureCause
           /\ assignedAtClosure' = assignedAtClosure
           /\ UNCHANGED outcome
           /\ UNCHANGED drainRemaining
           /\ watchdogWithoutProbe' = FALSE
           /\ probeOutstanding' = TRUE
           /\ probeWasSent' = TRUE
    /\ silenceRemaining' = MaxSilenceBudget
    /\ firstExpiryObserved' = TRUE
    /\ UNCHANGED
        <<startupRemaining,
          startupRenewed,
          readyMatched,
          lifecycleActive,
          suspensionBudget,
          mainLoopContinuous,
          gapBudget,
          assigned,
          accepted,
          released,
          quiesced,
          workState,
          realmDestroyed,
          sourceRevoked,
          startDuringDrain,
          mismatchedReadyAccepted,
          probeSatisfiedStartup,
          unboundedWatchdogFailure,
          mainGapWatchdogFailure,
          plannedOutcomeMismatch,
          unexpectedOutcomeMismatch,
          quiescedBeforeRelease,
          callbackAfterReleaseObserved,
          nonTaskMessageRenewed>>

AllowanceChurnRenewalMutation ==
    /\ Mutation = AllowanceChurnRenews
    /\ epochState = Suspect
    /\ probeOutstanding
    /\ workState = NoWork
    /\ workState' = BoundedWork
    /\ epochState' = Ready
    /\ silenceRemaining' = MaxSilenceBudget
    /\ probeOutstanding' = FALSE
    /\ UNCHANGED
        <<closureCause,
          startupRemaining,
          startupRenewed,
          readyMatched,
          lifecycleActive,
          suspensionBudget,
          mainLoopContinuous,
          gapBudget,
          assigned,
          accepted,
          released,
          outcome,
          quiesced,
          probeWasSent,
          firstExpiryObserved,
          drainRemaining,
          realmDestroyed,
          sourceRevoked,
          assignedAtClosure,
          startDuringDrain,
          mismatchedReadyAccepted,
          probeSatisfiedStartup,
          watchdogWithoutProbe,
          unboundedWatchdogFailure,
          mainGapWatchdogFailure,
          plannedOutcomeMismatch,
          unexpectedOutcomeMismatch,
          quiescedBeforeRelease,
          callbackAfterReleaseObserved,
          nonTaskMessageRenewed>>

SecondSilenceExpiry ==
    /\ epochState = Suspect
    /\ lifecycleActive
    /\ mainLoopContinuous
    /\ ~HasUnboundedAllowance
    /\ silenceRemaining = 0
    /\ epochState' = Draining
    /\ closureCause' = UnexpectedCause
    /\ assignedAtClosure' = assigned \ released
    /\ outcome' =
        [o \in Operations |->
            IF o \in assigned /\ outcome[o] = NoOutcome
            THEN FailedOutcome
            ELSE outcome[o]]
    /\ drainRemaining' = MaxDrainBudget
    /\ probeOutstanding' = FALSE
    /\ UNCHANGED
        <<startupRemaining,
          startupRenewed,
          readyMatched,
          lifecycleActive,
          suspensionBudget,
          mainLoopContinuous,
          gapBudget,
          assigned,
          accepted,
          released,
          quiesced,
          workState,
          silenceRemaining,
          probeWasSent,
          firstExpiryObserved,
          realmDestroyed,
          sourceRevoked>>
    /\ UnchangedMutationFlags

TerminateWhileUnboundedMutation ==
    /\ Mutation = TerminateWhileUnbounded
    /\ epochState \in {Ready, Suspect}
    /\ HasUnboundedAllowance
    /\ epochState' = Draining
    /\ closureCause' = UnexpectedCause
    /\ assignedAtClosure' = assigned \ released
    /\ outcome' =
        [o \in Operations |->
            IF o \in assigned /\ outcome[o] = NoOutcome
            THEN FailedOutcome
            ELSE outcome[o]]
    /\ drainRemaining' = MaxDrainBudget
    /\ unboundedWatchdogFailure' = TRUE
    /\ probeOutstanding' = FALSE
    /\ UNCHANGED
        <<startupRemaining,
          startupRenewed,
          readyMatched,
          lifecycleActive,
          suspensionBudget,
          mainLoopContinuous,
          gapBudget,
          assigned,
          accepted,
          released,
          quiesced,
          workState,
          silenceRemaining,
          probeWasSent,
          firstExpiryObserved,
          realmDestroyed,
          sourceRevoked,
          startDuringDrain,
          mismatchedReadyAccepted,
          probeSatisfiedStartup,
          watchdogWithoutProbe,
          mainGapWatchdogFailure,
          plannedOutcomeMismatch,
          unexpectedOutcomeMismatch,
          quiescedBeforeRelease,
          callbackAfterReleaseObserved,
          nonTaskMessageRenewed>>

TerminateAcrossMainGapMutation ==
    /\ Mutation = TerminateAcrossMainGap
    /\ epochState \in {Ready, Suspect}
    /\ ~mainLoopContinuous
    /\ epochState' = Draining
    /\ closureCause' = UnexpectedCause
    /\ assignedAtClosure' = assigned \ released
    /\ outcome' =
        [o \in Operations |->
            IF o \in assigned /\ outcome[o] = NoOutcome
            THEN FailedOutcome
            ELSE outcome[o]]
    /\ drainRemaining' = MaxDrainBudget
    /\ mainGapWatchdogFailure' = TRUE
    /\ probeOutstanding' = FALSE
    /\ UNCHANGED
        <<startupRemaining,
          startupRenewed,
          readyMatched,
          lifecycleActive,
          suspensionBudget,
          mainLoopContinuous,
          gapBudget,
          assigned,
          accepted,
          released,
          quiesced,
          workState,
          silenceRemaining,
          probeWasSent,
          firstExpiryObserved,
          realmDestroyed,
          sourceRevoked,
          startDuringDrain,
          mismatchedReadyAccepted,
          probeSatisfiedStartup,
          watchdogWithoutProbe,
          unboundedWatchdogFailure,
          plannedOutcomeMismatch,
          unexpectedOutcomeMismatch,
          quiescedBeforeRelease,
          callbackAfterReleaseObserved,
          nonTaskMessageRenewed>>

StartEpochWork(work) ==
    /\ epochState \in {Ready, Suspect}
    /\ workState = NoWork
    /\ work \in {BoundedWork, UnboundedWork}
    /\ ~(BoundedSilenceScenario /\ work = UnboundedWork)
    /\ workState' = work
    /\ UNCHANGED
        <<epochState,
          closureCause,
          startupRemaining,
          startupRenewed,
          readyMatched,
          lifecycleActive,
          suspensionBudget,
          mainLoopContinuous,
          gapBudget,
          assigned,
          accepted,
          released,
          outcome,
          quiesced,
          silenceRemaining,
          probeOutstanding,
          probeWasSent,
          firstExpiryObserved,
          drainRemaining,
          realmDestroyed,
          sourceRevoked,
          assignedAtClosure>>
    /\ UnchangedMutationFlags

FinishEpochWork ==
    /\ workState # NoWork
    /\ workState' = NoWork
    /\ IF workState = UnboundedWork
          /\ ~(\E o \in accepted:
                  OperationAllowance(o) = UnboundedAllowance)
       THEN
           /\ silenceRemaining' = MaxSilenceBudget
       ELSE
           /\ UNCHANGED silenceRemaining
    /\ UNCHANGED
        <<epochState,
          closureCause,
          startupRemaining,
          startupRenewed,
          readyMatched,
          lifecycleActive,
          suspensionBudget,
          mainLoopContinuous,
          gapBudget,
          assigned,
          accepted,
          released,
          outcome,
          quiesced,
          probeOutstanding,
          probeWasSent,
          firstExpiryObserved,
          drainRemaining,
          realmDestroyed,
          sourceRevoked,
          assignedAtClosure>>
    /\ UnchangedMutationFlags

SettleOperation(o) ==
    /\ epochState \in {Ready, Suspect}
    /\ o \in accepted
    /\ accepted' = accepted \ {o}
    /\ released' = released \cup {o}
    /\ outcome' =
        [outcome EXCEPT
            ![o] = IF outcome[o] = NoOutcome THEN SucceededOutcome ELSE @]
    /\ quiesced' = quiesced \cup {o}
    /\ IF OperationAllowance(o) = UnboundedAllowance
          /\ workState # UnboundedWork
          /\ ~(\E p \in accepted \ {o}:
                  OperationAllowance(p) = UnboundedAllowance)
       THEN
           /\ silenceRemaining' = MaxSilenceBudget
       ELSE
           /\ UNCHANGED silenceRemaining
    /\ UNCHANGED
        <<epochState,
          closureCause,
          startupRemaining,
          startupRenewed,
          readyMatched,
          lifecycleActive,
          suspensionBudget,
          mainLoopContinuous,
          gapBudget,
          assigned,
          workState,
          probeOutstanding,
          probeWasSent,
          firstExpiryObserved,
          drainRemaining,
          realmDestroyed,
          sourceRevoked,
          assignedAtClosure>>
    /\ UnchangedMutationFlags

EnterClosure(cause) ==
    /\ epochState \in {Starting, Ready, Suspect}
    /\ cause \in {PlannedCause} \cup UnexpectedCauses
    /\ cause \in UnexpectedCauses => epochState \in {Ready, Suspect}
    /\ epochState' = Draining
    /\ closureCause' = cause
    /\ assignedAtClosure' = assigned \ released
    /\ outcome' =
        [o \in Operations |->
            IF o \in assigned \ released
            THEN
                IF cause = PlannedCause
                THEN
                    IF Mutation = PlannedAsFailure
                    THEN FailedOutcome
                    ELSE CanceledOutcome
                ELSE
                    IF Mutation = UnexpectedAsCancellation
                        \/ /\ Mutation = WorkerDeclaredAsCancellation
                           /\ cause = WorkerDeclaredCause
                    THEN CanceledOutcome
                    ELSE FailedOutcome
            ELSE outcome[o]]
    /\ plannedOutcomeMismatch' =
        (Mutation = PlannedAsFailure /\ cause = PlannedCause)
    /\ unexpectedOutcomeMismatch' =
        (Mutation = UnexpectedAsCancellation /\ cause = UnexpectedCause)
    /\ drainRemaining' = MaxDrainBudget
    /\ probeOutstanding' = FALSE
    /\ UNCHANGED
        <<startupRemaining,
          startupRenewed,
          readyMatched,
          lifecycleActive,
          suspensionBudget,
          mainLoopContinuous,
          gapBudget,
          assigned,
          accepted,
          released,
          quiesced,
          workState,
          silenceRemaining,
          probeWasSent,
          firstExpiryObserved,
          realmDestroyed,
          sourceRevoked,
          startDuringDrain,
          mismatchedReadyAccepted,
          probeSatisfiedStartup,
          watchdogWithoutProbe,
          unboundedWatchdogFailure,
          mainGapWatchdogFailure,
          quiescedBeforeRelease,
          callbackAfterReleaseObserved,
          nonTaskMessageRenewed>>

ReleaseDuringDrain(o) ==
    /\ epochState = Draining
    /\ o \in assignedAtClosure
    /\ o \notin released
    /\ released' = released \cup {o}
    /\ quiesced' = quiesced \cup {o}
    /\ accepted' = accepted \ {o}
    /\ UNCHANGED
        <<epochState,
          closureCause,
          startupRemaining,
          startupRenewed,
          readyMatched,
          lifecycleActive,
          suspensionBudget,
          mainLoopContinuous,
          gapBudget,
          assigned,
          outcome,
          workState,
          silenceRemaining,
          probeOutstanding,
          probeWasSent,
          firstExpiryObserved,
          drainRemaining,
          realmDestroyed,
          sourceRevoked,
          assignedAtClosure>>
    /\ UnchangedMutationFlags

WorkerDeclaredEpochFailure ==
    /\ epochState \in {Ready, Suspect}
    /\ EnterClosure(WorkerDeclaredCause)

QuiesceBeforeReleaseMutation(o) ==
    /\ Mutation = QuiesceBeforeRelease
    /\ o \in assigned
    /\ o \notin released
    /\ quiesced' = quiesced \cup {o}
    /\ quiescedBeforeRelease' = TRUE
    /\ UNCHANGED
        <<epochState,
          closureCause,
          startupRemaining,
          startupRenewed,
          readyMatched,
          lifecycleActive,
          suspensionBudget,
          mainLoopContinuous,
          gapBudget,
          assigned,
          accepted,
          released,
          outcome,
          workState,
          silenceRemaining,
          probeOutstanding,
          probeWasSent,
          firstExpiryObserved,
          drainRemaining,
          realmDestroyed,
          sourceRevoked,
          assignedAtClosure,
          startDuringDrain,
          mismatchedReadyAccepted,
          probeSatisfiedStartup,
          watchdogWithoutProbe,
          unboundedWatchdogFailure,
          mainGapWatchdogFailure,
          plannedOutcomeMismatch,
          unexpectedOutcomeMismatch,
          callbackAfterReleaseObserved,
          nonTaskMessageRenewed>>

DrainTick ==
    /\ epochState = Draining
    /\ lifecycleActive
    /\ mainLoopContinuous
    /\ drainRemaining > 0
    /\ drainRemaining' = drainRemaining - 1
    /\ UNCHANGED
        <<epochState,
          closureCause,
          startupRemaining,
          startupRenewed,
          readyMatched,
          lifecycleActive,
          suspensionBudget,
          mainLoopContinuous,
          gapBudget,
          assigned,
          accepted,
          released,
          outcome,
          quiesced,
          workState,
          silenceRemaining,
          probeOutstanding,
          probeWasSent,
          firstExpiryObserved,
          realmDestroyed,
          sourceRevoked,
          assignedAtClosure>>
    /\ UnchangedMutationFlags

CanDestroyRealm ==
    /\ epochState = Draining
    /\ \/ drainRemaining = 0
       \/ /\ assignedAtClosure \subseteq released
          /\ workState = NoWork

DestroyRealm ==
    /\ CanDestroyRealm
    /\ Mutation # DrainNeverCloses
    /\ epochState' = Closed
    /\ realmDestroyed' = TRUE
    /\ sourceRevoked' = TRUE
    /\ released' = released \cup assignedAtClosure
    /\ quiesced' = quiesced \cup assignedAtClosure
    /\ accepted' = {}
    /\ workState' = NoWork
    /\ probeOutstanding' = FALSE
    /\ UNCHANGED
        <<closureCause,
          startupRemaining,
          startupRenewed,
          readyMatched,
          lifecycleActive,
          suspensionBudget,
          mainLoopContinuous,
          gapBudget,
          assigned,
          outcome,
          silenceRemaining,
          probeWasSent,
          firstExpiryObserved,
          drainRemaining,
          assignedAtClosure>>
    /\ UnchangedMutationFlags

WorkerCrash ==
    /\ epochState \in {Starting, Ready, Suspect}
    /\ epochState' = Closed
    /\ closureCause' = UnexpectedCause
    /\ assignedAtClosure' = assigned \ released
    /\ outcome' =
        [o \in Operations |->
            IF o \in assigned \ released
            THEN FailedOutcome
            ELSE outcome[o]]
    /\ released' = released \cup (assigned \ released)
    /\ quiesced' = quiesced \cup (assigned \ released)
    /\ accepted' = {}
    /\ workState' = NoWork
    /\ realmDestroyed' = TRUE
    /\ sourceRevoked' = TRUE
    /\ probeOutstanding' = FALSE
    /\ UNCHANGED
        <<startupRemaining,
          startupRenewed,
          readyMatched,
          lifecycleActive,
          suspensionBudget,
          mainLoopContinuous,
          gapBudget,
          assigned,
          silenceRemaining,
          probeWasSent,
          firstExpiryObserved,
          drainRemaining>>
    /\ UnchangedMutationFlags

CallbackAfterReleaseMutation ==
    /\ Mutation = CallbackAfterRelease
    /\ epochState = Closed
    /\ callbackAfterReleaseObserved' = TRUE
    /\ UNCHANGED
        <<epochState,
          closureCause,
          startupRemaining,
          startupRenewed,
          readyMatched,
          lifecycleActive,
          suspensionBudget,
          mainLoopContinuous,
          gapBudget,
          assigned,
          accepted,
          released,
          outcome,
          quiesced,
          workState,
          silenceRemaining,
          probeOutstanding,
          probeWasSent,
          firstExpiryObserved,
          drainRemaining,
          realmDestroyed,
          sourceRevoked,
          assignedAtClosure,
          startDuringDrain,
          mismatchedReadyAccepted,
          probeSatisfiedStartup,
          watchdogWithoutProbe,
          unboundedWatchdogFailure,
          mainGapWatchdogFailure,
          plannedOutcomeMismatch,
          unexpectedOutcomeMismatch,
          quiescedBeforeRelease,
          nonTaskMessageRenewed>>

Next ==
    \/ \E o \in Operations: AssignOperation(o)
    \/ \E o \in Operations: AcceptOperation(o)
    \/ \E o \in Operations: AcceptDuringDrainMutation(o)
    \/ StartupTick
    \/ StartupMessage
    \/ StartupProbeAcknowledged
    \/ ReceiveReady
    \/ AcceptReadyAfterStartupExpiryMutation
    \/ ReceiveMismatchedReady
    \/ AcceptMismatchedReadyMutation
    \/ StartupExpires
    \/ ReceiveBootstrapFailure
    \/ BootstrapFailureDrainsMutation
    \/ SuspendLifecycle
    \/ ResumeLifecycle
    \/ DetectMainLoopGap
    \/ ResumeMainLoop
    \/ TaskLoopEvidence
    \/ ProbeAcknowledgmentEvidence
    \/ NonTaskMessage
    \/ AllowanceChurnRenewalMutation
    \/ SilenceTick
    \/ FirstSilenceExpiry
    \/ SecondSilenceExpiry
    \/ TerminateWhileUnboundedMutation
    \/ TerminateAcrossMainGapMutation
    \/ \E work \in {BoundedWork, UnboundedWork}: StartEpochWork(work)
    \/ FinishEpochWork
    \/ \E o \in Operations: SettleOperation(o)
    \/ EnterClosure(PlannedCause)
    \/ EnterClosure(UnexpectedCause)
    \/ WorkerDeclaredEpochFailure
    \/ \E o \in Operations: ReleaseDuringDrain(o)
    \/ \E o \in Operations: QuiesceBeforeReleaseMutation(o)
    \/ DrainTick
    \/ DestroyRealm
    \/ WorkerCrash
    \/ CallbackAfterReleaseMutation

Spec ==
    /\ Init
    /\ [][Next]_vars
    /\ WF_vars(ResumeLifecycle)
    /\ WF_vars(ResumeMainLoop)
    /\ WF_vars(StartupTick)
    /\ WF_vars(StartupExpires)
    /\ WF_vars(SilenceTick)
    /\ WF_vars(FirstSilenceExpiry)
    /\ WF_vars(SecondSilenceExpiry)
    /\ WF_vars(DrainTick)
    /\ WF_vars(DestroyRealm)

TypeOK ==
    /\ epochState \in EpochStates
    /\ closureCause \in ClosureCauses
    /\ startupRemaining \in 0..MaxStartupBudget
    /\ startupRenewed \in BOOLEAN
    /\ readyMatched \in BOOLEAN
    /\ lifecycleActive \in BOOLEAN
    /\ suspensionBudget \in 0..1
    /\ mainLoopContinuous \in BOOLEAN
    /\ gapBudget \in 0..1
    /\ assigned \subseteq Operations
    /\ accepted \subseteq Operations
    /\ released \subseteq Operations
    /\ outcome \in [Operations -> Outcomes]
    /\ quiesced \subseteq Operations
    /\ workState \in WorkStates
    /\ silenceRemaining \in 0..MaxSilenceBudget
    /\ probeOutstanding \in BOOLEAN
    /\ probeWasSent \in BOOLEAN
    /\ firstExpiryObserved \in BOOLEAN
    /\ drainRemaining \in 0..MaxDrainBudget
    /\ realmDestroyed \in BOOLEAN
    /\ sourceRevoked \in BOOLEAN
    /\ assignedAtClosure \subseteq Operations
    /\ startDuringDrain \in BOOLEAN
    /\ mismatchedReadyAccepted \in BOOLEAN
    /\ probeSatisfiedStartup \in BOOLEAN
    /\ watchdogWithoutProbe \in BOOLEAN
    /\ unboundedWatchdogFailure \in BOOLEAN
    /\ mainGapWatchdogFailure \in BOOLEAN
    /\ plannedOutcomeMismatch \in BOOLEAN
    /\ unexpectedOutcomeMismatch \in BOOLEAN
    /\ quiescedBeforeRelease \in BOOLEAN
    /\ callbackAfterReleaseObserved \in BOOLEAN
    /\ nonTaskMessageRenewed \in BOOLEAN

StartupBudgetDoesNotRenew ==
    ~startupRenewed

ReadyRequiresUnexpiredStartup ==
    epochState \in {Ready, Suspect} => startupRemaining > 0

MatchingReadyIsRequired ==
    epochState \in {Ready, Suspect} => readyMatched

ProbeCannotSatisfyStartup ==
    ~probeSatisfiedStartup

MismatchedReadyCannotOpenEpoch ==
    ~mismatchedReadyAccepted

DrainingRefusesAssignments ==
    ~startDuringDrain

NonTaskMessagesDoNotRenewWatchdog ==
    ~nonTaskMessageRenewed

FirstWatchdogExpiryOnlyProbes ==
    ~watchdogWithoutProbe

SuspectRequiresIssuedProbe ==
    epochState = Suspect
    =>
    /\ firstExpiryObserved
    /\ probeWasSent
    /\ probeOutstanding

UnboundedSilenceCannotFailWatchdog ==
    ~unboundedWatchdogFailure

MainLoopGapCannotFailWatchdog ==
    ~mainGapWatchdogFailure

PlannedRestartCancelsPendingOperations ==
    ~plannedOutcomeMismatch

UnexpectedLossFailsPendingOperations ==
    ~unexpectedOutcomeMismatch

ClosureCauseDeterminesOutcome ==
    /\ closureCause = PlannedCause
       =>
       \A o \in assignedAtClosure: outcome[o] = CanceledOutcome
    /\ closureCause \in UnexpectedCauses
       =>
       \A o \in assignedAtClosure: outcome[o] = FailedOutcome

StartupFailureClosesImmediately ==
    closureCause = StartupFailureCause
    =>
    /\ epochState = Closed
    /\ realmDestroyed
    /\ sourceRevoked

FailedDrainingRequiresReadiness ==
    epochState = Draining /\ closureCause \in UnexpectedCauses
    => readyMatched

QuiescenceRequiresPhysicalRelease ==
    /\ ~quiescedBeforeRelease
    /\ quiesced \subseteq released

RealmReleaseRevokesSource ==
    realmDestroyed => sourceRevoked

NoCallbackAfterRealmRelease ==
    ~callbackAfterReleaseObserved

ClosedEpochHasNoLiveResources ==
    epochState = Closed
    =>
    /\ realmDestroyed
    /\ accepted = {}
    /\ workState = NoWork
    /\ assignedAtClosure \subseteq released
    /\ assignedAtClosure \subseteq quiesced

StartingEventuallyLeaves ==
    epochState = Starting ~> epochState # Starting

DrainingEventuallyCloses ==
    epochState = Draining ~> epochState = Closed

ContinuousBoundedSilenceEventuallyDrains ==
    /\ BoundedSilenceScenario
    /\ epochState = Ready
    ~> epochState \in {Draining, Closed}

=============================================================================
