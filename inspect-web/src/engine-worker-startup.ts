import type { EngineClient } from "./engine-client.ts";
import {
  createOperationAuthorityPage,
  type OperationDiagnostic,
  type OperationAuthorityPage,
} from "./operation-authority.ts";
import {
  engineWorkerBoundaryErrors,
  engineWorkerDiagnostic,
  engineWorkerText,
} from "./engine-worker-contract.ts";
import {
  encodeEngineStartupResult,
  engineStartupInput,
  engineStartupOperations,
} from "./engine-worker-startup-contract.ts";
import type { WorkerRuntimeHost, WorkerRuntimePreparationError } from "./worker-runtime-core.ts";
import type { BoundedPayloadDecoder } from "./worker-runtime-protocol.ts";
import type { WorkerOperationCatalog } from "./worker-runtime-realm.ts";

export interface EngineStartupClient {
  readonly host: Pick<EngineClient["host"], "buildIdentity">;
  readonly catalog: Pick<EngineClient["catalog"], "inspectVocabulary" | "listHomeDemos">;
  readonly package: Pick<
    EngineClient["package"],
    "listPackageActivityEcosystems" | "listPackageQueryCatalog"
  >;
}

interface StartupReads {
  readonly buildIdentity: EngineStartupClient["host"]["buildIdentity"];
  readonly inspectVocabulary: EngineStartupClient["catalog"]["inspectVocabulary"];
  readonly listHomeDemos: EngineStartupClient["catalog"]["listHomeDemos"];
  readonly listPackageActivityEcosystems:
    EngineStartupClient["package"]["listPackageActivityEcosystems"];
  readonly listPackageQueryCatalog: EngineStartupClient["package"]["listPackageQueryCatalog"];
}

interface StartupOperation<TValue> {
  readonly kind: string;
  readonly value: BoundedPayloadDecoder<TValue>;
}

export function registerEngineWorkerStartupOperations(
  operations: WorkerOperationCatalog,
  reads: StartupReads,
): void {
  function register<TValue>(operation: StartupOperation<TValue>, read: () => Promise<TValue>) {
    operations.register({
      kind: operation.kind,
      allowance: { kind: "unbounded" },
      input: engineStartupInput,
      rejectInvalidPayload: failure => ({ error: failure.message, diagnostic: failure.message }),
      async invoke() {
        try {
          return { kind: "succeeded", value: encodeEngineStartupResult(await read()) };
        } catch (error: unknown) {
          const message = engineWorkerDiagnostic(error);
          return { kind: "failed", failureKind: "unexpected", error: message, diagnostic: message };
        }
      },
    });
  }
  register(engineStartupOperations.buildIdentity, reads.buildIdentity);
  register(engineStartupOperations.inspectVocabulary, reads.inspectVocabulary);
  register(engineStartupOperations.listHomeDemos, reads.listHomeDemos);
  register(
    engineStartupOperations.listPackageActivityEcosystems,
    reads.listPackageActivityEcosystems);
  register(engineStartupOperations.listPackageQueryCatalog, reads.listPackageQueryCatalog);
}

export function bindEngineWorkerStartupClient(
  host: WorkerRuntimeHost<string, string>,
  reportDiagnostic: (diagnostic: OperationDiagnostic) => undefined,
  page: OperationAuthorityPage = createOperationAuthorityPage(),
): EngineStartupClient {
  const epoch = host.snapshot().epochToken;
  if (epoch === null) throw new Error("Start a Worker epoch before binding startup reads.");

  function bind<TValue>(operation: StartupOperation<TValue>): () => Promise<TValue> {
    const adapter = host.registerOperation({
      kind: operation.kind,
      allowance: { kind: "unbounded" },
      encodeInput: engineStartupInput.decode,
      value: operation.value,
      error: engineWorkerText,
      diagnostic: engineWorkerText,
      progress: engineWorkerText,
      mapPreparationError: error => error,
      boundaryErrors: engineWorkerBoundaryErrors,
    });
    return async () => {
      if (host.snapshot().epochToken !== epoch)
        throw new Error("Startup client belongs to a closed Worker epoch.");
      // A session is a replacement slot. Independent reads must not share one.
      const session = page.createSession<null, TValue, string, string, WorkerRuntimePreparationError>({
        feature: { publish: () => undefined },
        diagnostic: { report: reportDiagnostic },
      });
      try {
        const started = session.start(null, adapter);
        if (started.kind === "rejected") {
          const reason = started.reason.kind === "producer-rejected"
            ? started.reason.error.kind : started.reason.kind;
          throw new Error(`Startup read could not start: ${reason}.`);
        }
        const outcome = await started.handle.outcome;
        if (outcome.kind === "succeeded") return outcome.value;
        if (outcome.kind === "failed") throw new Error(outcome.error);
        throw new Error(`Startup read canceled: ${outcome.reason}.`);
      } finally {
        session.dispose();
      }
    };
  }
  const inspectVocabulary = bind(engineStartupOperations.inspectVocabulary);
  type Vocabulary = Awaited<ReturnType<typeof inspectVocabulary>>;
  let cachedVocabulary: Vocabulary | undefined;
  let pendingVocabulary: Promise<Vocabulary> | undefined;
  function requireOpenEpoch(): void {
    const snapshot = host.snapshot();
    if (snapshot.epochToken !== epoch)
      throw new Error("Startup client belongs to a closed Worker epoch.");
    if (snapshot.phase === "draining")
      throw new Error("Startup read could not start: worker-restarted.");
    if (snapshot.phase === "closed" || snapshot.phase === "absent")
      throw new Error("Startup read could not start: epoch-unavailable.");
  }
  async function readVocabulary(): Promise<Vocabulary> {
    requireOpenEpoch();
    if (cachedVocabulary !== undefined)
      return cachedVocabulary;
    pendingVocabulary ??= inspectVocabulary()
      .then(value => {
        cachedVocabulary = value;
        return value;
      })
      .finally(() => {
        pendingVocabulary = undefined;
      });
    return await pendingVocabulary;
  }
  return {
    host: { buildIdentity: bind(engineStartupOperations.buildIdentity) },
    catalog: {
      inspectVocabulary: readVocabulary,
      listHomeDemos: bind(engineStartupOperations.listHomeDemos),
    },
    package: {
      listPackageActivityEcosystems:
        bind(engineStartupOperations.listPackageActivityEcosystems),
      listPackageQueryCatalog: bind(engineStartupOperations.listPackageQueryCatalog),
    },
  };
}
