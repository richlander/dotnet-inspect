---------------- MODULE PackageSourceOperationConcurrency ----------------
EXTENDS Naturals, FiniteSets, TLC

CONSTANTS Capacity, IgnoreCapacity, EarlyRelease, DuplicateFetch, RestartCeiling

ASSUME /\ Capacity \in {1, 2}
       /\ {IgnoreCapacity, EarlyRelease, DuplicateFetch, RestartCeiling}
            \subseteq BOOLEAN

Steps == {1, 2, 3}
Keys == {<<"authority", "runtime", "package-a">>,
         <<"authority", "runtime", "package-b">>}
StepKey(step) ==
    IF step = 3
    THEN <<"authority", "runtime", "package-b">>
    ELSE <<"authority", "runtime", "package-a">>

VARIABLES root, operation, expired, steps, source, producers, retained,
          latePublication, parallelObserved

vars == <<root, operation, expired, steps, source, producers, retained,
          latePublication, parallelObserved>>

Active == {step \in Steps : steps[step] = "Active"}
Readers(key) == {step \in Active : StepKey(step) = key}

Init ==
    /\ root = "Open"
    /\ operation = "Unissued"
    /\ expired = FALSE
    /\ steps = [step \in Steps |-> "New"]
    /\ source = [key \in Keys |-> "Empty"]
    /\ producers = [key \in Keys |-> 0]
    /\ retained = {}
    /\ latePublication = FALSE
    /\ parallelObserved = FALSE

Issue ==
    /\ root = "Open"
    /\ operation = "Unissued"
    /\ operation' = "Live"
    /\ UNCHANGED <<root, expired, steps, source, producers, retained,
                   latePublication, parallelObserved>>

Begin(step) ==
    /\ operation = "Live"
    /\ ~expired
    /\ steps[step] = "New"
    /\ (Cardinality(Active) < Capacity \/ IgnoreCapacity)
    /\ LET key == StepKey(step)
           starts == source[key] = "Empty"
                     \/ (DuplicateFetch /\ source[key] = "Running")
       IN /\ steps' = [steps EXCEPT ![step] = "Active"]
          /\ source' = IF starts
                       THEN [source EXCEPT ![key] = "Running"]
                       ELSE source
          /\ producers' = IF starts
                          THEN [producers EXCEPT ![key] = @ + 1]
                          ELSE producers
    /\ parallelObserved' = (parallelObserved \/ Cardinality(Active) > 0)
    /\ UNCHANGED <<root, operation, expired, retained, latePublication>>

FinishFetch(key) ==
    /\ source[key] = "Running"
    /\ source' \in {
         [source EXCEPT ![key] = "Succeeded"],
         [source EXCEPT ![key] = "Failed"]}
    /\ producers' = [producers EXCEPT ![key] = 0]
    /\ UNCHANGED <<root, operation, expired, steps, retained,
                   latePublication, parallelObserved>>

FinishStep(step, authoritative) ==
    /\ step \in Active
    /\ LET key == StepKey(step)
           success == authoritative /\ source[key] = "Succeeded"
                      /\ (~expired \/ RestartCeiling)
           keep == retained \cup (IF success THEN {key} ELSE {})
       IN /\ source[key] \in {"Succeeded", "Failed"}
          /\ steps' = [steps EXCEPT ![step] = "Done"]
          /\ retained' = keep
          /\ source' = IF Readers(key) = {step} /\ key \notin keep
                       THEN [source EXCEPT ![key] = "Empty"]
                       ELSE source
          /\ latePublication' = (latePublication \/ (success /\ expired))
    /\ UNCHANGED <<root, operation, expired, producers, parallelObserved>>

Expire ==
    /\ operation = "Live"
    /\ ~expired
    /\ expired' = TRUE
    /\ UNCHANGED <<root, operation, steps, source, producers, retained,
                   latePublication, parallelObserved>>

Retire ==
    /\ root = "Open"
    /\ root' = "Settling"
    /\ UNCHANGED <<operation, expired, steps, source, producers, retained,
                   latePublication, parallelObserved>>

Release ==
    /\ operation = "Live"
    /\ (Active = {} \/ EarlyRelease)
    /\ operation' = "Released"
    /\ retained' = {}
    /\ UNCHANGED <<root, expired, steps, source, producers,
                   latePublication, parallelObserved>>

Settle ==
    /\ root = "Settling"
    /\ operation # "Live"
    /\ root' = "Settled"
    /\ UNCHANGED <<operation, expired, steps, source, producers, retained,
                   latePublication, parallelObserved>>

Terminal ==
    /\ root = "Settled"
    /\ UNCHANGED vars

FinishSomeStep(step) ==
    \E authoritative \in BOOLEAN : FinishStep(step, authoritative)

Next ==
    \/ Issue
    \/ \E step \in Steps : Begin(step)
    \/ \E key \in Keys : FinishFetch(key)
    \/ \E step \in Steps : FinishSomeStep(step)
    \/ Expire
    \/ Retire
    \/ Release
    \/ Settle
    \/ Terminal

Spec ==
    /\ Init
    /\ [][Next]_vars
    /\ WF_vars(Retire)
    /\ WF_vars(Release)
    /\ WF_vars(Settle)
    /\ \A key \in Keys : WF_vars(FinishFetch(key))
    /\ \A step \in Steps : WF_vars(FinishSomeStep(step))

CapacityIsBounded == Cardinality(Active) <= Capacity
ReleaseRequiresSettledWork == operation = "Released" => Active = {}
QuiescenceRetainsOperation == root = "Settled" => Active = {}
OneProducerPerKey == \A key \in Keys : producers[key] <= 1
ProducersHaveReaders ==
    \A key \in Keys : producers[key] > 0 => Readers(key) # {}
RetainedEvidenceSucceeded ==
    \A key \in retained : source[key] = "Succeeded"
NoLateSuccess == ~latePublication
RootEventuallySettles == <>(root = "Settled")
ParallelNotObserved == ~parallelObserved

=============================================================================
