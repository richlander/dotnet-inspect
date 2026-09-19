import { expect, test, type Page } from "@playwright/test";
import {
  subjectTab,
  chooseInspector,
  library,
  createType as type,
  core,
  other,
  empty,
  installFacades,
  releaseFacade,
  frameworkSurface,
  frameworkRoot,
  type PackageLoadingFixture,
  type BrowserAssemblySurface,
  type BrowserPackageSurface,
} from "./library-hierarchy.support.ts";

test.use({ viewport: { width: 900, height: 900 } });

async function installPackageLoadingFacades(
  page: Page,
  options: PackageLoadingFixture = {},
  replacement?: BrowserPackageSurface,
) {
  await installFacades(
    page, frameworkSurface, replacement ? [replacement] : [], "ready", "ready", undefined,
    "ready", "ready", undefined, {},
    { deferChanges: true, versions: ["10.0.1", "10.0.0"], ...options });
}

const packageCoordinateChanges = [{
  name: "TFM",
  selector: "#framework",
  original: "net10.0",
  selected: "net9.0",
  version: "10.0.0",
  framework: "net9.0",
  loadingLabel: "Loading net9.0 content…",
  failure: { failFrameworkOnce: "net9.0" },
  error: "Framework inspection failed",
}, {
  name: "version",
  selector: "#package-version",
  original: "10.0.0",
  selected: "10.0.1",
  version: "10.0.1",
  framework: "net10.0",
  loadingLabel: "Loading version 10.0.1 content…",
  failure: { failVersionOnce: "10.0.1" },
  error: "Version inspection failed",
}];

const packageCoordinateViews = [{
  id: "overview",
  name: "Overview",
  surface: ".package-overview-surface",
}, {
  id: "dependencies",
  name: "Dependencies",
  surface: ".package-dependencies-surface",
}];

async function expectPackageCoordinateView(
  page: Page,
  view: typeof packageCoordinateViews[number],
  version: string,
  framework: string,
) {
  await expect(page.locator(view.surface)).toBeVisible();
  if (view.id === "dependencies") {
    await expect(page.locator("[data-package-dependencies-status]")).toHaveText("0 packages");
    await expect(page.locator("html")).toHaveAttribute(
      "data-package-dependencies-request",
      JSON.stringify(["System.Text.Json", version, framework]));
    await expect(page.locator(".package-dependencies-surface footer"))
      .toContainText(`System.Text.Json@${version}`);
    await expect(page.locator(".package-dependencies-surface footer")).toContainText(framework);
  }
}

async function openCoordinateLibrary(page: Page) {
  await page.goto(frameworkRoot);
  await page.locator(`.library-list [data-lib-scope="${other.id}"]`).click();
  await chooseInspector(page, "data-library-lens", "metadata", "Metadata");
  await expect(page.locator('[data-mde-open="0"]')).toBeVisible();
  await expect(page.locator("html")).toHaveAttribute("data-metadata-request", other.id);
}

async function expectCoordinateLibrary(
  page: Page,
  version: string,
  framework: string,
  selected: BrowserAssemblySurface,
) {
  await expect(subjectTab(page, "library")).toHaveAttribute("aria-selected", "true");
  await expect(page.locator("html")).toHaveAttribute(
    "data-metadata-coordinate",
    JSON.stringify(["System.Text.Json", version, framework, selected.id]));
  await expect(page.locator('[data-mde-open="0"]')).toBeVisible();
  await expect(page.locator("#inspector-panel")).toContainText(`${selected.name}.dll`);
}

