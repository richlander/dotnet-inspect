// GENERATED FILE - DO NOT EDIT BY HAND.
//
// Generated from InspectWeb.ManagedOperationBridge.BrowserCanary.dll by:
//   eng/generate-inspect-web-managed-operation-bridge-canary.sh
// CI fails if this facade drifts.

import { dotnet } from "../_framework/dotnet.js";

export type CancellationRequestKind = "Requested" | "AlreadyRequested" | "NotActive" | number;

export type OperationCancelReason = "User" | "Superseded" | "Disposed" | "FeatureObserverFailed" | "Timeout" | "WorkerRestarted" | number;

export type OperationFailureKind = "Expected" | "Unexpected" | number;

export type OperationResultKind = "Succeeded" | "Failed" | "Canceled" | number;

export interface CancellationRequestReceipt {
  readonly kind: CancellationRequestKind;
  readonly reason: OperationCancelReason | null;
}

export interface OperationResultEnvelope {
  readonly kind: OperationResultKind;
  readonly value: string | null;
  readonly failureKind: OperationFailureKind | null;
  readonly error: string | null;
  readonly diagnostic: string | null;
  readonly cancelReason: OperationCancelReason | null;
}

export interface VerificationReceipt {
  readonly status: string;
  readonly bodyStarts: number;
  readonly withoutProgressStarts: number;
  readonly cancellationRequests: number;
  readonly completions: number;
  readonly retainedReports: number;
  readonly duplicateBoundaryFailures: number;
  readonly progressBoundaryFailures: number;
  readonly malformedInputFailures: number;
  readonly otherBoundaryFailures: number;
}

type $ManagedExports = {
  readonly "InspectWeb": {
    readonly "ManagedOperationBridge": {
      readonly "BrowserCanary": {
        readonly "Exports": {
          readonly "CompleteOperation.91425100": (operationId: string) => boolean;
          readonly "ReportRetainedProgress.91425100": (operationId: string) => boolean;
          readonly "RequestCancellation.271973316": (operationId: string, reason: string) => string;
          readonly "RunOperation.2048416469": (operationId: string, mode: string, progressCallback: (arg0: number, arg1: string, arg2: boolean) => undefined, retainProgress: boolean) => Promise<string>;
          readonly "RunWithoutProgress.976702342": (operationId: string) => Promise<string>;
          readonly "VerifyBaseline.1310674786": () => string;
        };
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
    value = $ownDataProperty(value, "InspectWeb");
    value = $ownDataProperty(value, "ManagedOperationBridge");
    value = $ownDataProperty(value, "BrowserCanary");
    value = $ownDataProperty(value, "Exports");
    value = $ownDataProperty(value, "CompleteOperation.91425100");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027InspectWeb.ManagedOperationBridge.BrowserCanary.Exports.CompleteOperation.91425100\u0027 is not callable.");
    }
  }
  {
    let value: unknown = exports;
    value = $ownDataProperty(value, "InspectWeb");
    value = $ownDataProperty(value, "ManagedOperationBridge");
    value = $ownDataProperty(value, "BrowserCanary");
    value = $ownDataProperty(value, "Exports");
    value = $ownDataProperty(value, "ReportRetainedProgress.91425100");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027InspectWeb.ManagedOperationBridge.BrowserCanary.Exports.ReportRetainedProgress.91425100\u0027 is not callable.");
    }
  }
  {
    let value: unknown = exports;
    value = $ownDataProperty(value, "InspectWeb");
    value = $ownDataProperty(value, "ManagedOperationBridge");
    value = $ownDataProperty(value, "BrowserCanary");
    value = $ownDataProperty(value, "Exports");
    value = $ownDataProperty(value, "RequestCancellation.271973316");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027InspectWeb.ManagedOperationBridge.BrowserCanary.Exports.RequestCancellation.271973316\u0027 is not callable.");
    }
  }
  {
    let value: unknown = exports;
    value = $ownDataProperty(value, "InspectWeb");
    value = $ownDataProperty(value, "ManagedOperationBridge");
    value = $ownDataProperty(value, "BrowserCanary");
    value = $ownDataProperty(value, "Exports");
    value = $ownDataProperty(value, "RunOperation.2048416469");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027InspectWeb.ManagedOperationBridge.BrowserCanary.Exports.RunOperation.2048416469\u0027 is not callable.");
    }
  }
  {
    let value: unknown = exports;
    value = $ownDataProperty(value, "InspectWeb");
    value = $ownDataProperty(value, "ManagedOperationBridge");
    value = $ownDataProperty(value, "BrowserCanary");
    value = $ownDataProperty(value, "Exports");
    value = $ownDataProperty(value, "RunWithoutProgress.976702342");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027InspectWeb.ManagedOperationBridge.BrowserCanary.Exports.RunWithoutProgress.976702342\u0027 is not callable.");
    }
  }
  {
    let value: unknown = exports;
    value = $ownDataProperty(value, "InspectWeb");
    value = $ownDataProperty(value, "ManagedOperationBridge");
    value = $ownDataProperty(value, "BrowserCanary");
    value = $ownDataProperty(value, "Exports");
    value = $ownDataProperty(value, "VerifyBaseline.1310674786");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027InspectWeb.ManagedOperationBridge.BrowserCanary.Exports.VerifyBaseline.1310674786\u0027 is not callable.");
    }
  }
}

async function $initializeRuntimeCore(
  runtime: JsExportRuntime,
): Promise<void> {
  const exports: unknown = await runtime.getAssemblyExports("InspectWeb.ManagedOperationBridge.BrowserCanary");
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

export function completeOperation(operationId: string): boolean {
  return $requireManagedExports()["InspectWeb"]["ManagedOperationBridge"]["BrowserCanary"]["Exports"]["CompleteOperation.91425100"](operationId);
}

export function reportRetainedProgress(operationId: string): boolean {
  return $requireManagedExports()["InspectWeb"]["ManagedOperationBridge"]["BrowserCanary"]["Exports"]["ReportRetainedProgress.91425100"](operationId);
}

export function requestCancellation(operationId: string, reason: string): CancellationRequestReceipt {
  const $result = $requireManagedExports()["InspectWeb"]["ManagedOperationBridge"]["BrowserCanary"]["Exports"]["RequestCancellation.271973316"](operationId, reason);
  const $parsed: unknown = JSON.parse($result);
  return $parsed as CancellationRequestReceipt;
}

export async function runOperation(operationId: string, mode: string, progressCallback: (arg0: number, arg1: string, arg2: boolean) => undefined, retainProgress: boolean): Promise<OperationResultEnvelope> {
  const $result = await $requireManagedExports()["InspectWeb"]["ManagedOperationBridge"]["BrowserCanary"]["Exports"]["RunOperation.2048416469"](operationId, mode, progressCallback, retainProgress);
  const $parsed: unknown = JSON.parse($result);
  return $parsed as OperationResultEnvelope;
}

export async function runWithoutProgress(operationId: string): Promise<OperationResultEnvelope> {
  const $result = await $requireManagedExports()["InspectWeb"]["ManagedOperationBridge"]["BrowserCanary"]["Exports"]["RunWithoutProgress.976702342"](operationId);
  const $parsed: unknown = JSON.parse($result);
  return $parsed as OperationResultEnvelope;
}

export function verifyBaseline(): VerificationReceipt {
  const $result = $requireManagedExports()["InspectWeb"]["ManagedOperationBridge"]["BrowserCanary"]["Exports"]["VerifyBaseline.1310674786"]();
  const $parsed: unknown = JSON.parse($result);
  return $parsed as VerificationReceipt;
}
