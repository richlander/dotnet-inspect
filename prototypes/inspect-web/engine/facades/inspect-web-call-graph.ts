import { dotnet } from "./runtime-loader.js";

export interface BrowserCallGraph {
  readonly mermaid: string;
  readonly callers: BrowserCallGraphNode;
  readonly callees: BrowserCallGraphNode;
  readonly scope: BrowserCallGraphScope;
  readonly targets: ReadonlyArray<BrowserCallGraphTarget>;
  readonly diagnostics: BrowserCallGraphDiagnostics;
  readonly noBody: boolean;
}

export interface BrowserCallGraphDiagnostics {
  readonly incompleteNodes: number;
  readonly incompleteEdges: number;
  readonly bindingIdentityConflicts: number;
  readonly hasUnexploredTraversalBoundary: boolean;
  readonly hasAnalysisFailureBoundary: boolean;
  readonly isIncomplete: boolean;
}

export interface BrowserCallGraphNode {
  readonly label: string;
  readonly status: string;
  readonly inLoop: boolean;
  readonly source: string | null;
  readonly children: ReadonlyArray<BrowserCallGraphNode>;
  readonly assembly: string;
  readonly typeFullName: string;
  readonly memberName: string;
}

export interface BrowserCallGraphScope {
  readonly packages: number;
  readonly assemblies: number;
  readonly callerAssemblies: number;
  readonly calleeScope: string;
}

export interface BrowserCallGraphTarget {
  readonly id: string;
  readonly assembly: string;
  readonly assemblyVersion: string | null;
  readonly assemblyCulture: string | null;
  readonly assemblyPublicKeyToken: string | null;
  readonly typeFullName: string;
  readonly typeMetadataId: string | null;
  readonly typeDefinitionId: string | null;
  readonly memberName: string;
  readonly parameterTypes: ReadonlyArray<string>;
  readonly returnType: string;
  readonly genericArity: number;
  readonly metadataToken: number | null;
  readonly selectorKey: string;
  readonly kind: string;
  readonly platformPack: string | null;
  readonly surfaceAssemblyId: string | null;
}

type $ManagedExports = {
  readonly "CallGraphExports": {
    readonly "ExpandPlatformCallGraph.1136010516": (targetFramework: string, platformVersion: string, assembly: string, pack: string, assemblyVersion: string, assemblyCulture: string | null, assemblyPublicKeyToken: string | null, typeFullName: string, memberName: string, selectorKey: string, metadataToken: number) => Promise<string>;
    readonly "QueryMemberCallGraph.1135530322": (packageId: string, version: string, targetFramework: string, assemblyName: string, typeIdentity: string, typeQueryId: string, memberName: string, memberSignature: string, selectorKey: string, metadataToken: number, workspaceJson: string) => Promise<string>;
  };
};

export interface JsExportRuntime {
  readonly getAssemblyExports: (assemblyName: string) => Promise<unknown>;
  readonly runMain: (
    mainAssemblyName?: string,
    args?: string[],
  ) => Promise<number>;
}

const $notInitializedError = new Error("The .NET runtime facade is not initialized.");
let $runtime: JsExportRuntime | undefined;
let $managedExports: $ManagedExports | undefined;
let $initialization: Promise<void> | undefined;
let $initializationFailure: { readonly error: unknown } | undefined;

function $ownDataProperty(value: unknown, key: string): unknown {
  if (value === null || (typeof value !== "object" && typeof value !== "function")) {
    throw new Error(`Managed export path '${key}' has a non-object parent.`);
  }
  const descriptor = Object.getOwnPropertyDescriptor(value, key);
  if (descriptor === undefined || !("value" in descriptor)) {
    throw new Error(`Managed export path '${key}' is not an own data property.`);
  }
  return descriptor.value;
}

function $requireRuntime(): JsExportRuntime {
  if ($initializationFailure !== undefined) throw $initializationFailure.error;
  if ($runtime === undefined) {
    throw $notInitializedError;
  }
  return $runtime;
}

function $requireManagedExports(): $ManagedExports {
  if ($initializationFailure !== undefined) throw $initializationFailure.error;
  if ($managedExports === undefined) {
    throw $notInitializedError;
  }
  return $managedExports;
}

function $validateManagedExports(exports: unknown): asserts exports is $ManagedExports {
  {
    let value: unknown = exports;
    value = $ownDataProperty(value, "CallGraphExports");
    value = $ownDataProperty(value, "ExpandPlatformCallGraph.1136010516");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027CallGraphExports.ExpandPlatformCallGraph.1136010516\u0027 is not callable.");
    }
  }
  {
    let value: unknown = exports;
    value = $ownDataProperty(value, "CallGraphExports");
    value = $ownDataProperty(value, "QueryMemberCallGraph.1135530322");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027CallGraphExports.QueryMemberCallGraph.1135530322\u0027 is not callable.");
    }
  }
}

async function $initializeRuntimeCore(
  runtime: JsExportRuntime,
): Promise<void> {
  const exports: unknown = await runtime.getAssemblyExports("InspectWeb.Engine.CallGraphExports");
  $validateManagedExports(exports);
  $runtime = runtime;
  $managedExports = exports;
}

export function createRuntime(): Promise<JsExportRuntime> {
  return dotnet.create();
}

export function initializeRuntime(
  runtime?: JsExportRuntime | PromiseLike<JsExportRuntime>,
): Promise<void> {
  if ($initialization === undefined) {
    $initialization = Promise.resolve()
      .then(() => runtime === undefined ? createRuntime() : runtime)
      .then($initializeRuntimeCore)
      .catch((error: unknown) => {
        $initializationFailure = { error };
        throw error;
      });
  }
  return $initialization;
}

export function runEntryPoint(
  mainAssemblyName?: string,
  args?: string[],
): Promise<number> {
  return $requireRuntime().runMain(mainAssemblyName, args);
}

export async function expandPlatformCallGraph(targetFramework: string, platformVersion: string, assembly: string, pack: string, assemblyVersion: string, assemblyCulture: string | null, assemblyPublicKeyToken: string | null, typeFullName: string, memberName: string, selectorKey: string, metadataToken: number): Promise<BrowserCallGraph> {
  const $result = await $requireManagedExports()["CallGraphExports"]["ExpandPlatformCallGraph.1136010516"](targetFramework, platformVersion, assembly, pack, assemblyVersion, assemblyCulture, assemblyPublicKeyToken, typeFullName, memberName, selectorKey, metadataToken);
  const $parsed: unknown = JSON.parse($result);
  return $parsed as BrowserCallGraph;
}

export async function queryMemberCallGraph(packageId: string, version: string, targetFramework: string, assemblyName: string, typeIdentity: string, typeQueryId: string, memberName: string, memberSignature: string, selectorKey: string, metadataToken: number, workspaceJson: string): Promise<BrowserCallGraph> {
  const $result = await $requireManagedExports()["CallGraphExports"]["QueryMemberCallGraph.1135530322"](packageId, version, targetFramework, assemblyName, typeIdentity, typeQueryId, memberName, memberSignature, selectorKey, metadataToken, workspaceJson);
  const $parsed: unknown = JSON.parse($result);
  return $parsed as BrowserCallGraph;
}

