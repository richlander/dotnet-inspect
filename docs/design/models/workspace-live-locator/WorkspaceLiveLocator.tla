-------------------- MODULE WorkspaceLiveLocator --------------------
EXTENDS Naturals, FiniteSets

CONSTANT Fault
ASSUME Fault \in {"None", "BootstrapGap", "RetagAnswer", "EarlyRelease"}

Members == 1..2
Callers == 1..2
NoResult == "NoResult"
Workspace == "Workspace"
GroupOne == <<Workspace, 1>>
GroupTwo == <<Workspace, 2>>
NoGroup == <<"NoWorkspace", 0>>
ReleaseResults == {"Released", "ReleaseFailed"}
NoReleaseResult == "NoReleaseResult"

VARIABLES phase, published, baseline, known, notified, working, inventories,
    scans, declarations, queries, captured, results, bootstrapRace,
    requestedOne, completedOne, releaseOne,
    requestedTwo, completedTwo, releaseTwo

localVars == <<phase, published, baseline, known, notified, working,
    inventories, scans, declarations, queries, captured, results, bootstrapRace>>
oneVars == <<requestedOne, completedOne, releaseOne>>
twoVars == <<requestedTwo, completedTwo, releaseTwo>>
vars == <<localVars, oneVars, twoVars>>

One == INSTANCE AssemblyContextGroupReleaseLifecycle WITH
    Group <- GroupOne, NoGroup <- NoGroup,
    ReleaseResults <- ReleaseResults, NoReleaseResult <- NoReleaseResult,
    requestedGroup <- requestedOne, completedGroup <- completedOne,
    completionResult <- releaseOne
Two == INSTANCE AssemblyContextGroupReleaseLifecycle WITH
    Group <- GroupTwo, NoGroup <- NoGroup,
    ReleaseResults <- ReleaseResults, NoReleaseResult <- NoReleaseResult,
    requestedGroup <- requestedTwo, completedGroup <- completedTwo,
    completionResult <- releaseTwo

\* Ordinals represent distinct committed population receipts, not event counts
\* or arithmetic over an implementation's opaque publication identity.
Receipt(n) ==
    [workspace |-> Workspace, publication |-> n, occurrences |-> 1..n]
Evidence(p) ==
    [occurrence |-> p,
     coordinate |-> IF p = 1 THEN "PackageLibrary" ELSE "PlatformLibrary",
     origin |-> IF p = 1 THEN "FeedProducer" ELSE "PlatformProducer",
     context |-> <<Workspace, p>>]
Answer(n, evidence) ==
    [receipt |-> Receipt(n),
     outcomes |-> [p \in 1..n |-> evidence[p]],
     candidates |-> {Evidence(p) : p \in {m \in 1..n : evidence[m] = "Ready"}}]
Missing == {p \in 1..known : inventories[p] = "Unseen"}
Settled(n) == \A p \in 1..n : inventories[p] # "Unseen"
QuiescentOne == working # 1
QuiescentTwo == working # 2

Init ==
    /\ phase = "Dormant"
    /\ published = 1
    /\ baseline = 0
    /\ known = 0
    /\ notified = FALSE
    /\ working = 0
    /\ inventories = [p \in Members |-> "Unseen"]
    /\ scans = [p \in Members |-> 0]
    /\ declarations \in [Members -> {"Ready", "Rejected"}]
    /\ queries = [q \in Callers |-> "Unused"]
    /\ captured = [q \in Callers |-> 0]
    /\ results = [q \in Callers |-> NoResult]
    /\ bootstrapRace = FALSE
    /\ One!Init
    /\ Two!Init

\* The environment supplies an already committed append. Acquisition and
\* Artifact publication internals are outside this Workspace observation model.
PublishAppend ==
    /\ phase \in {"Dormant", "Starting", "Active"}
    /\ published = 1
    /\ published' = 2
    /\ notified' = (notified \/ phase = "Active")
    /\ bootstrapRace' = (bootstrapRace \/ phase = "Starting")
    /\ UNCHANGED <<phase, baseline, known, working, inventories, scans,
                   declarations, queries, captured, results, oneVars, twoVars>>

