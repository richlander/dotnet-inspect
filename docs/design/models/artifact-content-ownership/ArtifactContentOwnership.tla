-------------------- MODULE ArtifactContentOwnership --------------------
(***************************************************************************)
(* Artifact-owned interaction among exact content references, current      *)
(* query authority, transferred retained-content child leases, scoped      *)
(* borrows, session retirement, and backing acquisition-resource release.  *)
(*                                                                         *)
(* Query-lease and content-reference identities preserve their issuing     *)
(* session. Each content child records the exact reference and query lease  *)
(* under which it was issued. Query replacement keeps the old lease stale  *)
(* and the replacement current at the same time. A transferred child uses  *)
(* its exact reference independently of that later policy replacement.      *)
(***************************************************************************)
EXTENDS Naturals, FiniteSets, TLC

CONSTANTS
  Holders,
  PrimarySession,
  ForeignSession,
  ContentOne,
  ContentTwo,
  ReferenceOne,
  ReferenceTwo,
  ForeignReference,
  InitialQueryLease,
  ReplacementQueryLease,
  ForeignQueryLease,
  IssuanceMode, \* "Gated" or "Ungated"
  BorrowMode,   \* "LeaseBound" or "QueryBound"
  ReferenceMode,\* "Exact" or "Unbound"
  ReleaseMode   \* "AwaitChildren" or "Immediate"

ASSUME Cardinality(Holders) >= 2
ASSUME PrimarySession # ForeignSession
ASSUME ContentOne # ContentTwo
ASSUME ReferenceOne # ReferenceTwo
ASSUME ReferenceOne # ForeignReference
ASSUME ReferenceTwo # ForeignReference
ASSUME InitialQueryLease # ReplacementQueryLease
ASSUME InitialQueryLease # ForeignQueryLease
ASSUME ReplacementQueryLease # ForeignQueryLease
ASSUME IssuanceMode \in {"Gated", "Ungated"}
ASSUME BorrowMode \in {"LeaseBound", "QueryBound"}
ASSUME ReferenceMode \in {"Exact", "Unbound"}
ASSUME ReleaseMode \in {"AwaitChildren", "Immediate"}

References == {ReferenceOne, ReferenceTwo, ForeignReference}
QueryLeases ==
  {InitialQueryLease, ReplacementQueryLease, ForeignQueryLease}
NoReference == "NoReference"
NoQueryLease == "NoQueryLease"

ReferenceSession(reference) ==
  IF reference = ForeignReference
    THEN ForeignSession
    ELSE PrimarySession

ReferenceContent(reference) ==
  CASE reference = ReferenceOne -> ContentOne
    [] reference = ReferenceTwo -> ContentTwo
    [] OTHER -> ContentOne

QueryLeaseSession(lease) ==
  IF lease = ForeignQueryLease
    THEN ForeignSession
    ELSE PrimarySession

VARIABLES
  session,              \* "open" | "retiring" | "released"
  retireRequested,
  queryLeases,          \* [QueryLeases -> "absent" | "current" | "stale"]
  leases,               \* [Holders -> "absent" | "live" | "released"]
  leaseReferences,      \* exact reference bound to each issued child
  issuingQueries,       \* query lease that authorized each child
  borrows,              \* [Holders -> "idle" | "live" | "done" | "rejected"]
  borrowReferences,     \* exact reference used by an admitted borrow
  wIssuanceGuard,
  wChildIndependent,
  wExactBorrow,
  wReleaseGuard,
  pBorrowAfterReplace,
  pInvalidIssueRejected,
  pMismatchRejected,
  pReplacementIssue,
  pRetiredAfterChild

vars ==
  << session, retireRequested, queryLeases, leases, leaseReferences,
     issuingQueries, borrows, borrowReferences, wIssuanceGuard,
     wChildIndependent, wExactBorrow, wReleaseGuard, pBorrowAfterReplace,
     pInvalidIssueRejected, pMismatchRejected, pReplacementIssue,
     pRetiredAfterChild >>

QueryLeaseStates == {"absent", "current", "stale"}
LeaseStates == {"absent", "live", "released"}
BorrowStates == {"idle", "live", "done", "rejected"}

ValidIssue(lease, reference) ==
  /\ session = "open"
  /\ queryLeases[lease] = "current"
  /\ QueryLeaseSession(lease) = PrimarySession
  /\ ReferenceSession(reference) = PrimarySession

