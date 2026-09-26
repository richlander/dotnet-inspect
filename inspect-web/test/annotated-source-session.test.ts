import assert from "node:assert/strict";
import test from "node:test";
import {
  annotationState,
  capabilityReason,
  clearAnnotations,
  closeFindingDetail,
  createAnnotatedSourceViewerModel,
  createEmbeddedSession,
  dismissModalSession,
  escapeAnnotatedSource,
  hitTestAnnotatedNode,
  openModalSession,
  renderedFindingTargets,
  renderedStructuralTargets,
  selectAllAnnotations,
  selectDefaultAnnotations,
  selectFinding,
  selectRelationshipPresentation,
  selectNode,
  showRelationshipOccurrences,
  toggleCoordinates,
  toggleFindingAnnotation,
  toggleMedium,
} from "../src/annotated-source-session.ts";
import type {
  AnnotatedSourceResult,
  AnnotatedSourceSession,
} from "../src/annotated-source-session.ts";
import {
  validateDocument,
} from "../src/document-model.ts";
import { inertStringFixture } from "./inert-string-fixture.ts";
import type {
  AnnotatedSourceDocument,
} from "../src/document-model.ts";
import { sampleDocument as sampleDocumentFixture } from "../../prototypes/annotated-source-viewer/src/sample-document.js";
import {
  csharpOnlyEmptyViewerCatalog,
  sampleInvocationTarget,
  sampleViewerCatalog,
} from "./annotated-source-result-fixture.ts";

validateDocument(sampleDocumentFixture);
const sampleDocument: AnnotatedSourceDocument = sampleDocumentFixture;

function sampleResult(
  document: AnnotatedSourceDocument = sampleDocument,
): AnnotatedSourceResult {
  return {
    document,
    signature: inertStringFixture("public void Sample()"),
    viewerCatalog: sampleViewerCatalog,
    findingEvidenceDocuments: [],
    findingEvidence: [],
    callRelationships: [],
    provenance: inertStringFixture("test"),
    contextLimitation: null,
  };
}

test("viewer model derives supported, annotatable, and default sets from product data", () => {
  const result: AnnotatedSourceResult = {
    ...sampleResult(),
    viewerCatalog: {
      ...sampleViewerCatalog,
      defaultFindingIds: [2, 1, 99, 0, 1],
      supportedMedia: ["CSharp"],
    },
  };

  const model = createAnnotatedSourceViewerModel(result);

  assert.deepEqual(model.supportedMedia, ["CSharp"]);
  assert.deepEqual(model.annotatableFindingIds, [0, 1]);
  assert.deepEqual(model.defaultFindingIds, [0, 1]);
  assert.deepEqual([...model.invocationLikeNodeKinds], ["ObjectCreationExpression"]);
});

test("viewer model retains product-issued invocation destinations by node", () => {
  const invocationDocument: AnnotatedSourceDocument = {
    ...sampleDocument,
    nodes: sampleDocument.nodes.map(node =>
      node.id === 1
        ? { ...node, kind: "InvocationExpression" }
        : node),
  };
  const invocationModel = createAnnotatedSourceViewerModel({
    ...sampleResult(invocationDocument),
    viewerCatalog: {
      ...sampleViewerCatalog,
      invocationLikeNodeKinds: ["InvocationExpression"],
      invocationDestinations: [{
        nodeId: 1,
        target: sampleInvocationTarget,
      }],
      destinations: {
        available: true,
        unavailableReason: null,
      },
    },
  });

  assert.equal(invocationModel.invocationDestinations.length, 1);
  assert.equal(invocationModel.invocationDestinations[0]?.nodeId, 1);
  assert.equal(
    invocationModel.invocationDestinations[0]?.target.selectorKey,
    "method:Target",
  );
});

