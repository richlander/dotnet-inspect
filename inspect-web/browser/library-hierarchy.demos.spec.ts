import { expect, test, type Page } from "@playwright/test";
import {
  subjectTab,
  inspectorTab,
  chooseSubject,
  library,
  run,
  createType as type,
  core,
  other,
  surface,
  platformVersion,
  openProductDestination,
  installFacades,
  type BrowserAssemblySurface,
  type BrowserPackageSurface,
  type BrowserTypeSurface,
  type BrowserCallGraph,
  type BrowserHomeDemoRunResult,
} from "./library-hierarchy.support.ts";

test.use({ viewport: { width: 900, height: 900 } });

const peerSurface: BrowserPackageSurface = {
  ...surface,
  package: "Peer.Package",
  defaultAssemblyId: other.id,
  assemblies: [other],
  types: [type("Example.Neighbor", other)],
  totalMembers: 1,
};

const platformFocusAssembly: BrowserAssemblySurface = {
  id: "System.Text.Json",
  name: "System.Text.Json",
  version: "11.0.0.0",
  culture: null,
  publicKeyToken: null,
  asset: "System.Text.Json.dll",
  publicTypes: 1,
  publicMembers: 1,
  platformPack: "netcore.app",
};
const platformPeerAssembly: BrowserAssemblySurface = {
  id: "System.Runtime",
  name: "System.Runtime",
  version: "11.0.0.0",
  culture: null,
  publicKeyToken: null,
  asset: "System.Runtime.dll",
  publicTypes: 0,
  publicMembers: 0,
  platformPack: "netcore.app",
};
const platformFocusType: BrowserTypeSurface = {
  ...type("Example.Widget", platformFocusAssembly),
  accessibility: "internal",
  accessibilityId: "internal",
  platformPack: "netcore.app",
};
const platformFocusSurface: BrowserPackageSurface = {
  ...surface,
  package: "Microsoft.NETCore.App",
  version: platformVersion,
  frameworks: ["net11.0"],
  activeFramework: "net11.0",
  defaultAssemblyId: platformFocusAssembly.id,
  assemblies: [platformFocusAssembly],
  types: [platformFocusType],
  accessibility: [
    {
      id: "public",
      label: "Public",
      order: 0,
      isDefault: true,
      count: 0,
    },
    {
      id: "internal",
      label: "Internal",
      order: 1,
      isDefault: false,
      count: 1,
    },
  ],
  totalMembers: 1,
};
const platformPeerSurface: BrowserPackageSurface = {
  ...platformFocusSurface,
  defaultAssemblyId: platformPeerAssembly.id,
  assemblies: [platformPeerAssembly],
  types: [],
  totalMembers: 0,
};

function demoCallGraph(
  assembly: string,
  typeFullName: string,
): BrowserCallGraph {
  const node = {
    label: "Run",
    status: "Analyzed",
    inLoop: false,
    source: null,
    children: [],
    assembly,
    typeFullName,
    memberName: "Run",
  };
  return {
    mermaid: 'flowchart TD\n  run["Run"]',
    callers: node,
    callees: node,
    scope: {
      packages: 1,
      assemblies: 1,
      callerAssemblies: 1,
      calleeScope: "Workspace",
    },
    targets: [],
    diagnostics: {
      incompleteNodes: 0,
      incompleteEdges: 0,
      bindingIdentityConflicts: 0,
      hasUnexploredTraversalBoundary: false,
      hasAnalysisFailureBoundary: false,
      unavailableDependencyRoutes: 0,
      hasIncompleteCorrespondence: false,
      unclassifiedBoundaryEdges: 0,
      unclassifiedBoundaryNamedEdges: 0,
      unclassifiedBoundaryAssemblies: [],
      physicalOccurrenceUnavailableEdges: 0,
      isIncomplete: false,
    },
    noBody: false,
  };
}

