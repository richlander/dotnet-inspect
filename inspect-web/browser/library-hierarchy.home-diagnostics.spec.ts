import { expect, test, type Page } from "@playwright/test";
import {
  surface,
  installFacades,
  releaseFacade,
  root,
  openProductDestination,
  type DiagnosticsFixture,
} from "./library-hierarchy.support.ts";

test.use({ viewport: { width: 900, height: 900 } });

async function installDiagnosticsFacades(
  page: Page,
  diagnostics: DiagnosticsFixture = {},
): Promise<void> {
  await installFacades(
    page,
    surface,
    [],
    "ready",
    "ready",
    undefined,
    "ready",
    "ready",
    undefined,
    diagnostics);
}

test("Home keeps Search and curated demos ahead of artwork", async ({
  page,
}, testInfo) => {
  await installFacades(
    page,
    surface,
    [],
    "ready",
    "ready",
    undefined,
    "ready",
    "ready",
    {
      catalog: [{
        id: "system-text-json-api",
        title: "System.Text.Json API",
        summary: "Browse a real package API",
      }],
      results: {},
    });

  await page.setViewportSize({ width: 1440, height: 900 });
  await page.goto("/");
  await expect(page.locator(".home-search"))
    .toHaveAttribute("aria-busy", "false");
  await expect(page.locator(".home-title"))
    .toHaveText("Inspect .NET packages in your browser.");
  await expect(page.locator(".home-lede-wide")).toBeVisible();
  await expect(page.locator(".home-lede-narrow")).toBeHidden();
  await expect(page.locator(".home-demos-copy"))
    .toContainText("Start from a curated package query.");
  await expect(page.locator(".data-bar"))
    .toContainText("CLI tool · Agent skill · Demos · Diagnostics · Credits");

  const wideSearch = await page.locator(".home-search").boundingBox();
  const wideDemos = await page.locator(".home-demos").boundingBox();
  const wideDataBar = await page.locator(".data-bar").boundingBox();
  if (!wideSearch || !wideDemos || !wideDataBar) {
    throw new Error("The wide Home composition did not render.");
  }
  expect(wideSearch.y + wideSearch.height).toBeLessThan(wideDemos.y);
  expect(wideDemos.y + wideDemos.height).toBeLessThan(wideDataBar.y);
  await page.screenshot({
    path: testInfo.outputPath("home-wide.png"),
    fullPage: false,
  });

  await page.setViewportSize({ width: 390, height: 844 });
  await expect(page.locator(".home-lede-wide")).toBeHidden();
  await expect(page.locator(".home-lede-narrow")).toBeVisible();
  await expect(page.locator(".home-demos-copy span")).toBeHidden();

  const narrowSearch = await page.locator(".home-search").boundingBox();
  const narrowDemos = await page.locator(".home-demos").boundingBox();
  const narrowArt = await page.locator(".home-art").boundingBox();
  const narrowDataBar = await page.locator(".data-bar").boundingBox();
  if (!narrowSearch || !narrowDemos || !narrowArt || !narrowDataBar) {
    throw new Error("The narrow Home composition did not render.");
  }
  expect(narrowSearch.y + narrowSearch.height).toBeLessThan(narrowDemos.y);
  expect(narrowDemos.y + narrowDemos.height)
    .toBeLessThanOrEqual(narrowDataBar.y);
  expect(narrowArt.y).toBeGreaterThan(narrowDemos.y + narrowDemos.height);
  expect(narrowDataBar.y + narrowDataBar.height).toBeCloseTo(844, 0);
  expect(await page.evaluate(() =>
    document.documentElement.scrollWidth <= document.documentElement.clientWidth
    && document.body.scrollWidth <= document.body.clientWidth))
    .toBe(true);
  await page.screenshot({
    path: testInfo.outputPath("home-narrow.png"),
    fullPage: false,
  });

  await page.getByRole("link", { name: "Credits" }).click();
  await expect(page).toHaveURL("/credits");
  await expect(page.getByRole("heading", { name: "Credits", level: 1 }))
    .toBeVisible();

  await page.goto(root);
  await expect(page.locator("#inspector-panel h1"))
    .toHaveText("Example.Package");
  await page.getByRole("link", { name: "Credits" }).click();
  await expect(page).toHaveURL("/credits");
});

