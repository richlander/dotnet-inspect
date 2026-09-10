import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";
import { bindProductionEngineClient } from "../src/engine-worker-client.ts";
import {
  encodeEngineOperationValue,
  engineOperationMaximumCharacters,
  registerEngineWorkerOperations,
  type EngineFacades,
} from "../src/engine-worker-operations.ts";
import { registerEngineWorkerStartupOperations } from "../src/engine-worker-startup.ts";
import {
  createEngineWorkerProducerClasses, engineWorkerDiagnostic, engineWorkerPolicy, engineWorkerText,
} from "../src/engine-worker-contract.ts";
import {
  FakeWorkerRuntime, ManualWorkerRuntimeEnvironment,
  QueueWorkerRuntimeTransportFactory, WorkerRuntimeHost,
} from "../src/worker-runtime-core.ts";
import { WorkerOperationCatalog } from "../src/worker-runtime-realm.ts";

function deferred<T>() {
  let resolve!: (value: T) => void;
  const promise = new Promise<T>(accept => { resolve = accept; });
  return { promise, resolve };
}

function fixture(options: {
  bootstrap?: () => Promise<void>;
  source?: Partial<EngineFacades["source"]>;
} = {}) {
  const environment = new ManualWorkerRuntimeEnvironment();
  const operations = new WorkerOperationCatalog();
  const calls: string[] = [];
  let count = 1;
  let starts = 0;
  const unused = (): never => { throw new Error("Unused fixture facade operation."); };
  const lifecycle = { createRuntime: unused, initializeRuntime: unused, runEntryPoint: unused };
  const facades: EngineFacades = {
    host: {
      ...lifecycle, asyncLoweringCanary: unused, managedCpuCanary: unused,
      buildIdentity: unused, configureHost: unused, drainEpochWorkReporter: unused,
      registerEpochWorkReporter: unused, unregisterEpochWorkReporter: unused,
    },
    package: {
      ...lifecycle,
      activateWorkspacePackageOccurrence: unused, cancelPackageQuery: unused,
      getPlatformCatalog: unused,
      getPackageDocument: unused, listGalleryDiscoveryCatalog: unused,
      listPackageAssemblyQueryPatterns: unused, listPackageQueryFacets: unused,
      loadRuntimePack: unused, loadRuntimePackAssembly: unused,
      openPackageAssemblyQueryResult: unused, queryMemberDocumentation: unused,
      queryPackage: unused, queryPackageDependencies: unused, queryPackageVersions: unused,
      queryWorkspacePackageOccurrences: unused, requestPackageQueryMatches: unused,
      resolvePackageDependencyVersion: unused, runPackageAssemblyQuery: unused, runPackageQuery: unused,
      async getPlatformVersions() { calls.push("platform-versions"); return ["11.0.0"]; },
      async prefetchPlatformPacks() { calls.push("prefetch-platform"); },
      clearWorkspacePackageOccurrences() { calls.push("clear"); count = 0; },
      packageCacheStats() { return { packages: count, resident: 0, workspaces: 0, residentBytes: 0 }; },
      matchPackageDependencyCoordinate(...args: Parameters<EngineFacades["package"]["matchPackageDependencyCoordinate"]>) {
        calls.push(JSON.stringify(args));
        return { outcome: "NoMatch", candidateKey: null };
      },
      searchTypes: () => [],
    },
    catalog: {
      ...lifecycle,
      decodeWorkspaceShareState: unused, listHomeDemos: unused,
      listVocabulary: unused, runHomeDemo: unused,
      resolveHomeDemo: (id: string) => { calls.push(id); return { found: false, demo: null }; },
      encodeWorkspaceShareState: () => { throw new Error("encoding failed"); },
    },
    source: {
      ...lifecycle,
      cancelMemberSourceComparison: unused, cancelMethodBodyComparison: unused,
      cancelTypeSourceQuery: unused, queryMemberAnnotatedSource: unused,
      queryMemberFindingCensus: unused, queryMemberSource: unused,
      queryMemberSourceComparison: unused, queryMethodBodyComparison: unused,
      queryMethodBodyComparisonTargets: unused, queryTypeMemberSource: unused, queryTypeSource: unused,
      cancelSourceQuery() { calls.push("cancel"); },
      ...options.source,
    },
    metadata: {
      ...lifecycle,
      queryGraphMemberSurface: unused, queryPackageHeapEntries: unused,
      queryPackageMetadata: unused, queryPackageMetadataTable: unused,
      queryPlatformHeapEntries: unused, queryPlatformMetadata: unused,
      queryPlatformMetadataTable: unused, queryTypeProjection: unused,
    },
    analysis: {
      ...lifecycle,
      queryCloneCandidates: unused, queryMemberFacts: unused,
      queryPackageIntegrations: unused, queryPackageOpportunities: unused,
      queryPackagePerformance: unused, queryPlatformIntegrations: unused,
      queryPlatformOpportunities: unused, queryPlatformPerformance: unused,
    },
    callGraph: { ...lifecycle, expandPlatformCallGraph: unused, queryMemberCallGraph: unused },
  };
  registerEngineWorkerOperations(operations, () => facades);
  registerEngineWorkerStartupOperations(operations, {
    async buildIdentity() { return { version: "test", commit: null, builtAtUtc: null, commitUrl: null }; },
    async listVocabulary() { return { schema_version: 1, sections: [] }; },
    async listHomeDemos() { return { demos: [] }; },
    async listPackageQueryFacets() { return { facets: [] }; },
    async listGalleryDiscoveryCatalog() {
      return { packageType: { id: "type", label: "Type", summary: "", suggestions: [] }, orders: [] };
    },
  });
  const workers = Array.from({ length: 2 }, () => new FakeWorkerRuntime({
    scheduler: environment,
    bootstrap: {
      decoder: engineWorkerText,
      async bootstrap() { starts++; await options.bootstrap?.(); },
    },
    diagnostic: engineWorkerDiagnostic, operations,
    unknownOperationRejection: kind => ({ error: kind, diagnostic: kind }),
    producerClasses: createEngineWorkerProducerClasses(),
  }));
  const failures: string[] = [];
  const host = new WorkerRuntimeHost({
    ...engineWorkerPolicy,
    transport: new QueueWorkerRuntimeTransportFactory(workers),
    clock: environment, lifecycle: environment,
    bootstrap: { encode: engineWorkerText.decode, diagnostic: engineWorkerText },
    diagnostic: engineWorkerText, createDiagnostic: (_kind, detail) => engineWorkerDiagnostic(detail),
    producerClasses: createEngineWorkerProducerClasses(),
    callbacks: {
      failure: failure => { failures.push(failure.kind); },
      diagnostic: () => undefined, realmReleased: () => undefined,
    },
  });
  assert.equal(host.start("https://inspect.example").kind, "started");
  const client = bindProductionEngineClient(host, () => undefined);
  return { host, client, calls, failures, environment, starts: () => starts };
}

