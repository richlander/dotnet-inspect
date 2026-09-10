import assert from "node:assert/strict";
import test from "node:test";
import {
  createEngineWorkerProducerClasses,
  engineWorkerDiagnostic,
  engineWorkerPolicy,
  engineWorkerText,
} from "../src/engine-worker-contract.ts";
import {
  bindEngineWorkerOrdinaryClient,
  engineWorkerOrdinaryMaximumCollectionEntries,
  engineWorkerOrdinaryMaximumJsonCharacters,
  engineWorkerOrdinaryMaximumNesting,
  engineWorkerOrdinaryOperationKinds,
  engineWorkerOrdinaryOperations,
  registerEngineWorkerOrdinaryOperations,
  type EngineWorkerOrdinaryFacades,
} from "../src/engine-worker-ordinary.ts";
import {
  FakeWorkerRuntime,
  ManualWorkerRuntimeEnvironment,
  QueueWorkerRuntimeTransportFactory,
  WorkerRuntimeHost,
} from "../src/worker-runtime-core.ts";
import { WorkerOperationCatalog } from "../src/worker-runtime-realm.ts";

type FacadeOverrides = {
  readonly [TGroup in keyof EngineWorkerOrdinaryFacades]?:
    Partial<EngineWorkerOrdinaryFacades[TGroup]>;
};

function unexpected(name: string): never {
  throw new Error(`Unexpected facade call: ${name}.`);
}

// oxlint-disable-next-line typescript/no-unnecessary-type-parameters
function contractViolation<T>(value: unknown): T {
  // Tests use this one cast to exercise runtime rejection beyond declarations.
  // oxlint-disable-next-line typescript/no-unsafe-type-assertion
  return value as T;
}

const defaultFacades: EngineWorkerOrdinaryFacades = {
  package: {
    getPlatformCatalog: () => unexpected("getPlatformCatalog"),
    getPlatformVersions: () => unexpected("getPlatformVersions"),
    listPackageAssemblyQueryPatterns: () =>
      unexpected("listPackageAssemblyQueryPatterns"),
    matchPackageDependencyCoordinate: () =>
      unexpected("matchPackageDependencyCoordinate"),
    searchTypes: () => unexpected("searchTypes"),
    activateWorkspacePackageOccurrence: () =>
      unexpected("activateWorkspacePackageOccurrence"),
    clearWorkspacePackageOccurrences: () =>
      unexpected("clearWorkspacePackageOccurrences"),
    packageCacheStats: () => unexpected("packageCacheStats"),
    prefetchPlatformPacks: () => unexpected("prefetchPlatformPacks"),
    queryPackage: () => unexpected("queryPackage"),
    openPackageAssemblyQueryResult: () =>
      unexpected("openPackageAssemblyQueryResult"),
    loadRuntimePack: () => unexpected("loadRuntimePack"),
    loadRuntimePackAssembly: () =>
      unexpected("loadRuntimePackAssembly"),
    getPackageDocument: () => unexpected("getPackageDocument"),
    queryMemberDocumentation: () =>
      unexpected("queryMemberDocumentation"),
    queryPackageDependencies: () =>
      unexpected("queryPackageDependencies"),
    queryPackageVersions: () => unexpected("queryPackageVersions"),
    queryWorkspacePackageOccurrences: () =>
      unexpected("queryWorkspacePackageOccurrences"),
    resolvePackageDependencyVersion: () =>
      unexpected("resolvePackageDependencyVersion"),
  },
  metadata: {
    queryTypeProjection: () => unexpected("queryTypeProjection"),
    queryPackageMetadataTable: () =>
      unexpected("queryPackageMetadataTable"),
    queryPlatformMetadataTable: () =>
      unexpected("queryPlatformMetadataTable"),
    queryPackageHeapEntries: () =>
      unexpected("queryPackageHeapEntries"),
    queryPlatformHeapEntries: () =>
      unexpected("queryPlatformHeapEntries"),
    queryPackageMetadata: () => unexpected("queryPackageMetadata"),
    queryPlatformMetadata: () => unexpected("queryPlatformMetadata"),
    queryGraphMemberSurface: () =>
      unexpected("queryGraphMemberSurface"),
  },
  analysis: {
    queryMemberFacts: () => unexpected("queryMemberFacts"),
    queryPackageIntegrations: () =>
      unexpected("queryPackageIntegrations"),
    queryPlatformIntegrations: () =>
      unexpected("queryPlatformIntegrations"),
    queryPackageOpportunities: () =>
      unexpected("queryPackageOpportunities"),
    queryPlatformOpportunities: () =>
      unexpected("queryPlatformOpportunities"),
    queryPackagePerformance: () =>
      unexpected("queryPackagePerformance"),
    queryPlatformPerformance: () =>
      unexpected("queryPlatformPerformance"),
  },
  source: {
    queryMemberSource: () => unexpected("queryMemberSource"),
    queryTypeMemberSource: () => unexpected("queryTypeMemberSource"),
    cancelSourceQuery: () => unexpected("cancelSourceQuery"),
    queryMethodBodyComparisonTargets: () =>
      unexpected("queryMethodBodyComparisonTargets"),
    queryMethodBodyComparison: () =>
      unexpected("queryMethodBodyComparison"),
    cancelMethodBodyComparison: () =>
      unexpected("cancelMethodBodyComparison"),
    queryMemberSourceComparison: () =>
      unexpected("queryMemberSourceComparison"),
    cancelMemberSourceComparison: () =>
      unexpected("cancelMemberSourceComparison"),
    queryMemberFindingCensus: () =>
      unexpected("queryMemberFindingCensus"),
  },
  callGraph: {
    queryMemberCallGraph: () => unexpected("queryMemberCallGraph"),
    expandPlatformCallGraph: () =>
      unexpected("expandPlatformCallGraph"),
  },
  catalog: {
    resolveHomeDemo: () => unexpected("resolveHomeDemo"),
    decodeWorkspaceShareState: () =>
      unexpected("decodeWorkspaceShareState"),
    encodeWorkspaceShareState: () =>
      unexpected("encodeWorkspaceShareState"),
    runHomeDemo: () => unexpected("runHomeDemo"),
  },
};