test("Home preserves focused controls through delayed Build identity", async ({
  page,
}) => {
  await installDiagnosticsFacades(page, { buildIdentity: "pending" });
  await page.goto("/");
  await expect(page.locator(".home-search"))
    .toHaveAttribute("aria-busy", "false");

  const credits = page.getByRole("link", { name: "Credits" });
  await credits.focus();
  await expect(credits).toBeFocused();

  await releaseFacade(page, "finish-build-identity");
  await expect(page.locator(".data-bar-product"))
    .toContainText("dotnet-inspect vfixture");
  await expect(credits).toBeFocused();
  await expect(page.locator("#spotlight-input")).not.toBeFocused();
});

test("Home preserves focus across adjacent startup rerenders", async ({
  page,
}) => {
  await installFacades(
    page,
    surface,
    [],
    "ready",
    "ready",
    undefined,
    "ready",
    "ready",
    {
      catalog: [{
        id: "system-text-json-api",
        title: "System.Text.Json API",
        summary: "Browse a real package API",
      }],
      results: {},
      catalogPending: true,
    },
    { buildIdentity: "pending" });
  await page.goto("/");
  await expect(page.locator("html"))
    .toHaveAttribute("data-home-demo-catalog-pending", "true");
  await expect(page.locator("html"))
    .toHaveAttribute("data-build-identity-pending", "true");

  const credits = page.getByRole("link", { name: "Credits" });
  await credits.focus();
  await expect(credits).toBeFocused();

  await page.evaluate(() => {
    const fixtureWindow = window as Window & {
      homeFocusFrames?: FrameRequestCallback[];
      homeFocusRequestAnimationFrame?: typeof requestAnimationFrame;
    };
    fixtureWindow.homeFocusFrames = [];
    fixtureWindow.homeFocusRequestAnimationFrame = window.requestAnimationFrame;
    window.requestAnimationFrame = callback => {
      fixtureWindow.homeFocusFrames!.push(callback);
      return fixtureWindow.homeFocusFrames!.length;
    };
  });

  await Promise.all([
    releaseFacade(page, "finish-home-demo-catalog"),
    releaseFacade(page, "finish-build-identity"),
  ]);
  await expect(page.locator("#home-demos")).toContainText("1 available");
  await expect(page.locator(".data-bar-product"))
    .toContainText("dotnet-inspect vfixture");

  await page.evaluate(() => {
    const fixtureWindow = window as Window & {
      homeFocusFrames?: FrameRequestCallback[];
      homeFocusRequestAnimationFrame?: typeof requestAnimationFrame;
    };
    const frames = fixtureWindow.homeFocusFrames ?? [];
    if (fixtureWindow.homeFocusRequestAnimationFrame) {
      window.requestAnimationFrame =
        fixtureWindow.homeFocusRequestAnimationFrame;
    }
    delete fixtureWindow.homeFocusFrames;
    delete fixtureWindow.homeFocusRequestAnimationFrame;
    const timestamp = performance.now();
    for (const frame of frames) frame(timestamp);
  });

  await expect(credits).toBeFocused();
  await expect(page.locator("#spotlight-input")).not.toBeFocused();
});