test("production composition shares readiness and exposes seven owning facade groups", async () => {
  const startup = deferred<void>();
  const state = fixture({ bootstrap: () => startup.promise });
  const match = state.client.package.matchPackageDependencyCoordinate("P", null, "[]");
  const demo = state.client.catalog.resolveHomeDemo("demo");
  await state.environment.flushAsync();
  assert.deepEqual(state.calls, []);
  assert.equal(state.starts(), 1);
  startup.resolve();
  await state.client.ready;
  assert.deepEqual(await match, { outcome: "NoMatch", candidateKey: null });
  assert.deepEqual(await demo, { found: false, demo: null });
  assert.deepEqual(Object.keys(state.client).sort(),
    ["analysis", "callGraph", "catalog", "host", "metadata", "package", "ready", "source"]);
  assert.equal(typeof state.client.source.typeSourceAdapter.prepare, "function");
  assert.equal(typeof state.client.source.methodBodyTargetsAdapter.prepare, "function");
  assert.equal(typeof state.client.source.methodBodyComparisonAdapter.prepare, "function");
  assert.equal(typeof state.client.source.memberSourceComparisonAdapter.prepare, "function");
  assert.equal(typeof state.client.package.queryAdapter.requestControl, "function");
  assert.deepEqual(state.calls, ['["P",null,"[]"]', "demo"]);
  assert.deepEqual(await state.client.package.getPlatformVersions("net11.0"), ["11.0.0"]);
  assert.equal(
    await state.client.package.prefetchPlatformPacks("net11.0", "11.0.0"),
    undefined);
  assert.equal((await state.client.package.packageCacheStats()).packages, 1);
  assert.equal(await state.client.package.clearWorkspacePackageOccurrences(), undefined);
  assert.equal((await state.client.package.packageCacheStats()).packages, 0);
  assert.deepEqual(state.calls, [
    '["P",null,"[]"]',
    "demo",
    "platform-versions",
    "prefetch-platform",
    "clear",
  ]);
  state.host.dispose();
});

test("ordinary controls and neighboring reads run while another managed operation awaits", async () => {
  const pending = deferred<Awaited<ReturnType<EngineFacades["source"]["queryMemberSource"]>>>();
  const state = fixture({ source: { queryMemberSource: () => pending.promise } });
  await state.environment.flushAsync();
  await state.client.ready;
  const query = state.client.source.queryMemberSource("P", "1", "net11.0", "P.dll", "T", "M", "", 1, "[]");
  await state.environment.flushAsync();
  await state.client.source.cancelSourceQuery();
  assert.deepEqual(state.calls, ["cancel"]);
  assert.equal((await state.client.catalog.resolveHomeDemo("other")).found, false);
  const value = { provider: "CSharp", provenance: "generated", url: null, pdbSourceLimitation: null, text: "class T {}" };
  pending.resolve(value);
  assert.deepEqual(await query, value);
  state.host.dispose();
});

