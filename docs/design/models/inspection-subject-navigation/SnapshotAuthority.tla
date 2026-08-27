-------------------------- MODULE SnapshotAuthority --------------------------
(***************************************************************************)
(* Design model of retained versus stateless navigation execution.          *)
(*                                                                          *)
(* A retained operation reads prior state only from its navigation session,  *)
(* and a caller cannot supply a second retained-state value.  A stateless    *)
(* evaluation may consume an explicit prior snapshot as data and retains     *)
(* nothing.  The model checks those two execution modes and the effect       *)
(* authority that guards a result.  It says nothing about how a snapshot is  *)
(* computed, how lenses are ranked, or what a lens contains.                 *)
(*                                                                          *)
(* Product concept                       Model variable                      *)
(*   the session's installed snapshot      installed                         *)
(*   how many retained commits happened    retainedCommits                   *)
(*   the operation now executing           command                           *)
(*   the returned navigation result        result                            *)
(*   the session's effect epoch            effectEpoch                       *)
(*   the unconsumed effect authority       effect                            *)
(*   authority held by a consumer          hostAuthority                     *)
(*                                                                          *)
(* Snapshots carry provenance.  A session snapshot records the origin of the *)
(* snapshot it was derived from, and only session snapshots can carry a      *)
(* session lens.  A caller- or foreign-supplied snapshot carries a lens that *)
(* a session snapshot never has, so a committed retained lens that came from *)
(* caller data is detectable rather than indistinguishable.                  *)
(*                                                                          *)
(* `basisWitness`, `snapshotStabilityWitness`, `rejectionAuthorityWitness`,  *)
(* and `executeWitness` are latching booleans.  Each re-derives,             *)
(* independently of the action's own guard, the exact condition the design   *)
(* requires for the step just taken, so a later weakening of a guard breaks  *)
(* the paired invariant.  `snapshotStabilityWitness` compares the whole      *)
(* installed snapshot record, not its revision, so a step that rewrote the   *)
(* snapshot's lens or provenance while leaving the revision alone is caught  *)
(* too.                                                                     *)
(***************************************************************************)
EXTENDS Naturals

CONSTANTS
  MaxCommands,      \* how many operations one behaviour may submit
  SessionLenses,    \* lens identities a session snapshot can carry
  ForeignLenses,    \* lens identities only caller or foreign data can carry
  SessionId,        \* the identity of this retained navigation session
  ForeignSessionId  \* some other session, used only for foreign authority

ASSUME MaxCommands \in Nat
ASSUME SessionLenses \cap ForeignLenses = {}
ASSUME SessionId # ForeignSessionId

VARIABLES
  installed,
  retainedCommits,
  command,
  result,
  effectEpoch,
  effect,
  hostAuthority,
  commandsIssued,
  basisWitness,
  snapshotStabilityWitness,
  rejectionAuthorityWitness,
  executeWitness

vars == << installed, retainedCommits, command, result, effectEpoch, effect,
           hostAuthority, commandsIssued, basisWitness,
           snapshotStabilityWitness, rejectionAuthorityWitness,
           executeWitness >>

Modes == {"retained", "stateless"}
AllLenses == SessionLenses \cup ForeignLenses

NoSnapshot ==
  [origin |-> "none", rev |-> 0, lens |-> "none", derivedFrom |-> "none"]

\* Prior state a caller might hand in explicitly.  "caller" is a snapshot the
\* consumer invented, "foreign" is one from another session or host, and
\* "session" is a stale copy of this session's own earlier snapshot that the
\* consumer kept.  In retained mode all three are equally inadmissible: the
\* rule is about who owns the prior state, not about whether the value looks
\* plausible.
SuppliedSnapshots ==
  { [origin |-> o, rev |-> 1, lens |-> l, derivedFrom |-> o] :
      o \in {"caller", "foreign"}, l \in ForeignLenses }
  \cup
  { [origin |-> "session", rev |-> 0, lens |-> l, derivedFrom |-> "session"] :
      l \in SessionLenses }

LensesOfSnapshot(s) ==
  IF s.origin = "session" THEN SessionLenses ELSE ForeignLenses

NoCommand == [mode |-> "none", lens |-> "none", prior |-> NoSnapshot]
NoResult ==
  [mode |-> "none", outcome |-> "none", lens |-> "none", basis |-> "none",
   reason |-> "none"]

Authority(rev, intent, epoch) ==
  [session |-> SessionId, rev |-> rev, intent |-> intent, epoch |-> epoch]

NoAuthority == [session |-> "none", rev |-> 0, intent |-> 0, epoch |-> 0]

ForeignAuthority ==
  [session |-> ForeignSessionId, rev |-> 1, intent |-> 1, epoch |-> 1]

\* TypeOK only types the state.  Whether the installed snapshot is
\* session-owned is a safety claim, checked by NoForeignRetainedState.
TypeOK ==
  /\ installed.origin \in {"session", "caller", "foreign"}
  /\ installed.lens \in AllLenses
  /\ installed.rev \in Nat
  /\ retainedCommits \in Nat
  /\ command.mode \in Modes \cup {"none"}
  /\ command.lens \in AllLenses \cup {"none"}
  /\ command.prior \in SuppliedSnapshots \cup {NoSnapshot}
  /\ result.mode \in Modes \cup {"none"}
  /\ result.outcome \in {"applied", "rejected", "none"}
  /\ commandsIssued \in 0 .. MaxCommands
  /\ effectEpoch \in Nat
  /\ basisWitness \in BOOLEAN
  /\ snapshotStabilityWitness \in BOOLEAN
  /\ rejectionAuthorityWitness \in BOOLEAN
  /\ executeWitness \in BOOLEAN

Init ==
  /\ installed = [origin |-> "session", rev |-> 0,
                  lens |-> CHOOSE l \in SessionLenses : TRUE,
                  derivedFrom |-> "initial"]
  /\ retainedCommits = 0
  /\ command = NoCommand
  /\ result = NoResult
  /\ effectEpoch = 0
  /\ effect = NoAuthority
  /\ hostAuthority = NoAuthority
  /\ commandsIssued = 0
  /\ basisWitness = TRUE
  /\ snapshotStabilityWitness = TRUE
  /\ rejectionAuthorityWitness = TRUE
  /\ executeWitness = TRUE

(***************************************************************************)
(* Submitting an operation.  A retained submission may carry prior state    *)
(* the caller invented; a stateless submission may carry an explicit prior   *)
(* snapshot legitimately.  Either way a new operation supersedes unconsumed  *)
(* authority, which is how a consumer can end up holding a stale one.        *)
(***************************************************************************)
SubmitCommand(mode, lens, prior) ==
  /\ command = NoCommand
  /\ commandsIssued < MaxCommands
  /\ command' = [mode |-> mode, lens |-> lens, prior |-> prior]
  /\ commandsIssued' = commandsIssued + 1
  /\ effect' = NoAuthority
  /\ UNCHANGED << installed, retainedCommits, result, effectEpoch,
                  hostAuthority, basisWitness, rejectionAuthorityWitness,
                  executeWitness >>
  /\ snapshotStabilityWitness' =
       /\ snapshotStabilityWitness
       /\ installed' = installed
       /\ retainedCommits' = retainedCommits

\* A retained operation that carries explicitly supplied prior state is
\* rejected with a typed outcome, whether that value was invented by the
\* consumer, minted by another session, or is a stale copy of this session's
\* own earlier snapshot.  The session never adopts it.  The rejection is
\* still a retained result: it advances the effect epoch and comes back under
\* current retained authority so a consumer can render and acknowledge it
\* like any other outcome.
RejectSuppliedPriorState ==
  /\ command.mode = "retained"
  /\ command.prior # NoSnapshot
  /\ result' = [mode |-> "retained", outcome |-> "rejected", lens |-> "none",
                basis |-> "none", reason |-> "suppliedPriorState"]
  /\ effectEpoch' = effectEpoch + 1
  /\ effect' = Authority(installed.rev, commandsIssued, effectEpoch + 1)
  /\ hostAuthority' = effect'
  /\ command' = NoCommand
  /\ UNCHANGED << installed, retainedCommits, commandsIssued, basisWitness,
                  executeWitness >>
  /\ snapshotStabilityWitness' =
       /\ snapshotStabilityWitness
       /\ installed' = installed
       /\ retainedCommits' = retainedCommits
  /\ rejectionAuthorityWitness' =
       /\ rejectionAuthorityWitness
       /\ effectEpoch' = effectEpoch + 1
       /\ effect'.session = SessionId
       /\ effect'.rev = installed'.rev
       /\ effect'.intent = commandsIssued'
       /\ effect'.epoch = effectEpoch'
       /\ installed' = installed
       /\ retainedCommits' = retainedCommits

\* A retained lens request that names a lens the installed snapshot does not
\* offer is rejected.  The committed lens comes from the snapshot, never from
\* consumer data.  This rejection is also a retained result under current
\* authority.
RejectLensOutsideInstalledSnapshot ==
  /\ command.mode = "retained"
  /\ command.prior = NoSnapshot
  /\ command.lens \notin LensesOfSnapshot(installed)
  /\ result' = [mode |-> "retained", outcome |-> "rejected", lens |-> "none",
                basis |-> "none", reason |-> "lensNotInInstalledSnapshot"]
  /\ effectEpoch' = effectEpoch + 1
  /\ effect' = Authority(installed.rev, commandsIssued, effectEpoch + 1)
  /\ hostAuthority' = effect'
  /\ command' = NoCommand
  /\ UNCHANGED << installed, retainedCommits, commandsIssued, basisWitness,
                  executeWitness >>
  /\ snapshotStabilityWitness' =
       /\ snapshotStabilityWitness
       /\ installed' = installed
       /\ retainedCommits' = retainedCommits
  /\ rejectionAuthorityWitness' =
       /\ rejectionAuthorityWitness
       /\ effectEpoch' = effectEpoch + 1
       /\ effect'.session = SessionId
       /\ effect'.rev = installed'.rev
       /\ effect'.intent = commandsIssued'
       /\ effect'.epoch = effectEpoch'
       /\ installed' = installed
       /\ retainedCommits' = retainedCommits

(***************************************************************************)
(* Retained execution.  The basis is the installed snapshot and nothing      *)
(* else; the replacement snapshot records what it was derived from.  If this *)
(* action ever took its basis from `command.prior`, `derivedFrom` would say  *)
(* so and NoForeignRetainedState would fail.                                 *)
(***************************************************************************)
ExecuteRetained ==
  /\ command.mode = "retained"
  /\ command.prior = NoSnapshot
  /\ command.lens \in LensesOfSnapshot(installed)
  /\ LET basis == installed IN
       /\ installed' = [origin      |-> "session",
                        rev         |-> basis.rev + 1,
                        lens        |-> command.lens,
                        derivedFrom |-> basis.origin]
       /\ result' = [mode |-> "retained", outcome |-> "applied",
                     lens |-> command.lens, basis |-> basis.origin,
                     reason |-> "none"]
       /\ basisWitness' =
            /\ basisWitness
            /\ basis = installed
            /\ basis.origin = "session"
            /\ command.prior = NoSnapshot
  /\ retainedCommits' = retainedCommits + 1
  /\ effectEpoch' = effectEpoch + 1
  /\ effect' = Authority(installed.rev + 1, commandsIssued, effectEpoch + 1)
  /\ hostAuthority' = effect'
  /\ command' = NoCommand
  /\ UNCHANGED << commandsIssued, snapshotStabilityWitness,
                  rejectionAuthorityWitness, executeWitness >>

