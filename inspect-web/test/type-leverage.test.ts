import assert from "node:assert/strict";
import test from "node:test";
import type {
  BrowserCompileLibraryAvailability,
  BrowserLibraryNamespaceLeverageIndex,
  BrowserLibraryStructuralSalience,
  BrowserLibraryTypeLeverageShard,
} from "../src/facades/inspect-web-analysis.d.ts";
import {
  createTypeLeverageCoordinator,
  projectTypeLeverage,
  typeLeverageFeedback,
} from "../src/type-leverage.ts";
import { createOperationAuthorityPage } from "../src/operation-authority.ts";
import type { TypeLeverageLoadState } from "../src/type-leverage.ts";

const compileLibrary: BrowserCompileLibraryAvailability = {
  status: "Selected",
  targetFramework: "net11.0",
  message: null,
};

const index: BrowserLibraryNamespaceLeverageIndex = {
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
};

const shard: BrowserLibraryTypeLeverageShard = {
  namespace: "Example.Core",
  disposition: "complete",
  signatureCoverage: {
    considered: 4,
    examined: 4,
    unavailable: 0,
    limited: 0,
  },
  bodyCoverage: null,
  types: [
    {
      typeDefinitionId: "Example.Core.Sea",
      typeDisplay: "Example.Core.Sea",
      designationEligible: true,
      incomingDegree: 8,
      outgoingDegree: 1,
      role: "foundation",
      pole: "SeaLevel",
    },
    {
      typeDefinitionId: "Example.Core.Peak",
      typeDisplay: "Example.Core.Peak",
      designationEligible: true,
      incomingDegree: 8,
      outgoingDegree: 10,
      role: "hub",
      pole: "MountainPeak",
    },
    {
      typeDefinitionId: "Example.Core.Tie",
      typeDisplay: "Example.Core.Tie",
      designationEligible: true,
      incomingDegree: 8,
      outgoingDegree: 8,
      role: "hub",
      pole: null,
    },
  ],
  seaLevelOrder: [
    "Example.Core.Sea",
    "Example.Core.Peak",
    "Example.Core.Tie",
  ],
  mountainPeakOrder: [
    "Example.Core.Peak",
    "Example.Core.Tie",
    "Example.Core.Sea",
  ],
  diagnostics: [],
};

const toolsShard: BrowserLibraryTypeLeverageShard = {
  ...shard,
  namespace: "Example.Tools",
  types: [],
  seaLevelOrder: [],
  mountainPeakOrder: [],
};

const bodyCoverage = {
  bodiesConsidered: 8,
  bodiesExamined: 8,
  bodiesPhysicalOnly: 0,
  bodiesUnavailable: 0,
  bodiesLimited: 0,
  operandsConsidered: 16,
  operandsExamined: 16,
  operandsUnavailable: 0,
  operandsLimited: 0,
};

const bodyShard: BrowserLibraryTypeLeverageShard = {
  ...shard,
  bodyCoverage,
  types: [],
  seaLevelOrder: [],
  mountainPeakOrder: [],
};

const bodyToolsShard: BrowserLibraryTypeLeverageShard = {
  ...bodyShard,
  namespace: "Example.Tools",
};

const document: BrowserLibraryStructuralSalience = {
  schemaVersion: 2,
  surface: {
    outcome: "available",
    methodologyVersion: "structural-salience.v3",
    evidenceMode: "signature",
    namespaceIndex: index,
    typeLeverageShards: [shard, toolsShard],
    failure: null,
    failureKind: null,
  },
  implementation: {
    outcome: "available",
    methodologyVersion: "structural-salience.v3",
    evidenceMode: "body-use",
    namespaceIndex: null,
    typeLeverageShards: [bodyShard, bodyToolsShard],
    failure: null,
    failureKind: null,
  },
  compileLibrary,
};

