import { expect, test } from "@playwright/test";
import {
  subjectTab,
  chooseInspector,
  chooseSubject,
  surface,
  platformVersion,
  alternatePlatformVersion,
  installFacades,
  releaseFacade,
  currentWorkspaceHistoryState,
  openProductDestination,
  openInstalledPlatform,
  openPlatform,
} from "./library-hierarchy.support.ts";

test.use({ viewport: { width: 900, height: 900 } });

test("Activity Back restores focus on the Platform route", async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  await openPlatform(page);
  const platformLocation = page.url();
  await openProductDestination(page, "activity");
  await expect(page).toHaveURL(/\/activity$/);

  await page.goBack();

  await expect(page).toHaveURL(platformLocation);
  await expect(page.locator("[data-product-navigation-button]")).toBeFocused();
});

test("platform-only Workspace preserves Query as its Back predecessor", async ({
  page,
}) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  await openPlatform(page);
  await openProductDestination(page, "query");
  await expect(page).toHaveURL(/\/query$/);

  await openProductDestination(page, "workspace");

  await expect(page.locator("#inspector-panel h1")).toHaveText("Workspace");
  await expect(page).not.toHaveURL(/\/query$/);
  await page.goBack();
  await expect(page).toHaveURL(/\/query$/);
  await expect(page.locator("#package-query-heading"))
    .toHaveText("Package query");
});

test("pending platform Workspace projection keeps Query current", async ({
  page,
}) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  await openPlatform(page);
  await openProductDestination(page, "query");
  await expect(page).toHaveURL(/\/query$/);
  const queryLocation = page.url();

  await releaseFacade(page, "hold-workspace-encode");
  await openProductDestination(page, "workspace");
  await expect(page.locator("html"))
    .toHaveAttribute("data-workspace-encode-pending", "true");
  await expect(page).toHaveURL(queryLocation);
  await expect(page.locator("#package-query-heading"))
    .toHaveText("Package query");
  await expect(page.locator("[data-product-navigation-button]"))
    .toBeFocused();

  await page.locator("[data-product-navigation-button]").click();
  await expect(page.locator('[data-product-destination="query"]'))
    .toHaveAttribute("aria-current", "page");
  await expect(page.locator('[data-product-destination="workspace"]'))
    .not.toHaveAttribute("aria-current", "page");
  await page.evaluate(() => {
    const app = document.querySelector("#app");
    if (!app) throw new Error("Missing application root");
    const observer = new MutationObserver(() => {
      const active = document.activeElement;
      document.documentElement.dataset.workspaceInterveningFocus =
        `${active?.tagName ?? ""}#${active?.id ?? ""}`;
      observer.disconnect();
    });
    observer.observe(app, { childList: true });
  });

  await releaseFacade(page, "finish-workspace-encode");
  await expect(page.locator("html"))
    .toHaveAttribute("data-workspace-intervening-focus", "DIV#app");
  const app = page.locator("#app");
  await expect(app).toBeFocused();
  await expect(app).toHaveAttribute("tabindex", "-1");
  await page.keyboard.press("Tab");
  await expect(app).not.toHaveAttribute("tabindex");
  await expect(page.locator("#inspector-panel h1")).toHaveText("Workspace");
  await expect(page).not.toHaveURL(queryLocation);
});

test("Workspace projection parks brand focus before replacement", async ({
  page,
}) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  await openPlatform(page);
  await openProductDestination(page, "query");
  await releaseFacade(page, "hold-workspace-encode");
  await openProductDestination(page, "workspace");
  await expect(page.locator("html"))
    .toHaveAttribute("data-workspace-encode-pending", "true");
  await expect(page.locator("[data-product-navigation-button]"))
    .toBeFocused();
  await page.evaluate(() => {
    const app = document.querySelector("#app");
    if (!app) throw new Error("Missing application root");
    const observer = new MutationObserver(() => {
      const active = document.activeElement;
      document.documentElement.dataset.workspaceReplacementFocus =
        `${active?.tagName ?? ""}#${active?.id ?? ""}`;
      observer.disconnect();
    });
    observer.observe(app, { childList: true });
  });

  await releaseFacade(page, "finish-workspace-encode");

  await expect(page.locator("html"))
    .toHaveAttribute("data-workspace-replacement-focus", "DIV#app");
  await expect(page.locator("[data-workspace-select]")).toBeFocused();
});

test("superseded Workspace projection cannot steal Activity focus", async ({
  page,
}) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  await openPlatform(page);
  await openProductDestination(page, "query");
  await releaseFacade(page, "hold-workspace-encode");
  await openProductDestination(page, "workspace");
  await expect(page.locator("html"))
    .toHaveAttribute("data-workspace-encode-pending", "true");

  await openProductDestination(page, "activity");
  await expect(page).toHaveURL(/\/activity$/);
  const packageSet = page.locator("#package-changes-package-set");
  await expect(packageSet).toBeFocused();

  await releaseFacade(page, "finish-workspace-encode");
  await page.waitForTimeout(100);
  await expect(packageSet).toBeFocused();
});

test("Platform Workspace projection failure remains visible on Query", async ({
  page,
}) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  await openPlatform(page);
  await openProductDestination(page, "query");
  const queryLocation = page.url();

  await releaseFacade(page, "fail-workspace-encode");
  await openProductDestination(page, "workspace");

  await expect(page).toHaveURL(queryLocation);
  await expect(page.locator("#package-query-heading"))
    .toHaveText("Package query");
  await expect(page.locator(".query-navigation-error"))
    .toContainText("Fixture workspace projection failure.");
  await expect(page.locator("[data-product-navigation-button]"))
    .toBeFocused();
});

test("delayed Platform Workspace failure preserves newer Query focus", async ({
  page,
}) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  await openPlatform(page);
  await openProductDestination(page, "query");

  await releaseFacade(page, "hold-workspace-encode");
  await releaseFacade(page, "fail-workspace-encode");
  await openProductDestination(page, "workspace");
  await expect(page.locator("html"))
    .toHaveAttribute("data-workspace-encode-pending", "true");

  const queryInput = page.locator("#package-query-prefix");
  await queryInput.focus();
  await expect(queryInput).toBeFocused();
  await releaseFacade(page, "finish-workspace-encode");

  await expect(page.locator(".query-navigation-error"))
    .toContainText("Fixture workspace projection failure.");
  await expect(queryInput).toBeFocused();
});

