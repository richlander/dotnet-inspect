import assert from "node:assert/strict";
import test from "node:test";

import {
  createPackageInspectionCoordinator,
  resolvePackagePerformanceMember,
  workspaceDependencyKey,
  type PackageInspectionDependencies,
  type PackageInspectionState,
  type PackagePerformance,
} from "../src/package-inspection.ts";
import type {
  AppMemberSurface,
  AppPackage,
  AppTypeSurface,
} from "../src/package-acquisition.ts";
import type {
  BrowserPackageDependencies,
} from "../src/facades/inspect-web-package.d.ts";
import type {
  BrowserPackageIntegrations,
  BrowserPackageOpportunities,
  BrowserPerformanceMember,
} from "../src/facades/inspect-web-analysis.d.ts";
import type { PackageMetadata } from "../src/metadata-viewer.ts";

const selectedCompileLibrary = {
  status: "Selected" as const,
  targetFramework: "net10.0",
  message: null,
};

function packageModel(
  overrides: Partial<AppPackage> = {},
): AppPackage {
  return {
    id: "Example.Package",
    version: "1.2.3",
    frameworks: ["net10.0"],
    activeFramework: "net10.0",
    assembly: "Example.Package",
    assemblyId: "example-package",
    assemblyAsset: "lib/net10.0/Example.Package.dll",
    source: { kind: "nuget.org" },
    assemblies: [],
    types: [],
    accessibility: [],
    totalTypes: 0,
    totalMembers: 0,
    documents: [],
    icon: null,
    isRuntimePack: false,
    ...overrides,
  };
}

function inspectionState(
  overrides: Partial<PackageInspectionState> = {},
): PackageInspectionState {
  return {
    packages: [],
    atPackageRoot: false,
    atLibraryRoot: false,
    packageLens: "overview",
    libraryLens: "overview",
    packageDependencies: null,
    packageDependenciesLoading: false,
    packageDependenciesError: "",
    packageDependenciesKey: "",
    workspaceDependencies: {},
    workspaceDependencyErrors: {},
    workspaceDependencyLoads: new Set<string>(),
    packageIntegrations: null,
    packageIntegrationsLoading: false,
    packageIntegrationsError: "",
    packageIntegrationsKey: "",
    packageOpportunities: null,
    packageOpportunitiesLoading: false,
    packageOpportunitiesError: "",
    packageOpportunitiesKey: "",
    packagePerformance: null,
    packagePerformanceLoading: false,
    packagePerformanceError: "",
    packagePerformanceKey: "",
    packageMetadata: null,
    packageMetadataLoading: false,
    packageMetadataError: "",
    packageMetadataKey: "",
    ...overrides,
  };
}

function dependencyResult(
  packageId = "Example.Package",
  error: string | null = null,
): BrowserPackageDependencies {
  return {
    package: packageId,
    version: "1.2.3",
    activeFramework: "net10.0",
    assembly: `${packageId}.dll`,
    dependencyGroups: [{
      index: 0,
      framework: "net10.0",
      isActive: true,
      dependencies: [{ id: "Example.Dependency", versionRange: "[1.0.0,)" }],
    }],
    assemblyReferences: [],
    dependencyGroupError: error,
    assemblyReferenceError: null,
    compileLibrary: selectedCompileLibrary,
  };
}

function integrationsResult(): BrowserPackageIntegrations {
  return {
    package: "Example.Package",
    version: "1.2.3",
    framework: "net10.0",
    categories: [],
    totalSignals: 0,
    isComplete: true,
    inspectionError: null,
    compileLibrary: selectedCompileLibrary,
  };
}

function opportunitiesResult(): BrowserPackageOpportunities {
  return {
    package: "Example.Package",
    version: "1.2.3",
    activeFramework: "net10.0",
    categories: [],
    totalOpportunities: 0,
    isComplete: true,
    inspectionError: null,
    compileLibrary: selectedCompileLibrary,
  };
}

function performanceResult(): PackagePerformance {
  return {
    members: [],
    inspectionError: null,
    nonPublicOpportunities: 0,
    totalOpportunities: 0,
    compileLibrary: selectedCompileLibrary,
  };
}

function performanceMember(): AppMemberSurface {
  return {
    name: "Bounds",
    kind: "property",
    signature: "object Bounds",
    accessibility: "public",
    isStatic: false,
    isUnsafe: false,
    isVirtual: false,
    isAbstract: false,
    isOverride: false,
    isExtension: false,
    isObsolete: false,
    genericArity: 0,
    metadataToken: null,
    returnType: "object",
    parameters: [],
    documentationId: "P:Example.Outer.Inner.Bounds",
    summary: null,
    returns: null,
    exceptions: [],
    stableSelector: "Bounds~surface",
    anchorDigest: "surface",
    canonicalSignature: "P:Example.Outer.Inner.Bounds",
    graphSelectorKey: "property:Bounds",
    bodySelectors: [{
      token: 0x06000001,
      memberName: "get_Bounds",
      selectorKey: "get_Bounds",
    }],
  };
}

function performanceType(
  member: AppMemberSurface,
): AppTypeSurface {
  return {
    id: "Example.dll:Example.Outer+Inner",
    definitionId: "Example.Outer+Inner",
    queryId: "Example.Outer.Inner",
    metadataId: "Example.Outer+Inner",
    name: "Inner",
    displayName: "Inner",
    namespace: "Example",
    kind: "class",
    accessibility: "public",
    accessibilityId: "public",
    assembly: "Example.dll",
    assemblyId: "example",
    assemblyName: "Example",
    members: 1,
    signature: "public class Inner",
    api: [member],
    platformPack: null,
  };
}

test(
  "performance navigation uses stable surface identity across body tokens",
  () => {
    const member = performanceMember();
    const type = performanceType(member);
    const packageItem: AppPackage = packageModel({ types: [type] });
    const performance: BrowserPerformanceMember = {
      assembly: "Example.dll",
      typeId: "Example.Outer+Inner",
      memberName: "Bounds",
      stableSelector: "Bounds~surface",
      bodyTokens: [0x06001000],
      opportunityCount: 1,
      inLoopCount: 0,
      shapes: ["box-value-type"],
      confidence: "high",
    };

    assert.deepEqual(
      resolvePackagePerformanceMember(packageItem, performance),
      { type, member });
    assert.equal(
      resolvePackagePerformanceMember(
        packageItem,
        { ...performance, stableSelector: "Bounds~different" }),
      null);
  });

function metadataResult(): PackageMetadata {
  return { assemblies: [] };
}

function metadataFailureResult(): PackageMetadata {
  return {
    assemblies: [],
    inspectionError: "all metadata reads failed",
  };
}

