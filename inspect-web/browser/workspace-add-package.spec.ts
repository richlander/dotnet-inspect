import { expect, test } from "@playwright/test";

test("Add selects a package without losing the old row or active coordinate", async ({ page }) => {
  await page.goto("/browser/workspace-add-package.html");
  await page.getByRole("button", { name: "Add package", exact: true }).click();
  const dialog = page.getByRole("dialog", { name: "Add package", exact: true });
  const search = dialog.getByRole("combobox", { name: "Add package", exact: true });
  await expect(search).toBeFocused();
  await expect(dialog.locator("[data-sl-remove]")).toHaveCount(0);
  await expect(dialog.getByRole("option")).toHaveCount(1);
  await search.fill("Beta");
  await expect(dialog.getByRole("option", { name: /Beta/ })).toBeVisible();
  await search.press("Enter");
  await expect(dialog).toHaveCount(0);
  await expect(page.getByRole("button", { name: "Inspect Alpha 1.2.3 net10.0" })).toBeVisible();
  await expect(page.getByRole("button", { name: "Inspect Beta 4.5.6 net9.0" })).toBeVisible();
  await expect(page.locator("#notice")).toHaveText("Active: Alpha");
  await expect(page.getByRole("heading", { name: "Workspace", exact: true })).toBeFocused();
});

test("empty Workspace accepts its first package and loaded selection stays singular", async ({ page }) => {
  await page.goto("/browser/workspace-add-package.html?empty=1");
  const add = page.getByRole("button", { name: "Add package", exact: true });
  await add.click();
  await page.getByRole("combobox", { name: "Add package", exact: true }).fill("Beta");
  await page.getByRole("option", { name: /Beta/ }).click();
  await expect(page.locator("#notice")).toHaveText("Active: Beta");
  await add.click();
  await expect(page.getByRole("option", { name: /Beta.*already in Workspace/ })).toBeVisible();
  await page.getByRole("combobox", { name: "Add package", exact: true }).press("Enter");
  await expect(page.locator("[data-workspace-activate]")).toHaveCount(1);
});

test("Cancel and Escape return to Add, and Tab stays in the picker", async ({ page }) => {
  await page.goto("/browser/workspace-add-package.html");
  const add = page.getByRole("button", { name: "Add package", exact: true });
  await add.click();
  const input = page.getByRole("combobox", { name: "Add package", exact: true });
  const cancel = page.getByRole("button", { name: "Cancel", exact: true });
  await input.press("Tab");
  await expect(cancel).toBeFocused();
  await cancel.press("Tab");
  await expect(input).toBeFocused();
  await input.press("Shift+Tab");
  await expect(cancel).toBeFocused();
  await cancel.press("Enter");
  await expect(add).toBeFocused();
  await add.press("Enter");
  await input.press("Escape");
  await expect(add).toBeFocused();
  await expect(page.locator("[data-workspace-activate]")).toHaveCount(1);
});

test("source failure is visible and editing the query recovers", async ({ page }) => {
  await page.goto("/browser/workspace-add-package.html");
  await page.getByRole("button", { name: "Add package", exact: true }).click();
  const dialog = page.getByRole("dialog", { name: "Add package", exact: true });
  const input = dialog.getByRole("combobox");
  await input.fill("fail");
  await expect(dialog.getByRole("status")).toContainText("NuGet unavailable");
  await expect(dialog).not.toContainText("Nothing matches");
  await page.clock.install({ time: new Date("2026-01-01T00:00:00Z") });
  await page.clock.pauseAt(new Date("2026-01-01T00:01:00Z"));
  await input.fill("failx");
  await input.press("Backspace");
  await page.clock.runFor(250);
  await expect(dialog.getByRole("status")).toContainText("NuGet unavailable");
  await expect(dialog).not.toContainText("Nothing matches");
  await input.fill("Beta");
  await page.clock.runFor(250);
  await expect(dialog.getByRole("option", { name: /Beta/ })).toBeVisible();
  await expect(dialog.getByRole("status")).toHaveCount(0);
});

test("normal Search after dismissal regains its scopes and removal affordances", async ({ page }) => {
  await page.goto("/browser/workspace-add-package.html");
  await page.getByRole("button", { name: "Add package", exact: true }).click();
  await page.getByRole("button", { name: "Cancel", exact: true }).click();
  await page.getByRole("button", { name: "Search", exact: true }).click();
  const dialog = page.getByRole("dialog", { name: "Go to anything", exact: true });
  await expect(dialog.locator("[data-sl-remove]")).toHaveCount(1);
  await expect(dialog).toContainText("All");
  await expect(dialog).toContainText("Platform");
  await dialog.getByRole("option", { name: /Alpha/ }).click();
  await expect(page.locator("#notice")).toHaveText("Ordinary Search selection");
});

test("Add focus survives a Workspace refresh and the control fits narrow widths", async ({ page }) => {
  await page.setViewportSize({ width: 360, height: 700 });
  await page.goto("/browser/workspace-add-package.html");
  const add = page.getByRole("button", { name: "Add package", exact: true });
  await add.focus();
  await page.evaluate(() => document.dispatchEvent(new Event("workspace-add-rerender")));
  await expect(add).toBeFocused();
  await expect(add).toBeInViewport();
  await add.click();
  await expect(page.getByRole("button", { name: "Cancel", exact: true })).toBeInViewport();
});
