import { expect, test, type Page } from "@playwright/test";
import {
  installLibraryUploadFacades,
  releaseFacade,
  root,
  subjectTab,
} from "./library-hierarchy.support.ts";

async function dropLibrary(page: Page, name: string, bytes: number[]) {
  const transfer = await page.evaluateHandle(({ fileName, content }) => {
    const dataTransfer = new DataTransfer();
    dataTransfer.items.add(new File(
      [new Uint8Array(content)],
      fileName,
      { type: "application/octet-stream" },
    ));
    return dataTransfer;
  }, { fileName: name, content: bytes });
  try {
    await page.dispatchEvent("body", "dragover", {
      dataTransfer: transfer,
    });
    await page.dispatchEvent("body", "drop", {
      dataTransfer: transfer,
    });
  } finally {
    await transfer.dispose();
  }
}

test("global drop keeps managed-image rejection visible", async ({ page }) => {
  await installLibraryUploadFacades(page, "rejected");
  await page.goto(root);
  await expect(subjectTab(page, "package"))
    .toHaveAttribute("aria-selected", "true");

  await dropLibrary(page, "native.dll", [0x4d, 0x5a, 0, 1]);

  await expect(page.getByRole("dialog", { name: "Managed Library" }))
    .toBeVisible();
  await expect(page.locator(".library-open-error"))
    .toHaveText("The dropped file is not a managed assembly.");
});

test("routed navigation retires an in-flight upload", async ({ page }) => {
  await installLibraryUploadFacades(page, "deferred");
  await page.goto(root);
  await expect(subjectTab(page, "package"))
    .toHaveAttribute("aria-selected", "true");
  await page.evaluate(() => history.pushState(null, "", "/demos"));

  await dropLibrary(page, "Deferred.dll", [1, 2, 3, 4]);
  await expect(page.locator("html")).toHaveAttribute(
    "data-library-upload-request",
    JSON.stringify(["Deferred.dll", 4]),
  );
  await expect(page.locator(".library-open-status")).toBeVisible();

  await page.goBack();
  await expect(page.locator("#library-open-dialog")).toHaveCount(0);
  await expect(subjectTab(page, "package"))
    .toHaveAttribute("aria-selected", "true");

  await releaseFacade(page, "finish-library-upload");
  await expect(page.getByText("Browser upload", { exact: true }))
    .toHaveCount(0);
  await expect(subjectTab(page, "package"))
    .toHaveAttribute("aria-selected", "true");
});

test("successful upload replaces stale Package URL with Home", async ({ page }) => {
  await installLibraryUploadFacades(page, "available");
  await page.goto(root);
  await expect(subjectTab(page, "package"))
    .toHaveAttribute("aria-selected", "true");

  await dropLibrary(page, "Uploaded.Library.dll", [1, 2, 3, 4]);

  await expect(page.getByText("Browser upload", { exact: true }))
    .toBeVisible();
  await expect.poll(() => new URL(page.url()).pathname).toBe("/");
  await expect.poll(() => new URL(page.url()).search).toBe("");
  await expect.poll(() => new URL(page.url()).hash).toBe("");

  await page.reload();
  await expect(page.locator(".home-title"))
    .toHaveText("Inspect .NET packages and libraries in your browser.");
});
