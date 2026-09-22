import type {
  BrowserPackageChangesCancellation,
  BrowserPackageChangesFailure,
  BrowserPackageChangesInspection,
  BrowserPackageChangesProgress,
  BrowserPackageChangesRequest,
  BrowserPackageChangesResult,
  BrowserPackageChangesRow,
  DateTimeOffsetString,
} from "./facades/inspect-web-package.d.ts";
import type {
  WorkerRuntimeOperationRegistration,
  WorkerRuntimePreparationError,
} from "./worker-runtime-core.ts";
import type {
  BoundedPayloadDecodeResult,
  BoundedPayloadDecoder,
  ManagedOperationSettlement,
  WorkerOperationCancelReason,
} from "./worker-runtime-protocol.ts";
import { isWorkerOperationCancelReason } from "./worker-runtime-protocol.ts";
import type {
  WorkerOperationCatalog,
  WorkerOperationContext,
} from "./worker-runtime-realm.ts";
import {
  mapEngineWorkerBoundaryErrors,
} from "./engine-worker-contract.ts";

const engineWorkerPackageChangesKind = "package-changes";

const maximumRequestCharacters = 1_024;
const maximumCallbackCharacters = 8 * 1_024 * 1_024;
const maximumTerminalCharacters = 32 * 1_024 * 1_024;
const maximumDiagnosticCharacters = 64 * 1_024;
// Covers the densest schema-valid shape beneath the terminal text ceiling,
// including advisory references repeated in rows and the summary.
const maximumItems = 1_250_000;
const dateTimeOffsetJsonPattern =
  /^(\d{4})-(\d{2})-(\d{2})T(\d{2}):(\d{2}):(\d{2})(?:\.(\d{1,7}))?(Z|([+-])(\d{2}):(\d{2}))$/;

function isLeapYear(year: number): boolean {
  return year % 4 === 0 && (year % 100 !== 0 || year % 400 === 0);
}

function daysInMonth(year: number, month: number): number {
  if (month === 2)
    return isLeapYear(year) ? 29 : 28;
  if (month === 4 || month === 6 || month === 9 || month === 11)
    return 30;
  return 31;
}

export function isDateTimeOffsetJsonString(
  value: string,
): value is DateTimeOffsetString {
  const match = dateTimeOffsetJsonPattern.exec(value);
  if (match === null)
    return false;

  const year = Number(match[1]);
  const month = Number(match[2]);
  const day = Number(match[3]);
  const hour = Number(match[4]);
  const minute = Number(match[5]);
  const second = Number(match[6]);
  if (year < 1
    || month < 1
    || month > 12
    || day < 1
    || day > daysInMonth(year, month)
    || hour > 23
    || minute > 59
    || second > 59) {
    return false;
  }

  if (match[8] === "Z")
    return true;

  const offsetSign = match[9];
  const offsetHour = Number(match[10]);
  const offsetMinute = Number(match[11]);
  if (offsetHour > 14
    || offsetMinute > 59
    || (offsetHour === 14 && offsetMinute !== 0)) {
    return false;
  }

  const offsetSeconds = (offsetHour * 60 + offsetMinute) * 60;
  const localSeconds = hour * 60 * 60 + minute * 60 + second;
  if (year === 1
    && month === 1
    && day === 1
    && offsetSign === "+"
    && localSeconds < offsetSeconds) {
    return false;
  }
  return !(year === 9999
    && month === 12
    && day === 31
    && offsetSign === "-"
    && localSeconds + offsetSeconds >= 24 * 60 * 60);
}

type Schema =
  | { readonly kind: "string"; readonly maximum: number;
      readonly values?: readonly string[]; readonly timestamp?: boolean }
  | { readonly kind: "number"; readonly maximum?: number }
  | { readonly kind: "boolean" }
  | { readonly kind: "nullable"; readonly value: Schema }
  | { readonly kind: "array"; readonly value: Schema;
      readonly maximum: number }
  | { readonly kind: "object";
      readonly fields: Readonly<Record<string, Schema>> };

