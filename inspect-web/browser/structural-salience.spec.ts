import { expect, test } from "@playwright/test";
import {
  chooseSubject,
  core,
  installFacades,
  root,
  selectLibrary,
} from "./library-hierarchy.support.ts";

test.use({ viewport: { width: 900, height: 900 } });

test("Clear filters preserves the active structural-salience view", async ({
  page,
}) => {
  await installFacades(page);
  await page.goto(root);
  await selectLibrary(page, core.id);
  await chooseSubject(page, "type", "Type");
  await page.locator("#type-filter-summary").click();

  await page.getByRole(
    "button",
    { name: "Show structural salience", exact: true },
  ).click();
  await expect(page.getByRole(
    "button",
    { name: "Hide salience", exact: true },
  )).toBeVisible();
  await expect(page.locator(".type-leverage-cues")).toHaveCount(1);

  await page.locator('[data-namespace="Example"]').click();
  await page.locator(
    '[data-type-leverage-filter="sea-level"]',
  ).click();
  await page.locator("#clear-filter").click();

  await expect(page.getByRole(
    "button",
    { name: "Hide salience", exact: true },
  )).toBeVisible();
  await expect(page.locator(".type-leverage-cues")).toHaveCount(1);
  await expect(page.locator(
    '[data-type-leverage-filter=""]',
  )).toHaveClass(/\bactive\b/);
  await expect(page.locator("html")).toHaveAttribute(
    "data-structural-salience-index-request-count",
    "1",
  );
  await expect(page.locator("html")).toHaveAttribute(
    "data-structural-salience-shard-request-count",
    "1",
  );
});
