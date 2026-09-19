import { expect, test } from "@playwright/test";
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
  installFacades,
  root,
  currentWorkspaceHistoryState,
} from "./library-hierarchy.support.ts";

test.use({ viewport: { width: 900, height: 900 } });

for (const [width, selectedLibrary, activation] of [
  [900, core, "click"],
  [480, core, "keyboard"],
  [900, empty, "keyboard"],
  [480, empty, "click"],
] as const) {
  test(`Library back returns ${selectedLibrary.name} to Package at ${width}px with ${activation}`, async ({ page }) => {
    await page.setViewportSize({ width, height: 900 });
    await installFacades(page);
    await page.goto(root);
    await selectLibrary(page, selectedLibrary.id);
    await expect(subjectTab(page, "library")).toHaveAttribute("aria-selected", "true");
    const libraryLocation = page.url();
    if (activation === "click") {
      await chooseSubject(page, "package", "Package");
    } else {
      const packageTab = subjectTab(page, "package");
      if (await packageTab.isVisible()) {
        await packageTab.focus();
        await page.keyboard.press("Enter");
      } else {
        const trigger = page.locator("[data-navigation-trigger='subject']");
        await trigger.click();
        const item = page.locator("#subject-navigation-menu")
          .locator('[data-scope="package"]');
        await item.focus();
        await page.keyboard.press("Enter");
      }
    }
    await expect(subjectTab(page, "package")).toHaveAttribute("aria-selected", "true");
    await expect(page.locator("#inspector-panel")).toBeVisible();
    await expect(page.locator(".package-overview-surface")).toBeVisible();
    await expect(page.locator(".package-overview-surface [data-lib-scope]"))
      .toHaveCount(0);
    const packageLocation = page.url();
    expect(packageLocation).not.toBe(libraryLocation);

    await page.getByRole("button", { name: "Application menu", exact: true }).press("Alt+ArrowLeft");
    await expect(page).toHaveURL(libraryLocation);
    await expect(subjectTab(page, "library")).toHaveAttribute("aria-selected", "true");
    await expect(page.locator("#inspector-panel h1")).toHaveText(selectedLibrary.name);
    await expect(page.locator(
      `.library-subject-list [data-library-subject="${selectedLibrary.id}"]`))
      .toHaveAttribute("aria-selected", "true");

    await page.getByRole("button", { name: "Application menu", exact: true }).press("Alt+ArrowRight");
    await expect(page).toHaveURL(packageLocation);
    await expect(subjectTab(page, "package")).toHaveAttribute("aria-selected", "true");
    await page.reload();
    await expect(subjectTab(page, "package")).toHaveAttribute("aria-selected", "true");
    await expect(page.locator(".package-overview-surface [data-lib-scope]"))
      .toHaveCount(0);
  });
}

for (const [width, activation] of [[900, "click"], [480, "keyboard"]] as const) {
  test(`Type back reveals its Library inspector at ${width}px with ${activation}`, async ({ page }) => {
    await page.setViewportSize({ width, height: 900 });
    await installFacades(page);
    await page.goto(root);
    await selectLibrary(page, other.id);
    await chooseInspector(page, "data-library-lens", "references", "References");
    await expect(page.locator("#inspector-panel")).toContainText("Example.Other.Dependency");
    const libraryLocation = page.url();
    await chooseSubject(page, "type", "Type");
    if (width === 480) {
      await page.getByRole("button", { name: "Types", exact: true }).click();
    }
    await page.locator('#type-list [data-type]').click();
    await expect(subjectTab(page, "type")).toHaveAttribute("aria-selected", "true");
    const typeLocation = page.url();
    if (width === 480) {
      await page.getByRole("button", { name: "Types", exact: true }).click();
    }
    const back = page.locator(".type-browser .nav-back-row");
    await expect(back).toHaveAttribute("title", "Back to library");
    await expect(back).toHaveAccessibleName("Example.Other: Back to library");
    if (activation === "click") {
      await back.click();
    } else {
      await back.focus();
      await page.keyboard.press("Enter");
    }
    await expect(subjectTab(page, "library")).toHaveAttribute("aria-selected", "true");
    await expect(page.locator("#inspector-panel")).toBeVisible();
    await expect(page.locator("#inspector-panel")).toContainText("Example.Other.Dependency");
    await expect(page.locator(width === 480
      ? "#content-navigation-toggle" : ".library-subject-list")).toBeFocused();
    await expect(page.locator(
      `.library-subject-list [data-library-subject="${other.id}"]`))
      .toHaveAttribute("aria-selected", "true");
    await expect(page).toHaveURL(libraryLocation);

    await page.getByRole("button", { name: "Application menu", exact: true }).press("Alt+ArrowLeft");
    await expect(page).toHaveURL(typeLocation);
    await expect(subjectTab(page, "type")).toHaveAttribute("aria-selected", "true");
    await page.getByRole("button", { name: "Application menu", exact: true }).press("Alt+ArrowRight");
    await expect(page).toHaveURL(libraryLocation);
    await expect(inspectorTab(page, "data-library-lens", "references"))
      .toHaveAttribute("aria-selected", "true");
  });
}

