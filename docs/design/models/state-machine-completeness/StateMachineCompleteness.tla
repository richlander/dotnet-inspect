---------------------- MODULE StateMachineCompleteness ----------------------
(***************************************************************************)
(* A model of the completeness invariants in                               *)
(* docs/design/state-machine-relationship-index.md.                        *)
(*                                                                         *)
(* This models the CONTRACT, not the implementation. It abstracts away     *)
(* metadata decoding, signature matching, and every specific budget. What  *)
(* it keeps is the part that is genuinely stateful and therefore hard to   *)
(* check by reading prose:                                                 *)
(*                                                                         *)
(*   - construction visits structural machines one at a time and may fail  *)
(*     for the whole module at any point;                                  *)
(*   - rejections merge into shared components as claims are discovered,   *)
(*     in an order the compiler chooses, not one we control; and           *)
(*   - a whole-module failure must reject every machine, while a per-claim *)
(*     refusal rejects only what its claim reaches.                        *)
(*                                                                         *)
(* The properties checked here are C1, C2, C3 and C5 of that document,     *)
(* plus absorption of failure. C4 is about what a consumer may infer and   *)
(* C6 is about an external observer; neither is a statement about system   *)
(* state, so neither is modeled.                                           *)
(***************************************************************************)

EXTENDS FiniteSets, Naturals

CONSTANTS
    MachineCount, \* how many structural state machines the image holds
    MaxBudget,    \* construction steps available before exhaustion
    FailureMode,  \* how whole-module failure rewrites results
    MergeMode,    \* how far a merge propagates
    FinishMode    \* what construction requires before publishing

(***************************************************************************)
(* The three mode constants exist so that each deliberate counterexample is *)
(* a committed, re-runnable configuration rather than a described edit. A   *)
(* property no mutation can falsify is not checking anything, and a README  *)
(* table asserting that one was falsified is exactly the ungated prose      *)
(* claim this model was written to eliminate. See README.md.                *)
(*                                                                          *)
(*   FailureMode = "Total"              whole-module failure rejects all    *)
(*               = "PreserveClassified" leaves earlier results standing     *)
(*               = "ReportAbsent"       reports Absent instead of Rejected  *)
(*                                                                          *)
(*   MergeMode   = "Component"          merge rewrites the whole component  *)
(*               = "NamedPairOnly"      merge rewrites only the two named   *)
(*                                                                          *)
(*   FinishMode  = "Guarded"            publish only when visited and merged*)
(*               = "AllowUnvisited"     publish with rows unclassified      *)
(*               = "AllowUnmerged"      publish without merging             *)
(***************************************************************************)
ASSUME
    /\ MachineCount \in Nat \ {0}
    /\ MaxBudget \in Nat
    /\ FailureMode \in {"Total", "PreserveClassified", "ReportAbsent"}
    /\ MergeMode \in {"Component", "NamedPairOnly"}
    /\ FinishMode \in {"Guarded", "AllowUnvisited", "AllowUnmerged"}

(***************************************************************************)
(* Machines are indices rather than opaque names purely so that component  *)
(* ids have a finite domain TLC can enumerate. Nothing below depends on    *)
(* their order.                                                           *)
(***************************************************************************)
Machines == 1..MachineCount

(***************************************************************************)
(* `SharedPairs` is the input the merging exists to serve: two rejections   *)
(* that name the same kickoff or the same claimed type. Modeling it is what *)
(* lets the model check that merging actually happens, and not merely that  *)
(* it is self-consistent when it does. An earlier revision omitted it, and  *)
(* an index that never merged anything satisfied component agreement        *)
(* vacuously -- see the AllowUnmerged configuration.                        *)
(*                                                                          *)
(* Consecutive machines share a contributor, giving one chain. A chain is   *)
(* the interesting topology: merging must be transitive, so 1 and 3 end up  *)
(* together even though no claim names both.                                *)
(***************************************************************************)
SharedPairs == {<<i, i + 1>> : i \in 1..(MachineCount - 1)}

MustMerge(a, b) ==
    \/ <<a, b>> \in SharedPairs
    \/ <<b, a>> \in SharedPairs

(***************************************************************************)
(* Results a query can return. `Unclassified` is not a result the index can *)
(* publish -- it is the modeling stand-in for a row construction has not    *)
(* reached yet, and C1 requires that none survives into a terminal state.   *)
(***************************************************************************)
Results == {"Resolved", "Rejected", "Absent", "Unclassified"}

\* The two ways construction can fail for the whole module.
FailureKinds == {"Malformed", "BudgetExceeded"}

(***************************************************************************)
(* Evidence a rejection carries, as small integers so that combining them   *)
(* is obviously associative and commutative. Read 1, 2, 3 as three distinct *)
(* per-claim refusal kinds such as Unresolved, Ambiguous, and Duplicate.    *)
(* Their identity does not matter; that a merged component must agree on    *)
(* one of them is the whole point.                                          *)
(***************************************************************************)
RejectionKinds == 1..3
NoEvidence == 0

