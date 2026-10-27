import { expect, test, type Page } from "@playwright/test";
import {
  subjectTab,
  inspectorTab,
  chooseInspector,
  chooseSubject,
  library,
  run,
  createType as type,
  core,
  other,
  surface,
  platformVersion,
  alternatePlatformVersion,
  openProductDestination,
  installFacades,
  releaseFacade,
  type BrowserAssemblySurface,
  type BrowserPackageSurface,
  type BrowserTypeSurface,
  type BrowserCallGraph,
  type BrowserHomeDemoResolved,
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
    boundaries: [],
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
      ? [platformPeerSurface, platformFocusSurface].map(packageSurface => ({
        surface: packageSurface,
        runtimeIdentifier: null,
        implementationComparisonRuntimeIdentifier: null,
      }))
      : [peerSurface, surface].map(packageSurface => ({
        surface: packageSurface,
        runtimeIdentifier: "linux-x64",
        implementationComparisonRuntimeIdentifier: "linux-x64",
      })),
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

function homeDemoResolution(
  id: string,
  section: "Methods" | "Call Graph",
  focusKind: "package" | "platform",
): BrowserHomeDemoResolved {
  const callGraph = section === "Call Graph";
  const members = focusKind === "platform"
    ? [{
      kind: "platform",
      id: "runtime",
      version: platformVersion,
      framework: "net11.0",
      assembly: platformFocusAssembly.name,
    }]
    : [peerSurface, surface].map(model => ({
      kind: "package",
      id: model.package,
      version: model.version,
      framework: model.activeFramework,
      assembly: null,
    }));
  const tabs = members.map((member, index) => ({
    id: focusKind === "platform" ? "stj" : `t${index}`,
    member,
  }));
  const focus = tabs.at(-1)!;
  const view = {
    library: focusKind === "platform" ? platformFocusAssembly.name : core.name,
    type: focusKind === "platform"
      ? platformFocusType.id
      : surface.types[0]!.id,
    memberAnchor: callGraph ? run.anchorDigest : null,
    memberKey: callGraph ? run.stableSelector : null,
    section,
  };
  const workspaceTabs = focusKind === "platform"
    ? [{
      id: focus.id,
      kind: "group",
      source: ":Platform",
      version: focus.member.version,
      framework: focus.member.framework,
      runtimeIdentifier: null,
    }]
    : tabs.map(tab => ({
      id: tab.id,
      kind: "package",
      source: tab.member.id,
      version: tab.member.version,
      framework: tab.member.framework,
      runtimeIdentifier: null,
    }));
  const workspaceId = `${id}-workspace`;
  const viewId = `${id}-view`;
  const navigationId = `${id}-navigation`;
  const coordinates = members.map(member =>
    member.kind === "platform"
      ? {
        kind: "platform",
        family: member.id,
        assembly: member.assembly,
        version: member.version,
        framework: member.framework,
      }
      : {
        kind: "package",
        id: member.id,
        version: member.version,
        framework: member.framework,
      });
  return {
    id,
    title: `${focusKind} ${section}`,
    summary: "Typed product demo",
    workspaceMembers: members,
    tabs,
    focusTabIndex: members.length - 1,
    view,
    workspace: {
      tabs: workspaceTabs,
      contexts: [{
        id: "demo",
        tabIds: workspaceTabs.map(tab => tab.id),
      }],
      activeTabId: focus.id,
      selectedContextId: "demo",
      view: {
        lens: "api",
        type: view.type,
        memberAnchor: view.memberAnchor,
        memberSignature: null,
        section: callGraph ? "call-graph" : null,
        libraries: [view.library],
        sourceView: null,
        comparison: null,
      },
      traversalTargetFramework: null,
      hierarchyWorkspacePacket: null,
    },
    restorationDefinitions: [
      JSON.stringify({
        schemaVersion: 1,
        kind: "workspace",
        id: workspaceId,
        title: `${focusKind} ${section}`,
        description: "Typed product demo",
        contexts: [{
          name: "demo",
          framework: focus.member.framework,
          members: coordinates,
        }],
      }),
      JSON.stringify({
        schemaVersion: 1,
        kind: "view",
        id: viewId,
        type: view.type,
        memberAnchor: view.memberAnchor,
        memberKey: view.memberKey,
        section,
      }),
      JSON.stringify({
        schemaVersion: 1,
        kind: "navigation",
        id: navigationId,
        tabs: tabs.map((tab, index) => ({
          id: tab.id,
          coordinate: coordinates[index],
        })),
        focus: focus.id,
      }),
      JSON.stringify({
        schemaVersion: 1,
        kind: "scenario",
        id,
        title: `${focusKind} ${section}`,
        description: "Typed product demo",
        workspace: workspaceId,
        context: "demo",
        view: viewId,
        navigation: navigationId,
      }),
    ],
  };
}

async function installHomeDemo(
  page: Page,
  section: "Methods" | "Call Graph",
  focusKind: "package" | "platform",
  options?: {
    holdAdmission?: boolean;
    holdPostingRecord?: boolean;
    holdAcknowledgement?: boolean;
    holdResolutionAt?: number;
    failResolutionAt?: number;
    holdSurfaceAdmissionAt?: number;
  },
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
      resolutions: {
        [id]: homeDemoResolution(id, section, focusKind),
      },
      results: { [id]: homeDemoResult(section, focusKind) },
      ...(options?.holdResolutionAt === undefined
        ? {}
        : { resolutionPendingOrdinal: options.holdResolutionAt }),
      ...(options?.failResolutionAt === undefined
        ? {}
        : { resolutionFailureOrdinal: options.failResolutionAt }),
      ...(options?.holdSurfaceAdmissionAt === undefined
        ? {}
        : {
          surfaceAdmissionPendingOrdinal:
            options.holdSurfaceAdmissionAt,
        }),
    },
    undefined,
    undefined,
    undefined,
    undefined,
    options);
  return id;
}

