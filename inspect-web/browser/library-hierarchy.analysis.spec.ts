import { expect, test, type Page } from "@playwright/test";
import {
  subjectTab,
  inspectorTab,
  chooseInspector,
  chooseSubject,
  selectLibrary,
  library,
  createType as type,
  core,
  other,
  empty,
  run,
  surface,
  platformVersion,
  installFacades,
  releaseFacade,
  root,
  openPlatform,
} from "./library-hierarchy.support.ts";

test.use({ viewport: { width: 900, height: 900 } });

async function openIntegrations(page: Page, location = root) {
  await page.goto(location);
  await selectLibrary(page, core.id);
  await chooseInspector(
    page,
    "data-library-lens",
    "analysis",
    "Analysis",
  );
  await page.locator('[data-analysis-mode="integrations"]').click();
  await expect(inspectorTab(page, "data-library-lens", "analysis"))
    .toHaveAttribute("aria-selected", "true");
}

test("Analysis opens on Relationships as its first tab", async ({ page }) => {
  await installFacades(page);
  await page.goto(root);
  await selectLibrary(page, core.id);
  await chooseInspector(
    page,
    "data-library-lens",
    "analysis",
    "Analysis",
  );

  const tabs = page.getByRole("tablist", { name: "Analysis views" })
    .getByRole("tab");
  await expect(tabs).toHaveText([
    "Relationships",
    "Dependencies",
    "Complexity",
    "Integrations",
    "Performance Triage",
    "Resource Triage",
  ]);
  await expect(tabs.first()).toHaveAttribute("aria-selected", "true");
  await expect(page.locator('[data-analysis-mode="relationships"]'))
    .toHaveAttribute("aria-selected", "true");
});

test("Dependencies auto-loads and reveals one selected relationship", async ({
  page,
}) => {
  await installFacades(page);
  await page.goto(root);
  await selectLibrary(page, core.id);
  await chooseInspector(
    page,
    "data-library-lens",
    "analysis",
    "Analysis",
  );
  await page.getByRole("tab", { name: "Dependencies", exact: true }).click();

  const frame = page.locator(".analysis-inspector");
  await expect(page.locator("html")).toHaveAttribute(
    "data-dependency-structure-request",
    core.id,
  );
  await expect(frame.locator("path.metrics-dependency-edge")).toHaveCount(1);
  await expect(frame.getByRole("checkbox", {
    name: "Include global namespace",
  })).not.toBeChecked();
  await expect(frame.locator(".metrics-dependency-node-label")
    .filter({ hasText: "(global)" })).toHaveCount(0);

  const edge = frame.locator("path.metrics-dependency-edge");
  await edge.focus();
  await edge.press("Enter");
  await expect(edge).toHaveAttribute("aria-pressed", "true");
  await expect(frame.locator("[data-dependency-edge-detail]:visible"))
    .toHaveCount(1);
  await expect(frame.locator("[data-dependency-edge-detail]:visible"))
    .toContainText("Example.Api");
  await expect(frame.locator("[data-dependency-edge-detail]:visible"))
    .toContainText("Example.Core");
});

async function selectPerformanceAnalysis(page: Page) {
  const performance = page.locator('[data-analysis-mode="performance"]');
  await performance.click();
  await expect(performance).toHaveAttribute("aria-selected", "true");
}

async function expectCompactAnalysisHeader(page: Page) {
  const frame = page.locator(".analysis-inspector");
  const header = frame.locator("header");
  const tabs = header.getByRole("tablist", { name: "Analysis views" });
  await expect(tabs).toBeVisible();
  for (const name of [
    "Relationships",
    "Dependencies",
    "Complexity",
    "Integrations",
    "Performance Triage",
    "Resource Triage",
  ]) {
    const tab = tabs.getByRole("tab", { name, exact: true });
    await tab.scrollIntoViewIfNeeded();
    // IntersectionObserver can round a fully visible edge by a fraction of a pixel.
    await expect(tab).toBeInViewport({ ratio: 0.999 });
    expect(await tab.evaluate(element => element.scrollWidth <= element.clientWidth)).toBe(true);
  }
  const headerBox = await header.boundingBox();
  const tabsBox = await tabs.boundingBox();
  const resultsBox = await frame.getByRole("tabpanel").boundingBox();
  expect(headerBox!.height).toBe(40);
  expect(Math.abs(tabsBox!.y - headerBox!.y)).toBeLessThanOrEqual(1);
  expect(tabsBox!.y + tabsBox!.height).toBeLessThanOrEqual(headerBox!.y + headerBox!.height);
  expect(headerBox!.x + headerBox!.width - tabsBox!.x - tabsBox!.width).toBeLessThanOrEqual(16);
  expect(Math.abs(resultsBox!.y - headerBox!.y - headerBox!.height)).toBeLessThanOrEqual(1);
  expect(await header.evaluate(element => element.scrollWidth <= element.clientWidth)).toBe(true);
  expect(await page.evaluate(() =>
    document.documentElement.scrollWidth <= document.documentElement.clientWidth)).toBe(true);
}

