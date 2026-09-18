import assert from "node:assert/strict";
import test from "node:test";
import {
  createOperationAuthorityPage,
  type OperationHandle,
  type OperationProducerSink,
} from "../src/operation-authority.ts";
import {
  WorkerProbeSequenceAllocator,
  type WorkerRuntimePreparationError,
} from "../src/worker-runtime-core.ts";
import { WORKER_RUNTIME_PROTOCOL_VERSION } from "../src/worker-runtime-protocol.ts";

import {
  type TestWorker,
  type TestSettlement,
  uncanceledOperation,
  type TestHarness,
  deferred,
  terminalCallbacks,
  createHarness,
  session,
  started,
  captureIdentity,
  preparedBinding,
  workerEnvelope,
  operationMessages,
  postWorker,
  startReady,
} from "./worker-runtime-test-fixture.ts";

test("matching probe acknowledgment proves a covered missing Control response", async () => {
  const settlement = deferred<TestSettlement>();
  const harness = createHarness({
    invoke: () => settlement.promise,
    omitResponse: kind => kind === "control-acknowledged",
    controlResponseGraceMilliseconds: 5,
  });
  await startReady(harness);
  const operationSession = session(harness.adapter);
  const handle = started(
    operationSession.session.start("input", harness.adapter),
  );
  await harness.environment.flushAsync();

  const pending = harness.adapter.requestControl(handle.id, "more");
  await harness.environment.flushAsync();
  harness.environment.advanceActive(5);
  await harness.environment.flushAsync();

  assert.equal(harness.failures[0]?.kind, "control-response");
  assert.deepEqual(await pending, {
    kind: "failed",
    error: "control-boundary:control-response",
  });
  assert.deepEqual(await handle.outcome, {
    kind: "failed",
    error: "boundary:control-response",
  });
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
  const firstHandle = started(
    first.session.start("first", harness.adapter),
  );
  await harness.environment.flushAsync();
  harness.environment.advanceActive(20);
  await harness.environment.flushAsync();
  assert.equal(harness.host.snapshot().outstandingProbeSequence, 1);
  harness.workers[0]!.emitHeartbeat();
  assert.equal(harness.host.snapshot().outstandingProbeSequence, 1);
  assert.equal(harness.failures.length, 0);

  const second = session(harness.adapter, authority);
  const secondHandle = started(
    second.session.start("second", harness.adapter),
  );
  await harness.environment.flushAsync();
  assert.equal(harness.failures[0]?.kind, "control-response");
  assert.equal(harness.host.snapshot().phase, "draining");
  assert.deepEqual(await firstHandle.outcome, {
    kind: "failed",
    error: "boundary:control-response",
  });
  assert.deepEqual(await secondHandle.outcome, {
    kind: "failed",
    error: "boundary:control-response",
  });
  settlements[1]!.resolve({ kind: "succeeded", value: "second" });
  await harness.environment.flushAsync();
  assert.equal(harness.host.snapshot().phase, "draining");
  settlements[0]!.resolve({ kind: "succeeded", value: "first" });
  await harness.environment.flushAsync();
  assert.equal(harness.host.snapshot().phase, "closed");
  assert.equal(harness.workers[0]!.terminateCount, 1);
  assert.deepEqual(harness.releasedEpochs, [1]);
  await Promise.all([firstHandle.quiesced, secondHandle.quiesced]);
});

