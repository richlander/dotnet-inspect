import { expect, test } from "@playwright/test";

test.beforeEach(async ({ page }) => {
  await page.goto("/browser/annotated-source.html");
  await expect(page.locator("#explore-annotated")).toBeVisible();
});

test("inline source uses the page bar and ends with provenance", async ({ page }) => {
  await expect(page.locator(".annotated-reader-head")).toHaveCount(0);
  await expect(page.locator(".detail-head #copy-annotated")).toHaveText("Copy");
  await expect(page.locator(".detail-head #explore-annotated")).toHaveText("Explore");
  await expect(page.locator(".annotated-reader-footer")).toContainText(
    "browser-gate product fixture",
  );
});

test("inline source owns horizontal scrolling for long product lines", async ({ page }) => {
  const source = page.locator(".annotated-reader > .annotated-source-code");
  await source.locator(".annotated-source-line code").first().evaluate(element => {
    element.textContent = "return " + "VeryLongIdentifier.".repeat(80) + "Value;";
  });

  const metrics = await source.evaluate(element => {
    element.scrollLeft = 240;
    return {
      clientWidth: element.clientWidth,
      scrollWidth: element.scrollWidth,
      scrollLeft: element.scrollLeft,
    };
  });

  expect(metrics.scrollWidth).toBeGreaterThan(metrics.clientWidth);
  expect(metrics.scrollLeft).toBeGreaterThan(0);
});

test("source copy excludes annotation and inspector chrome", async ({ page }) => {
  await page.locator("#copy-annotated").click();
  const copied = await page.locator("body").getAttribute("data-copied-source");

  expect(copied).toContain("return new object();");
  expect(copied).not.toContain("alloc.new");
  expect(copied).not.toContain("Persistent inspector");
});

test("annotation rows preserve the anchored source indentation", async ({ page }) => {
  await page.locator("#explore-annotated").click();
  const annotationRow = page.locator(".annotated-row-items").filter({
    has: page.locator("#annotated-chip-modal-0-1-CSharp"),
  });
  const anchorStart =
    await annotationRow.getAttribute("data-annotated-anchor-start");
  if (anchorStart === null) {
    throw new Error("Annotation row has no product-issued anchor");
  }
  const invocation = page.locator(
    `#annotated-source-modal [data-annotated-source-start="${anchorStart}"]`,
  );
  const annotation = page.locator("#annotated-chip-modal-0-1-CSharp");
  const invocationBox = await invocation.boundingBox();
  const annotationBox = await annotation.boundingBox();
  if (!invocationBox || !annotationBox) {
    throw new Error("Anchored annotation has no browser geometry");
  }

  expect(Math.abs(invocationBox.x - annotationBox.x)).toBeLessThan(1);
});

test("finding chips open detail without jumping the source", async ({ page }) => {
  await page.addStyleTag({
    content: ".annotated-reader { height: 150px !important; }",
  });
  const source = page.locator(
    '.annotated-source-code[data-annotated-surface="embedded"]',
  );
  const before = await source.evaluate(element => {
    element.scrollTop = 18;
    return element.scrollTop;
  });
  expect(before).toBeGreaterThan(0);

  await page.locator("#annotated-chip-embedded-0-1-CSharp").click();

  await expect(page.locator("#annotated-detail-title")).toBeFocused();
  await expect(source).toHaveJSProperty("scrollTop", before);
});

test("modal finding chips preserve the source pane position", async ({ page }) => {
  await page.locator("#explore-annotated").click();
  await page.addStyleTag({
    content: `
      .annotated-modal-source .annotated-source-code {
        min-height: 900px !important;
        overflow: visible !important;
      }
    `,
  });
  const source = page.locator(".annotated-modal-source");
  const before = await source.evaluate(element => {
    element.scrollTop = 18;
    return element.scrollTop;
  });
  expect(before).toBeGreaterThan(0);

  await page.locator("#annotated-chip-modal-0-1-CSharp").click();

  await expect(page.locator("#annotated-detail-title")).toBeFocused();
  await expect(source).toHaveJSProperty("scrollTop", before);
});

