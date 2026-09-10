import assert from "node:assert/strict";
import test from "node:test";

import type {
  BrowserSource,
  BrowserTypeSourceResult,
} from "../src/facades/inspect-web-source.d.ts";
import type {
  OperationFeatureEvent,
  OperationHandle,
} from "../src/operation-authority.ts";
import { createOperationAuthorityPage } from "../src/operation-authority.ts";
import {
  registerEngineWorkerTypeSourceAdapter,
  type EngineWorkerTypeSourceAdapter,
} from "../src/engine-worker-client.ts";
import {
  createEngineWorkerProducerClasses,
  engineWorkerDiagnostic,
  engineWorkerPolicy,
  engineWorkerText,
} from "../src/engine-worker-contract.ts";
import {
  engineWorkerTypeSourceCancellationIsRunning,
  engineWorkerTypeSourceInput,
  engineWorkerTypeSourceKind,
  engineWorkerTypeSourceProgress,
  engineWorkerTypeSourceValue,
  mapEngineWorkerTypeSourceResult,
  registerEngineWorkerTypeSourceOperation,
  type EngineWorkerTypeSourceFacade,
} from "../src/engine-worker-source.ts";
import {
  createSourceInspectionCoordinator,
  type SourceInspectionState,
  type TypeSourceLoadRequest,
} from "../src/source-inspection.ts";
import {
  FakeWorkerOperationCatalog,
  FakeWorkerRuntime,
  ManualWorkerRuntimeEnvironment,
  QueueWorkerRuntimeTransportFactory,
  WorkerRuntimeHost,
  type WorkerRuntimePreparationError,
} from "../src/worker-runtime-core.ts";
import type {
  ManagedOperationSettlement,
} from "../src/worker-runtime-protocol.ts";

const source: BrowserSource = {
  provider: "decompiled",
  provenance: "dotnet-inspect fixture",
  url: null,
  pdbSourceLimitation: "PDB unavailable",
  text: "public sealed class Widget {}",
};

const request: TypeSourceLoadRequest = {
  packageId: "Example.Package",
  version: "1.2.3",
  framework: "net11.0",
  assembly: "Example.dll",
  type: "Example.Widget",
  taste: "[\"readable-locals\"]",
  signature: "page-only-signature",
  isVisible: () => true,
};

interface Deferred<T> {
  readonly promise: Promise<T>;
  readonly resolve: (value: T) => void;
}

function deferred<T>(): Deferred<T> {
  let resolvePromise: ((value: T) => void) | undefined;
  const promise = new Promise<T>(resolve => {
    resolvePromise = resolve;
  });
  return {
    promise,
    resolve: value => {
      resolvePromise?.(value);
    },
  };
}

function ownData(value: unknown, name: string): unknown {
  if (typeof value !== "object" || value === null) return undefined;
  const property = Object.getOwnPropertyDescriptor(value, name);
  return property !== undefined && "value" in property
    ? property.value
    : undefined;
}

function succeeded(value: BrowserSource = source): BrowserTypeSourceResult {
  return {
    version: 1,
    kind: "Succeeded",
    value,
    failureKind: null,
    error: null,
    diagnostic: null,
    reason: null,
  };
}

function canceled(reason: string): BrowserTypeSourceResult {
  return {
    version: 1,
    kind: "Canceled",
    value: null,
    failureKind: null,
    error: null,
    diagnostic: null,
    reason,
  };
}

interface Harness {
  readonly environment: ManualWorkerRuntimeEnvironment;
  readonly worker: FakeWorkerRuntime<string, string>;
  readonly host: WorkerRuntimeHost<string, string>;
  readonly adapter: EngineWorkerTypeSourceAdapter;
}

function createHarness(
  registerWorker: (operations: FakeWorkerOperationCatalog) => void,
): Harness {
  const environment = new ManualWorkerRuntimeEnvironment();
  const operations = new FakeWorkerOperationCatalog();
  registerWorker(operations);
  const worker = new FakeWorkerRuntime<string, string>({
    scheduler: environment,
    bootstrap: {
      decoder: engineWorkerText,
      bootstrap: () => undefined,
    },
    diagnostic: engineWorkerDiagnostic,
    unknownOperationRejection: kind => ({
      error: `Unknown operation: ${kind}`,
      diagnostic: `Unknown operation: ${kind}`,
    }),
    operations,
    producerClasses: createEngineWorkerProducerClasses(),
  });
  const host = new WorkerRuntimeHost<string, string>({
    transport: new QueueWorkerRuntimeTransportFactory([worker]),
    clock: environment,
    lifecycle: environment,
    bootstrap: {
      encode: engineWorkerText.decode,
      diagnostic: engineWorkerText,
    },
    diagnostic: engineWorkerText,
    callbacks: {
      failure: () => undefined,
      diagnostic: () => undefined,
      realmReleased: () => undefined,
    },
    createDiagnostic: (kind, detail) =>
      `${kind}: ${engineWorkerDiagnostic(detail)}`.slice(0, 4_096),
    idleHeartbeatIntervalMilliseconds:
      engineWorkerPolicy.idleHeartbeatIntervalMilliseconds,
    schedulingToleranceMilliseconds:
      engineWorkerPolicy.schedulingToleranceMilliseconds,
    startupBudgetMilliseconds: 100,
    controlResponseGraceMilliseconds: 10,
    drainBudgetMilliseconds: 20,
    producerClasses: createEngineWorkerProducerClasses(),
  });
  return {
    environment,
    worker,
    host,
    adapter: registerEngineWorkerTypeSourceAdapter(host),
  };
}

