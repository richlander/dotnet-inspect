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
  await expect(page.locator(".subject-zone #application-menu-button"))
    .toBeVisible();
  await expect(page.locator(".subject-zone > .shell-actions")).toBeVisible();
  await expect(page.locator(".shell-actions > *").last())
    .toHaveClass("application-menu-slot");
  await expect(page.locator("#share, #open-settings, #help")).toHaveCount(0);
  await expect(page.locator(".workspace-title")).toHaveCount(0);
  await expect(page.locator(".titlebar")).not.toContainText("0:");
  await expect(page.locator(".titlebar")).not.toContainText("Platform");
  await expect(page.locator(".workspace-window")).toHaveCount(0);
  await expect(page.locator(".brand-icon img")).toHaveAttribute(
    "src",
    "/assets/dotnet-inspect-bot.png");
});

test("the Application menu owns global actions and modal focus return", async ({
  page,
}) => {
  await page.setViewportSize({ width: 400, height: 520 });
  await page.goto("/browser/workspace-titlebar.html?member=1");

  const button = page.locator("#application-menu-button");
  await button.focus();
  await page.keyboard.press("ArrowDown");
  const items = page.getByRole("menuitem");
  await expect(items).toHaveText(["Share", "Settings", "Keyboard help"]);
  await expect(items.first()).toBeFocused();
  await expect(page.getByRole("separator")).toHaveCount(1);
  await expect(page.locator("#application-menu-overlay > #application-menu"))
    .toBeVisible();
  const popup = await box(page, "#application-menu");
  expect(popup.x).toBeGreaterThanOrEqual(0);
  expect(popup.y).toBeGreaterThanOrEqual(0);
  expect(popup.x + popup.width).toBeLessThanOrEqual(400);
  expect(popup.y + popup.height).toBeLessThanOrEqual(520);

  await page.keyboard.press("End");
  await expect(items.last()).toBeFocused();
  await page.keyboard.press("ArrowDown");
  await expect(items.first()).toBeFocused();
  await page.keyboard.press("ArrowUp");
  await expect(items.last()).toBeFocused();
  await page.keyboard.press("Escape");
  await expect(button).toBeFocused();
  await expect(page.locator("#application-menu")).toBeHidden();

  await button.press("Enter");
  await expect(items.first()).toBeFocused();
  await page.keyboard.press("Escape");
  await button.press("Space");
  await expect(items.first()).toBeFocused();
  await page.keyboard.press("Tab");
  await expect(page.locator("#application-menu")).toBeHidden();

  await button.click();
  await page.locator(".workspace").click({ position: { x: 2, y: 2 } });
  await expect(page.locator("#application-menu")).toBeHidden();

  await button.click();
  await page.getByRole("menuitem", { name: "Settings" }).click();
  await expect(page.getByRole("dialog", { name: "Settings" })).toBeVisible();
  await expect(page.locator("#settings-title")).toBeFocused();
  await expect(page.locator(".workbench")).toHaveAttribute("inert", "");
  await page.keyboard.press("Shift+Tab");
  await expect(page.getByRole("button", { name: "Light" })).toBeFocused();
  await page.keyboard.press("Escape");
  await expect(page.getByRole("dialog", { name: "Settings" })).toBeHidden();
  await expect(button).toBeFocused();

  await button.click();
  await page.getByRole("menuitem", { name: "Keyboard help" }).click();
  await expect(page.getByRole("dialog", { name: "Keyboard help" })).toBeVisible();
  await expect(page.locator("#keyboard-help-title")).toBeFocused();
  await page.locator("#keyboard-help-close").click();
  await expect(button).toBeFocused();

  await button.click();
  await items.first().click();
  await expect(page.locator("body")).toHaveAttribute("data-shared", "true");
  await expect(button).toBeFocused();

  await button.click();
  await page.setViewportSize({ width: 400, height: 180 });
  const shortPopup = await box(page, "#application-menu");
  expect(shortPopup.y).toBeGreaterThanOrEqual(0);
  expect(shortPopup.y + shortPopup.height).toBeLessThanOrEqual(180);
  expect(await page.locator("#application-menu").evaluate(menu =>
    menu.scrollHeight > menu.clientHeight)).toBe(true);
  await page.keyboard.press("Escape");

  await page.setViewportSize({ width: 400, height: 520 });
  await button.click();
  const visualHeight = await page.evaluate(() => {
    const viewport = window.visualViewport;
    if (!viewport) throw new Error("Visual viewport is unavailable.");
    Object.defineProperty(
      viewport,
      "height",
      { configurable: true, value: 120 });
    viewport.dispatchEvent(new Event("resize"));
    return viewport.height;
  });
  const visualPopup = await box(page, "#application-menu");
  expect(visualPopup.y).toBeGreaterThanOrEqual(0);
  expect(visualPopup.y + visualPopup.height)
    .toBeLessThanOrEqual(visualHeight);
  await page.keyboard.press("Escape");
});

