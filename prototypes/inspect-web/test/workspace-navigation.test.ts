import assert from "node:assert/strict";
import test from "node:test";

import {
  bindWorkspaceRetryToUrl,
  browserCreatedCallGraphTabIds,
  buildPackageRootStateUrl,
  buildWorkspaceStateUrl,
  callGraphCaptureTopology,
  createNavigationHistory,
  createNavigationSequence,
  createWorkspaceLocationPersistence,
  parseWorkspaceLocation,
  parseWorkspaceRoute,
  recoverWorkspaceRouteFailure,
  retainedMissingPlatformTarget,
  retainedPlatformTargetVersion,
  retainWorkspaceUrlPreservation,
  resolveWorkspaceRoute,
  selectedBrowserCallGraphPackageTabIds,
  shouldInterceptLinkClick,
  workspaceUrlPreservationApplies,
  workspaceShareTabsMatchResolved,
  workspaceShareCaptureTopology,
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
} from "../src/facades/inspect-web-catalog.d.ts";

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
    subject: null,
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
    workspaceSubjectOpen: false,
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

test("navigation history restores a pre-activation transaction snapshot", () => {
  let current: TestView | null = { id: "stable", revision: 1 };
  const history = createNavigationHistory({
    capture: () => current && { ...current },
    signature: view => `${view.id}:${view.revision}`,
    apply: view => {
      current = { ...view };
      return true;
    },
    onExhausted() {},
  });

  history.record();
  const snapshot = history.snapshot();
  current = { id: "partial", revision: 1 };
  history.record();
  history.restore(snapshot);

  assert.equal(history.canBack(), false);
  assert.equal(history.canForward(), false);
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
});

test("workspace-subject URLs preserve retained coordinates and restore Workspace", () => {
  const state = workspaceState({
    subject: "workspace",
    view: {
      lens: null,
      type: null,
      memberAnchor: null,
      memberSignature: null,
      section: null,
      libraries: [],
    },
  });
  const url = buildWorkspaceStateUrl(
    "https://inspect.example/?package=Old#pkg",
    state,
    () => encoded());

  assert.equal(url.hash, "#workspace");
  const parsed = parseWorkspaceLocation(
    locationSnapshot(url),
    () => decoded(state));
  assert.equal(parsed.workspaceSubjectOpen, true);
  assert.equal(parsed.atPackageRoot, true);
  assert.equal(parsed.packageLens, "overview");
  assert.equal(parsed.tabs.length, 2);
  assert.equal(parsed.package, "Example.Second");
  assert.equal(parsed.workspaceNotice, "");
});

test("canonical context capture does not broaden a selected subset for Call Graph", () => {
  const basis: BrowserWorkspaceShareState = {
    tabs: [
      {
        id: "t0",
        kind: "package",
        source: "A",
        version: "1.0.0",
        framework: "net10.0",
        runtimeIdentifier: null,
      },
      {
        id: "t1",
        kind: "package",
        source: "B",
        version: "1.0.0",
        framework: "net10.0",
        runtimeIdentifier: null,
      },
      {
        id: "t2",
        kind: "package",
        source: "C",
        version: "1.0.0",
        framework: "net10.0",
        runtimeIdentifier: null,
      },
    ],
    contexts: [{ id: "g0", tabIds: ["t0", "t1"] }],
    activeTabId: "t0",
    selectedContextId: "g0",
    view: {
      lens: "api",
      type: null,
      memberAnchor: null,
      memberSignature: null,
      section: "Call Graph",
      libraries: [],
    },
  };

  const captured = workspaceShareCaptureTopology(
    basis.tabs,
    0,
    basis,
    true,
    true);

  assert.deepEqual(captured, {
    contexts: [{ id: "g0", tabIds: ["t0", "t1"] }],
    selectedContextId: "g0",
  });
});

test("Browser-created Call Graph state synthesizes root-first package context", () => {
  const tabs = workspaceState().tabs;

  const captured = workspaceShareCaptureTopology(
    tabs,
    1,
    null,
    false,
    true);

  assert.deepEqual(captured.contexts.at(-1), {
    id: "g2",
    tabIds: ["t1", "t0"],
  });
  assert.equal(captured.selectedContextId, "g2");
});

