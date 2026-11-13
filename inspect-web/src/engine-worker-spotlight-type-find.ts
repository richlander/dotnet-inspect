import type {
  BrowserSpotlightTypeFindCancellation,
  BrowserSpotlightTypeFindCandidate,
  BrowserSpotlightTypeFindInspection,
  BrowserSpotlightTypeFindResult,
} from "./facades/inspect-web-catalog.d.ts";
import { decodeEngineWorkerJsonValue } from "./engine-worker-ordinary.ts";
import { mapEngineWorkerBoundaryErrors } from "./engine-worker-contract.ts";
import type {
  WorkerRuntimeOperationRegistration,
  WorkerRuntimePreparationError,
} from "./worker-runtime-core.ts";
import type {
  WorkerOperationCatalog,
  WorkerOperationContext,
} from "./worker-runtime-realm.ts";
import {
  isWorkerOperationCancelReason,
  type BoundedPayloadDecodeResult,
  type BoundedPayloadDecoder,
  type ManagedOperationSettlement,
  type WorkerOperationCancelReason,
} from "./worker-runtime-protocol.ts";

const engineWorkerSpotlightTypeFindKind = "spotlight-type-find";
const maximumTextCharacters = 64 * 1024;
const maximumDiagnosticCharacters = 64 * 1024;

export interface SpotlightTypeFindRequest {
  readonly retainedDefinitionId: string;
  readonly realizationId: string;
  readonly resultGeneration: number;
  readonly text: string;
  readonly maximumRows: number;
}

export interface SpotlightTypeFindEvent {
  readonly kind: "Finding";
  readonly candidate: BrowserSpotlightTypeFindCandidate;
}

export interface EngineWorkerSpotlightTypeFindFailure {
  readonly failureKind: "Expected" | "Unexpected";
  readonly error: string;
  readonly diagnostic: string;
}

export interface EngineWorkerSpotlightTypeFindFacade {
  findSpotlightTypes(
    operationId: string,
    retainedDefinitionId: string,
    realizationId: string,
    resultGeneration: number,
    text: string,
    maximumRows: number,
    eventSink: unknown,
  ): Promise<BrowserSpotlightTypeFindResult>;
  cancelSpotlightTypeFind(
    operationId: string,
    reason: string,
  ): BrowserSpotlightTypeFindCancellation;
}

type SpotlightTypeFindSettlement = ManagedOperationSettlement<
  BrowserSpotlightTypeFindInspection,
  EngineWorkerSpotlightTypeFindFailure,
  string
>;

function rejected<T = never>(
  message: string,
  reason: "invalid" | "oversized" = "invalid",
): BoundedPayloadDecodeResult<T> {
  return { kind: "rejected", reason, message };
}

function dataRecord(value: unknown): object | null {
  return typeof value === "object"
      && value !== null
      && !Array.isArray(value)
    ? value
    : null;
}

function ownData(candidate: object, name: string): unknown {
  const property = Object.getOwnPropertyDescriptor(candidate, name);
  return property !== undefined && "value" in property
    ? property.value
    : undefined;
}

function hasExactData(candidate: object, names: readonly string[]): boolean {
  const keys = Reflect.ownKeys(candidate);
  return keys.length === names.length
    && names.every(name => {
      const property = Object.getOwnPropertyDescriptor(candidate, name);
      return property !== undefined && "value" in property;
    });
}

function boundedString(
  value: unknown,
  label: string,
  maximum = maximumDiagnosticCharacters,
): BoundedPayloadDecodeResult<string> {
  if (typeof value !== "string") return rejected(`Expected ${label} text.`);
  if (value.length > maximum) {
    return rejected(
      `${label} text exceeds ${maximum} characters.`,
      "oversized",
    );
  }
  return { kind: "decoded", value };
}

function nullableString(
  value: unknown,
  label: string,
): BoundedPayloadDecodeResult<string | null> {
  return value === null
    ? { kind: "decoded", value: null }
    : boundedString(value, label);
}

