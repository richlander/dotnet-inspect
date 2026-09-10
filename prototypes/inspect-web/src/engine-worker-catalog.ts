import type { OperationDiagnostic } from "./operation-authority.ts";
import {
  createEngineWorkerOrdinaryBinder,
  defineEngineWorkerOrdinaryOperation,
  engineWorkerArguments1,
  engineWorkerOperationKinds,
  engineWorkerTextArgument,
  registerEngineWorkerOrdinaryOperation,
  type EngineWorkerAsyncFacade,
} from "./engine-worker-ordinary.ts";
import type { WorkerRuntimeHost } from "./worker-runtime-core.ts";
import type { WorkerOperationCatalog } from "./worker-runtime-realm.ts";

export type EngineWorkerCatalogFacade = Pick<
  typeof import("./facades/inspect-web-catalog.d.ts"),
  | "decodeWorkspaceShareState"
  | "encodeWorkspaceShareState"
  | "resolveHomeDemo"
  | "runHomeDemo"
>;

export type EngineWorkerCatalogClient =
  EngineWorkerAsyncFacade<EngineWorkerCatalogFacade>;

const T = engineWorkerTextArgument;

const engineWorkerCatalogOperations = {
  decodeWorkspaceShareState:
    defineEngineWorkerOrdinaryOperation<
      EngineWorkerCatalogFacade["decodeWorkspaceShareState"]
    >(
      "catalog-decode-workspace-share-state",
      engineWorkerArguments1(T),
      "object",
    ),
  encodeWorkspaceShareState:
    defineEngineWorkerOrdinaryOperation<
      EngineWorkerCatalogFacade["encodeWorkspaceShareState"]
    >(
      "catalog-encode-workspace-share-state",
      engineWorkerArguments1(T),
      "object",
    ),
  resolveHomeDemo:
    defineEngineWorkerOrdinaryOperation<
      EngineWorkerCatalogFacade["resolveHomeDemo"]
    >(
      "catalog-resolve-home-demo",
      engineWorkerArguments1(T),
      "object",
    ),
  runHomeDemo:
    defineEngineWorkerOrdinaryOperation<
      EngineWorkerCatalogFacade["runHomeDemo"]
    >(
      "catalog-run-home-demo",
      engineWorkerArguments1(T),
      "object",
    ),
};

export const engineWorkerCatalogOperationKinds = engineWorkerOperationKinds(
    engineWorkerCatalogOperations.decodeWorkspaceShareState,
    engineWorkerCatalogOperations.encodeWorkspaceShareState,
    engineWorkerCatalogOperations.resolveHomeDemo,
    engineWorkerCatalogOperations.runHomeDemo,
);

export function registerEngineWorkerCatalogOperations(
  operations: WorkerOperationCatalog,
  facade: () => EngineWorkerCatalogFacade,
): void {
  const descriptors = engineWorkerCatalogOperations;
  registerEngineWorkerOrdinaryOperation(
    operations,
    descriptors.decodeWorkspaceShareState,
    (...arguments_) => facade().decodeWorkspaceShareState(...arguments_),
  );
  registerEngineWorkerOrdinaryOperation(
    operations,
    descriptors.encodeWorkspaceShareState,
    (...arguments_) => facade().encodeWorkspaceShareState(...arguments_),
  );
  registerEngineWorkerOrdinaryOperation(
    operations,
    descriptors.resolveHomeDemo,
    (...arguments_) => facade().resolveHomeDemo(...arguments_),
  );
  registerEngineWorkerOrdinaryOperation(
    operations,
    descriptors.runHomeDemo,
    (...arguments_) => facade().runHomeDemo(...arguments_),
  );
}

export function bindEngineWorkerCatalogClient(
  host: WorkerRuntimeHost<string, string>,
  reportDiagnostic: (diagnostic: OperationDiagnostic) => undefined,
): EngineWorkerCatalogClient {
  const binder = createEngineWorkerOrdinaryBinder(host, reportDiagnostic);
  const operations = engineWorkerCatalogOperations;
  return {
    decodeWorkspaceShareState:
      binder.bind(operations.decodeWorkspaceShareState),
    encodeWorkspaceShareState:
      binder.bind(operations.encodeWorkspaceShareState),
    resolveHomeDemo: binder.bind(operations.resolveHomeDemo),
    runHomeDemo: binder.bind(operations.runHomeDemo),
  };
}
