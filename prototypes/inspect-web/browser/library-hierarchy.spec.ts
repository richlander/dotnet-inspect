import { expect, test, type Page } from "@playwright/test";
import { basename } from "node:path";
import { fileURLToPath } from "node:url";
import type {
  BrowserAssemblySurface,
  BrowserMemberSurface,
  BrowserPackageSurface,
  BrowserTypeSurface,
} from "../src/facades/inspect-web-package.d.ts";
import type { PlatformAssemblyRow, PlatformCatalogTarget } from "../src/platform-index.ts";

test.use({ viewport: { width: 900, height: 900 } });

function library(id: string, name: string, count: number): BrowserAssemblySurface {
  return {
    id,
    name,
    version: "1.0.0.0",
    culture: null,
    publicKeyToken: null,
    asset: `lib/net10.0/${name}.dll`,
    publicTypes: count,
    publicMembers: count,
    platformPack: null,
  };
}

const run: BrowserMemberSurface = {
  name: "Run",
  kind: "method",
  signature: "public void Run()",
  accessibility: "public",
  isStatic: false,
  isUnsafe: false,
  isVirtual: false,
  isAbstract: false,
  isOverride: false,
  isExtension: false,
  isObsolete: false,
  genericArity: 0,
  metadataToken: 0x06000001,
  returnType: "void",
  parameters: [],
  documentationId: "M:Example.Widget.Run",
  summary: "Runs the widget.",
  returns: null,
  exceptions: [],
  stableSelector: "Run",
  anchorDigest: "widget-run",
  canonicalSignature: "void Example.Widget.Run()",
  anchorTypeFullName: "Example.Widget",
  graphSelectorKey: "Run",
  bodySelectors: [{ token: 0x06000001, memberName: "Run", selectorKey: "Run" }],
};

function type(id: string, assembly: BrowserAssemblySurface): BrowserTypeSurface {
  return {
    id: `${assembly.id}:${id}`,
    definitionId: id,
    queryId: id,
    metadataId: id,
    name: id.split(".").at(-1)!,
    displayName: id,
    namespace: "Example",
    kind: "class",
    accessibility: "public",
    accessibilityId: "public",
    assembly: `${assembly.name}.dll`,
    assemblyId: assembly.id,
    assemblyName: assembly.name,
    members: 1,
    signature: `public class ${id}`,
    api: [run],
    platformPack: null,
  };
}

const core = library("asset:core", "Example.Core", 1);
const other = library("asset:other", "Example.Other", 1);
const empty = library("asset:empty", "Example.Empty", 0);
const surface: BrowserPackageSurface = {
  package: "Example.Package",
  version: "1.0.0",
  frameworks: ["net10.0"],
  activeFramework: "net10.0",
  defaultAssemblyId: core.id,
  compileLibrary: { status: "Selected", targetFramework: "net10.0", message: null },
  assemblies: [core, other, empty],
  types: [type("Example.Widget", core), type("Example.Neighbor", other)],
  accessibility: [{ id: "public", label: "Public", order: 0, isDefault: true, count: 2 }],
  totalMembers: 2,
  documents: [],
  icon: null,
  inspectionErrors: [],
  inspectionError: null,
};

const platformVersion = "11.0.0-preview.7.26381.103";
const alternatePlatformVersion = "11.0.0-preview.8.26401.1";
function platformRow(assembly: string, kind: PlatformAssemblyRow["kind"], inReferencePack = true, hasImplementation = true): PlatformAssemblyRow {
  return {
    tfm: "net11.0", pack: "netcore.app", assembly, file: `${assembly}.dll`,
    kind, inReferencePack, hasImplementation, forwardsTo: kind === "facade" ? "System.Text.Json" : null,
    version: "11.0.0.0", packVersion: platformVersion, publicTypes: assembly === "System.Text.Json" ? 1 : 0,
  };
}
const platformTarget: PlatformCatalogTarget = {
  tfm: "net11.0", version: platformVersion,
  rows: [
    platformRow("System.Text.Json", "impl"),
    platformRow("System.Facade", "facade"),
    platformRow("System.Private.Empty", "impl", false),
    platformRow("System.ReferenceOnly", "ref", true, false),
  ],
};
const historicalPlatformTarget: PlatformCatalogTarget = {
  tfm: "netstandard2.1", version: "2.1.0",
  rows: [{
    ...platformRow("netstandard", "ref", true, false),
    tfm: "netstandard2.1", pack: "netstandard", packVersion: "2.1.0", version: "2.1.0.0",
  }],
};
interface PlatformFixture {
  warmup?: "pending" | "fail-once";
  discoveryFailure?: boolean;
  catalogFailure?: boolean;
  catalogPending?: boolean;
  wrongCatalog?: boolean;
  libraryFailure?: boolean;
  libraryPending?: boolean;
  libraryPendingPack?: PlatformAssemblyRow["pack"];
  duplicateLibrary?: boolean;
  nativeCoreLib?: boolean;
  mismatchedFile?: boolean;
}

// Exercise the production composition root and bindings with deterministic facade
// responses. Codec and participant-query behavior have separate engine outcome gates.
async function installFacades(
  page: Page,
  model = surface,
  additionalSurfaces: readonly BrowserPackageSurface[] = [],
  references: "ready" | "long" | "empty" | "query-error" | "inspection-error" | "deferred" = "ready",
  integrations: "ready" | "long" | "empty" | "partial" | "partial-empty" | "query-error" | "deferred" = "ready",
  platform?: PlatformFixture,
  opportunities: "ready" | "long" | "empty" | "partial" | "partial-empty" | "query-error" | "deferred" = "ready",
  analysis: "ready" | "long" | "empty" | "partial" | "partial-empty" | "query-error" | "deferred" = "ready",
) {
  const catalogTarget: PlatformCatalogTarget = {
    ...platformTarget,
    rows: [
      ...platformTarget.rows.map(row => platform?.mismatchedFile && row.assembly === "System.Text.Json"
        ? { ...row, file: "PhysicalPayload.dll" } : row),
      ...(platform?.duplicateLibrary ? [{ ...platformTarget.rows[0]!, pack: "aspnetcore.app" as const }] : []),
      ...(platform?.nativeCoreLib ? [{ ...platformRow("System.Private.CoreLib", "impl", false), publicTypes: 1 }] : []),
    ],
  };
  const common = "export async function initializeRuntime() {}";
  const surfaceLookup = `
    const surfaces = ${JSON.stringify([model, ...additionalSurfaces])};
    function surfaceFor(id) {
      return surfaces.find(item => item.package === id) ?? surfaces[0];
    }`;
  const modules: Record<string, string> = {
    host: `
      export async function createRuntime() { return {}; }
      export function configureHost() {}
      export async function runEntryPoint() { return 0; }
      export function buildIdentity() {
        return { version: "fixture", commit: null, builtAtUtc: null, commitUrl: null };
      }`,
    package: `
      ${surfaceLookup}
      const platformTarget = ${JSON.stringify(catalogTarget)};
      const platformOptions = ${JSON.stringify(platform ?? {})};
      let warmupAttempts = 0;
      export async function getPlatformVersions(tfm) {
        document.documentElement.dataset.platformVersionsRequest = tfm;
        if (platformOptions.discoveryFailure) throw new Error("Version discovery offline");
        return ["${alternatePlatformVersion}", "${platformVersion}"];
      }
      export async function getPlatformCatalog(tfm, version) {
        document.documentElement.dataset.platformCatalogRequest = JSON.stringify([tfm, version]);
        if (platformOptions.catalogPending) await new Promise(resolve => document.addEventListener("finish-platform-catalog", resolve, { once: true }));
        if (platformOptions.catalogFailure) throw new Error("Catalog offline");
        const actualVersion = platformOptions.wrongCatalog ? platformTarget.version : version;
        return {
          tfm, version: actualVersion,
          rows: platformTarget.rows.map(row => ({
            ...row, tfm, packVersion: actualVersion,
            assembly: row.assembly === "System.Facade" ? "System.NewFacade" : row.assembly,
            file: row.assembly === "System.Facade" ? "System.NewFacade.dll" : row.file,
          }))
        };
      }
      export async function prefetchPlatformPacks(tfm, version) {
        document.documentElement.dataset.platformWarmup = JSON.stringify([tfm, version]);
        document.documentElement.dataset.platformWarmupAttempts = String(++warmupAttempts);
        if (platformOptions.warmup === "pending") await new Promise(resolve => document.addEventListener("finish-platform-warmup", resolve, { once: true }));
        if (platformOptions.warmup === "fail-once" && warmupAttempts === 1) throw new Error("Archive offline");
      }
      export async function loadRuntimePackAssembly(tfm, version, file, pack, assetFileName = file) {
        if (pack === undefined) {
          const surface = surfaceFor("Microsoft.NETCore.App");
          const selected = surface.assemblies.find(item => item.name + ".dll" === file);
          if (!selected) throw new Error("Unknown platform library: " + file);
          return JSON.stringify({
            ...surface,
            defaultAssemblyId: selected.id,
            activeFramework: tfm,
            version: version || surface.version,
          });
        }
        document.documentElement.dataset.platformLibraryRequest = JSON.stringify([tfm, version, file, pack, assetFileName]);
        if (platformOptions.libraryPending
          && (!platformOptions.libraryPendingPack || platformOptions.libraryPendingPack === pack)) {
          await new Promise(resolve => document.addEventListener("finish-platform-library", resolve, { once: true }));
        }
        if (platformOptions.libraryFailure) throw new Error("Library offline");
        const row = platformTarget.rows.find(row => row.assembly + ".dll" === file && row.pack === pack)
          ?? { assembly: file.replace(/\\.dll$/, ""), file, publicTypes: 0 };
        const assembly = {
          id: row.assembly, name: row.assembly, version: "11.0.0.0",
          culture: null, publicKeyToken: null,
          asset: file === "System.Private.CoreLib.dll" ? "runtimes/linux-x64/native/" + assetFileName : assetFileName,
          publicTypes: row.publicTypes,
          publicMembers: row.publicTypes, platformPack: pack,
        };
        const types = row.publicTypes ? [{
          ...surfaces[0].types[0], id: assembly.id + ":Example.Widget",
          assembly: file, assemblyName: assembly.name, assemblyId: assembly.id, platformPack: pack,
        }] : [];
        return JSON.stringify({
          ...surfaces[0], package: "Microsoft.NETCore.App", version, frameworks: [tfm], activeFramework: tfm,
          defaultAssemblyId: assembly.id, assemblies: [assembly], types,
          totalMembers: row.publicTypes,
        });
      }
      export async function queryPackage(id, version, framework) {
        const surface = surfaceFor(id);
        return {
          ...surface,
          package: id,
          version: version === "latest" ? surface.version : version,
          activeFramework: framework || surface.activeFramework,
        };
      }
      export async function queryPackageVersions() {
        return { versions: ["1.0.0", "0.9.0"], currentVersionInsertionIndex: 0, previousVersion: "0.9.0", previousVersionUnavailableReason: null };
      }
      export async function loadRuntimePack(framework, version) {
        document.documentElement.dataset.runtimePackRequest = JSON.stringify([framework, version]);
        const surface = surfaceFor("Microsoft.NETCore.App");
        return JSON.stringify({ ...surface, activeFramework: framework, version: version || surface.version });
      }
      export function searchTypes(query, candidatesJson) {
        const normalized = query.toLowerCase();
        return JSON.parse(candidatesJson)
          .filter(candidate => candidate.name.toLowerCase().includes(normalized)
            || candidate.full.toLowerCase().includes(normalized))
          .map(candidate => ({ key: candidate.key, kind: "substring" }));
      }
      export function clearWorkspacePackageOccurrences() {}
      export async function queryWorkspacePackageOccurrences(json) {
        return { superseded: false, occurrences: JSON.parse(json).map(coordinate => ({
          ...coordinate, action: JSON.stringify(coordinate),
        })) };
      }
      export async function activateWorkspacePackageOccurrence(action) {
        const coordinate = JSON.parse(action);
        return { activated: true, superseded: false,
          package: await queryPackage(coordinate.package, coordinate.version, coordinate.framework) };
      }
      export function packageCacheStats() {
        return { packages: 1, resident: 1, workspaces: 1, residentBytes: 0 };
      }
      export function listPackageQueryFacets() { return { facets: [] }; }
      export async function queryMemberDocumentation() {
        return { summary: "Runs the widget.", returns: null, parameters: {}, exceptions: [] };
      }
      export async function queryPackageDependencies(id, version, framework, asset) {
        document.documentElement.dataset.referenceRequest = asset;
        const surface = surfaceFor(id);
        const selected = surface.assemblies.find(item => item.id === asset);
        if (!selected) throw new Error("Unknown library: " + asset);
        const scenario = ${JSON.stringify(references)};
        if (scenario === "deferred") {
          await new Promise(resolve => document.addEventListener(
            "fixture-references-ready:" + asset, resolve, { once: true }));
        }
        if (scenario === "query-error") throw new Error("Reference query unavailable.");
        return {
          package: id, version, activeFramework: framework, assembly: selected.name,
          dependencyGroups: [], dependencyGroupError: null,
          assemblyReferences: scenario === "inspection-error" ? "Cannot decode AssemblyRef."
            : { references: scenario === "empty" ? [] : scenario === "long"
            ? Array.from({ length: 80 }, (_, index) => ({
                name: selected.name + "." + "LongNamespace.".repeat(20) + "Reference" + index,
                version: "1.2.3.4", culture: "x-" + Array(20).fill("private").join("-"),
                publicKeyToken: "0123456789abcdef"
              }))
            : [{ name: selected.name + ".Dependency", version: "1.0.0.0", culture: null, publicKeyToken: null }] },
          compileLibrary: surface.compileLibrary
        };
      }`,
    metadata: `
      ${surfaceLookup}
      export async function queryPlatformMetadata(tfm, version, file, pack) {
        document.documentElement.dataset.platformMetadataRequest = JSON.stringify([tfm, version, file, pack]);
        return {
          assemblies: [{
            assembly: file,
            metadataRoots: [{
              requestedRoot: "Cli", canonicalRoot: "Cli",
              rootRelativeVirtualAddress: 256, rootSize: 512, aliasesCliMetadata: false,
              metadataVersion: "v4.0.30319", metadataVersionTruncated: false,
              kind: "Ecma335", isAssembly: true, metadataSize: 512,
              projectedTableTotal: 1, heaps: [],
              tables: [{ index: 0, name: "Module", rowCount: 1, isProjected: true }],
              headers: {}
            }],
            cliMetadataError: null, manifestMetadataError: null,
            readyToRun: null, readyToRunError: null
          }],
          inspectionError: null, compileLibrary: null,
        };
      }
      export async function queryPackageMetadata(id, version, framework, asset) {
        document.documentElement.dataset.metadataRequest = asset;
        const surface = surfaceFor(id);
        const selected = surface.assemblies.find(item => item.id === asset);
        if (!selected) throw new Error("Unknown library: " + asset);
        return {
          assemblies: [{
            assembly: selected.name + ".dll",
            metadataRoots: [{
              requestedRoot: "Cli", canonicalRoot: "Cli",
              rootRelativeVirtualAddress: 256, rootSize: 512, aliasesCliMetadata: false,
              metadataVersion: "v4.0.30319", metadataVersionTruncated: false,
              kind: "Ecma335", isAssembly: true, metadataSize: 512,
              projectedTableTotal: 1, heaps: [],
              tables: [{ index: 0, name: "Module", rowCount: 1, isProjected: true }],
              headers: {}
            }],
            cliMetadataError: null, manifestMetadataError: null,
            readyToRun: null, readyToRunError: null
          }],
          inspectionError: null, compileLibrary: surface.compileLibrary
        };
      }
      export async function queryPackageMetadataTable(
        id, version, framework, asset, metadataRoot, index, startRowId) {
        document.documentElement.dataset.tableRequest = asset;
        return { index, name: "Module", rowCount: 1, startRowId, columns: [], rows: [], error: null };
      }`,
    analysis: `
      ${surfaceLookup}
      export async function queryPackageIntegrations(id, version, framework, asset) {
        document.documentElement.dataset.integrationRequest = asset;
        const surface = surfaceFor(id);
        const selected = surface.assemblies.find(item => item.id === asset);
        if (!selected) throw new Error("Unknown library: " + asset);
        return integrationsFor(surface, selected, version, framework, asset);
      }
      async function integrationsFor(surface, selected, version, framework, asset) {
        const scenario = ${JSON.stringify(integrations)};
        if (scenario === "deferred") {
          await new Promise(resolve => document.addEventListener(
            "fixture-integrations-ready:" + asset, resolve, { once: true }));
        }
        if (scenario === "query-error") throw new Error("Integration query unavailable.");
        const categories = scenario === "empty" || scenario === "partial-empty" ? [] : [
          { integration: "Dependency Injection", signals: [
            { name: selected.name + ".ServiceExtensions.AddWidgets(IServiceCollection services)", shape: "Method", kind: "Extension method" },
            { name: selected.name + ".WidgetService", shape: "Type", kind: "Implementation" }
          ] },
          { integration: "Logging", signals: [
            { name: selected.name + ".WidgetLogger.Write(ILogger logger)", shape: "Method", kind: "Parameter" }
          ] }
        ];
        if (scenario === "long") {
          categories[0].integration += "." + "LongCategory".repeat(30);
          categories[0].signals = Array.from({ length: 80 }, (_, index) => ({
            name: selected.name + "." + "LongNamespace.".repeat(20)
              + "Add" + "LongSignalName".repeat(15) + index + "(IServiceCollection services)",
            shape: "Method", kind: "LongKind".repeat(30)
          }));
        }
        const partial = scenario.startsWith("partial");
        return {
          package: surface.package, version, framework, categories,
          totalSignals: categories.reduce((total, category) => total + category.signals.length, 0),
          isComplete: !partial, inspectionError: partial ? "A library participant could not be inspected." : null,
          compileLibrary: surface.compileLibrary
        };
      }
      export async function queryPlatformIntegrations(framework, version, file, pack) {
        document.documentElement.dataset.platformIntegrationRequest = file + ":" + pack;
        const row = ${JSON.stringify(catalogTarget.rows)}.find(item => item.assembly + ".dll" === file && item.pack === pack);
        if (!row) throw new Error("Unknown platform library: " + file);
        const surface = {
          ...surfaces[0],
          package: "Microsoft.NETCore.App",
          compileLibrary: row.file
        };
        const selected = { id: "platform:" + pack + ":" + file, name: row.assembly, asset: row.file };
        return integrationsFor(surface, selected, version, framework, selected.id);
      }
      async function opportunitiesFor(id, surface, selected, version, framework, asset) {
        const scenario = ${JSON.stringify(opportunities)};
        if (scenario === "deferred") {
          await new Promise(resolve => document.addEventListener(
            "fixture-opportunities-ready:" + asset, resolve, { once: true }));
        }
        if (scenario === "query-error") throw new Error("Opportunity query unavailable.");
        const item = (api, integrationType, lookFor) => ({
          api, integrationType, lookFor,
          sourceDefinitionId: api,
          sourceAssembly: selected.name,
          sourceAssemblyVersion: selected.version,
          sourceAssemblyCulture: selected.culture,
          sourceAssemblyPublicKeyToken: selected.publicKeyToken
        });
        const categories = scenario === "empty" || scenario === "partial-empty" ? [] : [
          { integration: "Cloud clients", items: [
            item("Example.Widget", "Microsoft.Extensions.Http IHttpClientBuilder registration", "AddHttpClient, AddStandardResilienceHandler"),
            item(selected.name + ".LegacyCloudClient", "IServiceCollection registration", "AddCloudClient")
          ] },
          { integration: "Configuration", items: [
            item(selected.name + ".LegacyOptions", "IConfiguration binding", "AddOptions, Configure")
          ] }
        ];
        if (scenario === "long") {
          categories[0].integration += "." + "LongOpportunityArea".repeat(25);
          categories[0].items = Array.from({ length: 80 }, (_, index) => item(
            selected.name + "." + "LongNamespace.".repeat(18)
              + "LongOpportunityType".repeat(12) + index,
            "Microsoft.Extensions." + "LongSuggestedPackage".repeat(15)
              + " " + "Long integration kind ".repeat(15),
            Array.from({ length: 8 }, (_, token) => "Add" + "LongApiName".repeat(8) + token).join(", ")
          ));
        }
        const partial = scenario.startsWith("partial");
        return {
          package: id, version, activeFramework: framework, categories,
          totalOpportunities: categories.reduce((total, category) => total + category.items.length, 0),
          isComplete: !partial,
          inspectionError: partial ? "A library participant could not be inspected." : null,
          compileLibrary: surface.compileLibrary
        };
      }
      export async function queryPackageOpportunities(id, version, framework, asset) {
        document.documentElement.dataset.opportunityRequest = asset;
        const surface = surfaceFor(id);
        const selected = surface.assemblies.find(item => item.id === asset);
        if (!selected) throw new Error("Unknown library: " + asset);
        return opportunitiesFor(id, surface, selected, version, framework, asset);
      }
      export async function queryPlatformOpportunities(framework, version, file, pack) {
        document.documentElement.dataset.platformOpportunityRequest = file + ":" + pack;
        const row = ${JSON.stringify(catalogTarget.rows)}.find(item => item.assembly + ".dll" === file && item.pack === pack);
        if (!row) throw new Error("Unknown platform library: " + file);
        const surface = {
          ...surfaces[0],
          package: "Microsoft.NETCore.App",
          compileLibrary: row.file
        };
        const selected = {
          id: "platform:" + pack + ":" + file,
          name: row.assembly,
          asset: row.file,
          version: row.version,
          culture: null,
          publicKeyToken: null
        };
        return opportunitiesFor(
          surface.package, surface, selected, version, framework, selected.id);
      }
      async function performanceFor(surface, selected, version, framework, requestKey) {
        const selectedType = surface.types.find(item => item.assemblyId === selected.id);
        if (!selectedType) throw new Error("Library has no projected type: " + selected.asset);
        const scenario = ${JSON.stringify(analysis)};
        if (scenario === "deferred") {
          await new Promise(resolve => document.addEventListener(
            "fixture-analysis-ready:" + requestKey, resolve, { once: true }));
        }
        if (scenario === "query-error") throw new Error("Analysis query unavailable.");
        const member = (memberName, opportunityCount, inLoopCount, shapes, confidence) => ({
          assembly: selected.name + ".dll",
          typeId: selectedType.definitionId,
          memberName,
          stableSelector: "Run",
          bodyTokens: [100663297],
          opportunityCount,
          inLoopCount,
          shapes,
          confidence
        });
        const members = scenario === "empty" || scenario === "partial-empty" ? [] : [
          member("Run", 3, 1, ["box-value-type", "string-concat"], "high"),
          member("Write", 1, 0, ["array-allocation"], "medium")
        ];
        if (scenario === "long") {
          members.splice(0, members.length, ...Array.from({ length: 80 }, (_, index) =>
            member(
              "LongPerformanceMember".repeat(12) + index,
              index + 1,
              index % 3,
              ["LongAllocationShape".repeat(12), "loop-carried-allocation"],
              index % 2 ? "medium" : "high")));
        }
        const partial = scenario.startsWith("partial");
        return {
          members,
          inspectionError: partial ? "A method body could not be analyzed." : null,
          nonPublicOpportunities: 2,
          totalOpportunities: members.reduce((total, item) => total + item.opportunityCount, 0) + 2,
          compileLibrary: surface.compileLibrary
        };
      }
      export async function queryPackagePerformance(id, version, framework, asset) {
        document.documentElement.dataset.analysisRequest = asset;
        const surface = surfaceFor(id);
        const selected = surface.assemblies.find(item => item.id === asset);
        if (!selected) throw new Error("Unknown library: " + asset);
        return performanceFor(surface, selected, version, framework, asset);
      }
      export async function queryPlatformPerformance(framework, version, file, pack) {
        document.documentElement.dataset.platformAnalysisRequest = file + ":" + pack;
        const row = ${JSON.stringify(catalogTarget.rows)}.find(item => item.assembly + ".dll" === file && item.pack === pack);
        if (!row) throw new Error("Unknown platform library: " + file);
        const selected = {
          ...surfaces[0].assemblies[0],
          id: "platform:" + pack + ":" + file,
          name: row.assembly,
          asset: row.file,
          platformPack: pack
        };
        const selectedType = {
          ...surfaces[0].types[0],
          id: selected.id + ":Example.Widget",
          assembly: file,
          assemblyName: row.assembly,
          assemblyId: selected.id,
          platformPack: pack
        };
        const surface = {
          ...surfaces[0],
          package: "Microsoft.NETCore.App",
          compileLibrary: row.file,
          assemblies: [selected],
          types: [selectedType]
        };
        return JSON.stringify(await performanceFor(
          surface, selected, version, framework, selected.id));
      }`,
    source: "",
    "call-graph": "",
    catalog: `
      export function listVocabulary() { return { sections: [] }; }
      export function listHomeDemos() { return { demos: [] }; }
      export function encodeWorkspaceShareState(json) {
        return { succeeded: true, packet: btoa(json), failure: null };
      }
      export function decodeWorkspaceShareState(packet) {
        return { succeeded: true, state: JSON.parse(atob(packet)), failure: null };
      }`,
  };
  await page.route("https://cdn.jsdelivr.net/**", route => route.abort());
  await page.route("**/inspect-web-*.js", route => {
    const name = new URL(route.request().url()).pathname
      .replace("/inspect-web-", "").replace(".js", "");
    const body = modules[name];
    if (body === undefined) throw new Error(`Unexpected facade: ${name}`);
    return route.fulfill({
      contentType: "text/javascript",
      body: `${common}\n${body}`,
    });
  });
  await page.route(/\/assets\/[^/]+\.(?:js|css|woff2?|ttf)$/, route => route.fulfill({
    path: fileURLToPath(new URL(
      `../dist/assets/${basename(new URL(route.request().url()).pathname)}`,
      import.meta.url)),
  }));
  await page.route("**/assets/platform-index.json", route =>
    route.fulfill(platform ? {
      contentType: "application/json",
      body: JSON.stringify({ schemaVersion: 1, defaultFramework: "net11.0", targets: [catalogTarget, historicalPlatformTarget] }),
    } : { status: 404, body: "Platform catalog is not part of this fixture." }));
  await page.route("**/*", route =>
    route.request().resourceType() === "document"
      ? route.fulfill({
          path: fileURLToPath(new URL("../dist/index.html", import.meta.url)),
          contentType: "text/html",
        })
      : route.fallback());
}

