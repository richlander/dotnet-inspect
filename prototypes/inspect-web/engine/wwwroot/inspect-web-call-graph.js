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
        value = $ownDataProperty(value, "CallGraphExports");
        value = $ownDataProperty(value, "ExpandPlatformCallGraph.1136010516");
        if (typeof value !== "function") {
            throw new Error("Managed export \u0027CallGraphExports.ExpandPlatformCallGraph.1136010516\u0027 is not callable.");
        }
    }
    {
        let value = exports;
        value = $ownDataProperty(value, "CallGraphExports");
        value = $ownDataProperty(value, "QueryMemberCallGraph.1135530322");
        if (typeof value !== "function") {
            throw new Error("Managed export \u0027CallGraphExports.QueryMemberCallGraph.1135530322\u0027 is not callable.");
        }
    }
}
async function $initializeRuntimeCore() {
    const runtime = await dotnet.create();
    const exports = await runtime.getAssemblyExports("InspectWeb.Engine.CallGraphExports");
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
export async function expandPlatformCallGraph(targetFramework, platformVersion, assembly, pack, assemblyVersion, assemblyCulture, assemblyPublicKeyToken, typeFullName, memberName, selectorKey, metadataToken) {
    const $result = await $requireManagedExports()["CallGraphExports"]["ExpandPlatformCallGraph.1136010516"](targetFramework, platformVersion, assembly, pack, assemblyVersion, assemblyCulture, assemblyPublicKeyToken, typeFullName, memberName, selectorKey, metadataToken);
    const $parsed = JSON.parse($result);
    return $parsed;
}
export async function queryMemberCallGraph(packageId, version, targetFramework, assemblyName, typeIdentity, typeQueryId, memberName, memberSignature, selectorKey, metadataToken, workspaceJson) {
    const $result = await $requireManagedExports()["CallGraphExports"]["QueryMemberCallGraph.1135530322"](packageId, version, targetFramework, assemblyName, typeIdentity, typeQueryId, memberName, memberSignature, selectorKey, metadataToken, workspaceJson);
    const $parsed = JSON.parse($result);
    return $parsed;
}
