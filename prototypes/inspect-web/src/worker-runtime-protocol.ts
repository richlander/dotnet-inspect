import type { OperationCancelReason } from "./operation-authority.ts";

export const WORKER_RUNTIME_PROTOCOL_VERSION = 1;

export type WorkerWireEpochToken = number;

export interface WorkerWireOperationReference {
  readonly operationId: string;
  readonly operationSequence: number;
}

export type WorkerOperationCancelReason = OperationCancelReason;

export type WorkerLivenessAllowance =
  | {
      readonly kind: "bounded";
      readonly maxSilentActiveMilliseconds: number;
    }
  | { readonly kind: "unbounded" };

export type ManagedOperationSettlement<
  TValue,
  TError,
  TDiagnostic,
> =
  | {
      readonly kind: "succeeded";
      readonly value: TValue;
    }
  | {
      readonly kind: "failed";
      readonly failureKind: "expected" | "unexpected";
      readonly error: TError;
      readonly diagnostic: TDiagnostic;
    }
  | {
      readonly kind: "canceled";
      readonly reason: WorkerOperationCancelReason;
    };

type RawManagedOperationSettlement =
  ManagedOperationSettlement<unknown, unknown, unknown>;

interface WorkerWireEnvelopeHeader {
  readonly protocolVersion: typeof WORKER_RUNTIME_PROTOCOL_VERSION;
  readonly epochToken: WorkerWireEpochToken;
}

export interface RawInitializeMainToWorkerEnvelope
  extends WorkerWireEnvelopeHeader {
  readonly kind: "initialize";
  readonly bootstrap: unknown;
  readonly idleHeartbeatIntervalMilliseconds: number;
  readonly idleAllowanceMilliseconds: number;
}

export interface RawStartMainToWorkerEnvelope
  extends WorkerWireEnvelopeHeader {
  readonly kind: "start";
  readonly operation: WorkerWireOperationReference;
  readonly operationKind: string;
  readonly payload: unknown;
}

interface CancelMainToWorkerEnvelope
  extends WorkerWireEnvelopeHeader {
  readonly kind: "cancel";
  readonly operation: WorkerWireOperationReference;
  readonly reason: WorkerOperationCancelReason;
}

interface ProbeMainToWorkerEnvelope
  extends WorkerWireEnvelopeHeader {
  readonly kind: "probe";
  readonly probeSequence: number;
}

export type RawMainToWorkerEnvelope =
  | RawInitializeMainToWorkerEnvelope
  | RawStartMainToWorkerEnvelope
  | CancelMainToWorkerEnvelope
  | ProbeMainToWorkerEnvelope;

export interface InitializeMainToWorkerEnvelope<TBootstrap>
  extends WorkerWireEnvelopeHeader {
  readonly kind: "initialize";
  readonly bootstrap: TBootstrap;
  readonly idleHeartbeatIntervalMilliseconds: number;
  readonly idleAllowanceMilliseconds: number;
}

export interface StartMainToWorkerEnvelope<TPayload>
  extends WorkerWireEnvelopeHeader {
  readonly kind: "start";
  readonly operation: WorkerWireOperationReference;
  readonly operationKind: string;
  readonly payload: TPayload;
}

export type MainToWorkerEnvelope<TBootstrap, TPayload> =
  | InitializeMainToWorkerEnvelope<TBootstrap>
  | StartMainToWorkerEnvelope<TPayload>
  | CancelMainToWorkerEnvelope
  | ProbeMainToWorkerEnvelope;

interface ReadyWorkerToMainEnvelope
  extends WorkerWireEnvelopeHeader {
  readonly kind: "ready";
  readonly idleHeartbeatIntervalMilliseconds: number;
}

export interface RawStartupFailedWorkerToMainEnvelope
  extends WorkerWireEnvelopeHeader {
  readonly kind: "startup-failed";
  readonly diagnostic: unknown;
}

interface AcceptedWorkerToMainEnvelope
  extends WorkerWireEnvelopeHeader {
  readonly kind: "accepted";
  readonly operation: WorkerWireOperationReference;
  readonly allowance: WorkerLivenessAllowance;
}

export interface RawRejectedWorkerToMainEnvelope
  extends WorkerWireEnvelopeHeader {
  readonly kind: "rejected";
  readonly operation: WorkerWireOperationReference;
  readonly error: unknown;
  readonly diagnostic: unknown;
}

interface CancelAcknowledgedWorkerToMainEnvelope
  extends WorkerWireEnvelopeHeader {
  readonly kind: "cancel-acknowledged";
  readonly operation: WorkerWireOperationReference;
  readonly status: "running" | "not-active";
}

export interface RawProgressWorkerToMainEnvelope
  extends WorkerWireEnvelopeHeader {
  readonly kind: "progress";
  readonly operation: WorkerWireOperationReference;
  readonly payload: unknown;
}

export interface RawSettledWorkerToMainEnvelope
  extends WorkerWireEnvelopeHeader {
  readonly kind: "settled";
  readonly operation: WorkerWireOperationReference;
  readonly settlement: RawManagedOperationSettlement;
}

interface HeartbeatWorkerToMainEnvelope
  extends WorkerWireEnvelopeHeader {
  readonly kind: "heartbeat";
}