function positiveInt(value: unknown): number | null {
  return typeof value === "number"
      && Number.isInteger(value)
      && value > 0
      && value <= 0x7fffffff
    ? value
    : null;
}

export const engineWorkerSpotlightTypeFindInput:
BoundedPayloadDecoder<SpotlightTypeFindRequest> = {
  decode(value) {
    const candidate = dataRecord(value);
    if (candidate === null || !hasExactData(candidate, [
      "retainedDefinitionId",
      "realizationId",
      "resultGeneration",
      "text",
      "maximumRows",
    ])) {
      return rejected("Expected a Spotlight Type Find request.");
    }
    const retainedDefinitionId = boundedString(
      ownData(candidate, "retainedDefinitionId"),
      "Spotlight Type Find retained definition",
    );
    if (retainedDefinitionId.kind === "rejected")
      return retainedDefinitionId;
    const realizationId = boundedString(
      ownData(candidate, "realizationId"),
      "Spotlight Type Find realization",
    );
    if (realizationId.kind === "rejected") return realizationId;
    const text = boundedString(
      ownData(candidate, "text"),
      "Spotlight Type Find query",
      maximumTextCharacters,
    );
    if (text.kind === "rejected") return text;
    const resultGeneration = positiveInt(
      ownData(candidate, "resultGeneration"),
    );
    const maximumRows = positiveInt(
      ownData(candidate, "maximumRows"),
    );
    if (resultGeneration === null)
      return rejected("Spotlight Type Find result generation is invalid.");
    if (maximumRows === null)
      return rejected("Spotlight Type Find maximum rows is invalid.");
    return {
      kind: "decoded",
      value: {
        retainedDefinitionId: retainedDefinitionId.value,
        realizationId: realizationId.value,
        resultGeneration,
        text: text.value,
        maximumRows,
      },
    };
  },
};

const spotlightTypeFindValue:
BoundedPayloadDecoder<BrowserSpotlightTypeFindInspection> = {
  decode(value) {
    return decodeEngineWorkerJsonValue<BrowserSpotlightTypeFindInspection>(
      value,
    );
  },
};

const spotlightTypeFindFailure:
BoundedPayloadDecoder<EngineWorkerSpotlightTypeFindFailure> = {
  decode(value) {
    const candidate = dataRecord(value);
    if (candidate === null || !hasExactData(candidate, [
      "failureKind",
      "error",
      "diagnostic",
    ])) {
      return rejected("Expected a Spotlight Type Find failure.");
    }
    const failureKind = ownData(candidate, "failureKind");
    if (failureKind !== "Expected" && failureKind !== "Unexpected")
      return rejected("Spotlight Type Find failure kind is invalid.");
    const error = boundedString(
      ownData(candidate, "error"),
      "Spotlight Type Find error",
    );
    if (error.kind === "rejected") return error;
    const diagnostic = boundedString(
      ownData(candidate, "diagnostic"),
      "Spotlight Type Find diagnostic",
    );
    if (diagnostic.kind === "rejected") return diagnostic;
    return {
      kind: "decoded",
      value: {
        failureKind,
        error: error.value,
        diagnostic: diagnostic.value,
      },
    };
  },
};

const spotlightTypeFindEvent:
BoundedPayloadDecoder<SpotlightTypeFindEvent> = {
  decode(value) {
    const candidate = dataRecord(value);
    if (candidate === null
      || !hasExactData(candidate, ["kind", "candidate"])
      || ownData(candidate, "kind") !== "Finding") {
      return rejected("Expected a Spotlight Type Find finding event.");
    }
    const finding = spotlightTypeFindCandidate(
      ownData(candidate, "candidate"),
    );
    if (finding.kind === "rejected") return finding;
    return {
      kind: "decoded",
      value: { kind: "Finding", candidate: finding.value },
    };
  },
};

