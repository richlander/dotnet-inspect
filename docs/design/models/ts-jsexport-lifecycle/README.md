# ts-jsexport lifecycle model

This directory model-checks the lifecycle interaction defined by
[`ts-jsexport`](../../ts-jsexport.md). It supplements that readable
specification; it does not define TypeScript emission or prove an
implementation.

## Scope

`TsJsExportLifecycle.tla` models two generated facade modules, two callers per
facade, two assembly-specific export roots, and one SDK-owned runtime in a
JavaScript realm. It checks both coordination modes permitted by the design:

- `SharedInFlight`: both facade modules begin initialization concurrently and
  attach through runtime-owner coordination.
- `Serialized`: the consumer starts the second facade only after the first
  reaches a terminal result. A local failure therefore does not strand the
  second facade, which can still reuse the completed runtime.

Each caller issues its own initialization request. The first request for a
facade starts its state machine; concurrent and later requests join the same
ready or failed result without incrementing that facade's initialization
count. The model also includes runtime creation, assembly export acquisition,
complete validation, state publication, managed-operation and entry-point
calls, terminal failure, and atomic realm restart.

One-step history variables retain the source phase and call-event state for
each transition. The checked properties can therefore distinguish a legal
post-initialization call from managed code invoked by the transition that
publishes ready state, and can detect any ready or failed facade leaving its
terminal phase without a realm restart.

The model deliberately keeps these values abstract:

- exact JavaScript property descriptors and dispatch keys;
- generated TypeScript names and wire declarations;
- managed operation arguments and results;
- SDK implementation details below runtime creation completion; and
- browser worker and message transport policy.

