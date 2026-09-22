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
  engineWorkerUploadedLibraryMaximumBytes,
  registerEngineWorkerOrdinaryOperations,
  type EngineWorkerOrdinaryFacades,
} from "../src/engine-worker-ordinary.ts";
import type {
  RetainedWorkspaceActivationClient,
} from "../src/retained-workspace-activation.ts";
import {
  FakeWorkerRuntime,
  ManualWorkerRuntimeEnvironment,
  QueueWorkerRuntimeTransportFactory,
  WorkerRuntimeHost,
} from "../src/worker-runtime-core.ts";
import { WorkerOperationCatalog } from "../src/worker-runtime-realm.ts";
import { inertStringFixture } from "./inert-string-fixture.ts";
import type {
  BrowserRetainedWorkspaceActivationResult,
  BrowserRetainedWorkspaceDefinitionState,
  BrowserRetainedWorkspacePackageAdmissionResult,
  BrowserRetainedWorkspacePlatformAdmissionResult,
  BrowserRetainedWorkspacePosting,
} from "../src/facades/inspect-web-catalog.d.ts";
import type {
  BrowserPackageLoadResult,
  BrowserPackageSurface,
} from "../src/facades/inspect-web-package.d.ts";
import type {
  BrowserMemberSource,
  BrowserSource,
} from "../src/facades/inspect-web-source.d.ts";

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
  library: {
    openUploadedLibrary: () => unexpected("openUploadedLibrary"),
  },
  package: {
    classifyPackageGraphIdentities: () =>
      unexpected("classifyPackageGraphIdentities"),
    getPlatformCatalog: () => unexpected("getPlatformCatalog"),
    getPlatformVersions: () => unexpected("getPlatformVersions"),
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
    queryPackageRoot: () => unexpected("queryPackageRoot"),
    loadRuntimePack: () => unexpected("loadRuntimePack"),
    loadRuntimePackAssembly: () =>
      unexpected("loadRuntimePackAssembly"),
    getPackageDocument: () => unexpected("getPackageDocument"),
    queryLibraries: () => unexpected("queryLibraries"),
    queryLibraryApi: () => unexpected("queryLibraryApi"),
    queryMemberDocumentation: () =>
      unexpected("queryMemberDocumentation"),
    queryPlatformMemberDocumentation: () =>
      unexpected("queryPlatformMemberDocumentation"),
    queryPackageDependencies: () =>
      unexpected("queryPackageDependencies"),
    queryPackagePruning: () =>
      unexpected("queryPackagePruning"),
    queryPackageVersions: () => unexpected("queryPackageVersions"),
    queryWorkspacePackageOccurrences: () =>
      unexpected("queryWorkspacePackageOccurrences"),
    resolvePackageDependencyVersion: () =>
      unexpected("resolvePackageDependencyVersion"),
  },
  metadata: {
    cancelLibraryApiDiff: () =>
      unexpected("cancelLibraryApiDiff"),
    queryLibraryApiDiff: () =>
      unexpected("queryLibraryApiDiff"),
    queryMemberDeclaration: () =>
      unexpected("queryMemberDeclaration"),
    queryPlatformMemberDeclaration: () =>
      unexpected("queryPlatformMemberDeclaration"),
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
    queryCloneCandidates: () => unexpected("queryCloneCandidates"),
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
    abandonRetainedWorkspaceNavigation: () =>
      unexpected("abandonRetainedWorkspaceNavigation"),
    acknowledgeRetainedWorkspaceNavigation: () =>
      unexpected("acknowledgeRetainedWorkspaceNavigation"),
    admitRetainedWorkspacePackage: () =>
      unexpected("admitRetainedWorkspacePackage"),
    admitRetainedWorkspacePlatform: () =>
      unexpected("admitRetainedWorkspacePlatform"),
    activateRetainedWorkspaceDefinition: () =>
      unexpected("activateRetainedWorkspaceDefinition"),
    activateRetainedWorkspaceDefinitionWithCredentials: () =>
      unexpected("activateRetainedWorkspaceDefinitionWithCredentials"),
    cancelRetainedWorkspaceActivation: () =>
      unexpected("cancelRetainedWorkspaceActivation"),
    canonicalizeWorkspaceSharePacket: () =>
      unexpected("canonicalizeWorkspaceSharePacket"),
    commitRetainedWorkspaceActivation: () =>
      unexpected("commitRetainedWorkspaceActivation"),
    completeRetainedWorkspaceActivation: () =>
      unexpected("completeRetainedWorkspaceActivation"),
    completeRetainedWorkspaceDeactivation: () =>
      unexpected("completeRetainedWorkspaceDeactivation"),
    deactivateRetainedWorkspaceDefinition: () =>
      unexpected("deactivateRetainedWorkspaceDefinition"),
    describeWorkspacePackageSources: () =>
      unexpected("describeWorkspacePackageSources"),
    resolveHomeDemo: () => unexpected("resolveHomeDemo"),
    decodeWorkspaceShareState: () =>
      unexpected("decodeWorkspaceShareState"),
    encodeWorkspaceShareState: () =>
      unexpected("encodeWorkspaceShareState"),
    observeRetainedWorkspaceSettlement: () =>
      unexpected("observeRetainedWorkspaceSettlement"),
    prepareRetainedWorkspaceDefinition: () =>
      unexpected("prepareRetainedWorkspaceDefinition"),
    prepareRetainedWorkspaceDefinitionWithCredentials: () =>
      unexpected("prepareRetainedWorkspaceDefinitionWithCredentials"),
    recordRetainedWorkspaceNavigationPosting: () =>
      unexpected("recordRetainedWorkspaceNavigationPosting"),
    runHomeDemo: () => unexpected("runHomeDemo"),
    validateRetainedWorkspaceNavigationAuthority: () =>
      unexpected("validateRetainedWorkspaceNavigationAuthority"),
  },
};

