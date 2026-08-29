---------------------- MODULE ArtifactGenerationAccess ----------------------
(***************************************************************************)
(* Content-access lifecycle of one artifact generation, owned by           *)
(* `docs/design/artifact-acquisition-and-workspaces.md`.                   *)
(*                                                                         *)
(* The model checks how content opens interact with generation             *)
(* termination: an admission-phase materialization read through a          *)
(* source-adapter acquisition lease, query-phase opens of owner-retained   *)
(* content, and the `EndGeneration`/lease-disposal sequence that a         *)
(* session's `DisposeAsync` runs. It is the companion of                   *)
(* `docs/models/artifact-session-admission/ArtifactSessionAdmission.tla`,  *)
(* which models demand admission and treats "the dependent group reports  *)
(* quiescent" as an abstract given event; this model is about what         *)
(* quiescence must mean for content access, and shows the current          *)
(* mechanics cannot observe it.                                            *)
(*                                                                         *)
(* Two policy dimensions distinguish the target design from the current    *)
(* mechanics (`src/DotnetInspector.Artifacts/ArtifactAccess.cs`,           *)
(* `src/DotnetInspector.Artifacts.Workspaces/ArtifactSetSession.cs`):      *)
(*                                                                         *)
(*   OpenMode = "Gated"    target: a validated open registers itself       *)
(*                         atomically with the authority's ended/draining  *)
(*                         decision, so ending the generation and          *)
(*                         admitting an open are ordered by one gate.      *)
(*   OpenMode = "Recheck"  current: `ArtifactContribution.OpenRead` and    *)
(*                         `RetainedArtifactContent.OpenRead` re-read      *)
(*                         volatile flags outside the gate and then run    *)
(*                         the opener unconditionally, so an open can      *)
(*                         complete strictly after `EndGeneration`.        *)
(*                                                                         *)
(*   ReleaseMode = "AwaitQuiescence"                                       *)
(*                         target: termination closes new access, then     *)
(*                         releases acquisition leases and marks the       *)
(*                         generation ended only after no in-flight read   *)
(*                         or open stream remains.                         *)
(*   ReleaseMode = "Immediate"                                             *)
(*                         current: `TerminateAsync` sets the disposed     *)
(*                         state, calls `EndGeneration`, and disposes      *)
(*                         every acquisition lease without waiting for an  *)
(*                         in-flight sealing read or open stream.          *)
(*                                                                         *)
(* PublishMode = "Unguarded" is a mutation that removes publication's      *)
(* sealing-state check (the guard the existing test                        *)
(* `ArtifactSetSession_DisposalDuringSealCannotPublish` gates).            *)
(*                                                                         *)
(* Product concept                              Model state                *)
(*   session state (constructing/sealing/        session                   *)
(*     published/disposed)                                                 *)
(*   `TerminateAsync` progress                   term                      *)
(*   authority `_ended` after `EndGeneration`    Ended (derived)           *)
(*   target-design access closure                Draining (derived)        *)
(*   acquisition leases disposed                 LeasesReleased (derived)  *)
(*   sealing materialization read                mat                       *)
(*   query consumers opening retained content    readers                   *)
(*                                                                         *)
(* Guard witnesses are latching booleans initialized TRUE; the step that   *)
(* completes an open, performs a read, or publishes re-derives the         *)
(* condition the design requires from pre-step state and conjoins it, so   *)
(* a weakened guard fails the paired invariant.                            *)
(*                                                                         *)
(* Modeling simplifications. Acquisition batches are collapsed: the        *)
(* sealing phase has one materialization read standing for the whole      *)
(* `MaterializeAsync` loop, and one read step stands for the copy loop's   *)
(* next chunk. Authorization replacement and revocation are not modeled;   *)
(* `ReplaceQueryAuthorization` produces the same validate-then-open        *)
(* window against a revoked authorization that `EndGeneration` produces    *)
(* against an ended generation, so one flag stands for both. Budget        *)
(* arithmetic, content identity, digests, and multiple generations are     *)
(* out of scope. A query open today lands on a GC-retained byte array, so  *)
(* completing after the end returns stale-but-intact content; the          *)
(* `OpensNeverCompleteAfterEnd` invariant states the contract              *)
(* (`EndGeneration` "rejects every future open or mint"), which becomes    *)
(* load-bearing when retained content moves to a content-addressed store   *)
(* or budget-charged release, both contemplated by the owning design.      *)
(***************************************************************************)
EXTENDS Naturals, FiniteSets, TLC

CONSTANTS
  QueryReaders,  \* finite set of concurrent query consumers
  OpenMode,      \* "Gated" (target) or "Recheck" (current)
  ReleaseMode,   \* "AwaitQuiescence" (target) or "Immediate" (current)
  PublishMode    \* "StateGuarded" (product) or "Unguarded" (mutation)

ASSUME Cardinality(QueryReaders) >= 2
ASSUME OpenMode \in {"Gated", "Recheck"}
ASSUME ReleaseMode \in {"AwaitQuiescence", "Immediate"}
ASSUME PublishMode \in {"StateGuarded", "Unguarded"}

VARIABLES
  session,        \* "constructing" | "sealing" | "published" | "disposed"
  everPublished,  \* latched: publication committed at least once
  term,           \* "idle" | "begun" | "ended" | "released" | "done"
  termRequested,  \* the owner has requested disposal
  mat,            \* materialization read: "idle" | "validated" | "reading"
                  \*   | "done" | "failed" | "rejected"
  readers,        \* [QueryReaders -> "idle" | "validated" | "checking"
                  \*   | "open" | "closed" | "rejected"]
  wOpenAfterEnd,  \* witness: no open completed after the generation ended
  wLiveRead,      \* witness: no read step observed released leases
  wPublishGuard,  \* witness: publication only from the sealing state
  pQueryClosed,   \* probe: some query open/close round trip completed
  pOverlap        \* probe: termination began during a live read or stream

vars == << session, everPublished, term, termRequested, mat, readers,
           wOpenAfterEnd, wLiveRead, wPublishGuard, pQueryClosed, pOverlap >>

(***************************************************************************)
(* Derived authority flags. `TerminateAsync` orders the disposed state,    *)
(* `EndGeneration`, and lease disposal; the target design instead closes   *)
(* access first (Draining) and both ends the generation and releases       *)
(* leases at quiescence, in one settlement step.                           *)
(***************************************************************************)
Ended ==
  IF ReleaseMode = "Immediate"
    THEN term \in {"ended", "released", "done"}
    ELSE term \in {"released", "done"}

Draining == ReleaseMode = "AwaitQuiescence" /\ term # "idle"

LeasesReleased == term \in {"released", "done"}

\* No in-flight materialization read and no open query stream.
Quiescent ==
  /\ mat # "reading"
  /\ \A r \in QueryReaders : readers[r] # "open"

MatStates ==
  {"idle", "validated", "reading", "done", "failed", "rejected"}
ReaderStates ==
  {"idle", "validated", "checking", "open", "closed", "rejected"}

TypeOK ==
  /\ session \in {"constructing", "sealing", "published", "disposed"}
  /\ everPublished \in BOOLEAN
  /\ term \in {"idle", "begun", "ended", "released", "done"}
  /\ termRequested \in BOOLEAN
  /\ mat \in MatStates
  /\ readers \in [QueryReaders -> ReaderStates]
  /\ wOpenAfterEnd \in BOOLEAN
  /\ wLiveRead \in BOOLEAN
  /\ wPublishGuard \in BOOLEAN
  /\ pQueryClosed \in BOOLEAN
  /\ pOverlap \in BOOLEAN

Init ==
  /\ session = "constructing"
  /\ everPublished = FALSE
  /\ term = "idle"
  /\ termRequested = FALSE
  /\ mat = "idle"
  /\ readers = [r \in QueryReaders |-> "idle"]
  /\ wOpenAfterEnd = TRUE
  /\ wLiveRead = TRUE
  /\ wPublishGuard = TRUE
  /\ pQueryClosed = FALSE
  /\ pOverlap = FALSE

(***************************************************************************)
(* Session phases. Sealing snapshots the acquired batches and runs the     *)
(* materialization; publication commits atomically under the session gate  *)
(* and requires the sealing state unless the mutation removes that guard.  *)
(***************************************************************************)
StartSeal ==
  /\ session = "constructing"
  /\ session' = "sealing"
  /\ UNCHANGED << everPublished, term, termRequested, mat, readers,
                  wOpenAfterEnd, wLiveRead, wPublishGuard, pQueryClosed,
                  pOverlap >>

Publish ==
  /\ mat = "done"
  /\ IF PublishMode = "StateGuarded"
       THEN session = "sealing"
       ELSE session \in {"sealing", "disposed"}
  /\ session' = "published"
  /\ everPublished' = TRUE
  /\ wPublishGuard' = (wPublishGuard /\ (session = "sealing"))
  /\ UNCHANGED << term, termRequested, mat, readers, wOpenAfterEnd,
                  wLiveRead, pQueryClosed, pOverlap >>

(***************************************************************************)
(* Termination. The owner may request disposal at any time, including      *)
(* while sealing is mid-read. Current mechanics run begin/end/release      *)
(* without observing readers; the target settles only at quiescence.       *)
(***************************************************************************)
TermRequest ==
  /\ termRequested = FALSE
  /\ termRequested' = TRUE
  /\ UNCHANGED << session, everPublished, term, mat, readers, wOpenAfterEnd,
                  wLiveRead, wPublishGuard, pQueryClosed, pOverlap >>

TermBegin ==
  /\ termRequested = TRUE
  /\ term = "idle"
  /\ term' = "begun"
  /\ session' = "disposed"
  /\ pOverlap' =
      (\/ pOverlap
       \/ mat = "reading"
       \/ \E r \in QueryReaders : readers[r] = "open")
  /\ UNCHANGED << everPublished, termRequested, mat, readers, wOpenAfterEnd,
                  wLiveRead, wPublishGuard, pQueryClosed >>

TermEnd ==
  /\ ReleaseMode = "Immediate"
  /\ term = "begun"
  /\ term' = "ended"
  /\ UNCHANGED << session, everPublished, termRequested, mat, readers,
                  wOpenAfterEnd, wLiveRead, wPublishGuard, pQueryClosed,
                  pOverlap >>

TermRelease ==
  /\ ReleaseMode = "Immediate"
  /\ term = "ended"
  /\ term' = "released"
  /\ UNCHANGED << session, everPublished, termRequested, mat, readers,
                  wOpenAfterEnd, wLiveRead, wPublishGuard, pQueryClosed,
                  pOverlap >>

TermSettle ==
  /\ ReleaseMode = "AwaitQuiescence"
  /\ term = "begun"
  /\ Quiescent
  /\ term' = "released"
  /\ UNCHANGED << session, everPublished, termRequested, mat, readers,
                  wOpenAfterEnd, wLiveRead, wPublishGuard, pQueryClosed,
                  pOverlap >>

TermDone ==
  /\ term = "released"
  /\ term' = "done"
  /\ UNCHANGED << session, everPublished, termRequested, mat, readers,
                  wOpenAfterEnd, wLiveRead, wPublishGuard, pQueryClosed,
                  pOverlap >>

(***************************************************************************)
(* Sealing materialization. Validation models `ArtifactContribution.       *)
(* OpenRead`'s `EnsureAccess` flag checks passing; in Recheck mode the     *)
(* opener then runs unconditionally, while in Gated mode admitting the     *)
(* open is atomic with the ended/draining decision. The read step models   *)
(* the copy loop's next chunk against the adapter-lease-backed stream.     *)
(***************************************************************************)
MatValidate ==
  /\ session = "sealing"
  /\ mat = "idle"
  /\ ~Ended
  /\ ~Draining
  /\ mat' = "validated"
  /\ UNCHANGED << session, everPublished, term, termRequested, readers,
                  wOpenAfterEnd, wLiveRead, wPublishGuard, pQueryClosed,
                  pOverlap >>

MatOpenRecheck ==
  /\ OpenMode = "Recheck"
  /\ mat = "validated"
  /\ mat' = "reading"
  /\ wOpenAfterEnd' = (wOpenAfterEnd /\ ~Ended)
  /\ UNCHANGED << session, everPublished, term, termRequested, readers,
                  wLiveRead, wPublishGuard, pQueryClosed, pOverlap >>

MatOpenGated ==
  /\ OpenMode = "Gated"
  /\ mat = "validated"
  /\ ~Ended
  /\ ~Draining
  /\ mat' = "reading"
  /\ wOpenAfterEnd' = (wOpenAfterEnd /\ ~Ended)
  /\ UNCHANGED << session, everPublished, term, termRequested, readers,
                  wLiveRead, wPublishGuard, pQueryClosed, pOverlap >>

MatRejectClosed ==
  /\ OpenMode = "Gated"
  /\ mat = "validated"
  /\ (Ended \/ Draining)
  /\ mat' = "rejected"
  /\ UNCHANGED << session, everPublished, term, termRequested, readers,
                  wOpenAfterEnd, wLiveRead, wPublishGuard, pQueryClosed,
                  pOverlap >>

MatReadOk ==
  /\ mat = "reading"
  /\ ~LeasesReleased
  /\ mat' = "done"
  /\ wLiveRead' = (wLiveRead /\ ~LeasesReleased)
  /\ UNCHANGED << session, everPublished, term, termRequested, readers,
                  wOpenAfterEnd, wPublishGuard, pQueryClosed, pOverlap >>

MatReadTorn ==
  /\ mat = "reading"
  /\ LeasesReleased
  /\ mat' = "failed"
  /\ wLiveRead' = (wLiveRead /\ ~LeasesReleased)
  /\ UNCHANGED << session, everPublished, term, termRequested, readers,
                  wOpenAfterEnd, wPublishGuard, pQueryClosed, pOverlap >>

(***************************************************************************)
(* Query opens. Validation models `ValidateQueryLease` under the           *)
(* authority gate; the Recheck path then models `RetainedArtifactContent.  *)
(* OpenRead`'s flag-only `EnsureAccess` followed by the unconditional      *)
(* opener. A returned stream stays readable until its consumer closes it,  *)
(* per the product's documented open-stream contract.                      *)
(***************************************************************************)
ReaderValidate(r) ==
  /\ session = "published"
  /\ readers[r] = "idle"
  /\ ~Ended
  /\ ~Draining
  /\ readers' = [readers EXCEPT ![r] = "validated"]
  /\ UNCHANGED << session, everPublished, term, termRequested, mat,
                  wOpenAfterEnd, wLiveRead, wPublishGuard, pQueryClosed,
                  pOverlap >>

ReaderRecheck(r) ==
  /\ OpenMode = "Recheck"
  /\ readers[r] = "validated"
  /\ ~Ended
  /\ ~Draining
  /\ readers' = [readers EXCEPT ![r] = "checking"]
  /\ UNCHANGED << session, everPublished, term, termRequested, mat,
                  wOpenAfterEnd, wLiveRead, wPublishGuard, pQueryClosed,
                  pOverlap >>

ReaderOpenRecheck(r) ==
  /\ OpenMode = "Recheck"
  /\ readers[r] = "checking"
  /\ readers' = [readers EXCEPT ![r] = "open"]
  /\ wOpenAfterEnd' = (wOpenAfterEnd /\ ~Ended)
  /\ UNCHANGED << session, everPublished, term, termRequested, mat,
                  wLiveRead, wPublishGuard, pQueryClosed, pOverlap >>

ReaderOpenGated(r) ==
  /\ OpenMode = "Gated"
  /\ readers[r] = "validated"
  /\ ~Ended
  /\ ~Draining
  /\ readers' = [readers EXCEPT ![r] = "open"]
  /\ wOpenAfterEnd' = (wOpenAfterEnd /\ ~Ended)
  /\ UNCHANGED << session, everPublished, term, termRequested, mat,
                  wLiveRead, wPublishGuard, pQueryClosed, pOverlap >>

ReaderRejected(r) ==
  /\ readers[r] = "validated"
  /\ (Ended \/ Draining)
  /\ readers' = [readers EXCEPT ![r] = "rejected"]
  /\ UNCHANGED << session, everPublished, term, termRequested, mat,
                  wOpenAfterEnd, wLiveRead, wPublishGuard, pQueryClosed,
                  pOverlap >>

ReaderClose(r) ==
  /\ readers[r] = "open"
  /\ readers' = [readers EXCEPT ![r] = "closed"]
  /\ pQueryClosed' = TRUE
  /\ UNCHANGED << session, everPublished, term, termRequested, mat,
                  wOpenAfterEnd, wLiveRead, wPublishGuard, pOverlap >>

Next ==
  \/ StartSeal
  \/ Publish
  \/ TermRequest
  \/ TermBegin
  \/ TermEnd
  \/ TermRelease
  \/ TermSettle
  \/ TermDone
  \/ MatValidate
  \/ MatOpenRecheck
  \/ MatOpenGated
  \/ MatRejectClosed
  \/ MatReadOk
  \/ MatReadTorn
  \/ \E r \in QueryReaders :
       \/ ReaderValidate(r)
       \/ ReaderRecheck(r)
       \/ ReaderOpenRecheck(r)
       \/ ReaderOpenGated(r)
       \/ ReaderRejected(r)
       \/ ReaderClose(r)

\* Weak fairness on every internal step: an admitted open eventually reads
\* and closes, a rejection eventually settles, and a requested termination
\* eventually progresses. `TermRequest`, `StartSeal`, `Publish`, and
\* `ReaderValidate` stay unfair: the owner and consumers may simply never
\* act, and no liveness claim depends on them acting.
Fairness ==
  /\ WF_vars(TermBegin)
  /\ WF_vars(TermEnd)
  /\ WF_vars(TermRelease)
  /\ WF_vars(TermSettle)
  /\ WF_vars(TermDone)
  /\ WF_vars(MatOpenRecheck)
  /\ WF_vars(MatOpenGated)
  /\ WF_vars(MatRejectClosed)
  /\ WF_vars(MatReadOk)
  /\ WF_vars(MatReadTorn)
  /\ \A r \in QueryReaders : WF_vars(ReaderRecheck(r))
  /\ \A r \in QueryReaders : WF_vars(ReaderOpenRecheck(r))
  /\ \A r \in QueryReaders : WF_vars(ReaderOpenGated(r))
  /\ \A r \in QueryReaders : WF_vars(ReaderRejected(r))
  /\ \A r \in QueryReaders : WF_vars(ReaderClose(r))

Spec == Init /\ [][Next]_vars /\ Fairness

(***************************************************************************)
(* Safety.                                                                 *)
(***************************************************************************)

\* The contract `EndGeneration` documents: it "rejects every future open
\* or mint". Re-derived at every open-completing step independently of
\* that step's own guard.
OpensNeverCompleteAfterEnd == wOpenAfterEnd

\* No materialization read chunk is served while the acquisition leases
\* backing its stream are disposed: disposal "must not invalidate content
\* under an active callback".
ReadsSeeLiveLeases == wLiveRead

\* Publication commits only from the sealing state; disposal during
\* sealing can never publish. Mirrors the existing product test gate
\* `ArtifactSetSession_DisposalDuringSealCannotPublish`.
PublishRequiresActiveSealing == wPublishGuard

\* The generation never ends while a materialization read is in flight or
\* a query stream is open: ending is the content-release linearization
\* point, so it must observe content quiescence.
EndImpliesContentQuiescent ==
  Ended =>
    /\ mat # "reading"
    /\ \A r \in QueryReaders : readers[r] # "open"

\* Termination owns the disposed session state.
SessionTermCoherence == (term # "idle") => (session = "disposed")

(***************************************************************************)
(* Reachability probes. Each is checked negated in a probe configuration   *)
(* that is expected to fail, proving the behavior is genuinely reachable   *)
(* rather than vacuously safe.                                             *)
(***************************************************************************)

ProbeNoQueryRoundTrip == pQueryClosed = FALSE

ProbeNoOverlappedTermination == ~(pOverlap /\ term = "done")

(***************************************************************************)
(* Liveness.                                                               *)
(***************************************************************************)

\* A requested termination eventually completes, including when it must
\* first drain a mid-read materialization or open query streams. This is
\* the obligation the target design takes on by waiting: it holds only
\* because draining rejects new opens and every open stream is eventually
\* closed by its consumer (weak fairness on the read and close steps).
TerminationEventuallyCompletes == termRequested ~> (term = "done")

\* An admitted or in-flight materialization read eventually settles.
MaterializerEventuallySettles ==
  (mat \in {"validated", "reading"}) ~>
    (mat \in {"done", "failed", "rejected"})

\* Every consumer past validation eventually closes or is rejected.
ReadersEventuallySettle ==
  \A r \in QueryReaders :
    (readers[r] \in {"validated", "checking", "open"}) ~>
      (readers[r] \in {"closed", "rejected"})

=============================================================================