Those concerns retain their concrete gates in the owning design and in
[#4842](https://github.com/richlander/dotnet-inspect/issues/4842).

## Assumptions

- One imported SDK builder owns one runtime state per JavaScript realm.
- The consumer either serializes facade initialization or supplies shared
  in-flight runtime coordination.
- Each bounded caller eventually requests initialization. Runtime creation,
  assembly acquisition, and validation eventually complete when enabled.
  `Spec` states those weak-fairness assumptions explicitly.
- Runtime creation may fail for every attached facade. Local acquisition and
  validation failures are limited to `FacadeA` so the model can check that
  `FacadeB` remains able to use the shared runtime.
- A realm restart atomically discards all facade-local and runtime lifecycle
  state, after which the same bounded startup scenario begins again.
- The checked configurations permit one realm restart. The bound covers state
  reset and a complete second startup without making the epoch unbounded.
- Two facades and two callers per facade are sufficient to expose duplicate
  creation, same-facade single-flight, cross-facade root use, and local failure
  isolation. This bound does not prove arbitrary-cardinality performance.

## Checked properties

| Design property | Model property |
| --- | --- |
| One SDK runtime creation per realm | `OneSharedRuntimeCreation` |
| Concurrent callers join one facade initialization | `OneInitializationPerFacade` |
| Each facade acquires once | `OneAcquisitionPerFacade` |
| A facade acquires and publishes only its assembly root | `AcquisitionUsesOwnAssembly`, `PublicationUsesOwnAssembly` |
| Publication follows complete validation | `PublicationRequiresCompleteValidation`, `ReadyHasCompleteState` |
| Failure publishes no partial state | `FailurePublishesNothing` |
| A facade never disposes the shared runtime | `FacadeNeverDisposesRuntime` |
| Managed operations and `runEntryPoint()` require readiness | `ManagedCallsRequireReady`, `EntryPointCallsRequireReady` |
| Calls begin only from ready, including the transition that publishes ready state | `ManagedCodeStartsReady` |
| Initialization invokes neither managed operations nor `runMain()` | `InitializationInvokesNoManagedCode`, `ManagedCodeStartsReady` |
| Requested initialization reaches ready or terminal failure | `RequestedEventuallyTerminates` |
| A local facade failure does not prevent its peer from reaching ready | `LocalFailureIsolation` |
| Valid facades both attach to the runtime | `AllFacadesEventuallyReady` |
| Ready and failed states are stable until realm restart | `TerminalPhasePersistsUntilRestart` |

In the shared-in-flight positive trace, separate caller actions start both
facades, one facade changes the runtime from absent to creating, the other
joins that creation, and each then acquires its own root. In the serialized
positive trace, caller actions start only `FacadeA`; after it reaches a
terminal result, callers start `FacadeB`, which observes the same completed
runtime without starting another one.

## Running TLC

The recorded run used OpenJDK 21 and TLA+ tools 1.7.4 (`TLC2 2.19`,
revision `5a47802`). The checked `tla2tools.jar` has SHA-256
`936a262061c914694dfd669a543be24573c45d5aa0ff20a8b96b23d01e050e88`.

```bash
curl -fsSL \
  https://github.com/tlaplus/tlaplus/releases/download/v1.7.4/tla2tools.jar \
  -o /tmp/tla2tools.jar
echo "936a262061c914694dfd669a543be24573c45d5aa0ff20a8b96b23d01e050e88  /tmp/tla2tools.jar" \
  | sha256sum -c -
cd docs/design/models/ts-jsexport-lifecycle
java -XX:+UseParallelGC -cp /tmp/tla2tools.jar tlc2.TLC \
  -workers auto -cleanup \
  -config TsJsExportLifecycleShared.cfg TsJsExportLifecycle.tla
java -XX:+UseParallelGC -cp /tmp/tla2tools.jar tlc2.TLC \
  -workers auto -cleanup \
  -config TsJsExportLifecycleSharedSuccess.cfg TsJsExportLifecycle.tla
java -XX:+UseParallelGC -cp /tmp/tla2tools.jar tlc2.TLC \
  -workers auto -cleanup \
  -config TsJsExportLifecycleSerialized.cfg TsJsExportLifecycle.tla
java -XX:+UseParallelGC -cp /tmp/tla2tools.jar tlc2.TLC \
  -workers auto -cleanup \
  -config TsJsExportLifecycleSerializedSuccess.cfg TsJsExportLifecycle.tla
```

All configurations use two facades, two callers per facade, two distinct
assembly roots, and at most one realm restart:

| Configuration | Runtime/local failures | Generated states | Distinct states | Result |
| --- | --- | ---: | ---: | --- |
| `TsJsExportLifecycleShared.cfg` | Enabled | 28,686 | 4,940 | No error |
| `TsJsExportLifecycleSharedSuccess.cfg` | Disabled | 22,045 | 3,751 | No error |
| `TsJsExportLifecycleSerialized.cfg` | Enabled | 11,409 | 2,180 | No error |
| `TsJsExportLifecycleSerializedSuccess.cfg` | Disabled | 9,371 | 1,631 | No error |

Generic deadlock checking is disabled because reaching terminal facade states
after the final permitted restart is an expected finite-model endpoint.
`RequestedEventuallyTerminates`, `LocalFailureIsolation`, and
`AllFacadesEventuallyReady` state the applicable progress requirements
directly.

## Counterexample mutations

Twelve opt-in configurations each enable one incorrect lifecycle transition and
must fail with the named invariant:

| Configuration | Deliberate defect | Expected violation |
| --- | --- | --- |
| `TsJsExportLifecycleEarlyPublication.cfg` | Publishes after acquisition but before validation | `PublicationRequiresCompleteValidation` |
| `TsJsExportLifecycleDuplicateRuntime.cfg` | Starts a second runtime while one is in flight | `OneSharedRuntimeCreation` |
| `TsJsExportLifecycleDuplicateFacade.cfg` | Starts facade initialization for every concurrent caller | `OneInitializationPerFacade` |
| `TsJsExportLifecycleDuplicateAcquisition.cfg` | Starts a second acquisition before validation completes | `OneAcquisitionPerFacade` |
| `TsJsExportLifecycleCrossAssembly.cfg` | Gives a facade the other assembly root | `AcquisitionUsesOwnAssembly` |
| `TsJsExportLifecycleDisposeRuntime.cfg` | Disposes the shared runtime after local failure | `FacadeNeverDisposesRuntime` |
| `TsJsExportLifecycleFailPeer.cfg` | Fails the peer when one facade fails locally | `LocalFailureIsolation` |
| `TsJsExportLifecycleFailPeerSerialized.cfg` | Fails a serialized peer after local failure | `LocalFailureIsolation` |
| `TsJsExportLifecycleManagedDuringInit.cfg` | Invokes managed code while initialization is active | `InitializationInvokesNoManagedCode` |
| `TsJsExportLifecycleManagedOnReadyTransition.cfg` | Invokes managed code on the transition that publishes ready state | `ManagedCodeStartsReady` |
| `TsJsExportLifecycleLoseReady.cfg` | Leaves ready state without restarting the realm | `TerminalPhasePersistsUntilRestart` |
| `TsJsExportLifecycleLoseFailure.cfg` | Leaves failed state without restarting the realm | `TerminalPhasePersistsUntilRestart` |

Run a mutation by substituting its configuration name in the TLC command. A
successful mutation run is a TLC invariant violation with a concrete state
trace; a clean exit means the mutation gate is vacuous or broken.
Mutation state counts are intentionally not recorded because parallel TLC
workers can encounter the first violating state at different points. Every
configuration was also run with `-workers 1` and produced its named violation.
These mutations target the lifecycle properties most likely to regress; they
are non-vacuity evidence, not a requirement to manufacture one mutation per
checked invariant.
