type TerminalEngineFailureRetry =
  (() => void | Promise<unknown>) | null | string;

export interface TerminalEngineFailureState {
  loading: boolean;
  engineReady: boolean;
  engineStartupFailed: boolean;
  engineStatus: string;
  errorTitle: string;
  error: string;
  errorDetail: string;
  retryAction: TerminalEngineFailureRetry;
}

export interface TerminalEngineFailureLatch {
  fail(state: TerminalEngineFailureState, detail: string): void;
  reassert(state: TerminalEngineFailureState): boolean;
  reloadIfFailed(): boolean;
}

export function createTerminalEngineFailureLatch(
  reload: () => void,
): TerminalEngineFailureLatch {
  let detail: string | null = null;

  const reassert = (state: TerminalEngineFailureState): boolean => {
    if (detail === null) return false;
    state.loading = false;
    state.engineReady = false;
    state.engineStartupFailed = true;
    state.engineStatus = "";
    state.errorTitle = "Inspection Worker stopped";
    state.error =
      "The inspection engine stopped. Reload to start a new Worker.";
    state.errorDetail = detail;
    state.retryAction = reload;
    return true;
  };

  return {
    fail(state, failureDetail) {
      detail = failureDetail;
      reassert(state);
    },
    reassert,
    reloadIfFailed() {
      if (detail === null) return false;
      reload();
      return true;
    },
  };
}