const root = "/?package=Example.Package&version=1.0.0&framework=net10.0#pkg";

async function openPlatform(page: Page, options: PlatformFixture = {}) {
  await installFacades(page, surface, [], "ready", "ready", options);
  await page.goto("/");
  await page.locator("[data-sl-load-runtime]").click();
  await expect(page.locator('[data-scope="platform"]')).toHaveAttribute("aria-selected", "true");
}

test("Platform opens its catalog before warm-up, with reference membership and role labels", async ({ page }) => {
  await openPlatform(page, { warmup: "pending" });
  await expect(page.locator(".platform-library-row")).toHaveCount(3);
  await expect(page.locator("#platform-framework option")).toHaveText(["net11.0"]);
  await expect(page.locator("#inspector-panel")).toContainText("Downloading runtime packs");
  await expect(page.locator("[data-platform-role=facade]")).toContainText("Facade");
  await expect(page.locator("[data-platform-role=facade] .platform-role-icon")).toHaveCSS("border-top-style", "dashed");
  await expect(page.locator("[data-platform-role=implementation] .platform-role-icon")).toHaveCSS("border-top-style", "solid");
  await expect(page.locator("[data-platform-role=reference]")).toContainText("Unsupported: no runtime implementation");
  await expect(page.locator("[data-platform-role=reference] button")).toHaveCount(0);
  await expect(page.locator("[data-scope=package], [data-package-lens], #package-version, #framework")).toHaveCount(0);
  await page.getByLabel("Include all libraries").check();
  const privateRow = page.locator("[data-platform-role=private] button");
  await expect(privateRow).toBeEnabled();
  await expect(privateRow).toContainText("Private implementation");
  await expect(privateRow.locator(".platform-role-icon")).toHaveText("P");
  await expect(privateRow.locator(".platform-role-icon")).toHaveCSS("border-top-style", "double");
  await privateRow.click();
  await expect(page.locator('[data-scope="library"]')).toHaveAttribute("aria-selected", "true");
  await expect(page.locator("#inspector-panel h1")).toHaveText("System.Private.Empty");
  await expect(page.locator("#type-list [data-type]")).toHaveCount(0);
  await expect(page.locator("html")).toHaveAttribute("data-platform-library-request",
    JSON.stringify(["net11.0", platformVersion, "System.Private.Empty.dll", "netcore.app", "System.Private.Empty.dll"]));
});

test("Platform catalog occupies the full workspace at every responsive breakpoint", async ({ page }) => {
  await openPlatform(page);
  const workspace = page.locator(".platform-workspace");

  for (const width of [1440, 900, 390]) {
    await page.setViewportSize({ width, height: 844 });
    const layout = await workspace.evaluate(element => {
      const detail = element.querySelector<HTMLElement>(":scope > .detail-pane");
      if (!detail) throw new Error("Platform detail pane is missing.");
      return {
        columns: getComputedStyle(element).gridTemplateColumns.trim().split(/\s+/),
        detailWidth: detail.getBoundingClientRect().width,
        workspaceWidth: element.getBoundingClientRect().width,
      };
    });

    expect(layout.columns).toHaveLength(1);
    expect(Math.abs(layout.workspaceWidth - layout.detailWidth)).toBeLessThan(1);
  }
});

test("Platform warm-up failure preserves inventory and has an independent retry", async ({ page }) => {
  await openPlatform(page, { warmup: "fail-once", discoveryFailure: true });
  await expect(page.locator("#inspector-panel")).toContainText("Archive offline");
  await expect(page.locator("#inspector-panel")).toContainText("Version discovery offline");
  await expect(page.locator("#platform-version")).toHaveValue(platformVersion);
  await expect(page.locator(".platform-library-row")).toHaveCount(3);
  await page.locator('[data-platform-retry="warmup"]').click();
  await expect(page.locator("html")).toHaveAttribute("data-platform-warmup-attempts", "2");
  await expect(page.locator('[data-platform-retry="warmup"]')).toHaveCount(0);
  await expect(page.locator(".platform-library-row")).toHaveCount(3);
});

test("Platform exact version switch installs its matching catalog and pins Library inspection", async ({ page }) => {
  await openPlatform(page);
  await expect(page.locator("#platform-version option")).toHaveCount(2);
  await page.getByLabel("Platform version", { exact: true }).selectOption(alternatePlatformVersion);
  await expect(page.locator("#platform-version")).toHaveValue(alternatePlatformVersion);
  await expect(page.locator(".platform-library-list")).toContainText("System.NewFacade");
  await page.getByRole("button", { name: /System.Text.Json Implementation/ }).click();
  await expect(page.locator("html")).toHaveAttribute("data-platform-library-request",
    JSON.stringify(["net11.0", alternatePlatformVersion, "System.Text.Json.dll", "netcore.app", "System.Text.Json.dll"]));
  await expect(page.locator("#inspector-panel h1")).toHaveText("System.Text.Json");
  await page.locator('[data-library-lens="metadata"]').click();
  await expect(page.locator("html")).toHaveAttribute("data-platform-metadata-request",
    JSON.stringify(["net11.0", alternatePlatformVersion, "System.Text.Json.dll", "netcore.app"]));
  await expect(page.locator("#inspector-panel")).toContainText("Module");
  await expect(page.locator("#package-version, #framework, [data-platform-metadata-library]")).toHaveCount(0);
});

test("Platform mismatched catalog does not relabel the installed inventory", async ({ page }) => {
  await openPlatform(page, { wrongCatalog: true });
  await expect(page.locator("#platform-version option")).toHaveCount(2);
  await page.getByLabel("Platform version", { exact: true }).selectOption(alternatePlatformVersion);
  await expect(page.locator("#inspector-panel")).toContainText("does not match");
  await expect(page.locator("#platform-version")).toHaveValue(platformVersion);
  await expect(page.locator(".platform-library-list")).toContainText("System.Facade");
  await expect(page.locator(".platform-library-list")).not.toContainText("System.NewFacade");
});

