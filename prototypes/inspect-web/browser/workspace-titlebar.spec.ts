import { expect, test, type Page } from "@playwright/test";
import type { SlideStripPolicy } from "../src/slide-strip.ts";
import { callFactsFixture, exceptionRegionsFixture, safetyFactsFixture } from "../test/member-facts-fixture.ts";

async function box(page: Page, selector: string) {
  const value = await page.locator(selector).boundingBox();
  expect(value).not.toBeNull();
  return value!;
}

async function renderSpotlightFooter(
  page: Page,
  spotlightScope: "all" | "commands",
) {
  await page.evaluate(async scope => {
    const [{ createSpotlight }, { KeybindingRegistry }] = await Promise.all([
      import("../src/spotlight.ts"),
      import("../src/keybinding-registry.ts"),
    ]);
    const escapeHtml = (value: unknown) => String(value)
      .replaceAll("&", "&amp;")
      .replaceAll("<", "&lt;")
      .replaceAll(">", "&gt;")
      .replaceAll('"', "&quot;");
    const spotlight = createSpotlight({
      keybindings: new KeybindingRegistry(),
      state: {
        spotlightOpen: true,
        spotlightQuery: "",
        spotlightIndex: 0,
        spotlightScope: scope,
        spotlightFocus: "input",
        spotlightChipIndex: 0,
      },
      lenses: () => [["api", "API"]],
      escapeHtml,
      highlightRanges: value => escapeHtml(value),
      kindIcon: () => "C",
      searchResults: () => [],
      pickResult: () => {},
      executeCommand: () => undefined,
      reportCommandError: () => {},
      commandContext: () => null,
      schedulePackageFetch: () => {},
      resetPackageSearch: () => {},
      packageSearchLoading: () => false,
      packageCount: () => 1,
      activeFramework: () => "net10.0",
      render: () => {},
    });
    const app = document.querySelector<HTMLElement>("#app");
    if (!app) throw new Error("Workspace harness app is missing");
    app.innerHTML = spotlight.modalHtml();
  }, spotlightScope);
}

test("the top shell row separates application scopes from inspection subjects", async ({
  page,
}) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  await page.goto("/browser/workspace-titlebar.html?package=1");

  const titleNavigation = await box(page, ".title-navigation");
  const search = await box(page, "#open-search");
  const forward = await box(page, "#nav-forward");
  const menuSlot = await box(page, ".application-menu-slot");

  expect(titleNavigation.x + titleNavigation.width)
    .toBeLessThanOrEqual(menuSlot.x);
  expect(titleNavigation.width).toBeCloseTo(284, 0);
  expect(forward.x + forward.width).toBeLessThanOrEqual(search.x);
  expect(search.width).toBeCloseTo(224, 0);
  expect(menuSlot.x + menuSlot.width).toBeCloseTo(1440, 0);
  await expect(page.locator(".targetbar .inspected-target")).toBeVisible();
  await expect(page.locator(".targetbar .subject-path-segment.root"))
    .toHaveText("System.Text.Json");
  await expect(page.locator(".titlebar #open-search")).toBeVisible();
  await expect(page.locator(".title-search-label-full"))
    .toHaveText("Search types, members, packages");
  await expect(page.locator("#open-search kbd")).toHaveCount(0);
  await expect(page.locator(".titlebar .nav-history")).toBeVisible();
  await expect(page.locator(".titlebar .subject-inspector-region"))
    .toBeVisible();
  await expect(page.locator(".titlebar .application-scope-strip"))
    .toBeVisible();
  await expect(page.locator("[data-application-scope]"))
    .toHaveText(["Query", "Workspace"]);
  await expect(page.locator("[data-application-scope='query']"))
    .not.toHaveAttribute("aria-current", "page");
  await expect(page.locator("[data-application-scope='workspace']"))
    .not.toHaveAttribute("aria-current", "page");
  await expect(page.locator(".scope-switch [data-subject-tab]")).toHaveCount(3);
  await expect(page.locator("[data-subject-tab][data-scope='package']"))
    .toHaveAttribute("aria-label", "Package");
  await expect(page.locator("[data-subject-tab][data-scope='type']"))
    .toHaveAttribute("aria-label", "Type");
  await expect(page.locator("[data-scope='workspace']")).toHaveCount(0);
  await expect(page.locator(".titlebar #application-menu-button")).toBeVisible();
  await expect(page.locator(".targetbar #application-menu-button"))
    .toHaveCount(0);
  await expect(page.locator("#share, #open-settings, #help")).toHaveCount(0);
  await expect(page.locator(".workspace-title")).toHaveCount(0);
  await expect(page.locator(".titlebar")).not.toContainText("0:");
  await expect(page.locator(".titlebar")).not.toContainText("Platform");
  await expect(page.locator(".workspace-window")).toHaveCount(0);
  await expect(page.locator(".brand-icon img")).toHaveAttribute(
    "src",
    "/assets/dotnet-inspect-bot.png");
});

test("the content frame clamps wide inventory and pushes at constrained widths", async ({
  page,
}) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  await page.goto("/browser/workspace-titlebar.html?member=1");

  const wideInventory = await box(page, "#content-navigation-pane");
  const wideDetail = await box(page, ".detail-pane");
  expect(wideInventory.width).toBeGreaterThanOrEqual(304);
  expect(wideInventory.width).toBeLessThanOrEqual(360);
  expect(wideInventory.x + wideInventory.width).toBeCloseTo(wideDetail.x, 0);

  await page.setViewportSize({ width: 900, height: 700 });
  const intermediateInventory = await box(page, "#content-navigation-pane");
  expect(intermediateInventory.width).toBeCloseTo(304, 0);

  await page.setViewportSize({ width: 600, height: 700 });
  const href = page.url();
  const historyLength = await page.evaluate(() => history.length);
  const toggle = page.getByRole("button", { name: "Members" });
  await expect(toggle).toBeVisible();
  await expect(page.locator("#content-navigation-pane")).toBeHidden();
  await expect(page.locator(".detail-pane")).toBeVisible();

  await toggle.click();
  await expect(page.locator("#content-navigation-pane")).toBeVisible();
  await expect(page.locator(".detail-pane")).toBeHidden();
  await expect(page.locator("#type-list")).toBeFocused();
  await expect(page.getByRole("button", {
    name: "Show details",
  })).toBeVisible();
  expect(page.url()).toBe(href);
  expect(await page.evaluate(() => history.length)).toBe(historyLength);

  await page.locator("[data-harness-navigation-row]").click();
  await expect(page.locator("#content-navigation-pane")).toBeHidden();
  await expect(page.locator(".detail-pane")).toBeVisible();
  await expect(toggle).toBeFocused();
  expect(page.url()).toBe(href);
  expect(await page.evaluate(() => history.length)).toBe(historyLength);
  expect(await page.evaluate(() =>
    document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);

  await toggle.click();
  await page.getByRole("button", {
    name: "Show details",
  }).click();
  await expect(page.locator(".detail-pane")).toBeVisible();
  expect(page.url()).toBe(href);
  expect(await page.evaluate(() => history.length)).toBe(historyLength);

  await toggle.click();
  await page.getByRole("button", { name: "Show details" }).focus();
  await page.setViewportSize({ width: 900, height: 700 });
  await expect(page.locator("#type-list")).toBeFocused();
  await expect(page.locator("#content-navigation-pane")).toBeVisible();
  await expect(page.locator(".detail-pane")).toBeVisible();
});

test("narrowing retains detail after focus leaves the content frame", async ({
  page,
}) => {
  await page.setViewportSize({ width: 900, height: 700 });
  await page.goto("/browser/workspace-titlebar.html?member=1");

  await page.locator("#type-list").focus();
  await page.locator(".member-documentation .docs-unavailable").click();
  await expect(page.locator("body")).toBeFocused();
  await page.evaluate(() => new Promise<void>(resolve =>
    requestAnimationFrame(() => requestAnimationFrame(() => resolve()))));

  await page.setViewportSize({ width: 600, height: 700 });
  await expect(page.locator("#content-navigation-pane")).toBeHidden();
  await expect(page.locator(".detail-pane")).toBeVisible();
});

test("immediate narrowing ignores stale navigation focus ownership", async ({
  page,
}) => {
  await page.setViewportSize({ width: 900, height: 700 });
  await page.goto("/browser/workspace-titlebar.html?member=1");

  await page.locator("#type-list").focus();
  await page.locator(".member-documentation .docs-unavailable").click();
  await page.setViewportSize({ width: 600, height: 700 });

  await expect(page.locator("#content-navigation-pane")).toBeHidden();
  await expect(page.locator(".detail-pane")).toBeVisible();
});

test("immediate narrowing follows navigation through replacement rendering", async ({
  page,
}) => {
  await page.setViewportSize({ width: 900, height: 700 });
  await page.goto("/browser/workspace-titlebar.html?member=1");

  await page.locator("#type-list").focus();
  await page.evaluate(() => window.beginContentFrameReplacementProbe());
  await expect(page.locator("body")).toBeFocused();
  await page.setViewportSize({ width: 600, height: 700 });
  await page.evaluate(() => window.flushContentFrameReplacementProbe());

  await expect(page.locator("#content-navigation-pane")).toBeVisible();
  await expect(page.locator(".detail-pane")).toBeHidden();
  await expect(page.locator("#type-list")).toBeFocused();
});

test("pointer departure cancels replacement focus authority", async ({
  page,
}) => {
  await page.setViewportSize({ width: 900, height: 700 });
  await page.goto("/browser/workspace-titlebar.html?member=1");

  await page.locator("#type-list").focus();
  await page.evaluate(() => window.beginContentFrameReplacementProbe());
  await page.locator(".member-documentation .docs-unavailable").click();
  await page.setViewportSize({ width: 600, height: 700 });
  await page.evaluate(() => window.flushContentFrameReplacementProbe());

  await expect(page.locator("#content-navigation-pane")).toBeHidden();
  await expect(page.locator(".detail-pane")).toBeVisible();
  await expect(page.locator("body")).toBeFocused();
});

test("pointer departure cancels queued replacement focus", async ({
  page,
}) => {
  await page.setViewportSize({ width: 900, height: 700 });
  await page.goto("/browser/workspace-titlebar.html?member=1");

  await page.locator("#type-list").focus();
  await page.evaluate(() => window.beginContentFrameReplacementProbe());
  await page.locator(".member-documentation .docs-unavailable").click();
  await page.evaluate(() => window.flushContentFrameReplacementProbe());

  await expect(page.locator("body")).toBeFocused();
  await expect(page.locator("#content-navigation-pane")).toBeVisible();
  await expect(page.locator(".detail-pane")).toBeVisible();
});

