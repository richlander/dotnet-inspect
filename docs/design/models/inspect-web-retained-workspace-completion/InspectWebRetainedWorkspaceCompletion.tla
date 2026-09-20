------------- MODULE InspectWebRetainedWorkspaceCompletion -------------
EXTENDS FiniteSets, Naturals, TLC

CONSTANTS Fault, CoordinatorFault

ASSUME Fault \in {
    "None",
    "F01PrematureRelease",
    "F05StaleCompatibilityRetirement",
    "F06DeleteBypassesAcceptedExclusion",
    "F17RollbackRevivesSoleActive"
}

ASSUME CoordinatorFault \in {
    "None",
    "PostCutoverAdmission",
    "EarlyClose",
    "StaleCandidatePublish",
    "TransferAuthority",
    "ForgetFailure",
    "NeverSettle",
    "BypassCandidateSettlement",
    "ReadyWithConstruction",
    "PrematureCoordinatorClose",
    "PostCloseAdmission",
    "ReviveAfterClose"
}

Definitions == 1..2
Transitions == 1..2
NoDefinition == 0
NoTransition == 0
MaxIntent == 2
MaxOrdinal == 2
InitialRealization == 1
InitialDefinition == 1

TransitionKinds == {"None", "Managed", "Compatibility", "Delete"}
TransitionStates == {
    "Unused",
    "WaitingCandidate",
    "Preparing",
    "Completing",
    "Prepared",
    "Accepted",
    "CancelledPendingSettlement",
    "SupersededPendingSettlement",
    "Rejected",
    "Released"
}
ManagedOutcomes == {
    "None",
    "Pending",
    "ManagedSucceeded",
    "NoCutoverFailed",
    "RetirementPending",
    "RetiredSucceeded",
    "RetiredFailed"
}
ConsumerOutcomes == {"None", "Pending", "Succeeded", "Failed"}
TransportStates == {"None", "Known", "Unknown"}
PresentationKinds == {"None", "Managed", "Compatibility"}
CompletionWitnesses == {
    "CancellationBeforeAcceptance",
    "CancellationBeforeAcceptanceSettled",
    "RejectionBeforeAcceptance",
    "StalePreparationCompletionIgnored",
    "RequestRejectedWhileAccepted",
    "NoCutoverFailureCompletion",
    "AcceptedOperationSuperseded",
    "ManagedCutover",
    "CompatibilityRetirement",
    "SoleDeletionRetirement",
    "StaleCompatibilityRetiredNewer",
    "CancellationAfterAcceptanceOwned",
    "TransportOutcomeUnknown",
    "PostCutoverFailure",
    "UnknownPostCutoverFailure",
    "PrematureReleaseBeforePresentation",
    "ManagedReleasedBeforePredecessorSettlement",
    "CompatibilityCompletion",
    "CompatibilityCompletionBeforeRetirementFailure",
    "CompatibilityRetirementFailureBeforeCompletion",
    "SoleDeletionCompletion",
    "OldCompletionIgnored",
    "RetiredSoleActiveRevived"
}

NoAuthority == <<"None", 0>>
EffectAuthority(t) == <<"Effect", t>>
NoPosting ==
    [realization |-> 0, ordinal |-> 0, authority |-> NoAuthority]
Posting(t, r, ordinal) ==
    [realization |-> r,
     ordinal |-> ordinal,
     authority |-> EffectAuthority(t)]
PostingValues ==
    {NoPosting}
    \cup {Posting(t, r, ordinal) :
            t \in Transitions,
            r \in 1..3,
            ordinal \in 1..MaxOrdinal}

EmptyTransition ==
    [state |-> "Unused",
     kind |-> "None",
     intent |-> 0,
     candidate |-> 0,
     incumbent |-> 0,
     incumbentDefinition |-> NoDefinition,
     targetDefinition |-> NoDefinition,
     outcome |-> "None",
     consumer |-> "None",
     consumerTransition |-> NoTransition,
     posting |-> NoPosting,
     consumerAssociation |-> NoPosting,
     transport |-> "None",
     cancelled |-> FALSE,
     released |-> FALSE]

VARIABLES
    coordinatorState,
    coordinatorCloseTargets,
    coordinatorPhase,
    coordinatorActive,
    coordinatorCandidate,
    coordinatorPendingCandidate,
    coordinatorCandidateBarrier,
    coordinatorAdmissionOpen,
    coordinatorConstructionAdmissionOpen,
    coordinatorConstructionRealization,
    coordinatorConstructionAdmittedWhileCurrent,
    coordinatorOperationRealization,
    coordinatorOperationDefinition,
    coordinatorAdmittedWhileSelected,
    coordinatorCloseRequested,
    coordinatorFailures,
    coordinatorWitnesses,
    bootstrapState,
    realizationDefinition,
    selectedDefinition,
    presentationKind,
    presentedRealization,
    nextOrdinal,
    latestIntent,
    acceptedTransition,
    transitions,
    visibleFailures,
    witnesses

Coordinator ==
    INSTANCE WorkspaceRealizationCutover WITH
        Fault <- CoordinatorFault,
        coordinatorState <- coordinatorState,
        closeTargets <- coordinatorCloseTargets,
        phase <- coordinatorPhase,
        active <- coordinatorActive,
        candidate <- coordinatorCandidate,
        pendingCandidate <- coordinatorPendingCandidate,
        candidateBarrier <- coordinatorCandidateBarrier,
        admissionOpen <- coordinatorAdmissionOpen,
        constructionAdmissionOpen <-
            coordinatorConstructionAdmissionOpen,
        constructionRealization <- coordinatorConstructionRealization,
        constructionAdmittedWhileCurrent <-
            coordinatorConstructionAdmittedWhileCurrent,
        operationRealization <- coordinatorOperationRealization,
        operationDefinition <- coordinatorOperationDefinition,
        admittedWhileSelected <- coordinatorAdmittedWhileSelected,
        closeRequested <- coordinatorCloseRequested,
        failures <- coordinatorFailures,
        witnesses <- coordinatorWitnesses

Realizations == Coordinator!Realizations
NoRealization == Coordinator!NoRealization

