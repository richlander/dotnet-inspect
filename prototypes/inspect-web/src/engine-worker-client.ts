import {
  createOperationAuthorityPage,
  type OperationCancelReason,
  type OperationAuthorityPage,
  type OperationDiagnostic,
  type OperationHandle,
  type OperationStartError,
  type OperationSession,
  type OperationProducerAdapter,
} from "./operation-authority.ts";
import type {
  BrowserSource,
  BrowserTypeSourceResult,
} from "./facades/inspect-web-source.d.ts";
import type {
  BrowserPackageQueryEvent,
  BrowserPackageQueryResult,
} from "./facades/inspect-web-package.d.ts";
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
  type EngineWorkerTypeSourceFailure,
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
  type EngineWorkerPackageQueryTerminalFailure,
} from "./engine-worker-package-query.ts";
import {
  bindEngineWorkerOrdinaryClient,
} from "./engine-worker-ordinary.ts";
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
import {
  PACKAGE_QUERY_INITIAL_MATCH_CREDIT,
  type QueryRequest,
} from "./package-query.ts";
import type { EngineClient } from "./engine-client.ts";

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

export interface SharedEngineOperationAuthority {
  readonly page: OperationAuthorityPage;
  startWithId<T>(operationId: string, start: () => T): T;
}

export function createSharedEngineOperationAuthority(): SharedEngineOperationAuthority {
  let suppliedOperationId: string | null = null;
  const page = createOperationAuthorityPage({
    allocation: {
      createId: () => {
        const operationId = suppliedOperationId;
        suppliedOperationId = null;
        return operationId ?? globalThis.crypto.randomUUID();
      },
    },
  });
  return {
    page,
    startWithId<T>(operationId: string, start: () => T): T {
      if (suppliedOperationId !== null) {
        throw new Error(
          "An engine operation identity allocation is already active.");
      }
      suppliedOperationId = operationId;
      try {
        return start();
      } finally {
        suppliedOperationId = null;
      }
    },
  };
}

export type EngineWorkerTypeSourceAdapter = OperationProducerAdapter<
  TypeSourceLoadRequest,
  BrowserSource,
  EngineWorkerTypeSourceFailure,
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

function bindCloneCandidateFacade(
  adapter: EngineWorkerCloneCandidateAdapter,
  reportDiagnostic: (diagnostic: OperationDiagnostic) => undefined,
  authority: SharedEngineOperationAuthority,
): Pick<EngineClient["analysis"], "queryCloneCandidates">
  & { readonly dispose: () => void } {
  const session = authority.page.createSession<
    string,
    BrowserCloneCandidateResult,
    string,
    never,
    WorkerRuntimePreparationError
  >({
    feature: { publish: () => undefined },
    diagnostic: { report: reportDiagnostic },
  });
  return {
    async queryCloneCandidates(requestJson) {
      const started = session.start(requestJson, adapter);
      if (started.kind === "rejected") {
        throw new Error(
          `Clone Candidates could not start: ${startFailureReason(started.reason)}.`);
      }
      const outcome = await started.handle.outcome;
      await started.handle.quiesced;
      if (outcome.kind === "succeeded") return outcome.value;
      if (outcome.kind === "failed") throw new Error(outcome.error);
      throw new Error(`Clone Candidates operation was ${outcome.reason}.`);
    },
    dispose() {
      session.dispose();
    },
  };
}

export type EngineWorkerPackageQueryAdapter =
  WorkerRuntimeControlledOperationAdapter<
    QueryRequest,
    EngineWorkerPackageQueryCompletionEvent,
    EngineWorkerPackageQueryTerminalFailure,
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

function operationCancelReason(reason: string): OperationCancelReason {
  switch (reason) {
    case "user":
    case "superseded":
    case "disposed":
    case "feature-observer-failed":
    case "timeout":
    case "worker-restarted":
      return reason;
    default:
      return "user";
  }
}

function startFailureReason(
  reason: OperationStartError<WorkerRuntimePreparationError>,
): string {
  return reason.kind === "producer-rejected"
    ? reason.error.kind
    : reason.kind;
}

