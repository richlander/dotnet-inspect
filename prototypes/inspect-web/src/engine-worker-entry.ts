import { startEngine } from "./engine-facades.ts";
import {
  createEngineWorkerProducerClasses,
  engineWorkerCanaryKind,
  engineWorkerDiagnostic,
  engineWorkerText,
} from "./engine-worker-contract.ts";
import { createEngineWorkerBootstrap } from "./engine-worker-epoch-work.ts";
import { registerEngineWorkerCpuOperation } from "./engine-worker-cpu.ts";
import {
  registerEngineWorkerAnalysisOperations,
  type EngineWorkerAnalysisFacade,
} from "./engine-worker-analysis.ts";
import {
  registerEngineWorkerCallGraphOperations,
  type EngineWorkerCallGraphFacade,
} from "./engine-worker-call-graph.ts";
import {
  registerEngineWorkerCatalogOperations,
  type EngineWorkerCatalogFacade,
} from "./engine-worker-catalog.ts";
import {
  registerEngineWorkerMetadataOperations,
  type EngineWorkerMetadataFacade,
} from "./engine-worker-metadata.ts";
import {
  registerEngineWorkerPackageOperations,
  type EngineWorkerPackageFacade,
} from "./engine-worker-package.ts";
import {
  registerEngineWorkerPackageQueryOperation,
  type EngineWorkerPackageQueryFacade,
} from "./engine-worker-package-query.ts";
import {
  registerEngineWorkerTypeSourceOperation,
  type EngineWorkerTypeSourceFacade,
} from "./engine-worker-source.ts";
import {
  registerEngineWorkerOrdinarySourceOperations,
  type EngineWorkerOrdinarySourceFacade,
} from "./engine-worker-source-ordinary.ts";
import {
  registerEngineWorkerSourceAuthorityOperations,
  type EngineWorkerSourceAuthorityFacade,
} from "./engine-worker-source-authority.ts";
import { registerEngineWorkerStartupOperations } from "./engine-worker-startup.ts";
import { WorkerOperationCatalog, WorkerRuntimeRealm } from "./worker-runtime-realm.ts";

const operations = new WorkerOperationCatalog();
registerEngineWorkerCpuOperation(
  operations,
  () => import("/inspect-web-host.js"),
);
registerEngineWorkerStartupOperations(operations, {
  async buildIdentity() {
    return (await import("/inspect-web-host.js")).buildIdentity();
  },
  async listVocabulary() {
    return (await import("/inspect-web-catalog.js")).listVocabulary();
  },
  async listHomeDemos() {
    return (await import("/inspect-web-catalog.js")).listHomeDemos();
  },
  async listPackageQueryFacets() {
    return (await import("/inspect-web-package.js")).listPackageQueryFacets();
  },
  async listGalleryDiscoveryCatalog() {
    return (await import("/inspect-web-package.js")).listGalleryDiscoveryCatalog();
  },
});
let sourceFacade:
  (EngineWorkerTypeSourceFacade
    & EngineWorkerSourceAuthorityFacade
    & EngineWorkerOrdinarySourceFacade)
  | undefined;
registerEngineWorkerTypeSourceOperation(operations, () => {
  if (sourceFacade === undefined)
    throw new Error("Type Source facade is unavailable before Worker readiness.");
  return sourceFacade;
});
registerEngineWorkerSourceAuthorityOperations(operations, () => {
  if (sourceFacade === undefined) {
    throw new Error(
      "Source authority facade is unavailable before Worker readiness.");
  }
  return sourceFacade;
});
let packageQueryFacade:
  (EngineWorkerPackageQueryFacade & EngineWorkerPackageFacade)
  | undefined;
registerEngineWorkerPackageQueryOperation(operations, () => {
  if (packageQueryFacade === undefined) {
    throw new Error(
      "Package Query facade is unavailable before Worker readiness.");
  }
  return packageQueryFacade;
});
registerEngineWorkerPackageOperations(operations, () => {
  if (packageQueryFacade === undefined) {
    throw new Error(
      "Package facade is unavailable before Worker readiness.");
  }
  return packageQueryFacade;
});
registerEngineWorkerOrdinarySourceOperations(operations, () => {
  if (sourceFacade === undefined) {
    throw new Error(
      "Source facade is unavailable before Worker readiness.");
  }
  return sourceFacade;
});

