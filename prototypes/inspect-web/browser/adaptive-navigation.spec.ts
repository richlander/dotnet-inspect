import { expect, test, type Page } from "@playwright/test";

async function forms(page: Page) {
  const navigation = page.locator("[data-scope-bar]");
  return {
    subject: await navigation.getAttribute("data-subject-form"),
    inspector: await navigation.getAttribute("data-inspector-form"),
  };
}

async function expectCurrentInspector(
  page: Page,
  label: string,
) {
  const trigger = page.locator("[data-navigation-trigger='inspector']");
  if (await trigger.isVisible()) {
    await expect(trigger).toHaveAccessibleName(label);
    return;
  }
  await expect(page.getByRole("tab", { name: label, exact: true }))
    .toHaveAttribute("aria-selected", "true");
}

async function expectCurrentSubject(page: Page, label: string) {
  const trigger = page.locator("[data-navigation-trigger='subject']");
  if (await trigger.isVisible()) {
    await expect(trigger).toHaveAccessibleName(label);
    return;
  }
  await expect(page.getByRole("tab", { name: label, exact: true }))
    .toHaveAttribute("aria-selected", "true");
}

test("adaptive navigation preserves complete inventories and manual activation", async ({
  page,
}) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  await page.goto("/browser/workspace-titlebar.html?member=1");

  await expect.poll(() => forms(page)).toEqual({
    subject: "tabs",
    inspector: "tabs",
  });
  await expect(page.locator("[data-subject-tab]")).toHaveCount(4);
  await expect(page.locator("[data-inspector-tab]")).toHaveCount(5);
  await expect(page.locator("[data-slide-strip-allocation]")).toHaveCount(0);
  await expect(page.locator("[data-slide-strip]")).toHaveCount(0);
  await expect(page.locator("[data-slide-strip-representation]")).toHaveCount(0);

  const member = page.getByRole("tab", { name: "Member" });
  const type = page.getByRole("tab", { name: "Type" });
  await member.focus();
  await page.keyboard.press("ArrowLeft");
  await expect(type).toBeFocused();
  await expect(type).toHaveAttribute("aria-selected", "false");
  await expect(member).toHaveAttribute("aria-selected", "true");
  await page.keyboard.press("Enter");
  await expect(type).toHaveAttribute("aria-selected", "true");
  await expect(type).toBeFocused();

  const api = page.getByRole("tab", { name: "API" });
  const metadata = page.getByRole("tab", { name: "Metadata" });
  await api.focus();
  await page.keyboard.press("ArrowRight");
  await expect(metadata).toBeFocused();
  await expect(metadata).toHaveAttribute("aria-selected", "false");
  await expect(api).toHaveAttribute("aria-selected", "true");
  await page.keyboard.press("Enter");
  await expect(metadata).toHaveAttribute("aria-selected", "true");
  await expect(metadata).toBeFocused();
});

test("adaptive navigation selects deterministic mixed and dual Chooser forms", async ({
  page,
}) => {
  await page.goto("/browser/workspace-titlebar.html?member=1");

  const observed = new Set<string>();
  for (const width of [1000, 900, 800, 700, 600, 500, 390]) {
    await page.setViewportSize({ width, height: 900 });
    await expect.poll(async () => {
      const pair = await forms(page);
      observed.add(`${pair.subject}/${pair.inspector}`);
      return pair.subject !== null && pair.inspector !== null;
    }).toBe(true);
  }

  expect(observed).toContain("tabs/tabs");
  expect(
    observed.has("tabs/chooser") || observed.has("chooser/tabs"),
  ).toBe(true);
  expect(observed).toContain("chooser/chooser");
  await expect(page.locator("[data-navigation-trigger='subject']")).toBeVisible();
  await expect(page.locator("[data-navigation-trigger='inspector']")).toBeVisible();
  for (const tabs of await page.locator("[data-navigation-tabs]").all()) {
    await expect(tabs).toBeHidden();
  }
  expect(await page.locator(
    "[data-scope-bar] [data-navigation-item='tab']",
  ).evaluateAll(items => items.filter(item =>
    item.checkVisibility()).length)).toBe(0);

  const horizontalOverflow = await page.evaluate(() =>
    document.documentElement.scrollWidth - document.documentElement.clientWidth);
  expect(horizontalOverflow).toBeLessThanOrEqual(0);
  const subjectLabel = page.locator(
    "[data-navigation-trigger='subject'] > span").first();
  expect(await subjectLabel.evaluate(element =>
    element.scrollWidth <= element.clientWidth)).toBe(true);
});