Request(q) ==
    /\ phase \in {"Dormant", "Starting", "Active"}
    /\ queries[q] = "Unused"
    /\ queries' = [queries EXCEPT ![q] = "Waiting"]
    /\ captured' = [captured EXCEPT ![q] = published]
    /\ phase' = IF phase = "Dormant" THEN "Starting" ELSE phase
    /\ baseline' = IF phase = "Dormant" THEN published ELSE baseline
    /\ known' = IF phase = "Active" THEN published ELSE known
    /\ UNCHANGED <<published, notified, working, inventories, scans,
                   declarations, results, bootstrapRace, oneVars, twoVars>>

Activate ==
    /\ phase = "Starting"
    /\ phase' = "Active"
    /\ known' = IF Fault = "BootstrapGap" THEN baseline ELSE published
    /\ UNCHANGED <<published, baseline, notified, working, inventories, scans,
                   declarations, queries, captured, results, bootstrapRace,
                   oneVars, twoVars>>

Observe ==
    /\ phase = "Active"
    /\ notified
    /\ known' = published
    /\ notified' = FALSE
    /\ UNCHANGED <<phase, published, baseline, working, inventories, scans,
                   declarations, queries, captured, results, bootstrapRace,
                   oneVars, twoVars>>

StartRead ==
    /\ phase = "Active"
    /\ working = 0
    /\ Missing # {}
    /\ LET p == CHOOSE m \in Missing : \A other \in Missing : m <= other
       IN /\ working' = p
          /\ scans' = [scans EXCEPT ![p] = @ + 1]
    /\ UNCHANGED <<phase, published, baseline, known, notified, inventories,
                   declarations, queries, captured, results, bootstrapRace,
                   oneVars, twoVars>>

FinishRead ==
    /\ working \in Members
    /\ inventories' = [inventories EXCEPT ![working] = declarations[working]]
    /\ working' = 0
    /\ UNCHANGED <<phase, published, baseline, known, notified, scans,
                   declarations, queries, captured, results, bootstrapRace,
                   oneVars, twoVars>>

Respond(q) ==
    /\ phase = "Active"
    /\ queries[q] = "Waiting"
    /\ Settled(captured[q])
    /\ queries' = [queries EXCEPT ![q] = "Done"]
    /\ results' = [results EXCEPT ![q] =
        Answer(IF Fault = "RetagAnswer" THEN known ELSE captured[q], inventories)]
    /\ UNCHANGED <<phase, published, baseline, known, notified, working,
                   inventories, scans, declarations, captured, bootstrapRace,
                   oneVars, twoVars>>

Cancel(q) ==
    /\ queries[q] = "Waiting"
    /\ queries' = [queries EXCEPT ![q] = "Cancelled"]
    /\ UNCHANGED <<phase, published, baseline, known, notified, working,
                   inventories, scans, declarations, captured, results,
                   bootstrapRace, oneVars, twoVars>>

BeginClose ==
    /\ phase \in {"Dormant", "Starting", "Active"}
    /\ phase' = "Closing"
    /\ notified' = FALSE
    /\ queries' = [q \in Callers |->
        IF queries[q] = "Waiting" THEN "Cancelled" ELSE queries[q]]
    /\ One!RequestRelease
    /\ IF published = 2 THEN Two!RequestRelease ELSE UNCHANGED twoVars
    /\ UNCHANGED <<published, baseline, known, working, inventories, scans,
                   declarations, captured, results, bootstrapRace>>

ReleaseOne ==
    /\ phase = "Closing"
    /\ \E r \in ReleaseResults :
        One!CompleteRelease(r, Fault = "EarlyRelease" \/ QuiescentOne)
    /\ UNCHANGED <<localVars, twoVars>>

