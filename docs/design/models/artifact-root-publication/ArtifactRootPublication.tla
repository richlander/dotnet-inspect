--------------------- MODULE ArtifactRootPublication ---------------------
(***************************************************************************)
(* Models the Artifact Acquisition owned handoff from provisional physical *)
(* Root preparation to current runtime Workspace composition, specified by *)
(* "Artifact Root preparation and scope publication" in                    *)
(* docs/design/artifact-acquisition-and-workspaces.md.                     *)
(*                                                                         *)
(* One focused claim: PublishArtifactRootComposition either commits the    *)
(* logical Scope pointer and the physical Root composition together with   *)
(* one fresh owner-issued composition identity and terminal Published      *)
(* receipts, or commits neither, releases every listed prepared batch, and *)
(* preserves both old current states -- while every preparation settles    *)
(* under its finite deadline, retired Roots reject new query entry, and an *)
(* already admitted lease drains.                                          *)
(*                                                                         *)
(* The owner-issued join currencies modeled here are the preparation       *)
(* receipt identity and state, the current and reserved candidate physical *)
(* composition generation identities, the retained Root generation         *)
(* reference, the Scope publication base carried by the sealed             *)
(* participant, and the current logical/physical pair an observer reads.   *)
(* Concrete representations (opaque handles, typed results, coordinates)    *)
(* are deliberately abstracted; only the distinctions that make the join    *)
(* sound are preserved.                                                    *)
(*                                                                         *)
(* Scenario switches ("Enable*") bound which independent concurrent        *)
(* activity a configuration explores. They never relax a guard, a check    *)
(* order, or an invariant: each one only removes an orthogonal actor from  *)
(* Next, so a configuration that disables one explores a subgraph of the   *)
(* configuration that enables it. Splitting the four actors keeps every    *)
(* positive configuration far inside the repository's check budget, and    *)
(* the model README records which configuration owns each required         *)
(* behavior and which actor it needs.                                      *)
(***************************************************************************)
EXTENDS Integers, FiniteSets

CONSTANTS
    RootA,                              \* current Root; the plan retains it
    RootB,                              \* current Root; the plan omits it
    RootC,                              \* adopted from PreparationOne
    RootD,                              \* adopted from PreparationTwo
    PreparationOne,
    PreparationTwo,
    \* Scenario switches. They bound independent concurrent actors so a
    \* focused configuration checks its property over the smallest graph
    \* that still contains the behavior; they never weaken a check.
    EnableObservers,        \* gate-observing reads and new query entry
    EnableCallerRelease,    \* explicit ReleaseArtifactRootPreparation races
    EnableRetry,            \* the second publication attempt
    EnableLeaseDrain,       \* a query lease admitted before publication
    \* Mutation switches. Every positive configuration sets the "Allow*"
    \* switches FALSE and the "Enforce*" switches TRUE.
    AllowYieldingCommit,
    AllowRefusedParticipantPublish,
    AllowRetiredRootEntry,
    AllowReceiptReuse,
    AllowParticipantReuse,
    AllowReleaseDuringPublishing,
    AllowConsumptionBeforeShapeValidation,
    AllowOmittedDeadlineSettlement,
    AllowRetirementToCutLease,
    AllowSynthesizedCompositionIdentity,
    EnforceCheckPrecedence

Roots == {RootA, RootB, RootC, RootD}
Preparations == {PreparationOne, PreparationTwo}

ASSUME
    /\ Cardinality(Roots) = 4
    /\ Cardinality(Preparations) = 2
    /\ EnableObservers \in BOOLEAN
    /\ EnableCallerRelease \in BOOLEAN
    /\ EnableRetry \in BOOLEAN
    /\ EnableLeaseDrain \in BOOLEAN
    /\ AllowYieldingCommit \in BOOLEAN
    /\ AllowRefusedParticipantPublish \in BOOLEAN
    /\ AllowRetiredRootEntry \in BOOLEAN
    /\ AllowReceiptReuse \in BOOLEAN
    /\ AllowParticipantReuse \in BOOLEAN
    /\ AllowReleaseDuringPublishing \in BOOLEAN
    /\ AllowConsumptionBeforeShapeValidation \in BOOLEAN
    /\ AllowOmittedDeadlineSettlement \in BOOLEAN
    /\ AllowRetirementToCutLease \in BOOLEAN
    /\ AllowSynthesizedCompositionIdentity \in BOOLEAN
    /\ EnforceCheckPrecedence \in BOOLEAN

(***************************************************************************)
(* Finite harness bounds. Generation and identity values are abstract       *)
(* owner-issued tokens; monotone integers preserve issuance freshness and   *)
(* non-reuse without reproducing the opaque product representation.         *)
(***************************************************************************)
MaxGeneration == 2
AbsentGeneration == 0
Generations == 0..MaxGeneration
CompositionIds == 0..10
ScopeBases == 0..8
MaxScopeSupersessionBase == 3

EmptyComposition == [r \in Roots |-> AbsentGeneration]

InitialComposition ==
    [r \in Roots |-> IF r \in {RootA, RootB} THEN 1 ELSE AbsentGeneration]

\* The desired complete physical set: retain RootA at its current
\* generation, adopt RootC and RootD from the two listed receipts, and omit
\* (retire) RootB.
DesiredComposition(retainedGeneration) ==
    [r \in Roots |->
        CASE r = RootA -> retainedGeneration
          [] r = RootC -> MaxGeneration
          [] r = RootD -> MaxGeneration
          [] OTHER     -> AbsentGeneration]

ReceiptStates == {"Prepared", "Publishing", "Published", "Released"}
TerminalReceiptStates == {"Published", "Released"}

ReceiptStateRefusals ==
    {"PreparationAlreadyPublished", "PreparationReleased",
     "PreparationPublishing"}

ApplicabilityRefusalReasons ==
    ReceiptStateRefusals \cup
    {"WorkspaceClosed", "Cancelled", "DeadlineExpired", "CompositionMismatch",
     "GenerationMismatch", "BudgetExceeded"}

RefusalReasons ==
    ApplicabilityRefusalReasons \cup
    {"None", "Malformed", "ParticipantRefused", "ParticipantAlreadyConsumed"}

OperationResults == RefusalReasons \cup {"Published"}

ReleaseResults ==
    {"None", "Released", "NoEffect", "PreparationPublishing",
     "PreparationAlreadyPublished"}

OperationPhases ==
    {"Unsubmitted", "Submitted", "ShapeOk", "Staged", "TokenIssued",
     "CommitYield", "Committed", "Rejected"}

ParticipantStates == {"Available", "TokenIssued", "Committed", "Refused"}

PointerSwapPhases == {"None", "PhysicalOnly", "Complete"}

ObservedPairKinds == {"Old", "New", "Half"}

EntryOutcomeKinds == {"AdmittedCurrent", "AdmittedRetired", "Rejected"}

VARIABLES
    opPhase,                    \* publication operation phase
    opResult,                   \* typed operation result
    gateRefusalReason,          \* reason actually reported at the gate
    gateRefusalExpected,        \* first applicable reason in owner order
    retryResult,                \* typed result of the second attempt
    receiptState,               \* receipt lifecycle per preparation identity
    adoptionCount,              \* how often each receipt's batch was adopted
    callerReleased,             \* receipt drained through caller release
    releaseResult,              \* typed ReleaseArtifactRootPreparation result
    releasedByOperation,        \* receipts the publication operation released
    participantState,           \* sealed Scope participant lifecycle
    participantExpectedBase,    \* Scope publication base the participant holds
    currentScopeBase,           \* Scope's current publication base
    publicationScopeSwapCount,  \* logical pointer swaps this protocol caused
    currentCompositionId,       \* current physical composition generation
    currentRoots,               \* current physical Root admission
    pointerSwapPhase,           \* atomicity of the old-to-new commit
    nextCompositionId,          \* fresh non-reused identity source
    reservedCompositionId,      \* unpublished candidate composition identity
    discardedCandidateId,       \* candidate identity permanently discarded
    stagedRoots,                \* privately staged composition
    planExpectedComposition,    \* plan's expected composition generation
    planRetainedGeneration,     \* plan's Retain(RootA, generation) reference
    planMalformed,              \* plan fails owner shape validation
    cancelled,
    deadlineExpired,
    workspaceOpen,
    budgetSufficient,
    observedPairs,              \* logical/physical pairs an observer read
    entryOutcomes,              \* outcomes of new query-entry attempts
    existingLease               \* lease admitted on RootB before publication

planVars ==
    <<planExpectedComposition, planRetainedGeneration, planMalformed>>
receiptVars ==
    <<receiptState, adoptionCount, callerReleased, releaseResult,
      releasedByOperation>>
currentVars ==
    <<currentScopeBase, publicationScopeSwapCount, currentCompositionId,
      currentRoots, pointerSwapPhase>>
candidateVars ==
    <<nextCompositionId, reservedCompositionId, discardedCandidateId,
      stagedRoots>>
envVars == <<cancelled, deadlineExpired, workspaceOpen, budgetSufficient>>
observerVars == <<observedPairs, entryOutcomes, existingLease>>
opVars ==
    <<opPhase, opResult, gateRefusalReason, gateRefusalExpected, retryResult>>
participantVars == <<participantState, participantExpectedBase>>

vars ==
    <<planVars, receiptVars, currentVars, candidateVars, envVars,
      observerVars, opVars, participantVars>>

(***************************************************************************)
(* The runtime Workspace composition gate is one asynchronous exclusion    *)
(* boundary. PublishArtifactRootComposition holds it from applicability    *)
(* revalidation through the non-yielding commit, so no gate-observing      *)
(* action interleaves. "CommitYield" exists only under                     *)
(* AllowYieldingCommit: it is the mutation that lets the commit region     *)
(* yield between the two internal pointer assignments.                     *)
(***************************************************************************)
GateHeld == opPhase \in {"Staged", "TokenIssued"}

ListedPrepared == {p \in Preparations : receiptState[p] = "Prepared"}

ReleaseAllStillPrepared ==
    [p \in Preparations |->
        IF receiptState[p] = "Prepared" THEN "Released" ELSE receiptState[p]]

\* Once the operation owns the batch (every listed receipt is Publishing),
\* a refusal drains the complete provisional batch.
ReleaseAllListed == [p \in Preparations |-> "Released"]

ReceiptRefusalFor(p) ==
    CASE receiptState[p] = "Published"  -> "PreparationAlreadyPublished"
      [] receiptState[p] = "Released"   -> "PreparationReleased"
      [] receiptState[p] = "Publishing" -> "PreparationPublishing"
      [] OTHER                          -> "None"

\* Receipt-state precedence reports the first non-Prepared receipt in plan
\* order; PreparationOne precedes PreparationTwo in the plan.
ReceiptStateRefusal ==
    IF ReceiptRefusalFor(PreparationOne) # "None"
    THEN ReceiptRefusalFor(PreparationOne)
    ELSE ReceiptRefusalFor(PreparationTwo)

\* The exact owner order: listed receipt states in plan order, the open
\* Workspace, cancellation and deadline, expected composition generation,
\* every retained generation reference, then admission budgets.
FirstApplicableRefusal ==
    IF ReceiptStateRefusal # "None" THEN ReceiptStateRefusal
    ELSE IF ~workspaceOpen THEN "WorkspaceClosed"
    ELSE IF cancelled THEN "Cancelled"
    ELSE IF deadlineExpired THEN "DeadlineExpired"
    ELSE IF planExpectedComposition # currentCompositionId
         THEN "CompositionMismatch"
    ELSE IF planRetainedGeneration # currentRoots[RootA]
         THEN "GenerationMismatch"
    ELSE IF ~budgetSufficient THEN "BudgetExceeded"
    ELSE "None"

ApplicableRefusalSet ==
    (IF ReceiptStateRefusal # "None" THEN {ReceiptStateRefusal} ELSE {})
    \cup (IF ~workspaceOpen THEN {"WorkspaceClosed"} ELSE {})
    \cup (IF cancelled THEN {"Cancelled"} ELSE {})
    \cup (IF deadlineExpired THEN {"DeadlineExpired"} ELSE {})
    \cup (IF planExpectedComposition # currentCompositionId
          THEN {"CompositionMismatch"} ELSE {})
    \cup (IF planRetainedGeneration # currentRoots[RootA]
          THEN {"GenerationMismatch"} ELSE {})
    \cup (IF ~budgetSufficient THEN {"BudgetExceeded"} ELSE {})

-----------------------------------------------------------------------------
Init ==
    /\ opPhase = "Unsubmitted"
    /\ opResult = "None"
    /\ gateRefusalReason = "None"
    /\ gateRefusalExpected = "None"
    /\ retryResult = "None"
    /\ receiptState = [p \in Preparations |-> "Prepared"]
    /\ adoptionCount = [p \in Preparations |-> 0]
    /\ callerReleased = [p \in Preparations |-> FALSE]
    /\ releaseResult = [p \in Preparations |-> "None"]
    /\ releasedByOperation = {}
    /\ participantState = "Available"
    /\ participantExpectedBase = 0
    /\ currentScopeBase = 1
    /\ publicationScopeSwapCount = 0
    /\ currentCompositionId = 1
    /\ currentRoots = InitialComposition
    /\ pointerSwapPhase = "None"
    /\ nextCompositionId = 2
    /\ reservedCompositionId = 0
    /\ discardedCandidateId = 0
    /\ stagedRoots = EmptyComposition
    /\ planExpectedComposition = 0
    /\ planRetainedGeneration = 0
    /\ planMalformed = FALSE
    /\ cancelled = FALSE
    /\ deadlineExpired = FALSE
    /\ workspaceOpen = TRUE
    /\ budgetSufficient = TRUE
    /\ observedPairs = {}
    /\ entryOutcomes = {}
    /\ existingLease = "Admitted"

-----------------------------------------------------------------------------
(***************************************************************************)
(* Exogenous events. Cancellation and the finite deadline belong to the    *)
(* plan and to every listed receipt, which the owner requires to match.    *)
(***************************************************************************)

Cancel ==
    /\ ~cancelled
    /\ cancelled' = TRUE
    /\ UNCHANGED <<planVars, receiptVars, currentVars, candidateVars,
                   observerVars, opVars, participantVars,
                   deadlineExpired, workspaceOpen, budgetSufficient>>

DeadlineExpires ==
    /\ ~deadlineExpired
    /\ deadlineExpired' = TRUE
    /\ UNCHANGED <<planVars, receiptVars, currentVars, candidateVars,
                   observerVars, opVars, participantVars,
                   cancelled, workspaceOpen, budgetSufficient>>

CloseWorkspace ==
    /\ workspaceOpen
    /\ ~GateHeld
    /\ workspaceOpen' = FALSE
    /\ UNCHANGED <<planVars, receiptVars, currentVars, candidateVars,
                   observerVars, opVars, participantVars,
                   cancelled, deadlineExpired, budgetSufficient>>

BudgetPressure ==
    /\ budgetSufficient
    /\ ~GateHeld
    /\ budgetSufficient' = FALSE
    /\ UNCHANGED <<planVars, receiptVars, currentVars, candidateVars,
                   observerVars, opVars, participantVars,
                   cancelled, deadlineExpired, workspaceOpen>>

(***************************************************************************)
(* Owner-internal replacement of an unrelated current Root. It observes    *)
(* the same composition gate and advances the physical-composition         *)
(* identity, which is what makes a waiting plan stale.                     *)
(***************************************************************************)
OwnerInternalReplacement ==
    /\ ~GateHeld
    /\ workspaceOpen
    /\ currentRoots[RootA] = 1
    /\ currentRoots' = [currentRoots EXCEPT ![RootA] = MaxGeneration]
    /\ currentCompositionId' = nextCompositionId
    /\ nextCompositionId' = nextCompositionId + 1
    /\ UNCHANGED <<planVars, receiptVars, envVars, observerVars, opVars,
                   participantVars, currentScopeBase,
                   publicationScopeSwapCount, pointerSwapPhase,
                   reservedCompositionId, discardedCandidateId, stagedRoots>>

(***************************************************************************)
(* An independent retain-only Scope publication supersedes a waiting      *)
(* plan. Even with an equal physical set, publication advances both the    *)
(* Scope base and the owner-issued composition epoch.                     *)
(***************************************************************************)
ScopeSupersession ==
    /\ ~GateHeld
    /\ workspaceOpen
    /\ currentScopeBase < MaxScopeSupersessionBase
    /\ currentScopeBase' = currentScopeBase + 1
    /\ currentCompositionId' = nextCompositionId
    /\ nextCompositionId' = nextCompositionId + 1
    /\ UNCHANGED <<planVars, receiptVars, envVars,
                   observerVars, opVars, participantVars,
                   publicationScopeSwapCount, currentRoots, pointerSwapPhase,
                   reservedCompositionId, discardedCandidateId, stagedRoots>>

-----------------------------------------------------------------------------
(***************************************************************************)
(* Gate-observing readers: a Scope/composition read and new query entry.   *)
(***************************************************************************)

ObserveCurrentPair ==
    /\ EnableObservers
    /\ ~GateHeld
    /\ LET seen == CASE pointerSwapPhase = "None"     -> "Old"
                     [] pointerSwapPhase = "Complete" -> "New"
                     [] OTHER                         -> "Half"
       IN /\ seen \notin observedPairs
          /\ observedPairs' = observedPairs \cup {seen}
    /\ UNCHANGED <<planVars, receiptVars, currentVars, candidateVars, envVars,
                   opVars, participantVars, entryOutcomes, existingLease>>

\* A resource-free generation reference a consumer retained from the initial
\* composition epoch. Currentness must be established by comparison with the
\* owner's current composition, never by holding such a reference.
StaleRetainedReference(r, g) == r \in {RootA, RootB} /\ g = 1

AttemptQueryEntry ==
    /\ EnableObservers
    /\ ~GateHeld
    /\ workspaceOpen
    /\ \E r \in Roots, g \in 1..MaxGeneration :
        LET isCurrent == currentRoots[r] = g
            admits    == \/ isCurrent
                         \/ (AllowRetiredRootEntry /\ StaleRetainedReference(r, g))
            outcome   == IF ~admits THEN "Rejected"
                         ELSE IF isCurrent THEN "AdmittedCurrent"
                         ELSE "AdmittedRetired"
        IN /\ outcome \notin entryOutcomes
           /\ entryOutcomes' = entryOutcomes \cup {outcome}
    /\ UNCHANGED <<planVars, receiptVars, currentVars, candidateVars, envVars,
                   opVars, participantVars, observedPairs, existingLease>>

\* Work that entered an old generation before publication keeps its ordinary
\* lease and drains under the existing generation-access contract. The
\* mutation makes retirement cut that lease instead: once the plan retires
\* RootB, the already admitted lease can no longer complete.
DrainLease ==
    /\ EnableLeaseDrain
    /\ existingLease = "Admitted"
    /\ ~(AllowRetirementToCutLease /\ currentRoots[RootB] = AbsentGeneration)
    /\ existingLease' = "Drained"
    /\ UNCHANGED <<planVars, receiptVars, currentVars, candidateVars, envVars,
                   opVars, participantVars, observedPairs, entryOutcomes>>

-----------------------------------------------------------------------------
(***************************************************************************)
(* ReleaseArtifactRootPreparation. Idempotent for Prepared/Released;       *)
(* typed non-release for Publishing and Published.                         *)
(***************************************************************************)

ExplicitRelease(p) ==
    /\ LET drainsPublishing ==
               AllowReleaseDuringPublishing /\ receiptState[p] = "Publishing"
           newState ==
               IF receiptState[p] = "Prepared" \/ drainsPublishing
               THEN "Released"
               ELSE receiptState[p]
           newResult ==
               CASE receiptState[p] = "Prepared"   -> "Released"
                 [] receiptState[p] = "Released"   -> "NoEffect"
                 [] receiptState[p] = "Published"  -> "PreparationAlreadyPublished"
                 [] drainsPublishing               -> "Released"
                 [] OTHER                          -> "PreparationPublishing"
       IN /\ \/ newState # receiptState[p]
             \/ newResult # releaseResult[p]
          /\ receiptState' = [receiptState EXCEPT ![p] = newState]
          /\ callerReleased' =
                 [callerReleased EXCEPT
                     ![p] = callerReleased[p] \/ (newState # receiptState[p])]
          /\ releaseResult' = [releaseResult EXCEPT ![p] = newResult]
    /\ UNCHANGED <<planVars, currentVars, candidateVars, envVars, observerVars,
                   opVars, participantVars, adoptionCount, releasedByOperation>>

CallerRelease ==
    /\ EnableCallerRelease
    /\ \E p \in Preparations : ExplicitRelease(p)

(***************************************************************************)
(* The owner observes the finite deadline and releases an abandoned        *)
(* Prepared receipt even when its caller never submits a publication.      *)
(***************************************************************************)
OwnerReleasesExpiredPreparation(p) ==
    /\ ~AllowOmittedDeadlineSettlement
    /\ deadlineExpired
    /\ receiptState[p] = "Prepared"
    /\ receiptState' = [receiptState EXCEPT ![p] = "Released"]
    /\ UNCHANGED <<planVars, currentVars, candidateVars, envVars, observerVars,
                   opVars, participantVars, adoptionCount, callerReleased,
                   releaseResult, releasedByOperation>>

OwnerDeadlineSettlement ==
    \E p \in Preparations : OwnerReleasesExpiredPreparation(p)

-----------------------------------------------------------------------------
(***************************************************************************)
(* PublishArtifactRootComposition, step by step.                           *)
(***************************************************************************)

\* The caller constructs a plan and a fresh sealed participant. The
\* participant records the Scope publication base current at construction.
\* The plan may carry a stale expected composition identity or a stale
\* retained generation reference retained from an earlier epoch.
SubmitPlan ==
    /\ opPhase = "Unsubmitted"
    /\ \E malformed \in BOOLEAN,
          expected \in {1, currentCompositionId},
          retained \in {1, currentRoots[RootA]} :
        /\ planMalformed' = malformed
        /\ planExpectedComposition' = expected
        /\ planRetainedGeneration' = retained
    /\ participantExpectedBase' = currentScopeBase
    /\ opPhase' = "Submitted"
    /\ UNCHANGED <<receiptVars, currentVars, candidateVars, envVars,
                   observerVars, opResult, gateRefusalReason,
                   gateRefusalExpected, retryResult, participantState>>

\* Step 1. Shape validation happens before any listed receipt is consumed.
ValidateShape ==
    /\ opPhase = "Submitted"
    /\ IF planMalformed
       THEN /\ opPhase' = "Rejected"
            /\ opResult' = "Malformed"
            /\ IF AllowConsumptionBeforeShapeValidation
               THEN /\ receiptState' = ReleaseAllStillPrepared
                    /\ releasedByOperation' = ListedPrepared
               ELSE UNCHANGED <<receiptState, releasedByOperation>>
       ELSE /\ opPhase' = "ShapeOk"
            /\ UNCHANGED <<opResult, receiptState, releasedByOperation>>
    /\ UNCHANGED <<planVars, currentVars, candidateVars, envVars, observerVars,
                   participantVars, gateRefusalReason, gateRefusalExpected,
                   retryResult, adoptionCount, callerReleased, releaseResult>>

\* Steps 2 and 3. Enter the composition gate, revalidate applicability in the
\* owner's exact order, then move every listed receipt to Publishing, stage
\* the complete new physical composition, and reserve one fresh unpublished
\* candidate composition identity.
GateRefuse ==
    /\ FirstApplicableRefusal # "None"
    /\ \E reason \in (IF EnforceCheckPrecedence
                      THEN {FirstApplicableRefusal}
                      ELSE ApplicableRefusalSet) :
        /\ gateRefusalReason' = reason
        /\ opResult' = reason
    /\ gateRefusalExpected' = FirstApplicableRefusal
    /\ opPhase' = "Rejected"
    /\ receiptState' = ReleaseAllStillPrepared
    /\ releasedByOperation' = ListedPrepared
    /\ UNCHANGED <<planVars, currentVars, candidateVars, envVars, observerVars,
                   participantVars, retryResult, adoptionCount, callerReleased,
                   releaseResult>>

GateStage ==
    /\ FirstApplicableRefusal = "None"
    /\ opPhase' = "Staged"
    /\ receiptState' = [p \in Preparations |-> "Publishing"]
    /\ stagedRoots' = DesiredComposition(currentRoots[RootA])
    /\ reservedCompositionId' = nextCompositionId
    /\ nextCompositionId' = nextCompositionId + 1
    /\ UNCHANGED <<planVars, currentVars, envVars, observerVars,
                   participantVars, opResult, gateRefusalReason,
                   gateRefusalExpected, retryResult, adoptionCount,
                   callerReleased, releaseResult, releasedByOperation,
                   discardedCandidateId>>

EnterGateAndRevalidate ==
    /\ opPhase = "ShapeOk"
    /\ \/ GateRefuse
       \/ GateStage

\* Step 4. The sealed participant prepares its commit from the exact current
\* composition plus the reserved candidate identity. Refusal releases all
\* staging, permanently discards the candidate identity, releases every
\* listed receipt, and preserves both current states.
ParticipantRefusalOutcome ==
    IF participantState # "Available" THEN "ParticipantAlreadyConsumed"
    ELSE IF participantExpectedBase # currentScopeBase THEN "ParticipantRefused"
    ELSE IF cancelled THEN "Cancelled"
    ELSE IF deadlineExpired THEN "DeadlineExpired"
    ELSE "None"

ParticipantRefuses(reason) ==
    /\ participantState' = "Refused"
    /\ opResult' = reason
    /\ IF AllowRefusedParticipantPublish
       THEN \* Mutation: publish anyway after a product-level refusal.
            /\ opPhase' = "Committed"
            /\ pointerSwapPhase' = "Complete"
            /\ currentRoots' = stagedRoots
            /\ currentCompositionId' = reservedCompositionId
            /\ currentScopeBase' = currentScopeBase + 1
            /\ publicationScopeSwapCount' = publicationScopeSwapCount + 1
            /\ receiptState' = [p \in Preparations |-> "Published"]
            /\ adoptionCount' =
                   [p \in Preparations |-> adoptionCount[p] + 1]
            /\ releasedByOperation' = releasedByOperation
            /\ discardedCandidateId' = discardedCandidateId
       ELSE /\ opPhase' = "Rejected"
            /\ UNCHANGED <<currentVars>>
            /\ receiptState' = ReleaseAllListed
            /\ adoptionCount' = adoptionCount
            /\ releasedByOperation' = Preparations
            /\ discardedCandidateId' = reservedCompositionId
    /\ stagedRoots' = EmptyComposition
    /\ reservedCompositionId' = 0
    /\ UNCHANGED <<planVars, envVars, observerVars, gateRefusalReason,
                   gateRefusalExpected, retryResult, callerReleased,
                   releaseResult, nextCompositionId, participantExpectedBase>>

ParticipantIssuesToken ==
    /\ ParticipantRefusalOutcome = "None"
    /\ participantState' = "TokenIssued"
    /\ opPhase' = "TokenIssued"
    /\ UNCHANGED <<planVars, receiptVars, currentVars, candidateVars, envVars,
                   observerVars, opResult, gateRefusalReason,
                   gateRefusalExpected, retryResult, participantExpectedBase>>

ParticipantPrepareCommit ==
    /\ opPhase = "Staged"
    /\ \/ /\ ParticipantRefusalOutcome # "None"
          /\ ParticipantRefuses(ParticipantRefusalOutcome)
       \/ /\ ParticipantRefusalOutcome = "None"
          /\ \/ ParticipantRefuses("ParticipantRefused")
             \/ ParticipantIssuesToken

\* Step 5. Final recheck, then the no-fail commit token performs the
\* preconstructed pointer swap inside one non-yielding critical region.
FinalRecheckRefusalOutcome ==
    IF cancelled THEN "Cancelled"
    ELSE IF deadlineExpired THEN "DeadlineExpired"
    ELSE IF planExpectedComposition # currentCompositionId
         THEN "CompositionMismatch"
    ELSE IF planRetainedGeneration # currentRoots[RootA]
         THEN "GenerationMismatch"
    ELSE "None"

FinalRecheckRefuses ==
    /\ FinalRecheckRefusalOutcome # "None"
    /\ opPhase' = "Rejected"
    /\ opResult' = FinalRecheckRefusalOutcome
    /\ receiptState' = ReleaseAllListed
    /\ releasedByOperation' = Preparations
    /\ stagedRoots' = EmptyComposition
    /\ discardedCandidateId' = reservedCompositionId
    /\ reservedCompositionId' = 0
    /\ UNCHANGED <<planVars, currentVars, envVars, observerVars,
                   participantVars, gateRefusalReason, gateRefusalExpected,
                   retryResult, adoptionCount, callerReleased, releaseResult,
                   nextCompositionId>>

CommitBothPointers ==
    /\ FinalRecheckRefusalOutcome = "None"
    /\ ~AllowYieldingCommit
    /\ opPhase' = "Committed"
    /\ opResult' = "Published"
    /\ pointerSwapPhase' = "Complete"
    /\ currentRoots' = stagedRoots
    \* Commit publishes the exact reserved candidate identity the
    \* participant already saw. The mutation synthesizes a different fresh
    \* identity at commit instead, so the value the participant built its
    \* snapshot against never becomes current.
    /\ IF AllowSynthesizedCompositionIdentity
       THEN /\ currentCompositionId' = nextCompositionId
            /\ nextCompositionId' = nextCompositionId + 1
       ELSE /\ currentCompositionId' = reservedCompositionId
            /\ UNCHANGED nextCompositionId
    /\ currentScopeBase' = currentScopeBase + 1
    /\ publicationScopeSwapCount' = publicationScopeSwapCount + 1
    /\ participantState' = "Committed"
    /\ receiptState' = [p \in Preparations |-> "Published"]
    /\ adoptionCount' = [p \in Preparations |-> adoptionCount[p] + 1]
    /\ stagedRoots' = EmptyComposition
    /\ UNCHANGED <<planVars, envVars, observerVars, gateRefusalReason,
                   gateRefusalExpected, retryResult, callerReleased,
                   releaseResult, releasedByOperation,
                   discardedCandidateId, participantExpectedBase,
                   reservedCompositionId>>

\* Mutation: the commit region yields between the physical and the logical
\* pointer assignment, releasing the composition gate in between.
CommitPhysicalOnly ==
    /\ FinalRecheckRefusalOutcome = "None"
    /\ AllowYieldingCommit
    /\ opPhase' = "CommitYield"
    /\ pointerSwapPhase' = "PhysicalOnly"
    /\ currentRoots' = stagedRoots
    /\ currentCompositionId' = reservedCompositionId
    /\ UNCHANGED <<planVars, receiptVars, envVars, observerVars,
                   participantVars, opResult, gateRefusalReason,
                   gateRefusalExpected, retryResult, currentScopeBase,
                   publicationScopeSwapCount, candidateVars>>

CommitLogicalAfterYield ==
    /\ opPhase = "CommitYield"
    /\ opPhase' = "Committed"
    /\ opResult' = "Published"
    /\ pointerSwapPhase' = "Complete"
    /\ currentScopeBase' = currentScopeBase + 1
    /\ publicationScopeSwapCount' = publicationScopeSwapCount + 1
    /\ participantState' = "Committed"
    /\ receiptState' = [p \in Preparations |-> "Published"]
    /\ adoptionCount' = [p \in Preparations |-> adoptionCount[p] + 1]
    /\ stagedRoots' = EmptyComposition
    /\ UNCHANGED <<planVars, envVars, observerVars, gateRefusalReason,
                   gateRefusalExpected, retryResult, callerReleased,
                   releaseResult, releasedByOperation, nextCompositionId,
                   discardedCandidateId, participantExpectedBase,
                   reservedCompositionId, currentCompositionId, currentRoots>>

FinalRecheckAndCommit ==
    /\ opPhase = "TokenIssued"
    /\ \/ FinalRecheckRefuses
       \/ CommitBothPointers
       \/ CommitPhysicalOnly

-----------------------------------------------------------------------------
(***************************************************************************)
(* The second publication attempt. A receipt-bearing retry meets the       *)
(* receipt-state precedence; a receipt-free retry meets the single-use     *)
(* participant, and an equivalent participant meets its stale Scope        *)
(* publication base. Neither may repeat adoption or logical publication.   *)
(***************************************************************************)
SecondAttempt ==
    /\ EnableRetry
    /\ opPhase \in {"Committed", "Rejected"}
    /\ retryResult = "None"
    /\ \E shape \in {"ReuseReceipt", "ReceiptFree"},
          participant \in {"Same", "Equivalent"} :
        LET receiptRefusal ==
                IF shape = "ReuseReceipt"
                THEN ReceiptStateRefusal
                ELSE "None"
            \* "Same" is the original single-use participant; "Equivalent" is
            \* a separately constructed participant that still carries the
            \* same expected Scope publication base.
            participantRefusal ==
                IF participant = "Same" /\ participantState # "Available"
                THEN "ParticipantAlreadyConsumed"
                ELSE IF participantExpectedBase # currentScopeBase
                     THEN "ParticipantRefused"
                     ELSE "None"
            reuseReceipts ==
                /\ AllowReceiptReuse
                /\ shape = "ReuseReceipt"
                /\ receiptState[PreparationOne] = "Published"
            reuseParticipant == AllowParticipantReuse
        IN /\ \/ receiptRefusal # "None"
              \/ participantRefusal # "None"
              \/ reuseParticipant
           /\ retryResult' =
                  IF reuseReceipts \/ reuseParticipant THEN "Published"
                  ELSE IF receiptRefusal # "None" THEN receiptRefusal
                  ELSE participantRefusal
           /\ adoptionCount' =
                  IF reuseReceipts
                  THEN [adoptionCount EXCEPT ![PreparationOne] = @ + 1]
                  ELSE adoptionCount
           /\ publicationScopeSwapCount' =
                  IF reuseParticipant
                  THEN publicationScopeSwapCount + 1
                  ELSE publicationScopeSwapCount
           /\ currentScopeBase' =
                  IF reuseParticipant
                  THEN currentScopeBase + 1
                  ELSE currentScopeBase
           /\ receiptState' =
                  IF shape = "ReuseReceipt"
                  THEN ReleaseAllStillPrepared
                  ELSE receiptState
    /\ UNCHANGED <<planVars, callerReleased, releaseResult,
                   releasedByOperation, currentCompositionId, currentRoots,
                   pointerSwapPhase, candidateVars, envVars, observerVars,
                   opPhase, opResult, gateRefusalReason, gateRefusalExpected,
                   participantVars>>

-----------------------------------------------------------------------------
Next ==
    \/ Cancel
    \/ DeadlineExpires
    \/ CloseWorkspace
    \/ BudgetPressure
    \/ OwnerInternalReplacement
    \/ ScopeSupersession
    \/ ObserveCurrentPair
    \/ AttemptQueryEntry
    \/ DrainLease
    \/ CallerRelease
    \/ OwnerDeadlineSettlement
    \/ SubmitPlan
    \/ ValidateShape
    \/ EnterGateAndRevalidate
    \/ ParticipantPrepareCommit
    \/ FinalRecheckAndCommit
    \/ CommitLogicalAfterYield
    \/ SecondAttempt

Fairness ==
    /\ WF_vars(DeadlineExpires)
    /\ WF_vars(OwnerDeadlineSettlement)
    /\ WF_vars(DrainLease)
    /\ WF_vars(ValidateShape)
    /\ WF_vars(EnterGateAndRevalidate)
    /\ WF_vars(ParticipantPrepareCommit)
    /\ WF_vars(FinalRecheckAndCommit)
    /\ WF_vars(CommitLogicalAfterYield)

SafetySpec == Init /\ [][Next]_vars
Spec == Init /\ [][Next]_vars /\ Fairness

-----------------------------------------------------------------------------
(***************************************************************************)
(* Invariants.                                                             *)
(***************************************************************************)

TypeOK ==
    /\ opPhase \in OperationPhases
    /\ opResult \in OperationResults
    /\ gateRefusalReason \in RefusalReasons
    /\ gateRefusalExpected \in RefusalReasons
    /\ retryResult \in OperationResults
    /\ receiptState \in [Preparations -> ReceiptStates]
    /\ adoptionCount \in [Preparations -> 0..2]
    /\ callerReleased \in [Preparations -> BOOLEAN]
    /\ releaseResult \in [Preparations -> ReleaseResults]
    /\ releasedByOperation \subseteq Preparations
    /\ participantState \in ParticipantStates
    /\ participantExpectedBase \in ScopeBases
    /\ currentScopeBase \in ScopeBases
    /\ publicationScopeSwapCount \in 0..2
    /\ currentCompositionId \in CompositionIds
    /\ currentRoots \in [Roots -> Generations]
    /\ pointerSwapPhase \in PointerSwapPhases
    /\ nextCompositionId \in CompositionIds
    /\ reservedCompositionId \in CompositionIds
    /\ discardedCandidateId \in CompositionIds
    /\ stagedRoots \in [Roots -> Generations]
    /\ planExpectedComposition \in CompositionIds
    /\ planRetainedGeneration \in Generations
    /\ planMalformed \in BOOLEAN
    /\ cancelled \in BOOLEAN
    /\ deadlineExpired \in BOOLEAN
    /\ workspaceOpen \in BOOLEAN
    /\ budgetSufficient \in BOOLEAN
    /\ observedPairs \subseteq ObservedPairKinds
    /\ entryOutcomes \subseteq EntryOutcomeKinds
    /\ existingLease \in {"Admitted", "Drained"}

\* Shape validation precedes receipt consumption: a malformed plan leaves
\* every matching Prepared receipt under caller ownership.
MalformedPlanReleasesNoReceipt ==
    (opResult = "Malformed") => releasedByOperation = {}

\* The gate reports the first applicable check in the owner's exact order.
GateRefusalIsFirstApplicable ==
    gateRefusalReason = gateRefusalExpected

\* Any refusal once applicability validation starts releases every listed
\* still-Prepared batch.
ApplicabilityRefusalReleasesEveryListedBatch ==
    (opResult \in ApplicabilityRefusalReasons) =>
        \A p \in Preparations : receiptState[p] = "Released"

\* A typed participant refusal after staging publishes nothing, releases
\* every provisional resource, and permanently discards the candidate id.
RefusedParticipantPublishesNothing ==
    (participantState = "Refused") =>
        /\ pointerSwapPhase = "None"
        /\ publicationScopeSwapCount = 0
        /\ \A p \in Preparations :
               /\ adoptionCount[p] = 0
               /\ receiptState[p] = "Released"
        /\ stagedRoots = EmptyComposition
        /\ reservedCompositionId = 0
        /\ discardedCandidateId # 0

\* Scope reads and query entries observe either the complete old pair or the
\* complete new pair, never a half-state. This is the state-predicate form:
\* the two internal pointer assignments happen inside one non-yielding
\* region that holds the composition gate, so no gate-observing action can
\* interleave while only the physical pointer has moved. It does not depend
\* on an observer actually running, so every configuration can check it.
HalfStateIsNeverGateVisible ==
    ~GateHeld => pointerSwapPhase # "PhysicalOnly"

\* The witnessed form of the same claim: a gate-observing reader that runs
\* in every reachable non-gated state never records a half-state. It is
\* entailed by HalfStateIsNeverGateVisible and is kept as a focused
\* diagnostic in the observer configuration, not as independent evidence.
OldOrNewCompositionIsObserved == "Half" \notin observedPairs

\* Retirement stops new query entry; a staged Root is not query-admissible.
NoQueryEntersRetiredRoot == "AdmittedRetired" \notin entryOutcomes

\* A caller release racing a Publishing receipt never drains the staged
\* batch that publication alone owns.
PublishedReceiptWasNotCallerReleased ==
    \A p \in Preparations :
        (receiptState[p] = "Published") => ~callerReleased[p]

\* Each listed receipt has one terminal outcome and one adoption at most.
ReceiptPublishesAtMostOnce ==
    \A p \in Preparations : adoptionCount[p] <= 1

ReceiptOutcomeIsExactlyOneTerminal ==
    \A p \in Preparations :
        /\ (receiptState[p] = "Published") => adoptionCount[p] = 1
        /\ (receiptState[p] = "Released")  => adoptionCount[p] = 0

\* A receipt-free plan's single-use participant and non-reused Scope base
\* prevent a repeated logical publication, including through ABA.
LogicalPublicationIsSingleUse == publicationScopeSwapCount <= 1

\* Cancellation or deadline expiry before the final recheck never leaves a
\* swapped pointer behind.
CancellationNeverLeavesASwappedPointer ==
    (opResult \in {"Cancelled", "DeadlineExpired"}) =>
        pointerSwapPhase = "None"

\* A reserved candidate composition identity that does not commit is never
\* reused and never becomes current.
DiscardedCandidateIdentityNeverBecomesCurrent ==
    (discardedCandidateId # 0) =>
        /\ currentCompositionId # discardedCandidateId
        /\ reservedCompositionId # discardedCandidateId

\* Successful publication reports Published only with both pointers swapped.
\* The owner may retire or replace a Root afterwards, so this state
\* predicate does not constrain the later current composition; the exact
\* published composition is pinned by the action property below.
PublishedResultImpliesCompleteSwap ==
    (opResult = "Published") => pointerSwapPhase = "Complete"

-----------------------------------------------------------------------------
(***************************************************************************)
(* Action properties. The commit step is the only transition that turns a  *)
(* reserved candidate composition into the current one, so the exact       *)
(* published pair is stated over that transition rather than over every    *)
(* later state: the owner may retire or replace a Root afterwards, which   *)
(* legitimately advances the current composition again.                    *)
(***************************************************************************)

\* Successful publication makes the exact reserved candidate identity
\* current together with the complete desired physical set -- RootA
\* retained at the plan's retained generation reference, RootC and RootD
\* adopted, RootB retired -- swaps both pointers in that one step, and
\* leaves no staging behind.
CommitPublishesExactlyTheReservedComposition ==
    [][ (opResult # "Published" /\ opResult' = "Published") =>
            /\ pointerSwapPhase' = "Complete"
            /\ currentCompositionId' = reservedCompositionId
            /\ currentCompositionId' # discardedCandidateId'
            /\ currentRoots'[RootA] = planRetainedGeneration'
            /\ currentRoots'[RootB] = AbsentGeneration
            /\ currentRoots'[RootC] = MaxGeneration
            /\ currentRoots'[RootD] = MaxGeneration
            /\ stagedRoots' = EmptyComposition ]_vars

\* The current physical composition identity changes only together with a
\* fresh, never-reused value: no physical change reuses or reverts to an
\* identity that was already current.
CompositionIdentityAdvancesOnEveryPhysicalChange ==
    [][ (currentRoots' # currentRoots) =>
            /\ currentCompositionId' # currentCompositionId
            /\ currentCompositionId' > currentCompositionId ]_vars

-----------------------------------------------------------------------------
(***************************************************************************)
(* Liveness.                                                               *)
(***************************************************************************)

EveryPreparationEventuallySettles ==
    \A p \in Preparations : <>(receiptState[p] \in TerminalReceiptStates)

SubmittedPublicationEventuallySettles ==
    (opPhase = "Submitted") ~> (opPhase \in {"Committed", "Rejected"})

AdmittedLeaseEventuallyDrains == <>(existingLease = "Drained")

\* The design's exact claim for a Root omitted from a committed desired set:
\* new query entry stops, but a lease admitted before publication still
\* drains under the existing generation-access contract.
RetiredRootLeaseEventuallyDrains ==
    (pointerSwapPhase = "Complete" /\ currentRoots[RootB] = AbsentGeneration)
        ~> (existingLease = "Drained")

-----------------------------------------------------------------------------
(***************************************************************************)
(* Reachability probes. Each is expected to FAIL in its own configuration; *)
(* the counterexample is the evidence that the modeled interleaving is     *)
(* actually explored rather than vacuously excluded.                       *)
(***************************************************************************)

NoPublishedCompositionObserved == opResult # "Published"

NoPublishingReleaseRaceObserved ==
    \A p \in Preparations : releaseResult[p] # "PreparationPublishing"

NoRetiredLeaseDrainObserved ==
    ~( /\ pointerSwapPhase = "Complete"
       /\ currentRoots[RootB] = AbsentGeneration
       /\ existingLease = "Drained" )

NoBothPairsObserved == ~({"Old", "New"} \subseteq observedPairs)

=============================================================================
