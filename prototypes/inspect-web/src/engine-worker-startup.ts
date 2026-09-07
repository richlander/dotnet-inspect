import type { EngineClient } from "./engine-client.ts";
import {
  createOperationAuthorityPage,
  type OperationDiagnostic,
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
  readonly catalog: Pick<EngineClient["catalog"], "listVocabulary" | "listHomeDemos">;
  readonly package: Pick<EngineClient["package"], "listPackageQueryFacets" | "listGalleryDiscoveryCatalog">;
}

interface StartupReads {
  readonly buildIdentity: EngineStartupClient["host"]["buildIdentity"];
  readonly listVocabulary: EngineStartupClient["catalog"]["listVocabulary"];
  readonly listHomeDemos: EngineStartupClient["catalog"]["listHomeDemos"];
  readonly listPackageQueryFacets: EngineStartupClient["package"]["listPackageQueryFacets"];
  readonly listGalleryDiscoveryCatalog: EngineStartupClient["package"]["listGalleryDiscoveryCatalog"];
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
  register(engineStartupOperations.listVocabulary, reads.listVocabulary);
  register(engineStartupOperations.listHomeDemos, reads.listHomeDemos);
  register(engineStartupOperations.listPackageQueryFacets, reads.listPackageQueryFacets);
  register(engineStartupOperations.listGalleryDiscoveryCatalog, reads.listGalleryDiscoveryCatalog);
}

export function bindEngineWorkerStartupClient(
  host: WorkerRuntimeHost<string, string>,
  reportDiagnostic: (diagnostic: OperationDiagnostic) => undefined,
): EngineStartupClient {
  const epoch = host.snapshot().epochToken;
  if (epoch === null) throw new Error("Start a Worker epoch before binding startup reads.");
  const page = createOperationAuthorityPage();

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
  return {
    host: { buildIdentity: bind(engineStartupOperations.buildIdentity) },
    catalog: {
      listVocabulary: bind(engineStartupOperations.listVocabulary),
      listHomeDemos: bind(engineStartupOperations.listHomeDemos),
    },
    package: {
      listPackageQueryFacets: bind(engineStartupOperations.listPackageQueryFacets),
      listGalleryDiscoveryCatalog: bind(engineStartupOperations.listGalleryDiscoveryCatalog),
    },
  };
}