ChildrenSettled ==
  \A h \in Holders :
    /\ leases[h] \in {"absent", "released"}
    /\ borrows[h] # "live"

TypeOK ==
  /\ session \in {"open", "retiring", "released"}
  /\ retireRequested \in BOOLEAN
  /\ queryLeases \in [QueryLeases -> QueryLeaseStates]
  /\ leases \in [Holders -> LeaseStates]
  /\ leaseReferences \in [Holders -> References \union {NoReference}]
  /\ issuingQueries \in [Holders -> QueryLeases \union {NoQueryLease}]
  /\ borrows \in [Holders -> BorrowStates]
  /\ borrowReferences \in [Holders -> References \union {NoReference}]
  /\ wIssuanceGuard \in BOOLEAN
  /\ wChildIndependent \in BOOLEAN
  /\ wExactBorrow \in BOOLEAN
  /\ wReleaseGuard \in BOOLEAN
  /\ pBorrowAfterReplace \in BOOLEAN
  /\ pInvalidIssueRejected \in BOOLEAN
  /\ pMismatchRejected \in BOOLEAN
  /\ pReplacementIssue \in BOOLEAN
  /\ pRetiredAfterChild \in BOOLEAN

Init ==
  /\ session = "open"
  /\ retireRequested = FALSE
  /\ queryLeases =
      [lease \in QueryLeases |->
        IF lease = InitialQueryLease
          THEN "current"
          ELSE IF lease = ForeignQueryLease
            THEN "current"
            ELSE "absent"]
  /\ leases = [h \in Holders |-> "absent"]
  /\ leaseReferences = [h \in Holders |-> NoReference]
  /\ issuingQueries = [h \in Holders |-> NoQueryLease]
  /\ borrows = [h \in Holders |-> "idle"]
  /\ borrowReferences = [h \in Holders |-> NoReference]
  /\ wIssuanceGuard = TRUE
  /\ wChildIndependent = TRUE
  /\ wExactBorrow = TRUE
  /\ wReleaseGuard = TRUE
  /\ pBorrowAfterReplace = FALSE
  /\ pInvalidIssueRejected = FALSE
  /\ pMismatchRejected = FALSE
  /\ pReplacementIssue = FALSE
  /\ pRetiredAfterChild = FALSE

ReplaceQueryAuthorization ==
  /\ session = "open"
  /\ queryLeases[InitialQueryLease] = "current"
  /\ queryLeases[ReplacementQueryLease] = "absent"
  /\ queryLeases' =
      [queryLeases EXCEPT
        ![InitialQueryLease] = "stale",
        ![ReplacementQueryLease] = "current"]
  /\ UNCHANGED << session, retireRequested, leases, leaseReferences,
                  issuingQueries, borrows, borrowReferences,
                  wIssuanceGuard, wChildIndependent, wExactBorrow,
                  wReleaseGuard, pBorrowAfterReplace,
                  pInvalidIssueRejected, pMismatchRejected,
                  pReplacementIssue, pRetiredAfterChild >>

IssueContentLease(holder, queryLease, reference) ==
  /\ holder \in Holders
  /\ queryLease \in QueryLeases
  /\ reference \in References
  /\ leases[holder] = "absent"
  /\ IF IssuanceMode = "Gated"
       THEN ValidIssue(queryLease, reference)
       ELSE session # "released"
  /\ leases' = [leases EXCEPT ![holder] = "live"]
  /\ leaseReferences' =
      [leaseReferences EXCEPT ![holder] = reference]
  /\ issuingQueries' =
      [issuingQueries EXCEPT ![holder] = queryLease]
  /\ wIssuanceGuard' =
      (wIssuanceGuard /\ ValidIssue(queryLease, reference))
  /\ pReplacementIssue' =
      (pReplacementIssue
        \/ queryLease = ReplacementQueryLease)
  /\ UNCHANGED << session, retireRequested, queryLeases, borrows,
                  borrowReferences, wChildIndependent, wExactBorrow,
                  wReleaseGuard, pBorrowAfterReplace,
                  pInvalidIssueRejected, pMismatchRejected,
                  pRetiredAfterChild >>

