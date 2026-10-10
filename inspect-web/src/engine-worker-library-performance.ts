import type {
  BrowserCompileLibraryAvailability,
  BrowserPackagePerformanceSummary,
  BrowserPerformanceBodyTarget,
  BrowserPerformanceMember,
} from "./facades/inspect-web-analysis.d.ts";
import { mapEngineWorkerBoundaryErrors } from "./engine-worker-contract.ts";
import type {
  WorkerRuntimeOperationRegistration,
  WorkerRuntimePreparationError,
} from "./worker-runtime-core.ts";
import type {
  WorkerOperationCatalog,
  WorkerOperationContext,
} from "./worker-runtime-realm.ts";
import type {
  BoundedPayloadDecodeResult,
  BoundedPayloadDecoder,
  WorkerOperationCancelReason,
} from "./worker-runtime-protocol.ts";
import {
  isWorkerOperationCancelReason,
  type ManagedOperationSettlement,
} from "./worker-runtime-protocol.ts";

export const engineWorkerLibraryPerformanceKind = "library-performance";

const maximumAuxiliaryCharacters = 64 * 1024;
const maximumDiagnosticCharacters = 64 * 1024;
const maximumMemberNameCharacters = 4 * 1024;
const maximumBodyTokens = 4_096;
const maximumShapes = 4_096;
const maximumBodyTargets = 4_096;
const maximumEventCharacters = 1_024 * 1_024;

export interface LibraryPerformanceLoadRequest {
  readonly packageId: string;
  readonly version: string;
  readonly targetFramework: string;
  readonly assemblyName: string;
}

export type EngineWorkerLibraryPerformanceFacade = Pick<
  typeof import("./facades/inspect-web-analysis.d.ts"),
  "queryPackagePerformanceStreaming" | "cancelLibraryPerformanceAnalysis"
>;

export interface EngineWorkerLibraryPerformanceTerminalFailure {
  readonly failureKind: "Expected" | "Unexpected";
  readonly error: string;
  readonly diagnostic: string;
}

type LibraryPerformanceSettlement = ManagedOperationSettlement<
  BrowserPackagePerformanceSummary,
  EngineWorkerLibraryPerformanceTerminalFailure,
  string
>;

class LibraryPerformancePayloadError extends Error {
  readonly reason: "invalid" | "oversized";

  constructor(
    message: string,
    reason: "invalid" | "oversized" = "invalid",
  ) {
    super(message);
    this.reason = reason;
  }
}

function rejected(error: unknown): BoundedPayloadDecodeResult<never> {
  if (error instanceof LibraryPerformancePayloadError) {
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
      : "Library Performance payload validation failed.",
    cause: error,
  };
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}

function expectRecord(
  value: unknown,
  label: string,
): Record<string, unknown> {
  if (!isRecord(value))
    throw new LibraryPerformancePayloadError(`Expected ${label} object.`);
  return value;
}

function expectExactKeys(
  value: Record<string, unknown>,
  keys: readonly string[],
  label: string,
): void {
  const actual = Object.keys(value);
  if (actual.length !== keys.length || !keys.every(key => key in value)) {
    throw new LibraryPerformancePayloadError(
      `${label} has unexpected shape.`);
  }
}

function expectString(
  value: unknown,
  label: string,
  maximum = maximumAuxiliaryCharacters,
): string {
  if (typeof value !== "string") {
    throw new LibraryPerformancePayloadError(`Expected ${label} text.`);
  }
  if (value.length > maximum) {
    throw new LibraryPerformancePayloadError(
      `${label} text exceeds ${maximum} characters.`, "oversized");
  }
  return value;
}

function expectNullableString(
  value: unknown,
  label: string,
  maximum = maximumAuxiliaryCharacters,
): string | null {
  return value === null ? null : expectString(value, label, maximum);
}

