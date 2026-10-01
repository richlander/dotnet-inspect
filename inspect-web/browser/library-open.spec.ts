import { expect, test, type Page } from "@playwright/test";
import {
  chooseSubject,
  installLibraryUploadFacades,
  openProductDestination,
  releaseFacade,
  run,
  root,
  surface,
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

async function waitForExamplePackageReady(page: Page) {
  await expect(page.locator("#inspector-panel h1"))
    .toHaveText("Example.Package");
}

async function openLibraryFromBrandMenu(page: Page) {
  await page.locator("[data-product-navigation-button]").click();
  await page.getByRole(
    "menuitem",
    { name: "Open Library…", exact: true },
  ).click();
  await expect(page.locator("#library-open-title")).toBeFocused();
}

test("global drop keeps managed-image rejection visible", async ({ page }) => {
  await installLibraryUploadFacades(page, "rejected");
  await page.goto(root);
  await expect(subjectTab(page, "package"))
    .toHaveAttribute("aria-selected", "true");
  await waitForExamplePackageReady(page);

  await dropLibrary(page, "native.dll", [0x4d, 0x5a, 0, 1]);

  await expect(page.getByRole("dialog", { name: "Managed Library" }))
    .toBeVisible();
  await expect(page.locator(".library-open-error"))
    .toHaveText("The dropped file is not a managed assembly.");
  await expect(page.locator(".library-open-error")).toBeFocused();
});

test("Open dismissal restores its logical invoker", async ({ page }) => {
  await installLibraryUploadFacades(page, "rejected");
  await page.goto("/");
  await expect(page.locator(".home-search")).not.toHaveClass(/engine-pending/);
  await openLibraryFromBrandMenu(page);
  await page.locator("#library-open-close").click();
  await expect(page.locator("[data-product-navigation-button]")).toBeFocused();

  await page.goto(root);
  await waitForExamplePackageReady(page);
  await openLibraryFromBrandMenu(page);
  await page.keyboard.press("Escape");
  await expect(page.locator("[data-product-navigation-button]")).toBeFocused();

  await openLibraryFromBrandMenu(page);
  await page.locator("#library-open-backdrop").click({
    position: { x: 5, y: 5 },
  });
  await expect(page.locator("[data-product-navigation-button]")).toBeFocused();
});

test("global drop replaces another modal and inerts its surface", async ({
  page,
}) => {
  await installLibraryUploadFacades(page, "rejected");
  await page.goto(root);
  await waitForExamplePackageReady(page);
  await page.locator("#application-menu-button").click();
  await page.getByRole("menuitem", { name: "Settings", exact: true }).click();
  await expect(page.locator("#settings-dialog")).toBeVisible();

  await dropLibrary(page, "native.dll", [0x4d, 0x5a, 0, 1]);

  await expect(page.locator("#settings-dialog")).toHaveCount(0);
  await expect(page.locator("#library-open-dialog")).toBeVisible();
  await expect(page.locator("#app > .workbench")).toHaveAttribute("inert", "");
  await expect(page.locator(".library-open-error")).toBeFocused();
});

test("early drop waits for engine readiness before reading bytes", async ({
  page,
}) => {
  await installLibraryUploadFacades(page, "available");
  let releaseEngine!: () => void;
  const engineGate = new Promise<void>(resolve => {
    releaseEngine = resolve;
  });
  await page.route(/\/assets\/engine-worker-entry-[^/]+\.js$/, async route => {
    await engineGate;
    await route.fallback();
  });
  await page.goto("/");
  await expect(page.locator(".home-title")).toBeVisible();
  await page.evaluate(() => {
    File.prototype.arrayBuffer = function () {
      document.documentElement.dataset.libraryArrayBufferReads =
        String(Number(
          document.documentElement.dataset.libraryArrayBufferReads ?? "0")
          + 1);
      return Blob.prototype.arrayBuffer.call(this);
    };
  });

  await dropLibrary(page, "Uploaded.Library.dll", [1, 2, 3, 4]);

  await expect(page.locator(".library-open-error"))
    .toHaveText("Wait for the browser inspection engine to finish starting.");
  await expect(page.locator("html"))
    .not.toHaveAttribute("data-library-array-buffer-reads", /.+/);
  await expect(page.locator("html"))
    .not.toHaveAttribute("data-library-upload-request", /.+/);

  releaseEngine();
  await expect(page.locator("html")).toHaveAttribute(
    "data-library-array-buffer-reads",
    "1",
  );
  await expect(page.getByText("Browser upload", { exact: true }))
    .toBeVisible();
});

test("Open progress does not render raw Unicode controls from File.name", async ({
  page,
}) => {
  await installLibraryUploadFacades(page, "deferred");
  await page.goto(root);
  await waitForExamplePackageReady(page);
  const declaredName = "invoice\u202Egpj\u2028.exe";

  await dropLibrary(page, declaredName, [1, 2, 3, 4]);

  await expect(page.locator("html")).toHaveAttribute(
    "data-library-upload-request",
    JSON.stringify([declaredName, 4]),
  );
  await expect(page.locator(".library-open-status"))
    .toHaveText("Opening managed assembly…");

  await releaseFacade(page, "finish-library-upload");
  await expect(page.getByText("Browser upload", { exact: true }))
    .toBeVisible();
});

test("routed navigation retires an in-flight upload", async ({ page }) => {
  await installLibraryUploadFacades(page, "deferred");
  await page.goto(root);
  await expect(subjectTab(page, "package"))
    .toHaveAttribute("aria-selected", "true");
  await waitForExamplePackageReady(page);
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
  await expect(page.locator("#inspector-panel h1")).toBeFocused();
  await expect.poll(() => new URL(page.url()).pathname).toBe("/");
  await expect.poll(() => new URL(page.url()).search).toBe("");
  await expect.poll(() => new URL(page.url()).hash).toBe("");

  await page.reload();
  await expect(page.locator(".home-title"))
    .toHaveText("Inspect .NET packages and libraries in your browser.");
});

test("uploaded Library method family renders owner-backed receiver kinds", async ({
  page,
}) => {
  const widget = surface.types.find(
    candidate => candidate.definitionId === "Example.Widget",
  );
  if (!widget) throw new Error("The upload fixture has no Widget Type.");
  const staticRun = {
    ...run,
    signature: "void Run(int value)",
    isStatic: true,
    metadataToken: 0x06000002,
    declarationMetadataToken: 0x06000002,
    stableSelector: "Run:1",
    anchorDigest: "widget-run-static",
    canonicalSignature: "M:Example.Widget.Run(System.Int32)",
    bodySelectors: [{
      token: 0x06000002,
      memberName: "Run",
      selectorKey: "Run:1",
    }],
  };
  const extensionRun = {
    ...run,
    signature: "void Run(Example.Widget value)",
    isStatic: true,
    isExtension: true,
    metadataToken: 0x06000003,
    declarationMetadataToken: 0x06000003,
    stableSelector: "Run:2",
    anchorDigest: "widget-run-extension",
    canonicalSignature: "M:Example.Widget.Run(Example.Widget)",
    bodySelectors: [{
      token: 0x06000003,
      memberName: "Run",
      selectorKey: "Run:2",
    }],
  };
  const uploadedSurface = {
    ...surface,
    assemblies: [{
      ...surface.assemblies[0]!,
      publicTypes: 1,
      publicMembers: 2,
    }],
    types: [{
      ...widget,
      members: 2,
      api: [staticRun, extensionRun],
    }],
    accessibility: [{
      id: "public",
      label: "Public",
      order: 0,
      isDefault: true,
      count: 2,
    }],
    totalMembers: 2,
  };
  await installLibraryUploadFacades(
    page,
    "available",
    {},
    uploadedSurface,
  );
  await page.goto(root);
  await waitForExamplePackageReady(page);

  await dropLibrary(page, "Uploaded.Library.dll", [1, 2, 3, 4]);
  await expect(page.getByText("Browser upload", { exact: true }))
    .toBeVisible();
  await chooseSubject(page, "type", "Type");
  await page.locator(
    `#type-list [data-type="${widget.id}"]`,
  ).click();
  await page.locator("[data-member]", { hasText: "Run" }).click();

  await expect(page.locator("#member-surface-title")).toHaveText("Run");
  await expect(page.locator(".member-surface-list"))
    .toContainText("public static void Run(int value)");
  await expect(page.locator(".member-surface-list"))
    .toContainText("public extension void Run(Example.Widget value)");
  await expect(page.locator("html")).toHaveAttribute(
    "data-uploaded-type-member-population-request",
    JSON.stringify([
      "Uploaded.Library.dll",
      4,
      "Example.Widget",
      "csharp",
      "public",
    ]),
  );
  await expect(page.locator("html")).not.toHaveAttribute(
    "data-uploaded-library-member-group-document-request",
    /.+/,
  );
});

test("successful upload is excluded from retained Workspace restoration", async ({
  page,
}) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  await installLibraryUploadFacades(page, "available");
  await page.goto(root);
  await waitForExamplePackageReady(page);

  await dropLibrary(page, "Uploaded.Library.dll", [1, 2, 3, 4]);
  await expect(page.getByText("Browser upload", { exact: true }))
    .toBeVisible();

  await page.getByRole("button", { name: /Search/ }).click();
  await page.locator("#spotlight-input").fill("Other.Package@1.0.0");
  await page.locator('[data-sl-pkg-load="Other.Package"]').click();
  await expect(page.locator(".inspected-target"))
    .toContainText("Other.Package");

  await openProductDestination(page, "workspace");
  await page.locator('[data-workspace-switch="workspace-1"]').click();

  await expect(page.getByText("Browser upload", { exact: true }))
    .toHaveCount(0);
  await expect(page.locator(".inspected-target"))
    .toContainText("Example.Package");
  await expect.poll(() => new URL(page.url()).searchParams.get("package"))
    .toBe("Example.Package");
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
  await waitForExamplePackageReady(page);

  await dropLibrary(page, "Uploaded.Library.dll", [1, 2, 3, 4]);
  await expect(page.getByText("Browser upload", { exact: true }))
    .toBeVisible();

  await page.getByRole("button", { name: /Search/ }).click();
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
  await waitForExamplePackageReady(page);

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
  await expect(page.locator("#app > :not(#library-open-backdrop)"))
    .toHaveAttribute("inert", "");
});
