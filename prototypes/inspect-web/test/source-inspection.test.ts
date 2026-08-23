import assert from "node:assert/strict";
import test from "node:test";

import {
  closedGraphSource,
  createSourceInspectionCoordinator,
  type SourceInspectionDependencies,
  type SourceInspectionState,
} from "../src/source-inspection.ts";
import type { BrowserSource } from "../src/inspect-web-engine.d.ts";
import type { MemberFocusSnapshot } from "../src/member-focus.ts";

function source(text: string): BrowserSource {
  return {
    provider: "pdb",
    provenance: "SourceLink",
    url: "https://example.test/source.cs",
    pdbSourceLimitation: null,
    text,
  };
}

function focusSnapshot(selector = "#member-filter"): MemberFocusSnapshot {
  return {
    selector,
    dataTarget: null,
    selection: null,
    navigationScope: null,
    navigationSelection: null,
    navigationScrollTop: null,
    focusLost: false,
  };
}

function inspectionState(
  overrides: Partial<SourceInspectionState> = {},
): SourceInspectionState {
  return {
    settings: false,
    explorer: null,
    loading: false,
    error: "",
    home: false,
    package: {},
    atPackageRoot: false,
    lens: "api",
    selectedMemberKey: "method:Build",
    memberSection: "source",
    sourceRequestGeneration: 0,
    memberSource: null,
    memberSourceLoading: false,
    memberSourceError: "",
    memberSourceKey: "",
    typeSource: null,
    typeSourceLoading: false,
    typeSourceError: "",
    typeSourceKey: "",
    graphSource: closedGraphSource,
    taste: [],
    ...overrides,
  };
}

function inspectionDependencies(
  state: SourceInspectionState,
  overrides: Partial<Omit<SourceInspectionDependencies, "state">> = {},
): SourceInspectionDependencies {
  return {
    state,
    queryMemberSource: async () => source("member"),
    queryTypeSource: async () => source("type"),
    queryGraphSource: async () => source("graph"),
    cancelEngineSourceRequest: () => {},
    describeError: error =>
      error instanceof Error ? error.message : String(error),
    render: () => {},
    renderPreservingMemberFocus: fallback =>
      fallback ?? focusSnapshot(),
    ...overrides,
  };
}

function deferred<T>() {
  let resolve!: (value: T) => void;
  let reject!: (reason: unknown) => void;
  const promise = new Promise<T>((accept, fail) => {
    resolve = accept;
    reject = fail;
  });
  return { promise, resolve, reject };
}

test("hidden source work is cancelled through the shared engine boundary", () => {
  let cancellations = 0;
  const state = inspectionState({
    settings: true,
    memberSourceLoading: true,
    memberSourceKey: "member",
  });
  const coordinator = createSourceInspectionCoordinator(
    inspectionDependencies(state, {
      cancelEngineSourceRequest: () => cancellations++,
    }));

  coordinator.cancelHiddenRequest();

  assert.equal(cancellations, 1);
  assert.equal(state.sourceRequestGeneration, 1);
  assert.equal(state.memberSourceLoading, false);
  assert.equal(state.memberSourceKey, "");

  state.settings = false;
  state.memberSourceLoading = true;
  coordinator.cancelHiddenRequest();
  assert.equal(cancellations, 1);
  assert.equal(state.memberSourceLoading, true);
});

test("member source publishes only for the current member selection", async () => {
  const query = deferred<BrowserSource>();
  const focusRenders: Array<string | null> = [];
  let current = true;
  const state = inspectionState();
  const coordinator = createSourceInspectionCoordinator(
    inspectionDependencies(state, {
      queryMemberSource: async request => {
        assert.equal(request.member, "Build");
        assert.equal(request.taste, "[\"expression-bodied-members\"]");
        return query.promise;
      },
      renderPreservingMemberFocus: fallback => {
        focusRenders.push(fallback?.selector ?? null);
        return fallback ?? focusSnapshot();
      },
    }));

  const load = coordinator.loadMemberSource({
    signature: "member-signature",
    packageId: "Example.Package",
    version: "1.2.3",
    framework: "net10.0",
    assembly: "Example.Package",
    type: "Example.Widget",
    member: "Build",
    selectorKey: "method",
    metadataToken: 42,
    taste: "[\"expression-bodied-members\"]",
    isCurrent: () => current,
  });
  assert.equal(state.memberSourceLoading, true);
  current = false;
  query.resolve(source("stale"));
  await load;

  assert.equal(state.memberSource, null);
  assert.equal(state.memberSourceLoading, false);
  assert.deepEqual(focusRenders, [null]);
});

test("current member source failures remain visible and restore focus", async () => {
  const renders: Array<string | null> = [];
  const state = inspectionState();
  const coordinator = createSourceInspectionCoordinator(
    inspectionDependencies(state, {
      queryMemberSource: async () => {
        throw new Error("source unavailable");
      },
      renderPreservingMemberFocus: fallback => {
        renders.push(fallback?.selector ?? null);
        return fallback ?? focusSnapshot("#type-list");
      },
    }));

  await coordinator.loadMemberSource({
    signature: "member-signature",
    packageId: "Example.Package",
    version: "1.2.3",
    framework: "net10.0",
    assembly: "Example.Package",
    type: "Example.Widget",
    member: "Build",
    selectorKey: "method",
    metadataToken: 42,
    taste: "[]",
    isCurrent: () => true,
  });

  assert.equal(state.memberSourceError, "source unavailable");
  assert.equal(state.memberSourceLoading, false);
  assert.deepEqual(renders, [null, "#type-list"]);
});