interface ProbeAcknowledgedWorkerToMainEnvelope
  extends WorkerWireEnvelopeHeader {
  readonly kind: "probe-acknowledged";
  readonly probeSequence: number;
}

interface EpochWorkStartedWorkerToMainEnvelope
  extends WorkerWireEnvelopeHeader {
  readonly kind: "epoch-work-started";
  readonly workSequence: number;
  readonly allowance: WorkerLivenessAllowance;
}

interface EpochWorkFinishedWorkerToMainEnvelope
  extends WorkerWireEnvelopeHeader {
  readonly kind: "epoch-work-finished";
  readonly workSequence: number;
}

export interface RawEpochFailedWorkerToMainEnvelope
  extends WorkerWireEnvelopeHeader {
  readonly kind: "epoch-failed";
  readonly diagnostic: unknown;
}

export type RawWorkerToMainEnvelope =
  | ReadyWorkerToMainEnvelope
  | RawStartupFailedWorkerToMainEnvelope
  | AcceptedWorkerToMainEnvelope
  | RawRejectedWorkerToMainEnvelope
  | CancelAcknowledgedWorkerToMainEnvelope
  | RawProgressWorkerToMainEnvelope
  | RawSettledWorkerToMainEnvelope
  | HeartbeatWorkerToMainEnvelope
  | ProbeAcknowledgedWorkerToMainEnvelope
  | EpochWorkStartedWorkerToMainEnvelope
  | EpochWorkFinishedWorkerToMainEnvelope
  | RawEpochFailedWorkerToMainEnvelope;

export interface StartupFailedWorkerToMainEnvelope<TDiagnostic>
  extends WorkerWireEnvelopeHeader {
  readonly kind: "startup-failed";
  readonly diagnostic: TDiagnostic;
}

export interface RejectedWorkerToMainEnvelope<TError, TDiagnostic>
  extends WorkerWireEnvelopeHeader {
  readonly kind: "rejected";
  readonly operation: WorkerWireOperationReference;
  readonly error: TError;
  readonly diagnostic: TDiagnostic;
}

export interface ProgressWorkerToMainEnvelope<TProgress>
  extends WorkerWireEnvelopeHeader {
  readonly kind: "progress";
  readonly operation: WorkerWireOperationReference;
  readonly payload: TProgress;
}

export interface SettledWorkerToMainEnvelope<TValue, TError, TDiagnostic>
  extends WorkerWireEnvelopeHeader {
  readonly kind: "settled";
  readonly operation: WorkerWireOperationReference;
  readonly settlement: ManagedOperationSettlement<
    TValue,
    TError,
    TDiagnostic
  >;
}

export interface EpochFailedWorkerToMainEnvelope<TDiagnostic>
  extends WorkerWireEnvelopeHeader {
  readonly kind: "epoch-failed";
  readonly diagnostic: TDiagnostic;
}

export type WorkerToMainEnvelope<
  TValue,
  TError,
  TDiagnostic,
  TProgress,
> =
  | ReadyWorkerToMainEnvelope
  | StartupFailedWorkerToMainEnvelope<TDiagnostic>
  | AcceptedWorkerToMainEnvelope
  | RejectedWorkerToMainEnvelope<TError, TDiagnostic>
  | CancelAcknowledgedWorkerToMainEnvelope
  | ProgressWorkerToMainEnvelope<TProgress>
  | SettledWorkerToMainEnvelope<TValue, TError, TDiagnostic>
  | HeartbeatWorkerToMainEnvelope
  | ProbeAcknowledgedWorkerToMainEnvelope
  | EpochWorkStartedWorkerToMainEnvelope
  | EpochWorkFinishedWorkerToMainEnvelope
  | EpochFailedWorkerToMainEnvelope<TDiagnostic>;

export type BoundedPayloadDecodeResult<T> =
  | {
      readonly kind: "decoded";
      readonly value: T;
    }
  | {
      readonly kind: "rejected";
      readonly reason: "invalid" | "oversized";
      readonly message: string;
      readonly cause?: unknown;
    };

// The owner decoder applies semantic validation and its explicit payload
// budget before returning "decoded".
export interface BoundedPayloadDecoder<T> {
  readonly decode: (value: unknown) => BoundedPayloadDecodeResult<T>;
}

export interface WorkerSettlementPayloadDecoders<
  TValue,
  TError,
  TDiagnostic,
> {
  readonly value: BoundedPayloadDecoder<TValue>;
  readonly error: BoundedPayloadDecoder<TError>;
  readonly diagnostic: BoundedPayloadDecoder<TDiagnostic>;
}

export type WorkerEnvelopeDecodeFailureCategory =
  | "shape"
  | "property"
  | "value"
  | "protocol-version"
  | "epoch"
  | "payload-budget"
  | "payload-codec";

export type WorkerEnvelopeDecodeFailureCode =
  | "not-record"
  | "missing-property"
  | "unexpected-property"
  | "accessor-property"
  | "invalid-discriminator"
  | "invalid-integer"
  | "invalid-string"
  | "invalid-literal"
  | "wrong-version"
  | "wrong-epoch"
  | "payload-oversized"
  | "payload-rejected";

export interface WorkerEnvelopeDecodeFailure {
  readonly category: WorkerEnvelopeDecodeFailureCategory;
  readonly code: WorkerEnvelopeDecodeFailureCode;
  readonly path: string;
  readonly message: string;
  readonly cause: unknown;
}

