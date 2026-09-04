import type {
  OperationCancelReason,
  OperationIdentity,
  OperationPreparation,
  OperationProducerAdapter,
  OperationProducerSink,
  OperationTerminalPublication,
  PreparedOperationProducer,
} from "./operation-authority.ts";
import {
  decodeBoundMainToWorkerEnvelope,
  decodeEpochFailedPayload,
  decodeProgressPayload,
  decodeRejectedPayload,
  decodeSettledPayload,
  decodeStartPayload,
  decodeStartupFailedPayload,
  decodeUnboundInitializationEnvelope,
  decodeWorkerToMainEnvelope,
  type BoundedPayloadDecodeResult,
  type BoundedPayloadDecoder,
  type ManagedOperationSettlement,
  type RawMainToWorkerEnvelope,
  type RawWorkerToMainEnvelope,
  type WorkerEnvelopeDecodeFailure,
  type WorkerLivenessAllowance,
  type WorkerWireOperationReference,
  WORKER_RUNTIME_PROTOCOL_VERSION,
} from "./worker-runtime-protocol.ts";

declare const workerEpochTokenBrand: unique symbol;
const workerIdleCompatibleBrand: unique symbol
  = Symbol("WorkerIdleCompatible");

export type WorkerEpochToken = number & {
  readonly [workerEpochTokenBrand]: "WorkerEpochToken";
};

export type WorkerIdleCompatible = {
  readonly [workerIdleCompatibleBrand]: "WorkerIdleCompatible";
};

export type WorkerRuntimeFailureKind =
  | "startup"
  | "worker-crash"
  | "protocol"
  | "watchdog"
  | "control-response"
  | "probe-exhaustion"
  | "worker-declared"
  | "worker-message";

export interface WorkerRuntimeFailure<TDiagnostic> {
  readonly kind: WorkerRuntimeFailureKind;
  readonly diagnostic: TDiagnostic;
}

export type WorkerRuntimeBoundaryErrors<TError> = Readonly<{
  [TKind in WorkerRuntimeFailureKind]: TError;
}>;

export type WorkerEpochClosure<TDiagnostic> =
  | {
      readonly kind: "planned-restart";
      readonly reason: "worker-restarted";
    }
  | {
      readonly kind: "unexpected-failure";
      readonly failure: WorkerRuntimeFailure<TDiagnostic>;
    };

export interface WorkerRuntimeSource {
  send(message: unknown): void;
  terminate(): void;
}

export interface WorkerRuntimeTransportBinding {
  readonly source: WorkerRuntimeSource;
  bind(handlers: WorkerRuntimeTransportHandlers): () => void;
}

export interface WorkerRuntimeTransportHandlers {
  readonly message: (source: WorkerRuntimeSource, data: unknown) => void;
  readonly error: (source: WorkerRuntimeSource, diagnostic: unknown) => void;
  readonly messageError: (
    source: WorkerRuntimeSource,
    diagnostic: unknown,
  ) => void;
}

interface WorkerRuntimeTransportFactory {
  create(): WorkerRuntimeTransportBinding;
}

interface WorkerRuntimeActiveClock {
  now(): number;
  subscribe(listener: () => void): () => void;
}

interface WorkerRuntimeLifecycleSignals {
  subscribe(listeners: WorkerRuntimeLifecycleListeners): () => void;
}

export interface WorkerRuntimeLifecycleListeners {
  readonly suspended: () => void;
  readonly resumed: () => void;
  readonly mainLoopRecovered: (gapActiveMilliseconds: number) => void;
}

interface WorkerRuntimeTaskScheduler {
  enqueue(task: () => void): void;
}

interface WorkerRuntimeBootstrapCodec<TBootstrap, TDiagnostic> {
  readonly encode: (
    bootstrap: TBootstrap,
  ) => BoundedPayloadDecodeResult<unknown>;
  readonly diagnostic: BoundedPayloadDecoder<TDiagnostic>;
}

export interface WorkerRuntimeOperationRegistration<
  TInput,
  TValue,
  TError,
  TOperationDiagnostic,
  TProgress,
  TPreparationError,
> {
  readonly kind: string;
  readonly allowance: WorkerLivenessAllowance;
  readonly encodeInput: (
    input: TInput,
  ) => BoundedPayloadDecodeResult<unknown>;
  readonly value: BoundedPayloadDecoder<TValue>;
  readonly error: BoundedPayloadDecoder<TError>;
  readonly diagnostic: BoundedPayloadDecoder<TOperationDiagnostic>;
  readonly progress: BoundedPayloadDecoder<TProgress>;
  readonly mapPreparationError: (
    error: WorkerRuntimePreparationError,
  ) => TPreparationError;
  readonly boundaryErrors: WorkerRuntimeBoundaryErrors<TError>;
}

export type WorkerRuntimePreparationError =
  | {
      readonly kind: "epoch-unavailable";
    }
  | {
      readonly kind: "operation-kind-already-registered";
    }
  | {
      readonly kind: "invalid-operation-reference";
    }
  | {
      readonly kind: "operation-sequence-replayed";
    }
  | {
      readonly kind: "operation-sequence-exhausted";
    }
  | {
      readonly kind: "payload-rejected";
      readonly reason: "invalid" | "oversized";
      readonly message: string;
      readonly cause?: unknown;
    };

export type WorkerRuntimeEpochStartResult =
  | {
      readonly kind: "started";
      readonly epochToken: WorkerEpochToken;
    }
  | {
      readonly kind: "rejected";
      readonly reason:
        | "bootstrap-invalid"
        | "bootstrap-oversized"
        | "epoch-active"
        | "host-disposed"
        | "epoch-token-exhausted"
        | "worker-creation-failed";
      readonly detail?: unknown;
    };

export interface WorkerRuntimeDiagnostic<TDiagnostic> {
  readonly kind:
    | "callback-error"
    | "epoch-token-exhausted";
  readonly diagnostic: TDiagnostic;
  readonly error?: unknown;
}

interface WorkerRuntimeCallbacks<TDiagnostic> {
  readonly failure: (
    failure: WorkerRuntimeFailure<TDiagnostic>,
  ) => undefined;
  readonly diagnostic: (
    diagnostic: WorkerRuntimeDiagnostic<TDiagnostic>,
  ) => undefined;
  readonly realmReleased: (epochToken: WorkerEpochToken) => undefined;
}

export interface WorkerRuntimeHostOptions<
  TBootstrap,
  TDiagnostic,
> {
  readonly transport: WorkerRuntimeTransportFactory;
  readonly clock: WorkerRuntimeActiveClock;
  readonly lifecycle: WorkerRuntimeLifecycleSignals;
  readonly bootstrap: WorkerRuntimeBootstrapCodec<TBootstrap, TDiagnostic>;
  readonly diagnostic: BoundedPayloadDecoder<TDiagnostic>;
  readonly callbacks: WorkerRuntimeCallbacks<TDiagnostic>;
  readonly createDiagnostic: (
    kind: WorkerRuntimeFailureKind | "callback-error" | "epoch-token-exhausted",
    detail: unknown,
  ) => TDiagnostic;
  readonly idleHeartbeatIntervalMilliseconds: number;
  readonly schedulingToleranceMilliseconds?: number;
  readonly startupBudgetMilliseconds: number;
  readonly controlResponseGraceMilliseconds: number;
  readonly drainBudgetMilliseconds: number;
  readonly maximumEpochToken?: number;
  readonly maximumOperationSequence?: number;
  readonly createProbeSequenceAllocator?: () => WorkerProbeSequenceAllocator;
  readonly producerClasses: WorkerProducerClassRegistry;
}

export interface WorkerRuntimeSnapshot<TDiagnostic> {
  readonly epochToken: WorkerEpochToken | null;
  readonly phase:
    | "absent"
    | "starting"
    | "flushing"
    | "ready"
    | "suspect"
    | "draining"
    | "closed";
  readonly closure: WorkerEpochClosure<TDiagnostic> | null;
  readonly heldOperations: number;
  readonly activeOperations: number;
  readonly compactControlRecords: number;
  readonly activeEpochWork: number;
  readonly outstandingProbeSequence: number | null;
  readonly deferredControlProbe: boolean;
  readonly lastTaskEvidenceOrigin: number | null;
}

interface DeferredCommand {
  readonly key: string;
  dueAt: number;
  responded: boolean;
  probeMark: number | null;
}

interface ProbeRegister {
  readonly sequence: number;
  readonly coveredResponses: readonly DeferredCommand[];
  watchdogAdopted: boolean;
  watchdogOrigin: number | null;
}

type OperationMessageReceiveResult =
  | { readonly kind: "success" }
  | {
      readonly kind: "failure";
      readonly failure: WorkerEnvelopeDecodeFailure;
    };

type WorkerPostResult =
  | { readonly kind: "sent" }
  | { readonly kind: "failed"; readonly error: unknown };

interface PreparedOperationReservation {
  completion: (() => void) | null;
}

interface MainOperationRecord<TDiagnostic> {
  readonly identity: OperationIdentity;
  readonly reference: WorkerWireOperationReference;
  readonly registration: MainOperationRegistration;
  payload: unknown;
  phase: "held" | "awaiting-admission" | "accepted" | "physically-closed";
  cancelReason: OperationCancelReason | null;
  cancelSent: boolean;
  cancelAcknowledged: boolean;
  logicalClosureReported: boolean;
  quiescenceReported: boolean;
  readonly receiveRejected: (
    envelope: Extract<
      RawWorkerToMainEnvelope,
      { readonly kind: "rejected" }
    >,
  ) => OperationMessageReceiveResult;
  readonly receiveProgress: (
    envelope: Extract<
      RawWorkerToMainEnvelope,
      { readonly kind: "progress" }
    >,
  ) => OperationMessageReceiveResult;
  readonly receiveSettled: (
    envelope: Extract<
      RawWorkerToMainEnvelope,
      { readonly kind: "settled" }
    >,
  ) => OperationMessageReceiveResult;
  readonly sealClosure: (
    closure: WorkerEpochClosure<TDiagnostic>,
  ) => void;
  readonly commitClosure: () => void;
  readonly publishClosure: () => void;
  readonly reportCancellation: (reason: OperationCancelReason) => void;
  readonly reportQuiescence: () => void;
  readonly release: () => void;
}

