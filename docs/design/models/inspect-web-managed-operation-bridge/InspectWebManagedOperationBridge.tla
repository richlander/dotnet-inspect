--------------- MODULE InspectWebManagedOperationBridge ---------------
(***************************************************************************)
(* Abstract lifecycle of the managed operation bridge owned by             *)
(* docs/design/inspect-web-managed-operation-bridge.md.                    *)
(*                                                                         *)
(* The model covers two worker-issued operation IDs admitted to one        *)
(* dynamic active-operation table, their cancellation, progress-callback,  *)
(* settlement, release, and quiescence sequence, and one feature-owned     *)
(* shared physical producer that both operations attach to as waiters.     *)
(*                                                                         *)
(* Deliberately outside the model: main-thread publication authority, DOM  *)
(* rendering, worker epochs, restart, watchdogs, boundary message          *)
(* validation, facade generation, feature-specific phases, cache policy,   *)
(* and shared-producer key semantics.                                      *)
(***************************************************************************)
EXTENDS FiniteSets, Naturals

CONSTANTS
    OperationA,
    OperationB,
    ReasonUser,
    ReasonSupersede,
    NoReason,
    MaxProgress,
    MaxCancelRequests,
    MaxWorkSequence,
    Mutation

Operations == {OperationA, OperationB}
Reasons == {ReasonUser, ReasonSupersede}

(***************************************************************************)
(* Wrapper phases.  The release sequence is ordered: settlement seals the   *)
(* entry, classification follows the callout drain, the progress callback   *)
(* closes, the shared subscription detaches, the exact entry leaves the     *)
(* table, and only then does the exported Task quiesce.                     *)
(***************************************************************************)
NotAdmitted == "NotAdmitted"
Active == "Active"
Settling == "Settling"
Classified == "Classified"
CallbackClosed == "CallbackClosed"
Detached == "Detached"
Removed == "Removed"
Quiesced == "Quiesced"
Phases ==
    {NotAdmitted,
     Active,
     Settling,
     Classified,
     CallbackClosed,
     Detached,
     Removed,
     Quiesced}
AfterSettlementBegins ==
    {Settling, Classified, CallbackClosed, Detached, Removed, Quiesced}
AfterCallbackCloses == {CallbackClosed, Detached, Removed, Quiesced}

(***************************************************************************)
(* Feature-body observations, abstracted from concrete values and errors.   *)
(***************************************************************************)
NoBodyResult == "NoBodyResult"
BodyValue == "BodyValue"
BodyCanceled == "BodyCanceled"
BodyExpectedFailure == "BodyExpectedFailure"
BodyUnexpectedFailure == "BodyUnexpectedFailure"
BodyResults ==
    {NoBodyResult,
     BodyValue,
     BodyCanceled,
     BodyExpectedFailure,
     BodyUnexpectedFailure}

(***************************************************************************)
(* Typed terminal outcomes produced by the single classification step.      *)
(***************************************************************************)
NoOutcome == "NoOutcome"
SucceededOutcome == "SucceededOutcome"
CanceledOutcome == "CanceledOutcome"
FailedExpectedOutcome == "FailedExpectedOutcome"
FailedUnexpectedOutcome == "FailedUnexpectedOutcome"
Outcomes ==
    {NoOutcome,
     SucceededOutcome,
     CanceledOutcome,
     FailedExpectedOutcome,
     FailedUnexpectedOutcome}

(***************************************************************************)
(* Cancellation-request results returned across the boundary.               *)
(***************************************************************************)
NoCancelResult == "NoCancelResult"
CancelApplied == "CancelApplied"
CancelAlreadyRequested == "CancelAlreadyRequested"
CancelNotActive == "CancelNotActive"
CancelResults ==
    {NoCancelResult, CancelApplied, CancelAlreadyRequested, CancelNotActive}

(***************************************************************************)
(* Entry-scoped callout leases.  At most one callout is in flight per entry *)
(* in this model; that is enough to expose drain ordering.                  *)
(***************************************************************************)
NoCallout == "NoCallout"
ProgressCallout == "ProgressCallout"
CancelCallout == "CancelCallout"
CalloutKinds == {NoCallout, ProgressCallout, CancelCallout}

(***************************************************************************)
(* Shared-producer subscription and producer disposition.                   *)
(***************************************************************************)
SubNone == "SubNone"
SubAttached == "SubAttached"
SubDetached == "SubDetached"
Subscriptions == {SubNone, SubAttached, SubDetached}

ProducerIdle == "ProducerIdle"
ProducerRunning == "ProducerRunning"
ProducerSucceeded == "ProducerSucceeded"
ProducerFailed == "ProducerFailed"
ProducerStopped == "ProducerStopped"
ProducerFaulted == "ProducerFaulted"
ProducerFinalized == "ProducerFinalized"
ProducerStates ==
    {ProducerIdle,
     ProducerRunning,
     ProducerSucceeded,
     ProducerFailed,
     ProducerStopped,
     ProducerFaulted,
     ProducerFinalized}
ProducerSettled ==
    {ProducerSucceeded, ProducerFailed, ProducerStopped, ProducerFaulted}

(***************************************************************************)
(* Work-sequence allocation.  Zero is the "no lease" sentinel; allocated    *)
(* sequences begin at one.                                                  *)
(***************************************************************************)
NoLease == 0

NoExhaustion == "NoExhaustion"
ExhaustionVisible == "ExhaustionVisible"
ExhaustionHidden == "ExhaustionHidden"
ExhaustionStates == {NoExhaustion, ExhaustionVisible, ExhaustionHidden}

DupNone == "DupNone"
DupRejected == "DupRejected"
DupInstalled == "DupInstalled"
DuplicateAdmissionStates == {DupNone, DupRejected, DupInstalled}