interface PayloadBudget {
  remainingCharacters: number;
  remainingItems: number;
}

class PackageChangesPayloadError extends Error {
  readonly reason: "invalid" | "oversized";

  constructor(
    message: string,
    reason: "invalid" | "oversized" = "invalid",
  ) {
    super(message);
    this.reason = reason;
  }
}

function text(
  maximum = maximumDiagnosticCharacters,
  values?: readonly string[],
): Schema {
  return values === undefined
    ? { kind: "string", maximum }
    : { kind: "string", maximum, values };
}

function timestamp(allowNull = false): Schema {
  const value: Schema = {
    kind: "string",
    maximum: 64,
    timestamp: true,
  };
  return allowNull ? { kind: "nullable", value } : value;
}

function integer(maximum?: number): Schema {
  return maximum === undefined
    ? { kind: "number" }
    : { kind: "number", maximum };
}

function nullable(value: Schema): Schema {
  return { kind: "nullable", value };
}

function array(value: Schema, maximum: number): Schema {
  return { kind: "array", value, maximum };
}

function record(fields: Readonly<Record<string, Schema>>): Schema {
  return { kind: "object", fields };
}

const advisoryReferenceSchema = record({
  ghsaId: text(64),
  cveId: nullable(text(64)),
  severity: text(16, ["Unknown", "Low", "Medium", "High", "Critical"]),
  advisoryUrl: text(2_048),
  publishedAt: timestamp(),
  updatedAt: timestamp(),
});

const advisoryEvidenceSchema = record({
  availability: text(16, ["Complete", "Partial", "Unavailable"]),
  advisories: array(advisoryReferenceSchema, 4_096),
});

const catalogActivitySchema = record({
  packageId: text(256),
  version: text(256),
  normalizedPackageId: text(256),
  normalizedVersion: text(256),
  leafUrl: text(2_048),
  commitId: text(128),
  commitTimestamp: timestamp(),
  catalogKind: text(16, ["Details", "Delete"]),
  activity: text(32, ["SnapshotObserved", "DeletionObserved"]),
});

const packageReceiptSchema = record({
  receivedAt: timestamp(),
  basis: text(32, ["Created", "PublishedFallback"]),
});

const securityReleaseSchema = record({
  receipt: packageReceiptSchema,
  advisories: array(advisoryReferenceSchema, 4_096),
});

const rowSchema = record({
  catalogActivity: catalogActivitySchema,
  currentAdvisoryContext: advisoryEvidenceSchema,
  fixedVersionEvidence: advisoryEvidenceSchema,
  packageReceipt: nullable(packageReceiptSchema),
  securityReleaseStatus: text(48, [
    "CheckedNoFixedVersionAssociation",
    "FixedVersionEvidenceUnavailable",
    "EvidencedInInterval",
    "ReceiptOutsideInterval",
    "DeleteActivityUnevaluable",
    "ReceiptFailure",
    "ReceiptLimitReached",
  ]),
  securityRelease: nullable(securityReleaseSchema),
  isSecurityRelevant: { kind: "boolean" },
});

const packageSourceFailureSchema = record({
  capability: integer(63),
  packageId: nullable(text(256)),
  version: nullable(text(256)),
  kind: text(32, [
    "Unsupported",
    "NotFound",
    "AuthenticationRequired",
    "Timeout",
    "InvalidResponse",
    "ResponseRejected",
    "Transport",
  ]),
  detail: text(),
});

const receiptFailureSchema = record({
  catalogActivity: catalogActivitySchema,
  failure: packageSourceFailureSchema,
});

