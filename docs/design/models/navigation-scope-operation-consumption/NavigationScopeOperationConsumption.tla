--------------- MODULE NavigationScopeOperationConsumption ---------------
(***************************************************************************)
(* Finite composition of Navigation's protected Scope-result consumption. *)
(* Scope owns the request/result association and transition lifecycle;     *)
(* Navigation owns acceptance, protection, complete-result consumption,   *)
(* publication, and release. Subjects and lenses are opaque prepared facts. *)
(***************************************************************************)
EXTENDS Naturals, FiniteSets, Sequences

CONSTANTS Profiles, Fault, Witness

VARIABLES
    profile,
    requests,
    requestStates,
    submissionCounts,
    settlementCounts,
    results,
    cancellationResponses,
    slot,
    acceptanceCandidate,
    accepted,
    protectedOperation,
    protectedSlot,
    currentIntent,
    currentSemantic,
    revision,
    generation,
    acknowledgedRevision,
    acknowledgedGeneration,
    effectEpoch,
    effect,
    hostAuthority,
    consumerSemantic,
    consumerRevision,
    consumerGeneration,
    consumerEpoch,
    staleWork,
    maintenanceQueue,
    maintenanceApplied,
    preparation,
    resultPublished,
    publishedAssociation,
    publicationBaseSlot,
    localCancellation,
    focus,
    historyCount,
    actionConsumptions,
    visibleEffect,
    refusalSafe,
    seen

Operations == 1..2
SuccessfulOutcomes == {"Committed", "NoEffect"}
TerminalOutcomes ==
    {"Committed", "NoEffect", "Rejected", "Failed", "Cancelled",
     "Superseded", "Unavailable"}

ScopeSnapshot(members, ready) ==
    [members |-> members, ready |-> ready]

EmptyScope == ScopeSnapshot({}, {})
InitialScope == ScopeSnapshot({"old"}, {"old"})
ReplacementScope == ScopeSnapshot({"requested"}, {"requested"})
ReplacementPendingScope == ScopeSnapshot({"requested"}, {})
DuplicateScope == ScopeSnapshot({"old"}, {"old"})
DuplicatePendingScope == ScopeSnapshot({"old"}, {})

TargetOccurrence(request, snapshot) ==
    CHOOSE occurrence \in snapshot.members :
        occurrence = request.target

WrongOccurrence(request, snapshot) ==
    IF "old" \in snapshot.members THEN "old" ELSE "workspace"

Scope ==
    INSTANCE WorkspaceScopeOperationHandoff WITH
        Operations <- Operations,
        NoOperation <- 0,
        NoWorkspace <- "none",
        NoKind <- "None",
        NoSnapshot <- EmptyScope,
        NoTarget <- "none",
        NoRequestedOccurrence <- "none",
        NoOutcome <- "None",
        InitialSnapshot <- InitialScope,
        SuccessfulOutcomes <- SuccessfulOutcomes,
        TerminalOutcomes <- TerminalOutcomes,
        UnavailableOutcome <- "Unavailable",
        TargetOccurrence <- TargetOccurrence,
        WrongOccurrence <- WrongOccurrence,
        Fault <- Fault,
        requests <- requests,
        requestStates <- requestStates,
        submissionCounts <- submissionCounts,
        settlementCounts <- settlementCounts,
        results <- results,
        cancellationResponses <- cancellationResponses

NoAssociation ==
    Scope!Association("none", 0, "None", 0, FALSE)

NoAuthority ==
    [session |-> "none",
     revision |-> 0,
     intent |-> 0,
     epoch |-> 0]

Authority(newRevision, intent, epoch) ==
    [session |-> "navigation",
     revision |-> newRevision,
     intent |-> intent,
     epoch |-> epoch]

PreAcceptanceAuthority == Authority(1, 0, 0)

AuthorityIsCurrent(authority) ==
    /\ authority # NoAuthority
    /\ authority = effect
    /\ authority.session = "navigation"
    /\ authority.revision = revision
    /\ authority.intent = currentIntent
    /\ authority.epoch = effectEpoch

NoPreparation ==
    [state |-> "none",
     subject |-> "none",
     libraryContext |-> "none",
     effectiveLens |-> "none",
     requestedLens |-> "none",
     successor |-> "none"]

Prepared(
        state,
        subject,
        libraryContext,
        effectiveLens,
        requestedLens,
        successor) ==
    [state |-> state,
     subject |-> subject,
     libraryContext |-> libraryContext,
     effectiveLens |-> effectiveLens,
     requestedLens |-> requestedLens,
     successor |-> successor]

Semantic(
        scope,
        membershipCurrent,
        historicalScope,
        activeOccurrence,
        activeSubject,
        libraryContext,
        effectiveLens,
        requestedLens,
        outcome) ==
    [scope |-> scope,
     membershipCurrent |-> membershipCurrent,
     historicalScope |-> historicalScope,
     activeOccurrence |-> activeOccurrence,
     activeSubject |-> activeSubject,
     libraryContext |-> libraryContext,
     effectiveLens |-> effectiveLens,
     requestedLens |-> requestedLens,
     outcome |-> outcome]

InitialSemantic ==
    IF profile \in {"Duplicate", "DuplicatePending", "DuplicateNoIntent"}
    THEN Semantic(
        InitialScope,
        TRUE,
        EmptyScope,
        "workspace",
        "workspaceSubject",
        "none",
        "workspaceOverview",
        "workspaceOverview",
        "initial")
    ELSE Semantic(
        InitialScope,
        TRUE,
        EmptyScope,
        "old",
        "oldType",
        "markupLibrary",
        "source",
        "source",
        "initial")

ScopeKind ==
    IF profile \in
        {"Duplicate", "DuplicatePending", "DuplicateNoIntent",
         "NoEffectChangedInventory"}
    THEN "Add"
    ELSE "Replace"

HasExplicitTarget ==
    profile \in
        {"Replacement", "Forwarded", "Duplicate", "DuplicatePending",
         "MembershipPreparationFailed"}

ScopeTarget ==
    IF HasExplicitTarget
    THEN IF profile \in {"Duplicate", "DuplicatePending"}
         THEN "old"
         ELSE "requested"
    ELSE "none"

PrimaryRequest ==
    Scope!Request(
        "workspace",
        1,
        ScopeKind,
        1,
        FALSE,
        0,
        1,
        IF ScopeKind = "Add" THEN <<"old">> ELSE <<"requested">>,
        ScopeTarget,
        TRUE)

SecondaryRequest ==
    Scope!Request(
        "workspace",
        2,
        "Replace",
        1,
        FALSE,
        0,
        2,
        <<"requested">>,
        "none",
        TRUE)

ScopeOutcome ==
    CASE profile \in
            {"Replacement", "Forwarded", "NoIntentReplacement",
             "RetainedSuccessor", "MembershipPreparationFailed"} ->
                "Committed"
      [] profile \in
            {"Duplicate", "DuplicatePending", "DuplicateNoIntent",
             "NoEffectChangedInventory"} -> "NoEffect"
      [] profile = "Rejected" -> "Rejected"
      [] profile = "Failed" -> "Failed"
      [] profile = "Cancelled" -> "Cancelled"
      [] profile = "Superseded" -> "Superseded"
      [] profile = "Unavailable" -> "Unavailable"
      [] OTHER -> "Committed"

ScopeResultSnapshot ==
    CASE profile \in
            {"Replacement", "Forwarded", "NoIntentReplacement",
             "MembershipPreparationFailed"} ->
                ReplacementScope
      [] profile = "RetainedSuccessor" ->
                ScopeSnapshot({"successor"}, {"successor"})
      [] profile \in {"Duplicate", "DuplicateNoIntent"} -> DuplicateScope
      [] profile = "DuplicatePending" -> DuplicatePendingScope
      [] profile = "NoEffectChangedInventory" ->
                ScopeSnapshot({"old", "requested"}, {"old", "requested"})
      [] profile \in {"Rejected", "Failed", "Cancelled", "Superseded"} ->
                ReplacementScope
      [] OTHER -> InitialScope

PreparationForProfile ==
    CASE profile = "Forwarded" ->
        Prepared(
            "ready",
            "forwardedType",
            "baseLibrary",
            "source",
            "source",
            "none")
      [] profile = "Replacement" ->
        Prepared(
            "ready",
            "requestedType",
            "destinationLibrary",
            "source",
            "source",
            "none")
      [] profile = "RetainedSuccessor" ->
        Prepared(
            "ready",
            "successorType",
            "successorLibrary",
            "source",
            "source",
            "successor")
      [] profile = "NoIntentReplacement" ->
        Prepared(
            "ready",
            "requestedType",
            "destinationLibrary",
            "overview",
            "overview",
            "none")
      [] profile = "Duplicate" ->
        Prepared(
            "ready",
            "oldType",
            "markupLibrary",
            "source",
            "source",
            "none")
      [] profile = "DuplicatePending" ->
        Prepared(
            "unavailable",
            "none",
            "none",
            "none",
            "source",
            "none")
      [] profile = "MembershipPreparationFailed" ->
        Prepared(
            "failed",
            "none",
            "none",
            "none",
            "source",
            "none")
      [] profile = "Unavailable" ->
        Prepared(
            "historical",
            "none",
            "none",
            "none",
            "source",
            "none")
      [] OTHER ->
        Prepared(
            "ready",
            "oldType",
            "markupLibrary",
            "source",
            "source",
            "none")

ResultScope(operation) ==
    IF Fault = "OldInventory" /\ operation = 1
    THEN currentSemantic.scope
    ELSE results[operation].snapshot

CurrentMembership(operation) ==
    results[operation].outcome # "Unavailable"

RetainedActive(scope) ==
    currentSemantic.activeOccurrence \in scope.members

SelectedOccurrence(operation) ==
    LET scope == ResultScope(operation)
        explicitTarget ==
            requests[operation].association.hasExplicitTarget
        requested ==
            IF Fault = "WrongRequestedOccurrence"
            THEN "old"
            ELSE results[operation].requested
    IN
    IF ~CurrentMembership(operation)
    THEN "workspace"
    ELSE IF preparation.state # "ready"
    THEN IF RetainedActive(scope)
         THEN currentSemantic.activeOccurrence
         ELSE "workspace"
    ELSE IF results[operation].outcome \in SuccessfulOutcomes
            /\ explicitTarget
    THEN requested
    ELSE IF RetainedActive(scope)
    THEN currentSemantic.activeOccurrence
    ELSE IF preparation.successor \in scope.members
    THEN preparation.successor
    ELSE "workspace"

SemanticFor(operation) ==
    LET scope == ResultScope(operation)
        active == SelectedOccurrence(operation)
        current == CurrentMembership(operation)
        subject ==
            IF active = "workspace"
            THEN "workspaceSubject"
            ELSE IF preparation.state = "ready"
                 THEN preparation.subject
                 ELSE currentSemantic.activeSubject
        libraryContext ==
            IF active = "workspace"
            THEN "none"
            ELSE IF preparation.state = "ready"
                 THEN IF Fault = "WrongForwardedContext"
                      THEN "markupLibrary"
                      ELSE preparation.libraryContext
                 ELSE currentSemantic.libraryContext
        effectiveLens ==
            IF active = "workspace"
            THEN "workspaceOverview"
            ELSE IF preparation.state = "ready"
                 THEN preparation.effectiveLens
                 ELSE currentSemantic.effectiveLens
        outcome ==
            IF ~current
            THEN "scopeUnavailable"
            ELSE IF preparation.state = "failed"
            THEN "preparationFailed"
            ELSE IF preparation.state = "unavailable"
            THEN "preparationUnavailable"
            ELSE results[operation].outcome
    IN
    Semantic(
        scope,
        current,
        IF current THEN EmptyScope ELSE results[operation].snapshot,
        active,
        subject,
        libraryContext,
        effectiveLens,
        preparation.requestedLens,
        outcome)

scopeVars ==
    <<requests, requestStates, submissionCounts, settlementCounts, results,
      cancellationResponses>>

navigationVars ==
    <<slot, acceptanceCandidate, accepted, protectedOperation, protectedSlot,
      currentIntent, currentSemantic, revision, generation,
      acknowledgedRevision, acknowledgedGeneration, effectEpoch, effect,
      hostAuthority, consumerSemantic, consumerRevision, consumerGeneration,
      consumerEpoch, staleWork, maintenanceQueue, maintenanceApplied,
      preparation, resultPublished, publishedAssociation,
      publicationBaseSlot, localCancellation, focus, historyCount,
      actionConsumptions, visibleEffect, refusalSafe>>

RefusalStableState ==
    <<scopeVars, slot, acceptanceCandidate, accepted, protectedOperation,
      protectedSlot, currentIntent, currentSemantic, revision, generation,
      acknowledgedRevision, acknowledgedGeneration, effectEpoch, effect,
      hostAuthority, consumerSemantic, consumerRevision, consumerGeneration,
      consumerEpoch, staleWork, maintenanceQueue, maintenanceApplied,
      preparation, resultPublished, publishedAssociation, publicationBaseSlot,
      localCancellation, focus, historyCount, actionConsumptions, visibleEffect>>

vars == <<profile, scopeVars, navigationVars, seen>>

Init ==
    /\ profile \in Profiles
    /\ Scope!Init
    /\ slot = 1
    /\ acceptanceCandidate = 0
    /\ accepted = FALSE
    /\ protectedOperation = 0
    /\ protectedSlot = 0
    /\ currentIntent = 0
    /\ currentSemantic = InitialSemantic
    /\ revision = 1
    /\ generation = 1
    /\ acknowledgedRevision = 1
    /\ acknowledgedGeneration = 1
    /\ effectEpoch = 0
    /\ effect = PreAcceptanceAuthority
    /\ hostAuthority = NoAuthority
    /\ consumerSemantic = InitialSemantic
    /\ consumerRevision = 1
    /\ consumerGeneration = 1
    /\ consumerEpoch = 0
    /\ staleWork = "working"
    /\ maintenanceQueue = <<1>>
    /\ maintenanceApplied = FALSE
    /\ preparation = NoPreparation
    /\ resultPublished = FALSE
    /\ publishedAssociation = NoAssociation
    /\ publicationBaseSlot = 0
    /\ localCancellation = FALSE
    /\ focus = InitialSemantic.activeOccurrence
    /\ historyCount = 0
    /\ actionConsumptions = 0
    /\ visibleEffect = FALSE
    /\ refusalSafe = TRUE
    /\ seen = {}

IssueForeign ==
    /\ profile = "ForeignResult"
    /\ Scope!Issue(2, SecondaryRequest)
    /\ seen' = seen \union {"ForeignIssued"}
    /\ UNCHANGED <<profile, navigationVars>>

SubmitForeign ==
    /\ profile = "ForeignResult"
    /\ Scope!Submit(2)
    /\ seen' = seen \union {"ForeignSubmitted"}
    /\ UNCHANGED <<profile, navigationVars>>

SettleForeign ==
    /\ profile = "ForeignResult"
    /\ Scope!Settle(2, "Committed", "None", InitialScope, 0)
    /\ seen' = seen \union {"ForeignSettled"}
    /\ UNCHANGED <<profile, navigationVars>>

IssuePrimary ==
    /\ requestStates[1] = "Unissued"
    /\ (profile # "ForeignResult" \/ requestStates[2] = "Settled")
    /\ Scope!Issue(1, PrimaryRequest)
    /\ seen' = seen \union {"PrimaryIssued"}
    /\ UNCHANGED <<profile, slot, acceptanceCandidate, accepted,
        protectedOperation, protectedSlot, currentIntent, currentSemantic,
        revision, generation, acknowledgedRevision, acknowledgedGeneration,
        effectEpoch, effect, hostAuthority, consumerSemantic,
        consumerRevision, consumerGeneration, consumerEpoch, staleWork,
        maintenanceQueue, maintenanceApplied, preparation, resultPublished,
        publishedAssociation, publicationBaseSlot, localCancellation, focus,
        historyCount, actionConsumptions, visibleEffect, refusalSafe>>

PrepareAcceptance ==
    /\ requestStates[1] = "Issued"
    /\ acceptanceCandidate = 0
    /\ ~accepted
    /\ acceptanceCandidate' = slot
    /\ seen' = seen \union {"AcceptancePrepared"}
    /\ UNCHANGED <<profile, scopeVars, slot, accepted, protectedOperation,
        protectedSlot, currentIntent, currentSemantic, revision, generation,
        acknowledgedRevision, acknowledgedGeneration, effectEpoch, effect,
        hostAuthority, consumerSemantic, consumerRevision, consumerGeneration,
        consumerEpoch, staleWork, maintenanceQueue, maintenanceApplied,
        preparation, resultPublished, publishedAssociation,
        publicationBaseSlot, localCancellation, focus, historyCount,
        actionConsumptions, visibleEffect, refusalSafe>>

OrdinaryAdvanceBeforeAcceptance ==
    /\ profile = "StaleAcceptance"
    /\ "SlotAdvancedBeforeAcceptance" \notin seen
    /\ acceptanceCandidate = slot
    /\ ~accepted
    /\ slot' = slot + 1
    /\ currentIntent' = currentIntent + 1
    /\ effect' = NoAuthority
    /\ hostAuthority' = NoAuthority
    /\ seen' = seen \union {"SlotAdvancedBeforeAcceptance"}
    /\ UNCHANGED <<profile, scopeVars, acceptanceCandidate, accepted,
        protectedOperation, protectedSlot, currentSemantic, revision,
        generation, acknowledgedRevision, acknowledgedGeneration, effectEpoch,
        consumerSemantic, consumerRevision,
        consumerGeneration, consumerEpoch, staleWork, maintenanceQueue,
        maintenanceApplied, preparation, resultPublished,
        publishedAssociation, publicationBaseSlot, localCancellation, focus,
        historyCount, actionConsumptions, visibleEffect, refusalSafe>>

RejectStaleAcceptance ==
    /\ acceptanceCandidate # 0
    /\ acceptanceCandidate # slot
    /\ acceptanceCandidate' = 0
    /\ seen' = seen \union {"StaleAcceptanceRejected"}
    /\ UNCHANGED <<profile, scopeVars, slot, accepted, protectedOperation,
        protectedSlot, currentIntent, currentSemantic, revision, generation,
        acknowledgedRevision, acknowledgedGeneration, effectEpoch, effect,
        hostAuthority, consumerSemantic, consumerRevision, consumerGeneration,
        consumerEpoch, staleWork, maintenanceQueue, maintenanceApplied,
        preparation, resultPublished, publishedAssociation,
        publicationBaseSlot, localCancellation, focus, historyCount,
        actionConsumptions, visibleEffect, refusalSafe>>

CommitAcceptance ==
    /\ acceptanceCandidate = slot
    /\ ~accepted
    /\ requestStates[1] = "Issued"
    /\ slot' = slot + 1
    /\ acceptanceCandidate' = 0
    /\ accepted' = TRUE
    /\ protectedOperation' = 1
    /\ protectedSlot' = slot + 1
    /\ currentIntent' = currentIntent + 1
    /\ effect' = NoAuthority
    /\ hostAuthority' = NoAuthority
    /\ staleWork' = "stale"
    /\ seen' = seen \union {"ProtectedAccepted"}
    /\ UNCHANGED <<profile, scopeVars, currentSemantic, revision, generation,
        acknowledgedRevision, acknowledgedGeneration, effectEpoch,
        consumerSemantic, consumerRevision, consumerGeneration, consumerEpoch,
        maintenanceQueue, maintenanceApplied, preparation, resultPublished,
        publishedAssociation, publicationBaseSlot, localCancellation, focus,
        historyCount, actionConsumptions, visibleEffect, refusalSafe>>

SubmitProtected ==
    /\ protectedOperation = 1
    /\ protectedSlot = slot
    /\ Scope!Submit(1)
    /\ seen' = seen \union {"ProtectedSubmitted"}
    /\ UNCHANGED <<profile, navigationVars>>

BrokenSubmitBeforeAcceptance ==
    /\ Fault = "SubmitBeforeAcceptance"
    /\ requestStates[1] = "Issued"
    /\ protectedOperation = 0
    /\ Scope!Submit(1)
    /\ seen' = seen \union {"SubmittedBeforeAcceptance"}
    /\ UNCHANGED <<profile, navigationVars>>

IssueLaterScopeCommand ==
    /\ profile = "LaterRefusal"
    /\ protectedOperation = 1
    /\ requestStates[2] = "Unissued"
    /\ Scope!Issue(2, SecondaryRequest)
    /\ seen' = seen \union {"LaterScopeIssued"}
    /\ UNCHANGED <<profile, navigationVars>>

RefuseLaterCommand(commandKind) ==
    /\ protectedOperation = 1
    /\ commandKind \in {"subject", "lens", "coordinate", "restoration"}
    /\ IF Fault = "RefusalSupersedes"
       THEN /\ currentIntent' = currentIntent + 1
            /\ focus' = "workspace"
            /\ historyCount' = historyCount + 1
            /\ actionConsumptions' = actionConsumptions + 1
       ELSE /\ UNCHANGED
                <<currentIntent, focus, historyCount, actionConsumptions>>
    /\ seen' = seen \union {"LaterCommandRefused"}
    /\ UNCHANGED <<profile, scopeVars, slot, acceptanceCandidate, accepted,
        protectedOperation, protectedSlot, currentSemantic, revision,
        generation, acknowledgedRevision, acknowledgedGeneration, effectEpoch,
        effect, hostAuthority, consumerSemantic, consumerRevision,
        consumerGeneration, consumerEpoch, staleWork, maintenanceQueue,
        maintenanceApplied, preparation, resultPublished,
        publishedAssociation, publicationBaseSlot, localCancellation,
        visibleEffect>>
    /\ refusalSafe' = (refusalSafe /\ UNCHANGED RefusalStableState)

RefuseLaterScopeCommand ==
    /\ protectedOperation = 1
    /\ requestStates[2] = "Issued"
    /\ seen' = seen \union {"LaterScopeRefused"}
    /\ UNCHANGED <<profile, scopeVars, slot, acceptanceCandidate, accepted,
        protectedOperation, protectedSlot, currentIntent, currentSemantic,
        revision, generation, acknowledgedRevision, acknowledgedGeneration,
        effectEpoch, effect, hostAuthority, consumerSemantic,
        consumerRevision, consumerGeneration, consumerEpoch, staleWork,
        maintenanceQueue, maintenanceApplied, preparation, resultPublished,
        publishedAssociation, publicationBaseSlot, localCancellation, focus,
        historyCount, actionConsumptions, visibleEffect>>
    /\ refusalSafe' = (refusalSafe /\ UNCHANGED RefusalStableState)

IssueSuperseder ==
    /\ profile = "Superseded"
    /\ requestStates[1] = "Submitted"
    /\ requestStates[2] = "Unissued"
    /\ Scope!Issue(2, SecondaryRequest)
    /\ seen' = seen \union {"SupersederIssued"}
    /\ UNCHANGED <<profile, navigationVars>>

SettlePrimary ==
    /\ requestStates[1] = "Submitted"
    /\ (ScopeOutcome # "Superseded"
        \/ requestStates[2] \in {"Issued", "Submitted", "Settled"})
    /\ Scope!Settle(
        1,
        ScopeOutcome,
        "None",
        ScopeResultSnapshot,
        IF ScopeOutcome = "Superseded" THEN 2 ELSE 0)
    /\ seen' = seen \union {"PrimarySettled", ScopeOutcome}
    /\ UNCHANGED <<profile, navigationVars>>

ObserveCancellationNoEffect ==
    /\ protectedOperation = 1
    /\ requestStates[1] \in {"Issued", "Settled"}
    /\ cancellationResponses[1].kind = "None"
    /\ Scope!ObserveCancellationNoEffect(
        1,
        IF requestStates[1] = "Settled"
        THEN results[1].outcome
        ELSE "None")
    /\ IF Fault = "ReleaseOnControlNoEffect"
       THEN protectedOperation' = 0
       ELSE UNCHANGED protectedOperation
    /\ seen' = seen \union {"CancellationObservedNoEffect"}
    /\ UNCHANGED <<profile, slot, acceptanceCandidate, accepted,
        protectedSlot, currentIntent, currentSemantic, revision, generation,
        acknowledgedRevision, acknowledgedGeneration, effectEpoch, effect,
        hostAuthority, consumerSemantic, consumerRevision, consumerGeneration,
        consumerEpoch, staleWork, maintenanceQueue, maintenanceApplied,
        preparation, resultPublished, publishedAssociation,
        publicationBaseSlot, localCancellation, focus, historyCount,
        actionConsumptions, visibleEffect, refusalSafe>>

ReturnCancellationSettlement ==
    /\ protectedOperation = 1
    /\ Scope!ReturnCancellationSettlement(1)
    /\ seen' = seen \union {"CancellationReturnedSettlement"}
    /\ UNCHANGED <<profile, navigationVars>>

LocalCancel ==
    /\ protectedOperation = 1
    /\ requestStates[1] = "Submitted"
    /\ ~localCancellation
    /\ localCancellation' = TRUE
    /\ seen' = seen \union {"LocalCancellationObserved"}
    /\ UNCHANGED <<profile, scopeVars, slot, acceptanceCandidate, accepted,
        protectedOperation, protectedSlot, currentIntent, currentSemantic,
        revision, generation, acknowledgedRevision, acknowledgedGeneration,
        effectEpoch, effect, hostAuthority, consumerSemantic,
        consumerRevision, consumerGeneration, consumerEpoch, staleWork,
        maintenanceQueue, maintenanceApplied, preparation, resultPublished,
        publishedAssociation, publicationBaseSlot, focus, historyCount,
        actionConsumptions, visibleEffect, refusalSafe>>

PrepareNavigationResult ==
    /\ protectedOperation = 1
    /\ requestStates[1] = "Settled"
    /\ preparation.state = "none"
    /\ preparation' = PreparationForProfile
    /\ seen' = seen \union {"NavigationPrepared", PreparationForProfile.state}
    /\ UNCHANGED <<profile, scopeVars, slot, acceptanceCandidate, accepted,
        protectedOperation, protectedSlot, currentIntent, currentSemantic,
        revision, generation, acknowledgedRevision, acknowledgedGeneration,
        effectEpoch, effect, hostAuthority, consumerSemantic,
        consumerRevision, consumerGeneration, consumerEpoch, staleWork,
        maintenanceQueue, maintenanceApplied, resultPublished,
        publishedAssociation, publicationBaseSlot, localCancellation, focus,
        historyCount, actionConsumptions, visibleEffect, refusalSafe>>

PublishResult(operation) ==
    LET semantic == SemanticFor(operation)
        nextRevision ==
            IF semantic = currentSemantic THEN revision ELSE revision + 1
        nextGeneration == generation + 1
        nextEpoch == effectEpoch + 1
    IN
    /\ protectedOperation = 1
    /\ protectedSlot = slot
    /\ requestStates[operation] = "Settled"
    /\ preparation.state # "none"
    /\ ~resultPublished
    /\ currentSemantic' = semantic
    /\ revision' = nextRevision
    /\ generation' = nextGeneration
    /\ effectEpoch' = nextEpoch
    /\ effect' = Authority(nextRevision, currentIntent, nextEpoch)
    /\ hostAuthority' = NoAuthority
    /\ slot' = slot + 1
    /\ protectedOperation' = 0
    /\ resultPublished' = TRUE
    /\ publishedAssociation' = results[operation].association
    /\ publicationBaseSlot' = protectedSlot
    /\ seen' =
        seen \union {"NavigationPublished", results[operation].outcome}
    /\ UNCHANGED <<profile, scopeVars, acceptanceCandidate, accepted,
        protectedSlot, currentIntent, acknowledgedRevision,
        acknowledgedGeneration, consumerSemantic, consumerRevision,
        consumerGeneration, consumerEpoch, staleWork, maintenanceQueue,
        maintenanceApplied, preparation, localCancellation, focus,
        historyCount, actionConsumptions, visibleEffect, refusalSafe>>

PublishPrimaryResult ==
    /\ Fault # "ForeignSettlement"
    /\ PublishResult(1)

PublishForeignResult ==
    /\ Fault = "ForeignSettlement"
    /\ profile = "ForeignResult"
    /\ PublishResult(2)

CompleteStaleWork ==
    /\ staleWork = "stale"
    /\ protectedOperation = 1
    /\ IF Fault = "StaleWorkReplaces"
       THEN /\ currentSemantic' =
                [currentSemantic EXCEPT
                    !.activeOccurrence = "old",
                    !.activeSubject = "oldType"]
            /\ slot' = slot + 1
            /\ staleWork' = staleWork
       ELSE /\ staleWork' = "discarded"
            /\ UNCHANGED <<currentSemantic, slot>>
    /\ seen' = seen \union {"StaleWorkCompleted"}
    /\ UNCHANGED <<profile, scopeVars, acceptanceCandidate, accepted,
        protectedOperation, protectedSlot, currentIntent, revision, generation,
        acknowledgedRevision, acknowledgedGeneration, effectEpoch, effect,
        hostAuthority, consumerSemantic, consumerRevision, consumerGeneration,
        consumerEpoch, maintenanceQueue, maintenanceApplied, preparation,
        resultPublished, publishedAssociation, publicationBaseSlot,
        localCancellation, focus, historyCount, actionConsumptions,
        visibleEffect, refusalSafe>>

DiscardStaleAfterSettlement ==
    /\ staleWork = "stale"
    /\ resultPublished
    /\ staleWork' = "discarded"
    /\ seen' = seen \union {"StaleWorkDiscarded"}
    /\ UNCHANGED <<profile, scopeVars, slot, acceptanceCandidate, accepted,
        protectedOperation, protectedSlot, currentIntent, currentSemantic,
        revision, generation, acknowledgedRevision, acknowledgedGeneration,
        effectEpoch, effect, hostAuthority, consumerSemantic,
        consumerRevision, consumerGeneration, consumerEpoch, maintenanceQueue,
        maintenanceApplied, preparation, resultPublished,
        publishedAssociation, publicationBaseSlot, localCancellation, focus,
        historyCount, actionConsumptions, visibleEffect, refusalSafe>>

AttemptOldEffect ==
    /\ accepted
    /\ protectedOperation = 1
    /\ "OldEffectAttempted" \notin seen
    /\ IF Fault = "StaleEffectExecutes"
            \/ AuthorityIsCurrent(PreAcceptanceAuthority)
       THEN /\ focus' = "old"
            /\ historyCount' = historyCount + 1
            /\ visibleEffect' = TRUE
       ELSE UNCHANGED <<focus, historyCount, visibleEffect>>
    /\ seen' = seen \union {"OldEffectAttempted"}
    /\ UNCHANGED <<profile, scopeVars, slot, acceptanceCandidate, accepted,
        protectedOperation, protectedSlot, currentIntent, currentSemantic,
        revision, generation, acknowledgedRevision, acknowledgedGeneration,
        effectEpoch, effect, hostAuthority, consumerSemantic,
        consumerRevision, consumerGeneration, consumerEpoch, staleWork,
        maintenanceQueue, maintenanceApplied, preparation, resultPublished,
        publishedAssociation, publicationBaseSlot, localCancellation,
        actionConsumptions, refusalSafe>>

PostConsumer ==
    /\ resultPublished
    /\ effect # NoAuthority
    /\ hostAuthority = NoAuthority
    /\ AuthorityIsCurrent(effect)
    /\ hostAuthority' = effect
    /\ consumerSemantic' = currentSemantic
    /\ consumerRevision' = revision
    /\ consumerGeneration' = generation
    /\ consumerEpoch' = effectEpoch
    /\ seen' = seen \union {"ConsumerPosted"}
    /\ UNCHANGED <<profile, scopeVars, slot, acceptanceCandidate, accepted,
        protectedOperation, protectedSlot, currentIntent, currentSemantic,
        revision, generation, acknowledgedRevision, acknowledgedGeneration,
        effectEpoch, effect, staleWork, maintenanceQueue,
        maintenanceApplied, preparation, resultPublished,
        publishedAssociation, publicationBaseSlot, localCancellation, focus,
        historyCount, actionConsumptions, visibleEffect, refusalSafe>>

ExecuteVisibleEffect ==
    /\ hostAuthority = effect
    /\ effect # NoAuthority
    /\ consumerEpoch = effect.epoch
    /\ consumerSemantic = currentSemantic
    /\ ~visibleEffect
    /\ focus' = currentSemantic.activeOccurrence
    /\ historyCount' = historyCount + 1
    /\ visibleEffect' = TRUE
    /\ seen' = seen \union {"VisibleEffectExecuted"}
    /\ UNCHANGED <<profile, scopeVars, slot, acceptanceCandidate, accepted,
        protectedOperation, protectedSlot, currentIntent, currentSemantic,
        revision, generation, acknowledgedRevision, acknowledgedGeneration,
        effectEpoch, effect, hostAuthority, consumerSemantic,
        consumerRevision, consumerGeneration, consumerEpoch, staleWork,
        maintenanceQueue, maintenanceApplied, preparation, resultPublished,
        publishedAssociation, publicationBaseSlot, localCancellation,
        actionConsumptions, refusalSafe>>

Acknowledge ==
    /\ visibleEffect
    /\ hostAuthority = effect
    /\ consumerEpoch = effect.epoch
    /\ acknowledgedRevision' = consumerRevision
    /\ acknowledgedGeneration' = consumerGeneration
    /\ effect' = NoAuthority
    /\ hostAuthority' = NoAuthority
    /\ seen' = seen \union {"Acknowledged"}
    /\ UNCHANGED <<profile, scopeVars, slot, acceptanceCandidate, accepted,
        protectedOperation, protectedSlot, currentIntent, currentSemantic,
        revision, generation, effectEpoch, consumerSemantic, consumerRevision,
        consumerGeneration, consumerEpoch, staleWork, maintenanceQueue,
        maintenanceApplied, preparation, resultPublished,
        publishedAssociation, publicationBaseSlot, localCancellation, focus,
        historyCount, actionConsumptions, visibleEffect, refusalSafe>>

ApplyMaintenance ==
    /\ accepted
    /\ protectedOperation = 0
    /\ resultPublished
    /\ maintenanceQueue = <<1>>
    /\ maintenanceQueue' = <<>>
    /\ maintenanceApplied' = TRUE
    /\ slot' = slot + 1
    /\ seen' = seen \union {"MaintenanceApplied"}
    /\ UNCHANGED <<profile, scopeVars, acceptanceCandidate, accepted,
        protectedOperation, protectedSlot, currentIntent, currentSemantic,
        revision, generation, acknowledgedRevision, acknowledgedGeneration,
        effectEpoch, effect, hostAuthority, consumerSemantic,
        consumerRevision, consumerGeneration, consumerEpoch, staleWork,
        preparation, resultPublished, publishedAssociation,
        publicationBaseSlot, localCancellation, focus, historyCount,
        actionConsumptions, visibleEffect, refusalSafe>>

Next ==
    \/ IssueForeign
    \/ SubmitForeign
    \/ SettleForeign
    \/ IssuePrimary
    \/ PrepareAcceptance
    \/ OrdinaryAdvanceBeforeAcceptance
    \/ RejectStaleAcceptance
    \/ CommitAcceptance
    \/ SubmitProtected
    \/ BrokenSubmitBeforeAcceptance
    \/ IssueLaterScopeCommand
    \/ \E commandKind \in
        {"subject", "lens", "coordinate", "restoration"} :
            RefuseLaterCommand(commandKind)
    \/ RefuseLaterScopeCommand
    \/ IssueSuperseder
    \/ SettlePrimary
    \/ ObserveCancellationNoEffect
    \/ ReturnCancellationSettlement
    \/ LocalCancel
    \/ PrepareNavigationResult
    \/ PublishPrimaryResult
    \/ PublishForeignResult
    \/ CompleteStaleWork
    \/ DiscardStaleAfterSettlement
    \/ AttemptOldEffect
    \/ PostConsumer
    \/ ExecuteVisibleEffect
    \/ Acknowledge
    \/ ApplyMaintenance
    /\ UNCHANGED profile

SafetySpec == Init /\ [][Next]_vars

ScopeOwnerAssumptions == Scope!OwnerAssumptions
ScopeOwnerSafety ==
    /\ Scope!TypeOK
    /\ Scope!RequestLifecycleIsOneShot
    /\ Scope!TerminalResultsPreserveAssociation
    /\ Scope!CancellationResponsesAreTyped
ScopeBehaviorRefinesOwner == Scope!SafetySpec
ScopeControlNoEffectCannotSettleMutation ==
    Scope!ControlNoEffectCannotSettleMutation

TypeOK ==
    /\ profile \in Profiles
    /\ slot \in Nat /\ slot > 0
    /\ acceptanceCandidate \in Nat
    /\ accepted \in BOOLEAN
    /\ protectedOperation \in {0, 1}
    /\ protectedSlot \in Nat
    /\ currentIntent \in Nat
    /\ currentSemantic.membershipCurrent \in BOOLEAN
    /\ revision \in Nat /\ revision > 0
    /\ generation \in Nat /\ generation > 0
    /\ acknowledgedRevision \in Nat
    /\ acknowledgedGeneration \in Nat
    /\ effectEpoch \in Nat
    /\ staleWork \in {"working", "stale", "discarded"}
    /\ maintenanceQueue \in {<<1>>, <<>>}
    /\ maintenanceApplied \in BOOLEAN
    /\ preparation.state \in
        {"none", "ready", "unavailable", "failed", "historical"}
    /\ resultPublished \in BOOLEAN
    /\ localCancellation \in BOOLEAN
    /\ historyCount \in Nat
    /\ actionConsumptions \in Nat
    /\ visibleEffect \in BOOLEAN
    /\ refusalSafe \in BOOLEAN

ProtectedAcceptancePrecedesSubmission ==
    submissionCounts[1] = 1 => accepted

ProtectionBindsExactScopeAssociation ==
    protectedOperation = 1 =>
        /\ requests[1].association.operation = 1
        /\ requests[1].association.workspace = "workspace"
        /\ protectedSlot = slot

OnlyCorrelatedSettlementPublishes ==
    resultPublished =>
        /\ requestStates[1] = "Settled"
        /\ publishedAssociation = requests[1].association
        /\ publicationBaseSlot = protectedSlot

CancellationControlCannotReleaseProtection ==
    cancellationResponses[1].kind = "ObservedNoEffect"
        /\ ~resultPublished =>
            protectedOperation = 1

LocalCancellationCannotAbandonSubmittedEffect ==
    localCancellation /\ ~resultPublished =>
        /\ protectedOperation = 1
        /\ requestStates[1] \in {"Submitted", "Settled"}

LaterRefusalPreservesNavigationState == refusalSafe

QueuedMaintenanceSurvivesProtection ==
    protectedOperation = 1 =>
        /\ maintenanceQueue = <<1>>
        /\ ~maintenanceApplied

StaleWorkCannotReplaceDuringProtection ==
    protectedOperation = 1 =>
        /\ slot = protectedSlot
        /\ currentSemantic = InitialSemantic

StaleAuthorityCannotExecuteDuringProtection ==
    protectedOperation = 1 => ~visibleEffect

CompleteScopeResultIsConsumed ==
    resultPublished =>
        IF results[1].outcome = "Unavailable"
        THEN /\ ~currentSemantic.membershipCurrent
             /\ currentSemantic.historicalScope =
                results[1].snapshot
        ELSE /\ currentSemantic.membershipCurrent
             /\ currentSemantic.scope =
                results[1].snapshot

MembershipPreparationFailureIsCurrentFailure ==
    resultPublished
        /\ results[1].outcome = "Committed"
        /\ preparation.state \in {"failed", "unavailable"} =>
            /\ currentSemantic.scope = results[1].snapshot
            /\ currentSemantic.outcome \in
                {"preparationFailed", "preparationUnavailable"}

ForwardedPreparedContextIsPublished ==
    resultPublished /\ profile = "Forwarded" =>
        /\ currentSemantic.activeSubject = "forwardedType"
        /\ currentSemantic.libraryContext = "baseLibrary"

ExactRequestedOccurrenceActivates ==
    resultPublished
        /\ results[1].outcome \in SuccessfulOutcomes
        /\ requests[1].association.hasExplicitTarget
        /\ preparation.state = "ready" =>
            currentSemantic.activeOccurrence = results[1].requested

OwnerPolicyDoesNotInventActivation ==
    resultPublished
        /\ ~requests[1].association.hasExplicitTarget
        /\ ~RetainedActive(results[1].snapshot)
        /\ preparation.successor = "none" =>
            currentSemantic.activeOccurrence = "workspace"

AuthorizedSuccessorIsExact ==
    resultPublished
        /\ preparation.successor # "none"
        /\ ~requests[1].association.hasExplicitTarget =>
            currentSemantic.activeOccurrence = preparation.successor

RevisionAndGenerationRemainDistinct ==
    resultPublished =>
        /\ generation = 2
        /\ revision =
            IF currentSemantic = InitialSemantic THEN 1 ELSE 2

CurrentEffectAuthorityIsExact ==
    effect # NoAuthority =>
        /\ effect.session = "navigation"
        /\ effect.revision = revision
        /\ effect.intent = currentIntent
        /\ effect.epoch = effectEpoch

ConsumerPostingUsesCurrentAuthority ==
    hostAuthority # NoAuthority =>
        /\ hostAuthority = effect
        /\ consumerSemantic = currentSemantic
        /\ consumerRevision = revision
        /\ consumerGeneration = generation
        /\ consumerEpoch = effectEpoch

AcknowledgementUsesCompositePublication ==
    effect = NoAuthority /\ resultPublished /\ visibleEffect =>
        /\ acknowledgedRevision = consumerRevision
        /\ acknowledgedGeneration = consumerGeneration

ProtectedAttemptEventuallyReleases ==
    accepted /\ requestStates[1] = "Submitted"
        ~> resultPublished /\ protectedOperation = 0

MaintenanceEventuallyResumes ==
    accepted /\ resultPublished ~> maintenanceQueue = <<>>

Framed(action) == action /\ UNCHANGED profile

Fairness ==
    /\ WF_vars(Framed(IssuePrimary))
    /\ WF_vars(Framed(PrepareAcceptance))
    /\ WF_vars(Framed(CommitAcceptance))
    /\ WF_vars(Framed(SubmitProtected))
    /\ WF_vars(Framed(IssueSuperseder))
    /\ WF_vars(Framed(SettlePrimary))
    /\ WF_vars(Framed(PrepareNavigationResult))
    /\ WF_vars(Framed(PublishPrimaryResult))
    /\ WF_vars(Framed(PostConsumer))
    /\ WF_vars(Framed(ExecuteVisibleEffect))
    /\ WF_vars(Framed(Acknowledge))
    /\ WF_vars(Framed(ApplyMaintenance))

Spec == SafetySpec /\ Fairness

ObservedEvents == seen
NoWitness == Witness \notin ObservedEvents

=============================================================================
