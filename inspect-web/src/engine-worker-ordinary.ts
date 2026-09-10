import type * as AnalysisFacadeModule from "./facades/inspect-web-analysis.d.ts";
import type * as CallGraphFacadeModule from "./facades/inspect-web-call-graph.d.ts";
import type * as CatalogFacadeModule from "./facades/inspect-web-catalog.d.ts";
import type * as MetadataFacadeModule from "./facades/inspect-web-metadata.d.ts";
import type * as PackageFacadeModule from "./facades/inspect-web-package.d.ts";
import type * as SourceFacadeModule from "./facades/inspect-web-source.d.ts";
import {
  createOperationAuthorityPage,
  type OperationAuthorityPage,
  type OperationDiagnostic,
} from "./operation-authority.ts";
import {
  engineWorkerBoundaryErrors,
  engineWorkerDiagnostic,
  engineWorkerText,
} from "./engine-worker-contract.ts";
import type {
  WorkerEpochToken,
  WorkerRuntimeHost,
  WorkerRuntimePreparationError,
} from "./worker-runtime-core.ts";
import type {
  BoundedPayloadDecodeResult,
  BoundedPayloadDecoder,
} from "./worker-runtime-protocol.ts";
import type { WorkerOperationCatalog } from "./worker-runtime-realm.ts";

type AnalysisFacade = typeof AnalysisFacadeModule;
type CallGraphFacade = typeof CallGraphFacadeModule;
type CatalogFacade = typeof CatalogFacadeModule;
type MetadataFacade = typeof MetadataFacadeModule;
type PackageFacade = typeof PackageFacadeModule;
type SourceFacade = typeof SourceFacadeModule;

type PackageOperationName =
  | "getPlatformCatalog"
  | "getPlatformVersions"
  | "listPackageAssemblyQueryPatterns"
  | "matchPackageDependencyCoordinate"
  | "searchTypes"
  | "activateWorkspacePackageOccurrence"
  | "clearWorkspacePackageOccurrences"
  | "packageCacheStats"
  | "prefetchPlatformPacks"
  | "queryPackage"
  | "openPackageAssemblyQueryResult"
  | "loadRuntimePack"
  | "loadRuntimePackAssembly"
  | "getPackageDocument"
  | "queryMemberDocumentation"
  | "queryPackageDependencies"
  | "queryPackageVersions"
  | "queryWorkspacePackageOccurrences"
  | "resolvePackageDependencyVersion";

type MetadataOperationName =
  | "queryTypeProjection"
  | "queryPackageMetadataTable"
  | "queryPlatformMetadataTable"
  | "queryPackageHeapEntries"
  | "queryPlatformHeapEntries"
  | "queryPackageMetadata"
  | "queryPlatformMetadata"
  | "queryGraphMemberSurface"
  | "queryGraphMemberSurfaceByMethodAddress";

type AnalysisOperationName =
  | "queryMemberFacts"
  | "queryPackageIntegrations"
  | "queryPlatformIntegrations"
  | "queryPackageOpportunities"
  | "queryPlatformOpportunities"
  | "queryPackagePerformance"
  | "queryPlatformPerformance";

type SourceOperationName =
  | "queryMemberSource"
  | "queryTypeMemberSource"
  | "cancelSourceQuery"
  | "queryMethodBodyComparisonTargets"
  | "queryMethodBodyComparison"
  | "cancelMethodBodyComparison"
  | "queryMemberSourceComparison"
  | "cancelMemberSourceComparison"
  | "queryMemberFindingCensus";

type CallGraphOperationName =
  | "queryMemberCallGraph"
  | "expandPlatformCallGraph";

type CatalogOperationName =
  | "resolveHomeDemo"
  | "decodeWorkspaceShareState"
  | "encodeWorkspaceShareState"
  | "runHomeDemo";

type AsyncMethod<TMethod> =
  TMethod extends (...args: infer TArgs) => infer TResult
    ? (...args: TArgs) => Promise<Awaited<TResult>>
    : never;

type AsyncFacadeGroup<TFacade, TName extends keyof TFacade> = {
  readonly [TMember in TName]: AsyncMethod<TFacade[TMember]>;
};

export interface EngineWorkerOrdinaryFacades {
  readonly package: Pick<PackageFacade, PackageOperationName>;
  readonly metadata: Pick<MetadataFacade, MetadataOperationName>;
  readonly analysis: Pick<AnalysisFacade, AnalysisOperationName>;
  readonly source: Pick<SourceFacade, SourceOperationName>;
  readonly callGraph: Pick<CallGraphFacade, CallGraphOperationName>;
  readonly catalog: Pick<CatalogFacade, CatalogOperationName>;
}

export interface EngineWorkerOrdinaryClient {
  readonly package: AsyncFacadeGroup<PackageFacade, PackageOperationName>;
  readonly metadata: AsyncFacadeGroup<MetadataFacade, MetadataOperationName>;
  readonly analysis: AsyncFacadeGroup<AnalysisFacade, AnalysisOperationName>;
  readonly source: AsyncFacadeGroup<SourceFacade, SourceOperationName>;
  readonly callGraph: AsyncFacadeGroup<CallGraphFacade, CallGraphOperationName>;
  readonly catalog: AsyncFacadeGroup<CatalogFacade, CatalogOperationName>;
}

export const engineWorkerOrdinaryMaximumJsonCharacters = 8_388_608;
export const engineWorkerOrdinaryMaximumNesting = 64;
export const engineWorkerOrdinaryMaximumCollectionEntries = 262_144;

