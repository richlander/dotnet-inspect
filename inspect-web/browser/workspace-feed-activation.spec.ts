import { expect, test } from "@playwright/test";
import {
  installWorkspaceSourceFacades,
} from "./library-hierarchy.support.ts";

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