test("Home default focus yields to a post-render user selection", async ({
  page,
}) => {
  await installDiagnosticsFacades(page, { buildIdentity: "pending" });
  await page.goto("/");
  await expect(page.locator(".home-search"))
    .toHaveAttribute("aria-busy", "false");
  await page.locator(".home-title").click();

  await page.evaluate(() => {
    const fixtureWindow = window as Window & {
      homeFocusFrames?: FrameRequestCallback[];
      homeFocusRequestAnimationFrame?: typeof requestAnimationFrame;
    };
    fixtureWindow.homeFocusFrames = [];
    fixtureWindow.homeFocusRequestAnimationFrame = window.requestAnimationFrame;
    window.requestAnimationFrame = callback => {
      fixtureWindow.homeFocusFrames!.push(callback);
      return fixtureWindow.homeFocusFrames!.length;
    };
  });

  await releaseFacade(page, "finish-build-identity");
  await expect(page.locator(".data-bar-product"))
    .toContainText("dotnet-inspect vfixture");

  const credits = page.getByRole("link", { name: "Credits" });
  await credits.focus();
  await expect(credits).toBeFocused();

  await page.evaluate(() => {
    const fixtureWindow = window as Window & {
      homeFocusFrames?: FrameRequestCallback[];
      homeFocusRequestAnimationFrame?: typeof requestAnimationFrame;
    };
    const frames = fixtureWindow.homeFocusFrames ?? [];
    if (fixtureWindow.homeFocusRequestAnimationFrame) {
      window.requestAnimationFrame =
        fixtureWindow.homeFocusRequestAnimationFrame;
    }
    delete fixtureWindow.homeFocusFrames;
    delete fixtureWindow.homeFocusRequestAnimationFrame;
    const timestamp = performance.now();
    for (const frame of frames) frame(timestamp);
  });

  await expect(credits).toBeFocused();
  await expect(page.locator("#spotlight-input")).not.toBeFocused();
});

test("Home preserves focused Settings controls through delayed Build identity", async ({
  page,
}) => {
  await installDiagnosticsFacades(page, { buildIdentity: "pending" });
  await page.goto("/");
  await expect(page.locator(".home-search"))
    .toHaveAttribute("aria-busy", "false");

  await page.getByRole("button", { name: "Open settings" }).click();
  const darkTheme = page.getByRole("button", { name: "Dark" });
  await darkTheme.focus();
  await expect(darkTheme).toBeFocused();

  await releaseFacade(page, "finish-build-identity");
  await expect(page.locator(".data-bar-product"))
    .toContainText("dotnet-inspect vfixture");
  await expect(darkTheme).toBeFocused();
  await expect(page.locator("#settings-title")).not.toBeFocused();
  await expect(page.locator("#spotlight-input")).not.toBeFocused();
});

test("Home preserves Settings dismissal through an adjacent Build rerender", async ({
  page,
}) => {
  await installDiagnosticsFacades(page, { buildIdentity: "pending" });
  await page.goto("/");
  await expect(page.locator(".home-search"))
    .toHaveAttribute("aria-busy", "false");

  await page.getByRole("button", { name: "Open settings" }).click();
  await expect(page.locator("#settings-title")).toBeFocused();

  await page.evaluate(() => {
    const fixtureWindow = window as Window & {
      homeFocusFrames?: FrameRequestCallback[];
      homeFocusRequestAnimationFrame?: typeof requestAnimationFrame;
    };
    fixtureWindow.homeFocusFrames = [];
    fixtureWindow.homeFocusRequestAnimationFrame = window.requestAnimationFrame;
    window.requestAnimationFrame = callback => {
      fixtureWindow.homeFocusFrames!.push(callback);
      return fixtureWindow.homeFocusFrames!.length;
    };
  });

  await page.getByRole("button", { name: "Close" }).click();
  const settingsButton = page.getByRole("button", { name: "Open settings" });
  await expect(settingsButton).toBeFocused();

  await releaseFacade(page, "finish-build-identity");
  await expect(page.locator(".data-bar-product"))
    .toContainText("dotnet-inspect vfixture");

  await page.evaluate(() => {
    const fixtureWindow = window as Window & {
      homeFocusFrames?: FrameRequestCallback[];
      homeFocusRequestAnimationFrame?: typeof requestAnimationFrame;
    };
    const frames = fixtureWindow.homeFocusFrames ?? [];
    if (fixtureWindow.homeFocusRequestAnimationFrame) {
      window.requestAnimationFrame =
        fixtureWindow.homeFocusRequestAnimationFrame;
    }
    delete fixtureWindow.homeFocusFrames;
    delete fixtureWindow.homeFocusRequestAnimationFrame;
    const timestamp = performance.now();
    for (const frame of frames) frame(timestamp);
  });

  await expect(settingsButton).toBeFocused();
  await expect(page.locator("#spotlight-input")).not.toBeFocused();
});

