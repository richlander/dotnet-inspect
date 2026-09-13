interface FrameworkRequestOutcome<TRequest> {
  readonly kind: "finished" | "failed";
  readonly request: TRequest;
  readonly description: string;
  readonly error: string | null;
}

interface PendingFrameworkRequest<TRequest> {
  readonly promise: Promise<FrameworkRequestOutcome<TRequest>>;
  finish(): void;
  fail(error: string): void;
}

export interface FrameworkRequestTracker<TRequest> {
  observe(request: TRequest, description: string): void;
  finish(request: TRequest): void;
  fail(request: TRequest, error: string): void;
  complete(timeoutMilliseconds: number): Promise<readonly TRequest[]>;
  dispose(): void;
}

function pendingFrameworkRequest<TRequest>(
  request: TRequest,
  description: string,
): PendingFrameworkRequest<TRequest> {
  let settled = false;
  let settle!: (outcome: FrameworkRequestOutcome<TRequest>) => void;
  const promise = new Promise<FrameworkRequestOutcome<TRequest>>(resolve => {
    settle = resolve;
  });

  function complete(kind: "finished" | "failed", error: string | null): void {
    if (settled) return;
    settled = true;
    settle({ kind, request, description, error });
  }

  return {
    promise,
    finish: () => complete("finished", null),
    fail: error => complete("failed", error),
  };
}

export function createFrameworkRequestTracker<TRequest>():
  FrameworkRequestTracker<TRequest> {
  const pending = new Map<TRequest, PendingFrameworkRequest<TRequest>>();
  let accepting = true;

  return {
    observe(request, description) {
      if (!accepting || pending.has(request)) return;
      pending.set(request, pendingFrameworkRequest(request, description));
    },
    finish(request) {
      pending.get(request)?.finish();
    },
    fail(request, error) {
      pending.get(request)?.fail(error);
    },
    async complete(timeoutMilliseconds) {
      accepting = false;
      const requests = [...pending.values()];
      if (requests.length === 0) {
        throw new Error(
          "Managed readiness completed without framework network requests.",
        );
      }

      let timer: ReturnType<typeof setTimeout> | undefined;
      const timeout = new Promise<never>((_accept, reject) => {
        timer = setTimeout(
          () => reject(new Error(
            "Timed out waiting for framework network requests to complete.",
          )),
          Math.max(0, timeoutMilliseconds),
        );
      });
      let outcomes: readonly FrameworkRequestOutcome<TRequest>[];
      try {
        outcomes = await Promise.race([
          Promise.all(requests.map(request => request.promise)),
          timeout,
        ]);
      } finally {
        if (timer !== undefined) clearTimeout(timer);
      }

      const failure = outcomes.find(outcome => outcome.kind === "failed");
      if (failure !== undefined) {
        throw new Error(
          `Framework request failed: ${failure.description}: ${failure.error}`,
        );
      }
      return outcomes.map(outcome => outcome.request);
    },
    dispose() {
      accepting = false;
    },
  };
}
