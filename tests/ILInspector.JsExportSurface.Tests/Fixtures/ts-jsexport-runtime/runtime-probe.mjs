import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { pathToFileURL } from "node:url";

if (process.argv.length !== 3) {
  throw new Error("Usage: node runtime-probe.mjs <generated-facade.js>");
}

const facadeUrl = pathToFileURL(process.argv[2]);
const facadeSource = readFileSync(process.argv[2], "utf8");
const configureHostKey =
  facadeSource.match(/"(ConfigureHost\.-?\d+)"/)?.[1];
const echoKey = facadeSource.match(/"(Echo\.-?\d+)"/)?.[1];
const getWidgetAsyncKey =
  facadeSource.match(/"(GetWidgetAsync\.-?\d+)"/)?.[1];
const getRuntimeApiAsyncKey =
  facadeSource.match(/"(GetRuntimeApiAsync\.-?\d+)"/)?.[1];
assert.ok(
  configureHostKey,
  "The generated ConfigureHost runtime dispatch key was not found.",
);
assert.ok(echoKey, "The generated Echo runtime dispatch key was not found.");
assert.ok(
  getWidgetAsyncKey,
  "The generated GetWidgetAsync runtime dispatch key was not found.",
);
assert.ok(
  getRuntimeApiAsyncKey,
  "The generated GetRuntimeApiAsync runtime dispatch key was not found.",
);
let importSequence = 0;

function managedExports(methods = {}) {
  return {
    ILInspector: {
      JsExportSurface: {
        TypeScriptFixtures: {
          TypeScriptFixtureExports: {
            [configureHostKey]:
              methods.configureHost ?? (() => {}),
            [echoKey]: methods.echo ?? ((value) => value),
            [getWidgetAsyncKey]:
              methods.getWidgetAsync
              ?? (async (name, count) => JSON.stringify({ name, count })),
            [getRuntimeApiAsyncKey]:
              methods.getRuntimeApiAsync
              ?? (async (value) => JSON.stringify({ value })),
          },
        },
      },
    },
  };
}

function configureScenario(options = {}) {
  const scenario = {
    applicationArguments: [],
    createCalls: 0,
    createError: options.createError,
    diagnosticTracing: [],
    exports: options.exports ?? managedExports(),
    getAssemblyExportsCalls: [],
    runMainCalls: [],
  };

  scenario.runtime = {
    async getAssemblyExports(assemblyName) {
      scenario.getAssemblyExportsCalls.push(assemblyName);
      return scenario.exports;
    },
    async runMain(mainAssemblyName, args) {
      scenario.runMainCalls.push([mainAssemblyName, args]);
      if (options.runMainError !== undefined) {
        throw options.runMainError;
      }

      return options.runMainResult ?? 0;
    },
  };

  globalThis.__tsJsExportScenario = scenario;
  return scenario;
}

async function freshFacade() {
  importSequence++;
  return import(`${facadeUrl.href}?scenario=${importSequence}`);
}

{
  const hostCalls = [];
  const scenario = configureScenario({
    exports: managedExports({
      configureHost: (origin) => hostCalls.push(origin),
      echo: () => "not-json",
      getWidgetAsync: async (name, count) => JSON.stringify({ name, count }),
    }),
    runMainResult: 37,
  });
  const facade = await freshFacade();

  let notInitializedError;
  assert.throws(
    () => facade.echo("before"),
    (error) => {
      notInitializedError = error;
      return /not initialized/.test(error.message);
    },
  );
  assert.throws(
    () => facade.runEntryPoint("Before.dll", ["before"]),
    (error) => error === notInitializedError,
  );

  const initializationResults = await Promise.all([
    facade.initializeRuntime(),
    facade.initializeRuntime(),
  ]);
  await facade.initializeRuntime();

  assert.deepEqual(initializationResults, [undefined, undefined]);
  assert.equal(scenario.createCalls, 1);
  assert.deepEqual(scenario.diagnosticTracing, []);
  assert.deepEqual(scenario.applicationArguments, []);
  assert.deepEqual(
    scenario.getAssemblyExportsCalls,
    ["ILInspector.JsExportSurface.TypeScriptFixtures"],
  );
  assert.deepEqual(hostCalls, []);
  facade.configureHost("https://example.test");
  assert.deepEqual(hostCalls, ["https://example.test"]);
  assert.equal(facade.echo("value"), "not-json");
  assert.deepEqual(
    await facade.getWidgetAsync("widget", 3),
    { name: "widget", count: 3 },
  );
  assert.deepEqual(
    await facade.getRuntimeApiAsync("runtime"),
    { value: "runtime" },
  );
  assert.equal(await facade.runEntryPoint("Main.dll", ["one", "two"]), 37);
  assert.deepEqual(
    scenario.runMainCalls,
    [["Main.dll", ["one", "two"]]],
  );
}

