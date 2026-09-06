import { startEngine } from "./engine-facades.ts";
import {
  createEngineWorkerProducerClasses,
  engineWorkerCanaryKind,
  engineWorkerDiagnostic,
  engineWorkerText,
} from "./engine-worker-contract.ts";
import { createEngineWorkerBootstrap } from "./engine-worker-epoch-work.ts";
import { WorkerOperationCatalog, WorkerRuntimeRealm } from "./worker-runtime-realm.ts";

const operations = new WorkerOperationCatalog();
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
const realm = new WorkerRuntimeRealm({
  bootstrap: { decoder: engineWorkerText, bootstrap: bootstrap.bootstrap },
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
