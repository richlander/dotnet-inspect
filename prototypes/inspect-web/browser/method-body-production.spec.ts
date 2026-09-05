import { expect, test, type Page } from "@playwright/test";

const site = process.env.INSPECT_WEB_METHOD_BODY_URL;
const fixturePackage = process.env.INSPECT_WEB_METHOD_BODY_FIXTURE;

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

test.describe("published Method Body Diff", () => {
  test.skip(!site, "Set INSPECT_WEB_METHOD_BODY_URL to the published Wasm site.");
  test.setTimeout(180_000);

  test("generated facade preserves real pair, exact, and bodyless outcomes", async ({ page }, testInfo) => {
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
        candidate => candidate.definitionId === "Microsoft.Extensions.Primitives.StringSegment",
      );
      const member = type?.api.find(candidate => candidate.name === "Trim");
      if (!type || !member)
        throw new Error("The published package does not contain StringSegment.Trim.");
      const body = member.bodySelectors.find(
        candidate => candidate.token === member.metadataToken,
      );
      if (!body)
        throw new Error("StringSegment.Trim has no exact Browser body selector.");
      const prepared = await source.queryMethodBodyComparisonTargets(
        "method-body-production-targets",
        surface.package, surface.version, surface.activeFramework,
        type.assemblyId, type.definitionId,
        body.memberName, body.selectorKey, body.token,
      );
      if (prepared.kind !== "Succeeded" || !prepared.value)
        throw new Error(`Comparison preparation failed: ${JSON.stringify(prepared)}`);
      const targets = prepared.value;
      const after = targets.methods.find(candidate =>
        candidate.typeIdentity === targets.before.typeIdentity &&
        candidate.memberName === "TrimStart");
      const bodyless = targets.methods.find(candidate =>
        candidate.typeIdentity === "Microsoft.Extensions.Primitives.IChangeToken" &&
        candidate.memberName === "RegisterChangeCallback");
      if (!after || !bodyless)
        throw new Error("The implementation inventory lost a demo method.");
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

    await testInfo.attach("native-comparisons.json", {
      body: JSON.stringify(evidence, null, 2),
      contentType: "application/json",
    });
    expect(evidence.cancellation.kind).toBe("NotActive");
    for (const result of [evidence.different, evidence.same, evidence.noBody]) {
      expect(result.kind).toBe("Succeeded");
      expect(result.value?.stage).toBe("Research");
      expect(result.value?.outcome).toBe("Completed");
      expect(result.value?.producers).toHaveLength(2);
      expect(result.value?.request.before).toEqual(evidence.targets.before);
      for (const producer of result.value!.producers) {
        expect(producer.before.moduleVersionId).toBe(evidence.targets.moduleVersionId);
        expect(producer.after.moduleVersionId).toBe(evidence.targets.moduleVersionId);
      }
    }
    const different = evidence.different.value!;
    expect(different.producers.find(producer => producer.cSharp)?.cSharp?.isExact).toBe(false);
    expect(different.producers.find(producer => producer.il)?.il?.isExact).toBe(false);
    const same = evidence.same.value!;
    expect(same.producers.find(producer => producer.cSharp)?.cSharp?.isExact).toBe(true);
    expect(same.producers.find(producer => producer.il)?.il?.isExact).toBe(true);
    for (const producer of evidence.noBody.value!.producers) {
      expect(producer.after.state).toBe("NoApplicableInput");
      expect(producer.nativeVerdict).not.toBe("Exact");
    }
  });

  test("compiled reference package reaches the real comparison and accessor paths", async ({ page }, testInfo) => {
    test.skip(!fixturePackage, "Set INSPECT_WEB_METHOD_BODY_FIXTURE to the catalog package asset.");
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
      if (!type || !member || !launch)
        throw new Error("The compiled reference fixture lost Left.Compute(int).");
      const prepared = await source.queryMethodBodyComparisonTargets(
        "method-body-fixture-targets",
        surface.package, surface.version, surface.activeFramework, type.assemblyId,
        type.definitionId, launch.memberName, launch.selectorKey, launch.token,
      );
      if (prepared.kind !== "Succeeded" || !prepared.value)
        throw new Error(`Fixture preparation failed: ${JSON.stringify(prepared)}`);
      const targets = prepared.value;
      const different = targets.methods.find(candidate => candidate.memberName === "Transform");
      const bodyless = targets.methods.find(candidate => candidate.memberName === "WithoutBody");
      const getter = targets.methods.find(candidate => candidate.memberName === "get_Value");
      const setter = targets.methods.find(candidate => candidate.memberName === "set_Value");
      if (!different || !bodyless || !getter || !setter)
        throw new Error("The compiled implementation inventory lost a fixture method.");
      const request = {
        packageId: targets.packageId, version: targets.version,
        framework: targets.framework, assembly: targets.assembly,
        moduleVersionId: targets.moduleVersionId,
        before: targets.before, after: different,
      };
      const compared = await source.queryMethodBodyComparison(
        "method-body-fixture-different", JSON.stringify(request),
      );
      const noBody = await source.queryMethodBodyComparison(
        "method-body-fixture-bodyless", JSON.stringify({ ...request, after: bodyless }),
      );
      const accessors = await source.queryMethodBodyComparison(
        "method-body-fixture-accessors",
        JSON.stringify({ ...request, before: getter, after: setter }),
      );
      return { launch, targets, compared, noBody, accessors, getter, setter };
    });
    await testInfo.attach("compiled-fixture-comparisons.json", {
      body: JSON.stringify(evidence, null, 2),
      contentType: "application/json",
    });
    expect(evidence.targets.before.metadataToken).not.toBe(evidence.launch.token);
    expect(evidence.compared.kind).toBe("Succeeded");
    expect(evidence.compared.value?.outcome).toBe("Completed");
    expect(evidence.compared.value?.producers.find(producer => producer.cSharp)?.cSharp?.isExact).toBe(false);
    expect(evidence.compared.value?.producers.find(producer => producer.il)?.il?.isExact).toBe(false);
    expect(evidence.noBody.kind).toBe("Succeeded");
    expect(evidence.noBody.value?.producers).toHaveLength(2);
    for (const producer of evidence.noBody.value!.producers)
      expect(producer.after.state).toBe("NoApplicableInput");
    expect(evidence.accessors.kind).toBe("Succeeded");
    expect(evidence.accessors.value?.producers).toHaveLength(2);
    for (const producer of evidence.accessors.value!.producers) {
      expect(producer.before.metadataToken).toBe(evidence.getter.metadataToken);
      expect(producer.after.metadataToken).toBe(evidence.setter.metadataToken);
    }
  });

  test("real dialog keeps explicit pairs, native outcomes, navigation, and focus", async ({ page }, testInfo) => {
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
    await page.locator("button.api-row[data-member]").filter({ hasText: /\bTrim\b/ }).click();
    await page.locator('[data-member-section="source"]').click();
    const action = page.locator("#compare-method-bodies");
    await expect(action).toBeEnabled();
    const beforeUrl = page.url();
    const beforeHistory = await page.evaluate(() => history.length);
    await action.click();
    const dialog = page.locator("#method-body-diff-modal");
    const after = dialog.locator("#method-body-diff-after");
    const compare = dialog.locator("#method-body-diff-compare");
    await expect(dialog).toBeVisible();
    await expect(after).toBeEnabled();
    await expect(after).toBeFocused();
    await expect(compare).toBeDisabled();

    async function choose(
      method: string, type = "Microsoft.Extensions.Primitives.StringSegment",
    ): Promise<void> {
      const option = after.locator("option").filter({
        hasText: new RegExp(`\\b${method}\\s*\\(`),
      }).filter({ hasText: `${type} /` });
      await expect(option).toHaveCount(1);
      const label = await option.textContent();
      if (label === null)
        throw new Error(`No label for the product-issued ${method} selection.`);
      await after.selectOption({ label });
      await expect(after).toBeFocused();
      await expect(dialog.locator("[data-method-body-comparison-outcome]")).toHaveCount(0);
    }
    async function submit(): Promise<void> {
      await compare.click();
      await expect(dialog.locator("[data-method-body-comparison-outcome]"))
        .toHaveText("Completed", { timeout: 60_000 });
    }
    const csharp = dialog.locator('[data-method-body-producer="CSharp"]');
    const il = dialog.locator('[data-method-body-producer="IlBody"]');
    await choose("TrimStart");
    await submit();
    await expect(csharp.locator('[data-method-body-exact="false"]')).toBeVisible();
    await expect(il.locator("[data-method-body-il-outcome]")).toBeVisible();
    await expect(il.locator("details")).toHaveJSProperty("open", false);
    await il.locator("summary").click();
    await expect(il.locator('[data-method-body-row="il"]').first()).toBeVisible();
    await testInfo.attach("different-methods.png", {
      body: await page.screenshot({ fullPage: true }), contentType: "image/png",
    });

    await choose("Trim");
    await submit();
    await expect(csharp.locator('[data-method-body-exact="true"]')).toBeVisible();
    await expect(il.locator('[data-method-body-exact="true"]')).toBeVisible();

    await choose("RegisterChangeCallback", "Microsoft.Extensions.Primitives.IChangeToken");
    await submit();
    for (const lane of [csharp, il]) {
      await expect(lane.locator(
        '[data-method-body-endpoint="after"] [data-method-body-endpoint-state]',
      )).toHaveText("NoApplicableInput");
      await expect(lane.locator("[data-method-body-exact]")).toHaveCount(0);
    }
    await page.setViewportSize({ width: 390, height: 844 });
    await expect(dialog).toBeVisible();
    expect(await page.evaluate(() =>
      document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
    await testInfo.attach("bodyless-narrow.png", {
      body: await page.screenshot({ fullPage: true }), contentType: "image/png",
    });
    await page.keyboard.press("Escape");
    await expect(dialog).toHaveCount(0);
    await expect(action).toBeFocused();
    expect(page.url()).toBe(beforeUrl);
    expect(await page.evaluate(() => history.length)).toBe(beforeHistory);
    await expect(page.getByRole("group", { name: "Source actions" })
      .getByRole("button", { name: "Copy", exact: true })).toBeEnabled({ timeout: 60_000 });
  });
});
