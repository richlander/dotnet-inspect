---------------- MODULE ResearchWorkspaceTargetComposition ----------------
EXTENDS FiniteSets, Integers, Sequences, TLC

\* Owned by docs/design/research-workspace-target-composition.md.
\* This model instantiates Metadata forwarding and binding-version behavior,
\* then joins opaque Queries identities through pre-existing Research maps.

CONSTANTS
    Facade,
    Target,
    Other,
    Before,
    After,
    GroupBefore,
    GroupAfter,
    QueryOperation,
    QueryQuestion,
    ResearchOperation,
    ReceiptBefore,
    ReceiptAfter,
    ForeignReceipt,
    QueryRootId,
    QueryTerminalSelectedId,
    QueryTerminalOppositeId,
    ResearchRootId,
    ResearchTerminalSelectedId,
    ResearchTerminalOppositeId,
    FreshResearchId,
    AttemptRootId,
    AttemptTerminalSelectedId,
    AttemptTerminalOppositeId,
    FreshAttemptId,
    DomainRootId,
    DomainTerminalSelectedId,
    DomainTerminalOppositeId,
    DomainOtherSelectedId,
    CensusRootId,
    CensusTerminalSelectedId,
    CensusTerminalOppositeId,
    CensusOtherSelectedId,
    InitialBinding,
    ReplacementBinding,
    NoOutcome,
    HopBudget,
    ForwardingMutationMode,
    CompositionMutationMode

Assemblies == {Facade, Target, Other}
Sides == {Before, After}
Groups == {GroupBefore, GroupAfter}
Receipts == {ReceiptBefore, ReceiptAfter, ForeignReceipt}
QueryIds ==
    {QueryRootId, QueryTerminalSelectedId, QueryTerminalOppositeId}
ResearchIds ==
    {ResearchRootId, ResearchTerminalSelectedId,
     ResearchTerminalOppositeId, FreshResearchId}
AttemptIds ==
    {AttemptRootId, AttemptTerminalSelectedId,
     AttemptTerminalOppositeId, FreshAttemptId}
DomainIds ==
    {DomainRootId, DomainTerminalSelectedId,
     DomainTerminalOppositeId, DomainOtherSelectedId}
CensusIds ==
    {CensusRootId, CensusTerminalSelectedId,
     CensusTerminalOppositeId, CensusOtherSelectedId}
AttemptKinds ==
    {"Resolved", "DeclaringTypeForwarded", "NotFound", "ReferenceOnly",
     "NotRequested", "Failed"}
RootAttemptKinds ==
    {"Resolved", "DeclaringTypeForwarded", "NotFound"}
DomainHealthKinds == {"Healthy", "Blocked"}
RequestKinds == {"Carried", "ExactAddress"}
CompositionPhases ==
    {"Resolving", "Selected", "Unavailable", "Rejected",
     "ContractFault", "Complete"}

ASSUME
    /\ Cardinality(Assemblies) = 3
    /\ Cardinality(Sides) = 2
    /\ Cardinality(Groups) = 2
    /\ Cardinality(Receipts) = 3
    /\ Cardinality(QueryIds) = 3
    /\ Cardinality(ResearchIds) = 4
    /\ Cardinality(AttemptIds) = 4
    /\ Cardinality(DomainIds) = 4
    /\ Cardinality(CensusIds) = 4
    /\ QueryOperation # ResearchOperation
    /\ InitialBinding # ReplacementBinding
    /\ NoOutcome \notin Assemblies
    /\ NoOutcome \notin Sides
    /\ NoOutcome \notin Groups
    /\ NoOutcome \notin Receipts
    /\ NoOutcome \notin QueryIds
    /\ NoOutcome \notin ResearchIds
    /\ NoOutcome \notin AttemptIds
    /\ NoOutcome \notin DomainIds
    /\ NoOutcome \notin CensusIds
    /\ NoOutcome \notin AttemptKinds
    /\ NoOutcome \notin DomainHealthKinds
    /\ HopBudget \in 1..3
    /\ ForwardingMutationMode \in
        {"Policy", "LooseScope", "PermitCycle", "ResolveForwarder",
         "CollapseBindingMiss", "ResolveAtStart", "AcceptInvalidImage"}
    /\ CompositionMutationMode \in
        {"Policy", "UseFacade", "CrossSide", "ReconstructReceipt",
         "RelabelRoot", "SelectNonResolvedAttempt", "SubstituteCensus",
         "SubstituteParticipant", "DropPath", "IgnoreBindingDrift",
         "InvokeUnavailable"}

