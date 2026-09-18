import { dotnet } from "./runtime-loader.js";
const $notInitializedError = new Error("The .NET runtime facade is not initialized.");
let $runtime;
let $managedExports;
let $initialization;
let $initializationFailure;
function $ownDataProperty(value, key) {
    if (value === null || (typeof value !== "object" && typeof value !== "function")) {
        throw new Error(`Managed export path '${key}' has a non-object parent.`);
    }
    const descriptor = Object.getOwnPropertyDescriptor(value, key);
    if (descriptor === undefined || !("value" in descriptor)) {
        throw new Error(`Managed export path '${key}' is not an own data property.`);
    }
    return descriptor.value;
}
function $requireRuntime() {
    if ($initializationFailure !== undefined)
        throw $initializationFailure.error;
    if ($runtime === undefined) {
        throw $notInitializedError;
    }
    return $runtime;
}
function $requireManagedExports() {
    if ($initializationFailure !== undefined)
        throw $initializationFailure.error;
    if ($managedExports === undefined) {
        throw $notInitializedError;
    }
    return $managedExports;
}
function $validateManagedExports(exports) {
    {
        let value = exports;
        value = $ownDataProperty(value, "DotnetInspect");
        value = $ownDataProperty(value, "Web");
        value = $ownDataProperty(value, "Interop");
        value = $ownDataProperty(value, "Catalog");
        value = $ownDataProperty(value, "CatalogExports");
        value = $ownDataProperty(value, "ActivateRetainedWorkspacePackageOccurrence.1001223652");
        if (typeof value !== "function") {
            throw new Error("Managed export \u0027DotnetInspect.Web.Interop.Catalog.CatalogExports.ActivateRetainedWorkspacePackageOccurrence.1001223652\u0027 is not callable.");
        }
    }
    {
        let value = exports;
        value = $ownDataProperty(value, "DotnetInspect");
        value = $ownDataProperty(value, "Web");
        value = $ownDataProperty(value, "Interop");
        value = $ownDataProperty(value, "Catalog");
        value = $ownDataProperty(value, "CatalogExports");
        value = $ownDataProperty(value, "CancelRetainedWorkspaceActivation.976702342");
        if (typeof value !== "function") {
            throw new Error("Managed export \u0027DotnetInspect.Web.Interop.Catalog.CatalogExports.CancelRetainedWorkspaceActivation.976702342\u0027 is not callable.");
        }
    }
    {
        let value = exports;
        value = $ownDataProperty(value, "DotnetInspect");
        value = $ownDataProperty(value, "Web");
        value = $ownDataProperty(value, "Interop");
        value = $ownDataProperty(value, "Catalog");
        value = $ownDataProperty(value, "CatalogExports");
        value = $ownDataProperty(value, "CanonicalizeWorkspaceSharePacket.304094707");
        if (typeof value !== "function") {
            throw new Error("Managed export \u0027DotnetInspect.Web.Interop.Catalog.CatalogExports.CanonicalizeWorkspaceSharePacket.304094707\u0027 is not callable.");
        }
    }
    {
        let value = exports;
        value = $ownDataProperty(value, "DotnetInspect");
        value = $ownDataProperty(value, "Web");
        value = $ownDataProperty(value, "Interop");
        value = $ownDataProperty(value, "Catalog");
        value = $ownDataProperty(value, "CatalogExports");
        value = $ownDataProperty(value, "CaptureCompleteWorkspaceShareState.304094707");
        if (typeof value !== "function") {
            throw new Error("Managed export \u0027DotnetInspect.Web.Interop.Catalog.CatalogExports.CaptureCompleteWorkspaceShareState.304094707\u0027 is not callable.");
        }
    }
    {
        let value = exports;
        value = $ownDataProperty(value, "DotnetInspect");
        value = $ownDataProperty(value, "Web");
        value = $ownDataProperty(value, "Interop");
        value = $ownDataProperty(value, "Catalog");
        value = $ownDataProperty(value, "CatalogExports");
        value = $ownDataProperty(value, "CommitRetainedWorkspaceActivation.976702342");
        if (typeof value !== "function") {
            throw new Error("Managed export \u0027DotnetInspect.Web.Interop.Catalog.CatalogExports.CommitRetainedWorkspaceActivation.976702342\u0027 is not callable.");
        }
    }
    {
        let value = exports;
        value = $ownDataProperty(value, "DotnetInspect");
        value = $ownDataProperty(value, "Web");
        value = $ownDataProperty(value, "Interop");
        value = $ownDataProperty(value, "Catalog");
        value = $ownDataProperty(value, "CatalogExports");
        value = $ownDataProperty(value, "DeactivateRetainedWorkspaceDefinition.976702342");
        if (typeof value !== "function") {
            throw new Error("Managed export \u0027DotnetInspect.Web.Interop.Catalog.CatalogExports.DeactivateRetainedWorkspaceDefinition.976702342\u0027 is not callable.");
        }
    }
    {
        let value = exports;
        value = $ownDataProperty(value, "DotnetInspect");
        value = $ownDataProperty(value, "Web");
        value = $ownDataProperty(value, "Interop");
        value = $ownDataProperty(value, "Catalog");
        value = $ownDataProperty(value, "CatalogExports");
        value = $ownDataProperty(value, "DecodeWorkspaceShareState.304094707");
        if (typeof value !== "function") {
            throw new Error("Managed export \u0027DotnetInspect.Web.Interop.Catalog.CatalogExports.DecodeWorkspaceShareState.304094707\u0027 is not callable.");
        }
    }
    {
        let value = exports;
        value = $ownDataProperty(value, "DotnetInspect");
        value = $ownDataProperty(value, "Web");
        value = $ownDataProperty(value, "Interop");
        value = $ownDataProperty(value, "Catalog");
        value = $ownDataProperty(value, "CatalogExports");
        value = $ownDataProperty(value, "EncodeWorkspaceShareState.304094707");
        if (typeof value !== "function") {
            throw new Error("Managed export \u0027DotnetInspect.Web.Interop.Catalog.CatalogExports.EncodeWorkspaceShareState.304094707\u0027 is not callable.");
        }
    }
    {
        let value = exports;
        value = $ownDataProperty(value, "DotnetInspect");
        value = $ownDataProperty(value, "Web");
        value = $ownDataProperty(value, "Interop");
        value = $ownDataProperty(value, "Catalog");
        value = $ownDataProperty(value, "CatalogExports");
        value = $ownDataProperty(value, "ListHomeDemos.1310674786");
        if (typeof value !== "function") {
            throw new Error("Managed export \u0027DotnetInspect.Web.Interop.Catalog.CatalogExports.ListHomeDemos.1310674786\u0027 is not callable.");
        }
    }
    {
        let value = exports;
        value = $ownDataProperty(value, "DotnetInspect");
        value = $ownDataProperty(value, "Web");
        value = $ownDataProperty(value, "Interop");
        value = $ownDataProperty(value, "Catalog");
        value = $ownDataProperty(value, "CatalogExports");
        value = $ownDataProperty(value, "ListVocabulary.1310674786");
        if (typeof value !== "function") {
            throw new Error("Managed export \u0027DotnetInspect.Web.Interop.Catalog.CatalogExports.ListVocabulary.1310674786\u0027 is not callable.");
        }
    }
    {
        let value = exports;
        value = $ownDataProperty(value, "DotnetInspect");
        value = $ownDataProperty(value, "Web");
        value = $ownDataProperty(value, "Interop");
        value = $ownDataProperty(value, "Catalog");
        value = $ownDataProperty(value, "CatalogExports");
        value = $ownDataProperty(value, "ObserveRetainedWorkspaceSettlement.976702342");
        if (typeof value !== "function") {
            throw new Error("Managed export \u0027DotnetInspect.Web.Interop.Catalog.CatalogExports.ObserveRetainedWorkspaceSettlement.976702342\u0027 is not callable.");
        }
    }
    {
        let value = exports;
        value = $ownDataProperty(value, "DotnetInspect");
        value = $ownDataProperty(value, "Web");
        value = $ownDataProperty(value, "Interop");
        value = $ownDataProperty(value, "Catalog");
        value = $ownDataProperty(value, "CatalogExports");
        value = $ownDataProperty(value, "PrepareRetainedWorkspaceDefinition.225870354");
        if (typeof value !== "function") {
            throw new Error("Managed export \u0027DotnetInspect.Web.Interop.Catalog.CatalogExports.PrepareRetainedWorkspaceDefinition.225870354\u0027 is not callable.");
        }
    }
    {
        let value = exports;
        value = $ownDataProperty(value, "DotnetInspect");
        value = $ownDataProperty(value, "Web");
        value = $ownDataProperty(value, "Interop");
        value = $ownDataProperty(value, "Catalog");
        value = $ownDataProperty(value, "CatalogExports");
        value = $ownDataProperty(value, "ResolveHomeDemo.304094707");
        if (typeof value !== "function") {
            throw new Error("Managed export \u0027DotnetInspect.Web.Interop.Catalog.CatalogExports.ResolveHomeDemo.304094707\u0027 is not callable.");
        }
    }
    {
        let value = exports;
        value = $ownDataProperty(value, "DotnetInspect");
        value = $ownDataProperty(value, "Web");
        value = $ownDataProperty(value, "Interop");
        value = $ownDataProperty(value, "Catalog");
        value = $ownDataProperty(value, "CatalogExports");
        value = $ownDataProperty(value, "RunHomeDemo.976702342");
        if (typeof value !== "function") {
            throw new Error("Managed export \u0027DotnetInspect.Web.Interop.Catalog.CatalogExports.RunHomeDemo.976702342\u0027 is not callable.");
        }
    }
}
async function $initializeRuntimeCore(runtime) {
    const exports = await runtime.getAssemblyExports("DotnetInspect.Web.Interop.Catalog");
    $validateManagedExports(exports);
    $runtime = runtime;
    $managedExports = exports;
}
export function createRuntime() {
    return dotnet.create();
}
export function initializeRuntime(runtime) {
    if ($initialization === undefined) {
        $initialization = Promise.resolve()
            .then(() => runtime === undefined ? createRuntime() : runtime)
            .then($initializeRuntimeCore)
            .catch((error) => {
            $initializationFailure = { error };
            throw error;
        });
    }
    return $initialization;
}
export function runEntryPoint(mainAssemblyName, args) {
    return $requireRuntime().runMain(mainAssemblyName, args);
}
export async function activateRetainedWorkspacePackageOccurrence(retainedDefinitionId, realizationId, navigationId) {
    const $result = await $requireManagedExports()["DotnetInspect"]["Web"]["Interop"]["Catalog"]["CatalogExports"]["ActivateRetainedWorkspacePackageOccurrence.1001223652"](retainedDefinitionId, realizationId, navigationId);
    const $parsed = JSON.parse($result);
    return $parsed;
}
export async function cancelRetainedWorkspaceActivation(activationIntentId) {
    const $result = await $requireManagedExports()["DotnetInspect"]["Web"]["Interop"]["Catalog"]["CatalogExports"]["CancelRetainedWorkspaceActivation.976702342"](activationIntentId);
    const $parsed = JSON.parse($result);
    return $parsed;
}
export function canonicalizeWorkspaceSharePacket(encoded) {
    const $result = $requireManagedExports()["DotnetInspect"]["Web"]["Interop"]["Catalog"]["CatalogExports"]["CanonicalizeWorkspaceSharePacket.304094707"](encoded);
    const $parsed = JSON.parse($result);
    return $parsed;
}
export function captureCompleteWorkspaceShareState(stateJson) {
    const $result = $requireManagedExports()["DotnetInspect"]["Web"]["Interop"]["Catalog"]["CatalogExports"]["CaptureCompleteWorkspaceShareState.304094707"](stateJson);
    const $parsed = JSON.parse($result);
    return $parsed;
}
export async function commitRetainedWorkspaceActivation(activationIntentId) {
    const $result = await $requireManagedExports()["DotnetInspect"]["Web"]["Interop"]["Catalog"]["CatalogExports"]["CommitRetainedWorkspaceActivation.976702342"](activationIntentId);
    const $parsed = JSON.parse($result);
    return $parsed;
}
export async function deactivateRetainedWorkspaceDefinition(retainedDefinitionId) {
    const $result = await $requireManagedExports()["DotnetInspect"]["Web"]["Interop"]["Catalog"]["CatalogExports"]["DeactivateRetainedWorkspaceDefinition.976702342"](retainedDefinitionId);
    const $parsed = JSON.parse($result);
    return $parsed;
}
export function decodeWorkspaceShareState(encoded) {
    const $result = $requireManagedExports()["DotnetInspect"]["Web"]["Interop"]["Catalog"]["CatalogExports"]["DecodeWorkspaceShareState.304094707"](encoded);
    const $parsed = JSON.parse($result);
    return $parsed;
}
export function encodeWorkspaceShareState(stateJson) {
    const $result = $requireManagedExports()["DotnetInspect"]["Web"]["Interop"]["Catalog"]["CatalogExports"]["EncodeWorkspaceShareState.304094707"](stateJson);
    const $parsed = JSON.parse($result);
    return $parsed;
}
export function listHomeDemos() {
    const $result = $requireManagedExports()["DotnetInspect"]["Web"]["Interop"]["Catalog"]["CatalogExports"]["ListHomeDemos.1310674786"]();
    const $parsed = JSON.parse($result);
    return $parsed;
}
export function listVocabulary() {
    const $result = $requireManagedExports()["DotnetInspect"]["Web"]["Interop"]["Catalog"]["CatalogExports"]["ListVocabulary.1310674786"]();
    const $parsed = JSON.parse($result);
    return $parsed;
}
export async function observeRetainedWorkspaceSettlement(settlementId) {
    const $result = await $requireManagedExports()["DotnetInspect"]["Web"]["Interop"]["Catalog"]["CatalogExports"]["ObserveRetainedWorkspaceSettlement.976702342"](settlementId);
    const $parsed = JSON.parse($result);
    return $parsed;
}
export async function prepareRetainedWorkspaceDefinition(activationIntentId, retainedDefinitionId, label, canonicalLocation, canonicalPacket, presentationActiveTabIndex) {
    const $result = await $requireManagedExports()["DotnetInspect"]["Web"]["Interop"]["Catalog"]["CatalogExports"]["PrepareRetainedWorkspaceDefinition.225870354"](activationIntentId, retainedDefinitionId, label, canonicalLocation, canonicalPacket, presentationActiveTabIndex);
    const $parsed = JSON.parse($result);
    return $parsed;
}
export function resolveHomeDemo(scenarioId) {
    const $result = $requireManagedExports()["DotnetInspect"]["Web"]["Interop"]["Catalog"]["CatalogExports"]["ResolveHomeDemo.304094707"](scenarioId);
    const $parsed = JSON.parse($result);
    return $parsed;
}
export async function runHomeDemo(scenarioId) {
    const $result = await $requireManagedExports()["DotnetInspect"]["Web"]["Interop"]["Catalog"]["CatalogExports"]["RunHomeDemo.976702342"](scenarioId);
    const $parsed = JSON.parse($result);
    return $parsed;
}
