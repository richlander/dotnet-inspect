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
import type { WorkerRuntimeHost, WorkerRuntimePreparationError } from "./worker-runtime-core.ts";
import type { BoundedPayloadDecoder } from "./worker-runtime-protocol.ts";
import type { WorkerOperationCatalog } from "./worker-runtime-realm.ts";

export interface EngineFacades {
  readonly host: typeof import("./facades/inspect-web-host.d.ts");
  readonly package: typeof import("./facades/inspect-web-package.d.ts");
  readonly metadata: typeof import("./facades/inspect-web-metadata.d.ts");
  readonly analysis: typeof import("./facades/inspect-web-analysis.d.ts");
  readonly source: typeof import("./facades/inspect-web-source.d.ts");
  readonly callGraph: typeof import("./facades/inspect-web-call-graph.d.ts");
  readonly catalog: typeof import("./facades/inspect-web-catalog.d.ts");
}

type ManagedFunction = (...args: never[]) => unknown;
type AsyncFunction<F extends ManagedFunction> =
  (...args: Parameters<F>) => Promise<Awaited<ReturnType<F>>>;
type ParameterKind<T> = null extends T ? "nullable-string"
  : T extends string ? "string" : T extends number ? "number"
    : T extends boolean ? "boolean" : never;
// An empty never-array enforces zero arguments without the native semantic
// API's mapped-empty-tuple crash (the project-wide semantic sweep gates this).
type ParameterKinds<T extends readonly unknown[]> = T extends []
  ? readonly never[]
  : { [K in keyof T]: ParameterKind<T[K]> };
type ResultKind<T> = T extends void ? "void" : T extends string ? "string"
  : T extends readonly unknown[] ? "array" : T extends object ? "object" : never;

interface OperationBinder {
  <F extends ManagedFunction>(
    kind: string,
    select: (facades: EngineFacades) => F,
    parameters: ParameterKinds<Parameters<F>>,
    result: ResultKind<Awaited<ReturnType<F>>>,
  ): AsyncFunction<F>;
}

