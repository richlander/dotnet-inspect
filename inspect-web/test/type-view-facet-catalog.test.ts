import assert from "node:assert/strict";
import test from "node:test";
import {
  canonicalTypeLens,
  setTypeViewFacetCatalog,
  typeLensesFor,
  typeRelationshipsOfferedFor,
} from "../src/data.ts";

const descriptors = [
  { id: "type.overview", kind: "Type", title: "Overview", order: 100 },
  { id: "type.api", kind: "Type", title: "Members", order: 200 },
  { id: "type.analysis", kind: "Type", title: "Analysis", order: 250 },
  { id: "type.metadata", kind: "Type", title: "Metadata", order: 300 },
  { id: "type.source", kind: "Type", title: "Source", order: 400 },
  { id: "type.compare", kind: "Type", title: "Compare", order: 500 },
] as const;

test("Type inspectors hide the legacy Metadata alias", () => {
  setTypeViewFacetCatalog(descriptors);

  assert.deepEqual(typeLensesFor({ source: { kind: "nuget.org" } }), [
    ["overview", "Overview"],
    ["api", "Members"],
    ["analysis", "Analysis"],
    ["source", "Source"],
    ["compare", "Compare"],
  ]);
  // Platform Types keep Analysis for Unsafe; file sources don't offer it.
  assert.deepEqual(typeLensesFor({ isRuntimePack: true }).map(([id]) => id),
    ["overview", "api", "analysis", "source"]);
  assert.deepEqual(typeLensesFor({ source: { kind: "file" } }).map(([id]) => id),
    ["overview", "api"]);
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

// Relationships is offered only for package-backed Types; a Platform Type's
// Analysis offers Unsafe alone.
test("Type Relationships is offered only for package-backed Types", () => {
  assert.equal(typeRelationshipsOfferedFor({ source: { kind: "nuget.org" } }), true);
  assert.equal(typeRelationshipsOfferedFor({ source: { kind: "feed" } }), true);
  assert.equal(typeRelationshipsOfferedFor({ isRuntimePack: true, source: { kind: "nuget.org" } }), false);
  assert.equal(typeRelationshipsOfferedFor({ source: { kind: "file" } }), false);
  assert.equal(typeRelationshipsOfferedFor(null), false);
});
