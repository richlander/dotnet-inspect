import type { OperationCancelReason } from "./operation-authority.ts";
import type { WorkerProducerClassRegistry } from "./worker-runtime-core.ts";
import {
  decodeBoundMainToWorkerEnvelope,
  decodeStartPayload,
  decodeUnboundInitializationEnvelope,
  type BoundedPayloadDecoder,
  type ManagedOperationSettlement,
  type RawMainToWorkerEnvelope,
  type RawWorkerToMainEnvelope,
  type WorkerEnvelopeDecodeFailure,
  type WorkerLivenessAllowance,
  type WorkerWireOperationReference,
  WORKER_RUNTIME_PROTOCOL_VERSION,
} from "./worker-runtime-protocol.ts";

export function sameAllowance(
  left: WorkerLivenessAllowance,
  right: WorkerLivenessAllowance,
): boolean {
  return left.kind === "unbounded"
    ? right.kind === "unbounded"
    : right.kind === "bounded"
      && left.maxSilentActiveMilliseconds
        === right.maxSilentActiveMilliseconds;
}

export interface WorkerRuntimeTaskScheduler {
  enqueue(task: () => void): void;
}

interface WorkerRuntimeBootstrapAdapter<TBootstrap> {
  readonly decoder: BoundedPayloadDecoder<TBootstrap>;
  readonly bootstrap: (
    bootstrap: TBootstrap,
  ) => void | Promise<void>;
}

export interface WorkerOperationContext {
  readonly operation: WorkerWireOperationReference;
  readonly cache: WorkerEpochCache;
  /** Returns false after settlement, realm failure, or disposal. */
  reportProgress(payload: unknown): boolean;
  startEpochWork(
    producerClass: string,
    sequence: number,
    advertisedAllowance?: WorkerLivenessAllowance,
  ): boolean;
  finishEpochWork(sequence: number): boolean;
}

export interface WorkerEpochCache {
  readonly size: number;
  get(key: string): unknown;
  set(key: string, value: unknown): boolean;
  has(key: string): boolean;
  delete(key: string): boolean;
}

class RevocableWorkerEpochCache implements WorkerEpochCache {
  readonly #entries = new Map<string, unknown>();
  #active = true;

  get size(): number {
    return this.#entries.size;
  }

  get(key: string): unknown {
    return this.#active ? this.#entries.get(key) : undefined;
  }

  set(key: string, value: unknown): boolean {
    if (!this.#active) return false;
    this.#entries.set(key, value);
    return true;
  }

  has(key: string): boolean {
    return this.#active && this.#entries.has(key);
  }

  delete(key: string): boolean {
    return this.#active && this.#entries.delete(key);
  }

  revoke(): void {
    this.#active = false;
    this.#entries.clear();
  }
}

export interface WorkerOperationRegistration<
  TInput,
  TValue,
  TError,
  TOperationDiagnostic,
> {
  readonly kind: string;
  readonly allowance: WorkerLivenessAllowance;
  readonly input: BoundedPayloadDecoder<TInput>;
  readonly rejectInvalidPayload: (
    failure: WorkerEnvelopeDecodeFailure,
  ) => {
    readonly error: TError;
    readonly diagnostic: TOperationDiagnostic;
  };
  readonly invoke: (
    input: TInput,
    context: WorkerOperationContext,
  ) => ManagedOperationSettlement<TValue, TError, TOperationDiagnostic>
    | Promise<
      ManagedOperationSettlement<TValue, TError, TOperationDiagnostic>
    >;
  readonly cancel?: (
    operation: WorkerWireOperationReference,
    reason: OperationCancelReason,
  ) => boolean | Promise<boolean>;
}

type WorkerCancel = (
  operation: WorkerWireOperationReference,
  reason: OperationCancelReason,
) => boolean | Promise<boolean>;

interface WorkerOperationDispatchHandlers {
  readonly accepted: (
    allowance: WorkerLivenessAllowance,
    cancel: WorkerCancel | null,
  ) => boolean;
  readonly rejected: (error: unknown, diagnostic: unknown) => void;
  readonly settled: (
    settlement: ManagedOperationSettlement<unknown, unknown, unknown>,
  ) => void;
  readonly failed: (error: unknown) => void;
}

interface ErasedWorkerOperationRegistration {
  readonly kind: string;
  readonly dispatch: (
    envelope: Extract<
      RawMainToWorkerEnvelope,
      { readonly kind: "start" }
    >,
    context: WorkerOperationContext,
    handlers: WorkerOperationDispatchHandlers,
  ) => void;
}

function eraseSettlement<TValue, TError, TDiagnostic>(
  settlement: ManagedOperationSettlement<TValue, TError, TDiagnostic>,
): ManagedOperationSettlement<unknown, unknown, unknown> {
  if (settlement.kind === "succeeded")
    return { kind: "succeeded", value: settlement.value };
  if (settlement.kind === "failed") {
    return {
      kind: "failed",
      failureKind: settlement.failureKind,
      error: settlement.error,
      diagnostic: settlement.diagnostic,
    };
  }
  return { kind: "canceled", reason: settlement.reason };
}

export class WorkerOperationCatalog {
  readonly #registrations =
    new Map<string, ErasedWorkerOperationRegistration>();

