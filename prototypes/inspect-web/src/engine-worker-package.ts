import type { OperationDiagnostic } from "./operation-authority.ts";
import {
  createEngineWorkerOrdinaryBinder,
  defineEngineWorkerOrdinaryOperation,
  engineWorkerArguments0,
  engineWorkerArguments1,
  engineWorkerArguments2,
  engineWorkerArguments3,
  engineWorkerArguments4,
  engineWorkerArguments5,
  engineWorkerNullableTextArgument,
  engineWorkerOperationKinds,
  engineWorkerTextArgument,
  registerEngineWorkerOrdinaryOperation,
  type EngineWorkerAsyncFacade,
} from "./engine-worker-ordinary.ts";
import type { WorkerRuntimeHost } from "./worker-runtime-core.ts";
import type { WorkerOperationCatalog } from "./worker-runtime-realm.ts";

export type EngineWorkerPackageFacade = Pick<
  typeof import("./facades/inspect-web-package.d.ts"),
  | "activateWorkspacePackageOccurrence"
  | "clearWorkspacePackageOccurrences"
  | "getPackageDocument"
  | "getPlatformCatalog"
  | "getPlatformVersions"
  | "listPackageAssemblyQueryPatterns"
  | "loadRuntimePack"
  | "loadRuntimePackAssembly"
  | "matchPackageDependencyCoordinate"
  | "openPackageAssemblyQueryResult"
  | "packageCacheStats"
  | "prefetchPlatformPacks"
  | "queryMemberDocumentation"
  | "queryPackage"
  | "queryPackageDependencies"
  | "queryPackageVersions"
  | "queryWorkspacePackageOccurrences"
  | "resolvePackageDependencyVersion"
  | "searchTypes"
>;

export type EngineWorkerPackageClient =
  EngineWorkerAsyncFacade<EngineWorkerPackageFacade>;

const T = engineWorkerTextArgument;
const NT = engineWorkerNullableTextArgument;

export const engineWorkerPackageOperations = {
  activateWorkspacePackageOccurrence:
    defineEngineWorkerOrdinaryOperation<
      EngineWorkerPackageFacade["activateWorkspacePackageOccurrence"]
    >(
      "package-activate-workspace-occurrence",
      engineWorkerArguments1(T),
      "object",
    ),
  clearWorkspacePackageOccurrences:
    defineEngineWorkerOrdinaryOperation<
      EngineWorkerPackageFacade["clearWorkspacePackageOccurrences"]
    >(
      "package-clear-workspace-occurrences",
      engineWorkerArguments0(),
      "void",
    ),
  getPackageDocument:
    defineEngineWorkerOrdinaryOperation<
      EngineWorkerPackageFacade["getPackageDocument"]
    >(
      "package-get-document",
      engineWorkerArguments3(T, T, T),
      "object",
    ),
  getPlatformCatalog:
    defineEngineWorkerOrdinaryOperation<
      EngineWorkerPackageFacade["getPlatformCatalog"]
    >(
      "package-get-platform-catalog",
      engineWorkerArguments2(T, T),
      "object",
    ),
  getPlatformVersions:
    defineEngineWorkerOrdinaryOperation<
      EngineWorkerPackageFacade["getPlatformVersions"]
    >(
      "package-get-platform-versions",
      engineWorkerArguments1(T),
      "array",
    ),
  listPackageAssemblyQueryPatterns:
    defineEngineWorkerOrdinaryOperation<
      EngineWorkerPackageFacade["listPackageAssemblyQueryPatterns"]
    >(
      "package-list-assembly-query-patterns",
      engineWorkerArguments0(),
      "array",
    ),
  loadRuntimePack:
    defineEngineWorkerOrdinaryOperation<
      EngineWorkerPackageFacade["loadRuntimePack"]
    >(
      "package-load-runtime-pack",
      engineWorkerArguments2(T, T),
      "string",
    ),
  loadRuntimePackAssembly:
    defineEngineWorkerOrdinaryOperation<
      EngineWorkerPackageFacade["loadRuntimePackAssembly"]
    >(
      "package-load-runtime-pack-assembly",
      engineWorkerArguments5(T, T, T, T, T),
      "string",
    ),
  matchPackageDependencyCoordinate:
    defineEngineWorkerOrdinaryOperation<
      EngineWorkerPackageFacade["matchPackageDependencyCoordinate"]
    >(
      "package-match-dependency-coordinate",
      engineWorkerArguments3(T, NT, T),
      "object",
    ),
  openPackageAssemblyQueryResult:
    defineEngineWorkerOrdinaryOperation<
      EngineWorkerPackageFacade["openPackageAssemblyQueryResult"]
    >(
      "package-open-assembly-query-result",
      engineWorkerArguments1(T),
      "object",
    ),
  packageCacheStats:
    defineEngineWorkerOrdinaryOperation<
      EngineWorkerPackageFacade["packageCacheStats"]
    >(
      "package-cache-stats",
      engineWorkerArguments0(),
      "object",
    ),
  prefetchPlatformPacks:
    defineEngineWorkerOrdinaryOperation<
      EngineWorkerPackageFacade["prefetchPlatformPacks"]
    >(
      "package-prefetch-platform-packs",
      engineWorkerArguments2(T, T),
      "void",
    ),
  queryMemberDocumentation:
    defineEngineWorkerOrdinaryOperation<
      EngineWorkerPackageFacade["queryMemberDocumentation"]
    >(
      "package-query-member-documentation",
      engineWorkerArguments5(T, T, T, T, T),
      "object",
    ),
  queryPackage:
    defineEngineWorkerOrdinaryOperation<
      EngineWorkerPackageFacade["queryPackage"]
    >(
      "package-query-package",
      engineWorkerArguments3(T, T, T),
      "object",
    ),
  queryPackageDependencies:
    defineEngineWorkerOrdinaryOperation<
      EngineWorkerPackageFacade["queryPackageDependencies"]
    >(
      "package-query-dependencies",
      engineWorkerArguments4(T, T, T, T),
      "object",
    ),
  queryPackageVersions:
    defineEngineWorkerOrdinaryOperation<
      EngineWorkerPackageFacade["queryPackageVersions"]
    >(
      "package-query-versions",
      engineWorkerArguments2(T, T),
      "object",
    ),
  queryWorkspacePackageOccurrences:
    defineEngineWorkerOrdinaryOperation<
      EngineWorkerPackageFacade["queryWorkspacePackageOccurrences"]
    >(
      "package-query-workspace-occurrences",
      engineWorkerArguments1(T),
      "object",
    ),
  resolvePackageDependencyVersion:
    defineEngineWorkerOrdinaryOperation<
      EngineWorkerPackageFacade["resolvePackageDependencyVersion"]
    >(
      "package-resolve-dependency-version",
      engineWorkerArguments2(T, NT),
      "string",
    ),
  searchTypes:
    defineEngineWorkerOrdinaryOperation<
      EngineWorkerPackageFacade["searchTypes"]
    >(
      "package-search-types",
      engineWorkerArguments2(T, T),
      "array",
    ),
};