test("viewer model retains exact non-default call relationships", () => {
  const relationshipFact = {
    id: sampleDocument.facts.length,
    descriptor: "call.edge",
    category: "Relationship",
    conditionality: "Always",
    detail: "Example.Targets.Target(System.Int32)",
    origin: "Body",
    source_offset: 0,
  } as const;
  const relationshipDocument: AnnotatedSourceDocument = {
    ...sampleDocument,
    facts: [...sampleDocument.facts, relationshipFact],
    targets: [
      ...sampleDocument.targets,
      { fact_id: relationshipFact.id, node_id: 1 },
    ],
  };
  const relationshipModel = createAnnotatedSourceViewerModel({
    ...sampleResult(relationshipDocument),
    viewerCatalog: {
      ...sampleViewerCatalog,
      callRelationships: {
        available: true,
        unavailableReason: null,
      },
    },
    callRelationships: [{
      edgeRow: 1,
      factId: relationshipFact.id,
      moduleVersionId: "11111111-1111-1111-1111-111111111111",
      callerToken: 0x06000001,
      ilOffset: 0,
      operandToken: 0x0A000001,
      kind: "Call",
      inLoop: false,
      target: sampleInvocationTarget,
    }],
  });

  assert.equal(relationshipModel.callRelationships.length, 1);
  assert.equal(
    relationshipModel.callRelationships[0]?.factId,
    relationshipFact.id);
  assert.ok(
    relationshipModel.annotatableFindingIds.includes(relationshipFact.id));
  assert.ok(
    !relationshipModel.defaultFindingIds.includes(relationshipFact.id));
});

test("viewer model retains exact call cycles by physical relationship fact", () => {
  const relationshipFact = {
    id: sampleDocument.facts.length,
    descriptor: "call.edge",
    category: "Relationship",
    conditionality: "Always",
    detail: "Example.Targets.Target(System.Int32)",
    origin: "Body",
    source_offset: 0,
  } as const;
  const relationshipDocument: AnnotatedSourceDocument = {
    ...sampleDocument,
    facts: [...sampleDocument.facts, relationshipFact],
    targets: [
      ...sampleDocument.targets,
      { fact_id: relationshipFact.id, node_id: 1 },
    ],
  };
  const relationship = {
    edgeRow: 1,
    factId: relationshipFact.id,
    moduleVersionId: "11111111-1111-1111-1111-111111111111",
    callerToken: 0x06000001,
    ilOffset: 0,
    operandToken: 0x0A000001,
    kind: "Call",
    inLoop: false,
    target: sampleInvocationTarget,
  } as const;
  const model = createAnnotatedSourceViewerModel({
    ...sampleResult(relationshipDocument),
    viewerCatalog: {
      ...sampleViewerCatalog,
      callRelationships: {
        available: true,
        unavailableReason: null,
      },
      callCycles: {
        available: true,
        unavailableReason: null,
        isComplete: true,
        limits: [],
        findings: [{
          findingKey: "cycle:key",
          ordinal: 0,
          edgeRows: [1],
          factIds: [relationshipFact.id],
          targets: [{
            ...sampleInvocationTarget,
            memberName: "Caller",
          }],
        }],
      },
    },
    callRelationships: [relationship],
  });

  assert.equal(model.callCycles.findings.length, 1);
  assert.equal(
    model.callCyclesByFactId.get(relationshipFact.id)?.[0]?.findingKey,
    "cycle:key");
});

test("viewer model rejects call cycles without exact first-edge anchors", () => {
  assert.throws(
    () => createAnnotatedSourceViewerModel({
      ...sampleResult(),
      viewerCatalog: {
        ...sampleViewerCatalog,
        callCycles: {
          available: true,
          unavailableReason: null,
          isComplete: true,
          limits: [],
          findings: [{
            findingKey: "cycle:key",
            ordinal: 0,
            edgeRows: [1],
            factIds: [0],
            targets: [sampleInvocationTarget],
          }],
        },
      },
    }),
    /require call relationships/,
  );
});