async function startReady(harness: Harness): Promise<void> {
  assert.equal(harness.host.start("").kind, "started");
  await harness.environment.flushAsync();
  assert.equal(harness.host.snapshot().phase, "ready");
}

function startSource(
  adapter: EngineWorkerTypeSourceAdapter,
): {
  readonly handle: OperationHandle<BrowserSource, string>;
  readonly events: OperationFeatureEvent<BrowserSource, string, never>[];
} {
  const events: OperationFeatureEvent<BrowserSource, string, never>[] = [];
  const page = createOperationAuthorityPage({
    allocation: { createId: () => "source-operation" },
  });
  const session = page.createSession<
    TypeSourceLoadRequest,
    BrowserSource,
    string,
    never,
    WorkerRuntimePreparationError
  >({
    feature: {
      publish: event => {
        events.push(event);
        return undefined;
      },
    },
    diagnostic: { report: () => undefined },
  });
  const started = session.start(request, adapter);
  assert.equal(started.kind, "started");
  if (started.kind !== "started")
    throw new Error("Expected Type Source to start.");
  return { handle: started.handle, events };
}

test("production Type Source consumer keeps the authority identity, stale suppression and quiescence", async () => {
  const first = deferred<BrowserTypeSourceResult>();
  const second = deferred<BrowserTypeSourceResult>();
  const third = deferred<BrowserTypeSourceResult>();
  const calls: string[] = [];
  const cancellations: string[] = [];
  const facade: EngineWorkerTypeSourceFacade = {
    queryTypeSource(id) {
      calls.push(id);
      return calls.length === 1 ? first.promise : calls.length === 2 ? second.promise : third.promise;
    },
    cancelTypeSourceQuery(id, reason) {
      cancellations.push(`${id}:${reason}`);
      return { kind: "Requested", reason };
    },
  };
  const harness = createHarness(operations =>
    registerEngineWorkerTypeSourceOperation(operations, () => facade));
  await startReady(harness);
  let sequence = 0;
  const state: SourceInspectionState = {
    settings: false, explorer: null, loading: false, error: "", home: false,
    package: {}, atPackageRoot: false, lens: "source", selectedMemberKey: "",
    memberSection: "source", sourceRequestGeneration: 0,
    memberSource: null, memberSourceLoading: false, memberSourceError: "", memberSourceKey: "",
    typeSource: null, typeSourceLoading: false, typeSourceError: "", typeSourceKey: "",
    graphSource: { status: "closed" }, taste: [],
  };
  const coordinator = createSourceInspectionCoordinator({
    state,
    operationAuthority: createOperationAuthorityPage({
      allocation: { createId: () => `source-owner-${++sequence}` },
    }),
    typeSourceAdapter: harness.adapter,
    queryMemberSource: async () => source,
    queryGraphSource: async () => source,
    cancelEngineSourceRequest: () => assert.fail("Direct cancellation must not run."),
    memberSourceHasConcreteOverload: () => false,
    reportOperationDiagnostic: () => undefined,
    describeError: String, render: () => undefined,
    renderPreservingMemberFocus: () => ({
      selector: "", dataTarget: null, selection: null, navigationScope: null,
      navigationSelection: null, navigationScrollTop: null, focusLost: false,
    }),
  });
  let firstQuiesced = false;
  const old = coordinator.loadTypeSource(request).then(() => { firstQuiesced = true; return undefined; });
  await harness.environment.flushAsync();
  const current = coordinator.loadTypeSource({ ...request, signature: "replacement" });
  await harness.environment.flushAsync();
  assert.deepEqual(calls, ["source-owner-1", "source-owner-2"]);
  assert.deepEqual(cancellations, ["source-owner-1:superseded"]);
  assert.equal(firstQuiesced, false);
  second.resolve(succeeded({ ...source, text: "new source" }));
  await harness.environment.flushAsync();
  await current;
  assert.equal(state.typeSource?.text, "new source");
  first.resolve(succeeded({ ...source, text: "stale source" }));
  await harness.environment.flushAsync();
  await old;
  assert.equal(state.typeSource?.text, "new source");
  assert.equal(firstQuiesced, true);
  assert.equal(harness.host.snapshot().activeOperations, 0);
  const replaced = coordinator.loadTypeSource({ ...request, signature: "third" });
  await harness.environment.flushAsync();
  const oversized = { ...request, signature: "oversized", type: "T".repeat(65_537) };
  await coordinator.loadTypeSource(oversized);
  await harness.environment.flushAsync();
  assert.equal(state.typeSourceKey, "oversized");
  assert.equal(state.typeSource, null);
  assert.match(state.typeSourceError, /producer-rejected/);
  assert.equal(calls.length, 3);
  assert.equal(cancellations.at(-1), "source-owner-3:superseded");
  third.resolve(succeeded({ ...source, text: "stale third" }));
  await harness.environment.flushAsync();
  await replaced;
  await coordinator.loadTypeSource(oversized);
  assert.equal(state.typeSourceKey, "oversized");
  assert.equal(state.typeSource, null);
  assert.match(state.typeSourceError, /producer-rejected/);
  assert.equal(calls.length, 3);
  harness.host.dispose();
});

