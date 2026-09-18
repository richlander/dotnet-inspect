import assert from "node:assert/strict";
import test from "node:test";
import { type WorkerOperationContext } from "../src/worker-runtime-realm.ts";

import {
  type TestSettlement,
  deferred,
  stringDecoder,
  createHarness,
  session,
  started,
  workerEnvelope,
  createRealmHarness,
  realmInitialization,
  startReady,
  flushRealm,
} from "./worker-runtime-test-fixture.ts";

test("production realm waits for bootstrap fulfillment and echoes readiness without a scheduler", async () => {
  const bootstrap = deferred<void>();
  const inputs: string[] = [];
  const { realm, messages } = createRealmHarness({
    bootstrap: {
      decoder: stringDecoder(),
      bootstrap: input => {
        inputs.push(input);
        return bootstrap.promise;
      },
    },
  });
  realm.emitHeartbeat();
  realm.receive(realmInitialization());
  realm.emitHeartbeat();
  await flushRealm();
  assert.deepEqual(inputs, ["bootstrap"]);
  assert.deepEqual(messages, []);
  bootstrap.resolve(undefined);
  await flushRealm();
  assert.deepEqual(messages, [workerEnvelope(1, {
    kind: "ready",
    idleHeartbeatIntervalMilliseconds: 10,
  })]);
  realm.emitHeartbeat();
  assert.deepEqual(messages.at(-1), workerEnvelope(1, { kind: "heartbeat" }));
  realm.dispose();
});

for (const invalid of [
  { name: "malformed envelope", message: null, code: "not-record" },
  {
    name: "invalid bootstrap payload",
    message: realmInitialization(42),
    code: "payload-rejected",
  },
]) {
  test(`production realm surfaces ${invalid.name} before epoch binding`, async () => {
    let bootstrapCalls = 0;
    const { realm, messages } = createRealmHarness({
      bootstrap: {
        decoder: stringDecoder(),
        bootstrap: () => { bootstrapCalls++; },
      },
    });
    assert.throws(() => realm.receive(invalid.message), (error: unknown) => {
      assert.ok(error instanceof Error);
      assert.equal(error.message, "Worker initialization envelope was rejected.");
      assert.ok(typeof error.cause === "object" && error.cause !== null);
      assert.ok("code" in error.cause);
      assert.equal(error.cause.code, invalid.code);
      assert.throws(
        () => realm.fail(error),
        { message: "Worker realm failed before initialization.", cause: error },
      );
      return true;
    });
    realm.emitHeartbeat();
    await flushRealm();
    assert.equal(bootstrapCalls, 0);
    assert.deepEqual(messages, []);
    realm.dispose();
  });
}

for (const mode of ["throw", "reject"] as const) {
  test(`production realm reports bootstrap ${mode} as StartupFailed`, async () => {
    const error = new Error("bootstrap boundary failed");
    const { realm, messages } = createRealmHarness({
      bootstrap: {
        decoder: stringDecoder(),
        bootstrap: () => {
          if (mode === "throw") throw error;
          return Promise.reject(error);
        },
      },
    });
    realm.receive(realmInitialization());
    await flushRealm();
    assert.deepEqual(messages, [workerEnvelope(1, {
      kind: "startup-failed",
      diagnostic: { code: "worker", detail: error },
    })]);
    realm.dispose();
  });
}

test("production realm accepts before synchronous progress and revokes progress at settlement", async () => {
  const settlement = deferred<TestSettlement>();
  const contexts: WorkerOperationContext[] = [];
  const { realm, messages } = createRealmHarness({
    invoke: (_input, context) => {
      contexts.push(context);
      assert.equal(messages.at(-1)?.kind, "accepted");
      assert.equal(context.reportProgress("synchronous prefix"), true);
      return settlement.promise;
    },
  });
  realm.receive(realmInitialization());
  await flushRealm();
  const operation = { operationId: "progress", operationSequence: 1 };
  realm.receive(workerEnvelope(1, {
    kind: "start",
    operation,
    operationKind: "echo",
    payload: "input",
  }));
  await flushRealm();
  assert.deepEqual(messages.map(message => message.kind), [
    "ready", "accepted", "progress",
  ]);
  assert.deepEqual(messages.at(-1), workerEnvelope(1, {
    kind: "progress",
    operation,
    payload: "synchronous prefix",
  }));
  assert.equal(contexts[0]!.reportProgress({ completed: 1 }), true);
  settlement.resolve({ kind: "succeeded", value: "complete" });
  await flushRealm();
  assert.equal(contexts[0]!.reportProgress("late"), false);
  assert.equal(messages.at(-1)?.kind, "settled");
  assert.equal(realm.activeOperationCount, 0);
  realm.dispose();
});