function expectLiteral<const TAllowed extends readonly string[]>(
  value: unknown,
  allowed: TAllowed,
  label: string,
): TAllowed[number] {
  if (typeof value !== "string" || !allowed.includes(value)) {
    throw new LibraryPerformancePayloadError(
      `Expected ${label} to be one of ${allowed.join(", ")}.`);
  }
  return value;
}

function expectNumber(value: unknown, label: string): number {
  if (typeof value !== "number" || !Number.isFinite(value)) {
    throw new LibraryPerformancePayloadError(`Expected ${label} number.`);
  }
  return value;
}

function expectNumberArray(
  value: unknown,
  label: string,
  maximum: number,
): number[] {
  if (!Array.isArray(value) || value.length > maximum) {
    throw new LibraryPerformancePayloadError(
      `Expected ${label} array of at most ${maximum} numbers.`);
  }
  return value.map((entry, index) =>
    expectNumber(entry, `${label}[${index}]`));
}

function expectNullableNumberArray(
  value: unknown,
  label: string,
  maximum: number,
): number[] | null {
  return value === null ? null : expectNumberArray(value, label, maximum);
}

function expectStringArray(
  value: unknown,
  label: string,
  maximum: number,
  itemMaximum = maximumAuxiliaryCharacters,
): string[] {
  if (!Array.isArray(value) || value.length > maximum) {
    throw new LibraryPerformancePayloadError(
      `Expected ${label} array of at most ${maximum} strings.`);
  }
  return value.map((entry, index) =>
    expectString(entry, `${label}[${index}]`, itemMaximum));
}

function compileLibrary(
  value: unknown,
): BrowserCompileLibraryAvailability {
  const record = expectRecord(value, "Library Performance compile library");
  expectExactKeys(
    record,
    ["status", "targetFramework", "message"],
    "Library Performance compile library");
  return {
    status: expectLiteral(
      record.status,
      [
        "Selected",
        "NoCompileAssets",
        "NoMatchingTargetFramework",
        "EmptyCompileGroup",
        "InvalidImplementationAssets",
      ],
      "compile library status"),
    targetFramework: expectNullableString(
      record.targetFramework, "compile library target framework", 256),
    message: expectNullableString(
      record.message, "compile library message"),
  };
}

function bodyTarget(value: unknown): BrowserPerformanceBodyTarget {
  const record = expectRecord(value, "Library Performance body target");
  expectExactKeys(
    record,
    ["typeId", "memberName", "selectorKey", "methodToken", "issueOffsets"],
    "Library Performance body target");
  return {
    typeId: expectString(record.typeId, "body target type id"),
    memberName: expectString(
      record.memberName, "body target member name", maximumMemberNameCharacters),
    selectorKey: expectString(record.selectorKey, "body target selector key"),
    methodToken: expectNumber(record.methodToken, "body target method token"),
    issueOffsets: expectNullableNumberArray(
      record.issueOffsets, "body target issue offsets", maximumBodyTokens),
  };
}

function member(value: unknown): BrowserPerformanceMember {
  const record = expectRecord(value, "Library Performance member");
  expectExactKeys(
    record,
    [
      "assembly",
      "typeId",
      "memberName",
      "stableSelector",
      "bodyTokens",
      "opportunityCount",
      "inLoopCount",
      "shapes",
      "confidence",
      "bodyTargets",
    ],
    "Library Performance member");
  const bodyTargets = record.bodyTargets === null
    ? null
    : (() => {
      if (!Array.isArray(record.bodyTargets)
        || record.bodyTargets.length > maximumBodyTargets) {
        throw new LibraryPerformancePayloadError(
          "Library Performance member has invalid body targets.");
      }
      return record.bodyTargets.map(bodyTarget);
    })();
  return {
    assembly: expectString(record.assembly, "member assembly"),
    typeId: expectString(record.typeId, "member type id"),
    memberName: expectString(
      record.memberName, "member name", maximumMemberNameCharacters),
    stableSelector: expectString(record.stableSelector, "member selector"),
    bodyTokens: expectNumberArray(
      record.bodyTokens, "member body tokens", maximumBodyTokens),
    opportunityCount: expectNumber(
      record.opportunityCount, "member opportunity count"),
    inLoopCount: expectNumber(record.inLoopCount, "member in-loop count"),
    shapes: expectStringArray(record.shapes, "member shapes", maximumShapes, 256),
    confidence: expectString(record.confidence, "member confidence", 64),
    bodyTargets,
  };
}

