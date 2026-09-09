import type {
  BrowserMethodBodyComparison,
  BrowserMethodBodyComparisonRequest,
  BrowserMethodBodyComparisonResult,
  BrowserMethodBodyTargets,
  BrowserMethodBodyTargetsResult,
  BrowserSourceComparison,
  BrowserSourceComparisonRequest,
  BrowserSourceComparisonResult,
} from "./facades/inspect-web-source.d.ts";
import type { MethodBodyComparisonContext } from "./method-body-comparison.ts";
import {
  engineWorkerTypeSourceCancellationIsRunning,
} from "./engine-worker-source.ts";
import { engineWorkerBoundaryErrors } from "./engine-worker-contract.ts";
import type {
  WorkerRuntimeOperationRegistration,
  WorkerRuntimePreparationError,
} from "./worker-runtime-core.ts";
import type {
  BoundedPayloadDecodeResult,
  BoundedPayloadDecoder,
  ManagedOperationSettlement,
} from "./worker-runtime-protocol.ts";
import {
  isWorkerOperationCancelReason,
} from "./worker-runtime-protocol.ts";
import type { WorkerOperationCatalog } from "./worker-runtime-realm.ts";

const maximumRequestCharacters = 1_048_576;
const maximumResultCharacters = 32_000_000;
const maximumDiagnosticCharacters = 64 * 1024;

export const engineWorkerMethodBodyTargetsKind =
  "method-body-comparison-targets";
export const engineWorkerMethodBodyComparisonKind =
  "method-body-comparison";
export const engineWorkerMemberSourceComparisonKind =
  "member-source-comparison";

type JsonFieldKind =
  | "array"
  | "boolean"
  | "nullable-string"
  | "number"
  | "object"
  | "string";

interface JsonField {
  readonly name: string;
  readonly kind: JsonFieldKind;
}

interface GeneratedJsonCodec<T extends object> {
  readonly decoder: BoundedPayloadDecoder<T>;
  encode(value: unknown): BoundedPayloadDecodeResult<string>;
}

class SourceAuthorityPayloadError extends Error {
  readonly reason: "invalid" | "oversized";

  constructor(
    message: string,
    reason: "invalid" | "oversized" = "invalid",
  ) {
    super(message);
    this.reason = reason;
  }
}