(***************************************************************************)
(* Mutations.  "None" is the faithful model; every other value injects one  *)
(* deliberate defect so a targeted configuration produces a counterexample. *)
(***************************************************************************)
NoMutation == "None"
BodyBeforeRegistration == "BodyBeforeRegistration"
DuplicateAdmission == "DuplicateAdmission"
ReasonOverwrite == "ReasonOverwrite"
SettlingAcceptsCancel == "SettlingAcceptsCancel"
ClassifyBeforeDrain == "ClassifyBeforeDrain"
ProgressAfterSeal == "ProgressAfterSeal"
ReleaseLeaseBeforeRecordingFailure == "ReleaseLeaseBeforeRecordingFailure"
NonAtomicFinalDetach == "NonAtomicFinalDetach"
CallbackAfterClose == "CallbackAfterClose"
RemoveBeforeCallbackClose == "RemoveBeforeCallbackClose"
QuiesceBeforeRelease == "QuiesceBeforeRelease"
FirstWaiterStopsProducer == "FirstWaiterStopsProducer"
MissingEpochLease == "MissingEpochLease"
DuplicateWorkFinish == "DuplicateWorkFinish"
WorkSequenceReuse == "WorkSequenceReuse"
SilentExhaustion == "SilentExhaustion"
Mutations ==
    {NoMutation,
     BodyBeforeRegistration,
     DuplicateAdmission,
     ReasonOverwrite,
     SettlingAcceptsCancel,
     ClassifyBeforeDrain,
     ProgressAfterSeal,
     ReleaseLeaseBeforeRecordingFailure,
     NonAtomicFinalDetach,
     CallbackAfterClose,
     RemoveBeforeCallbackClose,
     QuiesceBeforeRelease,
     FirstWaiterStopsProducer,
     MissingEpochLease,
     DuplicateWorkFinish,
     WorkSequenceReuse,
     SilentExhaustion}

ASSUME
    /\ Cardinality(Operations) = 2
    /\ OperationA # OperationB
    /\ Cardinality(Reasons) = 2
    /\ ReasonUser # ReasonSupersede
    /\ NoReason \notin Reasons
    /\ MaxProgress \in Nat
    /\ MaxCancelRequests \in Nat
    /\ MaxWorkSequence \in Nat
    /\ Mutation \in Mutations

VARIABLES
    phase,
    activeEntries,
    bodyStarted,
    bodyResult,
    duplicateAdmission,
    cancelReason,
    firstAcceptedReason,
    cancelSignalCount,
    cancelRequestBudget,
    lastCancelResult,
    cancelResultFaithful,
    calloutKind,
    calloutFailure,
    boundaryFailure,
    pendingCalloutFailure,
    callbackOpen,
    progressBudget,
    postCloseAttempted,
    callbackAfterCloseObserved,
    terminalOutcome,
    terminalReason,
    terminalCount,
    quiesceCount,
    subscription,
    producerState,
    producerLease,
    leaseFinishCount,
    nextWorkSequence,
    allocatedSequences,
    allocationCount,
    exhaustionState,
    pendingLeaseInstall

entryVars ==
    <<phase, activeEntries, bodyStarted, bodyResult, duplicateAdmission>>
cancelVars ==
    <<cancelReason,
      firstAcceptedReason,
      cancelSignalCount,
      cancelRequestBudget,
      lastCancelResult,
      cancelResultFaithful>>
calloutVars ==
    <<calloutKind, calloutFailure, boundaryFailure, pendingCalloutFailure>>
progressVars ==
    <<callbackOpen,
      progressBudget,
      postCloseAttempted,
      callbackAfterCloseObserved>>
terminalVars ==
    <<terminalOutcome, terminalReason, terminalCount, quiesceCount>>
producerVars == <<subscription, producerState>>
leaseVars ==
    <<producerLease,
      leaseFinishCount,
      nextWorkSequence,
      allocatedSequences,
      allocationCount,
      exhaustionState,
      pendingLeaseInstall>>

vars ==
    <<entryVars,
      cancelVars,
      calloutVars,
      progressVars,
      terminalVars,
      producerVars,
      leaseVars>>

WaitersOf(sub) == {op \in Operations : sub[op] = SubAttached}
Waiters == WaitersOf(subscription)

Init ==
    /\ phase = [op \in Operations |-> NotAdmitted]
    /\ activeEntries = [op \in Operations |-> 0]
    /\ bodyStarted = [op \in Operations |-> FALSE]
    /\ bodyResult = [op \in Operations |-> NoBodyResult]
    /\ duplicateAdmission = DupNone
    /\ cancelReason = [op \in Operations |-> NoReason]
    /\ firstAcceptedReason = [op \in Operations |-> NoReason]
    /\ cancelSignalCount = [op \in Operations |-> 0]
    /\ cancelRequestBudget = MaxCancelRequests
    /\ lastCancelResult = NoCancelResult
    /\ cancelResultFaithful = TRUE
    /\ calloutKind = [op \in Operations |-> NoCallout]
    /\ calloutFailure = [op \in Operations |-> FALSE]
    /\ boundaryFailure = [op \in Operations |-> FALSE]
    /\ pendingCalloutFailure = [op \in Operations |-> FALSE]
    /\ callbackOpen = [op \in Operations |-> FALSE]
    /\ progressBudget = MaxProgress
    /\ postCloseAttempted = FALSE
    /\ callbackAfterCloseObserved = FALSE
    /\ terminalOutcome = [op \in Operations |-> NoOutcome]
    /\ terminalReason = [op \in Operations |-> NoReason]
    /\ terminalCount = [op \in Operations |-> 0]
    /\ quiesceCount = [op \in Operations |-> 0]
    /\ subscription = [op \in Operations |-> SubNone]
    /\ producerState = ProducerIdle
    /\ producerLease = NoLease
    /\ leaseFinishCount = 0
    /\ nextWorkSequence = 1
    /\ allocatedSequences = {}
    /\ allocationCount = 0
    /\ exhaustionState = NoExhaustion
    /\ pendingLeaseInstall = FALSE

