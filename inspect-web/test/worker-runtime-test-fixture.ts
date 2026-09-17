import assert from "node:assert/strict";
import {
  createOperationAuthorityPage,
  type OperationCancellationState,
  type OperationFeatureEvent,
  type OperationHandle,
  type OperationIdentity,
  type OperationOutcome,
  type OperationPreparation,
  type OperationProducerAdapter,
  type OperationProducerSink,
  type OperationSession,
  type OperationStartResult,
} from "../src/operation-authority.ts";
import {
  FakeWorkerOperationCatalog,
  FakeWorkerRuntime,
  ManualWorkerRuntimeEnvironment,
  QueueWorkerRuntimeTransportFactory,
  WorkerProbeSequenceAllocator,
  WorkerProducerClassRegistry,
  WorkerRuntimeHost,
  type FakeWorkerOperationContext,
  type FakeWorkerOperationRegistration,
  type FakeWorkerRuntimeOptions,
  type WorkerRuntimeBoundaryErrors,
  type WorkerRuntimeControlledOperationAdapter,
  type WorkerRuntimeControlledOperationRegistration,
  type WorkerRuntimeFailure,
  type WorkerRuntimeFailureKind,
  type WorkerRuntimeLifecycleListeners,
  type WorkerRuntimePreparationError,
  type WorkerRuntimeSource,
  type WorkerRuntimeTransportBinding,
  type WorkerRuntimeTransportHandlers,
} from "../src/worker-runtime-core.ts";
import {
  WorkerOperationCatalog,
  WorkerRuntimeRealm,
  type WorkerOperationRegistration,
  type WorkerRuntimeRealmOptions,
} from "../src/worker-runtime-realm.ts";
import {
  type BoundedPayloadDecodeResult,
  type BoundedPayloadDecoder,
  type ManagedOperationSettlement,
  type RawWorkerToMainEnvelope,
  type WorkerLivenessAllowance,
  WORKER_RUNTIME_PROTOCOL_VERSION,
} from "../src/worker-runtime-protocol.ts";

export interface TestDiagnostic {
  readonly code: string;
  readonly detail: unknown;
}

export type TestAdapter = WorkerRuntimeControlledOperationAdapter<
  string,
  string,
  string,
  string,
  WorkerRuntimePreparationError,
  never,
  string,
  string,
  string
>;
export type TestSession = OperationSession<
  string,
  string,
  string,
  string,
  WorkerRuntimePreparationError
>;
export type TestEvent = OperationFeatureEvent<string, string, string>;
export type TestHost = WorkerRuntimeHost<
  string,
  TestDiagnostic
>;
export type TestWorker = FakeWorkerRuntime<
  string,
  TestDiagnostic
>;
export type TestSettlement = ManagedOperationSettlement<
  string,
  string,
  TestDiagnostic
>;

export const uncanceledOperation: OperationCancellationState = { reason: null };

export interface Deferred<T> {
  readonly promise: Promise<T>;
  readonly resolve: (value: T) => void;
  readonly reject: (error: Error) => void;
}

export interface TestHarness {
  readonly environment: ManualWorkerRuntimeEnvironment;
  readonly producerClasses: WorkerProducerClassRegistry;
  readonly workerProducerClasses: readonly WorkerProducerClassRegistry[];
  readonly workers: readonly TestWorker[];
  readonly transportSources: readonly WorkerRuntimeSource[];
  readonly host: TestHost;
  readonly adapter: TestAdapter;
  readonly failures: WorkerRuntimeFailure<TestDiagnostic>[];
  readonly runtimeDiagnostics: TestDiagnostic[];
  readonly releasedEpochs: number[];
}

export interface HarnessOptions {
  readonly bootstrap?: () => void | Promise<void>;
  readonly encodeBootstrap?: (
    bootstrap: string,
  ) => BoundedPayloadDecodeResult<unknown>;
  readonly encodeInput?: (
    input: string,
  ) => BoundedPayloadDecodeResult<unknown>;
  readonly encodeControlInput?: (
    input: string,
  ) => BoundedPayloadDecodeResult<unknown>;
  readonly clockUnsubscribeError?: Error;
  readonly lifecycleUnsubscribeError?: Error;
  readonly create?: () => void;
  readonly detachError?: Error;
  readonly detach?: () => void;
  readonly terminate?: () => void;
  readonly bindMessage?: unknown;
  readonly synchronousInitializeMessages?: (
    epochToken: number,
  ) => readonly unknown[];
  readonly synchronousAccepted?: boolean;
  readonly synchronousAcceptedAllowance?: WorkerLivenessAllowance;
  readonly synchronousStartError?: unknown;
  readonly synchronousStartActiveAdvanceMilliseconds?: number;
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
  readonly control?: WorkerOperationRegistration<
    string,
    string,
    string,
    TestDiagnostic,
    string,
    string
  >["control"] | null;
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
  readonly diagnostic?: (diagnostic: TestDiagnostic) => void;
  readonly realmReleased?: (epochToken: number) => void;
  readonly workerProducerClassIdleAllowanceMilliseconds?: number;
  readonly workerAllowance?: WorkerLivenessAllowance;
}