ReleaseTwo ==
    /\ phase = "Closing"
    /\ \E r \in ReleaseResults :
        Two!CompleteRelease(r, Fault = "EarlyRelease" \/ QuiescentTwo)
    /\ UNCHANGED <<localVars, oneVars>>

FinishClose ==
    /\ phase = "Closing"
    /\ completedOne = GroupOne
    /\ published = 2 => completedTwo = GroupTwo
    /\ working = 0
    /\ phase' = "Closed"
    /\ inventories' = [p \in Members |-> "Unseen"]
    /\ UNCHANGED <<published, baseline, known, notified, working, scans,
                   declarations, queries, captured, results, bootstrapRace,
                   oneVars, twoVars>>

Next ==
    \/ PublishAppend
    \/ \E q \in Callers : Request(q) \/ Respond(q) \/ Cancel(q)
    \/ Activate \/ Observe \/ StartRead \/ FinishRead
    \/ BeginClose \/ ReleaseOne \/ ReleaseTwo \/ FinishClose

SafetySpec == Init /\ [][Next]_vars
Spec ==
    /\ SafetySpec
    /\ WF_vars(Activate)
    /\ WF_vars(Observe)
    /\ WF_vars(StartRead)
    /\ WF_vars(FinishRead)
    /\ WF_vars(ReleaseOne)
    /\ WF_vars(ReleaseTwo)
    /\ WF_vars(FinishClose)
    /\ \A q \in Callers : WF_vars(Respond(q))

TypeOK ==
    /\ phase \in {"Dormant", "Starting", "Active", "Closing", "Closed"}
    /\ published \in Members
    /\ baseline \in 0..2
    /\ known \in 0..published
    /\ notified \in BOOLEAN
    /\ working \in 0..known
    /\ inventories \in [Members -> {"Unseen", "Ready", "Rejected"}]
    /\ scans \in [Members -> 0..2]
    /\ declarations \in [Members -> {"Ready", "Rejected"}]
    /\ queries \in [Callers -> {"Unused", "Waiting", "Done", "Cancelled"}]
    /\ captured \in [Callers -> 0..published]
    /\ bootstrapRace \in BOOLEAN
    /\ One!TypeOK /\ Two!TypeOK

ExactAnswers ==
    \A q \in Callers :
        IF queries[q] = "Done"
        THEN results[q] = Answer(captured[q], declarations)
        ELSE results[q] = NoResult

LazyAndResident ==
    /\ \A p \in Members : scans[p] <= 1
    /\ phase = "Dormant" => scans = [p \in Members |-> 0]
    /\ phase = "Closed" => inventories = [p \in Members |-> "Unseen"]

OwnerReleaseSafety ==
    /\ One!CompletionMatchesRequest /\ One!CompletionCarriesResult
    /\ Two!CompletionMatchesRequest /\ Two!CompletionCarriesResult
    /\ completedOne = GroupOne => QuiescentOne
    /\ completedTwo = GroupTwo => QuiescentTwo

OneRefinesOwner == One!SafetySpec(QuiescentOne)
TwoRefinesOwner == Two!SafetySpec(QuiescentTwo)

MaintenanceProgress ==
    \A p \in Members :
        (phase = "Active" /\ p <= published)
            ~> (phase \in {"Closing", "Closed"} \/ inventories[p] # "Unseen")
RequestProgress ==
    \A q \in Callers :
        queries[q] = "Waiting" ~> queries[q] \in {"Done", "Cancelled"}
CloseProgress == phase = "Closing" ~> phase = "Closed"

NeverCompletedBootstrapRace ==
    ~(bootstrapRace /\ queries[1] = "Done" /\ captured[1] = 1
      /\ inventories[2] # "Unseen" /\ scans = [p \in Members |-> 1])

=============================================================================