interface MainEpoch<TDiagnostic> {
  readonly token: WorkerEpochToken;
  readonly transport: WorkerRuntimeTransportBinding;
  readonly source: WorkerRuntimeSource;
  readonly startupStartedAt: number;
  startupDeadline: number;
  detach: (() => void) | null;
  phase:
    | "starting"
    | "flushing"
    | "ready"
    | "suspect"
    | "draining"
    | "closed";
  closure: WorkerEpochClosure<TDiagnostic> | null;
  preparationHighWater: number;
  operationHighWater: number;
  readonly preparedOperations: Map<number, PreparedOperationReservation>;
  flushingPreparedOperations: boolean;
  readonly operations: Map<
    string,
    MainOperationRecord<TDiagnostic>
  >;
  readonly held: MainOperationRecord<TDiagnostic>[];
  readonly commands: Map<string, DeferredCommand>;
  readonly probeSequences: WorkerProbeSequenceAllocator;
  probe: ProbeRegister | null;
  deferredControlProbe: boolean;
  lastTaskEvidenceOrigin: number | null;
  watchdogStageOrigin: number | null;
  workHighWater: number;
  readonly epochWork: Map<number, WorkerLivenessAllowance>;
  hadUnboundedAllowance: boolean;
  drainDeadline: number | null;
  suspended: boolean;
  preparedBindings: number;
  producerCallouts: number;
  closurePublicationActive: boolean;
  terminationFinalizing: boolean;
  terminationFinalized: boolean;
  realmReleased: boolean;
}

interface RegisteredProducerClass {
  readonly name: string;
  readonly allowance: WorkerLivenessAllowance;
  readonly structuralBoundMilliseconds: number | null;
  readonly capability: WorkerIdleCompatible | null;
}

interface MainOperationRegistration {
  readonly kind: string;
  readonly allowance: WorkerLivenessAllowance;
}

function validatePositiveSafeInteger(value: number, name: string): number {
  if (!Number.isSafeInteger(value) || value <= 0)
    throw new RangeError(`${name} must be a positive safe integer.`);
  return value;
}

function validateNonNegativeSafeInteger(value: number, name: string): number {
  if (!Number.isSafeInteger(value) || value < 0)
    throw new RangeError(`${name} must be a non-negative safe integer.`);
  return value;
}

function sameAllowance(
  left: WorkerLivenessAllowance,
  right: WorkerLivenessAllowance,
): boolean {
  return left.kind === "unbounded"
    ? right.kind === "unbounded"
    : right.kind === "bounded"
      && left.maxSilentActiveMilliseconds
        === right.maxSilentActiveMilliseconds;
}

function operationKey(reference: WorkerWireOperationReference): string {
  return `${reference.operationId}\u0000${reference.operationSequence}`;
}

function commandKey(
  kind: "start" | "cancel",
  reference: WorkerWireOperationReference,
): string {
  return `${kind}\u0000${operationKey(reference)}`;
}

function brandEpochToken(value: number): WorkerEpochToken {
  // The page allocator is the sole construction boundary for the opaque brand.
  // oxlint-disable-next-line typescript/no-unsafe-type-assertion
  return value as WorkerEpochToken;
}

function ownDataPropertyMismatches(
  value: object,
  property: string,
  expected: unknown,
): boolean {
  const descriptor = Object.getOwnPropertyDescriptor(value, property);
  return descriptor !== undefined
    && "value" in descriptor
    && descriptor.value !== expected;
}

function hasMismatchedReadyEcho(
  value: unknown,
  epochToken: WorkerEpochToken,
  idleHeartbeatIntervalMilliseconds: number,
): boolean {
  if (typeof value !== "object" || value === null || Array.isArray(value))
    return false;
  const descriptor = Object.getOwnPropertyDescriptor(value, "kind");
  if (descriptor === undefined
    || !("value" in descriptor)
    || descriptor.value !== "ready") {
    return false;
  }
  return ownDataPropertyMismatches(
    value,
    "protocolVersion",
    WORKER_RUNTIME_PROTOCOL_VERSION,
  )
    || ownDataPropertyMismatches(value, "epochToken", epochToken)
    || ownDataPropertyMismatches(
      value,
      "idleHeartbeatIntervalMilliseconds",
      idleHeartbeatIntervalMilliseconds,
    );
}

export class WorkerEpochTokenAllocator {
  readonly #maximumToken: number;
  #nextToken = 1;
  #exhausted = false;

  constructor(maximumToken = Number.MAX_SAFE_INTEGER) {
    this.#maximumToken = validatePositiveSafeInteger(
      maximumToken,
      "maximumToken",
    );
  }

  allocate():
    | { readonly kind: "allocated"; readonly token: WorkerEpochToken }
    | { readonly kind: "exhausted" } {
    if (this.#exhausted) return { kind: "exhausted" };
    const token = this.#nextToken;
    if (token === this.#maximumToken) {
      this.#exhausted = true;
    } else {
      this.#nextToken++;
    }
    return { kind: "allocated", token: brandEpochToken(token) };
  }
}

export class WorkerProbeSequenceAllocator {
  #nextSequence: number;

  constructor(nextSequence = 1) {
    this.#nextSequence = validatePositiveSafeInteger(
      nextSequence,
      "nextSequence",
    );
  }

  allocate():
    | { readonly kind: "allocated"; readonly sequence: number }
    | { readonly kind: "exhausted" } {
    if (this.#nextSequence > Number.MAX_SAFE_INTEGER)
      return { kind: "exhausted" };
    const sequence = this.#nextSequence;
    if (sequence === Number.MAX_SAFE_INTEGER) {
      this.#nextSequence = Number.MAX_SAFE_INTEGER + 1;
    } else {
      this.#nextSequence++;
    }
    return { kind: "allocated", sequence };
  }
}

export type WorkerProducerClassRegistrationResult =
  | {
      readonly kind: "idle-compatible";
      readonly capability: WorkerIdleCompatible;
    }
  | {
      readonly kind: "epoch-work-required";
    };

export class WorkerProducerClassRegistry {
  readonly #idleAllowanceMilliseconds: number;
  readonly #classes = new Map<string, RegisteredProducerClass>();
  readonly #capabilities = new Set<WorkerIdleCompatible>();

  constructor(idleAllowanceMilliseconds: number) {
    this.#idleAllowanceMilliseconds = validatePositiveSafeInteger(
      idleAllowanceMilliseconds,
      "idleAllowanceMilliseconds",
    );
  }

  get idleAllowanceMilliseconds(): number {
    return this.#idleAllowanceMilliseconds;
  }

  register(
    name: string,
    allowance: WorkerLivenessAllowance,
    structuralBoundMilliseconds: number | null,
  ): WorkerProducerClassRegistrationResult {
    if (this.#classes.has(name))
      throw new Error(`Producer class ${name} is already registered.`);
    if (allowance.kind === "bounded") {
      validatePositiveSafeInteger(
        allowance.maxSilentActiveMilliseconds,
        "maxSilentActiveMilliseconds",
      );
    }
    if (structuralBoundMilliseconds !== null) {
      validatePositiveSafeInteger(
        structuralBoundMilliseconds,
        "structuralBoundMilliseconds",
      );
      if (allowance.kind === "bounded"
        && structuralBoundMilliseconds
          > allowance.maxSilentActiveMilliseconds) {
        throw new RangeError(
          "structuralBoundMilliseconds must not exceed the producer "
            + "allowance.",
        );
      }
    }

    let capability: WorkerIdleCompatible | null = null;
    if (allowance.kind === "bounded"
      && allowance.maxSilentActiveMilliseconds
        <= this.#idleAllowanceMilliseconds
      && structuralBoundMilliseconds !== null
      && structuralBoundMilliseconds <= this.#idleAllowanceMilliseconds) {
      capability = Object.freeze({
        [workerIdleCompatibleBrand]: "WorkerIdleCompatible",
      });
      this.#capabilities.add(capability);
    }
    this.#classes.set(name, {
      name,
      allowance,
      structuralBoundMilliseconds,
      capability,
    });
    return capability === null
      ? { kind: "epoch-work-required" }
      : { kind: "idle-compatible", capability };
  }

  classify(name: string): WorkerProducerClassRegistrationResult {
    const registered = this.#classes.get(name);
    if (registered?.capability === undefined
      || registered.capability === null) {
      return { kind: "epoch-work-required" };
    }
    return {
      kind: "idle-compatible",
      capability: registered.capability,
    };
  }

  acceptsCapability(capability: WorkerIdleCompatible): boolean {
    return this.#capabilities.has(capability);
  }

  allowance(name: string): WorkerLivenessAllowance | null {
    return this.#classes.get(name)?.allowance ?? null;
  }

  acceptsLeaseAllowance(allowance: WorkerLivenessAllowance): boolean {
    for (const registered of this.#classes.values()) {
      if (sameAllowance(registered.allowance, allowance)) return true;
    }
    return false;
  }
}

export class WorkerRuntimeHost<TBootstrap, TDiagnostic> {
  readonly #options: WorkerRuntimeHostOptions<TBootstrap, TDiagnostic>;
  readonly #epochTokens: WorkerEpochTokenAllocator;
  readonly #producerClasses: WorkerProducerClassRegistry;
  readonly #registrations = new Set<string>();
  readonly #unsubscribeClock: () => void;
  readonly #unsubscribeLifecycle: () => void;
  #current: MainEpoch<TDiagnostic> | null = null;
  #disposed = false;