test("viewer model retains synchronous completion by physical relationship fact", () => {
  const relationshipFact = {
    id: sampleDocument.facts.length,
    descriptor: "call.edge",
    category: "Relationship",
    conditionality: "Always",
    detail: "System.Threading.Tasks.Task<int>.get_Result()",
    origin: "Body",
    source_offset: 0,
  } as const;
  const relationshipDocument: AnnotatedSourceDocument = {
    ...sampleDocument,
    facts: [...sampleDocument.facts, relationshipFact],
    targets: [
      ...sampleDocument.targets,
      { fact_id: relationshipFact.id, node_id: 1 },
    ],
  };
  const model = createAnnotatedSourceViewerModel({
    ...sampleResult(relationshipDocument),
    viewerCatalog: {
      ...sampleViewerCatalog,
      callRelationships: {
        available: true,
        unavailableReason: null,
      },
      synchronousCompletions: {
        available: true,
        unavailableReason: null,
        observations: [{
          factId: relationshipFact.id,
          kind: "TaskResult",
        }],
      },
    },
    callRelationships: [{
      edgeRow: 1,
      factId: relationshipFact.id,
      moduleVersionId: "11111111-1111-1111-1111-111111111111",
      callerToken: 0x06000001,
      ilOffset: 0,
      operandToken: 0x0A000001,
      kind: "CallVirtual",
      inLoop: false,
      target: sampleInvocationTarget,
    }],
  });

  assert.equal(
    model.synchronousCompletionsByFactId
      .get(relationshipFact.id)?.kind,
    "TaskResult");
});

test("viewer model rejects synchronous completion without relationship evidence", () => {
  assert.throws(
    () => createAnnotatedSourceViewerModel({
      ...sampleResult(),
      viewerCatalog: {
        ...sampleViewerCatalog,
        synchronousCompletions: {
          available: true,
          unavailableReason: null,
          observations: [{
            factId: 0,
            kind: "TaskResult",
          }],
        },
      },
    }),
    /require call relationships/,
  );
});

test("viewer model retains classic await completion paths by exact node", () => {
  const awaitDocument: AnnotatedSourceDocument = {
    ...sampleDocument,
    nodes: sampleDocument.nodes.map(node =>
      node.id === 1
        ? { ...node, kind: "AwaitExpression" }
        : node),
  };
  const model = createAnnotatedSourceViewerModel({
    ...sampleResult(awaitDocument),
    viewerCatalog: {
      ...sampleViewerCatalog,
      awaitCompletionPaths: {
        available: true,
        unavailableReason: null,
        observations: [{ nodeId: 1 }],
      },
    },
  });

  assert.equal(
    model.awaitCompletionPathsByNodeId.get(1)?.nodeId,
    1);
});

test("viewer model rejects await completion paths on non-await nodes", () => {
  assert.throws(
    () => createAnnotatedSourceViewerModel({
      ...sampleResult(),
      viewerCatalog: {
        ...sampleViewerCatalog,
        awaitCompletionPaths: {
          available: true,
          unavailableReason: null,
          observations: [{ nodeId: 1 }],
        },
      },
    }),
    /C# AwaitExpression node/,
  );
});

test("viewer model retains allocation exception paths by exact fact", () => {
  const model = createAnnotatedSourceViewerModel({
    ...sampleResult(),
    viewerCatalog: {
      ...sampleViewerCatalog,
      allocationExceptionPaths: {
        available: true,
        unavailableReason: null,
        observations: [{
          factId: 0,
          kind: "ThrownValue",
        }],
      },
    },
  });

  assert.equal(
    model.allocationExceptionPathsByFactId.get(0)?.kind,
    "ThrownValue");
});

test("viewer model rejects allocation exception paths on non-allocation facts", () => {
  assert.throws(
    () => createAnnotatedSourceViewerModel({
      ...sampleResult(),
      viewerCatalog: {
        ...sampleViewerCatalog,
        allocationExceptionPaths: {
          available: true,
          unavailableReason: null,
          observations: [{
            factId: 2,
            kind: "ExceptionHandler",
          }],
        },
      },
    }),
    /typed allocation evidence/,
  );
});

test("viewer model rejects missing or duplicate allocation exception paths", () => {
  const observation = {
    factId: 0,
    kind: "ThrownValue" as const,
  };
  assert.throws(
    () => createAnnotatedSourceViewerModel({
      ...sampleResult(),
      viewerCatalog: {
        ...sampleViewerCatalog,
        allocationExceptionPaths: {
          available: true,
          unavailableReason: null,
          observations: [observation, observation],
        },
      },
    }),
    /unique typed allocation evidence/,
  );
  assert.throws(
    () => createAnnotatedSourceViewerModel({
      ...sampleResult(),
      viewerCatalog: {
        ...sampleViewerCatalog,
        allocationExceptionPaths: {
          available: true,
          unavailableReason: null,
          observations: [{
            factId: 99,
            kind: "ExceptionHandler",
          }],
        },
      },
    }),
    /unique typed allocation evidence/,
  );
});

