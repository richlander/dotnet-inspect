import type { EngineClient } from "./engine-client.ts";

type BenchmarkHost = Pick<EngineClient["host"], "buildIdentity">;
type BenchmarkPackage = Pick<
  EngineClient["package"],
  "queryPackage" | "loadRuntimePack"
>;
type BenchmarkAnalysis = Pick<
  EngineClient["analysis"],
  "queryMemberFacts" | "queryPackagePerformance"
>;
type BenchmarkSource = Pick<
  EngineClient["source"],
  "queryRetainedMethodBodyComparison"
  | "queryRetainedMethodBodyComparisonTargets"
>;
type BenchmarkCatalog = Pick<
  EngineClient["catalog"],
  "activateRetainedWorkspaceDefinition"
  | "captureCompleteWorkspaceShareState"
>;

export interface PublishedRuntimeBenchmarkBridge {
  readonly host: BenchmarkHost;
  readonly package: BenchmarkPackage;
  readonly analysis: BenchmarkAnalysis;
  readonly source: BenchmarkSource;
  readonly catalog: BenchmarkCatalog;
}

export interface PublishedRuntimeBenchmarkTarget {
  __inspectWebRuntimeBenchmark?: PublishedRuntimeBenchmarkBridge;
}

declare global {
  interface Window extends PublishedRuntimeBenchmarkTarget {}
}

export const publishedRuntimeBenchmarkParameter = "runtime-benchmark";

export function createPublishedRuntimeBenchmarkBridge(
  client: PublishedRuntimeBenchmarkBridge,
): PublishedRuntimeBenchmarkBridge {
  return {
    host: {
      buildIdentity: client.host.buildIdentity,
    },
    package: {
      queryPackage: client.package.queryPackage,
      loadRuntimePack: client.package.loadRuntimePack,
    },
    analysis: {
      queryMemberFacts: client.analysis.queryMemberFacts,
      queryPackagePerformance: client.analysis.queryPackagePerformance,
    },
    source: {
      queryRetainedMethodBodyComparison:
        client.source.queryRetainedMethodBodyComparison,
      queryRetainedMethodBodyComparisonTargets:
        client.source.queryRetainedMethodBodyComparisonTargets,
    },
    catalog: {
      activateRetainedWorkspaceDefinition:
        client.catalog.activateRetainedWorkspaceDefinition,
      captureCompleteWorkspaceShareState:
        client.catalog.captureCompleteWorkspaceShareState,
    },
  };
}

export function installPublishedRuntimeBenchmarkBridge(
  target: PublishedRuntimeBenchmarkTarget,
  search: string,
  bridge: PublishedRuntimeBenchmarkBridge,
): boolean {
  if (new URLSearchParams(search).get(publishedRuntimeBenchmarkParameter)
      !== "1") {
    return false;
  }
  target.__inspectWebRuntimeBenchmark = bridge;
  return true;
}
