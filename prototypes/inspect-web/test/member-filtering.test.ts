import assert from "node:assert/strict";
import test from "node:test";
import {
  bodyTargetMatchesOverload,
  captureLibraryScope,
  decodeBodyTarget,
  encodeBodyTarget,
  filterMemberGroups,
  invalidateMemberCallGraphWork,
  invalidateSourceDestinationWork,
  memberGroupMatches,
  memberNavTargetIndex,
  memberScopeIsActive,
  restoreLibraryScope,
  restoreMemberHistoryState,
  selectedConcreteOverload,
} from "../src/member-filtering.ts";

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

test("body targets round-trip through the compact rich-packet tuple", () => {
  const target = {
    memberName: "get_Value",
    selectorKey: "getter",
    metadataToken: 11,
  };
  assert.deepEqual(decodeBodyTarget(encodeBodyTarget(target)), target);
  assert.equal(encodeBodyTarget({
    memberName: null,
    selectorKey: null,
    metadataToken: null,
  }), null);
  assert.deepEqual(
    decodeBodyTarget(["get_Value", "getter", null]),
    { memberName: "get_Value", selectorKey: "getter", metadataToken: null });
  assert.equal(decodeBodyTarget([null, null, null]), null);
  assert.equal(decodeBodyTarget(["get_Value", "getter", "11"]), null);
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
    memberSection: "source" as const,
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
  const methodGroup = groups[0];
  assert.ok(methodGroup);
  assert.equal(memberGroupMatches(methodGroup, {
    kind: "method",
    accessibility: "public",
    trait: "isStatic",
    query: "path",
  }), true);

  assert.equal(memberGroupMatches(methodGroup, {
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
    lens: "api" as const,
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

test("a multi-overload picker has no concrete overload", () => {
  const overloads = ["first", "second"];
  assert.equal(selectedConcreteOverload(overloads, null), undefined);
  assert.equal(selectedConcreteOverload(overloads, 0), "first");
  assert.equal(selectedConcreteOverload(["only"], null), "only");
});

test("Call graph invalidation releases every asynchronous owner", () => {
  const state = {
    memberCallGraphSeq: 4,
    memberCallGraphLoading: false,
    memberCallGraphExpanding: true,
    memberCallGraphKey: "package|type|member",
    platformDrillLoading: true,
    platformDrillError: "old failure",
  };

  invalidateMemberCallGraphWork(state);

  assert.deepEqual(state, {
    memberCallGraphSeq: 5,
    memberCallGraphLoading: false,
    memberCallGraphExpanding: false,
    memberCallGraphKey: "",
    platformDrillLoading: false,
    platformDrillError: "",
  });
});

test("source invalidation supersedes package and platform destinations", () => {
  const state = {
    memberCallGraphSeq: 4,
    memberCallGraphLoading: false,
    memberCallGraphExpanding: false,
    memberCallGraphKey: "package|type|member",
    platformDrillLoading: true,
    platformDrillError: "old platform failure",
    graphMemberNavigationSeq: 7,
    graphMemberNavigationTitle: "Target.Run",
    graphMemberNavigationError: "old package failure",
    pendingGraphMemberDeepLink: null,
  };

  invalidateSourceDestinationWork(state);

  assert.equal(state.memberCallGraphSeq, 5);
  assert.equal(state.graphMemberNavigationSeq, 8);
  assert.equal(state.platformDrillLoading, false);
  assert.equal(state.graphMemberNavigationTitle, "");
  assert.equal(state.graphMemberNavigationError, "");
  assert.equal(state.pendingGraphMemberDeepLink, null);
});

test("source invalidation lets an active history restoration finish", () => {
  const pending = { member: "Run" };
  const state = {
    memberCallGraphSeq: 4,
    memberCallGraphLoading: false,
    memberCallGraphExpanding: false,
    memberCallGraphKey: "",
    platformDrillLoading: true,
    platformDrillError: "",
    graphMemberNavigationSeq: 7,
    graphMemberNavigationTitle: "Target.Run",
    graphMemberNavigationError: "",
    pendingGraphMemberDeepLink: pending,
  };

  invalidateSourceDestinationWork(state);

  assert.equal(state.memberCallGraphSeq, 5);
  assert.equal(state.graphMemberNavigationSeq, 7);
  assert.equal(state.pendingGraphMemberDeepLink, pending);
});

test("library scope round-trips only within the restored package", () => {
  const captured = captureLibraryScope(new Set(["System.Runtime", "System.Console"]));
  assert.deepEqual(captured, ["System.Console", "System.Runtime"]);
  assert.deepEqual(
    [...restoreLibraryScope(captured, [
      "System.Console",
      "System.Private.CoreLib",
      "System.Runtime",
    ])!],
    captured);
  assert.equal(
    restoreLibraryScope(["System.Private.CoreLib"], ["Newtonsoft.Json"]),
    null);
  assert.equal(restoreLibraryScope(["Newtonsoft.Json"], ["Newtonsoft.Json"]), null);
});