for (const width of [1440, 390, 320]) {
  test(`Analysis tabs preserve the Library and use manual keyboard activation at ${width}px`, async ({ page }, testInfo) => {
    await page.setViewportSize({ width, height: 900 });
    await installFacades(page);
    await openIntegrations(page);
    const frame = page.locator(".analysis-inspector");
    const integrations = frame.getByRole("tab", { name: "Integrations", exact: true });
    const performance = frame.getByRole("tab", { name: "Performance Triage", exact: true });
    await expect(page.locator('[data-library-lens="opportunities"]')).toHaveCount(0);
    await expect(frame.getByRole("tab", { name: "Opportunities", exact: true }))
      .toHaveCount(0);
    await expect(integrations).toHaveAttribute("aria-selected", "true");
    await expect(frame.locator(".signal-row")).toHaveCount(3);
    await expect(frame.locator(".opp-row")).toHaveCount(3);
    await expectCompactAnalysisHeader(page);
    await integrations.focus();
    await integrations.press("ArrowRight");
    await expect(performance).toBeFocused();
    await expect(integrations).toHaveAttribute("aria-selected", "true");
    await performance.press("Enter");
    await expect(performance).toHaveAttribute("aria-selected", "true");
    await expect(frame.locator("h1")).toHaveText("Analysis");
    await expect(frame.locator("footer")).toHaveCount(0);
    await expect(inspectorTab(page, "data-library-lens", "analysis"))
      .toHaveAttribute("aria-selected", "true");
    await expectCompactAnalysisHeader(page);
    await page.screenshot({ path: testInfo.outputPath("analysis-tabs-performance.png") });

    await performance.press("ArrowLeft");
    await expect(integrations).toBeFocused();
    await integrations.press("Space");
    await expect(integrations).toHaveAttribute("aria-selected", "true");
    await expect(frame.locator(".signal-row")).toHaveCount(3);
    await expect(frame.locator(".opp-row")).toHaveCount(3);
    await expect(frame.locator("footer")).toHaveCount(0);
    await page.screenshot({ path: testInfo.outputPath("integration-tabs-integrations.png") });

    await frame.locator("[data-opp-type]").first().click();
    await expect(subjectTab(page, "type")).toHaveAttribute("aria-selected", "true");
    await page.getByRole("button", { name: "Application menu", exact: true }).press("Alt+ArrowLeft");
    await expect(integrations).toHaveAttribute("aria-selected", "true");
    await page.getByRole("button", { name: "Application menu", exact: true }).press("Alt+ArrowRight");
    await expect(subjectTab(page, "type")).toHaveAttribute("aria-selected", "true");
    await chooseSubject(page, "library", "Library");
    await expect(integrations).toHaveAttribute("aria-selected", "true");
    await expect(frame.locator(".opp-row")).toHaveCount(3);
  });
}

test("Analysis tabs retain selected mode and focus when an inactive scan settles", async ({ page }) => {
  await installFacades(page, surface, [], "ready", "deferred", undefined, "deferred");
  await openIntegrations(page);
  const frame = page.locator(".analysis-inspector");
  const integrations = frame.getByRole("tab", { name: "Integrations", exact: true });
  await expect(frame).toContainText("Scanning integrations");
  await releaseFacade(page, "fixture-integrations-ready:asset:core");
  await expect(frame.locator(".signal-row")).toHaveCount(3);
  await expect(frame.locator("header")).toContainText("scanning");
  await expect(integrations).toHaveAttribute("aria-selected", "true");
  await releaseFacade(page, "fixture-opportunities-ready:asset:core");
  await expect(frame.locator(".opp-row")).toHaveCount(3);
  await expect(frame.locator(".signal-row")).toHaveCount(3);
});

async function openAnalysis(page: Page, location = root) {
  await page.goto(location);
  await selectLibrary(page, core.id);
  await chooseInspector(page, "data-library-lens", "analysis", "Analysis");
  await expect(inspectorTab(page, "data-library-lens", "analysis"))
    .toHaveAttribute("aria-selected", "true");
  await selectPerformanceAnalysis(page);
}

