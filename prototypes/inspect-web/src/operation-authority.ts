declare const operationIdBrand: unique symbol;

export type OperationId = string & {
  readonly [operationIdBrand]: "OperationId";
};

export interface OperationIdentity {
  readonly id: OperationId;
  readonly sequence: number;
}

export type OperationCancelReason =
  | "user"
  | "superseded"
  | "disposed"
  | "feature-observer-failed"
  | "timeout"
  | "worker-restarted";

export type OperationTerminalOutcome<TValue, TError> =
  | { readonly kind: "succeeded"; readonly value: TValue }
  | { readonly kind: "failed"; readonly error: TError };

export type OperationOutcome<TValue, TError> =
  | OperationTerminalOutcome<TValue, TError>
  | {
      readonly kind: "canceled";
      readonly reason: OperationCancelReason;
    };

export interface OperationProgress<TProgress> {
  readonly operationId: OperationId;
  readonly value: TProgress;
}

export type OperationControlResult =
  | { readonly kind: "applied" }
  | { readonly kind: "no-op" }
  | {
      readonly kind: "rejected";
      readonly reason: "feature-observer-active";
    };

export interface OperationHandle<TValue, TError> {
  readonly id: OperationId;
  readonly outcome: Promise<OperationOutcome<TValue, TError>>;
  readonly quiesced: Promise<void>;
  cancel(reason?: OperationCancelReason): OperationControlResult;
}

export type OperationStartError<TPrepareError> =
  | { readonly kind: "session-disposed" }
  | { readonly kind: "session-changed" }
  | { readonly kind: "identity-exhausted" }
  | { readonly kind: "feature-observer-active" }
  | {
      readonly kind: "producer-rejected";
      readonly error: TPrepareError;
    };

export type OperationStartResult<TValue, TError, TPrepareError> =
  | {
      readonly kind: "started";
      readonly handle: OperationHandle<TValue, TError>;
    }
  | {
      readonly kind: "rejected";
      readonly reason: OperationStartError<TPrepareError>;
    };

export type OperationFeatureEvent<TValue, TError, TProgress> =
  | {
      readonly kind: "started";
      readonly operation: OperationIdentity;
    }
  | {
      readonly kind: "replaced";
      readonly previousOperationId: OperationId;
      readonly operation: OperationIdentity;
      readonly reason: "superseded";
    }
  | {
      readonly kind: "progress";
      readonly progress: OperationProgress<TProgress>;
    }
  | {
      readonly kind: "terminal";
      readonly operationId: OperationId;
      readonly outcome: OperationTerminalOutcome<TValue, TError>;
    }
  | {
      readonly kind: "canceled";
      readonly operationId: OperationId;
      readonly reason: OperationCancelReason;
    }
  | {
      readonly kind: "disposed";
      readonly operationId: OperationId | null;
    };

export interface OperationFeatureObserver<TValue, TError, TProgress> {
  readonly publish: (
    event: OperationFeatureEvent<TValue, TError, TProgress>,
  ) => undefined;
}

export interface OperationDiagnostic {
  readonly kind:
    | "producer-contract"
    | "producer-callout"
    | "feature-observer";
  readonly operationId: OperationId | null;
  readonly error: unknown;
}

export interface OperationDiagnosticObserver {
  readonly report: (diagnostic: OperationDiagnostic) => undefined;
}

export interface OperationProducerSink<TValue, TError, TProgress> {
  readonly reportProgress: (value: TProgress) => undefined;
  readonly reportTerminal: (
    outcome: OperationOutcome<TValue, TError>,
  ) => undefined;
  readonly reportQuiesced: () => undefined;
  readonly reportUnexpectedFailure: (error: unknown) => undefined;
}

export interface PreparedOperationProducer {
  readonly requestCancellation: (
    reason: OperationCancelReason,
  ) => undefined;
  readonly activate: () => undefined;
  readonly abandon: () => undefined;
}

export type OperationPreparation<TPrepareError> =
  | {
      readonly kind: "prepared";
      readonly binding: PreparedOperationProducer;
    }
  | {
      readonly kind: "rejected";
      readonly error: TPrepareError;
    };

export interface OperationProducerAdapter<
  TInput,
  TValue,
  TError,
  TProgress,
  TPrepareError,
> {
  readonly prepare: (
    identity: OperationIdentity,
    input: TInput,
    sink: OperationProducerSink<TValue, TError, TProgress>,
  ) => OperationPreparation<TPrepareError>;
}