const failureSchema = record({
  provider: text(32, ["Catalog", "Advisory", "PackageReceipt"]),
  catalogFailure: nullable(packageSourceFailureSchema),
  advisoryFailure: nullable(text(48, [
    "RequestLimitReached",
    "ResponseByteLimitReached",
    "AggregateResponseByteLimitReached",
    "DeadlineReached",
    "RateLimitOrForbidden",
    "SourceUnavailable",
    "InvalidData",
    "InvalidContinuation",
  ])),
  packageReceiptFailure: nullable(receiptFailureSchema),
});

const progressSchema = record({
  phase: text(32, ["Catalog", "Advisory", "PackageReceipt"]),
  completed: integer(),
  total: nullable(integer()),
  capturedHorizon: timestamp(true),
  catalogPagesAcquired: integer(),
  catalogHttpAttempts: integer(),
  catalogDecodedBytes: integer(),
});

const advisoryPackageSchema = record({
  packageId: text(256),
  version: text(256),
  currentAdvisoryContext: advisoryEvidenceSchema,
  fixedVersionEvidence: advisoryEvidenceSchema,
});

const advisoryAcquisitionSchema = record({
  packageProducerKey: text(256),
  advisoryProducer: text(256),
  observedAt: timestamp(),
  apiRequests: integer(),
  responseBytes: integer(),
  complete: { kind: "boolean" },
  failures: array(text(48, [
    "RequestLimitReached",
    "ResponseByteLimitReached",
    "AggregateResponseByteLimitReached",
    "DeadlineReached",
    "RateLimitOrForbidden",
    "SourceUnavailable",
    "InvalidData",
    "InvalidContinuation",
  ]), 32),
  packages: array(advisoryPackageSchema, 1_000),
});

const summarySchema = record({
  capturedHorizon: timestamp(true),
  catalogCompletion: nullable(text(32, [
    "WindowExhausted",
    "SourceHorizonReached",
    "PageLimitReached",
    "RequestLimitReached",
    "DecodedByteLimitReached",
  ])),
  catalogFailure: nullable(packageSourceFailureSchema),
  catalogPagesAcquired: integer(),
  catalogHttpAttempts: integer(),
  catalogDecodedBytes: integer(),
  catalogInWindowEventCount: integer(),
  matchingEventCount: integer(),
  retainedEventCount: integer(),
  candidateLimitReached: { kind: "boolean" },
  advisoryEvidence: advisoryAcquisitionSchema,
  receiptCandidates: integer(),
  receiptRequests: integer(),
  receiptSuccesses: integer(),
  receiptFailures: array(receiptFailureSchema, 1_000),
  receiptLimitReached: { kind: "boolean" },
  currentContextUnevaluableRows: integer(),
  securityReleaseUnevaluableRows: integer(),
  eligibleRowCount: integer(),
  returnedRowCount: integer(),
  resultLimitReached: { kind: "boolean" },
  completion: text(32, [
    "Complete",
    "ResultLimitReached",
    "Partial",
    "Failed",
  ]),
});

const documentSchema = record({
  schemaVersion: integer(1),
  request: record({
    referenceTime: timestamp(),
    fromExclusive: timestamp(),
    throughInclusive: timestamp(),
    usedDefaultInterval: { kind: "boolean" },
    packageScope: record({
      kind: text(16, ["PackageSet"]),
      selectionId: nullable(text(80)),
      prefix: nullable(text(256)),
      packageIds: array(text(256), 1_000),
    }),
    securitySelection: text(32, ["AllActivity", "SecurityRelevant"]),
    maximumRows: integer(1_000),
    maximumCandidateEvents: integer(1_000),
    maximumReceiptRequests: integer(1_000),
  }),
  source: record({
    producerKey: text(256),
    producer: text(512),
    transportKind: text(16, ["NuGetV3"]),
  }),
  progress: array(progressSchema, 4_096),
  rows: array(rowSchema, 1_000),
  failures: array(failureSchema, 2_002),
  summary: summarySchema,
});

const shareSchema = record({
  kind: text(32, ["Available", "NonProjectable"]),
  fullUrl: nullable(text(8_192)),
  packet: nullable(text(8_192)),
  path: nullable(text(256)),
  reason: nullable(text()),
});