async function installMethodsHomeDemos(
  page: Page,
  options: {
    resolutionPendingOrdinal?: number;
    resolutionFailureOrdinal?: number;
    preparationPendingOrdinal?: number;
    preparationFailureOrdinal?: number;
  },
): Promise<{ packageId: string; platformId: string }> {
  const packageId = "package-methods";
  const platformId = "platform-methods";
  await installFacades(
    page,
    surface,
    [],
    "ready",
    "ready",
    {},
    "ready",
    "ready",
    {
      catalog: [
        {
          id: packageId,
          title: "package Methods",
          summary: "Typed product demo",
        },
        {
          id: platformId,
          title: "platform Methods",
          summary: "Typed product demo",
        },
      ],
      resolutions: {
        [packageId]: homeDemoResolution(packageId, "Methods", "package"),
        [platformId]: homeDemoResolution(platformId, "Methods", "platform"),
      },
      results: {
        [packageId]: homeDemoResult("Methods", "package"),
        [platformId]: homeDemoResult("Methods", "platform"),
      },
      ...(options.resolutionPendingOrdinal === undefined
        ? {}
        : { resolutionPendingOrdinal: options.resolutionPendingOrdinal }),
      ...(options.resolutionFailureOrdinal === undefined
        ? {}
        : { resolutionFailureOrdinal: options.resolutionFailureOrdinal }),
      ...(options.preparationPendingOrdinal === undefined
        ? {}
        : { preparationPendingOrdinal: options.preparationPendingOrdinal }),
      ...(options.preparationFailureOrdinal === undefined
        ? {}
        : { preparationFailureOrdinal: options.preparationFailureOrdinal }),
    });
  return { packageId, platformId };
}

