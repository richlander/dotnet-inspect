import { expect, test, type Page } from "@playwright/test";
import { writeFile } from "node:fs/promises";

const site = process.env.INSPECT_WEB_SOURCE_DIFF_URL;
const fixtureOnly = process.env.INSPECT_WEB_SOURCE_DIFF_FIXTURE_ONLY === "1";
const beforePackage = process.env.INSPECT_WEB_SOURCE_DIFF_BEFORE_PACKAGE;
const afterPackage = process.env.INSPECT_WEB_SOURCE_DIFF_AFTER_PACKAGE;
const beforeSource = process.env.INSPECT_WEB_SOURCE_DIFF_BEFORE_SOURCE;
const afterSource = process.env.INSPECT_WEB_SOURCE_DIFF_AFTER_SOURCE;

async function openPublishedSite(page: Page): Promise<void> {
  await page.goto(site!, { waitUntil: "networkidle" });
  await page.setContent("<!doctype html><title>Source facade test</title>");
  await page.evaluate(async origin => {
    const host = await import("/inspect-web-host.js");
    const packages = await import("/inspect-web-package.js");
    const source = await import("/inspect-web-source.js");
    const runtime = host.createRuntime();
    await host.initializeRuntime(runtime);
    await packages.initializeRuntime(runtime);
    await source.initializeRuntime(runtime);
    host.configureHost(origin);
    await host.runEntryPoint();
  }, new URL(site!).origin);
}

