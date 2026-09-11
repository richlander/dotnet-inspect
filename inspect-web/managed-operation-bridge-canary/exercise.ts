import * as bridge from "./facades/bridge.js";

type CancelReason =
  | "User"
  | "Superseded"
  | "Disposed"
  | "FeatureObserverFailed"
  | "Timeout"
  | "WorkerRestarted";

interface ReasonCase {
  readonly wire: string;
  readonly expected: CancelReason;
}

export interface ExerciseOptions {
  readonly cancelNeighbor?: boolean;
  readonly skipExpectedFailure?: boolean;
  readonly skipRetainedProgress?: boolean;
}

interface ProgressWitness {
  calls: number;
  readonly events: string[];
}

const reasonCases: readonly ReasonCase[] = [
  { wire: "user", expected: "User" },
  { wire: "superseded", expected: "Superseded" },
  { wire: "disposed", expected: "Disposed" },
  {
    wire: "feature-observer-failed",
    expected: "FeatureObserverFailed",
  },
  { wire: "timeout", expected: "Timeout" },
  { wire: "worker-restarted", expected: "WorkerRestarted" },
];

function expect(actual: unknown, expected: unknown, operation: string): void {
  if (actual !== expected) {
    throw new Error(
      `${operation} returned ${String(actual)} instead of ${String(expected)}.`,
    );
  }
}

function newProgressWitness(): ProgressWitness {
  return { calls: 0, events: [] };
}

function observeProgress(
  witness: ProgressWitness,
): (sequence: number, phase: string, isFinal: boolean) => undefined {
  return (sequence, phase, isFinal): undefined => {
    witness.calls++;
    witness.events.push(`${sequence}:${phase}:${String(isFinal)}`);
    return undefined;
  };
}

function expectProgress(
  witness: ProgressWitness,
  expected: readonly string[],
  operation: string,
): void {
  expect(
    witness.events.join("|"),
    expected.join("|"),
    `${operation} progress events`,
  );
}

function expectRequested(
  receipt: bridge.CancellationRequestReceipt,
  reason: CancelReason,
  operation: string,
): void {
  expect(receipt.kind, "Requested", `${operation} request kind`);
  expect(receipt.reason, reason, `${operation} request reason`);
}

function expectCanceled(
  result: bridge.OperationResultEnvelope,
  reason: CancelReason,
  operation: string,
): void {
  expect(result.kind, "Canceled", `${operation} result kind`);
  expect(result.cancelReason, reason, `${operation} cancel reason`);
  expect(result.value, null, `${operation} canceled value`);
  expect(result.failureKind, null, `${operation} canceled failure kind`);
}

function expectSucceeded(
  result: bridge.OperationResultEnvelope,
  value: string,
  operation: string,
): void {
  expect(result.kind, "Succeeded", `${operation} result kind`);
  expect(result.value, value, `${operation} value`);
  expect(result.failureKind, null, `${operation} failure kind`);
  expect(result.cancelReason, null, `${operation} cancel reason`);
}

function expectFailed(
  result: bridge.OperationResultEnvelope,
  failureKind: "Expected" | "Unexpected",
  error: string,
  diagnostic: string,
  operation: string,
): void {
  expect(result.kind, "Failed", `${operation} result kind`);
  expect(result.failureKind, failureKind, `${operation} failure kind`);
  expect(result.error, error, `${operation} error`);
  expect(result.diagnostic, diagnostic, `${operation} diagnostic`);
  expect(result.value, null, `${operation} failed value`);
  expect(result.cancelReason, null, `${operation} cancel reason`);
}

async function expectRejected(
  operation: Promise<unknown>,
  name: string,
): Promise<void> {
  try {
    await operation;
  } catch {
    return;
  }

  throw new Error(`${name} fulfilled instead of rejecting.`);
}

function expectThrows(operation: () => unknown, name: string): void {
  try {
    operation();
  } catch {
    return;
  }

  throw new Error(`${name} returned instead of throwing.`);
}

