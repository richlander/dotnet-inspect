import { expect, test } from "@playwright/test";
import {
  chooseSubject,
  installFacades,
  openInstalledPlatform,
  releaseFacade,
  surface,
} from "./library-hierarchy.support.ts";

test.use({ viewport: { width: 1440, height: 900 } });

async function openXml(
  page: Parameters<typeof installFacades>[0],
  options: {
    forwarderInternalType?: boolean;
    forwarderFailure?: boolean;
    forwarderPending?: boolean;
  } = {},
) {
  await installFacades(page, surface, [], "ready", "ready", {
    forwarders: true, ...options,
  });
  await openInstalledPlatform(page);
  await page.locator('[data-platform-library]').filter({
    has: page.getByText("System.Xml", { exact: true }),
  }).click();
  await expect(page.locator("#library-overview-title")).toHaveText("System.Xml");
  await expect(page.locator(".overview-identity-detail").filter({ hasText: "Facade assembly" }))
    .toBeVisible();
  const row = page.locator('[data-type="System.Xml:System.Xml.XmlReader"]');
  await expect(row).toContainText("Forwarded");
  await row.click();
  await expect(page.locator("#forwarded-type-title")).toHaveText("System.Xml.XmlReader");
  await expect(page.locator('[data-inspector-tab][data-lens="overview"]')).toHaveAttribute("aria-selected", "true");
  await expect(page.locator('[data-lens="metadata"]')).toHaveCount(0);
  await expect(page.locator('[data-lens="api"]')).toHaveCount(0);
  await expect(page.locator('[data-inspector-tab][data-lens="source"]')).toHaveCount(0);
}

// PR-fast: production UI over deterministic responses for the real XML route.
test("XML forwarders open each immediate Library and restore fresh history actions", async ({ page }) => {
  await openXml(page);
  const firstAction = await page.locator("html").getAttribute("data-forwarder-view");
  await page.locator("[data-platform-forwarder]").click();
  await expect(page.locator("[data-platform-forwarder]")).toHaveText("System.Private.Xml");
  await expect(page.locator("#forwarded-type-title")).toBeFocused();
  await expect(page.locator('[data-inspector-tab][data-lens="source"]')).toHaveCount(0);
  await page.locator("[data-platform-forwarder]").click();
  await expect(page.locator('[data-inspector-tab][data-lens="api"]')).toHaveAttribute("aria-selected", "true");
  await expect(page.locator('[data-inspector-tab][data-lens="source"]')).toBeVisible();
  await expect(page.locator("[data-platform-forwarder]")).toHaveCount(0);
  await expect(page.locator('[data-type="System.Private.Xml:System.Xml.XmlReader"]')).toBeVisible();
  await expect(page.locator("html")).toHaveAttribute(
    "data-platform-type-member-population-request",
    /"System.Xml.XmlReader","csharp","public"\]$/,
  );
  await page.locator("#nav-back").click();
  await expect(page.locator("[data-platform-forwarder]")).toHaveText("System.Private.Xml");
  await page.locator("#nav-back").click();
  await expect(page.locator("[data-platform-forwarder]")).toHaveText("System.Xml.ReaderWriter");
  expect(await page.locator("html").getAttribute("data-forwarder-view")).not.toBe(firstAction);
  await page.reload();
  await expect(page.locator("#forwarded-type-title")).toHaveText("System.Xml.XmlReader");
  await expect(page.locator("[data-platform-forwarder]")).toHaveText("System.Xml.ReaderWriter");
});

// PR-fast: copies through both forwarding hops and the ordinary defining Type.
test("XML forwarded and defining Type subjects display and copy the same qualified name", async ({ page }) => {
  await openXml(page);
  await page.evaluate(() => {
    Object.defineProperty(navigator, "clipboard", {
      configurable: true,
      value: {
        writeText: async (text: string) => {
          document.documentElement.dataset.copiedTypeName = text;
        },
      },
    });
  });
  for (const destination of ["System.Xml.ReaderWriter", "System.Private.Xml", null]) {
    const copyType = page.getByRole("button", {
      name: "Copy type name System.Xml.XmlReader", exact: true,
    });
    await expect(copyType).toHaveText("System.Xml.XmlReader");
    await page.evaluate(() => { delete document.documentElement.dataset.copiedTypeName; });
    await copyType.click();
    await expect(page.locator("html"))
      .toHaveAttribute("data-copied-type-name", "System.Xml.XmlReader");
    if (destination) {
      await page.getByRole("button", { name: destination, exact: true }).click();
      await expect(page.locator(`[data-type="${destination}:System.Xml.XmlReader"]`))
        .toHaveAttribute("aria-selected", "true");
    } else {
      await expect(page.locator('[data-inspector-tab][data-lens="api"]'))
        .toHaveAttribute("aria-selected", "true");
    }
  }
});

test("unavailable forwarding preserves subject, location and actionable focus", async ({ page }) => {
  await openXml(page, { forwarderFailure: true });
  const source = page.url();
  await page.locator("[data-platform-forwarder]").click();
  await expect(page.locator(".forwarded-type-overview [role=alert]"))
    .toContainText("unavailable");
  await expect(page.locator("#forwarded-type-title")).toHaveText("System.Xml.XmlReader");
  await expect(page.locator("[data-platform-forwarder]")).toBeFocused();
  await expect(page).toHaveURL(source);
});

