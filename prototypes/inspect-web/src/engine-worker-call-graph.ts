import type { OperationDiagnostic } from "./operation-authority.ts";
import {
  createEngineWorkerOrdinaryBinder,
  defineEngineWorkerOrdinaryOperation,
  engineWorkerArguments11,
  engineWorkerNullableTextArgument,
  engineWorkerNumberArgument,
  engineWorkerOperationKinds,
  engineWorkerTextArgument,
  registerEngineWorkerOrdinaryOperation,
  type EngineWorkerAsyncFacade,
} from "./engine-worker-ordinary.ts";
import type { WorkerRuntimeHost } from "./worker-runtime-core.ts";
import type { WorkerOperationCatalog } from "./worker-runtime-realm.ts";

export type EngineWorkerCallGraphFacade = Pick<
  typeof import("./facades/inspect-web-call-graph.d.ts"),
  "expandPlatformCallGraph" | "queryMemberCallGraph"
>;

export type EngineWorkerCallGraphClient =
  EngineWorkerAsyncFacade<EngineWorkerCallGraphFacade>;

const T = engineWorkerTextArgument;
const NT = engineWorkerNullableTextArgument;
const N = engineWorkerNumberArgument;

const engineWorkerCallGraphOperations = {
  expandPlatformCallGraph:
    defineEngineWorkerOrdinaryOperation<
      EngineWorkerCallGraphFacade["expandPlatformCallGraph"]
    >(
      "call-graph-expand-platform",
      engineWorkerArguments11(T, T, T, T, T, NT, NT, T, T, T, N),
      "object",
    ),
  queryMemberCallGraph:
    defineEngineWorkerOrdinaryOperation<
      EngineWorkerCallGraphFacade["queryMemberCallGraph"]
    >(
      "call-graph-query-member",
      engineWorkerArguments11(T, T, T, T, T, T, T, T, T, N, T),
      "object",
    ),
};

export const engineWorkerCallGraphOperationKinds = engineWorkerOperationKinds(
    engineWorkerCallGraphOperations.expandPlatformCallGraph,
    engineWorkerCallGraphOperations.queryMemberCallGraph,
);

export function registerEngineWorkerCallGraphOperations(
  operations: WorkerOperationCatalog,
  facade: () => EngineWorkerCallGraphFacade,
): void {
  const descriptors = engineWorkerCallGraphOperations;
  registerEngineWorkerOrdinaryOperation(
    operations,
    descriptors.expandPlatformCallGraph,
    (...arguments_) => facade().expandPlatformCallGraph(...arguments_),
  );
  registerEngineWorkerOrdinaryOperation(
    operations,
    descriptors.queryMemberCallGraph,
    (...arguments_) => facade().queryMemberCallGraph(...arguments_),
  );
}

export function bindEngineWorkerCallGraphClient(
  host: WorkerRuntimeHost<string, string>,
  reportDiagnostic: (diagnostic: OperationDiagnostic) => undefined,
): EngineWorkerCallGraphClient {
  const binder = createEngineWorkerOrdinaryBinder(host, reportDiagnostic);
  const operations = engineWorkerCallGraphOperations;
  return {
    expandPlatformCallGraph:
      binder.bind(operations.expandPlatformCallGraph),
    queryMemberCallGraph:
      binder.bind(operations.queryMemberCallGraph),
  };
}
