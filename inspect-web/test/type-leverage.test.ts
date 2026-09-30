import assert from "node:assert/strict";
import test from "node:test";
import type {
  BrowserCompileLibraryAvailability,
  BrowserLibraryNamespaceLeverage,
  BrowserLibraryTypeLeverageShard,
} from "../src/facades/inspect-web-analysis.d.ts";
import {
  createTypeLeverageCoordinator,
  projectTypeLeverage,
  typeLeverageMatchesFilter,
} from "../src/type-leverage.ts";
import { createOperationAuthorityPage } from "../src/operation-authority.ts";
import type { TypeLeverageLoadState } from "../src/type-leverage.ts";

const compileLibrary: BrowserCompileLibraryAvailability = {
  status: "Selected",
  targetFramework: "net11.0",
  message: null,
};

const index: BrowserLibraryNamespaceLeverage = {
  schemaVersion: 1,
  outcome: "available",
  methodologyVersion: "structural-salience.v1",
  evidenceMode: "signature",
  disposition: "complete",
  coverage: {
    considered: 8,
    examined: 8,
    unavailable: 0,
    limited: 0,
  },
  namespaces: [
    {
      namespace: "Example.Core",
      typeCount: 4,
      externalIncomingSourceTypeCount: 8,
      topLeverage: true,
    },
    {
      namespace: "Example.Tools",
      typeCount: 2,
      externalIncomingSourceTypeCount: 3,
      topLeverage: false,
    },
  ],
  diagnostics: [],
  failure: null,
  compileLibrary,
};

const shard: BrowserLibraryTypeLeverageShard = {
  schemaVersion: 1,
  outcome: "available",
  methodologyVersion: "structural-salience.v1",
  evidenceMode: "signature",
  namespace: "Example.Core",
  disposition: "complete",
  coverage: {
    considered: 4,
    examined: 4,
    unavailable: 0,
    limited: 0,
  },
  types: [
    {
      typeDefinitionId: "Example.Core.Sea",
      typeDisplay: "Example.Core.Sea",
      designationEligible: true,
      signatureIncomingDegree: 8,
      signatureOutgoingDegree: 1,
      role: "foundation",
      seaLevel: true,
      mountainPeak: false,
    },
    {
      typeDefinitionId: "Example.Core.Both",
      typeDisplay: "Example.Core.Both",
      designationEligible: true,
      signatureIncomingDegree: 8,
      signatureOutgoingDegree: 10,
      role: "hub",
      seaLevel: true,
      mountainPeak: true,
    },
    {
      typeDefinitionId: "Example.Core.Noise",
      typeDisplay: "Example.Core.Noise",
      designationEligible: true,
      signatureIncomingDegree: 7,
      signatureOutgoingDegree: 9,
      role: "hub",
      seaLevel: false,
      mountainPeak: false,
    },
  ],
  seaLevelOrder: [
    "Example.Core.Sea",
    "Example.Core.Both",
    "Example.Core.Noise",
  ],
  mountainPeakOrder: [
    "Example.Core.Both",
    "Example.Core.Noise",
    "Example.Core.Sea",
  ],
  diagnostics: [],
  failure: null,
  compileLibrary,
};

test("owner-issued namespace and Type designations drive presentation", () => {
  const projection = projectTypeLeverage(index, [shard]);
  const sea = projection.byType.get("Example.Core.Sea");
  const both = projection.byType.get("Example.Core.Both");

  assert.equal(projection.byNamespace.get("Example.Core")?.topLeverage, true);
  assert.equal(projection.byNamespace.get("Example.Tools")?.topLeverage, false);
  assert.equal(projection.seaLevelCount, 2);
  assert.equal(projection.mountainPeakCount, 1);
  assert.equal(sea?.seaLevel, true);
  assert.equal(sea?.mountainPeak, false);
  assert.equal(both?.seaLevel, true);
  assert.equal(both?.mountainPeak, true);
  assert.equal(projection.byType.has("Example.Core.Noise"), false);
  assert.equal(typeLeverageMatchesFilter(both, "sea-level"), true);
  assert.equal(typeLeverageMatchesFilter(sea, "mountain-peak"), false);
});

test("the Browser does not derive relative categories from scores", () => {
  const projection = projectTypeLeverage(index, [{
    ...shard,
    types: shard.types.map(row => ({
      ...row,
      seaLevel: false,
      mountainPeak: false,
    })),
  }]);

  assert.equal(projection.byType.size, 0);
  assert.equal(projection.seaLevelCount, 0);
  assert.equal(projection.mountainPeakCount, 0);
});

test("malformed exact-identity orders fail visibly", () => {
  assert.throws(
    () => projectTypeLeverage(index, [{
      ...shard,
      seaLevelOrder: ["Example.Core.Missing"],
    }]),
    /ineligible Type 'Example\.Core\.Missing'/,
  );
});

test("qualified index and shard evidence remain visible", () => {
  const projection = projectTypeLeverage({
    ...index,
    disposition: "Qualified",
    coverage: {
      considered: 8,
      examined: 6,
      unavailable: 1,
      limited: 1,
    },
    diagnostics: ["SIG001: index evidence was unavailable"],
  }, [{
    ...shard,
    diagnostics: ["SIG002: shard evidence was limited"],
  }]);

  assert.equal(projection.disposition, "Qualified");
  assert.deepEqual(projection.coverage, {
    considered: 12,
    examined: 10,
    unavailable: 1,
    limited: 1,
  });
  assert.deepEqual(projection.diagnostics, [
    "SIG001: index evidence was unavailable",
    "SIG002: shard evidence was limited",
  ]);
});