test("Spotlight offers separate NuGet and Platform System.Text.Json destinations without warming packs", async ({ page }) => {
  await installFacades(page, surface, [], "ready", "ready", {});
  await page.route("https://azuresearch-usnc.nuget.org/query?**", route => route.fulfill({
    contentType: "application/json", body: JSON.stringify({ data: [{ id: "System.Text.Json", version: "11.0.0-preview.7" }] }),
  }));
  await page.goto("/");
  await expect(page.getByRole("contentinfo")).toContainText("browser wasm ready");
  await page.getByRole("combobox").fill("System.Text.Json");
  await expect(page.locator('[data-sl-pkg-load="System.Text.Json"]')).toBeVisible();
  await expect(page.locator('[data-sl-platform-lib="System.Text.Json"]')).toContainText("Platform");
  await expect(page.locator("html")).not.toHaveAttribute("data-platform-warmup");
  await page.locator('[data-sl-platform-lib="System.Text.Json"]').click();
  await expect(page.locator('[data-scope="library"]')).toHaveAttribute("aria-selected", "true");
  await expect(page.locator('[data-scope="platform"]')).toHaveAttribute("aria-selected", "false");
});

test("Platform Library parent, history and refresh retain the exact target without choosing a Type", async ({ page }) => {
  await openPlatform(page);
  const platformLocation = page.url();
  await page.getByRole("button", { name: /System.Facade Facade/ }).click();
  await expect(page.locator('[data-scope="library"]')).toHaveAttribute("aria-selected", "true");
  const libraryLocation = page.url();
  await expect(page.locator("#type-list [data-type]")).toHaveCount(0);
  await page.reload();
  await expect(page.locator("#inspector-panel h1")).toHaveText("System.Facade");
  await expect(page.locator("html")).toHaveAttribute("data-platform-library-request",
    JSON.stringify(["net11.0", platformVersion, "System.Facade.dll", "netcore.app", "System.Facade.dll"]));
  await page.locator(".type-browser .nav-back-row").click();
  await expect(page.locator('[data-scope="platform"]')).toHaveAttribute("aria-selected", "true");
  await expect(page.locator(".platform-library-list")).toBeFocused();
  await expect(page).toHaveURL(platformLocation);
  await page.getByRole("button", { name: "Application menu", exact: true }).press("Alt+ArrowLeft");
  await expect(page).toHaveURL(libraryLocation);
  await expect(page.locator("#inspector-panel h1")).toHaveText("System.Facade");
  await page.getByRole("button", { name: "Application menu", exact: true }).press("Alt+ArrowRight");
  await expect(page).toHaveURL(platformLocation);
  await page.reload();
  await expect(page.locator('[data-scope="platform"]')).toHaveAttribute("aria-selected", "true");
  await expect(page.locator("#platform-version")).toHaveValue(platformVersion);
  await expect(page.locator("html")).not.toHaveAttribute("data-platform-library-request");
});

test("history-restored cached Platform Libraries remain usable through Spotlight", async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  await openPlatform(page);
  await page.getByRole("button", { name: /System.Text.Json Implementation/ }).click();
  const libraryLocation = page.url();
  await page.locator(".type-browser .nav-back-row").click();
  await expect(page.locator('[data-scope="platform"]')).toHaveAttribute("aria-selected", "true");
  await page.getByLabel("Platform version", { exact: true }).selectOption(alternatePlatformVersion);
  await expect(page.locator("#platform-version")).toHaveValue(alternatePlatformVersion);
  await page.getByRole("button", { name: /System.Text.Json Implementation/ }).click();
  await expect(page.locator('[data-scope="library"]')).toHaveAttribute("aria-selected", "true");
  await page.getByRole("button", { name: "Application menu", exact: true }).press("Alt+ArrowLeft");
  await expect(page.locator("#platform-version")).toHaveValue(alternatePlatformVersion);
  await page.getByRole("button", { name: "Application menu", exact: true }).press("Alt+ArrowLeft");
  await expect(page.locator("#platform-version")).toHaveValue(platformVersion);
  await page.getByRole("button", { name: "Application menu", exact: true }).press("Alt+ArrowLeft");
  await expect(page).toHaveURL(libraryLocation);
  await expect(page.locator("#inspector-panel h1")).toHaveText("System.Text.Json");

  await page.getByRole(
    "button",
    { name: "Search types, members, packages", exact: true },
  ).click();
  await page.locator("#spotlight-input").fill("Widget");
  await page.locator('[data-sl-type*="Example.Widget"]:not([data-sl-member])').first().click();
  await expect(page.locator('[data-scope="type"]')).toHaveAttribute("aria-selected", "true");
  await expect(page.locator(".inspected-target")).toContainText("Example.Widget");
});

test("Platform Library acquisition failure keeps its catalog usable", async ({ page }) => {
  await openPlatform(page, { libraryFailure: true });
  await page.getByRole("button", { name: /System.Text.Json Implementation/ }).click();
  await expect(page.locator("#inspector-panel")).toContainText("Library offline");
  await expect(page.locator(".platform-library-row")).toHaveCount(3);
  await expect(page.locator('[data-platform-retry="library"]')).toBeEnabled();
});

test("Catalog-only Platform is a Workspace coordinate and pending Library work cannot steal it", async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  await openPlatform(page, { libraryPending: true, warmup: "pending" });
  await page.getByRole("button", { name: /System.Text.Json Implementation/ }).click();
  await expect(page.locator("html")).toHaveAttribute("data-platform-library-request");
  await page.locator('[data-application-scope="workspace"]').click();
  await expect(page.locator("#inspector-panel h1")).toHaveText("Workspace");
  await expect(page.locator("#inspector-panel")).toContainText(platformVersion);
  await page.evaluate(() => document.dispatchEvent(new Event("finish-platform-library")));
  await expect(page.locator("#inspector-panel h1")).toHaveText("Workspace");
  await page.locator("[data-workspace-platform]").click();
  await expect(page.locator(".platform-library-row")).toHaveCount(3);
  await expect(page.locator("#platform-version")).toHaveValue(platformVersion);
  await expect(page.locator('[data-scope="platform"]')).toHaveAttribute("aria-selected", "true");
});

test("pending Platform catalog cannot overwrite a loaded Package selected through Spotlight", async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  await installFacades(page, surface, [], "ready", "ready", { catalogPending: true });
  await page.goto(root);
  await page.getByRole(
    "button",
    { name: "Search types, members, packages", exact: true },
  ).click();
  await page.locator("[data-sl-load-runtime]").click();
  await expect(page.locator('[data-scope="platform"]'))
    .toHaveAttribute("aria-selected", "true");
  await expect(page.locator("#platform-version option")).toHaveCount(2);
  await page.getByLabel("Platform version", { exact: true })
    .selectOption(alternatePlatformVersion);
  await expect(page.locator("html")).toHaveAttribute(
    "data-platform-catalog-request",
    JSON.stringify(["net11.0", alternatePlatformVersion]),
  );

  await page.getByRole(
    "button",
    { name: "Search types, members, packages", exact: true },
  ).click();
  await page.locator("#spotlight-input").fill("Example.Package");
  await page.locator('[data-sl-pkg-open="Example.Package"]').click();
  await expect(page.locator('[data-scope="library"]')).toHaveAttribute("aria-selected", "true");
  await page.locator('[data-scope="package"]').click();
  await expect(page.locator('[data-scope="package"]')).toHaveAttribute("aria-selected", "true");

  await page.evaluate(() => document.dispatchEvent(new Event("finish-platform-catalog")));
  await expect(page.locator('[data-scope="package"]')).toHaveAttribute("aria-selected", "true");
  await expect(page.locator("#inspector-panel h1")).toHaveText("Example.Package");
});

test("pending Platform catalog cannot overwrite a loaded Type selected through Commands", async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  await installFacades(page, surface, [], "ready", "ready", { catalogPending: true });
  await page.goto(root);
  await page.getByRole(
    "button",
    { name: "Search types, members, packages", exact: true },
  ).click();
  await page.locator("[data-sl-load-runtime]").click();
  await expect(page.locator('[data-scope="platform"]'))
    .toHaveAttribute("aria-selected", "true");
  await page.getByRole("button", { name: /System.Text.Json Implementation/ }).click();
  await expect(page.locator('[data-scope="library"]')).toHaveAttribute("aria-selected", "true");
  await page.locator(".type-browser .nav-back-row").click();
  await expect(page.locator('[data-scope="platform"]')).toHaveAttribute("aria-selected", "true");
  await page.getByLabel("Platform version", { exact: true })
    .selectOption(alternatePlatformVersion);
  await expect(page.locator("html")).toHaveAttribute(
    "data-platform-catalog-request",
    JSON.stringify(["net11.0", alternatePlatformVersion]),
  );

  await page.getByRole(
    "button",
    { name: "Search types, members, packages", exact: true },
  ).click();
  await page.locator('[data-sl-scope="commands"]').click();
  await page.locator("#spotlight-input").fill("type Widget");
  await expect(page.locator("#spotlight-results")).toContainText("type Widget");
  await page.keyboard.press("Enter");
  await expect(page.locator('[data-scope="type"]')).toHaveAttribute("aria-selected", "true");
  await expect(page.locator(".inspected-target")).toContainText("Example.Widget");

  await page.evaluate(() => document.dispatchEvent(new Event("finish-platform-catalog")));
  await expect(page.locator('[data-scope="type"]')).toHaveAttribute("aria-selected", "true");
  await expect(page.locator(".inspected-target")).toContainText("Example.Widget");
});

for (const destination of ["Type", "Member"] as const) {
  test(`pending Platform Library cannot overwrite a loaded ${destination} selected through Spotlight`, async ({ page }) => {
    await page.setViewportSize({ width: 1440, height: 900 });
    await installFacades(page, surface, [], "ready", "ready", { libraryPending: true });
    await page.goto(root);
    await page.getByRole(
      "button",
      { name: "Search types, members, packages", exact: true },
    ).click();
    await page.locator("[data-sl-load-runtime]").click();
    await expect(page.locator('[data-scope="platform"]'))
      .toHaveAttribute("aria-selected", "true");
    await page.getByRole("button", { name: /System.Text.Json Implementation/ }).click();
    await expect(page.locator("html")).toHaveAttribute("data-platform-library-request");

    await page.getByRole(
      "button",
      { name: "Search types, members, packages", exact: true },
    ).click();
    await page.locator("#spotlight-input").fill(
      destination === "Type" ? "Widget" : "Run");
    if (destination === "Type") {
      await page.locator('[data-sl-type*="Example.Widget"]').click();
      await expect(page.locator('[data-scope="type"]')).toHaveAttribute("aria-selected", "true");
      await expect(page.locator(".inspected-target")).toContainText("Example.Widget");
    } else {
      await page.locator('[data-sl-member][data-sl-type*="Example.Widget"]').click();
      await expect(page.locator('[data-scope="member"]')).toHaveAttribute("aria-selected", "true");
      await expect(page.locator("#inspector-panel h1")).toContainText("Run");
    }

    await page.evaluate(() => document.dispatchEvent(new Event("finish-platform-library")));
    await expect(page.locator(
      destination === "Type" ? '[data-scope="type"]' : '[data-scope="member"]',
    )).toHaveAttribute("aria-selected", "true");
  });
}

test("Platform version history restores the prior exact catalog on a narrow viewport", async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 });
  await openPlatform(page);
  const originalLocation = page.url();
  await expect(page.locator("#platform-version option")).toHaveCount(2);
  await page.getByLabel("Platform version", { exact: true }).selectOption(alternatePlatformVersion);
  await expect(page.locator("#platform-version")).toHaveValue(alternatePlatformVersion);
  await page.getByRole("button", { name: "Application menu", exact: true }).press("Alt+ArrowLeft");
  await expect(page).toHaveURL(originalLocation);
  await expect(page.locator("#platform-version")).toHaveValue(platformVersion);
  await expect(page.locator(".platform-library-list")).toContainText("System.Facade");
  await expect(page.locator(".platform-library-list")).not.toContainText("System.NewFacade");
});

test("A failed dynamic Platform catalog leaves the installed target and inventory available", async ({ page }) => {
  await openPlatform(page, { catalogFailure: true });
  await expect(page.locator("#platform-version option")).toHaveCount(2);
  await page.getByLabel("Platform version", { exact: true }).selectOption(alternatePlatformVersion);
  await expect(page.locator("#inspector-panel")).toContainText("Catalog offline");
  await expect(page.locator("#platform-version")).toHaveValue(platformVersion);
  await expect(page.locator(".platform-library-row")).toHaveCount(3);
  await expect(page.locator('[data-platform-retry="catalog"]')).toBeEnabled();
});

test("Sequential same-named Platform Libraries replace the prior family and retain the selected pack", async ({ page }) => {
  await openPlatform(page, { duplicateLibrary: true });
  await page.getByRole("button", { name: /System.Text.Json Implementation netcore.app/ }).click();
  await expect(page.locator("html")).toHaveAttribute("data-platform-library-request",
    JSON.stringify(["net11.0", platformVersion, "System.Text.Json.dll", "netcore.app", "System.Text.Json.dll"]));
  const netCoreLibraryLocation = page.url();
  await page.locator(".type-browser .nav-back-row").click();
  await expect(page.locator('[data-scope="platform"]')).toHaveAttribute("aria-selected", "true");
  const platformLocation = page.url();
  await page.getByRole("button", { name: /System.Text.Json Implementation aspnetcore.app/ }).click();
  await expect(page.locator("html")).toHaveAttribute("data-platform-library-request",
    JSON.stringify(["net11.0", platformVersion, "System.Text.Json.dll", "aspnetcore.app", "System.Text.Json.dll"]));
  const aspNetLibraryLocation = page.url();
  await page.locator('[data-library-lens="metadata"]').click();
  await expect(page.locator("html")).toHaveAttribute("data-platform-metadata-request",
    JSON.stringify(["net11.0", platformVersion, "System.Text.Json.dll", "aspnetcore.app"]));
  await page.getByRole("button", { name: "Application menu", exact: true }).press("Alt+ArrowLeft");
  await expect(page).toHaveURL(aspNetLibraryLocation);
  await page.getByRole("button", { name: "Application menu", exact: true }).press("Alt+ArrowLeft");
  await expect(page).toHaveURL(platformLocation);
  await expect(page.locator('[data-scope="platform"]')).toHaveAttribute("aria-selected", "true");
  await page.getByRole("button", { name: "Application menu", exact: true }).press("Alt+ArrowLeft");
  await expect(page).toHaveURL(netCoreLibraryLocation);
  await expect(page.locator("html")).toHaveAttribute("data-platform-library-request",
    JSON.stringify(["net11.0", platformVersion, "System.Text.Json.dll", "netcore.app", "System.Text.Json.dll"]));
  await expect(page.locator("#inspector-panel h1")).toHaveText("System.Text.Json");
  await page.locator('[data-library-lens="metadata"]').click();
  await expect(page.locator("html")).toHaveAttribute("data-platform-metadata-request",
    JSON.stringify(["net11.0", platformVersion, "System.Text.Json.dll", "netcore.app"]));
  await page.reload();
  await expect(page.locator("html")).toHaveAttribute("data-platform-metadata-request",
    JSON.stringify(["net11.0", platformVersion, "System.Text.Json.dll", "netcore.app"]));
});

test("Runtime-only CoreLib in the native asset directory uses the same exact Library inspector", async ({ page }) => {
  await openPlatform(page, { nativeCoreLib: true });
  await expect(page.getByRole("button", { name: /System.Private.CoreLib/ })).toHaveCount(0);
  await page.getByLabel("Include all libraries").check();
  await page.getByRole("button", { name: /System.Private.CoreLib Private implementation/ }).click();
  await expect(page.locator("#inspector-panel h1")).toHaveText("System.Private.CoreLib");
  await page.locator('[data-library-lens="metadata"]').click();
  await expect(page.locator("html")).toHaveAttribute("data-platform-metadata-request",
    JSON.stringify(["net11.0", platformVersion, "System.Private.CoreLib.dll", "netcore.app"]));
  await page.reload();
  await expect(page.locator("html")).toHaveAttribute("data-platform-metadata-request",
    JSON.stringify(["net11.0", platformVersion, "System.Private.CoreLib.dll", "netcore.app"]));
});

