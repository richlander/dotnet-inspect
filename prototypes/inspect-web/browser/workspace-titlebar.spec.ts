import { expect, test, type Page } from "@playwright/test";

async function box(page: Page, selector: string) {
  const value = await page.locator(selector).boundingBox();
  expect(value).not.toBeNull();
  return value!;
}

async function slideAfter(page: Page, selector: string) {
  await page.locator(selector).evaluate(element => {
    element.dispatchEvent(new WheelEvent("wheel", {
      bubbles: true,
      cancelable: true,
      deltaY: 100,
    }));
  });
}

test("the title bar contains the inspected target without tab-like workspace identity", async ({
  page,
}) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  await page.goto("/browser/workspace-titlebar.html?package=1");

  const titleNavigation = await box(page, ".title-navigation");
  const search = await box(page, "#open-search");
  const forward = await box(page, "#nav-forward");

  expect(titleNavigation.x + titleNavigation.width).toBeCloseTo(1440, 0);
  expect(forward.x + forward.width).toBeLessThanOrEqual(search.x);
  expect(search.x + search.width).toBeCloseTo(1440, 0);
  await expect(page.locator(".titlebar .inspected-target")).toBeVisible();
  await expect(page.locator(".titlebar .subject-path-segment.root"))
    .toHaveText("System.Text.Json");
  await expect(page.locator(".titlebar #open-search")).toBeVisible();
  await expect(page.locator(".titlebar .nav-history")).toBeVisible();
  await expect(page.locator(".titlebar #share")).toHaveCount(0);
  await expect(page.locator(".titlebar #open-settings")).toHaveCount(0);
  await expect(page.locator(".titlebar #help")).toHaveCount(0);
  await expect(page.locator(".subject-zone #share")).toBeVisible();
  await expect(page.locator(".subject-zone #open-settings")).toBeVisible();
  await expect(page.locator(".subject-zone #help")).toBeVisible();
  await expect(page.locator(".shell-actions > button").last())
    .toHaveAttribute("id", "help");
  await expect(page.locator(".workspace-title")).toHaveCount(0);
  await expect(page.locator(".titlebar")).not.toContainText("0:");
  await expect(page.locator(".titlebar")).not.toContainText("Platform");
  await expect(page.locator(".workspace-window")).toHaveCount(0);
  await expect(page.locator(".brand-icon img")).toHaveAttribute(
    "src",
    "/assets/dotnet-inspect-bot.png");
});

test("the inspected target precedes title navigation and package selectors stay in content", async ({
  page,
}) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  await page.goto("/browser/workspace-titlebar.html?package=1");

  const brand = await box(page, ".brand");
  const target = await box(page, ".inspected-target");
  const lensbar = await box(page, ".lensbar");
  const subjectZone = await box(page, ".subject-zone");

  expect(target.x).toBeGreaterThanOrEqual(brand.x + brand.width);
  expect(target.y).toBeCloseTo(brand.y, 0);
  expect(lensbar.y).toBeCloseTo(subjectZone.y, 0);
  await expect(page.locator(".titlebar .lensbar")).toHaveCount(0);
  await expect(page.locator(".subject-zone .lensbar")).toBeVisible();
  await expect(page.locator(".titlebar #package-version")).toHaveCount(0);
  await expect(page.locator(".titlebar #framework")).toHaveCount(0);
  await expect(page.locator(".detail-scroll #package-version")).toBeVisible();
  await expect(page.locator(".detail-scroll #framework")).toBeVisible();
  await expect(page.locator("#go-home")).toHaveCount(0);

  const packageIcon = await box(page, ".subject-icon");
  expect(packageIcon.width).toBeCloseTo(20, 0);
  expect(packageIcon.height).toBeCloseTo(20, 0);
  await expect(page.locator(".subject-icon img")).toHaveAttribute(
    "src",
    /^data:image\/png;base64,/);
  await expect(page.locator(".subject-icon img")).toHaveJSProperty(
    "naturalWidth",
    456);
});

