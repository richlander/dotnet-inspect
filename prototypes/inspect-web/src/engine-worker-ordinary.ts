import {
  createOperationAuthorityPage,
  type OperationDiagnostic,
  type OperationAuthorityPage,
} from "./operation-authority.ts";
import {
  engineWorkerBoundaryErrors,
  engineWorkerDiagnostic,
  engineWorkerText,
} from "./engine-worker-contract.ts";
import type {
  WorkerRuntimeHost,
  WorkerEpochToken,
  WorkerRuntimePreparationError,
} from "./worker-runtime-core.ts";
import type {
  BoundedPayloadDecodeResult,
  BoundedPayloadDecoder,
} from "./worker-runtime-protocol.ts";
import type { WorkerOperationCatalog } from "./worker-runtime-realm.ts";

export const engineWorkerOrdinaryMaximumInputJsonCharacters = 1_048_576;
export const engineWorkerOrdinaryMaximumResultJsonCharacters = 32_000_000;

export type EngineWorkerOrdinaryMethod =
  (...arguments_: never[]) => unknown;

export type EngineWorkerAsyncFacade<TFacade> = {
  readonly [TKey in keyof TFacade]:
    TFacade[TKey] extends (...arguments_: infer TArguments) => infer TResult
      ? (...arguments_: TArguments) => Promise<Awaited<TResult>>
      : never;
};

type EngineWorkerOrdinaryResultCategory<TValue> =
  [TValue] extends [void]
    ? "void"
    : TValue extends string
      ? "string"
      : TValue extends readonly unknown[]
        ? "array"
        : TValue extends object
          ? "object"
          : never;

interface EngineWorkerOrdinaryResult<TValue> {
  readonly encode: (value: TValue) => string;
  readonly decoder: BoundedPayloadDecoder<TValue>;
}

export interface EngineWorkerOrdinaryArguments<
  TArguments extends readonly unknown[],
> {
  readonly encode: (
    arguments_: TArguments,
  ) => BoundedPayloadDecodeResult<unknown>;
  readonly decoder: BoundedPayloadDecoder<TArguments>;
}

export interface EngineWorkerOrdinaryOperation<
  TArguments extends readonly unknown[],
  TValue,
> {
  readonly kind: string;
  readonly arguments: EngineWorkerOrdinaryArguments<TArguments>;
  readonly result: EngineWorkerOrdinaryResult<TValue>;
}

function rejected(
  message: string,
  reason: "invalid" | "oversized" = "invalid",
  cause?: unknown,
): BoundedPayloadDecodeResult<never> {
  return cause === undefined
    ? { kind: "rejected", reason, message }
    : { kind: "rejected", reason, message, cause };
}

function decodeArgumentPayload(
  value: unknown,
  arity: number,
): BoundedPayloadDecodeResult<unknown[]> {
  if (typeof value !== "string")
    return rejected("Expected ordinary Worker argument JSON.");
  if (value.length > engineWorkerOrdinaryMaximumInputJsonCharacters) {
    return rejected(
      `Ordinary Worker argument JSON exceeds ${
        engineWorkerOrdinaryMaximumInputJsonCharacters
      } characters.`,
      "oversized",
    );
  }

  try {
    const parsed: unknown = JSON.parse(value);
    if (!Array.isArray(parsed) || parsed.length !== arity) {
      return rejected(
        `Expected an ordinary Worker argument tuple with ${arity} entries.`,
      );
    }
    return { kind: "decoded", value: parsed };
  } catch (error: unknown) {
    if (!(error instanceof SyntaxError)) throw error;
    return rejected("Ordinary Worker argument JSON is invalid.", "invalid", error);
  }
}

function isEmptyArgumentTuple(value: unknown[]): value is [] {
  return value.length === 0;
}

function createArguments<TArguments extends readonly unknown[]>(
  decoder: BoundedPayloadDecoder<TArguments>,
): EngineWorkerOrdinaryArguments<TArguments> {
  return {
    decoder,
    encode(arguments_) {
      let encoded: string | undefined;
      try {
        encoded = JSON.stringify(arguments_);
      } catch (error: unknown) {
        return rejected(
          "Ordinary Worker arguments are not JSON.",
          "invalid",
          error,
        );
      }
      if (encoded === undefined)
        return rejected("Ordinary Worker arguments are not JSON.");
      const validation = decoder.decode(encoded);
      if (validation.kind === "rejected") return validation;
      return { kind: "decoded", value: encoded };
    },
  };
}