test("Workspace occurrence activation retains Package Info", async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  await installFacades(page);
  await page.goto(root);
  const overview = page.locator(".package-overview-surface");
  await expect(overview.locator(".package-info-rows")).toBeVisible();

  await page.locator('[data-application-scope="workspace"]').click();
  const occurrence = page.locator("[data-workspace-activate]");
  await expect(occurrence).toBeEnabled();
  await occurrence.click();
  await expect(subjectTab(page, "library")).toHaveAttribute("aria-selected", "true");
  await chooseSubject(page, "package", "Package");

  await expect(subjectTab(page, "package")).toHaveAttribute("aria-selected", "true");
  await expect(overview.locator(
    ".package-overview-summary .section-title h2"))
    .toHaveText("Package Info");
});

for (const width of [1440, 800, 390]) {
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
    await expect(overview.locator("#package-version")).toBeVisible();
    await expect(overview.locator("#framework")).toHaveCount(0);
    const packageIconSource = await overview.locator("[data-package-icon]").getAttribute("src");
    await expect(page.locator(".overview-surface-head p")).toHaveText("2 types · 2 members");
    await expect(page.locator(".overview-surface-footer span")).toHaveText([
      "Example.Package@1.0.0", "net10.0",
    ]);
    await expect(overview.locator(".library-row, [data-lib-scope]")).toHaveCount(0);
    await expect(overview.locator(".comparison-target-row")).toHaveCount(2);
    const diffTarget = overview.locator("#package-diff-target");
    await expect(diffTarget.locator("option:checked")).toHaveText("Automatic: 0.9.0");
    await expect(overview.locator(".comparison-target-policy"))
      .toHaveText("Session only. Choosing a target does not run a comparison or change shared links.");
    await expect(overview.locator(
      ".package-overview-summary .section-title h2"))
      .toHaveText("Package Info");
    await expect(overview.locator(
      ".package-overview-resources .section-title h2"))
      .toHaveText("Comparison targets");
    await expect(overview.locator(".package-info-rows dt")).toHaveText([
      "Package Size (compressed)",
      "Selected TFM",
      "Selected-TFM Folders",
      "Selected-TFM Library Count",
      "Selected-TFM Size",
      "TFMs",
    ]);
    if (width === 1440) {
      const inventory = await overview.locator(".package-overview-summary").boundingBox();
      const resources = await overview.locator(".package-overview-resources").boundingBox();
      expect(inventory).not.toBeNull();
      expect(resources).not.toBeNull();
      expect(resources!.x).toBeGreaterThanOrEqual(inventory!.x + inventory!.width);
      expect(resources!.y).toBeCloseTo(inventory!.y, 0);
      expect((await diffTarget.boundingBox())!.width).toBeGreaterThan(250);
      expect(await diffTarget.evaluate(select => {
        if (!(select instanceof HTMLSelectElement)) return false;
        const canvas = document.createElement("canvas");
        const context = canvas.getContext("2d");
        if (!context) return false;
        context.font = getComputedStyle(select).font;
        return context.measureText(select.selectedOptions[0]?.text ?? "").width + 48
          <= select.clientWidth;
      })).toBe(true);
    } else {
      const inventory = await overview.locator(".package-overview-summary").boundingBox();
      const resources = await overview.locator(".package-overview-resources").boundingBox();
      expect(inventory).not.toBeNull();
      expect(resources).not.toBeNull();
      expect(resources!.x).toBeCloseTo(inventory!.x, 0);
      expect(resources!.y).toBeGreaterThanOrEqual(inventory!.y + inventory!.height);
      const row = await overview.locator(".comparison-target-row").first().boundingBox();
      const heading = await overview.locator(".comparison-target-heading").first().boundingBox();
      const selection = await overview.locator(".comparison-target-selection").first().boundingBox();
      expect(row).not.toBeNull();
      expect(heading).not.toBeNull();
      expect(selection).not.toBeNull();
      expect(Math.abs(selection!.x - heading!.x)).toBeLessThan(1);
      expect(selection!.y).toBeGreaterThan(heading!.y + heading!.height);
    }
    expect(await overview.locator(".overview-scroll").evaluate(scroll =>
      scroll.scrollWidth - scroll.clientWidth)).toBe(0);
    expect(await page.evaluate(() =>
      document.documentElement.scrollWidth - document.documentElement.clientWidth)).toBe(0);
    if (width === 390) {
      await page.getByRole("button", { name: "Frameworks", exact: true }).click();
      await expect(page.locator('[data-package-framework][aria-current="page"]'))
        .toBeFocused();
      await page.getByRole("button", { name: "Show details", exact: true }).click();
    }
    await chooseSubject(page, "library", "Library");
    await expect(subjectTab(page, "library")).toHaveAttribute("aria-selected", "true");
    await expect(page.locator("#inspector-panel h1")).toHaveText("All libraries");
    await expect(page.locator(".library-subject-list [data-library-subject]"))
      .toHaveCount(4);
    await expect(page.locator(
      `.library-subject-list [data-library-subject="${empty.id}"]`))
      .toContainText("0 types");
    await selectLibrary(page, other.id);
    await expect(page.locator("#inspector-panel h1")).toHaveText("Example.Other");
    const libraryOverview = page.locator(".library-overview-surface");
    expect(await libraryOverview.boundingBox()).toEqual(
      await page.locator("#inspector-panel").boundingBox());
    expect((await libraryOverview.locator(".overview-identity h1").boundingBox())!.width).toBeGreaterThan(100);
    await expect(libraryOverview.locator(".overview-identity [data-package-icon]")).toBeVisible();
    expect(await libraryOverview.locator("[data-package-icon]").getAttribute("src")).toBe(packageIconSource);
    await expect(libraryOverview.locator(".overview-identity-detail")).toHaveText([
      "lib/net10.0/Example.Other.dll",
      "Example.Other, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null",
    ]);
    await expect(libraryOverview.locator(
      ".library-overview-content .section-title h2")).toHaveText([
        "Namespaces",
        "Type kinds",
      ]);
    const namespaceRegion = await libraryOverview.locator(
      ".library-overview-namespaces").boundingBox();
    const kindRegion = await libraryOverview.locator(
      ".library-overview-kinds").boundingBox();
    expect(namespaceRegion).not.toBeNull();
    expect(kindRegion).not.toBeNull();
    if (width === 1440) {
      expect(kindRegion!.x).toBeGreaterThanOrEqual(
        namespaceRegion!.x + namespaceRegion!.width);
      expect(kindRegion!.y).toBeCloseTo(namespaceRegion!.y, 0);
    } else {
      expect(kindRegion!.x).toBeCloseTo(namespaceRegion!.x, 0);
      expect(kindRegion!.y).toBeGreaterThanOrEqual(
        namespaceRegion!.y + namespaceRegion!.height);
    }
    await expect(libraryOverview.locator(".overview-surface-head p")).toHaveText("1 type · 1 member");
    await expect(libraryOverview.locator(".overview-controls")).toHaveCount(0);
    await expect(overview).toHaveCount(0);
    await chooseSubject(page, "package", "Package");
    await expect(overview).toBeVisible();
    await selectLibrary(page, empty.id);
    await expect(libraryOverview.getByRole("heading", { level: 1 })).toHaveText("Example.Empty");
    await expect(libraryOverview.locator(".overview-surface-head p")).toHaveText("0 types · 0 members");
    await expect(libraryOverview.locator(".library-overview-namespaces"))
      .toContainText("No public namespaces.");
    await expect(libraryOverview.locator(".library-overview-kinds"))
      .toContainText("No public types.");
    await expect(libraryOverview.locator(
      "[data-namespace-jump], [data-kind-jump]")).toHaveCount(0);
    await expect(libraryOverview.locator(".overview-surface-footer")).toBeVisible();
  });
}

