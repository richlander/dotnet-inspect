import { assertNever } from "./data.ts";
import type {
  BrowserCapabilityCatalogSearchInspection,
  BrowserCapabilityCatalogSearchResult,
} from "./facades/inspect-web-package.d.ts";
import type { SpotlightScope } from "./spotlight.ts";

export const SPOTLIGHT_CAPABILITY_RESULT_LIMIT = 6;
const CAPABILITY_SEARCH_MAXIMUM_TEXT_LENGTH = 128;

export interface SpotlightCapabilitySearchState {
  spotlightQuery: string;
  spotlightScope: SpotlightScope;
  spotlightCapabilitySearch: SpotlightCapabilitySearchResultState;
}

export type SpotlightCapabilitySearchResultState =
  | { readonly status: "idle" }
  | { readonly status: "loading"; readonly query: string }
  | {
      readonly status: "ready";
      readonly query: string;
      readonly inspection: BrowserCapabilityCatalogSearchInspection;
    }
  | {
      readonly status: "failed";
      readonly query: string;
      readonly error: string;
    };

export function normalizeSpotlightCapabilitySearchSnapshot(
  state: SpotlightCapabilitySearchResultState,
): SpotlightCapabilitySearchResultState {
  return state.status === "loading" ? { status: "idle" } : state;
}

export function visibleSpotlightCapabilityResults(
  state: SpotlightCapabilitySearchResultState,
  query: string,
): readonly BrowserCapabilityCatalogSearchResult[] {
  return state.status === "ready" && state.query === query
    ? state.inspection.content.results
    : [];
}

export function spotlightCapabilitySearchMessage(
  state: SpotlightCapabilitySearchResultState,
  query: string,
): string {
  switch (state.status) {
    case "idle":
    case "loading":
      return "";
    case "failed":
      return state.query === query ? state.error : "";
    case "ready":
      return state.query === query
        ? state.inspection.diagnostics
            .map(diagnostic => diagnostic.summary)
            .join(" ")
        : "";
    default:
      return assertNever(state, "Spotlight capability search state");
  }
}

export interface SpotlightCapabilitySearchDependencies<TSchedule> {
  state: SpotlightCapabilitySearchState;
  searchCapabilities: (
    query: string,
    maximumResults: number,
  ) => Promise<BrowserCapabilityCatalogSearchInspection>;
  schedule: (callback: () => Promise<void>, delay: number) => TSchedule;
  cancelScheduled: (scheduled: TSchedule) => void;
  updateResults: () => void;
}

export function createSpotlightCapabilitySearch<TSchedule>(
  dependencies: SpotlightCapabilitySearchDependencies<TSchedule>,
) {
  const { state } = dependencies;
  let scheduled: TSchedule | null = null;

  return {
    reset() {
      if (scheduled !== null) dependencies.cancelScheduled(scheduled);
      scheduled = null;
      state.spotlightCapabilitySearch = { status: "idle" };
    },

    schedule() {
      const query = state.spotlightQuery.trim();
      if (scheduled !== null) {
        dependencies.cancelScheduled(scheduled);
        scheduled = null;
      }
      if (state.spotlightScope !== "all" || query.length === 0) {
        state.spotlightCapabilitySearch = { status: "idle" };
        return;
      }
      if (query.length > CAPABILITY_SEARCH_MAXIMUM_TEXT_LENGTH) {
        state.spotlightCapabilitySearch = {
          status: "failed",
          query,
          error: "Capability search supports at most 128 characters.",
        };
        return;
      }
      const current = state.spotlightCapabilitySearch;
      if (current.status === "ready" && current.query === query) return;

      const pending = { status: "loading", query } as const;
      state.spotlightCapabilitySearch = pending;
      scheduled = dependencies.schedule(async () => {
        scheduled = null;
        let next: SpotlightCapabilitySearchResultState;
        try {
          const inspection = await dependencies.searchCapabilities(
            query,
            SPOTLIGHT_CAPABILITY_RESULT_LIMIT,
          );
          next = { status: "ready", query, inspection };
        } catch (error) {
          const message = error instanceof Error
            ? error.message
            : String(error);
          next = {
            status: "failed",
            query,
            error:
              `Capability search failed: ${message}. Edit the search to try again.`,
          };
        }
        if (state.spotlightCapabilitySearch !== pending
          || state.spotlightQuery.trim() !== query
          || state.spotlightScope !== "all") return;
        state.spotlightCapabilitySearch = next;
        if (next.status === "failed"
          || next.inspection.content.results.length > 0
          || next.inspection.diagnostics.length > 0) {
          dependencies.updateResults();
        }
      }, 60);
    },
  };
}