  constructor(
    options: WorkerRuntimeHostOptions<TBootstrap, TDiagnostic>,
  ) {
    const idleHeartbeatIntervalMilliseconds = validatePositiveSafeInteger(
      options.idleHeartbeatIntervalMilliseconds,
      "idleHeartbeatIntervalMilliseconds",
    );
    const schedulingToleranceMilliseconds = validateNonNegativeSafeInteger(
      options.schedulingToleranceMilliseconds ?? 0,
      "schedulingToleranceMilliseconds",
    );
    validatePositiveSafeInteger(
      options.startupBudgetMilliseconds,
      "startupBudgetMilliseconds",
    );
    validatePositiveSafeInteger(
      options.controlResponseGraceMilliseconds,
      "controlResponseGraceMilliseconds",
    );
    validatePositiveSafeInteger(
      options.drainBudgetMilliseconds,
      "drainBudgetMilliseconds",
    );
    validatePositiveSafeInteger(
      options.maximumOperationSequence ?? Number.MAX_SAFE_INTEGER,
      "maximumOperationSequence",
    );
    const idleAllowanceMilliseconds = validatePositiveSafeInteger(
      idleHeartbeatIntervalMilliseconds + schedulingToleranceMilliseconds,
      "idleAllowanceMilliseconds",
    );
    if (options.producerClasses.idleAllowanceMilliseconds
      !== idleAllowanceMilliseconds) {
      throw new RangeError(
        "producerClasses must use the host idle allowance.",
      );
    }
    this.#options = options;
    this.#epochTokens = new WorkerEpochTokenAllocator(
      options.maximumEpochToken,
    );
    this.#producerClasses = options.producerClasses;
    this.#unsubscribeClock = options.clock.subscribe(() => {
      this.#evaluateTime();
    });
    this.#unsubscribeLifecycle = options.lifecycle.subscribe({
      suspended: () => {
        const epoch = this.#current;
        if (epoch !== null) epoch.suspended = true;
      },
      resumed: () => {
        const epoch = this.#current;
        if (epoch === null) return;
        epoch.suspended = false;
        if (epoch.phase === "ready" || epoch.phase === "suspect")
          this.#recoverPostReadiness(epoch);
      },
      mainLoopRecovered: gapActiveMilliseconds => {
        validateNonNegativeSafeInteger(
          gapActiveMilliseconds,
          "gapActiveMilliseconds",
        );
        const epoch = this.#current;
        if (epoch === null) return;
        for (const command of epoch.commands.values()) {
          if (!command.responded) command.dueAt += gapActiveMilliseconds;
        }
        if (epoch.phase === "starting" || epoch.phase === "flushing") {
          epoch.startupDeadline += gapActiveMilliseconds;
        } else if (epoch.phase === "ready" || epoch.phase === "suspect") {
          this.#recoverPostReadiness(epoch);
        } else if (epoch.phase === "draining"
          && epoch.drainDeadline !== null) {
          epoch.drainDeadline += gapActiveMilliseconds;
        }
      },
    });
  }

  dispose(): void {
    if (this.#disposed) return;
    this.#disposed = true;
    this.#unsubscribeClock();
    this.#unsubscribeLifecycle();
    const epoch = this.#current;
    if (epoch !== null && epoch.phase !== "closed")
      this.restart();
  }

  registerOperation<
    TInput,
    TValue,
    TError,
    TOperationDiagnostic,
    TProgress,
    TPreparationError,
  >(
    registration: WorkerRuntimeOperationRegistration<
      TInput,
      TValue,
      TError,
      TOperationDiagnostic,
      TProgress,
      TPreparationError
    >,
  ): OperationProducerAdapter<
    TInput,
    TValue,
    TError,
    TProgress,
    TPreparationError
  > {
    if (this.#registrations.has(registration.kind)) {
      return {
        prepare: () => ({
          kind: "rejected",
          error: registration.mapPreparationError({
            kind: "operation-kind-already-registered",
          }),
        }),
      };
    }
    if (registration.allowance.kind === "bounded") {
      validatePositiveSafeInteger(
        registration.allowance.maxSilentActiveMilliseconds,
        "maxSilentActiveMilliseconds",
      );
    }
    this.#registrations.add(registration.kind);
    return {
      prepare: (identity, input, sink) =>
        this.#prepareOperation(registration, identity, input, sink),
    };
  }

  start(bootstrap: TBootstrap): WorkerRuntimeEpochStartResult {
    if (this.#disposed)
      return { kind: "rejected", reason: "host-disposed" };
    const current = this.#current;
    if (current !== null && current.phase !== "closed")
      return { kind: "rejected", reason: "epoch-active" };

    const encoded = this.#options.bootstrap.encode(bootstrap);
    if (encoded.kind === "rejected") {
      return {
        kind: "rejected",
        reason: encoded.reason === "oversized"
          ? "bootstrap-oversized"
          : "bootstrap-invalid",
        detail: encoded.cause ?? encoded.message,
      };
    }

    const allocation = this.#epochTokens.allocate();
    if (allocation.kind === "exhausted") {
      const diagnostic = this.#options.createDiagnostic(
        "epoch-token-exhausted",
        "Worker epoch-token allocation exhausted.",
      );
      this.#reportDiagnostic({
        kind: "epoch-token-exhausted",
        diagnostic,
      });
      return { kind: "rejected", reason: "epoch-token-exhausted" };
    }

    let transport: WorkerRuntimeTransportBinding;
    try {
      transport = this.#options.transport.create();
    } catch (error: unknown) {
      const failure: WorkerRuntimeFailure<TDiagnostic> = {
        kind: "startup",
        diagnostic: this.#options.createDiagnostic("startup", error),
      };
      this.#reportFailure(failure);
      return {
        kind: "rejected",
        reason: "worker-creation-failed",
        detail: error,
      };
    }
    const now = this.#options.clock.now();
    const epoch: MainEpoch<TDiagnostic> = {
      token: allocation.token,
      transport,
      source: transport.source,
      startupStartedAt: now,
      startupDeadline: now + this.#options.startupBudgetMilliseconds,
      detach: null,
      phase: "starting",
      closure: null,
      preparationHighWater: 0,
      operationHighWater: 0,
      preparedOperations: new Map(),
      flushingPreparedOperations: false,
      operations: new Map(),
      held: [],
      commands: new Map(),
      probeSequences: this.#options.createProbeSequenceAllocator?.()
        ?? new WorkerProbeSequenceAllocator(),
      probe: null,
      deferredControlProbe: false,
      lastTaskEvidenceOrigin: null,
      watchdogStageOrigin: null,
      workHighWater: 0,
      epochWork: new Map(),
      hadUnboundedAllowance: false,
      drainDeadline: null,
      suspended: false,
      preparedBindings: 0,
      producerCallouts: 0,
      closurePublicationActive: false,
      terminationFinalizing: false,
      terminationFinalized: false,
      realmReleased: false,
    };
    this.#current = epoch;
    try {
      epoch.detach = transport.bind({
        message: (source, data) => {
          this.receiveMessage(source, data);
        },
        error: (source, diagnostic) => {
          this.receiveWorkerError(source, diagnostic);
        },
        messageError: (source, diagnostic) => {
          this.receiveWorkerMessageError(source, diagnostic);
        },
      });
    } catch (error: unknown) {
      this.#fail(epoch, "startup", error, true);
      return {
        kind: "rejected",
        reason: "worker-creation-failed",
        detail: error,
      };
    }

    const initialization = this.#post(epoch, {
      protocolVersion: WORKER_RUNTIME_PROTOCOL_VERSION,
      epochToken: epoch.token,
      kind: "initialize",
      bootstrap: encoded.value,
      idleHeartbeatIntervalMilliseconds:
        this.#options.idleHeartbeatIntervalMilliseconds,
      idleAllowanceMilliseconds: this.#idleAllowance(),
    });
    if (initialization.kind === "failed") {
      return {
        kind: "rejected",
        reason: "worker-creation-failed",
        detail: initialization.error,
      };
    }
    return { kind: "started", epochToken: epoch.token };
  }

  restart(): void {
    const epoch = this.#current;
    if (epoch === null || epoch.phase === "closed") return;
    this.#commitClosure(epoch, {
      kind: "planned-restart",
      reason: "worker-restarted",
    }, true);
  }

  receiveMessage(source: WorkerRuntimeSource, data: unknown): void {
    const epoch = this.#current;
    if (epoch === null
      || epoch.phase === "closed"
      || source !== epoch.source) {
      return;
    }

    const decoded = decodeWorkerToMainEnvelope(data, epoch.token);
    if (decoded.kind === "failure") {
      const preReady = epoch.phase === "starting" || epoch.phase === "flushing";
      if (preReady && hasMismatchedReadyEcho(
        data,
        epoch.token,
        this.#options.idleHeartbeatIntervalMilliseconds,
      )) {
        this.#fail(
          epoch,
          "startup",
          decoded.failure,
          true,
        );
        return;
      }
      this.#protocolFailure(epoch, decoded.failure);
      return;
    }

    if (epoch.phase === "starting" || epoch.phase === "flushing") {
      this.#receiveStarting(epoch, decoded.value);
      return;
    }
    if (epoch.phase === "ready" || epoch.phase === "suspect") {
      this.#receiveReady(epoch, decoded.value);
      return;
    }
    if (epoch.phase === "draining")
      this.#receiveDraining(epoch, decoded.value);
  }

  receiveWorkerError(
    source: WorkerRuntimeSource,
    diagnostic: unknown,
  ): void {
    this.#receiveWorkerFault(source, diagnostic);
  }

  receiveWorkerMessageError(
    source: WorkerRuntimeSource,
    diagnostic: unknown,
  ): void {
    this.#receiveWorkerFault(source, diagnostic);
  }

  receiveWorkerCrash(
    source: WorkerRuntimeSource,
    diagnostic: unknown,
  ): void {
    const epoch = this.#current;
    if (epoch === null
      || epoch.phase === "closed"
      || source !== epoch.source) {
      return;
    }
    if (epoch.phase === "draining") {
      this.#hardTerminate(epoch);
      return;
    }
    this.#fail(epoch, "worker-crash", diagnostic, true);
  }

  snapshot(): WorkerRuntimeSnapshot<TDiagnostic> {
    const epoch = this.#current;
    if (epoch === null) {
      return {
        epochToken: null,
        phase: "absent",
        closure: null,
        heldOperations: 0,
        activeOperations: 0,
        compactControlRecords: 0,
        activeEpochWork: 0,
        outstandingProbeSequence: null,
        deferredControlProbe: false,
        lastTaskEvidenceOrigin: null,
      };
    }
    let activeOperations = 0;
    let compactControlRecords = 0;
    for (const record of epoch.operations.values()) {
      if (record.phase === "physically-closed") compactControlRecords++;
      else activeOperations++;
    }
    return {
      epochToken: epoch.token,
      phase: epoch.phase,
      closure: epoch.closure,
      heldOperations: epoch.held.length,
      activeOperations,
      compactControlRecords,
      activeEpochWork: epoch.epochWork.size,
      outstandingProbeSequence: epoch.probe?.sequence ?? null,
      deferredControlProbe: epoch.deferredControlProbe,
      lastTaskEvidenceOrigin: epoch.lastTaskEvidenceOrigin,
    };
  }

  #prepareOperation<
    TInput,
    TValue,
    TError,
    TOperationDiagnostic,
    TProgress,
    TPreparationError,
  >(
    registration: WorkerRuntimeOperationRegistration<
      TInput,
      TValue,
      TError,
      TOperationDiagnostic,
      TProgress,
      TPreparationError
    >,
    identity: OperationIdentity,
    input: TInput,
    sink: OperationProducerSink<TValue, TError, TProgress>,
  ): OperationPreparation<TPreparationError> {
    const reject = (
      error: WorkerRuntimePreparationError,
    ): OperationPreparation<TPreparationError> => ({
      kind: "rejected",
      error: registration.mapPreparationError(error),
    });
    const epoch = this.#current;
    if (epoch === null
      || (epoch.phase !== "starting"
        && epoch.phase !== "flushing"
        && epoch.phase !== "ready"
        && epoch.phase !== "suspect")) {
      return reject({ kind: "epoch-unavailable" });
    }
    if (!Number.isSafeInteger(identity.sequence) || identity.sequence <= 0) {
      return reject({ kind: "invalid-operation-reference" });
    }
    if (identity.sequence > this.#maximumOperationSequence()) {
      return reject({ kind: "operation-sequence-exhausted" });
    }
    if (epoch.preparationHighWater === this.#maximumOperationSequence()) {
      return reject({ kind: "operation-sequence-exhausted" });
    }
    if (identity.sequence <= epoch.preparationHighWater) {
      return reject({ kind: "operation-sequence-replayed" });
    }
    epoch.preparationHighWater = identity.sequence;
    const reservation: PreparedOperationReservation = {
      completion: null,
    };
    epoch.preparedOperations.set(identity.sequence, reservation);
    epoch.preparedBindings++;
    let preparedLifetimeReleased = false;
    const releasePreparedLifetime = (): void => {
      if (preparedLifetimeReleased) return;
      preparedLifetimeReleased = true;
      this.#releasePreparedBinding(epoch);
    };
    const skipPreparation = (): void => {
      this.#resolvePreparedOperation(
        epoch,
        reservation,
        () => undefined,
      );
      releasePreparedLifetime();
    };
    let encoded: ReturnType<typeof registration.encodeInput>;
    try {
      encoded = registration.encodeInput(input);
    } catch (error: unknown) {
      skipPreparation();
      throw error;
    }
    if (encoded.kind === "rejected") {
      skipPreparation();
      return reject({
        kind: "payload-rejected",
        reason: encoded.reason,
        message: encoded.message,
        ...(encoded.cause === undefined ? {} : { cause: encoded.cause }),
      });
    }

    let state: "prepared" | "activated" | "abandoned" = "prepared";
    let retainedEpoch: MainEpoch<TDiagnostic> | null = epoch;
    let retainedSink: OperationProducerSink<
      TValue,
      TError,
      TProgress
    > | null = sink;
    let retainedPayload: unknown = encoded.value;
    let activatedRecord: MainOperationRecord<TDiagnostic> | null = null;
    let activationAssigned = false;
    let pendingCancellation: OperationCancelReason | null = null;
    const binding: PreparedOperationProducer = {
      requestCancellation: reason => {
        if (activatedRecord === null || retainedEpoch === null) return;
        if (activationAssigned) {
          this.#cancelOperation(retainedEpoch, activatedRecord, reason);
        } else if (pendingCancellation === null) {
          pendingCancellation = reason;
        }
      },
      activate: () => {
        if (state !== "prepared") return;
        state = "activated";
        const assignedEpoch = retainedEpoch;
        const assignedSink = retainedSink;
        const payload = retainedPayload;
        retainedSink = null;
        retainedPayload = undefined;
        if (assignedEpoch === null || assignedSink === null) {
          skipPreparation();
          return;
        }
        const record = this.#createOperationRecord(
          assignedEpoch,
          registration,
          identity,
          payload,
          assignedSink,
        );
        activatedRecord = record;
        this.#resolvePreparedOperation(
          epoch,
          reservation,
          () => {
            activationAssigned = true;
            try {
              this.#activatePrepared(assignedEpoch, record);
              if (pendingCancellation !== null)
                this.#cancelOperation(
                  assignedEpoch,
                  record,
                  pendingCancellation,
                );
            } finally {
              releasePreparedLifetime();
            }
          },
        );
      },
      abandon: () => {
        if (state !== "prepared") return;
        state = "abandoned";
        retainedEpoch = null;
        retainedSink = null;
        retainedPayload = undefined;
        skipPreparation();
      },
    };
    return { kind: "prepared", binding };
  }

  #resolvePreparedOperation(
    epoch: MainEpoch<TDiagnostic>,
    reservation: PreparedOperationReservation,
    completion: () => void,
  ): void {
    if (reservation.completion !== null)
      throw new Error("Prepared operation reservation was resolved twice.");
    reservation.completion = completion;
    this.#flushPreparedOperations(epoch);
  }

  #flushPreparedOperations(epoch: MainEpoch<TDiagnostic>): void {
    if (epoch.flushingPreparedOperations) return;
    epoch.flushingPreparedOperations = true;
    try {
      while (true) {
        const next = epoch.preparedOperations.entries().next();
        if (next.done) return;
        const [sequence, reservation] = next.value;
        const completion = reservation.completion;
        if (completion === null) return;
        epoch.preparedOperations.delete(sequence);
        try {
          completion();
        } catch (error: unknown) {
          this.#reportCallbackError(error);
        }
      }
    } finally {
      epoch.flushingPreparedOperations = false;
    }
  }

  #createOperationRecord<
    TInput,
    TValue,
    TError,
    TOperationDiagnostic,
    TProgress,
    TPreparationError,
  >(
    epoch: MainEpoch<TDiagnostic>,
    registration: WorkerRuntimeOperationRegistration<
      TInput,
      TValue,
      TError,
      TOperationDiagnostic,
      TProgress,
      TPreparationError
    >,
    identity: OperationIdentity,
    payload: unknown,
    sink: OperationProducerSink<TValue, TError, TProgress>,
  ): MainOperationRecord<TDiagnostic> {
    let retainedSink: OperationProducerSink<
      TValue,
      TError,
      TProgress
    > | null = sink;
    let sealedClosure: WorkerEpochClosure<TDiagnostic> | null = null;
    let closurePublication: OperationTerminalPublication | null = null;
    const invoke = <TResult>(
      call: (
        current: OperationProducerSink<TValue, TError, TProgress>,
      ) => TResult,
    ): TResult | null => {
      const current = retainedSink;
      if (current === null) return null;
      epoch.producerCallouts++;
      try {
        return call(current);
      } catch (error: unknown) {
        this.#reportCallbackError(error);
        return null;
      } finally {
        this.#releaseProducerCallout(epoch);
      }
    };
    const reference: WorkerWireOperationReference = {
      operationId: identity.id,
      operationSequence: identity.sequence,
    };
    const record: MainOperationRecord<TDiagnostic> = {
      identity,
      reference,
      registration: {
        kind: registration.kind,
        allowance: registration.allowance,
      },
      payload,
      phase: "held",
      cancelReason: null,
      cancelSent: false,
      cancelAcknowledged: false,
      logicalClosureReported: false,
      quiescenceReported: false,
      receiveRejected: envelope => {
        const decoded = decodeRejectedPayload(
          envelope,
          registration.error,
          registration.diagnostic,
        );
        if (decoded.kind === "failure") return decoded;
        if (!record.logicalClosureReported) {
          record.logicalClosureReported = true;
          invoke(current => {
            current.reportTerminal({
              kind: "failed",
              error: decoded.value.error,
            });
          });
        }
        return { kind: "success" };
      },
      receiveProgress: envelope => {
        const decoded = decodeProgressPayload(
          envelope,
          registration.progress,
        );
        if (decoded.kind === "failure") return decoded;
        invoke(current => {
          current.reportProgress(decoded.value.payload);
        });
        return { kind: "success" };
      },
      receiveSettled: envelope => {
        const decoded = decodeSettledPayload(envelope, {
          value: registration.value,
          error: registration.error,
          diagnostic: registration.diagnostic,
        });
        if (decoded.kind === "failure") return decoded;
        if (!record.logicalClosureReported) {
          record.logicalClosureReported = true;
          const settlement = decoded.value.settlement;
          if (settlement.kind === "failed"
            && settlement.failureKind === "unexpected") {
            invoke(current => {
              current.reportUnexpectedTerminal(
                settlement.error,
                settlement.diagnostic,
              );
            });
          } else {
            invoke(current => {
              if (settlement.kind === "succeeded") {
                current.reportTerminal({
                  kind: "succeeded",
                  value: settlement.value,
                });
              } else if (settlement.kind === "failed") {
                current.reportTerminal({
                  kind: "failed",
                  error: settlement.error,
                });
              } else {
                current.reportTerminal({
                  kind: "canceled",
                  reason: settlement.reason,
                });
              }
            });
          }
        }
        return { kind: "success" };
      },
      sealClosure: closure => {
        if (record.logicalClosureReported) return;
        record.logicalClosureReported = true;
        sealedClosure = closure;
      },
      commitClosure: () => {
        const closure = sealedClosure;
        if (closure === null) return;
        sealedClosure = null;
        if (closure.kind === "planned-restart") {
          closurePublication = invoke(current =>
            current.commitTerminal({
              kind: "canceled",
              reason: closure.reason,
            }));
        } else {
          closurePublication = invoke(current =>
            current.commitTerminal({
              kind: "failed",
              error: registration.boundaryErrors[closure.failure.kind],
            }));
        }
      },
      publishClosure: () => {
        const publication = closurePublication;
        if (publication === null) return;
        closurePublication = null;
        invoke(() => publication.publish());
      },
      reportCancellation: reason => {
        if (record.logicalClosureReported) return;
        record.logicalClosureReported = true;
        invoke(current => {
          current.reportTerminal({ kind: "canceled", reason });
        });
      },
      reportQuiescence: () => {
        if (record.quiescenceReported) return;
        record.quiescenceReported = true;
        invoke(current => {
          current.reportQuiesced();
        });
      },
      release: () => {
        retainedSink = null;
        sealedClosure = null;
        closurePublication = null;
        record.payload = undefined;
      },
    };
    return record;
  }

  #activatePrepared(
    epoch: MainEpoch<TDiagnostic>,
    record: MainOperationRecord<TDiagnostic>,
  ): void {
    const reference = record.reference;

    if (epoch.operationHighWater === this.#maximumOperationSequence()
      || reference.operationSequence <= epoch.operationHighWater) {
      this.#reportOperationClosure(
        record,
        {
          kind: "unexpected-failure",
          failure: {
            kind: "protocol",
            diagnostic: this.#options.createDiagnostic(
              "protocol",
              "Operation sequence was not greater than epoch high-water.",
            ),
          },
        },
      );
      this.#reportOperationQuiescence(record);
      this.#releaseOperationPayload(record);
      return;
    }
    if (reference.operationSequence > this.#maximumOperationSequence()) {
      this.#reportOperationClosure(
        record,
        {
          kind: "unexpected-failure",
          failure: {
            kind: "protocol",
            diagnostic: this.#options.createDiagnostic(
              "protocol",
              "Operation sequence exceeded the configured safe bound.",
            ),
          },
        },
      );
      this.#reportOperationQuiescence(record);
      this.#releaseOperationPayload(record);
      return;
    }
    epoch.operationHighWater = reference.operationSequence;
    const existing = epoch.operations.get(reference.operationId);
    if (existing !== undefined) {
      this.#fail(
        epoch,
        "protocol",
        "An active operation ID was assigned a newer sequence.",
        false,
      );
      const closure = epoch.closure;
      if (closure !== null) this.#reportOperationClosure(record, closure);
      this.#reportOperationQuiescence(record);
      this.#releaseOperationPayload(record);
      return;
    }
    epoch.operations.set(reference.operationId, record);

    if (epoch.phase === "starting" || epoch.phase === "flushing") {
      epoch.held.push(record);
      epoch.held.sort(
        (left, right) =>
          left.reference.operationSequence - right.reference.operationSequence,
      );
      return;
    }
    if (epoch.phase === "ready" || epoch.phase === "suspect") {
      this.#postStart(epoch, record);
      return;
    }

    const closure = epoch.closure;
    if (closure === null)
      throw new Error("A closed epoch must retain its committed closure.");
    this.#reportOperationClosure(record, closure);
    this.#reportOperationQuiescence(record);
    this.#releaseOperationPayload(record);
    epoch.operations.delete(reference.operationId);
  }

  #cancelOperation(
    epoch: MainEpoch<TDiagnostic>,
    record: MainOperationRecord<TDiagnostic>,
    reason: OperationCancelReason,
  ): void {
    const current = epoch.operations.get(record.identity.id);
    if (current !== record
      || record.cancelReason !== null
      || record.logicalClosureReported) {
      return;
    }
    record.cancelReason = reason;
    if (record.phase === "held") {
      const index = epoch.held.indexOf(record);
      if (index >= 0) epoch.held.splice(index, 1);
      record.reportCancellation(reason);
      record.phase = "physically-closed";
      this.#reportOperationQuiescence(record);
      this.#releaseOperationPayload(record);
      epoch.operations.delete(record.reference.operationId);
      return;
    }
    if (record.phase === "awaiting-admission" || record.phase === "accepted")
      this.#postCancel(epoch, record, reason);
  }

  #postStart(
    epoch: MainEpoch<TDiagnostic>,
    record: MainOperationRecord<TDiagnostic>,
  ): void {
    record.phase = "awaiting-admission";
    this.#trackCommand(epoch, "start", record.reference);
    this.#post(epoch, {
      protocolVersion: WORKER_RUNTIME_PROTOCOL_VERSION,
      epochToken: epoch.token,
      kind: "start",
      operation: record.reference,
      operationKind: record.registration.kind,
      payload: record.payload,
    });
    if (record.cancelReason !== null)
      this.#postCancel(epoch, record, record.cancelReason);
  }

  #postCancel(
    epoch: MainEpoch<TDiagnostic>,
    record: MainOperationRecord<TDiagnostic>,
    reason: OperationCancelReason,
  ): void {
    if (record.cancelSent || record.phase === "held") return;
    record.cancelSent = true;
    this.#trackCommand(epoch, "cancel", record.reference);
    this.#post(epoch, {
      protocolVersion: WORKER_RUNTIME_PROTOCOL_VERSION,
      epochToken: epoch.token,
      kind: "cancel",
      operation: record.reference,
      reason,
    });
  }

  #trackCommand(
    epoch: MainEpoch<TDiagnostic>,
    kind: "start" | "cancel",
    reference: WorkerWireOperationReference,
  ): void {
    const key = commandKey(kind, reference);
    const command: DeferredCommand = {
      key,
      dueAt: this.#options.clock.now()
        + this.#options.controlResponseGraceMilliseconds,
      responded: false,
      probeMark: epoch.probe?.sequence ?? null,
    };
    epoch.commands.set(key, command);
  }

  #post(
    epoch: MainEpoch<TDiagnostic>,
    message: RawMainToWorkerEnvelope,
  ): WorkerPostResult {
    if (epoch.phase === "closed")
      return { kind: "failed", error: "Worker epoch is closed." };
    try {
      epoch.source.send(message);
      return { kind: "sent" };
    } catch (error: unknown) {
      this.#fail(epoch, "worker-crash", error, true);
      return { kind: "failed", error };
    }
  }

  #receiveStarting(
    epoch: MainEpoch<TDiagnostic>,
    envelope: RawWorkerToMainEnvelope,
  ): void {
    if (envelope.kind === "ready") {
      if (epoch.phase !== "starting"
        || envelope.idleHeartbeatIntervalMilliseconds
          !== this.#options.idleHeartbeatIntervalMilliseconds
        || this.#options.clock.now() >= epoch.startupDeadline) {
        this.#fail(
          epoch,
          "startup",
          envelope,
          true,
        );
        return;
      }
      epoch.phase = "flushing";
      while (epoch.held.length > 0 && epoch.phase === "flushing") {
        const record = epoch.held.shift();
        if (record !== undefined) this.#postStart(epoch, record);
      }
      if (epoch.phase !== "flushing") return;
      epoch.phase = "ready";
      epoch.lastTaskEvidenceOrigin = this.#options.clock.now();
      epoch.watchdogStageOrigin = null;
      epoch.hadUnboundedAllowance = this.#currentAllowance(epoch) === null;
      return;
    }
    if (envelope.kind === "startup-failed") {
      const diagnostic = decodeStartupFailedPayload(
        envelope,
        this.#options.bootstrap.diagnostic,
      );
      if (diagnostic.kind === "failure") {
        this.#protocolFailure(epoch, diagnostic.failure);
        return;
      }
      this.#fail(epoch, "startup", diagnostic.value.diagnostic, true);
      return;
    }
    this.#fail(epoch, "protocol", envelope, true);
  }

  #receiveReady(
    epoch: MainEpoch<TDiagnostic>,
    envelope: RawWorkerToMainEnvelope,
  ): void {
    switch (envelope.kind) {
      case "accepted":
        this.#receiveAccepted(epoch, envelope);
        return;
      case "rejected":
        this.#receiveRejected(epoch, envelope);
        return;
      case "cancel-acknowledged":
        this.#receiveCancelAcknowledged(epoch, envelope);
        return;
      case "progress":
        this.#receiveProgress(epoch, envelope);
        return;
      case "settled":
        this.#receiveSettled(epoch, envelope);
        return;
      case "heartbeat":
        this.#recordTaskEvidence(epoch);
        return;
      case "probe-acknowledged":
        this.#receiveProbeAcknowledged(epoch, envelope.probeSequence);
        return;
      case "epoch-work-started":
        this.#receiveEpochWorkStarted(
          epoch,
          envelope.workSequence,
          envelope.allowance,
        );
        return;
      case "epoch-work-finished":
        this.#receiveEpochWorkFinished(epoch, envelope.workSequence);
        return;
      case "epoch-failed": {
        const decoded = decodeEpochFailedPayload(
          envelope,
          this.#options.diagnostic,
        );
        if (decoded.kind === "failure") {
          this.#protocolFailure(epoch, decoded.failure);
          return;
        }
        this.#fail(
          epoch,
          "worker-declared",
          decoded.value.diagnostic,
          false,
        );
        return;
      }
      case "ready":
      case "startup-failed":
        this.#protocolFailure(epoch, envelope);
        return;
    }
  }

  #receiveDraining(
    epoch: MainEpoch<TDiagnostic>,
    envelope: RawWorkerToMainEnvelope,
  ): void {
    switch (envelope.kind) {
      case "rejected":
        this.#receiveRejected(epoch, envelope, true);
        return;
      case "settled":
        this.#receiveSettled(epoch, envelope, true);
        return;
      case "cancel-acknowledged":
        this.#receiveCancelAcknowledged(epoch, envelope, true);
        return;
      case "epoch-work-finished":
        this.#receiveEpochWorkFinished(epoch, envelope.workSequence, true);
        return;
      case "accepted":
        this.#receiveAccepted(epoch, envelope, true);
        return;
      case "epoch-work-started":
        this.#receiveEpochWorkStarted(
          epoch,
          envelope.workSequence,
          envelope.allowance,
          true,
        );
        return;
      case "heartbeat":
      case "probe-acknowledged":
      case "progress":
      case "ready":
      case "startup-failed":
      case "epoch-failed":
        return;
    }
  }

  #findOperation(
    epoch: MainEpoch<TDiagnostic>,
    reference: WorkerWireOperationReference,
  ): MainOperationRecord<TDiagnostic> | null {
    const record = epoch.operations.get(reference.operationId);
    return record !== undefined
      && record.reference.operationSequence === reference.operationSequence
      ? record
      : null;
  }

  #receiveAccepted(
    epoch: MainEpoch<TDiagnostic>,
    envelope: Extract<RawWorkerToMainEnvelope, { readonly kind: "accepted" }>,
    draining = false,
  ): void {
    const record = this.#findOperation(epoch, envelope.operation);
    if (record === null
      || record.phase !== "awaiting-admission"
      || !sameAllowance(record.registration.allowance, envelope.allowance)) {
      if (!draining) this.#protocolFailure(epoch, envelope);
      return;
    }
    if (!draining
      && !this.#commitCommandResponse(epoch, "start", envelope.operation)) {
      return;
    }
    record.phase = "accepted";
    if (!draining) {
      this.#recordTaskEvidence(epoch);
      this.#topologyChanged(epoch);
    }
  }

  #receiveRejected(
    epoch: MainEpoch<TDiagnostic>,
    envelope: Extract<RawWorkerToMainEnvelope, { readonly kind: "rejected" }>,
    draining = false,
  ): void {
    const record = this.#findOperation(epoch, envelope.operation);
    if (record === null || record.phase !== "awaiting-admission") {
      if (!draining) this.#protocolFailure(epoch, envelope);
      return;
    }
    if (!draining
      && !this.#commitCommandResponse(epoch, "start", envelope.operation)) {
      return;
    }
    const received = record.receiveRejected(envelope);
    if (received.kind === "failure") {
      if (!draining) this.#protocolFailure(epoch, received.failure);
      return;
    }
    record.phase = "physically-closed";
    this.#reportOperationQuiescence(record);
    this.#releaseOperationPayload(record);
    if (!draining) this.#recordTaskEvidence(epoch);
    this.#retireOperationIfComplete(epoch, record);
    this.#topologyChanged(epoch);
    this.#closeDrainedRealmIfReleased(epoch);
  }

  #receiveCancelAcknowledged(
    epoch: MainEpoch<TDiagnostic>,
    envelope: Extract<
      RawWorkerToMainEnvelope,
      { readonly kind: "cancel-acknowledged" }
    >,
    draining = false,
  ): void {
    const record = this.#findOperation(epoch, envelope.operation);
    if (record === null
      || !record.cancelSent
      || record.cancelAcknowledged
      || record.phase === "held"
      || record.phase === "awaiting-admission") {
      if (!draining) this.#protocolFailure(epoch, envelope);
      return;
    }
    if (!draining
      && !this.#commitCommandResponse(epoch, "cancel", envelope.operation)) {
      return;
    }
    record.cancelAcknowledged = true;
    if (!draining) this.#recordTaskEvidence(epoch);
    this.#retireOperationIfComplete(epoch, record);
    this.#closeDrainedRealmIfReleased(epoch);
  }

  #receiveProgress(
    epoch: MainEpoch<TDiagnostic>,
    envelope: Extract<RawWorkerToMainEnvelope, { readonly kind: "progress" }>,
  ): void {
    const record = this.#findOperation(epoch, envelope.operation);
    if (record === null || record.phase !== "accepted") {
      this.#protocolFailure(epoch, envelope);
      return;
    }
    const received = record.receiveProgress(envelope);
    if (received.kind === "failure") {
      this.#protocolFailure(epoch, received.failure);
      return;
    }
  }

  #receiveSettled(
    epoch: MainEpoch<TDiagnostic>,
    envelope: Extract<RawWorkerToMainEnvelope, { readonly kind: "settled" }>,
    draining = false,
  ): void {
    const record = this.#findOperation(epoch, envelope.operation);
    if (record === null || record.phase !== "accepted") {
      if (!draining) this.#protocolFailure(epoch, envelope);
      return;
    }
    const received = record.receiveSettled(envelope);
    if (received.kind === "failure") {
      if (!draining) this.#protocolFailure(epoch, received.failure);
      return;
    }
    record.phase = "physically-closed";
    this.#reportOperationQuiescence(record);
    this.#releaseOperationPayload(record);
    this.#retireOperationIfComplete(epoch, record);
    this.#topologyChanged(epoch);
    this.#closeDrainedRealmIfReleased(epoch);
  }

  #receiveEpochWorkStarted(
    epoch: MainEpoch<TDiagnostic>,
    sequence: number,
    allowance: WorkerLivenessAllowance,
    draining = false,
  ): void {
    if (sequence <= epoch.workHighWater
      || !this.#producerClasses.acceptsLeaseAllowance(allowance)) {
      if (!draining) this.#protocolFailure(epoch, { sequence, allowance });
      return;
    }
    epoch.workHighWater = sequence;
    epoch.epochWork.set(sequence, allowance);
    if (!draining) this.#topologyChanged(epoch);
  }

  #receiveEpochWorkFinished(
    epoch: MainEpoch<TDiagnostic>,
    sequence: number,
    draining = false,
  ): void {
    if (!epoch.epochWork.delete(sequence)) {
      if (!draining) this.#protocolFailure(epoch, { sequence });
      return;
    }
    this.#topologyChanged(epoch);
    this.#closeDrainedRealmIfReleased(epoch);
  }

  #commitCommandResponse(
    epoch: MainEpoch<TDiagnostic>,
    kind: "start" | "cancel",
    reference: WorkerWireOperationReference,
  ): boolean {
    const key = commandKey(kind, reference);
    const command = epoch.commands.get(key);
    if (command === undefined || command.responded) {
      this.#protocolFailure(epoch, { key, reason: "duplicate-response" });
      return false;
    }
    if (epoch.probe !== null
      && command.probeMark === epoch.probe.sequence) {
      this.#fail(
        epoch,
        "control-response",
        {
          command: key,
          missingProbeAcknowledgment: epoch.probe.sequence,
        },
        false,
      );
      return false;
    }
    command.responded = true;
    if (!this.#commandReferencedByProbe(epoch, command))
      epoch.commands.delete(key);
    return true;
  }

  #commandReferencedByProbe(
    epoch: MainEpoch<TDiagnostic>,
    command: DeferredCommand,
  ): boolean {
    return epoch.probe !== null
      && (command.probeMark === epoch.probe.sequence
        || epoch.probe.coveredResponses.includes(command));
  }

  #receiveProbeAcknowledged(
    epoch: MainEpoch<TDiagnostic>,
    sequence: number,
  ): void {
    const probe = epoch.probe;
    if (probe === null || probe.sequence !== sequence) {
      this.#protocolFailure(epoch, {
        sequence,
        outstanding: probe?.sequence ?? null,
      });
      return;
    }
    const omitted = probe.coveredResponses.find(command => !command.responded);
    if (omitted !== undefined) {
      this.#fail(
        epoch,
        "control-response",
        { command: omitted.key, probeSequence: sequence },
        false,
      );
      return;
    }
    const exhausted = sequence === Number.MAX_SAFE_INTEGER;
    const deferredControlProbe = epoch.deferredControlProbe;
    this.#retireProbe(epoch);
    if (exhausted) {
      this.#fail(
        epoch,
        "probe-exhaustion",
        { probeSequence: sequence },
        false,
      );
      return;
    }
    this.#recordTaskEvidence(epoch);
    if (deferredControlProbe && this.#hasUnresolvedCommands(epoch))
      this.#sendProbe(epoch, false);
  }

  #retireProbe(
    epoch: MainEpoch<TDiagnostic>,
  ): void {
    const sequence = epoch.probe?.sequence;
    if (sequence === undefined) return;
    epoch.probe = null;
    for (const [key, command] of epoch.commands) {
      if (command.probeMark === sequence) command.probeMark = null;
      if (command.responded) epoch.commands.delete(key);
    }
    epoch.deferredControlProbe = false;
  }

  #hasUnresolvedCommands(
    epoch: MainEpoch<TDiagnostic>,
  ): boolean {
    for (const command of epoch.commands.values()) {
      if (!command.responded) return true;
    }
    return false;
  }

  #recordTaskEvidence(
    epoch: MainEpoch<TDiagnostic>,
  ): void {
    if (epoch.phase !== "ready" && epoch.phase !== "suspect") return;
    epoch.lastTaskEvidenceOrigin = this.#options.clock.now();
    epoch.watchdogStageOrigin = null;
    epoch.phase = "ready";
  }

  #topologyChanged(
    epoch: MainEpoch<TDiagnostic>,
  ): void {
    if (epoch.phase !== "ready" && epoch.phase !== "suspect") return;
    const unbounded = this.#currentAllowance(epoch) === null;
    if (epoch.hadUnboundedAllowance && !unbounded) {
      const now = this.#options.clock.now();
      if (epoch.phase === "suspect") epoch.watchdogStageOrigin = now;
      else epoch.lastTaskEvidenceOrigin = now;
      epoch.hadUnboundedAllowance = false;
      return;
    }
    epoch.hadUnboundedAllowance = unbounded;
    this.#evaluateTime();
  }

  #currentAllowance(
    epoch: MainEpoch<TDiagnostic>,
  ): number | null {
    let maximum = this.#idleAllowance();
    for (const record of epoch.operations.values()) {
      if (record.phase !== "accepted") continue;
      const allowance = record.registration.allowance;
      if (allowance.kind === "unbounded") return null;
      maximum = Math.max(
        maximum,
        allowance.maxSilentActiveMilliseconds,
      );
    }
    for (const allowance of epoch.epochWork.values()) {
      if (allowance.kind === "unbounded") return null;
      maximum = Math.max(
        maximum,
        allowance.maxSilentActiveMilliseconds,
      );
    }
    return maximum;
  }

  #idleAllowance(): number {
    return this.#options.idleHeartbeatIntervalMilliseconds
      + (this.#options.schedulingToleranceMilliseconds ?? 0);
  }

  #maximumOperationSequence(): number {
    return this.#options.maximumOperationSequence ?? Number.MAX_SAFE_INTEGER;
  }

  #recoverPostReadiness(
    epoch: MainEpoch<TDiagnostic>,
  ): void {
    const now = this.#options.clock.now();
    epoch.phase = "ready";
    epoch.lastTaskEvidenceOrigin = now;
    epoch.watchdogStageOrigin = null;
    epoch.hadUnboundedAllowance = this.#currentAllowance(epoch) === null;
  }

  #evaluateTime(): void {
    const epoch = this.#current;
    if (epoch === null || epoch.phase === "closed" || epoch.suspended) return;
    const now = this.#options.clock.now();
    if ((epoch.phase === "starting" || epoch.phase === "flushing")
      && now >= epoch.startupDeadline) {
      this.#fail(
        epoch,
        "startup",
        "The worker did not become ready within its active-time budget.",
        true,
      );
      return;
    }
    if (epoch.phase === "draining") {
      if (epoch.drainDeadline !== null && now >= epoch.drainDeadline)
        this.#hardTerminate(epoch);
      return;
    }
    if (epoch.phase !== "ready" && epoch.phase !== "suspect") return;

    this.#evaluateControlGrace(epoch, now);
    if (epoch.phase !== "ready" && epoch.phase !== "suspect") return;
    const allowance = this.#currentAllowance(epoch);
    if (allowance === null) return;
    if (epoch.phase === "ready") {
      const origin = epoch.lastTaskEvidenceOrigin;
      if (origin !== null && now >= origin + allowance) {
        epoch.phase = "suspect";
        if (epoch.probe === null) {
          this.#sendProbe(epoch, true);
        } else {
          epoch.probe.watchdogAdopted = true;
          epoch.probe.watchdogOrigin = now;
          epoch.watchdogStageOrigin = now;
        }
      }
      return;
    }
    const stageOrigin = epoch.watchdogStageOrigin;
    if (stageOrigin !== null && now >= stageOrigin + allowance) {
      this.#fail(
        epoch,
        "watchdog",
        "The worker task loop remained silent through both watchdog stages.",
        false,
      );
    }
  }

  #evaluateControlGrace(
    epoch: MainEpoch<TDiagnostic>,
    now: number,
  ): void {
    for (const command of epoch.commands.values()) {
      if (command.responded || now < command.dueAt) continue;
      const probe = epoch.probe;
      if (probe === null) {
        this.#sendProbe(epoch, false);
        return;
      }
      if (!probe.coveredResponses.includes(command))
        epoch.deferredControlProbe = true;
    }
  }

  #sendProbe(
    epoch: MainEpoch<TDiagnostic>,
    watchdog: boolean,
  ): void {
    if (epoch.probe !== null
      || (epoch.phase !== "ready" && epoch.phase !== "suspect")) {
      return;
    }
    const allocation = epoch.probeSequences.allocate();
    if (allocation.kind === "exhausted") {
      this.#fail(
        epoch,
        "probe-exhaustion",
        "No probe sequence remains allocatable.",
        false,
      );
      return;
    }
    const coveredResponses = Object.freeze(
      [...epoch.commands.values()].filter(command => !command.responded),
    );
    const now = this.#options.clock.now();
    epoch.probe = {
      sequence: allocation.sequence,
      coveredResponses,
      watchdogAdopted: watchdog,
      watchdogOrigin: watchdog ? now : null,
    };
    if (watchdog) epoch.watchdogStageOrigin = now;
    this.#post(epoch, {
      protocolVersion: WORKER_RUNTIME_PROTOCOL_VERSION,
      epochToken: epoch.token,
      kind: "probe",
      probeSequence: allocation.sequence,
    });
  }

  #receiveWorkerFault(
    source: WorkerRuntimeSource,
    diagnostic: unknown,
  ): void {
    const epoch = this.#current;
    if (epoch === null
      || epoch.phase === "closed"
      || source !== epoch.source) {
      return;
    }
    if (epoch.phase === "draining") {
      return;
    }
    const immediate = epoch.phase === "starting" || epoch.phase === "flushing";
    this.#fail(epoch, "worker-message", diagnostic, immediate);
  }

  #protocolFailure(
    epoch: MainEpoch<TDiagnostic>,
    detail: unknown,
  ): void {
    const immediate = epoch.phase === "starting" || epoch.phase === "flushing";
    this.#fail(epoch, "protocol", detail, immediate);
  }

  #fail(
    epoch: MainEpoch<TDiagnostic>,
    kind: WorkerRuntimeFailureKind,
    detail: unknown,
    immediate: boolean,
  ): void {
    const failure: WorkerRuntimeFailure<TDiagnostic> = {
      kind,
      diagnostic: this.#options.createDiagnostic(kind, detail),
    };
    this.#commitClosure(epoch, {
      kind: "unexpected-failure",
      failure,
    }, immediate || kind === "worker-crash");
  }

  #commitClosure(
    epoch: MainEpoch<TDiagnostic>,
    closure: WorkerEpochClosure<TDiagnostic>,
    immediate: boolean,
  ): void {
    if (epoch.phase === "closed") return;
    if (epoch.closure !== null) {
      if (immediate) this.#hardTerminate(epoch);
      return;
    }
    epoch.closure = closure;
    epoch.phase = "draining";
    epoch.drainDeadline = this.#options.clock.now()
      + this.#options.drainBudgetMilliseconds;
    epoch.closurePublicationActive = true;
    try {
      for (const record of epoch.operations.values())
        this.#sealOperationClosure(record, closure);
      for (const record of epoch.operations.values())
        this.#commitOperationClosure(record);
      for (const record of epoch.operations.values())
        this.#publishOperationClosure(record);
      if (closure.kind === "unexpected-failure")
        this.#reportFailure(closure.failure);

      if (immediate) {
        this.#hardTerminate(epoch);
        return;
      }
      this.#closeDrainedRealmIfReleased(epoch);
    } finally {
      epoch.closurePublicationActive = false;
      this.#finalizeHardTerminationIfReady(epoch);
    }
  }

  #reportOperationClosure(
    record: MainOperationRecord<TDiagnostic>,
    closure: WorkerEpochClosure<TDiagnostic>,
  ): void {
    this.#sealOperationClosure(record, closure);
    this.#commitOperationClosure(record);
    this.#publishOperationClosure(record);
  }

  #sealOperationClosure(
    record: MainOperationRecord<TDiagnostic>,
    closure: WorkerEpochClosure<TDiagnostic>,
  ): void {
    record.sealClosure(closure);
  }

  #commitOperationClosure(
    record: MainOperationRecord<TDiagnostic>,
  ): void {
    record.commitClosure();
  }

  #publishOperationClosure(
    record: MainOperationRecord<TDiagnostic>,
  ): void {
    record.publishClosure();
  }

  #reportCallbackError(error: unknown): void {
    const diagnostic = this.#options.createDiagnostic(
      "callback-error",
      error,
    );
    this.#reportDiagnostic({
      kind: "callback-error",
      diagnostic,
      error,
    });
  }

  #reportOperationQuiescence(
    record: MainOperationRecord<TDiagnostic>,
  ): void {
    record.reportQuiescence();
  }

  #releaseOperationPayload(
    record: MainOperationRecord<TDiagnostic>,
  ): void {
    record.release();
  }

  #retireOperationIfComplete(
    epoch: MainEpoch<TDiagnostic>,
    record: MainOperationRecord<TDiagnostic>,
  ): void {
    if (record.phase !== "physically-closed") return;
    if (record.cancelSent && !record.cancelAcknowledged) return;
    epoch.operations.delete(record.reference.operationId);
  }

  #closeDrainedRealmIfReleased(
    epoch: MainEpoch<TDiagnostic>,
  ): void {
    if (epoch.phase !== "draining") return;
    for (const record of epoch.operations.values()) {
      if (record.phase !== "physically-closed"
        || (record.cancelSent && !record.cancelAcknowledged)) {
        return;
      }
    }
    if (epoch.epochWork.size > 0) return;
    this.#hardTerminate(epoch);
  }

  #hardTerminate(
    epoch: MainEpoch<TDiagnostic>,
  ): void {
    if (epoch.phase !== "closed") {
      epoch.phase = "closed";
      epoch.detach?.();
      epoch.detach = null;
      try {
        epoch.source.terminate();
      } catch (error: unknown) {
        const diagnostic = this.#options.createDiagnostic(
          "callback-error",
          error,
        );
        this.#reportDiagnostic({
          kind: "callback-error",
          diagnostic,
          error,
        });
      }
      epoch.held.length = 0;
      epoch.commands.clear();
      epoch.probe = null;
      epoch.deferredControlProbe = false;
      epoch.epochWork.clear();
    }
    this.#finalizeHardTerminationIfReady(epoch);
  }

  #releaseProducerCallout(epoch: MainEpoch<TDiagnostic>): void {
    if (epoch.producerCallouts <= 0)
      throw new Error("Producer callout lifetime was released more than once.");
    epoch.producerCallouts--;
    this.#finalizeHardTerminationIfReady(epoch);
  }

  #finalizeHardTerminationIfReady(epoch: MainEpoch<TDiagnostic>): void {
    if (epoch.phase !== "closed"
      || epoch.producerCallouts !== 0
      || epoch.closurePublicationActive
      || epoch.terminationFinalizing
      || epoch.terminationFinalized) {
      return;
    }
    epoch.terminationFinalizing = true;
    try {
      for (const record of epoch.operations.values()) {
        if (epoch.closure !== null)
          this.#reportOperationClosure(record, epoch.closure);
        this.#reportOperationQuiescence(record);
        this.#releaseOperationPayload(record);
      }
      epoch.operations.clear();
    } finally {
      epoch.terminationFinalizing = false;
    }
    epoch.terminationFinalized = true;
    this.#reportRealmReleasedIfReady(epoch);
  }

  #releasePreparedBinding(epoch: MainEpoch<TDiagnostic>): void {
    if (epoch.preparedBindings <= 0)
      throw new Error("Prepared binding lifetime was released more than once.");
    epoch.preparedBindings--;
    this.#reportRealmReleasedIfReady(epoch);
  }

  #reportRealmReleasedIfReady(epoch: MainEpoch<TDiagnostic>): void {
    if (epoch.phase !== "closed"
      || !epoch.terminationFinalized
      || epoch.producerCallouts !== 0
      || epoch.preparedBindings !== 0
      || epoch.realmReleased) {
      return;
    }
    epoch.realmReleased = true;
    try {
      this.#options.callbacks.realmReleased(epoch.token);
    } catch (error: unknown) {
      const diagnostic = this.#options.createDiagnostic(
        "callback-error",
        error,
      );
      this.#reportDiagnostic({
        kind: "callback-error",
        diagnostic,
        error,
      });
    }
  }

  #reportFailure(failure: WorkerRuntimeFailure<TDiagnostic>): void {
    try {
      this.#options.callbacks.failure(failure);
    } catch (error: unknown) {
      const diagnostic = this.#options.createDiagnostic(
        "callback-error",
        error,
      );
      this.#reportDiagnostic({
        kind: "callback-error",
        diagnostic,
        error,
      });
    }
  }

  #reportDiagnostic(diagnostic: WorkerRuntimeDiagnostic<TDiagnostic>): void {
    try {
      this.#options.callbacks.diagnostic(diagnostic);
    } catch {
      // The injected diagnostic callback is the last-resort reporting path.
    }
  }
}

