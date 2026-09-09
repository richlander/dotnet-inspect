import assert from "node:assert/strict";
import test from "node:test";

import {
  bindEngineWorkerAnalysisClient,
  engineWorkerAnalysisOperationKinds,
  registerEngineWorkerAnalysisOperations,
  type EngineWorkerAnalysisFacade,
} from "../src/engine-worker-analysis.ts";
import {
  bindEngineWorkerCallGraphClient,
  engineWorkerCallGraphOperationKinds,
  registerEngineWorkerCallGraphOperations,
  type EngineWorkerCallGraphFacade,
} from "../src/engine-worker-call-graph.ts";
import {
  bindEngineWorkerCatalogClient,
  engineWorkerCatalogOperationKinds,
  registerEngineWorkerCatalogOperations,
  type EngineWorkerCatalogFacade,
} from "../src/engine-worker-catalog.ts";
import {
  createEngineWorkerProducerClasses,
  engineWorkerDiagnostic,
  engineWorkerPolicy,
  engineWorkerText,
} from "../src/engine-worker-contract.ts";
import {
  bindEngineWorkerMetadataClient,
  engineWorkerMetadataOperationKinds,
  registerEngineWorkerMetadataOperations,
  type EngineWorkerMetadataFacade,
} from "../src/engine-worker-metadata.ts";
import {
  engineWorkerOrdinaryMaximumInputJsonCharacters,
  engineWorkerOrdinaryMaximumResultJsonCharacters,
  setEngineWorkerBindingPage,
} from "../src/engine-worker-ordinary.ts";
import { engineWorkerCanaryKind } from "../src/engine-worker-contract.ts";
import { engineWorkerCpuKind } from "../src/engine-worker-cpu.ts";
import { engineWorkerPackageQueryKind } from "../src/engine-worker-package-query.ts";
import { engineStartupOperations } from "../src/engine-worker-startup-contract.ts";
import { engineWorkerTypeSourceKind } from "../src/engine-worker-source.ts";
import {
  engineWorkerMemberSourceComparisonKind,
  engineWorkerMethodBodyComparisonKind,
  engineWorkerMethodBodyTargetsKind,
} from "../src/engine-worker-source-authority.ts";
import {
  bindEngineWorkerPackageClient,
  engineWorkerPackageOperationKinds,
  engineWorkerPackageOperations,
  registerEngineWorkerPackageOperations,
  type EngineWorkerPackageFacade,
} from "../src/engine-worker-package.ts";
import {
  bindEngineWorkerStartupClient,
  registerEngineWorkerStartupOperations,
} from "../src/engine-worker-startup.ts";
import {
  bindEngineWorkerOrdinarySourceClient,
  engineWorkerOrdinarySourceOperationKinds,
  registerEngineWorkerOrdinarySourceOperations,
  type EngineWorkerOrdinarySourceFacade,
} from "../src/engine-worker-source-ordinary.ts";
import type { BrowserCallGraph } from "../src/facades/inspect-web-call-graph.d.ts";
import type { BrowserWorkspaceShareDecodeResult } from "../src/facades/inspect-web-catalog.d.ts";
import type { BrowserHeapListing } from "../src/facades/inspect-web-metadata.d.ts";
import type { BrowserPackageAssemblyQueryPattern } from "../src/facades/inspect-web-package.d.ts";
import type { BrowserSource } from "../src/facades/inspect-web-source.d.ts";
import {
  createOperationAuthorityPage,
  type OperationAuthorityPage,
} from "../src/operation-authority.ts";
import {
  FakeWorkerRuntime,
  ManualWorkerRuntimeEnvironment,
  QueueWorkerRuntimeTransportFactory,
  WorkerRuntimeHost,
} from "../src/worker-runtime-core.ts";
import { WorkerOperationCatalog } from "../src/worker-runtime-realm.ts";

const patterns: readonly BrowserPackageAssemblyQueryPattern[] = [{
  id: "implements",
  label: "Implements",
  summary: "Find implementations.",
  maximumOperandLength: 200,
  maximumPackages: 20,
}];

const heap: BrowserHeapListing = {
  assembly: "Example.dll",
  heap: "strings",
  streamName: "#Strings",
  coverage: "Complete",
  entries: [],
  rowsTruncated: false,
  entriesTruncated: false,
  error: null,
};

const source: BrowserSource = {
  provider: "decompiled",
  provenance: "fixture",
  url: null,
  pdbSourceLimitation: null,
  text: "public sealed class Widget {}",
};

