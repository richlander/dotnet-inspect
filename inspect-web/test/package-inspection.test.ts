import assert from "node:assert/strict";
import test from "node:test";

import {
  createPackageInspectionCoordinator,
  projectPackageDependencyInspection,
  projectPackagePerformanceType,
  resolvePackagePerformanceMember,
  resolvePackagePerformanceType,
  workspaceDependencyKey,
  type PackageDependenciesState,
  type PackageInspectionDependencies,
  type PackageInspectionState,
  type LibraryReferenceInspectionEnvelope,
  type PackageIntegrationsState,
  type PackageMetadataState,
  type PackageOpportunitiesState,
  type PackagePerformance,
  type PackageResourceTriage,
  type PackageUnsafeMembers,
  type PackageVulnerabilityState,
} from "../src/package-inspection.ts";
import {
  type AppMemberSurface,
  type AppPackage,
  type AppTypeSurface,
} from "../src/package-acquisition.ts";
import type {
  AssemblyReferenceRow,
  BrowserPackageDependencyInspection,
  BrowserPackageVulnerabilityResult,
} from "../src/facades/inspect-web-package.d.ts";
import type {
  BrowserPackageIntegrations,
  BrowserPackageOpportunities,
  BrowserPerformanceMember,
  BrowserUnsafeMember,
} from "../src/facades/inspect-web-analysis.d.ts";
import type { PackageMetadata } from "../src/metadata-viewer.ts";
import {
  dateTimeOffsetString,
} from "./date-time-offset-string-fixture.ts";
import { packageInertStringFixture } from "./inert-string-fixture.ts";

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
    producerLabel: "NuGet.org",
    assemblies: [],
    types: [],
    typeKinds: [],
    typeTraits: [],
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
    packageMetadataRoot: "cli",
    packageDependencies: { status: "idle" },
    libraryReferences: { status: "idle" },
    packageVulnerabilities: { status: "idle" },
    workspaceDependencies: {},
    workspaceDependencyErrors: {},
    workspaceDependencyLoads: new Set<string>(),
    packageIntegrations: { status: "idle" },
    packageOpportunities: { status: "idle" },
    packagePerformance: null,
    packagePerformanceLoading: false,
    packagePerformanceError: "",
    packagePerformanceKey: "",
    packageResourceTriage: null,
    packageResourceTriageLoading: false,
    packageResourceTriageError: "",
    packageResourceTriageKey: "",
    packageUnsafeMembers: null,
    packageUnsafeMembersLoading: false,
    packageUnsafeMembersError: "",
    packageUnsafeMembersKey: "",
    packageLibraryMetrics: null,
    packageLibraryMetricsLoading: false,
    packageLibraryMetricsError: "",
    packageLibraryMetricsKey: "",
    packageLibraryMetricsRelationshipState: null,
    packageLibraryDependencyStructure: null,
    packageLibraryDependencyStructureLoading: false,
    packageLibraryDependencyStructureError: "",
    packageLibraryDependencyStructureKey: "",
    packageMetadata: { status: "idle" },
    ...overrides,
  };
}

function dependencyResult(
  packageId = "Example.Package",
  error: string | null = null,
  groupFramework = "net10.0",
): BrowserPackageDependencyInspection {
  const groups = [{
    targetFramework: groupFramework,
    isImplicitManifestGroup: false,
    dependencies: [{
      id: "Example.Dependency",
      versionRange: "[1.0.0,)",
    }],
  }];
  const selectedGroup = error ? null : groups[0] ?? null;
  return {
    inspection: {
      content: {
        kind: "available",
        manifest: {
          coordinate: { packageId, version: "1.2.3" },
          manifestVersion: "1.0",
          description: null,
          authors: null,
          repository: null,
          repositoryType: null,
          repositoryCommit: null,
          license: null,
          licenseUrl: null,
          packageTypes: [],
          isToolPackage: false,
          readmeFile: null,
          dependencyGroups: groups,
          declaredPackageId: packageId,
          declaredPackageVersion: "1.2.3",
          dependencyDeclarations: {
            outcome: "available",
            value: { groups },
          },
          dependencyIdentifierObservations: [],
          licenseDeclaration: null,
          iconFile: null,
          iconUrl: null,
          identityProvenance: 0,
          frameworkReferences: {
            outcome: "available",
            value: { groups: [] },
          },
        },
        dependencies: {
          groups,
          requestedTargetFramework: "net10.0",
          selectedTargetFramework: selectedGroup?.targetFramework ?? null,
          selectedGroupIndex: selectedGroup ? 0 : null,
          selectionStatus: selectedGroup ? 0 : 2,
          selectedGroup,
        },
      },
      share: {
        kind: "nonProjectable",
        fullUrl: null,
        packet: null,
        path: "package-dependencies/share",
        reason: packageInertStringFixture(
          "Package dependency inspection is not projectable."),
      },
      diagnostics: [],
    },
    platformReferences: [],
    defaultPlatformReference: null,
    platformReferenceError: null,
  };
}

function dependencyProjection(
  result: BrowserPackageDependencyInspection = dependencyResult(),
) {
  return projectPackageDependencyInspection(result);
}

function libraryReferenceResult(
  references: readonly AssemblyReferenceRow[] = [],
): LibraryReferenceInspectionEnvelope {
  return {
    content: { outcome: "available", references },
    share: {
      kind: "nonProjectable",
      fullUrl: null,
      packet: null,
      path: "",
      reason: packageInertStringFixture(
        "Library reference inspection is not projectable."),
    },
    diagnostics: [],
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
    inspection: null,
  };
}

test("package dependency state rejects contradictory shapes at compile time", () => {
  const result = dependencyResult();
  const loadingWithResult: PackageDependenciesState = {
    status: "loading",
    request: { key: "current" },
    // @ts-expect-error A loading request cannot also carry dependency evidence.
    result,
  };
  const readyWithError: PackageDependenciesState = {
    status: "ready",
    key: "current",
    result,
    // @ts-expect-error Ready dependency evidence cannot also carry query failure.
    error: "failure",
  };
  // @ts-expect-error A failed dependency request always carries its diagnostic.
  const failedWithoutError: PackageDependenciesState = {
    status: "failed",
    key: "current",
  };
  void [loadingWithResult, readyWithError, failedWithoutError];
});