export const engineWorkerTextArgument: BoundedPayloadDecoder<string> = {
  decode: value => typeof value === "string"
    ? { kind: "decoded", value }
    : rejected("Expected a text argument."),
};

export const engineWorkerNullableTextArgument:
BoundedPayloadDecoder<string | null> = {
  decode: value => value === null || typeof value === "string"
    ? { kind: "decoded", value }
    : rejected("Expected a nullable text argument."),
};

export const engineWorkerNumberArgument: BoundedPayloadDecoder<number> = {
  decode: value => typeof value === "number" && Number.isFinite(value)
    ? { kind: "decoded", value }
    : rejected("Expected a finite numeric argument."),
};

export const engineWorkerBooleanArgument: BoundedPayloadDecoder<boolean> = {
  decode: value => typeof value === "boolean"
    ? { kind: "decoded", value }
    : rejected("Expected a Boolean argument."),
};

export function engineWorkerArguments0():
EngineWorkerOrdinaryArguments<[]> {
  return createArguments<[]>({
    decode(value) {
      const payload = decodeArgumentPayload(value, 0);
      if (payload.kind === "rejected") return payload;
      if (!isEmptyArgumentTuple(payload.value)) {
        return rejected("Expected an ordinary Worker argument tuple with 0 entries.");
      }
      return { kind: "decoded", value: payload.value };
    },
  });
}

export function engineWorkerArguments1<T0>(
  argument0: BoundedPayloadDecoder<T0>,
): EngineWorkerOrdinaryArguments<[T0]> {
  return createArguments({
    decode(value) {
      const payload = decodeArgumentPayload(value, 1);
      if (payload.kind === "rejected") return payload;
      const value0 = argument0.decode(payload.value[0]);
      if (value0.kind === "rejected") return value0;
      return { kind: "decoded", value: [value0.value] };
    },
  });
}

export function engineWorkerArguments2<T0, T1>(
  argument0: BoundedPayloadDecoder<T0>,
  argument1: BoundedPayloadDecoder<T1>,
): EngineWorkerOrdinaryArguments<[T0, T1]> {
  return createArguments({
    decode(value) {
      const payload = decodeArgumentPayload(value, 2);
      if (payload.kind === "rejected") return payload;
      const value0 = argument0.decode(payload.value[0]);
      if (value0.kind === "rejected") return value0;
      const value1 = argument1.decode(payload.value[1]);
      if (value1.kind === "rejected") return value1;
      return { kind: "decoded", value: [value0.value, value1.value] };
    },
  });
}

export function engineWorkerArguments3<T0, T1, T2>(
  argument0: BoundedPayloadDecoder<T0>,
  argument1: BoundedPayloadDecoder<T1>,
  argument2: BoundedPayloadDecoder<T2>,
): EngineWorkerOrdinaryArguments<[T0, T1, T2]> {
  return createArguments({
    decode(value) {
      const payload = decodeArgumentPayload(value, 3);
      if (payload.kind === "rejected") return payload;
      const value0 = argument0.decode(payload.value[0]);
      if (value0.kind === "rejected") return value0;
      const value1 = argument1.decode(payload.value[1]);
      if (value1.kind === "rejected") return value1;
      const value2 = argument2.decode(payload.value[2]);
      if (value2.kind === "rejected") return value2;
      return {
        kind: "decoded",
        value: [value0.value, value1.value, value2.value],
      };
    },
  });
}