function inspectionDependencies(
  state: PackageInspectionState,
  overrides: Partial<Omit<PackageInspectionDependencies, "state">> = {},
): PackageInspectionDependencies {
  return {
    state,
    queryDependencies: async packageItem => dependencyResult(packageItem.id),
    queryPackageIntegrations: async () => integrationsResult(),
    queryPlatformIntegrations: async () => integrationsResult(),
    queryPackageOpportunities: async () => opportunitiesResult(),
    queryPlatformOpportunities: async () => opportunitiesResult(),
    queryPackagePerformance: async () => performanceResult(),
    queryPlatformPerformance: async () => performanceResult(),
    queryPackageMetadata: async () => metadataResult(),
    queryPlatformMetadata: async () => metadataResult(),
    platformPackForAssembly: assemblyName => `pack:${assemblyName}`,
    describeError: error =>
      error instanceof Error ? error.message : String(error),
    refreshPackageStats: () => {},
    render: () => {},
    renderDependencyGraph: async () => {},
    ...overrides,
  };
}

function deferred<T>() {
  let resolve!: (value: T) => void;
  let reject!: (reason?: unknown) => void;
  const promise = new Promise<T>((accept, deny) => {
    resolve = accept;
    reject = deny;
  });
  return { promise, resolve, reject };
}

test("Library References does not acquire other workspace dependencies", async () => {
  const selected = packageModel();
  const other = packageModel({ id: "Other.Package" });
  const state = inspectionState({
    packages: [selected, other],
    atLibraryRoot: true,
    libraryLens: "references",
  });
  const calls: string[] = [];
  const coordinator = createPackageInspectionCoordinator(
    inspectionDependencies(state, {
      queryDependencies: async pkg => {
        calls.push(`${pkg.id}#${pkg.assemblyId}`);
        return dependencyResult(pkg.id);
      },
    }));
  await coordinator.loadDependencies(selected, "library:references");
  assert.deepEqual(calls, ["Example.Package#example-package"]);
});

type PackageInspectionCoordinator =
  ReturnType<typeof createPackageInspectionCoordinator>;

interface PackageLensFixture<T> {
  name: string;
  result: T;
  cachesFailure?: boolean;
  createCoordinator:
    (
      state: PackageInspectionState,
      query: () => Promise<T>,
      render?: () => void,
    ) =>
      PackageInspectionCoordinator;
  load: (coordinator: PackageInspectionCoordinator, signature: string) =>
    Promise<void>;
  readResult: (state: PackageInspectionState) => T | null;
  readLoading: (state: PackageInspectionState) => boolean;
  readError: (state: PackageInspectionState) => string;
  setKey: (state: PackageInspectionState, key: string) => void;
  setError: (state: PackageInspectionState, error: string) => void;
}

async function verifyPackageLensLifecycle<T>(
  fixture: PackageLensFixture<T>,
) {
  {
    const query = deferred<T>();
    const events: string[] = [];
    const state = inspectionState();
    fixture.setKey(state, "old");
    fixture.setError(state, "old failure");
    const coordinator = fixture.createCoordinator(
      state,
      async () => query.promise,
      () => events.push("render"));

    const load = fixture.load(coordinator, "current");

    assert.equal(
      fixture.readResult(state),
      null,
      `${fixture.name} cleared result`);
    assert.equal(
      fixture.readLoading(state),
      true,
      `${fixture.name} started loading`);
    assert.equal(
      fixture.readError(state),
      "",
      `${fixture.name} cleared error`);
    assert.deepEqual(events, ["render"], `${fixture.name} start render`);

    query.resolve(fixture.result);
    await load;

    assert.deepEqual(
      fixture.readResult(state),
      fixture.result,
      `${fixture.name} current result`);
    assert.equal(
      fixture.readLoading(state),
      false,
      `${fixture.name} current loading`);
    assert.equal(
      fixture.readError(state),
      "",
      `${fixture.name} current error`);
    assert.deepEqual(
      events,
      ["render", "render"],
      `${fixture.name} completion render`);
  }

  {
    const state = inspectionState();
    const coordinator = fixture.createCoordinator(state, async () => {
      throw new Error("current failure");
    });

    await fixture.load(coordinator, "current");

    assert.equal(
      fixture.readResult(state),
      null,
      `${fixture.name} failed result`);
    assert.equal(
      fixture.readLoading(state),
      false,
      `${fixture.name} failed loading`);
    assert.equal(
      fixture.readError(state),
      "current failure",
      `${fixture.name} current failure`);
  }

  {
    let queries = 0;
    const state = inspectionState();
    fixture.setKey(state, "cached");
    fixture.setError(state, "cached failure");
    const coordinator = fixture.createCoordinator(state, async () => {
      queries++;
      return fixture.result;
    });

    await fixture.load(coordinator, "cached");

    if (fixture.cachesFailure ?? true) {
      assert.equal(queries, 0, `${fixture.name} cached query`);
      assert.equal(
        fixture.readError(state),
        "cached failure",
        `${fixture.name} cached failure`);
    } else {
      assert.equal(queries, 1, `${fixture.name} retried query`);
      assert.deepEqual(
        fixture.readResult(state),
        fixture.result,
        `${fixture.name} retried result`);
      assert.equal(
        fixture.readError(state),
        "",
        `${fixture.name} cleared retried failure`);
    }
  }

  {
    const query = deferred<T>();
    const state = inspectionState();
    const coordinator = fixture.createCoordinator(
      state,
      async () => query.promise);

    const load = fixture.load(coordinator, "first");
    fixture.setKey(state, "second");
    fixture.setError(state, "newer failure");
    query.reject(new Error("stale failure"));
    await load;

    assert.equal(
      fixture.readError(state),
      "newer failure",
      `${fixture.name} stale failure`);
    assert.equal(
      fixture.readLoading(state),
      true,
      `${fixture.name} stale loading`);
  }

  for (const outcome of ["success", "failure"]) {
    for (const completionOrder of ["stale-first", "current-first"]) {
      const label = `${fixture.name}: ${outcome}, ${completionOrder}`;
      const stale = deferred<T>();
      const current = deferred<T>();
      const currentResult = structuredClone(fixture.result);
      let queries = 0;
      let renders = 0;
      const state = inspectionState({ packages: [packageModel()] });
      const coordinator = fixture.createCoordinator(
        state,
        async () => ++queries === 1 ? stale.promise : current.promise,
        () => renders++);

      const staleLoad = fixture.load(coordinator, "same-coordinate");
      state.packages = [];
      coordinator.invalidatePackageResults();
      state.packages = [packageModel()];
      const currentLoad = fixture.load(coordinator, "same-coordinate");
      assert.equal(queries, 2, label);

      if (completionOrder === "current-first") {
        current.resolve(currentResult);
        await currentLoad;
      }
      const rendersBeforeStaleCompletion = renders;
      const workspaceBeforeStaleCompletion = structuredClone(
        state.workspaceDependencies);
      if (outcome === "success") {
        stale.resolve(fixture.result);
      } else {
        stale.reject(new Error("retired failure"));
      }
      await staleLoad;

      assert.strictEqual(
        fixture.readResult(state),
        completionOrder === "current-first" ? currentResult : null,
        label);
      assert.equal(
        fixture.readLoading(state),
        completionOrder === "stale-first",
        label);
      assert.equal(fixture.readError(state), "", label);
      assert.equal(renders, rendersBeforeStaleCompletion, label);
      assert.deepEqual(
        state.workspaceDependencies,
        workspaceBeforeStaleCompletion,
        label);
      assert.deepEqual(state.workspaceDependencyErrors, {}, label);

      if (completionOrder === "stale-first") {
        current.resolve(currentResult);
        await currentLoad;
      }
      assert.strictEqual(fixture.readResult(state), currentResult, label);
      assert.equal(fixture.readLoading(state), false, label);
      assert.equal(fixture.readError(state), "", label);
    }
  }
}