test("keyboard tab activation preserves focus across shell replacement", async ({
  page,
}) => {
  await page.goto("/browser/workspace-titlebar.html");

  const api = page.getByRole("tab", { name: "API" });
  const metadata = page.getByRole("tab", { name: "Metadata" });
  await api.focus();
  await page.keyboard.press("ArrowRight");
  await expect(metadata).toBeFocused();
  await expect(metadata).toHaveAttribute("aria-selected", "false");
  await page.evaluate(() => window.rerenderScopeBarProbe());
  await expect(api).toHaveAttribute("aria-selected", "true");
  await expect(api).toHaveAttribute("tabindex", "-1");
  await expect(metadata).toHaveAttribute("aria-selected", "false");
  await expect(metadata).toHaveAttribute("tabindex", "0");
  await expect(metadata).toBeFocused();
  await page.keyboard.press("Enter");
  await expect(metadata).toHaveAttribute("aria-selected", "true");
  await expect(metadata).toBeFocused();

  const type = page.getByRole("tab", { name: "Type" });
  const packageSubject = page.getByRole("tab", { name: "Package" });
  await type.focus();
  await page.keyboard.press("ArrowLeft");
  await expect(packageSubject).toBeFocused();
  await expect(packageSubject).toHaveAttribute("aria-selected", "false");
  await page.keyboard.press("Enter");
  await expect(packageSubject).toHaveAttribute("aria-selected", "true");
  await expect(packageSubject).toBeFocused();
});

test("removed focused tabs fall back to the persistent shell control", async ({
  page,
}) => {
  await page.goto("/browser/workspace-titlebar.html");

  const metadata = page.getByRole("tab", { name: "Metadata" });
  await metadata.focus();
  await page.evaluate(() => window.renderPackageScopeProbe());

  await expect(page.locator(".brand")).toBeFocused();
  await expect(page.locator(".slide-strip-inspector")).toHaveAttribute(
    "data-initial-anchor",
    "overview");
});

test("replaced allocation controls park focus on the persistent shell", async ({
  page,
}) => {
  await page.setViewportSize({ width: 760, height: 900 });
  await page.goto("/browser/workspace-titlebar.html?member=1");

  await page.locator("[data-more-subjects]").focus();
  await page.evaluate(() => window.rerenderScopeBarProbe());

  await expect(page.locator(".brand")).toBeFocused();
});

test("packages without an embedded icon use NuGet's package fallback", async ({
  page,
}) => {
  await page.goto("/browser/workspace-titlebar.html?package=1&fallback=1");

  await expect(page.locator(".subject-icon img")).toHaveAttribute(
    "src",
    "https://nuget.org/Content/gallery/img/default-package-icon-256x256.png");
  await expect(page.locator(".subject-icon")).not.toContainText("⬡");
});

