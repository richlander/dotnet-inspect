import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { stripTypeScriptTypes } from "node:module";
import { runInNewContext } from "node:vm";
import test from "node:test";
import { parseSync } from "oxc-parser";
import {
  assemblyDescriptorForType,
  memberRequestKey,
  packageIdentityKey,
} from "../src/data.ts";
import {
  createCallGraphInspectionCoordinator,
  type CallGraphInspectionState,
  type CallGraphWorkspacePackage,
  type MemberCallGraphRequest,
  type PlatformDrillRequest,
} from "../src/call-graph-inspection.ts";
import type { BrowserCallGraph } from "../src/facades/inspect-web-call-graph.d.ts";
import {
  invalidateGraphMemberNavigationWork,
  invalidateMemberCallGraphWork,
  memberScopeIsActive,
  selectedConcreteOverload,
} from "../src/member-filtering.ts";
import {
  createPackageRemoval,
  type PackageRemovalState,
} from "../src/package-removal.ts";
import {
  browserCreatedCallGraphTabIds,
  createNavigationSequence,
  selectedBrowserCallGraphPackageTabIds,
  workspaceShareTabsMatchResolved,
} from "../src/workspace-navigation.ts";

const alpha = { id: "Alpha", version: "1.0.0", activeFramework: "net10.0" };
const beta = { ...alpha, id: "Beta" };
const otherAlpha = { ...alpha, version: "2.0.0" };

function harness(packages = [alpha, beta, otherAlpha]) {
  const state: PackageRemovalState<typeof alpha> = {
    packages,
    package: packages[0] ?? null,
    recentPackages: [alpha, beta].map(pkg => ({
      id: pkg.id, version: pkg.version, framework: pkg.activeFramework,
    })),
  };
  let stored = JSON.stringify(state.recentPackages);
  let failStorage = false;
  const released: typeof alpha[] = [];
  const activated: (typeof alpha | null)[] = [];
  const removal = createPackageRemoval({
    state,
    persistRecent: entries => {
      if (failStorage) throw new Error("Storage is unavailable");
      stored = JSON.stringify(entries);
    },
    activate: next => {
      activated.push(next);
      state.package = next;
    },
    release: pkg => released.push(pkg),
  });
  return {
    state, removal, released, activated,
    persisted: () => JSON.parse(stored) as unknown,
    failStorage: () => { failStorage = true; },
  };
}

test("forgetting a recent ID persists without changing loaded packages", () => {
  const h = harness();
  h.removal.forgetRecent("aLPHa");
  assert.deepEqual(h.persisted(), [
    { id: "Beta", version: "1.0.0", framework: "net10.0" },
  ]);
  assert.deepEqual(h.state.packages, [alpha, beta, otherAlpha]);
  assert.equal(h.state.package, alpha);
  assert.deepEqual(h.released, []);
});

test("removing an inactive coordinate preserves active selection and other versions", () => {
  const h = harness();
  h.removal.removeLoaded(packageIdentityKey(otherAlpha));
  assert.deepEqual(h.state.packages, [alpha, beta]);
  assert.equal(h.state.package, alpha);
  assert.deepEqual(h.activated, []);
  assert.deepEqual(h.released, [otherAlpha]);
  assert.equal(h.state.recentPackages.some(entry => entry.id === "Alpha"), false);
});

test("active and last removal use the existing successor and release each removed model", () => {
  const h = harness([alpha, beta]);
  h.removal.removeLoaded(packageIdentityKey(alpha));
  assert.equal(h.state.package, beta);
  h.removal.removeLoaded(packageIdentityKey(beta));
  assert.equal(h.state.package, null);
  assert.deepEqual(h.state.packages, []);
  assert.deepEqual(h.persisted(), []);
  assert.deepEqual(h.released, [alpha, beta]);
});

test("storage failure preserves membership and history without running removal effects", () => {
  const h = harness();
  const before = structuredClone(h.state);
  h.failStorage();
  assert.throws(() => h.removal.forgetRecent("Alpha"), /Storage/);
  assert.throws(() => h.removal.removeLoaded(packageIdentityKey(alpha)), /Storage/);
  assert.deepEqual(h.state, before);
  assert.deepEqual(h.activated, []);
  assert.deepEqual(h.released, []);
});