test("reminted tab IDs cannot preserve stale canonical contexts", () => {
  const basis: BrowserWorkspaceShareState = {
    ...workspaceState(),
    contexts: [{ id: "g0", tabIds: ["t1"] }],
    selectedContextId: "g0",
  };
  const reminted = basis.tabs.map((tab, index) => ({
    ...tab,
    source: `Replacement.${index}`,
  }));

  const captured = workspaceShareCaptureTopology(
    reminted,
    0,
    basis,
    false,
    false);

  assert.deepEqual(captured, {
    contexts: [
      { id: "g0", tabIds: ["t0"] },
      { id: "g1", tabIds: ["t1"] },
    ],
    selectedContextId: "g0",
  });
});

test("Browser-created Call Graph contexts include only binding-compatible tabs", () => {
  const tabs = workspaceState().tabs.map((tab, index) => ({
    ...tab,
    framework: index === 0 ? "net10.0" : "net6.0",
  }));

  assert.deepEqual(browserCreatedCallGraphTabIds(tabs, 0), ["t0"]);
  assert.deepEqual(
    workspaceShareCaptureTopology(tabs, 0, null, false, true),
    {
      contexts: [
        { id: "g0", tabIds: ["t0"] },
        { id: "g1", tabIds: ["t1"] },
      ],
      selectedContextId: "g0",
    });
});

test("executed Call Graph topology preserves exact product order and excludes unrelated tabs", () => {
  const tabs = workspaceState().tabs.concat({
    id: "t2",
    kind: "package",
    source: "Unrelated.Package",
    version: "1.0.0",
    framework: "net10.0",
    runtimeIdentifier: null,
  });

  assert.deepEqual(
    callGraphCaptureTopology(tabs, 1, ["t0", "t1"]),
    {
      contexts: [
        { id: "g0", tabIds: ["t0"] },
        { id: "g1", tabIds: ["t1"] },
        { id: "g2", tabIds: ["t2"] },
        { id: "g3", tabIds: ["t0", "t1"] },
      ],
      selectedContextId: "g3",
    });
  assert.throws(
    () => callGraphCaptureTopology(tabs, 1, ["t0", "t2"]),
    /active package is not part/);
});

test("Platform drill target version preserves exact versus floating packet identity", () => {
  const runtimePack = {
    version: "10.0.10",
    activeFramework: "net10.0",
  };
  const tab = {
    id: "t0",
    kind: "group",
    source: ":Platform",
    version: "10.0.10",
    framework: "net10.0",
    runtimeIdentifier: null,
  };

  assert.equal(
    retainedPlatformTargetVersion(tab, runtimePack, "net10.0"),
    "10.0.10");
  assert.equal(
    retainedPlatformTargetVersion(
      { ...tab, version: null },
      runtimePack,
      "net10.0"),
    "");
  assert.equal(
    retainedPlatformTargetVersion(tab, runtimePack, "net9.0"),
    "");
  assert.equal(
    retainedPlatformTargetVersion(null, runtimePack, "net10.0"),
    "");
});

test("canonical tabs must remain distinct and ordered after resolution", () => {
  const requested = workspaceState().tabs;
  const resolved = requested.map(tab => ({
    ...tab,
    version: tab.version ?? "10.0.11",
    framework: tab.framework ?? "net10.0",
  }));

  assert.equal(workspaceShareTabsMatchResolved(requested, resolved), true);
  assert.equal(
    workspaceShareTabsMatchResolved(requested, resolved.slice(0, 1)),
    false);
  assert.equal(
    workspaceShareTabsMatchResolved(
      requested,
      [resolved[1]!, resolved[0]!]),
    false);
});

