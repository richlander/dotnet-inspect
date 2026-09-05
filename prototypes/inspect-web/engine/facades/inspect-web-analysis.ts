import { dotnet } from "./runtime-loader.js";

export type BrowserCompileLibraryStatus = "Selected" | "NoCompileAssets" | "NoMatchingTargetFramework" | "EmptyCompileGroup" | "InvalidImplementationAssets" | number;

export interface BrowserAllocationFact {
  readonly kind: string;
  readonly type: string | null;
  readonly offset: string;
  readonly countedAsHeap: boolean;
  readonly frequency: string;
  readonly multiplicity: string;
  readonly path: string;
  readonly escape: string;
  readonly inLoop: boolean;
  readonly estimatedSizeBytes: number | null;
  readonly detail: string | null;
}

export interface BrowserCallFact {
  readonly callee: string;
  readonly offset: string;
  readonly opcode: string;
  readonly kind: string;
  readonly multiplicity: string;
  readonly inLoop: boolean;
}

export interface BrowserCompileLibraryAvailability {
  readonly status: BrowserCompileLibraryStatus;
  readonly targetFramework: string | null;
  readonly message: string | null;
}

export interface BrowserExceptionRegion {
  readonly region: number;
  readonly clause: string;
  readonly tryRange: string;
  readonly handlerRange: string;
  readonly filterRange: string | null;
  readonly caughtType: string | null;
}

export interface BrowserIntegrationCategory {
  readonly integration: string;
  readonly signals: ReadonlyArray<BrowserIntegrationSignal>;
}

export interface BrowserIntegrationSignal {
  readonly kind: string;
  readonly name: string;
  readonly shape: string;
}

export interface BrowserMemberFacts {
  readonly metadataToken: number;
  readonly signals: BrowserMethodSignals;
  readonly allocations: ReadonlyArray<BrowserAllocationFact>;
  readonly calls: ReadonlyArray<BrowserCallFact>;
  readonly safety: ReadonlyArray<BrowserSafetyFact>;
  readonly exceptionRegions: ReadonlyArray<BrowserExceptionRegion>;
  readonly performanceOpportunities: ReadonlyArray<BrowserPerformanceOpportunity>;
  readonly diagnostics: ReadonlyArray<string>;
}

export interface BrowserMethodSignals {
  readonly allocations: number;
  readonly copies: number;
  readonly unsafe: boolean;
  readonly reflection: number;
  readonly throws: number;
  readonly catches: number;
  readonly finallys: number;
  readonly allocatesInLoop: boolean;
  readonly evidenceOffsets: ReadonlyArray<string>;
  readonly exceptionTypes: ReadonlyArray<string>;
}

export interface BrowserOpportunityCategory {
  readonly integration: string;
  readonly items: ReadonlyArray<BrowserOpportunityItem>;
}

export interface BrowserOpportunityItem {
  readonly api: string;
  readonly integrationType: string;
  readonly lookFor: string;
  readonly sourceDefinitionId: string | null;
  readonly sourceAssembly: string;
  readonly sourceAssemblyVersion: string;
  readonly sourceAssemblyCulture: string | null;
  readonly sourceAssemblyPublicKeyToken: string | null;
}

export interface BrowserPackageIntegrations {
  readonly package: string;
  readonly version: string;
  readonly framework: string;
  readonly categories: ReadonlyArray<BrowserIntegrationCategory>;
  readonly totalSignals: number;
  readonly isComplete: boolean;
  readonly inspectionError: string | null;
  readonly compileLibrary: BrowserCompileLibraryAvailability;
}

export interface BrowserPackageOpportunities {
  readonly package: string;
  readonly version: string;
  readonly activeFramework: string;
  readonly categories: ReadonlyArray<BrowserOpportunityCategory>;
  readonly totalOpportunities: number;
  readonly isComplete: boolean;
  readonly inspectionError: string | null;
  readonly compileLibrary: BrowserCompileLibraryAvailability;
}

export interface BrowserPackagePerformance {
  readonly members: ReadonlyArray<BrowserPerformanceMember>;
  readonly inspectionError: string | null;
  readonly nonPublicOpportunities: number;
  readonly totalOpportunities: number;
  readonly compileLibrary: BrowserCompileLibraryAvailability;
}

export interface BrowserPerformanceMember {
  readonly assembly: string;
  readonly typeId: string;
  readonly memberName: string;
  readonly stableSelector: string;
  readonly bodyTokens: ReadonlyArray<number>;
  readonly opportunityCount: number;
  readonly inLoopCount: number;
  readonly shapes: ReadonlyArray<string>;
  readonly confidence: string;
}

export interface BrowserPerformanceOpportunity {
  readonly shape: string;
  readonly evidence: string;
  readonly fix: string;
  readonly confidence: string;
  readonly offset: string | null;
  readonly inLoop: boolean;
  readonly caveat: string | null;
  readonly finding: string | null;
  readonly provenance: string;
}

export interface BrowserSafetyFact {
  readonly kind: string;
  readonly offset: string | null;
  readonly operation: string;
  readonly requirement: string;
  readonly evidence: string;
}