const diagnosticSchema = record({
  code: text(256),
  severity: text(32),
  summary: text(),
  correspondence: nullable(text()),
});

const inspectionSchema = record({
  content: documentSchema,
  share: shareSchema,
  diagnostics: array(diagnosticSchema, 1_024),
});

const eventSchema = record({
  kind: text(16, ["Progress", "Row", "Failure"]),
  progress: nullable(progressSchema),
  row: nullable(rowSchema),
  failure: nullable(failureSchema),
});

const requestSchema = record({
  packageSetId: text(80),
  fromExclusive: nullable(timestamp()),
  throughInclusive: nullable(timestamp()),
  securityOnly: { kind: "boolean" },
  maximumRows: integer(1_000),
});

function ownData(
  value: object,
  name: string,
  path: string,
): unknown {
  const property = Object.getOwnPropertyDescriptor(value, name);
  if (property === undefined || !("value" in property)) {
    throw new PackageChangesPayloadError(
      `${path}.${name} must be an own data property.`);
  }
  return property.value;
}

function consumeItem(budget: PayloadBudget, path: string): void {
  if (--budget.remainingItems < 0) {
    throw new PackageChangesPayloadError(
      `${path} exceeds the Package Activity item budget.`,
      "oversized",
    );
  }
}

function decodeSchema(
  schema: Schema,
  value: unknown,
  budget: PayloadBudget,
  path: string,
): void {
  if (schema.kind === "nullable") {
    if (value !== null)
      decodeSchema(schema.value, value, budget, path);
    return;
  }
  if (schema.kind === "string") {
    if (typeof value !== "string")
      throw new PackageChangesPayloadError(`${path} must be text.`);
    if (value.length > schema.maximum) {
      throw new PackageChangesPayloadError(
        `${path} exceeds ${schema.maximum} characters.`,
        "oversized",
      );
    }
    budget.remainingCharacters -= value.length;
    if (budget.remainingCharacters < 0) {
      throw new PackageChangesPayloadError(
        `${path} exceeds the Package Activity text budget.`,
        "oversized",
      );
    }
    if (schema.values !== undefined && !schema.values.includes(value))
      throw new PackageChangesPayloadError(`${path} has an unknown value.`);
    if (schema.timestamp === true && !isDateTimeOffsetJsonString(value)) {
      throw new PackageChangesPayloadError(
        `${path} must be an ISO 8601 timestamp.`);
    }
    return;
  }
  if (schema.kind === "number") {
    if (typeof value !== "number"
      || !Number.isSafeInteger(value)
      || value < 0
      || (schema.maximum !== undefined
        && value > schema.maximum)) {
      throw new PackageChangesPayloadError(
        `${path} must be a bounded non-negative integer.`);
    }
    return;
  }
  if (schema.kind === "boolean") {
    if (typeof value !== "boolean")
      throw new PackageChangesPayloadError(`${path} must be Boolean.`);
    return;
  }
  if (schema.kind === "array") {
    if (!Array.isArray(value))
      throw new PackageChangesPayloadError(`${path} must be an array.`);
    if (value.length > schema.maximum) {
      throw new PackageChangesPayloadError(
        `${path} exceeds ${schema.maximum} items.`,
        "oversized",
      );
    }
    value.forEach((item, index) => {
      consumeItem(budget, `${path}[${index}]`);
      decodeSchema(
        schema.value,
        item,
        budget,
        `${path}[${index}]`,
      );
    });
    return;
  }

  if (typeof value !== "object" || value === null || Array.isArray(value))
    throw new PackageChangesPayloadError(`${path} must be an object.`);
  consumeItem(budget, path);
  const names = Object.keys(schema.fields);
  const keys = Reflect.ownKeys(value);
  if (keys.length !== names.length
    || !names.every(name => keys.includes(name))) {
    throw new PackageChangesPayloadError(
      `${path} must contain exactly: ${names.join(", ")}.`);
  }
  names.forEach(name =>
    decodeSchema(
      schema.fields[name]!,
      ownData(value, name, path),
      budget,
      `${path}.${name}`,
    ));
}