(***************************************************************************)
(* Stateless execution.  An explicit prior snapshot is ordinary data: the    *)
(* lens may be derived from it, no session state is installed, and no        *)
(* retained effect authority is issued.                                      *)
(***************************************************************************)
StatelessLensAdmissible ==
  IF command.prior = NoSnapshot
    THEN command.lens \in SessionLenses
    ELSE command.lens \in LensesOfSnapshot(command.prior)

ExecuteStateless ==
  /\ command.mode = "stateless"
  /\ StatelessLensAdmissible
  /\ result' = [mode |-> "stateless", outcome |-> "applied",
                lens |-> command.lens, basis |-> command.prior.origin,
                reason |-> "none"]
  /\ command' = NoCommand
  /\ UNCHANGED << installed, retainedCommits, effectEpoch, effect,
                  hostAuthority, commandsIssued, basisWitness,
                  rejectionAuthorityWitness, executeWitness >>
  /\ snapshotStabilityWitness' =
       /\ snapshotStabilityWitness
       /\ installed' = installed
       /\ retainedCommits' = retainedCommits

RejectStatelessLens ==
  /\ command.mode = "stateless"
  /\ ~StatelessLensAdmissible
  /\ result' = [mode |-> "stateless", outcome |-> "rejected", lens |-> "none",
                basis |-> "none", reason |-> "lensNotInSuppliedPriorState"]
  /\ command' = NoCommand
  /\ UNCHANGED << installed, retainedCommits, effectEpoch, effect,
                  hostAuthority, commandsIssued, basisWitness,
                  rejectionAuthorityWitness, executeWitness >>
  /\ snapshotStabilityWitness' =
       /\ snapshotStabilityWitness
       /\ installed' = installed
       /\ retainedCommits' = retainedCommits

