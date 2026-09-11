------------------ MODULE InspectWebWorkerClosureIdentity -----------------
(***************************************************************************)
(* Finite exact-closure identity model for the worker runtime.              *)
(*                                                                         *)
(* The lifecycle model owns timing and release. This companion expands the  *)
(* committed closure into exact kind and diagnostic identity so later       *)
(* unexpected faults and worker loss cannot replace either field.           *)
(***************************************************************************)
EXTENDS FiniteSets, TLC

CONSTANT Mutation

NoMutation == "None"
ReplaceFailureKind == "ReplaceFailureKind"
ReplaceDiagnostic == "ReplaceDiagnostic"
CrashReplacesClosure == "CrashReplacesClosure"
RewriteOutcome == "RewriteOutcome"
Mutations ==
    {NoMutation,
     ReplaceFailureKind,
     ReplaceDiagnostic,
     CrashReplacesClosure,
     RewriteOutcome}

Live == "Live"
Draining == "Draining"
Closed == "Closed"
States == {Live, Draining, Closed}

NoClosure == "NoClosure"
PlannedClosure == "PlannedClosure"
StartupFailure == "StartupFailure"
WorkerCrashFailure == "WorkerCrashFailure"
WatchdogFailure == "WatchdogFailure"
ProtocolFailure == "ProtocolFailure"
ControlResponseFailure == "ControlResponseFailure"
ProbeExhaustionFailure == "ProbeExhaustionFailure"
WorkerMessageFailure == "WorkerMessageFailure"
WorkerDeclaredFailure == "WorkerDeclaredFailure"
ClosureKinds ==
    {NoClosure,
     PlannedClosure,
     StartupFailure,
     WorkerCrashFailure,
     WatchdogFailure,
     ProtocolFailure,
     ControlResponseFailure,
     ProbeExhaustionFailure,
     WorkerMessageFailure,
     WorkerDeclaredFailure}
UnexpectedClosureKinds ==
    {StartupFailure,
     WorkerCrashFailure,
     WatchdogFailure,
     ProtocolFailure,
     ControlResponseFailure,
     ProbeExhaustionFailure,
     WorkerMessageFailure,
     WorkerDeclaredFailure}

NoDiagnostic == "NoDiagnostic"
DiagnosticA == "DiagnosticA"
DiagnosticB == "DiagnosticB"
Diagnostics == {NoDiagnostic, DiagnosticA, DiagnosticB}

NoOutcome == "NoOutcome"
CanceledOutcome == "CanceledOutcome"
FailedOutcome == "FailedOutcome"
Outcomes == {NoOutcome, CanceledOutcome, FailedOutcome}

ASSUME Mutation \in Mutations

VARIABLES
    state,
    closureKind,
    diagnostic,
    outcome,
    laterFaultObserved,
    laterFaultKind,
    crashObserved

vars ==
    <<state,
      closureKind,
      diagnostic,
      outcome,
      laterFaultObserved,
      laterFaultKind,
      crashObserved>>

Init ==
    /\ state = Live
    /\ closureKind = NoClosure
    /\ diagnostic = NoDiagnostic
    /\ outcome = NoOutcome
    /\ laterFaultObserved = FALSE
    /\ laterFaultKind = NoClosure
    /\ crashObserved = FALSE

BeginPlannedClosure ==
    /\ state = Live
    /\ state' = Draining
    /\ closureKind' = PlannedClosure
    /\ diagnostic' = NoDiagnostic
    /\ outcome' = CanceledOutcome
    /\ UNCHANGED <<laterFaultObserved, laterFaultKind, crashObserved>>

BeginUnexpectedClosure(kind) ==
    /\ state = Live
    /\ kind \in UnexpectedClosureKinds
    /\ state' = Draining
    /\ closureKind' = kind
    /\ diagnostic' = DiagnosticA
    /\ outcome' = FailedOutcome
    /\ UNCHANGED <<laterFaultObserved, laterFaultKind, crashObserved>>

LaterFaultDuringDrain(kind) ==
    /\ state = Draining
    /\ ~laterFaultObserved
    /\ kind \in UnexpectedClosureKinds
    /\ kind # closureKind
    /\ laterFaultObserved' = TRUE
    /\ laterFaultKind' = kind
    /\ closureKind' =
        IF Mutation = ReplaceFailureKind
        THEN kind
        ELSE closureKind
    /\ diagnostic' =
        IF Mutation = ReplaceDiagnostic
        THEN DiagnosticB
        ELSE diagnostic
    /\ outcome' =
        IF Mutation = RewriteOutcome
        THEN
            IF outcome = CanceledOutcome
            THEN FailedOutcome
            ELSE CanceledOutcome
        ELSE outcome
    /\ UNCHANGED <<state, crashObserved>>

WorkerCrashDuringDrain ==
    /\ state = Draining
    /\ ~crashObserved
    /\ state' = Closed
    /\ crashObserved' = TRUE
    /\ closureKind' =
        IF Mutation = CrashReplacesClosure
        THEN WorkerCrashFailure
        ELSE closureKind
    /\ diagnostic' =
        IF Mutation = CrashReplacesClosure
        THEN DiagnosticB
        ELSE diagnostic
    /\ UNCHANGED <<outcome, laterFaultObserved, laterFaultKind>>

FinishDraining ==
    /\ state = Draining
    /\ state' = Closed
    /\ UNCHANGED
        <<closureKind,
          diagnostic,
          outcome,
          laterFaultObserved,
          laterFaultKind,
          crashObserved>>

Next ==
    \/ BeginPlannedClosure
    \/ \E kind \in UnexpectedClosureKinds: BeginUnexpectedClosure(kind)
    \/ \E kind \in UnexpectedClosureKinds: LaterFaultDuringDrain(kind)
    \/ WorkerCrashDuringDrain
    \/ FinishDraining

Spec == Init /\ [][Next]_vars

TypeOK ==
    /\ state \in States
    /\ closureKind \in ClosureKinds
    /\ diagnostic \in Diagnostics
    /\ outcome \in Outcomes
    /\ laterFaultObserved \in BOOLEAN
    /\ laterFaultKind \in ClosureKinds
    /\ crashObserved \in BOOLEAN

CommittedClosureIdentityIsStable ==
    [][closureKind # NoClosure
       =>
       /\ closureKind' = closureKind
       /\ diagnostic' = diagnostic]_vars

CommittedClosureOutcomeIsStable ==
    [][closureKind # NoClosure => outcome' = outcome]_vars

ClosureIdentityDeterminesOutcome ==
    /\ closureKind = PlannedClosure => outcome = CanceledOutcome
    /\ closureKind \in UnexpectedClosureKinds => outcome = FailedOutcome

CrashPreservesCommittedClosure ==
    crashObserved
    =>
    /\ state = Closed
    /\ closureKind # NoClosure

LaterFaultAfterDifferentFailureIsReachable ==
    ~(laterFaultObserved
      /\ closureKind \in UnexpectedClosureKinds
      /\ laterFaultKind \in UnexpectedClosureKinds
      /\ laterFaultKind # closureKind)

=============================================================================
