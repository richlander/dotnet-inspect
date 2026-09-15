------------------ MODULE WorkspaceBindingPolicyRealization ------------------
(***************************************************************************)
(* Design model for workspace-owned realization of one assembly context     *)
(* carrying a completed composed binding policy.                            *)
(*                                                                         *)
(* The workspace issues one immutable preparation envelope, validates the   *)
(* returned completion against the exact participant, role, delegate-map    *)
(* (including its complete participant-origin routes), and policy-version   *)
(* inputs, constructs the group only after policy adoption, and atomically   *)
(* publishes or retires the group and policy. A second generation represents *)
(* a later authorized replacement after observed delegated-policy drift      *)
(* retires the first generation. A failed private attempt does not schedule  *)
(* that replacement.                                                        *)
(*                                                                         *)
(* The model treats artifact acquisition, candidate arbitration, policy     *)
(* answer semantics, query leases, and group cleanup as adjacent contracts. *)
(***************************************************************************)
EXTENDS FiniteSets, TLC

CONSTANTS
    GenerationOne,
    GenerationTwo,
    VersionOne,
    VersionTwo,
    ParticipantPlanOne,
    ParticipantPlanTwo,
    RoleProjectionOne,
    RoleProjectionTwo,
    DelegateMapOne,
    DelegateMapTwo,
    NoValue,
    EnforcePreparationMatch,
    EnforceParticipantMatch,
    EnforceRoleMatch,
    EnforceDelegateMapMatch,
    EnforceCompletionVersion,
    EnforceFailureClassification,
    EnforcePolicyBeforeGroup,
    EnforceConstructedPolicyIdentity,
    EnforceBindingVersionLifecycle,
    EnforcePublishVersion,
    EnforceAtomicPublication,
    EnforceAtomicRetirement,
    EnforceNoAutomaticRetryAfterFailure,
    EnforceRetirementBeforeReplacementStart,
    EnforceRetirementBeforeReplacement

Generations == {GenerationOne, GenerationTwo}
Versions == {VersionOne, VersionTwo}
ParticipantPlans == {ParticipantPlanOne, ParticipantPlanTwo}
RoleProjections == {RoleProjectionOne, RoleProjectionTwo}
DelegateMaps == {DelegateMapOne, DelegateMapTwo}

ASSUME
    /\ GenerationOne # GenerationTwo
    /\ VersionOne # VersionTwo
    /\ ParticipantPlanOne # ParticipantPlanTwo
    /\ RoleProjectionOne # RoleProjectionTwo
    /\ DelegateMapOne # DelegateMapTwo
    /\ NoValue \notin
        Generations \union Versions \union ParticipantPlans
            \union RoleProjections \union DelegateMaps
    /\ EnforcePreparationMatch \in BOOLEAN
    /\ EnforceParticipantMatch \in BOOLEAN
    /\ EnforceRoleMatch \in BOOLEAN
    /\ EnforceDelegateMapMatch \in BOOLEAN
    /\ EnforceCompletionVersion \in BOOLEAN
    /\ EnforceFailureClassification \in BOOLEAN
    /\ EnforcePolicyBeforeGroup \in BOOLEAN
    /\ EnforceConstructedPolicyIdentity \in BOOLEAN
    /\ EnforceBindingVersionLifecycle \in BOOLEAN
    /\ EnforcePublishVersion \in BOOLEAN
    /\ EnforceAtomicPublication \in BOOLEAN
    /\ EnforceAtomicRetirement \in BOOLEAN
    /\ EnforceNoAutomaticRetryAfterFailure \in BOOLEAN
    /\ EnforceRetirementBeforeReplacementStart \in BOOLEAN
    /\ EnforceRetirementBeforeReplacement \in BOOLEAN

Phases ==
    {"Absent", "Preparing", "Completed", "Adopted", "Ready",
     "Published", "Failed", "Retired"}

CompletionModes ==
    {"Exact", "ForeignPreparation", "ParticipantPlanMismatch",
     "RoleProjectionMismatch", "DelegateMapMismatch",
     "CompletionVersionMismatch"}

