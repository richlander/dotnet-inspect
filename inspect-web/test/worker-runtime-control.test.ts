import assert from "node:assert/strict";
import test from "node:test";
import {
  type OperationHandle,
  type OperationOutcome,
  type OperationProducerSink,
} from "../src/operation-authority.ts";

import {
  type TestAdapter,
  type TestSettlement,
  uncanceledOperation,
  type TestHarness,
  deferred,
  stringDecoder,
  terminalCallbacks,
  mainRegistration,
  createHarness,
  session,
  started,
  captureIdentity,
  preparedBinding,
  workerEnvelope,
  operationMessages,
  workerReceivedMessage,
  ownDataProperty,
  startReady,
} from "./worker-runtime-test-fixture.ts";

test("typed operation control acknowledges the exact active operation", async () => {
  const settlement = deferred<TestSettlement>();
  const harness = createHarness({ invoke: () => settlement.promise });
  await startReady(harness);
  const operationSession = session(harness.adapter);
  const handle = started(
    operationSession.session.start("input", harness.adapter),
  );
  await harness.environment.flushAsync();

  assert.deepEqual(
    await harness.adapter.requestControl(handle.id, "more"),
    { kind: "acknowledged", value: "controlled:more" },
  );
  assert.deepEqual(operationMessages(harness.workers[0]!), [
    "initialize",
    "start",
    "control",
  ]);

  settlement.resolve({ kind: "succeeded", value: "done" });
  await handle.quiesced;
});

test("operation control is confined to its registration instance", async () => {
  const settlement = deferred<TestSettlement>();
  const harness = createHarness({ invoke: () => settlement.promise });
  const otherAdapter = harness.host.registerControlledOperation({
    ...mainRegistration({
      kind: "bounded",
      maxSilentActiveMilliseconds: 20,
    }),
    kind: "other",
  });
  await startReady(harness);
  const operationSession = session(harness.adapter);
  const handle = started(
    operationSession.session.start("input", harness.adapter),
  );
  await harness.environment.flushAsync();

  assert.deepEqual(
    await otherAdapter.requestControl(handle.id, "more"),
    { kind: "failed", error: "control:operation-mismatch" },
  );
  assert.equal(
    operationMessages(harness.workers[0]!)
      .filter(kind => kind === "control").length,
    0,
  );

  settlement.resolve({ kind: "succeeded", value: "done" });
  await handle.quiesced;
});

test("operation control rejects oversized input before posting", async () => {
  const settlement = deferred<TestSettlement>();
  const harness = createHarness({ invoke: () => settlement.promise });
  await startReady(harness);
  const operationSession = session(harness.adapter);
  const handle = started(
    operationSession.session.start("input", harness.adapter),
  );
  await harness.environment.flushAsync();

  assert.deepEqual(
    await harness.adapter.requestControl(handle.id, "x".repeat(33)),
    { kind: "failed", error: "control:payload-rejected" },
  );
  assert.equal(
    operationMessages(harness.workers[0]!)
      .filter(kind => kind === "control").length,
    0,
  );

  settlement.resolve({ kind: "succeeded", value: "done" });
  await handle.quiesced;
});

test("operation control reserves busy state before reentrant encoding", async () => {
  const settlement = deferred<TestSettlement>();
  const nestedRequests:
    Array<ReturnType<TestAdapter["requestControl"]>> = [];
  let harness: TestHarness;
  let handle: OperationHandle<string, string>;
  let reenter = true;
  harness = createHarness({
    invoke: () => settlement.promise,
    encodeControlInput: input => {
      if (reenter) {
        reenter = false;
        nestedRequests.push(
          harness.adapter.requestControl(handle.id, "nested"),
        );
      }
      return { kind: "decoded", value: input };
    },
  });
  await startReady(harness);
  const operationSession = session(harness.adapter);
  handle = started(
    operationSession.session.start("input", harness.adapter),
  );
  await harness.environment.flushAsync();

  const outer = harness.adapter.requestControl(handle.id, "outer");
  assert.deepEqual(await nestedRequests[0]!, {
    kind: "failed",
    error: "control:control-busy",
  });
  await harness.environment.flushAsync();
  assert.deepEqual(await outer, {
    kind: "acknowledged",
    value: "controlled:outer",
  });
  assert.equal(
    operationMessages(harness.workers[0]!)
      .filter(kind => kind === "control").length,
    1,
  );

  settlement.resolve({ kind: "succeeded", value: "done" });
  await handle.quiesced;
});