(***************************************************************************)
(* Admission.  The complete entry is installed synchronously, before the    *)
(* feature body can reach an incomplete wait.  A duplicate concurrently     *)
(* active ID installs no second entry.                                      *)
(***************************************************************************)
Admit(op) ==
    /\ phase[op] = NotAdmitted
    /\ activeEntries[op] = 0
    /\ phase' = [phase EXCEPT ![op] = Active]
    /\ activeEntries' = [activeEntries EXCEPT ![op] = 1]
    /\ callbackOpen' = [callbackOpen EXCEPT ![op] = TRUE]
    /\ bodyStarted' = [bodyStarted EXCEPT ![op] = TRUE]
    /\ UNCHANGED <<bodyResult, duplicateAdmission>>
    /\ UNCHANGED <<progressBudget,
                   postCloseAttempted,
                   callbackAfterCloseObserved>>
    /\ UNCHANGED <<cancelVars, calloutVars, terminalVars, producerVars,
                   leaseVars>>

\* One duplicate concurrently active ID attempt per behaviour is enough to
\* expose whether a second entry can be installed.
AttemptDuplicateAdmission(op) ==
    /\ phase[op] = Active
    /\ duplicateAdmission = DupNone
    /\ IF Mutation = DuplicateAdmission
       THEN /\ duplicateAdmission' = DupInstalled
            /\ activeEntries' = [activeEntries EXCEPT ![op] = @ + 1]
       ELSE /\ duplicateAdmission' = DupRejected
            /\ UNCHANGED activeEntries
    /\ UNCHANGED <<phase, bodyStarted, bodyResult>>
    /\ UNCHANGED <<cancelVars, calloutVars, progressVars, terminalVars,
                   producerVars, leaseVars>>

\* Faithfully, the body starts only inside a registered entry; this action is
\* the deliberate defect that lets it start first.
MutantStartBodyBeforeRegistration(op) ==
    /\ Mutation = BodyBeforeRegistration
    /\ phase[op] = NotAdmitted
    /\ ~bodyStarted[op]
    /\ bodyStarted' = [bodyStarted EXCEPT ![op] = TRUE]
    /\ UNCHANGED <<phase, activeEntries, bodyResult, duplicateAdmission>>
    /\ UNCHANGED <<cancelVars, calloutVars, progressVars, terminalVars,
                   producerVars, leaseVars>>

(***************************************************************************)
(* Shared producer attachment.  Ownership of the producer stays outside the *)
(* bridge; the bridge models only subscription attachment and detachment.   *)
(***************************************************************************)
AttachWaiter(op) ==
    /\ phase[op] = Active
    /\ bodyStarted[op]
    /\ subscription[op] = SubNone
    /\ producerState \in {ProducerIdle, ProducerRunning}
    /\ subscription' = [subscription EXCEPT ![op] = SubAttached]
    /\ producerState' = ProducerRunning
    /\ UNCHANGED entryVars
    /\ UNCHANGED <<cancelVars, calloutVars, progressVars, terminalVars,
                   leaseVars>>

ProducerSucceed ==
    /\ producerState = ProducerRunning
    /\ producerState' = ProducerSucceeded
    /\ UNCHANGED subscription
    /\ UNCHANGED entryVars
    /\ UNCHANGED <<cancelVars, calloutVars, progressVars, terminalVars,
                   leaseVars>>

ProducerFail ==
    /\ producerState = ProducerRunning
    /\ producerState' = ProducerFailed
    /\ UNCHANGED subscription
    /\ UNCHANGED entryVars
    /\ UNCHANGED <<cancelVars, calloutVars, progressVars, terminalVars,
                   leaseVars>>

ProducerSettle == ProducerSucceed \/ ProducerFail

\* Feature-owned policy may stop a producer only once no waiter remains.
ProducerStop ==
    /\ producerState = ProducerRunning
    /\ Waiters = {}
    /\ producerState' = ProducerStopped
    /\ UNCHANGED subscription
    /\ UNCHANGED entryVars
    /\ UNCHANGED <<cancelVars, calloutVars, progressVars, terminalVars,
                   leaseVars>>

\* Producer finalization releases any installed epoch-work lease exactly once.
ProducerFinalize ==
    /\ producerState \in ProducerSettled
    /\ Waiters = {}
    /\ producerState' = ProducerFinalized
    /\ leaseFinishCount' =
        IF producerLease # NoLease THEN leaseFinishCount + 1
        ELSE leaseFinishCount
    /\ UNCHANGED subscription
    /\ UNCHANGED <<producerLease,
                   nextWorkSequence,
                   allocatedSequences,
                   allocationCount,
                   exhaustionState,
                   pendingLeaseInstall>>
    /\ UNCHANGED entryVars
    /\ UNCHANGED <<cancelVars, calloutVars, progressVars, terminalVars>>

MutantDuplicateWorkFinish ==
    /\ Mutation = DuplicateWorkFinish
    /\ producerState = ProducerFinalized
    /\ producerLease # NoLease
    /\ leaseFinishCount < 2
    /\ leaseFinishCount' = leaseFinishCount + 1
    /\ UNCHANGED producerVars
    /\ UNCHANGED <<producerLease,
                   nextWorkSequence,
                   allocatedSequences,
                   allocationCount,
                   exhaustionState,
                   pendingLeaseInstall>>
    /\ UNCHANGED entryVars
    /\ UNCHANGED <<cancelVars, calloutVars, progressVars, terminalVars>>

(***************************************************************************)
(* Feature-body observations.  BodyCanceled models an operation-token       *)
(* cancellation, which may or may not correspond to an accepted bridge      *)
(* reason.                                                                  *)
(***************************************************************************)
BodyRunning(op) ==
    /\ phase[op] = Active
    /\ bodyStarted[op]
    /\ bodyResult[op] = NoBodyResult

ObserveBodyCanceled(op) ==
    /\ BodyRunning(op)
    /\ bodyResult' = [bodyResult EXCEPT ![op] = BodyCanceled]
    /\ UNCHANGED <<phase, activeEntries, bodyStarted, duplicateAdmission>>
    /\ UNCHANGED <<cancelVars, calloutVars, progressVars, terminalVars,
                   producerVars, leaseVars>>

ObserveProducerValue(op) ==
    /\ BodyRunning(op)
    /\ subscription[op] = SubAttached
    /\ producerState = ProducerSucceeded
    /\ bodyResult' = [bodyResult EXCEPT ![op] = BodyValue]
    /\ UNCHANGED <<phase, activeEntries, bodyStarted, duplicateAdmission>>
    /\ UNCHANGED <<cancelVars, calloutVars, progressVars, terminalVars,
                   producerVars, leaseVars>>

