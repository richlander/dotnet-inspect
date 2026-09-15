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
PreReadyProtocolCause == "PreReadyProtocolCause"
PreReadyWorkerMessageCause == "PreReadyWorkerMessageCause"
UnexpectedCauses ==
    {UnexpectedCause,
     StartupFailureCause,
     WorkerDeclaredCause,
     PreReadyProtocolCause,
     PreReadyWorkerMessageCause}
ClosureCauses == {NoCause, PlannedCause} \cup UnexpectedCauses

NoPreReadyFault == "NoPreReadyFault"
PreReadyProtocolFault == "PreReadyProtocolFault"
PreReadyWorkerMessageFault == "PreReadyWorkerMessageFault"
PreReadyFaultKinds ==
    {NoPreReadyFault, PreReadyProtocolFault, PreReadyWorkerMessageFault}

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
PreReadyProtocolFailureDrains == "PreReadyProtocolFailureDrains"
PreReadyProtocolAsStartup == "PreReadyProtocolAsStartup"
PreReadyWorkerMessageAsProtocol == "PreReadyWorkerMessageAsProtocol"
IgnorePreReadyHeartbeat == "IgnorePreReadyHeartbeat"
IgnorePreReadyProbeAck == "IgnorePreReadyProbeAck"
PreReadyHeartbeatAsWorkerMessage == "PreReadyHeartbeatAsWorkerMessage"
ReplaceCauseDuringDrain == "ReplaceCauseDuringDrain"
RewriteOutcomeDuringDrain == "RewriteOutcomeDuringDrain"
CrashDuringDrainWaits == "CrashDuringDrainWaits"
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
     AcceptReadyAfterStartupExpiry,
     PreReadyProtocolFailureDrains,
     PreReadyProtocolAsStartup,
     PreReadyWorkerMessageAsProtocol,
     IgnorePreReadyHeartbeat,
     IgnorePreReadyProbeAck,
     PreReadyHeartbeatAsWorkerMessage,
     ReplaceCauseDuringDrain,
     RewriteOutcomeDuringDrain,
     CrashDuringDrainWaits}

FaultDuringDrainEvent == "FaultDuringDrainEvent"
CrashDuringDrainEvent == "CrashDuringDrainEvent"
DrainEvents == {FaultDuringDrainEvent, CrashDuringDrainEvent}

NoInvalidPreReadyInput == "NoInvalidPreReadyInput"
UnexpectedPreReadyHeartbeatInput == "UnexpectedPreReadyHeartbeatInput"
UnexpectedProbeAcknowledgmentInput == "UnexpectedProbeAcknowledgmentInput"
PreReadyInputKinds ==
    {NoInvalidPreReadyInput,
     UnexpectedPreReadyHeartbeatInput,
     UnexpectedProbeAcknowledgmentInput}

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
    preReadyFaultKind,
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
    callbackDelivered,
    nonTaskMessageRenewed,
    preReadyInvalidInputKind,
    drainEventsSeen

vars ==
    <<epochState,
      closureCause,
      preReadyFaultKind,
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
      callbackDelivered,
      nonTaskMessageRenewed,
      preReadyInvalidInputKind,
      drainEventsSeen>>

Init ==
    /\ epochState = Starting
    /\ closureCause = NoCause
    /\ preReadyFaultKind = NoPreReadyFault
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
    /\ callbackDelivered = FALSE
    /\ nonTaskMessageRenewed = FALSE
    /\ preReadyInvalidInputKind = NoInvalidPreReadyInput
    /\ drainEventsSeen = {}

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
          callbackDelivered,
          nonTaskMessageRenewed,
          preReadyFaultKind,
          preReadyInvalidInputKind,
          drainEventsSeen>>

UnchangedMutationFlagsExceptPreReadyInput ==
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
          callbackDelivered,
          nonTaskMessageRenewed,
          preReadyFaultKind,
          drainEventsSeen>>