function packageQueryRequest(
  searchText: string,
  facetIdsJson: string,
  maximumCandidates: number,
  maximumMatches: number,
  includePrerelease: boolean,
  initialMatchCredit: number,
  packageType: string | null,
  sourceOrderId: string | null,
  discovery: boolean,
): QueryRequest {
  if (initialMatchCredit !== PACKAGE_QUERY_INITIAL_MATCH_CREDIT) {
    throw new Error(
      `Package Query initial credit must be ${PACKAGE_QUERY_INITIAL_MATCH_CREDIT}.`);
  }
  const rawFacetIds: unknown = JSON.parse(facetIdsJson);
  if (!Array.isArray(rawFacetIds)
    || !rawFacetIds.every(value => typeof value === "string")) {
    throw new TypeError("Package Query facet IDs must be a JSON string array.");
  }
  return {
    inputKind: discovery ? "gallery" : "package",
    scopeQuery: searchText,
    facets: rawFacetIds.map(key => ({
      key,
      label: key,
      tier: "nuspec",
    })),
    requestedLimit: maximumCandidates,
    requestedMatchLimit: maximumMatches,
    packageType,
    sourceOrderId,
    includePrerelease,
  };
}

function packageAssemblyQueryRequest(
  patternId: string,
  operand: string,
  packageCoordinatesJson: string,
  targetFramework: string,
  initialMatchCredit: number,
): QueryRequest {
  if (initialMatchCredit !== PACKAGE_QUERY_INITIAL_MATCH_CREDIT) {
    throw new Error(
      `Package Query initial credit must be ${PACKAGE_QUERY_INITIAL_MATCH_CREDIT}.`);
  }
  const rawCoordinates: unknown = JSON.parse(packageCoordinatesJson);
  if (!Array.isArray(rawCoordinates)
    || !rawCoordinates.every(value => typeof value === "string")) {
    throw new TypeError(
      "Package Query coordinates must be a JSON string array.");
  }
  return {
    inputKind: "package",
    scopeQuery: "",
    facets: [],
    requestedLimit: Math.max(1, rawCoordinates.length),
    requestedMatchLimit: Math.max(1, rawCoordinates.length),
    packageType: null,
    sourceOrderId: null,
    includePrerelease: false,
    assemblyPattern: {
      patternId,
      operand,
      packageCoordinates: rawCoordinates,
      targetFramework,
    },
  };
}

function publishPackageQueryEvent(
  eventSink: unknown,
  event: BrowserPackageQueryEvent,
): void {
  if ((typeof eventSink !== "object" && typeof eventSink !== "function")
    || eventSink === null) {
    throw new TypeError("Package Query event sink is unavailable.");
  }
  if (!Reflect.set(eventSink, "event", JSON.stringify(event))) {
    throw new TypeError("Package Query event sink rejected an event.");
  }
}

export function bindTypeSourceFacade(
  adapter: EngineWorkerTypeSourceAdapter,
  reportDiagnostic: (diagnostic: OperationDiagnostic) => undefined,
  authority: SharedEngineOperationAuthority,
): Pick<
  EngineClient["source"],
  "cancelTypeSourceQuery" | "queryTypeSource"