for (const running of [true, false]) {
  test(`production realm serializes synchronous cancellation returning ${running}`, async () => {
    const settlement = deferred<TestSettlement>();
    const operation = { operationId: "cancel", operationSequence: 1 };
    const { realm, messages } = createRealmHarness({
      invoke: () => settlement.promise,
      cancel: (reference, reason) => {
        assert.deepEqual(reference, operation);
        assert.equal(reason, "user");
        assert.equal(messages.at(-1)?.kind, "accepted");
        return running;
      },
    });
    realm.receive(realmInitialization());
    await flushRealm();
    realm.receive(workerEnvelope(1, {
      kind: "start", operation, operationKind: "echo", payload: "input",
    }));
    realm.receive(workerEnvelope(1, {
      kind: "cancel", operation, reason: "user",
    }));
    realm.receive(workerEnvelope(1, { kind: "probe", probeSequence: 1 }));
    await flushRealm();
    assert.deepEqual(messages.slice(1), [
      workerEnvelope(1, {
        kind: "accepted",
        operation,
        allowance: { kind: "bounded", maxSilentActiveMilliseconds: 20 },
      }),
      workerEnvelope(1, {
        kind: "cancel-acknowledged",
        operation,
        status: running ? "running" : "not-active",
      }),
      workerEnvelope(1, { kind: "probe-acknowledged", probeSequence: 1 }),
    ]);
    settlement.resolve({ kind: "canceled", reason: "user" });
    await flushRealm();
    assert.equal(messages.at(-1)?.kind, "settled");
    realm.dispose();
  });
}

test("production realm awaits cancellation before later commands but not physical settlement", async () => {
  const first = deferred<TestSettlement>();
  const second = deferred<TestSettlement>();
  const cancellation = deferred<boolean>();
  const contexts: WorkerOperationContext[] = [];
  const { realm, messages } = createRealmHarness({
    invoke: (input, context) => {
      contexts.push(context);
      return input === "first" ? first.promise : second.promise;
    },
    cancel: () => cancellation.promise,
  });
  realm.receive(realmInitialization());
  await flushRealm();
  const operation = { operationId: "first", operationSequence: 1 };
  realm.receive(workerEnvelope(1, {
    kind: "start", operation, operationKind: "echo", payload: "first",
  }));
  realm.receive(workerEnvelope(1, {
    kind: "cancel", operation, reason: "user",
  }));
  realm.receive(workerEnvelope(1, { kind: "probe", probeSequence: 1 }));
  realm.receive(workerEnvelope(1, {
    kind: "start",
    operation: { operationId: "second", operationSequence: 2 },
    operationKind: "echo",
    payload: "second",
  }));
  await flushRealm();
  assert.deepEqual(messages.map(message => message.kind), ["ready", "accepted"]);
  assert.equal(contexts.length, 1);
  first.resolve({ kind: "canceled", reason: "user" });
  await flushRealm();
  assert.equal(messages.at(-1)?.kind, "settled");
  assert.equal(contexts[0]!.reportProgress("late"), false);
  cancellation.resolve(false);
  await flushRealm();
  assert.deepEqual(messages.slice(-3).map(message => message.kind), [
    "cancel-acknowledged", "probe-acknowledged", "accepted",
  ]);
  assert.equal(contexts.length, 2);
  assert.equal(contexts[0]!.reportProgress("still late"), false);
  assert.equal(contexts[1]!.reportProgress("current"), true);
  second.resolve({ kind: "succeeded", value: "second" });
  await flushRealm();
  realm.dispose();
});

