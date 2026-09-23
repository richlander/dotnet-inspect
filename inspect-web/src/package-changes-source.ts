import type {
  BrowserPackageChangesInspection,
  BrowserPackageChangesPackageSetCatalog,
  BrowserPackageChangesPackageSetDescriptor,
  BrowserPackageChangesRequest,
  BrowserPackageChangesResult,
} from "./facades/inspect-web-package.d.ts";
import {
  decodeEngineWorkerPackageChangesEvent,
  type EngineWorkerPackageChangesEvent,
} from "./engine-worker-package-changes.ts";
import type {
  PackageChangesDataSource,
  PackageChangesTerminalResult,
} from "./package-changes.ts";

export interface BrowserPackageChangesEngine {
  cancel(operationId: string, reason: string): void;
  run(
    operationId: string,
    request: BrowserPackageChangesRequest,
    eventSink: unknown,
  ): Promise<BrowserPackageChangesResult>;
}

export interface BrowserPackageChangesDataSourceOptions {
  readonly createOperationId?: () => string;
  readonly onInspection?: (
    inspection: BrowserPackageChangesInspection | null,
  ) => void;
}

export function packageChangesPackageSets(
  catalog: BrowserPackageChangesPackageSetCatalog,
): BrowserPackageChangesPackageSetDescriptor[] {
  if (catalog.version !== 1) {
    throw new Error(
      `Package Activity package-set catalog version '${catalog.version}' is unsupported.`);
  }
  if (catalog.packageSets.length === 0) {
    throw new Error("Package Activity requires at least one product package set.");
  }
  const ids = new Set<string>();
  let priorOrder: number | null = null;
  return catalog.packageSets.map(packageSet => {
    if (!packageSet.id.trim()
        || !packageSet.title.trim()
        || !packageSet.summary.trim()
        || !Number.isSafeInteger(packageSet.order)) {
      throw new TypeError(
        "The Package Activity package-set catalog contains an invalid descriptor.");
    }
    if (ids.has(packageSet.id)) {
      throw new TypeError(
        `The Package Activity package-set catalog repeats '${packageSet.id}'.`);
    }
    ids.add(packageSet.id);
    if (priorOrder !== null && packageSet.order <= priorOrder) {
      throw new TypeError(
        "The Package Activity package-set catalog is not in product order.");
    }
    priorOrder = packageSet.order;
    return { ...packageSet };
  });
}

export function createBrowserPackageChangesDataSource(
  engine: BrowserPackageChangesEngine,
  options: BrowserPackageChangesDataSourceOptions = {},
): PackageChangesDataSource {
  const createOperationId =
    options.createOperationId ?? (() => globalThis.crypto.randomUUID());
  const onInspection = options.onInspection ?? (() => {});
  return {
    async run(request, onProgress, onRow, onFailure, abortSignal) {
      if (abortSignal.aborted) {
        return canceledResult(abortSignal.reason);
      }
      const operationId = createOperationId();
      if (!operationId) {
        throw new Error(
          "The Browser Package Activity operation ID allocator returned no ID.");
      }
      onInspection(null);

      let flushScheduled = false;
      let observerFailure: unknown = null;
      const pending: EngineWorkerPackageChangesEvent[] = [];
      const flush = () => {
        flushScheduled = false;
        const events = pending.splice(0);
        try {
          for (const event of events) {
            if (event.kind === "Progress") {
              if (event.progress === null) {
                throw new TypeError(
                  "A Package Activity progress event contained no progress.");
              }
              onProgress(event.progress);
            } else if (event.kind === "Row") {
              if (event.row === null) {
                throw new TypeError(
                  "A Package Activity row event contained no row.");
              }
              onRow(event.row);
            } else if (event.kind === "Failure") {
              if (event.failure === null) {
                throw new TypeError(
                  "A Package Activity failure event contained no failure.");
              }
              onFailure(event.failure);
            } else {
              throw new TypeError(
                "The Package Activity decoder returned an unsupported event.");
            }
          }
        } catch (error: unknown) {
          observerFailure = error;
          pending.length = 0;
          engine.cancel(operationId, "feature-observer-failed");
        }
      };
      const scheduleFlush = () => {
        if (flushScheduled) return;
        flushScheduled = true;
        queueMicrotask(flush);
      };
      const eventSink: Record<string, unknown> = {};
      Object.defineProperty(eventSink, "event", {
        set(value: unknown) {
          if (typeof value !== "string") {
            engine.cancel(operationId, "feature-observer-failed");
            throw new TypeError(
              "The Browser Package Activity event payload was not JSON text.");
          }
          try {
            pending.push(parsePackageChangesEvent(value));
          } catch (error: unknown) {
            engine.cancel(operationId, "feature-observer-failed");
            throw error instanceof Error
              ? error
              : new Error(
                  "The Package Activity event decoder failed with a non-Error value.");
          }
          scheduleFlush();
        },
      });

      const cancel = () =>
        engine.cancel(operationId, cancellationReason(abortSignal.reason));
      abortSignal.addEventListener("abort", cancel, { once: true });
      try {
        const result = await engine.run(
          operationId,
          request,
          eventSink);
        flush();
        if (observerFailure !== null) {
          throw observerFailure instanceof Error
            ? observerFailure
            : new Error(
                "The Package Activity feature observer failed with a non-Error value.");
        }
        if (result.version !== 1) {
          throw new Error(
            "The Browser Package Activity result version is unsupported.");
        }
        if (result.kind === "Canceled") {
          return {
            kind: "canceled",
            reason: result.reason ?? "user",
          };
        }
        if (result.kind === "Failed") {
          return {
            kind: "failed",
            error: result.error
              ?? "The Browser Package Activity operation failed without an error.",
            diagnostic: result.diagnostic,
          };
        }
        if (result.kind !== "Succeeded" || result.inspection === null) {
          throw new TypeError(
            "The Browser Package Activity result had no terminal inspection.");
        }
        onInspection(result.inspection);
        return {
          kind: "succeeded",
          inspection: result.inspection,
        };
      } finally {
        abortSignal.removeEventListener("abort", cancel);
      }
    },
  };
}

function parsePackageChangesEvent(
  json: string,
): EngineWorkerPackageChangesEvent {
  const parsed: unknown = JSON.parse(json);
  return decodeEngineWorkerPackageChangesEvent(parsed);
}

function cancellationReason(reason: unknown): string {
  switch (reason) {
    case "disposed":
    case "superseded":
    case "user":
      return reason;
    default:
      return "user";
  }
}

function canceledResult(reason: unknown): PackageChangesTerminalResult {
  return {
    kind: "canceled",
    reason: cancellationReason(reason),
  };
}
