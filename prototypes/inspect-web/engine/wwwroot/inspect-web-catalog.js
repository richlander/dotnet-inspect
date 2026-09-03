import { dotnet } from "./_framework/dotnet.js";
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
        value = $ownDataProperty(value, "CatalogExports");
        value = $ownDataProperty(value, "DecodeWorkspaceShareState.304094707");
        if (typeof value !== "function") {
            throw new Error("Managed export \u0027CatalogExports.DecodeWorkspaceShareState.304094707\u0027 is not callable.");
        }
    }
    {
        let value = exports;
        value = $ownDataProperty(value, "CatalogExports");
        value = $ownDataProperty(value, "EncodeWorkspaceShareState.304094707");
        if (typeof value !== "function") {
            throw new Error("Managed export \u0027CatalogExports.EncodeWorkspaceShareState.304094707\u0027 is not callable.");
        }
    }
    {
        let value = exports;
        value = $ownDataProperty(value, "CatalogExports");
        value = $ownDataProperty(value, "ListHomeDemos.1310674786");
        if (typeof value !== "function") {
            throw new Error("Managed export \u0027CatalogExports.ListHomeDemos.1310674786\u0027 is not callable.");
        }
    }
    {
        let value = exports;
        value = $ownDataProperty(value, "CatalogExports");
        value = $ownDataProperty(value, "ListVocabulary.1310674786");
        if (typeof value !== "function") {
            throw new Error("Managed export \u0027CatalogExports.ListVocabulary.1310674786\u0027 is not callable.");
        }
    }
    {
        let value = exports;
        value = $ownDataProperty(value, "CatalogExports");
        value = $ownDataProperty(value, "ResolveHomeDemo.304094707");
        if (typeof value !== "function") {
            throw new Error("Managed export \u0027CatalogExports.ResolveHomeDemo.304094707\u0027 is not callable.");
        }
    }
    {
        let value = exports;
        value = $ownDataProperty(value, "CatalogExports");
        value = $ownDataProperty(value, "RunHomeDemo.976702342");
        if (typeof value !== "function") {
            throw new Error("Managed export \u0027CatalogExports.RunHomeDemo.976702342\u0027 is not callable.");
        }
    }
}
async function $initializeRuntimeCore() {
    const runtime = await dotnet.create();
    const exports = await runtime.getAssemblyExports("InspectWeb.Engine.CatalogExports");
    $validateManagedExports(exports);
    $runtime = runtime;
    $managedExports = exports;
}
export function initializeRuntime() {
    if ($initialization === undefined) {
        $initialization = Promise.resolve()
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
export function decodeWorkspaceShareState(encoded) {
    const $result = $requireManagedExports()["CatalogExports"]["DecodeWorkspaceShareState.304094707"](encoded);
    const $parsed = JSON.parse($result);
    return $parsed;
}
export function encodeWorkspaceShareState(stateJson) {
    const $result = $requireManagedExports()["CatalogExports"]["EncodeWorkspaceShareState.304094707"](stateJson);
    const $parsed = JSON.parse($result);
    return $parsed;
}
export function listHomeDemos() {
    const $result = $requireManagedExports()["CatalogExports"]["ListHomeDemos.1310674786"]();
    const $parsed = JSON.parse($result);
    return $parsed;
}
export function listVocabulary() {
    const $result = $requireManagedExports()["CatalogExports"]["ListVocabulary.1310674786"]();
    const $parsed = JSON.parse($result);
    return $parsed;
}
export function resolveHomeDemo(scenarioId) {
    const $result = $requireManagedExports()["CatalogExports"]["ResolveHomeDemo.304094707"](scenarioId);
    const $parsed = JSON.parse($result);
    return $parsed;
}
export async function runHomeDemo(scenarioId) {
    const $result = await $requireManagedExports()["CatalogExports"]["RunHomeDemo.976702342"](scenarioId);
    const $parsed = JSON.parse($result);
    return $parsed;
}