UnchangedMutationFlagsExceptPreReadyInputAndProbe ==
    UNCHANGED
        <<startDuringDrain,
          mismatchedReadyAccepted,
          watchdogWithoutProbe,
          unboundedWatchdogFailure,
          mainGapWatchdogFailure,
          plannedOutcomeMismatch,
          unexpectedOutcomeMismatch,
          quiescedBeforeRelease,
          callbackAfterReleaseObserved,
          callbackDelivered,
          nonTaskMessageRenewed,
          preReadyFaultKind,
          drainEventsSeen>>

UnchangedMutationFlagsExceptDrainEvent ==
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
          callbackDelivered,
          nonTaskMessageRenewed,
          preReadyFaultKind,
          preReadyInvalidInputKind>>

UnchangedMutationFlagsExceptCallback ==
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
          nonTaskMessageRenewed,
          preReadyFaultKind,
          preReadyInvalidInputKind,
          drainEventsSeen>>

AssignOperation(o) ==
    /\ epochState \in {Starting, Ready, Suspect}
    /\ o \notin assigned
    /\ assigned' = assigned \cup {o}
    /\ UNCHANGED
        <<epochState,
          closureCause,
          preReadyFaultKind,
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
          preReadyFaultKind,
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
          preReadyFaultKind,
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
          callbackDelivered,
          nonTaskMessageRenewed,
          preReadyInvalidInputKind,
          drainEventsSeen>>

StartupTick ==
    /\ epochState = Starting
    /\ lifecycleActive
    /\ mainLoopContinuous
    /\ startupRemaining > 0
    /\ startupRemaining' = startupRemaining - 1
    /\ UNCHANGED
        <<epochState,
          closureCause,
          preReadyFaultKind,
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

ClosePartialRealm(cause, faultKind, invalidInputKind) ==
    /\ epochState = Starting
    /\ cause \in
        {StartupFailureCause,
         PreReadyProtocolCause,
         PreReadyWorkerMessageCause}
    /\ faultKind \in PreReadyFaultKinds
    /\ epochState' = Closed
    /\ closureCause' = cause
    /\ preReadyFaultKind' = faultKind
    /\ preReadyInvalidInputKind' = invalidInputKind
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
    /\ UNCHANGED
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
          callbackDelivered,
          nonTaskMessageRenewed,
          drainEventsSeen>>

IgnoreUnexpectedPreReadyInput(inputKind) ==
    /\ epochState = Starting
    /\ inputKind \in
        {UnexpectedPreReadyHeartbeatInput,
         UnexpectedProbeAcknowledgmentInput}
    /\ preReadyInvalidInputKind' = inputKind
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
          assignedAtClosure>>
    /\ UnchangedMutationFlagsExceptPreReadyInput

StartupHeartbeat ==
    /\ epochState = Starting
    /\ IF Mutation = RenewStartupFromMessage
       THEN
           /\ startupRemaining' = MaxStartupBudget
           /\ startupRenewed' = TRUE
           /\ preReadyInvalidInputKind' = UnexpectedPreReadyHeartbeatInput
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
           /\ UnchangedMutationFlagsExceptPreReadyInput
       ELSE
           IF Mutation = IgnorePreReadyHeartbeat
           THEN
               IgnoreUnexpectedPreReadyInput(
                   UnexpectedPreReadyHeartbeatInput)
           ELSE
               ClosePartialRealm(
                   IF Mutation = PreReadyHeartbeatAsWorkerMessage
                   THEN PreReadyWorkerMessageCause
                   ELSE PreReadyProtocolCause,
                   IF Mutation = PreReadyHeartbeatAsWorkerMessage
                   THEN PreReadyWorkerMessageFault
                   ELSE PreReadyProtocolFault,
                   UnexpectedPreReadyHeartbeatInput)

