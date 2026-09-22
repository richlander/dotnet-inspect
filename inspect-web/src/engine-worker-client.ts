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
  BrowserTypeCodeView,
  BrowserTypeSourceResult,
} from "./facades/inspect-web-source.d.ts";
import type {
  BrowserPackageChangesRequest,
  BrowserPackageChangesResult,
  BrowserPackageQueryEvent,
  BrowserPackageQueryResult,
  BrowserPackageQueryTerm,
} from "./facades/inspect-web-package.d.ts";
import { typeSourceView, type TypeSourceLoadRequest } from "./source-inspection.ts";
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
  createEngineWorkerPackageChangesHostRegistration,
  engineWorkerPackageChangesInput,
  type EngineWorkerPackageChangesDurableEvent,
  type EngineWorkerPackageChangesTerminalFailure,
} from "./engine-worker-package-changes.ts";
import {
  createEngineWorkerPackageQueryHostRegistration,
  type EngineWorkerPackageQueryDurableEvent,
  type EngineWorkerPackageQueryTerminal,
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
  BrowserTypeCodeView,
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

export type EngineWorkerPackageQueryAdapter =
  WorkerRuntimeControlledOperationAdapter<
    QueryRequest,
    EngineWorkerPackageQueryTerminal,
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

export type EngineWorkerPackageChangesAdapter =
  OperationProducerAdapter<
    BrowserPackageChangesRequest,
    import("./facades/inspect-web-package.d.ts").BrowserPackageChangesInspection,
    EngineWorkerPackageChangesTerminalFailure,
    import("./facades/inspect-web-package.d.ts").BrowserPackageChangesProgress,
    WorkerRuntimePreparationError,
    EngineWorkerPackageChangesDurableEvent
  >;

export function registerEngineWorkerPackageChangesAdapter(
  host: EngineWorkerHost,
): EngineWorkerPackageChangesAdapter {
  return host.registerOperation(
    createEngineWorkerPackageChangesHostRegistration(),
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

function registerEngineWorkerCanaryAdapter(host: EngineWorkerHost) {
  return host.registerOperation({
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
}

function packageQueryRequest(
  searchText: string,
  terms: readonly BrowserPackageQueryTerm[],
  targetFramework: string | null,
  maximumCandidates: number,
  maximumMatches: number,
  includePrerelease: boolean,
  initialMatchCredit: number,
): QueryRequest {
  if (initialMatchCredit !== PACKAGE_QUERY_INITIAL_MATCH_CREDIT) {
    throw new Error(
      `Package Query initial credit must be ${PACKAGE_QUERY_INITIAL_MATCH_CREDIT}.`);
  }
  return {
    scopeQuery: searchText,
    presets: [],
    terms: terms.map(term => {
      return {
        descriptor: {
          key: term.key,
          label: term.key,
          summary: "",
          weight: 0,
          tier: "nuspec",
          executionClass: "nuspec",
          operators: [term.operator],
          valueKind: "",
          example: "",
          multiline: false,
        },
        operator: term.operator,
        value: term.value,
      };
    }),
    requestedLimit: maximumCandidates,
    requestedMatchLimit: maximumMatches,
    includePrerelease,
    targetFramework: targetFramework ?? "net10.0",
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

function publishPackageChangesEvent(
  eventSink: unknown,
  event: unknown,
): void {
  if ((typeof eventSink !== "object" && typeof eventSink !== "function")
    || eventSink === null) {
    throw new TypeError("Package Activity event sink is unavailable.");
  }
  if (!Reflect.set(eventSink, "event", JSON.stringify(event))) {
    throw new TypeError("Package Activity event sink rejected an event.");
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
      BrowserTypeCodeView,
      EngineWorkerTypeSourceFailure
    >;
    readonly session: OperationSession<
      TypeSourceLoadRequest,
      BrowserTypeCodeView,
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
      view,
    ): Promise<BrowserTypeSourceResult> {
      if (active.has(operationId))
        throw new Error(`Type Source operation '${operationId}' is already active.`);
      const selectedView = typeSourceView(view);
      if (selectedView === null)
        throw new Error(`Unknown Type Source view '${view}'.`);
      const session = authority.page.createSession<
        TypeSourceLoadRequest,
        BrowserTypeCodeView,
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
          view: selectedView,
          signature: `${packageId}/${version}/${framework}/${assembly}/${type}/${view}`,
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
  | "runPackageQuery"
> & { readonly dispose: () => void } {
  interface ActivePackageQuery {
    readonly handle: OperationHandle<
      EngineWorkerPackageQueryTerminal,
      EngineWorkerPackageQueryTerminalFailure
    >;
    readonly session: OperationSession<
      QueryRequest,
      EngineWorkerPackageQueryTerminal,
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
      EngineWorkerPackageQueryTerminal,
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
          version: 3,
          kind: "Succeeded",
          value: outcome.value.inspection === null
            ? outcome.value.event
            : null,
          inspection: outcome.value.inspection,
          failureKind: null,
          error: null,
          diagnostic: null,
          reason: null,
        };
      }
      if (outcome.kind === "failed") {
        return {
          version: 3,
          kind: "Failed",
          value: null,
          inspection: null,
          failureKind: outcome.error.failureKind,
          error: outcome.error.error,
          diagnostic: outcome.error.diagnostic,
          reason: null,
        };
      }
      return {
        version: 3,
        kind: "Canceled",
        value: null,
        inspection: null,
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
      termsJson,
      targetFramework,
      maximumCandidates,
      maximumMatches,
      includePrerelease,
      initialMatchCredit,
      eventSink,
    ) {
      return run(
        operationId,
        packageQueryRequest(
          searchText,
          termsJson,
          targetFramework,
          maximumCandidates,
          maximumMatches,
          includePrerelease,
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

export function bindPackageChangesFacade(
  adapter: EngineWorkerPackageChangesAdapter,
  reportDiagnostic: (diagnostic: OperationDiagnostic) => undefined,
  authority: SharedEngineOperationAuthority,
): Pick<
  EngineClient["package"],
  "cancelPackageActivity" | "runPackageActivity"
> & { readonly dispose: () => void } {
  type PackageChangesInspection =
    import("./facades/inspect-web-package.d.ts")
      .BrowserPackageChangesInspection;
  type PackageChangesProgress =
    import("./facades/inspect-web-package.d.ts")
      .BrowserPackageChangesProgress;
  interface ActivePackageChanges {
    readonly handle: OperationHandle<
      PackageChangesInspection,
      EngineWorkerPackageChangesTerminalFailure
    >;
    readonly session: OperationSession<
      BrowserPackageChangesRequest,
      PackageChangesInspection,
      EngineWorkerPackageChangesTerminalFailure,
      PackageChangesProgress,
      WorkerRuntimePreparationError,
      EngineWorkerPackageChangesDurableEvent
    >;
  }
  const active = new Map<string, ActivePackageChanges>();

  return {
    cancelPackageActivity(operationId, reason) {
      active.get(operationId)?.handle.cancel(operationCancelReason(reason));
    },
    async runPackageActivity(operationId, request, eventSink) {
      if (active.has(operationId)) {
        throw new Error(
          `Package Activity operation '${operationId}' is already active.`);
      }
      const decodedRequest =
        engineWorkerPackageChangesInput.decode(request);
      if (decodedRequest.kind === "rejected") {
        throw new TypeError(decodedRequest.message, {
          cause: decodedRequest.cause,
        });
      }
      const session = authority.page.createSession<
        BrowserPackageChangesRequest,
        PackageChangesInspection,
        EngineWorkerPackageChangesTerminalFailure,
        PackageChangesProgress,
        WorkerRuntimePreparationError,
        EngineWorkerPackageChangesDurableEvent
      >({
        feature: {
          publish: event => {
            if (event.kind === "progress") {
              publishPackageChangesEvent(eventSink, {
                kind: "Progress",
                progress: event.progress.value,
                row: null,
                failure: null,
              });
            } else if (event.kind === "durable") {
              publishPackageChangesEvent(eventSink, event.durable.value);
            }
            return undefined;
          },
        },
        diagnostic: { report: reportDiagnostic },
      });
      const started = authority.startWithId(
        operationId,
        () => session.start(decodedRequest.value, adapter),
      );
      if (started.kind === "rejected") {
        session.dispose();
        throw new Error(
          `Package Activity could not start: ${
            startFailureReason(started.reason)
          }.`);
      }
      active.set(operationId, { handle: started.handle, session });
      try {
        const outcome = await started.handle.outcome;
        await started.handle.quiesced;
        if (outcome.kind === "succeeded") {
          return {
            version: 1,
            kind: "Succeeded",
            inspection: outcome.value,
            failureKind: null,
            error: null,
            diagnostic: null,
            reason: null,
          } satisfies BrowserPackageChangesResult;
        }
        if (outcome.kind === "failed") {
          return {
            version: 1,
            kind: "Failed",
            inspection: null,
            failureKind: outcome.error.failureKind,
            error: outcome.error.error,
            diagnostic: outcome.error.diagnostic,
            reason: null,
          } satisfies BrowserPackageChangesResult;
        }
        return {
          version: 1,
          kind: "Canceled",
          inspection: null,
          failureKind: null,
          error: null,
          diagnostic: null,
          reason: outcome.reason,
        } satisfies BrowserPackageChangesResult;
      } finally {
        active.delete(operationId);
        session.dispose();
      }
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
  const adapter = registerEngineWorkerCanaryAdapter(host);
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
    BrowserTypeCodeView,
    EngineWorkerTypeSourceFailure,
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
  const readinessAdapter = registerEngineWorkerCanaryAdapter(host);
  const readinessSession = authority.page.createSession<
    string, string, string, string, WorkerRuntimePreparationError
  >({
    feature: { publish: () => undefined },
    diagnostic: { report: options.operationDiagnostic },
  });
  const readiness = readinessSession.start("", readinessAdapter);
  const ready = (async () => {
    if (readiness.kind !== "started") {
      throw new Error(
        `Engine readiness probe refused: ${startFailureReason(readiness.reason)}.`);
    }
    const outcome = await readiness.handle.outcome;
    await readiness.handle.quiesced;
    if (outcome.kind === "succeeded") return;
    if (outcome.kind === "failed") {
      throw new Error(`Engine readiness probe failed: ${outcome.error}`);
    }
    throw new Error(`Engine readiness probe was canceled: ${outcome.reason}.`);
  })();
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
  const packageQuery = bindPackageQueryFacade(
    registerEngineWorkerPackageQueryAdapter(host),
    options.operationDiagnostic,
    authority,
  );
  const packageChanges = bindPackageChangesFacade(
    registerEngineWorkerPackageChangesAdapter(host),
    options.operationDiagnostic,
    authority,
  );
  const identity = startup.host.buildIdentity();
  // The eager startup read may settle before the page awaits it. Observe that
  // rejection now; the retained promise still rejects to the Build consumer.
  void identity.catch(() => undefined);
  const client: EngineClient = {
    host: {
      buildIdentity: () => identity,
    },
    package: {
      ...ordinary.package,
      ...startup.package,
      ...packageQuery,
      ...packageChanges,
    },
    metadata: ordinary.metadata,
    analysis: ordinary.analysis,
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
    ready,
    dispose() {
      readinessSession.dispose();
      packageChanges.dispose();
      packageQuery.dispose();
      typeSource.dispose();
      host.dispose();
    },
  };
}