VARIABLES
    resolutionPhase,
    current,
    path,
    initialScope,
    scope,
    hops,
    lastDeclaration,
    validated,
    terminalKind,
    terminalCause,
    terminalAssembly,
    liveBinding,
    bindingAdvanced,
    rootSealed,
    terminalAdmittedSelected,
    terminalSealedSelected,
    terminalAdmittedOpposite,
    terminalSealedOpposite,
    terminalCandidate,
    rootAttemptKind,
    terminalAttemptKind,
    rootDomainHealth,
    terminalDomainHealth,
    requestKind,
    capturedBinding,
    receiptMap,
    attemptMap,
    censusMap,
    compositionPhase,
    effectiveQueryInput,
    effectiveResearchAttempt,
    effectiveCensus,
    retainedRootAttempt,
    retainedHops,
    retainedBinding

resolutionVars ==
    <<resolutionPhase, current, path, initialScope, scope, hops,
      lastDeclaration, validated, terminalKind, terminalCause,
      terminalAssembly>>

bindingVars == <<liveBinding, bindingAdvanced>>

inputVars ==
    <<rootSealed, terminalAdmittedSelected, terminalSealedSelected,
      terminalAdmittedOpposite, terminalSealedOpposite, terminalCandidate,
      rootAttemptKind, terminalAttemptKind, rootDomainHealth,
      terminalDomainHealth, requestKind, capturedBinding, receiptMap,
      attemptMap, censusMap>>

outputVars ==
    <<compositionPhase, effectiveQueryInput, effectiveResearchAttempt,
      effectiveCensus, retainedRootAttempt, retainedHops, retainedBinding>>

vars ==
    <<resolutionPhase, current, path, initialScope, scope, hops,
      lastDeclaration, validated, terminalKind, terminalCause,
      terminalAssembly, liveBinding, bindingAdvanced, rootSealed,
      terminalAdmittedSelected, terminalSealedSelected,
      terminalAdmittedOpposite, terminalSealedOpposite, terminalCandidate,
      rootAttemptKind, terminalAttemptKind, rootDomainHealth,
      terminalDomainHealth, requestKind, capturedBinding, receiptMap,
      attemptMap, censusMap, compositionPhase, effectiveQueryInput,
      effectiveResearchAttempt, effectiveCensus, retainedRootAttempt,
      retainedHops, retainedBinding>>

Forwarding ==
    INSTANCE TypeForwardingResolution WITH
        AssemblyA <- Facade,
        AssemblyB <- Target,
        AssemblyC <- Other,
        NoOutcome <- NoOutcome,
        HopBudget <- HopBudget,
        MutationMode <- ForwardingMutationMode,
        phase <- resolutionPhase,
        current <- current,
        path <- path,
        initialScope <- initialScope,
        scope <- scope,
        hops <- hops,
        lastDeclaration <- lastDeclaration,
        validated <- validated,
        terminalKind <- terminalKind,
        terminalCause <- terminalCause,
        terminalAssembly <- terminalAssembly

BindingLifecycle ==
    INSTANCE AssemblyBindingPolicyVersionLifecycle WITH
        InitialVersion <- InitialBinding,
        ReplacementVersion <- ReplacementBinding,
        version <- liveBinding,
        advanced <- bindingAdvanced

SelectedSide == Before
OppositeSide == After

GroupFor(side) ==
    IF side = Before THEN GroupBefore ELSE GroupAfter

ReceiptFor(side) ==
    IF side = Before THEN ReceiptBefore ELSE ReceiptAfter

QueryIdFor(side, assembly) ==
    IF assembly = Facade
       THEN QueryRootId
       ELSE IF side = SelectedSide
            THEN QueryTerminalSelectedId
            ELSE QueryTerminalOppositeId

ResearchIdForQuery(queryId) ==
    CASE queryId = QueryRootId -> ResearchRootId
      [] queryId = QueryTerminalSelectedId ->
            ResearchTerminalSelectedId
      [] queryId = QueryTerminalOppositeId ->
            ResearchTerminalOppositeId

AttemptIdForResearch(researchId) ==
    CASE researchId = ResearchRootId -> AttemptRootId
      [] researchId = ResearchTerminalSelectedId ->
            AttemptTerminalSelectedId
      [] researchId = ResearchTerminalOppositeId ->
            AttemptTerminalOppositeId