AdvanceTimings ==
    {"Never", "BeforeCompletion", "AfterCompletion", "AfterAdoption",
     "AfterGroupConstruction", "AfterPublication"}

FailureKinds ==
    {"None", "ForeignPreparation", "ParticipantPlanMismatch",
     "RoleProjectionMismatch", "DelegateMapMismatch", "CompletionVersionMismatch",
     "PolicyVersionMismatch"}

ExpectedParticipants(g) ==
    IF g = GenerationOne
    THEN ParticipantPlanOne
    ELSE ParticipantPlanTwo

ExpectedRoles(g) ==
    IF g = GenerationOne
    THEN RoleProjectionOne
    ELSE RoleProjectionTwo

ExpectedDelegateMap(g) ==
    IF g = GenerationOne
    THEN DelegateMapOne
    ELSE DelegateMapTwo

OtherGeneration(g) ==
    IF g = GenerationOne
    THEN GenerationTwo
    ELSE GenerationOne

OtherParticipants(g) ==
    IF ExpectedParticipants(g) = ParticipantPlanOne
    THEN ParticipantPlanTwo
    ELSE ParticipantPlanOne

OtherRoles(g) ==
    IF ExpectedRoles(g) = RoleProjectionOne
    THEN RoleProjectionTwo
    ELSE RoleProjectionOne

OtherDelegateMap(g) ==
    IF ExpectedDelegateMap(g) = DelegateMapOne
    THEN DelegateMapTwo
    ELSE DelegateMapOne

OtherVersion(v) ==
    IF v = VersionOne
    THEN VersionTwo
    ELSE VersionOne

VARIABLES
    phase,
    activeGeneration,
    capturedVersion,
    completionMode,
    advanceTiming,
    completionPreparation,
    completionParticipants,
    completionRoles,
    completionDelegateMap,
    completionVersion,
    adoptedPolicy,
    groupConstructed,
    constructedPolicy,
    failure,
    publishedGroup,
    publishedPolicy,
    everPublished,
    liveVersion,
    versionAdvanced,
    currentAccessRequested,
    driftObserved,
    publishVersionWitness,
    retirementAtomicWitness

BindingVersion ==
    INSTANCE AssemblyBindingPolicyVersionLifecycle WITH
        InitialVersion <- VersionOne,
        ReplacementVersion <- VersionTwo,
        version <- liveVersion,
        advanced <- versionAdvanced

EffectiveMode(g) ==
    IF g = GenerationOne
    THEN completionMode
    ELSE "Exact"

ReturnedPreparation(g) ==
    IF EffectiveMode(g) = "ForeignPreparation"
    THEN OtherGeneration(g)
    ELSE g

ReturnedParticipants(g) ==
    IF EffectiveMode(g) = "ParticipantPlanMismatch"
    THEN OtherParticipants(g)
    ELSE ExpectedParticipants(g)

ReturnedRoles(g) ==
    IF EffectiveMode(g) = "RoleProjectionMismatch"
    THEN OtherRoles(g)
    ELSE ExpectedRoles(g)

ReturnedDelegateMap(g) ==
    IF EffectiveMode(g) = "DelegateMapMismatch"
    THEN OtherDelegateMap(g)
    ELSE ExpectedDelegateMap(g)

ReturnedVersion(g) ==
    IF EffectiveMode(g) = "CompletionVersionMismatch"
    THEN OtherVersion(capturedVersion[g])
    ELSE capturedVersion[g]

StaticCompletionMatches(g) ==
    /\ completionPreparation[g] = g
    /\ completionParticipants[g] = ExpectedParticipants(g)
    /\ completionRoles[g] = ExpectedRoles(g)
    /\ completionDelegateMap[g] = ExpectedDelegateMap(g)
    /\ completionVersion[g] = capturedVersion[g]

CompletionAcceptedByConfiguredChecks(g) ==
    /\ (~EnforcePreparationMatch
        \/ completionPreparation[g] = g)
    /\ (~EnforceParticipantMatch
        \/ completionParticipants[g] = ExpectedParticipants(g))
    /\ (~EnforceRoleMatch
        \/ completionRoles[g] = ExpectedRoles(g))
    /\ (~EnforceDelegateMapMatch
        \/ completionDelegateMap[g] = ExpectedDelegateMap(g))
    /\ (~EnforceCompletionVersion
        \/ completionVersion[g] = capturedVersion[g])

