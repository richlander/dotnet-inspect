import { expect, test, type Page } from "@playwright/test";
import { writeFile } from "node:fs/promises";

const site = process.env.INSPECT_WEB_METHOD_BODY_URL;
const fixturePackage = process.env.INSPECT_WEB_METHOD_BODY_FIXTURE;

async function openPublishedSite(page: Page): Promise<void> {
  await page.goto(site!, { waitUntil: "networkidle" });
  await page.setContent("<!doctype html><title>Method facade test</title>");
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

test.describe("published Method Body comparison transport", () => {
  test.skip(!site, "Set INSPECT_WEB_METHOD_BODY_URL to the published Wasm site.");
  test.setTimeout(180_000);

  test("generated facade preserves real pair, exact, and bodyless outcomes",
    async ({ page }, testInfo) => {
      await openPublishedSite(page);

      const evidence = await page.evaluate(async () => {
        const packages = await import("/inspect-web-package.js");
        const source = await import("/inspect-web-source.js");
        const cancellation = source.cancelMethodBodyComparison(
          "method-body-production-not-active", "user",
        );
        const surface = await packages.queryPackage(
          "Microsoft.Extensions.Primitives", "10.0.0", "net10.0",
        );
        const type = surface.types.find(
          candidate =>
            candidate.definitionId
            === "Microsoft.Extensions.Primitives.StringSegment",
        );
        const member = type?.api.find(candidate => candidate.name === "Trim");
        if (!type || !member) {
          throw new Error(
            "The published package does not contain StringSegment.Trim.");
        }
        const body = member.bodySelectors.find(
          candidate => candidate.token === member.metadataToken,
        );
        if (!body) {
          throw new Error(
            "StringSegment.Trim has no exact Browser body selector.");
        }
        const prepared = await source.queryMethodBodyComparisonTargets(
          "method-body-production-targets",
          surface.package, surface.version, surface.activeFramework,
          type.assemblyId, type.definitionId,
          body.memberName, body.selectorKey, body.token,
        );
        if (prepared.kind !== "Succeeded" || !prepared.value) {
          throw new Error(
            `Comparison preparation failed: ${JSON.stringify(prepared)}`);
        }
        const targets = prepared.value;
        const after = targets.methods.find(candidate =>
          candidate.typeIdentity === targets.before.typeIdentity
          && candidate.memberName === "TrimStart");
        const bodyless = targets.methods.find(candidate =>
          candidate.typeIdentity
            === "Microsoft.Extensions.Primitives.IChangeToken"
          && candidate.memberName === "RegisterChangeCallback");
        if (!after || !bodyless) {
          throw new Error(
            "The implementation inventory lost a demo method.");
        }
        const request = {
          packageId: targets.packageId,
          version: targets.version,
          framework: targets.framework,
          assembly: targets.assembly,
          moduleVersionId: targets.moduleVersionId,
          before: targets.before,
          after,
        };
        const different = await source.queryMethodBodyComparison(
          "method-body-production-different", JSON.stringify(request),
        );
        const same = await source.queryMethodBodyComparison(
          "method-body-production-same",
          JSON.stringify({ ...request, after: targets.before }),
        );
        const noBody = await source.queryMethodBodyComparison(
          "method-body-production-bodyless",
          JSON.stringify({ ...request, after: bodyless }),
        );
        return { targets, different, same, noBody, cancellation };
      });

      const evidencePath = testInfo.outputPath("native-comparisons.json");
      await writeFile(evidencePath, JSON.stringify(evidence, null, 2));
      await testInfo.attach("native-comparisons.json", {
        path: evidencePath,
        contentType: "application/json",
      });
      expect(evidence.cancellation.kind).toBe("NotActive");
      for (const result of [
        evidence.different, evidence.same, evidence.noBody,
      ]) {
        expect(result.kind).toBe("Succeeded");
        expect(result.value?.stage).toBe("Research");
        expect(result.value?.outcome).toBe("Completed");
        expect(result.value?.producers).toHaveLength(2);
        expect(result.value?.request.before).toEqual(evidence.targets.before);
        for (const producer of result.value!.producers) {
          expect(producer.before.moduleVersionId)
            .toBe(evidence.targets.moduleVersionId);
          expect(producer.after.moduleVersionId)
            .toBe(evidence.targets.moduleVersionId);
        }
      }
      const different = evidence.different.value!;
      expect(different.producers.find(producer => producer.cSharp)
        ?.cSharp?.isExact).toBe(false);
      expect(different.producers.find(producer => producer.il)
        ?.il?.isExact).toBe(false);
      const same = evidence.same.value!;
      expect(same.producers.find(producer => producer.cSharp)
        ?.cSharp?.isExact).toBe(true);
      expect(same.producers.find(producer => producer.il)
        ?.il?.isExact).toBe(true);
      for (const producer of evidence.noBody.value!.producers) {
        expect(producer.after.state).toBe("NoApplicableInput");
        expect(producer.nativeVerdict).not.toBe("Exact");
      }
    });

  test("compiled reference package reaches comparison and accessor paths",
    async ({ page }, testInfo) => {
      test.skip(
        !fixturePackage,
        "Set INSPECT_WEB_METHOD_BODY_FIXTURE to the catalog package asset.");
      await page.route("**/inspectweb.methodbodyfixtures*.nupkg",
        route => route.fulfill({
          path: fixturePackage!,
          contentType: "application/octet-stream",
          headers: { "access-control-allow-origin": "*" },
        }));
      await openPublishedSite(page);
      const evidence = await page.evaluate(async () => {
        const packages = await import("/inspect-web-package.js");
        const source = await import("/inspect-web-source.js");
        const surface = await packages.queryPackage(
          "InspectWeb.MethodBodyFixtures", "1.0.0", "net11.0",
        );
        const type = surface.types.find(candidate =>
          candidate.definitionId === "InspectWeb.MethodBodyFixtures.Left");
        const member = type?.api.find(candidate =>
          candidate.name === "Compute" && candidate.parameters.length === 1);
        const launch = member?.bodySelectors.find(candidate =>
          candidate.token === member.metadataToken);
        if (!type || !member || !launch) {
          throw new Error(
            "The compiled reference fixture lost Left.Compute(int).");
        }
        const prepared = await source.queryMethodBodyComparisonTargets(
          "method-body-fixture-targets",
          surface.package, surface.version, surface.activeFramework,
          type.assemblyId, type.definitionId,
          launch.memberName, launch.selectorKey, launch.token,
        );
        if (prepared.kind !== "Succeeded" || !prepared.value) {
          throw new Error(
            `Fixture preparation failed: ${JSON.stringify(prepared)}`);
        }
        const targets = prepared.value;
        const different = targets.methods.find(
          candidate => candidate.memberName === "Transform");
        const bodyless = targets.methods.find(
          candidate => candidate.memberName === "WithoutBody");
        const getter = targets.methods.find(
          candidate => candidate.memberName === "get_Value");
        const setter = targets.methods.find(
          candidate => candidate.memberName === "set_Value");
        if (!different || !bodyless || !getter || !setter) {
          throw new Error(
            "The compiled implementation inventory lost a fixture method.");
        }
        const request = {
          packageId: targets.packageId,
          version: targets.version,
          framework: targets.framework,
          assembly: targets.assembly,
          moduleVersionId: targets.moduleVersionId,
          before: targets.before,
          after: different,
        };
        const compared = await source.queryMethodBodyComparison(
          "method-body-fixture-different", JSON.stringify(request),
        );
        const noBody = await source.queryMethodBodyComparison(
          "method-body-fixture-bodyless",
          JSON.stringify({ ...request, after: bodyless }),
        );
        const accessors = await source.queryMethodBodyComparison(
          "method-body-fixture-accessors",
          JSON.stringify({ ...request, before: getter, after: setter }),
        );
        return { launch, targets, compared, noBody, accessors, getter, setter };
      });
      const evidencePath =
        testInfo.outputPath("compiled-fixture-comparisons.json");
      await writeFile(evidencePath, JSON.stringify(evidence, null, 2));
      await testInfo.attach("compiled-fixture-comparisons.json", {
        path: evidencePath,
        contentType: "application/json",
      });
      expect(evidence.targets.before.metadataToken)
        .not.toBe(evidence.launch.token);
      expect(evidence.compared.kind).toBe("Succeeded");
      expect(evidence.compared.value?.outcome).toBe("Completed");
      expect(evidence.compared.value?.producers
        .find(producer => producer.cSharp)?.cSharp?.isExact).toBe(false);
      expect(evidence.compared.value?.producers
        .find(producer => producer.il)?.il?.isExact).toBe(false);
      expect(evidence.noBody.kind).toBe("Succeeded");
      expect(evidence.noBody.value?.producers).toHaveLength(2);
      for (const producer of evidence.noBody.value!.producers) {
        expect(producer.after.state).toBe("NoApplicableInput");
      }
      expect(evidence.accessors.kind).toBe("Succeeded");
      expect(evidence.accessors.value?.producers).toHaveLength(2);
      for (const producer of evidence.accessors.value!.producers) {
        expect(producer.before.metadataToken)
          .toBe(evidence.getter.metadataToken);
        expect(producer.after.metadataToken)
          .toBe(evidence.setter.metadataToken);
      }
    });
});
