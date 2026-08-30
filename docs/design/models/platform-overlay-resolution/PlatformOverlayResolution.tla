----------------------- MODULE PlatformOverlayResolution -----------------------
EXTENDS FiniteSets, Integers, Sequences, TLC

\* Owned by docs/design/platform-composition-and-overlays.md.
\* Candidate acquisition, identity decoding, workspace-role assignment, and
\* compatibility computation are inputs. The model owns validation of the
\* closed role snapshot, arbitration among its classified candidates, and
\* attribution when a platform cannot satisfy an overlay traversal request.

CONSTANTS
    DesignatedOne,
    DesignatedTwo,
    Platform,
    Unentitled,
    ExactReference,
    SkewedReference,
    AvailableMember,
    UnavailableMember,
    NoCandidate,
    SelectionMode,
    TraversalMode,
    RoleValidationMode

Candidates == {DesignatedOne, DesignatedTwo, Platform, Unentitled}
CanonicalDesignatedCandidates == {DesignatedOne, DesignatedTwo}
CanonicalPlatformCandidates == {Platform}
References == {ExactReference, SkewedReference}
Members == {AvailableMember, UnavailableMember}
TraversalRequests == References \X Members
CallerDesignated == "CallerDesignated"
PlatformAuthorized == "PlatformAuthorized"
WorkspaceRoles == {CallerDesignated, PlatformAuthorized}
RoleEvidenceModes ==
    {"Pending", "Valid", "Missing", "Foreign", "Stale", "Replayed",
     "WrongGroup", "Incomplete", "Extra", "Contradictory"}
CurrentGroup == "CurrentGroup"
ForeignGroup == "ForeignGroup"
NoGroup == "NoGroup"
CurrentGeneration == "CurrentGeneration"
ForeignGeneration == "ForeignGeneration"
StaleGeneration == "StaleGeneration"
NoGeneration == "NoGeneration"

ASSUME
    /\ Cardinality(Candidates) = 4
    /\ Cardinality(References) = 2
    /\ Cardinality(Members) = 2
    /\ NoCandidate \notin Candidates
    /\ SelectionMode \in
        {"Policy", "RegistrationOrder", "VersionSensitive"}
    /\ TraversalMode \in
        {"Policy", "RejectSkew", "SuppressFailure"}
    /\ RoleValidationMode \in {"Policy", "LegacyFallback"}

Phases == {"Registering", "Loaded", "Resolved", "Traversed"}
ResolutionFailures ==
    {"Pending", "None", "NoMatch", "Ambiguous", "InvalidRoleEvidence"}
TraversalOutcomes ==
    {"NotStarted", "Found", "ResolutionFailed",
     "CompatibilityFailure", "Missing"}

NoDuplicates(sequence) ==
    \A left, right \in 1..Len(sequence):
        left # right => sequence[left] # sequence[right]

RegisteredSet(sequence) ==
    {sequence[index] : index \in 1..Len(sequence)}

RegistrationSequences ==
    {sequence \in UNION {[1..length -> Candidates] : length \in 0..4}:
        NoDuplicates(sequence)}

RegistrationPermutations(sequence) ==
    {candidate \in [1..Len(sequence) -> Candidates]:
        /\ NoDuplicates(candidate)
        /\ RegisteredSet(candidate) = RegisteredSet(sequence)}

CanonicalRoles(candidate) ==
    IF candidate \in CanonicalDesignatedCandidates
    THEN {CallerDesignated}
    ELSE
        IF candidate \in CanonicalPlatformCandidates
        THEN {PlatformAuthorized}
        ELSE {}

CanonicalRoleAssignments ==
    [candidate \in Candidates |-> CanonicalRoles(candidate)]

ContradictoryRoleAssignments(witness) ==
    [candidate \in Candidates |->
        IF candidate = witness
        THEN {CallerDesignated, PlatformAuthorized}
        ELSE CanonicalRoles(candidate)]

RolesAt(domain, assignments, candidate) ==
    IF candidate \in domain THEN assignments[candidate] ELSE {}