DomainIdForResearch(researchId) ==
    CASE researchId = ResearchRootId -> DomainRootId
      [] researchId = ResearchTerminalSelectedId ->
            DomainTerminalSelectedId
      [] researchId = ResearchTerminalOppositeId ->
            DomainTerminalOppositeId

CensusIdForDomain(domainId) ==
    CASE domainId = DomainRootId -> CensusRootId
      [] domainId = DomainTerminalSelectedId ->
            CensusTerminalSelectedId
      [] domainId = DomainTerminalOppositeId ->
            CensusTerminalOppositeId
      [] domainId = DomainOtherSelectedId ->
            CensusOtherSelectedId

QueryInput(side, assembly) ==
    [id |-> QueryIdFor(side, assembly),
     operation |-> QueryOperation,
     question |-> QueryQuestion,
     side |-> side,
     registration |->
        [group |-> GroupFor(side), assembly |-> assembly]]

ResearchInput(id, side, queryInput) ==
    [id |-> id,
     operation |-> ResearchOperation,
     question |-> QueryQuestion,
     side |-> side,
     receipt |-> ReceiptFor(side),
     queryInput |-> queryInput]

ReceiptMapValue ==
    [queryId \in QueryIds |->
        CASE queryId = QueryRootId ->
                ResearchInput(
                    ResearchRootId,
                    SelectedSide,
                    QueryInput(SelectedSide, Facade))
          [] queryId = QueryTerminalSelectedId ->
                ResearchInput(
                    ResearchTerminalSelectedId,
                    SelectedSide,
                    QueryInput(SelectedSide, terminalCandidate))
          [] queryId = QueryTerminalOppositeId ->
                ResearchInput(
                    ResearchTerminalOppositeId,
                    OppositeSide,
                    QueryInput(OppositeSide, terminalCandidate))]

AttemptMapValue ==
    [researchId \in
        {ResearchRootId, ResearchTerminalSelectedId,
         ResearchTerminalOppositeId} |->
        [id |-> AttemptIdForResearch(researchId),
         input |->
            CASE researchId = ResearchRootId ->
                    ReceiptMapValue[QueryRootId]
              [] researchId = ResearchTerminalSelectedId ->
                    ReceiptMapValue[QueryTerminalSelectedId]
              [] researchId = ResearchTerminalOppositeId ->
                    ReceiptMapValue[QueryTerminalOppositeId],
         domain |-> DomainIdForResearch(researchId),
         kind |->
            IF researchId = ResearchRootId
               THEN rootAttemptKind
               ELSE terminalAttemptKind]]

CensusMapValue ==
    [domainId \in DomainIds |->
        CASE domainId = DomainRootId ->
                [id |-> CensusRootId,
                 domain |-> DomainRootId,
                 side |-> SelectedSide,
                 attempts |-> {AttemptRootId},
                 health |-> rootDomainHealth]
          [] domainId = DomainTerminalSelectedId ->
                [id |-> CensusTerminalSelectedId,
                 domain |-> DomainTerminalSelectedId,
                 side |-> SelectedSide,
                 attempts |-> {AttemptTerminalSelectedId},
                 health |-> terminalDomainHealth]
          [] domainId = DomainTerminalOppositeId ->
                [id |-> CensusTerminalOppositeId,
                 domain |-> DomainTerminalOppositeId,
                 side |-> OppositeSide,
                 attempts |-> {AttemptTerminalOppositeId},
                 health |-> terminalDomainHealth]
          [] domainId = DomainOtherSelectedId ->
                [id |-> CensusOtherSelectedId,
                 domain |-> DomainOtherSelectedId,
                 side |-> SelectedSide,
                 attempts |-> {},
                 health |-> "Healthy"]]

ChosenSide ==
    IF /\ CompositionMutationMode = "CrossSide"
       /\ Len(hops) > 0
       THEN OppositeSide
       ELSE SelectedSide

ChosenAssembly ==
    IF /\ CompositionMutationMode = "UseFacade"
       /\ Len(hops) > 0
       THEN Facade
       ELSE terminalAssembly

ChosenAdmitted ==
    IF ChosenAssembly = Facade
       THEN TRUE
       ELSE IF ChosenSide = SelectedSide
            THEN terminalAdmittedSelected
            ELSE terminalAdmittedOpposite

