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
  wrongCatalog?: boolean;
  libraryFailure?: boolean;
  libraryPending?: boolean;
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
  platform?: PlatformFixture,
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
      export async function loadRuntimePackAssembly(tfm, version, file, pack) {
        document.documentElement.dataset.platformLibraryRequest = JSON.stringify([tfm, version, file, pack]);
        if (platformOptions.libraryPending) await new Promise(resolve => document.addEventListener("finish-platform-library", resolve, { once: true }));
        if (platformOptions.libraryFailure) throw new Error("Library offline");
        const row = platformTarget.rows.find(row => row.assembly + ".dll" === file && row.pack === pack)
          ?? { assembly: file.replace(/\\.dll$/, ""), file, publicTypes: 0 };
        const assembly = {
          id: "platform:" + pack + ":" + file, name: row.assembly, version: "11.0.0.0",
          culture: null, publicKeyToken: null,
          asset: file === "System.Private.CoreLib.dll" ? "runtimes/linux-x64/native/" + row.file : row.file,
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
      export async function queryPackageVersions() { return ["1.0.0"]; }
      export function searchTypes() { return []; }
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
        return {
          package: id, version, activeFramework: framework, assembly: selected.name,
          dependencyGroups: [], dependencyGroupError: null, assemblyReferenceError: null,
          assemblyReferences: [{ name: selected.name + ".Dependency", version: "1.0.0.0", culture: null, publicKeyToken: null }],
          compileLibrary: surface.compileLibrary
        };
      }`,
    metadata: `
      ${surfaceLookup}
      export async function queryPlatformMetadata(tfm, version, file, pack) {
        document.documentElement.dataset.platformMetadataRequest = JSON.stringify([tfm, version, file, pack]);
        return {
          assemblies: [{
            assembly: file, metadataVersion: "v4.0.30319", metadataVersionTruncated: false,
            kind: "Ecma335", isAssembly: true, metadataSize: 512, projectedTableTotal: 1, heaps: [],
            tables: [{ index: 0, name: "Module", rowCount: 1, isProjected: true }], headers: {}
          }], inspectionError: null, compileLibrary: null,
        };
      }
      export async function queryPackageMetadata(id, version, framework, asset) {
        document.documentElement.dataset.metadataRequest = asset;
        const surface = surfaceFor(id);
        const selected = surface.assemblies.find(item => item.id === asset);
        if (!selected) throw new Error("Unknown library: " + asset);
        return {
          assemblies: [{
            assembly: selected.name + ".dll", metadataVersion: "v4.0.30319",
            metadataVersionTruncated: false, kind: "Ecma335", isAssembly: true,
            metadataSize: 512, projectedTableTotal: 1, heaps: [],
            tables: [{ index: 0, name: "Module", rowCount: 1, isProjected: true }], headers: {}
          }],
          inspectionError: null, compileLibrary: surface.compileLibrary
        };
      }
      export async function queryPackageMetadataTable(id, version, framework, asset, index, startRowId) {
        document.documentElement.dataset.tableRequest = asset;
        return { index, name: "Module", rowCount: 1, startRowId, columns: [], rows: [], error: null };
      }`,
    analysis: "",
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
  await installFacades(page, surface, [], options);
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
    JSON.stringify(["net11.0", platformVersion, "System.Private.Empty.dll", "netcore.app"]));
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
    JSON.stringify(["net11.0", alternatePlatformVersion, "System.Text.Json.dll", "netcore.app"]));
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
  await installFacades(page, surface, [], {});
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
  await expect(page.locator('[data-scope="platform"]')).toBeVisible();
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
    JSON.stringify(["net11.0", platformVersion, "System.Facade.dll", "netcore.app"]));
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

test("Same-named Platform Libraries retain their catalog-issued pack through metadata and refresh", async ({ page }) => {
  await openPlatform(page, { duplicateLibrary: true });
  await page.getByRole("button", { name: /System.Text.Json Implementation aspnetcore.app/ }).click();
  await expect(page.locator("html")).toHaveAttribute("data-platform-library-request",
    JSON.stringify(["net11.0", platformVersion, "System.Text.Json.dll", "aspnetcore.app"]));
  await page.locator('[data-library-lens="metadata"]').click();
  await expect(page.locator("html")).toHaveAttribute("data-platform-metadata-request",
    JSON.stringify(["net11.0", platformVersion, "System.Text.Json.dll", "aspnetcore.app"]));
  await page.reload();
  await expect(page.locator("html")).toHaveAttribute("data-platform-metadata-request",
    JSON.stringify(["net11.0", platformVersion, "System.Text.Json.dll", "aspnetcore.app"]));
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
  await expect(page.locator("#inspector-panel h1")).toHaveText("System.Text.Json");
  await expect(page.locator("html")).toHaveAttribute("data-platform-library-request",
    JSON.stringify(["net11.0", platformVersion, "System.Text.Json.dll", "netcore.app"]));
  await page.locator('[data-library-lens="metadata"]').click();
  await expect(page.locator("html")).toHaveAttribute("data-platform-metadata-request",
    JSON.stringify(["net11.0", platformVersion, "System.Text.Json.dll", "netcore.app"]));
  await page.reload();
  await expect(page.locator("html")).toHaveAttribute("data-platform-metadata-request",
    JSON.stringify(["net11.0", platformVersion, "System.Text.Json.dll", "netcore.app"]));
});

test("Package and catalog-only Platform remain distinct coordinates in the same shared Workspace", async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  await installFacades(page, surface, [], {});
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
  await expect(page.locator('[data-scope="package"]')).toHaveAttribute("aria-selected", "true");
  await page.locator('[data-application-scope="workspace"]').click();
  await page.locator("[data-workspace-platform]").click();
  await expect(page.locator('[data-scope="platform"]')).toHaveAttribute("aria-selected", "true");
  await expect(page.locator("#platform-version")).toHaveValue(platformVersion);
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

for (const width of [1440, 390]) {
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

test("opening another package from Library enters Package and preserves history", async ({ page }) => {
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
  await expect(page.locator('[data-scope="package"]')).toHaveAttribute("aria-selected", "true");
  await expect(page.locator(".library-list [data-lib-scope]")).toHaveCount(3);
  await expect(page.locator('[data-library-lens]')).toHaveCount(0);
  await page.getByRole("button", { name: "Application menu", exact: true }).press("Alt+ArrowLeft");
  await expect(page.locator(".inspected-target")).toContainText("Example.Package");
  await expect(page.locator("#inspector-panel h1")).toHaveText("Example.Core");
  await expect(page.locator("#type-list [data-type]")).toHaveCount(1);
  await page.getByRole("button", { name: "Application menu", exact: true }).press("Alt+ArrowRight");
  await expect(page.locator(".inspected-target")).toContainText("Second.Package");
  await expect(page.locator('[data-scope="package"]')).toHaveAttribute("aria-selected", "true");
  await page.locator('.library-list [data-lib-scope="asset:other"]').click();
  await expect(page.locator("#type-list [data-type]")).toHaveCount(1);
  await expect(page.locator("#type-list")).toContainText("Neighbor");
  await page.reload();
  await expect(page.locator("#inspector-panel h1")).toHaveText("Example.Other");
  await expect(page.locator("#type-list [data-type]")).toHaveCount(1);
  await expect(page.locator("#type-list")).toContainText("Neighbor");
});

test("Search between retained packages restores the incoming Library ancestry", async ({ page }) => {
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
  await page.locator('.library-list [data-lib-scope="asset:second"]').click();
  await expect(page.locator("#inspector-panel h1")).toHaveText("Second.Core");

  await page.keyboard.press("Control+p");
  await page.locator('[data-sl-pkg-open="Example.Package"]').click();
  await expect(page.locator('[data-scope="package"]')).toHaveAttribute("aria-selected", "true");
  await expect(page.locator(".library-subject-list")).toBeFocused();
  await expect(page.locator('[data-subject-tab][data-scope="library"]')).toHaveCount(1);
  await expect(page.locator('[data-subject-tab][data-scope="type"]')).toHaveCount(1);
  await page.keyboard.press("Tab");
  await expect(page.locator('.library-subject-list [data-lib-scope="asset:core"]')).toBeFocused();
  await page.keyboard.press("Enter");
  await expect(page.locator("#inspector-panel h1")).toHaveText("Example.Core");
  await expect(page.locator("#type-list [data-type]")).toHaveCount(1);
  await expect(page.locator("#type-list")).toContainText("Widget");

  await page.keyboard.press("Control+p");
  await page.locator('[data-sl-pkg-open="Second.Package"]').click();
  await expect(page.locator('[data-scope="package"]')).toHaveAttribute("aria-selected", "true");
  await expect(page.locator(".library-subject-list")).toBeFocused();
  await page.locator('[data-subject-tab]:not([hidden])').first().press("Home");
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
