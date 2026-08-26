import assert from "node:assert/strict";
import test from "node:test";

import {
  buildWorkspaceStateUrl,
  createNavigationHistory,
  createNavigationSequence,
  createWorkspaceLocationPersistence,
  parseWorkspaceLocation,
  parseWorkspaceRoute,
  resolveWorkspaceRoute,
  shouldInterceptLinkClick,
  workspaceViewSignature,
  type LinkNavigationClick,
  type WorkspaceLocationSnapshot,
  type WorkspaceUrlState,
  type WorkspaceView,
} from "../src/workspace-navigation.ts";
import type {
  BrowserWorkspaceShareDecodeResult,
  BrowserWorkspaceShareEncodeResult,
  BrowserWorkspaceShareState,
} from "../src/inspect-web-engine.d.ts";

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
      {
        id: "t0",
        kind: "package",
        source: "Example.First",
        version: "1.0.0",
        framework: "net10.0",
        runtimeIdentifier: null,
      },
      {
        id: "t1",
        kind: "package",
        source: "Example.Second",
        version: "2.0.0",
        framework: "net10.0",
        runtimeIdentifier: null,
      },
    ],
    contexts: [{
      id: "g0",
      tabIds: ["t0", "t1"],
    }],
    activeTabId: "t1",
    selectedContextId: "g0",
    view: {
      lens: "api",
      type: "Example.Widget",
      memberAnchor: "0123456789",
      memberSignature: null,
      section: "facts",
      libraries: ["Example.Second"],
    },
    ...overrides,
  };
}

function decoded(
  state: BrowserWorkspaceShareState = workspaceState(),
): BrowserWorkspaceShareDecodeResult {
  return {
    succeeded: true,
    state,
    failure: null,
  };
}

function rejected(message: string): BrowserWorkspaceShareDecodeResult {
  return {
    succeeded: false,
    state: null,
    failure: {
      kind: "InvalidShape",
      path: "packet",
      message,
    },
  };
}