test.describe("published authored Source comparison transport", () => {
  test.skip(!site, "Set INSPECT_WEB_SOURCE_DIFF_URL to the published Wasm site.");
  test.setTimeout(180_000);

  test("public package versions retain independent Source outcomes",
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
          candidate.definitionId
            === "Microsoft.Extensions.Primitives.StringSegment");
        const member = type?.api.find(candidate => candidate.name === "Trim");
        const body = member?.bodySelectors.find(candidate =>
          candidate.token === member.metadataToken);
        if (!type || !body) {
          throw new Error(
            "The real package does not expose StringSegment.Trim.");
        }
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
      const evidencePath =
        testInfo.outputPath("public-source-comparisons.json");
      await writeFile(evidencePath, JSON.stringify(evidence, null, 2));
      await testInfo.attach("public-source-comparisons.json", {
        path: evidencePath,
        contentType: "application/json",
      });
      for (const result of [evidence.compared, evidence.same]) {
        expect(result.kind).toBe("Succeeded");
        expect(result.value).not.toBeNull();
        expect(result.value!.before.version).toBe("10.0.0");
        expect(result.value!.before.memberIdentity).not.toBeNull();
        expect(result.value!.after.memberIdentity).not.toBeNull();
        for (const endpoint of [
          result.value!.before, result.value!.after,
        ]) {
          expect(endpoint.packageId.toLowerCase())
            .toBe("microsoft.extensions.primitives");
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
      if (evidence.same.value!.status === "Compared") {
        expect(evidence.same.value!.isExact).toBe(true);
      } else {
        expect(evidence.same.value!.isExact).toBe(false);
      }
    });

  test("cataloged Source-only, exact, moved, and unavailable declarations preserve evidence",
    async ({ page }, testInfo) => {
      test.skip(!beforePackage || !afterPackage || !beforeSource || !afterSource,
        "Set the four catalog-resolved Source comparison fixture assets.");
      let omitAfterSource = false;
      await page.context().route(
        "**/inspectweb.sourcecomparisonfixture*.nupkg",
        async route => {
          const version =
            route.request().url().includes(".2.0.0.nupkg") ? 2 : 1;
          await route.fulfill({
            path: version === 1 ? beforePackage! : afterPackage!,
            contentType: "application/octet-stream",
            headers: { "access-control-allow-origin": "*" },
          });
        });
      await page.context().route(
        "https://raw.githubusercontent.com/dotnet-inspect-fixtures/source-comparison/**",
        async route => {
          const after =
            route.request().url().includes("/source-comparison/v2/");
          if (after && omitAfterSource) {
            await route.fulfill({
              status: 404,
              body: "Fixture source unavailable.",
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
      await openPublishedSite(page);

      async function compareMember(targetPage: Page, name: string) {
        return targetPage.evaluate(async memberName => {
          const packages = await import("/inspect-web-package.js");
          const source = await import("/inspect-web-source.js");
          const surface = await packages.queryPackage(
            "InspectWeb.SourceComparisonFixture", "1.0.0", "net11.0",
          );
          const type = surface.types.find(candidate =>
            candidate.definitionId
              === "SourceDiffFixture.Counter");
          const member = type?.api.find(candidate =>
            candidate.name === memberName);
          const body = member?.bodySelectors.find(candidate =>
            candidate.token === member.metadataToken);
          if (!type || !body) {
            throw new Error(
              `The fixture does not expose Counter.${memberName}.`);
          }
          const request = {
            packageId: surface.package,
            beforeVersion: surface.version,
            afterVersion: "2.0.0",
            framework: surface.activeFramework,
            assembly: type.assemblyId,
            typeIdentity: type.definitionId,
            memberName: body.memberName,
            selectorKey: body.selectorKey,
            metadataToken: body.token,
          };
          return source.queryMemberSourceComparison(
            `source-comparison-fixture-${memberName}`,
            JSON.stringify(request),
          );
        }, name);
      }

      const changed = await compareMember(page, "Value");
      const exact = await compareMember(page, "Unchanged");
      const moved = await compareMember(page, "MovedBlock");
      const movedAndEdited = await compareMember(page, "MovedBlockAndEdit");
      omitAfterSource = true;
      const unavailablePage = await page.context().newPage();
      await openPublishedSite(unavailablePage);
      const unavailable = await compareMember(unavailablePage, "Value");
      await unavailablePage.close();
      const evidence = {
        changed, exact, moved, movedAndEdited, unavailable,
      };
      const evidencePath =
        testInfo.outputPath("fixture-source-comparisons.json");
      await writeFile(evidencePath, JSON.stringify(evidence, null, 2));
      await testInfo.attach("fixture-source-comparisons.json", {
        path: evidencePath,
        contentType: "application/json",
      });

      expect(changed.kind).toBe("Succeeded");
      expect(changed.value?.status).toBe("Compared");
      expect(changed.value?.isExact).toBe(false);
      expect(changed.value?.lines.some(line =>
        line.kind === "Removed" && line.beforeText?.includes("1 + 2")))
        .toBe(true);
      expect(changed.value?.lines.some(line =>
        line.kind === "Added" && line.afterText?.includes("=> 3")))
        .toBe(true);

      expect(exact.kind).toBe("Succeeded");
      expect(exact.value?.status).toBe("Compared");
      expect(exact.value?.isExact).toBe(true);

      for (const result of [moved, movedAndEdited]) {
        expect(result.kind).toBe("Succeeded");
        expect(result.value?.status).toBe("Compared");
        const moves = result.value?.lines.filter(
          line => line.difference === "Moved") ?? [];
        expect(moves).toHaveLength(2);
        expect(moves.map(line => [
          line.beforeLine, line.afterLine,
        ])).toEqual([[3, 5], [4, 6]]);
      }
      expect(movedAndEdited.value?.lines.some(
        line => line.kind === "Removed")).toBe(true);
      expect(movedAndEdited.value?.lines.some(
        line => line.kind === "Added")).toBe(true);

      expect(unavailable.kind).toBe("Succeeded");
      expect(unavailable.value?.status).toBe("Unavailable");
      expect(unavailable.value?.before.state).toBe("Available");
      expect(unavailable.value?.before.text).toContain("1 + 2");
      expect(unavailable.value?.after.state).not.toBe("Available");
      expect(unavailable.value?.isExact).toBe(false);
      expect(unavailable.value?.lines).toHaveLength(0);
    });
});