> & { readonly dispose: () => void } {
  interface ActiveTypeSource {
    readonly handle: OperationHandle<
      BrowserSource,
      EngineWorkerTypeSourceFailure
    >;
    readonly session: OperationSession<
      TypeSourceLoadRequest,
      BrowserSource,
      EngineWorkerTypeSourceFailure,
      never,
      WorkerRuntimePreparationError
    >;
  }
  const active = new Map<string, ActiveTypeSource>();
  return {
    async queryTypeSource(
      operationId,
      packageId,
      version,
      framework,
      assembly,
      type,
      taste,
    ): Promise<BrowserTypeSourceResult> {
      if (active.has(operationId))
        throw new Error(`Type Source operation '${operationId}' is already active.`);
      const session = authority.page.createSession<
        TypeSourceLoadRequest,
        BrowserSource,
        EngineWorkerTypeSourceFailure,
        never,
        WorkerRuntimePreparationError
      >({
        feature: { publish: () => undefined },
        diagnostic: { report: reportDiagnostic },
      });
      const started = authority.startWithId(operationId, () => session.start({
          packageId,
          version,
          framework,
          assembly,
          type,
          taste,
          signature: `${packageId}/${version}/${framework}/${assembly}/${type}`,
          isVisible: () => true,
        }, adapter));
      if (started.kind === "rejected") {
        session.dispose();
        throw new Error(
          `Type Source could not start: ${startFailureReason(started.reason)}.`);
      }
      active.set(operationId, { handle: started.handle, session });
      try {
        const outcome = await started.handle.outcome;
        await started.handle.quiesced;
        if (outcome.kind === "succeeded") {
          return {
            version: 1,
            kind: "Succeeded",
            value: outcome.value,
            failureKind: null,
            error: null,
            diagnostic: null,
            reason: null,
          };
        }
        if (outcome.kind === "failed") {
          return {
            version: 1,
            kind: "Failed",
            value: null,
            failureKind: outcome.error.failureKind,
            error: outcome.error.error,
            diagnostic: outcome.error.diagnostic,
            reason: null,
          };
        }
        return {
          version: 1,
          kind: "Canceled",
          value: null,
          failureKind: null,
          error: null,
          diagnostic: null,
          reason: outcome.reason,
        };
      } finally {
        active.delete(operationId);
        session.dispose();
      }
    },
    cancelTypeSourceQuery(operationId, reason) {
      active.get(operationId)?.handle.cancel(operationCancelReason(reason));
    },
    dispose() {
      for (const operation of active.values())
        operation.session.dispose();
      active.clear();
    },
  };
}

export function bindPackageQueryFacade(
  adapter: EngineWorkerPackageQueryAdapter,
  reportDiagnostic: (diagnostic: OperationDiagnostic) => undefined,
  authority: SharedEngineOperationAuthority,
): Pick<
  EngineClient["package"],
  | "cancelPackageQuery"
  | "requestPackageQueryMatches"
  | "runPackageAssemblyQuery"
  | "runPackageQuery"