test("focus departure cancels replacement focus authority", async ({
  page,
}) => {
  await page.setViewportSize({ width: 900, height: 700 });
  await page.goto("/browser/workspace-titlebar.html?member=1");

  await page.locator("#type-list").focus();
  await page.evaluate(() => window.beginContentFrameReplacementProbe());
  await page.locator(".member-documentation .docs-unavailable")
    .evaluate(element => {
      if (!(element instanceof HTMLElement))
        throw new Error("The detail focus target is unavailable.");
      element.tabIndex = -1;
      element.focus();
    });
  await page.setViewportSize({ width: 600, height: 700 });
  await page.evaluate(() => window.flushContentFrameReplacementProbe());

  await expect(page.locator("#content-navigation-pane")).toBeHidden();
  await expect(page.locator(".detail-pane")).toBeVisible();
  await expect(page.locator(".member-documentation .docs-unavailable"))
    .toBeFocused();
});

test("failed replacement restoration expires its pane authority", async ({
  page,
}) => {
  await page.setViewportSize({ width: 900, height: 700 });
  await page.goto("/browser/workspace-titlebar.html?member=1");

  await page.locator("#type-list").focus();
  await page.evaluate(() => {
    window.beginContentFrameReplacementProbe();
    document.querySelector("#type-list")?.remove();
    window.flushContentFrameReplacementProbe();
  });
  await page.setViewportSize({ width: 600, height: 700 });

  await expect(page.locator("#content-navigation-pane")).toBeHidden();
  await expect(page.locator(".detail-pane")).toBeVisible();
  await expect(page.locator("body")).toBeFocused();
});

test("immediate widening ignores a departed narrow toggle", async ({
  page,
}) => {
  await page.setViewportSize({ width: 600, height: 700 });
  await page.goto("/browser/workspace-titlebar.html?member=1");

  await page.getByRole("button", { name: "Members" }).focus();
  await page.locator(".member-documentation .docs-unavailable").click();
  await expect(page.locator("body")).toBeFocused();
  await page.setViewportSize({ width: 900, height: 700 });

  await expect(page.locator("body")).toBeFocused();
  await expect(page.locator("#content-navigation-pane")).toBeVisible();
  await expect(page.locator(".detail-pane")).toBeVisible();
});

test("keyboard entry focuses an empty Member inventory after replacement", async ({
  page,
}) => {
  await page.setViewportSize({ width: 600, height: 700 });
  await page.goto(
    "/browser/workspace-titlebar.html?empty-member-entry=1");

  await page.getByRole("button", { name: "Types" }).click();
  await expect(page.locator("#type-list")).toBeFocused();
  await page.keyboard.press("Enter");

  await expect(page.getByText("No members match these filters.")).toBeVisible();
  await expect(page.locator("#type-list")).toBeFocused();
  await expect(page.locator(".detail-pane")).toBeHidden();
});

test("the narrow return control integrates with Metadata and Source frames", async ({
  page,
}) => {
  await page.setViewportSize({ width: 600, height: 700 });
  await page.goto("/browser/workspace-titlebar.html?metadata=1");

  const metadataHeader = await box(page, ".metadata-surface-head");
  const metadataToggle = await box(page, "#content-navigation-toggle");
  expect(metadataToggle.y).toBeGreaterThanOrEqual(metadataHeader.y);
  expect(metadataToggle.y + metadataToggle.height)
    .toBeLessThanOrEqual(metadataHeader.y + metadataHeader.height);
  await expect(page.locator(".metadata-surface-head h1")).toHaveText("Metadata");

  await page.goto("/browser/workspace-titlebar.html?package-dependencies=1");
  const packageDependenciesHeader = await box(
    page,
    ".package-dependencies-surface-head");
  const packageDependenciesToggle = await box(
    page,
    "#content-navigation-toggle");
  expect(packageDependenciesToggle.y)
    .toBeGreaterThanOrEqual(packageDependenciesHeader.y);
  expect(packageDependenciesToggle.y + packageDependenciesToggle.height)
    .toBeLessThanOrEqual(
      packageDependenciesHeader.y + packageDependenciesHeader.height);
  await expect(page.locator(".detail-pane"))
    .toHaveClass(/content-navigation-integrated/);
  await expect(page.locator(".package-dependencies-surface-head h1"))
    .toHaveText("Dependencies");
  const packageDependenciesFooter = await box(
    page,
    ".package-dependencies-surface-footer");
  const packageDependenciesCoordinate = await box(
    page,
    ".package-dependencies-surface-footer span:first-child");
  const packageDependenciesFramework = await box(
    page,
    ".package-dependencies-surface-footer span:last-child");
  expect(packageDependenciesCoordinate.x)
    .toBeLessThan(packageDependenciesFooter.x + packageDependenciesFooter.width / 3);
  expect(packageDependenciesFramework.x + packageDependenciesFramework.width)
    .toBeGreaterThan(
      packageDependenciesFooter.x + packageDependenciesFooter.width * 2 / 3);
  expect(await page.evaluate(() =>
    document.documentElement.scrollWidth
    - document.documentElement.clientWidth)).toBeLessThanOrEqual(0);

  await page.goto("/browser/workspace-titlebar.html?package-metadata=1");
  const packageMetadataHeader = await box(
    page,
    ".package-metadata-surface-head");
  const packageMetadataToggle = await box(
    page,
    "#content-navigation-toggle");
  expect(packageMetadataToggle.y)
    .toBeGreaterThanOrEqual(packageMetadataHeader.y);
  expect(packageMetadataToggle.y + packageMetadataToggle.height)
    .toBeLessThanOrEqual(
      packageMetadataHeader.y + packageMetadataHeader.height);
  await expect(page.locator(".detail-pane"))
    .toHaveClass(/content-navigation-integrated/);
  await expect(page.locator(".package-metadata-surface-head h1"))
    .toHaveText("Metadata images");
  expect(await page.evaluate(() =>
    document.documentElement.scrollWidth
    - document.documentElement.clientWidth)).toBeLessThanOrEqual(0);

  await page.goto("/browser/workspace-titlebar.html?member=1&source=1");
  const sourceNavigation = await box(page, ".content-navigation-bar");
  const source = await box(page, ".source-result");
  expect(source.y).toBeCloseTo(
    sourceNavigation.y + sourceNavigation.height,
    0);
  await expect(page.locator("#inspector-panel > h1")).toHaveCount(0);
});

for (const [subject, width] of [
  ["package", 1440], ["package", 390], ["library", 1440], ["library", 390],
] as const) {
  test(`${subject} Overview fills its frame and contains long content at ${width}px`, async ({ page }) => {
    await page.setViewportSize({ width, height: 900 });
    await page.goto(`/browser/workspace-titlebar.html?${subject}-overview=1&long=1`);

    const panel = await box(page, "#inspector-panel");
    const surface = await box(page, ".overview-surface");
    expect(surface.x).toBeCloseTo(panel.x, 0);
    expect(surface.y).toBeCloseTo(panel.y, 0);
    expect(surface.width).toBeCloseTo(panel.width, 0);
    expect(surface.height).toBeCloseTo(panel.height, 0);
    await expect(page.locator(".type-heading, .package-coordinate-editor")).toHaveCount(0);
    const name = `Example.${"LongNamespace.".repeat(12)}Library`;
    await expect(page.locator("#inspector-panel h1")).toHaveText(name);
    expect((await box(page, ".overview-identity h1")).width).toBeGreaterThan(100);
    expect((await box(page, ".overview-identity .subject-icon")).width).toBe(40);
    await expect(page.locator(".overview-surface-head p")).toHaveText("32 types · 1,234 members");
    await expect(page.locator(".overview-surface-footer span")).toHaveText([
      "System.Text.Json@10.0.0", "net10.0",
    ]);
    if (subject === "package") {
      await page.getByRole("combobox", { name: "Version", exact: true }).selectOption("9.0.0");
      await expect(page.locator("#package-version")).toHaveValue("9.0.0");
      await page.getByRole("combobox", { name: "Framework", exact: true }).selectOption("net10.0-windows10.0.19041.0");
      await expect(page.locator("#framework")).toHaveValue("net10.0-windows10.0.19041.0");
    } else {
      await expect(page.locator(".overview-controls")).toHaveCount(0);
      await expect(page.locator(".overview-identity-detail")).toHaveText([
        `lib/net10.0/${name}.dll`,
        `${name}, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null`,
      ]);
    }

    const header = await box(page, ".overview-surface-head");
    const controls = subject === "package" ? await box(page, ".overview-controls") : null;
    const footer = await box(page, ".overview-surface-footer");
    expect(await page.locator(".overview-scroll").evaluate(element =>
      element.scrollHeight > element.clientHeight)).toBe(true);
    await page.locator(".overview-scroll").evaluate(element => {
      element.scrollTop = element.scrollHeight;
    });
    if (subject === "package") {
      await expect(page.locator("[data-doc-path='README.md']")).toBeVisible();
    } else {
      await expect(page.locator("[data-namespace-jump]").last()).toBeVisible();
    }
    expect((await box(page, ".overview-surface-head")).y).toBe(header.y);
    if (controls) expect((await box(page, ".overview-controls")).y).toBe(controls.y);
    expect((await box(page, ".overview-surface-footer")).y).toBe(footer.y);
    expect(await page.locator(".overview-scroll").evaluate(element =>
      element.scrollWidth - element.clientWidth)).toBeLessThanOrEqual(0);
    expect(await page.evaluate(() =>
      document.documentElement.scrollWidth - document.documentElement.clientWidth)).toBeLessThanOrEqual(0);

    if (width === 390) {
      const toggle = await box(page, "#content-navigation-toggle");
      expect(toggle.y).toBeGreaterThanOrEqual(header.y);
      expect(toggle.y + toggle.height).toBeLessThanOrEqual(header.y + header.height);
      await page.getByRole("button", { name: subject === "package" ? "Libraries" : "Types", exact: true }).click();
      await expect(page.locator(subject === "package" ? ".library-subject-list" : ".type-list")).toBeFocused();
      await expect(page.locator(".detail-pane")).toBeHidden();
    }
  });
}

test("Package Overview keeps empty totals and available documents", async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 700 });
  await page.goto("/browser/workspace-titlebar.html?package-overview=1&empty=1");
  await expect(page.locator(".overview-surface-head p")).toHaveText("0 types · 0 members");
  await expect(page.locator(".library-row")).toHaveCount(0);
  await expect(page.locator("[data-doc-path='README.md']")).toBeVisible();
  await expect(page.locator(".overview-surface-footer")).toBeVisible();
});