for (const subject of ["Package", "Library"]) {
  test(`both package icons retain the existing image fallback on ${subject} Overview`, async ({ page }) => {
    await installFacades(page);
    await page.goto(root);
    if (subject === "Library") {
      await chooseSubject(page, "library", "Library");
      await expect(page.locator(".library-overview-surface")).toBeVisible();
    }
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
}

test("Library Overview discloses incomplete exact-Library inspection", async ({ page }) => {
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
    { libraryApiIncomplete: true });
  await page.goto(root);
  await selectLibrary(page, core.id);

  const warning = page.locator(".library-overview-surface .metadata-warning");
  await expect(warning).toContainText(
    "This library could not be inspected completely");
  await expect(warning).toContainText(
    "TypeDefinitions: BadImageFormat: invalid row");
  await expect(page.locator(".overview-surface-head p"))
    .toHaveText("1 type · 1 member");
});

test("Library Overview retries a failed exact-Library request only on demand", async ({ page }) => {
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
    { libraryApiFailure: true });
  await page.goto(root);
  await selectLibrary(page, core.id);

  await expect(page.getByRole("heading", { name: "Public API unavailable" }))
    .toBeVisible();
  await expect(page.locator("html")).toHaveAttribute(
    "data-library-api-attempts",
    "1");
  await page.waitForTimeout(100);
  await expect(page.locator("html")).toHaveAttribute(
    "data-library-api-attempts",
    "1");

  await page.getByRole("button", { name: "Retry", exact: true }).click();
  await expect(page.locator("html")).toHaveAttribute(
    "data-library-api-attempts",
    "2");
  await expect(page.getByRole("heading", { name: "Public API unavailable" }))
    .toBeVisible();
});

