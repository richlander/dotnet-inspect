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

test("aggregate Type lists load icon-only cues automatically", async ({
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
      slowStructuralSalience: true,
    },
  );
  await page.goto(root);
  await chooseSubject(page, "type", "Type");
  await expect(page.locator(".type-row .item-achievement-rail")).toHaveCount(2);
  await expect(page.locator("[data-type-leverage-filter]")).toHaveCount(0);
  await expect(page.getByRole("button", {
    name: /structural salience/i,
  })).toHaveCount(0);

  await page.locator("#type-filter-summary").click();
  await page.locator("#namespace-jump").selectOption("Example");
  await expect(page.locator("html")).toHaveAttribute(
    "data-structural-salience-request-count",
    "2",
  );
  await expect(page.locator(".type-row.surface-sea-level")).toHaveCount(1);
  await expect(page.locator(".type-row.surface-mountain-peak")).toHaveCount(1);
  await expect(page.locator(".type-row.implementation-sea-level"))
    .toHaveCount(2);
  await expect(page.locator(
    ".type-row.surface-sea-level.implementation-sea-level",
  )).toHaveCount(1);
  await expect(page.locator(
    ".type-row.surface-mountain-peak.implementation-sea-level",
  )).toHaveCount(1);
  expect(await page.locator(
    ".item-achievement-glyph.surface-sea-level",
  ).first()
    .evaluate(element => getComputedStyle(element).maskImage)).not.toBe("none");
  await expect(page.locator(".metadata-warning")).toHaveCount(0);
  await page.locator("#clear-filter").click();
  await expect(page.locator(".item-achievement-glyph.surface-sea-level"))
    .toHaveCount(1);
  await expect(page.locator(
    ".item-achievement-glyph.surface-mountain-peak",
  )).toHaveCount(1);
  await expect(page.locator(
    ".item-achievement-glyph.implementation-sea-level",
  )).toHaveCount(2);
  await expect(page.locator("html")).toHaveAttribute(
    "data-structural-salience-request-count",
    "2",
  );
  await page.locator(".type-browser").screenshot({
    path: testInfo.outputPath("type-browser-salience.png"),
  });

  await selectLibrary(page, core.id);
  await chooseSubject(page, "library", "Library");
  await chooseInspector(page, "data-library-lens", "analysis", "Analysis");
  await page.locator('[data-analysis-mode="complexity"]').click();
  await expect(page.locator(".library-complexity-surface"))
    .not.toContainText("Structural Salience");
  await page.locator('[data-analysis-mode="relationships"]').click();
  await expect(page.locator(".library-relationships-surface"))
    .not.toContainText("Structural Salience");
});

test("qualified structural salience remains visible and retryable", async ({
  page,
}) => {
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
    },
  );
  await page.goto(root);
  await chooseSubject(page, "type", "Type");
  await expect(page.locator("html")).toHaveAttribute(
    "data-structural-salience-request-count",
    "2",
  );
  await expect(page.locator(".metadata-warning").filter({
    hasText: "Structural salience has qualified evidence",
  })).toHaveCount(1);
  await expect(page.locator("#type-list .type-row").first())
    .toBeInViewport({ ratio: 1 });
  await page.locator("[data-type-leverage-retry]").click();
  await expect(page.locator("html")).toHaveAttribute(
    "data-structural-salience-request-count",
    "4",
  );
});