export interface OperationSession<
  TInput,
  TValue,
  TError,
  TProgress,
  TPrepareError,
> {
  start(
    input: TInput,
    adapter: OperationProducerAdapter<
      TInput,
      TValue,
      TError,
      TProgress,
      TPrepareError
    >,
  ): OperationStartResult<TValue, TError, TPrepareError>;
  cancelCurrent(reason?: OperationCancelReason): OperationControlResult;
  dispose(): OperationControlResult;
}

export interface OperationSessionObservers<TValue, TError, TProgress> {
  readonly feature: OperationFeatureObserver<TValue, TError, TProgress>;
  readonly diagnostic: OperationDiagnosticObserver;
}

export interface OperationAuthorityPage {
  createSession<TInput, TValue, TError, TProgress, TPrepareError>(
    observers: OperationSessionObservers<TValue, TError, TProgress>,
  ): OperationSession<TInput, TValue, TError, TProgress, TPrepareError>;
}

export interface OperationIdentityAllocationOptions {
  readonly maximumSequence?: number;
  readonly createId?: () => string;
}

export interface OperationLastResortConsole {
  readonly report: (
    diagnostic: OperationDiagnostic,
    observerError: unknown,
  ) => undefined;
}

export interface OperationAuthorityPageOptions {
  readonly allocation?: OperationIdentityAllocationOptions;
  readonly lastResortConsole?: OperationLastResortConsole;
}

interface Deferred<T> {
  readonly promise: Promise<T>;
  readonly resolve: (value: T) => void;
}

interface PageState {
  readonly maximumSequence: number;
  readonly createId: () => string;
  readonly allocatedIds: Set<string>;
  readonly lastResortConsole: OperationLastResortConsole;
  nextSequence: number;
  identityExhausted: boolean;
  featureObserverActive: boolean;
}

interface OperationRecord<TValue, TError, TProgress> {
  readonly identity: OperationIdentity;
  readonly outcomeDeferred: Deferred<OperationOutcome<TValue, TError>>;
  readonly quiescedDeferred: Deferred<void>;
  readonly handle: OperationHandle<TValue, TError>;
  readonly sink: OperationProducerSink<TValue, TError, TProgress>;
  binding: PreparedOperationProducer | null;
  outcome: OperationOutcome<TValue, TError> | null;
  activated: boolean;
  cancellationReserved: boolean;
  terminalReported: boolean;
  released: boolean;
}

interface SessionState<TValue, TError, TProgress> {
  readonly page: PageState;
  readonly diagnosticObserver: OperationDiagnosticObserver;
  featureObserver: OperationFeatureObserver<TValue, TError, TProgress> | null;
  current: OperationRecord<TValue, TError, TProgress> | null;
  revision: number;
  disposed: boolean;
}

type PublicationAuthorityPredicate = <TValue, TError, TProgress>(
  session: SessionState<TValue, TError, TProgress>,
  record: OperationRecord<TValue, TError, TProgress>,
) => boolean;

const defaultLastResortConsole: OperationLastResortConsole = {
  report: (diagnostic, observerError) => {
    console.error(
      "Operation authority diagnostic observer failed.",
      diagnostic,
      observerError,
    );
    return undefined;
  },
};

const standardPublicationAuthority: PublicationAuthorityPredicate
  = (session, record) =>
    !session.disposed
    && session.current === record
    && record.outcome === null;

function deferred<T>(): Deferred<T> {
  let resolvePromise: ((value: T) => void) | undefined;
  const promise = new Promise<T>((resolve) => {
    resolvePromise = resolve;
  });
  return {
    promise,
    resolve: value => {
      resolvePromise?.(value);
    },
  };
}

function brandOperationId(value: string): OperationId {
  // The page allocator is the sole construction boundary for the opaque brand.
  // oxlint-disable-next-line typescript/no-unsafe-type-assertion
  return value as OperationId;
}

function validateMaximumSequence(value: number): number {
  if (!Number.isSafeInteger(value) || value < 0)
    throw new RangeError("maximumSequence must be a non-negative safe integer.");
  return value;
}

function reportDiagnostic<TValue, TError, TProgress>(
  session: SessionState<TValue, TError, TProgress>,
  diagnostic: OperationDiagnostic,
): void {
  try {
    session.diagnosticObserver.report(diagnostic);
  } catch (observerError: unknown) {
    try {
      session.page.lastResortConsole.report(diagnostic, observerError);
    } catch {
      // The last-resort sink cannot safely report its own failure.
    }
  }
}