function assertSummary(
  value: unknown,
): BrowserPackagePerformanceSummary {
  const record = expectRecord(value, "Library Performance summary");
  expectExactKeys(
    record,
    [
      "inspectionError",
      "nonPublicOpportunities",
      "totalOpportunities",
      "compileLibrary",
    ],
    "Library Performance summary");
  return {
    inspectionError: expectNullableString(
      record.inspectionError, "summary inspection error",
      maximumDiagnosticCharacters),
    nonPublicOpportunities: expectNumber(
      record.nonPublicOpportunities, "summary non-public opportunities"),
    totalOpportunities: expectNumber(
      record.totalOpportunities, "summary total opportunities"),
    compileLibrary: compileLibrary(record.compileLibrary),
  };
}

export const engineWorkerLibraryPerformanceInput:
BoundedPayloadDecoder<LibraryPerformanceLoadRequest> = {
  decode(value) {
    try {
      const record = expectRecord(value, "Library Performance request");
      expectExactKeys(
        record,
        ["packageId", "version", "targetFramework", "assemblyName"],
        "Library Performance request");
      return {
        kind: "decoded",
        value: {
          packageId: expectString(record.packageId, "request package id"),
          version: expectString(record.version, "request version", 256),
          targetFramework: expectString(
            record.targetFramework, "request target framework", 256),
          assemblyName: expectString(
            record.assemblyName, "request assembly name"),
        },
      };
    } catch (error: unknown) {
      return rejected(error);
    }
  },
};

export type EngineWorkerLibraryPerformanceDurableEvent = {
  readonly kind: "Item";
  readonly item: BrowserPerformanceMember;
};

export const engineWorkerLibraryPerformanceDurableEvent:
BoundedPayloadDecoder<EngineWorkerLibraryPerformanceDurableEvent> = {
  decode(value) {
    try {
      const record = expectRecord(value, "Library Performance event");
      expectExactKeys(record, ["kind", "item"], "Library Performance event");
      expectLiteral(record.kind, ["Item"], "event kind");
      return {
        kind: "decoded",
        value: { kind: "Item", item: member(record.item) },
      };
    } catch (error: unknown) {
      return rejected(error);
    }
  },
};

export const engineWorkerLibraryPerformanceProgress:
BoundedPayloadDecoder<never> = {
  decode() {
    return rejected(new LibraryPerformancePayloadError(
      "Library Performance does not publish progress payloads."));
  },
};

const libraryPerformanceText: BoundedPayloadDecoder<string> = {
  decode(value) {
    try {
      return {
        kind: "decoded",
        value: expectString(value, "Library Performance diagnostic",
          maximumDiagnosticCharacters),
      };
    } catch (error: unknown) {
      return rejected(error);
    }
  },
};

const libraryPerformanceSummaryDecoder:
BoundedPayloadDecoder<BrowserPackagePerformanceSummary> = {
  decode(value) {
    try {
      return { kind: "decoded", value: assertSummary(value) };
    } catch (error: unknown) {
      return rejected(error);
    }
  },
};

const libraryPerformanceFailure:
BoundedPayloadDecoder<EngineWorkerLibraryPerformanceTerminalFailure> = {
  decode(value) {
    try {
      const record = expectRecord(value, "Library Performance failure");
      expectExactKeys(
        record,
        ["failureKind", "error", "diagnostic"],
        "Library Performance failure");
      return {
        kind: "decoded",
        value: {
          failureKind: expectLiteral(
            record.failureKind, ["Expected", "Unexpected"], "failure kind"),
          error: expectString(
            record.error, "failure error", maximumDiagnosticCharacters),
          diagnostic: expectString(
            record.diagnostic, "failure diagnostic",
            maximumDiagnosticCharacters),
        },
      };
    } catch (error: unknown) {
      return rejected(error);
    }
  },
};