ObserveExpectedFailure(op) ==
    /\ BodyRunning(op)
    /\ subscription[op] = SubAttached
    /\ producerState = ProducerFailed
    /\ bodyResult' = [bodyResult EXCEPT ![op] = BodyExpectedFailure]
    /\ UNCHANGED <<phase, activeEntries, bodyStarted, duplicateAdmission>>
    /\ UNCHANGED <<cancelVars, calloutVars, progressVars, terminalVars,
                   producerVars, leaseVars>>

ObserveUnexpectedFailure(op) ==
    /\ BodyRunning(op)
    /\ subscription[op] = SubAttached
    /\ producerState = ProducerFailed
    /\ bodyResult' = [bodyResult EXCEPT ![op] = BodyUnexpectedFailure]
    /\ UNCHANGED <<phase, activeEntries, bodyStarted, duplicateAdmission>>
    /\ UNCHANGED <<cancelVars, calloutVars, progressVars, terminalVars,
                   producerVars, leaseVars>>

ObserveBody(op) ==
    \/ ObserveBodyCanceled(op)
    \/ ObserveProducerValue(op)
    \/ ObserveExpectedFailure(op)
    \/ ObserveUnexpectedFailure(op)

(***************************************************************************)
(* Cancellation.  The first accepted reason linearizes under the entry      *)
(* guard; later requests cannot overwrite it or signal the token again.     *)
(***************************************************************************)
ExpectedCancelResult(op) ==
    IF phase[op] = Active
    THEN IF cancelReason[op] = NoReason
         THEN CancelApplied
         ELSE CancelAlreadyRequested
    ELSE CancelNotActive

RequestCancel(op, reason) ==
    /\ phase[op] # NotAdmitted
    /\ cancelRequestBudget > 0
    /\ calloutKind[op] = NoCallout
    /\ LET accepts == phase[op] = Active /\ cancelReason[op] = NoReason
           mutantAccepts ==
               /\ Mutation = SettlingAcceptsCancel
               /\ phase[op] = Settling
               /\ cancelReason[op] = NoReason
           mutantOverwrites ==
               /\ Mutation = ReasonOverwrite
               /\ phase[op] = Active
               /\ cancelReason[op] # NoReason
           returned ==
               IF mutantAccepts THEN CancelApplied
               ELSE ExpectedCancelResult(op)
       IN
        /\ cancelRequestBudget' = cancelRequestBudget - 1
        /\ lastCancelResult' = returned
        /\ cancelResultFaithful' =
            (cancelResultFaithful /\ (returned = ExpectedCancelResult(op)))
        /\ cancelReason' =
            IF accepts \/ mutantAccepts \/ mutantOverwrites
            THEN [cancelReason EXCEPT ![op] = reason]
            ELSE cancelReason
        /\ firstAcceptedReason' =
            IF accepts
            THEN [firstAcceptedReason EXCEPT ![op] = reason]
            ELSE firstAcceptedReason
        /\ cancelSignalCount' =
            IF accepts \/ mutantAccepts
            THEN [cancelSignalCount EXCEPT ![op] = @ + 1]
            ELSE cancelSignalCount
        /\ calloutKind' =
            IF accepts \/ mutantAccepts
            THEN [calloutKind EXCEPT ![op] = CancelCallout]
            ELSE calloutKind
    /\ UNCHANGED <<calloutFailure, boundaryFailure, pendingCalloutFailure>>
    /\ UNCHANGED entryVars
    /\ UNCHANGED <<progressVars, terminalVars, producerVars, leaseVars>>

EndCancelCalloutSucceeded(op) ==
    /\ calloutKind[op] = CancelCallout
    /\ calloutKind' = [calloutKind EXCEPT ![op] = NoCallout]
    /\ UNCHANGED <<calloutFailure, boundaryFailure, pendingCalloutFailure>>
    /\ UNCHANGED entryVars
    /\ UNCHANGED <<cancelVars, progressVars, terminalVars, producerVars,
                   leaseVars>>

\* A throwing token callback is recorded on the entry before the lease is
\* released, so classification cannot run before observing the failure.
EndCancelCalloutFailed(op) ==
    /\ calloutKind[op] = CancelCallout
    /\ ~calloutFailure[op]
    /\ ~pendingCalloutFailure[op]
    /\ calloutKind' = [calloutKind EXCEPT ![op] = NoCallout]
    /\ calloutFailure' = [calloutFailure EXCEPT ![op] = TRUE]
    /\ UNCHANGED <<boundaryFailure, pendingCalloutFailure>>
    /\ UNCHANGED entryVars
    /\ UNCHANGED <<cancelVars, progressVars, terminalVars, producerVars,
                   leaseVars>>

(***************************************************************************)
(* Progress.  The scoped reporter invokes the JavaScript callback only      *)
(* while the entry is active and the callback lease is open.                *)
(***************************************************************************)
BeginProgressCallout(op) ==
    /\ \/ phase[op] = Active
       \/ (Mutation = ProgressAfterSeal
           /\ phase[op] \in {Settling, Classified})
    /\ callbackOpen[op]
    /\ calloutKind[op] = NoCallout
    /\ progressBudget > 0
    /\ progressBudget' = progressBudget - 1
    /\ calloutKind' = [calloutKind EXCEPT ![op] = ProgressCallout]
    /\ UNCHANGED <<calloutFailure, boundaryFailure, pendingCalloutFailure>>
    /\ UNCHANGED <<callbackOpen, postCloseAttempted,
                   callbackAfterCloseObserved>>
    /\ UNCHANGED entryVars
    /\ UNCHANGED <<cancelVars, terminalVars, producerVars, leaseVars>>

EndProgressCalloutSucceeded(op) ==
    /\ calloutKind[op] = ProgressCallout
    /\ calloutKind' = [calloutKind EXCEPT ![op] = NoCallout]
    /\ UNCHANGED <<calloutFailure, boundaryFailure, pendingCalloutFailure>>
    /\ UNCHANGED entryVars
    /\ UNCHANGED <<cancelVars, progressVars, terminalVars, producerVars,
                   leaseVars>>