function createFacades(
  overrides: FacadeOverrides = {},
): EngineWorkerOrdinaryFacades {
  return {
    package: { ...defaultFacades.package, ...overrides.package },
    metadata: { ...defaultFacades.metadata, ...overrides.metadata },
    analysis: { ...defaultFacades.analysis, ...overrides.analysis },
    source: { ...defaultFacades.source, ...overrides.source },
    callGraph: { ...defaultFacades.callGraph, ...overrides.callGraph },
    catalog: { ...defaultFacades.catalog, ...overrides.catalog },
  };
}

function deferred<T>() {
  let resolve!: (value: T) => void;
  const promise = new Promise<T>(accept => {
    resolve = accept;
  });
  return { promise, resolve };
}

function fixture(overrides: FacadeOverrides = {}) {
  const environment = new ManualWorkerRuntimeEnvironment();
  const diagnostics: string[] = [];
  const failures: string[] = [];
  const operations = new WorkerOperationCatalog();
  const facades = createFacades(overrides);
  registerEngineWorkerOrdinaryOperations(operations, () => facades);
  const workers = Array.from({ length: 2 }, () =>
    new FakeWorkerRuntime({
      scheduler: environment,
      bootstrap: {
        decoder: engineWorkerText,
        bootstrap: () => undefined,
      },
      diagnostic: engineWorkerDiagnostic,
      unknownOperationRejection: () => ({
        error: "Unknown operation.",
        diagnostic: "Unknown operation.",
      }),
      operations,
      producerClasses: createEngineWorkerProducerClasses(),
    }));
  const host = new WorkerRuntimeHost({
    ...engineWorkerPolicy,
    transport: new QueueWorkerRuntimeTransportFactory(workers),
    clock: environment,
    lifecycle: environment,
    bootstrap: {
      encode: engineWorkerText.decode,
      diagnostic: engineWorkerText,
    },
    diagnostic: engineWorkerText,
    createDiagnostic: (_kind, detail) => engineWorkerDiagnostic(detail),
    producerClasses: createEngineWorkerProducerClasses(),
    callbacks: {
      failure: failure => {
        failures.push(failure.kind);
        return undefined;
      },
      diagnostic: diagnostic => {
        diagnostics.push(diagnostic.kind);
        return undefined;
      },
      realmReleased: () => undefined,
    },
  });
  assert.equal(host.start("https://inspect.example").kind, "started");
  const client = bindEngineWorkerOrdinaryClient(host, diagnostic => {
    diagnostics.push(diagnostic.kind);
    return undefined;
  });
  return {
    client,
    diagnostics,
    environment,
    failures,
    host,
    workers,
  };
}