for (const [selectedLibrary, activation] of [[core, "click"], [empty, "keyboard"]] as const) {
  test(`narrow Package navigation returns to ${selectedLibrary.name} details with ${activation}`, async ({ page }) => {
    await page.setViewportSize({ width: 480, height: 900 });
    await installFacades(page);
    await page.goto(root);
    await expect(page.locator(".package-overview-surface [data-lib-scope]"))
      .toHaveCount(0);

    const frameworks = page.getByRole("button", { name: "Frameworks", exact: true });
    await frameworks.click();
    await expect(page.locator('[data-package-framework][aria-current="page"]'))
      .toBeFocused();
    const location = page.url();
    const historyLength = await page.evaluate(() => history.length);
    await page.getByRole("button", { name: "Show details", exact: true }).click();
    await expect(page.locator("#inspector-panel")).toBeVisible();
    await expect(frameworks).toBeFocused();
    expect(page.url()).toBe(location);
    expect(await page.evaluate(() => history.length)).toBe(historyLength);

    await selectLibrary(page, selectedLibrary.id);
    await expect(subjectTab(page, "library")).toHaveAttribute("aria-selected", "true");
    await expect(page.locator(".content-frame")).toHaveAttribute("data-content-pane", "detail");
    await expect(page.locator("#inspector-panel")).toBeVisible();
    await expect(page.locator("#inspector-panel h1")).toHaveText(selectedLibrary.name);
    await expect(page.locator("#content-navigation-toggle")).toBeFocused();

    if (selectedLibrary.publicTypes > 0) {
      await chooseSubject(page, "type", "Type");
      await page.getByRole("button", { name: "Types", exact: true }).click();
      await expect(page.locator("#type-list")).toBeFocused();
      await expect(page.locator("#type-list [data-type]"))
        .toHaveCount(selectedLibrary.publicTypes);
      await page.getByRole("button", { name: "Show details", exact: true }).click();
      await expect(page.locator("#inspector-panel")).toBeVisible();
      await expect(page.locator("#content-navigation-toggle")).toBeFocused();
    } else {
      await expect(subjectTab(page, "type")).toHaveCount(0);
    }
  });
}

test("production navigation separates Package, Library, Type and Member", async ({ page }) => {
  const errors: string[] = [];
  page.on("pageerror", error => errors.push(error.message));
  await installFacades(page);
  await page.goto(root);
  await expect(inspectorTab(page, "data-package-lens", "dependencies"))
    .toBeVisible();
  await expect(page.locator('[data-library-lens]')).toHaveCount(0);
  await expect(page.locator(".package-overview-surface [data-lib-scope]"))
    .toHaveCount(0);

  await selectLibrary(page, other.id);
  await expect(subjectTab(page, "library")).toHaveAttribute("aria-selected", "true");
  await expect(page.locator("#inspector-panel h1")).toHaveText("Example.Other");
  await chooseInspector(page, "data-library-lens", "references", "References");
  await expect(page.locator("#inspector-panel")).toContainText("Example.Other.Dependency");
  await expect(page.locator("html")).toHaveAttribute("data-reference-request", "asset:other");

  await chooseSubject(page, "package", "Package");
  await selectLibrary(page, core.id);
  await chooseSubject(page, "type", "Type");
  await page.locator('#type-list [data-type]').click();
  await expect(subjectTab(page, "type")).toHaveAttribute("aria-selected", "true");
  await chooseSubject(page, "member", "Member");
  await expect(subjectTab(page, "member")).toHaveAttribute("aria-selected", "true");
  await expect(page.locator(".inspected-target")).toContainText("Example.Core");
  await expect(page.locator(".inspected-target")).toContainText("Example.Widget");
  await expect(page.locator(".inspected-target")).toContainText("Run");
  expect(errors).toEqual([]);
});

test("direct Library subject entry scopes Types before and after refresh", async ({ page }) => {
  await installFacades(page);
  await page.goto(root);
  await chooseSubject(page, "library", "Library");
  await expect(page.locator("#inspector-panel h1")).toHaveText("All libraries");
  await expect(page.locator(".library-subject-list [data-library-subject]"))
    .toHaveCount(4);
  await chooseSubject(page, "type", "Type");
  await expect(page.locator("#type-list [data-type]")).toHaveCount(2);
  await expect(page.locator("#type-list")).toContainText("Widget");
  await expect(page.locator("#type-list")).toContainText("Neighbor");
  await page.reload();
  await expect(subjectTab(page, "type")).toHaveAttribute("aria-selected", "true");
  await expect(page.locator("#type-list [data-type]")).toHaveCount(2);
  await expect(page.locator("#type-list")).toContainText("Widget");
});

