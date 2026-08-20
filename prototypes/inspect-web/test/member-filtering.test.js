import assert from "node:assert/strict";
import test from "node:test";
import {
  bodyTargetMatchesOverload,
  filterMemberGroups,
  memberGroupMatches,
  memberNavTargetIndex,
  memberScopeIsActive,
  restoreMemberHistoryState,
} from "../src/member-filtering.js";

test("body targets must identify the selected overload or one of its accessor bodies", () => {
  const member = { name: "Value" };
  const overload = {
    metadataToken: 10,
    graphSelectorKey: "property",
    bodySelectors: [
      { token: 11, memberName: "get_Value", selectorKey: "getter" },
      { token: 12, memberName: "set_Value", selectorKey: "setter" },
    ],
  };

  assert.equal(
    bodyTargetMatchesOverload(
      { metadataToken: 10, memberName: "Value", selectorKey: "property" },
      member,
      overload),
    true);
  assert.equal(
    bodyTargetMatchesOverload(
      { metadataToken: null, memberName: "get_Value", selectorKey: "getter" },
      member,
      overload),
    true);
  assert.equal(
    bodyTargetMatchesOverload(
      { metadataToken: 99, memberName: "get_Value", selectorKey: "getter" },
      member,
      overload),
    false);
  assert.equal(bodyTargetMatchesOverload({}, member, overload), false);
});

test("history restores type filters independently of Member browse scope", () => {
  const type = { id: "Example.Widget" };
  const restored = restoreMemberHistoryState({
    memberBrowseTypeId: "",
    selectedMemberKey: "",
    memberKindFilter: "method",
    memberAccessibilityFilter: "protected",
    memberTraitFilter: "isStatic",
    memberTextFilter: "build",
  }, type, null);

  assert.equal(restored.memberBrowseTypeId, "");
  assert.equal(restored.selectedMemberKey, "");
  assert.equal(restored.memberKindFilter, "method");
  assert.equal(restored.memberAccessibilityFilter, "protected");
  assert.equal(restored.memberTraitFilter, "isStatic");
  assert.equal(restored.memberTextFilter, "build");
});

test("history rejects a missing member and stale overload body", () => {
  const type = { id: "Example.Widget" };
  const member = {
    key: "method:Build",
    name: "Build",
    overloads: [{
      metadataToken: 10,
      graphSelectorKey: "build",
      bodySelectors: [],
    }],
  };
  const baseView = {
    memberBrowseTypeId: type.id,
    selectedMemberKey: member.key,
    memberKindFilter: "all",
    memberAccessibilityFilter: "all",
    memberTraitFilter: "",
    memberTextFilter: "",
    selectedOverloadIndex: 4,
    memberSection: "source",
    bodyTarget: {
      metadataToken: 99,
      memberName: "Build",
      selectorKey: "build",
    },
  };

  assert.deepEqual(
    restoreMemberHistoryState(baseView, type, member, ["overview", "source"]),
    {
      selectedMemberKey: member.key,
      memberBrowseTypeId: type.id,
      memberKindFilter: "all",
      memberAccessibilityFilter: "all",
      memberTraitFilter: "",
      memberTextFilter: "",
      selectedOverloadIndex: null,
      memberSection: "overview",
      selectedBodyTarget: null,
    });
  assert.equal(
    restoreMemberHistoryState(baseView, type, null, []).memberBrowseTypeId,
    "");
});

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
  const state = {
    atPackageRoot: false,
    lens: "api",
    selectedMemberKey: "",
    memberBrowseTypeId: "Type0",
    selectedTypeId: null,
  };
  assert.equal(memberScopeIsActive(state, "Type0"), true);
  assert.equal(memberScopeIsActive(state, "Type1"), false);
  assert.equal(memberScopeIsActive({ ...state, atPackageRoot: true }, "Type0"), false);
  assert.equal(memberScopeIsActive({ ...state, lens: "metadata" }, "Type0"), false);
});

test("member navigation enters the nearest edge from no selection", () => {
  assert.equal(memberNavTargetIndex(-1, 3, 1), 0);
  assert.equal(memberNavTargetIndex(-1, 3, -1), 2);
  assert.equal(memberNavTargetIndex(0, 3, 1), 1);
  assert.equal(memberNavTargetIndex(2, 3, 1), 2);
});
