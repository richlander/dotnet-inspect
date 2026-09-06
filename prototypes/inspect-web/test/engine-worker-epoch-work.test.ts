import assert from "node:assert/strict";
import { setImmediate } from "node:timers/promises";
import test from "node:test";
import {
  createEngineWorkerProducerClasses,
  engineWorkerDiagnostic,
  engineWorkerManagedProducerAllowance,
  engineWorkerPolicy,
  engineWorkerText,
} from "../src/engine-worker-contract.ts";
import {
  createEngineWorkerBootstrap,
  type EngineWorkerEpochWorkExports,
} from "../src/engine-worker-epoch-work.ts";
import { WorkerOperationCatalog, WorkerRuntimeRealm } from "../src/worker-runtime-realm.ts";
import {
  type RawWorkerToMainEnvelope,
  WORKER_RUNTIME_PROTOCOL_VERSION,
} from "../src/worker-runtime-protocol.ts";

function deferred<T>() {
  let resolve!: (value: T) => void;
  const promise = new Promise<T>(accept => { resolve = accept; });
  return { promise, resolve };
}

function fixture(options: {
  readonly engineReady?: Promise<void>;
  readonly hostReady?: Promise<void>;
  readonly registrationFailure?: Error;
  readonly drain?: () => Promise<void>;
  readonly postFailure?: Error;
} = {}) {
  const events: string[] = [];
  const messages: RawWorkerToMainEnvelope[] = [];
  const cleanupFailures: unknown[] = [];
  const boot = deferred<RawWorkerToMainEnvelope>();
  const hostLoading = deferred<void>();
  let callbacks: {
    readonly allowance: string;
    readonly started: (sequence: number, allowance: string) => void;
    readonly finished: (sequence: number) => void;
  } | undefined;
  const exports: EngineWorkerEpochWorkExports = {
    registerEpochWorkReporter(allowance, started, finished) {
      events.push("register");
      if (options.registrationFailure !== undefined) throw options.registrationFailure;
      callbacks = { allowance, started, finished };
    },
    async drainEpochWorkReporter() {
      events.push("drain");
      await options.drain?.();
    },
    unregisterEpochWorkReporter() {
      events.push("unregister");
    },
  };
  const bootstrap = createEngineWorkerBootstrap(
    async origin => {
      assert.equal(origin, "https://inspect.example");
      events.push("engine:start");
      await options.engineReady;
      events.push("engine:ready");
    },
    async () => {
      events.push("host:load");
      hostLoading.resolve();
      await options.hostReady;
      return exports;
    },
    {
      startEpochWork: (producerClass, sequence, allowance) =>
        realm.startEpochWork(producerClass, sequence, allowance),
      finishEpochWork: sequence => realm.finishEpochWork(sequence),
      fail: detail => realm.fail(detail),
    },
  );
  const realm = new WorkerRuntimeRealm({
    bootstrap: { decoder: engineWorkerText, bootstrap: bootstrap.bootstrap },
    diagnostic: engineWorkerDiagnostic,
    unknownOperationRejection: () => ({ error: "unknown", diagnostic: "unknown" }),
    operations: new WorkerOperationCatalog(),
    producerClasses: createEngineWorkerProducerClasses(),
    post(message) {
      if (message.kind === "epoch-work-started" && options.postFailure !== undefined)
        throw options.postFailure;
      messages.push(message);
      events.push(message.kind);
      if (message.kind === "ready" || message.kind === "startup-failed") boot.resolve(message);
      if (message.kind === "startup-failed" || message.kind === "epoch-failed") {
        void bootstrap.close().catch((error: unknown) => { cleanupFailures.push(error); });
      }
    },
  });
  realm.receive({
    protocolVersion: WORKER_RUNTIME_PROTOCOL_VERSION,
    epochToken: 37,
    kind: "initialize",
    bootstrap: "https://inspect.example",
    idleHeartbeatIntervalMilliseconds: engineWorkerPolicy.idleHeartbeatIntervalMilliseconds,
    idleAllowanceMilliseconds: engineWorkerPolicy.idleHeartbeatIntervalMilliseconds
      + engineWorkerPolicy.schedulingToleranceMilliseconds,
  });
  return {
    realm, bootstrap, boot: boot.promise, hostLoading: hostLoading.promise,
    events, messages, cleanupFailures,
    start(sequence: number, allowance?: string) {
      assert.ok(callbacks);
      return callbacks.started(sequence, allowance ?? callbacks.allowance);
    },
    finish(sequence: number) {
      assert.ok(callbacks);
      return callbacks.finished(sequence);
    },
  };
}

test("managed reporter registration follows facade readiness and precedes Worker Ready", async t => {
  const engineReady = deferred<void>();
  const state = fixture({ engineReady: engineReady.promise });
  assert.deepEqual(state.events, ["engine:start"]);
  engineReady.resolve();
  assert.equal((await state.boot).kind, "ready");
  assert.deepEqual(state.events, ["engine:start", "engine:ready", "host:load", "register", "ready"]);
  await state.bootstrap.close();
  assert.deepEqual(state.events.slice(-2), ["drain", "unregister"]);
  t.diagnostic(state.events.join(" -> "));
});

test("failed managed registration produces StartupFailed without Ready", async t => {
  const state = fixture({ registrationFailure: new Error("registration refused") });
  assert.equal((await state.boot).kind, "startup-failed");
  await state.bootstrap.close();
  assert.deepEqual(state.messages.map(message => message.kind), ["startup-failed"]);
  assert.equal(state.events.includes("drain"), false);
  assert.equal(state.events.includes("unregister"), false);
  t.diagnostic(state.events.join(" -> "));
});

