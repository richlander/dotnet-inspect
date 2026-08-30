------------------------- MODULE BindingNameOwnership -------------------------
EXTENDS FiniteSets, Integers, Sequences, TLC

\* Owned by docs/design/type-forwarding-resolution.md.
\* Policy owners have already issued one result for each tier under one stable
\* version. The model owns only miss fallthrough and frozen preservation.

CONSTANTS
    TierOne,
    TierTwo,
    NoNameOwner,
    NameOwnedNoMatch,
    Undifferentiated,
    Selected,
    Ambiguous,
    Unavailable,
    Rejected,
    NoResult,
    AssemblyReference,
    IntrinsicCoreLibrary,
    CompositionMode,
    TargetValidationMode,
    ChainMode,
    FreezeMode

Tiers == <<TierOne, TierTwo>>
TierSet == {TierOne, TierTwo}
Misses == {NoNameOwner, NameOwnedNoMatch, Undifferentiated}
TerminalResults == {Selected, Ambiguous, Unavailable, Rejected}
PolicyResults == Misses \union TerminalResults
Targets == {AssemblyReference, IntrinsicCoreLibrary}
Phases == {"Selecting", "Selected", "Frozen"}

ASSUME
    /\ Cardinality(TierSet) = 2
    /\ Cardinality(PolicyResults) = 7
    /\ Cardinality(Targets) = 2
    /\ NoResult \notin PolicyResults
    /\ CompositionMode \in
        {"Policy", "FallThroughOwned", "FallThroughLegacy",
         "PrematureNoOwner"}
    /\ TargetValidationMode \in {"Policy", "AfterComposition"}
    /\ ChainMode \in {"Complete", "OmitEligibleTier"}
    /\ FreezeMode \in {"Policy", "CollapseMisses"}

CanContinue(result) ==
    CASE CompositionMode = "Policy" ->
            result = NoNameOwner
      [] CompositionMode = "FallThroughOwned" ->
            result \in {NoNameOwner, NameOwnedNoMatch}
      [] CompositionMode = "FallThroughLegacy" ->
            result \in {NoNameOwner, Undifferentiated}
      [] OTHER -> FALSE

InvalidMissing(result, requestTarget) ==
    /\ requestTarget = IntrinsicCoreLibrary
    /\ result \in Misses

ValidateMissingNow(result, requestTarget) ==
    /\ TargetValidationMode = "Policy"
    /\ InvalidMissing(result, requestTarget)

FrozenResult(result) ==
    IF /\ FreezeMode = "CollapseMisses"
       /\ result \in Misses
    THEN Undifferentiated
    ELSE result

VARIABLES
    phase,
    issued,
    target,
    requestEligibleTierCount,
    configuredTierCount,
    current,
    evaluated,
    selection,
    frozen

vars ==
    <<phase, issued, target, requestEligibleTierCount, configuredTierCount,
      current, evaluated, selection, frozen>>

Init ==
    /\ phase = "Selecting"
    /\ issued \in [TierSet -> PolicyResults]
    /\ target \in Targets
    /\ requestEligibleTierCount \in 1..Len(Tiers)
    /\ configuredTierCount \in 1..requestEligibleTierCount
    /\ (ChainMode = "Complete" =>
        configuredTierCount = requestEligibleTierCount)
    /\ current = 1
    /\ evaluated = <<>>
    /\ selection = NoResult
    /\ frozen = NoResult

Evaluate ==
    LET tier == Tiers[current]
        result == issued[tier]
    IN
        /\ phase = "Selecting"
        /\ evaluated' = Append(evaluated, tier)
        /\ IF ValidateMissingNow(result, target)
           THEN
                /\ phase' = "Selected"
                /\ current' = current
                /\ selection' = Rejected
           ELSE
                IF /\ CanContinue(result)
                   /\ current < configuredTierCount
                THEN
                    /\ current' = current + 1
                    /\ UNCHANGED <<phase, selection>>
                ELSE
                    /\ phase' = "Selected"
                    /\ current' = current
                    /\ selection' = result
        /\ UNCHANGED
            <<issued, target, requestEligibleTierCount,
              configuredTierCount, frozen>>

Freeze ==
    /\ phase = "Selected"
    /\ phase' = "Frozen"
    /\ frozen' = FrozenResult(selection)
    /\ UNCHANGED
        <<issued, target, requestEligibleTierCount, configuredTierCount,
          current, evaluated, selection>>

Next == Evaluate \/ Freeze

Spec ==
    /\ Init
    /\ [][Next]_vars
    /\ WF_vars(Evaluate)
    /\ WF_vars(Freeze)

TypeOK ==
    /\ phase \in Phases
    /\ issued \in [TierSet -> PolicyResults]
    /\ target \in Targets
    /\ requestEligibleTierCount \in 1..Len(Tiers)
    /\ configuredTierCount \in 1..requestEligibleTierCount
    /\ current \in 1..Len(Tiers)
    /\ evaluated \in Seq(TierSet)
    /\ Len(evaluated) <= Len(Tiers)
    /\ selection \in PolicyResults \union {NoResult}
    /\ frozen \in PolicyResults \union {NoResult}

OnlyNoNameOwnerReachesLowerTier ==
    Len(evaluated) = 2 => issued[TierOne] = NoNameOwner

IntrinsicMissingRejectedBeforeContinuation ==
    /\ target = IntrinsicCoreLibrary
    /\ issued[TierOne] \in Misses
    =>
        /\ Len(evaluated) <= 1
        /\ phase # "Selecting" => selection = Rejected

TargetValidationPreventsHiddenSelection ==
    /\ target = IntrinsicCoreLibrary
    /\ requestEligibleTierCount = 2
    /\ issued[TierOne] = NoNameOwner
    /\ issued[TierTwo] = Selected
    /\ phase # "Selecting"
    =>
        /\ Len(evaluated) = 1
        /\ selection = Rejected

CompositeNoNameOwnerRequiresCompleteExhaustion ==
    /\ phase \in {"Selected", "Frozen"}
    /\ selection = NoNameOwner
    => Len(evaluated) = requestEligibleTierCount

NameOwnedNoMatchStops ==
    issued[TierOne] = NameOwnedNoMatch => Len(evaluated) <= 1

UndifferentiatedStops ==
    issued[TierOne] = Undifferentiated => Len(evaluated) <= 1

AllNoNameOwnerRemainsNoNameOwner ==
    /\ phase \in {"Selected", "Frozen"}
    /\ target = AssemblyReference
    /\ \A index \in 1..requestEligibleTierCount:
        issued[Tiers[index]] = NoNameOwner
    => selection = NoNameOwner

TerminalFirstTierStops ==
    issued[TierOne] \in TerminalResults => Len(evaluated) <= 1

FrozenDispositionPreserved ==
    phase = "Frozen" => frozen = selection

SelectionConverges == <>(phase = "Frozen")

=============================================================================
