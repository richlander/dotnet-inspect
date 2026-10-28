import assert from "node:assert/strict";
import test from "node:test";
import {
  canonicalTypeLens,
  setTypeViewFacetCatalog,
  typeLensesFor,
} from "../src/data.ts";

const descriptors = [
  { id: "type.overview", kind: "Type", title: "Overview", order: 100 },
  { id: "type.api", kind: "Type", title: "Members", order: 200 },
  { id: "type.metadata", kind: "Type", title: "Metadata", order: 300 },
  { id: "type.source", kind: "Type", title: "Source", order: 400 },
  { id: "type.compare", kind: "Type", title: "Compare", order: 500 },
] as const;

test("Type inspectors hide the legacy Metadata alias", () => {
  setTypeViewFacetCatalog(descriptors);

  assert.deepEqual(typeLensesFor({ source: { kind: "nuget.org" } }), [
    ["overview", "Overview"],
    ["api", "Members"],
    ["source", "Source"],
    ["compare", "Compare"],
  ]);
  assert.equal(canonicalTypeLens("metadata"), "overview");
  assert.equal(canonicalTypeLens("source"), "source");
});

test("Type inspector bindings reject product drift instead of hiding it", () => {
  assert.throws(
    () => setTypeViewFacetCatalog(descriptors.slice(0, -1)),
    /Required Type view facet 'type\.compare' is missing/);
  assert.throws(
    () => setTypeViewFacetCatalog([
      ...descriptors,
      { id: "type.future", kind: "Type", title: "Future", order: 500 },
    ]),
    /Type view facet 'type\.future' has no Browser renderer binding/);
  assert.throws(
    () => setTypeViewFacetCatalog([
      descriptors[1],
      descriptors[0],
      descriptors[2],
      descriptors[3],
    ]),
    /product-issued finite ascending order/);
});
