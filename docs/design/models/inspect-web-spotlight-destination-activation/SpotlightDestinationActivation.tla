---------------- MODULE SpotlightDestinationActivation ----------------
(***************************************************************************)
(* Focused model for Inspect Web Spotlight destination activation.         *)
(*                                                                         *)
(* One exact result is classified against one captured active Workspace,   *)
(* Scope revision, publication base, complete registration projection, and *)
(* exact Package occurrence inventory. Current Package admission may       *)
(* commit before focus settles. External-package construction is an opaque  *)
(* Workspace Definitions result that only a current attempt may publish.    *)
(***************************************************************************)
EXTENDS Naturals, Sequences, TLC

CONSTANTS Mutation, Scenario

NoMutation == "None"
CollapseSameNameCoverage == "CollapseSameNameCoverage"
DropOverlappingWitnesses == "DropOverlappingWitnesses"
FallbackAfterCoveredFailure == "FallbackAfterCoveredFailure"
StaleCurrentCommit == "StaleCurrentCommit"
StaleDirectFocus == "StaleDirectFocus"
StalePostMembershipFocus == "StalePostMembershipFocus"
StaleFreshPublication == "StaleFreshPublication"
WrongLibraryOccurrence == "WrongLibraryOccurrence"
PublishFailedFreshActivation == "PublishFailedFreshActivation"
DualCurrentAndFreshPublication == "DualCurrentAndFreshPublication"

Mutations ==
    {NoMutation,
     CollapseSameNameCoverage,
     DropOverlappingWitnesses,
     FallbackAfterCoveredFailure,
     StaleCurrentCommit,
     StaleDirectFocus,
     StalePostMembershipFocus,
     StaleFreshPublication,
     WrongLibraryOccurrence,
     PublishFailedFreshActivation,
     DualCurrentAndFreshPublication}

ASSUME Mutation \in Mutations

SourcePairScenario == "SourcePair"
CoveredPackageScenario == "CoveredPackage"
LibraryScenario == "Library"
ExactLibraryScenario == "ExactLibrary"
OverlapScenario == "Overlap"
Scenarios ==
    {SourcePairScenario,
     CoveredPackageScenario,
     LibraryScenario,
     ExactLibraryScenario,
     OverlapScenario}

ASSUME Scenario \in Scenarios

CurrentWorkspace == "workspace-current"
ReplacementWorkspace == "workspace-replacement"
FreshWorkspaceOne == "workspace-fresh-1"
FreshWorkspaceTwo == "workspace-fresh-2"
Workspaces ==
    {CurrentWorkspace,
     ReplacementWorkspace,
     FreshWorkspaceOne,
     FreshWorkspaceTwo}

ExistingPackage == "existing-package"
PlatformJsonLibrary == "platform-System.Text.Json"
PackageJson == "package-System.Text.Json"
PackageJsonLibrary == "package-System.Text.Json-library"
ExtensionsPackage == "package-Microsoft.Extensions.Logging"
Destinations ==
    {ExistingPackage,
     PlatformJsonLibrary,
     PackageJson,
     PackageJsonLibrary,
     ExtensionsPackage}

PackageDestinations ==
    {ExistingPackage, PackageJson, ExtensionsPackage}
LibraryDestinations ==
    {PlatformJsonLibrary, PackageJsonLibrary}

NoWorkspace == "no-workspace"
NoDestination == "no-destination"
NoFocus == "no-focus"
NoPlan == "no-plan"
NoOccurrence == "no-occurrence"
WrongOccurrence == "wrong-occurrence"
ExistingOccurrence == "occurrence-existing"
OccurrenceOne == "occurrence-1"
OccurrenceTwo == "occurrence-2"
Occurrences ==
    {ExistingOccurrence, OccurrenceOne, OccurrenceTwo, WrongOccurrence}

PlatformExactWitness == "platform-exact-library"
PlatformEcosystemWitness == "ecosystem.platform/platform-population"
PackageExactLibraryWitness == "package-exact-library"
DirectPackagePrefixWitness == "direct-System-prefix"
EcosystemPackagePrefixWitness == "ecosystem.sample/System-prefix"
ExtensionsEcosystemPrefixWitness ==
    "ecosystem.extensions/Microsoft.Extensions-prefix"

CoverageWitnesses ==
    {PlatformExactWitness,
     PlatformEcosystemWitness,
     PackageExactLibraryWitness,
     DirectPackagePrefixWitness,
     EcosystemPackagePrefixWitness,
     ExtensionsEcosystemPrefixWitness}

NavigateCurrent == "navigate-current"
ActivateCurrentPackageLibrary == "activate-current-package-library"
AddCurrentPackage == "add-current-package"
AddCurrentPackageLibrary == "add-current-package-library"
ActivateCurrentPlatform == "activate-current-platform"
RestoreExternalPackageWorkspace == "restore-external-package-workspace"
UnavailableLibrary == "unavailable-library"

Plans ==
    {NavigateCurrent,
     ActivateCurrentPackageLibrary,
     AddCurrentPackage,
     AddCurrentPackageLibrary,
     ActivateCurrentPlatform,
     RestoreExternalPackageWorkspace,
     UnavailableLibrary}

Unused == "unused"
Pending == "pending"
Settled == "settled"
AttemptStates == {Unused, Pending, Settled}

Ready == "ready"
CurrentMembershipCommitted == "current-membership-committed"
DefinitionsPending == "definitions-pending"
DefinitionsPrepared == "definitions-prepared"
Phases ==
    {Ready,
     CurrentMembershipCommitted,
     DefinitionsPending,
     DefinitionsPrepared}

