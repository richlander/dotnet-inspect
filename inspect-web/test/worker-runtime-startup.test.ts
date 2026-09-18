import assert from "node:assert/strict";
import test from "node:test";
import {
  FakeWorkerOperationCatalog,
  FakeWorkerRuntime,
  ManualWorkerRuntimeEnvironment,
  QueueWorkerRuntimeTransportFactory,
  WorkerEpochTokenAllocator,
  WorkerProducerClassRegistry,
  WorkerRuntimeHost,
  type WorkerRuntimeFailure,
  type WorkerRuntimeTransportBinding,
} from "../src/worker-runtime-core.ts";
import { type WorkerLivenessAllowance } from "../src/worker-runtime-protocol.ts";

import {
  type TestDiagnostic,
  type TestSettlement,
  type TestHarness,
  deferred,
  stringDecoder,
  diagnosticDecoder,
  createHarness,
  session,
  started,
  workerEnvelope,
  operationMessages,
  startReady,
} from "./worker-runtime-test-fixture.ts";

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

test("creation-time disposal retains subscriptions after failed unowned termination", () => {
  const terminationError = new Error("unowned termination failed");
  const clockError = new Error("clock cleanup must remain deferred");
  const lifecycleError = new Error("lifecycle cleanup must remain deferred");
  let harness: TestHarness;
  harness = createHarness({
    create: () => {
      harness.host.dispose();
    },
    terminate: () => {
      throw terminationError;
    },
    clockUnsubscribeError: clockError,
    lifecycleUnsubscribeError: lifecycleError,
  });

  assert.deepEqual(harness.host.start("bootstrap"), {
    kind: "rejected",
    reason: "host-disposed",
  });
  assert.equal(harness.workers[0]!.terminated, false);
  assert.deepEqual(
    harness.runtimeDiagnostics.map(diagnostic => diagnostic.detail),
    [terminationError],
  );
});

test("disposal during throwing creation remains the authoritative result", () => {
  const creationError = new Error("creation failed after disposal");
  let harness: TestHarness;
  harness = createHarness({
    create: () => {
      harness.host.dispose();
      throw creationError;
    },
  });

  assert.deepEqual(harness.host.start("bootstrap"), {
    kind: "rejected",
    reason: "host-disposed",
    detail: creationError,
  });
  assert.equal(harness.failures[0]?.kind, "startup");
  assert.equal(harness.failures[0]?.diagnostic.detail, creationError);
  assert.deepEqual(harness.host.start("later"), {
    kind: "rejected",
    reason: "host-disposed",
  });
});

test("bind-time Ready cannot bypass initialization dispatch", () => {
  const detachError = new Error("bind-time detach failed");
  let releasedBeforeDetach = false;
  let terminatedAtDetachDiagnostic = false;
  let releasedAtDetachDiagnostic = false;
  let harness: TestHarness;
  harness = createHarness({
    bindMessage: workerEnvelope(1, {
      kind: "ready",
      idleHeartbeatIntervalMilliseconds: 10,
    }),
    detachError,
    detach: () => {
      releasedBeforeDetach = harness.releasedEpochs.includes(1);
    },
    diagnostic: diagnostic => {
      if (diagnostic.detail !== detachError) return;
      terminatedAtDetachDiagnostic = harness.workers[0]!.terminated;
      releasedAtDetachDiagnostic = harness.releasedEpochs.includes(1);
    },
  });

  assert.deepEqual(harness.host.start("bootstrap"), {
    kind: "rejected",
    reason: "worker-creation-failed",
  });
  assert.equal(harness.failures[0]?.kind, "protocol");
  assert.equal(harness.host.snapshot().phase, "closed");
  assert.deepEqual(harness.workers[0]!.receivedMessages, []);
  assert.equal(harness.workers[0]!.terminateCount, 1);
  assert.equal(releasedBeforeDetach, false);
  assert.equal(terminatedAtDetachDiagnostic, true);
  assert.equal(releasedAtDetachDiagnostic, false);
  assert.deepEqual(harness.releasedEpochs, [1]);
});

test("synchronous initialization failure cannot return a started epoch", () => {
  const leaseAllowance: WorkerLivenessAllowance = {
    kind: "bounded",
    maxSilentActiveMilliseconds: 30,
  };
  const harness = createHarness({
    synchronousInitializeMessages: epochToken => [
      workerEnvelope(epochToken, {
        kind: "ready",
        idleHeartbeatIntervalMilliseconds: 10,
      }),
      workerEnvelope(epochToken, {
        kind: "epoch-work-started",
        workSequence: 1,
        allowance: leaseAllowance,
      }),
      workerEnvelope(epochToken, {
        kind: "epoch-failed",
        diagnostic: { code: "worker", detail: "initialization failed" },
      }),
    ],
  });

  assert.deepEqual(harness.host.start("bootstrap"), {
    kind: "rejected",
    reason: "worker-creation-failed",
  });
  assert.equal(harness.failures[0]?.kind, "worker-declared");
  assert.equal(harness.host.snapshot().phase, "draining");
  assert.equal(harness.host.snapshot().activeEpochWork, 1);
  assert.equal(harness.workers[0]!.terminateCount, 0);

  harness.workers[0]!.emitRaw(workerEnvelope(1, {
    kind: "epoch-work-finished",
    workSequence: 1,
  }));
  assert.equal(harness.host.snapshot().phase, "closed");
  assert.equal(harness.workers[0]!.terminateCount, 1);
  assert.deepEqual(harness.releasedEpochs, [1]);
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