for (const change of packageCoordinateChanges) {
  const nextLibrary = {
    ...library("replacement:other", other.name, 2),
    asset: `lib/${change.framework}/${other.name}.dll`,
  };
  const replacement: BrowserPackageSurface = {
    ...frameworkSurface,
    version: change.version,
    activeFramework: change.framework,
    assemblies: [empty, nextLibrary, core],
    types: [
      type("Example.Widget", core),
      type("Example.ReplacementNeighbor", nextLibrary),
      type("Example.AddedNeighbor", nextLibrary),
    ],
    totalMembers: 3,
  };

  for (const view of packageCoordinateViews) {
    for (const width of [1280, 390]) {
      test(`package ${change.name} loading retains ${view.name} at ${width}px`, async ({ page }) => {
        await page.setViewportSize({ width, height: 900 });
        await installPackageLoadingFacades(page);
        await page.goto(frameworkRoot);
        await chooseInspector(page, "data-package-lens", view.id, view.name);
        await expectPackageCoordinateView(page, view, "10.0.0", "net10.0");
        const selector = page.locator(change.selector);
        await expect(selector).toHaveValue(change.original);
        const target = await page.locator(".targetbar .subject-path").textContent();
        const titlebar = await page.locator(".titlebar").boundingBox();
        const url = page.url();
        await page.evaluate(() => {
          new MutationObserver(records => {
            if (records.some(record => [...record.addedNodes].some(node =>
              node instanceof Element
              && (node.matches(".loading-screen")
                || node.querySelector(".loading-screen"))))) {
              document.documentElement.dataset.interstitialShown = "true";
            }
          }).observe(document.querySelector("#app")!, { childList: true, subtree: true });
        });
        await selector.focus();
        await selector.selectOption(change.selected);

        await expect(page.locator("html")).toHaveAttribute(
          "data-package-query-pending",
          JSON.stringify(["System.Text.Json", change.version, change.framework]));
        await expect(page.locator("#package-content-loading")).toHaveText(change.loadingLabel);
        await expect(page.locator("#package-content-loading")).toBeFocused();
        await expect(page.locator("#inspector-panel")).toHaveAttribute("aria-busy", "true");
        await expect(page.locator(".loading-screen, .loading-bot")).toHaveCount(0);
        await expect(page.locator(".targetbar .subject-path")).toHaveText(target!);
        await expect(page.locator(".data-bar")).toBeVisible();
        expect(await page.locator(".titlebar").boundingBox()).toEqual(titlebar);
        expect(page.url()).toBe(url);
        expect(await page.evaluate(() =>
          document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);

        await releaseFacade(page, "finish-package-query");
        await expect(selector).toHaveValue(change.selected);
        await expectPackageCoordinateView(page, view, change.version, change.framework);
        await expect(selector).toBeFocused();
        await expect(page.locator("#inspector-panel")).not.toHaveAttribute("aria-busy", "true");
        await expect(page.locator("#package-content-loading, .loading-screen")).toHaveCount(0);

        await selector.selectOption(change.original);
        await expect(selector).toHaveValue(change.original);
        await expectPackageCoordinateView(page, view, "10.0.0", "net10.0");
        await expect(selector).toBeFocused();
        await expect(page.locator("#package-content-loading")).toHaveCount(0);
        await expect(page.locator("html")).not.toHaveAttribute("data-interstitial-shown");
      });
    }

    test(`package ${change.name} failure restores ${view.name} and retries inside the same page`, async ({ page }) => {
      await installPackageLoadingFacades(page, change.failure);
      await page.goto(frameworkRoot);
      await chooseInspector(page, "data-package-lens", view.id, view.name);
      await expectPackageCoordinateView(page, view, "10.0.0", "net10.0");
      await page.locator(change.selector).focus();
      await page.locator(change.selector).selectOption(change.selected);
      await expect(page.locator("html")).toHaveAttribute("data-package-query-pending");
      await page.locator("html").evaluate(element => {
        delete element.dataset.packageQueryPending;
      });
      await releaseFacade(page, "finish-package-query");
      await expect(page.locator(change.selector)).toHaveValue(change.original);
      await expectPackageCoordinateView(page, view, "10.0.0", "net10.0");
      await expect(page.locator(change.selector)).toBeFocused();
      await expect(page.locator(".query-notice")).toContainText(change.error);
      await expect(page.locator(".loading-screen")).toHaveCount(0);
      const retry = page.locator(".query-notice").getByRole("button", { name: "Retry" });
      await retry.focus();
      await retry.press("Enter");
      await expect(page.locator("#package-content-loading")).toHaveText(change.loadingLabel);
      await expect(page.locator("#package-content-loading")).toBeFocused();
      await expect(page.locator("html")).toHaveAttribute("data-package-query-pending");
      await releaseFacade(page, "finish-package-query");
      await expect(page.locator(change.selector)).toHaveValue(change.selected);
      await expectPackageCoordinateView(page, view, change.version, change.framework);
      await expect(page.locator(".query-notice")).toHaveCount(0);
      await expect(page.locator(change.selector)).toBeFocused();
    });
  }

  for (const width of [1280, 390]) {
    test(`Library metadata ${change.name} retains selection intent at ${width}px`, async ({ page }) => {
      await page.setViewportSize({ width, height: 900 });
      await installPackageLoadingFacades(page, {}, replacement);
      await openCoordinateLibrary(page);
      const target = await page.locator(".targetbar .subject-path").textContent();
      const url = page.url();
      const selector = page.locator(change.selector);
      await selector.focus();
      await selector.selectOption(change.selected);
      await expect(page.locator("html")).toHaveAttribute("data-package-query-pending");
      await expect(page.locator("#package-content-loading")).toHaveText(change.loadingLabel);
      await expect(page.locator("#package-content-loading")).toBeFocused();
      await expect(page.locator(".targetbar .subject-path")).toHaveText(target!);
      await expect(page.locator(".loading-screen")).toHaveCount(0);
      expect(page.url()).toBe(url);
      await releaseFacade(page, "finish-package-query");
      await expectCoordinateLibrary(page, change.version, change.framework, nextLibrary);
      await expect(selector).toHaveValue(change.selected);
      await expect(selector).toBeFocused();
      await expect(page.locator("#type-list [data-type]")).toHaveCount(2);
      await expect(page.locator("#type-list")).toContainText("ReplacementNeighbor");
      await expect(page.locator("#type-list")).toContainText("AddedNeighbor");
      await expect(page.locator("#type-list")).not.toContainText("Widget");
      await expect(page.locator(".query-notice")).toHaveCount(0);
      expect(await page.evaluate(() =>
        document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);

      await selector.selectOption(change.original);
      await expectCoordinateLibrary(page, "10.0.0", "net10.0", other);
      await expect(selector).toBeFocused();
      await expect(page.locator("#type-list")).toContainText("Neighbor");
      await expect(page.locator("#type-list")).not.toContainText("AddedNeighbor");
      await expect(page.locator("#package-content-loading, .loading-screen")).toHaveCount(0);
    });
  }

  test(`Library metadata ${change.name} failure retains selection intent through Retry`, async ({ page }) => {
    await installPackageLoadingFacades(page, change.failure, replacement);
    await openCoordinateLibrary(page);
    const selector = page.locator(change.selector);
    await selector.focus();
    await selector.selectOption(change.selected);
    await expect(page.locator("html")).toHaveAttribute("data-package-query-pending");
    await page.locator("html").evaluate(element => {
      delete element.dataset.packageQueryPending;
    });
    await releaseFacade(page, "finish-package-query");
    await expect(page.locator(".query-notice")).toContainText(change.error);
    await expectCoordinateLibrary(page, "10.0.0", "net10.0", other);
    await expect(selector).toHaveValue(change.original);
    await expect(selector).toBeFocused();
    const retry = page.locator(".query-notice").getByRole("button", { name: "Retry" });
    await retry.focus();
    await retry.press("Enter");
    await expect(page.locator("#package-content-loading")).toBeFocused();
    await expect(page.locator("html")).toHaveAttribute("data-package-query-pending");
    await releaseFacade(page, "finish-package-query");
    await expectCoordinateLibrary(page, change.version, change.framework, nextLibrary);
    await expect(selector).toBeFocused();
    await expect(page.locator(".query-notice")).toHaveCount(0);
  });

  test(`Library metadata ${change.name} retains a library whose public Type inventory becomes empty`, async ({ page }) => {
    const emptied = { ...nextLibrary, publicTypes: 0, publicMembers: 0 };
    await installPackageLoadingFacades(page, {}, {
      ...replacement,
      assemblies: [core, emptied],
      types: [type("Example.Widget", core)],
      totalMembers: 1,
    });
    await openCoordinateLibrary(page);
    await page.locator(change.selector).selectOption(change.selected);
    await expect(page.locator("html")).toHaveAttribute("data-package-query-pending");
    await releaseFacade(page, "finish-package-query");
    await expectCoordinateLibrary(page, change.version, change.framework, emptied);
    await expect(page.locator("#type-list [data-type]")).toHaveCount(0);
    await expect(page.locator('[data-subject-tab][data-scope="type"]')).toHaveCount(0);
    await expect(page.locator(".query-notice")).toHaveCount(0);
  });

  for (const scenario of ["missing", "ambiguous", "root-only"] as const) {
    test(`Library metadata ${change.name} explains the ${scenario} selection fallback`, async ({ page }) => {
      const rootOnly = scenario === "root-only";
      await installPackageLoadingFacades(page, {}, {
        ...replacement,
        defaultAssemblyId: rootOnly ? null : core.id,
        compileLibrary: rootOnly
          ? { status: "NoCompileAssets", targetFramework: change.framework, message: "No compile Libraries." }
          : replacement.compileLibrary,
        assemblies: rootOnly ? [] : [
          core,
          ...(scenario === "ambiguous"
            ? [nextLibrary, { ...nextLibrary, id: "replacement:duplicate" }]
            : []),
        ],
        types: rootOnly ? [] : [type("Example.Widget", core)],
        totalMembers: rootOnly ? 0 : 1,
      });
      await openCoordinateLibrary(page);
      const selector = page.locator(change.selector);
      await selector.focus();
      await selector.selectOption(change.selected);
      await expect(page.locator("html")).toHaveAttribute("data-package-query-pending");
      await releaseFacade(page, "finish-package-query");
      await expect(page.locator(".package-overview-surface")).toBeVisible();
      await expect(page.locator(".query-notice").filter({ hasText: "Showing Package Overview." })).toContainText(
        `The library '${other.name}' is not uniquely available in System.Text.Json@${change.version} (${change.framework}). Showing Package Overview.`);
      if (rootOnly) {
        await expect(page.locator(".query-notice").filter({ hasText: "No compile Libraries." })).toBeVisible();
      }
      await expect(subjectTab(page, "package")).toHaveAttribute("aria-selected", "true");
      await expect(selector).toHaveValue(change.selected);
      await expect(selector).toBeFocused();
      await expect(page.locator(".loading-screen")).toHaveCount(0);
    });
  }

  test(`leaving a pending package ${change.name} ignores its late completion`, async ({ page }) => {
    await installPackageLoadingFacades(page);
    await page.goto(frameworkRoot);
    await page.locator(change.selector).selectOption(change.selected);
    await expect(page.locator("html")).toHaveAttribute("data-package-query-pending");
    await page.locator(".brand").click();
    await expect(page.locator(".home-search")).toBeVisible();
    await releaseFacade(page, "finish-package-query");
    await expect(page.locator("html")).toHaveAttribute("data-package-query-settled");
    await expect(page.locator(".home-search")).toBeVisible();
    await expect(page.locator("#package-content-loading, .loading-screen")).toHaveCount(0);
  });
}

test("initial package loading retains the full acquisition interstitial", async ({ page }) => {
  await installPackageLoadingFacades(page, { deferInitial: true });
  await page.goto(frameworkRoot);
  await expect(page.locator("html")).toHaveAttribute("data-package-query-pending");
  await expect(page.locator(".loading-screen .loading-bot")).toBeVisible();
  await expect(page.locator("#package-content-loading, .titlebar")).toHaveCount(0);
  await releaseFacade(page, "finish-package-query");
  await expect(page.locator("#framework")).toHaveValue("net10.0");
  await expect(page.locator(".package-overview-surface")).toBeVisible();
});
