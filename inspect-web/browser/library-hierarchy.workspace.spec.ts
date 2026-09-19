import { expect, test } from "@playwright/test";
import {
  subjectTab,
  inspectorTab,
  chooseInspector,
  chooseSubject,
  selectLibrary,
  expectCurrentSubjectVisible,
  core,
  other,
  empty,
  surface,
  installFacades,
  root,
} from "./library-hierarchy.support.ts";

test.use({ viewport: { width: 900, height: 900 } });

for (const preferred of [other, empty]) {
  for (const width of [900, 480]) {
    test(`implicit package entry selects product-default ${preferred.name} at ${width}px`, async ({ page }) => {
      await page.setViewportSize({ width, height: 900 });
      await installFacades(page, { ...surface, defaultAssemblyId: preferred.id });
      await page.goto(root.replace("#pkg", ""));
      await expect(subjectTab(page, "library")).toHaveAttribute("aria-selected", "true");
      await expect(page.locator(".library-overview-surface h1")).toHaveText("All libraries");
      await page.reload();
      await expect(page.locator(".library-overview-surface h1")).toHaveText("All libraries");
      if (width === 480) {
        await page.getByRole("button", { name: "Libraries", exact: true }).click();
      }
      await chooseSubject(page, "package", "Package");
      await expect(page.locator(".package-overview-surface h1")).toHaveText(surface.package);
      await page.reload();
      await expect(page.locator(".package-overview-surface h1")).toHaveText(surface.package);
    });
  }
}

for (const status of ["NoCompileAssets", "EmptyCompileGroup"] as const) {
  test(`implicit ${status} package entry retains the Package subject`, async ({ page }) => {
    await installFacades(page, {
      ...surface,
      defaultAssemblyId: null,
      compileLibrary: { status, targetFramework: "net10.0", message: null },
      assemblies: [],
      types: [],
      accessibility: [],
      totalMembers: 0,
    });
    await page.goto(root.replace("#pkg", ""));
    await expect(subjectTab(page, "package")).toHaveAttribute("aria-selected", "true");
    await expect(page.locator(".package-overview-surface h1")).toHaveText(surface.package);
    await expect(page.locator(".package-overview-surface [data-lib-scope]"))
      .toHaveCount(0);
    await expect(page.locator(".query-notice-text")).toContainText(status);
    await page.reload();
    await expect(page.locator(".package-overview-surface h1")).toHaveText(surface.package);
  });
}

for (const incomingPackage of [surface.package, "Second.Package"]) {
  for (const destination of ["default", "Package", "Metadata"]) {
    test(`legacy history restores ${destination} in ${incomingPackage}`, async ({ page }) => {
      await installFacades(page, { ...surface, defaultAssemblyId: other.id }, [
        { ...surface, package: "Second.Package", defaultAssemblyId: empty.id },
      ]);
      await page.goto(root);
      await selectLibrary(page, core.id);
      await expect(page.locator(".library-overview-surface h1")).toHaveText(core.name);

      const target = `/?package=${incomingPackage}&version=1.0.0&framework=net10.0`
        + (destination === "Package" ? "#pkg"
          : destination === "Metadata" ? "#library:metadata"
            : "");
      await page.evaluate(url => history.pushState(null, "", url), target);
      await page.goBack();
      await expect(page.locator(".library-overview-surface h1")).toHaveText(core.name);
      await page.goForward();
      await expect(page.locator(".inspected-target")).toContainText(incomingPackage);
      if (destination === "Package") {
        await expect(page.locator(".package-overview-surface h1")).toHaveText(incomingPackage);
      } else if (destination === "Metadata") {
        await expect(inspectorTab(page, "data-library-lens", "metadata"))
          .toHaveAttribute("aria-selected", "true");
        await expect(page.locator("#inspector-panel"))
          .toContainText("Metadata requires one Library");
      } else {
        await expect(page.locator(".library-overview-surface h1"))
          .toHaveText("All libraries");
      }
      await page.goBack();
      await expect(page.locator(".library-overview-surface h1")).toHaveText(core.name);
    });
  }
}
test("Package comparison targets survive Library, Type, and Member navigation", async ({ page }) => {
  await installFacades(page);
  await page.goto(root);
  await expect(page.locator("#package-diff-target-status"))
    .toHaveText("Previous listed release");
  await expect(page.locator("#package-diff-target option:checked"))
    .toHaveText("Automatic: 0.9.0");
  await page.locator("#package-diff-target").focus();
  await page.locator("#package-diff-target").selectOption("exact:1.0.0");
  await expect(page.locator("#package-diff-target")).toBeFocused();
  await page.locator("#package-clone-target").selectOption("package:0");
  await selectLibrary(page, core.id);
  await expect(page.locator("#package-comparison-targets")).toHaveCount(0);
  await chooseSubject(page, "type", "Type");
  await page.locator("#type-list [data-type]").click();
  await chooseSubject(page, "member", "Member");
  await expect(subjectTab(page, "member")).toHaveAttribute("aria-selected", "true");
  await chooseSubject(page, "package", "Package");
  await expect(page.locator("#package-diff-target")).toHaveValue("exact:1.0.0");
  await expect(page.locator("#package-clone-target")).toHaveValue("package:0");
  await page.locator("#package-diff-target").selectOption("previous");
  await expect(page.locator("#package-diff-target-status"))
    .toHaveText("Previous listed release");
});

