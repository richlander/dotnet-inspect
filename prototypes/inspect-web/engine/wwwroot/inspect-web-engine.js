// GENERATED FILE — DO NOT EDIT BY HAND.
//
// Generated from InspectWeb.Engine.dll's [JSExport] surface. Regenerate with:
//   eng/generate-inspect-web-engine-facade.sh
// CI fails if this facade or either compiler-derived artifact drifts.
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
        value = $ownDataProperty(value, "ActivateWorkspacePackageOccurrence.304094707");
        if (typeof value !== "function") {
            throw new Error("Managed export \u0027InspectionEngine.ActivateWorkspacePackageOccurrence.304094707\u0027 is not callable.");
        }
    }
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
        value = $ownDataProperty(value, "CancelPackageQuery.19325221");
        if (typeof value !== "function") {
            throw new Error("Managed export \u0027InspectionEngine.CancelPackageQuery.19325221\u0027 is not callable.");
        }
    }
    {
        let value = exports;
        value = $ownDataProperty(value, "InspectionEngine");
        value = $ownDataProperty(value, "CancelSourceQuery.19325221");
        if (typeof value !== "function") {
            throw new Error("Managed export \u0027InspectionEngine.CancelSourceQuery.19325221\u0027 is not callable.");
        }
    }
    {
        let value = exports;
        value = $ownDataProperty(value, "InspectionEngine");
        value = $ownDataProperty(value, "ClearWorkspacePackageOccurrences.19325221");
        if (typeof value !== "function") {
            throw new Error("Managed export \u0027InspectionEngine.ClearWorkspacePackageOccurrences.19325221\u0027 is not callable.");
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
    {
        let value = exports;
        value = $ownDataProperty(value, "InspectionEngine");
        value = $ownDataProperty(value, "DecodeWorkspaceShareState.304094707");
        if (typeof value !== "function") {
            throw new Error("Managed export \u0027InspectionEngine.DecodeWorkspaceShareState.304094707\u0027 is not callable.");
        }
    }
    {
        let value = exports;
        value = $ownDataProperty(value, "InspectionEngine");
        value = $ownDataProperty(value, "EncodeWorkspaceShareState.304094707");
        if (typeof value !== "function") {
            throw new Error("Managed export \u0027InspectionEngine.EncodeWorkspaceShareState.304094707\u0027 is not callable.");
        }
    }
    {
        let value = exports;
        value = $ownDataProperty(value, "InspectionEngine");
        value = $ownDataProperty(value, "ExpandPlatformCallGraph.1136010516");
        if (typeof value !== "function") {
            throw new Error("Managed export \u0027InspectionEngine.ExpandPlatformCallGraph.1136010516\u0027 is not callable.");
        }
    }
    {
        let value = exports;
        value = $ownDataProperty(value, "InspectionEngine");
        value = $ownDataProperty(value, "GetPackageDocument.1001223652");
        if (typeof value !== "function") {
            throw new Error("Managed export \u0027InspectionEngine.GetPackageDocument.1001223652\u0027 is not callable.");
        }
    }
    {
        let value = exports;
        value = $ownDataProperty(value, "InspectionEngine");
        value = $ownDataProperty(value, "ListHomeDemos.1310674786");
        if (typeof value !== "function") {
            throw new Error("Managed export \u0027InspectionEngine.ListHomeDemos.1310674786\u0027 is not callable.");
        }
    }
    {
        let value = exports;
        value = $ownDataProperty(value, "InspectionEngine");
        value = $ownDataProperty(value, "ListPackageQueryFacets.1310674786");
        if (typeof value !== "function") {
            throw new Error("Managed export \u0027InspectionEngine.ListPackageQueryFacets.1310674786\u0027 is not callable.");
        }
    }
    {
        let value = exports;
        value = $ownDataProperty(value, "InspectionEngine");
        value = $ownDataProperty(value, "ListVocabulary.1310674786");
        if (typeof value !== "function") {
            throw new Error("Managed export \u0027InspectionEngine.ListVocabulary.1310674786\u0027 is not callable.");
        }
    }
    {
        let value = exports;
        value = $ownDataProperty(value, "InspectionEngine");
        value = $ownDataProperty(value, "LoadRuntimePack.451505237");
        if (typeof value !== "function") {
            throw new Error("Managed export \u0027InspectionEngine.LoadRuntimePack.451505237\u0027 is not callable.");
        }
    }
    {
        let value = exports;
        value = $ownDataProperty(value, "InspectionEngine");
        value = $ownDataProperty(value, "LoadRuntimePackAssembly.1579276339");
        if (typeof value !== "function") {
            throw new Error("Managed export \u0027InspectionEngine.LoadRuntimePackAssembly.1579276339\u0027 is not callable.");
        }
    }
    {
        let value = exports;
        value = $ownDataProperty(value, "InspectionEngine");
        value = $ownDataProperty(value, "MatchPackageDependencyCoordinate.1537767637");
        if (typeof value !== "function") {
            throw new Error("Managed export \u0027InspectionEngine.MatchPackageDependencyCoordinate.1537767637\u0027 is not callable.");
        }
    }
    {
        let value = exports;
        value = $ownDataProperty(value, "InspectionEngine");
        value = $ownDataProperty(value, "PackageCacheStats.1310674786");
        if (typeof value !== "function") {
            throw new Error("Managed export \u0027InspectionEngine.PackageCacheStats.1310674786\u0027 is not callable.");
        }
    }
    {
        let value = exports;
        value = $ownDataProperty(value, "InspectionEngine");
        value = $ownDataProperty(value, "QueryGraphMemberSurface.1542089313");
        if (typeof value !== "function") {
            throw new Error("Managed export \u0027InspectionEngine.QueryGraphMemberSurface.1542089313\u0027 is not callable.");
        }
    }
    {
        let value = exports;
        value = $ownDataProperty(value, "InspectionEngine");
        value = $ownDataProperty(value, "QueryMemberAnnotatedSource.1135530322");
        if (typeof value !== "function") {
            throw new Error("Managed export \u0027InspectionEngine.QueryMemberAnnotatedSource.1135530322\u0027 is not callable.");
        }
    }
    {
        let value = exports;
        value = $ownDataProperty(value, "InspectionEngine");
        value = $ownDataProperty(value, "QueryMemberCallGraph.1135530322");
        if (typeof value !== "function") {
            throw new Error("Managed export \u0027InspectionEngine.QueryMemberCallGraph.1135530322\u0027 is not callable.");
        }
    }
    {
        let value = exports;
        value = $ownDataProperty(value, "InspectionEngine");
        value = $ownDataProperty(value, "QueryMemberDocumentation.1330709314");
        if (typeof value !== "function") {
            throw new Error("Managed export \u0027InspectionEngine.QueryMemberDocumentation.1330709314\u0027 is not callable.");
        }
    }
    {
        let value = exports;
        value = $ownDataProperty(value, "InspectionEngine");
        value = $ownDataProperty(value, "QueryMemberFacts.581406856");
        if (typeof value !== "function") {
            throw new Error("Managed export \u0027InspectionEngine.QueryMemberFacts.581406856\u0027 is not callable.");
        }
    }
    {
        let value = exports;
        value = $ownDataProperty(value, "InspectionEngine");
        value = $ownDataProperty(value, "QueryMemberSource.641907440");
        if (typeof value !== "function") {
            throw new Error("Managed export \u0027InspectionEngine.QueryMemberSource.641907440\u0027 is not callable.");
        }
    }
    {
        let value = exports;
        value = $ownDataProperty(value, "InspectionEngine");
        value = $ownDataProperty(value, "QueryPackage.1001223652");
        if (typeof value !== "function") {
            throw new Error("Managed export \u0027InspectionEngine.QueryPackage.1001223652\u0027 is not callable.");
        }
    }
    {
        let value = exports;
        value = $ownDataProperty(value, "InspectionEngine");
        value = $ownDataProperty(value, "QueryPackageDependencies.1579276339");
        if (typeof value !== "function") {
            throw new Error("Managed export \u0027InspectionEngine.QueryPackageDependencies.1579276339\u0027 is not callable.");
        }
    }
    {
        let value = exports;
        value = $ownDataProperty(value, "InspectionEngine");
        value = $ownDataProperty(value, "QueryPackageHeapEntries.1330709314");
        if (typeof value !== "function") {
            throw new Error("Managed export \u0027InspectionEngine.QueryPackageHeapEntries.1330709314\u0027 is not callable.");
        }
    }
    {
        let value = exports;
        value = $ownDataProperty(value, "InspectionEngine");
        value = $ownDataProperty(value, "QueryPackageIntegrations.1001223652");
        if (typeof value !== "function") {
            throw new Error("Managed export \u0027InspectionEngine.QueryPackageIntegrations.1001223652\u0027 is not callable.");
        }
    }
    {
        let value = exports;
        value = $ownDataProperty(value, "InspectionEngine");
        value = $ownDataProperty(value, "QueryPackageMetadata.1001223652");
        if (typeof value !== "function") {
            throw new Error("Managed export \u0027InspectionEngine.QueryPackageMetadata.1001223652\u0027 is not callable.");
        }
    }
    {
        let value = exports;
        value = $ownDataProperty(value, "InspectionEngine");
        value = $ownDataProperty(value, "QueryPackageMetadataTable.1509466830");
        if (typeof value !== "function") {
            throw new Error("Managed export \u0027InspectionEngine.QueryPackageMetadataTable.1509466830\u0027 is not callable.");
        }
    }
    {
        let value = exports;
        value = $ownDataProperty(value, "InspectionEngine");
        value = $ownDataProperty(value, "QueryPackageOpportunities.1001223652");
        if (typeof value !== "function") {
            throw new Error("Managed export \u0027InspectionEngine.QueryPackageOpportunities.1001223652\u0027 is not callable.");
        }
    }
    {
        let value = exports;
        value = $ownDataProperty(value, "InspectionEngine");
        value = $ownDataProperty(value, "QueryPackagePerformance.1001223652");
        if (typeof value !== "function") {
            throw new Error("Managed export \u0027InspectionEngine.QueryPackagePerformance.1001223652\u0027 is not callable.");
        }
    }
    {
        let value = exports;
        value = $ownDataProperty(value, "InspectionEngine");
        value = $ownDataProperty(value, "QueryPackageVersions.976702342");
        if (typeof value !== "function") {
            throw new Error("Managed export \u0027InspectionEngine.QueryPackageVersions.976702342\u0027 is not callable.");
        }
    }
    {
        let value = exports;
        value = $ownDataProperty(value, "InspectionEngine");
        value = $ownDataProperty(value, "QueryPlatformHeapEntries.1330709314");
        if (typeof value !== "function") {
            throw new Error("Managed export \u0027InspectionEngine.QueryPlatformHeapEntries.1330709314\u0027 is not callable.");
        }
    }
    {
        let value = exports;
        value = $ownDataProperty(value, "InspectionEngine");
        value = $ownDataProperty(value, "QueryPlatformIntegrations.1579276339");
        if (typeof value !== "function") {
            throw new Error("Managed export \u0027InspectionEngine.QueryPlatformIntegrations.1579276339\u0027 is not callable.");
        }
    }
    {
        let value = exports;
        value = $ownDataProperty(value, "InspectionEngine");
        value = $ownDataProperty(value, "QueryPlatformMetadata.1579276339");
        if (typeof value !== "function") {
            throw new Error("Managed export \u0027InspectionEngine.QueryPlatformMetadata.1579276339\u0027 is not callable.");
        }
    }
    {
        let value = exports;
        value = $ownDataProperty(value, "InspectionEngine");
        value = $ownDataProperty(value, "QueryPlatformMetadataTable.1509466830");
        if (typeof value !== "function") {
            throw new Error("Managed export \u0027InspectionEngine.QueryPlatformMetadataTable.1509466830\u0027 is not callable.");
        }
    }
    {
        let value = exports;
        value = $ownDataProperty(value, "InspectionEngine");
        value = $ownDataProperty(value, "QueryPlatformOpportunities.1579276339");
        if (typeof value !== "function") {
            throw new Error("Managed export \u0027InspectionEngine.QueryPlatformOpportunities.1579276339\u0027 is not callable.");
        }
    }
    {
        let value = exports;
        value = $ownDataProperty(value, "InspectionEngine");
        value = $ownDataProperty(value, "QueryPlatformPerformance.1579276339");
        if (typeof value !== "function") {
            throw new Error("Managed export \u0027InspectionEngine.QueryPlatformPerformance.1579276339\u0027 is not callable.");
        }
    }
    {
        let value = exports;
        value = $ownDataProperty(value, "InspectionEngine");
        value = $ownDataProperty(value, "QueryTypeMemberSource.641907440");
        if (typeof value !== "function") {
            throw new Error("Managed export \u0027InspectionEngine.QueryTypeMemberSource.641907440\u0027 is not callable.");
        }
    }
    {
        let value = exports;
        value = $ownDataProperty(value, "InspectionEngine");
        value = $ownDataProperty(value, "QueryTypeProjection.1330709314");
        if (typeof value !== "function") {
            throw new Error("Managed export \u0027InspectionEngine.QueryTypeProjection.1330709314\u0027 is not callable.");
        }
    }
    {
        let value = exports;
        value = $ownDataProperty(value, "InspectionEngine");
        value = $ownDataProperty(value, "QueryTypeSource.649160465");
        if (typeof value !== "function") {
            throw new Error("Managed export \u0027InspectionEngine.QueryTypeSource.649160465\u0027 is not callable.");
        }
    }
    {
        let value = exports;
        value = $ownDataProperty(value, "InspectionEngine");
        value = $ownDataProperty(value, "QueryWorkspacePackageOccurrences.976702342");
        if (typeof value !== "function") {
            throw new Error("Managed export \u0027InspectionEngine.QueryWorkspacePackageOccurrences.976702342\u0027 is not callable.");
        }
    }
    {
        let value = exports;
        value = $ownDataProperty(value, "InspectionEngine");
        value = $ownDataProperty(value, "ResolveHomeDemo.304094707");
        if (typeof value !== "function") {
            throw new Error("Managed export \u0027InspectionEngine.ResolveHomeDemo.304094707\u0027 is not callable.");
        }
    }
    {
        let value = exports;
        value = $ownDataProperty(value, "InspectionEngine");
        value = $ownDataProperty(value, "ResolvePackageDependencyVersion.451505237");
        if (typeof value !== "function") {
            throw new Error("Managed export \u0027InspectionEngine.ResolvePackageDependencyVersion.451505237\u0027 is not callable.");
        }
    }
    {
        let value = exports;
        value = $ownDataProperty(value, "InspectionEngine");
        value = $ownDataProperty(value, "RunHomeDemo.976702342");
        if (typeof value !== "function") {
            throw new Error("Managed export \u0027InspectionEngine.RunHomeDemo.976702342\u0027 is not callable.");
        }
    }
    {
        let value = exports;
        value = $ownDataProperty(value, "InspectionEngine");
        value = $ownDataProperty(value, "RunPackageQuery.287304775");
        if (typeof value !== "function") {
            throw new Error("Managed export \u0027InspectionEngine.RunPackageQuery.287304775\u0027 is not callable.");
        }
    }
    {
        let value = exports;
        value = $ownDataProperty(value, "InspectionEngine");
        value = $ownDataProperty(value, "SearchTypes.271973316");
        if (typeof value !== "function") {
            throw new Error("Managed export \u0027InspectionEngine.SearchTypes.271973316\u0027 is not callable.");
        }
    }
}
async function $initializeRuntimeCore() {
    const runtime = await dotnet.create();
    const exports = await runtime.getAssemblyExports("InspectWeb.Engine");
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
export function activateWorkspacePackageOccurrence(action) {
    const $result = $requireManagedExports()["InspectionEngine"]["ActivateWorkspacePackageOccurrence.304094707"](action);
    const $parsed = JSON.parse($result);
    return $parsed;
}
export async function asyncLoweringCanary() {
    return await $requireManagedExports()["InspectionEngine"]["AsyncLoweringCanary.1684317047"]();
}
export function buildIdentity() {
    const $result = $requireManagedExports()["InspectionEngine"]["BuildIdentity.1310674786"]();
    const $parsed = JSON.parse($result);
    return $parsed;
}
export function cancelPackageQuery() {
    return $requireManagedExports()["InspectionEngine"]["CancelPackageQuery.19325221"]();
}
export function cancelSourceQuery() {
    return $requireManagedExports()["InspectionEngine"]["CancelSourceQuery.19325221"]();
}
export function clearWorkspacePackageOccurrences() {
    return $requireManagedExports()["InspectionEngine"]["ClearWorkspacePackageOccurrences.19325221"]();
}
export function configureHost(origin) {
    return $requireManagedExports()["InspectionEngine"]["ConfigureHost.92020726"](origin);
}
export function decodeWorkspaceShareState(encoded) {
    const $result = $requireManagedExports()["InspectionEngine"]["DecodeWorkspaceShareState.304094707"](encoded);
    const $parsed = JSON.parse($result);
    return $parsed;
}
export function encodeWorkspaceShareState(stateJson) {
    const $result = $requireManagedExports()["InspectionEngine"]["EncodeWorkspaceShareState.304094707"](stateJson);
    const $parsed = JSON.parse($result);
    return $parsed;
}
export async function expandPlatformCallGraph(targetFramework, platformVersion, assembly, pack, assemblyVersion, assemblyCulture, assemblyPublicKeyToken, typeFullName, memberName, selectorKey, metadataToken) {
    const $result = await $requireManagedExports()["InspectionEngine"]["ExpandPlatformCallGraph.1136010516"](targetFramework, platformVersion, assembly, pack, assemblyVersion, assemblyCulture, assemblyPublicKeyToken, typeFullName, memberName, selectorKey, metadataToken);
    const $parsed = JSON.parse($result);
    return $parsed;
}
export async function getPackageDocument(packageId, version, path) {
    const $result = await $requireManagedExports()["InspectionEngine"]["GetPackageDocument.1001223652"](packageId, version, path);
    const $parsed = JSON.parse($result);
    return $parsed;
}
export function listHomeDemos() {
    const $result = $requireManagedExports()["InspectionEngine"]["ListHomeDemos.1310674786"]();
    const $parsed = JSON.parse($result);
    return $parsed;
}
export function listPackageQueryFacets() {
    const $result = $requireManagedExports()["InspectionEngine"]["ListPackageQueryFacets.1310674786"]();
    const $parsed = JSON.parse($result);
    return $parsed;
}
export function listVocabulary() {
    const $result = $requireManagedExports()["InspectionEngine"]["ListVocabulary.1310674786"]();
    const $parsed = JSON.parse($result);
    return $parsed;
}
export async function loadRuntimePack(targetFramework, platformVersion) {
    return await $requireManagedExports()["InspectionEngine"]["LoadRuntimePack.451505237"](targetFramework, platformVersion);
}
export async function loadRuntimePackAssembly(targetFramework, platformVersion, assemblyFileName, pack) {
    return await $requireManagedExports()["InspectionEngine"]["LoadRuntimePackAssembly.1579276339"](targetFramework, platformVersion, assemblyFileName, pack);
}
export function matchPackageDependencyCoordinate(packageId, declaredRange, candidatesJson) {
    const $result = $requireManagedExports()["InspectionEngine"]["MatchPackageDependencyCoordinate.1537767637"](packageId, declaredRange, candidatesJson);
    const $parsed = JSON.parse($result);
    return $parsed;
}
export function packageCacheStats() {
    const $result = $requireManagedExports()["InspectionEngine"]["PackageCacheStats.1310674786"]();
    const $parsed = JSON.parse($result);
    return $parsed;
}
export async function queryGraphMemberSurface(packageId, version, targetFramework, assemblyName, typeIdentity, memberName, selectorKey, metadataToken) {
    const $result = await $requireManagedExports()["InspectionEngine"]["QueryGraphMemberSurface.1542089313"](packageId, version, targetFramework, assemblyName, typeIdentity, memberName, selectorKey, metadataToken);
    const $parsed = JSON.parse($result);
    return $parsed;
}
export async function queryMemberAnnotatedSource(packageId, version, targetFramework, assemblyName, typeIdentity, typeQueryId, memberName, memberSignature, selectorKey, metadataToken, styleOptionsJson) {
    const $result = await $requireManagedExports()["InspectionEngine"]["QueryMemberAnnotatedSource.1135530322"](packageId, version, targetFramework, assemblyName, typeIdentity, typeQueryId, memberName, memberSignature, selectorKey, metadataToken, styleOptionsJson);
    const $parsed = JSON.parse($result);
    return $parsed;
}
export async function queryMemberCallGraph(packageId, version, targetFramework, assemblyName, typeIdentity, typeQueryId, memberName, memberSignature, selectorKey, metadataToken, workspaceJson) {
    const $result = await $requireManagedExports()["InspectionEngine"]["QueryMemberCallGraph.1135530322"](packageId, version, targetFramework, assemblyName, typeIdentity, typeQueryId, memberName, memberSignature, selectorKey, metadataToken, workspaceJson);
    const $parsed = JSON.parse($result);
    return $parsed;
}
export async function queryMemberDocumentation(packageId, version, framework, assemblyName, documentationId) {
    const $result = await $requireManagedExports()["InspectionEngine"]["QueryMemberDocumentation.1330709314"](packageId, version, framework, assemblyName, documentationId);
    const $parsed = JSON.parse($result);
    return $parsed;
}
export async function queryMemberFacts(packageId, version, targetFramework, assemblyName, typeIdentity, memberName, memberSignature, selectorKey, metadataToken, implementationBodySelected) {
    const $result = await $requireManagedExports()["InspectionEngine"]["QueryMemberFacts.581406856"](packageId, version, targetFramework, assemblyName, typeIdentity, memberName, memberSignature, selectorKey, metadataToken, implementationBodySelected);
    const $parsed = JSON.parse($result);
    return $parsed;
}
export async function queryMemberSource(packageId, version, targetFramework, assemblyName, typeIdentity, memberName, selectorKey, metadataToken, styleOptionsJson) {
    const $result = await $requireManagedExports()["InspectionEngine"]["QueryMemberSource.641907440"](packageId, version, targetFramework, assemblyName, typeIdentity, memberName, selectorKey, metadataToken, styleOptionsJson);
    const $parsed = JSON.parse($result);
    return $parsed;
}
export async function queryPackage(packageId, version, targetFramework) {
    const $result = await $requireManagedExports()["InspectionEngine"]["QueryPackage.1001223652"](packageId, version, targetFramework);
    const $parsed = JSON.parse($result);
    return $parsed;
}
export async function queryPackageDependencies(packageId, version, targetFramework, assemblyId) {
    const $result = await $requireManagedExports()["InspectionEngine"]["QueryPackageDependencies.1579276339"](packageId, version, targetFramework, assemblyId);
    const $parsed = JSON.parse($result);
    return $parsed;
}
export async function queryPackageHeapEntries(packageId, version, targetFramework, assemblyFileName, heap) {
    const $result = await $requireManagedExports()["InspectionEngine"]["QueryPackageHeapEntries.1330709314"](packageId, version, targetFramework, assemblyFileName, heap);
    const $parsed = JSON.parse($result);
    return $parsed;
}
export async function queryPackageIntegrations(packageId, version, targetFramework) {
    const $result = await $requireManagedExports()["InspectionEngine"]["QueryPackageIntegrations.1001223652"](packageId, version, targetFramework);
    const $parsed = JSON.parse($result);
    return $parsed;
}
export async function queryPackageMetadata(packageId, version, targetFramework) {
    const $result = await $requireManagedExports()["InspectionEngine"]["QueryPackageMetadata.1001223652"](packageId, version, targetFramework);
    const $parsed = JSON.parse($result);
    return $parsed;
}
export async function queryPackageMetadataTable(packageId, version, targetFramework, assemblyFileName, tableIndex, startRowId, maxRows) {
    const $result = await $requireManagedExports()["InspectionEngine"]["QueryPackageMetadataTable.1509466830"](packageId, version, targetFramework, assemblyFileName, tableIndex, startRowId, maxRows);
    const $parsed = JSON.parse($result);
    return $parsed;
}
export async function queryPackageOpportunities(packageId, version, targetFramework) {
    const $result = await $requireManagedExports()["InspectionEngine"]["QueryPackageOpportunities.1001223652"](packageId, version, targetFramework);
    const $parsed = JSON.parse($result);
    return $parsed;
}
export async function queryPackagePerformance(packageId, version, targetFramework) {
    const $result = await $requireManagedExports()["InspectionEngine"]["QueryPackagePerformance.1001223652"](packageId, version, targetFramework);
    const $parsed = JSON.parse($result);
    return $parsed;
}
export async function queryPackageVersions(packageId) {
    const $result = await $requireManagedExports()["InspectionEngine"]["QueryPackageVersions.976702342"](packageId);
    const $parsed = JSON.parse($result);
    return $parsed;
}
export async function queryPlatformHeapEntries(targetFramework, platformVersion, assemblyFileName, pack, heap) {
    const $result = await $requireManagedExports()["InspectionEngine"]["QueryPlatformHeapEntries.1330709314"](targetFramework, platformVersion, assemblyFileName, pack, heap);
    const $parsed = JSON.parse($result);
    return $parsed;
}
export async function queryPlatformIntegrations(targetFramework, platformVersion, assemblyFileName, pack) {
    const $result = await $requireManagedExports()["InspectionEngine"]["QueryPlatformIntegrations.1579276339"](targetFramework, platformVersion, assemblyFileName, pack);
    const $parsed = JSON.parse($result);
    return $parsed;
}
export async function queryPlatformMetadata(targetFramework, platformVersion, assemblyFileName, pack) {
    const $result = await $requireManagedExports()["InspectionEngine"]["QueryPlatformMetadata.1579276339"](targetFramework, platformVersion, assemblyFileName, pack);
    const $parsed = JSON.parse($result);
    return $parsed;
}
export async function queryPlatformMetadataTable(targetFramework, platformVersion, assemblyFileName, pack, tableIndex, startRowId, maxRows) {
    const $result = await $requireManagedExports()["InspectionEngine"]["QueryPlatformMetadataTable.1509466830"](targetFramework, platformVersion, assemblyFileName, pack, tableIndex, startRowId, maxRows);
    const $parsed = JSON.parse($result);
    return $parsed;
}
export async function queryPlatformOpportunities(targetFramework, platformVersion, assemblyFileName, pack) {
    const $result = await $requireManagedExports()["InspectionEngine"]["QueryPlatformOpportunities.1579276339"](targetFramework, platformVersion, assemblyFileName, pack);
    const $parsed = JSON.parse($result);
    return $parsed;
}
export async function queryPlatformPerformance(targetFramework, platformVersion, assemblyFileName, pack) {
    return await $requireManagedExports()["InspectionEngine"]["QueryPlatformPerformance.1579276339"](targetFramework, platformVersion, assemblyFileName, pack);
}
export async function queryTypeMemberSource(packageId, version, targetFramework, assemblyName, typeIdentity, memberName, selectorKey, metadataToken, styleOptionsJson) {
    const $result = await $requireManagedExports()["InspectionEngine"]["QueryTypeMemberSource.641907440"](packageId, version, targetFramework, assemblyName, typeIdentity, memberName, selectorKey, metadataToken, styleOptionsJson);
    const $parsed = JSON.parse($result);
    return $parsed;
}
export async function queryTypeProjection(packageId, version, targetFramework, assemblyName, typeId) {
    const $result = await $requireManagedExports()["InspectionEngine"]["QueryTypeProjection.1330709314"](packageId, version, targetFramework, assemblyName, typeId);
    const $parsed = JSON.parse($result);
    return $parsed;
}
export async function queryTypeSource(packageId, version, targetFramework, assemblyName, typeIdentity, styleOptionsJson) {
    const $result = await $requireManagedExports()["InspectionEngine"]["QueryTypeSource.649160465"](packageId, version, targetFramework, assemblyName, typeIdentity, styleOptionsJson);
    const $parsed = JSON.parse($result);
    return $parsed;
}
export async function queryWorkspacePackageOccurrences(workspaceJson) {
    const $result = await $requireManagedExports()["InspectionEngine"]["QueryWorkspacePackageOccurrences.976702342"](workspaceJson);
    const $parsed = JSON.parse($result);
    return $parsed;
}
export function resolveHomeDemo(scenarioId) {
    const $result = $requireManagedExports()["InspectionEngine"]["ResolveHomeDemo.304094707"](scenarioId);
    const $parsed = JSON.parse($result);
    return $parsed;
}
export async function resolvePackageDependencyVersion(packageId, declaredRange) {
    return await $requireManagedExports()["InspectionEngine"]["ResolvePackageDependencyVersion.451505237"](packageId, declaredRange);
}
export async function runHomeDemo(scenarioId) {
    const $result = await $requireManagedExports()["InspectionEngine"]["RunHomeDemo.976702342"](scenarioId);
    const $parsed = JSON.parse($result);
    return $parsed;
}
export async function runPackageQuery(prefix, facetIdsJson, maximumCandidates, maximumMatches, includePrerelease, eventSink) {
    const $result = await $requireManagedExports()["InspectionEngine"]["RunPackageQuery.287304775"](prefix, facetIdsJson, maximumCandidates, maximumMatches, includePrerelease, eventSink);
    const $parsed = JSON.parse($result);
    return $parsed;
}
export function searchTypes(query, candidatesJson) {
    const $result = $requireManagedExports()["InspectionEngine"]["SearchTypes.271973316"](query, candidatesJson);
    const $parsed = JSON.parse($result);
    return $parsed;
}
