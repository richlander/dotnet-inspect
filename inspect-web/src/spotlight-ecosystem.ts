import type { BrowserEcosystemPackageClassification, BrowserEcosystemPackageCandidate, BrowserEcosystemPackageInventory } from "./facades/inspect-web-package.d.ts";
import type { PlatformCatalogTarget } from "./platform-index.ts";
import type { SpotlightResult } from "./spotlight.ts";

function coordinate(result: SpotlightResult): { id: string; version?: string } | null {
  switch (result.kind) {
    case "pkg-loaded": return result.pkg.isRuntimePack ? null : result.pkg;
    case "pkg-nuget": return result.hit;
    case "pkg-recent": return result.entry;
    default: return null;
  }
}

function coordinateKey(value: { id: string; version?: string | null }): string {
  return JSON.stringify([value.id.toLowerCase(), value.version ?? null]);
}

/** Local Worker annotation; no catalog loading or acquisition is admitted here. */
export function createSpotlightEcosystemClassification(options: {
  classify: (tfm: string, candidates: readonly BrowserEcosystemPackageCandidate[], inventory: BrowserEcosystemPackageInventory) => Promise<readonly BrowserEcosystemPackageClassification[]>;
  updateResults: () => void;
}) {
  let inventoryTarget: PlatformCatalogTarget | null = null;
  let inventory: BrowserEcosystemPackageInventory | null = null;
  let requestKey = "";
  let annotations = new Map<string, BrowserEcosystemPackageClassification>();
  let failure = "";
  return {
    error: () => failure,
    project(results: SpotlightResult[], traversalTfm: string, target: PlatformCatalogTarget | null): SpotlightResult[] {
      const candidates = [...new Map(results.flatMap(result => {
        const value = coordinate(result);
        return value ? [[coordinateKey(value), { id: value.id, version: value.version ?? null }] as const] : [];
      })).values()];
      const candidatesJson = JSON.stringify(candidates);
      if (target !== inventoryTarget) {
        inventoryTarget = target;
        inventory = target
          ? { tfm: target.tfm, version: target.version, supplies: target.supplies }
          : null;
      }
      const key = JSON.stringify([traversalTfm, candidatesJson, inventory]);
      if (key !== requestKey) {
        requestKey = key;
        annotations = new Map();
        failure = "";
        if (candidates.length > 0) {
          // Promise.resolve also contains a synchronous transport failure.
          void Promise.resolve().then(() => options.classify(traversalTfm, candidates, inventory ?? { tfm: traversalTfm, version: "", supplies: null })).then(values => {
            if (requestKey !== key) return undefined;
            annotations = new Map(values.map(value => [coordinateKey(value), value]));
            options.updateResults();
            return undefined;
          }, (error: unknown) => {
            if (requestKey !== key) return undefined;
            failure = `Ecosystem annotations unavailable: ${error instanceof Error ? error.message : String(error)}`;
            options.updateResults();
            return undefined;
          });
        }
      }
      return results.map(result => {
        const value = coordinate(result);
        const annotation = value ? annotations.get(coordinateKey(value)) : null;
        return annotation?.ecosystemId && annotation.ecosystemTitle
          ? { ...result, ecosystem: {
              id: annotation.ecosystemId,
              title: annotation.ecosystemTitle,
              isPruned: annotation.isPruned,
              traversalTfm,
              platformVersion: target?.version ?? null,
            } }
          : result;
      });
    },
  };
}