test("viewer model rejects call relationships without exact occurrence evidence", () => {
  assert.throws(
    () => createAnnotatedSourceViewerModel({
      ...sampleResult(),
      callRelationships: [{
        edgeRow: 1,
        factId: 0,
        moduleVersionId: "not-a-guid",
        callerToken: 0x06000001,
        ilOffset: 0,
        operandToken: 0x0A000001,
        kind: "Call",
        inLoop: false,
        target: sampleInvocationTarget,
      }],
    }),
    /Unavailable Annotated Source call relationships cannot carry rows/,
  );
});

test("viewer model rejects invalid or contradictory invocation destinations", () => {
  const invocationDocument: AnnotatedSourceDocument = {
    ...sampleDocument,
    nodes: sampleDocument.nodes.map(node =>
      node.id === 1
        ? { ...node, kind: "InvocationExpression" }
        : node),
  };
  const destination = {
    nodeId: 1,
    target: sampleInvocationTarget,
  };

  assert.throws(
    () => createAnnotatedSourceViewerModel({
      ...sampleResult(invocationDocument),
      viewerCatalog: {
        ...sampleViewerCatalog,
        invocationDestinations: [destination],
      },
    }),
    /Unavailable Annotated Source destinations cannot carry rows/,
  );
  assert.throws(
    () => createAnnotatedSourceViewerModel({
      ...sampleResult(invocationDocument),
      viewerCatalog: {
        ...sampleViewerCatalog,
        invocationDestinations: [
          destination,
          destination,
        ],
        destinations: {
          available: true,
          unavailableReason: null,
        },
      },
    }),
    /destination node 1 is duplicated/,
  );
  assert.throws(
    () => createAnnotatedSourceViewerModel({
      ...sampleResult(),
      viewerCatalog: {
        ...sampleViewerCatalog,
        invocationDestinations: [destination],
        destinations: {
          available: true,
          unavailableReason: null,
        },
      },
    }),
    /does not name a C# invocation/,
  );
});

test("unsupported-only targets do not enter the annotation universe or default set", () => {
  const document: AnnotatedSourceDocument = {
    ...sampleDocument,
    facts: [
      ...sampleDocument.facts,
      {
        id: 3,
        descriptor: "il.only",
        category: "Semantics",
        conditionality: "Always",
        detail: "IL only",
        origin: "Body",
        source_offset: 1,
      },
    ],
    targets: [
      ...sampleDocument.targets,
      { fact_id: 3, node_id: 3 },
    ],
  };
  const result: AnnotatedSourceResult = {
    ...sampleResult(document),
    viewerCatalog: {
      ...sampleViewerCatalog,
      defaultFindingIds: [0, 3],
      supportedMedia: ["CSharp"],
    },
  };

  const model = createAnnotatedSourceViewerModel(result);

  assert.deepEqual(model.annotatableFindingIds, [0, 1]);
  assert.deepEqual(model.defaultFindingIds, [0]);
});

test("each modal opening is fresh and transfers only an eligible embedded primary", () => {
  const model = createAnnotatedSourceViewerModel(sampleResult());
  let embedded = createEmbeddedSession(model);
  embedded = selectFinding(embedded, {
    kind: "annotation",
    factId: 0,
    nodeId: 1,
    medium: "CSharp",
  });

  const opened = openModalSession(model, {
    ...embedded,
    coordinatesVisible: true,
  });

  assert.equal(opened.embedded.detail, null);
  assert.deepEqual(opened.modal, {
    surface: "modal",
    primary: { kind: "finding", id: 0 },
    activeFindingIds: [0, 1],
    activeRegionIds: [],
    visibleMedia: ["CSharp"],
    coordinatesVisible: false,
    relationshipPresentation: "Table",
    detail: null,
  });
  assert.deepEqual(opened.focus, { kind: "inspector", factId: 0 });

  const ineligible = openModalSession(model, {
    ...createEmbeddedSession(model),
    primary: { kind: "node", id: 1 },
  });
  assert.equal(ineligible.modal.primary, null);
  assert.deepEqual(ineligible.focus, { kind: "heading" });
});

