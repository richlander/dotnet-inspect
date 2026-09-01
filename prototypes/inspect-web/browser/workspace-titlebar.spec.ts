import { expect, test, type Page } from "@playwright/test";

async function box(page: Page, selector: string) {
  const value = await page.locator(selector).boundingBox();
  expect(value).not.toBeNull();
  return value!;
}

test("workspace windows use natural width and leave room for the broad title", async ({
  page,
}) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  await page.goto("/browser/workspace-titlebar.html");

  const workspaceStrip = await box(page, ".workspace-strip");
  const workspaceTitle = await box(page, ".workspace-title");
  const titleActions = await box(page, ".title-actions");
  const workspaceWindows =
    await page.locator(".workspace-window").evaluateAll(elements =>
      elements.map(element => element.getBoundingClientRect().width));

  expect(workspaceStrip.width).toBeLessThan(400);
  expect(workspaceTitle.width).toBeGreaterThan(100);
  expect(workspaceWindows[0]).not.toBe(workspaceWindows[1]);
  expect(titleActions.x + titleActions.width).toBeCloseTo(1440, 0);
});

test("crowded workspaces scroll before title-bar controls move", async ({
  page,
}) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  await page.goto("/browser/workspace-titlebar.html?count=12");

  const overflow = await page.locator(".workspace-strip").evaluate(element => ({
    clientWidth: element.clientWidth,
    scrollWidth: element.scrollWidth,
  }));
  const titleActions = await box(page, ".title-actions");

  expect(overflow.scrollWidth).toBeGreaterThan(overflow.clientWidth);
  expect(titleActions.x + titleActions.width).toBeCloseTo(1440, 0);
});

test("narrow layout preserves the three-row hierarchy and app controls", async ({
  page,
}) => {
  await page.setViewportSize({ width: 760, height: 900 });
  await page.goto("/browser/workspace-titlebar.html");

  const titleActions = await box(page, ".title-actions");
  const titlebar = await box(page, ".titlebar");
  const lensbar = await box(page, ".lensbar");
  const targetSelector = await box(page, ".detail-head");

  await expect(page.locator("#package-version")).toBeVisible();
  await expect(page.locator("#framework")).toBeVisible();
  await expect(page.locator("#open-search")).toBeVisible();
  await expect(page.locator("#go-home")).toBeVisible();
  await expect(page.locator("#open-settings")).toBeVisible();
  expect(titlebar.y).toBeLessThan(lensbar.y);
  expect(lensbar.y).toBeLessThan(targetSelector.y);
  expect(titleActions.x + titleActions.width).toBeCloseTo(760, 0);
  expect(targetSelector.x + targetSelector.width).toBeLessThanOrEqual(760);
});
