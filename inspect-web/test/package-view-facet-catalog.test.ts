import assert from "node:assert/strict";
import test from "node:test";
import {
  packageLensDefinitions,
  setPackageViewFacetCatalog,
  type PackageViewFacetDescriptor,
} from "../src/data.ts";

const descriptors = [
  { id: "package.libraries", kind: "Package", title: "Libraries", order: 50 },
  { id: "package.overview", kind: "Package", title: "Summary", order: 100 },
  { id: "package.dependencies", kind: "Package", title: "Dependencies", order: 200 },
  {
    id: "package.dependency-hierarchy",
    kind: "Package",
    title: "Dependency Tree",
    order: 300,
  },
] as const satisfies readonly PackageViewFacetDescriptor[];

test("Package inspectors require the product catalog", () => {
  assert.throws(
    () => packageLensDefinitions(),
    /product Package view-facet catalog is unavailable/);
});

test("Package exact-match inspectors use product descriptors in Browser order", () => {
  setPackageViewFacetCatalog(descriptors);

  assert.deepEqual(packageLensDefinitions(), [
    ["overview", "Summary"],
    ["compare", "Compare"],
    ["dependencies", "References"],
    ["hierarchy", "Dependency Tree"],
    ["vulnerabilities", "Vulnerabilities"],
  ]);
});

test("Package inspector bindings reject required descriptor drift", () => {
  assert.throws(
    () => setPackageViewFacetCatalog(
      descriptors.filter(
        descriptor => descriptor.id !== "package.dependencies")),
    /Required Package view facet 'package\.dependencies' is missing/);
  assert.throws(
    () => setPackageViewFacetCatalog([
      ...descriptors,
      { ...descriptors[1], order: 400 },
    ]),
    /Package view facet 'package\.overview' is duplicated/);
  assert.throws(
    () => setPackageViewFacetCatalog([
      descriptors[0],
      { ...descriptors[1], title: " " },
      ...descriptors.slice(2),
    ]),
    /Package view facet 'package\.overview' has no title/);
  assert.throws(
    () => setPackageViewFacetCatalog([
      descriptors[1],
      descriptors[0],
      ...descriptors.slice(2),
    ]),
    /product-issued finite ascending order/);
});
