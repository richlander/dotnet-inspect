import assert from "node:assert/strict";
import test from "node:test";
import { createOperationAuthorityPage } from "../src/operation-authority.ts";

import {
  type TestSettlement,
  deferred,
  createHarness,
  session,
  started,
  workerEnvelope,
  operationMessages,
  startReady,
} from "./worker-runtime-test-fixture.ts";

test("watchdog uses largest allowance, excludes progress, admits while suspect, and fails in two stages", async () => {
  const settlement = deferred<TestSettlement>();
  const harness = createHarness({
    invoke: () => settlement.promise,
    allowance: { kind: "bounded", maxSilentActiveMilliseconds: 20 },
    omitResponse: kind => kind === "probe-acknowledged",
  });
  await startReady(harness);
  const authority = createOperationAuthorityPage({
    allocation: {
      createId: (() => {
        let id = 1;
        return () => `watchdog-${id++}`;
      })(),
    },
  });
  const first = session(harness.adapter, authority);
  const handle = started(first.session.start("input", harness.adapter));
  await harness.environment.flushAsync();
  harness.environment.advanceActive(19);
  harness.workers[0]!.emitRaw(workerEnvelope(1, {
    kind: "progress",
    operation: { operationId: handle.id, operationSequence: 1 },
    payload: "non-renewing",
  }));
  harness.environment.advanceActive(1);
  assert.equal(harness.host.snapshot().phase, "suspect");

  const second = session(harness.adapter, authority);
  const secondHandle = started(
    second.session.start("still-admitted", harness.adapter),
  );
  assert.equal(secondHandle.id.length > 0, true);
  await harness.environment.flushAsync();
  assert.equal(harness.host.snapshot().phase, "draining");
  assert.equal(harness.failures[0]?.kind, "control-response");
});

test("continuous bounded silence produces exact watchdog failure on the second interval", async () => {
  const harness = createHarness({
    omitResponse: kind => kind === "probe-acknowledged",
  });
  await startReady(harness);
  harness.environment.advanceActive(10);
  assert.equal(harness.host.snapshot().phase, "suspect");
  harness.environment.advanceActive(10);
  assert.equal(harness.failures[0]?.kind, "watchdog");
  assert.equal(harness.host.snapshot().phase, "closed");
});

test("bounded epoch-work topology recomputes from retained task evidence origin", async () => {
  const settlement = deferred<TestSettlement>();
  const harness = createHarness({ invoke: () => settlement.promise });
  await startReady(harness);
  harness.environment.advanceActive(5);
  assert.equal(
    harness.workers[0]!.startEpochWork("speculative", 1),
    true,
  );
  assert.equal(harness.host.snapshot().lastTaskEvidenceOrigin, 0);
  harness.environment.advanceActive(24);
  assert.equal(harness.host.snapshot().phase, "ready");
  harness.environment.advanceActive(1);
  assert.equal(harness.host.snapshot().phase, "suspect");
});

test("bounded topology shrink immediately evaluates an elapsed watchdog deadline", async () => {
  const harness = createHarness();
  await startReady(harness);
  assert.equal(harness.workers[0]!.startEpochWork("speculative", 1), true);
  assert.equal(harness.host.snapshot().activeEpochWork, 1);

  harness.environment.advanceActive(20);
  assert.equal(harness.host.snapshot().phase, "ready");
  assert.equal(harness.workers[0]!.finishEpochWork(1), true);

  assert.equal(harness.host.snapshot().phase, "suspect");
});

test("unbounded work disables silence judgment and final close grants one bounded interval", async () => {
  const settlement = deferred<TestSettlement>();
  const harness = createHarness({
    invoke: () => settlement.promise,
    allowance: { kind: "unbounded" },
  });
  await startReady(harness);
  const operationSession = session(harness.adapter);
  const handle = started(
    operationSession.session.start("input", harness.adapter),
  );
  await harness.environment.flushAsync();
  harness.environment.advanceActive(1_000);
  assert.equal(harness.host.snapshot().phase, "ready");
  settlement.resolve({ kind: "succeeded", value: "done" });
  await harness.environment.flushAsync();
  const origin = harness.host.snapshot().lastTaskEvidenceOrigin;
  assert.equal(origin, 1_000);
  harness.environment.advanceActive(9);
  assert.equal(harness.host.snapshot().phase, "ready");
  harness.environment.advanceActive(1);
  assert.equal(harness.host.snapshot().phase, "suspect");
  await handle.quiesced;
});