test("Platform Library projection failure does not use package fallback", async ({
  page,
}) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  await openPlatform(page);
  await page.getByRole(
    "button",
    { name: /System.Text.Json Implementation/ },
  ).click();
  await expect(subjectTab(page, "library"))
    .toHaveAttribute("aria-selected", "true");
  await openProductDestination(page, "query");
  const queryLocation = page.url();

  await releaseFacade(page, "fail-workspace-encode");
  await openProductDestination(page, "workspace");

  await expect(page).toHaveURL(queryLocation);
  await expect(page.locator("#package-query-heading"))
    .toHaveText("Package query");
  await expect(page.locator(".query-navigation-error"))
    .toContainText("Fixture workspace projection failure.");
});

test("Platform opens its catalog before warm-up, with reference membership and role labels", async ({ page }) => {
  await openPlatform(page, { warmup: "pending" });
  await expect(page.locator(".platform-library-row")).toHaveCount(3);
  await expect(page.locator("#platform-framework option")).toHaveText(["net11.0"]);
  await expect(page.locator("#inspector-panel")).toContainText("Downloading runtime packs");
  await expect(page.locator("[data-platform-role=facade]")).toContainText("Facade");
  await expect(page.locator("[data-platform-role=facade] .platform-role-icon")).toHaveCSS("border-top-style", "dashed");
  await expect(page.locator("[data-platform-role=implementation] .platform-role-icon")).toHaveCSS("border-top-style", "solid");
  await expect(page.locator("[data-platform-role=reference]")).toContainText("Unsupported: no runtime implementation");
  await expect(page.locator("[data-platform-role=reference] button")).toHaveCount(0);
  await expect(page.locator("[data-scope=package], [data-package-lens], #package-version, #framework")).toHaveCount(0);
  await page.getByLabel("Include all libraries").check();
  const privateRow = page.locator("[data-platform-role=private] button");
  await expect(privateRow).toBeEnabled();
  await expect(privateRow).toContainText("Private implementation");
  await expect(privateRow.locator(".platform-role-icon")).toHaveText("P");
  await expect(privateRow.locator(".platform-role-icon")).toHaveCSS("border-top-style", "double");
  await privateRow.click();
  await expect(subjectTab(page, "library")).toHaveAttribute("aria-selected", "true");
  await expect(page.locator("#inspector-panel h1")).toHaveText("System.Private.Empty");
  await expect(page.locator("#type-list [data-type]")).toHaveCount(0);
  await expect(page.locator("html")).toHaveAttribute("data-platform-library-request",
    JSON.stringify(["net11.0", platformVersion, "System.Private.Empty.dll", "netcore.app", "System.Private.Empty.dll"]));
});

test("Platform catalog occupies the full workspace at every responsive breakpoint", async ({ page }) => {
  await openPlatform(page);
  const workspace = page.locator(".platform-workspace");

  for (const width of [1440, 900, 390]) {
    await page.setViewportSize({ width, height: 844 });
    const layout = await workspace.evaluate(element => {
      const detail = element.querySelector<HTMLElement>(":scope > .detail-pane");
      if (!detail) throw new Error("Platform detail pane is missing.");
      return {
        columns: getComputedStyle(element).gridTemplateColumns.trim().split(/\s+/),
        detailWidth: detail.getBoundingClientRect().width,
        workspaceWidth: element.getBoundingClientRect().width,
      };
    });

    expect(layout.columns).toHaveLength(1);
    expect(Math.abs(layout.workspaceWidth - layout.detailWidth)).toBeLessThan(1);
  }
});

test("Platform warm-up failure preserves inventory and has an independent retry", async ({ page }) => {
  await openPlatform(page, { warmup: "fail-once", discoveryFailure: true });
  await expect(page.locator("#inspector-panel")).toContainText("Archive offline");
  await expect(page.locator("#inspector-panel")).toContainText("Version discovery offline");
  await expect(page.locator("#platform-version")).toHaveValue(platformVersion);
  await expect(page.locator(".platform-library-row")).toHaveCount(3);
  await page.locator('[data-platform-retry="warmup"]').click();
  await expect(page.locator("html")).toHaveAttribute("data-platform-warmup-attempts", "2");
  await expect(page.locator('[data-platform-retry="warmup"]')).toHaveCount(0);
  await expect(page.locator(".platform-library-row")).toHaveCount(3);
});

test("Platform exact version switch installs its matching catalog and pins Library inspection", async ({ page }) => {
  await openPlatform(page);
  await expect(page.locator("#platform-version option")).toHaveCount(2);
  await page.getByLabel("Platform version", { exact: true }).selectOption(alternatePlatformVersion);
  await expect(page.locator("#platform-version")).toHaveValue(alternatePlatformVersion);
  await expect(page.locator(".platform-library-list")).toContainText("System.NewFacade");
  await page.getByRole("button", { name: /System.Text.Json Implementation/ }).click();
  await expect(page.locator("html")).toHaveAttribute("data-platform-library-request",
    JSON.stringify(["net11.0", alternatePlatformVersion, "System.Text.Json.dll", "netcore.app", "System.Text.Json.dll"]));
  await expect(page.locator("#inspector-panel h1")).toHaveText("System.Text.Json");
  await chooseInspector(page, "data-library-lens", "metadata", "Metadata");
  await expect(page.locator("html")).toHaveAttribute("data-platform-metadata-request",
    JSON.stringify(["net11.0", alternatePlatformVersion, "System.Text.Json.dll", "netcore.app"]));
  await expect(page.locator("#inspector-panel")).toContainText("Module");
  await expect(page.locator("#package-version, #framework, [data-platform-metadata-library]")).toHaveCount(0);
});

test("Platform mismatched catalog does not relabel the installed inventory", async ({ page }) => {
  await openPlatform(page, { wrongCatalog: true });
  await expect(page.locator("#platform-version option")).toHaveCount(2);
  await page.getByLabel("Platform version", { exact: true }).selectOption(alternatePlatformVersion);
  await expect(page.locator("#inspector-panel")).toContainText("does not match");
  await expect(page.locator("#platform-version")).toHaveValue(platformVersion);
  await expect(page.locator(".platform-library-list")).toContainText("System.Facade");
  await expect(page.locator(".platform-library-list")).not.toContainText("System.NewFacade");
});