export type WorkerEnvelopeDecodeResult<T> =
  | {
      readonly kind: "success";
      readonly value: T;
    }
  | {
      readonly kind: "failure";
      readonly failure: WorkerEnvelopeDecodeFailure;
    };

type DecodeResult<T> = WorkerEnvelopeDecodeResult<T>;
type ClosedRecord = ReadonlyMap<string, unknown>;
type EqualTypes<TLeft, TRight> =
  [TLeft] extends [TRight]
    ? [TRight] extends [TLeft]
      ? true
      : false
    : false;

const cancellationReasonValues = [
  "user",
  "superseded",
  "disposed",
  "feature-observer-failed",
  "timeout",
  "worker-restarted",
] as const satisfies readonly OperationCancelReason[];

const cancellationReasonCatalogMatchesAuthority:
  EqualTypes<
    (typeof cancellationReasonValues)[number],
    OperationCancelReason
  > = true;
void cancellationReasonCatalogMatchesAuthority;

const cancellationReasons: ReadonlySet<string> =
  new Set(cancellationReasonValues);

function success<T>(value: T): DecodeResult<T> {
  return { kind: "success", value };
}

function failure(
  category: WorkerEnvelopeDecodeFailureCategory,
  code: WorkerEnvelopeDecodeFailureCode,
  path: string,
  message: string,
  cause?: unknown,
): DecodeResult<never> {
  return {
    kind: "failure",
    failure: { category, code, path, message, cause },
  };
}

function propertyPath(path: string, property: string): string {
  return /^[A-Za-z_$][\w$]*$/.test(property)
    ? `${path}.${property}`
    : `${path}[${JSON.stringify(property)}]`;
}

function decodeRecordShape(
  value: unknown,
  path: string,
): DecodeResult<object> {
  if (typeof value !== "object" || value === null || Array.isArray(value)) {
    return failure(
      "shape",
      "not-record",
      path,
      "Expected a non-null, non-array object.",
    );
  }
  return success(value);
}

function readOwnDataProperty(
  value: object,
  property: string,
  path: string,
): DecodeResult<unknown> {
  const descriptor = Object.getOwnPropertyDescriptor(value, property);
  const propertyLocation = propertyPath(path, property);
  if (descriptor === undefined) {
    return failure(
      "property",
      "missing-property",
      propertyLocation,
      `Missing own property ${property}.`,
    );
  }
  if (!("value" in descriptor)) {
    return failure(
      "property",
      "accessor-property",
      propertyLocation,
      `Property ${property} must be an own data property.`,
    );
  }
  const propertyValue: unknown = descriptor.value;
  return success(propertyValue);
}

function decodeClosedRecord(
  value: unknown,
  expectedProperties: readonly string[],
  path: string,
): DecodeResult<ClosedRecord> {
  const shape = decodeRecordShape(value, path);
  if (shape.kind === "failure") return shape;

  const ownKeys = Reflect.ownKeys(shape.value);
  for (const key of ownKeys) {
    if (typeof key === "symbol") {
      return failure(
        "property",
        "unexpected-property",
        `${path}[symbol]`,
        "Unexpected own symbol property.",
      );
    }
    if (!expectedProperties.includes(key)) {
      return failure(
        "property",
        "unexpected-property",
        propertyPath(path, key),
        `Unexpected own property ${key}.`,
      );
    }
  }

  const properties = new Map<string, unknown>();
  for (const property of expectedProperties) {
    const decoded = readOwnDataProperty(shape.value, property, path);
    if (decoded.kind === "failure") return decoded;
    properties.set(property, decoded.value);
  }
  return success(properties);
}

function decodeDiscriminator(
  value: unknown,
  path: string,
): DecodeResult<string> {
  const shape = decodeRecordShape(value, path);
  if (shape.kind === "failure") return shape;
  const kind = readOwnDataProperty(shape.value, "kind", path);
  if (kind.kind === "failure") return kind;
  if (typeof kind.value !== "string") {
    return failure(
      "value",
      "invalid-discriminator",
      propertyPath(path, "kind"),
      "Expected a string discriminator.",
    );
  }
  return success(kind.value);
}

function decodePositiveSafeInteger(
  value: unknown,
  path: string,
): DecodeResult<number> {
  if (typeof value !== "number"
    || !Number.isSafeInteger(value)
    || value <= 0) {
    return failure(
      "value",
      "invalid-integer",
      path,
      "Expected a finite positive safe integer.",
    );
  }
  return success(value);
}

function decodeString(
  value: unknown,
  path: string,
): DecodeResult<string> {
  if (typeof value !== "string") {
    return failure(
      "value",
      "invalid-string",
      path,
      "Expected a string.",
    );
  }
  return success(value);
}

function decodeLiteral<T extends string>(
  value: unknown,
  isAllowed: (candidate: string) => candidate is T,
  path: string,
  description: string,
): DecodeResult<T> {
  if (typeof value !== "string" || !isAllowed(value)) {
    return failure(
      "value",
      "invalid-literal",
      path,
      `Expected ${description}.`,
    );
  }
  return success(value);
}

function isCancellationReason(
  value: string,
): value is WorkerOperationCancelReason {
  return cancellationReasons.has(value);
}

