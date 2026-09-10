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

// The retired UI drove a modal (#compare-authored-source / #source-diff-modal) that no
// longer exists (#6423's atomic Source Diff retirement). This suite now exercises only the
// paired Source comparison facade directly through the published generated module, which
// remains available for a later on-demand annotated comparison consumer.
test.describe("published authored Source comparison facade", () => {
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

  test("cataloged Source-only, exact, moved, and unavailable declarations reach the real managed facade, with the retired UI absent",
    async ({ page }) => {
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

      // Every case calls the generated facade directly, exactly as the retired dialog's
      // "compare" action used to — only the DOM walk is gone.
      async function compareMember(name: string, afterVersion = "2.0.0") {
        return page.evaluate(async (evaluateArgs: { name: string; afterVersion: string }) => {
          const packages = await import("/inspect-web-package.js");
          const source = await import("/inspect-web-source.js");
          const surface = await packages.queryPackage(
            "InspectWeb.SourceComparisonFixture", "1.0.0", "net11.0");
          const type = surface.types.find(candidate => candidate.name === "Counter");
          const member = type?.api.find(candidate => candidate.name === evaluateArgs.name);
          const body = member?.bodySelectors.find(candidate =>
            candidate.token === member.metadataToken);
          if (!type || !member || !body)
            throw new Error(`The fixture package does not expose Counter.${evaluateArgs.name}.`);
          const request = {
            packageId: surface.package,
            beforeVersion: surface.version,
            afterVersion: evaluateArgs.afterVersion,
            framework: surface.activeFramework,
            assembly: type.assemblyId,
            typeIdentity: type.definitionId,
            memberName: body.memberName,
            selectorKey: body.selectorKey,
            metadataToken: body.token,
          };
          return source.queryMemberSourceComparison(
            `source-comparison-${evaluateArgs.name}-${evaluateArgs.afterVersion}`,
            JSON.stringify(request));
        }, { name, afterVersion });
      }

      const value = await compareMember("Value");
      expect(value.kind).toBe("Succeeded");
      expect(value.value!.status).toBe("Compared");
      expect(value.value!.isExact).toBe(false);
      expect(value.value!.lines.some(line =>
        line.kind === "Removed" && line.beforeText?.includes("1 + 2"))).toBe(true);
      expect(value.value!.lines.some(line =>
        line.kind === "Added" && line.afterText?.includes("=> 3"))).toBe(true);

      const unchanged = await compareMember("Unchanged");
      expect(unchanged.value!.status).toBe("Compared");
      expect(unchanged.value!.isExact).toBe(true);

      for (const member of ["MovedBlock", "MovedBlockAndEdit"]) {
        const moved = await compareMember(member);
        expect(moved.value!.status).toBe("Compared");
        const moves = moved.value!.lines.filter(line => line.difference === "Moved");
        expect(moves).toHaveLength(2);
        expect(moves[0]!.beforeLine).toBe(3);
        expect(moves[0]!.afterLine).toBe(5);
        expect(moves[0]!.beforeText).toContain("First annotation.");
        expect(moves[1]!.beforeLine).toBe(4);
        expect(moves[1]!.afterLine).toBe(6);
        if (member === "MovedBlockAndEdit") {
          expect(moved.value!.lines.filter(line => line.kind === "Removed")).toHaveLength(1);
          expect(moved.value!.lines.filter(line => line.kind === "Added")).toHaveLength(1);
        }
      }

      omitAfterSource = true;
      const unavailable = await compareMember("Value");
      expect(unavailable.value!.status).toBe("Unavailable");
      expect(unavailable.value!.before.state).toBe("Available");
      expect(unavailable.value!.before.text).toContain("1 + 2");
      expect(unavailable.value!.after.text).toBeNull();
      expect(unavailable.value!.after.detail).toBeTruthy();

      // The retired Compare authored source action and Source Diff modal are absent from
      // this same published page, including from a Member's Source section.
      await page.locator("[data-type]").filter({
        has: page.getByText("Counter", { exact: true }),
      }).first().click();
      await page.locator("button.api-row[data-member]").first().click();
      const memberSection = page.locator('[data-member-section="source"]');
      if (await memberSection.count() > 0) await memberSection.click();
      await expect(page.locator("#compare-authored-source")).toHaveCount(0);
      await expect(page.locator("#source-diff-modal")).toHaveCount(0);
      await expect(page.locator("#source-diff-backdrop")).toHaveCount(0);
    });
});
