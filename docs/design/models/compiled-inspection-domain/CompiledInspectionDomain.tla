---------------------- MODULE CompiledInspectionDomain ----------------------
(***************************************************************************)
(* Design model for the Compiled Inspection Domain Composition owner in    *)
(* `docs/design/section-pipeline.md`.                                       *)
(*                                                                         *)
(* One immutable producer domain admits multiple immutable section lenses. *)
(* Two request operations then plan and execute independently over that     *)
(* shared domain. The query catalog's prerequisite closure is abstracted as *)
(* PlanOf: this owner must use that owner-issued plan without changing it.   *)
(*                                                                         *)
(* The model checks that foreign lens queries are rejected, request plans   *)
(* retain only their own demand, cancellation cannot later publish success, *)
(* a caller context is borrowed only while execution is running, and the    *)
(* composition owner never disposes that context. It does not model query   *)
(* algorithms, acquisition, rendering, row selection, or result payloads.   *)
(***************************************************************************)
EXTENDS FiniteSets, TLC

CONSTANTS
  RequestA, RequestB,
  LensA, LensB, InvalidLens,
  QueryA, QueryB, QueryC, ForeignQuery,
  ContextA, ContextB,
  AllowForeignLensBinding,
  PublishCancelledSuccess,
  RetainContextAfterTerminal,
  DisposeContextOnCancel,
  BorrowOtherRequestPlan

Requests == {RequestA, RequestB}
Lenses == {LensA, LensB, InvalidLens}
DomainQueries == {QueryA, QueryB, QueryC}
AllQueries == DomainQueries \cup {ForeignQuery}
Contexts == {ContextA, ContextB}

NoLens == "NoLens"
NoContext == "NoContext"
NoQuery == "NoQuery"

RequestStates == {"Idle", "Planned", "Running", "Succeeded", "Cancelled"}
TerminalStates == {"Succeeded", "Cancelled"}

LensQueries(lens) ==
  CASE lens = LensA -> {QueryA, QueryB}
    [] lens = LensB -> {QueryB, QueryC}
    [] OTHER -> {ForeignQuery}

\* QueryB depends on QueryA. The L1 query owner supplies this closure;
\* composition treats it as an atomic answer rather than rebuilding it.
PlanOf(demand) ==
  IF QueryB \in demand
  THEN demand \cup {QueryA}
  ELSE demand

HostDemand(query) ==
  IF query = NoQuery THEN {} ELSE {query}

Other(request) ==
  IF request = RequestA THEN RequestB ELSE RequestA

VARIABLES
  boundLenses,
  rejectedLenses,
  requestState,
  lensOf,
  directDemand,
  queryPlan,
  borrowedContext,
  resultPublished,
  resultPlan,
  cancelledEver,
  disposedByComposition

vars ==
  << boundLenses, rejectedLenses, requestState, lensOf, directDemand,
     queryPlan, borrowedContext, resultPublished, resultPlan,
     cancelledEver, disposedByComposition >>

TypeOK ==
  /\ boundLenses \subseteq Lenses
  /\ rejectedLenses \subseteq Lenses
  /\ requestState \in [Requests -> RequestStates]
  /\ lensOf \in [Requests -> Lenses \cup {NoLens}]
  /\ directDemand \in [Requests -> SUBSET AllQueries]
  /\ queryPlan \in [Requests -> SUBSET AllQueries]
  /\ borrowedContext \in [Requests -> Contexts \cup {NoContext}]
  /\ resultPublished \in [Requests -> BOOLEAN]
  /\ resultPlan \in [Requests -> SUBSET AllQueries]
  /\ cancelledEver \in [Requests -> BOOLEAN]
  /\ disposedByComposition \in [Requests -> BOOLEAN]

LensBindingsUseOnlyDomainQueries ==
  \A lens \in boundLenses : LensQueries(lens) \subseteq DomainQueries

RejectedLensesStayUnbound ==
  rejectedLenses \cap boundLenses = {}

PlanMatchesOwnDemand ==
  \A request \in Requests :
    requestState[request] # "Idle"
      => queryPlan[request] = PlanOf(directDemand[request])

PlansUseOnlyDomainQueries ==
  \A request \in Requests :
    requestState[request] # "Idle"
      => queryPlan[request] \subseteq DomainQueries

BorrowedContextOnlyWhileRunning ==
  \A request \in Requests :
    (borrowedContext[request] # NoContext)
      <=> (requestState[request] = "Running")

CompositionNeverDisposesContext ==
  \A request \in Requests : ~disposedByComposition[request]

CancellationSuppressesSuccess ==
  \A request \in Requests :
    cancelledEver[request]
      => /\ requestState[request] = "Cancelled"
         /\ ~resultPublished[request]

PublishedResultMatchesOwnPlan ==
  \A request \in Requests :
    resultPublished[request]
      => /\ requestState[request] = "Succeeded"
         /\ resultPlan[request] = queryPlan[request]
         /\ resultPlan[request] = PlanOf(directDemand[request])

ResultShapeIsConsistent ==
  \A request \in Requests :
    (requestState[request] = "Succeeded") <=> resultPublished[request]

Init ==
  /\ boundLenses = {}
  /\ rejectedLenses = {}
  /\ requestState = [request \in Requests |-> "Idle"]
  /\ lensOf = [request \in Requests |-> NoLens]
  /\ directDemand = [request \in Requests |-> {}]
  /\ queryPlan = [request \in Requests |-> {}]
  /\ borrowedContext = [request \in Requests |-> NoContext]
  /\ resultPublished = [request \in Requests |-> FALSE]
  /\ resultPlan = [request \in Requests |-> {}]
  /\ cancelledEver = [request \in Requests |-> FALSE]
  /\ disposedByComposition = [request \in Requests |-> FALSE]

SettleLens(lens) ==
  /\ lens \notin boundLenses \cup rejectedLenses
  /\ IF LensQueries(lens) \subseteq DomainQueries
        \/ AllowForeignLensBinding
     THEN /\ boundLenses' = boundLenses \cup {lens}
          /\ rejectedLenses' = rejectedLenses
     ELSE /\ boundLenses' = boundLenses
          /\ rejectedLenses' = rejectedLenses \cup {lens}
  /\ UNCHANGED << requestState, lensOf, directDemand, queryPlan,
                  borrowedContext, resultPublished, resultPlan,
                  cancelledEver, disposedByComposition >>

PlanRequest(request, lens, hostQuery) ==
  /\ requestState[request] = "Idle"
  /\ lens \in boundLenses
  /\ hostQuery \in DomainQueries \cup {NoQuery}
  /\ LET demand == LensQueries(lens) \cup HostDemand(hostQuery)
         chosenPlan ==
           IF BorrowOtherRequestPlan
              /\ requestState[Other(request)] # "Idle"
           THEN queryPlan[Other(request)]
           ELSE PlanOf(demand)
     IN /\ requestState' =
              [requestState EXCEPT ![request] = "Planned"]
        /\ lensOf' = [lensOf EXCEPT ![request] = lens]
        /\ directDemand' = [directDemand EXCEPT ![request] = demand]
        /\ queryPlan' = [queryPlan EXCEPT ![request] = chosenPlan]
  /\ UNCHANGED << boundLenses, rejectedLenses, borrowedContext,
                  resultPublished, resultPlan, cancelledEver,
                  disposedByComposition >>

StartRequest(request, context) ==
  /\ requestState[request] = "Planned"
  /\ context \in Contexts
  /\ requestState' = [requestState EXCEPT ![request] = "Running"]
  /\ borrowedContext' =
       [borrowedContext EXCEPT ![request] = context]
  /\ UNCHANGED << boundLenses, rejectedLenses, lensOf, directDemand,
                  queryPlan, resultPublished, resultPlan, cancelledEver,
                  disposedByComposition >>

CancelRequest(request) ==
  /\ requestState[request] \in {"Planned", "Running"}
  /\ requestState' = [requestState EXCEPT ![request] = "Cancelled"]
  /\ borrowedContext' =
       [borrowedContext EXCEPT ![request] = NoContext]
  /\ cancelledEver' = [cancelledEver EXCEPT ![request] = TRUE]
  /\ disposedByComposition' =
       [disposedByComposition EXCEPT
          ![request] = IF DisposeContextOnCancel THEN TRUE ELSE @]
  /\ UNCHANGED << boundLenses, rejectedLenses, lensOf, directDemand,
                  queryPlan, resultPublished, resultPlan >>

CompleteRequest(request) ==
  /\ requestState[request] = "Running"
      \/ (PublishCancelledSuccess
          /\ requestState[request] = "Cancelled")
  /\ requestState' = [requestState EXCEPT ![request] = "Succeeded"]
  /\ borrowedContext' =
       [borrowedContext EXCEPT
          ![request] =
            IF RetainContextAfterTerminal THEN @ ELSE NoContext]
  /\ resultPublished' =
       [resultPublished EXCEPT ![request] = TRUE]
  /\ resultPlan' =
       [resultPlan EXCEPT ![request] = queryPlan[request]]
  /\ UNCHANGED << boundLenses, rejectedLenses, lensOf, directDemand,
                  queryPlan, cancelledEver, disposedByComposition >>

AdvanceRequest(request) ==
  \/ \E context \in Contexts : StartRequest(request, context)
  \/ CancelRequest(request)
  \/ CompleteRequest(request)

Next ==
  \/ \E lens \in Lenses : SettleLens(lens)
  \/ \E request \in Requests :
       \E lens \in Lenses :
         \E hostQuery \in DomainQueries \cup {NoQuery} :
           PlanRequest(request, lens, hostQuery)
  \/ \E request \in Requests : AdvanceRequest(request)

Spec ==
  /\ Init
  /\ [][Next]_vars
  /\ \A request \in Requests : WF_vars(AdvanceRequest(request))

EveryActiveRequestEventuallyTerminates ==
  \A request \in Requests :
    [](requestState[request] \in {"Planned", "Running"}
       => <> (requestState[request] \in TerminalStates))

=============================================================================