test("pointer hit testing prefers the product-issued invocation node", async ({ page }) => {
  await page.locator("#explore-annotated").click();
  const invocation = page.locator(
    '#annotated-source-modal .annotated-source-segment.invocation:has-text("object")',
  ).first();
  await invocation.click({ position: { x: 8, y: 8 } });

  await expect(page.locator("#annotated-node-1")).toBeFocused();
  await expect(page.locator("#annotated-node-4")).toHaveCount(0);
});

test("source selection reveals the focused inspector node", async ({ page }) => {
  await page.locator("#explore-annotated").click();
  await page.addStyleTag({
    content: ".annotated-modal-inspector { height: 120px !important; }",
  });
  const inspector = page.locator(".annotated-modal-inspector");
  const before = await inspector.evaluate(element => {
    element.scrollTop = element.scrollHeight;
    return element.scrollTop;
  });
  expect(before).toBeGreaterThan(0);

  const invocation = page.locator(
    '#annotated-source-modal .annotated-source-segment.invocation:has-text("object")',
  ).first();
  await invocation.click({ position: { x: 8, y: 8 } });

  const focused = page.locator("#annotated-node-1");
  await expect(focused).toBeFocused();
  const visible = await inspector.evaluate((element, selector) => {
    const target = document.querySelector(selector);
    if (!(target instanceof HTMLElement)) return false;
    const viewport = element.getBoundingClientRect();
    const bounds = target.getBoundingClientRect();
    return bounds.bottom > viewport.top && bounds.top < viewport.bottom;
  }, "#annotated-node-1");
  expect(visible).toBe(true);
});

test("keyboard source activation selects the same invocation node", async ({ page }) => {
  await page.locator("#explore-annotated").click();
  const invocation = page.locator(
    '#annotated-source-modal .annotated-source-segment.invocation:has-text("object")',
  ).first();
  await invocation.focus();
  await invocation.press("Enter");

  await expect(page.locator("#annotated-node-1")).toBeFocused();
});

test("selected invocation offers explicit destinations and hands off the modal", async ({
  page,
}) => {
  await page.locator("#explore-annotated").click();
  const invocation = page.locator(
    '#annotated-source-modal .annotated-source-segment.invocation:has-text("object")',
  ).first();
  await invocation.click({ position: { x: 8, y: 8 } });

  const member = page.locator(
    '[data-annotated-action="destination-open"][data-destination="member"]',
  );
  const source = page.locator(
    '[data-annotated-action="destination-open"][data-destination="source"]',
  );
  await expect(member).toHaveText("Member");
  await expect(source).toHaveText("Source");
  await expect(member).toHaveAttribute(
    "aria-label",
    "Open member overview for System.Object..ctor",
  );

  await source.click();
  await expect(page.locator("#annotated-source-modal")).toHaveCount(0);
  await expect(page.locator("body")).toHaveAttribute(
    "data-destination",
    "source:0",
  );
});

test("native pointer drag selects text without activating a node", async ({ page }) => {
  await page.locator("#explore-annotated").click();
  const source = page.locator(
    '#annotated-source-modal .annotated-source-segment.addressable:has-text("for")',
  ).first();
  const box = await source.boundingBox();
  if (!box) throw new Error("Addressable source has no browser geometry");

  await page.mouse.move(box.x + 3, box.y + box.height / 2);
  await page.mouse.down();
  await page.mouse.move(box.x + Math.min(box.width - 3, 55), box.y + box.height / 2, {
    steps: 8,
  });
  await page.mouse.up();

  const selected = await page.evaluate(() => window.getSelection()?.toString() ?? "");
  expect(selected.length).toBeGreaterThan(0);
  await expect(page.locator(".annotated-inspector-section").first()).toContainText(
    "Nothing selected",
  );
});

test("Escape closes detail before dismissing and restores exact focus", async ({ page }) => {
  await page.locator("#explore-annotated").click();
  await page.locator("#annotated-inspector-0").click();
  await expect(page.locator("#annotated-detail-title")).toBeFocused();

  await page.keyboard.press("Escape");
  await expect(page.locator("#annotated-detail-title")).toHaveCount(0);
  await expect(page.locator("#annotated-inspector-0")).toBeFocused();

  await page.keyboard.press("Escape");
  await expect(page.locator("#annotated-source-modal")).toHaveCount(0);
  await expect(page.locator("#explore-annotated")).toBeFocused();
});