test("workspace dependency keys normalize complete package coordinates", () => {
  assert.equal(
    workspaceDependencyKey({
      id: "Example.Package",
      version: "1.2.3-PREVIEW",
      activeFramework: "NET10.0",
    }),
    "example.package@1.2.3-preview@net10.0");
});

test("dependency results cache for a resident package after the foreground lens moves", async () => {
  const packageItem = packageModel();
  const request = deferred<BrowserPackageDependencies>();
  const events: string[] = [];
  const state = inspectionState({ packages: [packageItem] });
  const coordinator = createPackageInspectionCoordinator(
    inspectionDependencies(state, {
      queryDependencies: async () => request.promise,
      refreshPackageStats: () => events.push("stats"),
      render: () => events.push("render"),
      renderDependencyGraph: async () => {
        events.push("graph");
      },
    }));

  const load = coordinator.loadDependencies(packageItem, "first");
  assert.equal(state.packageDependenciesLoading, true);
  state.packageDependenciesKey = "second";
  request.resolve(dependencyResult(packageItem.id, "partial dependency data"));
  await load;

  assert.equal(state.packageDependencies, null);
  assert.equal(state.packageDependenciesLoading, true);
  const key = workspaceDependencyKey(packageItem);
  const workspace = state.workspaceDependencies[key];
  assert.ok(workspace);
  assert.equal(
    workspace.dependencyGroups?.[0]?.framework,
    "net10.0");
  assert.equal(
    state.workspaceDependencyErrors[key],
    "partial dependency data");
  assert.deepEqual(events, ["render", "stats", "render"]);
});

test("foreground dependency success refreshes cached groups and clears a prior error", async () => {
  const packageItem = packageModel();
  const key = workspaceDependencyKey(packageItem);
  const state = inspectionState({
    packages: [packageItem],
    workspaceDependencyErrors: { [key]: "stale failure" },
  });
  const coordinator = createPackageInspectionCoordinator(
    inspectionDependencies(state));

  await coordinator.loadDependencies(packageItem, "current");

  const workspace = state.workspaceDependencies[key];
  assert.ok(workspace);
  assert.equal(
    workspace.dependencyGroups?.[0]?.dependencies?.[0]?.id,
    "Example.Dependency");
  assert.equal(Object.hasOwn(state.workspaceDependencyErrors, key), false);
});

test("package lens loaders reuse cached results without querying or clearing them", async () => {
  const packageItem = packageModel();
  const dependencies = dependencyResult();
  const integrations = integrationsResult();
  const opportunities = opportunitiesResult();
  const performance = performanceResult();
  const metadata = metadataResult();
  const cases = [
    {
      name: "dependencies",
      cached: dependencies,
      state: inspectionState({
        packageDependenciesKey: "cached",
        packageDependencies: dependencies,
      }),
      read: (state: PackageInspectionState) => state.packageDependencies,
      load: (coordinator: ReturnType<typeof createPackageInspectionCoordinator>) =>
        coordinator.loadDependencies(packageItem, "cached"),
    },
    {
      name: "integrations",
      cached: integrations,
      state: inspectionState({
        packageIntegrationsKey: "cached",
        packageIntegrations: integrations,
      }),
      read: (state: PackageInspectionState) => state.packageIntegrations,
      load: (coordinator: ReturnType<typeof createPackageInspectionCoordinator>) =>
        coordinator.loadIntegrations(packageItem, "cached", null),
    },
    {
      name: "opportunities",
      cached: opportunities,
      state: inspectionState({
        packageOpportunitiesKey: "cached",
        packageOpportunities: opportunities,
      }),
      read: (state: PackageInspectionState) => state.packageOpportunities,
      load: (coordinator: ReturnType<typeof createPackageInspectionCoordinator>) =>
        coordinator.loadOpportunities(packageItem, "cached", null),
    },
    {
      name: "performance",
      cached: performance,
      state: inspectionState({
        packagePerformanceKey: "cached",
        packagePerformance: performance,
      }),
      read: (state: PackageInspectionState) => state.packagePerformance,
      load: (coordinator: ReturnType<typeof createPackageInspectionCoordinator>) =>
        coordinator.loadPerformance(packageItem, "cached", null),
    },
    {
      name: "metadata",
      cached: metadata,
      state: inspectionState({
        packageMetadataKey: "cached",
        packageMetadata: metadata,
      }),
      read: (state: PackageInspectionState) => state.packageMetadata,
      load: (coordinator: ReturnType<typeof createPackageInspectionCoordinator>) =>
        coordinator.loadMetadata(packageItem, "cached", null),
    },
  ];

  for (const fixture of cases) {
    let queries = 0;
    let renders = 0;
    const coordinator = createPackageInspectionCoordinator(
      inspectionDependencies(fixture.state, {
        queryDependencies: async () => {
          queries++;
          return dependencyResult();
        },
        queryPackageIntegrations: async () => {
          queries++;
          return integrationsResult();
        },
        queryPackageOpportunities: async () => {
          queries++;
          return opportunitiesResult();
        },
        queryPackagePerformance: async () => {
          queries++;
          return performanceResult();
        },
        queryPackageMetadata: async () => {
          queries++;
          return metadataResult();
        },
        render: () => renders++,
      }));

    await fixture.load(coordinator);

    assert.equal(queries, 0, fixture.name);
    assert.equal(renders, 1, fixture.name);
    assert.strictEqual(fixture.read(fixture.state), fixture.cached, fixture.name);
  }
});

test("package lens loaders reuse cached failures without querying", async () => {
  const packageItem = packageModel();
  let queries = 0;
  let renders = 0;
  const state = inspectionState({
    packagePerformanceKey: "cached",
    packagePerformanceError: "cached failure",
  });
  const coordinator = createPackageInspectionCoordinator(
    inspectionDependencies(state, {
      queryPackagePerformance: async () => {
        queries++;
        return performanceResult();
      },
      render: () => renders++,
    }));

  await coordinator.loadPerformance(packageItem, "cached", null);

  assert.equal(queries, 0);
  assert.equal(renders, 1);
  assert.equal(state.packagePerformanceError, "cached failure");
});

