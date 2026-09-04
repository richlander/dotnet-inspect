import assert from "node:assert/strict";
import test from "node:test";

import {
  createOperationAuthorityPage,
  type OperationFeatureEvent,
  type OperationHandle,
  type OperationIdentity,
  type OperationOutcome,
  type OperationPreparation,
  type OperationProducerAdapter,
  type OperationProducerSink,
  type OperationSession,
  type OperationStartResult,
  type PreparedOperationProducer,
} from "../src/operation-authority.ts";
import {
  FakeWorkerOperationCatalog,
  FakeWorkerRuntime,
  ManualWorkerRuntimeEnvironment,
  QueueWorkerRuntimeTransportFactory,
  WorkerEpochTokenAllocator,
  WorkerProbeSequenceAllocator,
  WorkerProducerClassRegistry,
  WorkerRuntimeHost,
  type FakeWorkerOperationContext,
  type FakeWorkerOperationRegistration,
  type FakeWorkerRuntimeOptions,
  type WorkerRuntimeBoundaryErrors,
  type WorkerRuntimeFailure,
  type WorkerRuntimeFailureKind,
  type WorkerRuntimeLifecycleListeners,
  type WorkerRuntimeOperationRegistration,
  type WorkerRuntimePreparationError,
  type WorkerRuntimeSource,
  type WorkerRuntimeTransportBinding,
  type WorkerRuntimeTransportHandlers,
} from "../src/worker-runtime-core.ts";
import {
  type BoundedPayloadDecodeResult,
  type BoundedPayloadDecoder,
  type ManagedOperationSettlement,
  type WorkerLivenessAllowance,
  WORKER_RUNTIME_PROTOCOL_VERSION,
} from "../src/worker-runtime-protocol.ts";

interface TestDiagnostic {
  readonly code: string;
  readonly detail: unknown;
}

type TestAdapter = OperationProducerAdapter<
  string,
  string,
  string,
  string,
  WorkerRuntimePreparationError
>;
type TestSession = OperationSession<
  string,
  string,
  string,
  string,
  WorkerRuntimePreparationError
>;
type TestEvent = OperationFeatureEvent<string, string, string>;
type TestHost = WorkerRuntimeHost<
  string,
  TestDiagnostic
>;
type TestWorker = FakeWorkerRuntime<
  string,
  TestDiagnostic
>;
type TestSettlement = ManagedOperationSettlement<
  string,
  string,
  TestDiagnostic
>;

interface Deferred<T> {
  readonly promise: Promise<T>;
  readonly resolve: (value: T) => void;
  readonly reject: (error: Error) => void;
}

interface TestHarness {
  readonly environment: ManualWorkerRuntimeEnvironment;
  readonly producerClasses: WorkerProducerClassRegistry;
  readonly workerProducerClasses: readonly WorkerProducerClassRegistry[];
  readonly workers: readonly TestWorker[];
  readonly host: TestHost;
  readonly adapter: TestAdapter;
  readonly failures: WorkerRuntimeFailure<TestDiagnostic>[];
  readonly runtimeDiagnostics: TestDiagnostic[];
  readonly releasedEpochs: number[];
}

interface HarnessOptions {
  readonly bootstrap?: () => void | Promise<void>;
  readonly encodeBootstrap?: (
    bootstrap: string,
  ) => BoundedPayloadDecodeResult<unknown>;
  readonly encodeInput?: (
    input: string,
  ) => BoundedPayloadDecodeResult<unknown>;
  readonly clockUnsubscribeError?: Error;
  readonly lifecycleUnsubscribeError?: Error;
  readonly detachError?: Error;
  readonly synchronousAccepted?: boolean;
  readonly invoke?: (
    input: string,
    context: FakeWorkerOperationContext,
  ) => TestSettlement | Promise<TestSettlement>;
  readonly cancel?: FakeWorkerOperationRegistration<
    string,
    string,
    string,
    TestDiagnostic
  >["cancel"];
  readonly allowance?: WorkerLivenessAllowance;
  readonly omitResponse?: FakeWorkerRuntimeOptions<
    string,
    TestDiagnostic
  >["omitResponse"];
  readonly maximumEpochToken?: number;
  readonly maximumOperationSequence?: number;
  readonly createProbeSequenceAllocator?: () => WorkerProbeSequenceAllocator;
  readonly workerCount?: number;
  readonly producerClassDefinitions?: readonly ProducerClassDefinition[];
  readonly startupBudgetMilliseconds?: number;
  readonly controlResponseGraceMilliseconds?: number;
  readonly drainBudgetMilliseconds?: number;
  readonly failure?: (failure: WorkerRuntimeFailure<TestDiagnostic>) => void;
  readonly realmReleased?: (epochToken: number) => void;
  readonly workerProducerClassIdleAllowanceMilliseconds?: number;
  readonly workerAllowance?: WorkerLivenessAllowance;
}

interface ProducerClassDefinition {
  readonly name: string;
  readonly allowance: WorkerLivenessAllowance;
  readonly structuralBoundMilliseconds: number | null;
}

function deferred<T>(): Deferred<T> {
  let resolvePromise: ((value: T) => void) | undefined;
  let rejectPromise: ((error: Error) => void) | undefined;
  const promise = new Promise<T>((resolve, reject) => {
    resolvePromise = resolve;
    rejectPromise = reject;
  });
  return {
    promise,
    resolve: value => {
      resolvePromise?.(value);
    },
    reject: error => {
      rejectPromise?.(error);
    },
  };
}

function stringDecoder(): BoundedPayloadDecoder<string> {
  return {
    decode: value => typeof value === "string"
      ? { kind: "decoded", value }
      : {
          kind: "rejected",
          reason: "invalid",
          message: "Expected a string.",
        },
  };
}

function terminalCallbacks<TValue, TError>(
  publish: (outcome: OperationOutcome<TValue, TError>) => undefined,
): Pick<
  OperationProducerSink<TValue, TError, never>,
  "commitTerminal" | "reportTerminal"
> {
  return {
    commitTerminal: outcome => ({
      publish: () => publish(outcome),
    }),
    reportTerminal: publish,
  };
}

function boundaryErrors<TError>(
  create: (kind: WorkerRuntimeFailureKind) => TError,
): WorkerRuntimeBoundaryErrors<TError> {
  return {
    startup: create("startup"),
    "worker-crash": create("worker-crash"),
    protocol: create("protocol"),
    watchdog: create("watchdog"),
    "control-response": create("control-response"),
    "probe-exhaustion": create("probe-exhaustion"),
    "worker-declared": create("worker-declared"),
    "worker-message": create("worker-message"),
  };
}

function diagnosticDecoder(): BoundedPayloadDecoder<TestDiagnostic> {
  return {
    decode: value => {
      if (typeof value !== "object" || value === null) {
        return {
          kind: "rejected",
          reason: "invalid",
          message: "Expected a diagnostic object.",
        };
      }
      const code = Object.getOwnPropertyDescriptor(value, "code");
      const detail = Object.getOwnPropertyDescriptor(value, "detail");
      if (code === undefined
        || !("value" in code)
        || typeof code.value !== "string"
        || detail === undefined
        || !("value" in detail)) {
        return {
          kind: "rejected",
          reason: "invalid",
          message: "Expected closed diagnostic data.",
        };
      }
      return {
        kind: "decoded",
        value: { code: code.value, detail: detail.value },
      };
    },
  };
}

function recordDecoder<T>(
  decode: (value: object) => T | null,
  message: string,
): BoundedPayloadDecoder<T> {
  return {
    decode: value => {
      if (typeof value === "object" && value !== null && !Array.isArray(value)) {
        const decoded = decode(value);
        if (decoded !== null) return { kind: "decoded", value: decoded };
      }
      return {
        kind: "rejected",
        reason: "invalid",
        message,
      };
    },
  };
}

function mainRegistration(
  allowance: WorkerLivenessAllowance,
  encodeInput: (
    input: string,
  ) => BoundedPayloadDecodeResult<unknown> = input => input.length <= 32
    ? { kind: "decoded", value: input }
    : {
        kind: "rejected",
        reason: "oversized",
        message: "Input exceeds 32 code units.",
      },
): WorkerRuntimeOperationRegistration<
  string,
  string,
  string,
  TestDiagnostic,
  string,
  WorkerRuntimePreparationError
> {
  return {
    kind: "echo",
    allowance,
    encodeInput,
    value: stringDecoder(),
    error: stringDecoder(),
    diagnostic: diagnosticDecoder(),
    progress: stringDecoder(),
    mapPreparationError: error => error,
    boundaryErrors: boundaryErrors(kind => `boundary:${kind}`),
  };
}

function createProducerClasses(
  definitions: readonly ProducerClassDefinition[],
  idleAllowanceMilliseconds = 10,
): WorkerProducerClassRegistry {
  const producerClasses = new WorkerProducerClassRegistry(
    idleAllowanceMilliseconds,
  );
  for (const definition of definitions) {
    producerClasses.register(
      definition.name,
      definition.allowance,
      definition.structuralBoundMilliseconds,
    );
  }
  return producerClasses;
}

function createHarness(options: HarnessOptions = {}): TestHarness {
  const environment = new ManualWorkerRuntimeEnvironment();
  const allowance = options.allowance ?? {
    kind: "bounded",
    maxSilentActiveMilliseconds: 20,
  };
  const producerClassDefinitions = options.producerClassDefinitions ?? [{
    name: "speculative",
    allowance: { kind: "bounded", maxSilentActiveMilliseconds: 30 },
    structuralBoundMilliseconds: 30,
  }];
  const producerClasses = createProducerClasses(producerClassDefinitions);
  const workerCount = options.workerCount ?? 1;
  const workers: TestWorker[] = [];
  const workerProducerClasses: WorkerProducerClassRegistry[] = [];
  for (let index = 0; index < workerCount; index++) {
    const operations = new FakeWorkerOperationCatalog();
    operations.register({
      kind: "echo",
      allowance: options.workerAllowance ?? allowance,
      input: stringDecoder(),
      rejectInvalidPayload: detail => ({
        error: "invalid-payload",
        diagnostic: { code: "invalid-payload", detail },
      }),
      invoke: options.invoke ?? (input => ({
        kind: "succeeded",
        value: input,
      })),
      ...(options.cancel === undefined ? {} : { cancel: options.cancel }),
    });
    const workerClasses = createProducerClasses(
      producerClassDefinitions,
      options.workerProducerClassIdleAllowanceMilliseconds ?? 10,
    );
    workerProducerClasses.push(workerClasses);
    workers.push(new FakeWorkerRuntime({
      scheduler: environment,
      bootstrap: {
        decoder: stringDecoder(),
        bootstrap: options.bootstrap ?? (() => undefined),
      },
      diagnostic: detail => ({ code: "worker", detail }),
      unknownOperationRejection: kind => ({
        error: "unknown-operation-kind",
        diagnostic: { code: "unknown-operation-kind", detail: kind },
      }),
      operations,
      producerClasses: workerClasses,
      ...(options.omitResponse === undefined
        ? {}
        : { omitResponse: options.omitResponse }),
    }));
  }

  const failures: WorkerRuntimeFailure<TestDiagnostic>[] = [];
  const runtimeDiagnostics: TestDiagnostic[] = [];
  const releasedEpochs: number[] = [];
  const detachError = options.detachError;
  const clockUnsubscribeError = options.clockUnsubscribeError;
  const lifecycleUnsubscribeError = options.lifecycleUnsubscribeError;
  const queuedTransport = new QueueWorkerRuntimeTransportFactory(workers);
  const transport = detachError === undefined
    && options.synchronousAccepted !== true
    ? queuedTransport
    : {
        create: (): WorkerRuntimeTransportBinding => {
          const binding = queuedTransport.create();
          let handlers: WorkerRuntimeTransportHandlers | null = null;
          const source: WorkerRuntimeSource = {
            send: message => {
              binding.source.send(message);
              if (options.synchronousAccepted !== true
                || typeof message !== "object"
                || message === null
                || ownDataProperty(message, "kind") !== "start") {
                return;
              }
              const epochToken = ownDataProperty(message, "epochToken");
              if (typeof epochToken !== "number") return;
              handlers?.message(source, workerEnvelope(epochToken, {
                kind: "accepted",
                operation: ownDataProperty(message, "operation"),
                allowance,
              }));
            },
            terminate: () => binding.source.terminate(),
          };
          return {
            source,
            bind: nextHandlers => {
              handlers = nextHandlers;
              const detach = binding.bind({
                message: (_source, data) =>
                  nextHandlers.message(source, data),
                error: (_source, diagnostic) =>
                  nextHandlers.error(source, diagnostic),
                messageError: (_source, diagnostic) =>
                  nextHandlers.messageError(source, diagnostic),
              });
              return () => {
                handlers = null;
                detach();
                if (detachError !== undefined)
                  throw detachError;
              };
            },
          };
        },
      };
  const clock = clockUnsubscribeError === undefined
    ? environment
    : {
        now: () => environment.now(),
        subscribe: (listener: () => void) => {
          const unsubscribe = environment.subscribe(listener);
          return () => {
            unsubscribe();
            throw clockUnsubscribeError;
          };
        },
      };
  const lifecycle = lifecycleUnsubscribeError === undefined
    ? environment
    : {
        subscribe: (listeners: WorkerRuntimeLifecycleListeners) => {
          const unsubscribe = environment.subscribe(listeners);
          return () => {
            unsubscribe();
            throw lifecycleUnsubscribeError;
          };
        },
      };
  const host = new WorkerRuntimeHost<string, TestDiagnostic>({
    transport,
    clock,
    lifecycle,
    bootstrap: {
      encode: options.encodeBootstrap
        ?? (bootstrap => ({ kind: "decoded", value: bootstrap })),
      diagnostic: diagnosticDecoder(),
    },
    diagnostic: diagnosticDecoder(),
    callbacks: {
      failure: failure => {
        failures.push(failure);
        options.failure?.(failure);
        return undefined;
      },
      diagnostic: diagnostic => {
        runtimeDiagnostics.push(diagnostic.diagnostic);
        return undefined;
      },
      realmReleased: epochToken => {
        releasedEpochs.push(epochToken);
        options.realmReleased?.(epochToken);
        return undefined;
      },
    },
    createDiagnostic: (kind, detail) => ({ code: kind, detail }),
    idleHeartbeatIntervalMilliseconds: 10,
    startupBudgetMilliseconds: options.startupBudgetMilliseconds ?? 100,
    controlResponseGraceMilliseconds:
      options.controlResponseGraceMilliseconds ?? 10,
    drainBudgetMilliseconds: options.drainBudgetMilliseconds ?? 20,
    producerClasses,
    ...(options.maximumEpochToken === undefined
      ? {}
      : { maximumEpochToken: options.maximumEpochToken }),
    ...(options.maximumOperationSequence === undefined
      ? {}
      : { maximumOperationSequence: options.maximumOperationSequence }),
    ...(options.createProbeSequenceAllocator === undefined
      ? {}
      : {
          createProbeSequenceAllocator:
            options.createProbeSequenceAllocator,
        }),
  });
  const adapter = host.registerOperation(
    mainRegistration(allowance, options.encodeInput),
  );
  return {
    environment,
    producerClasses,
    workerProducerClasses,
    workers,
    host,
    adapter,
    failures,
    runtimeDiagnostics,
    releasedEpochs,
  };
}

async function startReady(harness: TestHarness): Promise<void> {
  assert.equal(harness.host.start("bootstrap").kind, "started");
  await harness.environment.flushAsync();
  assert.equal(harness.host.snapshot().phase, "ready");
}

function session(
  adapter: TestAdapter,
  page = createOperationAuthorityPage({
    allocation: {
      createId: (() => {
        let id = 1;
        return () => `operation-${id++}`;
      })(),
    },
  }),
): {
  readonly session: TestSession;
  readonly events: TestEvent[];
} {
  const events: TestEvent[] = [];
  const operationSession = page.createSession<
    string,
    string,
    string,
    string,
    WorkerRuntimePreparationError
  >({
    feature: {
      publish: event => {
        events.push(event);
        return undefined;
      },
    },
    diagnostic: {
      report: () => undefined,
    },
  });
  return { session: operationSession, events };
}

