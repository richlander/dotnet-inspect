// Executes the compiler-derived JavaScript modules of the production facade set against a
// probe runtime. It proves that the seven independently generated modules compose over one
// shared runtime module, that each one acquires its own managed export assembly, that
// importing a module performs no managed work, and that only the host module's
// `runEntryPoint()` reaches the runtime.
import assert from "node:assert/strict";
import { mkdirSync, readFileSync, writeFileSync } from "node:fs";
import { resolve } from "node:path";
import { pathToFileURL } from "node:url";

interface FacadeIdentity {
  readonly module: string;
  readonly assembly: string;
  readonly root: string;
}

interface JsExportRuntime {
  readonly getAssemblyExports: (assemblyName: string) => Promise<unknown>;
  readonly runMain: (
    mainAssemblyName?: string,
    args?: string[],
  ) => Promise<number>;
}

// The exact production facade set, stated here independently of the generation script so a
// module that silently changes its assembly, root type or membership fails this gate.
const facades: readonly FacadeIdentity[] = [
  { module: "inspect-web-host", assembly: "InspectWeb.Engine", root: "InspectionEngine" },
  {
    module: "inspect-web-package",
    assembly: "InspectWeb.Engine.PackageExports",
    root: "PackageExports",
  },
  {
    module: "inspect-web-metadata",
    assembly: "InspectWeb.Engine.MetadataExports",
    root: "MetadataExports",
  },
  {
    module: "inspect-web-analysis",
    assembly: "InspectWeb.Engine.AnalysisExports",
    root: "AnalysisExports",
  },
  {
    module: "inspect-web-source",
    assembly: "InspectWeb.Engine.SourceExports",
    root: "SourceExports",
  },
  {
    module: "inspect-web-call-graph",
    assembly: "InspectWeb.Engine.CallGraphExports",
    root: "CallGraphExports",
  },
  {
    module: "inspect-web-catalog",
    assembly: "InspectWeb.Engine.CatalogExports",
    root: "CatalogExports",
  },
];

interface RepresentativeOperation {
  readonly name: string;
  readonly args: readonly unknown[];
  readonly key: string;
}

// One representative operation per facade, invoked through its own module so every module
// is proved to dispatch into its own assembly rather than merely to initialize.
const representativeOperations: Readonly<Record<string, RepresentativeOperation>> = {
  "inspect-web-host": {
    name: "buildIdentity",
    args: [],
    key: "BuildIdentity",
  },
  "inspect-web-package": {
    name: "queryPackageVersions",
    args: ["Example.Package"],
    key: "QueryPackageVersions",
  },
  "inspect-web-metadata": {
    name: "queryPackageMetadata",
    args: ["Example.Package", "1.0.0", "net11.0"],
    key: "QueryPackageMetadata",
  },
  "inspect-web-analysis": {
    name: "queryPackageOpportunities",
    args: ["Example.Package", "1.0.0", "net11.0"],
    key: "QueryPackageOpportunities",
  },
  "inspect-web-source": {
    name: "cancelSourceQuery",
    args: [],
    key: "CancelSourceQuery",
  },
  "inspect-web-call-graph": {
    name: "expandPlatformCallGraph",
    args: [
      "net11.0",
      "11.0.0",
      "System.Private.CoreLib.dll",
      "netcore.app",
      "11.0.0.0",
      null,
      null,
      "System.String",
      "Concat",
      "selector",
      0,
    ],
    key: "ExpandPlatformCallGraph",
  },
  "inspect-web-catalog": {
    name: "listVocabulary",
    args: [],
    key: "ListVocabulary",
  },
};

interface ProbeState {
  calls: string[];
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null;
}

function isProbeState(value: unknown): value is ProbeState {
  return isRecord(value)
    && Array.isArray(value.calls)
    && value.calls.every(call => typeof call === "string");
}

interface FacadeModule extends Record<string, unknown> {
  createRuntime(): Promise<JsExportRuntime>;
  initializeRuntime(
    runtime?: JsExportRuntime | PromiseLike<JsExportRuntime>,
  ): Promise<void>;
  runEntryPoint(): Promise<number>;
}