function isManagedFailureKind(
  value: string,
): value is "expected" | "unexpected" {
  return value === "expected" || value === "unexpected";
}

function isCancellationStatus(
  value: string,
): value is "running" | "not-active" {
  return value === "running" || value === "not-active";
}

function decodeHeader(
  record: ClosedRecord,
  path: string,
): DecodeResult<WorkerWireEnvelopeHeader> {
  const protocolVersion = record.get("protocolVersion");
  if (protocolVersion !== WORKER_RUNTIME_PROTOCOL_VERSION) {
    return failure(
      "protocol-version",
      "wrong-version",
      propertyPath(path, "protocolVersion"),
      `Expected protocol version ${WORKER_RUNTIME_PROTOCOL_VERSION}.`,
    );
  }

  const epochToken = decodePositiveSafeInteger(
    record.get("epochToken"),
    propertyPath(path, "epochToken"),
  );
  if (epochToken.kind === "failure") return epochToken;
  return success({
    protocolVersion: WORKER_RUNTIME_PROTOCOL_VERSION,
    epochToken: epochToken.value,
  });
}

function requireExpectedEpoch(
  header: WorkerWireEnvelopeHeader,
  expectedEpochToken: WorkerWireEpochToken,
  path: string,
): DecodeResult<WorkerWireEnvelopeHeader> {
  if (header.epochToken !== expectedEpochToken) {
    return failure(
      "epoch",
      "wrong-epoch",
      propertyPath(path, "epochToken"),
      `Expected epoch token ${expectedEpochToken}.`,
    );
  }
  return success(header);
}

function decodeOperationReference(
  value: unknown,
  path: string,
): DecodeResult<WorkerWireOperationReference> {
  const record = decodeClosedRecord(
    value,
    ["operationId", "operationSequence"],
    path,
  );
  if (record.kind === "failure") return record;
  const operationId = decodeString(
    record.value.get("operationId"),
    propertyPath(path, "operationId"),
  );
  if (operationId.kind === "failure") return operationId;
  const operationSequence = decodePositiveSafeInteger(
    record.value.get("operationSequence"),
    propertyPath(path, "operationSequence"),
  );
  if (operationSequence.kind === "failure") return operationSequence;
  return success({
    operationId: operationId.value,
    operationSequence: operationSequence.value,
  });
}

function decodeCancellationReason(
  value: unknown,
  path: string,
): DecodeResult<WorkerOperationCancelReason> {
  return decodeLiteral<WorkerOperationCancelReason>(
    value,
    isCancellationReason,
    path,
    "a known operation cancellation reason",
  );
}

function decodeAllowance(
  value: unknown,
  path: string,
): DecodeResult<WorkerLivenessAllowance> {
  const discriminator = decodeDiscriminator(value, path);
  if (discriminator.kind === "failure") return discriminator;
  if (discriminator.value === "unbounded") {
    const record = decodeClosedRecord(value, ["kind"], path);
    if (record.kind === "failure") return record;
    return success({ kind: "unbounded" });
  }
  if (discriminator.value === "bounded") {
    const record = decodeClosedRecord(
      value,
      ["kind", "maxSilentActiveMilliseconds"],
      path,
    );
    if (record.kind === "failure") return record;
    const milliseconds = decodePositiveSafeInteger(
      record.value.get("maxSilentActiveMilliseconds"),
      propertyPath(path, "maxSilentActiveMilliseconds"),
    );
    if (milliseconds.kind === "failure") return milliseconds;
    return success({
      kind: "bounded",
      maxSilentActiveMilliseconds: milliseconds.value,
    });
  }
  return failure(
    "value",
    "invalid-discriminator",
    propertyPath(path, "kind"),
    "Expected bounded or unbounded liveness allowance.",
  );
}

function decodeRawSettlement(
  value: unknown,
  path: string,
): DecodeResult<RawManagedOperationSettlement> {
  const discriminator = decodeDiscriminator(value, path);
  if (discriminator.kind === "failure") return discriminator;

  if (discriminator.value === "succeeded") {
    const record = decodeClosedRecord(value, ["kind", "value"], path);
    if (record.kind === "failure") return record;
    return success({
      kind: "succeeded",
      value: record.value.get("value"),
    });
  }

  if (discriminator.value === "failed") {
    const record = decodeClosedRecord(
      value,
      ["kind", "failureKind", "error", "diagnostic"],
      path,
    );
    if (record.kind === "failure") return record;
    const failureKind = decodeLiteral<"expected" | "unexpected">(
      record.value.get("failureKind"),
      isManagedFailureKind,
      propertyPath(path, "failureKind"),
      "expected or unexpected managed failure kind",
    );
    if (failureKind.kind === "failure") return failureKind;
    return success({
      kind: "failed",
      failureKind: failureKind.value,
      error: record.value.get("error"),
      diagnostic: record.value.get("diagnostic"),
    });
  }

  if (discriminator.value === "canceled") {
    const record = decodeClosedRecord(value, ["kind", "reason"], path);
    if (record.kind === "failure") return record;
    const reason = decodeCancellationReason(
      record.value.get("reason"),
      propertyPath(path, "reason"),
    );
    if (reason.kind === "failure") return reason;
    return success({ kind: "canceled", reason: reason.value });
  }

  return failure(
    "value",
    "invalid-discriminator",
    propertyPath(path, "kind"),
    "Expected succeeded, failed, or canceled settlement.",
  );
}