RejectInvalidIssue(holder, queryLease, reference) ==
  /\ IssuanceMode = "Gated"
  /\ holder \in Holders
  /\ queryLease \in QueryLeases
  /\ reference \in References
  /\ leases[holder] = "absent"
  /\ ~ValidIssue(queryLease, reference)
  /\ pInvalidIssueRejected' = TRUE
  /\ UNCHANGED << session, retireRequested, queryLeases, leases,
                  leaseReferences, issuingQueries, borrows,
                  borrowReferences, wIssuanceGuard, wChildIndependent,
                  wExactBorrow, wReleaseGuard, pBorrowAfterReplace,
                  pMismatchRejected, pReplacementIssue,
                  pRetiredAfterChild >>

StartBorrow(holder, reference) ==
  /\ holder \in Holders
  /\ reference \in References
  /\ leases[holder] = "live"
  /\ borrows[holder] \in {"idle", "done"}
  /\ IF ReferenceMode = "Exact"
       THEN reference = leaseReferences[holder]
       ELSE TRUE
  /\ IF BorrowMode = "LeaseBound"
       THEN TRUE
       ELSE queryLeases[issuingQueries[holder]] = "current"
  /\ borrows' = [borrows EXCEPT ![holder] = "live"]
  /\ borrowReferences' =
      [borrowReferences EXCEPT ![holder] = reference]
  /\ wExactBorrow' =
      (wExactBorrow /\ reference = leaseReferences[holder])
  /\ pBorrowAfterReplace' =
      (pBorrowAfterReplace
        \/ (issuingQueries[holder] = InitialQueryLease
          /\ queryLeases[InitialQueryLease] = "stale"
          /\ BorrowMode = "LeaseBound"))
  /\ UNCHANGED << session, retireRequested, queryLeases, leases,
                  leaseReferences, issuingQueries, wIssuanceGuard,
                  wChildIndependent, wReleaseGuard,
                  pInvalidIssueRejected, pMismatchRejected,
                  pReplacementIssue, pRetiredAfterChild >>

RejectBorrowAfterPolicyReplacement(holder) ==
  /\ BorrowMode = "QueryBound"
  /\ holder \in Holders
  /\ leases[holder] = "live"
  /\ borrows[holder] \in {"idle", "done"}
  /\ queryLeases[issuingQueries[holder]] = "stale"
  /\ borrows' = [borrows EXCEPT ![holder] = "rejected"]
  /\ wChildIndependent' = FALSE
  /\ UNCHANGED << session, retireRequested, queryLeases, leases,
                  leaseReferences, issuingQueries, borrowReferences,
                  wIssuanceGuard, wExactBorrow, wReleaseGuard,
                  pBorrowAfterReplace, pInvalidIssueRejected,
                  pMismatchRejected, pReplacementIssue,
                  pRetiredAfterChild >>

RejectMismatchedBorrow(holder, reference) ==
  /\ ReferenceMode = "Exact"
  /\ holder \in Holders
  /\ reference \in References
  /\ leases[holder] = "live"
  /\ borrows[holder] \in {"idle", "done"}
  /\ reference # leaseReferences[holder]
  /\ pMismatchRejected' = TRUE
  /\ UNCHANGED << session, retireRequested, queryLeases, leases,
                  leaseReferences, issuingQueries, borrows,
                  borrowReferences, wIssuanceGuard, wChildIndependent,
                  wExactBorrow, wReleaseGuard, pBorrowAfterReplace,
                  pInvalidIssueRejected, pReplacementIssue,
                  pRetiredAfterChild >>

EndBorrow(holder) ==
  /\ holder \in Holders
  /\ borrows[holder] = "live"
  /\ borrows' = [borrows EXCEPT ![holder] = "done"]
  /\ borrowReferences' =
      [borrowReferences EXCEPT ![holder] = NoReference]
  /\ UNCHANGED << session, retireRequested, queryLeases, leases,
                  leaseReferences, issuingQueries, wIssuanceGuard,
                  wChildIndependent, wExactBorrow, wReleaseGuard,
                  pBorrowAfterReplace, pInvalidIssueRejected,
                  pMismatchRejected, pReplacementIssue,
                  pRetiredAfterChild >>

ReleaseContentLease(holder) ==
  /\ holder \in Holders
  /\ leases[holder] = "live"
  /\ borrows[holder] # "live"
  /\ leases' = [leases EXCEPT ![holder] = "released"]
  /\ UNCHANGED << session, retireRequested, queryLeases, leaseReferences,
                  issuingQueries, borrows, borrowReferences,
                  wIssuanceGuard, wChildIndependent, wExactBorrow,
                  wReleaseGuard, pBorrowAfterReplace,
                  pInvalidIssueRejected, pMismatchRejected,
                  pReplacementIssue, pRetiredAfterChild >>

