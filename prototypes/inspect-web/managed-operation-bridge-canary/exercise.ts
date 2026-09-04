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
  operation: string,
): void {
  expect(result.kind, "Failed", `${operation} result kind`);
  expect(result.failureKind, failureKind, `${operation} failure kind`);
  expect(result.error, error, `${operation} error`);
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