export interface ProducerClassDefinition {
  readonly name: string;
  readonly allowance: WorkerLivenessAllowance;
  readonly structuralBoundMilliseconds: number | null;
}

export function deferred<T>(): Deferred<T> {
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

export function stringDecoder(): BoundedPayloadDecoder<string> {
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

export function terminalCallbacks<TValue, TError>(
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

export function boundaryErrors<TError>(
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

export function diagnosticDecoder(): BoundedPayloadDecoder<TestDiagnostic> {
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

export function recordDecoder<T>(
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

export function mainRegistration(
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
  encodeControlInput: (
    input: string,
  ) => BoundedPayloadDecodeResult<unknown> = input => input.length <= 32
    ? { kind: "decoded", value: input }
    : {
        kind: "rejected",
        reason: "oversized",
        message: "Control input exceeds 32 code units.",
      },
): WorkerRuntimeControlledOperationRegistration<
  string,
  string,
  string,
  TestDiagnostic,
  string,
  WorkerRuntimePreparationError,
  never,
  string,
  string,
  string
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
    control: {
      encodeInput: encodeControlInput,
      value: stringDecoder(),
      mapRequestError: error => `control:${error.kind}`,
      boundaryErrors: boundaryErrors(kind => `control-boundary:${kind}`),
    },
  };
}

export function createProducerClasses(
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

export function createHarness(options: HarnessOptions = {}): TestHarness {
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
  const transportSources: WorkerRuntimeSource[] = [];
  const workerProducerClasses: WorkerProducerClassRegistry[] = [];
  for (let index = 0; index < workerCount; index++) {
    const operations = new FakeWorkerOperationCatalog();
    operations.register<string, string, string, TestDiagnostic, string, string>({
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
      ...(options.control === null
        ? {}
        : {
            control: options.control ?? {
              input: stringDecoder(),
              invoke: (_operation, input) => ({
                kind: "acknowledged",
                value: `controlled:${input}`,
              }),
            },
          }),
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
  const createCallback = options.create;
  const detachError = options.detachError;
  const detachCallback = options.detach;
  const terminateCallback = options.terminate;
  const clockUnsubscribeError = options.clockUnsubscribeError;
  const lifecycleUnsubscribeError = options.lifecycleUnsubscribeError;
  const bindMessage = options.bindMessage;
  const synchronousInitializeMessages = options.synchronousInitializeMessages;
  const synchronousAcceptedAllowance = options.synchronousAcceptedAllowance
    ?? (options.synchronousAccepted === true ? allowance : undefined);
  const synchronousStartError = options.synchronousStartError;
  const synchronousStartActiveAdvanceMilliseconds =
    options.synchronousStartActiveAdvanceMilliseconds;
  const queuedTransport = new QueueWorkerRuntimeTransportFactory(workers);
  const transport = detachError === undefined
    && createCallback === undefined
    && detachCallback === undefined
    && terminateCallback === undefined
    && bindMessage === undefined
    && synchronousInitializeMessages === undefined
    && synchronousAcceptedAllowance === undefined
    && synchronousStartError === undefined
    && synchronousStartActiveAdvanceMilliseconds === undefined
    ? queuedTransport
    : {
        create: (): WorkerRuntimeTransportBinding => {
          createCallback?.();
          const binding = queuedTransport.create();
          let handlers: WorkerRuntimeTransportHandlers | null = null;
          const source: WorkerRuntimeSource = {
            send: message => {
              binding.source.send(message);
              if (typeof message !== "object" || message === null) return;
              const epochToken = ownDataProperty(message, "epochToken");
              if (typeof epochToken !== "number") return;
              const kind = ownDataProperty(message, "kind");
              if (kind === "initialize"
                && synchronousInitializeMessages !== undefined) {
                for (const response of synchronousInitializeMessages(epochToken))
                  handlers?.message(source, response);
              }
              if (kind !== "start") return;
              if (synchronousStartActiveAdvanceMilliseconds !== undefined) {
                environment.advanceActive(
                  synchronousStartActiveAdvanceMilliseconds,
                );
              }
              if (synchronousStartError !== undefined)
                handlers?.error(source, synchronousStartError);
              if (synchronousAcceptedAllowance !== undefined) {
                handlers?.message(source, workerEnvelope(epochToken, {
                  kind: "accepted",
                  operation: ownDataProperty(message, "operation"),
                  allowance: synchronousAcceptedAllowance,
                }));
              }
            },
            terminate: () => {
              terminateCallback?.();
              binding.source.terminate();
            },
          };
          transportSources.push(source);
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
              if (bindMessage !== undefined)
                nextHandlers.message(source, bindMessage);
              return () => {
                detachCallback?.();
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
        options.diagnostic?.(diagnostic.diagnostic);
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
  const adapter = host.registerControlledOperation(
    mainRegistration(
      allowance,
      options.encodeInput,
      options.encodeControlInput,
    ),
  );
  return {
    environment,
    producerClasses,
    workerProducerClasses,
    workers,
    transportSources,
    host,
    adapter,
    failures,
    runtimeDiagnostics,
    releasedEpochs,
  };
}

export async function startReady(harness: TestHarness): Promise<void> {
  assert.equal(harness.host.start("bootstrap").kind, "started");
  await harness.environment.flushAsync();
  assert.equal(harness.host.snapshot().phase, "ready");
}

export function session(
  adapter: TestAdapter,
  page = createOperationAuthorityPage({
    allocation: {
      createId: (() => {
        let id = 1;
        return () => `operation-${id++}`;
      })(),
    },
  }),
  publish?: (event: TestEvent) => undefined,
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
        publish?.(event);
        return undefined;
      },
    },
    diagnostic: {
      report: () => undefined,
    },
  });
  return { session: operationSession, events };
}

export function started<TValue, TError, TPrepareError>(
  result: OperationStartResult<TValue, TError, TPrepareError>,
): OperationHandle<TValue, TError> {
  assert.equal(result.kind, "started");
  if (result.kind !== "started")
    throw new Error("Expected a started operation.");
  return result.handle;
}

export function captureIdentities(count: number): readonly OperationIdentity[] {
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

export function captureIdentity(): OperationIdentity {
  return captureIdentities(1)[0]!;
}

export function preparedBinding(
  preparation: OperationPreparation<WorkerRuntimePreparationError>,
) {
  assert.equal(preparation.kind, "prepared");
  if (preparation.kind !== "prepared")
    throw new Error("Expected prepared binding.");
  return preparation.binding;
}

export function workerEnvelope(
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

export function operationMessages(worker: TestWorker): readonly string[] {
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

export function workerReceivedMessage(worker: TestWorker, kind: string): object {
  for (const message of worker.receivedMessages) {
    if (typeof message === "object"
      && message !== null
      && ownDataProperty(message, "kind") === kind) {
      return message;
    }
  }
  throw new Error(`Expected Worker to receive ${kind}.`);
}

export function ownDataProperty(value: object, property: string): unknown {
  const descriptor = Object.getOwnPropertyDescriptor(value, property);
  if (descriptor === undefined || !("value" in descriptor)) return undefined;
  const propertyValue: unknown = descriptor.value;
  return propertyValue;
}

export function postWorker(worker: TestWorker, message: unknown): void {
  worker.send(message);
}

export function createRealmHarness(options: {
  readonly bootstrap?: WorkerRuntimeRealmOptions<
    string,
    TestDiagnostic
  >["bootstrap"];
  readonly invoke?: WorkerOperationRegistration<
    string,
    string,
    string,
    TestDiagnostic
  >["invoke"];
  readonly cancel?: WorkerOperationRegistration<
    string,
    string,
    string,
    TestDiagnostic
  >["cancel"];
  readonly control?: WorkerOperationRegistration<
    string,
    string,
    string,
    TestDiagnostic,
    string,
    string
  >["control"];
} = {}) {
  const messages: RawWorkerToMainEnvelope[] = [];
  const operations = new WorkerOperationCatalog();
  operations.register<string, string, string, TestDiagnostic, string, string>({
    kind: "echo",
    allowance: { kind: "bounded", maxSilentActiveMilliseconds: 20 },
    input: stringDecoder(),
    rejectInvalidPayload: detail => ({
      error: "invalid-payload",
      diagnostic: { code: "invalid-payload", detail },
    }),
    invoke: options.invoke ?? (input => ({ kind: "succeeded", value: input })),
    ...(options.cancel === undefined ? {} : { cancel: options.cancel }),
    ...(options.control === undefined ? {} : { control: options.control }),
  });
  const realm = new WorkerRuntimeRealm({
    bootstrap: options.bootstrap ?? {
      decoder: stringDecoder(),
      bootstrap: () => undefined,
    },
    diagnostic: detail => ({ code: "worker", detail }),
    unknownOperationRejection: kind => ({
      error: "unknown-operation-kind",
      diagnostic: { code: "unknown-operation-kind", detail: kind },
    }),
    operations,
    producerClasses: createProducerClasses([{
      name: "speculative",
      allowance: { kind: "bounded", maxSilentActiveMilliseconds: 30 },
      structuralBoundMilliseconds: 30,
    }]),
    post: message => messages.push(message),
  });
  return { realm, messages };
}

export function realmInitialization(bootstrap: unknown = "bootstrap"): unknown {
  return workerEnvelope(1, {
    kind: "initialize",
    bootstrap,
    idleHeartbeatIntervalMilliseconds: 10,
    idleAllowanceMilliseconds: 10,
  });
}

export async function flushRealm(): Promise<void> {
  await new Promise<void>(resolve => setImmediate(resolve));
}