test("Platform requests metadata identity while sharing the exact physical Library filename", async ({ page }) => {
  await openPlatform(page, { mismatchedFile: true });
  await page.getByRole("button", { name: /System.Text.Json Implementation/ }).click();
  await expect(page.locator("html")).toHaveAttribute("data-platform-library-request",
    JSON.stringify(["net11.0", platformVersion, "System.Text.Json.dll", "netcore.app", "PhysicalPayload.dll"]));
  await expect(page.locator("#inspector-panel h1")).toHaveText("System.Text.Json");
  await page.locator('[data-library-lens="metadata"]').click();
  await expect(page.locator("html")).toHaveAttribute("data-platform-metadata-request",
    JSON.stringify(["net11.0", platformVersion, "System.Text.Json.dll", "netcore.app"]));
  await page.reload();
  await expect(page.locator("html")).toHaveAttribute("data-platform-metadata-request",
    JSON.stringify(["net11.0", platformVersion, "System.Text.Json.dll", "netcore.app"]));
});

test("Package and catalog-only Platform remain distinct coordinates in the same shared Workspace", async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  await installFacades(page, surface, [], "ready", "ready", {});
  await page.goto(root);
  await page.getByRole("button", { name: "Search types, members, packages", exact: true }).click();
  await page.locator("[data-sl-load-runtime]").click();
  await expect(page.locator('[data-scope="platform"]')).toHaveAttribute("aria-selected", "true");
  await page.reload();
  await expect(page.locator("#platform-version")).toHaveValue(platformVersion);
  await expect(page.locator("html")).not.toHaveAttribute("data-platform-library-request");
  await page.locator('[data-application-scope="workspace"]').click();
  await expect(page.locator("#inspector-panel")).toContainText("2 loaded coordinates");
  await expect(page.locator("#inspector-panel")).toContainText("Example.Package");
  await expect(page.locator("[data-workspace-platform]")).toContainText(platformVersion);
  await page.locator("[data-workspace-activate]").click();
  await expect(page.locator('[data-scope="library"]')).toHaveAttribute("aria-selected", "true");
  await expect(page.locator(".library-overview-surface h1")).toHaveText(core.name);
  await page.locator('[data-application-scope="workspace"]').click();
  await page.locator("[data-workspace-platform]").click();
  await expect(page.locator('[data-scope="platform"]')).toHaveAttribute("aria-selected", "true");
  await expect(page.locator("#platform-version")).toHaveValue(platformVersion);
});

test("catalog-only Platform retains its Workspace identity and canonical URL across another Workspace", async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  await page.addInitScript(() => localStorage.setItem(
    "inspect-recent-packages",
    JSON.stringify([{ id: "Second.Package", version: "1.0.0", framework: "net10.0" }]),
  ));
  await openPlatform(page);
  const platformLocation = page.url();
  const platformWorkspace = await currentWorkspaceHistoryState(page);
  expect(platformWorkspace.id).not.toBeNull();

  await page.keyboard.press("Control+p");
  await page.locator('[data-sl-pkg-recent="Second.Package"]').click();
  await expect(page.locator(".inspected-target")).toContainText("Second.Package");
  expect((await currentWorkspaceHistoryState(page)).id).not.toBe(platformWorkspace.id);

  await page.goBack();
  await expect(page).toHaveURL(platformLocation);
  await expect(page.locator('[data-scope="platform"]')).toHaveAttribute("aria-selected", "true");
  expect(await currentWorkspaceHistoryState(page)).toEqual(platformWorkspace);
  await expect(page.locator("#platform-version")).toHaveValue(platformVersion);
  await expect(page.locator("html")).not.toHaveAttribute("data-platform-library-request");

  await page.locator('[data-application-scope="workspace"]').click();
  await expect(page.locator("[data-workspace-select]")).toContainText("1 loaded coordinate");
  await expect(page.locator("[data-workspace-switch]")).toHaveCount(1);
  await page.locator("[data-workspace-platform]").click();
  await expect(page).toHaveURL(platformLocation);
});

test("missing shipped catalog opens a visible Platform failure without runtime acquisition", async ({ page }) => {
  await installFacades(page);
  await page.goto(root);
  await expect(page.locator("#inspector-panel h1")).toHaveText("Example.Package");
  const originalWorkspace = await currentWorkspaceHistoryState(page);
  await page.keyboard.press("Control+p");
  await page.locator("[data-sl-load-runtime]").click();
  await expect(page.locator('[data-scope="platform"]')).toHaveAttribute("aria-selected", "true");
  await expect(page.locator("#inspector-panel")).toContainText("The Platform catalog could not be loaded.");
  await expect(page.locator('[data-platform-retry="catalog"]')).toBeEnabled();
  await expect(page.locator(".platform-library-row")).toHaveCount(0);
  await expect(page.locator("html")).not.toHaveAttribute("data-runtime-pack-request");
  expect(await currentWorkspaceHistoryState(page)).toEqual(originalWorkspace);
});

test("restored Platform failure retries its own Library request", async ({ page }) => {
  await page.addInitScript(() => localStorage.setItem(
    "inspect-recent-packages",
    JSON.stringify([{ id: "Second.Package", version: "1.0.0", framework: "net10.0" }]),
  ));
  await openPlatform(page, { libraryFailure: true });
  await page.getByRole("button", { name: /System.Text.Json Implementation/ }).click();
  await expect(page.locator("#inspector-panel")).toContainText(
    "Could not open Platform Library: Library offline",
  );
  const firstWorkspace = await currentWorkspaceHistoryState(page);

  await page.keyboard.press("Control+p");
  await page.locator('[data-sl-pkg-recent="Second.Package"]').click();
  await expect(page.locator(".inspected-target")).toContainText("Second.Package");
  await page.keyboard.press("Control+p");
  await page.locator("[data-sl-load-runtime]").click();
  await page.getByRole("button", { name: /System.Facade Facade/ }).click();
  await expect(page.locator("#inspector-panel")).toContainText(
    "Could not open Platform Library: Library offline",
  );

  await page.goBack();
  await expect.poll(() => currentWorkspaceHistoryState(page)).toEqual(firstWorkspace);
  await expect(page.locator("#inspector-panel")).toContainText(
    "Could not open Platform Library: Library offline",
  );
  await page.locator('[data-platform-retry="library"]').click();
  await expect(page.locator("html")).toHaveAttribute(
    "data-platform-library-request",
    JSON.stringify([
      "net11.0",
      platformVersion,
      "System.Text.Json.dll",
      "netcore.app",
      "System.Text.Json.dll",
    ]),
  );
});

for (const pendingRequest of ["Library", "catalog"] as const) {
  test(`restored Platform ${pendingRequest} cancellation becomes retryable`, async ({ page }) => {
    await page.addInitScript(() => localStorage.setItem(
      "inspect-recent-packages",
      JSON.stringify([{ id: "Second.Package", version: "1.0.0", framework: "net10.0" }]),
    ));
    await openPlatform(page, pendingRequest === "Library"
      ? { libraryPending: true, libraryFailure: true }
      : { catalogPending: true, catalogFailure: true });
    const platformWorkspace = await currentWorkspaceHistoryState(page);

    if (pendingRequest === "Library") {
      await page.getByRole("button", { name: /System.Text.Json Implementation/ }).click();
      await expect(page.locator("#inspector-panel")).toContainText(
        "Opening the selected Library...",
      );
    } else {
      await page.getByLabel("Platform version", { exact: true })
        .selectOption(alternatePlatformVersion);
      await expect(page.locator("#inspector-panel")).toContainText(
        "Loading Platform catalog...",
      );
    }

    await page.keyboard.press("Control+p");
    await page.locator('[data-sl-pkg-recent="Second.Package"]').click();
    await expect(page.locator(".inspected-target")).toContainText("Second.Package");
    await page.evaluate(request => document.dispatchEvent(new Event(
      request === "Library" ? "finish-platform-library" : "finish-platform-catalog",
    )), pendingRequest);

    await page.goBack();
    await expect.poll(() => currentWorkspaceHistoryState(page)).toEqual(platformWorkspace);
    await expect(page.locator("#inspector-panel")).toContainText(
      pendingRequest === "Library"
        ? "Platform Library opening was interrupted."
        : "Platform catalog loading was interrupted.",
    );
    await expect(page.locator(
      `[data-platform-retry="${pendingRequest === "Library" ? "library" : "catalog"}"]`,
    )).toBeEnabled();
  });
}

test("same-Workspace navigation retires superseded Platform catalog progress", async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  await openPlatform(page, { catalogPending: true });
  await page.getByLabel("Platform version", { exact: true })
    .selectOption(alternatePlatformVersion);
  await expect(page.locator("#inspector-panel")).toContainText(
    "Loading Platform catalog...",
  );

  await page.getByRole("button", { name: /System.Text.Json Implementation/ }).click();
  await expect(page.locator('[data-scope="library"]')).toHaveAttribute(
    "aria-selected",
    "true",
  );
  await page.evaluate(() =>
    document.dispatchEvent(new Event("finish-platform-catalog")));
  await page.locator('[data-scope="platform"]').click();

  await expect(page.locator("#inspector-panel")).toContainText(
    "Platform catalog loading was interrupted.",
  );
  await expect(page.locator('[data-platform-retry="catalog"]')).toBeEnabled();
  await expect(page.locator("#inspector-panel")).not.toContainText(
    "Loading Platform catalog...",
  );
});

async function currentWorkspaceHistoryState(page: Page): Promise<{
  id: string | null;
  session: string | null;
}> {
  return page.evaluate(() => {
    const isRecord = (
      candidate: unknown,
    ): candidate is Record<string, unknown> =>
      typeof candidate === "object" && candidate !== null;
    const value: unknown = history.state;
    if (!isRecord(value)) {
      return { id: null, session: null };
    }
    return {
      id: typeof value.inspectWorkspaceId === "string"
        ? value.inspectWorkspaceId
        : null,
      session: typeof value.inspectWorkspaceSession === "string"
        ? value.inspectWorkspaceSession
        : null,
    };
  });
}

for (const preferred of [other, empty]) {
  for (const width of [900, 480]) {
    test(`implicit package entry selects product-default ${preferred.name} at ${width}px`, async ({ page }) => {
      await page.setViewportSize({ width, height: 900 });
      await installFacades(page, { ...surface, defaultAssemblyId: preferred.id });
      await page.goto(root.replace("#pkg", ""));
      await expect(page.locator('[data-scope="library"]')).toHaveAttribute("aria-selected", "true");
      await expect(page.locator(".library-overview-surface h1")).toHaveText(preferred.name);
      await page.reload();
      await expect(page.locator(".library-overview-surface h1")).toHaveText(preferred.name);
      if (width === 480) {
        await page.getByRole("button", { name: "Types", exact: true }).click();
      }
      await page.locator("[data-type-nav-back]").click();
      await expect(page.locator(".package-overview-surface h1")).toHaveText(surface.package);
      await page.reload();
      await expect(page.locator(".package-overview-surface h1")).toHaveText(surface.package);
    });
  }
}

for (const status of ["NoCompileAssets", "EmptyCompileGroup"] as const) {
  test(`implicit ${status} package entry retains the Package subject`, async ({ page }) => {
    await installFacades(page, {
      ...surface,
      defaultAssemblyId: null,
      compileLibrary: { status, targetFramework: "net10.0", message: null },
      assemblies: [],
      types: [],
      accessibility: [],
      totalMembers: 0,
    });
    await page.goto(root.replace("#pkg", ""));
    await expect(page.locator('[data-scope="package"]')).toHaveAttribute("aria-selected", "true");
    await expect(page.locator(".package-overview-surface h1")).toHaveText(surface.package);
    await expect(page.locator(".library-list")).toContainText("No managed libraries");
    await expect(page.locator(".query-notice-text")).toContainText(status);
    await page.reload();
    await expect(page.locator(".package-overview-surface h1")).toHaveText(surface.package);
  });
}

for (const incomingPackage of [surface.package, "Second.Package"]) {
  for (const destination of ["default", "Package", "Metadata"]) {
    test(`legacy history restores ${destination} in ${incomingPackage}`, async ({ page }) => {
      const preferred = incomingPackage === surface.package ? other : empty;
      await installFacades(page, { ...surface, defaultAssemblyId: other.id }, [
        { ...surface, package: "Second.Package", defaultAssemblyId: empty.id },
      ]);
      await page.goto(root);
      await page.locator('.library-list [data-lib-scope="asset:core"]').click();
      await expect(page.locator(".library-overview-surface h1")).toHaveText(core.name);

      const target = `/?package=${incomingPackage}&version=1.0.0&framework=net10.0`
        + (destination === "Package" ? "#pkg"
          : destination === "Metadata" ? "#library:metadata"
            : "");
      await page.evaluate(url => history.pushState(null, "", url), target);
      await page.goBack();
      await expect(page.locator(".library-overview-surface h1")).toHaveText(core.name);
      await page.goForward();
      await expect(page.locator(".inspected-target")).toContainText(incomingPackage);
      if (destination === "Package") {
        await expect(page.locator(".package-overview-surface h1")).toHaveText(incomingPackage);
      } else if (destination === "Metadata") {
        await expect(page.locator('[data-library-lens="metadata"]')).toHaveAttribute("aria-selected", "true");
        await expect(page.locator("html")).toHaveAttribute("data-metadata-request", preferred.id);
      } else {
        await expect(page.locator(".library-overview-surface h1")).toHaveText(preferred.name);
      }
      await page.goBack();
      await expect(page.locator(".library-overview-surface h1")).toHaveText(core.name);
    });
  }
}
test("Package comparison targets survive Library, Type, and Member navigation", async ({ page }) => {
  await installFacades(page);
  await page.goto(root);
  await expect(page.locator("#package-diff-target-status"))
    .toHaveText("Previous listed release");
  await expect(page.locator("#package-diff-target option:checked"))
    .toHaveText("Automatic: 0.9.0");
  await page.locator("#package-diff-target").focus();
  await page.locator("#package-diff-target").selectOption("exact:1.0.0");
  await expect(page.locator("#package-diff-target")).toBeFocused();
  await page.locator("#package-clone-target").selectOption("package:0");
  await page.locator('.library-list [data-lib-scope="asset:core"]').click();
  await expect(page.locator("#package-comparison-targets")).toHaveCount(0);
  await page.locator("#type-list [data-type]").click();
  await page.locator('[data-subject-tab]:not([hidden])').first().press("End");
  await expect(page.locator('[data-scope="member"]')).toHaveAttribute("aria-selected", "true");
  await page.locator('[data-subject-tab]:not([hidden])').first().press("Home");
  await expect(page.locator("#package-diff-target")).toHaveValue("exact:1.0.0");
  await expect(page.locator("#package-clone-target")).toHaveValue("package:0");
  await page.locator("#package-diff-target").selectOption("previous");
  await expect(page.locator("#package-diff-target-status"))
    .toHaveText("Previous listed release");
});

for (const initialWidth of [1440, 390]) {
  test(`active subject continuity keeps Library visible from ${initialWidth}px entry`, async ({ page }, testInfo) => {
    await page.setViewportSize({ width: initialWidth, height: 844 });
    await installFacades(page);
    await page.goto(root);
    await expect(page.locator('[data-scope="package"]')).toBeVisible();
    await page.locator('.library-list [data-lib-scope="asset:core"]').click();
    const libraryTab = page.locator('[data-scope="library"]');
    await expect(libraryTab).toHaveAttribute("aria-selected", "true");
    await expect(libraryTab).toBeVisible();
    await expect(page.locator("#inspector-panel h1")).toHaveText(core.name);
    const location = page.url();
    const historyLength = await page.evaluate(() => history.length);
    const menu = page.getByRole("button", { name: "Application menu", exact: true });
    await menu.focus();

    await page.setViewportSize({ width: 390, height: 844 });
    await expect(libraryTab).toBeInViewport({ ratio: 1 });
    await expect(libraryTab).toHaveAttribute("aria-selected", "true");
    await expect(menu).toBeFocused();
    await expect(page).toHaveURL(location);
    expect(await page.evaluate(() => history.length)).toBe(historyLength);
    await testInfo.attach("active-library-390", {
      body: await page.screenshot(),
      contentType: "image/png",
    });

    await page.reload();
    await expect(libraryTab).toHaveAttribute("aria-selected", "true");
    await expect(libraryTab).toBeInViewport({ ratio: 1 });
    await expect(page.locator("#inspector-panel h1")).toHaveText(core.name);
    await page.setViewportSize({ width: 1440, height: 844 });
    for (const subject of ["package", "library", "type"]) {
      await expect(page.locator(`[data-scope="${subject}"]`)).toBeVisible();
    }
    await page.setViewportSize({ width: 390, height: 844 });
    await expect(libraryTab).toBeInViewport({ ratio: 1 });
    await expect(libraryTab).toHaveAttribute("aria-selected", "true");
  });
}

