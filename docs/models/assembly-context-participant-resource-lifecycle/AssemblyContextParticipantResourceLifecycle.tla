---------- MODULE AssemblyContextParticipantResourceLifecycle ----------
EXTENDS FiniteSets, Integers, TLC

CONSTANTS
    ParticipantCount,
    ResourceCount,
    LeaseCount,
    AllowLateLease,
    AllowEarlyResourceRetirement,
    AllowEarlySnapshotRelease,
    AllowSiblingRelease

GroupIdentity == "AssemblyContextGroup"
NoGroupIdentity == "NoGroup"
NoReleaseResult == "NoReleaseResult"

ASSUME
    /\ ParticipantCount \in Nat \ {0}
    /\ ResourceCount \in Nat \ {0}
    /\ LeaseCount \in Nat \ {0}
    /\ AllowLateLease \in BOOLEAN
    /\ AllowEarlyResourceRetirement \in BOOLEAN
    /\ AllowEarlySnapshotRelease \in BOOLEAN
    /\ AllowSiblingRelease \in BOOLEAN
    /\ GroupIdentity # NoGroupIdentity
    /\ NoReleaseResult # "Succeeded"

Participants == 1..ParticipantCount
Resources == 1..ResourceCount
Leases == 1..LeaseCount

ParticipantStates == {"Live", "ReleaseRequested", "SnapshotReleased"}
ResourceStates == {"Live", "Retired"}
LeaseStates == {"Unused", "Active", "Closed"}

LeaseParticipant(lease) ==
    ((lease - 1) % ParticipantCount) + 1

LeaseResource(lease) ==
    (((lease - 1) \div ParticipantCount) % ResourceCount) + 1

VARIABLES
    participantState,
    resourceState,
    leaseState,
    retainedImages,
    requestedGroup,
    completedGroup,
    completionResult,
    leaseAdmissionWitness,
    resourceRetirementWitness,
    snapshotOrderWitness,
    siblingIndependenceWitness

localVars ==
    <<participantState, resourceState, leaseState, retainedImages,
      leaseAdmissionWitness, resourceRetirementWitness,
      snapshotOrderWitness, siblingIndependenceWitness>>

releaseVars == <<requestedGroup, completedGroup, completionResult>>

vars == <<localVars, releaseVars>>

ActivePairLeases(participant, resource) ==
    {lease \in Leases:
        /\ leaseState[lease] = "Active"
        /\ LeaseParticipant(lease) = participant
        /\ LeaseResource(lease) = resource}

ActiveParticipantLeases(participant) ==
    {lease \in Leases:
        /\ leaseState[lease] = "Active"
        /\ LeaseParticipant(lease) = participant}

AllResourcesRetired(participant) ==
    \A resource \in Resources:
        resourceState[participant][resource] = "Retired"

AllParticipantsReleased ==
    \A participant \in Participants:
        participantState[participant] = "SnapshotReleased"

GroupRelease ==
    INSTANCE AssemblyContextGroupReleaseLifecycle
        WITH Group <- GroupIdentity,
             NoGroup <- NoGroupIdentity,
             ReleaseResults <- {"Succeeded"},
             NoReleaseResult <- NoReleaseResult,
             requestedGroup <- requestedGroup,
             completedGroup <- completedGroup,
             completionResult <- completionResult

Init ==
    /\ participantState =
        [participant \in Participants |-> "Live"]
    /\ resourceState =
        [participant \in Participants |->
            [resource \in Resources |-> "Live"]]
    /\ leaseState =
        [lease \in Leases |-> "Unused"]
    /\ retainedImages = ParticipantCount
    /\ requestedGroup = NoGroupIdentity
    /\ completedGroup = NoGroupIdentity
    /\ completionResult = NoReleaseResult
    /\ leaseAdmissionWitness = TRUE
    /\ resourceRetirementWitness = TRUE
    /\ snapshotOrderWitness = TRUE
    /\ siblingIndependenceWitness = TRUE
    /\ GroupRelease!Init

