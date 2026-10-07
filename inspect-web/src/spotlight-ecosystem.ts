import type {
  BrowserEcosystemPackageClassification,
  BrowserEcosystemPackageCandidate,
  BrowserEcosystemPackageInventory,
} from "./facades/inspect-web-package.d.ts";
import type { PlatformCatalogTarget } from "./platform-index.ts";
import type { SpotlightPruningEvidence, SpotlightResult } from "./spotlight.ts";

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
  let inventoryRevision = 0;
  let inventory: BrowserEcosystemPackageInventory | null = null;
  let requestKey = "";
  let requestGeneration = 0;
  let annotations = new Map<string, BrowserEcosystemPackageClassification>();
  let failure = "";
  return {
    error: () => failure,
    project(results: SpotlightResult[], traversalTfm: string, target: PlatformCatalogTarget | null): SpotlightResult[] {
      const candidatesByCoordinate = new Map<string, BrowserEcosystemPackageCandidate>();
      for (const result of results) {
        const value = coordinate(result);
        if (value) candidatesByCoordinate.set(
          coordinateKey(value), { id: value.id, version: value.version ?? null });
      }
      const candidates = [...candidatesByCoordinate.values()];
      const candidatesJson = JSON.stringify(candidates);
      if (target !== inventoryTarget) {
        inventoryTarget = target;
        inventoryRevision++;
        inventory = target
          ? { tfm: target.tfm, version: target.version, supplies: target.supplies }
          : null;
      }
      const key = JSON.stringify([traversalTfm, candidatesJson, inventoryRevision]);
      if (key !== requestKey) {
        requestKey = key;
        const generation = ++requestGeneration;
        annotations = new Map();
        failure = "";
        if (candidates.length > 0) {
          const requestedInventory = inventory ?? { tfm: traversalTfm, version: "", supplies: null };
          // Promise.resolve also contains a synchronous transport failure.
          void Promise.resolve().then(() => options.classify(
            traversalTfm, candidates, requestedInventory)).then(values => {
            if (requestGeneration !== generation) return undefined;
            annotations = new Map();
            for (const value of values) annotations.set(coordinateKey(value), value);
            options.updateResults();
            return undefined;
          }, (error: unknown) => {
            if (requestGeneration !== generation) return undefined;
            failure = `Ecosystem annotations unavailable: ${error instanceof Error ? error.message : String(error)}`;
            options.updateResults();
            return undefined;
          });
        }
      }
      const libraryPruning = new Map<string, SpotlightPruningEvidence>();
      if (target) {
        for (const annotation of annotations.values()) {
          if (annotation.isPruned !== true) continue;
          const pack = annotation.platformLayer === "DotNetRuntime" ? "netcore.app"
            : annotation.platformLayer === "AspNetCore" ? "aspnetcore.app" : null;
          if (pack) libraryPruning.set(
            JSON.stringify([annotation.id.toLowerCase(), pack, target.tfm, target.version]),
            { traversalTfm, platformVersion: target.version });
        }
      }
      return results.map(result => {
        if (result.kind === "framework-lib") {
          const pruning = libraryPruning.get(JSON.stringify([
            result.assembly.toLowerCase(), result.pack, result.tfm, result.version,
          ]));
          if (pruning) return { ...result, pruning };
          if (!result.pruning) return result;
          const { pruning: _pruning, ...unannotated } = result;
          return unannotated;
        }
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
