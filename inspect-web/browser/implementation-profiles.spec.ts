import { expect, test } from "@playwright/test";
import {
  chooseInspector,
  chooseSubject,
  core,
  createType,
  installFacades,
  inspectorTab,
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
): BrowserMemberSurface {
  return {
    ...run,
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
      memberName: "Run",
      selectorKey: stableSelector,
    }],
  };
}

function overloadedPackage(): BrowserPackageSurface {
  const type = {
    ...createType("Example.Widget", core),
    members: 2,
    api: [
      overload("Run(int)", "public void Run(int value)", 0x06000100),
      overload("Run(string)", "public void Run(string value)", 0x06000101),
    ],
  };
  return {
    ...surface,
    assemblies: surface.assemblies.map(assembly =>
      assembly.id === core.id
        ? { ...assembly, publicMembers: 2 }
        : assembly),
    types: [
      type,
      ...surface.types.filter(candidate =>
        candidate.definitionId !== type.definitionId),
    ],
    totalMembers: 3,
  };
}

test("implementation profiles stay lazy, preserve family identity, and render accessible evidence", async ({
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

  const html = page.locator("html");
  expect(await html.getAttribute(
    "data-implementation-profile-request-count")).toBeNull();
  const tab = inspectorTab(
    page,
    "data-member-section",
    "implementation-profiles",
  );
  await expect(tab).toHaveAttribute("aria-label", "Implementation profiles");
  await chooseInspector(
    page,
    "data-member-section",
    "implementation-profiles",
    "Implementation profiles",
  );

  await expect(page.getByRole("heading", {
    name: "Loading implementation profiles",
  })).toBeVisible();
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
  await expect(page.getByRole("heading", {
    name: "Example.Widget.Run",
  })).toBeVisible();
  await expect(page.locator(".member-surface-head p"))
    .toContainText("2 overloads");
  await expect(page.locator(".implementation-profile-overload"))
    .toHaveCount(2);
  await expect(page.locator(".implementation-profile-physical-row"))
    .toHaveCount(3);
  await expect(page.getByRole("heading", {
    name: "Generated physical body",
  })).toBeVisible();
  await expect(page.getByRole("img", {
    name: "80 instructions; 100% of the largest physical body in this overload family",
  })).toBeVisible();
  await expect(page.getByText("Branches: 8", { exact: true })).toBeVisible();
  await expect(page.getByText("Loops: 2", { exact: true })).toBeVisible();

  await page.getByText("Raw implementation metrics", { exact: true })
    .first()
    .click();
  await expect(
    page.getByText("Distinct opcode count", { exact: true }).first(),
  ).toBeVisible();

  await chooseInspector(
    page,
    "data-member-section",
    "overview",
    "Overview",
  );
  await chooseInspector(
    page,
    "data-member-section",
    "implementation-profiles",
    "Implementation profiles",
  );
  await expect(html).toHaveAttribute(
    "data-implementation-profile-request-count",
    "1",
  );
  await expect(page.getByRole("heading", {
    name: "Example.Widget.Run",
  })).toBeVisible();
});