test("failed facade startup does not register a reporter", async () => {
  const state = fixture({ engineReady: Promise.reject(new Error("facade startup failed")) });
  assert.equal((await state.boot).kind, "startup-failed");
  await state.bootstrap.close();
  assert.deepEqual(state.events, ["engine:start", "startup-failed"]);
});

test("closure during startup prevents late managed registration", async () => {
  const engineReady = deferred<void>();
  const state = fixture({ engineReady: engineReady.promise });
  state.realm.fail(new Error("startup closed"));
  assert.equal((await state.boot).kind, "startup-failed");
  await state.bootstrap.close();
  engineReady.resolve();
  await setImmediate();
  assert.equal(state.events.includes("register"), false);
  assert.deepEqual(state.messages.map(message => message.kind), ["startup-failed"]);
});

test("registered callbacks use the realm epoch and the shared unbounded receiver policy", async () => {
  const state = fixture();
  await state.boot;
  assert.equal(state.start(1), undefined);
  assert.equal(state.start(2), undefined);
  assert.equal(state.realm.activeEpochWorkCount, 2);
  assert.equal(state.finish(1), undefined);
  assert.equal(state.finish(2), undefined);
  assert.equal(state.realm.activeEpochWorkCount, 0);
  const started = state.messages.filter(message => message.kind === "epoch-work-started");
  assert.deepEqual(started, [1, 2].map(workSequence => ({
    protocolVersion: WORKER_RUNTIME_PROTOCOL_VERSION,
    epochToken: 37,
    kind: "epoch-work-started",
    workSequence,
    allowance: { kind: "unbounded" },
  })));
  assert.equal(
    createEngineWorkerProducerClasses().acceptsLeaseAllowance(engineWorkerManagedProducerAllowance),
    true,
  );
  await state.bootstrap.close();
});

test("closure while acquiring the host facade prevents late registration", async () => {
  const hostReady = deferred<void>();
  const state = fixture({ hostReady: hostReady.promise });
  await state.hostLoading;
  state.realm.fail(new Error("host acquisition closed"));
  assert.equal((await state.boot).kind, "startup-failed");
  await state.bootstrap.close();
  hostReady.resolve();
  await setImmediate();
  assert.equal(state.events.includes("register"), false);
  assert.deepEqual(state.messages.map(message => message.kind), ["startup-failed"]);
});

test("an allowance not issued by Worker registration throws and fails the epoch", async () => {
  const state = fixture();
  await state.boot;
  assert.throws(() => state.start(1, '{"kind":"bounded","maxSilentActiveMilliseconds":1}'), /allowance/);
  assert.equal(state.realm.activeEpochWorkCount, 0);
  assert.deepEqual(state.messages.map(message => message.kind), ["ready", "epoch-failed"]);
  await state.bootstrap.close();
});

test("rejected replay is not a successful managed start and earlier finishes can still drain", async () => {
  const drain = deferred<void>();
  const state = fixture({ drain: () => drain.promise });
  await state.boot;
  state.start(1);
  assert.throws(() => state.start(1), /rejected/);
  assert.equal(state.events.includes("drain"), false);
  await setImmediate();
  assert.equal(state.events.includes("drain"), true);
  assert.equal(state.events.includes("unregister"), false);
  assert.equal(state.finish(1), undefined);
  assert.equal(state.realm.activeEpochWorkCount, 0);
  drain.resolve();
  await state.bootstrap.close();
  assert.deepEqual(state.messages.map(message => message.kind), [
    "ready", "epoch-work-started", "epoch-failed", "epoch-work-finished",
  ]);
  assert.equal(state.events.at(-1), "unregister");
});

test("unmatched finish is a throwing boundary failure", async () => {
  const state = fixture();
  await state.boot;
  assert.throws(() => state.finish(1), /rejected/);
  assert.deepEqual(state.messages.map(message => message.kind), ["ready", "epoch-failed"]);
  await state.bootstrap.close();
});

test("transport exceptions remain throwing epoch failures", async () => {
  const failure = new Error("postMessage failed");
  const state = fixture({ postFailure: failure });
  await state.boot;
  assert.throws(() => state.start(1), error => error === failure);
  assert.deepEqual(state.messages.map(message => message.kind), ["ready", "epoch-failed"]);
  state.finish(1);
  await state.bootstrap.close();
});

test("close stops admission synchronously and retains finish callbacks through one drain", async () => {
  const drain = deferred<void>();
  const state = fixture({ drain: () => drain.promise });
  await state.boot;
  state.start(1);
  const closing = state.bootstrap.close();
  assert.equal(state.bootstrap.close(), closing);
  assert.equal(state.events.includes("drain"), false);
  assert.throws(() => state.start(2), /admission is closed/);
  await setImmediate();
  assert.equal(state.events.filter(event => event === "drain").length, 1);
  assert.equal(state.events.includes("unregister"), false);
  state.finish(1);
  drain.resolve();
  await closing;
  assert.equal(state.events.filter(event => event === "unregister").length, 1);
});

test("a drained managed reporting failure still unregisters and rejects cleanup", async () => {
  const failure = new Error("managed reporter drain failed");
  const state = fixture({ drain: () => Promise.reject(failure) });
  await state.boot;
  await assert.rejects(state.bootstrap.close(), error => error === failure);
  assert.deepEqual(state.events.slice(-2), ["drain", "unregister"]);
  await assert.rejects(state.bootstrap.close(), error => error === failure);
  assert.equal(state.events.filter(event => event === "unregister").length, 1);
});
