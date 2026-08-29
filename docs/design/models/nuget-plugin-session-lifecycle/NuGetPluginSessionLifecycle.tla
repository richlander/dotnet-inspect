---------------- MODULE NuGetPluginSessionLifecycle ----------------
EXTENDS FiniteSets, Integers, TLC

\* Owned by docs/design/nuget-authentication.md.
\* Discovery, HTTP authentication retries, credential scope, and token
\* acquisition are inputs or adjacent concerns. This model owns only one
\* plugin connection's bidirectional conversation and terminal shutdown.

CONSTANTS
    RequestCount,
    HandshakeMode,
    InboundFailureMode,
    ResponseMode,
    ProgressMode,
    ShutdownMode

ASSUME
    /\ RequestCount = 2
    /\ HandshakeMode \in {"Symmetric", "HostOnly"}
    /\ InboundFailureMode \in {"Contain", "Drop"}
    /\ ResponseMode \in {"Correlated", "Misdirect"}
    /\ ProgressMode \in {"Correlated", "Misdirect"}
    /\ ShutdownMode \in {"CloseAdmission", "SnapshotOnly"}

Requests == 1..RequestCount

ConnectionStates == {"Handshaking", "Ready", "Failed", "Closing", "Closed"}
ReadLoopStates == {"Running", "Stopped"}
HostHandshakeStates == {"Waiting", "Succeeded", "Failed"}
PluginHandshakeStates ==
    {"NotReceived",
     "ValidWaitingWrite",
     "InvalidWaitingWrite",
     "ValidWriting",
     "InvalidWriting",
     "RespondedSuccess",
     "RespondedError",
     "Dropped",
     "Aborted"}
RequestStates == {"Unused", "Registered", "Writing", "Waiting", "Done"}
RequestOutcomes ==
    {"Pending", "Success", "PluginFault", "TimedOut",
     "CallerCanceled", "ConnectionClosed"}
ShutdownPhases == {"None", "Observed", "Captured", "Settled"}
NoWriter == 0
PluginHandshakeWriter == RequestCount + 1
WriterOwners == 0..PluginHandshakeWriter

VARIABLES
    hostHandshakeSucceeds,
    inboundHandshakeValid,
    claimsAuthentication,
    connection,
    readLoop,
    admissionOpen,
    hostHandshake,
    pluginHandshake,
    writeOwner,
    requestState,
    requestOutcome,
    deadlineRemaining,
    progressSent,
    completionCount,
    shutdownPhase,
    shutdownSnapshot,
    readyWitness,
    admissionWitness,
    responseWitness,
    progressWitness

vars ==
    <<hostHandshakeSucceeds, inboundHandshakeValid, claimsAuthentication,
      connection, readLoop, admissionOpen, hostHandshake, pluginHandshake,
      writeOwner, requestState, requestOutcome, deadlineRemaining,
      progressSent, completionCount, shutdownPhase, shutdownSnapshot,
      readyWitness, admissionWitness, responseWitness, progressWitness>>

LiveRequest(request) ==
    requestState[request] \in {"Registered", "Writing", "Waiting"}

LiveRequests ==
    {request \in Requests : LiveRequest(request)}

AlternativeWaitingRequest(source) ==
    CHOOSE target \in Requests \ {source} :
        requestState[target] = "Waiting"

CanMisdirect(source) ==
    \E target \in Requests \ {source} :
        requestState[target] = "Waiting"

ResponseTarget(source) ==
    IF ResponseMode = "Correlated"
    THEN source
    ELSE AlternativeWaitingRequest(source)

ProgressTarget(source) ==
    IF ProgressMode = "Correlated"
    THEN source
    ELSE AlternativeWaitingRequest(source)

HandshakeSatisfied ==
    IF HandshakeMode = "Symmetric"
    THEN pluginHandshake = "RespondedSuccess"
    ELSE TRUE