test("Diagnostics opens from Settings and the data bar without entering Spotlight or the Application menu", async ({
  page,
}, testInfo) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  await installDiagnosticsFacades(page);
  await page.goto(root);
  await expect(page.locator("#inspector-panel h1"))
    .toHaveText("Example.Package");

  await page.locator("#application-menu-button").click();
  await expect(page.locator("#application-menu"))
    .not.toContainText("Diagnostics");
  await page.locator('[data-application-action="settings"]').click();
  await page.locator("#settings-diagnostics-open").click();

  await expect(page).toHaveURL(/\/diagnostics$/);
  await expect(page.locator("#diagnostics-heading")).toBeFocused();
  await expect(page.locator("#diagnostics-runtime-heading"))
    .toHaveText("Runtime startup");
  await expect(page.locator(".diagnostics-runtime-state-ready"))
    .toContainText("engine ready");
  await expect(page.locator("#diagnostics-build-heading")).toHaveText("Build");
  await expect(page.locator("#diagnostics-cache-heading"))
    .toHaveText("Isolated storage");
  const storageCard = page.locator(".diagnostics-card").filter({
    has: page.locator("#diagnostics-cache-heading"),
  });
  await expect(storageCard).toContainText("Resident payloads");
  await expect(storageCard).toContainText("Package-entry budget");
  await expect(storageCard).toContainText("12");
  await expect(storageCard).toContainText("0 B of 128 MB");
  await expect(storageCard).toContainText("1 of 4");
  await expect(storageCard).toContainText("256 per role");
  await expect(storageCard).toContainText("64 MB each");
  await expect(page.locator(".data-bar")).toHaveCount(0);
  await expect(page.getByRole("button", { name: "Back to previous page" }))
    .toBeVisible();
  await expect(page.locator("#diagnostics-product"))
    .toHaveAccessibleName("dotnet-inspect navigation");
  await page.screenshot({
    path: testInfo.outputPath("diagnostics-wide.png"),
    fullPage: true,
  });

  await page.locator("#diagnostics-product").click();
  await page.locator('[data-product-destination="home"]').click();
  await expect(page).toHaveURL("/");
  await expect(page.locator("main h1")).toBeFocused();

  await page.goBack();
  await expect(page).toHaveURL(/\/diagnostics$/);
  await expect(page.locator("#diagnostics-heading")).toBeFocused();
  await page.getByRole("button", { name: "Back to previous page" }).click();
  await expect(page).toHaveURL(/package=Example\.Package/);
  await expect(page.locator("#inspector-panel h1")).toBeFocused();

  await page.getByRole("link", { name: "Diagnostics" }).click();
  await expect(page).toHaveURL(/\/diagnostics$/);
  await expect(page.locator("#diagnostics-heading")).toBeFocused();
  await page.getByRole("button", { name: "Back to previous page" }).click();
  await expect(page).toHaveURL(/package=Example\.Package/);
  await expect(page.locator("#inspector-panel h1")).toBeFocused();

  await page.keyboard.press("Control+k");
  await expect(page.locator("#spotlight-input")).toBeFocused();
  await page.locator("#spotlight-input").fill("diagnostics");
  await expect(page.getByRole("option").filter({ hasText: "diagnostics" }))
    .toHaveCount(0);
  await page.keyboard.press("Escape");
  await expect(page.locator("#spotlight-input")).toHaveCount(0);
  await expect(page).toHaveURL(/package=Example\.Package/);
});