function createFacades(
  overrides: FacadeOverrides = {},
): EngineWorkerOrdinaryFacades {
  return {
    library: { ...defaultFacades.library, ...overrides.library },
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

function retainedDetailSurface(
  packageId: string,
  typeId: string,
  platformPack: string | null,
): BrowserPackageSurface {
  const assemblyName = `${packageId}.dll`;
  return {
    package: packageId,
    version: "10.0.0",
    frameworks: ["net10.0"],
    activeFramework: "net10.0",
    icon: {
      mediaType: "image/png",
      base64: "AA==",
    },
    defaultAssemblyId: assemblyName,
    compileLibrary: {
      status: "Selected",
      targetFramework: "net10.0",
      message: null,
    },
    assemblies: [{
      id: assemblyName,
      name: assemblyName,
      version: "10.0.0.0",
      culture: null,
      publicKeyToken: null,
      asset: `lib/net10.0/${assemblyName}`,
      publicTypes: 1,
      publicMembers: 2,
      platformPack,
    }],
    types: [{
      id: typeId,
      definitionId: typeId,
      queryId: typeId,
      metadataId: typeId,
      name: typeId.split(".").at(-1) ?? typeId,
      displayName: typeId,
      namespace: typeId.split(".").slice(0, -1).join("."),
      kind: "class",
      accessibility: "public",
      accessibilityId: "public",
      assembly: assemblyName,
      assemblyId: assemblyName,
      assemblyName,
      members: 2,
      signature: `public class ${typeId}`,
      api: [],
      platformPack,
    }],
    accessibility: [{
      id: "public",
      label: "Public",
      order: 0,
      isDefault: true,
      count: 1,
    }],
    totalMembers: 2,
    documents: [{
      kind: "readme",
      name: "README.md",
      path: "README.md",
      size: 128,
    }],
    inspectionErrors: ["Retained inspection notice."],
    inspectionError: null,
  };
}

test("uploaded Library input uses a bounded structured-clone byte tuple", () => {
  const operation =
    engineWorkerOrdinaryOperations.library.openUploadedLibrary;
  const input: [string, number[]] = [
    "Example.dll",
    [0x4d, 0x5a, 0x00, 0x01],
  ];

  assert.deepEqual(operation.encodeInput(input), {
    kind: "decoded",
    value: input,
  });
  assert.deepEqual(operation.input.decode(input), {
    kind: "decoded",
    value: input,
  });

  const invalidByte = operation.encodeInput([
    "Example.dll",
    [0x4d, 256],
  ]);
  assert.equal(invalidByte.kind, "rejected");
  if (invalidByte.kind === "rejected") {
    assert.equal(invalidByte.reason, "invalid");
    assert.match(invalidByte.message, /outside the byte range/);
  }

  const oversized: number[] = [];
  oversized.length = engineWorkerUploadedLibraryMaximumBytes + 1;
  const oversizedInput = operation.input.decode([
    "Example.dll",
    oversized,
  ]);
  assert.equal(oversizedInput.kind, "rejected");
  if (oversizedInput.kind === "rejected") {
    assert.equal(oversizedInput.reason, "oversized");
    assert.match(oversizedInput.message, /exceeds 33554432 bytes/);
  }
});

test("format 3 packet remains opaque across Browser Worker transport", async () => {
  const packet =
    "eyJmIjozLCJ0IjpbXSwiZyI6W10sInIiOltbInAiLCJNaWNyb3NvZnQuRXh0ZW5zaW9ucy4iXV0sImEiOm51bGwsIngiOm51bGwsInYiOlt7InQiOm51bGwsInUiOnsiayI6IndvcmtzcGFjZSJ9fV19";
  let received = "";
  const state = fixture({
    catalog: {
      canonicalizeWorkspaceSharePacket(encoded) {
        received = encoded;
        return {
          succeeded: true,
          packet: encoded,
          failure: null,
        };
      },
    },
  });

  const result =
    state.client.catalog.canonicalizeWorkspaceSharePacket(packet);
  await state.environment.flushAsync();

  assert.equal(received, packet);
  assert.deepEqual(await result, {
    succeeded: true,
    packet,
    failure: null,
  });
  state.host.dispose();
});

test("retained Catalog transport preserves compact posting and bounded Package and Platform detail", async () => {
  const definition = {
    tabs: [],
    contexts: [],
    registrations: [{
      kind: "packagePrefix",
      exactLibrary: null,
      packagePrefix: "Microsoft.Extensions.",
      ecosystem: null,
    }],
    activeTabId: null,
    selectedContextId: null,
  } satisfies BrowserRetainedWorkspaceDefinitionState;
  const posting = {
    retainedDefinitionId: "definition-exact",
    label: "Example",
    canonicalLocation: "/inspect/example",
    canonicalPacket: "packet-exact",
    realizationId: "realization-exact",
    publicationOrdinal: 7,
    definition,
    navigation: {
      operation: "Initialize",
      request: "request-exact",
      snapshot: {
        generation: "generation-exact",
        scope: {
          kind: "Current",
          runtimeFailure: null,
        },
        workspace: {
          id: "workspace-exact",
          kind: "Workspace",
          label: "Example",
          summary: null,
          parent: null,
        },
        activePackage: null,
        activeSubject: {
          id: "workspace-exact",
          kind: "Workspace",
          label: "Example",
          summary: null,
          parent: null,
        },
        typeInventoryLibraryContext: null,
        packages: [],
        hierarchy: [],
        libraries: [],
        types: [],
        members: [],
        lenses: [],
        lensOutcome: {
          kind: "Applied",
          basis: "Recommendation",
          subject: {
            id: "workspace-exact",
            kind: "Workspace",
            label: "Example",
            summary: null,
            parent: null,
          },
          effectiveLens: null,
          request: null,
          preferredRole: null,
          policyFailure: null,
          resolution: null,
          suspension: null,
        },
        diagnostics: [],
      },
      outcome: {
        kind: "Applied",
        rejection: null,
        failureSource: null,
        message: null,
        request: null,
        resolution: null,
        scope: null,
        diagnostics: [],
        coordinateRetention: null,
      },
      synchronization: "SynchronizationRequired",
      authority: {
        session: "session-exact",
        revision: "revision-exact",
        intent: "intent-exact",
        epoch: "epoch-exact",
      },
    },
    packages: [{
      navigationId: "package-navigation",
      contextIndex: 0,
      consumerPackageSubjectId: "package-subject",
      summary: {
        selectedCompileFramework: null,
        libraryCount: 1,
        typeCount: 1,
        memberCount: 2,
        documentCount: 1,
        hasInspectionNotices: true,
      },
    }],
    platforms: [{
      navigationId: "platform-navigation",
      contextIndex: 1,
      family: "Microsoft.NETCore.App",
      runtimeIdentifier: "linux-x64",
      summary: {
        selectedCompileFramework: "net10.0",
        libraryCount: 1,
        typeCount: 1,
        memberCount: 2,
        documentCount: 1,
        hasInspectionNotices: true,
      },
    }],
    predecessor: null,
    cleanup: null,
  } satisfies BrowserRetainedWorkspacePosting;
  const activation = {
    status: "activated",
    posting,
    failure: null,
  } satisfies BrowserRetainedWorkspaceActivationResult;
  const packageSurface = retainedDetailSurface(
    "Microsoft.Extensions.Logging",
    "Microsoft.Extensions.Logging.ILogger",
    null,
  );
  const admittedPackage = {
    status: "admitted",
    package: {
      navigationId: "package-navigation",
      contextIndex: 0,
      consumerPackageSubjectId: "package-subject",
      surface: packageSurface,
      typePage: {
        offset: 0,
        totalTypes: 2,
        nextOffset: 1,
      },
    },
    message: null,
  } satisfies BrowserRetainedWorkspacePackageAdmissionResult;
  const supersededPackage = {
    status: "superseded",
    package: null,
    message: null,
  } satisfies BrowserRetainedWorkspacePackageAdmissionResult;
  const platformSurface = retainedDetailSurface(
    "Microsoft.NETCore.App",
    "System.String",
    "Microsoft.NETCore.App.Ref",
  );
  const admittedPlatform = {
    status: "admitted",
    platform: {
      navigationId: "platform-navigation",
      contextIndex: 1,
      family: "Microsoft.NETCore.App",
      runtimeIdentifier: "linux-x64",
      surface: platformSurface,
      typePage: {
        offset: 0,
        totalTypes: 2,
        nextOffset: 1,
      },
    },
    message: null,
  } satisfies BrowserRetainedWorkspacePlatformAdmissionResult;
  const supersededPlatform = {
    status: "superseded",
    platform: null,
    message: null,
  } satisfies BrowserRetainedWorkspacePlatformAdmissionResult;
  const unavailablePlatform = {
    status: "unavailable",
    platform: null,
    message: "Retained Platform detail exceeds the Worker JSON bound.",
  } satisfies BrowserRetainedWorkspacePlatformAdmissionResult;
  const packageArguments: (readonly unknown[])[] = [];
  const platformArguments: (readonly unknown[])[] = [];
  const state = fixture({
    catalog: {
      activateRetainedWorkspaceDefinition: async () => activation,
      admitRetainedWorkspacePackage: async (...args) => {
        packageArguments.push(args);
        return args[1] === "old-realization"
          ? supersededPackage
          : admittedPackage;
      },
      admitRetainedWorkspacePlatform: async (...args) => {
        platformArguments.push(args);
        return args[3] === 1
          ? supersededPlatform
          : args[2] === "oversized-navigation"
          ? unavailablePlatform
          : admittedPlatform;
      },
    },
  });

  const activationResult =
    state.client.catalog.activateRetainedWorkspaceDefinition(
      "definition-exact",
      "Example",
      "/inspect/example",
      "packet-exact",
    );
  await state.environment.flushAsync();
  assert.deepEqual(await activationResult, activation);

  const packageResult =
    state.client.catalog.admitRetainedWorkspacePackage(
      "definition-exact",
      "realization-exact",
      "package-navigation",
      0,
    );
  const supersededResult =
    state.client.catalog.admitRetainedWorkspacePackage(
      "definition-exact",
      "old-realization",
      "package-navigation",
      0,
    );
  await state.environment.flushAsync();
  assert.deepEqual(await packageResult, admittedPackage);
  assert.deepEqual(await supersededResult, supersededPackage);

  const platformResult =
    state.client.catalog.admitRetainedWorkspacePlatform(
      "definition-exact",
      "realization-exact",
      "platform-navigation",
      0,
    );
  await state.environment.flushAsync();
  const firstPlatformPage = await platformResult;
  assert.deepEqual(firstPlatformPage, admittedPlatform);
  const nextOffset = firstPlatformPage.platform?.typePage.nextOffset;
  if (nextOffset === null || nextOffset === undefined)
    throw new Error("Expected a retained Platform continuation page.");
  assert.equal(nextOffset, 1);

  const stalePlatformPage =
    state.client.catalog.admitRetainedWorkspacePlatform(
      "definition-exact",
      "realization-exact",
      "platform-navigation",
      nextOffset,
    );
  const unavailableResult =
    state.client.catalog.admitRetainedWorkspacePlatform(
      "definition-exact",
      "realization-exact",
      "oversized-navigation",
      0,
    );
  await state.environment.flushAsync();
  assert.deepEqual(await stalePlatformPage, supersededPlatform);
  assert.deepEqual(await unavailableResult, unavailablePlatform);

  assert.equal("surface" in posting.packages[0]!, false);
  assert.equal("icon" in posting.packages[0]!, false);
  assert.equal("types" in posting.packages[0]!, false);
  assert.equal("surface" in posting.platforms[0]!, false);
  assert.equal("icon" in posting.platforms[0]!, false);
  assert.equal("types" in posting.platforms[0]!, false);
  assert.deepEqual(packageArguments, [[
    "definition-exact",
    "realization-exact",
    "package-navigation",
    0,
  ], [
    "definition-exact",
    "old-realization",
    "package-navigation",
    0,
  ]]);
  assert.deepEqual(platformArguments, [[
    "definition-exact",
    "realization-exact",
    "platform-navigation",
    0,
  ], [
    "definition-exact",
    "realization-exact",
    "platform-navigation",
    1,
  ], [
    "definition-exact",
    "realization-exact",
    "oversized-navigation",
    0,
  ]]);
  assert.deepEqual(state.diagnostics, []);
  state.host.dispose();
});

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
  let classificationArguments: readonly unknown[] = [];
  let matchArguments: readonly unknown[] = [];
  let pruningArguments: readonly unknown[] = [];
  let libraryDiffArguments: readonly unknown[] = [];
  let libraryDiffCancelArguments: readonly unknown[] = [];
  let platformDocumentationArguments: readonly unknown[] = [];
  let libraryQueryArguments: readonly unknown[] = [];
  const state = fixture({
    package: {
      classifyPackageGraphIdentities: (...args) => {
        classificationArguments = args;
        return ["Inspected", "External"];
      },
      searchTypes: () => searchResult,
      activateWorkspacePackageOccurrence: async () => activation,
      clearWorkspacePackageOccurrences: async () => {
        cleared++;
      },
      queryMemberDocumentation: async () =>
        contractViolation(null),
      queryLibraries: (...args) => {
        libraryQueryArguments = args;
        return Promise.resolve({
          content: {
            results: [{
              assetId: "compile:ref/net11.0/Example.dll",
              library: "Example",
              path: "ref/net11.0/Example.dll",
              source: "Example.Package",
              version: "1.0.0.0",
              sourceKind: "Package",
              targetFramework: "net11.0",
              matchedReferences: ["System.Runtime"],
            }],
            failures: [],
            summary: {
              populationCandidates: 1,
              candidateLimit: 256,
              candidates: 1,
              matches: 1,
              failures: 0,
              incompleteReasons: "None",
              isComplete: true,
            },
          },
          share: {
            kind: "Available",
            fullUrl: null,
            packet: "packet",
            path: null,
            reason: null,
          },
          diagnostics: [],
        });
      },
      queryPlatformMemberDocumentation: (...args) => {
        platformDocumentationArguments = args;
        return Promise.resolve(contractViolation(null));
      },
      matchPackageDependencyCoordinate: (...args) => {
        matchArguments = args;
        return { outcome: "Unique", candidateKey: "candidate" };
      },
      queryPackagePruning: (...args) => {
        pruningArguments = args;
        return Promise.resolve({
          schemaVersion: 1,
          package: "Example",
          version: "1.0.0",
          targetFramework: "net10.0",
          selectedFramework: "net10.0",
          family: "Microsoft.NETCore.App",
          platformVersion: "10.0.0",
          completion: "Complete",
          rows: [],
          declarationFailures: [],
          summary: {
            declarations: 0,
            evaluated: 0,
            delegated: 0,
            retained: 0,
            notEvaluated: 0,
            failed: 0,
            declarationFailures: 0,
          },
          message: null,
        });
      },
    },
    metadata: {
      queryLibraryApiDiff: (...args) => {
        libraryDiffArguments = args;
        return contractViolation({
          schemaVersion: 1,
          kind: "Canceled",
          reason: "test",
        });
      },
      cancelLibraryApiDiff: (...args) => {
        libraryDiffCancelArguments = args;
        return { kind: "Requested", reason: "superseded" };
      },
    },
  });

  const sync = state.client.package.searchTypes("String", []);
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
  const libraryQuery = state.client.package.queryLibraries(
    "Example",
    "1.0.0",
    "net11.0",
    "[\"compile:ref/net11.0/Example.dll\"]",
    "[\"System.Runtime\"]",
  );
  const platformDocumentation =
    state.client.package.queryPlatformMemberDocumentation(
      "net11.0",
      "11.0.0",
      "System.Runtime.dll",
      "netcore.app",
      "M:System.String.Clone",
    );
  const classified = state.client.package.classifyPackageGraphIdentities(
    "Example.Root",
    ["Example.Root", "Other"],
  );
  const matched = state.client.package.matchPackageDependencyCoordinate(
    "Dependency",
    null,
    [{
      key: "candidate",
      provenance: "NuGetPackage",
      packageId: "Dependency",
      version: "1.0.0",
      targetFramework: "net11.0",
    }],
  );
  const pruning = state.client.package.queryPackagePruning(
    "Example",
    "1.0.0",
    "net10.0",
    {
      schemaVersion: 1,
      family: "netcoreapp",
      targetFramework: "net10.0",
      platformVersion: "10.0.0",
      supplies: [],
    },
  );
  const libraryDiff = state.client.metadata.queryLibraryApiDiff(
    "operation-1",
    "{\"schemaVersion\":1}",
  );
  const libraryDiffCancellation =
    state.client.metadata.cancelLibraryApiDiff(
      "operation-1",
      "superseded",
    );
  await state.environment.flushAsync();

  assert.deepEqual(await sync, searchResult);
  assert.deepEqual(await asyncDto, activation);
  assert.equal(await voidResult, undefined);
  assert.equal(await nullResult, null);
  assert.deepEqual((await libraryQuery).content.results, [{
    assetId: "compile:ref/net11.0/Example.dll",
    library: "Example",
    path: "ref/net11.0/Example.dll",
    source: "Example.Package",
    version: "1.0.0.0",
    sourceKind: "Package",
    targetFramework: "net11.0",
    matchedReferences: ["System.Runtime"],
  }]);
  assert.deepEqual(libraryQueryArguments, [
    "Example",
    "1.0.0",
    "net11.0",
    "[\"compile:ref/net11.0/Example.dll\"]",
    "[\"System.Runtime\"]",
  ]);
  assert.equal(await platformDocumentation, null);
  assert.deepEqual(platformDocumentationArguments, [
    "net11.0",
    "11.0.0",
    "System.Runtime.dll",
    "netcore.app",
    "M:System.String.Clone",
  ]);
  assert.deepEqual(await classified, ["Inspected", "External"]);
  assert.deepEqual(classificationArguments, [
    "Example.Root",
    ["Example.Root", "Other"],
  ]);
  assert.deepEqual(await matched, {
    outcome: "Unique",
    candidateKey: "candidate",
  });
  assert.deepEqual(matchArguments, [
    "Dependency",
    null,
    [{
      key: "candidate",
      provenance: "NuGetPackage",
      packageId: "Dependency",
      version: "1.0.0",
      targetFramework: "net11.0",
    }],
  ]);
  assert.equal((await pruning).completion, "Complete");
  assert.deepEqual(pruningArguments, [
    "Example",
    "1.0.0",
    "net10.0",
    {
      schemaVersion: 1,
      family: "netcoreapp",
      targetFramework: "net10.0",
      platformVersion: "10.0.0",
      supplies: [],
    },
  ]);
  assert.deepEqual(await libraryDiff, {
    schemaVersion: 1,
    kind: "Canceled",
    reason: "test",
  });
  assert.deepEqual(await libraryDiffCancellation, {
    kind: "Requested",
    reason: "superseded",
  });
  assert.deepEqual(libraryDiffArguments, [
    "operation-1",
    "{\"schemaVersion\":1}",
  ]);
  assert.deepEqual(libraryDiffCancelArguments, [
    "operation-1",
    "superseded",
  ]);
  assert.equal(cleared, 1);
  assert.deepEqual(state.diagnostics, []);
  state.host.dispose();
});