test("application scopes yield before complete navigation falls back", async ({
  page,
}) => {
  await page.goto("/browser/workspace-titlebar.html?member=1");

  for (const width of [1161, 1280]) {
    await page.setViewportSize({ width, height: 900 });
    await expect.poll(() => forms(page)).toEqual({
      subject: "tabs",
      inspector: "tabs",
    });
    await expect(page.locator(".application-scope-region")).toBeHidden();
  }

  await page.setViewportSize({ width: 1440, height: 900 });
  await expect.poll(() => forms(page)).toEqual({
    subject: "tabs",
    inspector: "tabs",
  });
  await expect(page.locator(".application-scope-region")).toBeVisible();
});

test("application scopes yield before a subject-only group falls back", async ({
  page,
}) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  await page.goto("/browser/workspace-titlebar.html?workspace=1");
  await expect(page.locator(".application-scope-region")).toBeVisible();
  await expect.poll(() => forms(page)).toEqual({
    subject: "tabs",
    inspector: null,
  });

  await page.setViewportSize({ width: 260, height: 900 });
  await expect(page.locator(".application-scope-region")).toBeHidden();
  await expect.poll(() => forms(page)).toEqual({
    subject: "chooser",
    inspector: null,
  });
  await expect(page.locator("[data-navigation-tabs]")).toBeHidden();
  await expect(page.locator("[data-navigation-trigger='subject']")).toBeVisible();
});

test("Chooser browsing, cancellation, and commit stay explicit", async ({
  page,
}) => {
  await page.setViewportSize({ width: 390, height: 900 });
  await page.goto("/browser/workspace-titlebar.html?member=1");

  const inspectorTrigger =
    page.locator("[data-navigation-trigger='inspector']");
  await expect(inspectorTrigger).toBeVisible();
  await expect(inspectorTrigger).toHaveAccessibleName("Overview");
  await inspectorTrigger.click();
  const menu = page.getByRole("menu", { name: "Member lenses" });
  await expect(menu).toBeVisible();
  const overview = menu.getByRole("menuitemradio", { name: "Overview" });
  const callGraph = menu.getByRole("menuitemradio", { name: "Call graph" });
  await expect(overview).toHaveAttribute("aria-checked", "true");
  await expect(overview).toBeFocused();
  await page.keyboard.press("ArrowDown");
  await expect(callGraph).toBeFocused();
  await expect(overview).toHaveAttribute("aria-checked", "true");
  await page.keyboard.press("Escape");
  await expect(menu).toBeHidden();
  await expect(inspectorTrigger).toBeFocused();
  await expect(inspectorTrigger).toHaveAccessibleName("Overview");

  await inspectorTrigger.click();
  await expect(overview).toBeFocused();
  await menu.getByRole("menuitemradio", { name: "Facts" }).click();
  await expect(inspectorTrigger).toHaveAccessibleName("Facts");
  await expect(page.locator("[data-member-section='facts'][aria-checked='true']"))
    .toHaveCount(1);

  const subjectTrigger = page.locator("[data-navigation-trigger='subject']");
  await subjectTrigger.click();
  const subjectMenu = page.getByRole("menu", { name: "Subjects" });
  await subjectMenu.getByRole("menuitemradio", { name: "Type" }).click();
  await expectCurrentSubject(page, "Type");
  await expectCurrentInspector(page, "API");
});