type JsonPrimitive = null | boolean | number | string;
type JsonValue = JsonPrimitive | JsonValue[] | { [name: string]: JsonValue };
type ResultKind = "value" | "void";

const voidMarkerName = "$engineWorkerOrdinary";
const voidMarkerValue = "void";
const parseJson: (text: string) => unknown = JSON.parse;

class OrdinaryPayloadError extends Error {
  readonly reason: "invalid" | "oversized";

  constructor(
    message: string,
    reason: "invalid" | "oversized" = "invalid",
  ) {
    super(message);
    this.reason = reason;
  }
}

interface JsonBudget {
  remainingEntries: number;
  readonly ancestors: Set<object>;
}

function consumeEntries(budget: JsonBudget, count: number): void {
  budget.remainingEntries -= count;
  if (budget.remainingEntries < 0) {
    throw new OrdinaryPayloadError(
      `Ordinary Worker JSON exceeds ${
        engineWorkerOrdinaryMaximumCollectionEntries
      } collection entries.`,
      "oversized",
    );
  }
}

function dataProperty(
  value: object,
  name: string,
  description: string,
): PropertyDescriptor {
  const property = Object.getOwnPropertyDescriptor(value, name);
  if (property === undefined || !("value" in property)) {
    throw new OrdinaryPayloadError(
      `${description}.${name} must be an own data property.`,
    );
  }
  if (!property.enumerable) {
    throw new OrdinaryPayloadError(
      `${description}.${name} must be enumerable.`,
    );
  }
  return property;
}

function copyJsonValue(
  value: unknown,
  description: string,
  budget: JsonBudget,
  depth: number,
): JsonValue {
  if (depth > engineWorkerOrdinaryMaximumNesting) {
    throw new OrdinaryPayloadError(
      "Ordinary Worker JSON exceeds 64 levels of nesting.",
      "oversized",
    );
  }
  if (value === null
    || typeof value === "boolean"
    || typeof value === "string") {
    if (typeof value === "string"
      && value.length > engineWorkerOrdinaryMaximumJsonCharacters) {
      throw new OrdinaryPayloadError(
        "Ordinary Worker JSON contains an oversized string.",
        "oversized",
      );
    }
    return value;
  }
  if (typeof value === "number") {
    if (!Number.isFinite(value)) {
      throw new OrdinaryPayloadError(
        `${description} contains a non-finite number.`,
      );
    }
    return value;
  }
  if (typeof value !== "object") {
    throw new OrdinaryPayloadError(
      `${description} contains non-JSON ${typeof value} data.`,
    );
  }
  if (budget.ancestors.has(value)) {
    throw new OrdinaryPayloadError(
      `${description} contains a cycle.`,
    );
  }

  budget.ancestors.add(value);
  try {
    if (Array.isArray(value)) {
      if (Reflect.getPrototypeOf(value) !== Array.prototype) {
        throw new OrdinaryPayloadError(
          `${description} must be an ordinary array.`,
        );
      }
      consumeEntries(budget, value.length + 1);
      const keys = Reflect.ownKeys(value);
      if (keys.length !== value.length + 1
        || !keys.every(key =>
          key === "length"
          || (typeof key === "string"
            && Number.isSafeInteger(Number(key))
            && String(Number(key)) === key
            && Number(key) >= 0
            && Number(key) < value.length))) {
        throw new OrdinaryPayloadError(
          `${description} must be a closed dense array.`,
        );
      }
      const result: JsonValue[] = [];
      Object.setPrototypeOf(result, null);
      for (let index = 0; index < value.length; index++) {
        const property = dataProperty(
          value,
          String(index),
          description,
        );
        result[index] = copyJsonValue(
          property.value,
          `${description}[${index}]`,
          budget,
          depth + 1,
        );
      }
      return result;
    }

    const prototype = Reflect.getPrototypeOf(value);
    if (prototype !== Object.prototype && prototype !== null) {
      throw new OrdinaryPayloadError(
        `${description} must be an ordinary data object.`,
      );
    }
    const keys = Reflect.ownKeys(value);
    if (!keys.every(key => typeof key === "string")) {
      throw new OrdinaryPayloadError(
        `${description} contains symbol keys.`,
      );
    }
    consumeEntries(budget, keys.length + 1);
    const result: { [name: string]: JsonValue } = {};
    Object.setPrototypeOf(result, null);
    for (const key of keys) {
      if (typeof key !== "string") {
        throw new OrdinaryPayloadError(
          `${description} contains symbol keys.`,
        );
      }
      const property = dataProperty(value, key, description);
      result[key] = copyJsonValue(
        property.value,
        `${description}.${key}`,
        budget,
        depth + 1,
      );
    }
    return result;
  } finally {
    budget.ancestors.delete(value);
  }
}

function jsonBudget(): JsonBudget {
  return {
    remainingEntries: engineWorkerOrdinaryMaximumCollectionEntries,
    ancestors: new Set<object>(),
  };
}

function encodeTransportTuple(
  tuple: readonly unknown[],
  description: string,
): string {
  const safe = copyJsonValue(tuple, description, jsonBudget(), 0);
  const encoded = JSON.stringify(safe);
  if (encoded === undefined) {
    throw new OrdinaryPayloadError(
      `${description} is not JSON.`,
    );
  }
  if (encoded.length > engineWorkerOrdinaryMaximumJsonCharacters) {
    throw new OrdinaryPayloadError(
      `${description} JSON exceeds ${
        engineWorkerOrdinaryMaximumJsonCharacters
      } characters.`,
      "oversized",
    );
  }
  return encoded;
}

