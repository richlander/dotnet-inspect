import assert from "node:assert/strict";
import test from "node:test";

import {
  createOperationAuthorityPage,
  type OperationDiagnostic,
  type OperationFeatureEvent,
  type OperationHandle,
  type OperationProducerAdapter,
  type OperationSession,
  type OperationStartResult,
} from "../src/operation-authority.ts";
import {
  ManualWorkerRuntimeEnvironment,
  WorkerProducerClassRegistry,
  WorkerRuntimeHost,
  type WorkerRuntimeBoundaryErrors,
  type WorkerRuntimeFailure,
  type WorkerRuntimeFailureKind,
  type WorkerRuntimeOperationRegistration,
  type WorkerRuntimePreparationError,
  type WorkerRuntimeSource,
  type WorkerRuntimeTransportBinding,
  type WorkerRuntimeTransportHandlers,
} from "../src/worker-runtime-core.ts";
import {
  WorkerOperationCatalog,
  WorkerRuntimeRealm,
  type WorkerOperationContext,
  type WorkerOperationRegistration,
} from "../src/worker-runtime-realm.ts";
import {
  type BoundedPayloadDecoder,
  type ManagedOperationSettlement,
  type RawEventsWorkerToMainEnvelope,
  type RawWorkerToMainEnvelope,
  type WorkerNonterminalEvent,
  type WorkerWireOperationReference,
  WORKER_RUNTIME_MAX_EVENT_BATCH_SIZE,
  WORKER_RUNTIME_PROTOCOL_VERSION,
} from "../src/worker-runtime-protocol.ts";

interface TestDiagnostic {
  readonly code: string;
  readonly detail: unknown;
}

type TestDurable =
  | { readonly kind: "item"; readonly value: string }
  | { readonly kind: "item-failure"; readonly error: string };

type TestSettlement = ManagedOperationSettlement<
  string,
  string,
  TestDiagnostic
>;

type TestEvent = OperationFeatureEvent<
  string,
  string,
  string,
  TestDurable
>;

type TestAdapter = OperationProducerAdapter<
  string,
  string,
  string,
  string,
  WorkerRuntimePreparationError,
  TestDurable
>;

type TestSession = OperationSession<
  string,
  string,
  string,
  string,
  WorkerRuntimePreparationError,
  TestDurable
>;

interface Deferred<T> {
  readonly promise: Promise<T>;
  readonly resolve: (value: T) => void;
}

interface CapturedInvocation {
  readonly input: string;
  readonly context: WorkerOperationContext;
}

interface IntegratedHarness {
  readonly environment: ManualWorkerRuntimeEnvironment;
  readonly host: WorkerRuntimeHost<string, TestDiagnostic>;
  readonly transports: readonly RealmTransport[];
  readonly adapter: TestAdapter;
  readonly session: TestSession;
  readonly events: TestEvent[];
  readonly authorityDiagnostics: OperationDiagnostic[];
  readonly runtimeFailures: WorkerRuntimeFailure<TestDiagnostic>[];
  readonly invocations: CapturedInvocation[];
}

interface HarnessOptions {
  readonly invoke?: (
    input: string,
    context: WorkerOperationContext,
  ) => TestSettlement | Promise<TestSettlement>;
  readonly cancel?: WorkerOperationRegistration<
    string,
    string,
    string,
    TestDiagnostic
  >["cancel"];
  readonly durableDecoder?: BoundedPayloadDecoder<TestDurable> | null;
  readonly onFeature?: (event: TestEvent) => void;
  readonly workerCount?: number;
  readonly failPost?: (
    workerIndex: number,
    message: RawWorkerToMainEnvelope,
  ) => Error | null;
}