test("every package lens preserves its lifecycle and same-coordinate ownership across invalidation", async () => {
  const packageItem = packageModel();

  await verifyPackageLensLifecycle({
    name: "dependencies",
    result: dependencyResult(),
    createCoordinator: (state, query, render = () => {}) =>
      createPackageInspectionCoordinator(
        inspectionDependencies(state, {
          queryDependencies: async () => query(),
          render,
        })),
    load: (coordinator, signature) =>
      coordinator.loadDependencies(packageItem, signature),
    readResult: state => state.packageDependencies,
    readLoading: state => state.packageDependenciesLoading,
    readError: state => state.packageDependenciesError,
    setKey: (state, key) => { state.packageDependenciesKey = key; },
    setError: (state, error) => { state.packageDependenciesError = error; },
  });
  await verifyPackageLensLifecycle({
    name: "integrations",
    result: integrationsResult(),
    createCoordinator: (state, query, render = () => {}) =>
      createPackageInspectionCoordinator(
        inspectionDependencies(state, {
          queryPackageIntegrations: async () => query(),
          render,
        })),
    load: (coordinator, signature) =>
      coordinator.loadIntegrations(packageItem, signature, null),
    readResult: state => state.packageIntegrations,
    readLoading: state => state.packageIntegrationsLoading,
    readError: state => state.packageIntegrationsError,
    setKey: (state, key) => { state.packageIntegrationsKey = key; },
    setError: (state, error) => { state.packageIntegrationsError = error; },
  });
  await verifyPackageLensLifecycle({
    name: "opportunities",
    result: opportunitiesResult(),
    createCoordinator: (state, query, render = () => {}) =>
      createPackageInspectionCoordinator(
        inspectionDependencies(state, {
          queryPackageOpportunities: async () => query(),
          render,
        })),
    load: (coordinator, signature) =>
      coordinator.loadOpportunities(packageItem, signature, null),
    readResult: state => state.packageOpportunities,
    readLoading: state => state.packageOpportunitiesLoading,
    readError: state => state.packageOpportunitiesError,
    setKey: (state, key) => { state.packageOpportunitiesKey = key; },
    setError: (state, error) => { state.packageOpportunitiesError = error; },
  });
  await verifyPackageLensLifecycle({
    name: "performance",
    result: performanceResult(),
    createCoordinator: (state, query, render = () => {}) =>
      createPackageInspectionCoordinator(
        inspectionDependencies(state, {
          queryPackagePerformance: async () => query(),
          render,
        })),
    load: (coordinator, signature) =>
      coordinator.loadPerformance(packageItem, signature, null),
    readResult: state => state.packagePerformance,
    readLoading: state => state.packagePerformanceLoading,
    readError: state => state.packagePerformanceError,
    setKey: (state, key) => { state.packagePerformanceKey = key; },
    setError: (state, error) => { state.packagePerformanceError = error; },
  });
  await verifyPackageLensLifecycle({
    name: "metadata",
    result: metadataResult(),
    cachesFailure: false,
    createCoordinator: (state, query, render = () => {}) =>
      createPackageInspectionCoordinator(
        inspectionDependencies(state, {
          queryPackageMetadata: async () => query(),
          render,
        })),
    load: (coordinator, signature) =>
      coordinator.loadMetadata(packageItem, signature, null),
    readResult: state => state.packageMetadata,
    readLoading: state => state.packageMetadataLoading,
    readError: state => state.packageMetadataError,
    setKey: (state, key) => { state.packageMetadataKey = key; },
    setError: (state, error) => { state.packageMetadataError = error; },
  });
});

test("metadata requests suppress duplicates and preserve unique ownership", async () => {
  const packageItem = packageModel();
  const firstA = deferred<PackageMetadata>();
  const b = deferred<PackageMetadata>();
  const newestA = deferred<PackageMetadata>();
  const requests = [firstA, b, newestA];
  let queries = 0;
  const state = inspectionState();
  const coordinator = createPackageInspectionCoordinator(
    inspectionDependencies(state, {
      queryPackageMetadata: async () => requests[queries++]!.promise,
    }));

  const firstALoad = coordinator.loadMetadata(packageItem, "A", null);
  const duplicateALoad = coordinator.loadMetadata(packageItem, "A", null);
  assert.equal(queries, 1);
  await duplicateALoad;

  const bLoad = coordinator.loadMetadata(packageItem, "B", null);
  const newestALoad = coordinator.loadMetadata(packageItem, "A", null);
  assert.equal(queries, 3);

  const newest = metadataResult();
  newestA.resolve(newest);
  await newestALoad;
  firstA.reject(new Error("stale A failure"));
  await firstALoad;

  assert.strictEqual(state.packageMetadata, newest);
  assert.equal(state.packageMetadataError, "");
  assert.equal(state.packageMetadataLoading, false);

  b.resolve(metadataResult());
  await bLoad;
  assert.strictEqual(state.packageMetadata, newest);
});

test("all-failed metadata results remain visible and retryable", async () => {
  const packageItem = packageModel();
  let queries = 0;
  const state = inspectionState();
  const coordinator = createPackageInspectionCoordinator(
    inspectionDependencies(state, {
      queryPackageMetadata: async () => {
        queries++;
        return metadataFailureResult();
      },
    }));

  await coordinator.loadMetadata(packageItem, "metadata", null);

  assert.equal(state.packageMetadata, null);
  assert.equal(state.packageMetadataError, "all metadata reads failed");
  assert.equal(state.packageMetadataLoading, false);

  await coordinator.loadMetadata(packageItem, "metadata", null);

  assert.equal(queries, 2);
  assert.equal(state.packageMetadataError, "all metadata reads failed");
});

test("stale package lens rejection cannot overwrite newer state", async () => {
  const packageItem = packageModel();
  const request = deferred<PackagePerformance>();
  const state = inspectionState();
  const coordinator = createPackageInspectionCoordinator(
    inspectionDependencies(state, {
      queryPackagePerformance: async () => request.promise,
    }));

  const load = coordinator.loadPerformance(packageItem, "first", null);
  state.packagePerformanceKey = "second";
  state.packagePerformanceError = "newer failure";
  request.reject(new Error("stale failure"));
  await load;

  assert.equal(state.packagePerformanceError, "newer failure");
  assert.equal(state.packagePerformanceLoading, true);
});