NoResult == "none"
CurrentActivated == "current-activated"
PlatformActivated == "platform-activated"
FreshWorkspaceActivated == "fresh-workspace-activated"
Unavailable == "unavailable"
Failed == "failed"
MembershipCommittedFocusFailed == "membership-committed-focus-failed"
Stale == "stale"
Superseded == "superseded"
MembershipCommittedFocusSuperseded ==
    "membership-committed-focus-superseded"

Results ==
    {NoResult,
     CurrentActivated,
     PlatformActivated,
     FreshWorkspaceActivated,
     Unavailable,
     Failed,
     MembershipCommittedFocusFailed,
     Stale,
     Superseded,
     MembershipCommittedFocusSuperseded}

MaxIntent == 2
Tokens == 1..MaxIntent
RevisionValues == 1..3
BaseValues == 1..4
RegistrationProfiles == {1, 2, 3}

FreshWorkspaceFor(t) ==
    IF t = 1 THEN FreshWorkspaceOne ELSE FreshWorkspaceTwo

IssuedOccurrenceFor(t) ==
    IF t = 1 THEN OccurrenceOne ELSE OccurrenceTwo

EnclosingPackage(destination) ==
    IF destination = PackageJsonLibrary
    THEN PackageJson
    ELSE destination

EmptyOccurrences ==
    [package \in PackageDestinations |-> NoOccurrence]

FullCoverage(profile, destination) ==
    IF destination = PlatformJsonLibrary
    THEN <<PlatformExactWitness, PlatformEcosystemWitness>>
    ELSE IF destination = ExtensionsPackage
    THEN <<ExtensionsEcosystemPrefixWitness>>
    ELSE IF profile = 3 /\ destination = PackageJson
    THEN <<DirectPackagePrefixWitness, EcosystemPackagePrefixWitness>>
    ELSE IF profile = 2 /\ destination = PackageJsonLibrary
    THEN <<PackageExactLibraryWitness>>
    ELSE IF profile = 3 /\ destination = PackageJsonLibrary
    THEN
        <<PackageExactLibraryWitness,
          DirectPackagePrefixWitness,
          EcosystemPackagePrefixWitness>>
    ELSE <<>>

ProjectedCoverage(profile, destination) ==
    IF /\ Mutation = CollapseSameNameCoverage
       /\ profile = 1
       /\ destination \in {PackageJson, PackageJsonLibrary}
    THEN <<PlatformExactWitness, PlatformEcosystemWitness>>
    ELSE IF /\ Mutation = DropOverlappingWitnesses
            /\ profile = 3
            /\ destination = PackageJsonLibrary
    THEN <<PackageExactLibraryWitness>>
    ELSE FullCoverage(profile, destination)

OccurrenceFor(occurrences, destination) ==
    IF destination = PackageJsonLibrary
    THEN occurrences[PackageJson]
    ELSE IF destination \in PackageDestinations
    THEN occurrences[destination]
    ELSE NoOccurrence

PlanFor(occurrences, realizedLibraries, coverage, destination) ==
    IF /\ destination \in PackageDestinations
       /\ OccurrenceFor(occurrences, destination) # NoOccurrence
    THEN NavigateCurrent
    ELSE IF destination \in realizedLibraries
    THEN NavigateCurrent
    ELSE IF /\ destination = PackageJsonLibrary
            /\ OccurrenceFor(occurrences, destination) # NoOccurrence
    THEN ActivateCurrentPackageLibrary
    ELSE IF destination = PlatformJsonLibrary
    THEN
        IF Len(coverage) > 0
        THEN ActivateCurrentPlatform
        ELSE UnavailableLibrary
    ELSE IF destination = PackageJsonLibrary
    THEN
        IF Len(coverage) > 0
        THEN AddCurrentPackageLibrary
        ELSE UnavailableLibrary
    ELSE IF destination \in PackageDestinations
    THEN
        IF Len(coverage) > 0
        THEN AddCurrentPackage
        ELSE RestoreExternalPackageWorkspace
    ELSE UnavailableLibrary

AllowedDestination(token, destination) ==
    CASE Scenario = SourcePairScenario ->
        IF token = 1
        THEN destination = PlatformJsonLibrary
        ELSE destination = PackageJson
      [] Scenario = CoveredPackageScenario ->
        IF token = 1
        THEN destination = ExtensionsPackage
        ELSE destination = ExistingPackage
      [] Scenario = LibraryScenario ->
        IF token = 1
        THEN destination = PackageJson
        ELSE destination = PackageJsonLibrary
      [] Scenario = ExactLibraryScenario ->
        IF token = 1
        THEN destination = PackageJsonLibrary
        ELSE destination = ExistingPackage
      [] Scenario = OverlapScenario ->
        IF token = 1
        THEN destination = PackageJsonLibrary
        ELSE destination = ExistingPackage
      [] OTHER -> FALSE

AllowedReplacementProfile(profile) ==
    CASE Scenario = SourcePairScenario -> profile = 2
      [] Scenario = LibraryScenario -> profile = 3
      [] Scenario = ExactLibraryScenario -> profile = 2
      [] Scenario = OverlapScenario -> profile = 3
      [] OTHER -> profile \in {2, 3}

NoAttempt ==
    [ state                  |-> Unused,
      plan                   |-> NoPlan,
      workspace              |-> NoWorkspace,
      scopeRevision          |-> 0,
      scopeBase              |-> 0,
      registrationProfile    |-> 0,
      destination            |-> NoDestination,
      coverage               |-> <<>>,
      occurrence             |-> NoOccurrence,
      phase                  |-> Ready,
      committedScopeRevision |-> 0,
      committedScopeBase     |-> 0,
      returnedOccurrence     |-> NoOccurrence ]