test("namespace filtering includes forwarded-only namespaces and follows the selected Library", async ({ page }) => {
  await openXml(page);
  await page.locator("[data-type-filter-disclosure] > summary").click();
  const namespace = page.getByRole("combobox", { name: "Filter by namespace" });
  await expect(namespace.locator('option[value="System.Xml"]'))
    .toHaveText("System.Xml · 1");
  await namespace.selectOption("System.Xml");
  await expect(namespace).toHaveValue("System.Xml");
  await expect(page.locator('[data-type="System.Xml:System.Xml.XmlReader"]'))
    .toBeVisible();
  await expect(page.locator("#forwarded-type-title")).toHaveText("System.Xml.XmlReader");
  await page.locator("[data-platform-forwarder]").click();
  await expect(page.locator("[data-platform-forwarder]")).toHaveText("System.Private.Xml");
  await page.locator("[data-platform-forwarder]").click();
  await expect(page.locator('[data-type="System.Private.Xml:System.Xml.XmlReader"]'))
    .toBeVisible();
  await expect(namespace.locator('option[value="System.Xml"]'))
    .toHaveText("System.Xml · 1");
});

test("selected forwarded Kind remains visible when Accessibility excludes forwarders", async ({
  page,
}) => {
  await openXml(page, { forwarderInternalType: true });
  await page.locator("[data-type-filter-disclosure] > summary").click();
  const kind = page.getByRole("combobox", { name: "Type kind" });
  const accessibility =
    page.getByRole("combobox", { name: "Type accessibility" });

  await kind.selectOption("forwarded");
  await expect(page.locator(
    '[data-type="System.Xml:System.Xml.XmlReader"]',
  )).toBeVisible();
  await accessibility.selectOption("internal");

  await expect(kind).toHaveValue("forwarded");
  await expect(kind.locator('option[value="forwarded"]'))
    .toHaveText("forwarded · 0");
  await expect(page.locator("#type-list [data-type]")).toHaveCount(0);

  await kind.selectOption("");
  await expect(page.locator(
    '[data-type="System.Xml:Hidden.InternalType"]',
  )).toBeVisible();
});

test("Library scope controls and keyboard selection retain a forwarded Type", async ({ page }) => {
  await openXml(page);
  await chooseSubject(page, "library", "Library");
  await chooseSubject(page, "type", "Type");
  await expect(page.locator("#forwarded-type-title")).toHaveText("System.Xml.XmlReader");
  await chooseSubject(page, "library", "Library");
  await page.reload();
  await expect(page.locator("#library-overview-title")).toHaveText("System.Xml");
  await chooseSubject(page, "type", "Type");
  await expect(page.locator("#forwarded-type-title")).toHaveText("System.Xml.XmlReader");
  await chooseSubject(page, "library", "Library");
  await page.locator("#type-list").press("ArrowDown");
  await page.locator("#type-list").press("Enter");
  await expect(page.locator("#forwarded-type-title")).toHaveText("System.Xml.XmlReader");
});

test("leaving a pending forwarder cannot replace the newer Library subject", async ({ page }) => {
  await openXml(page, { forwarderPending: true });
  const retired = await page.locator("html").getAttribute("data-forwarder-view");
  await page.locator("[data-platform-forwarder]").click();
  await expect(page.locator(".forwarded-type-overview [role=status]")).toBeVisible();
  await chooseSubject(page, "library", "Library");
  await releaseFacade(page, "finish-forwarder");
  await expect(page.locator("#library-overview-title")).toHaveText("System.Xml");
  await expect(page.locator("[data-platform-forwarder]")).toHaveCount(0);
  await expect.poll(() => page.locator("html").getAttribute("data-forwarder-view"))
    .not.toBe(retired);
  await page.locator('[data-type="System.Xml:System.Xml.XmlReader"]').click();
  await expect(page.locator("[data-platform-forwarder]")).toBeEnabled();
  await page.locator("[data-platform-forwarder]").click();
  await releaseFacade(page, "finish-forwarder");
  await expect(page.locator("[data-platform-forwarder]")).toHaveText("System.Private.Xml");
});

test("leaving and returning does not revive a superseded forwarding action", async ({ page }) => {
  await openXml(page, { forwarderPending: true });
  const retired = await page.locator("html").getAttribute("data-forwarder-view");
  await page.locator("[data-platform-forwarder]").click();
  await expect(page.locator(".forwarded-type-overview [role=status]")).toBeVisible();
  await chooseSubject(page, "library", "Library");
  await chooseSubject(page, "type", "Type");
  await expect.poll(() => page.locator("html").getAttribute("data-forwarder-view"))
    .not.toBe(retired);
  await releaseFacade(page, "finish-forwarder");
  await expect(page.locator("[data-platform-forwarder]")).toHaveText("System.Xml.ReaderWriter");
  await expect(page.locator("[data-platform-forwarder]")).toBeEnabled();
});
