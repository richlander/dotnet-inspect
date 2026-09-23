import type {
  BrowserTypeExplorerAccessibility,
  BrowserTypeExplorerBodyMode,
  BrowserTypeExplorerInspection,
  BrowserTypeExplorerMemberIdentity,
  BrowserTypeExplorerPlacement,
  BrowserTypeExplorerRequest,
  BrowserTypeExplorerResult,
  BrowserTypeSourceCancellation,
} from "./facades/inspect-web-source.d.ts";
import { decodeEngineWorkerJsonValue } from "./engine-worker-ordinary.ts";
import { mapEngineWorkerBoundaryErrors } from "./engine-worker-contract.ts";
import type {
  WorkerRuntimeOperationRegistration,
  WorkerRuntimePreparationError,
} from "./worker-runtime-core.ts";
import type { WorkerOperationCatalog } from "./worker-runtime-realm.ts";
import type {
  BoundedPayloadDecodeResult,
  BoundedPayloadDecoder,
  WorkerOperationCancelReason,
} from "./worker-runtime-protocol.ts";
import {
  isWorkerOperationCancelReason,
  type ManagedOperationSettlement,
} from "./worker-runtime-protocol.ts";

export const engineWorkerTypeExplorerKind = "type-explorer";

const maximumRequestCharacters = 96 * 1024;
const maximumAuxiliaryCharacters = 64 * 1024;

export interface TypeExplorerLoadRequest {
  readonly packageId: string;
  readonly version: string;
  readonly framework: string;
  readonly assembly: string;
  readonly type: string;
  readonly taste: string;
  readonly projection: BrowserTypeExplorerRequest;
}

export interface EngineWorkerTypeExplorerFacade {
  queryTypeExplorer(
    operationId: string,
    packageId: string,
    version: string,
    targetFramework: string,
    assemblyName: string,
    typeIdentity: string,
    styleOptionsJson: string,
    requestJson: BrowserTypeExplorerRequest,
  ): Promise<BrowserTypeExplorerResult>;
  cancelTypeExplorerQuery(
    operationId: string,
    reason: string,
  ): BrowserTypeSourceCancellation;
}

export interface EngineWorkerTypeExplorerFailure {
  readonly failureKind: "Expected" | "Unexpected";
  readonly error: string;
  readonly diagnostic: string;
}

type TypeExplorerSettlement = ManagedOperationSettlement<
  BrowserTypeExplorerInspection,
  EngineWorkerTypeExplorerFailure,
  string
>;

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

function hasExactData(
  candidate: object,
  names: readonly string[],
): boolean {
  const keys = Reflect.ownKeys(candidate);
  return keys.length === names.length
    && names.every(name => {
      const property = Object.getOwnPropertyDescriptor(candidate, name);
      return property !== undefined && "value" in property;
    });
}

function rejected(
  message: string,
  reason: "invalid" | "oversized" = "invalid",
): BoundedPayloadDecodeResult<never> {
  return { kind: "rejected", reason, message };
}

function boundedString(
  value: unknown,
  label: string,
  maximum = maximumAuxiliaryCharacters,
): BoundedPayloadDecodeResult<string> {
  if (typeof value !== "string")
    return rejected(`Expected ${label} text.`);
  if (value.length > maximum) {
    return rejected(
      `${label} text exceeds ${maximum} characters.`,
      "oversized");
  }
  return { kind: "decoded", value };
}

function memberIdentity(
  value: unknown,
): BoundedPayloadDecodeResult<BrowserTypeExplorerMemberIdentity | null> {
  if (value === null) return { kind: "decoded", value: null };
  const candidate = dataRecord(value);
  if (candidate === null || !hasExactData(candidate, [
    "stableSelector",
    "canonicalSignature",
    "fingerprint",
    "typeFullName",
    "memberName",
  ])) {
    return rejected("Expected a complete Type Explorer member identity.");
  }
  const stableSelector = boundedString(
    ownData(candidate, "stableSelector"),
    "member stable selector",
    16 * 1024);
  if (stableSelector.kind === "rejected") return stableSelector;
  const canonicalSignature = boundedString(
    ownData(candidate, "canonicalSignature"),
    "member canonical signature",
    16 * 1024);
  if (canonicalSignature.kind === "rejected") return canonicalSignature;
  const fingerprint = boundedString(
    ownData(candidate, "fingerprint"),
    "member fingerprint",
    10);
  if (fingerprint.kind === "rejected") return fingerprint;
  const typeFullName = boundedString(
    ownData(candidate, "typeFullName"),
    "member Type name",
    16 * 1024);
  if (typeFullName.kind === "rejected") return typeFullName;
  const memberName = boundedString(
    ownData(candidate, "memberName"),
    "member name",
    16 * 1024);
  if (memberName.kind === "rejected") return memberName;
  if (!/^[0-9a-f]{10}$/iu.test(fingerprint.value))
    return rejected("Type Explorer member fingerprint is invalid.");
  return {
    kind: "decoded",
    value: {
      stableSelector: stableSelector.value,
      canonicalSignature: canonicalSignature.value,
      fingerprint: fingerprint.value,
      typeFullName: typeFullName.value,
      memberName: memberName.value,
    },
  };
}