test("dismissal destroys modal-local state and derives the fixed embedded state", () => {
  const model = createAnnotatedSourceViewerModel(sampleResult());
  const modal: AnnotatedSourceSession = {
    surface: "modal",
    primary: { kind: "finding", id: 0 },
    activeFindingIds: [],
    activeRegionIds: [0],
    visibleMedia: ["Il"],
    coordinatesVisible: true,
    relationshipPresentation: "Diagram",
    detail: {
      factId: 0,
      opener: { kind: "inspector", factId: 0 },
    },
  };

  assert.deepEqual(dismissModalSession(model, modal), {
    surface: "embedded",
    primary: { kind: "finding", id: 0 },
    activeFindingIds: [0, 1],
    activeRegionIds: [],
    visibleMedia: ["CSharp"],
    coordinatesVisible: false,
    relationshipPresentation: "Table",
    detail: null,
  });
});

test("relationship presentation is modal-local and preserves viewer state", () => {
  const model = createAnnotatedSourceViewerModel(sampleResult());
  const modal =
    openModalSession(model, createEmbeddedSession(model)).modal;
  const diagram = selectRelationshipPresentation(modal, "Diagram");

  assert.equal(diagram.state.relationshipPresentation, "Diagram");
  assert.deepEqual(diagram.state.primary, modal.primary);
  assert.deepEqual(diagram.state.activeFindingIds, modal.activeFindingIds);
  assert.deepEqual(diagram.state.visibleMedia, modal.visibleMedia);
  assert.equal(diagram.state.coordinatesVisible, modal.coordinatesVisible);
  assert.deepEqual(diagram.focus, {
    kind: "relationship-presentation",
    value: "Diagram",
  });
  assert.equal(
    openModalSession(
      model,
      dismissModalSession(model, diagram.state),
    ).modal.relationshipPresentation,
    "Table",
  );

  const occurrences = showRelationshipOccurrences(diagram.state, 17);
  assert.equal(occurrences.state.relationshipPresentation, "Table");
  assert.deepEqual(occurrences.focus, {
    kind: "relationship",
    factId: 17,
  });
});

test("reported annotation state uses Default, All, Clear, Custom precedence", () => {
  const model = createAnnotatedSourceViewerModel(sampleResult());
  const embedded = createEmbeddedSession(model);

  assert.equal(annotationState(model, embedded), "Default");
  assert.equal(annotationState(model, { ...embedded, activeFindingIds: [0, 1] }), "Default");
  assert.equal(annotationState(model, { ...embedded, activeFindingIds: [] }), "Clear");
  assert.equal(annotationState(model, { ...embedded, activeFindingIds: [0] }), "Custom");

  const emptyDefault = createAnnotatedSourceViewerModel({
    ...sampleResult(),
    viewerCatalog: {
      ...sampleViewerCatalog,
      defaultFindingIds: [],
    },
  });
  assert.equal(
    annotationState(emptyDefault, createEmbeddedSession(emptyDefault)),
    "Default",
  );

  const universeDefault = createAnnotatedSourceViewerModel(sampleResult({
    ...sampleDocument,
    regions: [],
  }));
  assert.deepEqual(
    universeDefault.defaultFindingIds,
    universeDefault.annotatableFindingIds,
  );
  assert.equal(
    annotationState(universeDefault, createEmbeddedSession(universeDefault)),
    "Default",
  );
});

test("rendered annotations derive from active membership and visible media", () => {
  const model = createAnnotatedSourceViewerModel(sampleResult());
  const embedded = createEmbeddedSession(model);

  assert.deepEqual(
    renderedFindingTargets(model, embedded).map(target => [
      target.factId,
      target.nodeId,
      target.medium,
    ]),
    [
      [0, 1, "CSharp"],
      [1, 0, "CSharp"],
    ],
  );
  assert.deepEqual(
    renderedFindingTargets(model, {
      ...embedded,
      visibleMedia: ["Il"],
    }).map(target => [target.factId, target.nodeId, target.medium]),
    [
      [0, 3, "Il"],
      [1, 2, "Il"],
    ],
  );
  assert.deepEqual(
    renderedFindingTargets(model, {
      ...embedded,
      activeFindingIds: [],
      visibleMedia: ["CSharp", "Il"],
    }),
    [],
  );
});

