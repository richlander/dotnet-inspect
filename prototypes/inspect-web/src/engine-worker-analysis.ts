import type {
  BrowserCloneCandidateRequest,
  BrowserCloneCandidateResult,
} from "./facades/inspect-web-analysis.d.ts";
import {
  engineWorkerBoundaryErrors,
  engineWorkerManagedProducerAllowance,
  engineWorkerText,
} from "./engine-worker-contract.ts";
import type { OperationProducerAdapter } from "./operation-authority.ts";
import type {
  WorkerRuntimeOperationRegistration,
  WorkerRuntimePreparationError,
} from "./worker-runtime-core.ts";
import type {
  BoundedPayloadDecodeResult,
  BoundedPayloadDecoder,
} from "./worker-runtime-protocol.ts";
import type { WorkerOperationCatalog } from "./worker-runtime-realm.ts";

const engineWorkerCloneCandidateKind = "clone-candidates";

const maximumCloneRequestCharacters = 512 * 1024;
const maximumCloneResultCharacters = 8 * 1024 * 1024;
const maximumClonePayloadDepth = 32;
const maximumClonePayloadEntries = 100_000;
const maximumClonePackages = 12;
const maximumCloneSeeds = 1_000;
// The producer independently admits implementation and reference-only roles.
const maximumCloneLibraries = 256 * 2;
const maximumCloneRows = 100;

export interface EngineWorkerCloneCandidateFacade {
  queryCloneCandidates(requestJson: string): Promise<BrowserCloneCandidateResult>;
}

function dataRecord(value: unknown): Record<string, unknown> | null {
  if (typeof value !== "object" || value === null || Array.isArray(value))
    return null;
  if (Object.getPrototypeOf(value) !== Object.prototype
    && Object.getPrototypeOf(value) !== null) {
    return null;
  }
  return Object.fromEntries(
    Object.keys(value).map(key => [key, Reflect.get(value, key)]),
  );
}

function hasExactData(
  value: Record<string, unknown>,
  expected: readonly string[],
): boolean {
  const keys = Object.keys(value);
  return keys.length === expected.length
    && expected.every(key => Object.prototype.hasOwnProperty.call(value, key));
}

function isInteger(value: unknown): value is number {
  return typeof value === "number" && Number.isSafeInteger(value);
}

function isStringOrInteger(value: unknown): value is string | number {
  return typeof value === "string" || isInteger(value);
}

function isNullableStringOrInteger(
  value: unknown,
): value is string | number | null {
  return value === null || isStringOrInteger(value);
}

function isCloneSeed(value: unknown): boolean {
  const candidate = dataRecord(value);
  if (candidate === null
    || !hasExactData(candidate, [
      "kind",
      "typeDefinitionId",
      "member",
      "body",
    ])) {
    return false;
  }
  const member = dataRecord(candidate.member);
  const body = dataRecord(candidate.body);
  return isStringOrInteger(candidate.kind)
    && (candidate.typeDefinitionId === null
      || typeof candidate.typeDefinitionId === "string")
    && (member === null || (
      hasExactData(member, [
        "stableSelector",
        "canonicalSignature",
        "fingerprint",
        "typeFullName",
        "memberName",
      ])
      && Object.values(member).every(item => typeof item === "string")
    ))
    && (body === null || (
      hasExactData(body, ["memberName", "selectorKey", "metadataToken"])
      && typeof body.memberName === "string"
      && typeof body.selectorKey === "string"
      && isInteger(body.metadataToken)
    ));
}

