------------------------ MODULE BindingSelectionVersion -----------------------
EXTENDS FiniteSets, Integers, TLC

\* Owned by docs/design/type-forwarding-resolution.md.
\* Requests, selection payloads, acquisition, and declarations are abstract.
\* The model owns only answer/version association, version non-reuse, cache
\* reuse, commit validation, and policy-dependent publication.

CONSTANTS
    StateOne,
    StateTwo,
    StateThree,
    VersionOne,
    VersionTwo,
    VersionThree,
    AnswerOne,
    AnswerTwo,
    AnswerThree,
    NoVersion,
    NoAnswer,
    VersionMode,
    AssociationMode,
    FinalValidationMode,
    SideEffectMode

States == {StateOne, StateTwo, StateThree}
Versions == {VersionOne, VersionTwo, VersionThree}
Answers == {AnswerOne, AnswerTwo, AnswerThree}
Operations == {"Cold", "Cache"}
Phases ==
    {"Ready", "Active", "Associating", "Validating", "Committed", "Done"}

ASSUME
    /\ Cardinality(States) = 3
    /\ Cardinality(Versions) = 3
    /\ Cardinality(Answers) = 3
    /\ NoVersion \notin Versions
    /\ NoAnswer \notin Answers
    /\ VersionMode \in {"Unique", "ReuseVersionOne"}
    /\ AssociationMode \in {"Atomic", "ConsumerObserved"}
    /\ FinalValidationMode \in {"Policy", "Skip"}
    /\ SideEffectMode \in {"Policy", "BeforeValidation"}

VersionFor(state) ==
    CASE state = StateOne -> VersionOne
      [] state = StateTwo -> VersionTwo
      [] /\ state = StateThree
         /\ VersionMode = "ReuseVersionOne" -> VersionOne
      [] OTHER -> VersionThree

AnswerFor(state) ==
    CASE state = StateOne -> AnswerOne
      [] state = StateTwo -> AnswerTwo
      [] OTHER -> AnswerThree

NextState(state) ==
    CASE state = StateOne -> StateTwo
      [] state = StateTwo -> StateThree
      [] OTHER -> StateThree

VARIABLES
    phase,
    policyState,
    operation,
    expectedVersion,
    returnedVersion,
    returnedAnswer,
    commitVersion,
    commitState,
    committed,
    bindingCached,
    resolutionCached,
    generationPublished

vars ==
    <<phase, policyState, operation, expectedVersion, returnedVersion,
      returnedAnswer, commitVersion, commitState, committed, bindingCached,
      resolutionCached, generationPublished>>

Init ==
    /\ phase = "Ready"
    /\ policyState = StateOne
    /\ operation \in Operations
    /\ expectedVersion = NoVersion
    /\ returnedVersion = NoVersion
    /\ returnedAnswer = NoAnswer
    /\ commitVersion = NoVersion
    /\ commitState = StateOne
    /\ committed = FALSE
    /\ bindingCached = FALSE
    /\ resolutionCached = FALSE
    /\ generationPublished = FALSE

Capture ==
    /\ phase = "Ready"
    /\ phase' = "Active"
    /\ expectedVersion' = VersionFor(policyState)
    /\ UNCHANGED
        <<policyState, operation, returnedVersion, returnedAnswer,
          commitVersion, commitState, committed, bindingCached,
          resolutionCached, generationPublished>>

Advance ==
    /\ phase \in {"Active", "Associating", "Validating", "Committed"}
    /\ policyState # StateThree
    /\ policyState' = NextState(policyState)
    /\ UNCHANGED
        <<phase, operation, expectedVersion, returnedVersion,
          returnedAnswer, commitVersion, committed, bindingCached,
          resolutionCached, commitState, generationPublished>>

EvaluateCold ==
    /\ phase = "Active"
    /\ operation = "Cold"
    /\ returnedAnswer' = AnswerFor(policyState)
    /\ IF AssociationMode = "Atomic"
       THEN
            /\ phase' = "Validating"
            /\ returnedVersion' = VersionFor(policyState)
       ELSE
            /\ phase' = "Associating"
            /\ returnedVersion' = NoVersion
    /\ bindingCached' = (SideEffectMode = "BeforeValidation")
    /\ resolutionCached' = (SideEffectMode = "BeforeValidation")
    /\ generationPublished' = (SideEffectMode = "BeforeValidation")
    /\ UNCHANGED
        <<policyState, operation, expectedVersion, commitVersion, commitState,
          committed>>

