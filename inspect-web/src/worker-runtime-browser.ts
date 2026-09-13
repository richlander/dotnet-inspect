import {
  WorkerRuntimeHost,
  type WorkerRuntimeHostOptions,
  type WorkerRuntimeLifecycleListeners,
  type WorkerRuntimeSource,
  type WorkerRuntimeTransportBinding,
  type WorkerRuntimeTransportHandlers,
} from "./worker-runtime-core.ts";

interface BrowserRuntimeDocument extends EventTarget {
  readonly hidden: boolean;
}

export interface BrowserWorkerRuntimeEnvironmentOptions {
  readonly document: BrowserRuntimeDocument;
  readonly window: EventTarget;
  readonly pollIntervalMilliseconds?: number;
  readonly schedulingToleranceMilliseconds?: number;
  readonly now?: () => number;
  readonly schedule?: (
    callback: () => void,
    milliseconds: number,
  ) => () => void;
}

function positiveMilliseconds(value: number, name: string): number {
  if (!Number.isSafeInteger(value) || value <= 0) {
    throw new RangeError(`${name} must be a positive safe integer.`);
  }
  return value;
}

function scheduleInterval(callback: () => void, milliseconds: number): () => void {
  const timer = globalThis.setInterval(callback, milliseconds);
  return () => globalThis.clearInterval(timer);
}

export class BrowserWorkerRuntimeEnvironment {
  readonly #options: BrowserWorkerRuntimeEnvironmentOptions;
  readonly #now: () => number;
  readonly #interval: number;
  readonly #tolerance: number;
  readonly #clockListeners = new Set<() => void>();
  readonly #lifecycleListeners = new Set<WorkerRuntimeLifecycleListeners>();
  #stopTimer: (() => void) | null = null;
  #wallTime = 0;
  #activeTime = 0;
  #hidden = false;
  #pageHidden = false;
  #frozen = false;

  readonly clock = {
    now: (): number => this.#sample(),
    subscribe: (listener: () => void): (() => void) => {
      this.#connect();
      this.#clockListeners.add(listener);
      return () => {
        this.#clockListeners.delete(listener);
        this.#disconnectIfUnused();
      };
    },
  };

  readonly lifecycle = {
    subscribe: (listener: WorkerRuntimeLifecycleListeners): (() => void) => {
      this.#connect();
      this.#lifecycleListeners.add(listener);
      return () => {
        this.#lifecycleListeners.delete(listener);
        this.#disconnectIfUnused();
      };
    },
  };

  constructor(options: BrowserWorkerRuntimeEnvironmentOptions) {
    this.#options = options;
    this.#now = options.now ?? (() => performance.now());
    this.#interval = positiveMilliseconds(
      options.pollIntervalMilliseconds ?? 100,
      "pollIntervalMilliseconds",
    );
    this.#tolerance = options.schedulingToleranceMilliseconds ?? 1_000;
    if (!Number.isSafeInteger(this.#tolerance) || this.#tolerance < 0) {
      throw new RangeError("schedulingToleranceMilliseconds must be a non-negative safe integer.");
    }
  }

