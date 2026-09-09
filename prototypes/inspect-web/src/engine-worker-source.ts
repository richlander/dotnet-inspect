import type {
  BrowserSource,
  BrowserTypeSourceCancellation,
  BrowserTypeSourceResult,
} from "./facades/inspect-web-source.d.ts";
import type { TypeSourceLoadRequest } from "./source-inspection.ts";
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
import { engineWorkerBoundaryErrors } from "./engine-worker-contract.ts";

export const engineWorkerTypeSourceKind = "type-source";

const maxRequestCharacters = 64 * 1024;
const maxAuxiliaryCharacters = 64 * 1024;
const maxSourceTextCharacters = 32_000_000;

export interface EngineWorkerTypeSourceInput {
  readonly packageId: string;
  readonly version: string;
  readonly framework: string;
  readonly assembly: string;
  readonly type: string;
  readonly taste: string;
}

export interface EngineWorkerTypeSourceFacade {
  queryTypeSource(
    operationId: string,
    packageId: string,
    version: string,
    targetFramework: string,
    assemblyName: string,
    typeIdentity: string,
    styleOptionsJson: string,
  ): Promise<BrowserTypeSourceResult>;
  cancelTypeSourceQuery(
    operationId: string,
    reason: string,
  ): BrowserTypeSourceCancellation;
}

type TypeSourceSettlement =
  ManagedOperationSettlement<BrowserSource, string, string>;

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
  label: string,
  maximumCharacters = maxAuxiliaryCharacters,
): BoundedPayloadDecoder<string> {
  return {
    decode(value) {
      if (typeof value !== "string")
        return rejected(`Expected ${label} text.`);
      if (value.length > maximumCharacters) {
        return rejected(
          `${label} text exceeds ${maximumCharacters} characters.`,
          "oversized",
        );
      }
      return { kind: "decoded", value };
    },
  };
}

function decodeNullableString(
  value: unknown,
  label: string,
): BoundedPayloadDecodeResult<string | null> {
  if (value === null) return { kind: "decoded", value: null };
  return boundedString(label).decode(value);
}

export const engineWorkerTypeSourceInput:
BoundedPayloadDecoder<EngineWorkerTypeSourceInput> = {
  decode(value) {
    const candidate = dataRecord(value);
    if (candidate === null || !hasExactData(candidate, [
      "packageId",
      "version",
      "framework",
      "assembly",
      "type",
      "taste",
    ])) {
      return rejected("Expected a Type Source request.");
    }

    const packageId = ownData(candidate, "packageId");
    const version = ownData(candidate, "version");
    const framework = ownData(candidate, "framework");
    const assembly = ownData(candidate, "assembly");
    const type = ownData(candidate, "type");
    const taste = ownData(candidate, "taste");
    if (typeof packageId !== "string"
      || typeof version !== "string"
      || typeof framework !== "string"
      || typeof assembly !== "string"
      || typeof type !== "string"
      || typeof taste !== "string") {
      return rejected("Type Source request fields must be text.");
    }
    const characters = packageId.length
      + version.length
      + framework.length
      + assembly.length
      + type.length
      + taste.length;
    if (characters > maxRequestCharacters) {
      return rejected(
        `Type Source request exceeds ${maxRequestCharacters} characters.`,
        "oversized",
      );
    }

    return {
      kind: "decoded",
      value: {
        packageId,
        version,
        framework,
        assembly,
        type,
        taste,
      },
    };
  },
};

export const engineWorkerTypeSourceValue: BoundedPayloadDecoder<BrowserSource> = {
  decode(value) {
    const candidate = dataRecord(value);
    if (candidate === null || !hasExactData(candidate, [
      "provider",
      "provenance",
      "url",
      "pdbSourceLimitation",
      "text",
    ])) {
      return rejected("Expected a Type Source value.");
    }

    const provider = boundedString("Type Source provider")
      .decode(ownData(candidate, "provider"));
    if (provider.kind === "rejected") return provider;
    const provenance = boundedString("Type Source provenance")
      .decode(ownData(candidate, "provenance"));
    if (provenance.kind === "rejected") return provenance;
    const url = decodeNullableString(
      ownData(candidate, "url"),
      "Type Source URL",
    );
    if (url.kind === "rejected") return url;
    const limitation = decodeNullableString(
      ownData(candidate, "pdbSourceLimitation"),
      "Type Source PDB limitation",
    );
    if (limitation.kind === "rejected") return limitation;
    const text = boundedString(
      "Type Source",
      maxSourceTextCharacters,
    ).decode(ownData(candidate, "text"));
    if (text.kind === "rejected") return text;

    const auxiliaryCharacters =
      provider.value.length
      + provenance.value.length
      + (url.value?.length ?? 0)
      + (limitation.value?.length ?? 0);
    if (auxiliaryCharacters > maxAuxiliaryCharacters) {
      return rejected(
        `Type Source metadata exceeds ${maxAuxiliaryCharacters} characters.`,
        "oversized",
      );
    }

    return {
      kind: "decoded",
      value: {
        provider: provider.value,
        provenance: provenance.value,
        url: url.value,
        pdbSourceLimitation: limitation.value,
        text: text.value,
      },
    };
  },
};

const typeSourceError = boundedString("Type Source error");
const typeSourceDiagnostic = boundedString("Type Source diagnostic");

export const engineWorkerTypeSourceProgress: BoundedPayloadDecoder<never> = {
  decode() {
    return rejected("Type Source does not publish progress payloads.");
  },
};

function cancellationReason(
  value: unknown,
): WorkerOperationCancelReason | null {
  return typeof value === "string" && isWorkerOperationCancelReason(value)
    ? value
    : null;
}

