import assert from "node:assert/strict";
import test from "node:test";

import {
  registerEngineWorkerPackageQueryAdapter,
  type EngineWorkerPackageQueryAdapter,
} from "../src/engine-worker-client.ts";
import {
  createEngineWorkerProducerClasses,
  engineWorkerDiagnostic,
  engineWorkerPolicy,
  engineWorkerText,
} from "../src/engine-worker-contract.ts";
import {
  engineWorkerPackageQueryCancellationIsRunning,
  engineWorkerPackageQueryCompletionEvent,
  engineWorkerPackageQueryDurableEvent,
  engineWorkerPackageQueryInput,
  mapEngineWorkerPackageQueryCredit,
  mapEngineWorkerPackageQueryResult,
  registerEngineWorkerPackageQueryOperation,
  type EngineWorkerPackageQueryCompletionEvent,
  type EngineWorkerPackageQueryDurableEvent,
  type EngineWorkerPackageQueryFacade,
} from "../src/engine-worker-package-query.ts";
import type {
  BrowserPackageQueryResult,
} from "../src/facades/inspect-web-package.d.ts";
import {
  createOperationAuthorityPage,
  type OperationFeatureEvent,
  type OperationHandle,
  type OperationSession,
} from "../src/operation-authority.ts";
import {
  createAssemblyQueryRequest,
  createQueryRequest,
  type QueryRequest,
} from "../src/package-query.ts";
import {
  FakeWorkerOperationCatalog,
  FakeWorkerRuntime,
  ManualWorkerRuntimeEnvironment,
  QueueWorkerRuntimeTransportFactory,
  WorkerRuntimeHost,
  type WorkerRuntimePreparationError,
  type WorkerRuntimeSource,
  type WorkerRuntimeTransportBinding,
  type WorkerRuntimeTransportHandlers,
} from "../src/worker-runtime-core.ts";

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

const progressEvent: EngineWorkerPackageQueryDurableEvent = {
  kind: "Progress",
  row: null,
  failure: null,
  completion: null,
  progress: {
    phase: "Manifest",
    completed: 1,
    limit: 20,
  },
  assessment: null,
};

const matchEvent: EngineWorkerPackageQueryDurableEvent = {
  kind: "Match",
  row: {
    packageId: "Contoso.Library",
    version: "1.2.3",
    tier: "Nuspec",
    evidence: [{
      id: "description",
      text: "matched package description",
      scope: "Package",
      summary: {
        count: 2,
        preview: ["first", "second"],
      },
    }],
    totalDownloads: 42,
    verified: true,
    producer: "nuget-gallery",
    description: "A test package.",
    rootRequest: "root1:Contoso.Library@1.2.3",
  },
  failure: null,
  completion: null,
  progress: null,
  assessment: null,
};

const failureEvent: EngineWorkerPackageQueryDurableEvent = {
  kind: "Failure",
  row: null,
  failure: {
    packageId: "Contoso.Broken",
    version: "1.0.0",
    producer: "manifest",
    kind: "ManifestAcquisition",
    message: "manifest unavailable",
  },
  completion: null,
  progress: null,
  assessment: null,
};

const assessmentEvent: EngineWorkerPackageQueryDurableEvent = {
  kind: "Assessment",
  row: null,
  failure: null,
  completion: null,
  progress: null,
  assessment: {
    packageId: "Contoso.Other",
    version: "2.0.0",
    disposition: "NoMatch",
    message: "literal absent",
    assetPath: "lib/net10.0/Contoso.Other.dll",
    rootRequest: "root1:Contoso.Other@2.0.0",
  },
};

const completionEvent: EngineWorkerPackageQueryCompletionEvent = {
  kind: "Completed",
  row: null,
  failure: null,
  completion: {
    prefix: "Contoso.",
    producer: "nuget-gallery",
    candidateLimit: 200,
    matchLimit: 100,
    candidates: 2,
    matches: 1,
    failures: 1,
    kind: "Exhausted",
    sourceCandidates: 2,
    estimatedTotalHits: 2,
    semanticMisses: 0,
    notApplicable: 0,
    scope: "prefix",
  },
  progress: null,
  assessment: null,
};

