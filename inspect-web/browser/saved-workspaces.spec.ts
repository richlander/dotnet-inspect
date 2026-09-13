import { expect, test, type Page } from "@playwright/test";

async function save(page: Page, name: string) {
  await page.getByRole("button", { name: "Save Workspace", exact: true }).click();
  await page.getByLabel("Workspace name", { exact: true }).fill(name);
  await page.getByRole("button", { name: "Save", exact: true }).click();
}

test("save, reopen, and forget survive refresh without changing the live Workspace on Forget", async ({ page }) => {
  await page.goto("/browser/saved-workspaces.html");
  await save(page, "Json study");
  const open = page.getByRole("button", { name: "Open saved Workspace Json study", exact: true });
  await expect(open).toBeFocused();
  await page.getByRole("button", { name: "Load a different Workspace" }).click();
  await expect(page.getByRole("button", { name: "Inspect Beta 4.5.6 net9.0" })).toBeVisible();
  await open.click();
  await expect(page.getByRole("button", { name: "Inspect Alpha 1.2.3 net10.0" })).toBeVisible();
  await page.goto("/browser/saved-workspaces.html?empty=1");
  await expect(page.getByRole("button", { name: "Save Workspace", exact: true })).toBeDisabled();
  await open.click();
  await expect(page.getByRole("button", { name: "Inspect Alpha 1.2.3 net10.0" })).toBeVisible();
  await page.getByRole("button", { name: "Forget saved Workspace Json study", exact: true }).click();
  await expect(open).toHaveCount(0);
  await expect(page.getByRole("button", { name: "Inspect Alpha 1.2.3 net10.0" })).toBeVisible();
  await expect(page.getByRole("button", { name: "Save Workspace", exact: true })).toBeFocused();
  await page.reload();
  await expect(open).toHaveCount(0);
});

test("duplicate names keep the prior save and the draft visible", async ({ page }) => {
  await page.goto("/browser/saved-workspaces.html");
  await save(page, "Study");
  await save(page, " study ");
  await expect(page.getByRole("alert")).toContainText("already exists");
  await expect(page.getByLabel("Workspace name", { exact: true })).toHaveValue(" study ");
  await expect(page.locator("[data-saved-workspace-open]")).toHaveCount(1);
  await page.getByRole("button", { name: "Cancel", exact: true }).click();
  await expect(page.getByRole("button", { name: "Save Workspace", exact: true })).toBeFocused();
});

test("name text and selection survive an asynchronous Workspace replacement render", async ({ page }) => {
  await page.goto("/browser/saved-workspaces.html");
  await page.getByRole("button", { name: "Save Workspace", exact: true }).click();
  const input = page.getByLabel("Workspace name", { exact: true });
  await expect(input).toBeFocused();
  await input.fill("Json study");
  await input.evaluate(element => {
    if (!(element instanceof HTMLInputElement)) throw new Error("Missing name input");
    element.setSelectionRange(2, 6, "backward");
    document.dispatchEvent(new Event("saved-workspace-rerender"));
  });
  await expect(input).toBeFocused();
  await expect(input).toHaveValue("Json study");
  expect(await input.evaluate(element => element instanceof HTMLInputElement
    ? [element.selectionStart, element.selectionEnd, element.selectionDirection] : null)).toEqual([2, 6, "backward"]);
  await input.press("Enter");
  await expect(page.getByRole("button", { name: "Open saved Workspace Json study", exact: true })).toBeFocused();
});

test("Forget uses next, previous, and heading fallback without activating a save", async ({ page }) => {
  await page.goto("/browser/saved-workspaces.html");
  await save(page, "First");
  await save(page, "Second");
  await save(page, "Third");
  await page.getByRole("button", { name: "Forget saved Workspace Second", exact: true }).click();
  await expect(page.getByRole("button", { name: "Forget saved Workspace Third", exact: true })).toBeFocused();
  await page.getByRole("button", { name: "Forget saved Workspace Third", exact: true }).press("Enter");
  await expect(page.getByRole("button", { name: "Forget saved Workspace First", exact: true })).toBeFocused();
  await page.getByRole("button", { name: "Remove Alpha 1.2.3 net10.0 from Workspace", exact: true }).click();
  await expect(page.getByRole("button", { name: "Open saved Workspace First", exact: true })).toBeVisible();
  await page.getByRole("button", { name: "Forget saved Workspace First", exact: true }).click();
  await expect(page.getByRole("heading", { name: "Workspace", exact: true })).toBeFocused();
  await expect(page.locator("#notice")).toHaveText("");
});

for (const failure of ["write-failure", "projection-failure"]) {
  test(`${failure} leaves the live Workspace and draft unchanged`, async ({ page }) => {
    await page.goto(`/browser/saved-workspaces.html?${failure}=1`);
    await save(page, "Study");
    await expect(page.getByRole("alert")).toContainText("Could not save Workspace");
    await expect(page.getByLabel("Workspace name", { exact: true })).toHaveValue("Study");
    await expect(page.locator("[data-saved-workspace-open]")).toHaveCount(0);
    await expect(page.getByRole("button", { name: "Inspect Alpha 1.2.3 net10.0" })).toBeVisible();
  });
}

test("failed storage deletion keeps its entry and reports the error", async ({ page }) => {
  await page.goto("/browser/saved-workspaces.html");
  await save(page, "Study");
  await page.goto("/browser/saved-workspaces.html?write-failure=1");
  await page.getByRole("button", { name: "Forget saved Workspace Study", exact: true }).click();
  await expect(page.getByRole("button", { name: "Open saved Workspace Study", exact: true })).toBeVisible();
  await expect(page.getByRole("alert")).toContainText("Quota exceeded");
  await expect(page.getByRole("button", { name: "Inspect Alpha 1.2.3 net10.0" })).toBeVisible();
});

test("unreadable storage remains visible and Retry rereads without an overwrite", async ({ page }) => {
  await page.goto("/browser/saved-workspaces.html");
  await save(page, "Study");
  await page.goto("/browser/saved-workspaces.html?read-failure=1");
  await expect(page.getByRole("alert")).toContainText("Could not read saved Workspaces");
  await expect(page.getByRole("button", { name: "Save Workspace", exact: true })).toBeDisabled();
  await page.evaluate(() => document.dispatchEvent(new Event("storage-available")));
  await page.getByRole("button", { name: "Retry reading saved Workspaces" }).click();
  await expect(page.getByRole("button", { name: "Open saved Workspace Study", exact: true })).toBeVisible();
});

test("saved close controls remain visible at narrow widths with long names", async ({ page }) => {
  await page.setViewportSize({ width: 360, height: 700 });
  await page.goto("/browser/saved-workspaces.html");
  await save(page, "Long".repeat(30));
  const remove = page.locator("[data-saved-workspace-remove]");
  await expect(remove).toBeInViewport();
  await remove.click();
  await expect(remove).toHaveCount(0);
});