function encoded(
  packet = "canonical-packet",
): BrowserWorkspaceShareEncodeResult {
  return {
    succeeded: true,
    packet,
    failure: null,
  };
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

test("workspace URLs delegate canonical encoding and product-decoded activation", () => {
  const state = workspaceState();
  const encodedStates: unknown[] = [];
  const url = buildWorkspaceStateUrl(
    "https://inspect.example/packages/old?stale=1#metadata",
    state,
    stateJson => {
      encodedStates.push(JSON.parse(stateJson) as unknown);
      return encoded();
    });

  assert.equal(url.pathname, "/");
  assert.equal(url.searchParams.get("package"), "Example.Second");
  assert.equal(url.searchParams.get("w"), "canonical-packet");
  assert.equal(url.hash, "");
  const encodedState = encodedStates[0];
  assert.ok(encodedState && typeof encodedState === "object");
  assert.ok("contexts" in encodedState);
  assert.ok("activeTabId" in encodedState);
  assert.ok("selectedContextId" in encodedState);
  assert.deepEqual(encodedState.contexts, state.contexts);
  assert.equal(encodedState.activeTabId, "t1");
  assert.equal(encodedState.selectedContextId, "g0");

  const parsed = parseWorkspaceLocation(
    locationSnapshot(url),
    () => decoded());
  assert.deepEqual(
    parsed.tabs.map(tab => [tab.id, tab.version, tab.framework]),
    [
      ["Example.First", "1.0.0", "net10.0"],
      ["Example.Second", "2.0.0", "net10.0"],
    ]);
  assert.equal(parsed.active, 1);
  assert.equal(parsed.package, "Example.Second");
  assert.equal(parsed.version, "2.0.0");
  assert.equal(parsed.framework, "net10.0");
  assert.equal(parsed.lens, "api");
  assert.equal(parsed.library, "Example.Second");
  assert.equal(parsed.libraryPack, null);
  assert.equal(parsed.type, "Example.Widget");
  assert.equal(parsed.member, null);
  assert.equal(parsed.memberAnchor, "0123456789");
  assert.equal(parsed.memberSignature, null);
  assert.equal(parsed.overload, null);
  assert.equal(parsed.section, "facts");
  assert.deepEqual(parsed.contexts, state.contexts);
  assert.equal(parsed.selectedContextId, "g0");
  assert.equal(parsed.workspaceNotice, "");
});

test("workspace route preflight defers packet decoding", () => {
  const location = locationSnapshot(
    "https://inspect.example/?package=Visible.Package&w=opaque#metadata");
  const route = parseWorkspaceRoute(location);

  assert.equal(route.encodedWorkspaceState, "opaque");
  assert.equal(route.hasWorkspaceState, true);
  assert.equal(route.visible.package, "Visible.Package");
  assert.deepEqual(route.visible.tabs, []);
  assert.equal(route.visible.workspaceNotice, "");

  let decodeCalls = 0;
  const resolved = resolveWorkspaceRoute(route, value => {
    decodeCalls++;
    assert.equal(value, "opaque");
    return rejected("The product decoder rejected this packet.");
  });

  assert.equal(decodeCalls, 1);
  assert.equal(resolved.package, "Visible.Package");
  assert.deepEqual(resolved.tabs, []);
  assert.equal(
    resolved.workspaceNotice,
    "The shared workspace state was rejected (InvalidShape): "
      + "The product decoder rejected this packet.");
});

test("workspace route resolution skips the decoder without packet state", () => {
  const route = parseWorkspaceRoute(locationSnapshot(
    "https://inspect.example/packages/Example.Package/1.0.0#source"));
  let decodeCalls = 0;
  const resolved = resolveWorkspaceRoute(route, () => {
    decodeCalls++;
    return rejected("unexpected");
  });

  assert.equal(route.encodedWorkspaceState, null);
  assert.equal(route.hasWorkspaceState, false);
  assert.equal(decodeCalls, 0);
  assert.equal(resolved.package, "Example.Package");
  assert.equal(resolved.workspaceNotice, "");
});

test("location preflight snapshots once and defers decoding", () => {
  let currentCalls = 0;
  let decodeCalls = 0;
  const persistence = createWorkspaceLocationPersistence({
    current() {
      currentCalls++;
      return locationSnapshot(
        "https://inspect.example/?package=Visible.Package&w=opaque");
    },
    replace() {},
    push() {},
    decode(value) {
      decodeCalls++;
      assert.equal(value, "opaque");
      return rejected("The product decoder rejected this packet.");
    },
    encode: () => encoded(),
  });

  const preflight = persistence.preflightCurrent();
  assert.equal(currentCalls, 1);
  assert.equal(decodeCalls, 0);
  assert.equal(preflight.visible.package, "Visible.Package");
  assert.equal(preflight.hasWorkspaceState, true);

  const resolved = preflight.resolve();

  assert.equal(currentCalls, 1);
  assert.equal(decodeCalls, 1);
  assert.equal(
    resolved.workspaceNotice,
    "The shared workspace state was rejected (InvalidShape): "
      + "The product decoder rejected this packet.");
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

test("unknown workspace view and member-section tokens are ignored", () => {
  const unknownLens = parseWorkspaceLocation(locationSnapshot(
    "https://inspect.example/?package=Example.Package"
      + "&section=history#implementation"), () => rejected("unused"));
  assert.equal(unknownLens.lens, null);
  assert.equal(unknownLens.atPackageRoot, false);
  assert.equal(unknownLens.packageLens, null);
  assert.equal(unknownLens.section, null);

  const unknownPackageLens = parseWorkspaceLocation(locationSnapshot(
    "https://inspect.example/?package=Example.Package#pkg:files"),
  () => rejected("unused"));
  assert.equal(unknownPackageLens.atPackageRoot, true);
  assert.equal(unknownPackageLens.packageLens, "overview");
});

test("product decoder failures preserve visible location authority", () => {
  const parsed = parseWorkspaceLocation(
    locationSnapshot(
      "https://inspect.example/?package=Visible.Package&w=legacy"),
    () => rejected("Legacy packets are not supported."));

  assert.equal(parsed.package, "Visible.Package");
  assert.deepEqual(parsed.tabs, []);
  assert.match(parsed.workspaceNotice, /Legacy packets are not supported/);
});

test("unsupported canonical Browser views fail visibly without partial state", () => {
  const unsupported = workspaceState({
    view: {
      ...workspaceState().view,
      section: "History",
    },
  });
  const parsed = parseWorkspaceLocation(
    locationSnapshot(
      "https://inspect.example/?package=Visible.Package&w=canonical"),
    () => decoded(unsupported));

  assert.equal(parsed.package, "Visible.Package");
  assert.deepEqual(parsed.tabs, []);
  assert.match(parsed.workspaceNotice, /not supported by this browser/);
});

test("location persistence contains sync failures but leaves direct build failures visible", () => {
  const current = locationSnapshot("https://inspect.example/");
  const replaced: string[] = [];
  const pushed: string[] = [];
  let failEncoding = false;
  const encode = (): BrowserWorkspaceShareEncodeResult => failEncoding
    ? {
      succeeded: false,
      packet: null,
      failure: {
        kind: "InvalidTopology",
        path: "context[g0]",
        message: "The selected context is not projectable.",
      },
    }
    : encoded();
  const persistence = createWorkspaceLocationPersistence({
    current: () => current,
    replace: url => replaced.push(url),
    push: url => pushed.push(url),
    decode: () => rejected("unused"),
    encode,
  });

  persistence.sync(workspaceState());
  persistence.push("/");
  const replacedUrl = replaced[0];
  assert.ok(replacedUrl);
  assert.equal(new URL(replacedUrl).searchParams.get("package"), "Example.Second");
  assert.deepEqual(pushed, ["/"]);
  const replacedCount = replaced.length;
  failEncoding = true;
  assert.doesNotThrow(() => persistence.sync(workspaceState()));
  assert.equal(replaced.length, replacedCount);

  const blocked = createWorkspaceLocationPersistence({
    current: () => current,
    replace: () => {
      throw new DOMException("blocked");
    },
    push: () => {
      throw new DOMException("blocked");
    },
    decode: () => rejected("unused"),
    encode: () => encoded(),
  });
  assert.doesNotThrow(() => blocked.sync(workspaceState()));
  assert.doesNotThrow(() => blocked.push("/"));
  assert.throws(
    () => persistence.build(workspaceState()),
    /selected context is not projectable/);
});

function linkClick(overrides: Partial<LinkNavigationClick> = {}): LinkNavigationClick {
  return {
    button: 0,
    metaKey: false,
    ctrlKey: false,
    shiftKey: false,
    altKey: false,
    defaultPrevented: false,
    download: false,
    target: null,
    href: "https://inspect.example/credits",
    origin: "https://inspect.example",
    currentOrigin: "https://inspect.example",
    ...overrides,
  };
}

test("shouldInterceptLinkClick takes over a plain same-origin left click", () => {
  assert.equal(shouldInterceptLinkClick(linkClick()), true);
});

test("shouldInterceptLinkClick leaves default-prevented clicks alone", () => {
  assert.equal(
    shouldInterceptLinkClick(linkClick({ defaultPrevented: true })),
    false);
});

test("shouldInterceptLinkClick leaves non-primary-button clicks alone", () => {
  assert.equal(shouldInterceptLinkClick(linkClick({ button: 1 })), false);
});

test("shouldInterceptLinkClick leaves modified clicks alone (new tab/window)", () => {
  for (const overrides of [
    { metaKey: true }, { ctrlKey: true }, { shiftKey: true }, { altKey: true },
  ]) {
    assert.equal(shouldInterceptLinkClick(linkClick(overrides)), false);
  }
});

test("shouldInterceptLinkClick leaves download links alone", () => {
  assert.equal(shouldInterceptLinkClick(linkClick({ download: true })), false);
});

test("shouldInterceptLinkClick leaves an explicit other-target link alone", () => {
  assert.equal(
    shouldInterceptLinkClick(linkClick({ target: "_blank" })),
    false);
  assert.equal(
    shouldInterceptLinkClick(linkClick({ target: "_self" })),
    true);
});

test("shouldInterceptLinkClick leaves cross-origin links alone", () => {
  assert.equal(
    shouldInterceptLinkClick(linkClick({ origin: "https://github.com" })),
    false);
});

test("shouldInterceptLinkClick requires a resolvable href", () => {
  assert.equal(shouldInterceptLinkClick(linkClick({ href: null })), false);
});