const graphNode = {
  label: "Widget.Run",
  status: "Resolved",
  inLoop: false,
  source: null,
  children: [],
  assembly: "Example",
  typeFullName: "Example.Widget",
  memberName: "Run",
};

const graph: BrowserCallGraph = {
  mermaid: "graph TD",
  callers: graphNode,
  callees: graphNode,
  scope: {
    packages: 1,
    assemblies: 1,
    callerAssemblies: 1,
    calleeScope: "Workspace",
  },
  targets: [],
  diagnostics: {
    incompleteNodes: 0,
    incompleteEdges: 0,
    bindingIdentityConflicts: 0,
    hasUnexploredTraversalBoundary: false,
    hasAnalysisFailureBoundary: false,
    isIncomplete: false,
  },
  noBody: false,
};

const decodedShare: BrowserWorkspaceShareDecodeResult = {
  succeeded: false,
  state: null,
  failure: {
    kind: "InvalidPacket",
    path: "$",
    message: "Packet is invalid.",
  },
};

interface Deferred<T> {
  readonly promise: Promise<T>;
  readonly resolve: (value: T) => void;
}

function deferred<T>(): Deferred<T> {
  let resolvePromise: ((value: T) => void) | undefined;
  const promise = new Promise<T>(resolve => {
    resolvePromise = resolve;
  });
  return {
    promise,
    resolve: value => {
      resolvePromise?.(value);
    },
  };
}

function unavailable(): never {
  throw new Error("Unexpected facade call.");
}

interface FixtureOptions {
  readonly loadRuntimePack?: EngineWorkerPackageFacade["loadRuntimePack"];
  readonly runHomeDemo?: EngineWorkerCatalogFacade["runHomeDemo"];
  readonly queryMemberSource?:
    EngineWorkerOrdinarySourceFacade["queryMemberSource"];
  readonly bindingPage?: OperationAuthorityPage;
}