for (const width of [900, 390]) {
  test(`Library navigation commits aggregate and exact subjects at ${width}px`, async ({ page }) => {
    await page.setViewportSize({ width, height: 900 });
    await installFacades(page);
    await page.goto(root);
    await chooseSubject(page, "library", "Library");
    if (width === 390)
      await page.getByRole("button", { name: "Libraries", exact: true }).click();
    const list = page.locator(".library-subject-list");
    await list.focus();
    const aggregateLocation = page.url();

    await page.keyboard.press("ArrowDown");
    await expect(page.locator("#inspector-panel h1")).toHaveText("All libraries");
    await expect(page).toHaveURL(aggregateLocation);
    await page.keyboard.press("Enter");
    await expect(page.locator("#inspector-panel h1")).toHaveText(core.name);
    await expect(page.locator(
      `.library-subject-list [data-library-subject="${core.id}"]`))
      .toHaveAttribute("aria-selected", "true");

    if (width === 390)
      await page.getByRole("button", { name: "Libraries", exact: true }).click();
    await page.locator(".library-subject-list").focus();
    await page.keyboard.press("Home");
    await page.keyboard.press("Enter");
    await expect(page.locator("#inspector-panel h1")).toHaveText("All libraries");
    await expect(page.locator(
      '.library-subject-list [data-library-subject="all"]'))
      .toHaveAttribute("aria-selected", "true");
  });
}

for (const [lens, label] of [
  ["compare", "Compare"],
  ["references", "References"],
  ["integrations", "Integrations"],
  ["analysis", "Analysis"],
  ["metadata", "Metadata"],
] as const) {
  test(`aggregate Library makes ${label} exact-only`, async ({ page }) => {
    await installFacades(page);
    await page.goto(root);
    await chooseSubject(page, "library", "Library");
    await chooseInspector(page, "data-library-lens", lens, label);
    await expect(page.getByRole("heading", {
      name: `${label} requires one Library`,
    })).toBeVisible();
  });
}

test("returning to Library retains its inspector and selected Type context", async ({ page }) => {
  await installFacades(page, {
    ...surface,
    assemblies: [{ ...core, publicTypes: 2, publicMembers: 2 }, other, empty],
    types: [...surface.types, type("Example.SecondWidget", core)],
    accessibility: surface.accessibility.map(bucket => ({ ...bucket, count: 3 })),
    totalMembers: 3,
  });
  await page.goto(root);
  await selectLibrary(page, core.id);
  await chooseInspector(page, "data-library-lens", "references", "References");
  await expect(page.locator("#inspector-panel")).toContainText("Example.Core.Dependency");
  await chooseSubject(page, "type", "Type");
  await page.locator('#type-list [data-type="asset:core:Example.SecondWidget"]').click();
  await chooseSubject(page, "member", "Member");
  await expect(subjectTab(page, "member")).toHaveAttribute("aria-selected", "true");
  await expect(page.locator(".inspected-target")).toContainText("Example.SecondWidget");
  await chooseSubject(page, "type", "Type");
  await expect(subjectTab(page, "type")).toHaveAttribute("aria-selected", "true");
  await chooseSubject(page, "library", "Library");
  await expect(inspectorTab(page, "data-library-lens", "references"))
    .toHaveAttribute("aria-selected", "true");
  await expect(page.locator("#inspector-panel")).toContainText("Example.Core.Dependency");
  await expect(page.locator(
    `.library-subject-list [data-library-subject="${core.id}"]`))
    .toHaveAttribute("aria-selected", "true");
  await chooseSubject(page, "type", "Type");
  await expect(subjectTab(page, "type")).toHaveAttribute("aria-selected", "true");
  await expect(page.locator(".inspected-target")).toContainText("Example.SecondWidget");
});

