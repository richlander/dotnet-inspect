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
const getStringDtoAsyncKey =
  facadeSource.match(/"(GetStringDtoAsync\.-?\d+)"/)?.[1];
const getKeywordHolderAsyncKey =
  facadeSource.match(/"(GetKeywordHolderAsync\.-?\d+)"/)?.[1];
const getKeywordMapAsyncKey =
  facadeSource.match(/"(GetKeywordMapAsync\.-?\d+)"/)?.[1];
const getBlobAsyncKey =
  facadeSource.match(/"(GetBlobAsync\.-?\d+)"/)?.[1];
const getHiddenTypeJsonIncludeAsyncKey =
  facadeSource.match(/"(GetHiddenTypeJsonIncludeAsync\.-?\d+)"/)?.[1];
const getNullableWidgetAsyncKey =
  facadeSource.match(/"(GetNullableWidgetAsync\.-?\d+)"/)?.[1];
const getJsonElementKey =
  facadeSource.match(/"(GetJsonElement\.-?\d+)"/)?.[1];
const observeValueKey =
  facadeSource.match(/"(ObserveValue\.-?\d+)"/)?.[1];
const transformValueKey =
  facadeSource.match(/"(TransformValue\.-?\d+)"/)?.[1];
const thenMatch = facadeSource.match(
  /export function (operation_[0-9a-f]+)\(value\) \{\n\s+return [^\n]*\["(Then\.-?\d+)"\]\(value\);\n\}/,
);
const thenOperationName = thenMatch?.[1];
const thenKey = thenMatch?.[2];
const undefinedMatch = facadeSource.match(
  /export function (operation_[0-9a-f]+)\(value\) \{\n\s+return [^\n]*\["(Undefined\.-?\d+)"\]\(value\);\n\}/,
);
const undefinedOperationName = undefinedMatch?.[1];
const undefinedKey = undefinedMatch?.[2];
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
assert.ok(
  getStringDtoAsyncKey,
  "The generated GetStringDtoAsync runtime dispatch key was not found.",
);
assert.ok(
  getKeywordHolderAsyncKey,
  "The generated GetKeywordHolderAsync runtime dispatch key was not found.",
);
assert.ok(
  getKeywordMapAsyncKey,
  "The generated GetKeywordMapAsync runtime dispatch key was not found.",
);
assert.ok(
  getBlobAsyncKey,
  "The generated GetBlobAsync runtime dispatch key was not found.",
);
assert.ok(
  getHiddenTypeJsonIncludeAsyncKey,
  "The generated GetHiddenTypeJsonIncludeAsync runtime dispatch key "
    + "was not found.",
);
assert.ok(
  getNullableWidgetAsyncKey,
  "The generated GetNullableWidgetAsync runtime dispatch key was not found.",
);
assert.ok(
  getJsonElementKey,
  "The generated GetJsonElement runtime dispatch key was not found.",
);
assert.ok(
  observeValueKey,
  "The generated ObserveValue runtime dispatch key was not found.",
);
assert.ok(
  transformValueKey,
  "The generated TransformValue runtime dispatch key was not found.",
);
assert.ok(
  undefinedKey,
  "The generated Undefined runtime dispatch key was not found.",
);
assert.ok(
  undefinedOperationName,
  "The generated Undefined facade operation was not found.",
);
assert.ok(thenKey, "The generated Then runtime dispatch key was not found.");
assert.ok(
  thenOperationName,
  "The generated Then facade operation was not found.",
);
assert.notEqual(
  thenOperationName,
  undefinedOperationName,
  "Then and Undefined must have distinct facade operations.",
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
            [getStringDtoAsyncKey]:
              methods.getStringDtoAsync
              ?? (async (value) => JSON.stringify({ value })),
            [getKeywordHolderAsyncKey]:
              methods.getKeywordHolderAsync
              ?? (async (title) => JSON.stringify({
                title,
                inner: { value: title },
                many: [{ value: title }],
                byName: { [title]: { value: title } },
                byteDtos: [{ value: title }],
              })),
            [getKeywordMapAsyncKey]:
              methods.getKeywordMapAsync
              ?? (async (value) => JSON.stringify({
                [value]: { value },
              })),
            [getBlobAsyncKey]:
              methods.getBlobAsync
              ?? (async () => JSON.stringify({
                blob: "AQ==",
                maybeBlob: null,
                blobs: ["AQ==", null],
                blobsByName: { none: null },
              })),
            [getHiddenTypeJsonIncludeAsyncKey]:
              methods.getHiddenTypeJsonIncludeAsync
              ?? (async () => JSON.stringify({ public: "public" })),
            [getNullableWidgetAsyncKey]:
              methods.getNullableWidgetAsync
              ?? (async (name) => JSON.stringify({ name, count: 1 })),
            [getJsonElementKey]:
              methods.getJsonElement
              ?? (() => JSON.stringify({ value: "json" })),
            [observeValueKey]:
              methods.observeValue
              ?? ((callback) => callback(42)),
            [transformValueKey]:
              methods.transformValue
              ?? ((callback) => callback(42, "answer")),
            [undefinedKey]:
              methods.undefinedOperation ?? ((value) => value),
            [thenKey]:
              methods.thenOperation ?? ((value) => value),
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
  assert.equal("then" in facade, false);

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
  assert.deepEqual(
    await facade.getStringDtoAsync("keyword"),
    { value: "keyword" },
  );
  assert.deepEqual(
    await facade.getKeywordHolderAsync("holder"),
    {
      title: "holder",
      inner: { value: "holder" },
      many: [{ value: "holder" }],
      byName: { holder: { value: "holder" } },
      byteDtos: [{ value: "holder" }],
    },
  );
  assert.deepEqual(
    await facade.getKeywordMapAsync("map"),
    { map: { value: "map" } },
  );
  assert.deepEqual(
    await facade.getBlobAsync(),
    {
      blob: "AQ==",
      maybeBlob: null,
      blobs: ["AQ==", null],
      blobsByName: { none: null },
    },
  );
  assert.deepEqual(
    await facade.getNullableWidgetAsync("nullable"),
    { name: "nullable", count: 1 },
  );
  assert.deepEqual(
    await facade.getHiddenTypeJsonIncludeAsync(),
    { public: "public" },
  );
  assert.deepEqual(facade.getJsonElement(), { value: "json" });
  const observed = [];
  facade.observeValue((value) => {
    observed.push(value);
  });
  assert.deepEqual(observed, [42]);
  assert.equal(
    facade.transformValue(
      (value, text) => value === 42 && text === "answer",
    ),
    true,
  );
  assert.equal(facade[undefinedOperationName]("defined"), "defined");
  assert.equal(facade[thenOperationName]("importable"), "importable");
  assert.equal(await facade.runEntryPoint("Main.dll", ["one", "two"]), 37);
  assert.deepEqual(
    scenario.runMainCalls,
    [["Main.dll", ["one", "two"]]],
  );
}

{
  configureScenario({
    exports: managedExports({
      getNullableWidgetAsync: async () => null,
    }),
  });
  const facade = await freshFacade();
  await facade.initializeRuntime();
  await assert.rejects(
    facade.getNullableWidgetAsync("null"),
    /returned null for an authenticated JSON envelope/,
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
