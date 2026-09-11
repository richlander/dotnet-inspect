--------------------------- MODULE TsJsExportLifecycle ---------------------------
EXTENDS FiniteSets, Naturals, TLC

CONSTANTS
    FacadeA,
    FacadeB,
    CallerA,
    CallerB,
    RootA,
    RootB,
    NoRoot,
    NoEpoch,
    Coordination,
    AllowRuntimeFailure,
    AllowLocalFailure,
    FailableFacade,
    MaxRestarts,
    Mutation

Facades == {FacadeA, FacadeB}
Callers == {CallerA, CallerB}
Roots == {RootA, RootB}

Idle == "Idle"
WaitingRuntime == "WaitingRuntime"
AwaitingRuntime == "AwaitingRuntime"
Acquiring == "Acquiring"
Validating == "Validating"
Ready == "Ready"
Failed == "Failed"

FacadePhases ==
    {Idle, WaitingRuntime, AwaitingRuntime, Acquiring, Validating, Ready, Failed}
ActivePhases == {WaitingRuntime, AwaitingRuntime, Acquiring, Validating}
TerminalPhases == {Ready, Failed}

RuntimeAbsent == "RuntimeAbsent"
RuntimeCreating == "RuntimeCreating"
RuntimeReady == "RuntimeReady"
RuntimeFailed == "RuntimeFailed"
RuntimePhases == {RuntimeAbsent, RuntimeCreating, RuntimeReady, RuntimeFailed}

NoFailure == "NoFailure"
RuntimeCreationFailure == "RuntimeCreationFailure"
ExportAcquisitionFailure == "ExportAcquisitionFailure"
ExportValidationFailure == "ExportValidationFailure"
PeerInvalidation == "PeerInvalidation"
LostReadyState == "LostReadyState"
LostFailedState == "LostFailedState"
FailureKinds ==
    {NoFailure,
     RuntimeCreationFailure,
     ExportAcquisitionFailure,
     ExportValidationFailure,
     PeerInvalidation,
     LostReadyState,
     LostFailedState}

SharedInFlight == "SharedInFlight"
Serialized == "Serialized"

NoMutation == "None"
EarlyPublication == "EarlyPublication"
DuplicateRuntimeCreation == "DuplicateRuntimeCreation"
DuplicateFacadeInitialization == "DuplicateFacadeInitialization"
DuplicateAcquisition == "DuplicateAcquisition"
CrossAssemblyRoot == "CrossAssemblyRoot"
DisposeRuntimeOnFailure == "DisposeRuntimeOnFailure"
FailPeerOnLocalFailure == "FailPeerOnLocalFailure"
InvokeManagedDuringInitialization == "InvokeManagedDuringInitialization"
InvokeManagedOnReadyTransition == "InvokeManagedOnReadyTransition"
LoseReadyWithoutRestart == "LoseReadyWithoutRestart"
LoseFailureWithoutRestart == "LoseFailureWithoutRestart"
Mutations ==
    {NoMutation,
     EarlyPublication,
     DuplicateRuntimeCreation,
     DuplicateFacadeInitialization,
     DuplicateAcquisition,
     CrossAssemblyRoot,
     DisposeRuntimeOnFailure,
     FailPeerOnLocalFailure,
     InvokeManagedDuringInitialization,
     InvokeManagedOnReadyTransition,
     LoseReadyWithoutRestart,
     LoseFailureWithoutRestart}

ASSUME
    /\ Cardinality(Facades) = 2
    /\ Cardinality(Callers) = 2
    /\ Cardinality(Roots) = 2
    /\ NoRoot \notin Roots
    /\ NoEpoch \notin Nat
    /\ Coordination \in {SharedInFlight, Serialized}
    /\ AllowRuntimeFailure \in BOOLEAN
    /\ AllowLocalFailure \in BOOLEAN
    /\ FailableFacade \in Facades
    /\ MaxRestarts \in Nat
    /\ Mutation \in Mutations