export const engineWorkerPackageOperationKinds = engineWorkerOperationKinds(
    engineWorkerPackageOperations.activateWorkspacePackageOccurrence,
    engineWorkerPackageOperations.clearWorkspacePackageOccurrences,
    engineWorkerPackageOperations.getPackageDocument,
    engineWorkerPackageOperations.getPlatformCatalog,
    engineWorkerPackageOperations.getPlatformVersions,
    engineWorkerPackageOperations.listPackageAssemblyQueryPatterns,
    engineWorkerPackageOperations.loadRuntimePack,
    engineWorkerPackageOperations.loadRuntimePackAssembly,
    engineWorkerPackageOperations.matchPackageDependencyCoordinate,
    engineWorkerPackageOperations.openPackageAssemblyQueryResult,
    engineWorkerPackageOperations.packageCacheStats,
    engineWorkerPackageOperations.prefetchPlatformPacks,
    engineWorkerPackageOperations.queryMemberDocumentation,
    engineWorkerPackageOperations.queryPackage,
    engineWorkerPackageOperations.queryPackageDependencies,
    engineWorkerPackageOperations.queryPackageVersions,
    engineWorkerPackageOperations.queryWorkspacePackageOccurrences,
    engineWorkerPackageOperations.resolvePackageDependencyVersion,
    engineWorkerPackageOperations.searchTypes,
);