test("Platform and missing coordinates fail visibly rather than removing another package", () => {
  const platform = { ...beta, isRuntimePack: true };
  const h = harness([alpha, platform]);
  assert.throws(() => h.removal.removeLoaded(packageIdentityKey(platform)), /no longer removable/);
  assert.throws(() => h.removal.removeLoaded("missing"), /no longer removable/);
  assert.deepEqual(h.state.packages, [alpha, platform]);
});

const appSource = readFileSync(new URL("../src/dotnet-inspect.ts", import.meta.url), "utf8");
const app = parseSync("dotnet-inspect.ts", appSource);
const hostNames = new Set([
  "activateAfterPackageRemoval", "finishPackageRemoval", "activatePackage",
  "packageIdentityEquals", "defaultAccessibilityFilter", "scope",
]);
const hostDeclarations = app.program.body
  .filter(node => node.type === "FunctionDeclaration" && hostNames.has(node.id?.name ?? ""))
  .map(node => appSource.slice(node.start, node.end)).join("\n");

for (const home of [true, false]) {
  test(`original application removal handlers preserve ${home ? "Home" : "Workspace"} through last removal`, () => {
    const state = {
      ...graphInspectionState(),
      home, packages: [alpha, beta], package: alpha as typeof alpha | null,
      workspaceSubjectOpen: !home, atPackageRoot: false,
      dependenciesGroupIndex: 2, selectedTypeId: "Old.Type",
      selectedMemberKey: "Old.Member", selectedOverloadIndex: 3,
      memberBrowseTypeId: "Old.Type", accessibilityFilter: new Set<string>(),
      workspaceShareBasis: { previous: true },
    };
    const locations: string[] = [];
    const historyState = { __entryId: "workspace-entry" };
    const replacementStates: unknown[] = [];
    const released: typeof alpha[] = [];
    const effects: string[] = [];
    const context = {
      state, packageIdentityKey, next: beta, first: alpha,
      spotlightCache: {}, spotlightMemberCache: {},
      resetLocationFilters: () => effects.push("filters"),
      resetMemberFilters: () => {},
      resetMemberSectionState: () => effects.push("member"),
      navigationSequence: { begin: () => effects.push("cancel") },
      invalidateGraphMemberNavigation: () => {},
      invalidateMemberCallGraphWork,
      clearWorkspaceOccurrenceView: () => effects.push("occurrences"),
      packageInspection: { invalidatePackageResults: () => effects.push("results") },
      releasePackageModelCaches: (pkg: typeof alpha) => released.push(pkg),
      history: { state: historyState },
      workspaceLocation: {
        replace: (url: string, entryState: unknown) => {
          locations.push(url);
          replacementStates.push(entryState);
        },
      },
      render: () => effects.push("render"),
    };
    runInNewContext(stripTypeScriptTypes(`${hostDeclarations}
      state.packages = [next];
      activateAfterPackageRemoval(next);
      finishPackageRemoval(first);
      state.packages = [];
      activateAfterPackageRemoval(null);
      finishPackageRemoval(next);
    `), context);
    assert.equal(state.home, home);
    assert.equal(state.package, null);
    assert.equal(state.workspaceSubjectOpen, !home);
    assert.equal(state.dependenciesGroupIndex, null);
    assert.equal(state.selectedTypeId, "");
    assert.equal(state.selectedMemberKey, "");
    assert.equal(state.selectedOverloadIndex, null);
    assert.equal(state.workspaceShareBasis, null);
    assert.deepEqual(locations, home ? [] : ["/demos"]);
    assert.deepEqual(replacementStates, home ? [] : [historyState]);
    assert.deepEqual(released, [alpha, beta]);
    assert.equal(effects.filter(effect => effect === "render").length, 2);
  });
}