function producerContractError<TValue, TError, TProgress>(
  session: SessionState<TValue, TError, TProgress>,
  record: OperationRecord<TValue, TError, TProgress>,
  message: string,
): void {
  reportDiagnostic(session, {
    kind: "producer-contract",
    operationId: record.identity.id,
    error: new Error(message),
  });
}

function resolveOutcome<TValue, TError, TProgress>(
  record: OperationRecord<TValue, TError, TProgress>,
  outcome: OperationOutcome<TValue, TError>,
): boolean {
  if (record.outcome !== null) return false;
  record.outcome = outcome;
  record.outcomeDeferred.resolve(outcome);
  return true;
}

function reserveCancellation<TValue, TError, TProgress>(
  record: OperationRecord<TValue, TError, TProgress>,
): boolean {
  if (!record.activated || record.cancellationReserved || record.binding === null)
    return false;
  record.cancellationReserved = true;
  return true;
}

function invokeCancellation<TValue, TError, TProgress>(
  session: SessionState<TValue, TError, TProgress>,
  record: OperationRecord<TValue, TError, TProgress>,
  reason: OperationCancelReason,
): void {
  const binding = record.binding;
  if (binding === null) return;
  try {
    binding.requestCancellation(reason);
  } catch (error: unknown) {
    reportDiagnostic(session, {
      kind: "producer-callout",
      operationId: record.identity.id,
      error,
    });
  }
}

function abandon<TValue, TError, TProgress>(
  session: SessionState<TValue, TError, TProgress>,
  record: OperationRecord<TValue, TError, TProgress>,
): void {
  const binding = record.binding;
  if (binding === null) return;
  try {
    binding.abandon();
  } catch (error: unknown) {
    reportDiagnostic(session, {
      kind: "producer-callout",
      operationId: record.identity.id,
      error,
    });
    return;
  }
  if (!record.released) {
    record.released = true;
    record.quiescedDeferred.resolve(undefined);
  }
}

function faultFeatureObserver<TValue, TError, TProgress>(
  session: SessionState<TValue, TError, TProgress>,
  failedOperationId: OperationId | null,
  error: unknown,
): {
  readonly cancellation:
    | {
        readonly record: OperationRecord<TValue, TError, TProgress>;
        readonly reason: OperationCancelReason;
      }
    | null;
} {
  session.featureObserver = null;
  let cancellation:
    | {
        readonly record: OperationRecord<TValue, TError, TProgress>;
        readonly reason: OperationCancelReason;
      }
    | null = null;
  const current = session.current;
  if (!session.disposed) {
    session.disposed = true;
    session.current = null;
    session.revision++;
    if (current !== null && current.outcome === null) {
      const reason = "feature-observer-failed";
      resolveOutcome(current, { kind: "canceled", reason });
      if (reserveCancellation(current))
        cancellation = { record: current, reason };
    }
  }
  reportDiagnostic(session, {
    kind: "feature-observer",
    operationId: failedOperationId,
    error,
  });
  return { cancellation };
}

function publishFeature<TValue, TError, TProgress>(
  session: SessionState<TValue, TError, TProgress>,
  event: OperationFeatureEvent<TValue, TError, TProgress>,
  observer = session.featureObserver,
): boolean {
  if (observer === null) return false;
  session.page.featureObserverActive = true;
  try {
    observer.publish(event);
    return true;
  } catch (error: unknown) {
    session.page.featureObserverActive = false;
    const failedOperationId = event.kind === "started" || event.kind === "replaced"
      ? event.operation.id
      : event.kind === "progress"
        ? event.progress.operationId
        : event.operationId;
    const fault = faultFeatureObserver(session, failedOperationId, error);
    if (fault.cancellation !== null)
      invokeCancellation(
        session,
        fault.cancellation.record,
        fault.cancellation.reason,
      );
    return false;
  } finally {
    session.page.featureObserverActive = false;
  }
}