function invalidResult(error: unknown): LibraryPerformanceSettlement {
  const diagnostic = error instanceof Error
    ? error.message.slice(0, maximumDiagnosticCharacters)
    : "Library Performance result validation failed.";
  return {
    kind: "failed",
    failureKind: "unexpected",
    error: {
      failureKind: "Unexpected",
      error: "Library Performance returned invalid Worker boundary data.",
      diagnostic,
    },
    diagnostic,
  };
}

export function mapEngineWorkerLibraryPerformanceResult(
  value: unknown,
): LibraryPerformanceSettlement {
  try {
    const record = expectRecord(value, "Library Performance result");
    expectExactKeys(
      record,
      [
        "version",
        "kind",
        "summary",
        "failureKind",
        "error",
        "diagnostic",
        "reason",
      ],
      "Library Performance result");
    if (record.version !== 1) {
      throw new LibraryPerformancePayloadError(
        "Expected a version 1 Library Performance result.");
    }
    const kind = expectLiteral(
      record.kind, ["Succeeded", "Failed", "Canceled"], "result kind");
    if (kind === "Succeeded") {
      if (record.summary === null
        || record.failureKind !== null
        || record.error !== null
        || record.diagnostic !== null
        || record.reason !== null) {
        throw new LibraryPerformancePayloadError(
          "Library Performance success has invalid terminal fields.");
      }
      return { kind: "succeeded", value: assertSummary(record.summary) };
    }
    if (kind === "Failed") {
      if (record.summary !== null
        || (record.failureKind !== "Expected"
          && record.failureKind !== "Unexpected")
        || record.error === null
        || record.diagnostic === null
        || record.reason !== null) {
        throw new LibraryPerformancePayloadError(
          "Library Performance failure has invalid terminal fields.");
      }
      return {
        kind: "failed",
        failureKind: record.failureKind === "Expected"
          ? "expected"
          : "unexpected",
        error: {
          failureKind: record.failureKind,
          error: expectString(
            record.error, "result error", maximumDiagnosticCharacters),
          diagnostic: expectString(
            record.diagnostic, "result diagnostic",
            maximumDiagnosticCharacters),
        },
        diagnostic: expectString(
          record.diagnostic, "result diagnostic",
          maximumDiagnosticCharacters),
      };
    }
    if (record.summary !== null
      || record.failureKind !== null
      || record.error !== null
      || record.diagnostic !== null
      || record.reason === null
      || typeof record.reason !== "string"
      || !isWorkerOperationCancelReason(record.reason)) {
      throw new LibraryPerformancePayloadError(
        "Library Performance cancellation has invalid terminal fields.");
    }
    return { kind: "canceled", reason: record.reason };
  } catch (error: unknown) {
    return invalidResult(error);
  }
}

export function engineWorkerLibraryPerformanceCancellationIsRunning(
  value: unknown,
  requestedReason: WorkerOperationCancelReason,
): boolean {
  const record = expectRecord(value, "Library Performance cancellation");
  expectExactKeys(record, ["kind", "reason"], "Library Performance cancellation");
  const kind = expectLiteral(
    record.kind,
    ["Requested", "AlreadyRequested", "NotActive"],
    "cancellation kind");
  if (kind === "NotActive") {
    if (record.reason !== null) {
      throw new LibraryPerformancePayloadError(
        "Inactive Library Performance cancellation has a reason.");
    }
    return false;
  }
  if (record.reason === null
    || typeof record.reason !== "string"
    || !isWorkerOperationCancelReason(record.reason)) {
    throw new LibraryPerformancePayloadError(
      "Library Performance cancellation reason is invalid.");
  }
  if (kind === "Requested" && record.reason !== requestedReason) {
    throw new LibraryPerformancePayloadError(
      "Library Performance cancellation changed the requested reason.");
  }
  return true;
}