RoleEvidenceValidAt(
        mode,
        group,
        generation,
        domain,
        assignments,
        sequence) ==
    /\ mode # "Pending"
    /\ mode # "Missing"
    /\ group = CurrentGroup
    /\ generation = CurrentGeneration
    /\ domain = RegisteredSet(sequence)
    /\ \A candidate \in domain:
        /\ assignments[candidate] \subseteq WorkspaceRoles
        /\ ~(/\ CallerDesignated \in assignments[candidate]
             /\ PlatformAuthorized \in assignments[candidate])

VARIABLES
    phase,
    registration,
    roleEvidenceMode,
    roleGroup,
    roleGeneration,
    roleDomain,
    roleAssignments,
    loadWarning,
    selection,
    shadowed,
    resolutionFailure,
    traversal

vars ==
    <<phase, registration, roleEvidenceMode, roleGroup, roleGeneration,
      roleDomain, roleAssignments, loadWarning, selection, shadowed,
      resolutionFailure, traversal>>

RoleEvidenceStructurallyValid ==
    RoleEvidenceValidAt(
        roleEvidenceMode,
        roleGroup,
        roleGeneration,
        roleDomain,
        roleAssignments,
        registration)

RoleEvidenceAccepted ==
    IF RoleValidationMode = "Policy"
    THEN RoleEvidenceStructurallyValid
    ELSE roleEvidenceMode # "Pending"

BindingRoles(candidate) ==
    IF /\ RoleValidationMode = "LegacyFallback"
       /\ ~RoleEvidenceStructurallyValid
    THEN CanonicalRoles(candidate)
    ELSE RolesAt(roleDomain, roleAssignments, candidate)

DesignatedCandidatesFor(sequence) ==
    {candidate \in RegisteredSet(sequence):
        CallerDesignated \in BindingRoles(candidate)}

PlatformCandidatesFor(sequence) ==
    {candidate \in RegisteredSet(sequence):
        PlatformAuthorized \in BindingRoles(candidate)}

EntitledCandidatesFor(sequence) ==
    DesignatedCandidatesFor(sequence) \union PlatformCandidatesFor(sequence)

\* Every modeled candidate has the requested name and can bind under the
\* adjacent identity policy. Authority comes only from the accepted snapshot.
EligibleFor(sequence, reference) ==
    {candidate \in RegisteredSet(sequence):
        /\ RoleEvidenceAccepted
        /\ candidate \in EntitledCandidatesFor(sequence)
        /\ reference \in References}

TopCandidates(sequence, reference) ==
    LET eligible == EligibleFor(sequence, reference)
        designated == eligible \cap DesignatedCandidatesFor(sequence)
        platform == eligible \cap PlatformCandidatesFor(sequence)
    IN
        IF designated # {} THEN designated ELSE platform

PolicyCandidate(sequence, reference) ==
    LET top == TopCandidates(sequence, reference)
    IN
        IF Cardinality(top) = 1
        THEN CHOOSE candidate \in top : TRUE
        ELSE NoCandidate

FirstEligibleIndex(sequence, reference) ==
    CHOOSE index \in 1..Len(sequence):
        /\ sequence[index] \in EligibleFor(sequence, reference)
        /\ \A earlier \in 1..(index - 1):
            sequence[earlier] \notin EligibleFor(sequence, reference)

RegistrationOrderCandidate(sequence, reference) ==
    IF EligibleFor(sequence, reference) = {}
    THEN NoCandidate
    ELSE sequence[FirstEligibleIndex(sequence, reference)]

\* ExactReference happens to version-match the platform candidate, while
\* SkewedReference happens to version-match the designated candidates.
\* Both references are otherwise bindable under the owner contract.
VersionEqual(reference, candidate) ==
    IF reference = ExactReference
    THEN candidate = Platform
    ELSE candidate \in CanonicalDesignatedCandidates

VersionSensitiveCandidate(sequence, reference) ==
    LET policy == PolicyCandidate(sequence, reference)
        eligible == EligibleFor(sequence, reference)
        matchingPlatforms ==
            {candidate \in eligible \cap PlatformCandidatesFor(sequence):
                VersionEqual(reference, candidate)}
    IN
        IF policy = NoCandidate
        THEN NoCandidate
        ELSE
            IF /\ eligible \cap DesignatedCandidatesFor(sequence) # {}
               /\ matchingPlatforms # {}
            THEN CHOOSE candidate \in matchingPlatforms : TRUE
            ELSE policy

