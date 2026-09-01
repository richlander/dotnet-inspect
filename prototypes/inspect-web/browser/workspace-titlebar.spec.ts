import { expect, test, type Page } from "@playwright/test";

async function box(page: Page, selector: string) {
  const value = await page.locator(selector).boundingBox();
  expect(value).not.toBeNull();
  return value!;
}

test("the title bar contains no tab-like active package identity", async ({
  page,
}) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  await page.goto("/browser/workspace-titlebar.html?package=1");

  const titleActions = await box(page, ".title-actions");

  expect(titleActions.x + titleActions.width).toBeCloseTo(1440, 0);
  await expect(page.locator(".workspace-title")).toHaveCount(0);
  await expect(page.locator(".titlebar")).not.toContainText("0:");
  await expect(page.locator(".titlebar")).not.toContainText("Platform");
  await expect(page.locator(".workspace-window")).toHaveCount(0);
});

test("subjects and inspectors share the title bar without package selectors", async ({
  page,
}) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  await page.goto("/browser/workspace-titlebar.html?package=1");

  const brand = await box(page, ".brand");
  const lensbar = await box(page, ".lensbar");

  expect(lensbar.x).toBeGreaterThanOrEqual(brand.x + brand.width);
  await expect(page.locator(".titlebar #package-version")).toHaveCount(0);
  await expect(page.locator(".titlebar #framework")).toHaveCount(0);
  await expect(page.locator(".detail-scroll #package-version")).toBeVisible();
  await expect(page.locator(".detail-scroll #framework")).toBeVisible();
  await expect(page.locator("#go-home")).toHaveCount(0);
});

test("narrow layout preserves the two-line hierarchy and primary actions", async ({
  page,
}) => {
  await page.setViewportSize({ width: 760, height: 900 });
  await page.goto("/browser/workspace-titlebar.html");

  const titleActions = await box(page, ".title-actions");
  const titlebar = await box(page, ".titlebar");
  const inspectedSubjectLine = await box(page, ".detail-head");
  const namespacePicker = await box(page, ".namespace-picker");
  const typeList = await box(page, ".type-list");

  await expect(page.locator("#package-version")).toHaveCount(0);
  await expect(page.locator("#framework")).toHaveCount(0);
  await expect(page.locator("#open-search")).toBeVisible();
  await expect(page.locator("#go-home")).toHaveCount(0);
  await expect(page.locator(".subject-identity")).toContainText(
    "System.Text.Json.JsonSerializer");
  await expect(page.locator(".detail-head #share")).toBeVisible();
  await expect(page.locator("#copy-name")).toBeVisible();
  await expect(page.locator("#help")).toBeHidden();
  await expect(page.locator("#open-settings")).toBeHidden();
  expect(titlebar.y).toBeLessThan(inspectedSubjectLine.y);
  expect(titleActions.x + titleActions.width).toBeCloseTo(760, 0);
  expect(inspectedSubjectLine.x + inspectedSubjectLine.width).toBeLessThanOrEqual(760);
  expect(namespacePicker.y + namespacePicker.height).toBeLessThanOrEqual(typeList.y);
});

test("Workspace gives retained coordinates the pane and keeps Share readable", async ({
  page,
}) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  await page.goto("/browser/workspace-titlebar.html?workspace=1");

  const list = await box(page, ".workspace-coordinate-list");
  const lastCoordinate = await box(page, ".workspace-coordinate:last-child");
  const share = await box(page, "#share");

  expect(list.height).toBeGreaterThan(200);
  expect(lastCoordinate.y + lastCoordinate.height)
    .toBeLessThanOrEqual(list.y + list.height);
  expect(share.width).toBeGreaterThan(40);
  await expect(page.locator("#copy-name")).toHaveCount(0);
});
