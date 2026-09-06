import assert from "node:assert/strict";
import test from "node:test";

import { sampleDocument } from "../../annotated-source-viewer/src/sample-document.js";
import type { AnnotatedSourceResult } from "../src/annotated-source.ts";
import {
  createAnnotatedSourceViewerModel,
  createEmbeddedSession,
} from "../src/annotated-source-session.ts";
import { validateAnnotatedSourceDocument } from "../src/annotated-source-view.ts";
import { sampleViewerCatalog } from "./annotated-source-result-fixture.ts";
import {
  cancelFindingCensusRequest,
  createMemberDetailInspectionCoordinator,
  type DocumentableMemberSurface,
  type MemberDetailInspectionDependencies,
  type MemberDetailInspectionState,
  type MemberDocumentationRequest,
  type MemberFindingCensusRequest,
  type MemberFacts,
  type MemberFactsRequest,
} from "../src/member-detail-inspection.ts";
import {
  createMemberFindingInteraction,
  selectFindingInstance,
  type MemberFindingCensus,
} from "../src/finding-interaction.ts";
import type { MemberFocusSnapshot } from "../src/member-focus.ts";
import {
  createAppMemberSurface,
  type AppMemberSurface,
} from "../src/package-acquisition.ts";
import type {
  BrowserMemberSurface,
} from "../src/facades/inspect-web-package.d.ts";
import {
  memberFindingCensusFixture,
} from "./member-finding-census-fixture.ts";

function wireMemberSurface(
  overrides: Partial<BrowserMemberSurface> = {},
): BrowserMemberSurface {
  return {
    name: "Run",
    kind: "Method",
    signature: "void Run(string value)",
    accessibility: "Public",
    isStatic: false,
    isUnsafe: false,
    isVirtual: false,
    isAbstract: false,
    isOverride: false,
    isExtension: false,
    isObsolete: false,
    genericArity: 0,
    metadataToken: 0x06000001,
    returnType: "void",
    parameters: [{
      name: "value",
      type: "string",
      modifier: null,
      hasDefault: false,
      defaultValue: null,
      description: null,
    }],
    documentationId: "M:Example.Widget.Run(System.String)",
    summary: null,
    returns: null,
    exceptions: [],
    stableSelector: "Run(string)",
    anchorDigest: "abc123",
    canonicalSignature: "void Example.Widget.Run(string value)",
    graphSelectorKey: "Run|System.String",
    bodySelectors: [],
    ...overrides,
  };
}

function memberSurface(
  overrides: Partial<AppMemberSurface> = {},
): DocumentableMemberSurface {
  return {
    ...createAppMemberSurface(wireMemberSurface()),
    ...overrides,
  };
}

function generatedMemberSurfaceRejectsMutation(
  surface: BrowserMemberSurface,
): void {
  // @ts-expect-error Generated wire properties are producer-owned snapshots.
  surface.summary = "application state";
  // @ts-expect-error Nested generated wire records are readonly.
  surface.parameters[0]!.description = "application state";
  // @ts-expect-error Generated wire collections are readonly.
  surface.exceptions[0] = {
    type: "System.InvalidOperationException",
    description: "application state",
  };
}
void generatedMemberSurfaceRejectsMutation;

function factsResult(): MemberFacts {
  return {
    metadataToken: 0x06000001,
    signals: {
      allocations: 0,
      copies: 0,
      reflection: 0,
      throws: 0,
      catches: 0,
      finallys: 0,
      unsafe: false,
      allocatesInLoop: false,
      evidenceOffsets: [],
      exceptionTypes: [],
    },
    allocations: [],
    calls: [],
    safety: [],
    exceptionRegions: [],
    performanceOpportunities: [],
    diagnostics: [],
  };
}

function annotatedResult(): AnnotatedSourceResult {
  const document: unknown = sampleDocument;
  validateAnnotatedSourceDocument(document);
  return {
    document,
    viewerCatalog: sampleViewerCatalog,
    provenance: "decompiled from IL",
    contextLimitation: null,
  };
}

function findingCensusResult(): MemberFindingCensus {
  return memberFindingCensusFixture();
}

