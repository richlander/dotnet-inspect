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

export interface SharedProducerSnapshot {
  readonly bodyStarts: number;
  readonly waiterCount: number;
  readonly activeOperations: number;
  readonly operations: number;
  readonly settledOperations: number;
  readonly stopRequests: number;
  readonly producerCanceled: boolean;
  readonly eventsClosed: boolean;
  readonly finalizing: boolean;
  readonly producerCompleted: boolean;
}

export interface SharedVerificationReceipt {
  readonly status: string;
  readonly producerStarts: number;
  readonly waiterCalls: number;
  readonly succeededWaiters: number;
  readonly canceledWaiters: number;
  readonly failedWaiters: number;
  readonly observerFailures: number;
  readonly cleanupFailures: number;
  readonly otherBoundaryFailures: number;
  readonly stopRequests: number;
  readonly releasedProducers: number;
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
          readonly "CompleteSharedProducer.91425100": (producerId: string) => boolean;
          readonly "CreateSharedProducer.677461511": (producerId: string, mode: string) => void;
          readonly "FinishSharedFinalization.91425100": (producerId: string) => boolean;
          readonly "GetSharedSnapshot.304094707": (producerId: string) => string;
          readonly "ReleaseSharedProducer.92020726": (producerId: string) => void;
          readonly "ReportRetainedProgress.91425100": (operationId: string) => boolean;
          readonly "ReportSharedProgress.381764023": (producerId: string, sequence: number) => boolean;
          readonly "RequestCancellation.271973316": (operationId: string, reason: string) => string;
          readonly "RunOperation.2048416469": (operationId: string, mode: string, progressCallback: (arg0: number, arg1: string, arg2: boolean) => undefined, retainProgress: boolean) => Promise<string>;
          readonly "RunSharedOperation.1965950847": (operationId: string, producerId: string, progressCallback: (arg0: number, arg1: string, arg2: boolean) => undefined) => Promise<string>;
          readonly "RunWithoutProgress.976702342": (operationId: string) => Promise<string>;
          readonly "VerifyBaseline.1310674786": () => string;
          readonly "VerifySharedBaseline.1310674786": () => string;
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
    value = $ownDataProperty(value, "CompleteSharedProducer.91425100");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027InspectWeb.ManagedOperationBridge.BrowserCanary.Exports.CompleteSharedProducer.91425100\u0027 is not callable.");
    }
  }
  {
    let value: unknown = exports;
    value = $ownDataProperty(value, "InspectWeb");
    value = $ownDataProperty(value, "ManagedOperationBridge");
    value = $ownDataProperty(value, "BrowserCanary");
    value = $ownDataProperty(value, "Exports");
    value = $ownDataProperty(value, "CreateSharedProducer.677461511");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027InspectWeb.ManagedOperationBridge.BrowserCanary.Exports.CreateSharedProducer.677461511\u0027 is not callable.");
    }
  }
  {
    let value: unknown = exports;
    value = $ownDataProperty(value, "InspectWeb");
    value = $ownDataProperty(value, "ManagedOperationBridge");
    value = $ownDataProperty(value, "BrowserCanary");
    value = $ownDataProperty(value, "Exports");
    value = $ownDataProperty(value, "FinishSharedFinalization.91425100");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027InspectWeb.ManagedOperationBridge.BrowserCanary.Exports.FinishSharedFinalization.91425100\u0027 is not callable.");
    }
  }
  {
    let value: unknown = exports;
    value = $ownDataProperty(value, "InspectWeb");
    value = $ownDataProperty(value, "ManagedOperationBridge");
    value = $ownDataProperty(value, "BrowserCanary");
    value = $ownDataProperty(value, "Exports");
    value = $ownDataProperty(value, "GetSharedSnapshot.304094707");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027InspectWeb.ManagedOperationBridge.BrowserCanary.Exports.GetSharedSnapshot.304094707\u0027 is not callable.");
    }
  }
  {
    let value: unknown = exports;
    value = $ownDataProperty(value, "InspectWeb");
    value = $ownDataProperty(value, "ManagedOperationBridge");
    value = $ownDataProperty(value, "BrowserCanary");
    value = $ownDataProperty(value, "Exports");
    value = $ownDataProperty(value, "ReleaseSharedProducer.92020726");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027InspectWeb.ManagedOperationBridge.BrowserCanary.Exports.ReleaseSharedProducer.92020726\u0027 is not callable.");
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
    value = $ownDataProperty(value, "ReportSharedProgress.381764023");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027InspectWeb.ManagedOperationBridge.BrowserCanary.Exports.ReportSharedProgress.381764023\u0027 is not callable.");
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
    value = $ownDataProperty(value, "RunSharedOperation.1965950847");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027InspectWeb.ManagedOperationBridge.BrowserCanary.Exports.RunSharedOperation.1965950847\u0027 is not callable.");
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
  {
    let value: unknown = exports;
    value = $ownDataProperty(value, "InspectWeb");
    value = $ownDataProperty(value, "ManagedOperationBridge");
    value = $ownDataProperty(value, "BrowserCanary");
    value = $ownDataProperty(value, "Exports");
    value = $ownDataProperty(value, "VerifySharedBaseline.1310674786");
    if (typeof value !== "function") {
      throw new Error("Managed export \u0027InspectWeb.ManagedOperationBridge.BrowserCanary.Exports.VerifySharedBaseline.1310674786\u0027 is not callable.");
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

export function completeSharedProducer(producerId: string): boolean {
  return $requireManagedExports()["InspectWeb"]["ManagedOperationBridge"]["BrowserCanary"]["Exports"]["CompleteSharedProducer.91425100"](producerId);
}

export function createSharedProducer(producerId: string, mode: string): void {
  return $requireManagedExports()["InspectWeb"]["ManagedOperationBridge"]["BrowserCanary"]["Exports"]["CreateSharedProducer.677461511"](producerId, mode);
}

export function finishSharedFinalization(producerId: string): boolean {
  return $requireManagedExports()["InspectWeb"]["ManagedOperationBridge"]["BrowserCanary"]["Exports"]["FinishSharedFinalization.91425100"](producerId);
}

export function getSharedSnapshot(producerId: string): SharedProducerSnapshot {
  const $result = $requireManagedExports()["InspectWeb"]["ManagedOperationBridge"]["BrowserCanary"]["Exports"]["GetSharedSnapshot.304094707"](producerId);
  const $parsed: unknown = JSON.parse($result);
  return $parsed as SharedProducerSnapshot;
}

export function releaseSharedProducer(producerId: string): void {
  return $requireManagedExports()["InspectWeb"]["ManagedOperationBridge"]["BrowserCanary"]["Exports"]["ReleaseSharedProducer.92020726"](producerId);
}

export function reportRetainedProgress(operationId: string): boolean {
  return $requireManagedExports()["InspectWeb"]["ManagedOperationBridge"]["BrowserCanary"]["Exports"]["ReportRetainedProgress.91425100"](operationId);
}

export function reportSharedProgress(producerId: string, sequence: number): boolean {
  return $requireManagedExports()["InspectWeb"]["ManagedOperationBridge"]["BrowserCanary"]["Exports"]["ReportSharedProgress.381764023"](producerId, sequence);
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

export async function runSharedOperation(operationId: string, producerId: string, progressCallback: (arg0: number, arg1: string, arg2: boolean) => undefined): Promise<OperationResultEnvelope> {
  const $result = await $requireManagedExports()["InspectWeb"]["ManagedOperationBridge"]["BrowserCanary"]["Exports"]["RunSharedOperation.1965950847"](operationId, producerId, progressCallback);
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

export function verifySharedBaseline(): SharedVerificationReceipt {
  const $result = $requireManagedExports()["InspectWeb"]["ManagedOperationBridge"]["BrowserCanary"]["Exports"]["VerifySharedBaseline.1310674786"]();
  const $parsed: unknown = JSON.parse($result);
  return $parsed as SharedVerificationReceipt;
}