CandidateFor(sequence, reference) ==
    CASE SelectionMode = "Policy" ->
            PolicyCandidate(sequence, reference)
      [] SelectionMode = "RegistrationOrder" ->
            RegistrationOrderCandidate(sequence, reference)
      [] OTHER ->
            VersionSensitiveCandidate(sequence, reference)

FailureFor(sequence, reference) ==
    IF ~RoleEvidenceAccepted
    THEN "InvalidRoleEvidence"
    ELSE
        IF CandidateFor(sequence, reference) # NoCandidate
        THEN "None"
        ELSE
            IF EligibleFor(sequence, reference) = {}
            THEN "NoMatch"
            ELSE "Ambiguous"

ShadowedFor(sequence, reference) ==
    LET selected == CandidateFor(sequence, reference)
    IN
        IF selected = NoCandidate
        THEN {}
        ELSE EligibleFor(sequence, reference) \ {selected}

KnownSkewAt(sequence, reference, domain, assignments) ==
    /\ reference = SkewedReference
    /\ {candidate \in RegisteredSet(sequence):
            CallerDesignated \in RolesAt(domain, assignments, candidate)}
        # {}
    /\ {candidate \in RegisteredSet(sequence):
            PlatformAuthorized \in RolesAt(domain, assignments, candidate)}
        # {}

KnownSkewFor(sequence, reference) ==
    /\ RoleEvidenceAccepted
    /\ reference = SkewedReference
    /\ DesignatedCandidatesFor(sequence) # {}
    /\ PlatformCandidatesFor(sequence) # {}

AvailableInPlatform(member) ==
    member = AvailableMember

SelectedOverlayUnderSkew(sequence, selected, reference) ==
    /\ selected[reference] \in DesignatedCandidatesFor(sequence)
    /\ KnownSkewFor(sequence, reference)

Init ==
    /\ phase = "Registering"
    /\ registration = <<>>
    /\ roleEvidenceMode = "Pending"
    /\ roleGroup = NoGroup
    /\ roleGeneration = NoGeneration
    /\ roleDomain = {}
    /\ roleAssignments =
        [candidate \in Candidates |-> {}]
    /\ loadWarning = [reference \in References |-> FALSE]
    /\ selection = [reference \in References |-> NoCandidate]
    /\ shadowed = [reference \in References |-> {}]
    /\ resolutionFailure =
        [reference \in References |-> "Pending"]
    /\ traversal =
        [request \in TraversalRequests |-> "NotStarted"]

Register(candidate) ==
    /\ phase = "Registering"
    /\ candidate \in Candidates \ RegisteredSet(registration)
    /\ registration' = Append(registration, candidate)
    /\ UNCHANGED
        <<phase, roleEvidenceMode, roleGroup, roleGeneration, roleDomain,
          roleAssignments, loadWarning, selection, shadowed,
          resolutionFailure, traversal>>

FinishLoadWith(mode, group, generation, domain, assignments) ==
    /\ phase = "Registering"
    /\ phase' = "Loaded"
    /\ roleEvidenceMode' = mode
    /\ roleGroup' = group
    /\ roleGeneration' = generation
    /\ roleDomain' = domain
    /\ roleAssignments' = assignments
    /\ loadWarning' =
        [reference \in References |->
            IF RoleEvidenceValidAt(
                mode,
                group,
                generation,
                domain,
                assignments,
                registration)
            THEN KnownSkewAt(
                registration, reference, domain, assignments)
            ELSE FALSE]
    /\ UNCHANGED
        <<registration, selection, shadowed, resolutionFailure, traversal>>