test("Member Facts presents a compact summary separate from member identity", async ({
  page,
}) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  await page.goto("/browser/workspace-titlebar.html?member=1&member-facts=populated");
  await expect(page.locator(".facts-summary-heading h2"))
    .toHaveText("Analysis summary");
  await expect(page.locator(".facts-summary-list dt")).toHaveText([
    "Allocations", "Calls", "Copies", "Reflection calls",
    "Throws / catches / finally", "Unsafe", "Allocates in loop",
  ]);
  await expect(page.locator(".facts-summary-value"))
    .toHaveText(["1", "3", "0", "0", "1 / 0 / 0", "no", "no"]);
  await expect(page.locator(".facts-summary a, .facts-summary button"))
    .toHaveCount(0);
  await expect(page.locator(".facts-metadata-identity"))
    .toHaveText("Metadata token0x06000125");
  await expect(page.locator(".member-surface-head p"))
    .toHaveText("method · 1 of 1");
  await expect(page.locator(".allocation-facts h2"))
    .toHaveText("Allocation facts");
  const header = await box(page, ".member-surface-head");
  const summary = await box(page, ".facts-summary");
  expect(summary.y - (header.y + header.height)).toBeCloseTo(12, 0);
  expect(summary.height).toBeLessThanOrEqual(310);
  const value = await box(page, ".facts-summary-list > div:first-child .facts-summary-value");
  const evidence = await box(page, ".facts-summary-list > div:first-child .fact-evidence");
  expect(Math.abs(value.y - evidence.y)).toBeLessThan(3);
  expect(evidence.x).toBeGreaterThan(value.x + value.width);
});

test("Member Facts keeps zero, loading, and failure states distinct", async ({
  page,
}) => {
  await page.setViewportSize({ width: 600, height: 900 });
  for (const mode of ["zero", "loading", "error"]) {
    await page.goto(
      `/browser/workspace-titlebar.html?member=1&member-facts=${mode}`);
    await expect(page.locator(".member-surface-head")).toBeVisible();
    if (mode === "zero") {
      await expect(page.locator(".facts-summary-value"))
        .toHaveText(["0", "0", "0", "0", "0 / 0 / 0", "no", "no"]);
      await expect(page.locator(".fact-evidence")).toHaveCount(0);
      await expect(page.locator(".allocation-facts > header > span"))
        .toHaveText("0 occurrences");
      await expect(page.locator(".allocation-empty"))
        .toHaveText("No allocation occurrences were found in this method.");
      await expect(page.locator(".allocation-row")).toHaveCount(0);
      await expect(page.locator(".call-facts > header > span"))
        .toHaveText("0 call sites");
      await expect(page.locator(".call-empty"))
        .toHaveText("No direct call sites were found in this method.");
      await expect(page.locator(".call-row")).toHaveCount(0);
      await expect(page.locator(".safety-facts > header > span"))
        .toHaveText("0 facts");
      await expect(page.locator(".safety-empty"))
        .toHaveText("No unsafe operations or declaration evidence were found.");
      await expect(page.locator(".safety-row")).toHaveCount(0);
      await expect(page.locator(".exception-regions > header > span"))
        .toHaveText("0 regions");
      await expect(page.locator(".exception-empty"))
        .toHaveText("No exception regions were found in this method.");
      await expect(page.locator(".exception-row")).toHaveCount(0);
    } else {
      await expect(page.locator(".facts-summary")).toHaveCount(0);
      await expect(page.locator(".allocation-facts")).toHaveCount(0);
      await expect(page.locator(".call-facts")).toHaveCount(0);
      await expect(page.locator(".safety-facts")).toHaveCount(0);
      await expect(page.locator(".exception-regions")).toHaveCount(0);
      await expect(page.locator(".member-surface-scroll h2"))
        .toHaveText(mode === "loading" ? "Analyzing method…" : "Facts query failed");
      if (mode === "error") {
        await expect(page.locator(".member-surface-scroll p"))
          .toHaveText("The selected method could not be decoded.");
      }
    }
  }
});

