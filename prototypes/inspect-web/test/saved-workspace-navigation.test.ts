import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { stripTypeScriptTypes } from "node:module";
import { runInNewContext } from "node:vm";
import test from "node:test";
import { parseSync } from "oxc-parser";
import { createCatalogRequests, type DotnetRelease } from "../src/catalog-requests.ts";
import {
  createPackageComparisonTargets,
  type ComparisonPackage,
} from "../src/package-comparison-targets.ts";
import {
  MAX_WORKSPACE_PACKAGES,
  packageIdentityKey,
  retainWorkspacePackage,
  typeLensesFor,
} from "../src/data.ts";
import type {
  BrowserHomeDemoResolveResult,
  BrowserWorkspaceShareDecodeResult,
  BrowserWorkspaceShareEncodeResult,
  BrowserWorkspaceShareState,
} from "../src/facades/inspect-web-catalog.d.ts";
import type { BrowserPackageSurface } from "../src/facades/inspect-web-package.d.ts";
import {
  invalidateGraphMemberNavigationWork,
  invalidateMemberCallGraphWork,
  memberScopeIsActive,
} from "../src/member-filtering.ts";
import {
  createPackageAcquisition,
  type PackageAcquisitionDependencies,
} from "../src/package-acquisition.ts";
import { workspaceDependencyKey } from "../src/package-inspection.ts";
import {
  isProductHomeDemosPath,
  productHomeDemoLocationHref,
} from "../src/product-home-demos.ts";
import type { SavedWorkspace } from "../src/saved-workspaces.ts";
import type { SpotlightPackageResult } from "../src/spotlight.ts";
import type { WorkspaceFocusTarget } from "../src/workspace-subject.ts";
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
  "errorMessage", "isRecord", "runHomeDemo", "resolveAndRunHomeDemo", "failDemoWorkspaceOpen",
  "addWorkspacePackage", "openWorkspacePackagePicker", "beginSpotlightNavigation",
  "canRestoreWorkbenchFocus", "isTextEntry",
  "retainPackageModel", "packageIdentityEquals", "releasePackageModelCaches",
  "invalidateWorkspaceMembershipViews", "invalidateGraphMemberNavigation",
  "clearWorkspaceOccurrenceView", "clearWorkspacePackages",
  "activatePackage", "defaultAccessibilityFilter", "resetMemberFilters",
]);
const hostFunctions = app.program.body.filter(
  node => node.type === "FunctionDeclaration" && hostNames.has(node.id?.name ?? ""));
assert.equal(hostFunctions.length, hostNames.size);
const acquisitionDeclaration = app.program.body.find(node =>
  node.type === "VariableDeclaration" && node.declarations.some(declaration =>
    declaration.id.type === "Identifier" && declaration.id.name === "packageAcquisition"));
assert.ok(acquisitionDeclaration);
const hostDeclarations = [...hostFunctions, acquisitionDeclaration]
  .map(node => appSource.slice(node.start, node.end)).join("\n");

interface Package extends ComparisonPackage {
  isRuntimePack?: boolean;
  types: { id: string }[];
}

const sourcePackage: Package = {
  id: "Source", version: "1.2.3", activeFramework: "net10.0", types: [],
  source: { kind: "nuget.org" },
};
const packet = "opaque+/packet?name=ignored&x=1#fragment";
const saved = Object.freeze({ name: "My Workspace", packet });

function packageSurface(
  id = "Added.Package", version = "4.5.6", framework = "net10.0",
): BrowserPackageSurface {
  return {
    package: id, version, frameworks: ["net9.0", "net10.0"], activeFramework: framework,
    defaultAssemblyId: "added-core",
    compileLibrary: { status: "Selected", targetFramework: framework, message: null },
    assemblies: [{
      id: "added-core", name: "Added.Core", version: "4.5.6.0",
      culture: null, publicKeyToken: null, asset: `lib/${framework}/Added.Core.dll`,
      publicTypes: 1, publicMembers: 0, platformPack: null,
    }],
    types: [{
      id: "Added.Widget", definitionId: "Added.Widget", queryId: "Added.Widget",
      metadataId: "Added.Widget", name: "Widget", displayName: "Added.Widget",
      namespace: "Added", kind: "class", accessibility: "public", accessibilityId: "public",
      assembly: "Added.Core", assemblyId: "added-core", assemblyName: "Added.Core",
      members: 0, signature: "public class Widget", api: [], platformPack: null,
    }],
    accessibility: [{ id: "public", label: "Public", order: 0, isDefault: true, count: 1 }],
    totalMembers: 0, documents: [], icon: null, inspectionErrors: [], inspectionError: null,
  };
}

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

