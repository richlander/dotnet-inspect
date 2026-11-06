---------------- MODULE PackageSourceCandidateListing ----------------
EXTENDS Naturals, FiniteSets, Sequences, TLC

CONSTANT IgnoreUnknown
ASSUME IgnoreUnknown \in BOOLEAN

Keys == {<<"operation", "authority-a", "runtime-a", "package">>,
         <<"operation", "authority-b", "runtime-b", "package">>}
Versions == 1..3
Requests == 1..2
Unknown == "Unknown"
Reported(key) == IF key[2] = "authority-a" THEN Versions ELSE {1, 3}
Preference(request) == IF request = 1 THEN <<1, 2, 3>> ELSE <<3, 2>>
Page(version) == IF version < 3 THEN 1 ELSE 2

VARIABLES truth, facts, active, owner, phase, result, starts
vars == <<truth, facts, active, owner, phase, result, starts>>

Best(request, candidates) ==
    LET positions == {i \in 1..Len(Preference(request)) :
                          Preference(request)[i] \in candidates}
    IN IF positions = {}
       THEN 0
       ELSE Preference(request)[CHOOSE i \in positions :
                                    \A j \in positions : i <= j]

Potential ==
    {v \in Versions : \E key \in Keys :
        /\ v \in Reported(key)
        /\ (facts[key][v] = "Listed"
            \/ (~IgnoreUnknown /\ facts[key][v] = Unknown))}

Needs(key, version) ==
    IF version \in Reported(key)
    THEN facts[key][version] = Unknown
    ELSE FALSE

Oracle(request) ==
    Best(request, {v \in Versions : \E key \in Keys :
        v \in Reported(key) /\ truth[key][v] = "Listed"})

Init ==
    /\ truth \in [Keys -> [Versions -> {"Listed", "Unlisted"}]]
    /\ facts = [key \in Keys |-> [v \in Versions |->
          IF key[2] = "authority-a" /\ v = 3
          THEN truth[key][v] ELSE Unknown]]
    /\ active = [key \in Keys |-> 0]
    /\ owner = [key \in Keys |-> 0]
    /\ phase = [request \in Requests |-> "Search"]
    /\ result = [request \in Requests |-> 0]
    /\ starts = [key \in Keys |-> [page \in 1..2 |-> 0]]

Emit(request) ==
    /\ phase[request] = "Search"
    /\ LET best == Best(request, Potential)
       IN /\ \A key \in Keys : ~Needs(key, best)
          /\ result' = [result EXCEPT ![request] = best]
    /\ phase' = [phase EXCEPT ![request] = "Done"]
    /\ UNCHANGED <<truth, facts, active, owner, starts>>

Start(request, key) ==
    /\ phase[request] = "Search"
    /\ active[key] = 0
    /\ LET best == Best(request, Potential)
       IN /\ Needs(key, best)
          /\ active' = [active EXCEPT ![key] = Page(best)]
          /\ starts' = [starts EXCEPT ![key][Page(best)] = @ + 1]
    /\ owner' = [owner EXCEPT ![key] = request]
    /\ phase' = [phase EXCEPT ![request] = "Waiting"]
    /\ UNCHANGED <<truth, facts, result>>

Complete(key) ==
    /\ active[key] # 0
    /\ facts' = [facts EXCEPT ![key] =
          [v \in Versions |->
              IF v \in Reported(key) /\ Page(v) = active[key]
              THEN truth[key][v] ELSE facts[key][v]]]
    /\ phase' = [phase EXCEPT ![owner[key]] = "Search"]
    /\ active' = [active EXCEPT ![key] = 0]
    /\ owner' = [owner EXCEPT ![key] = 0]
    /\ UNCHANGED <<truth, result, starts>>

Next ==
    \/ \E request \in Requests : Emit(request)
    \/ \E request \in Requests, key \in Keys : Start(request, key)
    \/ \E key \in Keys : Complete(key)

Spec ==
    /\ Init
    /\ [][Next]_vars
    /\ \A request \in Requests : WF_vars(Emit(request))
    /\ \A request \in Requests, key \in Keys : WF_vars(Start(request, key))
    /\ \A key \in Keys : WF_vars(Complete(key))

TypeOK ==
    /\ truth \in [Keys -> [Versions -> {"Listed", "Unlisted"}]]
    /\ facts \in [Keys -> [Versions -> {"Listed", "Unlisted", Unknown}]]
    /\ active \in [Keys -> 0..2]
    /\ owner \in [Keys -> 0..2]
    /\ phase \in [Requests -> {"Search", "Waiting", "Done"}]
    /\ result \in [Requests -> 0..3]
    /\ starts \in [Keys -> [1..2 -> Nat]]

KnownFactsAreSound ==
    \A key \in Keys, v \in Versions :
        facts[key][v] # Unknown => facts[key][v] = truth[key][v]

ResultMatchesCompleteListing ==
    \A request \in Requests :
        phase[request] = "Done" => result[request] = Oracle(request)

NoDuplicatePageAcquisition ==
    \A key \in Keys, page \in 1..2 : starts[key][page] <= 1

AllRequestsSettle == <>(\A request \in Requests : phase[request] = "Done")
=============================================================================