test("owner-issued namespace and Type designations drive presentation", () => {
  const projection = projectTypeLeverage(document);
  const sea = projection.byType.get("Example.Core.Sea");
  const peak = projection.byType.get("Example.Core.Peak");

  assert.equal(projection.byNamespace.get("Example.Core")?.topLeverage, true);
  assert.equal(projection.byNamespace.get("Example.Tools")?.topLeverage, false);
  assert.deepEqual(
    projection.namespaceOrder.map(row => row.namespace),
    ["Example.Core", "Example.Tools"],
  );
  assert.deepEqual(
    projection.shardsByNamespace.get("Example.Core")
      ?.seaLevelOrder.map(row => row.typeDefinitionId),
    shard.seaLevelOrder,
  );
  assert.deepEqual(
    projection.shardsByNamespace.get("Example.Core")
      ?.mountainPeakOrder.map(row => row.typeDefinitionId),
    shard.mountainPeakOrder,
  );
  assert.equal(projection.seaLevelCount, 1);
  assert.equal(projection.mountainPeakCount, 1);
  assert.equal(sea?.[0]?.pole, "sea-level");
  assert.equal(peak?.[0]?.pole, "mountain-peak");
  assert.equal(projection.byType.has("Example.Core.Tie"), false);
});

test("aligned and opposing evidence modes retain two independent poles", () => {
  const implementationShard: BrowserLibraryTypeLeverageShard = {
    ...bodyShard,
    types: [
      {
        typeDefinitionId: "Example.Core.Sea",
        typeDisplay: "Example.Core.Sea",
        designationEligible: true,
        incomingDegree: 12,
        outgoingDegree: 1,
        role: "foundation",
        pole: "SeaLevel",
      },
      {
        typeDefinitionId: "Example.Core.Peak",
        typeDisplay: "Example.Core.Peak",
        designationEligible: true,
        incomingDegree: 11,
        outgoingDegree: 2,
        role: "foundation",
        pole: "SeaLevel",
      },
    ],
    seaLevelOrder: ["Example.Core.Sea", "Example.Core.Peak"],
    mountainPeakOrder: ["Example.Core.Peak", "Example.Core.Sea"],
  };
  const projection = projectTypeLeverage({
    ...document,
    implementation: {
      ...document.implementation,
      typeLeverageShards: [implementationShard, bodyToolsShard],
    },
  });

  assert.deepEqual(
    projection.byType.get("Example.Core.Sea")?.map(cue => [
      cue.evidenceMode,
      cue.pole,
    ]),
    [
      ["surface", "sea-level"],
      ["implementation", "sea-level"],
    ],
  );
  assert.deepEqual(
    projection.byType.get("Example.Core.Peak")?.map(cue => [
      cue.evidenceMode,
      cue.pole,
    ]),
    [
      ["surface", "mountain-peak"],
      ["implementation", "sea-level"],
    ],
  );
  assert.equal(projection.seaLevelCount, 3);
  assert.equal(projection.mountainPeakCount, 1);
});

test("physical-only implementation evidence keeps surface salience complete", () => {
  const detail = "Compiler-generated body retained as physical-only evidence; its Type uses are excluded from logical-owner relationships.";
  const projection = projectTypeLeverage({
    ...document,
    implementation: {
      ...document.implementation,
      typeLeverageShards: [bodyShard, bodyToolsShard].map(value => ({
        ...value,
        disposition: "qualified",
        bodyCoverage: { ...bodyCoverage, bodiesExamined: 7, bodiesPhysicalOnly: 1 },
        diagnostics: [detail, detail],
      })),
    },
  });

  assert.equal(projection.disposition, "complete");
  assert.deepEqual(projection.diagnostics, []);
  assert.equal(
    projection.byType.get("Example.Core.Sea")?.[0]?.evidenceMode,
    "surface",
  );
  assert.deepEqual(projection.warnings, [
    "Implementation Type leverage has qualified evidence; surface leverage is independent",
    `Implementation Type leverage: ${detail}`,
  ]);
});

test("implementation unavailability retains surface cues and warning", () => {
  const projection = projectTypeLeverage({
    ...document,
    implementation: {
      outcome: "unavailable",
      methodologyVersion: null,
      evidenceMode: "body-use",
      namespaceIndex: null,
      typeLeverageShards: [],
      failure: "The Library is reference-only.",
      failureKind: "NoImplementationAssembly",
    },
  });

  assert.equal(
    projection.byType.get("Example.Core.Sea")?.[0]?.evidenceMode,
    "surface",
  );
  assert.deepEqual(projection.warnings, [
    "Implementation Type leverage is unavailable (NoImplementationAssembly): The Library is reference-only.",
  ]);
});