function spotlightTypeFindCandidate(
  value: unknown,
): BoundedPayloadDecodeResult<BrowserSpotlightTypeFindCandidate> {
  const candidate = dataRecord(value);
  if (candidate === null || !hasExactData(candidate, [
    "identity",
    "action",
    "reason",
    "name",
    "namespace",
    "library",
    "source",
    "declarationKind",
  ])) {
    return rejected("Expected a Spotlight Type Find candidate.");
  }
  const identity = boundedString(
    ownData(candidate, "identity"),
    "Spotlight Type Find candidate identity",
  );
  if (identity.kind === "rejected") return identity;
  const action = nullableString(
    ownData(candidate, "action"),
    "Spotlight Type Find candidate action",
  );
  if (action.kind === "rejected") return action;
  const reason = nullableString(
    ownData(candidate, "reason"),
    "Spotlight Type Find candidate reason",
  );
  if (reason.kind === "rejected") return reason;
  const name = boundedString(
    ownData(candidate, "name"),
    "Spotlight Type Find candidate name",
  );
  if (name.kind === "rejected") return name;
  const namespace = boundedString(
    ownData(candidate, "namespace"),
    "Spotlight Type Find candidate namespace",
  );
  if (namespace.kind === "rejected") return namespace;
  const library = boundedString(
    ownData(candidate, "library"),
    "Spotlight Type Find candidate library",
  );
  if (library.kind === "rejected") return library;
  const source = boundedString(
    ownData(candidate, "source"),
    "Spotlight Type Find candidate source",
  );
  if (source.kind === "rejected") return source;
  const declarationKind = boundedString(
    ownData(candidate, "declarationKind"),
    "Spotlight Type Find candidate declaration kind",
  );
  if (declarationKind.kind === "rejected") return declarationKind;
  return {
    kind: "decoded",
    value: {
      identity: identity.value,
      action: action.value,
      reason: reason.value,
      name: name.value,
      namespace: namespace.value,
      library: library.value,
      source: source.value,
      declarationKind: declarationKind.value,
    },
  };
}

const spotlightTypeFindDiagnostic = {
  decode(value: unknown) {
    return boundedString(value, "Spotlight Type Find diagnostic");
  },
} satisfies BoundedPayloadDecoder<string>;

const spotlightTypeFindBoundaryErrors =
  mapEngineWorkerBoundaryErrors(message => ({
    failureKind: "Unexpected" as const,
    error: message,
    diagnostic: message,
  }));

const noProgress: BoundedPayloadDecoder<never> = {
  decode() {
    return rejected("Spotlight Type Find does not publish progress payloads.");
  },
};

function invalidResultFailure(
  error: unknown,
): SpotlightTypeFindSettlement {
  const diagnostic = error instanceof Error
    ? error.message.slice(0, maximumDiagnosticCharacters)
    : "Spotlight Type Find result validation failed.";
  return {
    kind: "failed",
    failureKind: "unexpected",
    error: {
      failureKind: "Unexpected",
      error: "Spotlight Type Find returned invalid Worker boundary data.",
      diagnostic,
    },
    diagnostic,
  };
}