async function openHomeDemo(
  page: Page,
  section: "Methods" | "Call Graph",
  focusKind: "package" | "platform",
): Promise<unknown> {
  const id = await installHomeDemo(page, section, focusKind);
  await page.goto("/demos");
  await page.locator(`[data-workspace-demo="${id}"]`).click();
  if (section === "Methods") {
    await expect(page).toHaveURL(/\/\?w=.+#workspace$/);
    await expect(page.locator("html"))
      .toHaveAttribute("data-home-demo-prepare", id);
    await expect(page.locator("html"))
      .not.toHaveAttribute("data-home-demo-run");
    return null;
  }
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

test("Ecosystem Adds stay serialized across a route visit", async ({
  page,
}) => {
  await installHomeDemo(page, "Methods", "package", {
    holdAdmission: true,
  });
  await page.goto("/");
  await openProductDestination(page, "ecosystems");
  await page.locator(
    '[data-ecosystem="ecosystem.fixture-package"] [data-ecosystem-open]',
  ).click();
  const additions = page.locator("[data-ecosystem-package-add]");
  await expect(additions).toHaveCount(24);
  const initialAcknowledgements = Number(
    await page.locator("html").getAttribute(
      "data-ecosystem-package-acknowledgement-count") ?? "0");
  await additions.first().click();
  await expect(page.locator("html")).toHaveAttribute(
    "data-ecosystem-package-admission-pending",
    "true",
  );
  await expect(page.locator("html")).toHaveAttribute(
    "data-ecosystem-package-admission-count",
    "1",
  );

  await page.locator("[data-product-navigation-button]").click();
  await page.locator('[data-product-destination="query"]').click();
  await expect(page).toHaveURL("/query");
  await page.goBack();
  await expect(additions).toHaveCount(24);
  await expect(additions.first()).toHaveText("Adding\u2026");
  await expect(additions.first()).toBeDisabled();
  await expect(additions.nth(1)).toBeDisabled();
  await expect(page.locator("html")).toHaveAttribute(
    "data-ecosystem-package-admission-count",
    "1",
  );

  await releaseFacade(page, "finish-ecosystem-package-admission");
  await expect(additions.first()).toHaveText("Added");
  await expect(additions.nth(1)).toBeEnabled();
  await expect(page.locator(".workspace-card.active"))
    .toContainText("1 loaded coordinate");
  await expect(page.locator("html")).toHaveAttribute(
    "data-ecosystem-package-acknowledgement-count",
    String(initialAcknowledgements + 1),
  );
});

test("Ecosystem posting survives navigation during Scope record", async ({
  page,
}) => {
  await installHomeDemo(page, "Methods", "package", {
    holdPostingRecord: true,
  });
  await page.goto("/");
  await openProductDestination(page, "ecosystems");
  await page.locator(
    '[data-ecosystem="ecosystem.fixture-package"] [data-ecosystem-open]',
  ).click();
  const additions = page.locator("[data-ecosystem-package-add]");
  await expect(additions).toHaveCount(24);
  const initialAcknowledgements = Number(
    await page.locator("html").getAttribute(
      "data-ecosystem-package-acknowledgement-count") ?? "0");
  await additions.first().click();
  await expect(page.locator("html")).toHaveAttribute(
    "data-ecosystem-package-posting-pending",
    "true",
  );
  await expect(page.locator("html")).toHaveAttribute(
    "data-ecosystem-package-admission-count",
    "1",
  );

  await page.locator("[data-product-navigation-button]").click();
  await page.locator('[data-product-destination="query"]').click();
  await expect(page).toHaveURL("/query");
  await releaseFacade(page, "finish-ecosystem-package-posting");
  await expect(page.locator("html")).toHaveAttribute(
    "data-ecosystem-package-acknowledgement-count",
    String(initialAcknowledgements + 1),
  );
  await page.goBack();
  await expect(page.locator(".workspace-card.active"))
    .toContainText("1 loaded coordinate");
  await expect(additions).toHaveCount(24);
});

for (const staleAt of ["admission", "posting record"] as const) {
  test(`A stale Ecosystem ${staleAt} cannot replace a newer Workspace`, async ({
    page,
  }) => {
    await installHomeDemo(page, "Methods", "package", {
      holdAdmission: staleAt === "admission",
      holdPostingRecord: staleAt === "posting record",
      holdAcknowledgement: true,
    });
    await page.goto("/");
    await openProductDestination(page, "ecosystems");
    await page.locator(
      '[data-ecosystem="ecosystem.fixture-package"] [data-ecosystem-open]',
    ).click();
    const additions = page.locator("[data-ecosystem-package-add]");
    const activeWorkspace = page.locator(".workspace-card.active");
    const html = page.locator("html");
    await expect(additions).toHaveCount(24);
    await expect(activeWorkspace.locator("small")).toHaveText("Active");
    const initialAcknowledgements = Number(
      await html.getAttribute(
        "data-ecosystem-package-acknowledgement-count") ?? "0");
    const originalUrl = page.url();
    await additions.first().click();

    if (staleAt === "admission") {
      await expect(html).toHaveAttribute(
        "data-ecosystem-package-admission-pending",
        "true",
      );
      await openProductDestination(page, "query");
      await expect(page).toHaveURL("/query");
      await releaseFacade(page, "finish-ecosystem-package-admission");
    } else {
      await expect(html).toHaveAttribute(
        "data-ecosystem-package-posting-pending",
        "true",
      );
      await activeWorkspace.click();
      await expect(page).toHaveURL(originalUrl);
      await releaseFacade(page, "finish-ecosystem-package-posting");
    }
    await expect(html).toHaveAttribute(
      "data-ecosystem-package-acknowledgement-pending",
      "true",
    );
    await expect(html).toHaveAttribute(
      "data-ecosystem-package-acknowledgement-count",
      String(initialAcknowledgements + 1),
    );

    await openProductDestination(page, "ecosystems");
    await page.locator(
      '[data-ecosystem="ecosystem.fixture-platform"] [data-ecosystem-open]',
    ).click();
    await expect(page).toHaveURL("/ecosystems/ecosystem.fixture-platform");
    await expect(activeWorkspace).toContainText("Platform fixture");
    await expect(activeWorkspace).toContainText("0 loaded coordinates");
    await expect(activeWorkspace.locator("small")).toHaveText("Active");
    await expect(html).toHaveAttribute(
      "data-ecosystem-package-acknowledgement-count",
      String(initialAcknowledgements + 2),
    );
    await expect(html).toHaveAttribute(
      "data-ecosystem-package-acknowledgement-pending",
      "true",
    );

    await releaseFacade(page, "finish-ecosystem-package-acknowledgement");
    await expect(html).toHaveAttribute(
      "data-ecosystem-package-acknowledgement-pending",
      "false",
    );
    await expect(activeWorkspace).toContainText("Platform fixture");
    await expect(activeWorkspace).toContainText("0 loaded coordinates");

    await expect(additions).toHaveCount(24);
    await expect(additions.first()).toBeEnabled();
    await additions.first().click();
    await expect(html).toHaveAttribute(
      "data-ecosystem-package-admission-count",
      "2",
    );
    await expect(additions.first()).toHaveText("Added");
    await expect(activeWorkspace).toContainText("Platform fixture");
    await expect(activeWorkspace).toContainText("1 loaded coordinate");
    await expect(html).toHaveAttribute(
      "data-ecosystem-package-admissions",
      /"ecosystem\.fixture-platform"/u,
    );
  });
}

test("Ecosystems is a first-class product catalog destination", async ({
  page,
}, testInfo) => {
  await installHomeDemo(page, "Methods", "package");
  await page.goto("/");
  await openProductDestination(page, "ecosystems");
  await expect(page).toHaveURL("/ecosystems");
  await expect(page.getByRole("heading", { name: "Ecosystems", exact: true }))
    .toBeFocused();
  await expect(page.locator(
    "[data-ecosystem='ecosystem.fixture-platform']"
      + " + [data-ecosystem='ecosystem.fixture-package']",
  )).toBeVisible();
  await expect(page.locator("[data-ecosystem='ecosystem.fixture-package']"))
    .toContainText("3 core packages");
  await expect(page.locator("[data-ecosystem='ecosystem.fixture-package']"))
    .toContainText("Integration scanner");
  const openActions = page.locator("[data-ecosystem-open]");
  const actionableCards = page.locator(
    ".ecosystem-catalog-row",
  ).filter({ has: page.locator("[data-ecosystem-open]") });
  expect(await openActions.count()).toBe(await actionableCards.count());
  expect(await openActions.count()).toBeGreaterThan(0);
  await page.locator("[data-product-navigation-button]").click();
  await expect(page.locator(
    "[data-product-destination][aria-current='page']",
  )).toHaveText(["Ecosystems"]);
  await page.keyboard.press("Escape");
  await page.screenshot({ path: testInfo.outputPath("ecosystems-wide.png") });

  const firstActionable = actionableCards.first();
  const ecosystemId = await firstActionable.getAttribute("data-ecosystem");
  if (ecosystemId === null) {
    throw new Error("The actionable Ecosystem omitted its catalog ID.");
  }
  const ecosystemTitle = await firstActionable.getByRole("heading", {
    level: 2,
  }).innerText();
  await firstActionable.locator("[data-ecosystem-open]").click();
  await expect(page).toHaveURL(`/ecosystems/${ecosystemId}`);
  await expect(page.locator(
    '[data-navigation-group="subject"][data-navigation-current="true"]'
      + '[data-navigation-item="tab"]',
  )).toContainText(ecosystemTitle);
  await expect(page.getByRole("heading", {
    name: ecosystemTitle,
    exact: true,
  })).toBeVisible();
  await expect(page.locator("html")).toHaveAttribute(
    "data-ecosystem-package-query",
    /"ecosystem\.fixture-[^"]+",200,96,false,24/u,
  );
  const discoveredPackages = page.locator(
    "[data-ecosystem-package-open]");
  const packageAdmissions = page.locator(
    "[data-ecosystem-package-add]");
  await expect(discoveredPackages).toHaveCount(24);
  await expect(packageAdmissions).toHaveCount(24);
  await expect(page.getByText("Show: 24 | 48 | 96")).toBeVisible();
  const initialOperation = await page.locator("html").getAttribute(
    "data-ecosystem-package-query");
  await packageAdmissions.first().click();
  await expect(packageAdmissions.first()).toHaveText("Added");
  await expect(page).toHaveURL(`/ecosystems/${ecosystemId}`);
  await expect(page.getByRole("heading", {
    name: ecosystemTitle,
    exact: true,
  })).toBeVisible();
  await expect(page.locator("html")).toHaveAttribute(
    "data-ecosystem-package-admissions",
    /"Aspire\.Hosting","9\.0\.0","ecosystem\.fixture-[^"]+","ExactPackage","Aspire\.Hosting"/u,
  );
  await expect(discoveredPackages).toHaveCount(24);
  await expect(page.getByText("Show: 24 | 48 | 96")).toBeVisible();
  await discoveredPackages.first().click();
  await expect(page.locator(".query-error")).toHaveText(
    "Fixture package activation failed.");
  await expect(discoveredPackages).toHaveCount(24);
  await expect(page.getByText("Show: 24 | 48 | 96")).toBeVisible();
  await expect(page.locator("html")).toHaveAttribute(
    "data-ecosystem-package-query",
    initialOperation ?? "",
  );
  await page.locator(
    '[data-ecosystem-package-capacity="48"]',
  ).click();
  await expect(discoveredPackages).toHaveCount(48);
  await page.locator(
    '[data-ecosystem-package-capacity="96"]',
  ).click();
  await expect(discoveredPackages).toHaveCount(60);
  await expect(page.locator(
    '[data-ecosystem-package-capacity="96"]',
  )).toHaveCount(0);
  await expect(page.getByText("All matching packages are shown."))
    .toBeVisible();
  await expect(page.locator(
    "[data-product-package-action],"
      + " [data-workspace-platform],"
      + " [data-workspace-framework-library]",
  )).toHaveCount(0);
  const ecosystemLocation = page.url();
  await openProductDestination(page, "home");
  await expect(page).toHaveURL("/");
  await page.goBack();
  await expect(page).toHaveURL(ecosystemLocation);
  await expect(page.getByRole("heading", {
    name: ecosystemTitle,
    exact: true,
  })).toBeVisible();
  await expect(discoveredPackages).toHaveCount(24);
  await expect(page.locator("html")).not.toHaveAttribute(
    "data-ecosystem-package-query",
    initialOperation ?? "",
  );
  await page.goForward();
  await expect(page).toHaveURL("/");
  await page.goBack();
  await expect(page.getByRole("heading", {
    name: ecosystemTitle,
    exact: true,
  })).toBeVisible();
  await expect(discoveredPackages).toHaveCount(24);
  await page.locator("[data-product-navigation-button]").click();
  await page.locator('[data-product-destination="query"]').click();
  await expect(page).toHaveURL("/query");
  await expect(page.locator("html")).toHaveAttribute(
    "data-ecosystem-package-query-cancellations",
    "1",
  );

  await page.goto("/");
  await page.getByRole("link", { name: "Ecosystems", exact: true }).click();
  await expect(page).toHaveURL("/ecosystems");
  await page.reload();
  const ecosystemCards = page.locator(".ecosystem-catalog-row");
  await expect(ecosystemCards).toHaveCount(2);
  await expect(ecosystemCards.locator(".ecosystem-catalog-icon")).toHaveCount(2);
  await expect(ecosystemCards.locator("h2")).toHaveText([
    "Platform fixture",
    "Package fixture",
  ]);
  await expect(page.locator(
    "[data-ecosystem='ecosystem.fixture-platform']")).toBeVisible();
  await page.getByRole("link", { name: "Home", exact: true }).click();
  await page.goBack();
  await expect(page).toHaveURL("/ecosystems");
  await expect(page.getByRole("heading", { name: "Ecosystems", exact: true }))
    .toBeFocused();
  await page.setViewportSize({ width: 390, height: 844 });
  expect(await page.evaluate(() =>
    document.documentElement.scrollWidth <= document.documentElement.clientWidth))
    .toBe(true);
  await page.screenshot({ path: testInfo.outputPath("ecosystems-narrow.png") });
});

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
  )).toHaveText(["Demos"]);
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
  await expect(page.locator(".library-overview-surface h1"))
    .toHaveText(assembly.name);
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
  await openProductDestination(page, "workspace", { waitForCommit: true });
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
  await openProductDestination(page, "workspace");
  await expect(page).toHaveURL(workspaceUrl);
  await expect(page.getByRole("heading", { name: "Workspace", exact: true }))
    .toBeVisible();
  await page.goBack();
  await expect(page).toHaveURL("/demos");
  await expect(page.getByRole("heading", { name: "Demos", exact: true }))
    .toBeVisible();
  await page.goBack();
  await expect(page).toHaveURL(workspaceUrl);
  await expect(page.locator("[data-workspace-activate]")).toContainText("System.Text.Json");
  await expect(page.locator("[data-workspace-activate]")).toContainText("netstandard2.0");
  await expect(page.locator("[data-workspace-demo]")).toHaveCount(0);
});

