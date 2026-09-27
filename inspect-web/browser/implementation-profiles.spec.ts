import { expect, test } from "@playwright/test";
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
): BrowserMemberSurface {
  return {
    ...run,
    name,
    signature,
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
    members: 4,
    api: [
      overload("Run(int)", "public void Run(int value)", 0x06000100),
      overload("Run(string)", "public void Run(string value)", 0x06000101),
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
    totalMembers: 5,
  };
}

test("Type heat paints after the member list and family evidence stays behind its disclosure", async ({
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

  const runFamily = page.locator("[data-nav-member]").filter({ hasText: "Run" });
  await runFamily.click();
  await expect(runFamily.locator(".family-heat-cue.progress"))
    .toHaveText("measuring");

  await releaseFacade(page, `fixture-type-heat-ready:${core.id}`);
  await expect(runFamily.locator(".family-heat-cue")).toHaveCount(0);
  const rows = page.locator(".overload-nav-row");
  await expect(rows).toHaveCount(2);
  await expect(rows.nth(0)).toHaveClass(/\bheated\b/);
  await expect(rows.nth(0)).not.toHaveClass(/\bhub\b/);
  await expect(rows.nth(0)).toHaveAttribute(
    "aria-description",
    "98 instructions; 100% of the largest body in this family",
  );
  await expect(rows.nth(1)).toHaveClass(/\bhub\b/);
  await expect(rows.nth(1)).not.toHaveClass(/\bheated\b/);

  await page.locator('[data-nav-overload="0"]').click();
  await expect(page.getByRole("heading", { name: "Implementation" }))
    .toBeVisible();
  await expect(page.locator(".implementation-profile-heat-summary"))
    .toHaveText("98 instructions; 100% of the largest body in this family");
  expect(await html.getAttribute(
    "data-implementation-profile-request-count")).toBeNull();

  await page.getByText("Implementation evidence", { exact: true }).click();
  await expect(html).toHaveAttribute(
    "data-implementation-profile-request-count",
    "1",
  );
  expect(JSON.parse(
    await html.getAttribute("data-implementation-profile-request") ?? "null",
  )).toEqual([
    "Example.Package",
    "1.0.0",
    "net10.0",
    core.id,
    "Example.Widget",
    ["Run(int)", "Run(string)"],
  ]);
  await releaseFacade(
    page,
    `fixture-implementation-profiles-ready:${core.id}`,
  );
  await expect(page.locator(".implementation-profile-overload"))
    .toHaveCount(1);
  await expect(page.locator(".implementation-profile-physical-row"))
    .toHaveCount(2);
  await expect(page.getByText("Branches: 8", { exact: true })).toBeVisible();

  // Moving within the Type reuses its heat, and a disclosure opened on one
  // overload never turns later selection into family requests.
  const compute = page.locator("[data-nav-member]")
    .filter({ hasText: "Compute" });
  await compute.click();
  await expect(rows).toHaveCount(2);
  await expect(rows.nth(0)).toHaveClass(/\bheated\b/);
  await expect(rows.nth(1)).toHaveClass(/\bheated\b/);
  await expect(html).toHaveAttribute("data-type-heat-request-count", "1");
  await page.locator('[data-nav-overload="1"]').click();
  await expect(page.locator("[data-implementation-evidence]"))
    .not.toHaveAttribute("open", "");
  await expect(html).toHaveAttribute(
    "data-implementation-profile-request-count",
    "1",
  );
  await page.getByText("Implementation evidence", { exact: true }).click();
  await expect(html).toHaveAttribute(
    "data-implementation-profile-request-count",
    "2",
  );
  expect(JSON.parse(
    await html.getAttribute("data-implementation-profile-request") ?? "null",
  )).toEqual([
    "Example.Package",
    "1.0.0",
    "net10.0",
    core.id,
    "Example.Widget",
    ["Compute(int)", "Compute(string)"],
  ]);
});

function packageWithPlainType(): BrowserPackageSurface {
  const overloaded = overloadedPackage();
  return {
    ...overloaded,
    assemblies: overloaded.assemblies.map(assembly =>
      assembly.id === core.id
        ? { ...assembly, publicMembers: 5, publicTypes: 2 }
        : assembly),
    types: [
      ...overloaded.types.slice(0, 1),
      createType("Example.Plain", core),
      ...overloaded.types.slice(1),
    ],
    totalMembers: 6,
  };
}

test("results that settle while the reader is away are shown on return without a request", async ({
  page,
}) => {
  await installFacades(
    page,
    packageWithPlainType(),
    [],
    "ready",
    "ready",
    undefined,
    "ready",
    "deferred",
  );
  await page.goto(root);
  await selectLibrary(page, core.id);
  const html = page.locator("html");
  const types = page.locator("#type-list [data-type]");
  const openType = async (name: string) => {
    await chooseSubject(page, "type", "Type");
    await types.filter({ hasText: name }).first().click();
    await chooseSubject(page, "member", "Member");
  };
  const runFamily = page.locator("[data-nav-member]").filter({ hasText: "Run" });

  // Type heat settles while the reader is on a Type with no eligible family.
  await openType("Widget");
  await expect(html).toHaveAttribute("data-type-heat-request-count", "1");
  await runFamily.click();
  await expect(runFamily.locator(".family-heat-cue.progress"))
    .toHaveText("measuring");
  await openType("Plain");
  await releaseFacade(page, `fixture-type-heat-ready:${core.id}`);
  await openType("Widget");
  await runFamily.click();
  await expect(page.locator(".overload-nav-row").nth(0))
    .toHaveClass(/\bheated\b/);
  await expect(runFamily.locator(".family-heat-cue")).toHaveCount(0);
  await expect(html).toHaveAttribute("data-type-heat-request-count", "1");

  // Family detail settles while the reader is on another family.
  await page.locator('[data-nav-overload="0"]').click();
  await page.getByText("Implementation evidence", { exact: true }).click();
  await expect(html).toHaveAttribute(
    "data-implementation-profile-request-count",
    "1",
  );
  await page.locator("[data-nav-member]").filter({ hasText: "Compute" })
    .click();
  await releaseFacade(
    page,
    `fixture-implementation-profiles-ready:${core.id}`,
  );
  await runFamily.click();
  await page.locator('[data-nav-overload="0"]').click();
  await expect(page.locator("[data-implementation-evidence]"))
    .not.toHaveAttribute("open", "");
  await page.getByText("Implementation evidence", { exact: true }).click();
  await expect(page.locator(".implementation-profile-overload"))
    .toHaveCount(1);
  await expect(html).toHaveAttribute(
    "data-implementation-profile-request-count",
    "1",
  );
});
