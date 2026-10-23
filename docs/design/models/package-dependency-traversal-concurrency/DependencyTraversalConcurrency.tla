---------------- MODULE DependencyTraversalConcurrency ----------------
EXTENDS Naturals, FiniteSets, Sequences

CONSTANTS Capacity, Budget, BrokenCapacity, BrokenBudget,
          BrokenOrder, BrokenRelease

Jobs == 1..4
Limit == IF Budget < Cardinality(Jobs) THEN Budget ELSE Cardinality(Jobs)

VARIABLES started, active, ready, replayed, discarded, stopping,
          released, sawOverlap, sawRefill

vars == <<started, active, ready, replayed, discarded, stopping,
          released, sawOverlap, sawRefill>>

ReplayedSet == {replayed[i] : i \in 1..Len(replayed)}

Init ==
    /\ started = 0
    /\ active = {}
    /\ ready = {}
    /\ replayed = <<>>
    /\ discarded = {}
    /\ stopping = FALSE
    /\ released = FALSE
    /\ sawOverlap = FALSE
    /\ sawRefill = FALSE

Start ==
    /\ ~stopping /\ ~released
    /\ started < Cardinality(Jobs)
    /\ (BrokenBudget \/ started < Limit)
    /\ (BrokenCapacity \/ Cardinality(active) < Capacity)
    /\ started' = started + 1
    /\ active' = active \cup {started + 1}
    /\ sawOverlap' = (sawOverlap \/ Cardinality(active') > 1)
    /\ sawRefill' = (sawRefill \/ (started >= Capacity /\ 1 \in active))
    /\ UNCHANGED <<ready, replayed, discarded, stopping, released>>

Finish(j) ==
    /\ ~released
    /\ j \in active
    /\ active' = active \ {j}
    /\ ready' = ready \cup {j}
    /\ UNCHANGED <<started, replayed, discarded, stopping,
                   released, sawOverlap, sawRefill>>

Replay(j) ==
    /\ ~stopping /\ ~released
    /\ j \in ready
    /\ (BrokenOrder \/ j = Len(replayed) + 1)
    /\ ready' = ready \ {j}
    /\ replayed' = Append(replayed, j)
    /\ UNCHANGED <<started, active, discarded, stopping,
                   released, sawOverlap, sawRefill>>

Stop ==
    /\ ~stopping /\ ~released
    /\ stopping' = TRUE
    /\ UNCHANGED <<started, active, ready, replayed, discarded,
                   released, sawOverlap, sawRefill>>

Discard(j) ==
    /\ stopping /\ ~released
    /\ j \in ready
    /\ ready' = ready \ {j}
    /\ discarded' = discarded \cup {j}
    /\ UNCHANGED <<started, active, replayed, stopping,
                   released, sawOverlap, sawRefill>>

Release ==
    /\ ~released
    /\ (stopping \/ started = Limit)
    /\ (BrokenRelease \/ (active = {} /\ ready = {}))
    /\ released' = TRUE
    /\ UNCHANGED <<started, active, ready, replayed, discarded,
                   stopping, sawOverlap, sawRefill>>

Next ==
    \/ Start
    \/ \E j \in Jobs : Finish(j)
    \/ \E j \in Jobs : Replay(j)
    \/ Stop
    \/ \E j \in Jobs : Discard(j)
    \/ Release
    \/ (released /\ UNCHANGED vars)

Spec ==
    /\ Init
    /\ [][Next]_vars
    /\ WF_vars(Start)
    /\ WF_vars(Release)
    /\ \A j \in Jobs : WF_vars(Finish(j))
    /\ \A j \in Jobs : WF_vars(Replay(j))
    /\ \A j \in Jobs : WF_vars(Discard(j))

TypeOK ==
    /\ started \in 0..Cardinality(Jobs)
    /\ active \subseteq Jobs /\ ready \subseteq Jobs
    /\ replayed \in Seq(Jobs) /\ discarded \subseteq Jobs
    /\ stopping \in BOOLEAN /\ released \in BOOLEAN
    /\ sawOverlap \in BOOLEAN /\ sawRefill \in BOOLEAN

CapacityIsBounded == Cardinality(active) <= Capacity
WorkIsBounded == started <= Limit
AdmissionIsOrdered == replayed = [i \in 1..Len(replayed) |-> i]
ReleaseRequiresSettledWork == released => (active = {} /\ ready = {})
WorkIsAccounted ==
    /\ active \cup ready \cup ReplayedSet \cup discarded = 1..started
    /\ Cardinality(active) + Cardinality(ready)
        + Len(replayed) + Cardinality(discarded) = started
EventuallyReleased == <>released
OverlapNotObserved == ~sawOverlap
RefillNotObserved == ~sawRefill
======================================================================