test("All includes product-issued structural regions while Default excludes them", () => {
  const model = createAnnotatedSourceViewerModel(sampleResult());
  const embedded = createEmbeddedSession(model);

  assert.deepEqual(model.structuralRegionIds, [0]);
  assert.deepEqual(renderedStructuralTargets(model, embedded), []);

  const all = selectAllAnnotations(model, embedded).state;
  assert.deepEqual(all.activeRegionIds, [0]);
  assert.equal(annotationState(model, all), "All");
  assert.deepEqual(
    renderedStructuralTargets(model, all).map(target => [
      target.regionId,
      target.region.role,
      target.medium,
    ]),
    [
      [0, "Body", "CSharp"],
      [0, "Body", "CSharp"],
    ],
  );
});

test("annotation controls preserve orthogonal presentation state", () => {
  const model = createAnnotatedSourceViewerModel(sampleResult());
  const state: AnnotatedSourceSession = {
    surface: "modal",
    primary: { kind: "finding", id: 0 },
    activeFindingIds: [0],
    activeRegionIds: [],
    visibleMedia: ["CSharp", "Il"],
    coordinatesVisible: true,
    relationshipPresentation: "Diagram",
    detail: {
      factId: 0,
      opener: { kind: "inspector", factId: 0 },
    },
  };

  const all = selectAllAnnotations(model, state);
  assert.deepEqual(all.state.activeFindingIds, [0, 1]);
  assert.deepEqual(all.state.activeRegionIds, [0]);
  assert.deepEqual(all.state.primary, state.primary);
  assert.deepEqual(all.state.detail, state.detail);
  assert.deepEqual(all.state.visibleMedia, state.visibleMedia);
  assert.equal(all.state.coordinatesVisible, true);
  assert.equal(all.state.relationshipPresentation, "Diagram");

  const defaults = selectDefaultAnnotations(model, state);
  assert.equal(defaults.state.primary, null);
  assert.equal(defaults.state.detail, null);
  assert.deepEqual(defaults.state.visibleMedia, state.visibleMedia);
  assert.equal(defaults.state.coordinatesVisible, true);

  const cleared = clearAnnotations(state);
  assert.deepEqual(cleared.state.activeFindingIds, []);
  assert.deepEqual(cleared.state.activeRegionIds, []);
  assert.equal(cleared.state.primary, null);
  assert.equal(cleared.state.detail, null);
  assert.deepEqual(cleared.state.visibleMedia, state.visibleMedia);
  assert.equal(cleared.state.coordinatesVisible, true);
});

test("same-medium sibling targets do not replace the exact detail opener", () => {
  const document: AnnotatedSourceDocument = {
    ...sampleDocument,
    nodes: [
      ...sampleDocument.nodes,
      {
        id: 4,
        kind: "IdentifierName",
        medium: "CSharp",
        spans: [{
          start: sampleDocument.text.indexOf("object"),
          length: "object".length,
        }],
      },
    ],
    targets: [
      ...sampleDocument.targets,
      { fact_id: 0, node_id: 4 },
    ],
  };
  const model = createAnnotatedSourceViewerModel(sampleResult(document));
  const modal = openModalSession(model, createEmbeddedSession(model)).modal;
  const detail = selectFinding(modal, {
    kind: "annotation",
    factId: 0,
    nodeId: 1,
    medium: "CSharp",
  });

  assert.deepEqual(closeFindingDetail(model, detail).focus, {
    kind: "annotation",
    factId: 0,
    nodeId: 1,
    medium: "CSharp",
  });
});

test("removing the primary Finding clears primary and detail but adding does not select", () => {
  const model = createAnnotatedSourceViewerModel(sampleResult());
  const state = selectFinding(createEmbeddedSession(model), {
    kind: "annotation",
    factId: 0,
    nodeId: 1,
    medium: "CSharp",
  });

  const removed = toggleFindingAnnotation(model, state, 0);
  assert.equal(removed.state.primary, null);
  assert.equal(removed.state.detail, null);
  assert.deepEqual(removed.state.activeFindingIds, [1]);
  assert.deepEqual(removed.focus, { kind: "finding-toggle", factId: 0 });

  const added = toggleFindingAnnotation(model, removed.state, 0);
  assert.equal(added.state.primary, null);
  assert.equal(added.state.detail, null);
  assert.deepEqual(added.state.activeFindingIds, [0, 1]);
});

