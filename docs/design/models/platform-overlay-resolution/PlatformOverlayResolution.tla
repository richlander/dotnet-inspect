----------------------- MODULE PlatformOverlayResolution -----------------------
EXTENDS FiniteSets, Integers, Sequences, TLC

\* Owned by docs/design/platform-composition-and-overlays.md.
\* Candidate acquisition, identity decoding, and compatibility computation are
\* inputs. The model owns only arbitration among already-classified candidates
\* and attribution when a platform cannot satisfy an overlay traversal request.

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
    TraversalMode

Candidates == {DesignatedOne, DesignatedTwo, Platform, Unentitled}
DesignatedCandidates == {DesignatedOne, DesignatedTwo}
PlatformCandidates == {Platform}
EntitledCandidates == DesignatedCandidates \union PlatformCandidates
References == {ExactReference, SkewedReference}
Members == {AvailableMember, UnavailableMember}
TraversalRequests == References \X Members

ASSUME
    /\ Cardinality(Candidates) = 4
    /\ Cardinality(References) = 2
    /\ Cardinality(Members) = 2
    /\ NoCandidate \notin Candidates
    /\ SelectionMode \in
        {"Policy", "RegistrationOrder", "VersionSensitive"}
    /\ TraversalMode \in
        {"Policy", "RejectSkew", "SuppressFailure"}

Phases == {"Registering", "Loaded", "Resolved", "Traversed"}
ResolutionFailures == {"Pending", "None", "NoMatch", "Ambiguous"}
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

\* Every modeled candidate has the requested name and can bind under the
\* adjacent identity policy. Entitlement remains a separate acquisition fact.
EligibleFor(sequence, reference) ==
    {candidate \in RegisteredSet(sequence):
        /\ candidate \in EntitledCandidates
        /\ reference \in References}

TopCandidates(sequence, reference) ==
    LET eligible == EligibleFor(sequence, reference)
        designated == eligible \cap DesignatedCandidates
        platform == eligible \cap PlatformCandidates
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
    ELSE candidate \in DesignatedCandidates

VersionSensitiveCandidate(sequence, reference) ==
    LET policy == PolicyCandidate(sequence, reference)
        eligible == EligibleFor(sequence, reference)
        matchingPlatforms ==
            {candidate \in eligible \cap PlatformCandidates:
                VersionEqual(reference, candidate)}
    IN
        IF policy = NoCandidate
        THEN NoCandidate
        ELSE
            IF /\ eligible \cap DesignatedCandidates # {}
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

KnownSkewFor(sequence, reference) ==
    /\ reference = SkewedReference
    /\ EligibleFor(sequence, reference) \cap DesignatedCandidates # {}
    /\ EligibleFor(sequence, reference) \cap PlatformCandidates # {}

AvailableInPlatform(member) ==
    member = AvailableMember

SelectedOverlayUnderSkew(sequence, selected, reference) ==
    /\ selected[reference] \in DesignatedCandidates
    /\ KnownSkewFor(sequence, reference)

VARIABLES
    phase,
    registration,
    loadWarning,
    selection,
    shadowed,
    resolutionFailure,
    traversal

vars ==
    <<phase, registration, loadWarning, selection, shadowed,
      resolutionFailure, traversal>>

Init ==
    /\ phase = "Registering"
    /\ registration = <<>>
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
        <<phase, loadWarning, selection, shadowed, resolutionFailure,
          traversal>>

FinishLoad ==
    /\ phase = "Registering"
    /\ phase' = "Loaded"
    /\ loadWarning' =
        [reference \in References |->
            KnownSkewFor(registration, reference)]
    /\ UNCHANGED
        <<registration, selection, shadowed, resolutionFailure, traversal>>

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
    /\ UNCHANGED <<registration, loadWarning, traversal>>

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
        <<registration, loadWarning, selection, shadowed, resolutionFailure>>

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
    /\ loadWarning \in [References -> BOOLEAN]
    /\ selection \in [References -> Candidates \union {NoCandidate}]
    /\ shadowed \in [References -> SUBSET Candidates]
    /\ resolutionFailure \in [References -> ResolutionFailures]
    /\ traversal \in [TraversalRequests -> TraversalOutcomes]

SelectedCandidatesAreEntitled ==
    \A reference \in References:
        selection[reference] # NoCandidate =>
            /\ selection[reference] \in EntitledCandidates
            /\ selection[reference] \in RegisteredSet(registration)

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
                        \cap DesignatedCandidates
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
                    \cap DesignatedCandidates
            skewedDesignated ==
                EligibleFor(registration, SkewedReference)
                    \cap DesignatedCandidates
            exactPlatform ==
                EligibleFor(registration, ExactReference)
                    \cap PlatformCandidates
            skewedPlatform ==
                EligibleFor(registration, SkewedReference)
                    \cap PlatformCandidates
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