function mapSpotlightTypeFindResult(
  result: BrowserSpotlightTypeFindResult,
): SpotlightTypeFindSettlement {
  try {
    if (result.kind === "Succeeded") {
      if (result.inspection === null
        || result.failureKind !== null
        || result.error !== null
        || result.diagnostic !== null
        || result.reason !== null) {
        throw new Error("Spotlight Type Find success is malformed.");
      }
      return { kind: "succeeded", value: result.inspection };
    }
    if (result.kind === "Failed") {
      if (result.inspection !== null
        || (result.failureKind !== "Expected"
          && result.failureKind !== "Unexpected")
        || typeof result.error !== "string"
        || typeof result.diagnostic !== "string"
        || result.reason !== null) {
        throw new Error("Spotlight Type Find failure is malformed.");
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
    if (result.kind === "Canceled") {
      if (result.inspection !== null
        || result.failureKind !== null
        || result.error !== null
        || result.diagnostic !== null
        || typeof result.reason !== "string"
        || !isWorkerOperationCancelReason(result.reason)) {
        throw new Error("Spotlight Type Find cancellation is malformed.");
      }
      return { kind: "canceled", reason: result.reason };
    }
    throw new Error(
      `Unsupported Spotlight Type Find result kind '${
        String(result.kind)
      }'.`,
    );
  } catch (error: unknown) {
    return invalidResultFailure(error);
  }
}

function cancellationIsRunning(
  result: BrowserSpotlightTypeFindCancellation,
  requestedReason: WorkerOperationCancelReason,
): boolean {
  if (result.status === "NotActive") {
    if (result.reason !== null) {
      throw new Error(
        "Inactive Spotlight Type Find cancellation has a reason.",
      );
    }
    return false;
  }
  if (typeof result.reason !== "string"
    || !isWorkerOperationCancelReason(result.reason)) {
    throw new Error("Spotlight Type Find cancellation reason is invalid.");
  }
  if (result.status === "Requested") {
    if (result.reason !== requestedReason) {
      throw new Error(
        "Spotlight Type Find cancellation changed the requested reason.",
      );
    }
    return true;
  }
  if (result.status === "AlreadyRequested") return true;
  throw new Error("Spotlight Type Find cancellation status is invalid.");
}

export function createEngineWorkerSpotlightTypeFindHostRegistration():
WorkerRuntimeOperationRegistration<
  SpotlightTypeFindRequest,
  BrowserSpotlightTypeFindInspection,
  EngineWorkerSpotlightTypeFindFailure,
  string,
  never,
  WorkerRuntimePreparationError,
  SpotlightTypeFindEvent
> {
  return {
    kind: engineWorkerSpotlightTypeFindKind,
    allowance: { kind: "unbounded" },
    encodeInput: engineWorkerSpotlightTypeFindInput.decode,
    value: spotlightTypeFindValue,
    error: spotlightTypeFindFailure,
    diagnostic: spotlightTypeFindDiagnostic,
    progress: noProgress,
    durable: spotlightTypeFindEvent,
    mapPreparationError: error => error,
    boundaryErrors: spotlightTypeFindBoundaryErrors,
  };
}

function createEventSink(
  context: WorkerOperationContext,
): Record<string, unknown> {
  const eventSink: Record<string, unknown> = {};
  Object.defineProperty(eventSink, "event", {
    set(value: unknown) {
      if (typeof value !== "string") {
        throw new Error(
          "Spotlight Type Find callback payload was not JSON text.",
        );
      }
      const decoded = spotlightTypeFindEvent.decode(JSON.parse(value));
      if (decoded.kind === "rejected") throw new Error(decoded.message);
      if (!context.reportEvents([{
        kind: "durable",
        payload: decoded.value,
      }])) {
        throw new Error(
          "Spotlight Type Find Worker event publication is closed.",
        );
      }
    },
  });
  return eventSink;
}

export function registerEngineWorkerSpotlightTypeFindOperation(
  operations: WorkerOperationCatalog,
  facade: () => EngineWorkerSpotlightTypeFindFacade,
): void {
  operations.register({
    kind: engineWorkerSpotlightTypeFindKind,
    allowance: { kind: "unbounded" },
    input: engineWorkerSpotlightTypeFindInput,
    rejectInvalidPayload: failure => ({
      error: {
        failureKind: "Unexpected",
        error: failure.message,
        diagnostic: failure.message,
      },
      diagnostic: failure.message,
    }),
    invoke: async (input, context) =>
      mapSpotlightTypeFindResult(
        await facade().findSpotlightTypes(
          context.operation.operationId,
          input.retainedDefinitionId,
          input.realizationId,
          input.resultGeneration,
          input.text,
          input.maximumRows,
          createEventSink(context),
        ),
      ),
    cancel: (operation, reason) =>
      cancellationIsRunning(
        facade().cancelSpotlightTypeFind(operation.operationId, reason),
        reason,
      ),
  });
}
