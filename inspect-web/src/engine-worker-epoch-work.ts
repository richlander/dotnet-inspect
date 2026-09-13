import {
  engineWorkerManagedProducerAllowance,
  engineWorkerManagedProducerClass,
} from "./engine-worker-contract.ts";
import type { WorkerRuntimeRealm } from "./worker-runtime-realm.ts";

export type EngineWorkerEpochWorkExports = Pick<
  typeof import("/inspect-web-host.js"),
  "registerEpochWorkReporter" | "drainEpochWorkReporter" | "unregisterEpochWorkReporter"
>;

type EpochWorkRealm = Pick<
  WorkerRuntimeRealm<unknown, unknown>,
  "startEpochWork" | "finishEpochWork" | "fail"
>;

export function createEngineWorkerBootstrap(
  startEngine: (origin: string) => Promise<void>,
  loadHost: () => Promise<EngineWorkerEpochWorkExports>,
  realm: EpochWorkRealm,
) {
  const allowance = JSON.stringify(engineWorkerManagedProducerAllowance);
  let registered: EngineWorkerEpochWorkExports | undefined;
  let closed = false;
  let closing: Promise<void> | undefined;

  function report(deliver: () => boolean): void {
    try {
      if (!deliver()) throw new Error("Worker rejected managed epoch-work reporting.");
    } catch (error: unknown) {
      realm.fail(error);
      throw error;
    }
  }

  function requireOpen(): void {
    if (closed) throw new Error("Worker epoch-reporter admission is closed.");
  }

  async function drainRegistered(): Promise<void> {
    // Failure can be declared inside a managed callback. Drain only after
    // that synchronous interop stack unwinds; finish callbacks remain live.
    await Promise.resolve();
    if (registered === undefined) return;
    try {
      await registered.drainEpochWorkReporter();
    } finally {
      registered.unregisterEpochWorkReporter();
    }
  }

  return {
    bootstrap: async (origin: string): Promise<void> => {
      await startEngine(origin);
      requireOpen();
      const host = await loadHost();
      requireOpen();
      host.registerEpochWorkReporter(
        allowance,
        (sequence, advertised) => {
          report(() => {
            requireOpen();
            if (advertised !== allowance)
              throw new Error("Managed epoch-work allowance differs from its Worker registration.");
            return realm.startEpochWork(
              engineWorkerManagedProducerClass,
              sequence,
              engineWorkerManagedProducerAllowance,
            );
          });
        },
        sequence => { report(() => realm.finishEpochWork(sequence)); },
      );
      registered = host;
    },
    close(): Promise<void> {
      closed = true;
      closing ??= drainRegistered();
      return closing;
    },
  };
}