test("Spotlight offers NuGet and .NET Library System.Text.Json destinations without a Platform choice", async ({ page }) => {
  await installFacades(page, surface, [], "ready", "ready", { libraryPending: true });
  await page.route("https://azuresearch-usnc.nuget.org/query?**", route => route.fulfill({
    contentType: "application/json", body: JSON.stringify({ data: [{ id: "System.Text.Json", version: "11.0.0-preview.7" }] }),
  }));
  await page.goto("/");
  const search = page.getByRole("combobox");
  await expect(search).toBeEnabled();
  await search.fill("System.Text.Json");
  await expect(page.locator('[data-sl-pkg-load="System.Text.Json"]')).toBeVisible();
  await expect(page.locator('[data-sl-framework-lib="System.Text.Json"]')).toContainText(".NET library");
  await expect(page.locator('[data-sl-scope="runtime"]')).toHaveCount(0);
  await expect(page.getByRole("button", { name: "Platform", exact: true })).toHaveCount(0);
  await expect(page.locator("html")).not.toHaveAttribute("data-platform-warmup");
  await page.locator('[data-sl-framework-lib="System.Text.Json"]').click();
  await expect(page.locator(".platform-workspace")).toHaveCount(0);
  await expect(subjectTab(page, "platform")).toHaveCount(0);
  await expect(page.getByText("Opening the selected Library...")).toHaveCount(0);
  await releaseFacade(page, "finish-platform-library");
  await expect(subjectTab(page, "library")).toHaveAttribute("aria-selected", "true");
  await expect(subjectTab(page, "platform")).toHaveCount(0);
  await expect(page.locator("[data-type-nav-back]")).toHaveCount(0);
  await expect(page.locator(".inspected-target .subject-path")).toContainText("System.Text.Json");
  await expect(page.locator(".inspected-target .subject-path")).not.toContainText("Platform");
  await openProductDestination(page, "workspace");
  await expect(page.locator("[data-workspace-platform]")).toHaveCount(0);
  await expect(page.locator("[data-workspace-framework-library]")).toContainText("System.Text.Json");
  await page.locator("[data-workspace-framework-library]").click();
  await expect(subjectTab(page, "library")).toHaveAttribute("aria-selected", "true");
  await openProductDestination(page, "workspace");
  await page.keyboard.press("Control+p");
  await page.locator("#spotlight-input").fill("System.Text.Json");
  await page.locator('[data-sl-framework-lib="System.Text.Json"]').click();
  await expect(subjectTab(page, "platform")).toHaveCount(0);
  await expect(page.locator("[data-type-nav-back]")).toHaveCount(0);
  await expect(page.locator(".inspected-target .subject-path")).not.toContainText("Platform");
  await expect.poll(() => new URL(page.url()).pathname).not.toBe("/query");
  await page.reload();
  await expect(subjectTab(page, "platform")).toHaveCount(0);
  await expect(page.locator("html")).toHaveAttribute("data-platform-library-request");
  await releaseFacade(page, "finish-platform-library");
  await expect(subjectTab(page, "library")).toHaveAttribute("aria-selected", "true");
  await expect(subjectTab(page, "platform")).toHaveCount(0);
  await expect(page.locator("[data-type-nav-back]")).toHaveCount(0);
});

test("Spotlight framework Library failure stays outside Platform presentation", async ({ page }) => {
  await installFacades(page, surface, [], "ready", "ready", { libraryFailure: true });
  await page.goto("/");
  const search = page.getByRole("combobox");
  await search.fill("System.Text.Json");
  await page.locator('[data-sl-framework-lib="System.Text.Json"]').click();
  await expect(page.locator(".platform-workspace")).toHaveCount(0);
  await expect(subjectTab(page, "platform")).toHaveCount(0);
  await expect(page.locator(".query-notice-text")).toContainText(
    "Could not open Library: Library offline",
  );
  await expect(page.locator(".query-notice-text")).not.toContainText("Platform Library");
});

test("a direct Spotlight framework Library remains a Library after package activation", async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  await page.addInitScript(() => localStorage.setItem(
    "inspect-recent-packages",
    JSON.stringify([{ id: "Second.Package", version: "1.0.0", framework: "net10.0" }]),
  ));
  await installFacades(page, surface, [], "ready", "ready", {});
  await page.goto("/");
  await page.getByRole("combobox").fill("System.Text.Json");
  await page.locator('[data-sl-framework-lib="System.Text.Json"]').click();
  await expect(subjectTab(page, "library")).toHaveAttribute("aria-selected", "true");

  await openProductDestination(page, "workspace");
  await page.getByRole("button", { name: "Add package", exact: true }).click();
  const add = page.getByRole("dialog", { name: "Add package", exact: true });
  await add.getByRole("combobox", { name: "Add package", exact: true })
    .fill("Second.Package");
  await add.locator('[data-sl-pkg-recent="Second.Package"]').click();
  await expect(page.locator("[data-workspace-platform]")).toHaveCount(0);
  await expect(page.locator("[data-workspace-framework-library]"))
    .toContainText("System.Text.Json");

  await page.getByRole(
    "button",
    { name: "Inspect Second.Package 1.0.0 net10.0", exact: true },
  ).click();
  await expect(page.locator(".inspected-target")).toContainText("Second.Package");
  await openProductDestination(page, "workspace");
  await expect(page.locator("[data-workspace-platform]")).toHaveCount(0);
  await expect(page.locator("[data-workspace-framework-library]"))
    .toContainText("System.Text.Json");
  await page.locator("[data-workspace-framework-library]").click();
  await expect(page.locator("#inspector-panel h1")).toHaveText("System.Text.Json");
  await expect(subjectTab(page, "library")).toHaveAttribute("aria-selected", "true");
  await expect(page.locator(".inspected-target")).not.toContainText("Second.Package");
});

