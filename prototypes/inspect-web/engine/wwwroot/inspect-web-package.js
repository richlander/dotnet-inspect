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
        value = $ownDataProperty(value, "PackageExports");
        value = $ownDataProperty(value, "ActivateWorkspacePackageOccurrence.976702342");
        if (typeof value !== "function") {
            throw new Error("Managed export \u0027PackageExports.ActivateWorkspacePackageOccurrence.976702342\u0027 is not callable.");
        }
    }
    {
        let value = exports;
        value = $ownDataProperty(value, "PackageExports");
        value = $ownDataProperty(value, "CancelPackageQuery.19325221");
        if (typeof value !== "function") {
            throw new Error("Managed export \u0027PackageExports.CancelPackageQuery.19325221\u0027 is not callable.");
        }
    }
    {
        let value = exports;
        value = $ownDataProperty(value, "PackageExports");
        value = $ownDataProperty(value, "ClearWorkspacePackageOccurrences.19325221");
        if (typeof value !== "function") {
            throw new Error("Managed export \u0027PackageExports.ClearWorkspacePackageOccurrences.19325221\u0027 is not callable.");
        }
    }
    {
        let value = exports;
        value = $ownDataProperty(value, "PackageExports");
        value = $ownDataProperty(value, "GetPackageDocument.1001223652");
        if (typeof value !== "function") {
            throw new Error("Managed export \u0027PackageExports.GetPackageDocument.1001223652\u0027 is not callable.");
        }
    }
    {
        let value = exports;
        value = $ownDataProperty(value, "PackageExports");
        value = $ownDataProperty(value, "ListPackageQueryFacets.1310674786");
        if (typeof value !== "function") {
            throw new Error("Managed export \u0027PackageExports.ListPackageQueryFacets.1310674786\u0027 is not callable.");
        }
    }
    {
        let value = exports;
        value = $ownDataProperty(value, "PackageExports");
        value = $ownDataProperty(value, "LoadRuntimePack.451505237");
        if (typeof value !== "function") {
            throw new Error("Managed export \u0027PackageExports.LoadRuntimePack.451505237\u0027 is not callable.");
        }
    }
    {
        let value = exports;
        value = $ownDataProperty(value, "PackageExports");
        value = $ownDataProperty(value, "LoadRuntimePackAssembly.1579276339");
        if (typeof value !== "function") {
            throw new Error("Managed export \u0027PackageExports.LoadRuntimePackAssembly.1579276339\u0027 is not callable.");
        }
    }
    {
        let value = exports;
        value = $ownDataProperty(value, "PackageExports");
        value = $ownDataProperty(value, "MatchPackageDependencyCoordinate.1537767637");
        if (typeof value !== "function") {
            throw new Error("Managed export \u0027PackageExports.MatchPackageDependencyCoordinate.1537767637\u0027 is not callable.");
        }
    }
    {
        let value = exports;
        value = $ownDataProperty(value, "PackageExports");
        value = $ownDataProperty(value, "PackageCacheStats.1310674786");
        if (typeof value !== "function") {
            throw new Error("Managed export \u0027PackageExports.PackageCacheStats.1310674786\u0027 is not callable.");
        }
    }
    {
        let value = exports;
        value = $ownDataProperty(value, "PackageExports");
        value = $ownDataProperty(value, "QueryMemberDocumentation.1330709314");
        if (typeof value !== "function") {
            throw new Error("Managed export \u0027PackageExports.QueryMemberDocumentation.1330709314\u0027 is not callable.");
        }
    }
    {
        let value = exports;
        value = $ownDataProperty(value, "PackageExports");
        value = $ownDataProperty(value, "QueryPackage.1001223652");
        if (typeof value !== "function") {
            throw new Error("Managed export \u0027PackageExports.QueryPackage.1001223652\u0027 is not callable.");
        }
    }
    {
        let value = exports;
        value = $ownDataProperty(value, "PackageExports");
        value = $ownDataProperty(value, "QueryPackageDependencies.1579276339");
        if (typeof value !== "function") {
            throw new Error("Managed export \u0027PackageExports.QueryPackageDependencies.1579276339\u0027 is not callable.");
        }
    }
    {
        let value = exports;
        value = $ownDataProperty(value, "PackageExports");
        value = $ownDataProperty(value, "QueryPackageVersions.976702342");
        if (typeof value !== "function") {
            throw new Error("Managed export \u0027PackageExports.QueryPackageVersions.976702342\u0027 is not callable.");
        }
    }
    {
        let value = exports;
        value = $ownDataProperty(value, "PackageExports");
        value = $ownDataProperty(value, "QueryWorkspacePackageOccurrences.976702342");
        if (typeof value !== "function") {
            throw new Error("Managed export \u0027PackageExports.QueryWorkspacePackageOccurrences.976702342\u0027 is not callable.");
        }
    }
    {
        let value = exports;
        value = $ownDataProperty(value, "PackageExports");
        value = $ownDataProperty(value, "ResolvePackageDependencyVersion.451505237");
        if (typeof value !== "function") {
            throw new Error("Managed export \u0027PackageExports.ResolvePackageDependencyVersion.451505237\u0027 is not callable.");
        }
    }
    {
        let value = exports;
        value = $ownDataProperty(value, "PackageExports");
        value = $ownDataProperty(value, "RunPackageQuery.287304775");
        if (typeof value !== "function") {
            throw new Error("Managed export \u0027PackageExports.RunPackageQuery.287304775\u0027 is not callable.");
        }
    }
    {
        let value = exports;
        value = $ownDataProperty(value, "PackageExports");
        value = $ownDataProperty(value, "SearchTypes.271973316");
        if (typeof value !== "function") {
            throw new Error("Managed export \u0027PackageExports.SearchTypes.271973316\u0027 is not callable.");
        }
    }
}
async function $initializeRuntimeCore(runtime) {
    const exports = await runtime.getAssemblyExports("InspectWeb.Engine.PackageExports");
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
export async function activateWorkspacePackageOccurrence(action) {
    const $result = await $requireManagedExports()["PackageExports"]["ActivateWorkspacePackageOccurrence.976702342"](action);
    const $parsed = JSON.parse($result);
    return $parsed;
}
export function cancelPackageQuery() {
    return $requireManagedExports()["PackageExports"]["CancelPackageQuery.19325221"]();
}
export function clearWorkspacePackageOccurrences() {
    return $requireManagedExports()["PackageExports"]["ClearWorkspacePackageOccurrences.19325221"]();
}
export async function getPackageDocument(packageId, version, path) {
    const $result = await $requireManagedExports()["PackageExports"]["GetPackageDocument.1001223652"](packageId, version, path);
    const $parsed = JSON.parse($result);
    return $parsed;
}
export function listPackageQueryFacets() {
    const $result = $requireManagedExports()["PackageExports"]["ListPackageQueryFacets.1310674786"]();
    const $parsed = JSON.parse($result);
    return $parsed;
}
export async function loadRuntimePack(targetFramework, platformVersion) {
    return await $requireManagedExports()["PackageExports"]["LoadRuntimePack.451505237"](targetFramework, platformVersion);
}
export async function loadRuntimePackAssembly(targetFramework, platformVersion, assemblyFileName, pack) {
    return await $requireManagedExports()["PackageExports"]["LoadRuntimePackAssembly.1579276339"](targetFramework, platformVersion, assemblyFileName, pack);
}
export function matchPackageDependencyCoordinate(packageId, declaredRange, candidatesJson) {
    const $result = $requireManagedExports()["PackageExports"]["MatchPackageDependencyCoordinate.1537767637"](packageId, declaredRange, candidatesJson);
    const $parsed = JSON.parse($result);
    return $parsed;
}
export function packageCacheStats() {
    const $result = $requireManagedExports()["PackageExports"]["PackageCacheStats.1310674786"]();
    const $parsed = JSON.parse($result);
    return $parsed;
}
export async function queryMemberDocumentation(packageId, version, framework, assemblyName, documentationId) {
    const $result = await $requireManagedExports()["PackageExports"]["QueryMemberDocumentation.1330709314"](packageId, version, framework, assemblyName, documentationId);
    const $parsed = JSON.parse($result);
    return $parsed;
}
export async function queryPackage(packageId, version, targetFramework) {
    const $result = await $requireManagedExports()["PackageExports"]["QueryPackage.1001223652"](packageId, version, targetFramework);
    const $parsed = JSON.parse($result);
    return $parsed;
}
export async function queryPackageDependencies(packageId, version, targetFramework, assemblyId) {
    const $result = await $requireManagedExports()["PackageExports"]["QueryPackageDependencies.1579276339"](packageId, version, targetFramework, assemblyId);
    const $parsed = JSON.parse($result);
    return $parsed;
}
export async function queryPackageVersions(packageId) {
    const $result = await $requireManagedExports()["PackageExports"]["QueryPackageVersions.976702342"](packageId);
    const $parsed = JSON.parse($result);
    return $parsed;
}
export async function queryWorkspacePackageOccurrences(workspaceJson) {
    const $result = await $requireManagedExports()["PackageExports"]["QueryWorkspacePackageOccurrences.976702342"](workspaceJson);
    const $parsed = JSON.parse($result);
    return $parsed;
}
export async function resolvePackageDependencyVersion(packageId, declaredRange) {
    return await $requireManagedExports()["PackageExports"]["ResolvePackageDependencyVersion.451505237"](packageId, declaredRange);
}
export async function runPackageQuery(prefix, facetIdsJson, maximumCandidates, maximumMatches, includePrerelease, eventSink) {
    const $result = await $requireManagedExports()["PackageExports"]["RunPackageQuery.287304775"](prefix, facetIdsJson, maximumCandidates, maximumMatches, includePrerelease, eventSink);
    const $parsed = JSON.parse($result);
    return $parsed;
}
export function searchTypes(query, candidatesJson) {
    const $result = $requireManagedExports()["PackageExports"]["SearchTypes.271973316"](query, candidatesJson);
    const $parsed = JSON.parse($result);
    return $parsed;
}
