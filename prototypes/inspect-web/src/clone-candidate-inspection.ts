import type {
  BrowserCloneCandidateRequest,
  BrowserCloneCandidateResult,
  BrowserCloneCandidateBreadth,
  BrowserCloneCandidateDiscovery,
  BrowserCloneCandidateSeedRequest,
} from "./facades/inspect-web-analysis.d.ts";

export type CloneCandidateBreadth =
  Exclude<BrowserCloneCandidateBreadth, number>;
export type CloneCandidateDiscovery =
  Exclude<BrowserCloneCandidateDiscovery, number>;

export const DEFAULT_CLONE_BREADTH: CloneCandidateBreadth = "Everything";
export const DEFAULT_CLONE_DISCOVERY: CloneCandidateDiscovery =
  "SimilarNames";

export interface CloneCandidatePackageCoordinate {
  readonly id: string;
  readonly version: string;
  readonly framework: string;
  readonly runtimePack?: boolean;
}

export interface CloneCandidateRequestInput {
  readonly packages: readonly CloneCandidatePackageCoordinate[];
  readonly selectedPackage: CloneCandidatePackageCoordinate;
  readonly assembly: string;
  readonly seed: BrowserCloneCandidateSeedRequest;
}

export interface CloneCandidateRequestAvailability {
  readonly request: BrowserCloneCandidateRequest | null;
  readonly reason: string;
}

export interface CloneCandidateInspectionState {
  breadth: CloneCandidateBreadth;
  discovery: CloneCandidateDiscovery;
  request: BrowserCloneCandidateRequest | null;
  result: BrowserCloneCandidateResult | null;
  loading: boolean;
  error: string;
  navigationLoading: boolean;
  navigationError: string;
  selectedRank: number | null;
}

export function createCloneCandidateInspectionState():
CloneCandidateInspectionState {
  return {
    breadth: DEFAULT_CLONE_BREADTH,
    discovery: DEFAULT_CLONE_DISCOVERY,
    request: null,
    result: null,
    loading: false,
    error: "",
    navigationLoading: false,
    navigationError: "",
    selectedRank: null,
  };
}

function coordinateKey(coordinate: CloneCandidatePackageCoordinate): string {
  return [
    coordinate.id,
    coordinate.version,
    coordinate.framework,
  ].map(value => value.toLowerCase()).join("\u241f");
}

export function buildCloneCandidateRequest(
  input: CloneCandidateRequestInput,
  breadth: CloneCandidateBreadth,
  discovery: CloneCandidateDiscovery,
): CloneCandidateRequestAvailability {
  if (input.selectedPackage.runtimePack) {
    return {
      request: null,
      reason: "Clone Candidates is not available for platform runtime libraries.",
    };
  }
  if (!input.assembly) {
    return {
      request: null,
      reason: "The selected library does not have an exact assembly identity.",
    };
  }

  const packages = input.packages.filter(pkg => !pkg.runtimePack);
  const selectedKey = coordinateKey(input.selectedPackage);
  const selectedPackageIndex = packages.findIndex(
    coordinate => coordinateKey(coordinate) === selectedKey);
  if (selectedPackageIndex < 0) {
    return {
      request: null,
      reason: "The selected package is no longer present in the Workspace.",
    };
  }

  return {
    request: {
      schemaVersion: 1,
      packages: packages.map(coordinate => ({
        packageId: coordinate.id,
        version: coordinate.version,
        targetFramework: coordinate.framework,
      })),
      selectedPackageIndex,
      assembly: input.assembly,
      seed: input.seed,
      breadth,
      discovery,
    },
    reason: "",
  };
}

export interface CloneCandidateInspectionDependencies {
  readonly state: CloneCandidateInspectionState;
  query(requestJson: string): Promise<BrowserCloneCandidateResult>;
  isCurrent(request: BrowserCloneCandidateRequest): boolean;
  describeError(error: unknown): string;
  render(): void;
}

export interface CloneCandidateInspectionCoordinator {
  setBreadth(value: CloneCandidateBreadth): boolean;
  setDiscovery(value: CloneCandidateDiscovery): boolean;
  reconcile(
    input: CloneCandidateRequestInput | null,
    unavailableReason: string,
  ): boolean;
  load(input: CloneCandidateRequestInput): Promise<void>;
  showUnavailable(reason: string): void;
  selectRank(rank: number): boolean;
  clear(): void;
  dispose(): void;
}

export function createCloneCandidateInspectionCoordinator(
  dependencies: CloneCandidateInspectionDependencies,
): CloneCandidateInspectionCoordinator {
  const { state } = dependencies;
  let generation = 0;

  const clearResult = () => {
    state.request = null;
    state.result = null;
    state.loading = false;
    state.error = "";
    state.navigationLoading = false;
    state.navigationError = "";
    state.selectedRank = null;
  };

  const replace = () => {
    generation++;
    clearResult();
  };

  return {
    setBreadth(value) {
      if (state.breadth === value) return false;
      state.breadth = value;
      replace();
      return true;
    },

    setDiscovery(value) {
      if (state.discovery === value) return false;
      state.discovery = value;
      replace();
      return true;
    },

    reconcile(input, unavailableReason) {
      const availability = input
        ? buildCloneCandidateRequest(
            input,
            state.breadth,
            state.discovery)
        : { request: null, reason: unavailableReason };
      if (availability.request) {
        if (state.request
          && JSON.stringify(state.request)
            === JSON.stringify(availability.request)) {
          return false;
        }
      } else if (state.request === null
        && state.result === null
        && !state.loading
        && state.error === availability.reason) {
        return false;
      }

      replace();
      return true;
    },

    async load(input) {
      const availability = buildCloneCandidateRequest(
        input,
        state.breadth,
        state.discovery);
      if (!availability.request) {
        this.showUnavailable(availability.reason);
        return;
      }

      const request = availability.request;
      const requestJson = JSON.stringify(request);
      const requestGeneration = ++generation;
      state.request = request;
      state.result = null;
      state.loading = true;
      state.error = "";
      state.navigationLoading = false;
      state.navigationError = "";
      state.selectedRank = null;
      dependencies.render();
      try {
        const result = await dependencies.query(requestJson);
        if (requestGeneration !== generation
          || !dependencies.isCurrent(request)) {
          return;
        }
        state.result = result;
        state.selectedRank = result.kind === "Available"
          ? result.document?.rows[0]?.rank ?? null
          : null;
      } catch (error) {
        if (requestGeneration !== generation
          || !dependencies.isCurrent(request)) {
          return;
        }
        state.error = dependencies.describeError(error)
          || "Clone candidate discovery failed.";
      } finally {
        if (requestGeneration === generation) {
          state.loading = false;
          if (dependencies.isCurrent(request)) dependencies.render();
        }
      }
    },

    showUnavailable(reason) {
      replace();
      state.error = reason;
      dependencies.render();
    },

    selectRank(rank) {
      const rows = state.result?.kind === "Available"
        ? state.result.document?.rows ?? []
        : [];
      if (!rows.some(row => row.rank === rank)
        || state.selectedRank === rank) {
        return false;
      }
      state.selectedRank = rank;
      state.navigationError = "";
      dependencies.render();
      return true;
    },

    clear() {
      replace();
    },

    dispose() {
      replace();
    },
  };
}
