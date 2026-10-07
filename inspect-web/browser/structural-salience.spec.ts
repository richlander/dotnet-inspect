import { expect, test } from "@playwright/test";
import {
  chooseInspector,
  chooseSubject,
  core,
  installFacades,
  openInstalledPlatform,
  releaseFacade,
  root,
  selectLibrary,
  surface,
} from "./library-hierarchy.support.ts";

test.use({ viewport: { width: 900, height: 900 } });

test("aggregate Type lists load icon-only cues automatically", async ({
  page,
}, testInfo) => {
  await installFacades(
    page,
    undefined,
    [],
    "ready",
    "ready",
    undefined,
    "ready",
    "ready",
    undefined,
    {
      slowStructuralSalience: true,
    },
  );
  await page.goto(root);
  await chooseSubject(page, "type", "Type");
  await expect(page.locator(".type-row .item-achievement-rail")).toHaveCount(2);
  await expect(page.locator("[data-type-leverage-filter]")).toHaveCount(0);
  await expect(page.getByRole("button", {
    name: /structural salience/i,
  })).toHaveCount(0);

  await page.locator("#type-filter-summary").click();
  await page.locator("#namespace-jump").selectOption("Example");
  await expect(page.locator("html")).toHaveAttribute(
    "data-structural-salience-request-count",
    "2",
  );
  await expect(page.locator(".type-row.surface-sea-level")).toHaveCount(1);
  await expect(page.locator(".type-row.surface-mountain-peak")).toHaveCount(1);
  await expect(page.locator(".type-row.implementation-sea-level"))
    .toHaveCount(2);
  await expect(page.locator(
    ".type-row.surface-sea-level.implementation-sea-level",
  )).toHaveCount(1);
  await expect(page.locator(
    ".type-row.surface-mountain-peak.implementation-sea-level",
  )).toHaveCount(1);
  expect(await page.locator(
    ".item-achievement-glyph.surface-sea-level",
  ).first()
    .evaluate(element => getComputedStyle(element).maskImage)).not.toBe("none");
  await expect(page.locator(".metadata-warning")).toHaveCount(0);
  await page.locator("#clear-filter").click();
  await expect(page.locator(".item-achievement-glyph.surface-sea-level"))
    .toHaveCount(1);
  await expect(page.locator(
    ".item-achievement-glyph.surface-mountain-peak",
  )).toHaveCount(1);
  await expect(page.locator(
    ".item-achievement-glyph.implementation-sea-level",
  )).toHaveCount(2);
  await expect(page.locator("html")).toHaveAttribute(
    "data-structural-salience-request-count",
    "2",
  );
  await page.locator(".type-browser").screenshot({
    path: testInfo.outputPath("type-browser-salience.png"),
  });

  await selectLibrary(page, core.id);
  await chooseSubject(page, "library", "Library");
  await chooseInspector(page, "data-library-lens", "analysis", "Analysis");
  await page.locator('[data-analysis-mode="complexity"]').click();
  await expect(page.locator(".library-complexity-surface"))
    .not.toContainText("Structural Salience");
  await page.locator('[data-analysis-mode="relationships"]').click();
  await expect(page.locator(".library-relationships-surface"))
    .not.toContainText("Structural Salience");
});

test("settled qualified structural salience stays visible without ineffective Retry", async ({
  page,
}) => {
  await installFacades(
    page,
    undefined,
    [],
    "ready",
    "ready",
    undefined,
    "ready",
    "ready",
    undefined,
    {
      qualifiedStructuralSalience: true,
    },
  );
  await page.goto(root);
  await chooseSubject(page, "type", "Type");
  await expect(page.locator("html")).toHaveAttribute(
    "data-structural-salience-request-count",
    "2",
  );
  await expect(page.locator(".data-bar-errors").filter({
    hasText: "Structural salience has qualified evidence",
  })).toHaveCount(1);
  await expect(page.locator("#type-list .type-row").first())
    .toBeInViewport({ ratio: 1 });
  await expect(page.locator("[data-type-leverage-retry]")).toHaveCount(0);
  await expect(page.locator(".type-browser-status .metadata-warning"))
    .toHaveCount(0);
  await expect(page.locator(".data-bar")).not.toContainText("CLI tool");
  expect((await page.locator(".data-bar").boundingBox())?.height).toBe(30);
  await page.locator("#type-list .type-row").last().click();
  await expect(page.locator(".data-bar-errors")).toHaveCount(0);
  await expect(page.locator(".data-bar")).toContainText("CLI tool");
  await expect(page.locator("html")).toHaveAttribute(
    "data-structural-salience-request-count", "2");
});

test("rejected ownership is useful information without Retry", async ({ page }) => {
  await installFacades(page, undefined, [], "ready", "ready", undefined,
    "ready", "ready", undefined, { rejectedOwnershipStructuralSalience: true });
  await page.goto(root);
  await chooseSubject(page, "type", "Type");
  await expect(page.locator(".data-bar-errors")).toContainText(
    "Generated-body ownership evidence was rejected.");
  await expect(page.locator("[data-type-leverage-retry]")).toHaveCount(0);
  await expect(page.locator(".type-row.surface-sea-level")).toHaveCount(1);
  await expect(page.locator(".type-row.implementation-sea-level")).toHaveCount(2);
});

