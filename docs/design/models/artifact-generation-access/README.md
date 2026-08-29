# ArtifactGenerationAccess

`ArtifactGenerationAccess.tla` models how content opens interact with
generation termination for one artifact generation: admission-phase
materialization reads through source-adapter acquisition leases, query-phase
opens of owner-retained content, and the `EndGeneration`/lease-disposal
sequence that `ArtifactSetSession.DisposeAsync` runs.

Owning design:
[`docs/design/artifact-acquisition-and-workspaces.md`](../../artifact-acquisition-and-workspaces.md)
(the `ArtifactSetSession` acquisition-lifetime boundary). The design requires
that the session "releases all child leases after every dependent group is
quiescent" and that disposal "must not invalidate content under an active
callback".

## Generation end is not backing-resource release

The model keeps these as distinct events, matching the product's documented
access contract: a stream that was already returned stays valid after
`EndGeneration`, which rejects only later opens (`ArtifactAccess.cs:168`,
gated by
`RetainedContent_RejectsRevokedOrForeignAuthorizationWithoutRevokingOpenStream`).
"Ended while an admitted stream remains open" is therefore a documented-safe
state in every mode, and no invariant forbids it. The lifetime defect the
model targets is **release**: disposing the backing acquisition leases while
a registered opener, admitted read, or returned stream is still live.

## Boundary with the admission model

[`ArtifactSessionAdmission`](../../../models/artifact-session-admission/ArtifactSessionAdmission.tla)
models demand admission, single-flight joining, and disposal-forced draining,
and treats "the dependent group reports quiescent" as an abstract given event
(its `GroupBecomesQuiescent` action). This model is about what quiescence must
mean for content access — registered opener callbacks, materialization reads,
and returned streams — and shows that the current mechanics have no way to
observe it. The two models share no state; each names the other's scope as a
non-claim.

## Current mechanics versus target design

Two constant-selected policy dimensions separate the shipped mechanics
(`src/DotnetInspector.Artifacts/ArtifactAccess.cs`,
`src/DotnetInspector.Artifacts.Workspaces/ArtifactSetSession.cs`) from the
design intent:

- **`OpenMode`.** `"Recheck"` (current): `ArtifactContribution.OpenRead`
  (`ArtifactAccess.cs:142`) and `RetainedArtifactContent.OpenRead`
  (`ArtifactAccess.cs:190`) validate volatile flags outside the authority
  gate (`ArtifactAccessLease.EnsureAccess`, `ArtifactAccess.cs:568`) and then
  run the opener unconditionally, so an open can complete strictly after
  `EndGeneration` (`ArtifactAccess.cs:399`) despite its contract to reject
  "every future open or mint". `"Gated"` (target): registering an open is
  atomic with the ended decision; the potentially blocking opener runs after
  registration and before a stream is returned.
- **`ReleaseMode`.** `"Immediate"` (current):
  `ArtifactSetSession.TerminateAsync` (`ArtifactSetSession.cs:627`) sets the
  disposed state, calls `EndGeneration` (line 654), and disposes every
  acquisition lease (line 657) without waiting for an in-flight sealing read
  (`MaterializeAsync`, `ArtifactSetSession.cs:577`) or an open query stream.
  `"AwaitQuiescence"` (target): termination ends the generation immediately
  — closing new access, exactly as `EndGeneration` documents — cancels
  registered opener callbacks and an in-flight materialization read it owns,
  and releases acquisition leases only once no registered opener, read, or
  returned stream remains.

`OpeningCancelMode = "Disabled"` is a mutation that removes target owner
cancellation of registered openers. `PublishMode = "Unguarded"` removes
publication's sealing-state guard, mirroring the existing product test
`ArtifactSetSession_DisposalDuringSealCannotPublish`.

