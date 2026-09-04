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
         "InvokeUnavailable", "CrossScope", "OmitNonTerminalParticipant"}

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
    terminalReceiptMappedSelected,
    terminalResearchActiveSelected,
    duplicateSealedSelected,
    foreignSealedSelected,
    terminalCandidate,
    rootAttemptKind,
    terminalAttemptKind,
    requestKind,
    queryImageReady,
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
      terminalAdmittedOpposite, terminalSealedOpposite,
      terminalReceiptMappedSelected, terminalResearchActiveSelected,
      duplicateSealedSelected, foreignSealedSelected, terminalCandidate,
      rootAttemptKind, terminalAttemptKind, requestKind, queryImageReady,
      capturedBinding, receiptMap, attemptMap, censusMap>>

outputVars ==
    <<compositionPhase, effectiveQueryInput, effectiveResearchAttempt,
      effectiveCensus, retainedRootAttempt, retainedHops, retainedBinding>>

vars ==
    <<resolutionPhase, current, path, initialScope, scope, hops,
      lastDeclaration, validated, terminalKind, terminalCause,
      terminalAssembly, liveBinding, bindingAdvanced, rootSealed,
      terminalAdmittedSelected, terminalSealedSelected,
      terminalAdmittedOpposite, terminalSealedOpposite,
      terminalReceiptMappedSelected, terminalResearchActiveSelected,
      duplicateSealedSelected, foreignSealedSelected, terminalCandidate,
      rootAttemptKind, terminalAttemptKind, requestKind, queryImageReady,
      capturedBinding, receiptMap, attemptMap, censusMap, compositionPhase,
      effectiveQueryInput, effectiveResearchAttempt, effectiveCensus,
      retainedRootAttempt, retainedHops, retainedBinding>>

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
SelectedScopeId == <<"ResearchScope", QueryQuestion, SelectedSide>>
OtherScopeId == <<"ResearchScope", QueryQuestion, OppositeSide>>
ResearchScopeIds == {SelectedScopeId, OtherScopeId}

DomainScope(domainId) ==
    IF domainId = DomainOtherSelectedId
       THEN OtherScopeId
       ELSE SelectedScopeId

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

SealedQueryIds ==
    (IF rootSealed THEN {QueryRootId} ELSE {})
    \cup
    (IF terminalSealedSelected
        THEN {QueryTerminalSelectedId}
        ELSE {})
    \cup
    (IF terminalSealedOpposite
        THEN {QueryTerminalOppositeId}
        ELSE {})

ActiveReceiptQueryIds ==
    (IF rootSealed THEN {QueryRootId} ELSE {})
    \cup
    (IF terminalReceiptMappedSelected
        THEN {QueryTerminalSelectedId}
        ELSE {})
    \cup
    (IF terminalSealedOpposite
        THEN {QueryTerminalOppositeId}
        ELSE {})

ActiveResearchQueryIds ==
    (IF rootSealed THEN {QueryRootId} ELSE {})
    \cup
    (IF terminalResearchActiveSelected
        THEN {QueryTerminalSelectedId}
        ELSE {})
    \cup
    (IF terminalSealedOpposite
        THEN {QueryTerminalOppositeId}
        ELSE {})

ReceiptMapValue ==
    [active |-> ActiveReceiptQueryIds,
     values |->
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
                        QueryInput(OppositeSide, terminalCandidate))]]

OwnerResearchIds ==
    {ResearchRootId, ResearchTerminalSelectedId,
     ResearchTerminalOppositeId}

ActiveAttemptResearchIds ==
    {ResearchIdForQuery(queryId) :
        queryId \in ActiveResearchQueryIds}

