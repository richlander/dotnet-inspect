import type { EngineClient } from "./engine-client.ts";

type SourceComparisonPackage = Pick<
  EngineClient["package"],
  "queryPackage"
>;
type SourceComparisonSource = Pick<
  EngineClient["source"],
  "cancelMemberSourceComparison" | "queryMemberSourceComparison"
>;

export interface PublishedSourceComparisonBridge {
  readonly package: SourceComparisonPackage;
  readonly source: SourceComparisonSource;
}

export interface PublishedSourceComparisonTarget {
  __inspectWebSourceComparison?: PublishedSourceComparisonBridge;
}

declare global {
  interface Window extends PublishedSourceComparisonTarget {}
}

const publishedSourceComparisonParameter = "source-comparison-gate";

export function createPublishedSourceComparisonBridge(
  client: PublishedSourceComparisonBridge,
): PublishedSourceComparisonBridge {
  return {
    package: {
      queryPackage: client.package.queryPackage,
    },
    source: {
      cancelMemberSourceComparison:
        client.source.cancelMemberSourceComparison,
      queryMemberSourceComparison:
        client.source.queryMemberSourceComparison,
    },
  };
}

export function installPublishedSourceComparisonBridge(
  target: PublishedSourceComparisonTarget,
  search: string,
  bridge: PublishedSourceComparisonBridge,
): boolean {
  if (new URLSearchParams(search).get(publishedSourceComparisonParameter)
      !== "1") {
    return false;
  }
  target.__inspectWebSourceComparison = bridge;
  return true;
}
