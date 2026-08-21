import assert from "node:assert/strict";
import test from "node:test";

import {
  buildWorkspaceStateUrl,
  createNavigationHistory,
  createNavigationSequence,
  createWorkspaceLocationPersistence,
  parseWorkspaceLocation,
  type WorkspaceLocationSnapshot,
  type WorkspaceUrlState,
} from "../src/workspace-navigation.ts";

interface TestView {
  id: string;
  revision: number;
}

function locationSnapshot(value: string | URL): WorkspaceLocationSnapshot {
  const url = new URL(value);
  return {
    href: url.href,
    pathname: url.pathname,
    search: url.search,
    hash: url.hash,
  };
}

function workspaceState(
  overrides: Partial<WorkspaceUrlState> = {},
): WorkspaceUrlState {
  return {
    package: "Example.Second",
    tabs: [
      { id: "Example.First", version: "1.0.0", framework: "net9.0" },
      { id: "Example.Second", version: "2.0.0", framework: "net10.0" },
    ],
    active: 1,
    lens: "api",
    atPackageRoot: false,
    packageLens: "overview",
    library: null,
    selectedTypeId: "Example.Widget",
    selectedMemberKey: "method:Build",
    selectedOverloadIndex: 2,
    memberSection: "facts",
    selectedBodyTarget: {
      memberName: "Build",
      selectorKey: "method",
      metadataToken: 42,
    },
    memberBrowse: true,
    memberTextFilter: "build",
    memberKindFilter: "method",
    memberAccessibilityFilter: "public",
    memberTraitFilter: "isStatic",
    ...overrides,
  };
}

test("navigation history skips stale views and truncates a forward branch", () => {
  let current: TestView | null = { id: "first", revision: 1 };
  const applied: TestView[] = [];
  let exhausted = 0;
  let history: ReturnType<typeof createNavigationHistory<TestView>>;
  history = createNavigationHistory({
    capture: () => current && { ...current },
    signature: view => view.id,
    apply: view => {
      applied.push(view);
      if (view.id === "stale") return false;
      current = { ...view };
      history.normalizeCurrent();
      return true;
    },
    onExhausted: () => exhausted++,
  });

  history.record();
  current = { id: "first", revision: 2 };
  history.record();
  current = { id: "stale", revision: 1 };
  history.record();
  current = { id: "latest", revision: 1 };
  history.record();

  assert.equal(history.canBack(), true);
  assert.equal(history.canForward(), false);
  assert.equal(history.back(), true);
  assert.deepEqual(applied, [
    { id: "stale", revision: 1 },
    { id: "first", revision: 2 },
  ]);
  assert.equal(history.canBack(), false);
  assert.equal(history.canForward(), true);

  current = { id: "branch", revision: 1 };
  history.record();
  assert.equal(history.canForward(), false);
  assert.equal(history.forward(), false);
  assert.equal(exhausted, 1);
});

test("navigation sequence has one monotonic cancellation authority", () => {
  const sequence = createNavigationSequence();
  assert.equal(sequence.current(), 0);
  const first = sequence.begin();
  assert.equal(sequence.isCurrent(first), true);
  const second = sequence.begin();
  assert.equal(sequence.isCurrent(first), false);
  assert.equal(sequence.isCurrent(second), true);
  sequence.invalidate();
  assert.equal(sequence.isCurrent(second), false);
  assert.equal(sequence.current(), 3);
});