function createRecord<TValue, TError, TProgress>(
  session: SessionState<TValue, TError, TProgress>,
  identity: OperationIdentity,
  publicationAuthority: PublicationAuthorityPredicate,
): OperationRecord<TValue, TError, TProgress> {
  const outcomeDeferred = deferred<OperationOutcome<TValue, TError>>();
  const quiescedDeferred = deferred<void>();
  let record: OperationRecord<TValue, TError, TProgress>;

  const sink: OperationProducerSink<TValue, TError, TProgress> = {
    reportProgress: value => {
      if (record.released) {
        producerContractError(
          session,
          record,
          "Producer reported progress after resource release.",
        );
        return undefined;
      }
      if (publicationAuthority(session, record)) {
        publishFeature(session, {
          kind: "progress",
          progress: { operationId: record.identity.id, value },
        });
      }
      return undefined;
    },
    reportTerminal: outcome => {
      if (record.released) {
        producerContractError(
          session,
          record,
          "Producer reported a terminal outcome after resource release.",
        );
        return undefined;
      }
      if (record.terminalReported) {
        producerContractError(
          session,
          record,
          "Producer reported more than one terminal outcome.",
        );
        return undefined;
      }
      record.terminalReported = true;
      if (!publicationAuthority(session, record)) return undefined;
      resolveOutcome(record, outcome);
      session.revision++;
      if (outcome.kind === "canceled") {
        publishFeature(session, {
          kind: "canceled",
          operationId: record.identity.id,
          reason: outcome.reason,
        });
      } else {
        publishFeature(session, {
          kind: "terminal",
          operationId: record.identity.id,
          outcome,
        });
      }
      return undefined;
    },
    reportQuiesced: () => {
      if (record.released) {
        producerContractError(
          session,
          record,
          "Producer reported resource release more than once.",
        );
        return undefined;
      }
      if (!record.terminalReported) {
        producerContractError(
          session,
          record,
          "Producer reported resource release before physical settlement.",
        );
        return undefined;
      }
      record.released = true;
      record.quiescedDeferred.resolve(undefined);
      return undefined;
    },
    reportUnexpectedFailure: error => {
      if (record.released) {
        producerContractError(
          session,
          record,
          "Producer reported an unexpected failure after resource release.",
        );
        return undefined;
      }
      reportDiagnostic(session, {
        kind: "producer-contract",
        operationId: record.identity.id,
        error,
      });
      return undefined;
    },
  };

  const handle: OperationHandle<TValue, TError> = {
    id: identity.id,
    outcome: outcomeDeferred.promise,
    quiesced: quiescedDeferred.promise,
    cancel: reason => cancelRecord(session, record, reason ?? "user"),
  };

  record = {
    identity,
    outcomeDeferred,
    quiescedDeferred,
    handle,
    sink,
    binding: null,
    outcome: null,
    activated: false,
    cancellationReserved: false,
    terminalReported: false,
    released: false,
  };
  return record;
}

function cancelRecord<TValue, TError, TProgress>(
  session: SessionState<TValue, TError, TProgress>,
  record: OperationRecord<TValue, TError, TProgress>,
  reason: OperationCancelReason,
): OperationControlResult {
  if (session.page.featureObserverActive)
    return { kind: "rejected", reason: "feature-observer-active" };
  if (record.outcome !== null) return { kind: "no-op" };

  resolveOutcome(record, { kind: "canceled", reason });
  session.revision++;
  const cancellationReserved = reserveCancellation(record);
  publishFeature(session, {
    kind: "canceled",
    operationId: record.identity.id,
    reason,
  });
  if (cancellationReserved) invokeCancellation(session, record, reason);
  return { kind: "applied" };
}

function allocateIdentity(
  page: PageState,
):
  | { readonly kind: "allocated"; readonly identity: OperationIdentity }
  | {
      readonly kind: "rejected";
      readonly reason: { readonly kind: "identity-exhausted" };
    } {
  if (page.identityExhausted || page.nextSequence > page.maximumSequence)
    return { kind: "rejected", reason: { kind: "identity-exhausted" } };
  const sequence = page.nextSequence;
  page.nextSequence++;
  const id = brandOperationId(page.createId());
  if (page.allocatedIds.has(id)) {
    page.identityExhausted = true;
    return { kind: "rejected", reason: { kind: "identity-exhausted" } };
  }
  page.allocatedIds.add(id);
  return { kind: "allocated", identity: { id, sequence } };
}