AdmitLease(lease) ==
    LET participant == LeaseParticipant(lease)
        resource == LeaseResource(lease)
    IN
    /\ leaseState[lease] = "Unused"
    /\ resourceState[participant][resource] = "Live"
    /\ \/ /\ requestedGroup = NoGroupIdentity
          /\ participantState[participant] = "Live"
       \/ /\ AllowLateLease
          /\ participantState[participant] # "SnapshotReleased"
    /\ leaseState' =
        [leaseState EXCEPT ![lease] = "Active"]
    /\ leaseAdmissionWitness' =
        /\ leaseAdmissionWitness
        /\ requestedGroup = NoGroupIdentity
        /\ participantState[participant] = "Live"
    /\ UNCHANGED
        <<participantState, resourceState, retainedImages, releaseVars,
          resourceRetirementWitness, snapshotOrderWitness,
          siblingIndependenceWitness>>

CloseLease(lease) ==
    /\ leaseState[lease] = "Active"
    /\ leaseState' =
        [leaseState EXCEPT ![lease] = "Closed"]
    /\ UNCHANGED
        <<participantState, resourceState, retainedImages, releaseVars,
          leaseAdmissionWitness, resourceRetirementWitness,
          snapshotOrderWitness, siblingIndependenceWitness>>

RequestParticipantRelease(participant) ==
    /\ participantState[participant] = "Live"
    /\ participantState' =
        [participantState EXCEPT ![participant] = "ReleaseRequested"]
    /\ UNCHANGED
        <<resourceState, leaseState, retainedImages, releaseVars,
          leaseAdmissionWitness, resourceRetirementWitness,
          snapshotOrderWitness, siblingIndependenceWitness>>

RequestGroupParticipantRelease(participant) ==
    /\ requestedGroup = GroupIdentity
    /\ RequestParticipantRelease(participant)

RetireParticipantResource(participant, resource) ==
    /\ participantState[participant] = "ReleaseRequested"
    /\ resourceState[participant][resource] = "Live"
    /\ \/ ActivePairLeases(participant, resource) = {}
       \/ AllowEarlyResourceRetirement
    /\ resourceState' =
        [resourceState EXCEPT
            ![participant][resource] = "Retired"]
    /\ resourceRetirementWitness' =
        /\ resourceRetirementWitness
        /\ ActivePairLeases(participant, resource) = {}
    /\ UNCHANGED
        <<participantState, leaseState, retainedImages, releaseVars,
          leaseAdmissionWitness, snapshotOrderWitness,
          siblingIndependenceWitness>>

ReleaseParticipantSnapshot(participant) ==
    /\ participantState[participant] = "ReleaseRequested"
    /\ \/ /\ AllResourcesRetired(participant)
          /\ ActiveParticipantLeases(participant) = {}
       \/ AllowEarlySnapshotRelease
    /\ participantState' =
        [participantState EXCEPT ![participant] = "SnapshotReleased"]
    /\ retainedImages' = retainedImages - 1
    /\ snapshotOrderWitness' =
        /\ snapshotOrderWitness
        /\ AllResourcesRetired(participant)
        /\ ActiveParticipantLeases(participant) = {}
    /\ UNCHANGED
        <<resourceState, leaseState, releaseVars,
          leaseAdmissionWitness, resourceRetirementWitness,
          siblingIndependenceWitness>>

ReleaseFromSibling(participant, sibling) ==
    /\ AllowSiblingRelease
    /\ participant # sibling
    /\ participantState[participant] = "ReleaseRequested"
    /\ participantState[sibling] = "ReleaseRequested"
    /\ AllResourcesRetired(sibling)
    /\ ActiveParticipantLeases(sibling) = {}
    /\ participantState' =
        [participantState EXCEPT ![participant] = "SnapshotReleased"]
    /\ retainedImages' = retainedImages - 1
    /\ siblingIndependenceWitness' = FALSE
    /\ UNCHANGED
        <<resourceState, leaseState, releaseVars,
          leaseAdmissionWitness, resourceRetirementWitness,
          snapshotOrderWitness>>

RequestGroupRelease ==
    /\ GroupRelease!RequestRelease
    /\ UNCHANGED localVars

CompleteGroupRelease ==
    /\ GroupRelease!CompleteRelease("Succeeded", AllParticipantsReleased)
    /\ UNCHANGED localVars