  #suspended(): boolean {
    return this.#hidden || this.#pageHidden || this.#frozen;
  }

  #sample(): number {
    const wallTime = this.#now();
    const elapsed = Math.max(0, wallTime - this.#wallTime);
    this.#wallTime = wallTime;
    if (this.#stopTimer === null || this.#suspended()) {
      return Math.floor(this.#activeTime);
    }
    const previous = Math.floor(this.#activeTime);
    this.#activeTime += elapsed;
    const activeTime = Math.floor(this.#activeTime);
    if (elapsed > this.#interval + this.#tolerance) {
      // Publish recovery before a message handler can judge an overdue deadline.
      // Advancing the anchor first also makes a recovery callback's now() safe.
      for (const listener of this.#lifecycleListeners) {
        listener.mainLoopRecovered(activeTime - previous);
      }
    }
    return Math.floor(this.#activeTime);
  }

  #transition(change: () => void): void {
    this.#sample();
    const wasSuspended = this.#suspended();
    change();
    const suspended = this.#suspended();
    if (wasSuspended === suspended) return;
    for (const listener of this.#lifecycleListeners) {
      if (suspended) listener.suspended();
      else listener.resumed();
    }
    for (const listener of this.#clockListeners) listener();
  }

  readonly #visibilityChanged = (): void => {
    this.#transition(() => {
      this.#hidden = this.#options.document.hidden;
    });
  };

  readonly #pageHide = (): void => {
    this.#transition(() => { this.#pageHidden = true; });
  };

  readonly #pageShow = (): void => {
    this.#transition(() => {
      this.#pageHidden = false;
      this.#hidden = this.#options.document.hidden;
    });
  };

  readonly #freeze = (): void => {
    this.#transition(() => { this.#frozen = true; });
  };

  readonly #resume = (): void => {
    this.#transition(() => { this.#frozen = false; });
  };

  #connect(): void {
    if (this.#stopTimer !== null) return;
    this.#wallTime = this.#now();
    this.#hidden = this.#options.document.hidden;
    this.#pageHidden = false;
    this.#frozen = false;
    const document = this.#options.document;
    const window = this.#options.window;
    document.addEventListener("visibilitychange", this.#visibilityChanged);
    document.addEventListener("freeze", this.#freeze);
    document.addEventListener("resume", this.#resume);
    window.addEventListener("pagehide", this.#pageHide);
    window.addEventListener("pageshow", this.#pageShow);
    this.#stopTimer = (this.#options.schedule ?? scheduleInterval)(() => {
      this.#sample();
      for (const listener of this.#clockListeners) listener();
    }, this.#interval);
  }

  #disconnectIfUnused(): void {
    if (this.#clockListeners.size !== 0 || this.#lifecycleListeners.size !== 0) {
      return;
    }
    this.#sample();
    this.#stopTimer?.();
    this.#stopTimer = null;
    const document = this.#options.document;
    const window = this.#options.window;
    document.removeEventListener("visibilitychange", this.#visibilityChanged);
    document.removeEventListener("freeze", this.#freeze);
    document.removeEventListener("resume", this.#resume);
    window.removeEventListener("pagehide", this.#pageHide);
    window.removeEventListener("pageshow", this.#pageShow);
  }
}

class BrowserWorkerRuntimeTransport {
  readonly #createWorker: () => Worker;

  constructor(createWorker: () => Worker) {
    this.#createWorker = createWorker;
  }

  create(): WorkerRuntimeTransportBinding {
    const worker = this.#createWorker();
    const source: WorkerRuntimeSource = {
      send: message => worker.postMessage(message, { transfer: [] }),
      terminate: () => worker.terminate(),
    };
    return {
      source,
      bind(handlers: WorkerRuntimeTransportHandlers): () => void {
        const message = (event: MessageEvent<unknown>): void => {
          handlers.message(source, event.data);
        };
        const error = (event: ErrorEvent): void => {
          handlers.error(source, new Error(event.message));
        };
        const messageError = (): void => {
          handlers.messageError(source, new Error("Worker message could not be deserialized."));
        };
        worker.addEventListener("message", message);
        worker.addEventListener("error", error);
        worker.addEventListener("messageerror", messageError);
        return () => {
          worker.removeEventListener("message", message);
          worker.removeEventListener("error", error);
          worker.removeEventListener("messageerror", messageError);
        };
      },
    };
  }
}

export function createBrowserWorkerRuntimeHost<TBootstrap, TDiagnostic>(
  createWorker: () => Worker,
  options: Omit<
    WorkerRuntimeHostOptions<TBootstrap, TDiagnostic>,
    "clock" | "lifecycle" | "transport"
  >,
): WorkerRuntimeHost<TBootstrap, TDiagnostic> {
  const environment = new BrowserWorkerRuntimeEnvironment({
    document,
    window,
    schedulingToleranceMilliseconds:
      options.schedulingToleranceMilliseconds ?? 0,
  });
  return new WorkerRuntimeHost({
    ...options,
    clock: environment.clock,
    lifecycle: environment.lifecycle,
    transport: new BrowserWorkerRuntimeTransport(createWorker),
  });
}