for (const width of [1440, 390]) {
  test(`production Analysis retains selected Library results at ${width}px`, async ({ page }, testInfo) => {
    await page.setViewportSize({ width, height: 900 });
    await installFacades(page);
    await openAnalysis(page);
    const frame = page.locator(".library-analysis-surface");
    await expect(frame.locator(".perf-row")).toHaveCount(3);
    await expect(frame.locator("header")).toContainText("3 members");
    await expect(frame.locator("header")).toContainText("6 opportunities");
    await expect(frame.locator(".perf-row")).toContainText(["Run", "Write", "PrivateWork"]);
    await expect(frame.locator("footer")).toHaveCount(0);
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
      const back = page.getByRole("button", { name: "Libraries", exact: true });
      await back.click();
      await expect(page.locator(".library-subject-list")).toBeFocused();
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
    await expect(frame.locator("footer")).toHaveCount(0);
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
        await expect(frame.locator(".perf-row")).toHaveCount(3);
        await expect(frame.locator("header")).toContainText("partial");
      } else {
        await expect(frame.locator("h2")).toHaveText(scenario === "empty"
          ? "No allocation hot spots" : scenario === "partial-empty"
            ? "Analysis incomplete" : "Analysis failed");
        await expect(frame.locator(".perf-row")).toHaveCount(0);
      }
      if (scenario.startsWith("partial")) {
        await expect(frame).toContainText("This library could not be analyzed completely");
        await expect(frame).not.toContainText("No allocation hot spots");
      }
      await expect(frame.locator("footer")).toHaveCount(0);
    });
  }

  test(`production Analysis keeps Platform Library selection outside the scroller at ${width}px`, async ({ page }) => {
    await page.setViewportSize({ width, height: 900 });
    await openPlatform(page, { mismatchedFile: true });
    await page.getByTitle("Inspect System.Text.Json", { exact: true }).click();
    await chooseInspector(page, "data-library-lens", "analysis", "Analysis");
    await selectPerformanceAnalysis(page);
    const frame = page.locator(".library-analysis-surface");
    await expect(frame.locator(".perf-row")).toHaveCount(3);
    const picker = frame.locator(".library-analysis-controls select");
    await expect(picker).toBeVisible();
    await expect(picker).toHaveValue("System.Text.Json");
    await expect(frame.locator("footer")).toHaveCount(0);
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
  await expect(subjectTab(page, "member"))
    .toHaveAttribute("aria-selected", "true");
  await expect(inspectorTab(page, "data-member-section", "facts")).toHaveAttribute("aria-selected", "true");
  await expect(page.getByRole("button", { name: "Explore", exact: true })).toBeEnabled();

  await page.locator("[data-nav-member]").filter({ hasText: "Run" }).click();
  await expect(page.locator("#member-surface-title")).toHaveText("Run");
  await expect(page.locator(".member-surface-head"))
    .toContainText("method · 1 of 1");
  await expect(page.locator(".member-surface-list .overload-row"))
    .toHaveCount(0);
  expect(await page.locator("html").getAttribute(
    "data-member-group-document-request",
  )).toBeNull();
  await expect(inspectorTab(page, "data-member-section", "overview")).toHaveAttribute("aria-selected", "true");
});

test("production Analysis opens a private ranked member through all accessibility", async ({
  page,
}) => {
  const privateWork = {
    ...run,
    name: "PrivateWork",
    signature: "private void PrivateWork()",
    accessibility: "private",
    stableSelector: "PrivateWork",
    canonicalSignature: "M:Example.Widget.PrivateWork",
    documentationId: "M:Example.Widget.PrivateWork",
    graphSelectorKey: "PrivateWork",
    metadataToken: 0x06000003,
    declarationMetadataToken: 0x06000003,
    bodySelectors: [{
      token: 0x06000003,
      memberName: "PrivateWork",
      selectorKey: "PrivateWork",
    }],
    isHidden: true,
  };
  await installFacades(page, {
    ...surface,
    types: surface.types.map(candidate =>
      candidate.definitionId === "Example.Widget"
        ? {
            ...candidate,
            members: candidate.members + 1,
            api: [...candidate.api, privateWork],
          }
        : candidate),
    totalMembers: surface.totalMembers + 1,
  });
  await openAnalysis(page);
  await page.locator(".library-analysis-surface .perf-row")
    .filter({ hasText: "PrivateWork" })
    .click();

  await expect(subjectTab(page, "member"))
    .toHaveAttribute("aria-selected", "true");
  await expect(page.locator("[data-member-access-filter]"))
    .toHaveValue("all");
  await expect(page.locator("#member-surface-title"))
    .toHaveText("PrivateWork");
  await expect(inspectorTab(page, "data-member-section", "facts"))
    .toHaveAttribute("aria-selected", "true");
  await expect(page.locator("html"))
    .toHaveAttribute(
      "data-performance-type-member-population-request",
      JSON.stringify([
        "Example.Package",
        "1.0.0",
        "net10.0",
        "Example.Core.dll",
        "Example.ImplementationOnlyWorker",
        "csharp",
        "all",
      ]),
    );
  expect(await page.locator("html").getAttribute(
    "data-type-member-population-request",
  )).toBeNull();
});

test("ranked Analysis members replace sticky private intent with all accessibility", async ({
  page,
}) => {
  await installFacades(page);
  await page.goto(root);
  await selectLibrary(page, core.id);
  await chooseSubject(page, "type", "Type");
  await page.locator(
    '#type-list [data-type="asset:core:Example.Widget"]',
  ).click();
  await page.locator("#member-filter-summary").click();
  const accessibility = page.locator("[data-member-access-filter]");
  await expect(accessibility.locator('option[value="public"]'))
    .toContainText("·");
  await accessibility.selectOption("private");
  await expect(accessibility).toHaveValue("private");

  await chooseSubject(page, "library", "Library");
  await chooseInspector(page, "data-library-lens", "analysis", "Analysis");
  await selectPerformanceAnalysis(page);
  await page.locator(".library-analysis-surface .perf-row").first().click();

  await expect(subjectTab(page, "member"))
    .toHaveAttribute("aria-selected", "true");
  await expect(page.locator("[data-member-access-filter]"))
    .toHaveValue("all");
  await expect(page.locator("#inspector-panel")).toContainText("Analysis summary");
});