test("active subject continuity retains explicit browsing until the subject changes", async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 });
  await installFacades(page);
  await page.goto(root);
  await page.locator('.library-list [data-lib-scope="asset:core"]').click();
  const libraryTab = page.locator('[data-scope="library"]');
  const packageTab = page.locator('[data-scope="package"]');
  await expect(libraryTab).toBeVisible();
  const menu = page.getByRole("button", { name: "Application menu", exact: true });
  await menu.focus();
  const location = page.url();
  const historyLength = await page.evaluate(() => history.length);
  await page.locator(".slide-strip-subject").hover();
  await page.mouse.wheel(-100, 0);
  await expect(packageTab).toBeVisible();
  await expect(libraryTab).toBeHidden();
  await expect(libraryTab).toHaveAttribute("aria-selected", "true");
  await expect(packageTab).toHaveAttribute("aria-selected", "false");
  await expect(menu).toBeFocused();
  await expect(page).toHaveURL(location);
  expect(await page.evaluate(() => history.length)).toBe(historyLength);

  await page.setViewportSize({ width: 1440, height: 844 });
  await expect(libraryTab).toBeVisible();
  await page.setViewportSize({ width: 390, height: 844 });
  await expect(packageTab).toBeVisible();
  await expect(libraryTab).toBeHidden();
  await page.locator('[data-library-lens="overview"]').press("ArrowRight");
  await page.keyboard.press("Enter");
  await expect(page.locator("#inspector-panel")).toContainText("Example.Core.Dependency");
  await expect(packageTab).toBeVisible();
  await expect(libraryTab).toBeHidden();
  await expect(libraryTab).toHaveAttribute("aria-selected", "true");

  await packageTab.click();
  await expect(packageTab).toHaveAttribute("aria-selected", "true");
  await page.locator('.library-list [data-lib-scope="asset:other"]').click();
  await expect(libraryTab).toHaveAttribute("aria-selected", "true");
  await expect(libraryTab).toBeInViewport({ ratio: 1 });
  await expect(page.locator("#inspector-panel h1")).toHaveText(other.name);
});

test("active subject continuity preserves focus without making a manual window", async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 844 });
  await installFacades(page);
  await page.goto(root);
  await page.locator('.library-list [data-lib-scope="asset:core"]').click();
  const libraryTab = page.locator('[data-scope="library"]');
  const typeTab = page.locator('[data-scope="type"]');
  await typeTab.focus();
  await page.setViewportSize({ width: 390, height: 844 });
  await expect(typeTab).toBeFocused();
  await expect(typeTab).toBeInViewport({ ratio: 1 });
  await expect(typeTab).toHaveAttribute("aria-selected", "false");
  await expect(libraryTab).toHaveAttribute("aria-selected", "true");

  await page.getByRole("button", { name: "Application menu", exact: true }).focus();
  await page.setViewportSize({ width: 1440, height: 844 });
  await expect(libraryTab).toBeVisible();
  await page.setViewportSize({ width: 390, height: 844 });
  await expect(libraryTab).toBeInViewport({ ratio: 1 });
  await libraryTab.press("ArrowLeft");
  await expect(page.locator('[data-scope="package"]')).toBeFocused();
  await expect(page.locator('[data-scope="package"]')).toHaveAttribute("aria-selected", "true");
});

async function openIntegrations(page: Page, location = root) {
  await page.goto(location);
  await page.locator('.library-list [data-lib-scope="asset:core"]').click();
  await page.locator('[data-library-lens="overview"]').press("ArrowRight");
  if (await page.locator('[data-library-lens="references"]').count()) {
    await page.keyboard.press("ArrowRight");
  }
  await page.keyboard.press("Enter");
  await expect(page.locator('[data-library-lens="integrations"]')).toHaveAttribute("aria-selected", "true");
}

async function openOpportunities(page: Page, location = root) {
  await page.goto(location);
  await page.locator('.library-list [data-lib-scope="asset:core"]').click();
  await page.locator('[data-library-lens="overview"]').press("ArrowRight");
  if (await page.locator('[data-library-lens="references"]').count()) {
    await page.keyboard.press("ArrowRight");
  }
  await page.keyboard.press("ArrowRight");
  await page.keyboard.press("Enter");
  await expect(page.locator('[data-library-lens="opportunities"]'))
    .toHaveAttribute("aria-selected", "true");
}

async function openAnalysis(page: Page, location = root) {
  await page.goto(location);
  await page.locator('.library-list [data-lib-scope="asset:core"]').click();
  await page.locator('[data-library-lens="analysis"]')
    .evaluate((element: HTMLElement) => element.click());
  await expect(page.locator('[data-library-lens="analysis"]'))
    .toHaveAttribute("aria-selected", "true");
}

for (const width of [1440, 390]) {
  test(`production Analysis retains selected Library results at ${width}px`, async ({ page }, testInfo) => {
    await page.setViewportSize({ width, height: 900 });
    await installFacades(page);
    await openAnalysis(page);
    const frame = page.locator(".library-analysis-surface");
    await expect(frame.locator(".perf-row")).toHaveCount(2);
    await expect(frame.locator("header")).toContainText("2 public members");
    await expect(frame.locator("header")).toContainText("6 opportunities");
    await expect(frame.locator("header")).toContainText("2 non-public");
    await expect(frame.locator("footer")).toContainText(core.asset);
    await expect(frame.locator("footer")).toContainText("Example.Core, Version=1.0.0.0");
    await expect(page.locator("html")).toHaveAttribute("data-analysis-request", "asset:core");
    await expect(page.locator("#inspector-panel > .type-heading")).toHaveCount(0);
    const panelBox = await page.locator("#inspector-panel").boundingBox();
    const frameBox = await frame.boundingBox();
    const rowBox = await frame.locator(".perf-row").first().boundingBox();
    expect(Math.abs(frameBox!.height - panelBox!.height)).toBeLessThanOrEqual(2);
    expect(Math.abs(frameBox!.width - panelBox!.width)).toBeLessThanOrEqual(2);
    expect(Math.abs(rowBox!.width - frameBox!.width)).toBeLessThanOrEqual(2);
    expect(Math.abs(rowBox!.x - frameBox!.x)).toBeLessThanOrEqual(1);
    await page.screenshot({ path: testInfo.outputPath("analysis-after.png") });
    if (width === 390) {
      const back = page.getByRole("button", { name: "Types", exact: true });
      await back.click();
      await expect(page.locator("#type-list")).toBeFocused();
      await page.getByRole("button", { name: "Show details", exact: true }).click();
      await expect(back).toBeFocused();
      await expect(frame).toBeVisible();
    }
  });

  test(`production Analysis contains long fields and keeps its frame while scrolling at ${width}px`, async ({ page }) => {
    await page.setViewportSize({ width, height: 900 });
    const longCore = library(core.id, "Example." + "LongLibraryName".repeat(25), 1);
    await installFacades(page, {
      ...surface, assemblies: [longCore], types: [type("Example.Widget", longCore)], totalMembers: 1,
    }, [], "ready", "ready", undefined, "ready", "long");
    await openAnalysis(page);
    const frame = page.locator(".library-analysis-surface");
    await expect(frame.locator(".perf-row")).toHaveCount(80);
    const header = await frame.locator("header").boundingBox();
    const footer = await frame.locator("footer").boundingBox();
    const scroll = frame.locator(".library-analysis-scroll");
    const geometry = await scroll.evaluate(element => ({
      width: element.clientWidth, scrollWidth: element.scrollWidth,
      height: element.clientHeight, scrollHeight: element.scrollHeight,
      pageWidth: document.documentElement.clientWidth,
      pageScrollWidth: document.documentElement.scrollWidth,
    }));
    expect(geometry.scrollWidth).toBeLessThanOrEqual(geometry.width + 1);
    expect(geometry.pageScrollWidth).toBeLessThanOrEqual(geometry.pageWidth + 1);
    expect(geometry.scrollHeight).toBeGreaterThan(geometry.height);
    await scroll.evaluate(element => { element.scrollTop = element.scrollHeight; });
    await expect(frame.locator(".perf-row").last()).toBeInViewport();
    expect(await frame.locator("header").boundingBox()).toEqual(header);
    expect(await frame.locator("footer").boundingBox()).toEqual(footer);
  });

  for (const scenario of ["empty", "partial", "partial-empty", "query-error"] as const) {
    test(`production Analysis retains its ${scenario} state at ${width}px`, async ({ page }) => {
      await page.setViewportSize({ width, height: 900 });
      await installFacades(
        page,
        surface,
        [],
        "ready",
        "ready",
        undefined,
        "ready",
        scenario);
      await openAnalysis(page);
      const frame = page.locator(".library-analysis-surface");
      if (scenario === "partial") {
        await expect(frame.locator(".perf-row")).toHaveCount(2);
        await expect(frame.locator("header")).toContainText("partial");
      } else {
        await expect(frame.locator("h2")).toHaveText(scenario === "empty"
          ? "No public allocation hot spots" : scenario === "partial-empty"
            ? "Analysis incomplete" : "Analysis failed");
        await expect(frame.locator(".perf-row")).toHaveCount(0);
      }
      if (scenario.startsWith("partial")) {
        await expect(frame).toContainText("A method body could not be analyzed.");
        await expect(frame).not.toContainText("No public allocation hot spots");
      }
      await expect(frame.locator("footer")).toBeInViewport();
    });
  }

  test(`production Analysis keeps Platform Library selection outside the scroller at ${width}px`, async ({ page }) => {
    await page.setViewportSize({ width, height: 900 });
    await openPlatform(page, { mismatchedFile: true });
    await page.getByTitle("Inspect System.Text.Json", { exact: true }).click();
    await page.locator('[data-library-lens="analysis"]').click();
    const frame = page.locator(".library-analysis-surface");
    await expect(frame.locator(".perf-row")).toHaveCount(2);
    const picker = frame.locator(".library-analysis-controls select");
    await expect(picker).toBeVisible();
    await expect(picker).toHaveValue("System.Text.Json");
    await expect(frame.locator("footer")).toContainText("PhysicalPayload.dll");
    await expect(page.locator("html"))
      .toHaveAttribute("data-platform-analysis-request", "System.Text.Json.dll:netcore.app");
    const controls = await frame.locator(".library-analysis-controls").boundingBox();
    const content = await frame.locator(".library-analysis-scroll").boundingBox();
    expect(controls!.y + controls!.height).toBeLessThanOrEqual(content!.y + 1);
  });
}

test("production Analysis rows open the exact ranked member", async ({ page }) => {
  await installFacades(page);
  await openAnalysis(page);
  await page.locator(".library-analysis-surface .perf-row").first().click();
  await expect(page.locator('[data-scope="member"]'))
    .toHaveAttribute("aria-selected", "true");
  await expect(page.locator("#inspector-panel")).toContainText("Runs the widget.");
});

test("production Analysis keeps deferred Library results out of the incoming analysis", async ({ page }) => {
  await installFacades(
    page,
    surface,
    [],
    "ready",
    "ready",
    undefined,
    "ready",
    "deferred");
  await openAnalysis(page);
  await expect(page.locator(".library-analysis-surface")).toContainText("Analyzing allocations");
  await expect(page.locator(".library-analysis-surface footer")).toContainText(core.asset);
  await page.evaluate(() => document.dispatchEvent(new Event("fixture-analysis-ready:asset:core")));
  await expect(page.locator(".library-analysis-scroll .perf-row")).toHaveCount(2);
  await page.locator('[data-subject-tab]:not([hidden])').first().press("Home");
  await page.locator('.library-list [data-lib-scope="asset:other"]').click();
  await page.locator('[data-library-lens="analysis"]')
    .evaluate((element: HTMLElement) => element.click());
  await expect(page.locator(".library-analysis-surface")).toContainText("Analyzing allocations");
  await expect(page.locator(".library-analysis-surface footer")).toContainText(other.asset);
  await expect(page.locator(".library-analysis-surface")).not.toContainText(core.name);
  await page.evaluate(() => document.dispatchEvent(new Event("fixture-analysis-ready:asset:other")));
  await expect(page.locator(".library-analysis-scroll .perf-name").first())
    .toContainText("Neighbor.Run");
});

for (const width of [1440, 390]) {
  test(`production Opportunities retains selected Library results at ${width}px`, async ({ page }, testInfo) => {
    await page.setViewportSize({ width, height: 900 });
    await installFacades(page);
    await openOpportunities(page);
    const frame = page.locator(".library-opportunities-surface");
    await expect(frame.locator(".opp-row")).toHaveCount(3);
    await expect(frame.locator("header")).toContainText("2 areas");
    await expect(frame.locator("header")).toContainText("3 suggestions");
    await expect(frame.locator("footer")).toContainText(core.asset);
    await expect(frame.locator("footer")).toContainText("Example.Core, Version=1.0.0.0");
    await expect(frame.locator("[data-opp-type]")).toHaveCount(3);
    await expect(frame.locator("[data-opp-package]")).toHaveCount(1);
    await expect(frame.locator("[data-opp-lookfor]")).toHaveCount(5);
    await expect(page.locator("html")).toHaveAttribute("data-opportunity-request", "asset:core");
    await expect(page.locator("#inspector-panel > .type-heading")).toHaveCount(0);
    const panelBox = await page.locator("#inspector-panel").boundingBox();
    const frameBox = await frame.boundingBox();
    const rowBox = await frame.locator(".opp-row").first().boundingBox();
    expect(Math.abs(frameBox!.height - panelBox!.height)).toBeLessThanOrEqual(2);
    expect(Math.abs(frameBox!.width - panelBox!.width)).toBeLessThanOrEqual(2);
    expect(Math.abs(rowBox!.width - frameBox!.width)).toBeLessThanOrEqual(2);
    expect(Math.abs(rowBox!.x - frameBox!.x)).toBeLessThanOrEqual(1);
    await page.screenshot({ path: testInfo.outputPath("opportunities.png") });
    if (width === 390) {
      const back = page.getByRole("button", { name: "Types", exact: true });
      await back.click();
      await expect(page.locator("#type-list")).toBeFocused();
      await page.getByRole("button", { name: "Show details", exact: true }).click();
      await expect(back).toBeFocused();
      await expect(frame).toBeVisible();
    }
  });

  test(`production Opportunities contains long fields and keeps its frame while scrolling at ${width}px`, async ({ page }) => {
    await page.setViewportSize({ width, height: 900 });
    const longCore = library(core.id, "Example." + "LongLibraryName".repeat(25), 1);
    await installFacades(page, {
      ...surface, assemblies: [longCore], types: [type("Example.Widget", longCore)], totalMembers: 1,
    }, [], "ready", "ready", undefined, "long");
    await openOpportunities(page);
    const frame = page.locator(".library-opportunities-surface");
    await expect(frame.locator(".opp-row")).toHaveCount(81);
    const header = await frame.locator("header").boundingBox();
    const footer = await frame.locator("footer").boundingBox();
    const scroll = frame.locator(".library-opportunities-scroll");
    const geometry = await scroll.evaluate(element => ({
      width: element.clientWidth, scrollWidth: element.scrollWidth,
      height: element.clientHeight, scrollHeight: element.scrollHeight,
      pageWidth: document.documentElement.clientWidth,
      pageScrollWidth: document.documentElement.scrollWidth,
    }));
    expect(geometry.scrollWidth).toBeLessThanOrEqual(geometry.width + 1);
    expect(geometry.pageScrollWidth).toBeLessThanOrEqual(geometry.pageWidth + 1);
    expect(geometry.scrollHeight).toBeGreaterThan(geometry.height);
    await scroll.evaluate(element => { element.scrollTop = element.scrollHeight; });
    await expect(frame.locator(".opp-row").last()).toBeInViewport();
    expect(await frame.locator("header").boundingBox()).toEqual(header);
    expect(await frame.locator("footer").boundingBox()).toEqual(footer);
  });

  for (const scenario of ["empty", "partial", "partial-empty", "query-error"] as const) {
    test(`production Opportunities retains its ${scenario} state at ${width}px`, async ({ page }) => {
      await page.setViewportSize({ width, height: 900 });
      await installFacades(page, surface, [], "ready", "ready", undefined, scenario);
      await openOpportunities(page);
      const frame = page.locator(".library-opportunities-surface");
      if (scenario === "partial") {
        await expect(frame.locator(".opp-row")).toHaveCount(3);
        await expect(frame.locator("header")).toContainText("partial");
      } else {
        await expect(frame.locator("h2")).toHaveText(scenario === "empty"
          ? "No integration opportunities" : scenario === "partial-empty"
            ? "Opportunity scan incomplete" : "Opportunity scan failed");
        await expect(frame.locator(".opp-row")).toHaveCount(0);
      }
      if (scenario.startsWith("partial")) {
        await expect(frame).toContainText("A library participant could not be inspected.");
        await expect(frame).not.toContainText("No integration opportunities");
      }
      await expect(frame.locator("footer")).toBeInViewport();
    });
  }

  test(`production Opportunities keeps Platform Library selection outside the scroller at ${width}px`, async ({ page }) => {
    await page.setViewportSize({ width, height: 900 });
    await openPlatform(page, { mismatchedFile: true });
    await page.getByTitle("Inspect System.Facade", { exact: true }).click();
    await page.locator('[data-library-lens="opportunities"]').click();
    const frame = page.locator(".library-opportunities-surface");
    await expect(frame.locator(".opp-row")).toHaveCount(3);
    const picker = frame.locator(".library-opportunities-controls select");
    await expect(picker).toBeVisible();
    await expect(picker).toHaveValue("System.Facade");
    await picker.selectOption("System.Text.Json");
    await expect(frame.locator(".opp-type-ns").nth(1)).toContainText("System.Text.Json");
    await expect(frame.locator("footer")).toContainText("PhysicalPayload.dll");
    await expect(page.locator("html")).toHaveAttribute(
      "data-platform-library-request",
      JSON.stringify([
        "net11.0",
        platformVersion,
        "System.Text.Json.dll",
        "netcore.app",
        "PhysicalPayload.dll",
      ]),
    );
    await expect(page.locator("html"))
      .toHaveAttribute("data-platform-opportunity-request", "System.Text.Json.dll:netcore.app");
    const header = await frame.locator("header").boundingBox();
    const content = await frame.locator(".library-opportunities-scroll").boundingBox();
    expect(header!.y + header!.height).toBeLessThanOrEqual(content!.y + 1);
  });
}

