import assert from "node:assert/strict";
import test from "node:test";

import {
  buildWorkspaceStateUrl,
  createNavigationHistory,
  createNavigationSequence,
  createWorkspaceLocationPersistence,
  parseWorkspaceLocation,
  workspaceViewSignature,
  type WorkspaceLocationSnapshot,
  type WorkspaceUrlState,
  type WorkspaceView,
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
    libraryPack: null,
    selectedTypeId: "Example.Widget",
    selectedMemberKey: "method:Build",
    selectedOverloadIndex: 2,
    memberSection: "facts",
    selectedBodyTarget: {
      memberName: "Build",
      selectorKey: "method",
      metadataToken: 42,
    },
    graphTarget: null,
    memberBrowse: true,
    memberTextFilter: "build",
    memberKindFilter: "method",
    memberAccessibilityFilter: "public",
    memberTraitFilter: "isStatic",
    ...overrides,
  };
}

function sharePacket(url: URL): Record<string, unknown> {
  const encoded = url.searchParams.get("w");
  assert.ok(encoded);
  const value: unknown = JSON.parse(
    Buffer.from(encoded, "base64url").toString("utf8"));
  assert.ok(value && typeof value === "object" && !Array.isArray(value));
  return Object.fromEntries(Object.entries(value));
}

function workspaceView(
  overrides: Partial<WorkspaceView> = {},
): WorkspaceView {
  const graphTarget = {
    assembly: "Example.Second",
    assemblyVersion: "2.0.0.0",
    assemblyCulture: null,
    assemblyPublicKeyToken: null,
    typeDefinitionId: "T:Example.Widget",
    typeMetadataId: "Example.Widget",
    memberName: "Build",
    selectorKey: "Build|System.String",
    metadataToken: 0x0600002a,
  };
  return {
    package: "Example.Second",
    packageKey: "example.second@2.0.0/net10.0",
    lens: "api",
    selectedTypeId: "Example.Widget",
    selectedMemberKey: "graph:method:Build",
    memberBrowseTypeId: "Example.Widget",
    memberKindFilter: "all",
    memberAccessibilityFilter: "all",
    memberTraitFilter: "",
    memberTextFilter: "",
    selectedOverloadIndex: 0,
    bodyTarget: graphTarget,
    memberSection: "overview",
    atPackageRoot: false,
    packageLens: "overview",
    libraryScope: null,
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

  applied.length = 0;
  assert.equal(history.forward(), true);
  assert.deepEqual(applied, [{ id: "latest", revision: 1 }]);
  assert.equal(history.back(), true);

  applied.length = 0;
  current = { id: "branch", revision: 1 };
  history.record();
  assert.equal(history.back(), true);
  assert.deepEqual(applied, [{ id: "first", revision: 2 }]);
  assert.equal(history.forward(), true);
  assert.deepEqual(applied.at(-1), { id: "branch", revision: 1 });
  assert.equal(history.canForward(), false);
  assert.equal(exhausted, 0);
});

test("navigation history removes stale entries and reports exhausted directions", () => {
  let current: TestView | null = { id: "first", revision: 1 };
  const unavailable = new Set<string>();
  const applied: TestView[] = [];
  let exhausted = 0;
  const history = createNavigationHistory({
    capture: () => current && { ...current },
    signature: view => view.id,
    apply: view => {
      applied.push(view);
      if (unavailable.has(view.id)) return false;
      current = { ...view };
      return true;
    },
    onExhausted: () => exhausted++,
  });

  history.record();
  current = { id: "middle", revision: 1 };
  history.record();
  current = { id: "latest", revision: 1 };
  history.record();
  assert.equal(history.back(), true);
  assert.equal(history.back(), true);

  unavailable.add("middle");
  applied.length = 0;
  assert.equal(history.forward(), true);
  assert.deepEqual(applied, [
    { id: "middle", revision: 1 },
    { id: "latest", revision: 1 },
  ]);
  assert.equal(history.canForward(), false);

  unavailable.delete("middle");
  applied.length = 0;
  assert.equal(history.back(), true);
  assert.deepEqual(applied, [{ id: "first", revision: 1 }]);
  assert.equal(history.back(), false);
  assert.equal(exhausted, 1);

  unavailable.add("latest");
  applied.length = 0;
  assert.equal(history.forward(), false);
  assert.deepEqual(applied, [{ id: "latest", revision: 1 }]);
  assert.equal(history.canForward(), false);
  assert.equal(exhausted, 2);
  applied.length = 0;
  assert.equal(history.canBack(), false);
  assert.equal(history.back(), false);
  assert.deepEqual(applied, []);
  assert.equal(exhausted, 3);
});