const libraryPerformanceBoundaryErrors =
  mapEngineWorkerBoundaryErrors(
    (message): EngineWorkerLibraryPerformanceTerminalFailure => ({
      failureKind: "Unexpected",
      error: message,
      diagnostic: message,
    }),
  );

export function createEngineWorkerLibraryPerformanceHostRegistration():
WorkerRuntimeOperationRegistration<
  LibraryPerformanceLoadRequest,
  BrowserPackagePerformanceSummary,
  EngineWorkerLibraryPerformanceTerminalFailure,
  string,
  never,
  WorkerRuntimePreparationError,
  EngineWorkerLibraryPerformanceDurableEvent
> {
  return {
    kind: engineWorkerLibraryPerformanceKind,
    allowance: { kind: "unbounded" },
    encodeInput: value => engineWorkerLibraryPerformanceInput.decode(value),
    value: libraryPerformanceSummaryDecoder,
    error: libraryPerformanceFailure,
    diagnostic: libraryPerformanceText,
    progress: engineWorkerLibraryPerformanceProgress,
    durable: engineWorkerLibraryPerformanceDurableEvent,
    mapPreparationError: error => error,
    boundaryErrors: libraryPerformanceBoundaryErrors,
  };
}

function createManagedEventSink(
  context: WorkerOperationContext,
): Record<string, unknown> {
  const eventSink: Record<string, unknown> = {};
  Object.defineProperty(eventSink, "event", {
    set(value: unknown) {
      if (typeof value !== "string") {
        throw new LibraryPerformancePayloadError(
          "Library Performance callback payload was not JSON text.");
      }
      if (value.length > maximumEventCharacters) {
        throw new LibraryPerformancePayloadError(
          "Library Performance callback exceeds its wire budget.",
          "oversized",
        );
      }
      let parsed: unknown;
      try {
        parsed = JSON.parse(value);
      } catch (error: unknown) {
        throw new LibraryPerformancePayloadError(
          error instanceof Error
            ? `Library Performance callback JSON was invalid: ${error.message}`
            : "Library Performance callback JSON was invalid.");
      }
      const decoded = engineWorkerLibraryPerformanceDurableEvent.decode(
        parsed);
      if (decoded.kind === "rejected") {
        throw new LibraryPerformancePayloadError(
          decoded.message, decoded.reason);
      }
      const published = context.reportEvents([{
        kind: "durable",
        payload: decoded.value,
      }]);
      if (!published) {
        throw new Error(
          "Library Performance Worker event publication is closed.");
      }
    },
  });
  return eventSink;
}

export function registerEngineWorkerLibraryPerformanceOperation(
  operations: WorkerOperationCatalog,
  facade: () => EngineWorkerLibraryPerformanceFacade,
): void {
  operations.register({
    kind: engineWorkerLibraryPerformanceKind,
    allowance: { kind: "unbounded" },
    input: engineWorkerLibraryPerformanceInput,
    rejectInvalidPayload: failure => ({
      error: {
        failureKind: "Unexpected" as const,
        error:
          "Library Performance request was rejected at the Worker boundary.",
        diagnostic: failure.message,
      },
      diagnostic: failure.message,
    }),
    invoke: async (input, context) => {
      const result = await facade().queryPackagePerformanceStreaming(
        context.operation.operationId,
        input.packageId,
        input.version,
        input.targetFramework,
        input.assemblyName,
        createManagedEventSink(context),
      );
      return mapEngineWorkerLibraryPerformanceResult(result);
    },
    cancel: (operation, reason) =>
      engineWorkerLibraryPerformanceCancellationIsRunning(
        facade().cancelLibraryPerformanceAnalysis(
          operation.operationId, reason),
        reason,
      ),
  });
}
