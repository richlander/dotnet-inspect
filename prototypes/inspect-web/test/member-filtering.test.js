import assert from "node:assert/strict";
import test from "node:test";
import {
  filterMemberGroups,
  memberGroupMatches,
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
        isAsync: true,
        isObsolete: false,
      },
      {
        signature: "protected void Build()",
        accessibility: "protected",
        isStatic: false,
        isAsync: false,
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
        isAsync: false,
        isObsolete: false,
      },
    ],
  },
];

test("member filters compose on one matching overload", () => {
  assert.equal(memberGroupMatches(groups[0], {
    kind: "method",
    accessibility: "public",
    trait: "isAsync",
    query: "path",
  }), true);

  assert.equal(memberGroupMatches(groups[0], {
    kind: "method",
    accessibility: "protected",
    trait: "isAsync",
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
