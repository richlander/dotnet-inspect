import {
  createOperationAuthorityPage,
  type OperationDiagnostic,
  type OperationProducerAdapter,
} from "./operation-authority.ts";
import type { BrowserSource } from "./facades/inspect-web-source.d.ts";
import type { TypeSourceLoadRequest } from "./source-inspection.ts";
import { createBrowserWorkerRuntimeHost } from "./worker-runtime-browser.ts";
import {
  createEngineWorkerProducerClasses,
  engineWorkerBoundaryErrors,
  engineWorkerCanaryKind,
  engineWorkerDiagnostic,
  engineWorkerPolicy,
  engineWorkerText,
} from "./engine-worker-contract.ts";
import {
  createEngineWorkerTypeSourceHostRegistration,
} from "./engine-worker-source.ts";
import {
  createEngineWorkerCloneCandidateHostRegistration,
  type EngineWorkerCloneCandidateAdapter,
} from "./engine-worker-analysis.ts";
import type {
  BrowserCloneCandidateResult,
} from "./facades/inspect-web-analysis.d.ts";
import {
  createEngineWorkerPackageQueryHostRegistration,
  type EngineWorkerPackageQueryCompletionEvent,
  type EngineWorkerPackageQueryDurableEvent,
} from "./engine-worker-package-query.ts";
import {
  bindEngineWorkerCpuProbe,
} from "./engine-worker-cpu.ts";
import type {
  WorkerRuntimeControlledOperationAdapter,
  WorkerRuntimeHost,
  WorkerRuntimeHostOptions,
  WorkerRuntimePreparationError,
} from "./worker-runtime-core.ts";
import { bindEngineWorkerStartupClient } from "./engine-worker-startup.ts";
import type { QueryRequest } from "./package-query.ts";

function createEngineWorker(): Worker {
  return new Worker(new URL("./engine-worker-entry.ts", import.meta.url), {
    type: "module",
    name: "dotnet-inspect",
  });
}

export interface EngineWorkerProbeOptions {
  readonly callbacks: WorkerRuntimeHostOptions<string, string>["callbacks"];
  readonly operationDiagnostic: (diagnostic: OperationDiagnostic) => undefined;
  readonly startupBudgetMilliseconds?: number;
}

export type EngineWorkerHost = WorkerRuntimeHost<string, string>;

export type EngineWorkerTypeSourceAdapter = OperationProducerAdapter<
  TypeSourceLoadRequest,
  BrowserSource,
  string,
  never,
  WorkerRuntimePreparationError
>;

export function registerEngineWorkerTypeSourceAdapter(
  host: EngineWorkerHost,
): EngineWorkerTypeSourceAdapter {
  return host.registerOperation(
    createEngineWorkerTypeSourceHostRegistration(),
  );
}

export function registerEngineWorkerCloneCandidateAdapter(
  host: EngineWorkerHost,
): EngineWorkerCloneCandidateAdapter {
  return host.registerOperation(
    createEngineWorkerCloneCandidateHostRegistration(),
  );
}

export type EngineWorkerPackageQueryAdapter =
  WorkerRuntimeControlledOperationAdapter<
    QueryRequest,
    EngineWorkerPackageQueryCompletionEvent,
    string,
    never,
    WorkerRuntimePreparationError,
    EngineWorkerPackageQueryDurableEvent,
    number,
    number,
    string
  >;

export function registerEngineWorkerPackageQueryAdapter(
  host: EngineWorkerHost,
): EngineWorkerPackageQueryAdapter {
  return host.registerControlledOperation(
    createEngineWorkerPackageQueryHostRegistration(),
  );
}

function createHost(options: EngineWorkerProbeOptions) {
  return createBrowserWorkerRuntimeHost(createEngineWorker, {
    ...engineWorkerPolicy,
    startupBudgetMilliseconds:
      options.startupBudgetMilliseconds ?? engineWorkerPolicy.startupBudgetMilliseconds,
    producerClasses: createEngineWorkerProducerClasses(),
    bootstrap: { encode: engineWorkerText.decode, diagnostic: engineWorkerText },
    diagnostic: engineWorkerText,
    createDiagnostic: (kind, detail) => `${kind}: ${engineWorkerDiagnostic(detail)}`.slice(0, 4_096),
    callbacks: options.callbacks,
  });
}

