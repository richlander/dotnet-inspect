---------------------- MODULE BindingCompositionCurrency ----------------------
EXTENDS FiniteSets, Integers, Sequences, TLC

\* Owned by docs/design/type-forwarding-resolution.md.
\* Identity matching, role precedence, and version construction are inputs.
\* The model owns complete handoff issuance and closed finalization.

CONSTANTS
    CandidateOne,
    CandidateTwo,
    CandidateThree,
    VersionOne,
    VersionTwo,
    DomainMode,
    DecisionMode,
    BoundaryMode,
    VersionMode

Candidates == {CandidateOne, CandidateTwo, CandidateThree}
CanonicalCandidates == <<CandidateOne, CandidateTwo, CandidateThree>>
Versions == {VersionOne, VersionTwo}
CompositionRequired == "CompositionRequired"
NonDomainKinds ==
    {"Selected", "Ambiguous", "Unavailable", "Rejected",
     "NoNameOwner", "NameOwnedNoMatch", "Undifferentiated"}
SourceKinds == {CompositionRequired} \union NonDomainKinds
ResultKinds ==
    NonDomainKinds \union {"None", "Superseded", CompositionRequired}
Phases == {"Ready", "Issued", "Completed"}

ASSUME
    /\ Cardinality(Candidates) = 3
    /\ Cardinality(Versions) = 2
    /\ DomainMode \in
        {"Policy", "OmitEligible", "AddIneligible",
         "EnumerationOrder", "DuplicateRegistration", "ReopenTerminal"}
    /\ DecisionMode \in
        {"Policy", "AcceptInjection", "DropInactive",
         "PromoteSelectedShadow", "PromoteAmbiguousShadow",
         "ReverseProjection", "SubstituteContender",
         "CanonicalizeTerminal"}
    /\ BoundaryMode \in {"Policy", "LeakUnfinalized"}
    /\ VersionMode \in {"Policy", "InterpretForeignSnapshot"}

NoDuplicates(sequence) ==
    \A left, right \in 1..Len(sequence):
        left # right => sequence[left] # sequence[right]

CandidateEnumerations ==
    {sequence \in [1..3 -> Candidates] : NoDuplicates(sequence)}

Position(sequence, candidate) ==
    CHOOSE index \in 1..Len(sequence): sequence[index] = candidate

OrderedBy(sequence, candidateSet) ==
    [rank \in 1..Cardinality(candidateSet) |->
        CHOOSE candidate \in candidateSet:
            Cardinality(
                {other \in candidateSet:
                    Position(sequence, other) <=
                        Position(sequence, candidate)}) = rank]

CanonicalFor(candidateSet) ==
    OrderedBy(CanonicalCandidates, candidateSet)

Reversed(sequence) ==
    [index \in 1..Len(sequence) |->
        sequence[Len(sequence) - index + 1]]

LastCanonical(candidateSet) ==
    CanonicalFor(candidateSet)[Cardinality(candidateSet)]

FirstCanonical(candidateSet) ==
    CanonicalFor(candidateSet)[1]

SecondCanonical(candidateSet) ==
    CanonicalFor(candidateSet)[2]

AddedCandidate(candidateSet) ==
    CHOOSE candidate \in Candidates \ candidateSet: TRUE

IssuedSet(candidateSet) ==
    CASE DomainMode = "OmitEligible" ->
            candidateSet \ {LastCanonical(candidateSet)}
      [] DomainMode = "AddIneligible" ->
            candidateSet \union {AddedCandidate(candidateSet)}
      [] OTHER -> candidateSet

IssuedOrder(enumeration, candidateSet) ==
    LET issued == IssuedSet(candidateSet)
    IN
        IF DomainMode = "EnumerationOrder"
        THEN OrderedBy(enumeration, issued)
        ELSE
            IF DomainMode = "DuplicateRegistration"
            THEN Append(CanonicalFor(issued), FirstCanonical(issued))
            ELSE CanonicalFor(issued)