function rejected(
  error: unknown,
): BoundedPayloadDecodeResult<never> {
  if (error instanceof PackageChangesPayloadError) {
    return {
      kind: "rejected",
      reason: error.reason,
      message: error.message,
      cause: error,
    };
  }
  return {
    kind: "rejected",
    reason: "invalid",
    message: error instanceof Error
      ? error.message
      : "Package Activity payload validation failed.",
    cause: error,
  };
}

function validate(
  schema: Schema,
  value: unknown,
  characterBudget: number,
  path: string,
): void {
  decodeSchema(
    schema,
    value,
    {
      remainingCharacters: characterBudget,
      remainingItems: maximumItems,
    },
    path,
  );
}

function assertRequest(
  value: unknown,
): asserts value is BrowserPackageChangesRequest {
  validate(
    requestSchema,
    value,
    maximumRequestCharacters,
    "Package Activity request",
  );
}

export const engineWorkerPackageChangesInput:
BoundedPayloadDecoder<BrowserPackageChangesRequest> = {
  decode(value) {
    try {
      assertRequest(value);
      const request = value;
      if (!/^package-set\.[a-z][a-z0-9]*(?:-[a-z0-9]+)*$/.test(
        request.packageSetId,
      )) {
        throw new PackageChangesPayloadError(
          "Package Activity requires a canonical package-set identity.");
      }
      if ((request.fromExclusive === null)
        !== (request.throughInclusive === null)) {
        throw new PackageChangesPayloadError(
          "Package Activity interval endpoints must be supplied together.");
      }
      if (request.maximumRows < 1) {
        throw new PackageChangesPayloadError(
          "Package Activity maximumRows must be positive.");
      }
      return { kind: "decoded", value: request };
    } catch (error: unknown) {
      return rejected(error);
    }
  },
};

type RawPackageChangesEvent = {
  readonly kind: "Progress" | "Row" | "Failure";
  readonly progress: BrowserPackageChangesProgress | null;
  readonly row: BrowserPackageChangesRow | null;
  readonly failure: BrowserPackageChangesFailure | null;
};

function assertEvent(
  value: unknown,
): asserts value is RawPackageChangesEvent {
  validate(
    eventSchema,
    value,
    maximumCallbackCharacters,
    "Package Activity event",
  );
}

function assertProgress(
  value: unknown,
): asserts value is BrowserPackageChangesProgress {
  validate(
    progressSchema,
    value,
    maximumCallbackCharacters,
    "Package Activity progress",
  );
}

function assertInspection(
  value: unknown,
): asserts value is BrowserPackageChangesInspection {
  validate(
    inspectionSchema,
    value,
    maximumTerminalCharacters,
    "Package Activity inspection",
  );
}

function assertDiagnosticText(
  value: unknown,
): asserts value is string {
  validate(
    text(maximumDiagnosticCharacters),
    value,
    maximumDiagnosticCharacters,
    "Package Activity diagnostic",
  );
}

function assertTerminalFailure(
  value: unknown,
): asserts value is EngineWorkerPackageChangesTerminalFailure {
  validate(
    record({
      failureKind: text(16, ["Expected", "Unexpected"]),
      error: text(maximumDiagnosticCharacters),
      diagnostic: text(maximumDiagnosticCharacters),
    }),
    value,
    maximumDiagnosticCharacters * 2,
    "Package Activity failure",
  );
}