function decodeTransportTuple(
  value: unknown,
  expectedLength: number,
  description: string,
): readonly unknown[] {
  if (typeof value !== "string") {
    throw new OrdinaryPayloadError(
      `Expected ${description} JSON text.`,
    );
  }
  if (value.length > engineWorkerOrdinaryMaximumJsonCharacters) {
    throw new OrdinaryPayloadError(
      `${description} JSON exceeds ${
        engineWorkerOrdinaryMaximumJsonCharacters
      } characters.`,
      "oversized",
    );
  }
  let parsed: unknown;
  try {
    parsed = parseJson(value);
  } catch (error: unknown) {
    if (!(error instanceof SyntaxError)) throw error;
    throw new OrdinaryPayloadError(
      `${description} JSON is malformed.`,
    );
  }
  copyJsonValue(parsed, description, jsonBudget(), 0);
  if (!Array.isArray(parsed) || parsed.length !== expectedLength) {
    throw new OrdinaryPayloadError(
      `${description} must be a ${expectedLength}-element JSON array.`,
    );
  }
  return parsed;
}

function rejectedPayload(
  error: OrdinaryPayloadError,
): BoundedPayloadDecodeResult<never> {
  return {
    kind: "rejected",
    reason: error.reason,
    message: error.message,
    cause: error,
  };
}

function createInputDecoder<TArgs extends readonly unknown[]>(
  argumentCount: number,
): BoundedPayloadDecoder<TArgs> {
  return {
    decode(value) {
      try {
        const tuple = decodeTransportTuple(
          value,
          argumentCount,
          "Ordinary Worker argument tuple",
        );
        // Complete JSON-tree and tuple-length validation establishes TArgs.
        // oxlint-disable-next-line typescript/no-unsafe-type-assertion
        return { kind: "decoded", value: tuple as TArgs };
      } catch (error: unknown) {
        if (!(error instanceof OrdinaryPayloadError)) throw error;
        return rejectedPayload(error);
      }
    },
  };
}

function encodeInputTuple(
  input: readonly unknown[],
): BoundedPayloadDecodeResult<unknown> {
  try {
    return {
      kind: "decoded",
      value: encodeTransportTuple(
        input,
        "Ordinary Worker argument tuple",
      ),
    };
  } catch (error: unknown) {
    if (!(error instanceof OrdinaryPayloadError)) throw error;
    return rejectedPayload(error);
  }
}

function createValueDecoder<TResult>(): BoundedPayloadDecoder<TResult> {
  return {
    decode(value) {
      try {
        const tuple = decodeTransportTuple(
          value,
          1,
          "Ordinary Worker result tuple",
        );
        // Complete JSON-tree and tuple-length validation establishes the
        // generated facade result type without rebuilding its DTO.
        // oxlint-disable-next-line typescript/no-unsafe-type-assertion
        return { kind: "decoded", value: tuple[0] as TResult };
      } catch (error: unknown) {
        if (!(error instanceof OrdinaryPayloadError)) throw error;
        return rejectedPayload(error);
      }
    },
  };
}

function isVoidMarker(value: unknown): boolean {
  if (typeof value !== "object" || value === null || Array.isArray(value))
    return false;
  const keys = Reflect.ownKeys(value);
  if (keys.length !== 1 || keys[0] !== voidMarkerName) return false;
  const marker = Object.getOwnPropertyDescriptor(value, voidMarkerName);
  return marker !== undefined
    && "value" in marker
    && marker.value === voidMarkerValue;
}

const voidValueDecoder: BoundedPayloadDecoder<void> = {
  decode(value) {
    try {
      const tuple = decodeTransportTuple(
        value,
        1,
        "Ordinary Worker result tuple",
      );
      if (!isVoidMarker(tuple[0])) {
        throw new OrdinaryPayloadError(
          "Ordinary Worker void result marker is invalid.",
        );
      }
      return { kind: "decoded", value: undefined };
    } catch (error: unknown) {
      if (!(error instanceof OrdinaryPayloadError)) throw error;
      return rejectedPayload(error);
    }
  },
};

const noProgress: BoundedPayloadDecoder<never> = {
  decode() {
    return {
      kind: "rejected",
      reason: "invalid",
      message: "Ordinary Worker operations do not publish progress.",
    };
  },
};

function encodeResultTuple(value: unknown, resultKind: ResultKind): string {
  if (resultKind === "void") {
    if (value !== undefined) {
      throw new OrdinaryPayloadError(
        "Ordinary Worker void operation returned a value.",
      );
    }
    return encodeTransportTuple(
      [{ [voidMarkerName]: voidMarkerValue }],
      "Ordinary Worker result tuple",
    );
  }
  return encodeTransportTuple(
    [value],
    "Ordinary Worker result tuple",
  );
}

interface ErasedOrdinaryOperation {
  readonly kind: string;
  registerWorker(
    operations: WorkerOperationCatalog,
    facades: () => EngineWorkerOrdinaryFacades,
  ): void;
}

export interface EngineWorkerOrdinaryOperation<
  TArgs extends readonly unknown[],
  TResult,
> extends ErasedOrdinaryOperation {
  readonly argumentCount: number;
  readonly resultKind: ResultKind;
  readonly input: BoundedPayloadDecoder<TArgs>;
  readonly value: BoundedPayloadDecoder<TResult>;
  encodeInput(input: TArgs): BoundedPayloadDecodeResult<unknown>;
  bindPage(
    host: WorkerRuntimeHost<string, string>,
    page: OperationAuthorityPage,
    epoch: WorkerEpochToken,
    reportDiagnostic: (diagnostic: OperationDiagnostic) => undefined,
  ): (...args: TArgs) => Promise<TResult>;
}

