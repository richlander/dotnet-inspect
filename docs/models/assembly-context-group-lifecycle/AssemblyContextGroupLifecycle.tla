----------------- MODULE AssemblyContextGroupLifecycle -----------------
EXTENDS FiniteSets, Integers, TLC

CONSTANTS
    ParticipantCount,
    CallbackCount,
    MaxRetainedImages,
    AllowEarlyRelease,
    AllowEarlySnapshotRelease,
    AllowRejectedRetention,
    AllowSuccessfulPolicyViolation,
    AllowReleasedAsRejected,
    AllowPreReservationFailureCaching,
    AllowExceptionalFailureCaching

GroupIdentity == "AssemblyContextGroup"
NoGroupIdentity == "NoGroup"
NoReleaseResult == "NoReleaseResult"

ASSUME /\ ParticipantCount \in Nat \ {0}
       /\ CallbackCount \in Nat \ {0}
       /\ MaxRetainedImages \in 0..ParticipantCount
       /\ AllowEarlyRelease \in BOOLEAN
       /\ AllowEarlySnapshotRelease \in BOOLEAN
       /\ AllowRejectedRetention \in BOOLEAN
       /\ AllowSuccessfulPolicyViolation \in BOOLEAN
       /\ AllowReleasedAsRejected \in BOOLEAN
       /\ AllowPreReservationFailureCaching \in BOOLEAN
       /\ AllowExceptionalFailureCaching \in BOOLEAN
       /\ GroupIdentity # NoGroupIdentity
       /\ {"Succeeded"} # {}
       /\ NoReleaseResult \notin {"Succeeded"}

Participants == 1..ParticipantCount
Callbacks == 1..CallbackCount

CallbackStates == {"Unused", "Admitted", "Active", "Finalizing", "Done"}
CallbackOutcomes ==
    {"Pending", "Succeeded", "Rejected", "ReleasedFailure", "OpenFailure"}
ParticipantStates ==
    {"Cold", "Opening", "Reserved", "Ready", "Rejected", "Released"}
GroupStates == {"Open", "Disposed", "Released"}
ReleasePhases == {"NotStarted", "Resources", "Snapshots", "Complete"}
ResourceStates == {"Owned", "Released"}

\* Callback targets repeat over the bounded participant set. With three
\* callbacks and two participants, TLC explores both participant-local
\* independence and two callbacks contending for one participant.
Target(callback) ==
    ((callback - 1) % ParticipantCount) + 1

VARIABLES
    callbackState,
    callbackHasView,
    callbackOutcome,
    releaseOnExit,
    participantState,
    openingCallback,
    retainedImages,
    groupState,
    releasePhase,
    resourceState,
    releaseCount,
    requestedGroup,
    completedGroup,
    completionResult,
    admissionWitness,
    quiescenceWitness,
    resourceOrderWitness,
    activeViewWitness,
    completionPolicyWitness,
    releasedAccessWitness,
    exceptionalRollbackWitness

vars ==
    <<callbackState, callbackHasView, callbackOutcome, releaseOnExit,
      participantState, openingCallback, retainedImages, groupState,
      releasePhase, resourceState,
      releaseCount, requestedGroup, completedGroup, completionResult,
      admissionWitness, quiescenceWitness,
      resourceOrderWitness, activeViewWitness, completionPolicyWitness,
      releasedAccessWitness, exceptionalRollbackWitness>>

releaseVars == <<requestedGroup, completedGroup, completionResult>>

PreserveRelease(action) ==
    action /\ UNCHANGED releaseVars

LiveCallbacks ==
    {callback \in Callbacks:
        callbackState[callback] \in {"Admitted", "Active", "Finalizing"}}

GroupRelease ==
    INSTANCE AssemblyContextGroupReleaseLifecycle
        WITH Group <- GroupIdentity,
             NoGroup <- NoGroupIdentity,
             ReleaseResults <- {"Succeeded"},
             NoReleaseResult <- NoReleaseResult,
             requestedGroup <- requestedGroup,
             completedGroup <- completedGroup,
             completionResult <- completionResult