ExpectedRoot(f) == IF f = FacadeA THEN RootA ELSE RootB
OtherFacade(f) == IF f = FacadeA THEN FacadeB ELSE FacadeA

VARIABLES
    phase,
    waiters,
    runtimePhase,
    runtimeStarts,
    initializationStarts,
    acquisitionStarts,
    acquiredRoot,
    publishedRoot,
    failureKind,
    isolationOwed,
    runtimeDisposed,
    managedCallEpoch,
    entryPointCallEpoch,
    realmEpoch,
    previousPhase,
    previousRealmEpoch,
    previousManagedCallEpoch,
    previousEntryPointCallEpoch

vars ==
    <<phase,
      waiters,
      runtimePhase,
      runtimeStarts,
      initializationStarts,
      acquisitionStarts,
      acquiredRoot,
      publishedRoot,
      failureKind,
      isolationOwed,
      runtimeDisposed,
      managedCallEpoch,
      entryPointCallEpoch,
      realmEpoch,
      previousPhase,
      previousRealmEpoch,
      previousManagedCallEpoch,
      previousEntryPointCallEpoch>>

Init ==
    /\ phase = [f \in Facades |-> Idle]
    /\ waiters = [f \in Facades |-> {}]
    /\ runtimePhase = RuntimeAbsent
    /\ runtimeStarts = 0
    /\ initializationStarts = [f \in Facades |-> 0]
    /\ acquisitionStarts = [f \in Facades |-> 0]
    /\ acquiredRoot = [f \in Facades |-> NoRoot]
    /\ publishedRoot = [f \in Facades |-> NoRoot]
    /\ failureKind = [f \in Facades |-> NoFailure]
    /\ isolationOwed = [f \in Facades |-> FALSE]
    /\ runtimeDisposed = FALSE
    /\ managedCallEpoch = [f \in Facades |-> NoEpoch]
    /\ entryPointCallEpoch = [f \in Facades |-> NoEpoch]
    /\ realmEpoch = 0
    /\ previousPhase = [f \in Facades |-> Idle]
    /\ previousRealmEpoch = 0
    /\ previousManagedCallEpoch = [f \in Facades |-> NoEpoch]
    /\ previousEntryPointCallEpoch = [f \in Facades |-> NoEpoch]

CanRequest(f) ==
    \/ Coordination = SharedInFlight
    \/ f = FacadeA
    \/ phase[FacadeA] \in TerminalPhases

RequestInitialization(f, c) ==
    /\ CanRequest(f)
    /\ c \notin waiters[f]
    /\ waiters' = [waiters EXCEPT ![f] = @ \cup {c}]
    /\ phase' =
        IF phase[f] = Idle
        THEN [phase EXCEPT ![f] = WaitingRuntime]
        ELSE phase
    /\ initializationStarts' =
        IF phase[f] = Idle \/ Mutation = DuplicateFacadeInitialization
        THEN [initializationStarts EXCEPT ![f] = @ + 1]
        ELSE initializationStarts
    /\ UNCHANGED
        <<runtimePhase,
          runtimeStarts,
          acquisitionStarts,
          acquiredRoot,
          publishedRoot,
          failureKind,
          isolationOwed,
          runtimeDisposed,
          managedCallEpoch,
          entryPointCallEpoch,
          realmEpoch>>

StartRuntime(f) ==
    /\ phase[f] = WaitingRuntime
    /\ runtimePhase = RuntimeAbsent
    /\ phase' = [phase EXCEPT ![f] = AwaitingRuntime]
    /\ runtimePhase' = RuntimeCreating
    /\ runtimeStarts' = runtimeStarts + 1
    /\ managedCallEpoch' =
        IF Mutation = InvokeManagedDuringInitialization
        THEN [managedCallEpoch EXCEPT ![f] = realmEpoch]
        ELSE managedCallEpoch
    /\ entryPointCallEpoch' =
        IF Mutation = InvokeManagedDuringInitialization
        THEN [entryPointCallEpoch EXCEPT ![f] = realmEpoch]
        ELSE entryPointCallEpoch
    /\ UNCHANGED
        <<waiters,
          initializationStarts,
          acquisitionStarts,
          acquiredRoot,
          publishedRoot,
          failureKind,
          isolationOwed,
          runtimeDisposed,
          realmEpoch>>