export function engineWorkerArguments4<T0, T1, T2, T3>(
  argument0: BoundedPayloadDecoder<T0>,
  argument1: BoundedPayloadDecoder<T1>,
  argument2: BoundedPayloadDecoder<T2>,
  argument3: BoundedPayloadDecoder<T3>,
): EngineWorkerOrdinaryArguments<[T0, T1, T2, T3]> {
  return createArguments({
    decode(value) {
      const payload = decodeArgumentPayload(value, 4);
      if (payload.kind === "rejected") return payload;
      const value0 = argument0.decode(payload.value[0]);
      if (value0.kind === "rejected") return value0;
      const value1 = argument1.decode(payload.value[1]);
      if (value1.kind === "rejected") return value1;
      const value2 = argument2.decode(payload.value[2]);
      if (value2.kind === "rejected") return value2;
      const value3 = argument3.decode(payload.value[3]);
      if (value3.kind === "rejected") return value3;
      return {
        kind: "decoded",
        value: [value0.value, value1.value, value2.value, value3.value],
      };
    },
  });
}

export function engineWorkerArguments5<T0, T1, T2, T3, T4>(
  argument0: BoundedPayloadDecoder<T0>,
  argument1: BoundedPayloadDecoder<T1>,
  argument2: BoundedPayloadDecoder<T2>,
  argument3: BoundedPayloadDecoder<T3>,
  argument4: BoundedPayloadDecoder<T4>,
): EngineWorkerOrdinaryArguments<[T0, T1, T2, T3, T4]> {
  return createArguments({
    decode(value) {
      const payload = decodeArgumentPayload(value, 5);
      if (payload.kind === "rejected") return payload;
      const value0 = argument0.decode(payload.value[0]);
      if (value0.kind === "rejected") return value0;
      const value1 = argument1.decode(payload.value[1]);
      if (value1.kind === "rejected") return value1;
      const value2 = argument2.decode(payload.value[2]);
      if (value2.kind === "rejected") return value2;
      const value3 = argument3.decode(payload.value[3]);
      if (value3.kind === "rejected") return value3;
      const value4 = argument4.decode(payload.value[4]);
      if (value4.kind === "rejected") return value4;
      return {
        kind: "decoded",
        value: [
          value0.value,
          value1.value,
          value2.value,
          value3.value,
          value4.value,
        ],
      };
    },
  });
}

export function engineWorkerArguments6<T0, T1, T2, T3, T4, T5>(
  argument0: BoundedPayloadDecoder<T0>,
  argument1: BoundedPayloadDecoder<T1>,
  argument2: BoundedPayloadDecoder<T2>,
  argument3: BoundedPayloadDecoder<T3>,
  argument4: BoundedPayloadDecoder<T4>,
  argument5: BoundedPayloadDecoder<T5>,
): EngineWorkerOrdinaryArguments<[T0, T1, T2, T3, T4, T5]> {
  return createArguments({
    decode(value) {
      const payload = decodeArgumentPayload(value, 6);
      if (payload.kind === "rejected") return payload;
      const value0 = argument0.decode(payload.value[0]);
      if (value0.kind === "rejected") return value0;
      const value1 = argument1.decode(payload.value[1]);
      if (value1.kind === "rejected") return value1;
      const value2 = argument2.decode(payload.value[2]);
      if (value2.kind === "rejected") return value2;
      const value3 = argument3.decode(payload.value[3]);
      if (value3.kind === "rejected") return value3;
      const value4 = argument4.decode(payload.value[4]);
      if (value4.kind === "rejected") return value4;
      const value5 = argument5.decode(payload.value[5]);
      if (value5.kind === "rejected") return value5;
      return {
        kind: "decoded",
        value: [
          value0.value,
          value1.value,
          value2.value,
          value3.value,
          value4.value,
          value5.value,
        ],
      };
    },
  });
}

export function engineWorkerArguments8<
  T0, T1, T2, T3, T4, T5, T6, T7,