async function exerciseCancellationReasons(
  options: ExerciseOptions,
): Promise<void> {
  for (const [index, reasonCase] of reasonCases.entries()) {
    const operationId = `reason-${reasonCase.wire}`;
    const witness = newProgressWitness();
    const operation = bridge.runOperation(
      operationId,
      "cancel",
      observeProgress(witness),
      index === 0,
    );
    expect(
      witness.calls,
      1,
      `${reasonCase.expected} synchronous progress admission`,
    );
    let neighbor:
      | {
          readonly operation: Promise<bridge.OperationResultEnvelope>;
          readonly witness: ProgressWitness;
        }
      | undefined;
    if (index === 0) {
      const neighborWitness = newProgressWitness();
      neighbor = {
        operation: bridge.runOperation(
          "reason-neighbor",
          "success",
          observeProgress(neighborWitness),
          false,
        ),
        witness: neighborWitness,
      };
      expect(
        neighborWitness.calls,
        1,
        "Neighbor synchronous progress admission",
      );
    }

    const cancellationTarget =
      index === 0 && options.cancelNeighbor === true
        ? "reason-neighbor"
        : operationId;
    const cancellation = bridge.requestCancellation(
      cancellationTarget,
      reasonCase.wire,
    );
    expectRequested(
      cancellation,
      reasonCase.expected,
      `${reasonCase.expected} cancellation`,
    );
    if (index === 0 && options.cancelNeighbor === true) {
      expect(
        bridge.completeOperation(operationId),
        true,
        "Wrong-target selected cleanup",
      );
    }

    const result = await operation;
    if (index === 0) {
      const callsBeforeCloseProbe = witness.calls;
      expect(
        bridge.reportRetainedProgress(operationId),
        true,
        "Canceled reporter closed state",
      );
      expect(
        witness.calls,
        callsBeforeCloseProbe,
        "Canceled callback calls after settlement",
      );
    }
    let neighborResult: bridge.OperationResultEnvelope | undefined;
    if (neighbor !== undefined) {
      expect(
        bridge.completeOperation("reason-neighbor"),
        true,
        "Neighbor completion",
      );
      neighborResult = await neighbor.operation;
    }

    expectCanceled(
      result,
      reasonCase.expected,
      `${reasonCase.expected} cancellation`,
    );
    expectProgress(
      witness,
      ["1:started:false"],
      `${reasonCase.expected} cancellation`,
    );
    if (neighbor !== undefined && neighborResult !== undefined) {
      expectSucceeded(
        neighborResult,
        "controlled-success",
        "Neighbor operation",
      );
      expect(
        neighbor.witness.calls,
        2,
        "Neighbor progress callback calls",
      );
      expectProgress(
        neighbor.witness,
        ["1:started:false", "2:completed:true"],
        "Neighbor operation",
      );
    }
  }
}

async function exerciseFirstReason(): Promise<void> {
  const witness = newProgressWitness();
  const operation = bridge.runOperation(
    "first-reason",
    "late-success",
    observeProgress(witness),
    false,
  );
  expectRequested(
    bridge.requestCancellation("first-reason", "user"),
    "User",
    "First cancellation",
  );
  const repeated = bridge.requestCancellation("first-reason", "timeout");
  expect(repeated.kind, "AlreadyRequested", "Repeated cancellation kind");
  expect(repeated.reason, "User", "Repeated cancellation reason");
  expect(
    bridge.completeOperation("first-reason"),
    true,
    "First-reason completion",
  );
  expectCanceled(await operation, "User", "First-reason operation");
  expect(witness.calls, 2, "First-reason progress callback calls");
  expectProgress(
    witness,
    ["1:started:false", "2:completed:true"],
    "First-reason operation",
  );
}