test("right-side actions yield from labels to arrows to nothing", async ({
  page,
}) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  await page.goto("/browser/workspace-titlebar.html?member=1");
  await expect(page.locator(".title-search-label-full")).toBeVisible();
  await expect(page.locator(".title-search-label-compact")).toBeHidden();

  await page.goto("/browser/workspace-titlebar.html?member=1&long=1");
  await expect(page.locator("#open-search")).toBeHidden();
  await expect(page.locator(".title-navigation .nav-history")).toBeVisible();

  for (const width of [800, 761]) {
    await page.setViewportSize({ width, height: 900 });
    await page.goto("/browser/workspace-titlebar.html?member=1");
    await expect(
      page.locator(".slide-strip-inspector .lens-label").first(),
    ).toBeVisible();
    expect(await page.evaluate(() =>
      document.documentElement.scrollWidth
      - document.documentElement.clientWidth)).toBeLessThanOrEqual(0);
  }

  await page.setViewportSize({ width: 760, height: 900 });
  await page.goto("/browser/workspace-titlebar.html?member=1");

  const titlebar = await box(page, ".titlebar");
  const subjectZone = await box(page, ".subject-zone");

  await expect(page.locator("#package-version")).toHaveCount(0);
  await expect(page.locator("#framework")).toHaveCount(0);
  await expect(page.locator("#open-search")).toBeVisible();
  await expect(page.locator(".title-search-label-full")).toBeHidden();
  await expect(page.locator(".title-search-label-compact"))
    .toHaveText("Search");
  await expect(page.locator(".title-search-label-compact")).toBeVisible();
  await expect(page.locator(".title-navigation .nav-history")).toBeVisible();
  const inspectorStrip = page.locator(".slide-strip-inspector");
  await expect(inspectorStrip).toHaveAttribute("data-mode", "label");
  await expect(
    inspectorStrip.locator("[data-inspector-tab]:not([hidden])"),
  ).toHaveCount(4);
  const callGraph = page.getByRole("tab", { name: "Call graph" });
  await expect(callGraph).toBeVisible();
  await expect(callGraph).toHaveAttribute("aria-selected", "false");
  const overview = page.getByRole("tab", { name: "Overview" });
  await expect(overview).toHaveAttribute("aria-selected", "true");
  await expect(overview).toHaveAttribute("aria-controls", "inspector-panel");
  await expect(page.locator("#inspector-panel")).toHaveAttribute(
    "aria-labelledby",
    "active-inspector-tab");
  const subjectTabs = page.locator(".scope-switch [data-subject-tab]");
  await expect(subjectTabs).toHaveCount(4);
  expect(await subjectTabs.evaluateAll(tabs =>
    tabs.every(tab => tab.getAttribute("aria-controls") === "subject-panel")))
    .toBe(true);
  await expect(page.locator("#subject-panel")).toHaveAttribute(
    "aria-labelledby",
    "active-subject-tab");
  await expect(page.locator(".scope-switch [tabindex='0']")).toHaveCount(1);
  const memberSubject = page.locator('[data-scope="member"]');
  await expect(memberSubject).toHaveAttribute("aria-selected", "true");
  await memberSubject.focus();
  await page.keyboard.press("ArrowLeft");
  await expect(page.locator('[data-scope="type"]')).toBeFocused();
  await expect(memberSubject).toHaveAttribute("aria-selected", "true");
  await overview.focus();
  await page.keyboard.press("ArrowRight");
  await expect(callGraph).toBeFocused();
  await expect(callGraph).toHaveAttribute("aria-selected", "false");
  await expect(overview).toHaveAttribute("aria-selected", "true");
  await expect(page.locator("#go-home")).toHaveCount(0);
  await expect(page.locator(".subject-path-segment")).toHaveText([
    "System.Text.Json",
    "System.Text.Json.JsonSerializer",
    "DeserializeSync",
  ]);
  await expect(page.locator(".titlebar .subject-path")).toBeVisible();
  await expect(page.locator(".subject-zone .subject-path")).toHaveCount(0);
  await expect(page.locator(".subject-zone .scope-switch")).toBeVisible();
  await expect(page.locator(".subject-zone #share")).toBeVisible();
  await expect(page.locator(".subject-zone #open-settings")).toBeVisible();
  await expect(page.locator(".subject-zone #help")).toBeVisible();
  await expect(page.locator("#copy-name")).toHaveCount(0);
  await expect(page.locator("#taste-btn")).toHaveCount(0);
  expect(titlebar.y).toBeLessThan(subjectZone.y);
  expect(subjectZone.x).toBe(0);
  expect(subjectZone.x + subjectZone.width).toBeCloseTo(760, 0);

  await page.setViewportSize({ width: 650, height: 900 });
  await expect(page.locator("#open-search")).toBeHidden();
  expect(await page.evaluate(() => window.focusWorkbenchSearchProbe()))
    .toBe(false);
  await expect(page.locator(".title-navigation .nav-history")).toBeVisible();
  await expect(page.locator("#share")).toBeVisible();
  await expect(page.locator("#open-settings")).toBeVisible();
  await expect(page.locator("#help")).toBeVisible();

  await page.setViewportSize({ width: 560, height: 900 });
  await expect(page.locator(".title-navigation .nav-history")).toBeHidden();
  await expect(page.locator("#open-settings")).toBeVisible();
  await expect(page.locator("#help")).toBeVisible();

  await page.setViewportSize({ width: 480, height: 900 });
  await expect(page.locator("#help")).toBeVisible();
  await expect(inspectorStrip).toHaveAttribute("data-mode", "label");
  await expect(
    inspectorStrip.locator(
      '[data-inspector-tab]:not([hidden]) [data-slide-strip-representation="label"]',
    ),
  ).toHaveCount(2);
  expect(await inspectorStrip.locator(
    '[data-inspector-tab]:not([hidden]) [data-slide-strip-representation="short-label"]',
  ).evaluateAll(labels =>
    labels.every(label => getComputedStyle(label).display === "none")))
    .toBe(true);

  await page.setViewportSize({ width: 400, height: 900 });
  await expect(page.locator("#share")).toBeHidden();
  await expect(page.locator("#open-settings")).toBeVisible();
  await expect(page.locator("#help")).toBeVisible();
  const horizontalOverflow = await page.evaluate(() =>
    document.documentElement.scrollWidth - document.documentElement.clientWidth);
  expect(horizontalOverflow).toBeLessThanOrEqual(0);
  const narrowNamespacePicker = await box(page, ".namespace-picker");
  const narrowTypeList = await box(page, ".type-list");
  expect(narrowNamespacePicker.y + narrowNamespacePicker.height)
    .toBeLessThanOrEqual(narrowTypeList.y);
});

