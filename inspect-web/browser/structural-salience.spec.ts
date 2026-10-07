import { expect, test } from "@playwright/test";
import { renderMemberNav, type MemberGroup } from "../src/type-panel.ts";
import type { ItemAchievement } from "../src/item-achievements.ts";
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

test("Member and overload achievements retain three visible horizontal slots", async ({ page }) => {
  const group: MemberGroup = {
    key: "method:Run",
    name: "Run",
    kind: "method",
    overloads: [
      { signature: "public void Run(int value)", parameters: [{ type: "int" }] },
      { signature: "public void Run(string value)", parameters: [{ type: "string" }] },
    ],
  };
  const achievements: ItemAchievement[] = [
    { kind: "top-leverage", description: "Top Leverage" },
    { kind: "api-diff", description: "API differences" },
    { kind: "implementation-hub", description: "Implementation Hub" },
  ];
  const html = renderMemberNav({
    type: surface.types[0]!,
    entries: [
      { kind: "member", group },
      { kind: "overload", group, index: 0 },
      { kind: "overload", group, index: 1 },
    ],
    memberCount: 2,
    visibleMemberCount: 2,
    filterControlsHtml: "",
    selectedMemberKey: group.key,
    selectedOverloadIndex: 1,
    escapeHtml: String,
    typeDisplayName: type => type.name,
    shortKind: String,
    highlight: String,
    memberAchievements: (_group, index) => index === null
      ? achievements
      : achievements.slice(0, 2),
    overloadHeat: () => ({ heatStrength: null, hub: true, description: "Implementation Hub" }),
  });
  await page.goto("/browser/annotated-source.html");
  await page.setContent(`<!doctype html>
    <link rel="stylesheet" href="/src/styles.css">
    ${html}`);
  const rows = page.locator(".member-row, .overload-nav-row");
  await expect(rows).toHaveCount(3);
  for (const row of await rows.all()) {
    const glyphs = row.locator(".item-achievement-glyph");
    await expect(glyphs).toHaveCount(3);
    const bounds = await row.boundingBox();
    const boxes = await Promise.all((await glyphs.all()).map(glyph => glyph.boundingBox()));
    expect(bounds).not.toBeNull();
    expect(boxes.every(box => box !== null)).toBe(true);
    for (let index = 0; index < boxes.length; index++) {
      const box = boxes[index]!;
      expect(box.height).toBe(16);
      expect(box.y).toBe(boxes[0]!.y);
      expect(box.y).toBeGreaterThanOrEqual(bounds!.y);
      expect(box.y + box.height).toBeLessThanOrEqual(bounds!.y + bounds!.height);
      if (index > 0)
        expect(box.x).toBeGreaterThanOrEqual(boxes[index - 1]!.x + 16);
    }
  }
});

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
  await expect(page.locator(".type-row.surface-sea-level")).toHaveCount(0);
  await expect(page.locator(".type-row.surface-mountain-peak")).toHaveCount(0);
  await expect(page.locator(".type-row.implementation-sea-level"))
    .toHaveCount(2);
  const rails = page.locator(".type-row .item-achievement-rail");
  for (const rail of await rails.all()) {
    await expect(rail.locator(".item-achievement-glyph")).toHaveCount(1);
    await expect(rail).toHaveAccessibleName(/surface.*implementation/);
    const geometry = await rail.locator(".item-achievement-glyph").evaluate(element => ({
      column: getComputedStyle(element).gridColumnStart,
      width: getComputedStyle(element.parentElement!).width,
      mask: getComputedStyle(element).maskImage,
    }));
    expect(geometry.column).toBe("2");
    expect(geometry.width).toBe("34px");
    expect(geometry.mask).not.toBe("none");
  }
  await expect(page.locator(".metadata-warning")).toHaveCount(0);
  await page.locator("#clear-filter").click();
  await expect(page.locator(".item-achievement-glyph.surface-sea-level"))
    .toHaveCount(0);
  await expect(page.locator(
    ".item-achievement-glyph.surface-mountain-peak",
  )).toHaveCount(0);
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
  await expect(page.locator(".type-row.surface-sea-level")).toHaveCount(0);
  await expect(page.locator(".type-row.implementation-sea-level")).toHaveCount(2);
  await expect(page.locator(".type-row .item-achievement-rail").first())
    .toHaveAccessibleName(/surface.*implementation/);
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