test("invalidation clears package results, failures, keys, and loads without changing selection or workspace caches", () => {
  const remaining = packageModel();
  const key = workspaceDependencyKey(remaining);
  const workspaceDependencies = {
    [key]: {
      dependencyGroups: dependencyResult().dependencyGroups,
      dependencyGroupError: "retained partial data",
    },
  };
  const workspaceDependencyErrors = { [key]: "retained partial data" };
  const retainedState = {
    packages: [remaining],
    atPackageRoot: true,
    packageLens: "analysis" as const,
    workspaceDependencies,
    workspaceDependencyErrors,
  };
  const state = inspectionState({
    ...retainedState,
    packageDependencies: dependencyResult(),
    packageDependenciesLoading: true,
    packageDependenciesError: "dependency failure",
    packageDependenciesKey: "dependencies",
    packageIntegrations: integrationsResult(),
    packageIntegrationsLoading: true,
    packageIntegrationsError: "integration failure",
    packageIntegrationsKey: "integrations",
    packageOpportunities: opportunitiesResult(),
    packageOpportunitiesLoading: true,
    packageOpportunitiesError: "opportunity failure",
    packageOpportunitiesKey: "opportunities",
    packagePerformance: performanceResult(),
    packagePerformanceLoading: true,
    packagePerformanceError: "performance failure",
    packagePerformanceKey: "performance",
    packageMetadata: metadataResult(),
    packageMetadataLoading: true,
    packageMetadataError: "metadata failure",
    packageMetadataKey: "metadata",
    workspaceDependencyLoads: new Set([key]),
  });
  const coordinator = createPackageInspectionCoordinator(
    inspectionDependencies(state));

  coordinator.invalidatePackageResults();

  assert.deepEqual(state, inspectionState(retainedState));
  assert.strictEqual(state.packages, retainedState.packages);
  assert.strictEqual(state.workspaceDependencies, workspaceDependencies);
  assert.strictEqual(state.workspaceDependencyErrors, workspaceDependencyErrors);
});

test("invalidated package completions cannot publish, render, or start follow-up work", async () => {
  for (const outcome of ["success", "failure"]) {
    const removed = packageModel();
    const remaining = packageModel({ id: "Example.Remaining" });
    const dependencies = deferred<BrowserPackageDependencies>();
    const integrations = deferred<BrowserPackageIntegrations>();
    const opportunities = deferred<BrowserPackageOpportunities>();
    const performance = deferred<PackagePerformance>();
    const metadata = deferred<PackageMetadata>();
    const events: string[] = [];
    const state = inspectionState({ packages: [removed, remaining] });
    const coordinator = createPackageInspectionCoordinator(
      inspectionDependencies(state, {
        queryDependencies: async packageItem => {
          events.push(`query:${packageItem.id}`);
          return dependencies.promise;
        },
        queryPackageIntegrations: async () => integrations.promise,
        queryPackageOpportunities: async () => opportunities.promise,
        queryPackagePerformance: async () => performance.promise,
        queryPackageMetadata: async () => metadata.promise,
        render: () => events.push("render"),
        refreshPackageStats: () => events.push("stats"),
        renderDependencyGraph: async () => { events.push("graph"); },
      }));
    const loads = [
      coordinator.loadDependencies(removed, "dependencies"),
      coordinator.loadIntegrations(removed, "integrations", null),
      coordinator.loadOpportunities(removed, "opportunities", null),
      coordinator.loadPerformance(removed, "performance", null),
      coordinator.loadMetadata(removed, "metadata", null),
    ];

    state.packages = [remaining];
    events.length = 0;
    coordinator.invalidatePackageResults();
    assert.deepEqual(state, inspectionState({ packages: [remaining] }), outcome);

    if (outcome === "success") {
      dependencies.resolve(dependencyResult());
      integrations.resolve(integrationsResult());
      opportunities.resolve(opportunitiesResult());
      performance.resolve(performanceResult());
      metadata.resolve(metadataResult());
    } else {
      for (const request of [
        dependencies, integrations, opportunities, performance, metadata,
      ]) {
        request.reject(new Error("retired failure"));
      }
    }
    await Promise.all(loads);

    assert.deepEqual(state, inspectionState({ packages: [remaining] }), outcome);
    assert.deepEqual(events, [], outcome);
  }
});

test("invalidated workspace dependency loads cannot restore removed entries or continue their queue", async () => {
  for (const outcome of ["success", "failure"]) {
    const removed = packageModel();
    const queued = packageModel({ id: "Example.Queued" });
    const cached = packageModel({ id: "Example.Cached" });
    const key = workspaceDependencyKey(cached);
    const request = deferred<BrowserPackageDependencies>();
    const events: string[] = [];
    const state = inspectionState({
      packages: [removed, queued, cached],
      atPackageRoot: true,
      packageLens: "dependencies",
      workspaceDependencies: {
        [key]: { dependencyGroups: dependencyResult(cached.id).dependencyGroups },
      },
      workspaceDependencyErrors: { [key]: "retained warning" },
    });
    const coordinator = createPackageInspectionCoordinator(
      inspectionDependencies(state, {
        queryDependencies: async packageItem => {
          events.push(`query:${packageItem.id}`);
          return packageItem === removed
            ? request.promise
            : dependencyResult(packageItem.id);
        },
        render: () => events.push("render"),
        refreshPackageStats: () => events.push("stats"),
        renderDependencyGraph: async () => { events.push("graph"); },
      }));

    const load = coordinator.ensureWorkspaceDependencies();
    assert.deepEqual(events, [`query:${removed.id}`], outcome);
    state.packages = [queued, cached];
    events.length = 0;
    coordinator.invalidatePackageResults();
    assert.equal(state.workspaceDependencyLoads.size, 0, outcome);
    const invalidatedState = structuredClone(state);

    if (outcome === "success") {
      request.resolve(dependencyResult(removed.id, "retired warning"));
    } else {
      request.reject(new Error("retired failure"));
    }
    await load;

    assert.deepEqual(state, invalidatedState, outcome);
    assert.deepEqual(events, [], outcome);

    await coordinator.ensureWorkspaceDependencies();
    assert.deepEqual(events, [`query:${queued.id}`, "render", "stats"], outcome);
    assert.deepEqual(
      state.workspaceDependencies[key],
      invalidatedState.workspaceDependencies[key],
      outcome);
    assert.equal(state.workspaceDependencyErrors[key], "retained warning", outcome);
  }
});

