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
        value = $ownDataProperty(value, "Catalog");
        value = $ownDataProperty(value, "CatalogExports");
        value = $ownDataProperty(value, "AbandonRetainedWorkspaceNavigation.1618630472");
        if (typeof value !== "function") {
            throw new Error("Managed export \u0027DotnetInspect.Web.Interop.Catalog.CatalogExports.AbandonRetainedWorkspaceNavigation.1618630472\u0027 is not callable.");
        }
    }
    {
        let value = exports;
        value = $ownDataProperty(value, "DotnetInspect");
        value = $ownDataProperty(value, "Web");
        value = $ownDataProperty(value, "Interop");
        value = $ownDataProperty(value, "Catalog");
        value = $ownDataProperty(value, "CatalogExports");
        value = $ownDataProperty(value, "AcknowledgeRetainedWorkspaceNavigation.1618630472");
        if (typeof value !== "function") {
            throw new Error("Managed export \u0027DotnetInspect.Web.Interop.Catalog.CatalogExports.AcknowledgeRetainedWorkspaceNavigation.1618630472\u0027 is not callable.");
        }
    }
    {
        let value = exports;
        value = $ownDataProperty(value, "DotnetInspect");
        value = $ownDataProperty(value, "Web");
        value = $ownDataProperty(value, "Interop");
        value = $ownDataProperty(value, "Catalog");
        value = $ownDataProperty(value, "CatalogExports");
        value = $ownDataProperty(value, "ActivateRetainedWorkspaceDefinition.1579276339");
        if (typeof value !== "function") {
            throw new Error("Managed export \u0027DotnetInspect.Web.Interop.Catalog.CatalogExports.ActivateRetainedWorkspaceDefinition.1579276339\u0027 is not callable.");
        }
    }
    {
        let value = exports;
        value = $ownDataProperty(value, "DotnetInspect");
        value = $ownDataProperty(value, "Web");
        value = $ownDataProperty(value, "Interop");
        value = $ownDataProperty(value, "Catalog");
        value = $ownDataProperty(value, "CatalogExports");
        value = $ownDataProperty(value, "CanonicalizeWorkspaceSharePacket.304094707");
        if (typeof value !== "function") {
            throw new Error("Managed export \u0027DotnetInspect.Web.Interop.Catalog.CatalogExports.CanonicalizeWorkspaceSharePacket.304094707\u0027 is not callable.");
        }
    }
    {
        let value = exports;
        value = $ownDataProperty(value, "DotnetInspect");
        value = $ownDataProperty(value, "Web");
        value = $ownDataProperty(value, "Interop");
        value = $ownDataProperty(value, "Catalog");
        value = $ownDataProperty(value, "CatalogExports");
        value = $ownDataProperty(value, "DeactivateRetainedWorkspaceDefinition.976702342");
        if (typeof value !== "function") {
            throw new Error("Managed export \u0027DotnetInspect.Web.Interop.Catalog.CatalogExports.DeactivateRetainedWorkspaceDefinition.976702342\u0027 is not callable.");
        }
    }
    {
        let value = exports;
        value = $ownDataProperty(value, "DotnetInspect");
        value = $ownDataProperty(value, "Web");
        value = $ownDataProperty(value, "Interop");
        value = $ownDataProperty(value, "Catalog");
        value = $ownDataProperty(value, "CatalogExports");
        value = $ownDataProperty(value, "DecodeWorkspaceShareState.304094707");
        if (typeof value !== "function") {
            throw new Error("Managed export \u0027DotnetInspect.Web.Interop.Catalog.CatalogExports.DecodeWorkspaceShareState.304094707\u0027 is not callable.");
        }
    }
    {
        let value = exports;
        value = $ownDataProperty(value, "DotnetInspect");
        value = $ownDataProperty(value, "Web");
        value = $ownDataProperty(value, "Interop");
        value = $ownDataProperty(value, "Catalog");
        value = $ownDataProperty(value, "CatalogExports");
        value = $ownDataProperty(value, "EncodeWorkspaceShareState.304094707");
        if (typeof value !== "function") {
            throw new Error("Managed export \u0027DotnetInspect.Web.Interop.Catalog.CatalogExports.EncodeWorkspaceShareState.304094707\u0027 is not callable.");
        }
    }
    {
        let value = exports;
        value = $ownDataProperty(value, "DotnetInspect");
        value = $ownDataProperty(value, "Web");
        value = $ownDataProperty(value, "Interop");
        value = $ownDataProperty(value, "Catalog");
        value = $ownDataProperty(value, "CatalogExports");
        value = $ownDataProperty(value, "ListHomeDemos.1310674786");
        if (typeof value !== "function") {
            throw new Error("Managed export \u0027DotnetInspect.Web.Interop.Catalog.CatalogExports.ListHomeDemos.1310674786\u0027 is not callable.");
        }
    }
    {
        let value = exports;
        value = $ownDataProperty(value, "DotnetInspect");
        value = $ownDataProperty(value, "Web");
        value = $ownDataProperty(value, "Interop");
        value = $ownDataProperty(value, "Catalog");
        value = $ownDataProperty(value, "CatalogExports");
        value = $ownDataProperty(value, "ListVocabulary.1310674786");
        if (typeof value !== "function") {
            throw new Error("Managed export \u0027DotnetInspect.Web.Interop.Catalog.CatalogExports.ListVocabulary.1310674786\u0027 is not callable.");
        }
    }
    {
        let value = exports;
        value = $ownDataProperty(value, "DotnetInspect");
        value = $ownDataProperty(value, "Web");
        value = $ownDataProperty(value, "Interop");
        value = $ownDataProperty(value, "Catalog");
        value = $ownDataProperty(value, "CatalogExports");
        value = $ownDataProperty(value, "ObserveRetainedWorkspaceSettlement.976702342");
        if (typeof value !== "function") {
            throw new Error("Managed export \u0027DotnetInspect.Web.Interop.Catalog.CatalogExports.ObserveRetainedWorkspaceSettlement.976702342\u0027 is not callable.");
        }
    }
    {
        let value = exports;
        value = $ownDataProperty(value, "DotnetInspect");
        value = $ownDataProperty(value, "Web");
        value = $ownDataProperty(value, "Interop");
        value = $ownDataProperty(value, "Catalog");
        value = $ownDataProperty(value, "CatalogExports");
        value = $ownDataProperty(value, "RecordRetainedWorkspaceNavigationInstallation.1618630472");
        if (typeof value !== "function") {
            throw new Error("Managed export \u0027DotnetInspect.Web.Interop.Catalog.CatalogExports.RecordRetainedWorkspaceNavigationInstallation.1618630472\u0027 is not callable.");
        }
    }
    {
        let value = exports;
        value = $ownDataProperty(value, "DotnetInspect");
        value = $ownDataProperty(value, "Web");
        value = $ownDataProperty(value, "Interop");
        value = $ownDataProperty(value, "Catalog");
        value = $ownDataProperty(value, "CatalogExports");
        value = $ownDataProperty(value, "ResolveHomeDemo.304094707");
        if (typeof value !== "function") {
            throw new Error("Managed export \u0027DotnetInspect.Web.Interop.Catalog.CatalogExports.ResolveHomeDemo.304094707\u0027 is not callable.");
        }
    }
    {
        let value = exports;
        value = $ownDataProperty(value, "DotnetInspect");
        value = $ownDataProperty(value, "Web");
        value = $ownDataProperty(value, "Interop");
        value = $ownDataProperty(value, "Catalog");
        value = $ownDataProperty(value, "CatalogExports");
        value = $ownDataProperty(value, "RunHomeDemo.976702342");
        if (typeof value !== "function") {
            throw new Error("Managed export \u0027DotnetInspect.Web.Interop.Catalog.CatalogExports.RunHomeDemo.976702342\u0027 is not callable.");
        }
    }
    {
        let value = exports;
        value = $ownDataProperty(value, "DotnetInspect");
        value = $ownDataProperty(value, "Web");
        value = $ownDataProperty(value, "Interop");
        value = $ownDataProperty(value, "Catalog");
        value = $ownDataProperty(value, "CatalogExports");
        value = $ownDataProperty(value, "ValidateRetainedWorkspaceNavigationAuthority.1044747233");
        if (typeof value !== "function") {
            throw new Error("Managed export \u0027DotnetInspect.Web.Interop.Catalog.CatalogExports.ValidateRetainedWorkspaceNavigationAuthority.1044747233\u0027 is not callable.");
        }
    }
}
async function $initializeRuntimeCore(runtime) {
    const exports = await runtime.getAssemblyExports("DotnetInspect.Web.Interop.Catalog");
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
function $serializeJsonInput(value, operation, parameter) {
    const json = JSON.stringify(value);
    if (json === undefined) {
        throw new TypeError(`${operation} parameter '${parameter}' could not be serialized as JSON.`);
    }
    return json;
}
export function abandonRetainedWorkspaceNavigation(realizationId, publicationOrdinal, session, revision, intent, epoch) {
    return $requireManagedExports()["DotnetInspect"]["Web"]["Interop"]["Catalog"]["CatalogExports"]["AbandonRetainedWorkspaceNavigation.1618630472"](realizationId, publicationOrdinal, session, revision, intent, epoch);
}
export function acknowledgeRetainedWorkspaceNavigation(realizationId, publicationOrdinal, session, revision, intent, epoch) {
    return $requireManagedExports()["DotnetInspect"]["Web"]["Interop"]["Catalog"]["CatalogExports"]["AcknowledgeRetainedWorkspaceNavigation.1618630472"](realizationId, publicationOrdinal, session, revision, intent, epoch);
}
export async function activateRetainedWorkspaceDefinition(retainedDefinitionId, label, canonicalLocation, canonicalPacket) {
    const $result = await $requireManagedExports()["DotnetInspect"]["Web"]["Interop"]["Catalog"]["CatalogExports"]["ActivateRetainedWorkspaceDefinition.1579276339"](retainedDefinitionId, label, canonicalLocation, canonicalPacket);
    const $parsed = JSON.parse($result);
    return $parsed;
}
export function canonicalizeWorkspaceSharePacket(encoded) {
    const $result = $requireManagedExports()["DotnetInspect"]["Web"]["Interop"]["Catalog"]["CatalogExports"]["CanonicalizeWorkspaceSharePacket.304094707"](encoded);
    const $parsed = JSON.parse($result);
    return $parsed;
}
export async function deactivateRetainedWorkspaceDefinition(retainedDefinitionId) {
    const $result = await $requireManagedExports()["DotnetInspect"]["Web"]["Interop"]["Catalog"]["CatalogExports"]["DeactivateRetainedWorkspaceDefinition.976702342"](retainedDefinitionId);
    const $parsed = JSON.parse($result);
    return $parsed;
}
export function decodeWorkspaceShareState(encoded) {
    const $result = $requireManagedExports()["DotnetInspect"]["Web"]["Interop"]["Catalog"]["CatalogExports"]["DecodeWorkspaceShareState.304094707"](encoded);
    const $parsed = JSON.parse($result);
    return $parsed;
}
export function encodeWorkspaceShareState(stateJson) {
    const $result = $requireManagedExports()["DotnetInspect"]["Web"]["Interop"]["Catalog"]["CatalogExports"]["EncodeWorkspaceShareState.304094707"]($serializeJsonInput(stateJson, "DotnetInspect.Web.Interop.Catalog.CatalogExports.EncodeWorkspaceShareState.304094707", "stateJson"));
    const $parsed = JSON.parse($result);
    return $parsed;
}
export function listHomeDemos() {
    const $result = $requireManagedExports()["DotnetInspect"]["Web"]["Interop"]["Catalog"]["CatalogExports"]["ListHomeDemos.1310674786"]();
    const $parsed = JSON.parse($result);
    return $parsed;
}
export function listVocabulary() {
    const $result = $requireManagedExports()["DotnetInspect"]["Web"]["Interop"]["Catalog"]["CatalogExports"]["ListVocabulary.1310674786"]();
    const $parsed = JSON.parse($result);
    return $parsed;
}
export async function observeRetainedWorkspaceSettlement(settlementId) {
    const $result = await $requireManagedExports()["DotnetInspect"]["Web"]["Interop"]["Catalog"]["CatalogExports"]["ObserveRetainedWorkspaceSettlement.976702342"](settlementId);
    const $parsed = JSON.parse($result);
    return $parsed;
}
export function recordRetainedWorkspaceNavigationInstallation(realizationId, publicationOrdinal, session, revision, intent, epoch) {
    return $requireManagedExports()["DotnetInspect"]["Web"]["Interop"]["Catalog"]["CatalogExports"]["RecordRetainedWorkspaceNavigationInstallation.1618630472"](realizationId, publicationOrdinal, session, revision, intent, epoch);
}
export function resolveHomeDemo(scenarioId) {
    const $result = $requireManagedExports()["DotnetInspect"]["Web"]["Interop"]["Catalog"]["CatalogExports"]["ResolveHomeDemo.304094707"](scenarioId);
    const $parsed = JSON.parse($result);
    return $parsed;
}
export async function runHomeDemo(scenarioId) {
    const $result = await $requireManagedExports()["DotnetInspect"]["Web"]["Interop"]["Catalog"]["CatalogExports"]["RunHomeDemo.976702342"](scenarioId);
    const $parsed = JSON.parse($result);
    return $parsed;
}
export function validateRetainedWorkspaceNavigationAuthority(realizationId, publicationOrdinal, session, revision, intent, epoch) {
    return $requireManagedExports()["DotnetInspect"]["Web"]["Interop"]["Catalog"]["CatalogExports"]["ValidateRetainedWorkspaceNavigationAuthority.1044747233"](realizationId, publicationOrdinal, session, revision, intent, epoch);
}
