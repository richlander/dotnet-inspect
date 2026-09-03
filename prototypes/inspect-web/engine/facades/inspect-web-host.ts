import { dotnet, type RuntimeAPI } from "./_framework/dotnet.js";

export interface BrowserBuildIdentity {
  readonly version: string;
  readonly commit: string | null;
  readonly builtAtUtc: string | null;
  readonly commitUrl: string | null;
}

type $ManagedExports = {
  readonly "InspectionEngine": {
    readonly "AsyncLoweringCanary.1684317047": () => Promise<string>;
    readonly "BuildIdentity.1310674786": () => string;
    readonly "ConfigureHost.92020726": (origin: string) => void;
  };
};

const $notInitializedError = new Error("The .NET runtime facade is not initialized.");
let $runtime: RuntimeAPI | undefined;
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

function $requireRuntime(): RuntimeAPI {
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
    value = $ownDataProperty(value, "InspectionEngine");
    value = $ownDataProperty(value, "AsyncLoweringCanary.1684317047");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027InspectionEngine.AsyncLoweringCanary.1684317047\u0027 is not callable.");
    }
  }
  {
    let value: unknown = exports;
    value = $ownDataProperty(value, "InspectionEngine");
    value = $ownDataProperty(value, "BuildIdentity.1310674786");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027InspectionEngine.BuildIdentity.1310674786\u0027 is not callable.");
    }
  }
  {
    let value: unknown = exports;
    value = $ownDataProperty(value, "InspectionEngine");
    value = $ownDataProperty(value, "ConfigureHost.92020726");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027InspectionEngine.ConfigureHost.92020726\u0027 is not callable.");
    }
  }
}

async function $initializeRuntimeCore(): Promise<void> {
  const runtime = await dotnet.create();
  const exports: unknown = await runtime.getAssemblyExports("InspectWeb.Engine");
  $validateManagedExports(exports);
  $runtime = runtime;
  $managedExports = exports;
}

export function initializeRuntime(): Promise<void> {
  if ($initialization === undefined) {
    $initialization = Promise.resolve()
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

export async function asyncLoweringCanary(): Promise<string> {
  return await $requireManagedExports()["InspectionEngine"]["AsyncLoweringCanary.1684317047"]();
}

export function buildIdentity(): BrowserBuildIdentity {
  const $result = $requireManagedExports()["InspectionEngine"]["BuildIdentity.1310674786"]();
  const $parsed: unknown = JSON.parse($result);
  return $parsed as BrowserBuildIdentity;
}

export function configureHost(origin: string): void {
  return $requireManagedExports()["InspectionEngine"]["ConfigureHost.92020726"](origin);
}