AttemptMapValue ==
    [active |-> ActiveAttemptResearchIds,
     values |->
        [researchId \in OwnerResearchIds |->
            [id |-> AttemptIdForResearch(researchId),
             scope |-> SelectedScopeId,
             input |->
                CASE researchId = ResearchRootId ->
                        ReceiptMapValue.values[QueryRootId]
                  [] researchId = ResearchTerminalSelectedId ->
                        ReceiptMapValue.values[QueryTerminalSelectedId]
                  [] researchId = ResearchTerminalOppositeId ->
                        ReceiptMapValue.values[QueryTerminalOppositeId],
             domain |-> DomainIdForResearch(researchId),
             kind |->
                CASE researchId = ResearchRootId ->
                        rootAttemptKind
                  [] OTHER -> terminalAttemptKind]]]

ActiveCensusDomainIds ==
    {DomainIdForResearch(researchId) :
        researchId \in AttemptMapValue.active}

ResearchIdsForDomainSide(domainId, side) ==
    {researchId \in AttemptMapValue.active :
        /\ AttemptMapValue.values[researchId].domain = domainId
        /\ AttemptMapValue.values[researchId].input.side = side}

OwnerAttemptIds(domainId, side) ==
    {AttemptMapValue.values[researchId].id :
        researchId \in ResearchIdsForDomainSide(domainId, side)}

RootCensusHealth ==
    IF rootAttemptKind \in {"Resolved", "NotFound"}
       THEN "Healthy"
       ELSE "Blocked"

SelectedTerminalCensusHealth ==
    IF terminalAttemptKind \in {"Resolved", "NotFound"}
       THEN "Healthy"
       ELSE "Blocked"

OppositeTerminalCensusHealth ==
    IF terminalAttemptKind \in {"Resolved", "NotFound"}
       THEN "Healthy"
       ELSE "Blocked"

CensusMapValue ==
    [active |-> ActiveCensusDomainIds,
     values |->
        [domainId \in DomainIds |->
            CASE domainId = DomainRootId ->
                    [id |-> CensusRootId,
                     scope |-> SelectedScopeId,
                     domain |-> DomainRootId,
                     side |-> SelectedSide,
                     attempts |->
                        OwnerAttemptIds(
                            DomainRootId,
                            SelectedSide),
                     health |-> RootCensusHealth]
              [] domainId = DomainTerminalSelectedId ->
                    [id |-> CensusTerminalSelectedId,
                     scope |-> SelectedScopeId,
                     domain |-> DomainTerminalSelectedId,
                     side |-> SelectedSide,
                     attempts |->
                        OwnerAttemptIds(
                            DomainTerminalSelectedId,
                            SelectedSide),
                     health |-> SelectedTerminalCensusHealth]
              [] domainId = DomainTerminalOppositeId ->
                    [id |-> CensusTerminalOppositeId,
                     scope |-> SelectedScopeId,
                     domain |-> DomainTerminalOppositeId,
                     side |-> OppositeSide,
                     attempts |->
                        OwnerAttemptIds(
                            DomainTerminalOppositeId,
                            OppositeSide),
                     health |-> OppositeTerminalCensusHealth]
              [] domainId = DomainOtherSelectedId ->
                    [id |-> CensusOtherSelectedId,
                     scope |-> OtherScopeId,
                     domain |-> DomainOtherSelectedId,
                     side |-> SelectedSide,
                     attempts |-> {},
                     health |-> "Healthy"]]]

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

LegitimateResearchInput ==
    receiptMap.values[ChosenQueryInput.id]

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
       ELSE IF /\ CompositionMutationMode = "UseFacade"
               /\ Len(hops) > 0
            THEN receiptMap.values[QueryTerminalSelectedId]
       ELSE LegitimateResearchInput

ChosenAttemptKind ==
    IF ChosenAssembly = Facade
       THEN rootAttemptKind
       ELSE terminalAttemptKind

ReconstructedAttempt ==
    [id |-> FreshAttemptId,
     scope |-> SelectedScopeId,
     input |-> ReconstructedResearchInput,
     domain |->
        IF ChosenSide = SelectedSide
           THEN DomainTerminalSelectedId
           ELSE DomainTerminalOppositeId,
     kind |-> ChosenAttemptKind]

