# Annotated Source interaction design models

These executable TLA+ models specify the stateful interaction mechanisms in
the
[Annotated Source browser interaction model](../../annotated-source-interaction-model.md).
They keep the owning design readable while allowing TLC to exhaust every
transition permitted by small finite instances.

The models are independent:

| Model | Mechanism |
| --- | --- |
| `SurfaceSession.tla` | Embedded/full transfer, mode-local selection and detail, annotation visibility, layered Escape, and focus fallback |
| `EntryRestoration.tla` | History replacement and push, sticky mode, fresh member state, forward truncation, and Back/Forward restoration |

## Scope and assumptions

`SurfaceSession.tla` abstracts product-issued identities to three Findings and
one node. Two Findings are annotatable: one has C# and IL targets and one is
IL-only. The third represents an unanchored or member-header Finding reachable
only through its persistent inspector action. The default set ranges over
every subset of the annotatable universe so Default, All, Clear, and Custom
overlap cases are explored rather than fixed by configuration.

The surface model treats each user gesture as an atomic transition. It checks
the following safety properties:

| Invariant | Claim |
| --- | --- |
| `SelectionShapes` | Embedded selection cannot become a node, and every selected value has the matching kind |
| `DetailMatchesPrimary` | Finding detail always belongs to the primary Finding |
| `EmbeddedStateIsConstrained` | Embedded state contains only default C#-visible Findings |
| `FullDetailExistsOnlyInFullMode` | Exit cannot leave retained full detail available for resurrection |
| `ReportedStateIsDerived` | Default, All, Clear, and Custom are derived from the active and default sets with the required precedence |
| `FocusIsValid` | Restored focus names a control that survives in the resulting mode and media |
| `ExplorePreservesOrInitializesExactly` | First Explore initializes from embedded state; later Explore preserves retained full state |
| `ExitTransfersAndClosesExactly` | Exit transfers only an eligible Finding, retains non-detail full state, and closes detail |
| `EmbeddedEscapeIsStateNeutral` | Embedded Escape without a transient layer falls through without mutating either mode |
| `MediaChangesAreOrthogonal` | Media changes never change active annotations, reported state, primary selection, or detail |
| `DetailClosureRestoresValidFocus` | Detail closes to its surviving chip or the persistent inspector action |

`EntryRestoration.tla` abstracts one entry's required mode-local state to an
embedded and full revision. A revision represents the primary selection,
active annotation set, visible media, and coordinate preferences that the
design requires history to restore. Transient Finding detail, focus, and
scroll are deliberately outside that currency. The model explores two members,
up to three history entries, and one local edit per mode.

It checks:

| Invariant | Claim |
| --- | --- |
| `VisibleEntryIsCurrent` | The visible entry always equals the stored entry at the browser-history cursor |
| `EntryIdsAreUnique` | Every pushed entry keeps a stable distinct identity, including after forward truncation |
| `LocalStateChangesDoNotNavigate` | Local edits update only the current entry and do not move or grow history |
| `ModeChangesReplaceCurrentEntry` | Explore and Exit replace the current entry while retaining both mode-local revisions |
| `SuccessfulNavigationIsFreshAndSticky` | Member navigation pushes one fresh entry in the originating mode and transfers no local revision |
| `BackAndForwardRestoreExactEntries` | Back and Forward restore the complete previously stored required entry |
| `FailedNavigationRetainsHistory` | A typed non-applied destination outcome changes no history or visible state |

Neither model asserts user-input liveness. A user may stop with a detail
surface open or on any history entry, and every transition modeled here is
synchronous and atomic. Asynchronous subject resolution, supersession,
maintenance, effect authority, and their liveness properties belong to the
[Inspection Subject Navigation models](../inspection-subject-navigation/README.md).

## Non-claims

The models do not specify:

- source span geometry, hit testing, rendering, or ARIA behavior;
- Finding, member, assembly, or packet identity construction;
- destination availability or typed outcome construction;
- canonical packet encoding or version compatibility;
- declaration projection, Finding evidence, or census identity;
- browser implementation conformance; or
- asynchronous navigation ordering already owned by Inspection Subject
  Navigation.

Those contracts remain in the owning prose documents and named implementation
gates. TLC results are evidence about these finite design models, not the
future C# or TypeScript implementation.

## Running TLC

Use the pinned toolchain from the
[TLA+ setup runbook](../../../runbooks/tla-plus-setup.md). From this directory:

```sh
java -XX:+UseParallelGC -cp "$TLA_TOOLS_JAR" tlc2.TLC \
  -workers auto -cleanup -coverage 1 \
  -config SurfaceSession.cfg SurfaceSession.tla
java -XX:+UseParallelGC -cp "$TLA_TOOLS_JAR" tlc2.TLC \
  -workers auto -cleanup -coverage 1 \
  -config EntryRestoration.cfg EntryRestoration.tla
```

Run the commands sequentially. Concurrent TLC processes using `-cleanup` in
one directory can remove one another's state metadata.

The recorded run used OpenJDK 21.0.12 and TLA+ tools 1.8.0
(`TLC2 2026.08.21.155922`, revision `9787e65`).

| Model | States generated | Distinct states | Search depth |
| --- | ---: | ---: | ---: |
| `SurfaceSession.tla` | 48,444 | 4,752 | 11 |
| `EntryRestoration.tla` | 27,764 | 7,200 | 15 |

## Non-vacuity probes

Each scratch mutation below was checked independently against the shipped
configuration. Every mutation produced a concrete counterexample:

| Mutation | Reported violation |
| --- | --- |
| Let a later Explore overwrite retained full state from embedded state | `ExplorePreservesOrInitializesExactly` |
| Retain full Finding detail during Exit | `FullDetailExistsOnlyInFullMode` |
| Let embedded Escape clear the embedded primary | `EmbeddedEscapeIsStateNeutral` |
| Let a media toggle remove an active annotation | `MediaChangesAreOrthogonal` |
| Restore focus to a hidden chip instead of its inspector action | `FocusIsValid` |
| Push a history entry for Explore or Exit | `VisibleEntryIsCurrent` |
| Copy origin local state into a member destination | `SuccessfulNavigationIsFreshAndSticky` |
| Restore an adjacent entry rather than the cursor entry | `VisibleEntryIsCurrent` |
| Change visible state after a failed destination outcome | `VisibleEntryIsCurrent` |