test("production realm serializes typed control acknowledgment before later commands", async () => {
  const first = deferred<TestSettlement>();
  const second = deferred<TestSettlement>();
  const control = deferred<{
    readonly kind: "acknowledged";
    readonly value: string;
  }>();
  const contexts: WorkerOperationContext[] = [];
  const { realm, messages } = createRealmHarness({
    invoke: (input, context) => {
      contexts.push(context);
      return input === "first" ? first.promise : second.promise;
    },
    control: {
      input: stringDecoder(),
      invoke: (reference, input) => {
        assert.deepEqual(reference, {
          operationId: "first",
          operationSequence: 1,
        });
        assert.equal(input, "credit");
        return control.promise;
      },
    },
  });
  realm.receive(realmInitialization());
  await flushRealm();
  const operation = { operationId: "first", operationSequence: 1 };
  realm.receive(workerEnvelope(1, {
    kind: "start", operation, operationKind: "echo", payload: "first",
  }));
  realm.receive(workerEnvelope(1, {
    kind: "control",
    operation,
    controlSequence: 1,
    payload: "credit",
  }));
  realm.receive(workerEnvelope(1, { kind: "probe", probeSequence: 1 }));
  realm.receive(workerEnvelope(1, {
    kind: "start",
    operation: { operationId: "second", operationSequence: 2 },
    operationKind: "echo",
    payload: "second",
  }));
  await flushRealm();
  assert.deepEqual(messages.map(message => message.kind), ["ready", "accepted"]);
  assert.equal(contexts.length, 1);

  first.resolve({ kind: "succeeded", value: "first" });
  await flushRealm();
  assert.equal(messages.at(-1)?.kind, "settled");
  control.resolve({ kind: "acknowledged", value: "granted" });
  await flushRealm();
  assert.deepEqual(messages.slice(-3), [
    workerEnvelope(1, {
      kind: "control-acknowledged",
      operation,
      controlSequence: 1,
      status: "acknowledged",
      payload: "granted",
    }),
    workerEnvelope(1, { kind: "probe-acknowledged", probeSequence: 1 }),
    workerEnvelope(1, {
      kind: "accepted",
      operation: { operationId: "second", operationSequence: 2 },
      allowance: { kind: "bounded", maxSilentActiveMilliseconds: 20 },
    }),
  ]);
  assert.equal(contexts.length, 2);
  second.resolve({ kind: "succeeded", value: "second" });
  await flushRealm();
  realm.dispose();
});

test("production realm closes control admission before cancellation awaits", async () => {
  const settlement = deferred<TestSettlement>();
  const cancellation = deferred<boolean>();
  let controls = 0;
  const { realm, messages } = createRealmHarness({
    invoke: () => settlement.promise,
    cancel: () => cancellation.promise,
    control: {
      input: stringDecoder(),
      invoke: (_reference, input) => {
        controls++;
        return { kind: "acknowledged", value: input };
      },
    },
  });
  realm.receive(realmInitialization());
  await flushRealm();
  const operation = { operationId: "control", operationSequence: 1 };
  realm.receive(workerEnvelope(1, {
    kind: "start", operation, operationKind: "echo", payload: "input",
  }));
  await flushRealm();

  realm.receive(workerEnvelope(1, {
    kind: "cancel", operation, reason: "user",
  }));
  realm.receive(workerEnvelope(1, {
    kind: "control", operation, controlSequence: 1, payload: "late",
  }));
  await flushRealm();
  assert.equal(controls, 0);
  cancellation.resolve(true);
  await flushRealm();
  assert.equal(controls, 0);
  assert.deepEqual(messages.slice(-2), [
    workerEnvelope(1, {
      kind: "cancel-acknowledged",
      operation,
      status: "running",
    }),
    workerEnvelope(1, {
      kind: "control-acknowledged",
      operation,
      controlSequence: 1,
      status: "not-active",
    }),
  ]);

  settlement.resolve({ kind: "canceled", reason: "user" });
  await flushRealm();
  realm.dispose();
});

