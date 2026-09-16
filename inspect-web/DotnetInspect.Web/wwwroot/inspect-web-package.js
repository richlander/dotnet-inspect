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
        value = $ownDataProperty(value, "Package");
        value = $ownDataProperty(value, "PackageExports");
        value = $ownDataProperty(value, "ActivateWorkspacePackageOccurrence.976702342");
        if (typeof value !== "function") {
            throw new Error("Managed export \u0027DotnetInspect.Web.Interop.Package.PackageExports.ActivateWorkspacePackageOccurrence.976702342\u0027 is not callable.");
        }
    }
    {
        let value = exports;
        value = $ownDataProperty(value, "DotnetInspect");
        value = $ownDataProperty(value, "Web");
        value = $ownDataProperty(value, "Interop");
        value = $ownDataProperty(value, "Package");
        value = $ownDataProperty(value, "PackageExports");
        value = $ownDataProperty(value, "CancelPackageChanges.271973316");
        if (typeof value !== "function") {
            throw new Error("Managed export \u0027DotnetInspect.Web.Interop.Package.PackageExports.CancelPackageChanges.271973316\u0027 is not callable.");
        }
    }
    {
        let value = exports;
        value = $ownDataProperty(value, "DotnetInspect");
        value = $ownDataProperty(value, "Web");
        value = $ownDataProperty(value, "Interop");
        value = $ownDataProperty(value, "Package");
        value = $ownDataProperty(value, "PackageExports");
        value = $ownDataProperty(value, "CancelPackageQuery.271973316");
        if (typeof value !== "function") {
            throw new Error("Managed export \u0027DotnetInspect.Web.Interop.Package.PackageExports.CancelPackageQuery.271973316\u0027 is not callable.");
        }
    }
    {
        let value = exports;
        value = $ownDataProperty(value, "DotnetInspect");
        value = $ownDataProperty(value, "Web");
        value = $ownDataProperty(value, "Interop");
        value = $ownDataProperty(value, "Package");
        value = $ownDataProperty(value, "PackageExports");
        value = $ownDataProperty(value, "ClassifyPackageGraphIdentities.271973316");
        if (typeof value !== "function") {
            throw new Error("Managed export \u0027DotnetInspect.Web.Interop.Package.PackageExports.ClassifyPackageGraphIdentities.271973316\u0027 is not callable.");
        }
    }
    {
        let value = exports;
        value = $ownDataProperty(value, "DotnetInspect");
        value = $ownDataProperty(value, "Web");
        value = $ownDataProperty(value, "Interop");
        value = $ownDataProperty(value, "Package");
        value = $ownDataProperty(value, "PackageExports");
        value = $ownDataProperty(value, "ClearWorkspacePackageOccurrences.1731052262");
        if (typeof value !== "function") {
            throw new Error("Managed export \u0027DotnetInspect.Web.Interop.Package.PackageExports.ClearWorkspacePackageOccurrences.1731052262\u0027 is not callable.");
        }
    }
    {
        let value = exports;
        value = $ownDataProperty(value, "DotnetInspect");
        value = $ownDataProperty(value, "Web");
        value = $ownDataProperty(value, "Interop");
        value = $ownDataProperty(value, "Package");
        value = $ownDataProperty(value, "PackageExports");
        value = $ownDataProperty(value, "GetPackageDocument.1001223652");
        if (typeof value !== "function") {
            throw new Error("Managed export \u0027DotnetInspect.Web.Interop.Package.PackageExports.GetPackageDocument.1001223652\u0027 is not callable.");
        }
    }
    {
        let value = exports;
        value = $ownDataProperty(value, "DotnetInspect");
        value = $ownDataProperty(value, "Web");
        value = $ownDataProperty(value, "Interop");
        value = $ownDataProperty(value, "Package");
        value = $ownDataProperty(value, "PackageExports");
        value = $ownDataProperty(value, "GetPlatformCatalog.451505237");
        if (typeof value !== "function") {
            throw new Error("Managed export \u0027DotnetInspect.Web.Interop.Package.PackageExports.GetPlatformCatalog.451505237\u0027 is not callable.");
        }
    }
    {
        let value = exports;
        value = $ownDataProperty(value, "DotnetInspect");
        value = $ownDataProperty(value, "Web");
        value = $ownDataProperty(value, "Interop");
        value = $ownDataProperty(value, "Package");
        value = $ownDataProperty(value, "PackageExports");
        value = $ownDataProperty(value, "GetPlatformVersions.976702342");
        if (typeof value !== "function") {
            throw new Error("Managed export \u0027DotnetInspect.Web.Interop.Package.PackageExports.GetPlatformVersions.976702342\u0027 is not callable.");
        }
    }
    {
        let value = exports;
        value = $ownDataProperty(value, "DotnetInspect");
        value = $ownDataProperty(value, "Web");
        value = $ownDataProperty(value, "Interop");
        value = $ownDataProperty(value, "Package");
        value = $ownDataProperty(value, "PackageExports");
        value = $ownDataProperty(value, "ListPackageChangesPackageSets.1310674786");
        if (typeof value !== "function") {
            throw new Error("Managed export \u0027DotnetInspect.Web.Interop.Package.PackageExports.ListPackageChangesPackageSets.1310674786\u0027 is not callable.");
        }
    }
    {
        let value = exports;
        value = $ownDataProperty(value, "DotnetInspect");
        value = $ownDataProperty(value, "Web");
        value = $ownDataProperty(value, "Interop");
        value = $ownDataProperty(value, "Package");
        value = $ownDataProperty(value, "PackageExports");
        value = $ownDataProperty(value, "ListPackageQueryCatalog.1310674786");
        if (typeof value !== "function") {
            throw new Error("Managed export \u0027DotnetInspect.Web.Interop.Package.PackageExports.ListPackageQueryCatalog.1310674786\u0027 is not callable.");
        }
    }
    {
        let value = exports;
        value = $ownDataProperty(value, "DotnetInspect");
        value = $ownDataProperty(value, "Web");
        value = $ownDataProperty(value, "Interop");
        value = $ownDataProperty(value, "Package");
        value = $ownDataProperty(value, "PackageExports");
        value = $ownDataProperty(value, "LoadRuntimePack.451505237");
        if (typeof value !== "function") {
            throw new Error("Managed export \u0027DotnetInspect.Web.Interop.Package.PackageExports.LoadRuntimePack.451505237\u0027 is not callable.");
        }
    }
    {
        let value = exports;
        value = $ownDataProperty(value, "DotnetInspect");
        value = $ownDataProperty(value, "Web");
        value = $ownDataProperty(value, "Interop");
        value = $ownDataProperty(value, "Package");
        value = $ownDataProperty(value, "PackageExports");
        value = $ownDataProperty(value, "LoadRuntimePackAssembly.1330709314");
        if (typeof value !== "function") {
            throw new Error("Managed export \u0027DotnetInspect.Web.Interop.Package.PackageExports.LoadRuntimePackAssembly.1330709314\u0027 is not callable.");
        }
    }
    {
        let value = exports;
        value = $ownDataProperty(value, "DotnetInspect");
        value = $ownDataProperty(value, "Web");
        value = $ownDataProperty(value, "Interop");
        value = $ownDataProperty(value, "Package");
        value = $ownDataProperty(value, "PackageExports");
        value = $ownDataProperty(value, "MatchPackageDependencyCoordinate.1537767637");
        if (typeof value !== "function") {
            throw new Error("Managed export \u0027DotnetInspect.Web.Interop.Package.PackageExports.MatchPackageDependencyCoordinate.1537767637\u0027 is not callable.");
        }
    }
    {
        let value = exports;
        value = $ownDataProperty(value, "DotnetInspect");
        value = $ownDataProperty(value, "Web");
        value = $ownDataProperty(value, "Interop");
        value = $ownDataProperty(value, "Package");
        value = $ownDataProperty(value, "PackageExports");
        value = $ownDataProperty(value, "PackageCacheStats.1310674786");
        if (typeof value !== "function") {
            throw new Error("Managed export \u0027DotnetInspect.Web.Interop.Package.PackageExports.PackageCacheStats.1310674786\u0027 is not callable.");
        }
    }
    {
        let value = exports;
        value = $ownDataProperty(value, "DotnetInspect");
        value = $ownDataProperty(value, "Web");
        value = $ownDataProperty(value, "Interop");
        value = $ownDataProperty(value, "Package");
        value = $ownDataProperty(value, "PackageExports");
        value = $ownDataProperty(value, "PrefetchPlatformPacks.1782598084");
        if (typeof value !== "function") {
            throw new Error("Managed export \u0027DotnetInspect.Web.Interop.Package.PackageExports.PrefetchPlatformPacks.1782598084\u0027 is not callable.");
        }
    }
    {
        let value = exports;
        value = $ownDataProperty(value, "DotnetInspect");
        value = $ownDataProperty(value, "Web");
        value = $ownDataProperty(value, "Interop");
        value = $ownDataProperty(value, "Package");
        value = $ownDataProperty(value, "PackageExports");
        value = $ownDataProperty(value, "QueryMemberDocumentation.1330709314");
        if (typeof value !== "function") {
            throw new Error("Managed export \u0027DotnetInspect.Web.Interop.Package.PackageExports.QueryMemberDocumentation.1330709314\u0027 is not callable.");
        }
    }
    {
        let value = exports;
        value = $ownDataProperty(value, "DotnetInspect");
        value = $ownDataProperty(value, "Web");
        value = $ownDataProperty(value, "Interop");
        value = $ownDataProperty(value, "Package");
        value = $ownDataProperty(value, "PackageExports");
        value = $ownDataProperty(value, "QueryPackage.1001223652");
        if (typeof value !== "function") {
            throw new Error("Managed export \u0027DotnetInspect.Web.Interop.Package.PackageExports.QueryPackage.1001223652\u0027 is not callable.");
        }
    }
    {
        let value = exports;
        value = $ownDataProperty(value, "DotnetInspect");
        value = $ownDataProperty(value, "Web");
        value = $ownDataProperty(value, "Interop");
        value = $ownDataProperty(value, "Package");
        value = $ownDataProperty(value, "PackageExports");
        value = $ownDataProperty(value, "QueryPackageDependencies.1579276339");
        if (typeof value !== "function") {
            throw new Error("Managed export \u0027DotnetInspect.Web.Interop.Package.PackageExports.QueryPackageDependencies.1579276339\u0027 is not callable.");
        }
    }
    {
        let value = exports;
        value = $ownDataProperty(value, "DotnetInspect");
        value = $ownDataProperty(value, "Web");
        value = $ownDataProperty(value, "Interop");
        value = $ownDataProperty(value, "Package");
        value = $ownDataProperty(value, "PackageExports");
        value = $ownDataProperty(value, "QueryPackagePruning.1579276339");
        if (typeof value !== "function") {
            throw new Error("Managed export \u0027DotnetInspect.Web.Interop.Package.PackageExports.QueryPackagePruning.1579276339\u0027 is not callable.");
        }
    }
    {
        let value = exports;
        value = $ownDataProperty(value, "DotnetInspect");
        value = $ownDataProperty(value, "Web");
        value = $ownDataProperty(value, "Interop");
        value = $ownDataProperty(value, "Package");
        value = $ownDataProperty(value, "PackageExports");
        value = $ownDataProperty(value, "QueryPackageVersions.451505237");
        if (typeof value !== "function") {
            throw new Error("Managed export \u0027DotnetInspect.Web.Interop.Package.PackageExports.QueryPackageVersions.451505237\u0027 is not callable.");
        }
    }
    {
        let value = exports;
        value = $ownDataProperty(value, "DotnetInspect");
        value = $ownDataProperty(value, "Web");
        value = $ownDataProperty(value, "Interop");
        value = $ownDataProperty(value, "Package");
        value = $ownDataProperty(value, "PackageExports");
        value = $ownDataProperty(value, "QueryWorkspacePackageOccurrences.976702342");
        if (typeof value !== "function") {
            throw new Error("Managed export \u0027DotnetInspect.Web.Interop.Package.PackageExports.QueryWorkspacePackageOccurrences.976702342\u0027 is not callable.");
        }
    }
    {
        let value = exports;
        value = $ownDataProperty(value, "DotnetInspect");
        value = $ownDataProperty(value, "Web");
        value = $ownDataProperty(value, "Interop");
        value = $ownDataProperty(value, "Package");
        value = $ownDataProperty(value, "PackageExports");
        value = $ownDataProperty(value, "RequestPackageQueryMatches.146925470");
        if (typeof value !== "function") {
            throw new Error("Managed export \u0027DotnetInspect.Web.Interop.Package.PackageExports.RequestPackageQueryMatches.146925470\u0027 is not callable.");
        }
    }
    {
        let value = exports;
        value = $ownDataProperty(value, "DotnetInspect");
        value = $ownDataProperty(value, "Web");
        value = $ownDataProperty(value, "Interop");
        value = $ownDataProperty(value, "Package");
        value = $ownDataProperty(value, "PackageExports");
        value = $ownDataProperty(value, "ResolvePackageDependencyVersion.451505237");
        if (typeof value !== "function") {
            throw new Error("Managed export \u0027DotnetInspect.Web.Interop.Package.PackageExports.ResolvePackageDependencyVersion.451505237\u0027 is not callable.");
        }
    }
    {
        let value = exports;
        value = $ownDataProperty(value, "DotnetInspect");
        value = $ownDataProperty(value, "Web");
        value = $ownDataProperty(value, "Interop");
        value = $ownDataProperty(value, "Package");
        value = $ownDataProperty(value, "PackageExports");
        value = $ownDataProperty(value, "RunPackageChanges.1791926993");
        if (typeof value !== "function") {
            throw new Error("Managed export \u0027DotnetInspect.Web.Interop.Package.PackageExports.RunPackageChanges.1791926993\u0027 is not callable.");
        }
    }
    {
        let value = exports;
        value = $ownDataProperty(value, "DotnetInspect");
        value = $ownDataProperty(value, "Web");
        value = $ownDataProperty(value, "Interop");
        value = $ownDataProperty(value, "Package");
        value = $ownDataProperty(value, "PackageExports");
        value = $ownDataProperty(value, "RunPackageQuery.1685943924");
        if (typeof value !== "function") {
            throw new Error("Managed export \u0027DotnetInspect.Web.Interop.Package.PackageExports.RunPackageQuery.1685943924\u0027 is not callable.");
        }
    }
    {
        let value = exports;
        value = $ownDataProperty(value, "DotnetInspect");
        value = $ownDataProperty(value, "Web");
        value = $ownDataProperty(value, "Interop");
        value = $ownDataProperty(value, "Package");
        value = $ownDataProperty(value, "PackageExports");
        value = $ownDataProperty(value, "SearchTypes.271973316");
        if (typeof value !== "function") {
            throw new Error("Managed export \u0027DotnetInspect.Web.Interop.Package.PackageExports.SearchTypes.271973316\u0027 is not callable.");
        }
    }
}
async function $initializeRuntimeCore(runtime) {
    const exports = await runtime.getAssemblyExports("DotnetInspect.Web.Interop.Package");
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
    const $result = await $requireManagedExports()["DotnetInspect"]["Web"]["Interop"]["Package"]["PackageExports"]["ActivateWorkspacePackageOccurrence.976702342"](action);
    const $parsed = JSON.parse($result);
    return $parsed;
}
export function cancelPackageChanges(operationId, reason) {
    const $result = $requireManagedExports()["DotnetInspect"]["Web"]["Interop"]["Package"]["PackageExports"]["CancelPackageChanges.271973316"](operationId, reason);
    const $parsed = JSON.parse($result);
    return $parsed;
}
export function cancelPackageQuery(operationId, reason) {
    const $result = $requireManagedExports()["DotnetInspect"]["Web"]["Interop"]["Package"]["PackageExports"]["CancelPackageQuery.271973316"](operationId, reason);
    const $parsed = JSON.parse($result);
    return $parsed;
}
export function classifyPackageGraphIdentities(inspectedPackageId, packageIdsJson) {
    const $result = $requireManagedExports()["DotnetInspect"]["Web"]["Interop"]["Package"]["PackageExports"]["ClassifyPackageGraphIdentities.271973316"](inspectedPackageId, packageIdsJson);
    const $parsed = JSON.parse($result);
    return $parsed;
}
export async function clearWorkspacePackageOccurrences() {
    return await $requireManagedExports()["DotnetInspect"]["Web"]["Interop"]["Package"]["PackageExports"]["ClearWorkspacePackageOccurrences.1731052262"]();
}
export async function getPackageDocument(packageId, version, path) {
    const $result = await $requireManagedExports()["DotnetInspect"]["Web"]["Interop"]["Package"]["PackageExports"]["GetPackageDocument.1001223652"](packageId, version, path);
    const $parsed = JSON.parse($result);
    return $parsed;
}
export async function getPlatformCatalog(targetFramework, platformVersion) {
    const $result = await $requireManagedExports()["DotnetInspect"]["Web"]["Interop"]["Package"]["PackageExports"]["GetPlatformCatalog.451505237"](targetFramework, platformVersion);
    const $parsed = JSON.parse($result);
    return $parsed;
}
export async function getPlatformVersions(targetFramework) {
    const $result = await $requireManagedExports()["DotnetInspect"]["Web"]["Interop"]["Package"]["PackageExports"]["GetPlatformVersions.976702342"](targetFramework);
    const $parsed = JSON.parse($result);
    return $parsed;
}
export function listPackageChangesPackageSets() {
    const $result = $requireManagedExports()["DotnetInspect"]["Web"]["Interop"]["Package"]["PackageExports"]["ListPackageChangesPackageSets.1310674786"]();
    const $parsed = JSON.parse($result);
    return $parsed;
}
export function listPackageQueryCatalog() {
    const $result = $requireManagedExports()["DotnetInspect"]["Web"]["Interop"]["Package"]["PackageExports"]["ListPackageQueryCatalog.1310674786"]();
    const $parsed = JSON.parse($result);
    return $parsed;
}
export async function loadRuntimePack(targetFramework, platformVersion) {
    return await $requireManagedExports()["DotnetInspect"]["Web"]["Interop"]["Package"]["PackageExports"]["LoadRuntimePack.451505237"](targetFramework, platformVersion);
}
export async function loadRuntimePackAssembly(targetFramework, platformVersion, assemblyFileName, pack, assetFileName) {
    return await $requireManagedExports()["DotnetInspect"]["Web"]["Interop"]["Package"]["PackageExports"]["LoadRuntimePackAssembly.1330709314"](targetFramework, platformVersion, assemblyFileName, pack, assetFileName);
}
export function matchPackageDependencyCoordinate(packageId, declaredRange, candidatesJson) {
    const $result = $requireManagedExports()["DotnetInspect"]["Web"]["Interop"]["Package"]["PackageExports"]["MatchPackageDependencyCoordinate.1537767637"](packageId, declaredRange, candidatesJson);
    const $parsed = JSON.parse($result);
    return $parsed;
}
export function packageCacheStats() {
    const $result = $requireManagedExports()["DotnetInspect"]["Web"]["Interop"]["Package"]["PackageExports"]["PackageCacheStats.1310674786"]();
    const $parsed = JSON.parse($result);
    return $parsed;
}
export async function prefetchPlatformPacks(targetFramework, platformVersion) {
    return await $requireManagedExports()["DotnetInspect"]["Web"]["Interop"]["Package"]["PackageExports"]["PrefetchPlatformPacks.1782598084"](targetFramework, platformVersion);
}
export async function queryMemberDocumentation(packageId, version, framework, assemblyName, documentationId) {
    const $result = await $requireManagedExports()["DotnetInspect"]["Web"]["Interop"]["Package"]["PackageExports"]["QueryMemberDocumentation.1330709314"](packageId, version, framework, assemblyName, documentationId);
    const $parsed = JSON.parse($result);
    return $parsed;
}
export async function queryPackage(packageId, version, targetFramework) {
    const $result = await $requireManagedExports()["DotnetInspect"]["Web"]["Interop"]["Package"]["PackageExports"]["QueryPackage.1001223652"](packageId, version, targetFramework);
    const $parsed = JSON.parse($result);
    return $parsed;
}
export async function queryPackageDependencies(packageId, version, targetFramework, assemblyId) {
    const $result = await $requireManagedExports()["DotnetInspect"]["Web"]["Interop"]["Package"]["PackageExports"]["QueryPackageDependencies.1579276339"](packageId, version, targetFramework, assemblyId);
    const $parsed = JSON.parse($result);
    return $parsed;
}
export async function queryPackagePruning(packageId, version, targetFramework, requestJson) {
    const $result = await $requireManagedExports()["DotnetInspect"]["Web"]["Interop"]["Package"]["PackageExports"]["QueryPackagePruning.1579276339"](packageId, version, targetFramework, requestJson);
    const $parsed = JSON.parse($result);
    return $parsed;
}
export async function queryPackageVersions(packageId, currentVersion) {
    const $result = await $requireManagedExports()["DotnetInspect"]["Web"]["Interop"]["Package"]["PackageExports"]["QueryPackageVersions.451505237"](packageId, currentVersion);
    const $parsed = JSON.parse($result);
    return $parsed;
}
export async function queryWorkspacePackageOccurrences(workspaceJson) {
    const $result = await $requireManagedExports()["DotnetInspect"]["Web"]["Interop"]["Package"]["PackageExports"]["QueryWorkspacePackageOccurrences.976702342"](workspaceJson);
    const $parsed = JSON.parse($result);
    return $parsed;
}
export function requestPackageQueryMatches(operationId, additionalMatchCredit) {
    const $result = $requireManagedExports()["DotnetInspect"]["Web"]["Interop"]["Package"]["PackageExports"]["RequestPackageQueryMatches.146925470"](operationId, additionalMatchCredit);
    const $parsed = JSON.parse($result);
    return $parsed;
}
export async function resolvePackageDependencyVersion(packageId, declaredRange) {
    return await $requireManagedExports()["DotnetInspect"]["Web"]["Interop"]["Package"]["PackageExports"]["ResolvePackageDependencyVersion.451505237"](packageId, declaredRange);
}
export async function runPackageChanges(operationId, requestJson, eventSink) {
    const $result = await $requireManagedExports()["DotnetInspect"]["Web"]["Interop"]["Package"]["PackageExports"]["RunPackageChanges.1791926993"](operationId, requestJson, eventSink);
    const $parsed = JSON.parse($result);
    return $parsed;
}
export async function runPackageQuery(operationId, prefix, facetIdsJson, termsJson, maximumCandidates, maximumMatches, includePrerelease, initialMatchCredit, eventSink) {
    const $result = await $requireManagedExports()["DotnetInspect"]["Web"]["Interop"]["Package"]["PackageExports"]["RunPackageQuery.1685943924"](operationId, prefix, facetIdsJson, termsJson, maximumCandidates, maximumMatches, includePrerelease, initialMatchCredit, eventSink);
    const $parsed = JSON.parse($result);
    return $parsed;
}
export function searchTypes(query, candidatesJson) {
    const $result = $requireManagedExports()["DotnetInspect"]["Web"]["Interop"]["Package"]["PackageExports"]["SearchTypes.271973316"](query, candidatesJson);
    const $parsed = JSON.parse($result);
    return $parsed;
}
