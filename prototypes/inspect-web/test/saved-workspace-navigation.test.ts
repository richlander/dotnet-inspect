import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { stripTypeScriptTypes } from "node:module";
import { runInNewContext } from "node:vm";
import test from "node:test";
import { parseSync } from "oxc-parser";
import {
  MAX_WORKSPACE_PACKAGES,
  packageIdentityKey,
  typeLensesFor,
} from "../src/data.ts";
import type {
  BrowserWorkspaceShareDecodeResult,
  BrowserWorkspaceShareEncodeResult,
  BrowserWorkspaceShareState,
} from "../src/facades/inspect-web-catalog.d.ts";
import { memberScopeIsActive } from "../src/member-filtering.ts";
import { isProductHomeDemosPath } from "../src/product-home-demos.ts";
import type { SavedWorkspace, SavedWorkspaceFocus } from "../src/saved-workspaces.ts";
import {
  createNavigationHistory,
  createNavigationSequence,
  createWorkspaceLocationPersistence,
  parseWorkspaceLocation,
  workspaceShareCaptureTopology,
  workspaceShareTabsMatchResolved,
  type ParsedWorkspaceLocation,
} from "../src/workspace-navigation.ts";

const appSource = readFileSync(new URL("../src/dotnet-inspect.ts", import.meta.url), "utf8");
const app = parseSync("dotnet-inspect.ts", appSource);
assert.deepEqual(app.errors, []);
const hostNames = new Set([
  "captureSavedWorkspacePacket", "captureWorkspaceUrlState",
  "capturedShareTabs", "resolvedWorkspaceShareTabs", "scope", "syncUrl", "buildStateUrl",
  "openSavedWorkspace", "restoreWorkspaceCatalogEntry", "restoreWorkspaceFromLocation",
  "parseWorkspaceHref", "beginDemoNavigation", "stageDemoNavigation",
  "commitDemoNavigation", "cancelDemoNavigation",
  "captureCanonicalWorkspaceRestoreSnapshot", "restoreCanonicalWorkspaceRestoreSnapshot",
  "failWorkspaceCatalogAction", "afterCurrentNavigationFrame",
  "focusInspectionResult", "focusLevelOneHeading",
  "applyLocationView", "canonicalViewRestorationFailure", "commitWorkspaceShareBasis",
  "errorMessage", "isRecord", "runHomeDemo", "failDemoWorkspaceOpen",
]);
const hostFunctions = app.program.body.filter(
  node => node.type === "FunctionDeclaration" && hostNames.has(node.id?.name ?? ""));
assert.equal(hostFunctions.length, hostNames.size);
const hostDeclarations = hostFunctions
  .map(node => appSource.slice(node.start, node.end)).join("\n");

interface Package {
  id: string;
  version: string;
  activeFramework: string;
  isRuntimePack?: boolean;
  types: { id: string }[];
}

const sourcePackage: Package = {
  id: "Source", version: "1.2.3", activeFramework: "net10.0", types: [],
};
const packet = "opaque+/packet?name=ignored&x=1#fragment";
const saved = Object.freeze({ name: "My Workspace", packet });

function sharedState(): BrowserWorkspaceShareState {
  return {
    tabs: [
      { id: "first", kind: "package", source: "Alpha", version: "2.3.4",
        framework: "net10.0", runtimeIdentifier: null },
      { id: "second", kind: "package", source: "Beta", version: "5.6.7",
        framework: "net9.0", runtimeIdentifier: null },
    ],
    contexts: [{ id: "both", tabIds: ["first", "second"] }],
    activeTabId: "second",
    selectedContextId: "both",
    view: {
      lens: null, type: null, memberAnchor: null, memberSignature: null,
      section: null, libraries: [],
    },
  };
}

function deferred<T>() {
  let resolve!: (value: T) => void;
  let reject!: (reason: unknown) => void;
  const promise = new Promise<T>((accept, deny) => {
    resolve = accept;
    reject = deny;
  });
  return { promise, resolve, reject };
}