InboundHandshakeLive ==
    pluginHandshake \in
        {"ValidWaitingWrite", "InvalidWaitingWrite",
         "ValidWriting", "InvalidWriting"}

InboundHandshakeSettled ==
    pluginHandshake \in
        {"RespondedSuccess", "RespondedError", "Aborted"}

Init ==
    /\ hostHandshakeSucceeds \in BOOLEAN
    /\ inboundHandshakeValid \in BOOLEAN
    /\ claimsAuthentication \in BOOLEAN
    /\ connection = "Handshaking"
    /\ readLoop = "Running"
    /\ admissionOpen = FALSE
    /\ hostHandshake = "Waiting"
    /\ pluginHandshake = "NotReceived"
    /\ writeOwner = NoWriter
    /\ requestState =
        [request \in Requests |-> "Unused"]
    /\ requestOutcome =
        [request \in Requests |-> "Pending"]
    /\ deadlineRemaining =
        [request \in Requests |-> 0]
    /\ progressSent =
        [request \in Requests |-> 0]
    /\ completionCount =
        [request \in Requests |-> 0]
    /\ shutdownPhase = "None"
    /\ shutdownSnapshot = {}
    /\ readyWitness = TRUE
    /\ admissionWitness = TRUE
    /\ responseWitness = TRUE
    /\ progressWitness = TRUE

DeliverHostHandshake ==
    /\ connection = "Handshaking"
    /\ readLoop = "Running"
    /\ shutdownPhase = "None"
    /\ hostHandshake = "Waiting"
    /\ hostHandshake' =
        IF hostHandshakeSucceeds
        THEN "Succeeded"
        ELSE "Failed"
    /\ UNCHANGED
        <<hostHandshakeSucceeds, inboundHandshakeValid,
          claimsAuthentication, connection, readLoop, admissionOpen,
          pluginHandshake, writeOwner, requestState, requestOutcome,
          deadlineRemaining, progressSent, completionCount,
          shutdownPhase, shutdownSnapshot, readyWitness,
          admissionWitness, responseWitness, progressWitness>>

DeliverPluginHandshake ==
    /\ connection = "Handshaking"
    /\ readLoop = "Running"
    /\ shutdownPhase = "None"
    /\ pluginHandshake = "NotReceived"
    /\ pluginHandshake' =
        IF inboundHandshakeValid
        THEN "ValidWaitingWrite"
        ELSE "InvalidWaitingWrite"
    /\ UNCHANGED
        <<hostHandshakeSucceeds, inboundHandshakeValid,
          claimsAuthentication, connection, readLoop, admissionOpen,
          hostHandshake, writeOwner, requestState, requestOutcome,
          deadlineRemaining, progressSent, completionCount,
          shutdownPhase, shutdownSnapshot, readyWitness,
          admissionWitness, responseWitness, progressWitness>>

AcquirePluginHandshakeWrite ==
    /\ connection = "Handshaking"
    /\ readLoop = "Running"
    /\ shutdownPhase = "None"
    /\ pluginHandshake \in {"ValidWaitingWrite", "InvalidWaitingWrite"}
    /\ writeOwner = NoWriter
    /\ writeOwner' = PluginHandshakeWriter
    /\ pluginHandshake' =
        IF pluginHandshake = "ValidWaitingWrite"
        THEN "ValidWriting"
        ELSE "InvalidWriting"
    /\ UNCHANGED
        <<hostHandshakeSucceeds, inboundHandshakeValid,
          claimsAuthentication, connection, readLoop, admissionOpen,
          hostHandshake, requestState, requestOutcome, deadlineRemaining,
          progressSent, completionCount, shutdownPhase, shutdownSnapshot,
          readyWitness, admissionWitness, responseWitness, progressWitness>>