\* A throwing progress callback is a bridge-contract failure: it is recorded
\* on the entry and closes further progress before the lease is released, and
\* it rejects the exported Task after release rather than becoming a
\* feature-owned terminal envelope.
EndProgressCalloutFailed(op) ==
    /\ calloutKind[op] = ProgressCallout
    /\ ~boundaryFailure[op]
    /\ ~pendingCalloutFailure[op]
    /\ calloutKind' = [calloutKind EXCEPT ![op] = NoCallout]
    /\ boundaryFailure' = [boundaryFailure EXCEPT ![op] = TRUE]
    /\ callbackOpen' = [callbackOpen EXCEPT ![op] = FALSE]
    /\ UNCHANGED <<calloutFailure, pendingCalloutFailure>>
    /\ UNCHANGED <<progressBudget, postCloseAttempted,
                   callbackAfterCloseObserved>>
    /\ UNCHANGED entryVars
    /\ UNCHANGED <<cancelVars, terminalVars, producerVars, leaseVars>>

\* Faithfully, every callout records its failure before releasing its lease.
\* This defect releases first and records afterwards.
MutantReleaseLeaseBeforeRecordingFailure(op) ==
    /\ Mutation = ReleaseLeaseBeforeRecordingFailure
    /\ calloutKind[op] # NoCallout
    /\ ~calloutFailure[op]
    /\ ~pendingCalloutFailure[op]
    /\ calloutKind' = [calloutKind EXCEPT ![op] = NoCallout]
    /\ pendingCalloutFailure' = [pendingCalloutFailure EXCEPT ![op] = TRUE]
    /\ UNCHANGED <<calloutFailure, boundaryFailure>>
    /\ UNCHANGED entryVars
    /\ UNCHANGED <<cancelVars, progressVars, terminalVars, producerVars,
                   leaseVars>>

MutantRecordDeferredCalloutFailure(op) ==
    /\ Mutation = ReleaseLeaseBeforeRecordingFailure
    /\ pendingCalloutFailure[op]
    /\ calloutFailure' = [calloutFailure EXCEPT ![op] = TRUE]
    /\ pendingCalloutFailure' = [pendingCalloutFailure EXCEPT ![op] = FALSE]
    /\ UNCHANGED <<calloutKind, boundaryFailure>>
    /\ UNCHANGED entryVars
    /\ UNCHANGED <<cancelVars, progressVars, terminalVars, producerVars,
                   leaseVars>>

DrainCallout(op) ==
    \/ EndProgressCalloutSucceeded(op)
    \/ EndProgressCalloutFailed(op)
    \/ EndCancelCalloutSucceeded(op)
    \/ EndCancelCalloutFailed(op)
    \/ MutantReleaseLeaseBeforeRecordingFailure(op)

\* A report racing with or following the seal must not reach JavaScript.
AttemptReportAfterClose(op) ==
    /\ phase[op] \in AfterCallbackCloses
    /\ ~callbackOpen[op]
    /\ ~postCloseAttempted
    /\ postCloseAttempted' = TRUE
    /\ callbackAfterCloseObserved' =
        (callbackAfterCloseObserved \/ (Mutation = CallbackAfterClose))
    /\ UNCHANGED <<callbackOpen, progressBudget>>
    /\ UNCHANGED entryVars
    /\ UNCHANGED <<cancelVars, calloutVars, terminalVars, producerVars,
                   leaseVars>>

(***************************************************************************)
(* Settlement and typed terminal classification.                            *)
(***************************************************************************)
BeginSettlement(op) ==
    /\ phase[op] = Active
    /\ bodyResult[op] # NoBodyResult
    /\ phase' = [phase EXCEPT ![op] = Settling]
    /\ UNCHANGED <<activeEntries, bodyStarted, bodyResult, duplicateAdmission>>
    /\ UNCHANGED <<cancelVars, calloutVars, progressVars, terminalVars,
                   producerVars, leaseVars>>

Classification(op) ==
    IF calloutFailure[op] \/ bodyResult[op] = BodyUnexpectedFailure
    THEN FailedUnexpectedOutcome
    ELSE IF bodyResult[op] = BodyExpectedFailure
         THEN FailedExpectedOutcome
         ELSE IF cancelReason[op] # NoReason
              THEN CanceledOutcome
              ELSE IF bodyResult[op] = BodyCanceled
                   THEN FailedUnexpectedOutcome
                   ELSE SucceededOutcome

Classify(op) ==
    /\ phase[op] = Settling
    /\ \/ calloutKind[op] = NoCallout
       \/ Mutation = ClassifyBeforeDrain
    /\ phase' = [phase EXCEPT ![op] = Classified]
    /\ terminalOutcome' = [terminalOutcome EXCEPT ![op] = Classification(op)]
    /\ terminalReason' =
        [terminalReason EXCEPT ![op] =
            IF Classification(op) = CanceledOutcome
            THEN cancelReason[op]
            ELSE NoReason]
    /\ terminalCount' = [terminalCount EXCEPT ![op] = @ + 1]
    /\ UNCHANGED quiesceCount
    /\ UNCHANGED <<activeEntries, bodyStarted, bodyResult, duplicateAdmission>>
    /\ UNCHANGED <<cancelVars, calloutVars, progressVars, producerVars,
                   leaseVars>>

(***************************************************************************)
(* Release.  Close the callback lease, detach the shared subscription,      *)
(* remove the exact entry, then quiesce.                                    *)
(***************************************************************************)
CloseCallback(op) ==
    /\ phase[op] = Classified
    /\ phase' = [phase EXCEPT ![op] = CallbackClosed]
    /\ callbackOpen' = [callbackOpen EXCEPT ![op] = FALSE]
    /\ UNCHANGED <<progressBudget, postCloseAttempted,
                   callbackAfterCloseObserved>>
    /\ UNCHANGED <<activeEntries, bodyStarted, bodyResult, duplicateAdmission>>
    /\ UNCHANGED <<cancelVars, calloutVars, terminalVars, producerVars,
                   leaseVars>>