test("closing detail reveals its exact annotation opener", async ({ page }) => {
  await page.locator("#explore-annotated").click();
  await page.addStyleTag({
    content: `
      .annotated-modal-source .annotated-source-code {
        min-height: 900px !important;
        overflow: visible !important;
      }
    `,
  });
  const source = page.locator(".annotated-modal-source");
  const chip = page.locator("#annotated-chip-modal-0-1-CSharp");
  await chip.click();
  await expect(page.locator("#annotated-detail-title")).toBeFocused();
  const scrolled = await source.evaluate(element => {
    element.scrollTop = 400;
    return element.scrollTop;
  });
  expect(scrolled).toBeGreaterThan(0);

  await page.keyboard.press("Escape");

  await expect(chip).toBeFocused();
  const visible = await source.evaluate((element, selector) => {
    const target = document.querySelector(selector);
    if (!(target instanceof HTMLElement)) return false;
    const viewport = element.getBoundingClientRect();
    const bounds = target.getBoundingClientRect();
    return bounds.bottom > viewport.top && bounds.top < viewport.bottom;
  }, "#annotated-chip-modal-0-1-CSharp");
  expect(visible).toBe(true);
});

test("pointer Close dismisses detail and modal together", async ({ page }) => {
  await page.locator("#explore-annotated").click();
  await page.locator("#annotated-inspector-0").click();
  await page.locator("#annotated-modal-close").click();

  await expect(page.locator("#annotated-source-modal")).toHaveCount(0);
  await expect(page.locator("#explore-annotated")).toBeFocused();
});

test("backdrop dismissal returns focus to Explore", async ({ page }) => {
  await page.locator("#explore-annotated").click();
  await page.locator("#annotated-source-backdrop").click({
    position: { x: 2, y: 2 },
  });

  await expect(page.locator("#annotated-source-modal")).toHaveCount(0);
  await expect(page.locator("#explore-annotated")).toBeFocused();
});

test("modal makes the background inert and traps forward and reverse Tab", async ({ page }) => {
  await page.locator("#explore-annotated").click();
  await expect(page.locator("#harness-background")).toHaveAttribute("inert", "");

  const focusable = page.locator(
    '#annotated-source-modal button:not([disabled]), '
      + '#annotated-source-modal [href], '
      + '#annotated-source-modal input:not([disabled]), '
      + '#annotated-source-modal select:not([disabled]), '
      + '#annotated-source-modal textarea:not([disabled]), '
      + '#annotated-source-modal [tabindex]:not([tabindex="-1"])',
  );
  const first = focusable.first();
  const last = focusable.last();

  await last.focus();
  await page.keyboard.press("Tab");
  await expect(first).toBeFocused();

  await first.focus();
  await page.keyboard.press("Shift+Tab");
  await expect(last).toBeFocused();
});

// Prism is bundled rather than loaded from a CDN, so this asserts the grammar registered
// and tokenized real C#. Before bundling, a test like this needed the network to pass and
// so could not be a gate at all.
test("bundled Prism tokenizes the C# source", async ({ page }) => {
  const keywords = page.locator(".annotated-source-code .token.keyword");
  await expect(keywords.first()).toHaveText("for");

  // A class the C# grammar produces and the clike grammar alone does not, so this fails if
  // the language modules are imported in the wrong order or one is dropped.
  await expect(page.locator(".annotated-source-code .token.class-name").first())
    .toBeVisible();
});

// The static check in `scripts/check-no-cross-origin-subresources.ts` reads markup, so it
// cannot see a library fetched by `import("https://cdn.example/lib.js")`. This observes
// the requests the browser actually issues, which covers that gap.
test("rendering the page issues no cross-origin requests", async ({ page }) => {
  const external: string[] = [];
  await page.route("**", route => {
    const url = new URL(route.request().url());
    if (url.origin !== new URL(page.url() || "http://127.0.0.1:4175").origin) {
      external.push(url.href);
    }
    return route.continue();
  });

  await page.reload();
  await expect(page.locator("#explore-annotated")).toBeVisible();
  await expect(page.locator(".annotated-source-code .token.keyword").first()).toBeVisible();

  expect(external).toEqual([]);
});