>(
  argument0: BoundedPayloadDecoder<T0>,
  argument1: BoundedPayloadDecoder<T1>,
  argument2: BoundedPayloadDecoder<T2>,
  argument3: BoundedPayloadDecoder<T3>,
  argument4: BoundedPayloadDecoder<T4>,
  argument5: BoundedPayloadDecoder<T5>,
  argument6: BoundedPayloadDecoder<T6>,
  argument7: BoundedPayloadDecoder<T7>,
): EngineWorkerOrdinaryArguments<[T0, T1, T2, T3, T4, T5, T6, T7]> {
  return createArguments({
    decode(value) {
      const payload = decodeArgumentPayload(value, 8);
      if (payload.kind === "rejected") return payload;
      const value0 = argument0.decode(payload.value[0]);
      if (value0.kind === "rejected") return value0;
      const value1 = argument1.decode(payload.value[1]);
      if (value1.kind === "rejected") return value1;
      const value2 = argument2.decode(payload.value[2]);
      if (value2.kind === "rejected") return value2;
      const value3 = argument3.decode(payload.value[3]);
      if (value3.kind === "rejected") return value3;
      const value4 = argument4.decode(payload.value[4]);
      if (value4.kind === "rejected") return value4;
      const value5 = argument5.decode(payload.value[5]);
      if (value5.kind === "rejected") return value5;
      const value6 = argument6.decode(payload.value[6]);
      if (value6.kind === "rejected") return value6;
      const value7 = argument7.decode(payload.value[7]);
      if (value7.kind === "rejected") return value7;
      return {
        kind: "decoded",
        value: [
          value0.value,
          value1.value,
          value2.value,
          value3.value,
          value4.value,
          value5.value,
          value6.value,
          value7.value,
        ],
      };
    },
  });
}

export function engineWorkerArguments9<
  T0, T1, T2, T3, T4, T5, T6, T7, T8,
>(
  argument0: BoundedPayloadDecoder<T0>,
  argument1: BoundedPayloadDecoder<T1>,
  argument2: BoundedPayloadDecoder<T2>,
  argument3: BoundedPayloadDecoder<T3>,
  argument4: BoundedPayloadDecoder<T4>,
  argument5: BoundedPayloadDecoder<T5>,
  argument6: BoundedPayloadDecoder<T6>,
  argument7: BoundedPayloadDecoder<T7>,
  argument8: BoundedPayloadDecoder<T8>,
): EngineWorkerOrdinaryArguments<[T0, T1, T2, T3, T4, T5, T6, T7, T8]> {
  return createArguments({
    decode(value) {
      const payload = decodeArgumentPayload(value, 9);
      if (payload.kind === "rejected") return payload;
      const value0 = argument0.decode(payload.value[0]);
      if (value0.kind === "rejected") return value0;
      const value1 = argument1.decode(payload.value[1]);
      if (value1.kind === "rejected") return value1;
      const value2 = argument2.decode(payload.value[2]);
      if (value2.kind === "rejected") return value2;
      const value3 = argument3.decode(payload.value[3]);
      if (value3.kind === "rejected") return value3;
      const value4 = argument4.decode(payload.value[4]);
      if (value4.kind === "rejected") return value4;
      const value5 = argument5.decode(payload.value[5]);
      if (value5.kind === "rejected") return value5;
      const value6 = argument6.decode(payload.value[6]);
      if (value6.kind === "rejected") return value6;
      const value7 = argument7.decode(payload.value[7]);
      if (value7.kind === "rejected") return value7;
      const value8 = argument8.decode(payload.value[8]);
      if (value8.kind === "rejected") return value8;
      return {
        kind: "decoded",
        value: [
          value0.value,
          value1.value,
          value2.value,
          value3.value,
          value4.value,
          value5.value,
          value6.value,
          value7.value,
          value8.value,
        ],
      };
    },
  });
}

export function engineWorkerArguments10<
  T0, T1, T2, T3, T4, T5, T6, T7, T8, T9,
>(
  argument0: BoundedPayloadDecoder<T0>,
  argument1: BoundedPayloadDecoder<T1>,
  argument2: BoundedPayloadDecoder<T2>,
  argument3: BoundedPayloadDecoder<T3>,
  argument4: BoundedPayloadDecoder<T4>,
  argument5: BoundedPayloadDecoder<T5>,
  argument6: BoundedPayloadDecoder<T6>,
  argument7: BoundedPayloadDecoder<T7>,
  argument8: BoundedPayloadDecoder<T8>,
  argument9: BoundedPayloadDecoder<T9>,
): EngineWorkerOrdinaryArguments<
  [T0, T1, T2, T3, T4, T5, T6, T7, T8, T9]