function decodePayload<T>(
  value: unknown,
  decoder: BoundedPayloadDecoder<T>,
  path: string,
): DecodeResult<T> {
  const decoded = decoder.decode(value);
  if (decoded.kind === "decoded") return success(decoded.value);
  if (decoded.reason === "oversized") {
    return failure(
      "payload-budget",
      "payload-oversized",
      path,
      decoded.message,
      decoded.cause,
    );
  }
  return failure(
    "payload-codec",
    "payload-rejected",
    path,
    decoded.message,
    decoded.cause,
  );
}

function decodeInitializeStructure(
  value: unknown,
): DecodeResult<RawInitializeMainToWorkerEnvelope> {
  const path = "$";
  const discriminator = decodeDiscriminator(value, path);
  if (discriminator.kind === "failure") return discriminator;
  if (discriminator.value !== "initialize") {
    return failure(
      "value",
      "invalid-discriminator",
      "$.kind",
      "Expected an initialize envelope.",
    );
  }

  const record = decodeClosedRecord(
    value,
    [
      "protocolVersion",
      "epochToken",
      "kind",
      "bootstrap",
      "idleHeartbeatIntervalMilliseconds",
      "idleAllowanceMilliseconds",
    ],
    path,
  );
  if (record.kind === "failure") return record;
  const header = decodeHeader(record.value, path);
  if (header.kind === "failure") return header;
  const idleHeartbeatIntervalMilliseconds = decodePositiveSafeInteger(
    record.value.get("idleHeartbeatIntervalMilliseconds"),
    "$.idleHeartbeatIntervalMilliseconds",
  );
  if (idleHeartbeatIntervalMilliseconds.kind === "failure")
    return idleHeartbeatIntervalMilliseconds;
  const idleAllowanceMilliseconds = decodePositiveSafeInteger(
    record.value.get("idleAllowanceMilliseconds"),
    "$.idleAllowanceMilliseconds",
  );
  if (idleAllowanceMilliseconds.kind === "failure")
    return idleAllowanceMilliseconds;
  return success({
    ...header.value,
    kind: "initialize",
    bootstrap: record.value.get("bootstrap"),
    idleHeartbeatIntervalMilliseconds:
      idleHeartbeatIntervalMilliseconds.value,
    idleAllowanceMilliseconds: idleAllowanceMilliseconds.value,
  });
}

export function decodeInitializePayload<TBootstrap>(
  envelope: RawInitializeMainToWorkerEnvelope,
  decoder: BoundedPayloadDecoder<TBootstrap>,
): WorkerEnvelopeDecodeResult<
  InitializeMainToWorkerEnvelope<TBootstrap>
> {
  const bootstrap = decodePayload(
    envelope.bootstrap,
    decoder,
    "$.bootstrap",
  );
  if (bootstrap.kind === "failure") return bootstrap;
  return success({ ...envelope, bootstrap: bootstrap.value });
}

export function decodeStartPayload<TPayload>(
  envelope: RawStartMainToWorkerEnvelope,
  decoder: BoundedPayloadDecoder<TPayload>,
): WorkerEnvelopeDecodeResult<StartMainToWorkerEnvelope<TPayload>> {
  const payload = decodePayload(envelope.payload, decoder, "$.payload");
  if (payload.kind === "failure") return payload;
  return success({ ...envelope, payload: payload.value });
}

export function decodeStartupFailedPayload<TDiagnostic>(
  envelope: RawStartupFailedWorkerToMainEnvelope,
  decoder: BoundedPayloadDecoder<TDiagnostic>,
): WorkerEnvelopeDecodeResult<
  StartupFailedWorkerToMainEnvelope<TDiagnostic>
> {
  const diagnostic = decodePayload(
    envelope.diagnostic,
    decoder,
    "$.diagnostic",
  );
  if (diagnostic.kind === "failure") return diagnostic;
  return success({ ...envelope, diagnostic: diagnostic.value });
}

export function decodeRejectedPayload<TError, TDiagnostic>(
  envelope: RawRejectedWorkerToMainEnvelope,
  errorDecoder: BoundedPayloadDecoder<TError>,
  diagnosticDecoder: BoundedPayloadDecoder<TDiagnostic>,
): WorkerEnvelopeDecodeResult<
  RejectedWorkerToMainEnvelope<TError, TDiagnostic>
> {
  const error = decodePayload(envelope.error, errorDecoder, "$.error");
  if (error.kind === "failure") return error;
  const diagnostic = decodePayload(
    envelope.diagnostic,
    diagnosticDecoder,
    "$.diagnostic",
  );
  if (diagnostic.kind === "failure") return diagnostic;
  return success({
    ...envelope,
    error: error.value,
    diagnostic: diagnostic.value,
  });
}

export function decodeProgressPayload<TProgress>(
  envelope: RawProgressWorkerToMainEnvelope,
  decoder: BoundedPayloadDecoder<TProgress>,
): WorkerEnvelopeDecodeResult<ProgressWorkerToMainEnvelope<TProgress>> {
  const payload = decodePayload(envelope.payload, decoder, "$.payload");
  if (payload.kind === "failure") return payload;
  return success({ ...envelope, payload: payload.value });
}