test("reentrant cancellation during control encoding prevents the post", async () => {
  const settlement = deferred<TestSettlement>();
  let handle: OperationHandle<string, string>;
  const harness = createHarness({
    invoke: () => settlement.promise,
    encodeControlInput: input => {
      handle.cancel("user");
      return { kind: "decoded", value: input };
    },
  });
  await startReady(harness);
  const operationSession = session(harness.adapter);
  handle = started(
    operationSession.session.start("input", harness.adapter),
  );
  await harness.environment.flushAsync();

  assert.deepEqual(
    await harness.adapter.requestControl(handle.id, "more"),
    { kind: "not-active" },
  );
  assert.deepEqual(operationMessages(harness.workers[0]!), [
    "initialize",
    "start",
    "cancel",
  ]);

  settlement.resolve({ kind: "canceled", reason: "user" });
  await handle.quiesced;
});

test("reentrant restart during control encoding prevents the post", async () => {
  const settlement = deferred<TestSettlement>();
  let harness: TestHarness;
  harness = createHarness({
    invoke: () => settlement.promise,
    encodeControlInput: input => {
      harness.host.restart();
      return { kind: "decoded", value: input };
    },
  });
  await startReady(harness);
  const operationSession = session(harness.adapter);
  const handle = started(
    operationSession.session.start("input", harness.adapter),
  );
  await harness.environment.flushAsync();

  assert.deepEqual(
    await harness.adapter.requestControl(handle.id, "more"),
    {
      kind: "failed",
      error: "control:operation-closed",
    },
  );
  assert.equal(
    operationMessages(harness.workers[0]!)
      .filter(kind => kind === "control").length,
    0,
  );
  assert.deepEqual(await handle.outcome, {
    kind: "canceled",
    reason: "worker-restarted",
  });
  await handle.quiesced;
});

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
  test(`deferred ${cutoff.name} during control encoding prevents the post`, async () => {
    const settlement = deferred<TestSettlement>();
    let applyCutoff: (() => void) | null = null;
    const controlResults:
      Array<ReturnType<TestAdapter["requestControl"]>> = [];
    let harness: TestHarness;
    harness = createHarness({
      invoke: () => settlement.promise,
      encodeControlInput: input => {
        applyCutoff?.();
        return { kind: "decoded", value: input };
      },
    });
    await startReady(harness);
    const identity = captureIdentity();
    const outcomes: OperationOutcome<string, string>[] = [];
    let quiesced = false;
    const sink: OperationProducerSink<string, string, string> = {
      reportProgress: () => {
        harness.workers[0]!.emitHeartbeat();
        controlResults.push(
          harness.adapter.requestControl(identity.id, "more"),
        );
        return undefined;
      },
      reportDurable: () => undefined,
      reportUnexpectedTerminal: () => undefined,
      reportUnexpectedFailure: () => undefined,
      ...terminalCallbacks<string, string>(outcome => {
        outcomes.push(outcome);
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
    applyCutoff = () => cutoff.apply(harness);

    harness.workers[0]!.emitRaw(workerEnvelope(1, {
      kind: "progress",
      operation: {
        operationId: identity.id,
        operationSequence: identity.sequence,
      },
      payload: "progress",
    }));

    assert.deepEqual(await controlResults[0]!, { kind: "not-active" });
    assert.equal(
      operationMessages(harness.workers[0]!)
        .filter(kind => kind === "control").length,
      0,
    );
    assert.deepEqual(outcomes, [{
      kind: "canceled",
      reason: "worker-restarted",
    }]);
    assert.equal(quiesced, true);
  });
}

test("a pending operation control rejects overlap and retains its acknowledgment after settlement", async () => {
  const settlement = deferred<TestSettlement>();
  const control = deferred<{
    readonly kind: "acknowledged";
    readonly value: string;
  }>();
  const harness = createHarness({
    invoke: () => settlement.promise,
    control: {
      input: stringDecoder(),
      invoke: () => control.promise,
    },
  });
  await startReady(harness);
  const operationSession = session(harness.adapter);
  const handle = started(
    operationSession.session.start("input", harness.adapter),
  );
  await harness.environment.flushAsync();

  const first = harness.adapter.requestControl(handle.id, "first");
  assert.deepEqual(
    await harness.adapter.requestControl(handle.id, "second"),
    { kind: "failed", error: "control:control-busy" },
  );
  settlement.resolve({ kind: "succeeded", value: "done" });
  await harness.environment.flushAsync();
  assert.equal(harness.host.snapshot().compactControlRecords, 1);

  control.resolve({ kind: "acknowledged", value: "granted" });
  await harness.environment.flushAsync();
  assert.deepEqual(await first, {
    kind: "acknowledged",
    value: "granted",
  });
  assert.equal(harness.host.snapshot().compactControlRecords, 0);
  await handle.quiesced;
});

test("worker failure preserves an admitted control acknowledgment for natural draining", async () => {
  const settlement = deferred<TestSettlement>();
  const control = deferred<{
    readonly kind: "acknowledged";
    readonly value: string;
  }>();
  const harness = createHarness({
    invoke: () => settlement.promise,
    control: {
      input: stringDecoder(),
      invoke: () => control.promise,
    },
  });
  await startReady(harness);
  const operationSession = session(harness.adapter);
  const handle = started(
    operationSession.session.start("input", harness.adapter),
  );
  await harness.environment.flushAsync();

  const pending = harness.adapter.requestControl(handle.id, "more");
  await harness.environment.flushAsync();
  harness.workers[0]!.fail(new Error("worker boundary failed"));
  await harness.environment.flushAsync();
  assert.deepEqual(await pending, {
    kind: "failed",
    error: "control-boundary:worker-declared",
  });

  settlement.resolve({ kind: "succeeded", value: "physical release" });
  await harness.environment.flushAsync();
  assert.equal(harness.host.snapshot().compactControlRecords, 1);
  control.resolve({ kind: "acknowledged", value: "late acknowledgment" });
  await harness.environment.flushAsync();

  assert.equal(
    harness.workers[0]!.emittedMessages
      .filter(message => message.kind === "control-acknowledged").length,
    1,
  );
  assert.equal(harness.host.snapshot().compactControlRecords, 0);
  assert.equal(harness.workers[0]!.activeOperationCount, 0);
  assert.equal(harness.workers[0]!.terminated, true);
  await handle.quiesced;
});

test("malformed operation control acknowledgment fails the epoch", async () => {
  const settlement = deferred<TestSettlement>();
  const harness = createHarness({
    invoke: () => settlement.promise,
    omitResponse: kind => kind === "control-acknowledged",
  });
  await startReady(harness);
  const operationSession = session(harness.adapter);
  const handle = started(
    operationSession.session.start("input", harness.adapter),
  );
  await harness.environment.flushAsync();

  const pending = harness.adapter.requestControl(handle.id, "more");
  await harness.environment.flushAsync();
  const controlMessage = workerReceivedMessage(
    harness.workers[0]!,
    "control",
  );
  const epochToken = harness.host.snapshot().epochToken;
  assert.ok(epochToken !== null);
  harness.workers[0]!.emitRaw(workerEnvelope(epochToken, {
    kind: "control-acknowledged",
    operation: ownDataProperty(controlMessage, "operation"),
    controlSequence: ownDataProperty(controlMessage, "controlSequence"),
    status: "acknowledged",
    payload: 42,
  }));
  await harness.environment.flushAsync();

  assert.equal(harness.failures[0]?.kind, "protocol");
  assert.deepEqual(await pending, {
    kind: "failed",
    error: "control-boundary:protocol",
  });
  settlement.resolve({ kind: "succeeded", value: "done" });
  harness.environment.advanceActive(20);
  await harness.environment.flushAsync();
  await handle.quiesced;
});

test("active operation without control support fails the epoch", async () => {
  const settlement = deferred<TestSettlement>();
  const harness = createHarness({
    invoke: () => settlement.promise,
    control: null,
  });
  await startReady(harness);
  const operationSession = session(harness.adapter);
  const handle = started(
    operationSession.session.start("input", harness.adapter),
  );
  await harness.environment.flushAsync();

  const pending = harness.adapter.requestControl(handle.id, "more");
  await harness.environment.flushAsync();

  assert.equal(harness.failures[0]?.kind, "worker-declared");
  assert.deepEqual(await pending, {
    kind: "failed",
    error: "control-boundary:worker-declared",
  });
  settlement.resolve({ kind: "succeeded", value: "done" });
  await harness.environment.flushAsync();
  await handle.quiesced;
});

test("cancellation closes control admission without discarding a posted response", async () => {
  const settlement = deferred<TestSettlement>();
  const control = deferred<{
    readonly kind: "acknowledged";
    readonly value: string;
  }>();
  const harness = createHarness({
    invoke: () => settlement.promise,
    cancel: () => true,
    control: {
      input: stringDecoder(),
      invoke: () => control.promise,
    },
  });
  await startReady(harness);
  const operationSession = session(harness.adapter);
  const handle = started(
    operationSession.session.start("input", harness.adapter),
  );
  await harness.environment.flushAsync();

  const pending = harness.adapter.requestControl(handle.id, "first");
  handle.cancel("user");
  assert.deepEqual(
    await harness.adapter.requestControl(handle.id, "second"),
    { kind: "not-active" },
  );
  control.resolve({ kind: "acknowledged", value: "granted-before-cancel" });
  settlement.resolve({ kind: "canceled", reason: "user" });
  await harness.environment.flushAsync();
  assert.deepEqual(await pending, {
    kind: "acknowledged",
    value: "granted-before-cancel",
  });
  await handle.quiesced;
});

test("authority cancellation closes control admission before its feature callback", async () => {
  const settlement = deferred<TestSettlement>();
  const harness = createHarness({
    invoke: () => settlement.promise,
    cancel: () => true,
  });
  await startReady(harness);
  const observerMessages: Array<readonly string[]> = [];
  const controlResults:
    Array<ReturnType<TestAdapter["requestControl"]>> = [];
  const operationSession = session(
    harness.adapter,
    undefined,
    event => {
      if (event.kind === "canceled") {
        observerMessages.push(operationMessages(harness.workers[0]!));
        controlResults.push(
          harness.adapter.requestControl(event.operationId, "late"),
        );
      }
      return undefined;
    },
  );
  const handle = started(
    operationSession.session.start("input", harness.adapter),
  );
  await harness.environment.flushAsync();

  assert.deepEqual(handle.cancel("user"), { kind: "applied" });
  assert.deepEqual(observerMessages, [["initialize", "start"]]);
  assert.deepEqual(await controlResults[0]!, { kind: "not-active" });
  assert.deepEqual(operationMessages(harness.workers[0]!), [
    "initialize",
    "start",
    "cancel",
  ]);

  settlement.resolve({ kind: "canceled", reason: "user" });
  await harness.environment.flushAsync();
  await handle.quiesced;
});

test("boundary closure fails the caller but retains the posted control response", async () => {
  const settlement = deferred<TestSettlement>();
  const harness = createHarness({
    invoke: () => settlement.promise,
    omitResponse: kind => kind === "control-acknowledged",
  });
  await startReady(harness);
  const operationSession = session(harness.adapter);
  const handle = started(
    operationSession.session.start("input", harness.adapter),
  );
  await harness.environment.flushAsync();

  const pending = harness.adapter.requestControl(handle.id, "more");
  await harness.environment.flushAsync();
  const controlMessage = workerReceivedMessage(
    harness.workers[0]!,
    "control",
  );
  const epochToken = harness.host.snapshot().epochToken;
  assert.ok(epochToken !== null);

  harness.workers[0]!.emitRaw(workerEnvelope(epochToken, {
    kind: "heartbeat",
    unexpected: true,
  }));
  await harness.environment.flushAsync();
  assert.deepEqual(await pending, {
    kind: "failed",
    error: "control-boundary:protocol",
  });

  settlement.resolve({ kind: "succeeded", value: "done" });
  await harness.environment.flushAsync();
  assert.equal(harness.host.snapshot().compactControlRecords, 1);

  harness.workers[0]!.emitRaw(workerEnvelope(epochToken, {
    kind: "control-acknowledged",
    operation: ownDataProperty(controlMessage, "operation"),
    controlSequence: ownDataProperty(controlMessage, "controlSequence"),
    status: "acknowledged",
    payload: "ignored-after-closure",
  }));
  await harness.environment.flushAsync();
  assert.equal(harness.host.snapshot().compactControlRecords, 0);
  await handle.quiesced;
});

test("not-active control acknowledgment closes the operation control port", async () => {
  const settlement = deferred<TestSettlement>();
  const harness = createHarness({
    invoke: () => settlement.promise,
    control: {
      input: stringDecoder(),
      invoke: () => ({ kind: "not-active" }),
    },
  });
  await startReady(harness);
  const operationSession = session(harness.adapter);
  const handle = started(
    operationSession.session.start("input", harness.adapter),
  );
  await harness.environment.flushAsync();

  assert.deepEqual(
    await harness.adapter.requestControl(handle.id, "more"),
    { kind: "not-active" },
  );
  assert.deepEqual(
    await harness.adapter.requestControl(handle.id, "again"),
    { kind: "not-active" },
  );
  assert.equal(
    operationMessages(harness.workers[0]!)
      .filter(kind => kind === "control").length,
    1,
  );

  settlement.resolve({ kind: "succeeded", value: "done" });
  await handle.quiesced;
});
