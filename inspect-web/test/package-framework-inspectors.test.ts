import assert from "node:assert/strict";
import test from "node:test";
import {
  isEmptyPackageFramework,
  packageLensesFor,
  setPackageViewFacetCatalog,
} from "../src/data.ts";

setPackageViewFacetCatalog([
  { id: "package.libraries", kind: "Package", title: "Libraries", order: 50 },
  { id: "package.overview", kind: "Package", title: "Overview", order: 100 },
  { id: "package.dependencies", kind: "Package", title: "Dependencies", order: 200 },
  {
    id: "package.dependency-hierarchy",
    kind: "Package",
    title: "Dependency Hierarchy",
    order: 300,
  },
]);

const packageContext = (status: string) => ({
  source: { kind: "nuget.org" },
  packageChildren: { content: { status } },
});

test("explicitly empty framework exposes only Package Overview", () => {
  const pkg = packageContext("SelectedEmpty");
  assert.equal(isEmptyPackageFramework(pkg), true);
  assert.deepEqual(packageLensesFor(pkg), [["overview", "Overview"]]);
});

test("other package outcomes retain their inspectors and failure visibility", () => {
  for (const status of ["Available", "NoCompileAssets", "NoApplicableTarget", "Unavailable"]) {
    const pkg = packageContext(status);
    assert.equal(isEmptyPackageFramework(pkg), false);
    assert.deepEqual(packageLensesFor(pkg).map(([id]) => id),
      ["overview", "compare", "dependencies", "vulnerabilities"]);
  }
});
