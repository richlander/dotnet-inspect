-------------- MODULE WorkspaceDefinitionsCompleteRestoration --------------
EXTENDS Sequences, FiniteSets, Naturals, TLC

CONSTANT Fault

ASSUME Fault \in {
    "None",
    "WrongAssociation",
    "StaleActivation",
    "WrongEvidenceOrder",
    "RejectedConstruct",
    "DoubleCleanup",
    "NeverCleanup"
}

Attempts == 1..2
Workspaces == 1..2
Contexts == 1..2
NoWorkspace == 0

Phases == {
    "Unused",
    "Admitted",
    "Rejected",
    "Constructing",
    "Resolving",
    "Ready",
    "Activated",
    "Closing",
    "Closed",
    "Superseded"
}

ResultKinds == {"None", "Activated", "Failed", "Superseded"}
PendingKinds == {"None", "Failed", "Superseded"}

Request(t) == <<"Request", t>>
Plan(t) == <<"Plan", t>>
NoValue == <<"None", 0>>

NoResult ==
    [kind |-> "None",
     request |-> NoValue,
     plan |-> NoValue,
     workspace |-> NoWorkspace]

VARIABLES
    current,
    phase,
    request,
    plan,
    workspace,
    usedWorkspaces,
    evidence,
    result,
    pending,
    cleanupCount,
    activatedWhileCurrent,
    witnesses

vars ==
    <<current, phase, request, plan, workspace, usedWorkspaces, evidence,
      result, pending, cleanupCount, activatedWhileCurrent, witnesses>>

Init ==
    /\ current = 0
    /\ phase = [t \in Attempts |-> "Unused"]
    /\ request = [t \in Attempts |-> NoValue]
    /\ plan = [t \in Attempts |-> NoValue]
    /\ workspace = [t \in Attempts |-> NoWorkspace]
    /\ usedWorkspaces = {}
    /\ evidence = [t \in Attempts |-> <<>>]
    /\ result = [t \in Attempts |-> NoResult]
    /\ pending = [t \in Attempts |-> "None"]
    /\ cleanupCount = [w \in Workspaces |-> 0]
    /\ activatedWhileCurrent = [t \in Attempts |-> TRUE]
    /\ witnesses = {}

StartRequest(t) ==
    /\ t = current + 1
    /\ t \in Attempts
    /\ phase[t] = "Unused"
    /\ current' = t
    /\ phase' = [phase EXCEPT ![t] = "Admitted"]
    /\ request' = [request EXCEPT ![t] = Request(t)]
    /\ plan' = [plan EXCEPT ![t] = Plan(t)]
    /\ UNCHANGED <<workspace, usedWorkspaces, evidence, result, pending,
                   cleanupCount, activatedWhileCurrent, witnesses>>

RejectRequest(t) ==
    /\ phase[t] = "Admitted"
    /\ t = current
    /\ phase' = [phase EXCEPT ![t] = "Rejected"]
    /\ result' =
        [result EXCEPT ![t] =
            [kind |-> "Failed",
             request |-> request[t],
             plan |-> plan[t],
             workspace |-> NoWorkspace]]
    /\ witnesses' = witnesses \cup {"Rejection"}
    /\ UNCHANGED <<current, request, plan, workspace, usedWorkspaces,
                   evidence, pending, cleanupCount, activatedWhileCurrent>>

BeginConstruction(t, w) ==
    /\ w \notin usedWorkspaces
    /\ \/ /\ phase[t] = "Admitted"
          /\ t = current
       \/ /\ Fault = "RejectedConstruct"
          /\ phase[t] = "Rejected"
    /\ phase' = [phase EXCEPT ![t] = "Constructing"]
    /\ workspace' = [workspace EXCEPT ![t] = w]
    /\ usedWorkspaces' = usedWorkspaces \cup {w}
    /\ UNCHANGED <<current, request, plan, evidence, result, pending,
                   cleanupCount, activatedWhileCurrent, witnesses>>