> {
  return createArguments({
    decode(value) {
      const payload = decodeArgumentPayload(value, 10);
      if (payload.kind === "rejected") return payload;
      const value0 = argument0.decode(payload.value[0]);
      if (value0.kind === "rejected") return value0;
      const value1 = argument1.decode(payload.value[1]);
      if (value1.kind === "rejected") return value1;
      const value2 = argument2.decode(payload.value[2]);
      if (value2.kind === "rejected") return value2;
      const value3 = argument3.decode(payload.value[3]);
      if (value3.kind === "rejected") return value3;
      const value4 = argument4.decode(payload.value[4]);
      if (value4.kind === "rejected") return value4;
      const value5 = argument5.decode(payload.value[5]);
      if (value5.kind === "rejected") return value5;
      const value6 = argument6.decode(payload.value[6]);
      if (value6.kind === "rejected") return value6;
      const value7 = argument7.decode(payload.value[7]);
      if (value7.kind === "rejected") return value7;
      const value8 = argument8.decode(payload.value[8]);
      if (value8.kind === "rejected") return value8;
      const value9 = argument9.decode(payload.value[9]);
      if (value9.kind === "rejected") return value9;
      return {
        kind: "decoded",
        value: [
          value0.value,
          value1.value,
          value2.value,
          value3.value,
          value4.value,
          value5.value,
          value6.value,
          value7.value,
          value8.value,
          value9.value,
        ],
      };
    },
  });
}

export function engineWorkerArguments11<
  T0, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10,
>(
  argument0: BoundedPayloadDecoder<T0>,
  argument1: BoundedPayloadDecoder<T1>,
  argument2: BoundedPayloadDecoder<T2>,
  argument3: BoundedPayloadDecoder<T3>,
  argument4: BoundedPayloadDecoder<T4>,
  argument5: BoundedPayloadDecoder<T5>,
  argument6: BoundedPayloadDecoder<T6>,
  argument7: BoundedPayloadDecoder<T7>,
  argument8: BoundedPayloadDecoder<T8>,
  argument9: BoundedPayloadDecoder<T9>,
  argument10: BoundedPayloadDecoder<T10>,
): EngineWorkerOrdinaryArguments<
  [T0, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10]
> {
  return createArguments({
    decode(value) {
      const payload = decodeArgumentPayload(value, 11);
      if (payload.kind === "rejected") return payload;
      const value0 = argument0.decode(payload.value[0]);
      if (value0.kind === "rejected") return value0;
      const value1 = argument1.decode(payload.value[1]);
      if (value1.kind === "rejected") return value1;
      const value2 = argument2.decode(payload.value[2]);
      if (value2.kind === "rejected") return value2;
      const value3 = argument3.decode(payload.value[3]);
      if (value3.kind === "rejected") return value3;
      const value4 = argument4.decode(payload.value[4]);
      if (value4.kind === "rejected") return value4;
      const value5 = argument5.decode(payload.value[5]);
      if (value5.kind === "rejected") return value5;
      const value6 = argument6.decode(payload.value[6]);
      if (value6.kind === "rejected") return value6;
      const value7 = argument7.decode(payload.value[7]);
      if (value7.kind === "rejected") return value7;
      const value8 = argument8.decode(payload.value[8]);
      if (value8.kind === "rejected") return value8;
      const value9 = argument9.decode(payload.value[9]);
      if (value9.kind === "rejected") return value9;
      const value10 = argument10.decode(payload.value[10]);
      if (value10.kind === "rejected") return value10;
      return {
        kind: "decoded",
        value: [
          value0.value,
          value1.value,
          value2.value,
          value3.value,
          value4.value,
          value5.value,
          value6.value,
          value7.value,
          value8.value,
          value9.value,
          value10.value,
        ],
      };
    },
  });
}

function validateGeneratedResultCategory(
  value: unknown,
  category: "void" | "string" | "array" | "object",
): boolean {
  if (category === "void") return value === undefined;
  if (category === "string") return typeof value === "string";
  if (category === "array") return Array.isArray(value);
  return typeof value === "object" && value !== null && !Array.isArray(value);
}

