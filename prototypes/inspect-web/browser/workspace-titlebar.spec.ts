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
  await expect(page.locator(".titlebar #open-search")).toHaveCount(0);
  await expect(page.locator(".brand-icon img")).toHaveAttribute(
    "src",
    "/assets/dotnet-inspect-bot.png");
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

  const productName = await box(page, ".brand > span:last-child");
  const packageName = await box(page, ".subject-path-segment.root");
  const productIcon = await box(page, ".brand-icon");
  const packageIcon = await box(page, ".subject-icon");
  expect(packageName.x).toBeCloseTo(productName.x, 0);
  expect(packageIcon.width).toBeCloseTo(productIcon.width, 0);
});

test("narrow layout preserves the two-line hierarchy and primary actions", async ({
  page,
}) => {
  await page.setViewportSize({ width: 760, height: 900 });
  await page.goto("/browser/workspace-titlebar.html");

  const titleActions = await box(page, ".title-actions");
  const titlebar = await box(page, ".titlebar");
  const subjectZone = await box(page, ".subject-zone");
  const namespacePicker = await box(page, ".namespace-picker");
  const typeList = await box(page, ".type-list");

  await expect(page.locator("#package-version")).toHaveCount(0);
  await expect(page.locator("#framework")).toHaveCount(0);
  await expect(page.locator("#open-search")).toBeVisible();
  await expect(page.locator("#go-home")).toHaveCount(0);
  await expect(page.locator(".subject-path-segment")).toHaveText([
    "System.Text.Json",
    "System.Text.Json.JsonSerializer",
  ]);
  await expect(page.locator(".subject-zone #share")).toBeVisible();
  await expect(page.locator("#copy-name")).toHaveCount(0);
  await expect(page.locator("#taste-btn")).toHaveCount(0);
  await expect(page.locator("#help")).toBeHidden();
  await expect(page.locator("#open-settings")).toBeHidden();
  expect(titlebar.y).toBeLessThan(subjectZone.y);
  expect(titleActions.x + titleActions.width).toBeCloseTo(760, 0);
  expect(subjectZone.x).toBe(0);
  expect(subjectZone.x + subjectZone.width).toBeCloseTo(760, 0);
  expect(namespacePicker.y + namespacePicker.height).toBeLessThanOrEqual(typeList.y);
});

test("the subject zone advertises the typed Package, Type, and Member path", async ({
  page,
}) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  await page.goto("/browser/workspace-titlebar.html?member=1");

  await expect(page.locator(".subject-path-segment")).toHaveText([
    "System.Text.Json",
    "System.Text.Json.JsonSerializer",
    "DeserializeSync",
  ]);
  await expect(page.locator(".subject-path-separator")).toHaveCount(2);
  await expect(page.locator("[data-subject-copy]")).toHaveCount(3);
  await expect(page.locator(".subject-path-segment.current")).toHaveCSS(
    "color",
    "rgb(229, 102, 63)");
  const packageText = await page.locator(".subject-path-segment").nth(0)
    .evaluate(element => getComputedStyle(element).fontSize);
  const typeText = await page.locator(".subject-path-segment").nth(1)
    .evaluate(element => getComputedStyle(element).fontSize);
  const typeWeight = await page.locator(".subject-path-segment").nth(1)
    .evaluate(element => getComputedStyle(element).fontWeight);
  expect(Number.parseFloat(packageText)).toBeGreaterThan(
    Number.parseFloat(typeText));
  expect(Number.parseInt(typeWeight, 10)).toBeGreaterThanOrEqual(600);
  await page.locator("[data-subject-copy='1']").click();
  await expect(page.locator("body")).toHaveAttribute(
    "data-copied-subject",
    "System.Text.Json.JsonSerializer");
  const forward = await box(page, "#nav-forward");
  const search = await box(page, "#open-search");
  expect(forward.x + forward.width).toBeLessThanOrEqual(search.x);
  expect(search.x - (forward.x + forward.width)).toBeLessThanOrEqual(7);
  const zone = await box(page, ".subject-zone");
  const workspace = await box(page, ".workspace");
  expect(zone.x).toBe(0);
  expect(zone.width).toBeCloseTo(1440, 0);
  expect(zone.y + zone.height).toBeLessThanOrEqual(workspace.y);
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
  await expect(page.locator("[data-subject-copy]")).toHaveCount(0);
});
