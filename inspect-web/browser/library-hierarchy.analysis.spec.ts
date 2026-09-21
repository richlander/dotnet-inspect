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
    "integrations",
    "Integrations",
  );
  await expect(inspectorTab(page, "data-library-lens", "integrations"))
    .toHaveAttribute("aria-selected", "true");
}

async function openOpportunities(page: Page, location = root) {
  await openIntegrations(page, location);
  await page.locator('[data-integration-mode="opportunities"]').click();
  await expect(page.locator('[data-integration-mode="opportunities"]'))
    .toHaveAttribute("aria-selected", "true");
}

async function expectCompactIntegrationHeader(page: Page) {
  const frame = page.locator(".integration-inspector");
  const header = frame.locator("header");
  const tabs = header.getByRole("tablist", { name: "Integration views" });
  await expect(tabs).toBeVisible();
  for (const name of ["Integrations", "Opportunities"]) {
    const tab = tabs.getByRole("tab", { name, exact: true });
    await expect(tab).toBeInViewport({ ratio: 1 });
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
  test(`Integration tabs preserve the Library and use manual keyboard activation at ${width}px`, async ({ page }, testInfo) => {
    await page.setViewportSize({ width, height: 900 });
    await installFacades(page);
    await openIntegrations(page);
    const frame = page.locator(".integration-inspector");
    const integrations = frame.getByRole("tab", { name: "Integrations", exact: true });
    const opportunities = frame.getByRole("tab", { name: "Opportunities", exact: true });
    await expect(page.locator('[data-library-lens="opportunities"]')).toHaveCount(0);
    await expect(integrations).toHaveAttribute("aria-selected", "true");
    await expect(opportunities).toBeInViewport({ ratio: 1 });
    await expect(frame.locator(".signal-row")).toHaveCount(3);
    await expectCompactIntegrationHeader(page);
    expect(await page.locator("html").getAttribute("data-opportunity-request")).toBeNull();
    await integrations.focus();
    await integrations.press("ArrowRight");
    await expect(opportunities).toBeFocused();
    await expect(integrations).toHaveAttribute("aria-selected", "true");
    expect(await page.locator("html").getAttribute("data-opportunity-request")).toBeNull();
    await opportunities.press("Enter");
    await expect(opportunities).toHaveAttribute("aria-selected", "true");
    await expect(opportunities).toBeFocused();
    await expect(frame.locator(".opp-row")).toHaveCount(3);
    await expect(frame.locator("h1")).toHaveText("Integrations");
    await expect(frame.locator("footer")).toContainText(core.asset);
    await expect(inspectorTab(page, "data-library-lens", "integrations"))
      .toHaveAttribute("aria-selected", "true");
    await expectCompactIntegrationHeader(page);
    await page.screenshot({ path: testInfo.outputPath("integration-tabs-opportunities.png") });

    await opportunities.press("Home");
    await expect(integrations).toBeFocused();
    await expect(opportunities).toHaveAttribute("aria-selected", "true");
    await integrations.press("Space");
    await expect(integrations).toHaveAttribute("aria-selected", "true");
    await expect(frame.locator(".signal-row")).toHaveCount(3);
    await expect(frame.locator("footer")).toContainText(core.asset);
    await page.screenshot({ path: testInfo.outputPath("integration-tabs-integrations.png") });
    await integrations.press("End");
    await opportunities.press("Space");
    await expect(frame.locator(".opp-row")).toHaveCount(3);

    await frame.locator("[data-opp-type]").first().click();
    await expect(subjectTab(page, "type")).toHaveAttribute("aria-selected", "true");
    await page.getByRole("button", { name: "Application menu", exact: true }).press("Alt+ArrowLeft");
    await expect(opportunities).toHaveAttribute("aria-selected", "true");
    await page.getByRole("button", { name: "Application menu", exact: true }).press("Alt+ArrowRight");
    await expect(subjectTab(page, "type")).toHaveAttribute("aria-selected", "true");
    await chooseSubject(page, "library", "Library");
    await expect(opportunities).toHaveAttribute("aria-selected", "true");
    await expect(frame.locator(".opp-row")).toHaveCount(3);
  });
}

test("Integration tabs retain selected mode and focus when an inactive scan settles", async ({ page }) => {
  await installFacades(page, surface, [], "ready", "deferred", undefined, "deferred");
  await openIntegrations(page);
  const frame = page.locator(".integration-inspector");
  const integrations = frame.getByRole("tab", { name: "Integrations", exact: true });
  const opportunities = frame.getByRole("tab", { name: "Opportunities", exact: true });
  await expect(frame).toContainText("Scanning integrations");
  await opportunities.click();
  await expect(frame).toContainText("Scanning opportunities");
  await opportunities.press("ArrowLeft");
  await expect(integrations).toBeFocused();
  await releaseFacade(page, "fixture-integrations-ready:asset:core");
  await expect(frame).toContainText("Scanning opportunities");
  await expect(opportunities).toHaveAttribute("aria-selected", "true");
  await expect(integrations).toBeFocused();
  await releaseFacade(page, "fixture-opportunities-ready:asset:core");
  await expect(frame.locator(".opp-row")).toHaveCount(3);
  await expect(integrations).toBeFocused();
  await expect(opportunities).toHaveAttribute("aria-selected", "true");
  await integrations.press("Enter");
  await expect(frame.locator(".signal-row")).toHaveCount(3);
});

async function openAnalysis(page: Page, location = root) {
  await page.goto(location);
  await selectLibrary(page, core.id);
  await chooseInspector(page, "data-library-lens", "analysis", "Analysis");
  await expect(inspectorTab(page, "data-library-lens", "analysis"))
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
    await chooseInspector(page, "data-library-lens", "analysis", "Analysis");
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
  await expect(subjectTab(page, "member"))
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
  await releaseFacade(page, "fixture-analysis-ready:asset:core");
  await expect(page.locator(".library-analysis-scroll .perf-row")).toHaveCount(2);
  await chooseSubject(page, "package", "Package");
  await selectLibrary(page, other.id);
  await chooseInspector(page, "data-library-lens", "analysis", "Analysis");
  await expect(page.locator(".library-analysis-surface")).toContainText("Analyzing allocations");
  await expect(page.locator(".library-analysis-surface footer")).toContainText(other.asset);
  await expect(page.locator(".library-analysis-surface")).not.toContainText(core.name);
  await releaseFacade(page, "fixture-analysis-ready:asset:other");
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
      const back = page.getByRole("button", { name: "Libraries", exact: true });
      await back.click();
      await expect(page.locator(".library-subject-list")).toBeFocused();
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
    await chooseInspector(
      page,
      "data-library-lens",
      "integrations",
      "Integrations",
    );
    await page.locator('[data-integration-mode="opportunities"]').click();
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
  await chooseInspector(
    page,
    "data-library-lens",
    "integrations",
    "Integrations",
  );
  await page.locator('[data-integration-mode="opportunities"]').click();
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

test("production Opportunities keeps deferred Library results out of the incoming scan", async ({ page }) => {
  await installFacades(page, surface, [], "ready", "ready", undefined, "deferred");
  await openOpportunities(page);
  await expect(page.locator(".library-opportunities-surface")).toContainText("Scanning opportunities");
  await expect(page.locator(".library-opportunities-surface footer")).toContainText(core.asset);
  await releaseFacade(page, "fixture-opportunities-ready:asset:core");
  await expect(page.locator(".library-opportunities-scroll .opp-row")).toHaveCount(3);
  await chooseSubject(page, "package", "Package");
  await selectLibrary(page, other.id);
  await chooseInspector(
    page,
    "data-library-lens",
    "integrations",
    "Integrations",
  );
  await page.locator('[data-integration-mode="opportunities"]').click();
  await expect(page.locator(".library-opportunities-surface")).toContainText("Scanning opportunities");
  await expect(page.locator(".library-opportunities-surface footer")).toContainText(other.asset);
  await expect(page.locator(".library-opportunities-surface")).not.toContainText(core.name);
  await releaseFacade(page, "fixture-opportunities-ready:asset:other");
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
    await chooseInspector(
      page,
      "data-library-lens",
      "integrations",
      "Integrations",
    );
    const frame = page.locator(".library-integrations-surface");
    await expect(frame.locator(".signal-row")).toHaveCount(3);
    await expect(frame.locator(".library-integrations-controls")).toHaveCount(0);
    await expect(frame.locator(".signal-ns").first()).toContainText("System.Text.Json");
    await chooseSubject(page, "platform", "Platform");
    await page.getByTitle("Inspect System.Facade", { exact: true }).click();
    await chooseInspector(
      page,
      "data-library-lens",
      "integrations",
      "Integrations",
    );
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
  await releaseFacade(page, "fixture-integrations-ready:asset:core");
  await expect(page.locator(".library-integrations-scroll .signal-row")).toHaveCount(3);
  await chooseSubject(page, "package", "Package");
  await selectLibrary(page, other.id);
  await chooseInspector(
    page,
    "data-library-lens",
    "integrations",
    "Integrations",
  );
  await expect(page.locator(".library-integrations-surface")).toContainText("Scanning integrations");
  await expect(page.locator(".library-integrations-surface footer")).toContainText(other.asset);
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
  await releaseFacade(page, "fixture-references-ready:asset:core");
  await expect(page.locator(".library-references-scroll")).toContainText("Example.Core.Dependency");
  await chooseSubject(page, "package", "Package");
  await selectLibrary(page, other.id);
  await chooseInspector(page, "data-library-lens", "references", "References");
  await expect(page.locator(".library-references-surface")).toContainText("Reading direct AssemblyRef rows");
  await expect(page.locator(".library-references-surface footer")).toContainText(other.asset);
  await expect(page.locator(".library-references-surface")).not.toContainText("Example.Core");
  await releaseFacade(page, "fixture-references-ready:asset:other");
  await expect(page.locator(".library-references-scroll")).toContainText("Example.Other.Dependency");
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