test("ordinary package transport preserves settled and NotSettled baselines", async () => {
  const surface = {
    package: "System.Text.Json",
    version: "8.0.5",
    frameworks: [],
    activeFramework: "",
    icon: null,
    defaultAssemblyId: null,
    compileLibrary: {
      status: "NoCompileAssets",
      targetFramework: null,
      message: null,
    },
    assemblies: [],
    types: [],
    accessibility: [],
    totalMembers: 0,
    documents: [],
    inspectionErrors: [],
    inspectionError: null,
  } satisfies BrowserPackageSurface;
  const settled = {
    versionSettlement: {
      content: {
        kind: "Settled",
        result: {
          request: {
            packageId: "system.text.json",
            version: null,
          },
          coordinate: {
            packageId: "system.text.json",
            version: "8.0.5",
          },
          includePrerelease: false,
          freshness: "RefreshedForRequest",
          listings: [{ version: "8.0.5", listed: true }],
          sourceListings: [{
            version: "8.0.5",
            feed: "nuget.org",
            listed: true,
          }],
        },
        failure: null,
      },
      share: {
        kind: "NonProjectable",
        fullUrl: null,
        packet: null,
        path: "package-version-settlement/share",
        reason: "No canonical Workspace share projection.",
      },
      diagnostics: [{
        code: "package-version-settlement.source-failure",
        severity: "Warning",
        summary: "A neighboring source was unavailable.",
        correspondence: null,
      }],
    },
    packageInfo: {
      content: {
        status: "Measured",
        packageId: "System.Text.Json",
        packageVersion: "8.0.5",
        compressedPackageBytes: 2048,
        selectedTargetFramework: "net10.0",
        availableTargetFrameworks: ["net10.0"],
        selectedTargetFrameworkFolders: ["lib"],
        selectedLibraryPayloadBytes: 1024,
        selectedLibraryCount: 1,
        detail: null,
        unavailableReason: null,
        hasSelectedSlice: true,
      },
      share: {
        kind: "NonProjectable",
        fullUrl: null,
        packet: null,
        path: "package-info-measurements/share",
        reason: "No canonical Workspace share projection.",
      },
      diagnostics: [],
    },
    surface,
  } satisfies BrowserPackageLoadResult;
  const notSettled = {
    versionSettlement: {
      content: {
        kind: "NotSettled",
        result: null,
        failure: {
          request: {
            packageId: "missing.package",
            version: null,
          },
          kind: "NotFound",
          reason: "No configured source contains the package.",
          operationTimedOut: false,
          authorityFailures: [{
            authority: "nuget.org",
            kind: "NotFound",
            message: "The package was not found.",
            timeoutKind: null,
          }],
        },
      },
      share: {
        kind: "NonProjectable",
        fullUrl: null,
        packet: null,
        path: "package-version-settlement/share",
        reason: "No canonical Workspace share projection.",
      },
      diagnostics: [],
    },
    packageInfo: null,
    surface: null,
  } satisfies BrowserPackageLoadResult;
  const state = fixture({
    package: {
      queryPackage: async packageId =>
        packageId === "Missing.Package"
          ? notSettled
          : settled,
    },
  });

  const settledResult = state.client.package.queryPackage(
    "System.Text.Json",
    "latest",
    "net10.0",
  );
  const notSettledResult = state.client.package.queryPackage(
    "Missing.Package",
    "latest",
    "net10.0",
  );
  await state.environment.flushAsync();

  assert.deepEqual(await settledResult, settled);
  assert.deepEqual(
    (await settledResult).versionSettlement,
    settled.versionSettlement);
  assert.deepEqual(await notSettledResult, notSettled);
  assert.equal((await notSettledResult).surface, null);
  assert.deepEqual(state.diagnostics, []);
  state.host.dispose();
});