CompletionFailure(g) ==
    CASE completionPreparation[g] # g ->
            "ForeignPreparation"
      [] completionParticipants[g] # ExpectedParticipants(g) ->
            "ParticipantPlanMismatch"
      [] completionRoles[g] # ExpectedRoles(g) ->
            "RoleProjectionMismatch"
      [] completionDelegateMap[g] # ExpectedDelegateMap(g) ->
            "DelegateMapMismatch"
      [] completionVersion[g] # capturedVersion[g] ->
            "CompletionVersionMismatch"
      [] OTHER -> "PolicyVersionMismatch"

NoPublishedState ==
    /\ publishedGroup = NoValue
    /\ publishedPolicy = NoValue

CurrentGeneration(g) ==
    /\ publishedGroup = g
    /\ publishedPolicy = g

TimingBlocks(nextPhase) ==
    /\ ~versionAdvanced
    /\ CASE nextPhase = "Complete" ->
                advanceTiming = "BeforeCompletion"
          [] nextPhase = "Adopt" ->
                advanceTiming = "AfterCompletion"
          [] nextPhase = "Construct" ->
                advanceTiming = "AfterAdoption"
          [] nextPhase = "Publish" ->
                advanceTiming = "AfterGroupConstruction"
          [] OTHER -> FALSE

vars ==
    <<phase, activeGeneration, capturedVersion, completionMode,
      advanceTiming, completionPreparation, completionParticipants,
      completionRoles, completionDelegateMap, completionVersion,
      adoptedPolicy, groupConstructed, constructedPolicy, failure,
      publishedGroup,
      publishedPolicy, everPublished, liveVersion, versionAdvanced,
      currentAccessRequested, driftObserved, publishVersionWitness,
      retirementAtomicWitness>>

