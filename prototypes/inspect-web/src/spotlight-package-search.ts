import type {
  SpotlightPackageHit,
  SpotlightScope,
} from "./spotlight.ts";

export interface SpotlightPackageSearchState {
  spotlightQuery: string;
  spotlightScope: SpotlightScope;
  spotlightPkgHits: SpotlightPackageHit[];
  /** The query whose successful results are cached in spotlightPkgHits. */
  spotlightPkgQuery: string;
  spotlightPkgLoading: boolean;
  spotlightPkgError?: string;
}

export interface SpotlightPackageSearchDependencies<TSchedule> {
  state: SpotlightPackageSearchState;
  queryPackages: (query: string) => Promise<readonly SpotlightPackageHit[]>;
  schedule: (callback: () => Promise<void>, delay: number) => TSchedule;
  cancelScheduled: (scheduled: TSchedule) => void;
  updateResults: () => void;
}

export function createSpotlightPackageSearch<TSchedule>(
  dependencies: SpotlightPackageSearchDependencies<TSchedule>,
) {
  const { state } = dependencies;
  let scheduled: TSchedule | null = null;
  let generation = 0;

  const fetchPackages = async (query: string, requestGeneration: number) => {
    try {
      const hits = await dependencies.queryPackages(query);
      if (requestGeneration !== generation
        || state.spotlightQuery.trim() !== query) return;
      state.spotlightPkgHits = [...hits];
      state.spotlightPkgQuery = query;
      state.spotlightPkgError = "";
    } catch (error) {
      if (requestGeneration !== generation
        || state.spotlightQuery.trim() !== query) return;
      state.spotlightPkgHits = [];
      state.spotlightPkgQuery = "";
      const message = error instanceof Error ? error.message : String(error);
      state.spotlightPkgError =
        `Package search failed: ${message}. Edit the search to try again.`;
    } finally {
      if (requestGeneration === generation
        && state.spotlightQuery.trim() === query) {
        state.spotlightPkgLoading = false;
        dependencies.updateResults();
      }
    }
  };

  return {
    reset() {
      generation++;
      if (scheduled !== null) dependencies.cancelScheduled(scheduled);
      scheduled = null;
      state.spotlightPkgHits = [];
      state.spotlightPkgQuery = "";
      state.spotlightPkgLoading = false;
      state.spotlightPkgError = "";
    },

    schedule() {
      const query = state.spotlightQuery.trim();
      if (scheduled !== null) {
        dependencies.cancelScheduled(scheduled);
        scheduled = null;
      }
      if (state.spotlightScope !== "all"
        && state.spotlightScope !== "packages") {
        generation++;
        state.spotlightPkgLoading = false;
        state.spotlightPkgError = "";
        return;
      }
      if (query.length < 2) {
        generation++;
        state.spotlightPkgHits = [];
        state.spotlightPkgQuery = "";
        state.spotlightPkgLoading = false;
        state.spotlightPkgError = "";
        return;
      }
      if (query === state.spotlightPkgQuery) {
        generation++;
        state.spotlightPkgLoading = false;
        return;
      }
      const requestGeneration = ++generation;
      state.spotlightPkgError = "";
      state.spotlightPkgLoading = true;
      scheduled = dependencies.schedule(async () => {
        scheduled = null;
        await fetchPackages(query, requestGeneration);
      }, 220);
    },
  };
}
