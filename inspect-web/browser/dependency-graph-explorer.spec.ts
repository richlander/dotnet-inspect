import { expect, test } from "@playwright/test";

test.beforeEach(async ({ page }) => {
  await page.goto("/browser/dependency-graph-explorer.html");
  await expect(page.locator("#dependency-graph-diagram svg")).toBeVisible();
});

test("Dependencies relocates the live graph and group controls, not the lists", async ({ page }) => {
  await page.getByRole("button", { name: "Zoom in", exact: true }).click();
  const style = await page.locator("#dependency-graph-diagram svg").getAttribute("style");
  await page.evaluate(() => window.dependencyExploreProbe.rememberSvg());
  const before = await page.evaluate(() => ({
    ...window.dependencyExploreProbe.counts(), url: location.href, history: history.length,
  }));
  await page.getByRole("button", { name: "Explore", exact: true }).click();
  const dialog = page.getByRole("dialog", { name: "Dependency graph" });
  await expect(dialog.locator(".graph-explorer-kind")).toHaveText("Dependency graph");
  await expect(dialog.locator("#graph-explorer-title")).toHaveText("Example.Package@1.0.0");
  await expect(dialog.locator(".graph-explorer-context")).toHaveText("Target framework net10.0");
  await expect(dialog.locator(".graph-explorer-summary"))
    .toHaveText("callers above · dependencies below · click a package to open");
  await expect(dialog.locator(".dependency-graph-section > .section-title")).toBeHidden();
  await expect(dialog.locator("#dep-tfm-chips")).toBeVisible();
  const legend = dialog.locator(".graph-legend");
  await expect(legend).toContainText("inspected package");
  await expect(legend).toContainText("open in workspace");
  await expect(legend).toContainText("load on selection");
  await expect(legend.locator(".legend-swatch")).toHaveCount(3);
  await expect(dialog.locator("#dep-list-section, #assembly-references, #coordinates")).toHaveCount(0);
  await expect(page.locator("#graph-explorer-title")).toBeFocused();
  expect(await page.evaluate(() => window.dependencyExploreProbe.sameSvg())).toBe(true);
  expect(await page.locator("#dependency-graph-diagram svg").getAttribute("style")).toBe(style);
  await page.keyboard.press("Shift+Tab");
  await expect(page.getByRole("button", { name: "Fit", exact: true })).toBeFocused();
  await page.keyboard.press("Tab");
  await expect(page.getByRole("button", { name: "Close", exact: true })).toBeFocused();
  await page.evaluate(() => document.querySelector<HTMLElement>("#coordinates")!.focus());
  await expect(page.getByRole("button", { name: "Close", exact: true })).toBeFocused();
  await page.keyboard.press("Escape");
  await expect(page.getByRole("button", { name: "Explore", exact: true })).toBeFocused();
  expect(await page.evaluate(() => window.dependencyExploreProbe.sameSvg())).toBe(true);
  expect(await page.locator("#dependency-graph-diagram svg").getAttribute("style")).toBe(style);
  expect(await page.evaluate(() => ({
    ...window.dependencyExploreProbe.counts(), url: location.href, history: history.length,
  }))).toEqual(before);
});

test("group changes stay expanded and preserve an empty selection on Close", async ({ page }) => {
  await page.getByRole("button", { name: "Explore", exact: true }).click();
  await page.getByRole("button", { name: "net11.0", exact: true }).click();
  await expect(page.getByRole("dialog")).toContainText("No connected packages");
  await expect(page.getByRole("button", { name: "net11.0", exact: true })).toHaveAttribute("aria-pressed", "true");
  await expect(page.locator("#dep-list-section")).toHaveText("net11.0: 0 packages");
  await page.getByRole("button", { name: "net10.0", exact: true }).click();
  await expect(page.getByRole("dialog").locator("svg")).toBeVisible();
  await page.getByRole("button", { name: "net11.0", exact: true }).click();
  await page.getByRole("button", { name: "Close", exact: true }).click();
  await expect(page.getByRole("button", { name: "Explore", exact: true })).toBeFocused();
  await expect(page.getByRole("button", { name: "net11.0", exact: true })).toHaveAttribute("aria-pressed", "true");
  await page.getByRole("button", { name: "Explore", exact: true }).click();
  await expect(page.getByRole("dialog")).toContainText("No connected packages");
});

test("a pending diagram completes in the viewer without another mount", async ({ page }) => {
  await page.evaluate(() => window.dependencyExploreProbe.startPending());
  await expect(page.getByText("Rendering graph...", { exact: true })).toBeVisible();
  const before = await page.evaluate(() => window.dependencyExploreProbe.counts());
  await page.getByRole("button", { name: "Explore", exact: true }).click();
  await page.evaluate(() => window.dependencyExploreProbe.finishPending());
  await expect(page.getByRole("dialog").locator("svg")).toBeVisible();
  expect(await page.evaluate(() => window.dependencyExploreProbe.counts())).toEqual(before);
});

test("dependency nodes are keyboard navigable and dragging does not activate them", async ({ page }) => {
  await page.getByRole("button", { name: "Explore", exact: true }).click();
  const node = page.getByRole("button", { name: "Open Loaded.Dependency", exact: true });
  const box = await node.boundingBox();
  await page.mouse.move(box!.x + box!.width / 2, box!.y + box!.height / 2);
  await page.mouse.down();
  await page.mouse.move(box!.x + box!.width / 2 + 55, box!.y + box!.height / 2 + 40, { steps: 5 });
  await page.mouse.up();
  expect((await page.evaluate(() => window.dependencyExploreProbe.counts())).navigations).toBe(0);
  await node.focus();
  await page.keyboard.press("Enter");
  await expect(page.getByRole("dialog")).toHaveCount(0);
  await expect(page.getByRole("heading", { level: 1 })).toHaveText("Dependencies: Loaded.Dependency");
  await expect(page.getByRole("heading", { level: 1 })).toBeFocused();
});