test("stale Platform Opportunities acquisition cannot replace a newer family selection", async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  await openPlatform(page, {
    duplicateLibrary: true,
    libraryPending: true,
    libraryPendingPack: "aspnetcore.app",
  });
  await page.getByRole(
    "button",
    { name: /System.Text.Json Implementation netcore.app/ },
  ).click();
  await page.locator('[data-library-lens="opportunities"]').click();
  const picker = page.locator(
    ".library-opportunities-controls .platform-library-select",
  );
  await picker
    .locator('option[data-pack="aspnetcore.app"]')
    .first()
    .evaluate(option => {
      if (!(option instanceof HTMLOptionElement))
        throw new Error("Expected a Platform Library option.");
      const select = option.closest("select");
      if (!(select instanceof HTMLSelectElement))
        throw new Error("Expected a Platform Library picker.");
      for (const candidate of select.options) candidate.selected = false;
      option.selected = true;
      select.dispatchEvent(new Event("change", { bubbles: true }));
    });
  await expect(page.locator("html")).toHaveAttribute(
    "data-platform-library-request",
    JSON.stringify([
      "net11.0",
      platformVersion,
      "System.Text.Json.dll",
      "aspnetcore.app",
      "System.Text.Json.dll",
    ]),
  );

  await page.getByRole(
    "button",
    { name: "Search types, members, packages", exact: true },
  ).click();
  await page.locator("#spotlight-input").fill("Widget");
  await page.locator('[data-sl-type*="Example.Widget"]:not([data-sl-member])').first().click();
  await expect(page.locator('[data-scope="type"]')).toHaveAttribute(
    "aria-selected",
    "true",
  );

  await page.evaluate(() =>
    document.dispatchEvent(new Event("finish-platform-library")));
  await page.locator('[data-scope="library"]').click();
  await page.locator('[data-library-lens="metadata"]').click();
  await expect(page.locator("html")).toHaveAttribute(
    "data-platform-metadata-request",
    JSON.stringify([
      "net11.0",
      platformVersion,
      "System.Text.Json.dll",
      "netcore.app",
    ]),
  );
});

test("production Opportunities keeps deferred Library results out of the incoming scan", async ({ page }) => {
  await installFacades(page, surface, [], "ready", "ready", undefined, "deferred");
  await openOpportunities(page);
  await expect(page.locator(".library-opportunities-surface")).toContainText("Scanning opportunities");
  await expect(page.locator(".library-opportunities-surface footer")).toContainText(core.asset);
  await page.evaluate(() => document.dispatchEvent(new Event("fixture-opportunities-ready:asset:core")));
  await expect(page.locator(".library-opportunities-scroll .opp-row")).toHaveCount(3);
  await page.locator('[data-subject-tab]:not([hidden])').first().press("Home");
  await page.locator('.library-list [data-lib-scope="asset:other"]').click();
  await page.locator('[data-library-lens="overview"]').press("ArrowRight");
  await page.keyboard.press("ArrowRight");
  await page.keyboard.press("ArrowRight");
  await page.keyboard.press("Enter");
  await expect(page.locator(".library-opportunities-surface")).toContainText("Scanning opportunities");
  await expect(page.locator(".library-opportunities-surface footer")).toContainText(other.asset);
  await expect(page.locator(".library-opportunities-surface")).not.toContainText(core.name);
  await page.evaluate(() => document.dispatchEvent(new Event("fixture-opportunities-ready:asset:other")));
  await expect(page.locator(".library-opportunities-scroll .opp-type-ns").nth(1)).toContainText(other.name);
});

for (const width of [1440, 390]) {
  test(`production Integrations retains selected Library results at ${width}px`, async ({ page }, testInfo) => {
    await page.setViewportSize({ width, height: 900 });
    await installFacades(page);
    await openIntegrations(page);
    await expect(page.locator("#inspector-panel .signal-row")).toHaveCount(3);
    await expect(page.locator("#inspector-panel .signal-name").first()).toHaveText("WidgetService");
    await expect(page.locator("#inspector-panel")).toContainText("Dependency Injection");
    await expect(page.locator("#inspector-panel")).toContainText("Logging");
    await expect(page.locator("html")).toHaveAttribute("data-integration-request", "asset:core");
    const frame = page.locator(".library-integrations-surface");
    await expect(frame.locator("header")).toContainText("2 categories");
    await expect(frame.locator("header")).toContainText("3 signals");
    await expect(frame.locator("footer")).toContainText(core.asset);
    await expect(frame.locator("footer")).toContainText("Example.Core, Version=1.0.0.0");
    await expect(page.locator("#inspector-panel > .type-heading")).toHaveCount(0);
    const panelBox = await page.locator("#inspector-panel").boundingBox();
    const frameBox = await frame.boundingBox();
    const rowBox = await frame.locator(".signal-row").first().boundingBox();
    expect(Math.abs(frameBox!.height - panelBox!.height)).toBeLessThanOrEqual(2);
    expect(Math.abs(frameBox!.width - panelBox!.width)).toBeLessThanOrEqual(2);
    expect(Math.abs(rowBox!.width - frameBox!.width)).toBeLessThanOrEqual(2);
    expect(Math.abs(rowBox!.x - frameBox!.x)).toBeLessThanOrEqual(1);
    await page.screenshot({ path: testInfo.outputPath("integrations.png") });
    if (width === 390) {
      const back = page.getByRole("button", { name: "Types", exact: true });
      await back.click();
      await expect(page.locator("#type-list")).toBeFocused();
      await page.getByRole("button", { name: "Show details", exact: true }).click();
      await expect(back).toBeFocused();
      await expect(frame).toBeVisible();
    }
  });

  test(`production Integrations contains long fields and keeps its frame while scrolling at ${width}px`, async ({ page }) => {
    await page.setViewportSize({ width, height: 900 });
    const longCore = library(core.id, "Example." + "LongLibraryName".repeat(25), 1);
    await installFacades(page, {
      ...surface, assemblies: [longCore], types: [type("Example.Widget", longCore)], totalMembers: 1,
    }, [], "ready", "long");
    await openIntegrations(page);
    const frame = page.locator(".library-integrations-surface");
    await expect(frame.locator(".signal-row")).toHaveCount(81);
    const header = await frame.locator("header").boundingBox();
    const footer = await frame.locator("footer").boundingBox();
    const scroll = frame.locator(".library-integrations-scroll");
    const geometry = await scroll.evaluate(element => ({
      width: element.clientWidth, scrollWidth: element.scrollWidth,
      height: element.clientHeight, scrollHeight: element.scrollHeight,
      pageWidth: document.documentElement.clientWidth, pageScrollWidth: document.documentElement.scrollWidth,
    }));
    expect(geometry.scrollWidth).toBeLessThanOrEqual(geometry.width + 1);
    expect(geometry.pageScrollWidth).toBeLessThanOrEqual(geometry.pageWidth + 1);
    expect(geometry.scrollHeight).toBeGreaterThan(geometry.height);
    await scroll.evaluate(element => { element.scrollTop = element.scrollHeight; });
    await expect(frame.locator(".signal-row").last()).toBeInViewport();
    expect(await frame.locator("header").boundingBox()).toEqual(header);
    expect(await frame.locator("footer").boundingBox()).toEqual(footer);
  });

  for (const scenario of ["empty", "partial", "partial-empty", "query-error"] as const) {
    test(`production Integrations retains its ${scenario} state at ${width}px`, async ({ page }) => {
      await page.setViewportSize({ width, height: 900 });
      await installFacades(page, surface, [], "ready", scenario);
      await openIntegrations(page);
      const frame = page.locator(".library-integrations-surface");
      if (scenario === "partial") {
        await expect(frame.locator(".signal-row")).toHaveCount(3);
        await expect(frame.locator("header")).toContainText("partial");
      } else {
        await expect(frame.locator("h2")).toHaveText(scenario === "empty"
          ? "No ecosystem integrations detected" : scenario === "partial-empty"
            ? "Integration scan incomplete" : "Integration scan failed");
        await expect(frame.locator(".signal-row")).toHaveCount(0);
      }
      if (scenario.startsWith("partial")) {
        await expect(frame).toContainText("A library participant could not be inspected.");
        await expect(frame).not.toContainText("No ecosystem integrations detected");
      }
      await expect(frame.locator("footer")).toBeInViewport();
    });
  }

  test(`production Integrations uses Platform navigation without a second library picker at ${width}px`, async ({ page }) => {
    await page.setViewportSize({ width, height: 900 });
    await openPlatform(page);
    await page.getByTitle("Inspect System.Text.Json", { exact: true }).click();
    await page.locator('[data-library-lens="overview"]').press("ArrowRight");
    await page.keyboard.press("Enter");
    const frame = page.locator(".library-integrations-surface");
    await expect(frame.locator(".signal-row")).toHaveCount(3);
    await expect(frame.locator(".library-integrations-controls")).toHaveCount(0);
    await expect(frame.locator(".signal-ns").first()).toContainText("System.Text.Json");
    await page.locator('[data-scope="library"]').press("Home");
    await page.getByTitle("Inspect System.Facade", { exact: true }).click();
    await page.locator('[data-library-lens="overview"]').press("ArrowRight");
    await page.keyboard.press("Enter");
    await expect(frame.locator(".signal-ns").first()).toContainText("System.Facade");
    await expect(frame.locator("footer")).toContainText("System.Facade.dll");
    await expect(page.locator("html")).toHaveAttribute("data-platform-integration-request", "System.Facade.dll:netcore.app");
  });
}

test("production Integrations keeps deferred Library results out of the incoming scan", async ({ page }) => {
  await installFacades(page, surface, [], "ready", "deferred");
  await openIntegrations(page);
  await expect(page.locator(".library-integrations-surface")).toContainText("Scanning integrations");
  await expect(page.locator(".library-integrations-surface footer")).toContainText(core.asset);
  await page.evaluate(() => document.dispatchEvent(new Event("fixture-integrations-ready:asset:core")));
  await expect(page.locator(".library-integrations-scroll .signal-row")).toHaveCount(3);
  await page.locator('[data-subject-tab]:not([hidden])').first().press("Home");
  await page.locator('.library-list [data-lib-scope="asset:other"]').click();
  await page.locator('[data-library-lens="overview"]').press("ArrowRight");
  await page.keyboard.press("ArrowRight");
  await page.keyboard.press("Enter");
  await expect(page.locator(".library-integrations-surface")).toContainText("Scanning integrations");
  await expect(page.locator(".library-integrations-surface footer")).toContainText(other.asset);
  await expect(page.locator(".library-integrations-surface")).not.toContainText(core.name);
  await page.evaluate(() => document.dispatchEvent(new Event("fixture-integrations-ready:asset:other")));
  await expect(page.locator(".library-integrations-scroll .signal-ns").first()).toContainText(other.name);
});

async function openReferences(page: Page) {
  await page.goto(root);
  await page.locator('.library-list [data-lib-scope="asset:core"]').click();
  await page.locator('[data-library-lens="overview"]').press("ArrowRight");
  await page.keyboard.press("Enter");
  await expect(page.locator('[data-library-lens="references"]')).toHaveAttribute("aria-selected", "true");
}