test("a response proving a missing probe retains reentrant physical evidence", async () => {
  const settlements = [
    deferred<TestSettlement>(),
    deferred<TestSettlement>(),
  ];
  let invocation = 0;
  let accepted = 0;
  let secondHandle: OperationHandle<string, string> | null = null;
  let harness: TestHarness;
  harness = createHarness({
    invoke: () => settlements[invocation++]!.promise,
    omitResponse: kind => {
      if (kind === "probe-acknowledged") return true;
      if (kind !== "accepted") return false;
      accepted++;
      return accepted === 2;
    },
    controlResponseGraceMilliseconds: 5,
    failure: () => {
      const current = secondHandle;
      assert.notEqual(current, null);
      if (current === null) return;
      harness.workers[0]!.emitRaw(workerEnvelope(1, {
        kind: "settled",
        operation: {
          operationId: current.id,
          operationSequence: 2,
        },
        settlement: { kind: "succeeded", value: "physical" },
      }));
    },
  });
  await startReady(harness);
  const authority = createOperationAuthorityPage({
    allocation: {
      createId: (() => {
        let id = 1;
        return () => `reentrant-response-${id++}`;
      })(),
    },
  });
  const first = session(harness.adapter, authority);
  const firstHandle = started(
    first.session.start("first", harness.adapter),
  );
  await harness.environment.flushAsync();
  harness.environment.advanceActive(20);
  await harness.environment.flushAsync();
  assert.equal(harness.host.snapshot().outstandingProbeSequence, 1);

  const second = session(harness.adapter, authority);
  secondHandle = started(
    second.session.start("second", harness.adapter),
  );
  await harness.environment.flushAsync();
  harness.workers[0]!.emitRaw(workerEnvelope(1, {
    kind: "accepted",
    operation: {
      operationId: secondHandle.id,
      operationSequence: 2,
    },
    allowance: { kind: "bounded", maxSilentActiveMilliseconds: 20 },
  }));

  assert.equal(harness.failures[0]?.kind, "control-response");
  assert.equal(harness.host.snapshot().phase, "draining");
  assert.equal(harness.host.snapshot().activeOperations, 1);
  assert.deepEqual(await secondHandle.outcome, {
    kind: "failed",
    error: "boundary:control-response",
  });
  await secondHandle.quiesced;

  harness.workers[0]!.emitRaw(workerEnvelope(1, {
    kind: "settled",
    operation: {
      operationId: firstHandle.id,
      operationSequence: 1,
    },
    settlement: { kind: "succeeded", value: "physical" },
  }));
  assert.equal(harness.host.snapshot().phase, "closed");
  await firstHandle.quiesced;
});

test("response FIFO does not let a probe acknowledgment overtake acceptance", async () => {
  const settlements = [
    deferred<TestSettlement>(),
    deferred<TestSettlement>(),
  ];
  let invocation = 0;
  let accepted = 0;
  let secondHandle: OperationHandle<string, string> | null = null;
  let harness: TestHarness;
  harness = createHarness({
    invoke: () => settlements[invocation++]!.promise,
    omitResponse: kind => {
      if (kind === "probe-acknowledged") return true;
      if (kind !== "accepted") return false;
      accepted++;
      return accepted === 2;
    },
    controlResponseGraceMilliseconds: 5,
  });
  await startReady(harness);
  const page = createOperationAuthorityPage({
    allocation: {
      createId: (() => {
        let id = 1;
        return () => `response-fifo-${id++}`;
      })(),
    },
  });
  const firstSession = page.createSession<
    string,
    string,
    string,
    string,
    WorkerRuntimePreparationError
  >({
    feature: {
      publish: event => {
        if (event.kind !== "terminal") return undefined;
        const second = secondHandle;
        assert.notEqual(second, null);
        if (second === null) return undefined;
        harness.workers[0]!.emitRaw(workerEnvelope(1, {
          kind: "accepted",
          operation: {
            operationId: second.id,
            operationSequence: 2,
          },
          allowance: {
            kind: "bounded",
            maxSilentActiveMilliseconds: 20,
          },
        }));
        harness.workers[0]!.emitRaw(workerEnvelope(1, {
          kind: "probe-acknowledged",
          probeSequence: 1,
        }));
        return undefined;
      },
    },
    diagnostic: { report: () => undefined },
  });
  const secondSession = page.createSession<
    string,
    string,
    string,
    string,
    WorkerRuntimePreparationError
  >({
    feature: { publish: () => undefined },
    diagnostic: { report: () => undefined },
  });
  const firstHandle = started(
    firstSession.start("first", harness.adapter),
  );
  secondHandle = started(
    secondSession.start("second", harness.adapter),
  );
  await harness.environment.flushAsync();
  harness.environment.advanceActive(5);
  await harness.environment.flushAsync();
  assert.equal(harness.host.snapshot().outstandingProbeSequence, 1);

  settlements[0]!.resolve({ kind: "succeeded", value: "first" });
  await harness.environment.flushAsync();

  assert.equal(harness.failures.length, 0);
  assert.equal(harness.host.snapshot().outstandingProbeSequence, null);
  assert.equal(harness.host.snapshot().activeOperations, 1);
  assert.deepEqual(await firstHandle.outcome, {
    kind: "succeeded",
    value: "first",
  });
  await firstHandle.quiesced;

  settlements[1]!.resolve({ kind: "succeeded", value: "second" });
  await harness.environment.flushAsync();
  assert.deepEqual(await secondHandle.outcome, {
    kind: "succeeded",
    value: "second",
  });
  await secondHandle.quiesced;
});

