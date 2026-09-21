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

async function dropLibraryAndReadDefaultPrevented(
  page: Page,
  name: string,
  bytes: number[],
) {
  return page.evaluate(({ fileName, content }) => {
    const dataTransfer = new DataTransfer();
    dataTransfer.items.add(new File(
      [new Uint8Array(content)],
      fileName,
      { type: "application/octet-stream" },
    ));
    const event = new DragEvent("drop", {
      bubbles: true,
      cancelable: true,
      dataTransfer,
    });
    document.body.dispatchEvent(event);
    return event.defaultPrevented;
  }, { fileName: name, content: bytes });
}

async function waitForWorkspaceReady(page: Page) {
  await expect(page.locator("#app"))
    .not.toHaveAttribute("aria-busy", "true");
}

test("global drop keeps managed-image rejection visible", async ({ page }) => {
  await installLibraryUploadFacades(page, "rejected");
  await page.goto(root);
  await expect(subjectTab(page, "package"))
    .toHaveAttribute("aria-selected", "true");
  await waitForWorkspaceReady(page);

  await dropLibrary(page, "native.dll", [0x4d, 0x5a, 0, 1]);

  await expect(page.getByRole("dialog", { name: "Managed Library" }))
    .toBeVisible();
  await expect(page.locator(".library-open-error"))
    .toHaveText("The dropped file is not a managed assembly.");
  await expect(page.locator(".library-open-error")).toBeFocused();
});

test("routed navigation retires an in-flight upload", async ({ page }) => {
  await installLibraryUploadFacades(page, "deferred");
  await page.goto(root);
  await expect(subjectTab(page, "package"))
    .toHaveAttribute("aria-selected", "true");
  await waitForWorkspaceReady(page);
  await page.evaluate(() => history.pushState(null, "", "/demos"));

  await dropLibrary(page, "Deferred.dll", [1, 2, 3, 4]);
  await expect(page.locator("html")).toHaveAttribute(
    "data-library-upload-request",
    JSON.stringify(["Deferred.dll", 4]),
  );
  await expect(page.locator(".library-open-status")).toBeVisible();
  await expect(page.locator(".library-open-status")).toBeFocused();
  expect(await dropLibraryAndReadDefaultPrevented(
    page,
    "Ignored.dll",
    [5, 6, 7, 8],
  )).toBe(true);
  await expect(page.locator("html")).toHaveAttribute(
    "data-library-upload-request",
    JSON.stringify(["Deferred.dll", 4]),
  );

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

test("upload retires an older in-flight Package transition", async ({ page }) => {
  await installLibraryUploadFacades(page, "available", {
    deferChanges: true,
    versions: ["1.0.1", "1.0.0"],
  });
  await page.goto(root);

  await page.locator("#package-version").selectOption("1.0.1");
  await expect(page.locator("html")).toHaveAttribute(
    "data-package-query-pending",
    JSON.stringify(["Example.Package", "1.0.1", "net10.0"]),
  );

  await dropLibrary(page, "Uploaded.Library.dll", [1, 2, 3, 4]);
  await expect(page.getByText("Browser upload", { exact: true }))
    .toBeVisible();

  await releaseFacade(page, "finish-package-query");
  await expect(page.getByText("Browser upload", { exact: true }))
    .toBeVisible();
  await expect(subjectTab(page, "library"))
    .toHaveAttribute("aria-selected", "true");
});

test("failed Package open restores the uploaded Library", async ({ page }) => {
  await installLibraryUploadFacades(page, "available", {
    deferChanges: true,
    failVersionOnce: "1.0.1",
    versions: ["1.0.1", "1.0.0"],
  });
  await page.goto(root);
  await expect(subjectTab(page, "package"))
    .toHaveAttribute("aria-selected", "true");
  await waitForWorkspaceReady(page);

  await dropLibrary(page, "Uploaded.Library.dll", [1, 2, 3, 4]);
  await expect(page.getByText("Browser upload", { exact: true }))
    .toBeVisible();

  await page.getByRole(
    "button",
    { name: "Search", exact: true },
  ).click();
  await page.locator("#spotlight-input")
    .fill("Other.Package@1.0.1");
  await page.locator('[data-sl-pkg-load="Other.Package"]').click();
  await expect(page.locator("html")).toHaveAttribute(
    "data-package-query-pending",
    JSON.stringify(["Other.Package", "1.0.1", ""]),
  );

  await releaseFacade(page, "finish-package-query");
  await expect(page.getByText("Browser upload", { exact: true }))
    .toBeVisible();
  await expect(subjectTab(page, "library"))
    .toHaveAttribute("aria-selected", "true");
  await expect(page.locator(".query-notice"))
    .toContainText("Version inspection failed");
});

test("history does not alias replaced same-name uploads", async ({ page }) => {
  await installLibraryUploadFacades(page, "available");
  await page.goto(root);
  await expect(subjectTab(page, "package"))
    .toHaveAttribute("aria-selected", "true");
  await waitForWorkspaceReady(page);

  await dropLibrary(page, "Uploaded.Library.dll", [1, 2, 3, 4]);
  await expect(page.getByText("Browser upload", { exact: true }))
    .toBeVisible();
  await expect(page.locator("html")).toHaveAttribute(
    "data-library-upload-digest",
    `${"0".repeat(63)}1`,
  );
  await dropLibrary(page, "Uploaded.Library.dll", [2, 3, 4, 5]);
  await expect(page.locator("html")).toHaveAttribute(
    "data-library-upload-digest",
    `${"0".repeat(63)}2`,
  );
  await expect(page.locator("#library-open-dialog")).toHaveCount(0);

  const back = page.getByRole("button", { name: "Back", exact: true });
  await expect(back).toBeEnabled();
  await back.click();
  await expect(page.getByText("Browser upload", { exact: true }))
    .toHaveCount(0);
  await expect(subjectTab(page, "package"))
    .toHaveAttribute("aria-selected", "true");
});

test("page-level drop remains available on Diagnostics", async ({ page }) => {
  await installLibraryUploadFacades(page, "rejected");
  await page.goto("/diagnostics");
  await expect(page.getByRole("heading", { name: "Diagnostics", exact: true }))
    .toBeVisible();

  await dropLibrary(page, "native.dll", [0x4d, 0x5a, 0, 1]);

  await expect(page.getByRole("dialog", { name: "Managed Library" }))
    .toBeVisible();
  await expect(page.locator(".library-open-error"))
    .toHaveText("The dropped file is not a managed assembly.");
});
