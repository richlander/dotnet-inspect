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
(* Generation end and backing-resource release are deliberately distinct   *)
(* events. The product's access contract keeps a stream that was already   *)
(* returned valid after `EndGeneration` and rejects only later opens       *)
(* (`ArtifactAccess.cs:168-171`,                                           *)
(* `ArtifactContractTests.cs:172-226`), so "ended while an admitted        *)
(* stream remains open" is a documented-safe state in every mode. The      *)
(* lifetime defect the model targets is release: disposing the backing     *)
(* acquisition leases while an admitted read or stream is still live.      *)
(*                                                                         *)
(* Two policy dimensions distinguish the target design from the current    *)
(* mechanics (`src/DotnetInspector.Artifacts/ArtifactAccess.cs`,           *)
(* `src/DotnetInspector.Artifacts.Workspaces/ArtifactSetSession.cs`):      *)
(*                                                                         *)
(*   OpenMode = "Gated"    target: a validated open registers itself       *)
(*                         atomically with the authority's ended decision, *)
(*                         so ending the generation and admitting an open  *)
(*                         are ordered by one gate.                        *)
(*   OpenMode = "Recheck"  current: `ArtifactContribution.OpenRead` and    *)
(*                         `RetainedArtifactContent.OpenRead` re-read      *)
(*                         volatile flags outside the gate and then run    *)
(*                         the opener unconditionally, so an open can      *)
(*                         complete strictly after `EndGeneration`.        *)
(*                                                                         *)
(*   ReleaseMode = "AwaitQuiescence"                                       *)
(*                         target: termination ends the generation         *)
(*                         immediately (closing new access), cancels       *)
(*                         registered openers and a materialization read   *)
(*                         it owns, and releases acquisition leases only   *)
(*                         after no registered access remains.             *)
(*   ReleaseMode = "Immediate"                                             *)
(*                         current: `TerminateAsync` sets the disposed     *)
(*                         state, calls `EndGeneration`, and disposes      *)
(*                         every acquisition lease without waiting for an  *)
(*                         in-flight sealing read or open stream.          *)
(*                                                                         *)
(* OpeningCancelMode = "Disabled" is a mutation that removes target owner  *)
(* cancellation of registered openers. PublishMode = "Unguarded" removes  *)
(* publication's sealing-state check (the guard the existing test          *)
(* `ArtifactSetSession_DisposalDuringSealCannotPublish` gates).            *)
(*                                                                         *)
(* Product concept                              Model state                *)
(*   session state (constructing/sealing/        session                   *)
(*     published/disposed)                                                 *)
(*   `TerminateAsync` progress                   term                      *)
(*   authority `_ended` after `EndGeneration`    Ended (derived)           *)
(*   acquisition leases disposed                 LeasesReleased (derived)  *)
(*   sealing materialization open/read           mat, matRegistered        *)
(*   query consumers opening retained content    readers, readerRegistered *)
(*                                                                         *)
(* Guard witnesses are latching booleans initialized TRUE; the step that   *)
(* completes an open, performs a read, releases leases, or publishes       *)
(* re-derives the condition the design requires from pre-step state and    *)
(* conjoins it, so a weakened guard fails the paired invariant.            *)
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
(*                                                                         *)
(* Fairness is calibrated to what the product can actually guarantee.      *)
(* `MatOpenComplete`, `ReaderOpenComplete`, and `MatReadOk` are NOT fair:   *)
(* the synchronous `Func<Stream>` opener and adapter-backed read have no   *)
(* bounded-completion contract. The target design therefore includes      *)
(* owner-triggered cancellation of a registered opener and materialization *)
(* read. `ReaderClose` IS fair, which encodes the assumption that every    *)
(* query consumer eventually disposes its returned stream; that assumption *)
(* is a stated obligation on the owning design, not something the authority*)
(* can enforce today.                                                      *)
(***************************************************************************)
EXTENDS Naturals, FiniteSets, TLC

CONSTANTS
  QueryReaders,  \* finite set of concurrent query consumers
  OpenMode,      \* "Gated" (target) or "Recheck" (current)
  ReleaseMode,   \* "AwaitQuiescence" (target) or "Immediate" (current)
  OpeningCancelMode, \* "Enabled" (target) or "Disabled" (mutation)
  PublishMode    \* "StateGuarded" (product) or "Unguarded" (mutation)

ASSUME Cardinality(QueryReaders) >= 2
ASSUME OpenMode \in {"Gated", "Recheck"}
ASSUME ReleaseMode \in {"AwaitQuiescence", "Immediate"}
ASSUME OpeningCancelMode \in {"Enabled", "Disabled"}
ASSUME PublishMode \in {"StateGuarded", "Unguarded"}

VARIABLES
  session,        \* "constructing" | "sealing" | "published" | "disposed"
  everPublished,  \* latched: publication committed at least once
  term,           \* "idle" | "begun" | "ended" | "released" | "done"
  termRequested,  \* the owner has requested disposal
  mat,            \* materialization access: "idle" | "validated"
                  \*   | "opening" | "reading" | "done" | "failed"
                  \*   | "rejected"
  matRegistered,  \* the target authority admitted this materializer
  readers,        \* [QueryReaders -> "idle" | "validated" | "checking"
                  \*   | "opening" | "open" | "closed" | "failed"
                  \*   | "rejected"]
  readerRegistered,\* [QueryReaders -> BOOLEAN], target admissions
  wOpenAfterEnd,  \* witness: no open completed after the generation ended
  wLiveRead,      \* witness: no read step observed released leases
  wReleaseQuiet,  \* witness: release happened only at content quiescence
  wReleaseStreams,\* witness: no query stream was open at the release step
  wPublishGuard,  \* witness: publication only from the sealing state
  pQueryClosed,   \* probe: some query open/close round trip completed
  pOverlap        \* probe: termination began during a live read or stream

vars == << session, everPublished, term, termRequested, mat, matRegistered,
           readers, readerRegistered, wOpenAfterEnd, wLiveRead,
           wReleaseQuiet, wReleaseStreams, wPublishGuard, pQueryClosed,
           pOverlap >>

(***************************************************************************)
(* Derived authority flags. In current mechanics, `TerminateAsync` orders  *)
(* the disposed state, `EndGeneration`, and lease disposal as separate     *)
(* unsynchronized steps. In the target design, ending the generation is    *)
(* part of beginning termination -- access closes immediately, exactly as  *)
(* `EndGeneration` documents -- while lease release waits for content      *)
(* quiescence. Already-returned streams survive the end in both modes,    *)
(* matching the product's documented access contract.                      *)
(***************************************************************************)
Ended ==
  IF ReleaseMode = "Immediate"
    THEN term \in {"ended", "released", "done"}
    ELSE term # "idle"

LeasesReleased == term \in {"released", "done"}

\* No opener, materialization read, or returned query stream is live.
ContentQuiescent ==
  /\ mat \notin {"opening", "reading"}
  /\ \A r \in QueryReaders : readers[r] \notin {"opening", "open"}

\* What the authority can await. In target mode every live access registers
\* before invoking its opener; Recheck mode intentionally does not.
RegisteredQuiescent ==
  /\ ~matRegistered
  /\ \A r \in QueryReaders : ~readerRegistered[r]

MatStates ==
  {"idle", "validated", "opening", "reading", "done", "failed", "rejected"}
ReaderStates ==
  {"idle", "validated", "checking", "opening", "open", "closed", "failed",
   "rejected"}

TypeOK ==
  /\ session \in {"constructing", "sealing", "published", "disposed"}
  /\ everPublished \in BOOLEAN
  /\ term \in {"idle", "begun", "ended", "released", "done"}
  /\ termRequested \in BOOLEAN
  /\ mat \in MatStates
  /\ matRegistered \in BOOLEAN
  /\ readers \in [QueryReaders -> ReaderStates]
  /\ readerRegistered \in [QueryReaders -> BOOLEAN]
  /\ wOpenAfterEnd \in BOOLEAN
  /\ wLiveRead \in BOOLEAN
  /\ wReleaseQuiet \in BOOLEAN
  /\ wReleaseStreams \in BOOLEAN
  /\ wPublishGuard \in BOOLEAN
  /\ pQueryClosed \in BOOLEAN
  /\ pOverlap \in BOOLEAN

RegistrationCoherence ==
  /\ (matRegistered
        <=> (OpenMode = "Gated" /\ mat \in {"opening", "reading"}))
  /\ \A r \in QueryReaders :
       (readerRegistered[r]
         <=> (OpenMode = "Gated" /\ readers[r] \in {"opening", "open"}))

Init ==
  /\ session = "constructing"
  /\ everPublished = FALSE
  /\ term = "idle"
  /\ termRequested = FALSE
  /\ mat = "idle"
  /\ matRegistered = FALSE
  /\ readers = [r \in QueryReaders |-> "idle"]
  /\ readerRegistered = [r \in QueryReaders |-> FALSE]
  /\ wOpenAfterEnd = TRUE
  /\ wLiveRead = TRUE
  /\ wReleaseQuiet = TRUE
  /\ wReleaseStreams = TRUE
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
  /\ UNCHANGED << everPublished, term, termRequested, mat, matRegistered,
                  readers, readerRegistered, wOpenAfterEnd, wLiveRead,
                  wReleaseQuiet, wReleaseStreams, wPublishGuard, pQueryClosed,
                  pOverlap >>

Publish ==
  /\ mat = "done"
  /\ IF PublishMode = "StateGuarded"
       THEN session = "sealing"
       ELSE session \in {"sealing", "disposed"}
  /\ session' = "published"
  /\ everPublished' = TRUE
  /\ wPublishGuard' = (wPublishGuard /\ (session = "sealing"))
  /\ UNCHANGED << term, termRequested, mat, matRegistered, readers,
                  readerRegistered, wOpenAfterEnd, wLiveRead, wReleaseQuiet,
                  wReleaseStreams, pQueryClosed, pOverlap >>

(***************************************************************************)
(* Termination. The owner may request disposal at any time, including      *)
(* while sealing is mid-read. Current mechanics run begin/end/release      *)
(* without observing readers; the target ends access at begin and          *)
(* releases only at quiescence, re-deriving that condition into a          *)
(* witness at the release step.                                            *)
(***************************************************************************)
TermRequest ==
  /\ termRequested = FALSE
  /\ termRequested' = TRUE
  /\ UNCHANGED << session, everPublished, term, mat, matRegistered, readers,
                  readerRegistered, wOpenAfterEnd, wLiveRead, wReleaseQuiet,
                  wReleaseStreams, wPublishGuard, pQueryClosed, pOverlap >>

TermBegin ==
  /\ termRequested = TRUE
  /\ term = "idle"
  /\ term' = "begun"
  /\ session' = "disposed"
  /\ pOverlap' =
      (\/ pOverlap
       \/ mat = "reading"
       \/ \E r \in QueryReaders : readers[r] = "open")
  /\ UNCHANGED << everPublished, termRequested, mat, matRegistered, readers,
                  readerRegistered, wOpenAfterEnd, wLiveRead, wReleaseQuiet,
                  wReleaseStreams, wPublishGuard, pQueryClosed >>

TermEnd ==
  /\ ReleaseMode = "Immediate"
  /\ term = "begun"
  /\ term' = "ended"
  /\ UNCHANGED << session, everPublished, termRequested, mat, matRegistered,
                  readers, readerRegistered, wOpenAfterEnd, wLiveRead,
                  wReleaseQuiet, wReleaseStreams, wPublishGuard, pQueryClosed,
                  pOverlap >>

TermRelease ==
  /\ ReleaseMode = "Immediate"
  /\ term = "ended"
  /\ term' = "released"
  /\ wReleaseQuiet' = (wReleaseQuiet /\ ContentQuiescent)
  /\ wReleaseStreams' =
      (wReleaseStreams /\ \A r \in QueryReaders : readers[r] # "open")
  /\ UNCHANGED << session, everPublished, termRequested, mat, matRegistered,
                  readers, readerRegistered, wOpenAfterEnd, wLiveRead,
                  wPublishGuard, pQueryClosed, pOverlap >>

TermSettle ==
  /\ ReleaseMode = "AwaitQuiescence"
  /\ term = "begun"
  /\ RegisteredQuiescent
  /\ term' = "released"
  /\ wReleaseQuiet' = (wReleaseQuiet /\ ContentQuiescent)
  /\ wReleaseStreams' =
      (wReleaseStreams /\ \A r \in QueryReaders : readers[r] # "open")
  /\ UNCHANGED << session, everPublished, termRequested, mat, matRegistered,
                  readers, readerRegistered, wOpenAfterEnd, wLiveRead,
                  wPublishGuard, pQueryClosed, pOverlap >>

TermDone ==
  /\ term = "released"
  /\ term' = "done"
  /\ UNCHANGED << session, everPublished, termRequested, mat, matRegistered,
                  readers, readerRegistered, wOpenAfterEnd, wLiveRead,
                  wReleaseQuiet, wReleaseStreams, wPublishGuard, pQueryClosed,
                  pOverlap >>

(***************************************************************************)
(* Sealing materialization. Validation models `ArtifactContribution.       *)
(* OpenRead`'s `EnsureAccess` flag checks passing. In Recheck mode the      *)
(* opener begins without registration; in Gated mode registration is       *)
(* atomic with the ended decision. Opener completion is a separate, unfair *)
(* environment step because `Func<Stream>` has no bounded-completion       *)
(* contract. The read step models the copy loop's next chunk. Target       *)
(* termination cancels registered opening and reading phases; current      *)
(* mechanics have no such interruption.                                    *)
(***************************************************************************)
MatValidate ==
  /\ session = "sealing"
  /\ mat = "idle"
  /\ ~Ended
  /\ mat' = "validated"
  /\ UNCHANGED << session, everPublished, term, termRequested, matRegistered,
                  readers, readerRegistered, wOpenAfterEnd, wLiveRead,
                  wReleaseQuiet, wReleaseStreams, wPublishGuard, pQueryClosed,
                  pOverlap >>

MatOpenRecheck ==
  /\ OpenMode = "Recheck"
  /\ mat = "validated"
  /\ mat' = "opening"
  /\ matRegistered' = FALSE
  /\ UNCHANGED << session, everPublished, term, termRequested, readers,
                  readerRegistered, wOpenAfterEnd, wLiveRead, wReleaseQuiet,
                  wReleaseStreams, wPublishGuard, pQueryClosed, pOverlap >>

MatOpenGated ==
  /\ OpenMode = "Gated"
  /\ mat = "validated"
  /\ ~Ended
  /\ mat' = "opening"
  /\ matRegistered' = TRUE
  /\ UNCHANGED << session, everPublished, term, termRequested, readers,
                  readerRegistered, wOpenAfterEnd, wLiveRead, wReleaseQuiet,
                  wReleaseStreams, wPublishGuard, pQueryClosed, pOverlap >>

MatRejectClosed ==
  /\ OpenMode = "Gated"
  /\ mat = "validated"
  /\ Ended
  /\ mat' = "rejected"
  /\ matRegistered' = FALSE
  /\ UNCHANGED << session, everPublished, term, termRequested, readers,
                  readerRegistered, wOpenAfterEnd, wLiveRead, wReleaseQuiet,
                  wReleaseStreams, wPublishGuard, pQueryClosed, pOverlap >>

MatOpenComplete ==
  /\ mat = "opening"
  /\ mat' = "reading"
  /\ wOpenAfterEnd' = (wOpenAfterEnd /\ (~Ended \/ matRegistered))
  /\ UNCHANGED << session, everPublished, term, termRequested, matRegistered,
                  readers, readerRegistered, wLiveRead, wReleaseQuiet,
                  wReleaseStreams, wPublishGuard, pQueryClosed, pOverlap >>

MatOpenFailed ==
  /\ mat = "opening"
  /\ mat' = "failed"
  /\ matRegistered' = FALSE
  /\ UNCHANGED << session, everPublished, term, termRequested, readers,
                  readerRegistered, wOpenAfterEnd, wLiveRead, wReleaseQuiet,
                  wReleaseStreams, wPublishGuard, pQueryClosed, pOverlap >>

MatOpenCancelled ==
  /\ ReleaseMode = "AwaitQuiescence"
  /\ OpeningCancelMode = "Enabled"
  /\ mat = "opening"
  /\ matRegistered
  /\ Ended
  /\ mat' = "failed"
  /\ matRegistered' = FALSE
  /\ UNCHANGED << session, everPublished, term, termRequested, readers,
                  readerRegistered, wOpenAfterEnd, wLiveRead, wReleaseQuiet,
                  wReleaseStreams, wPublishGuard, pQueryClosed, pOverlap >>

MatReadOk ==
  /\ mat = "reading"
  /\ ~LeasesReleased
  /\ mat' = "done"
  /\ matRegistered' = FALSE
  /\ wLiveRead' = (wLiveRead /\ ~LeasesReleased)
  /\ UNCHANGED << session, everPublished, term, termRequested, readers,
                  readerRegistered, wOpenAfterEnd, wReleaseQuiet,
                  wReleaseStreams, wPublishGuard, pQueryClosed, pOverlap >>

MatReadTorn ==
  /\ mat = "reading"
  /\ LeasesReleased
  /\ mat' = "failed"
  /\ matRegistered' = FALSE
  /\ wLiveRead' = (wLiveRead /\ ~LeasesReleased)
  /\ UNCHANGED << session, everPublished, term, termRequested, readers,
                  readerRegistered, wOpenAfterEnd, wReleaseQuiet,
                  wReleaseStreams, wPublishGuard, pQueryClosed, pOverlap >>

MatReadCancelled ==
  /\ ReleaseMode = "AwaitQuiescence"
  /\ mat = "reading"
  /\ matRegistered
  /\ Ended
  /\ mat' = "failed"
  /\ matRegistered' = FALSE
  /\ UNCHANGED << session, everPublished, term, termRequested, readers,
                  readerRegistered, wOpenAfterEnd, wLiveRead, wReleaseQuiet,
                  wReleaseStreams, wPublishGuard, pQueryClosed, pOverlap >>

(***************************************************************************)
(* Query opens. Validation models `ValidateQueryLease` under the           *)
(* authority gate. Recheck begins the opener without registration; Gated   *)
(* registers before invoking it. Completion is an unfair environment step  *)
(* because `Func<Stream>` has no bounded-completion contract. Target        *)
(* termination can cancel a registered opener. A returned stream stays     *)
(* readable until its consumer closes it, so no action forces an open       *)
(* reader shut when the generation ends.                                   *)
(***************************************************************************)
ReaderValidate(r) ==
  /\ session = "published"
  /\ readers[r] = "idle"
  /\ ~Ended
  /\ readers' = [readers EXCEPT ![r] = "validated"]
  /\ UNCHANGED << session, everPublished, term, termRequested, mat,
                  matRegistered, readerRegistered, wOpenAfterEnd, wLiveRead,
                  wReleaseQuiet, wReleaseStreams, wPublishGuard, pQueryClosed,
                  pOverlap >>

ReaderRecheck(r) ==
  /\ OpenMode = "Recheck"
  /\ readers[r] = "validated"
  /\ ~Ended
  /\ readers' = [readers EXCEPT ![r] = "checking"]
  /\ UNCHANGED << session, everPublished, term, termRequested, mat,
                  matRegistered, readerRegistered, wOpenAfterEnd, wLiveRead,
                  wReleaseQuiet, wReleaseStreams, wPublishGuard, pQueryClosed,
                  pOverlap >>

ReaderOpenRecheck(r) ==
  /\ OpenMode = "Recheck"
  /\ readers[r] = "checking"
  /\ readers' = [readers EXCEPT ![r] = "opening"]
  /\ readerRegistered' =
      [readerRegistered EXCEPT ![r] = FALSE]
  /\ UNCHANGED << session, everPublished, term, termRequested, mat,
                  matRegistered, wOpenAfterEnd, wLiveRead, wReleaseQuiet,
                  wReleaseStreams, wPublishGuard, pQueryClosed, pOverlap >>

ReaderOpenGated(r) ==
  /\ OpenMode = "Gated"
  /\ readers[r] = "validated"
  /\ ~Ended
  /\ readers' = [readers EXCEPT ![r] = "opening"]
  /\ readerRegistered' =
      [readerRegistered EXCEPT ![r] = TRUE]
  /\ UNCHANGED << session, everPublished, term, termRequested, mat,
                  matRegistered, wOpenAfterEnd, wLiveRead, wReleaseQuiet,
                  wReleaseStreams, wPublishGuard, pQueryClosed, pOverlap >>

ReaderOpenComplete(r) ==
  /\ readers[r] = "opening"
  /\ readers' = [readers EXCEPT ![r] = "open"]
  /\ wOpenAfterEnd' =
      (wOpenAfterEnd /\ (~Ended \/ readerRegistered[r]))
  /\ UNCHANGED << session, everPublished, term, termRequested, mat,
                  matRegistered, readerRegistered, wLiveRead, wReleaseQuiet,
                  wReleaseStreams, wPublishGuard, pQueryClosed, pOverlap >>

ReaderOpenFailed(r) ==
  /\ readers[r] = "opening"
  /\ readers' = [readers EXCEPT ![r] = "failed"]
  /\ readerRegistered' =
      [readerRegistered EXCEPT ![r] = FALSE]
  /\ UNCHANGED << session, everPublished, term, termRequested, mat,
                  matRegistered, wOpenAfterEnd, wLiveRead, wReleaseQuiet,
                  wReleaseStreams, wPublishGuard, pQueryClosed, pOverlap >>

ReaderOpenCancelled(r) ==
  /\ ReleaseMode = "AwaitQuiescence"
  /\ OpeningCancelMode = "Enabled"
  /\ readers[r] = "opening"
  /\ readerRegistered[r]
  /\ Ended
  /\ readers' = [readers EXCEPT ![r] = "failed"]
  /\ readerRegistered' =
      [readerRegistered EXCEPT ![r] = FALSE]
  /\ UNCHANGED << session, everPublished, term, termRequested, mat,
                  matRegistered, wOpenAfterEnd, wLiveRead, wReleaseQuiet,
                  wReleaseStreams, wPublishGuard, pQueryClosed, pOverlap >>

ReaderRejected(r) ==
  /\ readers[r] = "validated"
  /\ Ended
  /\ readers' = [readers EXCEPT ![r] = "rejected"]
  /\ readerRegistered' =
      [readerRegistered EXCEPT ![r] = FALSE]
  /\ UNCHANGED << session, everPublished, term, termRequested, mat,
                  matRegistered, wOpenAfterEnd, wLiveRead, wReleaseQuiet,
                  wReleaseStreams, wPublishGuard, pQueryClosed, pOverlap >>

ReaderClose(r) ==
  /\ readers[r] = "open"
  /\ readers' = [readers EXCEPT ![r] = "closed"]
  /\ readerRegistered' =
      [readerRegistered EXCEPT ![r] = FALSE]
  /\ pQueryClosed' = TRUE
  /\ UNCHANGED << session, everPublished, term, termRequested, mat,
                  matRegistered, wOpenAfterEnd, wLiveRead, wReleaseQuiet,
                  wReleaseStreams, wPublishGuard, pOverlap >>

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
  \/ MatOpenComplete
  \/ MatOpenFailed
  \/ MatOpenCancelled
  \/ MatReadOk
  \/ MatReadTorn
  \/ MatReadCancelled
  \/ \E r \in QueryReaders :
       \/ ReaderValidate(r)
       \/ ReaderRecheck(r)
       \/ ReaderOpenRecheck(r)
       \/ ReaderOpenGated(r)
       \/ ReaderOpenComplete(r)
       \/ ReaderOpenFailed(r)
       \/ ReaderOpenCancelled(r)
       \/ ReaderRejected(r)
       \/ ReaderClose(r)

\* Weak fairness on rejection, cancellation, close, and termination
\* progress. Unfair by design: `TermRequest`, `StartSeal`, `Publish`,
\* `MatValidate`, and `ReaderValidate` (the owner and consumers may simply
\* never act), opener completion/failure (the synchronous callback has no
\* bounded-completion contract), and `MatReadOk` (an adapter-backed read may
\* stall forever). Target termination compensates with fair cancellation of
\* registered opening and reading phases.
\* `ReaderClose` fairness encodes the consumer obligation to eventually
\* dispose a returned stream.
Fairness ==
  /\ WF_vars(TermBegin)
  /\ WF_vars(TermEnd)
  /\ WF_vars(TermRelease)
  /\ WF_vars(TermSettle)
  /\ WF_vars(TermDone)
  /\ WF_vars(MatOpenRecheck)
  /\ WF_vars(MatOpenGated)
  /\ WF_vars(MatRejectClosed)
  /\ WF_vars(MatOpenCancelled)
  /\ WF_vars(MatReadTorn)
  /\ WF_vars(MatReadCancelled)
  /\ \A r \in QueryReaders : WF_vars(ReaderRecheck(r))
  /\ \A r \in QueryReaders : WF_vars(ReaderOpenRecheck(r))
  /\ \A r \in QueryReaders : WF_vars(ReaderOpenGated(r))
  /\ \A r \in QueryReaders : WF_vars(ReaderOpenCancelled(r))
  /\ \A r \in QueryReaders : WF_vars(ReaderRejected(r))
  /\ \A r \in QueryReaders : WF_vars(ReaderClose(r))

Spec == Init /\ [][Next]_vars /\ Fairness

(***************************************************************************)
(* Safety.                                                                 *)
(***************************************************************************)

\* The contract `EndGeneration` documents: it "rejects every future open
\* or mint". Re-derived at every open-completing step. A target open
\* registered before the end may complete after it; an unregistered Recheck
\* opener may not. An already-returned stream surviving the end is also not
\* a violation.
OpensNeverCompleteAfterEnd == wOpenAfterEnd

\* No materialization read chunk is served while the acquisition leases
\* backing its stream are disposed: disposal "must not invalidate content
\* under an active callback".
ReadsSeeLiveLeases == wLiveRead

\* Every live target opener, read, and returned stream is represented by an
\* authority registration, and Recheck mode has no such registrations.
\* This is the connection that makes `RegisteredQuiescent` sufficient for
\* target release while exposing the current mechanics' observation gap.
AccessRegistrationsMatchLiveContent == RegistrationCoherence

\* Backing-resource release happens only at content quiescence -- no
\* in-flight materialization read and no open query stream. This is the
\* design's "releases all child leases after every dependent group is
\* quiescent" specialized to content access. Both the structural form and
\* the witness re-derived at the release step are checked.
ReleaseImpliesContentQuiescent == LeasesReleased => ContentQuiescent

ReleaseQuiescenceWitnessHolds == wReleaseQuiet

\* The query-stream projection of the release rule, re-derived at the
\* release step itself: it fails only when a query stream is open at the
\* moment leases are released. The current-mechanics negative control
\* checks this so its counterexample must show a published generation
\* whose open stream is overtaken by release, not the earlier
\* materialization violation.
QueryStreamReleaseWitnessHolds == wReleaseStreams

\* Publication commits only from the sealing state; disposal during
\* sealing can never publish. Mirrors the existing product test gate
\* `ArtifactSetSession_DisposalDuringSealCannotPublish`.
PublishRequiresActiveSealing == wPublishGuard

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
\* first drain a mid-read materialization or open query streams. In the
\* target design this holds because ending the generation rejects new
\* opens, an in-flight materialization read is cancelled by its owner, and
\* every consumer eventually closes its stream (the `ReaderClose`
\* fairness assumption). It does NOT assume an adapter-backed read
\* completes on its own.
TerminationEventuallyCompletes == termRequested ~> (term = "done")

\* Once termination is requested, a validated, registered opener, or
\* materialization read eventually settles. Without termination the product
\* has nothing that interrupts a stalled opener or adapter read, so no
\* unconditional settlement claim is made.
TerminationSettlesMaterialization ==
  (termRequested /\ mat \in {"validated", "opening", "reading"}) ~>
    (mat \in {"done", "failed", "rejected"})

\* Once termination is requested, every consumer past validation eventually
\* closes, fails, or is rejected. No unconditional opener-completion claim is
\* made.
TerminationSettlesReaders ==
  \A r \in QueryReaders :
    (termRequested
      /\ readers[r] \in {"validated", "checking", "opening", "open"}) ~>
      (readers[r] \in {"closed", "failed", "rejected"})

=============================================================================