test("Package comparison targets consume an omitted predecessor", async ({ page }) => {
  await installFacades(
    page, surface, [], "ready", "ready", undefined,
    "ready", "ready", undefined, {}, { versions: [] },
  );
  await page.goto(root);
  await expect(page.locator("#package-diff-target-status"))
    .toHaveText("No earlier listed version is available.");
  await expect(page.locator("#package-diff-target option:checked"))
    .toHaveText("Automatic: no earlier version");
});

for (const initialWidth of [1440, 390]) {
  test(`active subject continuity keeps Library visible from ${initialWidth}px entry`, async ({ page }, testInfo) => {
    await page.setViewportSize({ width: initialWidth, height: 844 });
    await installFacades(page);
    await page.goto(root);
    await expectCurrentSubjectVisible(page, "package", "Package");
    await selectLibrary(page, core.id);
    const libraryTab = subjectTab(page, "library");
    await expect(libraryTab).toHaveAttribute("aria-selected", "true");
    await expect(page.locator("#inspector-panel h1")).toHaveText(core.name);
    const location = page.url();
    const historyLength = await page.evaluate(() => history.length);
    const menu = page.getByRole("button", { name: "Application menu", exact: true });
    await menu.focus();

    await page.setViewportSize({ width: 390, height: 844 });
    await expect(libraryTab).toHaveAttribute("aria-selected", "true");
    await expectCurrentSubjectVisible(page, "library", "Library");
    await expect(menu).toBeFocused();
    await expect(page).toHaveURL(location);
    expect(await page.evaluate(() => history.length)).toBe(historyLength);
    await testInfo.attach("active-library-390", {
      body: await page.screenshot(),
      contentType: "image/png",
    });

    await page.reload();
    await expect(libraryTab).toHaveAttribute("aria-selected", "true");
    await expectCurrentSubjectVisible(page, "library", "Library");
    await expect(page.locator("#inspector-panel h1")).toHaveText(core.name);
    await page.setViewportSize({ width: 1440, height: 844 });
    for (const subject of ["package", "library", "type"]) {
      await expect(subjectTab(page, subject)).toBeVisible();
    }
    await page.setViewportSize({ width: 390, height: 844 });
    await expect(libraryTab).toHaveAttribute("aria-selected", "true");
    await expectCurrentSubjectVisible(page, "library", "Library");
  });
}

test("active subject continuity retains explicit browsing until the subject changes", async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 });
  await installFacades(page);
  await page.goto(root);
  await selectLibrary(page, core.id);
  const libraryTab = subjectTab(page, "library");
  const packageTab = subjectTab(page, "package");
  const trigger = page.locator("[data-navigation-trigger='subject']");
  await expectCurrentSubjectVisible(page, "library", "Library");
  const menu = page.getByRole("button", { name: "Application menu", exact: true });
  await menu.focus();
  const location = page.url();
  const historyLength = await page.evaluate(() => history.length);
  await trigger.click();
  const packageItem = page.locator("#subject-navigation-menu")
    .locator('[data-scope="package"]');
  await page.keyboard.press("Home");
  await expect(packageItem).toBeFocused();
  await expect(packageItem).toHaveAttribute("aria-checked", "false");
  await expect(libraryTab).toHaveAttribute("aria-selected", "true");
  await expect(packageTab).toHaveAttribute("aria-selected", "false");
  await expect(page).toHaveURL(location);
  expect(await page.evaluate(() => history.length)).toBe(historyLength);

  await page.keyboard.press("Escape");
  await expect(trigger).toBeFocused();
  await chooseInspector(page, "data-library-lens", "references", "References");
  await expect(page.locator("#inspector-panel")).toContainText("Example.Core.Dependency");
  await expect(libraryTab).toHaveAttribute("aria-selected", "true");
  await expectCurrentSubjectVisible(page, "library", "Library");

  await chooseSubject(page, "package", "Package");
  await selectLibrary(page, other.id);
  await expect(libraryTab).toHaveAttribute("aria-selected", "true");
  await expectCurrentSubjectVisible(page, "library", "Library");
  await expect(page.locator("#inspector-panel h1")).toHaveText(other.name);
});

test("active subject continuity preserves focus without making a manual window", async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 844 });
  await installFacades(page);
  await page.goto(root);
  await selectLibrary(page, core.id);
  const libraryTab = subjectTab(page, "library");
  const packageTab = subjectTab(page, "package");
  const typeTab = subjectTab(page, "type");
  await typeTab.focus();
  await page.setViewportSize({ width: 390, height: 844 });
  await expect(page.locator("[data-navigation-trigger='subject']")).toBeFocused();
  await expect(typeTab).toHaveAttribute("aria-selected", "false");
  await expect(libraryTab).toHaveAttribute("aria-selected", "true");

  await page.getByRole("button", { name: "Application menu", exact: true }).focus();
  await page.setViewportSize({ width: 1440, height: 844 });
  await expect(libraryTab).toBeVisible();
  await libraryTab.focus();
  await libraryTab.press("ArrowLeft");
  await expect(packageTab).toBeFocused();
  await expect(libraryTab).toHaveAttribute("aria-selected", "true");
  await expect(packageTab).toHaveAttribute("aria-selected", "false");
  await page.keyboard.press("Enter");
  await expect(packageTab).toHaveAttribute("aria-selected", "true");
});