test("Workspace retains an in-place framework Library selection", async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  await installFacades(page, surface, [], "ready", "ready", {});
  await page.goto("/");
  await page.getByRole("combobox").fill("System.Text.Json");
  await page.locator('[data-sl-framework-lib="System.Text.Json"]').click();
  await chooseInspector(
    page,
    "data-library-lens",
    "integrations",
    "Integrations",
  );
  await page.locator('[data-integration-mode="opportunities"]').click();
  const picker = page.locator(
    ".library-opportunities-controls .platform-library-select",
  );
  await picker.selectOption("System.Facade");
  await expect(picker).toHaveValue("System.Facade");

  await openProductDestination(page, "workspace");
  const frameworkLibrary =
    page.locator("[data-workspace-framework-library]");
  await expect(frameworkLibrary).toContainText("System.Facade");
  await expect(frameworkLibrary)
    .toHaveAttribute("data-workspace-framework-library", "System.Facade");
  await frameworkLibrary.click();
  await expect(page.locator("#inspector-panel h1")).toHaveText("System.Facade");
  await expect(subjectTab(page, "platform")).toHaveCount(0);
});

test("Platform Library parent, history and refresh retain the exact target without choosing a Type", async ({ page }) => {
  await openPlatform(page);
  const platformLocation = page.url();
  await page.getByRole("button", { name: /System.Facade Facade/ }).click();
  await expect(subjectTab(page, "library")).toHaveAttribute("aria-selected", "true");
  await expect.poll(() => page.url()).not.toBe(platformLocation);
  const libraryLocation = page.url();
  await expect(page.locator("#type-list [data-type]")).toHaveCount(0);
  await page.reload();
  await expect(page.locator("#inspector-panel h1")).toHaveText("System.Facade");
  await expect(page.locator("html")).toHaveAttribute("data-platform-library-request",
    JSON.stringify(["net11.0", platformVersion, "System.Facade.dll", "netcore.app", "System.Facade.dll"]));
  await expect(page.locator("[data-type-nav-back]")).toHaveAttribute("title", "Back to platform");
  await page.locator(".type-browser .nav-back-row").click();
  await expect(subjectTab(page, "platform")).toHaveAttribute("aria-selected", "true");
  await expect(page.locator(".platform-library-list")).toBeFocused();
  await expect(page).toHaveURL(platformLocation);
  await page.getByRole("button", { name: "Application menu", exact: true }).press("Alt+ArrowLeft");
  await expect(page).toHaveURL(libraryLocation);
  await expect(page.locator("#inspector-panel h1")).toHaveText("System.Facade");
  await page.getByRole("button", { name: "Application menu", exact: true }).press("Alt+ArrowRight");
  await expect(page).toHaveURL(platformLocation);
  await page.reload();
  await expect(subjectTab(page, "platform")).toHaveAttribute("aria-selected", "true");
  await expect(page.locator("#platform-version")).toHaveValue(platformVersion);
  await expect(page.locator("html")).not.toHaveAttribute("data-platform-library-request");
});

test("history-restored cached Platform Libraries remain usable through Spotlight", async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  await openPlatform(page);
  const platformLocation = page.url();
  await page.getByRole("button", { name: /System.Text.Json Implementation/ }).click();
  await expect.poll(() => page.url()).not.toBe(platformLocation);
  const libraryLocation = page.url();
  await page.locator(".type-browser .nav-back-row").click();
  await expect(subjectTab(page, "platform")).toHaveAttribute("aria-selected", "true");
  await page.getByLabel("Platform version", { exact: true }).selectOption(alternatePlatformVersion);
  await expect(page.locator("#platform-version")).toHaveValue(alternatePlatformVersion);
  await page.getByRole("button", { name: /System.Text.Json Implementation/ }).click();
  await expect(subjectTab(page, "library")).toHaveAttribute("aria-selected", "true");
  await page.getByRole("button", { name: "Application menu", exact: true }).press("Alt+ArrowLeft");
  await expect(page.locator("#platform-version")).toHaveValue(alternatePlatformVersion);
  await page.getByRole("button", { name: "Application menu", exact: true }).press("Alt+ArrowLeft");
  await expect(page.locator("#platform-version")).toHaveValue(platformVersion);
  await page.getByRole("button", { name: "Application menu", exact: true }).press("Alt+ArrowLeft");
  await expect(page).toHaveURL(libraryLocation);
  await expect(page.locator("#inspector-panel h1")).toHaveText("System.Text.Json");

  await page.getByRole(
    "button",
    { name: "Search types, members, packages", exact: true },
  ).click();
  await page.locator("#spotlight-input").fill("Widget");
  await page.locator('[data-sl-type*="Example.Widget"]:not([data-sl-member])').first().click();
  await expect(subjectTab(page, "type")).toHaveAttribute("aria-selected", "true");
  await expect(page.locator(".inspected-target")).toContainText("Example.Widget");
});

test("Platform Library acquisition failure keeps its catalog usable", async ({ page }) => {
  await openPlatform(page, { libraryFailure: true });
  await page.getByRole("button", { name: /System.Text.Json Implementation/ }).click();
  await expect(page.locator("#inspector-panel")).toContainText("Library offline");
  await expect(page.locator(".platform-library-row")).toHaveCount(3);
  await expect(page.locator('[data-platform-retry="library"]')).toBeEnabled();
});

test("Catalog-only Platform is a Workspace coordinate and pending Library work cannot steal it", async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  await openPlatform(page, { libraryPending: true, warmup: "pending" });
  await page.getByRole("button", { name: /System.Text.Json Implementation/ }).click();
  await expect(page.locator("html")).toHaveAttribute("data-platform-library-request");
  await openProductDestination(page, "workspace");
  await expect(page.locator("#inspector-panel h1")).toHaveText("Workspace");
  await expect(page.locator("#inspector-panel")).toContainText(platformVersion);
  await releaseFacade(page, "finish-platform-library");
  await expect(page.locator("#inspector-panel h1")).toHaveText("Workspace");
  await page.locator("[data-workspace-platform]").click();
  await expect(page.locator(".platform-library-row")).toHaveCount(3);
  await expect(page.locator("#platform-version")).toHaveValue(platformVersion);
  await expect(subjectTab(page, "platform")).toHaveAttribute("aria-selected", "true");
});