function bodyMode(value: unknown): BrowserTypeExplorerBodyMode | null {
  return value === "Bodies"
    || value === "Skeleton"
    || value === "SelectedBody"
    ? value
    : null;
}

function placement(value: unknown): BrowserTypeExplorerPlacement | null {
  return value === "All" || value === "Instance" || value === "Static"
    ? value
    : null;
}

function accessibility(
  value: unknown,
): BrowserTypeExplorerAccessibility | null {
  return value === "Unknown"
    || value === "Private"
    || value === "PrivateProtected"
    || value === "Protected"
    || value === "Internal"
    || value === "ProtectedInternal"
    || value === "Public"
    ? value
    : null;
}

export const engineWorkerTypeExplorerRequest:
BoundedPayloadDecoder<BrowserTypeExplorerRequest> = {
  decode(value) {
    const candidate = dataRecord(value);
    if (candidate === null || !hasExactData(candidate, [
      "bodyMode",
      "selectedMember",
      "placement",
      "accessibilities",
      "includeGenerated",
      "includeDocumentation",
      "includeAttributes",
    ])) {
      return rejected("Expected a Type Explorer projection request.");
    }
    const selectedBodyMode = bodyMode(ownData(candidate, "bodyMode"));
    const selectedPlacement = placement(ownData(candidate, "placement"));
    const selectedMember = memberIdentity(
      ownData(candidate, "selectedMember"));
    if (selectedBodyMode === null)
      return rejected("Type Explorer body mode is invalid.");
    if (selectedPlacement === null)
      return rejected("Type Explorer placement is invalid.");
    if (selectedMember.kind === "rejected") return selectedMember;
    const rawAccessibilities = ownData(candidate, "accessibilities");
    if (!Array.isArray(rawAccessibilities) || rawAccessibilities.length > 7)
      return rejected("Type Explorer accessibilities are invalid.");
    const accessibilities: BrowserTypeExplorerAccessibility[] = [];
    for (const rawAccessibility of rawAccessibilities) {
      const decodedAccessibility = accessibility(rawAccessibility);
      if (decodedAccessibility === null
        || accessibilities.includes(decodedAccessibility)) {
        return rejected("Type Explorer accessibilities are invalid.");
      }
      accessibilities.push(decodedAccessibility);
    }
    const includeGenerated = ownData(candidate, "includeGenerated");
    const includeDocumentation =
      ownData(candidate, "includeDocumentation");
    const includeAttributes = ownData(candidate, "includeAttributes");
    if (typeof includeGenerated !== "boolean"
      || typeof includeDocumentation !== "boolean"
      || typeof includeAttributes !== "boolean") {
      return rejected("Type Explorer inclusion controls are invalid.");
    }
    return {
      kind: "decoded",
      value: {
        bodyMode: selectedBodyMode,
        selectedMember: selectedMember.value,
        placement: selectedPlacement,
        accessibilities,
        includeGenerated,
        includeDocumentation,
        includeAttributes,
      },
    };
  },
};

