import {
  type OperationAuthorityPage,
  type OperationDiagnostic,
  type OperationOutcome,
} from "./operation-authority.ts";
import {
  engineWorkerBoundaryErrors,
  engineWorkerDiagnostic,
  engineWorkerText,
} from "./engine-worker-contract.ts";
import type {
  WorkerRuntimeHost,
  WorkerRuntimePreparationError,
} from "./worker-runtime-core.ts";
import type {
  BoundedPayloadDecoder,
} from "./worker-runtime-protocol.ts";
import {
  type WorkerOperationCatalog,
} from "./worker-runtime-realm.ts";

const engineWorkerCpuKind = "runtime-managed-cpu-canary";

const engineWorkerCpuCacheKey = "runtime-managed-cpu-canary-invocations";

export interface EngineWorkerCpuProgress {
  readonly kind: "entered";
  readonly at: number;
}

export interface EngineWorkerCpuResult {
  readonly invocation: number;
  readonly checksum: string;
  readonly startedAt: number;
  readonly completedAt: number;
}

export type EngineWorkerCpuEntry =
  | EngineWorkerCpuProgress
  | {
      readonly kind: "closed";
      readonly outcome: OperationOutcome<EngineWorkerCpuResult, string>;
    };

export interface EngineWorkerCpuFacade {
  managedCpuCanary(): string;
}

const engineWorkerCpuInput: BoundedPayloadDecoder<null> = {
  decode: value => value === null
    ? { kind: "decoded", value }
    : { kind: "rejected", reason: "invalid", message: "Managed CPU canary takes no arguments." },
};

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}

function exactKeys(value: Record<string, unknown>, keys: readonly string[]): boolean {
  const actual = Object.keys(value);
  return actual.length === keys.length && keys.every(key => Object.hasOwn(value, key));
}

const engineWorkerCpuProgress: BoundedPayloadDecoder<EngineWorkerCpuProgress> = {
  decode(value) {
    if (!isRecord(value)
      || !exactKeys(value, ["kind", "at"])
      || value.kind !== "entered"
      || typeof value.at !== "number"
      || !Number.isFinite(value.at)) {
      return { kind: "rejected", reason: "invalid", message: "Invalid managed CPU entry marker." };
    }
    return { kind: "decoded", value: { kind: "entered", at: value.at } };
  },
};

const engineWorkerCpuResult: BoundedPayloadDecoder<EngineWorkerCpuResult> = {
  decode(value) {
    if (!isRecord(value)
      || !exactKeys(value, ["invocation", "checksum", "startedAt", "completedAt"])
      || typeof value.invocation !== "number"
      || !Number.isSafeInteger(value.invocation)
      || value.invocation < 1
      || typeof value.checksum !== "string"
      || !/^[0-9a-f]{8}$/.test(value.checksum)
      || typeof value.startedAt !== "number"
      || !Number.isFinite(value.startedAt)
      || typeof value.completedAt !== "number"
      || !Number.isFinite(value.completedAt)
      || value.completedAt < value.startedAt) {
      return { kind: "rejected", reason: "invalid", message: "Invalid managed CPU canary result." };
    }
    return {
      kind: "decoded",
      value: {
        invocation: value.invocation,
        checksum: value.checksum,
        startedAt: value.startedAt,
        completedAt: value.completedAt,
      },
    };
  },
};

function nextInvocation(cache: {
  get(key: string): unknown;
  set(key: string, value: unknown): boolean;
}): number {
  const previous = cache.get(engineWorkerCpuCacheKey);
  if (previous !== undefined
    && (typeof previous !== "number" || !Number.isSafeInteger(previous) || previous < 1)) {
    throw new Error("Managed CPU canary cache is invalid.");
  }
  const invocation = previous === undefined ? 1 : previous + 1;
  if (!Number.isSafeInteger(invocation))
    throw new Error("Managed CPU canary invocation identity is exhausted.");
  if (!cache.set(engineWorkerCpuCacheKey, invocation))
    throw new Error("Managed CPU canary cache is unavailable.");
  return invocation;
}

export function registerEngineWorkerCpuOperation(
  operations: WorkerOperationCatalog,
  facade: () => Promise<EngineWorkerCpuFacade>,
  now: () => number = () => performance.timeOrigin + performance.now(),
): void {
  operations.register({
    kind: engineWorkerCpuKind,
    allowance: { kind: "unbounded" },
    input: engineWorkerCpuInput,
    rejectInvalidPayload: failure => ({
      error: failure.message,
      diagnostic: failure.message,
    }),
    async invoke(_input, context) {
      try {
        const host = await facade();
        const invocation = nextInvocation(context.cache);
        const startedAt = now();
        if (!context.reportProgress({ kind: "entered", at: startedAt })) {
          const message = "Managed CPU canary entry could not be reported.";
          return {
            kind: "failed",
            failureKind: "unexpected",
            error: message,
            diagnostic: message,
          };
        }
        const checksum = host.managedCpuCanary();
        return {
          kind: "succeeded",
          value: { invocation, checksum, startedAt, completedAt: now() },
        };
      } catch (error: unknown) {
        const message = engineWorkerDiagnostic(error);
        return {
          kind: "failed",
          failureKind: "unexpected",
          error: message,
          diagnostic: message,
        };
      }
    },
  });
}

function deferred<T>() {
  let resolve!: (value: T) => void;
  const promise = new Promise<T>(accept => { resolve = accept; });
  return { promise, resolve };
}

export function bindEngineWorkerCpuProbe(
  host: WorkerRuntimeHost<string, string>,
  page: OperationAuthorityPage,
  reportDiagnostic: (diagnostic: OperationDiagnostic) => undefined,
) {
  const adapter = host.registerOperation({
    kind: engineWorkerCpuKind,
    allowance: { kind: "unbounded" },
    encodeInput: engineWorkerCpuInput.decode,
    value: engineWorkerCpuResult,
    error: engineWorkerText,
    diagnostic: engineWorkerText,
    progress: engineWorkerCpuProgress,
    mapPreparationError: error => error,
    boundaryErrors: engineWorkerBoundaryErrors,
  });
  const activeSessions = new Set<() => void>();

  return {
    start() {
      const entry = deferred<EngineWorkerCpuEntry>();
      let entrySettled = false;
      const session = page.createSession<
        null,
        EngineWorkerCpuResult,
        string,
        EngineWorkerCpuProgress,
        WorkerRuntimePreparationError
      >({
        feature: {
          publish(event) {
            if (!entrySettled && event.kind === "progress") {
              entrySettled = true;
              entry.resolve(event.progress.value);
            }
            return undefined;
          },
        },
        diagnostic: { report: reportDiagnostic },
      });
      let released = false;
      const release = () => {
        if (released) return;
        released = true;
        activeSessions.delete(release);
        session.dispose();
      };
      activeSessions.add(release);
      const result = session.start(null, adapter);
      if (result.kind !== "started") {
        release();
        return result;
      }
      void result.handle.outcome.then(outcome => {
        if (!entrySettled) {
          entrySettled = true;
          entry.resolve({ kind: "closed", outcome });
        }
        return undefined;
      });
      void result.handle.quiesced.then(release);
      return { ...result, entry: entry.promise };
    },
    dispose() {
      for (const release of activeSessions) release();
    },
  };
}