test("managed rejection stays visible without failing neighboring ordinary reads", async () => {
  const state = fixture();
  await state.environment.flushAsync();
  await state.client.ready;
  await assert.rejects(state.client.catalog.encodeWorkspaceShareState("{}"), /encoding failed/);
  assert.equal((await state.client.package.packageCacheStats()).packages, 1);
  assert.deepEqual(state.failures, []);
  state.host.dispose();
});

test("invalid arguments are rejected before dispatch and a closed epoch never rebinds", async () => {
  const state = fixture();
  await state.environment.flushAsync();
  await state.client.ready;
  await assert.rejects(
    // @ts-expect-error: Verify the runtime boundary rejects incorrectly typed callers.
    state.client.package.matchPackageDependencyCoordinate("P", null, undefined),
    /payload-rejected/,
  );
  assert.deepEqual(state.calls, []);
  state.host.dispose();
  await assert.rejects(state.client.catalog.resolveHomeDemo("old"), /epoch-unavailable|closed Worker epoch/);
  assert.equal(state.starts(), 1);
});

test("one bootstrap failure rejects readiness and all operations without fallback", async () => {
  const state = fixture({ async bootstrap() { throw new Error("unavailable"); } });
  const ready = assert.rejects(state.client.ready, /startup failed/);
  const call = assert.rejects(state.client.catalog.resolveHomeDemo("never"), /startup failed/);
  await state.environment.flushAsync();
  await Promise.all([ready, call]);
  assert.deepEqual(state.calls, []);
  assert.equal(state.starts(), 1);
  state.host.dispose();
});

test("ordinary transport preserves data and rejects accessors, cycles, sparse arrays and non-JSON data", () => {
  const value = JSON.parse('{"__proto__":{"value":1},"nested":[null,true,2,"λ"]}') as unknown;
  assert.deepEqual(JSON.parse(encodeEngineOperationValue(value)), value);
  let called = false;
  const getter = Object.defineProperty({}, "text", { enumerable: true, get() { called = true; return ""; } });
  assert.throws(() => encodeEngineOperationValue(getter), /enumerable data/);
  assert.equal(called, false);
  const cycle: unknown[] = [];
  cycle.push(cycle);
  const sparse: unknown[] = [];
  sparse.length = 2;
  for (const invalid of [cycle, sparse, new Date(), { text: undefined }, { number: Infinity }])
    assert.throws(() => encodeEngineOperationValue(invalid));
  assert.throws(() => encodeEngineOperationValue("x".repeat(engineOperationMaximumCharacters + 1)), /budget/);
});

test("production page has no direct generated imports or main-thread runtime path", () => {
  const source = readFileSync(new URL("../src/dotnet-inspect.ts", import.meta.url), "utf8");
  assert.doesNotMatch(source, /import\(["']\/inspect-web-|createMainThreadEngineClient|startEngine\(/);
  assert.match(source, /createProductionEngineClient/);
  assert.match(source, /createWorkerPackageQueryDataSource/);
  assert.match(source, /engineClient\.source\.typeSourceAdapter\.prepare/);
});

test("every generated production export has a closed ordinary or feature-owned Worker binding", () => {
  const source = readFileSync(new URL("../src/engine-worker-operations.ts", import.meta.url), "utf8");
  const special: Readonly<Record<string, readonly string[]>> = {
    host: ["buildIdentity", "configureHost", "asyncLoweringCanary", "managedCpuCanary",
      "drainEpochWorkReporter", "registerEpochWorkReporter", "unregisterEpochWorkReporter"],
    package: ["listPackageQueryFacets", "listGalleryDiscoveryCatalog",
      "runPackageQuery", "runPackageAssemblyQuery", "cancelPackageQuery", "requestPackageQueryMatches"],
    source: ["queryTypeSource", "cancelTypeSourceQuery",
      "queryMethodBodyComparisonTargets", "queryMethodBodyComparison", "queryMemberSourceComparison",
      "cancelMethodBodyComparison", "cancelMemberSourceComparison"],
    catalog: ["listHomeDemos", "listVocabulary"],
  };
  for (const [group, module] of [
    ["host", "host"], ["package", "package"], ["metadata", "metadata"],
    ["analysis", "analysis"], ["source", "source"], ["callGraph", "call-graph"], ["catalog", "catalog"],
  ]) {
    const declaration = readFileSync(new URL(`../src/facades/inspect-web-${module}.d.ts`, import.meta.url), "utf8");
    for (const [, name] of declaration.matchAll(/export declare function (\w+)\(/g)) {
      if (!name || ["createRuntime", "initializeRuntime", "runEntryPoint"].includes(name)) continue;
      assert.ok(special[group!]?.includes(name) || source.includes(`f.${group}.${name},`),
        `${group}.${name} has no explicit Worker binding.`);
    }
  }
});
