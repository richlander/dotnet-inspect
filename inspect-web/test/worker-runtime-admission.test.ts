import assert from "node:assert/strict";
import test from "node:test";
import {
  createOperationAuthorityPage,
  type OperationFeatureEvent,
  type OperationHandle,
  type OperationPreparation,
  type OperationProducerAdapter,
  type OperationProducerSink,
} from "../src/operation-authority.ts";
import {
  FakeWorkerOperationCatalog,
  FakeWorkerRuntime,
  ManualWorkerRuntimeEnvironment,
  QueueWorkerRuntimeTransportFactory,
  WorkerProducerClassRegistry,
  WorkerRuntimeHost,
  type WorkerRuntimeFailure,
  type WorkerRuntimePreparationError,
} from "../src/worker-runtime-core.ts";
import {
  type ManagedOperationSettlement,
  WORKER_RUNTIME_PROTOCOL_VERSION,
} from "../src/worker-runtime-protocol.ts";

import {
  type TestDiagnostic,
  type TestSettlement,
  uncanceledOperation,
  type TestHarness,
  deferred,
  stringDecoder,
  terminalCallbacks,
  boundaryErrors,
  diagnosticDecoder,
  recordDecoder,
  createHarness,
  session,
  started,
  captureIdentities,
  captureIdentity,
  preparedBinding,
  workerEnvelope,
  operationMessages,
  ownDataProperty,
  postWorker,
  startReady,
} from "./worker-runtime-test-fixture.ts";