async function exerciseFailures(
  options: ExerciseOptions,
): Promise<void> {
  if (options.skipExpectedFailure !== true) {
    const expectedWitness = newProgressWitness();
    const expected = bridge.runOperation(
      "expected-failure",
      "expected-failure",
      observeProgress(expectedWitness),
      true,
    );
    expect(
      bridge.completeOperation("expected-failure"),
      true,
      "Expected failure completion",
    );
    expectFailed(
      await expected,
      "Expected",
      "expected-canary-failure",
      "The controlled feature reported an expected failure.",
      "Expected failure",
    );
    const expectedCalls = expectedWitness.calls;
    expect(
      bridge.reportRetainedProgress("expected-failure"),
      true,
      "Expected-failure reporter closed state",
    );
    expect(
      expectedWitness.calls,
      expectedCalls,
      "Expected-failure callback calls after settlement",
    );
  }

  const unexpectedWitness = newProgressWitness();
  const unexpected = bridge.runOperation(
    "unexpected-failure",
    "unexpected-failure",
    observeProgress(unexpectedWitness),
    false,
  );
  expectRequested(
    bridge.requestCancellation("unexpected-failure", "superseded"),
    "Superseded",
    "Unexpected-failure cancellation",
  );
  expect(
    bridge.completeOperation("unexpected-failure"),
    true,
    "Unexpected failure completion",
  );
  expectFailed(
    await unexpected,
    "Unexpected",
    "InvalidOperationException",
    "The controlled feature failed unexpectedly.",
    "Unexpected failure",
  );
  expectProgress(
    unexpectedWitness,
    ["1:started:false"],
    "Unexpected failure",
  );

  const foreignWitness = newProgressWitness();
  const foreignCancellation = bridge.runOperation(
    "foreign-cancellation",
    "foreign-cancellation",
    observeProgress(foreignWitness),
    false,
  );
  expect(
    bridge.completeOperation("foreign-cancellation"),
    true,
    "Foreign cancellation completion",
  );
  expectFailed(
    await foreignCancellation,
    "Unexpected",
    "OperationCanceledException",
    "The controlled feature supplied no accepted reason.",
    "Foreign cancellation",
  );
  expectProgress(
    foreignWitness,
    ["1:started:false"],
    "Foreign cancellation",
  );
}

async function exerciseDuplicateAndReadmission(): Promise<void> {
  const firstWitness = newProgressWitness();
  const first = bridge.runOperation(
    "readmission",
    "late-success",
    observeProgress(firstWitness),
    false,
  );
  const duplicateWitness = newProgressWitness();
  await expectRejected(
    bridge.runOperation(
      "readmission",
      "success",
      observeProgress(duplicateWitness),
      false,
    ),
    "Duplicate active operation",
  );
  expect(duplicateWitness.calls, 0, "Duplicate progress callback calls");
  expect(
    bridge.completeOperation("readmission"),
    true,
    "Original duplicate-ID operation completion",
  );
  expectSucceeded(
    await first,
    "controlled-success",
    "Original duplicate-ID operation",
  );
  expectProgress(
    firstWitness,
    ["1:started:false", "2:completed:true"],
    "Original duplicate-ID operation",
  );

  const readmittedWitness = newProgressWitness();
  const readmitted = bridge.runOperation(
    "readmission",
    "success",
    observeProgress(readmittedWitness),
    false,
  );
  expect(
    bridge.completeOperation("readmission"),
    true,
    "Readmitted operation completion",
  );
  expectSucceeded(
    await readmitted,
    "controlled-success",
    "Readmitted operation",
  );
  expectProgress(
    readmittedWitness,
    ["1:started:false", "2:completed:true"],
    "Readmitted operation",
  );
}

async function exerciseProgressFailure(): Promise<void> {
  let callbackCalls = 0;
  await expectRejected(
    bridge.runOperation(
      "progress-failure",
      "cancel",
      (sequence, phase, isFinal): undefined => {
        expect(sequence, 1, "Throwing callback sequence");
        expect(phase, "started", "Throwing callback phase");
        expect(isFinal, false, "Throwing callback final marker");
        callbackCalls++;
        throw new Error("Canary progress callback failed.");
      },
      true,
    ),
    "Throwing progress callback",
  );
  expect(callbackCalls, 1, "Throwing progress callback calls");

  const cancellation = bridge.requestCancellation(
    "progress-failure",
    "user",
  );
  expect(cancellation.kind, "NotActive", "Settled failure cancellation");
  expect(cancellation.reason, null, "Settled failure cancellation reason");
  expect(
    bridge.reportRetainedProgress("progress-failure"),
    true,
    "Failed reporter closed state",
  );
  expect(callbackCalls, 1, "Failed callback calls after settlement");
}