function isCloneRequest(value: unknown): value is BrowserCloneCandidateRequest {
  const candidate = dataRecord(value);
  if (candidate === null || !hasExactData(candidate, [
    "schemaVersion",
    "packages",
    "selectedPackageIndex",
    "assembly",
    "seed",
    "breadth",
    "discovery",
  ])) {
    return false;
  }
  if (candidate.schemaVersion !== 1
    || !Array.isArray(candidate.packages)
    || candidate.packages.length === 0
    || candidate.packages.length > maximumClonePackages
    || !isInteger(candidate.selectedPackageIndex)
    || candidate.selectedPackageIndex < 0
    || candidate.selectedPackageIndex >= candidate.packages.length
    || typeof candidate.assembly !== "string"
    || !isCloneSeed(candidate.seed)
    || !isStringOrInteger(candidate.breadth)
    || !isStringOrInteger(candidate.discovery)) {
    return false;
  }
  return candidate.packages.every(item => {
    const packageItem = dataRecord(item);
    return packageItem !== null
      && hasExactData(packageItem, [
        "packageId",
        "version",
        "targetFramework",
      ])
      && Object.values(packageItem).every(itemValue =>
        typeof itemValue === "string");
  });
}

function inspectPlainPayload(
  value: unknown,
  maximumCharacters: number,
): BoundedPayloadDecodeResult<unknown> {
  const active = new Set<object>();
  let entries = 0;
  let characters = 0;

  function visit(current: unknown, depth: number): boolean {
    if (depth > maximumClonePayloadDepth) return false;
    if (current === null
      || typeof current === "boolean"
      || typeof current === "number") {
      return typeof current !== "number" || Number.isFinite(current);
    }
    if (typeof current === "string") {
      characters += current.length;
      return characters <= maximumCharacters;
    }
    if (typeof current !== "object" || active.has(current)) return false;
    active.add(current);
    if (Array.isArray(current)) {
      entries += current.length;
      const valid = entries <= maximumClonePayloadEntries
        && current.every(item => visit(item, depth + 1));
      active.delete(current);
      return valid;
    }
    const record = dataRecord(current);
    if (record === null) {
      active.delete(current);
      return false;
    }
    const keys = Object.keys(record);
    entries += keys.length;
    const valid = entries <= maximumClonePayloadEntries
      && keys.every(key => {
        characters += key.length;
        return characters <= maximumCharacters
          && visit(record[key], depth + 1);
      });
    active.delete(current);
    return valid;
  }

  if (!visit(value, 0)) {
    const reason = characters > maximumCharacters
      || entries > maximumClonePayloadEntries
      ? "oversized"
      : "invalid";
    return {
      kind: "rejected",
      reason,
      message: reason === "oversized"
        ? "Clone Candidates payload exceeds its transport limit."
        : "Clone Candidates payload is not plain structured data.",
    };
  }

  return { kind: "decoded", value };
}

function isCloneResult(value: unknown): value is BrowserCloneCandidateResult {
  const candidate = dataRecord(value);
  if (candidate === null || !hasExactData(candidate, [
    "schemaVersion",
    "request",
    "kind",
    "document",
    "seedLibrary",
    "openFailureKind",
    "failure",
    "presentationRejectionKind",
    "subject",
    "detail",
    "metadataRootReason",
  ])) {
    return false;
  }
  if (candidate.schemaVersion !== 1
    || !isCloneRequest(candidate.request)
    || !isStringOrInteger(candidate.kind)
    || (candidate.document !== null && dataRecord(candidate.document) === null)
    || (candidate.seedLibrary !== null && dataRecord(candidate.seedLibrary) === null)
    || !isNullableStringOrInteger(candidate.openFailureKind)
    || (candidate.failure !== null && dataRecord(candidate.failure) === null)
    || !isNullableStringOrInteger(candidate.presentationRejectionKind)
    || (candidate.subject !== null && dataRecord(candidate.subject) === null)
    || (candidate.detail !== null && typeof candidate.detail !== "string")
    || !isNullableStringOrInteger(candidate.metadataRootReason)) {
    return false;
  }
  if (candidate.kind === "Available") {
    const document = dataRecord(candidate.document);
    if (document === null
      || !Array.isArray(document.rows)
      || !Array.isArray(document.seeds)
      || !Array.isArray(document.libraries)
      || document.rows.length > maximumCloneRows
      || document.seeds.length > maximumCloneSeeds
      || document.libraries.length > maximumCloneLibraries) {
      return false;
    }
  } else if (candidate.document !== null) {
    return false;
  }
  return true;
}