FinishLoad ==
    \/ FinishLoadWith(
        "Valid",
        CurrentGroup,
        CurrentGeneration,
        RegisteredSet(registration),
        CanonicalRoleAssignments)
    \/ FinishLoadWith(
        "Missing",
        NoGroup,
        NoGeneration,
        {},
        CanonicalRoleAssignments)
    \/ FinishLoadWith(
        "Foreign",
        CurrentGroup,
        ForeignGeneration,
        RegisteredSet(registration),
        CanonicalRoleAssignments)
    \/ FinishLoadWith(
        "Stale",
        CurrentGroup,
        StaleGeneration,
        RegisteredSet(registration),
        CanonicalRoleAssignments)
    \/ FinishLoadWith(
        "Replayed",
        CurrentGroup,
        StaleGeneration,
        RegisteredSet(registration),
        CanonicalRoleAssignments)
    \/ FinishLoadWith(
        "WrongGroup",
        ForeignGroup,
        CurrentGeneration,
        RegisteredSet(registration),
        CanonicalRoleAssignments)
    \/ /\ RegisteredSet(registration) # {}
       /\ \E missing \in RegisteredSet(registration):
            FinishLoadWith(
                "Incomplete",
                CurrentGroup,
                CurrentGeneration,
                RegisteredSet(registration) \ {missing},
                CanonicalRoleAssignments)
    \/ /\ Candidates \ RegisteredSet(registration) # {}
       /\ \E extra \in Candidates \ RegisteredSet(registration):
            FinishLoadWith(
                "Extra",
                CurrentGroup,
                CurrentGeneration,
                RegisteredSet(registration) \union {extra},
                CanonicalRoleAssignments)
    \/ /\ RegisteredSet(registration) # {}
       /\ \E witness \in RegisteredSet(registration):
            FinishLoadWith(
                "Contradictory",
                CurrentGroup,
                CurrentGeneration,
                RegisteredSet(registration),
                ContradictoryRoleAssignments(witness))

Resolve ==
    /\ phase = "Loaded"
    /\ phase' = "Resolved"
    /\ selection' =
        [reference \in References |->
            CandidateFor(registration, reference)]
    /\ shadowed' =
        [reference \in References |->
            ShadowedFor(registration, reference)]
    /\ resolutionFailure' =
        [reference \in References |->
            FailureFor(registration, reference)]
    /\ UNCHANGED
        <<registration, roleEvidenceMode, roleGroup, roleGeneration, roleDomain,
          roleAssignments, loadWarning, traversal>>

TraversalFor(reference, member) ==
    IF resolutionFailure[reference] # "None"
    THEN "ResolutionFailed"
    ELSE
        IF AvailableInPlatform(member)
        THEN
            IF /\ TraversalMode = "RejectSkew"
               /\ SelectedOverlayUnderSkew(
                    registration, selection, reference)
            THEN "CompatibilityFailure"
            ELSE "Found"
        ELSE
            IF SelectedOverlayUnderSkew(
                registration, selection, reference)
            THEN
                IF TraversalMode = "SuppressFailure"
                THEN "Missing"
                ELSE "CompatibilityFailure"
            ELSE "Missing"

Traverse ==
    /\ phase = "Resolved"
    /\ phase' = "Traversed"
    /\ traversal' =
        [request \in TraversalRequests |->
            TraversalFor(request[1], request[2])]
    /\ UNCHANGED
        <<registration, roleEvidenceMode, roleGroup, roleGeneration, roleDomain,
          roleAssignments, loadWarning, selection, shadowed,
          resolutionFailure>>

Next ==
    (\/ \E candidate \in Candidates : Register(candidate))
    \/ FinishLoad
    \/ Resolve
    \/ Traverse

Spec ==
    /\ Init
    /\ [][Next]_vars
    /\ WF_vars(FinishLoad)
    /\ WF_vars(Resolve)
    /\ WF_vars(Traverse)

TypeOK ==
    /\ phase \in Phases
    /\ registration \in RegistrationSequences
    /\ roleEvidenceMode \in RoleEvidenceModes
    /\ roleGroup \in {CurrentGroup, ForeignGroup, NoGroup}
    /\ roleGeneration \in
        {CurrentGeneration, ForeignGeneration, StaleGeneration, NoGeneration}
    /\ roleDomain \subseteq Candidates
    /\ roleAssignments \in [Candidates -> SUBSET WorkspaceRoles]
    /\ loadWarning \in [References -> BOOLEAN]
    /\ selection \in [References -> Candidates \union {NoCandidate}]
    /\ shadowed \in [References -> SUBSET Candidates]
    /\ resolutionFailure \in [References -> ResolutionFailures]
    /\ traversal \in [TraversalRequests -> TraversalOutcomes]

SelectedCandidatesAreEntitled ==
    \A reference \in References:
        selection[reference] # NoCandidate =>
            /\ selection[reference] \in EntitledCandidatesFor(registration)
            /\ selection[reference] \in RegisteredSet(registration)