test("missing Platform reacquisition retains only an aligned canonical pin", () => {
  const packageTab = workspaceState().tabs[0]!;
  const platformTab = {
    id: "platform",
    kind: "group",
    source: ":Platform",
    version: "10.0.10",
    framework: "net10.0",
    runtimeIdentifier: null,
  };
  const basis = [packageTab, platformTab];

  assert.deepEqual(
    retainedMissingPlatformTarget(basis, [packageTab], "net10.0"),
    { tabIndex: 1, version: "10.0.10" });
  assert.deepEqual(
    retainedMissingPlatformTarget(
      [{ ...platformTab, version: null }, packageTab],
      [packageTab],
      "net10.0"),
    { tabIndex: 0, version: "" });
  assert.equal(
    retainedMissingPlatformTarget(basis, [packageTab], "net9.0"),
    null);
  assert.equal(
    retainedMissingPlatformTarget(
      [{ ...platformTab, framework: null }, packageTab],
      [packageTab],
      "net10.0"),
    null);
  assert.equal(
    retainedMissingPlatformTarget(
      basis,
      [{ ...packageTab, source: "Replacement.Package" }],
      "net10.0"),
    null);
});

test("package-root URLs discard stale workspace state and restore the package lens", () => {
  const url = buildPackageRootStateUrl(
    "https://inspect.example/?package=Old&w=stale#api",
    {
      package: "Example.Package",
      version: "1.2.3",
      framework: "net10.0",
      lens: "dependencies",
    });

  assert.equal(url.searchParams.get("w"), null);
  assert.equal(url.searchParams.get("package"), "Example.Package");
  assert.equal(url.hash, "#pkg:dependencies");
  const parsed = parseWorkspaceLocation(
    locationSnapshot(url),
    () => rejected("unexpected"));
  assert.equal(parsed.atPackageRoot, true);
  assert.equal(parsed.workspaceSubjectOpen, false);
  assert.equal(parsed.packageLens, "dependencies");
  assert.equal(parsed.version, "1.2.3");
});

test("unsupported canonical lenses fail visibly before activation", () => {
  const initial = workspaceState();
  const state = workspaceState({
    view: {
      ...initial.view,
      lens: "future-lens",
    },
  });

  const parsed = parseWorkspaceLocation(
    locationSnapshot("https://inspect.example/?package=Visible&w=opaque"),
    () => decoded(state));

  assert.deepEqual(parsed.tabs, []);
  assert.match(parsed.workspaceNotice, /future-lens.*not supported/);
});