// The existing managed canary is an explicit diagnostic consumer, not a feature
// migration or a claim that application operations already run in this Worker.
export function createEngineWorkerProbe(options: EngineWorkerProbeOptions) {
  const host = createHost(options);
  const adapter = host.registerOperation({
    kind: engineWorkerCanaryKind,
    allowance: { kind: "unbounded" },
    encodeInput: (input: string) => engineWorkerText.decode(input),
    value: engineWorkerText,
    error: engineWorkerText,
    diagnostic: engineWorkerText,
    progress: engineWorkerText,
    mapPreparationError: error => error,
    boundaryErrors: engineWorkerBoundaryErrors,
  });
  const typeSourceAdapter = registerEngineWorkerTypeSourceAdapter(host);
  const cloneCandidateAdapter =
    registerEngineWorkerCloneCandidateAdapter(host);
  const page = createOperationAuthorityPage();
  const cpu = bindEngineWorkerCpuProbe(host, page, options.operationDiagnostic);
  const session = page.createSession<
    string, string, string, string, WorkerRuntimePreparationError
  >({
    feature: { publish: () => undefined },
    diagnostic: { report: options.operationDiagnostic },
  });
  const typeSourceSession = page.createSession<
    TypeSourceLoadRequest,
    BrowserSource,
    string,
    never,
    WorkerRuntimePreparationError
  >({
    feature: { publish: () => undefined },
    diagnostic: { report: options.operationDiagnostic },
  });
  const cloneCandidateSession = page.createSession<
    string,
    BrowserCloneCandidateResult,
    string,
    never,
    WorkerRuntimePreparationError
  >({
    feature: { publish: () => undefined },
    diagnostic: { report: options.operationDiagnostic },
  });
  return {
    host,
    probe: () => session.start("", adapter),
    cpuProbe: () => cpu.start(),
    typeSource: (request: TypeSourceLoadRequest) =>
      typeSourceSession.start(request, typeSourceAdapter),
    cloneCandidates: (requestJson: string) =>
      cloneCandidateSession.start(requestJson, cloneCandidateAdapter),
    dispose: () => {
      session.dispose();
      typeSourceSession.dispose();
      cloneCandidateSession.dispose();
      cpu.dispose();
      host.dispose();
    },
  };
}

function delay(milliseconds: number): Promise<void> {
  return new Promise(resolve => {
    setTimeout(resolve, milliseconds);
  });
}

export function createEngineWorkerCloneCandidateClient(
  origin: string,
  options: EngineWorkerProbeOptions,
) {
  const host = createHost(options);
  const adapter = registerEngineWorkerCloneCandidateAdapter(host);
  const page = createOperationAuthorityPage();
  const session = page.createSession<
    string,
    BrowserCloneCandidateResult,
    string,
    never,
    WorkerRuntimePreparationError
  >({
    feature: { publish: () => undefined },
    diagnostic: { report: options.operationDiagnostic },
  });
  const started = host.start(origin);
  if (started.kind === "rejected") {
    session.dispose();
    host.dispose();
    throw new Error(`Worker could not start: ${started.reason}.`, {
      cause: started.detail,
    });
  }
  const ready = async (): Promise<void> => {
    const deadline = performance.now()
      + (options.startupBudgetMilliseconds
        ?? engineWorkerPolicy.startupBudgetMilliseconds);
    while (host.snapshot().phase === "starting") {
      if (performance.now() >= deadline)
        throw new Error("Clone Candidates Worker startup timed out.");
      await delay(10);
    }
    if (host.snapshot().phase !== "ready")
      throw new Error("Clone Candidates Worker is unavailable.");
  };
  let queryGeneration = 0;
  return {
    async query(requestJson: string): Promise<BrowserCloneCandidateResult> {
      const requestGeneration = ++queryGeneration;
      await ready();
      if (requestGeneration !== queryGeneration) {
        throw new Error("Clone Candidates operation was superseded.");
      }
      const operation = session.start(requestJson, adapter);
      if (operation.kind === "rejected") {
        throw new Error(
          `Clone Candidates Worker rejected the operation: ${operation.reason.kind}.`,
        );
      }
      const outcome = await operation.handle.outcome;
      if (outcome.kind === "succeeded") return outcome.value;
      if (outcome.kind === "failed") throw new Error(outcome.error);
      throw new Error(`Clone Candidates operation was ${outcome.reason}.`);
    },
    dispose() {
      queryGeneration++;
      session.dispose();
      host.dispose();
    },
  };
}

export function createEngineWorkerStartupClient(origin: string, options: EngineWorkerProbeOptions) {
  const host = createHost(options);
  const started = host.start(origin);
  if (started.kind === "rejected") {
    host.dispose();
    throw new Error(`Worker could not start: ${started.reason}.`, { cause: started.detail });
  }
  return {
    client: bindEngineWorkerStartupClient(host, options.operationDiagnostic),
    dispose: () => host.dispose(),
  };
}
