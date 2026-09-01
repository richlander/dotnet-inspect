import { expect, test, type Page } from "@playwright/test";

async function box(page: Page, selector: string) {
  const value = await page.locator(selector).boundingBox();
  expect(value).not.toBeNull();
  return value!;
}

test("the title bar contains the inspected target without tab-like workspace identity", async ({
  page,
}) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  await page.goto("/browser/workspace-titlebar.html?package=1");

  const titleNavigation = await box(page, ".title-navigation");
  const search = await box(page, "#open-search");
  const back = await box(page, "#nav-back");

  expect(titleNavigation.x + titleNavigation.width).toBeCloseTo(1440, 0);
  expect(search.x + search.width).toBeLessThanOrEqual(back.x);
  await expect(page.locator(".titlebar .inspected-target")).toBeVisible();
  await expect(page.locator(".titlebar .subject-path-segment.root"))
    .toHaveText("System.Text.Json");
  await expect(page.locator(".titlebar #open-search")).toBeVisible();
  await expect(page.locator(".titlebar .nav-history")).toBeVisible();
  await expect(page.locator(".titlebar #share")).toHaveCount(0);
  await expect(page.locator(".titlebar #open-settings")).toHaveCount(0);
  await expect(page.locator(".titlebar #help")).toHaveCount(0);
  await expect(page.locator(".subject-zone #share")).toBeVisible();
  await expect(page.locator(".subject-zone #open-settings")).toBeVisible();
  await expect(page.locator(".subject-zone #help")).toBeVisible();
  await expect(page.locator(".shell-actions > button").last())
    .toHaveAttribute("id", "help");
  await expect(page.locator(".workspace-title")).toHaveCount(0);
  await expect(page.locator(".titlebar")).not.toContainText("0:");
  await expect(page.locator(".titlebar")).not.toContainText("Platform");
  await expect(page.locator(".workspace-window")).toHaveCount(0);
  await expect(page.locator(".brand-icon img")).toHaveAttribute(
    "src",
    "/assets/dotnet-inspect-bot.png");
});

test("the inspected target precedes title navigation and package selectors stay in content", async ({
  page,
}) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  await page.goto("/browser/workspace-titlebar.html?package=1");

  const brand = await box(page, ".brand");
  const target = await box(page, ".inspected-target");
  const lensbar = await box(page, ".lensbar");
  const subjectZone = await box(page, ".subject-zone");

  expect(target.x).toBeGreaterThanOrEqual(brand.x + brand.width);
  expect(target.y).toBeCloseTo(brand.y, 0);
  expect(lensbar.y).toBeCloseTo(subjectZone.y, 0);
  await expect(page.locator(".titlebar .lensbar")).toHaveCount(0);
  await expect(page.locator(".subject-zone .lensbar")).toBeVisible();
  await expect(page.locator(".titlebar #package-version")).toHaveCount(0);
  await expect(page.locator(".titlebar #framework")).toHaveCount(0);
  await expect(page.locator(".detail-scroll #package-version")).toBeVisible();
  await expect(page.locator(".detail-scroll #framework")).toBeVisible();
  await expect(page.locator("#go-home")).toHaveCount(0);

  const packageIcon = await box(page, ".subject-icon");
  expect(packageIcon.width).toBeCloseTo(20, 0);
  expect(packageIcon.height).toBeCloseTo(20, 0);
  await expect(page.locator(".subject-icon img")).toHaveAttribute(
    "src",
    /^data:image\/png;base64,/);
  await expect(page.locator(".subject-icon img")).toHaveJSProperty(
    "naturalWidth",
    456);
});

test("packages without an embedded icon use NuGet's package fallback", async ({
  page,
}) => {
  await page.goto("/browser/workspace-titlebar.html?package=1&fallback=1");

  await expect(page.locator(".subject-icon img")).toHaveAttribute(
    "src",
    "https://nuget.org/Content/gallery/img/default-package-icon-256x256.png");
  await expect(page.locator(".subject-icon")).not.toContainText("⬡");
});