test("media controls come from the catalog and cannot hide the last visible medium", () => {
  const model = createAnnotatedSourceViewerModel(sampleResult());
  const modal = openModalSession(model, createEmbeddedSession(model)).modal;

  const rejected = toggleMedium(model, modal, "CSharp");
  assert.deepEqual(rejected.state.visibleMedia, ["CSharp"]);
  assert.deepEqual(rejected.focus, { kind: "medium-toggle", medium: "CSharp" });

  const withIl = toggleMedium(model, modal, "Il");
  assert.deepEqual(withIl.state.visibleMedia, ["CSharp", "Il"]);
  const ilOnly = toggleMedium(model, withIl.state, "CSharp");
  assert.deepEqual(ilOnly.state.visibleMedia, ["Il"]);

  const csharpOnlyModel = createAnnotatedSourceViewerModel({
    ...sampleResult(),
    viewerCatalog: csharpOnlyEmptyViewerCatalog,
  });
  const unsupported = toggleMedium(
    csharpOnlyModel,
    createEmbeddedSession(csharpOnlyModel),
    "Il",
  );
  assert.deepEqual(unsupported.state.visibleMedia, ["CSharp"]);
});

test("coordinates toggle independently and selection preserves presentation", () => {
  const model = createAnnotatedSourceViewerModel(sampleResult());
  const modal = toggleMedium(
    model,
    openModalSession(model, createEmbeddedSession(model)).modal,
    "Il",
  ).state;
  const coordinates = toggleCoordinates(modal);
  const finding = selectFinding(coordinates.state, {
    kind: "inspector",
    factId: 0,
  });
  const node = selectNode(finding, 1);

  assert.equal(coordinates.state.coordinatesVisible, true);
  assert.deepEqual(finding.activeFindingIds, modal.activeFindingIds);
  assert.deepEqual(finding.visibleMedia, ["CSharp", "Il"]);
  assert.equal(finding.coordinatesVisible, true);
  assert.deepEqual(node.primary, { kind: "node", id: 1 });
  assert.equal(node.detail, null);
  assert.deepEqual(node.activeFindingIds, modal.activeFindingIds);
  assert.deepEqual(node.visibleMedia, ["CSharp", "Il"]);
  assert.equal(node.coordinatesVisible, true);
});

test("detail closes to the exact rendered opener or its Finding inspector fallback", () => {
  const model = createAnnotatedSourceViewerModel(sampleResult());
  const modal = {
    ...openModalSession(model, createEmbeddedSession(model)).modal,
    visibleMedia: ["CSharp", "Il"] as const,
  };
  const csharpDetail = selectFinding(modal, {
    kind: "annotation",
    factId: 0,
    nodeId: 1,
    medium: "CSharp",
  });
  assert.deepEqual(closeFindingDetail(model, csharpDetail).focus, {
    kind: "annotation",
    factId: 0,
    nodeId: 1,
    medium: "CSharp",
  });

  const hiddenOpener = toggleMedium(model, csharpDetail, "CSharp").state;
  assert.deepEqual(closeFindingDetail(model, hiddenOpener).focus, {
    kind: "inspector",
    factId: 0,
  });

  const inspectorDetail = selectFinding(modal, {
    kind: "inspector",
    factId: 0,
  });
  assert.deepEqual(closeFindingDetail(model, inspectorDetail).focus, {
    kind: "inspector",
    factId: 0,
  });

  const relationshipFact = {
    id: sampleDocument.facts.length,
    descriptor: "call.edge",
    category: "Relationship",
    conditionality: "Always",
    detail: "Example.Targets.Target(System.Int32)",
    origin: "Body",
    source_offset: 0,
  } as const;
  const relationshipDocument: AnnotatedSourceDocument = {
    ...sampleDocument,
    facts: [...sampleDocument.facts, relationshipFact],
    targets: [
      ...sampleDocument.targets,
      { fact_id: relationshipFact.id, node_id: 1 },
    ],
  };
  const relationshipModel = createAnnotatedSourceViewerModel({
    ...sampleResult(relationshipDocument),
    viewerCatalog: {
      ...sampleViewerCatalog,
      callRelationships: {
        available: true,
        unavailableReason: null,
      },
    },
    callRelationships: [{
      edgeRow: 1,
      factId: relationshipFact.id,
      moduleVersionId: "11111111-1111-1111-1111-111111111111",
      callerToken: 0x06000001,
      ilOffset: 0,
      operandToken: 0x0A000001,
      kind: "Call",
      inLoop: false,
      target: sampleInvocationTarget,
    }],
  });
  const relationshipDetail = selectFinding(
    openModalSession(
      relationshipModel,
      createEmbeddedSession(relationshipModel),
    ).modal,
    {
      kind: "relationship",
      factId: relationshipFact.id,
    },
  );
  assert.deepEqual(
    closeFindingDetail(relationshipModel, relationshipDetail).focus,
    {
      kind: "relationship",
      factId: relationshipFact.id,
    },
  );
  assert.deepEqual(
    escapeAnnotatedSource(relationshipModel, relationshipDetail).focus,
    {
      kind: "relationship",
      factId: relationshipFact.id,
    },
  );
});