test("empty Library metadata survives refresh and history without selecting a neighbor", async ({ page }) => {
  await installFacades(page);
  await page.goto(root);
  await selectLibrary(page, empty.id);
  await expect(page.locator("#inspector-panel")).toContainText("No public types");
  await expect(page.locator('[data-scope="type"]')).toHaveCount(0);
  await chooseInspector(page, "data-library-lens", "metadata", "Metadata");
  await expect(page.locator("#inspector-panel")).toContainText("Example.Empty.dll");
  await expect(page.locator("html")).toHaveAttribute("data-metadata-request", "asset:empty");
  await page.locator('[data-mde-open="0"]').click();
  await expect(page.locator("html")).toHaveAttribute("data-table-request", "asset:empty");
  await page.keyboard.press("Escape");
  await page.keyboard.press("Escape");
  const shared = page.url();
  await page.reload();
  await expect(inspectorTab(page, "data-library-lens", "metadata"))
    .toHaveAttribute("aria-selected", "true");
  await expect(page.locator("#inspector-panel")).toContainText("Example.Empty.dll");
  await expect(page.locator("#type-list [data-type]")).toHaveCount(0);
  await chooseSubject(page, "package", "Package");
  await page.getByRole("button", { name: "Application menu", exact: true }).press("Alt+ArrowLeft");
  await expect(page.locator("#inspector-panel")).toContainText("Example.Empty.dll");
  await expect(page.locator(".inspected-target")).not.toContainText("Widget");
  await expect(page).toHaveURL(shared);
  await chooseSubject(page, "package", "Package");
  await chooseSubject(page, "library", "Library");
  await expect(inspectorTab(page, "data-library-lens", "metadata"))
    .toHaveAttribute("aria-selected", "true");
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
  await chooseSubject(page, "library", "Library");
  const list = page.locator(".library-subject-list");
  await list.focus();
  await page.keyboard.press("ArrowDown");
  await page.keyboard.press("Enter");
  await expect(subjectTab(page, "library")).toHaveAttribute("aria-selected", "true");
  await expect(page.locator("#inspector-panel h1")).toHaveText("Example.Core");
  await page.reload();
  await expect(subjectTab(page, "library")).toHaveAttribute("aria-selected", "true");
});

test("browser history restores each retained Workspace Library", async ({ page }) => {
  await page.addInitScript(() => localStorage.setItem(
    "inspect-recent-packages",
    JSON.stringify([{ id: "Second.Package", version: "1.0.0", framework: "net10.0" }]),
  ));
  await installFacades(page);
  await page.goto(root);
  await selectLibrary(page, core.id);
  await page.keyboard.press("Control+p");
  await page.locator('[data-sl-pkg-recent="Second.Package"]').click();
  await expect(page.locator(".inspected-target")).toContainText("Second.Package");
  await expect(subjectTab(page, "library")).toHaveAttribute("aria-selected", "true");
  await expect(page.locator(".library-overview-surface h1")).toHaveText("All libraries");
  await page.goBack();
  await expect(page.locator(".inspected-target")).toContainText("Example.Package");
  await expect(page.locator("#inspector-panel h1")).toHaveText("Example.Core");
  await page.goForward();
  await expect(page.locator(".inspected-target")).toContainText("Second.Package");
  await expect(subjectTab(page, "library")).toHaveAttribute("aria-selected", "true");
  await selectLibrary(page, other.id);
  await chooseSubject(page, "type", "Type");
  await expect(page.locator("#type-list [data-type]")).toHaveCount(1);
  await expect(page.locator("#type-list")).toContainText("Neighbor");
  await page.reload();
  await expect(page.locator(".inspected-target")).toContainText("Example.Other");
  await expect(page.locator("#type-list [data-type]")).toHaveCount(1);
  await expect(page.locator("#type-list")).toContainText("Neighbor");
});

test("browser history restores the incoming retained Library ancestry", async ({ page }) => {
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
  await selectLibrary(page, core.id);
  await page.keyboard.press("Control+p");
  await page.locator('[data-sl-pkg-recent="Second.Package"]').click();
  await expect(page.locator("#inspector-panel h1")).toHaveText("All libraries");

  await page.goBack();
  await expect(subjectTab(page, "library")).toHaveAttribute("aria-selected", "true");
  await expect(page.locator('[data-subject-tab][data-scope="library"]')).toHaveCount(1);
  await expect(page.locator('[data-subject-tab][data-scope="type"]')).toHaveCount(1);
  await expect(page.locator("#inspector-panel h1")).toHaveText("Example.Core");

  await page.goForward();
  await expect(subjectTab(page, "library")).toHaveAttribute("aria-selected", "true");
  await chooseSubject(page, "package", "Package");
  await chooseSubject(page, "library", "Library");
  await expect(page.locator("#inspector-panel h1")).toHaveText("All libraries");
  await page.getByRole("button", { name: "Application menu", exact: true }).press("Alt+ArrowLeft");
  await expect(subjectTab(page, "package")).toHaveAttribute("aria-selected", "true");
  await page.getByRole("button", { name: "Application menu", exact: true }).press("Alt+ArrowRight");
  await expect(page.locator("#inspector-panel h1")).toHaveText("All libraries");
  await page.reload();
  await expect(page.locator("#inspector-panel h1")).toHaveText("All libraries");
});