function createPage(
  options: OperationAuthorityPageOptions,
  publicationAuthority: PublicationAuthorityPredicate,
): OperationAuthorityPage {
  const page: PageState = {
    maximumSequence: validateMaximumSequence(
      options.allocation?.maximumSequence ?? Number.MAX_SAFE_INTEGER,
    ),
    createId: options.allocation?.createId
      ?? (() => globalThis.crypto.randomUUID()),
    allocatedIds: new Set<string>(),
    lastResortConsole: options.lastResortConsole ?? defaultLastResortConsole,
    nextSequence: 1,
    identityExhausted: false,
    featureObserverActive: false,
  };

  return {
    createSession: <TInput, TValue, TError, TProgress, TPrepareError>(
      observers: OperationSessionObservers<TValue, TError, TProgress>,
    ): OperationSession<TInput, TValue, TError, TProgress, TPrepareError> => {
      const session: SessionState<TValue, TError, TProgress> = {
        page,
        featureObserver: observers.feature,
        diagnosticObserver: observers.diagnostic,
        current: null,
        revision: 0,
        disposed: false,
      };

      return {
        start: (input, adapter) => {
          if (page.featureObserverActive) {
            return {
              kind: "rejected",
              reason: { kind: "feature-observer-active" },
            };
          }
          if (session.disposed) {
            return {
              kind: "rejected",
              reason: { kind: "session-disposed" },
            };
          }
          const allocation = allocateIdentity(page);
          if (allocation.kind === "rejected")
            return { kind: "rejected", reason: allocation.reason };

          const capturedRevision = session.revision;
          const capturedCurrentId = session.current?.identity.id ?? null;
          const candidate = createRecord<TValue, TError, TProgress>(
            session,
            allocation.identity,
            publicationAuthority,
          );
          const preparation = adapter.prepare(
            allocation.identity,
            input,
            candidate.sink,
          );
          const sessionChanged = session.revision !== capturedRevision
            || (session.current?.identity.id ?? null) !== capturedCurrentId;

          if (session.disposed) {
            if (preparation.kind === "prepared") {
              candidate.binding = preparation.binding;
              abandon(session, candidate);
            }
            return {
              kind: "rejected",
              reason: { kind: "session-disposed" },
            };
          }
          if (sessionChanged) {
            if (preparation.kind === "prepared") {
              candidate.binding = preparation.binding;
              abandon(session, candidate);
            }
            return {
              kind: "rejected",
              reason: { kind: "session-changed" },
            };
          }
          if (preparation.kind === "rejected") {
            return {
              kind: "rejected",
              reason: {
                kind: "producer-rejected",
                error: preparation.error,
              },
            };
          }

          candidate.binding = preparation.binding;
          const previous = session.current;
          session.current = candidate;
          session.revision++;

          let priorCancellation:
            | {
                readonly record: OperationRecord<TValue, TError, TProgress>;
                readonly reason: OperationCancelReason;
              }
            | null = null;
          if (previous !== null && previous.outcome === null) {
            const reason: OperationCancelReason = "superseded";
            resolveOutcome(previous, { kind: "canceled", reason });
            if (reserveCancellation(previous))
              priorCancellation = { record: previous, reason };
          }

          const event: OperationFeatureEvent<TValue, TError, TProgress>
            = previous === null
              ? { kind: "started", operation: candidate.identity }
              : {
                  kind: "replaced",
                  previousOperationId: previous.identity.id,
                  operation: candidate.identity,
                  reason: "superseded",
                };
          const published = publishFeature(session, event);
          if (published) {
            candidate.activated = true;
            candidate.binding.activate();
          } else {
            abandon(session, candidate);
          }
          if (priorCancellation !== null)
            invokeCancellation(
              session,
              priorCancellation.record,
              priorCancellation.reason,
            );
          return { kind: "started", handle: candidate.handle };
        },
        cancelCurrent: reason => {
          if (page.featureObserverActive)
            return { kind: "rejected", reason: "feature-observer-active" };
          const current = session.current;
          if (current === null) return { kind: "no-op" };
          return cancelRecord(session, current, reason ?? "user");
        },
        dispose: () => {
          if (page.featureObserverActive)
            return { kind: "rejected", reason: "feature-observer-active" };
          if (session.disposed) return { kind: "no-op" };

          const observer = session.featureObserver;
          const current = session.current;
          session.disposed = true;
          session.current = null;
          session.featureObserver = null;
          session.revision++;
          let cancellationReserved = false;
          if (current !== null && current.outcome === null) {
            resolveOutcome(current, {
              kind: "canceled",
              reason: "disposed",
            });
            cancellationReserved = reserveCancellation(current);
          }
          publishFeature(session, {
            kind: "disposed",
            operationId: current?.identity.id ?? null,
          }, observer);
          if (current !== null && cancellationReserved)
            invokeCancellation(session, current, "disposed");
          return { kind: "applied" };
        },
      };
    },
  };
}

export function createOperationAuthorityPage(
  options: OperationAuthorityPageOptions = {},
): OperationAuthorityPage {
  return createPage(options, standardPublicationAuthority);
}