ChosenSealed ==
    IF ChosenAssembly = Facade
       THEN rootSealed
       ELSE IF ChosenSide = SelectedSide
            THEN terminalSealedSelected
            ELSE terminalSealedOpposite

ChosenQueryInput == QueryInput(ChosenSide, ChosenAssembly)

LegitimateResearchInput == receiptMap[ChosenQueryInput.id]

ReconstructedResearchInput ==
    [id |-> FreshResearchId,
     operation |-> ResearchOperation,
     question |-> QueryQuestion,
     side |-> ChosenSide,
     receipt |-> ForeignReceipt,
     queryInput |-> ChosenQueryInput]

ChosenResearchInput ==
    IF CompositionMutationMode = "ReconstructReceipt"
       THEN ReconstructedResearchInput
       ELSE LegitimateResearchInput

ChosenAttemptKind ==
    IF ChosenAssembly = Facade
       THEN rootAttemptKind
       ELSE terminalAttemptKind

ReconstructedAttempt ==
    [id |-> FreshAttemptId,
     input |-> ReconstructedResearchInput,
     domain |->
        IF ChosenSide = SelectedSide
           THEN DomainTerminalSelectedId
           ELSE DomainTerminalOppositeId,
     kind |-> ChosenAttemptKind]

ChosenResearchAttempt ==
    IF CompositionMutationMode = "ReconstructReceipt"
       THEN ReconstructedAttempt
       ELSE attemptMap[ChosenResearchInput.id]

ChosenCensus ==
    IF CompositionMutationMode = "SubstituteCensus"
       THEN censusMap[DomainOtherSelectedId]
       ELSE censusMap[ChosenResearchAttempt.domain]

RelabeledRootAttempt ==
    [id |-> attemptMap[ResearchRootId].id,
     input |-> attemptMap[ResearchRootId].input,
     domain |-> attemptMap[ResearchRootId].domain,
     kind |-> "Resolved"]

ChosenRootAttempt ==
    IF /\ CompositionMutationMode = "RelabelRoot"
       /\ Len(hops) > 0
       THEN RelabeledRootAttempt
       ELSE attemptMap[ResearchRootId]

ChosenHops ==
    IF /\ CompositionMutationMode = "DropPath"
       /\ Len(hops) > 0
       THEN <<>>
       ELSE hops

BindingReady ==
    \/ liveBinding = capturedBinding
    \/ CompositionMutationMode = "IgnoreBindingDrift"

ResolutionReady == terminalKind = "Resolved"

AssociationReady ==
    /\ requestKind = "Carried"
    /\ rootSealed
    /\ ChosenAdmitted
    /\ ChosenSealed
    /\ \/ ChosenAssembly = Facade
       \/ ChosenAssembly = terminalCandidate
       \/ CompositionMutationMode = "SubstituteParticipant"

ExpectedRootAttempt ==
    IF terminalAssembly = Facade
       THEN rootAttemptKind = "Resolved"
       ELSE rootAttemptKind = "DeclaringTypeForwarded"

ExactCensusReady ==
    /\ ChosenCensus.domain = ChosenResearchAttempt.domain
    /\ ChosenCensus.side = ChosenResearchAttempt.input.side
    /\ ChosenResearchAttempt.id \in ChosenCensus.attempts

AttemptReady ==
    \/ /\ ExactCensusReady
       /\ ChosenCensus.health = "Healthy"
       /\ ChosenResearchAttempt.kind = "Resolved"
    \/ /\ CompositionMutationMode =
            "SelectNonResolvedAttempt"
       /\ ExactCensusReady
       /\ ChosenCensus.health = "Healthy"
    \/ /\ CompositionMutationMode \in
            {"ReconstructReceipt", "SubstituteCensus"}
       /\ ChosenCensus.health = "Healthy"
       /\ ChosenResearchAttempt.kind = "Resolved"
    \/ /\ CompositionMutationMode = "UseFacade"
       /\ Len(hops) > 0
       /\ ExactCensusReady
       /\ ChosenCensus.health = "Healthy"

CiInputConstraint ==
    /\ rootSealed
    /\ terminalAdmittedSelected
    /\ terminalSealedSelected
    /\ terminalAdmittedOpposite
    /\ terminalSealedOpposite
    /\ rootAttemptKind \in
        {"Resolved", "DeclaringTypeForwarded"}
    /\ terminalAttemptKind = "Resolved"
    /\ rootDomainHealth = "Healthy"
    /\ terminalDomainHealth = "Healthy"
    /\ requestKind = "Carried"