test("Member Facts reflows values and evidence within the detail pane", async ({
  page,
}) => {
  for (const width of [900, 480, 360]) {
    await page.setViewportSize({ width, height: 900 });
    await page.goto(
      "/browser/workspace-titlebar.html?member=1&member-facts=long");
    const value = await box(page, ".facts-summary-list > div:first-child .facts-summary-value");
    const evidence = await box(page, ".facts-summary-list > div:first-child .fact-evidence");
    expect(evidence.y).toBeGreaterThanOrEqual(value.y + value.height);
    expect(evidence.x).toBeCloseTo(value.x, 0);
    for (const selector of [
      ".facts-summary", ".facts-summary-list > div",
      ".facts-summary-value", ".fact-evidence", ".member-surface-scroll",
    ]) {
      const contained = await page.locator(selector).evaluateAll(elements =>
        elements.every(element => element.scrollWidth <= element.clientWidth));
      expect(contained, `${selector} at ${width}px`).toBe(true);
    }
    expect(await page.evaluate(() =>
      document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
  }
});

test("Member Facts allocation rows preserve all nine fields in occurrence order", async ({
  page,
}) => {
  await page.setViewportSize({ width: 1440, height: 1000 });
  await page.goto("/browser/workspace-titlebar.html?member=1&allocation-facts=populated");
  await expect(page.locator(".allocation-facts > header > span"))
    .toHaveText("3 occurrences");
  await expect(page.locator(".facts-summary-value").first()).toHaveText("2");
  await expect(page.locator(".allocation-location code"))
    .toHaveText(["IL_0020", "IL_0048", "IL_009C"]);
  await expect(page.locator(".allocation-location > span"))
    .toHaveText(["Object", "Array", "Enumerator"]);
  await expect(page.locator(".allocation-type"))
    .toHaveText([
      "System.Text.Json.JsonException",
      "System.Byte[]",
      "System.Collections.Generic.Dictionary<System.String, System.Text.Json.JsonElement>.Enumerator",
    ]);
  const values = [
    ["yes", "Conditional", "ErrorPath", "ThrowPath", "no", "not available"],
    ["yes", "Loop", "LoopBody", "LocalOnly", "yes", "280 B"],
    ["no", "Once", "StraightLine", "Unknown", "no", "not available"],
  ];
  for (const [index, expected] of values.entries()) {
    const row = page.locator(".allocation-row").nth(index);
    await expect(row.locator("dt")).toHaveText([
      "Counted as heap", "Multiplicity", "Path", "Escape", "Loop", "Est. size",
    ]);
    await expect(row.locator("dd")).toHaveText(expected);
  }
  await expect(page.locator(".allocation-facts a, .allocation-facts button, .allocation-facts details"))
    .toHaveCount(0);
  await expect(page.locator(".call-facts h2, .safety-facts h2, .exception-regions h2"))
    .toHaveText(["Calls", "Safety facts", "Exception regions"]);
  const section = await box(page, ".allocation-facts");
  const summary = await box(page, ".facts-summary");
  expect(section.width).toBeCloseTo(summary.width, 0);
  expect(section.height).toBeLessThanOrEqual(300);
  expect(section.y - (summary.y + summary.height)).toBeCloseTo(20, 0);
});

test("Member Facts allocation rows reflow by pane width without hiding long values", async ({
  page,
}) => {
  for (const width of [1440, 900, 600, 360]) {
    await page.setViewportSize({ width, height: 1000 });
    await page.goto("/browser/workspace-titlebar.html?member=1&allocation-facts=long");
    const location = await box(page, ".allocation-row:first-child .allocation-location");
    const type = await box(page, ".allocation-row:first-child .allocation-type");
    if (width === 900 || width === 360) {
      expect(type.y).toBeGreaterThanOrEqual(location.y + location.height);
      expect(type.x).toBeCloseTo(location.x, 0);
    } else {
      expect(type.x).toBeGreaterThan(location.x + location.width);
    }
    for (const selector of [
      ".allocation-facts", ".allocation-row", ".allocation-main",
      ".allocation-location", ".allocation-type", ".allocation-properties > div",
      ".allocation-properties dd", ".member-surface-scroll",
    ]) {
      expect(await page.locator(selector).evaluateAll(elements =>
        elements.every(element => element.scrollWidth <= element.clientWidth)),
      `${selector} at ${width}px`).toBe(true);
    }
    await expect(page.locator(".allocation-type").first())
      .toHaveText("Example.Serialization.BufferedDocumentReader<System.Collections.Generic.Dictionary<System.String, System.Collections.Generic.List<System.Text.Json.JsonElement>>>");
    await expect(page.locator(".allocation-location code").first()).toHaveText("IL_12345678");
    await expect(page.locator(".allocation-type").last()).toHaveText("Type unavailable");
    await expect(page.locator(".allocation-row").nth(1).locator("dd").last())
      .toHaveText("2147483647 B");
  }
});

test("Member Facts call rows preserve all five fields and repeated callees", async ({
  page,
}) => {
  await page.setViewportSize({ width: 1440, height: 1000 });
  await page.goto("/browser/workspace-titlebar.html?member=1&call-facts=populated");
  const facts = callFactsFixture();
  await expect(page.locator(".call-facts > header > span")).toHaveText("4 call sites");
  await expect(page.locator(".facts-summary-value").nth(1)).toHaveText("4");
  await expect(page.locator(".call-row")).toHaveCount(4);
  for (const [index, call] of facts.calls.entries()) {
    const row = page.locator(".call-row").nth(index);
    await expect(row.locator(".call-location code")).toHaveText([call.offset, call.opcode]);
    await expect(row.locator(".call-callee")).toHaveText(call.callee);
    await expect(row.locator("dt")).toHaveText(["Multiplicity", "Loop"]);
    await expect(row.locator("dd")).toHaveText([call.multiplicity, call.inLoop ? "yes" : "no"]);
  }
  await expect(page.locator(".call-facts a, .call-facts button, .call-facts details"))
    .toHaveCount(0);
  await expect(page.locator(".safety-facts h2, .exception-regions h2"))
    .toHaveText(["Safety facts", "Exception regions"]);
  const calls = await box(page, ".call-facts");
  const allocations = await box(page, ".allocation-facts");
  expect(calls.width).toBeCloseTo(allocations.width, 0);
  expect(calls.height).toBeLessThanOrEqual(310);
  expect(calls.y - (allocations.y + allocations.height)).toBeCloseTo(20, 0);
});

test("Member Facts call rows reflow by pane width without hiding long values", async ({
  page,
}) => {
  const facts = callFactsFixture("long");
  for (const width of [1440, 900, 600, 360]) {
    await page.setViewportSize({ width, height: 1000 });
    await page.goto("/browser/workspace-titlebar.html?member=1&call-facts=long");
    const location = await box(page, ".call-row:first-child .call-location");
    const callee = await box(page, ".call-row:first-child .call-callee");
    if (width === 900 || width === 360) {
      expect(callee.y).toBeGreaterThanOrEqual(location.y + location.height);
      expect(callee.x).toBeCloseTo(location.x, 0);
    } else {
      expect(callee.x).toBeGreaterThan(location.x + location.width);
    }
    for (const selector of [
      ".call-facts", ".call-row", ".call-location", ".call-main",
      ".call-callee", ".call-properties", ".call-properties > div",
      ".call-properties dd", ".member-surface-scroll",
    ]) {
      expect(await page.locator(selector).evaluateAll(elements =>
        elements.every(element => element.scrollWidth <= element.clientWidth)),
      `${selector} at ${width}px`).toBe(true);
    }
    await expect(page.locator(".call-callee")).toHaveText(facts.calls.map(call => call.callee));
    await expect(page.locator(".call-location code").last()).toHaveText("call");
    await expect(page.locator(".call-location").last().locator("code").first())
      .toHaveText("IL_12345678");
    await expect(page.locator(".call-properties").first().locator("dd"))
      .toHaveText(["Unknown", "no"]);
  }
});

test("Member Facts safety rows preserve all five fields and explicit absent offsets", async ({
  page,
}) => {
  await page.setViewportSize({ width: 1440, height: 1000 });
  await page.goto("/browser/workspace-titlebar.html?member=1&safety-facts=populated");
  const facts = safetyFactsFixture();
  await expect(page.locator(".safety-facts > header > span")).toHaveText("5 facts");
  await expect(page.locator(".safety-row")).toHaveCount(5);
  await expect(page.locator(".safety-no-offset")).toHaveCount(2);
  for (const [index, fact] of facts.safety.entries()) {
    const row = page.locator(".safety-row").nth(index);
    await expect(row.locator(".safety-location > :first-child"))
      .toHaveText(fact.offset ?? "No IL offset");
    await expect(row.locator(".safety-kind")).toHaveText(fact.kind);
    await expect(row.locator(".safety-operation")).toHaveText(fact.operation);
    await expect(row.locator("dt")).toHaveText(["Requirement", "Evidence"]);
    await expect(row.locator("dd")).toHaveText([fact.requirement, fact.evidence]);
  }
  await expect(page.locator(".safety-facts a, .safety-facts button, .safety-facts details"))
    .toHaveCount(0);
  await expect(page.locator(".exception-regions h2")).toHaveText("Exception regions");
  const safety = await box(page, ".safety-facts");
  const calls = await box(page, ".call-facts");
  expect(safety.width).toBeCloseTo(calls.width, 0);
  expect(safety.height).toBeLessThanOrEqual(375);
  expect(safety.y - (calls.y + calls.height)).toBeCloseTo(20, 0);
});

test("Member Facts safety rows reflow by pane width without hiding long values", async ({
  page,
}) => {
  const facts = safetyFactsFixture("long");
  for (const width of [1440, 900, 600, 360]) {
    await page.setViewportSize({ width, height: 1100 });
    await page.goto("/browser/workspace-titlebar.html?member=1&safety-facts=long");
    const location = await box(page, ".safety-row:first-child .safety-location");
    const operation = await box(page, ".safety-row:first-child .safety-operation");
    if (width === 900 || width === 360) {
      expect(operation.y).toBeGreaterThanOrEqual(location.y + location.height);
      expect(operation.x).toBeCloseTo(location.x, 0);
    } else {
      expect(operation.x).toBeGreaterThan(location.x + location.width);
    }
    for (const selector of [
      ".safety-facts", ".safety-row", ".safety-location", ".safety-location > *",
      ".safety-main", ".safety-operation", ".safety-properties",
      ".safety-properties > div", ".safety-properties dd", ".member-surface-scroll",
    ]) {
      expect(await page.locator(selector).evaluateAll(elements =>
        elements.every(element => element.scrollWidth <= element.clientWidth)),
      `${selector} at ${width}px`).toBe(true);
    }
    await expect(page.locator(".safety-operation")).toHaveText(facts.safety.map(fact => fact.operation));
    await expect(page.locator(".safety-location code").last()).toHaveText("IL_12345678");
    await expect(page.locator(".safety-no-offset")).toHaveCount(2);
  }
});

test("Member Facts exception rows preserve all six fields and nullable values", async ({
  page,
}) => {
  await page.setViewportSize({ width: 1440, height: 1000 });
  await page.goto("/browser/workspace-titlebar.html?member=1&exception-regions=populated");
  const facts = exceptionRegionsFixture();
  await expect(page.locator(".exception-regions > header > span")).toHaveText("4 regions");
  await expect(page.locator(".exception-row")).toHaveCount(4);
  for (const [index, region] of facts.exceptionRegions.entries()) {
    const row = page.locator(".exception-row").nth(index);
    await expect(row.locator(".exception-identity > span")).toHaveText(`Region ${region.region}`);
    await expect(row.locator(".exception-clause")).toHaveText(region.clause);
    await expect(row.locator("dt")).toHaveText(["Caught type", "Try", "Handler", "Filter"]);
    await expect(row.locator("dd")).toHaveText([
      region.caughtType ?? "not supplied", region.tryRange, region.handlerRange,
      region.filterRange ?? "not supplied",
    ]);
  }
  await expect(page.locator(".exception-regions a, .exception-regions button, .exception-regions details"))
    .toHaveCount(0);
  await expect(page.locator(".safety-facts h2")).toHaveText("Safety facts");
  await expect(page.locator(".performance-facts h2")).toHaveText("Performance opportunities");
  const regions = await box(page, ".exception-regions");
  const safety = await box(page, ".safety-facts");
  expect(regions.width).toBeCloseTo(safety.width, 0);
  expect(regions.height).toBeLessThanOrEqual(300);
  expect(regions.y - (safety.y + safety.height)).toBeCloseTo(20, 0);
});

test("Member Facts exception rows reflow by pane width without hiding long values", async ({
  page,
}) => {
  const facts = exceptionRegionsFixture("long");
  for (const width of [1440, 900, 600, 360]) {
    await page.setViewportSize({ width, height: 1100 });
    await page.goto("/browser/workspace-titlebar.html?member=1&exception-regions=long");
    const identity = await box(page, ".exception-row:first-child .exception-identity");
    const type = await box(page, ".exception-row:first-child .exception-type");
    if (width === 900 || width === 360) {
      expect(type.y).toBeGreaterThanOrEqual(identity.y + identity.height);
      expect(type.x).toBeCloseTo(identity.x, 0);
    } else {
      expect(type.x).toBeGreaterThan(identity.x + identity.width);
    }
    for (const selector of [
      ".exception-regions", ".exception-row", ".exception-identity",
      ".exception-identity > *", ".exception-main", ".exception-type",
      ".exception-type dd", ".exception-ranges", ".exception-ranges > div",
      ".exception-ranges dd", ".member-surface-scroll",
    ]) {
      expect(await page.locator(selector).evaluateAll(elements =>
        elements.every(element => element.scrollWidth <= element.clientWidth)),
      `${selector} at ${width}px`).toBe(true);
    }
    await expect(page.locator(".exception-identity > span").first())
      .toHaveText("Region 123456789");
    for (const [index, region] of facts.exceptionRegions.entries()) {
      await expect(page.locator(".exception-row").nth(index).locator("dd")).toHaveText([
        region.caughtType ?? "not supplied", region.tryRange, region.handlerRange,
        region.filterRange ?? "not supplied",
      ]);
    }
  }
});

test("Member Overview anchors its declaration below the quiet header", async ({
  page,
}) => {
  for (const documentation of ["missing", "summary", "loading", "error"]) {
    await page.setViewportSize({ width: 900, height: 700 });
    await page.goto(
      `/browser/workspace-titlebar.html?member=1&member-docs=${documentation}`);

    const header = await box(page, ".member-surface-head");
    const declaration = await box(page, ".signature-panel");
    expect(declaration.y - (header.y + header.height)).toBeCloseTo(12, 0);
    await expect(page.locator(".member-documentation")).toBeVisible();
    await expect(page.locator(".member-identity")).toBeVisible();
  }
});

test("Member Overview keeps declaration, summary, and identity in order", async ({
  page,
}) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  await page.goto(
    "/browser/workspace-titlebar.html?member=1&member-docs=summary");

  const order = await page.evaluate(() => {
    const declaration = document.querySelector(".signature-panel");
    const summary = document.querySelector(".member-documentation");
    const identity = document.querySelector(".member-identity");
    if (!declaration || !summary || !identity)
      throw new Error("The Member Overview top-content regions are unavailable.");
    return [
      Boolean(declaration.compareDocumentPosition(summary)
        & Node.DOCUMENT_POSITION_FOLLOWING),
      Boolean(summary.compareDocumentPosition(identity)
        & Node.DOCUMENT_POSITION_FOLLOWING),
    ];
  });
  expect(order).toEqual([true, true]);

  const detail = await box(page, ".detail-pane");
  const declaration = await box(page, ".signature-panel");
  const summary = await box(page, ".member-documentation");
  const parameters = await box(page, ".member-parameters");
  const parameterProse = await box(
    page,
    ".member-parameters .member-contract-list > div:first-child dd p");
  const returnsProse = await box(page, ".member-returns dd p");
  expect(detail.x + detail.width - (declaration.x + declaration.width))
    .toBeCloseTo(16, 0);
  expect(summary.width).toBeLessThan(declaration.width);
  expect(parameters.width).toBeLessThanOrEqual(900);
  expect(parameterProse.width).toBeLessThanOrEqual(900);
  expect(returnsProse.width).toBeLessThanOrEqual(900);
  expect(declaration.width).toBeGreaterThan(parameters.width);
});

test("Member Overview presents a compact structured contract body", async ({
  page,
}) => {
  await page.setViewportSize({ width: 1440, height: 1000 });
  await page.goto(
    "/browser/workspace-titlebar.html?member=1&member-docs=summary");

  await expect(page.locator(".member-contract-heading h2"))
    .toHaveText(["Parameters", "Returns", "Exceptions"]);
  await expect(page.locator(".member-parameters .member-contract-heading span"))
    .toHaveText("3 parameters");
  await expect(page.locator(".member-exceptions .member-contract-heading span"))
    .toHaveText("2 documented");
  await expect(page.locator(".member-parameters a")).toHaveCount(0);
  await expect(page.locator(".member-contract-name"))
    .toHaveText(["utf8Json", "jsonTypeInfo", "propertyNamingPolicy"]);
  await expect(page.locator(".member-contract-default"))
    .toHaveText(/Default\s+"camelCasePropertyNamingAndCaseInsensitive"/);
  await expect(page.locator(".member-applicability"))
    .toHaveText(/Applies to\s*net10\.0/);
  expect(await page.locator(".member-contract-heading h2").first()
    .evaluate(element => getComputedStyle(element).fontSize)).toBe("16px");
  const wideParameterIdentity = await box(
    page,
    ".member-parameters .member-contract-list > div:first-child dt");
  const wideParameterDocumentation = await box(
    page,
    ".member-parameters .member-contract-list > div:first-child dd");
  expect(wideParameterDocumentation.y)
    .toBeCloseTo(wideParameterIdentity.y, 0);

  const order = await page.evaluate(() => {
    const selectors = [
      ".member-identity",
      ".member-parameters",
      ".member-returns",
      ".member-exceptions",
      ".member-applicability",
    ];
    const regions = selectors.map(selector => document.querySelector(selector));
    if (regions.some(region => !region))
      throw new Error("The Member Overview contract regions are unavailable.");
    return regions.slice(0, -1).every((region, index) =>
      Boolean(region!.compareDocumentPosition(regions[index + 1]!)
        & Node.DOCUMENT_POSITION_FOLLOWING));
  });
  expect(order).toBe(true);
});

test("Member Overview distinguishes lower documentation states", async ({
  page,
}) => {
  const states = [
    {
      mode: "loading",
      parameter: "Loading parameter documentation…",
      exceptions: "Loading documented exceptions…",
    },
    {
      mode: "error",
      parameter: "Parameter documentation is unavailable.",
      exceptions: "Exception documentation is unavailable.",
    },
    {
      mode: "missing",
      parameter:
        "No parameter documentation was found in the package XML documentation.",
      exceptions: "No exceptions are documented for this overload.",
    },
  ];
  for (const state of states) {
    await page.goto(
      `/browser/workspace-titlebar.html?member=1&member-docs=${state.mode}`);
    await expect(page.locator(".member-parameters dd p").first())
      .toHaveText(state.parameter);
    await expect(page.locator(".member-exceptions > p"))
      .toHaveText(state.exceptions);
  }
});

test("Member Overview responds to constrained pane widths", async ({
  page,
}) => {
  for (const width of [860, 480]) {
    await page.setViewportSize({ width, height: 700 });
    await page.goto(
      "/browser/workspace-titlebar.html?member=1&member-docs=summary");

    const label = await box(
      page,
      ".member-identity dl > div:first-child dt");
    const value = await box(
      page,
      ".member-identity dl > div:first-child dd");
    const parameterIdentity = await box(
      page,
      ".member-parameters .member-contract-list > div:first-child dt");
    const parameterDocumentation = await box(
      page,
      ".member-parameters .member-contract-list > div:first-child dd");
    expect(value.y).toBeGreaterThan(label.y);
    expect(parameterDocumentation.y).toBeGreaterThan(parameterIdentity.y);
    for (const selector of [
      ".member-parameters .member-contract-list > div:nth-child(2) dt",
      ".member-parameters .member-contract-list > div:nth-child(3) dt",
      ".member-exceptions .member-contract-list > div:first-child dt",
    ]) {
      expect(await page.locator(selector).evaluate(element =>
        element.scrollWidth <= element.clientWidth)).toBe(true);
    }
    expect(await page.evaluate(() =>
      document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
  }

  await page.goto(
    "/browser/workspace-titlebar.html?member=1&member-docs=summary&long-signature=1");
  const declaration = await box(page, ".signature-panel");
  const declarationHeader = await box(page, ".signature-language");
  const copy = await box(page, "#copy-signature");
  const code = await page.locator(".signature-code").evaluate(element => ({
    clientWidth: element.clientWidth,
    scrollWidth: element.scrollWidth,
  }));
  const longParameterRow = await page.locator(
    ".member-parameters .member-contract-list > div:first-child")
    .evaluate(element => ({
      clientWidth: element.clientWidth,
      scrollWidth: element.scrollWidth,
    }));
  const memberScroller = await page.locator(".member-surface-scroll")
    .evaluate(element => ({
      clientWidth: element.clientWidth,
      scrollWidth: element.scrollWidth,
    }));
  expect(code.scrollWidth).toBeGreaterThan(code.clientWidth);
  expect(longParameterRow.scrollWidth)
    .toBeLessThanOrEqual(longParameterRow.clientWidth);
  expect(memberScroller.scrollWidth)
    .toBeLessThanOrEqual(memberScroller.clientWidth);
  expect(copy.x + copy.width)
    .toBeLessThanOrEqual(declaration.x + declaration.width);
  expect(copy.y).toBeGreaterThanOrEqual(declarationHeader.y);
  expect(copy.y + copy.height)
    .toBeLessThanOrEqual(declarationHeader.y + declarationHeader.height);
});

test("the Application menu owns global actions and modal focus return", async ({
  page,
}) => {
  await page.setViewportSize({ width: 400, height: 520 });
  await page.goto("/browser/workspace-titlebar.html?member=1");

  const button = page.locator("#application-menu-button");
  await button.focus();
  await page.keyboard.press("ArrowDown");
  const items = page.getByRole("menuitem");
  await expect(items).toHaveText(["Share", "Settings", "Keyboard help"]);
  await expect(items.first()).toBeFocused();
  await expect(page.getByRole("separator")).toHaveCount(1);
  await expect(page.locator("#application-menu-overlay > #application-menu"))
    .toBeVisible();
  const popup = await box(page, "#application-menu");
  expect(popup.x).toBeGreaterThanOrEqual(0);
  expect(popup.y).toBeGreaterThanOrEqual(0);
  expect(popup.x + popup.width).toBeLessThanOrEqual(400);
  expect(popup.y + popup.height).toBeLessThanOrEqual(520);

  await page.keyboard.press("End");
  await expect(items.last()).toBeFocused();
  await page.keyboard.press("ArrowDown");
  await expect(items.first()).toBeFocused();
  await page.keyboard.press("ArrowUp");
  await expect(items.last()).toBeFocused();
  await page.keyboard.press("Escape");
  await expect(button).toBeFocused();
  await expect(page.locator("#application-menu")).toBeHidden();

  await button.press("Enter");
  await expect(items.first()).toBeFocused();
  await page.keyboard.press("Escape");
  await button.press("Space");
  await expect(items.first()).toBeFocused();
  await page.keyboard.press("Tab");
  await expect(page.locator("#application-menu")).toBeHidden();

  await button.click();
  await page.locator(".workspace").click({ position: { x: 2, y: 2 } });
  await expect(page.locator("#application-menu")).toBeHidden();

  await button.click();
  await page.getByRole("menuitem", { name: "Settings" }).click();
  await expect(page.getByRole("dialog", { name: "Settings" })).toBeVisible();
  await expect(page.locator("#settings-title")).toBeFocused();
  await expect(page.locator(".workbench")).toHaveAttribute("inert", "");
  await page.keyboard.press("Enter");
  await expect(page.locator("body")).not.toHaveAttribute(
    "data-drill-in",
    "true");
  await page.keyboard.press("Shift+Tab");
  await expect(page.getByRole("button", { name: "Light" })).toBeFocused();
  await page.keyboard.press("Escape");
  await expect(page.getByRole("dialog", { name: "Settings" })).toBeHidden();
  await expect(button).toBeFocused();

  await button.click();
  await page.getByRole("menuitem", { name: "Keyboard help" }).click();
  await expect(page.getByRole("dialog", { name: "Keyboard help" })).toBeVisible();
  await expect(page.locator("#keyboard-help-title")).toBeFocused();
  await page.keyboard.press("Enter");
  await expect(page.locator("body")).not.toHaveAttribute(
    "data-drill-in",
    "true");
  await page.locator("#keyboard-help-close").click();
  await expect(button).toBeFocused();
  await page.locator("#inspector-panel").evaluate(element =>
    element.setAttribute("tabindex", "-1"));
  await page.locator("#inspector-panel").focus();
  await page.keyboard.press("Enter");
  await expect(page.locator("body")).toHaveAttribute("data-drill-in", "true");

  await button.click();
  await items.first().click();
  await expect(page.locator("body")).toHaveAttribute("data-shared", "true");
  await expect(button).toBeFocused();

  await page.setViewportSize({ width: 800, height: 520 });
  await page.evaluate(() => delete document.body.dataset.shared);
  await button.click();
  await items.first().click();
  await page.locator(".brand").focus();
  await expect(page.locator(".brand")).toBeFocused();
  await expect(page.locator("body")).toHaveAttribute("data-shared", "true");
  await expect(page.locator(".brand")).toBeFocused();

  await button.click();
  await page.setViewportSize({ width: 400, height: 140 });
  const shortPopup = await box(page, "#application-menu");
  expect(shortPopup.y).toBeGreaterThanOrEqual(0);
  expect(shortPopup.y + shortPopup.height).toBeLessThanOrEqual(140);
  expect(await page.locator("#application-menu").evaluate(menu =>
    menu.scrollHeight > menu.clientHeight)).toBe(true);
  await page.keyboard.press("Escape");

  await page.setViewportSize({ width: 400, height: 520 });
  await button.click();
  const visualBounds = await page.evaluate(() => {
    const viewport = window.visualViewport;
    if (!viewport) throw new Error("Visual viewport is unavailable.");
    for (const [property, value] of [
      ["height", 120],
      ["width", 120],
      ["offsetTop", 40],
      ["offsetLeft", 40],
    ] as const) {
      Object.defineProperty(
        viewport,
        property,
        { configurable: true, value });
    }
    viewport.dispatchEvent(new Event("scroll"));
    return {
      bottom: viewport.offsetTop + viewport.height - 8,
      left: viewport.offsetLeft + 8,
      right: viewport.offsetLeft + viewport.width - 8,
      top: viewport.offsetTop + 8,
    };
  });
  const visualPopup = await box(page, "#application-menu");
  expect(visualPopup.x).toBeGreaterThanOrEqual(visualBounds.left);
  expect(visualPopup.x + visualPopup.width)
    .toBeLessThanOrEqual(visualBounds.right);
  expect(visualPopup.y).toBeGreaterThanOrEqual(visualBounds.top);
  expect(visualPopup.y + visualPopup.height)
    .toBeLessThanOrEqual(visualBounds.bottom);
  expect(await page.locator("#application-menu").evaluate(menu =>
    Number.parseFloat(getComputedStyle(menu).minWidth)))
    .toBeLessThan(180);
  await page.keyboard.press("Escape");
});

test("Keyboard help reflects current command availability and surface", async ({
  page,
}) => {
  await page.goto("/browser/workspace-titlebar.html?package=1");
  await page.getByRole("button", { name: "Application menu" }).click();
  await page.getByRole("menuitem", { name: "Keyboard help" }).click();
  await expect(page.getByText("Search types, members, and packages"))
    .toBeVisible();
  await expect(page.getByText("Leave the current member or subject"))
    .toHaveCount(0);
  await expect(page.getByText("Go back")).toHaveCount(0);
  await expect(page.getByText("Go forward")).toHaveCount(0);

  await page.goto(
    "/browser/workspace-titlebar.html?package=1&history-back=1");
  await page.getByRole("button", { name: "Application menu" }).click();
  await page.getByRole("menuitem", { name: "Keyboard help" }).click();
  await expect(page.getByText("Go back")).toHaveCount(2);
  await expect(page.getByText("Go forward")).toHaveCount(0);

  await page.goto("/browser/workspace-titlebar.html?workspace=1");
  await page.getByRole("button", { name: "Application menu" }).click();
  await page.getByRole("menuitem", { name: "Keyboard help" }).click();
  await expect(page.getByText("Search types, members, and packages"))
    .toBeVisible();
  await expect(page.getByText("Focus the current list filter")).toHaveCount(0);
  await expect(page.getByText("Select a subject or inspector")).toHaveCount(0);
  await expect(page.getByText("Move across subjects or inspectors"))
    .toHaveCount(0);
  await expect(page.getByText("Open the selected item")).toBeVisible();
  await expect(page.getByText("Leave the current member or subject"))
    .toHaveCount(0);

  await page.goto("/browser/workspace-titlebar.html?member=1&graph=1");
  await page.getByRole("button", { name: "Application menu" }).click();
  await page.getByRole("menuitem", { name: "Keyboard help" }).click();
  await expect(page.getByText("Zoom the current graph")).toBeVisible();
  await expect(page.getByText("Pan the current graph horizontally"))
    .toBeVisible();
  await expect(page.getByText("Pan the current graph vertically"))
    .toBeVisible();
});

test("application menu keeps a fixed trailing slot outside SlideStrip overflow", async ({
  page,
}) => {
  for (const width of [1440, 400, 220]) {
    await page.setViewportSize({ width, height: 900 });
    await page.goto("/browser/workspace-titlebar.html?member=1");

    const subjectRegion = await box(page, ".subject-inspector-region");
    const menuSlot = await box(page, ".application-menu-slot");
    expect(subjectRegion.x + subjectRegion.width)
      .toBeLessThanOrEqual(menuSlot.x + 1);
    expect(menuSlot.x + menuSlot.width).toBeCloseTo(width, 0);
    await expect(page.locator(
      ".subject-inspector-region #application-menu-button",
    )).toHaveCount(0);
    expect(await page.evaluate(() =>
      document.documentElement.scrollWidth
      - document.documentElement.clientWidth)).toBeLessThanOrEqual(0);
  }
});

test("application and contextual actions preserve focus across responsive layout", async ({
  page,
}) => {
  await page.setViewportSize({ width: 1120, height: 900 });
  await page.goto("/browser/workspace-titlebar.html?member=1&source=1");

  const copy = page.locator("#copy-source");
  await copy.focus();
  await page.setViewportSize({ width: 400, height: 900 });
  await expect(copy).toBeFocused();

  const menuButton = page.locator("#application-menu-button");
  await menuButton.focus();
  await page.setViewportSize({ width: 600, height: 900 });
  await expect(menuButton).toBeFocused();

  await menuButton.click();
  await page.getByRole("menuitem", { name: "Settings" }).click();
  await page.setViewportSize({ width: 400, height: 900 });
  await expect(page.locator("#settings-title")).toBeFocused();
  await page.keyboard.press("Escape");
  await expect(menuButton).toBeFocused();
});

test("application menu returns focus to its replacement shell identity", async ({
  page,
}) => {
  await page.goto("/browser/workspace-titlebar.html?member=1");
  const button = page.locator("#application-menu-button");
  await button.click();
  await expect(page.getByRole("menuitem", { name: "Share" })).toBeFocused();

  await page.evaluate(() => window.rerenderApplicationMenuProbe());

  await expect(button).toBeFocused();
  await expect(page.locator("#application-menu")).toBeHidden();
});

test("the inspected target occupies the second row and package selectors stay in content", async ({
  page,
}) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  await page.goto("/browser/workspace-titlebar.html?package=1");

  const target = await box(page, ".inspected-target");
  const lensbar = await box(page, ".lensbar");
  const titlebar = await box(page, ".titlebar");
  const targetbar = await box(page, ".targetbar");

  expect(target.x).toBeLessThan(20);
  expect(target.y).toBeGreaterThanOrEqual(targetbar.y);
  expect(target.y + target.height)
    .toBeLessThanOrEqual(targetbar.y + targetbar.height);
  expect(Math.abs(
    target.y + target.height / 2
    - (targetbar.y + targetbar.height / 2),
  )).toBeLessThanOrEqual(1);
  expect(targetbar.y).toBeGreaterThanOrEqual(titlebar.y + titlebar.height);
  expect(lensbar.y).toBeCloseTo(titlebar.y, 0);
  await expect(page.locator(".titlebar .lensbar")).toBeVisible();
  await expect(page.locator(".targetbar .lensbar")).toHaveCount(0);
  await expect(page.locator(".subject-path-segment.root.current")).toHaveCSS(
    "color",
    "rgb(232, 233, 228)",
  );
  await expect(page.locator(".titlebar #package-version")).toHaveCount(0);
  await expect(page.locator(".titlebar #framework")).toHaveCount(0);
  await expect(page.locator(".detail-scroll #package-version")).toBeVisible();
  await expect(page.locator(".detail-scroll #framework")).toBeVisible();
  await expect(page.locator("#go-home")).toHaveCount(0);

  const packageIcon = await box(page, ".subject-icon");
  expect(packageIcon.width).toBeCloseTo(20, 0);
  expect(packageIcon.height).toBeCloseTo(20, 0);
  await expect(page.locator(".subject-icon img")).toHaveAttribute(
    "src",
    /^data:image\/png;base64,/);
  await expect(page.locator(".subject-icon img")).toHaveJSProperty(
    "naturalWidth",
    456);
});

test("keyboard tab activation preserves adaptive focus across shell replacement", async ({
  page,
}) => {
  await page.goto("/browser/workspace-titlebar.html");

  const api = page.getByRole("tab", { name: "API" });
  const metadata = page.getByRole("tab", { name: "Metadata" });
  await api.focus();
  await page.keyboard.press("ArrowRight");
  await expect(metadata).toBeFocused();
  await expect(metadata).toHaveAttribute("aria-selected", "false");
  await page.evaluate(() => window.rerenderScopeBarProbe());
  await expect(api).toHaveAttribute("aria-selected", "true");
  await expect(api).toHaveAttribute("tabindex", "-1");
  await expect(metadata).toHaveAttribute("aria-selected", "false");
  await expect(metadata).toHaveAttribute("tabindex", "0");
  await expect(metadata).toBeFocused();
  await page.keyboard.press("Enter");
  await expect(metadata).toHaveAttribute("aria-selected", "true");
  await expect(metadata).toBeFocused();

  const type = page.getByRole("tab", { name: "Type" });
  const librarySubject = page.getByRole("tab", { name: "Library" });
  const packageSubject = page.getByRole("tab", { name: "Package" });
  await type.focus();
  await page.keyboard.press("ArrowLeft");
  await expect(librarySubject).toBeFocused();
  await expect(librarySubject).toHaveAttribute("aria-selected", "false");
  await expect(type).toHaveAttribute("aria-selected", "true");
  await page.keyboard.press("Enter");
  await expect(librarySubject).toHaveAttribute("aria-selected", "true");
  if (await librarySubject.isVisible()) {
    await expect(librarySubject).toBeFocused();
  } else {
    await expect(page.locator("[data-navigation-trigger='subject']"))
      .toBeFocused();
  }
  await expect(packageSubject).toHaveAttribute("aria-selected", "false");
});

test("removed focused tabs fall back to the persistent shell control", async ({
  page,
}) => {
  await page.goto("/browser/workspace-titlebar.html");

  const metadata = page.getByRole("tab", { name: "Metadata" });
  await metadata.focus();
  await page.evaluate(() => window.renderPackageScopeProbe());

  await expect(page.locator(".brand")).toBeFocused();
  await expect(
    page.locator("[data-inspector-tab][data-package-lens='overview']"),
  ).toHaveAttribute("tabindex", "0");
});

test("packages without an embedded icon use NuGet's package fallback", async ({
  page,
}) => {
  await page.goto("/browser/workspace-titlebar.html?package=1&fallback=1");

  await expect(page.locator(".subject-icon img")).toHaveAttribute(
    "src",
    "https://nuget.org/Content/gallery/img/default-package-icon-256x256.png");
  await expect(page.locator(".subject-icon")).not.toContainText("⬡");
});

test("Spotlight keeps its Search shortcut guidance visible when narrow", async ({
  page,
}) => {
  await page.setViewportSize({ width: 280, height: 700 });
  await page.goto("/browser/workspace-titlebar.html");

  for (const scope of ["all", "commands"] as const) {
    await renderSpotlightFooter(page, scope);
    const modal = await box(page, ".spotlight");
    const guidance = page.locator(".spotlight-foot span");
    await expect(guidance.first()).toContainText("Ctrl P search");

    for (const item of await guidance.all()) {
      const itemBox = await item.boundingBox();
      expect(itemBox).not.toBeNull();
      expect(itemBox!.x).toBeGreaterThanOrEqual(modal.x);
      expect(itemBox!.x + itemBox!.width)
        .toBeLessThanOrEqual(modal.x + modal.width);
    }
  }
});

async function windowContinuityProbe(
  page: Page,
  windowContinuity?: SlideStripPolicy["windowContinuity"],
) {
  await page.goto("/browser/workspace-titlebar.html");

  return page.evaluate(async continuityPolicy => {
    const { SlideStripDomController } = await import(
      "../src/slide-strip-dom.ts");
    document.head.insertAdjacentHTML(
      "beforeend",
      `<style>
        .resize-continuity-probe .slide-strip-items {
          display: flex;
          gap: 0;
        }
        .resize-continuity-probe button {
          padding: 0;
          border: 0;
        }
        .resize-continuity-probe [data-slide-strip-id="a"] {
          width: 60px;
        }
        .resize-continuity-probe [data-slide-strip-id="b"] {
          width: 40px;
        }
        .resize-continuity-probe [data-slide-strip-id="c"] {
          width: 70px;
        }
      </style>`);
    const element = document.createElement("div");
    element.className = "slide-strip resize-continuity-probe";
    element.innerHTML = `
      <div class="slide-strip-items">
        <button data-slide-strip-id="a">
          <span data-slide-strip-representation="label">A</span>
        </button>
        <button data-slide-strip-id="b">
          <span data-slide-strip-representation="label">B</span>
        </button>
        <button data-slide-strip-id="c">
          <span data-slide-strip-representation="label">C</span>
        </button>
      </div>
      <span data-slide-strip-before></span>
      <span data-slide-strip-after></span>`;
    document.body.append(element);
    const continuity: { key: string; leadingId?: string } = {
      key: "resize-continuity",
    };
    const createController = (continuityKey: string) => new SlideStripDomController(
      element,
      [
        { id: "a", label: "A" },
        { id: "b", label: "B" },
        { id: "c", label: "C" },
      ],
      {
        modes: [{ kind: "label", minimumVisible: 1, gap: 0 }],
        initialAnchor: "b",
        preferredDirection: "after",
        continuityKey,
        ...(continuityPolicy ? { windowContinuity: continuityPolicy } : {}),
        fallbackVisibilityFloor: 20,
        oversizedAlignment: "start",
      },
      continuity);
    const controller = createController("resize-continuity");
    const snapshot = () => ({
      visible: [...element.querySelectorAll<HTMLElement>(
        "[data-slide-strip-id]:not([hidden])")]
        .map(item => item.dataset.slideStripId),
      leading: continuity.leadingId,
    });

    controller.apply(controller.resolve(100));
    const initial = snapshot();
    controller.apply(controller.resolve(110));
    const wider = snapshot();
    controller.apply(controller.resolve(170));
    const complete = snapshot();
    const edgeNoOp = controller.slide("after");
    controller.apply(controller.resolve(60));
    const narrowed = snapshot();
    let focusBeforeSlide = null;
    const focusOutside = () => {
      const button = document.querySelector<HTMLElement>(
        "#application-menu-button");
      if (!button) throw new Error("The external focus target is missing.");
      button.focus();
    };
    if (continuityPolicy === "anchor-until-slide") {
      controller.revealForFocus("c");
      const focused = snapshot();
      const focusedId = document.activeElement instanceof HTMLElement
        ? document.activeElement.dataset.slideStripId
        : undefined;
      focusOutside();
      controller.apply(controller.resolve(60));
      focusBeforeSlide = { focused, focusedId, afterFocus: snapshot() };
    }
    controller.apply(controller.resolve(100));
    const moved = controller.slide("after");
    const slid = snapshot();
    controller.apply(controller.resolve(170));
    const expandedAfterSlide = snapshot();
    controller.apply(controller.resolve(110));
    const narrowedAfterSlide = snapshot();
    controller.revealForFocus("a");
    const focusedAfterSlide = snapshot();
    focusOutside();
    controller.apply(controller.resolve(70));
    const afterFocus = snapshot();
    const resetController = createController("reset-continuity");
    resetController.apply(resetController.resolve(60));
    const reset = snapshot();

    return {
      initial,
      wider,
      complete,
      edgeNoOp,
      narrowed,
      focusBeforeSlide,
      moved,
      slid,
      expandedAfterSlide,
      narrowedAfterSlide,
      focusedAfterSlide,
      afterFocus,
      reset,
    };
  }, windowContinuity);
}

test("default window continuity retains the initially applied window", async ({
  page,
}) => {
  const state = await windowContinuityProbe(page);

  expect(state).toEqual({
    initial: { visible: ["a", "b"], leading: "a" },
    wider: { visible: ["a", "b"], leading: "a" },
    complete: { visible: ["a", "b", "c"], leading: "a" },
    edgeNoOp: false,
    narrowed: { visible: ["a"], leading: "a" },
    focusBeforeSlide: null,
    moved: true,
    slid: { visible: ["c"], leading: "c" },
    expandedAfterSlide: {
      visible: ["a", "b", "c"],
      leading: "c",
    },
    narrowedAfterSlide: { visible: ["b", "c"], leading: "c" },
    focusedAfterSlide: { visible: ["a", "b"], leading: "a" },
    afterFocus: { visible: ["a"], leading: "a" },
    reset: { visible: ["b"], leading: "b" },
  });
});

test("anchor-following window continuity retains only explicit slides", async ({
  page,
}) => {
  const state = await windowContinuityProbe(page, "anchor-until-slide");

  expect(state).toEqual({
    initial: { visible: ["a", "b"], leading: undefined },
    wider: { visible: ["b", "c"], leading: undefined },
    complete: { visible: ["a", "b", "c"], leading: undefined },
    edgeNoOp: false,
    narrowed: { visible: ["b"], leading: undefined },
    focusBeforeSlide: {
      focused: { visible: ["c"], leading: undefined },
      focusedId: "c",
      afterFocus: { visible: ["b"], leading: undefined },
    },
    moved: true,
    slid: { visible: ["c"], leading: "c" },
    expandedAfterSlide: {
      visible: ["a", "b", "c"],
      leading: "c",
    },
    narrowedAfterSlide: { visible: ["b", "c"], leading: "c" },
    focusedAfterSlide: { visible: ["a", "b"], leading: "c" },
    afterFocus: { visible: ["c"], leading: "c" },
    reset: { visible: ["b"], leading: undefined },
  });
});

test("a mounted empty SlideStrip applies its empty state", async ({ page }) => {
  await page.goto("/browser/workspace-titlebar.html");

  const state = await page.evaluate(async () => {
    const { SlideStripDomController } = await import(
      "../src/slide-strip-dom.ts");
    const outside = document.createElement("button");
    outside.textContent = "Outside";
    document.body.append(outside);
    outside.focus();

    const element = document.createElement("div");
    element.className = "slide-strip";
    element.innerHTML = `
      <div class="slide-strip-items"></div>
      <span data-slide-strip-before></span>
      <span data-slide-strip-after></span>`;
    document.body.append(element);
    const controller = new SlideStripDomController(
      element,
      [],
      {
        modes: [{ kind: "label", minimumVisible: 1, gap: 0 }],
        initialAnchor: "empty",
        preferredDirection: "after",
        continuityKey: "empty",
        fallbackVisibilityFloor: 28,
        oversizedAlignment: "start",
      },
      { key: "empty" });
    const resolved = controller.resolve(100);
    controller.apply(resolved);
    return {
      result: resolved.result,
      current: controller.current,
      width: element.style.width,
      mode: element.dataset.mode,
      minimumWidth: controller.minimumOuterWidth,
      preferredWidth: controller.preferredOuterWidth,
      fallbackWidth: controller.fallbackOuterWidth,
      beforeHidden: element.querySelector<HTMLElement>(
        "[data-slide-strip-before]")?.hidden,
      afterHidden: element.querySelector<HTMLElement>(
        "[data-slide-strip-after]")?.hidden,
      slide: controller.slide("after"),
      outsideFocused: document.activeElement === outside,
    };
  });

  expect(state).toEqual({
    result: null,
    current: null,
    width: "100px",
    mode: undefined,
    minimumWidth: 0,
    preferredWidth: 0,
    fallbackWidth: 0,
    beforeHidden: true,
    afterHidden: true,
    slide: false,
    outsideFocused: true,
  });
});

test("subject-only layout reserves the empty-strip context label", async ({
  page,
}) => {
  await page.setViewportSize({ width: 800, height: 900 });
  await page.goto("/browser/workspace-titlebar.html?member=1&empty=1");

  const bounds = await page.evaluate(() => {
    const bar = document.querySelector<HTMLElement>(".lensbar");
    const context = document.querySelector<HTMLElement>(".lens-context");
    if (!bar || !context) throw new Error("Subject-only context is unavailable.");
    return {
      barRight: bar.getBoundingClientRect().right,
      contextRight: context.getBoundingClientRect().right,
    };
  });
  expect(bounds.contextRight).toBeLessThanOrEqual(bounds.barRight);
  await expect(page.locator(".lens-context")).toHaveText(
    "Filtered member list");
});

test("Annotated Source keeps its complete action group under shell pressure", async ({
  page,
}) => {
  for (const width of [1120, 1050, 800, 600, 400]) {
    await page.setViewportSize({ width, height: 900 });
    await page.goto("/browser/workspace-titlebar.html?member=1&annotated=1");

    const actions = await box(page, ".working-surface-actions");
    const copy = await box(page, "#copy-annotated");
    const explore = await box(page, "#explore-annotated");
    await expect(page.locator("#copy-annotated")).toBeVisible();
    await expect(page.locator("#explore-annotated")).toBeVisible();
    expect(copy.x).toBeGreaterThanOrEqual(actions.x - 1);
    expect(explore.x).toBeGreaterThanOrEqual(actions.x - 1);
    expect(explore.x + explore.width)
      .toBeLessThanOrEqual(actions.x + actions.width);
    expect(await page.evaluate(() =>
      document.documentElement.scrollWidth
      - document.documentElement.clientWidth)).toBeLessThanOrEqual(0);
  }
});

test("Source fills the detail area below working-surface actions and above provenance", async ({
  page,
}) => {
  for (const width of [1120, 600, 400]) {
    await page.setViewportSize({ width, height: 900 });
    await page.goto("/browser/workspace-titlebar.html?member=1&source=1");

    await expect(page.locator("#copy-source")).toBeVisible();
    await expect(page.locator(".shell-action-link")).toHaveText("Open");
    await expect(page.locator("#inspector-panel > h1")).toHaveCount(0);
    await expect(
      page.getByRole("group", { name: "Source actions" }),
    ).toBeVisible();
    await expect(
      page.locator("#application-menu-button"),
    ).toBeVisible();
    await expect(
      page.getByRole("region", { name: "Source code" }),
    ).toBeVisible();

    const inspector = await box(page, "#inspector-panel");
    const source = await box(page, ".source-result");
    const subjectRegion = await box(page, ".subject-inspector-region");
    const actions = await box(page, ".working-surface-actions");
    const menuSlot = await box(page, ".application-menu-slot");
    const targetbar = await box(page, ".targetbar");
    const code = await box(page, ".source-result pre");
    const provenance = await box(page, ".source-provenance");
    expect(source.x).toBeCloseTo(inspector.x, 0);
    expect(source.y).toBeCloseTo(inspector.y, 0);
    expect(source.width).toBeCloseTo(inspector.width, 0);
    expect(source.height).toBeCloseTo(inspector.height, 0);
    expect(menuSlot.y + menuSlot.height).toBeLessThanOrEqual(targetbar.y + 1);
    expect(subjectRegion.y + subjectRegion.height)
      .toBeLessThanOrEqual(targetbar.y + 1);
    expect(actions.y).toBeGreaterThanOrEqual(targetbar.y);
    expect(actions.y + actions.height)
      .toBeLessThanOrEqual(targetbar.y + targetbar.height);
    expect(actions.x + actions.width)
      .toBeLessThanOrEqual(targetbar.x + targetbar.width);
    expect(code.y).toBeCloseTo(source.y, 0);
    expect(code.y + code.height).toBeLessThanOrEqual(provenance.y + 1);
    expect(provenance.y + provenance.height)
      .toBeCloseTo(source.y + source.height, 0);
    expect(await page.evaluate(() =>
      document.documentElement.scrollWidth
      - document.documentElement.clientWidth)).toBeLessThanOrEqual(0);
  }

  for (const width of [1920, 1440, 1120, 600, 400]) {
    await page.setViewportSize({ width, height: 900 });
    await page.goto(
      "/browser/workspace-titlebar.html?member=1&source=1&limitation=1");

    const provenance = await box(
      page,
      ".source-provenance > span:first-of-type");
    const limitation = await box(
      page,
      ".source-provenance > .graph-source-status");
    expect(provenance.width).toBeGreaterThan(16);
    expect(limitation.width).toBeGreaterThan(16);
    expect(limitation.y).toBeGreaterThanOrEqual(
      provenance.y + provenance.height - 1);
    const limitationMetrics = await page.locator(
      ".source-provenance > .graph-source-status",
    ).evaluate(element => ({
      clientWidth: element.clientWidth,
      scrollWidth: element.scrollWidth,
    }));
    expect(limitationMetrics.scrollWidth)
      .toBeLessThanOrEqual(limitationMetrics.clientWidth);
    expect(await page.evaluate(() =>
      document.documentElement.scrollWidth
      - document.documentElement.clientWidth)).toBeLessThanOrEqual(0);
  }
});

test("the target row advertises the typed Package, Library, Type, and Member path", async ({
  page,
}) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  await page.goto("/browser/workspace-titlebar.html?member=1");

  await expect(page.locator(".subject-path-segment")).toHaveText([
    "System.Text.Json",
    "System.Text.Json",
    "System.Text.Json.JsonSerializer",
    "DeserializeSync",
  ]);
  await expect(page.locator(".subject-path-separator")).toHaveCount(3);
  await expect(page.locator("[data-subject-copy]")).toHaveCount(4);
  await expect(page.locator(".targetbar .subject-path")).toBeVisible();
  await expect(page.locator(".titlebar .scope-switch")).toBeVisible();
  await expect(page.locator(".titlebar .lens")).toHaveCount(5);
  await expect(page.locator(".subject-path-segment.current")).toHaveCSS(
    "color",
    "rgb(157, 140, 255)");
  const packageText = await page.locator(".subject-path-segment").nth(0)
    .evaluate(element => getComputedStyle(element).fontSize);
  const typeText = await page.locator(".subject-path-segment").nth(1)
    .evaluate(element => getComputedStyle(element).fontSize);
  const typeWeight = await page.locator(".subject-path-segment").nth(1)
    .evaluate(element => getComputedStyle(element).fontWeight);
  expect(Number.parseFloat(packageText)).toBeCloseTo(
    Number.parseFloat(typeText), 1);
  expect(Number.parseInt(typeWeight, 10)).toBeLessThan(600);
  await page.locator("[data-subject-copy='2']").click();
  await expect(page.locator("body")).toHaveAttribute(
    "data-copied-subject",
    "System.Text.Json.JsonSerializer");
  const search = await box(page, "#open-search");
  const forward = await box(page, "#nav-forward");
  expect(forward.x + forward.width).toBeLessThanOrEqual(search.x);
  expect(search.x - (forward.x + forward.width)).toBeLessThanOrEqual(7);
  const menuSlot = await box(page, ".application-menu-slot");
  expect(search.x + search.width).toBeLessThanOrEqual(menuSlot.x);
  const titlebar = await box(page, ".titlebar");
  const targetbar = await box(page, ".targetbar");
  const target = await box(page, ".inspected-target");
  const workspace = await box(page, ".workspace");
  const pathSegments = await page.locator(".subject-path-segment")
    .evaluateAll(segments => segments.map(segment => {
      const bounds = segment.getBoundingClientRect();
      return { x: bounds.x, right: bounds.right };
    }));
  expect(target.x).toBeLessThan(20);
  expect(target.y).toBeGreaterThanOrEqual(targetbar.y);
  expect(target.y + target.height)
    .toBeLessThanOrEqual(targetbar.y + targetbar.height);
  expect(Math.abs(
    target.y + target.height / 2
    - (targetbar.y + targetbar.height / 2),
  )).toBeLessThanOrEqual(1);
  expect(titlebar.y + titlebar.height).toBeLessThanOrEqual(targetbar.y);
  expect(targetbar.x).toBe(0);
  expect(targetbar.width).toBeCloseTo(1440, 0);
  expect(targetbar.y + targetbar.height).toBeLessThanOrEqual(workspace.y);
  for (let index = 1; index < pathSegments.length; index++) {
    const current = pathSegments[index];
    const previous = pathSegments[index - 1];
    if (!current || !previous) {
      throw new Error("Inspected-target path geometry is incomplete.");
    }
    expect(current.x - previous.right)
      .toBeLessThan(40);
  }
});

test("Workspace keeps the singular Workspace visible and menu fixed", async ({
  page,
}) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  await page.goto("/browser/workspace-titlebar.html?workspace=1");

  const list = await box(page, ".workspace-list");
  const defaultWorkspace = await box(page, ".workspace-card");
  const applicationMenu = await box(page, "#application-menu-button");

  expect(list.height).toBeGreaterThan(200);
  expect(defaultWorkspace.y + defaultWorkspace.height)
    .toBeLessThanOrEqual(list.y + list.height);
  expect(applicationMenu.width).toBeCloseTo(30, 0);
  await page.locator("#application-menu-button").click();
  await expect(page.getByRole("menuitem", { name: "Share" })).toBeVisible();
  await expect(page.locator("#copy-name")).toHaveCount(0);
  await expect(page.locator("[data-subject-copy]")).toHaveCount(0);
  await expect(page.locator("[data-application-scope='workspace']"))
    .toHaveAttribute("aria-current", "page");
  await expect(page.locator("[data-application-scope='workspace']"))
    .toBeVisible();
  await expect(page.locator("[data-subject-tab][data-scope='package']"))
    .toHaveAttribute("aria-selected", "false");
});