test("pending Platform catalog cannot overwrite a loaded Package selected through Spotlight", async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  await installFacades(page, surface, [], "ready", "ready", { catalogPending: true });
  await openInstalledPlatform(page, true);
  await expect(subjectTab(page, "platform"))
    .toHaveAttribute("aria-selected", "true");
  await expect(page.locator("#platform-version option")).toHaveCount(2);
  await page.getByLabel("Platform version", { exact: true })
    .selectOption(alternatePlatformVersion);
  await expect(page.locator("html")).toHaveAttribute(
    "data-platform-catalog-request",
    JSON.stringify(["net11.0", alternatePlatformVersion]),
  );

  await page.getByRole(
    "button",
    { name: "Search types, members, packages", exact: true },
  ).click();
  await page.locator("#spotlight-input").fill("Example.Package");
  await page.locator('[data-sl-pkg-open="Example.Package"]').click();
  await expect(page.locator(".library-overview-surface h1"))
    .toHaveText("All libraries");
  await expect(subjectTab(page, "library")).toHaveAttribute("aria-selected", "true");
  await chooseSubject(page, "package", "Package");

  await releaseFacade(page, "finish-platform-catalog");
  await expect(subjectTab(page, "package")).toHaveAttribute("aria-selected", "true");
  await expect(page.locator("#inspector-panel h1")).toHaveText("Example.Package");
});

test("pending Platform catalog cannot overwrite a loaded Type selected through Commands", async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  await installFacades(page, surface, [], "ready", "ready", { catalogPending: true });
  await openInstalledPlatform(page, true);
  await expect(subjectTab(page, "platform"))
    .toHaveAttribute("aria-selected", "true");
  await page.getByRole("button", { name: /System.Text.Json Implementation/ }).click();
  await expect(subjectTab(page, "library")).toHaveAttribute("aria-selected", "true");
  await page.locator(".type-browser .nav-back-row").click();
  await expect(subjectTab(page, "platform")).toHaveAttribute("aria-selected", "true");
  await page.getByLabel("Platform version", { exact: true })
    .selectOption(alternatePlatformVersion);
  await expect(page.locator("html")).toHaveAttribute(
    "data-platform-catalog-request",
    JSON.stringify(["net11.0", alternatePlatformVersion]),
  );

  await page.getByRole(
    "button",
    { name: "Search types, members, packages", exact: true },
  ).click();
  await page.locator('[data-sl-scope="commands"]').click();
  await page.locator("#spotlight-input").fill("type Widget");
  await expect(page.locator("#spotlight-results")).toContainText("type Widget");
  await page.keyboard.press("Enter");
  await expect(subjectTab(page, "type")).toHaveAttribute("aria-selected", "true");
  await expect(page.locator(".inspected-target")).toContainText("Example.Widget");

  await releaseFacade(page, "finish-platform-catalog");
  await expect(subjectTab(page, "type")).toHaveAttribute("aria-selected", "true");
  await expect(page.locator(".inspected-target")).toContainText("Example.Widget");
});

for (const destination of ["Type", "Member"] as const) {
  test(`pending Platform Library cannot overwrite a loaded ${destination} selected through Spotlight`, async ({ page }) => {
    await page.setViewportSize({ width: 1440, height: 900 });
    await installFacades(page, surface, [], "ready", "ready", { libraryPending: true });
    await openInstalledPlatform(page, true);
    await expect(subjectTab(page, "platform"))
      .toHaveAttribute("aria-selected", "true");
    await page.getByRole("button", { name: /System.Text.Json Implementation/ }).click();
    await expect(page.locator("html")).toHaveAttribute("data-platform-library-request");

    await page.getByRole(
      "button",
      { name: "Search types, members, packages", exact: true },
    ).click();
    await page.locator("#spotlight-input").fill(
      destination === "Type" ? "Widget" : "Run");
    if (destination === "Type") {
      await page.locator('[data-sl-type*="Example.Widget"]').click();
      await expect(subjectTab(page, "type")).toHaveAttribute("aria-selected", "true");
      await expect(page.locator(".inspected-target")).toContainText("Example.Widget");
    } else {
      await page.locator('[data-sl-member][data-sl-type*="Example.Widget"]').click();
      await expect(subjectTab(page, "member")).toHaveAttribute("aria-selected", "true");
      await expect(page.locator("#inspector-panel h1")).toContainText("Run");
    }

    await releaseFacade(page, "finish-platform-library");
    await expect(page.locator(
      `[data-subject-tab][data-scope="${destination.toLowerCase()}"]`,
    )).toHaveAttribute("aria-selected", "true");
  });
}

test("Platform version history restores the prior exact catalog on a narrow viewport", async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 });
  await openPlatform(page);
  const originalLocation = page.url();
  await expect(page.locator("#platform-version option")).toHaveCount(2);
  await page.getByLabel("Platform version", { exact: true }).selectOption(alternatePlatformVersion);
  await expect(page.locator("#platform-version")).toHaveValue(alternatePlatformVersion);
  await page.getByRole("button", { name: "Application menu", exact: true }).press("Alt+ArrowLeft");
  await expect(page).toHaveURL(originalLocation);
  await expect(page.locator("#platform-version")).toHaveValue(platformVersion);
  await expect(page.locator(".platform-library-list")).toContainText("System.Facade");
  await expect(page.locator(".platform-library-list")).not.toContainText("System.NewFacade");
});

test("A failed dynamic Platform catalog leaves the installed target and inventory available", async ({ page }) => {
  await openPlatform(page, { catalogFailure: true });
  await expect(page.locator("#platform-version option")).toHaveCount(2);
  await page.getByLabel("Platform version", { exact: true }).selectOption(alternatePlatformVersion);
  await expect(page.locator("#inspector-panel")).toContainText("Catalog offline");
  await expect(page.locator("#platform-version")).toHaveValue(platformVersion);
  await expect(page.locator(".platform-library-row")).toHaveCount(3);
  await expect(page.locator('[data-platform-retry="catalog"]')).toBeEnabled();
});