CanonicalTerminalActive(sourceKind, candidateSet) ==
    CASE sourceKind = "Selected" ->
            {FirstCanonical(candidateSet)}
      [] sourceKind = "Ambiguous" ->
            {FirstCanonical(candidateSet), SecondCanonical(candidateSet)}
      [] OTHER -> {}

VARIABLES
    phase,
    sourceKind,
    enumeration,
    identityEligible,
    sourceActive,
    attemptedContenders,
    consumerPresent,
    capturedVersion,
    snapshotVersion,
    domain,
    domainOrder,
    resultKind,
    resultActive,
    resultInactive,
    resultActiveOrder,
    resultInactiveOrder

vars ==
    <<phase, sourceKind, enumeration, identityEligible, sourceActive,
      attemptedContenders, consumerPresent, capturedVersion, snapshotVersion,
      domain, domainOrder, resultKind, resultActive, resultInactive,
      resultActiveOrder, resultInactiveOrder>>

Init ==
    /\ phase = "Ready"
    /\ sourceKind \in SourceKinds
    /\ enumeration \in CandidateEnumerations
    /\ identityEligible \in SUBSET Candidates
    /\ sourceActive \in SUBSET Candidates
    /\ attemptedContenders \in SUBSET Candidates
    /\ consumerPresent \in BOOLEAN
    /\ (sourceKind = CompositionRequired => identityEligible # {})
    /\ (sourceKind = "Selected" =>
        /\ sourceActive \subseteq identityEligible
        /\ Cardinality(sourceActive) = 1)
    /\ (sourceKind = "Ambiguous" =>
        /\ sourceActive \subseteq identityEligible
        /\ Cardinality(sourceActive) >= 2)
    /\ (sourceKind \notin {"Selected", "Ambiguous"} =>
        sourceActive = {})
    /\ (sourceKind \in
            {"Unavailable", "Rejected", "NoNameOwner",
             "NameOwnedNoMatch", "Undifferentiated"} =>
        identityEligible = {})
    /\ (DomainMode = "OmitEligible" =>
        /\ sourceKind = CompositionRequired
        /\ Cardinality(identityEligible) >= 2)
    /\ (DomainMode = "AddIneligible" =>
        /\ sourceKind = CompositionRequired
        /\ identityEligible # Candidates)
    /\ (DomainMode = "EnumerationOrder" =>
        sourceKind = CompositionRequired)
    /\ (DomainMode = "DuplicateRegistration" =>
        sourceKind = CompositionRequired)
    /\ (DomainMode = "ReopenTerminal" =>
        /\ sourceKind \in {"Selected", "Ambiguous"}
        /\ identityEligible \ sourceActive # {})
    /\ (DecisionMode = "AcceptInjection" =>
        /\ sourceKind = CompositionRequired
        /\ attemptedContenders # {}
        /\ ~(attemptedContenders \subseteq identityEligible)
        /\ consumerPresent)
    /\ (DecisionMode = "DropInactive" =>
        /\ sourceKind = CompositionRequired
        /\ attemptedContenders # {}
        /\ attemptedContenders \subseteq identityEligible
        /\ attemptedContenders # identityEligible
        /\ consumerPresent)
    /\ (DecisionMode = "PromoteSelectedShadow" =>
        /\ DomainMode = "ReopenTerminal"
        /\ sourceKind = "Selected"
        /\ attemptedContenders \subseteq
            identityEligible \ sourceActive
        /\ Cardinality(attemptedContenders) = 1
        /\ consumerPresent)
    /\ (DecisionMode = "PromoteAmbiguousShadow" =>
        /\ DomainMode = "ReopenTerminal"
        /\ sourceKind = "Ambiguous"
        /\ attemptedContenders \subseteq
            identityEligible \ sourceActive
        /\ Cardinality(attemptedContenders) = 1
        /\ consumerPresent)
    /\ (DecisionMode = "ReverseProjection" =>
        /\ sourceKind = CompositionRequired
        /\ attemptedContenders \subseteq identityEligible
        /\ Cardinality(attemptedContenders) >= 2
        /\ consumerPresent)
    /\ (DecisionMode = "SubstituteContender" =>
        /\ sourceKind = CompositionRequired
        /\ Cardinality(attemptedContenders) = 1
        /\ attemptedContenders \subseteq identityEligible
        /\ identityEligible \ attemptedContenders # {}
        /\ consumerPresent)
    /\ (DecisionMode = "CanonicalizeTerminal" =>
        /\ DomainMode = "Policy"
        /\ sourceKind \in {"Selected", "Ambiguous"}
        /\ sourceActive # CanonicalTerminalActive(
            sourceKind,
            identityEligible))
    /\ (BoundaryMode = "LeakUnfinalized" =>
        /\ sourceKind = CompositionRequired
        /\ ~consumerPresent)
    /\ (VersionMode = "InterpretForeignSnapshot" =>
        /\ sourceKind = CompositionRequired
        /\ consumerPresent
        /\ snapshotVersion = VersionTwo)
    /\ capturedVersion = VersionOne
    /\ snapshotVersion \in Versions
    /\ domain = {}
    /\ domainOrder = <<>>
    /\ resultKind = "None"
    /\ resultActive = {}
    /\ resultInactive = {}
    /\ resultActiveOrder = <<>>
    /\ resultInactiveOrder = <<>>

IssueComposition ==
    /\ phase = "Ready"
    /\ sourceKind = CompositionRequired
    /\ IF snapshotVersion = capturedVersion
            \/ VersionMode = "InterpretForeignSnapshot"
       THEN
            /\ phase' = "Issued"
            /\ domain' = IssuedSet(identityEligible)
            /\ domainOrder' =
                IssuedOrder(enumeration, identityEligible)
            /\ UNCHANGED
                <<resultKind, resultActive, resultInactive,
                  resultActiveOrder, resultInactiveOrder>>
       ELSE
            /\ phase' = "Completed"
            /\ domain' = {}
            /\ domainOrder' = <<>>
            /\ resultKind' = "Superseded"
            /\ resultActive' = {}
            /\ resultInactive' = {}
            /\ resultActiveOrder' = <<>>
            /\ resultInactiveOrder' = <<>>
    /\ UNCHANGED
        <<sourceKind, enumeration, identityEligible, sourceActive,
          attemptedContenders, consumerPresent, capturedVersion,
          snapshotVersion>>

IssueNonDomain ==
    /\ phase = "Ready"
    /\ sourceKind \in NonDomainKinds
    /\ IF snapshotVersion # capturedVersion
       THEN
            /\ phase' = "Completed"
            /\ domain' = {}
            /\ domainOrder' = <<>>
            /\ resultKind' = "Superseded"
            /\ resultActive' = {}
            /\ resultInactive' = {}
            /\ resultActiveOrder' = <<>>
            /\ resultInactiveOrder' = <<>>
       ELSE
            IF DomainMode = "ReopenTerminal"
            THEN
                /\ phase' = "Issued"
                /\ domain' = identityEligible
                /\ domainOrder' = CanonicalFor(identityEligible)
                /\ UNCHANGED
                    <<resultKind, resultActive, resultInactive,
                      resultActiveOrder, resultInactiveOrder>>
            ELSE
                /\ phase' = "Completed"
                /\ domain' = {}
                /\ domainOrder' = <<>>
                /\ resultKind' = sourceKind
                /\ resultActive' =
                    IF DecisionMode = "CanonicalizeTerminal"
                    THEN
                        CanonicalTerminalActive(
                            sourceKind,
                            identityEligible)
                    ELSE sourceActive
                /\ resultInactive' =
                    identityEligible \ resultActive'
                /\ resultActiveOrder' =
                    CanonicalFor(resultActive')
                /\ resultInactiveOrder' =
                    CanonicalFor(resultInactive')
    /\ UNCHANGED
        <<sourceKind, enumeration, identityEligible, sourceActive,
          attemptedContenders, consumerPresent, capturedVersion,
          snapshotVersion>>

ValidDecision ==
    /\ attemptedContenders # {}
    /\ attemptedContenders \subseteq domain

Finalize ==
    /\ phase = "Issued"
    /\ consumerPresent
    /\ phase' = "Completed"
    /\ IF DecisionMode = "AcceptInjection"
       THEN
            /\ resultKind' =
                IF Cardinality(attemptedContenders) = 1
                THEN "Selected"
                ELSE "Ambiguous"
            /\ resultActive' = attemptedContenders
            /\ resultInactive' = domain \ attemptedContenders
            /\ resultActiveOrder' = CanonicalFor(attemptedContenders)
            /\ resultInactiveOrder' =
                OrderedBy(
                    domainOrder,
                    domain \ attemptedContenders)
       ELSE
            IF ValidDecision
            THEN
                /\ resultKind' =
                    IF Cardinality(attemptedContenders) = 1
                    THEN "Selected"
                    ELSE "Ambiguous"
                /\ resultActive' =
                    IF DecisionMode = "SubstituteContender"
                    THEN
                        {CHOOSE candidate
                            \in domain \ attemptedContenders: TRUE}
                    ELSE attemptedContenders
                /\ IF DecisionMode = "DropInactive"
                   THEN
                        resultInactive' =
                            (domain \ resultActive')
                                \ {FirstCanonical(
                                    domain \ resultActive')}
                   ELSE
                        resultInactive' =
                            domain \ resultActive'
                /\ IF DecisionMode = "ReverseProjection"
                   THEN
                        resultActiveOrder' =
                            Reversed(
                                OrderedBy(
                                    domainOrder,
                                    attemptedContenders))
                   ELSE
                        resultActiveOrder' =
                            OrderedBy(
                                domainOrder,
                                resultActive')
                /\ resultInactiveOrder' =
                    OrderedBy(domainOrder, resultInactive')
            ELSE
                /\ resultKind' = "Rejected"
                /\ resultActive' = {}
                /\ resultInactive' = {}
                /\ resultActiveOrder' = <<>>
                /\ resultInactiveOrder' = <<>>
    /\ UNCHANGED
        <<sourceKind, enumeration, identityEligible, sourceActive,
          attemptedContenders, consumerPresent, capturedVersion,
          snapshotVersion, domain, domainOrder>>

RejectUnfinalized ==
    /\ phase = "Issued"
    /\ ~consumerPresent
    /\ phase' = "Completed"
    /\ resultKind' =
        IF BoundaryMode = "LeakUnfinalized"
        THEN CompositionRequired
        ELSE "Rejected"
    /\ resultActive' = {}
    /\ resultInactive' = {}
    /\ resultActiveOrder' = <<>>
    /\ resultInactiveOrder' = <<>>
    /\ UNCHANGED
        <<sourceKind, enumeration, identityEligible, sourceActive,
          attemptedContenders, consumerPresent, capturedVersion,
          snapshotVersion, domain, domainOrder>>

Next ==
    IssueComposition
    \/ IssueNonDomain
    \/ Finalize
    \/ RejectUnfinalized

Spec ==
    /\ Init
    /\ [][Next]_vars
    /\ WF_vars(IssueComposition)
    /\ WF_vars(IssueNonDomain)
    /\ WF_vars(Finalize)
    /\ WF_vars(RejectUnfinalized)

TypeOK ==
    /\ phase \in Phases
    /\ sourceKind \in SourceKinds
    /\ enumeration \in CandidateEnumerations
    /\ identityEligible \in SUBSET Candidates
    /\ sourceActive \in SUBSET Candidates
    /\ attemptedContenders \in SUBSET Candidates
    /\ consumerPresent \in BOOLEAN
    /\ capturedVersion \in Versions
    /\ snapshotVersion \in Versions
    /\ domain \in SUBSET Candidates
    /\ domainOrder \in Seq(Candidates)
    /\ resultKind \in ResultKinds
    /\ resultActive \in SUBSET Candidates
    /\ resultInactive \in SUBSET Candidates
    /\ resultActiveOrder \in Seq(Candidates)
    /\ resultInactiveOrder \in Seq(Candidates)

DomainIsComplete ==
    /\ sourceKind = CompositionRequired
    /\ phase \in {"Issued", "Completed"}
    /\ snapshotVersion = capturedVersion
    =>
        domain = identityEligible

DomainContainsOnlyEligible ==
    /\ sourceKind = CompositionRequired
    /\ phase \in {"Issued", "Completed"}
    /\ snapshotVersion = capturedVersion
    =>
        domain \subseteq identityEligible

DomainOrderIsCanonical ==
    /\ sourceKind = CompositionRequired
    /\ phase \in {"Issued", "Completed"}
    /\ snapshotVersion = capturedVersion
    =>
        domainOrder = CanonicalFor(domain)

DomainOrderMatchesMembers ==
    phase \in {"Issued", "Completed"} =>
        /\ NoDuplicates(domainOrder)
        /\ {domainOrder[index] : index \in 1..Len(domainOrder)} = domain

NonDomainResultsNeverIssueDomain ==
    sourceKind \in NonDomainKinds =>
        /\ phase # "Issued"
        /\ domain = {}

NonDomainResultsArePreserved ==
    /\ sourceKind \in NonDomainKinds
    /\ phase = "Completed"
    /\ snapshotVersion = capturedVersion
    =>
        /\ resultKind = sourceKind
        /\ resultActive = sourceActive
        /\ resultInactive = identityEligible \ sourceActive
        /\ resultActiveOrder =
            CanonicalFor(sourceActive)
        /\ resultInactiveOrder =
            CanonicalFor(identityEligible \ sourceActive)

InactiveEvidenceNeverPromoted ==
    /\ sourceKind \in {"Selected", "Ambiguous"}
    /\ phase = "Completed"
    =>
        resultActive \cap (identityEligible \ sourceActive) = {}

FinalCandidatesComeFromDomain ==
    /\ sourceKind = CompositionRequired
    /\ resultKind \in {"Selected", "Ambiguous"}
    =>
        resultActive \union resultInactive \subseteq domain

FinalPartitionPreservesDomain ==
    /\ sourceKind = CompositionRequired
    /\ resultKind \in {"Selected", "Ambiguous"}
    =>
        /\ resultActive # {}
        /\ resultActive \cap resultInactive = {}
        /\ resultActive \union resultInactive = domain
        /\ (resultKind = "Selected" <=> Cardinality(resultActive) = 1)
        /\ (resultKind = "Ambiguous" <=> Cardinality(resultActive) > 1)

FinalProjectionOrderIsPreserved ==
    /\ sourceKind = CompositionRequired
    /\ resultKind \in {"Selected", "Ambiguous"}
    =>
        /\ resultActiveOrder = OrderedBy(domainOrder, resultActive)
        /\ resultInactiveOrder = OrderedBy(domainOrder, resultInactive)

ValidDecisionIsHonored ==
    /\ sourceKind = CompositionRequired
    /\ phase = "Completed"
    /\ consumerPresent
    /\ snapshotVersion = capturedVersion
    /\ ValidDecision
    =>
        /\ resultActive = attemptedContenders
        /\ resultInactive = domain \ attemptedContenders
        /\ resultKind =
            IF Cardinality(attemptedContenders) = 1
            THEN "Selected"
            ELSE "Ambiguous"

MalformedDecisionIsRejected ==
    /\ sourceKind = CompositionRequired
    /\ phase = "Completed"
    /\ snapshotVersion = capturedVersion
    /\ ~ValidDecision
    =>
        /\ resultKind = "Rejected"
        /\ resultActive = {}
        /\ resultInactive = {}

ForeignSnapshotIsNotInterpreted ==
    /\ phase = "Completed"
    /\ snapshotVersion # capturedVersion
    =>
        /\ resultKind = "Superseded"
        /\ domain = {}

UnfinalizedHandoffIsRejected ==
    /\ sourceKind = CompositionRequired
    /\ phase = "Completed"
    /\ ~consumerPresent
    /\ snapshotVersion = capturedVersion
    =>
        resultKind = "Rejected"

SupersededPublishesNoDecision ==
    resultKind = "Superseded" =>
        /\ resultActive = {}
        /\ resultInactive = {}
        /\ resultActiveOrder = <<>>
        /\ resultInactiveOrder = <<>>

CompositionConverges == <>(phase = "Completed")

=============================================================================