VARIABLES
    activeWorkspace,
    scopeRevision,
    scopeBase,
    registrationProfile,
    packageOccurrences,
    realizedLibraries,
    focus,
    intent,
    attempts,
    results,
    membershipCommits,
    focusPublications,
    freshPublications,
    visibleFailures,
    definitionsRequests,
    definitionsReady,
    sourceMutations,
    badCurrentCommit,
    badDirectFocus,
    badPostMembershipFocus,
    badFreshPublication,
    badLibraryOccurrence

vars ==
    <<activeWorkspace,
      scopeRevision,
      scopeBase,
      registrationProfile,
      packageOccurrences,
      realizedLibraries,
      focus,
      intent,
      attempts,
      results,
      membershipCommits,
      focusPublications,
      freshPublications,
      visibleFailures,
      definitionsRequests,
      definitionsReady,
      sourceMutations,
      badCurrentCommit,
      badDirectFocus,
      badPostMembershipFocus,
      badFreshPublication,
      badLibraryOccurrence>>

TypeOK ==
    /\ activeWorkspace \in Workspaces
    /\ focus \in Destinations \cup {NoFocus}
    /\ intent \in 0..MaxIntent
    /\ membershipCommits \subseteq Tokens
    /\ focusPublications \subseteq Tokens
    /\ freshPublications \subseteq Tokens
    /\ visibleFailures \subseteq Tokens
    /\ definitionsRequests \subseteq Tokens
    /\ definitionsReady \subseteq Tokens
    /\ sourceMutations \subseteq Tokens
    /\ badCurrentCommit \in BOOLEAN
    /\ badDirectFocus \in BOOLEAN
    /\ badPostMembershipFocus \in BOOLEAN
    /\ badFreshPublication \in BOOLEAN
    /\ badLibraryOccurrence \in BOOLEAN
    /\ \A workspace \in Workspaces :
        /\ scopeRevision[workspace] \in RevisionValues
        /\ scopeBase[workspace] \in BaseValues
        /\ registrationProfile[workspace] \in RegistrationProfiles
        /\ realizedLibraries[workspace] \subseteq LibraryDestinations
        /\ \A package \in PackageDestinations :
            packageOccurrences[workspace][package]
                \in Occurrences \cup {NoOccurrence}
    /\ \A token \in Tokens :
        /\ attempts[token].state \in AttemptStates
        /\ attempts[token].plan \in Plans \cup {NoPlan}
        /\ attempts[token].workspace \in Workspaces \cup {NoWorkspace}
        /\ attempts[token].scopeRevision \in 0..3
        /\ attempts[token].scopeBase \in 0..4
        /\ attempts[token].registrationProfile \in 0..3
        /\ attempts[token].destination
            \in Destinations \cup {NoDestination}
        /\ attempts[token].coverage \in Seq(CoverageWitnesses)
        /\ attempts[token].occurrence
            \in Occurrences \cup {NoOccurrence}
        /\ attempts[token].phase \in Phases
        /\ attempts[token].committedScopeRevision \in 0..3
        /\ attempts[token].committedScopeBase \in 0..4
        /\ attempts[token].returnedOccurrence
            \in Occurrences \cup {NoOccurrence}
        /\ results[token] \in Results

Init ==
    /\ activeWorkspace = CurrentWorkspace
    /\ scopeRevision = [workspace \in Workspaces |-> 1]
    /\ scopeBase = [workspace \in Workspaces |-> 1]
    /\ registrationProfile = [workspace \in Workspaces |-> 1]
    /\ packageOccurrences =
        [workspace \in Workspaces |->
            IF workspace = CurrentWorkspace
            THEN
                [EmptyOccurrences EXCEPT
                    ![ExistingPackage] = ExistingOccurrence]
            ELSE EmptyOccurrences]
    /\ realizedLibraries =
        [workspace \in Workspaces |-> {}]
    /\ focus = NoFocus
    /\ intent = 0
    /\ attempts = [token \in Tokens |-> NoAttempt]
    /\ results = [token \in Tokens |-> NoResult]
    /\ membershipCommits = {}
    /\ focusPublications = {}
    /\ freshPublications = {}
    /\ visibleFailures = {}
    /\ definitionsRequests = {}
    /\ definitionsReady = {}
    /\ sourceMutations = {}
    /\ badCurrentCommit = FALSE
    /\ badDirectFocus = FALSE
    /\ badPostMembershipFocus = FALSE
    /\ badFreshPublication = FALSE
    /\ badLibraryOccurrence = FALSE

CapturedBasisIsCurrent(token) ==
    /\ attempts[token].state = Pending
    /\ token = intent
    /\ attempts[token].workspace = activeWorkspace
    /\ attempts[token].scopeRevision =
        scopeRevision[activeWorkspace]
    /\ attempts[token].scopeBase = scopeBase[activeWorkspace]
    /\ attempts[token].registrationProfile =
        registrationProfile[activeWorkspace]

CommittedBasisIsCurrent(token) ==
    /\ attempts[token].state = Pending
    /\ attempts[token].phase = CurrentMembershipCommitted
    /\ token = intent
    /\ attempts[token].workspace = activeWorkspace
    /\ attempts[token].committedScopeRevision =
        scopeRevision[activeWorkspace]
    /\ attempts[token].committedScopeBase =
        scopeBase[activeWorkspace]
    /\ attempts[token].registrationProfile =
        registrationProfile[activeWorkspace]