function homeDemoResult(
  section: "Methods" | "Call Graph",
  focusKind: "package" | "platform",
): BrowserHomeDemoRunResult {
  const platform = focusKind === "platform";
  const callGraph = section === "Call Graph";
  return {
    found: true,
    packages: platform
      ? [platformPeerSurface, platformFocusSurface]
      : [peerSurface, surface],
    activation: {
      focusKind,
      focusId: platform ? "runtime" : surface.package,
      focusVersion: platform ? platformVersion : surface.version,
      focusFramework: platform ? "net11.0" : surface.activeFramework,
      focusAssembly: platform ? platformFocusAssembly.name : null,
      platformContextId: platform ? "platform-demo-context" : null,
      typeId: platform ? platformFocusType.id : surface.types[0]!.id,
      section,
      memberName: callGraph ? run.name : null,
      memberKind: callGraph ? run.kind : null,
      memberAnchorDigest: callGraph ? run.anchorDigest : null,
      memberSection: callGraph ? "call-graph" : null,
    },
    callGraph: callGraph
      ? demoCallGraph(
        platform ? platformFocusAssembly.name : core.name,
        "Example.Widget")
      : null,
  };
}

async function installHomeDemo(
  page: Page,
  section: "Methods" | "Call Graph",
  focusKind: "package" | "platform",
): Promise<string> {
  const id = `${focusKind}-${section === "Methods" ? "methods" : "graph"}`;
  await installFacades(
    page,
    surface,
    [],
    "ready",
    "ready",
    focusKind === "platform" ? {} : undefined,
    "ready",
    "ready",
    {
      catalog: [{
        id,
        title: `${focusKind} ${section}`,
        summary: "Typed product demo",
      }],
      results: { [id]: homeDemoResult(section, focusKind) },
    });
  return id;
}

async function openHomeDemo(
  page: Page,
  section: "Methods" | "Call Graph",
  focusKind: "package" | "platform",
): Promise<unknown> {
  const id = await installHomeDemo(page, section, focusKind);
  await page.goto("/demos");
  await page.locator(`[data-workspace-demo="${id}"]`).click();
  await expect(page.locator("html")).toHaveAttribute("data-home-demo-run", id);
  await page.waitForFunction(() =>
    new URL(location.href).searchParams.has("w"));
  const json = await page.evaluate(() => {
    const packet = new URL(location.href).searchParams.get("w");
    if (!packet) throw new Error("The home demo did not publish a workspace packet.");
    return atob(packet);
  });
  const share: unknown = JSON.parse(json);
  return share;
}

test("Demos is a dedicated page reached from Home and the data bar", async ({
  page,
}, testInfo) => {
  const id = await installHomeDemo(page, "Methods", "package");
  await page.goto("/");
  await page.locator("#home-demos").click();
  await expect(page).toHaveURL("/demos");
  await expect(page.getByRole("heading", { name: "Demos", exact: true }))
    .toBeFocused();
  await expect(page.locator(`[data-workspace-demo="${id}"]`)).toBeVisible();
  await expect(page.getByRole("heading", { name: "Workspace", exact: true }))
    .toHaveCount(0);
  await expect(page.locator("[data-workspace-add-package], [data-workspace-save]"))
    .toHaveCount(0);
  await page.locator("[data-product-navigation-button]").click();
  await expect(page.locator(
    "[data-product-destination][aria-current='page']",
  )).toHaveCount(0);
  const workspace =
    page.locator("[data-product-destination='workspace']");
  await expect(workspace).toHaveAttribute("aria-disabled", "true");
  await expect(workspace).toHaveAccessibleDescription("No workspace is open");
  await page.keyboard.press("ArrowDown");
  await page.keyboard.press("ArrowDown");
  await expect(workspace).toBeFocused();
  await page.keyboard.press("Enter");
  await expect(page).toHaveURL("/demos");
  await expect(workspace).toBeFocused();
  await expect(page.locator(".product-navigation-menu")).toBeVisible();
  await page.keyboard.press("Escape");
  await expect(page.locator("html")).not.toHaveAttribute("data-home-demo-run");
  await page.screenshot({ path: testInfo.outputPath("demos-wide.png") });

  await page.getByRole("link", { name: "Home", exact: true }).click();
  await expect(page).toHaveURL("/");
  await page.getByRole("link", { name: "Demos", exact: true }).click();
  await expect(page).toHaveURL("/demos");
  await page.reload();
  await expect(page.getByRole("heading", { name: "Demos", exact: true }))
    .toBeVisible();
  await page.setViewportSize({ width: 390, height: 844 });
  await expect(page.locator(`[data-workspace-demo="${id}"]`))
    .toBeInViewport({ ratio: 1 });
  expect(await page.evaluate(() =>
    document.documentElement.scrollWidth <= document.documentElement.clientWidth))
    .toBe(true);
  await page.screenshot({ path: testInfo.outputPath("demos-narrow.png") });
  await page.keyboard.press("Control+p");
  await expect(page.locator("#spotlight-input")).toBeFocused();
  await page.keyboard.press("Escape");
  await expect(page.locator("#spotlight-input")).toHaveCount(0);
});

