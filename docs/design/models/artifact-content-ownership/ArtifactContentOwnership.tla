-------------------- MODULE ArtifactContentOwnership --------------------
(***************************************************************************)
(* Artifact-owned interaction among current query policy, transferred      *)
(* retained-content child leases, scoped borrows, session retirement, and  *)
(* backing acquisition-resource release.                                   *)
(*                                                                         *)
(* The model deliberately separates query authorization from an already    *)
(* issued content child. Query replacement rejects later issuance through  *)
(* the stale query lease, but a transferred child remains usable until its  *)
(* owner releases it. Session retirement rejects new children and waits for *)
(* every child and admitted borrow before backing resources release.        *)
(***************************************************************************)
EXTENDS Naturals, FiniteSets, TLC

CONSTANTS
  Holders,
  IssuanceMode, \* "Gated" or "Ungated"
  BorrowMode,   \* "LeaseBound" or "QueryBound"
  ReleaseMode   \* "AwaitChildren" or "Immediate"

ASSUME Cardinality(Holders) >= 2
ASSUME IssuanceMode \in {"Gated", "Ungated"}
ASSUME BorrowMode \in {"LeaseBound", "QueryBound"}
ASSUME ReleaseMode \in {"AwaitChildren", "Immediate"}

VARIABLES
  session,              \* "open" | "retiring" | "released"
  retireRequested,
  queryLease,           \* "current" | "stale"
  leases,               \* [Holders -> "absent" | "live" | "released"]
  borrows,              \* [Holders -> "idle" | "live" | "done" | "rejected"]
  wIssuanceGuard,
  wChildIndependent,
  wReleaseGuard,
  pBorrowAfterReplace,
  pRejectedOldIssue,
  pRetiredAfterChild

vars ==
  << session, retireRequested, queryLease, leases, borrows,
     wIssuanceGuard, wChildIndependent, wReleaseGuard,
     pBorrowAfterReplace, pRejectedOldIssue, pRetiredAfterChild >>

LeaseStates == {"absent", "live", "released"}
BorrowStates == {"idle", "live", "done", "rejected"}

ChildrenSettled ==
  \A h \in Holders :
    /\ leases[h] \in {"absent", "released"}
    /\ borrows[h] # "live"

TypeOK ==
  /\ session \in {"open", "retiring", "released"}
  /\ retireRequested \in BOOLEAN
  /\ queryLease \in {"current", "stale"}
  /\ leases \in [Holders -> LeaseStates]
  /\ borrows \in [Holders -> BorrowStates]
  /\ wIssuanceGuard \in BOOLEAN
  /\ wChildIndependent \in BOOLEAN
  /\ wReleaseGuard \in BOOLEAN
  /\ pBorrowAfterReplace \in BOOLEAN
  /\ pRejectedOldIssue \in BOOLEAN
  /\ pRetiredAfterChild \in BOOLEAN

Init ==
  /\ session = "open"
  /\ retireRequested = FALSE
  /\ queryLease = "current"
  /\ leases = [h \in Holders |-> "absent"]
  /\ borrows = [h \in Holders |-> "idle"]
  /\ wIssuanceGuard = TRUE
  /\ wChildIndependent = TRUE
  /\ wReleaseGuard = TRUE
  /\ pBorrowAfterReplace = FALSE
  /\ pRejectedOldIssue = FALSE
  /\ pRetiredAfterChild = FALSE

ReplaceQueryAuthorization ==
  /\ session = "open"
  /\ queryLease = "current"
  /\ queryLease' = "stale"
  /\ UNCHANGED << session, retireRequested, leases, borrows,
                  wIssuanceGuard, wChildIndependent, wReleaseGuard,
                  pBorrowAfterReplace, pRejectedOldIssue,
                  pRetiredAfterChild >>

IssueContentLease(h) ==
  /\ leases[h] = "absent"
  /\ IF IssuanceMode = "Gated"
       THEN /\ session = "open"
            /\ queryLease = "current"
       ELSE session # "released"
  /\ leases' = [leases EXCEPT ![h] = "live"]
  /\ wIssuanceGuard' =
      (wIssuanceGuard /\ session = "open" /\ queryLease = "current")
  /\ UNCHANGED << session, retireRequested, queryLease, borrows,
                  wChildIndependent, wReleaseGuard, pBorrowAfterReplace,
                  pRejectedOldIssue, pRetiredAfterChild >>

RejectOldQueryIssue(h) ==
  /\ IssuanceMode = "Gated"
  /\ leases[h] = "absent"
  /\ session = "open"
  /\ queryLease = "stale"
  /\ pRejectedOldIssue' = TRUE
  /\ UNCHANGED << session, retireRequested, queryLease, leases, borrows,
                  wIssuanceGuard, wChildIndependent, wReleaseGuard,
                  pBorrowAfterReplace, pRetiredAfterChild >>