> & { readonly dispose: () => void } {
  interface ActivePackageQuery {
    readonly handle: OperationHandle<
      EngineWorkerPackageQueryCompletionEvent,
      EngineWorkerPackageQueryTerminalFailure
    >;
    readonly session: OperationSession<
      QueryRequest,
      EngineWorkerPackageQueryCompletionEvent,
      EngineWorkerPackageQueryTerminalFailure,
      never,
      WorkerRuntimePreparationError,
      EngineWorkerPackageQueryDurableEvent
    >;
  }
  const active = new Map<string, ActivePackageQuery>();

  async function run(
    operationId: string,
    request: QueryRequest,
    eventSink: unknown,
  ): Promise<BrowserPackageQueryResult> {
    if (active.has(operationId))
      throw new Error(`Package Query operation '${operationId}' is already active.`);
    const session = authority.page.createSession<
      QueryRequest,
      EngineWorkerPackageQueryCompletionEvent,
      EngineWorkerPackageQueryTerminalFailure,
      never,
      WorkerRuntimePreparationError,
      EngineWorkerPackageQueryDurableEvent
    >({
      feature: {
        publish: event => {
          if (event.kind === "durable")
            publishPackageQueryEvent(eventSink, event.durable.value);
          return undefined;
        },
      },
      diagnostic: { report: reportDiagnostic },
    });
    const started = authority.startWithId(
      operationId,
      () => session.start(request, adapter),
    );
    if (started.kind === "rejected") {
      session.dispose();
      throw new Error(
        `Package Query could not start: ${startFailureReason(started.reason)}.`);
    }
    active.set(operationId, { handle: started.handle, session });
    try {
      const outcome = await started.handle.outcome;
      await started.handle.quiesced;
      if (outcome.kind === "succeeded") {
        return {
          version: 1,
          kind: "Succeeded",
          value: outcome.value,
          failureKind: null,
          error: null,
          diagnostic: null,
          reason: null,
        };
      }
      if (outcome.kind === "failed") {
        return {
          version: 1,
          kind: "Failed",
          value: null,
          failureKind: outcome.error.failureKind,
          error: outcome.error.error,
          diagnostic: outcome.error.diagnostic,
          reason: null,
        };
      }
      return {
        version: 1,
        kind: "Canceled",
        value: null,
        failureKind: null,
        error: null,
        diagnostic: null,
        reason: outcome.reason,
      };
    } finally {
      active.delete(operationId);
      session.dispose();
    }
  }

  return {
    cancelPackageQuery(operationId, reason) {
      active.get(operationId)?.handle.cancel(operationCancelReason(reason));
    },
    async requestPackageQueryMatches(operationId, additionalMatchCredit) {
      const operation = active.get(operationId);
      if (operation === undefined) {
        return { kind: "NotActive", additionalMatchCredit: null };
      }
      const result = await adapter.requestControl(
        operation.handle.id,
        additionalMatchCredit,
      );
      if (result.kind === "acknowledged") {
        return {
          kind: "Granted",
          additionalMatchCredit: result.value,
        };
      }
      if (result.kind === "not-active") {
        return { kind: "NotActive", additionalMatchCredit: null };
      }
      throw new Error(result.error);
    },
    runPackageQuery(
      operationId,
      searchText,
      facetIdsJson,
      maximumCandidates,
      maximumMatches,
      includePrerelease,
      initialMatchCredit,
      eventSink,
      packageType,
      sourceOrderId,
      discovery,
    ) {
      return run(
        operationId,
        packageQueryRequest(
          searchText,
          facetIdsJson,
          maximumCandidates,
          maximumMatches,
          includePrerelease,
          initialMatchCredit,
          packageType,
          sourceOrderId,
          discovery,
        ),
        eventSink,
      );
    },
    runPackageAssemblyQuery(
      operationId,
      patternId,
      operand,
      packageCoordinatesJson,
      targetFramework,
      initialMatchCredit,
      eventSink,
    ) {
      return run(
        operationId,
        packageAssemblyQueryRequest(
          patternId,
          operand,
          packageCoordinatesJson,
          targetFramework,
          initialMatchCredit,
        ),
        eventSink,
      );
    },
    dispose() {
      for (const operation of active.values())
        operation.session.dispose();
      active.clear();
    },
  };
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
    EngineWorkerTypeSourceFailure,
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

export interface ProductionEngineWorkerClient {
  readonly host: EngineWorkerHost;
  readonly client: EngineClient;
  readonly ready: Promise<void>;
  dispose(): void;
}

export function createProductionEngineWorkerClient(
  origin: string,
  options: EngineWorkerProbeOptions,
): ProductionEngineWorkerClient {
  const host = createHost(options);
  const started = host.start(origin);
  if (started.kind === "rejected") {
    host.dispose();
    throw new Error(`Worker could not start: ${started.reason}.`, {
      cause: started.detail,
    });
  }

  const authority = createSharedEngineOperationAuthority();
  const startup = bindEngineWorkerStartupClient(
    host,
    options.operationDiagnostic,
    authority.page,
  );
  const ordinary = bindEngineWorkerOrdinaryClient(
    host,
    options.operationDiagnostic,
    authority.page,
  );
  const typeSource = bindTypeSourceFacade(
    registerEngineWorkerTypeSourceAdapter(host),
    options.operationDiagnostic,
    authority,
  );
  const cloneCandidates = bindCloneCandidateFacade(
    registerEngineWorkerCloneCandidateAdapter(host),
    options.operationDiagnostic,
    authority,
  );
  const packageQuery = bindPackageQueryFacade(
    registerEngineWorkerPackageQueryAdapter(host),
    options.operationDiagnostic,
    authority,
  );
  const identity = startup.host.buildIdentity();
  const client: EngineClient = {
    host: {
      buildIdentity: () => identity,
    },
    package: {
      ...ordinary.package,
      ...startup.package,
      ...packageQuery,
    },
    metadata: ordinary.metadata,
    analysis: {
      ...ordinary.analysis,
      ...cloneCandidates,
    },
    source: {
      ...ordinary.source,
      ...typeSource,
    },
    callGraph: ordinary.callGraph,
    catalog: {
      ...ordinary.catalog,
      ...startup.catalog,
    },
  };
  return {
    host,
    client,
    ready: identity.then(() => undefined),
    dispose() {
      packageQuery.dispose();
      cloneCandidates.dispose();
      typeSource.dispose();
      host.dispose();
    },
  };
}
