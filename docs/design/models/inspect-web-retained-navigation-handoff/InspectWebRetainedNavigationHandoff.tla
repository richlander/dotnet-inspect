---------------- MODULE InspectWebRetainedNavigationHandoff ----------------
EXTENDS Naturals, TLC

\* This model composes retained-realization cutover with the initial
\* Navigation-result handoff. Navigation's later focus, announcement, history,
\* and synchronization lifecycle remains owned by UiEffectLifecycle.

CONSTANTS
    EnforceCurrentPosting,
    EnforcePostingBeforeAcknowledge,
    EnforcePredecessorRetirement

ASSUME EnforceCurrentPosting \in BOOLEAN
ASSUME EnforcePostingBeforeAcknowledge \in BOOLEAN
ASSUME EnforcePredecessorRetirement \in BOOLEAN

Realizations == 1..2
NoRealization == 0
ResultStatuses ==
    {"unused", "issued", "delivered", "posted", "recorded",
     "acknowledged", "abandoned"}
SlotStatuses == {"unused", "current", "retired"}

VARIABLES
    activeRealization,
    activeOrdinal,
    nextOrdinal,
    publicationOrdinal,
    authority,
    resultStatus,
    slotStatus,
    presentedRealization,
    presentedOrdinal,
    stalePostingWitness,
    earlyAcknowledgeWitness,
    missingRetirementWitness,
    outOfOrderWitness

vars == <<
    activeRealization,
    activeOrdinal,
    nextOrdinal,
    publicationOrdinal,
    authority,
    resultStatus,
    slotStatus,
    presentedRealization,
    presentedOrdinal,
    stalePostingWitness,
    earlyAcknowledgeWitness,
    missingRetirementWitness,
    outOfOrderWitness
>>

Init ==
    /\ activeRealization = NoRealization
    /\ activeOrdinal = 0
    /\ nextOrdinal = 0
    /\ publicationOrdinal = [r \in Realizations |-> 0]
    /\ authority = [r \in Realizations |-> 0]
    /\ resultStatus = [r \in Realizations |-> "unused"]
    /\ slotStatus = [r \in Realizations |-> "unused"]
    /\ presentedRealization = NoRealization
    /\ presentedOrdinal = 0
    /\ stalePostingWitness = FALSE
    /\ earlyAcknowledgeWitness = FALSE
    /\ missingRetirementWitness = FALSE
    /\ outOfOrderWitness = FALSE

CurrentTuple(r) ==
    /\ r = activeRealization
    /\ publicationOrdinal[r] = activeOrdinal
    /\ authority[r] = publicationOrdinal[r]
    /\ slotStatus[r] = "current"