test("Type Source Worker adapter projects clone-safe input and returns source", async () => {
  const calls: unknown[][] = [];
  const facade: EngineWorkerTypeSourceFacade = {
    queryTypeSource: (...args) => {
      calls.push(args);
      return Promise.resolve(succeeded());
    },
    cancelTypeSourceQuery: () => ({ kind: "NotActive", reason: null }),
  };
  const harness = createHarness(operations =>
    registerEngineWorkerTypeSourceOperation(operations, () => facade));
  await startReady(harness);

  const { handle } = startSource(harness.adapter);
  await harness.environment.flushAsync();

  assert.deepEqual(await handle.outcome, { kind: "succeeded", value: source });
  await handle.quiesced;
  assert.deepEqual(calls, [[
    "source-operation",
    request.packageId,
    request.version,
    request.framework,
    request.assembly,
    request.type,
    request.taste,
  ]]);
  const start = harness.worker.receivedMessages.find(message =>
    ownData(message, "kind") === "start");
  assert.notEqual(start, undefined);
  const payload = ownData(start, "payload");
  assert.deepEqual(payload, {
    packageId: request.packageId,
    version: request.version,
    framework: request.framework,
    assembly: request.assembly,
    type: request.type,
    taste: request.taste,
  });
  assert.doesNotThrow(() => structuredClone(payload));

  harness.host.dispose();
});

test("Type Source Worker operation forwards keyed cancellation", async () => {
  const result = deferred<BrowserTypeSourceResult>();
  const cancellations: unknown[][] = [];
  const facade: EngineWorkerTypeSourceFacade = {
    queryTypeSource: () => result.promise,
    cancelTypeSourceQuery: (...args) => {
      cancellations.push(args);
      return { kind: "Requested", reason: args[1] };
    },
  };
  const harness = createHarness(operations =>
    registerEngineWorkerTypeSourceOperation(operations, () => facade));
  await startReady(harness);

  const { handle } = startSource(harness.adapter);
  await harness.environment.flushAsync();
  assert.equal(handle.cancel("superseded").kind, "applied");
  await harness.environment.flushAsync();
  assert.deepEqual(cancellations, [["source-operation", "superseded"]]);
  assert.deepEqual(await handle.outcome, {
    kind: "canceled",
    reason: "superseded",
  });

  result.resolve(canceled("superseded"));
  await Promise.resolve();
  await harness.environment.flushAsync();
  await handle.quiesced;
  harness.host.dispose();
});

test("Type Source managed terminal results map without losing failure kind", () => {
  assert.deepEqual(mapEngineWorkerTypeSourceResult(succeeded()), {
    kind: "succeeded",
    value: source,
  });
  assert.deepEqual(mapEngineWorkerTypeSourceResult({
    version: 1,
    kind: "Failed",
    value: null,
    failureKind: "Expected",
    error: "source unavailable",
    diagnostic: "expected detail",
    reason: null,
  }), {
    kind: "failed",
    failureKind: "expected",
    error: "source unavailable",
    diagnostic: "expected detail",
  });
  assert.deepEqual(mapEngineWorkerTypeSourceResult({
    version: 1,
    kind: "Failed",
    value: null,
    failureKind: "Unexpected",
    error: "source crashed",
    diagnostic: "stack",
    reason: null,
  }), {
    kind: "failed",
    failureKind: "unexpected",
    error: "source crashed",
    diagnostic: "stack",
  });
  assert.deepEqual(mapEngineWorkerTypeSourceResult(canceled("timeout")), {
    kind: "canceled",
    reason: "timeout",
  });
  assert.deepEqual(mapEngineWorkerTypeSourceResult(succeeded({
    ...source,
    provenance: "P".repeat(64 * 1024),
  })), {
    kind: "failed",
    failureKind: "unexpected",
    error: "Type Source exceeded Worker transport limits.",
    diagnostic: "Type Source metadata exceeds 65536 characters.",
  });
});