test("Keyboard help reflects current command availability and surface", async ({
  page,
}) => {
  await page.goto("/browser/workspace-titlebar.html?package=1");
  await page.getByRole("button", { name: "Application menu" }).click();
  await page.getByRole("menuitem", { name: "Keyboard help" }).click();
  await expect(page.getByText("Search types, members, and packages"))
    .toBeVisible();
  await expect(page.getByText("Leave the current member or subject"))
    .toHaveCount(0);

  await page.goto("/browser/workspace-titlebar.html?workspace=1");
  await page.getByRole("button", { name: "Application menu" }).click();
  await page.getByRole("menuitem", { name: "Keyboard help" }).click();
  await expect(page.getByText("Search types, members, and packages"))
    .toBeVisible();
  await expect(page.getByText("Focus the current list filter")).toHaveCount(0);
  await expect(page.getByText("Select a subject or inspector")).toHaveCount(0);
  await expect(page.getByText("Move across subjects or inspectors"))
    .toHaveCount(0);
  await expect(page.getByText("Leave the current member or subject"))
    .toHaveCount(0);

  await page.goto("/browser/workspace-titlebar.html?member=1&graph=1");
  await page.getByRole("button", { name: "Application menu" }).click();
  await page.getByRole("menuitem", { name: "Keyboard help" }).click();
  await expect(page.getByText("Zoom the current graph")).toBeVisible();
  await expect(page.getByText("Pan the current graph horizontally"))
    .toBeVisible();
  await expect(page.getByText("Pan the current graph vertically"))
    .toBeVisible();
});

test("application menu keeps a fixed trailing slot outside SlideStrip overflow", async ({
  page,
}) => {
  for (const width of [1440, 400, 220]) {
    await page.setViewportSize({ width, height: 900 });
    await page.goto("/browser/workspace-titlebar.html?member=1");

    const subjectRegion = await box(page, ".subject-inspector-region");
    const menuSlot = await box(page, ".application-menu-slot");
    expect(subjectRegion.x + subjectRegion.width)
      .toBeLessThanOrEqual(menuSlot.x + 1);
    expect(menuSlot.x + menuSlot.width).toBeCloseTo(width, 0);
    await expect(page.locator(
      ".subject-inspector-region #application-menu-button",
    )).toHaveCount(0);
    expect(await page.evaluate(() =>
      document.documentElement.scrollWidth
      - document.documentElement.clientWidth)).toBeLessThanOrEqual(0);
  }
});