OtherScopeAttempt ==
    [id |-> FreshAttemptId,
     scope |-> OtherScopeId,
     input |-> LegitimateResearchInput,
     domain |-> DomainOtherSelectedId,
     kind |-> ChosenAttemptKind]

ChosenResearchAttempt ==
    IF CompositionMutationMode = "CrossScope"
       THEN OtherScopeAttempt
       ELSE IF CompositionMutationMode = "ReconstructReceipt"
       THEN ReconstructedAttempt
       ELSE attemptMap.values[ChosenResearchInput.id]

OtherScopeCensus ==
    [id |-> CensusOtherSelectedId,
     scope |-> OtherScopeId,
     domain |-> DomainOtherSelectedId,
     side |-> ChosenSide,
     attempts |-> {FreshAttemptId},
     health |-> "Healthy"]

ChosenCensus ==
    IF CompositionMutationMode = "CrossScope"
       THEN OtherScopeCensus
       ELSE IF CompositionMutationMode = "SubstituteCensus"
       THEN censusMap.values[DomainOtherSelectedId]
       ELSE censusMap.values[ChosenResearchAttempt.domain]

RelabeledRootAttempt ==
    [id |-> attemptMap.values[ResearchRootId].id,
     scope |-> attemptMap.values[ResearchRootId].scope,
     input |-> attemptMap.values[ResearchRootId].input,
     domain |-> attemptMap.values[ResearchRootId].domain,
     kind |-> "Resolved"]

ChosenRootAttempt ==
    IF /\ CompositionMutationMode = "RelabelRoot"
       /\ Len(hops) > 0
       THEN RelabeledRootAttempt
    ELSE attemptMap.values[ResearchRootId]

ChosenHops ==
    IF /\ CompositionMutationMode = "DropPath"
       /\ Len(hops) > 0
       THEN <<>>
       ELSE hops

BindingReady ==
    \/ liveBinding = capturedBinding
    \/ CompositionMutationMode = "IgnoreBindingDrift"

RequestReady == requestKind = "Carried"

ResolutionReady == terminalKind = "Resolved"

AssociationReady ==
    /\ RequestReady
    /\ rootSealed
    /\ ChosenAdmitted
    /\ ChosenSealed
    /\ ChosenQueryInput.id \in receiptMap.active
    /\ \/ ChosenResearchInput.id \in attemptMap.active
       \/ CompositionMutationMode = "ReconstructReceipt"
    /\ \/ ChosenAssembly = Facade
       \/ ChosenAssembly = terminalCandidate
       \/ CompositionMutationMode = "SubstituteParticipant"

ExpectedRootAttempt ==
    IF terminalAssembly = Facade
       THEN rootAttemptKind = "Resolved"
       ELSE rootAttemptKind = "DeclaringTypeForwarded"

ExactCensusReady ==
    /\ \/ ChosenCensus.domain \in censusMap.active
       \/ CompositionMutationMode = "CrossScope"
    /\ ChosenCensus.scope = ChosenResearchAttempt.scope
    /\ ChosenResearchAttempt.scope =
        DomainScope(ChosenResearchAttempt.domain)
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

OmittedNonTerminalParticipantSelected ==
    CompositionMutationMode = "OmitNonTerminalParticipant"

PopulationReady ==
    /\ rootSealed
    /\ terminalAdmittedSelected = terminalSealedSelected
    /\ receiptMap.active = SealedQueryIds
    /\ ActiveResearchQueryIds = receiptMap.active
    /\ ~OmittedNonTerminalParticipantSelected
    /\ ~duplicateSealedSelected
    /\ ~foreignSealedSelected

OwnerInvocationReady ==
    /\ PopulationReady
    /\ RequestReady
    /\ queryImageReady