function started<TValue, TError, TPrepareError>(
  result: OperationStartResult<TValue, TError, TPrepareError>,
): OperationHandle<TValue, TError> {
  assert.equal(result.kind, "started");
  if (result.kind !== "started")
    throw new Error("Expected a started operation.");
  return result.handle;
}

function captureIdentities(count: number): readonly OperationIdentity[] {
  const captured: OperationIdentity[] = [];
  const authority = createOperationAuthorityPage({
    allocation: {
      createId: (() => {
        let id = 1;
        return () => `captured-operation-${id++}`;
      })(),
    },
  });
  const captureSession = authority.createSession<
    string,
    string,
    string,
    string,
    string
  >({
    feature: { publish: () => undefined },
    diagnostic: { report: () => undefined },
  });
  const captureAdapter: OperationProducerAdapter<
    string,
    string,
    string,
    string,
    string
  > = {
    prepare: identity => {
      captured.push(identity);
      return { kind: "rejected", error: "captured" };
    },
  };
  for (let index = 0; index < count; index++)
    captureSession.start("capture", captureAdapter);
  assert.equal(captured.length, count);
  return captured;
}

function captureIdentity(): OperationIdentity {
  return captureIdentities(1)[0]!;
}

function preparedBinding(
  preparation: OperationPreparation<WorkerRuntimePreparationError>,
) {
  assert.equal(preparation.kind, "prepared");
  if (preparation.kind !== "prepared")
    throw new Error("Expected prepared binding.");
  return preparation.binding;
}

function workerEnvelope(
  token: number,
  envelope: unknown,
): unknown {
  if (typeof envelope !== "object" || envelope === null)
    throw new Error("Worker envelope body must be an object.");
  return {
    protocolVersion: WORKER_RUNTIME_PROTOCOL_VERSION,
    epochToken: token,
    ...envelope,
  };
}

function operationMessages(worker: TestWorker): readonly string[] {
  const kinds: string[] = [];
  for (const message of worker.receivedMessages) {
    if (typeof message !== "object" || message === null) continue;
    const descriptor = Object.getOwnPropertyDescriptor(message, "kind");
    if (descriptor !== undefined
      && "value" in descriptor
      && typeof descriptor.value === "string") {
      kinds.push(descriptor.value);
    }
  }
  return kinds;
}

function ownDataProperty(value: object, property: string): unknown {
  const descriptor = Object.getOwnPropertyDescriptor(value, property);
  if (descriptor === undefined || !("value" in descriptor)) return undefined;
  const propertyValue: unknown = descriptor.value;
  return propertyValue;
}

function postWorker(worker: TestWorker, message: unknown): void {
  worker.send(message);
}

test("epoch tokens are positive, monotonic, non-reused, and exhaust visibly", () => {
  const allocator = new WorkerEpochTokenAllocator(2);
  assert.deepEqual(allocator.allocate(), { kind: "allocated", token: 1 });
  assert.deepEqual(allocator.allocate(), { kind: "allocated", token: 2 });
  assert.deepEqual(allocator.allocate(), { kind: "exhausted" });
  assert.deepEqual(allocator.allocate(), { kind: "exhausted" });
});

test("bootstrap encoding reserves start ownership before reentrant start", async () => {
  let harness: TestHarness;
  let nestedStart: ReturnType<TestHarness["host"]["start"]> | null = null;
  harness = createHarness({
    encodeBootstrap: bootstrap => {
      nestedStart = harness.host.start("nested");
      return { kind: "decoded", value: bootstrap };
    },
  });

  assert.deepEqual(harness.host.start("outer"), {
    kind: "started",
    epochToken: 1,
  });
  assert.deepEqual(nestedStart, {
    kind: "rejected",
    reason: "epoch-active",
  });
  assert.deepEqual(
    operationMessages(harness.workers[0]!),
    ["initialize"],
  );
  await harness.environment.flushAsync();
  assert.equal(harness.host.snapshot().phase, "ready");
  assert.equal(harness.workers[0]!.terminateCount, 0);
});

test("bootstrap encoding disposal prevents post-disposal epoch creation", () => {
  let harness: TestHarness;
  harness = createHarness({
    encodeBootstrap: bootstrap => {
      harness.host.dispose();
      return { kind: "decoded", value: bootstrap };
    },
    startupBudgetMilliseconds: 10,
  });

  assert.deepEqual(harness.host.start("bootstrap"), {
    kind: "rejected",
    reason: "host-disposed",
  });
  assert.deepEqual(harness.host.snapshot(), {
    epochToken: null,
    phase: "absent",
    closure: null,
    heldOperations: 0,
    activeOperations: 0,
    compactControlRecords: 0,
    activeEpochWork: 0,
    outstandingProbeSequence: null,
    deferredControlProbe: false,
    lastTaskEvidenceOrigin: null,
  });
  assert.deepEqual(harness.workers[0]!.receivedMessages, []);
  assert.equal(harness.workers[0]!.terminateCount, 0);
  harness.environment.advanceActive(100);
  assert.equal(harness.failures.length, 0);
  assert.deepEqual(harness.host.start("later"), {
    kind: "rejected",
    reason: "host-disposed",
  });
});

test("synchronous Initialize send failure rejects start without reusing its token", async () => {
  const environment = new ManualWorkerRuntimeEnvironment();
  const initializeError = new Error("Initialize send failed.");
  let terminateCount = 0;
  const throwingBinding: WorkerRuntimeTransportBinding = {
    source: {
      send: () => {
        throw initializeError;
      },
      terminate: () => {
        terminateCount++;
      },
    },
    bind: () => () => undefined,
  };
  const worker = new FakeWorkerRuntime({
    scheduler: environment,
    bootstrap: {
      decoder: stringDecoder(),
      bootstrap: () => undefined,
    },
    diagnostic: detail => ({ code: "worker", detail }),
    unknownOperationRejection: kind => ({
      error: "unknown-operation-kind",
      diagnostic: { code: "unknown-operation-kind", detail: kind },
    }),
    operations: new FakeWorkerOperationCatalog(),
    producerClasses: new WorkerProducerClassRegistry(10),
  });
  const failures: WorkerRuntimeFailure<TestDiagnostic>[] = [];
  const released: number[] = [];
  const host = new WorkerRuntimeHost<string, TestDiagnostic>({
    transport: new QueueWorkerRuntimeTransportFactory([
      throwingBinding,
      worker,
    ]),
    clock: environment,
    lifecycle: environment,
    bootstrap: {
      encode: value => ({ kind: "decoded", value }),
      diagnostic: diagnosticDecoder(),
    },
    diagnostic: diagnosticDecoder(),
    callbacks: {
      failure: failure => {
        failures.push(failure);
        return undefined;
      },
      diagnostic: () => undefined,
      realmReleased: token => {
        released.push(token);
        return undefined;
      },
    },
    createDiagnostic: (kind, detail) => ({ code: kind, detail }),
    idleHeartbeatIntervalMilliseconds: 10,
    startupBudgetMilliseconds: 100,
    controlResponseGraceMilliseconds: 10,
    drainBudgetMilliseconds: 20,
    maximumEpochToken: 2,
    producerClasses: new WorkerProducerClassRegistry(10),
  });

  const first = host.start("first");
  assert.equal(first.kind, "rejected");
  if (first.kind === "rejected") {
    assert.equal(first.reason, "worker-creation-failed");
    assert.equal(first.detail, initializeError);
  }
  assert.equal(failures[0]?.kind, "worker-crash");
  assert.equal(terminateCount, 1);
  assert.deepEqual(released, [1]);

  const second = host.start("second");
  assert.deepEqual(second, { kind: "started", epochToken: 2 });
  await environment.flushAsync();
  assert.equal(host.snapshot().phase, "ready");
});

test("epoch authority requires exact source and token and old traffic is stale", async () => {
  const settlement = deferred<TestSettlement>();
  const harness = createHarness({
    invoke: () => settlement.promise,
    drainBudgetMilliseconds: 5,
    maximumEpochToken: 2,
    workerCount: 2,
  });
  await startReady(harness);
  const firstToken = harness.host.snapshot().epochToken;
  assert.equal(firstToken, 1);
  const firstWorker = harness.workers[0]!;
  const secondWorker = harness.workers[1]!;
  const operationSession = session(harness.adapter);
  const handle = started(
    operationSession.session.start("active", harness.adapter),
  );
  await harness.environment.flushAsync();

  harness.host.receiveMessage(
    secondWorker,
    workerEnvelope(1, { kind: "heartbeat" }),
  );
  assert.equal(harness.failures.length, 0);
  assert.equal(harness.host.snapshot().phase, "ready");
  firstWorker.emitRaw(workerEnvelope(2, { kind: "heartbeat" }));
  assert.equal(harness.failures[0]?.kind, "protocol");
  assert.equal(harness.host.snapshot().phase, "draining");
  assert.deepEqual(await handle.outcome, {
    kind: "failed",
    error: "boundary:protocol",
  });
  assert.equal(harness.runtimeDiagnostics.length, 0);
  harness.environment.advanceActive(5);
  await handle.quiesced;

  assert.equal(firstWorker.terminated, true);
  assert.equal(harness.host.start("replacement").kind, "started");
  await harness.environment.flushAsync();
  assert.equal(harness.host.snapshot().epochToken, 2);
  const replacementOrigin = harness.host.snapshot().lastTaskEvidenceOrigin;
  harness.host.receiveMessage(
    firstWorker,
    workerEnvelope(2, { kind: "heartbeat" }),
  );
  assert.equal(harness.failures.length, 1);
  assert.equal(
    harness.host.snapshot().lastTaskEvidenceOrigin,
    replacementOrigin,
  );

  harness.host.restart();
  assert.deepEqual(harness.host.start("exhausted"), {
    kind: "rejected",
    reason: "epoch-token-exhausted",
  });
  assert.equal(
    harness.runtimeDiagnostics.at(-1)?.code,
    "epoch-token-exhausted",
  );
});

test("preparation rejects synchronously without posting or retaining a sink", () => {
  const harness = createHarness();
  const identity = captureIdentity();
  const calls: string[] = [];
  const sink: OperationProducerSink<string, string, string> = {
    reportProgress: () => {
      calls.push("progress");
      return undefined;
    },
    ...terminalCallbacks(() => {
      calls.push("terminal");
      return undefined;
    }),
    reportUnexpectedTerminal: () => {
      calls.push("unexpected-terminal");
      return undefined;
    },
    reportQuiesced: () => {
      calls.push("quiesced");
      return undefined;
    },
    reportUnexpectedFailure: () => {
      calls.push("unexpected");
      return undefined;
    },
  };
  assert.deepEqual(harness.adapter.prepare(identity, "input", sink), {
    kind: "rejected",
    error: { kind: "epoch-unavailable" },
  });
  assert.deepEqual(calls, []);
  assert.deepEqual(harness.workers[0]!.receivedMessages, []);
});

test("abandonment is resource-free and activation installs before callout", async () => {
  const harness = createHarness();
  await startReady(harness);
  const [identity, secondIdentity] = captureIdentities(2);
  const calls: string[] = [];
  const sink: OperationProducerSink<string, string, string> = {
    reportProgress: () => undefined,
    ...terminalCallbacks(() => {
      calls.push("terminal");
      return undefined;
    }),
    reportUnexpectedTerminal: () => undefined,
    reportQuiesced: () => {
      calls.push("quiesced");
      return undefined;
    },
    reportUnexpectedFailure: () => undefined,
  };
  const abandoned = preparedBinding(
    harness.adapter.prepare(identity!, "input", sink),
  );
  abandoned.abandon();
  abandoned.activate();
  assert.deepEqual(operationMessages(harness.workers[0]!), ["initialize"]);
  assert.deepEqual(calls, []);

  const activated = preparedBinding(
    harness.adapter.prepare(secondIdentity!, "input", sink),
  );
  activated.activate();
  assert.equal(harness.host.snapshot().activeOperations, 1);
  assert.deepEqual(
    operationMessages(harness.workers[0]!),
    ["initialize", "start"],
  );
});

test("cross-session preparation reentrancy preserves assignment order and queued cancellation", async () => {
  let harness: TestHarness;
  let secondSession: ReturnType<typeof session> | null = null;
  const second: {
    handle: OperationHandle<string, string> | null;
  } = { handle: null };
  let cancelResult: ReturnType<OperationHandle<string, string>["cancel"]>
    | null = null;
  harness = createHarness({
    encodeInput: input => {
      if (input === "first") {
        if (secondSession === null)
          throw new Error("Second session was not installed.");
        second.handle = started(
          secondSession.session.start("second", harness.adapter),
        );
        cancelResult = second.handle.cancel("user");
      }
      return { kind: "decoded", value: input };
    },
  });
  await startReady(harness);
  const authority = createOperationAuthorityPage({
    allocation: {
      createId: (() => {
        let id = 1;
        return () => `reentrant-prepare-${id++}`;
      })(),
    },
  });
  const firstSession = session(harness.adapter, authority);
  secondSession = session(harness.adapter, authority);

  const firstHandle = started(
    firstSession.session.start("first", harness.adapter),
  );
  await harness.environment.flushAsync();

  const starts = harness.workers[0]!.receivedMessages.flatMap(message => {
    if (typeof message !== "object" || message === null) return [];
    return ownDataProperty(message, "kind") === "start"
      ? [ownDataProperty(message, "operation")]
      : [];
  });
  assert.deepEqual(starts, [
    { operationId: "reentrant-prepare-1", operationSequence: 1 },
    { operationId: "reentrant-prepare-2", operationSequence: 2 },
  ]);
  assert.deepEqual(cancelResult, { kind: "applied" });
  assert.ok(second.handle);
  assert.deepEqual(await firstHandle.outcome, {
    kind: "succeeded",
    value: "first",
  });
  assert.deepEqual(await second.handle.outcome, {
    kind: "canceled",
    reason: "user",
  });
  await Promise.all([firstHandle.quiesced, second.handle.quiesced]);
  assert.equal(harness.failures.length, 0);
});

test("same-session preparation replacement releases its sequence gap", async () => {
  let harness: TestHarness;
  let operationSession: ReturnType<typeof session> | null = null;
  const replacement: {
    handle: OperationHandle<string, string> | null;
  } = { handle: null };
  harness = createHarness({
    encodeInput: input => {
      if (input === "first") {
        if (operationSession === null)
          throw new Error("Operation session was not installed.");
        replacement.handle = started(
          operationSession.session.start("replacement", harness.adapter),
        );
      }
      return { kind: "decoded", value: input };
    },
  });
  await startReady(harness);
  const authority = createOperationAuthorityPage({
    allocation: {
      createId: (() => {
        let id = 1;
        return () => `reentrant-replacement-${id++}`;
      })(),
    },
  });
  operationSession = session(harness.adapter, authority);

  assert.deepEqual(
    operationSession.session.start("first", harness.adapter),
    { kind: "rejected", reason: { kind: "session-changed" } },
  );
  await harness.environment.flushAsync();

  const starts = harness.workers[0]!.receivedMessages.flatMap(message => {
    if (typeof message !== "object" || message === null) return [];
    return ownDataProperty(message, "kind") === "start"
      ? [ownDataProperty(message, "operation")]
      : [];
  });
  assert.deepEqual(starts, [
    { operationId: "reentrant-replacement-2", operationSequence: 2 },
  ]);
  assert.ok(replacement.handle);
  assert.deepEqual(await replacement.handle.outcome, {
    kind: "succeeded",
    value: "replacement",
  });
  await replacement.handle.quiesced;
  assert.equal(harness.failures.length, 0);
});