The structural root is that nothing reports content quiescence:
`ArtifactAccessLease.Dispose` (`ArtifactAccess.cs:596`) only latches a local
flag and never informs the authority, opener callbacks run after validation
without registration, and returned streams are untracked. `TerminateAsync`
therefore has nothing it could wait on even if it wanted to. The session's own
remarks (`ArtifactSetSession.cs:80`) state the slice "does not yet implement …
dependent-group quiescence".

## Files

Positive configurations (must pass with no errors):

- [`Safety.cfg`](Safety.cfg) — target design (`Gated` +
  `AwaitQuiescence`): `TypeOK`, `OpensNeverCompleteAfterEnd`,
  `ReadsSeeLiveLeases`, `AccessRegistrationsMatchLiveContent`,
  `ReleaseImpliesContentQuiescent`, `ReleaseQuiescenceWitnessHolds`,
  `QueryStreamReleaseWitnessHolds`, `PublishRequiresActiveSealing`,
  `SessionTermCoherence`.
- [`Liveness.cfg`](Liveness.cfg) — target design:
  `TerminationEventuallyCompletes`, `TerminationSettlesMaterialization`,
  `TerminationSettlesReaders`.

Current-mechanics configurations (**each is expected to report a
violation**; the counterexample is the finding):

- [`CurrentOpenAfterEnd.cfg`](CurrentOpenAfterEnd.cfg) — an open passes its
  flag checks, `EndGeneration` completes, and the opener still returns a
  stream: `OpensNeverCompleteAfterEnd` fails.
- [`CurrentTornMaterialization.cfg`](CurrentTornMaterialization.cfg) — the
  owner disposes the session while `SealAsync` is materializing; the
  acquisition leases are disposed under the active read:
  `ReadsSeeLiveLeases` fails.
- [`CurrentReleaseDuringOpenStream.cfg`](CurrentReleaseDuringOpenStream.cfg)
  — leases are released while a published generation's query stream is
  open: `QueryStreamReleaseWitnessHolds` fails. The witness is re-derived
  at the release step itself, so the counterexample must show the stream
  open when release happens (publish, `ReaderValidate`, the stream opening,
  then `TermRelease` under it), not the materialization violation.

Broken-policy mutations of the target (**each is expected to report a
violation**, proving the corresponding rule is load-bearing):

- [`BrokenUngatedOpen.cfg`](BrokenUngatedOpen.cfg) — drain-wait without
  gate-atomic registration: an unregistered opener is invisible to
  termination, so `OpensNeverCompleteAfterEnd` still fails.
- [`BrokenImmediateRelease.cfg`](BrokenImmediateRelease.cfg) — gate-atomic
  opens without the drain-wait: an admitted read is still torn by immediate
  lease release, so `ReadsSeeLiveLeases` fails. Together with
  `BrokenUngatedOpen` this shows the two target rules are independently
  necessary.
- [`BrokenOpeningCancellation.cfg`](BrokenOpeningCancellation.cfg) — keeps
  gate-atomic registration and quiescence-awaiting release but removes owner
  cancellation of registered openers, so `TerminationEventuallyCompletes`
  fails when an opener stalls.
- [`BrokenUnguardedPublish.cfg`](BrokenUnguardedPublish.cfg) — removes the
  sealing-state guard: `PublishRequiresActiveSealing` fails.

Reachability probes (**each is expected to report a violation**, proving the
guarded path is genuinely exercised rather than vacuously safe):

- [`ReachabilityQueryRoundTrip.cfg`](ReachabilityQueryRoundTrip.cfg) — a
  published generation serves a complete open/close round trip.
- [`ReachabilityOverlappedTermination.cfg`](ReachabilityOverlappedTermination.cfg)
  — termination begins while a read or stream is live and still completes.

## Checked results

TLC 2026.08.21.155922 (rev `9787e65`, from the pinned `tla2tools.jar` v1.8.0 —
see [`docs/runbooks/tla-plus-setup.md`](../../../runbooks/tla-plus-setup.md))
with two query readers:

