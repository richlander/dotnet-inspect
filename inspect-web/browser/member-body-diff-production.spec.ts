import { writeFile } from "node:fs/promises";
import { expect, test } from "@playwright/test";
import { chooseSubject, selectFirstExactLibrary } from "./library-subject-actions.ts";

const site = process.env.INSPECT_WEB_SOURCE_DIFF_URL;

test("Member Body opens an added property across the former Worker limits", async ({ page }) => {
  test.skip(!site, "Set INSPECT_WEB_SOURCE_DIFF_URL to the published Wasm site.");
  test.setTimeout(300_000);
  await page.addInitScript(() => {
    Object.defineProperty(navigator, "clipboard", { value: { write: async (items: ClipboardItem[]) => {
      (window as Window & { copiedShare?: string }).copiedShare = await (await items[0]!.getType("text/plain")).text();
    } } });
  });
  await page.goto(`${new URL(site!).origin}/?package=System.Text.Json&version=10.0.12&framework=netstandard2.0#pkg`);
  await page.locator('[data-package-lens="compare"][role="tab"]').click({ timeout: 120_000 });
  await page.locator("#package-diff-target").selectOption("exact:9.0.20", { timeout: 120_000 });
  await chooseSubject(page, "library");
  await selectFirstExactLibrary(page);
  await page.locator('[data-navigation-id="compare"][role="tab"]').click();
  await page.locator("#compare-diff-content").selectOption("member-body");
  await page.locator('[data-member-body-type="System.Text.Json.JsonDocumentOptions"]').click({ timeout: 180_000 });
  await page.locator("[data-member-body-member]").filter({ hasText: "AllowDuplicateProperties" }).first().click();
  await expect(page.locator(".member-body-reader .source-diff-viewer")).toBeVisible({ timeout: 120_000 });
  await expect(page.locator(".member-body-reader")).not.toContainText("Not present on this side");
  await expect(page.locator(".source-diff-viewer-summary")).toContainText("2 added");
  await expect(page.locator("#inspector-panel h1, #inspector-panel h2")).toHaveCount(0);
  await expect(page.locator(".targetbar #compare-diff-content")).toHaveValue("member-body");
  await expect(page.locator(".targetbar [data-member-body-explore]")).toBeVisible();
  const panel = await page.locator("#inspector-panel").boundingBox();
  const reader = await page.locator(".member-body-reader").boundingBox();
  expect(reader!.x).toBe(panel!.x);
  expect(reader!.width).toBe(panel!.width);
  expect(reader!.y + reader!.height).toBe(panel!.y + panel!.height);
  const explore = await page.locator("#member-body-explore").boundingBox();
  const controls = await page.locator(".compare-controls-row").boundingBox();
  expect(controls!.y).toBeGreaterThanOrEqual(explore!.y + explore!.height);
  await expect(page.locator(".member-body-reader")).toContainText("AllowDuplicateProperties");
  await expect(page.getByRole("button", { name: "Show diff", exact: true })).toHaveCount(0);
  await page.screenshot({ path: test.info().outputPath("added-property-inline.png") });
  await page.locator('[data-member-body-medium="Il"]').click();
  await expect(page.locator('.targetbar [data-member-body-medium="Il"]')).toBeFocused();
  await expect(page.locator(".member-body-reader .source-diff-viewer")).toContainText("IL_");
  await page.locator("#application-menu-button").click();
  await page.locator('[data-application-action="share"]').click();
  await expect.poll(() => page.evaluate(() => (window as Window & { copiedShare?: string }).copiedShare ?? "")).not.toBe("");
  const shared = await page.evaluate(() => (window as Window & { copiedShare?: string }).copiedShare!);
  await writeFile(test.info().outputPath("shared-packet.txt"), new URL(shared).searchParams.get("w")!);
  const reopened = await page.context().newPage();
  await reopened.goto(shared);
  await expect(reopened.locator(".member-body-reader .source-diff-viewer")).toContainText("IL_", { timeout: 180_000 });
  await expect(reopened.locator(".compare-target-value")).toHaveText("9.0.20 → 10.0.12");
  await expect(reopened.locator("#compare-diff-content")).toHaveValue("member-body");
  await expect(reopened.locator(".inspected-target")).toContainText("AllowDuplicateProperties");
  await expect(reopened.locator('[data-member-body-medium="Il"]')).toHaveAttribute("aria-pressed", "true");
  await reopened.locator('[data-member-body-medium="CSharp"]').click();
  await expect(reopened.locator(".member-body-reader")).toContainText("AllowDuplicateProperties");
  await reopened.screenshot({ path: test.info().outputPath("shared-added-property.png") });
  await reopened.close();
});