async function exerciseOptionalProgress(): Promise<void> {
  const result = await bridge.runWithoutProgress("without-progress");
  expectSucceeded(result, "without-progress", "Without-progress operation");
}

async function exerciseCallbackClosure(
  options: ExerciseOptions,
): Promise<void> {
  const witness = newProgressWitness();
  const operation = bridge.runOperation(
    "callback-closure",
    "success",
    observeProgress(witness),
    true,
  );
  expect(
    bridge.completeOperation("callback-closure"),
    true,
    "Callback-closure completion",
  );
  expectSucceeded(
    await operation,
    "controlled-success",
    "Callback-closure operation",
  );
  expect(witness.calls, 2, "Callback-closure progress calls");
  expectProgress(
    witness,
    ["1:started:false", "2:completed:true"],
    "Callback-closure operation",
  );

  if (options.skipRetainedProgress !== true) {
    const callsBeforeCloseProbe = witness.calls;
    expect(
      bridge.reportRetainedProgress("callback-closure"),
      true,
      "Successful reporter closed state",
    );
    expect(
      witness.calls,
      callsBeforeCloseProbe,
      "Successful callback calls after settlement",
    );
  }
}

async function exerciseMalformedInputs(): Promise<void> {
  await expectRejected(
    bridge.runOperation(
      "",
      "success",
      (_sequence, _phase, _isFinal): undefined => undefined,
      false,
    ),
    "Empty operation ID",
  );
  await expectRejected(
    bridge.runOperation(
      "unknown-mode",
      "not-a-mode",
      (_sequence, _phase, _isFinal): undefined => undefined,
      false,
    ),
    "Unknown operation mode",
  );
  expectThrows(
    () => bridge.requestCancellation("unknown-reason", "not-a-reason"),
    "Unknown cancellation reason",
  );
}

export async function exerciseManagedBridge(
  options: ExerciseOptions = {},
): Promise<bridge.VerificationReceipt> {
  await exerciseCancellationReasons(options);
  await exerciseFirstReason();
  await exerciseFailures(options);
  await exerciseDuplicateAndReadmission();
  await exerciseProgressFailure();
  await exerciseOptionalProgress();
  await exerciseCallbackClosure(options);
  await exerciseMalformedInputs();
  return bridge.verifyBaseline();
}

export interface SharedExerciseOptions {
  readonly splitNeighbor?: boolean;
  readonly finishEarly?: boolean;
  readonly skipFinalNatural?: boolean;
}

async function waitForSharedPhase(
  producerId: string,
  phase: "eventsClosed" | "finalizing",
): Promise<bridge.SharedProducerSnapshot> {
  const deadline = performance.now() + 10_000;
  for (;;) {
    const snapshot = bridge.getSharedSnapshot(producerId);
    if (snapshot[phase]) return snapshot;
    if (performance.now() >= deadline) {
      throw new Error(`${producerId} did not reach ${phase}.`);
    }
    // Poll an actual managed phase; elapsed time is only a failure budget.
    await new Promise<void>(resolve => setTimeout(resolve, 0));
  }
}

function expectSharedPending(
  snapshot: bridge.SharedProducerSnapshot,
  settled: number,
  stops: number,
  name: string,
): void {
  expect(snapshot.bodyStarts, 1, `${name} physical starts`);
  expect(snapshot.producerCompleted, false, `${name} physical completion`);
  expect(snapshot.waiterCount, 1, `${name} represented waiters`);
  expect(snapshot.activeOperations, 1, `${name} active operations`);
  expect(snapshot.operations, settled + 1, `${name} tracked operations`);
  expect(snapshot.settledOperations, settled, `${name} settled managed tasks`);
  expect(snapshot.stopRequests, stops, `${name} stop requests`);
  expect(snapshot.producerCanceled, stops === 1, `${name} producer cancellation`);
}