// This is the complete ordinary production inventory, not a member-name dispatcher.
// Startup reads and callback/authority operations retain their feature-owned adapters.
function compose(bind: OperationBinder) {
  return {
    package: {
      activateWorkspacePackageOccurrence: bind("package-activate-occurrence", (f: EngineFacades) => f.package.activateWorkspacePackageOccurrence, ["string"], "object"),
      clearWorkspacePackageOccurrences: bind("package-clear-occurrences", (f: EngineFacades) => f.package.clearWorkspacePackageOccurrences, [], "void"),
      getPlatformCatalog: bind("package-platform-catalog", (f: EngineFacades) => f.package.getPlatformCatalog, ["string", "string"], "object"),
      getPlatformVersions: bind("package-platform-versions", (f: EngineFacades) => f.package.getPlatformVersions, ["string"], "array"),
      getPackageDocument: bind("package-document", (f: EngineFacades) => f.package.getPackageDocument, ["string", "string", "string"], "object"),
      listPackageAssemblyQueryPatterns: bind("package-assembly-patterns", (f: EngineFacades) => f.package.listPackageAssemblyQueryPatterns, [], "array"),
      loadRuntimePack: bind("package-load-runtime", (f: EngineFacades) => f.package.loadRuntimePack, ["string", "string"], "string"),
      loadRuntimePackAssembly: bind("package-load-runtime-assembly", (f: EngineFacades) => f.package.loadRuntimePackAssembly, ["string", "string", "string", "string", "string"], "string"),
      matchPackageDependencyCoordinate: bind("package-match-dependency", (f: EngineFacades) => f.package.matchPackageDependencyCoordinate, ["string", "nullable-string", "string"], "object"),
      openPackageAssemblyQueryResult: bind("package-open-assembly-result", (f: EngineFacades) => f.package.openPackageAssemblyQueryResult, ["string"], "object"),
      packageCacheStats: bind("package-cache-stats", (f: EngineFacades) => f.package.packageCacheStats, [], "object"),
      prefetchPlatformPacks: bind("package-prefetch-platform-packs", (f: EngineFacades) => f.package.prefetchPlatformPacks, ["string", "string"], "void"),
      queryMemberDocumentation: bind("package-member-documentation", (f: EngineFacades) => f.package.queryMemberDocumentation, ["string", "string", "string", "string", "string"], "object"),
      queryPackage: bind("package-surface", (f: EngineFacades) => f.package.queryPackage, ["string", "string", "string"], "object"),
      queryPackageDependencies: bind("package-dependencies", (f: EngineFacades) => f.package.queryPackageDependencies, ["string", "string", "string", "string"], "object"),
      queryPackageVersions: bind("package-versions", (f: EngineFacades) => f.package.queryPackageVersions, ["string", "string"], "object"),
      queryWorkspacePackageOccurrences: bind("package-workspace-occurrences", (f: EngineFacades) => f.package.queryWorkspacePackageOccurrences, ["string"], "object"),
      resolvePackageDependencyVersion: bind("package-resolve-dependency", (f: EngineFacades) => f.package.resolvePackageDependencyVersion, ["string", "nullable-string"], "string"),
      searchTypes: bind("package-search-types", (f: EngineFacades) => f.package.searchTypes, ["string", "string"], "array"),
    },
    metadata: {
      queryGraphMemberSurface: bind("metadata-graph-member", (f: EngineFacades) => f.metadata.queryGraphMemberSurface, ["string", "string", "string", "string", "string", "string", "string", "number"], "object"),
      queryPackageHeapEntries: bind("metadata-package-heap", (f: EngineFacades) => f.metadata.queryPackageHeapEntries, ["string", "string", "string", "string", "string", "string"], "object"),
      queryPackageMetadata: bind("metadata-package", (f: EngineFacades) => f.metadata.queryPackageMetadata, ["string", "string", "string", "string"], "object"),
      queryPackageMetadataTable: bind("metadata-package-table", (f: EngineFacades) => f.metadata.queryPackageMetadataTable, ["string", "string", "string", "string", "string", "number", "number", "number"], "object"),
      queryPlatformHeapEntries: bind("metadata-platform-heap", (f: EngineFacades) => f.metadata.queryPlatformHeapEntries, ["string", "string", "string", "string", "string", "string"], "object"),
      queryPlatformMetadata: bind("metadata-platform", (f: EngineFacades) => f.metadata.queryPlatformMetadata, ["string", "string", "string", "string"], "object"),
      queryPlatformMetadataTable: bind("metadata-platform-table", (f: EngineFacades) => f.metadata.queryPlatformMetadataTable, ["string", "string", "string", "string", "string", "number", "number", "number"], "object"),
      queryTypeProjection: bind("metadata-type", (f: EngineFacades) => f.metadata.queryTypeProjection, ["string", "string", "string", "string", "string"], "object"),
    },
    analysis: {
      queryCloneCandidates: bind("analysis-clones", (f: EngineFacades) => f.analysis.queryCloneCandidates, ["string"], "object"),
      queryMemberFacts: bind("analysis-member-facts", (f: EngineFacades) => f.analysis.queryMemberFacts, ["string", "string", "string", "string", "string", "string", "string", "string", "number", "boolean"], "object"),
      queryPackageIntegrations: bind("analysis-package-integrations", (f: EngineFacades) => f.analysis.queryPackageIntegrations, ["string", "string", "string", "string"], "object"),
      queryPackageOpportunities: bind("analysis-package-opportunities", (f: EngineFacades) => f.analysis.queryPackageOpportunities, ["string", "string", "string", "string"], "object"),
      queryPackagePerformance: bind("analysis-package-performance", (f: EngineFacades) => f.analysis.queryPackagePerformance, ["string", "string", "string", "string"], "object"),
      queryPlatformIntegrations: bind("analysis-platform-integrations", (f: EngineFacades) => f.analysis.queryPlatformIntegrations, ["string", "string", "string", "string"], "object"),
      queryPlatformOpportunities: bind("analysis-platform-opportunities", (f: EngineFacades) => f.analysis.queryPlatformOpportunities, ["string", "string", "string", "string"], "object"),
      queryPlatformPerformance: bind("analysis-platform-performance", (f: EngineFacades) => f.analysis.queryPlatformPerformance, ["string", "string", "string", "string"], "string"),
    },
    source: {
      cancelSourceQuery: bind("source-cancel", (f: EngineFacades) => f.source.cancelSourceQuery, [], "void"),
      queryMemberAnnotatedSource: bind("source-annotated-member", (f: EngineFacades) => f.source.queryMemberAnnotatedSource, ["string", "string", "string", "string", "string", "string", "string", "string", "string", "number", "string"], "object"),
      queryMemberFindingCensus: bind("source-member-census", (f: EngineFacades) => f.source.queryMemberFindingCensus, ["string", "string", "string", "string", "string", "string", "string", "string", "string", "number", "string"], "object"),
      queryMemberSource: bind("source-member", (f: EngineFacades) => f.source.queryMemberSource, ["string", "string", "string", "string", "string", "string", "string", "number", "string"], "object"),
      queryTypeMemberSource: bind("source-type-member", (f: EngineFacades) => f.source.queryTypeMemberSource, ["string", "string", "string", "string", "string", "string", "string", "number", "string"], "object"),
    },
    callGraph: {
      expandPlatformCallGraph: bind("call-graph-platform", (f: EngineFacades) => f.callGraph.expandPlatformCallGraph, ["string", "string", "string", "string", "string", "nullable-string", "nullable-string", "string", "string", "string", "number"], "object"),
      queryMemberCallGraph: bind("call-graph-member", (f: EngineFacades) => f.callGraph.queryMemberCallGraph, ["string", "string", "string", "string", "string", "string", "string", "string", "string", "number", "string"], "object"),
    },
    catalog: {
      decodeWorkspaceShareState: bind("catalog-decode-workspace", (f: EngineFacades) => f.catalog.decodeWorkspaceShareState, ["string"], "object"),
      encodeWorkspaceShareState: bind("catalog-encode-workspace", (f: EngineFacades) => f.catalog.encodeWorkspaceShareState, ["string"], "object"),
      resolveHomeDemo: bind("catalog-resolve-demo", (f: EngineFacades) => f.catalog.resolveHomeDemo, ["string"], "object"),
      runHomeDemo: bind("catalog-run-demo", (f: EngineFacades) => f.catalog.runHomeDemo, ["string"], "object"),
    },
  };
}