test("open Chooser focus survives same-inventory replacement and Tab dismisses", async ({
  page,
}) => {
  await page.setViewportSize({ width: 390, height: 900 });
  await page.goto("/browser/workspace-titlebar.html?member=1");

  const trigger = page.locator("[data-navigation-trigger='inspector']");
  await trigger.click();
  const menu = page.getByRole("menu", { name: "Member lenses" });
  const callGraph = menu.getByRole("menuitemradio", { name: "Call graph" });
  await page.keyboard.press("ArrowDown");
  await expect(callGraph).toBeFocused();
  await page.evaluate(() => window.rerenderScopeBarProbe());
  await expect(menu).toBeVisible();
  await expect(callGraph).toBeFocused();
  await expect(page.locator("[data-inspector-tab][tabindex='0']"))
    .toHaveCount(1);
  await page.keyboard.press("Tab");
  await expect(menu).toBeHidden();
  await expect(trigger).not.toBeFocused();
  await expect(page.locator("#application-menu-button")).toBeFocused();
});

test("Tab dismissal resolves document order before an open Chooser unpins", async ({
  page,
}) => {
  await page.setViewportSize({ width: 390, height: 900 });
  await page.goto("/browser/workspace-titlebar.html?member=1");

  const trigger = page.locator("[data-navigation-trigger='inspector']");
  await trigger.click();
  await page.setViewportSize({ width: 1100, height: 900 });
  await expect(page.getByRole("menu", { name: "Member lenses" })).toBeVisible();
  await page.keyboard.press("Tab");

  await expect(page.getByRole("menu", { name: "Member lenses" })).toBeHidden();
  await expect(page.locator("#application-menu-button")).toBeFocused();
  await expect.poll(() => forms(page)).toEqual({
    subject: "tabs",
    inspector: "tabs",
  });
});

test("outside pointer dismissal closes and unpins a Chooser", async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 900 });
  await page.goto("/browser/workspace-titlebar.html?member=1");

  const trigger = page.locator("[data-navigation-trigger='inspector']");
  const menu = page.getByRole("menu", { name: "Member lenses" });
  await trigger.click();
  await expect(menu).toBeVisible();
  await page.locator("#application-menu-button").click();
  await expect(menu).toBeHidden();
  await expect(trigger).toHaveAttribute("aria-expanded", "false");

  await page.setViewportSize({ width: 1440, height: 900 });
  await expect.poll(() => forms(page)).toEqual({
    subject: "tabs",
    inspector: "tabs",
  });
});

test("dual Choosers share constrained width and keep full accessible labels", async ({
  page,
}) => {
  await page.setViewportSize({ width: 220, height: 900 });
  await page.goto("/browser/workspace-titlebar.html?member=1");

  await expect.poll(() => forms(page)).toEqual({
    subject: "chooser",
    inspector: "chooser",
  });
  const subject = page.locator("[data-navigation-group='subject']");
  const inspector = page.locator("[data-navigation-group='inspector']");
  const subjectBox = await subject.boundingBox();
  const inspectorBox = await inspector.boundingBox();
  expect(subjectBox).not.toBeNull();
  expect(inspectorBox).not.toBeNull();
  expect(Math.abs(subjectBox!.width - inspectorBox!.width))
    .toBeLessThanOrEqual(1);

  const trigger = page.locator("[data-navigation-trigger='inspector']");
  await trigger.click();
  const menu = page.getByRole("menu", { name: "Member lenses" });
  const menuBox = await menu.boundingBox();
  expect(menuBox).not.toBeNull();
  expect(menuBox!.x).toBeGreaterThanOrEqual(8);
  expect(menuBox!.x + menuBox!.width).toBeLessThanOrEqual(212);
  await menu
    .getByRole("menuitemradio", { name: "Annotated source" })
    .click();
  await expect(trigger).toHaveAccessibleName("Annotated source");
  await expect(trigger).toHaveAttribute("title", "Annotated source");
  expect(await trigger.locator("span").first().evaluate(element =>
    element.scrollWidth > element.clientWidth)).toBe(true);
});