coordinatorVars == <<
    coordinatorState,
    coordinatorCloseTargets,
    coordinatorPhase,
    coordinatorActive,
    coordinatorCandidate,
    coordinatorPendingCandidate,
    coordinatorCandidateBarrier,
    coordinatorAdmissionOpen,
    coordinatorConstructionAdmissionOpen,
    coordinatorConstructionRealization,
    coordinatorConstructionAdmittedWhileCurrent,
    coordinatorOperationRealization,
    coordinatorOperationDefinition,
    coordinatorAdmittedWhileSelected,
    coordinatorCloseRequested,
    coordinatorFailures,
    coordinatorWitnesses
>>

completionVars == <<
    bootstrapState,
    realizationDefinition,
    selectedDefinition,
    presentationKind,
    presentedRealization,
    nextOrdinal,
    latestIntent,
    acceptedTransition,
    transitions,
    visibleFailures,
    witnesses
>>

vars == <<
    coordinatorState,
    coordinatorCloseTargets,
    coordinatorPhase,
    coordinatorActive,
    coordinatorCandidate,
    coordinatorPendingCandidate,
    coordinatorCandidateBarrier,
    coordinatorAdmissionOpen,
    coordinatorConstructionAdmissionOpen,
    coordinatorConstructionRealization,
    coordinatorConstructionAdmittedWhileCurrent,
    coordinatorOperationRealization,
    coordinatorOperationDefinition,
    coordinatorAdmittedWhileSelected,
    coordinatorCloseRequested,
    coordinatorFailures,
    coordinatorWitnesses,
    bootstrapState,
    realizationDefinition,
    selectedDefinition,
    presentationKind,
    presentedRealization,
    nextOrdinal,
    latestIntent,
    acceptedTransition,
    transitions,
    visibleFailures,
    witnesses
>>

CoordinatorUnchanged ==
    UNCHANGED coordinatorVars

CompletionUnchanged ==
    UNCHANGED completionVars

NextTransition ==
    IF transitions[1].state = "Unused"
    THEN 1
    ELSE IF transitions[2].state = "Unused"
         THEN 2
         ELSE NoTransition

CoordinatorCandidateAbsent ==
    coordinatorCandidate = NoRealization
    /\ coordinatorPendingCandidate = NoRealization

OwnerTerminal(r) ==
    IF r = NoRealization
    THEN TRUE
    ELSE coordinatorPhase[r] \in {"Settled", "Failed"}

TerminalManagedOutcome(t) ==
    transitions[t].outcome \in {
        "ManagedSucceeded",
        "NoCutoverFailed",
        "RetiredSucceeded",
        "RetiredFailed"
    }

RequiredConsumerAssociation(t) ==
    IF transitions[t].outcome = "ManagedSucceeded"
    THEN transitions[t].posting
    ELSE NoPosting

MatchingConsumerTerminal(t) ==
    /\ transitions[t].consumer \in {"Succeeded", "Failed"}
    /\ transitions[t].consumerTransition = t
    /\ transitions[t].consumerAssociation =
        RequiredConsumerAssociation(t)

RequiredManagedSettlement(t) ==
    CASE transitions[t].outcome = "ManagedSucceeded" -> TRUE
      [] transitions[t].outcome = "NoCutoverFailed" ->
            OwnerTerminal(transitions[t].candidate)
      [] transitions[t].outcome
            \in {"RetiredSucceeded", "RetiredFailed"} ->
            coordinatorState = "Closed"
      [] OTHER -> FALSE

CompletionReady(t) ==
    /\ TerminalManagedOutcome(t)
    /\ MatchingConsumerTerminal(t)
    /\ RequiredManagedSettlement(t)

Init ==
    /\ Coordinator!Init
    /\ bootstrapState = "None"
    /\ realizationDefinition =
        [r \in Realizations |-> NoDefinition]
    /\ selectedDefinition = NoDefinition
    /\ presentationKind = "None"
    /\ presentedRealization = NoRealization
    /\ nextOrdinal = 0
    /\ latestIntent = 0
    /\ acceptedTransition = NoTransition
    /\ transitions = [t \in Transitions |-> EmptyTransition]
    /\ visibleFailures = {}
    /\ witnesses = {}

BeginBootstrap ==
    /\ bootstrapState = "None"
    /\ Coordinator!BeginCandidate(InitialRealization)
    /\ bootstrapState' = "Preparing"
    /\ realizationDefinition' =
        [realizationDefinition EXCEPT
            ![InitialRealization] = InitialDefinition]
    /\ UNCHANGED <<
        selectedDefinition,
        presentationKind,
        presentedRealization,
        nextOrdinal,
        latestIntent,
        acceptedTransition,
        transitions,
        visibleFailures,
        witnesses
        >>

CompleteBootstrap ==
    /\ bootstrapState = "Preparing"
    /\ Coordinator!CompleteCandidate(InitialRealization)
    /\ bootstrapState' = "Completing"
    /\ UNCHANGED <<
        realizationDefinition,
        selectedDefinition,
        presentationKind,
        presentedRealization,
        nextOrdinal,
        latestIntent,
        acceptedTransition,
        transitions,
        visibleFailures,
        witnesses
        >>

ReadyBootstrap ==
    /\ bootstrapState = "Completing"
    /\ Coordinator!ReadyCandidate(InitialRealization)
    /\ bootstrapState' = "Ready"
    /\ UNCHANGED <<
        realizationDefinition,
        selectedDefinition,
        presentationKind,
        presentedRealization,
        nextOrdinal,
        latestIntent,
        acceptedTransition,
        transitions,
        visibleFailures,
        witnesses
        >>

CutOverBootstrap ==
    /\ bootstrapState = "Ready"
    /\ Coordinator!CutOver(InitialRealization)
    /\ bootstrapState' = "Done"
    /\ selectedDefinition' = InitialDefinition
    /\ presentationKind' = "Managed"
    /\ presentedRealization' = InitialRealization
    /\ UNCHANGED <<
        realizationDefinition,
        nextOrdinal,
        latestIntent,
        acceptedTransition,
        transitions,
        visibleFailures,
        witnesses
        >>