- `Safety.cfg`: 427 states generated, 256 distinct, depth 19, no errors; all
  nine invariants pass, and every target-mode action fires, including owner
  cancellation of registered openers and the materialization read. The
  `Recheck`/`Immediate` actions and `MatReadTorn` are correctly unreachable.
- `Liveness.cfg`: same graph, all three temporal properties pass.
- A neighboring three-reader safety run also passes (3,038 states generated,
  1,356 distinct, depth 23), so the properties are not fitted to the
  two-reader bound.
- All three `Current*` configurations and all four `Broken*` configurations
  fail on exactly their intended invariant or temporal property; both probes
  are reachable.

The shortest torn-materialization counterexample is ten states: seal
begins, the contribution open validates, the owner's disposal runs to
completion (disposed state → `EndGeneration` → leases disposed), the
unregistered opener starts and returns anyway, and the next read chunk
observes released leases. The query-stream counterexample is fifteen states:
it publishes the generation, lets `q1` validate and open its stream, and then
releases the leases under that open stream.

Run any configuration with:

```bash
java -cp /path/to/tla2tools.jar tlc2.TLC \
  -config Safety.cfg -workers auto -cleanup ArtifactGenerationAccess.tla
```

## What the counterexamples mean for the product

- The torn materialization is the concrete case: adapter-backed streams
  (extracted package entries, temporary files) can be invalidated under an
  active `MaterializeAsync` read when disposal races sealing. Today this
  surfaces as an adapter-dependent exception; the design obligation is that
  disposal must not invalidate content under an active callback.
- The open-after-end window is contract-level today: a query open that
  completes after `EndGeneration` reads a GC-retained `byte[]`, so the
  content is stale-but-intact. It becomes load-bearing when retained content
  moves to a content-addressed store or budget-charged release (both
  contemplated by the owning design), because release keyed to the
  generation's lifetime would then invalidate bytes under an unregistered
  open.
- Release-under-an-open-stream is the same future hazard on the query side,
  and on the admission side it is the torn read above. An already-returned
  stream surviving `EndGeneration` is not a defect — it is the documented
  access contract — which is why the target design waits for streams at
  release rather than revoking them at the end.
- The target design's liveness carries three obligations the owning document
  does not state today. First, a registered opener must be interruptible by
  its owner: `OpenReadable` invokes a synchronous `Func<Stream>` with no
  cancellation or bounded-completion contract, so a stalled callback can
  otherwise block termination. Second, an in-flight materialization read must
  likewise be owner-interruptible: the implementation awaits
  `Stream.ReadAsync` with only the caller token. The model includes both
  cancellation paths and makes no fairness assumption that an opener or
  adapter read completes on its own. Third, abandoned returned query streams
  need a policy (bound the wait, or invalidate visibly): termination completes
  only because every consumer eventually closes its stream, encoded as a
  fairness assumption the authority cannot enforce today. All three belong in
  the owning document's termination contract.

## Assumptions and simplifications

- One generation, one sealing materialization read standing for the whole
  `MaterializeAsync` loop, and one read step standing for the copy loop's
  next chunk.
- Authorization replacement and revocation are folded into the ended flag:
  `ReplaceQueryAuthorization` produces the same validate-then-open window
  against a revoked authorization that `EndGeneration` produces against an
  ended generation.
- The owner's `TermRequest`, `StartSeal`, `Publish`, and the `MatValidate`
  and `ReaderValidate` arrivals are unfair environment actions; no liveness
  claim depends on them.
- Opener completion/failure and `MatReadOk` are deliberately unfair
  (callbacks and adapter-backed reads may stall). `ReaderClose` fairness
  encodes the consumer obligation to eventually dispose a returned stream.

## Non-claims

The model says nothing about demand admission or single-flight joining
(owned by `ArtifactSessionAdmission`), budget arithmetic, content identity or
digests, adapter behavior behind a disposed lease, multiple concurrent
generations, or the assembly-group quiescence protocol above this layer.
These results establish evidence about the model, not the implementation;
the implementation-facing statements above cite the exact code they
describe.