test("browser history from before reload reuses the active Workspace", async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  await page.addInitScript(() => localStorage.setItem(
    "inspect-recent-packages",
    JSON.stringify([{ id: "Second.Package", version: "1.0.0", framework: "net10.0" }]),
  ));
  await installFacades(page);
  await page.goto(root);
  await selectLibrary(page, core.id);
  await page.keyboard.press("Control+p");
  await page.locator('[data-sl-pkg-recent="Second.Package"]').click();
  await expect(page.locator(".inspected-target")).toContainText("Second.Package");

  const previousSession = (await currentWorkspaceHistoryState(page)).session;
  await page.reload();
  await expect.poll(async () =>
    (await currentWorkspaceHistoryState(page)).session).not.toBe(previousSession);
  const reloadedWorkspace = await currentWorkspaceHistoryState(page);
  await page.goBack();
  await expect(page.locator(".inspected-target")).toContainText("Example.Package");
  await expect.poll(() =>
    currentWorkspaceHistoryState(page)).toEqual(reloadedWorkspace);

  await page.goForward();
  await expect(page.locator(".inspected-target")).toContainText("Second.Package");
  await expect.poll(() =>
    currentWorkspaceHistoryState(page)).toEqual(reloadedWorkspace);
  await page.locator('[data-application-scope="workspace"]').click();
  await expect(page.locator(".workspace-card")).toHaveCount(1);
  await expect(page.locator(".query-notice-text", {
    hasText: "Workspace limit reached",
  })).toHaveCount(0);
});

for (const startingSubject of ["Package", "Library", "Type", "Member"]) {
  test(`the type command enters the target Library from ${startingSubject}`, async ({ page }) => {
    await installFacades(page);
    await page.goto(root);
    if (startingSubject !== "Package") {
      await selectLibrary(page, core.id);
    }
    if (startingSubject === "Type" || startingSubject === "Member") {
      await chooseSubject(page, "type", "Type");
      await page.locator('#type-list [data-type="asset:core:Example.Widget"]').click();
    }
    if (startingSubject === "Member") {
      await chooseSubject(page, "member", "Member");
    }
    await expect(subjectTab(page, startingSubject.toLowerCase()))
      .toHaveAttribute("aria-selected", "true");
    await page.keyboard.press("Control+k");
    await page.locator("#spotlight-input").fill("type Neighbor");
    await expect(page.locator("#spotlight-results")).toContainText("type Neighbor");
    await page.keyboard.press("Enter");
    await expect(page.locator("#spotlight-input")).toHaveCount(0);
    await expect(subjectTab(page, "type")).toHaveAttribute("aria-selected", "true");
    await expect(page.locator(".inspected-target")).toContainText("Example.Other");
    await expect(page.locator(".inspected-target")).toContainText("Example.Neighbor");
    await expect(page.locator("#type-list [data-type]"))
      .toHaveCount(startingSubject === "Package" ? 2 : 1);
    if (startingSubject === "Package")
      await expect(page.locator("#type-list")).toContainText("Widget");
    else
      await expect(page.locator("#type-list")).not.toContainText("Widget");
    await page.reload();
    await expect(page.locator(".inspected-target")).toContainText("Example.Other");
    await expect(page.locator(".inspected-target")).toContainText("Example.Neighbor");
  });
}

test("an open Chooser yields Spotlight keyboard ownership", async ({ page }) => {
  await installFacades(page);
  await page.goto(root);
  await selectLibrary(page, core.id);
  await page.setViewportSize({ width: 390, height: 900 });

  const trigger = page.locator("[data-navigation-trigger='inspector']");
  const menu = page.locator("#inspector-navigation-menu");
  await trigger.click();
  const overview = inspectorTab(page, "data-library-lens", "overview");
  const references = menu.getByRole("menuitemradio", { name: "References" });
  await references.focus();
  await expect(references).toBeFocused();
  const url = page.url();

  await page.keyboard.press("Control+p");
  const input = page.locator("#spotlight-input");
  await expect(input).toBeFocused();
  await expect(menu).toBeHidden();
  expect(await input.evaluate(element => {
    const bounds = element.getBoundingClientRect();
    const target = document.elementFromPoint(
      bounds.left + bounds.width / 2,
      bounds.top + bounds.height / 2);
    return target === element || element.contains(target);
  })).toBe(true);
  await input.click();
  await expect(overview).toHaveAttribute("aria-selected", "true");
  expect(page.url()).toBe(url);

  await page.keyboard.press("Escape");
  await expect(input).toHaveCount(0);
  await expect(menu).toBeVisible();
  await expect(trigger).toHaveAttribute("aria-expanded", "true");
  await expect(references).toBeFocused();
  await page.keyboard.press("Escape");
  await expect(menu).toBeHidden();
  await expect(trigger).toBeFocused();
  await expect(overview).toHaveAttribute("aria-selected", "true");
  expect(page.url()).toBe(url);

  await trigger.click();
  await page.keyboard.press("Control+p");
  await expect(input).toBeFocused();
  await page.keyboard.press("Tab");
  await expect.poll(() => page.evaluate(() =>
    document.activeElement?.closest(".spotlight") != null)).toBe(true);
});

