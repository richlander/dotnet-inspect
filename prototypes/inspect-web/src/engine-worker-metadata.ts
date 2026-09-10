import type { OperationDiagnostic } from "./operation-authority.ts";
import {
  createEngineWorkerOrdinaryBinder,
  defineEngineWorkerOrdinaryOperation,
  engineWorkerArguments4,
  engineWorkerArguments5,
  engineWorkerArguments6,
  engineWorkerArguments8,
  engineWorkerNumberArgument,
  engineWorkerOperationKinds,
  engineWorkerTextArgument,
  registerEngineWorkerOrdinaryOperation,
  type EngineWorkerAsyncFacade,
} from "./engine-worker-ordinary.ts";
import type { WorkerRuntimeHost } from "./worker-runtime-core.ts";
import type { WorkerOperationCatalog } from "./worker-runtime-realm.ts";

export type EngineWorkerMetadataFacade = Pick<
  typeof import("./facades/inspect-web-metadata.d.ts"),
  | "queryGraphMemberSurface"
  | "queryPackageHeapEntries"
  | "queryPackageMetadata"
  | "queryPackageMetadataTable"
  | "queryPlatformHeapEntries"
  | "queryPlatformMetadata"
  | "queryPlatformMetadataTable"
  | "queryTypeProjection"
>;

export type EngineWorkerMetadataClient =
  EngineWorkerAsyncFacade<EngineWorkerMetadataFacade>;

const T = engineWorkerTextArgument;
const N = engineWorkerNumberArgument;

const engineWorkerMetadataOperations = {
  queryGraphMemberSurface:
    defineEngineWorkerOrdinaryOperation<
      EngineWorkerMetadataFacade["queryGraphMemberSurface"]
    >(
      "metadata-query-graph-member-surface",
      engineWorkerArguments8(T, T, T, T, T, T, T, N),
      "object",
    ),
  queryPackageHeapEntries:
    defineEngineWorkerOrdinaryOperation<
      EngineWorkerMetadataFacade["queryPackageHeapEntries"]
    >(
      "metadata-query-package-heap-entries",
      engineWorkerArguments6(T, T, T, T, T, T),
      "object",
    ),
  queryPackageMetadata:
    defineEngineWorkerOrdinaryOperation<
      EngineWorkerMetadataFacade["queryPackageMetadata"]
    >(
      "metadata-query-package",
      engineWorkerArguments4(T, T, T, T),
      "object",
    ),
  queryPackageMetadataTable:
    defineEngineWorkerOrdinaryOperation<
      EngineWorkerMetadataFacade["queryPackageMetadataTable"]
    >(
      "metadata-query-package-table",
      engineWorkerArguments8(T, T, T, T, T, N, N, N),
      "object",
    ),
  queryPlatformHeapEntries:
    defineEngineWorkerOrdinaryOperation<
      EngineWorkerMetadataFacade["queryPlatformHeapEntries"]
    >(
      "metadata-query-platform-heap-entries",
      engineWorkerArguments6(T, T, T, T, T, T),
      "object",
    ),
  queryPlatformMetadata:
    defineEngineWorkerOrdinaryOperation<
      EngineWorkerMetadataFacade["queryPlatformMetadata"]
    >(
      "metadata-query-platform",
      engineWorkerArguments4(T, T, T, T),
      "object",
    ),
  queryPlatformMetadataTable:
    defineEngineWorkerOrdinaryOperation<
      EngineWorkerMetadataFacade["queryPlatformMetadataTable"]
    >(
      "metadata-query-platform-table",
      engineWorkerArguments8(T, T, T, T, T, N, N, N),
      "object",
    ),
  queryTypeProjection:
    defineEngineWorkerOrdinaryOperation<
      EngineWorkerMetadataFacade["queryTypeProjection"]
    >(
      "metadata-query-type-projection",
      engineWorkerArguments5(T, T, T, T, T),
      "object",
    ),
};

export const engineWorkerMetadataOperationKinds = engineWorkerOperationKinds(
    engineWorkerMetadataOperations.queryGraphMemberSurface,
    engineWorkerMetadataOperations.queryPackageHeapEntries,
    engineWorkerMetadataOperations.queryPackageMetadata,
    engineWorkerMetadataOperations.queryPackageMetadataTable,
    engineWorkerMetadataOperations.queryPlatformHeapEntries,
    engineWorkerMetadataOperations.queryPlatformMetadata,
    engineWorkerMetadataOperations.queryPlatformMetadataTable,
    engineWorkerMetadataOperations.queryTypeProjection,
);

export function registerEngineWorkerMetadataOperations(
  operations: WorkerOperationCatalog,
  facade: () => EngineWorkerMetadataFacade,
): void {
  const descriptors = engineWorkerMetadataOperations;
  registerEngineWorkerOrdinaryOperation(
    operations,
    descriptors.queryGraphMemberSurface,
    (...arguments_) => facade().queryGraphMemberSurface(...arguments_),
  );
  registerEngineWorkerOrdinaryOperation(
    operations,
    descriptors.queryPackageHeapEntries,
    (...arguments_) => facade().queryPackageHeapEntries(...arguments_),
  );
  registerEngineWorkerOrdinaryOperation(
    operations,
    descriptors.queryPackageMetadata,
    (...arguments_) => facade().queryPackageMetadata(...arguments_),
  );
  registerEngineWorkerOrdinaryOperation(
    operations,
    descriptors.queryPackageMetadataTable,
    (...arguments_) => facade().queryPackageMetadataTable(...arguments_),
  );
  registerEngineWorkerOrdinaryOperation(
    operations,
    descriptors.queryPlatformHeapEntries,
    (...arguments_) => facade().queryPlatformHeapEntries(...arguments_),
  );
  registerEngineWorkerOrdinaryOperation(
    operations,
    descriptors.queryPlatformMetadata,
    (...arguments_) => facade().queryPlatformMetadata(...arguments_),
  );
  registerEngineWorkerOrdinaryOperation(
    operations,
    descriptors.queryPlatformMetadataTable,
    (...arguments_) => facade().queryPlatformMetadataTable(...arguments_),
  );
  registerEngineWorkerOrdinaryOperation(
    operations,
    descriptors.queryTypeProjection,
    (...arguments_) => facade().queryTypeProjection(...arguments_),
  );
}

export function bindEngineWorkerMetadataClient(
  host: WorkerRuntimeHost<string, string>,
  reportDiagnostic: (diagnostic: OperationDiagnostic) => undefined,
): EngineWorkerMetadataClient {
  const binder = createEngineWorkerOrdinaryBinder(host, reportDiagnostic);
  const operations = engineWorkerMetadataOperations;
  return {
    queryGraphMemberSurface:
      binder.bind(operations.queryGraphMemberSurface),
    queryPackageHeapEntries:
      binder.bind(operations.queryPackageHeapEntries),
    queryPackageMetadata: binder.bind(operations.queryPackageMetadata),
    queryPackageMetadataTable:
      binder.bind(operations.queryPackageMetadataTable),
    queryPlatformHeapEntries:
      binder.bind(operations.queryPlatformHeapEntries),
    queryPlatformMetadata:
      binder.bind(operations.queryPlatformMetadata),
    queryPlatformMetadataTable:
      binder.bind(operations.queryPlatformMetadataTable),
    queryTypeProjection: binder.bind(operations.queryTypeProjection),
  };
}
