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
        value = $ownDataProperty(value, "Source");
        value = $ownDataProperty(value, "SourceExports");
        value = $ownDataProperty(value, "CancelMemberSourceComparison.271973316");
        if (typeof value !== "function") {
            throw new Error("Managed export \u0027DotnetInspect.Web.Interop.Source.SourceExports.CancelMemberSourceComparison.271973316\u0027 is not callable.");
        }
    }
    {
        let value = exports;
        value = $ownDataProperty(value, "DotnetInspect");
        value = $ownDataProperty(value, "Web");
        value = $ownDataProperty(value, "Interop");
        value = $ownDataProperty(value, "Source");
        value = $ownDataProperty(value, "SourceExports");
        value = $ownDataProperty(value, "CancelMethodBodyComparison.271973316");
        if (typeof value !== "function") {
            throw new Error("Managed export \u0027DotnetInspect.Web.Interop.Source.SourceExports.CancelMethodBodyComparison.271973316\u0027 is not callable.");
        }
    }
    {
        let value = exports;
        value = $ownDataProperty(value, "DotnetInspect");
        value = $ownDataProperty(value, "Web");
        value = $ownDataProperty(value, "Interop");
        value = $ownDataProperty(value, "Source");
        value = $ownDataProperty(value, "SourceExports");
        value = $ownDataProperty(value, "CancelSourceQuery.19325221");
        if (typeof value !== "function") {
            throw new Error("Managed export \u0027DotnetInspect.Web.Interop.Source.SourceExports.CancelSourceQuery.19325221\u0027 is not callable.");
        }
    }
    {
        let value = exports;
        value = $ownDataProperty(value, "DotnetInspect");
        value = $ownDataProperty(value, "Web");
        value = $ownDataProperty(value, "Interop");
        value = $ownDataProperty(value, "Source");
        value = $ownDataProperty(value, "SourceExports");
        value = $ownDataProperty(value, "CancelTypeSourceQuery.271973316");
        if (typeof value !== "function") {
            throw new Error("Managed export \u0027DotnetInspect.Web.Interop.Source.SourceExports.CancelTypeSourceQuery.271973316\u0027 is not callable.");
        }
    }
    {
        let value = exports;
        value = $ownDataProperty(value, "DotnetInspect");
        value = $ownDataProperty(value, "Web");
        value = $ownDataProperty(value, "Interop");
        value = $ownDataProperty(value, "Source");
        value = $ownDataProperty(value, "SourceExports");
        value = $ownDataProperty(value, "QueryMemberAnnotatedSource.1135530322");
        if (typeof value !== "function") {
            throw new Error("Managed export \u0027DotnetInspect.Web.Interop.Source.SourceExports.QueryMemberAnnotatedSource.1135530322\u0027 is not callable.");
        }
    }
    {
        let value = exports;
        value = $ownDataProperty(value, "DotnetInspect");
        value = $ownDataProperty(value, "Web");
        value = $ownDataProperty(value, "Interop");
        value = $ownDataProperty(value, "Source");
        value = $ownDataProperty(value, "SourceExports");
        value = $ownDataProperty(value, "QueryMemberFindingCensus.1135530322");
        if (typeof value !== "function") {
            throw new Error("Managed export \u0027DotnetInspect.Web.Interop.Source.SourceExports.QueryMemberFindingCensus.1135530322\u0027 is not callable.");
        }
    }
    {
        let value = exports;
        value = $ownDataProperty(value, "DotnetInspect");
        value = $ownDataProperty(value, "Web");
        value = $ownDataProperty(value, "Interop");
        value = $ownDataProperty(value, "Source");
        value = $ownDataProperty(value, "SourceExports");
        value = $ownDataProperty(value, "QueryMemberSource.641907440");
        if (typeof value !== "function") {
            throw new Error("Managed export \u0027DotnetInspect.Web.Interop.Source.SourceExports.QueryMemberSource.641907440\u0027 is not callable.");
        }
    }
    {
        let value = exports;
        value = $ownDataProperty(value, "DotnetInspect");
        value = $ownDataProperty(value, "Web");
        value = $ownDataProperty(value, "Interop");
        value = $ownDataProperty(value, "Source");
        value = $ownDataProperty(value, "SourceExports");
        value = $ownDataProperty(value, "QueryMemberSourceComparison.451505237");
        if (typeof value !== "function") {
            throw new Error("Managed export \u0027DotnetInspect.Web.Interop.Source.SourceExports.QueryMemberSourceComparison.451505237\u0027 is not callable.");
        }
    }
    {
        let value = exports;
        value = $ownDataProperty(value, "DotnetInspect");
        value = $ownDataProperty(value, "Web");
        value = $ownDataProperty(value, "Interop");
        value = $ownDataProperty(value, "Source");
        value = $ownDataProperty(value, "SourceExports");
        value = $ownDataProperty(value, "QueryMethodBodyComparison.451505237");
        if (typeof value !== "function") {
            throw new Error("Managed export \u0027DotnetInspect.Web.Interop.Source.SourceExports.QueryMethodBodyComparison.451505237\u0027 is not callable.");
        }
    }
    {
        let value = exports;
        value = $ownDataProperty(value, "DotnetInspect");
        value = $ownDataProperty(value, "Web");
        value = $ownDataProperty(value, "Interop");
        value = $ownDataProperty(value, "Source");
        value = $ownDataProperty(value, "SourceExports");
        value = $ownDataProperty(value, "QueryMethodBodyComparisonTargets.642387634");
        if (typeof value !== "function") {
            throw new Error("Managed export \u0027DotnetInspect.Web.Interop.Source.SourceExports.QueryMethodBodyComparisonTargets.642387634\u0027 is not callable.");
        }
    }
    {
        let value = exports;
        value = $ownDataProperty(value, "DotnetInspect");
        value = $ownDataProperty(value, "Web");
        value = $ownDataProperty(value, "Interop");
        value = $ownDataProperty(value, "Source");
        value = $ownDataProperty(value, "SourceExports");
        value = $ownDataProperty(value, "QueryTypeMemberSource.641907440");
        if (typeof value !== "function") {
            throw new Error("Managed export \u0027DotnetInspect.Web.Interop.Source.SourceExports.QueryTypeMemberSource.641907440\u0027 is not callable.");
        }
    }
    {
        let value = exports;
        value = $ownDataProperty(value, "DotnetInspect");
        value = $ownDataProperty(value, "Web");
        value = $ownDataProperty(value, "Interop");
        value = $ownDataProperty(value, "Source");
        value = $ownDataProperty(value, "SourceExports");
        value = $ownDataProperty(value, "QueryTypeSource.1160082336");
        if (typeof value !== "function") {
            throw new Error("Managed export \u0027DotnetInspect.Web.Interop.Source.SourceExports.QueryTypeSource.1160082336\u0027 is not callable.");
        }
    }
}
async function $initializeRuntimeCore(runtime) {
    const exports = await runtime.getAssemblyExports("DotnetInspect.Web.Interop.Source");
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
export function cancelMemberSourceComparison(operationId, reason) {
    const $result = $requireManagedExports()["DotnetInspect"]["Web"]["Interop"]["Source"]["SourceExports"]["CancelMemberSourceComparison.271973316"](operationId, reason);
    const $parsed = JSON.parse($result);
    return $parsed;
}
export function cancelMethodBodyComparison(operationId, reason) {
    const $result = $requireManagedExports()["DotnetInspect"]["Web"]["Interop"]["Source"]["SourceExports"]["CancelMethodBodyComparison.271973316"](operationId, reason);
    const $parsed = JSON.parse($result);
    return $parsed;
}
export function cancelSourceQuery() {
    return $requireManagedExports()["DotnetInspect"]["Web"]["Interop"]["Source"]["SourceExports"]["CancelSourceQuery.19325221"]();
}
export function cancelTypeSourceQuery(operationId, reason) {
    const $result = $requireManagedExports()["DotnetInspect"]["Web"]["Interop"]["Source"]["SourceExports"]["CancelTypeSourceQuery.271973316"](operationId, reason);
    const $parsed = JSON.parse($result);
    return $parsed;
}
export async function queryMemberAnnotatedSource(packageId, version, targetFramework, assemblyName, typeIdentity, typeQueryId, memberName, memberSignature, selectorKey, metadataToken, styleOptionsJson) {
    const $result = await $requireManagedExports()["DotnetInspect"]["Web"]["Interop"]["Source"]["SourceExports"]["QueryMemberAnnotatedSource.1135530322"](packageId, version, targetFramework, assemblyName, typeIdentity, typeQueryId, memberName, memberSignature, selectorKey, metadataToken, styleOptionsJson);
    const $parsed = JSON.parse($result);
    return $parsed;
}
export async function queryMemberFindingCensus(packageId, version, targetFramework, assemblyName, typeIdentity, typeQueryId, memberName, memberSignature, selectorKey, metadataToken, styleOptionsJson) {
    const $result = await $requireManagedExports()["DotnetInspect"]["Web"]["Interop"]["Source"]["SourceExports"]["QueryMemberFindingCensus.1135530322"](packageId, version, targetFramework, assemblyName, typeIdentity, typeQueryId, memberName, memberSignature, selectorKey, metadataToken, styleOptionsJson);
    const $parsed = JSON.parse($result);
    return $parsed;
}
export async function queryMemberSource(packageId, version, targetFramework, assemblyName, typeIdentity, memberName, selectorKey, metadataToken, styleOptionsJson) {
    const $result = await $requireManagedExports()["DotnetInspect"]["Web"]["Interop"]["Source"]["SourceExports"]["QueryMemberSource.641907440"](packageId, version, targetFramework, assemblyName, typeIdentity, memberName, selectorKey, metadataToken, styleOptionsJson);
    const $parsed = JSON.parse($result);
    return $parsed;
}
export async function queryMemberSourceComparison(operationId, requestJson) {
    const $result = await $requireManagedExports()["DotnetInspect"]["Web"]["Interop"]["Source"]["SourceExports"]["QueryMemberSourceComparison.451505237"](operationId, requestJson);
    const $parsed = JSON.parse($result);
    return $parsed;
}
export async function queryMethodBodyComparison(operationId, requestJson) {
    const $result = await $requireManagedExports()["DotnetInspect"]["Web"]["Interop"]["Source"]["SourceExports"]["QueryMethodBodyComparison.451505237"](operationId, requestJson);
    const $parsed = JSON.parse($result);
    return $parsed;
}
export async function queryMethodBodyComparisonTargets(operationId, packageId, version, targetFramework, assemblyName, typeIdentity, memberName, selectorKey, metadataToken) {
    const $result = await $requireManagedExports()["DotnetInspect"]["Web"]["Interop"]["Source"]["SourceExports"]["QueryMethodBodyComparisonTargets.642387634"](operationId, packageId, version, targetFramework, assemblyName, typeIdentity, memberName, selectorKey, metadataToken);
    const $parsed = JSON.parse($result);
    return $parsed;
}
export async function queryTypeMemberSource(packageId, version, targetFramework, assemblyName, typeIdentity, memberName, selectorKey, metadataToken, styleOptionsJson) {
    const $result = await $requireManagedExports()["DotnetInspect"]["Web"]["Interop"]["Source"]["SourceExports"]["QueryTypeMemberSource.641907440"](packageId, version, targetFramework, assemblyName, typeIdentity, memberName, selectorKey, metadataToken, styleOptionsJson);
    const $parsed = JSON.parse($result);
    return $parsed;
}
export async function queryTypeSource(operationId, packageId, version, targetFramework, assemblyName, typeIdentity, styleOptionsJson) {
    const $result = await $requireManagedExports()["DotnetInspect"]["Web"]["Interop"]["Source"]["SourceExports"]["QueryTypeSource.1160082336"](operationId, packageId, version, targetFramework, assemblyName, typeIdentity, styleOptionsJson);
    const $parsed = JSON.parse($result);
    return $parsed;
}
