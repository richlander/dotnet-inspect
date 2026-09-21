import { expect, test, type Page } from "@playwright/test";
import { writeFile } from "node:fs/promises";
import {
  chooseSubject,
  selectFirstExactLibrary,
} from "./library-subject-actions.ts";

const site = process.env.INSPECT_WEB_SOURCE_DIFF_URL;
const fixtureOnly = process.env.INSPECT_WEB_SOURCE_DIFF_FIXTURE_ONLY === "1";
const beforePackage = process.env.INSPECT_WEB_SOURCE_DIFF_BEFORE_PACKAGE;
const afterPackage = process.env.INSPECT_WEB_SOURCE_DIFF_AFTER_PACKAGE;
const beforeSource = process.env.INSPECT_WEB_SOURCE_DIFF_BEFORE_SOURCE;
const afterSource = process.env.INSPECT_WEB_SOURCE_DIFF_AFTER_SOURCE;

async function openPublishedSite(page: Page): Promise<void> {
  await page.route(site!, route => route.fulfill({
    contentType: "text/html",
    body: "<!doctype html><title>Source facade test</title>",
  }), { times: 1 });
  await page.goto(site!, { waitUntil: "domcontentloaded" });
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

  test("System.Text.Json Type Source bounds a stalled PDB response",
    async ({ page }, testInfo) => {
      test.skip(fixtureOnly, "The CI gate uses deterministic acquired artifacts.");
      const upstreamStallMilliseconds = 60_000;
      let releasePdbResponse!: () => void;
      const pdbResponseGate = new Promise<void>(resolve => {
        releasePdbResponse = resolve;
      });
      let pdbResponseReleased = false;
      let msdlRequests = 0;
      const releaseTimer = setTimeout(() => {
        pdbResponseReleased = true;
        releasePdbResponse();
      }, upstreamStallMilliseconds);
      await page.route("**/api/msdl/**", async route => {
        msdlRequests++;
        await pdbResponseGate;
        await route.fulfill({
          status: 504,
          body: "Deliberately stalled PDB response.",
          headers: { "access-control-allow-origin": "*" },
        }).catch(() => undefined);
      });

      try {
        await openPublishedSite(page);
        const evidence = await page.evaluate(async () => {
          const packages = await import("/inspect-web-package.js");
          const source = await import("/inspect-web-source.js");
          const loadResult = await packages.queryPackage(
            "System.Text.Json", "11.0.0-preview.7.26381.103", "netstandard2.0",
          );
          const surface = loadResult.surface;
          if (surface === null) {
            throw new Error(
              loadResult.versionSettlement.content.failure?.reason
                ?? "System.Text.Json settlement did not produce a surface.",
            );
          }
          const type = surface.types.find(candidate =>
            candidate.definitionId === "System.HexConverter+Casing");
          if (!type) {
            throw new Error(
              "System.Text.Json does not expose System.HexConverter+Casing.",
            );
          }
          const started = performance.now();
          const result = await source.queryTypeSource(
            "type-source-system-text-json-hedge",
            surface.package,
            surface.version,
            surface.activeFramework,
            type.assemblyId,
            type.definitionId,
            "[]",
          );
          return {
            assembly: type.assemblyId,
            elapsedMilliseconds: performance.now() - started,
            framework: surface.activeFramework,
            result,
          };
        });
        const timingPath = testInfo.outputPath(
          "system-text-json-type-source-hedge.json",
        );
        await writeFile(timingPath, JSON.stringify({
          ...evidence,
          msdlRequests,
          pdbResponseReleased,
          upstreamStallMilliseconds,
        }, null, 2));
        await testInfo.attach("system-text-json-type-source-hedge.json", {
          path: timingPath,
          contentType: "application/json",
        });

        expect(msdlRequests).toBe(1);
        expect(pdbResponseReleased).toBe(false);
        expect(evidence.elapsedMilliseconds).toBeLessThan(
          upstreamStallMilliseconds,
        );
        expect(evidence.result.kind).toBe("Succeeded");
        expect(evidence.result.value?.provider).toBe("decompiled");
        expect(evidence.result.value?.text)
          .toContain("enum HexConverter.Casing");
        expect(evidence.result.value?.pdbSourceLimitation)
          .toContain("Portable PDB");
        expect(evidence.result.value?.url).toBeNull();
      } finally {
        clearTimeout(releaseTimer);
        pdbResponseReleased = true;
        releasePdbResponse();
      }
    });

  test("public package versions retain independent Source outcomes",
    async ({ page }, testInfo) => {
      test.skip(fixtureOnly, "The CI gate uses deterministic acquired artifacts.");
      await openPublishedSite(page);
      const evidence = await page.evaluate(async () => {
        const packages = await import("/inspect-web-package.js");
        const source = await import("/inspect-web-source.js");
        const loadResult = await packages.queryPackage(
          "Microsoft.Extensions.Primitives", "10.0.0", "net10.0",
        );
        const surface = loadResult.surface;
        if (surface === null) {
          throw new Error(
            loadResult.versionSettlement.content.failure?.reason
              ?? "Package version settlement did not produce a surface.",
          );
        }
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
      let sourceFetchCount = 0;
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
          sourceFetchCount++;
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

      async function memberRequest(
        targetPage: Page, name: string, selectedVersion = "1.0.0", typeName = "Counter",
      ) {
        return targetPage.evaluate(async ({ memberName, version, selectedType }) => {
          const packages = await import("/inspect-web-package.js");
          const loadResult = await packages.queryPackage(
            "InspectWeb.SourceComparisonFixture", version, "net11.0",
          );
          const surface = loadResult.surface;
          if (surface === null) {
            throw new Error(
              loadResult.versionSettlement.content.failure?.reason
                ?? "Package version settlement did not produce a surface.",
            );
          }
          const type = surface.types.find(candidate =>
            candidate.definitionId
              === `SourceDiffFixture.${selectedType}`);
          const member = type?.api.find(candidate =>
            candidate.name === memberName);
          const body = member?.bodySelectors.find(candidate =>
            candidate.token === member.metadataToken)
            ?? member?.bodySelectors.find(candidate =>
              candidate.memberName === `get_${memberName}`);
          if (!type || !body) {
            throw new Error(
              `The fixture does not expose ${selectedType}.${memberName}.`);
          }
          return {
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
        }, { memberName: name, version: selectedVersion, selectedType: typeName });
      }

      async function compareMember(targetPage: Page, name: string) {
        const selected = await memberRequest(targetPage, name);
        return targetPage.evaluate(async request => {
          const source = await import("/inspect-web-source.js");
          return source.queryMemberSourceComparison(
            `source-comparison-fixture-${request.memberName}`, JSON.stringify(request));
        }, selected);
      }

      async function memberSource(
        targetPage: Page, version: string, typeName = "Counter", name = "Value",
      ) {
        const selected = await memberRequest(targetPage, name, version, typeName);
        return targetPage.evaluate(async request => {
          const source = await import("/inspect-web-source.js");
          return source.queryMemberSource(
            request.packageId, request.beforeVersion, request.framework,
            request.assembly, request.typeIdentity, request.memberName,
            request.selectorKey, request.metadataToken, "[]");
        }, selected);
      }

      async function typeSource(targetPage: Page, version: string) {
        const selected = await memberRequest(targetPage, "Value", version);
        return targetPage.evaluate(async request => {
          const source = await import("/inspect-web-source.js");
          return source.queryTypeSource(
            `type-source-fixture-${request.beforeVersion}`,
            request.packageId, request.beforeVersion, request.framework,
            request.assembly, request.typeIdentity, "[]");
        }, selected);
      }

      const authoredMember = await memberSource(page, "1.0.0");
      const authoredType = await typeSource(page, "1.0.0");
      const initializedGetter = await memberSource(page, "1.0.0", "InitializedFieldGetter", "Count");
      const declinedGetter = await memberSource(page, "1.0.0", "CalculatedFieldGetter", "Count");
      expect(initializedGetter.source.provider).toBe("decompiled");
      expect(initializedGetter.source.text).toContain("readonly struct InitializedFieldGetter(int value)");
      expect(initializedGetter.source.text).toContain("get => field + 1;");
      expect(initializedGetter.source.text).toContain("} = value;");
      expect(declinedGetter.source.provider).toBe("decompiled");
      expect(declinedGetter.source.text).toContain("Count => field + 1;");
      expect(declinedGetter.source.text).not.toContain("struct ");
      expect(declinedGetter.source.text).not.toContain("} = ");
      const body = authoredMember.parts.find(part => part.kind === "Body");
      if (!body) {
        throw new Error("Authored fixture member did not publish its body part.");
      }
      const expectedBody = body.spans.map(span =>
        span.leadingIndentation
        + authoredMember.source.text.slice(span.start, span.end)).join("\n");
      const member = authoredMember.parts.find(
        part => part.kind === "Member");
      if (!member) {
        throw new Error("Authored fixture member did not publish its complete-member part.");
      }
      const expectedMember = member.spans.map(span =>
        span.leadingIndentation
        + authoredMember.source.text.slice(span.start, span.end)).join("\n");
      const documentation = authoredMember.parts.find(
        part => part.kind === "XmlDocumentation");
      if (!documentation) {
        throw new Error("Authored fixture member did not publish its XML documentation.");
      }
      const expectedDocumentation = documentation.spans.map(span =>
        span.leadingIndentation
        + authoredMember.source.text.slice(span.start, span.end)).join("\n");

      const applicationPage = await page.context().newPage();
      await applicationPage.addInitScript(() => {
        const state = window as typeof window & {
          __copiedMemberSource?: string;
        };
        Object.defineProperty(navigator, "clipboard", {
          configurable: true,
          value: {
            writeText: async (text: string) => {
              state.__copiedMemberSource = text;
            },
          },
        });
      });
      const applicationUrl = new URL(site!);
      applicationUrl.search = new URLSearchParams({
        package: "InspectWeb.SourceComparisonFixture",
        version: "1.0.0",
        framework: "net11.0",
      }).toString();
      applicationUrl.hash = "pkg";
      await applicationPage.goto(applicationUrl.href);
      const librarySubject =
        applicationPage.locator('[data-subject-tab][data-scope="library"]');
      await expect(librarySubject.or(applicationPage.locator(".load-error")))
        .toBeVisible({ timeout: 180_000 });
      if (await applicationPage.locator(".load-error").isVisible()) {
        throw new Error(
          await applicationPage.locator(".load-error").textContent()
            ?? "Published application failed to load the fixture package.");
      }
      await selectFirstExactLibrary(applicationPage);
      await chooseSubject(applicationPage, "type");
      await applicationPage.locator("#type-list [data-type]")
        .filter({ hasText: /\bCounter\b/ })
        .first()
        .click();
      await applicationPage.locator("[data-member]")
        .filter({ hasText: /\bValue\b/ })
        .click();
      await applicationPage.locator(
        '[data-member-section="source"]:visible',
      ).click();

      const selector =
        applicationPage.getByLabel("Select member source part");
      await expect(selector).toHaveValue("Member", { timeout: 60_000 });
      const sourceCode = applicationPage.locator(".source-result code");
      await expect.poll(() => sourceCode.textContent())
        .toBe(expectedMember);
      const settledSourceFetchCount = sourceFetchCount;
      await selector.selectOption("XmlDocumentation");
      await expect.poll(() => sourceCode.textContent())
        .toBe(expectedDocumentation);
      expect(sourceFetchCount).toBe(settledSourceFetchCount);
      await applicationPage.locator("#copy-source").click();
      await expect.poll(() => applicationPage.evaluate(() =>
        (window as typeof window & {
          __copiedMemberSource?: string;
        }).__copiedMemberSource)).toBe(expectedDocumentation);
      expect(sourceFetchCount).toBe(settledSourceFetchCount);
      await selector.selectOption("Body");
      await expect.poll(() => sourceCode.textContent()).toBe(expectedBody);
      expect(sourceFetchCount).toBe(settledSourceFetchCount);
      await applicationPage.locator("#copy-source").click();
      await expect.poll(() => applicationPage.evaluate(() =>
        (window as typeof window & {
          __copiedMemberSource?: string;
        }).__copiedMemberSource)).toBe(expectedBody);
      expect(sourceFetchCount).toBe(settledSourceFetchCount);
      await applicationPage.close();

      const changed = await compareMember(page, "Value");
      const exact = await compareMember(page, "Unchanged");
      const moved = await compareMember(page, "MovedBlock");
      const movedAndEdited = await compareMember(page, "MovedBlockAndEdit");
      omitAfterSource = true;
      const unavailablePage = await page.context().newPage();
      await openPublishedSite(unavailablePage);
      const unavailable = await compareMember(unavailablePage, "Value");
      const fallbackMember = await memberSource(unavailablePage, "2.0.0");
      const fallbackType = await typeSource(unavailablePage, "2.0.0");
      await unavailablePage.close();
      const evidence = {
        authoredMember, fallbackMember, authoredType, fallbackType,
        initializedGetter, declinedGetter,
        changed, exact, moved, movedAndEdited, unavailable,
      };
      const evidencePath =
        testInfo.outputPath("fixture-source-comparisons.json");
      await writeFile(evidencePath, JSON.stringify(evidence, null, 2));
      await testInfo.attach("fixture-source-comparisons.json", {
        path: evidencePath,
        contentType: "application/json",
      });

      expect(authoredMember.source.provider).toBe("pdb");
      expect(authoredMember.source.text).toContain("1 + 2");
      expect(authoredMember.source.pdbSourceLimitation).toBeNull();
      expect(authoredMember.source.url).toBeTruthy();
      expect(authoredMember.parts.map(part => part.kind)).toContain("Member");
      expect(authoredMember.parts.map(part => part.kind)).toContain("Body");
      expect(fallbackMember.source.provider).toBe("decompiled");
      expect(fallbackMember.source.text).toContain("Value");
      expect(fallbackMember.source.pdbSourceLimitation).toBeTruthy();
      expect(fallbackMember.source.url).toBeNull();
      expect(fallbackMember.parts).toEqual([]);

      expect(authoredType.kind).toBe("Succeeded");
      expect(authoredType.value?.provider).toBe("pdb");
      expect(authoredType.value?.text).toContain("class Counter");
      expect(authoredType.value?.text).toContain("1 + 2");
      expect(authoredType.value?.pdbSourceLimitation).toBeNull();
      expect(authoredType.value?.url).toBeTruthy();
      expect(fallbackType.kind).toBe("Succeeded");
      expect(fallbackType.value?.provider).toBe("decompiled");
      expect(fallbackType.value?.text).toContain("class Counter");
      expect(fallbackType.value?.pdbSourceLimitation).toBeTruthy();
      expect(fallbackType.value?.url).toBeNull();

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