AppendEvidence(t) ==
    /\ phase[t] \in {"Constructing", "Resolving"}
    /\ Len(evidence[t]) < Cardinality(Contexts)
    /\ LET next ==
            IF Fault = "WrongEvidenceOrder" /\ Len(evidence[t]) = 0
            THEN 2
            ELSE Len(evidence[t]) + 1
       IN evidence' = [evidence EXCEPT ![t] = Append(@, next)]
    /\ phase' = [phase EXCEPT ![t] = "Resolving"]
    /\ UNCHANGED <<current, request, plan, workspace, usedWorkspaces,
                   result, pending, cleanupCount, activatedWhileCurrent,
                   witnesses>>

CompletePreparation(t) ==
    /\ phase[t] = "Resolving"
    /\ evidence[t] = <<1, 2>>
    /\ phase' = [phase EXCEPT ![t] = "Ready"]
    /\ UNCHANGED <<current, request, plan, workspace, usedWorkspaces,
                   evidence, result, pending, cleanupCount,
                   activatedWhileCurrent, witnesses>>

FailPreparation(t) ==
    /\ phase[t] \in {"Constructing", "Resolving", "Ready"}
    /\ phase' = [phase EXCEPT ![t] = "Closing"]
    /\ pending' = [pending EXCEPT ![t] = "Failed"]
    /\ witnesses' = witnesses \cup {"Failure"}
    /\ UNCHANGED <<current, request, plan, workspace, usedWorkspaces,
                   evidence, result, cleanupCount, activatedWhileCurrent>>

CloseStale(t) ==
    /\ phase[t] \in {"Constructing", "Resolving", "Ready"}
    /\ t # current
    /\ phase' = [phase EXCEPT ![t] = "Closing"]
    /\ pending' = [pending EXCEPT ![t] = "Superseded"]
    /\ witnesses' = witnesses \cup {"Supersession"}
    /\ UNCHANGED <<current, request, plan, workspace, usedWorkspaces,
                   evidence, result, cleanupCount, activatedWhileCurrent>>

SupersedeBeforeConstruction(t) ==
    /\ phase[t] = "Admitted"
    /\ t # current
    /\ phase' = [phase EXCEPT ![t] = "Superseded"]
    /\ result' =
        [result EXCEPT ![t] =
            [kind |-> "Superseded",
             request |-> request[t],
             plan |-> plan[t],
             workspace |-> NoWorkspace]]
    /\ witnesses' = witnesses \cup {"Supersession"}
    /\ UNCHANGED <<current, request, plan, workspace, usedWorkspaces,
                   evidence, pending, cleanupCount, activatedWhileCurrent>>

OtherWorkspace(w) == IF w = 1 THEN 2 ELSE 1

Activate(t) ==
    /\ phase[t] = "Ready"
    /\ t = current \/ Fault = "StaleActivation"
    /\ phase' = [phase EXCEPT ![t] = "Activated"]
    /\ result' =
        [result EXCEPT ![t] =
            [kind |-> "Activated",
             request |-> request[t],
             plan |-> plan[t],
             workspace |->
                IF Fault = "WrongAssociation"
                THEN OtherWorkspace(workspace[t])
                ELSE workspace[t]]]
    /\ activatedWhileCurrent' =
        [activatedWhileCurrent EXCEPT ![t] = (t = current)]
    /\ witnesses' = witnesses \cup {"Activation"}
    /\ UNCHANGED <<current, request, plan, workspace, usedWorkspaces,
                   evidence, pending, cleanupCount>>

Cleanup(t) ==
    /\ \/ /\ phase[t] = "Closing"
          /\ cleanupCount[workspace[t]] = 0
       \/ /\ Fault = "DoubleCleanup"
          /\ phase[t] = "Closed"
          /\ cleanupCount[workspace[t]] = 1
    /\ Fault # "NeverCleanup"
    /\ phase' = [phase EXCEPT ![t] = "Closed"]
    /\ cleanupCount' =
        [cleanupCount EXCEPT ![workspace[t]] = @ + 1]
    /\ result' =
        [result EXCEPT ![t] =
            [kind |-> pending[t],
             request |-> request[t],
             plan |-> plan[t],
             workspace |-> NoWorkspace]]
    /\ witnesses' = witnesses \cup {"Cleanup"}
    /\ UNCHANGED <<current, request, plan, workspace, usedWorkspaces,
                   evidence, pending, activatedWhileCurrent>>

