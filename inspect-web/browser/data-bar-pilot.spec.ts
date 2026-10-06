import { expect, test } from "@playwright/test";
import { chooseInspector, core, installFacades, root, selectLibrary } from "./library-hierarchy.support.ts";

for (const width of [1440, 390]) {
  for (const outcome of ["ready", "empty"] as const) {
    test(`Library API comparison owns the compact data bar (${outcome}) at ${width}px`, async ({ page }) => {
      await page.setViewportSize({ width, height: 900 });
      await installFacades(page, undefined, [], "ready", "ready", undefined, "ready", "ready", undefined, { libraryApiDiffPilot: outcome });
      await page.goto(root);
      await selectLibrary(page, core.id);
      await chooseInspector(page, "data-library-lens", "compare", "Compare");
      const bar = page.locator(".data-bar");
      await expect(bar).toHaveAttribute("aria-label", "Inspection result");
      await expect(bar).toContainText(`${outcome === "empty" ? 0 : 1} changed Types`);
      await expect(bar).toContainText("0 breaking");
      await expect(bar).toContainText("Example.Core · Public API comparison");
      await expect(page.locator(".library-api-diff-metrics")).toHaveCount(0);
      await expect(page.locator(".library-api-diff-types > li")).toHaveCount(outcome === "empty" ? 0 : 1);
      expect((await bar.boundingBox())?.height).toBe(30);
      await chooseInspector(page, "data-library-lens", "overview", "Overview");
      await expect(bar).toHaveAttribute("aria-label", "Product information");
      await chooseInspector(page, "data-library-lens", "compare", "Compare");
      await expect(bar).toHaveAttribute("aria-label", "Inspection result");
    });
  }
}
