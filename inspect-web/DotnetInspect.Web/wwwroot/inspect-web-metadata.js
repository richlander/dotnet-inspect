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
        value = $ownDataProperty(value, "Metadata");
        value = $ownDataProperty(value, "MetadataExports");
        value = $ownDataProperty(value, "QueryGraphMemberSurface.1542089313");
        if (typeof value !== "function") {
            throw new Error("Managed export \u0027DotnetInspect.Web.Interop.Metadata.MetadataExports.QueryGraphMemberSurface.1542089313\u0027 is not callable.");
        }
    }
    {
        let value = exports;
        value = $ownDataProperty(value, "DotnetInspect");
        value = $ownDataProperty(value, "Web");
        value = $ownDataProperty(value, "Interop");
        value = $ownDataProperty(value, "Metadata");
        value = $ownDataProperty(value, "MetadataExports");
        value = $ownDataProperty(value, "QueryGraphMemberSurfaceByMethodAddress.642387634");
        if (typeof value !== "function") {
            throw new Error("Managed export \u0027DotnetInspect.Web.Interop.Metadata.MetadataExports.QueryGraphMemberSurfaceByMethodAddress.642387634\u0027 is not callable.");
        }
    }
    {
        let value = exports;
        value = $ownDataProperty(value, "DotnetInspect");
        value = $ownDataProperty(value, "Web");
        value = $ownDataProperty(value, "Interop");
        value = $ownDataProperty(value, "Metadata");
        value = $ownDataProperty(value, "MetadataExports");
        value = $ownDataProperty(value, "QueryPackageHeapEntries.649160465");
        if (typeof value !== "function") {
            throw new Error("Managed export \u0027DotnetInspect.Web.Interop.Metadata.MetadataExports.QueryPackageHeapEntries.649160465\u0027 is not callable.");
        }
    }
    {
        let value = exports;
        value = $ownDataProperty(value, "DotnetInspect");
        value = $ownDataProperty(value, "Web");
        value = $ownDataProperty(value, "Interop");
        value = $ownDataProperty(value, "Metadata");
        value = $ownDataProperty(value, "MetadataExports");
        value = $ownDataProperty(value, "QueryPackageMetadata.1579276339");
        if (typeof value !== "function") {
            throw new Error("Managed export \u0027DotnetInspect.Web.Interop.Metadata.MetadataExports.QueryPackageMetadata.1579276339\u0027 is not callable.");
        }
    }
    {
        let value = exports;
        value = $ownDataProperty(value, "DotnetInspect");
        value = $ownDataProperty(value, "Web");
        value = $ownDataProperty(value, "Interop");
        value = $ownDataProperty(value, "Metadata");
        value = $ownDataProperty(value, "MetadataExports");
        value = $ownDataProperty(value, "QueryPackageMetadataTable.1945598111");
        if (typeof value !== "function") {
            throw new Error("Managed export \u0027DotnetInspect.Web.Interop.Metadata.MetadataExports.QueryPackageMetadataTable.1945598111\u0027 is not callable.");
        }
    }
    {
        let value = exports;
        value = $ownDataProperty(value, "DotnetInspect");
        value = $ownDataProperty(value, "Web");
        value = $ownDataProperty(value, "Interop");
        value = $ownDataProperty(value, "Metadata");
        value = $ownDataProperty(value, "MetadataExports");
        value = $ownDataProperty(value, "QueryPlatformHeapEntries.649160465");
        if (typeof value !== "function") {
            throw new Error("Managed export \u0027DotnetInspect.Web.Interop.Metadata.MetadataExports.QueryPlatformHeapEntries.649160465\u0027 is not callable.");
        }
    }
    {
        let value = exports;
        value = $ownDataProperty(value, "DotnetInspect");
        value = $ownDataProperty(value, "Web");
        value = $ownDataProperty(value, "Interop");
        value = $ownDataProperty(value, "Metadata");
        value = $ownDataProperty(value, "MetadataExports");
        value = $ownDataProperty(value, "QueryPlatformMetadata.1579276339");
        if (typeof value !== "function") {
            throw new Error("Managed export \u0027DotnetInspect.Web.Interop.Metadata.MetadataExports.QueryPlatformMetadata.1579276339\u0027 is not callable.");
        }
    }
    {
        let value = exports;
        value = $ownDataProperty(value, "DotnetInspect");
        value = $ownDataProperty(value, "Web");
        value = $ownDataProperty(value, "Interop");
        value = $ownDataProperty(value, "Metadata");
        value = $ownDataProperty(value, "MetadataExports");
        value = $ownDataProperty(value, "QueryPlatformMetadataTable.1945598111");
        if (typeof value !== "function") {
            throw new Error("Managed export \u0027DotnetInspect.Web.Interop.Metadata.MetadataExports.QueryPlatformMetadataTable.1945598111\u0027 is not callable.");
        }
    }
    {
        let value = exports;
        value = $ownDataProperty(value, "DotnetInspect");
        value = $ownDataProperty(value, "Web");
        value = $ownDataProperty(value, "Interop");
        value = $ownDataProperty(value, "Metadata");
        value = $ownDataProperty(value, "MetadataExports");
        value = $ownDataProperty(value, "QueryTypeProjection.1330709314");
        if (typeof value !== "function") {
            throw new Error("Managed export \u0027DotnetInspect.Web.Interop.Metadata.MetadataExports.QueryTypeProjection.1330709314\u0027 is not callable.");
        }
    }
}
async function $initializeRuntimeCore(runtime) {
    const exports = await runtime.getAssemblyExports("DotnetInspect.Web.Interop.Metadata");
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
export async function queryGraphMemberSurface(packageId, version, targetFramework, assemblyName, typeIdentity, memberName, selectorKey, metadataToken) {
    const $result = await $requireManagedExports()["DotnetInspect"]["Web"]["Interop"]["Metadata"]["MetadataExports"]["QueryGraphMemberSurface.1542089313"](packageId, version, targetFramework, assemblyName, typeIdentity, memberName, selectorKey, metadataToken);
    const $parsed = JSON.parse($result);
    return $parsed;
}
export async function queryGraphMemberSurfaceByMethodAddress(packageId, version, targetFramework, assemblyName, assemblyVersion, assemblyCulture, assemblyPublicKeyToken, moduleVersionId, metadataToken) {
    const $result = await $requireManagedExports()["DotnetInspect"]["Web"]["Interop"]["Metadata"]["MetadataExports"]["QueryGraphMemberSurfaceByMethodAddress.642387634"](packageId, version, targetFramework, assemblyName, assemblyVersion, assemblyCulture, assemblyPublicKeyToken, moduleVersionId, metadataToken);
    const $parsed = JSON.parse($result);
    return $parsed;
}
export async function queryPackageHeapEntries(packageId, version, targetFramework, assemblyFileName, metadataRoot, heap) {
    const $result = await $requireManagedExports()["DotnetInspect"]["Web"]["Interop"]["Metadata"]["MetadataExports"]["QueryPackageHeapEntries.649160465"](packageId, version, targetFramework, assemblyFileName, metadataRoot, heap);
    const $parsed = JSON.parse($result);
    return $parsed;
}
export async function queryPackageMetadata(packageId, version, targetFramework, assemblyFileName) {
    const $result = await $requireManagedExports()["DotnetInspect"]["Web"]["Interop"]["Metadata"]["MetadataExports"]["QueryPackageMetadata.1579276339"](packageId, version, targetFramework, assemblyFileName);
    const $parsed = JSON.parse($result);
    return $parsed;
}
export async function queryPackageMetadataTable(packageId, version, targetFramework, assemblyFileName, metadataRoot, tableIndex, startRowId, maxRows) {
    const $result = await $requireManagedExports()["DotnetInspect"]["Web"]["Interop"]["Metadata"]["MetadataExports"]["QueryPackageMetadataTable.1945598111"](packageId, version, targetFramework, assemblyFileName, metadataRoot, tableIndex, startRowId, maxRows);
    const $parsed = JSON.parse($result);
    return $parsed;
}
export async function queryPlatformHeapEntries(targetFramework, platformVersion, assemblyFileName, pack, metadataRoot, heap) {
    const $result = await $requireManagedExports()["DotnetInspect"]["Web"]["Interop"]["Metadata"]["MetadataExports"]["QueryPlatformHeapEntries.649160465"](targetFramework, platformVersion, assemblyFileName, pack, metadataRoot, heap);
    const $parsed = JSON.parse($result);
    return $parsed;
}
export async function queryPlatformMetadata(targetFramework, platformVersion, assemblyFileName, pack) {
    const $result = await $requireManagedExports()["DotnetInspect"]["Web"]["Interop"]["Metadata"]["MetadataExports"]["QueryPlatformMetadata.1579276339"](targetFramework, platformVersion, assemblyFileName, pack);
    const $parsed = JSON.parse($result);
    return $parsed;
}
export async function queryPlatformMetadataTable(targetFramework, platformVersion, assemblyFileName, pack, metadataRoot, tableIndex, startRowId, maxRows) {
    const $result = await $requireManagedExports()["DotnetInspect"]["Web"]["Interop"]["Metadata"]["MetadataExports"]["QueryPlatformMetadataTable.1945598111"](targetFramework, platformVersion, assemblyFileName, pack, metadataRoot, tableIndex, startRowId, maxRows);
    const $parsed = JSON.parse($result);
    return $parsed;
}
export async function queryTypeProjection(packageId, version, targetFramework, assemblyName, typeId) {
    const $result = await $requireManagedExports()["DotnetInspect"]["Web"]["Interop"]["Metadata"]["MetadataExports"]["QueryTypeProjection.1330709314"](packageId, version, targetFramework, assemblyName, typeId);
    const $parsed = JSON.parse($result);
    return $parsed;
}