FinishPluginHandshakeWrite ==
    /\ connection = "Handshaking"
    /\ readLoop = "Running"
    /\ shutdownPhase = "None"
    /\ pluginHandshake \in {"ValidWriting", "InvalidWriting"}
    /\ writeOwner = PluginHandshakeWriter
    /\ pluginHandshake' =
        IF pluginHandshake = "ValidWriting"
        THEN "RespondedSuccess"
        ELSE
            IF InboundFailureMode = "Contain"
            THEN "RespondedError"
            ELSE "Dropped"
    /\ writeOwner' = NoWriter
    /\ UNCHANGED
        <<hostHandshakeSucceeds, inboundHandshakeValid,
          claimsAuthentication, connection, readLoop, admissionOpen,
          hostHandshake, requestState, requestOutcome, deadlineRemaining,
          progressSent, completionCount, shutdownPhase, shutdownSnapshot,
          readyWitness, admissionWitness, responseWitness, progressWitness>>

FailHostInitialization ==
    /\ connection = "Handshaking"
    /\ readLoop = "Running"
    /\ shutdownPhase = "None"
    /\ hostHandshake = "Failed"
    /\ connection' = "Failed"
    /\ readLoop' = "Stopped"
    /\ admissionOpen' = FALSE
    /\ pluginHandshake' =
        IF InboundHandshakeLive
        THEN "Aborted"
        ELSE pluginHandshake
    /\ writeOwner' = NoWriter
    /\ UNCHANGED
        <<hostHandshakeSucceeds, inboundHandshakeValid,
          claimsAuthentication, hostHandshake, requestState,
          requestOutcome, deadlineRemaining, progressSent,
          completionCount, shutdownPhase, shutdownSnapshot,
          readyWitness, admissionWitness, responseWitness, progressWitness>>

FailPluginInitialization ==
    /\ connection = "Handshaking"
    /\ readLoop = "Running"
    /\ shutdownPhase = "None"
    /\ pluginHandshake = "RespondedError"
    /\ connection' = "Failed"
    /\ readLoop' = "Stopped"
    /\ admissionOpen' = FALSE
    /\ writeOwner' = NoWriter
    /\ UNCHANGED
        <<hostHandshakeSucceeds, inboundHandshakeValid,
          claimsAuthentication, hostHandshake, pluginHandshake,
          requestState, requestOutcome, deadlineRemaining, progressSent,
          completionCount, shutdownPhase, shutdownSnapshot, readyWitness,
          admissionWitness, responseWitness, progressWitness>>

ResolveAuthenticationClaim ==
    /\ connection = "Handshaking"
    /\ readLoop = "Running"
    /\ shutdownPhase = "None"
    /\ hostHandshake = "Succeeded"
    /\ HandshakeSatisfied
    /\ connection' =
        IF claimsAuthentication
        THEN "Ready"
        ELSE "Failed"
    /\ readLoop' =
        IF claimsAuthentication
        THEN readLoop
        ELSE "Stopped"
    /\ admissionOpen' = claimsAuthentication
    /\ readyWitness' =
        (readyWitness
         /\ (~claimsAuthentication
             \/ /\ hostHandshake = "Succeeded"
                /\ pluginHandshake = "RespondedSuccess"))
    /\ UNCHANGED
        <<hostHandshakeSucceeds, inboundHandshakeValid,
          claimsAuthentication, hostHandshake, pluginHandshake,
          writeOwner, requestState, requestOutcome, deadlineRemaining,
          progressSent, completionCount, shutdownPhase, shutdownSnapshot,
          admissionWitness, responseWitness, progressWitness>>

BeginRequest(request) ==
    /\ request \in Requests
    /\ requestState[request] = "Unused"
    /\ connection = "Ready"
    /\ admissionOpen
    /\ requestState' =
        [requestState EXCEPT ![request] = "Registered"]
    /\ requestOutcome' =
        [requestOutcome EXCEPT ![request] = "Pending"]
    /\ deadlineRemaining' =
        [deadlineRemaining EXCEPT ![request] = 1]
    /\ progressSent' =
        [progressSent EXCEPT ![request] = 0]
    /\ admissionWitness' =
        (admissionWitness
         /\ readLoop = "Running"
         /\ shutdownPhase = "None")
    /\ UNCHANGED
        <<hostHandshakeSucceeds, inboundHandshakeValid,
          claimsAuthentication, connection, readLoop, admissionOpen,
          hostHandshake, pluginHandshake, writeOwner, completionCount,
          shutdownPhase, shutdownSnapshot, readyWitness,
          responseWitness, progressWitness>>