test("production realm returns not-active for an absent nonfuture operation", async () => {
  const { realm, messages } = createRealmHarness();
  realm.receive(realmInitialization());
  await flushRealm();
  const operation = { operationId: "completed", operationSequence: 1 };
  realm.receive(workerEnvelope(1, {
    kind: "start", operation, operationKind: "echo", payload: "input",
  }));
  await flushRealm();
  assert.equal(messages.at(-1)?.kind, "settled");

  realm.receive(workerEnvelope(1, {
    kind: "control", operation, controlSequence: 1, payload: "late",
  }));
  await flushRealm();
  assert.deepEqual(messages.at(-1), workerEnvelope(1, {
    kind: "control-acknowledged",
    operation,
    controlSequence: 1,
    status: "not-active",
  }));
  realm.dispose();
});

test("production realm rejects replayed active control sequences and future references", async () => {
  const settlement = deferred<TestSettlement>();
  const { realm, messages } = createRealmHarness({
    invoke: () => settlement.promise,
    control: {
      input: stringDecoder(),
      invoke: (_reference, input) => ({
        kind: "acknowledged",
        value: input,
      }),
    },
  });
  realm.receive(realmInitialization());
  await flushRealm();
  const operation = { operationId: "control", operationSequence: 1 };
  realm.receive(workerEnvelope(1, {
    kind: "start", operation, operationKind: "echo", payload: "input",
  }));
  realm.receive(workerEnvelope(1, {
    kind: "control", operation, controlSequence: 1, payload: "first",
  }));
  await flushRealm();
  assert.equal(messages.at(-1)?.kind, "control-acknowledged");
  realm.receive(workerEnvelope(1, {
    kind: "control", operation, controlSequence: 1, payload: "replayed",
  }));
  await flushRealm();
  assert.equal(messages.at(-1)?.kind, "epoch-failed");
  realm.dispose();

  const future = createRealmHarness({
    control: {
      input: stringDecoder(),
      invoke: (_reference, input) => ({
        kind: "acknowledged",
        value: input,
      }),
    },
  });
  future.realm.receive(realmInitialization());
  await flushRealm();
  future.realm.receive(workerEnvelope(1, {
    kind: "control",
    operation: { operationId: "future", operationSequence: 1 },
    controlSequence: 1,
    payload: "credit",
  }));
  await flushRealm();
  assert.equal(future.messages.at(-1)?.kind, "epoch-failed");
  future.realm.dispose();
});

test("production realm failure suppresses progress and new commands but preserves admitted release", async () => {
  const settlement = deferred<TestSettlement>();
  const contexts: WorkerOperationContext[] = [];
  const { realm, messages } = createRealmHarness({
    invoke: (_input, context) => {
      contexts.push(context);
      return settlement.promise;
    },
  });
  realm.receive(realmInitialization());
  await flushRealm();
  const operation = { operationId: "drain", operationSequence: 1 };
  realm.receive(workerEnvelope(1, {
    kind: "start", operation, operationKind: "echo", payload: "input",
  }));
  await flushRealm();
  assert.equal(contexts[0]!.startEpochWork("speculative", 1), true);
  const detail = new Error("worker boundary failed");
  realm.fail(detail);
  realm.fail(new Error("later failure"));
  assert.deepEqual(messages.at(-1), workerEnvelope(1, {
    kind: "epoch-failed",
    diagnostic: { code: "worker", detail },
  }));
  assert.equal(contexts[0]!.reportProgress("late"), false);
  assert.equal(realm.startEpochWork("speculative", 2), false);
  realm.receive(workerEnvelope(1, {
    kind: "start",
    operation: { operationId: "refused", operationSequence: 2 },
    operationKind: "echo",
    payload: "input",
  }));
  realm.receive(workerEnvelope(1, { kind: "cancel", operation, reason: "user" }));
  realm.receive(workerEnvelope(1, { kind: "probe", probeSequence: 1 }));
  await flushRealm();
  assert.equal(messages.at(-1)?.kind, "epoch-failed");
  settlement.resolve({ kind: "succeeded", value: "physical release" });
  await flushRealm();
  assert.equal(contexts[0]!.finishEpochWork(1), true);
  assert.equal(realm.finishEpochWork(1), false);
  assert.deepEqual(messages.slice(-3).map(message => message.kind), [
    "epoch-failed", "settled", "epoch-work-finished",
  ]);
  assert.equal(realm.activeOperationCount, 0);
  assert.equal(realm.activeEpochWorkCount, 0);
  realm.dispose();
});

