----------------- MODULE AssemblyContextGroupLifecycle -----------------
EXTENDS FiniteSets, Integers, TLC

CONSTANTS
    ParticipantCount,
    CallbackCount,
    MaxRetainedImages,
    AllowEarlyRelease,
    AllowEarlySnapshotRelease,
    AllowRejectedRetention

ASSUME /\ ParticipantCount \in Nat \ {0}
       /\ CallbackCount \in Nat \ {0}
       /\ MaxRetainedImages \in 0..ParticipantCount
       /\ AllowEarlyRelease \in BOOLEAN
       /\ AllowEarlySnapshotRelease \in BOOLEAN
       /\ AllowRejectedRetention \in BOOLEAN

Participants == 1..ParticipantCount
Callbacks == 1..CallbackCount

CallbackStates == {"Unused", "Admitted", "Active", "Done"}
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
    releaseOnExit,
    participantState,
    openCount,
    retainedImages,
    groupState,
    releasePhase,
    resourceState,
    releaseCount,
    admissionWitness,
    quiescenceWitness,
    resourceOrderWitness,
    activeViewWitness

vars ==
    <<callbackState, callbackHasView, releaseOnExit, participantState,
      openCount, retainedImages, groupState, releasePhase, resourceState,
      releaseCount, admissionWitness, quiescenceWitness,
      resourceOrderWitness, activeViewWitness>>

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
    /\ releaseOnExit \in [Callbacks -> BOOLEAN]
    /\ participantState =
        [participant \in Participants |-> "Cold"]
    /\ openCount =
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

AdmitCallback(callback) ==
    /\ callbackState[callback] = "Unused"
    /\ groupState = "Open"
    /\ callbackState' =
        [callbackState EXCEPT ![callback] = "Admitted"]
    /\ admissionWitness' =
        admissionWitness /\ (groupState = "Open")
    /\ UNCHANGED
        <<callbackHasView, releaseOnExit, participantState, openCount,
          retainedImages, groupState, releasePhase, resourceState,
          releaseCount, quiescenceWitness, resourceOrderWitness,
          activeViewWitness>>

StartOpen(callback) ==
    /\ callbackState[callback] = "Admitted"
    /\ participantState[Target(callback)] = "Cold"
    /\ participantState' =
        [participantState EXCEPT ![Target(callback)] = "Opening"]
    /\ openCount' =
        [openCount EXCEPT ![Target(callback)] = @ + 1]
    /\ UNCHANGED
        <<callbackState, callbackHasView, releaseOnExit, retainedImages,
          groupState, releasePhase, resourceState, releaseCount,
          admissionWitness, quiescenceWitness, resourceOrderWitness,
          activeViewWitness>>

ReserveImage(participant) ==
    /\ participantState[participant] = "Opening"
    /\ retainedImages < MaxRetainedImages
    /\ participantState' =
        [participantState EXCEPT ![participant] = "Reserved"]
    /\ retainedImages' = retainedImages + 1
    /\ UNCHANGED
        <<callbackState, callbackHasView, releaseOnExit, openCount,
          groupState, releasePhase, resourceState, releaseCount,
          admissionWitness, quiescenceWitness, resourceOrderWitness,
          activeViewWitness>>

RejectForBudget(participant) ==
    /\ participantState[participant] = "Opening"
    /\ retainedImages = MaxRetainedImages
    /\ participantState' =
        [participantState EXCEPT ![participant] = "Rejected"]
    /\ UNCHANGED
        <<callbackState, callbackHasView, releaseOnExit, openCount,
          retainedImages, groupState, releasePhase, resourceState,
          releaseCount, admissionWitness, quiescenceWitness,
          resourceOrderWitness, activeViewWitness>>

PublishImage(participant) ==
    /\ participantState[participant] = "Reserved"
    /\ participantState' =
        [participantState EXCEPT ![participant] = "Ready"]
    /\ UNCHANGED
        <<callbackState, callbackHasView, releaseOnExit, openCount,
          retainedImages, groupState, releasePhase, resourceState,
          releaseCount, admissionWitness, quiescenceWitness,
          resourceOrderWitness, activeViewWitness>>