CiInputConstraint ==
    /\ rootSealed
    /\ terminalAdmittedSelected
    /\ terminalSealedSelected
    /\ terminalAdmittedOpposite
    /\ terminalSealedOpposite
    /\ terminalReceiptMappedSelected
    /\ terminalResearchActiveSelected
    /\ ~duplicateSealedSelected
    /\ ~foreignSealedSelected
    /\ rootAttemptKind \in
        {"Resolved", "DeclaringTypeForwarded"}
    /\ terminalAttemptKind = "Resolved"
    /\ requestKind = "Carried"
    /\ queryImageReady

Init ==
    /\ Forwarding!Init
    /\ BindingLifecycle!Init
    /\ rootSealed \in BOOLEAN
    /\ terminalAdmittedSelected \in BOOLEAN
    /\ terminalSealedSelected \in BOOLEAN
    /\ terminalAdmittedOpposite \in BOOLEAN
    /\ terminalSealedOpposite \in BOOLEAN
    /\ terminalReceiptMappedSelected \in BOOLEAN
    /\ terminalResearchActiveSelected \in BOOLEAN
    /\ duplicateSealedSelected \in BOOLEAN
    /\ foreignSealedSelected \in BOOLEAN
    /\ terminalCandidate \in {Target, Other}
    /\ rootAttemptKind \in RootAttemptKinds
    /\ terminalAttemptKind \in AttemptKinds
    /\ requestKind \in RequestKinds
    /\ queryImageReady \in BOOLEAN
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
    /\ OwnerInvocationReady
    /\ Forwarding!Advance
    /\ UNCHANGED <<bindingVars, inputVars, outputVars>>

BindingAdvanceStep ==
    /\ compositionPhase = "Resolving"
    /\ OwnerInvocationReady
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

RejectPopulation ==
    /\ compositionPhase = "Resolving"
    /\ ~PopulationReady
    /\ PublishNonSuccess("Rejected")

RejectUnsupportedRequest ==
    /\ compositionPhase = "Resolving"
    /\ PopulationReady
    /\ ~RequestReady
    /\ PublishNonSuccess("Rejected")

PublishQueryUnavailable ==
    /\ compositionPhase = "Resolving"
    /\ PopulationReady
    /\ RequestReady
    /\ ~queryImageReady
    /\ PublishNonSuccess("Unavailable")

DetectBindingFault ==
    /\ compositionPhase = "Resolving"
    /\ OwnerInvocationReady
    /\ resolutionPhase = "Terminal"
    /\ ~BindingReady
    /\ PublishNonSuccess("ContractFault")

PublishResolutionUnavailable ==
    /\ compositionPhase = "Resolving"
    /\ OwnerInvocationReady
    /\ resolutionPhase = "Terminal"
    /\ BindingReady
    /\ ~ResolutionReady
    /\ PublishNonSuccess("Unavailable")

RejectAssociation ==
    /\ compositionPhase = "Resolving"
    /\ OwnerInvocationReady
    /\ resolutionPhase = "Terminal"
    /\ BindingReady
    /\ ResolutionReady
    /\ ~AssociationReady
    /\ PublishNonSuccess("Rejected")

RejectRootEvidence ==
    /\ compositionPhase = "Resolving"
    /\ OwnerInvocationReady
    /\ resolutionPhase = "Terminal"
    /\ BindingReady
    /\ ResolutionReady
    /\ AssociationReady
    /\ AttemptReady
    /\ ~ExpectedRootAttempt
    /\ PublishNonSuccess("Rejected")

PublishAttemptUnavailable ==
    /\ compositionPhase = "Resolving"
    /\ OwnerInvocationReady
    /\ resolutionPhase = "Terminal"
    /\ BindingReady
    /\ ResolutionReady
    /\ AssociationReady
    /\ ~AttemptReady
    /\ PublishNonSuccess("Unavailable")

