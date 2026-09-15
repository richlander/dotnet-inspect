import { assertNever } from "./data.ts";
import type {
  SpotlightPackageHit,
  SpotlightScope,
} from "./spotlight.ts";

export interface SpotlightPackageSearchState {
  spotlightQuery: string;
  spotlightScope: SpotlightScope;
  spotlightPackageSearch: SpotlightPackageSearchResultState;
}

export interface ReadySpotlightPackageSearch {
  readonly status: "ready";
  readonly query: string;
  readonly hits: readonly SpotlightPackageHit[];
}

export type SpotlightPackageSearchResultState =
  | { readonly status: "idle" }
  | {
      readonly status: "loading";
      readonly query: string;
      readonly cached: ReadySpotlightPackageSearch | null;
    }
  | ReadySpotlightPackageSearch
  | {
      readonly status: "failed";
      readonly query: string;
      readonly error: string;
    };

function cachedPackageSearch(
  state: SpotlightPackageSearchResultState,
): ReadySpotlightPackageSearch | null {
  switch (state.status) {
    case "ready":
      return state;
    case "loading":
      return state.cached;
    case "idle":
    case "failed":
      return null;
    default:
      return assertNever(state, "Spotlight package search state");
  }
}

function settledPackageSearch(
  state: SpotlightPackageSearchResultState,
): ReadySpotlightPackageSearch | { readonly status: "idle" } {
  return cachedPackageSearch(state) ?? { status: "idle" };
}

export function normalizeSpotlightPackageSearchSnapshot(
  state: SpotlightPackageSearchResultState,
): SpotlightPackageSearchResultState {
  return state.status === "loading" ? settledPackageSearch(state) : state;
}

export function visibleSpotlightPackageHits(
  state: SpotlightPackageSearchResultState,
  query: string,
): readonly SpotlightPackageHit[] {
  const cached = cachedPackageSearch(state);
  return cached?.query === query ? cached.hits : [];
}

export function spotlightPackageSearchIsLoading(
  state: SpotlightPackageSearchResultState,
): boolean {
  return state.status === "loading";
}

export function spotlightPackageSearchError(
  state: SpotlightPackageSearchResultState,
): string {
  return state.status === "failed" ? state.error : "";
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
  const packageScopeIsActive = () =>
    state.spotlightScope === "all"
    || state.spotlightScope === "packages";

  return {
    reset() {
      if (scheduled !== null) dependencies.cancelScheduled(scheduled);
      scheduled = null;
      state.spotlightPackageSearch = { status: "idle" };
    },

    schedule() {
      const query = state.spotlightQuery.trim();
      const current = state.spotlightPackageSearch;
      if (scheduled !== null) {
        dependencies.cancelScheduled(scheduled);
        scheduled = null;
      }
      if (!packageScopeIsActive()) {
        state.spotlightPackageSearch = settledPackageSearch(current);
        return;
      }
      if (query.length < 2) {
        state.spotlightPackageSearch = { status: "idle" };
        return;
      }
      const cached = cachedPackageSearch(current);
      if (query === cached?.query) {
        state.spotlightPackageSearch = cached;
        return;
      }
      const pending = {
        status: "loading",
        query,
        cached,
      } as const;
      state.spotlightPackageSearch = pending;
      scheduled = dependencies.schedule(async () => {
        scheduled = null;
        let next: SpotlightPackageSearchResultState;
        try {
          const hits = await dependencies.queryPackages(query);
          next = { status: "ready", query, hits: [...hits] };
        } catch (error) {
          const message = error instanceof Error
            ? error.message
            : String(error);
          next = {
            status: "failed",
            query,
            error:
              `Package search failed: ${message}. Edit the search to try again.`,
          };
        }
        if (state.spotlightPackageSearch !== pending
          || state.spotlightQuery.trim() !== query
          || !packageScopeIsActive()) return;
        state.spotlightPackageSearch = next;
        dependencies.updateResults();
      }, 220);
    },
  };
}