test("ranked Analysis activation does not outlive newer metadata spelling", async ({
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
    { deferTypeMemberPopulation: true },
  );
  await openAnalysis(page);

  await page.locator(".library-analysis-surface .perf-row").first().click();
  await expect(page.locator("html")).toHaveAttribute(
    "data-performance-type-member-population-request",
    /"csharp","all"\]$/,
  );
  await page.locator("#member-filter-summary").click();
  await page.locator("[data-member-spelling]").selectOption("metadata");
  await expect(page.locator("html")).toHaveAttribute(
    "data-type-member-population-request",
    /"metadata","all"\]$/,
  );

  await releaseFacade(page, "finish-type-member-population");

  await expect(page.locator("[data-member-spelling]"))
    .toHaveValue("metadata");
  await expect(subjectTab(page, "type"))
    .toHaveAttribute("aria-selected", "true");
  await expect(subjectTab(page, "member"))
    .toHaveAttribute("aria-selected", "false");
});

test("ranked Analysis activation does not outlive A to B to A Type navigation", async ({
  page,
}) => {
  const secondNeighbor = type("Example.SecondNeighbor", other);
  await installFacades(
    page,
    {
      ...surface,
      types: [...surface.types, secondNeighbor],
      accessibility: surface.accessibility.map(bucket =>
        bucket.id === "public" ? { ...bucket, count: 3 } : bucket),
      totalMembers: 3,
    },
    [],
    "ready",
    "ready",
    undefined,
    "ready",
    "ready",
    undefined,
    { deferTypeMemberPopulation: true },
  );
  await page.goto(root);
  await selectLibrary(page, other.id);
  await chooseInspector(page, "data-library-lens", "analysis", "Analysis");
  await expect(inspectorTab(page, "data-library-lens", "analysis"))
    .toHaveAttribute("aria-selected", "true");
  await selectPerformanceAnalysis(page);

  await page.locator(".library-analysis-surface .perf-row").first().click();
  await expect(page.locator("html")).toHaveAttribute(
    "data-performance-type-member-population-request",
    /"Example.Neighbor","csharp","all"\]$/,
  );
  await expect(subjectTab(page, "type"))
    .toHaveAttribute("aria-selected", "true");

  await page.locator(
    '#type-list [data-type="asset:other:Example.SecondNeighbor"]',
  ).click();
  await expect(page.locator("html")).toHaveAttribute(
    "data-type-member-population-request",
    /"Example.SecondNeighbor","csharp","all"\]$/,
  );
  await page.locator(
    '#type-list [data-type="asset:other:Example.Neighbor"]',
  ).click();
  await expect(page.locator("html")).toHaveAttribute(
    "data-type-member-population-request",
    /"Example.Neighbor","csharp","all"\]$/,
  );

  await releaseFacade(page, "finish-type-member-population");

  await expect(subjectTab(page, "type"))
    .toHaveAttribute("aria-selected", "true");
  await expect(subjectTab(page, "member"))
    .toHaveAttribute("aria-selected", "false");
});