export const engineOperationMaximumCharacters = 64 * 1024 * 1024;
const maximumNodes = 1_048_576;
const maximumDepth = 64;
class OversizedEnginePayload extends Error {}

// Clone only own enumerable JSON data, never getters or toJSON. Budgets apply
// before serialization as well as before parsing; no generated DTO is normalized.
export function encodeEngineOperationValue(value: unknown): string {
  let characters = 0;
  let nodes = 0;
  function clone(input: unknown, depth: number): unknown {
    if (++nodes > maximumNodes || depth > maximumDepth)
      throw new OversizedEnginePayload("Engine payload exceeds the structural budget.");
    if (typeof input === "string") {
      characters += input.length;
      if (characters > engineOperationMaximumCharacters)
        throw new OversizedEnginePayload("Engine payload exceeds the text budget.");
      return input;
    }
    if (input === null || typeof input === "boolean") return input;
    if (typeof input === "number" && Number.isFinite(input)) return input;
    if (typeof input !== "object" || input === null)
      throw new Error("Engine payload must contain only JSON data.");
    const array = Array.isArray(input);
    if (!array && Object.getPrototypeOf(input) !== Object.prototype
      && Object.getPrototypeOf(input) !== null)
      throw new Error("Engine payload must contain only plain records.");
    const keys = Reflect.ownKeys(input);
    if (keys.length > maximumNodes - nodes)
      throw new OversizedEnginePayload("Engine payload exceeds the structural budget.");
    if (array) {
      const length: unknown = Object.getOwnPropertyDescriptor(input, "length")?.value;
      if (typeof length !== "number" || !Number.isSafeInteger(length) || length < 0 || keys.length !== length + 1)
        throw new Error("Engine payload arrays must be dense data.");
      const result: unknown[] = [];
      for (let index = 0; index < length; index++) {
        const property = Object.getOwnPropertyDescriptor(input, String(index));
        if (!property || !("value" in property) || !property.enumerable)
          throw new Error("Engine payload arrays must be dense data.");
        result.push(clone(property.value, depth + 1));
      }
      return result;
    }
    const result: Record<string, unknown> = {};
    for (const key of keys) {
      const property = Object.getOwnPropertyDescriptor(input, key);
      if (typeof key !== "string" || !property || !("value" in property) || !property.enumerable)
        throw new Error("Engine payload records must contain only enumerable data.");
      clone(key, depth + 1);
      Object.defineProperty(result, key, {
        value: clone(property.value, depth + 1), enumerable: true,
      });
    }
    return result;
  }
  const json = JSON.stringify(clone(value, 0));
  if (json.length > engineOperationMaximumCharacters)
    throw new OversizedEnginePayload("Engine payload exceeds the encoded text budget.");
  return json;
}