function assertResult(
  value: unknown,
): asserts value is BrowserPackageChangesResult {
  validate(
    record({
      version: integer(1),
      kind: text(16, ["Succeeded", "Failed", "Canceled"]),
      inspection: nullable(inspectionSchema),
      failureKind: nullable(text(16, ["Expected", "Unexpected"])),
      error: nullable(text(maximumDiagnosticCharacters)),
      diagnostic: nullable(text(maximumDiagnosticCharacters)),
      reason: nullable(text(64)),
    }),
    value,
    maximumTerminalCharacters,
    "Package Activity result",
  );
}

function assertCancellation(
  value: unknown,
): asserts value is BrowserPackageChangesCancellation {
  validate(
    record({
      kind: text(32, ["Requested", "AlreadyRequested", "NotActive"]),
      reason: nullable(text(64)),
    }),
    value,
    256,
    "Package Activity cancellation",
  );
}

export type EngineWorkerPackageChangesDurableEvent =
  | RawPackageChangesEvent & {
      readonly kind: "Row";
      readonly row: BrowserPackageChangesRow;
    }
  | RawPackageChangesEvent & {
      readonly kind: "Failure";
      readonly failure: BrowserPackageChangesFailure;
    };

type EngineWorkerPackageChangesProgressEvent =
  RawPackageChangesEvent & {
    readonly kind: "Progress";
    readonly progress: BrowserPackageChangesProgress;
  };

export type EngineWorkerPackageChangesEvent =
  EngineWorkerPackageChangesProgressEvent
  | EngineWorkerPackageChangesDurableEvent;

export function decodeEngineWorkerPackageChangesEvent(
  value: unknown,
): EngineWorkerPackageChangesEvent {
  assertEvent(value);
  const event = value;
  if (event.kind === "Progress"
    && event.progress !== null
    && event.row === null
    && event.failure === null) {
    return {
      kind: "Progress",
      progress: event.progress,
      row: null,
      failure: null,
    };
  }
  if (event.kind === "Row"
    && event.progress === null
    && event.row !== null
    && event.failure === null) {
    return {
      kind: "Row",
      progress: null,
      row: event.row,
      failure: null,
    };
  }
  if (event.kind === "Failure"
    && event.progress === null
    && event.row === null
    && event.failure !== null) {
    return {
      kind: "Failure",
      progress: null,
      row: null,
      failure: event.failure,
    };
  }
  throw new PackageChangesPayloadError(
    "Package Activity event payload does not match its kind.");
}

const engineWorkerPackageChangesProgress:
BoundedPayloadDecoder<BrowserPackageChangesProgress> = {
  decode(value) {
    try {
      assertProgress(value);
      return {
        kind: "decoded",
        value,
      };
    } catch (error: unknown) {
      return rejected(error);
    }
  },
};

const engineWorkerPackageChangesDurableEvent:
BoundedPayloadDecoder<EngineWorkerPackageChangesDurableEvent> = {
  decode(value) {
    try {
      const event = decodeEngineWorkerPackageChangesEvent(value);
      if (event.kind === "Progress") {
        throw new PackageChangesPayloadError(
          "Package Activity progress is not a durable event.");
      }
      return {
        kind: "decoded",
        value: event,
      };
    } catch (error: unknown) {
      return rejected(error);
    }
  },
};

export const engineWorkerPackageChangesInspection:
BoundedPayloadDecoder<BrowserPackageChangesInspection> = {
  decode(value) {
    try {
      assertInspection(value);
      const inspection = value;
      if (inspection.content.schemaVersion !== 1) {
        throw new PackageChangesPayloadError(
          "Package Activity Document schema version is unsupported.");
      }
      if (inspection.share.kind === "Available") {
        if (inspection.share.fullUrl === null
          || inspection.share.packet === null
          || inspection.share.path !== null
          || inspection.share.reason !== null) {
          throw new PackageChangesPayloadError(
            "Available Package Activity Share has invalid fields.");
        }
      } else if (inspection.share.fullUrl !== null
        || inspection.share.packet !== null
        || inspection.share.path === null
        || inspection.share.reason === null) {
        throw new PackageChangesPayloadError(
          "Non-projectable Package Activity Share has invalid fields.");
      }
      return { kind: "decoded", value: inspection };
    } catch (error: unknown) {
      return rejected(error);
    }
  },
};