export const engineWorkerCloneCandidateInput:
BoundedPayloadDecoder<{ readonly requestJson: string }> = {
  decode(value) {
    const candidate = dataRecord(value);
    if (candidate === null || !hasExactData(candidate, ["requestJson"]))
      return { kind: "rejected", reason: "invalid", message: "Expected a Clone Candidates request." };
    if (typeof candidate.requestJson !== "string")
      return { kind: "rejected", reason: "invalid", message: "Clone Candidates request JSON is invalid." };
    if (candidate.requestJson.length > maximumCloneRequestCharacters) {
      return {
        kind: "rejected",
        reason: "oversized",
        message: "Clone Candidates request exceeds its transport limit.",
      };
    }
    let request: unknown;
    try {
      request = JSON.parse(candidate.requestJson);
    } catch {
      return { kind: "rejected", reason: "invalid", message: "Clone Candidates request JSON is invalid." };
    }
    const inspected = inspectPlainPayload(request, maximumCloneRequestCharacters);
    if (inspected.kind === "rejected") return inspected;
    if (!isCloneRequest(request))
      return { kind: "rejected", reason: "invalid", message: "Clone Candidates request shape is invalid." };
    return {
      kind: "decoded",
      value: { requestJson: candidate.requestJson },
    };
  },
};

export const engineWorkerCloneCandidateValue:
BoundedPayloadDecoder<BrowserCloneCandidateResult> = {
  decode(value) {
    const inspected = inspectPlainPayload(value, maximumCloneResultCharacters);
    if (inspected.kind === "rejected") return inspected;
    if (!isCloneResult(value)) {
      return {
        kind: "rejected",
        reason: "invalid",
        message: "Clone Candidates result shape is invalid.",
      };
    }
    return { kind: "decoded", value };
  },
};

export function createEngineWorkerCloneCandidateHostRegistration():
WorkerRuntimeOperationRegistration<
  string,
  BrowserCloneCandidateResult,
  string,
  string,
  never,
  WorkerRuntimePreparationError
> {
  return {
    kind: engineWorkerCloneCandidateKind,
    allowance: engineWorkerManagedProducerAllowance,
    encodeInput(requestJson) {
      return engineWorkerCloneCandidateInput.decode({ requestJson });
    },
    value: engineWorkerCloneCandidateValue,
    error: engineWorkerText,
    diagnostic: engineWorkerText,
    progress: {
      decode() {
        return {
          kind: "rejected",
          reason: "invalid",
          message: "Clone Candidates does not publish progress.",
        };
      },
    },
    mapPreparationError: error => error,
    boundaryErrors: engineWorkerBoundaryErrors,
  };
}

export function registerEngineWorkerCloneCandidateOperation(
  operations: WorkerOperationCatalog,
  facade: () => EngineWorkerCloneCandidateFacade,
): void {
  operations.register({
    kind: engineWorkerCloneCandidateKind,
    allowance: engineWorkerManagedProducerAllowance,
    input: engineWorkerCloneCandidateInput,
    rejectInvalidPayload: failure => ({
      error: failure.message,
      diagnostic: failure.message,
    }),
    invoke: async input => {
      const result = await facade().queryCloneCandidates(input.requestJson);
      const decoded = engineWorkerCloneCandidateValue.decode(result);
      if (decoded.kind === "rejected") {
        return {
          kind: "failed",
          failureKind: "unexpected",
          error: decoded.message,
          diagnostic: decoded.message,
        };
      }
      return { kind: "succeeded", value: decoded.value };
    },
  });
}

export type EngineWorkerCloneCandidateAdapter = OperationProducerAdapter<
  string,
  BrowserCloneCandidateResult,
  string,
  never,
  WorkerRuntimePreparationError
>;