test("rejected preparation releases its sequence gap for nested activation", async () => {
  let harness: TestHarness;
  let secondSession: ReturnType<typeof session> | null = null;
  const second: {
    handle: OperationHandle<string, string> | null;
  } = { handle: null };
  harness = createHarness({
    encodeInput: input => {
      if (input !== "first") return { kind: "decoded", value: input };
      if (secondSession === null)
        throw new Error("Second session was not installed.");
      second.handle = started(
        secondSession.session.start("second", harness.adapter),
      );
      return {
        kind: "rejected",
        reason: "invalid",
        message: "First input was rejected.",
      };
    },
  });
  await startReady(harness);
  const authority = createOperationAuthorityPage({
    allocation: {
      createId: (() => {
        let id = 1;
        return () => `reentrant-rejection-${id++}`;
      })(),
    },
  });
  const firstSession = session(harness.adapter, authority);
  secondSession = session(harness.adapter, authority);

  assert.deepEqual(
    firstSession.session.start("first", harness.adapter),
    {
      kind: "rejected",
      reason: {
        kind: "producer-rejected",
        error: {
          kind: "payload-rejected",
          reason: "invalid",
          message: "First input was rejected.",
        },
      },
    },
  );
  await harness.environment.flushAsync();

  const starts = harness.workers[0]!.receivedMessages.flatMap(message => {
    if (typeof message !== "object" || message === null) return [];
    return ownDataProperty(message, "kind") === "start"
      ? [ownDataProperty(message, "operation")]
      : [];
  });
  assert.deepEqual(starts, [
    { operationId: "reentrant-rejection-2", operationSequence: 2 },
  ]);
  assert.ok(second.handle);
  assert.deepEqual(await second.handle.outcome, {
    kind: "succeeded",
    value: "second",
  });
  await second.handle.quiesced;
  assert.equal(harness.failures.length, 0);
});

test("held cancellation is local and readiness flushes remaining starts in sequence order", async () => {
  const bootstrap = deferred<void>();
  const settlement = deferred<TestSettlement>();
  const harness = createHarness({
    bootstrap: () => bootstrap.promise,
    invoke: () => settlement.promise,
  });
  assert.equal(harness.host.start("bootstrap").kind, "started");
  harness.environment.flushTasks();

  const authority = createOperationAuthorityPage({
    allocation: {
      createId: (() => {
        let id = 1;
        return () => `held-${id++}`;
      })(),
    },
  });
  const first = session(harness.adapter, authority);
  const second = session(harness.adapter, authority);
  const third = session(harness.adapter, authority);
  const firstHandle = started(first.session.start("first", harness.adapter));
  const secondHandle = started(second.session.start("second", harness.adapter));
  const thirdHandle = started(third.session.start("third", harness.adapter));

  assert.deepEqual(firstHandle.cancel("user"), { kind: "applied" });
  assert.deepEqual(await firstHandle.outcome, {
    kind: "canceled",
    reason: "user",
  });
  await firstHandle.quiesced;
  assert.equal(harness.host.snapshot().heldOperations, 2);
  assert.deepEqual(operationMessages(harness.workers[0]!), ["initialize"]);

  bootstrap.resolve(undefined);
  await harness.environment.flushAsync();
  await harness.environment.flushAsync();
  assert.equal(harness.host.snapshot().phase, "ready");
  const starts = harness.workers[0]!.receivedMessages.flatMap(message => {
    if (typeof message !== "object" || message === null) return [];
    return ownDataProperty(message, "kind") === "start"
      ? [ownDataProperty(message, "operation")]
      : [];
  });
  assert.deepEqual(
    starts,
    [
      { operationId: "held-2", operationSequence: 2 },
      { operationId: "held-3", operationSequence: 3 },
    ],
  );
  assert.equal(await Promise.race([
    secondHandle.outcome.then(() => "settled"),
    Promise.resolve("pending"),
  ]), "pending");
  void thirdHandle;
});

test("readiness flush accepts a synchronous response to an emitted held start", async () => {
  const bootstrap = deferred<void>();
  const settlement = deferred<TestSettlement>();
  const harness = createHarness({
    bootstrap: () => bootstrap.promise,
    invoke: () => settlement.promise,
    omitResponse: kind => kind === "accepted",
    synchronousAccepted: true,
  });
  assert.equal(harness.host.start("bootstrap").kind, "started");
  harness.environment.flushTasks();
  const operationSession = session(harness.adapter);
  const handle = started(
    operationSession.session.start("input", harness.adapter),
  );

  bootstrap.resolve(undefined);
  await harness.environment.flushAsync();

  assert.equal(harness.host.snapshot().phase, "ready");
  assert.equal(harness.host.snapshot().activeOperations, 1);
  assert.equal(harness.failures.length, 0);
  settlement.resolve({ kind: "succeeded", value: "output" });
  await harness.environment.flushAsync();
  assert.deepEqual(await handle.outcome, {
    kind: "succeeded",
    value: "output",
  });
  await handle.quiesced;
});

test("a warm activation cannot overtake a start activated during readiness flush", async () => {
  const bootstrap = deferred<void>();
  const settlement = deferred<TestSettlement>();
  const harness = createHarness({
    bootstrap: () => bootstrap.promise,
    invoke: () => settlement.promise,
  });
  assert.equal(harness.host.start("bootstrap").kind, "started");
  harness.environment.flushTasks();
  const authority = createOperationAuthorityPage({
    allocation: {
      createId: (() => {
        let id = 1;
        return () => `flush-${id++}`;
      })(),
    },
  });
  const first = session(harness.adapter, authority);
  const second = session(harness.adapter, authority);
  started(first.session.start("first", harness.adapter));
  started(second.session.start("second", harness.adapter));
  bootstrap.resolve(undefined);
  await harness.environment.flushAsync();
  const third = session(harness.adapter, authority);
  started(third.session.start("third", harness.adapter));
  await harness.environment.flushAsync();
  const startOperations = harness.workers[0]!.receivedMessages.flatMap(
    message => {
      if (typeof message !== "object" || message === null) return [];
      if (ownDataProperty(message, "kind") !== "start")
        return [];
      return [ownDataProperty(message, "operation")];
    },
  );
  assert.deepEqual(startOperations, [
    { operationId: "flush-1", operationSequence: 1 },
    { operationId: "flush-2", operationSequence: 2 },
    { operationId: "flush-3", operationSequence: 3 },
  ]);
});

test("startup failure fails held starts without overwriting held local cancellation", async () => {
  const bootstrap = deferred<void>();
  const harness = createHarness({ bootstrap: () => bootstrap.promise });
  assert.equal(harness.host.start("bootstrap").kind, "started");
  harness.environment.flushTasks();
  const authority = createOperationAuthorityPage({
    allocation: {
      createId: (() => {
        let id = 1;
        return () => `startup-${id++}`;
      })(),
    },
  });
  const canceled = session(harness.adapter, authority);
  const failed = session(harness.adapter, authority);
  const canceledHandle = started(
    canceled.session.start("canceled", harness.adapter),
  );
  const failedHandle = started(
    failed.session.start("failed", harness.adapter),
  );
  canceledHandle.cancel("user");
  harness.workers[0]!.emitRaw(workerEnvelope(1, {
    kind: "startup-failed",
    diagnostic: { code: "bootstrap", detail: "failed" },
  }));
  assert.deepEqual(await canceledHandle.outcome, {
    kind: "canceled",
    reason: "user",
  });
  assert.deepEqual(await failedHandle.outcome, {
    kind: "failed",
    error: "boundary:startup",
  });
  await Promise.all([canceledHandle.quiesced, failedHandle.quiesced]);
});

test("closure before activation preserves planned and unexpected outcomes", async () => {
  for (const planned of [true, false]) {
    const bootstrap = deferred<void>();
    const harness = createHarness({ bootstrap: () => bootstrap.promise });
    assert.equal(harness.host.start("bootstrap").kind, "started");
    harness.environment.flushTasks();
    const events: string[] = [];
    let preparation: OperationPreparation<WorkerRuntimePreparationError>
      | null = null;
    const identity = captureIdentity();
    const sink: OperationProducerSink<string, string, string> = {
      reportProgress: () => undefined,
      reportUnexpectedTerminal: () => undefined,
      reportUnexpectedFailure: diagnostic => {
        events.push(`unexpected:${String(diagnostic)}`);
        return undefined;
      },
      ...terminalCallbacks(outcome => {
        events.push(
          outcome.kind === "canceled"
            ? `terminal:${outcome.reason}`
            : outcome.kind === "failed"
              ? `terminal:${outcome.error}`
              : `terminal:${outcome.value}`,
        );
        return undefined;
      }),
      reportQuiesced: () => {
        events.push("quiesced");
        return undefined;
      },
    };
    preparation = harness.adapter.prepare(identity, "input", sink);
    const binding = preparedBinding(preparation);
    if (planned) {
      harness.host.restart();
    } else {
      harness.workers[0]!.emitRaw({ malformed: true });
    }
    binding.activate();
    assert.deepEqual(
      events.map(event => event.startsWith("unexpected:") ? "unexpected" : event),
      planned
        ? ["terminal:worker-restarted", "quiesced"]
        : ["terminal:boundary:protocol", "quiesced"],
    );
    assert.equal(harness.failures.length, planned ? 0 : 1);
    assert.equal(
      operationMessages(harness.workers[0]!).includes("start"),
      false,
    );
  }
});

test("prepared activation callbacks complete before realm release", async () => {
  const order: string[] = [];
  let harness: TestHarness;
  harness = createHarness({
    realmReleased: () => {
      order.push("realm-released");
    },
  });
  await startReady(harness);
  const page = createOperationAuthorityPage({
    allocation: { createId: () => "prepared-operation" },
  });
  const operationSession = page.createSession<
    string,
    string,
    string,
    string,
    WorkerRuntimePreparationError
  >({
    feature: {
      publish: event => {
        order.push(`feature:${event.kind}`);
        if (event.kind === "started") harness.host.restart();
        return undefined;
      },
    },
    diagnostic: { report: () => undefined },
  });

  const handle = started(
    operationSession.start("input", harness.adapter),
  );
  assert.deepEqual(await handle.outcome, {
    kind: "canceled",
    reason: "worker-restarted",
  });
  await handle.quiesced;

  assert.deepEqual(order, [
    "feature:started",
    "feature:canceled",
    "realm-released",
  ]);
  assert.equal(harness.host.snapshot().phase, "closed");
  assert.equal(harness.workers[0]!.terminated, true);
});

test("prepared abandonment completes deferred realm release", async () => {
  const harness = createHarness();
  await startReady(harness);
  const sink: OperationProducerSink<string, string, string> = {
    reportProgress: () => undefined,
    ...terminalCallbacks(() => undefined),
    reportUnexpectedTerminal: () => undefined,
    reportQuiesced: () => undefined,
    reportUnexpectedFailure: () => undefined,
  };
  const binding = preparedBinding(
    harness.adapter.prepare(captureIdentity(), "input", sink),
  );

  harness.host.restart();
  assert.deepEqual(harness.releasedEpochs, []);

  binding.abandon();
  assert.deepEqual(harness.releasedEpochs, [1]);
});

test("terminal observer restart completes before realm release", async () => {
  const settlement = deferred<TestSettlement>();
  const order: string[] = [];
  let harness: TestHarness;
  harness = createHarness({
    invoke: () => settlement.promise,
    realmReleased: () => {
      order.push("realm-released");
    },
  });
  await startReady(harness);
  const page = createOperationAuthorityPage({
    allocation: { createId: () => "reentrant-terminal" },
  });
  const operationSession = page.createSession<
    string,
    string,
    string,
    string,
    WorkerRuntimePreparationError
  >({
    feature: {
      publish: event => {
        if (event.kind !== "terminal") return undefined;
        order.push("terminal-enter");
        harness.host.restart();
        assert.equal(harness.workers[0]!.terminated, true);
        assert.deepEqual(harness.releasedEpochs, []);
        order.push("terminal-exit");
        return undefined;
      },
    },
    diagnostic: { report: () => undefined },
  });

  const handle = started(operationSession.start("input", harness.adapter));
  await harness.environment.flushAsync();
  settlement.resolve({ kind: "succeeded", value: "value" });
  await harness.environment.flushAsync();

  assert.deepEqual(order, [
    "terminal-enter",
    "terminal-exit",
    "realm-released",
  ]);
  assert.deepEqual(await handle.outcome, {
    kind: "succeeded",
    value: "value",
  });
  await handle.quiesced;
  assert.equal(harness.host.snapshot().phase, "closed");
});

test("startup uses one non-renewable active-time budget and only matching Ready succeeds", async () => {
  const bootstrap = deferred<void>();
  const harness = createHarness({
    bootstrap: () => bootstrap.promise,
    startupBudgetMilliseconds: 10,
  });
  assert.equal(harness.host.start("bootstrap").kind, "started");
  harness.environment.flushTasks();
  harness.environment.advanceActive(4);
  harness.environment.suspend();
  harness.environment.advanceActive(100);
  harness.environment.resume();
  harness.environment.recoverMainLoop(50);
  harness.environment.advanceActive(5);
  assert.equal(harness.host.snapshot().phase, "starting");
  harness.environment.advanceActive(1);
  assert.equal(harness.host.snapshot().phase, "closed");
  assert.equal(harness.failures[0]?.kind, "startup");

  const successfulBootstrap = deferred<void>();
  const successful = createHarness({
    bootstrap: () => successfulBootstrap.promise,
    startupBudgetMilliseconds: 10,
  });
  assert.equal(successful.host.start("bootstrap").kind, "started");
  successful.environment.flushTasks();
  successful.environment.advanceActive(9);
  successfulBootstrap.resolve(undefined);
  await successful.environment.flushAsync();
  assert.equal(successful.host.snapshot().phase, "ready");
});

test("illegal pre-Ready input closes immediately with exact classification", () => {
  const cases: readonly {
    readonly send: (harness: TestHarness) => void;
    readonly kind: WorkerRuntimeFailure<TestDiagnostic>["kind"];
  }[] = [
    {
      send: harness => {
        harness.workers[0]!.emitRaw({ malformed: true });
      },
      kind: "protocol",
    },
    {
      send: harness => {
        harness.workers[0]!.emitRaw(workerEnvelope(1, {
          kind: "heartbeat",
        }));
      },
      kind: "protocol",
    },
    {
      send: harness => {
        harness.workers[0]!.emitRaw(workerEnvelope(2, {
          kind: "heartbeat",
        }));
      },
      kind: "protocol",
    },
    {
      send: harness => {
        harness.workers[0]!.emitRaw(workerEnvelope(1, {
          kind: "probe-acknowledged",
          probeSequence: 1,
        }));
      },
      kind: "protocol",
    },
    {
      send: harness => {
        harness.workers[0]!.emitError("worker error");
      },
      kind: "worker-message",
    },
    {
      send: harness => {
        harness.workers[0]!.emitMessageError("clone error");
      },
      kind: "worker-message",
    },
  ];
  for (const candidate of cases) {
    const bootstrap = deferred<void>();
    const harness = createHarness({ bootstrap: () => bootstrap.promise });
    assert.equal(harness.host.start("bootstrap").kind, "started");
    harness.environment.flushTasks();
    candidate.send(harness);
    assert.equal(harness.host.snapshot().phase, "closed");
    assert.equal(harness.failures[0]?.kind, candidate.kind);
    assert.equal(harness.workers[0]!.terminateCount, 1);
  }
});

test("StartupFailed and mismatched Ready retain startup classification", () => {
  const bootstrap = deferred<void>();
  const failed = createHarness({ bootstrap: () => bootstrap.promise });
  assert.equal(failed.host.start("bootstrap").kind, "started");
  failed.environment.flushTasks();
  failed.workers[0]!.emitRaw(workerEnvelope(1, {
    kind: "startup-failed",
    diagnostic: { code: "bootstrap", detail: "failed" },
  }));
  assert.equal(failed.failures[0]?.kind, "startup");
  assert.equal(failed.host.snapshot().phase, "closed");

  const mismatchBootstrap = deferred<void>();
  const mismatch = createHarness({
    bootstrap: () => mismatchBootstrap.promise,
  });
  assert.equal(mismatch.host.start("bootstrap").kind, "started");
  mismatch.environment.flushTasks();
  mismatch.workers[0]!.emitRaw(workerEnvelope(2, {
    kind: "ready",
    idleHeartbeatIntervalMilliseconds: 10,
  }));
  assert.equal(mismatch.failures[0]?.kind, "startup");
  assert.equal(mismatch.host.snapshot().phase, "closed");

  const intervalBootstrap = deferred<void>();
  const intervalMismatch = createHarness({
    bootstrap: () => intervalBootstrap.promise,
  });
  assert.equal(intervalMismatch.host.start("bootstrap").kind, "started");
  intervalMismatch.environment.flushTasks();
  intervalMismatch.workers[0]!.emitRaw(workerEnvelope(1, {
    kind: "ready",
    idleHeartbeatIntervalMilliseconds: 11,
  }));
  assert.equal(intervalMismatch.failures[0]?.kind, "startup");
  assert.equal(intervalMismatch.host.snapshot().phase, "closed");

  const versionBootstrap = deferred<void>();
  const versionMismatch = createHarness({
    bootstrap: () => versionBootstrap.promise,
  });
  assert.equal(versionMismatch.host.start("bootstrap").kind, "started");
  versionMismatch.environment.flushTasks();
  versionMismatch.workers[0]!.emitRaw({
    protocolVersion: WORKER_RUNTIME_PROTOCOL_VERSION + 1,
    epochToken: 1,
    kind: "ready",
    idleHeartbeatIntervalMilliseconds: 10,
  });
  assert.equal(versionMismatch.failures[0]?.kind, "startup");
  assert.equal(versionMismatch.host.snapshot().phase, "closed");
});