Phases == {"Building", "Built", "Failed"}

VARIABLES
    phase,        \* Building, Built, or Failed
    kind,         \* failure kind once phase = "Failed", else "None"
    result,       \* Machines -> Results
    component,    \* Machines -> component id; equal ids merge as one rejection
    evidence,     \* Machines -> rejection evidence, NoEvidence when not rejected
    visited,      \* machines construction has already classified
    budget        \* remaining construction steps

vars == <<phase, kind, result, component, evidence, visited, budget>>

(***************************************************************************)
(* Component identity. A rejection component is represented by the set of   *)
(* machines sharing an id. Merging is union: the merged component takes the *)
(* smallest contributing id, which makes the operation associative and      *)
(* commutative and is what C5's order independence rests on.                *)
(***************************************************************************)
ComponentOf(m) == {n \in Machines : component[n] = component[m]}

MinId(S) == CHOOSE i \in S : \A j \in S : i <= j

TypeOK ==
    /\ phase \in Phases
    /\ kind \in FailureKinds \cup {"None"}
    /\ result \in [Machines -> Results]
    /\ component \in [Machines -> Machines]
    /\ evidence \in [Machines -> RejectionKinds \cup {NoEvidence}]
    /\ visited \subseteq Machines
    /\ budget \in 0..MaxBudget

(***************************************************************************)
(* Every machine starts in its own rejection component; merging is the only *)
(* thing that brings two together.                                          *)
(***************************************************************************)
Init ==
    /\ phase = "Building"
    /\ kind = "None"
    /\ result = [m \in Machines |-> "Unclassified"]
    /\ component = [m \in Machines |-> m]
    /\ evidence = [m \in Machines |-> NoEvidence]
    /\ visited = {}
    /\ budget = MaxBudget

-----------------------------------------------------------------------------
(***************************************************************************)
(* Construction steps. Each visits one not-yet-visited machine and spends   *)
(* one unit of budget. The order is unconstrained: TLC explores every       *)
(* interleaving, which is what makes C5's order independence a real check   *)
(* rather than an assertion about one traversal.                           *)
(***************************************************************************)

\* The claim authenticates: this machine resolves.
ResolveOne(m) ==
    /\ phase = "Building"
    /\ m \notin visited
    /\ budget > 0
    /\ result' = [result EXCEPT ![m] = "Resolved"]
    /\ visited' = visited \cup {m}
    /\ budget' = budget - 1
    /\ UNCHANGED <<phase, kind, component, evidence>>

\* No claim names this machine at all: it is absent, not refused.
AbsentOne(m) ==
    /\ phase = "Building"
    /\ m \notin visited
    /\ budget > 0
    /\ result' = [result EXCEPT ![m] = "Absent"]
    /\ visited' = visited \cup {m}
    /\ budget' = budget - 1
    /\ UNCHANGED <<phase, kind, component, evidence>>

\* A claim names this machine and fails its role requirements. This is the
\* per-claim refusal of C3: narrow, and it does not disturb its neighbours.
RejectOne(m, e) ==
    /\ phase = "Building"
    /\ m \notin visited
    /\ budget > 0
    /\ e \in RejectionKinds
    /\ result' = [result EXCEPT ![m] = "Rejected"]
    /\ evidence' = [evidence EXCEPT ![m] = e]
    /\ visited' = visited \cup {m}
    /\ budget' = budget - 1
    /\ UNCHANGED <<phase, kind, component>>

(***************************************************************************)
(* Two rejections that share a kickoff or a claimed type merge. Every entry *)
(* in the merged component must end up with the same result, which is why   *)
(* the action rewrites all of them together rather than only the two named. *)
(***************************************************************************)
MergeRejections(m, n) ==
    /\ phase = "Building"
    /\ m # n
    /\ MustMerge(m, n)
    /\ result[m] = "Rejected"
    /\ result[n] = "Rejected"
    /\ component[m] # component[n]
    /\ LET merged == ComponentOf(m) \cup ComponentOf(n)
           id     == MinId({component[m], component[n]})
           ev     == MinId({evidence[x] : x \in merged})
           touched == IF MergeMode = "Component" THEN merged ELSE {m, n}
       IN  /\ component' = [x \in Machines |->
                              IF x \in merged THEN id ELSE component[x]]
           /\ evidence' = [x \in Machines |->
                              IF x \in touched THEN ev ELSE evidence[x]]
    /\ UNCHANGED <<phase, kind, result, visited, budget>>

