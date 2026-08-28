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
    AllowExceptionalFailureCaching

ASSUME /\ ParticipantCount \in Nat \ {0}
       /\ CallbackCount \in Nat \ {0}
       /\ MaxRetainedImages \in 0..ParticipantCount
       /\ AllowEarlyRelease \in BOOLEAN
       /\ AllowEarlySnapshotRelease \in BOOLEAN
       /\ AllowRejectedRetention \in BOOLEAN
       /\ AllowSuccessfulPolicyViolation \in BOOLEAN
       /\ AllowReleasedAsRejected \in BOOLEAN
       /\ AllowExceptionalFailureCaching \in BOOLEAN

Participants == 1..ParticipantCount
Callbacks == 1..CallbackCount

CallbackStates == {"Unused", "Admitted", "Active", "Done"}
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
    admissionWitness,
    quiescenceWitness,
    resourceOrderWitness,
    activeViewWitness,
    successfulPolicyWitness,
    releasedAccessWitness,
    exceptionalRollbackWitness

vars ==
    <<callbackState, callbackHasView, callbackOutcome, releaseOnExit,
      participantState, openingCallback, retainedImages, groupState,
      releasePhase, resourceState,
      releaseCount, admissionWitness, quiescenceWitness,
      resourceOrderWitness, activeViewWitness, successfulPolicyWitness,
      releasedAccessWitness, exceptionalRollbackWitness>>

LiveCallbacks ==
    {callback \in Callbacks:
        callbackState[callback] \in {"Admitted", "Active"}}

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
    /\ admissionWitness = TRUE
    /\ quiescenceWitness = TRUE
    /\ resourceOrderWitness = TRUE
    /\ activeViewWitness = TRUE
    /\ successfulPolicyWitness = TRUE
    /\ releasedAccessWitness = TRUE
    /\ exceptionalRollbackWitness = TRUE

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
          activeViewWitness, successfulPolicyWitness,
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
          activeViewWitness, successfulPolicyWitness,
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
          activeViewWitness, successfulPolicyWitness,
          releasedAccessWitness, exceptionalRollbackWitness>>

RejectForBudget(participant) ==
    /\ participantState[participant] = "Opening"
    /\ retainedImages = MaxRetainedImages
    /\ openingCallback[participant] \in Callbacks
    /\ callbackState' =
        [callbackState EXCEPT
            ![openingCallback[participant]] = "Done"]
    /\ callbackOutcome' =
        [callbackOutcome EXCEPT
            ![openingCallback[participant]] = "Rejected"]
    /\ participantState' =
        [participantState EXCEPT
            ![participant] =
                (IF /\ releaseOnExit[openingCallback[participant]]
                    /\ groupState = "Open"
                    /\ ~AllowRejectedRetention
                 THEN "Released"
                 ELSE "Rejected")]
    /\ openingCallback' =
        [openingCallback EXCEPT ![participant] = 0]
    /\ UNCHANGED
        <<callbackHasView, releaseOnExit, retainedImages,
          groupState, releasePhase, resourceState,
          releaseCount, admissionWitness, quiescenceWitness,
          resourceOrderWitness, activeViewWitness,
          successfulPolicyWitness, releasedAccessWitness,
          exceptionalRollbackWitness>>

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
          successfulPolicyWitness, releasedAccessWitness,
          exceptionalRollbackWitness>>

RejectReservedImage(participant) ==
    /\ participantState[participant] = "Reserved"
    /\ openingCallback[participant] \in Callbacks
    /\ callbackState' =
        [callbackState EXCEPT
            ![openingCallback[participant]] = "Done"]
    /\ callbackOutcome' =
        [callbackOutcome EXCEPT
            ![openingCallback[participant]] = "Rejected"]
    /\ participantState' =
        [participantState EXCEPT
            ![participant] =
                (IF /\ releaseOnExit[openingCallback[participant]]
                    /\ groupState = "Open"
                    /\ ~AllowRejectedRetention
                 THEN "Released"
                 ELSE "Rejected")]
    /\ openingCallback' =
        [openingCallback EXCEPT ![participant] = 0]
    /\ retainedImages' = retainedImages - 1
    /\ UNCHANGED
        <<callbackHasView, releaseOnExit,
          groupState, releasePhase, resourceState, releaseCount,
          admissionWitness, quiescenceWitness, resourceOrderWitness,
          activeViewWitness, successfulPolicyWitness,
          releasedAccessWitness, exceptionalRollbackWitness>>