RejectReservedImage(participant) ==
    /\ participantState[participant] = "Reserved"
    /\ participantState' =
        [participantState EXCEPT ![participant] = "Rejected"]
    /\ retainedImages' = retainedImages - 1
    /\ UNCHANGED
        <<callbackState, callbackHasView, releaseOnExit, openCount,
          groupState, releasePhase, resourceState, releaseCount,
          admissionWitness, quiescenceWitness, resourceOrderWitness,
          activeViewWitness>>

EnterCallback(callback) ==
    /\ callbackState[callback] = "Admitted"
    /\ participantState[Target(callback)] = "Ready"
    /\ callbackState' =
        [callbackState EXCEPT ![callback] = "Active"]
    /\ callbackHasView' =
        [callbackHasView EXCEPT ![callback] = TRUE]
    /\ UNCHANGED
        <<releaseOnExit, participantState, openCount, retainedImages,
          groupState, releasePhase, resourceState, releaseCount,
          admissionWitness, quiescenceWitness, resourceOrderWitness,
          activeViewWitness>>

FinishUnavailableCallback(callback) ==
    /\ callbackState[callback] = "Admitted"
    /\ participantState[Target(callback)] \in {"Rejected", "Released"}
    /\ callbackState' =
        [callbackState EXCEPT ![callback] = "Done"]
    /\ IF /\ releaseOnExit[callback]
          /\ groupState = "Open"
          /\ participantState[Target(callback)] = "Rejected"
       THEN IF AllowRejectedRetention
            THEN UNCHANGED participantState
            ELSE participantState' =
                    [participantState EXCEPT
                        ![Target(callback)] = "Released"]
       ELSE UNCHANGED participantState
    /\ UNCHANGED
        <<callbackHasView, releaseOnExit, openCount, retainedImages,
          groupState, releasePhase, resourceState,
          releaseCount, admissionWitness, quiescenceWitness,
          resourceOrderWitness, activeViewWitness>>

CompleteCallback(callback) ==
    /\ callbackState[callback] = "Active"
    /\ callbackState' =
        [callbackState EXCEPT ![callback] = "Done"]
    /\ callbackHasView' =
        [callbackHasView EXCEPT ![callback] = FALSE]
    /\ IF /\ releaseOnExit[callback]
          /\ groupState = "Open"
          /\ participantState[Target(callback)] = "Ready"
       THEN /\ participantState' =
                    [participantState EXCEPT
                        ![Target(callback)] = "Released"]
            /\ retainedImages' = retainedImages - 1
       ELSE /\ UNCHANGED <<participantState, retainedImages>>
    /\ UNCHANGED
        <<releaseOnExit, openCount, groupState, releasePhase,
          resourceState, releaseCount, admissionWitness,
          quiescenceWitness, resourceOrderWitness, activeViewWitness>>

DisposeGroup ==
    /\ groupState = "Open"
    /\ groupState' = "Disposed"
    /\ UNCHANGED
        <<callbackState, callbackHasView, releaseOnExit,
          participantState, openCount, retainedImages, releasePhase,
          resourceState, releaseCount, admissionWitness,
          quiescenceWitness, resourceOrderWitness, activeViewWitness>>

BeginGroupRelease ==
    /\ groupState = "Disposed"
    /\ releasePhase = "NotStarted"
    /\ LiveCallbacks = {}
    /\ releasePhase' = "Resources"
    /\ releaseCount' = releaseCount + 1
    /\ quiescenceWitness' =
        quiescenceWitness /\ (LiveCallbacks = {})
    /\ UNCHANGED
        <<callbackState, callbackHasView, releaseOnExit,
          participantState, openCount, retainedImages, groupState,
          resourceState, admissionWitness, resourceOrderWitness,
          activeViewWitness>>

BeginEarlyGroupRelease ==
    /\ AllowEarlyRelease
    /\ groupState = "Disposed"
    /\ releasePhase = "NotStarted"
    /\ LiveCallbacks # {}
    /\ releasePhase' = "Resources"
    /\ releaseCount' = releaseCount + 1
    /\ quiescenceWitness' = FALSE
    /\ UNCHANGED
        <<callbackState, callbackHasView, releaseOnExit,
          participantState, openCount, retainedImages, groupState,
          resourceState, admissionWitness, resourceOrderWitness,
          activeViewWitness>>

ReleaseOwnedResource ==
    /\ releasePhase = "Resources"
    /\ resourceState = "Owned"
    /\ resourceState' = "Released"
    /\ UNCHANGED
        <<callbackState, callbackHasView, releaseOnExit,
          participantState, openCount, retainedImages, groupState,
          releasePhase, releaseCount, admissionWitness,
          quiescenceWitness, resourceOrderWitness, activeViewWitness>>

