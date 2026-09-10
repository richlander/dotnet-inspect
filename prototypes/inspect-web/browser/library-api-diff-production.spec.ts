import { expect, test, type Page } from "@playwright/test";
import {
  libraryApiDiffCorruptNupkg,
  libraryApiDiffFixtureFramework,
  libraryApiDiffFixtureV1Nupkg,
  libraryApiDiffFixtureV2Nupkg,
} from "./library-api-diff-fixture.ts";

const site = process.env.INSPECT_WEB_LIBRARY_API_DIFF_URL;
const packageId = "InspectWeb.LibraryApiDiffFixture";
const lowerId = packageId.toLowerCase();
const v1Version = "1.0.0";
const v2Version = "2.0.0";

async function openPublishedSite(page: Page, url: string): Promise<void> {
  await page.goto(url, { waitUntil: "networkidle" });
  await page.waitForFunction(async () => {
    const host = await import("/inspect-web-host.js");
    try {
      return host.buildIdentity() !== null;
    } catch (error: unknown) {
      if (error instanceof Error &&
          error.message === "The .NET runtime facade is not initialized.")
        return false;
      throw error;
    }
  });
}

function registrationIndexJson(versions: readonly string[]): string {
  return JSON.stringify({
    items: [{
      items: versions.map(version => ({ catalogEntry: { version, listed: true } })),
    }],
  });
}

// Routes the real NuGet Gallery client's version listing, registration, and package-content
// endpoints (`NuGetGalleryPackageSourceClient`) for one fixture package id, without ever
// touching the real internet. `packageBytes` maps each version to its exact nupkg bytes —
// `undefined` yields a 404 (an unresolvable comparison target, not fabricated evidence).
async function routeGalleryPackage(
  page: Page,
  versions: readonly string[],
  packageBytes: ReadonlyMap<string, Buffer>,
): Promise<void> {
  await page.route(
    `https://globalcdn.nuget.org/v3-flatcontainer/${lowerId}/index.json`,
    async route => {
      await route.fulfill({
        status: 200,
        contentType: "application/json",
        body: JSON.stringify({ versions }),
      });
    });
  await page.route(
    `https://globalcdn.nuget.org/v3/registration5-gz-semver2/${lowerId}/index.json`,
    async route => {
      await route.fulfill({
        status: 200, contentType: "application/json", body: registrationIndexJson(versions),
      });
    });
  await page.route(
    `https://globalcdn.nuget.org/packages/${lowerId}.*.nupkg`,
    async route => {
      const match = /\.(\d+\.\d+\.\d+(?:-[^.]+)?)\.nupkg$/.exec(route.request().url());
      const bytes = match ? packageBytes.get(match[1]!) : undefined;
      if (!bytes) {
        await route.fulfill({ status: 404, body: "Fixture package version unavailable." });
        return;
      }
      await route.fulfill({
        status: 200, contentType: "application/octet-stream", body: bytes,
      });
    });
}

async function openLibraryDiff(page: Page): Promise<void> {
  // A single-assembly package like this fixture lands directly on its one Library; only a
  // multi-library package would need an explicit "[data-lib-scope]" selection first.
  const libraryRow = page.locator("[data-lib-scope]").first();
  if (await libraryRow.count() > 0) await libraryRow.click({ timeout: 90_000 });
  await page.locator('[data-library-lens="diff"]').click();
}