AcquireRequestWrite(request) ==
    /\ request \in Requests
    /\ connection = "Ready"
    /\ readLoop = "Running"
    /\ requestState[request] = "Registered"
    /\ writeOwner = NoWriter
    /\ requestState' =
        [requestState EXCEPT ![request] = "Writing"]
    /\ writeOwner' = request
    /\ UNCHANGED
        <<hostHandshakeSucceeds, inboundHandshakeValid,
          claimsAuthentication, connection, readLoop, admissionOpen,
          hostHandshake, pluginHandshake, requestOutcome,
          deadlineRemaining, progressSent, completionCount,
          shutdownPhase, shutdownSnapshot, readyWitness,
          admissionWitness, responseWitness, progressWitness>>

FinishRequestWrite(request) ==
    /\ request \in Requests
    /\ connection = "Ready"
    /\ readLoop = "Running"
    /\ requestState[request] = "Writing"
    /\ writeOwner = request
    /\ requestState' =
        [requestState EXCEPT ![request] = "Waiting"]
    /\ writeOwner' = NoWriter
    /\ UNCHANGED
        <<hostHandshakeSucceeds, inboundHandshakeValid,
          claimsAuthentication, connection, readLoop, admissionOpen,
          hostHandshake, pluginHandshake, requestOutcome,
          deadlineRemaining, progressSent, completionCount,
          shutdownPhase, shutdownSnapshot, readyWitness,
          admissionWitness, responseWitness, progressWitness>>

ReceiveResponse(source) ==
    /\ source \in Requests
    /\ requestState[source] = "Waiting"
    /\ (ResponseMode = "Correlated" \/ CanMisdirect(source))
    /\ LET target == ResponseTarget(source)
       IN
        /\ requestState' =
            [requestState EXCEPT ![target] = "Done"]
        /\ requestOutcome' =
            [requestOutcome EXCEPT ![target] = "Success"]
        /\ deadlineRemaining' =
            [deadlineRemaining EXCEPT ![target] = 0]
        /\ completionCount' =
            [completionCount EXCEPT ![target] = @ + 1]
        /\ responseWitness' =
            (responseWitness /\ target = source)
    /\ UNCHANGED
        <<hostHandshakeSucceeds, inboundHandshakeValid,
          claimsAuthentication, connection, readLoop, admissionOpen,
          hostHandshake, pluginHandshake, writeOwner, progressSent,
          shutdownPhase, shutdownSnapshot, readyWitness,
          admissionWitness, progressWitness>>

ReceiveProgress(source) ==
    /\ source \in Requests
    /\ requestState[source] = "Waiting"
    /\ progressSent[source] = 0
    /\ (ProgressMode = "Correlated" \/ CanMisdirect(source))
    /\ LET target == ProgressTarget(source)
       IN
        /\ deadlineRemaining' =
            [deadlineRemaining EXCEPT ![target] = 1]
        /\ progressSent' =
            [progressSent EXCEPT ![source] = 1]
        /\ progressWitness' =
            (progressWitness /\ target = source)
    /\ UNCHANGED
        <<hostHandshakeSucceeds, inboundHandshakeValid,
          claimsAuthentication, connection, readLoop, admissionOpen,
          hostHandshake, pluginHandshake, writeOwner, requestState,
          requestOutcome, completionCount, shutdownPhase,
          shutdownSnapshot, readyWitness, admissionWitness,
          responseWitness>>