test("ordinary transport preserves sync, async DTO, void, null, and arguments", async () => {
  const searchResult = [{
    key: "System.String",
    kind: "type",
    future: { nested: [null, true, 42] },
  }];
  const activation = {
    activated: false,
    superseded: false,
    package: null,
    future: { message: "preserved" },
  };
  let cleared = 0;
  let matchArguments: readonly unknown[] = [];
  const state = fixture({
    package: {
      searchTypes: () => searchResult,
      activateWorkspacePackageOccurrence: async () => activation,
      clearWorkspacePackageOccurrences: () => {
        cleared++;
      },
      queryMemberDocumentation: async () =>
        contractViolation(null),
      matchPackageDependencyCoordinate: (...args) => {
        matchArguments = args;
        return { outcome: "Unique", candidateKey: "candidate" };
      },
    },
  });

  const sync = state.client.package.searchTypes("String", "[]");
  const asyncDto =
    state.client.package.activateWorkspacePackageOccurrence("open");
  const voidResult =
    state.client.package.clearWorkspacePackageOccurrences();
  const nullResult = state.client.package.queryMemberDocumentation(
    "Example",
    "1.0.0",
    "net10.0",
    "Example.dll",
    "M:Example.Api.Run",
  );
  const matched = state.client.package.matchPackageDependencyCoordinate(
    "Dependency",
    null,
    "[{\"key\":\"candidate\"}]",
  );
  await state.environment.flushAsync();

  assert.deepEqual(await sync, searchResult);
  assert.deepEqual(await asyncDto, activation);
  assert.equal(await voidResult, undefined);
  assert.equal(await nullResult, null);
  assert.deepEqual(await matched, {
    outcome: "Unique",
    candidateKey: "candidate",
  });
  assert.deepEqual(matchArguments, [
    "Dependency",
    null,
    "[{\"key\":\"candidate\"}]",
  ]);
  assert.equal(cleared, 1);
  assert.deepEqual(state.diagnostics, []);
  state.host.dispose();
});

test("concurrent ordinary calls use independent authority sessions", async () => {
  const first = deferred<string>();
  const second = deferred<string>();
  let calls = 0;
  const state = fixture({
    package: {
      resolvePackageDependencyVersion: () =>
        ++calls === 1 ? first.promise : second.promise,
    },
  });
  const one = state.client.package.resolvePackageDependencyVersion(
    "Dependency",
    "[1.0.0,)",
  );
  const two = state.client.package.resolvePackageDependencyVersion(
    "Dependency",
    "[2.0.0,)",
  );
  await state.environment.flushAsync();
  assert.equal(state.host.snapshot().activeOperations, 2);

  second.resolve("2.1.0");
  assert.equal(await two, "2.1.0");
  assert.equal(state.host.snapshot().activeOperations, 1);
  first.resolve("1.5.0");
  assert.equal(await one, "1.5.0");
  assert.equal(state.host.snapshot().activeOperations, 0);
  assert.deepEqual(state.diagnostics, []);
  state.host.dispose();
});

test("generated rejection fails visibly without poisoning neighboring calls", async () => {
  const state = fixture({
    catalog: {
      async runHomeDemo() {
        throw new Error("Demo generation failed.");
      },
    },
    package: {
      packageCacheStats: () => ({
        packages: 4,
        resident: 2,
        workspaces: 1,
        residentBytes: 1024,
      }),
    },
  });
  const failure = assert.rejects(
    state.client.catalog.runHomeDemo("source"),
    /Demo generation failed/,
  );
  const neighbor = state.client.package.packageCacheStats();
  await state.environment.flushAsync();
  await failure;
  assert.deepEqual(await neighbor, {
    packages: 4,
    resident: 2,
    workspaces: 1,
    residentBytes: 1024,
  });
  assert.equal(state.host.snapshot().phase, "ready");
  assert.deepEqual(state.failures, []);
  state.host.dispose();
});