test("different family navigation leaves exact Facts for the shared document", async ({
  page,
}) => {
  const widget = surface.types.find(
    candidate => candidate.definitionId === "Example.Widget",
  );
  if (!widget) throw new Error("The Analysis fixture has no Widget Type.");
  const secondRun = {
    ...run,
    signature: "public void Run(int value)",
    metadataToken: 0x06000002,
    declarationMetadataToken: 0x06000002,
    stableSelector: "Run:2",
    anchorDigest: "widget-run-two",
    canonicalSignature: "M:Example.Widget.Run(System.Int32)",
    graphSelectorKey: "Run:2",
    bodySelectors: [{
      token: 0x06000002,
      memberName: "Run",
      selectorKey: "Run:2",
    }],
  };
  const stop = {
    ...run,
    name: "Stop",
    signature: "public void Stop()",
    metadataToken: 0x06000003,
    declarationMetadataToken: 0x06000003,
    documentationId: "M:Example.Widget.Stop",
    stableSelector: "Stop:1",
    anchorDigest: "widget-stop-one",
    canonicalSignature: "M:Example.Widget.Stop",
    graphSelectorKey: "Stop:1",
    bodySelectors: [{
      token: 0x06000003,
      memberName: "Stop",
      selectorKey: "Stop:1",
    }],
  };
  const secondStop = {
    ...stop,
    signature: "public void Stop(int code)",
    metadataToken: 0x06000004,
    declarationMetadataToken: 0x06000004,
    stableSelector: "Stop:2",
    anchorDigest: "widget-stop-two",
    canonicalSignature: "M:Example.Widget.Stop(System.Int32)",
    graphSelectorKey: "Stop:2",
    bodySelectors: [{
      token: 0x06000004,
      memberName: "Stop",
      selectorKey: "Stop:2",
    }],
  };
  await installFacades(page, {
    ...surface,
    assemblies: surface.assemblies.map(assembly =>
      assembly.id === core.id
        ? { ...assembly, publicMembers: 4 }
        : assembly),
    types: surface.types.map(candidate =>
      candidate.id === widget.id
        ? {
            ...candidate,
            members: 4,
            api: [run, secondRun, stop, secondStop],
          }
        : candidate),
    accessibility: surface.accessibility.map(bucket =>
      bucket.id === "public" ? { ...bucket, count: 5 } : bucket),
    totalMembers: 5,
  });
  await openAnalysis(page);
  await page.locator(".library-analysis-surface .perf-row").first().click();
  await chooseInspector(
    page,
    "data-member-section",
    "facts",
    "Analysis",
  );

  await page.locator("[data-nav-member]").filter({ hasText: "Stop" }).click();

  await expect(page.locator("#member-surface-title")).toHaveText("Stop");
  await expect(page.locator(".member-surface-head"))
    .toContainText("2 overloads");
  await expect(page.locator(".member-surface-list .overload-row"))
    .toHaveCount(2);
  expect(await page.locator("html").getAttribute(
    "data-member-group-document-request",
  )).toBeNull();

  await page.locator(".member-surface-list .overload-row").first().click();
  await page.keyboard.press("Backspace");
  await expect(page.locator("#member-surface-title")).toHaveText("Stop");
  await expect(page.locator(".member-surface-list .overload-row"))
    .toHaveCount(2);
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
  await expect(page.locator("#inspector-panel > section > footer")).toHaveCount(0);
  await releaseFacade(page, "fixture-analysis-ready:asset:core");
  await expect(page.locator(".library-analysis-scroll .perf-row")).toHaveCount(3);
  await chooseSubject(page, "package", "Package");
  await selectLibrary(page, other.id);
  await chooseInspector(page, "data-library-lens", "analysis", "Analysis");
  await selectPerformanceAnalysis(page);
  await expect(page.locator(".library-analysis-surface")).toContainText("Analyzing allocations");
  await expect(page.locator("#inspector-panel > section > footer")).toHaveCount(0);
  await expect(page.locator(".library-analysis-surface")).not.toContainText(core.name);
  await releaseFacade(page, "fixture-analysis-ready:asset:other");
  await expect(page.locator(".library-analysis-scroll .perf-name").first())
    .toContainText("Neighbor.Run");
});

for (const width of [1440, 390]) {
  test(`production Integrations retains suggested entries at ${width}px`, async ({ page }, testInfo) => {
    await page.setViewportSize({ width, height: 900 });
    await installFacades(page);
    await openIntegrations(page);
    const frame = page.locator(".library-integrations-surface");
    await expect(frame.locator(".opp-row")).toHaveCount(3);
    await expect(frame.locator("header")).toContainText("4 categories");
    await expect(frame.locator("header")).toContainText("3 detected");
    await expect(frame.locator("header")).toContainText("3 suggested");
    await expect(frame.locator("footer")).toHaveCount(0);
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
    await page.screenshot({ path: testInfo.outputPath("suggested-integrations.png") });
    if (width === 390) {
      const back = page.getByRole("button", { name: "Libraries", exact: true });
      await back.click();
      await expect(page.locator(".library-subject-list")).toBeFocused();
      await page.getByRole("button", { name: "Show details", exact: true }).click();
      await expect(back).toBeFocused();
      await expect(frame).toBeVisible();
    }
  });

  test(`production Integrations contains long suggested fields and keeps its frame while scrolling at ${width}px`, async ({ page }) => {
    await page.setViewportSize({ width, height: 900 });
    const longCore = library(core.id, "Example." + "LongLibraryName".repeat(25), 1);
    await installFacades(page, {
      ...surface, assemblies: [longCore], types: [type("Example.Widget", longCore)], totalMembers: 1,
    }, [], "ready", "ready", undefined, "long");
    await openIntegrations(page);
    const frame = page.locator(".library-integrations-surface");
    await expect(frame.locator(".opp-row")).toHaveCount(81);
    const header = await frame.locator("header").boundingBox();
    const scroll = frame.locator(".library-integrations-scroll");
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
    await expect(frame.locator("footer")).toHaveCount(0);
  });

  for (const scenario of ["empty", "partial", "partial-empty", "query-error"] as const) {
    test(`production Integrations retains its suggested ${scenario} state at ${width}px`, async ({ page }) => {
      await page.setViewportSize({ width, height: 900 });
      await installFacades(page, surface, [], "ready", "ready", undefined, scenario);
      await openIntegrations(page);
      const frame = page.locator(".library-integrations-surface");
      if (scenario === "partial") {
        await expect(frame.locator(".opp-row")).toHaveCount(3);
        await expect(frame.locator("header")).toContainText("partial");
      } else {
        await expect(frame.locator(".opp-row")).toHaveCount(0);
        await expect(frame.locator(".signal-row")).toHaveCount(3);
        if (scenario === "empty") {
          await expect(frame.locator("header")).toContainText("0 suggested");
          await expect(frame.locator(".metadata-warning")).toHaveCount(0);
        } else {
          await expect(frame.locator(".metadata-warning")).toBeVisible();
        }
      }
      if (scenario.startsWith("partial")) {
        await expect(frame).toContainText("A library participant could not be inspected.");
      } else if (scenario === "query-error") {
        await expect(frame).toContainText("Suggested integrations: Opportunity query unavailable.");
      }
      await expect(frame.locator("footer")).toHaveCount(0);
    });
  }

  test(`production Integrations follows Platform Library navigation at ${width}px`, async ({ page }) => {
    await page.setViewportSize({ width, height: 900 });
    await openPlatform(page, { mismatchedFile: true });
    await page.getByTitle("Inspect System.Facade", { exact: true }).click();
    await chooseInspector(
      page,
      "data-library-lens",
      "analysis",
      "Analysis",
    );
    await page.locator('[data-analysis-mode="integrations"]').click();
    const frame = page.locator(".library-integrations-surface");
    await expect(frame.locator(".opp-row")).toHaveCount(3);
    const picker = frame.locator(".library-integrations-controls select");
    await expect(picker).toBeVisible();
    await expect(picker).toHaveValue("System.Facade");
    await picker.selectOption("System.Text.Json");
    await expect(frame.locator(".opp-type-ns").nth(1)).toContainText("System.Text.Json");
    await expect(frame.locator("footer")).toHaveCount(0);
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
    const content = await frame.locator(".library-integrations-scroll").boundingBox();
    expect(header!.y + header!.height).toBeLessThanOrEqual(content!.y + 1);
  });
}

