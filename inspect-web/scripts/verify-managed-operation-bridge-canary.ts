import assert from "node:assert/strict";
import { resolve } from "node:path";
import { pathToFileURL } from "node:url";
import {
  decodeWorkerToMainEnvelope,
  WORKER_RUNTIME_PROTOCOL_VERSION,
  type WorkerLivenessAllowance,
} from "../src/worker-runtime-protocol.ts";

interface RuntimeWitness {
  readonly runtimeId: number;
}

interface RuntimeRegistry {
  getDotnetRuntime(runtimeId: number): unknown;
}

interface CanaryInitializer {
  initializeCanary(): Promise<void>;
}

interface ExerciseModule {
  exerciseManagedBridge(options?: {
    readonly cancelNeighbor?: boolean;
    readonly skipExpectedFailure?: boolean;
    readonly skipRetainedProgress?: boolean;
  }): Promise<unknown>;
  exerciseSharedBridge(options?: {
    readonly splitNeighbor?: boolean;
    readonly finishEarly?: boolean;
    readonly skipFinalNatural?: boolean;
  }): Promise<unknown>;
  exerciseEpochBridge(options: {
    readonly allowance: string;
    readonly started: (registration: number, sequence: number, allowance: string) => void;
    readonly finished: (registration: number, sequence: number) => void;
    readonly finishEarly?: boolean;
    readonly skipReuse?: boolean;
  }): Promise<unknown>;
}

type Scenario =
  | "baseline"
  | "wrong-cancellation-target"
  | "skip-expected-failure"
  | "skip-retained-progress"
  | "split-shared-neighbor"
  | "early-shared-finalization"
  | "skip-final-natural"
  | "early-epoch-finalization"
  | "skip-epoch-reuse";

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null;
}

function isInitializer(value: unknown): value is CanaryInitializer {
  return isRecord(value) && typeof value.initializeCanary === "function";
}

function isExerciseModule(value: unknown): value is ExerciseModule {
  return isRecord(value)
    && typeof value.exerciseManagedBridge === "function"
    && typeof value.exerciseSharedBridge === "function"
    && typeof value.exerciseEpochBridge === "function";
}

function isRuntimeRegistry(value: unknown): value is RuntimeRegistry {
  return isRecord(value) && typeof value.getDotnetRuntime === "function";
}

function registeredRuntimes(): RuntimeWitness[] {
  const runtimeGlobal: unknown = globalThis;
  assert.ok(
    isRuntimeRegistry(runtimeGlobal),
    "The Browser/Wasm runtime registry is unavailable.",
  );

  const runtimes: RuntimeWitness[] = [];
  for (let runtimeId = 0; runtimeId < 1024; runtimeId++) {
    const candidate = runtimeGlobal.getDotnetRuntime(runtimeId);
    if (isRecord(candidate) && candidate.runtimeId === runtimeId) {
      runtimes.push({ runtimeId });
    }
  }
  return runtimes;
}