test("Package navigation retains the shared System.Text.Json packet and Workspace stays separate from Demos", async ({
  page,
}) => {
  const assembly = library(
    "compile:lib/netstandard2.0/System.Text.Json.dll", "System.Text.Json", 1);
  const jsonSurface: BrowserPackageSurface = {
    ...surface,
    package: "System.Text.Json",
    version: platformVersion,
    frameworks: ["net11.0", "netstandard2.0"],
    activeFramework: "netstandard2.0",
    defaultAssemblyId: assembly.id,
    compileLibrary: { status: "Selected", targetFramework: "netstandard2.0", message: null },
    assemblies: [assembly],
    types: [type("System.Text.Json.JsonSerializer", assembly)],
  };
  await installFacades(page, jsonSurface);
  await page.goto(`/?package=System.Text.Json&version=${platformVersion}&framework=netstandard2.0`);
  await expect(subjectTab(page, "library")).toHaveAttribute("aria-selected", "true");
  await page.waitForFunction(() => new URL(location.href).searchParams.has("w"));
  const sharedLibraryUrl = page.url();
  await page.reload();
  await expect(subjectTab(page, "library")).toHaveAttribute("aria-selected", "true");
  await expect(page).toHaveURL(sharedLibraryUrl);

  await chooseSubject(page, "package", "Package");
  await expect.poll(() => page.evaluate((): unknown => {
    const packet = new URL(location.href).searchParams.get("w");
    return packet ? JSON.parse(atob(packet)) : null;
  })).toMatchObject({
    tabs: [{ source: "System.Text.Json", version: platformVersion, framework: "netstandard2.0" }],
    view: { lens: "overview" },
  });
  const packageUrl = page.url();
  expect([...new URL(packageUrl).searchParams.keys()]).toEqual(["package", "w"]);
  await page.reload();
  await expect(subjectTab(page, "package")).toHaveAttribute("aria-selected", "true");
  await expect(page.locator('[data-package-framework="netstandard2.0"]'))
    .toHaveAttribute("aria-current", "page");
  await openProductDestination(page, "workspace");
  await expect(page.getByRole("heading", { name: "Workspace", exact: true }))
    .toBeVisible();
  await expect(page.locator("[data-workspace-activate]")).toContainText("System.Text.Json");
  await expect(page.locator("[data-workspace-activate]")).toContainText("netstandard2.0");
  await expect(page.locator("[data-workspace-demo]")).toHaveCount(0);
  await page.waitForFunction(() => location.hash === "#workspace");
  const workspaceUrl = page.url();

  await page.getByRole("link", { name: "Demos", exact: true }).click();
  await expect(page).toHaveURL("/demos");
  await expect(page.getByRole("heading", { name: "Demos", exact: true }))
    .toBeVisible();
  await expect(page.locator("[data-workspace-activate]")).toHaveCount(0);
  await page.goBack();
  await expect(page).toHaveURL(workspaceUrl);
  await expect(page.locator("[data-workspace-activate]")).toContainText("System.Text.Json");
  await expect(page.locator("[data-workspace-activate]")).toContainText("netstandard2.0");
  await expect(page.locator("[data-workspace-demo]")).toHaveCount(0);
});

test("package Methods demo retains all returned coordinates and publishes its exact type", async ({
  page,
}) => {
  const share = await openHomeDemo(page, "Methods", "package");
  await expect(subjectTab(page, "type"))
    .toHaveAttribute("aria-selected", "true");
  await expect(page.locator(".inspected-target"))
    .toContainText("Example.Widget");
  expect(share).toMatchObject({
    tabs: [
      { id: "t0", source: "Peer.Package" },
      { id: "t1", source: "Example.Package" },
    ],
    contexts: expect.arrayContaining([{
      id: "g2",
      tabIds: ["t0", "t1"],
    }]),
    activeTabId: "t1",
    selectedContextId: "g2",
    view: {
      type: surface.types[0]!.id,
      memberAnchor: null,
      section: null,
      libraries: [core.id],
    },
  });
});

