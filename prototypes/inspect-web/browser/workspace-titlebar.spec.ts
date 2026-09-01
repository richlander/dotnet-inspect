import { expect, test, type Page } from "@playwright/test";

async function box(page: Page, selector: string) {
  const value = await page.locator(selector).boundingBox();
  expect(value).not.toBeNull();
  return value!;
}

test("the title bar gives the active workspace identity the available space", async ({
  page,
}) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  await page.goto("/browser/workspace-titlebar.html");

  const workspaceTitle = await box(page, ".workspace-title");
  const titleActions = await box(page, ".title-actions");

  expect(workspaceTitle.width).toBeGreaterThan(100);
  expect(titleActions.x + titleActions.width).toBeCloseTo(1440, 0);
  await expect(page.locator(".titlebar")).not.toContainText("0:");
  await expect(page.locator(".titlebar")).not.toContainText("Platform");
  await expect(page.locator(".workspace-window")).toHaveCount(0);
});

test("subjects, inspectors, and package selectors share the title bar", async ({
  page,
}) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  await page.goto("/browser/workspace-titlebar.html");

  const titlebar = await box(page, ".titlebar");
  const brand = await box(page, ".brand");
  const lensbar = await box(page, ".lensbar");
  const version = await box(page, "#package-version");
  const framework = await box(page, "#framework");

  expect(lensbar.x).toBeGreaterThanOrEqual(brand.x + brand.width);
  expect(version.y).toBeGreaterThanOrEqual(titlebar.y);
  expect(framework.y + framework.height).toBeLessThanOrEqual(
    titlebar.y + titlebar.height);
  await expect(page.locator(".titlebar #package-version")).toBeVisible();
  await expect(page.locator(".titlebar #framework")).toBeVisible();
});

test("narrow layout preserves the two-line hierarchy and primary actions", async ({
  page,
}) => {
  await page.setViewportSize({ width: 760, height: 900 });
  await page.goto("/browser/workspace-titlebar.html");

  const titleActions = await box(page, ".title-actions");
  const titlebar = await box(page, ".titlebar");
  const inspectedSubjectLine = await box(page, ".detail-head");
  const version = await box(page, "#package-version");
  const namespacePicker = await box(page, ".namespace-picker");
  const typeList = await box(page, ".type-list");

  await expect(page.locator("#package-version")).toBeVisible();
  await expect(page.locator("#framework")).toBeVisible();
  await expect(page.locator("#open-search")).toBeVisible();
  await expect(page.locator("#go-home")).toBeVisible();
  await expect(page.locator(".subject-identity")).toContainText(
    "System.Text.Json.JsonSerializer");
  await expect(page.locator(".detail-head #share")).toBeVisible();
  await expect(page.locator("#copy-name")).toBeVisible();
  await expect(page.locator("#help")).toBeHidden();
  await expect(page.locator("#open-settings")).toBeHidden();
  expect(titlebar.y).toBeLessThan(inspectedSubjectLine.y);
  expect(version.y).toBeGreaterThanOrEqual(titlebar.y);
  expect(version.y + version.height).toBeLessThanOrEqual(titlebar.y + titlebar.height);
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