Next ==
    \/ \E lease \in Leases:
        AdmitLease(lease)
    \/ \E lease \in Leases:
        CloseLease(lease)
    \/ \E participant \in Participants:
        RequestParticipantRelease(participant)
    \/ \E participant \in Participants:
        RequestGroupParticipantRelease(participant)
    \/ \E participant \in Participants:
       \E resource \in Resources:
        RetireParticipantResource(participant, resource)
    \/ \E participant \in Participants:
        ReleaseParticipantSnapshot(participant)
    \/ \E participant \in Participants:
       \E sibling \in Participants:
        ReleaseFromSibling(participant, sibling)
    \/ RequestGroupRelease
    \/ CompleteGroupRelease

Fairness ==
    /\ \A lease \in Leases:
        WF_vars(CloseLease(lease))
    /\ \A participant \in Participants:
        WF_vars(RequestGroupParticipantRelease(participant))
    /\ \A participant \in Participants:
       \A resource \in Resources:
        WF_vars(RetireParticipantResource(participant, resource))
    /\ \A participant \in Participants:
        WF_vars(ReleaseParticipantSnapshot(participant))
    /\ WF_vars(CompleteGroupRelease)

SafetySpec ==
    Init /\ [][Next]_vars

Spec ==
    SafetySpec /\ Fairness

TypeOK ==
    /\ participantState \in [Participants -> ParticipantStates]
    /\ resourceState \in
        [Participants -> [Resources -> ResourceStates]]
    /\ leaseState \in [Leases -> LeaseStates]
    /\ retainedImages \in 0..ParticipantCount
    /\ requestedGroup \in {NoGroupIdentity, GroupIdentity}
    /\ completedGroup \in {NoGroupIdentity, GroupIdentity}
    /\ completionResult \in {NoReleaseResult, "Succeeded"}
    /\ leaseAdmissionWitness \in BOOLEAN
    /\ resourceRetirementWitness \in BOOLEAN
    /\ snapshotOrderWitness \in BOOLEAN
    /\ siblingIndependenceWitness \in BOOLEAN

NoLeaseAdmissionAfterRelease ==
    leaseAdmissionWitness

ParticipantResourcesWaitForLeases ==
    resourceRetirementWitness

ParticipantResourcesPrecedeSnapshots ==
    snapshotOrderWitness

SiblingReleaseUsesOwnState ==
    siblingIndependenceWitness

ActiveLeasesRetainParticipants ==
    \A lease \in Leases:
        leaseState[lease] = "Active"
            => /\ participantState[LeaseParticipant(lease)]
                    # "SnapshotReleased"
               /\ resourceState
                    [LeaseParticipant(lease)]
                    [LeaseResource(lease)] = "Live"

RetainedImageAccountingIsExact ==
    retainedImages =
        Cardinality(
            {participant \in Participants:
                participantState[participant] # "SnapshotReleased"})

ResourcesPrecedeReleasedSnapshots ==
    \A participant \in Participants:
        participantState[participant] = "SnapshotReleased"
            => /\ AllResourcesRetired(participant)
               /\ ActiveParticipantLeases(participant) = {}

GroupCompletionRequiresParticipantRelease ==
    completedGroup = GroupIdentity
        => AllParticipantsReleased

ReleasedGroupOwnsNothing ==
    completedGroup = GroupIdentity
        => /\ retainedImages = 0
           /\ \A participant \in Participants:
                AllResourcesRetired(participant)
           /\ \A lease \in Leases:
                leaseState[lease] # "Active"

GroupReleaseCompletionMatchesRequest ==
    GroupRelease!CompletionMatchesRequest

GroupReleaseCompletionCarriesResult ==
    GroupRelease!CompletionCarriesResult

GroupReleaseBehaviorRefinesOwner ==
    GroupRelease!SafetySpec(AllParticipantsReleased)

EveryRequestedParticipantReleases ==
    \A participant \in Participants:
        participantState[participant] = "ReleaseRequested"
            ~> participantState[participant] = "SnapshotReleased"

RequestedGroupEventuallyCompletes ==
    GroupRelease!RequestedGroupEventuallyCompletes

=============================================================================