ReceiveFault(request) ==
    /\ request \in Requests
    /\ requestState[request] = "Waiting"
    /\ requestState' =
        [requestState EXCEPT ![request] = "Done"]
    /\ requestOutcome' =
        [requestOutcome EXCEPT ![request] = "PluginFault"]
    /\ deadlineRemaining' =
        [deadlineRemaining EXCEPT ![request] = 0]
    /\ completionCount' =
        [completionCount EXCEPT ![request] = @ + 1]
    /\ UNCHANGED
        <<hostHandshakeSucceeds, inboundHandshakeValid,
          claimsAuthentication, connection, readLoop, admissionOpen,
          hostHandshake, pluginHandshake, writeOwner, progressSent,
          shutdownPhase, shutdownSnapshot, readyWitness,
          admissionWitness, responseWitness, progressWitness>>

TickDeadline(request) ==
    /\ request \in Requests
    /\ requestState[request] = "Waiting"
    /\ deadlineRemaining[request] > 0
    /\ deadlineRemaining' =
        [deadlineRemaining EXCEPT ![request] = @ - 1]
    /\ UNCHANGED
        <<hostHandshakeSucceeds, inboundHandshakeValid,
          claimsAuthentication, connection, readLoop, admissionOpen,
          hostHandshake, pluginHandshake, writeOwner, requestState,
          requestOutcome, progressSent, completionCount, shutdownPhase,
          shutdownSnapshot, readyWitness, admissionWitness,
          responseWitness, progressWitness>>

TimeoutRequest(request) ==
    /\ request \in Requests
    /\ requestState[request] = "Waiting"
    /\ deadlineRemaining[request] = 0
    /\ requestState' =
        [requestState EXCEPT ![request] = "Done"]
    /\ requestOutcome' =
        [requestOutcome EXCEPT ![request] = "TimedOut"]
    /\ completionCount' =
        [completionCount EXCEPT ![request] = @ + 1]
    /\ UNCHANGED
        <<hostHandshakeSucceeds, inboundHandshakeValid,
          claimsAuthentication, connection, readLoop, admissionOpen,
          hostHandshake, pluginHandshake, writeOwner,
          deadlineRemaining, progressSent, shutdownPhase,
          shutdownSnapshot, readyWitness, admissionWitness,
          responseWitness, progressWitness>>

CancelCaller(request) ==
    /\ request \in Requests
    /\ LiveRequest(request)
    /\ requestState' =
        [requestState EXCEPT ![request] = "Done"]
    /\ requestOutcome' =
        [requestOutcome EXCEPT ![request] = "CallerCanceled"]
    /\ deadlineRemaining' =
        [deadlineRemaining EXCEPT ![request] = 0]
    /\ completionCount' =
        [completionCount EXCEPT ![request] = @ + 1]
    /\ writeOwner' =
        IF writeOwner = request
        THEN NoWriter
        ELSE writeOwner
    /\ UNCHANGED
        <<hostHandshakeSucceeds, inboundHandshakeValid,
          claimsAuthentication, connection, readLoop, admissionOpen,
          hostHandshake, pluginHandshake, progressSent, shutdownPhase,
          shutdownSnapshot, readyWitness, admissionWitness,
          responseWitness, progressWitness>>

ObservePipeClosed ==
    /\ readLoop = "Running"
    /\ connection \in {"Handshaking", "Ready"}
    /\ shutdownPhase = "None"
    /\ readLoop' = "Stopped"
    /\ shutdownPhase' = "Observed"
    /\ admissionOpen' =
        IF ShutdownMode = "CloseAdmission"
        THEN FALSE
        ELSE admissionOpen
    /\ UNCHANGED
        <<hostHandshakeSucceeds, inboundHandshakeValid,
          claimsAuthentication, connection, hostHandshake,
          pluginHandshake, writeOwner, requestState, requestOutcome,
          deadlineRemaining, progressSent, completionCount,
          shutdownSnapshot, readyWitness, admissionWitness,
          responseWitness, progressWitness>>