test("SlideStrip slides one uniform window without stealing external focus", async ({
  page,
}) => {
  await page.setViewportSize({ width: 560, height: 900 });
  await page.goto("/browser/workspace-titlebar.html?member=1");

  const inspector = page.locator(".slide-strip-inspector");
  await expect(inspector).toHaveAttribute("data-mode", "label");
  await expect(
    inspector.locator(
      '[data-inspector-tab]:not([hidden]) [data-slide-strip-representation="label"]',
    ),
  ).toHaveText(["Overview", "Call graph"]);
  await expect(inspector.locator("[data-slide-strip-before]")).toBeHidden();
  await expect(inspector.locator("[data-slide-strip-after]")).toBeVisible();

  const help = page.locator("#help");
  await help.focus();
  await slideAfter(page, ".slide-strip-inspector");
  await expect(help).toBeFocused();
  await expect(
    inspector.locator(
      '[data-inspector-tab]:not([hidden]) [data-slide-strip-representation="label"]',
    ),
  ).toHaveText(["Call graph", "Facts"]);
  await expect(inspector.locator("[data-slide-strip-before]")).toBeVisible();
  await expect(inspector.locator("[data-slide-strip-after]")).toBeVisible();

  const callGraph = page.getByRole("tab", { name: "Call graph" });
  await callGraph.focus();
  await page.keyboard.press("ArrowRight");
  const facts = page.getByRole("tab", { name: "Facts" });
  await expect(facts).toBeFocused();
  await expect(facts).toHaveAttribute("aria-selected", "false");
  await expect(page.locator('[data-member-section="overview"]'))
    .toHaveAttribute("aria-selected", "true");
  await page.keyboard.press("End");
  const annotated = page.getByRole("tab", { name: "Annotated source" });
  await expect(annotated).toBeFocused();
  await expect(annotated).toBeVisible();
  await expect(inspector).toHaveAttribute("data-fallback", "false");
});

test("allocation controls move between adjacent stable result pairs", async ({
  page,
}) => {
  await page.setViewportSize({ width: 760, height: 900 });
  await page.goto("/browser/workspace-titlebar.html?member=1");

  const subject = page.locator(".slide-strip-subject");
  const inspector = page.locator(".slide-strip-inspector");
  const moreSubjects = page.locator("[data-more-subjects]");
  await expect(moreSubjects).toHaveAttribute("aria-disabled", "false");
  await expect(
    subject.locator(
      '[data-subject-tab]:not([hidden]) [data-slide-strip-representation="label"]',
    ),
  ).toHaveText(["Type", "Member"]);
  await expect(
    inspector.locator(
      '[data-inspector-tab]:not([hidden]) [data-slide-strip-representation="label"]',
    ),
  ).toHaveText(["Overview", "Call graph", "Facts", "Source"]);

  await moreSubjects.click();
  await expect(moreSubjects).toBeFocused();
  await expect(
    subject.locator(
      '[data-subject-tab]:not([hidden]) [data-slide-strip-representation="label"]',
    ),
  ).toHaveText(["Package", "Type", "Member"]);
  await expect(
    inspector.locator(
      '[data-inspector-tab]:not([hidden]) [data-slide-strip-representation="label"]',
    ),
  ).toHaveText(["Overview", "Call graph", "Facts"]);
});

