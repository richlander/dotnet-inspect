import assert from "node:assert/strict";
import test from "node:test";
import {
  createOperationAuthorityPage,
  type OperationHandle,
  type OperationProducerSink,
} from "../src/operation-authority.ts";
import { type WorkerRuntimePreparationError } from "../src/worker-runtime-core.ts";

import {
  type TestSettlement,
  uncanceledOperation,
  deferred,
  terminalCallbacks,
  createHarness,
  session,
  started,
  captureIdentity,
  preparedBinding,
  workerEnvelope,
  operationMessages,
  ownDataProperty,
  startReady,
} from "./worker-runtime-test-fixture.ts";

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
    reportDurable: () => undefined,
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
  preparedBinding(
    harness.adapter.prepare(identity, "input", sink, uncanceledOperation),
  ).activate();
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
