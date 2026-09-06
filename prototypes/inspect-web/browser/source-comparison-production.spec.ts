import { expect, test, type Page } from "@playwright/test";
import { writeFile } from "node:fs/promises";

const site = process.env.INSPECT_WEB_SOURCE_DIFF_URL;
const fixtureOnly = process.env.INSPECT_WEB_SOURCE_DIFF_FIXTURE_ONLY === "1";
const beforePackage = process.env.INSPECT_WEB_SOURCE_DIFF_BEFORE_PACKAGE;
const afterPackage = process.env.INSPECT_WEB_SOURCE_DIFF_AFTER_PACKAGE;
const beforeSource = process.env.INSPECT_WEB_SOURCE_DIFF_BEFORE_SOURCE;
const afterSource = process.env.INSPECT_WEB_SOURCE_DIFF_AFTER_SOURCE;

async function openPublishedSite(page: Page, url = site!): Promise<void> {
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

test.describe("published authored Source comparison", () => {
  test.skip(!site, "Set INSPECT_WEB_SOURCE_DIFF_URL to the published Wasm site.");
  test.setTimeout(180_000);

  test("public package versions retain independent Source outcomes through the generated facade",
    async ({ page }, testInfo) => {
      test.skip(fixtureOnly, "The CI gate uses deterministic acquired artifacts.");
      await openPublishedSite(page);
      const evidence = await page.evaluate(async () => {
        const packages = await import("/inspect-web-package.js");
        const source = await import("/inspect-web-source.js");
        const surface = await packages.queryPackage(
          "Microsoft.Extensions.Primitives", "10.0.0", "net10.0",
        );
        const type = surface.types.find(candidate =>
          candidate.definitionId === "Microsoft.Extensions.Primitives.StringSegment");
        const member = type?.api.find(candidate => candidate.name === "Trim");
        const body = member?.bodySelectors.find(candidate =>
          candidate.token === member.metadataToken);
        if (!type || !body)
          throw new Error("The real package does not expose StringSegment.Trim.");
        const request = {
          packageId: surface.package,
          beforeVersion: surface.version,
          afterVersion: "10.0.1",
          framework: surface.activeFramework,
          assembly: type.assemblyId,
          typeIdentity: type.definitionId,
          memberName: body.memberName,
          selectorKey: body.selectorKey,
          metadataToken: body.token,
        };
        const compared = await source.queryMemberSourceComparison(
          "source-comparison-public-pair", JSON.stringify(request),
        );
        const same = await source.queryMemberSourceComparison(
          "source-comparison-public-same",
          JSON.stringify({ ...request, afterVersion: request.beforeVersion }),
        );
        return { request, compared, same };
      });
      const evidencePath = testInfo.outputPath("public-source-comparisons.json");
      await writeFile(evidencePath, JSON.stringify(evidence, null, 2));
      await testInfo.attach("public-source-comparisons.json", {
        path: evidencePath, contentType: "application/json",
      });
      for (const result of [evidence.compared, evidence.same]) {
        expect(result.kind).toBe("Succeeded");
        expect(result.value).not.toBeNull();
        expect(result.value!.before.version).toBe("10.0.0");
        expect(result.value!.before.memberIdentity).not.toBeNull();
        expect(result.value!.after.memberIdentity).not.toBeNull();
        for (const endpoint of [result.value!.before, result.value!.after]) {
          expect(endpoint.packageId.toLowerCase()).toBe("microsoft.extensions.primitives");
          expect(endpoint.moduleVersionId).not.toBeNull();
          if (endpoint.state === "Available") {
            expect(endpoint.text).toContain("Trim");
          } else {
            expect(endpoint.detail).toBeTruthy();
            expect(endpoint.text).toBeNull();
          }
        }
      }
      expect(evidence.compared.value!.request).toEqual(evidence.request);
      expect(evidence.compared.value!.after.version).toBe("10.0.1");
      if (evidence.same.value!.status === "Compared")
        expect(evidence.same.value!.isExact).toBe(true);
      else
        expect(evidence.same.value!.isExact).toBe(false);
    });

  test("real dialog submits an explicit version and preserves navigation and focus",
    async ({ page }, testInfo) => {
      test.skip(fixtureOnly, "The CI gate uses deterministic acquired artifacts.");
      const location = new URL(site!);
      location.search = new URLSearchParams({
        package: "Microsoft.Extensions.Primitives",
        version: "10.0.0",
        framework: "net10.0",
      }).toString();
      await openPublishedSite(page, location.href);
      await page.locator("[data-type]").filter({
        has: page.getByText("StringSegment", { exact: true }),
      }).first().click({ timeout: 90_000 });
      await page.locator("button.api-row[data-member]").filter({
        hasText: /\bTrim\b/,
      }).click();
      await page.locator('[data-member-section="source"]').click();
      const action = page.locator("#compare-authored-source");
      await expect(action).toBeEnabled();
      const beforeUrl = page.url();
      const beforeHistory = await page.evaluate(() => history.length);
      await action.click();
      const dialog = page.locator("#source-diff-modal");
      const version = dialog.locator("#source-diff-after-version");
      const compare = dialog.locator("#source-diff-compare");
      await expect(dialog).toBeVisible();
      await expect(version).toBeFocused();
      await expect(compare).toBeDisabled();
      await expect(dialog.locator("[data-source-diff-status]")).toHaveCount(0);
      await version.fill("10.0.1");
      await expect(compare).toBeEnabled();
      await expect(dialog.locator("[data-source-diff-status]")).toHaveCount(0);
      await compare.click();
      await expect(dialog.locator("[data-source-diff-status]")).toBeVisible({
        timeout: 90_000,
      });
      await expect(dialog.locator("[data-source-diff-submitted]"))
        .toContainText("Before 10.0.0");
      await expect(dialog.locator("[data-source-diff-submitted]"))
        .toContainText("After 10.0.1");
      await expect(dialog.locator('[data-source-diff-side="before"]'))
        .toContainText("10.0.0");
      await expect(dialog.locator('[data-source-diff-side="after"]'))
        .toContainText("10.0.1");
      await testInfo.attach("source-diff-public-dialog", {
        body: await dialog.screenshot(), contentType: "image/png",
      });
      await version.fill("10.0.0");
      await expect(dialog.locator("[data-source-diff-status]")).toHaveCount(0);
      await compare.click();
      await expect(dialog.locator("[data-source-diff-status]")).toBeVisible({
        timeout: 90_000,
      });
      const status = await dialog.locator("[data-source-diff-status]").textContent();
      if (status === "Compared")
        await expect(dialog.locator("[data-source-diff-verdict]"))
          .toHaveText("Exact authored source");
      else
        await expect(dialog).toContainText("Not compared");
      await page.keyboard.press("Escape");
      await expect(dialog).toHaveCount(0);
      await expect(action).toBeFocused();
      expect(page.url()).toBe(beforeUrl);
      expect(await page.evaluate(() => history.length)).toBe(beforeHistory);
    });

  test("cataloged Source-only, exact, moved, and unavailable declarations reach the real dialog",
    async ({ page }, testInfo) => {
      test.skip(!beforePackage || !afterPackage || !beforeSource || !afterSource,
        "Set the four catalog-resolved Source comparison fixture assets.");
      let omitAfterSource = false;
      await page.route("**/inspectweb.sourcecomparisonfixture*.nupkg", async route => {
        const version = route.request().url().includes(".2.0.0.nupkg") ? 2 : 1;
        await route.fulfill({
          path: version === 1 ? beforePackage! : afterPackage!,
          contentType: "application/octet-stream",
          headers: { "access-control-allow-origin": "*" },
        });
      });
      await page.route("https://raw.githubusercontent.com/dotnet-inspect-fixtures/source-comparison/**",
        async route => {
          const after = route.request().url().includes("/source-comparison/v2/");
          if (after && omitAfterSource) {
            await route.fulfill({
              status: 404, body: "Fixture source unavailable.",
              headers: { "access-control-allow-origin": "*" },
            });
            return;
          }
          await route.fulfill({
            path: after ? afterSource! : beforeSource!,
            contentType: "text/plain",
            headers: { "access-control-allow-origin": "*" },
          });
        });
      const location = new URL(site!);
      location.search = new URLSearchParams({
        package: "InspectWeb.SourceComparisonFixture",
        version: "1.0.0",
        framework: "net11.0",
      }).toString();
      await openPublishedSite(page, location.href);
      await page.locator("[data-type]").filter({
        has: page.getByText("Counter", { exact: true }),
      }).first().click({ timeout: 90_000 });
      const dialog = page.locator("#source-diff-modal");

      async function compareMember(name: string): Promise<void> {
        await page.locator("button.api-row[data-member]").filter({
          hasText: new RegExp(`\\b${name}\\b`),
        }).click();
        await page.locator('[data-member-section="source"]').click();
        await page.locator("#compare-authored-source").click();
        await expect(dialog.locator("#source-diff-after-version")).toBeFocused();
        await dialog.locator("#source-diff-after-version").fill("2.0.0");
        await expect(dialog.locator("[data-source-diff-status]")).toHaveCount(0);
        await dialog.locator("#source-diff-compare").click();
        await expect(dialog.locator("[data-source-diff-status]")).toBeVisible({
          timeout: 90_000,
        });
      }

      async function leaveMember(): Promise<void> {
        await page.keyboard.press("Escape");
        await expect(dialog).toHaveCount(0);
        await expect(page.locator("#compare-authored-source")).toBeFocused();
        await page.getByRole("tab", { name: "Type", exact: true }).click();
        await expect(page.locator("button.api-row[data-member]").first()).toBeVisible();
      }

      await compareMember("Value");
      await expect(dialog.locator("[data-source-diff-status]")).toHaveText("Compared");
      await expect(dialog.locator("[data-source-diff-exact]")).toHaveAttribute(
        "data-source-diff-exact", "false");
      await expect(dialog.locator('[data-source-diff-kind="Removed"]')).toContainText("1 + 2");
      await expect(dialog.locator('[data-source-diff-kind="Added"]')).toContainText("=> 3");
      await testInfo.attach("source-only-dialog", {
        body: await dialog.screenshot(), contentType: "image/png",
      });
      await leaveMember();

      await compareMember("Unchanged");
      await expect(dialog.locator("[data-source-diff-verdict]"))
        .toHaveText("Exact authored source");
      await leaveMember();

      for (const member of ["MovedBlock", "MovedBlockAndEdit"]) {
        await compareMember(member);
        await expect(dialog.locator("[data-source-diff-status]")).toHaveText("Compared");
        const moves = dialog.locator('[data-source-diff-difference="Moved"]');
        await expect(moves).toHaveCount(2);
        await expect(moves.first()).toContainText("Before · line 3");
        await expect(moves.first()).toContainText("After · line 5");
        await expect(moves.first()).toContainText("First annotation.");
        await expect(moves.last()).toContainText("Before · line 4");
        await expect(moves.last()).toContainText("After · line 6");
        if (member === "MovedBlockAndEdit") {
          await expect(dialog.locator('[data-source-diff-kind="Removed"]')).toHaveCount(1);
          await expect(dialog.locator('[data-source-diff-kind="Added"]')).toHaveCount(1);
        }
        await testInfo.attach(`${member}-dialog`, {
          body: await dialog.screenshot(), contentType: "image/png",
        });
        await leaveMember();
      }

      omitAfterSource = true;
      await compareMember("Value");
      await expect(dialog.locator("[data-source-diff-status]")).toHaveText("Unavailable");
      const before = dialog.locator('[data-source-diff-side="before"]');
      const after = dialog.locator('[data-source-diff-side="after"]');
      await expect(before.locator("[data-source-diff-endpoint-state]")).toHaveText("Available");
      await expect(before.locator("pre")).toContainText("1 + 2");
      await expect(after).toContainText("No authored declaration is available");
      await expect(dialog).toContainText("Not compared");
      await expect(dialog.locator("[data-source-diff-kind]")).toHaveCount(0);
      await testInfo.attach("unavailable-source-dialog", {
        body: await dialog.screenshot(), contentType: "image/png",
      });
    });
});
