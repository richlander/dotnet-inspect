---------------------- MODULE StateMachineCompleteness ----------------------
(***************************************************************************)
(* C1, C3, and the structural-async GetByStateMachine fragment of C2 from  *)
(* docs/design/state-machine-relationship-index.md.                        *)
(*                                                                         *)
(* This model owns structural-async-machine classification and whole-module *)
(* failure. Rejection-component merging has a different unit of state --   *)
(* published rejections connected by tagged merge keys -- and lives in     *)
(* RejectionComponentMerge.tla. Keeping those domains separate prevents a  *)
(* merge key such as a kickoff or claimed name from being mistaken for a   *)
(* structural async state machine.                                         *)
(***************************************************************************)

EXTENDS Naturals

CONSTANTS
    MachineCount, \* structural async state machines in the image
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
(*               = "Untyped"            publish failure with no typed kind  *)
(*               = "WrongMalformedKind" misreport malformed input           *)
(*               = "WrongBudgetKind"    misreport budget exhaustion         *)
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
    /\ FailureMode \in
        {"Total", "PreserveClassified", "ReportAbsent", "Untyped",
         "WrongMalformedKind", "WrongBudgetKind"}
    /\ FailureMutationMode \in {"Stable", "RewriteDetail"}
    /\ FinishMode \in {"Guarded", "AllowUnvisited", "DropAsAbsent"}

Machines == 1..MachineCount
Results == {"Resolved", "Rejected", "Absent", "Unclassified"}
Truths == {"Resolvable", "Refused", "NoClaim"}
FailureKinds == {"Malformed", "BudgetExceeded"}
FailureCauses == {"MalformedInput", "BudgetExhaustion"}
Phases == {"Building", "Built", "Failed"}

ExpectedFailureKind(c) ==
    IF c = "MalformedInput" THEN "Malformed" ELSE "BudgetExceeded"

(***************************************************************************)
(* Keep publication behavior independent of ExpectedFailureKind, the C2     *)
(* oracle. Sharing that helper would let a mapping defect change both the   *)
(* behavior and its check together.                                         *)
(***************************************************************************)
PublishedFailureKind(c) ==
    IF FailureMode = "Untyped"
    THEN "None"
    ELSE IF /\ FailureMode = "WrongMalformedKind"
            /\ c = "MalformedInput"
         THEN "BudgetExceeded"
         ELSE IF /\ FailureMode = "WrongBudgetKind"
                 /\ c = "BudgetExhaustion"
              THEN "Malformed"
              ELSE IF c = "MalformedInput"
                   THEN "Malformed"
                   ELSE "BudgetExceeded"

Expected(t) ==
    CASE t = "Resolvable" -> "Resolved"
      [] t = "Refused"    -> "Rejected"
      [] OTHER            -> "Absent"

VARIABLES
    truth,        \* independently recomputed classification
    phase,        \* Building, Built, or Failed
    cause,        \* independent whole-module failure trigger, or None
    kind,         \* whole-module failure kind, or None
    failureDetail,\* abstract published failure detail, or zero
    result,       \* published result per structural async machine
    budget        \* remaining classification steps

vars == <<truth, phase, cause, kind, failureDetail, result, budget>>

Visited == {m \in Machines : result[m] # "Unclassified"}

FailureProjection == <<phase, kind, failureDetail, result>>

TypeOK ==
    /\ truth \in [Machines -> Truths]
    /\ phase \in Phases
    /\ cause \in FailureCauses \cup {"None"}
    /\ kind \in FailureKinds \cup {"None"}
    /\ failureDetail \in 0..2
    /\ result \in [Machines -> Results]
    /\ budget \in 0..MaxBudget

Init ==
    /\ truth \in [Machines -> Truths]
    /\ phase = "Building"
    /\ cause = "None"
    /\ kind = "None"
    /\ failureDetail = 0
    /\ result = [m \in Machines |-> "Unclassified"]
    /\ budget = MaxBudget

ClassifyOne(m, expectedTruth, classification) ==
    /\ phase = "Building"
    /\ m \notin Visited
    /\ budget > 0
    /\ truth[m] = expectedTruth
    /\ result' = [result EXCEPT ![m] = classification]
    /\ budget' = budget - 1
    /\ UNCHANGED <<truth, phase, cause, kind, failureDetail>>

ResolveOne(m) == ClassifyOne(m, "Resolvable", "Resolved")
RejectOne(m) == ClassifyOne(m, "Refused", "Rejected")
AbsentOne(m) == ClassifyOne(m, "NoClaim", "Absent")

(***************************************************************************)
(* A whole-module failure overwrites every result, including classifications *)
(* already recorded and machines construction had not reached.             *)
(***************************************************************************)
FailModule(c) ==
    /\ phase = "Building"
    /\ phase' = "Failed"
    /\ cause' = c
    /\ kind' = PublishedFailureKind(c)
    /\ failureDetail' = 1
    /\ result' = [m \in Machines |->
                    CASE FailureMode = "ReportAbsent" -> "Absent"
                      [] FailureMode = "PreserveClassified" ->
                            IF result[m] = "Unclassified" THEN "Rejected"
                            ELSE result[m]
                      [] OTHER -> "Rejected"]
    /\ UNCHANGED <<truth, budget>>

Malform == FailModule("MalformedInput")

ExhaustBudget ==
    /\ budget = 0
    /\ Visited # Machines
    /\ FailModule("BudgetExhaustion")

MutateFailedDetail ==
    /\ phase = "Failed"
    /\ FailureMutationMode = "RewriteDetail"
    /\ failureDetail' = 2
    /\ UNCHANGED <<truth, phase, cause, kind, result, budget>>

Finish ==
    /\ phase = "Building"
    /\ (FinishMode \in {"AllowUnvisited", "DropAsAbsent"} \/ Visited = Machines)
    /\ phase' = "Built"
    /\ result' = IF FinishMode = "DropAsAbsent"
                 THEN [m \in Machines |->
                         IF result[m] = "Unclassified" THEN "Absent"
                         ELSE result[m]]
                 ELSE result
    /\ UNCHANGED <<truth, cause, kind, failureDetail, budget>>

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
    /\ (phase = "Failed") => kind = ExpectedFailureKind(cause)
    /\ (phase = "Failed") => \A m \in Machines : result[m] # "Absent"

C3_FailureRejectsAll ==
    (phase = "Failed") => \A m \in Machines : result[m] = "Rejected"

FailureIsAbsorbing ==
    [][ (phase = "Failed") => UNCHANGED FailureProjection ]_vars

EventuallyTerminal == <>Terminal

=============================================================================