StartupProbeAcknowledged ==
    /\ epochState = Starting
    /\ IF Mutation = ProbeSatisfiesStartup
       THEN
           /\ epochState' = Ready
           /\ readyMatched' = FALSE
           /\ probeSatisfiedStartup' = TRUE
           /\ preReadyInvalidInputKind' = UnexpectedProbeAcknowledgmentInput
           /\ UNCHANGED
               <<closureCause,
                 preReadyFaultKind,
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
                 assignedAtClosure>>
           /\ UnchangedMutationFlagsExceptPreReadyInputAndProbe
       ELSE
           IF Mutation = IgnorePreReadyProbeAck
           THEN
               IgnoreUnexpectedPreReadyInput(
                   UnexpectedProbeAcknowledgmentInput)
           ELSE
               ClosePartialRealm(
                   PreReadyProtocolCause,
                   PreReadyProtocolFault,
                   UnexpectedProbeAcknowledgmentInput)

ReceiveReady ==
    /\ epochState = Starting
    /\ startupRemaining > 0
    /\ epochState' = Ready
    /\ readyMatched' = TRUE
    /\ silenceRemaining' = MaxSilenceBudget
    /\ UNCHANGED
        <<closureCause,
          preReadyFaultKind,
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
          preReadyFaultKind,
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

ReceiveMismatchedReady ==
    /\ Mutation # AcceptMismatchedReady
    /\ ClosePartialRealm(
        StartupFailureCause,
        NoPreReadyFault,
        NoInvalidPreReadyInput)

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
          preReadyFaultKind,
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
          callbackDelivered,
          nonTaskMessageRenewed,
          preReadyInvalidInputKind,
          drainEventsSeen>>

StartupExpires ==
    /\ startupRemaining = 0
    /\ ClosePartialRealm(
        StartupFailureCause,
        NoPreReadyFault,
        NoInvalidPreReadyInput)

ReceiveBootstrapFailure ==
    /\ Mutation # BootstrapFailureDrains
    /\ ClosePartialRealm(
        StartupFailureCause,
        NoPreReadyFault,
        NoInvalidPreReadyInput)

ReceivePreReadyProtocolFailure ==
    /\ Mutation # PreReadyProtocolFailureDrains
    /\ ClosePartialRealm(
        IF Mutation = PreReadyProtocolAsStartup
        THEN StartupFailureCause
        ELSE PreReadyProtocolCause,
        PreReadyProtocolFault,
        NoInvalidPreReadyInput)

ReceivePreReadyWorkerMessageFailure ==
    /\ ClosePartialRealm(
        IF Mutation = PreReadyWorkerMessageAsProtocol
        THEN PreReadyProtocolCause
        ELSE PreReadyWorkerMessageCause,
        PreReadyWorkerMessageFault,
        NoInvalidPreReadyInput)

PreReadyProtocolFailureDrainsMutation ==
    /\ Mutation = PreReadyProtocolFailureDrains
    /\ epochState = Starting
    /\ epochState' = Draining
    /\ closureCause' = PreReadyProtocolCause
    /\ preReadyFaultKind' = PreReadyProtocolFault
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
    /\ UNCHANGED
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
          callbackDelivered,
          nonTaskMessageRenewed,
          preReadyInvalidInputKind,
          drainEventsSeen>>

BootstrapFailureDrainsMutation ==
    /\ Mutation = BootstrapFailureDrains
    /\ epochState = Starting
    /\ epochState' = Draining
    /\ closureCause' = StartupFailureCause
    /\ preReadyFaultKind' = NoPreReadyFault
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
          preReadyFaultKind,
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
          preReadyFaultKind,
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
          preReadyFaultKind,
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
          preReadyFaultKind,
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
          preReadyFaultKind,
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
          preReadyFaultKind,
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
          preReadyFaultKind,
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
          callbackAfterReleaseObserved,
          callbackDelivered,
          preReadyInvalidInputKind,
          drainEventsSeen>>

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
          preReadyFaultKind,
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
          nonTaskMessageRenewed,
          preReadyFaultKind,
          callbackDelivered,
          preReadyInvalidInputKind,
          drainEventsSeen>>

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
          preReadyFaultKind,
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
          callbackDelivered,
          nonTaskMessageRenewed,
          preReadyInvalidInputKind,
          drainEventsSeen>>

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
          nonTaskMessageRenewed,
          preReadyFaultKind,
          callbackDelivered,
          preReadyInvalidInputKind,
          drainEventsSeen>>

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
          nonTaskMessageRenewed,
          preReadyFaultKind,
          callbackDelivered,
          preReadyInvalidInputKind,
          drainEventsSeen>>