export function engineOperationDecoder<T>(validate: (value: unknown) => T): BoundedPayloadDecoder<T> {
  return {
    decode(value) {
      try {
        if (typeof value !== "string")
          throw new Error("Expected bounded engine payload JSON.");
        if (value.length > engineOperationMaximumCharacters)
          throw new OversizedEnginePayload("Engine payload exceeds the encoded text budget.");
        const parsed: unknown = JSON.parse(value);
        encodeEngineOperationValue(parsed);
        return { kind: "decoded", value: validate(parsed) };
      } catch (error: unknown) {
        return {
          kind: "rejected", reason: error instanceof OversizedEnginePayload ? "oversized" : "invalid",
          message: engineWorkerDiagnostic(error),
        };
      }
    },
  };
}

export function engineOperationInputDecoder<T extends readonly unknown[]>(kinds: ParameterKinds<T>) {
  function isInput(value: unknown): value is T {
    if (!Array.isArray(value) || value.length !== kinds.length)
      return false;
    for (let index = 0; index < kinds.length; index++) {
      const kind = kinds[index];
      const item: unknown = value[index];
      if (kind === "nullable-string"
        ? item !== null && typeof item !== "string"
        : typeof item !== kind)
        return false;
    }
    return true;
  }
  return engineOperationDecoder<T>(value => {
    if (!isInput(value)) throw new Error("Engine arguments do not match the generated operation.");
    return value;
  });
}

export function engineOperationValueDecoder<T>(kind: ResultKind<T>) {
  function isValue(value: unknown): value is T {
    return kind === "void" ? value === undefined
      : kind === "string" ? typeof value === "string"
        : kind === "array" ? Array.isArray(value)
          : typeof value === "object" && value !== null && !Array.isArray(value);
  }
  return engineOperationDecoder<T>(value => {
    const decoded = kind === "void" && value === null ? undefined : value;
    if (!isValue(decoded))
      throw new Error("Engine result shape does not match the generated operation.");
    // Generated functions own DTO validation/serialization; this layer validates
    // bounded data transport without creating a second copy of every DTO grammar.
    return decoded;
  });
}

