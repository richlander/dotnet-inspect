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
        value = $ownDataProperty(value, "AnalysisExports");
        value = $ownDataProperty(value, "QueryMemberFacts.581406856");
        if (typeof value !== "function") {
            throw new Error("Managed export \u0027AnalysisExports.QueryMemberFacts.581406856\u0027 is not callable.");
        }
    }
    {
        let value = exports;
        value = $ownDataProperty(value, "AnalysisExports");
        value = $ownDataProperty(value, "QueryPackageIntegrations.1579276339");
        if (typeof value !== "function") {
            throw new Error("Managed export \u0027AnalysisExports.QueryPackageIntegrations.1579276339\u0027 is not callable.");
        }
    }
    {
        let value = exports;
        value = $ownDataProperty(value, "AnalysisExports");
        value = $ownDataProperty(value, "QueryPackageOpportunities.1579276339");
        if (typeof value !== "function") {
            throw new Error("Managed export \u0027AnalysisExports.QueryPackageOpportunities.1579276339\u0027 is not callable.");
        }
    }
    {
        let value = exports;
        value = $ownDataProperty(value, "AnalysisExports");
        value = $ownDataProperty(value, "QueryPackagePerformance.1579276339");
        if (typeof value !== "function") {
            throw new Error("Managed export \u0027AnalysisExports.QueryPackagePerformance.1579276339\u0027 is not callable.");
        }
    }
    {
        let value = exports;
        value = $ownDataProperty(value, "AnalysisExports");
        value = $ownDataProperty(value, "QueryPlatformIntegrations.1579276339");
        if (typeof value !== "function") {
            throw new Error("Managed export \u0027AnalysisExports.QueryPlatformIntegrations.1579276339\u0027 is not callable.");
        }
    }
    {
        let value = exports;
        value = $ownDataProperty(value, "AnalysisExports");
        value = $ownDataProperty(value, "QueryPlatformOpportunities.1579276339");
        if (typeof value !== "function") {
            throw new Error("Managed export \u0027AnalysisExports.QueryPlatformOpportunities.1579276339\u0027 is not callable.");
        }
    }
    {
        let value = exports;
        value = $ownDataProperty(value, "AnalysisExports");
        value = $ownDataProperty(value, "QueryPlatformPerformance.1579276339");
        if (typeof value !== "function") {
            throw new Error("Managed export \u0027AnalysisExports.QueryPlatformPerformance.1579276339\u0027 is not callable.");
        }
    }
}
async function $initializeRuntimeCore(runtime) {
    const exports = await runtime.getAssemblyExports("InspectWeb.Engine.AnalysisExports");
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
export async function queryMemberFacts(packageId, version, targetFramework, assemblyName, typeIdentity, memberName, memberSignature, selectorKey, metadataToken, implementationBodySelected) {
    const $result = await $requireManagedExports()["AnalysisExports"]["QueryMemberFacts.581406856"](packageId, version, targetFramework, assemblyName, typeIdentity, memberName, memberSignature, selectorKey, metadataToken, implementationBodySelected);
    const $parsed = JSON.parse($result);
    return $parsed;
}
export async function queryPackageIntegrations(packageId, version, targetFramework, assemblyName) {
    const $result = await $requireManagedExports()["AnalysisExports"]["QueryPackageIntegrations.1579276339"](packageId, version, targetFramework, assemblyName);
    const $parsed = JSON.parse($result);
    return $parsed;
}
export async function queryPackageOpportunities(packageId, version, targetFramework, assemblyName) {
    const $result = await $requireManagedExports()["AnalysisExports"]["QueryPackageOpportunities.1579276339"](packageId, version, targetFramework, assemblyName);
    const $parsed = JSON.parse($result);
    return $parsed;
}
export async function queryPackagePerformance(packageId, version, targetFramework, assemblyName) {
    const $result = await $requireManagedExports()["AnalysisExports"]["QueryPackagePerformance.1579276339"](packageId, version, targetFramework, assemblyName);
    const $parsed = JSON.parse($result);
    return $parsed;
}
export async function queryPlatformIntegrations(targetFramework, platformVersion, assemblyFileName, pack) {
    const $result = await $requireManagedExports()["AnalysisExports"]["QueryPlatformIntegrations.1579276339"](targetFramework, platformVersion, assemblyFileName, pack);
    const $parsed = JSON.parse($result);
    return $parsed;
}
export async function queryPlatformOpportunities(targetFramework, platformVersion, assemblyFileName, pack) {
    const $result = await $requireManagedExports()["AnalysisExports"]["QueryPlatformOpportunities.1579276339"](targetFramework, platformVersion, assemblyFileName, pack);
    const $parsed = JSON.parse($result);
    return $parsed;
}
export async function queryPlatformPerformance(targetFramework, platformVersion, assemblyFileName, pack) {
    return await $requireManagedExports()["AnalysisExports"]["QueryPlatformPerformance.1579276339"](targetFramework, platformVersion, assemblyFileName, pack);
}