JoinRuntimeCreation(f) ==
    /\ Coordination = SharedInFlight
    /\ phase[f] = WaitingRuntime
    /\ runtimePhase = RuntimeCreating
    /\ phase' = [phase EXCEPT ![f] = AwaitingRuntime]
    /\ UNCHANGED
        <<waiters,
          runtimePhase,
          runtimeStarts,
          initializationStarts,
          acquisitionStarts,
          acquiredRoot,
          publishedRoot,
          failureKind,
          isolationOwed,
          runtimeDisposed,
          managedCallEpoch,
          entryPointCallEpoch,
          realmEpoch>>

StartDuplicateRuntime(f) ==
    /\ Mutation = DuplicateRuntimeCreation
    /\ phase[f] = WaitingRuntime
    /\ runtimePhase = RuntimeCreating
    /\ phase' = [phase EXCEPT ![f] = AwaitingRuntime]
    /\ runtimeStarts' = runtimeStarts + 1
    /\ UNCHANGED
        <<waiters,
          runtimePhase,
          initializationStarts,
          acquisitionStarts,
          acquiredRoot,
          publishedRoot,
          failureKind,
          isolationOwed,
          runtimeDisposed,
          managedCallEpoch,
          entryPointCallEpoch,
          realmEpoch>>

CompleteRuntimeSuccess ==
    /\ runtimePhase = RuntimeCreating
    /\ runtimePhase' = RuntimeReady
    /\ UNCHANGED
        <<phase,
          waiters,
          runtimeStarts,
          initializationStarts,
          acquisitionStarts,
          acquiredRoot,
          publishedRoot,
          failureKind,
          isolationOwed,
          runtimeDisposed,
          managedCallEpoch,
          entryPointCallEpoch,
          realmEpoch>>

CompleteRuntimeFailure ==
    /\ AllowRuntimeFailure
    /\ runtimePhase = RuntimeCreating
    /\ runtimePhase' = RuntimeFailed
    /\ UNCHANGED
        <<phase,
          waiters,
          runtimeStarts,
          initializationStarts,
          acquisitionStarts,
          acquiredRoot,
          publishedRoot,
          failureKind,
          isolationOwed,
          runtimeDisposed,
          managedCallEpoch,
          entryPointCallEpoch,
          realmEpoch>>

UseReadyRuntime(f) ==
    /\ phase[f] \in {WaitingRuntime, AwaitingRuntime}
    /\ runtimePhase = RuntimeReady
    /\ phase' = [phase EXCEPT ![f] = Acquiring]
    /\ acquisitionStarts' = [acquisitionStarts EXCEPT ![f] = @ + 1]
    /\ UNCHANGED
        <<waiters,
          runtimePhase,
          runtimeStarts,
          initializationStarts,
          acquiredRoot,
          publishedRoot,
          failureKind,
          isolationOwed,
          runtimeDisposed,
          managedCallEpoch,
          entryPointCallEpoch,
          realmEpoch>>

ObserveFailedRuntime(f) ==
    /\ phase[f] \in {WaitingRuntime, AwaitingRuntime}
    /\ runtimePhase = RuntimeFailed
    /\ phase' = [phase EXCEPT ![f] = Failed]
    /\ failureKind' =
        [failureKind EXCEPT ![f] = RuntimeCreationFailure]
    /\ UNCHANGED
        <<waiters,
          runtimePhase,
          runtimeStarts,
          initializationStarts,
          acquisitionStarts,
          acquiredRoot,
          publishedRoot,
          isolationOwed,
          runtimeDisposed,
          managedCallEpoch,
          entryPointCallEpoch,
          realmEpoch>>