test("ordinary source transport preserves member parts and flat graph source", async () => {
  const flat = {
    provider: "pdb",
    provenance: inertStringFixture("SourceLink"),
    url: "https://example.test/source.cs",
    pdbSourceLimitation: null,
    text: "public void M() { }",
  } satisfies BrowserSource;
  const member = {
    source: flat,
    parts: [{
      kind: "Member",
      spans: [{
        start: 0,
        length: flat.text.length,
        startLine: 1,
        endLine: 1,
        leadingIndentation: "",
        end: flat.text.length,
      }],
    }, {
      kind: "Body",
      spans: [{
        start: 16,
        length: 3,
        startLine: 1,
        endLine: 1,
        leadingIndentation: "",
        end: 19,
      }],
    }],
  } satisfies BrowserMemberSource;
  const state = fixture({
    source: {
      queryMemberSource: async () => member,
      queryTypeMemberSource: async () => flat,
    },
  });

  const memberResult = state.client.source.queryMemberSource(
    "Example",
    "1.0.0",
    "net11.0",
    "Example.dll",
    "Example.C",
    "M",
    "selector",
    0x06000001,
    "[]",
  );
  const graphResult = state.client.source.queryTypeMemberSource(
    "Example",
    "1.0.0",
    "net11.0",
    "Example.dll",
    "Example.C",
    "M",
    "selector",
    0x06000001,
    "[]",
  );
  await state.environment.flushAsync();

  assert.deepEqual(await memberResult, member);
  assert.deepEqual(await graphResult, flat);
  state.host.dispose();
});