let metadataFacade: EngineWorkerMetadataFacade | undefined;
registerEngineWorkerMetadataOperations(operations, () => {
  if (metadataFacade === undefined) {
    throw new Error(
      "Metadata facade is unavailable before Worker readiness.");
  }
  return metadataFacade;
});
let analysisFacade: EngineWorkerAnalysisFacade | undefined;
registerEngineWorkerAnalysisOperations(operations, () => {
  if (analysisFacade === undefined) {
    throw new Error(
      "Analysis facade is unavailable before Worker readiness.");
  }
  return analysisFacade;
});
let callGraphFacade: EngineWorkerCallGraphFacade | undefined;
registerEngineWorkerCallGraphOperations(operations, () => {
  if (callGraphFacade === undefined) {
    throw new Error(
      "Call Graph facade is unavailable before Worker readiness.");
  }
  return callGraphFacade;
});
let catalogFacade: EngineWorkerCatalogFacade | undefined;
registerEngineWorkerCatalogOperations(operations, () => {
  if (catalogFacade === undefined) {
    throw new Error(
      "Catalog facade is unavailable before Worker readiness.");
  }
  return catalogFacade;
});
operations.register({
  kind: engineWorkerCanaryKind,
  allowance: { kind: "unbounded" },
  input: engineWorkerText,
  rejectInvalidPayload: failure => ({
    error: failure.message,
    diagnostic: failure.message,
  }),
  invoke: async () => {
    const host = await import("/inspect-web-host.js");
    return { kind: "succeeded", value: await host.asyncLoweringCanary() };
  },
});

let heartbeat: ReturnType<typeof setInterval> | undefined;
const bootstrap = createEngineWorkerBootstrap(
  startEngine,
  () => import("/inspect-web-host.js"),
  {
    startEpochWork: (producerClass, sequence, allowance) =>
      realm.startEpochWork(producerClass, sequence, allowance),
    finishEpochWork: sequence => realm.finishEpochWork(sequence),
    fail: detail => realm.fail(detail),
  },
);
const bootstrapWorker = async (value: string): Promise<void> => {
  await bootstrap.bootstrap(value);
  [
    packageQueryFacade,
    metadataFacade,
    analysisFacade,
    sourceFacade,
    callGraphFacade,
    catalogFacade,
  ] = await Promise.all([
    import("/inspect-web-package.js"),
    import("/inspect-web-metadata.js"),
    import("/inspect-web-analysis.js"),
    import("/inspect-web-source.js"),
    import("/inspect-web-call-graph.js"),
    import("/inspect-web-catalog.js"),
  ]);
};
const realm = new WorkerRuntimeRealm({
  bootstrap: { decoder: engineWorkerText, bootstrap: bootstrapWorker },
  diagnostic: engineWorkerDiagnostic,
  unknownOperationRejection: kind => ({
    error: `Unknown Worker operation: ${kind}`,
    diagnostic: `Unknown Worker operation: ${kind}`,
  }),
  operations,
  producerClasses: createEngineWorkerProducerClasses(),
  post(message) {
    globalThis.postMessage(message, { transfer: [] });
    if (message.kind === "ready") {
      heartbeat = setInterval(
        () => realm.emitHeartbeat(),
        message.idleHeartbeatIntervalMilliseconds,
      );
    }
    if (message.kind === "startup-failed" || message.kind === "epoch-failed") {
      clearInterval(heartbeat);
      void bootstrap.close().catch((error: unknown) => { realm.fail(error); });
    }
  },
});

globalThis.addEventListener("message", (event: MessageEvent<unknown>) => {
  realm.receive(event.data);
});
globalThis.addEventListener("messageerror", () => {
  realm.fail(new Error("Main-thread message could not be deserialized."));
});