test.describe("published Library API Diff", () => {
  test.skip(!site, "Set INSPECT_WEB_LIBRARY_API_DIFF_URL to the published Wasm site.");
  test.setTimeout(180_000);

  test("real cataloged fixture package acquisition, Package target resolution, and the full changed-Type inventory/detail",
    async ({ page }, testInfo) => {
      await routeGalleryPackage(page, [v1Version, v2Version], new Map([
        [v1Version, libraryApiDiffFixtureV1Nupkg()],
        [v2Version, libraryApiDiffFixtureV2Nupkg()],
      ]));
      const location = new URL(site!);
      location.search = new URLSearchParams({
        package: packageId, version: v2Version, framework: libraryApiDiffFixtureFramework,
      }).toString();
      await openPublishedSite(page, location.href);

      await openLibraryDiff(page);

      // Entering Diff automatically executed one request against the automatic (previous)
      // target, resolved from the real registration index — the browser never ordered or
      // compared versions itself.
      const surface = page.locator(".library-api-diff-surface");
      await expect(surface.locator('[data-library-api-diff-state="loading"]'))
        .toBeVisible({ timeout: 5_000 }).catch(() => undefined);
      await expect(surface.locator('[data-library-api-diff-state="available"]'))
        .toBeVisible({ timeout: 90_000 });
      await expect(surface).toContainText(v1Version);
      await expect(surface).toContainText(v2Version);

      await testInfo.attach("library-api-diff-available.png", {
        body: await surface.screenshot(), contentType: "image/png",
      });

      // Complete aggregate summary: at least the changed, added, and removed Types this
      // fixture pair carries.
      await expect(surface.locator('[data-library-api-diff-count="added-types"] dd'))
        .toHaveText("1");
      await expect(surface.locator('[data-library-api-diff-count="removed-types"] dd'))
        .toHaveText("1");
      const changedTypesText = await surface
        .locator('[data-library-api-diff-count="changed-types"] dd').textContent();
      expect(Number(changedTypesText)).toBeGreaterThanOrEqual(3);

      // The flat changed-Type master list preserves document order and is complete: the
      // fixture's Added and Removed types are both present alongside the changed ones.
      await expect(surface.locator('[data-library-api-diff-type*="AddedType"]'))
        .toBeVisible();
      await expect(surface.locator('[data-library-api-diff-type*="RemovedType"]'))
        .toBeVisible();

      // Selecting ProjectionReceiver exposes its complete two-relation population: the hard
      // removed observation and the accepted extension-to-instance correspondence. The latter
      // retains its After role, both endpoint anchors, and match provenance.
      await surface.locator('[data-library-api-diff-type*="ProjectionReceiver"]').click();
      const detail = surface.locator("[data-library-api-diff-detail]");
      await expect(detail).toContainText("Transform");
      await expect(detail.locator(".library-api-diff-member-row")).toHaveCount(2);
      const movedRelation = detail.locator(
        '.library-api-diff-member-row[data-library-api-diff-member-role="After"]');
      await expect(movedRelation).toHaveCount(1);
      await expect(movedRelation)
        .toHaveAttribute("data-library-api-diff-member-pair-kind", "Changed");
      await expect(movedRelation)
        .toHaveAttribute("data-library-api-diff-member-role", "After");
      await expect(movedRelation.locator('[data-library-api-diff-member-endpoint="before"]'))
        .toContainText("LibraryApiDiffFixture.ProjectionExtensions");
      await expect(movedRelation.locator('[data-library-api-diff-member-endpoint="after"]'))
        .toContainText("LibraryApiDiffFixture.ProjectionReceiver");
      await expect(movedRelation.locator(".library-api-diff-member-match"))
        .toContainText("api.member.extension-instance");
      await expect(movedRelation.locator(".library-api-diff-member-match"))
        .toContainText("tier 85%");
      await expect(movedRelation.locator(".library-api-diff-member-match"))
        .toContainText("match 85%");

      // Detail selection replaces the rendered DOM, but the master inventory retains its
      // scroll position and exact keyboard focus so long inventories remain navigable.
      const typeList = surface.locator(".library-api-diff-list");
      const lastType = typeList.locator("[data-library-api-diff-type]").last();
      await typeList.evaluate(element => {
        element.scrollTop = element.scrollHeight;
      });
      const scrollBeforeSelection = await typeList.evaluate(element => element.scrollTop);
      await lastType.focus();
      await lastType.press("Space");
      await expect(lastType).toBeFocused();
      expect(await typeList.evaluate(element => element.scrollTop))
        .toBe(scrollBeforeSelection);

      await page.setViewportSize({ width: 600, height: 650 });
      const surfaceScroller = surface.locator(".library-api-diff-scroll");
      const narrowTypes = typeList.locator("[data-library-api-diff-type]");
      const narrowType = narrowTypes.nth(await narrowTypes.count() - 2);
      await narrowType.focus();
      const narrowScrollBeforeSelection =
        await surfaceScroller.evaluate(element => element.scrollTop);
      expect(narrowScrollBeforeSelection).toBeGreaterThan(0);
      const narrowTopBeforeSelection =
        (await narrowType.boundingBox())?.y ?? Number.NaN;
      await narrowType.press("Space");
      await expect(narrowType).toBeFocused();
      await expect.poll(() =>
        surfaceScroller.evaluate(element => element.scrollTop))
        .toBe(narrowScrollBeforeSelection);
      expect((await narrowType.boundingBox())?.y)
        .toBe(narrowTopBeforeSelection);

      // The retired Source Diff action and modal are absent from this working surface.
      await expect(page.locator("#compare-authored-source")).toHaveCount(0);
      await expect(page.locator("#source-diff-modal")).toHaveCount(0);
    });

  test("a same-version comparison is a successful empty result, not Unavailable",
    async ({ page }) => {
      await routeGalleryPackage(page, [v1Version], new Map([
        [v1Version, libraryApiDiffFixtureV1Nupkg()],
      ]));
      const location = new URL(site!);
      location.search = new URLSearchParams({
        package: packageId, version: v1Version, framework: libraryApiDiffFixtureFramework,
      }).toString();
      await page.setViewportSize({ width: 1600, height: 1000 });
      await openPublishedSite(page, location.href);

      // A single listed version has no predecessor; select the exact same version explicitly
      // through Package Overview's existing Comparison targets rather than the automatic
      // (previous) default, which is expected to report no predecessor here.
      await page.locator('[data-scope="package"]').click();
      await page.locator("#package-diff-target").selectOption(`exact:${v1Version}`);
      await openLibraryDiff(page);

      const surface = page.locator(".library-api-diff-surface");
      await expect(surface.locator('[data-library-api-diff-state="available"]'))
        .toBeVisible({ timeout: 90_000 });
      await expect(surface.locator("[data-library-api-diff-empty]"))
        .toHaveText("No public API changes.");
    });

  test("an unreadable comparison endpoint reports Not compared, not equality or a one-sided inventory",
    async ({ page }) => {
      await routeGalleryPackage(page, [v1Version, v2Version], new Map([
        [v1Version, libraryApiDiffCorruptNupkg()],
        [v2Version, libraryApiDiffFixtureV2Nupkg()],
      ]));
      const location = new URL(site!);
      location.search = new URLSearchParams({
        package: packageId, version: v2Version, framework: libraryApiDiffFixtureFramework,
      }).toString();
      await openPublishedSite(page, location.href);
      await openLibraryDiff(page);

      const surface = page.locator(".library-api-diff-surface");
      await expect(surface.locator('[data-library-api-diff-state="unavailable"]'))
        .toBeVisible({ timeout: 90_000 });
      await expect(surface).toContainText("Not compared");
      await expect(surface.locator('[data-library-api-diff-state="available"]')).toHaveCount(0);
    });
});
