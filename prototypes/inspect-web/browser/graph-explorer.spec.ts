import { expect, test } from "@playwright/test";

test.beforeEach(async ({ page }) => {
  await page.goto("/browser/graph-explorer.html");
  await expect(page.locator("#diagram svg")).toBeVisible();
});

test("Explore relocates the live graph without remounting or losing zoom", async ({ page }) => {
  await page.getByRole("button", { name: "Zoom in", exact: true }).click();
  const transform = await page.locator("#diagram svg").getAttribute("style");
  await page.evaluate(() => window.graphExploreProbe.rememberSvg());
  const before = await page.evaluate(() => ({
    ...window.graphExploreProbe.counts(),
    history: history.length,
    url: location.href,
  }));
  await page.getByRole("button", { name: "Explore", exact: true }).click();
  await expect(page.getByRole("dialog", { name: "Call graph" })).toBeVisible();
  await expect(page.locator("#graph-explorer-title")).toBeFocused();
  expect(await page.evaluate(() => window.graphExploreProbe.sameSvg())).toBe(true);
  expect(await page.locator("#diagram svg").getAttribute("style")).toBe(transform);
  await page.getByRole("button", { name: "Close", exact: true }).click();
  await expect(page.getByRole("button", { name: "Explore", exact: true })).toBeFocused();
  expect(await page.evaluate(() => window.graphExploreProbe.sameSvg())).toBe(true);
  expect(await page.locator("#diagram svg").getAttribute("style")).toBe(transform);
  expect(await page.evaluate(() => ({
    ...window.graphExploreProbe.counts(),
    history: history.length,
    url: location.href,
  }))).toEqual(before);
});

test("modal contains keyboard focus and makes the background unavailable", async ({ page }) => {
  await page.getByRole("button", { name: "Explore", exact: true }).click();
  await page.keyboard.press("Shift+Tab");
  await expect(page.getByText("Mermaid source", { exact: true })).toBeFocused();
  await page.keyboard.press("Tab");
  await expect(page.getByRole("button", { name: "Close", exact: true })).toBeFocused();
  await page.evaluate(() => document.getElementById("background")!.focus());
  await expect(page.getByRole("button", { name: "Close", exact: true })).toBeFocused();
  await page.locator(".graph-viewport").focus();
  const before = await page.locator("#diagram svg").getAttribute("style");
  await page.keyboard.press("ArrowRight");
  expect(await page.locator("#diagram svg").getAttribute("style")).not.toBe(before);
  await page.keyboard.press("Escape");
  await expect(page.getByRole("dialog")).toHaveCount(0);
  await expect(page.getByRole("button", { name: "Explore", exact: true })).toBeFocused();
});

test("drill and back replace the result without leaving Explore", async ({ page }) => {
  await page.getByRole("button", { name: "Explore", exact: true }).click();
  await page.getByRole("button", { name: "Drill into platform" }).focus();
  await page.keyboard.press("Enter");
  await expect(page.getByRole("dialog")).toContainText("Platform depth 1");
  await expect(page.locator("#diagram svg")).toBeVisible();
  await page.getByRole("button", { name: "Back", exact: true }).click();
  await expect(page.getByRole("dialog")).not.toContainText("Platform depth");
  await expect(page.locator("#diagram svg")).toBeVisible();
  await page.getByRole("button", { name: "Open member", exact: true }).focus();
  await page.keyboard.press("Enter");
  await expect(page.getByRole("dialog")).toHaveCount(0);
  await expect(page.getByRole("heading", { level: 1 })).toHaveText("Member member-two");
  await expect(page.getByRole("heading", { level: 1 })).toBeFocused();
});

test("pending results, errors, and no-body results remain visible in the viewer", async ({ page }) => {
  await page.getByRole("button", { name: "Explore", exact: true }).click();
  await page.evaluate(() => window.graphExploreProbe.update("pending"));
  await expect(page.getByRole("dialog")).toContainText("Scanning callers");
  await expect(page.locator("#diagram svg")).toBeVisible();
  await page.evaluate(() => window.graphExploreProbe.update("ready"));
  await expect(page.getByRole("dialog")).not.toContainText("Scanning callers");
  await page.evaluate(() => window.graphExploreProbe.update("failure"));
  await expect(page.getByRole("dialog")).toContainText("Call graph query failed");
  await page.evaluate(() => window.graphExploreProbe.update("no-body"));
  await expect(page.getByRole("dialog")).toContainText("No IL body");
  await page.getByRole("button", { name: "Close", exact: true }).click();
  await expect(page.getByRole("heading", { name: "No call graph" })).toBeFocused();
});

test("replacement modal and history dismissal never reopen Explore", async ({ page }) => {
  await page.getByRole("button", { name: "Explore", exact: true }).click();
  await page.evaluate(() => window.graphExploreProbe.replaceModal());
  await expect(page.getByRole("dialog", { name: "Settings" })).toBeVisible();
  await expect(page.getByRole("dialog", { name: "Call graph" })).toHaveCount(0);
  await page.getByRole("button", { name: "Done", exact: true }).click();
  await expect(page.getByRole("dialog")).toHaveCount(0);
  await page.getByRole("button", { name: "Explore", exact: true }).click();
  await page.evaluate(() => window.dispatchEvent(new PopStateEvent("popstate")));
  await expect(page.getByRole("dialog")).toHaveCount(0);
  await expect(page.getByRole("heading", { level: 1 })).toHaveText("Member history-member");
});

for (const size of [{ width: 1440, height: 1000 }, { width: 390, height: 844 }]) {
  test(`uses the available graph area at ${size.width}px without page overflow`, async ({ page }) => {
    await page.setViewportSize(size);
    await page.getByRole("button", { name: "Explore", exact: true }).click();
    const viewport = await page.locator(".graph-viewport").boundingBox();
    expect(viewport!.width).toBeGreaterThan(size.width - 30);
    expect(viewport!.height).toBeGreaterThan(size.height * 0.6);
    expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBe(size.width);
    await page.getByRole("button", { name: "Fit", exact: true }).click();
    await page.getByText("Mermaid source", { exact: true }).click();
    await expect(page.getByRole("dialog").locator("pre")).toBeVisible();
    await expect(page.getByRole("button", { name: "Close", exact: true })).toBeInViewport();
    await expect(page.getByRole("button", { name: "Fit", exact: true })).toBeInViewport();
  });
}
