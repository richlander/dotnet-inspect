import { expect, type Page } from "@playwright/test";

async function chooseSubject(
  page: Page,
  subject: string,
) {
  const tab = page.locator(`[data-subject-tab][data-scope="${subject}"]`);
  const trigger = page.locator("[data-navigation-trigger='subject']");
  await expect.poll(async () =>
    await tab.isVisible() || await trigger.isVisible()).toBe(true);
  if (await tab.isVisible()) {
    await tab.click();
  } else {
    await trigger.click();
    await page.locator("#subject-navigation-menu")
      .locator(`[data-scope="${subject}"]`)
      .click();
  }
  await expect(tab).toHaveAttribute("aria-selected", "true");
}

async function selectFirstExactLibrary(page: Page) {
  await chooseSubject(page, "library");
  const row = page.locator(
    '.library-subject-list [data-library-subject]:not([data-library-subject="all"])',
  ).first();
  const navigationToggle = page.getByRole(
    "button",
    { name: "Libraries", exact: true });
  await expect.poll(async () =>
    await row.isVisible() || await navigationToggle.isVisible()).toBe(true);
  if (!await row.isVisible()) await navigationToggle.click();
  await row.click();
  await expect(row).toHaveAttribute("aria-selected", "true");
}

export {
  chooseSubject,
  selectFirstExactLibrary,
};