SelectedCandidatesUseSnapshotRoles ==
    phase \in {"Resolved", "Traversed"} =>
        \A reference \in References:
            selection[reference] # NoCandidate =>
                /\ RoleEvidenceStructurallyValid
                /\ selection[reference] \in roleDomain
                /\ \/ CallerDesignated
                        \in roleAssignments[selection[reference]]
                   \/ PlatformAuthorized
                        \in roleAssignments[selection[reference]]

InvalidRoleEvidenceIsRejected ==
    (/\ phase \in {"Loaded", "Resolved", "Traversed"}
     /\ ~RoleEvidenceStructurallyValid)
    =>
        /\ \A reference \in References:
            ~loadWarning[reference]
        /\ (phase \in {"Resolved", "Traversed"} =>
                \A reference \in References:
                    /\ selection[reference] = NoCandidate
                    /\ shadowed[reference] = {}
                    /\ resolutionFailure[reference] =
                        "InvalidRoleEvidence")

SelectionMatchesFailure ==
    phase \in {"Resolved", "Traversed"} =>
        \A reference \in References:
            (selection[reference] = NoCandidate)
                <=> (resolutionFailure[reference] # "None")

DesignatedPrecedence ==
    phase \in {"Resolved", "Traversed"} =>
        \A reference \in References:
            LET designated ==
                    EligibleFor(registration, reference)
                        \cap DesignatedCandidatesFor(registration)
            IN
                Cardinality(designated) = 1 =>
                    selection[reference] \in designated

UnruledTieIsVisible ==
    phase \in {"Resolved", "Traversed"} =>
        \A reference \in References:
            Cardinality(TopCandidates(registration, reference)) > 1 =>
                /\ selection[reference] = NoCandidate
                /\ resolutionFailure[reference] = "Ambiguous"

ShadowingIsRecorded ==
    phase \in {"Resolved", "Traversed"} =>
        \A reference \in References:
            selection[reference] # NoCandidate =>
                shadowed[reference] =
                    EligibleFor(registration, reference)
                        \ {selection[reference]}

SelectionIsOrderIndependent ==
    phase \in {"Resolved", "Traversed"} =>
        \A reference \in References:
            \A alternate \in RegistrationPermutations(registration):
                CandidateFor(registration, reference) =
                    CandidateFor(alternate, reference)

ReferenceVersionDoesNotChangeWinner ==
    phase \in {"Resolved", "Traversed"} =>
        LET exactDesignated ==
                EligibleFor(registration, ExactReference)
                    \cap DesignatedCandidatesFor(registration)
            skewedDesignated ==
                EligibleFor(registration, SkewedReference)
                    \cap DesignatedCandidatesFor(registration)
            exactPlatform ==
                EligibleFor(registration, ExactReference)
                    \cap PlatformCandidatesFor(registration)
            skewedPlatform ==
                EligibleFor(registration, SkewedReference)
                    \cap PlatformCandidatesFor(registration)
        IN
            /\ Cardinality(exactDesignated) = 1
            /\ exactDesignated = skewedDesignated
            /\ exactPlatform # {}
            /\ skewedPlatform # {}
            => selection[ExactReference] = selection[SkewedReference]

KnownSkewIsWarnedAtLoad ==
    phase \in {"Loaded", "Resolved", "Traversed"} =>
        \A reference \in References:
            KnownSkewFor(registration, reference) =>
                loadWarning[reference]

UnavailableSkewIsAttributed ==
    phase = "Traversed" =>
        \A reference \in References:
            SelectedOverlayUnderSkew(
                registration, selection, reference) =>
                traversal[<<reference, UnavailableMember>>] =
                    "CompatibilityFailure"

AvailableTraversalSucceeds ==
    phase = "Traversed" =>
        \A reference \in References:
            selection[reference] # NoCandidate =>
                traversal[<<reference, AvailableMember>>] = "Found"

UnavailableWithoutSkewIsMissing ==
    phase = "Traversed" =>
        \A reference \in References:
            /\ selection[reference] # NoCandidate
            /\ ~SelectedOverlayUnderSkew(
                registration, selection, reference)
            => traversal[<<reference, UnavailableMember>>] = "Missing"

ResolutionConverges == <> (phase = "Traversed")

=============================================================================