test("application scopes yield before inspection identity without dropping focus", async ({
  page,
}) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  await page.goto("/browser/workspace-titlebar.html?member=1");

  const query = page.locator("[data-application-scope='query']");
  await query.focus();
  await page.setViewportSize({ width: 1100, height: 900 });
  await expect(page.locator(".brand")).toBeFocused();
  await expect(page.locator(".titlebar > .application-scope-region"))
    .toBeHidden();
  await expect(
    page.locator("[data-subject-tab]"),
  ).toHaveCount(4);
  await expect(
    page.locator("[data-inspector-tab]"),
  ).toHaveCount(5);
});

test("a trailing application scope transfers focus before terminal clipping", async ({
  page,
}) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  await page.goto("/browser/workspace-titlebar.html?member=1");

  const workspace = page.locator("[data-application-scope='workspace']");
  await workspace.focus();
  await page.setViewportSize({ width: 300, height: 900 });

  await expect(page.locator(".brand")).toBeFocused();
  await expect(page.locator(".titlebar > .application-scope-region"))
    .toBeHidden();
});

test("application scope rerenders preserve focus until responsive yielding", async ({
  page,
}) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  await page.goto("/browser/workspace-titlebar.html?member=1");

  const query = page.locator("[data-application-scope='query']");
  await query.focus();
  await expect(query).toBeVisible();
  await expect(query).toBeFocused();

  await page.evaluate(() => window.rerenderApplicationScopeProbe());

  await expect(page.locator("[data-application-scope='query']")).toBeFocused();
  await page.setViewportSize({ width: 900, height: 900 });
  await expect(page.locator(".titlebar > .application-scope-region"))
    .toBeHidden();
  await expect(page.locator(".brand")).toBeFocused();
});