test("dependency projection keeps source groups separate from logical selection", () => {
  const result = dependencyResult();
  if (result.inspection.content?.kind !== "available") {
    throw new Error("Expected available dependency inspection.");
  }
  const groups = [
    {
      targetFramework: "any",
      isImplicitManifestGroup: true,
      dependencies: [{ id: "A", versionRange: "1" }],
    },
    {
      targetFramework: "net10.0",
      isImplicitManifestGroup: false,
      dependencies: [{ id: "B", versionRange: "1" }],
    },
    {
      targetFramework: "any",
      isImplicitManifestGroup: true,
      dependencies: [{ id: "C", versionRange: "1" }],
    },
  ];
  const separated = {
    ...result,
    inspection: {
      ...result.inspection,
      content: {
        ...result.inspection.content,
        manifest: {
          ...result.inspection.content.manifest,
          dependencyGroups: groups,
        },
        dependencies: {
          groups,
          requestedTargetFramework: "net10.0",
          selectedTargetFramework: "any",
          selectedGroupIndex: 0,
          selectionStatus: 0,
          selectedGroup: {
            targetFramework: "any",
            isImplicitManifestGroup: true,
            dependencies: [
              groups[0]!.dependencies[0]!,
              groups[2]!.dependencies[0]!,
            ],
          },
        },
      },
    },
  };

  const projection = dependencyProjection(separated);

  assert.deepEqual(
    projection.dependencyGroups.map(group =>
      group.dependencies.map(dependency => dependency.id)),
    [["A"], ["B"], ["C"]]);
  assert.deepEqual(
    projection.selectedDependencyGroup?.dependencies.map(
      dependency => dependency.id),
    ["A", "C"]);
});

test("package integrations state rejects contradictory shapes at compile time", () => {
  const result = integrationsResult();
  const loadingWithResult: PackageIntegrationsState = {
    status: "loading",
    request: { key: "current" },
    // @ts-expect-error A loading request cannot also carry completed evidence.
    result,
  };
  const readyWithError: PackageIntegrationsState = {
    status: "ready",
    key: "current",
    result,
    // @ts-expect-error Ready evidence cannot also carry a failure diagnostic.
    error: "failure",
  };
  // @ts-expect-error A failed request always carries its visible diagnostic.
  const failedWithoutError: PackageIntegrationsState = {
    status: "failed",
    key: "current",
  };
  void [loadingWithResult, readyWithError, failedWithoutError];
});

function vulnerabilityResult(): BrowserPackageVulnerabilityResult {
  return {
    package: "example.package",
    version: "1.2.3",
    availability: "Complete",
    advisories: [],
    failures: [],
    advisoryProducer: "https://api.github.com/advisories",
    observedAt: dateTimeOffsetString("2026-10-05T00:00:00Z"),
  };
}

test("package vulnerability state rejects contradictory shapes at compile time", () => {
  const result = vulnerabilityResult();
  const loadingWithResult: PackageVulnerabilityState = {
    status: "loading",
    request: { key: "current" },
    // @ts-expect-error A loading request cannot also carry completed evidence.
    result,
  };
  const readyWithError: PackageVulnerabilityState = {
    status: "ready",
    key: "current",
    result,
    // @ts-expect-error Ready evidence cannot also carry a failure diagnostic.
    error: "failure",
  };
  // @ts-expect-error A failed request always carries its visible diagnostic.
  const failedWithoutError: PackageVulnerabilityState = {
    status: "failed",
    key: "current",
  };
  void [loadingWithResult, readyWithError, failedWithoutError];
});

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
    inspection: null,
  };
}

test("package opportunities state rejects contradictory shapes at compile time", () => {
  const result = opportunitiesResult();
  const loadingWithResult: PackageOpportunitiesState = {
    status: "loading",
    request: { key: "current" },
    // @ts-expect-error A loading request cannot also carry completed evidence.
    result,
  };
  const readyWithError: PackageOpportunitiesState = {
    status: "ready",
    key: "current",
    result,
    // @ts-expect-error Ready evidence cannot also carry a failure diagnostic.
    error: "failure",
  };
  // @ts-expect-error A failed request always carries its visible diagnostic.
  const failedWithoutError: PackageOpportunitiesState = {
    status: "failed",
    key: "current",
  };
  void [loadingWithResult, readyWithError, failedWithoutError];
});

function performanceResult(): PackagePerformance {
  return {
    members: [],
    syncCallsInAsync: [],
    syncCallsInAsyncCount: 0,
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
    declarationMetadataToken: 0x17000001,
    returnType: "object",
    parameters: [],
    documentationId: "P:Example.Outer.Inner.Bounds",
    summary: null,
    returns: null,
    exceptions: [],
    stableSelector: "Bounds~surface",
    anchorDigest: "surface",
    canonicalSignature: "P:Example.Outer.Inner.Bounds",
    anchorTypeFullName: "Example.Outer.Inner",
    declaringTypeDefinitionId: null,
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
    kindFacetId: "api.type-kind.class",
    traitFacetIds: ["api.type-trait.object"],
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

function performanceDeclaringType(
  type: AppTypeSurface,
): BrowserPerformanceMember["declaringType"] {
  const { api: _, ...declaringType } = type;
  return declaringType;
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
      bodyTargets: [],
      opportunityCount: 1,
      inLoopCount: 0,
      shapes: ["box-value-type"],
      confidence: "high",
      declaringType: performanceDeclaringType(type),
    };

    assert.deepEqual(
      resolvePackagePerformanceMember(packageItem, performance),
      { type, member });
    assert.equal(
      resolvePackagePerformanceMember(
        packageItem,
        { ...performance, stableSelector: "Bounds~different" }),
      null);
    assert.equal(
      resolvePackagePerformanceType(
        packageItem,
        {
          assembly: performance.assembly,
          typeId: performance.typeId,
        }),
      type);
  });

test(
  "performance navigation projects an implementation-only Type without mutating the package",
  () => {
    const member = performanceMember();
    const type = performanceType(member);
    const packageItem: AppPackage = packageModel({ types: [] });
    const performance: BrowserPerformanceMember = {
      assembly: type.assembly,
      typeId: type.definitionId,
      memberName: member.name,
      stableSelector: member.stableSelector,
      bodyTokens: [0x06001000],
      bodyTargets: [],
      opportunityCount: 1,
      inLoopCount: 0,
      shapes: ["box-value-type"],
      confidence: "high",
      declaringType: performanceDeclaringType(type),
    };

    assert.deepEqual(
      projectPackagePerformanceType(performance),
      { ...type, api: [] });
    assert.deepEqual(packageItem.types, []);
  });

function unsafeRow(type: AppTypeSurface, stableSelector: string): BrowserUnsafeMember {
  return {
    member: `${type.definitionId}.Bounds()`,
    metadataToken: 0x06001000,
    exposure: "Non-public",
    explicitContract: false,
    safeBoundary: "safe boundary",
    evidence: "pointer dereference",
    partial: false,
    typeId: type.definitionId,
    stableSelector,
    accessibility: "internal",
    declaringType: performanceDeclaringType(type),
    items: [],
  };
}

test(
  "an Unsafe row on a Type the ordinary list omits opens it as a transient Type",
  () => {
    const member = performanceMember();
    const type = performanceType(member);
    const packageItem: AppPackage = packageModel({ types: [] });
    const row = unsafeRow(type, member.stableSelector);

    assert.equal(
      resolvePackagePerformanceType(packageItem, { assembly: type.assembly, typeId: row.typeId ?? "" }),
      null);
    const projected = projectPackagePerformanceType({ declaringType: row.declaringType! });
    assert.deepEqual(projected, { ...type, api: [] });
    assert.equal(projected.definitionId, row.typeId);
    assert.deepEqual(packageItem.types, []);
  });