test("rich workspace URLs round-trip coordinates, scope, and member selection", () => {
  const state = workspaceState({
    library: "System.Private.CoreLib",
  });
  const url = buildWorkspaceStateUrl(
    "https://inspect.example/packages/old?stale=1#metadata",
    state);

  assert.equal(url.pathname, "/");
  assert.equal(url.searchParams.get("package"), "Example.Second");
  assert.equal(url.hash, "");
  const parsed = parseWorkspaceLocation(locationSnapshot(url));
  assert.deepEqual(parsed.tabs, state.tabs);
  assert.equal(parsed.active, 1);
  assert.equal(parsed.package, "Example.Second");
  assert.equal(parsed.version, "2.0.0");
  assert.equal(parsed.framework, "net10.0");
  assert.equal(parsed.lens, null);
  assert.equal(parsed.library, "System.Private.CoreLib");
  assert.equal(parsed.type, "Example.Widget");
  assert.equal(parsed.member, "method:Build");
  assert.equal(parsed.overload, "2");
  assert.equal(parsed.section, "facts");
  assert.deepEqual(parsed.bodyTarget, state.selectedBodyTarget);
  assert.equal(parsed.memberBrowse, true);
  assert.equal(parsed.memberTextFilter, "build");
  assert.equal(parsed.memberKindFilter, "method");
  assert.equal(parsed.memberAccessibilityFilter, "public");
  assert.equal(parsed.memberTraitFilter, "isStatic");
  assert.equal(parsed.workspaceNotice, "");
});

test("package-root URLs omit stale type selection and retain their package lens", () => {
  const url = buildWorkspaceStateUrl(
    "https://inspect.example/",
    workspaceState({
      atPackageRoot: true,
      packageLens: "dependencies",
    }));
  const parsed = parseWorkspaceLocation(locationSnapshot(url));

  assert.equal(parsed.atPackageRoot, true);
  assert.equal(parsed.packageLens, "dependencies");
  assert.equal(parsed.type, null);
  assert.equal(parsed.member, null);
  assert.equal(parsed.overload, null);
  assert.equal(parsed.section, null);
  assert.equal(parsed.bodyTarget, null);
  assert.equal(parsed.memberBrowse, false);
  assert.equal(parsed.memberTextFilter, "");
  assert.equal(parsed.memberKindFilter, "all");
  assert.equal(parsed.memberAccessibilityFilter, "all");
  assert.equal(parsed.memberTraitFilter, "");
});

test("legacy workspace packets retain visible-location authority", () => {
  const packet = Buffer.from(JSON.stringify([
    ["Example.First", "1.0.0", "net9.0"],
    ["Example.Second", "2.0.0", "net10.0"],
  ])).toString("base64url");
  const parsed = parseWorkspaceLocation(locationSnapshot(
    `https://inspect.example/?package=Example.Second&version=9.9.9`
      + `&framework=net8.0&type=Visible.Type&w=${packet}#source`));

  assert.equal(parsed.package, "Example.Second");
  assert.equal(parsed.version, "9.9.9");
  assert.equal(parsed.framework, "net8.0");
  assert.equal(parsed.type, "Visible.Type");
  assert.equal(parsed.lens, "source");
  assert.equal(parsed.active, 1);
});

test("invalid and oversized workspace packets stay visible", () => {
  const invalid = parseWorkspaceLocation(locationSnapshot(
    "https://inspect.example/?package=Example.Package&w=not-base64"));
  assert.equal(invalid.package, "Example.Package");
  assert.match(invalid.workspaceNotice, /invalid and was ignored/);

  const oversized = parseWorkspaceLocation({
    href: "https://inspect.example/",
    pathname: "/",
    search: `?w=${"x".repeat(65537)}`,
    hash: "",
  });
  assert.match(oversized.workspaceNotice, /65536-character limit/);
});

test("location persistence contains history failures but leaves build failures visible", () => {
  const current = locationSnapshot("https://inspect.example/");
  const replaced: string[] = [];
  const pushed: string[] = [];
  const persistence = createWorkspaceLocationPersistence({
    current: () => current,
    replace: url => replaced.push(url),
    push: url => pushed.push(url),
  });

  persistence.sync(workspaceState());
  persistence.push("/");
  assert.equal(new URL(replaced[0]).searchParams.get("package"), "Example.Second");
  assert.deepEqual(pushed, ["/"]);

  const blocked = createWorkspaceLocationPersistence({
    current: () => current,
    replace: () => {
      throw new DOMException("blocked");
    },
    push: () => {
      throw new DOMException("blocked");
    },
  });
  assert.doesNotThrow(() => blocked.sync(workspaceState()));
  assert.doesNotThrow(() => blocked.push("/"));
  assert.throws(
    () => blocked.build(workspaceState({
      memberTextFilter: "x".repeat(65537),
    })),
    /65536-character limit/);
});