test("Diagnostics leaves retained Workspace available without marking it current", async ({
  page,
}) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  await installDiagnosticsFacades(page);
  await page.goto(root);
  await openProductDestination(page, "workspace");
  await expect(page.locator("#inspector-panel h1")).toHaveText("Workspace");

  await page.locator("#application-menu-button").click();
  await page.locator('[data-application-action="settings"]').click();
  await page.locator("#settings-diagnostics-open").click();
  await expect(page).toHaveURL(/\/diagnostics$/);

  await page.locator("#diagnostics-product").click();
  const workspace =
    page.locator('[data-product-destination="workspace"]');
  await expect(workspace).not.toHaveAttribute("aria-current", "page");
  await workspace.click();

  await expect(page).not.toHaveURL(/\/diagnostics$/);
  await expect(page.locator("#inspector-panel h1")).toHaveText("Workspace");
});

test("Diagnostics retains its route geometry while Build evidence loads on a narrow viewport", async ({
  page,
}, testInfo) => {
  await page.setViewportSize({ width: 390, height: 720 });
  await installDiagnosticsFacades(page, { buildIdentity: "pending" });
  await page.goto("/diagnostics");

  await expect(page.locator("#diagnostics-heading")).toHaveText("Diagnostics");
  await expect(page.locator(".diagnostics-runtime-state-ready"))
    .toContainText("engine ready");
  await expect(page.locator(".diagnostics-card")).toHaveCount(3);
  expect(await page.evaluate(() =>
    document.documentElement.scrollWidth <= document.documentElement.clientWidth
    && document.body.scrollWidth <= document.body.clientWidth))
    .toBe(true);

  const cardTops = await page.locator(".diagnostics-card").evaluateAll(cards =>
    cards.map(card => card.getBoundingClientRect().top));
  expect(cardTops[0]).toBeLessThan(cardTops[1]!);
  expect(cardTops[1]).toBeLessThan(cardTops[2]!);

  await expect(page.locator("html"))
    .toHaveAttribute("data-build-identity-pending", "true");
  await releaseFacade(page, "finish-build-identity");
  await expect(page.locator(".diagnostics-card")
    .filter({ has: page.locator("#diagnostics-build-heading") }))
    .toContainText("fixture");
  await page.screenshot({
    path: testInfo.outputPath("diagnostics-narrow-top.png"),
    fullPage: false,
  });
  await page.locator(".diagnostics-main").evaluate(element => {
    element.scrollTop = element.scrollHeight;
  });
  await expect(page.locator("#diagnostics-cache-heading")).toBeInViewport();
  await expect(page.locator("#diagnostics-back")).toBeInViewport();
  await page.screenshot({
    path: testInfo.outputPath("diagnostics-narrow-end.png"),
    fullPage: false,
  });
  expect(await page.evaluate(() =>
    document.documentElement.scrollWidth <= document.documentElement.clientWidth
    && document.body.scrollWidth <= document.body.clientWidth))
    .toBe(true);
  await page.locator("#diagnostics-back").click();
  await expect(page).toHaveURL("/");
  await expect(page.locator("main h1")).toBeFocused();
});

test("Diagnostics Back keeps Home heading focus through a later Build rerender", async ({
  page,
}) => {
  await installDiagnosticsFacades(page, { buildIdentity: "pending" });
  await page.goto("/diagnostics");

  await expect(page.locator("html"))
    .toHaveAttribute("data-build-identity-pending", "true");
  await page.locator("#diagnostics-back").click();
  await expect(page).toHaveURL("/");
  await expect(page.locator("main h1")).toBeFocused();

  await releaseFacade(page, "finish-build-identity");
  await expect(page.locator(".data-bar-product"))
    .toContainText("dotnet-inspect vfixture");
  await expect(page.locator("main h1")).toBeFocused();
});

test("Diagnostics Back honors a later Home focus selection", async ({
  page,
}) => {
  await installDiagnosticsFacades(page, { buildIdentity: "pending" });
  await page.goto("/diagnostics");

  await expect(page.locator("html"))
    .toHaveAttribute("data-build-identity-pending", "true");
  await page.locator("#diagnostics-back").click();
  await expect(page).toHaveURL("/");
  await expect(page.locator("main h1")).toBeFocused();

  const credits = page.getByRole("link", { name: "Credits" });
  await credits.focus();
  await expect(credits).toBeFocused();

  await releaseFacade(page, "finish-build-identity");
  await expect(page.locator(".data-bar-product"))
    .toContainText("dotnet-inspect vfixture");
  await expect(credits).toBeFocused();
  await expect(page.locator("main h1")).not.toBeFocused();
  await expect(page.locator("#spotlight-input")).not.toBeFocused();
});