Cutover(r) ==
    /\ r \in Realizations
    /\ resultStatus[r] = "unused"
    /\ nextOrdinal < 2
    /\ LET predecessor == activeRealization
           ordinal == nextOrdinal + 1
       IN
       /\ activeRealization' = r
       /\ activeOrdinal' = ordinal
       /\ nextOrdinal' = ordinal
       /\ publicationOrdinal' =
            [publicationOrdinal EXCEPT ![r] = ordinal]
       /\ authority' = [authority EXCEPT ![r] = ordinal]
       /\ resultStatus' = [resultStatus EXCEPT ![r] = "issued"]
       /\ slotStatus' =
            IF predecessor = NoRealization
            THEN [slotStatus EXCEPT ![r] = "current"]
            ELSE [slotStatus EXCEPT
                    ![r] = "current",
                    ![predecessor] =
                        IF EnforcePredecessorRetirement
                        THEN "retired"
                        ELSE @]
       /\ presentedRealization' = NoRealization
       /\ presentedOrdinal' = 0
       /\ missingRetirementWitness' =
            (missingRetirementWitness
             \/ (predecessor # NoRealization
                 /\ slotStatus'[predecessor] # "retired"))
    /\ UNCHANGED <<
        stalePostingWitness,
        earlyAcknowledgeWitness,
        outOfOrderWitness
       >>

Deliver(r) ==
    /\ r \in Realizations
    /\ resultStatus[r] = "issued"
    /\ resultStatus' = [resultStatus EXCEPT ![r] = "delivered"]
    /\ UNCHANGED <<
        activeRealization,
        activeOrdinal,
        nextOrdinal,
        publicationOrdinal,
        authority,
        slotStatus,
        presentedRealization,
        presentedOrdinal,
        stalePostingWitness,
        earlyAcknowledgeWitness,
        missingRetirementWitness,
        outOfOrderWitness
       >>

Post(r) ==
    /\ r \in Realizations
    /\ resultStatus[r] = "delivered"
    /\ ~EnforceCurrentPosting \/ CurrentTuple(r)
    /\ resultStatus' = [resultStatus EXCEPT ![r] = "posted"]
    /\ presentedRealization' = r
    /\ presentedOrdinal' = publicationOrdinal[r]
    /\ stalePostingWitness' =
        (stalePostingWitness \/ ~CurrentTuple(r))
    /\ UNCHANGED <<
        activeRealization,
        activeOrdinal,
        nextOrdinal,
        publicationOrdinal,
        authority,
        slotStatus,
        earlyAcknowledgeWitness,
        missingRetirementWitness,
        outOfOrderWitness
       >>

RecordPosting(r) ==
    /\ r \in Realizations
    /\ resultStatus[r] = "posted"
    /\ CurrentTuple(r)
    /\ resultStatus' = [resultStatus EXCEPT ![r] = "recorded"]
    /\ UNCHANGED <<
        activeRealization,
        activeOrdinal,
        nextOrdinal,
        publicationOrdinal,
        authority,
        slotStatus,
        presentedRealization,
        presentedOrdinal,
        stalePostingWitness,
        earlyAcknowledgeWitness,
        missingRetirementWitness,
        outOfOrderWitness
       >>

Acknowledge(r) ==
    /\ r \in Realizations
    /\ IF EnforcePostingBeforeAcknowledge
       THEN resultStatus[r] = "recorded"
       ELSE resultStatus[r] \in {"delivered", "posted", "recorded"}
    /\ CurrentTuple(r)
    /\ earlyAcknowledgeWitness' =
        (earlyAcknowledgeWitness \/ resultStatus[r] # "recorded")
    /\ resultStatus' = [resultStatus EXCEPT ![r] = "acknowledged"]
    /\ UNCHANGED <<
        activeRealization,
        activeOrdinal,
        nextOrdinal,
        publicationOrdinal,
        authority,
        slotStatus,
        presentedRealization,
        presentedOrdinal,
        stalePostingWitness,
        missingRetirementWitness,
        outOfOrderWitness
       >>

Abandon(r) ==
    /\ r \in Realizations
    /\ resultStatus[r] \in {"delivered", "posted", "recorded"}
    /\ resultStatus' = [resultStatus EXCEPT ![r] = "abandoned"]
    /\ outOfOrderWitness' =
        (outOfOrderWitness
         \/ (publicationOrdinal[r] < presentedOrdinal
             /\ presentedRealization = activeRealization))
    /\ UNCHANGED <<
        activeRealization,
        activeOrdinal,
        nextOrdinal,
        publicationOrdinal,
        authority,
        slotStatus,
        presentedRealization,
        presentedOrdinal,
        stalePostingWitness,
        earlyAcknowledgeWitness,
        missingRetirementWitness
       >>

Next ==
    \/ \E r \in Realizations : Cutover(r)
    \/ \E r \in Realizations : Deliver(r)
    \/ \E r \in Realizations : Post(r)
    \/ \E r \in Realizations : RecordPosting(r)
    \/ \E r \in Realizations : Acknowledge(r)
    \/ \E r \in Realizations : Abandon(r)

Spec ==
    Init /\ [][Next]_vars

TypeOK ==
    /\ activeRealization \in Realizations \cup {NoRealization}
    /\ activeOrdinal \in 0..2
    /\ nextOrdinal \in 0..2
    /\ publicationOrdinal \in [Realizations -> 0..2]
    /\ authority \in [Realizations -> 0..2]
    /\ resultStatus \in [Realizations -> ResultStatuses]
    /\ slotStatus \in [Realizations -> SlotStatuses]
    /\ presentedRealization \in Realizations \cup {NoRealization}
    /\ presentedOrdinal \in 0..2
    /\ stalePostingWitness \in BOOLEAN
    /\ earlyAcknowledgeWitness \in BOOLEAN
    /\ missingRetirementWitness \in BOOLEAN
    /\ outOfOrderWitness \in BOOLEAN

PresentationIsCurrent ==
    presentedRealization = NoRealization
    \/ /\ presentedRealization = activeRealization
       /\ presentedOrdinal = activeOrdinal
       /\ slotStatus[presentedRealization] = "current"

PredecessorSlotIsRetired ==
    \A r \in Realizations :
        r # activeRealization /\ resultStatus[r] # "unused"
        => slotStatus[r] = "retired"

NoStalePostingObserved == ~stalePostingWitness
NoEarlyAcknowledgeObserved == ~earlyAcknowledgeWitness
NoMissingRetirementObserved == ~missingRetirementWitness
NoOutOfOrderRecoveryObserved == ~outOfOrderWitness

=============================================================================
