import { expect, test } from "@playwright/test";
import {
  chooseInspector,
  chooseSubject,
  core,
  installFacades,
  root,
  selectLibrary,
} from "./library-hierarchy.support.ts";

test.use({ viewport: { width: 900, height: 900 } });

test("ordinary Type lists load cues and filters without a separate viewer", async ({
  page,
}, testInfo) => {
  await installFacades(
    page,
    undefined,
    [],
    "ready",
    "ready",
    undefined,
    "ready",
    "ready",
    undefined,
    {
      qualifiedStructuralSalience: true,
      slowStructuralSalience: true,
    },
  );
  await page.goto(root);
  await selectLibrary(page, core.id);
  await chooseSubject(page, "type", "Type");
  await page.locator("#type-filter-summary").click();

  await page.locator("#namespace-jump").selectOption("Example");
  await expect(page.locator("html")).toHaveAttribute(
    "data-structural-salience-request-count",
    "1",
  );
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

  await page.locator("[data-type-leverage-retry]").click();
  await expect(page.locator("html")).toHaveAttribute(
    "data-structural-salience-request-count",
    "2",
  );
  await chooseSubject(page, "library", "Library");
  await chooseInspector(page, "data-library-lens", "analysis", "Analysis");
  await page.locator('[data-analysis-mode="metrics"]').click();
  await expect(page.locator(".library-metrics-surface"))
    .not.toContainText("Structural Salience");
});
