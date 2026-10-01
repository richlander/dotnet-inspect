import { expect, test, type Page } from "@playwright/test";
import {
  chooseSubject,
  core,
  createType,
  installFacades,
  releaseFacade,
  root,
  run,
  selectLibrary,
  surface,
  type BrowserMemberSurface,
  type BrowserPackageSurface,
} from "./library-hierarchy.support.ts";

test.use({ viewport: { width: 1000, height: 900 } });

function overload(
  stableSelector: string,
  signature: string,
  metadataToken: number,
  name = "Run",
  accessibility = "public",
): BrowserMemberSurface {
  return {
    ...run,
    name,
    signature,
    accessibility,
    metadataToken,
    declarationMetadataToken: metadataToken,
    documentationId: `M:Example.Widget.${stableSelector}`,
    stableSelector,
    anchorDigest: `widget-${stableSelector}`,
    canonicalSignature: signature,
    graphSelectorKey: stableSelector,
    bodySelectors: [{
      token: metadataToken,
      memberName: name,
      selectorKey: stableSelector,
    }],
  };
}

function overloadedPackage(): BrowserPackageSurface {
  const type = {
    ...createType("Example.Widget", core),
    members: 8,
    api: [
      overload("Run(int)", "public void Run(int value)", 0x06000100),
      overload("Run(string)", "public void Run(string value)", 0x06000101),
      overload(
        "Run(Guid)",
        "private void Run(Guid value)",
        0x06000104,
        "Run",
        "private",
      ),
      overload(
        "Run(DateTime)",
        "private void Run(DateTime value)",
        0x06000105,
        "Run",
        "private",
      ),
      overload(
        "Compute(int)",
        "public void Compute(int value)",
        0x06000102,
        "Compute",
      ),
      overload(
        "Compute(string)",
        "public void Compute(string value)",
        0x06000103,
        "Compute",
      ),
      overload(
        "Hidden(int)",
        "private void Hidden(int value)",
        0x06000106,
        "Hidden",
        "private",
      ),
      overload(
        "Hidden(string)",
        "private void Hidden(string value)",
        0x06000107,
        "Hidden",
        "private",
      ),
    ],
  };
  return {
    ...surface,
    assemblies: surface.assemblies.map(assembly =>
      assembly.id === core.id
        ? { ...assembly, publicMembers: 4 }
        : assembly),
    types: [
      type,
      ...surface.types.filter(candidate =>
        candidate.definitionId !== type.definitionId),
    ],
    totalMembers: 9,
  };
}

async function selectMemberAccessibility(
  page: Page,
  accessibility: "public" | "private",
) {
  const select = page.locator("[data-member-access-filter]");
  if (!await select.isVisible())
    await page.locator("#member-filter-summary").click();
  await select.selectOption(accessibility);
}

test("Type heat paints the member list without an Implementation section", async ({
  page,
}) => {
  await installFacades(
    page,
    overloadedPackage(),
    [],
    "ready",
    "ready",
    undefined,
    "ready",
    "deferred",
  );
  await page.goto(root);
  await selectLibrary(page, core.id);
  await chooseSubject(page, "type", "Type");
  await page.locator("#type-list [data-type]").click();
  await chooseSubject(page, "member", "Member");

  // One Type request, made after the member list paints; no family detail.
  const html = page.locator("html");
  await expect(html).toHaveAttribute("data-type-heat-request-count", "1");
  expect(JSON.parse(
    await html.getAttribute("data-type-heat-request") ?? "null",
  )).toEqual([
    "Example.Package",
    "1.0.0",
    "net10.0",
    core.id,
    "Example.Widget",
  ]);
  expect(await html.getAttribute(
    "data-implementation-profile-request-count")).toBeNull();
  await expect(page.locator('[data-member-section="implementation-profiles"]'))
    .toHaveCount(0);

  await selectMemberAccessibility(page, "private");
  const hidden = page.locator("[data-nav-member]")
    .filter({ hasText: "Hidden" });
  await hidden.click();
  await expect(hidden.locator(".family-heat-cue")).toHaveCount(0);
  const privateRun = page.locator("[data-nav-member]")
    .filter({ hasText: "Run" });
  await privateRun.click();
  await expect(privateRun.locator(".family-heat-cue.progress"))
    .toHaveText("measuring");

  await releaseFacade(page, `fixture-type-heat-ready:${core.id}`);
  await expect(privateRun.locator(".family-heat-cue")).toHaveCount(0);
  const rows = page.locator(".overload-nav-row");
  await expect(rows).toHaveCount(2);
  await expect(rows.nth(0)).toHaveClass(/\bheated\b/);
  await expect(rows.nth(0)).toHaveAttribute(
    "aria-description",
    "70 instructions; 71% of the largest body in this family, which is not a listed overload",
  );
  await expect(rows.nth(1)).toHaveClass(/\bhub\b/);
  await expect(rows.nth(1)).not.toHaveClass(/\bheated\b/);
  await expect(rows.locator(".overload-size")).toHaveCount(0);

  // Public and private populations reuse one Type record.
  await selectMemberAccessibility(page, "public");
  const runFamily = page.locator("[data-nav-member]").filter({ hasText: "Run" });
  await runFamily.click();
  await expect(rows).toHaveCount(2);
  await expect(rows.nth(0)).toHaveClass(/\bheated\b/);
  await expect(rows.nth(0)).not.toHaveClass(/\bhub\b/);
  await expect(rows.nth(0)).toHaveAttribute(
    "aria-description",
    "98 instructions; 100% of the largest body in this family",
  );
  await expect(rows.nth(1)).toHaveClass(/\bhub\b/);

  const compute = page.locator("[data-nav-member]")
    .filter({ hasText: "Compute" });
  await compute.click();
  await expect(rows).toHaveCount(2);
  await expect(rows.nth(0)).toHaveClass(/\bheated\b/);
  await expect(rows.nth(1)).toHaveClass(/\bheated\b/);
  await expect(html).toHaveAttribute("data-type-heat-request-count", "1");

  await page.locator('[data-nav-overload="1"]').click();
  await expect(page.getByRole("heading", { name: "Implementation" }))
    .toHaveCount(0);
  await expect(page.getByText("Implementation evidence", { exact: true }))
    .toHaveCount(0);
  expect(await html.getAttribute(
    "data-implementation-profile-request-count")).toBeNull();
});

test("non-public-only families do not inherit Type heat failure", async ({
  page,
}) => {
  await installFacades(
    page,
    overloadedPackage(),
    [],
    "ready",
    "ready",
    undefined,
    "ready",
    "query-error",
  );
  await page.goto(root);
  await selectLibrary(page, core.id);
  await chooseSubject(page, "type", "Type");
  await page.locator("#type-list [data-type]").click();
  await chooseSubject(page, "member", "Member");
  await expect(page.locator("html"))
    .toHaveAttribute("data-type-heat-request-count", "1");

  await selectMemberAccessibility(page, "private");
  const hidden = page.locator("[data-nav-member]")
    .filter({ hasText: "Hidden" });
  await hidden.click();
  await expect(hidden.locator(".family-heat-cue")).toHaveCount(0);

  const runFamily = page.locator("[data-nav-member]").filter({ hasText: "Run" });
  await runFamily.click();
  await expect(runFamily.locator(".family-heat-cue.problem"))
    .toHaveText("heat unavailable");
});
