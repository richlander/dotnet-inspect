import { expect, test } from "@playwright/test";

test.beforeEach(async ({ page }) => {
  await page.goto("/browser/finding-interaction.html");
  await expect(page.locator("#finding-facts-title")).toBeVisible();
});

test("Facts and Annotated Source select the exact Finding instance", async ({
  page,
}) => {
  const first = page.locator('[data-finding-instance="41"]');
  const second = page.locator('[data-finding-instance="42"]');
  await expect(first.locator(".finding-main")).toHaveText(
    await second.locator(".finding-main").innerText(),
  );

  await second.click();
  await expect(page.locator("#annotated-source-modal")).toBeVisible();
  await expect(page.locator("#annotated-detail-title")).toBeFocused();
  await expect(page.locator("#annotated-inspector-1")).toBeVisible();

  await page.locator("#annotated-modal-close").click();
  await page.locator("#show-facts").click();
  await expect(second).toHaveAttribute("aria-pressed", "true");
  await expect(first).toHaveAttribute("aria-pressed", "false");

  await page.locator("#show-annotated").click();
  const annotation =
    page.locator("#annotated-chip-embedded-0-1-CSharp");
  await annotation.focus();
  await annotation.press("Enter");
  await page.locator("#show-facts").click();
  await expect(first).toHaveAttribute("aria-pressed", "true");
  await expect(second).toHaveAttribute("aria-pressed", "false");
});