SelectEndpoint ==
    /\ compositionPhase = "Resolving"
    /\ OwnerInvocationReady
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
    \/ RejectPopulation
    \/ RejectUnsupportedRequest
    \/ PublishQueryUnavailable
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
    /\ terminalReceiptMappedSelected \in BOOLEAN
    /\ terminalResearchActiveSelected \in BOOLEAN
    /\ duplicateSealedSelected \in BOOLEAN
    /\ foreignSealedSelected \in BOOLEAN
    /\ terminalCandidate \in {Target, Other}
    /\ rootAttemptKind \in RootAttemptKinds
    /\ terminalAttemptKind \in AttemptKinds
    /\ requestKind \in RequestKinds
    /\ queryImageReady \in BOOLEAN
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
          /\ effectiveResearchAttempt.scope \in ResearchScopeIds
          /\ effectiveResearchAttempt.input.id \in ResearchIds
          /\ effectiveResearchAttempt.domain \in DomainIds
          /\ effectiveResearchAttempt.scope =
              DomainScope(effectiveResearchAttempt.domain)
          /\ effectiveResearchAttempt.kind \in AttemptKinds
    /\ effectiveCensus = NoOutcome
       \/ /\ effectiveCensus.id \in CensusIds
          /\ effectiveCensus.scope \in ResearchScopeIds
          /\ effectiveCensus.domain \in DomainIds
          /\ effectiveCensus.scope =
              DomainScope(effectiveCensus.domain)
          /\ effectiveCensus.side \in Sides
          /\ effectiveCensus.attempts \subseteq AttemptIds
          /\ effectiveCensus.health \in DomainHealthKinds
    /\ retainedRootAttempt = NoOutcome
       \/ /\ retainedRootAttempt.scope \in ResearchScopeIds
          /\ retainedRootAttempt.kind \in AttemptKinds
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

InvalidPopulationIsRejectedBeforeResolution ==
    ~PopulationReady =>
        /\ resolutionPhase = "Probing"
        /\ Len(hops) = 0
        /\ liveBinding = InitialBinding
        /\ ~bindingAdvanced
        /\ compositionPhase \in {"Resolving", "Rejected"}

UnsupportedRequestIsRejectedBeforeResolution ==
    /\ PopulationReady
    /\ ~RequestReady
    => /\ resolutionPhase = "Probing"
       /\ Len(hops) = 0
       /\ liveBinding = InitialBinding
       /\ ~bindingAdvanced
       /\ compositionPhase \in {"Resolving", "Rejected"}

ImageOpenFailureIsUnavailableBeforeOwners ==
    /\ PopulationReady
    /\ RequestReady
    /\ ~queryImageReady
    => /\ resolutionPhase = "Probing"
       /\ Len(hops) = 0
       /\ liveBinding = InitialBinding
       /\ ~bindingAdvanced
       /\ compositionPhase \in {"Resolving", "Unavailable"}

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
        /\ effectiveQueryInput.id \in receiptMap.active
        /\ effectiveResearchAttempt.input.id \in attemptMap.active
        /\ effectiveResearchAttempt.input =
            receiptMap.values[effectiveQueryInput.id]
        /\ effectiveResearchAttempt =
            attemptMap.values[effectiveResearchAttempt.input.id]

SelectedResearchInputMatchesSelectedQueryInput ==
    effectiveResearchAttempt # NoOutcome =>
        effectiveResearchAttempt.input.queryInput =
            effectiveQueryInput

SelectedCensusMatchesAttempt ==
    effectiveCensus # NoOutcome =>
        /\ effectiveResearchAttempt.domain \in censusMap.active
        /\ effectiveCensus =
            censusMap.values[effectiveResearchAttempt.domain]
        /\ effectiveCensus.scope =
            effectiveResearchAttempt.scope
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

SelectedScopeMatchesRequest ==
    effectiveResearchAttempt # NoOutcome =>
        /\ effectiveResearchAttempt.scope = SelectedScopeId
        /\ DomainScope(effectiveResearchAttempt.domain) =
            SelectedScopeId
        /\ effectiveCensus.scope = SelectedScopeId
        /\ retainedRootAttempt.scope = SelectedScopeId

RootAttemptIsPreserved ==
    retainedRootAttempt # NoOutcome =>
        retainedRootAttempt = attemptMap.values[ResearchRootId]

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

