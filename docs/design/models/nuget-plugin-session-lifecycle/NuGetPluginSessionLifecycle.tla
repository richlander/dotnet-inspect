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
    ShutdownMode,
    WriteTimeoutMode,
    InitializationTimeoutMode

ASSUME
    /\ RequestCount \in Nat
    /\ RequestCount >= 2
    /\ HandshakeMode \in {"Symmetric", "HostOnly"}
    /\ InboundFailureMode \in {"Contain", "Drop"}
    /\ ResponseMode \in {"Correlated", "Misdirect"}
    /\ ProgressMode \in {"Correlated", "Misdirect"}
    /\ ShutdownMode \in {"CloseAdmission", "SnapshotOnly"}
    /\ WriteTimeoutMode \in
        {"CoversWrite", "AfterWrite",
         "ReleasesWriter", "MisclassifiesPeers"}
    /\ InitializationTimeoutMode \in {"Enforced", "Absent"}

Requests == 1..RequestCount

ConnectionStates == {"Handshaking", "Ready", "Failed", "Closing", "Closed"}
ReadLoopStates == {"Running", "Stopped"}
HostHandshakeStates == {"Waiting", "Succeeded", "Failed"}
PluginHandshakeStates ==
    {"NotReceived",
     "ValidWaitingWrite",
     "InvalidWaitingWrite",
    "MalformedWaitingWrite",
    "ValidWriting",
    "InvalidWriting",
    "MalformedWriting",
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
    responseReceived,
    progressOpen,
    progressMessageParity,
    deadlineRenewalParity,
    completionCount,
    shutdownPhase,
    shutdownSnapshot,
    readyWitness,
    admissionWitness

vars ==
    <<hostHandshakeSucceeds, inboundHandshakeValid, claimsAuthentication,
      connection, readLoop, admissionOpen, hostHandshake, pluginHandshake,
      writeOwner, requestState, requestOutcome, deadlineRemaining,
      responseReceived, progressOpen, progressMessageParity,
      deadlineRenewalParity, completionCount, shutdownPhase,
      shutdownSnapshot, readyWitness, admissionWitness>>

LiveRequest(request) ==
    requestState[request] \in {"Registered", "Writing", "Waiting"}

LiveRequests ==
    {request \in Requests : LiveRequest(request)}

DeadlineEligible(request) ==
    IF WriteTimeoutMode = "AfterWrite"
    THEN requestState[request] = "Waiting"
    ELSE LiveRequest(request)

HandshakeSatisfied ==
    IF HandshakeMode = "Symmetric"
    THEN pluginHandshake = "RespondedSuccess"
    ELSE TRUE

InboundHandshakeLive ==
    pluginHandshake \in
        {"ValidWaitingWrite", "InvalidWaitingWrite",
         "MalformedWaitingWrite", "ValidWriting", "InvalidWriting",
         "MalformedWriting"}

InboundHandshakeSettled ==
    pluginHandshake \in
        {"RespondedSuccess", "RespondedError", "Aborted"}

OtherRequest(request) ==
    IF request = RequestCount
    THEN 1
    ELSE request + 1

ResponseTarget(messageId) ==
    IF ResponseMode = "Correlated"
    THEN messageId
    ELSE OtherRequest(messageId)

ProgressTarget(messageId) ==
    IF ProgressMode = "Correlated"
    THEN messageId
    ELSE OtherRequest(messageId)

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
    /\ responseReceived =
        [request \in Requests |-> FALSE]
    /\ progressOpen =
        [request \in Requests |-> TRUE]
    /\ progressMessageParity =
        [request \in Requests |-> FALSE]
    /\ deadlineRenewalParity =
        [request \in Requests |-> FALSE]
    /\ completionCount =
        [request \in Requests |-> 0]
    /\ shutdownPhase = "None"
    /\ shutdownSnapshot = {}
    /\ readyWitness = TRUE
    /\ admissionWitness = TRUE

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
          deadlineRemaining, responseReceived, progressOpen,
          progressMessageParity, deadlineRenewalParity, completionCount,
          shutdownPhase, shutdownSnapshot, readyWitness, admissionWitness>>

DeliverPluginHandshake ==
    /\ connection = "Handshaking"
    /\ readLoop = "Running"
    /\ shutdownPhase = "None"
    /\ pluginHandshake = "NotReceived"
    /\ pluginHandshake' \in
        {IF inboundHandshakeValid
         THEN "ValidWaitingWrite"
         ELSE "InvalidWaitingWrite",
         "MalformedWaitingWrite"}
    /\ UNCHANGED
        <<hostHandshakeSucceeds, inboundHandshakeValid,
          claimsAuthentication, connection, readLoop, admissionOpen,
          hostHandshake, writeOwner, requestState, requestOutcome,
          deadlineRemaining, responseReceived, progressOpen,
          progressMessageParity, deadlineRenewalParity, completionCount,
          shutdownPhase, shutdownSnapshot, readyWitness, admissionWitness>>

AcquirePluginHandshakeWrite ==
    /\ connection = "Handshaking"
    /\ readLoop = "Running"
    /\ shutdownPhase = "None"
    /\ (pluginHandshake \in
            {"ValidWaitingWrite", "InvalidWaitingWrite"}
        \/ /\ pluginHandshake = "MalformedWaitingWrite"
           /\ InboundFailureMode = "Contain")
    /\ writeOwner = NoWriter
    /\ writeOwner' = PluginHandshakeWriter
    /\ pluginHandshake' =
        IF pluginHandshake = "ValidWaitingWrite"
        THEN "ValidWriting"
        ELSE
            IF pluginHandshake = "InvalidWaitingWrite"
            THEN "InvalidWriting"
            ELSE "MalformedWriting"
    /\ UNCHANGED
        <<hostHandshakeSucceeds, inboundHandshakeValid,
          claimsAuthentication, connection, readLoop, admissionOpen,
          hostHandshake, requestState, requestOutcome, deadlineRemaining,
          responseReceived, progressOpen, progressMessageParity,
          deadlineRenewalParity, completionCount, shutdownPhase,
          shutdownSnapshot, readyWitness, admissionWitness>>

DropMalformedInbound ==
    /\ connection = "Handshaking"
    /\ readLoop = "Running"
    /\ shutdownPhase = "None"
    /\ pluginHandshake = "MalformedWaitingWrite"
    /\ InboundFailureMode = "Drop"
    /\ pluginHandshake' = "Dropped"
    /\ UNCHANGED
        <<hostHandshakeSucceeds, inboundHandshakeValid,
          claimsAuthentication, connection, readLoop, admissionOpen,
          hostHandshake, writeOwner, requestState, requestOutcome,
          deadlineRemaining, responseReceived, progressOpen,
          progressMessageParity, deadlineRenewalParity, completionCount,
          shutdownPhase, shutdownSnapshot, readyWitness, admissionWitness>>

FinishPluginHandshakeWrite ==
    /\ connection = "Handshaking"
    /\ readLoop = "Running"
    /\ shutdownPhase = "None"
    /\ pluginHandshake \in
        {"ValidWriting", "InvalidWriting", "MalformedWriting"}
    /\ writeOwner = PluginHandshakeWriter
    /\ pluginHandshake' =
        IF pluginHandshake = "ValidWriting"
        THEN "RespondedSuccess"
        ELSE "RespondedError"
    /\ writeOwner' = NoWriter
    /\ UNCHANGED
        <<hostHandshakeSucceeds, inboundHandshakeValid,
          claimsAuthentication, connection, readLoop, admissionOpen,
          hostHandshake, requestState, requestOutcome, deadlineRemaining,
          responseReceived, progressOpen, progressMessageParity,
          deadlineRenewalParity, completionCount, shutdownPhase,
          shutdownSnapshot, readyWitness, admissionWitness>>

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
          requestOutcome, deadlineRemaining, responseReceived,
          progressOpen, progressMessageParity, deadlineRenewalParity,
          completionCount, shutdownPhase, shutdownSnapshot,
          readyWitness, admissionWitness>>

TimeoutInitialization ==
    /\ connection = "Handshaking"
    /\ readLoop = "Running"
    /\ shutdownPhase = "None"
    /\ InitializationTimeoutMode = "Enforced"
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
          claimsAuthentication, hostHandshake,
          requestState, requestOutcome, deadlineRemaining,
          responseReceived, progressOpen, progressMessageParity,
          deadlineRenewalParity, completionCount, shutdownPhase,
          shutdownSnapshot, readyWitness, admissionWitness>>

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
          responseReceived, progressOpen, progressMessageParity,
          deadlineRenewalParity, completionCount, shutdownPhase,
          shutdownSnapshot, admissionWitness>>

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
    /\ progressOpen' =
        [progressOpen EXCEPT ![request] = TRUE]
    /\ admissionWitness' =
        (admissionWitness
         /\ readLoop = "Running"
         /\ shutdownPhase = "None")
    /\ UNCHANGED
        <<hostHandshakeSucceeds, inboundHandshakeValid,
          claimsAuthentication, connection, readLoop, admissionOpen,
          hostHandshake, pluginHandshake, writeOwner, responseReceived,
          progressMessageParity, deadlineRenewalParity, completionCount,
          shutdownPhase, shutdownSnapshot, readyWitness>>

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
          deadlineRemaining, responseReceived, progressOpen,
          progressMessageParity, deadlineRenewalParity, completionCount,
          shutdownPhase, shutdownSnapshot, readyWitness, admissionWitness>>

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
          deadlineRemaining, responseReceived, progressOpen,
          progressMessageParity, deadlineRenewalParity, completionCount,
          shutdownPhase, shutdownSnapshot, readyWitness, admissionWitness>>

ReceiveResponse(messageId) ==
    /\ messageId \in Requests
    /\ connection = "Ready"
    /\ readLoop = "Running"
    /\ requestState[messageId] = "Waiting"
    /\ LET target == ResponseTarget(messageId)
       IN
        /\ requestState[target] = "Waiting"
        /\ requestState' =
            [requestState EXCEPT ![target] = "Done"]
        /\ requestOutcome' =
            [requestOutcome EXCEPT ![target] = "Success"]
        /\ deadlineRemaining' =
            [deadlineRemaining EXCEPT ![target] = 0]
        /\ completionCount' =
            [completionCount EXCEPT ![target] = @ + 1]
    /\ responseReceived' =
        [responseReceived EXCEPT ![messageId] = TRUE]
    /\ UNCHANGED
        <<hostHandshakeSucceeds, inboundHandshakeValid,
          claimsAuthentication, connection, readLoop, admissionOpen,
          hostHandshake, pluginHandshake, writeOwner, progressOpen,
          progressMessageParity, deadlineRenewalParity, shutdownPhase,
          shutdownSnapshot, readyWitness, admissionWitness>>

ReceiveProgress(messageId) ==
    /\ messageId \in Requests
    /\ connection = "Ready"
    /\ readLoop = "Running"
    /\ requestState[messageId] = "Waiting"
    /\ progressOpen[messageId]
    /\ LET target == ProgressTarget(messageId)
       IN
        /\ requestState[target] = "Waiting"
        /\ deadlineRemaining' =
            [deadlineRemaining EXCEPT ![target] = 1]
        /\ deadlineRenewalParity' =
            [deadlineRenewalParity EXCEPT ![target] = ~@]
    /\ progressMessageParity' =
        [progressMessageParity EXCEPT ![messageId] = ~@]
    /\ UNCHANGED
        <<hostHandshakeSucceeds, inboundHandshakeValid,
          claimsAuthentication, connection, readLoop, admissionOpen,
          hostHandshake, pluginHandshake, writeOwner, requestState,
          requestOutcome, responseReceived, progressOpen, completionCount,
          shutdownPhase, shutdownSnapshot, readyWitness, admissionWitness>>

StopProgress(request) ==
    /\ request \in Requests
    /\ LiveRequest(request)
    /\ progressOpen[request]
    /\ progressOpen' =
        [progressOpen EXCEPT ![request] = FALSE]
    /\ UNCHANGED
        <<hostHandshakeSucceeds, inboundHandshakeValid,
          claimsAuthentication, connection, readLoop, admissionOpen,
          hostHandshake, pluginHandshake, writeOwner, requestState,
          requestOutcome, deadlineRemaining, responseReceived,
          progressMessageParity, deadlineRenewalParity, completionCount,
          shutdownPhase, shutdownSnapshot, readyWitness, admissionWitness>>

ReceiveFault(request) ==
    /\ request \in Requests
    /\ connection = "Ready"
    /\ readLoop = "Running"
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
          hostHandshake, pluginHandshake, writeOwner, responseReceived,
          progressOpen, progressMessageParity, deadlineRenewalParity,
          shutdownPhase, shutdownSnapshot, readyWitness, admissionWitness>>

TickDeadline(request) ==
    /\ request \in Requests
    /\ DeadlineEligible(request)
    /\ deadlineRemaining[request] > 0
    /\ deadlineRemaining' =
        [deadlineRemaining EXCEPT ![request] = @ - 1]
    /\ UNCHANGED
        <<hostHandshakeSucceeds, inboundHandshakeValid,
          claimsAuthentication, connection, readLoop, admissionOpen,
          hostHandshake, pluginHandshake, writeOwner, requestState,
          requestOutcome, responseReceived, progressOpen,
          progressMessageParity, deadlineRenewalParity, completionCount,
          shutdownPhase, shutdownSnapshot, readyWitness, admissionWitness>>

TimeoutRequest(request) ==
    /\ request \in Requests
    /\ DeadlineEligible(request)
    /\ deadlineRemaining[request] = 0
    /\ LET preemptsWriter ==
            requestState[request] = "Writing"
            /\ writeOwner = request
           terminateConnection ==
            preemptsWriter
            /\ WriteTimeoutMode # "ReleasesWriter"
           peerOutcome ==
            IF WriteTimeoutMode = "MisclassifiesPeers"
            THEN "TimedOut"
            ELSE "ConnectionClosed"
       IN
        /\ requestState' =
            IF terminateConnection
            THEN
                [candidate \in Requests |->
                    IF LiveRequest(candidate)
                    THEN "Done"
                    ELSE requestState[candidate]]
            ELSE [requestState EXCEPT ![request] = "Done"]
        /\ requestOutcome' =
            IF terminateConnection
            THEN
                [candidate \in Requests |->
                    IF LiveRequest(candidate)
                    THEN
                        IF candidate = request
                        THEN "TimedOut"
                        ELSE peerOutcome
                    ELSE requestOutcome[candidate]]
            ELSE [requestOutcome EXCEPT ![request] = "TimedOut"]
        /\ deadlineRemaining' =
            IF terminateConnection
            THEN
                [candidate \in Requests |->
                    IF LiveRequest(candidate)
                    THEN 0
                    ELSE deadlineRemaining[candidate]]
            ELSE deadlineRemaining
        /\ completionCount' =
            IF terminateConnection
            THEN
                [candidate \in Requests |->
                    IF LiveRequest(candidate)
                    THEN completionCount[candidate] + 1
                    ELSE completionCount[candidate]]
            ELSE [completionCount EXCEPT ![request] = @ + 1]
        /\ connection' =
            IF terminateConnection
            THEN "Closed"
            ELSE connection
        /\ readLoop' =
            IF terminateConnection
            THEN "Stopped"
            ELSE readLoop
        /\ admissionOpen' =
            IF terminateConnection
            THEN FALSE
            ELSE admissionOpen
        /\ writeOwner' =
            IF preemptsWriter
            THEN NoWriter
            ELSE writeOwner
        /\ shutdownPhase' =
            IF terminateConnection
            THEN "Settled"
            ELSE shutdownPhase
        /\ shutdownSnapshot' =
            IF terminateConnection
            THEN LiveRequests
            ELSE shutdownSnapshot
    /\ UNCHANGED
        <<hostHandshakeSucceeds, inboundHandshakeValid,
          claimsAuthentication, hostHandshake, pluginHandshake,
          responseReceived, progressOpen, progressMessageParity,
          deadlineRenewalParity, readyWitness, admissionWitness>>

CancelCaller(request) ==
    /\ request \in Requests
    /\ LiveRequest(request)
    /\ LET preemptsWriter ==
            requestState[request] = "Writing"
            /\ writeOwner = request
           terminateConnection ==
            preemptsWriter
            /\ WriteTimeoutMode # "ReleasesWriter"
           peerOutcome ==
            IF WriteTimeoutMode = "MisclassifiesPeers"
            THEN "TimedOut"
            ELSE "ConnectionClosed"
       IN
        /\ requestState' =
            IF terminateConnection
            THEN
                [candidate \in Requests |->
                    IF LiveRequest(candidate)
                    THEN "Done"
                    ELSE requestState[candidate]]
            ELSE [requestState EXCEPT ![request] = "Done"]
        /\ requestOutcome' =
            IF terminateConnection
            THEN
                [candidate \in Requests |->
                    IF LiveRequest(candidate)
                    THEN
                        IF candidate = request
                        THEN "CallerCanceled"
                        ELSE peerOutcome
                    ELSE requestOutcome[candidate]]
            ELSE [requestOutcome EXCEPT ![request] = "CallerCanceled"]
        /\ deadlineRemaining' =
            IF terminateConnection
            THEN
                [candidate \in Requests |->
                    IF LiveRequest(candidate)
                    THEN 0
                    ELSE deadlineRemaining[candidate]]
            ELSE [deadlineRemaining EXCEPT ![request] = 0]
        /\ completionCount' =
            IF terminateConnection
            THEN
                [candidate \in Requests |->
                    IF LiveRequest(candidate)
                    THEN completionCount[candidate] + 1
                    ELSE completionCount[candidate]]
            ELSE [completionCount EXCEPT ![request] = @ + 1]
        /\ connection' =
            IF terminateConnection
            THEN "Closed"
            ELSE connection
        /\ readLoop' =
            IF terminateConnection
            THEN "Stopped"
            ELSE readLoop
        /\ admissionOpen' =
            IF terminateConnection
            THEN FALSE
            ELSE admissionOpen
        /\ writeOwner' =
            IF preemptsWriter
            THEN NoWriter
            ELSE writeOwner
        /\ shutdownPhase' =
            IF terminateConnection
            THEN "Settled"
            ELSE shutdownPhase
        /\ shutdownSnapshot' =
            IF terminateConnection
            THEN LiveRequests
            ELSE shutdownSnapshot
    /\ UNCHANGED
        <<hostHandshakeSucceeds, inboundHandshakeValid,
          claimsAuthentication, hostHandshake, pluginHandshake,
          responseReceived, progressOpen, progressMessageParity,
          deadlineRenewalParity, readyWitness, admissionWitness>>

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
          deadlineRemaining, responseReceived, progressOpen,
          progressMessageParity, deadlineRenewalParity, completionCount,
          shutdownSnapshot, readyWitness, admissionWitness>>

CaptureShutdownSnapshot ==
    /\ shutdownPhase = "Observed"
    /\ connection # "Closed"
    /\ shutdownPhase' = "Captured"
    /\ shutdownSnapshot' = LiveRequests
    /\ UNCHANGED
        <<hostHandshakeSucceeds, inboundHandshakeValid,
          claimsAuthentication, connection, readLoop, admissionOpen,
          hostHandshake, pluginHandshake, writeOwner, requestState,
          requestOutcome, deadlineRemaining, responseReceived,
          progressOpen, progressMessageParity, deadlineRenewalParity,
          completionCount, readyWitness, admissionWitness>>

SettleShutdownSnapshot ==
    /\ shutdownPhase = "Captured"
    /\ connection # "Closed"
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
          claimsAuthentication, readLoop, hostHandshake, responseReceived,
          progressOpen, progressMessageParity, deadlineRenewalParity,
          shutdownSnapshot, readyWitness, admissionWitness>>

FinishClose ==
    /\ connection = "Closing"
    /\ LiveRequests = {}
    /\ writeOwner = NoWriter
    /\ connection' = "Closed"
    /\ UNCHANGED
        <<hostHandshakeSucceeds, inboundHandshakeValid,
          claimsAuthentication, readLoop, admissionOpen, hostHandshake,
          pluginHandshake, writeOwner, requestState, requestOutcome,
          deadlineRemaining, responseReceived, progressOpen,
          progressMessageParity, deadlineRenewalParity, completionCount,
          shutdownPhase, shutdownSnapshot, readyWitness, admissionWitness>>

Next ==
    \/ DeliverHostHandshake
    \/ DeliverPluginHandshake
    \/ AcquirePluginHandshakeWrite
    \/ DropMalformedInbound
    \/ FinishPluginHandshakeWrite
    \/ FailHostInitialization
    \/ TimeoutInitialization
    \/ ResolveAuthenticationClaim
    \/ \E request \in Requests : BeginRequest(request)
    \/ \E request \in Requests : AcquireRequestWrite(request)
    \/ \E request \in Requests : FinishRequestWrite(request)
    \/ \E messageId \in Requests : ReceiveResponse(messageId)
    \/ \E messageId \in Requests : ReceiveProgress(messageId)
    \/ \E request \in Requests : StopProgress(request)
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
    /\ WF_vars(AcquirePluginHandshakeWrite)
    /\ WF_vars(DropMalformedInbound)
    /\ WF_vars(FailHostInitialization)
    /\ WF_vars(TimeoutInitialization)
    /\ WF_vars(ResolveAuthenticationClaim)
    /\ \A request \in Requests:
        /\ WF_vars(AcquireRequestWrite(request))
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
    /\ responseReceived \in [Requests -> BOOLEAN]
    /\ progressOpen \in [Requests -> BOOLEAN]
    /\ progressMessageParity \in [Requests -> BOOLEAN]
    /\ deadlineRenewalParity \in [Requests -> BOOLEAN]
    /\ completionCount \in [Requests -> 0..2]
    /\ shutdownPhase \in ShutdownPhases
    /\ shutdownSnapshot \in SUBSET Requests
    /\ readyWitness \in BOOLEAN
    /\ admissionWitness \in BOOLEAN

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
        pluginHandshake \in
            {"ValidWriting", "InvalidWriting", "MalformedWriting"}
    /\ \A request \in Requests:
        (writeOwner = request) => requestState[request] = "Writing"

ReadyRequiresSymmetricHandshake ==
    /\ readyWitness
    /\ (connection = "Ready" =>
        /\ hostHandshake = "Succeeded"
        /\ pluginHandshake = "RespondedSuccess"
        /\ claimsAuthentication)

RequestAdmissionHasLiveReceiver ==
    admissionWitness

ResponsesCompleteOnlyTheirRequest ==
    \A request \in Requests:
        responseReceived[request]
            = (requestOutcome[request] = "Success")

ProgressRenewsOnlyItsRequest ==
    progressMessageParity = deadlineRenewalParity

InboundFailureIsContained ==
    pluginHandshake # "Dropped"

ShutdownSettlementIsComplete ==
    shutdownPhase = "Settled" => LiveRequests = {}

ClosedConnectionIsQuiescent ==
    connection = "Closed" =>
        /\ LiveRequests = {}
        /\ writeOwner = NoWriter
        /\ ~admissionOpen
        /\ readLoop = "Stopped"

ClosedConnectionIsAbsorbing ==
    [][connection = "Closed" => connection' = "Closed"]_vars

WriterPreemptionResult(request, expectedOutcome) ==
    /\ connection' = "Closed"
    /\ readLoop' = "Stopped"
    /\ ~admissionOpen'
    /\ writeOwner' = NoWriter
    /\ requestState'[request] = "Done"
    /\ requestOutcome'[request] = expectedOutcome
    /\ \A peer \in Requests:
        IF peer # request /\ LiveRequest(peer)
        THEN
            /\ requestState'[peer] = "Done"
            /\ requestOutcome'[peer] = "ConnectionClosed"
        ELSE TRUE

TimeoutWriterPreemption(request) ==
    /\ requestState[request] = "Writing"
    /\ writeOwner = request
    /\ requestState'[request] = "Done"
    /\ requestOutcome'[request] = "TimedOut"

CancellationWriterPreemption(request) ==
    /\ requestState[request] = "Writing"
    /\ writeOwner = request
    /\ requestState'[request] = "Done"
    /\ requestOutcome'[request] = "CallerCanceled"

WriterPreemptionIsContained ==
    [][
        /\ \A request \in Requests:
            TimeoutWriterPreemption(request) =>
                WriterPreemptionResult(request, "TimedOut")
        /\ \A request \in Requests:
            CancellationWriterPreemption(request) =>
                WriterPreemptionResult(request, "CallerCanceled")
    ]_vars

InitializationEventuallySettles ==
    connection = "Handshaking"
        ~> connection \in {"Ready", "Failed", "Closing", "Closed"}

InboundHandshakeEventuallySettles ==
    InboundHandshakeLive
        ~> (InboundHandshakeSettled
            \/ connection \in {"Failed", "Closing", "Closed"})

MalformedInboundEventuallySettles ==
    pluginHandshake = "MalformedWaitingWrite"
        ~> (InboundHandshakeSettled
            \/ connection \in {"Failed", "Closing", "Closed"})

EveryRequestSettlesAfterProgressStops ==
    \A request \in Requests:
        (LiveRequest(request) /\ ~progressOpen[request])
            ~> requestState[request] = "Done"

EveryAdmittedRequestSettles ==
    \A request \in Requests:
        requestState[request] # "Unused"
            ~> requestState[request] = "Done"

ObservedShutdownEventuallyCloses ==
    shutdownPhase # "None" ~> connection = "Closed"

=============================================================================