test("stale Platform suggested integration acquisition cannot replace a newer family selection", async ({ page }) => {
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
  await chooseInspector(
    page,
    "data-library-lens",
    "analysis",
    "Analysis",
  );
  await page.locator('[data-analysis-mode="integrations"]').click();
  const picker = page.locator(
    ".library-integrations-controls .platform-library-select",
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
  await expect(subjectTab(page, "type")).toHaveAttribute(
    "aria-selected",
    "true",
  );

  await releaseFacade(page, "finish-platform-library");
  await chooseSubject(page, "library", "Library");
  await chooseInspector(page, "data-library-lens", "metadata", "Metadata");
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

test("production Integrations keeps deferred suggested results out of the incoming scan", async ({ page }) => {
  await installFacades(page, surface, [], "ready", "ready", undefined, "deferred");
  await openIntegrations(page);
  await expect(page.locator(".library-integrations-surface header")).toContainText("scanning");
  await expect(page.locator("#inspector-panel > section > footer")).toHaveCount(0);
  await releaseFacade(page, "fixture-opportunities-ready:asset:core");
  await expect(page.locator(".library-integrations-scroll .opp-row")).toHaveCount(3);
  await chooseSubject(page, "package", "Package");
  await selectLibrary(page, other.id);
  await chooseInspector(
    page,
    "data-library-lens",
    "analysis",
    "Analysis",
  );
  await page.locator('[data-analysis-mode="integrations"]').click();
  await expect(page.locator(".library-integrations-surface header")).toContainText("scanning");
  await expect(page.locator("#inspector-panel > section > footer")).toHaveCount(0);
  await expect(page.locator(".library-integrations-surface")).not.toContainText(core.name);
  await releaseFacade(page, "fixture-opportunities-ready:asset:other");
  await expect(page.locator(".library-integrations-scroll .opp-type-ns").nth(1)).toContainText(other.name);
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
    await expect(frame.locator("header")).toContainText("4 categories");
    await expect(frame.locator("header")).toContainText("3 detected");
    await expect(frame.locator("header")).toContainText("3 suggested");
    await expect(frame.locator(".opp-row")).toHaveCount(3);
    await expect(frame.locator("footer")).toHaveCount(0);
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
      const back = page.getByRole("button", { name: "Libraries", exact: true });
      await back.click();
      await expect(page.locator(".library-subject-list")).toBeFocused();
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
    await expect(frame.locator('[role="listitem"]').last()).toBeInViewport();
    expect(await frame.locator("header").boundingBox()).toEqual(header);
    await expect(frame.locator("footer")).toHaveCount(0);
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
        await expect(frame.locator(".signal-row")).toHaveCount(0);
        await expect(frame.locator(".opp-row")).toHaveCount(3);
        if (scenario === "empty") {
          await expect(frame.locator("header")).toContainText("0 detected");
          await expect(frame.locator(".metadata-warning")).toHaveCount(0);
        } else {
          await expect(frame.locator(".metadata-warning")).toBeVisible();
        }
      }
      if (scenario.startsWith("partial")) {
        await expect(frame).toContainText("A library participant could not be inspected.");
      } else if (scenario === "query-error") {
        await expect(frame).toContainText("Detected integrations: Integration query unavailable.");
      }
      await expect(frame.locator("footer")).toHaveCount(0);
    });
  }

  test(`production Integrations keeps Platform Library selection outside the scroller at ${width}px`, async ({ page }) => {
    await page.setViewportSize({ width, height: 900 });
    await openPlatform(page);
    await page.getByTitle("Inspect System.Text.Json", { exact: true }).click();
    await chooseInspector(
      page,
      "data-library-lens",
      "analysis",
      "Analysis",
    );
    await page.locator('[data-analysis-mode="integrations"]').click();
    const frame = page.locator(".library-integrations-surface");
    await expect(frame.locator(".signal-row")).toHaveCount(3);
    const picker = frame.locator(".library-integrations-controls select");
    await expect(picker).toBeVisible();
    await expect(picker).toHaveValue("System.Text.Json");
    await expect(frame.locator(".signal-ns").first()).toContainText("System.Text.Json");
    await chooseSubject(page, "platform", "Platform");
    await page.getByTitle("Inspect System.Facade", { exact: true }).click();
    await chooseInspector(
      page,
      "data-library-lens",
      "analysis",
      "Analysis",
    );
    await page.locator('[data-analysis-mode="integrations"]').click();
    await expect(frame.locator(".signal-ns").first()).toContainText("System.Facade");
    await expect(frame.locator("footer")).toHaveCount(0);
    await expect(page.locator("html")).toHaveAttribute("data-platform-integration-request", "System.Facade.dll:netcore.app");
  });
}