function isFacadeModule(value: unknown): value is FacadeModule {
  return isRecord(value)
    && typeof value.createRuntime === "function"
    && typeof value.initializeRuntime === "function"
    && typeof value.runEntryPoint === "function";
}

interface HostFacade extends FacadeModule {
  asyncLoweringCanary(): Promise<string>;
  configureHost(origin: string): void;
}

function isHostFacade(value: unknown): value is HostFacade {
  return isFacadeModule(value)
    && typeof value.asyncLoweringCanary === "function"
    && typeof value.configureHost === "function";
}

type PublishedOperation = (...args: readonly unknown[]) => unknown;

function isPublishedOperation(value: unknown): value is PublishedOperation {
  return typeof value === "function";
}

function callableOperation(module: FacadeModule, name: string): PublishedOperation {
  const operation = module[name];
  assert.ok(isPublishedOperation(operation),
    `the generated module does not export the operation '${name}'`);
  return operation;
}

const facadeDirectory = process.argv[2];
if (!facadeDirectory) {
  throw new Error("Usage: verify-engine-facade-runtime.ts <compiled-facade-directory>");
}

const resolvedDirectory = resolve(facadeDirectory);

// Dispatch keys carry the managed overload discriminator, so they are read out of the
// generated source rather than guessed. Whether a wrapper awaits its managed result is read
// the same way: the probe must return a string where the wrapper parses one synchronously
// and a promise where it awaits.
interface ManagedOperation {
  readonly key: string;
  readonly asynchronous: boolean;
}

const managedAssemblies: {
  readonly assembly: string;
  readonly root: string;
  readonly operations: readonly ManagedOperation[];
}[] = [];