test("Escape closes detail, then dismisses modal, and falls through embedded", () => {
  const model = createAnnotatedSourceViewerModel(sampleResult());
  const modal = selectFinding(
    openModalSession(model, createEmbeddedSession(model)).modal,
    { kind: "inspector", factId: 0 },
  );

  const detailEscape = escapeAnnotatedSource(model, modal);
  assert.equal(detailEscape.handled, true);
  assert.equal(detailEscape.dismissModal, false);
  assert.equal(detailEscape.state.detail, null);
  assert.deepEqual(detailEscape.focus, { kind: "inspector", factId: 0 });

  const modalEscape = escapeAnnotatedSource(model, detailEscape.state);
  assert.equal(modalEscape.handled, true);
  assert.equal(modalEscape.dismissModal, true);
  assert.deepEqual(modalEscape.state, detailEscape.state);

  const embedded = createEmbeddedSession(model);
  const embeddedEscape = escapeAnnotatedSource(model, embedded);
  assert.equal(embeddedEscape.handled, false);
  assert.equal(embeddedEscape.dismissModal, false);
  assert.equal(embeddedEscape.focus, null);
  assert.deepEqual(embeddedEscape.state, embedded);
});

test("hit testing gives invocation-like nodes precedence before tightest generic nodes", () => {
  const objectStart = sampleDocument.text.indexOf("new object()");
  const document: AnnotatedSourceDocument = {
    ...sampleDocument,
    nodes: [
      ...sampleDocument.nodes,
      {
        id: 4,
        kind: "IdentifierName",
        medium: "CSharp",
        spans: [{ start: objectStart + 4, length: 6 }],
      },
    ],
  };
  const result: AnnotatedSourceResult = {
    ...sampleResult(document),
    viewerCatalog: {
      ...sampleViewerCatalog,
      invocationLikeNodeKinds: ["ObjectCreationExpression"],
    },
  };
  const model = createAnnotatedSourceViewerModel(result);

  assert.equal(
    hitTestAnnotatedNode(model, objectStart + 5, "CSharp")?.id,
    1,
  );

  const genericModel = createAnnotatedSourceViewerModel({
    ...result,
    viewerCatalog: {
      ...result.viewerCatalog,
      invocationLikeNodeKinds: [],
    },
  });
  assert.equal(
    hitTestAnnotatedNode(genericModel, objectStart + 5, "CSharp")?.id,
    4,
  );
});

test("typed capability absence remains visible", () => {
  assert.equal(
    capabilityReason({
      available: false,
      unavailableReason: "NotProjected",
    }),
    "Not projected by the current product query",
  );
  assert.equal(
    capabilityReason({
      available: false,
      unavailableReason: 7,
    }),
    "Unavailable (7)",
  );
  assert.equal(
    capabilityReason({
      available: true,
      unavailableReason: null,
    }),
    "Available",
  );
});