export function decodeSettledPayload<TValue, TError, TDiagnostic>(
  envelope: RawSettledWorkerToMainEnvelope,
  decoders: WorkerSettlementPayloadDecoders<
    TValue,
    TError,
    TDiagnostic
  >,
): WorkerEnvelopeDecodeResult<
  SettledWorkerToMainEnvelope<TValue, TError, TDiagnostic>
> {
  if (envelope.settlement.kind === "succeeded") {
    const value = decodePayload(
      envelope.settlement.value,
      decoders.value,
      "$.settlement.value",
    );
    if (value.kind === "failure") return value;
    return success({
      ...envelope,
      settlement: { kind: "succeeded", value: value.value },
    });
  }

  if (envelope.settlement.kind === "failed") {
    const error = decodePayload(
      envelope.settlement.error,
      decoders.error,
      "$.settlement.error",
    );
    if (error.kind === "failure") return error;
    const diagnostic = decodePayload(
      envelope.settlement.diagnostic,
      decoders.diagnostic,
      "$.settlement.diagnostic",
    );
    if (diagnostic.kind === "failure") return diagnostic;
    return success({
      ...envelope,
      settlement: {
        kind: "failed",
        failureKind: envelope.settlement.failureKind,
        error: error.value,
        diagnostic: diagnostic.value,
      },
    });
  }

  return success({
    ...envelope,
    settlement: envelope.settlement,
  });
}

export function decodeEpochFailedPayload<TDiagnostic>(
  envelope: RawEpochFailedWorkerToMainEnvelope,
  decoder: BoundedPayloadDecoder<TDiagnostic>,
): WorkerEnvelopeDecodeResult<
  EpochFailedWorkerToMainEnvelope<TDiagnostic>
> {
  const diagnostic = decodePayload(
    envelope.diagnostic,
    decoder,
    "$.diagnostic",
  );
  if (diagnostic.kind === "failure") return diagnostic;
  return success({ ...envelope, diagnostic: diagnostic.value });
}

export function decodeUnboundInitializationEnvelope<TBootstrap>(
  value: unknown,
  bootstrapDecoder: BoundedPayloadDecoder<TBootstrap>,
): WorkerEnvelopeDecodeResult<
  InitializeMainToWorkerEnvelope<TBootstrap>
> {
  const envelope = decodeInitializeStructure(value);
  if (envelope.kind === "failure") return envelope;
  return decodeInitializePayload(envelope.value, bootstrapDecoder);
}

export function decodeBoundMainToWorkerEnvelope(
  value: unknown,
  expectedEpochToken: WorkerWireEpochToken,
): WorkerEnvelopeDecodeResult<RawMainToWorkerEnvelope> {
  const path = "$";
  const discriminator = decodeDiscriminator(value, path);
  if (discriminator.kind === "failure") return discriminator;

  if (discriminator.value === "initialize") {
    const envelope = decodeInitializeStructure(value);
    if (envelope.kind === "failure") return envelope;
    const header = requireExpectedEpoch(
      envelope.value,
      expectedEpochToken,
      path,
    );
    if (header.kind === "failure") return header;
    return envelope;
  }

  if (discriminator.value === "start") {
    const record = decodeClosedRecord(
      value,
      [
        "protocolVersion",
        "epochToken",
        "kind",
        "operation",
        "operationKind",
        "payload",
      ],
      path,
    );
    if (record.kind === "failure") return record;
    const header = decodeHeader(record.value, path);
    if (header.kind === "failure") return header;
    const expectedHeader = requireExpectedEpoch(
      header.value,
      expectedEpochToken,
      path,
    );
    if (expectedHeader.kind === "failure") return expectedHeader;
    const operation = decodeOperationReference(
      record.value.get("operation"),
      "$.operation",
    );
    if (operation.kind === "failure") return operation;
    const operationKind = decodeString(
      record.value.get("operationKind"),
      "$.operationKind",
    );
    if (operationKind.kind === "failure") return operationKind;
    return success({
      ...expectedHeader.value,
      kind: "start",
      operation: operation.value,
      operationKind: operationKind.value,
      payload: record.value.get("payload"),
    });
  }

  if (discriminator.value === "cancel") {
    const record = decodeClosedRecord(
      value,
      ["protocolVersion", "epochToken", "kind", "operation", "reason"],
      path,
    );
    if (record.kind === "failure") return record;
    const header = decodeHeader(record.value, path);
    if (header.kind === "failure") return header;
    const expectedHeader = requireExpectedEpoch(
      header.value,
      expectedEpochToken,
      path,
    );
    if (expectedHeader.kind === "failure") return expectedHeader;
    const operation = decodeOperationReference(
      record.value.get("operation"),
      "$.operation",
    );
    if (operation.kind === "failure") return operation;
    const reason = decodeCancellationReason(
      record.value.get("reason"),
      "$.reason",
    );
    if (reason.kind === "failure") return reason;
    return success({
      ...expectedHeader.value,
      kind: "cancel",
      operation: operation.value,
      reason: reason.value,
    });
  }

  if (discriminator.value === "probe") {
    const record = decodeClosedRecord(
      value,
      ["protocolVersion", "epochToken", "kind", "probeSequence"],
      path,
    );
    if (record.kind === "failure") return record;
    const header = decodeHeader(record.value, path);
    if (header.kind === "failure") return header;
    const expectedHeader = requireExpectedEpoch(
      header.value,
      expectedEpochToken,
      path,
    );
    if (expectedHeader.kind === "failure") return expectedHeader;
    const probeSequence = decodePositiveSafeInteger(
      record.value.get("probeSequence"),
      "$.probeSequence",
    );
    if (probeSequence.kind === "failure") return probeSequence;
    return success({
      ...expectedHeader.value,
      kind: "probe",
      probeSequence: probeSequence.value,
    });
  }

  return failure(
    "value",
    "invalid-discriminator",
    "$.kind",
    "Unknown main-to-worker envelope discriminator.",
  );
}