test("operation response replay preserves FIFO across nested callbacks", async () => {
  const settlements = [
    deferred<TestSettlement>(),
    deferred<TestSettlement>(),
    deferred<TestSettlement>(),
  ];
  let invocation = 0;
  let secondHandle: OperationHandle<string, string> | null = null;
  let thirdHandle: OperationHandle<string, string> | null = null;
  let harness: TestHarness;
  harness = createHarness({
    invoke: () => settlements[invocation++]!.promise,
    omitResponse: kind => kind === "accepted",
  });
  await startReady(harness);
  const page = createOperationAuthorityPage({
    allocation: {
      createId: (() => {
        let id = 1;
        return () => `multiplexed-response-${id++}`;
      })(),
    },
  });
  const firstSession = page.createSession<
    string,
    string,
    string,
    string,
    WorkerRuntimePreparationError
  >({
    feature: {
      publish: event => {
        if (event.kind !== "terminal") return undefined;
        const second = secondHandle;
        const third = thirdHandle;
        assert.notEqual(second, null);
        assert.notEqual(third, null);
        if (second === null || third === null) return undefined;
        harness.workers[0]!.emitRaw(workerEnvelope(1, {
          kind: "rejected",
          operation: {
            operationId: second.id,
            operationSequence: 2,
          },
          error: "second-rejected",
          diagnostic: { code: "rejected", detail: "second" },
        }));
        harness.workers[0]!.emitRaw(workerEnvelope(1, {
          kind: "accepted",
          operation: {
            operationId: third.id,
            operationSequence: 3,
          },
          allowance: {
            kind: "bounded",
            maxSilentActiveMilliseconds: 20,
          },
        }));
        return undefined;
      },
    },
    diagnostic: { report: () => undefined },
  });
  const secondSession = page.createSession<
    string,
    string,
    string,
    string,
    WorkerRuntimePreparationError
  >({
    feature: {
      publish: event => {
        if (event.kind !== "terminal") return undefined;
        const third = thirdHandle;
        assert.notEqual(third, null);
        if (third === null) return undefined;
        harness.workers[0]!.emitRaw(workerEnvelope(1, {
          kind: "settled",
          operation: {
            operationId: third.id,
            operationSequence: 3,
          },
          settlement: { kind: "succeeded", value: "third" },
        }));
        return undefined;
      },
    },
    diagnostic: { report: () => undefined },
  });
  const thirdSession = page.createSession<
    string,
    string,
    string,
    string,
    WorkerRuntimePreparationError
  >({
    feature: { publish: () => undefined },
    diagnostic: { report: () => undefined },
  });
  const firstHandle = started(
    firstSession.start("first", harness.adapter),
  );
  secondHandle = started(
    secondSession.start("second", harness.adapter),
  );
  thirdHandle = started(
    thirdSession.start("third", harness.adapter),
  );
  await harness.environment.flushAsync();

  harness.workers[0]!.emitRaw(workerEnvelope(1, {
    kind: "rejected",
    operation: {
      operationId: firstHandle.id,
      operationSequence: 1,
    },
    error: "first-rejected",
    diagnostic: { code: "rejected", detail: "first" },
  }));

  assert.equal(harness.failures.length, 0);
  assert.equal(harness.host.snapshot().phase, "ready");
  assert.equal(harness.host.snapshot().activeOperations, 0);
  assert.deepEqual(await firstHandle.outcome, {
    kind: "failed",
    error: "first-rejected",
  });
  await firstHandle.quiesced;
  assert.deepEqual(await secondHandle.outcome, {
    kind: "failed",
    error: "second-rejected",
  });
  await secondHandle.quiesced;
  assert.deepEqual(await thirdHandle.outcome, {
    kind: "succeeded",
    value: "third",
  });
  await thirdHandle.quiesced;
});

const sourceFailureScenarios = [
  {
    name: "malformed current-source data",
    kind: "protocol",
    send: (worker: TestWorker) => {
      worker.emitRaw({ malformed: true });
    },
  },
  {
    name: "worker error",
    kind: "worker-message",
    send: (worker: TestWorker) => {
      worker.emitError("worker error");
    },
  },
  {
    name: "worker messageerror",
    kind: "worker-message",
    send: (worker: TestWorker) => {
      worker.emitMessageError("worker messageerror");
    },
  },
] as const;

