import type {
  BrowserPackageChangesFailure,
  BrowserPackageChangesInspection,
  BrowserPackageChangesProgress,
  BrowserPackageChangesRequest,
  BrowserPackageChangesRow,
} from "./facades/inspect-web-package.d.ts";

type PackageChangesCancelReason =
  | "disposed"
  | "superseded"
  | "user";

type PackageChangesSettlement =
  | { readonly kind: "idle" }
  | { readonly kind: "running" }
  | {
      readonly kind: "canceling";
      readonly reason: PackageChangesCancelReason;
    }
  | {
      readonly kind: "canceled";
      readonly reason: string;
    }
  | {
      readonly kind: "failed";
      readonly error: string;
      readonly diagnostic: string | null;
    }
  | {
      readonly kind: "succeeded";
      readonly inspection: BrowserPackageChangesInspection;
    };

export interface PackageChangesState {
  request: BrowserPackageChangesRequest | null;
  progress: BrowserPackageChangesProgress[];
  rows: BrowserPackageChangesRow[];
  failures: BrowserPackageChangesFailure[];
  settlement: PackageChangesSettlement;
}

export type PackageChangesTerminalResult =
  | {
      readonly kind: "succeeded";
      readonly inspection: BrowserPackageChangesInspection;
    }
  | {
      readonly kind: "canceled";
      readonly reason: string;
    }
  | {
      readonly kind: "failed";
      readonly error: string;
      readonly diagnostic: string | null;
    };

export interface PackageChangesDataSource {
  run(
    request: BrowserPackageChangesRequest,
    onProgress: (progress: BrowserPackageChangesProgress) => void,
    onRow: (row: BrowserPackageChangesRow) => void,
    onFailure: (failure: BrowserPackageChangesFailure) => void,
    abortSignal: AbortSignal,
  ): Promise<PackageChangesTerminalResult>;
}

export interface PackageChangesController {
  readonly state: PackageChangesState;
  run(request: BrowserPackageChangesRequest): Promise<void>;
  cancel(reason?: PackageChangesCancelReason): void;
}

export function initialPackageChangesState(): PackageChangesState {
  return {
    request: null,
    progress: [],
    rows: [],
    failures: [],
    settlement: { kind: "idle" },
  };
}

export function createPackageChangesRequest(
  packageSetId: string,
  options: {
    readonly fromExclusive?: string | null;
    readonly throughInclusive?: string | null;
    readonly securityOnly?: boolean;
    readonly maximumRows?: number;
  } = {},
): BrowserPackageChangesRequest {
  return {
    packageSetId,
    fromExclusive: options.fromExclusive ?? null,
    throughInclusive: options.throughInclusive ?? null,
    securityOnly: options.securityOnly ?? false,
    maximumRows: options.maximumRows ?? 100,
  };
}

function withLatestProgress(
  current: readonly BrowserPackageChangesProgress[],
  progress: BrowserPackageChangesProgress,
): BrowserPackageChangesProgress[] {
  const index = current.findIndex(
    candidate => candidate.phase === progress.phase);
  if (index < 0) return [...current, progress];
  return current.map(
    (candidate, candidateIndex) =>
      candidateIndex === index ? progress : candidate);
}

function latestProgress(
  history: readonly BrowserPackageChangesProgress[],
): BrowserPackageChangesProgress[] {
  return history.reduce(withLatestProgress, []);
}

export function createPackageChangesController(
  state: PackageChangesState,
  source: PackageChangesDataSource,
  onUpdate: () => void,
): PackageChangesController {
  let generation = 0;
  let active: {
    readonly controller: AbortController;
    accepting: boolean;
  } | null = null;

  function publish(currentGeneration: number, update: () => void): void {
    if (currentGeneration !== generation || active?.accepting !== true) return;
    update();
    onUpdate();
  }

  function cancel(
    reason: PackageChangesCancelReason = "user",
  ): void {
    if (active === null) return;
    active.accepting = false;
    active.controller.abort(reason);
    state.settlement = { kind: "canceling", reason };
    onUpdate();
  }

  async function run(request: BrowserPackageChangesRequest): Promise<void> {
    if (active !== null) {
      active.accepting = false;
      active.controller.abort("superseded");
    }
    const currentGeneration = ++generation;
    const controller = new AbortController();
    active = { controller, accepting: true };
    state.request = { ...request };
    state.progress = [];
    state.rows = [];
    state.failures = [];
    state.settlement = { kind: "running" };
    onUpdate();

    try {
      const result = await source.run(
        request,
        progress => publish(currentGeneration, () => {
          state.progress = withLatestProgress(state.progress, progress);
        }),
        row => publish(currentGeneration, () => {
          state.rows = [...state.rows, row];
        }),
        failure => publish(currentGeneration, () => {
          state.failures = [...state.failures, failure];
        }),
        controller.signal);
      if (currentGeneration !== generation) return;
      active = null;
      if (result.kind === "succeeded") {
        const content = result.inspection.content;
        state.progress = latestProgress(content.progress);
        state.rows = [...content.rows];
        state.failures = [...content.failures];
        state.settlement = {
          kind: "succeeded",
          inspection: result.inspection,
        };
      } else if (result.kind === "canceled") {
        state.settlement = {
          kind: "canceled",
          reason: result.reason,
        };
      } else {
        state.settlement = {
          kind: "failed",
          error: result.error,
          diagnostic: result.diagnostic,
        };
      }
      onUpdate();
    } catch (error: unknown) {
      if (currentGeneration !== generation) return;
      active = null;
      state.settlement = {
        kind: "failed",
        error: error instanceof Error ? error.message : String(error),
        diagnostic: null,
      };
      onUpdate();
    }
  }

  return { state, run, cancel };
}