function harness() {
  const state = {
    home: false, credits: false, packageQueryOpen: false,
    engineReady: true, loading: false, error: "",
    workspaceSubjectOpen: true, atPackageRoot: true, packageLens: "overview",
    packages: [sourcePackage], package: sourcePackage as Package | null,
    workspaceShareBasis: null as BrowserWorkspaceShareState | null,
    libraryScope: null as Set<string> | null,
    lens: "api", selectedTypeId: "", selectedMemberKey: "",
    selectedOverloadIndex: null, memberSection: "overview",
    queryNotice: "", queryNoticeRetryAction: null as (() => void) | null,
    workspaceDependencies: [], workspaceDependencyErrors: [],
    workspaceDependencyLoads: new Set<string>(),
    packageVersions: {}, packageVersionsLoading: {},
    accessibilityFilter: new Set(["public"]),
    memberAnnotatedEmbedded: null, memberAnnotatedModal: null,
    platformStack: [], platformRecent: [], recentPackages: [],
    spotlightPkgHits: [], history: [],
  };
  const navigationSequence = createNavigationSequence();
  const navigationHistory = createNavigationHistory({
    capture: () => state.package ? {
      key: packageIdentityKey(state.package),
      workspace: state.workspaceSubjectOpen,
    } : null,
    signature: view => JSON.stringify(view),
    apply: () => true,
    onExhausted: () => {},
  });
  navigationHistory.record();
  const location = new URL("https://inspect.test/?package=Source&w=source-packet&keep=1#workspace");
  const history = { state: { entry: "source-entry" } as unknown };
  const writes: { kind: "push" | "replace"; url: string; state: unknown }[] = [];
  const frames: (() => void)[] = [];
  const focus: unknown[] = [];
  const effects: string[] = [];
  const decoded: string[] = [];
  const encoded: unknown[] = [];
  const acquisitions: string[] = [];
  const operations: Promise<unknown>[] = [];
  const controls = {
    share: sharedState(),
    encodeResult: {
      succeeded: true, packet, failure: null,
    } as BrowserWorkspaceShareEncodeResult,
    decodeError: null as Error | null,
    decodeFailure: "",
    acquisition: async (_id: string): Promise<boolean> => true,
    selection: async (): Promise<void> => {},
    savedFocusAvailable: true,
  };
  function decode(value: string): BrowserWorkspaceShareDecodeResult {
    decoded.push(value);
    if (controls.decodeError) throw controls.decodeError;
    if (controls.decodeFailure) return {
      succeeded: false, state: null,
      failure: { kind: "InvalidShape", path: "packet", message: controls.decodeFailure },
    };
    return { succeeded: true, state: controls.share, failure: null };
  }
  const workspaceLocation = createWorkspaceLocationPersistence({
    current: () => location,
    decode,
    encode: json => {
      encoded.push(JSON.parse(json));
      return controls.encodeResult;
    },
    push: (url, entryState) => write("push", url, entryState),
    replace: (url, entryState) => write("replace", url, entryState),
  });
  function write(kind: "push" | "replace", url: string, entryState: unknown) {
    writes.push({ kind, url, state: entryState });
    location.href = new URL(url, location).href;
    history.state = entryState;
  }
  const heading = { tabIndex: 0, focus: () => focus.push("heading") };
  const document = {
    title: "",
    querySelector: (selector: string) => {
      assert.equal(selector, "main h1");
      return heading;
    },
  };
  const context = {
    state, location, history, document, workspaceLocation,
    navigationSequence, navigationHistory,
    pendingDemoNavigation: null as { navigationSeq: number; destination: string } | null,
    failedWorkspaceUrlState: null, spotlightCache: null,
    URL, URLSearchParams, Error, structuredClone, Set,
    MAX_WORKSPACE_PACKAGES, packageIdentityKey, memberScopeIsActive,
    typeLensesFor, workspaceShareCaptureTopology, workspaceShareTabsMatchResolved,
    parseWorkspaceLocation, isProductHomeDemosPath,
    inspectDecodeWorkspaceShareState: decode,
    requestAnimationFrame: (action: () => void) => frames.push(action),
    observeAsync: (operation: Promise<unknown>) => operations.push(operation),
    sourceInspection: {
      cancelCurrentRequest: () => {},
      clearGraphSource: () => {},
    },
    cancelAnnotatedSourceRequest: () => {},
    clearWorkspaceOccurrenceView: () => {},
    clearWorkspacePackages: () => { state.packages = []; state.package = null; },
    persistRecentPackages: () => {},
    persistPlatformRecent: () => {},
    refreshPackageStats: () => {},
    clearWorkspaceRouteFailure: () => true,
    resetLocationFilters: () => {},
    currentPackageQueryHandoff: () => false,
    retainFailedWorkspaceUrl: () => false,
    packageDisplayName: (pkg: Package) => pkg.id,
    selectedType: () => null,
    isRuntimePackId: () => false,
    loadPackage: async (
      id: string, version: string, framework: string,
      options: { background: boolean; navigationSeq: number },
    ) => {
      assert.equal(options.background, true);
      acquisitions.push(`${id}@${version}/${framework}`);
      const succeeded = await controls.acquisition(id);
      if (!navigationSequence.isCurrent(options.navigationSeq) || !succeeded) return null;
      const pkg = { id, version, activeFramework: framework, types: [] };
      state.packages.push(pkg);
      return pkg;
    },
    activatePackage: (pkg: Package) => { state.package = pkg; },
    applyLoadedPackageLibraryScope: () => null,
    applyDeepLink: (deep: ParsedWorkspaceLocation) => {
      effects.push("deep-link");
      state.selectedTypeId = deep.type ?? "";
    },
    loadSelectionData: () => controls.selection(),
    appendQueryNotice: (message: string, retry: (() => void) | null) => {
      state.queryNotice = message;
      state.queryNoticeRetryAction = retry;
    },
    restoreWorkspaceFocus: (_root: unknown, target: SavedWorkspaceFocus) => {
      focus.push(structuredClone(target));
      return controls.savedFocusAvailable;
    },
    focusWorkspace: () => { focus.push("workspace"); return true; },
    render: () => {
      effects.push("render");
      if (!state.loading && state.package && !state.home && !state.error) {
        runInNewContext("syncUrl()", context);
        navigationHistory.record();
      }
    },
    inspectResolveHomeDemo: () => ({ found: true, demo: {} }),
    productHomeDemoLocationHref: () => `/?w=${encodeURIComponent(packet)}#workspace`,
    inspectEncodeWorkspaceShareState: () => controls.encodeResult,
  };
  runInNewContext(stripTypeScriptTypes(hostDeclarations), context);
  return {
    state, context, controls, location, history, writes, decoded, encoded,
    acquisitions, focus, effects, operations, navigationHistory, navigationSequence,
    capture: (): string => {
      const result: unknown = runInNewContext("captureSavedWorkspacePacket()", context);
      assert.ok(typeof result === "string");
      return result;
    },
    open: (entry: SavedWorkspace = saved): void => {
      runInNewContext("openSavedWorkspace(entry)", { ...context, entry });
    },
    demo: (): void => { runInNewContext('runHomeDemo("demo")', context); },
    settle: async () => { await Promise.all(operations); },
    flushFocus: () => { for (const frame of frames.splice(0)) frame(); },
  };
}