function observeSettlement(operation: Promise<unknown>): { settled: boolean } {
  const witness = { settled: false };
  const markSettled = (): void => { witness.settled = true; };
  void operation.then(markSettled, markSettled);
  return witness;
}

function closeSharedScenario(
  producerId: string,
  witnesses: readonly ProgressWitness[],
): void {
  const calls = witnesses.map(witness => witness.calls);
  expect(bridge.reportSharedProgress(producerId, 99), true, "Settled shared events closed");
  for (const [index, witness] of witnesses.entries()) {
    expect(witness.calls, calls[index], "Shared callback calls after settlement");
  }
  bridge.releaseSharedProducer(producerId);
}

async function exerciseSharedNeighbor(
  observerFails: boolean,
  options: SharedExerciseOptions,
): Promise<void> {
  const producerId = observerFails ? "shared-observer" : "shared-neighbor";
  const firstId = `${producerId}-a`;
  const secondId = `${producerId}-b`;
  bridge.createSharedProducer(producerId, "natural-success");
  const firstWitness = newProgressWitness();
  const secondWitness = newProgressWitness();
  const recordFirst = observeProgress(firstWitness);
  const first = bridge.runSharedOperation(firstId, producerId, (sequence, phase, isFinal): undefined => {
    recordFirst(sequence, phase, isFinal);
    if (observerFails && sequence === 2) throw new Error("Shared observer failed.");
    return undefined;
  });
  expect(firstWitness.calls, 1, "Shared synchronous first admission");
  const neighborProducer = options.splitNeighbor === true ? `${producerId}-split` : producerId;
  if (neighborProducer !== producerId) {
    bridge.createSharedProducer(neighborProducer, "natural-success");
  }
  const second = bridge.runSharedOperation(secondId, neighborProducer, observeProgress(secondWitness));
  const secondSettlement = observeSettlement(second);
  expect(bridge.getSharedSnapshot(producerId).waiterCount, 2, "Shared initial waiter count");
  expect(secondWitness.calls, 0, "Shared neighbor receives no startup replay");
  expect(bridge.reportSharedProgress(producerId, 2), false, "Shared live events");
  if (observerFails) {
    await expectRejected(first, "Shared observer failure");
  } else {
    expectRequested(bridge.requestCancellation(firstId, "user"), "User", "Shared waiter");
    expectCanceled(await first, "User", "Shared waiter");
  }
  expectSharedPending(bridge.getSharedSnapshot(producerId), 1, 0, "Surviving neighbor");
  expect(secondSettlement.settled, false, "Surviving neighbor Promise settlement");
  expect(bridge.reportSharedProgress(producerId, 3), false, "Surviving neighbor event");
  expectProgress(firstWitness, ["1:started:false", "2:shared:false"], "Detached shared waiter");
  expectProgress(secondWitness, ["2:shared:false", "3:shared:false"], "Surviving shared waiter");
  expect(bridge.completeSharedProducer(producerId), true, "Shared producer completion");
  if (options.finishEarly === true) {
    expect(bridge.finishSharedFinalization(producerId), true, "Early finalization mutation");
  }
  const finalizing = await waitForSharedPhase(producerId, "finalizing");
  expectSharedPending(finalizing, 1, 0, "Shared finalization");
  expect(secondSettlement.settled, false, "Finalizing neighbor Promise settlement");
  expect(bridge.finishSharedFinalization(producerId), true, "Shared finalization release");
  expectSucceeded(await second, "shared-success", "Surviving shared waiter");
  expectProgress(secondWitness,
    ["2:shared:false", "3:shared:false", "4:completed:true"], "Completed shared neighbor");
  closeSharedScenario(producerId, [firstWitness, secondWitness]);
}