StartBorrow(h) ==
  /\ leases[h] = "live"
  /\ borrows[h] \in {"idle", "done"}
  /\ IF BorrowMode = "LeaseBound"
       THEN TRUE
       ELSE queryLease = "current"
  /\ borrows' = [borrows EXCEPT ![h] = "live"]
  /\ pBorrowAfterReplace' =
      (pBorrowAfterReplace
        \/ (queryLease = "stale" /\ BorrowMode = "LeaseBound"))
  /\ UNCHANGED << session, retireRequested, queryLease, leases,
                  wIssuanceGuard, wChildIndependent, wReleaseGuard,
                  pRejectedOldIssue, pRetiredAfterChild >>

RejectBorrowAfterPolicyReplacement(h) ==
  /\ BorrowMode = "QueryBound"
  /\ queryLease = "stale"
  /\ leases[h] = "live"
  /\ borrows[h] \in {"idle", "done"}
  /\ borrows' = [borrows EXCEPT ![h] = "rejected"]
  /\ wChildIndependent' = FALSE
  /\ UNCHANGED << session, retireRequested, queryLease, leases,
                  wIssuanceGuard, wReleaseGuard, pBorrowAfterReplace,
                  pRejectedOldIssue, pRetiredAfterChild >>

EndBorrow(h) ==
  /\ borrows[h] = "live"
  /\ borrows' = [borrows EXCEPT ![h] = "done"]
  /\ UNCHANGED << session, retireRequested, queryLease, leases,
                  wIssuanceGuard, wChildIndependent, wReleaseGuard,
                  pBorrowAfterReplace, pRejectedOldIssue,
                  pRetiredAfterChild >>

ReleaseContentLease(h) ==
  /\ leases[h] = "live"
  /\ borrows[h] # "live"
  /\ leases' = [leases EXCEPT ![h] = "released"]
  /\ UNCHANGED << session, retireRequested, queryLease, borrows,
                  wIssuanceGuard, wChildIndependent, wReleaseGuard,
                  pBorrowAfterReplace, pRejectedOldIssue,
                  pRetiredAfterChild >>

RequestRetirement ==
  /\ retireRequested = FALSE
  /\ retireRequested' = TRUE
  /\ UNCHANGED << session, queryLease, leases, borrows,
                  wIssuanceGuard, wChildIndependent, wReleaseGuard,
                  pBorrowAfterReplace, pRejectedOldIssue,
                  pRetiredAfterChild >>

BeginRetirement ==
  /\ retireRequested
  /\ session = "open"
  /\ session' = "retiring"
  /\ queryLease' = "stale"
  /\ UNCHANGED << retireRequested, leases, borrows,
                  wIssuanceGuard, wChildIndependent, wReleaseGuard,
                  pBorrowAfterReplace, pRejectedOldIssue,
                  pRetiredAfterChild >>

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
  /\ UNCHANGED << retireRequested, queryLease, leases, borrows,
                  wIssuanceGuard, wChildIndependent, pBorrowAfterReplace,
                  pRejectedOldIssue >>

Next ==
  \/ ReplaceQueryAuthorization
  \/ \E h \in Holders : IssueContentLease(h)
  \/ \E h \in Holders : RejectOldQueryIssue(h)
  \/ \E h \in Holders : StartBorrow(h)
  \/ \E h \in Holders : RejectBorrowAfterPolicyReplacement(h)
  \/ \E h \in Holders : EndBorrow(h)
  \/ \E h \in Holders : ReleaseContentLease(h)
  \/ RequestRetirement
  \/ BeginRetirement
  \/ ReleaseBackingResources

Spec ==
  /\ Init
  /\ [][Next]_vars
  /\ WF_vars(BeginRetirement)
  /\ WF_vars(ReleaseBackingResources)
  /\ \A h \in Holders : WF_vars(EndBorrow(h))
  /\ \A h \in Holders : SF_vars(ReleaseContentLease(h))

IssuanceRequiresCurrentPolicy == wIssuanceGuard
TransferredChildIndependentOfQueryPolicy == wChildIndependent
BackingReleaseRequiresChildSettlement == wReleaseGuard

ReleasedBackingHasNoLiveChild ==
  session = "released" => ChildrenSettled

LiveBorrowHasLiveChild ==
  \A h \in Holders : borrows[h] = "live" => leases[h] = "live"

RetirementEventuallySettles ==
  retireRequested ~> session = "released"

ProbeNoBorrowAfterReplacement == ~pBorrowAfterReplace
ProbeNoRejectedOldIssue == ~pRejectedOldIssue
ProbeNoRetirementAfterChild == ~pRetiredAfterChild

=============================================================================
