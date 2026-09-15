import assert from "node:assert/strict";
import test from "node:test";
import { BrowserWorkerRuntimeEnvironment } from "../src/worker-runtime-browser.ts";
import {
  FakeWorkerOperationCatalog,
  FakeWorkerRuntime,
  ManualWorkerRuntimeEnvironment,
  QueueWorkerRuntimeTransportFactory,
  WorkerProducerClassRegistry,
  WorkerRuntimeHost,
} from "../src/worker-runtime-core.ts";
import { engineWorkerText } from "../src/engine-worker-contract.ts";

class LifecycleDocument extends EventTarget {
  hidden = false;
}

function environment(initiallyHidden = false) {
  const document = new LifecycleDocument();
  document.hidden = initiallyHidden;
  const window = new EventTarget();
  let time = 0;
  let tick: (() => void) | undefined;
  const events: string[] = [];
  const runtime = new BrowserWorkerRuntimeEnvironment({
    document,
    window,
    now: () => time,
    pollIntervalMilliseconds: 10,
    schedulingToleranceMilliseconds: 20,
    schedule: callback => {
      tick = callback;
      return () => { tick = undefined; };
    },
  });
  const stopClock = runtime.clock.subscribe(() => {
    events.push(`clock:${runtime.clock.now()}`);
  });
  const stopLifecycle = runtime.lifecycle.subscribe({
    suspended: () => events.push("suspended"),
    resumed: () => events.push("resumed"),
    mainLoopRecovered: gap => {
      events.push(`gap:${gap}:now:${runtime.clock.now()}`);
    },
  });
  return {
    document,
    window,
    runtime,
    events,
    advance(milliseconds: number, notify = true) {
      time += milliseconds;
      if (notify) tick?.();
    },
    hasTimer: () => tick !== undefined,
    stopClock,
    stopLifecycle,
  };
}

test("browser active time pauses until every lifecycle suspension clears", () => {
  const state = environment();
  state.advance(10);
  state.document.hidden = true;
  state.document.dispatchEvent(new Event("visibilitychange"));
  state.window.dispatchEvent(new Event("pagehide"));
  state.document.dispatchEvent(new Event("freeze"));
  state.advance(500);
  assert.equal(state.runtime.clock.now(), 10);
  state.document.hidden = false;
  state.document.dispatchEvent(new Event("visibilitychange"));
  state.window.dispatchEvent(new Event("pageshow"));
  state.advance(500);
  assert.equal(state.runtime.clock.now(), 10);
  state.document.dispatchEvent(new Event("resume"));
  state.advance(10);
  assert.equal(state.runtime.clock.now(), 20);
  assert.deepEqual(
    state.events.filter(event => !event.startsWith("clock:")),
    ["suspended", "resumed"],
  );
  state.stopClock();
  state.stopLifecycle();
});

test("an initially hidden document has no active startup elapsed time", () => {
  const state = environment(true);
  state.advance(10_000);
  assert.equal(state.runtime.clock.now(), 0);
  state.document.hidden = false;
  state.document.dispatchEvent(new Event("visibilitychange"));
  state.advance(10);
  assert.equal(state.runtime.clock.now(), 10);
  assert.equal(state.events.some(event => event.startsWith("gap:")), false);
  state.stopClock();
  state.stopLifecycle();
});

test("a delayed timer reports one gap before deadline evaluation", () => {
  const state = environment();
  state.advance(10);
  state.events.length = 0;
  state.advance(1_000);
  assert.deepEqual(state.events, ["gap:1000:now:1010", "clock:1010"]);
  state.advance(10);
  assert.equal(state.events.filter(event => event.startsWith("gap:")).length, 1);
  state.stopClock();
  state.stopLifecycle();
});

test("a message-time clock read notices a gap before the timer runs", () => {
  const state = environment();
  state.advance(2_000, false);
  assert.equal(state.runtime.clock.now(), 2_000);
  assert.deepEqual(state.events, ["gap:2000:now:2000"]);
  state.advance(0);
  assert.deepEqual(state.events, ["gap:2000:now:2000", "clock:2000"]);
  state.stopClock();
  state.stopLifecycle();
});