test("Member Body opens inline and retains the document through media and Explore", async ({ page }) => {
  test.skip(!site, "Set INSPECT_WEB_SOURCE_DIFF_URL to the published Wasm site.");
  test.setTimeout(300_000);
  await page.goto(`${new URL(site!).origin}/?package=System.Text.Json&version=11.0.0-preview.7.26381.103&framework=net10.0#pkg`);
  await page.locator('[data-package-lens="compare"][role="tab"]').click({ timeout: 120_000 });
  await page.locator("#package-diff-target").selectOption("exact:11.0.0-preview.6.26359.118", { timeout: 120_000 });
  await chooseSubject(page, "library");
  await selectFirstExactLibrary(page);
  await page.locator('[data-navigation-id="compare"][role="tab"]').click();
  await page.locator("#compare-diff-content").selectOption("member-body");
  await page.locator('[data-member-body-type="System.Text.Json.JsonSerializerOptions"]').click({ timeout: 180_000 });
  const constructor = page.locator("[data-member-body-member]").filter({ hasText: /#ctor\(System.Text.Json.JsonSerializerOptions\)/ });
  await constructor.click();
  await expect(page.locator(".member-body-reader .source-diff-viewer")).toBeVisible({ timeout: 120_000 });
  await expect(page.locator(".member-body-reader")).toContainText("_inferClosedTypePolymorphism");
  await expect(page.getByRole("button", { name: "Show diff", exact: true })).toHaveCount(0);
  await page.locator('[data-member-body-medium="Il"]').click();
  await expect(page.locator(".member-body-reader .source-diff-viewer")).toContainText("IL_");
  await page.locator("[data-member-body-explore]").click();
  await expect(page.locator(".member-body-explore")).toBeVisible();
  await expect(page.locator(".member-body-explore .source-diff-viewer")).toContainText("IL_");
  await page.locator("[data-member-body-close]").click();
  await expect(page.locator(".member-body-reader .source-diff-viewer")).toContainText("IL_");
  await expect(page.locator("[data-member-body-explore]")).toBeFocused();
  await page.locator("[data-member-body-explore]").click();
  await page.keyboard.press("Escape");
  await expect(page.locator(".member-body-explore")).toHaveCount(0);
  await expect(page.locator("[data-member-body-explore]")).toBeFocused();
  await page.keyboard.press("ArrowDown");
  await expect(page.locator("#compare-diff-content")).toHaveValue("member-body");
  await page.keyboard.press("ArrowUp");
  await expect(page.locator('.targetbar [data-member-body-medium="Il"]')).toHaveAttribute("aria-pressed", "true");
  await expect(page.locator(".member-body-reader .source-diff-viewer")).toContainText("IL_");
  await page.setViewportSize({ width: 640, height: 800 });
  await page.locator('[data-source-diff-mode="side-by-side"]').click();
  await expect(page.locator(".member-body-reader .source-diff-viewer")).toHaveAttribute("data-mode", "side-by-side");
  expect(await page.locator(".compare-surface").evaluate(element => element.scrollWidth <= element.clientWidth)).toBe(true);
  for (const width of [640, 390]) {
    await page.setViewportSize({ width, height: 800 });
    await expect(page.locator(".targetbar #compare-diff-content")).toBeVisible();
    expect((await page.locator(".working-surface-actions").boundingBox())!.height).toBeLessThanOrEqual(80);
    for (const selector of ["#compare-diff-content", "#member-body-explore", "#compare-change-target", "#compare-mode-diff", "#compare-mode-clone"]) {
      const bounds = await page.locator(selector).boundingBox();
      expect(bounds!.x).toBeGreaterThanOrEqual(0);
      expect(bounds!.x + bounds!.width).toBeLessThanOrEqual(width);
    }
    const panel = (await page.locator("#inspector-panel").boundingBox())!;
    const reader = (await page.locator(".member-body-reader").boundingBox())!;
    expect(reader.x).toBe(panel.x);
    expect(reader.width).toBe(panel.width);
    expect(reader.y + reader.height).toBe(panel.y + panel.height);
    const explore = (await page.locator("#member-body-explore").boundingBox())!;
    const controls = (await page.locator(".compare-controls-row").boundingBox())!;
    expect(controls.y).toBeGreaterThanOrEqual(explore.y + explore.height);
    await page.screenshot({ path: test.info().outputPath(`inline-${width}.png`) });
  }
});