test("capture uses the original share projection and retains Workspace presentation without effects", () => {
  const h = harness();
  const basis = sharedState();
  h.state.packages = basis.tabs.map(tab => ({
    id: tab.source, version: tab.version!, activeFramework: tab.framework!, types: [],
  }));
  h.state.package = h.state.packages[1]!;
  h.state.workspaceShareBasis = basis;
  h.state.selectedTypeId = "Hidden.Old.Type";
  h.state.selectedMemberKey = "Hidden.Old.Member";
  const before = structuredClone(h.state);
  const href = h.location.href;
  const history = h.history.state;

  assert.equal(h.capture(), packet);
  assert.deepEqual(h.encoded, [basis]);
  assert.deepEqual(h.state, before);
  assert.equal(h.location.href, href);
  assert.equal(h.history.state, history);
  assert.equal(h.writes.length, 0);
  assert.deepEqual(h.effects, []);
  assert.deepEqual(h.decoded, []);
  assert.deepEqual(h.focus, []);
});

test("capture rejects wrong scopes, empty or unready Workspaces, and incomplete projection", () => {
  for (const mutate of [
    (h: ReturnType<typeof harness>) => { h.state.home = true; },
    (h: ReturnType<typeof harness>) => { h.state.credits = true; },
    (h: ReturnType<typeof harness>) => { h.state.packageQueryOpen = true; },
    (h: ReturnType<typeof harness>) => { h.state.workspaceSubjectOpen = false; },
    (h: ReturnType<typeof harness>) => { h.state.engineReady = false; },
    (h: ReturnType<typeof harness>) => { h.state.loading = true; },
    (h: ReturnType<typeof harness>) => { h.state.error = "Not ready"; },
    (h: ReturnType<typeof harness>) => { h.state.packages = []; h.state.package = null; },
    (h: ReturnType<typeof harness>) => { h.state.packages = []; },
    (h: ReturnType<typeof harness>) => { h.state.libraryScope = new Set(["One", "Two"]); },
  ]) {
    const h = harness();
    mutate(h);
    assert.throws(h.capture, /Workspace|workspace|library/);
    assert.equal(h.writes.length, 0);
    assert.deepEqual(h.encoded, []);
  }
  for (const result of [
    { succeeded: false, packet: null,
      failure: { kind: "InvalidShape", path: "workspace", message: "Projection unavailable" } },
    { succeeded: true, packet: "", failure: null },
  ] satisfies BrowserWorkspaceShareEncodeResult[]) {
    const h = harness();
    h.controls.encodeResult = result;
    assert.throws(h.capture, /Projection unavailable|canonical share/);
    assert.equal(h.writes.length, 0);
  }
});