test("invalidated workspace completions cannot overwrite reopened results or release a new load", async () => {
  for (const outcome of ["success", "failure"]) {
    for (const completionOrder of ["stale-first", "current-first"]) {
      const label = `${outcome}, ${completionOrder}`;
      const removed = packageModel();
      const key = workspaceDependencyKey(removed);
      const stale = deferred<BrowserPackageDependencies>();
      const current = deferred<BrowserPackageDependencies>();
      const currentResult = dependencyResult(removed.id, "current warning");
      let queries = 0;
      const events: string[] = [];
      const state = inspectionState({
        packages: [removed],
        atPackageRoot: true,
        packageLens: "dependencies",
      });
      const coordinator = createPackageInspectionCoordinator(
        inspectionDependencies(state, {
          queryDependencies: async () => ++queries === 1
            ? stale.promise
            : current.promise,
          render: () => events.push("render"),
          refreshPackageStats: () => events.push("stats"),
          renderDependencyGraph: async () => { events.push("graph"); },
        }));

      const staleLoad = coordinator.ensureWorkspaceDependencies();
      state.packages = [];
      coordinator.invalidatePackageResults();
      state.packages = [packageModel()];
      const currentLoad = coordinator.ensureWorkspaceDependencies();
      assert.equal(queries, 2, label);
      assert.deepEqual([...state.workspaceDependencyLoads], [key], label);
      if (completionOrder === "current-first") {
        current.resolve(currentResult);
        await currentLoad;
      }
      const currentState = structuredClone(state);
      events.length = 0;
      if (outcome === "success") {
        stale.resolve(dependencyResult(removed.id, "retired warning"));
      } else {
        stale.reject(new Error("retired failure"));
      }
      await staleLoad;

      assert.deepEqual(state, currentState, label);
      assert.deepEqual(events, [], label);
      if (completionOrder === "stale-first") {
        await coordinator.ensureWorkspaceDependencies();
        assert.equal(queries, 2, label);
        current.resolve(currentResult);
        await currentLoad;
      }
      assert.deepEqual(state.workspaceDependencies[key], {
        dependencyGroups: currentResult.dependencyGroups,
        dependencyGroupError: currentResult.dependencyGroupError,
      }, label);
      assert.equal(state.workspaceDependencyErrors[key], "current warning", label);
      assert.equal(state.workspaceDependencyLoads.size, 0, label);
    }
  }
});

test("workspace dependency loading records failures and ignores runtime packs", async () => {
  const good = packageModel({ id: "Example.Good" });
  const partial = packageModel({ id: "Example.Partial" });
  const bad = packageModel({ id: "Example.Bad" });
  const runtime = packageModel({
    id: "Microsoft.NETCore.App",
    isRuntimePack: true,
    source: { kind: "platform" },
  });
  const events: string[] = [];
  const goodKey = workspaceDependencyKey(good);
  const state = inspectionState({
    packages: [good, partial, bad, runtime],
    atPackageRoot: true,
    packageLens: "dependencies",
    workspaceDependencyErrors: { [goodKey]: "stale failure" },
  });
  const coordinator = createPackageInspectionCoordinator(
    inspectionDependencies(state, {
      queryDependencies: async packageItem => {
        events.push(`query:${packageItem.id}`);
        if (packageItem === bad) throw new Error("dependency feed unavailable");
        if (packageItem === partial) {
          return dependencyResult(
            packageItem.id,
            "no dependency group matches net10.0");
        }
        return dependencyResult(packageItem.id);
      },
      refreshPackageStats: () => events.push("stats"),
      render: () => events.push("render"),
    }));

  await coordinator.ensureWorkspaceDependencies();

  assert.deepEqual(events, [
    "query:Example.Good",
    "query:Example.Partial",
    "query:Example.Bad",
    "render",
    "stats",
  ]);
  const goodWorkspace = state.workspaceDependencies[goodKey];
  assert.ok(goodWorkspace);
  assert.equal(
    goodWorkspace.dependencyGroups?.[0]?.dependencies?.[0]?.id,
    "Example.Dependency");
  assert.equal(Object.hasOwn(state.workspaceDependencyErrors, goodKey), false);
  const partialKey = workspaceDependencyKey(partial);
  const partialWorkspace = state.workspaceDependencies[partialKey];
  assert.ok(partialWorkspace);
  assert.equal(
    partialWorkspace.dependencyGroupError,
    "no dependency group matches net10.0");
  assert.equal(
    state.workspaceDependencyErrors[partialKey],
    "no dependency group matches net10.0");
  const failedWorkspace = state.workspaceDependencies[workspaceDependencyKey(bad)];
  assert.ok(failedWorkspace);
  assert.deepEqual(failedWorkspace.dependencyGroups, []);
  assert.equal(
    state.workspaceDependencyErrors[workspaceDependencyKey(bad)],
    "dependency feed unavailable");
  assert.equal(
    Object.hasOwn(state.workspaceDependencies, workspaceDependencyKey(runtime)),
    false);
  assert.equal(state.workspaceDependencyLoads.size, 0);
});

test("workspace dependency loading skips keys already in flight", async () => {
  const packageItem = packageModel();
  const key = workspaceDependencyKey(packageItem);
  let queries = 0;
  let graphRenders = 0;
  const state = inspectionState({
    packages: [packageItem],
    workspaceDependencyLoads: new Set([key]),
  });
  const coordinator = createPackageInspectionCoordinator(
    inspectionDependencies(state, {
      queryDependencies: async () => {
        queries++;
        return dependencyResult();
      },
      renderDependencyGraph: async () => {
        graphRenders++;
      },
    }));

  await coordinator.ensureWorkspaceDependencies();

  assert.equal(queries, 0);
  assert.equal(graphRenders, 1);
  assert.deepEqual([...state.workspaceDependencyLoads], [key]);
});

test("workspace dependency loading is sequential and only renders the active lens", async () => {
  const first = packageModel({ id: "Example.First" });
  const second = packageModel({ id: "Example.Second" });
  const firstRequest = deferred<BrowserPackageDependencies>();
  const queries: string[] = [];
  let renders = 0;
  let stats = 0;
  const state = inspectionState({
    packages: [first, second],
    atPackageRoot: false,
    packageLens: "dependencies",
  });
  const coordinator = createPackageInspectionCoordinator(
    inspectionDependencies(state, {
      queryDependencies: async packageItem => {
        queries.push(packageItem.id);
        return packageItem.id === first.id
          ? firstRequest.promise
          : dependencyResult(packageItem.id);
      },
      render: () => renders++,
      refreshPackageStats: () => stats++,
    }));

  const load = coordinator.ensureWorkspaceDependencies();
  assert.deepEqual(queries, [first.id]);
  firstRequest.resolve(dependencyResult(first.id));
  await load;

  assert.deepEqual(queries, [first.id, second.id]);
  assert.equal(renders, 0);
  assert.equal(stats, 1);
});

test("workspace dependency loading does not render an active library lens", async () => {
  const packageItem = packageModel();
  let renders = 0;
  const state = inspectionState({
    packages: [packageItem],
    atPackageRoot: false,
    atLibraryRoot: true,
    libraryLens: "metadata",
  });
  const coordinator = createPackageInspectionCoordinator(
    inspectionDependencies(state, {
      render: () => renders++,
    }));

  await coordinator.ensureWorkspaceDependencies();

  assert.equal(renders, 0);
});

