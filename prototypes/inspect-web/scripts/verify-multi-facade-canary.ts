import assert from "node:assert/strict";
import { resolve } from "node:path";
import { pathToFileURL } from "node:url";

interface RuntimeWitness {
  readonly runtimeId: number;
}

interface RuntimeRegistry {
  getDotnetRuntime(runtimeId: number): unknown;
}

interface CanaryCoordinator {
  initializeFacades(): Promise<void>;
}

interface AlphaFacade {
  initializeRuntime(): Promise<void>;
}

interface ExerciseModule {
  exerciseFacades(
    options?: { readonly skipBetaSecondary?: boolean },
  ): Promise<unknown>;
}

type Scenario =
  | "baseline"
  | "skip-beta-initialization"
  | "skip-beta-operation";

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null;
}

function isCoordinator(value: unknown): value is CanaryCoordinator {
  return isRecord(value) && typeof value.initializeFacades === "function";
}

function isAlphaFacade(value: unknown): value is AlphaFacade {
  return isRecord(value) && typeof value.initializeRuntime === "function";
}

function isExerciseModule(value: unknown): value is ExerciseModule {
  return isRecord(value) && typeof value.exerciseFacades === "function";
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
      "Usage: verify-multi-facade-canary.ts "
        + "<published-wwwroot> "
        + "[baseline|skip-beta-initialization|skip-beta-operation]",
    );
  }
  if (
    scenarioArgument !== "baseline"
    && scenarioArgument !== "skip-beta-initialization"
    && scenarioArgument !== "skip-beta-operation"
  ) {
    throw new Error(`Unknown multi-facade scenario: ${scenarioArgument}`);
  }
  const scenario: Scenario = scenarioArgument;
  const site = resolve(siteArgument);

  if (scenario === "skip-beta-initialization") {
    const alpha: unknown = await import(
      pathToFileURL(resolve(site, "facades/alpha.js")).href);
    assert.ok(isAlphaFacade(alpha), "Alpha facade has an unexpected shape.");
    await alpha.initializeRuntime();
  } else {
    const coordinator: unknown = await import(
      pathToFileURL(resolve(site, "coordinator.js")).href);
    assert.ok(
      isCoordinator(coordinator),
      "Multi-facade coordinator has an unexpected shape.",
    );
    const readinessA = coordinator.initializeFacades();
    const readinessB = coordinator.initializeFacades();
    assert.strictEqual(
      readinessA,
      readinessB,
      "Concurrent readiness callers did not share one initialization.",
    );
    await Promise.all([readinessA, readinessB]);
  }

  const runtimeIds = registeredRuntimes().map(runtime => runtime.runtimeId);
  assert.equal(
    runtimeIds.length,
    1,
    "Expected exactly one live SDK runtime for both facades.",
  );

  const exercise: unknown = await import(
    pathToFileURL(resolve(site, "exercise.js")).href);
  assert.ok(
    isExerciseModule(exercise),
    "Multi-facade exercise module has an unexpected shape.",
  );
  const receipt = await exercise.exerciseFacades({
    skipBetaSecondary: scenario === "skip-beta-operation",
  });
  assert.deepEqual(receipt, {
    alphaAssembly: "alpha",
    betaAssembly: "beta",
    alphaFlavor: "Chocolate",
    betaFlavor: "Vanilla",
  });

  console.log(
    "ts-jsexport multi-facade Browser/Wasm canary passed "
      + "(one runtime; Alpha + Beta sync, overload, async, record, and enum).",
  );
}

await main().catch((error: unknown) => {
  console.error(error);
  process.exitCode = 1;
});
