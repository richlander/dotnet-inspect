import { expect, test, type Page } from "@playwright/test";
import { writeFile } from "node:fs/promises";
import type { TypeSourceView } from "../src/source-inspection.ts";
import {
  sourceDiffPayloadDecoder,
  type BrowserSourceComparisonResult,
} from "../src/source-diff-transport.ts";
import type {
  PublishedSourceComparisonBridge,
} from "../src/published-source-comparison-bridge.ts";
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

type SourceComparisonGateWindow = Window & {
  __inspectWebSourceComparison?: PublishedSourceComparisonBridge;
  __sourceComparisonPending?: Promise<BrowserSourceComparisonResult>;
};

function decodeSourceComparison(
  value: unknown,
): BrowserSourceComparisonResult {
  const result = sourceDiffPayloadDecoder.decode(value);
  if (result.kind === "decoded") return result.value;
  throw new Error(`Published Source comparison was rejected: ${result.message}`);
}

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
            "source",
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
        expect(evidence.result.value?.kind).toBe("source");
        if (evidence.result.value?.kind !== "source")
          throw new Error("Expected a decompiled type source code view.");
        expect(evidence.result.value.value.provider).toBe("decompiled");
        expect(evidence.result.value.value.text)
          .toContain("enum HexConverter.Casing");
        expect(evidence.result.value.value.pdbSourceLimitation)
          .toContain("Portable PDB");
        expect(evidence.result.value.value.url).toBeNull();
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
      const rawEvidence = await page.evaluate(async () => {
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
        if (!type || !member || !body) {
          throw new Error(
            "The real package does not expose StringSegment.Trim.");
        }
        const endpoint = {
          typeIdentity: type.definitionId,
          stableSelector: member.stableSelector,
          canonicalSignature: member.canonicalSignature,
          fingerprint: member.anchorDigest,
          typeFullName: member.anchorTypeFullName,
          memberName: member.name,
        };
        const request = {
          packageId: surface.package,
          beforeVersion: surface.version,
          afterVersion: "10.0.1",
          framework: surface.activeFramework,
          assembly: type.assemblyId,
          before: endpoint,
          after: endpoint,
        };
        const compared = await source.queryMemberSourceComparison(
          "source-comparison-public-pair", request,
        );
        const same = await source.queryMemberSourceComparison(
          "source-comparison-public-same",
          { ...request, afterVersion: request.beforeVersion },
        );
        return { request, compared, same };
      });
      const evidence = {
        request: rawEvidence.request,
        compared: decodeSourceComparison(rawEvidence.compared),
        same: decodeSourceComparison(rawEvidence.same),
      };
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
          if (!type || !member || !body) {
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
            stableSelector: member.stableSelector,
            canonicalSignature: member.canonicalSignature,
            fingerprint: member.anchorDigest,
            typeFullName: member.anchorTypeFullName,
            anchorMemberName: member.name,
          };
        }, { memberName: name, version: selectedVersion, selectedType: typeName });
      }

      async function compareMember(targetPage: Page, name: string) {
        const selected = await memberRequest(targetPage, name);
        const result = await targetPage.evaluate(async request => {
          const source = await import("/inspect-web-source.js");
          const endpoint = {
            typeIdentity: request.typeIdentity,
            stableSelector: request.stableSelector,
            canonicalSignature: request.canonicalSignature,
            fingerprint: request.fingerprint,
            typeFullName: request.typeFullName,
            memberName: request.anchorMemberName,
          };
          return source.queryMemberSourceComparison(
            `source-comparison-fixture-${request.memberName}`,
            {
              packageId: request.packageId,
              beforeVersion: request.beforeVersion,
              afterVersion: request.afterVersion,
              framework: request.framework,
              assembly: request.assembly,
              before: endpoint,
              after: endpoint,
            },
          );
        }, selected);
        return decodeSourceComparison(result);
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

      async function typeSource(
        targetPage: Page,
        version: string,
        view: TypeSourceView = "source",
      ) {
        const selected = await memberRequest(targetPage, "Value", version);
        return targetPage.evaluate(async request => {
          const source = await import("/inspect-web-source.js");
          return source.queryTypeSource(
            `type-source-fixture-${request.beforeVersion}-${request.view}`,
            request.packageId, request.beforeVersion, request.framework,
            request.assembly, request.typeIdentity, "[]", request.view);
        }, { ...selected, view });
      }

      const authoredMember = await memberSource(page, "1.0.0");
      const authoredType = await typeSource(page, "1.0.0");
      const decompiledType = await typeSource(
        page,
        "1.0.0",
        "decompiler-source");
      const apiType = await typeSource(page, "1.0.0", "api-declarations");
      const allType = await typeSource(page, "1.0.0", "all-declarations");
      if (apiType.value?.kind !== "apiDeclarations"
        || allType.value?.kind !== "apiDeclarations") {
        throw new Error("Expected completed declaration views from the source facade.");
      }
      const apiDeclarations = apiType.value.inspection;
      const allDeclarations = allType.value.inspection;
      expect(apiDeclarations.content.outcome).toBe("Available");
      expect(apiDeclarations.content.scope).toBe("ApiVisible");
      expect(apiDeclarations.content.text).toContain("public int Value();");
      expect(apiDeclarations.content.text).not.toContain("Hidden");
      expect(apiDeclarations.content.text).not.toContain("1 + 2");
      expect(allDeclarations.content.outcome).toBe("Available");
      expect(allDeclarations.content.scope).toBe("All");
      expect(allDeclarations.content.text).toContain("private int Hidden();");
      expect(allDeclarations.content.text).not.toContain("1 + 2");
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
      await applicationPage.locator("#explore-source").click();
      await expect(applicationPage.locator("#settings-backdrop")).toBeVisible();
      await expect(
        applicationPage.locator("#settings-decompiler-title"),
      ).toBeFocused();
      await applicationPage.locator("#settings-close").click();
      await expect(applicationPage.locator("#explore-source")).toBeFocused();
      await chooseSubject(applicationPage, "type");
      const typeApiUrl = applicationPage.url();
      await applicationPage.locator('[data-lens="source"]:visible').click();
      const typeView = applicationPage.getByLabel("Select type code view");
      await expect(typeView).toHaveValue("source");
      await expect(applicationPage).not.toHaveURL(typeApiUrl);
      const typeSourceUrl = applicationPage.url();
      await applicationPage.locator("#explore-source").click();
      await expect(applicationPage).toHaveURL(/\/type-explorer\?/u);
      const typeExplorerHeading = applicationPage.getByRole("heading", {
        level: 1,
        name: /Type Explorer: .*Counter/u,
      });
      await expect(typeExplorerHeading).toBeFocused();
      await expect(
        applicationPage.getByRole("region", { name: "Whole-Type C#" }),
      ).toContainText("Counter", { timeout: 60_000 });
      await applicationPage.reload();
      await expect(typeExplorerHeading).toBeFocused({ timeout: 60_000 });
      await expect(
        applicationPage.getByRole("region", { name: "Whole-Type C#" }),
      ).toContainText("Counter", { timeout: 60_000 });
      await applicationPage.setViewportSize({ width: 1120, height: 520 });
      const typeExplorerLayout = await applicationPage.evaluate(() => {
        const route = document.querySelector<HTMLElement>(
          ".type-explorer-route");
        const source = document.querySelector<HTMLElement>(
          ".type-explorer-source pre");
        if (route === null || source === null)
          throw new Error("Type Explorer layout was not rendered.");
        return {
          routeClientHeight: route.clientHeight,
          routeScrollHeight: route.scrollHeight,
          sourceClientHeight: source.clientHeight,
          sourceScrollHeight: source.scrollHeight,
        };
      });
      expect(typeExplorerLayout.routeScrollHeight)
        .toBe(typeExplorerLayout.routeClientHeight);
      expect(typeExplorerLayout.sourceScrollHeight)
        .toBeGreaterThan(typeExplorerLayout.sourceClientHeight);
      const selectedBody = applicationPage.getByRole("radio", {
        name: "Selected body",
      });
      const buildValueOutline = applicationPage
        .locator(".type-explorer-outline [data-type-explorer-declaration]")
        .filter({ hasText: /\bBuildValue\b/u })
        .first();
      await buildValueOutline.click();
      await expect(selectedBody).toBeDisabled();
      const selectedSource = applicationPage.locator(
        ".type-explorer-source [aria-current=\"true\"]");
      const selectedSourceOffset = async () => {
        await expect(selectedSource).toBeVisible();
        return await selectedSource.evaluate(selected => {
          const source = selected
            .closest(".type-explorer-source")
            ?.querySelector<HTMLElement>("pre");
          if (source === null || source === undefined)
            throw new Error("Type Explorer source was not rendered.");
          return selected.getBoundingClientRect().top
            - source.getBoundingClientRect().top;
        });
      };
      const buildValueOffset = await selectedSourceOffset();
      const staticMembers = applicationPage.getByRole("radio", {
        name: "Static",
      });
      const allMembers = applicationPage.getByRole("radio", {
        name: "All",
      });
      await staticMembers.check();
      await expect(
        applicationPage.getByRole("region", { name: "Whole-Type C#" }),
      ).toContainText("Counter", { timeout: 60_000 });
      await expect(buildValueOutline).toHaveAttribute("aria-current", "true");
      await expect.poll(async () =>
        Math.abs(await selectedSourceOffset() - buildValueOffset))
        .toBeLessThanOrEqual(2);
      await allMembers.check();
      const instanceMembers = applicationPage.getByRole("radio", {
        name: "Instance",
      });
      await instanceMembers.check();
      await expect(
        applicationPage.locator(".type-explorer-failure"),
      ).toContainText("SelectedMemberHidden");
      await expect(selectedBody).toBeDisabled();
      await allMembers.check();
      await expect(
        applicationPage.getByRole("region", { name: "Whole-Type C#" }),
      ).toContainText("Counter", { timeout: 60_000 });
      await expect(buildValueOutline).toHaveAttribute("aria-current", "true");
      await expect(selectedBody).toBeDisabled();
      const valueOutline = applicationPage
        .locator(".type-explorer-outline [data-type-explorer-declaration]")
        .filter({ hasText: /\bValue\b/u })
        .first();
      await valueOutline.click();
      await expect(valueOutline).toHaveAttribute("aria-current", "true");
      await expect(valueOutline).toBeFocused();
      const inspectBody = applicationPage.getByRole("button", {
        name: "Inspect method body",
      });
      await expect(inspectBody).toBeVisible();
      await inspectBody.scrollIntoViewIfNeeded();
      const typeExplorerBeforeInspect = await applicationPage.evaluate(() => {
        const source = document.querySelector<HTMLElement>(
          ".type-explorer-source pre");
        const outline = document.querySelector<HTMLElement>(
          ".type-explorer-outline");
        if (source === null || outline === null)
          throw new Error("Type Explorer scroll surfaces were not rendered.");
        return {
          url: location.href,
          sourceTop: source.scrollTop,
          sourceLeft: source.scrollLeft,
          outlineTop: outline.scrollTop,
        };
      });
      await inspectBody.click();
      await expect(
        applicationPage.locator("#annotated-source-backdrop"),
      ).toBeVisible({ timeout: 60_000 });
      await expect(
        applicationPage.locator("#annotated-modal-title"),
      ).toBeFocused();
      await expect(
        applicationPage.locator(".annotated-source-signature"),
      ).toContainText("public int Value()");
      await expect(
        applicationPage.locator(".annotated-source-signature"),
      ).not.toContainText("M:");
      await applicationPage
        .locator("#annotated-source-backdrop [data-annotated-action='copy']")
        .click();
      await expect.poll(() => applicationPage.evaluate(() =>
        (window as typeof window & {
          __copiedMemberSource?: string;
        }).__copiedMemberSource,
      )).toContain("public int Value()");
      await expect(applicationPage).toHaveURL(
        typeExplorerBeforeInspect.url);
      await applicationPage.locator("#annotated-modal-close").click();
      await expect(inspectBody).toBeFocused();
      await expect(valueOutline).toHaveAttribute("aria-current", "true");
      await expect.poll(() => applicationPage.evaluate(() => {
        const source = document.querySelector<HTMLElement>(
          ".type-explorer-source pre");
        const outline = document.querySelector<HTMLElement>(
          ".type-explorer-outline");
        if (source === null || outline === null) return null;
        return {
          url: location.href,
          sourceTop: source.scrollTop,
          sourceLeft: source.scrollLeft,
          outlineTop: outline.scrollTop,
        };
      })).toEqual(typeExplorerBeforeInspect);
      await staticMembers.check();
      await expect(
        applicationPage.locator(".type-explorer-failure"),
      ).toContainText("SelectedMemberHidden");
      await allMembers.check();
      await expect(
        applicationPage.getByRole("region", { name: "Whole-Type C#" }),
      ).toContainText("Counter", { timeout: 60_000 });
      await expect(valueOutline).toHaveAttribute("aria-current", "true");
      const sourceDeclaration = applicationPage
        .locator(".type-explorer-source-declaration")
        .filter({ hasText: /\bCounter\b/u })
        .first();
      await sourceDeclaration.click();
      await expect(sourceDeclaration).toBeFocused();
      await expect(sourceDeclaration).toHaveAttribute("aria-current", "true");
      await applicationPage.setViewportSize({ width: 600, height: 520 });
      await expect(
        applicationPage.locator(".type-explorer-outline"),
      ).toBeHidden();
      await expect(sourceDeclaration).toBeVisible();
      await expect(sourceDeclaration).toHaveAttribute("aria-current", "true");
      await applicationPage.setViewportSize({ width: 320, height: 480 });
      await expect(
        applicationPage.locator("#type-explorer-outline-toggle"),
      ).toBeVisible();
      const shortTypeExplorerLayout = await applicationPage.evaluate(() => {
        const route = document.querySelector<HTMLElement>(
          ".type-explorer-route");
        const documentSurface = document.querySelector<HTMLElement>(
          ".type-explorer-document");
        const source = document.querySelector<HTMLElement>(
          ".type-explorer-source");
        if (route === null || documentSurface === null || source === null)
          throw new Error("Short Type Explorer layout was not rendered.");
        return {
          routeClientHeight: route.clientHeight,
          routeScrollHeight: route.scrollHeight,
          documentHeight: documentSurface.getBoundingClientRect().height,
          sourceHeight: source.getBoundingClientRect().height,
        };
      });
      expect(shortTypeExplorerLayout.routeScrollHeight)
        .toBeGreaterThan(shortTypeExplorerLayout.routeClientHeight);
      expect(shortTypeExplorerLayout.documentHeight).toBeGreaterThan(0);
      expect(shortTypeExplorerLayout.sourceHeight).toBeGreaterThan(0);
      const skeleton = applicationPage.getByLabel("Skeleton");
      await skeleton.check();
      await expect(skeleton).toBeFocused();
      await expect(
        applicationPage.getByRole("region", { name: "Whole-Type C#" }),
      ).toContainText("Counter", { timeout: 60_000 });
      await applicationPage.getByRole("button", {
        name: "Back to Type Source",
      }).click();
      await expect(applicationPage).toHaveURL(typeSourceUrl);
      await expect(applicationPage.locator("#explore-source")).toBeFocused();
      await typeView.selectOption("decompiler-source");
      await expect.poll(() => sourceCode.textContent())
        .toBe(decompiledType.value?.kind === "source"
          ? decompiledType.value.value.text
          : null);
      await expect(applicationPage.locator("#explore-source")).toHaveCount(1);
      await typeView.selectOption("api-declarations");
      await expect.poll(() => sourceCode.textContent())
        .toBe(apiDeclarations.content.text);
      await expect(applicationPage.locator("#explore-source")).toHaveCount(0);
      await typeView.selectOption("all-declarations");
      await expect.poll(() => sourceCode.textContent())
        .toBe(allDeclarations.content.text);
      await applicationPage.locator("#copy-type-source").click();
      await expect.poll(() => applicationPage.evaluate(() =>
        (window as typeof window & {
          __copiedMemberSource?: string;
        }).__copiedMemberSource)).toBe(allDeclarations.content.text);
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
        authoredMember, fallbackMember, authoredType, decompiledType,
        fallbackType, apiType, allType,
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
      expect(authoredType.value?.kind).toBe("source");
      if (authoredType.value?.kind !== "source")
        throw new Error("Expected an authored type source code view.");
      expect(authoredType.value.value.provider).toBe("pdb");
      expect(authoredType.value.value.text).toContain("class Counter");
      expect(authoredType.value.value.text)
        .toContain("public int Value() => 1 + 2;");
      expect(authoredType.value.value.pdbSourceLimitation).toBeNull();
      expect(authoredType.value.value.url).toBeTruthy();
      expect(decompiledType.kind).toBe("Succeeded");
      expect(decompiledType.value?.kind).toBe("source");
      if (decompiledType.value?.kind !== "source")
        throw new Error("Expected an explicit decompiler type source code view.");
      expect(decompiledType.value.value.provider).toBe("decompiled");
      expect(decompiledType.value.value.text).toContain("class Counter");
      expect(decompiledType.value.value.text)
        .toContain("public int Value() => 3;");
      expect(decompiledType.value.value.pdbSourceLimitation).toBeNull();
      expect(decompiledType.value.value.url).toBeNull();
      expect(fallbackType.kind).toBe("Succeeded");
      expect(fallbackType.value?.kind).toBe("source");
      if (fallbackType.value?.kind !== "source")
        throw new Error("Expected a decompiled type source code view.");
      expect(fallbackType.value.value.provider).toBe("decompiled");
      expect(fallbackType.value.value.text).toContain("class Counter");
      expect(fallbackType.value.value.pdbSourceLimitation).toBeTruthy();
      expect(fallbackType.value.value.url).toBeNull();

      expect(changed.kind).toBe("Succeeded");
      expect(changed.value?.status).toBe("Compared");
      expect(changed.value?.isExact).toBe(false);
      expect(changed.value?.diff?.before.lines.some(line =>
        line.includes("1 + 2"))).toBe(true);
      expect(changed.value?.diff?.after.lines.some(line =>
        line.includes("=> 3"))).toBe(true);
      expect(changed.value?.diff?.relations.some(relation =>
        relation.kind === "Removal")).toBe(true);
      expect(changed.value?.diff?.relations.some(relation =>
        relation.kind === "Addition")).toBe(true);
      expect(changed.value?.diff?.changes).toHaveLength(1);

      expect(exact.kind).toBe("Succeeded");
      expect(exact.value?.status).toBe("Compared");
      expect(exact.value?.isExact).toBe(true);
      expect(exact.value?.diff?.changes).toHaveLength(0);
      expect(exact.value?.diff?.statistics).toEqual({
        added: 0,
        removed: 0,
        changedBefore: 0,
        changedAfter: 0,
        movedBefore: 0,
        movedAfter: 0,
      });

      for (const result of [moved, movedAndEdited]) {
        expect(result.kind).toBe("Succeeded");
        expect(result.value?.status).toBe("Compared");
        const moves = result.value?.diff?.relations.filter(
          relation => relation.placement === "Moved") ?? [];
        expect(moves).not.toHaveLength(0);
        expect(moves.some(relation =>
          relation.beforeCoordinates[0] !== relation.afterCoordinates[0]))
          .toBe(true);
      }
      expect(movedAndEdited.value?.diff?.after.lines.some(
        line => line.includes("+ 1"))).toBe(true);
      expect((movedAndEdited.value?.diff?.statistics.added ?? 0)
        + (movedAndEdited.value?.diff?.statistics.changedAfter ?? 0))
        .toBeGreaterThan(0);

      expect(unavailable.kind).toBe("Succeeded");
      expect(unavailable.value?.status).toBe("Unavailable");
      expect(unavailable.value?.before.state).toBe("Available");
      expect(unavailable.value?.before.text).toContain("1 + 2");
      expect(unavailable.value?.after.state).not.toBe("Available");
      expect(unavailable.value?.isExact).toBe(false);
      expect(unavailable.value?.diff).toBeNull();
    });

  test("production Worker decodes cancellation and successor comparison",
    async ({ page }) => {
      test.skip(!beforePackage || !afterPackage || !beforeSource || !afterSource,
        "Set the four catalog-resolved Source comparison fixture assets.");
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
      let releaseSource!: () => void;
      const sourceGate = new Promise<void>(resolve => {
        releaseSource = resolve;
      });
      let sourceStarted!: () => void;
      const sourceStart = new Promise<void>(resolve => {
        sourceStarted = resolve;
      });
      let holdSource = true;
      await page.context().route(
        "https://raw.githubusercontent.com/dotnet-inspect-fixtures/source-comparison/**",
        async route => {
          if (holdSource) {
            sourceStarted();
            await sourceGate;
          }
          const after =
            route.request().url().includes("/source-comparison/v2/");
          await route.fulfill({
            path: after ? afterSource! : beforeSource!,
            contentType: "text/plain",
            headers: { "access-control-allow-origin": "*" },
          }).catch(() => undefined);
        });

      const applicationUrl = new URL(site!);
      applicationUrl.searchParams.set("source-comparison-gate", "1");
      await page.goto(applicationUrl.href);
      await expect.poll(() => page.evaluate(() =>
        (window as SourceComparisonGateWindow)
          .__inspectWebSourceComparison !== undefined), {
        timeout: 180_000,
      }).toBe(true);
      const request = await page.evaluate(async () => {
        const bridge = (window as SourceComparisonGateWindow)
          .__inspectWebSourceComparison;
        if (!bridge) throw new Error("Source comparison bridge is unavailable.");
        const loadResult = await bridge.package.queryPackage(
          "InspectWeb.SourceComparisonFixture", "1.0.0", "net11.0",
        );
        const surface = loadResult.surface;
        if (surface === null) {
          throw new Error(
            loadResult.versionSettlement.content.failure?.reason
              ?? "Package version settlement did not produce a surface.",
          );
        }
        const type = surface.types.find(candidate =>
          candidate.definitionId === "SourceDiffFixture.Counter");
        const member = type?.api.find(candidate => candidate.name === "Value");
        const body = member?.bodySelectors.find(candidate =>
          candidate.token === member.metadataToken);
        if (!type || !member || !body) {
          throw new Error("The fixture does not expose Counter.Value.");
        }
        const endpoint = {
          typeIdentity: type.definitionId,
          stableSelector: member.stableSelector,
          canonicalSignature: member.canonicalSignature,
          fingerprint: member.anchorDigest,
          typeFullName: member.anchorTypeFullName,
          memberName: member.name,
        };
        return {
          packageId: surface.package,
          beforeVersion: surface.version,
          afterVersion: "2.0.0",
          framework: surface.activeFramework,
          assembly: type.assemblyId,
          before: endpoint,
          after: endpoint,
        };
      });
      const canceledId = "source-comparison-worker-canceled";
      await page.evaluate(({ operationId, comparisonRequest }) => {
        const target = window as SourceComparisonGateWindow;
        const bridge = target.__inspectWebSourceComparison;
        if (!bridge) throw new Error("Source comparison bridge is unavailable.");
        target.__sourceComparisonPending =
          bridge.source.queryMemberSourceComparison(
            operationId,
            comparisonRequest,
          );
      }, { operationId: canceledId, comparisonRequest: request });
      await sourceStart;
      await page.evaluate(async operationId => {
        const bridge = (window as SourceComparisonGateWindow)
          .__inspectWebSourceComparison;
        if (!bridge) throw new Error("Source comparison bridge is unavailable.");
        await bridge.source.cancelMemberSourceComparison(
          operationId,
          "superseded",
        );
      }, canceledId);
      holdSource = false;
      releaseSource();
      const canceled = await page.evaluate(async () => {
        const target = window as SourceComparisonGateWindow;
        if (!target.__sourceComparisonPending) {
          throw new Error("Canceled Source comparison is unavailable.");
        }
        return await target.__sourceComparisonPending;
      });
      expect(canceled.kind).toBe("Canceled");
      expect(canceled.reason).toBe("superseded");

      const successor = await page.evaluate(
        async ({ operationId, comparisonRequest }) => {
          const bridge = (window as SourceComparisonGateWindow)
            .__inspectWebSourceComparison;
          if (!bridge) throw new Error("Source comparison bridge is unavailable.");
          return await bridge.source.queryMemberSourceComparison(
            operationId,
            comparisonRequest,
          );
        },
        {
          operationId: "source-comparison-worker-successor",
          comparisonRequest: request,
        },
      );
      expect(successor.kind).toBe("Succeeded");
      expect(successor.value?.status).toBe("Compared");
      expect(successor.value?.diff?.before.lines.some(line =>
        line.includes("1 + 2"))).toBe(true);
      expect(successor.value?.diff?.after.lines.some(line =>
        line.includes("=> 3"))).toBe(true);
    });
});
