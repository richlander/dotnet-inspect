import { expect, test } from "@playwright/test";
import {
  currentWorkspaceHistoryState,
  installWorkspaceSourceFacades,
  openProductDestination,
} from "./library-hierarchy.support.ts";

test("Ecosystems Back preserves a source-owned Workspace", async ({ page }) => {
  await installWorkspaceSourceFacades(page, [{
    endpoint: "https://packages.example.test/v3/index.json",
    authentication: "Anonymous",
  }]);

  await page.goto("/");
  await expect(page.locator(".home-search"))
    .toHaveAttribute("aria-busy", "false");
  await page.evaluate(() => {
    const link = document.createElement("a");
    link.href = "/?w=source-bearing-packet";
    link.textContent = "Open source Workspace";
    document.body.append(link);
  });
  await page.getByRole("link", { name: "Open source Workspace" }).click();
  await expect(page.locator(".inspected-target"))
    .toContainText("Example.Package");
  const sourceWorkspace = await currentWorkspaceHistoryState(page);
  expect(sourceWorkspace.id).not.toBeNull();

  await openProductDestination(page, "ecosystems");
  await expect(page).toHaveURL("/ecosystems");
  await page.goBack();

  await expect(page).toHaveURL("/?w=source-bearing-packet");
  await expect(page.locator(".inspected-target"))
    .toContainText("Example.Package");
  await expect.poll(() => currentWorkspaceHistoryState(page))
    .toEqual(sourceWorkspace);
});

test("authentication-required Workspace prompts again after reload", async ({
  page,
}) => {
  const endpoint =
    "https://nuget.pkg.github.com/example/index.json";
  const pat = "page-session-secret";
  await installWorkspaceSourceFacades(page, [{
    endpoint,
    authentication: "AuthenticationRequired",
  }]);

  await page.goto("/?w=source-bearing-packet");
  const dialog = page.getByRole("dialog", {
    name: "Sign in to open this Workspace",
  });
  await expect(dialog).toBeVisible();
  await expect(dialog).toContainText(endpoint);
  await page.getByLabel("Username").fill("example");
  await page.getByLabel("Personal access token").fill(pat);
  await expect(page).not.toHaveURL(new RegExp(pat));
  await expect.poll(
    () => page.evaluate(() => JSON.stringify(localStorage)),
  ).not.toContain(pat);

  await page.reload();
  await expect(dialog).toBeVisible();
  await expect(page.getByLabel("Username")).toHaveValue("");
  await expect(page.getByLabel("Personal access token")).toHaveValue("");
  await page.getByRole("button", { name: "Cancel" }).click();
  await expect(dialog).toHaveCount(0);
  await expect(page.locator(".load-error strong"))
    .toHaveText("Workspace restore failed");
});