for (const sourceFailure of sourceFailureScenarios) {
  test(`${sourceFailure.name} follows an earlier reentrant progress callback through the source-event FIFO`, async () => {
    const settlement = deferred<TestSettlement>();
    const order: string[] = [];
    let quiesced = false;
    let harness: TestHarness;
    harness = createHarness({
      invoke: () => settlement.promise,
      failure: failure => {
        order.push(`runtime-failure:${failure.kind}`);
      },
    });
    await startReady(harness);
    const identity = captureIdentity();
    const sink: OperationProducerSink<string, string, string> = {
      reportProgress: () => {
        order.push("progress-enter");
        sourceFailure.send(harness.workers[0]!);
        order.push("progress-exit");
        return undefined;
      },
      reportDurable: () => undefined,
      reportUnexpectedTerminal: () => undefined,
      reportUnexpectedFailure: () => undefined,
      ...terminalCallbacks<string, string>(outcome => {
        assert.deepEqual(outcome, {
          kind: "failed",
          error: `boundary:${sourceFailure.kind}`,
        });
        order.push(`terminal:${sourceFailure.kind}`);
        return undefined;
      }),
      reportQuiesced: () => {
        quiesced = true;
        return undefined;
      },
    };
    preparedBinding(
      harness.adapter.prepare(identity, "input", sink, uncanceledOperation),
    ).activate();
    await harness.environment.flushAsync();

    harness.workers[0]!.emitRaw(workerEnvelope(1, {
      kind: "progress",
      operation: {
        operationId: identity.id,
        operationSequence: identity.sequence,
      },
      payload: "progress",
    }));

    assert.deepEqual(order, [
      "progress-enter",
      "progress-exit",
      `terminal:${sourceFailure.kind}`,
      `runtime-failure:${sourceFailure.kind}`,
    ]);

    settlement.resolve({ kind: "succeeded", value: "physical" });
    await harness.environment.flushAsync();
    assert.equal(harness.host.snapshot().phase, "closed");
    assert.equal(harness.workers[0]!.terminateCount, 1);
    assert.deepEqual(harness.releasedEpochs, [1]);
    assert.equal(quiesced, true);
  });
}

for (const sourceFailure of sourceFailureScenarios) {
  for (const cutoff of [
    {
      name: "restart",
      apply: (harness: TestHarness) => {
        harness.host.restart();
      },
    },
    {
      name: "disposal",
      apply: (harness: TestHarness) => {
        harness.host.dispose();
      },
    },
  ] as const) {
    test(`${sourceFailure.name} precedes later reentrant ${cutoff.name}`, async () => {
      const settlement = deferred<TestSettlement>();
      let harness: TestHarness;
      harness = createHarness({
        invoke: () => settlement.promise,
      });
      await startReady(harness);
      const identity = captureIdentity();
      const sink: OperationProducerSink<string, string, string> = {
        reportProgress: () => {
          sourceFailure.send(harness.workers[0]!);
          cutoff.apply(harness);
          return undefined;
        },
        reportDurable: () => undefined,
        reportUnexpectedTerminal: () => undefined,
        reportUnexpectedFailure: () => undefined,
        ...terminalCallbacks<string, string>(() => undefined),
        reportQuiesced: () => undefined,
      };
      preparedBinding(
        harness.adapter.prepare(identity, "input", sink, uncanceledOperation),
      ).activate();
      await harness.environment.flushAsync();

      harness.workers[0]!.emitRaw(workerEnvelope(1, {
        kind: "progress",
        operation: {
          operationId: identity.id,
          operationSequence: identity.sequence,
        },
        payload: "progress",
      }));

      assert.equal(harness.failures[0]?.kind, sourceFailure.kind);
      assert.deepEqual(harness.host.snapshot().closure, {
        kind: "unexpected-failure",
        failure: {
          kind: sourceFailure.kind,
          diagnostic: {
            code: sourceFailure.kind,
            detail: harness.failures[0]?.diagnostic.detail,
          },
        },
      });
      assert.equal(harness.host.snapshot().phase, "closed");
      assert.equal(harness.workers[0]!.terminateCount, 1);
      assert.deepEqual(harness.releasedEpochs, [1]);
    });
  }
}

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
