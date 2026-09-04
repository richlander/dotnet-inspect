// GENERATED FILE - DO NOT EDIT BY HAND.
//
// Generated independently from TsJsExport.MultiFacade.Alpha.dll by:
//   eng/generate-inspect-web-multi-facade-canary.sh
// CI fails if this facade drifts.

import { dotnet } from "../_framework/dotnet.js";

export type Flavor = "Vanilla" | "Chocolate" | number;

export interface Envelope {
  readonly assembly: string;
  readonly value: string;
  readonly flavor: Flavor;
}

type $ManagedExports = {
  readonly "MultiFacade": {
    readonly "Shared": {
      readonly "Exports": {
        readonly "Describe.1859881935": (value: number) => string;
        readonly "Describe.304094707": (value: string) => string;
        readonly "GetEnvelopeAsync.451505237": (value: string, flavor: string) => Promise<string>;
        readonly "Identity.1310674786": () => string;
        readonly "VerifyInvocations.1310674786": () => string;
      };
      readonly "SecondaryExports": {
        readonly "Identity.1310674786": () => string;
      };
    };
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
    value = $ownDataProperty(value, "MultiFacade");
    value = $ownDataProperty(value, "Shared");
    value = $ownDataProperty(value, "Exports");
    value = $ownDataProperty(value, "Describe.1859881935");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027MultiFacade.Shared.Exports.Describe.1859881935\u0027 is not callable.");
    }
  }
  {
    let value: unknown = exports;
    value = $ownDataProperty(value, "MultiFacade");
    value = $ownDataProperty(value, "Shared");
    value = $ownDataProperty(value, "Exports");
    value = $ownDataProperty(value, "Describe.304094707");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027MultiFacade.Shared.Exports.Describe.304094707\u0027 is not callable.");
    }
  }
  {
    let value: unknown = exports;
    value = $ownDataProperty(value, "MultiFacade");
    value = $ownDataProperty(value, "Shared");
    value = $ownDataProperty(value, "Exports");
    value = $ownDataProperty(value, "GetEnvelopeAsync.451505237");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027MultiFacade.Shared.Exports.GetEnvelopeAsync.451505237\u0027 is not callable.");
    }
  }
  {
    let value: unknown = exports;
    value = $ownDataProperty(value, "MultiFacade");
    value = $ownDataProperty(value, "Shared");
    value = $ownDataProperty(value, "Exports");
    value = $ownDataProperty(value, "Identity.1310674786");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027MultiFacade.Shared.Exports.Identity.1310674786\u0027 is not callable.");
    }
  }
  {
    let value: unknown = exports;
    value = $ownDataProperty(value, "MultiFacade");
    value = $ownDataProperty(value, "Shared");
    value = $ownDataProperty(value, "Exports");
    value = $ownDataProperty(value, "VerifyInvocations.1310674786");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027MultiFacade.Shared.Exports.VerifyInvocations.1310674786\u0027 is not callable.");
    }
  }
  {
    let value: unknown = exports;
    value = $ownDataProperty(value, "MultiFacade");
    value = $ownDataProperty(value, "Shared");
    value = $ownDataProperty(value, "SecondaryExports");
    value = $ownDataProperty(value, "Identity.1310674786");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027MultiFacade.Shared.SecondaryExports.Identity.1310674786\u0027 is not callable.");
    }
  }
}

async function $initializeRuntimeCore(
  runtime: JsExportRuntime,
): Promise<void> {
  const exports: unknown = await runtime.getAssemblyExports("TsJsExport.MultiFacade.Alpha");
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

export function describe(value: number): string {
  return $requireManagedExports()["MultiFacade"]["Shared"]["Exports"]["Describe.1859881935"](value);
}

export function operation_87a32703(value: string): string {
  return $requireManagedExports()["MultiFacade"]["Shared"]["Exports"]["Describe.304094707"](value);
}

export async function getEnvelopeAsync(value: string, flavor: string): Promise<Envelope> {
  const $result = await $requireManagedExports()["MultiFacade"]["Shared"]["Exports"]["GetEnvelopeAsync.451505237"](value, flavor);
  const $parsed: unknown = JSON.parse($result);
  return $parsed as Envelope;
}

export function identity(): string {
  return $requireManagedExports()["MultiFacade"]["Shared"]["Exports"]["Identity.1310674786"]();
}

export function verifyInvocations(): string {
  return $requireManagedExports()["MultiFacade"]["Shared"]["Exports"]["VerifyInvocations.1310674786"]();
}

export function operation_9a20007e(): string {
  return $requireManagedExports()["MultiFacade"]["Shared"]["SecondaryExports"]["Identity.1310674786"]();
}