interface FakeWorkerBootstrapAdapter<TBootstrap> {
  readonly decoder: BoundedPayloadDecoder<TBootstrap>;
  readonly bootstrap: (
    bootstrap: TBootstrap,
  ) => void | Promise<void>;
}

export interface FakeWorkerOperationContext {
  readonly operation: WorkerWireOperationReference;
  readonly cache: FakeWorkerEpochCache;
  startEpochWork(
    producerClass: string,
    sequence: number,
    advertisedAllowance?: WorkerLivenessAllowance,
  ): boolean;
  finishEpochWork(sequence: number): boolean;
}

export interface FakeWorkerEpochCache {
  readonly size: number;
  get(key: string): unknown;
  set(key: string, value: unknown): boolean;
  has(key: string): boolean;
  delete(key: string): boolean;
}

class RevocableFakeWorkerEpochCache implements FakeWorkerEpochCache {
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

export interface FakeWorkerOperationRegistration<
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
    context: FakeWorkerOperationContext,
  ) => ManagedOperationSettlement<TValue, TError, TOperationDiagnostic>
    | Promise<
      ManagedOperationSettlement<TValue, TError, TOperationDiagnostic>
    >;
  readonly cancel?: (
    operation: WorkerWireOperationReference,
    reason: OperationCancelReason,
  ) => boolean | Promise<boolean>;
}