test("Type Source managed result and cancellation validators reject drift", () => {
  assert.throws(
    () => mapEngineWorkerTypeSourceResult({
      ...succeeded(),
      value: { ...source, text: undefined },
    }),
    /Expected Type Source text/,
  );
  assert.throws(
    () => mapEngineWorkerTypeSourceResult({
      ...canceled("future-reason"),
    }),
    /cancellation reason is invalid/,
  );
  assert.throws(
    () => mapEngineWorkerTypeSourceResult({
      ...succeeded(),
      future: true,
    }),
    /Expected a version 1 Type Source result/,
  );

  assert.equal(engineWorkerTypeSourceCancellationIsRunning(
    { kind: "Requested", reason: "disposed" },
    "disposed",
  ), true);
  assert.equal(engineWorkerTypeSourceCancellationIsRunning(
    { kind: "AlreadyRequested", reason: "user" },
    "superseded",
  ), true);
  assert.equal(engineWorkerTypeSourceCancellationIsRunning(
    { kind: "NotActive", reason: null },
    "user",
  ), false);
  assert.throws(
    () => engineWorkerTypeSourceCancellationIsRunning(
      { kind: "Requested", reason: "user" },
      "timeout",
    ),
    /changed the requested reason/,
  );
});

test("Type Source codecs enforce request, result, and no-progress bounds", () => {
  const oversizedInput = engineWorkerTypeSourceInput.decode({
    packageId: request.packageId,
    version: request.version,
    framework: request.framework,
    assembly: request.assembly,
    type: "T".repeat(64 * 1024),
    taste: request.taste,
  });
  assert.equal(oversizedInput.kind, "rejected");
  if (oversizedInput.kind === "rejected")
    assert.equal(oversizedInput.reason, "oversized");
  assert.equal(engineWorkerTypeSourceInput.decode(request).kind, "rejected");

  const oversizedValue = engineWorkerTypeSourceValue.decode({
    ...source,
    provenance: "P".repeat(64 * 1024),
  });
  assert.equal(oversizedValue.kind, "rejected");
  if (oversizedValue.kind === "rejected")
    assert.equal(oversizedValue.reason, "oversized");

  const oversizedText = engineWorkerTypeSourceValue.decode({
    ...source,
    text: "S".repeat(32_000_001),
  });
  assert.equal(oversizedText.kind, "rejected");
  if (oversizedText.kind === "rejected")
    assert.equal(oversizedText.reason, "oversized");

  assert.deepEqual(engineWorkerTypeSourceProgress.decode("unexpected"), {
    kind: "rejected",
    reason: "invalid",
    message: "Type Source does not publish progress payloads.",
  });
});

test("Page adapter rejects a malformed Worker settlement as boundary failure", async () => {
  const harness = createHarness(operations => {
    operations.register({
      kind: engineWorkerTypeSourceKind,
      allowance: { kind: "unbounded" },
      input: engineWorkerTypeSourceInput,
      rejectInvalidPayload: failure => ({
        error: failure.message,
        diagnostic: failure.message,
      }),
      invoke: (): ManagedOperationSettlement<unknown, string, string> => ({
        kind: "succeeded",
        value: { ...source, text: 42 },
      }),
    });
  });
  await startReady(harness);

  const { handle } = startSource(harness.adapter);
  await harness.environment.flushAsync();

  assert.deepEqual(await handle.outcome, {
    kind: "failed",
    error: "Worker protocol failed.",
  });
  harness.environment.advanceActive(21);
  await handle.quiesced;
  harness.host.dispose();
});

test("Worker adapter contains malformed generated results to one operation", async () => {
  const facade: EngineWorkerTypeSourceFacade = {
    queryTypeSource: () => Promise.resolve({
      ...succeeded(),
      version: 2,
    }),
    cancelTypeSourceQuery: () => ({ kind: "NotActive", reason: null }),
  };
  const harness = createHarness(operations =>
    registerEngineWorkerTypeSourceOperation(operations, () => facade));
  await startReady(harness);

  const { handle } = startSource(harness.adapter);
  await harness.environment.flushAsync();

  assert.deepEqual(await handle.outcome, {
    kind: "failed",
    error: "Type Source returned invalid Worker boundary data.",
  });
  await handle.quiesced;
  assert.equal(harness.host.snapshot().phase, "ready");
  harness.host.dispose();
});