type $ManagedExports = {
  readonly "AnalysisExports": {
    readonly "QueryMemberFacts.581406856": (packageId: string, version: string, targetFramework: string, assemblyName: string, typeIdentity: string, memberName: string, memberSignature: string, selectorKey: string, metadataToken: number, implementationBodySelected: boolean) => Promise<string>;
    readonly "QueryPackageIntegrations.1001223652": (packageId: string, version: string, targetFramework: string) => Promise<string>;
    readonly "QueryPackageOpportunities.1001223652": (packageId: string, version: string, targetFramework: string) => Promise<string>;
    readonly "QueryPackagePerformance.1001223652": (packageId: string, version: string, targetFramework: string) => Promise<string>;
    readonly "QueryPlatformIntegrations.1579276339": (targetFramework: string, platformVersion: string, assemblyFileName: string, pack: string) => Promise<string>;
    readonly "QueryPlatformOpportunities.1579276339": (targetFramework: string, platformVersion: string, assemblyFileName: string, pack: string) => Promise<string>;
    readonly "QueryPlatformPerformance.1579276339": (targetFramework: string, platformVersion: string, assemblyFileName: string, pack: string) => Promise<string>;
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
    value = $ownDataProperty(value, "AnalysisExports");
    value = $ownDataProperty(value, "QueryMemberFacts.581406856");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027AnalysisExports.QueryMemberFacts.581406856\u0027 is not callable.");
    }
  }
  {
    let value: unknown = exports;
    value = $ownDataProperty(value, "AnalysisExports");
    value = $ownDataProperty(value, "QueryPackageIntegrations.1001223652");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027AnalysisExports.QueryPackageIntegrations.1001223652\u0027 is not callable.");
    }
  }
  {
    let value: unknown = exports;
    value = $ownDataProperty(value, "AnalysisExports");
    value = $ownDataProperty(value, "QueryPackageOpportunities.1001223652");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027AnalysisExports.QueryPackageOpportunities.1001223652\u0027 is not callable.");
    }
  }
  {
    let value: unknown = exports;
    value = $ownDataProperty(value, "AnalysisExports");
    value = $ownDataProperty(value, "QueryPackagePerformance.1001223652");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027AnalysisExports.QueryPackagePerformance.1001223652\u0027 is not callable.");
    }
  }
  {
    let value: unknown = exports;
    value = $ownDataProperty(value, "AnalysisExports");
    value = $ownDataProperty(value, "QueryPlatformIntegrations.1579276339");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027AnalysisExports.QueryPlatformIntegrations.1579276339\u0027 is not callable.");
    }
  }
  {
    let value: unknown = exports;
    value = $ownDataProperty(value, "AnalysisExports");
    value = $ownDataProperty(value, "QueryPlatformOpportunities.1579276339");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027AnalysisExports.QueryPlatformOpportunities.1579276339\u0027 is not callable.");
    }
  }
  {
    let value: unknown = exports;
    value = $ownDataProperty(value, "AnalysisExports");
    value = $ownDataProperty(value, "QueryPlatformPerformance.1579276339");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027AnalysisExports.QueryPlatformPerformance.1579276339\u0027 is not callable.");
    }
  }
}

async function $initializeRuntimeCore(
  runtime: JsExportRuntime,
): Promise<void> {
  const exports: unknown = await runtime.getAssemblyExports("InspectWeb.Engine.AnalysisExports");
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

export async function queryMemberFacts(packageId: string, version: string, targetFramework: string, assemblyName: string, typeIdentity: string, memberName: string, memberSignature: string, selectorKey: string, metadataToken: number, implementationBodySelected: boolean): Promise<BrowserMemberFacts> {
  const $result = await $requireManagedExports()["AnalysisExports"]["QueryMemberFacts.581406856"](packageId, version, targetFramework, assemblyName, typeIdentity, memberName, memberSignature, selectorKey, metadataToken, implementationBodySelected);
  const $parsed: unknown = JSON.parse($result);
  return $parsed as BrowserMemberFacts;
}

export async function queryPackageIntegrations(packageId: string, version: string, targetFramework: string): Promise<BrowserPackageIntegrations> {
  const $result = await $requireManagedExports()["AnalysisExports"]["QueryPackageIntegrations.1001223652"](packageId, version, targetFramework);
  const $parsed: unknown = JSON.parse($result);
  return $parsed as BrowserPackageIntegrations;
}

export async function queryPackageOpportunities(packageId: string, version: string, targetFramework: string): Promise<BrowserPackageOpportunities> {
  const $result = await $requireManagedExports()["AnalysisExports"]["QueryPackageOpportunities.1001223652"](packageId, version, targetFramework);
  const $parsed: unknown = JSON.parse($result);
  return $parsed as BrowserPackageOpportunities;
}

export async function queryPackagePerformance(packageId: string, version: string, targetFramework: string): Promise<BrowserPackagePerformance> {
  const $result = await $requireManagedExports()["AnalysisExports"]["QueryPackagePerformance.1001223652"](packageId, version, targetFramework);
  const $parsed: unknown = JSON.parse($result);
  return $parsed as BrowserPackagePerformance;
}

export async function queryPlatformIntegrations(targetFramework: string, platformVersion: string, assemblyFileName: string, pack: string): Promise<BrowserPackageIntegrations> {
  const $result = await $requireManagedExports()["AnalysisExports"]["QueryPlatformIntegrations.1579276339"](targetFramework, platformVersion, assemblyFileName, pack);
  const $parsed: unknown = JSON.parse($result);
  return $parsed as BrowserPackageIntegrations;
}

export async function queryPlatformOpportunities(targetFramework: string, platformVersion: string, assemblyFileName: string, pack: string): Promise<BrowserPackageOpportunities> {
  const $result = await $requireManagedExports()["AnalysisExports"]["QueryPlatformOpportunities.1579276339"](targetFramework, platformVersion, assemblyFileName, pack);
  const $parsed: unknown = JSON.parse($result);
  return $parsed as BrowserPackageOpportunities;
}

export async function queryPlatformPerformance(targetFramework: string, platformVersion: string, assemblyFileName: string, pack: string): Promise<string> {
  return await $requireManagedExports()["AnalysisExports"]["QueryPlatformPerformance.1579276339"](targetFramework, platformVersion, assemblyFileName, pack);
}