Init ==
    /\ Forwarding!Init
    /\ BindingLifecycle!Init
    /\ rootSealed \in BOOLEAN
    /\ terminalAdmittedSelected \in BOOLEAN
    /\ terminalSealedSelected \in BOOLEAN
    /\ terminalAdmittedOpposite \in BOOLEAN
    /\ terminalSealedOpposite \in BOOLEAN
    /\ terminalCandidate \in {Target, Other}
    /\ rootAttemptKind \in RootAttemptKinds
    /\ terminalAttemptKind \in AttemptKinds
    /\ rootDomainHealth \in DomainHealthKinds
    /\ terminalDomainHealth \in DomainHealthKinds
    /\ requestKind \in RequestKinds
    /\ capturedBinding = InitialBinding
    /\ receiptMap = ReceiptMapValue
    /\ attemptMap = AttemptMapValue
    /\ censusMap = CensusMapValue
    /\ compositionPhase = "Resolving"
    /\ effectiveQueryInput = NoOutcome
    /\ effectiveResearchAttempt = NoOutcome
    /\ effectiveCensus = NoOutcome
    /\ retainedRootAttempt = NoOutcome
    /\ retainedHops = <<>>
    /\ retainedBinding = NoOutcome

ResolveStep ==
    /\ compositionPhase = "Resolving"
    /\ Forwarding!Advance
    /\ UNCHANGED <<bindingVars, inputVars, outputVars>>

BindingAdvanceStep ==
    /\ compositionPhase = "Resolving"
    /\ BindingLifecycle!Advance
    /\ UNCHANGED <<resolutionVars, inputVars, outputVars>>

PublishNonSuccess(phase) ==
    /\ compositionPhase' = phase
    /\ effectiveQueryInput' = NoOutcome
    /\ effectiveResearchAttempt' = NoOutcome
    /\ effectiveCensus' = NoOutcome
    /\ retainedRootAttempt' = NoOutcome
    /\ retainedHops' = hops
    /\ retainedBinding' = NoOutcome
    /\ UNCHANGED <<resolutionVars, bindingVars, inputVars>>

DetectBindingFault ==
    /\ compositionPhase = "Resolving"
    /\ resolutionPhase = "Terminal"
    /\ ~BindingReady
    /\ PublishNonSuccess("ContractFault")

PublishResolutionUnavailable ==
    /\ compositionPhase = "Resolving"
    /\ resolutionPhase = "Terminal"
    /\ BindingReady
    /\ ~ResolutionReady
    /\ PublishNonSuccess("Unavailable")

RejectAssociation ==
    /\ compositionPhase = "Resolving"
    /\ resolutionPhase = "Terminal"
    /\ BindingReady
    /\ ResolutionReady
    /\ ~AssociationReady
    /\ PublishNonSuccess("Rejected")

RejectRootEvidence ==
    /\ compositionPhase = "Resolving"
    /\ resolutionPhase = "Terminal"
    /\ BindingReady
    /\ ResolutionReady
    /\ AssociationReady
    /\ AttemptReady
    /\ ~ExpectedRootAttempt
    /\ PublishNonSuccess("Rejected")

PublishAttemptUnavailable ==
    /\ compositionPhase = "Resolving"
    /\ resolutionPhase = "Terminal"
    /\ BindingReady
    /\ ResolutionReady
    /\ AssociationReady
    /\ ~AttemptReady
    /\ PublishNonSuccess("Unavailable")

SelectEndpoint ==
    /\ compositionPhase = "Resolving"
    /\ resolutionPhase = "Terminal"
    /\ BindingReady
    /\ ResolutionReady
    /\ AssociationReady
    /\ ExpectedRootAttempt
    /\ AttemptReady
    /\ compositionPhase' = "Selected"
    /\ effectiveQueryInput' = ChosenQueryInput
    /\ effectiveResearchAttempt' = ChosenResearchAttempt
    /\ effectiveCensus' = ChosenCensus
    /\ retainedRootAttempt' = ChosenRootAttempt
    /\ retainedHops' = ChosenHops
    /\ retainedBinding' = capturedBinding
    /\ UNCHANGED <<resolutionVars, bindingVars, inputVars>>

CompleteSelected ==
    /\ compositionPhase = "Selected"
    /\ compositionPhase' = "Complete"
    /\ UNCHANGED
        <<resolutionVars, bindingVars, inputVars, effectiveQueryInput,
          effectiveResearchAttempt, effectiveCensus, retainedRootAttempt,
          retainedHops, retainedBinding>>

