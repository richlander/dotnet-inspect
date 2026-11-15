import type { BrowserPerformanceAnalysisResult } from "./facades/inspect-web-analysis.d.ts";
import {
  engineWorkerLibraryPerformanceDurableEvent,
  type EngineWorkerLibraryPerformanceDurableEvent,
} from "./engine-worker-library-performance.ts";
import type {
  LibraryPerformanceDataSource,
  LibraryPerformanceTerminalResult,
} from "./library-performance.ts";

export interface BrowserLibraryPerformanceEngine {
  cancel(operationId: string, reason: string): void;
  run(
    operationId: string,
    packageId: string,
    version: string,
    targetFramework: string,
    assemblyName: string,
    eventSink: unknown,
  ): Promise<BrowserPerformanceAnalysisResult>;
}

export interface BrowserLibraryPerformanceDataSourceOptions {
  readonly createOperationId?: () => string;
}

export function createBrowserLibraryPerformanceDataSource(
  engine: BrowserLibraryPerformanceEngine,
  options: BrowserLibraryPerformanceDataSourceOptions = {},
): LibraryPerformanceDataSource {
  const createOperationId =
    options.createOperationId ?? (() => globalThis.crypto.randomUUID());
  return {
    async run(request, onItem, abortSignal) {
      if (abortSignal.aborted) {
        return canceledResult(abortSignal.reason);
      }
      const operationId = createOperationId();
      if (!operationId) {
        throw new Error(
          "The Browser Library Performance operation ID allocator returned no ID.");
      }

      let flushScheduled = false;
      let observerFailure: unknown = null;
      const pending: EngineWorkerLibraryPerformanceDurableEvent[] = [];
      const flush = () => {
        flushScheduled = false;
        const events = pending.splice(0);
        try {
          for (const event of events) {
            onItem(event.item);
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
              "The Browser Library Performance event payload was not JSON text.");
          }
          try {
            pending.push(parseLibraryPerformanceEvent(value));
          } catch (error: unknown) {
            engine.cancel(operationId, "feature-observer-failed");
            throw error instanceof Error
              ? error
              : new Error(
                  "The Library Performance event decoder failed with a non-Error value.");
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
          request.packageId,
          request.version,
          request.targetFramework,
          request.assemblyName,
          eventSink);
        flush();
        if (observerFailure !== null) {
          throw observerFailure instanceof Error
            ? observerFailure
            : new Error(
                "The Library Performance feature observer failed with a non-Error value.");
        }
        if (result.version !== 1) {
          throw new Error(
            "The Browser Library Performance result version is unsupported.");
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
              ?? "The Browser Library Performance operation failed without an error.",
            diagnostic: result.diagnostic,
          };
        }
        if (result.kind !== "Succeeded" || result.summary === null) {
          throw new TypeError(
            "The Browser Library Performance result had no terminal summary.");
        }
        return {
          kind: "succeeded",
          summary: result.summary,
        };
      } finally {
        abortSignal.removeEventListener("abort", cancel);
      }
    },
  };
}

function parseLibraryPerformanceEvent(
  json: string,
): EngineWorkerLibraryPerformanceDurableEvent {
  const parsed: unknown = JSON.parse(json);
  const decoded = engineWorkerLibraryPerformanceDurableEvent.decode(parsed);
  if (decoded.kind === "rejected") {
    throw new TypeError(decoded.message, { cause: decoded.cause });
  }
  return decoded.value;
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

function canceledResult(reason: unknown): LibraryPerformanceTerminalResult {
  return {
    kind: "canceled",
    reason: cancellationReason(reason),
  };
}