type FakeWorkerCancel = (
  operation: WorkerWireOperationReference,
  reason: OperationCancelReason,
) => boolean | Promise<boolean>;

interface FakeWorkerOperationDispatchHandlers {
  readonly accepted: (
    allowance: WorkerLivenessAllowance,
    cancel: FakeWorkerCancel | null,
  ) => boolean;
  readonly rejected: (error: unknown, diagnostic: unknown) => void;
  readonly settled: (
    settlement: ManagedOperationSettlement<unknown, unknown, unknown>,
  ) => void;
  readonly failed: (error: unknown) => void;
}

interface ErasedFakeWorkerOperationRegistration {
  readonly kind: string;
  readonly dispatch: (
    envelope: Extract<
      RawMainToWorkerEnvelope,
      { readonly kind: "start" }
    >,
    context: FakeWorkerOperationContext,
    handlers: FakeWorkerOperationDispatchHandlers,
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

export class FakeWorkerOperationCatalog {
  readonly #registrations =
    new Map<string, ErasedFakeWorkerOperationRegistration>();

  register<TInput, TValue, TError, TOperationDiagnostic>(
    registration: FakeWorkerOperationRegistration<
      TInput,
      TValue,
      TError,
      TOperationDiagnostic
    >,
  ): void {
    if (this.#registrations.has(registration.kind))
      throw new Error(`Fake operation ${registration.kind} is duplicated.`);
    const erased: ErasedFakeWorkerOperationRegistration = {
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
    context: FakeWorkerOperationContext,
    handlers: FakeWorkerOperationDispatchHandlers,
  ): boolean {
    const registration = this.#registrations.get(envelope.operationKind);
    if (registration === undefined) return false;
    registration.dispatch(envelope, context, handlers);
    return true;
  }
}

export interface FakeWorkerRuntimeOptions<TBootstrap, TDiagnostic> {
  readonly scheduler: WorkerRuntimeTaskScheduler;
  readonly bootstrap: FakeWorkerBootstrapAdapter<TBootstrap>;
  readonly diagnostic: (detail: unknown) => TDiagnostic;
  readonly unknownOperationRejection: (kind: string) => {
    readonly error: unknown;
    readonly diagnostic: unknown;
  };
  readonly operations: FakeWorkerOperationCatalog;
  readonly producerClasses: WorkerProducerClassRegistry;
  readonly omitResponse?: (
    kind: "accepted" | "rejected" | "cancel-acknowledged" | "probe-acknowledged",
    correlation: string,
  ) => boolean;
}

interface FakeActiveOperation {
  readonly operation: WorkerWireOperationReference;
  readonly cancel: FakeWorkerCancel | null;
  settling: boolean;
}

export class FakeWorkerRuntime<TBootstrap, TDiagnostic>
implements WorkerRuntimeTransportBinding, WorkerRuntimeSource {
  readonly source: WorkerRuntimeSource = this;
  readonly #cache = new RevocableFakeWorkerEpochCache();
  readonly receivedMessages: unknown[] = [];
  readonly emittedMessages: RawWorkerToMainEnvelope[] = [];
  readonly #options: FakeWorkerRuntimeOptions<TBootstrap, TDiagnostic>;
  readonly #active = new Map<string, FakeActiveOperation>();
  readonly #epochWork = new Map<number, WorkerLivenessAllowance>();
  #handlers: WorkerRuntimeTransportHandlers | null = null;
  #epochToken: number | null = null;
  #idleHeartbeatIntervalMilliseconds: number | null = null;
  #operationHighWater = 0;
  #workHighWater = 0;
  #lane: Promise<void> = Promise.resolve();
  #initialized = false;
  #ready = false;
  #failed = false;
  #terminated = false;
  #terminateCount = 0;

  constructor(
    options: FakeWorkerRuntimeOptions<TBootstrap, TDiagnostic>,
  ) {
    this.#options = options;
  }

  get terminateCount(): number {
    return this.#terminateCount;
  }

  get cache(): FakeWorkerEpochCache {
    return this.#cache;
  }

  get activeOperationCount(): number {
    return this.#active.size;
  }

  get activeEpochWorkCount(): number {
    return this.#epochWork.size;
  }

  get terminated(): boolean {
    return this.#terminated;
  }

  bind(handlers: WorkerRuntimeTransportHandlers): () => void {
    this.#handlers = handlers;
    return () => {
      if (this.#handlers === handlers) this.#handlers = null;
    };
  }

  send(message: unknown): void {
    if (this.#terminated) throw new Error("Fake worker is terminated.");
    this.receivedMessages.push(message);
    if (!this.#initialized) {
      this.#initialized = true;
      this.#options.scheduler.enqueue(() => {
        this.#initialize(message);
      });
      return;
    }
    this.#lane = this.#lane.then(() => this.#processCommand(message));
    this.#lane.catch((error: unknown) => {
      this.#declareFailure(error);
    });
  }

  terminate(): void {
    if (this.#terminated) return;
    this.#terminated = true;
    this.#terminateCount++;
    this.#handlers = null;
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

  emitRaw(data: unknown, source: WorkerRuntimeSource = this): void {
    this.#handlers?.message(source, data);
  }

  emitError(diagnostic: unknown, source: WorkerRuntimeSource = this): void {
    this.#handlers?.error(source, diagnostic);
  }

  emitMessageError(
    diagnostic: unknown,
    source: WorkerRuntimeSource = this,
  ): void {
    this.#handlers?.messageError(source, diagnostic);
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
      || this.#failed
      || this.#epochToken === null) return false;
    if (!this.#epochWork.delete(sequence)) {
      this.#declareFailure({
        kind: "invalid-epoch-work-finish",
        sequence,
      });
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
      return;
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
    const context: FakeWorkerOperationContext = {
      operation: envelope.operation,
      cache: this.cache,
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
          this.#active.set(envelope.operation.operationId, {
            operation: envelope.operation,
            cancel,
            settling: false,
          });
          if (!this.#omit("accepted", operationKey(envelope.operation))) {
            this.#emit({
              protocolVersion: WORKER_RUNTIME_PROTOCOL_VERSION,
              epochToken: this.#requiredEpochToken(),
              kind: "accepted",
              operation: envelope.operation,
              allowance,
            });
          }
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
    if (!this.#omit("cancel-acknowledged", operationKey(envelope.operation))) {
      this.#emit({
        protocolVersion: WORKER_RUNTIME_PROTOCOL_VERSION,
        epochToken: this.#requiredEpochToken(),
        kind: "cancel-acknowledged",
        operation: envelope.operation,
        status: running ? "running" : "not-active",
      });
    }
  }

  #processProbe(sequence: number): void {
    if (this.#omit("probe-acknowledged", String(sequence))) return;
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
    if (this.#omit("rejected", operationKey(envelope.operation))) return;
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
    if (this.#terminated || this.#failed) return;
    const active = this.#active.get(operation.operationId);
    if (active === undefined
      || active.operation.operationSequence !== operation.operationSequence) {
      this.#declareFailure({
        kind: "settlement-without-active-operation",
        operation,
      });
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
      throw new Error("Fake worker has no epoch token.");
    return this.#epochToken;
  }

  #omit(
    kind: "accepted" | "rejected" | "cancel-acknowledged" | "probe-acknowledged",
    correlation: string,
  ): boolean {
    return this.#options.omitResponse?.(kind, correlation) ?? false;
  }

  #emit(data: RawWorkerToMainEnvelope): void {
    if (this.#terminated) return;
    this.emittedMessages.push(data);
    this.#handlers?.message(this, data);
  }
}