StableScenarioInputs(
        rootKind,
        terminalAttemptScenarioKind,
        targetRequestKind,
        imageReady) ==
    /\ rootSealed
    /\ terminalAdmittedSelected
    /\ terminalSealedSelected
    /\ terminalAdmittedOpposite
    /\ terminalSealedOpposite
    /\ terminalReceiptMappedSelected
    /\ terminalResearchActiveSelected
    /\ ~duplicateSealedSelected
    /\ ~foreignSealedSelected
    /\ terminalCandidate = Target
    /\ rootAttemptKind = rootKind
    /\ terminalAttemptKind = terminalAttemptScenarioKind
    /\ requestKind = targetRequestKind
    /\ queryImageReady = imageReady
    /\ liveBinding = InitialBinding

DirectCompletionInputConstraint ==
    /\ StableScenarioInputs(
        "Resolved",
        "Resolved",
        "Carried",
        TRUE)
    /\ current = Facade
    /\ path = <<Facade>>
    /\ Len(hops) = 0
    /\ (resolutionPhase = "Terminal" =>
        /\ terminalKind = "Resolved"
        /\ terminalAssembly = Facade)

ForwardedResolvedRouteConstraint ==
    /\ current \in {Facade, Target}
    /\ path \in {<<Facade>>, <<Facade, Target>>}
    /\ Len(hops) <= 1
    /\ (resolutionPhase = "Terminal" =>
        /\ terminalKind = "Resolved"
        /\ terminalAssembly = Target
        /\ Len(hops) = 1)

ForwardedMutationInputConstraint ==
    /\ CiInputConstraint
    /\ rootAttemptKind = "DeclaringTypeForwarded"
    /\ terminalCandidate = Target
    /\ ForwardedResolvedRouteConstraint

TerminalCorrespondenceMutationInputConstraint ==
    /\ CiInputConstraint
    /\ rootAttemptKind = "DeclaringTypeForwarded"
    /\ terminalCandidate = Other
    /\ ForwardedResolvedRouteConstraint

NonResolvedMutationInputConstraint ==
    /\ rootSealed
    /\ terminalAdmittedSelected
    /\ terminalSealedSelected
    /\ terminalAdmittedOpposite
    /\ terminalSealedOpposite
    /\ terminalReceiptMappedSelected
    /\ terminalResearchActiveSelected
    /\ ~duplicateSealedSelected
    /\ ~foreignSealedSelected
    /\ rootAttemptKind = "DeclaringTypeForwarded"
    /\ terminalAttemptKind = "NotFound"
    /\ requestKind = "Carried"
    /\ queryImageReady
    /\ terminalCandidate = Target
    /\ ForwardedResolvedRouteConstraint