function graphInspectionState(): CallGraphInspectionState {
  return {
    memberCallGraph: null,
    memberCallGraphLoading: false,
    memberCallGraphError: "",
    graphMemberNavigationError: "",
    memberCallGraphKey: "",
    memberCallGraphExpanding: false,
    memberCallGraphSeq: 0,
    platformStack: [],
    platformDrillLoading: false,
    platformDrillError: "",
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

function removalGraph(packages: readonly typeof alpha[]): BrowserCallGraph {
  const node = {
    label: "Alpha.Widget.Run",
    status: "Analyzed",
    inLoop: false,
    source: null,
    children: [],
    assembly: "Alpha.dll",
    typeFullName: "Alpha.Widget",
    memberName: "Run",
  };
  return {
    mermaid: packages.map(pkg => pkg.id).join(" --> "),
    callers: {
      ...node,
      children: packages.filter(pkg => pkg.id !== alpha.id).map(pkg => ({
        ...node, label: `${pkg.id}.Caller.Run`, assembly: `${pkg.id}.dll`,
      })),
    },
    callees: node,
    scope: {
      packages: packages.length,
      assemblies: packages.length,
      callerAssemblies: packages.length,
      calleeScope: "target assembly",
    },
    targets: [],
    diagnostics: {
      incompleteNodes: 0,
      incompleteEdges: 0,
      bindingIdentityConflicts: 0,
      hasUnexploredTraversalBoundary: false,
      hasAnalysisFailureBoundary: false,
      isIncomplete: false,
    },
    noBody: false,
  };
}

const graphHostNames = new Set([
  "finishPackageRemoval", "invalidateGraphMemberNavigation",
  "loadSelectedMemberCallGraph", "memberRequestSignature", "memberRequestIsCurrent",
  "selectedCallGraphWorkspacePackages", "capturedShareTabs", "resolvedWorkspaceShareTabs",
  "currentPackage", "selectedType", "selectedMember", "memberGroups", "scope",
]);
const graphHostDeclarations = app.program.body
  .filter(node =>
    node.type === "FunctionDeclaration" && graphHostNames.has(node.id?.name ?? "")
    || node.type === "VariableDeclaration"
      && node.declarations.some(declaration =>
        declaration.id.type === "Identifier" && declaration.id.name === "packageRemoval"))
  .map(node => appSource.slice(node.start, node.end)).join("\n");

function graphRemovalHarness() {
  const type = {
    id: "T:Alpha.Widget", assembly: "Alpha.dll",
    api: [0, 1].map(index => ({
      kind: "Method", name: "Run", signature: `void Run(${index ? "int value" : ""})`,
      graphSelectorKey: `Run|${index ? "System.Int32" : ""}`,
      metadataToken: 0x06000001 + index,
    })),
  };
  const active = { ...alpha, types: [type], assemblies: [] };
  const state = {
    ...graphInspectionState(),
    packages: [active, { ...beta, types: [], assemblies: [] }], package: active,
    recentPackages: [alpha, beta].map(pkg => ({
      id: pkg.id, version: pkg.version, framework: pkg.activeFramework,
    })),
    home: false, workspaceSubjectOpen: false, atPackageRoot: false,
    lens: "api", selectedTypeId: type.id, selectedMemberKey: "Method:Run",
    memberBrowseTypeId: type.id, selectedOverloadIndex: 1, memberSection: "call-graph",
    selectedBodyTarget: { metadataToken: 0x06000002, selectorKey: "Run|System.Int32" },
    workspaceShareBasis: null,
    graphMemberNavigationSeq: 0, graphMemberNavigationTitle: "",
    pendingGraphMemberDeepLink: null,
  };
  const local = removalGraph([alpha]);
  const full = removalGraph([alpha, beta]);
  const expansionStarted = deferred<void>();
  const expansion = deferred<BrowserCallGraph>();
  const platformStarted = deferred<void>();
  const platform = deferred<BrowserCallGraph>();
  let deferExpansion = false;
  let deferPlatform = false;
  const queries: { request: MemberCallGraphRequest; workspace: CallGraphWorkspacePackage[] }[] = [];
  const rendered: (BrowserCallGraph | null)[] = [];
  const pending: Promise<void>[] = [];
  const selection = () => ({
    package: state.package,
    type: state.selectedTypeId,
    member: state.selectedMemberKey,
    overload: state.selectedOverloadIndex,
    body: state.selectedBodyTarget,
    browseType: state.memberBrowseTypeId,
    section: state.memberSection,
    lens: state.lens,
    atPackageRoot: state.atPackageRoot,
  });
  const render = () => {
    rendered.push(state.platformStack.at(-1)?.graph ?? state.memberCallGraph);
  };
  const coordinator = createCallGraphInspectionCoordinator({
    state,
    queryWorkspace: async (request, workspace) => {
      queries.push({ request, workspace: structuredClone(workspace) });
      if (!workspace.length) return local;
      if (deferExpansion) {
        expansionStarted.resolve(undefined);
        return expansion.promise;
      }
      return full;
    },
    queryPlatform: async () => {
      if (!deferPlatform) return removalGraph([alpha]);
      platformStarted.resolve(undefined);
      return platform.promise;
    },
    describeError: String,
    render,
    renderPreservingMemberFocus: () => {
      render();
      return {
        selector: "", dataTarget: null, selection: null,
        navigationScope: null, navigationSelection: null, navigationScrollTop: null,
        focusLost: false,
      };
    },
    renderCallGraph: async () => render(),
    nextPaint: async () => {},
    refreshPackageStats: () => {},
    patchCallGraphSection: render,
  });
  let host!: { load(): Promise<void>; remove(key: string): void };
  runInNewContext(
    stripTypeScriptTypes(`${graphHostDeclarations}
      registerHost({ load: loadSelectedMemberCallGraph, remove: key => packageRemoval.removeLoaded(key) });
    `),
    {
      registerHost: (api: typeof host) => { host = api; },
      state, callGraphInspection: coordinator,
      createPackageRemoval, packageIdentityKey, memberRequestKey,
      assemblyDescriptorForType, selectedConcreteOverload, memberScopeIsActive,
      browserCreatedCallGraphTabIds, selectedBrowserCallGraphPackageTabIds,
      workspaceShareTabsMatchResolved, invalidateMemberCallGraphWork,
      invalidateGraphMemberNavigationWork, navigationSequence: createNavigationSequence(),
      platformPackForAssembly: () => null,
      localStorage: { setItem: () => {} },
      activateAfterPackageRemoval: () => assert.fail("Inactive removal must not activate a package"),
      clearWorkspaceOccurrenceView: () => {},
      packageInspection: { invalidatePackageResults: () => {} },
      spotlightCache: null, spotlightMemberCache: null,
      releasePackageModelCaches: () => {},
      render, errorMessage: String,
      observeAsync: (work: Promise<void>) => pending.push(work),
    });
  return {
    state, local, full, queries, rendered, coordinator, host, selection,
    expansion, expansionStarted, platform, platformStarted,
    deferExpansion: () => { deferExpansion = true; },
    deferPlatform: () => { deferPlatform = true; },
    remove: () => host.remove(packageIdentityKey(beta)),
    settleRefresh: () => Promise.all(pending),
  };
}

for (const visible of [true, false]) {
  test(`inactive removal invalidates a completed ${visible ? "visible" : "cached"} graph without changing selection`, async () => {
    const h = graphRemovalHarness();
    await h.host.load();
    assert.equal(h.state.memberCallGraph, h.full);
    assert.equal(h.state.memberCallGraph.callers.children[0]?.assembly, "Beta.dll");
    if (!visible) h.state.memberSection = "facts";
    const selection = h.selection();

    h.remove();
    await h.settleRefresh();

    assert.deepEqual(h.selection(), selection);
    assert.deepEqual(h.state.packages.map(pkg => pkg.id), ["Alpha"]);
    if (!visible) {
      assert.equal(h.queries.length, 2, "Hidden graphs must not eagerly query");
      assert.equal(h.state.memberCallGraph, null);
      assert.equal(h.state.memberCallGraphKey, "");
      h.state.memberSection = "call-graph";
      await h.host.load();
    }
    assert.equal(h.queries.length, 3);
    assert.deepEqual(structuredClone(h.queries[2]?.request.workspacePackages), [
      { package: "Alpha", version: "1.0.0", framework: "net10.0" },
    ]);
    assert.equal(h.queries[2]?.request.hasOtherLibraries, false);
    assert.equal(h.state.memberCallGraph, h.local);
    assert.equal(h.rendered.at(-1), h.local);
    assert.equal(h.state.memberCallGraph.scope.packages, 1);
    assert.equal(h.state.memberCallGraph.callers.children.length, 0);
    await h.host.load();
    assert.equal(h.queries.length, 3, "Reopening may reuse only the refreshed graph");
  });
}

for (const failure of [false, true]) {
  test(`inactive removal rejects a pending expansion ${failure ? "failure" : "result"} even when the refreshed local graph is the same object`, async () => {
    const h = graphRemovalHarness();
    h.deferExpansion();
    const originalLoad = h.host.load();
    await h.expansionStarted.promise;
    const selection = h.selection();
    const originalRequest = h.queries[0]!.request;
    assert.equal(h.state.memberCallGraph, h.local);

    h.remove();
    await h.settleRefresh();

    assert.deepEqual(h.selection(), selection);
    assert.equal(h.queries.length, 3);
    assert.deepEqual(structuredClone(h.queries[2]?.request.workspacePackages), [
      { package: "Alpha", version: "1.0.0", framework: "net10.0" },
    ]);
    assert.equal(h.state.memberCallGraph, h.local);
    assert.equal(h.state.memberCallGraphKey, originalRequest.signature);
    assert.equal(originalRequest.isCurrent(), true, "The active member selection is unchanged");
    const renders = h.rendered.length;
    if (failure) h.expansion.reject(new Error("Beta expansion failed"));
    else h.expansion.resolve(h.full);
    await originalLoad;

    assert.equal(h.rendered.length, renders, "Obsolete expansion must not publish");
    assert.equal(h.state.memberCallGraph, h.local);
    assert.equal(h.state.memberCallGraphError, "");
    assert.equal(h.state.memberCallGraphLoading, false);
    assert.equal(h.state.memberCallGraphExpanding, false);
    await h.host.load();
    assert.equal(h.queries.length, 3);
    assert.equal(h.state.memberCallGraph.scope.packages, 1);
  });
}

for (const failure of [false, true]) {
  test(`inactive removal clears platform drill state and rejects its pending ${failure ? "failure" : "result"}`, async () => {
    const h = graphRemovalHarness();
    await h.host.load();
    const request: PlatformDrillRequest = {
      framework: "net10.0", platformVersion: "10.0.0",
      assembly: "System.Text.Json.dll", pack: "netcore.app",
      assemblyVersion: "10.0.0.0", assemblyCulture: null,
      assemblyPublicKeyToken: "cc7b13ffcd2ddd51",
      type: "T:System.Text.Json.JsonSerializer", member: "Serialize",
      selectorKey: "Serialize|System.Object", metadataToken: 0x06000001,
      title: "JsonSerializer.Serialize", errorTarget: "JsonSerializer.Serialize",
      isCurrent: () => true,
    };
    await h.coordinator.drill(request);
    assert.equal(h.state.platformStack.length, 1);
    h.deferPlatform();
    const drill = h.coordinator.drill(request);
    await h.platformStarted.promise;
    assert.equal(h.state.platformDrillLoading, true);
    const selection = h.selection();

    h.remove();
    await h.settleRefresh();
    const renders = h.rendered.length;
    if (failure) h.platform.reject(new Error("Obsolete platform failure"));
    else h.platform.resolve(removalGraph([beta]));
    await drill;

    assert.deepEqual(h.selection(), selection);
    assert.equal(h.state.platformStack.length, 0);
    assert.equal(h.state.platformDrillLoading, false);
    assert.equal(h.state.platformDrillError, "");
    assert.equal(h.state.memberCallGraph, h.local);
    assert.equal(h.rendered.at(-1), h.local);
    assert.equal(h.rendered.length, renders);
  });
}