(***************************************************************************)
(* Consumer side: authority validation.                                     *)
(***************************************************************************)
ExecuteEffectWork ==
  /\ hostAuthority # NoAuthority
  /\ hostAuthority = effect
  /\ executeWitness' =
       /\ executeWitness
       /\ hostAuthority.session = SessionId
       /\ hostAuthority.rev = installed.rev
       /\ hostAuthority.intent = commandsIssued
       /\ hostAuthority.epoch = effectEpoch
  /\ UNCHANGED << installed, retainedCommits, command, result, effectEpoch,
                  effect, hostAuthority, commandsIssued, basisWitness,
                  rejectionAuthorityWitness >>
  /\ snapshotStabilityWitness' =
       /\ snapshotStabilityWitness
       /\ installed' = installed
       /\ retainedCommits' = retainedCommits

AcknowledgeEffect ==
  /\ hostAuthority # NoAuthority
  /\ hostAuthority = effect
  /\ effect' = NoAuthority
  /\ hostAuthority' = NoAuthority
  /\ UNCHANGED << installed, retainedCommits, command, result, effectEpoch,
                  commandsIssued, basisWitness, rejectionAuthorityWitness,
                  executeWitness >>
  /\ snapshotStabilityWitness' =
       /\ snapshotStabilityWitness
       /\ installed' = installed
       /\ retainedCommits' = retainedCommits

