import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";

const catalogDeclarations = readFileSync(
  new URL("../src/facades/inspect-web-catalog.d.ts", import.meta.url),
  "utf8",
);
const sourceDeclarations = readFileSync(
  new URL("../src/facades/inspect-web-source.d.ts", import.meta.url),
  "utf8",
);

for (const [rowKind, resultType] of [
  ["Package", "BrowserRetainedWorkspacePackageAdmissionResult"],
  ["Platform", "BrowserRetainedWorkspacePlatformAdmissionResult"],
] as const) {
  test(`catalog facade decodes typed retained ${rowKind} detail pages`, () => {
    assert.ok(catalogDeclarations.includes(
      `admitRetainedWorkspace${rowKind}(retainedDefinitionId: string, `
      + "realizationId: string, navigationId: string, typeOffset: number): "
      + `Promise<${resultType}>;`,
    ));
  });
}

test("source facade separates member parts from flat source", () => {
  assert.ok(sourceDeclarations.includes(
    "export interface BrowserMemberSource {",
  ));
  assert.ok(sourceDeclarations.includes(
    "queryMemberSource(packageId: string, version: string, targetFramework: string, "
    + "assemblyName: string, typeIdentity: string, memberName: string, "
    + "selectorKey: string, metadataToken: number, styleOptionsJson: string): "
    + "Promise<BrowserMemberSource>;",
  ));
  assert.ok(sourceDeclarations.includes(
    "queryTypeMemberSource(packageId: string, version: string, "
    + "targetFramework: string, assemblyName: string, typeIdentity: string, "
    + "memberName: string, selectorKey: string, metadataToken: number, "
    + "styleOptionsJson: string): Promise<BrowserSource>;",
  ));
});