test("post-readiness protocol faults drain within a bounded active-time budget", async () => {
  const settlement = deferred<TestSettlement>();
  const harness = createHarness({
    invoke: () => settlement.promise,
    drainBudgetMilliseconds: 5,
  });
  await startReady(harness);
  const operationSession = session(harness.adapter);
  const handle = started(
    operationSession.session.start("active", harness.adapter),
  );
  await harness.environment.flushAsync();
  harness.workers[0]!.emitRaw({ malformed: true });
  assert.equal(harness.host.snapshot().phase, "draining");
  assert.deepEqual(await handle.outcome, {
    kind: "failed",
    error: "boundary:protocol",
  });
  harness.environment.advanceActive(4);
  assert.equal(harness.workers[0]!.terminated, false);
  harness.environment.advanceActive(1);
  assert.equal(harness.host.snapshot().phase, "closed");
  assert.equal(harness.workers[0]!.terminated, true);
  await handle.quiesced;
});

test("post-readiness worker faults drain for natural release or the bounded fallback", async () => {
  const settlement = deferred<TestSettlement>();
  const natural = createHarness({
    invoke: () => settlement.promise,
    drainBudgetMilliseconds: 10,
  });
  await startReady(natural);
  const operationSession = session(natural.adapter);
  const handle = started(
    operationSession.session.start("active", natural.adapter),
  );
  await natural.environment.flushAsync();
  assert.equal(
    natural.workers[0]!.startEpochWork("speculative", 1),
    true,
  );
  natural.workers[0]!.emitError("worker event");
  assert.equal(natural.failures[0]?.kind, "worker-message");
  assert.equal(natural.host.snapshot().phase, "draining");
  assert.equal(natural.workers[0]!.terminated, false);
  assert.deepEqual(await handle.outcome, {
    kind: "failed",
    error: "boundary:worker-message",
  });
  natural.workers[0]!.emitRaw(workerEnvelope(1, {
    kind: "settled",
    operation: { operationId: handle.id, operationSequence: 1 },
    settlement: { kind: "succeeded", value: "released" },
  }));
  assert.equal(natural.host.snapshot().phase, "draining");
  assert.equal(natural.workers[0]!.finishEpochWork(1), true);
  assert.equal(natural.host.snapshot().phase, "closed");
  assert.equal(natural.environment.now(), 0);
  await handle.quiesced;

  const fallbackSettlement = deferred<TestSettlement>();
  const fallback = createHarness({
    invoke: () => fallbackSettlement.promise,
    drainBudgetMilliseconds: 5,
  });
  await startReady(fallback);
  const fallbackSession = session(fallback.adapter);
  const fallbackHandle = started(
    fallbackSession.session.start("active", fallback.adapter),
  );
  await fallback.environment.flushAsync();
  fallback.workers[0]!.emitMessageError("messageerror event");
  assert.equal(fallback.failures[0]?.kind, "worker-message");
  assert.equal(fallback.host.snapshot().phase, "draining");
  fallback.environment.advanceActive(4);
  assert.equal(fallback.workers[0]!.terminated, false);
  fallback.environment.advanceActive(1);
  assert.equal(fallback.host.snapshot().phase, "closed");
  assert.equal(fallback.workers[0]!.terminated, true);
  await fallbackHandle.quiesced;
});

test("worker admission consumes newer sequences before ID, kind, or payload validation", async () => {
  const environment = new ManualWorkerRuntimeEnvironment();
  const producerClasses = new WorkerProducerClassRegistry(10);
  producerClasses.register(
    "lease",
    { kind: "bounded", maxSilentActiveMilliseconds: 20 },
    20,
  );
  const settlement = deferred<TestSettlement>();
  const operations = new FakeWorkerOperationCatalog();
  operations.register({
    kind: "echo",
    allowance: { kind: "bounded", maxSilentActiveMilliseconds: 20 },
    input: stringDecoder(),
    rejectInvalidPayload: detail => ({
      error: "invalid-payload",
      diagnostic: { code: "invalid-payload", detail },
    }),
    invoke: () => settlement.promise,
  });
  const worker = new FakeWorkerRuntime({
    scheduler: environment,
    bootstrap: {
      decoder: stringDecoder(),
      bootstrap: () => undefined,
    },
    diagnostic: detail => ({ code: "worker", detail }),
    unknownOperationRejection: kind => ({
      error: "unknown-operation-kind",
      diagnostic: { code: "unknown-operation-kind", detail: kind },
    }),
    operations,
    producerClasses,
  });
  const emittedKinds: string[] = [];
  worker.bind({
    message: (_source, data) => {
      if (typeof data !== "object" || data === null) return;
      const kind = Object.getOwnPropertyDescriptor(data, "kind");
      if (kind !== undefined
        && "value" in kind
        && typeof kind.value === "string") {
        emittedKinds.push(kind.value);
      }
    },
    error: () => undefined,
    messageError: () => undefined,
  });
  postWorker(worker, {
    protocolVersion: WORKER_RUNTIME_PROTOCOL_VERSION,
    epochToken: 1,
    kind: "initialize",
    bootstrap: "bootstrap",
    idleHeartbeatIntervalMilliseconds: 10,
    idleAllowanceMilliseconds: 10,
  });
  await environment.flushAsync();
  postWorker(worker, {
    protocolVersion: WORKER_RUNTIME_PROTOCOL_VERSION,
    epochToken: 1,
    kind: "start",
    operation: { operationId: "active", operationSequence: 1 },
    operationKind: "echo",
    payload: "first",
  });
  await environment.flushAsync();
  postWorker(worker, {
    protocolVersion: WORKER_RUNTIME_PROTOCOL_VERSION,
    epochToken: 1,
    kind: "start",
    operation: { operationId: "unknown", operationSequence: 3 },
    operationKind: "missing",
    payload: "third",
  });
  await environment.flushAsync();
  postWorker(worker, {
    protocolVersion: WORKER_RUNTIME_PROTOCOL_VERSION,
    epochToken: 1,
    kind: "start",
    operation: { operationId: "active", operationSequence: 4 },
    operationKind: "echo",
    payload: "duplicate",
  });
  await environment.flushAsync();
  assert.deepEqual(
    emittedKinds,
    ["ready", "accepted", "rejected", "epoch-failed"],
  );
});

test("invalid payload is Rejected without Accepted and a legal sequence gap remains usable", async () => {
  const environment = new ManualWorkerRuntimeEnvironment();
  const producerClasses = new WorkerProducerClassRegistry(10);
  const settlement = deferred<TestSettlement>();
  const operations = new FakeWorkerOperationCatalog();
  operations.register({
    kind: "echo",
    allowance: { kind: "bounded", maxSilentActiveMilliseconds: 20 },
    input: stringDecoder(),
    rejectInvalidPayload: detail => ({
      error: "invalid-payload",
      diagnostic: { code: "invalid-payload", detail },
    }),
    invoke: () => settlement.promise,
  });
  const worker = new FakeWorkerRuntime({
    scheduler: environment,
    bootstrap: {
      decoder: stringDecoder(),
      bootstrap: () => undefined,
    },
    diagnostic: detail => ({ code: "worker", detail }),
    unknownOperationRejection: kind => ({
      error: "unknown-operation-kind",
      diagnostic: { code: "unknown-operation-kind", detail: kind },
    }),
    operations,
    producerClasses,
  });
  const emittedKinds: string[] = [];
  worker.bind({
    message: (_source, data) => {
      if (typeof data !== "object" || data === null) return;
      const kind = ownDataProperty(data, "kind");
      if (typeof kind === "string") emittedKinds.push(kind);
    },
    error: () => undefined,
    messageError: () => undefined,
  });
  postWorker(worker, {
    protocolVersion: WORKER_RUNTIME_PROTOCOL_VERSION,
    epochToken: 1,
    kind: "initialize",
    bootstrap: "bootstrap",
    idleHeartbeatIntervalMilliseconds: 10,
    idleAllowanceMilliseconds: 10,
  });
  await environment.flushAsync();
  postWorker(worker, {
    protocolVersion: WORKER_RUNTIME_PROTOCOL_VERSION,
    epochToken: 1,
    kind: "start",
    operation: { operationId: "invalid", operationSequence: 2 },
    operationKind: "echo",
    payload: 42,
  });
  await environment.flushAsync();
  postWorker(worker, {
    protocolVersion: WORKER_RUNTIME_PROTOCOL_VERSION,
    epochToken: 1,
    kind: "start",
    operation: { operationId: "valid", operationSequence: 4 },
    operationKind: "echo",
    payload: "valid",
  });
  await environment.flushAsync();
  assert.deepEqual(emittedKinds, ["ready", "rejected", "accepted"]);
  assert.equal(worker.activeOperationCount, 1);
});

test("host operation high-water permits gaps, rejects replay after release, and exposes exhaustion", async () => {
  const harness = createHarness({ maximumOperationSequence: 3 });
  await startReady(harness);
  const authority = createOperationAuthorityPage({
    allocation: {
      createId: (() => {
        let id = 1;
        return () => `sequence-${id++}`;
      })(),
    },
  });
  const first = session(harness.adapter, authority);
  const firstHandle = started(
    first.session.start("first", harness.adapter),
  );
  await harness.environment.flushAsync();
  await firstHandle.quiesced;
  const firstEvent = first.events.find(event => event.kind === "started");
  if (firstEvent?.kind !== "started")
    throw new Error("First operation identity was not published.");
  const sink: OperationProducerSink<string, string, string> = {
    reportProgress: () => undefined,
    ...terminalCallbacks(() => undefined),
    reportUnexpectedTerminal: () => undefined,
    reportQuiesced: () => undefined,
    reportUnexpectedFailure: () => undefined,
  };
  assert.deepEqual(
    harness.adapter.prepare(firstEvent.operation, "replay", sink),
    {
      kind: "rejected",
      error: { kind: "operation-sequence-replayed" },
    },
  );

  const gapSession = authority.createSession<
    string,
    string,
    string,
    string,
    string
  >({
    feature: { publish: () => undefined },
    diagnostic: { report: () => undefined },
  });
  gapSession.start("gap", {
    prepare: () => ({ kind: "rejected", error: "intentional-gap" }),
  });

  const third = session(harness.adapter, authority);
  const thirdHandle = started(
    third.session.start("third", harness.adapter),
  );
  await harness.environment.flushAsync();
  assert.deepEqual(await thirdHandle.outcome, {
    kind: "succeeded",
    value: "third",
  });

  assert.deepEqual(
    harness.adapter.prepare(firstEvent.operation, "replay", sink),
    {
      kind: "rejected",
      error: { kind: "operation-sequence-exhausted" },
    },
  );
  const fourth = session(harness.adapter, authority);
  const exhausted = fourth.session.start("fourth", harness.adapter);
  assert.equal(exhausted.kind, "rejected");
  if (exhausted.kind === "rejected") {
    assert.deepEqual(exhausted.reason, {
      kind: "producer-rejected",
      error: { kind: "operation-sequence-exhausted" },
    });
  }
});

test("valid Rejected reports terminal failure and quiescence together", async () => {
  const settlement = deferred<TestSettlement>();
  const harness = createHarness({
    invoke: () => settlement.promise,
    omitResponse: kind => kind === "accepted",
  });
  await startReady(harness);
  const operationSession = session(harness.adapter);
  const handle = started(
    operationSession.session.start("input", harness.adapter),
  );
  await harness.environment.flushAsync();
  harness.workers[0]!.emitRaw(workerEnvelope(1, {
    kind: "rejected",
    operation: { operationId: handle.id, operationSequence: 1 },
    error: "invalid-input",
    diagnostic: { code: "invalid-input", detail: "rejected" },
  }));
  assert.deepEqual(await handle.outcome, {
    kind: "failed",
    error: "invalid-input",
  });
  await handle.quiesced;
  assert.equal(harness.host.snapshot().activeOperations, 0);
});