test("preparation rejects synchronously without posting or retaining a sink", () => {
  const harness = createHarness();
  const identity = captureIdentity();
  const calls: string[] = [];
  const sink: OperationProducerSink<string, string, string> = {
    reportProgress: () => {
      calls.push("progress");
      return undefined;
    },
    reportDurable: () => undefined,
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
  assert.deepEqual(
    harness.adapter.prepare(identity, "input", sink, uncanceledOperation),
    {
      kind: "rejected",
      error: { kind: "epoch-unavailable" },
    },
  );
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
    reportDurable: () => undefined,
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
    harness.adapter.prepare(identity!, "input", sink, uncanceledOperation),
  );
  abandoned.abandon();
  abandoned.activate();
  assert.deepEqual(operationMessages(harness.workers[0]!), ["initialize"]);
  assert.deepEqual(calls, []);

  const activated = preparedBinding(
    harness.adapter.prepare(
      secondIdentity!,
      "input",
      sink,
      uncanceledOperation,
    ),
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

test("readiness flush resumes held starts in order after every response yield", async () => {
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
  const page = createOperationAuthorityPage({
    allocation: {
      createId: (() => {
        let id = 1;
        return () => `yielded-flush-${id++}`;
      })(),
    },
  });
  const handles = Array.from({ length: 4 }, (_, index) => {
    const operationSession = session(harness.adapter, page);
    return started(
      operationSession.session.start(`input-${index}`, harness.adapter),
    );
  });

  bootstrap.resolve(undefined);
  await harness.environment.flushAsync();

  const starts = harness.workers[0]!.receivedMessages.flatMap(message => {
    if (typeof message !== "object" || message === null) return [];
    return ownDataProperty(message, "kind") === "start"
      ? [ownDataProperty(message, "operation")]
      : [];
  });
  assert.deepEqual(starts, [
    { operationId: "yielded-flush-1", operationSequence: 1 },
    { operationId: "yielded-flush-2", operationSequence: 2 },
    { operationId: "yielded-flush-3", operationSequence: 3 },
    { operationId: "yielded-flush-4", operationSequence: 4 },
  ]);
  assert.equal(harness.host.snapshot().phase, "ready");
  assert.equal(harness.host.snapshot().heldOperations, 0);
  assert.equal(harness.host.snapshot().activeOperations, 4);

  settlement.resolve({ kind: "succeeded", value: "output" });
  await harness.environment.flushAsync();
  await Promise.all(handles.map(handle => handle.quiesced));
});

test("readiness-flush allowance mismatch drains through later settlement", async () => {
  const bootstrap = deferred<void>();
  const settlement = deferred<TestSettlement>();
  const harness = createHarness({
    bootstrap: () => bootstrap.promise,
    invoke: () => settlement.promise,
    omitResponse: kind => kind === "accepted",
    synchronousAcceptedAllowance: {
      kind: "bounded",
      maxSilentActiveMilliseconds: 19,
    },
  });
  assert.equal(harness.host.start("bootstrap").kind, "started");
  harness.environment.flushTasks();
  const operationSession = session(harness.adapter);
  const handle = started(
    operationSession.session.start("input", harness.adapter),
  );

  bootstrap.resolve(undefined);
  await harness.environment.flushAsync();

  assert.equal(harness.failures[0]?.kind, "protocol");
  assert.equal(harness.host.snapshot().phase, "draining");
  assert.equal(harness.workers[0]!.terminateCount, 0);
  settlement.resolve({ kind: "succeeded", value: "late" });
  await harness.environment.flushAsync();
  assert.equal(harness.host.snapshot().phase, "closed");
  assert.equal(harness.workers[0]!.terminateCount, 1);
  assert.deepEqual(harness.releasedEpochs, [1]);
  assert.deepEqual(await handle.outcome, {
    kind: "failed",
    error: "boundary:protocol",
  });
  await handle.quiesced;
});

test("readiness-flush worker error uses bounded post-readiness draining", async () => {
  const bootstrap = deferred<void>();
  const settlement = deferred<TestSettlement>();
  const workerError = new Error("worker event during flush");
  const harness = createHarness({
    bootstrap: () => bootstrap.promise,
    invoke: () => settlement.promise,
    synchronousStartError: workerError,
  });
  assert.equal(harness.host.start("bootstrap").kind, "started");
  harness.environment.flushTasks();
  const operationSession = session(harness.adapter);
  const handle = started(
    operationSession.session.start("input", harness.adapter),
  );

  bootstrap.resolve(undefined);
  await harness.environment.flushAsync();

  assert.equal(harness.failures[0]?.kind, "worker-message");
  assert.equal(harness.host.snapshot().phase, "draining");
  assert.equal(harness.workers[0]!.terminateCount, 0);
  settlement.resolve({ kind: "succeeded", value: "late" });
  await harness.environment.flushAsync();
  assert.equal(harness.host.snapshot().phase, "closed");
  assert.equal(harness.workers[0]!.terminateCount, 1);
  assert.deepEqual(harness.releasedEpochs, [1]);
  assert.deepEqual(await handle.outcome, {
    kind: "failed",
    error: "boundary:worker-message",
  });
  await handle.quiesced;
});

test("readiness-flush failure releases later never-posted held starts", async () => {
  const bootstrap = deferred<void>();
  const settlement = deferred<TestSettlement>();
  const harness = createHarness({
    bootstrap: () => bootstrap.promise,
    invoke: () => settlement.promise,
    synchronousStartError: new Error("flush failed"),
  });
  assert.equal(harness.host.start("bootstrap").kind, "started");
  harness.environment.flushTasks();
  const authority = createOperationAuthorityPage({
    allocation: {
      createId: (() => {
        let id = 1;
        return () => `flush-failure-${id++}`;
      })(),
    },
  });
  const first = session(harness.adapter, authority);
  const second = session(harness.adapter, authority);
  const firstHandle = started(
    first.session.start("first", harness.adapter),
  );
  const secondHandle = started(
    second.session.start("second", harness.adapter),
  );

  bootstrap.resolve(undefined);
  await harness.environment.flushAsync();

  assert.deepEqual(operationMessages(harness.workers[0]!), [
    "initialize",
    "start",
  ]);
  assert.equal(harness.host.snapshot().heldOperations, 0);
  assert.equal(harness.host.snapshot().activeOperations, 1);
  assert.deepEqual(await secondHandle.outcome, {
    kind: "failed",
    error: "boundary:worker-message",
  });
  await secondHandle.quiesced;

  settlement.resolve({ kind: "succeeded", value: "late" });
  await harness.environment.flushAsync();
  assert.equal(harness.host.snapshot().phase, "closed");
  assert.deepEqual(await firstHandle.outcome, {
    kind: "failed",
    error: "boundary:worker-message",
  });
  await firstHandle.quiesced;
});

test("matching Ready ends the startup deadline before held-start flush", async () => {
  const bootstrap = deferred<void>();
  const settlement = deferred<TestSettlement>();
  const harness = createHarness({
    bootstrap: () => bootstrap.promise,
    invoke: () => settlement.promise,
    startupBudgetMilliseconds: 10,
    synchronousStartActiveAdvanceMilliseconds: 10,
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
  assert.equal(harness.failures.length, 0);
  assert.equal(harness.workers[0]!.terminateCount, 0);
  settlement.resolve({ kind: "succeeded", value: "output" });
  await harness.environment.flushAsync();
  assert.deepEqual(await handle.outcome, {
    kind: "succeeded",
    value: "output",
  });
  await handle.quiesced;
});

for (const scenario of [
  { name: "expired grace", elapsed: 5, accepted: false, probe: true },
  { name: "unexpired grace", elapsed: 4, accepted: false, probe: false },
  { name: "retired response", elapsed: 5, accepted: true, probe: false },
] as const) {
  test(`readiness flush evaluates ${scenario.name} without aging the watchdog`, async () => {
    const bootstrap = deferred<void>();
    const settlement = deferred<TestSettlement>();
    const harness = createHarness({
      bootstrap: () => bootstrap.promise,
      invoke: () => settlement.promise,
      controlResponseGraceMilliseconds: 5,
      synchronousStartActiveAdvanceMilliseconds: scenario.elapsed,
      synchronousAccepted: scenario.accepted,
      omitResponse: kind => kind === "accepted"
        || kind === "probe-acknowledged",
    });
    assert.equal(harness.host.start("bootstrap").kind, "started");
    harness.environment.flushTasks();
    const operationSession = session(harness.adapter);
    const handle = started(
      operationSession.session.start("input", harness.adapter),
    );

    bootstrap.resolve(undefined);
    await harness.environment.flushAsync();

    assert.deepEqual(operationMessages(harness.workers[0]!), scenario.probe
      ? ["initialize", "start", "probe"]
      : ["initialize", "start"]);
    assert.equal(harness.host.snapshot().phase, "ready");
    assert.equal(harness.host.snapshot().lastTaskEvidenceOrigin, scenario.elapsed);
    assert.equal(harness.host.snapshot().outstandingProbeSequence,
      scenario.probe ? 1 : null);
    assert.equal(harness.failures.length, 0);

    if (scenario.accepted) {
      settlement.resolve({ kind: "succeeded", value: "output" });
      await harness.environment.flushAsync();
      assert.deepEqual(await handle.outcome, {
        kind: "succeeded",
        value: "output",
      });
    } else {
      if (!scenario.probe) {
        harness.environment.advanceActive(1);
        await harness.environment.flushAsync();
        assert.equal(harness.host.snapshot().outstandingProbeSequence, 1);
      }
      harness.workers[0]!.emitRaw(workerEnvelope(1, {
        kind: "probe-acknowledged",
        probeSequence: 1,
      }));
      assert.deepEqual(await handle.outcome, {
        kind: "failed",
        error: "boundary:control-response",
      });
      harness.host.restart();
    }
    await handle.quiesced;
  });
}

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
      reportDurable: () => undefined,
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
    preparation = harness.adapter.prepare(
      identity,
      "input",
      sink,
      uncanceledOperation,
    );
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
    reportDurable: () => undefined,
    ...terminalCallbacks(() => undefined),
    reportUnexpectedTerminal: () => undefined,
    reportQuiesced: () => undefined,
    reportUnexpectedFailure: () => undefined,
  };
  const binding = preparedBinding(
    harness.adapter.prepare(
      captureIdentity(),
      "input",
      sink,
      uncanceledOperation,
    ),
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
    reportDurable: () => undefined,
    ...terminalCallbacks(() => undefined),
    reportUnexpectedTerminal: () => undefined,
    reportQuiesced: () => undefined,
    reportUnexpectedFailure: () => undefined,
  };
  assert.deepEqual(
    harness.adapter.prepare(
      firstEvent.operation,
      "replay",
      sink,
      uncanceledOperation,
    ),
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
    harness.adapter.prepare(
      firstEvent.operation,
      "replay",
      sink,
      uncanceledOperation,
    ),
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