function succeeded(
  value: EngineWorkerPackageQueryCompletionEvent = completionEvent,
): BrowserPackageQueryResult {
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

function canceled(reason: string): BrowserPackageQueryResult {
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

function emit(
  eventSink: unknown,
  event:
    | EngineWorkerPackageQueryDurableEvent
    | EngineWorkerPackageQueryCompletionEvent,
): void {
  if (typeof eventSink !== "object" || eventSink === null) {
    throw new Error("Expected an event sink.");
  }
  if (!Reflect.set(eventSink, "event", JSON.stringify(event))) {
    throw new Error("Package Query event sink rejected an event.");
  }
}

interface Harness {
  readonly environment: ManualWorkerRuntimeEnvironment;
  readonly worker: FakeWorkerRuntime<string, string>;
  readonly host: WorkerRuntimeHost<string, string>;
  readonly adapter: EngineWorkerPackageQueryAdapter;
  readonly delayedControlAcknowledgmentCount: () => number;
  readonly releaseControlAcknowledgments: () => void;
}

class DelayedControlAcknowledgmentTransport
implements WorkerRuntimeTransportBinding, WorkerRuntimeSource {
  readonly source: WorkerRuntimeSource = this;
  readonly #worker: FakeWorkerRuntime<string, string>;
  readonly #pending: unknown[] = [];
  #handlers: WorkerRuntimeTransportHandlers | null = null;

  constructor(worker: FakeWorkerRuntime<string, string>) {
    this.#worker = worker;
  }

  get pendingCount(): number {
    return this.#pending.length;
  }

  bind(handlers: WorkerRuntimeTransportHandlers): () => void {
    this.#handlers = handlers;
    const unbind = this.#worker.bind({
      message: (_source, data) => {
        if (typeof data === "object"
          && data !== null
          && Object.getOwnPropertyDescriptor(data, "kind")?.value
            === "control-acknowledged") {
          this.#pending.push(data);
          return;
        }
        handlers.message(this, data);
      },
      error: (_source, diagnostic) => handlers.error(this, diagnostic),
      messageError: (_source, diagnostic) =>
        handlers.messageError(this, diagnostic),
    });
    return () => {
      if (this.#handlers === handlers) this.#handlers = null;
      unbind();
    };
  }

  send(message: unknown): void {
    this.#worker.send(message);
  }

  terminate(): void {
    this.#worker.terminate();
  }

  release(): void {
    const handlers = this.#handlers;
    if (handlers === null) return;
    for (const data of this.#pending.splice(0))
      handlers.message(this, data);
  }
}

function createHarness(
  facade: EngineWorkerPackageQueryFacade,
  options: {
    readonly delayControlAcknowledgments?: boolean;
  } = {},
): Harness {
  const environment = new ManualWorkerRuntimeEnvironment();
  const operations = new FakeWorkerOperationCatalog();
  registerEngineWorkerPackageQueryOperation(operations, () => facade);
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
  const delayedTransport = options.delayControlAcknowledgments === true
    ? new DelayedControlAcknowledgmentTransport(worker)
    : null;
  const host = new WorkerRuntimeHost<string, string>({
    transport: new QueueWorkerRuntimeTransportFactory([
      delayedTransport ?? worker,
    ]),
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
    adapter: registerEngineWorkerPackageQueryAdapter(host),
    delayedControlAcknowledgmentCount: () =>
      delayedTransport?.pendingCount ?? 0,
    releaseControlAcknowledgments: () => delayedTransport?.release(),
  };
}

async function startReady(harness: Harness): Promise<void> {
  assert.equal(harness.host.start("").kind, "started");
  await harness.environment.flushAsync();
  assert.equal(harness.host.snapshot().phase, "ready");
}

type PackageQueryFeatureEvent = OperationFeatureEvent<
  EngineWorkerPackageQueryCompletionEvent,
  string,
  never,
  EngineWorkerPackageQueryDurableEvent
>;

type PackageQuerySession = OperationSession<
  QueryRequest,
  EngineWorkerPackageQueryCompletionEvent,
  string,
  never,
  WorkerRuntimePreparationError,
  EngineWorkerPackageQueryDurableEvent
>;

function createQuerySession(
  operationIds: readonly string[],
): {
  readonly events: PackageQueryFeatureEvent[];
  readonly session: PackageQuerySession;
} {
  let nextOperationId = 0;
  const events: PackageQueryFeatureEvent[] = [];
  return {
    events,
    session: createOperationAuthorityPage({
      allocation: {
        createId: () => {
          const operationId = operationIds[nextOperationId++];
          if (operationId === undefined)
            throw new Error("No Package Query operation ID remains.");
          return operationId;
        },
      },
    }).createSession<
      QueryRequest,
      EngineWorkerPackageQueryCompletionEvent,
      string,
      never,
      WorkerRuntimePreparationError,
      EngineWorkerPackageQueryDurableEvent
    >({
      feature: {
        publish: event => {
          events.push(event);
          return undefined;
        },
      },
      diagnostic: { report: () => undefined },
    }),
  };
}

function startQuery(
  adapter: EngineWorkerPackageQueryAdapter,
  request: QueryRequest = createQueryRequest("Contoso."),
  operationId = "package-query-operation",
  sessionState = createQuerySession([operationId]),
): {
  readonly handle: OperationHandle<
    EngineWorkerPackageQueryCompletionEvent,
    string
  >;
  readonly events: PackageQueryFeatureEvent[];
} {
  const { events, session } = sessionState;
  const started = session.start(request, adapter);
  assert.equal(started.kind, "started");
  if (started.kind !== "started")
    throw new Error("Expected Package Query to start.");
  return { handle: started.handle, events };
}

test("Package Query Worker adapter preserves request, durable events, credit, and terminal result", async () => {
  const terminal = deferred<BrowserPackageQueryResult>();
  const runs: unknown[][] = [];
  const credits: unknown[][] = [];
  const facade: EngineWorkerPackageQueryFacade = {
    cancelPackageQuery: (_operationId, reason) => ({
      kind: "Requested",
      reason,
    }),
    requestPackageQueryMatches: (...args) => {
      credits.push(args);
      return {
        kind: "Granted",
        additionalMatchCredit: args[1],
      };
    },
    runPackageAssemblyQuery: () => Promise.resolve(succeeded()),
    runPackageQuery: (...args) => {
      runs.push(args);
      emit(args[7], progressEvent);
      emit(args[7], matchEvent);
      emit(args[7], failureEvent);
      emit(args[7], assessmentEvent);
      return terminal.promise;
    },
  };
  const harness = createHarness(facade);
  await startReady(harness);

  const request: QueryRequest = {
    ...createQueryRequest("Contoso.", "gallery"),
    facets: [{
      key: "package.query.source-verified",
      label: "Verified",
      tier: "nuspec",
    }],
    packageType: "Dependency",
    sourceOrderId: "downloads",
    includePrerelease: true,
  };
  const { handle, events } = startQuery(harness.adapter, request);
  await harness.environment.flushAsync();

  const acknowledged = harness.adapter.requestControl(handle.id, 10);
  const overlapping = harness.adapter.requestControl(handle.id, 10);
  assert.deepEqual(await overlapping, {
    kind: "failed",
    error: "Package Query control is already pending.",
  });
  await harness.environment.flushAsync();
  assert.deepEqual(await acknowledged, {
    kind: "acknowledged",
    value: 10,
  });
  assert.deepEqual(credits, [["package-query-operation", 10]]);

  terminal.resolve(succeeded());
  await Promise.resolve();
  await harness.environment.flushAsync();
  assert.deepEqual(await handle.outcome, {
    kind: "succeeded",
    value: completionEvent,
  });
  await handle.quiesced;
  assert.deepEqual(
    events
      .filter(event => event.kind === "durable")
      .map(event => event.durable.value),
    [progressEvent, matchEvent, failureEvent, assessmentEvent],
  );
  assert.deepEqual(runs[0]?.slice(0, 7), [
    "package-query-operation",
    "Contoso.",
    '["package.query.source-verified"]',
    200,
    100,
    true,
    20,
  ]);
  assert.deepEqual(runs[0]?.slice(8), [
    "Dependency",
    "downloads",
    true,
  ]);

  const start = harness.worker.receivedMessages.find(message =>
    typeof message === "object"
    && message !== null
    && Object.getOwnPropertyDescriptor(message, "kind")?.value === "start");
  assert.notEqual(start, undefined);
  let payload: unknown;
  if (start !== undefined) {
    const descriptor = Object.getOwnPropertyDescriptor(start, "payload");
    payload = descriptor?.value;
  }
  assert.deepEqual(payload, {
    kind: "query",
    searchText: "Contoso.",
    facetIds: ["package.query.source-verified"],
    maximumCandidates: 200,
    maximumMatches: 100,
    includePrerelease: true,
    initialMatchCredit: 20,
    packageType: "Dependency",
    sourceOrderId: "downloads",
    discovery: true,
  });
  assert.doesNotThrow(() => structuredClone(payload));
  harness.host.dispose();
});

test("Package Query Worker adapter projects assembly requests and keyed cancellation", async () => {
  const terminal = deferred<BrowserPackageQueryResult>();
  const runs: unknown[][] = [];
  const cancellations: unknown[][] = [];
  const facade: EngineWorkerPackageQueryFacade = {
    cancelPackageQuery: (...args) => {
      cancellations.push(args);
      terminal.resolve(canceled(args[1]));
      return { kind: "Requested", reason: args[1] };
    },
    requestPackageQueryMatches: () => ({
      kind: "NotActive",
      additionalMatchCredit: null,
    }),
    runPackageAssemblyQuery: (...args) => {
      runs.push(args);
      return terminal.promise;
    },
    runPackageQuery: () => Promise.resolve(succeeded()),
  };
  const harness = createHarness(facade);
  await startReady(harness);

  const { handle } = startQuery(
    harness.adapter,
    createAssemblyQueryRequest(
      "package.query.assembly.ldstr-contains",
      "literal",
      ["Contoso.Library@1.2.3"],
      "net10.0"),
    "assembly-operation",
  );
  await harness.environment.flushAsync();
  assert.equal(handle.cancel("superseded").kind, "applied");
  await harness.environment.flushAsync();

  assert.deepEqual(cancellations, [
    ["assembly-operation", "superseded"],
  ]);
  assert.deepEqual(runs[0]?.slice(0, 6), [
    "assembly-operation",
    "package.query.assembly.ldstr-contains",
    "literal",
    '["Contoso.Library@1.2.3"]',
    "net10.0",
    20,
  ]);
  assert.deepEqual(await handle.outcome, {
    kind: "canceled",
    reason: "superseded",
  });
  await handle.quiesced;
  harness.host.dispose();
});

test("Package Query credit reports not-active and closes after settlement", async () => {
  const terminal = deferred<BrowserPackageQueryResult>();
  const facade: EngineWorkerPackageQueryFacade = {
    cancelPackageQuery: () => ({ kind: "NotActive", reason: null }),
    requestPackageQueryMatches: () => ({
      kind: "NotActive",
      additionalMatchCredit: null,
    }),
    runPackageAssemblyQuery: () => terminal.promise,
    runPackageQuery: () => terminal.promise,
  };
  const harness = createHarness(facade);
  await startReady(harness);
  const { handle } = startQuery(harness.adapter);
  await harness.environment.flushAsync();

  const inactive = harness.adapter.requestControl(handle.id, 10);
  await harness.environment.flushAsync();
  assert.deepEqual(await inactive, { kind: "not-active" });

  terminal.resolve(succeeded());
  await Promise.resolve();
  await harness.environment.flushAsync();
  await handle.quiesced;
  assert.deepEqual(
    await harness.adapter.requestControl(handle.id, 10),
    { kind: "not-active" },
  );
  harness.host.dispose();
});

test("Package Query retains an already-posted credit response across settlement", async () => {
  const terminal = deferred<BrowserPackageQueryResult>();
  const facade: EngineWorkerPackageQueryFacade = {
    cancelPackageQuery: () => ({ kind: "NotActive", reason: null }),
    requestPackageQueryMatches: (_operationId, amount) => ({
      kind: "Granted",
      additionalMatchCredit: amount,
    }),
    runPackageAssemblyQuery: () => terminal.promise,
    runPackageQuery: () => terminal.promise,
  };
  const harness = createHarness(facade, {
    delayControlAcknowledgments: true,
  });
  await startReady(harness);
  const { handle } = startQuery(harness.adapter);
  await harness.environment.flushAsync();

  const pendingCredit = harness.adapter.requestControl(handle.id, 10);
  await harness.environment.flushAsync();
  assert.equal(harness.delayedControlAcknowledgmentCount(), 1);

  terminal.resolve(succeeded());
  await Promise.resolve();
  await harness.environment.flushAsync();
  assert.deepEqual(await handle.outcome, {
    kind: "succeeded",
    value: completionEvent,
  });
  assert.deepEqual(
    await harness.adapter.requestControl(handle.id, 10),
    { kind: "not-active" },
  );

  harness.releaseControlAcknowledgments();
  assert.deepEqual(await pendingCredit, {
    kind: "acknowledged",
    value: 10,
  });
  await handle.quiesced;
  harness.host.dispose();
});

test("Package Query generated terminal validation contains malformed results to one operation", async () => {
  const facade: EngineWorkerPackageQueryFacade = {
    cancelPackageQuery: () => ({ kind: "NotActive", reason: null }),
    requestPackageQueryMatches: () => ({
      kind: "NotActive",
      additionalMatchCredit: null,
    }),
    runPackageAssemblyQuery: () => Promise.resolve({
      ...succeeded(),
      version: 2,
    }),
    runPackageQuery: () => Promise.resolve({
      ...succeeded(),
      value: matchEvent,
    }),
  };
  const harness = createHarness(facade);
  await startReady(harness);
  const sessionState = createQuerySession([
    "invalid-result",
    "assembly-invalid-result",
  ]);
  const { handle } = startQuery(
    harness.adapter,
    createQueryRequest("Contoso."),
    "invalid-result",
    sessionState,
  );
  await harness.environment.flushAsync();

  assert.deepEqual(await handle.outcome, {
    kind: "failed",
    error: "Package Query returned invalid Worker boundary data.",
  });
  await handle.quiesced;
  await harness.environment.flushAsync();
  assert.equal(harness.host.snapshot().phase, "ready");

  const assembly = startQuery(
    harness.adapter,
    createAssemblyQueryRequest(
      "package.query.assembly.ldstr-contains",
      "literal",
      ["Contoso.Library@1.2.3"],
      "net10.0"),
    "assembly-invalid-result",
    sessionState,
  );
  await harness.environment.flushAsync();
  assert.deepEqual(await assembly.handle.outcome, {
    kind: "failed",
    error: "Package Query returned invalid Worker boundary data.",
  });
  await assembly.handle.quiesced;
  assert.equal(harness.host.snapshot().phase, "ready");
  harness.host.dispose();
});

test("Package Query rejects terminal callbacks without failing the realm", async () => {
  const facade: EngineWorkerPackageQueryFacade = {
    cancelPackageQuery: () => ({ kind: "NotActive", reason: null }),
    requestPackageQueryMatches: () => ({
      kind: "NotActive",
      additionalMatchCredit: null,
    }),
    runPackageAssemblyQuery: () => Promise.resolve(succeeded()),
    async runPackageQuery(...args) {
      try {
        emit(args[7], completionEvent);
      } catch (error: unknown) {
        return {
          version: 1,
          kind: "Failed",
          value: null,
          failureKind: "Unexpected",
          error: "Package Query callback was rejected.",
          diagnostic: error instanceof Error ? error.message : String(error),
          reason: null,
        };
      }
      throw new Error("Expected the terminal callback to be rejected.");
    },
  };
  const harness = createHarness(facade);
  await startReady(harness);
  const { handle } = startQuery(harness.adapter);
  await harness.environment.flushAsync();

  assert.deepEqual(await handle.outcome, {
    kind: "failed",
    error: "Package Query callback was rejected.",
  });
  await handle.quiesced;
  assert.equal(harness.host.snapshot().phase, "ready");
  harness.host.dispose();
});

test("Package Query codecs reject terminal callbacks, malformed descriptors, and payload excess", () => {
  assert.equal(
    engineWorkerPackageQueryDurableEvent.decode(completionEvent).kind,
    "rejected",
  );
  assert.equal(
    engineWorkerPackageQueryCompletionEvent.decode(matchEvent).kind,
    "rejected",
  );
  assert.equal(engineWorkerPackageQueryInput.decode({
    kind: "assembly",
    patternId: "pattern",
    operand: "value",
    packageCoordinates: Array.from(
      { length: 4_097 },
      () => "Contoso.Package@1.0.0"),
    targetFramework: "net10.0",
    initialMatchCredit: 20,
  }).kind, "rejected");
  assert.equal(engineWorkerPackageQueryDurableEvent.decode({
    ...matchEvent,
    row: {
      ...matchEvent.row,
      description: "x".repeat(1_048_577),
    },
  }).kind, "rejected");

  const accessor = { ...matchEvent };
  Object.defineProperty(accessor, "row", {
    get: () => matchEvent.row,
  });
  assert.equal(
    engineWorkerPackageQueryDurableEvent.decode(accessor).kind,
    "rejected",
  );
});

test("Package Query cancellation, credit, and terminal mappers enforce exact responses", () => {
  assert.equal(engineWorkerPackageQueryCancellationIsRunning(
    { kind: "Requested", reason: "user" },
    "user",
  ), true);
  assert.equal(engineWorkerPackageQueryCancellationIsRunning(
    { kind: "AlreadyRequested", reason: "user" },
    "superseded",
  ), true);
  assert.equal(engineWorkerPackageQueryCancellationIsRunning(
    { kind: "NotActive", reason: null },
    "user",
  ), false);
  assert.throws(() => engineWorkerPackageQueryCancellationIsRunning(
    { kind: "Requested", reason: "timeout" },
    "user",
  ), /changed the requested reason/);

  assert.deepEqual(mapEngineWorkerPackageQueryCredit({
    kind: "Granted",
    additionalMatchCredit: 10,
  }, 10), { kind: "acknowledged", value: 10 });
  assert.deepEqual(mapEngineWorkerPackageQueryCredit({
    kind: "NotActive",
    additionalMatchCredit: null,
  }, 10), { kind: "not-active" });
  assert.throws(() => mapEngineWorkerPackageQueryCredit({
    kind: "Granted",
    additionalMatchCredit: 5,
  }, 10), /different match-credit amount/);

  assert.deepEqual(mapEngineWorkerPackageQueryResult({
    version: 1,
    kind: "Failed",
    value: null,
    failureKind: "Expected",
    error: "query unavailable",
    diagnostic: "expected detail",
    reason: null,
  }), {
    kind: "failed",
    failureKind: "expected",
    error: "query unavailable",
    diagnostic: "expected detail",
  });
  assert.deepEqual(mapEngineWorkerPackageQueryResult(canceled("timeout")), {
    kind: "canceled",
    reason: "timeout",
  });
  assert.deepEqual(mapEngineWorkerPackageQueryResult({
    version: 1,
    kind: "Failed",
    value: null,
    failureKind: "Unexpected",
    error: "query failed",
    diagnostic: "x".repeat(65_537),
    reason: null,
  }), {
    kind: "failed",
    failureKind: "unexpected",
    error: "Package Query exceeded Worker transport limits.",
    diagnostic: "Package Query diagnostic exceeds 65536 characters.",
  });
});