StartManagedPreparation(t, r, d) ==
    /\ bootstrapState = "Done"
    /\ coordinatorState = "Open"
    /\ acceptedTransition = NoTransition
    /\ t = NextTransition
    /\ t # NoTransition
    /\ latestIntent < MaxIntent
    /\ r \in Realizations
    /\ coordinatorPhase[r] = "Unused"
    /\ d \in Definitions \ {selectedDefinition}
    /\ Coordinator!BeginCandidate(r)
    /\ latestIntent' = latestIntent + 1
    /\ realizationDefinition' =
        [realizationDefinition EXCEPT ![r] = d]
    /\ transitions' =
        [transitions EXCEPT
            ![t].state = "Preparing",
            ![t].kind = "Managed",
            ![t].intent = latestIntent + 1,
            ![t].candidate = r,
            ![t].incumbent = coordinatorActive,
            ![t].incumbentDefinition = selectedDefinition,
            ![t].targetDefinition = d,
            ![t].outcome = "None",
            ![t].consumer = "None",
            ![t].transport = "None"]
    /\ UNCHANGED <<
        bootstrapState,
        selectedDefinition,
        presentationKind,
        presentedRealization,
        nextOrdinal,
        acceptedTransition,
        visibleFailures,
        witnesses
        >>

StartNonManagedPreparation(t, kind) ==
    /\ bootstrapState = "Done"
    /\ coordinatorState = "Open"
    /\ acceptedTransition = NoTransition
    /\ CoordinatorCandidateAbsent
    /\ t = NextTransition
    /\ t # NoTransition
    /\ kind \in {"Compatibility", "Delete"}
    /\ coordinatorActive # NoRealization
    /\ latestIntent < MaxIntent
    /\ latestIntent' = latestIntent + 1
    /\ transitions' =
        [transitions EXCEPT
            ![t].state = "Prepared",
            ![t].kind = kind,
            ![t].intent = latestIntent + 1,
            ![t].candidate = NoRealization,
            ![t].incumbent = coordinatorActive,
            ![t].incumbentDefinition = selectedDefinition,
            ![t].targetDefinition = NoDefinition,
            ![t].outcome = "None",
            ![t].consumer = "None",
            ![t].transport = "None"]
    /\ UNCHANGED <<
        bootstrapState,
        realizationDefinition,
        selectedDefinition,
        presentationKind,
        presentedRealization,
        nextOrdinal,
        acceptedTransition,
        visibleFailures,
        witnesses
        >>
    /\ CoordinatorUnchanged

SupersedeManagedPreparation(old, new, replacement, d) ==
    /\ acceptedTransition = NoTransition
    /\ old \in Transitions
    /\ transitions[old].kind = "Managed"
    /\ transitions[old].state
        \in {"Preparing", "Completing", "Prepared"}
    /\ transitions[old].candidate = coordinatorCandidate
    /\ new = NextTransition
    /\ new # NoTransition
    /\ latestIntent < MaxIntent
    /\ replacement \in Realizations
    /\ coordinatorPhase[replacement] = "Unused"
    /\ d \in Definitions \ {selectedDefinition}
    /\ Coordinator!SupersedeCandidate(
        transitions[old].candidate,
        replacement)
    /\ latestIntent' = latestIntent + 1
    /\ realizationDefinition' =
        [realizationDefinition EXCEPT ![replacement] = d]
    /\ transitions' =
        [transitions EXCEPT
            ![old].state = "SupersededPendingSettlement",
            ![new].state = "WaitingCandidate",
            ![new].kind = "Managed",
            ![new].intent = latestIntent + 1,
            ![new].candidate = replacement,
            ![new].incumbent = coordinatorActive,
            ![new].incumbentDefinition = selectedDefinition,
            ![new].targetDefinition = d]
    /\ UNCHANGED <<
        bootstrapState,
        selectedDefinition,
        presentationKind,
        presentedRealization,
        nextOrdinal,
        acceptedTransition,
        visibleFailures,
        witnesses
        >>

AdmitWaitingCandidate(t) ==
    /\ transitions[t].state = "WaitingCandidate"
    /\ transitions[t].candidate = coordinatorPendingCandidate
    /\ Coordinator!AdmitPendingCandidate(transitions[t].candidate)
    /\ transitions' =
        [transitions EXCEPT ![t].state = "Preparing"]
    /\ UNCHANGED <<
        bootstrapState,
        realizationDefinition,
        selectedDefinition,
        presentationKind,
        presentedRealization,
        nextOrdinal,
        latestIntent,
        acceptedTransition,
        visibleFailures,
        witnesses
        >>

CompleteManagedCandidate(t) ==
    /\ transitions[t].state = "Preparing"
    /\ transitions[t].candidate = coordinatorCandidate
    /\ Coordinator!CompleteCandidate(transitions[t].candidate)
    /\ transitions' =
        [transitions EXCEPT ![t].state = "Completing"]
    /\ UNCHANGED <<
        bootstrapState,
        realizationDefinition,
        selectedDefinition,
        presentationKind,
        presentedRealization,
        nextOrdinal,
        latestIntent,
        acceptedTransition,
        visibleFailures,
        witnesses
        >>

ReadyManagedCandidate(t) ==
    /\ transitions[t].state = "Completing"
    /\ transitions[t].candidate = coordinatorCandidate
    /\ Coordinator!ReadyCandidate(transitions[t].candidate)
    /\ transitions' =
        [transitions EXCEPT ![t].state = "Prepared"]
    /\ UNCHANGED <<
        bootstrapState,
        realizationDefinition,
        selectedDefinition,
        presentationKind,
        presentedRealization,
        nextOrdinal,
        latestIntent,
        acceptedTransition,
        visibleFailures,
        witnesses
        >>

CancelManagedBeforeAcceptance(t) ==
    /\ acceptedTransition = NoTransition
    /\ transitions[t].kind = "Managed"
    /\ transitions[t].state
        \in {"Preparing", "Completing", "Prepared"}
    /\ transitions[t].candidate = coordinatorCandidate
    /\ Coordinator!FailCandidate(transitions[t].candidate)
    /\ transitions' =
        [transitions EXCEPT
            ![t].state = "CancelledPendingSettlement"]
    /\ witnesses' = witnesses \cup {"CancellationBeforeAcceptance"}
    /\ UNCHANGED <<
        bootstrapState,
        realizationDefinition,
        selectedDefinition,
        presentationKind,
        presentedRealization,
        nextOrdinal,
        latestIntent,
        acceptedTransition,
        visibleFailures
        >>