test("Platform graph transport preserves retained context selection and ordinary null", async () => {
  const selections: (string | null)[] = [];
  const node = {
    label: "TryAddEnumerable", status: "Analyzed", inLoop: false,
    source: null, children: [], assembly: "Microsoft.Extensions.DependencyInjection.Abstractions",
    typeFullName: "ServiceCollectionDescriptorExtensions", memberName: "TryAddEnumerable",
  };
  const state = fixture({
    callGraph: {
      expandPlatformCallGraph: async (...args) => {
        selections.push(args[11]);
        return {
          mermaid: "graph TD",
          callers: node,
          callees: node,
          targets: [],
          scope: { packages: 0, assemblies: 3, callerAssemblies: 3, calleeScope: "Self" },
          diagnostics: {
            incompleteNodes: 0, incompleteEdges: 0, bindingIdentityConflicts: 0,
            hasUnexploredTraversalBoundary: false, hasAnalysisFailureBoundary: false,
            isIncomplete: false,
          },
          noBody: true,
        };
      },
    },
  });
  for (const contextId of ["retained-demo", null]) {
    const result = state.client.callGraph.expandPlatformCallGraph(
      "net10.0", "10.0.12", "Microsoft.Extensions.DependencyInjection.Abstractions",
      "aspnetcore.app", "10.0.0.0", null, null,
      "ServiceCollectionDescriptorExtensions", "TryAddEnumerable", "selector", 0,
      contextId);
    await state.environment.flushAsync();
    assert.equal((await result).scope.assemblies, 3);
  }
  assert.deepEqual(selections, ["retained-demo", null]);
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
        maxPackageEntries: 256,
        workspaces: 1,
        maxWorkspaces: 4,
        maxWorkspaceAssembliesPerRole: 256,
        residentBytes: 1024,
        maxResidentBytes: 134_217_728,
        maxWorkspaceRetainedImageBytes: 67_108_864,
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
    maxPackageEntries: 256,
    workspaces: 1,
    maxWorkspaces: 4,
    maxWorkspaceAssembliesPerRole: 256,
    residentBytes: 1024,
    maxResidentBytes: 134_217_728,
    maxWorkspaceRetainedImageBytes: 67_108_864,
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
        maxPackageEntries: 256,
        workspaces: 0,
        maxWorkspaces: 4,
        maxWorkspaceAssembliesPerRole: 256,
        residentBytes: 64,
        maxResidentBytes: 134_217_728,
        maxWorkspaceRetainedImageBytes: 67_108_864,
      }),
    },
  });
  const malformed = assert.rejects(
    state.client.package.queryPackageVersions("Example", "1.0.0"),
    /non-JSON undefined data/,
  );
  const oversized = assert.rejects(
    state.client.package.loadRuntimePack("net10.0", "10.0.0"),
    new RegExp(
      `exceeds ${engineWorkerOrdinaryMaximumJsonCharacters} characters`,
    ),
  );
  const neighbor = state.client.package.packageCacheStats();
  await state.environment.flushAsync();
  await Promise.all([malformed, oversized]);
  assert.equal((await neighbor).residentBytes, 64);
  assert.equal(state.host.snapshot().phase, "ready");
  assert.deepEqual(state.failures, []);
  state.host.dispose();
});