export interface EngineWorkerPackageChangesTerminalFailure {
  readonly failureKind: "Expected" | "Unexpected";
  readonly error: string;
  readonly diagnostic: string;
}

const packageChangesText: BoundedPayloadDecoder<string> = {
  decode(value) {
    try {
      assertDiagnosticText(value);
      return {
        kind: "decoded",
        value,
      };
    } catch (error: unknown) {
      return rejected(error);
    }
  },
};

const packageChangesFailure:
BoundedPayloadDecoder<EngineWorkerPackageChangesTerminalFailure> = {
  decode(value) {
    try {
      assertTerminalFailure(value);
      return {
        kind: "decoded",
        value,
      };
    } catch (error: unknown) {
      return rejected(error);
    }
  },
};

type PackageChangesSettlement = ManagedOperationSettlement<
  BrowserPackageChangesInspection,
  EngineWorkerPackageChangesTerminalFailure,
  string
>;

function invalidResult(error: unknown): PackageChangesSettlement {
  const diagnostic = error instanceof Error
    ? error.message.slice(0, maximumDiagnosticCharacters)
    : "Package Activity result validation failed.";
  return {
    kind: "failed",
    failureKind: "unexpected",
    error: {
      failureKind: "Unexpected",
      error: "Package Activity returned invalid Worker boundary data.",
      diagnostic,
    },
    diagnostic,
  };
}

export function mapEngineWorkerPackageChangesResult(
  value: unknown,
): PackageChangesSettlement {
  try {
    assertResult(value);
    const result = value;
    if (result.version !== 1) {
      throw new PackageChangesPayloadError(
        "Expected a version 1 Package Activity result.");
    }
    if (result.kind === "Succeeded") {
      if (result.inspection === null
        || result.failureKind !== null
        || result.error !== null
        || result.diagnostic !== null
        || result.reason !== null) {
        throw new PackageChangesPayloadError(
          "Package Activity success has invalid terminal fields.");
      }
      const inspection =
        engineWorkerPackageChangesInspection.decode(result.inspection);
      if (inspection.kind === "rejected")
        throw new PackageChangesPayloadError(
          inspection.message,
          inspection.reason);
      return { kind: "succeeded", value: inspection.value };
    }
    if (result.kind === "Failed") {
      if (result.inspection !== null
        || (result.failureKind !== "Expected"
          && result.failureKind !== "Unexpected")
        || result.error === null
        || result.diagnostic === null
        || result.reason !== null) {
        throw new PackageChangesPayloadError(
          "Package Activity failure has invalid terminal fields.");
      }
      return {
        kind: "failed",
        failureKind: result.failureKind === "Expected"
          ? "expected"
          : "unexpected",
        error: {
          failureKind: result.failureKind,
          error: result.error,
          diagnostic: result.diagnostic,
        },
        diagnostic: result.diagnostic,
      };
    }
    if (result.inspection !== null
      || result.failureKind !== null
      || result.error !== null
      || result.diagnostic !== null
      || result.reason === null
      || !isWorkerOperationCancelReason(result.reason)) {
      throw new PackageChangesPayloadError(
        "Package Activity cancellation has invalid terminal fields.");
    }
    return { kind: "canceled", reason: result.reason };
  } catch (error: unknown) {
    return invalidResult(error);
  }
}

export function engineWorkerPackageChangesCancellationIsRunning(
  value: unknown,
  requestedReason: WorkerOperationCancelReason,
): boolean {
  assertCancellation(value);
  const cancellation = value;
  if (cancellation.kind === "NotActive") {
    if (cancellation.reason !== null) {
      throw new PackageChangesPayloadError(
        "Inactive Package Activity cancellation has a reason.");
    }
    return false;
  }
  if (cancellation.reason === null
    || !isWorkerOperationCancelReason(cancellation.reason)) {
    throw new PackageChangesPayloadError(
      "Package Activity cancellation reason is invalid.");
  }
  if (cancellation.kind === "Requested"
    && cancellation.reason !== requestedReason) {
    throw new PackageChangesPayloadError(
      "Package Activity cancellation changed the requested reason.");
  }
  return true;
}