function resolvedDemo(): BrowserHomeDemoResolveResult {
  const members = sharedState().tabs.map(tab => ({
    kind: "package",
    id: tab.source,
    version: tab.version,
    framework: tab.framework,
    assembly: null,
  }));
  return {
    found: true,
    demo: {
      id: "demo",
      title: "Example demo",
      summary: "Open a two-package workspace.",
      workspaceMembers: members,
      tabs: members.map((member, index) => ({ id: `demo-${index}`, member })),
      focusTabIndex: 1,
      view: { library: null, type: null, memberAnchor: null, memberKey: null, section: null },
    },
  };
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
    selectedOverloadIndex: null as number | null, memberSection: "overview",
    typeFilter: "", namespaceFilter: "", kindFilter: "",
    memberBrowseTypeId: "", memberKindFilter: "all", memberAccessibilityFilter: "all",
    memberTraitFilter: "", memberTextFilter: "",
    requestedPackage: "", requestedVersion: "", requestedFramework: "",
    queryNotice: "", queryNoticeRetryAction: null as (() => void) | null,
    workspaceDependencies: {} as Record<string, unknown>,
    workspaceDependencyErrors: {} as Record<string, string>,
    workspaceDependencyLoads: new Set<string>(),
    dotnetReleases: null as DotnetRelease[] | null, dotnetReleasesLoading: false,
    accessibilityFilter: new Set(["public"]),
    memberAnnotatedEmbedded: null, memberAnnotatedModal: null,
    platformStack: [] as object[], platformRecent: [], recentPackages: [],
    spotlightPkgHits: [], history: [],
    spotlightOpen: false,
    memberCallGraph: null as object | null, memberCallGraphError: "", memberCallGraphKey: "",
    memberCallGraphLoading: false, memberCallGraphExpanding: false, memberCallGraphSeq: 0,
    platformDrillLoading: false, platformDrillError: "",
    graphMemberNavigationSeq: 0, graphMemberNavigationTitle: "", graphMemberNavigationError: "",
    pendingGraphMemberDeepLink: null as object | null,
    workspaceOccurrenceSignature: "", workspaceOccurrenceLoading: false,
    workspaceOccurrences: null as object | null, workspaceOccurrenceError: "",
  };
  const catalogRequests = createCatalogRequests({
    state,
    queryDotnetReleases: async () => [],
    queryPackageVersions: async pkg => ({
      versions: [pkg.version],
      currentVersionInsertionIndex: 0,
      previousVersion: null,
      previousVersionUnavailableReason: null,
    }),
    updatePlatformVersionSelect: () => {},
    updatePackageVersionSelect: () => {},
  });
  const packageComparisonTargets = createPackageComparisonTargets(() => state.packages);
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
  const previousEntries: { url: string; state: unknown }[] = [];
  const frames: (() => void)[] = [];
  const focus: unknown[] = [];
  const effects: string[] = [];
  const decoded: string[] = [];
  const encoded: unknown[] = [];
  const acquisitions: string[] = [];
  const queries: string[][] = [];
  const retained: { packageModel: Package; replacedPackage: Package | null }[] = [];
  const recent: string[][] = [];
  const invalidations: string[] = [];
  const toasts: string[] = [];
  const picker: {
    current: {
      pickResult: (result: SpotlightPackageResult) => void;
      focusAfterDismiss: () => void;
    } | null;
    opens: number;
    resets: number;
  } = { current: null, opens: 0, resets: 0 };
  const operations: Promise<unknown>[] = [];
  const demoResolutions: string[] = [];
  const callGraphRuns: { id: string; navigationSeq: number }[] = [];
  const controls = {
    share: sharedState(),
    encodeResult: {
      succeeded: true, packet, failure: null,
    } as BrowserWorkspaceShareEncodeResult,
    decodeError: null as Error | null,
    decodeFailure: "",
    acquisition: async (_id: string): Promise<boolean> => true,
    queryPackage: async (_id: string, _version: string, _framework: string) => packageSurface(),
    selection: async (): Promise<void> => {},
    resolveHomeDemo: async (_id: string): Promise<BrowserHomeDemoResolveResult> => resolvedDemo(),
    demoHref: productHomeDemoLocationHref,
    callGraph: async (): Promise<void> => {},
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
    if (kind === "push") previousEntries.push({ url: location.href, state: history.state });
    writes.push({ kind, url, state: entryState });
    location.href = new URL(url, location).href;
    history.state = entryState;
  }
  const heading = { tabIndex: 0, focus: () => focus.push("heading") };
  const document = {
    title: "",
    activeElement: null,
    querySelector: (selector: string) => {
      assert.equal(selector, "main h1");
      return heading;
    },
  };
  const context = {
    state, location, history, document, workspaceLocation,
    catalogRequests, packageComparisonTargets,
    navigationSequence, navigationHistory,
    pendingDemoNavigation: null as { navigationSeq: number; destination: string } | null,
    failedWorkspaceUrlState: null, spotlightCache: null as object | null,
    spotlightMemberCache: null as object | null,
    spotlightFocusGeneration: 0, documentFocusGeneration: 0, workspaceOccurrenceRevision: 0,
    HTMLElement: class { isContentEditable = false; },
    URL, URLSearchParams, Error, structuredClone, Set,
    MAX_WORKSPACE_PACKAGES, packageIdentityKey, memberScopeIsActive,
    workspaceDependencyKey, invalidateGraphMemberNavigationWork, invalidateMemberCallGraphWork,
    retainWorkspacePackage: (
      packages: readonly Package[], active: Package | null,
      packageModel: Package, replacedPackage: Package | null,
    ) => {
      retained.push({ packageModel, replacedPackage });
      return retainWorkspacePackage(packages, active, packageModel, replacedPackage);
    },
    createPackageAcquisition,
    inspectPackage: (...coordinate: Parameters<PackageAcquisitionDependencies["queryPackage"]>) => {
      queries.push(coordinate);
      return controls.queryPackage(...coordinate);
    },
    runtimePackPackage: () => null,
    recordRecentPackage: (...coordinate: string[]) => recent.push(coordinate),
    packageInspection: { invalidatePackageResults: () => invalidations.push("package-results") },
    inspectClearWorkspacePackageOccurrences: () => invalidations.push("occurrences"),
    applicationMenuOwnsFocus: () => false,
    showToast: (message: string) => toasts.push(message),
    spotlight: {
      openForPackageAddition: (purpose: NonNullable<typeof picker.current>) => {
        picker.current = purpose;
        picker.opens++;
        state.spotlightOpen = true;
      },
      reset: () => {
        picker.current = null;
        picker.resets++;
        state.spotlightOpen = false;
      },
    },
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
    methodBodyComparison: { dispose: () => {} },
    persistRecentPackages: () => {},
    persistPlatformRecent: () => {},
    refreshPackageStats: () => {},
    clearWorkspaceRouteFailure: () => true,
    resetLocationFilters: () => {},
    currentPackageQueryHandoff: () => false,
    retainFailedWorkspaceUrl: () => false,
    packageDisplayName: (pkg: Package) => pkg.id,
    selectedType: () => null,
    selectedLibraryRequest: () => "asset:retained-library",
    isRuntimePackId: () => false,
    loadPackage: async (
      id: string, version: string, framework: string,
      options: { background: boolean; navigationSeq: number },
    ) => {
      assert.equal(options.background, true);
      acquisitions.push(`${id}@${version}/${framework}`);
      const succeeded = await controls.acquisition(id);
      if (!navigationSequence.isCurrent(options.navigationSeq) || !succeeded) return null;
      const pkg = {
        id, version, activeFramework: framework, types: [],
        source: { kind: "nuget.org" },
      };
      state.packages.push(pkg);
      return pkg;
    },
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
    restoreWorkspaceFocus: (_root: unknown, target: WorkspaceFocusTarget) => {
      focus.push(structuredClone(target));
      return controls.savedFocusAvailable;
    },
    focusWorkspace: () => { focus.push("workspace"); return true; },
    render: (options: { synchronizeUrl?: boolean } = {}) => {
      effects.push("render");
      if (!state.loading && state.package && !state.home && !state.error) {
        if (options.synchronizeUrl !== false) runInNewContext("syncUrl()", context);
        navigationHistory.record();
      }
    },
    engineClient: {
      catalog: {
        resolveHomeDemo: (id: string) => {
          demoResolutions.push(id);
          return controls.resolveHomeDemo(id);
        },
      },
    },
    productHomeDemoLocationHref: (...args: Parameters<typeof productHomeDemoLocationHref>) =>
      controls.demoHref(...args),
    runCallGraphDemo: (id: string, _snapshot: unknown, navigationSeq: number) => {
      callGraphRuns.push({ id, navigationSeq });
      return controls.callGraph();
    },
    inspectEncodeWorkspaceShareState: () => controls.encodeResult,
  };
  runInNewContext(stripTypeScriptTypes(hostDeclarations), context);
  return {
    state, context, controls, location, history, writes, decoded, encoded,
    acquisitions, focus, effects, operations, navigationHistory, navigationSequence,
    queries, retained, recent, invalidations, toasts, picker, previousEntries,
    catalogRequests, packageComparisonTargets,
    demoResolutions, callGraphRuns,
    capture: (): string => {
      const result: unknown = runInNewContext("captureSavedWorkspacePacket()", context);
      assert.ok(typeof result === "string");
      return result;
    },
    open: (entry: SavedWorkspace = saved): void => {
      runInNewContext("openSavedWorkspace(entry)", { ...context, entry });
    },
    demo: (): void => { runInNewContext('runHomeDemo("demo")', context); },
    add: (result: SpotlightPackageResult = {
      kind: "pkg-nuget", hit: { id: "Added.Package" }, ranges: [],
    }): Promise<unknown> => {
      const operation = Promise.resolve<unknown>(runInNewContext(
        "addWorkspacePackage(result)", { ...context, result }));
      operations.push(operation);
      return operation;
    },
    openPicker: (): void => { runInNewContext("openWorkspacePackagePicker()", context); },
    settle: async () => { await Promise.all(operations); },
    flushFocus: () => { for (const frame of frames.splice(0)) frame(); },
  };
}

