import {
  createOperationAuthorityPage,
  type OperationDiagnostic,
  type OperationAuthorityPage,
  type OperationProducerAdapter,
} from "./operation-authority.ts";
import type {
  BrowserMethodBodyComparison,
  BrowserMethodBodyComparisonRequest,
  BrowserMethodBodyTargets,
  BrowserSource,
  BrowserSourceComparison,
  BrowserSourceComparisonRequest,
} from "./facades/inspect-web-source.d.ts";
import type { TypeSourceLoadRequest } from "./source-inspection.ts";
import type {
  MethodBodyComparisonContext,
} from "./method-body-comparison.ts";
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
  createEngineWorkerMemberSourceComparisonHostRegistration,
  createEngineWorkerMethodBodyComparisonHostRegistration,
  createEngineWorkerMethodBodyTargetsHostRegistration,
} from "./engine-worker-source-authority.ts";
import {
  setEngineWorkerBindingPage,
} from "./engine-worker-ordinary.ts";
import {
  bindEngineWorkerCpuProbe,
} from "./engine-worker-cpu.ts";
import {
  bindEngineWorkerAnalysisClient,
  type EngineWorkerAnalysisClient,
} from "./engine-worker-analysis.ts";
import {
  bindEngineWorkerCallGraphClient,
  type EngineWorkerCallGraphClient,
} from "./engine-worker-call-graph.ts";
import {
  bindEngineWorkerCatalogClient,
  type EngineWorkerCatalogClient,
} from "./engine-worker-catalog.ts";
import {
  bindEngineWorkerMetadataClient,
  type EngineWorkerMetadataClient,
} from "./engine-worker-metadata.ts";
import {
  bindEngineWorkerPackageClient,
  type EngineWorkerPackageClient,
} from "./engine-worker-package.ts";
import {
  bindEngineWorkerOrdinarySourceClient,
  type EngineWorkerOrdinarySourceClient,
} from "./engine-worker-source-ordinary.ts";
import type {
  WorkerRuntimeControlledOperationAdapter,
  WorkerRuntimeHost,
  WorkerRuntimeHostOptions,
  WorkerRuntimePreparationError,
} from "./worker-runtime-core.ts";
import { bindEngineWorkerStartupClient } from "./engine-worker-startup.ts";
import type { EngineStartupClient } from "./engine-worker-startup.ts";
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
  readonly operationAuthority?: OperationAuthorityPage;
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

export type EngineWorkerMethodBodyTargetsAdapter = OperationProducerAdapter<
  MethodBodyComparisonContext,
  BrowserMethodBodyTargets,
  string,
  never,
  WorkerRuntimePreparationError
>;

export type EngineWorkerMethodBodyComparisonAdapter =
  OperationProducerAdapter<
    BrowserMethodBodyComparisonRequest,
    BrowserMethodBodyComparison,
    string,
    never,
    WorkerRuntimePreparationError
  >;

export type EngineWorkerMemberSourceComparisonAdapter =
  OperationProducerAdapter<
    BrowserSourceComparisonRequest,
    BrowserSourceComparison,
    string,
    never,
    WorkerRuntimePreparationError
  >;

export function registerEngineWorkerMethodBodyTargetsAdapter(
  host: EngineWorkerHost,
): EngineWorkerMethodBodyTargetsAdapter {
  return host.registerOperation(
    createEngineWorkerMethodBodyTargetsHostRegistration(),
  );
}

export function registerEngineWorkerMethodBodyComparisonAdapter(
  host: EngineWorkerHost,
): EngineWorkerMethodBodyComparisonAdapter {
  return host.registerOperation(
    createEngineWorkerMethodBodyComparisonHostRegistration(),
  );
}

export function registerEngineWorkerMemberSourceComparisonAdapter(
  host: EngineWorkerHost,
): EngineWorkerMemberSourceComparisonAdapter {
  return host.registerOperation(
    createEngineWorkerMemberSourceComparisonHostRegistration(),
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

export interface EngineWorkerClient {
  readonly runtimeHost: EngineWorkerHost;
  readonly host: EngineStartupClient["host"];
  readonly package:
    EngineStartupClient["package"] & EngineWorkerPackageClient;
  readonly metadata: EngineWorkerMetadataClient;
  readonly analysis: EngineWorkerAnalysisClient;
  readonly source: EngineWorkerOrdinarySourceClient;
  readonly callGraph: EngineWorkerCallGraphClient;
  readonly catalog:
    EngineStartupClient["catalog"] & EngineWorkerCatalogClient;
  readonly packageQueryAdapter: EngineWorkerPackageQueryAdapter;
  readonly typeSourceAdapter: EngineWorkerTypeSourceAdapter;
  readonly methodBodyTargetsAdapter:
    EngineWorkerMethodBodyTargetsAdapter;
  readonly methodBodyComparisonAdapter:
    EngineWorkerMethodBodyComparisonAdapter;
  readonly memberSourceComparisonAdapter:
    EngineWorkerMemberSourceComparisonAdapter;
  dispose(): void;
}

export function createEngineWorkerClient(
  origin: string,
  options: EngineWorkerProbeOptions,
): EngineWorkerClient {
  const host = createHost(options);
  const started = host.start(origin);
  if (started.kind === "rejected") {
    host.dispose();
    throw new Error(
      `Worker could not start: ${started.reason}.`,
      { cause: started.detail },
    );
  }
  try {
    const epoch = host.snapshot().epochToken;
    if (epoch === null)
      throw new Error("The started Worker has no active epoch.");
    if (options.operationAuthority !== undefined) {
      setEngineWorkerBindingPage(
        host,
        epoch,
        options.operationAuthority);
    }
    const startup = bindEngineWorkerStartupClient(
      host,
      options.operationDiagnostic);
    return {
      runtimeHost: host,
      host: startup.host,
      package: {
        ...startup.package,
        ...bindEngineWorkerPackageClient(
          host,
          options.operationDiagnostic),
      },
      metadata: bindEngineWorkerMetadataClient(
        host,
        options.operationDiagnostic),
      analysis: bindEngineWorkerAnalysisClient(
        host,
        options.operationDiagnostic),
      source: bindEngineWorkerOrdinarySourceClient(
        host,
        options.operationDiagnostic),
      callGraph: bindEngineWorkerCallGraphClient(
        host,
        options.operationDiagnostic),
      catalog: {
        ...startup.catalog,
        ...bindEngineWorkerCatalogClient(
          host,
          options.operationDiagnostic),
      },
      packageQueryAdapter:
        registerEngineWorkerPackageQueryAdapter(host),
      typeSourceAdapter:
        registerEngineWorkerTypeSourceAdapter(host),
      methodBodyTargetsAdapter:
        registerEngineWorkerMethodBodyTargetsAdapter(host),
      methodBodyComparisonAdapter:
        registerEngineWorkerMethodBodyComparisonAdapter(host),
      memberSourceComparisonAdapter:
        registerEngineWorkerMemberSourceComparisonAdapter(host),
      dispose: () => host.dispose(),
    };
  } catch (error: unknown) {
    host.dispose();
    throw error;
  }
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
