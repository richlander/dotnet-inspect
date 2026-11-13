import type {
  BrowserPackagePerformanceSummary,
  BrowserPerformanceMember,
} from "./facades/inspect-web-analysis.d.ts";

type LibraryPerformanceCancelReason =
  | "disposed"
  | "superseded"
  | "user";

type LibraryPerformanceSettlement =
  | { readonly kind: "idle" }
  | { readonly kind: "running" }
  | {
      readonly kind: "canceling";
      readonly reason: LibraryPerformanceCancelReason;
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
      readonly summary: BrowserPackagePerformanceSummary;
    };

export interface LibraryPerformanceRequest {
  readonly packageId: string;
  readonly version: string;
  readonly targetFramework: string;
  readonly assemblyName: string;
}

export interface LibraryPerformanceState {
  request: LibraryPerformanceRequest | null;
  members: BrowserPerformanceMember[];
  settlement: LibraryPerformanceSettlement;
}

export type LibraryPerformanceTerminalResult =
  | {
      readonly kind: "succeeded";
      readonly summary: BrowserPackagePerformanceSummary;
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

export interface LibraryPerformanceRunResult {
  readonly members: BrowserPerformanceMember[];
  readonly settlement: LibraryPerformanceTerminalResult;
}

export interface LibraryPerformanceDataSource {
  run(
    request: LibraryPerformanceRequest,
    onItem: (member: BrowserPerformanceMember) => void,
    abortSignal: AbortSignal,
  ): Promise<LibraryPerformanceTerminalResult>;
}

export interface LibraryPerformanceController {
  readonly state: LibraryPerformanceState;
  run(request: LibraryPerformanceRequest): Promise<LibraryPerformanceRunResult>;
  cancel(reason?: LibraryPerformanceCancelReason): void;
  reset(): void;
}

export function initialLibraryPerformanceState(): LibraryPerformanceState {
  return {
    request: null,
    members: [],
    settlement: { kind: "idle" },
  };
}

export function createLibraryPerformanceController(
  state: LibraryPerformanceState,
  source: LibraryPerformanceDataSource,
  onUpdate: () => void,
): LibraryPerformanceController {
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
    reason: LibraryPerformanceCancelReason = "user",
  ): void {
    if (active === null) return;
    active.accepting = false;
    active.controller.abort(reason);
    state.settlement = { kind: "canceling", reason };
    onUpdate();
  }

  function reset(): void {
    const retiring = active;
    generation++;
    active = null;
    if (retiring !== null) {
      retiring.accepting = false;
      retiring.controller.abort("superseded");
    }
    const fresh = initialLibraryPerformanceState();
    state.request = fresh.request;
    state.members = fresh.members;
    state.settlement = fresh.settlement;
    onUpdate();
  }

  async function run(
    request: LibraryPerformanceRequest,
  ): Promise<LibraryPerformanceRunResult> {
    if (active !== null) {
      active.accepting = false;
      active.controller.abort("superseded");
    }
    const currentGeneration = ++generation;
    const controller = new AbortController();
    active = { controller, accepting: true };
    state.request = { ...request };
    state.members = [];
    state.settlement = { kind: "running" };
    onUpdate();
    const members: BrowserPerformanceMember[] = [];

    try {
      const result = await source.run(
        request,
        member => {
          members.push(member);
          publish(currentGeneration, () => {
            state.members = [...members];
          });
        },
        controller.signal);
      if (currentGeneration !== generation) {
        return { members, settlement: result };
      }
      active = null;
      state.settlement = result;
      onUpdate();
      return { members, settlement: result };
    } catch (error: unknown) {
      const result: LibraryPerformanceTerminalResult = {
        kind: "failed",
        error: error instanceof Error ? error.message : String(error),
        diagnostic: null,
      };
      if (currentGeneration !== generation) {
        return { members, settlement: result };
      }
      active = null;
      state.settlement = result;
      onUpdate();
      return { members, settlement: result };
    }
  }

  return { state, run, cancel, reset };
}