CaptureShutdownSnapshot ==
    /\ shutdownPhase = "Observed"
    /\ shutdownPhase' = "Captured"
    /\ shutdownSnapshot' = LiveRequests
    /\ UNCHANGED
        <<hostHandshakeSucceeds, inboundHandshakeValid,
          claimsAuthentication, connection, readLoop, admissionOpen,
          hostHandshake, pluginHandshake, writeOwner, requestState,
          requestOutcome, deadlineRemaining, progressSent,
          completionCount, readyWitness, admissionWitness,
          responseWitness, progressWitness>>

SettleShutdownSnapshot ==
    /\ shutdownPhase = "Captured"
    /\ shutdownPhase' = "Settled"
    /\ connection' = "Closing"
    /\ admissionOpen' = FALSE
    /\ requestState' =
        [request \in Requests |->
            IF request \in shutdownSnapshot /\ LiveRequest(request)
            THEN "Done"
            ELSE requestState[request]]
    /\ requestOutcome' =
        [request \in Requests |->
            IF request \in shutdownSnapshot /\ LiveRequest(request)
            THEN "ConnectionClosed"
            ELSE requestOutcome[request]]
    /\ deadlineRemaining' =
        [request \in Requests |->
            IF request \in shutdownSnapshot /\ LiveRequest(request)
            THEN 0
            ELSE deadlineRemaining[request]]
    /\ completionCount' =
        [request \in Requests |->
            IF request \in shutdownSnapshot /\ LiveRequest(request)
            THEN completionCount[request] + 1
            ELSE completionCount[request]]
    /\ pluginHandshake' =
        IF InboundHandshakeLive
        THEN "Aborted"
        ELSE pluginHandshake
    /\ writeOwner' = NoWriter
    /\ UNCHANGED
        <<hostHandshakeSucceeds, inboundHandshakeValid,
          claimsAuthentication, readLoop, hostHandshake, progressSent,
          shutdownSnapshot, readyWitness, admissionWitness,
          responseWitness, progressWitness>>

FinishClose ==
    /\ connection = "Closing"
    /\ LiveRequests = {}
    /\ writeOwner = NoWriter
    /\ connection' = "Closed"
    /\ UNCHANGED
        <<hostHandshakeSucceeds, inboundHandshakeValid,
          claimsAuthentication, readLoop, admissionOpen, hostHandshake,
          pluginHandshake, writeOwner, requestState, requestOutcome,
          deadlineRemaining, progressSent, completionCount,
          shutdownPhase, shutdownSnapshot, readyWitness,
          admissionWitness, responseWitness, progressWitness>>

Next ==
    \/ DeliverHostHandshake
    \/ DeliverPluginHandshake
    \/ AcquirePluginHandshakeWrite
    \/ FinishPluginHandshakeWrite
    \/ FailHostInitialization
    \/ FailPluginInitialization
    \/ ResolveAuthenticationClaim
    \/ \E request \in Requests : BeginRequest(request)
    \/ \E request \in Requests : AcquireRequestWrite(request)
    \/ \E request \in Requests : FinishRequestWrite(request)
    \/ \E request \in Requests : ReceiveResponse(request)
    \/ \E request \in Requests : ReceiveProgress(request)
    \/ \E request \in Requests : ReceiveFault(request)
    \/ \E request \in Requests : TickDeadline(request)
    \/ \E request \in Requests : TimeoutRequest(request)
    \/ \E request \in Requests : CancelCaller(request)
    \/ ObservePipeClosed
    \/ CaptureShutdownSnapshot
    \/ SettleShutdownSnapshot
    \/ FinishClose