RejectNonManagedBeforeAcceptance(t) ==
    /\ acceptedTransition = NoTransition
    /\ transitions[t].kind \in {"Compatibility", "Delete"}
    /\ transitions[t].state = "Prepared"
    /\ transitions[t].intent = latestIntent
    /\ transitions' =
        [transitions EXCEPT ![t].state = "Rejected"]
    /\ witnesses' = witnesses \cup {"RejectionBeforeAcceptance"}
    /\ UNCHANGED <<
        bootstrapState,
        realizationDefinition,
        selectedDefinition,
        presentationKind,
        presentedRealization,
        nextOrdinal,
        latestIntent,
        acceptedTransition,
        visibleFailures
        >>
    /\ CoordinatorUnchanged

FinalizePreAcceptanceRetirement(t) ==
    /\ transitions[t].state
        \in {"CancelledPendingSettlement",
             "SupersededPendingSettlement"}
    /\ OwnerTerminal(transitions[t].candidate)
    /\ transitions' =
        [transitions EXCEPT ![t].state = "Rejected"]
    /\ witnesses' =
        witnesses
        \cup (IF transitions[t].state = "CancelledPendingSettlement"
              THEN {"CancellationBeforeAcceptanceSettled"}
              ELSE {})
    /\ UNCHANGED <<
        bootstrapState,
        realizationDefinition,
        selectedDefinition,
        presentationKind,
        presentedRealization,
        nextOrdinal,
        latestIntent,
        acceptedTransition,
        visibleFailures
        >>
    /\ CoordinatorUnchanged

ObserveStalePreparationCompletion(t) ==
    /\ transitions[t].state
        \in {"SupersededPendingSettlement", "Rejected"}
    /\ transitions[t].intent < latestIntent
    /\ "StalePreparationCompletionIgnored" \notin witnesses
    /\ witnesses' =
        witnesses \cup {"StalePreparationCompletionIgnored"}
    /\ UNCHANGED <<
        bootstrapState,
        realizationDefinition,
        selectedDefinition,
        presentationKind,
        presentedRealization,
        nextOrdinal,
        latestIntent,
        acceptedTransition,
        transitions,
        visibleFailures
        >>
    /\ CoordinatorUnchanged

AcceptPrepared(t) ==
    /\ acceptedTransition = NoTransition
    /\ transitions[t].state = "Prepared"
    /\ transitions[t].intent = latestIntent
    /\ transitions[t].incumbent = coordinatorActive
    /\ transitions[t].incumbentDefinition = selectedDefinition
    /\ IF transitions[t].kind = "Managed"
       THEN /\ transitions[t].candidate = coordinatorCandidate
            /\ coordinatorPhase[transitions[t].candidate] = "Ready"
       ELSE /\ transitions[t].kind \in {"Compatibility", "Delete"}
            /\ CoordinatorCandidateAbsent
            /\ coordinatorActive # NoRealization
    /\ acceptedTransition' = t
    /\ transitions' =
        [transitions EXCEPT
            ![t].state = "Accepted",
            ![t].outcome = "Pending",
            ![t].consumer = "Pending",
            ![t].consumerTransition = NoTransition,
            ![t].consumerAssociation = NoPosting,
            ![t].transport = "Known"]
    /\ UNCHANGED <<
        bootstrapState,
        realizationDefinition,
        selectedDefinition,
        presentationKind,
        presentedRealization,
        nextOrdinal,
        latestIntent,
        visibleFailures,
        witnesses
        >>
    /\ CoordinatorUnchanged

AcceptStaleCompatibility(t) ==
    /\ Fault = "F05StaleCompatibilityRetirement"
    /\ acceptedTransition = NoTransition
    /\ transitions[t].state = "Prepared"
    /\ transitions[t].kind = "Compatibility"
    /\ transitions[t].intent < latestIntent
    /\ transitions[t].incumbent # coordinatorActive
    /\ coordinatorActive # NoRealization
    /\ CoordinatorCandidateAbsent
    /\ acceptedTransition' = t
    /\ transitions' =
        [transitions EXCEPT
            ![t].state = "Accepted",
            ![t].outcome = "Pending",
            ![t].consumer = "Pending",
            ![t].transport = "Known"]
    /\ UNCHANGED <<
        bootstrapState,
        realizationDefinition,
        selectedDefinition,
        presentationKind,
        presentedRealization,
        nextOrdinal,
        latestIntent,
        visibleFailures,
        witnesses
        >>
    /\ CoordinatorUnchanged

AttemptWorkspaceChangeWhileAccepted ==
    /\ acceptedTransition # NoTransition
    /\ NextTransition # NoTransition
    /\ "RequestRejectedWhileAccepted" \notin witnesses
    /\ witnesses' = witnesses \cup {"RequestRejectedWhileAccepted"}
    /\ UNCHANGED <<
        bootstrapState,
        realizationDefinition,
        selectedDefinition,
        presentationKind,
        presentedRealization,
        nextOrdinal,
        latestIntent,
        acceptedTransition,
        transitions,
        visibleFailures
        >>
    /\ CoordinatorUnchanged

BypassAcceptedWithDeletion(t) ==
    /\ Fault = "F06DeleteBypassesAcceptedExclusion"
    /\ acceptedTransition # NoTransition
    /\ t = NextTransition
    /\ t # NoTransition
    /\ latestIntent < MaxIntent
    /\ coordinatorState = "Open"
    /\ coordinatorActive # NoRealization
    /\ Coordinator!CloseCoordinator
    /\ latestIntent' = latestIntent + 1
    /\ acceptedTransition' = t
    /\ transitions' =
        [transitions EXCEPT
            ![t].state = "Accepted",
            ![t].kind = "Delete",
            ![t].intent = latestIntent + 1,
            ![t].incumbent = coordinatorActive,
            ![t].incumbentDefinition = selectedDefinition,
            ![t].outcome = "RetirementPending",
            ![t].consumer = "Pending",
            ![t].transport = "Known"]
    /\ selectedDefinition' = NoDefinition
    /\ presentationKind' = "None"
    /\ presentedRealization' = NoRealization
    /\ witnesses' = witnesses \cup {"AcceptedOperationSuperseded"}
    /\ UNCHANGED <<
        bootstrapState,
        realizationDefinition,
        nextOrdinal,
        visibleFailures
        >>