test("production Integrations keeps deferred Library results out of the incoming scan", async ({ page }) => {
  await installFacades(page, surface, [], "ready", "deferred");
  await openIntegrations(page);
  await expect(page.locator(".library-integrations-surface header")).toContainText("scanning");
  await expect(page.locator("#inspector-panel > section > footer")).toHaveCount(0);
  await releaseFacade(page, "fixture-integrations-ready:asset:core");
  await expect(page.locator(".library-integrations-scroll .signal-row")).toHaveCount(3);
  await chooseSubject(page, "package", "Package");
  await selectLibrary(page, other.id);
  await chooseInspector(
    page,
    "data-library-lens",
    "analysis",
    "Analysis",
  );
  await page.locator('[data-analysis-mode="integrations"]').click();
  await expect(page.locator(".library-integrations-surface header")).toContainText("scanning");
  await expect(page.locator("#inspector-panel > section > footer")).toHaveCount(0);
  await expect(page.locator(".library-integrations-surface")).not.toContainText(core.name);
  await releaseFacade(page, "fixture-integrations-ready:asset:other");
  await expect(page.locator(".library-integrations-scroll .signal-ns").first()).toContainText(other.name);
});

async function openReferences(page: Page) {
  await page.goto(root);
  await selectLibrary(page, core.id);
  await chooseInspector(page, "data-library-lens", "references", "References");
  await expect(inspectorTab(page, "data-library-lens", "references"))
    .toHaveAttribute("aria-selected", "true");
}