test("package Methods demo retains all resolved coordinates and restores its exact type", async ({
  page,
}) => {
  await openHomeDemo(page, "Methods", "package");
  const workspaceUrl = page.url();
  await expect(subjectTab(page, "type"))
    .toHaveAttribute("aria-selected", "true");
  await expect(page.locator(".inspected-target"))
    .toContainText("Example.Widget");
  await page.goBack();
  await expect(page).toHaveURL("/demos");
  await page.goForward();
  await expect(page).toHaveURL(workspaceUrl);
  await expect(subjectTab(page, "type"))
    .toHaveAttribute("aria-selected", "true");
  await expect(page.locator(".inspected-target"))
    .toContainText("Example.Widget");
  await expect(page.locator("html"))
    .not.toHaveAttribute("data-home-demo-run");
  await expect(page.locator("html"))
    .toHaveAttribute("data-home-demo-definition-record-count", "4");
  await expect(page.locator("html"))
    .toHaveAttribute(
      "data-home-demo-prepared-subjects",
      /\["t0","t1"\]/,
    );
  await openProductDestination(page, "workspace");
  await expect(page.locator(".workspace-card.active"))
    .toContainText("package Methods");
  await expect(page.getByRole("button", { name: /Inspect Peer\.Package/ }))
    .toBeVisible();
  await expect(page.getByRole("button", { name: /Inspect Example\.Package/ }))
    .toBeVisible();
});