CutOverAcceptedManaged(t) ==
    /\ acceptedTransition = t
    /\ transitions[t].state = "Accepted"
    /\ transitions[t].kind = "Managed"
    /\ transitions[t].outcome = "Pending"
    /\ transitions[t].candidate = coordinatorCandidate
    /\ nextOrdinal < MaxOrdinal
    /\ Coordinator!CutOver(transitions[t].candidate)
    /\ nextOrdinal' = nextOrdinal + 1
    /\ selectedDefinition' = transitions[t].targetDefinition
    /\ presentationKind' = "None"
    /\ presentedRealization' = NoRealization
    /\ transitions' =
        [transitions EXCEPT
            ![t].outcome = "ManagedSucceeded",
            ![t].posting =
                Posting(t, transitions[t].candidate, nextOrdinal + 1)]
    /\ witnesses' = witnesses \cup {"ManagedCutover"}
    /\ UNCHANGED <<
        bootstrapState,
        realizationDefinition,
        latestIntent,
        acceptedTransition,
        visibleFailures
        >>

FailAcceptedManaged(t) ==
    /\ acceptedTransition = t
    /\ transitions[t].state = "Accepted"
    /\ transitions[t].kind = "Managed"
    /\ transitions[t].outcome = "Pending"
    /\ transitions[t].candidate = coordinatorCandidate
    /\ Coordinator!FailCandidate(transitions[t].candidate)
    /\ transitions' =
        [transitions EXCEPT ![t].outcome = "NoCutoverFailed"]
    /\ visibleFailures' = visibleFailures \cup {t}
    /\ UNCHANGED <<
        bootstrapState,
        realizationDefinition,
        selectedDefinition,
        presentationKind,
        presentedRealization,
        nextOrdinal,
        latestIntent,
        acceptedTransition,
        witnesses
        >>

FailAcceptedBeforeRetirement(t) ==
    /\ acceptedTransition = t
    /\ transitions[t].state = "Accepted"
    /\ transitions[t].kind \in {"Compatibility", "Delete"}
    /\ transitions[t].outcome = "Pending"
    /\ transitions' =
        [transitions EXCEPT ![t].outcome = "NoCutoverFailed"]
    /\ visibleFailures' = visibleFailures \cup {t}
    /\ UNCHANGED <<
        bootstrapState,
        realizationDefinition,
        selectedDefinition,
        presentationKind,
        presentedRealization,
        nextOrdinal,
        latestIntent,
        acceptedTransition,
        witnesses
        >>
    /\ CoordinatorUnchanged

BeginAcceptedRetirement(t) ==
    /\ acceptedTransition = t
    /\ transitions[t].state = "Accepted"
    /\ transitions[t].kind \in {"Compatibility", "Delete"}
    /\ transitions[t].outcome = "Pending"
    /\ coordinatorState = "Open"
    /\ coordinatorActive # NoRealization
    /\ Coordinator!CloseCoordinator
    /\ transitions' =
        [transitions EXCEPT ![t].outcome = "RetirementPending"]
    /\ selectedDefinition' = NoDefinition
    /\ presentationKind' = "None"
    /\ presentedRealization' = NoRealization
    /\ witnesses' =
        witnesses
        \cup (IF transitions[t].kind = "Compatibility"
              THEN {"CompatibilityRetirement"}
              ELSE {"SoleDeletionRetirement"})
        \cup (IF transitions[t].kind = "Compatibility"
                  /\ transitions[t].incumbent # coordinatorActive
              THEN {"StaleCompatibilityRetiredNewer"}
              ELSE {})
    /\ UNCHANGED <<
        bootstrapState,
        realizationDefinition,
        nextOrdinal,
        latestIntent,
        acceptedTransition,
        visibleFailures
        >>

ObserveRetirementOutcome(t) ==
    /\ acceptedTransition = t
    /\ transitions[t].outcome = "RetirementPending"
    /\ coordinatorState = "Closed"
    /\ LET failed ==
            coordinatorCloseTargets \intersect coordinatorFailures # {}
       IN /\ transitions' =
              [transitions EXCEPT
                  ![t].outcome =
                      IF failed
                      THEN "RetiredFailed"
                      ELSE "RetiredSucceeded"]
          /\ presentationKind' =
              IF failed
                  /\ transitions[t].kind = "Compatibility"
              THEN "None"
              ELSE presentationKind
          /\ presentedRealization' =
              IF failed
                  /\ transitions[t].kind = "Compatibility"
              THEN NoRealization
              ELSE presentedRealization
          /\ visibleFailures' =
              IF failed
              THEN visibleFailures \cup {t}
              ELSE visibleFailures
          /\ witnesses' =
              witnesses
              \cup (IF failed
                        /\ transitions[t].kind = "Compatibility"
                        /\ transitions[t].consumer = "Succeeded"
                    THEN {
                        "CompatibilityCompletionBeforeRetirementFailure"
                    }
                    ELSE {})
    /\ UNCHANGED <<
        bootstrapState,
        realizationDefinition,
        selectedDefinition,
        nextOrdinal,
        latestIntent,
        acceptedTransition
        >>
    /\ CoordinatorUnchanged

CancelAfterAcceptance(t) ==
    /\ acceptedTransition = t
    /\ transitions[t].state = "Accepted"
    /\ ~transitions[t].cancelled
    /\ transitions' =
        [transitions EXCEPT ![t].cancelled = TRUE]
    /\ witnesses' =
        witnesses \cup {"CancellationAfterAcceptanceOwned"}
    /\ UNCHANGED <<
        bootstrapState,
        realizationDefinition,
        selectedDefinition,
        presentationKind,
        presentedRealization,
        nextOrdinal,
        latestIntent,
        acceptedTransition,
        visibleFailures
        >>
    /\ CoordinatorUnchanged