function deferred<T>(): Deferred<T> {
  let resolvePromise: ((value: T) => void) | undefined;
  const promise = new Promise<T>((resolve) => {
    resolvePromise = resolve;
  });
  return {
    promise,
    resolve: value => {
      resolvePromise?.(value);
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

function diagnosticDecoder(): BoundedPayloadDecoder<TestDiagnostic> {
  return {
    decode: value => {
      if (typeof value !== "object" || value === null || Array.isArray(value)) {
        return {
          kind: "rejected",
          reason: "invalid",
          message: "Expected a diagnostic record.",
        };
      }
      const keys = Reflect.ownKeys(value);
      const code = Object.getOwnPropertyDescriptor(value, "code");
      const detail = Object.getOwnPropertyDescriptor(value, "detail");
      if (keys.length !== 2
        || code === undefined
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

function durableDecoder(
  decoded?: (value: TestDurable) => void,
): BoundedPayloadDecoder<TestDurable> {
  return {
    decode: value => {
      if (typeof value !== "object" || value === null || Array.isArray(value)) {
        return {
          kind: "rejected",
          reason: "invalid",
          message: "Expected a durable event record.",
        };
      }
      const kind = Object.getOwnPropertyDescriptor(value, "kind");
      if (kind === undefined || !("value" in kind)) {
        return {
          kind: "rejected",
          reason: "invalid",
          message: "Expected a durable event kind.",
        };
      }
      let result: TestDurable | null = null;
      if (kind.value === "item") {
        const itemValue = Object.getOwnPropertyDescriptor(value, "value");
        if (Reflect.ownKeys(value).length === 2
          && itemValue !== undefined
          && "value" in itemValue
          && typeof itemValue.value === "string") {
          result = { kind: "item", value: itemValue.value };
        }
      } else if (kind.value === "item-failure") {
        const error = Object.getOwnPropertyDescriptor(value, "error");
        if (Reflect.ownKeys(value).length === 2
          && error !== undefined
          && "value" in error
          && typeof error.value === "string") {
          result = { kind: "item-failure", error: error.value };
        }
      }
      if (result === null) {
        return {
          kind: "rejected",
          reason: "invalid",
          message: "Expected a closed item or item-failure event.",
        };
      }
      decoded?.(result);
      return { kind: "decoded", value: result };
    },
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

function producerClasses(): WorkerProducerClassRegistry {
  return new WorkerProducerClassRegistry(10);
}

class RealmTransport implements WorkerRuntimeTransportBinding, WorkerRuntimeSource {
  readonly source: WorkerRuntimeSource = this;
  readonly realm: WorkerRuntimeRealm<string, TestDiagnostic>;
  readonly sentToWorker: unknown[] = [];
  readonly postedToMain: RawWorkerToMainEnvelope[] = [];
  readonly #failPost: (message: RawWorkerToMainEnvelope) => Error | null;
  #handlers: WorkerRuntimeTransportHandlers | null = null;
  #terminated = false;

  constructor(
    environment: ManualWorkerRuntimeEnvironment,
    operations: WorkerOperationCatalog,
    failPost: (message: RawWorkerToMainEnvelope) => Error | null,
  ) {
    this.#failPost = failPost;
    this.realm = new WorkerRuntimeRealm({
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
      producerClasses: producerClasses(),
      post: message => {
        const failure = this.#failPost(message);
        if (failure !== null) throw failure;
        this.postedToMain.push(message);
        this.#handlers?.message(this, message);
      },
    });
  }

  get terminated(): boolean {
    return this.#terminated;
  }

  bind(handlers: WorkerRuntimeTransportHandlers): () => void {
    this.#handlers = handlers;
    return () => {
      if (this.#handlers === handlers) this.#handlers = null;
    };
  }

  send(message: unknown): void {
    if (this.#terminated) throw new Error("Worker transport is terminated.");
    this.sentToWorker.push(message);
    this.realm.receive(message);
  }

  terminate(): void {
    if (this.#terminated) return;
    this.#terminated = true;
    this.#handlers = null;
    this.realm.dispose();
  }

  emitToHost(message: unknown): void {
    this.#handlers?.message(this, message);
  }
}

class RealmTransportFactory {
  readonly #transports: RealmTransport[];

  constructor(transports: readonly RealmTransport[]) {
    this.#transports = [...transports];
  }

  create(): WorkerRuntimeTransportBinding {
    const transport = this.#transports.shift();
    if (transport === undefined)
      throw new Error("No Worker realm transport remains.");
    return transport;
  }
}

function createHarness(options: HarnessOptions = {}): IntegratedHarness {
  const environment = new ManualWorkerRuntimeEnvironment();
  const invocations: CapturedInvocation[] = [];
  const workerCount = options.workerCount ?? 1;
  const transports = Array.from({ length: workerCount }, (_, workerIndex) => {
    const operations = new WorkerOperationCatalog();
    operations.register({
      kind: "events",
      allowance: { kind: "bounded", maxSilentActiveMilliseconds: 20 },
      input: stringDecoder(),
      rejectInvalidPayload: failure => ({
        error: "invalid-payload",
        diagnostic: { code: "invalid-payload", detail: failure },
      }),
      invoke: (input, context) => {
        invocations.push({ input, context });
        return options.invoke?.(input, context)
          ?? { kind: "succeeded", value: input };
      },
      ...(options.cancel === undefined ? {} : { cancel: options.cancel }),
    });
    return new RealmTransport(
      environment,
      operations,
      message => options.failPost?.(workerIndex, message) ?? null,
    );
  });
  const runtimeFailures: WorkerRuntimeFailure<TestDiagnostic>[] = [];
  const hostProducerClasses = producerClasses();
  const host = new WorkerRuntimeHost<string, TestDiagnostic>({
    transport: new RealmTransportFactory(transports),
    clock: environment,
    lifecycle: environment,
    bootstrap: {
      encode: bootstrap => ({ kind: "decoded", value: bootstrap }),
      diagnostic: diagnosticDecoder(),
    },
    diagnostic: diagnosticDecoder(),
    callbacks: {
      failure: failure => {
        runtimeFailures.push(failure);
        return undefined;
      },
      diagnostic: () => undefined,
      realmReleased: () => undefined,
    },
    createDiagnostic: (kind, detail) => ({ code: kind, detail }),
    idleHeartbeatIntervalMilliseconds: 10,
    startupBudgetMilliseconds: 100,
    controlResponseGraceMilliseconds: 10,
    drainBudgetMilliseconds: 20,
    producerClasses: hostProducerClasses,
  });
  const registration: WorkerRuntimeOperationRegistration<
    string,
    string,
    string,
    TestDiagnostic,
    string,
    WorkerRuntimePreparationError,
    TestDurable
  > = {
    kind: "events",
    allowance: { kind: "bounded", maxSilentActiveMilliseconds: 20 },
    encodeInput: input => ({ kind: "decoded", value: input }),
    value: stringDecoder(),
    error: stringDecoder(),
    diagnostic: diagnosticDecoder(),
    progress: stringDecoder(),
    ...(options.durableDecoder === null
      ? {}
      : { durable: options.durableDecoder ?? durableDecoder() }),
    mapPreparationError: error => error,
    boundaryErrors: boundaryErrors(kind => `boundary:${kind}`),
  };
  const adapter = host.registerOperation(registration);
  const events: TestEvent[] = [];
  const authorityDiagnostics: OperationDiagnostic[] = [];
  const page = createOperationAuthorityPage({
    allocation: {
      createId: (() => {
        let id = 1;
        return () => `event-operation-${id++}`;
      })(),
    },
  });
  const session = page.createSession<
    string,
    string,
    string,
    string,
    WorkerRuntimePreparationError,
    TestDurable
  >({
    feature: {
      publish: event => {
        events.push(event);
        options.onFeature?.(event);
        return undefined;
      },
    },
    diagnostic: {
      report: diagnostic => {
        authorityDiagnostics.push(diagnostic);
        return undefined;
      },
    },
  });
  return {
    environment,
    host,
    transports,
    adapter,
    session,
    events,
    authorityDiagnostics,
    runtimeFailures,
    invocations,
  };
}

async function startReady(harness: IntegratedHarness): Promise<void> {
  assert.equal(harness.host.start("bootstrap").kind, "started");
  await harness.environment.flushAsync();
  assert.equal(harness.host.snapshot().phase, "ready");
}

function started<TValue, TError, TPrepareError>(
  result: OperationStartResult<TValue, TError, TPrepareError>,
): OperationHandle<TValue, TError> {
  assert.equal(result.kind, "started");
  if (result.kind !== "started")
    throw new Error("Expected a started operation.");
  return result.handle;
}

async function promiseSettled(promise: Promise<unknown>): Promise<boolean> {
  let settled = false;
  promise.then(
    () => {
      settled = true;
      return undefined;
    },
    () => {
      settled = true;
      return undefined;
    },
  );
  await Promise.resolve();
  return settled;
}

function eventLabel(event: TestEvent): string {
  switch (event.kind) {
    case "started":
      return "started";
    case "replaced":
      return "replaced";
    case "progress":
      return `progress:${event.progress.value}`;
    case "durable":
      return event.durable.value.kind === "item"
        ? `item:${event.durable.value.value}`
        : `item-failure:${event.durable.value.error}`;
    case "terminal":
      return event.outcome.kind === "succeeded"
        ? `succeeded:${event.outcome.value}`
        : `failed:${event.outcome.error}`;
    case "canceled":
      return `canceled:${event.reason}`;
    case "disposed":
      return "disposed";
  }
  throw new Error("Unknown operation event.");
}

function eventsEnvelope(
  epochToken: number,
  operation: WorkerWireOperationReference,
  entries: RawEventsWorkerToMainEnvelope["entries"],
): RawEventsWorkerToMainEnvelope {
  return {
    protocolVersion: WORKER_RUNTIME_PROTOCOL_VERSION,
    epochToken,
    kind: "events",
    operation,
    entries,
  };
}

const orderedEntries = [
  { kind: "progress", payload: "discovering" },
  { kind: "durable", payload: { kind: "item", value: "one" } },
  {
    kind: "durable",
    payload: { kind: "item-failure", error: "two-failed" },
  },
] satisfies readonly WorkerNonterminalEvent<unknown, unknown>[];

for (const terminal of [
  {
    name: "successful",
    settlement: {
      kind: "succeeded",
      value: "complete",
    } satisfies TestSettlement,
    terminalLabel: "succeeded:complete",
    diagnosticCount: 0,
  },
  {
    name: "canceled",
    settlement: {
      kind: "canceled",
      reason: "user",
    } satisfies TestSettlement,
    terminalLabel: "canceled:user",
    diagnosticCount: 0,
  },
  {
    name: "expected failure",
    settlement: {
      kind: "failed",
      failureKind: "expected",
      error: "expected-error",
      diagnostic: { code: "expected", detail: "detail" },
    } satisfies TestSettlement,
    terminalLabel: "failed:expected-error",
    diagnosticCount: 0,
  },
  {
    name: "unexpected failure",
    settlement: {
      kind: "failed",
      failureKind: "unexpected",
      error: "unexpected-error",
      diagnostic: { code: "unexpected", detail: "detail" },
    } satisfies TestSettlement,
    terminalLabel: "failed:unexpected-error",
    diagnosticCount: 1,
  },
] as const) {
  test(`mixed event batches preserve order before ${terminal.name} settlement`, async t => {
    const harness = createHarness({
      invoke: (_input, context) => {
        assert.equal(context.reportEvents(orderedEntries), true);
        assert.equal(context.reportEvents([{
          kind: "durable",
          payload: { kind: "item", value: "three" },
        }]), true);
        assert.equal(context.reportProgress("finishing"), true);
        return terminal.settlement;
      },
    });
    await startReady(harness);
    const handle = started(harness.session.start("input", harness.adapter));
    await harness.environment.flushAsync();

    assert.deepEqual(harness.events.map(eventLabel), [
      "started",
      "progress:discovering",
      "item:one",
      "item-failure:two-failed",
      "item:three",
      "progress:finishing",
      terminal.terminalLabel,
    ]);
    assert.equal(
      harness.authorityDiagnostics.length,
      terminal.diagnosticCount,
    );
    assert.deepEqual(await handle.outcome, terminal.settlement.kind === "failed"
      ? { kind: "failed", error: terminal.settlement.error }
      : terminal.settlement.kind === "canceled"
        ? { kind: "canceled", reason: terminal.settlement.reason }
        : { kind: "succeeded", value: terminal.settlement.value });
    await handle.quiesced;
    if (terminal.name === "successful") {
      t.diagnostic(`Wire: ${harness.transports[0]!.postedToMain
        .map(message => message.kind).join(" -> ")}`);
      t.diagnostic(`Feature: ${harness.events.map(eventLabel).join(" -> ")}`);
    }
  });
}

test("progress-only registrations accept batched and standalone progress", async () => {
  const harness = createHarness({
    durableDecoder: null,
    invoke: (_input, context) => {
      assert.equal(context.reportEvents([{
        kind: "progress",
        payload: "batched",
      }]), true);
      assert.equal(context.reportProgress("standalone"), true);
      return { kind: "succeeded", value: "done" };
    },
  });
  await startReady(harness);
  const handle = started(harness.session.start("input", harness.adapter));
  await harness.environment.flushAsync();

  assert.deepEqual(harness.events.map(eventLabel), [
    "started",
    "progress:batched",
    "progress:standalone",
    "succeeded:done",
  ]);
  await handle.quiesced;
});

test("logical cancellation suppresses later batch publication until physical settlement", async () => {
  const settlement = deferred<TestSettlement>();
  const harness = createHarness({
    invoke: () => settlement.promise,
    cancel: () => true,
  });
  await startReady(harness);
  const handle = started(harness.session.start("input", harness.adapter));
  await harness.environment.flushAsync();
  const context = harness.invocations[0]!.context;

  assert.deepEqual(handle.cancel(), { kind: "applied" });
  assert.equal(context.reportEvents([
    { kind: "durable", payload: { kind: "item", value: "first" } },
    { kind: "durable", payload: { kind: "item", value: "suppressed" } },
  ]), true);
  await harness.environment.flushAsync();
  assert.deepEqual(harness.events.map(eventLabel), [
    "started",
    "canceled:user",
  ]);
  assert.deepEqual(await handle.outcome, { kind: "canceled", reason: "user" });
  assert.equal(await promiseSettled(handle.quiesced), false);

  settlement.resolve({ kind: "canceled", reason: "user" });
  await harness.environment.flushAsync();
  await handle.quiesced;
});

test("supersession suppresses stale durable publication without claiming quiescence", async () => {
  const firstSettlement = deferred<TestSettlement>();
  const secondSettlement = deferred<TestSettlement>();
  const harness = createHarness({
    invoke: input => input === "first"
      ? firstSettlement.promise
      : secondSettlement.promise,
    cancel: () => true,
  });
  await startReady(harness);
  const first = started(harness.session.start("first", harness.adapter));
  await harness.environment.flushAsync();
  const firstContext = harness.invocations[0]!.context;
  const second = started(harness.session.start("second", harness.adapter));
  await harness.environment.flushAsync();

  assert.deepEqual(await first.outcome, {
    kind: "canceled",
    reason: "superseded",
  });
  assert.equal(await promiseSettled(first.quiesced), false);
  assert.equal(firstContext.reportEvents([{
    kind: "durable",
    payload: { kind: "item", value: "stale" },
  }]), true);
  assert.equal(
    harness.events.some(
      event => event.kind === "durable"
        && event.durable.value.kind === "item"
        && event.durable.value.value === "stale",
    ),
    false,
  );

  firstSettlement.resolve({ kind: "canceled", reason: "superseded" });
  secondSettlement.resolve({ kind: "succeeded", value: "second" });
  await harness.environment.flushAsync();
  await first.quiesced;
  assert.deepEqual(await second.outcome, {
    kind: "succeeded",
    value: "second",
  });
  await second.quiesced;
});

test("observer failure cancels once and suppresses the rest of the batch", async () => {
  const settlement = deferred<TestSettlement>();
  const observerFailure = new Error("observer failed");
  const harness = createHarness({
    invoke: () => settlement.promise,
    cancel: () => true,
    onFeature: event => {
      if (event.kind === "durable") throw observerFailure;
    },
  });
  await startReady(harness);
  const handle = started(harness.session.start("input", harness.adapter));
  await harness.environment.flushAsync();

  assert.equal(harness.invocations[0]!.context.reportEvents([
    { kind: "durable", payload: { kind: "item", value: "first" } },
    { kind: "durable", payload: { kind: "item", value: "suppressed" } },
  ]), true);
  await harness.environment.flushAsync();

  assert.deepEqual(harness.events.map(eventLabel), [
    "started",
    "item:first",
  ]);
  assert.deepEqual(await handle.outcome, {
    kind: "canceled",
    reason: "feature-observer-failed",
  });
  assert.equal(harness.authorityDiagnostics[0]?.kind, "feature-observer");
  assert.equal(harness.authorityDiagnostics[0]?.error, observerFailure);
  assert.equal(await promiseSettled(handle.quiesced), false);

  settlement.resolve({
    kind: "canceled",
    reason: "feature-observer-failed",
  });
  await harness.environment.flushAsync();
  await handle.quiesced;
});

test("all event payloads decode before any entry publishes", async () => {
  const settlement = deferred<TestSettlement>();
  const harness = createHarness({ invoke: () => settlement.promise });
  await startReady(harness);
  const handle = started(harness.session.start("input", harness.adapter));
  await harness.environment.flushAsync();

  assert.equal(harness.invocations[0]!.context.reportEvents([
    { kind: "progress", payload: "would-have-published" },
    { kind: "durable", payload: { kind: "item", value: 42 } },
  ]), true);

  assert.deepEqual(harness.events.map(eventLabel), [
    "started",
    "failed:boundary:protocol",
  ]);
  assert.equal(harness.runtimeFailures[0]?.kind, "protocol");
  assert.equal(await promiseSettled(handle.quiesced), false);

  harness.environment.advanceActive(20);
  await handle.quiesced;
  assert.equal(harness.transports[0]!.terminated, true);
});

test("event batches before acceptance fail protocol state", async () => {
  const settlement = deferred<TestSettlement>();
  const harness = createHarness({ invoke: () => settlement.promise });
  await startReady(harness);
  const handle = started(harness.session.start("input", harness.adapter));

  harness.transports[0]!.emitToHost(eventsEnvelope(
    1,
    { operationId: handle.id, operationSequence: 1 },
    [{ kind: "progress", payload: "too-early" }],
  ));
  await harness.environment.flushAsync();

  assert.deepEqual(await handle.outcome, {
    kind: "failed",
    error: "boundary:protocol",
  });
  assert.equal(
    harness.events.some(event =>
      event.kind === "progress" && event.progress.value === "too-early"),
    false,
  );
});

test("an old Worker source cannot publish into a replacement epoch", async () => {
  const settlement = deferred<TestSettlement>();
  const harness = createHarness({
    workerCount: 2,
    invoke: () => settlement.promise,
  });
  await startReady(harness);
  const oldTransport = harness.transports[0]!;
  harness.host.restart();
  assert.equal(harness.host.start("replacement").kind, "started");
  await harness.environment.flushAsync();
  const handle = started(harness.session.start("input", harness.adapter));
  await harness.environment.flushAsync();

  harness.host.receiveMessage(oldTransport, eventsEnvelope(
    1,
    { operationId: handle.id, operationSequence: 1 },
    [{
      kind: "durable",
      payload: { kind: "item", value: "stale-epoch" },
    }],
  ));
  assert.equal(harness.invocations[0]!.context.reportEvents([{
    kind: "durable",
    payload: { kind: "item", value: "current-epoch" },
  }]), true);
  settlement.resolve({ kind: "succeeded", value: "done" });
  await harness.environment.flushAsync();

  assert.deepEqual(harness.events.map(eventLabel), [
    "started",
    "item:current-epoch",
    "succeeded:done",
  ]);
  assert.deepEqual(harness.runtimeFailures, []);
  await handle.quiesced;
});

test("a settled Worker invocation revokes its event reporter", async () => {
  const harness = createHarness({
    invoke: () => ({ kind: "succeeded", value: "done" }),
  });
  await startReady(harness);
  const handle = started(harness.session.start("input", harness.adapter));
  await harness.environment.flushAsync();
  await handle.quiesced;
  const eventCount = harness.events.length;
  const context = harness.invocations[0]!.context;

  assert.equal(context.reportEvents([{
    kind: "durable",
    payload: { kind: "item", value: "late" },
  }]), false);
  assert.equal(context.reportProgress("late-progress"), false);
  assert.equal(harness.events.length, eventCount);
});

test("event-channel send failure becomes a visible Worker boundary failure", async () => {
  const sendFailure = new Error("event post failed");
  const settlement = deferred<TestSettlement>();
  const harness = createHarness({
    failPost: (_worker, message) =>
      message.kind === "events" ? sendFailure : null,
    invoke: () => settlement.promise,
  });
  await startReady(harness);
  const handle = started(harness.session.start("input", harness.adapter));
  await harness.environment.flushAsync();

  assert.throws(() => {
    harness.invocations[0]!.context.reportEvents([{
      kind: "durable",
      payload: { kind: "item", value: "not-sent" },
    }]);
  }, sendFailure);
  assert.deepEqual(await handle.outcome, {
    kind: "failed",
    error: "boundary:worker-declared",
  });
  assert.equal(harness.runtimeFailures[0]?.kind, "worker-declared");
  assert.equal(
    harness.transports[0]!.postedToMain.some(message =>
      message.kind === "events"),
    false,
  );
  assert.equal(await promiseSettled(handle.quiesced), false);
  harness.environment.advanceActive(20);
  await handle.quiesced;
});

for (const invalidBatch of [
  {
    name: "empty",
    entries: [] as readonly WorkerNonterminalEvent<unknown, unknown>[],
  },
  {
    name: "oversized",
    entries: Array.from(
      { length: WORKER_RUNTIME_MAX_EVENT_BATCH_SIZE + 1 },
      () => ({ kind: "progress", payload: "too-many" }) as const,
    ),
  },
]) {
  test(`${invalidBatch.name} outgoing event batches fail the Worker boundary`, async () => {
    const harness = createHarness({
      invoke: (_input, context) => {
        assert.throws(
          () => context.reportEvents(invalidBatch.entries),
          { message: "Worker event batch was rejected." },
        );
        return { kind: "succeeded", value: "unreachable" };
      },
    });
    await startReady(harness);
    const handle = started(harness.session.start("input", harness.adapter));
    await harness.environment.flushAsync();

    assert.deepEqual(await handle.outcome, {
      kind: "failed",
      error: "boundary:worker-declared",
    });
    assert.equal(harness.runtimeFailures[0]?.kind, "worker-declared");
    assert.equal(
      harness.transports[0]!.postedToMain.some(message =>
        message.kind === "events"),
      false,
    );
    harness.environment.advanceActive(20);
    await handle.quiesced;
  });
}

test("decoder reentrancy queues later progress and settlement behind the batch", async () => {
  const settlement = deferred<TestSettlement>();
  const contextRef: { current: WorkerOperationContext | null } = {
    current: null,
  };
  let reentered = false;
  const harness = createHarness({
    durableDecoder: durableDecoder(() => {
      if (reentered) return;
      reentered = true;
      assert.equal(contextRef.current?.reportProgress("decoder-later"), true);
      settlement.resolve({ kind: "succeeded", value: "done" });
    }),
    invoke: (_input, current) => {
      contextRef.current = current;
      return settlement.promise;
    },
  });
  await startReady(harness);
  const handle = started(harness.session.start("input", harness.adapter));
  await harness.environment.flushAsync();

  assert.equal(contextRef.current?.reportEvents([
    { kind: "progress", payload: "batch-first" },
    { kind: "durable", payload: { kind: "item", value: "batch-second" } },
  ]), true);
  await harness.environment.flushAsync();

  assert.deepEqual(harness.events.map(eventLabel), [
    "started",
    "progress:batch-first",
    "item:batch-second",
    "progress:decoder-later",
    "succeeded:done",
  ]);
  await handle.quiesced;
});

for (const closure of ["restart", "dispose"] as const) {
  test(`immediate observer ${closure} stops remaining batch handoffs`, async () => {
    const settlement = deferred<TestSettlement>();
    let host: WorkerRuntimeHost<string, TestDiagnostic> | null = null;
    const harness = createHarness({
      invoke: () => settlement.promise,
      onFeature: event => {
        if (event.kind === "durable") host?.[closure]();
      },
    });
    host = harness.host;
    await startReady(harness);
    const handle = started(harness.session.start("input", harness.adapter));
    await harness.environment.flushAsync();

    assert.equal(harness.invocations[0]!.context.reportEvents([
      { kind: "durable", payload: { kind: "item", value: "first" } },
      { kind: "progress", payload: "second" },
      { kind: "durable", payload: { kind: "item", value: "third" } },
    ]), true);

    assert.deepEqual(harness.events.map(eventLabel), [
      "started",
      "item:first",
      "canceled:worker-restarted",
    ]);
    assert.deepEqual(await handle.outcome, {
      kind: "canceled",
      reason: "worker-restarted",
    });
    await handle.quiesced;
    assert.equal(harness.transports[0]!.terminated, true);
    assert.deepEqual(harness.authorityDiagnostics, []);
    assert.deepEqual(harness.runtimeFailures, []);
  });
}

test("reentrant restart follows already queued batch work", async () => {
  const settlement = deferred<TestSettlement>();
  const contextRef: { current: WorkerOperationContext | null } = {
    current: null,
  };
  let host: WorkerRuntimeHost<string, TestDiagnostic> | null = null;
  let decoderReentered = false;
  let restartRequested = false;
  const harness = createHarness({
    durableDecoder: durableDecoder(() => {
      if (decoderReentered) return;
      decoderReentered = true;
      assert.equal(
        contextRef.current?.reportProgress("queued-before-restart"),
        true,
      );
    }),
    invoke: (_input, current) => {
      contextRef.current = current;
      return settlement.promise;
    },
    onFeature: event => {
      if (!restartRequested && event.kind === "durable") {
        restartRequested = true;
        host?.restart();
      }
    },
  });
  host = harness.host;
  await startReady(harness);
  const handle = started(harness.session.start("input", harness.adapter));
  await harness.environment.flushAsync();

  assert.equal(contextRef.current?.reportEvents([
    { kind: "progress", payload: "batch-first" },
    { kind: "durable", payload: { kind: "item", value: "batch-second" } },
    { kind: "durable", payload: { kind: "item", value: "batch-third" } },
  ]), true);

  assert.deepEqual(harness.events.map(eventLabel), [
    "started",
    "progress:batch-first",
    "item:batch-second",
    "item:batch-third",
    "progress:queued-before-restart",
    "canceled:worker-restarted",
  ]);
  assert.deepEqual(await handle.outcome, {
    kind: "canceled",
    reason: "worker-restarted",
  });
  await handle.quiesced;
});
