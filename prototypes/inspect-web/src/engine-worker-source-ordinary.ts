import type { OperationDiagnostic } from "./operation-authority.ts";
import {
  createEngineWorkerOrdinaryBinder,
  defineEngineWorkerOrdinaryOperation,
  engineWorkerArguments0,
  engineWorkerArguments9,
  engineWorkerArguments11,
  engineWorkerNumberArgument,
  engineWorkerOperationKinds,
  engineWorkerTextArgument,
  registerEngineWorkerOrdinaryOperation,
  type EngineWorkerAsyncFacade,
} from "./engine-worker-ordinary.ts";
import type { WorkerRuntimeHost } from "./worker-runtime-core.ts";
import type { WorkerOperationCatalog } from "./worker-runtime-realm.ts";

export type EngineWorkerOrdinarySourceFacade = Pick<
  typeof import("./facades/inspect-web-source.d.ts"),
  | "cancelSourceQuery"
  | "queryMemberFindingCensus"
  | "queryMemberSource"
  | "queryTypeMemberSource"
>;

export type EngineWorkerOrdinarySourceClient =
  EngineWorkerAsyncFacade<EngineWorkerOrdinarySourceFacade>;

const T = engineWorkerTextArgument;
const N = engineWorkerNumberArgument;

const engineWorkerOrdinarySourceOperations = {
  cancelSourceQuery:
    defineEngineWorkerOrdinaryOperation<
      EngineWorkerOrdinarySourceFacade["cancelSourceQuery"]
    >(
      "source-cancel-query",
      engineWorkerArguments0(),
      "void",
    ),
  queryMemberFindingCensus:
    defineEngineWorkerOrdinaryOperation<
      EngineWorkerOrdinarySourceFacade["queryMemberFindingCensus"]
    >(
      "source-query-member-finding-census",
      engineWorkerArguments11(T, T, T, T, T, T, T, T, T, N, T),
      "object",
    ),
  queryMemberSource:
    defineEngineWorkerOrdinaryOperation<
      EngineWorkerOrdinarySourceFacade["queryMemberSource"]
    >(
      "source-query-member",
      engineWorkerArguments9(T, T, T, T, T, T, T, N, T),
      "object",
    ),
  queryTypeMemberSource:
    defineEngineWorkerOrdinaryOperation<
      EngineWorkerOrdinarySourceFacade["queryTypeMemberSource"]
    >(
      "source-query-type-member",
      engineWorkerArguments9(T, T, T, T, T, T, T, N, T),
      "object",
    ),
};

export const engineWorkerOrdinarySourceOperationKinds =
    engineWorkerOperationKinds(
      engineWorkerOrdinarySourceOperations.cancelSourceQuery,
      engineWorkerOrdinarySourceOperations.queryMemberFindingCensus,
      engineWorkerOrdinarySourceOperations.queryMemberSource,
      engineWorkerOrdinarySourceOperations.queryTypeMemberSource,
    );

export function registerEngineWorkerOrdinarySourceOperations(
  operations: WorkerOperationCatalog,
  facade: () => EngineWorkerOrdinarySourceFacade,
): void {
  const descriptors = engineWorkerOrdinarySourceOperations;
  registerEngineWorkerOrdinaryOperation(
    operations,
    descriptors.cancelSourceQuery,
    (...arguments_) => facade().cancelSourceQuery(...arguments_),
  );
  registerEngineWorkerOrdinaryOperation(
    operations,
    descriptors.queryMemberFindingCensus,
    (...arguments_) => facade().queryMemberFindingCensus(...arguments_),
  );
  registerEngineWorkerOrdinaryOperation(
    operations,
    descriptors.queryMemberSource,
    (...arguments_) => facade().queryMemberSource(...arguments_),
  );
  registerEngineWorkerOrdinaryOperation(
    operations,
    descriptors.queryTypeMemberSource,
    (...arguments_) => facade().queryTypeMemberSource(...arguments_),
  );
}

export function bindEngineWorkerOrdinarySourceClient(
  host: WorkerRuntimeHost<string, string>,
  reportDiagnostic: (diagnostic: OperationDiagnostic) => undefined,
): EngineWorkerOrdinarySourceClient {
  const binder = createEngineWorkerOrdinaryBinder(host, reportDiagnostic);
  const operations = engineWorkerOrdinarySourceOperations;
  return {
    cancelSourceQuery: binder.bind(operations.cancelSourceQuery),
    queryMemberFindingCensus:
      binder.bind(operations.queryMemberFindingCensus),
    queryMemberSource: binder.bind(operations.queryMemberSource),
    queryTypeMemberSource: binder.bind(operations.queryTypeMemberSource),
  };
}
