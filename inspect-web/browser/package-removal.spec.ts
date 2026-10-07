import { expect, test } from "@playwright/test";

for (const mode of ["", "?modal=1", "?modal=1&refresh=1"]) {
  test(`package removal preserves search and forgets history: ${mode || "Home"}`, async ({ page }) => {
    await page.goto(`/browser/package-removal.html${mode}`);
    const input = page.locator("#spotlight-input");
    await input.fill("Json");
    await input.evaluate(element => {
      if (!(element instanceof HTMLInputElement)) throw new Error("Search input is missing");
      element.setSelectionRange(1, 3);
    });
    await page.getByRole("button", { name: "Remove System.Text.Json 10.0.0 net10.0 from Workspace", exact: true }).click();
    await page.evaluate(() =>
      new Promise<void>(resolve => requestAnimationFrame(() => requestAnimationFrame(() => resolve()))));
    await expect(input).toBeFocused();
    await expect(input).toHaveValue("Json");
    expect(await input.evaluate(element => element instanceof HTMLInputElement
      ? [element.selectionStart, element.selectionEnd] : null)).toEqual([1, 3]);
    await expect(page.locator('[data-sl-pkg-open="System.Text.Json"]')).toHaveCount(0);
    await expect(page.locator('[data-sl-pkg-recent="System.Text.Json"]')).toHaveCount(0);
    await expect(page.locator('[data-sl-pkg-load="System.Text.Json"]')).toHaveCount(0);
    await expect(page.locator("#notice")).toHaveText("");
    if (mode) await expect(page.getByRole("dialog")).toBeVisible();
    await input.fill("System");
    await expect(page.locator('[data-sl-pkg-load="System.Text.Json"]')).toBeVisible();
    await page.goto("/browser/package-removal.html?cold=1");
    await expect(page.locator('[data-sl-pkg-recent="System.Text.Json"]')).toHaveCount(0);
  });
}

test("recent x forgets the entry across refresh without opening it", async ({ page }) => {
  await page.goto("/browser/package-removal.html");
  await page.getByRole("button", { name: "Forget Microsoft.Extensions.Http from recent packages", exact: true }).click();
  await expect(page.locator("#spotlight-input")).toBeFocused();
  await expect(page.locator('[data-sl-pkg-open="Newtonsoft.Json"]')).toBeVisible();
  await expect(page.locator("#notice")).toHaveText("");
  await page.reload();
  await expect(page.locator('[data-sl-pkg-recent="Microsoft.Extensions.Http"]')).toHaveCount(0);
});

test("Shift Delete removes the selected package without activating or clearing the query", async ({ page }) => {
  await page.goto("/browser/package-removal.html");
  const input = page.locator("#spotlight-input");
  await input.fill("Newton");
  await input.press("Shift+Delete");
  await expect(input).toHaveValue("Newton");
  await expect(input).toBeFocused();
  await expect(page.locator('[data-sl-pkg-open="Newtonsoft.Json"]')).toHaveCount(0);
  await expect(page.locator("#notice")).toHaveText("");
});

for (const mode of ["workspace=1", "workspace=1&loading=1", "workspace=1&failed-query=1"]) {
  test(`Workspace x remains usable and preserves focus: ${mode}`, async ({ page }) => {
    await page.goto(`/browser/package-removal.html?${mode}`);
    const first = page.getByRole("button", { name: "Remove Newtonsoft.Json 13.0.4 net10.0 from Workspace", exact: true });
    await first.focus();
    await first.press("Enter");
    const next = page.getByRole("button", { name: "Remove System.Text.Json 10.0.0 net10.0 from Workspace", exact: true });
    await expect(next).toBeFocused();
    await next.press("Enter");
    await expect(page.getByRole("heading", { name: "Workspace", exact: true })).toBeFocused();
    await expect(page.getByText("No packages are loaded in this Workspace.")).toBeVisible();
    await expect(page.locator("#notice")).toHaveText("");
  });
}

test("storage failure keeps the row and shows the failure", async ({ page }) => {
  await page.goto("/browser/package-removal.html?storage-failure=1");
  const button = page.getByRole("button", { name: "Remove Newtonsoft.Json 13.0.4 net10.0 from Workspace", exact: true });
  await button.click();
  await expect(button).toBeVisible();
  await expect(page.locator("#notice")).toContainText("Storage is unavailable");
});

test("removal remains visible at narrow width", async ({ page }) => {
  await page.setViewportSize({ width: 360, height: 700 });
  await page.goto("/browser/package-removal.html");
  const button = page.getByRole("button", { name: "Forget Microsoft.Extensions.Http from recent packages", exact: true });
  await expect(button).toBeInViewport();
  await button.click();
  await expect(button).toHaveCount(0);
});

test("platform pre-search dismissal persists while search and explicit reopening remain available", async ({ page }) => {
  await page.goto("/browser/package-removal.html?platform=1");
  const dismiss = page.getByRole("button", { name: "Dismiss System.Runtime from Spotlight suggestions", exact: true });
  await dismiss.click();
  await expect(page.locator('[data-sl-framework-lib="System.Runtime"]')).toHaveCount(0);
  await expect(page.locator("#notice")).toHaveText("");
  await page.reload();
  await expect(page.locator('[data-sl-framework-lib="System.Runtime"]')).toHaveCount(0);
  await page.locator("#spotlight-input").fill("System.Runtime");
  await expect(page.locator('[data-sl-framework-lib="System.Runtime"]')).toBeVisible();
  await expect(dismiss).toHaveCount(0);
  await page.locator('[data-sl-framework-lib="System.Runtime"]').click();
  await expect(page.locator("#notice")).toHaveText("Activated");
  await page.reload();
  await expect(dismiss).toBeVisible();
});

test("artifact columns align when dates and controls are absent", async ({ page }) => {
  await page.goto("/browser/package-removal.html?platform=1");
  const geometry = await page.locator(".spotlight-artifact").evaluateAll(rows => rows.map(row => {
    const metadata = row.querySelector(".spotlight-item-ns")!.getBoundingClientRect();
    const date = row.querySelector(".spotlight-item-date")!.getBoundingClientRect();
    return { metadataRight: metadata.right, dateRight: date.right, height: row.getBoundingClientRect().height };
  }));
  expect(geometry.length).toBeGreaterThan(1);
  for (const row of geometry) {
    expect(row.metadataRight).toBeCloseTo(geometry[0]!.metadataRight, 0);
    expect(row.dateRight).toBeCloseTo(geometry[0]!.dateRight, 0);
    expect(row.height).toEqual(geometry[0]!.height);
  }
  await page.locator("#spotlight-input").fill("System");
  await expect(page.locator('[data-sl-framework-lib="System.Runtime"]')).toBeVisible();
  await expect(page.getByRole("button", { name: "Dismiss System.Runtime from Spotlight suggestions" })).toHaveCount(0);
  const searchGeometry = await page.locator(".spotlight-artifact").evaluateAll(rows => rows.map(row => ({
    right: row.getBoundingClientRect().right,
    dateRight: row.querySelector(".spotlight-item-date")!.getBoundingClientRect().right,
  })));
  for (const row of searchGeometry) {
    expect(row.right).toBeCloseTo(searchGeometry[0]!.right, 0);
    expect(row.dateRight).toBeCloseTo(searchGeometry[0]!.dateRight, 0);
  }
});