LoseTransportResponse(t) ==
    /\ acceptedTransition = t
    /\ transitions[t].transport = "Known"
    /\ transitions[t].outcome
        \in {"ManagedSucceeded", "RetirementPending"}
    /\ transitions' =
        [transitions EXCEPT ![t].transport = "Unknown"]
    /\ witnesses' = witnesses \cup {"TransportOutcomeUnknown"}
    /\ UNCHANGED <<
        bootstrapState,
        realizationDefinition,
        selectedDefinition,
        presentationKind,
        presentedRealization,
        nextOrdinal,
        latestIntent,
        acceptedTransition,
        visibleFailures
        >>
    /\ CoordinatorUnchanged

ConsumerCanComplete(t) ==
    /\ transitions[t].outcome
        \in {"ManagedSucceeded",
             "NoCutoverFailed",
             "RetirementPending",
             "RetiredSucceeded",
             "RetiredFailed"}

CompleteConsumer(t, result) ==
    /\ acceptedTransition = t
    /\ transitions[t].consumer = "Pending"
    /\ ConsumerCanComplete(t)
    /\ result \in {"Succeeded", "Failed"}
    /\ transitions[t].transport = "Unknown" => result = "Failed"
    /\ transitions' =
        [transitions EXCEPT
            ![t].consumer = result,
            ![t].consumerTransition = t,
            ![t].consumerAssociation =
                RequiredConsumerAssociation(t)]
    /\ presentationKind' =
        IF transitions[t].outcome = "NoCutoverFailed"
        THEN presentationKind
        ELSE IF result = "Succeeded"
                    /\ transitions[t].outcome = "ManagedSucceeded"
             THEN "Managed"
             ELSE IF result = "Succeeded"
                         /\ transitions[t].kind = "Compatibility"
                         /\ transitions[t].outcome
                            \in {"RetirementPending", "RetiredSucceeded"}
                  THEN "Compatibility"
                  ELSE "None"
    /\ presentedRealization' =
        IF transitions[t].outcome = "NoCutoverFailed"
        THEN presentedRealization
        ELSE IF result = "Succeeded"
                    /\ transitions[t].outcome = "ManagedSucceeded"
             THEN transitions[t].candidate
             ELSE NoRealization
    /\ visibleFailures' =
        IF result = "Failed"
        THEN visibleFailures \cup {t}
        ELSE visibleFailures
    /\ witnesses' =
        witnesses
        \cup (IF result = "Failed"
                  /\ transitions[t].outcome = "ManagedSucceeded"
              THEN {"PostCutoverFailure"}
              ELSE {})
        \cup (IF result = "Failed"
                  /\ transitions[t].outcome = "ManagedSucceeded"
                  /\ transitions[t].transport = "Unknown"
              THEN {"UnknownPostCutoverFailure"}
              ELSE {})
        \cup (IF result = "Succeeded"
                  /\ transitions[t].kind = "Compatibility"
                  /\ transitions[t].outcome = "RetiredFailed"
              THEN {
                  "CompatibilityRetirementFailureBeforeCompletion"
              }
              ELSE {})
    /\ UNCHANGED <<
        bootstrapState,
        realizationDefinition,
        selectedDefinition,
        nextOrdinal,
        latestIntent,
        acceptedTransition
        >>
    /\ CoordinatorUnchanged

ReleaseAccepted(t) ==
    /\ acceptedTransition = t
    /\ transitions[t].state = "Accepted"
    /\ IF Fault = "F01PrematureRelease"
       THEN /\ transitions[t].kind = "Managed"
            /\ transitions[t].outcome = "ManagedSucceeded"
            /\ ~MatchingConsumerTerminal(t)
       ELSE CompletionReady(t)
    /\ acceptedTransition' = NoTransition
    /\ transitions' =
        [transitions EXCEPT
            ![t].state = "Released",
            ![t].released = TRUE]
    /\ witnesses' =
        witnesses
        \cup (IF ~CompletionReady(t)
              THEN {"PrematureReleaseBeforePresentation"}
              ELSE {})
        \cup (IF transitions[t].kind = "Managed"
                  /\ transitions[t].outcome = "ManagedSucceeded"
                  /\ transitions[t].incumbent # NoRealization
                  /\ coordinatorPhase[transitions[t].incumbent] =
                        "Draining"
              THEN {"ManagedReleasedBeforePredecessorSettlement"}
              ELSE {})
        \cup (IF transitions[t].outcome = "NoCutoverFailed"
              THEN {"NoCutoverFailureCompletion"}
              ELSE {})
        \cup (IF transitions[t].kind = "Compatibility"
                  /\ transitions[t].outcome
                        \in {"RetiredSucceeded", "RetiredFailed"}
              THEN {"CompatibilityCompletion"}
              ELSE {})
        \cup (IF transitions[t].kind = "Delete"
                  /\ transitions[t].outcome
                        \in {"RetiredSucceeded", "RetiredFailed"}
              THEN {"SoleDeletionCompletion"}
              ELSE {})
    /\ UNCHANGED <<
        bootstrapState,
        realizationDefinition,
        selectedDefinition,
        presentationKind,
        presentedRealization,
        nextOrdinal,
        latestIntent,
        visibleFailures
        >>
    /\ CoordinatorUnchanged

ObserveOldConsumerCompletion(old) ==
    /\ transitions[old].state = "Released"
    /\ acceptedTransition # NoTransition
    /\ acceptedTransition # old
    /\ "OldCompletionIgnored" \notin witnesses
    /\ witnesses' = witnesses \cup {"OldCompletionIgnored"}
    /\ UNCHANGED <<
        bootstrapState,
        realizationDefinition,
        selectedDefinition,
        presentationKind,
        presentedRealization,
        nextOrdinal,
        latestIntent,
        acceptedTransition,
        transitions,
        visibleFailures
        >>
    /\ CoordinatorUnchanged