test("every allocation level strictly trades subject for inspector richness", async ({
  page,
}) => {
  const modeOrder = ["label", "short-label", "icon", "index"];
  for (const width of [600, 760]) {
    await page.setViewportSize({ width, height: 900 });
    await page.goto("/browser/workspace-titlebar.html?member=1");
    await page.getByRole("tab", { name: "Overview" }).click();

    const subject = page.locator(".slide-strip-subject");
    const inspector = page.locator(".slide-strip-inspector");
    const moreSubjects = page.locator("[data-more-subjects]");
    const levels: {
      subjectCount: number;
      inspectorMode: number;
      inspectorCount: number;
    }[] = [];
    for (let attempt = 0; attempt < 10; attempt++) {
      const mode = await inspector.getAttribute("data-mode");
      levels.push({
        subjectCount: await subject.locator(
          "[data-subject-tab]:not([hidden])",
        ).count(),
        inspectorMode: modeOrder.indexOf(mode ?? ""),
        inspectorCount: await inspector.locator(
          "[data-inspector-tab]:not([hidden])",
        ).count(),
      });
      if (await moreSubjects.getAttribute("aria-disabled") === "true") break;
      await moreSubjects.click();
    }

    await expect(moreSubjects).toHaveAttribute("aria-disabled", "true");
    expect(levels.length).toBeGreaterThan(1);
    for (let index = 1; index < levels.length; index++) {
      const previous = levels[index - 1];
      const current = levels[index];
      if (!previous || !current) {
        throw new Error("Allocation ladder snapshot is incomplete.");
      }
      expect(current.subjectCount).toBeGreaterThan(previous.subjectCount);
      expect(
        current.inspectorMode > previous.inspectorMode
        || (current.inspectorMode === previous.inspectorMode
          && current.inspectorCount < previous.inspectorCount),
      ).toBe(true);
    }
  }
});

test("temporary pressure does not discard the retained allocation", async ({
  page,
}) => {
  await page.setViewportSize({ width: 760, height: 900 });
  await page.goto("/browser/workspace-titlebar.html?member=1");

  const moreSubjects = page.locator("[data-more-subjects]");
  for (let attempt = 0; attempt < 10; attempt++) {
    if (await moreSubjects.getAttribute("aria-disabled") === "true") break;
    await moreSubjects.click();
  }
  await expect(moreSubjects).toHaveAttribute("aria-disabled", "true");

  await page.setViewportSize({ width: 1440, height: 900 });
  await page.setViewportSize({ width: 760, height: 900 });

  await expect(moreSubjects).toHaveAttribute("aria-disabled", "true");
});