test("malformed and oversized generated results reject only their calls", async () => {
  const state = fixture({
    package: {
      queryPackageVersions: async () =>
        contractViolation({ versions: [undefined] }),
      loadRuntimePack: async () =>
        "x".repeat(engineWorkerOrdinaryMaximumJsonCharacters),
      packageCacheStats: () => ({
        packages: 1,
        resident: 1,
        workspaces: 0,
        residentBytes: 64,
      }),
    },
  });
  const malformed = assert.rejects(
    state.client.package.queryPackageVersions("Example", "1.0.0"),
    /non-JSON undefined data/,
  );
  const oversized = assert.rejects(
    state.client.package.loadRuntimePack("net10.0", "10.0.0"),
    /exceeds 1048576 characters/,
  );
  const neighbor = state.client.package.packageCacheStats();
  await state.environment.flushAsync();
  await Promise.all([malformed, oversized]);
  assert.equal((await neighbor).residentBytes, 64);
  assert.equal(state.host.snapshot().phase, "ready");
  assert.deepEqual(state.failures, []);
  state.host.dispose();
});

test("malformed and oversized inputs are rejected before facade invocation", async () => {
  let calls = 0;
  const state = fixture({
    package: {
      queryWorkspacePackageOccurrences: async () => {
        calls++;
        return { occurrences: [], superseded: false };
      },
    },
  });
  const accessor = {};
  Object.defineProperty(accessor, "value", {
    enumerable: true,
    get: () => "not data",
  });
  await assert.rejects(
    state.client.package.queryWorkspacePackageOccurrences(
      contractViolation(accessor),
    ),
    /own data property/,
  );
  await assert.rejects(
    state.client.package.queryWorkspacePackageOccurrences(
      "x".repeat(engineWorkerOrdinaryMaximumJsonCharacters),
    ),
    /exceeds 1048576 characters/,
  );
  assert.equal(calls, 0);
  assert.equal(state.host.snapshot().activeOperations, 0);
  state.host.dispose();
});

test("JSON tuple codec rejects unsafe trees and enforces explicit bounds", () => {
  const operation =
    engineWorkerOrdinaryOperations.package
      .matchPackageDependencyCoordinate;
  type Input = Parameters<typeof operation.encodeInput>[0];
  const encode = (value: unknown) =>
    operation.encodeInput(contractViolation<Input>(value));
  assert.equal(operation.input.decode("{").kind, "rejected");
  assert.equal(operation.input.decode("[\"id\",null]").kind, "rejected");

  const cyclic: unknown[] = [];
  cyclic.push(cyclic);
  assert.equal(encode(["id", null, cyclic]).kind, "rejected");

  const sparse: unknown[] = [];
  sparse.length = 1;
  assert.equal(encode(["id", null, sparse]).kind, "rejected");

  const extra = ["id", null, "[]"];
  Object.defineProperty(extra, "extra", {
    value: true,
    enumerable: true,
  });
  assert.equal(encode(extra).kind, "rejected");

  const symbolKey = { value: "candidate" };
  Object.defineProperty(symbolKey, Symbol("hidden"), {
    value: true,
    enumerable: true,
  });
  assert.equal(encode(["id", null, symbolKey]).kind, "rejected");

  assert.equal(encode(["id", null, Number.NaN]).kind, "rejected");
  assert.equal(encode(["id", null, undefined]).kind, "rejected");
  assert.equal(
    encode(["id", null, () => undefined]).kind,
    "rejected",
  );

  let nested: unknown = [];
  for (let index = 0;
    index <= engineWorkerOrdinaryMaximumNesting;
    index++) {
    nested = [nested];
  }
  assert.equal(encode(["id", null, nested]).kind, "rejected");

  const excessive = Array.from(
    { length: engineWorkerOrdinaryMaximumCollectionEntries },
    () => null,
  );
  assert.equal(encode(["id", null, excessive]).kind, "rejected");
});