export function registerEngineWorkerOperations(
  operations: WorkerOperationCatalog,
  facades: () => EngineFacades,
): void {
  compose(<F extends ManagedFunction>(
    kind: string, select: (facades: EngineFacades) => F,
    parameters: ParameterKinds<Parameters<F>>, result: ResultKind<Awaited<ReturnType<F>>>,
  ) => {
    operations.register({
      kind, allowance: { kind: "unbounded" },
      input: engineOperationInputDecoder<Parameters<F>>(parameters),
      rejectInvalidPayload: failure => ({ error: failure.message, diagnostic: failure.message }),
      async invoke(input) {
        try {
          const value = await select(facades())(...input);
          if (result === "void" && value !== undefined)
            throw new Error("Engine acknowledgment did not match its generated void result.");
          const encoded = encodeEngineOperationValue(result === "void" ? null : value);
          const checked = engineOperationValueDecoder(result).decode(encoded);
          if (checked.kind === "rejected") throw new Error(checked.message);
          return { kind: "succeeded", value: encoded };
        } catch (error: unknown) {
          const message = engineWorkerDiagnostic(error);
          return { kind: "failed", failureKind: "unexpected", error: message, diagnostic: message };
        }
      },
    });
    return async (..._args: Parameters<F>): Promise<Awaited<ReturnType<F>>> => {
      throw new Error("Worker operation registration is not a page client.");
    };
  });
}

export function bindEngineWorkerOperations(
  host: WorkerRuntimeHost<string, string>,
  reportDiagnostic: (diagnostic: OperationDiagnostic) => undefined,
  page: OperationAuthorityPage = createOperationAuthorityPage(),
) {
  const epoch = host.snapshot().epochToken;
  if (epoch === null) throw new Error("Start a Worker epoch before binding engine operations.");
  return compose(<F extends ManagedFunction>(
    kind: string, _select: (facades: EngineFacades) => F,
    parameters: ParameterKinds<Parameters<F>>, result: ResultKind<Awaited<ReturnType<F>>>,
  ) => {
    const input = engineOperationInputDecoder<Parameters<F>>(parameters);
    const adapter = host.registerOperation({
      kind, allowance: { kind: "unbounded" },
      encodeInput: (args: Parameters<F>) => {
        try {
          const json = encodeEngineOperationValue(args);
          const checked = input.decode(json);
          return checked.kind === "rejected" ? checked : { kind: "decoded", value: json };
        } catch (error: unknown) {
          return {
            kind: "rejected", reason: error instanceof OversizedEnginePayload ? "oversized" : "invalid",
            message: engineWorkerDiagnostic(error),
          };
        }
      },
      value: engineOperationValueDecoder<Awaited<ReturnType<F>>>(result),
      error: engineWorkerText, diagnostic: engineWorkerText, progress: engineWorkerText,
      mapPreparationError: error => error, boundaryErrors: engineWorkerBoundaryErrors,
    });
    return async (...args: Parameters<F>): Promise<Awaited<ReturnType<F>>> => {
      if (host.snapshot().epochToken !== epoch)
        throw new Error("Engine client belongs to a closed Worker epoch. Reload the page.");
      const session = page.createSession<
        Parameters<F>, Awaited<ReturnType<F>>, string, string, WorkerRuntimePreparationError
      >({
        feature: { publish: () => undefined }, diagnostic: { report: reportDiagnostic },
      });
      try {
        const started = session.start(args, adapter);
        if (started.kind === "rejected") {
          const reason = started.reason.kind === "producer-rejected"
            ? started.reason.error.kind : started.reason.kind;
          throw new Error(`Engine operation could not start: ${reason}.`);
        }
        const outcome = await started.handle.outcome;
        if (outcome.kind === "succeeded") return outcome.value;
        if (outcome.kind === "failed") throw new Error(outcome.error);
        throw new Error(`Engine operation canceled: ${outcome.reason}.`);
      } finally {
        session.dispose();
      }
    };
  });
}