RollbackRetiredSoleActive(t) ==
    /\ Fault = "F17RollbackRevivesSoleActive"
    /\ acceptedTransition = t
    /\ transitions[t].kind = "Delete"
    /\ transitions[t].outcome = "RetirementPending"
    /\ transitions[t].transport = "Unknown"
    /\ coordinatorState = "Closed"
    /\ transitions[t].incumbent # NoRealization
    /\ selectedDefinition' = transitions[t].incumbentDefinition
    /\ presentationKind' = "Managed"
    /\ presentedRealization' = transitions[t].incumbent
    /\ witnesses' = witnesses \cup {"RetiredSoleActiveRevived"}
    /\ UNCHANGED <<
        bootstrapState,
        realizationDefinition,
        nextOrdinal,
        latestIntent,
        acceptedTransition,
        transitions,
        visibleFailures
        >>
    /\ CoordinatorUnchanged

RequestCoordinatorRealizationClose(r) ==
    /\ Coordinator!RequestClose(r)
    /\ CompletionUnchanged

SettleCoordinatorRealization(r, result) ==
    /\ Coordinator!Settle(r, result)
    /\ CompletionUnchanged

FinishCoordinatorClose ==
    /\ Coordinator!FinishCoordinatorClose
    /\ CompletionUnchanged

AcceptedOutcome(t) ==
    \/ CutOverAcceptedManaged(t)
    \/ FailAcceptedManaged(t)
    \/ BeginAcceptedRetirement(t)
    \/ FailAcceptedBeforeRetirement(t)

CompleteConsumerAny(t) ==
    \E result \in {"Succeeded", "Failed"} :
        CompleteConsumer(t, result)

SettleAnyCoordinatorRealization(r) ==
    \E result \in {"Succeeded", "Failed"} :
        SettleCoordinatorRealization(r, result)

Next ==
    \/ BeginBootstrap
    \/ CompleteBootstrap
    \/ ReadyBootstrap
    \/ CutOverBootstrap
    \/ AttemptWorkspaceChangeWhileAccepted
    \/ \E t \in Transitions :
        AdmitWaitingCandidate(t)
        \/ CompleteManagedCandidate(t)
        \/ ReadyManagedCandidate(t)
        \/ CancelManagedBeforeAcceptance(t)
        \/ RejectNonManagedBeforeAcceptance(t)
        \/ FinalizePreAcceptanceRetirement(t)
        \/ ObserveStalePreparationCompletion(t)
        \/ AcceptPrepared(t)
        \/ AcceptStaleCompatibility(t)
        \/ CutOverAcceptedManaged(t)
        \/ FailAcceptedManaged(t)
        \/ FailAcceptedBeforeRetirement(t)
        \/ BeginAcceptedRetirement(t)
        \/ ObserveRetirementOutcome(t)
        \/ CancelAfterAcceptance(t)
        \/ LoseTransportResponse(t)
        \/ CompleteConsumerAny(t)
        \/ ReleaseAccepted(t)
        \/ ObserveOldConsumerCompletion(t)
        \/ RollbackRetiredSoleActive(t)
        \/ BypassAcceptedWithDeletion(t)
    \/ \E t \in Transitions,
          r \in Realizations,
          d \in Definitions :
        StartManagedPreparation(t, r, d)
    \/ \E t \in Transitions,
          kind \in {"Compatibility", "Delete"} :
        StartNonManagedPreparation(t, kind)
    \/ \E old, new \in Transitions,
          replacement \in Realizations,
          d \in Definitions :
        SupersedeManagedPreparation(old, new, replacement, d)
    \/ \E r \in Realizations :
        RequestCoordinatorRealizationClose(r)
        \/ SettleAnyCoordinatorRealization(r)
    \/ FinishCoordinatorClose

Spec ==
    Init /\ [][Next]_vars

FairSpec ==
    /\ Spec
    /\ \A r \in Realizations :
        WF_vars(RequestCoordinatorRealizationClose(r))
        /\ WF_vars(SettleAnyCoordinatorRealization(r))
    /\ WF_vars(FinishCoordinatorClose)
    /\ \A t \in Transitions :
        WF_vars(AcceptedOutcome(t))
        /\ WF_vars(ObserveRetirementOutcome(t))
        /\ WF_vars(CompleteConsumerAny(t))
        /\ WF_vars(ReleaseAccepted(t))

TypeOK ==
    /\ bootstrapState \in {"None", "Preparing", "Completing", "Ready", "Done"}
    /\ realizationDefinition \in
        [Realizations -> (Definitions \cup {NoDefinition})]
    /\ selectedDefinition \in Definitions \cup {NoDefinition}
    /\ presentationKind \in PresentationKinds
    /\ presentedRealization \in Realizations \cup {NoRealization}
    /\ nextOrdinal \in 0..MaxOrdinal
    /\ latestIntent \in 0..MaxIntent
    /\ acceptedTransition \in Transitions \cup {NoTransition}
    /\ transitions \in
        [Transitions ->
            [state : TransitionStates,
             kind : TransitionKinds,
             intent : 0..MaxIntent,
             candidate : Realizations \cup {NoRealization},
             incumbent : Realizations \cup {NoRealization},
             incumbentDefinition : Definitions \cup {NoDefinition},
             targetDefinition : Definitions \cup {NoDefinition},
             outcome : ManagedOutcomes,
             consumer : ConsumerOutcomes,
             consumerTransition : Transitions \cup {NoTransition},
             posting : PostingValues,
             consumerAssociation : PostingValues,
             transport : TransportStates,
             cancelled : BOOLEAN,
             released : BOOLEAN]]
    /\ visibleFailures \subseteq Transitions
    /\ witnesses \subseteq CompletionWitnesses

CoordinatorSafety ==
    Coordinator!Safety

ActiveSelectionIsExact ==
    IF coordinatorActive = NoRealization
    THEN selectedDefinition = NoDefinition
    ELSE realizationDefinition[coordinatorActive] = selectedDefinition

ManagedPresentationIsExact ==
    IF presentationKind = "Managed"
    THEN /\ presentedRealization = coordinatorActive
         /\ coordinatorActive # NoRealization
         /\ realizationDefinition[presentedRealization] =
                selectedDefinition
    ELSE presentedRealization = NoRealization