function fixture(options: FixtureOptions = {}) {
  const environment = new ManualWorkerRuntimeEnvironment();
  const calls: Array<{
    readonly operation: string;
    readonly arguments: readonly unknown[];
  }> = [];
  const diagnostics: string[] = [];
  const failures: string[] = [];

  const packageFacade: EngineWorkerPackageFacade = {
    activateWorkspacePackageOccurrence: unavailable,
    clearWorkspacePackageOccurrences() {
      calls.push({ operation: "clearWorkspacePackageOccurrences", arguments: [] });
    },
    getPackageDocument: unavailable,
    getPlatformCatalog: unavailable,
    getPlatformVersions: unavailable,
    listPackageAssemblyQueryPatterns() {
      calls.push({ operation: "listPackageAssemblyQueryPatterns", arguments: [] });
      return patterns;
    },
    loadRuntimePack: options.loadRuntimePack ?? unavailable,
    loadRuntimePackAssembly: unavailable,
    matchPackageDependencyCoordinate: unavailable,
    openPackageAssemblyQueryResult: unavailable,
    packageCacheStats: unavailable,
    prefetchPlatformPacks: unavailable,
    queryMemberDocumentation: unavailable,
    queryPackage: unavailable,
    queryPackageDependencies: unavailable,
    queryPackageVersions: unavailable,
    queryWorkspacePackageOccurrences: unavailable,
    resolvePackageDependencyVersion: unavailable,
    searchTypes: unavailable,
  };
  const metadataFacade: EngineWorkerMetadataFacade = {
    queryGraphMemberSurface: unavailable,
    queryPackageHeapEntries(...arguments_) {
      calls.push({ operation: "queryPackageHeapEntries", arguments: arguments_ });
      return Promise.resolve(heap);
    },
    queryPackageMetadata: unavailable,
    queryPackageMetadataTable: unavailable,
    queryPlatformHeapEntries: unavailable,
    queryPlatformMetadata: unavailable,
    queryPlatformMetadataTable: unavailable,
    queryTypeProjection: unavailable,
  };
  const analysisFacade: EngineWorkerAnalysisFacade = {
    queryMemberFacts: unavailable,
    queryPackageIntegrations: unavailable,
    queryPackageOpportunities: unavailable,
    queryPackagePerformance: unavailable,
    queryPlatformIntegrations: unavailable,
    queryPlatformOpportunities: unavailable,
    queryPlatformPerformance(...arguments_) {
      calls.push({ operation: "queryPlatformPerformance", arguments: arguments_ });
      return Promise.resolve("{\"members\":[]}");
    },
  };
  const sourceFacade: EngineWorkerOrdinarySourceFacade = {
    cancelSourceQuery() {
      calls.push({ operation: "cancelSourceQuery", arguments: [] });
    },
    queryMemberFindingCensus: unavailable,
    queryMemberSource(...arguments_) {
      calls.push({ operation: "queryMemberSource", arguments: arguments_ });
      return options.queryMemberSource?.(...arguments_)
        ?? Promise.resolve(source);
    },
    queryTypeMemberSource: unavailable,
  };
  const callGraphFacade: EngineWorkerCallGraphFacade = {
    expandPlatformCallGraph: unavailable,
    queryMemberCallGraph(...arguments_) {
      calls.push({ operation: "queryMemberCallGraph", arguments: arguments_ });
      return Promise.resolve(graph);
    },
  };
  const catalogFacade: EngineWorkerCatalogFacade = {
    decodeWorkspaceShareState(...arguments_) {
      calls.push({ operation: "decodeWorkspaceShareState", arguments: arguments_ });
      return decodedShare;
    },
    encodeWorkspaceShareState: unavailable,
    resolveHomeDemo(scenarioId) {
      calls.push({ operation: "resolveHomeDemo", arguments: [scenarioId] });
      return { found: false, demo: null };
    },
    runHomeDemo: options.runHomeDemo ?? unavailable,
  };

  const operations = new WorkerOperationCatalog();
  registerEngineWorkerPackageOperations(operations, () => packageFacade);
  registerEngineWorkerMetadataOperations(operations, () => metadataFacade);
  registerEngineWorkerAnalysisOperations(operations, () => analysisFacade);
  registerEngineWorkerOrdinarySourceOperations(operations, () => sourceFacade);
  registerEngineWorkerCallGraphOperations(operations, () => callGraphFacade);
  registerEngineWorkerCatalogOperations(operations, () => catalogFacade);
  registerEngineWorkerStartupOperations(operations, {
    async buildIdentity() {
      return {
        version: "1.0",
        commit: null,
        builtAtUtc: null,
        commitUrl: null,
      };
    },
    listVocabulary: unavailable,
    listHomeDemos: unavailable,
    listPackageQueryFacets: unavailable,
    listGalleryDiscoveryCatalog: unavailable,
  });

  const workers = Array.from({ length: 2 }, () => new FakeWorkerRuntime({
    scheduler: environment,
    bootstrap: {
      decoder: engineWorkerText,
      bootstrap: () => undefined,
    },
    diagnostic: engineWorkerDiagnostic,
    unknownOperationRejection: kind => ({
      error: `Unknown operation: ${kind}`,
      diagnostic: `Unknown operation: ${kind}`,
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
      },
      diagnostic: diagnostic => {
        diagnostics.push(diagnostic.kind);
      },
      realmReleased: () => undefined,
    },
  });
  assert.equal(host.start("https://inspect.example").kind, "started");
  if (options.bindingPage !== undefined) {
    const epoch = host.snapshot().epochToken;
    assert.notEqual(epoch, null);
    setEngineWorkerBindingPage(host, epoch!, options.bindingPage);
  }
  const reportDiagnostic = (diagnostic: { readonly kind: string }) => {
    diagnostics.push(diagnostic.kind);
    return undefined;
  };
  return {
    environment,
    host,
    calls,
    diagnostics,
    failures,
    startup: bindEngineWorkerStartupClient(host, reportDiagnostic),
    package: bindEngineWorkerPackageClient(host, reportDiagnostic),
    metadata: bindEngineWorkerMetadataClient(host, reportDiagnostic),
    analysis: bindEngineWorkerAnalysisClient(host, reportDiagnostic),
    source: bindEngineWorkerOrdinarySourceClient(host, reportDiagnostic),
    callGraph: bindEngineWorkerCallGraphClient(host, reportDiagnostic),
    catalog: bindEngineWorkerCatalogClient(host, reportDiagnostic),
  };
}

test("ordinary and specialized operations share one epoch sequence", async () => {
  let nextId = 1;
  const operationAuthority = createOperationAuthorityPage({
    allocation: { createId: () => `operation-${nextId++}` },
  });
  const state = fixture({ bindingPage: operationAuthority });

  const startup = state.startup.host.buildIdentity();
  await state.environment.flushAsync();
  await startup;
  const ordinary = state.package.listPackageAssemblyQueryPatterns();
  await state.environment.flushAsync();
  await ordinary;

  let nextSequence: number | null = null;
  const session = operationAuthority.createSession<
    null,
    string,
    string,
    string,
    string
  >({
    feature: { publish: () => undefined },
    diagnostic: { report: () => undefined },
  });
  const started = session.start(null, {
    prepare(identity) {
      nextSequence = identity.sequence;
      return { kind: "rejected", error: "expected" };
    },
  });
  assert.equal(started.kind, "rejected");
  assert.equal(nextSequence, 3);
  session.dispose();
});

test("facade bindings preserve exact argument order and generated-shaped results", async () => {
  const state = fixture();
  const packageResult = state.package.listPackageAssemblyQueryPatterns();
  assert.equal(packageResult instanceof Promise, true);
  const results = Promise.all([
    packageResult,
    state.metadata.queryPackageHeapEntries(
      "Example",
      "1.2.3",
      "net11.0",
      "Example.dll",
      "cli",
      "strings",
    ),
    state.analysis.queryPlatformPerformance(
      "net11.0",
      "11.0.0",
      "System.Runtime.dll",
      "Microsoft.NETCore.App.Ref",
    ),
    state.source.queryMemberSource(
      "Example",
      "1.2.3",
      "net11.0",
      "Example.dll",
      "Example.Widget",
      "Run",
      "selector",
      0x06000001,
      "[\"readable-locals\"]",
    ),
    state.callGraph.queryMemberCallGraph(
      "Example",
      "1.2.3",
      "net11.0",
      "Example.dll",
      "Example.Widget",
      "type-query",
      "Run",
      "void Run()",
      "selector",
      0x06000001,
      "{\"packages\":[]}",
    ),
    state.catalog.decodeWorkspaceShareState("share-packet"),
  ]);

  await state.environment.flushAsync();
  assert.deepEqual(await results, [
    patterns,
    heap,
    "{\"members\":[]}",
    source,
    graph,
    decodedShare,
  ]);
  assert.deepEqual(state.calls, [
    { operation: "listPackageAssemblyQueryPatterns", arguments: [] },
    {
      operation: "queryPackageHeapEntries",
      arguments: [
        "Example",
        "1.2.3",
        "net11.0",
        "Example.dll",
        "cli",
        "strings",
      ],
    },
    {
      operation: "queryPlatformPerformance",
      arguments: [
        "net11.0",
        "11.0.0",
        "System.Runtime.dll",
        "Microsoft.NETCore.App.Ref",
      ],
    },
    {
      operation: "queryMemberSource",
      arguments: [
        "Example",
        "1.2.3",
        "net11.0",
        "Example.dll",
        "Example.Widget",
        "Run",
        "selector",
        0x06000001,
        "[\"readable-locals\"]",
      ],
    },
    {
      operation: "queryMemberCallGraph",
      arguments: [
        "Example",
        "1.2.3",
        "net11.0",
        "Example.dll",
        "Example.Widget",
        "type-query",
        "Run",
        "void Run()",
        "selector",
        0x06000001,
        "{\"packages\":[]}",
      ],
    },
    {
      operation: "decodeWorkspaceShareState",
      arguments: ["share-packet"],
    },
  ]);
  assert.deepEqual(state.diagnostics, []);
  state.host.dispose();
});

test("ordinary calls complete independently out of order", async () => {
  const first = deferred<string>();
  const second = deferred<string>();
  let count = 0;
  const state = fixture({
    loadRuntimePack: () => (++count === 1 ? first.promise : second.promise),
  });
  const one = state.package.loadRuntimePack("net11.0", "first");
  const two = state.package.loadRuntimePack("net11.0", "second");
  await state.environment.flushAsync();

  second.resolve("second-pack");
  assert.equal(await two, "second-pack");
  first.resolve("first-pack");
  assert.equal(await one, "first-pack");
  assert.deepEqual(state.failures, []);
  state.host.dispose();
});

test("startup and ordinary bindings share independent operation identity", async () => {
  const state = fixture();
  const identity = state.startup.host.buildIdentity();
  const ordinary = state.package.listPackageAssemblyQueryPatterns();
  await state.environment.flushAsync();

  assert.equal((await identity).version, "1.0");
  assert.deepEqual(await ordinary, patterns);
  assert.deepEqual(state.failures, []);
  state.host.dispose();
});

test("one ordinary failure does not fail a neighboring operation", async () => {
  const state = fixture({
    runHomeDemo: async () => {
      throw new Error("Demo failed.");
    },
  });
  const failure = assert.rejects(
    state.catalog.runHomeDemo("broken"),
    /Demo failed/,
  );
  const neighbor = state.catalog.resolveHomeDemo("neighbor");
  await state.environment.flushAsync();

  await failure;
  assert.deepEqual(await neighbor, { found: false, demo: null });
  assert.equal(state.host.snapshot().phase, "ready");
  assert.deepEqual(state.failures, []);
  state.host.dispose();
});

test("singleton Source cancellation dispatches while a query remains pending", async () => {
  const pending = deferred<BrowserSource>();
  const state = fixture({
    queryMemberSource: () => pending.promise,
  });
  let querySettled = false;
  const query = state.source.queryMemberSource(
    "Example",
    "1.2.3",
    "net11.0",
    "Example.dll",
    "Example.Widget",
    "Run",
    "selector",
    0x06000001,
    "[]",
  ).then(value => {
    querySettled = true;
    return value;
  });
  const cancel = state.source.cancelSourceQuery();
  await state.environment.flushAsync();

  assert.equal(await cancel, undefined);
  assert.equal(querySettled, false);
  pending.resolve(source);
  assert.deepEqual(await query, source);
  state.host.dispose();
});

test("void ordinary operations settle successfully and remain independent", async () => {
  const state = fixture();
  const clear = state.package.clearWorkspacePackageOccurrences();
  const cancel = state.source.cancelSourceQuery();
  await state.environment.flushAsync();

  assert.equal(await clear, undefined);
  assert.equal(await cancel, undefined);
  assert.deepEqual(state.calls, [
    { operation: "clearWorkspacePackageOccurrences", arguments: [] },
    { operation: "cancelSourceQuery", arguments: [] },
  ]);
  state.host.dispose();
});

test("ordinary codecs reject malformed and oversized boundary payloads", () => {
  const input = engineWorkerPackageOperations.loadRuntimePack.arguments.decoder;
  assert.equal(input.decode("[\"net11.0\"]").kind, "rejected");
  assert.equal(input.decode("[42,\"11.0.0\"]").kind, "rejected");
  assert.deepEqual(
    input.decode("x".repeat(
      engineWorkerOrdinaryMaximumInputJsonCharacters + 1,
    )),
    {
      kind: "rejected",
      reason: "oversized",
      message: `Ordinary Worker argument JSON exceeds ${
        engineWorkerOrdinaryMaximumInputJsonCharacters
      } characters.`,
    },
  );

  const result = engineWorkerPackageOperations.loadRuntimePack.result.decoder;
  assert.equal(result.decode("{").kind, "rejected");
  assert.equal(result.decode("{}").kind, "rejected");
  assert.deepEqual(
    result.decode("x".repeat(
      engineWorkerOrdinaryMaximumResultJsonCharacters + 1,
    )),
    {
      kind: "rejected",
      reason: "oversized",
      message: `Ordinary Worker result JSON exceeds ${
        engineWorkerOrdinaryMaximumResultJsonCharacters
      } characters.`,
    },
  );
});

test("oversized page arguments reject visibly without reaching the facade", async () => {
  const state = fixture({
    loadRuntimePack: async () => "must-not-run",
  });
  await assert.rejects(
    state.package.loadRuntimePack(
      "x".repeat(engineWorkerOrdinaryMaximumInputJsonCharacters),
      "11.0.0",
    ),
    /argument JSON exceeds 1048576 characters/,
  );
  assert.deepEqual(state.calls, []);
  state.host.dispose();
});

test("the complete production Worker operation catalog is unique", () => {
  const kinds = [
    ...engineWorkerPackageOperationKinds,
    ...engineWorkerMetadataOperationKinds,
    ...engineWorkerAnalysisOperationKinds,
    ...engineWorkerOrdinarySourceOperationKinds,
    ...engineWorkerCallGraphOperationKinds,
    ...engineWorkerCatalogOperationKinds,
    ...Object.values(engineStartupOperations).map(operation => operation.kind),
    engineWorkerPackageQueryKind,
    engineWorkerTypeSourceKind,
    engineWorkerMethodBodyTargetsKind,
    engineWorkerMethodBodyComparisonKind,
    engineWorkerMemberSourceComparisonKind,
    engineWorkerCpuKind,
    engineWorkerCanaryKind,
  ];
  assert.equal(kinds.length, 56);
  assert.equal(new Set(kinds).size, kinds.length);
});

test("an ordinary client cannot follow a replacement Worker epoch", async () => {
  const state = fixture({
    loadRuntimePack: async () => "pack",
  });
  const pending = assert.rejects(
    state.package.loadRuntimePack("net11.0", "11.0.0"),
    /worker-restarted/,
  );
  state.host.restart();
  await pending;
  assert.equal(state.host.start("https://inspect.example").kind, "started");
  await state.environment.flushAsync();
  await assert.rejects(
    state.package.loadRuntimePack("net11.0", "11.0.0"),
    /closed Worker epoch/,
  );
  state.host.dispose();
});