test("query header omits scope buttons and preserves navigation focus across widths", async ({
  page,
}) => {
  await page.setViewportSize({ width: 700, height: 900 });
  await page.goto("/browser/workspace-titlebar.html");
  await page.evaluate(async () => {
    const { initialQueryState } = await import("../src/package-query.ts");
    const { renderPackageQueryView } =
      await import("../src/package-query-view.ts");
    const app = document.querySelector<HTMLElement>("#app");
    if (!app) throw new Error("The query focus harness root is unavailable.");
    app.innerHTML = renderPackageQueryView({
      state: initialQueryState(),
      availableFacets: [],
      escapeHtml: value => String(value),
    });
  });

  await expect(page.getByRole("button", { name: "Query", exact: true })).toHaveCount(0);
  await expect(page.getByRole("button", { name: "Workspace", exact: true })).toHaveCount(0);
  await expect(page.locator("#package-query-back")).toBeVisible();
  await page.locator("#package-query-product").focus();
  const productResult = await page.evaluate(async () => {
    const { initialQueryState } = await import("../src/package-query.ts");
    const {
      capturePackageQueryFocus,
      renderPackageQueryView,
      restorePackageQueryFocus,
    } = await import("../src/package-query-view.ts");
    const app = document.querySelector<HTMLElement>("#app");
    if (!app) throw new Error("The query focus harness root is unavailable.");
    const snapshot = capturePackageQueryFocus(document);
    app.innerHTML = renderPackageQueryView({
      state: initialQueryState(),
      availableFacets: [],
      escapeHtml: value => String(value),
    });
    return {
      restoration: restorePackageQueryFocus(document, snapshot),
      activeId: document.activeElement?.id,
    };
  });
  expect(productResult).toEqual({
    restoration: "restored",
    activeId: "package-query-product",
  });

  const back = page.locator("#package-query-back");
  await back.focus();
  await page.setViewportSize({ width: 500, height: 900 });
  await expect(back).toBeVisible();
  const backResult = await page.evaluate(async () => {
    const { initialQueryState } = await import("../src/package-query.ts");
    const {
      capturePackageQueryFocus,
      renderPackageQueryView,
      restorePackageQueryFocus,
    } = await import("../src/package-query-view.ts");
    const app = document.querySelector<HTMLElement>("#app");
    if (!app) throw new Error("The query focus harness root is unavailable.");
    const snapshot = capturePackageQueryFocus(document);
    app.innerHTML = renderPackageQueryView({
      state: initialQueryState(),
      availableFacets: [],
      escapeHtml: value => String(value),
    });
    return {
      restoration: restorePackageQueryFocus(document, snapshot),
      activeId: document.activeElement?.id,
    };
  });
  expect(backResult).toEqual({
    restoration: "restored",
    activeId: "package-query-back",
  });
  await expect(page.getByRole("button", { name: "Query", exact: true })).toHaveCount(0);
  await expect(page.getByRole("button", { name: "Workspace", exact: true })).toHaveCount(0);
  await expect(page.locator("#package-query-product")).toBeVisible();
});