test("the Browser does not derive relative categories from scores", () => {
  const projection = projectTypeLeverage({
    ...document,
    surface: {
      ...document.surface,
      typeLeverageShards: [{
        ...shard,
        types: shard.types.map(row => ({
          ...row,
          pole: null,
        })),
      }, toolsShard],
    },
  });

  assert.equal(projection.byType.size, 0);
  assert.equal(projection.seaLevelCount, 0);
  assert.equal(projection.mountainPeakCount, 0);
});

test("malformed exact-identity orders fail visibly", () => {
  assert.throws(
    () => projectTypeLeverage({
      ...document,
      surface: {
        ...document.surface,
        typeLeverageShards: [{
          ...shard,
          seaLevelOrder: ["Example.Core.Missing"],
        }, toolsShard],
      },
    }),
    /ineligible Type 'Example\.Core\.Missing'/,
  );
});

test("the exhaustive wire shape requires schema version two", () => {
  assert.throws(
    () => projectTypeLeverage({
      ...document,
      schemaVersion: 3,
    }),
    /Unsupported structural-salience schema version/,
  );
});

test("the exhaustive document must cover every namespace in index order", () => {
  assert.throws(
    () => projectTypeLeverage({
      ...document,
      surface: {
        ...document.surface,
        typeLeverageShards: [shard],
      },
    }),
    /does not cover every namespace/,
  );
  assert.throws(
    () => projectTypeLeverage({
      ...document,
      surface: {
        ...document.surface,
        typeLeverageShards: [toolsShard, shard],
      },
    }),
    /Expected Type-leverage shard 'Example\.Core' at index 0/,
  );
});