test("navigation history normalizes the current captured view", () => {
  let current: TestView | null = { id: "first", revision: 1 };
  const applied: TestView[] = [];
  const history = createNavigationHistory({
    capture: () => current && { ...current },
    signature: view => `${view.id}:${view.revision}`,
    apply: view => {
      applied.push(view);
      current = { ...view };
      return true;
    },
    onExhausted() {},
  });

  history.record();
  current = { id: "latest", revision: 1 };
  history.record();
  assert.equal(history.back(), true);
  current = { id: "first", revision: 2 };
  history.normalizeCurrent();
  history.record();

  assert.equal(history.canForward(), true);
  assert.equal(history.forward(), true);
  assert.equal(history.back(), true);
  assert.deepEqual(applied, [
    { id: "first", revision: 1 },
    { id: "latest", revision: 1 },
    { id: "first", revision: 2 },
  ]);
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
    libraryPack: "netcore.app",
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
  assert.equal(parsed.libraryPack, "netcore.app");
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

test("graph member URLs retain exact identity instead of a lossy body target", () => {
  const graphTarget = {
    assembly: "Example.Second",
    assemblyVersion: "2.0.0.0",
    assemblyCulture: null,
    assemblyPublicKeyToken: "0011223344556677",
    typeDefinitionId: "T:Example.Widget",
    typeMetadataId: "Example.Widget",
    memberName: "Build",
    selectorKey: "Build|System.String",
    metadataToken: 0x0600002a,
  };
  const state = workspaceState({
    selectedBodyTarget: graphTarget,
    graphTarget,
  });
  const url = buildWorkspaceStateUrl("https://inspect.example/", state);
  const packet = sharePacket(url);
  const parsed = parseWorkspaceLocation(locationSnapshot(url));

  assert.equal(Object.hasOwn(packet, "g"), true);
  assert.equal(Object.hasOwn(packet, "d"), false);
  assert.deepEqual(parsed.graphTarget, graphTarget);
  assert.equal(parsed.bodyTarget, null);
  assert.equal(parsed.type, state.selectedTypeId);
  assert.equal(parsed.member, state.selectedMemberKey);
  assert.equal(parsed.overload, String(state.selectedOverloadIndex));
});

test("history signatures distinguish exact graph member identity", () => {
  const original = workspaceView();
  const originalTarget = {
    assembly: "Example.Second",
    assemblyVersion: "2.0.0.0",
    assemblyCulture: null,
    assemblyPublicKeyToken: null,
    typeDefinitionId: "T:Example.Widget",
    typeMetadataId: "Example.Widget",
    memberName: "Build",
    selectorKey: "Build|System.String",
    metadataToken: 0x0600002a,
  };
  const variants = [
    { ...originalTarget, memberName: "BuildAsync" },
    { ...originalTarget, selectorKey: "Build|System.Int32" },
    { ...originalTarget, metadataToken: 0x0600002b },
  ];

  for (const bodyTarget of variants) {
    assert.notEqual(
      workspaceViewSignature(original),
      workspaceViewSignature(workspaceView({ bodyTarget })));
  }
});

test("history signatures distinguish captured library scope", () => {
  const original = workspaceView({
    libraryScope: ["System.Collections", "System.Runtime"],
  });

  assert.notEqual(
    workspaceViewSignature(original),
    workspaceViewSignature(workspaceView({
      libraryScope: ["System.Text.Json"],
    })));
});

test("package-root URLs omit stale type selection and retain their package lens", () => {
  const url = buildWorkspaceStateUrl(
    "https://inspect.example/",
    workspaceState({
      atPackageRoot: true,
      packageLens: "dependencies",
      graphTarget: {
        assembly: "Example.Second",
        assemblyVersion: "2.0.0.0",
        assemblyCulture: null,
        assemblyPublicKeyToken: null,
        typeDefinitionId: "T:Example.Widget",
        typeMetadataId: "Example.Widget",
        memberName: "Build",
        selectorKey: "Build|",
        metadataToken: 0x0600002a,
      },
    }));
  const packet = sharePacket(url);
  const parsed = parseWorkspaceLocation(locationSnapshot(url));

  assert.equal(parsed.atPackageRoot, true);
  assert.equal(parsed.packageLens, "dependencies");
  assert.equal(parsed.type, null);
  assert.equal(parsed.member, null);
  assert.equal(parsed.overload, null);
  assert.equal(parsed.section, null);
  assert.equal(parsed.bodyTarget, null);
  assert.equal(parsed.graphTarget, null);
  assert.equal(Object.hasOwn(packet, "g"), false);
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

  const structurallyInvalid = parseWorkspaceLocation(locationSnapshot(
    "https://inspect.example/?package=Example.Package&w=e30"));
  assert.equal(structurallyInvalid.package, "Example.Package");
  assert.match(structurallyInvalid.workspaceNotice, /invalid and was ignored/);

  const oversized = parseWorkspaceLocation({
    href: "https://inspect.example/",
    pathname: "/",
    search: `?w=${"x".repeat(65537)}`,
    hash: "",
  });
  assert.match(oversized.workspaceNotice, /65536-character limit/);
});

test("malformed rich packet fields cannot override the visible package", () => {
  const base = {
    t: [["Hidden.Package", "1.0.0", "net10.0"]],
    a: 0,
  };
  const invalidPackets: Record<string, unknown>[] = [
    { ...base, a: undefined },
    { ...base, a: "0" },
    { ...base, a: 0.5 },
    { ...base, a: -1 },
    { ...base, a: 1 },
    ...["l", "v", "y", "m", "c", "q", "k", "e", "r"]
      .map(key => ({ ...base, [key]: 1 })),
    ...[null, "", "not-a-platform-pack", 1]
      .map(p => ({ ...base, p })),
    ...[null, "0", -1, 0.5]
      .map(o => ({ ...base, o })),
    ...[null, "body", [], [null, null, null], ["Build", null, 0.5]]
      .map(d => ({ ...base, d })),
    ...[null, 0, true, "1"]
      .map(b => ({ ...base, b })),
  ];
  const missingActive = invalidPackets[0];
  assert.ok(missingActive);
  delete missingActive.a;

  for (const packet of invalidPackets) {
    const encoded = Buffer.from(JSON.stringify(packet)).toString("base64url");
    const parsed = parseWorkspaceLocation(locationSnapshot(
      `https://inspect.example/?package=Visible.Package&w=${encoded}`));
    assert.equal(parsed.package, "Visible.Package");
    assert.deepEqual(parsed.tabs, []);
    assert.equal(
      parsed.workspaceNotice,
      "The shared workspace state is invalid and was ignored.");
  }
});

test("invalid graph identities reject the rich packet without hiding the visible package", () => {
  const validGraph = [
    "Example.Second",
    "2.0.0.0",
    null,
    null,
    "T:Example.Widget",
    "Example.Widget",
    "Build",
    "Build|",
    0x0600002a,
  ];
  const packets = [
    {
      t: [["Hidden.Package", "1.0.0", "net10.0"]],
      a: 0,
      y: "Example.Widget",
      m: "method:Build",
      g: [...validGraph.slice(0, 8), "not-a-token"],
    },
    {
      t: [["Hidden.Package", "1.0.0", "net10.0"]],
      a: 0,
      y: "Example.Widget",
      m: "method:Build",
      o: 0,
      g: [validGraph[0], "", ...validGraph.slice(2)],
    },
    ...[
      -1,
      0,
      0x02000001,
      0x06000000,
      0x07000000,
      0x106000001,
    ].map(metadataToken => ({
      t: [["Hidden.Package", "1.0.0", "net10.0"]],
      a: 0,
      y: "Example.Widget",
      m: "method:Build",
      o: 0,
      g: [...validGraph.slice(0, 8), metadataToken],
    })),
    {
      t: [["Hidden.Package", "1.0.0", "net10.0"]],
      a: 0,
      y: "Example.Widget",
      m: "method:Build",
      g: validGraph,
    },
    {
      t: [["Hidden.Package", "1.0.0", "net10.0"]],
      a: 0,
      y: "Example.Widget",
      m: "method:Build",
      o: "0",
      g: validGraph,
    },
  ];

  for (const packet of packets) {
    const encoded = Buffer.from(JSON.stringify(packet)).toString("base64url");
    const parsed = parseWorkspaceLocation(locationSnapshot(
      `https://inspect.example/?package=Visible.Package&w=${encoded}`));
    assert.equal(parsed.package, "Visible.Package");
    assert.deepEqual(parsed.tabs, []);
    assert.equal(parsed.graphTarget, null);
    assert.equal(
      parsed.workspaceNotice,
      "The shared graph member target is invalid and was ignored.");
  }
});

test("location persistence contains sync failures but leaves direct build failures visible", () => {
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
  const replacedUrl = replaced[0];
  assert.ok(replacedUrl);
  assert.equal(new URL(replacedUrl).searchParams.get("package"), "Example.Second");
  assert.deepEqual(pushed, ["/"]);
  const replacedCount = replaced.length;
  assert.doesNotThrow(() => persistence.sync(workspaceState({
    memberTextFilter: "x".repeat(65537),
  })));
  assert.equal(replaced.length, replacedCount);

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
