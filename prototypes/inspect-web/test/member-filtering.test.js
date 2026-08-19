import assert from "node:assert/strict";
import test from "node:test";
import {
  filterMemberGroups,
  memberGroupMatches,
  memberNavTargetIndex,
  memberScopeIsActive,
} from "../src/member-filtering.js";

const groups = [
  {
    key: "method:Build",
    name: "Build",
    kind: "method",
    overloads: [
      {
        signature: "public static Task Build(string path)",
        accessibility: "public",
        isStatic: true,
        isObsolete: false,
      },
      {
        signature: "protected void Build()",
        accessibility: "protected",
        isStatic: false,
        isObsolete: true,
      },
    ],
  },
  {
    key: "property:Name",
    name: "Name",
    kind: "property",
    overloads: [
      {
        signature: "public string Name { get; }",
        accessibility: "public",
        isStatic: false,
        isObsolete: false,
      },
    ],
  },
];

test("member filters compose on one matching overload", () => {
  assert.equal(memberGroupMatches(groups[0], {
    kind: "method",
    accessibility: "public",
    trait: "isStatic",
    query: "path",
  }), true);

  assert.equal(memberGroupMatches(groups[0], {
    kind: "method",
    accessibility: "protected",
    trait: "isStatic",
    query: "",
  }), false);
});

test("member search covers names and signatures", () => {
  assert.deepEqual(
    filterMemberGroups(groups, {
      kind: "all",
      accessibility: "all",
      trait: "",
      query: "string name",
    }).map(group => group.key),
    ["property:Name"],
  );

  assert.deepEqual(
    filterMemberGroups(groups, {
      kind: "all",
      accessibility: "all",
      trait: "",
      query: "build",
    }).map(group => group.key),
    ["method:Build"],
  );
});

test("member scope follows the resolved type identity", () => {
  assert.equal(memberScopeIsActive({
    atPackageRoot: false,
    lens: "api",
    selectedMemberKey: "",
    memberBrowseTypeId: "Type0",
    selectedTypeId: null,
  }, "Type0"), true);
});

test("member navigation enters the nearest edge from no selection", () => {
  assert.equal(memberNavTargetIndex(-1, 3, 1), 0);
  assert.equal(memberNavTargetIndex(-1, 3, -1), 2);
  assert.equal(memberNavTargetIndex(0, 3, 1), 1);
  assert.equal(memberNavTargetIndex(2, 3, 1), 2);
});
