import assert from "node:assert/strict";
import test from "node:test";
import {
  memberSectionDefinitions,
  setMemberViewFacetCatalog,
  type MemberViewFacetDescriptor,
} from "../src/data.ts";

const descriptors = [
  { id: "member.overview", kind: "Member", title: "Summary", order: 100 },
  { id: "member.call-graph", kind: "Member", title: "Calls", order: 200 },
  { id: "member.facts", kind: "Member", title: "Facts", order: 300 },
  { id: "member.source", kind: "Member", title: "Code", order: 400 },
  {
    id: "member.annotated-source",
    kind: "Member",
    title: "Annotated code",
    order: 500,
  },
  { id: "member.compare", kind: "Member", title: "Compare builds", order: 600 },
] as const satisfies readonly MemberViewFacetDescriptor[];

test("Member inspectors require the product catalog", () => {
  assert.throws(
    () => memberSectionDefinitions(),
    /product Member view-facet catalog is unavailable/);
});

test("Member exact-match inspectors use product titles in Browser order", () => {
  setMemberViewFacetCatalog(descriptors);

  assert.deepEqual(memberSectionDefinitions(), [
    ["overview", "Summary"],
    ["call-graph", "Calls"],
    ["facts", "Facts"],
    ["resource-triage", "Resource Triage"],
    ["analysis", "Analysis"],
    ["source", "Code"],
    ["compare", "Compare builds"],
  ]);
});

test("Member inspector bindings reject required descriptor drift", () => {
  assert.throws(
    () => setMemberViewFacetCatalog(
      descriptors.filter(descriptor => descriptor.id !== "member.compare")),
    /Required Member view facet 'member\.compare' is missing/);
  assert.throws(
    () => setMemberViewFacetCatalog([
      ...descriptors,
      { ...descriptors[1], order: 700 },
    ]),
    /Member view facet 'member\.call-graph' is duplicated/);
  assert.throws(
    () => setMemberViewFacetCatalog([
      ...descriptors.slice(0, 2),
      { ...descriptors[2], title: " " },
      ...descriptors.slice(3),
    ]),
    /Member view facet 'member\.facts' has no title/);
  assert.throws(
    () => setMemberViewFacetCatalog([
      descriptors[1],
      descriptors[0],
      ...descriptors.slice(2),
    ]),
    /product-issued finite ascending order/);
});