AdvanceToDetached(op) == phase' = [phase EXCEPT ![op] = Detached]

DetachUnchanged ==
    /\ UNCHANGED <<activeEntries, bodyStarted, bodyResult, duplicateAdmission>>
    /\ UNCHANGED <<cancelVars, calloutVars, progressVars, terminalVars>>

DetachNoSubscription(op) ==
    /\ phase[op] = CallbackClosed
    /\ subscription[op] = SubNone
    /\ AdvanceToDetached(op)
    /\ UNCHANGED <<producerVars, leaseVars>>
    /\ DetachUnchanged

\* One waiter leaving while another remains never terminates the producer.
DetachNonFinalWaiter(op) ==
    /\ phase[op] = CallbackClosed
    /\ subscription[op] = SubAttached
    /\ WaitersOf([subscription EXCEPT ![op] = SubDetached]) # {}
    /\ AdvanceToDetached(op)
    /\ subscription' = [subscription EXCEPT ![op] = SubDetached]
    /\ producerState' =
        IF Mutation = FirstWaiterStopsProducer
        THEN ProducerStopped
        ELSE producerState
    /\ UNCHANGED leaseVars
    /\ DetachUnchanged

DetachFinalWaiterProducerSettled(op) ==
    /\ phase[op] = CallbackClosed
    /\ subscription[op] = SubAttached
    /\ WaitersOf([subscription EXCEPT ![op] = SubDetached]) = {}
    /\ producerState # ProducerRunning
    /\ AdvanceToDetached(op)
    /\ subscription' = [subscription EXCEPT ![op] = SubDetached]
    /\ UNCHANGED producerState
    /\ UNCHANGED leaseVars
    /\ DetachUnchanged

