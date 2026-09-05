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
        value = $ownDataProperty(value, "SourceExports");
        value = $ownDataProperty(value, "CancelSourceQuery.19325221");
        if (typeof value !== "function") {
            throw new Error("Managed export \u0027SourceExports.CancelSourceQuery.19325221\u0027 is not callable.");
        }
    }
    {
        let value = exports;
        value = $ownDataProperty(value, "SourceExports");
        value = $ownDataProperty(value, "QueryMemberAnnotatedSource.1135530322");
        if (typeof value !== "function") {
            throw new Error("Managed export \u0027SourceExports.QueryMemberAnnotatedSource.1135530322\u0027 is not callable.");
        }
    }
    {
        let value = exports;
        value = $ownDataProperty(value, "SourceExports");
        value = $ownDataProperty(value, "QueryMemberSource.641907440");
        if (typeof value !== "function") {
            throw new Error("Managed export \u0027SourceExports.QueryMemberSource.641907440\u0027 is not callable.");
        }
    }
    {
        let value = exports;
        value = $ownDataProperty(value, "SourceExports");
        value = $ownDataProperty(value, "QueryTypeMemberSource.641907440");
        if (typeof value !== "function") {
            throw new Error("Managed export \u0027SourceExports.QueryTypeMemberSource.641907440\u0027 is not callable.");
        }
    }
    {
        let value = exports;
        value = $ownDataProperty(value, "SourceExports");
        value = $ownDataProperty(value, "QueryTypeSource.649160465");
        if (typeof value !== "function") {
            throw new Error("Managed export \u0027SourceExports.QueryTypeSource.649160465\u0027 is not callable.");
        }
    }
}
async function $initializeRuntimeCore(runtime) {
    const exports = await runtime.getAssemblyExports("InspectWeb.Engine.SourceExports");
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
export function cancelSourceQuery() {
    return $requireManagedExports()["SourceExports"]["CancelSourceQuery.19325221"]();
}
export async function queryMemberAnnotatedSource(packageId, version, targetFramework, assemblyName, typeIdentity, typeQueryId, memberName, memberSignature, selectorKey, metadataToken, styleOptionsJson) {
    const $result = await $requireManagedExports()["SourceExports"]["QueryMemberAnnotatedSource.1135530322"](packageId, version, targetFramework, assemblyName, typeIdentity, typeQueryId, memberName, memberSignature, selectorKey, metadataToken, styleOptionsJson);
    const $parsed = JSON.parse($result);
    return $parsed;
}
export async function queryMemberSource(packageId, version, targetFramework, assemblyName, typeIdentity, memberName, selectorKey, metadataToken, styleOptionsJson) {
    const $result = await $requireManagedExports()["SourceExports"]["QueryMemberSource.641907440"](packageId, version, targetFramework, assemblyName, typeIdentity, memberName, selectorKey, metadataToken, styleOptionsJson);
    const $parsed = JSON.parse($result);
    return $parsed;
}
export async function queryTypeMemberSource(packageId, version, targetFramework, assemblyName, typeIdentity, memberName, selectorKey, metadataToken, styleOptionsJson) {
    const $result = await $requireManagedExports()["SourceExports"]["QueryTypeMemberSource.641907440"](packageId, version, targetFramework, assemblyName, typeIdentity, memberName, selectorKey, metadataToken, styleOptionsJson);
    const $parsed = JSON.parse($result);
    return $parsed;
}
export async function queryTypeSource(packageId, version, targetFramework, assemblyName, typeIdentity, styleOptionsJson) {
    const $result = await $requireManagedExports()["SourceExports"]["QueryTypeSource.649160465"](packageId, version, targetFramework, assemblyName, typeIdentity, styleOptionsJson);
    const $parsed = JSON.parse($result);
    return $parsed;
}