Next ==
    \/ \E t \in Attempts : StartRequest(t)
    \/ \E t \in Attempts : RejectRequest(t)
    \/ \E t \in Attempts, w \in Workspaces : BeginConstruction(t, w)
    \/ \E t \in Attempts : AppendEvidence(t)
    \/ \E t \in Attempts : CompletePreparation(t)
    \/ \E t \in Attempts : FailPreparation(t)
    \/ \E t \in Attempts : CloseStale(t)
    \/ \E t \in Attempts : SupersedeBeforeConstruction(t)
    \/ \E t \in Attempts : Activate(t)
    \/ \E t \in Attempts : Cleanup(t)

Fairness ==
    /\ \A t \in Attempts : WF_vars(CloseStale(t))
    /\ \A t \in Attempts : WF_vars(Cleanup(t))

Spec == Init /\ [][Next]_vars /\ Fairness

TypeOK ==
    /\ current \in 0..2
    /\ usedWorkspaces \subseteq Workspaces
    /\ witnesses \subseteq {
        "Rejection", "Failure", "Supersession", "Activation", "Cleanup"}
    /\ \A t \in Attempts :
        /\ phase[t] \in Phases
        /\ workspace[t] \in Workspaces \cup {NoWorkspace}
        /\ pending[t] \in PendingKinds
        /\ result[t].kind \in ResultKinds
        /\ result[t].workspace \in Workspaces \cup {NoWorkspace}
        /\ evidence[t] \in Seq(Contexts)
        /\ activatedWhileCurrent[t] \in BOOLEAN

FreshWorkspaceAssociation ==
    \A t1, t2 \in Attempts :
        workspace[t1] # NoWorkspace /\ workspace[t1] = workspace[t2]
            => t1 = t2

OnlyActivatedCarriesWorkspace ==
    \A t \in Attempts :
        (result[t].workspace # NoWorkspace)
            <=> (result[t].kind = "Activated")

ActivatedAssociationIsExact ==
    \A t \in Attempts :
        result[t].kind = "Activated" =>
            /\ result[t].request = request[t]
            /\ result[t].plan = plan[t]
            /\ result[t].workspace = workspace[t]

RejectedRequestConstructsNothing ==
    \A t \in Attempts :
        result[t].kind = "Failed" /\ pending[t] = "None" =>
            workspace[t] = NoWorkspace

EvidenceOrderIsDeterministic ==
    \A t \in Attempts :
        \A index \in 1..Len(evidence[t]) :
            evidence[t][index] = index

StaleCompletionIsRefused ==
    \A t \in Attempts :
        result[t].kind = "Activated" => activatedWhileCurrent[t]

CleanupIsExactlyOnce ==
    /\ \A w \in Workspaces : cleanupCount[w] <= 1
    /\ \A t \in Attempts :
        phase[t] = "Closed" => cleanupCount[workspace[t]] = 1

Safety ==
    /\ TypeOK
    /\ FreshWorkspaceAssociation
    /\ OnlyActivatedCarriesWorkspace
    /\ ActivatedAssociationIsExact
    /\ RejectedRequestConstructsNothing
    /\ EvidenceOrderIsDeterministic
    /\ StaleCompletionIsRefused
    /\ CleanupIsExactlyOnce

ClosingSettles ==
    \A t \in Attempts : phase[t] = "Closing" ~> phase[t] = "Closed"

ActivationReachable == "Activation" \in witnesses
RejectionReachable == "Rejection" \in witnesses
FailureCleanupReachable == {"Failure", "Cleanup"} \subseteq witnesses
SupersessionCleanupReachable ==
    {"Supersession", "Cleanup"} \subseteq witnesses

NotActivationReachable == ~ActivationReachable
NotRejectionReachable == ~RejectionReachable
NotFailureCleanupReachable == ~FailureCleanupReachable
NotSupersessionCleanupReachable == ~SupersessionCleanupReachable

=============================================================================