test("Sequential same-named Platform Libraries replace the prior family and retain the selected pack", async ({ page }) => {
  await openPlatform(page, { duplicateLibrary: true });
  await page.getByRole("button", { name: /System.Text.Json Implementation netcore.app/ }).click();
  await expect(page.locator("html")).toHaveAttribute("data-platform-library-request",
    JSON.stringify(["net11.0", platformVersion, "System.Text.Json.dll", "netcore.app", "System.Text.Json.dll"]));
  const netCoreLibraryLocation = page.url();
  await page.locator(".type-browser .nav-back-row").click();
  await expect(subjectTab(page, "platform")).toHaveAttribute("aria-selected", "true");
  const platformLocation = page.url();
  await page.getByRole("button", { name: /System.Text.Json Implementation aspnetcore.app/ }).click();
  await expect(page.locator("html")).toHaveAttribute("data-platform-library-request",
    JSON.stringify(["net11.0", platformVersion, "System.Text.Json.dll", "aspnetcore.app", "System.Text.Json.dll"]));
  const aspNetLibraryLocation = page.url();
  await chooseInspector(page, "data-library-lens", "metadata", "Metadata");
  await expect(page.locator("html")).toHaveAttribute("data-platform-metadata-request",
    JSON.stringify(["net11.0", platformVersion, "System.Text.Json.dll", "aspnetcore.app"]));
  await page.getByRole("button", { name: "Application menu", exact: true }).press("Alt+ArrowLeft");
  await expect(page).toHaveURL(aspNetLibraryLocation);
  await page.getByRole("button", { name: "Application menu", exact: true }).press("Alt+ArrowLeft");
  await expect(page).toHaveURL(platformLocation);
  await expect(subjectTab(page, "platform")).toHaveAttribute("aria-selected", "true");
  await page.getByRole("button", { name: "Application menu", exact: true }).press("Alt+ArrowLeft");
  await expect(page).toHaveURL(netCoreLibraryLocation);
  await expect(page.locator("html")).toHaveAttribute("data-platform-library-request",
    JSON.stringify(["net11.0", platformVersion, "System.Text.Json.dll", "netcore.app", "System.Text.Json.dll"]));
  await expect(page.locator("#inspector-panel h1")).toHaveText("System.Text.Json");
  await chooseInspector(page, "data-library-lens", "metadata", "Metadata");
  await expect(page.locator("html")).toHaveAttribute("data-platform-metadata-request",
    JSON.stringify(["net11.0", platformVersion, "System.Text.Json.dll", "netcore.app"]));
  await page.reload();
  await expect(page.locator("html")).toHaveAttribute("data-platform-metadata-request",
    JSON.stringify(["net11.0", platformVersion, "System.Text.Json.dll", "netcore.app"]));
});

test("Runtime-only CoreLib in the native asset directory uses the same exact Library inspector", async ({ page }) => {
  await openPlatform(page, { nativeCoreLib: true });
  await expect(page.getByRole("button", { name: /System.Private.CoreLib/ })).toHaveCount(0);
  await page.getByLabel("Include all libraries").check();
  await page.getByRole("button", { name: /System.Private.CoreLib Private implementation/ }).click();
  await expect(page.locator("#inspector-panel h1")).toHaveText("System.Private.CoreLib");
  await chooseInspector(page, "data-library-lens", "metadata", "Metadata");
  await expect(page.locator("html")).toHaveAttribute("data-platform-metadata-request",
    JSON.stringify(["net11.0", platformVersion, "System.Private.CoreLib.dll", "netcore.app"]));
  await page.reload();
  await expect(page.locator("html")).toHaveAttribute("data-platform-metadata-request",
    JSON.stringify(["net11.0", platformVersion, "System.Private.CoreLib.dll", "netcore.app"]));
});

test("Platform requests metadata identity while sharing the exact physical Library filename", async ({ page }) => {
  await openPlatform(page, { mismatchedFile: true });
  await page.getByRole("button", { name: /System.Text.Json Implementation/ }).click();
  await expect(page.locator("html")).toHaveAttribute("data-platform-library-request",
    JSON.stringify(["net11.0", platformVersion, "System.Text.Json.dll", "netcore.app", "PhysicalPayload.dll"]));
  await expect(page.locator("#inspector-panel h1")).toHaveText("System.Text.Json");
  await chooseInspector(page, "data-library-lens", "metadata", "Metadata");
  await expect(page.locator("html")).toHaveAttribute("data-platform-metadata-request",
    JSON.stringify(["net11.0", platformVersion, "System.Text.Json.dll", "netcore.app"]));
  await page.reload();
  await expect(page.locator("html")).toHaveAttribute("data-platform-metadata-request",
    JSON.stringify(["net11.0", platformVersion, "System.Text.Json.dll", "netcore.app"]));
});

test("Package and catalog-only Platform remain distinct coordinates in the same shared Workspace", async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  await installFacades(page, surface, [], "ready", "ready", {});
  await openInstalledPlatform(page, true);
  await expect(subjectTab(page, "platform")).toHaveAttribute("aria-selected", "true");
  await page.reload();
  await expect(page.locator("#platform-version")).toHaveValue(platformVersion);
  await expect(page.locator("html")).not.toHaveAttribute("data-platform-library-request");
  await openProductDestination(page, "workspace");
  await expect(page.locator("#inspector-panel")).toContainText("2 loaded coordinates");
  await expect(page.locator("#inspector-panel")).toContainText("Example.Package");
  await expect(page.locator("[data-workspace-platform]")).toContainText(platformVersion);
  await page.locator("[data-workspace-activate]").click();
  await expect(subjectTab(page, "library")).toHaveAttribute("aria-selected", "true");
  await expect(page.locator(".library-overview-surface h1"))
    .toHaveText("All libraries");
  await openProductDestination(page, "workspace");
  await page.locator("[data-workspace-platform]").click();
  await expect(subjectTab(page, "platform")).toHaveAttribute("aria-selected", "true");
  await expect(page.locator("#platform-version")).toHaveValue(platformVersion);
});

test("an unrelated Platform history entry does not parent a Spotlight Library", async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  await installFacades(page, surface, [], "ready", "ready", {});
  await openInstalledPlatform(page, true);
  await page.getByRole("button", { name: /System.Text.Json Implementation/ }).click();
  await expect(subjectTab(page, "library")).toHaveAttribute("aria-selected", "true");
  await page.locator("[data-type-nav-back]").click();
  await expect(subjectTab(page, "platform")).toHaveAttribute("aria-selected", "true");
  await openProductDestination(page, "workspace");
  await page.locator("[data-workspace-activate]").click();
  await expect(page.locator(".inspected-target")).toContainText("Example.Package");

  await page.keyboard.press("Control+p");
  await page.locator("#spotlight-input").fill("System.Text.Json");
  await page.locator('[data-sl-framework-lib="System.Text.Json"]').click();
  await expect(page.locator("#inspector-panel h1")).toHaveText("System.Text.Json");
  await expect(subjectTab(page, "library")).toHaveAttribute("aria-selected", "true");
  await expect(subjectTab(page, "platform")).toHaveCount(0);
  await expect(page.locator("[data-type-nav-back]")).toHaveCount(0);
  await expect(page.locator(".inspected-target .subject-path")).not.toContainText("Platform");
  await openProductDestination(page, "workspace");
  await expect(page.locator("[data-workspace-platform]")).toHaveCount(0);
});