const packageChangesBoundaryErrors =
  mapEngineWorkerBoundaryErrors(
    (message): EngineWorkerPackageChangesTerminalFailure => ({
      failureKind: "Unexpected",
      error: message,
      diagnostic: message,
    }),
  );

export function createEngineWorkerPackageChangesHostRegistration():
WorkerRuntimeOperationRegistration<
  BrowserPackageChangesRequest,
  BrowserPackageChangesInspection,
  EngineWorkerPackageChangesTerminalFailure,
  string,
  BrowserPackageChangesProgress,
  WorkerRuntimePreparationError,
  EngineWorkerPackageChangesDurableEvent
> {
  return {
    kind: engineWorkerPackageChangesKind,
    allowance: { kind: "unbounded" },
    encodeInput: value => engineWorkerPackageChangesInput.decode(value),
    value: engineWorkerPackageChangesInspection,
    error: packageChangesFailure,
    diagnostic: packageChangesText,
    progress: engineWorkerPackageChangesProgress,
    durable: engineWorkerPackageChangesDurableEvent,
    mapPreparationError: error => error,
    boundaryErrors: packageChangesBoundaryErrors,
  };
}

export type EngineWorkerPackageChangesFacade = Pick<
  typeof import("./facades/inspect-web-package.d.ts"),
  "cancelPackageActivity" | "runPackageActivity"
>;

function createManagedEventSink(
  context: WorkerOperationContext,
): Record<string, unknown> {
  const eventSink: Record<string, unknown> = {};
  Object.defineProperty(eventSink, "event", {
    set(value: unknown) {
      if (typeof value !== "string") {
        throw new PackageChangesPayloadError(
          "Package Activity callback payload was not JSON text.");
      }
      if (value.length > maximumCallbackCharacters) {
        throw new PackageChangesPayloadError(
          "Package Activity callback exceeds its wire budget.",
          "oversized",
        );
      }
      let parsed: unknown;
      try {
        parsed = JSON.parse(value);
      } catch (error: unknown) {
        throw new PackageChangesPayloadError(
          error instanceof Error
            ? `Package Activity callback JSON was invalid: ${error.message}`
            : "Package Activity callback JSON was invalid.");
      }
      const event = decodeEngineWorkerPackageChangesEvent(parsed);
      const published = event.kind === "Progress"
        ? context.reportEvents([{
            kind: "progress",
            payload: event.progress,
          }])
        : context.reportEvents([{
            kind: "durable",
            payload: event,
          }]);
      if (!published) {
        throw new Error(
          "Package Activity Worker event publication is closed.");
      }
    },
  });
  return eventSink;
}

export function registerEngineWorkerPackageChangesOperation(
  operations: WorkerOperationCatalog,
  facade: () => EngineWorkerPackageChangesFacade,
): void {
  operations.register({
    kind: engineWorkerPackageChangesKind,
    allowance: { kind: "unbounded" },
    input: engineWorkerPackageChangesInput,
    rejectInvalidPayload: failure => ({
      error: {
        failureKind: "Unexpected",
        error: failure.message,
        diagnostic: failure.message,
      },
      diagnostic: failure.message,
    }),
    invoke: async (input, context) => {
      const result = await facade().runPackageActivity(
        context.operation.operationId,
        input,
        createManagedEventSink(context),
      );
      return mapEngineWorkerPackageChangesResult(result);
    },
    cancel: (operation, reason) =>
      engineWorkerPackageChangesCancellationIsRunning(
        facade().cancelPackageActivity(operation.operationId, reason),
        reason,
      ),
  });
}