test("removed packages cannot publish an in-flight workspace dependency result", async () => {
  const packageItem = packageModel();
  const request = deferred<BrowserPackageDependencies>();
  const state = inspectionState({ packages: [packageItem] });
  const coordinator = createPackageInspectionCoordinator(
    inspectionDependencies(state, {
      queryDependencies: async () => request.promise,
    }));

  const load = coordinator.ensureWorkspaceDependencies();
  state.packages = [];
  request.resolve(dependencyResult());
  await load;

  assert.equal(
    Object.hasOwn(state.workspaceDependencies, workspaceDependencyKey(packageItem)),
    false);
  assert.equal(state.workspaceDependencyLoads.size, 0);
});

test("coordinate replacement cannot publish an in-flight workspace dependency result", async () => {
  const packageItem = packageModel();
  const request = deferred<BrowserPackageDependencies>();
  const state = inspectionState({ packages: [packageItem] });
  const coordinator = createPackageInspectionCoordinator(
    inspectionDependencies(state, {
      queryDependencies: async () => request.promise,
    }));

  const load = coordinator.ensureWorkspaceDependencies();
  state.packages = [packageModel({ version: "2.0.0" })];
  request.resolve(dependencyResult());
  await load;

  assert.equal(
    Object.hasOwn(state.workspaceDependencies, workspaceDependencyKey(packageItem)),
    false);
  assert.equal(state.workspaceDependencyLoads.size, 0);
});

test("packages removed before their workspace turn are not queried", async () => {
  const first = packageModel({ id: "Example.First" });
  const removed = packageModel({ id: "Example.Removed" });
  const firstRequest = deferred<BrowserPackageDependencies>();
  const queries: string[] = [];
  const state = inspectionState({ packages: [first, removed] });
  const coordinator = createPackageInspectionCoordinator(
    inspectionDependencies(state, {
      queryDependencies: async packageItem => {
        queries.push(packageItem.id);
        return packageItem === first
          ? firstRequest.promise
          : dependencyResult(packageItem.id);
      },
    }));

  const load = coordinator.ensureWorkspaceDependencies();
  state.packages = [first];
  firstRequest.resolve(dependencyResult(first.id));
  await load;

  assert.deepEqual(queries, [first.id]);
  assert.equal(
    Object.hasOwn(state.workspaceDependencies, workspaceDependencyKey(removed)),
    false);
});

test("removed packages cannot publish rejected workspace dependency requests", async () => {
  const packageItem = packageModel();
  const request = deferred<BrowserPackageDependencies>();
  const state = inspectionState({ packages: [packageItem] });
  const coordinator = createPackageInspectionCoordinator(
    inspectionDependencies(state, {
      queryDependencies: async () => request.promise,
    }));

  const load = coordinator.ensureWorkspaceDependencies();
  state.packages = [];
  request.reject(new Error("stale failure"));
  await load;

  const key = workspaceDependencyKey(packageItem);
  assert.equal(Object.hasOwn(state.workspaceDependencies, key), false);
  assert.equal(Object.hasOwn(state.workspaceDependencyErrors, key), false);
  assert.equal(state.workspaceDependencyLoads.size, 0);
});

test("removed packages cannot publish dependencies loaded by the foreground lens", async () => {
  const packageItem = packageModel();
  const request = deferred<BrowserPackageDependencies>();
  const state = inspectionState({ packages: [packageItem] });
  const coordinator = createPackageInspectionCoordinator(
    inspectionDependencies(state, {
      queryDependencies: async () => request.promise,
    }));

  const load = coordinator.loadDependencies(packageItem, "dependencies");
  state.packages = [];
  request.resolve(dependencyResult());
  await load;

  assert.equal(
    Object.hasOwn(state.workspaceDependencies, workspaceDependencyKey(packageItem)),
    false);
});

test("scoped package lenses route platform coordinates and suppress stale results", async () => {
  const packageItem = packageModel();
  const runtime = packageModel({
    id: "Microsoft.NETCore.App",
    isRuntimePack: true,
    source: { kind: "platform" },
  });
  const metadata = deferred<PackageMetadata>();
  const calls: string[] = [];
  const state = inspectionState({ packages: [packageItem, runtime] });
  const coordinator = createPackageInspectionCoordinator(
    inspectionDependencies(state, {
      queryPackageIntegrations: async model => {
        calls.push(`integrations:${model.id}`);
        return integrationsResult();
      },
      queryPlatformOpportunities: async (
        framework,
        platformVersion,
        assemblyName,
        pack,
      ) => {
        calls.push(
          `opportunities:${framework}/${platformVersion}/${assemblyName}/${pack}`);
        return opportunitiesResult();
      },
      queryPackagePerformance: async model => {
        calls.push(`performance:${model.id}`);
        throw new Error("analysis unavailable");
      },
      queryPackageMetadata: async model => {
        calls.push(`metadata:${model.id}`);
        return metadata.promise;
      },
    }));

  await coordinator.loadIntegrations(packageItem, "integrations", null);
  await coordinator.loadOpportunities(
    runtime,
    "opportunities",
    "System.Text.Json");
  await coordinator.loadPerformance(packageItem, "performance", null);
  const metadataLoad =
    coordinator.loadMetadata(packageItem, "metadata-first", null);
  state.packageMetadataKey = "metadata-second";
  metadata.resolve(metadataResult());
  await metadataLoad;

  assert.deepEqual(calls, [
    "integrations:Example.Package",
    "opportunities:net10.0/1.2.3/System.Text.Json.dll/pack:System.Text.Json",
    "performance:Example.Package",
    "metadata:Example.Package",
  ]);
  assert.ok(state.packageIntegrations);
  assert.ok(state.packageOpportunities);
  assert.equal(state.packagePerformance, null);
  assert.equal(state.packagePerformanceError, "analysis unavailable");
  assert.equal(state.packagePerformanceLoading, false);
  assert.equal(state.packageMetadata, null);
  assert.equal(state.packageMetadataLoading, true);
});

test("all scoped runtime package lenses route exact platform coordinates", async () => {
  const runtime = packageModel({
    id: "Microsoft.NETCore.App",
    isRuntimePack: true,
    source: { kind: "platform" },
  });
  const calls: string[] = [];
  const state = inspectionState({ packages: [runtime] });
  const coordinator = createPackageInspectionCoordinator(
    inspectionDependencies(state, {
      queryPlatformIntegrations: async (
        framework,
        platformVersion,
        assemblyName,
        pack,
      ) => {
        calls.push(
          `integrations:${framework}/${platformVersion}/${assemblyName}/${pack}`);
        return integrationsResult();
      },
      queryPlatformOpportunities: async (
        framework,
        platformVersion,
        assemblyName,
        pack,
      ) => {
        calls.push(
          `opportunities:${framework}/${platformVersion}/${assemblyName}/${pack}`);
        return opportunitiesResult();
      },
      queryPlatformPerformance: async (
        framework,
        platformVersion,
        assemblyName,
        pack,
      ) => {
        calls.push(
          `performance:${framework}/${platformVersion}/${assemblyName}/${pack}`);
        return performanceResult();
      },
      queryPlatformMetadata: async (
        framework,
        platformVersion,
        assemblyName,
        pack,
      ) => {
        calls.push(
          `metadata:${framework}/${platformVersion}/${assemblyName}/${pack}`);
        return metadataResult();
      },
    }));

  await coordinator.loadIntegrations(runtime, "integrations", "System.Text.Json");
  await coordinator.loadOpportunities(runtime, "opportunities", "System.Text.Json");
  await coordinator.loadPerformance(runtime, "performance", "System.Text.Json");
  await coordinator.loadMetadata(runtime, "metadata", "System.Text.Json");

  assert.deepEqual(calls, [
    "integrations:net10.0/1.2.3/System.Text.Json.dll/pack:System.Text.Json",
    "opportunities:net10.0/1.2.3/System.Text.Json.dll/pack:System.Text.Json",
    "performance:net10.0/1.2.3/System.Text.Json.dll/pack:System.Text.Json",
    "metadata:net10.0/1.2.3/System.Text.Json.dll/pack:System.Text.Json",
  ]);
});