(***************************************************************************)
(* Whole-module failure. This is C3's total case: it rejects every machine, *)
(* including ones construction had already resolved and ones it never       *)
(* reached. That total overwrite is the invariant -- an implementation that *)
(* left an earlier `Resolved` standing would produce the partial shape C3   *)
(* says belongs only to per-claim refusal.                                  *)
(***************************************************************************)
FailModule(k) ==
    /\ phase = "Building"
    /\ phase' = "Failed"
    /\ kind' = k
    /\ result' = [m \in Machines |->
                    CASE FailureMode = "ReportAbsent" -> "Absent"
                      [] FailureMode = "PreserveClassified" ->
                            IF result[m] = "Unclassified" THEN "Rejected" ELSE result[m]
                      [] OTHER -> "Rejected"]
    \* The whole module failed, so every machine carries the same evidence:
    \* the module-level kind, not a per-claim one.
    /\ evidence' = [m \in Machines |-> NoEvidence]
    /\ visited' = Machines
    /\ UNCHANGED <<component, budget>>

Malform == FailModule("Malformed")

\* Budget exhaustion is the same transition, reachable only when spent.
ExhaustBudget ==
    /\ budget = 0
    /\ visited # Machines
    /\ FailModule("BudgetExceeded")

\* Construction completes once every machine has been classified.
Finish ==
    /\ phase = "Building"
    /\ (FinishMode = "AllowUnvisited" \/ visited = Machines)
    \* Nothing is left to merge. Construction does not publish a component
    \* whose entries have not yet been brought into agreement.
    /\ ~(\E a, b \in Machines :
            /\ FinishMode # "AllowUnmerged"
            /\ a # b
            /\ MustMerge(a, b)
            /\ result[a] = "Rejected"
            /\ result[b] = "Rejected"
            /\ component[a] # component[b])
    /\ phase' = "Built"
    /\ UNCHANGED <<kind, result, component, evidence, visited, budget>>

Next ==
    \/ \E m \in Machines : ResolveOne(m)
    \/ \E m \in Machines : AbsentOne(m)
    \/ \E m \in Machines, e \in RejectionKinds : RejectOne(m, e)
    \/ \E m, n \in Machines : MergeRejections(m, n)
    \/ Malform
    \/ ExhaustBudget
    \/ Finish
    \* Terminal states stutter so the temporal properties are well formed.
    \/ /\ phase \in {"Built", "Failed"}
       /\ UNCHANGED vars

Spec == Init /\ [][Next]_vars /\ WF_vars(Next)

-----------------------------------------------------------------------------
(***************************************************************************)
(* Properties.                                                             *)
(***************************************************************************)

Terminal == phase \in {"Built", "Failed"}

(***************************************************************************)
(* C1 -- Totality. In a terminal state every machine carries exactly one    *)
(* publishable result. `Unclassified` is not publishable, so its absence    *)
(* is the whole claim: no row was silently dropped.                         *)
(***************************************************************************)
C1_Totality ==
    Terminal => \A m \in Machines : result[m] \in {"Resolved", "Rejected", "Absent"}

(***************************************************************************)
(* C2 -- Failure is never success-shaped. A failed construction carries a   *)
(* typed kind, and does not present as a clean index that simply found      *)
(* nothing. The second conjunct is the sharp one: `Absent` everywhere is    *)
(* exactly what an empty successful index would look like.                  *)
(***************************************************************************)
C2_FailureIsTyped ==
    /\ (phase = "Failed") => kind \in FailureKinds
    /\ (phase = "Failed") => \A m \in Machines : result[m] # "Absent"

(***************************************************************************)
(* C3 -- Whole-module failure rejects the whole module. Total, not partial: *)
(* nothing resolves, and nothing is left absent.                            *)
(***************************************************************************)
C3_FailureRejectsAll ==
    (phase = "Failed") => \A m \in Machines : result[m] = "Rejected"

(***************************************************************************)
(* C5 -- Merged rejections are confluent. Every machine sharing a component *)
(* carries the same result, whatever order the merges happened in. TLC      *)
(* explores all interleavings, so this holds over every discovery order.    *)
(***************************************************************************)
C5_ComponentsAgree ==
    \A m, n \in Machines :
        (component[m] = component[n])
            => /\ result[m] = result[n]
               /\ evidence[m] = evidence[n]

(***************************************************************************)
(* C5b -- Merging happens. Two rejections that share a contributor end up   *)
(* in one component. C5 says a component agrees with itself; without this,  *)
(* an index that merged nothing would satisfy C5 vacuously.                 *)
(***************************************************************************)
C5_SharedRejectionsMerge ==
    (phase = "Built") =>
        \A a, b \in Machines :
            (/\ MustMerge(a, b)
             /\ result[a] = "Rejected"
             /\ result[b] = "Rejected")
                => component[a] = component[b]

(***************************************************************************)
(* Failure absorbs: once construction has failed it never reports success,  *)
(* and its kind never changes underneath a consumer.                        *)
(***************************************************************************)
FailureIsAbsorbing ==
    [][ (phase = "Failed") => (phase' = "Failed" /\ kind' = kind) ]_vars

(***************************************************************************)
(* Construction terminates. Not a safety property, but it is what makes the *)
(* invariants above non-vacuous: a spec that never reached a terminal state *)
(* would satisfy C1 trivially.                                              *)
(***************************************************************************)
EventuallyTerminal == <>Terminal

=============================================================================
