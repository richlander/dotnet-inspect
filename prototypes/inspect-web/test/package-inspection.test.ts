import assert from "node:assert/strict";
import test from "node:test";

import {
  createPackageInspectionCoordinator,
  workspaceDependencyKey,
  type PackageInspectionDependencies,
  type PackageInspectionState,
  type PackagePerformance,
} from "../src/package-inspection.ts";
import type { AppPackage } from "../src/package-acquisition.ts";
import type {
  BrowserPackageDependencies,
  BrowserPackageIntegrations,
  BrowserPackageOpportunities,
} from "../src/inspect-web-engine.d.ts";
import type { PackageMetadata } from "../src/metadata-viewer.ts";

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
    packageLens: "overview",
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
  };
}

function performanceResult(): PackagePerformance {
  return {
    members: [],
    nonPublicOpportunities: 0,
    totalOpportunities: 0,
  };
}

function metadataResult(): PackageMetadata {
  return { assemblies: [] };
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
    renderDependencyGraph: () => {},
    ...overrides,
  };
}

function deferred<T>() {
  let resolve!: (value: T) => void;
  const promise = new Promise<T>(accept => {
    resolve = accept;
  });
  return { promise, resolve };
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
      renderDependencyGraph: () => events.push("graph"),
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
  assert.deepEqual(events, ["render", "stats", "render", "graph"]);
});

test("workspace dependency loading records failures and ignores runtime packs", async () => {
  const good = packageModel({ id: "Example.Good" });
  const bad = packageModel({ id: "Example.Bad" });
  const runtime = packageModel({
    id: "Microsoft.NETCore.App",
    isRuntimePack: true,
    source: { kind: "platform" },
  });
  const events: string[] = [];
  const state = inspectionState({
    packages: [good, bad, runtime],
    atPackageRoot: true,
    packageLens: "dependencies",
  });
  const coordinator = createPackageInspectionCoordinator(
    inspectionDependencies(state, {
      queryDependencies: async packageItem => {
        events.push(`query:${packageItem.id}`);
        if (packageItem === bad) throw new Error("dependency feed unavailable");
        return dependencyResult(packageItem.id);
      },
      refreshPackageStats: () => events.push("stats"),
      render: () => events.push("render"),
    }));

  await coordinator.ensureWorkspaceDependencies();

  assert.deepEqual(events, [
    "query:Example.Good",
    "query:Example.Bad",
    "render",
    "stats",
  ]);
  assert.ok(state.workspaceDependencies[workspaceDependencyKey(good)]);
  assert.deepEqual(
    state.workspaceDependencies[workspaceDependencyKey(bad)].dependencyGroups,
    []);
  assert.equal(
    state.workspaceDependencyErrors[workspaceDependencyKey(bad)],
    "dependency feed unavailable");
  assert.equal(
    Object.hasOwn(state.workspaceDependencies, workspaceDependencyKey(runtime)),
    false);
  assert.equal(state.workspaceDependencyLoads.size, 0);
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
      queryPlatformOpportunities: async (framework, assemblyName, pack) => {
        calls.push(`opportunities:${framework}/${assemblyName}/${pack}`);
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
    "opportunities:net10.0/System.Text.Json.dll/pack:System.Text.Json",
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