test("capture uses the original share projection and retains Workspace presentation without effects", () => {
  const h = harness();
  const basis = sharedState();
  h.state.packages = basis.tabs.map(tab => ({
    id: tab.source, version: tab.version!, activeFramework: tab.framework!, types: [],
    source: { kind: "nuget.org" },
  }));
  h.state.package = h.state.packages[1]!;
  h.state.workspaceShareBasis = basis;
  h.state.libraryScope = new Set(["asset:retained-library"]);
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
        activeFramework: "net10.0", isRuntimePack: platform, types: [],
        source: { kind: platform ? "platform" : "nuget.org" } },
      { id: "Beta", version: "5.6.7", activeFramework: "net9.0", types: [],
        source: { kind: "nuget.org" } },
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

for (const failure of ["acquisition", "selection"] as const) {
  test(`failed ${failure} Open restores comparison choices and their Package associations`, async () => {
    const h = harness();
    const other = { ...sourcePackage, id: "Comparison.Target" };
    h.state.packages.push(other);
    await h.catalogRequests.ensurePackageVersions(sourcePackage);
    const inventory = h.catalogRequests.packageVersions(sourcePackage);
    h.packageComparisonTargets.selectDiff(
      sourcePackage, { kind: "exact", version: sourcePackage.version }, inventory);
    h.packageComparisonTargets.selectClone(
      sourcePackage, { kind: "package", package: other });
    if (failure === "acquisition")
      h.controls.acquisition = async id => id !== "Beta";
    else
      h.controls.selection = async () => { throw new Error("View unavailable"); };

    h.open();
    await h.settle();

    const restored = h.state.packages.find(pkg => pkg.id === sourcePackage.id);
    const restoredTarget = h.state.packages.find(pkg => pkg.id === other.id);
    assert.ok(restored);
    assert.ok(restoredTarget);
    assert.notEqual(restored, sourcePackage);
    assert.notEqual(restoredTarget, other);
    assert.equal(h.state.package, restored);
    assert.deepEqual(h.packageComparisonTargets.get(restored).diff, {
      kind: "exact", version: sourcePackage.version,
    });
    const clone = h.packageComparisonTargets.get(restored).clone;
    assert.equal(clone.kind, "package");
    if (clone.kind === "package") assert.equal(clone.package, restoredTarget);
    assert.deepEqual(h.catalogRequests.packageVersions(restored), inventory);
    assert.deepEqual(h.catalogRequests.packageVersions(sourcePackage), { status: "idle" });
  });
}

test("successful saved Open retires comparison settings with the discarded Package models", async () => {
  const h = harness();
  await h.catalogRequests.ensurePackageVersions(sourcePackage);
  h.packageComparisonTargets.selectDiff(sourcePackage, {
    kind: "exact", version: sourcePackage.version,
  }, h.catalogRequests.packageVersions(sourcePackage));
  h.open();
  await h.settle();

  assert.ok(h.state.package);
  assert.deepEqual(h.packageComparisonTargets.get(h.state.package), {
    diff: { kind: "previous" }, clone: { kind: "workspace" },
  });
  assert.deepEqual(h.packageComparisonTargets.get(sourcePackage).diff, { kind: "previous" });
  assert.deepEqual(h.catalogRequests.packageVersions(sourcePackage), { status: "idle" });
});

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

test("demo resolution waits before acquisition and commits its complete location with the same navigation sequence", async () => {
  const h = harness();
  const resolution = deferred<BrowserHomeDemoResolveResult>();
  h.controls.resolveHomeDemo = () => resolution.promise;
  const href = h.location.href;
  const entryState = h.history.state;
  h.demo();
  const sequence = h.navigationSequence.current();
  assert.equal(h.state.loading, true);
  assert.equal(h.state.package, sourcePackage);
  assert.equal(h.location.href, href);
  assert.equal(h.history.state, entryState);
  assert.equal(h.writes.length, 0);
  assert.deepEqual(h.acquisitions, []);
  assert.deepEqual(h.decoded, []);
  assert.deepEqual(h.focus, []);
  assert.equal(h.context.pendingDemoNavigation?.navigationSeq, sequence);

  resolution.resolve(resolvedDemo());
  await h.settle();
  assert.equal(h.navigationSequence.current(), sequence);
  assert.equal(h.state.package?.id, "Beta");
  assert.equal(h.writes.filter(write => write.kind === "push").length, 1);
  assert.equal(h.location.searchParams.get("w"), packet);
  assert.equal(h.context.pendingDemoNavigation, null);
  h.flushFocus();
  assert.deepEqual(h.focus, ["heading"]);
});

for (const failure of ["resolution", "unknown", "projection", "decode"] as const) {
  test(`demo ${failure} failure retains the source location and existing retry/focus policy`, async () => {
    const h = harness();
    const href = h.location.href;
    const entryState = h.history.state;
    if (failure === "resolution")
      h.controls.resolveHomeDemo = async () => { throw new Error("Resolution unavailable"); };
    if (failure === "unknown")
      h.controls.resolveHomeDemo = async () => ({ found: false, demo: null });
    if (failure === "projection")
      h.controls.demoHref = () => { throw new Error("Projection unavailable"); };
    if (failure === "decode")
      h.controls.decodeError = new Error("Decoder unavailable");
    h.demo();
    await h.settle();
    assert.equal(h.location.href, href);
    assert.equal(h.history.state, entryState);
    assert.equal(h.writes.length, 0);
    assert.deepEqual(h.state.packages, [sourcePackage]);
    assert.equal(h.state.loading, false);
    assert.match(h.state.queryNotice, /^Demo failed:/);
    assert.equal(Boolean(h.state.queryNoticeRetryAction), failure === "resolution");
    assert.equal(h.context.pendingDemoNavigation, null);
    assert.deepEqual(h.acquisitions, []);
    assert.deepEqual(h.callGraphRuns, []);
    h.flushFocus();
    assert.deepEqual(h.focus, [{ kind: "demo", id: "demo" }]);
  });
}

test("a demo resolution retry reacquires the result under a new navigation sequence", async () => {
  const h = harness();
  h.controls.resolveHomeDemo = async () => { throw new Error("Try again"); };
  h.demo();
  await h.settle();
  const failedSequence = h.navigationSequence.current();
  assert.ok(h.state.queryNoticeRetryAction);
  h.controls.resolveHomeDemo = async () => resolvedDemo();
  h.state.queryNoticeRetryAction();
  await h.settle();
  assert.ok(h.navigationSequence.current() > failedSequence);
  assert.deepEqual(h.demoResolutions, ["demo", "demo"]);
  assert.equal(h.writes.filter(write => write.kind === "push").length, 1);
  h.flushFocus();
  assert.deepEqual(h.focus, ["heading"]);
});

for (const outcome of ["success", "unknown", "failure"] as const) {
  test(`superseded demo resolution ${outcome} cannot disturb a newer saved-workspace open`, async () => {
    const h = harness();
    const resolution = deferred<BrowserHomeDemoResolveResult>();
    const selection = deferred<void>();
    h.controls.resolveHomeDemo = () => resolution.promise;
    h.demo();
    h.controls.selection = () => selection.promise;
    h.open();
    await new Promise(resolve => setImmediate(resolve));
    const pending = h.context.pendingDemoNavigation;
    const snapshot = structuredClone(h.state);
    const effects = [...h.effects];
    const acquisitions = [...h.acquisitions];
    if (outcome === "failure") resolution.reject(new Error("Stale failure"));
    else resolution.resolve(outcome === "unknown" ? { found: false, demo: null } : resolvedDemo());
    await h.operations[0];
    assert.equal(h.context.pendingDemoNavigation, pending);
    assert.deepEqual(structuredClone(h.state), snapshot);
    assert.deepEqual(h.effects, effects);
    assert.deepEqual(h.acquisitions, acquisitions);
    assert.deepEqual(h.callGraphRuns, []);
    assert.equal(h.writes.length, 0);
    h.flushFocus();
    assert.deepEqual(h.focus, []);
    selection.resolve();
    await h.settle();
    assert.equal(h.writes.filter(write => write.kind === "push").length, 1);
    h.flushFocus();
    assert.deepEqual(h.focus, ["heading"]);
  });
}

test("call-graph demo execution receives the resolution navigation sequence and stays observed", async () => {
  const h = harness();
  const resolution = deferred<BrowserHomeDemoResolveResult>();
  const execution = deferred<void>();
  h.controls.resolveHomeDemo = () => resolution.promise;
  h.controls.demoHref = () => null;
  h.controls.callGraph = () => execution.promise;
  h.demo();
  const sequence = h.navigationSequence.current();
  assert.deepEqual(h.callGraphRuns, []);
  resolution.resolve(resolvedDemo());
  await new Promise(resolve => setImmediate(resolve));
  assert.deepEqual(h.callGraphRuns, [{ id: "demo", navigationSeq: sequence }]);
  assert.equal(h.navigationSequence.current(), sequence);
  assert.equal(h.context.pendingDemoNavigation?.navigationSeq, sequence);
  assert.equal(h.operations.length, 1);
  assert.equal(h.writes.length, 0);
  execution.resolve();
  await h.settle();
  assert.equal(h.context.pendingDemoNavigation, null);
});

function inspectionSelection(h: ReturnType<typeof harness>) {
  const s = h.state;
  return {
    package: s.package, workspaceSubjectOpen: s.workspaceSubjectOpen,
    atPackageRoot: s.atPackageRoot, packageLens: s.packageLens, lens: s.lens,
    selectedTypeId: s.selectedTypeId, selectedMemberKey: s.selectedMemberKey,
    selectedOverloadIndex: s.selectedOverloadIndex, memberSection: s.memberSection,
    memberBrowseTypeId: s.memberBrowseTypeId,
    typeFilter: s.typeFilter, namespaceFilter: s.namespaceFilter, kindFilter: s.kindFilter,
    libraryScope: s.libraryScope, accessibilityFilter: s.accessibilityFilter,
    memberKindFilter: s.memberKindFilter, memberAccessibilityFilter: s.memberAccessibilityFilter,
    memberTraitFilter: s.memberTraitFilter, memberTextFilter: s.memberTextFilter,
  };
}

function seedInspectionSelection(h: ReturnType<typeof harness>) {
  Object.assign(h.state, {
    selectedTypeId: "Source.Widget", selectedMemberKey: "Run",
    selectedOverloadIndex: 1, memberSection: "source", memberBrowseTypeId: "Source.Widget",
    typeFilter: "Widget", namespaceFilter: "Source", kindFilter: "class",
    libraryScope: new Set(["Source.Core"]), accessibilityFilter: new Set(["public", "internal"]),
    memberKindFilter: "method", memberAccessibilityFilter: "public",
    memberTraitFilter: "static", memberTextFilter: "Run",
  });
}

function fillWorkspace(h: ReturnType<typeof harness>, count = MAX_WORKSPACE_PACKAGES) {
  h.state.packages = [sourcePackage, ...Array.from({ length: count - 1 }, (_, index) => ({
    id: `Resident.${index}`, version: "1.0.0", activeFramework: "net10.0", types: [],
    source: { kind: "nuget.org" },
  }))];
  for (const pkg of h.state.packages) {
    h.state.workspaceDependencies[workspaceDependencyKey(pkg)] = { resident: pkg.id };
  }
}

function assertSourceHistory(
  h: ReturnType<typeof harness>, href: string, entryState: unknown,
) {
  assert.equal(h.location.href, href);
  assert.equal(h.history.state, entryState);
  assert.deepEqual(h.writes, []);
  assert.deepEqual(h.previousEntries, []);
}

test("Add appends the resolved coordinate, preserves inspection, invalidates membership views, and shares the same scope in URL and Save", async () => {
  const h = harness();
  h.state.packages.unshift({
    id: "Earlier", version: "2.0.0", activeFramework: "net9.0", types: [],
    source: { kind: "nuget.org" },
  });
  seedInspectionSelection(h);
  const previous = [...h.state.packages];
  const selection = inspectionSelection(h);
  const href = h.location.href;
  const entryState = h.history.state;
  Object.assign(h.state, {
    workspaceShareBasis: sharedState(),
    memberCallGraph: { nodes: ["old"] }, memberCallGraphError: "old", memberCallGraphKey: "old",
    memberCallGraphLoading: true, memberCallGraphExpanding: true,
    platformDrillLoading: true, platformDrillError: "old", platformStack: [{ old: true }],
    graphMemberNavigationTitle: "old", graphMemberNavigationError: "old",
    pendingGraphMemberDeepLink: { old: true },
    workspaceOccurrenceSignature: "old", workspaceOccurrenceLoading: true,
    workspaceOccurrences: { old: true }, workspaceOccurrenceError: "old",
  });
  h.context.spotlightCache = { old: true };
  h.context.spotlightMemberCache = { old: true };
  const query = deferred<BrowserPackageSurface>();
  h.controls.queryPackage = () => query.promise;
  const operation = h.add();
  assert.equal(h.state.loading, true);
  assertSourceHistory(h, href, entryState);
  assert.equal(h.retained.length, 0);
  assert.deepEqual(h.invalidations, []);
  query.resolve(packageSurface());
  await operation;

  assert.deepEqual(h.queries, [["Added.Package", "latest", ""]]);
  assert.deepEqual(h.state.packages.slice(0, 2), previous);
  previous.forEach((pkg, index) => assert.equal(h.state.packages[index], pkg));
  assert.equal(h.state.package, sourcePackage);
  assert.deepEqual(inspectionSelection(h), selection);
  assert.equal(h.state.packages.length, 3);
  const added = h.state.packages[2]!;
  assert.equal(h.retained.length, 1);
  assert.equal(h.retained[0]!.packageModel, added);
  assert.equal(h.retained[0]!.replacedPackage, null);
  assert.equal(added.types[0]?.id, "Added.Widget");
  assert.deepEqual(h.recent, [["Added.Package", "4.5.6", "net10.0"]]);
  assert.equal(h.state.loading, false);
  assert.equal(h.state.queryNotice, "");
  assert.equal(h.state.queryNoticeRetryAction, null);
  assert.equal(h.state.workspaceShareBasis, null);
  assert.equal(h.state.memberCallGraphSeq, 1);
  assert.equal(h.state.graphMemberNavigationSeq, 1);
  for (const value of [
    h.state.memberCallGraph, h.state.pendingGraphMemberDeepLink, h.state.workspaceOccurrences,
    h.context.spotlightCache, h.context.spotlightMemberCache,
  ]) assert.equal(value, null);
  for (const value of [
    h.state.memberCallGraphLoading, h.state.memberCallGraphExpanding,
    h.state.platformDrillLoading, h.state.workspaceOccurrenceLoading,
  ]) assert.equal(value, false);
  for (const value of [
    h.state.memberCallGraphKey, h.state.memberCallGraphError, h.state.platformDrillError,
    h.state.graphMemberNavigationTitle, h.state.graphMemberNavigationError,
    h.state.workspaceOccurrenceSignature, h.state.workspaceOccurrenceError,
  ]) assert.equal(value, "");
  assert.deepEqual(Array.from(h.state.platformStack), []);
  assert.deepEqual(h.invalidations, ["occurrences", "package-results"]);
  assert.equal(h.context.workspaceOccurrenceRevision, 1);
  assert.equal(h.capture(), packet);
  assert.ok(h.encoded.length >= 2);
  for (const projection of h.encoded) assert.deepEqual(projection, {
    tabs: [
      { id: "t0", kind: "package", source: "Earlier", version: "2.0.0",
        framework: "net9.0", runtimeIdentifier: null },
      { id: "t1", kind: "package", source: "Source", version: "1.2.3",
        framework: "net10.0", runtimeIdentifier: null },
      { id: "t2", kind: "package", source: "Added.Package", version: "4.5.6",
        framework: "net10.0", runtimeIdentifier: null },
    ],
    contexts: [
      { id: "g0", tabIds: ["t0"] }, { id: "g1", tabIds: ["t1"] }, { id: "g2", tabIds: ["t2"] },
    ],
    activeTabId: "t1", selectedContextId: "g1",
    view: { lens: null, type: null, memberAnchor: null, memberSignature: null,
      section: null, libraries: [] },
  });
  assert.equal(h.location.pathname, "/");
  assert.equal(h.location.hash, "#workspace");
  assert.equal(h.location.searchParams.get("w"), packet);
  assert.equal(h.writes.filter(write => write.kind === "push").length, 1);
  assert.deepEqual(h.previousEntries, [{ url: href, state: entryState }]);
  assert.deepEqual(saved, { name: "My Workspace", packet });
  assert.equal(h.context.pendingDemoNavigation, null);
  h.flushFocus();
  assert.deepEqual(h.focus, ["heading"]);
});

test("Add to an empty Workspace activates its first resolved coordinate and stays on Workspace", async () => {
  const h = harness();
  h.state.packages = [];
  h.state.package = null;
  h.location.href = "https://inspect.test/demos";
  await h.add();
  assert.equal(h.state.packages.length, 1);
  assert.equal(h.state.package, h.state.packages[0]);
  assert.equal(h.state.packages[0]?.id, "Added.Package");
  assert.deepEqual([
    h.state.requestedPackage, h.state.requestedVersion, h.state.requestedFramework,
  ], ["Added.Package", "4.5.6", "net10.0"]);
  assert.deepEqual([...h.state.accessibilityFilter], ["public"]);
  assert.equal(h.state.workspaceSubjectOpen, true);
  assert.equal(h.state.atPackageRoot, true);
  assert.equal(h.state.loading, false);
  assert.equal(h.retained.length, 1);
  assert.equal(h.location.pathname, "/");
  assert.equal(h.location.hash, "#workspace");
  assert.equal(h.location.searchParams.get("w"), h.capture());
  h.flushFocus();
  assert.deepEqual(h.focus, ["heading"]);
});

for (const atCap of [false, true]) {
  test(`Add of an inactive loaded exact duplicate ${atCap ? "at capacity" : "below capacity"} never queries, replaces, or activates`, async () => {
    const h = harness();
    fillWorkspace(h, atCap ? MAX_WORKSPACE_PACKAGES : 2);
    seedInspectionSelection(h);
    const previous = [...h.state.packages];
    const selection = inspectionSelection(h);
    const href = h.location.href;
    const entryState = h.history.state;
    const duplicate = previous[1]!;
    await h.add({ kind: "pkg-loaded", pkg: {
      ...duplicate, id: duplicate.id.toUpperCase(), activeFramework: "NET10.0",
    }, ranges: [] });
    assert.deepEqual(h.queries, []);
    assert.deepEqual(h.retained, []);
    assert.deepEqual(h.recent, []);
    assert.deepEqual(h.invalidations, []);
    assert.deepEqual(h.state.packages, previous);
    previous.forEach((pkg, index) => assert.equal(h.state.packages[index], pkg));
    assert.deepEqual(inspectionSelection(h), selection);
    assert.equal(h.state.package, sourcePackage);
    assertSourceHistory(h, href, entryState);
    assert.deepEqual(h.toasts, [`${duplicate.id} is already in Workspace.`]);
    assert.equal(h.state.queryNotice, "");
    h.flushFocus();
    assert.deepEqual(h.focus, ["heading"]);
  });
}

test("Add at the 12-coordinate cap refuses visibly before querying or evicting", async () => {
  const h = harness();
  assert.equal(MAX_WORKSPACE_PACKAGES, 12);
  fillWorkspace(h);
  seedInspectionSelection(h);
  const previous = [...h.state.packages];
  const dependencies = structuredClone(h.state.workspaceDependencies);
  const selection = inspectionSelection(h);
  const href = h.location.href;
  const entryState = h.history.state;
  await h.add();
  assert.deepEqual(h.queries, []);
  assert.deepEqual(h.retained, []);
  assert.deepEqual(h.recent, []);
  assert.deepEqual(h.invalidations, []);
  assert.deepEqual(h.state.packages, previous);
  previous.forEach((pkg, index) => assert.equal(h.state.packages[index], pkg));
  assert.deepEqual(h.state.workspaceDependencies, dependencies);
  assert.deepEqual(inspectionSelection(h), selection);
  assertSourceHistory(h, href, entryState);
  assert.match(h.state.queryNotice, /at most 12 coordinates.*Remove a package/);
  assert.equal(h.state.queryNoticeRetryAction, null);
  h.flushFocus();
  assert.deepEqual(h.focus, [{ kind: "add-package" }]);
});

test("Add whose last slot fills during query refuses before retention and preserves the independently admitted member", async () => {
  const h = harness();
  fillWorkspace(h, MAX_WORKSPACE_PACKAGES - 1);
  seedInspectionSelection(h);
  const selection = inspectionSelection(h);
  const href = h.location.href;
  const entryState = h.history.state;
  const query = deferred<BrowserPackageSurface>();
  h.controls.queryPackage = id => id === "Added.Package"
    ? query.promise : Promise.resolve(packageSurface(id, "8.9.0", "net9.0"));
  const operation = h.add();
  const independent: unknown = runInNewContext(
    'packageAcquisition.loadPackage({ packageId: "Arrived", version: "8.9.0", framework: "net9.0" })',
    h.context);
  const arrived = await independent;
  const admitted = [...h.state.packages];
  assert.equal(admitted.length, 12);
  assert.equal(admitted[11], arrived);
  const dependencies = structuredClone(h.state.workspaceDependencies);
  query.resolve(packageSurface());
  await operation;
  assert.deepEqual(h.queries, [
    ["Added.Package", "latest", ""], ["Arrived", "8.9.0", "net9.0"],
  ]);
  assert.equal(h.retained.length, 1);
  assert.equal(h.retained[0]!.packageModel, arrived);
  assert.deepEqual(h.recent, [["Arrived", "8.9.0", "net9.0"]]);
  assert.deepEqual(h.state.packages, admitted);
  admitted.forEach((pkg, index) => assert.equal(h.state.packages[index], pkg));
  assert.deepEqual(h.state.workspaceDependencies, dependencies);
  assert.deepEqual(inspectionSelection(h), selection);
  assert.deepEqual(h.invalidations, []);
  assertSourceHistory(h, href, entryState);
  assert.match(h.state.queryNotice, /Adding Added\.Package failed:.*at most 12 coordinates/);
  assert.ok(h.state.queryNoticeRetryAction);
  assert.equal(h.state.loading, false);
  assert.equal(h.context.pendingDemoNavigation, null);
  h.flushFocus();
  assert.deepEqual(h.focus, [{ kind: "add-package" }]);
});

for (const source of ["/?package=Source&w=source-packet#workspace", "/demos?keep=1"]) {
  test(`failed Add from ${source} keeps source history, focuses Add, and retries successfully`, async () => {
    const h = harness();
    seedInspectionSelection(h);
    h.location.href = new URL(source, h.location).href;
    const href = h.location.href;
    const entryState = h.history.state;
    const selection = inspectionSelection(h);
    const navigation = h.navigationHistory.snapshot();
    h.controls.queryPackage = async () => { throw new Error("Package service unavailable"); };
    await h.add();
    assertSourceHistory(h, href, entryState);
    assert.deepEqual(h.navigationHistory.snapshot(), navigation);
    assert.equal(h.state.package, sourcePackage);
    assert.deepEqual(h.state.packages, [sourcePackage]);
    assert.deepEqual(inspectionSelection(h), selection);
    assert.deepEqual(h.retained, []);
    assert.deepEqual(h.recent, []);
    assert.deepEqual(h.invalidations, []);
    assert.equal(h.state.loading, false);
    assert.equal(h.state.spotlightOpen, false);
    assert.equal(h.picker.opens, 0);
    assert.match(h.state.queryNotice, /Adding Added\.Package failed: Package service unavailable/);
    assert.ok(h.state.queryNoticeRetryAction);
    assert.equal(h.context.pendingDemoNavigation, null);
    h.flushFocus();
    assert.deepEqual(h.focus, [{ kind: "add-package" }]);

    const failedSequence = h.navigationSequence.current();
    h.controls.queryPackage = async () => packageSurface();
    h.state.queryNoticeRetryAction();
    await h.settle();
    assert.ok(h.navigationSequence.current() > failedSequence);
    assert.deepEqual(h.queries, [
      ["Added.Package", "latest", ""], ["Added.Package", "latest", ""],
    ]);
    assert.equal(h.state.packages.length, 2);
    assert.deepEqual(inspectionSelection(h), selection);
    assert.equal(h.state.queryNotice, "");
    assert.equal(h.state.queryNoticeRetryAction, null);
    assert.equal(h.retained.length, 1);
    assert.equal(h.location.pathname, "/");
    assert.equal(h.location.hash, "#workspace");
    assert.equal(h.location.searchParams.get("w"), packet);
    assert.deepEqual(h.previousEntries, [{ url: href, state: entryState }]);
    h.flushFocus();
    assert.deepEqual(h.focus, [{ kind: "add-package" }, "heading"]);
  });
}

for (const rejected of [false, true]) {
  test(`superseded Add ${rejected ? "failure" : "success"} cannot retain, overwrite, cancel, or focus over the next Add`, async () => {
    const h = harness();
    const first = deferred<BrowserPackageSurface>();
    const second = deferred<BrowserPackageSurface>();
    h.controls.queryPackage = id => id === "Added.Package" ? first.promise : second.promise;
    const stale = h.add();
    const successor = h.add({ kind: "pkg-nuget", hit: { id: "Successor" }, ranges: [] });
    const pending = h.context.pendingDemoNavigation;
    const successorState = structuredClone(h.state);
    if (rejected) first.reject(new Error("Stale query failed"));
    else first.resolve(packageSurface());
    await stale;
    assert.equal(h.context.pendingDemoNavigation, pending);
    assert.deepEqual(h.state, successorState);
    assert.equal(h.retained.length, 0);
    assert.deepEqual(h.recent, []);
    assert.deepEqual(h.invalidations, []);
    assert.equal(h.writes.length, 0);
    h.flushFocus();
    assert.deepEqual(h.focus, []);
    second.resolve(packageSurface("Successor", "7.8.9", "net9.0"));
    await successor;
    assert.deepEqual(h.state.packages.map(pkg => pkg.id), ["Source", "Successor"]);
    assert.equal(h.retained.length, 1);
    assert.equal(h.retained[0]!.packageModel.id, "Successor");
    assert.equal(h.writes.filter(write => write.kind === "push").length, 1);
    assert.equal(h.location.hash, "#workspace");
    assert.equal(h.state.queryNotice, "");
    h.flushFocus();
    assert.deepEqual(h.focus, ["heading"]);
  });
}

for (const rejected of [false, true]) {
  test(`Add ${rejected ? "failure" : "success"} arriving after saved Open cannot overwrite its navigation or steal focus`, async () => {
    const h = harness();
    const query = deferred<BrowserPackageSurface>();
    h.controls.queryPackage = () => query.promise;
    const stale = h.add();
    h.open();
    await h.operations[1];
    h.flushFocus();
    const successorState = structuredClone(h.state);
    const writes = structuredClone(h.writes);
    const href = h.location.href;
    const entryState = h.history.state;
    const focus = structuredClone(h.focus);
    if (rejected) query.reject(new Error("Stale query failed"));
    else query.resolve(packageSurface());
    await stale;
    // Saved Open installs VM-created collections; compare both snapshots in this realm.
    assert.deepEqual(structuredClone(h.state), successorState);
    assert.deepEqual(h.writes, writes);
    assert.equal(h.location.href, href);
    assert.equal(h.history.state, entryState);
    assert.deepEqual(h.retained, []);
    assert.deepEqual(h.recent, []);
    h.flushFocus();
    assert.deepEqual(h.focus, focus);
    assert.deepEqual(h.focus, ["heading"]);
  });
}

test("Add serialization failure after retention restores source membership, selection, and history", async () => {
  const h = harness();
  seedInspectionSelection(h);
  const selection = structuredClone(inspectionSelection(h));
  const previous = structuredClone(h.state.packages);
  const navigation = h.navigationHistory.snapshot();
  h.location.href = "https://inspect.test/demos?keep=1";
  const href = h.location.href;
  const entryState = h.history.state;
  h.controls.encodeResult = {
    succeeded: false, packet: null,
    failure: { kind: "InvalidShape", path: "workspace", message: "Cannot serialize Workspace" },
  };
  await h.add();
  assert.equal(h.retained.length, 1);
  assert.equal(h.retained[0]!.packageModel.id, "Added.Package");
  assert.deepEqual(h.state.packages, previous);
  assert.equal(h.state.package, h.state.packages[0]);
  assert.deepEqual(inspectionSelection(h), selection);
  assert.deepEqual(h.navigationHistory.snapshot(), navigation);
  assertSourceHistory(h, href, entryState);
  assert.match(h.state.queryNotice, /Adding Added\.Package failed: Cannot serialize Workspace/);
  assert.ok(h.state.queryNoticeRetryAction);
  assert.equal(h.state.loading, false);
  assert.equal(h.context.pendingDemoNavigation, null);
  h.flushFocus();
  assert.deepEqual(h.focus, [{ kind: "add-package" }]);
});

test("deferred Add failure focus is discarded when navigation changes before the next frame", async () => {
  const h = harness();
  h.controls.queryPackage = async () => { throw new Error("Package service unavailable"); };
  await h.add();
  h.navigationSequence.begin();
  h.flushFocus();
  assert.deepEqual(h.focus, []);
});

for (const [result, expected] of [
  [{ kind: "pkg-recent", entry: { id: "Recent", version: "3.2.1", framework: "net9.0" },
    ranges: [] }, ["Recent", "3.2.1", "net9.0"]],
  [{ kind: "pkg-recent", entry: { id: "Recent" }, ranges: [] }, ["Recent", "latest", ""]],
  [{ kind: "pkg-nuget", hit: { id: "Found", version: "6.7.8" }, ranges: [] },
    ["Found", "6.7.8", ""]],
] satisfies [SpotlightPackageResult, string[]][]) {
  test(`Workspace picker routes ${result.kind} ${expected.join("/")} through Add acquisition`, async () => {
    const h = harness();
    const href = h.location.href;
    h.openPicker();
    assert.equal(h.picker.opens, 1);
    assert.equal(h.state.spotlightOpen, true);
    assert.equal(h.location.href, href);
    assert.deepEqual(h.queries, []);
    assert.ok(h.picker.current);
    h.picker.current.pickResult(result);
    await h.settle();
    assert.deepEqual(h.queries, [expected]);
    assert.equal(h.picker.current, null);
    assert.equal(h.picker.resets, 1);
    assert.equal(h.state.spotlightOpen, false);
    assert.equal(h.state.packages.length, 2);
    assert.equal(h.retained.length, 1);
    h.flushFocus();
    assert.deepEqual(h.focus, ["heading"]);
  });
}

test("Workspace picker Cancel restores Add focus without acquiring or navigating", () => {
  const h = harness();
  const href = h.location.href;
  const entryState = h.history.state;
  h.openPicker();
  assert.ok(h.picker.current);
  const dismiss = h.picker.current.focusAfterDismiss;
  h.context.spotlight.reset();
  dismiss();
  h.flushFocus();
  assert.deepEqual(h.focus, [{ kind: "add-package" }]);
  assert.deepEqual(h.queries, []);
  assert.deepEqual(h.retained, []);
  assertSourceHistory(h, href, entryState);
});

test("Workspace picker refuses entry while the Workspace is not ready", () => {
  for (const unavailable of [
    { engineReady: false }, { loading: true }, { error: "Engine unavailable" },
  ]) {
    const h = harness();
    Object.assign(h.state, unavailable);
    h.openPicker();
    assert.equal(h.picker.opens, 0);
    assert.deepEqual(h.queries, []);
    assert.deepEqual(h.writes, []);
    assert.deepEqual(h.toasts, ["Workspace is not ready to add a package."]);
  }
});
