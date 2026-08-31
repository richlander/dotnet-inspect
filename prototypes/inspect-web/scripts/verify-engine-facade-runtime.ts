import assert from "node:assert/strict";
import { mkdirSync, readFileSync, writeFileSync } from "node:fs";
import { dirname, resolve } from "node:path";
import { pathToFileURL } from "node:url";

interface EngineFacade {
  asyncLoweringCanary(): Promise<string>;
  buildIdentity(): unknown;
  configureHost(origin: string): void;
  initializeRuntime(): Promise<void>;
  queryPackageVersions(packageId: string): Promise<readonly string[]>;
  runEntryPoint(): Promise<number>;
}

interface ProbeState {
  calls: string[];
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null;
}

function isEngineFacade(value: unknown): value is EngineFacade {
  return isRecord(value)
    && typeof value.asyncLoweringCanary === "function"
    && typeof value.buildIdentity === "function"
    && typeof value.configureHost === "function"
    && typeof value.initializeRuntime === "function"
    && typeof value.queryPackageVersions === "function"
    && typeof value.runEntryPoint === "function";
}

function isProbeState(value: unknown): value is ProbeState {
  return isRecord(value)
    && Array.isArray(value.calls)
    && value.calls.every(call => typeof call === "string");
}

const facadePath = process.argv[2];
if (!facadePath) {
  throw new Error("Usage: verify-engine-facade-runtime.ts <inspect-web-engine.js>");
}

const resolvedFacadePath = resolve(facadePath);
const facadeDirectory = dirname(resolvedFacadePath);
const source = readFileSync(resolvedFacadePath, "utf8");
const dispatchKeys = [
  ...new Set(
    [...source.matchAll(/\["InspectionEngine"\]\["([^"]+)"\]/g)]
      .map(match => match[1])
      .filter((key): key is string => key !== undefined)),
];
assert.ok(dispatchKeys.length > 0, "generated facade has no managed dispatch keys");

const frameworkDirectory = resolve(facadeDirectory, "_framework");
const statePath = resolve(facadeDirectory, "probe-state.js");
const runtimePath = resolve(frameworkDirectory, "dotnet.js");
mkdirSync(frameworkDirectory, { recursive: true });
writeFileSync(statePath, "export const calls = [];\n");
writeFileSync(
  runtimePath,
  `import { calls } from "../probe-state.js";
const dispatchKeys = ${JSON.stringify(dispatchKeys)};
const exports = { InspectionEngine: Object.fromEntries(dispatchKeys.map(key => [
  key,
  (...args) => {
    calls.push(\`managed:\${key}:\${JSON.stringify(args)}\`);
    if (key.startsWith("AsyncLoweringCanary.")) {
      return Promise.resolve("inspect-web-async-lowering-ok");
    }
    if (key.startsWith("BuildIdentity.")) return "{}";
    if (key.startsWith("QueryPackageVersions.")) return Promise.resolve("[]");
    return undefined;
  },
])) };
export const dotnet = {
  async create() {
    calls.push("create");
    return {
      async getAssemblyExports(assemblyName) {
        calls.push(\`exports:\${assemblyName}\`);
        return exports;
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
const importedFacade: unknown = await import(pathToFileURL(resolvedFacadePath).href);
assert.ok(isEngineFacade(importedFacade), "generated facade has an unexpected public shape");

assert.deepEqual(importedState.calls, []);
await importedFacade.initializeRuntime();
assert.deepEqual(importedState.calls, [
  "create",
  "exports:InspectWeb.Engine",
]);

importedFacade.configureHost("https://dotnet-inspect.test");
assert.match(
  importedState.calls.at(-1) ?? "",
  /^managed:ConfigureHost\.-?\d+:\["https:\/\/dotnet-inspect\.test"\]$/);
assert.equal(
  await importedFacade.asyncLoweringCanary(),
  "inspect-web-async-lowering-ok");
assert.deepEqual(importedFacade.buildIdentity(), {});
assert.deepEqual(await importedFacade.queryPackageVersions("Example.Package"), []);
assert.equal(await importedFacade.runEntryPoint(), 0);
assert.equal(importedState.calls.at(-1), "runMain:<default>:0");

console.log("inspect-web generated facade runtime canary passed.");