  register<TInput, TValue, TError, TOperationDiagnostic>(
    registration: WorkerOperationRegistration<
      TInput,
      TValue,
      TError,
      TOperationDiagnostic
    >,
  ): void {
    if (this.#registrations.has(registration.kind))
      throw new Error(`Worker operation ${registration.kind} is duplicated.`);
    const erased: ErasedWorkerOperationRegistration = {
      kind: registration.kind,
      dispatch: (envelope, context, handlers) => {
        const decoded = decodeStartPayload(envelope, registration.input);
        if (decoded.kind === "failure") {
          const rejection = registration.rejectInvalidPayload(
            decoded.failure,
          );
          handlers.rejected(rejection.error, rejection.diagnostic);
          return;
        }
        const cancel = registration.cancel;
        const accepted = handlers.accepted(
          registration.allowance,
          cancel === undefined
            ? null
            : (operation, reason) => cancel(operation, reason),
        );
        if (!accepted) return;
        let result:
          | ManagedOperationSettlement<
            TValue,
            TError,
            TOperationDiagnostic
          >
          | Promise<
            ManagedOperationSettlement<
              TValue,
              TError,
              TOperationDiagnostic
            >
          >;
        try {
          result = registration.invoke(decoded.value.payload, context);
        } catch (error: unknown) {
          handlers.failed(error);
          return;
        }
        Promise.resolve(result).then(
          settlement => {
            handlers.settled(eraseSettlement(settlement));
            return undefined;
          },
          (error: unknown) => {
            handlers.failed(error);
            return undefined;
          },
        );
      },
    };
    this.#registrations.set(registration.kind, erased);
  }

  dispatch(
    envelope: Extract<
      RawMainToWorkerEnvelope,
      { readonly kind: "start" }
    >,
    context: WorkerOperationContext,
    handlers: WorkerOperationDispatchHandlers,
  ): boolean {
    const registration = this.#registrations.get(envelope.operationKind);
    if (registration === undefined) return false;
    registration.dispatch(envelope, context, handlers);
    return true;
  }
}

export interface WorkerRuntimeRealmOptions<TBootstrap, TDiagnostic> {
  /** Optional initialization task dispatch; omit for synchronous decoding. */
  readonly scheduler?: WorkerRuntimeTaskScheduler;
  readonly bootstrap: WorkerRuntimeBootstrapAdapter<TBootstrap>;
  readonly diagnostic: (detail: unknown) => TDiagnostic;
  readonly unknownOperationRejection: (kind: string) => {
    readonly error: unknown;
    readonly diagnostic: unknown;
  };
  readonly operations: WorkerOperationCatalog;
  readonly producerClasses: WorkerProducerClassRegistry;
  /** One ordered output channel, normally the Worker's global postMessage. */
  readonly post: (message: RawWorkerToMainEnvelope) => void;
}

interface WorkerActiveOperation {
  readonly operation: WorkerWireOperationReference;
  readonly cancel: WorkerCancel | null;
  settling: boolean;
}

export class WorkerRuntimeRealm<TBootstrap, TDiagnostic> {
  readonly #cache = new RevocableWorkerEpochCache();
  readonly #options: WorkerRuntimeRealmOptions<TBootstrap, TDiagnostic>;
  readonly #active = new Map<string, WorkerActiveOperation>();
  readonly #epochWork = new Map<number, WorkerLivenessAllowance>();
  #epochToken: number | null = null;
  #idleHeartbeatIntervalMilliseconds: number | null = null;
  #operationHighWater = 0;
  #workHighWater = 0;
  #lane: Promise<void> = Promise.resolve();
  #initialized = false;
  #ready = false;
  #failed = false;
  #terminated = false;

