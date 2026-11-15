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

test("packages without compile assets use Contents while failures retain inspectors", () => {
  const noCompileAssets = packageContext("NoCompileAssets");
  assert.deepEqual(packageLensesFor(noCompileAssets).map(([id]) => id),
    ["overview", "contents", "dependencies", "compare", "vulnerabilities"]);

  for (const status of ["Available", "NoApplicableTarget", "Unavailable"]) {
    const pkg = packageContext(status);
    assert.equal(isEmptyPackageFramework(pkg), false);
    assert.deepEqual(packageLensesFor(pkg).map(([id]) => id),
      ["overview", "contents", "dependencies", "compare", "vulnerabilities"]);
  }

  for (const compileLibraryStatus of [
    "NoCompileAssets",
    "EmptyCompileGroup",
    "NoMatchingTargetFramework",
  ]) {
    assert.deepEqual(packageLensesFor({
      source: { kind: "nuget.org" },
      compileLibraryStatus,
    }), [
      ["contents", "Package Contents"],
      ["dependencies", "References"],
    ]);
  }
});

test("the Platform surface keeps its Overview without archive Contents", () => {
  assert.deepEqual(packageLensesFor({
    isRuntimePack: true,
    source: { kind: "platform" },
  }), [["overview", "Overview"]]);
});

test("non-NuGet package sources do not offer remote Package Contents", () => {
  assert.deepEqual(packageLensesFor({
    source: { kind: "file" },
    packageChildren: { content: { status: "Available" } },
  }).map(([id]) => id), ["overview", "dependencies", "compare"]);
});