CompleteAcquisitionSuccess(f) ==
    /\ phase[f] = Acquiring
    /\ phase' = [phase EXCEPT ![f] = Validating]
    /\ acquiredRoot' =
        [acquiredRoot EXCEPT
            ![f] =
                IF Mutation = CrossAssemblyRoot
                THEN ExpectedRoot(OtherFacade(f))
                ELSE ExpectedRoot(f)]
    /\ publishedRoot' =
        IF Mutation = EarlyPublication
        THEN [publishedRoot EXCEPT ![f] = ExpectedRoot(f)]
        ELSE publishedRoot
    /\ UNCHANGED
        <<waiters,
          runtimePhase,
          runtimeStarts,
          initializationStarts,
          acquisitionStarts,
          failureKind,
          isolationOwed,
          runtimeDisposed,
          managedCallEpoch,
          entryPointCallEpoch,
          realmEpoch>>

CompleteAcquisitionFailure(f) ==
    /\ AllowLocalFailure
    /\ f = FailableFacade
    /\ phase[f] = Acquiring
    /\ phase' =
        IF Mutation = FailPeerOnLocalFailure
        THEN [phase EXCEPT
                ![f] = Failed,
                ![OtherFacade(f)] = Failed]
        ELSE [phase EXCEPT ![f] = Failed]
    /\ failureKind' =
        IF Mutation = FailPeerOnLocalFailure
        THEN [failureKind EXCEPT
                ![f] = ExportAcquisitionFailure,
                ![OtherFacade(f)] = PeerInvalidation]
        ELSE [failureKind EXCEPT ![f] = ExportAcquisitionFailure]
    /\ isolationOwed' =
        [isolationOwed EXCEPT ![f] = phase[OtherFacade(f)] # Ready]
    /\ publishedRoot' =
        IF Mutation = FailPeerOnLocalFailure
        THEN [publishedRoot EXCEPT ![OtherFacade(f)] = NoRoot]
        ELSE publishedRoot
    /\ runtimePhase' =
        IF Mutation = DisposeRuntimeOnFailure
        THEN RuntimeAbsent
        ELSE runtimePhase
    /\ runtimeDisposed' =
        IF Mutation = DisposeRuntimeOnFailure
        THEN TRUE
        ELSE runtimeDisposed
    /\ UNCHANGED
        <<waiters,
          runtimeStarts,
          initializationStarts,
          acquisitionStarts,
          acquiredRoot,
          managedCallEpoch,
          entryPointCallEpoch,
          realmEpoch>>

StartDuplicateAcquisition(f) ==
    /\ Mutation = DuplicateAcquisition
    /\ phase[f] = Validating
    /\ runtimePhase = RuntimeReady
    /\ phase' = [phase EXCEPT ![f] = Acquiring]
    /\ acquisitionStarts' = [acquisitionStarts EXCEPT ![f] = @ + 1]
    /\ UNCHANGED
        <<waiters,
          runtimePhase,
          runtimeStarts,
          initializationStarts,
          acquiredRoot,
          publishedRoot,
          failureKind,
          isolationOwed,
          runtimeDisposed,
          managedCallEpoch,
          entryPointCallEpoch,
          realmEpoch>>

CompleteValidationSuccess(f) ==
    /\ phase[f] = Validating
    /\ phase' = [phase EXCEPT ![f] = Ready]
    /\ publishedRoot' = [publishedRoot EXCEPT ![f] = acquiredRoot[f]]
    /\ isolationOwed' =
        [isolationOwed EXCEPT ![OtherFacade(f)] = FALSE]
    /\ managedCallEpoch' =
        IF Mutation = InvokeManagedOnReadyTransition
        THEN [managedCallEpoch EXCEPT ![f] = realmEpoch]
        ELSE managedCallEpoch
    /\ entryPointCallEpoch' =
        IF Mutation = InvokeManagedOnReadyTransition
        THEN [entryPointCallEpoch EXCEPT ![f] = realmEpoch]
        ELSE entryPointCallEpoch
    /\ UNCHANGED
        <<waiters,
          runtimePhase,
          runtimeStarts,
          initializationStarts,
          acquisitionStarts,
          acquiredRoot,
          failureKind,
          runtimeDisposed,
          realmEpoch>>

CompleteValidationFailure(f) ==
    /\ AllowLocalFailure
    /\ f = FailableFacade
    /\ phase[f] = Validating
    /\ phase' =
        IF Mutation = FailPeerOnLocalFailure
        THEN [phase EXCEPT
                ![f] = Failed,
                ![OtherFacade(f)] = Failed]
        ELSE [phase EXCEPT ![f] = Failed]
    /\ acquiredRoot' = [acquiredRoot EXCEPT ![f] = NoRoot]
    /\ publishedRoot' =
        IF Mutation = FailPeerOnLocalFailure
        THEN [publishedRoot EXCEPT
                ![f] = NoRoot,
                ![OtherFacade(f)] = NoRoot]
        ELSE [publishedRoot EXCEPT ![f] = NoRoot]
    /\ failureKind' =
        IF Mutation = FailPeerOnLocalFailure
        THEN [failureKind EXCEPT
                ![f] = ExportValidationFailure,
                ![OtherFacade(f)] = PeerInvalidation]
        ELSE [failureKind EXCEPT ![f] = ExportValidationFailure]
    /\ isolationOwed' =
        [isolationOwed EXCEPT ![f] = phase[OtherFacade(f)] # Ready]
    /\ runtimePhase' =
        IF Mutation = DisposeRuntimeOnFailure
        THEN RuntimeAbsent
        ELSE runtimePhase
    /\ runtimeDisposed' =
        IF Mutation = DisposeRuntimeOnFailure
        THEN TRUE
        ELSE runtimeDisposed
    /\ UNCHANGED
        <<waiters,
          runtimeStarts,
          initializationStarts,
          acquisitionStarts,
          managedCallEpoch,
          entryPointCallEpoch,
          realmEpoch>>

CallManagedOperation(f) ==
    /\ phase[f] = Ready
    /\ managedCallEpoch' = [managedCallEpoch EXCEPT ![f] = realmEpoch]
    /\ UNCHANGED
        <<phase,
          waiters,
          runtimePhase,
          runtimeStarts,
          initializationStarts,
          acquisitionStarts,
          acquiredRoot,
          publishedRoot,
          failureKind,
          isolationOwed,
          runtimeDisposed,
          entryPointCallEpoch,
          realmEpoch>>

CallEntryPoint(f) ==
    /\ phase[f] = Ready
    /\ entryPointCallEpoch' = [entryPointCallEpoch EXCEPT ![f] = realmEpoch]
    /\ UNCHANGED
        <<phase,
          waiters,
          runtimePhase,
          runtimeStarts,
          initializationStarts,
          acquisitionStarts,
          acquiredRoot,
          publishedRoot,
          failureKind,
          isolationOwed,
          runtimeDisposed,
          managedCallEpoch,
          realmEpoch>>

LoseReadyState(f) ==
    /\ Mutation = LoseReadyWithoutRestart
    /\ phase[f] = Ready
    /\ phase' = [phase EXCEPT ![f] = Failed]
    /\ failureKind' = [failureKind EXCEPT ![f] = LostReadyState]
    /\ publishedRoot' = [publishedRoot EXCEPT ![f] = NoRoot]
    /\ UNCHANGED
        <<waiters,
          runtimePhase,
          runtimeStarts,
          initializationStarts,
          acquisitionStarts,
          acquiredRoot,
          isolationOwed,
          runtimeDisposed,
          managedCallEpoch,
          entryPointCallEpoch,
          realmEpoch>>

LoseFailedState(f) ==
    /\ Mutation = LoseFailureWithoutRestart
    /\ phase[f] = Failed
    /\ phase' = [phase EXCEPT ![f] = AwaitingRuntime]
    /\ failureKind' = [failureKind EXCEPT ![f] = LostFailedState]
    /\ UNCHANGED
        <<waiters,
          runtimePhase,
          runtimeStarts,
          initializationStarts,
          acquisitionStarts,
          acquiredRoot,
          publishedRoot,
          isolationOwed,
          runtimeDisposed,
          managedCallEpoch,
          entryPointCallEpoch,
          realmEpoch>>

RestartRealm ==
    /\ \A f \in Facades : phase[f] \in TerminalPhases
    /\ \A f \in Facades : ~isolationOwed[f]
    /\ realmEpoch < MaxRestarts
    /\ phase' = [f \in Facades |-> Idle]
    /\ waiters' = [f \in Facades |-> {}]
    /\ runtimePhase' = RuntimeAbsent
    /\ runtimeStarts' = 0
    /\ initializationStarts' = [f \in Facades |-> 0]
    /\ acquisitionStarts' = [f \in Facades |-> 0]
    /\ acquiredRoot' = [f \in Facades |-> NoRoot]
    /\ publishedRoot' = [f \in Facades |-> NoRoot]
    /\ failureKind' = [f \in Facades |-> NoFailure]
    /\ isolationOwed' = [f \in Facades |-> FALSE]
    /\ runtimeDisposed' = FALSE
    /\ managedCallEpoch' = [f \in Facades |-> NoEpoch]
    /\ entryPointCallEpoch' = [f \in Facades |-> NoEpoch]
    /\ realmEpoch' = realmEpoch + 1

FacadeProgress(f) ==
    \/ StartRuntime(f)
    \/ JoinRuntimeCreation(f)
    \/ StartDuplicateRuntime(f)
    \/ UseReadyRuntime(f)
    \/ ObserveFailedRuntime(f)
    \/ CompleteAcquisitionSuccess(f)
    \/ CompleteAcquisitionFailure(f)
    \/ StartDuplicateAcquisition(f)
    \/ CompleteValidationSuccess(f)
    \/ CompleteValidationFailure(f)
    \/ LoseReadyState(f)
    \/ LoseFailedState(f)

RuntimeProgress == CompleteRuntimeSuccess \/ CompleteRuntimeFailure

RawNext ==
    \/ \E f \in Facades, c \in Callers : RequestInitialization(f, c)
    \/ RuntimeProgress
    \/ \E f \in Facades : FacadeProgress(f)
    \/ \E f \in Facades : CallManagedOperation(f)
    \/ \E f \in Facades : CallEntryPoint(f)
    \/ RestartRealm

WithHistory(action) ==
    /\ action
    /\ previousPhase' = phase
    /\ previousRealmEpoch' = realmEpoch
    /\ previousManagedCallEpoch' = managedCallEpoch
    /\ previousEntryPointCallEpoch' = entryPointCallEpoch

Next == WithHistory(RawNext)

Spec ==
    /\ Init
    /\ [][Next]_vars
    /\ \A f \in Facades, c \in Callers :
        WF_vars(WithHistory(RequestInitialization(f, c)))
    /\ WF_vars(WithHistory(RuntimeProgress))
    /\ \A f \in Facades : WF_vars(WithHistory(FacadeProgress(f)))

TypeOK ==
    /\ phase \in [Facades -> FacadePhases]
    /\ waiters \in [Facades -> SUBSET Callers]
    /\ runtimePhase \in RuntimePhases
    /\ runtimeStarts \in Nat
    /\ initializationStarts \in [Facades -> Nat]
    /\ acquisitionStarts \in [Facades -> Nat]
    /\ acquiredRoot \in [Facades -> Roots \cup {NoRoot}]
    /\ publishedRoot \in [Facades -> Roots \cup {NoRoot}]
    /\ failureKind \in [Facades -> FailureKinds]
    /\ isolationOwed \in [Facades -> BOOLEAN]
    /\ runtimeDisposed \in BOOLEAN
    /\ managedCallEpoch \in [Facades -> Nat \cup {NoEpoch}]
    /\ entryPointCallEpoch \in [Facades -> Nat \cup {NoEpoch}]
    /\ realmEpoch \in Nat
    /\ previousPhase \in [Facades -> FacadePhases]
    /\ previousRealmEpoch \in Nat
    /\ previousManagedCallEpoch \in [Facades -> Nat \cup {NoEpoch}]
    /\ previousEntryPointCallEpoch \in [Facades -> Nat \cup {NoEpoch}]

OneSharedRuntimeCreation == runtimeStarts <= 1

OneInitializationPerFacade ==
    \A f \in Facades : initializationStarts[f] <= 1

OneAcquisitionPerFacade ==
    \A f \in Facades : acquisitionStarts[f] <= 1

AcquisitionUsesOwnAssembly ==
    \A f \in Facades :
        acquiredRoot[f] = NoRoot \/ acquiredRoot[f] = ExpectedRoot(f)

PublicationUsesOwnAssembly ==
    \A f \in Facades :
        publishedRoot[f] = NoRoot \/ publishedRoot[f] = ExpectedRoot(f)

PublicationRequiresCompleteValidation ==
    \A f \in Facades : publishedRoot[f] # NoRoot => phase[f] = Ready

FailurePublishesNothing ==
    \A f \in Facades : phase[f] = Failed => publishedRoot[f] = NoRoot

ReadyHasCompleteState ==
    \A f \in Facades :
        phase[f] = Ready =>
            /\ runtimePhase = RuntimeReady
            /\ acquiredRoot[f] = ExpectedRoot(f)
            /\ publishedRoot[f] = ExpectedRoot(f)

TerminalPhasePersistsUntilRestart ==
    \A f \in Facades :
        (previousPhase[f] \in TerminalPhases /\
         previousRealmEpoch = realmEpoch)
        => phase[f] = previousPhase[f]

FacadeNeverDisposesRuntime == ~runtimeDisposed

ManagedCallsRequireReady ==
    \A f \in Facades :
        managedCallEpoch[f] = NoEpoch \/
        (managedCallEpoch[f] = realmEpoch /\ phase[f] = Ready)

EntryPointCallsRequireReady ==
    \A f \in Facades :
        entryPointCallEpoch[f] = NoEpoch \/
        (entryPointCallEpoch[f] = realmEpoch /\ phase[f] = Ready)

InitializationInvokesNoManagedCode ==
    \A f \in Facades :
        phase[f] \in ActivePhases =>
            /\ managedCallEpoch[f] # realmEpoch
            /\ entryPointCallEpoch[f] # realmEpoch

ManagedCallsStartReady ==
    \A f \in Facades :
        ((managedCallEpoch[f] # previousManagedCallEpoch[f]) /\
         managedCallEpoch[f] = realmEpoch)
        => previousPhase[f] = Ready

EntryPointCallsStartReady ==
    \A f \in Facades :
        ((entryPointCallEpoch[f] # previousEntryPointCallEpoch[f]) /\
         entryPointCallEpoch[f] = realmEpoch)
        => previousPhase[f] = Ready

ManagedCodeStartsReady ==
    ManagedCallsStartReady /\ EntryPointCallsStartReady

RequestedEventuallyTerminates ==
    \A f \in Facades :
        (waiters[f] # {} /\ phase[f] \notin TerminalPhases)
        ~> phase[f] \in TerminalPhases

AllFacadesEventuallyReady ==
    []<>(\A f \in Facades : phase[f] = Ready)

LocalFailureIsolation ==
    \A f \in Facades : isolationOwed[f] ~> ~isolationOwed[f]

=============================================================================