for (const width of [1440, 390]) {
  test(`production References fills the pane and retains context at ${width}px`, async ({ page }, testInfo) => {
    await page.setViewportSize({ width, height: 900 });
    await installFacades(page);
    await openReferences(page);
    const frame = page.locator(".library-references-surface");
    await expect(frame.locator(".dep-list li")).toHaveCount(1);
    await expect(frame.locator("header")).toContainText("1 direct reference");
    await expect(frame.locator("footer")).toContainText(core.asset);
    await expect(frame.locator("footer")).toContainText("Example.Core, Version=1.0.0.0");
    await expect(frame.locator("footer")).toContainText("Example.Package@1.0.0");
    await expect(page.locator("#inspector-panel > .type-heading")).toHaveCount(0);
    await expect(frame.locator("h2")).toHaveCount(0);
    const panelBox = await page.locator("#inspector-panel").boundingBox();
    const frameBox = await frame.boundingBox();
    expect(panelBox).not.toBeNull();
    expect(frameBox).not.toBeNull();
    expect(Math.abs(frameBox!.width - panelBox!.width)).toBeLessThanOrEqual(2);
    expect(Math.abs(frameBox!.height - panelBox!.height)).toBeLessThanOrEqual(2);
    const listBox = await frame.locator(".dep-list").boundingBox();
    expect(Math.abs(listBox!.x - frameBox!.x)).toBeLessThanOrEqual(1);
    expect(Math.abs(listBox!.width - frameBox!.width)).toBeLessThanOrEqual(2);
    await page.screenshot({ path: testInfo.outputPath("references.png") });
    if (width === 390) {
      const back = page.getByRole("button", { name: "Types", exact: true });
      await expect(back).toBeVisible();
      await back.click();
      await expect(page.locator("#type-list")).toBeFocused();
      await page.getByRole("button", { name: "Show details", exact: true }).click();
      await expect(back).toBeFocused();
      await expect(frame).toBeVisible();
    }
  });

  test(`production References contains long fields and scrolls only its list at ${width}px`, async ({ page }) => {
    await page.setViewportSize({ width, height: 900 });
    const longCore = library(core.id, "Example." + "LongLibraryName".repeat(25), 1);
    await installFacades(page, {
      ...surface, assemblies: [longCore], types: [type("Example.Widget", longCore)], totalMembers: 1,
    }, [], "long");
    await openReferences(page);
    const frame = page.locator(".library-references-surface");
    await expect(frame.locator(".dep-list li")).toHaveCount(80);
    await expect(frame.locator("header")).toContainText("80 direct references");
    await expect(frame.locator("footer span").first()).toHaveAttribute("title", new RegExp(longCore.name));
    const headerBox = await frame.locator("header").boundingBox();
    const footerBox = await frame.locator("footer").boundingBox();
    const scroller = frame.locator(".library-references-scroll");
    const geometry = await scroller.evaluate(element => ({
      width: element.clientWidth, scrollWidth: element.scrollWidth,
      height: element.clientHeight, scrollHeight: element.scrollHeight,
      pageWidth: document.documentElement.clientWidth,
      pageScrollWidth: document.documentElement.scrollWidth,
    }));
    expect(geometry.scrollWidth).toBeLessThanOrEqual(geometry.width + 1);
    expect(geometry.pageScrollWidth).toBeLessThanOrEqual(geometry.pageWidth + 1);
    expect(geometry.scrollHeight).toBeGreaterThan(geometry.height);
    await scroller.evaluate(element => { element.scrollTop = element.scrollHeight; });
    await expect(frame.locator(".dep-list li").last()).toBeInViewport();
    expect(await frame.locator("header").boundingBox()).toEqual(headerBox);
    expect(await frame.locator("footer").boundingBox()).toEqual(footerBox);
  });

  for (const scenario of ["empty", "query-error", "inspection-error"] as const) {
    test(`production References preserves its ${scenario} frame at ${width}px`, async ({ page }) => {
      await page.setViewportSize({ width, height: 900 });
      await installFacades(page, surface, [], scenario);
      await openReferences(page);
      const frame = page.locator(".library-references-surface");
      await expect(frame.locator("h2")).toHaveText(scenario === "empty"
        ? "No direct references" : scenario === "query-error"
          ? "Reference query failed" : "Reference inspection failed");
      await expect(frame.locator("footer")).toBeInViewport();
      await expect(frame.locator("footer")).toContainText(core.asset);
      await expect(frame.locator(".dep-list")).toHaveCount(0);
      if (scenario !== "empty") {
        await expect(frame).not.toContainText("0 direct references");
        await expect(frame).toContainText(scenario === "query-error"
          ? "Reference query unavailable." : "Cannot decode AssemblyRef.");
      }
    });
  }
}

test("production References retains a loading frame and does not show a previous Library's rows", async ({ page }) => {
  await installFacades(page, surface, [], "deferred");
  await openReferences(page);
  await expect(page.locator(".library-references-surface")).toContainText("Reading direct AssemblyRef rows");
  await expect(page.locator(".library-references-surface footer")).toContainText(core.asset);
  await page.evaluate(() => document.dispatchEvent(new Event("fixture-references-ready:asset:core")));
  await expect(page.locator(".library-references-scroll")).toContainText("Example.Core.Dependency");
  await page.locator('[data-subject-tab]:not([hidden])').first().press("Home");
  await page.locator('.library-list [data-lib-scope="asset:other"]').click();
  await page.locator('[data-library-lens="overview"]').press("ArrowRight");
  await page.keyboard.press("Enter");
  await expect(page.locator(".library-references-surface")).toContainText("Reading direct AssemblyRef rows");
  await expect(page.locator(".library-references-surface footer")).toContainText(other.asset);
  await expect(page.locator(".library-references-surface")).not.toContainText("Example.Core");
  await page.evaluate(() => document.dispatchEvent(new Event("fixture-references-ready:asset:other")));
  await expect(page.locator(".library-references-scroll")).toContainText("Example.Other.Dependency");
});

test("Libraries navigation exposes the complete truncated name on hover", async ({ page }) => {
  const longLibrary = {
    ...core,
    name: "Example.Serialization.Providers.With.A.Very.Long.Library.Name",
  };
  await installFacades(page, {
    ...surface,
    assemblies: [longLibrary, other, empty],
    types: [type("Example.Widget", longLibrary), type("Example.Neighbor", other)],
  });
  await page.goto(root);
  const row = page.locator('.library-subject-list [data-lib-scope="asset:core"]');
  await expect(row).toBeVisible();
  const name = row.locator(".type-name");
  await expect(name).toHaveText(longLibrary.name);
  expect(await name.evaluate(element => element.scrollWidth > element.clientWidth)).toBe(true);
  const location = page.url();
  await row.hover();
  await expect(row).toHaveAttribute("title", `Inspect ${longLibrary.name}`);
  await expect(page.locator('.library-list [data-lib-scope="asset:core"]'))
    .toHaveAttribute("title", `Inspect ${longLibrary.name}`);
  await expect(page).toHaveURL(location);
});

for (const [width, selectedLibrary, activation] of [
  [900, core, "click"],
  [480, core, "keyboard"],
  [900, empty, "keyboard"],
  [480, empty, "click"],
] as const) {
  test(`Library back returns ${selectedLibrary.name} to Package at ${width}px with ${activation}`, async ({ page }) => {
    await page.setViewportSize({ width, height: 900 });
    await installFacades(page);
    await page.goto(root);
    await page.locator(`.library-list [data-lib-scope="${selectedLibrary.id}"]`).click();
    await expect(page.locator('[data-scope="library"]')).toHaveAttribute("aria-selected", "true");
    const libraryLocation = page.url();
    if (width === 480) {
      await page.getByRole("button", { name: "Types", exact: true }).click();
    }
    const back = page.locator(".type-browser .nav-back-row");
    await expect(back).toHaveAccessibleName(`${selectedLibrary.name}: Back to package`);
    if (activation === "click") {
      await back.click();
    } else {
      await back.focus();
      await page.keyboard.press("Enter");
    }
    await expect(page.locator('[data-scope="package"]')).toHaveAttribute("aria-selected", "true");
    await expect(page.locator("#inspector-panel")).toBeVisible();
    await expect(page.locator(".library-list [data-lib-scope]")).toHaveCount(3);
    await expect(page.locator(width === 480
      ? "#content-navigation-toggle" : ".library-subject-list")).toBeFocused();
    const packageLocation = page.url();
    expect(packageLocation).not.toBe(libraryLocation);

    await page.getByRole("button", { name: "Application menu", exact: true }).press("Alt+ArrowLeft");
    await expect(page).toHaveURL(libraryLocation);
    await expect(page.locator('[data-scope="library"]')).toHaveAttribute("aria-selected", "true");
    await expect(page.locator("#inspector-panel h1")).toHaveText(selectedLibrary.name);
    await expect(page.locator("#type-list [data-type]")).toHaveCount(selectedLibrary.publicTypes);
    await expect(page.locator(".type-browser .nav-back-row")).toHaveAttribute("title", "Back to package");

    await page.getByRole("button", { name: "Application menu", exact: true }).press("Alt+ArrowRight");
    await expect(page).toHaveURL(packageLocation);
    await expect(page.locator('[data-scope="package"]')).toHaveAttribute("aria-selected", "true");
    await page.reload();
    await expect(page.locator('[data-scope="package"]')).toHaveAttribute("aria-selected", "true");
    await expect(page.locator(".library-list [data-lib-scope]")).toHaveCount(3);
  });
}

for (const [width, activation] of [[900, "click"], [480, "keyboard"]] as const) {
  test(`Type back reveals its Library inspector at ${width}px with ${activation}`, async ({ page }) => {
    await page.setViewportSize({ width, height: 900 });
    await installFacades(page);
    await page.goto(root);
    await page.locator('.library-list [data-lib-scope="asset:other"]').click();
    await page.locator('[data-library-lens="overview"]').press("ArrowRight");
    await page.keyboard.press("Enter");
    await expect(page.locator("#inspector-panel")).toContainText("Example.Other.Dependency");
    const libraryLocation = page.url();
    if (width === 480) {
      await page.getByRole("button", { name: "Types", exact: true }).click();
    }
    await page.locator('#type-list [data-type]').click();
    await expect(page.locator('[data-scope="type"]')).toHaveAttribute("aria-selected", "true");
    const typeLocation = page.url();
    if (width === 480) {
      await page.getByRole("button", { name: "Types", exact: true }).click();
    }
    const back = page.locator(".type-browser .nav-back-row");
    await expect(back).toHaveAttribute("title", "Back to library");
    await expect(back).toHaveAccessibleName("Example.Other: Back to library");
    if (activation === "click") {
      await back.click();
    } else {
      await back.focus();
      await page.keyboard.press("Enter");
    }
    await expect(page.locator('[data-scope="library"]')).toHaveAttribute("aria-selected", "true");
    await expect(page.locator("#inspector-panel")).toBeVisible();
    await expect(page.locator("#inspector-panel")).toContainText("Example.Other.Dependency");
    await expect(page.locator(width === 480
      ? "#content-navigation-toggle" : "#type-list")).toBeFocused();
    await expect(page.locator("#type-list [data-type]")).toHaveCount(1);
    await expect(page.locator("#type-list")).toContainText("Neighbor");
    await expect(page.locator("#type-list")).not.toContainText("Widget");
    await expect(page).toHaveURL(libraryLocation);

    await page.getByRole("button", { name: "Application menu", exact: true }).press("Alt+ArrowLeft");
    await expect(page).toHaveURL(typeLocation);
    await expect(page.locator('[data-scope="type"]')).toHaveAttribute("aria-selected", "true");
    await page.getByRole("button", { name: "Application menu", exact: true }).press("Alt+ArrowRight");
    await expect(page).toHaveURL(libraryLocation);
    await expect(page.locator('[data-library-lens="references"]')).toHaveAttribute("aria-selected", "true");
  });
}

for (const width of [1440, 800, 390]) {
  test(`production Package Overview fills its frame and opens Library at ${width}px`, async ({ page }) => {
    await page.setViewportSize({ width, height: 900 });
    await installFacades(page);
    await page.goto(root);
    const overview = page.locator(".package-overview-surface");
    await expect(overview).toBeVisible();
    expect(await overview.boundingBox()).toEqual(
      await page.locator("#inspector-panel").boundingBox());
    await expect(page.locator(".type-heading, .package-coordinate-editor")).toHaveCount(0);
    await expect(overview.getByRole("heading", { level: 1 })).toHaveText("Example.Package");
    expect((await overview.locator(".overview-identity h1").boundingBox())!.width).toBeGreaterThan(100);
    await expect(overview.locator(".overview-identity [data-package-icon]")).toBeVisible();
    const packageIconSource = await overview.locator("[data-package-icon]").getAttribute("src");
    await expect(page.locator(".overview-surface-head p")).toHaveText("2 types · 2 members");
    await expect(page.locator(".overview-surface-footer span")).toHaveText([
      "Example.Package@1.0.0", "net10.0",
    ]);
    await expect(overview.locator(".library-row")).toHaveCount(3);
    await expect(overview.locator('[data-lib-scope="asset:empty"]')).toContainText("0 types");
    await expect(overview.locator(".comparison-target-row")).toHaveCount(2);
    const diffTarget = overview.locator("#package-diff-target");
    await expect(diffTarget.locator("option:checked")).toHaveText("Automatic: 0.9.0");
    await expect(overview.locator(".comparison-target-policy"))
      .toHaveText("Session only. Choosing a target does not run a comparison or change shared links.");
    if (width === 1440) {
      expect((await diffTarget.boundingBox())!.width).toBeGreaterThan(500);
      expect(await diffTarget.evaluate(select => {
        if (!(select instanceof HTMLSelectElement)) return false;
        const canvas = document.createElement("canvas");
        const context = canvas.getContext("2d");
        if (!context) return false;
        context.font = getComputedStyle(select).font;
        return context.measureText(select.selectedOptions[0]?.text ?? "").width + 48
          <= select.clientWidth;
      })).toBe(true);
    } else {
      const row = await overview.locator(".comparison-target-row").first().boundingBox();
      const heading = await overview.locator(".comparison-target-heading").first().boundingBox();
      const selection = await overview.locator(".comparison-target-selection").first().boundingBox();
      expect(row).not.toBeNull();
      expect(heading).not.toBeNull();
      expect(selection).not.toBeNull();
      expect(Math.abs(selection!.x - heading!.x)).toBeLessThan(1);
      expect(selection!.y).toBeGreaterThan(heading!.y + heading!.height);
    }
    expect(await overview.locator(".overview-scroll").evaluate(scroll =>
      scroll.scrollWidth - scroll.clientWidth)).toBe(0);
    expect(await page.evaluate(() =>
      document.documentElement.scrollWidth - document.documentElement.clientWidth)).toBe(0);
    if (width === 390) {
      await page.getByRole("button", { name: "Libraries", exact: true }).click();
      await expect(page.locator(".library-subject-list")).toBeFocused();
      await page.getByRole("button", { name: "Show details", exact: true }).click();
    }
    await overview.locator('[data-lib-scope="asset:other"]').click();
    await expect(page.locator('[data-scope="library"]')).toHaveAttribute("aria-selected", "true");
    await expect(page.locator("#inspector-panel h1")).toHaveText("Example.Other");
    const libraryOverview = page.locator(".library-overview-surface");
    expect(await libraryOverview.boundingBox()).toEqual(
      await page.locator("#inspector-panel").boundingBox());
    expect((await libraryOverview.locator(".overview-identity h1").boundingBox())!.width).toBeGreaterThan(100);
    await expect(libraryOverview.locator(".overview-identity [data-package-icon]")).toBeVisible();
    expect(await libraryOverview.locator("[data-package-icon]").getAttribute("src")).toBe(packageIconSource);
    await expect(libraryOverview.locator(".overview-identity-detail")).toHaveText([
      "lib/net10.0/Example.Other.dll",
      "Example.Other, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null",
    ]);
    await expect(libraryOverview.locator(".overview-surface-head p")).toHaveText("1 type · 1 member");
    await expect(libraryOverview.locator(".overview-controls")).toHaveCount(0);
    await expect(overview).toHaveCount(0);
    await page.locator('[data-subject-tab]:not([hidden])').first().press("Home");
    await expect(overview).toBeVisible();
    await overview.locator('[data-lib-scope="asset:empty"]').click();
    await expect(libraryOverview.getByRole("heading", { level: 1 })).toHaveText("Example.Empty");
    await expect(libraryOverview.locator(".overview-surface-head p")).toHaveText("0 types · 0 members");
    await expect(libraryOverview.locator(".overview-surface-footer")).toBeVisible();
  });
}

for (const subject of ["Package", "Library"]) {
  test(`both package icons retain the existing image fallback on ${subject} Overview`, async ({ page }) => {
    await installFacades(page);
    await page.goto(root);
    if (subject === "Library") {
      await page.locator('.package-overview-surface [data-lib-scope="asset:core"]').click();
      await expect(page.locator(".library-overview-surface")).toBeVisible();
    }
    const icons = page.locator("[data-package-icon]");
    await expect(icons).toHaveCount(2);
    await icons.evaluateAll(images => {
      for (const image of images) {
        image.setAttribute("src", "data:image/png;base64,broken");
        image.dispatchEvent(new Event("error"));
      }
    });
    for (const image of await icons.all()) {
      await expect(image).toHaveAttribute("src", "https://nuget.org/Content/gallery/img/default-package-icon-256x256.png");
    }
  });
}