export const engineWorkerTypeExplorerInput:
BoundedPayloadDecoder<TypeExplorerLoadRequest> = {
  decode(value) {
    const candidate = dataRecord(value);
    if (candidate === null || !hasExactData(candidate, [
      "packageId",
      "version",
      "framework",
      "assembly",
      "type",
      "taste",
      "projection",
    ])) {
      return rejected("Expected a Type Explorer request.");
    }
    const packageId = boundedString(
      ownData(candidate, "packageId"),
      "Type Explorer packageId",
      maximumRequestCharacters);
    if (packageId.kind === "rejected") return packageId;
    const version = boundedString(
      ownData(candidate, "version"),
      "Type Explorer version",
      maximumRequestCharacters);
    if (version.kind === "rejected") return version;
    const framework = boundedString(
      ownData(candidate, "framework"),
      "Type Explorer framework",
      maximumRequestCharacters);
    if (framework.kind === "rejected") return framework;
    const assembly = boundedString(
      ownData(candidate, "assembly"),
      "Type Explorer assembly",
      maximumRequestCharacters);
    if (assembly.kind === "rejected") return assembly;
    const type = boundedString(
      ownData(candidate, "type"),
      "Type Explorer type",
      maximumRequestCharacters);
    if (type.kind === "rejected") return type;
    const taste = boundedString(
      ownData(candidate, "taste"),
      "Type Explorer taste",
      maximumRequestCharacters);
    if (taste.kind === "rejected") return taste;
    const projection = engineWorkerTypeExplorerRequest.decode(
      ownData(candidate, "projection"));
    if (projection.kind === "rejected") return projection;
    if (packageId.value.length
      + version.value.length
      + framework.value.length
      + assembly.value.length
      + type.value.length
      + taste.value.length > maximumRequestCharacters) {
      return rejected(
        `Type Explorer request exceeds ${maximumRequestCharacters} characters.`,
        "oversized");
    }
    return {
      kind: "decoded",
      value: {
        packageId: packageId.value,
        version: version.value,
        framework: framework.value,
        assembly: assembly.value,
        type: type.value,
        taste: taste.value,
        projection: projection.value,
      },
    };
  },
};

const typeExplorerValue:
BoundedPayloadDecoder<BrowserTypeExplorerInspection> = {
  decode(value) {
    return decodeEngineWorkerJsonValue<BrowserTypeExplorerInspection>(value);
  },
};

