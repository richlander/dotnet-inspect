import { expect, test } from "@playwright/test";
import { installFacades, releaseFacade, root, surface } from "./library-hierarchy.support.ts";

for (const width of [1440, 390]) {
  test(`Overview renders documents before asynchronous titles at ${width}px`, async ({ page }, testInfo) => {
    await page.setViewportSize({ width, height: 900 });
    const model = { ...surface, documents: ["README.md", "guide.md", "untitled.md", "broken.md"].map(name => ({ kind: "readme", name, path: name, size: 120 })) };
    await installFacades(page, model, [], "ready", "ready", undefined, "ready", "ready", undefined, { packageDocumentTitles: "deferred" });
    await page.goto(root);
    const rows = page.locator(".package-document-list > li");
    await expect(rows).toHaveCount(4);
    await expect(page.locator(".doc-title")).toHaveText(Array(4).fill("Loading title…"));
    await expect(page.locator("html")).toHaveAttribute("data-package-document-title-request", /broken.md/);
    await expect(page.locator("[data-package-child-library]").first()).toBeVisible();
    await releaseFacade(page, "package-document-titles");
    await expect(page.locator(".doc-title")).toHaveText(["First title & details", "Setext title", "", "Title unavailable"]);
    const bounds = await rows.evaluateAll(elements => elements.map(element => {
      const box = element.getBoundingClientRect();
      return { x: box.x, y: box.y, bottom: box.bottom, right: box.right };
    }));
    for (let i = 1; i < bounds.length; i++) expect(bounds[i]?.y).toBeGreaterThanOrEqual(bounds[i - 1]?.bottom ?? 0);
    expect(bounds[0]?.right).toBeLessThanOrEqual(width);
    await page.screenshot({ path: testInfo.outputPath("package-documentation.png"), fullPage: true });
    await page.locator('[data-doc-path="README.md"]').click();
    await expect(page.locator(".doc-viewer-body")).toContainText("First title");
  });
}


test("late document titles cannot overwrite a replacement package version", async ({ page }) => {
  const model = { ...surface, documents: [{ kind: "readme", name: "README.md", path: "README.md", size: 120 }] };
  await installFacades(page, model, [{ ...model, version: "0.9.0" }], "ready", "ready", undefined, "ready", "ready", undefined, { packageDocumentTitles: "deferred" });
  await page.goto(root);
  await expect(page.locator("html")).toHaveAttribute("data-package-document-title-request", /1.0.0/);
  await page.locator('[data-package-version="0.9.0"]').click();
  await expect(page.locator(".doc-title")).toHaveText("Older package");
  await releaseFacade(page, "package-document-titles");
  await expect(page.locator("html")).toHaveAttribute("data-package-document-title-completion", /1.0.0/);
  await expect(page.locator(".doc-title")).toHaveText("Older package");
});

test("publishes body and frontmatter through one contained fragment", async ({ page }) => {
  const model = {
    ...surface,
    documents: [{ kind: "readme", name: "README.md", path: "README.md", size: 120 }],
  };
  await installFacades(
    page,
    model,
    [],
    "ready",
    "ready",
    undefined,
    "ready",
    "ready",
    undefined,
    {
      packageDocumentTitles: "ready",
      packageDocumentText: [
        "---",
        "name: package\u202Ename",
        "version: literal-\\u202E",
        "description: <p>description\u202Evalue</p>",
        "---",
        "# Heading \u202E",
        "",
        "Body literal-\\u202E and actual-\u202E.",
        "",
        "[link\u202E](https://example.test/path \"title\u202E\") with `code\u202E`.",
      ].join("\n"),
    },
  );
  await page.goto(root);
  await page.locator('[data-doc-path="README.md"]').click();

  const viewer = page.locator(".doc-viewer-body");
  await expect(viewer.locator("h1")).toHaveText("Heading \\u202E");
  await expect(viewer).toContainText("Body literal-\\\\u202E and actual-\\u202E.");
  await expect(viewer.locator("a")).toHaveText("link\\u202E");
  await expect(viewer.locator("a")).toHaveAttribute("title", "title\\u202E");
  await expect(viewer.locator("a")).not.toHaveAttribute("href", /.+/);
  await expect(viewer.locator("code")).toHaveText("code\\u202E");
  await expect(viewer.locator(".doc-frontmatter")).toContainText("package\\u202Ename");
  await expect(viewer.locator(".doc-frontmatter")).toContainText("literal-\\\\u202E");
  await expect(
    viewer.locator(".doc-frontmatter > .doc-fm-desc > p"),
  ).toHaveText("description\\u202Evalue");
  await expect(viewer.locator(".doc-frontmatter > p")).toHaveCount(0);

  const containment = await page.locator("html").getAttribute("data-package-document-containment");
  expect(containment).not.toBeNull();
  const containmentRequest: unknown = JSON.parse(containment ?? "{}");
  expect(containmentRequest).toEqual(expect.objectContaining({
    generation: expect.stringMatching(/^package-document-\d+$/),
    leaves: expect.arrayContaining([
      expect.objectContaining({ policy: "Prose", text: "Heading \u202E" }),
      expect.objectContaining({ policy: "Field", text: "package\u202Ename" }),
      expect.objectContaining({ policy: "Field", text: "title\u202E" }),
    ]),
  }));
});

test("shows managed containment rejection as a document failure", async ({ page }) => {
  const model = {
    ...surface,
    documents: [{ kind: "readme", name: "README.md", path: "README.md", size: 120 }],
  };
  await installFacades(
    page,
    model,
    [],
    "ready",
    "ready",
    undefined,
    "ready",
    "ready",
    undefined,
    {
      packageDocumentTitles: "ready",
      packageDocumentContainment: "rejected",
    },
  );
  await page.goto(root);
  await page.locator('[data-doc-path="README.md"]').click();
  await expect(page.getByRole("dialog", { name: "Package document" })).toContainText(
    "Package document containment rejected managed-rejection (OutputTextTooLarge; actual 2; limit 1)",
  );
});

test("closing during containment prevents stale document publication", async ({ page }) => {
  const model = {
    ...surface,
    documents: [{ kind: "readme", name: "README.md", path: "README.md", size: 120 }],
  };
  await installFacades(
    page,
    model,
    [],
    "ready",
    "ready",
    undefined,
    "ready",
    "ready",
    undefined,
    {
      packageDocumentTitles: "ready",
      packageDocumentContainment: "deferred",
    },
  );
  await page.goto(root);
  await page.locator('[data-doc-path="README.md"]').click();
  await expect(page.locator("html")).toHaveAttribute(
    "data-package-document-containment",
    /"generation":"package-document-\d+"/,
  );
  await page.getByRole("button", { name: "Close" }).click();
  await releaseFacade(page, "package-document-containment");
  await expect(page.locator("html")).toHaveAttribute(
    "data-package-document-containment-completion",
    /^package-document-\d+$/,
  );
  await expect(page.getByRole("dialog", { name: "Package document" })).toBeHidden();
});