test("oversized prepared activation is cancelled before rejection", async () => {
  const cancellations: string[] = [];
  const state = fixture({
    catalog: {
      prepareRetainedWorkspaceDefinition: async () =>
        contractViolation({
          status: "prepared",
          receipt: "receipt-oversized",
          preparation: {
            label: "x".repeat(engineWorkerOrdinaryMaximumJsonCharacters),
          },
          posting: null,
          failure: null,
        }),
      cancelRetainedWorkspaceActivation: async receipt => {
        cancellations.push(receipt);
        return {
          status: "superseded",
          posting: null,
          failure: null,
        };
      },
    },
  });

  const preparation =
    state.client.catalog.prepareRetainedWorkspaceDefinition(
      "definition",
      "Definition",
      "/definition",
      "packet",
    );
  await state.environment.flushAsync();
  await assert.rejects(
    preparation,
    new RegExp(
      `exceeds ${engineWorkerOrdinaryMaximumJsonCharacters} characters`,
    ),
  );
  assert.deepEqual(cancellations, ["receipt-oversized"]);
  assert.equal(state.host.snapshot().phase, "ready");
  state.host.dispose();
});

test("retained Workspace Navigation lifecycle crosses ordinary transport", async () => {
  const calls: string[] = [];
  const state = fixture({
    catalog: {
      validateRetainedWorkspaceNavigationAuthority: () => {
        calls.push("validate");
        return true;
      },
      recordRetainedWorkspaceNavigationPosting: () => {
        calls.push("record");
        return "accepted";
      },
      acknowledgeRetainedWorkspaceNavigation: () => {
        calls.push("acknowledge");
        return "accepted";
      },
      abandonRetainedWorkspaceNavigation: () => {
        calls.push("abandon");
        return "accepted";
      },
    },
  });
  const authority = [
    "realization-1",
    1,
    "session-1",
    "revision-1",
    "intent-1",
    "epoch-1",
  ] as const;
  const client: RetainedWorkspaceActivationClient = state.client.catalog;

  const results = Promise.all([
    client.validateRetainedWorkspaceNavigationAuthority(
      ...authority,
    ),
    client.recordRetainedWorkspaceNavigationPosting(
      ...authority,
    ),
    client.acknowledgeRetainedWorkspaceNavigation(
      ...authority,
    ),
    client.abandonRetainedWorkspaceNavigation(
      ...authority,
    ),
  ]);
  await state.environment.flushAsync();

  assert.deepEqual(await results, [
    true,
    "accepted",
    "accepted",
    "accepted",
  ]);
  assert.deepEqual(calls, [
    "validate",
    "record",
    "acknowledge",
    "abandon",
  ]);
  assert.equal(state.host.snapshot().phase, "ready");
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
      contractViolation(
        "x".repeat(engineWorkerOrdinaryMaximumJsonCharacters),
      ),
    ),
    new RegExp(
      `exceeds ${engineWorkerOrdinaryMaximumJsonCharacters} characters`,
    ),
  );
  assert.equal(calls, 0);
  assert.equal(state.host.snapshot().activeOperations, 0);
  state.host.dispose();
});