test("manual windows survive resize and reset with inspector inventory", async ({
  page,
}) => {
  await page.setViewportSize({ width: 560, height: 900 });
  await page.goto("/browser/workspace-titlebar.html?member=1");

  const inspector = page.locator(".slide-strip-inspector");
  const visibleLabels = () => inspector.locator(
    '[data-inspector-tab]:not([hidden]) [data-slide-strip-representation="label"]',
  );
  const help = page.locator("#help");
  await help.focus();
  await slideAfter(page, ".slide-strip-inspector");
  await expect(visibleLabels()).toHaveText(["Call graph", "Facts"]);
  await expect(help).toBeFocused();
  await expect(page.locator('[data-member-section="call-graph"]'))
    .toHaveAttribute("tabindex", "0");
  await expect(page.locator('[data-member-section="overview"]'))
    .toHaveAttribute("tabindex", "-1");

  await page.setViewportSize({ width: 800, height: 900 });
  await expect(visibleLabels()).toHaveCount(5);
  await page.setViewportSize({ width: 560, height: 900 });
  await expect(visibleLabels()).toHaveCount(2);
  await expect(page.locator('[data-member-section="overview"]')).toBeHidden();
  await expect(inspector.locator("[data-slide-strip-before]")).toBeVisible();

  const memberSubject = page.locator('[data-scope="member"]');
  await memberSubject.focus();
  await page.keyboard.press("ArrowLeft");
  const typeSubject = page.locator('[data-scope="type"]');
  await expect(typeSubject).toBeFocused();
  await page.keyboard.press("Enter");
  await expect(typeSubject).toHaveAttribute("aria-selected", "true");
  await expect(page.locator(".slide-strip-inspector")).toHaveAttribute(
    "data-mode",
    "label");
  await expect(
    page.locator(
      '.slide-strip-inspector [data-inspector-tab]:not([hidden]) [data-slide-strip-representation="label"]',
    ),
  ).toHaveText(["API", "Metadata", "Source"]);
});

test("removing a focused allocation control transfers focus before removal", async ({
  page,
}) => {
  await page.setViewportSize({ width: 760, height: 900 });
  await page.goto("/browser/workspace-titlebar.html?member=1");

  const moreSubjects = page.locator("[data-more-subjects]");
  await moreSubjects.focus();
  await expect(moreSubjects).toBeFocused();
  await page.setViewportSize({ width: 400, height: 900 });

  await expect(page.locator("[data-slide-strip-allocation]")).toBeHidden();
  await expect(page.locator('[data-scope="member"]')).toBeFocused();
  await expect(page.locator(".slide-strip-subject [tabindex='0']"))
    .toHaveCount(1);

  await page.setViewportSize({ width: 760, height: 900 });
  await moreSubjects.focus();
  await page.setViewportSize({ width: 1440, height: 900 });

  await expect(page.locator("[data-slide-strip-allocation]")).toBeHidden();
  await expect(page.locator('[data-scope="member"]')).toBeFocused();
  await expect(page.locator(".slide-strip-subject [tabindex='0']"))
    .toHaveCount(1);
});

test("edge indicators do not replace an item hit target", async ({ page }) => {
  await page.setViewportSize({ width: 560, height: 900 });
  await page.goto("/browser/workspace-titlebar.html?member=1");

  const target = await page.locator(".slide-strip-inspector").evaluate(
    element => {
      const bounds = element.getBoundingClientRect();
      const hit = document.elementFromPoint(
        bounds.right - 2,
        bounds.top + bounds.height / 2);
      return hit?.closest("[data-inspector-tab]")
        ?.getAttribute("data-member-section") ?? null;
    });

  expect(target).toBe("call-graph");
  const triangles = await page.locator(".slide-strip-inspector").evaluate(
    element => {
      const before = getComputedStyle(
        element.querySelector<HTMLElement>("[data-slide-strip-before]")!);
      const after = getComputedStyle(
        element.querySelector<HTMLElement>("[data-slide-strip-after]")!);
      return {
        before: before.borderRightWidth,
        after: after.borderLeftWidth,
        height: Number.parseFloat(after.borderTopWidth)
          + Number.parseFloat(after.borderBottomWidth),
      };
    });
  expect(triangles.before).toBe("4px");
  expect(triangles.after).toBe("4px");
  expect(triangles.height).toBe(18);
});

test("representation-specific gaps participate in measured capacity", async ({
  page,
}) => {
  await page.setViewportSize({ width: 480, height: 900 });
  await page.goto("/browser/workspace-titlebar.html?member=1");
  await page.addStyleTag({
    content: `
      .slide-strip-inspector[data-mode="short-label"] .slide-strip-items {
        gap: 60px;
      }`,
  });
  await page.evaluate(() => window.rerenderScopeBarProbe());

  const layout = await page.locator(".slide-strip-inspector").evaluate(
    element => {
      const items = element.querySelector<HTMLElement>(".slide-strip-items");
      if (!items) throw new Error("Inspector items are unavailable.");
      return {
        fallback: element.dataset.fallback,
        mode: element.dataset.mode,
        stripWidth: element.getBoundingClientRect().width,
        itemsWidth: items.getBoundingClientRect().width,
      };
    });
  expect(layout.fallback).toBe("false");
  expect(layout.mode).not.toBe("short-label");
  expect(layout.itemsWidth).toBeLessThanOrEqual(layout.stripWidth + 0.5);
});

