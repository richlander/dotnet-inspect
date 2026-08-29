---------------------- MODULE StateMachineCompleteness ----------------------
(***************************************************************************)
(* C1-C3 of docs/design/state-machine-relationship-index.md.               *)
(*                                                                         *)
(* This model owns structural-machine classification and whole-module      *)
(* failure. Rejection-component merging has a different unit of state --   *)
(* published rejections connected by tagged merge keys -- and lives in     *)
(* RejectionComponentMerge.tla. Keeping those domains separate prevents a  *)
(* merge key such as a kickoff or claimed name from being mistaken for a   *)
(* structural state machine.                                               *)
(***************************************************************************)

EXTENDS Naturals

CONSTANTS
    MachineCount, \* structural state machines in the image
    MaxBudget,    \* classification steps before exhaustion
    FailureMode,  \* how whole-module failure rewrites results
    FailureMutationMode, \* whether a published failure may change
    FinishMode    \* what construction requires before publishing

(***************************************************************************)
(* Modes make each deliberate counterexample a committed configuration.    *)
(*                                                                         *)
(*   FailureMode = "Total"              reject the whole module             *)
(*               = "PreserveClassified" leave earlier results standing      *)
(*               = "ReportAbsent"       report Absent instead of Rejected   *)
(*                                                                         *)
(*   FailureMutationMode = "Stable"      published failure is immutable      *)
(*                       = "RewriteDetail" mutate it after publication        *)
(*                                                                         *)
(*   FinishMode  = "Guarded"            require every machine classified    *)
(*               = "AllowUnvisited"     publish unclassified rows           *)
(*               = "DropAsAbsent"       publish a lost row as Absent         *)
(***************************************************************************)
ASSUME
    /\ MachineCount \in Nat \ {0}
    /\ MaxBudget \in Nat
    /\ FailureMode \in {"Total", "PreserveClassified", "ReportAbsent"}
    /\ FailureMutationMode \in {"Stable", "RewriteDetail"}
    /\ FinishMode \in {"Guarded", "AllowUnvisited", "DropAsAbsent"}

Machines == 1..MachineCount
Results == {"Resolved", "Rejected", "Absent", "Unclassified"}
Truths == {"Resolvable", "Refused", "NoClaim"}
FailureKinds == {"Malformed", "BudgetExceeded"}
Phases == {"Building", "Built", "Failed"}

Expected(t) ==
    CASE t = "Resolvable" -> "Resolved"
      [] t = "Refused"    -> "Rejected"
      [] OTHER            -> "Absent"

VARIABLES
    truth,        \* independently recomputed classification
    phase,        \* Building, Built, or Failed
    kind,         \* whole-module failure kind, or None
    failureDetail,\* abstract published failure detail, or zero
    result,       \* published result per structural machine
    visited,      \* structural machines construction reached
    budget        \* remaining classification steps

vars == <<truth, phase, kind, failureDetail, result, visited, budget>>

TypeOK ==
    /\ truth \in [Machines -> Truths]
    /\ phase \in Phases
    /\ kind \in FailureKinds \cup {"None"}
    /\ failureDetail \in 0..2
    /\ result \in [Machines -> Results]
    /\ visited \subseteq Machines
    /\ budget \in 0..MaxBudget

Init ==
    /\ truth \in [Machines -> Truths]
    /\ phase = "Building"
    /\ kind = "None"
    /\ failureDetail = 0
    /\ result = [m \in Machines |-> "Unclassified"]
    /\ visited = {}
    /\ budget = MaxBudget

ClassifyOne(m, expectedTruth, classification) ==
    /\ phase = "Building"
    /\ m \notin visited
    /\ budget > 0
    /\ truth[m] = expectedTruth
    /\ result' = [result EXCEPT ![m] = classification]
    /\ visited' = visited \cup {m}
    /\ budget' = budget - 1
    /\ UNCHANGED <<truth, phase, kind, failureDetail>>

ResolveOne(m) == ClassifyOne(m, "Resolvable", "Resolved")
RejectOne(m) == ClassifyOne(m, "Refused", "Rejected")
AbsentOne(m) == ClassifyOne(m, "NoClaim", "Absent")

(***************************************************************************)
(* A whole-module failure overwrites every result, including classifications *)
(* already recorded and machines construction had not reached.             *)
(***************************************************************************)
FailModule(k) ==
    /\ phase = "Building"
    /\ phase' = "Failed"
    /\ kind' = k
    /\ failureDetail' = 1
    /\ result' = [m \in Machines |->
                    CASE FailureMode = "ReportAbsent" -> "Absent"
                      [] FailureMode = "PreserveClassified" ->
                            IF result[m] = "Unclassified" THEN "Rejected"
                            ELSE result[m]
                      [] OTHER -> "Rejected"]
    /\ visited' = Machines
    /\ UNCHANGED <<truth, budget>>

Malform == FailModule("Malformed")

ExhaustBudget ==
    /\ budget = 0
    /\ visited # Machines
    /\ FailModule("BudgetExceeded")

MutateFailedDetail ==
    /\ phase = "Failed"
    /\ FailureMutationMode = "RewriteDetail"
    /\ failureDetail' = 2
    /\ UNCHANGED <<truth, phase, kind, result, visited, budget>>

Finish ==
    /\ phase = "Building"
    /\ (FinishMode \in {"AllowUnvisited", "DropAsAbsent"} \/ visited = Machines)
    /\ phase' = "Built"
    /\ result' = IF FinishMode = "DropAsAbsent"
                 THEN [m \in Machines |->
                         IF result[m] = "Unclassified" THEN "Absent"
                         ELSE result[m]]
                 ELSE result
    /\ UNCHANGED <<truth, kind, failureDetail, visited, budget>>

Next ==
    \/ \E m \in Machines : ResolveOne(m)
    \/ \E m \in Machines : RejectOne(m)
    \/ \E m \in Machines : AbsentOne(m)
    \/ Malform
    \/ ExhaustBudget
    \/ MutateFailedDetail
    \/ Finish
    \/ /\ phase \in {"Built", "Failed"}
       /\ UNCHANGED vars

Spec == Init /\ [][Next]_vars /\ WF_vars(Next)

Terminal == phase \in {"Built", "Failed"}

C1_Totality ==
    (phase = "Built") => \A m \in Machines : result[m] = Expected(truth[m])

C2_FailureIsTyped ==
    /\ (phase = "Failed") => kind \in FailureKinds
    /\ (phase = "Failed") => \A m \in Machines : result[m] # "Absent"

C3_FailureRejectsAll ==
    (phase = "Failed") => \A m \in Machines : result[m] = "Rejected"

FailureIsAbsorbing ==
    [][ (phase = "Failed") => UNCHANGED vars ]_vars

EventuallyTerminal == <>Terminal

=============================================================================