test("unloaded package navigation dismisses the viewer; failure remains inline", async ({ page }) => {
  await page.getByRole("button", { name: "Explore", exact: true }).click();
  await page.getByRole("button", { name: "Load Failed.Dependency", exact: true }).click();
  await expect(page.getByRole("dialog")).toHaveCount(0);
  await expect(page.getByRole("status")).toContainText("fixture acquisition failure");
  await expect(page.getByRole("button", { name: "Explore", exact: true })).toBeFocused();
  await page.getByRole("button", { name: "Explore", exact: true }).click();
  await page.getByRole("button", { name: "Load New.Dependency", exact: true }).focus();
  await page.keyboard.press("Space");
  await expect(page.getByRole("dialog")).toHaveCount(0);
  await expect(page.getByRole("heading", { level: 1 })).toHaveText("Dependencies: New.Dependency");
  await expect(page.getByRole("heading", { level: 1 })).toBeFocused();
});

test("query, diagram, and empty results remain distinct in an already-open viewer", async ({ page }) => {
  await page.getByRole("button", { name: "Explore", exact: true }).click();
  await page.evaluate(() => window.dependencyExploreProbe.update("render-error"));
  await expect(page.getByRole("dialog")).toContainText("Diagram rendering failed");
  await page.evaluate(() => window.dependencyExploreProbe.update("query-error"));
  await expect(page.getByRole("dialog")).toContainText("Dependency query failed");
  await page.evaluate(() => window.dependencyExploreProbe.update("no-groups"));
  await expect(page.getByRole("dialog")).toContainText("No package dependencies");
  await page.keyboard.press("Escape");
  await expect(page.getByRole("heading", { name: "No package dependencies" })).toBeFocused();
  await expect(page.getByRole("button", { name: "Explore", exact: true })).toBeDisabled();
});

test("coordinate replacement closes the viewer and notices travel with the graph", async ({ page }) => {
  await page.getByRole("button", { name: "Explore", exact: true }).click();
  await page.evaluate(() => window.dependencyExploreProbe.showNotices());
  await expect(page.getByRole("dialog")).toContainText("No exact dependency group");
  await expect(page.getByRole("dialog")).toContainText("Some workspace manifests could not be read");
  await expect(page.getByRole("dialog").locator("svg")).toBeVisible();
  await page.evaluate(() => window.dependencyExploreProbe.changeOwner());
  await expect(page.getByRole("dialog")).toHaveCount(0);
  await expect(page.locator("#dependency-graph-diagram svg")).toBeVisible();
});

for (const size of [{ width: 1440, height: 1000 }, { width: 390, height: 844 }]) {
  test(`Dependencies uses the viewport and keeps truncated diagnostics clear at ${size.width}px`, async ({ page }) => {
    await page.setViewportSize(size);
    const inline = await page.locator(".graph-viewport").boundingBox();
    expect(inline!.height).toBeCloseTo(540, 2);
    await page.getByRole("button", { name: "Explore", exact: true }).click();
    const viewport = await page.locator(".graph-viewport").boundingBox();
    expect(viewport!.width).toBeGreaterThan(size.width - 30);
    expect(viewport!.height).toBeGreaterThan(size.height * 0.7);
    await page.getByRole("button", { name: "netstandard2.0", exact: true }).click();
    await expect(page.getByRole("status")).toHaveText("Dependency graph truncated at 80 nodes.");
    await expect(page.getByRole("status")).toBeInViewport();
    const warning = await page.getByRole("status").boundingBox();
    const controls = await page.locator(".graph-controls").boundingBox();
    const legend = await page.locator(".graph-legend").boundingBox();
    expect(controls!.y + controls!.height).toBeLessThanOrEqual(warning!.y);
    expect(warning!.y + warning!.height).toBeLessThanOrEqual(legend!.y);
    expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBe(size.width);
    await page.getByRole("button", { name: "Fit", exact: true }).click();
    const extent = await page.evaluate(() => {
      const graphViewport =
        document.querySelector(".graph-explorer .graph-viewport")!;
      const svg = graphViewport.querySelector("svg")!;
      const viewportRect = graphViewport.getBoundingClientRect();
      const svgRect = svg.getBoundingClientRect();
      return {
        bottom: svgRect.bottom - viewportRect.bottom,
        left: viewportRect.left - svgRect.left,
        right: svgRect.right - viewportRect.right,
        top: viewportRect.top - svgRect.top,
      };
    });
    if (size.width === 1440) {
      expect(Math.max(extent.bottom, extent.left, extent.right, extent.top))
        .toBeLessThanOrEqual(1);
    } else {
      await expect(page.locator(".graph-viewport svg"))
        .toHaveAttribute("style", /scale\(0\.05\)/);
    }
    await expect(page.getByRole("button", { name: "Close", exact: true })).toBeInViewport();
    await page.getByRole("button", { name: "Close", exact: true }).click();
    expect((await page.locator(".graph-viewport").boundingBox())!.height).toBeCloseTo(540, 2);
  });
}
