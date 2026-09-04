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
        value = $ownDataProperty(value, "InspectionEngine");
        value = $ownDataProperty(value, "AsyncLoweringCanary.1684317047");
        if (typeof value !== "function") {
            throw new Error("Managed export \u0027InspectionEngine.AsyncLoweringCanary.1684317047\u0027 is not callable.");
        }
    }
    {
        let value = exports;
        value = $ownDataProperty(value, "InspectionEngine");
        value = $ownDataProperty(value, "BuildIdentity.1310674786");
        if (typeof value !== "function") {
            throw new Error("Managed export \u0027InspectionEngine.BuildIdentity.1310674786\u0027 is not callable.");
        }
    }
    {
        let value = exports;
        value = $ownDataProperty(value, "InspectionEngine");
        value = $ownDataProperty(value, "ConfigureHost.92020726");
        if (typeof value !== "function") {
            throw new Error("Managed export \u0027InspectionEngine.ConfigureHost.92020726\u0027 is not callable.");
        }
    }
}
async function $initializeRuntimeCore(runtime) {
    const exports = await runtime.getAssemblyExports("InspectWeb.Engine");
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
export async function asyncLoweringCanary() {
    return await $requireManagedExports()["InspectionEngine"]["AsyncLoweringCanary.1684317047"]();
}
export function buildIdentity() {
    const $result = $requireManagedExports()["InspectionEngine"]["BuildIdentity.1310674786"]();
    const $parsed = JSON.parse($result);
    return $parsed;
}
export function configureHost(origin) {
    return $requireManagedExports()["InspectionEngine"]["ConfigureHost.92020726"](origin);
}