test("an unrelated Platform history entry does not parent Spotlight Types or Members", async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  await installFacades(page, surface, [], "ready", "ready", {});
  await openInstalledPlatform(page, true);
  await page.getByRole("button", { name: /System.Text.Json Implementation/ }).click();
  await expect(subjectTab(page, "library")).toHaveAttribute("aria-selected", "true");
  await page.locator("[data-type-nav-back]").click();
  await expect(subjectTab(page, "platform")).toHaveAttribute("aria-selected", "true");
  await openProductDestination(page, "workspace");
  await page.locator("[data-workspace-activate]").click();
  await expect(page.locator(".inspected-target")).toContainText("Example.Package");

  await page.keyboard.press("Control+p");
  await page.locator("#spotlight-input").fill("Widget");
  await page.locator(
    '[data-sl-type*="Example.Widget"][data-sl-pkg="Microsoft.NETCore.App"]:not([data-sl-member])',
  ).first().click();
  await expect(subjectTab(page, "type")).toHaveAttribute("aria-selected", "true");
  await expect(subjectTab(page, "platform")).toHaveCount(0);
  await expect(page.locator("[data-type-nav-back]")).toHaveAttribute("title", "Back to library");
  await expect(page.locator(".inspected-target .subject-path")).not.toContainText("Platform");
  await openProductDestination(page, "workspace");
  await expect(page.locator("[data-workspace-platform]")).toHaveCount(0);
  await page.locator("[data-workspace-activate]").click();
  await expect(page.locator(".inspected-target")).toContainText("Example.Package");

  await page.keyboard.press("Control+p");
  await page.locator("#spotlight-input").fill("Run");
  await page.locator(
    '[data-sl-member][data-sl-type*="Example.Widget"][data-sl-pkg="Microsoft.NETCore.App"]',
  ).first().click();
  await expect(subjectTab(page, "member")).toHaveAttribute("aria-selected", "true");
  await expect(subjectTab(page, "platform")).toHaveCount(0);
  await expect(page.locator(".inspected-target .subject-path")).not.toContainText("Platform");
  await openProductDestination(page, "workspace");
  await expect(page.locator("[data-workspace-platform]")).toHaveCount(0);
});

test("catalog-only Platform retains its Workspace identity and canonical URL across another Workspace", async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  await page.addInitScript(() => localStorage.setItem(
    "inspect-recent-packages",
    JSON.stringify([{ id: "Second.Package", version: "1.0.0", framework: "net10.0" }]),
  ));
  await openPlatform(page);
  const platformLocation = page.url();
  const platformWorkspace = await currentWorkspaceHistoryState(page);
  expect(platformWorkspace.id).not.toBeNull();

  await page.keyboard.press("Control+p");
  await page.locator('[data-sl-pkg-recent="Second.Package"]').click();
  await expect(page.locator(".inspected-target")).toContainText("Second.Package");
  expect((await currentWorkspaceHistoryState(page)).id).not.toBe(platformWorkspace.id);

  await page.goBack();
  await expect(page).toHaveURL(platformLocation);
  await expect(subjectTab(page, "platform")).toHaveAttribute("aria-selected", "true");
  expect(await currentWorkspaceHistoryState(page)).toEqual(platformWorkspace);
  await expect(page.locator("#platform-version")).toHaveValue(platformVersion);
  await expect(page.locator("html")).not.toHaveAttribute("data-platform-library-request");

  await openProductDestination(page, "workspace");
  await expect(page.locator("[data-workspace-select]")).toContainText("1 loaded coordinate");
  await expect(page.locator("[data-workspace-switch]")).toHaveCount(1);
  await page.locator("[data-workspace-platform]").click();
  await expect(page).toHaveURL(platformLocation);
});

test("Platform descendant history and retained switching preserve the real parent", async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  await page.addInitScript(() => localStorage.setItem(
    "inspect-recent-packages",
    JSON.stringify([{ id: "Second.Package", version: "1.0.0", framework: "net10.0" }]),
  ));
  await openPlatform(page);
  await page.getByRole("button", { name: /System.Text.Json Implementation/ }).click();
  await expect(page.locator("[data-type-nav-back]")).toHaveAttribute("title", "Back to platform");

  await page.getByRole(
    "button",
    { name: "Search types, members, packages", exact: true },
  ).click();
  await page.locator("#spotlight-input").fill("Widget");
  await page.locator('[data-sl-type*="Example.Widget"]:not([data-sl-member])').first().click();
  await expect(subjectTab(page, "type")).toHaveAttribute("aria-selected", "true");
  await expect(subjectTab(page, "platform")).toHaveAttribute("aria-selected", "false");
  await page.getByRole("button", { name: "Application menu", exact: true }).press("Alt+ArrowLeft");
  await expect(subjectTab(page, "library")).toHaveAttribute("aria-selected", "true");
  await page.getByRole("button", { name: "Application menu", exact: true }).press("Alt+ArrowRight");
  await expect(subjectTab(page, "type")).toHaveAttribute("aria-selected", "true");
  await expect(subjectTab(page, "platform")).toHaveAttribute("aria-selected", "false");

  await page.getByRole(
    "button",
    { name: "Search types, members, packages", exact: true },
  ).click();
  await page.locator("#spotlight-input").fill("Run");
  await page.locator('[data-sl-member][data-sl-type*="Example.Widget"]').first().click();
  await expect(subjectTab(page, "member")).toHaveAttribute("aria-selected", "true");
  await expect(subjectTab(page, "platform")).toHaveAttribute("aria-selected", "false");

  await page.keyboard.press("Control+p");
  await page.locator('[data-sl-pkg-recent="Second.Package"]').click();
  await expect(page.locator(".inspected-target")).toContainText("Second.Package");
  await openProductDestination(page, "workspace");
  await page.locator("[data-workspace-switch]").click();

  await expect(subjectTab(page, "member")).toHaveAttribute("aria-selected", "true");
  await expect(page.locator("#inspector-panel h1")).toContainText("Run");
  await expect(subjectTab(page, "platform")).toHaveAttribute("aria-selected", "false");
});