test("Diagnostics starts runtime and cache evidence while Build identity is pending", async ({
  page,
}) => {
  await installDiagnosticsFacades(page, {
    buildIdentity: "pending",
    cachePending: true,
  });
  await page.goto("/diagnostics");

  await expect(page.locator("html"))
    .toHaveAttribute("data-build-identity-pending", "true");
  await expect(page.locator(".diagnostics-runtime-state-ready"))
    .toContainText("engine ready");
  await expect(page.locator("html"))
    .toHaveAttribute("data-package-cache-stats-pending", "true");

  await releaseFacade(page, "finish-build-identity");
  await releaseFacade(page, "finish-package-cache-stats");
  await expect(page.locator(".diagnostics-card")
    .filter({ has: page.locator("#diagnostics-build-heading") }))
    .toContainText("fixture");
  await expect(page.locator(".diagnostics-card")
    .filter({ has: page.locator("#diagnostics-cache-heading") }))
    .toContainText("Packages");
});

test("Diagnostics cache refresh does not reclaim relinquished heading focus", async ({
  page,
}) => {
  await installDiagnosticsFacades(page, { cachePending: true });
  await page.goto("/diagnostics");

  await expect(page.locator("#diagnostics-heading")).toBeFocused();
  await expect(page.locator("html"))
    .toHaveAttribute("data-package-cache-stats-pending", "true");
  await page.evaluate(() => {
    const app = document.querySelector("#app");
    if (!app) throw new Error("Missing application root");
    const observer = new MutationObserver(() => {
      const back = document.querySelector<HTMLButtonElement>(
        "#diagnostics-back");
      if (!back) return;
      observer.disconnect();
      back.focus();
    });
    observer.observe(app, { childList: true, subtree: true });
  });

  await releaseFacade(page, "finish-package-cache-stats");
  await expect(page.locator("#diagnostics-back")).toBeFocused();
  await expect(page.locator(".diagnostics-inline-loading")).toHaveCount(0);
});

test("Diagnostics cache refresh returns product-menu focus to the replacement brand", async ({
  page,
}) => {
  await installDiagnosticsFacades(page, { cachePending: true });
  await page.goto("/diagnostics");

  await expect(page.locator("html"))
    .toHaveAttribute("data-package-cache-stats-pending", "true");
  await page.locator("#diagnostics-product").click();
  await expect(page.locator('[data-product-destination="home"]'))
    .toBeFocused();

  await releaseFacade(page, "finish-package-cache-stats");

  await expect(page.locator(".diagnostics-inline-loading")).toHaveCount(0);
  await expect(page.locator(".product-navigation-menu")).toBeHidden();
  await expect(page.locator("#diagnostics-product")).toBeFocused();
});

test("Diagnostics parks recognized focus before refresh replacement", async ({
  page,
}) => {
  await installDiagnosticsFacades(page, { cachePending: true });
  await page.goto("/diagnostics");

  await expect(page.locator("html"))
    .toHaveAttribute("data-package-cache-stats-pending", "true");
  await page.locator("#diagnostics-back").focus();
  await page.evaluate(() => {
    const app = document.querySelector("#app");
    if (!app) throw new Error("Missing application root");
    const observer = new MutationObserver(() => {
      const active = document.activeElement;
      document.documentElement.dataset.diagnosticsReplacementFocus =
        `${active?.tagName ?? ""}#${active?.id ?? ""}`;
      observer.disconnect();
    });
    observer.observe(app, { childList: true });
  });

  await releaseFacade(page, "finish-package-cache-stats");

  await expect(page.locator("html"))
    .toHaveAttribute("data-diagnostics-replacement-focus", "DIV#app");
  await expect(page.locator("#diagnostics-back")).toBeFocused();
});