test("heterogeneous operation registrations retain narrow adapters and per-kind codecs", async () => {
  type TextInput = {
    readonly text: string;
    readonly mode: "success" | "failure" | "hold";
  };
  interface TextValue {
    readonly upper: string;
  }
  interface TextError {
    readonly textError: string;
  }
  interface TextDiagnostic {
    readonly textDiagnostic: string;
  }
  interface TextProgress {
    readonly textProgress: number;
  }
  interface TextPreparationError {
    readonly textPreparation: WorkerRuntimePreparationError;
  }

  type CountInput = {
    readonly count: number;
    readonly mode: "success" | "failure" | "hold";
  };
  interface CountValue {
    readonly doubled: number;
  }
  interface CountError {
    readonly countError: number;
  }
  interface CountDiagnostic {
    readonly countDiagnostic: number;
  }
  interface CountProgress {
    readonly countProgress: string;
  }
  interface CountPreparationError {
    readonly countPreparation: WorkerRuntimePreparationError;
  }

  const textInput = recordDecoder<TextInput>(value => {
    const text = ownDataProperty(value, "text");
    const mode = ownDataProperty(value, "mode");
    return typeof text === "string"
      && (mode === "success" || mode === "failure" || mode === "hold")
      ? { text, mode }
      : null;
  }, "Expected text input.");
  const textValue = recordDecoder<TextValue>(value => {
    const upper = ownDataProperty(value, "upper");
    return typeof upper === "string" ? { upper } : null;
  }, "Expected text value.");
  const textError = recordDecoder<TextError>(value => {
    const error = ownDataProperty(value, "textError");
    return typeof error === "string" ? { textError: error } : null;
  }, "Expected text error.");
  const textDiagnostic = recordDecoder<TextDiagnostic>(value => {
    const diagnostic = ownDataProperty(value, "textDiagnostic");
    return typeof diagnostic === "string"
      ? { textDiagnostic: diagnostic }
      : null;
  }, "Expected text diagnostic.");
  const textProgress = recordDecoder<TextProgress>(value => {
    const progress = ownDataProperty(value, "textProgress");
    return typeof progress === "number"
      ? { textProgress: progress }
      : null;
  }, "Expected text progress.");

  const countInput = recordDecoder<CountInput>(value => {
    const count = ownDataProperty(value, "count");
    const mode = ownDataProperty(value, "mode");
    return typeof count === "number"
      && (mode === "success" || mode === "failure" || mode === "hold")
      ? { count, mode }
      : null;
  }, "Expected count input.");
  const countValue = recordDecoder<CountValue>(value => {
    const doubled = ownDataProperty(value, "doubled");
    return typeof doubled === "number" ? { doubled } : null;
  }, "Expected count value.");
  const countError = recordDecoder<CountError>(value => {
    const error = ownDataProperty(value, "countError");
    return typeof error === "number" ? { countError: error } : null;
  }, "Expected count error.");
  const countDiagnostic = recordDecoder<CountDiagnostic>(value => {
    const diagnostic = ownDataProperty(value, "countDiagnostic");
    return typeof diagnostic === "number"
      ? { countDiagnostic: diagnostic }
      : null;
  }, "Expected count diagnostic.");
  const countProgress = recordDecoder<CountProgress>(value => {
    const progress = ownDataProperty(value, "countProgress");
    return typeof progress === "string"
      ? { countProgress: progress }
      : null;
  }, "Expected count progress.");

  const textHeld = deferred<
    ManagedOperationSettlement<TextValue, TextError, TextDiagnostic>
  >();
  const countHeld = deferred<
    ManagedOperationSettlement<CountValue, CountError, CountDiagnostic>
  >();
  const operations = new FakeWorkerOperationCatalog();
  operations.register<TextInput, TextValue, TextError, TextDiagnostic>({
    kind: "text",
    allowance: { kind: "bounded", maxSilentActiveMilliseconds: 20 },
    input: textInput,
    rejectInvalidPayload: failure => ({
      error: { textError: "invalid-payload" },
      diagnostic: { textDiagnostic: failure.code },
    }),
    invoke: input => {
      if (input.mode === "hold") return textHeld.promise;
      if (input.mode === "failure") {
        return {
          kind: "failed",
          failureKind: "unexpected",
          error: { textError: "text-failed" },
          diagnostic: { textDiagnostic: "text-diagnostic" },
        };
      }
      return {
        kind: "succeeded",
        value: { upper: input.text.toUpperCase() },
      };
    },
  });
  operations.register<CountInput, CountValue, CountError, CountDiagnostic>({
    kind: "count",
    allowance: { kind: "bounded", maxSilentActiveMilliseconds: 20 },
    input: countInput,
    rejectInvalidPayload: failure => ({
      error: { countError: -1 },
      diagnostic: { countDiagnostic: failure.path.length },
    }),
    invoke: input => {
      if (input.mode === "hold") return countHeld.promise;
      if (input.mode === "failure") {
        return {
          kind: "failed",
          failureKind: "unexpected",
          error: { countError: 17 },
          diagnostic: { countDiagnostic: 23 },
        };
      }
      return {
        kind: "succeeded",
        value: { doubled: input.count * 2 },
      };
    },
  });

  const environment = new ManualWorkerRuntimeEnvironment();
  const hostProducerClasses = new WorkerProducerClassRegistry(10);
  const workerProducerClasses = new WorkerProducerClassRegistry(10);
  assert.notEqual(hostProducerClasses, workerProducerClasses);
  const worker = new FakeWorkerRuntime({
    scheduler: environment,
    bootstrap: {
      decoder: stringDecoder(),
      bootstrap: () => undefined,
    },
    diagnostic: detail => ({ code: "worker", detail }),
    unknownOperationRejection: kind => ({
      error: { unknownKind: kind },
      diagnostic: { unknownOperation: kind },
    }),
    operations,
    producerClasses: workerProducerClasses,
  });
  const failures: WorkerRuntimeFailure<TestDiagnostic>[] = [];
  const host = new WorkerRuntimeHost<string, TestDiagnostic>({
    transport: new QueueWorkerRuntimeTransportFactory([worker]),
    clock: environment,
    lifecycle: environment,
    bootstrap: {
      encode: bootstrap => ({ kind: "decoded", value: bootstrap }),
      diagnostic: diagnosticDecoder(),
    },
    diagnostic: diagnosticDecoder(),
    callbacks: {
      failure: failure => {
        failures.push(failure);
        return undefined;
      },
      diagnostic: () => undefined,
      realmReleased: () => undefined,
    },
    createDiagnostic: (kind, detail) => ({ code: kind, detail }),
    idleHeartbeatIntervalMilliseconds: 10,
    startupBudgetMilliseconds: 100,
    controlResponseGraceMilliseconds: 10,
    drainBudgetMilliseconds: 5,
    producerClasses: hostProducerClasses,
  });
  const textAdapter = host.registerOperation({
    kind: "text",
    allowance: { kind: "bounded", maxSilentActiveMilliseconds: 20 },
    encodeInput: input => ({ kind: "decoded", value: input }),
    value: textValue,
    error: textError,
    diagnostic: textDiagnostic,
    progress: textProgress,
    mapPreparationError: error => ({ textPreparation: error }),
    boundaryErrors: boundaryErrors(kind => ({ textError: kind })),
  });
  const countAdapter = host.registerOperation({
    kind: "count",
    allowance: { kind: "bounded", maxSilentActiveMilliseconds: 20 },
    encodeInput: input => ({ kind: "decoded", value: input }),
    value: countValue,
    error: countError,
    diagnostic: countDiagnostic,
    progress: countProgress,
    mapPreparationError: error => ({ countPreparation: error }),
    boundaryErrors: boundaryErrors(kind => ({ countError: kind.length })),
  });
  const narrowTextAdapter: OperationProducerAdapter<
    TextInput,
    TextValue,
    TextError,
    TextProgress,
    TextPreparationError
  > = textAdapter;
  const narrowCountAdapter: OperationProducerAdapter<
    CountInput,
    CountValue,
    CountError,
    CountProgress,
    CountPreparationError
  > = countAdapter;
  // @ts-expect-error Heterogeneous adapters must not widen to another kind.
  const wrongAdapter: typeof narrowCountAdapter = narrowTextAdapter;
  void wrongAdapter;

  assert.equal(host.start("bootstrap").kind, "started");
  await environment.flushAsync();
  const page = createOperationAuthorityPage({
    allocation: {
      createId: (() => {
        let id = 1;
        return () => `heterogeneous-${id++}`;
      })(),
    },
  });
  const textEvents: OperationFeatureEvent<
    TextValue,
    TextError,
    TextProgress
  >[] = [];
  const textDiagnostics: unknown[] = [];
  const textSession = page.createSession<
    TextInput,
    TextValue,
    TextError,
    TextProgress,
    TextPreparationError
  >({
    feature: {
      publish: event => {
        textEvents.push(event);
        return undefined;
      },
    },
    diagnostic: {
      report: diagnostic => {
        textDiagnostics.push(diagnostic.error);
        return undefined;
      },
    },
  });
  const countEvents: OperationFeatureEvent<
    CountValue,
    CountError,
    CountProgress
  >[] = [];
  const countDiagnostics: unknown[] = [];
  const countSession = page.createSession<
    CountInput,
    CountValue,
    CountError,
    CountProgress,
    CountPreparationError
  >({
    feature: {
      publish: event => {
        countEvents.push(event);
        return undefined;
      },
    },
    diagnostic: {
      report: diagnostic => {
        countDiagnostics.push(diagnostic.error);
        return undefined;
      },
    },
  });

  const textSuccess = started(textSession.start(
    { text: "mixed", mode: "success" },
    textAdapter,
  ));
  const countSuccess = started(countSession.start(
    { count: 21, mode: "success" },
    countAdapter,
  ));
  await environment.flushAsync();
  assert.deepEqual(await textSuccess.outcome, {
    kind: "succeeded",
    value: { upper: "MIXED" },
  });
  assert.deepEqual(await countSuccess.outcome, {
    kind: "succeeded",
    value: { doubled: 42 },
  });

  const textFailure = started(textSession.start(
    { text: "failure", mode: "failure" },
    textAdapter,
  ));
  const countFailure = started(countSession.start(
    { count: 1, mode: "failure" },
    countAdapter,
  ));
  await environment.flushAsync();
  assert.deepEqual(await textFailure.outcome, {
    kind: "failed",
    error: { textError: "text-failed" },
  });
  assert.deepEqual(await countFailure.outcome, {
    kind: "failed",
    error: { countError: 17 },
  });
  assert.deepEqual(textDiagnostics, [{
    textDiagnostic: "text-diagnostic",
  }]);
  assert.deepEqual(countDiagnostics, [{ countDiagnostic: 23 }]);

  const textLive = started(textSession.start(
    { text: "hold", mode: "hold" },
    textAdapter,
  ));
  const countLive = started(countSession.start(
    { count: 2, mode: "hold" },
    countAdapter,
  ));
  await environment.flushAsync();
  worker.emitRaw(workerEnvelope(1, {
    kind: "progress",
    operation: { operationId: textLive.id, operationSequence: 5 },
    payload: { textProgress: 5 },
  }));
  worker.emitRaw(workerEnvelope(1, {
    kind: "progress",
    operation: { operationId: countLive.id, operationSequence: 6 },
    payload: { countProgress: "six" },
  }));
  assert.deepEqual(
    textEvents.filter(event => event.kind === "progress").at(-1),
    {
      kind: "progress",
      progress: {
        operationId: textLive.id,
        value: { textProgress: 5 },
      },
    },
  );
  assert.deepEqual(
    countEvents.filter(event => event.kind === "progress").at(-1),
    {
      kind: "progress",
      progress: {
        operationId: countLive.id,
        value: { countProgress: "six" },
      },
    },
  );

  worker.emitRaw(workerEnvelope(1, {
    kind: "progress",
    operation: { operationId: "absent", operationSequence: 99 },
    payload: { unrelated: true },
  }));
  assert.equal(failures[0]?.kind, "protocol");
  assert.deepEqual(await textLive.outcome, {
    kind: "failed",
    error: { textError: "protocol" },
  });
  assert.deepEqual(await countLive.outcome, {
    kind: "failed",
    error: { countError: "protocol".length },
  });
  environment.advanceActive(5);
  await Promise.all([textLive.quiesced, countLive.quiesced]);
});

test("main receive validation fails closed for every invalid operation ordering", async () => {
  const invalidMessages: readonly (
    (token: number, operationId: string) => unknown
  )[] = [
    (token, operationId) => workerEnvelope(token, {
      kind: "accepted",
      operation: { operationId, operationSequence: 1 },
      allowance: { kind: "bounded", maxSilentActiveMilliseconds: 20 },
    }),
    (token, operationId) => workerEnvelope(token, {
      kind: "rejected",
      operation: { operationId, operationSequence: 1 },
      error: "late",
      diagnostic: { code: "late", detail: null },
    }),
    (token, operationId) => workerEnvelope(token, {
      kind: "progress",
      operation: { operationId, operationSequence: 1 },
      payload: "early",
    }),
    (token, operationId) => workerEnvelope(token, {
      kind: "settled",
      operation: { operationId, operationSequence: 1 },
      settlement: { kind: "succeeded", value: "duplicate" },
    }),
    token => workerEnvelope(token, {
      kind: "progress",
      operation: { operationId: "absent", operationSequence: 99 },
      payload: "absent",
    }),
  ];

  for (let index = 0; index < invalidMessages.length; index++) {
    const settlement = deferred<TestSettlement>();
    const harness = createHarness({ invoke: () => settlement.promise });
    await startReady(harness);
    const operationSession = session(harness.adapter);
    started(operationSession.session.start("input", harness.adapter));
    await harness.environment.flushAsync();
    const operationId = operationSession.events.find(
      event => event.kind === "started",
    );
    assert.equal(operationId?.kind, "started");
    if (operationId?.kind !== "started")
      throw new Error("Started event missing.");

    if (index === 0) {
      harness.workers[0]!.emitRaw(invalidMessages[index]!(
        1,
        operationId.operation.id,
      ));
    } else if (index === 1) {
      harness.workers[0]!.emitRaw(invalidMessages[index]!(
        1,
        operationId.operation.id,
      ));
    } else if (index === 2) {
      const pendingHarness = createHarness({
        omitResponse: kind => kind === "accepted",
        invoke: () => settlement.promise,
      });
      await startReady(pendingHarness);
      const pendingSession = session(pendingHarness.adapter);
      const pendingHandle = started(
        pendingSession.session.start("input", pendingHarness.adapter),
      );
      await pendingHarness.environment.flushAsync();
      const pendingId = pendingHandle.id;
      pendingHarness.workers[0]!.emitRaw(invalidMessages[index]!(1, pendingId));
      assert.equal(pendingHarness.failures[0]?.kind, "protocol");
      continue;
    } else if (index === 3) {
      harness.workers[0]!.emitRaw(workerEnvelope(1, {
        kind: "settled",
        operation: {
          operationId: operationId.operation.id,
          operationSequence: 1,
        },
        settlement: { kind: "succeeded", value: "first" },
      }));
      harness.workers[0]!.emitRaw(invalidMessages[index]!(
        1,
        operationId.operation.id,
      ));
    } else {
      harness.workers[0]!.emitRaw(invalidMessages[index]!(
        1,
        operationId.operation.id,
      ));
    }
    assert.equal(harness.failures[0]?.kind, "protocol");
  }
});

test("allowance mismatch fails instead of silently narrowing liveness", async () => {
  const settlement = deferred<TestSettlement>();
  const harness = createHarness({
    invoke: () => settlement.promise,
    omitResponse: kind => kind === "accepted",
  });
  await startReady(harness);
  const operationSession = session(harness.adapter);
  const handle = started(
    operationSession.session.start("input", harness.adapter),
  );
  await harness.environment.flushAsync();
  harness.workers[0]!.emitRaw(workerEnvelope(1, {
    kind: "accepted",
    operation: { operationId: handle.id, operationSequence: 1 },
    allowance: { kind: "bounded", maxSilentActiveMilliseconds: 19 },
  }));
  assert.equal(harness.failures[0]?.kind, "protocol");
  assert.equal(harness.host.snapshot().phase, "draining");
  harness.workers[0]!.emitRaw(workerEnvelope(1, {
    kind: "settled",
    operation: { operationId: handle.id, operationSequence: 1 },
    settlement: { kind: "succeeded", value: "late" },
  }));
  assert.equal(harness.host.snapshot().phase, "closed");
  assert.equal(harness.workers[0]!.terminateCount, 1);
  assert.deepEqual(harness.releasedEpochs, [1]);
  assert.deepEqual(await handle.outcome, {
    kind: "failed",
    error: "boundary:protocol",
  });
  await handle.quiesced;
});

test("Settled maps unexpected diagnostic, terminal, then quiescence atomically", async () => {
  const settlement = deferred<TestSettlement>();
  const harness = createHarness({ invoke: () => settlement.promise });
  await startReady(harness);
  const identity = captureIdentity();
  const calls: string[] = [];
  const sink: OperationProducerSink<string, string, string> = {
    reportProgress: value => {
      calls.push(`progress:${value}`);
      return undefined;
    },
    reportUnexpectedTerminal: (error, diagnostic) => {
      const code = typeof diagnostic === "object" && diagnostic !== null
        ? ownDataProperty(diagnostic, "code")
        : "unknown";
      calls.push(`unexpected-terminal:${String(code)}:${error}`);
      return undefined;
    },
    reportUnexpectedFailure: diagnostic => {
      const code = typeof diagnostic === "object" && diagnostic !== null
        ? ownDataProperty(diagnostic, "code")
        : "unknown";
      calls.push(`unexpected:${String(code)}`);
      return undefined;
    },
    ...terminalCallbacks(outcome => {
      calls.push(
        outcome.kind === "failed"
          ? `terminal:${outcome.error}`
          : `terminal:${outcome.kind}`,
      );
      return undefined;
    }),
    reportQuiesced: () => {
      calls.push("quiesced");
      return undefined;
    },
  };
  preparedBinding(harness.adapter.prepare(identity, "input", sink)).activate();
  await harness.environment.flushAsync();
  settlement.resolve({
    kind: "failed",
    failureKind: "unexpected",
    error: "feature-error",
    diagnostic: { code: "unexpected-producer", detail: "detail" },
  });
  await harness.environment.flushAsync();
  assert.deepEqual(calls, [
    "unexpected-terminal:unexpected-producer:feature-error",
    "quiesced",
  ]);
});

test("unexpected Settled commits failure before diagnostic reentrancy", async () => {
  const settlement = deferred<TestSettlement>();
  const harness = createHarness({ invoke: () => settlement.promise });
  await startReady(harness);
  let handle: OperationHandle<string, string> | null = null;
  let cancelResult: ReturnType<OperationHandle<string, string>["cancel"]>
    | null = null;
  const page = createOperationAuthorityPage({
    allocation: { createId: () => "unexpected-operation" },
  });
  const operationSession = page.createSession<
    string,
    string,
    string,
    string,
    WorkerRuntimePreparationError
  >({
    feature: { publish: () => undefined },
    diagnostic: {
      report: () => {
        cancelResult = handle?.cancel("user") ?? null;
        return undefined;
      },
    },
  });
  handle = started(operationSession.start("input", harness.adapter));
  await harness.environment.flushAsync();

  settlement.resolve({
    kind: "failed",
    failureKind: "unexpected",
    error: "feature-error",
    diagnostic: { code: "unexpected-producer", detail: "detail" },
  });
  await harness.environment.flushAsync();

  assert.deepEqual(cancelResult, { kind: "no-op" });
  assert.deepEqual(await handle.outcome, {
    kind: "failed",
    error: "feature-error",
  });
  await handle.quiesced;
});

test("managed Promise rejection is an epoch boundary failure, not a feature result", async () => {
  const invocation = deferred<TestSettlement>();
  const harness = createHarness({ invoke: () => invocation.promise });
  await startReady(harness);
  const operationSession = session(harness.adapter);
  const handle = started(
    operationSession.session.start("input", harness.adapter),
  );
  await harness.environment.flushAsync();
  invocation.reject(new Error("managed promise rejected"));
  await harness.environment.flushAsync();
  assert.equal(harness.failures[0]?.kind, "worker-declared");
  assert.deepEqual(await handle.outcome, {
    kind: "failed",
    error: "boundary:worker-declared",
  });
});

