import { expect, test } from "@playwright/test";
import {
  chooseSubject,
  core,
  installFacades,
  root,
  selectLibrary,
} from "./library-hierarchy.support.ts";

test.use({ viewport: { width: 900, height: 900 } });

test("namespace projection and Clear filters preserve one salience request", async ({
  page,
}, testInfo) => {
  await installFacades(page);
  await page.goto(root.replace("#pkg", "&slow-salience=1#pkg"));
  await selectLibrary(page, core.id);
  await chooseSubject(page, "type", "Type");
  await page.locator("#type-filter-summary").click();

  await page.getByRole(
    "button",
    { name: "Show structural salience", exact: true },
  ).click();
  await page.locator('[data-namespace="Example"]').click();
  await expect(page.locator("html")).toHaveAttribute(
    "data-structural-salience-request-count",
    "1",
  );
  await expect(page.getByRole(
    "button",
    { name: "Hide salience", exact: true },
  )).toBeVisible();
  expect(await page.getByRole("img", {
    name: /sea-level Type/,
  }).getAttribute("class")).toContain("item-achievement-rail");
  await expect(page.locator(".type-row.sea-level")).toHaveCount(1);
  await expect(page.locator(".type-row.sea-level.mountain-peak"))
    .toHaveCount(0);
  expect(await page.locator(".item-achievement-glyph.sea-level").evaluate(
    element => getComputedStyle(element).maskImage,
  )).not.toBe("none");
  await expect(page.locator(".type-leverage-control"))
    .toContainText("1 namespace analyzed");

  await page.locator(
    '[data-type-leverage-filter="sea-level"]',
  ).click();
  await page.locator("#clear-filter").click();

  await expect(page.getByRole(
    "button",
    { name: "Hide salience", exact: true },
  )).toBeVisible();
  await expect(page.locator(".item-achievement-glyph")).toHaveCount(1);
  await expect(page.locator(
    '[data-type-leverage-filter=""]',
  )).toHaveClass(/\bactive\b/);
  await expect(page.locator("html")).toHaveAttribute(
    "data-structural-salience-request-count",
    "1",
  );
  await page.locator(".type-browser").screenshot({
    path: testInfo.outputPath("type-browser-salience.png"),
  });
});
