-------------------- MODULE InspectWebWorkerProbeMarks ---------------------
(***************************************************************************)
(* Finite per-command mark model for the worker-runtime probe seam.         *)
(*                                                                         *)
(* Two commands span two probe generations. authorityMark is the required  *)
(* immutable-per-command association after exact-register discharge;       *)
(* implementationMark is the value used by response classification.        *)
(***************************************************************************)
EXTENDS FiniteSets, Naturals, TLC

CONSTANTS
    CommandA,
    CommandB,
    MaxProbeSequence,
    MUTATION_GLOBAL_REMARK

Commands == {CommandA, CommandB}

NotPosted == "NotPosted"
Pending == "Pending"
Responded == "Responded"
CommandStates == {NotPosted, Pending, Responded}

NoCommand == "NoCommand"
FailureCommands == Commands \cup {NoCommand}

ASSUME
    /\ Cardinality(Commands) = 2
    /\ CommandA # CommandB
    /\ MaxProbeSequence = 2

VARIABLES
    probeOutstanding,
    probeSequence,
    nextProbeSequence,
    commandState,
    authorityMark,
    implementationMark,
    protocolFailure,
    failureCommand

vars ==
    <<probeOutstanding,
      probeSequence,
      nextProbeSequence,
      commandState,
      authorityMark,
      implementationMark,
      protocolFailure,
      failureCommand>>

Init ==
    /\ probeOutstanding = FALSE
    /\ probeSequence = 0
    /\ nextProbeSequence = 1
    /\ commandState = [c \in Commands |-> NotPosted]
    /\ authorityMark = [c \in Commands |-> 0]
    /\ implementationMark = [c \in Commands |-> 0]
    /\ protocolFailure = FALSE
    /\ failureCommand = NoCommand

SendProbe ==
    /\ ~probeOutstanding
    /\ ~protocolFailure
    /\ nextProbeSequence <= MaxProbeSequence
    /\ probeOutstanding' = TRUE
    /\ probeSequence' = nextProbeSequence
    /\ nextProbeSequence' = nextProbeSequence + 1
    /\ UNCHANGED
        <<commandState,
          authorityMark,
          implementationMark,
          protocolFailure,
          failureCommand>>

PostCommand(c) ==
    /\ c \in Commands
    /\ commandState[c] = NotPosted
    /\ ~protocolFailure
    /\ commandState' = [commandState EXCEPT ![c] = Pending]
    /\ authorityMark' =
        [authorityMark EXCEPT
            ![c] = IF probeOutstanding THEN probeSequence ELSE 0]
    /\ implementationMark' =
        IF MUTATION_GLOBAL_REMARK /\ probeOutstanding
        THEN
            [d \in Commands |->
                IF d = c \/ commandState[d] = Pending
                THEN probeSequence
                ELSE implementationMark[d]]
        ELSE
            [implementationMark EXCEPT
                ![c] = IF probeOutstanding THEN probeSequence ELSE 0]
    /\ UNCHANGED
        <<probeOutstanding,
          probeSequence,
          nextProbeSequence,
          protocolFailure,
          failureCommand>>

ReceiveProbeAcknowledgment ==
    /\ probeOutstanding
    /\ ~protocolFailure
    /\ probeOutstanding' = FALSE
    /\ probeSequence' = 0
    /\ authorityMark' =
        [c \in Commands |->
            IF authorityMark[c] = probeSequence
            THEN 0
            ELSE authorityMark[c]]
    /\ implementationMark' =
        [c \in Commands |->
            IF implementationMark[c] = probeSequence
            THEN 0
            ELSE implementationMark[c]]
    /\ UNCHANGED
        <<nextProbeSequence,
          commandState,
          protocolFailure,
          failureCommand>>

ReceiveCommandResponse(c) ==
    /\ c \in Commands
    /\ commandState[c] = Pending
    /\ ~protocolFailure
    /\ commandState' = [commandState EXCEPT ![c] = Responded]
    /\ IF /\ probeOutstanding
          /\ implementationMark[c] # 0
          /\ implementationMark[c] = probeSequence
       THEN
           /\ protocolFailure' = TRUE
           /\ failureCommand' = c
       ELSE
           /\ UNCHANGED <<protocolFailure, failureCommand>>
    /\ UNCHANGED
        <<probeOutstanding,
          probeSequence,
          nextProbeSequence,
          authorityMark,
          implementationMark>>

Next ==
    \/ SendProbe
    \/ \E c \in Commands: PostCommand(c)
    \/ ReceiveProbeAcknowledgment
    \/ \E c \in Commands: ReceiveCommandResponse(c)

Spec == Init /\ [][Next]_vars

TypeOK ==
    /\ probeOutstanding \in BOOLEAN
    /\ probeSequence \in 0..MaxProbeSequence
    /\ nextProbeSequence \in 1..(MaxProbeSequence + 1)
    /\ commandState \in [Commands -> CommandStates]
    /\ authorityMark \in [Commands -> 0..MaxProbeSequence]
    /\ implementationMark \in [Commands -> 0..MaxProbeSequence]
    /\ protocolFailure \in BOOLEAN
    /\ failureCommand \in FailureCommands

OneOutstandingProbeSequence ==
    /\ probeOutstanding => probeSequence # 0
    /\ ~probeOutstanding => probeSequence = 0

MarksNameTheOutstandingProbe ==
    \A c \in Commands:
        authorityMark[c] # 0
        =>
        /\ probeOutstanding
        /\ authorityMark[c] = probeSequence

ImplementationUsesPerCommandMarks ==
    implementationMark = authorityMark

ControlResponseFailureHasExactCommandProof ==
    protocolFailure
    =>
    /\ failureCommand \in Commands
    /\ probeOutstanding
    /\ authorityMark[failureCommand] # 0
    /\ authorityMark[failureCommand] = probeSequence

BothCommandsCanBePending ==
    ~(/\ commandState[CommandA] = Pending
      /\ commandState[CommandB] = Pending)

CrossGenerationResponseRaceIsReachable ==
    ~(/\ commandState[CommandA] = Pending
      /\ authorityMark[CommandA] = 0
      /\ probeOutstanding
      /\ probeSequence = 2)

=============================================================================
