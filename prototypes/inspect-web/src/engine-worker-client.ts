import {
  createOperationAuthorityPage,
  type OperationDiagnostic,
  type OperationAuthorityPage,
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
import type { EngineClient } from "./engine-client.ts";
import { bindEngineWorkerOperations } from "./engine-worker-operations.ts";
import { registerEngineWorkerComparisonAdapters } from "./engine-worker-comparison.ts";

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
  return {
    host,
    probe: () => session.start("", adapter),
    cpuProbe: () => cpu.start(),
    typeSource: (request: TypeSourceLoadRequest) =>
      typeSourceSession.start(request, typeSourceAdapter),
    dispose: () => {
      session.dispose();
      typeSourceSession.dispose();
      cpu.dispose();
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

export function bindProductionEngineClient(
  host: EngineWorkerHost,
  reportDiagnostic: EngineWorkerProbeOptions["operationDiagnostic"],
  authority: OperationAuthorityPage = createOperationAuthorityPage(),
): EngineClient {
  const startup = bindEngineWorkerStartupClient(host, reportDiagnostic, authority);
  const ordinary = bindEngineWorkerOperations(host, reportDiagnostic, authority);
  // A real startup read is admitted only after the Worker's complete bootstrap.
  // Retain its failure; no second epoch or page-runtime recovery is exposed.
  const ready = startup.host.buildIdentity().then(() => undefined);
  void ready.catch(() => undefined);
  return {
    ...ordinary,
    host: startup.host,
    catalog: { ...ordinary.catalog, ...startup.catalog },
    package: {
      ...ordinary.package, ...startup.package,
      queryAdapter: registerEngineWorkerPackageQueryAdapter(host),
    },
    source: {
      ...ordinary.source,
      typeSourceAdapter: registerEngineWorkerTypeSourceAdapter(host),
      ...registerEngineWorkerComparisonAdapters(host),
    },
    ready,
  };
}

export function createProductionEngineClient(
  origin: string,
  options: EngineWorkerProbeOptions & { readonly operationAuthority: OperationAuthorityPage },
): { readonly client: EngineClient; readonly dispose: () => void } {
  const host = createHost(options);
  const started = host.start(origin);
  if (started.kind === "rejected") {
    host.dispose();
    throw new Error(`Worker could not start: ${started.reason}. Reload the page.`, { cause: started.detail });
  }
  return {
    client: bindProductionEngineClient(host, options.operationDiagnostic, options.operationAuthority),
    dispose: () => host.dispose(),
  };
}