(***************************************************************************)
(* Final detachment of a producer that outlives its last operation wrapper. *)
(* Lease allocation and start commit before the final waiter is removed, so *)
(* the lease precedes that operation's quiescence.                          *)
(***************************************************************************)
DetachFinalWaiterWithLease(op) ==
    /\ phase[op] = CallbackClosed
    /\ subscription[op] = SubAttached
    /\ WaitersOf([subscription EXCEPT ![op] = SubDetached]) = {}
    /\ producerState = ProducerRunning
    /\ nextWorkSequence <= MaxWorkSequence
    /\ AdvanceToDetached(op)
    /\ subscription' = [subscription EXCEPT ![op] = SubDetached]
    /\ UNCHANGED producerState
    /\ producerLease' = nextWorkSequence
    /\ allocatedSequences' = allocatedSequences \cup {nextWorkSequence}
    /\ allocationCount' = allocationCount + 1
    /\ nextWorkSequence' =
        IF Mutation = WorkSequenceReuse
        THEN nextWorkSequence
        ELSE nextWorkSequence + 1
    /\ UNCHANGED <<leaseFinishCount, exhaustionState, pendingLeaseInstall>>
    /\ DetachUnchanged

\* Exhaustion must be visible: the producer is transferred to a fault state
\* rather than continuing as unleased work.
DetachFinalWaiterExhausted(op) ==
    /\ phase[op] = CallbackClosed
    /\ subscription[op] = SubAttached
    /\ WaitersOf([subscription EXCEPT ![op] = SubDetached]) = {}
    /\ producerState = ProducerRunning
    /\ nextWorkSequence > MaxWorkSequence
    /\ Mutation # SilentExhaustion
    /\ AdvanceToDetached(op)
    /\ subscription' = [subscription EXCEPT ![op] = SubDetached]
    /\ producerState' = ProducerFaulted
    /\ exhaustionState' = ExhaustionVisible
    /\ UNCHANGED <<producerLease,
                   leaseFinishCount,
                   nextWorkSequence,
                   allocatedSequences,
                   allocationCount,
                   pendingLeaseInstall>>
    /\ DetachUnchanged

MutantDetachFinalWaiterSilentlyExhausted(op) ==
    /\ Mutation = SilentExhaustion
    /\ phase[op] = CallbackClosed
    /\ subscription[op] = SubAttached
    /\ WaitersOf([subscription EXCEPT ![op] = SubDetached]) = {}
    /\ producerState = ProducerRunning
    /\ nextWorkSequence > MaxWorkSequence
    /\ AdvanceToDetached(op)
    /\ subscription' = [subscription EXCEPT ![op] = SubDetached]
    /\ UNCHANGED producerState
    /\ exhaustionState' = ExhaustionHidden
    /\ UNCHANGED <<producerLease,
                   leaseFinishCount,
                   nextWorkSequence,
                   allocatedSequences,
                   allocationCount,
                   pendingLeaseInstall>>
    /\ DetachUnchanged

MutantDetachFinalWaiterWithoutLease(op) ==
    /\ Mutation = MissingEpochLease
    /\ phase[op] = CallbackClosed
    /\ subscription[op] = SubAttached
    /\ WaitersOf([subscription EXCEPT ![op] = SubDetached]) = {}
    /\ producerState = ProducerRunning
    /\ AdvanceToDetached(op)
    /\ subscription' = [subscription EXCEPT ![op] = SubDetached]
    /\ UNCHANGED producerState
    /\ UNCHANGED leaseVars
    /\ DetachUnchanged

\* Faithfully, lease allocation and start commit into that exact producer
\* before the final waiter removal commits.  This defect removes the final
\* waiter first and installs the lease in a later step.
MutantDetachFinalWaiterDeferredLease(op) ==
    /\ Mutation = NonAtomicFinalDetach
    /\ phase[op] = CallbackClosed
    /\ subscription[op] = SubAttached
    /\ WaitersOf([subscription EXCEPT ![op] = SubDetached]) = {}
    /\ producerState = ProducerRunning
    /\ nextWorkSequence <= MaxWorkSequence
    /\ AdvanceToDetached(op)
    /\ subscription' = [subscription EXCEPT ![op] = SubDetached]
    /\ UNCHANGED producerState
    /\ pendingLeaseInstall' = TRUE
    /\ UNCHANGED <<producerLease,
                   leaseFinishCount,
                   nextWorkSequence,
                   allocatedSequences,
                   allocationCount,
                   exhaustionState>>
    /\ DetachUnchanged

MutantInstallDeferredLease ==
    /\ Mutation = NonAtomicFinalDetach
    /\ pendingLeaseInstall
    /\ nextWorkSequence <= MaxWorkSequence
    /\ pendingLeaseInstall' = FALSE
    /\ producerLease' = nextWorkSequence
    /\ allocatedSequences' = allocatedSequences \cup {nextWorkSequence}
    /\ allocationCount' = allocationCount + 1
    /\ nextWorkSequence' = nextWorkSequence + 1
    /\ UNCHANGED <<leaseFinishCount, exhaustionState>>
    /\ UNCHANGED producerVars
    /\ UNCHANGED entryVars
    /\ UNCHANGED <<cancelVars, calloutVars, progressVars, terminalVars>>

DetachSubscription(op) ==
    \/ DetachNoSubscription(op)
    \/ DetachNonFinalWaiter(op)
    \/ DetachFinalWaiterProducerSettled(op)
    \/ DetachFinalWaiterWithLease(op)
    \/ DetachFinalWaiterExhausted(op)
    \/ MutantDetachFinalWaiterSilentlyExhausted(op)
    \/ MutantDetachFinalWaiterWithoutLease(op)
    \/ MutantDetachFinalWaiterDeferredLease(op)

RemoveEntry(op) ==
    /\ \/ phase[op] = Detached
       \/ (Mutation = RemoveBeforeCallbackClose /\ phase[op] = Classified)
    /\ phase' = [phase EXCEPT ![op] = Removed]
    /\ activeEntries' = [activeEntries EXCEPT ![op] = 0]
    /\ UNCHANGED <<bodyStarted, bodyResult, duplicateAdmission>>
    /\ UNCHANGED <<cancelVars, calloutVars, progressVars, terminalVars,
                   producerVars, leaseVars>>

Quiesce(op) ==
    /\ \/ phase[op] = Removed
       \/ (Mutation = QuiesceBeforeRelease /\ phase[op] = Classified)
    /\ phase' = [phase EXCEPT ![op] = Quiesced]
    /\ quiesceCount' = [quiesceCount EXCEPT ![op] = @ + 1]
    /\ UNCHANGED <<terminalOutcome, terminalReason, terminalCount>>
    /\ UNCHANGED <<activeEntries, bodyStarted, bodyResult, duplicateAdmission>>
    /\ UNCHANGED <<cancelVars, calloutVars, progressVars, producerVars,
                   leaseVars>>

Next ==
    \/ \E op \in Operations : Admit(op)
    \/ \E op \in Operations : AttemptDuplicateAdmission(op)
    \/ \E op \in Operations : MutantStartBodyBeforeRegistration(op)
    \/ \E op \in Operations : AttachWaiter(op)
    \/ \E op \in Operations : ObserveBody(op)
    \/ \E op \in Operations, r \in Reasons : RequestCancel(op, r)
    \/ \E op \in Operations : DrainCallout(op)
    \/ \E op \in Operations : BeginProgressCallout(op)
    \/ \E op \in Operations : AttemptReportAfterClose(op)
    \/ \E op \in Operations : BeginSettlement(op)
    \/ \E op \in Operations : Classify(op)
    \/ \E op \in Operations : CloseCallback(op)
    \/ \E op \in Operations : DetachSubscription(op)
    \/ \E op \in Operations : RemoveEntry(op)
    \/ \E op \in Operations : Quiesce(op)
    \/ ProducerSettle
    \/ ProducerStop
    \/ ProducerFinalize
    \/ MutantDuplicateWorkFinish
    \/ MutantInstallDeferredLease
    \/ \E op \in Operations : MutantRecordDeferredCalloutFailure(op)

(***************************************************************************)
(* Type correctness.                                                        *)
(***************************************************************************)
TypeOK ==
    /\ phase \in [Operations -> Phases]
    /\ activeEntries \in [Operations -> 0..2]
    /\ bodyStarted \in [Operations -> BOOLEAN]
    /\ bodyResult \in [Operations -> BodyResults]
    /\ duplicateAdmission \in DuplicateAdmissionStates
    /\ cancelReason \in [Operations -> Reasons \cup {NoReason}]
    /\ firstAcceptedReason \in [Operations -> Reasons \cup {NoReason}]
    /\ cancelSignalCount \in [Operations -> 0..MaxCancelRequests]
    /\ cancelRequestBudget \in 0..MaxCancelRequests
    /\ lastCancelResult \in CancelResults
    /\ cancelResultFaithful \in BOOLEAN
    /\ calloutKind \in [Operations -> CalloutKinds]
    /\ calloutFailure \in [Operations -> BOOLEAN]
    /\ boundaryFailure \in [Operations -> BOOLEAN]
    /\ pendingCalloutFailure \in [Operations -> BOOLEAN]
    /\ callbackOpen \in [Operations -> BOOLEAN]
    /\ progressBudget \in 0..MaxProgress
    /\ postCloseAttempted \in BOOLEAN
    /\ callbackAfterCloseObserved \in BOOLEAN
    /\ terminalOutcome \in [Operations -> Outcomes]
    /\ terminalReason \in [Operations -> Reasons \cup {NoReason}]
    /\ terminalCount \in [Operations -> 0..2]
    /\ quiesceCount \in [Operations -> 0..2]
    /\ subscription \in [Operations -> Subscriptions]
    /\ producerState \in ProducerStates
    /\ producerLease \in 0..MaxWorkSequence
    /\ leaseFinishCount \in 0..2
    /\ nextWorkSequence \in 1..(MaxWorkSequence + 1)
    /\ allocatedSequences \subseteq 1..MaxWorkSequence
    /\ allocationCount \in 0..MaxWorkSequence
    /\ exhaustionState \in ExhaustionStates
    /\ pendingLeaseInstall \in BOOLEAN

(***************************************************************************)
(* Safety properties.                                                       *)
(***************************************************************************)

\* Registration is synchronous before the body can reach an incomplete wait.
RegistrationPrecedesManagedWork ==
    \A op \in Operations : bodyStarted[op] => phase[op] # NotAdmitted

\* A duplicate concurrently active ID installs no second entry.
OneActiveEntryPerId ==
    \A op \in Operations : activeEntries[op] <= 1

\* Cancellation linearizes when the first reason is stored.
FirstCancellationReasonWins ==
    \A op \in Operations :
        firstAcceptedReason[op] # NoReason =>
            cancelReason[op] = firstAcceptedReason[op]

CancellationSignalsAtMostOnce ==
    \A op \in Operations : cancelSignalCount[op] <= 1

\* Once settlement begins, cancellation returns not-active and signals nothing.
SettlingOperationRejectsCancellation ==
    \A op \in Operations :
        phase[op] \in AfterSettlementBegins =>
            cancelSignalCount[op] =
                (IF firstAcceptedReason[op] = NoReason THEN 0 ELSE 1)

\* The returned request result faithfully distinguishes active from not-active.
CancellationResultMatchesEntryState == cancelResultFaithful

\* The reason is stored and token signaling claimed before the callout lease is
\* taken and Cancel() is called outside the table guard.
CancelCalloutFollowsStoredReason ==
    \A op \in Operations :
        calloutKind[op] = CancelCallout => cancelReason[op] # NoReason

\* Settlement seals the entry against new progress callouts, so a progress
\* lease exists only while the entry is active or still draining.
SettlementSealsProgressAdmission ==
    \A op \in Operations :
        calloutKind[op] = ProgressCallout => phase[op] \in {Active, Settling}

\* Only post-drain state supplies the reason and failures used to classify.
CalloutsDrainBeforeClassification ==
    \A op \in Operations :
        terminalCount[op] > 0 => calloutKind[op] = NoCallout

\* Every callout records its failure before releasing its lease, so the
\* post-drain state that classifies and releases has already observed it.
ClassificationObservesCalloutFailure ==
    \A op \in Operations :
        (terminalCount[op] > 0 /\ calloutFailure[op]) =>
            terminalOutcome[op] = FailedUnexpectedOutcome

CalloutFailuresRecordedBeforeQuiescence ==
    \A op \in Operations :
        quiesceCount[op] > 0 => ~pendingCalloutFailure[op]

\* A progress-callback failure closes further progress on that entry.
ProgressFailureClosesCallback ==
    \A op \in Operations : boundaryFailure[op] => ~callbackOpen[op]

OneTerminalClassification ==
    \A op \in Operations : terminalCount[op] <= 1

\* Cancellation accepted before success settlement wins with its exact reason;
\* an unexpected failure is never hidden as expected cancellation.
CancellationReasonIsFaithful ==
    \A op \in Operations :
        /\ (terminalOutcome[op] = CanceledOutcome =>
                /\ terminalReason[op] \in Reasons
                /\ terminalReason[op] = firstAcceptedReason[op])
        /\ (bodyResult[op] = BodyUnexpectedFailure /\ terminalCount[op] > 0 =>
                terminalOutcome[op] = FailedUnexpectedOutcome)

NoCallbackAfterClose == ~callbackAfterCloseObserved

\* Callback close precedes table removal.
CallbackClosesBeforeRemoval ==
    \A op \in Operations :
        (phase[op] # NotAdmitted /\ activeEntries[op] = 0) =>
            ~callbackOpen[op]

\* Table removal and callback/resource release precede quiescence.
QuiescenceRequiresRelease ==
    \A op \in Operations :
        quiesceCount[op] > 0 =>
            /\ ~callbackOpen[op]
            /\ activeEntries[op] = 0
            /\ calloutKind[op] = NoCallout
            /\ subscription[op] # SubAttached
            /\ terminalCount[op] = 1

\* Settlement seals the entry and waits for every in-flight callout lease, so
\* no callout can still be in flight when the operation quiesces.
QuiescenceRequiresCalloutDrain ==
    \A op \in Operations :
        quiesceCount[op] > 0 => calloutKind[op] = NoCallout

OneQuiescencePerOperation ==
    \A op \in Operations : quiesceCount[op] <= 1

\* Canceling or detaching one waiter must not terminate a producer that still
\* has another waiter.
OneWaiterDoesNotStopSharedProducer ==
    producerState = ProducerStopped => Waiters = {}

\* A producer that outlives its final operation wrapper holds a started lease.
OutlivingProducerHasEpochWorkLease ==
    (producerState = ProducerRunning /\ Waiters = {}) =>
        producerLease # NoLease

WorkSequenceNeverReused ==
    /\ Cardinality(allocatedSequences) = allocationCount
    /\ \A s \in allocatedSequences : s < nextWorkSequence

WorkSequenceExhaustionIsVisible == exhaustionState # ExhaustionHidden

WorkLeaseFinishesAtMostOnce == leaseFinishCount <= 1

WorkLeaseFinishFollowsStart ==
    leaseFinishCount > 0 => producerLease # NoLease

(***************************************************************************)
(* Liveness under the stated fairness assumptions.                          *)
(***************************************************************************)
AdmittedEventuallyQuiesces ==
    \A op \in Operations :
        (phase[op] # NotAdmitted) ~> (phase[op] = Quiesced)

RunningProducerEventuallyFinalizes ==
    (producerState = ProducerRunning) ~> (producerState = ProducerFinalized)

StartedLeaseEventuallyFinishes ==
    (producerLease # NoLease) ~> (leaseFinishCount = 1)

Spec ==
    /\ Init
    /\ [][Next]_vars
    /\ \A op \in Operations : WF_vars(ObserveBody(op))
    /\ \A op \in Operations : WF_vars(DrainCallout(op))
    /\ \A op \in Operations : WF_vars(BeginSettlement(op))
    /\ \A op \in Operations : WF_vars(Classify(op))
    /\ \A op \in Operations : WF_vars(CloseCallback(op))
    /\ \A op \in Operations : WF_vars(DetachSubscription(op))
    /\ \A op \in Operations : WF_vars(RemoveEntry(op))
    /\ \A op \in Operations : WF_vars(Quiesce(op))
    /\ WF_vars(ProducerSettle)
    /\ WF_vars(ProducerFinalize)

=============================================================================