StartSelection(destination) ==
    LET workspace == activeWorkspace
        profile == registrationProfile[workspace]
        coverage == ProjectedCoverage(profile, destination)
        occurrence ==
            OccurrenceFor(packageOccurrences[workspace], destination)
    IN
    /\ intent < MaxIntent
    /\ destination \in Destinations
    /\ AllowedDestination(intent + 1, destination)
    /\ intent' = intent + 1
    /\ attempts' =
        [attempts EXCEPT
            ![intent + 1] =
                [ state                  |-> Pending,
                  plan                   |->
                    PlanFor(packageOccurrences[workspace],
                            realizedLibraries[workspace],
                            coverage,
                            destination),
                  workspace              |-> workspace,
                  scopeRevision          |->
                    scopeRevision[workspace],
                  scopeBase              |-> scopeBase[workspace],
                  registrationProfile    |-> profile,
                  destination            |-> destination,
                  coverage               |-> coverage,
                  occurrence             |-> occurrence,
                  phase                  |-> Ready,
                  committedScopeRevision |-> 0,
                  committedScopeBase     |-> 0,
                  returnedOccurrence     |-> NoOccurrence ]]
    /\ UNCHANGED
        <<activeWorkspace,
          scopeRevision,
          scopeBase,
          registrationProfile,
          packageOccurrences,
          realizedLibraries,
          focus,
          results,
          membershipCommits,
          focusPublications,
          freshPublications,
          visibleFailures,
          definitionsRequests,
          definitionsReady,
          sourceMutations,
          badCurrentCommit,
          badDirectFocus,
          badPostMembershipFocus,
          badFreshPublication,
          badLibraryOccurrence>>

ReplaceRegistrations(profile) ==
    /\ registrationProfile[activeWorkspace] = 1
    /\ profile \in {2, 3}
    /\ AllowedReplacementProfile(profile)
    /\ scopeRevision[activeWorkspace] < 3
    /\ scopeBase[activeWorkspace] < 4
    /\ registrationProfile' =
        [registrationProfile EXCEPT ![activeWorkspace] = profile]
    /\ scopeRevision' =
        [scopeRevision EXCEPT ![activeWorkspace] = @ + 1]
    /\ scopeBase' =
        [scopeBase EXCEPT ![activeWorkspace] = @ + 1]
    /\ UNCHANGED
        <<activeWorkspace,
          packageOccurrences,
          realizedLibraries,
          focus,
          intent,
          attempts,
          results,
          membershipCommits,
          focusPublications,
          freshPublications,
          visibleFailures,
          definitionsRequests,
          definitionsReady,
          sourceMutations,
          badCurrentCommit,
          badDirectFocus,
          badPostMembershipFocus,
          badFreshPublication,
          badLibraryOccurrence>>

AdvanceMembershipRevision ==
    /\ scopeRevision[activeWorkspace] < 3
    /\ scopeBase[activeWorkspace] < 4
    /\ scopeRevision' =
        [scopeRevision EXCEPT ![activeWorkspace] = @ + 1]
    /\ scopeBase' =
        [scopeBase EXCEPT ![activeWorkspace] = @ + 1]
    /\ UNCHANGED
        <<activeWorkspace,
          registrationProfile,
          packageOccurrences,
          realizedLibraries,
          focus,
          intent,
          attempts,
          results,
          membershipCommits,
          focusPublications,
          freshPublications,
          visibleFailures,
          definitionsRequests,
          definitionsReady,
          sourceMutations,
          badCurrentCommit,
          badDirectFocus,
          badPostMembershipFocus,
          badFreshPublication,
          badLibraryOccurrence>>

AdvancePublicationBase ==
    /\ scopeBase[activeWorkspace] < 4
    /\ scopeBase' =
        [scopeBase EXCEPT ![activeWorkspace] = @ + 1]
    /\ UNCHANGED
        <<activeWorkspace,
          scopeRevision,
          registrationProfile,
          packageOccurrences,
          realizedLibraries,
          focus,
          intent,
          attempts,
          results,
          membershipCommits,
          focusPublications,
          freshPublications,
          visibleFailures,
          definitionsRequests,
          definitionsReady,
          sourceMutations,
          badCurrentCommit,
          badDirectFocus,
          badPostMembershipFocus,
          badFreshPublication,
          badLibraryOccurrence>>

ReplaceActiveWorkspace ==
    /\ activeWorkspace = CurrentWorkspace
    /\ activeWorkspace' = ReplacementWorkspace
    /\ focus' = NoFocus
    /\ UNCHANGED
        <<scopeRevision,
          scopeBase,
          registrationProfile,
          packageOccurrences,
          realizedLibraries,
          intent,
          attempts,
          results,
          membershipCommits,
          focusPublications,
          freshPublications,
          visibleFailures,
          definitionsRequests,
          definitionsReady,
          sourceMutations,
          badCurrentCommit,
          badDirectFocus,
          badPostMembershipFocus,
          badFreshPublication,
          badLibraryOccurrence>>