Init ==
    /\ phase = [g \in Generations |-> "Absent"]
    /\ activeGeneration = NoValue
    /\ capturedVersion = [g \in Generations |-> NoValue]
    /\ completionMode \in CompletionModes
    /\ advanceTiming \in AdvanceTimings
    /\ (completionMode # "Exact" => advanceTiming = "Never")
    /\ completionPreparation = [g \in Generations |-> NoValue]
    /\ completionParticipants = [g \in Generations |-> NoValue]
    /\ completionRoles = [g \in Generations |-> NoValue]
    /\ completionDelegateMap = [g \in Generations |-> NoValue]
    /\ completionVersion = [g \in Generations |-> NoValue]
    /\ adoptedPolicy = [g \in Generations |-> FALSE]
    /\ groupConstructed = [g \in Generations |-> FALSE]
    /\ constructedPolicy = [g \in Generations |-> NoValue]
    /\ failure = [g \in Generations |-> "None"]
    /\ publishedGroup = NoValue
    /\ publishedPolicy = NoValue
    /\ everPublished = {}
    /\ BindingVersion!Init
    /\ currentAccessRequested = {}
    /\ driftObserved = {}
    /\ publishVersionWitness = TRUE
    /\ retirementAtomicWitness = TRUE

CanStart(g) ==
    /\ phase[g] = "Absent"
    /\ activeGeneration = NoValue
    /\ IF g = GenerationOne
       THEN
            /\ phase[GenerationTwo] = "Absent"
            /\ NoPublishedState
       ELSE
            /\ (phase[GenerationOne] = "Retired"
                \/ (/\ ~EnforceRetirementBeforeReplacementStart
                    /\ phase[GenerationOne] = "Published"
                    /\ CurrentGeneration(GenerationOne)
                    /\ liveVersion # capturedVersion[GenerationOne])
                \/ (/\ ~EnforceNoAutomaticRetryAfterFailure
                    /\ phase[GenerationOne] = "Failed"))
            /\ (NoPublishedState
                \/ ~EnforceRetirementBeforeReplacementStart)

StartPreparation(g) ==
    /\ CanStart(g)
    /\ phase' = [phase EXCEPT ![g] = "Preparing"]
    /\ activeGeneration' = g
    /\ capturedVersion' = [capturedVersion EXCEPT ![g] = liveVersion]
    /\ UNCHANGED
        <<completionMode, advanceTiming, completionPreparation,
          completionParticipants, completionRoles, completionDelegateMap,
          completionVersion, adoptedPolicy, groupConstructed,
          constructedPolicy, failure,
          publishedGroup, publishedPolicy, everPublished, liveVersion,
          versionAdvanced, currentAccessRequested, driftObserved,
          publishVersionWitness,
          retirementAtomicWitness>>

CompletePolicy(g) ==
    /\ activeGeneration = g
    /\ phase[g] = "Preparing"
    /\ ~TimingBlocks("Complete")
    /\ phase' = [phase EXCEPT ![g] = "Completed"]
    /\ completionPreparation' =
        [completionPreparation EXCEPT ![g] = ReturnedPreparation(g)]
    /\ completionParticipants' =
        [completionParticipants EXCEPT ![g] = ReturnedParticipants(g)]
    /\ completionRoles' =
        [completionRoles EXCEPT ![g] = ReturnedRoles(g)]
    /\ completionDelegateMap' =
        [completionDelegateMap EXCEPT ![g] = ReturnedDelegateMap(g)]
    /\ completionVersion' =
        [completionVersion EXCEPT ![g] = ReturnedVersion(g)]
    /\ UNCHANGED
        <<activeGeneration, capturedVersion, completionMode, advanceTiming,
          adoptedPolicy, groupConstructed, constructedPolicy, failure,
          publishedGroup,
          publishedPolicy, everPublished, liveVersion, versionAdvanced,
          currentAccessRequested, driftObserved, publishVersionWitness,
          retirementAtomicWitness>>

AdoptPolicy(g) ==
    /\ activeGeneration = g
    /\ phase[g] = "Completed"
    /\ ~TimingBlocks("Adopt")
    /\ IF /\ CompletionAcceptedByConfiguredChecks(g)
          /\ liveVersion = capturedVersion[g]
       THEN
            /\ phase' = [phase EXCEPT ![g] = "Adopted"]
            /\ adoptedPolicy' = [adoptedPolicy EXCEPT ![g] = TRUE]
            /\ UNCHANGED <<activeGeneration, failure>>
       ELSE
            /\ phase' = [phase EXCEPT ![g] = "Failed"]
            /\ adoptedPolicy' = [adoptedPolicy EXCEPT ![g] = FALSE]
            /\ failure' =
                [failure EXCEPT ![g] =
                    IF EnforceFailureClassification
                    THEN CompletionFailure(g)
                    ELSE "PolicyVersionMismatch"]
            /\ activeGeneration' = NoValue
    /\ UNCHANGED
        <<capturedVersion, completionMode, advanceTiming,
          completionPreparation, completionParticipants, completionRoles,
          completionDelegateMap, completionVersion, groupConstructed,
          constructedPolicy,
          publishedGroup, publishedPolicy, everPublished, liveVersion,
          versionAdvanced, currentAccessRequested, driftObserved,
          publishVersionWitness,
          retirementAtomicWitness>>

ConstructGroup(g) ==
    /\ activeGeneration = g
    /\ ~TimingBlocks("Construct")
    /\ IF EnforcePolicyBeforeGroup
       THEN phase[g] = "Adopted"
       ELSE
            /\ phase[g] \in {"Completed", "Adopted"}
            /\ StaticCompletionMatches(g)
            /\ liveVersion = capturedVersion[g]
    /\ phase' = [phase EXCEPT ![g] = "Ready"]
    /\ groupConstructed' = [groupConstructed EXCEPT ![g] = TRUE]
    /\ constructedPolicy' =
        [constructedPolicy EXCEPT ![g] =
            IF EnforceConstructedPolicyIdentity
            THEN g
            ELSE OtherGeneration(g)]
    /\ UNCHANGED
        <<activeGeneration, capturedVersion, completionMode, advanceTiming,
          completionPreparation, completionParticipants, completionRoles,
          completionDelegateMap, completionVersion, adoptedPolicy, failure,
          publishedGroup, publishedPolicy, everPublished, liveVersion,
          versionAdvanced, currentAccessRequested, driftObserved,
          publishVersionWitness,
          retirementAtomicWitness>>

PublishGeneration(g) ==
    /\ activeGeneration = g
    /\ phase[g] = "Ready"
    /\ ~TimingBlocks("Publish")
    /\ (~EnforcePublishVersion
        \/ liveVersion = capturedVersion[g])
    /\ IF EnforceRetirementBeforeReplacement
       THEN NoPublishedState
       ELSE
            /\ (NoPublishedState
                \/ (/\ g = GenerationTwo
                    /\ CurrentGeneration(GenerationOne)
                    /\ liveVersion # capturedVersion[GenerationOne]))
    /\ phase' = [phase EXCEPT ![g] = "Published"]
    /\ activeGeneration' = NoValue
    /\ publishedGroup' = g
    /\ publishedPolicy' =
        IF EnforceAtomicPublication
        THEN g
        ELSE NoValue
    /\ everPublished' = everPublished \union {g}
    /\ publishVersionWitness' =
        (publishVersionWitness
            /\ liveVersion = capturedVersion[g]
        )
    /\ UNCHANGED
        <<capturedVersion, completionMode, advanceTiming,
          completionPreparation, completionParticipants, completionRoles,
          completionDelegateMap, completionVersion, adoptedPolicy,
          groupConstructed, constructedPolicy, failure, liveVersion,
          versionAdvanced,
          currentAccessRequested, driftObserved,
          retirementAtomicWitness>>

InvalidatePrivateGeneration(g) ==
    /\ activeGeneration = g
    /\ phase[g] \in {"Preparing", "Completed", "Adopted", "Ready"}
    /\ capturedVersion[g] # liveVersion
    /\ phase' = [phase EXCEPT ![g] = "Failed"]
    /\ activeGeneration' = NoValue
    /\ adoptedPolicy' = [adoptedPolicy EXCEPT ![g] = FALSE]
    /\ groupConstructed' = [groupConstructed EXCEPT ![g] = FALSE]
    /\ constructedPolicy' =
        [constructedPolicy EXCEPT ![g] = NoValue]
    /\ failure' = [failure EXCEPT ![g] = "PolicyVersionMismatch"]
    /\ UNCHANGED
        <<capturedVersion, completionMode, advanceTiming,
          completionPreparation, completionParticipants, completionRoles,
          completionDelegateMap, completionVersion, publishedGroup,
          publishedPolicy, everPublished, liveVersion, versionAdvanced,
          currentAccessRequested, driftObserved, publishVersionWitness,
          retirementAtomicWitness>>

AdvanceComposedPolicyVersion ==
    /\ ~versionAdvanced
    /\ liveVersion = VersionOne
    /\ CASE advanceTiming = "BeforeCompletion" ->
                phase[GenerationOne] = "Preparing"
          [] advanceTiming = "AfterCompletion" ->
                phase[GenerationOne] = "Completed"
          [] advanceTiming = "AfterAdoption" ->
                phase[GenerationOne] = "Adopted"
          [] advanceTiming = "AfterGroupConstruction" ->
                phase[GenerationOne] = "Ready"
          [] advanceTiming = "AfterPublication" ->
                phase[GenerationOne] = "Published"
          [] OTHER -> FALSE
    /\ IF EnforceBindingVersionLifecycle
       THEN BindingVersion!Advance
       ELSE
            /\ liveVersion' = liveVersion
            /\ versionAdvanced' = TRUE
    /\ UNCHANGED
        <<phase, activeGeneration, capturedVersion, completionMode,
          advanceTiming, completionPreparation, completionParticipants,
          completionRoles, completionDelegateMap, completionVersion,
          adoptedPolicy, groupConstructed, constructedPolicy, failure,
          publishedGroup,
          publishedPolicy, everPublished, currentAccessRequested,
          driftObserved,
          publishVersionWitness, retirementAtomicWitness>>

RequestCurrentAccess(g) ==
    /\ CurrentGeneration(g)
    /\ capturedVersion[g] # liveVersion
    /\ g \notin currentAccessRequested
    /\ currentAccessRequested' = currentAccessRequested \union {g}
    /\ UNCHANGED
        <<phase, activeGeneration, capturedVersion, completionMode,
          advanceTiming, completionPreparation, completionParticipants,
          completionRoles, completionDelegateMap, completionVersion,
          adoptedPolicy, groupConstructed, constructedPolicy, failure,
          publishedGroup,
          publishedPolicy, everPublished, liveVersion, versionAdvanced,
          driftObserved, publishVersionWitness, retirementAtomicWitness>>

ObservePublishedDrift(g) ==
    /\ CurrentGeneration(g)
    /\ capturedVersion[g] # liveVersion
    /\ g \in currentAccessRequested
    /\ phase' = [phase EXCEPT ![g] = "Retired"]
    /\ publishedGroup' = NoValue
    /\ publishedPolicy' =
        IF EnforceAtomicRetirement
        THEN NoValue
        ELSE g
    /\ driftObserved' = driftObserved \union {g}
    /\ retirementAtomicWitness' =
        (retirementAtomicWitness
            /\ publishedGroup' = NoValue
            /\ publishedPolicy' = NoValue
        )
    /\ currentAccessRequested' = currentAccessRequested \ {g}
    /\ UNCHANGED
        <<activeGeneration, capturedVersion, completionMode, advanceTiming,
          completionPreparation, completionParticipants, completionRoles,
          completionDelegateMap, completionVersion, adoptedPolicy,
          groupConstructed, constructedPolicy, failure, everPublished,
          liveVersion,
          versionAdvanced, publishVersionWitness>>

StartAny == \E g \in Generations : StartPreparation(g)
CompleteAny == \E g \in Generations : CompletePolicy(g)
AdoptAny == \E g \in Generations : AdoptPolicy(g)
ConstructAny == \E g \in Generations : ConstructGroup(g)
PublishAny == \E g \in Generations : PublishGeneration(g)
InvalidateAny == \E g \in Generations : InvalidatePrivateGeneration(g)
ObserveDriftAny == \E g \in Generations : ObservePublishedDrift(g)
RequestCurrentAccessAny ==
    \E g \in Generations : RequestCurrentAccess(g)

Next ==
    StartAny
    \/ CompleteAny
    \/ AdoptAny
    \/ ConstructAny
    \/ PublishAny
    \/ InvalidateAny
    \/ AdvanceComposedPolicyVersion
    \/ RequestCurrentAccessAny
    \/ ObserveDriftAny

Spec ==
    /\ Init
    /\ [][Next]_vars
    /\ WF_vars(StartPreparation(GenerationOne))
    /\ WF_vars(CompleteAny)
    /\ WF_vars(AdoptAny)
    /\ WF_vars(ConstructAny)
    /\ WF_vars(PublishAny)
    /\ WF_vars(InvalidateAny)
    /\ WF_vars(AdvanceComposedPolicyVersion)
    /\ WF_vars(ObserveDriftAny)

TypeOK ==
    /\ phase \in [Generations -> Phases]
    /\ activeGeneration \in Generations \union {NoValue}
    /\ capturedVersion \in
        [Generations -> Versions \union {NoValue}]
    /\ completionMode \in CompletionModes
    /\ advanceTiming \in AdvanceTimings
    /\ completionPreparation \in
        [Generations -> Generations \union {NoValue}]
    /\ completionParticipants \in
        [Generations -> ParticipantPlans \union {NoValue}]
    /\ completionRoles \in
        [Generations -> RoleProjections \union {NoValue}]
    /\ completionDelegateMap \in
        [Generations -> DelegateMaps \union {NoValue}]
    /\ completionVersion \in
        [Generations -> Versions \union {NoValue}]
    /\ adoptedPolicy \in [Generations -> BOOLEAN]
    /\ groupConstructed \in [Generations -> BOOLEAN]
    /\ constructedPolicy \in
        [Generations -> Generations \union {NoValue}]
    /\ failure \in [Generations -> FailureKinds]
    /\ publishedGroup \in Generations \union {NoValue}
    /\ publishedPolicy \in Generations \union {NoValue}
    /\ everPublished \subseteq Generations
    /\ BindingVersion!TypeOK
    /\ currentAccessRequested \subseteq Generations
    /\ driftObserved \subseteq Generations
    /\ publishVersionWitness \in BOOLEAN
    /\ retirementAtomicWitness \in BOOLEAN

PublicationIsAtomic ==
    /\ (publishedGroup = NoValue) <=> (publishedPolicy = NoValue)
    /\ publishedGroup # NoValue =>
        publishedGroup = publishedPolicy

PublishedGenerationIsComplete ==
    \A g \in Generations :
        CurrentGeneration(g) =>
            /\ phase[g] = "Published"
            /\ adoptedPolicy[g]
            /\ groupConstructed[g]
            /\ constructedPolicy[g] = g
            /\ StaticCompletionMatches(g)

GroupConstructionRequiresPolicyAdoption ==
    \A g \in Generations :
        groupConstructed[g] => adoptedPolicy[g]

ConstructedParticipantsUseAdoptedPolicy ==
    \A g \in Generations :
        /\ groupConstructed[g] <=> constructedPolicy[g] # NoValue
        /\ groupConstructed[g] =>
            /\ adoptedPolicy[g]
            /\ constructedPolicy[g] = g

AdoptedPolicyMatchesPreparation ==
    \A g \in Generations :
        adoptedPolicy[g] => completionPreparation[g] = g

AdoptedPolicyMatchesParticipants ==
    \A g \in Generations :
        adoptedPolicy[g] =>
            completionParticipants[g] = ExpectedParticipants(g)

AdoptedPolicyMatchesRoles ==
    \A g \in Generations :
        adoptedPolicy[g] =>
            completionRoles[g] = ExpectedRoles(g)

AdoptedPolicyMatchesDelegateMap ==
    \A g \in Generations :
        adoptedPolicy[g] =>
            completionDelegateMap[g] = ExpectedDelegateMap(g)

AdoptedPolicyMatchesCapturedVersion ==
    \A g \in Generations :
        adoptedPolicy[g] =>
            completionVersion[g] = capturedVersion[g]

FailureClassificationIsExact ==
    \A g \in Generations :
        phase[g] = "Failed" =>
            /\ failure[g] # "None"
            /\ failure[g] =
                IF completionPreparation[g] = NoValue
                THEN "PolicyVersionMismatch"
                ELSE CompletionFailure(g)

FailedGenerationIsUnavailable ==
    \A g \in Generations :
        phase[g] = "Failed" =>
            /\ publishedGroup # g
            /\ publishedPolicy # g

RetiredGenerationIsUnavailable ==
    \A g \in driftObserved :
        /\ publishedGroup # g
        /\ publishedPolicy # g

ReplacementFollowsRetirement ==
    /\ GenerationOne \in everPublished
    /\ GenerationTwo \in everPublished
    =>
        phase[GenerationOne] = "Retired"

ReplacementStartsAfterRetirement ==
    /\ phase[GenerationTwo] # "Absent"
    /\ phase[GenerationOne] # "Failed"
    =>
        phase[GenerationOne] = "Retired"

FailedPrivateAttemptDoesNotRetry ==
    phase[GenerationOne] = "Failed" =>
        phase[GenerationTwo] = "Absent"

PublicationObservedCurrentVersion ==
    publishVersionWitness

BindingVersionAdvanceIsFresh ==
    BindingVersion!AdvancedVersionIsFresh

BindingVersionBehaviorRefinesOwner ==
    BindingVersion!SafetySpec

RetirementWasAtomic ==
    retirementAtomicWitness

EveryStartedGenerationSettles ==
    \A g \in Generations :
        phase[g] # "Absent"
            ~> phase[g] \in {"Failed", "Published", "Retired"}

ObservedVersionDriftEventuallyRetires ==
    \A g \in Generations :
        /\ CurrentGeneration(g)
        /\ capturedVersion[g] # liveVersion
        /\ g \in currentAccessRequested
        ~> phase[g] = "Retired"

StartedReplacementEventuallyPublishes ==
    phase[GenerationTwo] = "Preparing" ~> phase[GenerationTwo] = "Published"

ReplacementNotReached ==
    phase[GenerationTwo] # "Published"

=============================================================================