Spec ==
    /\ Init
    /\ [][Next]_vars
    /\ WF_vars(DeliverHostHandshake)
    /\ WF_vars(DeliverPluginHandshake)
    /\ WF_vars(AcquirePluginHandshakeWrite)
    /\ WF_vars(FinishPluginHandshakeWrite)
    /\ WF_vars(FailHostInitialization)
    /\ WF_vars(FailPluginInitialization)
    /\ WF_vars(ResolveAuthenticationClaim)
    /\ \A request \in Requests:
        /\ WF_vars(AcquireRequestWrite(request))
        /\ WF_vars(FinishRequestWrite(request))
        /\ WF_vars(TickDeadline(request))
        /\ WF_vars(TimeoutRequest(request))
    /\ WF_vars(CaptureShutdownSnapshot)
    /\ WF_vars(SettleShutdownSnapshot)
    /\ WF_vars(FinishClose)

TypeOK ==
    /\ hostHandshakeSucceeds \in BOOLEAN
    /\ inboundHandshakeValid \in BOOLEAN
    /\ claimsAuthentication \in BOOLEAN
    /\ connection \in ConnectionStates
    /\ readLoop \in ReadLoopStates
    /\ admissionOpen \in BOOLEAN
    /\ hostHandshake \in HostHandshakeStates
    /\ pluginHandshake \in PluginHandshakeStates
    /\ writeOwner \in WriterOwners
    /\ requestState \in [Requests -> RequestStates]
    /\ requestOutcome \in [Requests -> RequestOutcomes]
    /\ deadlineRemaining \in [Requests -> 0..1]
    /\ progressSent \in [Requests -> 0..1]
    /\ completionCount \in [Requests -> 0..1]
    /\ shutdownPhase \in ShutdownPhases
    /\ shutdownSnapshot \in SUBSET Requests
    /\ readyWitness \in BOOLEAN
    /\ admissionWitness \in BOOLEAN
    /\ responseWitness \in BOOLEAN
    /\ progressWitness \in BOOLEAN

RequestCompletionIsExact ==
    \A request \in Requests:
        IF requestState[request] = "Done"
        THEN
            /\ requestOutcome[request] # "Pending"
            /\ completionCount[request] = 1
        ELSE
            /\ requestOutcome[request] = "Pending"
            /\ completionCount[request] = 0

WriterIsOwnedByLiveWork ==
    /\ writeOwner = PluginHandshakeWriter =>
        pluginHandshake \in {"ValidWriting", "InvalidWriting"}
    /\ \A request \in Requests:
        (writeOwner = request) => requestState[request] = "Writing"
    /\ Cardinality(
        {owner \in WriterOwners : owner = writeOwner /\ owner # NoWriter}) <= 1

ReadyRequiresSymmetricHandshake ==
    /\ readyWitness
    /\ (connection = "Ready" =>
        /\ hostHandshake = "Succeeded"
        /\ pluginHandshake = "RespondedSuccess"
        /\ claimsAuthentication)

RequestAdmissionHasLiveReceiver ==
    admissionWitness

ResponsesCompleteOnlyTheirRequest ==
    responseWitness

ProgressRenewsOnlyItsRequest ==
    progressWitness

InboundFailureIsContained ==
    pluginHandshake # "Dropped"

ReadLoopLossClosesAdmission ==
    readLoop = "Stopped" => ~admissionOpen

ShutdownSettlementIsComplete ==
    shutdownPhase = "Settled" => LiveRequests = {}

ClosedConnectionIsQuiescent ==
    connection = "Closed" =>
        /\ LiveRequests = {}
        /\ writeOwner = NoWriter
        /\ ~admissionOpen
        /\ readLoop = "Stopped"

InitializationEventuallySettles ==
    connection = "Handshaking"
        ~> connection \in {"Ready", "Failed", "Closing", "Closed"}

InboundHandshakeEventuallySettles ==
    InboundHandshakeLive
        ~> (InboundHandshakeSettled
            \/ connection \in {"Failed", "Closing", "Closed"})

EveryAdmittedRequestSettles ==
    \A request \in Requests:
        requestState[request] # "Unused"
            ~> requestState[request] = "Done"

ObservedShutdownEventuallyCloses ==
    shutdownPhase # "None" ~> connection = "Closed"

=============================================================================
