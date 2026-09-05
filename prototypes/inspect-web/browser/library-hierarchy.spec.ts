import { expect, test, type Page } from "@playwright/test";
import { basename } from "node:path";
import { fileURLToPath } from "node:url";
import type {
  BrowserAssemblySurface,
  BrowserMemberSurface,
  BrowserPackageSurface,
  BrowserTypeSurface,
} from "../src/facades/inspect-web-package.d.ts";

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

// Exercise the production composition root and bindings with deterministic facade
// responses. Codec and participant-query behavior have separate engine outcome gates.
async function installFacades(page: Page, model = surface) {
  const common = "export async function initializeRuntime() {}";
  const modules: Record<string, string> = {
    host: `
      export async function createRuntime() { return {}; }
      export function configureHost() {}
      export async function runEntryPoint() { return 0; }
      export function buildIdentity() {
        return { version: "fixture", commit: null, builtAtUtc: null, commitUrl: null };
      }`,
    package: `
      const surface = ${JSON.stringify(model)};
      export async function queryPackage() { return surface; }
      export async function queryPackageVersions() { return ["1.0.0"]; }
      export function clearWorkspacePackageOccurrences() {}
      export function packageCacheStats() {
        return { packages: 1, resident: 1, workspaces: 1, residentBytes: 0 };
      }
      export function listPackageQueryFacets() { return { facets: [] }; }
      export async function queryMemberDocumentation() {
        return { summary: "Runs the widget.", returns: null, parameters: {}, exceptions: [] };
      }
      export async function queryPackageDependencies(id, version, framework, asset) {
        document.documentElement.dataset.referenceRequest = asset;
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
      const surface = ${JSON.stringify(model)};
      export async function queryPackageMetadata(id, version, framework, asset) {
        document.documentElement.dataset.metadataRequest = asset;
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
  await page.route("**/assets/platform-index.tsv", route =>
    route.fulfill({ status: 404, body: "Platform catalog is not part of this fixture." }));
  await page.route("**/*", route =>
    route.request().resourceType() === "document"
      ? route.fulfill({
          path: fileURLToPath(new URL("../dist/index.html", import.meta.url)),
          contentType: "text/html",
        })
      : route.fallback());
}

const root = "/?package=Example.Package&version=1.0.0&framework=net10.0#pkg";

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