for (const [selectedLibrary, activation] of [[core, "click"], [empty, "keyboard"]] as const) {
  test(`narrow Library navigation returns to ${selectedLibrary.name} details with ${activation}`, async ({ page }) => {
    await page.setViewportSize({ width: 480, height: 900 });
    await installFacades(page);
    await page.goto(root);
    await expect(page.locator(".library-list")).toBeVisible();

    const libraries = page.getByRole("button", { name: "Libraries", exact: true });
    await libraries.click();
    await expect(page.locator(".library-subject-list")).toBeFocused();
    const location = page.url();
    const historyLength = await page.evaluate(() => history.length);
    await page.getByRole("button", { name: "Show details", exact: true }).click();
    await expect(page.locator("#inspector-panel")).toBeVisible();
    await expect(libraries).toBeFocused();
    expect(page.url()).toBe(location);
    expect(await page.evaluate(() => history.length)).toBe(historyLength);

    await libraries.click();
    const row = page.locator(`.library-subject-list [data-lib-scope="${selectedLibrary.id}"]`);
    if (activation === "click") {
      await row.click();
    } else {
      await row.focus();
      await page.keyboard.press("Enter");
    }
    await expect(page.locator('[data-scope="library"]')).toHaveAttribute("aria-selected", "true");
    await expect(page.locator(".content-frame")).toHaveAttribute("data-content-pane", "detail");
    await expect(page.locator("#inspector-panel")).toBeVisible();
    await expect(page.locator("#inspector-panel h1")).toHaveText(selectedLibrary.name);
    await expect(page.locator("#content-navigation-toggle")).toBeFocused();

    await page.getByRole("button", { name: "Types", exact: true }).click();
    await expect(page.locator("#type-list")).toBeFocused();
    await expect(page.locator("#type-list [data-type]")).toHaveCount(selectedLibrary.publicTypes);
    await page.getByRole("button", { name: "Show details", exact: true }).click();
    await expect(page.locator("#inspector-panel")).toBeVisible();
    await expect(page.locator("#content-navigation-toggle")).toBeFocused();
  });
}

test("production navigation separates Package, Library, Type and Member", async ({ page }) => {
  const errors: string[] = [];
  page.on("pageerror", error => errors.push(error.message));
  await installFacades(page);
  await page.goto(root);
  await expect(page.locator('[data-package-lens="dependencies"]')).toBeVisible();
  await expect(page.locator('[data-library-lens]')).toHaveCount(0);
  await expect(page.locator(".library-list [data-lib-scope]")).toHaveCount(3);

  await page.locator('.library-list [data-lib-scope="asset:other"]').click();
  await expect(page.locator('[data-scope="library"]')).toHaveAttribute("aria-selected", "true");
  await expect(page.locator("#inspector-panel h1")).toHaveText("Example.Other");
  await expect(page.locator("#type-list")).toContainText("Neighbor");
  await expect(page.locator("#type-list")).not.toContainText("Widget");
  await page.locator('[data-library-lens="overview"]').press("ArrowRight");
  await page.keyboard.press("Enter");
  await expect(page.locator("#inspector-panel")).toContainText("Example.Other.Dependency");
  await expect(page.locator("html")).toHaveAttribute("data-reference-request", "asset:other");

  await page.locator('[data-subject-tab]:not([hidden])').first().press("Home");
  await expect(page.locator('[data-scope="package"]')).toHaveAttribute("aria-selected", "true");
  await page.locator('.library-list [data-lib-scope="asset:core"]').click();
  await page.locator('#type-list [data-type]').click();
  await expect(page.locator('[data-scope="type"]')).toHaveAttribute("aria-selected", "true");
  await page.locator('[data-subject-tab]:not([hidden])').first().press("End");
  await expect(page.locator('[data-scope="member"]')).toHaveAttribute("aria-selected", "true");
  await expect(page.locator(".inspected-target")).toContainText("Example.Core");
  await expect(page.locator(".inspected-target")).toContainText("Example.Widget");
  await expect(page.locator(".inspected-target")).toContainText("Run");
  expect(errors).toEqual([]);
});

test("direct Library subject entry scopes Types before and after refresh", async ({ page }) => {
  await installFacades(page);
  await page.goto(root);
  await page.getByRole("tab", { name: "Library", exact: true }).click();
  await expect(page.locator("#inspector-panel h1")).toHaveText("Example.Core");
  await expect(page.locator("#type-list [data-type]")).toHaveCount(1);
  await expect(page.locator("#type-list")).toContainText("Widget");
  await expect(page.locator("#type-list")).not.toContainText("Neighbor");
  await page.reload();
  await expect(page.locator("#inspector-panel h1")).toHaveText("Example.Core");
  await expect(page.locator("#type-list [data-type]")).toHaveCount(1);
  await expect(page.locator("#type-list")).toContainText("Widget");
});

test("returning to Library retains its inspector and selected Type context", async ({ page }) => {
  await installFacades(page, {
    ...surface,
    assemblies: [{ ...core, publicTypes: 2, publicMembers: 2 }, other, empty],
    types: [...surface.types, type("Example.SecondWidget", core)],
    accessibility: surface.accessibility.map(bucket => ({ ...bucket, count: 3 })),
    totalMembers: 3,
  });
  await page.goto(root);
  await page.locator('.library-list [data-lib-scope="asset:core"]').click();
  await page.locator('[data-library-lens="overview"]').press("ArrowRight");
  await page.keyboard.press("Enter");
  await expect(page.locator("#inspector-panel")).toContainText("Example.Core.Dependency");
  await page.locator('#type-list [data-type="asset:core:Example.SecondWidget"]').click();
  await page.locator('[data-subject-tab]:not([hidden])').first().press("End");
  await expect(page.locator('[data-scope="member"]')).toHaveAttribute("aria-selected", "true");
  await expect(page.locator(".inspected-target")).toContainText("Example.SecondWidget");
  await page.getByRole("tab", { name: "Member", exact: true }).press("ArrowLeft");
  await expect(page.locator('[data-scope="type"]')).toHaveAttribute("aria-selected", "true");
  await page.getByRole("tab", { name: "Type", exact: true }).press("ArrowLeft");
  await expect(page.locator('[data-library-lens="references"]')).toHaveAttribute("aria-selected", "true");
  await expect(page.locator("#inspector-panel")).toContainText("Example.Core.Dependency");
  await expect(page.locator("#type-list")).not.toContainText("Neighbor");
  await page.locator('[data-subject-tab]:not([hidden])').first().press("End");
  await expect(page.locator('[data-scope="type"]')).toHaveAttribute("aria-selected", "true");
  await expect(page.locator(".inspected-target")).toContainText("Example.SecondWidget");
});

test("empty Library metadata survives refresh and history without selecting a neighbor", async ({ page }) => {
  await installFacades(page);
  await page.goto(root);
  await page.locator('.library-list [data-lib-scope="asset:empty"]').click();
  await expect(page.locator("#inspector-panel")).toContainText("No public types");
  await expect(page.locator('[data-scope="type"]')).toHaveCount(0);
  await page.locator('[data-library-lens="overview"]').press("End");
  await page.keyboard.press("Enter");
  await expect(page.locator("#inspector-panel")).toContainText("Example.Empty.dll");
  await expect(page.locator("html")).toHaveAttribute("data-metadata-request", "asset:empty");
  await page.locator('[data-mde-open="0"]').click();
  await expect(page.locator("html")).toHaveAttribute("data-table-request", "asset:empty");
  await page.keyboard.press("Escape");
  await page.keyboard.press("Escape");
  const shared = page.url();
  await page.reload();
  await expect(page.locator('[data-library-lens="metadata"]')).toHaveAttribute("aria-selected", "true");
  await expect(page.locator("#inspector-panel")).toContainText("Example.Empty.dll");
  await expect(page.locator("#type-list [data-type]")).toHaveCount(0);
  await page.locator('[data-subject-tab]:not([hidden])').first().press("Home");
  await expect(page.locator('[data-scope="package"]')).toHaveAttribute("aria-selected", "true");
  await page.getByRole("button", { name: "Application menu", exact: true }).press("Alt+ArrowLeft");
  await expect(page.locator("#inspector-panel")).toContainText("Example.Empty.dll");
  await expect(page.locator(".inspected-target")).not.toContainText("Widget");
  await expect(page).toHaveURL(shared);
  await page.locator('[data-subject-tab]:not([hidden])').first().press("Home");
  await page.locator('[data-scope="package"]').press("ArrowRight");
  await expect(page.locator('[data-library-lens="metadata"]')).toHaveAttribute("aria-selected", "true");
  await expect(page.locator("#inspector-panel")).toContainText("Example.Empty.dll");
  await expect(page.locator("#type-list [data-type]")).toHaveCount(0);
});

test("a single-library package retains a distinct Library level", async ({ page }) => {
  await installFacades(page, {
    ...surface,
    assemblies: [core],
    types: [type("Example.Widget", core)],
    totalMembers: 1,
  });
  await page.goto(root);
  const button = page.locator('.package-library-nav [data-lib-scope="asset:core"]');
  await button.focus();
  await page.keyboard.press("Enter");
  await expect(page.locator('[data-scope="library"]')).toHaveAttribute("aria-selected", "true");
  await expect(page.locator("#inspector-panel h1")).toHaveText("Example.Core");
  await page.reload();
  await expect(page.locator('[data-scope="library"]')).toHaveAttribute("aria-selected", "true");
});

test("browser history restores each retained Workspace Library", async ({ page }) => {
  await page.addInitScript(() => localStorage.setItem(
    "inspect-recent-packages",
    JSON.stringify([{ id: "Second.Package", version: "1.0.0", framework: "net10.0" }]),
  ));
  await installFacades(page);
  await page.goto(root);
  await page.locator('.library-list [data-lib-scope="asset:core"]').click();
  await page.keyboard.press("Control+p");
  await page.locator('[data-sl-pkg-recent="Second.Package"]').click();
  await expect(page.locator(".inspected-target")).toContainText("Second.Package");
  await expect(page.locator('[data-scope="library"]')).toHaveAttribute("aria-selected", "true");
  await expect(page.locator(".library-overview-surface h1")).toHaveText("Example.Core");
  await page.goBack();
  await expect(page.locator(".inspected-target")).toContainText("Example.Package");
  await expect(page.locator("#inspector-panel h1")).toHaveText("Example.Core");
  await expect(page.locator("#type-list [data-type]")).toHaveCount(1);
  await page.goForward();
  await expect(page.locator(".inspected-target")).toContainText("Second.Package");
  await expect(page.locator('[data-scope="library"]')).toHaveAttribute("aria-selected", "true");
  await page.locator("[data-type-nav-back]").click();
  await page.locator('.library-list [data-lib-scope="asset:other"]').click();
  await expect(page.locator("#type-list [data-type]")).toHaveCount(1);
  await expect(page.locator("#type-list")).toContainText("Neighbor");
  await page.reload();
  await expect(page.locator("#inspector-panel h1")).toHaveText("Example.Other");
  await expect(page.locator("#type-list [data-type]")).toHaveCount(1);
  await expect(page.locator("#type-list")).toContainText("Neighbor");
});

test("browser history restores the incoming retained Library ancestry", async ({ page }) => {
  const secondLibrary = library("asset:second", "Second.Core", 1);
  await page.addInitScript(() => localStorage.setItem(
    "inspect-recent-packages",
    JSON.stringify([{ id: "Second.Package", version: "1.0.0", framework: "net10.0" }]),
  ));
  await installFacades(page, surface, [{
    ...surface,
    package: "Second.Package",
    defaultAssemblyId: secondLibrary.id,
    assemblies: [secondLibrary],
    types: [type("Example.SecondWidget", secondLibrary)],
    totalMembers: 1,
  }]);
  await page.goto(root);
  await page.locator('.library-list [data-lib-scope="asset:core"]').click();
  await page.keyboard.press("Control+p");
  await page.locator('[data-sl-pkg-recent="Second.Package"]').click();
  await expect(page.locator("#inspector-panel h1")).toHaveText("Second.Core");

  await page.goBack();
  await expect(page.locator('[data-scope="library"]')).toHaveAttribute("aria-selected", "true");
  await expect(page.locator('[data-subject-tab][data-scope="library"]')).toHaveCount(1);
  await expect(page.locator('[data-subject-tab][data-scope="type"]')).toHaveCount(1);
  await expect(page.locator("#inspector-panel h1")).toHaveText("Example.Core");
  await expect(page.locator("#type-list [data-type]")).toHaveCount(1);
  await expect(page.locator("#type-list")).toContainText("Widget");

  await page.goForward();
  await expect(page.locator('[data-scope="library"]')).toHaveAttribute("aria-selected", "true");
  await page.locator('[data-subject-tab]:not([hidden])').first().press("Home");
  await expect(page.locator('[data-scope="package"]')).toHaveAttribute("aria-selected", "true");
  await page.keyboard.press("ArrowRight");
  await expect(page.locator("#inspector-panel h1")).toHaveText("Second.Core");
  await expect(page.locator("#type-list [data-type]")).toHaveCount(1);
  await expect(page.locator("#type-list")).toContainText("SecondWidget");
  await page.getByRole("button", { name: "Application menu", exact: true }).press("Alt+ArrowLeft");
  await expect(page.locator('[data-scope="package"]')).toHaveAttribute("aria-selected", "true");
  await page.getByRole("button", { name: "Application menu", exact: true }).press("Alt+ArrowRight");
  await expect(page.locator("#inspector-panel h1")).toHaveText("Second.Core");
  await page.reload();
  await expect(page.locator("#inspector-panel h1")).toHaveText("Second.Core");
  await expect(page.locator("#type-list [data-type]")).toHaveCount(1);
  await expect(page.locator("#type-list")).toContainText("SecondWidget");
});

test("browser history from before reload reuses the active Workspace", async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  await page.addInitScript(() => localStorage.setItem(
    "inspect-recent-packages",
    JSON.stringify([{ id: "Second.Package", version: "1.0.0", framework: "net10.0" }]),
  ));
  await installFacades(page);
  await page.goto(root);
  await page.locator('.library-list [data-lib-scope="asset:core"]').click();
  await page.keyboard.press("Control+p");
  await page.locator('[data-sl-pkg-recent="Second.Package"]').click();
  await expect(page.locator(".inspected-target")).toContainText("Second.Package");

  const previousSession = (await currentWorkspaceHistoryState(page)).session;
  await page.reload();
  await expect.poll(async () =>
    (await currentWorkspaceHistoryState(page)).session).not.toBe(previousSession);
  const reloadedWorkspace = await currentWorkspaceHistoryState(page);
  await page.goBack();
  await expect(page.locator(".inspected-target")).toContainText("Example.Package");
  await expect.poll(() =>
    currentWorkspaceHistoryState(page)).toEqual(reloadedWorkspace);

  await page.goForward();
  await expect(page.locator(".inspected-target")).toContainText("Second.Package");
  await expect.poll(() =>
    currentWorkspaceHistoryState(page)).toEqual(reloadedWorkspace);
  await page.locator('[data-application-scope="workspace"]').click();
  await expect(page.locator(".workspace-card")).toHaveCount(1);
  await expect(page.locator(".query-notice-text", {
    hasText: "Workspace limit reached",
  })).toHaveCount(0);
});

for (const startingSubject of ["Package", "Library", "Type", "Member"]) {
  test(`the type command enters the target Library from ${startingSubject}`, async ({ page }) => {
    await installFacades(page);
    await page.goto(root);
    if (startingSubject !== "Package") {
      await page.locator('.library-list [data-lib-scope="asset:core"]').click();
    }
    if (startingSubject === "Type" || startingSubject === "Member") {
      await page.locator('#type-list [data-type="asset:core:Example.Widget"]').click();
    }
    if (startingSubject === "Member") {
      await page.locator('[data-subject-tab]:not([hidden])').first().press("End");
    }
    await expect(page.locator(`[data-scope="${startingSubject.toLowerCase()}"]`))
      .toHaveAttribute("aria-selected", "true");
    await page.keyboard.press("Control+k");
    await page.locator("#spotlight-input").fill("type Neighbor");
    await expect(page.locator("#spotlight-results")).toContainText("type Neighbor");
    await page.keyboard.press("Enter");
    await expect(page.locator("#spotlight-input")).toHaveCount(0);
    await expect(page.locator('[data-scope="type"]')).toHaveAttribute("aria-selected", "true");
    await expect(page.locator(".inspected-target")).toContainText("Example.Other");
    await expect(page.locator(".inspected-target")).toContainText("Example.Neighbor");
    await expect(page.locator("#type-list [data-type]")).toHaveCount(1);
    await expect(page.locator("#type-list")).not.toContainText("Widget");
    await page.reload();
    await expect(page.locator(".inspected-target")).toContainText("Example.Other");
    await expect(page.locator(".inspected-target")).toContainText("Example.Neighbor");
  });
}