test(
  "a Platform Unsafe row names its assembly as the Platform surface Types do",
  () => {
    const member = performanceMember();
    const type = { ...performanceType(member), assembly: "System.Fixture" };
    const packageItem: AppPackage = packageModel({ types: [type] });
    const row = unsafeRow(type, member.stableSelector);

    assert.equal(
      resolvePackagePerformanceType(
        packageItem,
        { assembly: row.declaringType!.assembly, typeId: row.typeId ?? "" }),
      type);
    assert.equal(
      resolvePackagePerformanceType(
        packageItem,
        { assembly: "System.Fixture.dll", typeId: row.typeId ?? "" }),
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

test("package metadata state distinguishes loading, evidence, and failure at compile time", () => {
  const result = metadataResult();
  const loadingWithResult: PackageMetadataState = {
    status: "loading",
    request: { key: "current" },
    // @ts-expect-error A loading request cannot also carry metadata evidence.
    result,
  };
  const readyWithError: PackageMetadataState = {
    status: "ready",
    key: "current",
    result,
    // @ts-expect-error Ready metadata cannot also carry a complete-failure diagnostic.
    error: "failure",
  };
  // @ts-expect-error A failed metadata request always carries its visible diagnostic.
  const failedWithoutError: PackageMetadataState = {
    status: "failed",
    key: "current",
  };
  void [loadingWithResult, readyWithError, failedWithoutError];
});

function libraryDependencyStructureResult() {
  return {
    outcome: "available" as const,
    methodologyVersion: "library-dependency-structure.v1",
    completeness: "Complete",
    population: null,
    namespaces: [],
    namespaceEdges: [],
    totalNamespaceEdgeCount: 0,
    cycles: [],
    diagnostics: [],
    failure: null,
  };
}

function inspectionDependencies(
  state: PackageInspectionState,
  overrides: Partial<Omit<PackageInspectionDependencies, "state">> = {},
): PackageInspectionDependencies {
  return {
    state,
    queryDependencies: async packageItem => dependencyResult(packageItem.id),
    queryLibraryReferences: async () => libraryReferenceResult(),
    queryVulnerabilities: async () => vulnerabilityResult(),
    queryPackageIntegrations: async () => integrationsResult(),
    queryPlatformIntegrations: async () => integrationsResult(),
    queryPackageOpportunities: async () => opportunitiesResult(),
    queryPlatformOpportunities: async () => opportunitiesResult(),
    queryPackagePerformance: async () => performanceResult(),
    queryPackageResourceTriage: async () => resourceTriageResult(),
    queryPlatformResourceTriage: async () => resourceTriageResult(),
    queryPackageUnsafeMembers: async () => unsafeMembersResult(),
    queryPlatformUnsafeMembers: async () => unsafeMembersResult(),
    queryPlatformPerformance: async () => performanceResult(),
    queryPackageLibraryDependencyStructure: async () =>
      libraryDependencyStructureResult(),
    queryPlatformLibraryDependencyStructure: async () =>
      libraryDependencyStructureResult(),
    queryPackageLibraryMetrics: async () => ({
      outcome: "available",
      methodologyVersion: "library-metrics.v1",
      population: null,
      distributions: [],
      asyncStateMachinePresence: null,
      typeSummaries: [],
      entangledRelationships: [],
      diagnostics: [],
      failure: null,
      compileLibrary: {
        status: "Selected",
        targetFramework: "net10.0",
        message: null,
      },
    }),
    queryPlatformLibraryMetrics: async () => ({
      outcome: "available",
      methodologyVersion: "library-metrics.v1",
      population: null,
      distributions: [],
      asyncStateMachinePresence: null,
      typeSummaries: [],
      entangledRelationships: [],
      diagnostics: [],
      failure: null,
      compileLibrary: {
        status: "Selected",
        targetFramework: "net10.0",
        message: null,
      },
    }),
    queryPackageMetadata: async () => metadataResult(),
    queryPlatformMetadata: async () => metadataResult(),
    platformLibraryCoordinates: (_package, assemblyName) => ({
      assemblyFileName: `${assemblyName}.dll`, pack: `pack:${assemblyName}`,
    }),
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

test("library metrics do not widen into dependency analysis", async () => {
  const selected = packageModel();
  const state = inspectionState({ packages: [selected] });
  const defaults = inspectionDependencies(state);
  let metricsQueries = 0;
  let dependencyQueries = 0;
  const coordinator = createPackageInspectionCoordinator(
    inspectionDependencies(state, {
      queryPackageLibraryMetrics: async (...args) => {
        metricsQueries++;
        return defaults.queryPackageLibraryMetrics(...args);
      },
      queryPackageLibraryDependencyStructure: async (...args) => {
        dependencyQueries++;
        return defaults.queryPackageLibraryDependencyStructure(...args);
      },
    }));

  await coordinator.loadLibraryMetrics(
    selected,
    "Example.Package@1.2.3/net10.0/Example.Package",
    "Example.Package");

  assert.equal(metricsQueries, 1);
  assert.equal(dependencyQueries, 0);
  assert.equal(state.packageLibraryDependencyStructure, null);

  state.packageLibraryMetricsRelationshipState = {
    visibleCount: 24,
    selectedSourceTypeKey: "Example.Source",
    selectedTargetTypeKey: "Example.Target",
  };
  await coordinator.loadLibraryDependencyStructure(
    selected,
    "Example.Package@1.2.3/net10.0/Example.Package",
    "Example.Package");

  assert.equal(dependencyQueries, 1);
  assert.deepEqual(
    state.packageLibraryDependencyStructure,
    libraryDependencyStructureResult(),
  );
  assert.deepEqual(state.packageLibraryMetricsRelationshipState, {
    visibleCount: 24,
    selectedSourceTypeKey: "Example.Source",
    selectedTargetTypeKey: "Example.Target",
  });
});

test("Library References does not acquire other workspace dependencies", async () => {
  const selected = packageModel({ runtimeIdentifier: "browser-wasm" });
  const other = packageModel({ id: "Other.Package" });
  const state = inspectionState({
    packages: [selected, other],
    atLibraryRoot: true,
    libraryLens: "references",
  });
  const calls: string[] = [];
  const coordinator = createPackageInspectionCoordinator(
    inspectionDependencies(state, {
      queryLibraryReferences: async pkg => {
        calls.push(
          `${pkg.id}#${pkg.assemblyId}@${pkg.runtimeIdentifier ?? ""}`);
        return libraryReferenceResult();
      },
    }));
  await coordinator.loadLibraryReferences(selected, "library:references");
  assert.deepEqual(
    calls,
    ["Example.Package#example-package@browser-wasm"]);
  assert.deepEqual(state.packageDependencies, { status: "idle" });
  assert.deepEqual(state.workspaceDependencies, {});
});

test("Library References replaces retained rows before a new Library settles", async () => {
  const first = deferred<LibraryReferenceInspectionEnvelope>();
  const second = deferred<LibraryReferenceInspectionEnvelope>();
  const requests = [first, second];
  const selected = packageModel();
  const state = inspectionState({ packages: [selected] });
  const coordinator = createPackageInspectionCoordinator(
    inspectionDependencies(state, {
      queryLibraryReferences: async () => requests.shift()!.promise,
    }));

  const firstLoad =
    coordinator.loadLibraryReferences(selected, "library:first");
  assert.deepEqual(state.libraryReferences, {
    status: "loading",
    request: { key: "library:first" },
  });

  const secondLoad =
    coordinator.loadLibraryReferences(selected, "library:second");
  assert.deepEqual(state.libraryReferences, {
    status: "loading",
    request: { key: "library:second" },
  });

  const current = libraryReferenceResult([{
    name: "Current.Reference",
    version: "1.0.0.0",
    culture: null,
    publicKeyToken: null,
  }]);
  second.resolve(current);
  await secondLoad;
  first.resolve(libraryReferenceResult([{
    name: "Retired.Reference",
    version: "1.0.0.0",
    culture: null,
    publicKeyToken: null,
  }]));
  await firstLoad;

  assert.deepEqual(state.libraryReferences, {
    status: "ready",
    key: "library:second",
    result: current,
  });
});

test("dependency queries do not require a selected Library or compile asset", async () => {
  const truncated = packageModel({
    id: "OpenAI",
    assemblyId: "",
    assembly: "",
    assemblyAsset: "",
    selectedCompileAssetId: "lib/net10.0/OpenAI.dll",
  });
  const rootOnly = packageModel({
    id: "Root.Only",
    assemblyId: "",
    assembly: "",
    assemblyAsset: "",
  });
  const state = inspectionState({ packages: [truncated, rootOnly] });
  const calls: string[] = [];
  const coordinator = createPackageInspectionCoordinator(
    inspectionDependencies(state, {
      queryDependencies: async pkg => {
        calls.push(
          `${pkg.id}@${pkg.version}/${pkg.activeFramework}`
          + `#${pkg.runtimeIdentifier ?? ""}`);
        return dependencyResult(pkg.id);
      },
    }));

  await coordinator.loadDependencies(truncated, "dependencies");
  await coordinator.ensureWorkspaceDependencies();

  assert.deepEqual(
    [...new Set(calls)].sort(),
    ["OpenAI@1.2.3/net10.0#", "Root.Only@1.2.3/net10.0#"]);
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
  const request = deferred<BrowserPackageDependencyInspection>();
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
  assert.equal(state.packageDependencies.status, "loading");
  const replacement = { status: "loading" as const, request: { key: "second" } };
  state.packageDependencies = replacement;
  request.resolve(dependencyResult(packageItem.id, "partial dependency data"));
  await load;

  assert.strictEqual(state.packageDependencies, replacement);
  const key = workspaceDependencyKey(packageItem);
  const workspace = state.workspaceDependencies[key];
  assert.ok(workspace);
  assert.equal(
    workspace.dependencyGroups?.[0]?.framework,
    "net10.0");
  assert.equal(
    state.workspaceDependencyErrors[key],
    "The manifest declares no dependency group for the active target framework.");
  assert.deepEqual(events, ["render", "stats", "render"]);
});

test("newer same-coordinate dependency evidence owns foreground settlement", async () => {
  const firstA = deferred<BrowserPackageDependencyInspection>();
  const packageB = deferred<BrowserPackageDependencyInspection>();
  const secondA = deferred<BrowserPackageDependencyInspection>();
  const requests = [firstA, packageB, secondA];
  const state = inspectionState();
  let queries = 0;
  const coordinator = createPackageInspectionCoordinator(
    inspectionDependencies(state, {
      queryDependencies: async () => {
        queries++;
        return requests.shift()!.promise;
      },
    }));
  const packageItem = packageModel();
  const firstALoad = coordinator.loadDependencies(packageItem, "A");
  const duplicateALoad = coordinator.loadDependencies(packageItem, "A");
  assert.equal(queries, 1);
  const packageBLoad = coordinator.loadDependencies(packageItem, "B");
  const secondALoad = coordinator.loadDependencies(packageItem, "A");
  const newerEvidence = dependencyResult("Newer.Package");
  secondA.resolve(newerEvidence);
  await secondALoad;
  firstA.resolve(dependencyResult("Older.Package"));
  packageB.resolve(dependencyResult("Other.Package"));
  await Promise.all([firstALoad, duplicateALoad, packageBLoad]);

  assert.equal(queries, 3);
  assert.deepEqual(state.packageDependencies, {
    status: "ready",
    key: "A",
    result: newerEvidence,
  });
});

test("dependency query failure is visible and cached for the same key", async () => {
  const packageItem = packageModel();
  const state = inspectionState();
  let queries = 0;
  const coordinator = createPackageInspectionCoordinator(
    inspectionDependencies(state, {
      queryDependencies: async () => {
        queries++;
        throw new Error("");
      },
    }));

  await coordinator.loadDependencies(packageItem, "current");
  await coordinator.loadDependencies(packageItem, "current");

  assert.equal(queries, 1);
  assert.deepEqual(state.packageDependencies, {
    status: "failed",
    key: "current",
    error: "Dependency query failed.",
  });
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

test("an unmatched shared dependency group keeps the workspace graph visibly partial", async () => {
  const packageItem = packageModel();
  const key = workspaceDependencyKey(packageItem);
  const result = dependencyResult(packageItem.id, "no matching group");
  const state = inspectionState({ packages: [packageItem] });
  const coordinator = createPackageInspectionCoordinator(
    inspectionDependencies(state, {
      queryDependencies: async () => result,
    }));

  await coordinator.loadDependencies(packageItem, "current");

  assert.equal(
    state.workspaceDependencyErrors[key],
    "The manifest declares no dependency group for the active target framework.");
  assert.equal(
    state.workspaceDependencies[key]?.dependencyGroups?.[0]?.dependencies?.[0]?.id,
    "Example.Dependency");
});

test("unsafe shared dependency framework text fails Browser presentation visibly", () => {
  const projection = dependencyProjection(
    dependencyResult("Example.Package", null, "net10.0\u202e"));

  assert.deepEqual(projection.dependencyGroups, []);
  assert.equal(
    projection.inspectionError,
    "A dependency group framework identifier cannot be represented safely.");
});

test("package lens loaders reuse cached results without querying or clearing them", async () => {
  const packageItem = packageModel();
  const dependencies = dependencyResult();
  const vulnerabilities = vulnerabilityResult();
  const integrations = integrationsResult();
  const opportunities = opportunitiesResult();
  const performance = performanceResult();
  const metadata = metadataResult();
  const cases = [
    {
      name: "dependencies",
      cached: dependencies,
      state: inspectionState({
        packageDependencies: {
          status: "ready",
          key: "cached",
          result: dependencies,
        },
      }),
      read: (state: PackageInspectionState) =>
        state.packageDependencies.status === "ready"
          ? state.packageDependencies.result
          : null,
      load: (coordinator: ReturnType<typeof createPackageInspectionCoordinator>) =>
        coordinator.loadDependencies(packageItem, "cached"),
    },
    {
      name: "vulnerabilities",
      cached: vulnerabilities,
      state: inspectionState({
        packageVulnerabilities: {
          status: "ready",
          key: "cached",
          result: vulnerabilities,
        },
      }),
      read: (state: PackageInspectionState) =>
        state.packageVulnerabilities.status === "ready"
          ? state.packageVulnerabilities.result
          : null,
      load: (coordinator: ReturnType<typeof createPackageInspectionCoordinator>) =>
        coordinator.loadVulnerabilities(packageItem, "cached"),
    },
    {
      name: "integrations",
      cached: integrations,
      state: inspectionState({
        packageIntegrations: {
          status: "ready",
          key: "cached",
          result: integrations,
        },
      }),
      read: (state: PackageInspectionState) =>
        state.packageIntegrations.status === "ready"
          ? state.packageIntegrations.result
          : null,
      load: (coordinator: ReturnType<typeof createPackageInspectionCoordinator>) =>
        coordinator.loadIntegrations(packageItem, "cached", null),
    },
    {
      name: "opportunities",
      cached: opportunities,
      state: inspectionState({
        packageOpportunities: {
          status: "ready",
          key: "cached",
          result: opportunities,
        },
      }),
      read: (state: PackageInspectionState) =>
        state.packageOpportunities.status === "ready"
          ? state.packageOpportunities.result
          : null,
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
        packageMetadata: {
          status: "ready",
          key: "cached",
          result: metadata,
        },
      }),
      read: (state: PackageInspectionState) =>
        state.packageMetadata.status === "ready"
          ? state.packageMetadata.result
          : null,
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
        queryVulnerabilities: async () => {
          queries++;
          return vulnerabilityResult();
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

test("scalar-field package lenses preserve lifecycle and ownership across invalidation", async () => {
  const packageItem = packageModel();

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
    name: "resource triage",
    result: resourceTriageResult(),
    createCoordinator: (state, query, render = () => {}) =>
      createPackageInspectionCoordinator(
        inspectionDependencies(state, {
          queryPackageResourceTriage: async () => query(),
          render,
        })),
    load: (coordinator, signature) =>
      coordinator.loadResourceTriage(packageItem, signature, null),
    readResult: state => state.packageResourceTriage,
    readLoading: state => state.packageResourceTriageLoading,
    readError: state => state.packageResourceTriageError,
    setKey: (state, key) => { state.packageResourceTriageKey = key; },
    setError: (state, error) => { state.packageResourceTriageError = error; },
  });
  await verifyPackageLensLifecycle({
    name: "unsafe members",
    result: unsafeMembersResult(),
    createCoordinator: (state, query, render = () => {}) =>
      createPackageInspectionCoordinator(
        inspectionDependencies(state, {
          queryPackageUnsafeMembers: async () => query(),
          render,
        })),
    load: (coordinator, signature) =>
      coordinator.loadUnsafeMembers(packageItem, signature, null),
    readResult: state => state.packageUnsafeMembers,
    readLoading: state => state.packageUnsafeMembersLoading,
    readError: state => state.packageUnsafeMembersError,
    setKey: (state, key) => { state.packageUnsafeMembersKey = key; },
    setError: (state, error) => { state.packageUnsafeMembersError = error; },
  });
});

test("package integrations state owns loading, settlement, caching, and replacement", async () => {
  const packageItem = packageModel();
  const result = integrationsResult();
  const query = deferred<BrowserPackageIntegrations>();
  const events: string[] = [];
  const state = inspectionState({
    packageIntegrations: {
      status: "failed",
      key: "old",
      error: "old failure",
    },
  });
  const coordinator = createPackageInspectionCoordinator(
    inspectionDependencies(state, {
      queryPackageIntegrations: async () => query.promise,
      render: () => events.push("render"),
    }));

  const load = coordinator.loadIntegrations(packageItem, "current", null);

  assert.equal(state.packageIntegrations.status, "loading");
  if (state.packageIntegrations.status === "loading") {
    assert.equal(state.packageIntegrations.request.key, "current");
  }
  assert.deepEqual(events, ["render"]);

  query.resolve(result);
  await load;

  assert.deepEqual(state.packageIntegrations, {
    status: "ready",
    key: "current",
    result,
  });
  assert.deepEqual(events, ["render", "render"]);

  let cachedQueries = 0;
  await createPackageInspectionCoordinator(
    inspectionDependencies(state, {
      queryPackageIntegrations: async () => {
        cachedQueries++;
        return result;
      },
      render: () => events.push("cached render"),
    }))
    .loadIntegrations(packageItem, "current", null);
  assert.equal(cachedQueries, 0);
  assert.equal(events.at(-1), "cached render");

  const failed = inspectionState();
  await createPackageInspectionCoordinator(
    inspectionDependencies(failed, {
      queryPackageIntegrations: async () => {
        throw new Error("current failure");
      },
    }))
    .loadIntegrations(packageItem, "current", null);
  assert.deepEqual(failed.packageIntegrations, {
    status: "failed",
    key: "current",
    error: "current failure",
  });

  const stale = deferred<BrowserPackageIntegrations>();
  const current = deferred<BrowserPackageIntegrations>();
  const requests = [stale, current];
  const replaced = inspectionState();
  const replacementCoordinator = createPackageInspectionCoordinator(
    inspectionDependencies(replaced, {
      queryPackageIntegrations: async () => requests.shift()!.promise,
    }));
  const staleLoad =
    replacementCoordinator.loadIntegrations(packageItem, "same", null);
  const currentLoad =
    replacementCoordinator.loadIntegrations(packageItem, "same", null);
  const currentResult = integrationsResult();
  current.resolve(currentResult);
  await currentLoad;
  stale.reject(new Error("stale failure"));
  await staleLoad;

  assert.equal(replaced.packageIntegrations.status, "ready");
  if (replaced.packageIntegrations.status === "ready") {
    assert.strictEqual(replaced.packageIntegrations.result, currentResult);
  }
});

test("package opportunities state owns loading, settlement, caching, and replacement", async () => {
  const packageItem = packageModel();
  const result = opportunitiesResult();
  const query = deferred<BrowserPackageOpportunities>();
  const events: string[] = [];
  const state = inspectionState({
    packageOpportunities: {
      status: "failed",
      key: "old",
      error: "old failure",
    },
  });
  const coordinator = createPackageInspectionCoordinator(
    inspectionDependencies(state, {
      queryPackageOpportunities: async () => query.promise,
      render: () => events.push("render"),
    }));

  const load = coordinator.loadOpportunities(packageItem, "current", null);

  assert.equal(state.packageOpportunities.status, "loading");
  if (state.packageOpportunities.status === "loading") {
    assert.equal(state.packageOpportunities.request.key, "current");
  }
  assert.deepEqual(events, ["render"]);

  query.resolve(result);
  await load;

  assert.deepEqual(state.packageOpportunities, {
    status: "ready",
    key: "current",
    result,
  });
  assert.deepEqual(events, ["render", "render"]);

  let cachedQueries = 0;
  await createPackageInspectionCoordinator(
    inspectionDependencies(state, {
      queryPackageOpportunities: async () => {
        cachedQueries++;
        return result;
      },
      render: () => events.push("cached render"),
    }))
    .loadOpportunities(packageItem, "current", null);
  assert.equal(cachedQueries, 0);
  assert.equal(events.at(-1), "cached render");

  const failed = inspectionState();
  await createPackageInspectionCoordinator(
    inspectionDependencies(failed, {
      queryPackageOpportunities: async () => {
        throw new Error("current failure");
      },
    }))
    .loadOpportunities(packageItem, "current", null);
  assert.deepEqual(failed.packageOpportunities, {
    status: "failed",
    key: "current",
    error: "current failure",
  });

  let emptyFailureQueries = 0;
  const emptyFailure = inspectionState();
  const emptyFailureCoordinator = createPackageInspectionCoordinator(
    inspectionDependencies(emptyFailure, {
      queryPackageOpportunities: async () => {
        emptyFailureQueries++;
        throw new Error("");
      },
    }));
  await emptyFailureCoordinator
    .loadOpportunities(packageItem, "current", null);
  await emptyFailureCoordinator
    .loadOpportunities(packageItem, "current", null);
  assert.equal(emptyFailureQueries, 1);
  assert.deepEqual(emptyFailure.packageOpportunities, {
    status: "failed",
    key: "current",
    error: "Opportunity inspection failed.",
  });

  const stale = deferred<BrowserPackageOpportunities>();
  const current = deferred<BrowserPackageOpportunities>();
  const requests = [stale, current];
  const replaced = inspectionState();
  const replacementCoordinator = createPackageInspectionCoordinator(
    inspectionDependencies(replaced, {
      queryPackageOpportunities: async () => requests.shift()!.promise,
    }));
  const staleLoad =
    replacementCoordinator.loadOpportunities(packageItem, "same", null);
  const currentLoad =
    replacementCoordinator.loadOpportunities(packageItem, "same", null);
  const currentResult = opportunitiesResult();
  current.resolve(currentResult);
  await currentLoad;
  stale.reject(new Error("stale failure"));
  await staleLoad;

  assert.equal(replaced.packageOpportunities.status, "ready");
  if (replaced.packageOpportunities.status === "ready") {
    assert.strictEqual(replaced.packageOpportunities.result, currentResult);
  }
});

test("package vulnerability state owns loading, settlement, caching, and replacement", async () => {
  const packageItem = packageModel();
  const result = vulnerabilityResult();
  const query = deferred<BrowserPackageVulnerabilityResult>();
  const events: string[] = [];
  const state = inspectionState({
    packageVulnerabilities: {
      status: "failed",
      key: "old",
      error: "old failure",
    },
  });
  const coordinator = createPackageInspectionCoordinator(
    inspectionDependencies(state, {
      queryVulnerabilities: async () => query.promise,
      render: () => events.push("render"),
    }));

  const load = coordinator.loadVulnerabilities(packageItem, "current");

  assert.equal(state.packageVulnerabilities.status, "loading");
  if (state.packageVulnerabilities.status === "loading") {
    assert.equal(state.packageVulnerabilities.request.key, "current");
  }
  assert.deepEqual(events, ["render"]);

  query.resolve(result);
  await load;

  assert.deepEqual(state.packageVulnerabilities, {
    status: "ready",
    key: "current",
    result,
  });
  assert.deepEqual(events, ["render", "render"]);

  let cachedQueries = 0;
  await createPackageInspectionCoordinator(
    inspectionDependencies(state, {
      queryVulnerabilities: async () => {
        cachedQueries++;
        return result;
      },
      render: () => events.push("cached render"),
    }))
    .loadVulnerabilities(packageItem, "current");
  assert.equal(cachedQueries, 0);
  assert.equal(events.at(-1), "cached render");

  const failed = inspectionState();
  await createPackageInspectionCoordinator(
    inspectionDependencies(failed, {
      queryVulnerabilities: async () => {
        throw new Error("current failure");
      },
    }))
    .loadVulnerabilities(packageItem, "current");
  assert.deepEqual(failed.packageVulnerabilities, {
    status: "failed",
    key: "current",
    error: "current failure",
  });

  const stale = deferred<BrowserPackageVulnerabilityResult>();
  const replaced = inspectionState();
  let replacementRenders = 0;
  const replacementCoordinator = createPackageInspectionCoordinator(
    inspectionDependencies(replaced, {
      queryVulnerabilities: async () => stale.promise,
      render: () => replacementRenders++,
    }));
  const staleLoad =
    replacementCoordinator.loadVulnerabilities(packageItem, "first");
  const newerRequest = { key: "second" };
  replaced.packageVulnerabilities = {
    status: "loading",
    request: newerRequest,
  };
  stale.reject(new Error("stale failure"));
  await staleLoad;

  assert.deepEqual(replaced.packageVulnerabilities, {
    status: "loading",
    request: newerRequest,
  });
  assert.equal(replacementRenders, 1);
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
  assert.equal(state.packageMetadata.status, "loading");
  const firstRequest = state.packageMetadata.status === "loading"
    ? state.packageMetadata.request
    : null;
  assert.ok(firstRequest);
  const duplicateALoad = coordinator.loadMetadata(packageItem, "A", null);
  assert.equal(queries, 1);
  await duplicateALoad;
  assert.deepEqual(state.packageMetadata, {
    status: "loading",
    request: firstRequest,
  });

  const bLoad = coordinator.loadMetadata(packageItem, "B", null);
  const newestALoad = coordinator.loadMetadata(packageItem, "A", null);
  assert.equal(queries, 3);

  const newest = metadataResult();
  newestA.resolve(newest);
  await newestALoad;
  firstA.reject(new Error("stale A failure"));
  await firstALoad;

  assert.deepEqual(state.packageMetadata, {
    status: "ready",
    key: "A",
    result: newest,
  });

  b.resolve(metadataResult());
  await bLoad;
  assert.deepEqual(state.packageMetadata, {
    status: "ready",
    key: "A",
    result: newest,
  });
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

  assert.deepEqual(state.packageMetadata, {
    status: "failed",
    key: "metadata",
    error: "all metadata reads failed",
  });

  await coordinator.loadMetadata(packageItem, "metadata", null);

  assert.equal(queries, 2);
  assert.deepEqual(state.packageMetadata, {
    status: "failed",
    key: "metadata",
    error: "all metadata reads failed",
  });
});

test("partial metadata remains ready while empty thrown diagnostics stay visible", async () => {
  const packageItem = packageModel({
    id: "System.Text.Json",
    version: "10.0.0",
  });
  const partial: PackageMetadata = {
    assemblies: [{ assembly: "Example.Package.dll" }],
    inspectionError: "one image could not be read",
  };
  let next: "partial" | "failure" = "partial";
  const state = inspectionState();
  const coordinator = createPackageInspectionCoordinator(
    inspectionDependencies(state, {
      queryPackageMetadata: async () => {
        if (next === "partial") return partial;
        throw new Error("");
      },
    }));

  await coordinator.loadMetadata(packageItem, "partial", null);
  assert.deepEqual(state.packageMetadata, {
    status: "ready",
    key: "partial",
    result: partial,
  });

  next = "failure";
  await coordinator.loadMetadata(packageItem, "failure", null);
  assert.deepEqual(state.packageMetadata, {
    status: "failed",
    key: "failure",
    error: "Metadata inspection failed.",
  });
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
      dependencyGroups: dependencyProjection().dependencyGroups,
      dependencyGroupError: "retained partial data",
    },
  };
  const workspaceDependencyErrors = { [key]: "retained partial data" };
  const retainedState = {
    packages: [remaining],
    atPackageRoot: false,
    atLibraryRoot: true,
    packageLens: "dependencies" as const,
    libraryLens: "analysis" as const,
    workspaceDependencies,
    workspaceDependencyErrors,
  };
  const state = inspectionState({
    ...retainedState,
    packageMetadataRoot: "r2r-manifest",
    packageDependencies: {
      status: "loading",
      request: { key: "dependencies" },
    },
    packageVulnerabilities: {
      status: "failed",
      key: "vulnerabilities",
      error: "vulnerability failure",
    },
    packageIntegrations: {
      status: "failed",
      key: "integrations",
      error: "integration failure",
    },
    packageOpportunities: {
      status: "failed",
      key: "opportunities",
      error: "opportunity failure",
    },
    packagePerformance: performanceResult(),
    packagePerformanceLoading: true,
    packagePerformanceError: "performance failure",
    packagePerformanceKey: "performance",
    packageMetadata: {
      status: "loading",
      request: { key: "metadata" },
    },
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

test("newer same-coordinate vulnerability evidence owns settlement", async () => {
  const firstA = deferred<BrowserPackageVulnerabilityResult>();
  const packageB = deferred<BrowserPackageVulnerabilityResult>();
  const secondA = deferred<BrowserPackageVulnerabilityResult>();
  const requests = [firstA, packageB, secondA];
  const state = inspectionState();
  const coordinator = createPackageInspectionCoordinator(
    inspectionDependencies(state, {
      queryVulnerabilities: async () => requests.shift()!.promise,
    }));
  const packageA = packageModel({ version: "1.0.0" });
  const packageBModel = packageModel({ version: "2.0.0" });
  const firstALoad =
    coordinator.loadVulnerabilities(packageA, "Example.Package@1.0.0");
  const packageBLoad =
    coordinator.loadVulnerabilities(packageBModel, "Example.Package@2.0.0");
  const secondALoad =
    coordinator.loadVulnerabilities(packageA, "Example.Package@1.0.0");
  const newerEvidence = {
    ...vulnerabilityResult(),
    version: "1.0.0",
    availability: "Partial" as const,
    failures: ["RateLimitOrForbidden" as const],
  };

  secondA.resolve(newerEvidence);
  await secondALoad;
  firstA.resolve({
    ...vulnerabilityResult(),
    version: "1.0.0",
  });
  packageB.resolve({
    ...vulnerabilityResult(),
    version: "2.0.0",
  });
  await Promise.all([firstALoad, packageBLoad]);

  assert.deepEqual(state.packageVulnerabilities, {
    status: "ready",
    key: "Example.Package@1.0.0",
    result: newerEvidence,
  });
});

test("invalidated package completions cannot publish, render, or start follow-up work", async () => {
  for (const outcome of ["success", "failure"]) {
    const removed = packageModel();
    const remaining = packageModel({ id: "Example.Remaining" });
    const dependencies = deferred<BrowserPackageDependencyInspection>();
    const vulnerabilities = deferred<BrowserPackageVulnerabilityResult>();
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
        queryVulnerabilities: async () => vulnerabilities.promise,
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
      coordinator.loadVulnerabilities(removed, "vulnerabilities"),
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
      vulnerabilities.resolve(vulnerabilityResult());
      integrations.resolve(integrationsResult());
      opportunities.resolve(opportunitiesResult());
      performance.resolve(performanceResult());
      metadata.resolve(metadataResult());
    } else {
      for (const request of [
        dependencies, vulnerabilities, integrations, opportunities,
        performance, metadata,
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
    const request = deferred<BrowserPackageDependencyInspection>();
    const events: string[] = [];
    const state = inspectionState({
      packages: [removed, queued, cached],
      atPackageRoot: true,
      packageLens: "dependencies",
      workspaceDependencies: {
        [key]: {
          dependencyGroups:
            dependencyProjection(dependencyResult(cached.id)).dependencyGroups,
        },
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
      const stale = deferred<BrowserPackageDependencyInspection>();
      const current = deferred<BrowserPackageDependencyInspection>();
      const currentResult = dependencyResult(removed.id, "current warning");
      const currentProjection = dependencyProjection(currentResult);
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
      assert.deepEqual(
        state.workspaceDependencies[key],
        currentProjection,
        label);
      assert.equal(
        state.workspaceDependencyErrors[key],
        "The manifest declares no dependency group for the active target framework.",
        label);
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
    "The manifest declares no dependency group for the active target framework.");
  assert.equal(
    state.workspaceDependencyErrors[partialKey],
    "The manifest declares no dependency group for the active target framework.");
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
  const firstRequest = deferred<BrowserPackageDependencyInspection>();
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
  const request = deferred<BrowserPackageDependencyInspection>();
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
  const request = deferred<BrowserPackageDependencyInspection>();
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
  const firstRequest = deferred<BrowserPackageDependencyInspection>();
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
  const request = deferred<BrowserPackageDependencyInspection>();
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
  const request = deferred<BrowserPackageDependencyInspection>();
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
  const replacementRequest = { key: "metadata-second" };
  state.packageMetadata = {
    status: "loading",
    request: replacementRequest,
  };
  metadata.resolve(metadataResult());
  await metadataLoad;

  assert.deepEqual(calls, [
    "integrations:Example.Package",
    "opportunities:net10.0/1.2.3/System.Text.Json.dll/pack:System.Text.Json",
    "performance:Example.Package",
    "metadata:Example.Package",
  ]);
  assert.equal(state.packageIntegrations.status, "ready");
  assert.equal(state.packageOpportunities.status, "ready");
  assert.equal(state.packagePerformance, null);
  assert.equal(state.packagePerformanceError, "analysis unavailable");
  assert.equal(state.packagePerformanceLoading, false);
  assert.deepEqual(state.packageMetadata, {
    status: "loading",
    request: replacementRequest,
  });
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
    const query = deferred<BrowserPackageDependencyInspection>();
    const state = inspectionState({
      packageDependencies: {
        status: "ready",
        key: "old",
        result: dependencyResult(),
      },
    });
    const coordinator = createPackageInspectionCoordinator(
      inspectionDependencies(state, {
        queryDependencies: async () => query.promise,
      }));
    const load = coordinator.loadDependencies(packageItem, "new");
    assert.deepEqual(state.packageDependencies, {
      status: "loading",
      request: { key: "new" },
    });
    query.resolve(dependencyResult());
    await load;
  }

  {
    const query = deferred<BrowserPackageIntegrations>();
    const state = inspectionState({
      packageIntegrations: {
        status: "ready",
        key: "old",
        result: integrationsResult(),
      },
    });
    const coordinator = createPackageInspectionCoordinator(
      inspectionDependencies(state, {
        queryPackageIntegrations: async () => query.promise,
      }));
    const load = coordinator.loadIntegrations(packageItem, "new", null);
    assert.equal(state.packageIntegrations.status, "loading");
    query.resolve(integrationsResult());
    await load;
  }

  {
    const query = deferred<BrowserPackageOpportunities>();
    const state = inspectionState({
      packageOpportunities: {
        status: "ready",
        key: "old",
        result: opportunitiesResult(),
      },
    });
    const coordinator = createPackageInspectionCoordinator(
      inspectionDependencies(state, {
        queryPackageOpportunities: async () => query.promise,
      }));
    const load = coordinator.loadOpportunities(packageItem, "new", null);
    assert.equal(state.packageOpportunities.status, "loading");
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
      packageMetadata: {
        status: "ready",
        key: "old",
        result: metadataResult(),
      },
    });
    const coordinator = createPackageInspectionCoordinator(
      inspectionDependencies(state, {
        queryPackageMetadata: async () => query.promise,
      }));
    const load = coordinator.loadMetadata(packageItem, "new", null);
      assert.equal(state.packageMetadata.status, "loading");
      if (state.packageMetadata.status === "loading") {
        assert.equal(state.packageMetadata.request.key, "new");
      }
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
    const newerRequest = { key: "second" };
    state.packageIntegrations = {
      status: "loading",
      request: newerRequest,
    };
    query.resolve(integrationsResult());
    await load;
    assert.deepEqual(state.packageIntegrations, {
      status: "loading",
      request: newerRequest,
    });
  }

  {
    const query = deferred<BrowserPackageOpportunities>();
    const state = inspectionState();
    const coordinator = createPackageInspectionCoordinator(
      inspectionDependencies(state, {
        queryPackageOpportunities: async () => query.promise,
      }));
    const load = coordinator.loadOpportunities(packageItem, "first", null);
    const newerRequest = { key: "second" };
    state.packageOpportunities = {
      status: "loading",
      request: newerRequest,
    };
    query.resolve(opportunitiesResult());
    await load;
    assert.deepEqual(state.packageOpportunities, {
      status: "loading",
      request: newerRequest,
    });
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
  assert.deepEqual(state.packageIntegrations, { status: "idle" });
  assert.deepEqual(state.packageOpportunities, { status: "idle" });
  assert.equal(state.packagePerformanceKey, "");
  assert.deepEqual(state.packageMetadata, { status: "idle" });
});

function unsafeMembersResult(): PackageUnsafeMembers {
  return { outcome: "available", assembly: "Selected.dll", counts: { members: 0, public: 0, propagating: 0, safeBoundaries: 0, safeBoundaryUndecided: 0 }, types: [], limitations: [], inspectionError: null };
}

test("unsafe members route the exact platform library and wait for selection", async () => {
  const runtime = packageModel({ id: "Microsoft.NETCore.App", isRuntimePack: true, source: { kind: "platform" } });
  const state = inspectionState();
  const calls: string[][] = [];
  const coordinator = createPackageInspectionCoordinator(inspectionDependencies(state, {
    queryPlatformUnsafeMembers: async (...args) => { calls.push(args); return unsafeMembersResult(); },
    queryPackageUnsafeMembers: async () => { throw new Error("Platform selection must use the platform query"); },
  }));
  await coordinator.loadUnsafeMembers(runtime, "none", null);
  assert.equal(calls.length, 0);
  await coordinator.loadUnsafeMembers(runtime, "selected", "System.Text.Json");
  assert.deepEqual(calls, [["net10.0", "1.2.3", "System.Text.Json.dll", "pack:System.Text.Json"]]);
  assert.equal(state.packageUnsafeMembers?.outcome, "available");
});

function resourceTriageResult(): PackageResourceTriage {
  return { outcome: "available", candidates: [], limitations: [], inspectionError: null, share: null, diagnostics: [] };
}

test("resource triage routes the exact platform library and waits for selection", async () => {
  const runtime = packageModel({ id: "Microsoft.NETCore.App", isRuntimePack: true, source: { kind: "platform" } });
  const state = inspectionState();
  const calls: string[][] = [];
  const coordinator = createPackageInspectionCoordinator(inspectionDependencies(state, {
    queryPlatformResourceTriage: async (...args) => { calls.push(args); return resourceTriageResult(); },
    queryPackageResourceTriage: async () => { throw new Error("Platform selection must use the platform query"); },
  }));
  await coordinator.loadResourceTriage(runtime, "none", null);
  assert.equal(calls.length, 0);
  await coordinator.loadResourceTriage(runtime, "selected", "System.Text.Json");
  assert.deepEqual(calls, [["net10.0", "1.2.3", "System.Text.Json.dll", "pack:System.Text.Json"]]);
  assert.equal(state.packageResourceTriage?.outcome, "available");
});

test("resource triage retains incomplete candidates and reuses its exact request", async () => {
  const pkg = packageModel();
  const state = inspectionState();
  const partial = { ...resourceTriageResult(), outcome: "incomplete", limitations: [{ kind: "ExceptionFlow", detail: "partial", method: null }] };
  let calls = 0;
  const coordinator = createPackageInspectionCoordinator(inspectionDependencies(state, {
    queryPackageResourceTriage: async (_pkg, library) => { assert.equal(library, "Selected.dll"); calls++; return partial; },
  }));
  await coordinator.loadResourceTriage(pkg, "selected", "Selected.dll");
  await coordinator.loadResourceTriage(pkg, "selected", "Selected.dll");
  assert.equal(calls, 1);
  assert.equal(state.packageResourceTriage, partial);
  assert.equal(state.packageResourceTriageError, "");
});
