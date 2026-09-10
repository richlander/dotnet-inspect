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
  registerEngineWorkerPackageQueryOperation,
  type EngineWorkerPackageQueryFacade,
} from "./engine-worker-package-query.ts";
import {
  registerEngineWorkerTypeSourceOperation,
  type EngineWorkerTypeSourceFacade,
} from "./engine-worker-source.ts";
import { registerEngineWorkerStartupOperations } from "./engine-worker-startup.ts";
import { WorkerOperationCatalog, WorkerRuntimeRealm } from "./worker-runtime-realm.ts";
import { registerEngineWorkerOperations, type EngineFacades } from "./engine-worker-operations.ts";
import { registerEngineWorkerComparisonOperations } from "./engine-worker-comparison.ts";

const operations = new WorkerOperationCatalog();
let facades: EngineFacades | undefined;
registerEngineWorkerOperations(operations, () => {
  if (!facades) throw new Error("Engine facades are unavailable before Worker readiness.");
  return facades;
});
registerEngineWorkerComparisonOperations(operations, () => {
  if (!facades) throw new Error("Comparison facade is unavailable before Worker readiness.");
  return facades.source;
});
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
let sourceFacade: EngineWorkerTypeSourceFacade | undefined;
registerEngineWorkerTypeSourceOperation(operations, () => {
  if (sourceFacade === undefined)
    throw new Error("Type Source facade is unavailable before Worker readiness.");
  return sourceFacade;
});
let packageQueryFacade: EngineWorkerPackageQueryFacade | undefined;
registerEngineWorkerPackageQueryOperation(operations, () => {
  if (packageQueryFacade === undefined) {
    throw new Error(
      "Package Query facade is unavailable before Worker readiness.");
  }
  return packageQueryFacade;
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
  const [host, packageApi, metadata, analysis, source, callGraph, catalog] = await Promise.all([
    import("/inspect-web-host.js"),
    import("/inspect-web-package.js"),
    import("/inspect-web-metadata.js"),
    import("/inspect-web-analysis.js"),
    import("/inspect-web-source.js"),
    import("/inspect-web-call-graph.js"),
    import("/inspect-web-catalog.js"),
  ]);
  facades = { host, package: packageApi, metadata, analysis, source, callGraph, catalog };
  sourceFacade = source;
  packageQueryFacade = packageApi;
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