test("running cancellation posts once after Start and waits for physical closure and acknowledgment", async () => {
  const settlement = deferred<TestSettlement>();
  const cancellation = deferred<boolean>();
  const harness = createHarness({
    invoke: () => settlement.promise,
    cancel: () => cancellation.promise,
  });
  await startReady(harness);
  const operationSession = session(harness.adapter);
  const handle = started(
    operationSession.session.start("input", harness.adapter),
  );
  await harness.environment.flushAsync();
  assert.deepEqual(handle.cancel("user"), { kind: "applied" });
  assert.deepEqual(handle.cancel("user"), { kind: "no-op" });
  await harness.environment.flushAsync();
  assert.deepEqual(operationMessages(harness.workers[0]!), [
    "initialize",
    "start",
    "cancel",
  ]);
  settlement.resolve({
    kind: "canceled",
    reason: "user",
  });
  await harness.environment.flushAsync();
  assert.equal(harness.host.snapshot().compactControlRecords, 1);
  cancellation.resolve(true);
  await harness.environment.flushAsync();
  await harness.environment.flushAsync();
  assert.equal(harness.host.snapshot().compactControlRecords, 0);
  await handle.quiesced;
});

test("not-active acknowledgment may follow settlement and retires the compact record", async () => {
  const settlement = deferred<TestSettlement>();
  const cancellation = deferred<boolean>();
  const harness = createHarness({
    invoke: () => settlement.promise,
    cancel: () => cancellation.promise,
  });
  await startReady(harness);
  const operationSession = session(harness.adapter);
  const handle = started(
    operationSession.session.start("input", harness.adapter),
  );
  await harness.environment.flushAsync();
  handle.cancel("user");
  await harness.environment.flushAsync();
  settlement.resolve({ kind: "succeeded", value: "raced" });
  await harness.environment.flushAsync();
  assert.equal(harness.host.snapshot().compactControlRecords, 1);
  cancellation.resolve(false);
  await harness.environment.flushAsync();
  assert.equal(harness.failures.length, 0);
  assert.equal(harness.host.snapshot().compactControlRecords, 0);
  await handle.quiesced;
});

test("cancellation acknowledgment cannot precede admission and worker rejects future cancellation", async () => {
  const settlement = deferred<TestSettlement>();
  const harness = createHarness({
    invoke: () => settlement.promise,
    omitResponse: kind => kind === "accepted",
  });
  await startReady(harness);
  const operationSession = session(harness.adapter);
  const handle = started(
    operationSession.session.start("input", harness.adapter),
  );
  handle.cancel("user");
  await harness.environment.flushAsync();
  harness.workers[0]!.emitRaw(workerEnvelope(1, {
    kind: "cancel-acknowledged",
    operation: { operationId: handle.id, operationSequence: 1 },
    status: "not-active",
  }));
  assert.equal(harness.failures[0]?.kind, "protocol");

  const future = createHarness({ invoke: () => settlement.promise });
  await startReady(future);
  postWorker(future.workers[0]!, {
    protocolVersion: WORKER_RUNTIME_PROTOCOL_VERSION,
    epochToken: 1,
    kind: "cancel",
    operation: { operationId: "future", operationSequence: 2 },
    reason: "user",
  });
  await future.environment.flushAsync();
  assert.equal(
    future.workers[0]!.emittedMessages.at(-1)?.kind,
    "epoch-failed",
  );
});

test("serialized cancellation cannot be overtaken by a later Probe", async () => {
  const settlement = deferred<TestSettlement>();
  const cancellation = deferred<boolean>();
  const harness = createHarness({
    invoke: () => settlement.promise,
    cancel: () => cancellation.promise,
    controlResponseGraceMilliseconds: 5,
  });
  await startReady(harness);
  const operationSession = session(harness.adapter);
  const handle = started(
    operationSession.session.start("input", harness.adapter),
  );
  await harness.environment.flushAsync();
  handle.cancel("user");
  await harness.environment.flushAsync();
  harness.environment.advanceActive(5);
  assert.deepEqual(operationMessages(harness.workers[0]!), [
    "initialize",
    "start",
    "cancel",
    "probe",
  ]);
  assert.equal(
    harness.workers[0]!.emittedMessages.some(
      envelope => envelope.kind === "probe-acknowledged",
    ),
    false,
  );
  cancellation.resolve(true);
  await harness.environment.flushAsync();
  const responses = harness.workers[0]!.emittedMessages
    .map(envelope => envelope.kind)
    .filter(kind =>
      kind === "cancel-acknowledged" || kind === "probe-acknowledged");
  assert.deepEqual(responses, [
    "cancel-acknowledged",
    "probe-acknowledged",
  ]);
});

test("matching probe acknowledgment proves a covered missing Start response", async () => {
  const settlement = deferred<TestSettlement>();
  const harness = createHarness({
    invoke: () => settlement.promise,
    omitResponse: kind => kind === "accepted",
    controlResponseGraceMilliseconds: 5,
  });
  await startReady(harness);
  const operationSession = session(harness.adapter);
  const handle = started(
    operationSession.session.start("input", harness.adapter),
  );
  await harness.environment.flushAsync();
  harness.environment.advanceActive(5);
  await harness.environment.flushAsync();
  assert.equal(harness.failures[0]?.kind, "control-response");
  assert.deepEqual(await handle.outcome, {
    kind: "failed",
    error: "boundary:control-response",
  });
});

test("matching probe acknowledgment proves a covered missing Cancel response", async () => {
  const settlement = deferred<TestSettlement>();
  const harness = createHarness({
    invoke: () => settlement.promise,
    cancel: () => true,
    omitResponse: kind => kind === "cancel-acknowledged",
    controlResponseGraceMilliseconds: 5,
  });
  await startReady(harness);
  const operationSession = session(harness.adapter);
  const handle = started(
    operationSession.session.start("input", harness.adapter),
  );
  await harness.environment.flushAsync();
  handle.cancel("user");
  await harness.environment.flushAsync();
  harness.environment.advanceActive(5);
  await harness.environment.flushAsync();
  assert.equal(harness.failures[0]?.kind, "control-response");
  assert.deepEqual(await handle.outcome, {
    kind: "canceled",
    reason: "user",
  });
});

test("a later serialized response proves a missing ProbeAcknowledged while heartbeat does not", async () => {
  const settlements = [
    deferred<TestSettlement>(),
    deferred<TestSettlement>(),
  ];
  let invocation = 0;
  const harness = createHarness({
    invoke: () => settlements[invocation++]!.promise,
    omitResponse: kind => kind === "probe-acknowledged",
    controlResponseGraceMilliseconds: 5,
  });
  await startReady(harness);
  const authority = createOperationAuthorityPage({
    allocation: {
      createId: (() => {
        let id = 1;
        return () => `probe-${id++}`;
      })(),
    },
  });
  const first = session(harness.adapter, authority);
  started(first.session.start("first", harness.adapter));
  await harness.environment.flushAsync();
  harness.environment.advanceActive(20);
  await harness.environment.flushAsync();
  assert.equal(harness.host.snapshot().outstandingProbeSequence, 1);
  harness.workers[0]!.emitHeartbeat();
  assert.equal(harness.host.snapshot().outstandingProbeSequence, 1);
  assert.equal(harness.failures.length, 0);

  const second = session(harness.adapter, authority);
  started(second.session.start("second", harness.adapter));
  await harness.environment.flushAsync();
  assert.equal(harness.failures[0]?.kind, "control-response");
});

test("deferred control coverage dispatches after an older probe retires", async () => {
  const settlement = deferred<TestSettlement>();
  let omitFirstProbe = true;
  const harness = createHarness({
    invoke: () => settlement.promise,
    omitResponse: kind => {
      if (kind === "accepted") return true;
      if (kind === "probe-acknowledged" && omitFirstProbe) {
        omitFirstProbe = false;
        return true;
      }
      return false;
    },
    controlResponseGraceMilliseconds: 5,
  });
  await startReady(harness);
  harness.environment.advanceActive(10);
  await harness.environment.flushAsync();
  assert.equal(harness.host.snapshot().outstandingProbeSequence, 1);

  const operationSession = session(harness.adapter);
  started(operationSession.session.start("input", harness.adapter));
  await harness.environment.flushAsync();
  harness.environment.advanceActive(5);
  assert.equal(harness.host.snapshot().deferredControlProbe, true);
  harness.workers[0]!.emitRaw(workerEnvelope(1, {
    kind: "probe-acknowledged",
    probeSequence: 1,
  }));
  await harness.environment.flushAsync();
  assert.equal(
    operationMessages(harness.workers[0]!).filter(kind => kind === "probe")
      .length,
    2,
  );
  assert.equal(harness.failures[0]?.kind, "control-response");
});

test("per-command probe marks are discharged exactly and cannot be overwritten by a later command", async () => {
  const settlements = [
    deferred<TestSettlement>(),
    deferred<TestSettlement>(),
  ];
  let invocation = 0;
  const harness = createHarness({
    invoke: () => settlements[invocation++]!.promise,
    omitResponse: kind =>
      kind === "accepted" || kind === "probe-acknowledged",
    controlResponseGraceMilliseconds: 5,
  });
  await startReady(harness);
  const authority = createOperationAuthorityPage({
    allocation: {
      createId: (() => {
        let id = 1;
        return () => `mark-${id++}`;
      })(),
    },
  });
  harness.environment.advanceActive(10);
  await harness.environment.flushAsync();
  const first = session(harness.adapter, authority);
  const firstHandle = started(
    first.session.start("first", harness.adapter),
  );
  await harness.environment.flushAsync();
  harness.workers[0]!.emitRaw(workerEnvelope(1, {
    kind: "probe-acknowledged",
    probeSequence: 1,
  }));
  harness.environment.advanceActive(5);
  await harness.environment.flushAsync();
  assert.equal(harness.host.snapshot().outstandingProbeSequence, 2);

  const second = session(harness.adapter, authority);
  const secondHandle = started(
    second.session.start("second", harness.adapter),
  );
  await harness.environment.flushAsync();
  harness.workers[0]!.emitRaw(workerEnvelope(1, {
    kind: "accepted",
    operation: { operationId: firstHandle.id, operationSequence: 1 },
    allowance: { kind: "bounded", maxSilentActiveMilliseconds: 20 },
  }));
  assert.equal(harness.failures.length, 0);
  assert.equal(harness.host.snapshot().outstandingProbeSequence, 2);
  harness.workers[0]!.emitRaw(workerEnvelope(1, {
    kind: "probe-acknowledged",
    probeSequence: 2,
  }));
  harness.workers[0]!.emitRaw(workerEnvelope(1, {
    kind: "accepted",
    operation: { operationId: secondHandle.id, operationSequence: 2 },
    allowance: { kind: "bounded", maxSilentActiveMilliseconds: 20 },
  }));
  assert.equal(harness.failures.length, 0);
});

test("probe acknowledgments are exact and maximum retirement enters probe-exhaustion", async () => {
  for (const sequence of [0, 2]) {
    const harness = createHarness({
      omitResponse: kind => kind === "probe-acknowledged",
    });
    await startReady(harness);
    harness.environment.advanceActive(10);
    await harness.environment.flushAsync();
    harness.workers[0]!.emitRaw(workerEnvelope(1, {
      kind: "probe-acknowledged",
      probeSequence: sequence === 0 ? 1 : sequence,
    }));
    if (sequence === 0) {
      harness.workers[0]!.emitRaw(workerEnvelope(1, {
        kind: "probe-acknowledged",
        probeSequence: 1,
      }));
    }
    assert.equal(harness.failures[0]?.kind, "protocol");
  }

  const exhaustion = createHarness({
    createProbeSequenceAllocator: () =>
      new WorkerProbeSequenceAllocator(Number.MAX_SAFE_INTEGER),
  });
  await startReady(exhaustion);
  exhaustion.environment.advanceActive(10);
  await exhaustion.environment.flushAsync();
  assert.equal(exhaustion.failures[0]?.kind, "probe-exhaustion");
  assert.equal(exhaustion.host.snapshot().phase, "closed");
});

test("stale acknowledgment fails while a newer probe is outstanding", async () => {
  let omitted = 0;
  const harness = createHarness({
    omitResponse: kind => {
      if (kind !== "probe-acknowledged") return false;
      omitted++;
      return true;
    },
  });
  await startReady(harness);
  harness.environment.advanceActive(10);
  await harness.environment.flushAsync();
  harness.workers[0]!.emitRaw(workerEnvelope(1, {
    kind: "probe-acknowledged",
    probeSequence: 1,
  }));
  harness.environment.advanceActive(10);
  await harness.environment.flushAsync();
  assert.equal(harness.host.snapshot().outstandingProbeSequence, 2);
  harness.workers[0]!.emitRaw(workerEnvelope(1, {
    kind: "probe-acknowledged",
    probeSequence: 1,
  }));
  assert.equal(harness.failures[0]?.kind, "protocol");
  assert.equal(omitted >= 2, true);
});

test("watchdog uses largest allowance, excludes progress, admits while suspect, and fails in two stages", async () => {
  const settlement = deferred<TestSettlement>();
  const harness = createHarness({
    invoke: () => settlement.promise,
    allowance: { kind: "bounded", maxSilentActiveMilliseconds: 20 },
    omitResponse: kind => kind === "probe-acknowledged",
  });
  await startReady(harness);
  const authority = createOperationAuthorityPage({
    allocation: {
      createId: (() => {
        let id = 1;
        return () => `watchdog-${id++}`;
      })(),
    },
  });
  const first = session(harness.adapter, authority);
  const handle = started(first.session.start("input", harness.adapter));
  await harness.environment.flushAsync();
  harness.environment.advanceActive(19);
  harness.workers[0]!.emitRaw(workerEnvelope(1, {
    kind: "progress",
    operation: { operationId: handle.id, operationSequence: 1 },
    payload: "non-renewing",
  }));
  harness.environment.advanceActive(1);
  assert.equal(harness.host.snapshot().phase, "suspect");

  const second = session(harness.adapter, authority);
  const secondHandle = started(
    second.session.start("still-admitted", harness.adapter),
  );
  assert.equal(secondHandle.id.length > 0, true);
  await harness.environment.flushAsync();
  assert.equal(harness.host.snapshot().phase, "draining");
  assert.equal(harness.failures[0]?.kind, "control-response");
});

test("continuous bounded silence produces exact watchdog failure on the second interval", async () => {
  const harness = createHarness({
    omitResponse: kind => kind === "probe-acknowledged",
  });
  await startReady(harness);
  harness.environment.advanceActive(10);
  assert.equal(harness.host.snapshot().phase, "suspect");
  harness.environment.advanceActive(10);
  assert.equal(harness.failures[0]?.kind, "watchdog");
  assert.equal(harness.host.snapshot().phase, "closed");
});

test("bounded epoch-work topology recomputes from retained task evidence origin", async () => {
  const settlement = deferred<TestSettlement>();
  const harness = createHarness({ invoke: () => settlement.promise });
  await startReady(harness);
  harness.environment.advanceActive(5);
  assert.equal(
    harness.workers[0]!.startEpochWork("speculative", 1),
    true,
  );
  assert.equal(harness.host.snapshot().lastTaskEvidenceOrigin, 0);
  harness.environment.advanceActive(24);
  assert.equal(harness.host.snapshot().phase, "ready");
  harness.environment.advanceActive(1);
  assert.equal(harness.host.snapshot().phase, "suspect");
});

test("bounded topology shrink immediately evaluates an elapsed watchdog deadline", async () => {
  const harness = createHarness();
  await startReady(harness);
  assert.equal(harness.workers[0]!.startEpochWork("speculative", 1), true);
  assert.equal(harness.host.snapshot().activeEpochWork, 1);

  harness.environment.advanceActive(20);
  assert.equal(harness.host.snapshot().phase, "ready");
  assert.equal(harness.workers[0]!.finishEpochWork(1), true);

  assert.equal(harness.host.snapshot().phase, "suspect");
});