const typeExplorerFailure:
BoundedPayloadDecoder<EngineWorkerTypeExplorerFailure> = {
  decode(value) {
    const candidate = dataRecord(value);
    if (candidate === null || !hasExactData(candidate, [
      "failureKind",
      "error",
      "diagnostic",
    ])) {
      return rejected("Expected a Type Explorer failure.");
    }
    const failureKind = ownData(candidate, "failureKind");
    if (failureKind !== "Expected" && failureKind !== "Unexpected")
      return rejected("Type Explorer failure kind is invalid.");
    const error = boundedString(
      ownData(candidate, "error"),
      "Type Explorer error");
    if (error.kind === "rejected") return error;
    const diagnostic = boundedString(
      ownData(candidate, "diagnostic"),
      "Type Explorer diagnostic");
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

const typeExplorerDiagnostic = {
  decode(value: unknown) {
    return boundedString(value, "Type Explorer diagnostic");
  },
} satisfies BoundedPayloadDecoder<string>;

const typeExplorerBoundaryErrors = mapEngineWorkerBoundaryErrors(message => ({
  failureKind: "Unexpected" as const,
  error: message,
  diagnostic: message,
}));

const noProgress: BoundedPayloadDecoder<never> = {
  decode() {
    return rejected("Type Explorer does not publish progress payloads.");
  },
};

function cancellationReason(
  value: unknown,
): WorkerOperationCancelReason | null {
  return typeof value === "string" && isWorkerOperationCancelReason(value)
    ? value
    : null;
}

export function mapEngineWorkerTypeExplorerResult(
  value: unknown,
): TypeExplorerSettlement {
  const candidate = dataRecord(value);
  if (candidate === null || !hasExactData(candidate, [
    "version",
    "kind",
    "value",
    "failureKind",
    "error",
    "diagnostic",
    "reason",
  ]) || ownData(candidate, "version") !== 1) {
    throw new Error("Expected a version 1 Type Explorer result.");
  }
  const kind = ownData(candidate, "kind");
  const rawValue = ownData(candidate, "value");
  const rawFailureKind = ownData(candidate, "failureKind");
  const rawError = ownData(candidate, "error");
  const rawDiagnostic = ownData(candidate, "diagnostic");
  const rawReason = ownData(candidate, "reason");
  if (kind === "Succeeded") {
    if (rawFailureKind !== null || rawError !== null
      || rawDiagnostic !== null || rawReason !== null) {
      throw new Error("Type Explorer success contains terminal failure data.");
    }
    const inspection = typeExplorerValue.decode(rawValue);
    if (inspection.kind === "rejected")
      throw new Error(inspection.message);
    return { kind: "succeeded", value: inspection.value };
  }
  if (kind === "Failed") {
    if (rawValue !== null || rawReason !== null)
      throw new Error("Type Explorer failure contains value or cancellation data.");
    const failure = typeExplorerFailure.decode({
      failureKind: rawFailureKind,
      error: rawError,
      diagnostic: rawDiagnostic,
    });
    if (failure.kind === "rejected")
      throw new Error(failure.message);
    return {
      kind: "failed",
      failureKind: failure.value.failureKind === "Expected"
        ? "expected"
        : "unexpected",
      error: failure.value,
      diagnostic: failure.value.diagnostic,
    };
  }
  if (kind === "Canceled") {
    if (rawValue !== null || rawFailureKind !== null
      || rawError !== null || rawDiagnostic !== null) {
      throw new Error("Type Explorer cancellation contains failure data.");
    }
    const reason = cancellationReason(rawReason);
    if (reason === null)
      throw new Error("Type Explorer cancellation reason is invalid.");
    return { kind: "canceled", reason };
  }
  throw new Error("Type Explorer result kind is invalid.");
}

export function engineWorkerTypeExplorerCancellationIsRunning(
  value: unknown,
  requestedReason: WorkerOperationCancelReason,
): boolean {
  const candidate = dataRecord(value);
  if (candidate === null || !hasExactData(candidate, ["kind", "reason"]))
    throw new Error("Expected a Type Explorer cancellation result.");
  const kind = ownData(candidate, "kind");
  const rawReason = ownData(candidate, "reason");
  if (kind === "NotActive") {
    if (rawReason !== null)
      throw new Error("Inactive Type Explorer cancellation has a reason.");
    return false;
  }
  const reason = cancellationReason(rawReason);
  if (reason === null)
    throw new Error("Type Explorer cancellation reason is invalid.");
  if (kind === "Requested") {
    if (reason !== requestedReason) {
      throw new Error(
        "Type Explorer cancellation changed the requested reason.");
    }
    return true;
  }
  if (kind === "AlreadyRequested") return true;
  throw new Error("Type Explorer cancellation kind is invalid.");
}

export function createEngineWorkerTypeExplorerHostRegistration():
WorkerRuntimeOperationRegistration<
  TypeExplorerLoadRequest,
  BrowserTypeExplorerInspection,
  EngineWorkerTypeExplorerFailure,
  string,
  never,
  WorkerRuntimePreparationError
> {
  return {
    kind: engineWorkerTypeExplorerKind,
    allowance: { kind: "unbounded" },
    encodeInput: engineWorkerTypeExplorerInput.decode,
    value: typeExplorerValue,
    error: typeExplorerFailure,
    diagnostic: typeExplorerDiagnostic,
    progress: noProgress,
    mapPreparationError: error => error,
    boundaryErrors: typeExplorerBoundaryErrors,
  };
}

export function registerEngineWorkerTypeExplorerOperation(
  operations: WorkerOperationCatalog,
  facade: () => EngineWorkerTypeExplorerFacade,
): void {
  operations.register({
    kind: engineWorkerTypeExplorerKind,
    allowance: { kind: "unbounded" },
    input: engineWorkerTypeExplorerInput,
    rejectInvalidPayload: failure => ({
      error: {
        failureKind: "Unexpected",
        error: failure.message,
        diagnostic: failure.message,
      },
      diagnostic: failure.message,
    }),
    invoke: async (input, context) => {
      try {
        return mapEngineWorkerTypeExplorerResult(
          await facade().queryTypeExplorer(
          context.operation.operationId,
          input.packageId,
          input.version,
          input.framework,
          input.assembly,
          input.type,
          input.taste,
          input.projection));
      } catch (error: unknown) {
        const diagnostic = error instanceof Error
          ? error.message.slice(0, maximumAuxiliaryCharacters)
          : "Type Explorer result validation failed.";
        return {
          kind: "failed",
          failureKind: "unexpected",
          error: {
            failureKind: "Unexpected",
            error: "Type Explorer returned invalid Worker boundary data.",
            diagnostic,
          },
          diagnostic,
        };
      }
    },
    cancel: (operation, reason) =>
      engineWorkerTypeExplorerCancellationIsRunning(
        facade().cancelTypeExplorerQuery(
          operation.operationId,
          reason),
        reason),
  });
}