AssociateObservedVersion ==
    /\ phase = "Associating"
    /\ phase' = "Validating"
    /\ returnedVersion' = VersionFor(policyState)
    /\ UNCHANGED
        <<policyState, operation, expectedVersion, returnedAnswer,
          commitVersion, commitState, committed, bindingCached,
          resolutionCached, generationPublished>>

EvaluateCache ==
    /\ phase = "Active"
    /\ operation = "Cache"
    /\ phase' = "Validating"
    /\ returnedVersion' = expectedVersion
    /\ returnedAnswer' = AnswerOne
    /\ bindingCached' = (SideEffectMode = "BeforeValidation")
    /\ resolutionCached' = (SideEffectMode = "BeforeValidation")
    /\ generationPublished' = (SideEffectMode = "BeforeValidation")
    /\ UNCHANGED
        <<policyState, operation, expectedVersion, commitVersion, commitState,
          committed>>

Commit ==
    /\ phase = "Validating"
    /\ commitVersion' = VersionFor(policyState)
    /\ commitState' = policyState
    /\ IF /\ returnedVersion = expectedVersion
          /\ (FinalValidationMode = "Skip"
              \/ VersionFor(policyState) = expectedVersion)
       THEN
            /\ phase' = "Committed"
            /\ committed' = TRUE
       ELSE
            /\ phase' = "Done"
            /\ committed' = FALSE
    /\ UNCHANGED
        <<policyState, operation, expectedVersion, returnedVersion,
          returnedAnswer, bindingCached, resolutionCached,
          generationPublished>>

Publish ==
    /\ phase = "Committed"
    /\ phase' = "Done"
    /\ bindingCached' = TRUE
    /\ resolutionCached' = TRUE
    /\ generationPublished' = TRUE
    /\ UNCHANGED
        <<policyState, operation, expectedVersion, returnedVersion,
          returnedAnswer, commitVersion, commitState, committed>>

Next ==
    Capture
    \/ Advance
    \/ EvaluateCold
    \/ AssociateObservedVersion
    \/ EvaluateCache
    \/ Commit
    \/ Publish

Spec ==
    /\ Init
    /\ [][Next]_vars
    /\ WF_vars(Capture)
    /\ WF_vars(EvaluateCold)
    /\ WF_vars(AssociateObservedVersion)
    /\ WF_vars(EvaluateCache)
    /\ WF_vars(Commit)
    /\ WF_vars(Publish)

TypeOK ==
    /\ phase \in Phases
    /\ policyState \in States
    /\ operation \in Operations
    /\ expectedVersion \in Versions \union {NoVersion}
    /\ returnedVersion \in Versions \union {NoVersion}
    /\ returnedAnswer \in Answers \union {NoAnswer}
    /\ commitVersion \in Versions \union {NoVersion}
    /\ commitState \in States
    /\ committed \in BOOLEAN
    /\ bindingCached \in BOOLEAN
    /\ resolutionCached \in BOOLEAN
    /\ generationPublished \in BOOLEAN

ReturnedColdSnapshotIsAtomic ==
    /\ phase \in {"Validating", "Committed", "Done"}
    /\ operation = "Cold"
    /\ returnedVersion # NoVersion
    =>
        \E state \in States:
            /\ returnedVersion = VersionFor(state)
            /\ returnedAnswer = AnswerFor(state)

AdvancedStateHasFreshVersion ==
    /\ phase # "Ready"
    /\ policyState # StateOne
    => VersionFor(policyState) # expectedVersion

CommittedAnswerBelongsToCapturedVersion ==
    committed =>
        /\ returnedVersion = expectedVersion
        /\ returnedAnswer = AnswerOne

CommitObservedCapturedVersion ==
    committed => commitVersion = expectedVersion

CachedAnswerNotCommittedAfterStateChange ==
    /\ committed
    /\ operation = "Cache"
    => commitState = StateOne

UncommittedGenerationHasNoPolicyPublication ==
    ~committed =>
        /\ ~bindingCached
        /\ ~resolutionCached
        /\ ~generationPublished

PolicyPublicationRequiresCommit ==
    (bindingCached \/ resolutionCached \/ generationPublished) => committed

SelectionConverges == <>(phase = "Done")

=============================================================================