function validateSerializedResultCategory(
  value: unknown,
  category: "void" | "string" | "array" | "object",
): boolean {
  return category === "void"
    ? value === null
    : validateGeneratedResultCategory(value, category);
}

// Same-build generated DTOs own their nested schema. This predicate is the
// bounded, top-level-validated generated-result transport boundary.
function isGeneratedResultValue<TValue>(
  value: unknown,
  category: EngineWorkerOrdinaryResultCategory<TValue>,
): value is TValue {
  if (category === "void") return value === undefined;
  return validateGeneratedResultCategory(value, category);
}

function createGeneratedResult<TValue>(
  category: EngineWorkerOrdinaryResultCategory<TValue>,
): EngineWorkerOrdinaryResult<TValue> {
  return {
    encode(value) {
      if (!validateGeneratedResultCategory(value, category))
        throw new Error(`Expected an ordinary Worker ${category} result.`);
      let encoded: string | undefined;
      try {
        encoded = JSON.stringify(category === "void" ? null : value);
      } catch (error: unknown) {
        throw new Error("Ordinary Worker result is not JSON.", { cause: error });
      }
      if (encoded === undefined)
        throw new Error("Ordinary Worker result is not JSON.");
      if (encoded.length > engineWorkerOrdinaryMaximumResultJsonCharacters) {
        throw new Error(
          `Ordinary Worker result JSON exceeds ${
            engineWorkerOrdinaryMaximumResultJsonCharacters
          } characters.`,
        );
      }
      return encoded;
    },
    decoder: {
      decode(value) {
        if (typeof value !== "string")
          return rejected("Expected ordinary Worker result JSON.");
        if (value.length > engineWorkerOrdinaryMaximumResultJsonCharacters) {
          return rejected(
            `Ordinary Worker result JSON exceeds ${
              engineWorkerOrdinaryMaximumResultJsonCharacters
            } characters.`,
            "oversized",
          );
        }
        try {
          const parsed: unknown = JSON.parse(value);
          if (!validateSerializedResultCategory(parsed, category))
            return rejected(`Expected an ordinary Worker ${category} result.`);
          const result: unknown = category === "void" ? undefined : parsed;
          if (!isGeneratedResultValue<TValue>(result, category))
            return rejected(`Expected an ordinary Worker ${category} result.`);
          return { kind: "decoded", value: result };
        } catch (error: unknown) {
          if (!(error instanceof SyntaxError)) throw error;
          return rejected(
            "Ordinary Worker result JSON is invalid.",
            "invalid",
            error,
          );
        }
      },
    },
  };
}

export function defineEngineWorkerOrdinaryOperation<
  TMethod extends EngineWorkerOrdinaryMethod,
>(
  kind: string,
  arguments_: EngineWorkerOrdinaryArguments<Parameters<TMethod>>,
  resultCategory: EngineWorkerOrdinaryResultCategory<
    Awaited<ReturnType<TMethod>>
  >,
): EngineWorkerOrdinaryOperation<
  Parameters<TMethod>,
  Awaited<ReturnType<TMethod>>
> {
  return {
    kind,
    arguments: arguments_,
    result: createGeneratedResult(resultCategory),
  };
}

export function engineWorkerOperationKinds(
  ...operations: readonly { readonly kind: string }[]
): readonly string[] {
  return operations.map(operation => operation.kind);
}

const noProgress: BoundedPayloadDecoder<never> = {
  decode: () => rejected("Ordinary Worker operations do not publish progress."),
};

export function registerEngineWorkerOrdinaryOperation<
  TArguments extends readonly unknown[],
  TValue,
>(
  operations: WorkerOperationCatalog,
  operation: EngineWorkerOrdinaryOperation<TArguments, TValue>,
  invoke: (
    ...arguments_: TArguments
  ) => TValue | PromiseLike<TValue>,
): void {
  operations.register({
    kind: operation.kind,
    allowance: { kind: "unbounded" },
    input: operation.arguments.decoder,
    rejectInvalidPayload: failure => ({
      error: failure.message,
      diagnostic: failure.message,
    }),
    async invoke(arguments_) {
      try {
        const value = await invoke(...arguments_);
        return {
          kind: "succeeded",
          value: operation.result.encode(value),
        };
      } catch (error: unknown) {
        const message = engineWorkerDiagnostic(error);
        return {
          kind: "failed",
          failureKind: "unexpected",
          error: message,
          diagnostic: message,
        };
      }
    },
  });
}