test("a closed-epoch ordinary client cannot dispatch into a replacement", async () => {
  let calls = 0;
  const state = fixture({
    package: {
      packageCacheStats: () => {
        calls++;
        return {
          packages: 0,
          resident: 0,
          workspaces: 0,
          residentBytes: 0,
        };
      },
    },
  });
  const pending = assert.rejects(
    state.client.package.packageCacheStats(),
    /worker-restarted/,
  );
  state.host.restart();
  await pending;
  assert.equal(
    state.host.start("https://inspect.example").kind,
    "started",
  );
  await state.environment.flushAsync();
  await assert.rejects(
    state.client.package.packageCacheStats(),
    /closed Worker epoch/,
  );
  assert.equal(calls, 0);
  state.host.dispose();
});

test("the page client and Worker catalog expose only the closed allow-list", () => {
  const expected = {
    package: [
      "activateWorkspacePackageOccurrence",
      "clearWorkspacePackageOccurrences",
      "getPackageDocument",
      "getPlatformCatalog",
      "getPlatformVersions",
      "listPackageAssemblyQueryPatterns",
      "loadRuntimePack",
      "loadRuntimePackAssembly",
      "matchPackageDependencyCoordinate",
      "openPackageAssemblyQueryResult",
      "packageCacheStats",
      "prefetchPlatformPacks",
      "queryMemberDocumentation",
      "queryPackage",
      "queryPackageDependencies",
      "queryPackageVersions",
      "queryWorkspacePackageOccurrences",
      "resolvePackageDependencyVersion",
      "searchTypes",
    ],
    metadata: [
      "queryGraphMemberSurface",
      "queryPackageHeapEntries",
      "queryPackageMetadata",
      "queryPackageMetadataTable",
      "queryPlatformHeapEntries",
      "queryPlatformMetadata",
      "queryPlatformMetadataTable",
      "queryTypeProjection",
    ],
    analysis: [
      "queryMemberFacts",
      "queryPackageIntegrations",
      "queryPackageOpportunities",
      "queryPackagePerformance",
      "queryPlatformIntegrations",
      "queryPlatformOpportunities",
      "queryPlatformPerformance",
    ],
    source: [
      "cancelMemberSourceComparison",
      "cancelMethodBodyComparison",
      "cancelSourceQuery",
      "queryMemberFindingCensus",
      "queryMemberSource",
      "queryMemberSourceComparison",
      "queryMethodBodyComparison",
      "queryMethodBodyComparisonTargets",
      "queryTypeMemberSource",
    ],
    callGraph: [
      "expandPlatformCallGraph",
      "queryMemberCallGraph",
    ],
    catalog: [
      "decodeWorkspaceShareState",
      "encodeWorkspaceShareState",
      "resolveHomeDemo",
      "runHomeDemo",
    ],
  } as const;
  const expectedKinds = [
    ...Object.values(engineWorkerOrdinaryOperations.package),
    ...Object.values(engineWorkerOrdinaryOperations.metadata),
    ...Object.values(engineWorkerOrdinaryOperations.analysis),
    ...Object.values(engineWorkerOrdinaryOperations.source),
    ...Object.values(engineWorkerOrdinaryOperations.callGraph),
    ...Object.values(engineWorkerOrdinaryOperations.catalog),
  ].map(operation => operation.kind).sort();
  assert.deepEqual(
    [...engineWorkerOrdinaryOperationKinds].sort(),
    expectedKinds,
  );
  assert.equal(engineWorkerOrdinaryOperationKinds.length, 49);

  const state = fixture();
  const groups = [
    "package",
    "metadata",
    "analysis",
    "source",
    "callGraph",
    "catalog",
  ] as const;
  for (const group of groups) {
    assert.deepEqual(
      Object.keys(state.client[group]).sort(),
      [...expected[group]].sort(),
    );
  }
  for (const excluded of [
    "cancelPackageQuery",
    "requestPackageQueryMatches",
    "runPackageAssemblyQuery",
    "runPackageQuery",
  ]) {
    assert.equal(excluded in state.client.package, false);
  }
  for (const excluded of [
    "cancelTypeSourceQuery",
    "queryTypeSource",
  ]) {
    assert.equal(excluded in state.client.source, false);
  }
  assert.equal("dispatch" in state.client, false);
  assert.equal("invoke" in state.client, false);
  state.host.dispose();
});