test("ordinary scheduling tolerance remains active time", () => {
  const state = environment();
  state.advance(30);
  assert.equal(state.runtime.clock.now(), 30);
  assert.deepEqual(state.events, ["clock:30"]);
  state.stopClock();
  state.stopLifecycle();
});

test("the last subscription releases the timer and browser event listeners", () => {
  const state = environment();
  state.stopClock();
  assert.equal(state.hasTimer(), true);
  state.stopLifecycle();
  assert.equal(state.hasTimer(), false);
  const before = [...state.events];
  state.document.dispatchEvent(new Event("freeze"));
  state.window.dispatchEvent(new Event("pagehide"));
  state.advance(1_000);
  assert.deepEqual(state.events, before);
  assert.equal(state.runtime.clock.now(), 0);
});

function nativeClockHost(state: ReturnType<typeof environment>) {
  const scheduler = new ManualWorkerRuntimeEnvironment();
  const worker = new FakeWorkerRuntime({
    scheduler,
    bootstrap: { decoder: engineWorkerText, bootstrap: () => undefined },
    diagnostic: () => "Worker failure.",
    unknownOperationRejection: () => ({ error: "Unknown operation.", diagnostic: "Unknown operation." }),
    operations: new FakeWorkerOperationCatalog(),
    producerClasses: new WorkerProducerClassRegistry(30),
  });
  const failures: string[] = [];
  const released: number[] = [];
  const host = new WorkerRuntimeHost({
    transport: new QueueWorkerRuntimeTransportFactory([worker]),
    clock: state.runtime.clock,
    lifecycle: state.runtime.lifecycle,
    bootstrap: { encode: engineWorkerText.decode, diagnostic: engineWorkerText },
    diagnostic: engineWorkerText,
    createDiagnostic: kind => kind,
    callbacks: {
      failure: failure => { failures.push(failure.kind); return undefined; },
      diagnostic: diagnostic => { assert.fail(diagnostic.kind); },
      realmReleased: epoch => { released.push(epoch); return undefined; },
    },
    producerClasses: new WorkerProducerClassRegistry(30),
    idleHeartbeatIntervalMilliseconds: 10,
    schedulingToleranceMilliseconds: 20,
    startupBudgetMilliseconds: 100,
    controlResponseGraceMilliseconds: 50,
    drainBudgetMilliseconds: 20,
  });
  assert.equal(host.start("").kind, "started");
  return { host, scheduler, worker, failures, released };
}

test("native visibility preserves the real host startup budget", async () => {
  const state = environment(true);
  const { host, scheduler, failures } = nativeClockHost(state);
  state.advance(10_000);
  assert.equal(host.snapshot().phase, "starting");
  state.document.hidden = false;
  state.document.dispatchEvent(new Event("visibilitychange"));
  state.advance(20);
  await scheduler.flushAsync();
  assert.equal(host.snapshot().phase, "ready");
  assert.deepEqual(failures, []);
  host.dispose();
  state.stopClock();
  state.stopLifecycle();
});

test("native scheduling recovery preserves remaining startup time without renewing the budget", () => {
  const state = environment();
  const { host, failures, released } = nativeClockHost(state);
  state.advance(20);
  state.advance(1_000);
  assert.equal(host.snapshot().phase, "starting");
  for (let elapsed = 0; elapsed < 70; elapsed += 10) state.advance(10);
  assert.equal(host.snapshot().phase, "starting");
  state.advance(10);
  assert.equal(host.snapshot().phase, "closed");
  assert.deepEqual(failures, ["startup"]);
  assert.deepEqual(released, [1]);
  host.dispose();
  state.stopClock();
  state.stopLifecycle();
});

test("native message-time recovery lets a heartbeat renew the real host watchdog", async () => {
  const state = environment();
  const { host, scheduler, worker, failures } = nativeClockHost(state);
  await scheduler.flushAsync();
  state.advance(10_000, false);
  worker.emitHeartbeat();
  assert.equal(host.snapshot().phase, "ready");
  assert.deepEqual(failures, []);
  state.advance(30);
  assert.equal(host.snapshot().phase, "suspect");
  host.dispose();
  state.stopClock();
  state.stopLifecycle();
});
