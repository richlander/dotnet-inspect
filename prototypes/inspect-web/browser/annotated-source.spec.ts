import { expect, test } from "@playwright/test";

test.beforeEach(async ({ page }) => {
  await page.goto("/browser/annotated-source.html");
  await expect(page.locator("#explore-annotated")).toBeVisible();
});

test("source copy excludes annotation and inspector chrome", async ({ page }) => {
  await page.locator("#copy-annotated").click();
  const copied = await page.locator("body").getAttribute("data-copied-source");

  expect(copied).toContain("return new object();");
  expect(copied).not.toContain("alloc.new");
  expect(copied).not.toContain("Persistent inspector");
});

test("annotation rows preserve the anchored source indentation", async ({ page }) => {
  await page.locator("#explore-annotated").click();
  const annotationRow = page.locator(".annotated-row-items").filter({
    has: page.locator("#annotated-chip-modal-0-1-CSharp"),
  });
  const anchorStart =
    await annotationRow.getAttribute("data-annotated-anchor-start");
  if (anchorStart === null) {
    throw new Error("Annotation row has no product-issued anchor");
  }
  const invocation = page.locator(
    `#annotated-source-modal [data-annotated-source-start="${anchorStart}"]`,
  );
  const annotation = page.locator("#annotated-chip-modal-0-1-CSharp");
  const invocationBox = await invocation.boundingBox();
  const annotationBox = await annotation.boundingBox();
  if (!invocationBox || !annotationBox) {
    throw new Error("Anchored annotation has no browser geometry");
  }

  expect(Math.abs(invocationBox.x - annotationBox.x)).toBeLessThan(1);
});

test("pointer hit testing prefers the product-issued invocation node", async ({ page }) => {
  await page.locator("#explore-annotated").click();
  const invocation = page.locator(
    '#annotated-source-modal .annotated-source-segment.invocation:has-text("object")',
  ).first();
  await invocation.click({ position: { x: 8, y: 8 } });

  await expect(page.locator("#annotated-node-1")).toBeFocused();
  await expect(page.locator("#annotated-node-4")).toHaveCount(0);
});

test("keyboard source activation selects the same invocation node", async ({ page }) => {
  await page.locator("#explore-annotated").click();
  const invocation = page.locator(
    '#annotated-source-modal .annotated-source-segment.invocation:has-text("object")',
  ).first();
  await invocation.focus();
  await invocation.press("Enter");

  await expect(page.locator("#annotated-node-1")).toBeFocused();
});

test("native pointer drag selects text without activating a node", async ({ page }) => {
  await page.locator("#explore-annotated").click();
  const source = page.locator(
    '#annotated-source-modal .annotated-source-segment.addressable:has-text("for")',
  ).first();
  const box = await source.boundingBox();
  if (!box) throw new Error("Addressable source has no browser geometry");

  await page.mouse.move(box.x + 3, box.y + box.height / 2);
  await page.mouse.down();
  await page.mouse.move(box.x + Math.min(box.width - 3, 55), box.y + box.height / 2, {
    steps: 8,
  });
  await page.mouse.up();

  const selected = await page.evaluate(() => window.getSelection()?.toString() ?? "");
  expect(selected.length).toBeGreaterThan(0);
  await expect(page.locator(".annotated-inspector-section").first()).toContainText(
    "Nothing selected",
  );
});

test("Escape closes detail before dismissing and restores exact focus", async ({ page }) => {
  await page.locator("#explore-annotated").click();
  await page.locator("#annotated-inspector-0").click();
  await expect(page.locator("#annotated-detail-title")).toBeFocused();

  await page.keyboard.press("Escape");
  await expect(page.locator("#annotated-detail-title")).toHaveCount(0);
  await expect(page.locator("#annotated-inspector-0")).toBeFocused();

  await page.keyboard.press("Escape");
  await expect(page.locator("#annotated-source-modal")).toHaveCount(0);
  await expect(page.locator("#explore-annotated")).toBeFocused();
});

test("pointer Close dismisses detail and modal together", async ({ page }) => {
  await page.locator("#explore-annotated").click();
  await page.locator("#annotated-inspector-0").click();
  await page.locator("#annotated-modal-close").click();

  await expect(page.locator("#annotated-source-modal")).toHaveCount(0);
  await expect(page.locator("#explore-annotated")).toBeFocused();
});

test("backdrop dismissal returns focus to Explore", async ({ page }) => {
  await page.locator("#explore-annotated").click();
  await page.locator("#annotated-source-backdrop").click({
    position: { x: 2, y: 2 },
  });

  await expect(page.locator("#annotated-source-modal")).toHaveCount(0);
  await expect(page.locator("#explore-annotated")).toBeFocused();
});

test("modal makes the background inert and traps forward and reverse Tab", async ({ page }) => {
  await page.locator("#explore-annotated").click();
  await expect(page.locator("#harness-background")).toHaveAttribute("inert", "");

  const focusable = page.locator(
    '#annotated-source-modal button:not([disabled]), '
      + '#annotated-source-modal [href], '
      + '#annotated-source-modal input:not([disabled]), '
      + '#annotated-source-modal select:not([disabled]), '
      + '#annotated-source-modal textarea:not([disabled]), '
      + '#annotated-source-modal [tabindex]:not([tabindex="-1"])',
  );
  const first = focusable.first();
  const last = focusable.last();

  await last.focus();
  await page.keyboard.press("Tab");
  await expect(first).toBeFocused();

  await first.focus();
  await page.keyboard.press("Shift+Tab");
  await expect(last).toBeFocused();
});
