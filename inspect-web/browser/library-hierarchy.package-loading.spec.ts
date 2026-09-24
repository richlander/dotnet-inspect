import { expect, test, type Page } from "@playwright/test";
import {
  subjectTab,
  chooseInspector,
  chooseSubject,
  selectLibrary,
  other,
  installFacades,
  releaseFacade,
  frameworkSurface,
  frameworkRoot,
  type PackageLoadingFixture,
  type BrowserPackageSurface,
} from "./library-hierarchy.support.ts";

test.use({ viewport: { width: 900, height: 900 } });

async function installPackageLoadingFacades(
  page: Page,
  options: PackageLoadingFixture = {},
  replacement?: BrowserPackageSurface,
  model: BrowserPackageSurface = frameworkSurface,
) {
  await installFacades(
    page, model, replacement ? [replacement] : [], "ready", "ready", undefined,
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

function packageCoordinateControl(
  page: Page,
  change: typeof packageCoordinateChanges[number],
  value: string,
) {
  return change.name === "TFM"
    ? page.locator(`[data-package-framework="${value}"]`)
    : page.locator(change.selector);
}

async function expectPackageCoordinateSelection(
  page: Page,
  change: typeof packageCoordinateChanges[number],
  value: string,
) {
  const control = packageCoordinateControl(page, change, value);
  if (change.name === "TFM")
    await expect(control).toHaveAttribute("aria-current", "page");
  else
    await expect(control).toHaveValue(value);
}

async function selectPackageCoordinate(
  page: Page,
  change: typeof packageCoordinateChanges[number],
  value: string,
) {
  const control = packageCoordinateControl(page, change, value);
  if (change.name === "TFM") {
    const navigationToggle = page.getByRole(
      "button",
      { name: "Frameworks", exact: true });
    await expect.poll(async () =>
      await control.isVisible() || await navigationToggle.isVisible()).toBe(true);
    if (!await control.isVisible()) await navigationToggle.click();
  }
  await control.focus();
  if (change.name === "TFM")
    await control.click();
  else
    await control.selectOption(value);
  return control;
}

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
  await expect(page.locator(view.surface).locator("#package-version"))
    .toBeVisible();
  await expect(page.locator(view.surface).locator("#framework"))
    .toHaveCount(0);
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
  await selectLibrary(page, other.id);
  await chooseInspector(page, "data-library-lens", "metadata", "Metadata");
  await expect(page.locator('[data-mde-open="0"]')).toBeVisible();
  await expect(page.locator("html")).toHaveAttribute("data-metadata-request", other.id);
}

test("Library Metadata omits Package coordinate selectors", async ({ page }) => {
  await installPackageLoadingFacades(page);
  await openCoordinateLibrary(page);
  await expect(page.locator(
    "#inspector-panel #package-version, #inspector-panel #framework"))
    .toHaveCount(0);
  await expect(page.locator("html")).toHaveAttribute(
    "data-metadata-coordinate",
    JSON.stringify(["System.Text.Json", "10.0.0", "net10.0", other.id]));
});

for (const width of [1280, 390]) {
  test(`subject-path TFM opens Package frameworks at ${width}px`, async ({
    page,
  }) => {
    await page.setViewportSize({ width, height: 900 });
    await installPackageLoadingFacades(page);
    await page.goto(frameworkRoot);
    await chooseSubject(page, "type", "Type");
    if (width === 390) {
      await chooseInspector(page, "data-lens", "source", "Source");
      await expect(
        page.getByRole("group", { name: "Source actions" }),
      ).toBeVisible();
      await expect(page.getByLabel("Select type code view")).toBeVisible();
      await expect(page.locator("#copy-type-source")).toBeVisible();
      await expect(page.getByRole("link", { name: "Open" })).toBeVisible();
      await expect(page.locator("#explore-source")).toBeVisible();
    }

    const targetFramework = page.getByRole(
      "button",
      {
        name: "Target framework net10.0. Change target framework for System.Text.Json",
      });
    await expect(targetFramework).toHaveText("· net10.0");
    await expect(targetFramework).toBeVisible();
    const frameworkBounds = await targetFramework.boundingBox();
    const pathBounds = await page.locator(".subject-path").boundingBox();
    expect(frameworkBounds).not.toBeNull();
    expect(pathBounds).not.toBeNull();
    expect(frameworkBounds!.x).toBeGreaterThanOrEqual(pathBounds!.x);
    expect(frameworkBounds!.x + frameworkBounds!.width)
      .toBeLessThanOrEqual(pathBounds!.x + pathBounds!.width + 1);
    await targetFramework.click();

    await expect(subjectTab(page, "package")).toHaveAttribute(
      "aria-selected",
      "true");
    await expect(page.locator('[data-package-framework="net10.0"]'))
      .toBeFocused();
  });
}

for (const change of packageCoordinateChanges) {
  test(`package ${change.name} replacement preserves latent exact Library selection`, async ({ page }) => {
    await installPackageLoadingFacades(page);
    await page.goto(frameworkRoot);
    await selectLibrary(page, other.id);
    await chooseSubject(page, "package", "Package");

    await selectPackageCoordinate(page, change, change.selected);
    await expect(page.locator("html")).toHaveAttribute(
      "data-package-query-pending",
      JSON.stringify(["System.Text.Json", change.version, change.framework]));
    await releaseFacade(page, "finish-package-query");

    await expect(subjectTab(page, "package")).toHaveAttribute("aria-selected", "true");
    await expectPackageCoordinateSelection(page, change, change.selected);
    await chooseSubject(page, "library", "Library");
    await expect(page.locator(".library-overview-surface h1")).toHaveText(other.name);
    await expect(page.locator(
      `.library-subject-list [data-library-subject="${other.id}"]`))
      .toHaveAttribute("aria-selected", "true");
  });
}

function sameNamedLibrarySurface(
  source: BrowserPackageSurface,
  version: string,
  framework: string,
): BrowserPackageSurface {
  const assemblyBySourceId = new Map(source.assemblies.map((assembly, index) => {
    const asset = `lib/${framework}/${index}/Example.Shared.dll`;
    return [assembly.id, {
      ...assembly,
      id: `compile:${asset}`,
      name: "Example.Shared",
      asset,
    }];
  }));
  const assemblies = [...assemblyBySourceId.values()];
  return {
    ...source,
    version,
    activeFramework: framework,
    defaultAssemblyId: source.defaultAssemblyId
      ? assemblyBySourceId.get(source.defaultAssemblyId)?.id
        ?? source.defaultAssemblyId
      : null,
    assemblies,
    types: source.types.map(item => {
      const assembly = assemblyBySourceId.get(item.assemblyId)!;
      return {
        ...item,
        id: `${assembly.id}:${item.definitionId}`,
        assemblyId: assembly.id,
        assembly: `${assembly.name}.dll`,
        assemblyName: assembly.name,
      };
    }),
  };
}

for (const change of packageCoordinateChanges) {
  test(`package ${change.name} replacement preserves same-named exact Library identity`, async ({ page }) => {
    const initial = sameNamedLibrarySurface(
      frameworkSurface,
      "10.0.0",
      "net10.0");
    const replacement = sameNamedLibrarySurface(
      frameworkSurface,
      change.version,
      change.framework);
    await installPackageLoadingFacades(page, {}, replacement, initial);
    await page.goto(frameworkRoot);
    const initialLibrary = initial.assemblies[1]!;
    const replacementLibrary = replacement.assemblies[1]!;
    await selectLibrary(page, initialLibrary.id);
    await chooseSubject(page, "package", "Package");

    await selectPackageCoordinate(page, change, change.selected);
    await expect(page.locator("html")).toHaveAttribute(
      "data-package-query-pending",
      JSON.stringify(["System.Text.Json", change.version, change.framework]));
    await releaseFacade(page, "finish-package-query");

    await expect(subjectTab(page, "package")).toHaveAttribute(
      "aria-selected", "true");
    await expectPackageCoordinateSelection(page, change, change.selected);
    await expect(page.locator(
      ".query-notice-text",
      { hasText: "not uniquely available" }))
      .toHaveCount(0);
    await chooseSubject(page, "library", "Library");
    await expect(page.locator(
      `.library-subject-list [data-library-subject="${replacementLibrary.id}"]`))
      .toHaveAttribute("aria-selected", "true");
  });
}

for (const width of [1280, 390]) {
  test(`Package Framework keyboard navigation selects a TFM at ${width}px`, async ({ page }) => {
    await page.setViewportSize({ width, height: 900 });
    await installPackageLoadingFacades(page);
    await page.goto(frameworkRoot);
    const current = page.locator('[data-package-framework="net10.0"]');
    const next = page.locator('[data-package-framework="net9.0"]');
    if (width === 390)
      await page.getByRole("button", { name: "Frameworks", exact: true }).click();
    else
      await current.focus();
    await expect(current).toBeFocused();

    await page.keyboard.press("ArrowDown");
    await expect(next).toBeFocused();
    await expect(subjectTab(page, "package")).toHaveAttribute("aria-selected", "true");
    await expect(page.locator("html")).not.toHaveAttribute("data-package-query-pending");

    await page.keyboard.press("ArrowUp");
    await expect(current).toBeFocused();
    await page.keyboard.press("ArrowDown");
    await page.keyboard.press("Enter");
    await expect(subjectTab(page, "package")).toHaveAttribute("aria-selected", "true");
    await expect(page.locator("html")).toHaveAttribute(
      "data-package-query-pending",
      JSON.stringify(["System.Text.Json", "10.0.0", "net9.0"]));
    await expect(page.locator("#package-content-loading"))
      .toHaveText("Loading net9.0 content…");

    await releaseFacade(page, "finish-package-query");
    await expect(next).toHaveAttribute("aria-current", "page");
  });
}

for (const change of packageCoordinateChanges) {
  for (const view of packageCoordinateViews) {
    for (const width of [1280, 390]) {
      test(`package ${change.name} loading retains ${view.name} at ${width}px`, async ({ page }) => {
        await page.setViewportSize({ width, height: 900 });
        await installPackageLoadingFacades(page);
        await page.goto(frameworkRoot);
        await chooseInspector(page, "data-package-lens", view.id, view.name);
        await expectPackageCoordinateView(page, view, "10.0.0", "net10.0");
        await expectPackageCoordinateSelection(
          page, change, change.original);
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
        await selectPackageCoordinate(page, change, change.selected);

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
        await expectPackageCoordinateSelection(
          page, change, change.selected);
        await expectPackageCoordinateView(page, view, change.version, change.framework);
        await expect(change.name === "TFM" && width === 390
          ? page.getByRole("button", { name: "Frameworks", exact: true })
          : packageCoordinateControl(
              page, change, change.selected)).toBeFocused();
        await expect(page.locator("#inspector-panel")).not.toHaveAttribute("aria-busy", "true");
        await expect(page.locator("#package-content-loading, .loading-screen")).toHaveCount(0);

        await selectPackageCoordinate(page, change, change.original);
        await expectPackageCoordinateSelection(
          page, change, change.original);
        await expectPackageCoordinateView(page, view, "10.0.0", "net10.0");
        await expect(change.name === "TFM" && width === 390
          ? page.getByRole("button", { name: "Frameworks", exact: true })
          : packageCoordinateControl(
              page, change, change.original)).toBeFocused();
        await expect(page.locator("#package-content-loading")).toHaveCount(0);
        await expect(page.locator("html")).not.toHaveAttribute("data-interstitial-shown");
      });
    }

    for (const width of [1280, 390]) {
      test(`package ${change.name} failure restores ${view.name} at ${width}px and retries inside the same page`, async ({ page }) => {
        await page.setViewportSize({ width, height: 900 });
        await installPackageLoadingFacades(page, change.failure);
        await page.goto(frameworkRoot);
        await chooseInspector(page, "data-package-lens", view.id, view.name);
        await expectPackageCoordinateView(page, view, "10.0.0", "net10.0");
        await selectPackageCoordinate(page, change, change.selected);
        await expect(page.locator("html")).toHaveAttribute("data-package-query-pending");
        await page.locator("html").evaluate(element => {
          delete element.dataset.packageQueryPending;
        });
        await releaseFacade(page, "finish-package-query");
        await expectPackageCoordinateSelection(
          page, change, change.original);
        await expectPackageCoordinateView(page, view, "10.0.0", "net10.0");
        await expect(change.name === "TFM" && width === 390
          ? page.getByRole("button", { name: "Frameworks", exact: true })
          : packageCoordinateControl(
              page, change, change.selected)).toBeFocused();
        await expect(page.locator(".query-notice")).toContainText(change.error);
        await expect(page.locator(".loading-screen")).toHaveCount(0);
        const retry = page.locator(".query-notice").getByRole("button", { name: "Retry" });
        await retry.focus();
        await retry.press("Enter");
        await expect(page.locator("#package-content-loading")).toHaveText(change.loadingLabel);
        await expect(page.locator("#package-content-loading")).toBeFocused();
        await expect(page.locator("html")).toHaveAttribute("data-package-query-pending");
        await releaseFacade(page, "finish-package-query");
        await expectPackageCoordinateSelection(
          page, change, change.selected);
        await expectPackageCoordinateView(page, view, change.version, change.framework);
        await expect(page.locator(".query-notice")).toHaveCount(0);
        await expect(change.name === "TFM" && width === 390
          ? page.getByRole("button", { name: "Frameworks", exact: true })
          : packageCoordinateControl(
              page, change, change.selected)).toBeFocused();
      });
    }
  }

  test(`leaving a pending package ${change.name} ignores its late completion`, async ({ page }) => {
    await installPackageLoadingFacades(page);
    await page.goto(frameworkRoot);
    await selectPackageCoordinate(page, change, change.selected);
    await expect(page.locator("html")).toHaveAttribute("data-package-query-pending");
    await page.locator(".brand").click();
    await page.locator('[data-product-destination="home"]').click();
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
  await expect(page.locator('[data-package-framework="net10.0"]'))
    .toHaveAttribute("aria-current", "page");
  await expect(page.locator(".package-overview-surface")).toBeVisible();
});