export class QueueWorkerRuntimeTransportFactory
implements WorkerRuntimeTransportFactory {
  readonly #queue: WorkerRuntimeTransportBinding[];

  constructor(bindings: readonly WorkerRuntimeTransportBinding[]) {
    this.#queue = [...bindings];
  }

  create(): WorkerRuntimeTransportBinding {
    const binding = this.#queue.shift();
    if (binding === undefined)
      throw new Error("No queued worker runtime transport remains.");
    return binding;
  }
}

export class ManualWorkerRuntimeEnvironment
implements
  WorkerRuntimeActiveClock,
  WorkerRuntimeLifecycleSignals,
  WorkerRuntimeTaskScheduler {
  readonly #clockListeners = new Set<() => void>();
  readonly #lifecycleListeners = new Set<WorkerRuntimeLifecycleListeners>();
  readonly #tasks: (() => void)[] = [];
  #now = 0;
  #suspended = false;

  now(): number {
    return this.#now;
  }

  subscribe(listener: (() => void) | WorkerRuntimeLifecycleListeners): () => void {
    if (typeof listener === "function") {
      this.#clockListeners.add(listener);
      return () => {
        this.#clockListeners.delete(listener);
      };
    }
    this.#lifecycleListeners.add(listener);
    return () => {
      this.#lifecycleListeners.delete(listener);
    };
  }

  enqueue(task: () => void): void {
    this.#tasks.push(task);
  }

  flushTasks(): void {
    while (this.#tasks.length > 0) {
      const task = this.#tasks.shift();
      task?.();
    }
  }

  async flushAsync(): Promise<void> {
    this.flushTasks();
    await Promise.resolve();
    await Promise.resolve();
    await Promise.resolve();
    await Promise.resolve();
    this.flushTasks();
  }

  advanceActive(milliseconds: number): void {
    validateNonNegativeSafeInteger(milliseconds, "milliseconds");
    if (!this.#suspended) this.#now += milliseconds;
    for (const listener of this.#clockListeners) listener();
  }

  suspend(): void {
    if (this.#suspended) return;
    this.#suspended = true;
    for (const listener of this.#lifecycleListeners) listener.suspended();
  }

  resume(): void {
    if (!this.#suspended) return;
    this.#suspended = false;
    for (const listener of this.#lifecycleListeners) listener.resumed();
  }

  recoverMainLoop(gapActiveMilliseconds: number): void {
    validateNonNegativeSafeInteger(
      gapActiveMilliseconds,
      "gapActiveMilliseconds",
    );
    this.#now += gapActiveMilliseconds;
    for (const listener of this.#lifecycleListeners)
      listener.mainLoopRecovered(gapActiveMilliseconds);
    for (const listener of this.#clockListeners) listener();
  }
}