test("package lens results clear before a different request completes", async () => {
  const packageItem = packageModel();

  {
    const query = deferred<BrowserPackageDependencies>();
    const state = inspectionState({
      packageDependenciesKey: "old",
      packageDependencies: dependencyResult(),
    });
    const coordinator = createPackageInspectionCoordinator(
      inspectionDependencies(state, {
        queryDependencies: async () => query.promise,
      }));
    const load = coordinator.loadDependencies(packageItem, "new");
    assert.equal(state.packageDependencies, null);
    assert.equal(state.packageDependenciesLoading, true);
    query.resolve(dependencyResult());
    await load;
  }

  {
    const query = deferred<BrowserPackageIntegrations>();
    const state = inspectionState({
      packageIntegrationsKey: "old",
      packageIntegrations: integrationsResult(),
    });
    const coordinator = createPackageInspectionCoordinator(
      inspectionDependencies(state, {
        queryPackageIntegrations: async () => query.promise,
      }));
    const load = coordinator.loadIntegrations(packageItem, "new", null);
    assert.equal(state.packageIntegrations, null);
    assert.equal(state.packageIntegrationsLoading, true);
    query.resolve(integrationsResult());
    await load;
  }

  {
    const query = deferred<BrowserPackageOpportunities>();
    const state = inspectionState({
      packageOpportunitiesKey: "old",
      packageOpportunities: opportunitiesResult(),
    });
    const coordinator = createPackageInspectionCoordinator(
      inspectionDependencies(state, {
        queryPackageOpportunities: async () => query.promise,
      }));
    const load = coordinator.loadOpportunities(packageItem, "new", null);
    assert.equal(state.packageOpportunities, null);
    assert.equal(state.packageOpportunitiesLoading, true);
    query.resolve(opportunitiesResult());
    await load;
  }

  {
    const query = deferred<PackagePerformance>();
    const state = inspectionState({
      packagePerformanceKey: "old",
      packagePerformance: performanceResult(),
    });
    const coordinator = createPackageInspectionCoordinator(
      inspectionDependencies(state, {
        queryPackagePerformance: async () => query.promise,
      }));
    const load = coordinator.loadPerformance(packageItem, "new", null);
    assert.equal(state.packagePerformance, null);
    assert.equal(state.packagePerformanceLoading, true);
    query.resolve(performanceResult());
    await load;
  }

  {
    const query = deferred<PackageMetadata>();
    const state = inspectionState({
      packageMetadataKey: "old",
      packageMetadata: metadataResult(),
    });
    const coordinator = createPackageInspectionCoordinator(
      inspectionDependencies(state, {
        queryPackageMetadata: async () => query.promise,
      }));
    const load = coordinator.loadMetadata(packageItem, "new", null);
    assert.equal(state.packageMetadata, null);
    assert.equal(state.packageMetadataLoading, true);
    query.resolve(metadataResult());
    await load;
  }
});

test("package lens results require their current request key", async () => {
  const packageItem = packageModel();

  {
    const query = deferred<BrowserPackageIntegrations>();
    const state = inspectionState();
    const coordinator = createPackageInspectionCoordinator(
      inspectionDependencies(state, {
        queryPackageIntegrations: async () => query.promise,
      }));
    const load = coordinator.loadIntegrations(packageItem, "first", null);
    state.packageIntegrationsKey = "second";
    query.resolve(integrationsResult());
    await load;
    assert.equal(state.packageIntegrations, null);
    assert.equal(state.packageIntegrationsLoading, true);
  }

  {
    const query = deferred<BrowserPackageOpportunities>();
    const state = inspectionState();
    const coordinator = createPackageInspectionCoordinator(
      inspectionDependencies(state, {
        queryPackageOpportunities: async () => query.promise,
      }));
    const load = coordinator.loadOpportunities(packageItem, "first", null);
    state.packageOpportunitiesKey = "second";
    query.resolve(opportunitiesResult());
    await load;
    assert.equal(state.packageOpportunities, null);
    assert.equal(state.packageOpportunitiesLoading, true);
  }

  {
    const query = deferred<PackagePerformance>();
    const state = inspectionState();
    const coordinator = createPackageInspectionCoordinator(
      inspectionDependencies(state, {
        queryPackagePerformance: async () => query.promise,
      }));
    const load = coordinator.loadPerformance(packageItem, "first", null);
    state.packagePerformanceKey = "second";
    query.resolve(performanceResult());
    await load;
    assert.equal(state.packagePerformance, null);
    assert.equal(state.packagePerformanceLoading, true);
  }
});

test("runtime package lenses wait for an explicit library scope", async () => {
  const runtime = packageModel({
    id: "Microsoft.NETCore.App",
    isRuntimePack: true,
    source: { kind: "platform" },
  });
  let queries = 0;
  let renders = 0;
  const state = inspectionState({ packages: [runtime] });
  const dependencies = inspectionDependencies(state, {
    queryPlatformIntegrations: async () => {
      queries++;
      return integrationsResult();
    },
    queryPlatformOpportunities: async () => {
      queries++;
      return opportunitiesResult();
    },
    queryPlatformPerformance: async () => {
      queries++;
      return performanceResult();
    },
    queryPlatformMetadata: async () => {
      queries++;
      return metadataResult();
    },
    render: () => renders++,
  });
  const coordinator = createPackageInspectionCoordinator(dependencies);

  await coordinator.loadIntegrations(runtime, "integrations", null);
  await coordinator.loadOpportunities(runtime, "opportunities", null);
  await coordinator.loadPerformance(runtime, "performance", null);
  await coordinator.loadMetadata(runtime, "metadata", null);

  assert.equal(queries, 0);
  assert.equal(renders, 0);
  assert.equal(state.packageIntegrationsKey, "");
  assert.equal(state.packageOpportunitiesKey, "");
  assert.equal(state.packagePerformanceKey, "");
  assert.equal(state.packageMetadataKey, "");
});