test("unbounded work disables silence judgment and final close grants one bounded interval", async () => {
  const settlement = deferred<TestSettlement>();
  const harness = createHarness({
    invoke: () => settlement.promise,
    allowance: { kind: "unbounded" },
  });
  await startReady(harness);
  const operationSession = session(harness.adapter);
  const handle = started(
    operationSession.session.start("input", harness.adapter),
  );
  await harness.environment.flushAsync();
  harness.environment.advanceActive(1_000);
  assert.equal(harness.host.snapshot().phase, "ready");
  settlement.resolve({ kind: "succeeded", value: "done" });
  await harness.environment.flushAsync();
  const origin = harness.host.snapshot().lastTaskEvidenceOrigin;
  assert.equal(origin, 1_000);
  harness.environment.advanceActive(9);
  assert.equal(harness.host.snapshot().phase, "ready");
  harness.environment.advanceActive(1);
  assert.equal(harness.host.snapshot().phase, "suspect");
  await handle.quiesced;
});

test("lifecycle and main-loop recovery rebase liveness while preserving the probe", async () => {
  const harness = createHarness({
    omitResponse: kind => kind === "probe-acknowledged",
  });
  await startReady(harness);
  harness.environment.advanceActive(10);
  await harness.environment.flushAsync();
  assert.equal(harness.host.snapshot().phase, "suspect");
  assert.equal(harness.host.snapshot().outstandingProbeSequence, 1);
  harness.environment.suspend();
  harness.environment.advanceActive(100);
  harness.environment.resume();
  assert.equal(harness.host.snapshot().phase, "ready");
  assert.equal(harness.host.snapshot().outstandingProbeSequence, 1);
  harness.environment.recoverMainLoop(100);
  assert.equal(harness.host.snapshot().outstandingProbeSequence, 1);
  harness.environment.advanceActive(9);
  assert.equal(harness.host.snapshot().phase, "ready");
  harness.environment.advanceActive(1);
  assert.equal(harness.host.snapshot().phase, "suspect");
  assert.equal(
    operationMessages(harness.workers[0]!).filter(kind => kind === "probe")
      .length,
    1,
  );
});

test("main-loop recovery preserves unresolved command control grace", async () => {
  const settlement = deferred<TestSettlement>();
  const harness = createHarness({
    invoke: () => settlement.promise,
    controlResponseGraceMilliseconds: 10,
    omitResponse: kind =>
      kind === "accepted" || kind === "probe-acknowledged",
  });
  await startReady(harness);
  const operationSession = session(harness.adapter);
  started(operationSession.session.start("active", harness.adapter));
  await harness.environment.flushAsync();
  harness.environment.advanceActive(9);
  assert.equal(harness.host.snapshot().outstandingProbeSequence, null);
  harness.environment.recoverMainLoop(100);
  assert.equal(harness.host.snapshot().outstandingProbeSequence, null);
  assert.equal(harness.failures.length, 0);
  harness.environment.advanceActive(1);
  assert.equal(harness.host.snapshot().outstandingProbeSequence, 1);
  assert.equal(harness.failures.length, 0);
  assert.equal(
    operationMessages(harness.workers[0]!).filter(kind => kind === "probe")
      .length,
    1,
  );
  harness.host.restart();
});

test("epoch-work validation mirrors high-water, active-set, allowance, and close release", async () => {
  const settlement = deferred<TestSettlement>();
  const harness = createHarness({ invoke: () => settlement.promise });
  await startReady(harness);
  assert.notEqual(
    harness.producerClasses,
    harness.workerProducerClasses[0],
  );
  assert.equal(
    harness.workers[0]!.startEpochWork("speculative", 1),
    true,
  );
  assert.equal(harness.host.snapshot().activeEpochWork, 1);
  assert.equal(
    harness.workers[0]!.finishEpochWork(1),
    true,
  );
  assert.equal(harness.host.snapshot().activeEpochWork, 0);
  assert.equal(
    harness.workers[0]!.finishEpochWork(1),
    false,
  );
  assert.equal(
    harness.workers[0]!.emittedMessages.at(-1)?.kind,
    "epoch-failed",
  );

  const mismatch = createHarness();
  await startReady(mismatch);
  assert.equal(
    mismatch.workers[0]!.startEpochWork(
      "speculative",
      1,
      { kind: "bounded", maxSilentActiveMilliseconds: 29 },
    ),
    false,
  );
  assert.equal(
    mismatch.workers[0]!.emittedMessages.at(-1)?.kind,
    "epoch-failed",
  );

  const unknownClass = createHarness();
  await startReady(unknownClass);
  assert.equal(
    unknownClass.workers[0]!.startEpochWork("unregistered", 1),
    false,
  );
  assert.equal(
    unknownClass.workers[0]!.emittedMessages.at(-1)?.kind,
    "epoch-failed",
  );

  const unknownAllowance = createHarness();
  await startReady(unknownAllowance);
  unknownAllowance.workers[0]!.emitRaw(workerEnvelope(1, {
    kind: "epoch-work-started",
    workSequence: 1,
    allowance: { kind: "bounded", maxSilentActiveMilliseconds: 31 },
  }));
  assert.equal(unknownAllowance.failures[0]?.kind, "protocol");

  const activeDuplicate = createHarness();
  await startReady(activeDuplicate);
  assert.equal(
    activeDuplicate.workers[0]!.startEpochWork("speculative", 1),
    true,
  );
  assert.equal(
    activeDuplicate.workers[0]!.startEpochWork("speculative", 1),
    false,
  );
  assert.equal(
    activeDuplicate.workers[0]!.emittedMessages.at(-1)?.kind,
    "epoch-failed",
  );

  const mainDuplicate = createHarness();
  await startReady(mainDuplicate);
  mainDuplicate.workers[0]!.emitRaw(workerEnvelope(1, {
    kind: "epoch-work-started",
    workSequence: 1,
    allowance: { kind: "bounded", maxSilentActiveMilliseconds: 30 },
  }));
  mainDuplicate.workers[0]!.emitRaw(workerEnvelope(1, {
    kind: "epoch-work-started",
    workSequence: 1,
    allowance: { kind: "bounded", maxSilentActiveMilliseconds: 30 },
  }));
  assert.equal(mainDuplicate.failures[0]?.kind, "protocol");
  mainDuplicate.host.receiveWorkerCrash(
    mainDuplicate.workers[0]!,
    "physical loss",
  );
  assert.equal(mainDuplicate.host.snapshot().activeEpochWork, 0);
});

test("main epoch-work unmatched and duplicate finishes fail closed", async () => {
  const unmatched = createHarness();
  await startReady(unmatched);
  unmatched.workers[0]!.emitRaw(workerEnvelope(1, {
    kind: "epoch-work-finished",
    workSequence: 1,
  }));
  assert.equal(unmatched.failures[0]?.kind, "protocol");

  const duplicate = createHarness();
  await startReady(duplicate);
  duplicate.workers[0]!.emitRaw(workerEnvelope(1, {
    kind: "epoch-work-started",
    workSequence: 1,
    allowance: { kind: "bounded", maxSilentActiveMilliseconds: 30 },
  }));
  duplicate.workers[0]!.emitRaw(workerEnvelope(1, {
    kind: "epoch-work-finished",
    workSequence: 1,
  }));
  duplicate.workers[0]!.emitRaw(workerEnvelope(1, {
    kind: "epoch-work-finished",
    workSequence: 1,
  }));
  assert.equal(duplicate.failures[0]?.kind, "protocol");
});

test("speculative producer lease releases while cache survives until restart", async () => {
  const speculativeContext: {
    current: FakeWorkerOperationContext | null;
  } = { current: null };
  const harness = createHarness({
    workerCount: 2,
    producerClassDefinitions: [{
      name: "speculative",
      allowance: { kind: "bounded", maxSilentActiveMilliseconds: 30 },
      structuralBoundMilliseconds: 30,
    }],
    invoke: (input, context) => {
      if (input === "prime") {
        speculativeContext.current = context;
        assert.equal(context.startEpochWork("speculative", 1), true);
        return { kind: "succeeded", value: "initial" };
      }
      const cached = context.cache.get("index");
      return {
        kind: "succeeded",
        value: typeof cached === "string" ? cached : "cache-miss",
      };
    },
  });
  await startReady(harness);
  const authority = createOperationAuthorityPage({
    allocation: {
      createId: (() => {
        let id = 1;
        return () => `cache-${id++}`;
      })(),
    },
  });
  const first = session(harness.adapter, authority);
  const firstHandle = started(
    first.session.start("prime", harness.adapter),
  );
  assert.deepEqual(await firstHandle.outcome, {
    kind: "succeeded",
    value: "initial",
  });
  await firstHandle.quiesced;
  assert.equal(harness.host.snapshot().activeEpochWork, 1);
  const retainedContext = speculativeContext.current;
  if (retainedContext === null)
    throw new Error("Speculative context was not retained.");
  retainedContext.cache.set("index", "cached-index");
  assert.equal(retainedContext.finishEpochWork(1), true);
  assert.equal(harness.host.snapshot().activeEpochWork, 0);

  const second = session(harness.adapter, authority);
  const secondHandle = started(
    second.session.start("consume", harness.adapter),
  );
  assert.deepEqual(await secondHandle.outcome, {
    kind: "succeeded",
    value: "cached-index",
  });

  harness.host.restart();
  assert.equal(harness.workers[0]!.cache.size, 0);
  assert.equal(harness.host.start("replacement").kind, "started");
  await harness.environment.flushAsync();
  const third = session(harness.adapter, authority);
  const thirdHandle = started(
    third.session.start("consume", harness.adapter),
  );
  assert.deepEqual(await thirdHandle.outcome, {
    kind: "succeeded",
    value: "cache-miss",
  });
});

test("hard termination revokes retained fake-worker operation contexts", async () => {
  const settlement = deferred<TestSettlement>();
  const retainedContext: {
    current: FakeWorkerOperationContext | null;
  } = { current: null };
  const harness = createHarness({
    invoke: (_input, context) => {
      retainedContext.current = context;
      return settlement.promise;
    },
  });
  await startReady(harness);
  const operationSession = session(harness.adapter);
  started(operationSession.session.start("input", harness.adapter));
  await harness.environment.flushAsync();
  const context = retainedContext.current;
  if (context === null)
    throw new Error("Expected a retained operation context.");

  harness.host.restart();

  assert.equal(
    context.startEpochWork("speculative", 1),
    false,
  );
  assert.equal(context.cache.set("late", "value"), false);
  assert.equal(harness.workers[0]!.activeEpochWorkCount, 0);
  assert.equal(harness.workers[0]!.cache.size, 0);
});

test("idle-compatible capabilities are opaque and only issued within the idle bound", () => {
  const registry = new WorkerProducerClassRegistry(10);
  const compatible = registry.register(
    "yielding",
    { kind: "bounded", maxSilentActiveMilliseconds: 10 },
    8,
  );
  const overBudget = registry.register(
    "slow",
    { kind: "bounded", maxSilentActiveMilliseconds: 20 },
    20,
  );
  const unbounded = registry.register(
    "unbounded",
    { kind: "unbounded" },
    8,
  );
  assert.equal(compatible.kind, "idle-compatible");
  if (compatible.kind !== "idle-compatible")
    throw new Error("Expected an idle-compatible capability.");
  assert.equal(registry.acceptsCapability(compatible.capability), true);
  assert.deepEqual(overBudget, { kind: "epoch-work-required" });
  assert.deepEqual(unbounded, { kind: "epoch-work-required" });
  assert.deepEqual(registry.classify("missing"), {
    kind: "epoch-work-required",
  });
  assert.deepEqual(registry.classify("slow"), {
    kind: "epoch-work-required",
  });
});

test("producer class registration rejects evidence beyond its allowance", () => {
  const registry = new WorkerProducerClassRegistry(10);

  assert.throws(
    () => registry.register(
      "invalid",
      { kind: "bounded", maxSilentActiveMilliseconds: 5 },
      6,
    ),
    /must not exceed the producer allowance/,
  );
});

test("runtime host requires producer classes for its exact idle allowance", () => {
  const environment = new ManualWorkerRuntimeEnvironment();

  assert.throws(
    () => new WorkerRuntimeHost<string, TestDiagnostic>({
      transport: new QueueWorkerRuntimeTransportFactory([]),
      clock: environment,
      lifecycle: environment,
      bootstrap: {
        encode: bootstrap => ({ kind: "decoded", value: bootstrap }),
        diagnostic: diagnosticDecoder(),
      },
      diagnostic: diagnosticDecoder(),
      callbacks: {
        failure: () => undefined,
        diagnostic: () => undefined,
        realmReleased: () => undefined,
      },
      createDiagnostic: (kind, detail) => ({ code: kind, detail }),
      idleHeartbeatIntervalMilliseconds: 10,
      startupBudgetMilliseconds: 100,
      controlResponseGraceMilliseconds: 10,
      drainBudgetMilliseconds: 20,
      producerClasses: new WorkerProducerClassRegistry(9),
    }),
    /must use the host idle allowance/,
  );
});

test("worker startup rejects a different producer-class idle allowance", async () => {
  const harness = createHarness({
    workerProducerClassIdleAllowanceMilliseconds: 100,
  });

  assert.equal(harness.host.start("bootstrap").kind, "started");
  await harness.environment.flushAsync();

  assert.equal(harness.failures[0]?.kind, "startup");
  assert.equal(harness.host.snapshot().phase, "closed");
});

test("failure notification cannot reentrantly admit a new operation", async () => {
  let harness: TestHarness;
  let retry: OperationStartResult<string, string, WorkerRuntimePreparationError>
    | null = null;
  harness = createHarness({
    failure: () => {
      const operationSession = session(harness.adapter);
      retry = operationSession.session.start("retry", harness.adapter);
    },
  });
  await startReady(harness);

  harness.workers[0]!.emitRaw({ malformed: true });

  assert.deepEqual(retry, {
    kind: "rejected",
    reason: {
      kind: "producer-rejected",
      error: { kind: "epoch-unavailable" },
    },
  });
  assert.equal(
    harness.workers[0]!.receivedMessages.filter(message =>
      typeof message === "object"
      && message !== null
      && Object.getOwnPropertyDescriptor(message, "kind")?.value === "start"
    ).length,
    0,
  );
});

test("operation closure precedes a reentrant failure-observer cancellation", async () => {
  const active: {
    handle: OperationHandle<string, string> | null;
  } = { handle: null };
  const settlement = deferred<TestSettlement>();
  const harness = createHarness({
    invoke: () => settlement.promise,
    failure: () => {
      active.handle?.cancel("user");
    },
  });
  await startReady(harness);
  const operationSession = session(harness.adapter);
  active.handle = started(
    operationSession.session.start("input", harness.adapter),
  );
  await harness.environment.flushAsync();

  harness.workers[0]!.emitRaw({ malformed: true });

  assert.deepEqual(await active.handle.outcome, {
    kind: "failed",
    error: "boundary:protocol",
  });
});

test("epoch closure seals every assigned record before sink callbacks run", async () => {
  const settlement = deferred<TestSettlement>();
  const harness = createHarness({ invoke: () => settlement.promise });
  await startReady(harness);
  const [firstIdentity, secondIdentity] = captureIdentities(2);
  const terminals: unknown[] = [];
  let secondBinding: PreparedOperationProducer | null = null;
  const firstSink: OperationProducerSink<string, string, string> = {
    reportProgress: () => undefined,
    reportUnexpectedTerminal: () => undefined,
    reportUnexpectedFailure: () => undefined,
    ...terminalCallbacks(outcome => {
      terminals.push(outcome);
      secondBinding?.requestCancellation("user");
      return undefined;
    }),
    reportQuiesced: () => undefined,
  };
  const secondSink: OperationProducerSink<string, string, string> = {
    reportProgress: () => undefined,
    reportUnexpectedTerminal: () => undefined,
    reportUnexpectedFailure: () => undefined,
    ...terminalCallbacks(outcome => {
      terminals.push(outcome);
      return undefined;
    }),
    reportQuiesced: () => undefined,
  };
  const firstBinding = preparedBinding(
    harness.adapter.prepare(firstIdentity!, "first", firstSink),
  );
  secondBinding = preparedBinding(
    harness.adapter.prepare(secondIdentity!, "second", secondSink),
  );
  firstBinding.activate();
  secondBinding.activate();
  await harness.environment.flushAsync();

  harness.workers[0]!.emitRaw({ malformed: true });

  assert.deepEqual(terminals, [
    { kind: "failed", error: "boundary:protocol" },
    { kind: "failed", error: "boundary:protocol" },
  ]);
  assert.deepEqual(
    operationMessages(harness.workers[0]!),
    ["initialize", "start", "start"],
  );
});

