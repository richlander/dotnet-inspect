import { expect, test } from "@playwright/test";

for (const mode of ["", "?modal=1"]) {
  test(`package removal preserves ${mode ? "modal" : "Home"} search and forgets history`, async ({ page }) => {
    await page.goto(`/browser/package-removal.html${mode}`);
    const input = page.locator("#spotlight-input");
    await input.fill("Json");
    await page.getByRole("button", { name: "Remove System.Text.Json 10.0.0 net10.0 from Workspace", exact: true }).click();
    await expect(input).toBeFocused();
    await expect(input).toHaveValue("Json");
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
