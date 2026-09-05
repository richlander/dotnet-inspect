import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";

import {
  createSourceInspectionCoordinator,
  type SourceInspectionDependencies,
  type SourceInspectionState,
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

function sourceText(value: BrowserSource | null): string | undefined {
  return value?.text;
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
    graphSourceOpen: false,
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
    graphSource: null,
    graphSourceLoading: false,
    graphSourceError: "",
    graphSourceTitle: "",
    graphSourceRequest: null,
    graphSourceSeq: 0,
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
    /class="working-surface-actions" role="group" aria-label="\$\{metadataWorkingSurface \? "Type graph actions" : packageDependenciesWorkingSurface \? "Dependency graph actions" : callGraphPageContext \? "Call graph actions" : annotatedPageContext \? "Annotated Source actions" : "Source actions"\}"[\s\S]*renderSourcePageActions\(\{[\s\S]*copyButtonId: sourcePageKind === "member"[\s\S]*"copy-source"[\s\S]*"copy-type-source"/);
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
    /state\.memberSource\s*\?\s*renderSourceResult\(\{/);
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

test("canonical transitions cancel visible source work before snapshot", () => {
  let cancellations = 0;
  const state = inspectionState({
    memberSourceLoading: true,
    memberSourceKey: "member",
  });
  const coordinator = createSourceInspectionCoordinator(
    inspectionDependencies(state, {
      cancelEngineSourceRequest: () => cancellations++,
    }));

  assert.equal(coordinator.cancelCurrentRequest(), true);
  assert.equal(cancellations, 1);
  assert.equal(state.sourceRequestGeneration, 1);
  assert.equal(state.memberSourceLoading, false);
  assert.equal(state.memberSourceKey, "");
  assert.equal(coordinator.cancelCurrentRequest(), false);
  assert.equal(cancellations, 1);
});

test("member picker releases source ownership before taste invalidation", () => {
  let cancellations = 0;
  let hasConcreteOverload = true;
  const state = inspectionState({
    memberSourceLoading: true,
    memberSourceKey: "member",
  });
  const coordinator = createSourceInspectionCoordinator(
    inspectionDependencies(state, {
      memberSourceHasConcreteOverload: () => hasConcreteOverload,
      cancelEngineSourceRequest: () => cancellations++,
    }));

  hasConcreteOverload = false;
  coordinator.cancelHiddenRequest();

  assert.equal(cancellations, 1);
  assert.equal(state.sourceRequestGeneration, 1);
  assert.equal(state.memberSourceLoading, false);
  assert.equal(state.memberSourceKey, "");

  state.memberSourceKey = "";
  coordinator.cancelHiddenRequest();
  assert.equal(cancellations, 1);
  assert.equal(state.memberSourceLoading, false);
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
    graphSourceOpen: true,
    graphSource: source("old workspace"),
    graphSourceTitle: "Old workspace",
    graphSourceRequest: { request, title: "Old workspace" },
    graphSourceSeq: 4,
  });
  const coordinator = createSourceInspectionCoordinator(
    inspectionDependencies(state, {
      render: () => renders++,
    }));

  coordinator.clearGraphSource();

  assert.equal(state.graphSourceOpen, false);
  assert.equal(state.graphSource, null);
  assert.equal(state.graphSourceRequest, null);
  assert.equal(state.graphSourceSeq, 5);
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
  assert.equal(state.typeSource?.text, "type");
  assert.equal(state.typeSourceLoading, false);
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
  assert.equal(state.typeSourceKey, "second");
  firstQuery.resolve(typeSource("stale"));
  await firstLoad;
  assert.equal(state.typeSource, null);
  assert.equal(state.typeSourceLoading, true);

  secondQuery.resolve(typeSource("current"));
  await secondLoad;
  assert.equal(sourceText(state.typeSource), "current");
  assert.equal(state.typeSourceLoading, false);
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
  assert.equal(state.typeSourceKey, "second");
  assert.equal(state.typeSourceLoading, true);
  replacementQuery.resolve(typeSource("replacement"));
  assert.ok(replacementLoad);
  await replacementLoad;
  assert.equal(sourceText(state.typeSource), "replacement");
  assert.equal(state.typeSourceLoading, false);
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
  assert.equal(state.typeSourceKey, "");
  assert.equal(state.typeSourceLoading, false);
  assert.equal(state.typeSourceError, "");
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
  assert.equal(state.memberSource?.text, "member");
  assert.equal(state.typeSource, null);
  typeQuery.resolve(typeSource("stale type"));
  await typeLoad;
  assert.equal(state.typeSource, null);
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

  assert.equal(state.typeSourceError, "type source unavailable");
  assert.equal(state.typeSourceLoading, false);
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
  assert.equal(state.typeSourceLoading, false);
  assert.equal(state.typeSourceKey, "");
  assert.equal(await promiseSettled(load), false);

  query.resolve(typeSource("late"));
  await load;
  assert.equal(state.typeSource, null);
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
  assert.equal(state.graphSourceOpen, true);
  assert.equal(state.graphSourceSeq, 1);
  assert.deepEqual(state.graphSourceRequest, {
    request,
    title: "Example.Widget.Build",
  });
  coordinator.closeGraphSource();
  assert.equal(state.graphSourceSeq, 3);
  query.resolve(source("stale graph"));
  await load;

  assert.equal(state.graphSourceOpen, false);
  assert.equal(state.graphSource, null);
  assert.equal(state.graphSourceRequest, null);
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

  assert.equal(state.graphSourceError, "graph source unavailable");
  assert.equal(state.graphSourceLoading, false);
  assert.equal(renders, 2);
});