test("Methods demo Forward reactivates an inactive retained definition", async ({
  page,
}) => {
  const id = await installHomeDemo(page, "Methods", "package");
  const demo = page.locator(`[data-workspace-demo="${id}"]`);
  await page.goto("/demos");
  await demo.click();
  await expect(page.locator(".inspected-target"))
    .toContainText("Example.Widget");
  const firstWorkspaceUrl = page.url();
  await page.getByRole("link", { name: "Demos", exact: true }).click();
  await demo.click();
  await expect(page.locator(".inspected-target"))
    .toContainText("Example.Widget");
  await releaseFacade(page, "remove-home-demo-catalog");
  await expect(page.locator("html"))
    .toHaveAttribute("data-home-demo-catalog-removed", "true");

  await page.goBack();
  await expect(page).toHaveURL(firstWorkspaceUrl);
  await expect(page.locator(".loading-screen")).toHaveCount(0);
  await expect(subjectTab(page, "type"))
    .toHaveAttribute("aria-selected", "true");
  await expect(page.locator(".inspected-target"))
    .toContainText("Example.Widget");
  await expect(page.locator("html"))
    .toHaveAttribute("data-home-demo-resolve-count", "2");
});

test("Methods demo Workspace Forward restores its active retained destination", async ({
  page,
}) => {
  await openHomeDemo(page, "Methods", "package");
  const workspaceUrl = page.url();
  await expect(page).toHaveURL(
    /\/\?w=.+#workspace$/,
  );
  await expect(page.locator("html"))
    .toHaveAttribute("data-home-demo-resolve-count", "1");

  await page.goBack();
  await expect(page).toHaveURL("/demos");
  await expect(page.getByRole("heading", { name: "Demos", exact: true }))
    .toBeVisible();
  await page.goForward();

  await expect(page).toHaveURL(workspaceUrl);
  await expect(subjectTab(page, "type"))
    .toHaveAttribute("aria-selected", "true");
  await expect(page.locator(".inspected-target"))
    .toContainText("Example.Widget");
  await expect(page.locator("html"))
    .toHaveAttribute("data-home-demo-resolve-count", "1");
});