RetainedParticipants ==
    {participant \in Participants:
        participantState[participant] \in {"Reserved", "Ready"}}

Init ==
    /\ callbackState =
        [callback \in Callbacks |-> "Unused"]
    /\ callbackHasView =
        [callback \in Callbacks |-> FALSE]
    /\ callbackOutcome =
        [callback \in Callbacks |-> "Pending"]
    /\ releaseOnExit \in [Callbacks -> BOOLEAN]
    /\ participantState =
        [participant \in Participants |-> "Cold"]
    /\ openingCallback =
        [participant \in Participants |-> 0]
    /\ retainedImages = 0
    /\ groupState = "Open"
    /\ releasePhase = "NotStarted"
    /\ resourceState = "Owned"
    /\ releaseCount = 0
    /\ requestedGroup = NoGroupIdentity
    /\ completedGroup = NoGroupIdentity
    /\ completionResult = NoReleaseResult
    /\ admissionWitness = TRUE
    /\ quiescenceWitness = TRUE
    /\ resourceOrderWitness = TRUE
    /\ activeViewWitness = TRUE
    /\ completionPolicyWitness = TRUE
    /\ releasedAccessWitness = TRUE
    /\ exceptionalRollbackWitness = TRUE
    /\ GroupRelease!Init

AdmitCallback(callback) ==
    /\ callbackState[callback] = "Unused"
    /\ groupState = "Open"
    /\ callbackState' =
        [callbackState EXCEPT ![callback] = "Admitted"]
    /\ admissionWitness' =
        (admissionWitness /\ (groupState = "Open"))
    /\ UNCHANGED
        <<callbackHasView, callbackOutcome, releaseOnExit,
          participantState, openingCallback,
          retainedImages, groupState, releasePhase, resourceState,
          releaseCount, quiescenceWitness, resourceOrderWitness,
          activeViewWitness, completionPolicyWitness,
          releasedAccessWitness, exceptionalRollbackWitness>>

StartOpen(callback) ==
    /\ callbackState[callback] = "Admitted"
    /\ participantState[Target(callback)] = "Cold"
    /\ participantState' =
        [participantState EXCEPT ![Target(callback)] = "Opening"]
    /\ openingCallback' =
        [openingCallback EXCEPT ![Target(callback)] = callback]
    /\ UNCHANGED
        <<callbackState, callbackHasView, callbackOutcome, releaseOnExit,
          retainedImages, groupState, releasePhase, resourceState, releaseCount,
          admissionWitness, quiescenceWitness, resourceOrderWitness,
          activeViewWitness, completionPolicyWitness,
          releasedAccessWitness, exceptionalRollbackWitness>>

ReserveImage(participant) ==
    /\ participantState[participant] = "Opening"
    /\ retainedImages < MaxRetainedImages
    /\ participantState' =
        [participantState EXCEPT ![participant] = "Reserved"]
    /\ retainedImages' = retainedImages + 1
    /\ UNCHANGED
        <<callbackState, callbackHasView, callbackOutcome, releaseOnExit,
          openingCallback,
          groupState, releasePhase, resourceState, releaseCount,
          admissionWitness, quiescenceWitness, resourceOrderWitness,
          activeViewWitness, completionPolicyWitness,
          releasedAccessWitness, exceptionalRollbackWitness>>

RejectForBudget(participant) ==
    /\ participantState[participant] = "Opening"
    /\ retainedImages = MaxRetainedImages
    /\ openingCallback[participant] \in Callbacks
    /\ callbackState' =
        [callbackState EXCEPT
            ![openingCallback[participant]] = "Finalizing"]
    /\ callbackOutcome' =
        [callbackOutcome EXCEPT
            ![openingCallback[participant]] = "Rejected"]
    /\ participantState' =
        [participantState EXCEPT ![participant] = "Rejected"]
    /\ openingCallback' =
        [openingCallback EXCEPT ![participant] = 0]
    /\ UNCHANGED
        <<callbackHasView, releaseOnExit, retainedImages,
          groupState, releasePhase, resourceState,
          releaseCount, admissionWitness, quiescenceWitness,
          resourceOrderWitness, activeViewWitness,
          completionPolicyWitness, releasedAccessWitness,
          exceptionalRollbackWitness>>