CompleteDirectFocus(token) ==
    LET workspace == attempts[token].workspace
        destination == attempts[token].destination
        expectedOccurrence ==
            OccurrenceFor(packageOccurrences[workspace], destination)
        usedOccurrence ==
            IF /\ Mutation = WrongLibraryOccurrence
               /\ destination = PackageJsonLibrary
            THEN WrongOccurrence
            ELSE attempts[token].occurrence
        authority == CapturedBasisIsCurrent(token)
    IN
    /\ attempts[token].state = Pending
    /\ attempts[token].phase = Ready
    /\ attempts[token].plan \in
        {NavigateCurrent,
         ActivateCurrentPackageLibrary,
         ActivateCurrentPlatform}
    /\ (authority \/ Mutation = StaleDirectFocus)
    /\ attempts' = [attempts EXCEPT ![token].state = Settled]
    /\ results' =
        [results EXCEPT
            ![token] =
                IF attempts[token].plan = ActivateCurrentPlatform
                THEN PlatformActivated
                ELSE CurrentActivated]
    /\ focus' = destination
    /\ realizedLibraries' =
        IF destination \in LibraryDestinations
        THEN
            [realizedLibraries EXCEPT
                ![workspace] = @ \cup {destination}]
        ELSE realizedLibraries
    /\ focusPublications' = focusPublications \cup {token}
    /\ badDirectFocus' =
        IF authority THEN badDirectFocus ELSE TRUE
    /\ badLibraryOccurrence' =
        IF /\ destination = PackageJsonLibrary
           /\ (usedOccurrence = NoOccurrence
               \/ usedOccurrence # expectedOccurrence)
        THEN TRUE
        ELSE badLibraryOccurrence
    /\ UNCHANGED
        <<activeWorkspace,
          scopeRevision,
          scopeBase,
          registrationProfile,
          packageOccurrences,
          intent,
          membershipCommits,
          freshPublications,
          visibleFailures,
          definitionsRequests,
          definitionsReady,
          sourceMutations,
          badCurrentCommit,
          badPostMembershipFocus,
          badFreshPublication>>

CommitCurrentMembership(token) ==
    LET workspace == attempts[token].workspace
        destination == attempts[token].destination
        package == EnclosingPackage(destination)
        issuedOccurrence == IssuedOccurrenceFor(token)
        newRevision == scopeRevision[workspace] + 1
        newBase == scopeBase[workspace] + 1
        authority == CapturedBasisIsCurrent(token)
    IN
    /\ attempts[token].state = Pending
    /\ attempts[token].phase = Ready
    /\ attempts[token].plan \in
        {AddCurrentPackage, AddCurrentPackageLibrary}
    /\ scopeRevision[workspace] < 3
    /\ scopeBase[workspace] < 4
    /\ (authority \/ Mutation = StaleCurrentCommit)
    /\ packageOccurrences' =
        [packageOccurrences EXCEPT
            ![workspace][package] = issuedOccurrence]
    /\ scopeRevision' =
        [scopeRevision EXCEPT ![workspace] = newRevision]
    /\ scopeBase' =
        [scopeBase EXCEPT ![workspace] = newBase]
    /\ attempts' =
        [attempts EXCEPT
            ![token].phase = CurrentMembershipCommitted,
            ![token].committedScopeRevision = newRevision,
            ![token].committedScopeBase = newBase,
            ![token].returnedOccurrence = issuedOccurrence]
    /\ membershipCommits' = membershipCommits \cup {token}
    /\ badCurrentCommit' =
        IF authority THEN badCurrentCommit ELSE TRUE
    /\ UNCHANGED
        <<activeWorkspace,
          registrationProfile,
          realizedLibraries,
          focus,
          intent,
          results,
          focusPublications,
          freshPublications,
          visibleFailures,
          definitionsRequests,
          definitionsReady,
          sourceMutations,
          badDirectFocus,
          badPostMembershipFocus,
          badFreshPublication,
          badLibraryOccurrence>>

CompletePostMembershipFocus(token) ==
    LET workspace == attempts[token].workspace
        destination == attempts[token].destination
        package == EnclosingPackage(destination)
        expectedOccurrence == packageOccurrences[workspace][package]
        usedOccurrence ==
            IF /\ Mutation = WrongLibraryOccurrence
               /\ destination = PackageJsonLibrary
            THEN WrongOccurrence
            ELSE attempts[token].returnedOccurrence
        authority == CommittedBasisIsCurrent(token)
    IN
    /\ attempts[token].state = Pending
    /\ attempts[token].phase = CurrentMembershipCommitted
    /\ attempts[token].plan \in
        {AddCurrentPackage, AddCurrentPackageLibrary}
    /\ (authority \/ Mutation = StalePostMembershipFocus)
    /\ attempts' = [attempts EXCEPT ![token].state = Settled]
    /\ results' = [results EXCEPT ![token] = CurrentActivated]
    /\ focus' = destination
    /\ realizedLibraries' =
        IF destination \in LibraryDestinations
        THEN
            [realizedLibraries EXCEPT
                ![workspace] = @ \cup {destination}]
        ELSE realizedLibraries
    /\ focusPublications' = focusPublications \cup {token}
    /\ badPostMembershipFocus' =
        IF authority THEN badPostMembershipFocus ELSE TRUE
    /\ badLibraryOccurrence' =
        IF /\ destination = PackageJsonLibrary
           /\ (usedOccurrence = NoOccurrence
               \/ usedOccurrence # expectedOccurrence)
        THEN TRUE
        ELSE badLibraryOccurrence
    /\ UNCHANGED
        <<activeWorkspace,
          scopeRevision,
          scopeBase,
          registrationProfile,
          packageOccurrences,
          intent,
          membershipCommits,
          freshPublications,
          visibleFailures,
          definitionsRequests,
          definitionsReady,
          sourceMutations,
          badCurrentCommit,
          badDirectFocus,
          badFreshPublication>>

FailCurrentBeforeCommit(token) ==
    LET fallback == Mutation = FallbackAfterCoveredFailure
        freshWorkspace == FreshWorkspaceFor(token)
        destination == attempts[token].destination
    IN
    /\ attempts[token].state = Pending
    /\ attempts[token].phase = Ready
    /\ attempts[token].plan \in
        {NavigateCurrent,
         ActivateCurrentPackageLibrary,
         AddCurrentPackage,
         AddCurrentPackageLibrary,
         ActivateCurrentPlatform}
    /\ CapturedBasisIsCurrent(token)
    /\ attempts' = [attempts EXCEPT ![token].state = Settled]
    /\ results' = [results EXCEPT ![token] = Failed]
    /\ visibleFailures' = visibleFailures \cup {token}
    /\ activeWorkspace' =
        IF fallback THEN freshWorkspace ELSE activeWorkspace
    /\ focus' = IF fallback THEN destination ELSE focus
    /\ packageOccurrences' =
        IF fallback /\ destination \in PackageDestinations
        THEN
            [packageOccurrences EXCEPT
                ![freshWorkspace][destination] =
                    IssuedOccurrenceFor(token)]
        ELSE packageOccurrences
    /\ freshPublications' =
        IF fallback
        THEN freshPublications \cup {token}
        ELSE freshPublications
    /\ UNCHANGED
        <<scopeRevision,
          scopeBase,
          registrationProfile,
          realizedLibraries,
          intent,
          membershipCommits,
          focusPublications,
          definitionsRequests,
          definitionsReady,
          sourceMutations,
          badCurrentCommit,
          badDirectFocus,
          badPostMembershipFocus,
          badFreshPublication,
          badLibraryOccurrence>>

FailPostMembershipFocus(token) ==
    LET fallback == Mutation = FallbackAfterCoveredFailure
        freshWorkspace == FreshWorkspaceFor(token)
        destination == attempts[token].destination
    IN
    /\ attempts[token].state = Pending
    /\ attempts[token].phase = CurrentMembershipCommitted
    /\ CommittedBasisIsCurrent(token)
    /\ attempts' = [attempts EXCEPT ![token].state = Settled]
    /\ results' =
        [results EXCEPT ![token] = MembershipCommittedFocusFailed]
    /\ visibleFailures' = visibleFailures \cup {token}
    /\ activeWorkspace' =
        IF fallback THEN freshWorkspace ELSE activeWorkspace
    /\ focus' = IF fallback THEN destination ELSE focus
    /\ packageOccurrences' =
        IF fallback /\ destination \in PackageDestinations
        THEN
            [packageOccurrences EXCEPT
                ![freshWorkspace][destination] =
                    IssuedOccurrenceFor(token)]
        ELSE packageOccurrences
    /\ freshPublications' =
        IF fallback
        THEN freshPublications \cup {token}
        ELSE freshPublications
    /\ UNCHANGED
        <<scopeRevision,
          scopeBase,
          registrationProfile,
          realizedLibraries,
          intent,
          membershipCommits,
          focusPublications,
          definitionsRequests,
          definitionsReady,
          sourceMutations,
          badCurrentCommit,
          badDirectFocus,
          badPostMembershipFocus,
          badFreshPublication,
          badLibraryOccurrence>>

RequestDefinitionsActivation(token) ==
    /\ attempts[token].state = Pending
    /\ attempts[token].phase = Ready
    /\ attempts[token].plan = RestoreExternalPackageWorkspace
    /\ CapturedBasisIsCurrent(token)
    /\ attempts' =
        [attempts EXCEPT ![token].phase = DefinitionsPending]
    /\ definitionsRequests' = definitionsRequests \cup {token}
    /\ UNCHANGED
        <<activeWorkspace,
          scopeRevision,
          scopeBase,
          registrationProfile,
          packageOccurrences,
          realizedLibraries,
          focus,
          intent,
          results,
          membershipCommits,
          focusPublications,
          freshPublications,
          visibleFailures,
          definitionsReady,
          sourceMutations,
          badCurrentCommit,
          badDirectFocus,
          badPostMembershipFocus,
          badFreshPublication,
          badLibraryOccurrence>>

DefinitionsPrepareCompleteActivation(token) ==
    /\ attempts[token].state = Pending
    /\ attempts[token].phase = DefinitionsPending
    /\ attempts' =
        [attempts EXCEPT ![token].phase = DefinitionsPrepared]
    /\ definitionsReady' = definitionsReady \cup {token}
    /\ UNCHANGED
        <<activeWorkspace,
          scopeRevision,
          scopeBase,
          registrationProfile,
          packageOccurrences,
          realizedLibraries,
          focus,
          intent,
          results,
          membershipCommits,
          focusPublications,
          freshPublications,
          visibleFailures,
          definitionsRequests,
          sourceMutations,
          badCurrentCommit,
          badDirectFocus,
          badPostMembershipFocus,
          badFreshPublication,
          badLibraryOccurrence>>

DefinitionsFailActivation(token) ==
    LET publishFailed == Mutation = PublishFailedFreshActivation
        freshWorkspace == FreshWorkspaceFor(token)
        destination == attempts[token].destination
    IN
    /\ attempts[token].state = Pending
    /\ attempts[token].phase = DefinitionsPending
    /\ CapturedBasisIsCurrent(token)
    /\ attempts' = [attempts EXCEPT ![token].state = Settled]
    /\ results' = [results EXCEPT ![token] = Failed]
    /\ visibleFailures' = visibleFailures \cup {token}
    /\ activeWorkspace' =
        IF publishFailed THEN freshWorkspace ELSE activeWorkspace
    /\ focus' = IF publishFailed THEN destination ELSE focus
    /\ packageOccurrences' =
        IF publishFailed
        THEN
            [packageOccurrences EXCEPT
                ![freshWorkspace][destination] =
                    IssuedOccurrenceFor(token)]
        ELSE packageOccurrences
    /\ freshPublications' =
        IF publishFailed
        THEN freshPublications \cup {token}
        ELSE freshPublications
    /\ UNCHANGED
        <<scopeRevision,
          scopeBase,
          registrationProfile,
          realizedLibraries,
          intent,
          membershipCommits,
          focusPublications,
          definitionsRequests,
          definitionsReady,
          sourceMutations,
          badCurrentCommit,
          badDirectFocus,
          badPostMembershipFocus,
          badFreshPublication,
          badLibraryOccurrence>>

PublishFreshActivation(token) ==
    LET freshWorkspace == FreshWorkspaceFor(token)
        sourceWorkspace == attempts[token].workspace
        destination == attempts[token].destination
        authority == CapturedBasisIsCurrent(token)
        dual == Mutation = DualCurrentAndFreshPublication
    IN
    /\ attempts[token].state = Pending
    /\ attempts[token].phase = DefinitionsPrepared
    /\ (authority \/ Mutation = StaleFreshPublication)
    /\ activeWorkspace' = freshWorkspace
    /\ focus' = destination
    /\ packageOccurrences' =
        [packageOccurrences EXCEPT
            ![freshWorkspace][destination] =
                IssuedOccurrenceFor(token)]
    /\ attempts' = [attempts EXCEPT ![token].state = Settled]
    /\ results' =
        [results EXCEPT ![token] = FreshWorkspaceActivated]
    /\ freshPublications' = freshPublications \cup {token}
    /\ sourceMutations' =
        IF dual THEN sourceMutations \cup {token} ELSE sourceMutations
    /\ membershipCommits' =
        IF dual
        THEN membershipCommits \cup {token}
        ELSE membershipCommits
    /\ badFreshPublication' =
        IF authority THEN badFreshPublication ELSE TRUE
    /\ UNCHANGED
        <<scopeRevision,
          scopeBase,
          registrationProfile,
          realizedLibraries,
          intent,
          focusPublications,
          visibleFailures,
          definitionsRequests,
          definitionsReady,
          badCurrentCommit,
          badDirectFocus,
          badPostMembershipFocus,
          badLibraryOccurrence>>

SettleUnavailable(token) ==
    /\ attempts[token].state = Pending
    /\ attempts[token].phase = Ready
    /\ attempts[token].plan = UnavailableLibrary
    /\ CapturedBasisIsCurrent(token)
    /\ attempts' = [attempts EXCEPT ![token].state = Settled]
    /\ results' = [results EXCEPT ![token] = Unavailable]
    /\ UNCHANGED
        <<activeWorkspace,
          scopeRevision,
          scopeBase,
          registrationProfile,
          packageOccurrences,
          realizedLibraries,
          focus,
          intent,
          membershipCommits,
          focusPublications,
          freshPublications,
          visibleFailures,
          definitionsRequests,
          definitionsReady,
          sourceMutations,
          badCurrentCommit,
          badDirectFocus,
          badPostMembershipFocus,
          badFreshPublication,
          badLibraryOccurrence>>

AttemptBasisIsStale(token) ==
    IF attempts[token].phase = CurrentMembershipCommitted
    THEN ~CommittedBasisIsCurrent(token)
    ELSE ~CapturedBasisIsCurrent(token)

SettleStaleOrSuperseded(token) ==
    LET afterMembership ==
            attempts[token].phase = CurrentMembershipCommitted
    IN
    /\ attempts[token].state = Pending
    /\ AttemptBasisIsStale(token)
    /\ attempts' = [attempts EXCEPT ![token].state = Settled]
    /\ results' =
        [results EXCEPT
            ![token] =
                IF afterMembership
                THEN MembershipCommittedFocusSuperseded
                ELSE IF token # intent THEN Superseded ELSE Stale]
    /\ UNCHANGED
        <<activeWorkspace,
          scopeRevision,
          scopeBase,
          registrationProfile,
          packageOccurrences,
          realizedLibraries,
          focus,
          intent,
          membershipCommits,
          focusPublications,
          freshPublications,
          visibleFailures,
          definitionsRequests,
          definitionsReady,
          sourceMutations,
          badCurrentCommit,
          badDirectFocus,
          badPostMembershipFocus,
          badFreshPublication,
          badLibraryOccurrence>>

SettleAttempt(token) ==
    \/ CompleteDirectFocus(token)
    \/ CommitCurrentMembership(token)
    \/ CompletePostMembershipFocus(token)
    \/ FailCurrentBeforeCommit(token)
    \/ FailPostMembershipFocus(token)
    \/ RequestDefinitionsActivation(token)
    \/ DefinitionsPrepareCompleteActivation(token)
    \/ DefinitionsFailActivation(token)
    \/ PublishFreshActivation(token)
    \/ SettleUnavailable(token)
    \/ SettleStaleOrSuperseded(token)

Next ==
    \/ \E destination \in Destinations : StartSelection(destination)
    \/ \E profile \in {2, 3} : ReplaceRegistrations(profile)
    \/ AdvanceMembershipRevision
    \/ AdvancePublicationBase
    \/ ReplaceActiveWorkspace
    \/ \E token \in Tokens : SettleAttempt(token)

Fairness ==
    \A token \in Tokens : WF_vars(SettleAttempt(token))

Spec == Init /\ [][Next]_vars /\ Fairness

(***************************************************************************)
(* Safety.                                                                 *)
(***************************************************************************)

ExactSourceIdentityControlsClassification ==
    \A token \in Tokens :
        IF /\ attempts[token].state # Unused
           /\ attempts[token].workspace = CurrentWorkspace
           /\ attempts[token].scopeRevision = 1
           /\ attempts[token].scopeBase = 1
           /\ attempts[token].registrationProfile = 1
        THEN
            /\ (attempts[token].destination = PlatformJsonLibrary
                  => attempts[token].plan = ActivateCurrentPlatform)
            /\ (attempts[token].destination = PackageJson
                  => attempts[token].plan =
                    RestoreExternalPackageWorkspace)
            /\ (attempts[token].destination = PackageJsonLibrary
                  => attempts[token].plan = UnavailableLibrary)
        ELSE TRUE

CapturedCoverageIsCompleteAndOrdered ==
    \A token \in Tokens :
        attempts[token].state # Unused
        =>
            attempts[token].coverage =
                FullCoverage(attempts[token].registrationProfile,
                             attempts[token].destination)

PackageMembershipCoversLibraryWithoutDuplicateAdd ==
    \A token \in Tokens :
        attempts[token].plan = ActivateCurrentPackageLibrary
        =>
            /\ attempts[token].destination = PackageJsonLibrary
            /\ attempts[token].occurrence # NoOccurrence

CurrentPublicationsMatchPlan ==
    /\ \A token \in focusPublications :
        attempts[token].plan \in
            {NavigateCurrent,
             ActivateCurrentPackageLibrary,
             AddCurrentPackage,
             AddCurrentPackageLibrary,
             ActivateCurrentPlatform}
    /\ \A token \in membershipCommits :
        attempts[token].plan \in
            {AddCurrentPackage, AddCurrentPackageLibrary}

FreshPublicationsMatchPlanAndDefinitions ==
    \A token \in freshPublications :
        /\ attempts[token].plan = RestoreExternalPackageWorkspace
        /\ token \in definitionsReady

OneWorkspaceEffectPerAttempt ==
    /\ membershipCommits \cap freshPublications = {}
    /\ focusPublications \cap freshPublications = {}
    /\ sourceMutations = {}

CoveredFailureNeverCreatesFreshWorkspace ==
    \A token \in Tokens :
        results[token] \in {Failed, MembershipCommittedFocusFailed}
        => token \notin freshPublications

AllEffectPublicationHeldCurrentAuthority ==
    /\ ~badCurrentCommit
    /\ ~badDirectFocus
    /\ ~badPostMembershipFocus
    /\ ~badFreshPublication

PackageLibraryFocusUsesExactOccurrence ==
    ~badLibraryOccurrence

CommittedMembershipSurvivesFocusNonSuccess ==
    \A token \in Tokens :
        results[token] \in
            {MembershipCommittedFocusFailed,
             MembershipCommittedFocusSuperseded}
        =>
            /\ token \in membershipCommits
            /\ packageOccurrences[attempts[token].workspace]
                    [EnclosingPackage(attempts[token].destination)]
                = attempts[token].returnedOccurrence

FailedDefinitionsResultIsNeverPublished ==
    \A token \in Tokens :
        results[token] = Failed
        => token \notin freshPublications

SettledResultMatchesPublication ==
    \A token \in Tokens :
        /\ results[token] = CurrentActivated
            => token \in focusPublications
        /\ results[token] = PlatformActivated
            => token \in focusPublications
        /\ results[token] = FreshWorkspaceActivated
            => token \in freshPublications

(***************************************************************************)
(* Reachability witnesses. Each is intentionally false once its path runs. *)
(***************************************************************************)

NoNavigateCurrentActivation ==
    \A token \in Tokens :
        ~(/\ attempts[token].plan = NavigateCurrent
          /\ results[token] = CurrentActivated)

NoAddCurrentPackageActivation ==
    \A token \in Tokens :
        ~(/\ attempts[token].plan = AddCurrentPackage
          /\ token \in membershipCommits
          /\ results[token] = CurrentActivated)

NoAddCurrentPackageLibraryActivation ==
    \A token \in Tokens :
        ~(/\ attempts[token].plan = AddCurrentPackageLibrary
          /\ token \in membershipCommits
          /\ results[token] = CurrentActivated)

NoPlatformActivation ==
    \A token \in Tokens :
        ~(/\ attempts[token].plan = ActivateCurrentPlatform
          /\ results[token] = PlatformActivated)

NoFreshWorkspacePublication ==
    \A token \in Tokens :
        ~(/\ attempts[token].plan = RestoreExternalPackageWorkspace
          /\ results[token] = FreshWorkspaceActivated)

NoUnavailableLibrarySettlement ==
    \A token \in Tokens :
        ~(/\ attempts[token].plan = UnavailableLibrary
          /\ results[token] = Unavailable)

NoVisibleFailure == visibleFailures = {}

NoPartialMembershipFailure ==
    \A token \in Tokens :
        results[token] # MembershipCommittedFocusFailed

NoStaleOrSupersededSettlement ==
    \A token \in Tokens :
        results[token] \notin
            {Stale, Superseded, MembershipCommittedFocusSuperseded}

NoMembershipCoveredLibraryActivation ==
    \A token \in Tokens :
        ~(/\ attempts[token].plan = ActivateCurrentPackageLibrary
          /\ results[token] = CurrentActivated)

NoCompleteOverlapCapture ==
    \A token \in Tokens :
        attempts[token].coverage # FullCoverage(3, PackageJsonLibrary)

(***************************************************************************)
(* Liveness.                                                               *)
(***************************************************************************)

EveryAttemptSettles ==
    \A token \in Tokens :
        attempts[token].state = Pending
        ~> attempts[token].state = Settled

=============================================================================