for (const width of [1440, 390]) {
  test(`production References fills the pane and retains context at ${width}px`, async ({ page }, testInfo) => {
    await page.setViewportSize({ width, height: 900 });
    await installFacades(page);
    await openReferences(page);
    const frame = page.locator(".library-references-surface");
    await expect(frame.locator(".dep-list li")).toHaveCount(1);
    await expect(frame.locator(".reference-graph-section .graph-viewport"))
      .toBeVisible();
    await expect(frame.locator(".reference-graph-section")).toContainText(
      "inspected assembly");
    await expect(frame.locator(".reference-list-section")).toContainText(
      "Assembly references");
    await expect(frame.locator("header")).toContainText("1 direct reference");
    await expect(frame.locator("footer")).toHaveCount(0);
    await expect(page.locator("#inspector-panel > .type-heading")).toHaveCount(0);
    await expect(frame.locator("h2")).toHaveCount(2);
    const panelBox = await page.locator("#inspector-panel").boundingBox();
    const frameBox = await frame.boundingBox();
    expect(panelBox).not.toBeNull();
    expect(frameBox).not.toBeNull();
    expect(Math.abs(frameBox!.width - panelBox!.width)).toBeLessThanOrEqual(2);
    expect(Math.abs(frameBox!.height - panelBox!.height)).toBeLessThanOrEqual(2);
    const listBox = await frame.locator(".dep-list").boundingBox();
    expect(listBox!.x).toBeGreaterThan(frameBox!.x);
    expect(listBox!.x + listBox!.width)
      .toBeLessThan(frameBox!.x + frameBox!.width);
    await page.screenshot({ path: testInfo.outputPath("references.png") });
    if (width === 390) {
      const back = page.getByRole("button", { name: "Libraries", exact: true });
      await expect(back).toBeVisible();
      await back.click();
      await expect(page.locator(".library-subject-list")).toBeFocused();
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
    await expect(frame.locator(".reference-graph-section")).toContainText(
      "Reference graph shows 79 of 80 direct references");
    await expect(frame.locator("header")).toContainText("80 direct references");
    await expect(frame.locator("footer")).toHaveCount(0);
    const headerBox = await frame.locator("header").boundingBox();
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
    await expect(frame.locator("footer")).toHaveCount(0);
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
      await expect(frame.locator("footer")).toHaveCount(0);
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
  await expect(page.locator("#inspector-panel > section > footer")).toHaveCount(0);
  await releaseFacade(page, "fixture-references-ready:asset:core");
  await expect(page.locator(".library-references-scroll")).toContainText("Example.Core.Dependency");
  await chooseSubject(page, "package", "Package");
  await selectLibrary(page, other.id);
  await chooseInspector(page, "data-library-lens", "references", "References");
  await expect(page.locator(".library-references-surface")).toContainText("Reading direct AssemblyRef rows");
  await expect(page.locator("#inspector-panel > section > footer")).toHaveCount(0);
  await expect(page.locator(".library-references-surface")).not.toContainText("Example.Core");
  await releaseFacade(page, "fixture-references-ready:asset:other");
  await expect(page.locator(".library-references-scroll")).toContainText("Example.Other.Dependency");
});

test("production References opens a uniquely resolved Workspace Library destination", async ({ page }) => {
  const target = library("asset:reference-target", "Reference.Target", 1);
  await installFacades(page, {
    ...surface,
    assemblies: [...surface.assemblies, target],
    types: [...surface.types, type("Example.ReferenceTarget", target)],
    accessibility: surface.accessibility.map(bucket =>
      bucket.id === "public" ? { ...bucket, count: 3 } : bucket),
    totalMembers: 3,
  }, [], "workspace-library");
  await page.goto(root);
  await selectLibrary(page, core.id);
  await chooseInspector(page, "data-library-lens", "references", "References");

  await page.getByRole(
    "button",
    { name: "Reference.Target", exact: true },
  ).click();

  await expect(page.locator(".inspected-target")).toContainText("Example.Package");
  await expect(subjectTab(page, "library")).toHaveAttribute(
    "aria-selected",
    "true");
  await expect(page.locator(".library-overview-surface h1"))
    .toHaveText("Reference.Target");
});

test("Library navigation exposes the complete long Library name on hover", async ({ page }) => {
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
  await chooseSubject(page, "library", "Library");
  const row = page.locator(
    `.library-subject-list [data-library-subject="${core.id}"]`);
  await expect(row).toBeVisible();
  const name = row.locator(".type-name");
  await expect(name).toHaveText(longLibrary.name);
  const location = page.url();
  await row.hover();
  await expect(row).toHaveAttribute("title", `Inspect ${longLibrary.name}`);
  await expect(page).toHaveURL(location);
});

test("triage rows have no collapsed code control and preserve Library Analysis", async ({ page }) => {
  await installFacades(page);
  await openAnalysis(page);
  await expect(page.locator(".library-performance-surface details")).toHaveCount(0);
  await expect(page.locator(".library-performance-surface [data-triage-code]")).toHaveCount(0);
  await expect(inspectorTab(page, "data-library-lens", "analysis"))
    .toHaveAttribute("aria-selected", "true");
});


test("Resource rows open member Resource Triage with detailed evidence and Explore", async ({ page }) => {
  const stop = { ...run, name: "Stop", stableSelector: "Stop", graphSelectorKey: "Stop", signature: "void Stop()", documentationId: "M:Example.Widget.Stop" };
  await installFacades(page, { ...surface, types: surface.types.map(candidate => candidate.definitionId === "Example.Widget" ? { ...candidate, queryId: "Example.Widget.QuerySpelling", members: 2, api: [run, stop] } : candidate) });
  await openAnalysis(page);
  await page.getByRole("tab", { name: "Resource Triage", exact: true }).click();
  const list = page.locator(".library-resource-triage-surface");
  await expect(list.locator(".perf-name")).toHaveText("Example.Widget.Run");
  await expect(list.locator(".perf-shape").first()).toHaveText("Pool churn on exception");
  await expect(list).not.toContainText("IL_");
  await expect(list.locator(".library-analysis-note")).toHaveCount(0);
  await list.locator(".perf-row").click();
  await expect(inspectorTab(page, "data-member-section", "resource-triage")).toHaveAttribute("aria-selected", "true");
  await expect(page.locator("#member-surface-title")).toHaveText("Pool churn on exception");
  await expect(page.getByRole("tab", { name: "Resource Triage", exact: true })).toHaveAttribute("aria-selected", "true");
  await expect(page.locator(".member-resource-triage")).not.toContainText("Resource cleanup");
  await expect(page.locator(".working-surface-actions #explore-source")).toBeVisible();
  const exploreBounds = await page.locator("#explore-source").boundingBox();
  const headingBounds = await page.locator("#member-surface-title").boundingBox();
  expect(exploreBounds!.y).toBeLessThan(headingBounds!.y);
  await expect(page.locator(".member-resource-triage")).toContainText("IL_0007");
  await expect(page.locator(".member-resource-triage")).toContainText("System.IO.Stream.Read");
  await page.getByRole("button", { name: "Explore", exact: true }).click();
  await expect(page.locator("#annotated-modal-title")).toBeVisible();
  await page.getByRole("button", { name: "Close", exact: true }).click();
  await expect(page.getByRole("button", { name: "Explore", exact: true })).toBeFocused();
  await page.locator("[data-nav-member]").filter({ hasText: "Stop" }).click();
  await chooseInspector(page, "data-member-section", "facts", "Analysis");
  await expect(page.locator("#member-surface-title")).toHaveText("Stop");
  await expect(page.locator(".member-resource-triage")).toHaveCount(0);
});