for (const platform of [false, true]) {
  test(`saving pins resolved ${platform ? "Platform" : "package"} coordinates without replacing packet-local identities or live share intent`, () => {
    const h = harness();
    const original = sharedState();
    const basis: BrowserWorkspaceShareState = {
      ...original,
      tabs: original.tabs.map((tab, index) => index === 0 ? {
        ...tab,
        kind: platform ? "group" : "package",
        source: platform ? ":Platform" : "Alpha",
        version: null, framework: null,
      } : tab),
    };
    h.state.packages = [
      { id: platform ? ":Platform" : "Alpha", version: "2.3.4",
        activeFramework: "net10.0", isRuntimePack: platform, types: [] },
      { id: "Beta", version: "5.6.7", activeFramework: "net9.0", types: [] },
    ];
    h.state.package = h.state.packages[1]!;
    h.state.workspaceShareBasis = basis;
    const before = structuredClone(h.state);
    h.capture();
    const expected = {
      ...basis,
      tabs: basis.tabs.map((tab, index) => index === 0
        ? { ...tab, version: "2.3.4", framework: "net10.0" } : tab),
    };
    assert.deepEqual(h.encoded, [expected]);
    assert.deepEqual(h.state, before);
    assert.equal(h.writes.length, 0);
  });
}