AbandonEffect ==
  /\ hostAuthority # NoAuthority
  /\ hostAuthority' = NoAuthority
  /\ effect' = IF hostAuthority = effect THEN NoAuthority ELSE effect
  /\ UNCHANGED << installed, retainedCommits, command, result, effectEpoch,
                  commandsIssued, basisWitness, rejectionAuthorityWitness,
                  executeWitness >>
  /\ snapshotStabilityWitness' =
       /\ snapshotStabilityWitness
       /\ installed' = installed
       /\ retainedCommits' = retainedCommits

\* A consumer is handed authority minted by another navigation session.
ForeignAuthorityOffered ==
  /\ hostAuthority = NoAuthority
  /\ hostAuthority' = ForeignAuthority
  /\ UNCHANGED << installed, retainedCommits, command, result, effectEpoch,
                  effect, commandsIssued, basisWitness,
                  rejectionAuthorityWitness, executeWitness >>
  /\ snapshotStabilityWitness' =
       /\ snapshotStabilityWitness
       /\ installed' = installed
       /\ retainedCommits' = retainedCommits

ResolveCommand ==
  \/ RejectSuppliedPriorState
  \/ RejectLensOutsideInstalledSnapshot
  \/ ExecuteRetained
  \/ ExecuteStateless
  \/ RejectStatelessLens