function transportLimitFailure(
  diagnostic: string,
): TypeSourceSettlement {
  return {
    kind: "failed",
    failureKind: "unexpected",
    error: "Type Source exceeded Worker transport limits.",
    diagnostic,
  };
}

function invalidResultFailure(error: unknown): TypeSourceSettlement {
  return {
    kind: "failed",
    failureKind: "unexpected",
    error: "Type Source returned invalid Worker boundary data.",
    diagnostic: error instanceof Error
      ? error.message.slice(0, maxAuxiliaryCharacters)
      : "Type Source result validation failed.",
  };
}

export function mapEngineWorkerTypeSourceResult(
  value: unknown,
): TypeSourceSettlement {
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
    throw new Error("Expected a version 1 Type Source result.");
  }

  const kind = ownData(candidate, "kind");
  const rawValue = ownData(candidate, "value");
  const failureKind = ownData(candidate, "failureKind");
  const rawError = ownData(candidate, "error");
  const rawDiagnostic = ownData(candidate, "diagnostic");
  const rawReason = ownData(candidate, "reason");
  if (kind === "Succeeded") {
    if (failureKind !== null
      || rawError !== null
      || rawDiagnostic !== null
      || rawReason !== null) {
      throw new Error("Type Source success contains terminal failure data.");
    }
    const source = engineWorkerTypeSourceValue.decode(rawValue);
    if (source.kind === "rejected") {
      if (source.reason === "oversized")
        return transportLimitFailure(source.message);
      throw new Error(source.message);
    }
    return { kind: "succeeded", value: source.value };
  }

  if (kind === "Failed") {
    if (rawValue !== null || rawReason !== null)
      throw new Error("Type Source failure contains value or cancellation data.");
    if (failureKind !== "Expected" && failureKind !== "Unexpected")
      throw new Error("Type Source failure kind is invalid.");
    const error = typeSourceError.decode(rawError);
    if (error.kind === "rejected") {
      if (error.reason === "oversized")
        return transportLimitFailure(error.message);
      throw new Error(error.message);
    }
    const diagnostic = typeSourceDiagnostic.decode(rawDiagnostic);
    if (diagnostic.kind === "rejected") {
      if (diagnostic.reason === "oversized")
        return transportLimitFailure(diagnostic.message);
      throw new Error(diagnostic.message);
    }
    return {
      kind: "failed",
      failureKind: failureKind === "Expected" ? "expected" : "unexpected",
      error: error.value,
      diagnostic: diagnostic.value,
    };
  }

  if (kind === "Canceled") {
    if (rawValue !== null
      || failureKind !== null
      || rawError !== null
      || rawDiagnostic !== null) {
      throw new Error("Type Source cancellation contains value or failure data.");
    }
    const reason = cancellationReason(rawReason);
    if (reason === null)
      throw new Error("Type Source cancellation reason is invalid.");
    return { kind: "canceled", reason };
  }

  throw new Error("Type Source result kind is invalid.");
}

export function engineWorkerTypeSourceCancellationIsRunning(
  value: unknown,
  requestedReason: WorkerOperationCancelReason,
): boolean {
  const candidate = dataRecord(value);
  if (candidate === null || !hasExactData(candidate, ["kind", "reason"]))
    throw new Error("Expected a Type Source cancellation result.");
  const kind = ownData(candidate, "kind");
  const rawReason = ownData(candidate, "reason");
  if (kind === "NotActive") {
    if (rawReason !== null)
      throw new Error("Inactive Type Source cancellation has a reason.");
    return false;
  }

  const reason = cancellationReason(rawReason);
  if (reason === null)
    throw new Error("Type Source cancellation reason is invalid.");
  if (kind === "Requested") {
    if (reason !== requestedReason)
      throw new Error("Type Source cancellation changed the requested reason.");
    return true;
  }
  if (kind === "AlreadyRequested") return true;
  throw new Error("Type Source cancellation kind is invalid.");
}

export function createEngineWorkerTypeSourceHostRegistration():
WorkerRuntimeOperationRegistration<
  TypeSourceLoadRequest,
  BrowserSource,
  string,
  string,
  never,
  WorkerRuntimePreparationError
> {
  return {
    kind: engineWorkerTypeSourceKind,
    allowance: { kind: "unbounded" },
    encodeInput(request) {
      return engineWorkerTypeSourceInput.decode({
        packageId: request.packageId,
        version: request.version,
        framework: request.framework,
        assembly: request.assembly,
        type: request.type,
        taste: request.taste,
      });
    },
    value: engineWorkerTypeSourceValue,
    error: typeSourceError,
    diagnostic: typeSourceDiagnostic,
    progress: engineWorkerTypeSourceProgress,
    mapPreparationError: error => error,
    boundaryErrors: engineWorkerBoundaryErrors,
  };
}

export function registerEngineWorkerTypeSourceOperation(
  operations: WorkerOperationCatalog,
  facade: () => EngineWorkerTypeSourceFacade,
): void {
  operations.register({
    kind: engineWorkerTypeSourceKind,
    allowance: { kind: "unbounded" },
    input: engineWorkerTypeSourceInput,
    rejectInvalidPayload: failure => ({
      error: failure.message,
      diagnostic: failure.message,
    }),
    invoke: async (input, context) => {
      const source = facade();
      const result = await source.queryTypeSource(
        context.operation.operationId,
        input.packageId,
        input.version,
        input.framework,
        input.assembly,
        input.type,
        input.taste,
      );
      try {
        return mapEngineWorkerTypeSourceResult(result);
      } catch (error: unknown) {
        return invalidResultFailure(error);
      }
    },
    cancel: (operation, reason) => {
      const result = facade().cancelTypeSourceQuery(
        operation.operationId,
        reason,
      );
      return engineWorkerTypeSourceCancellationIsRunning(result, reason);
    },
  });
}