test("qualified index and shard evidence remain visible", () => {
  const projection = projectTypeLeverage({
    ...document,
    surface: {
      ...document.surface,
      namespaceIndex: {
        ...index,
        disposition: "Qualified",
        coverage: {
          considered: 8,
          examined: 6,
          unavailable: 1,
          limited: 1,
        },
        diagnostics: ["SIG001: index evidence was unavailable"],
      },
      typeLeverageShards: [{
        ...shard,
        diagnostics: ["SIG002: shard evidence was limited"],
      }, toolsShard],
    },
  });

  assert.equal(projection.disposition, "Qualified");
  assert.deepEqual(projection.coverage, {
    considered: 16,
    examined: 14,
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
    (value: BrowserLibraryStructuralSalience) => void
  >();
  const published: TypeLeverageLoadState[] = [];
  let current = "A";
  let documentQueries = 0;
  const coordinator = createTypeLeverageCoordinator<Request>({
    operationAuthority: createOperationAuthorityPage(),
    key: request => request.key,
    libraryKey: request => request.library,
    queryDocument: request => {
      documentQueries++;
      return new Promise(resolve => pending.set(request.key, resolve));
    },
    isCurrent: request => request.key === current,
    describeError: error => String(error),
    reportOperationDiagnostic: () => undefined,
    publish: state => published.push(state),
  });

  coordinator.request({ key: "A", library: "A" });
  current = "B";
  coordinator.request({ key: "B", library: "B" });
  pending.get("A")?.(document);
  await new Promise(resolve => setTimeout(resolve, 0));
  pending.get("B")?.(document);
  await new Promise(resolve => setTimeout(resolve, 0));

  assert.deepEqual(
    published.map(state => `${state.status}:${state.key}`),
    ["loading:A", "loading:B", "ready:B"],
  );
  assert.equal(documentQueries, 2);

  current = "A";
  coordinator.request({ key: "A", library: "A" });
  assert.equal(published.at(-1)?.status, "ready");
  assert.equal(published.at(-1)?.key, "A");
});

test("concurrent presentations share one pending exhaustive document", async () => {
  interface Request {
    readonly key: string;
    readonly library: string;
    readonly lane: string;
  }
  let release = (_value: BrowserLibraryStructuralSalience): void => {
    throw new Error("The document request did not start.");
  };
  let documentQueries = 0;
  let current = "A";
  const coordinator = createTypeLeverageCoordinator<Request>({
    operationAuthority: createOperationAuthorityPage(),
    key: request => request.key,
    libraryKey: request => request.library,
    operationLane: request => request.lane,
    queryDocument: () => {
      documentQueries++;
      return new Promise(resolve => {
        release = resolve;
      });
    },
    isCurrent: request => request.key === current,
    describeError: error => String(error),
    reportOperationDiagnostic: () => undefined,
    publish: () => undefined,
  });

  coordinator.request({ key: "A", library: "library", lane: "type" });
  current = "B";
  coordinator.request({ key: "B", library: "library", lane: "metrics" });

  assert.equal(documentQueries, 1);
  assert.equal(coordinator.pending("A"), true);
  assert.equal(coordinator.pending("B"), true);

  release(document);
  await new Promise(resolve => setTimeout(resolve, 0));

  assert.equal(coordinator.pending("A"), false);
  assert.equal(coordinator.pending("B"), false);
  assert.notEqual(coordinator.presentation("B"), null);
});

test("retry invalidates the exhaustive document cache", async () => {
  interface Request {
    readonly key: string;
    readonly library: string;
  }
  let documentQueries = 0;
  const published: TypeLeverageLoadState[] = [];
  const coordinator = createTypeLeverageCoordinator<Request>({
    operationAuthority: createOperationAuthorityPage(),
    key: request => request.key,
    libraryKey: request => request.library,
    queryDocument: async () => {
      documentQueries++;
      return document;
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
  assert.equal(documentQueries, 1);

  coordinator.retry({ key: "A", library: "library" });
  assert.equal(coordinator.presentation("B"), null);
  await new Promise(resolve => setTimeout(resolve, 0));
  assert.equal(documentQueries, 2);
  assert.equal(published.at(-1)?.status, "ready");

  coordinator.request({ key: "B", library: "library" });
  await new Promise(resolve => setTimeout(resolve, 0));
  assert.notEqual(coordinator.presentation("B"), null);
  assert.equal(documentQueries, 2);
});

test("pre-retry document completion cannot repopulate caches", async () => {
  interface Request {
    readonly key: string;
    readonly library: string;
  }
  let current = "A";
  let releaseOld = (_value: BrowserLibraryStructuralSalience): void => {
    throw new Error("The old document request did not start.");
  };
  let documentQueries = 0;
  const replacementDocument: BrowserLibraryStructuralSalience = {
    ...document,
    surface: {
      ...document.surface,
      typeLeverageShards: [{
        ...shard,
        types: shard.types.map(row => ({ ...row, pole: null })),
      }, toolsShard],
    },
  };
  const coordinator = createTypeLeverageCoordinator<Request>({
    operationAuthority: createOperationAuthorityPage(),
    key: request => request.key,
    libraryKey: request => request.library,
    queryDocument: () => {
      documentQueries++;
      if (documentQueries === 1) {
        return new Promise(resolve => {
          releaseOld = resolve;
        });
      }
      return Promise.resolve(replacementDocument);
    },
    isCurrent: request => request.key === current,
    describeError: error => String(error),
    reportOperationDiagnostic: () => undefined,
    publish: () => undefined,
  });

  coordinator.request({ key: "A", library: "library" });
  await new Promise(resolve => setTimeout(resolve, 0));
  coordinator.retry({ key: "A", library: "library" });
  await new Promise(resolve => setTimeout(resolve, 0));
  releaseOld(document);
  await new Promise(resolve => setTimeout(resolve, 0));
  current = "B";
  coordinator.request({ key: "B", library: "library" });
  await new Promise(resolve => setTimeout(resolve, 0));

  assert.equal(documentQueries, 2);
  assert.equal(coordinator.presentation("B")?.seaLevelCount, 0);
  assert.equal(coordinator.presentation("B")?.mountainPeakCount, 0);
});


test("physical-only qualification supplies feedback without losing cues", () => {
  const projection = projectTypeLeverage({
    ...document,
    implementation: {
      ...document.implementation,
      typeLeverageShards: [bodyShard, bodyToolsShard].map(value => ({
        ...value,
        disposition: "qualified",
        bodyCoverage: {
          ...bodyCoverage,
          bodiesExamined: 7,
          bodiesPhysicalOnly: 1,
        },
        diagnostics: ["Compiler-generated body retained as physical-only evidence; its Type uses are excluded from logical-owner relationships."],
      })),
    },
  });
  assert.deepEqual(typeLeverageFeedback(projection), {
    message: "Structural salience has qualified evidence: Implementation Type leverage has qualified evidence; surface leverage is independent; Implementation Type leverage: Compiler-generated body retained as physical-only evidence; its Type uses are excluded from logical-owner relationships.",
    retry: "type-leverage",
  });
  assert.deepEqual(projection.byType, projectTypeLeverage(document).byType);
  assert.equal(typeLeverageFeedback(projectTypeLeverage(document)), null);
});