test("a fresh Spotlight Library preserves the predecessor Platform parent", async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  await openPlatform(page);
  await page.getByRole("button", { name: /System.Text.Json Implementation/ }).click();
  await expect(page.locator("[data-type-nav-back]")).toHaveAttribute("title", "Back to platform");
  const predecessorLocation = page.url();
  const predecessorWorkspace = await currentWorkspaceHistoryState(page);

  await page.getByRole(
    "button",
    { name: "Search types, members, packages", exact: true },
  ).click();
  await page.locator("#spotlight-input").fill("System.Facade");
  await releaseFacade(page, "hold-workspace-encode");
  await page.locator('[data-sl-framework-lib="System.Facade"]').click();
  await expect(page.locator("html")).toHaveAttribute("data-workspace-encode-pending", "true");
  await expect(page.locator("#inspector-panel h1")).toHaveText("System.Facade");
  await expect(subjectTab(page, "platform")).toHaveCount(0);
  await expect(page.locator("[data-type-nav-back]")).toHaveCount(0);
  await releaseFacade(page, "finish-workspace-encode");
  await page.getByRole("button", { name: "Application menu", exact: true }).click();
  await page.getByRole("menuitem", { name: "Settings", exact: true }).click();
  await page.locator('#settings-dialog [data-theme="light"]').click();
  await page.keyboard.press("Escape");
  await expect(page.locator("#inspector-panel h1")).toHaveText("System.Facade");
  await expect(subjectTab(page, "platform")).toHaveCount(0);
  await expect(page.getByRole("button", { name: "Back", exact: true })).toBeDisabled();

  await page.goBack();
  await expect(page).toHaveURL(predecessorLocation);
  await expect.poll(() => currentWorkspaceHistoryState(page)).toEqual(predecessorWorkspace);
  await expect(page.locator("#inspector-panel h1")).toHaveText("System.Text.Json");
  await expect(page.locator("[data-type-nav-back]")).toHaveAttribute("title", "Back to platform");
  await page.reload();
  await expect(page.locator("#inspector-panel h1")).toHaveText("System.Text.Json");
  await expect(page.locator("[data-type-nav-back]")).toHaveAttribute("title", "Back to platform");
});

test("restored Platform failure retries its own Library request", async ({ page }) => {
  await page.addInitScript(() => localStorage.setItem(
    "inspect-recent-packages",
    JSON.stringify([{ id: "Second.Package", version: "1.0.0", framework: "net10.0" }]),
  ));
  await openPlatform(page, { libraryFailure: true });
  await page.getByRole("button", { name: /System.Text.Json Implementation/ }).click();
  await expect(page.locator("#inspector-panel")).toContainText(
    "Could not open Platform Library: Library offline",
  );
  const firstWorkspace = await currentWorkspaceHistoryState(page);

  await page.keyboard.press("Control+p");
  await page.locator('[data-sl-pkg-recent="Second.Package"]').click();
  await expect(page.locator(".inspected-target")).toContainText("Second.Package");

  await page.goBack();
  await expect.poll(() => currentWorkspaceHistoryState(page)).toEqual(firstWorkspace);
  await expect(page.locator("#inspector-panel")).toContainText(
    "Could not open Platform Library: Library offline",
  );
  await page.locator('[data-platform-retry="library"]').click();
  await expect(page.locator("html")).toHaveAttribute(
    "data-platform-library-request",
    JSON.stringify([
      "net11.0",
      platformVersion,
      "System.Text.Json.dll",
      "netcore.app",
      "System.Text.Json.dll",
    ]),
  );
});

for (const pendingRequest of ["Library", "catalog"] as const) {
  test(`restored Platform ${pendingRequest} cancellation becomes retryable`, async ({ page }) => {
    await page.addInitScript(() => localStorage.setItem(
      "inspect-recent-packages",
      JSON.stringify([{ id: "Second.Package", version: "1.0.0", framework: "net10.0" }]),
    ));
    await openPlatform(page, pendingRequest === "Library"
      ? { libraryPending: true, libraryFailure: true }
      : { catalogPending: true, catalogFailure: true });
    const platformWorkspace = await currentWorkspaceHistoryState(page);

    if (pendingRequest === "Library") {
      await page.getByRole("button", { name: /System.Text.Json Implementation/ }).click();
      await expect(page.locator("#inspector-panel")).toContainText(
        "Opening the selected Library...",
      );
    } else {
      await page.getByLabel("Platform version", { exact: true })
        .selectOption(alternatePlatformVersion);
      await expect(page.locator("#inspector-panel")).toContainText(
        "Loading Platform catalog...",
      );
    }

    await page.keyboard.press("Control+p");
    await page.locator('[data-sl-pkg-recent="Second.Package"]').click();
    await expect(page.locator(".inspected-target")).toContainText("Second.Package");
    await releaseFacade(
      page,
      pendingRequest === "Library"
        ? "finish-platform-library"
        : "finish-platform-catalog",
    );

    await page.goBack();
    await expect.poll(() => currentWorkspaceHistoryState(page)).toEqual(platformWorkspace);
    await expect(page.locator("#inspector-panel")).toContainText(
      pendingRequest === "Library"
        ? "Platform Library opening was interrupted."
        : "Platform catalog loading was interrupted.",
    );
    await expect(page.locator(
      `[data-platform-retry="${pendingRequest === "Library" ? "library" : "catalog"}"]`,
    )).toBeEnabled();
  });
}

test("same-Workspace navigation retires superseded Platform catalog progress", async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  await openPlatform(page, { catalogPending: true });
  await page.getByLabel("Platform version", { exact: true })
    .selectOption(alternatePlatformVersion);
  await expect(page.locator("#inspector-panel")).toContainText(
    "Loading Platform catalog...",
  );

  await page.getByRole("button", { name: /System.Text.Json Implementation/ }).click();
  await expect(subjectTab(page, "library")).toHaveAttribute(
    "aria-selected",
    "true",
  );
  await releaseFacade(page, "finish-platform-catalog");
  await chooseSubject(page, "platform", "Platform");

  await expect(page.locator("#inspector-panel")).toContainText(
    "Platform catalog loading was interrupted.",
  );
  await expect(page.locator('[data-platform-retry="catalog"]')).toBeEnabled();
  await expect(page.locator("#inspector-panel")).not.toContainText(
    "Loading Platform catalog...",
  );
});