function preparationFailureMessage(
  error: WorkerRuntimePreparationError,
): string {
  if (error.kind === "payload-rejected")
    return `Ordinary Worker input was rejected: ${error.message}`;
  return `Ordinary Worker operation could not start: ${error.kind}.`;
}

function createOrdinaryOperation<
  TArgs extends readonly unknown[],
  TResult,
>(
  kind: string,
  argumentCount: TArgs["length"],
  resultKind: ResultKind,
  value: BoundedPayloadDecoder<TResult>,
  invoke: (
    facades: EngineWorkerOrdinaryFacades,
    ...args: TArgs
  ) => TResult | PromiseLike<TResult>,
): EngineWorkerOrdinaryOperation<TArgs, TResult> {
  const input = createInputDecoder<TArgs>(argumentCount);
  return {
    kind,
    argumentCount,
    resultKind,
    input,
    value,
    encodeInput: inputValue => encodeInputTuple(inputValue),
    registerWorker(operations, facades) {
      operations.register({
        kind,
        allowance: { kind: "unbounded" },
        input,
        rejectInvalidPayload: failure => ({
          error: failure.message,
          diagnostic: failure.message,
        }),
        async invoke(args) {
          let result: TResult;
          try {
            result = await invoke(facades(), ...args);
          } catch (error: unknown) {
            const message = engineWorkerDiagnostic(error);
            return {
              kind: "failed",
              failureKind: "unexpected",
              error: message,
              diagnostic: message,
            };
          }
          try {
            return {
              kind: "succeeded",
              value: encodeResultTuple(result, resultKind),
            };
          } catch (error: unknown) {
            if (!(error instanceof OrdinaryPayloadError)) throw error;
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
    },
    bindPage(host, page, epoch, reportDiagnostic) {
      const adapter = host.registerOperation({
        kind,
        allowance: { kind: "unbounded" },
        encodeInput: encodeInputTuple,
        value,
        error: engineWorkerText,
        diagnostic: engineWorkerText,
        progress: noProgress,
        mapPreparationError: error => error,
        boundaryErrors: engineWorkerBoundaryErrors,
      });
      return async (...args: TArgs): Promise<TResult> => {
        if (host.snapshot().epochToken !== epoch) {
          throw new Error(
            "Ordinary Worker client belongs to a closed Worker epoch.",
          );
        }
        const session = page.createSession<
          TArgs,
          TResult,
          string,
          never,
          WorkerRuntimePreparationError
        >({
          feature: { publish: () => undefined },
          diagnostic: { report: reportDiagnostic },
        });
        try {
          const started = session.start(args, adapter);
          if (started.kind === "rejected") {
            if (started.reason.kind === "producer-rejected") {
              throw new Error(
                preparationFailureMessage(started.reason.error),
              );
            }
            throw new Error(
              `Ordinary Worker operation could not start: `
                + `${started.reason.kind}.`,
            );
          }
          const outcome = await started.handle.outcome;
          if (outcome.kind === "succeeded") return outcome.value;
          if (outcome.kind === "failed") throw new Error(outcome.error);
          throw new Error(
            `Ordinary Worker operation canceled: ${outcome.reason}.`,
          );
        } finally {
          session.dispose();
        }
      };
    },
  };
}

function valueOperation<
  TArgs extends readonly unknown[],
  TRawResult,
>(
  kind: string,
  argumentCount: TArgs["length"],
  invoke: (
    facades: EngineWorkerOrdinaryFacades,
    ...args: TArgs
  ) => TRawResult,
): EngineWorkerOrdinaryOperation<TArgs, Awaited<TRawResult>> {
  return createOrdinaryOperation<TArgs, Awaited<TRawResult>>(
    kind,
    argumentCount,
    "value",
    createValueDecoder<Awaited<TRawResult>>(),
    (facades, ...args) => Promise.resolve(invoke(facades, ...args)),
  );
}

function voidOperation<TArgs extends readonly unknown[]>(
  kind: string,
  argumentCount: TArgs["length"],
  invoke: (
    facades: EngineWorkerOrdinaryFacades,
    ...args: TArgs
  ) => void | PromiseLike<void>,
): EngineWorkerOrdinaryOperation<TArgs, void> {
  return createOrdinaryOperation(
    kind,
    argumentCount,
    "void",
    voidValueDecoder,
    invoke,
  );
}

export const engineWorkerOrdinaryOperations = {
  package: {
    getPlatformCatalog: valueOperation(
      "ordinary-package-get-platform-catalog",
      2,
      (
        facades,
        ...args: Parameters<PackageFacade["getPlatformCatalog"]>
      ) => facades.package.getPlatformCatalog(...args),
    ),
    getPlatformVersions: valueOperation(
      "ordinary-package-get-platform-versions",
      1,
      (
        facades,
        ...args: Parameters<PackageFacade["getPlatformVersions"]>
      ) => facades.package.getPlatformVersions(...args),
    ),
    listPackageAssemblyQueryPatterns: valueOperation(
      "ordinary-package-list-assembly-query-patterns",
      0,
      (
        facades,
        ...args: Parameters<
          PackageFacade["listPackageAssemblyQueryPatterns"]
        >
      ) => facades.package.listPackageAssemblyQueryPatterns(...args),
    ),
    matchPackageDependencyCoordinate: valueOperation(
      "ordinary-package-match-dependency-coordinate",
      3,
      (
        facades,
        ...args: Parameters<
          PackageFacade["matchPackageDependencyCoordinate"]
        >
      ) => facades.package.matchPackageDependencyCoordinate(...args),
    ),
    searchTypes: valueOperation(
      "ordinary-package-search-types",
      2,
      (
        facades,
        ...args: Parameters<PackageFacade["searchTypes"]>
      ) => facades.package.searchTypes(...args),
    ),
    activateWorkspacePackageOccurrence: valueOperation(
      "ordinary-package-activate-workspace-occurrence",
      1,
      (
        facades,
        ...args: Parameters<
          PackageFacade["activateWorkspacePackageOccurrence"]
        >
      ) => facades.package.activateWorkspacePackageOccurrence(...args),
    ),
    clearWorkspacePackageOccurrences: voidOperation(
      "ordinary-package-clear-workspace-occurrences",
      0,
      (
        facades,
        ...args: Parameters<
          PackageFacade["clearWorkspacePackageOccurrences"]
        >
      ) => facades.package.clearWorkspacePackageOccurrences(...args),
    ),
    packageCacheStats: valueOperation(
      "ordinary-package-cache-stats",
      0,
      (
        facades,
        ...args: Parameters<PackageFacade["packageCacheStats"]>
      ) => facades.package.packageCacheStats(...args),
    ),
    prefetchPlatformPacks: voidOperation(
      "ordinary-package-prefetch-platform-packs",
      2,
      (
        facades,
        ...args: Parameters<PackageFacade["prefetchPlatformPacks"]>
      ) => facades.package.prefetchPlatformPacks(...args),
    ),
    queryPackage: valueOperation(
      "ordinary-package-query-package",
      3,
      (
        facades,
        ...args: Parameters<PackageFacade["queryPackage"]>
      ) => facades.package.queryPackage(...args),
    ),
    openPackageAssemblyQueryResult: valueOperation(
      "ordinary-package-open-assembly-query-result",
      1,
      (
        facades,
        ...args: Parameters<
          PackageFacade["openPackageAssemblyQueryResult"]
        >
      ) => facades.package.openPackageAssemblyQueryResult(...args),
    ),
    loadRuntimePack: valueOperation(
      "ordinary-package-load-runtime-pack",
      2,
      (
        facades,
        ...args: Parameters<PackageFacade["loadRuntimePack"]>
      ) => facades.package.loadRuntimePack(...args),
    ),
    loadRuntimePackAssembly: valueOperation(
      "ordinary-package-load-runtime-pack-assembly",
      5,
      (
        facades,
        ...args: Parameters<PackageFacade["loadRuntimePackAssembly"]>
      ) => facades.package.loadRuntimePackAssembly(...args),
    ),
    getPackageDocument: valueOperation(
      "ordinary-package-get-document",
      3,
      (
        facades,
        ...args: Parameters<PackageFacade["getPackageDocument"]>
      ) => facades.package.getPackageDocument(...args),
    ),
    queryMemberDocumentation: valueOperation(
      "ordinary-package-query-member-documentation",
      5,
      (
        facades,
        ...args: Parameters<PackageFacade["queryMemberDocumentation"]>
      ) => facades.package.queryMemberDocumentation(...args),
    ),
    queryPackageDependencies: valueOperation(
      "ordinary-package-query-dependencies",
      4,
      (
        facades,
        ...args: Parameters<PackageFacade["queryPackageDependencies"]>
      ) => facades.package.queryPackageDependencies(...args),
    ),
    queryPackageVersions: valueOperation(
      "ordinary-package-query-versions",
      2,
      (
        facades,
        ...args: Parameters<PackageFacade["queryPackageVersions"]>
      ) => facades.package.queryPackageVersions(...args),
    ),
    queryWorkspacePackageOccurrences: valueOperation(
      "ordinary-package-query-workspace-occurrences",
      1,
      (
        facades,
        ...args: Parameters<
          PackageFacade["queryWorkspacePackageOccurrences"]
        >
      ) => facades.package.queryWorkspacePackageOccurrences(...args),
    ),
    resolvePackageDependencyVersion: valueOperation(
      "ordinary-package-resolve-dependency-version",
      2,
      (
        facades,
        ...args: Parameters<
          PackageFacade["resolvePackageDependencyVersion"]
        >
      ) => facades.package.resolvePackageDependencyVersion(...args),
    ),
  },
  metadata: {
    queryTypeProjection: valueOperation(
      "ordinary-metadata-query-type-projection",
      5,
      (
        facades,
        ...args: Parameters<MetadataFacade["queryTypeProjection"]>
      ) => facades.metadata.queryTypeProjection(...args),
    ),
    queryPackageMetadataTable: valueOperation(
      "ordinary-metadata-query-package-table",
      8,
      (
        facades,
        ...args: Parameters<MetadataFacade["queryPackageMetadataTable"]>
      ) => facades.metadata.queryPackageMetadataTable(...args),
    ),
    queryPlatformMetadataTable: valueOperation(
      "ordinary-metadata-query-platform-table",
      8,
      (
        facades,
        ...args: Parameters<
          MetadataFacade["queryPlatformMetadataTable"]
        >
      ) => facades.metadata.queryPlatformMetadataTable(...args),
    ),
    queryPackageHeapEntries: valueOperation(
      "ordinary-metadata-query-package-heap",
      6,
      (
        facades,
        ...args: Parameters<MetadataFacade["queryPackageHeapEntries"]>
      ) => facades.metadata.queryPackageHeapEntries(...args),
    ),
    queryPlatformHeapEntries: valueOperation(
      "ordinary-metadata-query-platform-heap",
      6,
      (
        facades,
        ...args: Parameters<MetadataFacade["queryPlatformHeapEntries"]>
      ) => facades.metadata.queryPlatformHeapEntries(...args),
    ),
    queryPackageMetadata: valueOperation(
      "ordinary-metadata-query-package",
      4,
      (
        facades,
        ...args: Parameters<MetadataFacade["queryPackageMetadata"]>
      ) => facades.metadata.queryPackageMetadata(...args),
    ),
    queryPlatformMetadata: valueOperation(
      "ordinary-metadata-query-platform",
      4,
      (
        facades,
        ...args: Parameters<MetadataFacade["queryPlatformMetadata"]>
      ) => facades.metadata.queryPlatformMetadata(...args),
    ),
    queryGraphMemberSurface: valueOperation(
      "ordinary-metadata-query-graph-member-surface",
      8,
      (
        facades,
        ...args: Parameters<MetadataFacade["queryGraphMemberSurface"]>
      ) => facades.metadata.queryGraphMemberSurface(...args),
    ),
    queryGraphMemberSurfaceByMethodAddress: valueOperation(
      "ordinary-metadata-query-graph-member-surface-by-method-address",
      9,
      (
        facades,
        ...args: Parameters<
          MetadataFacade["queryGraphMemberSurfaceByMethodAddress"]
        >
      ) => facades.metadata.queryGraphMemberSurfaceByMethodAddress(...args),
    ),
  },
  analysis: {
    queryMemberFacts: valueOperation(
      "ordinary-analysis-query-member-facts",
      10,
      (
        facades,
        ...args: Parameters<AnalysisFacade["queryMemberFacts"]>
      ) => facades.analysis.queryMemberFacts(...args),
    ),
    queryPackageIntegrations: valueOperation(
      "ordinary-analysis-query-package-integrations",
      4,
      (
        facades,
        ...args: Parameters<AnalysisFacade["queryPackageIntegrations"]>
      ) => facades.analysis.queryPackageIntegrations(...args),
    ),
    queryPlatformIntegrations: valueOperation(
      "ordinary-analysis-query-platform-integrations",
      4,
      (
        facades,
        ...args: Parameters<AnalysisFacade["queryPlatformIntegrations"]>
      ) => facades.analysis.queryPlatformIntegrations(...args),
    ),
    queryPackageOpportunities: valueOperation(
      "ordinary-analysis-query-package-opportunities",
      4,
      (
        facades,
        ...args: Parameters<AnalysisFacade["queryPackageOpportunities"]>
      ) => facades.analysis.queryPackageOpportunities(...args),
    ),
    queryPlatformOpportunities: valueOperation(
      "ordinary-analysis-query-platform-opportunities",
      4,
      (
        facades,
        ...args: Parameters<AnalysisFacade["queryPlatformOpportunities"]>
      ) => facades.analysis.queryPlatformOpportunities(...args),
    ),
    queryPackagePerformance: valueOperation(
      "ordinary-analysis-query-package-performance",
      4,
      (
        facades,
        ...args: Parameters<AnalysisFacade["queryPackagePerformance"]>
      ) => facades.analysis.queryPackagePerformance(...args),
    ),
    queryPlatformPerformance: valueOperation(
      "ordinary-analysis-query-platform-performance",
      4,
      (
        facades,
        ...args: Parameters<AnalysisFacade["queryPlatformPerformance"]>
      ) => facades.analysis.queryPlatformPerformance(...args),
    ),
  },
  source: {
    queryMemberSource: valueOperation(
      "ordinary-source-query-member",
      9,
      (
        facades,
        ...args: Parameters<SourceFacade["queryMemberSource"]>
      ) => facades.source.queryMemberSource(...args),
    ),
    queryTypeMemberSource: valueOperation(
      "ordinary-source-query-type-member",
      9,
      (
        facades,
        ...args: Parameters<SourceFacade["queryTypeMemberSource"]>
      ) => facades.source.queryTypeMemberSource(...args),
    ),
    cancelSourceQuery: voidOperation(
      "ordinary-source-cancel-legacy-query",
      0,
      (
        facades,
        ...args: Parameters<SourceFacade["cancelSourceQuery"]>
      ) => facades.source.cancelSourceQuery(...args),
    ),
    queryMethodBodyComparisonTargets: valueOperation(
      "ordinary-source-query-method-body-comparison-targets",
      9,
      (
        facades,
        ...args: Parameters<
          SourceFacade["queryMethodBodyComparisonTargets"]
        >
      ) => facades.source.queryMethodBodyComparisonTargets(...args),
    ),
    queryMethodBodyComparison: valueOperation(
      "ordinary-source-query-method-body-comparison",
      2,
      (
        facades,
        ...args: Parameters<SourceFacade["queryMethodBodyComparison"]>
      ) => facades.source.queryMethodBodyComparison(...args),
    ),
    cancelMethodBodyComparison: valueOperation(
      "ordinary-source-cancel-method-body-comparison",
      2,
      (
        facades,
        ...args: Parameters<SourceFacade["cancelMethodBodyComparison"]>
      ) => facades.source.cancelMethodBodyComparison(...args),
    ),
    queryMemberSourceComparison: valueOperation(
      "ordinary-source-query-member-comparison",
      2,
      (
        facades,
        ...args: Parameters<SourceFacade["queryMemberSourceComparison"]>
      ) => facades.source.queryMemberSourceComparison(...args),
    ),
    cancelMemberSourceComparison: valueOperation(
      "ordinary-source-cancel-member-comparison",
      2,
      (
        facades,
        ...args: Parameters<SourceFacade["cancelMemberSourceComparison"]>
      ) => facades.source.cancelMemberSourceComparison(...args),
    ),
    queryMemberFindingCensus: valueOperation(
      "ordinary-source-query-member-finding-census",
      11,
      (
        facades,
        ...args: Parameters<SourceFacade["queryMemberFindingCensus"]>
      ) => facades.source.queryMemberFindingCensus(...args),
    ),
  },
  callGraph: {
    queryMemberCallGraph: valueOperation(
      "ordinary-call-graph-query-member",
      11,
      (
        facades,
        ...args: Parameters<CallGraphFacade["queryMemberCallGraph"]>
      ) => facades.callGraph.queryMemberCallGraph(...args),
    ),
    expandPlatformCallGraph: valueOperation(
      "ordinary-call-graph-expand-platform",
      11,
      (
        facades,
        ...args: Parameters<CallGraphFacade["expandPlatformCallGraph"]>
      ) => facades.callGraph.expandPlatformCallGraph(...args),
    ),
  },
  catalog: {
    resolveHomeDemo: valueOperation(
      "ordinary-catalog-resolve-home-demo",
      1,
      (
        facades,
        ...args: Parameters<CatalogFacade["resolveHomeDemo"]>
      ) => facades.catalog.resolveHomeDemo(...args),
    ),
    decodeWorkspaceShareState: valueOperation(
      "ordinary-catalog-decode-workspace-share-state",
      1,
      (
        facades,
        ...args: Parameters<CatalogFacade["decodeWorkspaceShareState"]>
      ) => facades.catalog.decodeWorkspaceShareState(...args),
    ),
    encodeWorkspaceShareState: valueOperation(
      "ordinary-catalog-encode-workspace-share-state",
      1,
      (
        facades,
        ...args: Parameters<CatalogFacade["encodeWorkspaceShareState"]>
      ) => facades.catalog.encodeWorkspaceShareState(...args),
    ),
    runHomeDemo: valueOperation(
      "ordinary-catalog-run-home-demo",
      1,
      (
        facades,
        ...args: Parameters<CatalogFacade["runHomeDemo"]>
      ) => facades.catalog.runHomeDemo(...args),
    ),
  },
} as const;

const ordinaryOperationList: readonly ErasedOrdinaryOperation[] = [
  ...Object.values(engineWorkerOrdinaryOperations.package),
  ...Object.values(engineWorkerOrdinaryOperations.metadata),
  ...Object.values(engineWorkerOrdinaryOperations.analysis),
  ...Object.values(engineWorkerOrdinaryOperations.source),
  ...Object.values(engineWorkerOrdinaryOperations.callGraph),
  ...Object.values(engineWorkerOrdinaryOperations.catalog),
];

export const engineWorkerOrdinaryOperationKinds: readonly string[] =
  Object.freeze(ordinaryOperationList.map(operation => operation.kind));

export function registerEngineWorkerOrdinaryOperations(
  operations: WorkerOperationCatalog,
  facades: () => EngineWorkerOrdinaryFacades,
): void {
  for (const operation of ordinaryOperationList)
    operation.registerWorker(operations, facades);
}

export function bindEngineWorkerOrdinaryClient(
  host: WorkerRuntimeHost<string, string>,
  reportDiagnostic: (diagnostic: OperationDiagnostic) => undefined,
  page: OperationAuthorityPage = createOperationAuthorityPage(),
): EngineWorkerOrdinaryClient {
  const epoch = host.snapshot().epochToken;
  if (epoch === null) {
    throw new Error(
      "Start a Worker epoch before binding ordinary operations.",
    );
  }
  const bind = <
    TArgs extends readonly unknown[],
    TResult,
  >(
    operation: EngineWorkerOrdinaryOperation<TArgs, TResult>,
  ): (...args: TArgs) => Promise<TResult> =>
    operation.bindPage(host, page, epoch, reportDiagnostic);

  return {
    package: {
      getPlatformCatalog: bind(
        engineWorkerOrdinaryOperations.package.getPlatformCatalog,
      ),
      getPlatformVersions: bind(
        engineWorkerOrdinaryOperations.package.getPlatformVersions,
      ),
      listPackageAssemblyQueryPatterns: bind(
        engineWorkerOrdinaryOperations.package
          .listPackageAssemblyQueryPatterns,
      ),
      matchPackageDependencyCoordinate: bind(
        engineWorkerOrdinaryOperations.package
          .matchPackageDependencyCoordinate,
      ),
      searchTypes: bind(
        engineWorkerOrdinaryOperations.package.searchTypes,
      ),
      activateWorkspacePackageOccurrence: bind(
        engineWorkerOrdinaryOperations.package
          .activateWorkspacePackageOccurrence,
      ),
      clearWorkspacePackageOccurrences: bind(
        engineWorkerOrdinaryOperations.package
          .clearWorkspacePackageOccurrences,
      ),
      packageCacheStats: bind(
        engineWorkerOrdinaryOperations.package.packageCacheStats,
      ),
      prefetchPlatformPacks: bind(
        engineWorkerOrdinaryOperations.package.prefetchPlatformPacks,
      ),
      queryPackage: bind(
        engineWorkerOrdinaryOperations.package.queryPackage,
      ),
      openPackageAssemblyQueryResult: bind(
        engineWorkerOrdinaryOperations.package
          .openPackageAssemblyQueryResult,
      ),
      loadRuntimePack: bind(
        engineWorkerOrdinaryOperations.package.loadRuntimePack,
      ),
      loadRuntimePackAssembly: bind(
        engineWorkerOrdinaryOperations.package.loadRuntimePackAssembly,
      ),
      getPackageDocument: bind(
        engineWorkerOrdinaryOperations.package.getPackageDocument,
      ),
      queryMemberDocumentation: bind(
        engineWorkerOrdinaryOperations.package.queryMemberDocumentation,
      ),
      queryPackageDependencies: bind(
        engineWorkerOrdinaryOperations.package.queryPackageDependencies,
      ),
      queryPackageVersions: bind(
        engineWorkerOrdinaryOperations.package.queryPackageVersions,
      ),
      queryWorkspacePackageOccurrences: bind(
        engineWorkerOrdinaryOperations.package
          .queryWorkspacePackageOccurrences,
      ),
      resolvePackageDependencyVersion: bind(
        engineWorkerOrdinaryOperations.package
          .resolvePackageDependencyVersion,
      ),
    },
    metadata: {
      queryTypeProjection: bind(
        engineWorkerOrdinaryOperations.metadata.queryTypeProjection,
      ),
      queryPackageMetadataTable: bind(
        engineWorkerOrdinaryOperations.metadata
          .queryPackageMetadataTable,
      ),
      queryPlatformMetadataTable: bind(
        engineWorkerOrdinaryOperations.metadata
          .queryPlatformMetadataTable,
      ),
      queryPackageHeapEntries: bind(
        engineWorkerOrdinaryOperations.metadata.queryPackageHeapEntries,
      ),
      queryPlatformHeapEntries: bind(
        engineWorkerOrdinaryOperations.metadata
          .queryPlatformHeapEntries,
      ),
      queryPackageMetadata: bind(
        engineWorkerOrdinaryOperations.metadata.queryPackageMetadata,
      ),
      queryPlatformMetadata: bind(
        engineWorkerOrdinaryOperations.metadata.queryPlatformMetadata,
      ),
      queryGraphMemberSurface: bind(
        engineWorkerOrdinaryOperations.metadata.queryGraphMemberSurface,
      ),
      queryGraphMemberSurfaceByMethodAddress: bind(
        engineWorkerOrdinaryOperations.metadata
          .queryGraphMemberSurfaceByMethodAddress,
      ),
    },
    analysis: {
      queryMemberFacts: bind(
        engineWorkerOrdinaryOperations.analysis.queryMemberFacts,
      ),
      queryPackageIntegrations: bind(
        engineWorkerOrdinaryOperations.analysis
          .queryPackageIntegrations,
      ),
      queryPlatformIntegrations: bind(
        engineWorkerOrdinaryOperations.analysis
          .queryPlatformIntegrations,
      ),
      queryPackageOpportunities: bind(
        engineWorkerOrdinaryOperations.analysis
          .queryPackageOpportunities,
      ),
      queryPlatformOpportunities: bind(
        engineWorkerOrdinaryOperations.analysis
          .queryPlatformOpportunities,
      ),
      queryPackagePerformance: bind(
        engineWorkerOrdinaryOperations.analysis
          .queryPackagePerformance,
      ),
      queryPlatformPerformance: bind(
        engineWorkerOrdinaryOperations.analysis
          .queryPlatformPerformance,
      ),
    },
    source: {
      queryMemberSource: bind(
        engineWorkerOrdinaryOperations.source.queryMemberSource,
      ),
      queryTypeMemberSource: bind(
        engineWorkerOrdinaryOperations.source.queryTypeMemberSource,
      ),
      cancelSourceQuery: bind(
        engineWorkerOrdinaryOperations.source.cancelSourceQuery,
      ),
      queryMethodBodyComparisonTargets: bind(
        engineWorkerOrdinaryOperations.source
          .queryMethodBodyComparisonTargets,
      ),
      queryMethodBodyComparison: bind(
        engineWorkerOrdinaryOperations.source.queryMethodBodyComparison,
      ),
      cancelMethodBodyComparison: bind(
        engineWorkerOrdinaryOperations.source
          .cancelMethodBodyComparison,
      ),
      queryMemberSourceComparison: bind(
        engineWorkerOrdinaryOperations.source
          .queryMemberSourceComparison,
      ),
      cancelMemberSourceComparison: bind(
        engineWorkerOrdinaryOperations.source
          .cancelMemberSourceComparison,
      ),
      queryMemberFindingCensus: bind(
        engineWorkerOrdinaryOperations.source.queryMemberFindingCensus,
      ),
    },
    callGraph: {
      queryMemberCallGraph: bind(
        engineWorkerOrdinaryOperations.callGraph.queryMemberCallGraph,
      ),
      expandPlatformCallGraph: bind(
        engineWorkerOrdinaryOperations.callGraph
          .expandPlatformCallGraph,
      ),
    },
    catalog: {
      resolveHomeDemo: bind(
        engineWorkerOrdinaryOperations.catalog.resolveHomeDemo,
      ),
      decodeWorkspaceShareState: bind(
        engineWorkerOrdinaryOperations.catalog
          .decodeWorkspaceShareState,
      ),
      encodeWorkspaceShareState: bind(
        engineWorkerOrdinaryOperations.catalog
          .encodeWorkspaceShareState,
      ),
      runHomeDemo: bind(
        engineWorkerOrdinaryOperations.catalog.runHomeDemo,
      ),
    },
  };
}
