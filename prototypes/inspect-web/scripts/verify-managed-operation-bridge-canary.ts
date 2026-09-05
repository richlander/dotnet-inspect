import assert from "node:assert/strict";
import { resolve } from "node:path";
import { pathToFileURL } from "node:url";

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
}

type Scenario =
  | "baseline"
  | "wrong-cancellation-target"
  | "skip-expected-failure"
  | "skip-retained-progress";

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null;
}

function isInitializer(value: unknown): value is CanaryInitializer {
  return isRecord(value) && typeof value.initializeCanary === "function";
}

function isExerciseModule(value: unknown): value is ExerciseModule {
  return isRecord(value) && typeof value.exerciseManagedBridge === "function";
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
        + "|skip-retained-progress]",
    );
  }
  if (
    scenarioArgument !== "baseline"
    && scenarioArgument !== "wrong-cancellation-target"
    && scenarioArgument !== "skip-expected-failure"
    && scenarioArgument !== "skip-retained-progress"
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