test("large generated results cross the former ordinary transport bounds", async () => {
  const formerMaximumJsonCharacters = 8_388_608;
  const formerMaximumCollectionEntries = 262_144;
  const versions = Array.from(
    { length: formerMaximumCollectionEntries },
    (_unused, index) => index === 0
      ? "x".repeat(formerMaximumJsonCharacters)
      : "",
  );
  const state = fixture({
    package: {
      queryPackageVersions: async () => ({
        versions,
        currentVersionInsertionIndex: 0,
      }),
    },
  });

  const result = state.client.package.queryPackageVersions("Example", "1.0.0");
  await state.environment.flushAsync();
  assert.deepEqual((await result).versions, versions);
  assert.equal(state.host.snapshot().phase, "ready");
  assert.deepEqual(state.failures, []);
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
          maxPackageEntries: 256,
          workspaces: 0,
          maxWorkspaces: 4,
          maxWorkspaceAssembliesPerRole: 256,
          residentBytes: 0,
          maxResidentBytes: 134_217_728,
          maxWorkspaceRetainedImageBytes: 67_108_864,
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
    library: [
      "openUploadedLibrary",
    ],
    package: [
      "activateWorkspacePackageOccurrence",
      "classifyPackageGraphIdentities",
      "clearWorkspacePackageOccurrences",
      "getPackageDocument",
      "getPlatformCatalog",
      "getPlatformVersions",
      "loadRuntimePack",
      "loadRuntimePackAssembly",
      "matchPackageDependencyCoordinate",
      "packageCacheStats",
      "prefetchPlatformPacks",
      "queryLibraries",
      "queryLibraryApi",
      "queryMemberDocumentation",
      "queryPlatformMemberDocumentation",
      "queryPackage",
      "queryPackageDependencies",
      "queryPackagePruning",
      "queryPackageRoot",
      "queryPackageVersions",
      "queryWorkspacePackageOccurrences",
      "resolvePackageDependencyVersion",
      "searchTypes",
    ],
    metadata: [
      "cancelLibraryApiDiff",
      "queryLibraryApiDiff",
      "queryGraphMemberSurface",
      "queryMemberDeclaration",
      "queryPlatformMemberDeclaration",
      "queryPackageHeapEntries",
      "queryPackageMetadata",
      "queryPackageMetadataTable",
      "queryPlatformHeapEntries",
      "queryPlatformMetadata",
      "queryPlatformMetadataTable",
      "queryTypeProjection",
    ],
    analysis: [
      "queryCloneCandidates",
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
      "abandonRetainedWorkspaceNavigation",
      "acknowledgeRetainedWorkspaceNavigation",
      "admitRetainedWorkspacePackage",
      "admitRetainedWorkspacePlatform",
      "activateRetainedWorkspaceDefinition",
      "activateRetainedWorkspaceDefinitionWithCredentials",
      "cancelRetainedWorkspaceActivation",
      "canonicalizeWorkspaceSharePacket",
      "commitRetainedWorkspaceActivation",
      "completeRetainedWorkspaceActivation",
      "completeRetainedWorkspaceDeactivation",
      "deactivateRetainedWorkspaceDefinition",
      "describeWorkspacePackageSources",
      "decodeWorkspaceShareState",
      "encodeWorkspaceShareState",
      "observeRetainedWorkspaceSettlement",
      "prepareRetainedWorkspaceDefinition",
      "prepareRetainedWorkspaceDefinitionWithCredentials",
      "recordRetainedWorkspaceNavigationPosting",
      "resolveHomeDemo",
      "runHomeDemo",
      "validateRetainedWorkspaceNavigationAuthority",
    ],
  } as const;
  const expectedKinds = [
    ...Object.values(engineWorkerOrdinaryOperations.library),
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
  assert.equal(engineWorkerOrdinaryOperationKinds.length, 77);

  const state = fixture();
  const groups = [
    "library",
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
