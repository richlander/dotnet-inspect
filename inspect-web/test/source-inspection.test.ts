import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";

import {
  createSourceInspectionCoordinator,
  graphSourceAutoLoadRequest,
  normalizeSourceResultSnapshot,
  sourceResultNeedsLoad,
  type SourceInspectionDependencies,
  type SourceInspectionState,
  type SourceResultState,
} from "../src/source-inspection.ts";
import type {
  BrowserSource,
  BrowserTypeSourceResult,
} from "../src/facades/inspect-web-source.d.ts";
import type { MemberFocusSnapshot } from "../src/member-focus.ts";
import { createOperationAuthorityPage } from "../src/operation-authority.ts";

function source(text: string): BrowserSource {
  return {
    provider: "pdb",
    provenance: "SourceLink",
    url: "https://example.test/source.cs",
    pdbSourceLimitation: null,
    text,
  };
}

function typeSource(text: string): BrowserTypeSourceResult {
  return {
    version: 1,
    kind: "Succeeded",
    value: source(text),
    failureKind: null,
    error: null,
    diagnostic: null,
    reason: null,
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

function sourceText(value: SourceResultState): string | undefined {
  return value.status === "ready" ? value.source.text : undefined;
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
    memberSource: { status: "idle" },
    typeSource: { status: "idle" },
    graphSource: { status: "closed" },
    taste: [],
    ...overrides,
  };
}

function inspectionDependencies(
  state: SourceInspectionState,
  overrides: Partial<Omit<SourceInspectionDependencies, "state">> = {},
): SourceInspectionDependencies {
  let nextOperationId = 1;
  return {
    state,
    operationAuthority: createOperationAuthorityPage({
      allocation: {
        createId: () => `source-operation-${nextOperationId++}`,
      },
    }),
    queryMemberSource: async () => source("member"),
    queryTypeSource: async () => typeSource("type"),
    queryGraphSource: async () => source("graph"),
    memberSourceHasConcreteOverload: () => true,
    cancelEngineSourceRequest: () => {},
    cancelTypeSourceRequest: () => {},
    reportOperationDiagnostic: () => undefined,
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
  const promise = new Promise<T>(accept => {
    resolve = accept;
  });
  return { promise, resolve };
}

test("Source composition uses shell actions and a full-area loaded surface", () => {
  const appSource = readFileSync(
    new URL("../src/dotnet-inspect.ts", import.meta.url),
    "utf8",
  );

  assert.match(
    appSource,
    /const sourcePageKind =[\s\S]*activeScope === "type" && state\.lens === "source"[\s\S]*activeScope === "member"[\s\S]*state\.memberSection === "source"/);
  assert.match(
    appSource,
    /class="working-surface-actions" role="group" aria-label="\$\{metadataWorkingSurface \? "Type graph actions" : packageDependenciesWorkingSurface \? "Dependency graph actions" : callGraphPageContext \? "Call graph actions" : annotatedPageContext \? "Annotated Source actions" : sourcePageKind \? "Source actions" : "Member actions"\}"[\s\S]*renderSourcePageActions\(\{[\s\S]*copyButtonId: sourcePageKind === "member"[\s\S]*"copy-source"[\s\S]*"copy-type-source"/);
  assert.match(
    appSource,
    /contextualActionsHtml: annotatedPageContext \|\| sourcePageKind[\s\S]*class="working-surface-actions"/);
  assert.doesNotMatch(
    appSource,
    /class="legacy-application-actions"/);
  assert.match(
    appSource,
    /detail-scroll\$\{annotatedWorkingSurface \? " annotated-working-surface" : ""\}\$\{sourceWorkingSurface \? " source-working-surface" : ""\}/);
  assert.match(
    appSource,
    /case "source":\s*return renderTypeSourceHtml\(item\);/);
  assert.match(
    appSource,
    /function renderMemberSourceHtml\(\) \{[\s\S]*switch \(state\.memberSource\.status\)[\s\S]*case "ready":[\s\S]*return renderSourceResult\(\{[\s\S]*source: state\.memberSource\.source/);
});

async function promiseSettled(promise: Promise<unknown>): Promise<boolean> {
  let settled = false;
  void promise.then(() => {
    settled = true;
    return undefined;
  });
  await Promise.resolve();
  return settled;
}

test("hidden source work is cancelled through the shared engine boundary", () => {
  let cancellations = 0;
  const state = inspectionState({
    settings: true,
    memberSource: { status: "loading", signature: "member" },
  });
  const coordinator = createSourceInspectionCoordinator(
    inspectionDependencies(state, {
      cancelEngineSourceRequest: () => cancellations++,
    }));

  coordinator.cancelHiddenRequest();

  assert.equal(cancellations, 1);
  assert.deepEqual(state.memberSource, { status: "idle" });

  state.settings = false;
  state.memberSource = { status: "loading", signature: "member" };
  coordinator.cancelHiddenRequest();
  assert.equal(cancellations, 1);
  assert.deepEqual(
    state.memberSource,
    { status: "loading", signature: "member" });
});

test("canonical transitions cancel visible source work before snapshot", () => {
  let cancellations = 0;
  const state = inspectionState({
    memberSource: { status: "loading", signature: "member" },
  });
  const coordinator = createSourceInspectionCoordinator(
    inspectionDependencies(state, {
      cancelEngineSourceRequest: () => cancellations++,
    }));

  assert.equal(coordinator.cancelCurrentRequest(), true);
  assert.equal(cancellations, 1);
  assert.deepEqual(state.memberSource, { status: "idle" });
  assert.equal(coordinator.cancelCurrentRequest(), false);
  assert.equal(cancellations, 1);
});

test("member picker releases source ownership before taste invalidation", () => {
  let cancellations = 0;
  let hasConcreteOverload = true;
  const state = inspectionState({
    memberSource: { status: "loading", signature: "member" },
  });
  const coordinator = createSourceInspectionCoordinator(
    inspectionDependencies(state, {
      memberSourceHasConcreteOverload: () => hasConcreteOverload,
      cancelEngineSourceRequest: () => cancellations++,
    }));

  hasConcreteOverload = false;
  coordinator.cancelHiddenRequest();

  assert.equal(cancellations, 1);
  assert.deepEqual(state.memberSource, { status: "idle" });

  coordinator.cancelHiddenRequest();
  assert.equal(cancellations, 1);
  assert.deepEqual(state.memberSource, { status: "idle" });
});

test("canonical commit clears a settled graph source without rendering", () => {
  let renders = 0;
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
  const state = inspectionState({
    graphSource: {
      status: "ready",
      request,
      title: "Old workspace",
      source: source("old workspace"),
    },
  });
  const coordinator = createSourceInspectionCoordinator(
    inspectionDependencies(state, {
      render: () => renders++,
    }));

  coordinator.clearGraphSource();

  assert.deepEqual(state.graphSource, { status: "closed" });
  assert.equal(renders, 0);
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
  assert.deepEqual(
    state.memberSource,
    { status: "loading", signature: "member-signature" });
  current = false;
  query.resolve(source("stale"));
  await load;

  assert.deepEqual(state.memberSource, { status: "idle" });
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

  assert.deepEqual(state.memberSource, {
    status: "failed",
    signature: "member-signature",
    error: "source unavailable",
  });
  assert.deepEqual(renders, [null, "#type-list"]);
});

test("empty member source failure remains settled", async () => {
  let queries = 0;
  const state = inspectionState();
  const coordinator = createSourceInspectionCoordinator(
    inspectionDependencies(state, {
      queryMemberSource: async () => {
        queries++;
        throw new Error("");
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

  assert.deepEqual(state.memberSource, {
    status: "failed",
    signature: "member-signature",
    error: "",
  });
  assert.equal(sourceResultNeedsLoad(state.memberSource, "member-signature"), false);
  assert.equal(queries, 1);
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
        return typeSource("type");
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
  assert.equal(sourceText(state.typeSource), "type");
  assert.equal(renders, 1);

  await coordinator.loadTypeSource(request);
  assert.equal(queries, 1);
  assert.equal(renders, 2);
});

test("type source replacement suppresses stale publication without cancelling the replacement", async () => {
  const firstQuery = deferred<BrowserTypeSourceResult>();
  const secondQuery = deferred<BrowserTypeSourceResult>();
  let cancellations = 0;
  const state = inspectionState({
    lens: "source",
    selectedMemberKey: "",
    memberSection: "overview",
  });
  const coordinator = createSourceInspectionCoordinator(
    inspectionDependencies(state, {
      queryTypeSource: (_operationId, request) =>
        request.type === "Example.First"
          ? firstQuery.promise
          : secondQuery.promise,
      cancelTypeSourceRequest: () => cancellations++,
    }));
  const request = {
    packageId: "Example.Package",
    version: "1.2.3",
    framework: "net10.0",
    assembly: "Example.Package",
    taste: "[]",
    isVisible: () => true,
  };

  const firstLoad = coordinator.loadTypeSource({
    ...request,
    signature: "first",
    type: "Example.First",
  });
  const secondLoad = coordinator.loadTypeSource({
    ...request,
    signature: "second",
    type: "Example.Second",
  });

  assert.equal(cancellations, 1);
  assert.deepEqual(
    state.typeSource,
    { status: "loading", signature: "second" });
  firstQuery.resolve(typeSource("stale"));
  await firstLoad;
  assert.deepEqual(
    state.typeSource,
    { status: "loading", signature: "second" });

  secondQuery.resolve(typeSource("current"));
  await secondLoad;
  assert.equal(sourceText(state.typeSource), "current");
});

test("synchronous type source failure cannot cancel a reentrant replacement", async () => {
  const replacementQuery = deferred<BrowserTypeSourceResult>();
  let cancellations = 0;
  let replacementLoad: Promise<void> | undefined;
  const state = inspectionState({
    lens: "source",
    selectedMemberKey: "",
    memberSection: "overview",
  });
  let coordinator!: ReturnType<typeof createSourceInspectionCoordinator>;
  coordinator = createSourceInspectionCoordinator(
    inspectionDependencies(state, {
      queryTypeSource: (_operationId, request) => {
        if (request.type === "Example.First") {
          replacementLoad = coordinator.loadTypeSource({
            signature: "second",
            packageId: "Example.Package",
            version: "1.2.3",
            framework: "net10.0",
            assembly: "Example.Package",
            type: "Example.Second",
            taste: "[]",
            isVisible: () => true,
          });
          throw new Error("first activation failed");
        }
        return replacementQuery.promise;
      },
      cancelTypeSourceRequest: () => cancellations++,
    }));

  await coordinator.loadTypeSource({
    signature: "first",
    packageId: "Example.Package",
    version: "1.2.3",
    framework: "net10.0",
    assembly: "Example.Package",
    type: "Example.First",
    taste: "[]",
    isVisible: () => true,
  });

  assert.equal(cancellations, 1);
  assert.deepEqual(
    state.typeSource,
    { status: "loading", signature: "second" });
  replacementQuery.resolve(typeSource("replacement"));
  assert.ok(replacementLoad);
  await replacementLoad;
  assert.equal(sourceText(state.typeSource), "replacement");
});

test("synchronous type source failure does not repeat reentrant cancellation", async () => {
  let cancellations = 0;
  const state = inspectionState({
    lens: "source",
    selectedMemberKey: "",
    memberSection: "overview",
  });
  let coordinator!: ReturnType<typeof createSourceInspectionCoordinator>;
  coordinator = createSourceInspectionCoordinator(
    inspectionDependencies(state, {
      queryTypeSource: () => {
        assert.equal(coordinator.cancelCurrentRequest(), true);
        throw new Error("activation failed after cancellation");
      },
      cancelTypeSourceRequest: () => cancellations++,
    }));

  await coordinator.loadTypeSource({
    signature: "first",
    packageId: "Example.Package",
    version: "1.2.3",
    framework: "net10.0",
    assembly: "Example.Package",
    type: "Example.First",
    taste: "[]",
    isVisible: () => true,
  });

  assert.equal(cancellations, 1);
  assert.deepEqual(state.typeSource, { status: "idle" });
});

test("legacy member source takeover cancels the authoritative type operation first", async () => {
  const typeQuery = deferred<BrowserTypeSourceResult>();
  let cancellations = 0;
  const state = inspectionState({
    lens: "source",
    selectedMemberKey: "",
    memberSection: "overview",
  });
  const coordinator = createSourceInspectionCoordinator(
    inspectionDependencies(state, {
      queryTypeSource: async () => typeQuery.promise,
      cancelTypeSourceRequest: () => cancellations++,
    }));
  const typeLoad = coordinator.loadTypeSource({
    signature: "type",
    packageId: "Example.Package",
    version: "1.2.3",
    framework: "net10.0",
    assembly: "Example.Package",
    type: "Example.Widget",
    taste: "[]",
    isVisible: () => false,
  });

  await coordinator.loadMemberSource({
    signature: "member",
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

  assert.equal(cancellations, 1);
  assert.equal(sourceText(state.memberSource), "member");
  assert.deepEqual(state.typeSource, { status: "idle" });
  typeQuery.resolve(typeSource("stale type"));
  await typeLoad;
  assert.deepEqual(state.typeSource, { status: "idle" });
});

test("current type source failures remain visible and restore focus", async () => {
  const renders: Array<string | null> = [];
  const state = inspectionState({
    lens: "source",
    selectedMemberKey: "",
    memberSection: "overview",
  });
  const coordinator = createSourceInspectionCoordinator(
    inspectionDependencies(state, {
      queryTypeSource: async () => {
        throw new Error("type source unavailable");
      },
      renderPreservingMemberFocus: fallback => {
        renders.push(fallback?.selector ?? null);
        return fallback ?? focusSnapshot("#type-list");
      },
    }));

  await coordinator.loadTypeSource({
    signature: "type",
    packageId: "Example.Package",
    version: "1.2.3",
    framework: "net10.0",
    assembly: "Example.Package",
    type: "Example.Widget",
    taste: "[]",
    isVisible: () => true,
  });

  assert.deepEqual(state.typeSource, {
    status: "failed",
    signature: "type",
    error: "type source unavailable",
  });
  assert.deepEqual(renders, [null, "#type-list"]);
});

test("type cancellation completes logically before the query quiesces", async () => {
  const query = deferred<BrowserTypeSourceResult>();
  let cancellations = 0;
  const state = inspectionState({
    lens: "source",
    selectedMemberKey: "",
    memberSection: "overview",
  });
  const coordinator = createSourceInspectionCoordinator(
    inspectionDependencies(state, {
      queryTypeSource: async () => query.promise,
      cancelTypeSourceRequest: () => cancellations++,
    }));
  const load = coordinator.loadTypeSource({
    signature: "type",
    packageId: "Example.Package",
    version: "1.2.3",
    framework: "net10.0",
    assembly: "Example.Package",
    type: "Example.Widget",
    taste: "[]",
    isVisible: () => true,
  });

  assert.equal(coordinator.cancelCurrentRequest(), true);
  assert.equal(cancellations, 1);
  assert.deepEqual(state.typeSource, { status: "idle" });
  assert.equal(await promiseSettled(load), false);

  query.resolve(typeSource("late"));
  await load;
  assert.deepEqual(state.typeSource, { status: "idle" });
  assert.equal(coordinator.cancelCurrentRequest(), false);
  assert.equal(cancellations, 1);
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
  query.resolve(source("stale graph"));
  await load;

  assert.deepEqual(state.graphSource, { status: "closed" });
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

  assert.deepEqual(state.graphSource, {
    status: "failed",
    request: {
      packageId: "Example.Package",
      version: "1.2.3",
      framework: "net10.0",
      assembly: "Example.Package",
      type: "Example.Widget",
      member: "Build",
      selectorKey: "method",
      metadataToken: 42,
    },
    title: "Example.Widget.Build",
    error: "graph source unavailable",
  });
  assert.equal(renders, 2);
});

test("empty graph source failure settles without automatic reload", async () => {
  const state = inspectionState();
  const coordinator = createSourceInspectionCoordinator(
    inspectionDependencies(state, {
      queryGraphSource: async () => {
        throw new Error("");
      },
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

  await coordinator.openGraphSource(request, "Example.Widget.Build");

  assert.deepEqual(state.graphSource, {
    status: "failed",
    request,
    title: "Example.Widget.Build",
    error: "",
  });
  assert.equal(graphSourceAutoLoadRequest(state.graphSource), null);
});

test("cancelled graph source retains the sole automatic reload request", async () => {
  const query = deferred<BrowserSource>();
  const state = inspectionState();
  const coordinator = createSourceInspectionCoordinator(
    inspectionDependencies(state, {
      queryGraphSource: async () => query.promise,
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
  assert.equal(coordinator.cancelCurrentRequest(), true);
  assert.deepEqual(graphSourceAutoLoadRequest(state.graphSource), {
    request,
    title: "Example.Widget.Build",
  });
  query.resolve(source("stale graph"));
  await load;
  assert.equal(state.graphSource.status, "cancelled");
});

test("source result loading eligibility and snapshots distinguish settled failure", () => {
  const emptyFailure = {
    status: "failed",
    signature: "same",
    error: "",
  } as const;
  assert.equal(sourceResultNeedsLoad({ status: "idle" }, "same"), true);
  assert.equal(
    sourceResultNeedsLoad(
      { status: "loading", signature: "same" },
      "same"),
    false);
  assert.equal(sourceResultNeedsLoad(emptyFailure, "same"), false);
  assert.equal(sourceResultNeedsLoad(emptyFailure, "other"), true);
  assert.deepEqual(
    normalizeSourceResultSnapshot({
      status: "loading",
      signature: "same",
    }),
    { status: "idle" });
  assert.equal(normalizeSourceResultSnapshot(emptyFailure), emptyFailure);
});

test("missing graph source payload settles without automatic reload", async () => {
  const state = inspectionState();
  const coordinator = createSourceInspectionCoordinator(
    inspectionDependencies(state, {
      queryGraphSource: async () => null,
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

  await coordinator.openGraphSource(request, "Example.Widget.Build");

  assert.deepEqual(state.graphSource, {
    status: "failed",
    request,
    title: "Example.Widget.Build",
    error: "",
  });
  assert.equal(graphSourceAutoLoadRequest(state.graphSource), null);
});