SingleAcceptedTransition ==
    \A t \in Transitions :
        transitions[t].state = "Accepted" <=> acceptedTransition = t

ReleasedTransitionsHaveRequiredCompletion ==
    \A t \in Transitions :
        transitions[t].released => CompletionReady(t)

ConsumerCompletionMatchesTransition ==
    \A t \in Transitions :
        transitions[t].consumer \in {"Succeeded", "Failed"} =>
            /\ transitions[t].consumerTransition = t
            /\ transitions[t].consumerAssociation =
                RequiredConsumerAssociation(t)

TerminalFailureIsVisible ==
    \A t \in Transitions :
        (transitions[t].outcome
            \in {"NoCutoverFailed", "RetiredFailed"}
         \/ transitions[t].consumer = "Failed")
        => t \in visibleFailures

NoCutoverFailurePreservesIncumbent ==
    \A t \in Transitions :
        transitions[t].state = "Accepted"
            /\ transitions[t].outcome = "NoCutoverFailed"
        => /\ coordinatorActive = transitions[t].incumbent
           /\ selectedDefinition =
                transitions[t].incumbentDefinition

ManagedCutoverDoesNotRollback ==
    \A t \in Transitions :
        transitions[t].state = "Accepted"
            /\ transitions[t].outcome = "ManagedSucceeded"
        => /\ coordinatorActive = transitions[t].candidate
           /\ selectedDefinition = transitions[t].targetDefinition

RetirementRemovesManagedSelection ==
    \A t \in Transitions :
        transitions[t].state = "Accepted"
            /\ transitions[t].outcome
                \in {"RetirementPending", "RetiredSucceeded", "RetiredFailed"}
        => /\ coordinatorActive = NoRealization
           /\ selectedDefinition = NoDefinition
           /\ presentationKind # "Managed"

TransitionOwnsCurrentPresentation(t) ==
    /\ transitions[t].intent = latestIntent
    /\ \/ acceptedTransition = t
       \/ /\ acceptedTransition = NoTransition
          /\ transitions[t].state = "Released"

FailedCompatibilityRetirementLeavesNoWorkspace ==
    \A t \in Transitions :
        /\ TransitionOwnsCurrentPresentation(t)
        /\ transitions[t].kind = "Compatibility"
        /\ transitions[t].outcome = "RetiredFailed"
        => /\ presentationKind = "None"
           /\ presentedRealization = NoRealization

CancellationBeforeAcceptancePreservesIncumbent ==
    \A t \in Transitions :
        transitions[t].state = "CancelledPendingSettlement"
            /\ transitions[t].intent = latestIntent
        => /\ coordinatorActive = transitions[t].incumbent
           /\ selectedDefinition = transitions[t].incumbentDefinition

CancellationAfterAcceptancePreservesOwnership ==
    \A t \in Transitions :
        transitions[t].state = "Accepted"
            /\ transitions[t].cancelled
        => acceptedTransition = t

NoPrematureReleaseBeforePresentation ==
    "PrematureReleaseBeforePresentation" \notin witnesses

NoStaleCompatibilityRetirement ==
    "StaleCompatibilityRetiredNewer" \notin witnesses

AcceptedOperationIsNotSuperseded ==
    "AcceptedOperationSuperseded" \notin witnesses

NoRetiredSoleActiveRevival ==
    "RetiredSoleActiveRevived" \notin witnesses

Safety ==
    /\ TypeOK
    /\ CoordinatorSafety
    /\ ActiveSelectionIsExact
    /\ ManagedPresentationIsExact
    /\ SingleAcceptedTransition
    /\ ReleasedTransitionsHaveRequiredCompletion
    /\ ConsumerCompletionMatchesTransition
    /\ TerminalFailureIsVisible
    /\ NoCutoverFailurePreservesIncumbent
    /\ ManagedCutoverDoesNotRollback
    /\ RetirementRemovesManagedSelection
    /\ FailedCompatibilityRetirementLeavesNoWorkspace
    /\ CancellationBeforeAcceptancePreservesIncumbent
    /\ CancellationAfterAcceptancePreservesOwnership
    /\ NoPrematureReleaseBeforePresentation
    /\ NoStaleCompatibilityRetirement
    /\ AcceptedOperationIsNotSuperseded
    /\ NoRetiredSoleActiveRevival

CoordinatorBehaviorRefinesOwner ==
    Coordinator!Spec

CoordinatorDrainageTerminates ==
    Coordinator!DrainageTerminates

CoordinatorCloseTerminates ==
    Coordinator!CoordinatorCloseTerminates

AcceptedCompletionTerminates ==
    \A t \in Transitions :
        acceptedTransition = t ~> transitions[t].state = "Released"

NeverManagedReleasedBeforePredecessorSettlement ==
    "ManagedReleasedBeforePredecessorSettlement" \notin witnesses

NeverCompatibilityCompletion ==
    "CompatibilityCompletion" \notin witnesses

NeverCompatibilityCompletionBeforeRetirementFailure ==
    "CompatibilityCompletionBeforeRetirementFailure" \notin witnesses

NeverCompatibilityRetirementFailureBeforeCompletion ==
    "CompatibilityRetirementFailureBeforeCompletion" \notin witnesses

NeverSoleDeletionCompletion ==
    "SoleDeletionCompletion" \notin witnesses

NeverCancellationBeforeAcceptanceSettled ==
    "CancellationBeforeAcceptanceSettled" \notin witnesses

NeverCancellationAfterAcceptanceOwned ==
    "CancellationAfterAcceptanceOwned" \notin witnesses

NeverAcceptedRequestRejected ==
    "RequestRejectedWhileAccepted" \notin witnesses

NeverNoCutoverFailureCompletion ==
    "NoCutoverFailureCompletion" \notin witnesses

NeverStalePreparationCompletionIgnored ==
    "StalePreparationCompletionIgnored" \notin witnesses

NeverOldCompletionIgnored ==
    "OldCompletionIgnored" \notin witnesses

NeverUnknownPostCutoverFailure ==
    "UnknownPostCutoverFailure" \notin witnesses

=============================================================================
