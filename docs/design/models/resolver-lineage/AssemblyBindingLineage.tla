------------------------- MODULE AssemblyBindingLineage -------------------------
EXTENDS Integers, FiniteSets, TLC

\* The binding owner supplies a selected descriptor and its continuation as a
\* pair. Each flow is an independent resolution of the same forwarding image.
CONSTANTS VersionOne, VersionTwo, Mode, AllowPolicyChange

Flows == {"Left", "Right"}
Destinations == {"Transitive", "CanonicalRight"}
Modes == {"Explicit", "RegistrationMap", "CandidateCache",
          "AlwaysInherit", "RouteChurn"}
Candidates == {"Shared", "RightRoot", "LeftBase", "RightBase"}
NoValue == "None"

ASSUME /\ VersionOne # VersionTwo
       /\ Mode \in Modes
       /\ AllowPolicyChange \in BOOLEAN

VARIABLES version, advanced, externalChange, destination, phase,
          occurrences, routes, cache, answers, status, committedVersion

BindingVersion ==
    INSTANCE AssemblyBindingPolicyVersionLifecycle WITH
        InitialVersion <- VersionOne,
        ReplacementVersion <- VersionTwo,
        version <- version,
        advanced <- advanced

vars == <<version, advanced, externalChange, destination, phase,
          occurrences, routes, cache, answers, status, committedVersion>>

SelectedCandidate ==
    IF destination = "Transitive" THEN "Shared" ELSE "RightRoot"

ExpectedResolver(flow) ==
    IF destination = "CanonicalRight" THEN "Right" ELSE flow

Terminal(resolver) ==
    IF resolver = "Left" THEN "LeftBase" ELSE "RightBase"

Lineage(resolver) ==
    [issuer |-> "Group", policyVersion |-> VersionOne, resolver |-> resolver]

Occurrence(flow) ==
    [candidate |-> SelectedCandidate,
     lineage |-> Lineage(
         IF Mode = "AlwaysInherit" THEN flow ELSE ExpectedResolver(flow))]

CacheKey(occurrence) ==
    IF Mode = "CandidateCache"
    THEN <<VersionOne, occurrence.candidate, "BaseReference", "Any">>
    ELSE <<VersionOne, occurrence.candidate, occurrence.lineage,
           "BaseReference", "Any">>

Init ==
    /\ BindingVersion!Init
    /\ externalChange = FALSE
    /\ destination \in Destinations
    /\ phase = [f \in Flows |-> "Seed"]
    /\ occurrences = [f \in Flows |-> NoValue]
    /\ routes = [candidate \in {"RightRoot"} |-> "Right"]
    /\ cache = [key \in {} |-> NoValue]
    /\ answers = [f \in Flows |-> NoValue]
    /\ status = "Running"
    /\ committedVersion = NoValue

SelectOccurrence(flow) ==
    /\ status = "Running"
    /\ version = VersionOne
    /\ phase[flow] = "Seed"
    /\ phase' = [phase EXCEPT ![flow] = "Selected"]
    /\ occurrences' = [occurrences EXCEPT ![flow] = Occurrence(flow)]
    /\ routes' =
        IF Mode = "RegistrationMap" /\ SelectedCandidate \notin DOMAIN routes
        THEN routes @@ (SelectedCandidate :> ExpectedResolver(flow))
        ELSE routes
    /\ IF Mode = "RouteChurn"
       THEN BindingVersion!Advance
       ELSE UNCHANGED <<version, advanced>>
    /\ UNCHANGED <<externalChange, destination, cache, answers, status,
                   committedVersion>>

ResolveReference(flow) ==
    /\ status = "Running"
    /\ version = VersionOne
    /\ phase[flow] = "Selected"
    /\ LET occurrence == occurrences[flow]
           key == CacheKey(occurrence)
           resolver ==
               IF Mode = "RegistrationMap"
               THEN routes[occurrence.candidate]
               ELSE occurrence.lineage.resolver
           answer ==
               IF key \in DOMAIN cache THEN cache[key] ELSE Terminal(resolver)
       IN /\ answers' = [answers EXCEPT ![flow] = answer]
          /\ cache' = IF key \in DOMAIN cache
                      THEN cache
                      ELSE cache @@ (key :> answer)
    /\ phase' = [phase EXCEPT ![flow] = "Prepared"]
    /\ UNCHANGED <<version, advanced, externalChange, destination,
                   occurrences, routes, status, committedVersion>>

ChangePolicy ==
    /\ AllowPolicyChange
    /\ status = "Running"
    /\ BindingVersion!Advance
    /\ externalChange' = TRUE
    /\ UNCHANGED <<destination, phase, occurrences, routes, cache, answers,
                   status, committedVersion>>

Settle ==
    /\ status = "Running"
    /\ IF version # VersionOne
       THEN /\ status' = "Superseded"
            /\ UNCHANGED committedVersion
       ELSE /\ \A f \in Flows : phase[f] = "Prepared"
            /\ status' = "Published"
            /\ committedVersion' = version
    /\ UNCHANGED <<version, advanced, externalChange, destination, phase,
                   occurrences, routes, cache, answers>>

Next ==
    (\E f \in Flows : SelectOccurrence(f) \/ ResolveReference(f))
    \/ ChangePolicy
    \/ Settle

Spec ==
    /\ Init
    /\ [][Next]_vars
    /\ \A f \in Flows : WF_vars(SelectOccurrence(f))
    /\ \A f \in Flows : WF_vars(ResolveReference(f))
    /\ WF_vars(Settle)

TypeOK ==
    /\ BindingVersion!TypeOK
    /\ externalChange \in BOOLEAN
    /\ destination \in Destinations
    /\ phase \in [Flows -> {"Seed", "Selected", "Prepared"}]
    /\ \A f \in Flows :
        IF phase[f] = "Seed"
        THEN occurrences[f] = NoValue
        ELSE occurrences[f] \in
            [candidate : Candidates,
             lineage : [issuer : {"Group"},
                        policyVersion : {VersionOne},
                        resolver : Flows]]
    /\ answers \in [Flows -> Candidates \union {NoValue}]
    /\ status \in {"Running", "Published", "Superseded"}
    /\ committedVersion \in {NoValue, VersionOne}

SelectionRetainsResolver ==
    \A f \in Flows :
        phase[f] \in {"Selected", "Prepared"}
        => occurrences[f].lineage = Lineage(ExpectedResolver(f))

ReferenceUsesSelectingContext ==
    \A f \in Flows :
        answers[f] # NoValue => answers[f] = Terminal(ExpectedResolver(f))

ContinuationDoesNotChangePolicy == advanced => externalChange

PublishedAssociation ==
    status = "Published"
    => /\ committedVersion = VersionOne
       /\ \A f \in Flows : answers[f] = Terminal(ExpectedResolver(f))

BindingVersionAdvanceIsFresh == BindingVersion!AdvancedVersionIsFresh
BindingVersionBehaviorRefinesOwner == BindingVersion!SafetySpec
AttemptSettles == <>(status # "Running")
StableAttemptPublishes == <>(status = "Published")
NeverSuperseded == status # "Superseded"

=============================================================================