test("Workspace retains its full split height at constrained widths", async ({
  page,
}) => {
  await page.setViewportSize({ width: 900, height: 700 });
  await page.goto("/browser/workspace-titlebar.html?workspace=1");

  const intermediateNavigation = await box(page, ".workspace-nav");
  expect(intermediateNavigation.width).toBeCloseTo(330, 0);

  await page.setViewportSize({ width: 600, height: 700 });

  await expect(page.locator(".detail-pane"))
    .not.toHaveClass(/content-navigation-/);
  const workspace = await box(page, ".workspace");
  const detail = await box(page, ".detail-pane");
  const inspector = await box(page, "#inspector-panel");
  expect(detail.height).toBeCloseTo(workspace.height, 0);
  expect(inspector.height).toBeCloseTo(detail.height, 0);
  expect(detail.height).toBeGreaterThan(500);
});

test("Workspace selection is observational and occurrence activation executes", async ({
  page,
}) => {
  await page.goto("/browser/workspace-titlebar.html?workspace=1");

  const workspace = page.locator("[data-workspace-default]");
  await expect(workspace).toHaveCount(1);
  const href = page.url();
  await workspace.click();

  await expect(workspace).toBeFocused();
  await expect(page.locator(".workspace-heading h1"))
    .toHaveText("Workspace");
  await expect(page.locator(".subject-path-segment"))
    .toHaveText("Workspace");
  await expect(page.locator("body"))
    .toHaveAttribute("data-workspace-execution-count", "0");
  expect(page.url()).toBe(href);

  await page.locator('[data-workspace-activate="occurrence-0"]').click();
  await expect(page.locator("body"))
    .toHaveAttribute("data-workspace-execution-count", "1");
  await expect(page.locator("body"))
    .toHaveAttribute(
      "data-workspace-execution",
      "occurrence-0");
});
