import assert from "node:assert/strict";
import test from "node:test";

import {
  closedGraphSource,
  createSourceInspectionCoordinator,
  graphSourceAutoLoad,
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

// This is the one place the slice is not behavior-preserving, and it is deliberate.
//
// The old auto-reload guard asked `!loading && !result && !error`, which an engine
// rejection carrying an empty message satisfied exactly: `describeError` returns "" for
// `new Error("")`, a thrown non-Error, or a thrown null. The modal therefore looked like
// it had never been attempted, and since the auto-reload runs at the end of every
// `render()`, it re-issued the request -- a render/reload/render loop with no bound.
//
// The union distinguishes the two states the old fields could not: a failure with no
// message is `failed`, and only `cancelled` -- open, unsettled, nothing in flight -- is
// the state the auto-reload is for. Adversarial review found this divergence; these two
// tests pin both halves of it.
test("an empty engine rejection settles as a failure, not as unattempted work", async () => {
  const state = inspectionState();
  const coordinator = createSourceInspectionCoordinator(
    inspectionDependencies(state, {
      queryGraphSource: async () => {
        throw new Error("");
      },
      describeError: (error: unknown) =>
        error instanceof Error ? error.message : "",
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

  // Not `cancelled`, which is what the auto-reload acts on. The modal settles and stays
  // settled instead of re-requesting on every render.
  assert.equal(state.graphSource.status, "failed");
  assert.equal(
    state.graphSource.status === "failed" ? state.graphSource.error : null,
    "");

  // The disclosed lifecycle change, gated rather than only described. The predecessor
  // left `loading=false`, `source=null`, `error=""` for a message-less rejection, and its
  // auto-load predicate read that as work never attempted -- so it reissued on every
  // render, forever. Settling as `failed` ends that loop, and this is the assertion that
  // says so rather than a comment claiming it.
  assert.equal(
    graphSourceAutoLoad(state.graphSource),
    null,
    "an empty-message rejection settles instead of retrying on every render");
});

test("a missing source payload settles without dereferencing it", async () => {
  const state = inspectionState();
  const coordinator = createSourceInspectionCoordinator(
    inspectionDependencies(state, {
      // The engine type promises a source; the hand-written declaration is not a runtime
      // guarantee, and the previous renderer guarded against exactly this.
      // The declared return type forbids this value; producing it is the point.
      // oxlint-disable-next-line typescript/no-unsafe-type-assertion
      queryGraphSource: (async () => null) as unknown as
        SourceInspectionDependencies["queryGraphSource"],
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
    "");

  // The second route to the same divergence, and the one round 2 review measured:
  // `oldWouldReload: true` against `currentWouldReload: false`. A falsy payload used to
  // look identical to unattempted work, so it retried on every render too.
  assert.equal(
    graphSourceAutoLoad(state.graphSource),
    null,
    "a falsy payload settles instead of retrying on every render");
});

// A -> B -> A, on both settlement paths.
//
// Ownership of the graph modal is the identity of the pending state object, not the
// identity of the request. Round 2 review (GPT-5.6 Sol) weakened both guards to compare
// requests instead -- rejecting a late result only when a *different* request had taken
// over -- and the whole suite stayed green, because nothing reopened the same request.
// That is the exact shape a user produces by opening a member's graph, opening another,
// and coming back: the first attempt's late result lands on the third attempt's modal.
//
// Two attempts at the same request are two different pieces of work, and the first one's
// answer is stale even though it "matches". These tests say that by outcome, which makes
// the source-text assertion about the guard's spelling unnecessary.
function settle<T>(
  queries: readonly ReturnType<typeof deferred<T>>[],
  index: number,
): ReturnType<typeof deferred<T>> {
  const query = queries[index];
  assert.ok(query, `no graph query was started at index ${index}`);
  return query;
}

// Read the modal's payload without narrowing `state.graphSource` for the rest of the test:
// a status assertion narrows the field permanently, and these tests assert on it more than
// once as later attempts settle.
function graphSourceText(graphSource: SourceInspectionState["graphSource"]): string | null {
  return graphSource.status === "ready" ? graphSource.source.text : null;
}

function graphRequest(member: string) {
  return {
    packageId: "Example.Package",
    version: "1.2.3",
    framework: "net10.0",
    assembly: "Example.Package",
    type: "Example.Widget",
    member,
    selectorKey: `method:${member}`,
    metadataToken: 42,
  };
}

test("a superseded graph request does not publish its result over a later attempt "
  + "at the same request", async () => {
  const queries: Array<ReturnType<typeof deferred<BrowserSource>>> = [];
  const state = inspectionState();
  const coordinator = createSourceInspectionCoordinator(
    inspectionDependencies(state, {
      queryGraphSource: async () => {
        const query = deferred<BrowserSource>();
        queries.push(query);
        return query.promise;
      },
    }));

  // The same request object throughout, so a guard that compares requests sees a match
  // on every resumption and a guard that compares pending states does not.
  const a = graphRequest("Build");
  const b = graphRequest("Dispose");

  const firstA = coordinator.openGraphSource(a, "Widget.Build");
  const openB = coordinator.openGraphSource(b, "Widget.Dispose");
  const secondA = coordinator.openGraphSource(a, "Widget.Build");
  assert.equal(state.graphSource.status, "loading");

  // Settle each stage before starting the next, so "the first attempt resolves last" is a
  // fact about the test rather than about microtask ordering.
  settle(queries, 1).resolve(source("B source"));
  await openB;
  settle(queries, 2).resolve(source("second A source"));
  await secondA;
  assert.equal(
    graphSourceText(state.graphSource),
    "second A source",
    "the attempt the user is waiting on publishes its own result");

  // The first attempt's answer arrives long after the user moved away and came back.
  settle(queries, 0).resolve(source("first A source"));
  await firstA;

  assert.equal(state.graphSource.status, "ready");
  assert.equal(
    graphSourceText(state.graphSource),
    "second A source",
    "a superseded attempt does not overwrite the attempt that replaced it");
});

test("a superseded graph request does not publish its rejection over a later attempt "
  + "at the same request", async () => {
  const queries: Array<ReturnType<typeof deferred<BrowserSource>>> = [];
  const state = inspectionState();
  const coordinator = createSourceInspectionCoordinator(
    inspectionDependencies(state, {
      queryGraphSource: async () => {
        const query = deferred<BrowserSource>();
        queries.push(query);
        return query.promise;
      },
    }));

  const a = graphRequest("Build");
  const b = graphRequest("Dispose");

  const firstA = coordinator.openGraphSource(a, "Widget.Build");
  const openB = coordinator.openGraphSource(b, "Widget.Dispose");
  const secondA = coordinator.openGraphSource(a, "Widget.Build");

  settle(queries, 1).reject(new Error("B failed"));
  settle(queries, 0).reject(new Error("first A failed"));
  // Deliberately not awaiting `secondA`: the point of this test is what the modal shows
  // while the third attempt is still in flight.
  await Promise.all([firstA, openB]);

  // The third attempt is still in flight. A stale rejection must not paint an error over
  // it -- the user would see a failure for work that has not finished.
  assert.equal(
    state.graphSource.status,
    "loading",
    "a stale rejection does not settle the attempt still running");

  settle(queries, 2).resolve(source("second A source"));
  await secondA;
  assert.equal(state.graphSource.status, "ready");
});