test("application and contextual actions preserve focus across responsive layout", async ({
  page,
}) => {
  await page.setViewportSize({ width: 1120, height: 900 });
  await page.goto("/browser/workspace-titlebar.html?member=1&source=1");

  const copy = page.locator("#copy-source");
  await copy.focus();
  await page.setViewportSize({ width: 400, height: 900 });
  await expect(copy).toBeFocused();

  const menuButton = page.locator("#application-menu-button");
  await menuButton.focus();
  await page.setViewportSize({ width: 600, height: 900 });
  await expect(menuButton).toBeFocused();

  await menuButton.click();
  await page.getByRole("menuitem", { name: "Settings" }).click();
  await page.setViewportSize({ width: 400, height: 900 });
  await expect(page.locator("#settings-title")).toBeFocused();
  await page.keyboard.press("Escape");
  await expect(menuButton).toBeFocused();
});

test("application menu returns focus to its replacement shell identity", async ({
  page,
}) => {
  await page.goto("/browser/workspace-titlebar.html?member=1");
  const button = page.locator("#application-menu-button");
  await button.click();
  await expect(page.getByRole("menuitem", { name: "Share" })).toBeFocused();

  await page.evaluate(() => window.rerenderApplicationMenuProbe());

  await expect(button).toBeFocused();
  await expect(page.locator("#application-menu")).toBeHidden();
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
  ).toHaveCount(5);
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
  await expect(page.locator(".subject-zone #application-menu-button"))
    .toBeVisible();
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
  await expect(page.locator("#application-menu-button")).toBeVisible();

  await page.setViewportSize({ width: 560, height: 900 });
  await expect(page.locator(".title-navigation .nav-history")).toBeHidden();
  await expect(page.locator("#application-menu-button")).toBeVisible();

  await page.setViewportSize({ width: 480, height: 900 });
  await expect(page.locator("#application-menu-button")).toBeVisible();
  await expect(inspectorStrip).toHaveAttribute("data-mode", "label");
  await expect(
    inspectorStrip.locator(
      '[data-inspector-tab]:not([hidden]) [data-slide-strip-representation="label"]',
    ),
  ).not.toHaveCount(0);
  expect(await inspectorStrip.locator(
    '[data-inspector-tab]:not([hidden]) [data-slide-strip-representation="short-label"]',
  ).evaluateAll(labels =>
    labels.every(label => getComputedStyle(label).display === "none")))
    .toBe(true);

  await page.setViewportSize({ width: 300, height: 900 });
  await expect(page.locator("#application-menu-button")).toBeVisible();
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
  await page.setViewportSize({ width: 400, height: 900 });
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

  const applicationMenu = page.locator("#application-menu-button");
  await applicationMenu.focus();
  await slideAfter(page, ".slide-strip-inspector");
  await expect(applicationMenu).toBeFocused();
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

test("width-only changes retain the initially applied window", async ({
  page,
}) => {
  await page.goto("/browser/workspace-titlebar.html");

  const state = await page.evaluate(async () => {
    const { SlideStripDomController } = await import(
      "../src/slide-strip-dom.ts");
    document.head.insertAdjacentHTML(
      "beforeend",
      `<style>
        .resize-continuity-probe .slide-strip-items {
          display: flex;
          gap: 0;
        }
        .resize-continuity-probe button {
          padding: 0;
          border: 0;
        }
        .resize-continuity-probe [data-slide-strip-id="a"] {
          width: 60px;
        }
        .resize-continuity-probe [data-slide-strip-id="b"] {
          width: 40px;
        }
        .resize-continuity-probe [data-slide-strip-id="c"] {
          width: 70px;
        }
      </style>`);
    const element = document.createElement("div");
    element.className = "slide-strip resize-continuity-probe";
    element.innerHTML = `
      <div class="slide-strip-items">
        <button data-slide-strip-id="a">
          <span data-slide-strip-representation="label">A</span>
        </button>
        <button data-slide-strip-id="b">
          <span data-slide-strip-representation="label">B</span>
        </button>
        <button data-slide-strip-id="c">
          <span data-slide-strip-representation="label">C</span>
        </button>
      </div>
      <span data-slide-strip-before></span>
      <span data-slide-strip-after></span>`;
    document.body.append(element);
    const continuity: { key: string; leadingId?: string } = {
      key: "resize-continuity",
    };
    const controller = new SlideStripDomController(
      element,
      [
        { id: "a", label: "A" },
        { id: "b", label: "B" },
        { id: "c", label: "C" },
      ],
      {
        modes: [{ kind: "label", minimumVisible: 1, gap: 0 }],
        initialAnchor: "b",
        preferredDirection: "after",
        continuityKey: "resize-continuity",
        fallbackVisibilityFloor: 20,
        oversizedAlignment: "start",
      },
      continuity);
    const snapshot = () => ({
      visible: [...element.querySelectorAll<HTMLElement>(
        "[data-slide-strip-id]:not([hidden])")]
        .map(item => item.dataset.slideStripId),
      leading: continuity.leadingId,
    });

    controller.apply(controller.resolve(100));
    const initial = snapshot();
    controller.apply(controller.resolve(110));
    const wider = snapshot();
    controller.apply(controller.resolve(170));
    const complete = snapshot();
    controller.apply(controller.resolve(100));
    const moved = controller.slide("after");
    const slid = snapshot();
    controller.apply(controller.resolve(170));
    const expandedAfterSlide = snapshot();
    controller.apply(controller.resolve(110));
    const narrowedAfterSlide = snapshot();

    return {
      initial,
      wider,
      complete,
      moved,
      slid,
      expandedAfterSlide,
      narrowedAfterSlide,
    };
  });

  expect(state).toEqual({
    initial: { visible: ["a", "b"], leading: "a" },
    wider: { visible: ["a", "b"], leading: "a" },
    complete: { visible: ["a", "b", "c"], leading: "a" },
    moved: true,
    slid: { visible: ["c"], leading: "c" },
    expandedAfterSlide: {
      visible: ["a", "b", "c"],
      leading: "c",
    },
    narrowedAfterSlide: { visible: ["b", "c"], leading: "c" },
  });
});

test("allocation controls move between adjacent stable result pairs", async ({
  page,
}) => {
  await page.setViewportSize({ width: 560, height: 900 });
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
  const initialInspectorCount = await inspector.locator(
    "[data-inspector-tab]:not([hidden])",
  ).count();

  await moreSubjects.click();
  await expect(moreSubjects).toBeFocused();
  await expect(
    subject.locator(
      '[data-subject-tab]:not([hidden]) [data-slide-strip-representation="label"]',
    ),
  ).toHaveText(["Package", "Type", "Member"]);
  const adjustedInspectorLabels = inspector.locator(
    '[data-inspector-tab]:not([hidden]) [data-slide-strip-representation="label"]',
  );
  await expect(adjustedInspectorLabels.first()).toHaveText("Overview");
  await expect(adjustedInspectorLabels.nth(1)).toHaveText("Call graph");
  expect(await adjustedInspectorLabels.count()).toBeLessThan(
    initialInspectorCount);
});

test("allocation preserves a manually slid inspector window", async ({
  page,
}) => {
  await page.setViewportSize({ width: 560, height: 900 });
  await page.goto("/browser/workspace-titlebar.html?member=1");

  const applicationMenu = page.locator("#application-menu-button");
  await applicationMenu.focus();
  await slideAfter(page, ".slide-strip-inspector");
  await expect(applicationMenu).toBeFocused();
  expect(await page.locator(
      ".slide-strip-inspector [data-inspector-tab]:not([hidden])",
    ).evaluateAll(items => items.map(item => item.dataset.slideStripId)),
  ).toEqual(["facts", "source", "annotated"]);

  await page.locator("[data-more-subjects]").click();
  await expect(
    page.locator(
      ".slide-strip-inspector [data-inspector-tab]:not([hidden])",
    ).first(),
  ).toHaveAttribute("data-slide-strip-id", "facts");
});

test("focus navigation refreshes allocation action candidates", async ({
  page,
}) => {
  await page.setViewportSize({ width: 560, height: 900 });
  await page.goto("/browser/workspace-titlebar.html?member=1");

  const moreSubjects = page.locator("[data-more-subjects]");
  await moreSubjects.click();
  await page.locator('[data-member-section="overview"]').focus();
  await page.keyboard.press("ArrowLeft");

  await expect(page.locator('[data-member-section="annotated"]')).toBeFocused();
  const visibleInspectors = await page.locator(
      ".slide-strip-inspector [data-inspector-tab]:not([hidden])",
    ).evaluateAll(items => items.map(item => item.dataset.slideStripId));
  expect(visibleInspectors).toContain("source");
  expect(visibleInspectors.at(-1)).toBe("annotated");
  await expect(moreSubjects).toHaveAttribute("aria-disabled", "false");
  const priorSubjectCount = await page.locator(
    ".slide-strip-subject [data-subject-tab]:not([hidden])",
  ).count();
  await moreSubjects.click();
  await expect.poll(() => page.locator(
    ".slide-strip-subject [data-subject-tab]:not([hidden])",
  ).count()).toBeGreaterThan(priorSubjectCount);
  await expect(page.locator('[data-member-section="annotated"]')).toBeVisible();
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
  await page.setViewportSize({ width: 400, height: 900 });
  await page.goto("/browser/workspace-titlebar.html?member=1");

  const inspector = page.locator(".slide-strip-inspector");
  const visibleLabels = () => inspector.locator(
    '[data-inspector-tab]:not([hidden]) [data-slide-strip-representation="label"]',
  );
  const applicationMenu = page.locator("#application-menu-button");
  await applicationMenu.focus();
  await slideAfter(page, ".slide-strip-inspector");
  await expect(visibleLabels()).toHaveText(["Call graph", "Facts"]);
  await expect(applicationMenu).toBeFocused();
  await expect(page.locator('[data-member-section="call-graph"]'))
    .toHaveAttribute("tabindex", "0");
  await expect(page.locator('[data-member-section="overview"]'))
    .toHaveAttribute("tabindex", "-1");

  await page.setViewportSize({ width: 800, height: 900 });
  await expect(visibleLabels()).toHaveCount(5);
  await page.setViewportSize({ width: 400, height: 900 });
  const narrowedLabels = await visibleLabels().allTextContents();
  expect(narrowedLabels).toEqual(["Facts", "Source"]);
  await expect(page.locator('[data-member-section="facts"]'))
    .toHaveAttribute("tabindex", "0");
  await expect(applicationMenu).toBeFocused();

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
  await page.setViewportSize({ width: 220, height: 900 });

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

test("allocation focus transfer participates in pressure selection", async ({
  page,
}) => {
  await page.goto("/browser/workspace-titlebar.html");

  const state = await page.evaluate(async () => {
    const scopeBar = await import("../src/scope-bar.ts");
    document.head.insertAdjacentHTML(
      "beforeend",
      `<style>
        .focus-pressure-probe .lensbar {
          width: 400px;
          flex: none;
        }
        .focus-pressure-probe
          [data-slide-strip="subject"] .slide-strip-item {
          width: 40px;
          padding: 0;
        }
        .focus-pressure-probe
          [data-slide-strip="inspector"] .slide-strip-item {
          width: 30px;
          padding: 0;
        }
        .focus-pressure-probe
          [data-slide-strip="inspector"]
          [data-slide-strip-id="a"] {
          width: 220px;
        }
      </style>`);
    document.body.innerHTML = `
      <div class="focus-pressure-probe">
        ${scopeBar.renderScopeBar({
          scope: "member",
          strip: [
            ["a", "Alpha", "A", "x"],
            ["b", "Beta", "B", "x"],
            ["c", "Charlie", "C", "x"],
            ["d", "Delta", "D", "x"],
            ["e", "Echo", "E", "x"],
          ],
          activeStripId: "a",
          stripAttribute: "data-member-section",
          showMemberScope: true,
          escapeHtml: String,
        })}
      </div>`;
    const binding = scopeBar.bindScopeBar(
      document,
      {
        onMemberSectionSelect() {},
        onPackageLensSelect() {},
        onScopeSelect() {},
        onTypeLensSelect() {},
      },
      scopeBar.createScopeBarState());
    await new Promise(resolve => {
      requestAnimationFrame(() => requestAnimationFrame(resolve));
    });

    const navigation = document.querySelector<HTMLElement>(".lensbar")!;
    const inspector = document.querySelector<HTMLElement>(
      '[data-slide-strip="inspector"]')!;
    const allocation = document.querySelector<HTMLElement>(
      "[data-slide-strip-allocation]")!;
    const moreInspectors = document.querySelector<HTMLElement>(
      "[data-more-inspectors]")!;
    inspector.dispatchEvent(new WheelEvent("wheel", {
      deltaY: 100,
      bubbles: true,
      cancelable: true,
    }));
    moreInspectors.focus();
    navigation.style.width = "150px";
    await new Promise(resolve => setTimeout(resolve, 100));

    const result = {
      pressure: navigation.dataset.pressure,
      inspectorFallback: inspector.dataset.fallback,
      focused: document.activeElement instanceof HTMLElement
        ? document.activeElement.dataset.slideStripId
        : undefined,
      controlsHidden: allocation.hidden,
    };
    binding.disconnect();
    return result;
  });

  expect(state.pressure).toBe("terminal");
  expect(state.controlsHidden).toBe(true);
  expect(state.focused).toBe("a");
  expect(state.inspectorFallback).toBe("true");
});

test("edge indicators do not replace an item hit target", async ({ page }) => {
  await page.setViewportSize({ width: 400, height: 900 });
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
  const indicators = await page.locator(".slide-strip-inspector").evaluate(
    element => {
      const before = getComputedStyle(
        element.querySelector<HTMLElement>("[data-slide-strip-before]")!);
      const after = getComputedStyle(
        element.querySelector<HTMLElement>("[data-slide-strip-after]")!);
      return {
        before: before.borderLeftWidth,
        after: after.borderRightWidth,
        width: after.width,
      };
    });
  expect(indicators.before).toBe("2px");
  expect(indicators.after).toBe("2px");
  expect(indicators.width).toBe("8px");
});

test("a mounted empty SlideStrip applies its empty state", async ({ page }) => {
  await page.goto("/browser/workspace-titlebar.html");

  const state = await page.evaluate(async () => {
    const { SlideStripDomController } = await import(
      "../src/slide-strip-dom.ts");
    const outside = document.createElement("button");
    outside.textContent = "Outside";
    document.body.append(outside);
    outside.focus();

    const element = document.createElement("div");
    element.className = "slide-strip";
    element.innerHTML = `
      <div class="slide-strip-items"></div>
      <span data-slide-strip-before></span>
      <span data-slide-strip-after></span>`;
    document.body.append(element);
    const controller = new SlideStripDomController(
      element,
      [],
      {
        modes: [{ kind: "label", minimumVisible: 1, gap: 0 }],
        initialAnchor: "empty",
        preferredDirection: "after",
        continuityKey: "empty",
        fallbackVisibilityFloor: 28,
        oversizedAlignment: "start",
      },
      { key: "empty" });
    const resolved = controller.resolve(100);
    controller.apply(resolved);
    return {
      result: resolved.result,
      current: controller.current,
      width: element.style.width,
      mode: element.dataset.mode,
      minimumWidth: controller.minimumOuterWidth,
      preferredWidth: controller.preferredOuterWidth,
      fallbackWidth: controller.fallbackOuterWidth,
      beforeHidden: element.querySelector<HTMLElement>(
        "[data-slide-strip-before]")?.hidden,
      afterHidden: element.querySelector<HTMLElement>(
        "[data-slide-strip-after]")?.hidden,
      slide: controller.slide("after"),
      outsideFocused: document.activeElement === outside,
    };
  });

  expect(state).toEqual({
    result: null,
    current: null,
    width: "100px",
    mode: undefined,
    minimumWidth: 0,
    preferredWidth: 0,
    fallbackWidth: 0,
    beforeHidden: true,
    afterHidden: true,
    slide: false,
    outsideFocused: true,
  });
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

test("item margins participate in measured capacity", async ({ page }) => {
  await page.setViewportSize({ width: 480, height: 900 });
  await page.goto("/browser/workspace-titlebar.html?member=1");
  await page.addStyleTag({
    content: `
      .slide-strip-inspector[data-mode="short-label"] .slide-strip-item {
        margin-right: 12px;
      }`,
  });
  await page.evaluate(() => window.rerenderScopeBarProbe());

  const layout = await page.locator(".slide-strip-inspector").evaluate(
    element => {
      const items = element.querySelector<HTMLElement>(".slide-strip-items");
      if (!items) throw new Error("Inspector items are unavailable.");
      return {
        fallback: element.dataset.fallback,
        stripWidth: element.getBoundingClientRect().width,
        itemsWidth: items.getBoundingClientRect().width,
      };
    });
  expect(layout.fallback).toBe("false");
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
  await page.setViewportSize({ width: 220, height: 900 });
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
  await page.setViewportSize({ width: 340, height: 900 });
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

test("Annotated Source keeps its complete action group under shell pressure", async ({
  page,
}) => {
  for (const width of [1120, 1050, 800, 600, 400]) {
    await page.setViewportSize({ width, height: 900 });
    await page.goto("/browser/workspace-titlebar.html?member=1&annotated=1");

    const actions = await box(page, ".working-surface-actions");
    const copy = await box(page, "#copy-annotated");
    const explore = await box(page, "#explore-annotated");
    await expect(page.locator("#copy-annotated")).toBeVisible();
    await expect(page.locator("#explore-annotated")).toBeVisible();
    expect(copy.x).toBeGreaterThanOrEqual(actions.x - 1);
    expect(explore.x).toBeGreaterThanOrEqual(actions.x - 1);
    expect(explore.x + explore.width)
      .toBeLessThanOrEqual(actions.x + actions.width);
    expect(await page.evaluate(() =>
      document.documentElement.scrollWidth
      - document.documentElement.clientWidth)).toBeLessThanOrEqual(0);
  }
});

test("Source fills the detail area below working-surface actions and above provenance", async ({
  page,
}) => {
  for (const width of [1120, 600, 400]) {
    await page.setViewportSize({ width, height: 900 });
    await page.goto("/browser/workspace-titlebar.html?member=1&source=1");

    await expect(page.locator("#copy-source")).toBeVisible();
    await expect(page.locator(".shell-action-link")).toHaveText("Open");
    await expect(page.locator("#inspector-panel > h1")).toHaveCount(0);
    await expect(
      page.getByRole("group", { name: "Source actions" }),
    ).toBeVisible();
    await expect(
      page.locator("#application-menu-button"),
    ).toBeVisible();
    await expect(
      page.getByRole("region", { name: "Source code" }),
    ).toBeVisible();

    const inspector = await box(page, "#inspector-panel");
    const source = await box(page, ".source-result");
    const subjectRegion = await box(page, ".subject-inspector-region");
    const actions = await box(page, ".working-surface-actions");
    const menuSlot = await box(page, ".application-menu-slot");
    const code = await box(page, ".source-result pre");
    const provenance = await box(page, ".source-provenance");
    expect(source.x).toBeCloseTo(inspector.x, 0);
    expect(source.y).toBeCloseTo(inspector.y, 0);
    expect(source.width).toBeCloseTo(inspector.width, 0);
    expect(source.height).toBeCloseTo(inspector.height, 0);
    expect(actions.x).toBeGreaterThanOrEqual(
      subjectRegion.x + subjectRegion.width - 1);
    expect(actions.x + actions.width).toBeLessThanOrEqual(menuSlot.x + 1);
    expect(code.y).toBeCloseTo(source.y, 0);
    expect(code.y + code.height).toBeLessThanOrEqual(provenance.y + 1);
    expect(provenance.y + provenance.height)
      .toBeCloseTo(source.y + source.height, 0);
    expect(await page.evaluate(() =>
      document.documentElement.scrollWidth
      - document.documentElement.clientWidth)).toBeLessThanOrEqual(0);
  }

  for (const width of [1920, 1440, 1120, 600, 400]) {
    await page.setViewportSize({ width, height: 900 });
    await page.goto(
      "/browser/workspace-titlebar.html?member=1&source=1&limitation=1");

    const provenance = await box(
      page,
      ".source-provenance > span:first-of-type");
    const limitation = await box(
      page,
      ".source-provenance > .graph-source-status");
    expect(provenance.width).toBeGreaterThan(16);
    expect(limitation.width).toBeGreaterThan(16);
    expect(limitation.y).toBeGreaterThanOrEqual(
      provenance.y + provenance.height - 1);
    const limitationMetrics = await page.locator(
      ".source-provenance > .graph-source-status",
    ).evaluate(element => ({
      clientWidth: element.clientWidth,
      scrollWidth: element.scrollWidth,
    }));
    expect(limitationMetrics.scrollWidth)
      .toBeLessThanOrEqual(limitationMetrics.clientWidth);
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

test("Workspace gives retained packets the pane and keeps the menu fixed", async ({
  page,
}) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  await page.goto("/browser/workspace-titlebar.html?workspace=1");

  const list = await box(page, ".workspace-packet-list");
  const lastPacket = await box(page, ".workspace-packet:last-child");
  const applicationMenu = await box(page, "#application-menu-button");

  expect(list.height).toBeGreaterThan(200);
  expect(lastPacket.y + lastPacket.height)
    .toBeLessThanOrEqual(list.y + list.height);
  expect(applicationMenu.width).toBeCloseTo(30, 0);
  await page.locator("#application-menu-button").click();
  await expect(page.getByRole("menuitem", { name: "Share" })).toBeVisible();
  await expect(page.locator("#copy-name")).toHaveCount(0);
  await expect(page.locator("[data-subject-copy]")).toHaveCount(0);
});

test("Workspace packet selection is observational and Open executes", async ({
  page,
}) => {
  await page.goto("/browser/workspace-titlebar.html?workspace=1");

  const packets = page.locator("[data-workspace-packet]");
  await expect(packets).toHaveCount(3);
  expect(await packets.evaluateAll(elements =>
    elements.map(element => element.getAttribute("data-workspace-packet"))))
    .toEqual([
    "stj-serializer",
    "stj-serialize-callgraph",
    "stj-getdecimal-callgraph",
  ]);
  const href = page.url();
  const serialize = page.locator(
    '[data-workspace-packet="stj-serialize-callgraph"]');
  await serialize.click();

  await expect(serialize).toBeFocused();
  await expect(page.locator(".workspace-heading h1"))
    .toHaveText("Serialize call graph");
  await expect(page.locator(".subject-path-segment"))
    .toHaveText("Serialize call graph");
  await expect(page.locator("body"))
    .toHaveAttribute("data-workspace-execution-count", "0");
  expect(page.url()).toBe(href);

  const getDecimal = page.locator(
    '[data-workspace-packet="stj-getdecimal-callgraph"]');
  await getDecimal.click();
  await expect(getDecimal).toBeFocused();
  await expect(page.locator(".workspace-heading h1"))
    .toHaveText("JsonElement.GetDecimal");
  expect(page.url()).toBe(href);

  await page.getByRole("button", { name: "Open workspace" }).click();
  await expect(page.locator("body"))
    .toHaveAttribute("data-workspace-execution-count", "1");
  await expect(page.locator("body"))
    .toHaveAttribute(
      "data-workspace-execution",
      "stj-getdecimal-callgraph");
});