CompleteUnavailableMutation ==
    /\ CompositionMutationMode = "InvokeUnavailable"
    /\ compositionPhase = "Unavailable"
    /\ compositionPhase' = "Complete"
    /\ UNCHANGED
        <<resolutionVars, bindingVars, inputVars, effectiveQueryInput,
          effectiveResearchAttempt, effectiveCensus, retainedRootAttempt,
          retainedHops, retainedBinding>>

Next ==
    \/ ResolveStep
    \/ BindingAdvanceStep
    \/ DetectBindingFault
    \/ PublishResolutionUnavailable
    \/ RejectAssociation
    \/ RejectRootEvidence
    \/ PublishAttemptUnavailable
    \/ SelectEndpoint
    \/ CompleteSelected
    \/ CompleteUnavailableMutation

Spec ==
    /\ Init
    /\ [][Next]_vars
    /\ WF_vars(Next)

TypeOK ==
    /\ Forwarding!TypeOK
    /\ BindingLifecycle!TypeOK
    /\ rootSealed \in BOOLEAN
    /\ terminalAdmittedSelected \in BOOLEAN
    /\ terminalSealedSelected \in BOOLEAN
    /\ terminalAdmittedOpposite \in BOOLEAN
    /\ terminalSealedOpposite \in BOOLEAN
    /\ terminalCandidate \in {Target, Other}
    /\ rootAttemptKind \in RootAttemptKinds
    /\ terminalAttemptKind \in AttemptKinds
    /\ rootDomainHealth \in DomainHealthKinds
    /\ terminalDomainHealth \in DomainHealthKinds
    /\ requestKind \in RequestKinds
    /\ capturedBinding = InitialBinding
    /\ receiptMap = ReceiptMapValue
    /\ attemptMap = AttemptMapValue
    /\ censusMap = CensusMapValue
    /\ compositionPhase \in CompositionPhases
    /\ effectiveQueryInput = NoOutcome
       \/ /\ effectiveQueryInput.id \in QueryIds
          /\ effectiveQueryInput.side \in Sides
          /\ effectiveQueryInput.registration.group \in Groups
          /\ effectiveQueryInput.registration.assembly \in Assemblies
    /\ effectiveResearchAttempt = NoOutcome
       \/ /\ effectiveResearchAttempt.id \in AttemptIds
          /\ effectiveResearchAttempt.input.id \in ResearchIds
          /\ effectiveResearchAttempt.domain \in DomainIds
          /\ effectiveResearchAttempt.kind \in AttemptKinds
    /\ effectiveCensus = NoOutcome
       \/ /\ effectiveCensus.id \in CensusIds
          /\ effectiveCensus.domain \in DomainIds
          /\ effectiveCensus.side \in Sides
          /\ effectiveCensus.attempts \subseteq AttemptIds
          /\ effectiveCensus.health \in DomainHealthKinds
    /\ retainedRootAttempt = NoOutcome
       \/ retainedRootAttempt.kind \in AttemptKinds
    /\ retainedHops \in Seq(
        [source : Assemblies, scope : {"Any", "Platform"}])
    /\ retainedBinding \in
        {InitialBinding, ReplacementBinding, NoOutcome}

ForwardingBehaviorRefinesOwner == Forwarding!Spec
BindingBehaviorRefinesOwner == BindingLifecycle!SafetySpec
ForwardingCurrentMatchesPath == Forwarding!CurrentMatchesPath
ForwardingPhaseShapeIsCoherent == Forwarding!PhaseShapeIsCoherent
ForwardingHopSourcesFollowSelectedPath ==
    Forwarding!HopSourcesFollowSelectedPath
ForwardingScopeNeverLoosens == Forwarding!ScopeNeverLoosens
ForwardingSelectedPathHasNoCycle == Forwarding!SelectedPathHasNoCycle
ForwardingHopBudgetIsObserved == Forwarding!HopBudgetIsObserved
ForwardingTerminalOutcomeMatchesCause ==
    Forwarding!TerminalOutcomeMatchesCause
ForwardingResolvedTerminalIsCurrent ==
    Forwarding!ResolvedTerminalIsCurrent
ForwardingResolvedRequiresDefinition ==
    Forwarding!ResolvedRequiresDefinedDeclaration
