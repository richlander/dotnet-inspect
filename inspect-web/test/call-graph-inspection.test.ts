import assert from "node:assert/strict";
import test from "node:test";

import {
  createCallGraphInspectionCoordinator,
  queryPlatformCallGraph,
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
      unavailableDependencyRoutes: 0,
      hasIncompleteCorrespondence: false,
      unclassifiedBoundaryEdges: 0,
      unclassifiedBoundaryNamedEdges: 0,
      unclassifiedBoundaryAssemblies: [],
      physicalOccurrenceUnavailableEdges: 0,
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
    platformContextId: null,
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
    traversalFramework: "net12.0",
    isCurrent: () => true,
    ...overrides,
  };
}

function drillRequest(
  overrides: Partial<PlatformDrillRequest> = {},
): PlatformDrillRequest {
  return {
    contextId: null,
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
    queryPackage: async () => graph("package"),
    queryPlatform: async () => graph("platform"),
    describeError: error =>
      error instanceof Error ? error.message : String(error),
    render: () => {},
    renderPreservingMemberFocus: () => focusSnapshot(),
    renderCallGraph: async () => {},
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
      queryPackage: async () => {
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

test("Platform reload and drill forward the exact context through the generated facade adapter", async () => {
  const contexts: (string | null)[] = [];
  const state = inspectionState();
  const coordinator = createCallGraphInspectionCoordinator(
    inspectionDependencies(state, {
      queryPlatform: request => queryPlatformCallGraph(async (...args) => {
        contexts.push(args[11]);
        if (args[11] === "expired")
          throw new Error("ContextUnavailable: selected context expired.");
        return graph(args[11] ?? "ordinary");
      }, request),
    }));
  const request = memberRequest({
    isRuntimePack: true,
    platformContextId: "demo-a",
  });
  await coordinator.load(request);
  await coordinator.load({ ...request, signature: "other-member" });
  await coordinator.load(request);
  await coordinator.drill(drillRequest({ contextId: "demo-a" }));
  assert.deepEqual(contexts, ["demo-a", "demo-a", "demo-a", "demo-a"]);
  await coordinator.popDrill();
  await coordinator.load({
    ...request, signature: "ordinary", platformContextId: null,
  });
  assert.equal(state.memberCallGraph?.mermaid, "ordinary");
  await coordinator.load({
    ...request, signature: "demo-b", platformContextId: "demo-b",
  });
  assert.equal(state.memberCallGraph?.mermaid, "demo-b");
  await coordinator.load({
    ...request, signature: "expired", platformContextId: "expired",
  });
  assert.equal(state.memberCallGraph, null);
  assert.match(state.memberCallGraphError, /ContextUnavailable/);
  await coordinator.drill(drillRequest({ contextId: "expired" }));
  assert.match(state.platformDrillError, /ContextUnavailable/);
  assert.deepEqual(contexts.slice(4), [null, "demo-b", "expired", "expired"]);
});

test("same-key requests in flight are not mistaken for cached results", async () => {
  const first = deferred<BrowserCallGraph>();
  const second = deferred<BrowserCallGraph>();
  let queries = 0;
  const state = inspectionState();
  const coordinator = createCallGraphInspectionCoordinator(
    inspectionDependencies(state, {
      queryPackage: async () => {
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

test("package call graphs load once with focus intact and forward traversal policy", async () => {
  const loaded = graph("dependency-aware");
  const preservedFocus = focusSnapshot();
  const events: string[] = [];
  const state = inspectionState({
    platformStack: [{ graph: graph("drilled"), title: "Old" }],
    platformDrillLoading: true,
    platformDrillError: "old failure",
  });
  const coordinator = createCallGraphInspectionCoordinator(
    inspectionDependencies(state, {
      queryPackage: async request => {
        events.push("query");
        assert.equal(request.packageId, "Example.Package");
        assert.equal(request.version, "1.2.3");
        assert.equal(request.framework, "net10.0");
        assert.equal(request.assembly, "Example.Package.dll");
        assert.equal(request.typeIdentity, "T:Example.Widget");
        assert.equal(request.type, "Example.Widget");
        assert.equal(request.selectorKey, "Run|");
        assert.equal(request.metadataToken, 0x06000001);
        assert.equal(request.traversalFramework, "net11.0");
        return loaded;
      },
      renderPreservingMemberFocus: fallback => {
        events.push(fallback ? "focus:restore" : "focus:capture");
        return preservedFocus;
      },
      renderCallGraph: async () => {
        events.push("graph");
      },
    }));

  await coordinator.load(memberRequest({ traversalFramework: "net11.0" }));

  assert.equal(state.memberCallGraph, loaded);
  assert.equal(state.memberCallGraphLoading, false);
  assert.equal(state.memberCallGraphExpanding, false);
  assert.deepEqual(state.platformStack, []);
  assert.equal(state.platformDrillLoading, false);
  assert.equal(state.platformDrillError, "");
  assert.deepEqual(events, [
    "focus:capture",
    "query",
    "focus:restore",
    "graph",
  ]);
});

test("package call graph failure remains visible without rendering a graph", async () => {
  let focusRenders = 0;
  let graphRenders = 0;
  const state = inspectionState();
  const coordinator = createCallGraphInspectionCoordinator(
    inspectionDependencies(state, {
      queryPackage: async () => {
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
  assert.equal(state.memberCallGraphExpanding, false);
  assert.equal(state.memberCallGraphError, "query unavailable");
  assert.equal(focusRenders, 2);
  assert.equal(graphRenders, 0);
});

test("stale package success cannot publish after its view owner changes", async () => {
  const request = deferred<BrowserCallGraph>();
  let current = true;
  const state = inspectionState();
  const coordinator = createCallGraphInspectionCoordinator(
    inspectionDependencies(state, {
      queryPackage: async () => request.promise,
    }));

  const load = coordinator.load(memberRequest({
    isCurrent: () => current,
  }));
  current = false;
  request.resolve(graph("stale"));
  await load;

  assert.equal(state.memberCallGraph, null);
  assert.equal(state.memberCallGraphLoading, true);
  assert.equal(state.memberCallGraphError, "");
});

test("stale package failure cannot publish after its view owner changes", async () => {
  const request = deferred<BrowserCallGraph>();
  let current = true;
  let graphRenders = 0;
  const state = inspectionState();
  const coordinator = createCallGraphInspectionCoordinator(
    inspectionDependencies(state, {
      queryPackage: async () => request.promise,
      renderCallGraph: async () => {
        graphRenders++;
      },
    }));

  const load = coordinator.load(memberRequest({
    isCurrent: () => current,
  }));
  current = false;
  request.reject(new Error("stale failure"));
  await load;

  assert.equal(state.memberCallGraph, null);
  assert.equal(state.memberCallGraphLoading, true);
  assert.equal(state.memberCallGraphError, "");
  assert.equal(graphRenders, 0);
});

test("package success cannot publish after its request key changes", async () => {
  const request = deferred<BrowserCallGraph>();
  const state = inspectionState();
  const coordinator = createCallGraphInspectionCoordinator(
    inspectionDependencies(state, {
      queryPackage: async () => request.promise,
    }));

  const load = coordinator.load(memberRequest());
  state.memberCallGraphKey = "newer";
  request.resolve(graph("stale"));
  await load;

  assert.equal(state.memberCallGraph, null);
  assert.equal(state.memberCallGraphLoading, true);
  assert.equal(state.memberCallGraphKey, "newer");
});

test("package success cannot publish after its sequence is superseded", async () => {
  const request = deferred<BrowserCallGraph>();
  const state = inspectionState();
  const coordinator = createCallGraphInspectionCoordinator(
    inspectionDependencies(state, {
      queryPackage: async () => request.promise,
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
  let packageQueries = 0;
  const platform = graph("platform");
  const state = inspectionState();
  const coordinator = createCallGraphInspectionCoordinator(
    inspectionDependencies(state, {
      queryPackage: async () => {
        packageQueries++;
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

  assert.equal(packageQueries, 0);
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