StartEpochWork(work) ==
    /\ epochState \in {Ready, Suspect}
    /\ workState = NoWork
    /\ work \in {BoundedWork, UnboundedWork}
    /\ ~(BoundedSilenceScenario /\ work = UnboundedWork)
    /\ workState' = work
    /\ UNCHANGED
        <<epochState,
          closureCause,
          preReadyFaultKind,
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
          preReadyFaultKind,
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
          preReadyFaultKind,
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
          nonTaskMessageRenewed,
          preReadyFaultKind,
          callbackDelivered,
          preReadyInvalidInputKind,
          drainEventsSeen>>

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
          preReadyFaultKind,
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
          preReadyFaultKind,
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
          callbackDelivered,
          nonTaskMessageRenewed,
          preReadyInvalidInputKind,
          drainEventsSeen>>

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
          preReadyFaultKind,
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

FaultDuringDrain ==
    /\ epochState = Draining
    /\ FaultDuringDrainEvent \notin drainEventsSeen
    /\ drainEventsSeen' = drainEventsSeen \cup {FaultDuringDrainEvent}
    /\ closureCause' =
        IF Mutation = ReplaceCauseDuringDrain
        THEN
            IF closureCause = PlannedCause
            THEN UnexpectedCause
            ELSE PlannedCause
        ELSE closureCause
    /\ IF Mutation = RewriteOutcomeDuringDrain
       THEN
           /\ OperationA \in assignedAtClosure
           /\ outcome' =
               [outcome EXCEPT
                   ![OperationA] =
                       IF @ = FailedOutcome
                       THEN CanceledOutcome
                       ELSE FailedOutcome]
       ELSE
           /\ UNCHANGED outcome
    /\ UNCHANGED
        <<epochState,
          preReadyFaultKind,
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
    /\ UnchangedMutationFlagsExceptDrainEvent

WorkerCrashDuringDrain ==
    /\ epochState = Draining
    /\ CrashDuringDrainEvent \notin drainEventsSeen
    /\ drainEventsSeen' = drainEventsSeen \cup {CrashDuringDrainEvent}
    /\ IF Mutation = CrashDuringDrainWaits
       THEN
           /\ UNCHANGED
               <<epochState,
                 accepted,
                 released,
                 quiesced,
                 workState,
                 probeOutstanding,
                 realmDestroyed,
                 sourceRevoked>>
       ELSE
           /\ epochState' = Closed
           /\ accepted' = {}
           /\ released' = released \cup assignedAtClosure
           /\ quiesced' = quiesced \cup assignedAtClosure
           /\ workState' = NoWork
           /\ probeOutstanding' = FALSE
           /\ realmDestroyed' = TRUE
           /\ sourceRevoked' = TRUE
    /\ UNCHANGED
        <<closureCause,
          preReadyFaultKind,
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
    /\ UnchangedMutationFlagsExceptDrainEvent