ForwardingResolvedRequiresValidatedCandidate ==
    Forwarding!ResolvedRequiresValidatedCandidate
BindingAdvancedVersionIsFresh ==
    BindingLifecycle!AdvancedVersionIsFresh

SelectedEndpointBelongsToRequestedSide ==
    effectiveQueryInput # NoOutcome =>
        /\ effectiveQueryInput.side = SelectedSide
        /\ effectiveQueryInput.registration.group =
            GroupFor(SelectedSide)
        /\ ChosenAdmitted
        /\ ChosenSealed

SelectedEndpointMatchesResolvedTerminal ==
    effectiveQueryInput # NoOutcome =>
        /\ terminalKind = "Resolved"
        /\ effectiveQueryInput.registration.assembly =
            terminalAssembly

SelectedResearchAttemptUsesPopulationReceipt ==
    effectiveResearchAttempt # NoOutcome =>
        /\ effectiveResearchAttempt.input =
            receiptMap[effectiveQueryInput.id]
        /\ effectiveResearchAttempt =
            attemptMap[effectiveResearchAttempt.input.id]

SelectedResearchInputMatchesSelectedQueryInput ==
    effectiveResearchAttempt # NoOutcome =>
        effectiveResearchAttempt.input.queryInput =
            effectiveQueryInput

SelectedCensusMatchesAttempt ==
    effectiveCensus # NoOutcome =>
        /\ effectiveCensus =
            censusMap[effectiveResearchAttempt.domain]
        /\ effectiveCensus.side =
            effectiveResearchAttempt.input.side
        /\ effectiveResearchAttempt.id
            \in effectiveCensus.attempts

SelectedAttemptIsResolved ==
    effectiveResearchAttempt # NoOutcome =>
        effectiveResearchAttempt.kind = "Resolved"

SelectedDomainIsHealthy ==
    effectiveCensus # NoOutcome =>
        effectiveCensus.health = "Healthy"

RootAttemptIsPreserved ==
    retainedRootAttempt # NoOutcome =>
        retainedRootAttempt = attemptMap[ResearchRootId]

ForwardingEvidenceIsPreserved ==
    effectiveResearchAttempt # NoOutcome =>
        retainedHops = hops

BindingVersionIsPreserved ==
    effectiveResearchAttempt # NoOutcome =>
        /\ retainedBinding = capturedBinding
        /\ retainedBinding = liveBinding

DirectResolutionUsesRoot ==
    /\ effectiveResearchAttempt # NoOutcome
    /\ Len(hops) = 0
    => effectiveQueryInput.registration.assembly = Facade

ForwardedRootAttemptRemainsUnavailable ==
    /\ effectiveResearchAttempt # NoOutcome
    /\ Len(hops) > 0
    => retainedRootAttempt.kind = "DeclaringTypeForwarded"

SelectedRequestIsCarried ==
    effectiveResearchAttempt # NoOutcome =>
        requestKind = "Carried"

UnavailableAttemptPrecedesRootRejection ==
    /\ resolutionPhase = "Terminal"
    /\ BindingReady
    /\ ResolutionReady
    /\ AssociationReady
    /\ ~AttemptReady
    /\ compositionPhase # "Resolving"
    => compositionPhase = "Unavailable"

RootEvidenceMismatchIsRejectedAfterUsableAttempt ==
    /\ resolutionPhase = "Terminal"
    /\ BindingReady
    /\ ResolutionReady
    /\ AssociationReady
    /\ AttemptReady
    /\ ~ExpectedRootAttempt
    /\ compositionPhase # "Resolving"
    => compositionPhase = "Rejected"

NonSuccessPublishesNoEndpoint ==
    compositionPhase \in
        {"Unavailable", "Rejected", "ContractFault"} =>
        /\ effectiveQueryInput = NoOutcome
        /\ effectiveResearchAttempt = NoOutcome
        /\ effectiveCensus = NoOutcome
        /\ retainedRootAttempt = NoOutcome

ResearchCompletionHasSelectedEndpoint ==
    compositionPhase = "Complete" =>
        /\ effectiveQueryInput # NoOutcome
        /\ effectiveResearchAttempt # NoOutcome
        /\ effectiveCensus # NoOutcome
        /\ effectiveResearchAttempt.kind = "Resolved"

CompositionConverges ==
    <>(compositionPhase \in
        {"Unavailable", "Rejected", "ContractFault", "Complete"})

=============================================================================