  constructor(
    options: WorkerRuntimeRealmOptions<TBootstrap, TDiagnostic>,
  ) {
    this.#options = options;
  }

  get cache(): WorkerEpochCache {
    return this.#cache;
  }

  get activeOperationCount(): number {
    return this.#active.size;
  }

  get activeEpochWorkCount(): number {
    return this.#epochWork.size;
  }

  get disposed(): boolean {
    return this.#terminated;
  }

  /**
   * Invalid initialization throws with the decode failure as its cause, since
   * no validated epoch token exists for a response. An optional scheduler
   * receives that throw in its initialization task instead of this call.
   */
  receive(message: unknown): void {
    if (this.#terminated) return;
    if (!this.#initialized) {
      this.#initialized = true;
      if (this.#options.scheduler === undefined) {
        this.#initialize(message);
      } else {
        this.#options.scheduler.enqueue(() => {
          this.#initialize(message);
        });
      }
      return;
    }
    this.#lane = this.#lane.then(() => this.#processCommand(message));
    this.#lane.catch((error: unknown) => {
      this.#declareFailure(error);
    });
  }

  /** Revokes local callback authority; the transport owns Worker destruction. */
  dispose(): void {
    if (this.#terminated) return;
    this.#terminated = true;
    this.#active.clear();
    this.#epochWork.clear();
    this.#cache.revoke();
  }

  emitHeartbeat(): void {
    if (!this.#ready || this.#epochToken === null) return;
    this.#emit({
      protocolVersion: WORKER_RUNTIME_PROTOCOL_VERSION,
      epochToken: this.#epochToken,
      kind: "heartbeat",
    });
  }

  /**
   * Reports StartupFailed before readiness or EpochFailed afterwards, allowing
   * admitted physical release until disposal. Before binding, throws instead.
   */
  fail(detail: unknown): void {
    if (this.#terminated) return;
    if (this.#epochToken === null) {
      this.#failed = true;
      throw new Error("Worker realm failed before initialization.", {
        cause: detail,
      });
    }
    if (!this.#ready) this.#startupFailed(detail);
    else this.#declareFailure(detail);
  }

  startEpochWork(
    producerClass: string,
    sequence: number,
    advertisedAllowance?: WorkerLivenessAllowance,
  ): boolean {
    if (this.#terminated
      || !this.#ready
      || this.#failed
      || this.#epochToken === null) return false;
    const registered = this.#options.producerClasses.allowance(producerClass);
    if (!Number.isSafeInteger(sequence)
      || sequence <= 0
      || sequence <= this.#workHighWater
      || registered === null) {
      this.#declareFailure({
        kind: "invalid-epoch-work-start",
        producerClass,
        sequence,
      });
      return false;
    }
    this.#workHighWater = sequence;
    const advertised = advertisedAllowance ?? registered;
    if (!sameAllowance(registered, advertised)) {
      this.#declareFailure({
        kind: "epoch-work-allowance-mismatch",
        producerClass,
        sequence,
      });
      return false;
    }
    this.#epochWork.set(sequence, registered);
    this.#emit({
      protocolVersion: WORKER_RUNTIME_PROTOCOL_VERSION,
      epochToken: this.#epochToken,
      kind: "epoch-work-started",
      workSequence: sequence,
      allowance: registered,
    });
    return true;
  }

  finishEpochWork(sequence: number): boolean {
    if (this.#terminated
      || !this.#ready
      || this.#epochToken === null) return false;
    if (!this.#epochWork.delete(sequence)) {
      if (!this.#failed) {
        this.#declareFailure({
          kind: "invalid-epoch-work-finish",
          sequence,
        });
      }
      return false;
    }
    this.#emit({
      protocolVersion: WORKER_RUNTIME_PROTOCOL_VERSION,
      epochToken: this.#epochToken,
      kind: "epoch-work-finished",
      workSequence: sequence,
    });
    return true;
  }

  #initialize(message: unknown): void {
    if (this.#terminated) return;
    const decoded = decodeUnboundInitializationEnvelope(
      message,
      this.#options.bootstrap.decoder,
    );
    if (decoded.kind === "failure") {
      this.#failed = true;
      throw new Error("Worker initialization envelope was rejected.", {
        cause: decoded.failure,
      });
    }
    this.#epochToken = decoded.value.epochToken;
    this.#idleHeartbeatIntervalMilliseconds
      = decoded.value.idleHeartbeatIntervalMilliseconds;
    if (decoded.value.idleAllowanceMilliseconds
      !== this.#options.producerClasses.idleAllowanceMilliseconds) {
      this.#startupFailed({
        kind: "producer-class-idle-allowance-mismatch",
        expected: decoded.value.idleAllowanceMilliseconds,
        actual: this.#options.producerClasses.idleAllowanceMilliseconds,
      });
      return;
    }
    let bootstrap: void | Promise<void>;
    try {
      bootstrap = this.#options.bootstrap.bootstrap(decoded.value.bootstrap);
    } catch (error: unknown) {
      this.#startupFailed(error);
      return;
    }
    Promise.resolve(bootstrap).then(
      () => {
        if (this.#terminated || this.#failed || this.#epochToken === null)
          return undefined;
        this.#ready = true;
        this.#emit({
          protocolVersion: WORKER_RUNTIME_PROTOCOL_VERSION,
          epochToken: this.#epochToken,
          kind: "ready",
          idleHeartbeatIntervalMilliseconds:
            this.#idleHeartbeatIntervalMilliseconds
            ?? decoded.value.idleHeartbeatIntervalMilliseconds,
        });
        return undefined;
      },
      (error: unknown) => {
        this.#startupFailed(error);
        return undefined;
      },
    );
  }

  async #processCommand(message: unknown): Promise<void> {
    if (this.#terminated || this.#failed || this.#epochToken === null) return;
    const decoded = decodeBoundMainToWorkerEnvelope(
      message,
      this.#epochToken,
    );
    if (decoded.kind === "failure") {
      this.#declareFailure(decoded.failure);
      return;
    }
    if (!this.#ready || decoded.value.kind === "initialize") {
      this.#declareFailure({
        kind: "illegal-command-state",
        command: decoded.value.kind,
      });
      return;
    }
    if (decoded.value.kind === "start") {
      this.#processStart(decoded.value);
      return;
    }
    if (decoded.value.kind === "cancel") {
      await this.#processCancel(decoded.value);
      return;
    }
    this.#processProbe(decoded.value.probeSequence);
  }

  #processStart(
    envelope: Extract<RawMainToWorkerEnvelope, { readonly kind: "start" }>,
  ): void {
    if (envelope.operation.operationSequence <= this.#operationHighWater) {
      this.#declareFailure({
        kind: "operation-sequence-replay",
        operation: envelope.operation,
      });
      return;
    }
    this.#operationHighWater = envelope.operation.operationSequence;
    if (this.#active.has(envelope.operation.operationId)) {
      this.#declareFailure({
        kind: "active-operation-id-duplicate",
        operation: envelope.operation,
      });
      return;
    }
    let admitted: WorkerActiveOperation | null = null;
    const context: WorkerOperationContext = {
      operation: envelope.operation,
      cache: this.cache,
      reportProgress: payload => {
        if (this.#terminated
          || this.#failed
          || admitted === null
          || admitted.settling
          || this.#active.get(envelope.operation.operationId) !== admitted)
          return false;
        this.#emit({
          protocolVersion: WORKER_RUNTIME_PROTOCOL_VERSION,
          epochToken: this.#requiredEpochToken(),
          kind: "progress",
          operation: envelope.operation,
          payload,
        });
        return true;
      },
      startEpochWork: (producerClass, sequence, advertisedAllowance) =>
        this.startEpochWork(producerClass, sequence, advertisedAllowance),
      finishEpochWork: sequence => this.finishEpochWork(sequence),
    };
    const dispatched = this.#options.operations.dispatch(
      envelope,
      context,
      {
        accepted: (allowance, cancel) => {
          if (this.#terminated || this.#failed) return false;
          admitted = {
            operation: envelope.operation,
            cancel,
            settling: false,
          };
          this.#active.set(envelope.operation.operationId, admitted);
          this.#emit({
            protocolVersion: WORKER_RUNTIME_PROTOCOL_VERSION,
            epochToken: this.#requiredEpochToken(),
            kind: "accepted",
            operation: envelope.operation,
            allowance,
          });
          return !this.#terminated && !this.#failed;
        },
        rejected: (error, diagnostic) => {
          this.#rejectStart(envelope, error, diagnostic);
        },
        settled: settlement => {
          this.#settle(envelope.operation, settlement);
        },
        failed: error => {
          this.#declareFailure(error);
        },
      },
    );
    if (dispatched) return;
    const rejection = this.#options.unknownOperationRejection(
      envelope.operationKind,
    );
    this.#rejectStart(envelope, rejection.error, rejection.diagnostic);
  }

  async #processCancel(
    envelope: Extract<RawMainToWorkerEnvelope, { readonly kind: "cancel" }>,
  ): Promise<void> {
    if (envelope.operation.operationSequence > this.#operationHighWater) {
      this.#declareFailure({
        kind: "future-cancellation",
        operation: envelope.operation,
      });
      return;
    }
    const active = this.#active.get(envelope.operation.operationId);
    let running = false;
    if (active !== undefined
      && active.operation.operationSequence
        === envelope.operation.operationSequence
      && !active.settling) {
      running = active.cancel === null
        ? false
        : await active.cancel(envelope.operation, envelope.reason);
    }
    this.#emit({
      protocolVersion: WORKER_RUNTIME_PROTOCOL_VERSION,
      epochToken: this.#requiredEpochToken(),
      kind: "cancel-acknowledged",
      operation: envelope.operation,
      status: running ? "running" : "not-active",
    });
  }

  #processProbe(sequence: number): void {
    this.#emit({
      protocolVersion: WORKER_RUNTIME_PROTOCOL_VERSION,
      epochToken: this.#requiredEpochToken(),
      kind: "probe-acknowledged",
      probeSequence: sequence,
    });
  }

  #rejectStart(
    envelope: Extract<RawMainToWorkerEnvelope, { readonly kind: "start" }>,
    error: unknown,
    diagnostic: unknown,
  ): void {
    this.#emit({
      protocolVersion: WORKER_RUNTIME_PROTOCOL_VERSION,
      epochToken: this.#requiredEpochToken(),
      kind: "rejected",
      operation: envelope.operation,
      error,
      diagnostic,
    });
  }

  #settle(
    operation: WorkerWireOperationReference,
    settlement: ManagedOperationSettlement<unknown, unknown, unknown>,
  ): void {
    if (this.#terminated) return;
    const active = this.#active.get(operation.operationId);
    if (active === undefined
      || active.operation.operationSequence !== operation.operationSequence) {
      if (!this.#failed) {
        this.#declareFailure({
          kind: "settlement-without-active-operation",
          operation,
        });
      }
      return;
    }
    active.settling = true;
    this.#active.delete(operation.operationId);
    this.#emit({
      protocolVersion: WORKER_RUNTIME_PROTOCOL_VERSION,
      epochToken: this.#requiredEpochToken(),
      kind: "settled",
      operation,
      settlement,
    });
  }

  #startupFailed(error: unknown): void {
    if (this.#terminated || this.#failed || this.#epochToken === null) return;
    this.#failed = true;
    this.#emit({
      protocolVersion: WORKER_RUNTIME_PROTOCOL_VERSION,
      epochToken: this.#epochToken,
      kind: "startup-failed",
      diagnostic: this.#options.diagnostic(error),
    });
  }

  #declareFailure(detail: unknown): void {
    if (this.#terminated || this.#failed || this.#epochToken === null) return;
    this.#failed = true;
    this.#emit({
      protocolVersion: WORKER_RUNTIME_PROTOCOL_VERSION,
      epochToken: this.#epochToken,
      kind: "epoch-failed",
      diagnostic: this.#options.diagnostic(detail),
    });
  }

  #requiredEpochToken(): number {
    if (this.#epochToken === null)
      throw new Error("Worker realm has no epoch token.");
    return this.#epochToken;
  }

  #emit(data: RawWorkerToMainEnvelope): void {
    if (this.#terminated) return;
    this.#options.post(data);
  }
}
