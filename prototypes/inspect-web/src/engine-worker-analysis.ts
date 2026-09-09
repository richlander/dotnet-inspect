import type { OperationDiagnostic } from "./operation-authority.ts";
import {
  createEngineWorkerOrdinaryBinder,
  defineEngineWorkerOrdinaryOperation,
  engineWorkerArguments4,
  engineWorkerArguments10,
  engineWorkerBooleanArgument,
  engineWorkerNumberArgument,
  engineWorkerOperationKinds,
  engineWorkerTextArgument,
  registerEngineWorkerOrdinaryOperation,
  type EngineWorkerAsyncFacade,
} from "./engine-worker-ordinary.ts";
import type { WorkerRuntimeHost } from "./worker-runtime-core.ts";
import type { WorkerOperationCatalog } from "./worker-runtime-realm.ts";

export type EngineWorkerAnalysisFacade = Pick<
  typeof import("./facades/inspect-web-analysis.d.ts"),
  | "queryMemberFacts"
  | "queryPackageIntegrations"
  | "queryPackageOpportunities"
  | "queryPackagePerformance"
  | "queryPlatformIntegrations"
  | "queryPlatformOpportunities"
  | "queryPlatformPerformance"
>;

export type EngineWorkerAnalysisClient =
  EngineWorkerAsyncFacade<EngineWorkerAnalysisFacade>;

const T = engineWorkerTextArgument;
const N = engineWorkerNumberArgument;
const B = engineWorkerBooleanArgument;

const engineWorkerAnalysisOperations = {
  queryMemberFacts:
    defineEngineWorkerOrdinaryOperation<
      EngineWorkerAnalysisFacade["queryMemberFacts"]
    >(
      "analysis-query-member-facts",
      engineWorkerArguments10(T, T, T, T, T, T, T, T, N, B),
      "object",
    ),
  queryPackageIntegrations:
    defineEngineWorkerOrdinaryOperation<
      EngineWorkerAnalysisFacade["queryPackageIntegrations"]
    >(
      "analysis-query-package-integrations",
      engineWorkerArguments4(T, T, T, T),
      "object",
    ),
  queryPackageOpportunities:
    defineEngineWorkerOrdinaryOperation<
      EngineWorkerAnalysisFacade["queryPackageOpportunities"]
    >(
      "analysis-query-package-opportunities",
      engineWorkerArguments4(T, T, T, T),
      "object",
    ),
  queryPackagePerformance:
    defineEngineWorkerOrdinaryOperation<
      EngineWorkerAnalysisFacade["queryPackagePerformance"]
    >(
      "analysis-query-package-performance",
      engineWorkerArguments4(T, T, T, T),
      "object",
    ),
  queryPlatformIntegrations:
    defineEngineWorkerOrdinaryOperation<
      EngineWorkerAnalysisFacade["queryPlatformIntegrations"]
    >(
      "analysis-query-platform-integrations",
      engineWorkerArguments4(T, T, T, T),
      "object",
    ),
  queryPlatformOpportunities:
    defineEngineWorkerOrdinaryOperation<
      EngineWorkerAnalysisFacade["queryPlatformOpportunities"]
    >(
      "analysis-query-platform-opportunities",
      engineWorkerArguments4(T, T, T, T),
      "object",
    ),
  queryPlatformPerformance:
    defineEngineWorkerOrdinaryOperation<
      EngineWorkerAnalysisFacade["queryPlatformPerformance"]
    >(
      "analysis-query-platform-performance",
      engineWorkerArguments4(T, T, T, T),
      "string",
    ),
};

export const engineWorkerAnalysisOperationKinds = engineWorkerOperationKinds(
    engineWorkerAnalysisOperations.queryMemberFacts,
    engineWorkerAnalysisOperations.queryPackageIntegrations,
    engineWorkerAnalysisOperations.queryPackageOpportunities,
    engineWorkerAnalysisOperations.queryPackagePerformance,
    engineWorkerAnalysisOperations.queryPlatformIntegrations,
    engineWorkerAnalysisOperations.queryPlatformOpportunities,
    engineWorkerAnalysisOperations.queryPlatformPerformance,
);

export function registerEngineWorkerAnalysisOperations(
  operations: WorkerOperationCatalog,
  facade: () => EngineWorkerAnalysisFacade,
): void {
  const descriptors = engineWorkerAnalysisOperations;
  registerEngineWorkerOrdinaryOperation(
    operations,
    descriptors.queryMemberFacts,
    (...arguments_) => facade().queryMemberFacts(...arguments_),
  );
  registerEngineWorkerOrdinaryOperation(
    operations,
    descriptors.queryPackageIntegrations,
    (...arguments_) => facade().queryPackageIntegrations(...arguments_),
  );
  registerEngineWorkerOrdinaryOperation(
    operations,
    descriptors.queryPackageOpportunities,
    (...arguments_) => facade().queryPackageOpportunities(...arguments_),
  );
  registerEngineWorkerOrdinaryOperation(
    operations,
    descriptors.queryPackagePerformance,
    (...arguments_) => facade().queryPackagePerformance(...arguments_),
  );
  registerEngineWorkerOrdinaryOperation(
    operations,
    descriptors.queryPlatformIntegrations,
    (...arguments_) => facade().queryPlatformIntegrations(...arguments_),
  );
  registerEngineWorkerOrdinaryOperation(
    operations,
    descriptors.queryPlatformOpportunities,
    (...arguments_) => facade().queryPlatformOpportunities(...arguments_),
  );
  registerEngineWorkerOrdinaryOperation(
    operations,
    descriptors.queryPlatformPerformance,
    (...arguments_) => facade().queryPlatformPerformance(...arguments_),
  );
}

export function bindEngineWorkerAnalysisClient(
  host: WorkerRuntimeHost<string, string>,
  reportDiagnostic: (diagnostic: OperationDiagnostic) => undefined,
): EngineWorkerAnalysisClient {
  const binder = createEngineWorkerOrdinaryBinder(host, reportDiagnostic);
  const operations = engineWorkerAnalysisOperations;
  return {
    queryMemberFacts: binder.bind(operations.queryMemberFacts),
    queryPackageIntegrations:
      binder.bind(operations.queryPackageIntegrations),
    queryPackageOpportunities:
      binder.bind(operations.queryPackageOpportunities),
    queryPackagePerformance:
      binder.bind(operations.queryPackagePerformance),
    queryPlatformIntegrations:
      binder.bind(operations.queryPlatformIntegrations),
    queryPlatformOpportunities:
      binder.bind(operations.queryPlatformOpportunities),
    queryPlatformPerformance:
      binder.bind(operations.queryPlatformPerformance),
  };
}