test("Diagnostics treats a refreshed history entry as direct", async ({
  page,
}) => {
  await installDiagnosticsFacades(page);
  await page.goto(root);
  await page.locator("#application-menu-button").click();
  await page.locator('[data-application-action="settings"]').click();
  await page.locator("#settings-diagnostics-open").click();
  await expect(page).toHaveURL(/\/diagnostics$/);

  await page.reload();
  await expect(page.locator("#diagnostics-heading")).toBeFocused();
  await page.locator("#diagnostics-back").click();
  await expect(page).toHaveURL("/");
  await page.goForward();
  await expect(page).toHaveURL("/");
});

test("Diagnostics renders absent framework timing as unavailable", async ({
  page,
}) => {
  await installDiagnosticsFacades(page);
  await page.goto("/diagnostics");
  await expect(page.locator(".diagnostics-runtime-state-ready")).toBeVisible();

  const runtimeCard = page.locator(".diagnostics-runtime-card");
  const factValue = (label: string) =>
    runtimeCard.getByText(label, { exact: true }).locator("..").locator("dd");
  await expect(factValue("Download")).toHaveText("Unavailable");
  await expect(factValue("Framework assets")).toHaveText("Unavailable");
  await expect(factValue("Transferred")).toHaveText("Unavailable");
  await expect(factValue("Decoded")).toHaveText("Unavailable");
});

test("Diagnostics preserves commit-link focus through cache refresh", async ({
  page,
}) => {
  await installDiagnosticsFacades(page, { cachePending: true });
  await page.goto("/diagnostics");

  await expect(page.locator("html"))
    .toHaveAttribute("data-package-cache-stats-pending", "true");
  const commitLink = page.locator("#diagnostics-commit");
  await commitLink.focus();
  await expect(commitLink).toBeFocused();

  await releaseFacade(page, "finish-package-cache-stats");
  await expect(commitLink).toBeFocused();
  await expect(page.locator(".diagnostics-inline-loading")).toHaveCount(0);
});

test("Diagnostics keeps startup and package-cache failures visible in place", async ({
  page,
}) => {
  await installDiagnosticsFacades(page, { runtimeFailure: true });
  await page.goto("/diagnostics");

  await expect(page.locator(".diagnostics-runtime-state-failed"))
    .toContainText("did not start");
  await expect(page.locator(".diagnostics-failure-detail"))
    .not.toBeEmpty();
  const cacheCard = page.locator(".diagnostics-card")
    .filter({ has: page.locator("#diagnostics-cache-heading") });
  await expect(cacheCard.locator(".diagnostics-inline-failed"))
    .toContainText("inspection engine did not start");
  await expect(page).toHaveURL(/\/diagnostics$/);
});

test("Diagnostics isolates a build-identity failure from runtime and cache", async ({
  page,
}) => {
  await installDiagnosticsFacades(page, { buildIdentity: "failed" });
  await page.goto("/diagnostics");

  await expect(page.locator(".diagnostics-runtime-state-ready"))
    .toContainText("engine ready");
  const buildCard = page.locator(".diagnostics-card")
    .filter({ has: page.locator("#diagnostics-build-heading") });
  await expect(buildCard.locator(".diagnostics-inline-failed"))
    .toContainText("Product build identity is unavailable");
  const cacheCard = page.locator(".diagnostics-card")
    .filter({ has: page.locator("#diagnostics-cache-heading") });
  await expect(cacheCard).toContainText("Packages");
  await expect(cacheCard.locator(".diagnostics-inline-failed")).toHaveCount(0);
});

test("Diagnostics discloses a package-cache statistics failure", async ({
  page,
}) => {
  await installDiagnosticsFacades(page, { cacheFailure: true });
  await page.goto("/diagnostics");

  await expect(page.locator(".diagnostics-runtime-state-ready"))
    .toContainText("engine ready");
  await expect(page.locator(".diagnostics-inline-failed"))
    .toContainText("Cache storage offline");
});