async function main(): Promise<void> {
  const siteArgument = process.argv[2];
  const scenarioArgument = process.argv[3] ?? "baseline";
  if (!siteArgument) {
    throw new Error(
      "Usage: verify-managed-operation-bridge-canary.ts "
        + "<published-wwwroot> "
        + "[baseline|wrong-cancellation-target|skip-expected-failure"
        + "|skip-retained-progress|split-shared-neighbor"
        + "|early-shared-finalization|skip-final-natural"
        + "|early-epoch-finalization|skip-epoch-reuse]",
    );
  }
  if (
    scenarioArgument !== "baseline"
    && scenarioArgument !== "wrong-cancellation-target"
    && scenarioArgument !== "skip-expected-failure"
    && scenarioArgument !== "skip-retained-progress"
    && scenarioArgument !== "split-shared-neighbor"
    && scenarioArgument !== "early-shared-finalization"
    && scenarioArgument !== "skip-final-natural"
    && scenarioArgument !== "early-epoch-finalization"
    && scenarioArgument !== "skip-epoch-reuse"
  ) {
    throw new Error(
      `Unknown managed-operation bridge scenario: ${scenarioArgument}`,
    );
  }
  const scenario: Scenario = scenarioArgument;
  const site = resolve(siteArgument);

  const initializer: unknown = await import(
    pathToFileURL(resolve(site, "initialize.js")).href);
  assert.ok(
    isInitializer(initializer),
    "Managed-operation bridge initializer has an unexpected shape.",
  );
  const readinessA = initializer.initializeCanary();
  const readinessB = initializer.initializeCanary();
  assert.strictEqual(
    readinessA,
    readinessB,
    "Concurrent readiness callers did not share one initialization.",
  );
  await Promise.all([readinessA, readinessB]);

  const runtimeIds = registeredRuntimes().map(runtime => runtime.runtimeId);
  assert.equal(
    runtimeIds.length,
    1,
    "Expected exactly one live SDK runtime for the bridge canary.",
  );

  const exercise: unknown = await import(
    pathToFileURL(resolve(site, "exercise.js")).href);
  assert.ok(
    isExerciseModule(exercise),
    "Managed-operation bridge exercise module has an unexpected shape.",
  );
  const receipt = await exercise.exerciseManagedBridge({
    cancelNeighbor: scenario === "wrong-cancellation-target",
    skipExpectedFailure: scenario === "skip-expected-failure",
    skipRetainedProgress: scenario === "skip-retained-progress",
  });
  assert.deepEqual(receipt, {
    status: "managed-operation-bridge:baseline-ok",
    bodyStarts: 15,
    withoutProgressStarts: 1,
    cancellationRequests: 10,
    completions: 8,
    retainedReports: 4,
    duplicateBoundaryFailures: 1,
    progressBoundaryFailures: 1,
    malformedInputFailures: 3,
    otherBoundaryFailures: 0,
  });
  const sharedReceipt = await exercise.exerciseSharedBridge({
    splitNeighbor: scenario === "split-shared-neighbor",
    finishEarly: scenario === "early-shared-finalization",
    skipFinalNatural: scenario === "skip-final-natural",
  });
  assert.deepEqual(sharedReceipt, {
    status: "managed-operation-bridge:shared-ok",
    producerStarts: 6,
    waiterCalls: 8,
    succeededWaiters: 2,
    canceledWaiters: 3,
    failedWaiters: 1,
    observerFailures: 1,
    cleanupFailures: 1,
    otherBoundaryFailures: 0,
    stopRequests: 1,
    releasedProducers: 6,
  });
  console.log(
    "Shared-waiter boundary: 6 producers, 8 waiters; "
      + "independent cancellation and observer isolation; natural and stopped "
      + "finalization drained; late failure rejected; producer cancellation stayed Failed.",
  );
  const allowance: WorkerLivenessAllowance = {
    kind: "bounded",
    maxSilentActiveMilliseconds: 25_000,
  };
  const started: [number, number][] = [];
  const finished: [number, number][] = [];
  const epochReceipt = await exercise.exerciseEpochBridge({
    allowance: JSON.stringify(allowance),
    started: (registration, sequence, rawAllowance) => {
      const decodedAllowance: unknown = JSON.parse(rawAllowance);
      const envelope = {
        protocolVersion: WORKER_RUNTIME_PROTOCOL_VERSION,
        epochToken: registration,
        kind: "epoch-work-started",
        workSequence: sequence,
        allowance: decodedAllowance,
      };
      assert.deepEqual(envelope.allowance, allowance);
      assert.deepEqual(decodeWorkerToMainEnvelope(envelope, registration),
        { kind: "success", value: envelope });
      started.push([registration, sequence]);
    },
    finished: (registration, sequence) => {
      const envelope = {
        protocolVersion: WORKER_RUNTIME_PROTOCOL_VERSION,
        epochToken: registration,
        kind: "epoch-work-finished",
        workSequence: sequence,
      };
      assert.deepEqual(decodeWorkerToMainEnvelope(envelope, registration),
        { kind: "success", value: envelope });
      finished.push([registration, sequence]);
    },
    finishEarly: scenario === "early-epoch-finalization",
    skipReuse: scenario === "skip-epoch-reuse",
  });
  assert.deepEqual(epochReceipt, {
    status: "managed-operation-bridge:epoch-ok",
    registrations: 3,
    producerStarts: 5,
    waiterCalls: 7,
    canceledWaiters: 6,
    boundaryFailures: 1,
    startAttempts: 5,
    finishAttempts: 4,
    completedObservations: 2,
    failedObservations: 3,
    drainFailures: 2,
    unregistrations: 3,
    releasedProducers: 5,
  });
  assert.deepEqual(started, [[1, 1], [1, 2], [1, 3], [3, 1]]);
  assert.deepEqual(finished, started);
  console.log(
    "Epoch-work boundary: final waiter settled with operations=0, waiters=0, lease=1; "
      + "later waiter reused the lease; finish followed physical finalization; "
      + "late producer, start, and finish failures remained observable.",
  );

  console.log(
    "Managed-operation bridge Browser/Wasm canary passed "
      + "(generated facade, Task/Promise results, keyed cancellation, "
      + "synchronous progress, rejection, and quiescent callback release).",
  );
}

await main().catch((error: unknown) => {
  console.error(error);
  process.exitCode = 1;
});
