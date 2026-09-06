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
async function installFacades(
  page: Page,
  model = surface,
  additionalSurfaces: readonly BrowserPackageSurface[] = [],
) {
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
    await expect(libraryOverview.locator(".overview-identity .subject-icon")).toHaveText("◫");
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

test("both Package icons retain the existing image fallback", async ({ page }) => {
  await installFacades(page);
  await page.goto(root);
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