async function exerciseSharedFinalWaiter(mode: string): Promise<void> {
  const producerId = `final-${mode}`;
  bridge.createSharedProducer(producerId, mode);
  const witness = newProgressWitness();
  const operation = bridge.runSharedOperation(producerId, producerId, observeProgress(witness));
  const settlement = observeSettlement(operation);
  // Attach the rejection observer before releasing a deliberately faulting producer.
  const rejection = mode === "late-failure"
    ? expectRejected(operation, "Late shared producer failure") : undefined;
  expectRequested(bridge.requestCancellation(producerId, "user"), "User", "Final shared waiter");
  const stops = mode === "stop-and-drain" ? 1 : 0;
  const detached = await waitForSharedPhase(producerId, "eventsClosed");
  expectSharedPending(detached, 0, stops, "Final detach");
  expect(settlement.settled, false, "Final detach Promise settlement");
  expect(bridge.reportSharedProgress(producerId, 2), true, "Final detached events closed");
  if (stops === 0) {
    expect(bridge.completeSharedProducer(producerId), true, "Natural final producer completion");
  }
  const finalizing = await waitForSharedPhase(producerId, "finalizing");
  expectSharedPending(finalizing, 0, stops, "Final waiter finalization");
  expect(settlement.settled, false, "Final waiter finalization Promise settlement");
  expect(bridge.reportSharedProgress(producerId, 3), true, "Finalizing events closed");
  expectProgress(witness, ["1:started:false"], "Final canceled waiter");
  expect(bridge.finishSharedFinalization(producerId), true, "Final waiter physical release");
  if (rejection !== undefined) {
    await rejection;
  } else {
    expectCanceled(await operation, "User", "Final shared waiter");
  }
  closeSharedScenario(producerId, [witness]);
}

async function exerciseSharedCancellationOrigin(): Promise<void> {
  const producerId = "shared-cancellation-origin";
  bridge.createSharedProducer(producerId, "origin-cancellation");
  const witness = newProgressWitness();
  const record = observeProgress(witness);
  let cancellation: bridge.CancellationRequestReceipt | undefined;
  const operation = bridge.runSharedOperation(producerId, producerId, (sequence, phase, isFinal): undefined => {
    record(sequence, phase, isFinal);
    cancellation = bridge.requestCancellation(producerId, "user");
    return undefined;
  });
  expect(cancellation?.kind, "Requested", "Reentrant shared cancellation kind");
  expect(cancellation?.reason, "User", "Reentrant shared cancellation reason");
  expectFailed(await operation, "Unexpected", "OperationCanceledException",
    "The shared producer supplied no accepted cancellation.", "Shared cancellation origin");
  expectProgress(witness, ["1:started:false"], "Shared cancellation origin");
  closeSharedScenario(producerId, [witness]);
}

export async function exerciseSharedBridge(
  options: SharedExerciseOptions = {},
): Promise<bridge.SharedVerificationReceipt> {
  await exerciseSharedNeighbor(false, options);
  await exerciseSharedNeighbor(true, {});
  if (options.skipFinalNatural !== true) {
    await exerciseSharedFinalWaiter("natural-success");
  }
  await exerciseSharedFinalWaiter("stop-and-drain");
  await exerciseSharedFinalWaiter("late-failure");
  await exerciseSharedCancellationOrigin();
  return bridge.verifySharedBaseline();
}

export interface EpochExerciseOptions {
  readonly allowance: string;
  readonly started: (registration: number, sequence: number, allowance: string) => void;
  readonly finished: (registration: number, sequence: number) => void;
  readonly finishEarly?: boolean;
  readonly skipReuse?: boolean;
}

function expectEpochOwnership(
  producerId: string,
  activeLeases: number,
  faultRecords: number,
  name: string,
): void {
  const producer = bridge.getSharedSnapshot(producerId);
  const epoch = bridge.getEpochWorkSnapshot();
  expect(producer.waiterCount, 0, `${name} remaining waiters`);
  expect(producer.activeOperations, 0, `${name} active operations`);
  expect(producer.settledOperations, producer.operations, `${name} settled managed tasks`);
  expect(producer.producerCompleted, false, `${name} physical completion`);
  expect(epoch.activeLeases, activeLeases, `${name} active leases`);
  expect(epoch.faultRecords, faultRecords, `${name} fault records`);
  expect(epoch.pendingCallouts, 0, `${name} pending callouts`);
  expect(epoch.registered, true, `${name} reporter registered`);
}