for (const facade of facades) {
  const modulePath = resolve(resolvedDirectory, `${facade.module}.js`);
  const source = readFileSync(modulePath, "utf8");

  const imports = [...source.matchAll(/^import\s[^;]*?from\s+"([^"]+)";$/gm)]
    .map(match => match[1]);
  assert.deepEqual(imports, ["./_framework/dotnet.js"],
    `${facade.module}.js must acquire the runtime through the one shared SDK module`);

  const acquired = [...source.matchAll(/getAssemblyExports\("([^"]+)"\)/g)]
    .map(match => match[1]);
  assert.deepEqual(acquired, [facade.assembly],
    `${facade.module}.js must acquire exactly its own managed export assembly`);

  const dispatches = [...source.matchAll(
    /export\s+(async\s+)?function\s+\w+\([\s\S]*?\["(\w+)"\]\["([^"]+)"\]/g)];
  const roots = [...new Set(dispatches.map(match => match[2]))];
  assert.deepEqual(roots, [facade.root],
    `${facade.module}.js must dispatch only through its own root type`);
  const operations = dispatches.map(match => ({
    key: match[3] ?? "",
    asynchronous: match[1] !== undefined,
  }));
  assert.ok(operations.length > 0, `${facade.module}.js has no managed dispatch keys`);
  managedAssemblies.push({
    assembly: facade.assembly,
    root: facade.root,
    operations,
  });
}

const frameworkDirectory = resolve(resolvedDirectory, "_framework");
const statePath = resolve(resolvedDirectory, "probe-state.js");
const runtimePath = resolve(frameworkDirectory, "dotnet.js");
mkdirSync(frameworkDirectory, { recursive: true });
writeFileSync(statePath, "export const calls = [];\n");

// One runtime for the whole set, the way the SDK behaves: a repeated `create()` call
// returns the completed runtime instead of building a second one.
writeFileSync(
  runtimePath,
  `import { calls } from "../probe-state.js";
const managedAssemblies = ${JSON.stringify(managedAssemblies)};
const assemblies = new Map(managedAssemblies.map(entry => [entry.assembly, {
  [entry.root]: Object.fromEntries(entry.operations.map(operation => [
    operation.key,
    (...args) => {
      calls.push(\`managed:\${entry.assembly}:\${operation.key}:\${JSON.stringify(args)}\`);
      if (operation.key.startsWith("AsyncLoweringCanary.")) {
        return Promise.resolve("inspect-web-async-lowering-ok");
      }
      return operation.asynchronous ? Promise.resolve("null") : "null";
    },
  ])),
}]));
export const dotnet = {
  async create() {
    calls.push("create");
    return {
      async getAssemblyExports(assemblyName) {
        calls.push(\`exports:\${assemblyName}\`);
        const managed = assemblies.get(assemblyName);
        if (managed === undefined) {
          throw new Error(\`No managed exports for '\${assemblyName}'.\`);
        }
        return managed;
      },
      async runMain(mainAssemblyName, args) {
        calls.push(\`runMain:\${mainAssemblyName ?? "<default>"}:\${args?.length ?? 0}\`);
        return 0;
      },
    };
  },
};
`);

assert.equal(
  Reflect.has(globalThis, "window"),
  false,
  "worker-context canary must run without a window global");

const importedState: unknown = await import(pathToFileURL(statePath).href);
assert.ok(isProbeState(importedState), "probe state module has an unexpected shape");

const loaded = new Map<string, FacadeModule>();
for (const facade of facades) {
  const imported: unknown = await import(
    pathToFileURL(resolve(resolvedDirectory, `${facade.module}.js`)).href);
  assert.ok(isFacadeModule(imported),
    `${facade.module}.js has an unexpected public shape`);
  loaded.set(facade.module, imported);
}

function facadeModule(module: string): FacadeModule {
  const loadedModule = loaded.get(module);
  assert.ok(loadedModule !== undefined, `${module}.js was not loaded`);
  return loadedModule;
}

assert.equal(importedState.calls.length, 0,
  "importing the facade set performed managed work before initialization");

// Eager, ordered, serial initialization: the consumer creates one runtime and every module
// acquires its own assembly through that same handle. The probe returns a fresh runtime for
// every create call, so this assertion cannot pass by SDK-style memoization.
const ownerIdentity = facades.at(0);
assert.ok(ownerIdentity, "the production facade set has no runtime owner");
const owner = facadeModule(ownerIdentity.module);
const runtime = owner.createRuntime();
for (const facade of facades) {
  await facadeModule(facade.module).initializeRuntime(runtime);
}

assert.deepEqual(
  importedState.calls,
  ["create", ...facades.map(facade => `exports:${facade.assembly}`)],
  "the consumer must create once and each facade must acquire its own assembly");

const host = facadeModule("inspect-web-host");
assert.ok(isHostFacade(host), "the host facade has an unexpected public shape");
host.configureHost("https://dotnet-inspect.test");
assert.match(
  importedState.calls.at(-1) ?? "",
  /^managed:InspectWeb\.Engine:ConfigureHost\.-?\d+:\["https:\/\/dotnet-inspect\.test"\]$/);
assert.equal(
  await host.asyncLoweringCanary(),
  "inspect-web-async-lowering-ok");

for (const facade of facades) {
  const representative = representativeOperations[facade.module];
  assert.ok(representative !== undefined,
    `${facade.module} has no representative operation`);
  const invoked: unknown = callableOperation(
    facadeModule(facade.module),
    representative.name,
  )(...representative.args);
  if (invoked instanceof Promise) await invoked;
  assert.match(
    importedState.calls.at(-1) ?? "",
    new RegExp(`^managed:${facade.assembly.replaceAll(".", String.raw`\.`)}:${
      representative.key}\\.-?\\d+:`),
    `${facade.module}.${representative.name}() must dispatch into its own assembly`);
}

assert.equal(await host.runEntryPoint(), 0);
assert.equal(importedState.calls.at(-1), "runMain:<default>:0");
assert.deepEqual(
  importedState.calls.filter(call => call.startsWith("runMain:")),
  ["runMain:<default>:0"],
  "only the host facade runs the managed entry point, exactly once");

console.log("inspect-web generated facade set runtime canary passed.");