function rejected(
  error: unknown,
): BoundedPayloadDecodeResult<never> {
  if (error instanceof SourceAuthorityPayloadError) {
    return {
      kind: "rejected",
      reason: error.reason,
      message: error.message,
      cause: error,
    };
  }
  throw error;
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

function generatedJsonCodec<T extends object>(
  label: string,
  fields: readonly JsonField[],
  maximumCharacters: number,
  exactFields: boolean,
): GeneratedJsonCodec<T> {
  const parse = (value: unknown): T => {
    if (typeof value !== "string") {
      throw new SourceAuthorityPayloadError(
        `Expected ${label} JSON text.`);
    }
    if (value.length > maximumCharacters) {
      throw new SourceAuthorityPayloadError(
        `${label} JSON exceeds ${maximumCharacters} characters.`,
        "oversized");
    }
    let parsed: unknown;
    try {
      parsed = JSON.parse(value);
    } catch (error: unknown) {
      throw new SourceAuthorityPayloadError(
        error instanceof Error
          ? `${label} JSON is invalid: ${error.message}`
          : `${label} JSON is invalid.`);
    }
    const candidate = dataRecord(parsed);
    if (candidate === null) {
      throw new SourceAuthorityPayloadError(
        `Expected a ${label} object.`);
    }
    if (exactFields && Reflect.ownKeys(candidate).length !== fields.length) {
      throw new SourceAuthorityPayloadError(
        `${label} has unexpected fields.`);
    }
    for (const field of fields) {
      const property = Object.getOwnPropertyDescriptor(
        candidate,
        field.name);
      if (property === undefined || !("value" in property)) {
        throw new SourceAuthorityPayloadError(
          `${label} is missing '${field.name}'.`);
      }
      const fieldValue: unknown = property.value;
      const matches = field.kind === "object"
        ? dataRecord(fieldValue) !== null
        : field.kind === "array"
          ? Array.isArray(fieldValue)
          : field.kind === "nullable-string"
            ? fieldValue === null || typeof fieldValue === "string"
            : typeof fieldValue === field.kind;
      const finiteNumber = field.kind !== "number"
        || (typeof fieldValue === "number"
          && Number.isFinite(fieldValue));
      if (!matches || !finiteNumber) {
        throw new SourceAuthorityPayloadError(
          `${label} field '${field.name}' has an invalid value.`);
      }
    }

    // The generated facade owns the complete DTO shape. This boundary verifies
    // its JSON representation, size, top-level category, and required fields;
    // reproducing every generated nested DTO would create a second schema owner.
    // oxlint-disable-next-line typescript/no-unsafe-type-assertion
    return candidate as T;
  };

  const decoder: BoundedPayloadDecoder<T> = {
    decode(value) {
      try {
        return { kind: "decoded", value: parse(value) };
      } catch (error: unknown) {
        return rejected(error);
      }
    },
  };
  return {
    decoder,
    encode(value) {
      let encoded: string;
      try {
        const candidate = dataRecord(value);
        if (candidate === null) {
          throw new SourceAuthorityPayloadError(
            `Expected a ${label} object.`);
        }
        encoded = JSON.stringify(candidate);
      } catch (error: unknown) {
        if (error instanceof SourceAuthorityPayloadError) {
          return rejected(error);
        }
        return rejected(new SourceAuthorityPayloadError(
          error instanceof Error
            ? `${label} is not JSON: ${error.message}`
            : `${label} is not JSON.`));
      }
      const decoded = decoder.decode(encoded);
      return decoded.kind === "rejected"
        ? decoded
        : { kind: "decoded", value: encoded };
    },
  };
}

const text = (name: string): JsonField => ({ name, kind: "string" });
const number = (name: string): JsonField => ({ name, kind: "number" });
const object = (name: string): JsonField => ({ name, kind: "object" });
const array = (name: string): JsonField => ({ name, kind: "array" });
const boolean = (name: string): JsonField => ({ name, kind: "boolean" });
const nullableText = (name: string): JsonField => ({
  name,
  kind: "nullable-string",
});

const targetsInput = generatedJsonCodec<MethodBodyComparisonContext>(
  "Method Body target request",
  [
    text("packageId"),
    text("version"),
    text("framework"),
    text("assembly"),
    text("typeIdentity"),
    text("memberName"),
    text("selectorKey"),
    number("metadataToken"),
    text("label"),
  ],
  maximumRequestCharacters,
  true);
const comparisonInput =
  generatedJsonCodec<BrowserMethodBodyComparisonRequest>(
    "Method Body comparison request",
    [
      text("packageId"),
      text("version"),
      text("framework"),
      text("assembly"),
      text("moduleVersionId"),
      object("before"),
      object("after"),
    ],
    maximumRequestCharacters,
    true);
const sourceComparisonInput =
  generatedJsonCodec<BrowserSourceComparisonRequest>(
    "Source comparison request",
    [
      text("packageId"),
      text("beforeVersion"),
      text("afterVersion"),
      text("framework"),
      text("assembly"),
      text("typeIdentity"),
      text("memberName"),
      text("selectorKey"),
      number("metadataToken"),
    ],
    maximumRequestCharacters,
    true);

const targetsValue = generatedJsonCodec<BrowserMethodBodyTargets>(
  "Method Body target result",
  [
    text("packageId"),
    text("version"),
    text("framework"),
    text("assembly"),
    text("moduleVersionId"),
    object("before"),
  ],
  maximumResultCharacters,
  false);
const comparisonValue =
  generatedJsonCodec<BrowserMethodBodyComparison>(
    "Method Body comparison result",
    [
      object("request"),
      text("stage"),
      text("outcome"),
      array("producers"),
      array("diagnostics"),
    ],
    maximumResultCharacters,
    false);
const sourceComparisonValue =
  generatedJsonCodec<BrowserSourceComparison>(
    "Source comparison result",
    [
      object("request"),
      text("status"),
      boolean("isExact"),
      object("before"),
      object("after"),
      array("lines"),
      nullableText("failure"),
    ],
    maximumResultCharacters,
    false);

type SourceAuthoritySettlement =
  ManagedOperationSettlement<string, string, string>;

function boundedText(value: unknown, label: string): string {
  if (typeof value !== "string") {
    throw new SourceAuthorityPayloadError(
      `Expected ${label} text.`);
  }
  if (value.length > maximumDiagnosticCharacters) {
    throw new SourceAuthorityPayloadError(
      `${label} text exceeds ${maximumDiagnosticCharacters} characters.`,
      "oversized");
  }
  return value;
}

function mapResult<T extends object>(
  value: unknown,
  label: string,
  codec: GeneratedJsonCodec<T>,
): SourceAuthoritySettlement {
  const candidate = dataRecord(value);
  if (candidate === null
    || Reflect.ownKeys(candidate).length !== 7
    || ownData(candidate, "version") !== 1) {
    throw new SourceAuthorityPayloadError(
      `Expected a version 1 ${label} result.`);
  }
  const kind = ownData(candidate, "kind");
  const resultValue = ownData(candidate, "value");
  const failureKind = ownData(candidate, "failureKind");
  const error = ownData(candidate, "error");
  const diagnostic = ownData(candidate, "diagnostic");
  const reason = ownData(candidate, "reason");
  if (kind === "Succeeded") {
    if (failureKind !== null
      || error !== null
      || diagnostic !== null
      || reason !== null) {
      throw new SourceAuthorityPayloadError(
        `${label} success contains failure data.`);
    }
    const encoded = codec.encode(resultValue);
    if (encoded.kind === "rejected") {
      return {
        kind: "failed",
        failureKind: "unexpected",
        error: `${label} returned invalid Worker boundary data.`,
        diagnostic: encoded.message,
      };
    }
    return { kind: "succeeded", value: encoded.value };
  }
  if (kind === "Failed") {
    if (resultValue !== null || reason !== null
      || (failureKind !== "Expected"
        && failureKind !== "Unexpected")) {
      throw new SourceAuthorityPayloadError(
        `${label} failure has invalid terminal data.`);
    }
    return {
      kind: "failed",
      failureKind:
        failureKind === "Expected" ? "expected" : "unexpected",
      error: boundedText(error, `${label} error`),
      diagnostic: boundedText(
        diagnostic,
        `${label} diagnostic`),
    };
  }
  if (kind === "Canceled") {
    if (resultValue !== null
      || failureKind !== null
      || error !== null
      || diagnostic !== null
      || typeof reason !== "string"
      || !isWorkerOperationCancelReason(reason)) {
      throw new SourceAuthorityPayloadError(
        `${label} cancellation has invalid terminal data.`);
    }
    return { kind: "canceled", reason };
  }
  throw new SourceAuthorityPayloadError(
    `${label} result kind is invalid.`);
}

type SourceFacade =
  typeof import("./facades/inspect-web-source.d.ts");

export type EngineWorkerSourceAuthorityFacade = Pick<
  SourceFacade,
  "cancelMemberSourceComparison"
  | "cancelMethodBodyComparison"
  | "queryMemberSourceComparison"
  | "queryMethodBodyComparison"
  | "queryMethodBodyComparisonTargets"
>;

interface AuthorityOperation<TInput extends object, TValue extends object> {
  readonly kind: string;
  readonly input: GeneratedJsonCodec<TInput>;
  readonly value: GeneratedJsonCodec<TValue>;
}

export const engineWorkerMethodBodyTargetsOperation:
AuthorityOperation<
  MethodBodyComparisonContext,
  BrowserMethodBodyTargets
> = {
  kind: engineWorkerMethodBodyTargetsKind,
  input: targetsInput,
  value: targetsValue,
};

export const engineWorkerMethodBodyComparisonOperation:
AuthorityOperation<
  BrowserMethodBodyComparisonRequest,
  BrowserMethodBodyComparison
> = {
  kind: engineWorkerMethodBodyComparisonKind,
  input: comparisonInput,
  value: comparisonValue,
};

export const engineWorkerMemberSourceComparisonOperation:
AuthorityOperation<
  BrowserSourceComparisonRequest,
  BrowserSourceComparison
> = {
  kind: engineWorkerMemberSourceComparisonKind,
  input: sourceComparisonInput,
  value: sourceComparisonValue,
};

function hostRegistration<TInput extends object, TValue extends object>(
  operation: AuthorityOperation<TInput, TValue>,
): WorkerRuntimeOperationRegistration<
  TInput,
  TValue,
  string,
  string,
  never,
  WorkerRuntimePreparationError
> {
  return {
    kind: operation.kind,
    allowance: { kind: "unbounded" },
    encodeInput: value => operation.input.encode(value),
    value: operation.value.decoder,
    error: {
      decode: value => typeof value === "string"
        ? { kind: "decoded", value }
        : {
            kind: "rejected",
            reason: "invalid",
            message: "Expected Source authority error text.",
          },
    },
    diagnostic: {
      decode: value => typeof value === "string"
        ? { kind: "decoded", value }
        : {
            kind: "rejected",
            reason: "invalid",
            message: "Expected Source authority diagnostic text.",
          },
    },
    progress: {
      decode: () => ({
        kind: "rejected",
        reason: "invalid",
        message: "Source authority operations do not publish progress.",
      }),
    },
    mapPreparationError: error => error,
    boundaryErrors: engineWorkerBoundaryErrors,
  };
}

export const createEngineWorkerMethodBodyTargetsHostRegistration = () =>
  hostRegistration(engineWorkerMethodBodyTargetsOperation);

export const createEngineWorkerMethodBodyComparisonHostRegistration = () =>
  hostRegistration(engineWorkerMethodBodyComparisonOperation);

export const createEngineWorkerMemberSourceComparisonHostRegistration = () =>
  hostRegistration(engineWorkerMemberSourceComparisonOperation);

function invalidResult(
  label: string,
  error: unknown,
): SourceAuthoritySettlement {
  return {
    kind: "failed",
    failureKind: "unexpected",
    error: `${label} returned invalid Worker boundary data.`,
    diagnostic: error instanceof Error
      ? error.message.slice(0, maximumDiagnosticCharacters)
      : `${label} result validation failed.`,
  };
}

export function registerEngineWorkerSourceAuthorityOperations(
  operations: WorkerOperationCatalog,
  facade: () => EngineWorkerSourceAuthorityFacade,
): void {
  operations.register({
    kind: engineWorkerMethodBodyTargetsKind,
    allowance: { kind: "unbounded" },
    input: targetsInput.decoder,
    rejectInvalidPayload: failure => ({
      error: failure.message,
      diagnostic: failure.message,
    }),
    async invoke(input, context) {
      const source = facade();
      const result: BrowserMethodBodyTargetsResult =
        await source.queryMethodBodyComparisonTargets(
          context.operation.operationId,
          input.packageId,
          input.version,
          input.framework,
          input.assembly,
          input.typeIdentity,
          input.memberName,
          input.selectorKey,
          input.metadataToken);
      try {
        return mapResult(
          result,
          "Method Body target",
          targetsValue);
      } catch (error: unknown) {
        return invalidResult("Method Body target", error);
      }
    },
    cancel: (operation, reason) =>
      engineWorkerTypeSourceCancellationIsRunning(
        facade().cancelMethodBodyComparison(
          operation.operationId,
          reason),
        reason),
  });
  operations.register({
    kind: engineWorkerMethodBodyComparisonKind,
    allowance: { kind: "unbounded" },
    input: comparisonInput.decoder,
    rejectInvalidPayload: failure => ({
      error: failure.message,
      diagnostic: failure.message,
    }),
    async invoke(input, context) {
      const result: BrowserMethodBodyComparisonResult =
        await facade().queryMethodBodyComparison(
          context.operation.operationId,
          JSON.stringify(input));
      try {
        return mapResult(
          result,
          "Method Body comparison",
          comparisonValue);
      } catch (error: unknown) {
        return invalidResult("Method Body comparison", error);
      }
    },
    cancel: (operation, reason) =>
      engineWorkerTypeSourceCancellationIsRunning(
        facade().cancelMethodBodyComparison(
          operation.operationId,
          reason),
        reason),
  });
  operations.register({
    kind: engineWorkerMemberSourceComparisonKind,
    allowance: { kind: "unbounded" },
    input: sourceComparisonInput.decoder,
    rejectInvalidPayload: failure => ({
      error: failure.message,
      diagnostic: failure.message,
    }),
    async invoke(input, context) {
      const result: BrowserSourceComparisonResult =
        await facade().queryMemberSourceComparison(
          context.operation.operationId,
          JSON.stringify(input));
      try {
        return mapResult(
          result,
          "Source comparison",
          sourceComparisonValue);
      } catch (error: unknown) {
        return invalidResult("Source comparison", error);
      }
    },
    cancel: (operation, reason) =>
      engineWorkerTypeSourceCancellationIsRunning(
        facade().cancelMemberSourceComparison(
          operation.operationId,
          reason),
        reason),
  });
}