test("right-side actions yield from labels to arrows to nothing", async ({
  page,
}) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  await page.goto("/browser/workspace-titlebar.html?member=1");
  await expect(page.locator(".title-search-label-full")).toBeVisible();
  await expect(page.locator(".title-search-label-compact")).toBeHidden();

  await page.goto("/browser/workspace-titlebar.html?member=1&long=1");
  await expect(page.locator("#open-search")).toBeHidden();
  await expect(page.locator(".title-navigation .nav-history")).toBeVisible();

  await page.setViewportSize({ width: 760, height: 900 });
  await page.goto("/browser/workspace-titlebar.html?member=1");

  const titlebar = await box(page, ".titlebar");
  const subjectZone = await box(page, ".subject-zone");
  const namespacePicker = await box(page, ".namespace-picker");
  const typeList = await box(page, ".type-list");

  await expect(page.locator("#package-version")).toHaveCount(0);
  await expect(page.locator("#framework")).toHaveCount(0);
  await expect(page.locator("#open-search")).toBeVisible();
  await expect(page.locator(".title-search-label-full")).toBeHidden();
  await expect(page.locator(".title-search-label-compact"))
    .toHaveText("Search");
  await expect(page.locator(".title-search-label-compact")).toBeVisible();
  await expect(page.locator(".title-navigation .nav-history")).toBeVisible();
  await expect(page.locator("#go-home")).toHaveCount(0);
  await expect(page.locator(".subject-path-segment")).toHaveText([
    "System.Text.Json",
    "System.Text.Json.JsonSerializer",
    "DeserializeSync",
  ]);
  await expect(page.locator(".titlebar .subject-path")).toBeVisible();
  await expect(page.locator(".subject-zone .subject-path")).toHaveCount(0);
  await expect(page.locator(".subject-zone .scope-switch")).toBeVisible();
  await expect(page.locator(".subject-zone #share")).toBeHidden();
  await expect(page.locator(".subject-zone #open-settings")).toBeHidden();
  await expect(page.locator(".subject-zone #help")).toBeVisible();
  await expect(page.locator("#copy-name")).toHaveCount(0);
  await expect(page.locator("#taste-btn")).toHaveCount(0);
  expect(titlebar.y).toBeLessThan(subjectZone.y);
  expect(subjectZone.x).toBe(0);
  expect(subjectZone.x + subjectZone.width).toBeCloseTo(760, 0);

  await page.setViewportSize({ width: 650, height: 900 });
  await expect(page.locator("#open-search")).toBeHidden();
  await expect(page.locator(".title-navigation .nav-history")).toBeVisible();
  await expect(page.locator("#share")).toBeHidden();
  await expect(page.locator("#open-settings")).toBeHidden();
  await expect(page.locator("#help")).toBeHidden();

  await page.setViewportSize({ width: 560, height: 900 });
  await expect(page.locator(".title-navigation .nav-history")).toBeHidden();
  await expect(page.locator("#open-settings")).toBeHidden();
  await expect(page.locator("#help")).toBeHidden();

  await page.setViewportSize({ width: 480, height: 900 });
  await expect(page.locator("#help")).toBeHidden();
  const horizontalOverflow = await page.evaluate(() =>
    document.documentElement.scrollWidth - document.documentElement.clientWidth);
  expect(horizontalOverflow).toBeLessThanOrEqual(0);
  expect(namespacePicker.y + namespacePicker.height).toBeLessThanOrEqual(typeList.y);
});

test("the title line advertises the typed Package, Type, and Member path", async ({
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
  await expect(page.locator(".titlebar .subject-path")).toBeVisible();
  await expect(page.locator(".subject-zone .scope-switch")).toBeVisible();
  await expect(page.locator(".subject-zone .lens")).toHaveCount(5);
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
  const search = await box(page, "#open-search");
  const back = await box(page, "#nav-back");
  expect(search.x + search.width).toBeLessThanOrEqual(back.x);
  expect(back.x - (search.x + search.width)).toBeLessThanOrEqual(7);
  const zone = await box(page, ".subject-zone");
  const target = await box(page, ".inspected-target");
  const workspace = await box(page, ".workspace");
  expect(target.y).toBeLessThan(zone.y);
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