test("package Call Graph demo applies the returned member and graph", async ({
  page,
}) => {
  const share = await openHomeDemo(page, "Call Graph", "package");
  await expect(subjectTab(page, "member"))
    .toHaveAttribute("aria-selected", "true");
  await expect(inspectorTab(page, "data-member-section", "call-graph"))
    .toHaveAttribute("aria-selected", "true");
  await expect(page.locator("#call-graph-diagram svg")).toBeVisible();
  expect(share).toMatchObject({
    view: {
      type: surface.types[0]!.id,
      memberAnchor: run.anchorDigest,
      section: "call-graph",
      libraries: [core.id],
    },
  });
});

test("Platform Methods demo uses its non-first engine surface without reloading it", async ({
  page,
}) => {
  const share = await openHomeDemo(page, "Methods", "platform");
  await expect(subjectTab(page, "type"))
    .toHaveAttribute("aria-selected", "true");
  await expect(page.locator(".inspected-target"))
    .toContainText("Example.Widget");
  await expect(page.locator(
    `[data-type="${platformFocusType.id}"]`,
  )).toBeVisible();
  await expect(page.locator("html"))
    .not.toHaveAttribute("data-platform-library-request", /.+/);
  expect(share).toMatchObject({
    tabs: [{
      kind: "group",
      source: ":Platform",
      version: platformVersion,
      framework: "net11.0",
    }],
    view: {
      type: platformFocusType.id,
      memberAnchor: null,
      section: null,
      libraries: [JSON.stringify(["netcore.app", "System.Text.Json.dll"])],
    },
  });
});

test("Platform Call Graph demo publishes the exact Library and member", async ({
  page,
}) => {
  const share = await openHomeDemo(page, "Call Graph", "platform");
  await expect(subjectTab(page, "member"))
    .toHaveAttribute("aria-selected", "true");
  await expect(inspectorTab(page, "data-member-section", "call-graph"))
    .toHaveAttribute("aria-selected", "true");
  await expect(page.locator("#call-graph-diagram svg")).toBeVisible();
  await expect(page.locator("html"))
    .not.toHaveAttribute("data-platform-library-request", /.+/);
  expect(share).toMatchObject({
    view: {
      type: platformFocusType.id,
      memberAnchor: run.anchorDigest,
      section: "call-graph",
      libraries: [JSON.stringify(["netcore.app", "System.Text.Json.dll"])],
    },
  });
});

test("home demo history failure restores the catalog without publication", async ({
  page,
}) => {
  const id = await installHomeDemo(page, "Methods", "package");
  await page.goto("/demos");
  const retainedBefore = await page.locator("[data-workspace-switch]").count();
  await page.evaluate(() => {
    Object.defineProperty(history, "pushState", {
      configurable: true,
      value: () => {
        throw new DOMException("History blocked");
      },
    });
  });
  await page.locator(`[data-workspace-demo="${id}"]`).click();
  await expect(page.locator(".query-notice-text"))
    .toContainText("could not commit its destination");
  await expect(page).toHaveURL(/\/demos$/);
  await expect(page.getByRole("heading", { name: "Demos", exact: true })).toBeVisible();
  await expect(page.locator(`[data-workspace-demo="${id}"]`)).toBeFocused();
  await expect(page.locator("[data-workspace-switch]"))
    .toHaveCount(retainedBefore);
});

test("Activity Back restores focus on the Demos route", async ({ page }) => {
  await installHomeDemo(page, "Methods", "package");
  await page.goto("/demos");
  await expect(page.getByRole("heading", { name: "Demos", exact: true }))
    .toBeVisible();
  await page.keyboard.press("Control+k");
  await page.locator("#spotlight-input").fill("activity");
  await page.locator('[data-sl-package-activity="1"]').click();
  await expect(page).toHaveURL(/\/activity$/);

  await page.goBack();

  await expect(page).toHaveURL(/\/demos$/);
  await expect(page.getByRole("heading", { name: "Demos", exact: true }))
    .toBeFocused();
});

test("Activity catalog failure focuses the visible route heading", async ({
  page,
}) => {
  await installFacades(
    page,
    surface,
    [],
    "ready",
    "ready",
    undefined,
    "ready",
    "ready",
    undefined,
    {},
    { activityCatalogFailure: true },
  );
  await page.goto("/activity");

  await expect(page.locator(".query-navigation-error"))
    .toContainText("Package Activity catalog offline");
  await expect(page.locator("#package-changes-package-set")).toBeDisabled();
  await expect(page.getByRole("heading", {
    name: "Package Activity",
    exact: true,
  })).toBeFocused();
});