export function decodeWorkerToMainEnvelope(
  value: unknown,
  expectedEpochToken: WorkerWireEpochToken,
): WorkerEnvelopeDecodeResult<RawWorkerToMainEnvelope> {
  const path = "$";
  const discriminator = decodeDiscriminator(value, path);
  if (discriminator.kind === "failure") return discriminator;

  if (discriminator.value === "ready") {
    const record = decodeClosedRecord(
      value,
      [
        "protocolVersion",
        "epochToken",
        "kind",
        "idleHeartbeatIntervalMilliseconds",
      ],
      path,
    );
    if (record.kind === "failure") return record;
    const header = decodeHeader(record.value, path);
    if (header.kind === "failure") return header;
    const expectedHeader = requireExpectedEpoch(
      header.value,
      expectedEpochToken,
      path,
    );
    if (expectedHeader.kind === "failure") return expectedHeader;
    const milliseconds = decodePositiveSafeInteger(
      record.value.get("idleHeartbeatIntervalMilliseconds"),
      "$.idleHeartbeatIntervalMilliseconds",
    );
    if (milliseconds.kind === "failure") return milliseconds;
    return success({
      ...expectedHeader.value,
      kind: "ready",
      idleHeartbeatIntervalMilliseconds: milliseconds.value,
    });
  }

  if (discriminator.value === "startup-failed"
    || discriminator.value === "epoch-failed") {
    const record = decodeClosedRecord(
      value,
      ["protocolVersion", "epochToken", "kind", "diagnostic"],
      path,
    );
    if (record.kind === "failure") return record;
    const header = decodeHeader(record.value, path);
    if (header.kind === "failure") return header;
    const expectedHeader = requireExpectedEpoch(
      header.value,
      expectedEpochToken,
      path,
    );
    if (expectedHeader.kind === "failure") return expectedHeader;
    return success({
      ...expectedHeader.value,
      kind: discriminator.value,
      diagnostic: record.value.get("diagnostic"),
    });
  }

  if (discriminator.value === "accepted") {
    const record = decodeClosedRecord(
      value,
      [
        "protocolVersion",
        "epochToken",
        "kind",
        "operation",
        "allowance",
      ],
      path,
    );
    if (record.kind === "failure") return record;
    const header = decodeHeader(record.value, path);
    if (header.kind === "failure") return header;
    const expectedHeader = requireExpectedEpoch(
      header.value,
      expectedEpochToken,
      path,
    );
    if (expectedHeader.kind === "failure") return expectedHeader;
    const operation = decodeOperationReference(
      record.value.get("operation"),
      "$.operation",
    );
    if (operation.kind === "failure") return operation;
    const allowance = decodeAllowance(
      record.value.get("allowance"),
      "$.allowance",
    );
    if (allowance.kind === "failure") return allowance;
    return success({
      ...expectedHeader.value,
      kind: "accepted",
      operation: operation.value,
      allowance: allowance.value,
    });
  }

  if (discriminator.value === "rejected") {
    const record = decodeClosedRecord(
      value,
      [
        "protocolVersion",
        "epochToken",
        "kind",
        "operation",
        "error",
        "diagnostic",
      ],
      path,
    );
    if (record.kind === "failure") return record;
    const header = decodeHeader(record.value, path);
    if (header.kind === "failure") return header;
    const expectedHeader = requireExpectedEpoch(
      header.value,
      expectedEpochToken,
      path,
    );
    if (expectedHeader.kind === "failure") return expectedHeader;
    const operation = decodeOperationReference(
      record.value.get("operation"),
      "$.operation",
    );
    if (operation.kind === "failure") return operation;
    return success({
      ...expectedHeader.value,
      kind: "rejected",
      operation: operation.value,
      error: record.value.get("error"),
      diagnostic: record.value.get("diagnostic"),
    });
  }

  if (discriminator.value === "cancel-acknowledged") {
    const record = decodeClosedRecord(
      value,
      ["protocolVersion", "epochToken", "kind", "operation", "status"],
      path,
    );
    if (record.kind === "failure") return record;
    const header = decodeHeader(record.value, path);
    if (header.kind === "failure") return header;
    const expectedHeader = requireExpectedEpoch(
      header.value,
      expectedEpochToken,
      path,
    );
    if (expectedHeader.kind === "failure") return expectedHeader;
    const operation = decodeOperationReference(
      record.value.get("operation"),
      "$.operation",
    );
    if (operation.kind === "failure") return operation;
    const status = decodeLiteral<"running" | "not-active">(
      record.value.get("status"),
      isCancellationStatus,
      "$.status",
      "running or not-active cancellation status",
    );
    if (status.kind === "failure") return status;
    return success({
      ...expectedHeader.value,
      kind: "cancel-acknowledged",
      operation: operation.value,
      status: status.value,
    });
  }

  if (discriminator.value === "progress") {
    const record = decodeClosedRecord(
      value,
      ["protocolVersion", "epochToken", "kind", "operation", "payload"],
      path,
    );
    if (record.kind === "failure") return record;
    const header = decodeHeader(record.value, path);
    if (header.kind === "failure") return header;
    const expectedHeader = requireExpectedEpoch(
      header.value,
      expectedEpochToken,
      path,
    );
    if (expectedHeader.kind === "failure") return expectedHeader;
    const operation = decodeOperationReference(
      record.value.get("operation"),
      "$.operation",
    );
    if (operation.kind === "failure") return operation;
    return success({
      ...expectedHeader.value,
      kind: "progress",
      operation: operation.value,
      payload: record.value.get("payload"),
    });
  }

  if (discriminator.value === "settled") {
    const record = decodeClosedRecord(
      value,
      ["protocolVersion", "epochToken", "kind", "operation", "settlement"],
      path,
    );
    if (record.kind === "failure") return record;
    const header = decodeHeader(record.value, path);
    if (header.kind === "failure") return header;
    const expectedHeader = requireExpectedEpoch(
      header.value,
      expectedEpochToken,
      path,
    );
    if (expectedHeader.kind === "failure") return expectedHeader;
    const operation = decodeOperationReference(
      record.value.get("operation"),
      "$.operation",
    );
    if (operation.kind === "failure") return operation;
    const settlement = decodeRawSettlement(
      record.value.get("settlement"),
      "$.settlement",
    );
    if (settlement.kind === "failure") return settlement;
    return success({
      ...expectedHeader.value,
      kind: "settled",
      operation: operation.value,
      settlement: settlement.value,
    });
  }

  if (discriminator.value === "heartbeat") {
    const record = decodeClosedRecord(
      value,
      ["protocolVersion", "epochToken", "kind"],
      path,
    );
    if (record.kind === "failure") return record;
    const header = decodeHeader(record.value, path);
    if (header.kind === "failure") return header;
    const expectedHeader = requireExpectedEpoch(
      header.value,
      expectedEpochToken,
      path,
    );
    if (expectedHeader.kind === "failure") return expectedHeader;
    return success({ ...expectedHeader.value, kind: "heartbeat" });
  }

  if (discriminator.value === "probe-acknowledged") {
    const record = decodeClosedRecord(
      value,
      ["protocolVersion", "epochToken", "kind", "probeSequence"],
      path,
    );
    if (record.kind === "failure") return record;
    const header = decodeHeader(record.value, path);
    if (header.kind === "failure") return header;
    const expectedHeader = requireExpectedEpoch(
      header.value,
      expectedEpochToken,
      path,
    );
    if (expectedHeader.kind === "failure") return expectedHeader;
    const probeSequence = decodePositiveSafeInteger(
      record.value.get("probeSequence"),
      "$.probeSequence",
    );
    if (probeSequence.kind === "failure") return probeSequence;
    return success({
      ...expectedHeader.value,
      kind: "probe-acknowledged",
      probeSequence: probeSequence.value,
    });
  }

  if (discriminator.value === "epoch-work-started") {
    const record = decodeClosedRecord(
      value,
      [
        "protocolVersion",
        "epochToken",
        "kind",
        "workSequence",
        "allowance",
      ],
      path,
    );
    if (record.kind === "failure") return record;
    const header = decodeHeader(record.value, path);
    if (header.kind === "failure") return header;
    const expectedHeader = requireExpectedEpoch(
      header.value,
      expectedEpochToken,
      path,
    );
    if (expectedHeader.kind === "failure") return expectedHeader;
    const workSequence = decodePositiveSafeInteger(
      record.value.get("workSequence"),
      "$.workSequence",
    );
    if (workSequence.kind === "failure") return workSequence;
    const allowance = decodeAllowance(
      record.value.get("allowance"),
      "$.allowance",
    );
    if (allowance.kind === "failure") return allowance;
    return success({
      ...expectedHeader.value,
      kind: "epoch-work-started",
      workSequence: workSequence.value,
      allowance: allowance.value,
    });
  }

  if (discriminator.value === "epoch-work-finished") {
    const record = decodeClosedRecord(
      value,
      ["protocolVersion", "epochToken", "kind", "workSequence"],
      path,
    );
    if (record.kind === "failure") return record;
    const header = decodeHeader(record.value, path);
    if (header.kind === "failure") return header;
    const expectedHeader = requireExpectedEpoch(
      header.value,
      expectedEpochToken,
      path,
    );
    if (expectedHeader.kind === "failure") return expectedHeader;
    const workSequence = decodePositiveSafeInteger(
      record.value.get("workSequence"),
      "$.workSequence",
    );
    if (workSequence.kind === "failure") return workSequence;
    return success({
      ...expectedHeader.value,
      kind: "epoch-work-finished",
      workSequence: workSequence.value,
    });
  }

  return failure(
    "value",
    "invalid-discriminator",
    "$.kind",
    "Unknown worker-to-main envelope discriminator.",
  );
}
