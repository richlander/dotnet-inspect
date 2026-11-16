---------------- MODULE DependencyTraversalPipeline ----------------
EXTENDS Naturals, FiniteSets, Sequences

CONSTANTS Capacity, ManifestBudget, BrokenCapacity, BrokenBudget,
          BrokenOrder, BrokenRelease

Candidates == 1..2
Resolution(i) == <<0, i>>
Manifest(i) == <<1, i>>
Jobs == {Resolution(i) : i \in Candidates}
        \cup {Manifest(i) : i \in Candidates}

VARIABLES queued, started, active, finished, published, reserved,
          stopping, released, sawPipeline

vars == <<queued, started, active, finished, published, reserved,
          stopping, released, sawPipeline>>

SeqSet(s) == {s[i] : i \in 1..Len(s)}

Init ==
    /\ queued = <<Resolution(1), Resolution(2)>>
    /\ started = {}
    /\ active = {}
    /\ finished = {}
    /\ published = <<>>
    /\ reserved = <<>>
    /\ stopping = FALSE
    /\ released = FALSE
    /\ sawPipeline = FALSE

Start ==
    /\ ~stopping /\ ~released
    /\ Len(queued) > 0
    /\ (BrokenCapacity \/ Cardinality(active) < Capacity)
    /\ LET job == Head(queued) IN
        /\ started' = started \cup {job}
        /\ active' = active \cup {job}
        /\ sawPipeline' = (sawPipeline
             \/ (job = Manifest(1) /\ Resolution(2) \in active))
    /\ queued' = Tail(queued)
    /\ UNCHANGED <<finished, published, reserved, stopping, released>>

Finish(job) ==
    /\ ~released
    /\ job \in active
    /\ active' = active \ {job}
    /\ finished' = finished \cup {job}
    /\ UNCHANGED <<queued, started, published, reserved,
                   stopping, released, sawPipeline>>

Publish(i) ==
    /\ ~stopping /\ ~released
    /\ Resolution(i) \in finished
    /\ i \notin SeqSet(published)
    /\ (BrokenOrder \/ i = Len(published) + 1)
    /\ published' = Append(published, i)
    /\ IF BrokenBudget \/ Len(reserved) < ManifestBudget
       THEN /\ reserved' = Append(reserved, i)
            /\ queued' = Append(queued, Manifest(i))
       ELSE UNCHANGED <<reserved, queued>>
    /\ UNCHANGED <<started, active, finished,
                   stopping, released, sawPipeline>>

Stop ==
    /\ ~stopping /\ ~released
    /\ stopping' = TRUE
    /\ queued' = <<>>
    /\ UNCHANGED <<started, active, finished, published, reserved,
                   released, sawPipeline>>

Release ==
    /\ ~released
    /\ (stopping \/ Len(published) = 2)
    /\ (BrokenRelease \/ (active = {} /\ Len(queued) = 0))
    /\ released' = TRUE
    /\ UNCHANGED <<queued, started, active, finished, published,
                   reserved, stopping, sawPipeline>>

Next ==
    \/ Start
    \/ \E job \in Jobs : Finish(job)
    \/ \E i \in Candidates : Publish(i)
    \/ Stop
    \/ Release
    \/ (released /\ UNCHANGED vars)

Spec ==
    /\ Init
    /\ [][Next]_vars
    /\ WF_vars(Start)
    /\ WF_vars(Release)
    /\ \A job \in Jobs : WF_vars(Finish(job))
    /\ \A i \in Candidates : WF_vars(Publish(i))

TypeOK ==
    /\ queued \in Seq(Jobs)
    /\ started \subseteq Jobs /\ active \subseteq Jobs
    /\ finished \subseteq Jobs
    /\ published \in Seq(Candidates) /\ reserved \in Seq(Candidates)
    /\ stopping \in BOOLEAN /\ released \in BOOLEAN
    /\ sawPipeline \in BOOLEAN

CapacityIsShared == Cardinality(active) <= Capacity
BudgetIsStable ==
    /\ Len(reserved) <= ManifestBudget
    /\ reserved = [i \in 1..Len(reserved) |-> i]
PublicationIsOrdered == published = [i \in 1..Len(published) |-> i]
DownstreamIsAdmitted ==
    \A i \in Candidates :
        Manifest(i) \in started => i \in SeqSet(reserved) \cap SeqSet(published)
WorkIsAccounted ==
    /\ active \cap finished = {}
    /\ active \cup finished = started
    /\ SeqSet(queued) \cap started = {}
ReleaseRequiresSettledWork == released => (active = {} /\ Len(queued) = 0)
EventuallyReleased == <>released
PipelineNotObserved == ~sawPipeline
====================================================================