DeliverCallback ==
    /\ ~callbackDelivered
    /\ IF Mutation = CallbackAfterRelease
       THEN epochState \in {Ready, Suspect, Draining, Closed}
       ELSE
           /\ epochState \in {Ready, Suspect, Draining}
           /\ ~realmDestroyed
           /\ ~sourceRevoked
    /\ callbackDelivered' = TRUE
    /\ callbackAfterReleaseObserved' = (realmDestroyed \/ sourceRevoked)
    /\ UNCHANGED
        <<epochState,
          closureCause,
          preReadyFaultKind,
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
          assignedAtClosure>>
    /\ UnchangedMutationFlagsExceptCallback

Next ==
    \/ \E o \in Operations: AssignOperation(o)
    \/ \E o \in Operations: AcceptOperation(o)
    \/ \E o \in Operations: AcceptDuringDrainMutation(o)
    \/ StartupTick
    \/ StartupHeartbeat
    \/ StartupProbeAcknowledged
    \/ ReceiveReady
    \/ AcceptReadyAfterStartupExpiryMutation
    \/ ReceiveMismatchedReady
    \/ AcceptMismatchedReadyMutation
    \/ StartupExpires
    \/ ReceiveBootstrapFailure
    \/ ReceivePreReadyProtocolFailure
    \/ ReceivePreReadyWorkerMessageFailure
    \/ PreReadyProtocolFailureDrainsMutation
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
    \/ FaultDuringDrain
    \/ WorkerCrashDuringDrain
    \/ DeliverCallback

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
    /\ preReadyFaultKind \in PreReadyFaultKinds
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
    /\ callbackDelivered \in BOOLEAN
    /\ nonTaskMessageRenewed \in BOOLEAN
    /\ preReadyInvalidInputKind \in PreReadyInputKinds
    /\ drainEventsSeen \subseteq DrainEvents

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

ClosureCauseIsStable ==
    [][closureCause # NoCause => closureCause' = closureCause]_vars

CommittedOutcomesAreStable ==
    [][closureCause # NoCause => outcome' = outcome]_vars

StartupFailureClosesImmediately ==
    closureCause = StartupFailureCause
    =>
    /\ epochState = Closed
    /\ realmDestroyed
    /\ sourceRevoked

PreReadyProtocolFailureClosesImmediately ==
    closureCause \in {PreReadyProtocolCause, PreReadyWorkerMessageCause}
    =>
    /\ epochState = Closed
    /\ realmDestroyed
    /\ sourceRevoked

PreReadyFaultClassificationIsPreserved ==
    /\ preReadyFaultKind = PreReadyProtocolFault
       => closureCause = PreReadyProtocolCause
    /\ preReadyFaultKind = PreReadyWorkerMessageFault
       => closureCause = PreReadyWorkerMessageCause

UnexpectedPreReadyInputClosesImmediately ==
    preReadyInvalidInputKind # NoInvalidPreReadyInput
    =>
    /\ epochState = Closed
    /\ realmDestroyed
    /\ sourceRevoked

PreReadyInvalidInputClassificationIsExact ==
    /\ preReadyInvalidInputKind = UnexpectedPreReadyHeartbeatInput
       =>
       /\ preReadyFaultKind = PreReadyProtocolFault
       /\ closureCause = PreReadyProtocolCause
    /\ preReadyInvalidInputKind = UnexpectedProbeAcknowledgmentInput
       =>
       /\ preReadyFaultKind = PreReadyProtocolFault
       /\ closureCause = PreReadyProtocolCause

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

CallbackDeliveryRequiresLiveAuthority ==
    [][(/\ ~callbackDelivered
         /\ callbackDelivered')
       =>
       /\ ~realmDestroyed
       /\ ~sourceRevoked]_vars

CallbackDeliveryIsReachable ==
    ~callbackDelivered

CrashDuringDrainClosesImmediately ==
    CrashDuringDrainEvent \in drainEventsSeen
    =>
    /\ epochState = Closed
    /\ realmDestroyed
    /\ sourceRevoked

FaultThenCrashIsReachable ==
    ~(FaultDuringDrainEvent \in drainEventsSeen
      /\ CrashDuringDrainEvent \in drainEventsSeen)

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