function preparationFailureMessage(
  operationKind: string,
  error: WorkerRuntimePreparationError,
): string {
  return error.kind === "payload-rejected"
    ? `${operationKind} could not start: ${error.message}`
    : `${operationKind} could not start: ${error.kind}.`;
}

export interface EngineWorkerOrdinaryBinder {
  bind<TArguments extends readonly unknown[], TValue>(
    operation: EngineWorkerOrdinaryOperation<TArguments, TValue>,
  ): (...arguments_: TArguments) => Promise<TValue>;
}

interface EngineWorkerBindingPage {
  readonly epoch: WorkerEpochToken;
  readonly page: OperationAuthorityPage;
}

const bindingPages =
  new WeakMap<WorkerRuntimeHost<string, string>, EngineWorkerBindingPage>();

export function setEngineWorkerBindingPage(
  host: WorkerRuntimeHost<string, string>,
  epoch: WorkerEpochToken,
  page: OperationAuthorityPage,
): void {
  const existing = bindingPages.get(host);
  if (existing !== undefined
    && (existing.epoch !== epoch || existing.page !== page)) {
    throw new Error(
      "The Worker epoch already has a different operation-authority page.");
  }
  bindingPages.set(host, { epoch, page });
}

export function engineWorkerBindingPage(
  host: WorkerRuntimeHost<string, string>,
  epoch: WorkerEpochToken,
): OperationAuthorityPage {
  const existing = bindingPages.get(host);
  if (existing !== undefined && existing.epoch === epoch)
    return existing.page;
  const page = createOperationAuthorityPage();
  bindingPages.set(host, { epoch, page });
  return page;
}

export function createEngineWorkerOrdinaryBinder(
  host: WorkerRuntimeHost<string, string>,
  reportDiagnostic: (diagnostic: OperationDiagnostic) => undefined,
): EngineWorkerOrdinaryBinder {
  const epoch = host.snapshot().epochToken;
  if (epoch === null)
    throw new Error("Start a Worker epoch before binding ordinary operations.");
  const page = engineWorkerBindingPage(host, epoch);

  return {
    bind<TArguments extends readonly unknown[], TValue>(
      operation: EngineWorkerOrdinaryOperation<TArguments, TValue>,
    ): (...arguments_: TArguments) => Promise<TValue> {
      const adapter = host.registerOperation({
        kind: operation.kind,
        allowance: { kind: "unbounded" },
        encodeInput: operation.arguments.encode,
        value: operation.result.decoder,
        error: engineWorkerText,
        diagnostic: engineWorkerText,
        progress: noProgress,
        mapPreparationError: error => error,
        boundaryErrors: engineWorkerBoundaryErrors,
      });

      return async (...arguments_: TArguments): Promise<TValue> => {
        if (host.snapshot().epochToken !== epoch) {
          throw new Error(
            `${operation.kind} client belongs to a closed Worker epoch.`,
          );
        }
        const session = page.createSession<
          TArguments,
          TValue,
          string,
          string,
          WorkerRuntimePreparationError
        >({
          feature: { publish: () => undefined },
          diagnostic: { report: reportDiagnostic },
        });
        try {
          const started = session.start(arguments_, adapter);
          if (started.kind === "rejected") {
            if (started.reason.kind === "producer-rejected") {
              throw new Error(preparationFailureMessage(
                operation.kind,
                started.reason.error,
              ));
            }
            throw new Error(
              `${operation.kind} could not start: ${started.reason.kind}.`,
            );
          }
          const outcome = await started.handle.outcome;
          if (outcome.kind === "succeeded") return outcome.value;
          if (outcome.kind === "failed") throw new Error(outcome.error);
          throw new Error(
            `${operation.kind} canceled: ${outcome.reason}.`,
          );
        } finally {
          session.dispose();
        }
      };
    },
  };
}