RequestRetirement ==
  /\ retireRequested = FALSE
  /\ retireRequested' = TRUE
  /\ UNCHANGED << session, queryLeases, leases, leaseReferences,
                  issuingQueries, borrows, borrowReferences,
                  wIssuanceGuard, wChildIndependent, wExactBorrow,
                  wReleaseGuard, pBorrowAfterReplace,
                  pInvalidIssueRejected, pMismatchRejected,
                  pReplacementIssue, pRetiredAfterChild >>

BeginRetirement ==
  /\ retireRequested
  /\ session = "open"
  /\ session' = "retiring"
  /\ queryLeases' =
      [lease \in QueryLeases |->
        IF queryLeases[lease] = "current"
          THEN "stale"
          ELSE queryLeases[lease]]
  /\ UNCHANGED << retireRequested, leases, leaseReferences,
                  issuingQueries, borrows, borrowReferences,
                  wIssuanceGuard, wChildIndependent, wExactBorrow,
                  wReleaseGuard, pBorrowAfterReplace,
                  pInvalidIssueRejected, pMismatchRejected,
                  pReplacementIssue, pRetiredAfterChild >>

ReleaseBackingResources ==
  /\ session = "retiring"
  /\ IF ReleaseMode = "AwaitChildren"
       THEN ChildrenSettled
       ELSE TRUE
  /\ session' = "released"
  /\ wReleaseGuard' = (wReleaseGuard /\ ChildrenSettled)
  /\ pRetiredAfterChild' =
      (pRetiredAfterChild
        \/ \E h \in Holders : leases[h] = "released")
  /\ UNCHANGED << retireRequested, queryLeases, leases,
                  leaseReferences, issuingQueries, borrows,
                  borrowReferences, wIssuanceGuard, wChildIndependent,
                  wExactBorrow, pBorrowAfterReplace,
                  pInvalidIssueRejected, pMismatchRejected,
                  pReplacementIssue >>

Next ==
  \/ ReplaceQueryAuthorization
  \/ \E holder \in Holders :
       \E queryLease \in QueryLeases :
         \E reference \in References :
           IssueContentLease(holder, queryLease, reference)
  \/ \E holder \in Holders :
       \E queryLease \in QueryLeases :
         \E reference \in References :
           RejectInvalidIssue(holder, queryLease, reference)
  \/ \E holder \in Holders :
       \E reference \in References :
         StartBorrow(holder, reference)
  \/ \E holder \in Holders :
       RejectBorrowAfterPolicyReplacement(holder)
  \/ \E holder \in Holders :
       \E reference \in References :
         RejectMismatchedBorrow(holder, reference)
  \/ \E holder \in Holders : EndBorrow(holder)
  \/ \E holder \in Holders : ReleaseContentLease(holder)
  \/ RequestRetirement
  \/ BeginRetirement
  \/ ReleaseBackingResources

Spec ==
  /\ Init
  /\ [][Next]_vars
  /\ WF_vars(BeginRetirement)
  /\ WF_vars(ReleaseBackingResources)
  /\ \A holder \in Holders : WF_vars(EndBorrow(holder))
  /\ \A holder \in Holders : SF_vars(ReleaseContentLease(holder))

IssuanceRequiresCurrentPolicyAndExactSession == wIssuanceGuard
TransferredChildIndependentOfQueryPolicy == wChildIndependent
ContentLeaseBindsExactReference == wExactBorrow
BackingReleaseRequiresChildSettlement == wReleaseGuard

ReleasedBackingHasNoLiveChild ==
  session = "released" => ChildrenSettled

LiveBorrowHasLiveExactChild ==
  \A holder \in Holders :
    borrows[holder] = "live" =>
      /\ leases[holder] = "live"
      /\ borrowReferences[holder] = leaseReferences[holder]
      /\ ReferenceContent(borrowReferences[holder])
          = ReferenceContent(leaseReferences[holder])

RetirementEventuallySettles ==
  retireRequested ~> session = "released"

ProbeNoBorrowAfterReplacement == ~pBorrowAfterReplace
ProbeNoInvalidIssueRejection == ~pInvalidIssueRejected
ProbeNoMismatchRejection == ~pMismatchRejected
ProbeNoReplacementIssue == ~pReplacementIssue
ProbeNoRetirementAfterChild == ~pRetiredAfterChild

=============================================================================