{
  const failure = new Error("create failed");
  const scenario = configureScenario({ createError: failure });
  const facade = await freshFacade();

  const results = await Promise.allSettled([
    facade.initializeRuntime(),
    facade.initializeRuntime(),
  ]);
  assert.equal(results[0].status, "rejected");
  assert.equal(results[0].reason, failure);
  assert.equal(results[1].status, "rejected");
  assert.equal(results[1].reason, failure);
  await assert.rejects(facade.initializeRuntime(), (error) => error === failure);
  assert.throws(() => facade.echo("failed"), (error) => error === failure);
  assert.throws(
    () => facade.runEntryPoint("Failed.dll"),
    (error) => error === failure,
  );
  assert.equal(scenario.createCalls, 1);
}

{
  let getterCalls = 0;
  const exports = {};
  Object.defineProperty(exports, "ILInspector", {
    get() {
      getterCalls++;
      return {};
    },
  });

  configureScenario({ exports });
  const facade = await freshFacade();
  await assert.rejects(facade.initializeRuntime(), /data property/);
  assert.equal(getterCalls, 0);
}

{
  const exports = Object.create({ ILInspector: {} });
  configureScenario({ exports });
  const facade = await freshFacade();
  await assert.rejects(facade.initializeRuntime(), /own data property/);
}

{
  const exports = managedExports();
  const typePath =
    exports.ILInspector.JsExportSurface.TypeScriptFixtures;
  const inheritedMethods = typePath.TypeScriptFixtureExports;
  typePath.TypeScriptFixtureExports = Object.create(inheritedMethods);

  configureScenario({ exports });
  const facade = await freshFacade();
  await assert.rejects(facade.initializeRuntime(), /own data property/);
}

{
  let getterCalls = 0;
  const methods = managedExports();
  const declaringType =
    methods.ILInspector.JsExportSurface.TypeScriptFixtures
      .TypeScriptFixtureExports;
  Object.defineProperty(declaringType, getWidgetAsyncKey, {
    configurable: true,
    get() {
      getterCalls++;
      return async () => "{}";
    },
  });

  configureScenario({ exports: methods });
  const facade = await freshFacade();
  let validationFailure;
  await assert.rejects(facade.initializeRuntime(), (error) => {
    validationFailure = error;
    return /data property/.test(error.message);
  });
  assert.equal(getterCalls, 0);
  assert.throws(
    () => facade.echo("partial"),
    (error) => error === validationFailure,
  );
}

{
  const exports = managedExports();
  const declaringType =
    exports.ILInspector.JsExportSurface.TypeScriptFixtures
      .TypeScriptFixtureExports;
  delete declaringType[echoKey];

  configureScenario({ exports });
  const facade = await freshFacade();
  await assert.rejects(facade.initializeRuntime(), /own data property/);
}

{
  const exports = managedExports();
  const declaringType =
    exports.ILInspector.JsExportSurface.TypeScriptFixtures
      .TypeScriptFixtureExports;
  declaringType[echoKey] = 42;

  configureScenario({ exports });
  const facade = await freshFacade();
  await assert.rejects(facade.initializeRuntime(), /not callable/);
}

{
  const managedFailure = new Error("managed export failed");
  configureScenario({
    exports: managedExports({
      echo: () => {
        throw managedFailure;
      },
      getWidgetAsync: async () => "not-json",
    }),
  });
  const facade = await freshFacade();
  await facade.initializeRuntime();

  assert.throws(
    () => facade.echo("failure"),
    (error) => error === managedFailure,
  );
  await assert.rejects(
    facade.getWidgetAsync("failure", 1),
    SyntaxError,
  );
}

{
  const failure = new Error("runMain failed");
  const scenario = configureScenario({ runMainError: failure });
  const facade = await freshFacade();
  await facade.initializeRuntime();

  await assert.rejects(
    facade.runEntryPoint("Main.dll", ["argument"]),
    (error) => error === failure,
  );
  assert.deepEqual(
    scenario.runMainCalls,
    [["Main.dll", ["argument"]]],
  );
}

delete globalThis.__tsJsExportScenario;
console.log("ts-jsexport runtime probe passed.");
