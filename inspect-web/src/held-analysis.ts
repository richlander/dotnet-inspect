// A background analysis adopter that holds one result per exact request for
// the page: it queues a request unless its result is held or in flight, runs
// it through an operation authority session, and records a ready value, a
// visible failure, or nothing when canceled. Owned by
// docs/design/inspect-web-background-analysis.md.

import type {
  BackgroundAnalysisOrder,
  BackgroundAnalysisQueue,
  BackgroundAnalysisRun,
} from "./background-analysis.ts";
import type {
  OperationAuthorityPage,
  OperationCancelReason,
  OperationDiagnostic,
  OperationId,
  OperationProducerAdapter,
} from "./operation-authority.ts";

export type HeldAnalysisEntry<TValue> =
  | { readonly status: "loading" }
  | { readonly status: "ready"; readonly value: TValue }
  | { readonly status: "failed"; readonly error: string };

/** What one producer result means for the held entry. */
export type HeldAnalysisOutcome<TValue> =
  | { readonly kind: "ready"; readonly value: TValue }
  | { readonly kind: "failed"; readonly error: string }
  | { readonly kind: "canceled" };

export interface HeldAnalysisDependencies<TRequest, TValue> {
  readonly name: string;
  readonly order: BackgroundAnalysisOrder;
  readonly queue: BackgroundAnalysisQueue;
  readonly operationAuthority: OperationAuthorityPage;
  key(request: TRequest): string;
  query(operationId: OperationId, request: TRequest): Promise<unknown>;
  /** Reads one producer result; throws when it is malformed. */
  read(result: unknown, request: TRequest): HeldAnalysisOutcome<TValue>;
  cancel(operationId: OperationId, reason: OperationCancelReason): void;
  describeError(error: unknown): string;
  reportOperationDiagnostic(diagnostic: OperationDiagnostic): undefined;
  render(): void;
}

export interface HeldAnalysis<TRequest, TValue> {
  /** Queues the request unless its result is held or in flight. */
  ensure(request: TRequest, isRelevant: () => boolean): void;
  retry(request: TRequest, isRelevant: () => boolean): void;
  entry(request: TRequest): HeldAnalysisEntry<TValue> | null;
}

export function createHeldAnalysis<TRequest, TValue>(
  dependencies: HeldAnalysisDependencies<TRequest, TValue>,
): HeldAnalysis<TRequest, TValue> {
  type Outcome = HeldAnalysisOutcome<TValue>;
  const entries = new Map<string, HeldAnalysisEntry<TValue>>();
  const session = dependencies.operationAuthority.createSession<
    TRequest,
    Outcome,
    unknown,
    never,
    never
  >({
    feature: { publish: () => undefined },
    diagnostic: {
      report: diagnostic => dependencies.reportOperationDiagnostic(diagnostic),
    },
  });
  const adapter: OperationProducerAdapter<
    TRequest,
    Outcome,
    unknown,
    never,
    never
  > = {
    prepare: (identity, request, sink) => {
      let cancellationRequested = false;
      return {
        kind: "prepared",
        binding: {
          requestCancellation: reason => {
            if (!cancellationRequested) {
              cancellationRequested = true;
              dependencies.cancel(identity.id, reason);
            }
            return undefined;
          },
          activate: () => {
            const finish = (outcome: Outcome | null, error?: unknown): undefined => {
              if (outcome) sink.reportTerminal({ kind: "succeeded", value: outcome });
              else sink.reportUnexpectedTerminal(error, error);
              sink.reportQuiesced();
              return undefined;
            };
            let query: Promise<unknown>;
            try {
              query = dependencies.query(identity.id, request);
            } catch (error: unknown) {
              return finish(null, error);
            }
            void query.then(
              result => {
                let outcome: Outcome;
                try {
                  outcome = dependencies.read(result, request);
                } catch (error: unknown) {
                  return finish(null, error);
                }
                return finish(outcome);
              },
              (error: unknown) => finish(null, error));
            return undefined;
          },
          abandon: () => undefined,
        },
      };
    },
  };

  const run = (request: TRequest, key: string): BackgroundAnalysisRun => {
    entries.set(key, { status: "loading" });
    const started = session.start(request, adapter);
    if (started.kind === "rejected") {
      entries.set(key, {
        status: "failed",
        error: `${dependencies.name} could not start: ${started.reason.kind}.`,
      });
      dependencies.render();
      return { settled: Promise.resolve(), cancel: () => undefined };
    }
    const record = (outcome: Awaited<typeof started.handle.outcome>) => {
      if (outcome.kind === "succeeded" && outcome.value.kind === "ready") {
        entries.set(key, { status: "ready", value: outcome.value.value });
      } else if (outcome.kind === "succeeded" && outcome.value.kind === "failed") {
        entries.set(key, { status: "failed", error: outcome.value.error });
      } else if (outcome.kind === "succeeded" || outcome.kind === "canceled") {
        // A canceled request is not held, so a later visit queues it again.
        entries.delete(key);
      } else {
        entries.set(key, {
          status: "failed",
          error: dependencies.describeError(outcome.error),
        });
      }
      dependencies.render();
    };
    return {
      settled: (async () => {
        record(await started.handle.outcome);
        await started.handle.quiesced;
      })(),
      cancel: () => {
        // A render inside another feature's publication cannot cancel; the
        // publication ends before the next microtask.
        if (started.handle.cancel("superseded").kind === "rejected")
          queueMicrotask(() => { started.handle.cancel("superseded"); });
      },
    };
  };

  const ensure = (request: TRequest, isRelevant: () => boolean) => {
    const key = dependencies.key(request);
    if (entries.has(key)) return;
    dependencies.queue.enqueue({
      key,
      order: dependencies.order,
      isRelevant,
      start: () => run(request, key),
    });
  };

  return {
    ensure,
    retry(request, isRelevant) {
      const key = dependencies.key(request);
      if (entries.get(key)?.status === "failed") entries.delete(key);
      ensure(request, isRelevant);
    },
    entry: request => entries.get(dependencies.key(request)) ?? null,
  };
}