test("oversized end alignment exposes the item's ending edge", async ({
  page,
}) => {
  await page.setViewportSize({ width: 560, height: 900 });
  await page.goto("/browser/workspace-titlebar.html?member=1");

  const edges = await page.locator(".slide-strip-inspector").evaluate(
    element => {
      element.style.width = "100px";
      element.dataset.fallback = "true";
      element.dataset.oversizedAlignment = "end";
      const items = element.querySelector<HTMLElement>(".slide-strip-items");
      if (!items) throw new Error("Inspector strip items are unavailable.");
      items.style.width = "200px";
      return {
        strip: element.getBoundingClientRect().right,
        items: items.getBoundingClientRect().right,
      };
    });

  expect(edges.items).toBeCloseTo(edges.strip, 0);
});

test("terminal pressure preserves both strips without page overflow", async ({
  page,
}) => {
  await page.setViewportSize({ width: 320, height: 900 });
  await page.goto("/browser/workspace-titlebar.html?member=1");

  await expect(page.locator(".lensbar")).toHaveAttribute(
    "data-pressure",
    "terminal");
  await expect(page.locator("[data-slide-strip-allocation]")).toBeHidden();
  await expect(
    page.locator(".slide-strip-subject [data-subject-tab]:not([hidden])"),
  ).toHaveCount(1);
  await expect(
    page.locator(".slide-strip-inspector [data-inspector-tab]:not([hidden])"),
  ).not.toHaveCount(0);
  expect(await page.evaluate(() =>
    document.documentElement.scrollWidth - document.documentElement.clientWidth))
    .toBeLessThanOrEqual(0);
});

test("subject-only layout reserves the empty-strip context label", async ({
  page,
}) => {
  await page.setViewportSize({ width: 800, height: 900 });
  await page.goto("/browser/workspace-titlebar.html?member=1&empty=1");

  const bounds = await page.evaluate(() => {
    const bar = document.querySelector<HTMLElement>(".lensbar");
    const context = document.querySelector<HTMLElement>(".lens-context");
    if (!bar || !context) throw new Error("Subject-only context is unavailable.");
    return {
      barRight: bar.getBoundingClientRect().right,
      contextRight: context.getBoundingClientRect().right,
    };
  });
  expect(bounds.contextRight).toBeLessThanOrEqual(bounds.barRight);
  await expect(page.locator(".lens-context")).toHaveText(
    "Filtered member list");
});

test("reduced motion preserves the same SlideStrip result", async ({ page }) => {
  await page.setViewportSize({ width: 480, height: 900 });
  await page.goto("/browser/workspace-titlebar.html?member=1");
  const snapshot = () => page.locator(".lensbar").evaluate(element => ({
    pressure: element.dataset.pressure,
    strips: [...element.querySelectorAll<HTMLElement>("[data-slide-strip]")]
      .map(strip => ({
        kind: strip.dataset.slideStrip,
        mode: strip.dataset.mode,
        visible: [...strip.querySelectorAll<HTMLElement>(
          "[data-slide-strip-id]:not([hidden])",
        )].map(item => item.dataset.slideStripId),
      })),
  }));
  const ordinary = await snapshot();
  expect(ordinary.strips.find(strip => strip.kind === "inspector")?.mode)
    .toBe("short-label");
  await expect(page.locator(
    '.slide-strip-inspector [data-inspector-tab]:not([hidden]) [data-slide-strip-representation="short-label"]',
  )).toHaveText(["O", "CG", "F"]);

  await page.emulateMedia({ reducedMotion: "reduce" });
  await page.reload();
  await expect(page.locator(".lensbar")).toHaveAttribute(
    "data-pressure",
    ordinary.pressure ?? "");
  expect(await snapshot()).toEqual(ordinary);
});