export function registerEngineWorkerPackageOperations(
  operations: WorkerOperationCatalog,
  facade: () => EngineWorkerPackageFacade,
): void {
  const descriptors = engineWorkerPackageOperations;
  registerEngineWorkerOrdinaryOperation(
    operations,
    descriptors.activateWorkspacePackageOccurrence,
    (...arguments_) =>
      facade().activateWorkspacePackageOccurrence(...arguments_),
  );
  registerEngineWorkerOrdinaryOperation(
    operations,
    descriptors.clearWorkspacePackageOccurrences,
    (...arguments_) => facade().clearWorkspacePackageOccurrences(...arguments_),
  );
  registerEngineWorkerOrdinaryOperation(
    operations,
    descriptors.getPackageDocument,
    (...arguments_) => facade().getPackageDocument(...arguments_),
  );
  registerEngineWorkerOrdinaryOperation(
    operations,
    descriptors.getPlatformCatalog,
    (...arguments_) => facade().getPlatformCatalog(...arguments_),
  );
  registerEngineWorkerOrdinaryOperation(
    operations,
    descriptors.getPlatformVersions,
    (...arguments_) => facade().getPlatformVersions(...arguments_),
  );
  registerEngineWorkerOrdinaryOperation(
    operations,
    descriptors.listPackageAssemblyQueryPatterns,
    (...arguments_) =>
      facade().listPackageAssemblyQueryPatterns(...arguments_),
  );
  registerEngineWorkerOrdinaryOperation(
    operations,
    descriptors.loadRuntimePack,
    (...arguments_) => facade().loadRuntimePack(...arguments_),
  );
  registerEngineWorkerOrdinaryOperation(
    operations,
    descriptors.loadRuntimePackAssembly,
    (...arguments_) => facade().loadRuntimePackAssembly(...arguments_),
  );
  registerEngineWorkerOrdinaryOperation(
    operations,
    descriptors.matchPackageDependencyCoordinate,
    (...arguments_) =>
      facade().matchPackageDependencyCoordinate(...arguments_),
  );
  registerEngineWorkerOrdinaryOperation(
    operations,
    descriptors.openPackageAssemblyQueryResult,
    (...arguments_) =>
      facade().openPackageAssemblyQueryResult(...arguments_),
  );
  registerEngineWorkerOrdinaryOperation(
    operations,
    descriptors.packageCacheStats,
    (...arguments_) => facade().packageCacheStats(...arguments_),
  );
  registerEngineWorkerOrdinaryOperation(
    operations,
    descriptors.prefetchPlatformPacks,
    (...arguments_) => facade().prefetchPlatformPacks(...arguments_),
  );
  registerEngineWorkerOrdinaryOperation(
    operations,
    descriptors.queryMemberDocumentation,
    (...arguments_) => facade().queryMemberDocumentation(...arguments_),
  );
  registerEngineWorkerOrdinaryOperation(
    operations,
    descriptors.queryPackage,
    (...arguments_) => facade().queryPackage(...arguments_),
  );
  registerEngineWorkerOrdinaryOperation(
    operations,
    descriptors.queryPackageDependencies,
    (...arguments_) => facade().queryPackageDependencies(...arguments_),
  );
  registerEngineWorkerOrdinaryOperation(
    operations,
    descriptors.queryPackageVersions,
    (...arguments_) => facade().queryPackageVersions(...arguments_),
  );
  registerEngineWorkerOrdinaryOperation(
    operations,
    descriptors.queryWorkspacePackageOccurrences,
    (...arguments_) =>
      facade().queryWorkspacePackageOccurrences(...arguments_),
  );
  registerEngineWorkerOrdinaryOperation(
    operations,
    descriptors.resolvePackageDependencyVersion,
    (...arguments_) =>
      facade().resolvePackageDependencyVersion(...arguments_),
  );
  registerEngineWorkerOrdinaryOperation(
    operations,
    descriptors.searchTypes,
    (...arguments_) => facade().searchTypes(...arguments_),
  );
}

export function bindEngineWorkerPackageClient(
  host: WorkerRuntimeHost<string, string>,
  reportDiagnostic: (diagnostic: OperationDiagnostic) => undefined,
): EngineWorkerPackageClient {
  const binder = createEngineWorkerOrdinaryBinder(host, reportDiagnostic);
  const operations = engineWorkerPackageOperations;
  return {
    activateWorkspacePackageOccurrence:
      binder.bind(operations.activateWorkspacePackageOccurrence),
    clearWorkspacePackageOccurrences:
      binder.bind(operations.clearWorkspacePackageOccurrences),
    getPackageDocument: binder.bind(operations.getPackageDocument),
    getPlatformCatalog: binder.bind(operations.getPlatformCatalog),
    getPlatformVersions: binder.bind(operations.getPlatformVersions),
    listPackageAssemblyQueryPatterns:
      binder.bind(operations.listPackageAssemblyQueryPatterns),
    loadRuntimePack: binder.bind(operations.loadRuntimePack),
    loadRuntimePackAssembly:
      binder.bind(operations.loadRuntimePackAssembly),
    matchPackageDependencyCoordinate:
      binder.bind(operations.matchPackageDependencyCoordinate),
    openPackageAssemblyQueryResult:
      binder.bind(operations.openPackageAssemblyQueryResult),
    packageCacheStats: binder.bind(operations.packageCacheStats),
    prefetchPlatformPacks: binder.bind(operations.prefetchPlatformPacks),
    queryMemberDocumentation:
      binder.bind(operations.queryMemberDocumentation),
    queryPackage: binder.bind(operations.queryPackage),
    queryPackageDependencies:
      binder.bind(operations.queryPackageDependencies),
    queryPackageVersions: binder.bind(operations.queryPackageVersions),
    queryWorkspacePackageOccurrences:
      binder.bind(operations.queryWorkspacePackageOccurrences),
    resolvePackageDependencyVersion:
      binder.bind(operations.resolvePackageDependencyVersion),
    searchTypes: binder.bind(operations.searchTypes),
  };
}