test("lifecycle and main-loop recovery rebase liveness while preserving the probe", async () => {
  const harness = createHarness({
    omitResponse: kind => kind === "probe-acknowledged",
  });
  await startReady(harness);
  harness.environment.advanceActive(10);
  await harness.environment.flushAsync();
  assert.equal(harness.host.snapshot().phase, "suspect");
  assert.equal(harness.host.snapshot().outstandingProbeSequence, 1);
  harness.environment.suspend();
  harness.environment.advanceActive(100);
  harness.environment.resume();
  assert.equal(harness.host.snapshot().phase, "ready");
  assert.equal(harness.host.snapshot().outstandingProbeSequence, 1);
  harness.environment.recoverMainLoop(100);
  assert.equal(harness.host.snapshot().outstandingProbeSequence, 1);
  harness.environment.advanceActive(9);
  assert.equal(harness.host.snapshot().phase, "ready");
  harness.environment.advanceActive(1);
  assert.equal(harness.host.snapshot().phase, "suspect");
  assert.equal(
    operationMessages(harness.workers[0]!).filter(kind => kind === "probe")
      .length,
    1,
  );
});

test("main-loop recovery preserves unresolved command control grace", async () => {
  const settlement = deferred<TestSettlement>();
  const harness = createHarness({
    invoke: () => settlement.promise,
    controlResponseGraceMilliseconds: 10,
    omitResponse: kind =>
      kind === "accepted" || kind === "probe-acknowledged",
  });
  await startReady(harness);
  const operationSession = session(harness.adapter);
  started(operationSession.session.start("active", harness.adapter));
  await harness.environment.flushAsync();
  harness.environment.advanceActive(9);
  assert.equal(harness.host.snapshot().outstandingProbeSequence, null);
  harness.environment.recoverMainLoop(100);
  assert.equal(harness.host.snapshot().outstandingProbeSequence, null);
  assert.equal(harness.failures.length, 0);
  harness.environment.advanceActive(1);
  assert.equal(harness.host.snapshot().outstandingProbeSequence, 1);
  assert.equal(harness.failures.length, 0);
  assert.equal(
    operationMessages(harness.workers[0]!).filter(kind => kind === "probe")
      .length,
    1,
  );
  harness.host.restart();
});

test("epoch-work validation mirrors high-water, active-set, allowance, and close release", async () => {
  const settlement = deferred<TestSettlement>();
  const harness = createHarness({ invoke: () => settlement.promise });
  await startReady(harness);
  assert.notEqual(
    harness.producerClasses,
    harness.workerProducerClasses[0],
  );
  assert.equal(
    harness.workers[0]!.startEpochWork("speculative", 1),
    true,
  );
  assert.equal(harness.host.snapshot().activeEpochWork, 1);
  assert.equal(
    harness.workers[0]!.finishEpochWork(1),
    true,
  );
  assert.equal(harness.host.snapshot().activeEpochWork, 0);
  assert.equal(
    harness.workers[0]!.finishEpochWork(1),
    false,
  );
  assert.equal(
    harness.workers[0]!.emittedMessages.at(-1)?.kind,
    "epoch-failed",
  );

  const mismatch = createHarness();
  await startReady(mismatch);
  assert.equal(
    mismatch.workers[0]!.startEpochWork(
      "speculative",
      1,
      { kind: "bounded", maxSilentActiveMilliseconds: 29 },
    ),
    false,
  );
  assert.equal(
    mismatch.workers[0]!.emittedMessages.at(-1)?.kind,
    "epoch-failed",
  );

  const unknownClass = createHarness();
  await startReady(unknownClass);
  assert.equal(
    unknownClass.workers[0]!.startEpochWork("unregistered", 1),
    false,
  );
  assert.equal(
    unknownClass.workers[0]!.emittedMessages.at(-1)?.kind,
    "epoch-failed",
  );

  const unknownAllowance = createHarness();
  await startReady(unknownAllowance);
  unknownAllowance.workers[0]!.emitRaw(workerEnvelope(1, {
    kind: "epoch-work-started",
    workSequence: 1,
    allowance: { kind: "bounded", maxSilentActiveMilliseconds: 31 },
  }));
  assert.equal(unknownAllowance.failures[0]?.kind, "protocol");

  const activeDuplicate = createHarness();
  await startReady(activeDuplicate);
  assert.equal(
    activeDuplicate.workers[0]!.startEpochWork("speculative", 1),
    true,
  );
  assert.equal(
    activeDuplicate.workers[0]!.startEpochWork("speculative", 1),
    false,
  );
  assert.equal(
    activeDuplicate.workers[0]!.emittedMessages.at(-1)?.kind,
    "epoch-failed",
  );

  const mainDuplicate = createHarness();
  await startReady(mainDuplicate);
  mainDuplicate.workers[0]!.emitRaw(workerEnvelope(1, {
    kind: "epoch-work-started",
    workSequence: 1,
    allowance: { kind: "bounded", maxSilentActiveMilliseconds: 30 },
  }));
  mainDuplicate.workers[0]!.emitRaw(workerEnvelope(1, {
    kind: "epoch-work-started",
    workSequence: 1,
    allowance: { kind: "bounded", maxSilentActiveMilliseconds: 30 },
  }));
  assert.equal(mainDuplicate.failures[0]?.kind, "protocol");
  mainDuplicate.host.receiveWorkerCrash(
    mainDuplicate.workers[0]!,
    "physical loss",
  );
  assert.equal(mainDuplicate.host.snapshot().activeEpochWork, 0);
});