test("Annotated Source keeps its sole Explore entry under shell pressure", async ({
  page,
}) => {
  for (const width of [1120, 1050, 800, 600, 400]) {
    await page.setViewportSize({ width, height: 900 });
    await page.goto("/browser/workspace-titlebar.html?member=1&annotated=1");

    const actions = await box(page, ".shell-actions");
    const explore = await box(page, "#explore-annotated");
    await expect(page.locator("#explore-annotated")).toBeVisible();
    expect(explore.x).toBeGreaterThanOrEqual(actions.x - 1);
    expect(explore.x + explore.width)
      .toBeLessThanOrEqual(actions.x + actions.width);
    expect(await page.evaluate(() =>
      document.documentElement.scrollWidth
      - document.documentElement.clientWidth)).toBeLessThanOrEqual(0);
  }
});

test("the title line advertises the typed Package, Type, and Member path", async ({
  page,
}) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  await page.goto("/browser/workspace-titlebar.html?member=1");

  await expect(page.locator(".subject-path-segment")).toHaveText([
    "System.Text.Json",
    "System.Text.Json.JsonSerializer",
    "DeserializeSync",
  ]);
  await expect(page.locator(".subject-path-separator")).toHaveCount(2);
  await expect(page.locator("[data-subject-copy]")).toHaveCount(3);
  await expect(page.locator(".titlebar .subject-path")).toBeVisible();
  await expect(page.locator(".subject-zone .scope-switch")).toBeVisible();
  await expect(page.locator(".subject-zone .lens")).toHaveCount(5);
  await expect(page.locator(".subject-path-segment.current")).toHaveCSS(
    "color",
    "rgb(229, 102, 63)");
  const packageText = await page.locator(".subject-path-segment").nth(0)
    .evaluate(element => getComputedStyle(element).fontSize);
  const typeText = await page.locator(".subject-path-segment").nth(1)
    .evaluate(element => getComputedStyle(element).fontSize);
  const typeWeight = await page.locator(".subject-path-segment").nth(1)
    .evaluate(element => getComputedStyle(element).fontWeight);
  expect(Number.parseFloat(packageText)).toBeGreaterThan(
    Number.parseFloat(typeText));
  expect(Number.parseInt(typeWeight, 10)).toBeGreaterThanOrEqual(600);
  await page.locator("[data-subject-copy='1']").click();
  await expect(page.locator("body")).toHaveAttribute(
    "data-copied-subject",
    "System.Text.Json.JsonSerializer");
  const search = await box(page, "#open-search");
  const forward = await box(page, "#nav-forward");
  expect(forward.x + forward.width).toBeLessThanOrEqual(search.x);
  expect(search.x - (forward.x + forward.width)).toBeLessThanOrEqual(7);
  expect(search.x + search.width).toBeCloseTo(1440, 0);
  const zone = await box(page, ".subject-zone");
  const target = await box(page, ".inspected-target");
  const workspace = await box(page, ".workspace");
  expect(target.y).toBeLessThan(zone.y);
  expect(zone.x).toBe(0);
  expect(zone.width).toBeCloseTo(1440, 0);
  expect(zone.y + zone.height).toBeLessThanOrEqual(workspace.y);
});

test("Workspace gives retained coordinates the pane and keeps Share readable", async ({
  page,
}) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  await page.goto("/browser/workspace-titlebar.html?workspace=1");

  const list = await box(page, ".workspace-coordinate-list");
  const lastCoordinate = await box(page, ".workspace-coordinate:last-child");
  const share = await box(page, "#share");

  expect(list.height).toBeGreaterThan(200);
  expect(lastCoordinate.y + lastCoordinate.height)
    .toBeLessThanOrEqual(list.y + list.height);
  expect(share.width).toBeGreaterThan(40);
  await expect(page.locator("#copy-name")).toHaveCount(0);
  await expect(page.locator("[data-subject-copy]")).toHaveCount(0);
});