test("epoch closure commits siblings before observer failure and publishes failure before release", async () => {
  const settlement = deferred<TestSettlement>();
  const order: string[] = [];
  let secondHandle: OperationHandle<string, string> | null = null;
  let cancelResult: ReturnType<OperationHandle<string, string>["cancel"]>
    | null = null;
  let harness: TestHarness;
  harness = createHarness({
    invoke: () => settlement.promise,
    failure: failure => {
      order.push(`runtime-failure:${failure.kind}`);
    },
    realmReleased: epochToken => {
      order.push(`realm-released:${epochToken}`);
    },
  });
  await startReady(harness);
  const authority = createOperationAuthorityPage({
    allocation: {
      createId: (() => {
        let id = 1;
        return () => `closure-operation-${id++}`;
      })(),
    },
  });
  const firstSession = authority.createSession<
    string,
    string,
    string,
    string,
    WorkerRuntimePreparationError
  >({
    feature: {
      publish: event => {
        if (event.kind !== "terminal") return undefined;
        order.push("first-terminal");
        throw new Error("first terminal observer failed");
      },
    },
    diagnostic: {
      report: diagnostic => {
        assert.equal(diagnostic.kind, "feature-observer");
        order.push("feature-diagnostic");
        cancelResult = secondHandle?.cancel("user") ?? null;
        harness.host.restart();
        assert.equal(harness.workers[0]!.terminated, true);
        assert.deepEqual(harness.releasedEpochs, []);
        return undefined;
      },
    },
  });
  const secondSession = authority.createSession<
    string,
    string,
    string,
    string,
    WorkerRuntimePreparationError
  >({
    feature: {
      publish: event => {
        if (event.kind === "terminal") order.push("second-terminal");
        return undefined;
      },
    },
    diagnostic: { report: () => undefined },
  });
  const firstHandle = started(
    firstSession.start("first", harness.adapter),
  );
  secondHandle = started(
    secondSession.start("second", harness.adapter),
  );
  await harness.environment.flushAsync();

  harness.workers[0]!.emitRaw({ malformed: true });

  assert.deepEqual(cancelResult, { kind: "no-op" });
  assert.equal(harness.failures.length, 1);
  assert.deepEqual(order, [
    "first-terminal",
    "feature-diagnostic",
    "second-terminal",
    "runtime-failure:protocol",
    "realm-released:1",
  ]);
  assert.deepEqual(await firstHandle.outcome, {
    kind: "failed",
    error: "boundary:protocol",
  });
  assert.deepEqual(await secondHandle.outcome, {
    kind: "failed",
    error: "boundary:protocol",
  });
  await Promise.all([firstHandle.quiesced, secondHandle.quiesced]);
});

test("synchronous fake admission cannot invoke after restart releases the realm", async () => {
  let harness: TestHarness;
  let invokeCount = 0;
  harness = createHarness({
    allowance: { kind: "bounded", maxSilentActiveMilliseconds: 20 },
    workerAllowance: { kind: "bounded", maxSilentActiveMilliseconds: 19 },
    invoke: input => {
      invokeCount++;
      return { kind: "succeeded", value: input };
    },
    failure: () => {
      harness.host.restart();
    },
  });
  await startReady(harness);
  const operationSession = session(harness.adapter);
  started(operationSession.session.start("input", harness.adapter));

  await harness.environment.flushAsync();

  assert.equal(harness.host.snapshot().phase, "closed");
  assert.equal(harness.workers[0]!.terminated, true);
  assert.equal(invokeCount, 0);
});

test("draining tracks delayed physical admission through settlement", async () => {
  const harness = createHarness();
  await startReady(harness);
  const operationSession = session(harness.adapter);
  const handle = started(
    operationSession.session.start("input", harness.adapter),
  );

  harness.workers[0]!.emitError("worker event");
  await harness.environment.flushAsync();

  assert.equal(harness.host.snapshot().phase, "closed");
  assert.equal(harness.host.snapshot().activeOperations, 0);
  assert.deepEqual(await handle.outcome, {
    kind: "failed",
    error: "boundary:worker-message",
  });
  await handle.quiesced;
});

test("draining tracks delayed epoch work until its physical finish", async () => {
  const settlement = deferred<TestSettlement>();
  const harness = createHarness({ invoke: () => settlement.promise });
  await startReady(harness);
  const operationSession = session(harness.adapter);
  const handle = started(
    operationSession.session.start("input", harness.adapter),
  );
  await harness.environment.flushAsync();
  harness.workers[0]!.emitError("worker event");

  harness.workers[0]!.emitRaw(workerEnvelope(1, {
    kind: "epoch-work-started",
    workSequence: 1,
    allowance: { kind: "bounded", maxSilentActiveMilliseconds: 30 },
  }));
  assert.equal(harness.host.snapshot().activeEpochWork, 1);
  harness.workers[0]!.emitRaw(workerEnvelope(1, {
    kind: "settled",
    operation: { operationId: handle.id, operationSequence: 1 },
    settlement: { kind: "succeeded", value: "physically-released" },
  }));
  assert.equal(harness.host.snapshot().phase, "draining");

  harness.workers[0]!.emitRaw(workerEnvelope(1, {
    kind: "epoch-work-finished",
    workSequence: 1,
  }));

  assert.equal(harness.host.snapshot().phase, "closed");
  await handle.quiesced;
});

test("disposed hosts reject restart and remain quiescent", async () => {
  const harness = createHarness();
  await startReady(harness);

  harness.host.dispose();
  harness.host.dispose();

  assert.equal(harness.host.snapshot().phase, "closed");
  assert.equal(harness.workers[0]!.terminateCount, 1);
  assert.deepEqual(harness.host.start("replacement"), {
    kind: "rejected",
    reason: "host-disposed",
  });
  harness.environment.advanceActive(1_000);
  assert.equal(harness.failures.length, 0);
});

test("teardown callback failures do not interrupt mandatory shutdown", async () => {
  const clockError = new Error("clock unsubscribe failed");
  const lifecycleError = new Error("lifecycle unsubscribe failed");
  const disposing = createHarness({
    clockUnsubscribeError: clockError,
    lifecycleUnsubscribeError: lifecycleError,
  });
  await startReady(disposing);

  assert.doesNotThrow(() => disposing.host.dispose());

  assert.equal(disposing.host.snapshot().phase, "closed");
  assert.equal(disposing.workers[0]!.terminateCount, 1);
  assert.deepEqual(disposing.releasedEpochs, [1]);
  assert.deepEqual(
    disposing.runtimeDiagnostics.map(diagnostic => diagnostic.detail),
    [clockError, lifecycleError],
  );

  const detachError = new Error("transport detach failed");
  const restarting = createHarness({ detachError });
  await startReady(restarting);

  assert.doesNotThrow(() => restarting.host.restart());

  assert.equal(restarting.host.snapshot().phase, "closed");
  assert.equal(restarting.workers[0]!.terminateCount, 1);
  assert.deepEqual(restarting.releasedEpochs, [1]);
  assert.deepEqual(
    restarting.runtimeDiagnostics.map(diagnostic => diagnostic.detail),
    [detachError],
  );
});

test("first closure identity and producer outcome survive later faults and draining crash", async () => {
  const settlement = deferred<TestSettlement>();
  const firstDiagnostic = { malformed: "first" };
  const harness = createHarness({
    invoke: () => settlement.promise,
    drainBudgetMilliseconds: 100,
  });
  await startReady(harness);
  const operationSession = session(harness.adapter);
  const handle = started(
    operationSession.session.start("input", harness.adapter),
  );
  await harness.environment.flushAsync();
  harness.workers[0]!.emitRaw(firstDiagnostic);
  const closure = harness.host.snapshot().closure;
  assert.equal(closure?.kind, "unexpected-failure");
  assert.equal(
    closure?.kind === "unexpected-failure"
      ? closure.failure.kind
      : null,
    "protocol",
  );
  harness.workers[0]!.emitError("later worker message");
  harness.workers[0]!.emitRaw(workerEnvelope(1, {
    kind: "epoch-failed",
    diagnostic: { code: "later", detail: "worker declared" },
  }));
  assert.equal(harness.host.snapshot().closure, closure);
  assert.deepEqual(await handle.outcome, {
    kind: "failed",
    error: "boundary:protocol",
  });
  harness.host.receiveWorkerCrash(
    harness.workers[0]!,
    "crash during draining",
  );
  assert.equal(harness.host.snapshot().closure, closure);
  assert.equal(harness.host.snapshot().phase, "closed");
  assert.deepEqual(harness.releasedEpochs, [1]);
  await handle.quiesced;
});

test("worker crash is an exact immediate boundary and natural release closes draining early", async () => {
  const crashSettlement = deferred<TestSettlement>();
  const crash = createHarness({ invoke: () => crashSettlement.promise });
  await startReady(crash);
  const crashSession = session(crash.adapter);
  const crashHandle = started(
    crashSession.session.start("input", crash.adapter),
  );
  await crash.environment.flushAsync();
  crash.host.receiveWorkerCrash(crash.workers[0]!, "worker disappeared");
  assert.equal(crash.failures[0]?.kind, "worker-crash");
  assert.equal(crash.host.snapshot().phase, "closed");
  assert.deepEqual(await crashHandle.outcome, {
    kind: "failed",
    error: "boundary:worker-crash",
  });

  const naturalSettlement = deferred<TestSettlement>();
  const natural = createHarness({
    invoke: () => naturalSettlement.promise,
    drainBudgetMilliseconds: 100,
  });
  await startReady(natural);
  const naturalSession = session(natural.adapter);
  const naturalHandle = started(
    naturalSession.session.start("input", natural.adapter),
  );
  await natural.environment.flushAsync();
  natural.workers[0]!.emitRaw({ malformed: true });
  assert.equal(natural.host.snapshot().phase, "draining");
  natural.workers[0]!.emitRaw(workerEnvelope(1, {
    kind: "settled",
    operation: { operationId: naturalHandle.id, operationSequence: 1 },
    settlement: { kind: "succeeded", value: "physically-released" },
  }));
  assert.equal(natural.host.snapshot().phase, "closed");
  assert.equal(natural.environment.now(), 0);
  await naturalHandle.quiesced;
});

test("draining stops admission synchronously", async () => {
  const settlement = deferred<TestSettlement>();
  const harness = createHarness({ invoke: () => settlement.promise });
  await startReady(harness);
  const authority = createOperationAuthorityPage({
    allocation: {
      createId: (() => {
        let id = 1;
        return () => `drain-${id++}`;
      })(),
    },
  });
  const first = session(harness.adapter, authority);
  started(first.session.start("first", harness.adapter));
  await harness.environment.flushAsync();
  harness.workers[0]!.emitRaw({ malformed: true });
  const second = session(harness.adapter, authority);
  const result = second.session.start("second", harness.adapter);
  assert.equal(result.kind, "rejected");
  if (result.kind === "rejected") {
    assert.deepEqual(result.reason, {
      kind: "producer-rejected",
      error: { kind: "epoch-unavailable" },
    });
  }
});

test("planned restart cancels while EpochFailed remains an unexpected boundary", async () => {
  const settlement = deferred<TestSettlement>();
  const planned = createHarness({ invoke: () => settlement.promise });
  await startReady(planned);
  const plannedSession = session(planned.adapter);
  const plannedHandle = started(
    plannedSession.session.start("input", planned.adapter),
  );
  await planned.environment.flushAsync();
  planned.host.restart();
  assert.deepEqual(await plannedHandle.outcome, {
    kind: "canceled",
    reason: "worker-restarted",
  });

  const unexpected = createHarness({ invoke: () => settlement.promise });
  await startReady(unexpected);
  const unexpectedSession = session(unexpected.adapter);
  const unexpectedHandle = started(
    unexpectedSession.session.start("input", unexpected.adapter),
  );
  await unexpected.environment.flushAsync();
  unexpected.workers[0]!.emitRaw(workerEnvelope(1, {
    kind: "epoch-failed",
    diagnostic: { code: "managed-boundary", detail: "failed" },
  }));
  assert.equal(unexpected.failures[0]?.kind, "worker-declared");
  assert.deepEqual(await unexpectedHandle.outcome, {
    kind: "failed",
    error: "boundary:worker-declared",
  });
});

test("callback errors remain failure-complete and realm release is reported once", async () => {
  const settlement = deferred<TestSettlement>();
  const harness = createHarness({ invoke: () => settlement.promise });
  await startReady(harness);
  const identity = captureIdentity();
  const calls: string[] = [];
  const sink: OperationProducerSink<string, string, string> = {
    reportProgress: () => undefined,
    reportUnexpectedTerminal: () => undefined,
    reportUnexpectedFailure: () => {
      calls.push("unexpected");
      throw new Error("unexpected callback failed");
    },
    ...terminalCallbacks(() => {
      calls.push("terminal");
      throw new Error("terminal callback failed");
    }),
    reportQuiesced: () => {
      calls.push("quiesced");
      throw new Error("quiescence callback failed");
    },
  };
  preparedBinding(harness.adapter.prepare(identity, "input", sink)).activate();
  await harness.environment.flushAsync();
  harness.workers[0]!.emitRaw({ malformed: true });
  harness.environment.advanceActive(20);
  assert.deepEqual(calls, ["terminal", "quiesced"]);
  assert.equal(
    harness.runtimeDiagnostics.filter(
      diagnostic => diagnostic.code === "callback-error",
    ).length,
    2,
  );
  harness.host.receiveWorkerCrash(harness.workers[0]!, "late crash");
  assert.deepEqual(harness.releasedEpochs, [1]);
});

test("callbacks and messages after realm release cannot deliver", async () => {
  const settlement = deferred<TestSettlement>();
  const harness = createHarness({ invoke: () => settlement.promise });
  await startReady(harness);
  const operationSession = session(harness.adapter);
  const handle = started(
    operationSession.session.start("input", harness.adapter),
  );
  await harness.environment.flushAsync();
  harness.host.restart();
  const failureCount = harness.failures.length;
  settlement.resolve({ kind: "succeeded", value: "late" });
  await harness.environment.flushAsync();
  harness.workers[0]!.emitRaw(workerEnvelope(1, {
    kind: "heartbeat",
  }));
  assert.equal(harness.failures.length, failureCount);
  assert.deepEqual(await handle.outcome, {
    kind: "canceled",
    reason: "worker-restarted",
  });
  await handle.quiesced;
});

test("operation authority remains usable by a neighboring browser-native producer", async () => {
  const page = createOperationAuthorityPage({
    allocation: { createId: () => "browser-native" },
  });
  const events: TestEvent[] = [];
  const browserSession = page.createSession<
    string,
    string,
    string,
    string,
    string
  >({
    feature: {
      publish: event => {
        events.push(event);
        return undefined;
      },
    },
    diagnostic: { report: () => undefined },
  });
  const nativeAdapter: OperationProducerAdapter<
    string,
    string,
    string,
    string,
    string
  > = {
    prepare: (_identity, input, sink) => ({
      kind: "prepared",
      binding: {
        requestCancellation: () => undefined,
        abandon: () => undefined,
        activate: () => {
          sink.reportTerminal({ kind: "succeeded", value: input });
          sink.reportQuiesced();
        },
      },
    }),
  };
  const handle = started(browserSession.start("native", nativeAdapter));
  assert.deepEqual(await handle.outcome, {
    kind: "succeeded",
    value: "native",
  });
  await handle.quiesced;
  assert.deepEqual(events.map(event => event.kind), ["started", "terminal"]);
});

test("sequence allocators start at one, never wrap, and report exhaustion", () => {
  const probes = new WorkerProbeSequenceAllocator(
    Number.MAX_SAFE_INTEGER,
  );
  assert.deepEqual(probes.allocate(), {
    kind: "allocated",
    sequence: Number.MAX_SAFE_INTEGER,
  });
  assert.deepEqual(probes.allocate(), { kind: "exhausted" });

  const normal = new WorkerProbeSequenceAllocator();
  assert.deepEqual(normal.allocate(), { kind: "allocated", sequence: 1 });
  assert.deepEqual(normal.allocate(), { kind: "allocated", sequence: 2 });
});