test("saved Open uses only the opaque packet at the current origin and commits after view completion", async () => {
  const h = harness();
  h.location.href = "https://inspect.test/demos?keep=1#workspace";
  const selection = deferred<void>();
  h.controls.selection = () => selection.promise;
  const href = h.location.href;
  const entryState = h.history.state;
  const entry = { ...saved, href: "https://elsewhere.invalid/?w=other" };
  h.open(entry);
  await new Promise(resolve => setImmediate(resolve));
  assert.deepEqual(h.decoded, [packet]);
  assert.deepEqual(h.acquisitions, ["Alpha@2.3.4/net10.0", "Beta@5.6.7/net9.0"]);
  assert.equal(h.location.href, href);
  assert.equal(h.history.state, entryState);
  assert.equal(h.writes.length, 0);
  assert.deepEqual(h.focus, []);
  selection.resolve();
  await h.settle();
  const pushed = h.writes.filter(write => write.kind === "push");
  assert.equal(pushed.length, 1);
  const destination = new URL(pushed[0]!.url);
  assert.equal(destination.origin, "https://inspect.test");
  assert.equal(destination.pathname, "/");
  assert.equal(destination.hash, "#workspace");
  assert.deepEqual([...destination.searchParams], [["w", packet]]);
  assert.equal(h.location.searchParams.get("w"), packet);
  assert.equal(h.location.hash, "#workspace");
  assert.equal(h.state.package?.id, "Beta");
  assert.equal(h.state.workspaceSubjectOpen, true);
  assert.equal(h.state.atPackageRoot, true);
  assert.equal(h.context.pendingDemoNavigation, null);
  h.flushFocus();
  assert.deepEqual(h.focus, ["heading"]);
});

test("saved Open restores into an empty Workspace without a separate loader", async () => {
  const h = harness();
  Object.assign(h.state, { packages: [], package: null });
  h.location.href = "https://inspect.test/demos";
  h.open();
  await h.settle();
  assert.equal(h.state.package?.id, "Beta");
  assert.equal(h.state.packages.length, 2);
  assert.equal(h.location.pathname, "/");
  assert.equal(h.location.hash, "#workspace");
  assert.equal(h.writes.filter(write => write.kind === "push").length, 1);
  h.flushFocus();
  assert.deepEqual(h.focus, ["heading"]);
});

function assertRetained(h: ReturnType<typeof harness>, href: string, entryState: unknown) {
  assert.deepEqual(h.state.packages, [sourcePackage]);
  assert.equal(h.state.package, h.state.packages[0]);
  assert.equal(h.state.workspaceSubjectOpen, true);
  assert.equal(h.state.loading, false);
  assert.equal(h.location.href, href);
  assert.equal(h.history.state, entryState);
  assert.equal(h.writes.length, 0);
  assert.match(h.state.queryNotice, /Saved Workspace "My Workspace" failed:/);
  assert.deepEqual(saved, { name: "My Workspace", packet });
  assert.equal(h.context.pendingDemoNavigation, null);
}

for (const failure of ["decode", "decoder-throw", "empty", "acquisition", "view", "selection"] as const) {
  test(`failed ${failure} Open retains the source Workspace/history and focuses the saved identity`, async () => {
    const h = harness();
    if (failure === "decode") h.controls.decodeFailure = "Unsupported packet";
    if (failure === "decoder-throw") h.controls.decodeError = new Error("Decoder unavailable");
    if (failure === "acquisition") h.controls.acquisition = async id => id !== "Beta";
    if (failure === "view") h.controls.share = {
      ...h.controls.share, view: { ...h.controls.share.view, type: "Missing.Type" },
    };
    if (failure === "selection") h.controls.selection = async () => { throw new Error("View unavailable"); };
    const href = h.location.href;
    const entryState = h.history.state;
    const navigation = h.navigationHistory.snapshot();
    h.open(failure === "empty" ? { ...saved, packet: "" } : saved);
    await h.settle();
    assertRetained(h, href, entryState);
    assert.deepEqual(h.navigationHistory.snapshot(), navigation);
    if (failure === "decode" || failure === "decoder-throw" || failure === "empty")
      assert.deepEqual(h.acquisitions, []);
    if (failure === "view") assert.match(h.state.queryNotice, /Missing.Type.*no longer available/);
    h.flushFocus();
    assert.deepEqual(h.focus, [{ kind: "saved-open", name: saved.name, index: 0 }]);
  });
}