test("multiple canonical Platform tabs fail visibly before activation", () => {
  const state = workspaceState();
  state.tabs = [
    {
      id: "t0",
      kind: "group",
      source: ":Platform",
      version: "10.0.10",
      framework: "net10.0",
      runtimeIdentifier: null,
    },
    {
      id: "t1",
      kind: "group",
      source: ":Platform",
      version: "10.0.11",
      framework: "net10.0",
      runtimeIdentifier: null,
    },
  ];

  const parsed = parseWorkspaceLocation(
    locationSnapshot("https://inspect.example/?package=Visible&w=opaque"),
    () => decoded(state));

  assert.deepEqual(parsed.tabs, []);
  assert.match(parsed.workspaceNotice, /multiple Platform tabs/);
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

test("authoritative packets bypass malformed courtesy paths", () => {
  let decodeCalls = 0;
  const parsed = parseWorkspaceLocation({
    href: "https://inspect.example/packages/%E0%A4%A/1.0.0"
      + "?package=Visible.Package&w=opaque",
    pathname: "/packages/%E0%A4%A/1.0.0",
    search: "?package=Visible.Package&w=opaque",
    hash: "",
  }, () => {
    decodeCalls++;
    return rejected("The packet is invalid.");
  });

  assert.equal(decodeCalls, 1);
  assert.equal(parsed.hasWorkspaceState, true);
  assert.equal(parsed.shareState, null);
  assert.equal(parsed.package, "Visible.Package");
  assert.equal(parsed.routeFailure, null);
  assert.match(parsed.workspaceNotice, /packet is invalid/);
});

test("malformed courtesy package routes become typed failures", () => {
  const route = parseWorkspaceRoute({
    href: "https://inspect.example/packages/%E0%A4%A/1.0.0",
    pathname: "/packages/%E0%A4%A/1.0.0",
    search: "",
    hash: "",
  });

  assert.equal(route.visible.package, "");
  assert.equal(route.visible.version, "");
  assert.deepEqual(route.visible.routeFailure, {
    kind: "MalformedPathEncoding",
    message:
      "The package route contains malformed percent-encoding in its package or version.",
  });

  const resolved = resolveWorkspaceRoute(route, () => {
    throw new Error("unexpected packet decode");
  });
  assert.deepEqual(resolved.routeFailure, route.visible.routeFailure);
});

test("valid courtesy package routes continue to decode normally", () => {
  const parsed = parseWorkspaceLocation(locationSnapshot(
    "https://inspect.example/packages/Example%2EPackage/1.0.0%2Bbuild#source"),
  () => {
    throw new Error("unexpected packet decode");
  });

  assert.equal(parsed.package, "Example.Package");
  assert.equal(parsed.version, "1.0.0+build");
  assert.equal(parsed.routeFailure, null);
});

test("an empty workspace parameter remains authoritative", () => {
  const route = parseWorkspaceRoute(locationSnapshot(
    "https://inspect.example/?package=Visible.Package&w=#metadata"));

  assert.equal(route.encodedWorkspaceState, "");
  assert.equal(route.hasWorkspaceState, true);
  assert.equal(route.visible.hasWorkspaceState, true);
});

test("Browser Call Graph contexts reject Platform participants", () => {
  const state = workspaceState();
  state.tabs = [
    state.tabs[0]!,
    {
      id: "t1",
      kind: "group",
      source: ":Platform",
      version: "10.0.11",
      framework: "net10.0",
      runtimeIdentifier: null,
    },
  ];
  state.contexts = [{ id: "g0", tabIds: ["t0", "t1"] }];
  state.selectedContextId = "g0";

  assert.throws(
    () => selectedBrowserCallGraphPackageTabIds(state),
    /Platform participant.*cannot realize/);

  state.contexts = [{ id: "g0", tabIds: ["t0"] }];
  assert.deepEqual(
    selectedBrowserCallGraphPackageTabIds(state),
    ["t0"]);
});

test("failed URL retention survives automatic renders until navigation changes", () => {
  const preservation = {
    url: "https://inspect.example/?package=Failed&w=opaque",
    projection: "old-workspace",
  };

  assert.equal(
    workspaceUrlPreservationApplies(
      preservation,
      preservation.url,
      preservation.projection),
    true);
  assert.equal(
    workspaceUrlPreservationApplies(
      preservation,
      "https://inspect.example/?package=Other",
      preservation.projection),
    false);
  assert.equal(
    workspaceUrlPreservationApplies(
      preservation,
      preservation.url,
      "changed-workspace"),
    false);
});

test("failed URL state is retained and retired atomically", () => {
  const routeFailure = {
    kind: "route",
    notice: "Package route failed",
    url: "https://inspect.example/packages/%E0%A4%A/1.0.0",
    projection: "resident-workspace",
  } as const;

  assert.equal(
    retainWorkspaceUrlPreservation(
      routeFailure,
      routeFailure.url,
      routeFailure.projection),
    routeFailure);
  assert.equal(
    retainWorkspaceUrlPreservation(
      routeFailure,
      routeFailure.url,
      "changed-workspace"),
    null);
});

test("workspace retry restores its owned URL before running", () => {
  let currentUrl = "https://inspect.example/packages/%E0%A4%A/1.0.0";
  let blockedReplaceCount = 0;
  let retryCount = 0;
  const failedUrl = "https://inspect.example/?w=canonical";
  const retry = bindWorkspaceRetryToUrl(
    failedUrl,
    () => currentUrl,
    url => {
      currentUrl = url;
      return true;
    },
    () => {
      retryCount++;
      return currentUrl;
    });

  assert.equal(retry(), failedUrl);
  assert.equal(currentUrl, failedUrl);
  assert.equal(retryCount, 1);

  const sameUrlBlockedRetry = bindWorkspaceRetryToUrl(
    failedUrl,
    () => currentUrl,
    () => {
      blockedReplaceCount++;
      return false;
    },
    () => {
      retryCount++;
    });
  assert.equal(sameUrlBlockedRetry(), undefined);
  assert.equal(blockedReplaceCount, 0);
  assert.equal(retryCount, 2);

  currentUrl = "https://inspect.example/packages/%E0%A4%A/1.0.0";
  const movedUrlBlockedRetry = bindWorkspaceRetryToUrl(
    failedUrl,
    () => currentUrl,
    () => {
      blockedReplaceCount++;
      return false;
    },
    () => {
      retryCount++;
    });
  assert.equal(movedUrlBlockedRetry(), undefined);
  assert.equal(blockedReplaceCount, 1);
  assert.equal(currentUrl, "https://inspect.example/packages/%E0%A4%A/1.0.0");
  assert.equal(retryCount, 2);
});

test("route failure recovery owns malformed URL replacement", () => {
  const failure = {
    pathname: "/packages/%E0%A4%A/1.0.0",
    search: "",
    recoveryUrl: "/?package=Example.Package&version=1.0.0",
  };
  const replacements: string[] = [];
  const malformedLocation = {
    pathname: failure.pathname,
    search: failure.search,
  };

  assert.equal(
    recoverWorkspaceRouteFailure(
      failure,
      malformedLocation,
      url => {
        replacements.push(url);
        return true;
      }),
    true);
  assert.deepEqual(replacements, [failure.recoveryUrl]);

  assert.equal(
    recoverWorkspaceRouteFailure(
      failure,
      malformedLocation,
      () => false),
    false);

  assert.equal(
    recoverWorkspaceRouteFailure(
      failure,
      { pathname: "/", search: "?w=canonical" },
      () => {
        throw new Error("A valid route must not be replaced.");
      }),
    true);
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

test("history signatures distinguish Workspace from Package", () => {
  const packageView = workspaceView({
    atPackageRoot: true,
    workspaceSubjectOpen: false,
  });

  assert.notEqual(
    workspaceViewSignature(packageView),
    workspaceViewSignature({
      ...packageView,
      workspaceSubjectOpen: true,
    }));
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
  assert.equal(parsed.hasWorkspaceState, true);
  assert.deepEqual(parsed.tabs, []);
  assert.match(parsed.workspaceNotice, /Legacy packets are not supported/);
});

test("canonical packets without a lens discard legacy hash state", () => {
  const initial = workspaceState();
  const state = workspaceState({
    view: {
      ...initial.view,
      lens: null,
    },
  });

  const parsed = parseWorkspaceLocation(
    locationSnapshot(
      "https://inspect.example/?package=Visible.Package&w=canonical#metadata"),
    () => decoded(state));

  assert.equal(parsed.shareState?.view.lens, null);
  assert.equal(parsed.lens, null);
  assert.equal(parsed.atPackageRoot, false);
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
  assert.equal(parsed.hasWorkspaceState, true);
  assert.deepEqual(parsed.tabs, []);
  assert.match(parsed.workspaceNotice, /not supported by this browser/);
});

test("location persistence contains sync failures but leaves direct build failures visible", () => {
  const current = locationSnapshot("https://inspect.example/");
  const replaced: Array<{ url: string; state: unknown }> = [];
  const pushed: Array<{ url: string; state: unknown }> = [];
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
    replace: (url, state) => replaced.push({ url, state }),
    push: (url, state) => pushed.push({ url, state }),
    decode: () => rejected("unused"),
    encode,
  });

  persistence.sync(workspaceState(), { entry: "workspace" });
  persistence.push("/", { route: "query" });
  assert.equal(persistence.replace("/valid"), true);
  const replacedEntry = replaced[0];
  assert.ok(replacedEntry);
  assert.equal(
    new URL(replacedEntry.url).searchParams.get("package"),
    "Example.Second");
  assert.deepEqual(replacedEntry.state, { entry: "workspace" });
  assert.deepEqual(pushed, [{
    url: "/",
    state: { route: "query" },
  }]);
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
  assert.equal(blocked.replace("/valid"), false);
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