test("production realm disposal revokes pending callbacks, work, and cache authority", async () => {
  const settlement = deferred<TestSettlement>();
  const cancellation = deferred<boolean>();
  const contexts: WorkerOperationContext[] = [];
  const { realm, messages } = createRealmHarness({
    invoke: (_input, context) => {
      contexts.push(context);
      return settlement.promise;
    },
    cancel: () => cancellation.promise,
  });
  realm.receive(realmInitialization());
  await flushRealm();
  const operation = { operationId: "dispose", operationSequence: 1 };
  realm.receive(workerEnvelope(1, {
    kind: "start", operation, operationKind: "echo", payload: "input",
  }));
  realm.receive(workerEnvelope(1, { kind: "cancel", operation, reason: "user" }));
  await flushRealm();
  const context = contexts[0]!;
  assert.equal(context.cache.set("prepared", "value"), true);
  assert.equal(context.startEpochWork("speculative", 1), true);
  const beforeDisposal = [...messages];
  realm.dispose();
  realm.dispose();
  assert.equal(realm.disposed, true);
  assert.equal(realm.activeOperationCount, 0);
  assert.equal(realm.activeEpochWorkCount, 0);
  assert.equal(context.cache.size, 0);
  assert.equal(context.cache.set("late", "value"), false);
  assert.equal(context.reportProgress("late"), false);
  assert.equal(context.finishEpochWork(1), false);
  assert.equal(context.startEpochWork("speculative", 2), false);
  realm.emitHeartbeat();
  realm.receive(workerEnvelope(1, { kind: "probe", probeSequence: 1 }));
  realm.fail(new Error("late error"));
  settlement.resolve({ kind: "succeeded", value: "late" });
  cancellation.resolve(true);
  await flushRealm();
  assert.deepEqual(messages, beforeDisposal);
});

test("production realm failure or disposal during bootstrap prevents later readiness", async () => {
  for (const close of ["fail", "dispose"] as const) {
    const bootstrap = deferred<void>();
    const { realm, messages } = createRealmHarness({
      bootstrap: {
        decoder: stringDecoder(),
        bootstrap: () => bootstrap.promise,
      },
    });
    realm.receive(realmInitialization());
    if (close === "fail") realm.fail("startup boundary");
    else realm.dispose();
    bootstrap.resolve(undefined);
    await flushRealm();
    assert.deepEqual(messages.map(message => message.kind),
      close === "fail" ? ["startup-failed"] : []);
    realm.dispose();
  }
});

test("fake transport publishes shared realm progress and revokes it on hard termination", async () => {
  const settlement = deferred<TestSettlement>();
  const contexts: WorkerOperationContext[] = [];
  const harness = createHarness({
    invoke: (_input, context) => {
      contexts.push(context);
      return settlement.promise;
    },
  });
  await startReady(harness);
  const operationSession = session(harness.adapter);
  const handle = started(operationSession.session.start("input", harness.adapter));
  await harness.environment.flushAsync();
  assert.equal(contexts[0]!.reportProgress("live"), true);
  assert.equal(harness.workers[0]!.emittedMessages.at(-1)?.kind, "progress");
  assert.equal(operationSession.events.at(-1)?.kind, "progress");
  harness.host.restart();
  assert.equal(contexts[0]!.reportProgress("late"), false);
  assert.equal(harness.workers[0]!.terminateCount, 1);
  settlement.resolve({ kind: "succeeded", value: "late" });
  await harness.environment.flushAsync();
  await handle.quiesced;
});