test("operation authority suppresses stale publication and retains caches", async () => {
  interface Request {
    readonly key: string;
    readonly library: string;
  }
  const pending = new Map<
    string,
    (value: BrowserLibraryNamespaceLeverage) => void
  >();
  const published: TypeLeverageLoadState[] = [];
  let current = "A";
  let shardQueries = 0;
  const coordinator = createTypeLeverageCoordinator<Request>({
    operationAuthority: createOperationAuthorityPage(),
    key: request => request.key,
    libraryKey: request => request.library,
    queryIndex: request =>
      new Promise(resolve => pending.set(request.key, resolve)),
    selectNamespaces: () => ["Example.Core"],
    queryShard: async () => {
      shardQueries++;
      return shard;
    },
    isCurrent: request => request.key === current,
    describeError: error => String(error),
    reportOperationDiagnostic: () => undefined,
    publish: state => published.push(state),
  });

  coordinator.request({ key: "A", library: "A" });
  current = "B";
  coordinator.request({ key: "B", library: "B" });
  pending.get("A")?.(index);
  await new Promise(resolve => setTimeout(resolve, 0));
  pending.get("B")?.(index);
  await new Promise(resolve => setTimeout(resolve, 0));

  assert.deepEqual(
    published.map(state => `${state.status}:${state.key}`),
    ["loading:A", "loading:B", "ready:B"],
  );
  assert.equal(shardQueries, 2);

  current = "A";
  coordinator.request({ key: "A", library: "A" });
  assert.equal(published.at(-1)?.status, "ready");
  assert.equal(published.at(-1)?.key, "A");
});

test("retry invalidates the index and namespace-shard caches", async () => {
  interface Request {
    readonly key: string;
    readonly library: string;
  }
  let indexQueries = 0;
  let shardQueries = 0;
  const published: TypeLeverageLoadState[] = [];
  const coordinator = createTypeLeverageCoordinator<Request>({
    operationAuthority: createOperationAuthorityPage(),
    key: request => request.key,
    libraryKey: request => request.library,
    queryIndex: async () => {
      indexQueries++;
      return index;
    },
    selectNamespaces: () => ["Example.Core"],
    queryShard: async () => {
      shardQueries++;
      return shard;
    },
    isCurrent: () => true,
    describeError: error => String(error),
    reportOperationDiagnostic: () => undefined,
    publish: state => published.push(state),
  });

  coordinator.request({ key: "A", library: "library" });
  await new Promise(resolve => setTimeout(resolve, 0));
  coordinator.request({ key: "A", library: "library" });
  coordinator.request({ key: "B", library: "library" });
  await new Promise(resolve => setTimeout(resolve, 0));
  assert.notEqual(coordinator.presentation("B"), null);
  assert.equal(indexQueries, 1);
  assert.equal(shardQueries, 1);

  coordinator.retry({ key: "A", library: "library" });
  assert.equal(coordinator.presentation("B"), null);
  await new Promise(resolve => setTimeout(resolve, 0));
  assert.equal(indexQueries, 2);
  assert.equal(shardQueries, 2);
  assert.equal(published.at(-1)?.status, "ready");
});

test("pre-retry shard completion cannot repopulate caches", async () => {
  interface Request {
    readonly key: string;
    readonly library: string;
  }
  let current = "A";
  let oldBPending = false;
  let releaseOldB = (_value: BrowserLibraryTypeLeverageShard): void => {
    throw new Error("The old B shard request did not start.");
  };
  const shardQueries: string[] = [];
  const toolsShard: BrowserLibraryTypeLeverageShard = {
    ...shard,
    namespace: "Example.Tools",
    types: [],
    seaLevelOrder: [],
    mountainPeakOrder: [],
  };
  const coordinator = createTypeLeverageCoordinator<Request>({
    operationAuthority: createOperationAuthorityPage(),
    key: request => request.key,
    libraryKey: request => request.library,
    queryIndex: async () => index,
    selectNamespaces: request => [
      request.key === "B" ? "Example.Tools" : "Example.Core",
    ],
    queryShard: request => {
      shardQueries.push(request.key);
      if (request.key === "B" && !oldBPending) {
        oldBPending = true;
        return new Promise(resolve => {
          releaseOldB = resolve;
        });
      }
      return Promise.resolve(
        request.key === "B" ? toolsShard : shard,
      );
    },
    isCurrent: request => request.key === current,
    describeError: error => String(error),
    reportOperationDiagnostic: () => undefined,
    publish: () => undefined,
  });

  coordinator.request({ key: "A", library: "library" });
  await new Promise(resolve => setTimeout(resolve, 0));
  current = "B";
  coordinator.request({ key: "B", library: "library" });
  await new Promise(resolve => setTimeout(resolve, 0));
  current = "A";
  coordinator.retry({ key: "A", library: "library" });
  await new Promise(resolve => setTimeout(resolve, 0));
  releaseOldB(toolsShard);
  await new Promise(resolve => setTimeout(resolve, 0));
  current = "B";
  coordinator.request({ key: "B", library: "library" });
  await new Promise(resolve => setTimeout(resolve, 0));

  assert.deepEqual(shardQueries, ["A", "B", "A", "B"]);
});