async function cancelEpochWaiter(
  operationId: string,
  operation: Promise<bridge.OperationResultEnvelope>,
): Promise<void> {
  expectRequested(bridge.requestCancellation(operationId, "user"), "User", "Leased waiter");
  expectCanceled(await operation, "User", "Leased waiter");
}

export async function exerciseEpochBridge(
  options: EpochExerciseOptions,
): Promise<bridge.EpochVerificationReceipt> {
  let currentProducer = "";
  const starts: number[] = [];
  const finishes: number[] = [];
  const register = (registration: number, failure?: "start" | "finish"): void => {
    bridge.registerEpochReporter(options.allowance, (sequence, allowance): undefined => {
      const producer = bridge.getSharedSnapshot(currentProducer);
      expect(producer.waiterCount, 1, "Epoch start represented final waiter");
      expect(producer.activeOperations, 1, "Epoch start active operation");
      expect(allowance, options.allowance, "Opaque allowance round trip");
      if (failure === "start") throw new Error("Epoch start observer failed.");
      starts.push(sequence);
      options.started(registration, sequence, allowance);
      return undefined;
    }, (sequence): undefined => {
      expect(bridge.getSharedSnapshot(currentProducer).producerCompleted,
        true, "Epoch finish physical completion");
      finishes.push(sequence);
      options.finished(registration, sequence);
      if (failure === "finish") throw new Error("Epoch finish observer failed.");
      return undefined;
    });
  };

  register(1);
  currentProducer = "epoch-shared";
  bridge.createLeasedSharedProducer(currentProducer, "natural-success");
  const firstWitness = newProgressWitness();
  const first = bridge.runSharedOperation("epoch-a", currentProducer, observeProgress(firstWitness));
  const secondWitness = newProgressWitness();
  const second = bridge.runSharedOperation("epoch-b", currentProducer, observeProgress(secondWitness));
  await cancelEpochWaiter("epoch-a", first);
  expect(starts.length, 0, "Non-final waiter lease allocations");
  await cancelEpochWaiter("epoch-b", second);
  expect(starts.join(","), "1", "Final waiter lease start before settlement");
  expectEpochOwnership(currentProducer, 1, 0, "Epoch handoff");
  expectThrows(() => bridge.unregisterEpochReporter(), "Unregister active epoch reporter");
  bridge.reportSharedProgress(currentProducer, 2);
  expectProgress(firstWitness, ["1:started:false"], "Leased detached callback");
  expectProgress(secondWitness, [], "Leased neighbor callback");

  if (options.skipReuse !== true) {
    const laterWitness = newProgressWitness();
    const later = bridge.runSharedOperation("epoch-later", currentProducer, observeProgress(laterWitness));
    bridge.reportSharedProgress(currentProducer, 3);
    expectProgress(laterWitness, ["3:shared:false"], "Later leased waiter");
    await cancelEpochWaiter("epoch-later", later);
    expect(starts.join(","), "1", "Reused lease start notifications");
    expectEpochOwnership(currentProducer, 1, 0, "Reused epoch handoff");
    bridge.reportSharedProgress(currentProducer, 9);
    expectProgress(laterWitness, ["3:shared:false"], "Later detached callback");
  }
  bridge.completeSharedProducer(currentProducer);
  if (options.finishEarly === true) bridge.finishSharedFinalization(currentProducer);
  await waitForSharedPhase(currentProducer, "finalizing");
  expectEpochOwnership(currentProducer, 1, 0, "Epoch finalization");
  expect(finishes.length, 0, "Finish notifications before physical finalization");
  bridge.finishSharedFinalization(currentProducer);
  expect(await bridge.observeLeasedProducer(currentProducer), "shared-success", "Leased completion");
  expect(finishes.join(","), "1", "Finalized lease notification");
  closeSharedScenario(currentProducer, [firstWitness, secondWitness]);

  currentProducer = "epoch-next";
  bridge.createLeasedSharedProducer(currentProducer, "natural-success");
  const nextWitness = newProgressWitness();
  const next = bridge.runSharedOperation(currentProducer, currentProducer, observeProgress(nextWitness));
  await cancelEpochWaiter(currentProducer, next);
  expect(starts.join(","), "1,2", "Sequence not reused after finish");
  bridge.completeSharedProducer(currentProducer);
  await waitForSharedPhase(currentProducer, "finalizing");
  expectEpochOwnership(currentProducer, 1, 0, "Next epoch producer");
  bridge.finishSharedFinalization(currentProducer);
  expect(await bridge.observeLeasedProducer(currentProducer), "shared-success", "Next leased completion");
  closeSharedScenario(currentProducer, [nextWitness]);

  currentProducer = "epoch-late-failure";
  bridge.createLeasedSharedProducer(currentProducer, "late-failure");
  const lateWitness = newProgressWitness();
  const late = bridge.runSharedOperation(currentProducer, currentProducer, observeProgress(lateWitness));
  await cancelEpochWaiter(currentProducer, late);
  expect(starts.join(","), "1,2,3", "Late producer sequence");
  const lateFailure = expectRejected(bridge.observeLeasedProducer(currentProducer), "Late leased failure");
  const drain = bridge.drainEpochReporter();
  const draining = observeSettlement(drain);
  bridge.completeSharedProducer(currentProducer);
  await waitForSharedPhase(currentProducer, "finalizing");
  expectEpochOwnership(currentProducer, 1, 0, "Late leased finalization");
  expect(draining.settled, false, "Epoch drain before producer finalization");
  expectThrows(() => bridge.unregisterEpochReporter(), "Unregister draining epoch reporter");
  bridge.finishSharedFinalization(currentProducer);
  await lateFailure;
  await drain;
  expect(finishes.join(","), "1,2,3", "Healthy epoch finish notifications");
  closeSharedScenario(currentProducer, [lateWitness]);
  bridge.unregisterEpochReporter();

  for (const [registration, failure] of [[2, "start"], [3, "finish"]] as const) {
    currentProducer = `epoch-${failure}-failure`;
    register(registration, failure);
    bridge.createLeasedSharedProducer(
      currentProducer, failure === "start" ? "stop-and-drain" : "natural-success");
    const witness = newProgressWitness();
    const operation = bridge.runSharedOperation(currentProducer, currentProducer, observeProgress(witness));
    const rejection = failure === "start" ? expectRejected(operation, "Failed lease start") : undefined;
    expectRequested(bridge.requestCancellation(currentProducer, "user"), "User", "Failing epoch waiter");
    if (rejection !== undefined) await rejection;
    else expectCanceled(await operation, "User", "Finish-failure waiter");
    expectEpochOwnership(currentProducer, failure === "start" ? 0 : 1,
      failure === "start" ? 1 : 0, "Failing epoch ownership");
    expect(bridge.getSharedSnapshot(currentProducer).stopRequests,
      failure === "start" ? 1 : 0, "Failed-start permitted stop");
    const observation = expectRejected(
      bridge.observeLeasedProducer(currentProducer), "Failed epoch producer observation");
    const failedDrain = bridge.drainEpochReporter();
    const drainSettlement = observeSettlement(failedDrain);
    const drainFailure = expectRejected(failedDrain, "Failed epoch drain");
    if (failure === "finish") bridge.completeSharedProducer(currentProducer);
    await waitForSharedPhase(currentProducer, "finalizing");
    expect(drainSettlement.settled, false, "Failed epoch drain before physical finalization");
    expectThrows(() => bridge.unregisterEpochReporter(), "Unregister retained failed work");
    bridge.finishSharedFinalization(currentProducer);
    await observation;
    await drainFailure;
    expect(bridge.getEpochWorkSnapshot().activeLeases, 0, "Failed epoch terminal leases");
    expect(bridge.getEpochWorkSnapshot().faultRecords, 0, "Failed epoch terminal fault records");
    closeSharedScenario(currentProducer, [witness]);
    bridge.unregisterEpochReporter();
  }
  expect(starts.join(","), "1,2,3,1", "All accepted start notifications");
  expect(finishes.join(","), "1,2,3,1", "All finish attempts without retry");
  return bridge.verifyEpochBaseline();
}
