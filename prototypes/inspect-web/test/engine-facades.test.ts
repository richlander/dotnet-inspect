// The coordinator is the application's only runtime composition point, so its behaviour is
// exercised here rather than only read as source. Every `/inspect-web-*.js` specifier resolves
// to a stand-in module that records what the coordinator did, which makes ordering,
// single-flight sharing, failure retention and the one entry-point call observable without a
// browser or a .NET runtime.

import assert from "node:assert/strict";
import { registerHooks } from "node:module";
import test from "node:test";
import { recording, resetRecording } from "./facade-fixtures/facade-state.ts";

const facadeModules = [
  "inspect-web-host",
  "inspect-web-package",
  "inspect-web-metadata",
  "inspect-web-analysis",
  "inspect-web-source",
  "inspect-web-call-graph",
  "inspect-web-catalog",
] as const;

registerHooks({
  resolve(specifier, context, nextResolve) {
    if (!specifier.startsWith("/inspect-web-")) {
      return nextResolve(specifier, context);
    }
    const facade = specifier.slice(1).replace(/\.js$/, "");
    assert.ok(
      (facadeModules as readonly string[]).includes(facade),
      `the coordinator imported an unexpected facade module: ${specifier}`);
    const scenario = context.parentURL === undefined
      ? "default"
      : new URL(context.parentURL).searchParams.get("scenario") ?? "default";
    const fixture = new URL("./facade-fixtures/facade.ts", import.meta.url);
    fixture.searchParams.set("facade", facade);
    fixture.searchParams.set("scenario", scenario);
    return { url: fixture.href, shortCircuit: true };
  },
});

interface EngineCoordinator {
  initializeFacades(): Promise<void>;
  startEngine(origin: string): Promise<void>;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null;
}

function isCoordinator(value: unknown): value is EngineCoordinator {
  return isRecord(value)
    && typeof value.initializeFacades === "function"
    && typeof value.startEngine === "function";
}

// A fresh module instance per scenario, because readiness and startup are retained in module
// scope exactly the way one browser session retains them.
async function loadCoordinator(scenario: string): Promise<EngineCoordinator> {
  const specifier = new URL("../src/engine-facades.ts", import.meta.url);
  specifier.searchParams.set("scenario", scenario);
  const loaded: unknown = await import(specifier.href);
  assert.ok(isCoordinator(loaded), "the coordinator has an unexpected public shape");
  return loaded;
}

test("startup initializes every facade once, in order, serially", async () => {
  resetRecording();
  const coordinator = await loadCoordinator("ordered");

  await coordinator.startEngine("https://dotnet-inspect.test");

  assert.deepEqual(recording.events, [
    ...facadeModules.flatMap(module => [`begin:${module}`, `end:${module}`]),
    "configureHost:https://dotnet-inspect.test",
    "runEntryPoint:inspect-web-host",
  ], "the seven facades initialize in order and serially, host policy is configured before "
    + "the entry point, and only the host facade runs it");
});

test("concurrent callers share one initialization and one entry point", async () => {
  resetRecording();
  const coordinator = await loadCoordinator("concurrent");

  await Promise.all([
    coordinator.startEngine("https://dotnet-inspect.test"),
    coordinator.startEngine("https://dotnet-inspect.test"),
    coordinator.initializeFacades(),
  ]);
  await coordinator.startEngine("https://dotnet-inspect.test");

  for (const module of facadeModules) {
    assert.equal(
      recording.events.filter(event => event === `begin:${module}`).length,
      1,
      `${module} initialized more than once`);
  }
  assert.deepEqual(
    recording.events.filter(event => event.startsWith("runEntryPoint:")),
    ["runEntryPoint:inspect-web-host"],
    "the managed entry point runs exactly once for concurrent callers");
  assert.deepEqual(
    recording.events.filter(event => event.startsWith("configureHost:")),
    ["configureHost:https://dotnet-inspect.test"]);
});

test("the first initialization failure is the failure every caller observes", async () => {
  resetRecording(["inspect-web-metadata"]);
  const coordinator = await loadCoordinator("failure");

  const first = await coordinator.startEngine("https://dotnet-inspect.test")
    .then(() => null, (error: unknown) => error);
  const second = await coordinator.startEngine("https://dotnet-inspect.test")
    .then(() => null, (error: unknown) => error);
  const readiness = await coordinator.initializeFacades()
    .then(() => null, (error: unknown) => error);

  assert.ok(first instanceof Error, "a failed facade must reject startup");
  assert.equal(
    first.message,
    "inspect-web-metadata could not acquire its managed export assembly");
  assert.equal(second, first,
    "a later caller must observe the first failure rather than a second attempt");
  assert.equal(readiness, first);

  // No fallback, no partial readiness: the facades after the failure never initialize, host
  // policy is never configured, and the entry point never runs.
  assert.deepEqual(recording.events, [
    "begin:inspect-web-host",
    "end:inspect-web-host",
    "begin:inspect-web-package",
    "end:inspect-web-package",
    "begin:inspect-web-metadata",
    "fail:inspect-web-metadata",
  ]);
});

test("the coordinator publishes composition only", async () => {
  resetRecording();
  const coordinator = await loadCoordinator("surface");

  assert.deepEqual(
    Object.keys(coordinator).sort(),
    ["initializeFacades", "startEngine"],
    "the coordinator must not re-export managed operations, a runtime, or raw exports");
  const started: unknown =
    await coordinator.startEngine("https://dotnet-inspect.test");
  assert.equal(started, undefined,
    "startup must not hand the application a runtime or a managed export object");
});