test("acquisition retry reopens the retained saved packet through a new transaction", async () => {
  const h = harness();
  h.controls.acquisition = async () => false;
  h.open();
  await h.settle();
  assert.ok(h.state.queryNoticeRetryAction);
  assert.equal(h.writes.length, 0);
  const failedSequence = h.navigationSequence.current();
  h.controls.acquisition = async () => true;
  h.state.queryNoticeRetryAction();
  await h.settle();
  assert.ok(h.navigationSequence.current() > failedSequence);
  assert.deepEqual(h.decoded, [packet, packet]);
  assert.equal(h.state.package?.id, "Beta");
  assert.equal(h.writes.filter(write => write.kind === "push").length, 1);
  h.flushFocus();
  assert.deepEqual(h.focus, ["heading"]);
});

for (const rejected of [false, true]) {
  test(`superseded ${rejected ? "rejected" : "successful"} completion cannot publish, cancel, or focus over its successor`, async () => {
    const h = harness();
    const first = deferred<void>();
    const second = deferred<void>();
    h.controls.selection = () => first.promise;
    h.open();
    await new Promise(resolve => setImmediate(resolve));
    h.controls.selection = () => second.promise;
    h.controls.share = {
      ...sharedState(),
      tabs: sharedState().tabs.map(tab =>
        tab.id === "second" ? { ...tab, source: "Gamma" } : tab),
    };
    h.controls.encodeResult = {
      succeeded: true, packet: "successor-packet", failure: null,
    };
    h.open({ name: "Successor", packet: "successor-packet" });
    await new Promise(resolve => setImmediate(resolve));
    const pending = h.context.pendingDemoNavigation;
    if (rejected) first.reject(new Error("Stale failure"));
    else first.resolve();
    await h.operations[0];
    assert.equal(h.context.pendingDemoNavigation, pending);
    assert.equal(h.state.package?.id, "Gamma");
    assert.equal(h.state.queryNotice, "");
    assert.equal(h.writes.length, 0);
    h.flushFocus();
    assert.deepEqual(h.focus, []);
    second.resolve();
    await h.settle();
    assert.equal(h.writes.filter(write => write.kind === "push").length, 1);
    assert.equal(new URL(h.writes[0]!.url).searchParams.get("w"), "successor-packet");
    assert.equal(h.location.searchParams.get("w"), "successor-packet");
    h.flushFocus();
    assert.deepEqual(h.focus, ["heading"]);
  });
}

test("deferred failure focus does not steal focus after navigation changes", async () => {
  const h = harness();
  h.controls.decodeFailure = "Invalid saved packet";
  h.open();
  await h.settle();
  h.navigationSequence.begin();
  h.flushFocus();
  assert.deepEqual(h.focus, []);
});

test("failed Open uses Workspace fallback only when the saved action is no longer rendered", async () => {
  const h = harness();
  h.controls.decodeFailure = "Invalid saved packet";
  h.controls.savedFocusAvailable = false;
  h.open();
  await h.settle();
  h.flushFocus();
  assert.deepEqual(h.focus, [
    { kind: "saved-open", name: saved.name, index: 0 }, "workspace",
  ]);
});

test("neighboring demo opening retains the shared transactional failure orchestration", async () => {
  const h = harness();
  const href = h.location.href;
  h.controls.acquisition = async () => false;
  h.demo();
  await h.settle();
  assert.equal(h.location.href, href);
  assert.equal(h.writes.length, 0);
  assert.deepEqual(h.state.packages, [sourcePackage]);
  assert.match(h.state.queryNotice, /^Demo failed:/);
  assert.ok(h.state.queryNoticeRetryAction);
  h.flushFocus();
  assert.deepEqual(h.focus, [{ kind: "demo", id: "demo" }]);
});
