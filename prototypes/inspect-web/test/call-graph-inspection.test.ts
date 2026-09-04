import assert from "node:assert/strict";
import test from "node:test";

import {
  callGraphErrorForView,
  createCallGraphInspectionCoordinator,
  type CallGraphInspectionDependencies,
  type CallGraphInspectionState,
  type MemberCallGraphRequest,
  type PlatformDrillRequest,
} from "../src/call-graph-inspection.ts";
import type {
  BrowserCallGraph,
} from "../src/facades/inspect-web-call-graph.d.ts";
import type { MemberFocusSnapshot } from "../src/member-focus.ts";

function graph(mermaid: string): BrowserCallGraph {
  const node = {
    label: "Example.Widget.Run",
    status: "Analyzed",
    inLoop: false,
    source: null,
    children: [],
    assembly: "Example.Package.dll",
    typeFullName: "Example.Widget",
    memberName: "Run",
  };
  return {
    mermaid,
    callers: { ...node },
    callees: { ...node },
    scope: {
      packages: 1,
      assemblies: 1,
      callerAssemblies: 1,
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

function inspectionState(
  overrides: Partial<CallGraphInspectionState> = {},
): CallGraphInspectionState {
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
    ...overrides,
  };
}

function focusSnapshot(): MemberFocusSnapshot {
  return {
    selector: "[data-member-id='M:Example.Widget.Run']",
    dataTarget: null,
    selection: null,
    navigationScope: null,
    navigationSelection: null,
    navigationScrollTop: null,
    focusLost: false,
  };
}

function memberRequest(
  overrides: Partial<MemberCallGraphRequest> = {},
): MemberCallGraphRequest {
  return {
    signature: "member",
    isRuntimePack: false,
    packageId: "Example.Package",
    version: "1.2.3",
    framework: "net10.0",
    assembly: "Example.Package.dll",
    platformPack: "netcore.app",
    platformAssemblyVersion: "1.0.0.0",
    platformAssemblyCulture: null,
    platformAssemblyPublicKeyToken: null,
    typeIdentity: "T:Example.Widget",
    type: "Example.Widget",
    platformType: "Example.Widget",
    member: "Run",
    memberSignature: "void Run()",
    selectorKey: "Run|",
    metadataToken: 0x06000001,
    workspacePackages: [],
    hasOtherLibraries: false,
    isCurrent: () => true,
    ...overrides,
  };
}

function drillRequest(
  overrides: Partial<PlatformDrillRequest> = {},
): PlatformDrillRequest {
  return {
    framework: "net10.0",
    platformVersion: "10.0.10",
    assembly: "System.Text.Json.dll",
    pack: "netcore.app",
    assemblyVersion: "10.0.0.0",
    assemblyCulture: null,
    assemblyPublicKeyToken: "cc7b13ffcd2ddd51",
    type: "T:System.Text.Json.JsonSerializer",
    member: "Serialize",
    selectorKey: "Serialize|System.Object",
    metadataToken: 0x06000001,
    title: "JsonSerializer.Serialize",
    errorTarget: "System.Text.Json.JsonSerializer.Serialize",
    isCurrent: () => true,
    ...overrides,
  };
}

function inspectionDependencies(
  state: CallGraphInspectionState,
  overrides: Partial<Omit<CallGraphInspectionDependencies, "state">> = {},
): CallGraphInspectionDependencies {
  return {
    state,
    queryWorkspace: async () => graph("workspace"),
    queryPlatform: async () => graph("platform"),
    describeError: error =>
      error instanceof Error ? error.message : String(error),
    render: () => {},
    renderPreservingMemberFocus: () => focusSnapshot(),
    renderCallGraph: async () => {},
    nextPaint: async () => {},
    refreshPackageStats: () => {},
    patchCallGraphSection: () => {},
    ...overrides,
  };
}

function deferred<T>() {
  let resolve!: (value: T) => void;
  let reject!: (reason?: unknown) => void;
  const promise = new Promise<T>((accept, deny) => {
    resolve = accept;
    reject = deny;
  });
  return { promise, resolve, reject };
}

test("cached call graphs render without querying again", async () => {
  let queries = 0;
  const events: string[] = [];
  const cached = graph("cached");
  const state = inspectionState({
    memberCallGraph: cached,
    memberCallGraphKey: "member",
  });
  const coordinator = createCallGraphInspectionCoordinator(
    inspectionDependencies(state, {
      queryWorkspace: async () => {
        queries++;
        return graph("unexpected");
      },
      render: () => events.push("render"),
      renderCallGraph: async () => {
        events.push("graph");
      },
    }));

  await coordinator.load(memberRequest());

  assert.equal(queries, 0);
  assert.equal(state.memberCallGraph, cached);
  assert.deepEqual(events, ["render", "graph"]);
});

test("same-key requests in flight are not mistaken for cached results", async () => {
  const first = deferred<BrowserCallGraph>();
  const second = deferred<BrowserCallGraph>();
  let queries = 0;
  const state = inspectionState();
  const coordinator = createCallGraphInspectionCoordinator(
    inspectionDependencies(state, {
      queryWorkspace: async () => {
        queries++;
        return queries === 1 ? first.promise : second.promise;
      },
    }));

  const firstLoad = coordinator.load(memberRequest());
  const secondLoad = coordinator.load(memberRequest());

  assert.equal(queries, 2);

  first.resolve(graph("stale"));
  second.resolve(graph("current"));
  await Promise.all([firstLoad, secondLoad]);

  assert.equal(state.memberCallGraph?.mermaid, "current");
});

test("workspace call graphs publish the fast local stage with focus intact", async () => {
  const local = graph("local");
  const preservedFocus = focusSnapshot();
  const events: string[] = [];
  const state = inspectionState({
    platformStack: [{ graph: graph("drilled"), title: "Old" }],
    platformDrillLoading: true,
    platformDrillError: "old failure",
  });
  const coordinator = createCallGraphInspectionCoordinator(
    inspectionDependencies(state, {
      queryWorkspace: async (request, workspace) => {
        events.push(`query:${workspace.length}`);
        assert.equal(request.typeIdentity, "T:Example.Widget");
        assert.equal(request.type, "Example.Widget");
        assert.equal(request.selectorKey, "Run|");
        assert.equal(request.metadataToken, 0x06000001);
        return local;
      },
      renderPreservingMemberFocus: fallback => {
        events.push(fallback ? "focus:restore" : "focus:capture");
        return preservedFocus;
      },
      renderCallGraph: async () => {
        events.push("graph");
      },
    }));

  await coordinator.load(memberRequest());

  assert.equal(state.memberCallGraph, local);
  assert.equal(state.memberCallGraphLoading, false);
  assert.equal(state.memberCallGraphExpanding, false);
  assert.deepEqual(state.platformStack, []);
  assert.equal(state.platformDrillLoading, false);
  assert.equal(state.platformDrillError, "");
  assert.deepEqual(events, [
    "focus:capture",
    "query:0",
    "focus:restore",
    "graph",
  ]);
});

test("workspace call graphs paint locally before expanding across packages", async () => {
  const local = graph("local");
  const full = graph("full");
  const workspacePackages = [{
    package: "Example.Package",
    version: "1.2.3",
    framework: "net10.0",
  }, {
    package: "Example.Dependency",
    version: "4.5.6",
    framework: "net10.0",
  }];
  const events: string[] = [];
  const state = inspectionState();
  const coordinator = createCallGraphInspectionCoordinator(
    inspectionDependencies(state, {
      queryWorkspace: async (_request, workspace) => {
        events.push(`query:${workspace.length}`);
        return workspace.length ? full : local;
      },
      renderPreservingMemberFocus: fallback => {
        events.push(fallback ? "focus:restore" : "focus:capture");
        return focusSnapshot();
      },
      renderCallGraph: async () => {
        events.push("graph");
      },
      nextPaint: async () => {
        events.push("paint");
      },
      refreshPackageStats: () => events.push("stats"),
      patchCallGraphSection: previous =>
        events.push(`patch:${previous}`),
    }));

  await coordinator.load(memberRequest({
    workspacePackages,
    hasOtherLibraries: true,
  }));

  assert.equal(state.memberCallGraph, full);
  assert.equal(state.memberCallGraphExpanding, false);
  assert.deepEqual(events, [
    "focus:capture",
    "query:0",
    "focus:restore",
    "graph",
    "paint",
    "query:2",
    "stats",
    "patch:local",
  ]);
});

test("workspace expansion rechecks identity after the paint yield", async () => {
  const paint = deferred<void>();
  const paintEntered = deferred<void>();
  let current = true;
  let queries = 0;
  const local = graph("local");
  const state = inspectionState();
  const coordinator = createCallGraphInspectionCoordinator(
    inspectionDependencies(state, {
      queryWorkspace: async () => {
        queries++;
        return local;
      },
      nextPaint: () => {
        paintEntered.resolve(undefined);
        return paint.promise;
      },
    }));

  const load = coordinator.load(memberRequest({
    workspacePackages: [{
      package: "Example.Package",
      version: "1.2.3",
      framework: "net10.0",
    }],
    hasOtherLibraries: true,
    isCurrent: () => current,
  }));
  await paintEntered.promise;
  current = false;
  paint.resolve(undefined);
  await load;

  assert.equal(queries, 1);
  assert.equal(state.memberCallGraph, local);
  assert.equal(state.memberCallGraphExpanding, true);
});

test("workspace expansion failure keeps the local graph and remains visible", async () => {
  let queries = 0;
  let graphRenders = 0;
  const local = graph("local");
  const state = inspectionState();
  const coordinator = createCallGraphInspectionCoordinator(
    inspectionDependencies(state, {
      queryWorkspace: async () => {
        if (queries++ === 0) return local;
        throw new Error("workspace unavailable");
      },
      renderCallGraph: async () => {
        graphRenders++;
      },
    }));

  await coordinator.load(memberRequest({
    workspacePackages: [{
      package: "Example.Package",
      version: "1.2.3",
      framework: "net10.0",
    }],
    hasOtherLibraries: true,
  }));

  assert.equal(state.memberCallGraph, local);
  assert.equal(state.memberCallGraphLoading, false);
  assert.equal(state.memberCallGraphExpanding, false);
  assert.equal(
    state.memberCallGraphError,
    "Workspace expansion was incomplete: workspace unavailable");
  assert.equal(graphRenders, 2);
});

test("platform descent preserves a completed in-flight workspace expansion", async () => {
  const expansion = deferred<BrowserCallGraph>();
  const expansionStarted = deferred<void>();
  const local = graph("local");
  const full = graph("full");
  let workspaceQueries = 0;
  let patches = 0;
  const state = inspectionState();
  const coordinator = createCallGraphInspectionCoordinator(
    inspectionDependencies(state, {
      queryWorkspace: async (_request, workspace) => {
        workspaceQueries++;
        if (!workspace.length) return local;
        expansionStarted.resolve(undefined);
        return expansion.promise;
      },
      patchCallGraphSection: () => patches++,
    }));
  const request = memberRequest({
    workspacePackages: [{
      package: "Example.Package",
      version: "1.2.3",
      framework: "net10.0",
    }],
    hasOtherLibraries: true,
  });

  const load = coordinator.load(request);
  await expansionStarted.promise;
  state.memberCallGraphSeq++;
  state.memberCallGraphExpanding = false;
  await coordinator.drill(drillRequest());
  expansion.resolve(full);
  await load;

  assert.equal(workspaceQueries, 2);
  assert.equal(state.memberCallGraph, full);
  assert.equal(state.memberCallGraphExpanding, false);
  assert.equal(patches, 0);
  await coordinator.popDrill();
  await coordinator.load(request);
  assert.equal(workspaceQueries, 2);
  assert.equal(state.memberCallGraph, full);
});

test("platform descent preserves an in-flight workspace expansion failure", async () => {
  const expansion = deferred<BrowserCallGraph>();
  const expansionStarted = deferred<void>();
  const local = graph("local");
  let workspaceQueries = 0;
  let graphRenders = 0;
  const state = inspectionState();
  const coordinator = createCallGraphInspectionCoordinator(
    inspectionDependencies(state, {
      queryWorkspace: async (_request, workspace) => {
        workspaceQueries++;
        if (!workspace.length) return local;
        expansionStarted.resolve(undefined);
        return expansion.promise;
      },
      renderCallGraph: async () => {
        graphRenders++;
      },
    }));
  const request = memberRequest({
    workspacePackages: [{
      package: "Example.Package",
      version: "1.2.3",
      framework: "net10.0",
    }],
    hasOtherLibraries: true,
  });

  const load = coordinator.load(request);
  await expansionStarted.promise;
  state.memberCallGraphSeq++;
  state.memberCallGraphExpanding = false;
  await coordinator.drill(drillRequest());
  expansion.reject(new Error("workspace unavailable"));
  await load;

  assert.equal(graphRenders, 2);
  assert.equal(
    state.memberCallGraphError,
    "Workspace expansion was incomplete: workspace unavailable");
  assert.equal(callGraphErrorForView(state), "");
  state.graphMemberNavigationError =
    "Could not open System.Text.Json.JsonSerializer.Serialize: exact projection failed";
  assert.equal(
    callGraphErrorForView(state),
    "Could not open System.Text.Json.JsonSerializer.Serialize: exact projection failed");
  state.graphMemberNavigationError = "";
  await coordinator.popDrill();
  assert.equal(
    callGraphErrorForView(state),
    "Workspace expansion was incomplete: workspace unavailable");
  await coordinator.load(request);
  assert.equal(workspaceQueries, 2);
  assert.equal(state.memberCallGraph, local);
  assert.equal(
    state.memberCallGraphError,
    "Workspace expansion was incomplete: workspace unavailable");
});

test("blocked platform activation publishes its in-flight workspace expansion", async () => {
  const expansion = deferred<BrowserCallGraph>();
  const expansionStarted = deferred<void>();
  const local = graph("local");
  const full = graph("full");
  let patches = 0;
  const state = inspectionState();
  const coordinator = createCallGraphInspectionCoordinator(
    inspectionDependencies(state, {
      queryWorkspace: async (_request, workspace) => {
        if (!workspace.length) return local;
        expansionStarted.resolve(undefined);
        return expansion.promise;
      },
      patchCallGraphSection: previous => {
        assert.equal(previous, "local");
        patches++;
      },
    }));

  const load = coordinator.load(memberRequest({
    workspacePackages: [{
      package: "Example.Package",
      version: "1.2.3",
      framework: "net10.0",
    }],
    hasOtherLibraries: true,
  }));
  await expansionStarted.promise;
  state.memberCallGraphSeq++;
  state.memberCallGraphExpanding = false;
  expansion.resolve(full);
  await load;

  assert.equal(state.memberCallGraph, full);
  assert.equal(patches, 1);
});

test("blocked platform activation publishes its workspace expansion failure", async () => {
  const expansion = deferred<BrowserCallGraph>();
  const expansionStarted = deferred<void>();
  const local = graph("local");
  let graphRenders = 0;
  const state = inspectionState();
  const coordinator = createCallGraphInspectionCoordinator(
    inspectionDependencies(state, {
      queryWorkspace: async (_request, workspace) => {
        if (!workspace.length) return local;
        expansionStarted.resolve(undefined);
        return expansion.promise;
      },
      renderCallGraph: async () => {
        graphRenders++;
      },
    }));

  const load = coordinator.load(memberRequest({
    workspacePackages: [{
      package: "Example.Package",
      version: "1.2.3",
      framework: "net10.0",
    }],
    hasOtherLibraries: true,
  }));
  await expansionStarted.promise;
  state.memberCallGraphSeq++;
  state.memberCallGraphExpanding = false;
  expansion.reject(new Error("workspace unavailable"));
  await load;

  assert.equal(
    state.memberCallGraphError,
    "Workspace expansion was incomplete: workspace unavailable");
  assert.equal(graphRenders, 2);
});

test("canceled expansion cannot replace a newer same-key local graph", async () => {
  const expansion = deferred<BrowserCallGraph>();
  const expansionStarted = deferred<void>();
  const local = graph("local");
  const newer = graph("newer");
  let patches = 0;
  const state = inspectionState();
  const coordinator = createCallGraphInspectionCoordinator(
    inspectionDependencies(state, {
      queryWorkspace: async (_request, workspace) => {
        if (!workspace.length) return local;
        expansionStarted.resolve(undefined);
        return expansion.promise;
      },
      patchCallGraphSection: () => patches++,
    }));

  const load = coordinator.load(memberRequest({
    workspacePackages: [{
      package: "Example.Package",
      version: "1.2.3",
      framework: "net10.0",
    }],
    hasOtherLibraries: true,
  }));
  await expansionStarted.promise;
  state.memberCallGraphSeq++;
  state.memberCallGraphExpanding = false;
  state.memberCallGraph = newer;
  expansion.resolve(graph("stale-full"));
  await load;

  assert.equal(state.memberCallGraph, newer);
  assert.equal(patches, 0);
});

test("canceled expansion failure cannot contaminate a newer same-key local graph", async () => {
  const expansion = deferred<BrowserCallGraph>();
  const expansionStarted = deferred<void>();
  const local = graph("local");
  const newer = graph("newer");
  let graphRenders = 0;
  const state = inspectionState();
  const coordinator = createCallGraphInspectionCoordinator(
    inspectionDependencies(state, {
      queryWorkspace: async (_request, workspace) => {
        if (!workspace.length) return local;
        expansionStarted.resolve(undefined);
        return expansion.promise;
      },
      renderCallGraph: async () => {
        graphRenders++;
      },
    }));

  const load = coordinator.load(memberRequest({
    workspacePackages: [{
      package: "Example.Package",
      version: "1.2.3",
      framework: "net10.0",
    }],
    hasOtherLibraries: true,
  }));
  await expansionStarted.promise;
  state.memberCallGraphSeq++;
  state.memberCallGraphExpanding = false;
  state.memberCallGraph = newer;
  expansion.reject(new Error("stale failure"));
  await load;

  assert.equal(state.memberCallGraph, newer);
  assert.equal(state.memberCallGraphError, "");
  assert.equal(graphRenders, 1);
});

test("canceled expansion failure retains a newer same-view activation error", async () => {
  const expansion = deferred<BrowserCallGraph>();
  const expansionStarted = deferred<void>();
  const local = graph("local");
  const state = inspectionState();
  const coordinator = createCallGraphInspectionCoordinator(
    inspectionDependencies(state, {
      queryWorkspace: async (_request, workspace) => {
        if (!workspace.length) return local;
        expansionStarted.resolve(undefined);
        return expansion.promise;
      },
    }));

  const load = coordinator.load(memberRequest({
    workspacePackages: [{
      package: "Example.Package",
      version: "1.2.3",
      framework: "net10.0",
    }],
    hasOtherLibraries: true,
  }));
  await expansionStarted.promise;
  state.memberCallGraphSeq++;
  state.memberCallGraphExpanding = false;
  state.graphMemberNavigationError =
    "Could not open Example.Widget.Hidden: exact projection failed";
  expansion.reject(new Error("workspace unavailable"));
  await load;

  assert.equal(
    callGraphErrorForView(state),
    "Could not open Example.Widget.Hidden: exact projection failed; "
      + "Workspace expansion was incomplete: workspace unavailable");
});

test("newer same-view activation error retains an earlier expansion failure", async () => {
  const expansion = deferred<BrowserCallGraph>();
  const expansionStarted = deferred<void>();
  const local = graph("local");
  const state = inspectionState();
  const coordinator = createCallGraphInspectionCoordinator(
    inspectionDependencies(state, {
      queryWorkspace: async (_request, workspace) => {
        if (!workspace.length) return local;
        expansionStarted.resolve(undefined);
        return expansion.promise;
      },
    }));

  const load = coordinator.load(memberRequest({
    workspacePackages: [{
      package: "Example.Package",
      version: "1.2.3",
      framework: "net10.0",
    }],
    hasOtherLibraries: true,
  }));
  await expansionStarted.promise;
  state.memberCallGraphSeq++;
  state.memberCallGraphExpanding = false;
  expansion.reject(new Error("workspace unavailable"));
  await load;
  state.graphMemberNavigationError =
    "Could not open Example.Widget.Hidden: exact projection failed";

  assert.equal(
    callGraphErrorForView(state),
    "Could not open Example.Widget.Hidden: exact projection failed; "
      + "Workspace expansion was incomplete: workspace unavailable");
});

test("initial workspace failure remains visible without rendering a graph", async () => {
  let focusRenders = 0;
  let graphRenders = 0;
  const state = inspectionState();
  const coordinator = createCallGraphInspectionCoordinator(
    inspectionDependencies(state, {
      queryWorkspace: async () => {
        throw new Error("query unavailable");
      },
      renderPreservingMemberFocus: () => {
        focusRenders++;
        return focusSnapshot();
      },
      renderCallGraph: async () => {
        graphRenders++;
      },
    }));

  await coordinator.load(memberRequest());

  assert.equal(state.memberCallGraph, null);
  assert.equal(state.memberCallGraphLoading, false);
  assert.equal(state.memberCallGraphError, "query unavailable");
  assert.equal(focusRenders, 2);
  assert.equal(graphRenders, 0);
});

test("stale workspace success cannot publish with the same request key", async () => {
  const request = deferred<BrowserCallGraph>();
  const state = inspectionState();
  const coordinator = createCallGraphInspectionCoordinator(
    inspectionDependencies(state, {
      queryWorkspace: async () => request.promise,
    }));

  const load = coordinator.load(memberRequest({
    isCurrent: () => false,
  }));
  request.resolve(graph("stale"));
  await load;

  assert.equal(state.memberCallGraph, null);
  assert.equal(state.memberCallGraphLoading, true);
  assert.equal(state.memberCallGraphKey, "member");
});

test("workspace success cannot publish after its request key changes", async () => {
  const request = deferred<BrowserCallGraph>();
  const state = inspectionState();
  const coordinator = createCallGraphInspectionCoordinator(
    inspectionDependencies(state, {
      queryWorkspace: async () => request.promise,
    }));

  const load = coordinator.load(memberRequest());
  state.memberCallGraphKey = "newer";
  request.resolve(graph("stale"));
  await load;

  assert.equal(state.memberCallGraph, null);
  assert.equal(state.memberCallGraphLoading, true);
  assert.equal(state.memberCallGraphKey, "newer");
});

test("workspace success cannot publish after its sequence is superseded", async () => {
  const request = deferred<BrowserCallGraph>();
  const state = inspectionState();
  const coordinator = createCallGraphInspectionCoordinator(
    inspectionDependencies(state, {
      queryWorkspace: async () => request.promise,
    }));

  const load = coordinator.load(memberRequest());
  state.memberCallGraphSeq++;
  request.resolve(graph("stale"));
  await load;

  assert.equal(state.memberCallGraph, null);
  assert.equal(state.memberCallGraphLoading, true);
  assert.equal(state.memberCallGraphKey, "member");
});

test("runtime members route directly through platform graph expansion", async () => {
  let workspaceQueries = 0;
  const platform = graph("platform");
  const state = inspectionState();
  const coordinator = createCallGraphInspectionCoordinator(
    inspectionDependencies(state, {
      queryWorkspace: async () => {
        workspaceQueries++;
        return graph("unexpected");
      },
      queryPlatform: async request => {
        assert.deepEqual(
          [
            request.framework,
            request.assembly,
            request.pack,
            request.assemblyVersion,
            request.assemblyCulture,
            request.assemblyPublicKeyToken,
            request.type,
            request.member,
            request.selectorKey,
            request.metadataToken,
          ],
          [
            "net10.0",
            "Example.Package.dll",
            "netcore.app",
            "1.0.0.0",
            null,
            null,
            "T:Example.Widget",
            "Run",
            "Run|",
            0x06000001,
          ]);
        return platform;
      },
    }));

  await coordinator.load(memberRequest({
    isRuntimePack: true,
    platformType: "T:Example.Widget",
  }));

  assert.equal(workspaceQueries, 0);
  assert.equal(state.memberCallGraph, platform);
  assert.equal(state.memberCallGraphLoading, false);
});

test("runtime graph failure remains visible", async () => {
  let focusRenders = 0;
  const state = inspectionState();
  const coordinator = createCallGraphInspectionCoordinator(
    inspectionDependencies(state, {
      queryPlatform: async () => {
        throw new Error("platform unavailable");
      },
      renderPreservingMemberFocus: () => {
        focusRenders++;
        return focusSnapshot();
      },
    }));

  await coordinator.load(memberRequest({ isRuntimePack: true }));

  assert.equal(state.memberCallGraph, null);
  assert.equal(state.memberCallGraphLoading, false);
  assert.equal(state.memberCallGraphExpanding, false);
  assert.equal(state.memberCallGraphError, "platform unavailable");
  assert.equal(focusRenders, 2);
});

test("superseded runtime graph completion cannot publish", async () => {
  const request = deferred<BrowserCallGraph>();
  const state = inspectionState();
  const coordinator = createCallGraphInspectionCoordinator(
    inspectionDependencies(state, {
      queryPlatform: async () => request.promise,
    }));

  const load = coordinator.load(memberRequest({ isRuntimePack: true }));
  state.memberCallGraphSeq++;
  request.resolve(graph("stale"));
  await load;

  assert.equal(state.memberCallGraph, null);
  assert.equal(state.memberCallGraphLoading, true);
});

test("runtime graph completion cannot publish after its view owner changes", async () => {
  const request = deferred<BrowserCallGraph>();
  let current = true;
  let graphRenders = 0;
  const state = inspectionState();
  const coordinator = createCallGraphInspectionCoordinator(
    inspectionDependencies(state, {
      queryPlatform: async () => request.promise,
      renderCallGraph: async () => {
        graphRenders++;
      },
    }));

  const load = coordinator.load(memberRequest({
    isRuntimePack: true,
    isCurrent: () => current,
  }));
  current = false;
  request.resolve(graph("stale"));
  await load;

  assert.equal(state.memberCallGraph, null);
  assert.equal(state.memberCallGraphLoading, true);
  assert.equal(state.memberCallGraphError, "");
  assert.equal(graphRenders, 0);
});

test("runtime graph failures stay silent after their view owner changes", async () => {
  const request = deferred<BrowserCallGraph>();
  let current = true;
  const state = inspectionState();
  const coordinator = createCallGraphInspectionCoordinator(
    inspectionDependencies(state, {
      queryPlatform: async () => request.promise,
    }));

  const load = coordinator.load(memberRequest({
    isRuntimePack: true,
    isCurrent: () => current,
  }));
  current = false;
  request.reject(new Error("stale failure"));
  await load;

  assert.equal(state.memberCallGraph, null);
  assert.equal(state.memberCallGraphLoading, true);
  assert.equal(state.memberCallGraphError, "");
});

test("platform drill publishes current graphs and pop restores the parent", async () => {
  const drilled = graph("drilled");
  const events: string[] = [];
  const state = inspectionState({
    memberCallGraph: graph("root"),
    memberCallGraphSeq: 3,
  });
  const coordinator = createCallGraphInspectionCoordinator(
    inspectionDependencies(state, {
      queryPlatform: async request => {
        assert.deepEqual(
          [
            request.framework,
            request.assembly,
            request.pack,
            request.assemblyVersion,
            request.assemblyCulture,
            request.assemblyPublicKeyToken,
            request.type,
            request.member,
            request.selectorKey,
            request.metadataToken,
          ],
          [
            "net10.0",
            "System.Text.Json.dll",
            "netcore.app",
            "10.0.0.0",
            null,
            "cc7b13ffcd2ddd51",
            "T:System.Text.Json.JsonSerializer",
            "Serialize",
            "Serialize|System.Object",
            0x06000001,
          ]);
        return drilled;
      },
      render: () => events.push("render"),
      renderPreservingMemberFocus: fallback => {
        events.push(fallback ? "focus:restore" : "focus:capture");
        return focusSnapshot();
      },
      renderCallGraph: async () => {
        events.push("graph");
      },
    }));

  await coordinator.drill(drillRequest());

  assert.equal(state.platformDrillLoading, false);
  assert.equal(state.platformDrillError, "");
  assert.deepEqual(state.platformStack, [{
    graph: drilled,
    title: "JsonSerializer.Serialize",
  }]);
  assert.deepEqual(events, ["focus:capture", "focus:restore", "graph"]);

  await coordinator.popDrill();
  assert.deepEqual(state.platformStack, []);
  assert.deepEqual(events, [
    "focus:capture",
    "focus:restore",
    "graph",
    "render",
    "graph",
  ]);
});

test("platform drill failures remain visible with the full target identity", async () => {
  let graphRenders = 0;
  const state = inspectionState({ memberCallGraphSeq: 4 });
  const coordinator = createCallGraphInspectionCoordinator(
    inspectionDependencies(state, {
      queryPlatform: async () => {
        throw new Error("range fetch unavailable");
      },
      renderCallGraph: async () => {
        graphRenders++;
      },
    }));

  await coordinator.drill(drillRequest());

  assert.equal(state.platformDrillLoading, false);
  assert.equal(
    state.platformDrillError,
    "Could not descend into System.Text.Json.JsonSerializer.Serialize: range fetch unavailable");
  assert.equal(graphRenders, 1);
});

test("superseded platform drill completion cannot publish", async () => {
  const request = deferred<BrowserCallGraph>();
  const state = inspectionState({ memberCallGraphSeq: 5 });
  const coordinator = createCallGraphInspectionCoordinator(
    inspectionDependencies(state, {
      queryPlatform: async () => request.promise,
    }));

  const load = coordinator.drill(drillRequest());
  state.memberCallGraphSeq++;
  request.reject(new Error("stale failure"));
  await load;

  assert.equal(state.platformStack.length, 0);
  assert.equal(state.platformDrillLoading, true);
  assert.equal(state.platformDrillError, "");
});

test("platform drill completion cannot publish after its view owner changes", async () => {
  const request = deferred<BrowserCallGraph>();
  let current = true;
  let graphRenders = 0;
  let memberRenders = 0;
  const state = inspectionState({ memberCallGraphSeq: 5 });
  const coordinator = createCallGraphInspectionCoordinator(
    inspectionDependencies(state, {
      queryPlatform: async () => request.promise,
      renderPreservingMemberFocus: () => {
        memberRenders++;
        return focusSnapshot();
      },
      renderCallGraph: async () => {
        graphRenders++;
      },
    }));

  const load = coordinator.drill(drillRequest({ isCurrent: () => current }));
  current = false;
  request.resolve(graph("stale"));
  await load;

  assert.equal(state.platformStack.length, 0);
  assert.equal(state.platformDrillLoading, false);
  assert.equal(state.platformDrillError, "");
  assert.equal(graphRenders, 0);
  assert.equal(memberRenders, 2);
});

test("platform drill failures stay silent after their view owner changes", async () => {
  const request = deferred<BrowserCallGraph>();
  let current = true;
  let graphRenders = 0;
  let memberRenders = 0;
  const state = inspectionState({ memberCallGraphSeq: 5 });
  const coordinator = createCallGraphInspectionCoordinator(
    inspectionDependencies(state, {
      queryPlatform: async () => request.promise,
      renderPreservingMemberFocus: () => {
        memberRenders++;
        return focusSnapshot();
      },
      renderCallGraph: async () => {
        graphRenders++;
      },
    }));

  const load = coordinator.drill(drillRequest({ isCurrent: () => current }));
  current = false;
  request.reject(new Error("stale failure"));
  await load;

  assert.equal(state.platformStack.length, 0);
  assert.equal(state.platformDrillLoading, false);
  assert.equal(state.platformDrillError, "");
  assert.equal(graphRenders, 0);
  assert.equal(memberRenders, 2);
});

test("duplicate platform drill requests do not query or render", async () => {
  let queries = 0;
  let renders = 0;
  const state = inspectionState({ platformDrillLoading: true });
  const coordinator = createCallGraphInspectionCoordinator(
    inspectionDependencies(state, {
      queryPlatform: async () => {
        queries++;
        return graph("unexpected");
      },
      renderPreservingMemberFocus: () => {
        renders++;
        return focusSnapshot();
      },
    }));

  await coordinator.drill(drillRequest());

  assert.equal(queries, 0);
  assert.equal(renders, 0);
  assert.equal(state.platformDrillLoading, true);
});