test("superseded Methods demo resolution cannot replace Back navigation", async ({
  page,
}) => {
  const id = await installHomeDemo(page, "Methods", "package", {
    holdResolutionAt: 1,
  });
  await page.goto("/");
  await openProductDestination(page, "demos");
  await expect(page).toHaveURL("/demos");
  await page.locator(`[data-workspace-demo="${id}"]`).click();
  await expect(page.locator("html")).toHaveAttribute(
    "data-home-demo-resolution-pending",
    "true",
  );

  await page.goBack();
  await expect(page).toHaveURL("/");
  await releaseFacade(page, "finish-home-demo-resolution");

  await expect(page.locator("html")).toHaveAttribute(
    "data-home-demo-resolution-pending",
    "false",
  );
  await expect(page).toHaveURL("/");
  await expect(page.locator("html"))
    .not.toHaveAttribute("data-home-demo-prepare");
  await expect(page.locator("#home-demos")).toBeVisible();
});

test("accepted Methods completion cannot replace Back navigation", async ({
  page,
}) => {
  const id = await installHomeDemo(page, "Methods", "package", {
    holdSurfaceAdmissionAt: 1,
  });
  await page.goto("/");
  await openProductDestination(page, "demos");
  await page.locator(`[data-workspace-demo="${id}"]`).click();
  await expect(page.locator("html")).toHaveAttribute(
    "data-home-demo-surface-admission-pending",
    "true",
  );

  await page.goBack();
  await expect(page).toHaveURL("/");
  await releaseFacade(page, "finish-home-demo-surface-admission");

  await expect(page.locator("html")).toHaveAttribute(
    "data-home-demo-surface-admission-pending",
    "false",
  );
  await expect(page).toHaveURL("/");
  await expect(page.locator("#home-demos")).toBeVisible();

  await openProductDestination(page, "workspace");
  const activeWorkspace = page.locator(".workspace-card.active");
  await expect(activeWorkspace).toContainText("package Methods");
  await activeWorkspace.click();
  await expect(page).toHaveURL(/\/\?w=.+#workspace$/);
  await expect(subjectTab(page, "type"))
    .toHaveAttribute("aria-selected", "true");
  await expect(page.locator(".inspected-target"))
    .toContainText("Example.Widget");
});

test("superseded retained Methods selection cannot replace Back navigation", async ({
  page,
}) => {
  const { packageId, platformId } =
    await installMethodsHomeDemos(page, { preparationPendingOrdinal: 3 });
  await page.goto("/demos");
  await page.locator(`[data-workspace-demo="${packageId}"]`).click();
  await page.getByRole("link", { name: "Demos", exact: true }).click();
  await page.locator(`[data-workspace-demo="${platformId}"]`).click();
  await expect(page).toHaveURL(
    /\/\?w=.+#workspace$/,
  );
  const platformUrl = page.url();
  await openProductDestination(page, "workspace");
  await expect(page.locator(".workspace-card.active"))
    .toContainText("platform Methods");

  await page.locator("[data-workspace-switch]").click();
  await expect(page.locator("html")).toHaveAttribute(
    "data-home-demo-preparation-pending",
    "true",
  );
  await page.goBack();
  await expect(page).toHaveURL(platformUrl);
  await releaseFacade(page, "finish-home-demo-preparation");

  await expect(page.locator("html")).toHaveAttribute(
    "data-home-demo-preparation-pending",
    "false",
  );
  await expect(page).toHaveURL(platformUrl);
  await expect(page.locator("html"))
    .toHaveAttribute("data-home-demo-resolve-count", "2");
  await openProductDestination(page, "workspace");
  await expect(page.locator(".workspace-card.active"))
    .toContainText("platform Methods");
});

test("superseded retained Methods deletion cannot activate its successor", async ({
  page,
}) => {
  const { packageId, platformId } =
    await installMethodsHomeDemos(page, { preparationPendingOrdinal: 3 });
  await page.goto("/demos");
  await page.locator(`[data-workspace-demo="${packageId}"]`).click();
  await page.getByRole("link", { name: "Demos", exact: true }).click();
  await page.locator(`[data-workspace-demo="${platformId}"]`).click();
  await expect(page).toHaveURL(
    /\/\?w=.+#workspace$/,
  );
  const platformUrl = page.url();
  await openProductDestination(page, "workspace");
  const activeRow = page.locator(".workspace-row").filter({
    has: page.locator(".workspace-card.active"),
  });
  await activeRow.locator("[data-workspace-delete]").click();
  await expect(page.locator("html")).toHaveAttribute(
    "data-home-demo-preparation-pending",
    "true",
  );

  await page.goBack();
  await expect(page).toHaveURL(platformUrl);
  await releaseFacade(page, "finish-home-demo-preparation");

  await expect(page.locator("html")).toHaveAttribute(
    "data-home-demo-preparation-pending",
    "false",
  );
  await expect(page).toHaveURL(platformUrl);
  await expect(page.locator("html"))
    .toHaveAttribute("data-home-demo-resolve-count", "2");
  await openProductDestination(page, "workspace");
  await expect(page.locator("[data-workspace-delete]")).toHaveCount(2);
  const preservedWorkspace = page.locator(".workspace-card.active");
  await expect(preservedWorkspace)
    .toContainText("platform Methods");
});

for (const staleOperation of ["selection", "deletion"] as const) {
  test(`superseded retained Methods ${staleOperation} suppresses preparation failure`, async ({
    page,
  }) => {
    const { packageId, platformId } =
      await installMethodsHomeDemos(page, {
        preparationPendingOrdinal: 3,
        preparationFailureOrdinal: 3,
      });
    await page.goto("/demos");
    await page.locator(`[data-workspace-demo="${packageId}"]`).click();
    await page.getByRole("link", { name: "Demos", exact: true }).click();
    await page.locator(`[data-workspace-demo="${platformId}"]`).click();
    await expect(page).toHaveURL(
      /\/\?w=.+#workspace$/,
    );
    const platformUrl = page.url();
    await openProductDestination(page, "workspace");

    if (staleOperation === "selection") {
      await page.locator("[data-workspace-switch]").click();
    } else {
      const activeRow = page.locator(".workspace-row").filter({
        has: page.locator(".workspace-card.active"),
      });
      await activeRow.locator("[data-workspace-delete]").click();
    }
    await expect(page.locator("html")).toHaveAttribute(
      "data-home-demo-preparation-pending",
      "true",
    );

    await page.goBack();
    await expect(page).toHaveURL(platformUrl);
    await releaseFacade(page, "finish-home-demo-preparation");

    await expect(page.locator("html")).toHaveAttribute(
      "data-home-demo-preparation-pending",
      "false",
    );
    await expect(page).toHaveURL(platformUrl);
    await expect(page.getByText("Fixture product demo preparation failed."))
      .toHaveCount(0);
    await expect(page.locator("html"))
      .toHaveAttribute("data-home-demo-resolve-count", "2");
    await openProductDestination(page, "workspace");
    await expect(page.locator("[data-workspace-delete]")).toHaveCount(2);
    await expect(page.locator(".workspace-card.active"))
      .toContainText("platform Methods");
  });
}

test("retained Methods deletion failure remains visible after reactivation", async ({
  page,
}) => {
  const { packageId, platformId } =
    await installMethodsHomeDemos(page, {
      preparationPendingOrdinal: 3,
      preparationFailureOrdinal: 3,
    });
  await page.goto("/demos");
  await page.locator(`[data-workspace-demo="${packageId}"]`).click();
  await page.getByRole("link", { name: "Demos", exact: true }).click();
  await page.locator(`[data-workspace-demo="${platformId}"]`).click();
  await openProductDestination(page, "workspace");

  const packageRow = page.locator(".workspace-row").filter({
    hasText: "package Methods",
  });
  const platformRow = page.locator(".workspace-row").filter({
    hasText: "platform Methods",
  });
  await platformRow.locator("[data-workspace-delete]").click();
  await expect(page.locator("html")).toHaveAttribute(
    "data-home-demo-preparation-pending",
    "true",
  );

  await packageRow.locator("[data-workspace-switch]").click();
  await expect(page.locator(".inspected-target"))
    .toContainText("Example.Widget");
  await openProductDestination(page, "workspace");
  await platformRow.locator("[data-workspace-switch]").click();
  await expect(page.locator(".inspected-target"))
    .toContainText("System.Text.Json");
  await expect(page.locator("html"))
    .toHaveAttribute("data-home-demo-preparation-count", "5");

  await releaseFacade(page, "finish-home-demo-preparation");
  await expect(page.locator("html")).toHaveAttribute(
    "data-home-demo-preparation-pending",
    "false",
  );
  await expect(page.locator(".inspected-target"))
    .toContainText("System.Text.Json");
  await openProductDestination(page, "workspace");
  await expect(page.locator(".query-notice-text")).toContainText(
    "Deleting retained Workspace failed: Fixture product demo preparation failed.",
  );
});

test("Methods demo definition capacity failure restores the catalog", async ({
  page,
}) => {
  const id = await installHomeDemo(page, "Methods", "package");
  const demo = page.locator(`[data-workspace-demo="${id}"]`);
  await page.goto("/demos");
  for (let index = 0; index < 4; index++) {
    await demo.click();
    await expect(page).toHaveURL(/\/\?w=.+#workspace$/);
    await page.getByRole("link", { name: "Demos", exact: true }).click();
    await expect(page).toHaveURL("/demos");
  }

  await demo.click();

  await expect(page).toHaveURL("/demos");
  await expect(page.locator(".loading-screen")).toHaveCount(0);
  await expect(page.getByRole("heading", { name: "Demos", exact: true }))
    .toBeVisible();
  await expect(page.locator(".query-notice-text"))
    .toContainText("retains at most 4 Workspace definitions");
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

test("Platform Methods demo retains its non-first engine surface while loading forwarders", async ({
  page,
}) => {
  await openHomeDemo(page, "Methods", "platform");
  const publishedUrl = page.url();
  await expect(page.locator("html"))
    .toHaveAttribute("data-forwarder-view", /.+/);
  await expect(page.getByText("Loading forwarded Types...", { exact: true }))
    .toHaveCount(0);
  await expect(page).toHaveURL(publishedUrl);
  await expect(subjectTab(page, "type"))
    .toHaveAttribute("aria-selected", "true");
  await expect(page.locator(".inspected-target"))
    .toContainText("Example.Widget");
  await expect(page.locator(
    `[data-type="${platformFocusType.id}"]`,
  )).toBeVisible();
  expect(new URL(publishedUrl).searchParams.has("w")).toBe(true);
  await expect(page.locator("html"))
    .toHaveAttribute("data-home-demo-definition-record-count", "4");
  await expect(page.locator("html"))
    .toHaveAttribute(
      "data-home-demo-prepared-subjects",
      /\["stj"\]/,
    );
  await openProductDestination(page, "workspace");
  await expect(page.locator(".workspace-card.active"))
    .toContainText("platform Methods");
});

test("Platform Call Graph demo publishes the exact Library and member", async ({
  page,
}) => {
  const share = await openHomeDemo(page, "Call Graph", "platform");
  const publishedUrl = page.url();
  await expect(page.locator("html"))
    .toHaveAttribute("data-forwarder-view", /.+/);
  await expect(page.getByText("Loading forwarded Types...", { exact: true }))
    .toHaveCount(0);
  await expect(page).toHaveURL(publishedUrl);
  await expect(subjectTab(page, "member"))
    .toHaveAttribute("aria-selected", "true");
  await expect(inspectorTab(page, "data-member-section", "call-graph"))
    .toHaveAttribute("aria-selected", "true");
  await expect(page.locator("#call-graph-diagram svg")).toBeVisible();
  await expect(page.locator(".inspected-target"))
    .toContainText("Example.Widget");
  await chooseInspector(page, "data-member-section", "source", "Source");
  await expect(page.locator(".source-result")).toContainText(
    "public void Run() {}",
  );
  await expect(page.locator("html")).toHaveAttribute(
    "data-platform-member-source-request",
    /platform-demo-context/,
  );
  expect(share).toMatchObject({
    view: {
      type: platformFocusType.id,
      memberAnchor: run.anchorDigest,
      section: "call-graph",
      libraries: [JSON.stringify(["netcore.app", "System.Text.Json.dll"])],
    },
  });

  await openProductDestination(page, "workspace");
  await page.locator("[data-workspace-platform]").click();
  await expect(subjectTab(page, "platform"))
    .toHaveAttribute("aria-selected", "true");
  await page.getByLabel("Platform version", { exact: true })
    .selectOption(alternatePlatformVersion);
  await expect(page.locator("#platform-version"))
    .toHaveValue(alternatePlatformVersion);
  await page.getByRole(
    "button",
    { name: /System.Text.Json Implementation/ },
  ).click();
  await chooseSubject(page, "type", "Type");
  await page.locator("#type-list [data-type]").first().click();
  await chooseSubject(page, "member", "Member");
  await chooseInspector(page, "data-member-section", "source", "Source");
  await expect(page.locator(".source-result")).toContainText(
    "public void Run() {}",
  );
  await expect(page.locator("html")).toHaveAttribute(
    "data-platform-member-source-request",
    new RegExp(alternatePlatformVersion.replaceAll(".", "\\.")),
  );
  await expect(page.locator("html")).toHaveAttribute(
    "data-platform-member-source-request",
    /,null\]$/,
  );
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
  await page.evaluate(() => {
    Reflect.deleteProperty(history, "pushState");
  });
  await openProductDestination(page, "workspace");
  await expect(page).toHaveURL(/\/\?w=.+#workspace$/);
  const activeWorkspace = page.locator(".workspace-card.active");
  await expect(activeWorkspace).toContainText("package Methods");
  await activeWorkspace.click();
  await expect(subjectTab(page, "type"))
    .toHaveAttribute("aria-selected", "true");
  await expect(page.locator(".inspected-target"))
    .toContainText("Example.Widget");
});

test("Activity Back restores focus on the Demos route", async ({ page }) => {
  await installHomeDemo(page, "Methods", "package");
  await page.goto("/demos");
  await expect(page.getByRole("heading", { name: "Demos", exact: true }))
    .toBeVisible();
  await page.keyboard.press("Control+k");
  await page.locator("#spotlight-input").fill("activity");
  await page.locator('[data-sl-scope="commands"]').click();
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
  await expect(page.locator("#package-changes-ecosystem")).toBeDisabled();
  await expect(page.getByRole("heading", {
    name: "Package Activity",
    exact: true,
  })).toBeFocused();
});