function inspectionState(
  overrides: Partial<MemberDetailInspectionState> = {},
): MemberDetailInspectionState {
  return {
    memberAnnotated: null,
    memberAnnotatedLoading: false,
    memberAnnotatedError: "",
    memberAnnotatedKey: "",
    memberAnnotatedEmbedded: null,
    memberAnnotatedModal: null,
    memberFindingInteraction: null,
    memberFindingSelectionError: "",
    memberFacts: null,
    memberFactsLoading: false,
    memberFactsError: "",
    memberFactsKey: "",
    memberDocumentationLoading: false,
    memberDocumentationError: "",
    memberDocumentationKey: "",
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

function documentationRequest(
  overload: DocumentableMemberSurface,
  overrides: Partial<MemberDocumentationRequest> = {},
): MemberDocumentationRequest {
  return {
    signature: "documentation",
    packageId: "Example.Package",
    version: "1.2.3",
    framework: "net10.0",
    assembly: "Example.Package.dll",
    overload,
    isRuntimePack: false,
    isCurrent: () => true,
    ...overrides,
  };
}

function findingCensusRequest(
  overrides: Partial<MemberFindingCensusRequest> = {},
): MemberFindingCensusRequest {
  return {
    signature: "annotated",
    packageId: "Example.Package",
    version: "1.2.3",
    framework: "net10.0",
    assembly: "Example.Package.dll",
    typeIdentity: "T:Example.Widget",
    type: "Example.Widget",
    member: "Run",
    memberSignature: "void Run(string value)",
    selectorKey: "Run|System.String",
    metadataToken: 0x06000001,
    taste: "[\"prefer-expression-bodied-members\"]",
    isCurrent: () => true,
    ...overrides,
  };
}

function factsRequest(
  overrides: Partial<MemberFactsRequest> = {},
): MemberFactsRequest {
  return {
    signature: "facts",
    packageId: "Example.Package",
    version: "1.2.3",
    framework: "net10.0",
    assembly: "Example.Package.dll",
    type: "Example.Widget",
    typeIdentity: "T:Example.Widget",
    member: "Run",
    memberSignature: "void Run(string value)",
    selectorKey: "Run|System.String",
    metadataToken: 0x06000001,
    implementationBodySelected: false,
    isCurrent: () => true,
    ...overrides,
  };
}

function inspectionDependencies(
  state: MemberDetailInspectionState,
  overrides: Partial<Omit<MemberDetailInspectionDependencies, "state">> = {},
): MemberDetailInspectionDependencies {
  return {
    state,
    queryDocumentation: async () => ({
      summary: "Runs the widget.",
      returns: null,
      parameters: { value: "The value to run." },
      exceptions: [],
    }),
    queryFindingCensus: async () => findingCensusResult(),
    queryFacts: async () => factsResult(),
    describeError: error =>
      error instanceof Error ? error.message : String(error),
    render: () => {},
    renderPreservingMemberFocus: () => focusSnapshot(),
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

test("runtime members settle documentation without querying a companion package", async () => {
  const overload = memberSurface();
  let queries = 0;
  let renders = 0;
  const state = inspectionState({
    memberDocumentationKey: "previous",
    memberDocumentationLoading: true,
    memberDocumentationError: "previous failure",
  });
  const coordinator = createMemberDetailInspectionCoordinator(
    inspectionDependencies(state, {
      queryDocumentation: async () => {
        queries++;
        throw new Error("unexpected query");
      },
      render: () => renders++,
    }));

  await coordinator.loadDocumentation(documentationRequest(overload, {
    isRuntimePack: true,
  }));

  assert.equal(queries, 0);
  assert.equal(renders, 1);
  assert.equal(overload.documentationLoaded, true);
  assert.equal(state.memberDocumentationKey, "documentation");
  assert.equal(state.memberDocumentationLoading, false);
  assert.equal(state.memberDocumentationError, "");
});

test("members without documentation ids settle without querying", async () => {
  const overload = memberSurface({ documentationId: null });
  let queries = 0;
  let renders = 0;
  const state = inspectionState();
  const coordinator = createMemberDetailInspectionCoordinator(
    inspectionDependencies(state, {
      queryDocumentation: async () => {
        queries++;
        throw new Error("unexpected query");
      },
      render: () => renders++,
    }));

  await coordinator.loadDocumentation(documentationRequest(overload));

  assert.equal(queries, 0);
  assert.equal(renders, 1);
  assert.equal(overload.documentationLoaded, undefined);
  assert.equal(state.memberDocumentationLoading, false);
});

test("already-loaded member documentation renders without querying again", async () => {
  const overload = memberSurface({ documentationLoaded: true });
  let queries = 0;
  let renders = 0;
  const state = inspectionState({
    memberDocumentationKey: "previous",
    memberDocumentationLoading: true,
    memberDocumentationError: "previous failure",
  });
  const coordinator = createMemberDetailInspectionCoordinator(
    inspectionDependencies(state, {
      queryDocumentation: async () => {
        queries++;
        throw new Error("unexpected query");
      },
      render: () => renders++,
    }));

  await coordinator.loadDocumentation(documentationRequest(overload));

  assert.equal(queries, 0);
  assert.equal(renders, 1);
  assert.equal(state.memberDocumentationKey, "documentation");
  assert.equal(state.memberDocumentationLoading, false);
  assert.equal(state.memberDocumentationError, "");
});

test("duplicate in-flight documentation requests do not query or render", async () => {
  const overload = memberSurface();
  let queries = 0;
  let renders = 0;
  const state = inspectionState({
    memberDocumentationKey: "documentation",
    memberDocumentationLoading: true,
  });
  const coordinator = createMemberDetailInspectionCoordinator(
    inspectionDependencies(state, {
      queryDocumentation: async () => {
        queries++;
        throw new Error("unexpected query");
      },
      render: () => renders++,
      renderPreservingMemberFocus: () => {
        renders++;
        return focusSnapshot();
      },
    }));

  await coordinator.loadDocumentation(documentationRequest(overload));

  assert.equal(queries, 0);
  assert.equal(renders, 0);
  assert.equal(state.memberDocumentationLoading, true);
});

test("another member starts while documentation is in flight", async () => {
  const overload = memberSurface();
  let queries = 0;
  const state = inspectionState({
    memberDocumentationKey: "previous",
    memberDocumentationLoading: true,
  });
  const coordinator = createMemberDetailInspectionCoordinator(
    inspectionDependencies(state, {
      queryDocumentation: async () => {
        queries++;
        return {
          summary: "Current member documentation.",
          returns: null,
          parameters: {},
          exceptions: [],
        };
      },
    }));

  await coordinator.loadDocumentation(documentationRequest(overload));

  assert.equal(queries, 1);
  assert.equal(state.memberDocumentationKey, "documentation");
  assert.equal(state.memberDocumentationLoading, false);
  assert.equal(overload.summary, "Current member documentation.");
  assert.equal(overload.documentationLoaded, true);
});

test("settled documentation failures can retry for the same member", async () => {
  const overload = memberSurface();
  let queries = 0;
  const state = inspectionState({
    memberDocumentationKey: "documentation",
    memberDocumentationError: "previous failure",
  });
  const coordinator = createMemberDetailInspectionCoordinator(
    inspectionDependencies(state, {
      queryDocumentation: async () => {
        queries++;
        return {
          summary: "Recovered documentation.",
          returns: null,
          parameters: {},
          exceptions: [],
        };
      },
    }));

  await coordinator.loadDocumentation(documentationRequest(overload));

  assert.equal(queries, 1);
  assert.equal(state.memberDocumentationError, "");
  assert.equal(overload.summary, "Recovered documentation.");
  assert.equal(overload.documentationLoaded, true);
});

test("documentation completion updates the current overload and restores focus", async () => {
  const overload = memberSurface();
  const preservedFocus = focusSnapshot();
  const focusCalls: (MemberFocusSnapshot | null | undefined)[] = [];
  const state = inspectionState();
  const coordinator = createMemberDetailInspectionCoordinator(
    inspectionDependencies(state, {
      queryDocumentation: async (request, documentationId) => {
        assert.equal(request.packageId, "Example.Package");
        assert.equal(
          documentationId,
          "M:Example.Widget.Run(System.String)");
        return {
          summary: "Runs the widget.",
          returns: "Nothing.",
          parameters: { value: "The value to run." },
          exceptions: [{
            type: "System.ArgumentException",
            description: "The value is invalid.",
          }],
        };
      },
      renderPreservingMemberFocus: fallback => {
        focusCalls.push(fallback);
        return preservedFocus;
      },
    }));

  await coordinator.loadDocumentation(documentationRequest(overload));

  assert.equal(overload.documentationLoaded, true);
  assert.equal(overload.summary, "Runs the widget.");
  assert.equal(overload.returns, "Nothing.");
  assert.equal(overload.parameters[0]?.description, "The value to run.");
  assert.equal(overload.exceptions[0]?.type, "System.ArgumentException");
  assert.equal(state.memberDocumentationLoading, false);
  assert.deepEqual(focusCalls, [undefined, preservedFocus]);
});

test("documentation hydration mutates only the application projection", async () => {
  const wire = wireMemberSurface();
  const overload = createAppMemberSurface(wire);
  const state = inspectionState();
  const coordinator = createMemberDetailInspectionCoordinator(
    inspectionDependencies(state));

  assert.notEqual(overload.parameters, wire.parameters);
  assert.notEqual(overload.parameters[0], wire.parameters[0]);
  assert.notEqual(overload.exceptions, wire.exceptions);

  await coordinator.loadDocumentation(documentationRequest(overload));

  assert.equal(overload.summary, "Runs the widget.");
  assert.equal(overload.parameters[0]?.description, "The value to run.");
  assert.equal(wire.summary, null);
  assert.equal(wire.parameters[0]?.description, null);
  assert.deepEqual(wire.exceptions, []);
});

test("current documentation failure remains visible", async () => {
  const overload = memberSurface();
  let focusRenders = 0;
  const state = inspectionState();
  const coordinator = createMemberDetailInspectionCoordinator(
    inspectionDependencies(state, {
      queryDocumentation: async () => {
        throw new Error("documentation unavailable");
      },
      renderPreservingMemberFocus: () => {
        focusRenders++;
        return focusSnapshot();
      },
    }));

  await coordinator.loadDocumentation(documentationRequest(overload));

  assert.equal(state.memberDocumentationLoading, false);
  assert.equal(
    state.memberDocumentationError,
    "documentation unavailable");
  assert.equal(focusRenders, 2);
});

test("stale documentation success cannot mutate the selected overload", async () => {
  const overload = memberSurface();
  let focusRenders = 0;
  const state = inspectionState();
  const coordinator = createMemberDetailInspectionCoordinator(
    inspectionDependencies(state, {
      renderPreservingMemberFocus: () => {
        focusRenders++;
        return focusSnapshot();
      },
    }));

  await coordinator.loadDocumentation(documentationRequest(overload, {
    isCurrent: () => false,
  }));

  assert.equal(overload.summary, null);
  assert.equal(overload.returns, null);
  assert.equal(overload.parameters[0]?.description, null);
  assert.equal(overload.documentationLoaded, undefined);
  assert.equal(state.memberDocumentationLoading, false);
  assert.equal(focusRenders, 1);
});

test("stale documentation failure cannot overwrite newer request state", async () => {
  const overload = memberSurface();
  const query = deferred<never>();
  const state = inspectionState();
  const coordinator = createMemberDetailInspectionCoordinator(
    inspectionDependencies(state, {
      queryDocumentation: async () => query.promise,
    }));

  const load = coordinator.loadDocumentation(documentationRequest(overload, {
    isCurrent: () => false,
  }));
  state.memberDocumentationKey = "newer";
  state.memberDocumentationError = "newer failure";
  query.reject(new Error("stale failure"));
  await load;

  assert.equal(overload.documentationLoaded, undefined);
  assert.equal(state.memberDocumentationError, "newer failure");
  assert.equal(state.memberDocumentationLoading, true);
});

test("Finding census publishes exact current results and initializes its reader", async () => {
  const result = findingCensusResult();
  const annotated = result.annotatedSource;
  const prior = selectFindingInstance(
    createMemberFindingInteraction(findingCensusResult()),
    result.factCensusReceipt,
    41,
  ).interaction;
  const preservedFocus = focusSnapshot();
  const focusCalls: (MemberFocusSnapshot | null | undefined)[] = [];
  const state = inspectionState({
    memberAnnotatedEmbedded: {
      ...createEmbeddedSession(createAnnotatedSourceViewerModel(annotated)),
      primary: { kind: "node", id: 1 },
    },
    memberAnnotatedModal: {
      ...createEmbeddedSession(createAnnotatedSourceViewerModel(annotated)),
      surface: "modal",
    },
    memberFindingInteraction: prior,
    memberFindingSelectionError: "old selection failure",
  });
  const coordinator = createMemberDetailInspectionCoordinator(
    inspectionDependencies(state, {
      queryFindingCensus: async request => {
        assert.deepEqual(
          [
            request.typeIdentity,
            request.type,
            request.selectorKey,
            request.metadataToken,
            request.taste,
          ],
          [
            "T:Example.Widget",
            "Example.Widget",
            "Run|System.String",
            0x06000001,
            "[\"prefer-expression-bodied-members\"]",
          ]);
        return result;
      },
      renderPreservingMemberFocus: fallback => {
        focusCalls.push(fallback);
        return preservedFocus;
      },
    }));

  await coordinator.loadFindingCensus(findingCensusRequest());

  assert.equal(state.memberAnnotated, annotated);
  assert.equal(state.memberFindingInteraction?.census, result);
  assert.equal(state.memberFindingInteraction?.selectedInstanceKey, null);
  assert.equal(state.memberFindingSelectionError, "");
  assert.equal(state.memberAnnotatedLoading, false);
  assert.deepEqual(
    state.memberAnnotatedEmbedded,
    createEmbeddedSession(createAnnotatedSourceViewerModel(annotated)),
  );
  assert.equal(state.memberAnnotatedModal, null);
  assert.deepEqual(focusCalls, [undefined, preservedFocus]);
});

test("cached annotated failure renders without querying again", async () => {
  let queries = 0;
  let renders = 0;
  const state = inspectionState({
    memberAnnotatedKey: "annotated",
    memberAnnotatedError: "document rejected",
  });
  const coordinator = createMemberDetailInspectionCoordinator(
    inspectionDependencies(state, {
      queryFindingCensus: async () => {
        queries++;
        return findingCensusResult();
      },
      render: () => renders++,
    }));

  await coordinator.loadFindingCensus(findingCensusRequest());

  assert.equal(queries, 0);
  assert.equal(renders, 1);
  assert.equal(state.memberAnnotatedError, "document rejected");
});

test("cached annotated source renders without querying again", async () => {
  const cachedCensus = findingCensusResult();
  const cached = cachedCensus.annotatedSource;
  const cachedInteraction = createMemberFindingInteraction(cachedCensus);
  let queries = 0;
  let renders = 0;
  const cachedSession =
    createEmbeddedSession(createAnnotatedSourceViewerModel(cached));
  const state = inspectionState({
    memberAnnotated: cached,
    memberAnnotatedKey: "annotated",
    memberAnnotatedEmbedded: cachedSession,
    memberFindingInteraction: cachedInteraction,
  });
  const coordinator = createMemberDetailInspectionCoordinator(
    inspectionDependencies(state, {
      queryFindingCensus: async () => {
        queries++;
        return findingCensusResult();
      },
      render: () => renders++,
    }));

  await coordinator.loadFindingCensus(findingCensusRequest());

  assert.equal(queries, 0);
  assert.equal(renders, 1);
  assert.equal(state.memberAnnotated, cached);
  assert.equal(state.memberFindingInteraction, cachedInteraction);
  assert.equal(state.memberAnnotatedEmbedded, cachedSession);
});

test("cleared annotated source reloads for the same member", async () => {
  const current = findingCensusResult();
  let queries = 0;
  const state = inspectionState({
    memberAnnotatedKey: "annotated",
  });
  const coordinator = createMemberDetailInspectionCoordinator(
    inspectionDependencies(state, {
      queryFindingCensus: async () => {
        queries++;
        return current;
      },
    }));

  await coordinator.loadFindingCensus(findingCensusRequest());

  assert.equal(queries, 1);
  assert.equal(state.memberAnnotated, current.annotatedSource);
  assert.equal(state.memberFindingInteraction?.census, current);
  assert.equal(state.memberAnnotatedLoading, false);
});

test("another member does not reuse a cached annotated source", async () => {
  const cachedCensus = findingCensusResult();
  const cached = cachedCensus.annotatedSource;
  const current = findingCensusResult();
  let queries = 0;
  const state = inspectionState({
    memberAnnotated: cached,
    memberAnnotatedKey: "previous",
    memberFindingInteraction:
      createMemberFindingInteraction(cachedCensus),
  });
  const coordinator = createMemberDetailInspectionCoordinator(
    inspectionDependencies(state, {
      queryFindingCensus: async () => {
        queries++;
        return current;
      },
    }));

  await coordinator.loadFindingCensus(findingCensusRequest());

  assert.equal(queries, 1);
  assert.equal(state.memberAnnotatedKey, "annotated");
  assert.equal(state.memberAnnotated, current.annotatedSource);
  assert.notEqual(state.memberAnnotated, cached);
  assert.equal(state.memberFindingInteraction?.census, current);
  assert.equal(state.memberAnnotatedLoading, false);
});

test("duplicate in-flight annotated requests do not query or mutate state", async () => {
  let queries = 0;
  let renders = 0;
  const state = inspectionState({
    memberAnnotatedKey: "annotated",
    memberAnnotatedLoading: true,
  });
  const coordinator = createMemberDetailInspectionCoordinator(
    inspectionDependencies(state, {
      queryFindingCensus: async () => {
        queries++;
        return findingCensusResult();
      },
      render: () => renders++,
      renderPreservingMemberFocus: () => {
        renders++;
        return focusSnapshot();
      },
    }));

  await coordinator.loadFindingCensus(findingCensusRequest());

  assert.equal(queries, 0);
  assert.equal(renders, 1);
  assert.equal(state.memberAnnotated, null);
  assert.equal(state.memberAnnotatedLoading, true);
});

test("canonical transitions settle Finding census before snapshot", () => {
  const state = inspectionState({
    memberAnnotatedLoading: true,
    memberAnnotatedKey: "annotated",
    memberAnnotatedError: "stale",
  });

  assert.equal(cancelFindingCensusRequest(state), true);
  assert.equal(state.memberAnnotatedLoading, false);
  assert.equal(state.memberAnnotatedKey, "");
  assert.equal(state.memberAnnotatedError, "");
  assert.equal(cancelFindingCensusRequest(state), false);
});

test("another member starts while a Finding census request is in flight", async () => {
  const previous = deferred<MemberFindingCensus>();
  const current = deferred<MemberFindingCensus>();
  let queries = 0;
  const state = inspectionState();
  const coordinator = createMemberDetailInspectionCoordinator(
    inspectionDependencies(state, {
      queryFindingCensus: request => {
        queries++;
        return request.signature === "previous"
          ? previous.promise
          : current.promise;
      },
    }));

  const previousLoad = coordinator.loadFindingCensus(
    findingCensusRequest({ signature: "previous" }));
  const currentLoad =
    coordinator.loadFindingCensus(findingCensusRequest());

  assert.equal(queries, 2);
  assert.equal(state.memberAnnotatedKey, "annotated");
  assert.equal(state.memberAnnotatedLoading, true);

  previous.resolve(findingCensusResult());
  await previousLoad;
  assert.equal(state.memberAnnotated, null);
  assert.equal(state.memberAnnotatedLoading, true);

  const currentResult = findingCensusResult();
  current.resolve(currentResult);
  await currentLoad;
  assert.equal(state.memberAnnotated, currentResult.annotatedSource);
  assert.equal(state.memberFindingInteraction?.census, currentResult);
  assert.equal(state.memberAnnotatedLoading, false);
});

test("an older same-signature Finding census cannot replace the latest request", async () => {
  const first = deferred<MemberFindingCensus>();
  const intervening = deferred<MemberFindingCensus>();
  const latest = deferred<MemberFindingCensus>();
  const queries = [first, intervening, latest];
  let queryIndex = 0;
  const state = inspectionState();
  const coordinator = createMemberDetailInspectionCoordinator(
    inspectionDependencies(state, {
      queryFindingCensus: async () => queries[queryIndex++]!.promise,
    }));

  const firstLoad = coordinator.loadFindingCensus(findingCensusRequest());
  const interveningLoad = coordinator.loadFindingCensus(
    findingCensusRequest({ signature: "intervening" }));
  const latestLoad = coordinator.loadFindingCensus(findingCensusRequest());

  const latestResult = {
    ...findingCensusResult(),
    factCensusReceipt: "33333333-3333-3333-3333-333333333333",
  };
  latest.resolve(latestResult);
  await latestLoad;
  assert.equal(
    state.memberFindingInteraction?.census.factCensusReceipt,
    latestResult.factCensusReceipt,
  );

  first.resolve(findingCensusResult());
  intervening.resolve(findingCensusResult());
  await Promise.all([firstLoad, interveningLoad]);

  assert.equal(state.memberAnnotated, latestResult.annotatedSource);
  assert.equal(
    state.memberFindingInteraction?.census.factCensusReceipt,
    latestResult.factCensusReceipt,
  );
  assert.equal(state.memberAnnotatedKey, "annotated");
  assert.equal(state.memberAnnotatedLoading, false);
});

test("current Finding census rejection remains visible", async () => {
  let focusRenders = 0;
  const state = inspectionState();
  const coordinator = createMemberDetailInspectionCoordinator(
    inspectionDependencies(state, {
      queryFindingCensus: async () => {
        throw new Error("document rejected");
      },
      renderPreservingMemberFocus: () => {
        focusRenders++;
        return focusSnapshot();
      },
    }));

  await coordinator.loadFindingCensus(findingCensusRequest());

  assert.equal(state.memberAnnotatedLoading, false);
  assert.equal(state.memberAnnotatedError, "document rejected");
  assert.equal(focusRenders, 2);
});

test("Finding census success requires the current member even when its key is unchanged", async () => {
  let focusRenders = 0;
  const state = inspectionState({
    memberAnnotatedKey: "previous",
    memberAnnotated: annotatedResult(),
  });
  const coordinator = createMemberDetailInspectionCoordinator(
    inspectionDependencies(state, {
      renderPreservingMemberFocus: () => {
        focusRenders++;
        return focusSnapshot();
      },
    }));

  await coordinator.loadFindingCensus(findingCensusRequest({
    isCurrent: () => false,
  }));

  assert.equal(state.memberAnnotated, null);
  assert.equal(state.memberAnnotatedKey, "annotated");
  assert.equal(state.memberAnnotatedLoading, false);
  assert.equal(focusRenders, 1);
});

test("Finding census success requires its request key even when the member is current", async () => {
  const query = deferred<MemberFindingCensus>();
  const state = inspectionState();
  const coordinator = createMemberDetailInspectionCoordinator(
    inspectionDependencies(state, {
      queryFindingCensus: async () => query.promise,
    }));

  const load = coordinator.loadFindingCensus(findingCensusRequest());
  const currentCensus = findingCensusResult();
  const currentInteraction =
    createMemberFindingInteraction(currentCensus);
  state.memberAnnotatedKey = "newer";
  state.memberAnnotated = currentCensus.annotatedSource;
  state.memberFindingInteraction = currentInteraction;
  query.resolve(findingCensusResult());
  await load;

  assert.equal(state.memberAnnotated, currentCensus.annotatedSource);
  assert.equal(state.memberFindingInteraction, currentInteraction);
  assert.equal(state.memberAnnotatedKey, "newer");
  assert.equal(state.memberAnnotatedLoading, true);
});

test("stale Finding census rejection cannot replace a newer request", async () => {
  const query = deferred<MemberFindingCensus>();
  const state = inspectionState();
  const coordinator = createMemberDetailInspectionCoordinator(
    inspectionDependencies(state, {
      queryFindingCensus: async () => query.promise,
    }));

  const load = coordinator.loadFindingCensus(findingCensusRequest({
    isCurrent: () => false,
  }));
  state.memberAnnotatedKey = "newer";
  state.memberAnnotatedError = "newer failure";
  query.reject(new Error("stale failure"));
  await load;

  assert.equal(state.memberAnnotated, null);
  assert.equal(state.memberAnnotatedError, "newer failure");
  assert.equal(state.memberAnnotatedLoading, true);
});

test("Finding census rejection requires the current member even when its key is unchanged", async () => {
  let focusRenders = 0;
  const state = inspectionState({
    memberAnnotatedKey: "previous",
    memberAnnotatedError: "previous failure",
  });
  const coordinator = createMemberDetailInspectionCoordinator(
    inspectionDependencies(state, {
      queryFindingCensus: async () => {
        throw new Error("stale failure");
      },
      renderPreservingMemberFocus: () => {
        focusRenders++;
        return focusSnapshot();
      },
    }));

  await coordinator.loadFindingCensus(findingCensusRequest({
    isCurrent: () => false,
  }));

  assert.equal(state.memberAnnotatedError, "");
  assert.equal(state.memberAnnotatedKey, "annotated");
  assert.equal(state.memberAnnotatedLoading, false);
  assert.equal(focusRenders, 1);
});

test("Finding census rejection requires its request key even when the member is current", async () => {
  const query = deferred<MemberFindingCensus>();
  const state = inspectionState();
  const coordinator = createMemberDetailInspectionCoordinator(
    inspectionDependencies(state, {
      queryFindingCensus: async () => query.promise,
    }));

  const load = coordinator.loadFindingCensus(findingCensusRequest());
  state.memberAnnotatedKey = "newer";
  state.memberAnnotatedError = "newer failure";
  query.reject(new Error("stale failure"));
  await load;

  assert.equal(state.memberAnnotatedError, "newer failure");
  assert.equal(state.memberAnnotatedKey, "newer");
  assert.equal(state.memberAnnotatedLoading, true);
});

test("member facts publish current results without invalidating the Finding census", async () => {
  const result = factsResult();
  const priorCensus = findingCensusResult();
  const priorAnnotated = priorCensus.annotatedSource;
  const priorInteraction = createMemberFindingInteraction(priorCensus);
  const preservedFocus = focusSnapshot();
  const focusCalls: (MemberFocusSnapshot | null | undefined)[] = [];
  const state = inspectionState({
    memberAnnotated: priorAnnotated,
    memberAnnotatedError: "old annotated failure",
    memberFindingInteraction: priorInteraction,
  });
  const coordinator = createMemberDetailInspectionCoordinator(
    inspectionDependencies(state, {
      queryFacts: async request => {
        assert.deepEqual(
          [
            request.packageId,
            request.framework,
            request.assembly,
            request.type,
            request.member,
            request.memberSignature,
          ],
          [
            "Example.Package",
            "net10.0",
            "Example.Package.dll",
            "Example.Widget",
            "Run",
            "void Run(string value)",
          ]);
        return result;
      },
      renderPreservingMemberFocus: fallback => {
        focusCalls.push(fallback);
        return preservedFocus;
      },
    }));

  await coordinator.loadFacts(factsRequest());

  assert.equal(state.memberFacts, result);
  assert.equal(state.memberFactsLoading, false);
  assert.equal(state.memberAnnotated, priorAnnotated);
  assert.equal(state.memberAnnotatedError, "old annotated failure");
  assert.equal(state.memberFindingInteraction, priorInteraction);
  assert.deepEqual(focusCalls, [undefined, preservedFocus]);
});

test("cached member facts failure renders without querying again", async () => {
  let queries = 0;
  let renders = 0;
  const state = inspectionState({
    memberFactsKey: "facts",
    memberFactsError: "facts unavailable",
  });
  const coordinator = createMemberDetailInspectionCoordinator(
    inspectionDependencies(state, {
      queryFacts: async () => {
        queries++;
        return factsResult();
      },
      render: () => renders++,
    }));

  await coordinator.loadFacts(factsRequest());

  assert.equal(queries, 0);
  assert.equal(renders, 1);
  assert.equal(state.memberFactsError, "facts unavailable");
});

test("cached member facts render without querying or invalidating annotated content", async () => {
  const cached = factsResult();
  const census = findingCensusResult();
  const annotated = census.annotatedSource;
  const interaction = createMemberFindingInteraction(census);
  let queries = 0;
  let renders = 0;
  const state = inspectionState({
    memberFacts: cached,
    memberFactsKey: "facts",
    memberAnnotated: annotated,
    memberFindingInteraction: interaction,
  });
  const coordinator = createMemberDetailInspectionCoordinator(
    inspectionDependencies(state, {
      queryFacts: async () => {
        queries++;
        return factsResult();
      },
      render: () => renders++,
    }));

  await coordinator.loadFacts(factsRequest());

  assert.equal(queries, 0);
  assert.equal(renders, 1);
  assert.equal(state.memberFacts, cached);
  assert.equal(state.memberAnnotated, annotated);
  assert.equal(state.memberFindingInteraction, interaction);
});

test("same member facts request does not duplicate in-flight analysis", async () => {
  const query = deferred<MemberFacts>();
  const result = factsResult();
  let queries = 0;
  const focusCalls: (MemberFocusSnapshot | null | undefined)[] = [];
  const state = inspectionState();
  const coordinator = createMemberDetailInspectionCoordinator(
    inspectionDependencies(state, {
      queryFacts: async () => {
        queries++;
        return query.promise;
      },
      renderPreservingMemberFocus: fallback => {
        focusCalls.push(fallback);
        return focusSnapshot();
      },
    }));

  const firstLoad = coordinator.loadFacts(factsRequest());
  const secondLoad = coordinator.loadFacts(factsRequest());

  assert.equal(queries, 1);
  assert.equal(state.memberFactsLoading, true);
  assert.deepEqual(focusCalls, [undefined, undefined]);
  query.resolve(result);
  await Promise.all([firstLoad, secondLoad]);
  assert.equal(state.memberFacts, result);
  assert.equal(state.memberFactsLoading, false);
  assert.deepEqual(focusCalls, [undefined, undefined, focusSnapshot()]);
});

test("returning to in-flight member facts reuses work and owns publication", async () => {
  for (const firstResolution of ["a", "b"] as const) {
    const aQuery = deferred<MemberFacts>();
    const bQuery = deferred<MemberFacts>();
    const aResult = {
      ...factsResult(),
      metadataToken: 0x06000001,
    };
    const bResult = {
      ...factsResult(),
      metadataToken: 0x06000002,
    };
    const queries = new Map<string, number>();
    let current = "a-first";
    let focusCalls = 0;
    const state = inspectionState();
    const coordinator = createMemberDetailInspectionCoordinator(
      inspectionDependencies(state, {
        queryFacts: async request => {
          queries.set(
            request.signature,
            (queries.get(request.signature) ?? 0) + 1);
          return request.signature === "a"
            ? aQuery.promise
            : bQuery.promise;
        },
        renderPreservingMemberFocus: () => {
          focusCalls++;
          return focusSnapshot();
        },
      }));

    const firstA = coordinator.loadFacts(factsRequest({
      signature: "a",
      isCurrent: () => current === "a-first",
    }));
    current = "b";
    const b = coordinator.loadFacts(factsRequest({
      signature: "b",
      metadataToken: 0x06000002,
      isCurrent: () => current === "b",
    }));
    current = "a-return";
    const returningA = coordinator.loadFacts(factsRequest({
      signature: "a",
      isCurrent: () => current === "a-return",
    }));

    assert.deepEqual([...queries], [["a", 1], ["b", 1]]);
    assert.equal(state.memberFactsKey, "a");
    assert.equal(state.memberFactsLoading, true);
    assert.equal(focusCalls, 3);

    if (firstResolution === "a") {
      aQuery.resolve(aResult);
      await Promise.all([firstA, returningA]);
      assert.equal(state.memberFacts, aResult);
      assert.equal(state.memberFactsLoading, false);
      bQuery.resolve(bResult);
      await b;
    } else {
      bQuery.resolve(bResult);
      await b;
      assert.equal(state.memberFacts, null);
      assert.equal(state.memberFactsLoading, true);
      aQuery.resolve(aResult);
      await Promise.all([firstA, returningA]);
    }

    assert.equal(state.memberFacts, aResult);
    assert.equal(state.memberFactsLoading, false);
    assert.equal(state.memberFactsKey, "a");
    assert.equal(focusCalls, 4);
  }
});

test("cleared member facts reload for the same member", async () => {
  const current = factsResult();
  let queries = 0;
  const state = inspectionState({
    memberFactsKey: "facts",
  });
  const coordinator = createMemberDetailInspectionCoordinator(
    inspectionDependencies(state, {
      queryFacts: async () => {
        queries++;
        return current;
      },
    }));

  await coordinator.loadFacts(factsRequest());

  assert.equal(queries, 1);
  assert.equal(state.memberFacts, current);
  assert.equal(state.memberFactsLoading, false);
});

test("another member does not reuse cached member facts", async () => {
  const cached = factsResult();
  const current = factsResult();
  let queries = 0;
  const state = inspectionState({
    memberFacts: cached,
    memberFactsKey: "previous",
  });
  const coordinator = createMemberDetailInspectionCoordinator(
    inspectionDependencies(state, {
      queryFacts: async () => {
        queries++;
        return current;
      },
    }));

  await coordinator.loadFacts(factsRequest());

  assert.equal(queries, 1);
  assert.equal(state.memberFactsKey, "facts");
  assert.equal(state.memberFacts, current);
  assert.notEqual(state.memberFacts, cached);
  assert.equal(state.memberFactsLoading, false);
});

test("another member starts while a facts request is in flight", async () => {
  const current = factsResult();
  let queries = 0;
  const state = inspectionState({
    memberFactsKey: "previous",
    memberFactsLoading: true,
  });
  const coordinator = createMemberDetailInspectionCoordinator(
    inspectionDependencies(state, {
      queryFacts: async () => {
        queries++;
        return current;
      },
    }));

  await coordinator.loadFacts(factsRequest());

  assert.equal(queries, 1);
  assert.equal(state.memberFactsKey, "facts");
  assert.equal(state.memberFacts, current);
  assert.equal(state.memberFactsLoading, false);
});

test("current member facts failure remains visible", async () => {
  let focusRenders = 0;
  const state = inspectionState();
  const coordinator = createMemberDetailInspectionCoordinator(
    inspectionDependencies(state, {
      queryFacts: async () => {
        throw new Error("facts unavailable");
      },
      renderPreservingMemberFocus: () => {
        focusRenders++;
        return focusSnapshot();
      },
    }));

  await coordinator.loadFacts(factsRequest());

  assert.equal(state.memberFactsLoading, false);
  assert.equal(state.memberFactsError, "facts unavailable");
  assert.equal(focusRenders, 2);
});

test("member facts success requires the current member even when its key is unchanged", async () => {
  let focusRenders = 0;
  const state = inspectionState({
    memberFactsKey: "previous",
    memberFacts: factsResult(),
  });
  const coordinator = createMemberDetailInspectionCoordinator(
    inspectionDependencies(state, {
      renderPreservingMemberFocus: () => {
        focusRenders++;
        return focusSnapshot();
      },
    }));

  await coordinator.loadFacts(factsRequest({
    isCurrent: () => false,
  }));

  assert.equal(state.memberFacts, null);
  assert.equal(state.memberFactsKey, "facts");
  assert.equal(state.memberFactsLoading, false);
  assert.equal(focusRenders, 1);
});

test("member facts success requires its request key even when the member is current", async () => {
  const query = deferred<MemberFacts>();
  const state = inspectionState();
  const coordinator = createMemberDetailInspectionCoordinator(
    inspectionDependencies(state, {
      queryFacts: async () => query.promise,
    }));

  const load = coordinator.loadFacts(factsRequest());
  state.memberFactsKey = "newer";
  query.resolve(factsResult());
  await load;

  assert.equal(state.memberFacts, null);
  assert.equal(state.memberFactsKey, "newer");
  assert.equal(state.memberFactsLoading, true);
});

test("stale member facts completion cannot publish over a newer key", async () => {
  const query = deferred<MemberFacts>();
  const state = inspectionState();
  const coordinator = createMemberDetailInspectionCoordinator(
    inspectionDependencies(state, {
      queryFacts: async () => query.promise,
    }));

  const load = coordinator.loadFacts(factsRequest({
    isCurrent: () => false,
  }));
  state.memberFactsKey = "newer";
  query.resolve(factsResult());
  await load;

  assert.equal(state.memberFacts, null);
  assert.equal(state.memberFactsKey, "newer");
  assert.equal(state.memberFactsLoading, true);
});

test("member facts rejection requires the current member even when its key is unchanged", async () => {
  let focusRenders = 0;
  const state = inspectionState({
    memberFactsKey: "previous",
    memberFactsError: "previous failure",
  });
  const coordinator = createMemberDetailInspectionCoordinator(
    inspectionDependencies(state, {
      queryFacts: async () => {
        throw new Error("stale failure");
      },
      renderPreservingMemberFocus: () => {
        focusRenders++;
        return focusSnapshot();
      },
    }));

  await coordinator.loadFacts(factsRequest({
    isCurrent: () => false,
  }));

  assert.equal(state.memberFactsError, "");
  assert.equal(state.memberFactsKey, "facts");
  assert.equal(state.memberFactsLoading, false);
  assert.equal(focusRenders, 1);
});

test("member facts rejection requires its request key even when the member is current", async () => {
  const query = deferred<MemberFacts>();
  const state = inspectionState();
  const coordinator = createMemberDetailInspectionCoordinator(
    inspectionDependencies(state, {
      queryFacts: async () => query.promise,
    }));

  const load = coordinator.loadFacts(factsRequest());
  state.memberFactsKey = "newer";
  state.memberFactsError = "newer failure";
  query.reject(new Error("stale failure"));
  await load;

  assert.equal(state.memberFactsError, "newer failure");
  assert.equal(state.memberFactsKey, "newer");
  assert.equal(state.memberFactsLoading, true);
});