Next ==
  \/ \E mode \in Modes, lens \in AllLenses,
        prior \in SuppliedSnapshots \cup {NoSnapshot} :
       SubmitCommand(mode, lens, prior)
  \/ ResolveCommand
  \/ ExecuteEffectWork
  \/ AcknowledgeEffect
  \/ AbandonEffect
  \/ ForeignAuthorityOffered

Fairness ==
  /\ WF_vars(ResolveCommand)
  /\ WF_vars(AcknowledgeEffect)

Spec == Init /\ [][Next]_vars /\ Fairness

(***************************************************************************)
(* Invariants.                                                             *)
(***************************************************************************)

\* No supplied snapshot becomes retained authority.  The installed
\* snapshot is always session-owned and always derived from session state.
NoForeignRetainedState ==
  /\ installed.origin = "session"
  /\ installed.derivedFrom \in {"session", "initial"}
  /\ installed.lens \in SessionLenses

\* Only retained execution installs state.  A stateless evaluation, a typed
\* rejection, or any authority step that changed the installed snapshot would
\* break this.
OnlyRetainedExecutionInstalls == installed.rev = retainedCommits

\* A retained operation used the installed snapshot as its only prior state.
RetainedPriorStateIsInstalledSnapshot == basisWitness

\* Only the retained apply action may replace the installed snapshot.  Every
\* other step, including stateless execution, stateless rejection, retained
\* rejection, and the authority-only steps, left the whole installed snapshot
\* record and the retained commit count exactly as they were.  This compares
\* the record itself rather than inferring stability from revision counting.
NonApplyStepsPreserveInstalledSnapshot == snapshotStabilityWitness

\* A retained typed rejection stays visible under exact current authority and
\* installs nothing: it advanced the effect epoch, returned authority naming
\* this session, the unchanged installed revision, the current operation, and
\* the new epoch, and left the whole installed snapshot record and the
\* retained commit count alone.  A rejection that adopted supplied prior state
\* would have to change that record, which this witness compares directly.
RetainedRejectionHasExactAuthorityAndInstallsNothing ==
  rejectionAuthorityWitness

\* The retained committed lens equals the installed snapshot's lens and is
\* never a lens that only caller or foreign data could have supplied.
RetainedCommittedLensEqualsInstalledLens ==
  (result.mode = "retained" /\ result.outcome = "applied") =>
    /\ result.lens = installed.lens
    /\ result.lens \in SessionLenses
    /\ result.basis = "session"

\* A stateless evaluation issues no retained effect authority.
StatelessIssuesNoRetainedAuthority ==
  (result.mode = "stateless") => effect = NoAuthority

\* Exact effect-authority validation: unconsumed authority always matches the
\* session's identity, installed revision, current operation, and epoch.
ExactCurrentAuthority ==
  effect # NoAuthority =>
    /\ effect.session = SessionId
    /\ effect.rev = installed.rev
    /\ effect.intent = commandsIssued
    /\ effect.epoch = effectEpoch

\* Stale or foreign authority never executes deferred work.
StaleOrForeignAuthorityNeverExecutes == executeWitness

(***************************************************************************)
(* Liveness and progress.                                                  *)
(***************************************************************************)
EveryCommandResolves == (command # NoCommand) ~> (command = NoCommand)

EffectEventuallyConsumed == (effect # NoAuthority) ~> (effect = NoAuthority)

\* Every retained operation that arrives carrying explicitly supplied prior
\* state reaches the typed rejection, rather than being applied or left
\* pending.  This holds for caller, foreign, and stale same-session prior
\* values alike, because the rule is about who owns retained prior state.  An
\* execution path that accepted supplied prior state would reach an applied
\* retained result instead and break this property.
SuppliedRetainedPriorStateIsAlwaysRejected ==
  (command.mode = "retained" /\ command.prior # NoSnapshot)
    ~> ( /\ result.mode = "retained"
         /\ result.outcome = "rejected"
         /\ result.reason = "suppliedPriorState" )

=============================================================================