FailReservedOpen(participant) ==
    /\ participantState[participant] = "Reserved"
    /\ openingCallback[participant] \in Callbacks
    /\ callbackState' =
        [callbackState EXCEPT
            ![openingCallback[participant]] = "Done"]
    /\ callbackOutcome' =
        [callbackOutcome EXCEPT
            ![openingCallback[participant]] = "OpenFailure"]
    /\ participantState' =
        [participantState EXCEPT
            ![participant] =
                (IF AllowExceptionalFailureCaching
                 THEN "Rejected"
                 ELSE IF /\ releaseOnExit[openingCallback[participant]]
                         /\ groupState = "Open"
                      THEN "Released"
                      ELSE "Cold")]
    /\ openingCallback' =
        [openingCallback EXCEPT ![participant] = 0]
    /\ retainedImages' = retainedImages - 1
    /\ exceptionalRollbackWitness' =
        (exceptionalRollbackWitness
         /\ callbackOutcome'[openingCallback[participant]] = "OpenFailure"
         /\ participantState'[participant] =
            (IF /\ releaseOnExit[openingCallback[participant]]
                /\ groupState = "Open"
             THEN "Released"
             ELSE "Cold")
         /\ openingCallback'[participant] = 0
         /\ retainedImages' = retainedImages - 1)
    /\ UNCHANGED
        <<callbackHasView, releaseOnExit, groupState, releasePhase,
          resourceState, releaseCount, admissionWitness,
          quiescenceWitness, resourceOrderWitness, activeViewWitness,
          successfulPolicyWitness, releasedAccessWitness>>

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
          activeViewWitness, successfulPolicyWitness,
          releasedAccessWitness, exceptionalRollbackWitness>>

FinishRejectedCallback(callback) ==
    /\ callbackState[callback] = "Admitted"
    /\ participantState[Target(callback)] = "Rejected"
    /\ callbackState' =
        [callbackState EXCEPT ![callback] = "Done"]
    /\ callbackOutcome' =
        [callbackOutcome EXCEPT ![callback] = "Rejected"]
    /\ IF /\ releaseOnExit[callback]
          /\ groupState = "Open"
       THEN IF AllowRejectedRetention
            THEN UNCHANGED participantState
            ELSE participantState' =
                    [participantState EXCEPT
                        ![Target(callback)] = "Released"]
       ELSE UNCHANGED participantState
    /\ UNCHANGED
        <<callbackHasView, releaseOnExit, openingCallback, retainedImages,
          groupState, releasePhase, resourceState,
          releaseCount, admissionWitness, quiescenceWitness,
          resourceOrderWitness, activeViewWitness,
          successfulPolicyWitness, releasedAccessWitness,
          exceptionalRollbackWitness>>

FinishReleasedCallback(callback) ==
    /\ callbackState[callback] = "Admitted"
    /\ participantState[Target(callback)] = "Released"
    /\ callbackState' =
        [callbackState EXCEPT ![callback] = "Done"]
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
          successfulPolicyWitness, exceptionalRollbackWitness>>

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
    /\ successfulPolicyWitness' =
        (successfulPolicyWitness
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
    /\ UNCHANGED
        <<callbackState, callbackHasView, callbackOutcome, releaseOnExit,
          participantState, openingCallback, retainedImages, releasePhase,
          resourceState, releaseCount, admissionWitness,
          quiescenceWitness, resourceOrderWitness, activeViewWitness,
          successfulPolicyWitness, releasedAccessWitness,
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
          activeViewWitness, successfulPolicyWitness,
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
          activeViewWitness, successfulPolicyWitness,
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
          successfulPolicyWitness, releasedAccessWitness,
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
          quiescenceWitness, activeViewWitness, successfulPolicyWitness,
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
          quiescenceWitness, activeViewWitness, successfulPolicyWitness,
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
          resourceOrderWitness, successfulPolicyWitness,
          releasedAccessWitness, exceptionalRollbackWitness>>

CompleteGroupRelease ==
    /\ releasePhase = "Snapshots"
    /\ \A participant \in Participants:
        participantState[participant] = "Released"
    /\ releasePhase' = "Complete"
    /\ groupState' = "Released"
    /\ UNCHANGED
        <<callbackState, callbackHasView, callbackOutcome, releaseOnExit,
          participantState, openingCallback, retainedImages,
          resourceState, releaseCount, admissionWitness,
          quiescenceWitness, resourceOrderWitness, activeViewWitness,
          successfulPolicyWitness, releasedAccessWitness,
          exceptionalRollbackWitness>>

Next ==
    \/ \E callback \in Callbacks:
        AdmitCallback(callback)
    \/ \E callback \in Callbacks:
        StartOpen(callback)
    \/ \E participant \in Participants:
        ReserveImage(participant)
    \/ \E participant \in Participants:
        RejectForBudget(participant)
    \/ \E participant \in Participants:
        PublishImage(participant)
    \/ \E participant \in Participants:
        RejectReservedImage(participant)
    \/ \E participant \in Participants:
        FailReservedOpen(participant)
    \/ \E callback \in Callbacks:
        EnterCallback(callback)
    \/ \E callback \in Callbacks:
        FinishRejectedCallback(callback)
    \/ \E callback \in Callbacks:
        FinishReleasedCallback(callback)
    \/ \E callback \in Callbacks:
        CompleteCallback(callback)
    \/ DisposeGroup
    \/ BeginGroupRelease
    \/ BeginEarlyGroupRelease
    \/ ReleaseOwnedResource
    \/ BeginSnapshotRelease
    \/ BeginEarlySnapshotRelease
    \/ \E participant \in Participants:
        ReleaseParticipant(participant)
    \/ CompleteGroupRelease

Fairness ==
    /\ \A callback \in Callbacks:
        /\ WF_vars(StartOpen(callback))
        /\ WF_vars(EnterCallback(callback))
        /\ WF_vars(FinishRejectedCallback(callback))
        /\ WF_vars(FinishReleasedCallback(callback))
        /\ WF_vars(CompleteCallback(callback))
    /\ \A participant \in Participants:
        /\ WF_vars(ReserveImage(participant))
        /\ WF_vars(RejectForBudget(participant))
        /\ WF_vars(PublishImage(participant))
        /\ WF_vars(RejectReservedImage(participant))
        /\ WF_vars(FailReservedOpen(participant))
        /\ WF_vars(ReleaseParticipant(participant))
    /\ WF_vars(BeginGroupRelease)
    /\ WF_vars(BeginEarlyGroupRelease)
    /\ WF_vars(ReleaseOwnedResource)
    /\ WF_vars(BeginSnapshotRelease)
    /\ WF_vars(BeginEarlySnapshotRelease)
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
    /\ admissionWitness \in BOOLEAN
    /\ quiescenceWitness \in BOOLEAN
    /\ resourceOrderWitness \in BOOLEAN
    /\ activeViewWitness \in BOOLEAN
    /\ successfulPolicyWitness \in BOOLEAN
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

CompletedCallbacksHaveOutcomes ==
    \A callback \in Callbacks:
        (callbackState[callback] = "Done") =
            (callbackOutcome[callback] # "Pending")

ActiveCallbacksHoldLocalViews ==
    \A callback \in Callbacks:
        (callbackState[callback] = "Active") =
            callbackHasView[callback]

NoAdmissionAfterDisposal ==
    admissionWitness

GroupReleaseWaitsForQuiescence ==
    quiescenceWitness

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

SuccessfulCompletionHonorsReleasePolicy ==
    successfulPolicyWitness

ReleasedParticipantAccessFails ==
    releasedAccessWitness

ExceptionalFailureRollsBackAndHonorsReleasePolicy ==
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
        => ENABLED StartOpen(callback)

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

=============================================================================