BeginSnapshotRelease ==
    /\ releasePhase = "Resources"
    /\ resourceState = "Released"
    /\ releasePhase' = "Snapshots"
    /\ resourceOrderWitness' =
        resourceOrderWitness /\ (resourceState = "Released")
    /\ UNCHANGED
        <<callbackState, callbackHasView, releaseOnExit,
          participantState, openCount, retainedImages, groupState,
          resourceState, releaseCount, admissionWitness,
          quiescenceWitness, activeViewWitness>>

BeginEarlySnapshotRelease ==
    /\ AllowEarlySnapshotRelease
    /\ releasePhase = "Resources"
    /\ resourceState = "Owned"
    /\ releasePhase' = "Snapshots"
    /\ resourceOrderWitness' = FALSE
    /\ UNCHANGED
        <<callbackState, callbackHasView, releaseOnExit,
          participantState, openCount, retainedImages, groupState,
          resourceState, releaseCount, admissionWitness,
          quiescenceWitness, activeViewWitness>>

ReleaseParticipant(participant) ==
    /\ releasePhase = "Snapshots"
    /\ participantState[participant] # "Released"
    /\ participantState' =
        [participantState EXCEPT ![participant] = "Released"]
    /\ IF participantState[participant] \in {"Reserved", "Ready"}
       THEN retainedImages' = retainedImages - 1
       ELSE UNCHANGED retainedImages
    /\ activeViewWitness' =
        activeViewWitness
        /\ ~(\E callback \in Callbacks:
                /\ callbackState[callback] = "Active"
                /\ Target(callback) = participant)
    /\ UNCHANGED
        <<callbackState, callbackHasView, releaseOnExit, openCount,
          groupState, releasePhase, resourceState, releaseCount,
          admissionWitness, quiescenceWitness, resourceOrderWitness>>

CompleteGroupRelease ==
    /\ releasePhase = "Snapshots"
    /\ \A participant \in Participants:
        participantState[participant] = "Released"
    /\ releasePhase' = "Complete"
    /\ groupState' = "Released"
    /\ UNCHANGED
        <<callbackState, callbackHasView, releaseOnExit,
          participantState, openCount, retainedImages, resourceState,
          releaseCount, admissionWitness, quiescenceWitness,
          resourceOrderWitness, activeViewWitness>>

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
    \/ \E callback \in Callbacks:
        EnterCallback(callback)
    \/ \E callback \in Callbacks:
        FinishUnavailableCallback(callback)
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
        /\ WF_vars(FinishUnavailableCallback(callback))
        /\ WF_vars(CompleteCallback(callback))
    /\ \A participant \in Participants:
        /\ WF_vars(ReserveImage(participant))
        /\ WF_vars(RejectForBudget(participant))
        /\ WF_vars(PublishImage(participant))
        /\ WF_vars(RejectReservedImage(participant))
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
    /\ releaseOnExit \in [Callbacks -> BOOLEAN]
    /\ participantState \in [Participants -> ParticipantStates]
    /\ openCount \in [Participants -> Nat]
    /\ retainedImages \in Nat
    /\ groupState \in GroupStates
    /\ releasePhase \in ReleasePhases
    /\ resourceState \in ResourceStates
    /\ releaseCount \in Nat
    /\ admissionWitness \in BOOLEAN
    /\ quiescenceWitness \in BOOLEAN
    /\ resourceOrderWitness \in BOOLEAN
    /\ activeViewWitness \in BOOLEAN

RetainedImagesStayWithinBudget ==
    retainedImages <= MaxRetainedImages

RetainedImageAccountingIsExact ==
    retainedImages = Cardinality(RetainedParticipants)

ParticipantOpensAtMostOnce ==
    \A participant \in Participants:
        openCount[participant] <= 1

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
        /\ releaseOnExit[callback]
        /\ groupState = "Open"
        => participantState[Target(callback)] # "Rejected"

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

EveryStartedOpenSettles ==
    \A participant \in Participants:
        participantState[participant] \in {"Opening", "Reserved"}
            ~> participantState[participant]
                \in {"Ready", "Rejected", "Released"}

DisposedGroupEventuallyReleases ==
    groupState = "Disposed" ~> groupState = "Released"

=============================================================================