for (const width of [900, 700]) {
  test(`qualified salience keeps platform Types visible with overlapping status at ${width}px`, async ({
    page,
  }) => {
    await page.setViewportSize({ width, height: 900 });
    await installFacades(
      page,
      surface,
      [],
      "ready",
      "ready",
      {
        forwarders: true,
        forwarderPending: true,
        forwarderViewPendingAfterFirst: true,
      },
      "ready",
      "ready",
      undefined,
      {
        qualifiedStructuralSalience: true,
      },
    );
    await openInstalledPlatform(page);
    await page.locator('[data-platform-library]').filter({
      has: page.getByText("System.Xml", { exact: true }),
    }).click();
    await expect(page.locator("#library-overview-title"))
      .toHaveText("System.Xml");
    await chooseSubject(page, "type", "Type");
    await expect(page.locator(".data-bar-errors").filter({
      hasText: "Structural salience has qualified evidence",
    })).toHaveCount(1);
    await page.locator("[data-platform-forwarder]").click();
    await expect(page.locator(".forwarded-type-overview [role=status]"))
      .toBeVisible();
    await chooseSubject(page, "library", "Library");
    await chooseSubject(page, "type", "Type");
    const inventoryStatus = page.locator(".type-browser-status").getByText(
      "Loading forwarded Types...",
      { exact: true },
    );
    await expect(inventoryStatus).toHaveCount(1);
    if (width < 768)
      await page.getByRole("button", { name: "Types", exact: true }).click();
    await expect(page.locator(".data-bar-errors")).toHaveCount(0);
    await expect(page.locator(".data-bar")).toContainText("CLI tool");
    await expect(inventoryStatus).toBeVisible();
    await expect(page.locator("#type-list .type-row").first())
      .toBeInViewport({ ratio: 1 });
    await releaseFacade(page, "finish-forwarder-view");
    await releaseFacade(page, "finish-forwarder");
  });
}

for (const width of [1440, 390]) {
  test(`physical-only salience stays silent and preserves inventory space at ${width}px`, async ({ page }) => {
    await page.setViewportSize({ width, height: 900 });
    await installFacades(page, undefined, [], "ready", "ready", undefined,
      "ready", "ready", undefined, { physicalOnlyStructuralSalience: true });
    await page.goto(root);
    await chooseSubject(page, "type", "Type");
    const feedback = page.locator(".data-bar-errors");
    await expect(page.locator(".type-row.implementation-sea-level")).toHaveCount(2);
    await expect(feedback).toHaveCount(0);
    await expect(page.locator(".data-bar")).toContainText("CLI tool");
    await expect(page.locator("[data-type-leverage-retry]")).toHaveCount(0);
    if (width < 768)
      await page.getByRole("button", { name: "Types", exact: true }).click();
    await expect(page.locator(".type-browser-status")).toHaveCount(0);
    await expect(page.locator("#type-list .type-row").first())
      .toBeInViewport({ ratio: 1 });
    const list = await page.locator("#type-list").boundingBox();
    const bar = await page.locator(".data-bar").boundingBox();
    expect(bar?.height).toBe(30);
    expect(list).not.toBeNull();
    expect(list!.height).toBeGreaterThan(500);
    expect(list!.y + list!.height).toBeLessThanOrEqual(bar!.y);
    await page.locator("#type-list .type-row").last().click();
    await expect(feedback).toHaveCount(0);
    await expect(page.locator(".data-bar")).toContainText("CLI tool");
    if (width < 768)
      await page.getByRole("button", { name: "Types", exact: true }).click();
    const clearedList = await page.locator("#type-list").boundingBox();
    expect(clearedList?.height).toBe(list!.height);
    await page.keyboard.press("Alt+ArrowLeft");
    await expect(feedback).toHaveCount(0);
    await page.keyboard.press("Alt+ArrowRight");
    await expect(feedback).toHaveCount(0);
    await expect(page.locator("html")).toHaveAttribute(
      "data-structural-salience-request-count", "2");
  });
}

test("failed salience acquisition uses the data bar and retries", async ({ page }) => {
  await installFacades(page, undefined, [], "ready", "ready", undefined,
    "ready", "ready", undefined, { failedStructuralSalience: true });
  await page.goto(root);
  await chooseSubject(page, "type", "Type");
  await expect(page.locator(".data-bar-errors")).toContainText(
    "Structural salience is unavailable: Structural evidence acquisition failed.");
  await expect(page.locator(".type-browser-status")).toHaveCount(0);
  await expect(page.locator("#type-list .type-row").first()).toBeInViewport({ ratio: 1 });
  await page.locator("[data-type-leverage-retry]").click();
  await expect(page.locator("html")).toHaveAttribute(
    "data-structural-salience-request-count", "4");
});


test("routine physical-only coverage never replaces the databar", async ({ page }) => {
  await installFacades(page, undefined, [], "ready", "ready", undefined,
    "ready", "ready", undefined, { physicalOnlyStructuralSalience: true });
  await page.goto(root);
  await chooseSubject(page, "type", "Type");
  await expect(page.locator("html")).toHaveAttribute(
    "data-structural-salience-request-count", "2");
  await expect(page.locator(".type-row.implementation-sea-level")).toHaveCount(2);
  await expect(page.locator(".data-bar-errors")).toHaveCount(0);
  await expect(page.locator(".data-bar")).toContainText("CLI tool");
  await expect(page.locator("[data-type-leverage-retry]")).toHaveCount(0);
  const cues = await page.locator(".item-achievement-glyph").count();
  await chooseInspector(page, "data-lens", "metadata", "Metadata");
  await expect(page.locator(".data-bar-errors")).toHaveCount(0);
  await expect(page.locator(".item-achievement-glyph")).toHaveCount(cues);
  await chooseInspector(page, "data-lens", "api", "API");
  await expect(page.locator(".data-bar-errors")).toHaveCount(0);
  await expect(page.locator("html")).toHaveAttribute(
    "data-structural-salience-request-count", "2");
});