test("type source caches an owned result without repainting a hidden surface", async () => {
  let queries = 0;
  let renders = 0;
  const state = inspectionState({
    lens: "source",
    selectedMemberKey: "",
    memberSection: "overview",
  });
  const coordinator = createSourceInspectionCoordinator(
    inspectionDependencies(state, {
      queryTypeSource: async () => {
        queries++;
        return source("type");
      },
      renderPreservingMemberFocus: fallback => {
        renders++;
        return fallback ?? focusSnapshot();
      },
    }));
  const request = {
    signature: "type-signature",
    packageId: "Example.Package",
    version: "1.2.3",
    framework: "net10.0",
    assembly: "Example.Package",
    type: "Example.Widget",
    taste: "[]",
    isVisible: () => false,
  };

  await coordinator.loadTypeSource(request);
  assert.equal(state.typeSource?.text, "type");
  assert.equal(state.typeSourceLoading, false);
  assert.equal(renders, 1);

  await coordinator.loadTypeSource(request);
  assert.equal(queries, 1);
  assert.equal(renders, 2);
});

test("closing graph source invalidates its result and cancels the engine", async () => {
  const query = deferred<BrowserSource>();
  const events: string[] = [];
  const state = inspectionState({
    taste: ["expression-bodied-members"],
  });
  const coordinator = createSourceInspectionCoordinator(
    inspectionDependencies(state, {
      queryGraphSource: async (request, taste) => {
        events.push(`query:${request.member}/${taste}`);
        return query.promise;
      },
      cancelEngineSourceRequest: () => events.push("cancel"),
      render: () => events.push("render"),
    }));
  const request = {
    packageId: "Example.Package",
    version: "1.2.3",
    framework: "net10.0",
    assembly: "Example.Package",
    type: "Example.Widget",
    member: "Build",
    selectorKey: "method",
    metadataToken: 42,
  };

  const load = coordinator.openGraphSource(request, "Example.Widget.Build");
  assert.deepEqual(state.graphSource, {
    status: "loading",
    request,
    title: "Example.Widget.Build",
  });
  coordinator.closeGraphSource();
  // Closing is one assignment, not a flag clear plus a counter bump. The in-flight
  // request loses ownership because `state.graphSource` no longer holds the object
  // it captured, so the late resolve below cannot find its way back into state.
  assert.deepEqual(state.graphSource, closedGraphSource);
  query.resolve(source("stale graph"));
  await load;

  assert.deepEqual(state.graphSource, closedGraphSource);
  assert.deepEqual(events, [
    "render",
    "query:Build/[\"expression-bodied-members\"]",
    "cancel",
    "render",
  ]);
});

test("current graph source failures settle as visible errors", async () => {
  let renders = 0;
  const state = inspectionState();
  const coordinator = createSourceInspectionCoordinator(
    inspectionDependencies(state, {
      queryGraphSource: async () => {
        throw new Error("graph source unavailable");
      },
      render: () => renders++,
    }));

  await coordinator.openGraphSource({
    packageId: "Example.Package",
    version: "1.2.3",
    framework: "net10.0",
    assembly: "Example.Package",
    type: "Example.Widget",
    member: "Build",
    selectorKey: "method",
    metadataToken: 42,
  }, "Example.Widget.Build");

  assert.equal(state.graphSource.status, "failed");
  assert.equal(
    state.graphSource.status === "failed" ? state.graphSource.error : null,
    "graph source unavailable");
  assert.equal(renders, 2);
});

// Ownership has two sides and they are separate guards. The test above covers a stale
// *resolve*; adversarial review pointed out that deleting the guard on the *reject* path
// left the whole suite green, because nothing ever abandoned a request that was going to
// fail. A failure is exactly as capable of arriving late as a success, and it is worse
// when it does: it paints an error over a modal the user has already moved on from.
test("an abandoned graph source failure cannot overwrite the closed modal", async () => {
  const query = deferred<BrowserSource>();
  let renders = 0;
  const state = inspectionState();
  const coordinator = createSourceInspectionCoordinator(
    inspectionDependencies(state, {
      queryGraphSource: async () => query.promise,
      render: () => renders++,
    }));

  const load = coordinator.openGraphSource({
    packageId: "Example.Package",
    version: "1.2.3",
    framework: "net10.0",
    assembly: "Example.Package",
    type: "Example.Widget",
    member: "Build",
    selectorKey: "method",
    metadataToken: 42,
  }, "Example.Widget.Build");

  assert.equal(state.graphSource.status, "loading");
  coordinator.closeGraphSource();
  assert.deepEqual(state.graphSource, closedGraphSource);

  query.reject(new Error("graph source unavailable"));
  await load;

  assert.deepEqual(state.graphSource, closedGraphSource);
  // The owning request renders on start and the close renders once. A rejected request
  // that no longer owns the modal returns before the settling render, so it adds none.
  assert.equal(renders, 2);
});
