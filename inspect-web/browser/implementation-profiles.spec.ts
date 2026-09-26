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

test("member-list heat follows the expanded family and marks the hub", async ({
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

  const html = page.locator("html");
  expect(await html.getAttribute(
    "data-implementation-profile-request-count")).toBeNull();
  await expect(page.locator('[data-member-section="implementation-profiles"]'))
    .toHaveCount(0);

  await chooseSubject(page, "member", "Member");
  await page.locator("[data-nav-member]").filter({ hasText: "Run" }).click();
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
  const runRow = page.locator("[data-nav-member]").filter({ hasText: "Run" });
  await expect(runRow.locator(".family-heat-cue")).toHaveText("measuring");
  await expect(page.locator(".overload-nav-row.heated")).toHaveCount(0);

  await releaseFacade(
    page,
    `fixture-implementation-profiles-ready:${core.id}`,
  );
  const intRow = page.locator('[data-nav-overload="0"]');
  const stringRow = page.locator('[data-nav-overload="1"]');
  await expect(intRow).toHaveClass(/\bheated\b/);
  await expect(intRow).not.toHaveClass(/\bhub\b/);
  await expect(intRow).toHaveAttribute(
    "aria-description",
    "80 instructions; 100% of the largest body in this family",
  );
  await expect(stringRow).toHaveClass(/\bhub\b/);
  await expect(stringRow).not.toHaveClass(/\bheated\b/);
  await expect(stringRow).toHaveAttribute(
    "aria-description",
    "5 instructions; 6% of the largest body in this family; hub called by 1 sibling method",
  );
  await expect(runRow.locator(".family-heat-cue")).toHaveCount(0);

  await page.locator("[data-nav-member]").filter({ hasText: "Compute" })
    .click();
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
  await releaseFacade(
    page,
    `fixture-implementation-profiles-ready:${core.id}`,
  );

  await page.locator("[data-nav-member]").filter({ hasText: "Run" }).click();
  await expect(page.locator('[data-nav-overload="0"]'))
    .toHaveClass(/\bheated\b/);
  await expect(html).toHaveAttribute(
    "data-implementation-profile-request-count",
    "2",
  );

  await page.locator('[data-nav-overload="0"]').click();
  await expect(page.getByRole("heading", { name: "Implementation" }))
    .toBeVisible();
  await expect(page.locator(".implementation-profile-physical-row"))
    .toHaveCount(2);
  await expect(page.getByRole("heading", {
    name: "Generated physical body",
  })).toBeVisible();
  await expect(page.getByText("Branches: 8", { exact: true })).toBeVisible();
  await page.getByText("Raw implementation metrics", { exact: true })
    .first()
    .click();
  await expect(
    page.getByText("Distinct opcode count", { exact: true }).first(),
  ).toBeVisible();
  await expect(html).toHaveAttribute(
    "data-implementation-profile-request-count",
    "2",
  );
});