UnavailableMutationInputConstraint ==
    /\ CiInputConstraint
    /\ terminalCandidate = Target
    /\ current \in {Facade, Target}
    /\ path \in {<<Facade>>, <<Facade, Target>>}
    /\ Len(hops) <= 1
    /\ (resolutionPhase = "Terminal" =>
        terminalKind # "Resolved")

ForwardedCompletionInputConstraint ==
    /\ StableScenarioInputs(
        "DeclaringTypeForwarded",
        "Resolved",
        "Carried",
        TRUE)
    /\ ForwardedResolvedRouteConstraint

BlockedTerminalCensusInputConstraint ==
    /\ StableScenarioInputs(
        "DeclaringTypeForwarded",
        "Failed",
        "Carried",
        TRUE)
    /\ ForwardedResolvedRouteConstraint

ExactAddressInputConstraint ==
    /\ rootSealed
    /\ terminalAdmittedSelected
    /\ terminalSealedSelected
    /\ terminalAdmittedOpposite
    /\ terminalSealedOpposite
    /\ terminalReceiptMappedSelected
    /\ terminalResearchActiveSelected
    /\ ~duplicateSealedSelected
    /\ ~foreignSealedSelected
    /\ requestKind = "ExactAddress"
    /\ liveBinding = InitialBinding

MissingTerminalPopulationInputConstraint ==
    /\ rootSealed
    /\ terminalAdmittedSelected
    /\ ~terminalSealedSelected
    /\ ~terminalReceiptMappedSelected
    /\ ~terminalResearchActiveSelected
    /\ ~duplicateSealedSelected
    /\ ~foreignSealedSelected
    /\ queryImageReady
    /\ liveBinding = InitialBinding

ForeignPopulationInputConstraint ==
    /\ rootSealed
    /\ terminalAdmittedSelected
    /\ terminalSealedSelected
    /\ terminalReceiptMappedSelected
    /\ terminalResearchActiveSelected
    /\ ~duplicateSealedSelected
    /\ foreignSealedSelected
    /\ queryImageReady
    /\ liveBinding = InitialBinding

DuplicatePopulationInputConstraint ==
    /\ rootSealed
    /\ terminalAdmittedSelected
    /\ terminalSealedSelected
    /\ terminalReceiptMappedSelected
    /\ terminalResearchActiveSelected
    /\ duplicateSealedSelected
    /\ ~foreignSealedSelected
    /\ queryImageReady
    /\ liveBinding = InitialBinding

BroaderResearchPopulationInputConstraint ==
    /\ rootSealed
    /\ ~terminalAdmittedSelected
    /\ ~terminalSealedSelected
    /\ ~terminalReceiptMappedSelected
    /\ terminalResearchActiveSelected
    /\ ~duplicateSealedSelected
    /\ ~foreignSealedSelected
    /\ requestKind = "Carried"
    /\ queryImageReady
    /\ liveBinding = InitialBinding

MissingNonTerminalPopulationInputConstraint ==
    /\ rootSealed
    /\ terminalAdmittedSelected
    /\ terminalSealedSelected
    /\ terminalAdmittedOpposite
    /\ terminalSealedOpposite
    /\ terminalReceiptMappedSelected
    /\ terminalResearchActiveSelected
    /\ OmittedNonTerminalParticipantSelected
    /\ ~duplicateSealedSelected
    /\ ~foreignSealedSelected
    /\ terminalCandidate = Target
    /\ rootAttemptKind = "DeclaringTypeForwarded"
    /\ terminalAttemptKind = "Resolved"
    /\ requestKind = "Carried"
    /\ queryImageReady
    /\ liveBinding = InitialBinding

ImageOpenFailureInputConstraint ==
    StableScenarioInputs(
        "Resolved",
        "Resolved",
        "Carried",
        FALSE)

DirectScenarioCompletesWithRoot ==
    <>(/\ compositionPhase = "Complete"
       /\ effectiveQueryInput.registration.assembly = Facade)

ForwardedScenarioCompletesWithTerminal ==
    <>(/\ compositionPhase = "Complete"
       /\ effectiveQueryInput.registration.assembly = Target
       /\ Len(retainedHops) = 1)

BlockedTerminalCensusBecomesUnavailable ==
    <> (compositionPhase = "Unavailable")

ExactAddressBecomesRejected ==
    <> (compositionPhase = "Rejected")

MissingTerminalPopulationBecomesRejected ==
    <> (compositionPhase = "Rejected")

MissingNonTerminalPopulationBecomesRejected ==
    <> (compositionPhase = "Rejected")

ForeignPopulationBecomesRejected ==
    <> (compositionPhase = "Rejected")

DuplicatePopulationBecomesRejected ==
    <> (compositionPhase = "Rejected")

BroaderResearchPopulationBecomesRejected ==
    <> (compositionPhase = "Rejected")

ImageOpenFailureBecomesUnavailable ==
    <>(/\ compositionPhase = "Unavailable"
       /\ resolutionPhase = "Probing"
       /\ Len(hops) = 0
       /\ liveBinding = InitialBinding
       /\ ~bindingAdvanced)

CompositionConverges ==
    <>(compositionPhase \in
        {"Unavailable", "Rejected", "ContractFault", "Complete"})

=============================================================================