test("responsive replacement transfers focus and preserves an open Chooser", async ({
  page,
}) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  await page.goto("/browser/workspace-titlebar.html?member=1");

  const member = page.getByRole("tab", { name: "Member" });
  await member.focus();
  await page.setViewportSize({ width: 390, height: 900 });
  const subjectTrigger = page.locator("[data-navigation-trigger='subject']");
  await expect(subjectTrigger).toBeFocused();

  await subjectTrigger.click();
  const subjectMenu = page.getByRole("menu", { name: "Subjects" });
  await expect(subjectMenu).toBeVisible();
  await expect(
    subjectMenu.getByRole("menuitemradio", { name: "Member" }),
  ).toBeFocused();
  await expect(page.locator("[data-subject-tab][tabindex='0']"))
    .toHaveCount(1);
  await page.setViewportSize({ width: 1440, height: 900 });
  await expect(subjectMenu).toBeVisible();
  await expect(subjectTrigger).toBeVisible();
  await page.keyboard.press("Escape");
  await expect(subjectMenu).toBeHidden();
  await expect(member).toBeFocused();
  await expect(member).toHaveAttribute("tabindex", "0");
  await expect(page.getByRole("tab", { name: "Type", exact: true }))
    .toHaveAttribute("tabindex", "-1");
});

test("committed subject keeps focus when activation changes its form", async ({
  page,
}) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  await page.goto("/browser/workspace-titlebar.html?member=1");

  const type = page.getByRole("tab", { name: "Type", exact: true });
  await type.focus();
  await page.keyboard.press("Enter");
  await expect(type).toHaveAttribute("aria-selected", "true");

  await page.setViewportSize({ width: 900, height: 900 });
  const library = page.getByRole("tab", { name: "Library", exact: true });
  await library.focus();
  await page.keyboard.press("Enter");

  const trigger = page.locator("[data-navigation-trigger='subject']");
  await expect(trigger).toBeFocused();
  await expect(trigger).toHaveAccessibleName("Library");
});

test("inspector content keeps an installed accessible-name owner", async ({
  page,
}) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  await page.goto("/browser/workspace-titlebar.html?member=1&source=1");

  const panel = page.locator("#inspector-panel");
  await expect(panel).toHaveAttribute("role", "tabpanel");
  await expect(panel).toHaveAttribute("aria-labelledby", "active-inspector-tab");
  await page.setViewportSize({ width: 390, height: 900 });
  await expect(panel).toHaveAttribute("role", "region");
  await expect(panel).toHaveAttribute(
    "aria-labelledby",
    "active-inspector-chooser");
  const trigger = page.locator("[data-navigation-trigger='inspector']");
  await trigger.click();
  await page.keyboard.press("Escape");
  await expect(panel).toHaveAttribute(
    "aria-labelledby",
    "active-inspector-chooser");
  await page.setViewportSize({ width: 1440, height: 900 });
  await expect(panel).toHaveAttribute("role", "tabpanel");
  await expect(panel).toHaveAttribute("aria-labelledby", "active-inspector-tab");
});

test("Workspace with no committed subject uses an honest focus origin", async ({
  page,
}) => {
  await page.setViewportSize({ width: 220, height: 900 });
  await page.goto("/browser/workspace-titlebar.html?workspace=1");

  const trigger = page.locator("[data-navigation-trigger='subject']");
  await expect(trigger).toBeVisible();
  await expect(trigger).toHaveAccessibleName("Choose subject");
  await trigger.click();
  const menu = page.getByRole("menu", { name: "Subjects" });
  await expect(menu.getByRole("menuitemradio", { name: "Package" }))
    .toHaveAttribute("aria-checked", "false");
  await expect(menu.getByRole("menuitemradio", { name: "Package" }))
    .toBeFocused();
  await page.keyboard.press("Escape");
  await page.setViewportSize({ width: 1440, height: 900 });
  const packageTab = page.getByRole("tab", { name: "Package" });
  await expect(packageTab).toBeFocused();
  await expect(packageTab).toHaveAttribute("aria-selected", "false");
  await expect(page.locator("#subject-panel")).toHaveAttribute(
    "aria-labelledby",
    "application-scope-workspace");
});
