import { expect, test } from "@playwright/test";

test.beforeEach(async ({ page }) => {
  await page.goto("/browser/type-graph-explorer.html");
  await expect(page.locator("#type-graph-diagram svg")).toBeVisible();
});

test("Type relationships relocates the live graph and warnings, not Metadata facts", async ({ page }) => {
  await page.evaluate(() => window.typeExploreProbe.update("partial"));
  await page.getByRole("button", { name: "Zoom in", exact: true }).click();
  const style = await page.locator("#type-graph-diagram svg").getAttribute("style");
  await page.evaluate(() => window.typeExploreProbe.rememberSvg());
  const before = await page.evaluate(() => ({
    ...window.typeExploreProbe.counts(), url: location.href, history: history.length,
  }));
  await page.getByRole("button", { name: "Explore", exact: true }).click();
  const dialog = page.getByRole("dialog", { name: "Type relationships" });
  await expect(dialog.locator(".metadata-warning")).toContainText("Fixture relationship could not be projected.");
  await expect(dialog.locator(".metadata-shape-section, .metadata-surface-footer, .type-chip-list")).toHaveCount(0);
  await expect(page.locator("#graph-explorer-title")).toBeFocused();
  expect(await page.evaluate(() => window.typeExploreProbe.sameSvg())).toBe(true);
  expect(await page.locator("#type-graph-diagram svg").getAttribute("style")).toBe(style);
  await page.keyboard.press("Shift+Tab");
  await expect(page.getByRole("button", { name: "Fit", exact: true })).toBeFocused();
  await page.keyboard.press("Tab");
  await expect(page.getByRole("button", { name: "Close", exact: true })).toBeFocused();
  await page.keyboard.press("Escape");
  await expect(page.getByRole("button", { name: "Explore", exact: true })).toBeFocused();
  expect(await page.evaluate(() => window.typeExploreProbe.sameSvg())).toBe(true);
  expect(await page.locator("#type-graph-diagram svg").getAttribute("style")).toBe(style);
  expect(await page.evaluate(() => ({
    ...window.typeExploreProbe.counts(), url: location.href, history: history.length,
  }))).toEqual(before);
});

test("pending Type graph rendering completes across placement changes without another mount", async ({ page }) => {
  await page.evaluate(() => window.typeExploreProbe.startPending());
  await expect(page.getByText("Rendering graph…", { exact: true })).toBeVisible();
  const before = await page.evaluate(() => window.typeExploreProbe.counts());
  await page.getByRole("button", { name: "Explore", exact: true }).click();
  await page.keyboard.press("Escape");
  await page.getByRole("button", { name: "Explore", exact: true }).click();
  await page.evaluate(() => window.typeExploreProbe.finishPending());
  await expect(page.getByRole("dialog").locator("svg")).toBeVisible();
  expect(await page.evaluate(() => window.typeExploreProbe.counts())).toEqual(before);
});

for (const activation of ["Enter", "Space"]) {
  test(`Type nodes navigate with ${activation}; unavailable nodes and dragging do not navigate`, async ({ page }) => {
    await page.getByRole("button", { name: "Explore", exact: true }).click();
    const unavailable = page.getByRole("img", { name: /External.Base - not in the browsable/ });
    await expect(unavailable).not.toHaveAttribute("tabindex");
    await unavailable.click();
    expect((await page.evaluate(() => window.typeExploreProbe.counts())).navigations).toBe(0);
    const node = page.getByRole("button", { name: "Open Example.Derived", exact: true });
    const box = await node.boundingBox();
    await page.mouse.move(box!.x + box!.width / 2, box!.y + box!.height / 2);
    await page.mouse.down();
    await page.mouse.move(box!.x + box!.width / 2 + 55, box!.y + box!.height / 2 + 40, { steps: 5 });
    await page.mouse.up();
    expect((await page.evaluate(() => window.typeExploreProbe.counts())).navigations).toBe(0);
    await node.focus();
    await page.keyboard.press(activation);
    await expect(page.getByRole("dialog")).toHaveCount(0);
    await expect(page.locator(".metadata-surface-footer")).toContainText("Example.Derived");
    await expect(page.getByRole("heading", { level: 1 })).toBeFocused();
  });
}

test("same-owner loading and failures stay visible; an empty replacement returns to Metadata", async ({ page }) => {
  await page.getByRole("button", { name: "Explore", exact: true }).click();
  await page.evaluate(() => window.typeExploreProbe.update("loading"));
  await expect(page.getByRole("dialog")).toContainText("Projecting type metadata");
  await page.evaluate(() => window.typeExploreProbe.update("error"));
  await expect(page.getByRole("dialog")).toContainText("Metadata projection failed");
  await expect(page.getByRole("dialog")).toContainText("Fixture projection failure.");
  await page.keyboard.press("Escape");
  await expect(page.getByRole("heading", { name: "Metadata projection failed" })).toBeFocused();
  await expect(page.getByRole("button", { name: "Explore", exact: true })).toBeDisabled();
  await page.evaluate(() => window.typeExploreProbe.update("ready"));
  await page.getByRole("button", { name: "Explore", exact: true }).click();
  await page.evaluate(() => window.typeExploreProbe.update("empty"));
  await expect(page.getByRole("dialog")).toHaveCount(0);
  await expect(page.getByRole("heading", { level: 1 })).toBeFocused();
  await expect(page.getByRole("button", { name: "Explore", exact: true })).toBeDisabled();
});

test("stale projection data is not rendered and coordinate replacement closes Explore", async ({ page }) => {
  await page.evaluate(() => window.typeExploreProbe.update("stale"));
  await expect(page.getByRole("button", { name: "Explore", exact: true })).toBeDisabled();
  await expect(page.locator("#type-graph-diagram")).toHaveCount(0);
  await page.evaluate(() => window.typeExploreProbe.update("ready"));
  await page.getByRole("button", { name: "Explore", exact: true }).click();
  await page.evaluate(() => window.typeExploreProbe.changeOwner());
  await expect(page.getByRole("dialog")).toHaveCount(0);
  await expect(page.getByRole("heading", { level: 1 })).toBeFocused();
});

for (const size of [{ width: 1440, height: 1000 }, { width: 390, height: 844 }]) {
  test(`Type relationships fills the viewport without covering warnings at ${size.width}px`, async ({ page }) => {
    await page.setViewportSize(size);
    await page.evaluate(() => window.typeExploreProbe.update("partial"));
    const inline = await page.locator(".graph-viewport").boundingBox();
    expect(inline!.height).toBeCloseTo(540, 2);
    await page.getByRole("button", { name: "Explore", exact: true }).click();
    const viewport = await page.locator(".graph-viewport").boundingBox();
    expect(viewport!.width).toBeGreaterThan(size.width - 30);
    expect(viewport!.height).toBeGreaterThan(size.height * 0.65);
    await expect(page.locator(".metadata-warning")).toBeInViewport();
    const warning = await page.locator(".metadata-warning").boundingBox();
    const controls = await page.locator(".graph-controls").boundingBox();
    expect(controls!.y + controls!.height).toBeLessThanOrEqual(warning!.y);
    expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBe(size.width);
    await page.getByRole("button", { name: "Fit", exact: true }).click();
    await expect(page.getByRole("button", { name: "Close", exact: true })).toBeInViewport();
    await page.getByRole("button", { name: "Close", exact: true }).click();
    expect((await page.locator(".graph-viewport").boundingBox())!.height).toBeCloseTo(540, 2);
  });
}