FailBeforeReservation(participant) ==
    /\ participantState[participant] = "Opening"
    /\ openingCallback[participant] \in Callbacks
    /\ callbackState' =
        [callbackState EXCEPT
            ![openingCallback[participant]] = "Finalizing"]
    /\ callbackOutcome' =
        [callbackOutcome EXCEPT
            ![openingCallback[participant]] = "OpenFailure"]
    /\ participantState' =
        [participantState EXCEPT
            ![participant] =
                (IF /\ AllowPreReservationFailureCaching
                    /\ retainedImages = MaxRetainedImages
                 THEN "Rejected"
                 ELSE "Cold")]
    /\ openingCallback' =
        [openingCallback EXCEPT ![participant] = 0]
    /\ retainedImages' = retainedImages
    /\ exceptionalRollbackWitness' =
        (exceptionalRollbackWitness
         /\ callbackState'[openingCallback[participant]] = "Finalizing"
         /\ callbackOutcome'[openingCallback[participant]] = "OpenFailure"
         /\ participantState'[participant] = "Cold"
         /\ openingCallback'[participant] = 0
         /\ retainedImages' = retainedImages)
    /\ UNCHANGED
        <<callbackHasView, releaseOnExit, groupState, releasePhase,
          resourceState, releaseCount, admissionWitness,
          quiescenceWitness, resourceOrderWitness, activeViewWitness,
          completionPolicyWitness, releasedAccessWitness>>

PublishImage(participant) ==
    /\ participantState[participant] = "Reserved"
    /\ openingCallback[participant] \in Callbacks
    /\ callbackState' =
        [callbackState EXCEPT
            ![openingCallback[participant]] = "Active"]
    /\ callbackHasView' =
        [callbackHasView EXCEPT
            ![openingCallback[participant]] = TRUE]
    /\ participantState' =
        [participantState EXCEPT ![participant] = "Ready"]
    /\ openingCallback' =
        [openingCallback EXCEPT ![participant] = 0]
    /\ UNCHANGED
        <<callbackOutcome, releaseOnExit,
          retainedImages, groupState, releasePhase, resourceState,
          releaseCount, admissionWitness, quiescenceWitness,
          resourceOrderWitness, activeViewWitness,
          completionPolicyWitness, releasedAccessWitness,
          exceptionalRollbackWitness>>

RejectReservedImage(participant) ==
    /\ participantState[participant] = "Reserved"
    /\ openingCallback[participant] \in Callbacks
    /\ callbackState' =
        [callbackState EXCEPT
            ![openingCallback[participant]] = "Finalizing"]
    /\ callbackOutcome' =
        [callbackOutcome EXCEPT
            ![openingCallback[participant]] = "Rejected"]
    /\ participantState' =
        [participantState EXCEPT ![participant] = "Rejected"]
    /\ openingCallback' =
        [openingCallback EXCEPT ![participant] = 0]
    /\ retainedImages' = retainedImages - 1
    /\ UNCHANGED
        <<callbackHasView, releaseOnExit,
          groupState, releasePhase, resourceState, releaseCount,
          admissionWitness, quiescenceWitness, resourceOrderWitness,
          activeViewWitness, completionPolicyWitness,
          releasedAccessWitness, exceptionalRollbackWitness>>

FailReservedOpen(participant) ==
    /\ participantState[participant] = "Reserved"
    /\ openingCallback[participant] \in Callbacks
    /\ callbackState' =
        [callbackState EXCEPT
            ![openingCallback[participant]] = "Finalizing"]
    /\ callbackOutcome' =
        [callbackOutcome EXCEPT
            ![openingCallback[participant]] = "OpenFailure"]
    /\ participantState' =
        [participantState EXCEPT
            ![participant] =
                (IF AllowExceptionalFailureCaching
                 THEN "Rejected"
                 ELSE "Cold")]
    /\ openingCallback' =
        [openingCallback EXCEPT ![participant] = 0]
    /\ retainedImages' = retainedImages - 1
    /\ exceptionalRollbackWitness' =
        (exceptionalRollbackWitness
         /\ callbackState'[openingCallback[participant]] = "Finalizing"
         /\ callbackOutcome'[openingCallback[participant]] = "OpenFailure"
         /\ participantState'[participant] = "Cold"
         /\ openingCallback'[participant] = 0
         /\ retainedImages' = retainedImages - 1)
    /\ UNCHANGED
        <<callbackHasView, releaseOnExit, groupState, releasePhase,
          resourceState, releaseCount, admissionWitness,
          quiescenceWitness, resourceOrderWitness, activeViewWitness,
          completionPolicyWitness, releasedAccessWitness>>

EnterCallback(callback) ==
    /\ callbackState[callback] = "Admitted"
    /\ participantState[Target(callback)] = "Ready"
    /\ callbackState' =
        [callbackState EXCEPT ![callback] = "Active"]
    /\ callbackHasView' =
        [callbackHasView EXCEPT ![callback] = TRUE]
    /\ UNCHANGED
        <<callbackOutcome, releaseOnExit, participantState, openingCallback,
          retainedImages,
          groupState, releasePhase, resourceState, releaseCount,
          admissionWitness, quiescenceWitness, resourceOrderWitness,
          activeViewWitness, completionPolicyWitness,
          releasedAccessWitness, exceptionalRollbackWitness>>

ObserveRejectedCallback(callback) ==
    /\ callbackState[callback] = "Admitted"
    /\ participantState[Target(callback)] = "Rejected"
    /\ callbackState' =
        [callbackState EXCEPT ![callback] = "Finalizing"]
    /\ callbackOutcome' =
        [callbackOutcome EXCEPT ![callback] = "Rejected"]
    /\ UNCHANGED
        <<callbackHasView, releaseOnExit, participantState,
          openingCallback, retainedImages, groupState, releasePhase, resourceState,
          releaseCount, admissionWitness, quiescenceWitness,
          resourceOrderWitness, activeViewWitness,
          completionPolicyWitness, releasedAccessWitness,
          exceptionalRollbackWitness>>

ObserveReleasedCallback(callback) ==
    /\ callbackState[callback] = "Admitted"
    /\ participantState[Target(callback)] = "Released"
    /\ callbackState' =
        [callbackState EXCEPT ![callback] = "Finalizing"]
    /\ callbackOutcome' =
        [callbackOutcome EXCEPT
            ![callback] =
                IF AllowReleasedAsRejected
                THEN "Rejected"
                ELSE "ReleasedFailure"]
    /\ releasedAccessWitness' =
        (releasedAccessWitness
         /\ callbackOutcome'[callback] = "ReleasedFailure")
    /\ UNCHANGED
        <<callbackHasView, releaseOnExit, participantState,
          openingCallback, retainedImages, groupState, releasePhase,
          resourceState, releaseCount, admissionWitness,
          quiescenceWitness, resourceOrderWitness, activeViewWitness,
          completionPolicyWitness, exceptionalRollbackWitness>>

CompleteFinalizingCallback(callback) ==
    /\ callbackState[callback] = "Finalizing"
    /\ \/ ~releaseOnExit[callback]
       \/ participantState[Target(callback)]
            \notin {"Opening", "Reserved"}
    /\ callbackState' =
        [callbackState EXCEPT ![callback] = "Done"]
    /\ IF /\ releaseOnExit[callback]
          /\ groupState = "Open"
          /\ ~(/\ callbackOutcome[callback] = "Rejected"
               /\ AllowRejectedRetention)
       THEN /\ participantState' =
                    [participantState EXCEPT
                        ![Target(callback)] = "Released"]
            /\ retainedImages' =
                retainedImages
                - (IF participantState[Target(callback)] = "Ready"
                   THEN 1
                   ELSE 0)
       ELSE /\ UNCHANGED <<participantState, retainedImages>>
    /\ completionPolicyWitness' =
        (completionPolicyWitness
         /\ IF /\ releaseOnExit[callback]
               /\ groupState = "Open"
            THEN /\ participantState'[Target(callback)] = "Released"
                 /\ retainedImages' =
                    retainedImages
                    - (IF participantState[Target(callback)] = "Ready"
                       THEN 1
                       ELSE 0)
            ELSE /\ participantState'[Target(callback)] =
                    participantState[Target(callback)]
                 /\ retainedImages' = retainedImages)
    /\ UNCHANGED
        <<callbackHasView, callbackOutcome, releaseOnExit, openingCallback,
          groupState, releasePhase, resourceState, releaseCount,
          admissionWitness, quiescenceWitness, resourceOrderWitness,
          activeViewWitness, releasedAccessWitness,
          exceptionalRollbackWitness>>

CompleteCallback(callback) ==
    /\ callbackState[callback] = "Active"
    /\ callbackState' =
        [callbackState EXCEPT ![callback] = "Done"]
    /\ callbackHasView' =
        [callbackHasView EXCEPT ![callback] = FALSE]
    /\ callbackOutcome' =
        [callbackOutcome EXCEPT ![callback] = "Succeeded"]
    /\ IF /\ groupState = "Open"
          /\ participantState[Target(callback)] = "Ready"
          /\ IF AllowSuccessfulPolicyViolation
             THEN ~releaseOnExit[callback]
             ELSE releaseOnExit[callback]
       THEN /\ participantState' =
                    [participantState EXCEPT
                        ![Target(callback)] = "Released"]
            /\ retainedImages' = retainedImages - 1
       ELSE /\ UNCHANGED <<participantState, retainedImages>>
    /\ completionPolicyWitness' =
        (completionPolicyWitness
         /\ IF /\ groupState = "Open"
               /\ participantState[Target(callback)] = "Ready"
               /\ releaseOnExit[callback]
            THEN /\ participantState'[Target(callback)] = "Released"
                 /\ retainedImages' = retainedImages - 1
            ELSE /\ participantState'[Target(callback)] =
                     participantState[Target(callback)]
                 /\ retainedImages' = retainedImages)
    /\ UNCHANGED
        <<releaseOnExit, openingCallback, groupState, releasePhase,
          resourceState, releaseCount, admissionWitness,
          quiescenceWitness, resourceOrderWitness, activeViewWitness,
          releasedAccessWitness, exceptionalRollbackWitness>>

DisposeGroup ==
    /\ groupState = "Open"
    /\ groupState' = "Disposed"
    /\ GroupRelease!RequestRelease
    /\ UNCHANGED
        <<callbackState, callbackHasView, callbackOutcome, releaseOnExit,
          participantState, openingCallback, retainedImages, releasePhase,
          resourceState, releaseCount, completedGroup, completionResult,
          admissionWitness,
          quiescenceWitness, resourceOrderWitness, activeViewWitness,
          completionPolicyWitness, releasedAccessWitness,
          exceptionalRollbackWitness>>

BeginGroupRelease ==
    /\ groupState = "Disposed"
    /\ releasePhase = "NotStarted"
    /\ LiveCallbacks = {}
    /\ releasePhase' = "Resources"
    /\ releaseCount' = releaseCount + 1
    /\ quiescenceWitness' =
        (quiescenceWitness /\ (LiveCallbacks = {}))
    /\ UNCHANGED
        <<callbackState, callbackHasView, callbackOutcome, releaseOnExit,
          participantState, openingCallback, retainedImages, groupState,
          resourceState, admissionWitness, resourceOrderWitness,
          activeViewWitness, completionPolicyWitness,
          releasedAccessWitness, exceptionalRollbackWitness>>

BeginEarlyGroupRelease ==
    /\ AllowEarlyRelease
    /\ groupState = "Disposed"
    /\ releasePhase = "NotStarted"
    /\ LiveCallbacks # {}
    /\ releasePhase' = "Resources"
    /\ releaseCount' = releaseCount + 1
    /\ quiescenceWitness' = FALSE
    /\ UNCHANGED
        <<callbackState, callbackHasView, callbackOutcome, releaseOnExit,
          participantState, openingCallback, retainedImages, groupState,
          resourceState, admissionWitness, resourceOrderWitness,
          activeViewWitness, completionPolicyWitness,
          releasedAccessWitness, exceptionalRollbackWitness>>

ReleaseOwnedResource ==
    /\ releasePhase = "Resources"
    /\ resourceState = "Owned"
    /\ resourceState' = "Released"
    /\ UNCHANGED
        <<callbackState, callbackHasView, callbackOutcome, releaseOnExit,
          participantState, openingCallback, retainedImages, groupState,
          releasePhase, releaseCount, admissionWitness,
          quiescenceWitness, resourceOrderWitness, activeViewWitness,
          completionPolicyWitness, releasedAccessWitness,
          exceptionalRollbackWitness>>

BeginSnapshotRelease ==
    /\ releasePhase = "Resources"
    /\ resourceState = "Released"
    /\ releasePhase' = "Snapshots"
    /\ resourceOrderWitness' =
        (resourceOrderWitness /\ (resourceState = "Released"))
    /\ UNCHANGED
        <<callbackState, callbackHasView, callbackOutcome, releaseOnExit,
          participantState, openingCallback, retainedImages, groupState,
          resourceState, releaseCount, admissionWitness,
          quiescenceWitness, activeViewWitness, completionPolicyWitness,
          releasedAccessWitness, exceptionalRollbackWitness>>

BeginEarlySnapshotRelease ==
    /\ AllowEarlySnapshotRelease
    /\ releasePhase = "Resources"
    /\ resourceState = "Owned"
    /\ releasePhase' = "Snapshots"
    /\ resourceOrderWitness' = FALSE
    /\ UNCHANGED
        <<callbackState, callbackHasView, callbackOutcome, releaseOnExit,
          participantState, openingCallback, retainedImages, groupState,
          resourceState, releaseCount, admissionWitness,
          quiescenceWitness, activeViewWitness, completionPolicyWitness,
          releasedAccessWitness, exceptionalRollbackWitness>>

ReleaseParticipant(participant) ==
    /\ releasePhase = "Snapshots"
    /\ participantState[participant] # "Released"
    /\ participantState' =
        [participantState EXCEPT ![participant] = "Released"]
    /\ IF participantState[participant] \in {"Reserved", "Ready"}
       THEN retainedImages' = retainedImages - 1
       ELSE UNCHANGED retainedImages
    /\ activeViewWitness' =
        (activeViewWitness
         /\ ~(\E callback \in Callbacks:
                 /\ callbackState[callback] = "Active"
                 /\ Target(callback) = participant))
    /\ UNCHANGED
        <<callbackState, callbackHasView, callbackOutcome, releaseOnExit,
          openingCallback, groupState, releasePhase, resourceState,
          releaseCount, admissionWitness, quiescenceWitness,
          resourceOrderWitness, completionPolicyWitness,
          releasedAccessWitness, exceptionalRollbackWitness>>

CompleteGroupRelease ==
    /\ releasePhase = "Snapshots"
    /\ \A participant \in Participants:
        participantState[participant] = "Released"
    /\ releasePhase' = "Complete"
    /\ groupState' = "Released"
    /\ GroupRelease!CompleteRelease(
        "Succeeded",
        LiveCallbacks = {} \/ AllowEarlyRelease)
    /\ UNCHANGED
        <<callbackState, callbackHasView, callbackOutcome, releaseOnExit,
          participantState, openingCallback, retainedImages,
          resourceState, releaseCount, requestedGroup, admissionWitness,
          quiescenceWitness, resourceOrderWitness, activeViewWitness,
          completionPolicyWitness, releasedAccessWitness,
          exceptionalRollbackWitness>>

Next ==
    \/ \E callback \in Callbacks:
        PreserveRelease(AdmitCallback(callback))
    \/ \E callback \in Callbacks:
        PreserveRelease(StartOpen(callback))
    \/ \E participant \in Participants:
        PreserveRelease(ReserveImage(participant))
    \/ \E participant \in Participants:
        PreserveRelease(RejectForBudget(participant))
    \/ \E participant \in Participants:
        PreserveRelease(FailBeforeReservation(participant))
    \/ \E participant \in Participants:
        PreserveRelease(PublishImage(participant))
    \/ \E participant \in Participants:
        PreserveRelease(RejectReservedImage(participant))
    \/ \E participant \in Participants:
        PreserveRelease(FailReservedOpen(participant))
    \/ \E callback \in Callbacks:
        PreserveRelease(EnterCallback(callback))
    \/ \E callback \in Callbacks:
        PreserveRelease(ObserveRejectedCallback(callback))
    \/ \E callback \in Callbacks:
        PreserveRelease(ObserveReleasedCallback(callback))
    \/ \E callback \in Callbacks:
        PreserveRelease(CompleteFinalizingCallback(callback))
    \/ \E callback \in Callbacks:
        PreserveRelease(CompleteCallback(callback))
    \/ DisposeGroup
    \/ PreserveRelease(BeginGroupRelease)
    \/ PreserveRelease(BeginEarlyGroupRelease)
    \/ PreserveRelease(ReleaseOwnedResource)
    \/ PreserveRelease(BeginSnapshotRelease)
    \/ PreserveRelease(BeginEarlySnapshotRelease)
    \/ \E participant \in Participants:
        PreserveRelease(ReleaseParticipant(participant))
    \/ CompleteGroupRelease

Fairness ==
    /\ \A callback \in Callbacks:
        /\ WF_vars(PreserveRelease(StartOpen(callback)))
        /\ WF_vars(PreserveRelease(EnterCallback(callback)))
        /\ WF_vars(PreserveRelease(ObserveRejectedCallback(callback)))
        /\ WF_vars(PreserveRelease(ObserveReleasedCallback(callback)))
        /\ WF_vars(PreserveRelease(CompleteFinalizingCallback(callback)))
        /\ WF_vars(PreserveRelease(CompleteCallback(callback)))
    /\ \A participant \in Participants:
        /\ WF_vars(PreserveRelease(ReserveImage(participant)))
        /\ WF_vars(PreserveRelease(RejectForBudget(participant)))
        /\ WF_vars(PreserveRelease(FailBeforeReservation(participant)))
        /\ WF_vars(PreserveRelease(PublishImage(participant)))
        /\ WF_vars(PreserveRelease(RejectReservedImage(participant)))
        /\ WF_vars(PreserveRelease(FailReservedOpen(participant)))
        /\ WF_vars(PreserveRelease(ReleaseParticipant(participant)))
    /\ WF_vars(PreserveRelease(BeginGroupRelease))
    /\ WF_vars(PreserveRelease(BeginEarlyGroupRelease))
    /\ WF_vars(PreserveRelease(ReleaseOwnedResource))
    /\ WF_vars(PreserveRelease(BeginSnapshotRelease))
    /\ WF_vars(PreserveRelease(BeginEarlySnapshotRelease))
    /\ WF_vars(CompleteGroupRelease)

Spec ==
    Init /\ [][Next]_vars /\ Fairness

TypeOK ==
    /\ callbackState \in [Callbacks -> CallbackStates]
    /\ callbackHasView \in [Callbacks -> BOOLEAN]
    /\ callbackOutcome \in [Callbacks -> CallbackOutcomes]
    /\ releaseOnExit \in [Callbacks -> BOOLEAN]
    /\ participantState \in [Participants -> ParticipantStates]
    /\ openingCallback \in [Participants -> (Callbacks \union {0})]
    /\ retainedImages \in Nat
    /\ groupState \in GroupStates
    /\ releasePhase \in ReleasePhases
    /\ resourceState \in ResourceStates
    /\ releaseCount \in Nat
    /\ requestedGroup \in {NoGroupIdentity, GroupIdentity}
    /\ completedGroup \in {NoGroupIdentity, GroupIdentity}
    /\ completionResult \in {NoReleaseResult, "Succeeded"}
    /\ admissionWitness \in BOOLEAN
    /\ quiescenceWitness \in BOOLEAN
    /\ resourceOrderWitness \in BOOLEAN
    /\ activeViewWitness \in BOOLEAN
    /\ completionPolicyWitness \in BOOLEAN
    /\ releasedAccessWitness \in BOOLEAN
    /\ exceptionalRollbackWitness \in BOOLEAN

RetainedImagesStayWithinBudget ==
    retainedImages <= MaxRetainedImages

RetainedImageAccountingIsExact ==
    retainedImages = Cardinality(RetainedParticipants)

OpeningOwnershipIsExact ==
    \A participant \in Participants:
        /\ (participantState[participant] \in {"Opening", "Reserved"}) =
            (openingCallback[participant] \in Callbacks)
        /\ (openingCallback[participant] \in Callbacks =>
                /\ callbackState[openingCallback[participant]] = "Admitted"
                /\ Target(openingCallback[participant]) = participant)

CallbackOutcomesMatchPhases ==
    \A callback \in Callbacks:
        (callbackState[callback] \in {"Finalizing", "Done"}) =
            (callbackOutcome[callback] # "Pending")

ActiveCallbacksHoldLocalViews ==
    \A callback \in Callbacks:
        (callbackState[callback] = "Active") =
            callbackHasView[callback]

NoAdmissionAfterDisposal ==
    admissionWitness

GroupReleaseWaitsForQuiescence ==
    quiescenceWitness

GroupReleaseBehaviorRefinesOwner ==
    GroupRelease!SafetySpec(LiveCallbacks = {})

GroupReleaseCompletionMatchesRequest ==
    GroupRelease!CompletionMatchesRequest

GroupReleaseCompletionCarriesResult ==
    GroupRelease!CompletionCarriesResult

OwnedResourcesPrecedeGroupSnapshots ==
    /\ resourceOrderWitness
    /\ (releasePhase \in {"Snapshots", "Complete"} =>
        resourceState = "Released")

RejectedReleaseAfterUseIsTerminal ==
    \A callback \in Callbacks:
        /\ callbackState[callback] = "Done"
        /\ callbackOutcome[callback] = "Rejected"
        /\ releaseOnExit[callback]
        /\ groupState = "Open"
        => participantState[Target(callback)] = "Released"

CompletionHonorsReleasePolicy ==
    completionPolicyWitness

ReleasedParticipantAccessFails ==
    releasedAccessWitness

ExceptionalFailureRollsBackForRetry ==
    exceptionalRollbackWitness

ActiveViewsSurviveGroupRelease ==
    activeViewWitness

GroupReleaseBeginsExactlyOnce ==
    /\ releaseCount <= 1
    /\ (releasePhase = "NotStarted") = (releaseCount = 0)

GroupReleaseRequiresDisposal ==
    releasePhase # "NotStarted" => groupState # "Open"

ReleasedGroupOwnsNothing ==
    groupState = "Released" =>
        /\ retainedImages = 0
        /\ resourceState = "Released"
        /\ \A participant \in Participants:
            participantState[participant] = "Released"

ParticipantLocalOpening ==
    \A callback \in Callbacks:
        /\ callbackState[callback] = "Admitted"
        /\ participantState[Target(callback)] = "Cold"
        => ENABLED PreserveRelease(StartOpen(callback))

EveryAdmittedCallbackSettles ==
    \A callback \in Callbacks:
        callbackState[callback] # "Unused"
            ~> callbackState[callback] = "Done"

EveryStartedOpenSettlesOrRollsBack ==
    \A participant \in Participants:
        participantState[participant] \in {"Opening", "Reserved"}
            ~> participantState[participant]
                \in {"Cold", "Ready", "Rejected", "Released"}

DisposedGroupEventuallyReleases ==
    groupState = "Disposed" ~> groupState = "Released"

RequestedGroupEventuallyCompletes ==
    GroupRelease!RequestedGroupEventuallyCompletes

=============================================================================
