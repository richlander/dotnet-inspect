import assert from "node:assert/strict";
import test from "node:test";
import {
  libraryLensDefinitions,
  setLibraryViewFacetCatalog,
  type LibraryViewFacetDescriptor,
} from "../src/data.ts";

const descriptors = [
  { id: "library.types", kind: "Library", title: "Types", order: 100 },
  { id: "library.info", kind: "Library", title: "Info", order: 200 },
  { id: "library.references", kind: "Library", title: "Uses", order: 300 },
  {
    id: "library.ecosystem-dependencies",
    kind: "Library",
    title: "Ecosystem dependencies",
    order: 400,
  },
  {
    id: "library.reference-hierarchy",
    kind: "Library",
    title: "Reference hierarchy",
    order: 500,
  },
  {
    id: "library.integrations",
    kind: "Library",
    title: "Integrations",
    order: 600,
  },
  { id: "library.analysis", kind: "Library", title: "Analysis facts", order: 700 },
  { id: "library.metadata", kind: "Library", title: "Metadata facts", order: 800 },
  { id: "library.compare", kind: "Library", title: "Compare builds", order: 900 },
] as const satisfies readonly LibraryViewFacetDescriptor[];

test("Library inspectors require the product catalog", () => {
  assert.throws(
    () => libraryLensDefinitions(),
    /product Library view-facet catalog is unavailable/);
});

test("Library exact-match inspectors use product titles in Browser order", () => {
  setLibraryViewFacetCatalog(descriptors);

  assert.deepEqual(libraryLensDefinitions(), [
    ["overview", "Overview"],
    ["references", "Uses"],
    ["compare", "Compare builds"],
    ["analysis", "Analysis"],
    ["metadata", "Metadata facts"],
  ]);
});

test("Library inspector bindings reject required descriptor drift", () => {
  assert.throws(
    () => setLibraryViewFacetCatalog(
      descriptors.filter(descriptor => descriptor.id !== "library.compare")),
    /Required Library view facet 'library\.compare' is missing/);
  assert.throws(
    () => setLibraryViewFacetCatalog([
      ...descriptors,
      { ...descriptors[2], order: 950 },
    ]),
    /Library view facet 'library\.references' is duplicated/);
  assert.throws(
    () => setLibraryViewFacetCatalog([
      ...descriptors.slice(0, 7),
      { ...descriptors[7], title: " " },
      descriptors[8],
    ]),
    /Library view facet 'library\.metadata' has no title/);
  assert.throws(
    () => setLibraryViewFacetCatalog([
      descriptors[0],
      descriptors[2],
      descriptors[1],
      ...descriptors.slice(3),
    ]),
    /product-issued finite ascending order/);
});