test("Spotlight backdrop dismissal restores an open Chooser", async ({
  page,
}) => {
  await installFacades(page);
  await page.goto(root);
  await selectLibrary(page, core.id);
  await page.setViewportSize({ width: 390, height: 900 });

  const trigger = page.locator("[data-navigation-trigger='inspector']");
  const menu = page.locator("#inspector-navigation-menu");
  await trigger.click();
  const overview = inspectorTab(page, "data-library-lens", "overview");
  const references = menu.getByRole("menuitemradio", { name: "References" });
  await references.focus();
  const url = page.url();

  await page.keyboard.press("Control+p");
  await expect(page.locator("#spotlight-input")).toBeFocused();
  await expect(menu).toBeHidden();
  await page.locator("#spotlight-backdrop").click({
    position: { x: 5, y: 5 },
  });

  await expect(page.locator("#spotlight-input")).toHaveCount(0);
  await expect(menu).toBeVisible();
  await expect(trigger).toHaveAttribute("aria-expanded", "true");
  await expect(references).toBeFocused();
  await expect(overview).toHaveAttribute("aria-selected", "true");
  expect(page.url()).toBe(url);
});

test("Chooser keyboard input does not commit workspace navigation", async ({
  page,
}) => {
  await installFacades(page);
  await page.goto(root);
  await selectLibrary(page, core.id);
  await page.setViewportSize({ width: 390, height: 900 });

  const url = page.url();
  const overview = inspectorTab(page, "data-library-lens", "overview");
  const inspectorTrigger =
    page.locator("[data-navigation-trigger='inspector']");
  const inspectorMenu = page.locator("#inspector-navigation-menu");
  await inspectorTrigger.click();
  const references =
    inspectorMenu.getByRole("menuitemradio", { name: "References" });
  await page.keyboard.press("r");
  await expect(references).toBeFocused();
  await page.keyboard.press("2");
  await page.keyboard.press("z");
  await expect(page.locator("#spotlight-input")).toHaveCount(0);
  await page.keyboard.press("ArrowRight");
  await expect(references).toBeFocused();
  await expect(inspectorMenu).toBeVisible();
  await expect(overview).toHaveAttribute("aria-selected", "true");
  expect(page.url()).toBe(url);
  await page.keyboard.press("Escape");

  const subjectTrigger =
    page.locator("[data-navigation-trigger='subject']");
  const subjectMenu = page.locator("#subject-navigation-menu");
  await subjectTrigger.click();
  const typeItem = subjectMenu.getByRole("menuitemradio", { name: "Type" });
  await typeItem.focus();
  await page.keyboard.press("ArrowLeft");
  await expect(typeItem).toBeFocused();
  await expect(subjectMenu).toBeVisible();
  await expect(subjectTab(page, "library"))
    .toHaveAttribute("aria-selected", "true");
  await expect(overview).toHaveAttribute("aria-selected", "true");
  expect(page.url()).toBe(url);
  await page.keyboard.press("Escape");
  await expect(subjectMenu).toBeHidden();
  await expect(subjectTrigger).toBeFocused();
});

for (const [command, dialog] of [
  ["settings", "#settings-dialog"],
  ["keyboard help", "#keyboard-help-dialog"],
] as const) {
  test(`an open Chooser regains focus after ${command}`, async ({ page }) => {
    await installFacades(page);
    await page.goto(root);
    await selectLibrary(page, core.id);
    await page.setViewportSize({ width: 390, height: 900 });

    const trigger = page.locator("[data-navigation-trigger='inspector']");
    const menu = page.locator("#inspector-navigation-menu");
    await trigger.click();
    const overview = inspectorTab(page, "data-library-lens", "overview");
    const references = menu.getByRole("menuitemradio", { name: "References" });
    await references.focus();
    const url = page.url();

    await page.keyboard.press("Control+k");
    const input = page.locator("#spotlight-input");
    await input.fill(command);
    await page.keyboard.press("Enter");
    await expect(page.locator(dialog)).toBeVisible();
    await page.keyboard.press("Escape");
    await expect(page.locator(dialog)).toHaveCount(0);
    await expect(menu).toBeVisible();
    await expect(trigger).toHaveAttribute("aria-expanded", "true");
    await expect(references).toBeFocused();

    await page.keyboard.press("Escape");
    await expect(menu).toBeHidden();
    await expect(trigger).toBeFocused();
    await expect(overview).toHaveAttribute("aria-selected", "true");
    expect(page.url()).toBe(url);
  });
}
