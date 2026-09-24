import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";
import {
  annotatedFocusSelector,
  bindAnnotatedSource,
  renderAnnotatedSource,
  renderAnnotatedSourceModal,
  renderAnnotatedSourcePageActions,
  type AnnotatedSourceAction,
  type AnnotatedSourceBindingActions,
} from "../src/annotated-source.ts";
import {
  createCSharpRangeHighlighter,
} from "../src/csharp-highlighting.ts";
import {
  createAnnotatedSourceViewerModel,
  createEmbeddedSession,
  openModalSession,
  selectAllAnnotations,
  selectFinding,
  selectNode,
  toggleCoordinates,
} from "../src/annotated-source-session.ts";
import type {
  AnnotatedSourceResult,
} from "../src/annotated-source-session.ts";
import {
  csharpHighlightingInput,
  csharpHighlightingText,
  validateAnnotatedSourceDocument,
} from "../src/annotated-source-view.ts";
import type { AnnotatedSourceDocument } from "../src/annotated-source-view.ts";
import { sampleDocument as sampleDocumentFixture } from "../../prototypes/annotated-source-viewer/src/sample-document.js";
import {
  csharpOnlyEmptyViewerCatalog,
  sampleCalleeDocument,
  sampleCalleeEvidence,
  sampleCalleeEvidenceDocuments,
  sampleInvocationTarget,
  sampleViewerCatalog,
} from "./annotated-source-result-fixture.ts";
import { inertStringFixture } from "./inert-string-fixture.ts";
import { fakeDom } from "./fake-dom.ts";

validateAnnotatedSourceDocument(sampleDocumentFixture);
const sampleDocument: AnnotatedSourceDocument = sampleDocumentFixture;

class FakeElement {
  readonly dataset: Record<string, string | undefined>;
  hidden = false;
  private readonly listeners = new Map<string, EventListener[]>();

  constructor(dataset: Record<string, string | undefined> = {}) {
    this.dataset = dataset;
  }

  addEventListener(type: string, listener: EventListener) {
    const listeners = this.listeners.get(type) ?? [];
    listeners.push(listener);
    this.listeners.set(type, listeners);
  }

  dispatch(type: string, values: object = {}) {
    for (const listener of this.listeners.get(type) ?? []) {
      listener(fakeDom.event({
        currentTarget: this,
        target: this,
        ...values,
      }));
    }
  }
}

class FakeRoot {
  private readonly actions: FakeElement[];

  constructor(actions: FakeElement[] = []) {
    this.actions = actions;
  }

  querySelector() {
    return null;
  }

  querySelectorAll(selector: string) {
    if (selector === "[data-annotated-action]") return this.actions;
    return [];
  }
}

function recordingActions(calls: AnnotatedSourceAction[]): AnnotatedSourceBindingActions {
  return {
    onAction: action => calls.push(action),
  };
}

test("annotated source bindings dispatch the documented fixed and chip actions", () => {
  const elements = [
    new FakeElement({ annotatedAction: "copy" }),
    new FakeElement({ annotatedAction: "explore" }),
    new FakeElement({ annotatedAction: "close-modal" }),
    new FakeElement({ annotatedAction: "close-detail" }),
    new FakeElement({
      annotatedAction: "annotation-open",
      factId: "4",
      nodeId: "7",
      medium: "Il",
    }),
    new FakeElement({ annotatedAction: "inspector-open", factId: "4" }),
    new FakeElement({ annotatedAction: "relationship-open", factId: "5" }),
    new FakeElement({
      annotatedAction: "relationship-occurrences-open",
      factId: "5",
    }),
    new FakeElement({
      annotatedAction: "relationship-presentation",
      relationshipPresentation: "Diagram",
    }),
    new FakeElement({ annotatedAction: "annotation-set", annotatedSet: "All" }),
    new FakeElement({ annotatedAction: "finding-toggle", factId: "4" }),
    new FakeElement({ annotatedAction: "medium-toggle", medium: "CSharp" }),
    new FakeElement({ annotatedAction: "coordinate-toggle" }),
    new FakeElement({ annotatedAction: "node-select", nodeId: "7" }),
    new FakeElement({
      annotatedAction: "destination-open",
      destinationIndex: "2",
      destination: "source",
    }),
    new FakeElement({
      annotatedAction: "relationship-destination-open",
      relationshipIndex: "3",
      destination: "member",
    }),
    new FakeElement({
      annotatedAction: "finding-evidence-open",
      factId: "4",
      destination: "member",
    }),
  ];
  const calls: AnnotatedSourceAction[] = [];
  bindAnnotatedSource(
    fakeDom.parentNode(new FakeRoot(elements)),
    recordingActions(calls),
  );

  for (const element of elements) element.dispatch("click");

  assert.deepEqual(calls, [
    { kind: "copy" },
    { kind: "explore" },
    { kind: "close-modal" },
    { kind: "close-detail" },
    {
      kind: "annotation-open",
      opener: {
        kind: "annotation",
        factId: 4,
        nodeId: 7,
        medium: "Il",
      },
    },
    { kind: "inspector-open", factId: 4 },
    { kind: "relationship-open", factId: 5 },
    { kind: "relationship-occurrences-open", factId: 5 },
    { kind: "relationship-presentation", value: "Diagram" },
    { kind: "annotation-set", value: "All" },
    { kind: "finding-toggle", factId: 4 },
    { kind: "medium-toggle", medium: "CSharp" },
    { kind: "coordinate-toggle" },
    { kind: "node-select", nodeId: 7 },
    {
      kind: "destination-open",
      destinationIndex: 2,
      destination: "source",
    },
    {
      kind: "relationship-destination-open",
      relationshipIndex: 3,
      destination: "member",
    },
    {
      kind: "finding-evidence-open",
      factId: 4,
      destination: "member",
    },
  ]);
});

test("malformed action identities are inert rather than dispatched as NaN", () => {
  const elements = [
    new FakeElement({ annotatedAction: "annotation-open", factId: "x" }),
    new FakeElement({ annotatedAction: "inspector-open" }),
    new FakeElement({ annotatedAction: "relationship-open" }),
    new FakeElement({ annotatedAction: "relationship-occurrences-open" }),
    new FakeElement({
      annotatedAction: "relationship-presentation",
      relationshipPresentation: "Graph",
    }),
    new FakeElement({ annotatedAction: "annotation-set", annotatedSet: "Maybe" }),
    new FakeElement({ annotatedAction: "finding-toggle", factId: "-1" }),
    new FakeElement({ annotatedAction: "medium-toggle", medium: "Other" }),
    new FakeElement({ annotatedAction: "node-select", nodeId: "" }),
    new FakeElement({
      annotatedAction: "destination-open",
      destinationIndex: "x",
      destination: "other",
    }),
    new FakeElement({
      annotatedAction: "relationship-destination-open",
      relationshipIndex: "x",
      destination: "other",
    }),
    new FakeElement({
      annotatedAction: "finding-evidence-open",
      factId: "x",
      destination: "other",
    }),
  ];
  const calls: AnnotatedSourceAction[] = [];
  bindAnnotatedSource(
    fakeDom.parentNode(new FakeRoot(elements)),
    recordingActions(calls),
  );

  for (const element of elements) element.dispatch("click");

  assert.deepEqual(calls, []);
});

function escapeHtml(value: unknown) {
  return String(value)
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;");
}

const result: AnnotatedSourceResult = {
  document: sampleDocument,
  viewerCatalog: sampleViewerCatalog,
  findingEvidenceDocuments: [],
  findingEvidence: [],
  callRelationships: [],
  provenance: inertStringFixture("decompiled from IL"),
  contextLimitation: null,
};

function embeddedHtml(source: AnnotatedSourceResult = result): string {
  const model = createAnnotatedSourceViewerModel(source);
  return renderAnnotatedSource({
    result: source,
    session: createEmbeddedSession(model),
    escapeHtml,
  });
}

function modalHtml(source: AnnotatedSourceResult = result): string {
  const model = createAnnotatedSourceViewerModel(source);
  return renderAnnotatedSourceModal({
    result: source,
    session: openModalSession(model, createEmbeddedSession(model)).modal,
    escapeHtml,
  });
}

function invocationResult(): AnnotatedSourceResult {
  return {
    ...result,
    document: {
      ...sampleDocument,
      nodes: sampleDocument.nodes.map(node =>
        node.id === 1
          ? { ...node, kind: "InvocationExpression" }
          : node),
    },
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
  };
}

function calleeEvidenceResult(
  evidence: AnnotatedSourceResult["findingEvidence"][number] =
    sampleCalleeEvidence,
  evidenceDocument: AnnotatedSourceDocument = sampleCalleeDocument,
): AnnotatedSourceResult {
  return {
    ...result,
    document: {
      ...sampleDocument,
      facts: sampleDocument.facts.map(fact =>
        fact.id === 0
          ? { ...fact, descriptor: "safety.callee" }
          : fact),
    },
    viewerCatalog: {
      ...sampleViewerCatalog,
      findingEvidence: {
        available: true,
        unavailableReason: null,
      },
    },
    findingEvidenceDocuments: evidence.documentId === null
      ? []
      : [{
          ...sampleCalleeEvidenceDocuments[0],
          document: evidenceDocument,
        }],
    findingEvidence: [evidence],
  };
}

function methodCostEvidenceResult(
  evidence: AnnotatedSourceResult["findingEvidence"][number] = {
    ...sampleCalleeEvidence,
    state: "Method",
    aggregateInputs: [
      {
        kind: "AllocationInLoop",
        value: null,
      },
      {
        kind: "Reflection",
        value: 3,
      },
    ],
    coordinates: [],
    documentId: null,
    nodeIds: [],
  },
): AnnotatedSourceResult {
  return {
    ...result,
    document: {
      ...sampleDocument,
      facts: sampleDocument.facts.map(fact =>
        fact.id === 0
          ? { ...fact, descriptor: "cost.callee" }
          : fact),
    },
    viewerCatalog: {
      ...sampleViewerCatalog,
      findingEvidence: {
        available: true,
        unavailableReason: null,
      },
    },
    findingEvidenceDocuments: evidence.documentId === null
      ? []
      : sampleCalleeEvidenceDocuments,
    findingEvidence: [evidence],
  };
}

function callCycleRelationshipResult(
  callCycles: AnnotatedSourceResult["viewerCatalog"]["callCycles"],
): { readonly source: AnnotatedSourceResult; readonly factId: number } {
  const factId = sampleDocument.facts.length;
  const document: AnnotatedSourceDocument = {
    ...sampleDocument,
    facts: [
      ...sampleDocument.facts,
      {
        id: factId,
        descriptor: "call.edge",
        category: "Relationship",
        conditionality: "Always",
        detail: "Example.Targets.Caller(System.Int32)",
        origin: "Body",
        source_offset: 0,
      },
    ],
    targets: [
      ...sampleDocument.targets,
      { fact_id: factId, node_id: 1 },
    ],
  };

  return {
    factId,
    source: {
      document,
      viewerCatalog: {
        ...sampleViewerCatalog,
        callRelationships: {
          available: true,
          unavailableReason: null,
        },
        callCycles,
      },
      findingEvidenceDocuments: [],
      findingEvidence: [],
      callRelationships: [{
        edgeRow: 1,
        factId,
        moduleVersionId: "11111111-1111-1111-1111-111111111111",
        callerToken: 0x06000001,
        ilOffset: 0,
        operandToken: 0x0A000001,
        kind: "Call",
        inLoop: false,
        target: sampleInvocationTarget,
      }],
      provenance: inertStringFixture("test"),
      contextLimitation: null,
    },
  };
}

function repeatedRelationshipResult(): AnnotatedSourceResult {
  const firstFactId = sampleDocument.facts.length;
  const secondFactId = firstFactId + 1;
  return {
    ...result,
    document: {
      ...sampleDocument,
      facts: [
        ...sampleDocument.facts,
        {
          id: firstFactId,
          descriptor: "call.edge",
          category: "Relationship",
          conditionality: "Always",
          detail: "Example.Targets.Target(System.Int32)",
          origin: "Body",
          source_offset: 0,
        },
        {
          id: secondFactId,
          descriptor: "call.edge",
          category: "Relationship",
          conditionality: "Always",
          detail: "Example.Targets.Target(System.Int32)",
          origin: "Body",
          source_offset: 1,
        },
      ],
      targets: [
        ...sampleDocument.targets,
        { fact_id: firstFactId, node_id: 2 },
        { fact_id: secondFactId, node_id: 3 },
      ],
    },
    viewerCatalog: {
      ...sampleViewerCatalog,
      callRelationships: {
        available: true,
        unavailableReason: null,
      },
    },
    callRelationships: [
      {
        edgeRow: 7,
        factId: firstFactId,
        moduleVersionId: "11111111-1111-1111-1111-111111111111",
        callerToken: 0x06000001,
        ilOffset: 0,
        operandToken: 0x0A000001,
        kind: "Call",
        inLoop: false,
        target: sampleInvocationTarget,
      },
      {
        edgeRow: 7,
        factId: secondFactId,
        moduleVersionId: "11111111-1111-1111-1111-111111111111",
        callerToken: 0x06000001,
        ilOffset: 1,
        operandToken: 0x0A000001,
        kind: "CallVirtual",
        inLoop: true,
        target: sampleInvocationTarget,
      },
    ],
  };
}

function versionDistinctRelationshipResult(): AnnotatedSourceResult {
  const source = repeatedRelationshipResult();
  return {
    ...source,
    callRelationships: source.callRelationships.map((relationship, index) => ({
      ...relationship,
      target: {
        ...relationship.target,
        assemblyVersion: index === 0 ? "1.0.0.0" : "2.0.0.0",
        surfaceAssemblyId: index === 0 ? "surface-v1" : "surface-v2",
      },
    })),
  };
}

test("the result preserves the validated portable document contract", () => {
  const document: AnnotatedSourceDocument = result.document;
  assert.equal(document, sampleDocument);
});

test("viewer model validates exact callee evidence documents and node kinds", () => {
  const model = createAnnotatedSourceViewerModel(calleeEvidenceResult());

  assert.equal(model.findingEvidence.length, 1);
  assert.equal(model.findingEvidenceByFactId.get(0)?.instanceKey, 41);
  assert.equal(
    model.findingEvidenceByFactId.get(0)?.document,
    sampleCalleeDocument,
  );
  assert.throws(
    () => createAnnotatedSourceViewerModel(calleeEvidenceResult({
      ...sampleCalleeEvidence,
      documentId: 99,
    })),
    /names no callee document/,
  );
  assert.throws(
    () => createAnnotatedSourceViewerModel({
      ...calleeEvidenceResult(),
      findingEvidenceDocuments: [
        ...sampleCalleeEvidenceDocuments,
        ...sampleCalleeEvidenceDocuments,
      ],
    }),
    /invalid or duplicate id/,
  );
  assert.throws(
    () => createAnnotatedSourceViewerModel({
      ...calleeEvidenceResult(),
      findingEvidenceDocuments: [
        ...sampleCalleeEvidenceDocuments,
        {
          id: 1,
          document: sampleCalleeDocument,
        },
      ],
    }),
    /unreferenced callee evidence document/,
  );
  assert.throws(
    () => createAnnotatedSourceViewerModel(calleeEvidenceResult({
      ...sampleCalleeEvidence,
      documentId: null,
    })),
    /requires a document, coordinates, and node ids/,
  );
  assert.throws(
    () => createAnnotatedSourceViewerModel(calleeEvidenceResult({
      ...sampleCalleeEvidence,
      nodeIds: [99],
    })),
    /node ids do not equal its exact coordinate matches/,
  );
  assert.throws(
    () => createAnnotatedSourceViewerModel(calleeEvidenceResult(
      sampleCalleeEvidence,
      {
        ...sampleCalleeDocument,
        nodes: [{
          ...sampleCalleeDocument.nodes[0],
          spans: [{ start: 99, length: 1 }],
        }],
      },
    )),
    /outside the document text/,
  );
  assert.throws(
    () => createAnnotatedSourceViewerModel(calleeEvidenceResult(
      sampleCalleeEvidence,
      {
        ...sampleCalleeDocument,
        nodes: [{
          ...sampleCalleeDocument.nodes[0],
          kind: "InvocationExpression",
        }],
      },
    )),
    /matches 0 StackAllocationExpression nodes/,
  );
  assert.throws(
    () => createAnnotatedSourceViewerModel(calleeEvidenceResult({
      ...sampleCalleeEvidence,
      coordinates: [{
        ilOffset: 3,
        kind: "Localloc",
      }],
    })),
    /matches 0 StackAllocationExpression nodes/,
  );
  assert.throws(
    () => createAnnotatedSourceViewerModel(calleeEvidenceResult(
      {
        ...sampleCalleeEvidence,
        nodeIds: [1],
      },
      {
        ...sampleCalleeDocument,
        text: `${sampleCalleeDocument.text}; stackalloc byte[2]`,
        nodes: [
          sampleCalleeDocument.nodes[0],
          {
            id: 1,
            kind: "StackAllocationExpression",
            medium: "CSharp",
            spans: [{
              start: sampleCalleeDocument.text.length + 2,
              length: 18,
            }],
            provenance: {
              il_offsets: [9],
            },
          },
        ],
      },
    )),
    /node ids do not equal its exact coordinate matches/,
  );
  assert.throws(
    () => createAnnotatedSourceViewerModel(calleeEvidenceResult(
      sampleCalleeEvidence,
      {
        ...sampleCalleeDocument,
        nodes: [{
          ...sampleCalleeDocument.nodes[0],
          provenance: {
            il_offsets: [],
          },
        }],
      },
    )),
    /provenance must be a non-empty C# IL-offset set/,
  );
  assert.throws(
    () => createAnnotatedSourceViewerModel(calleeEvidenceResult({
      ...sampleCalleeEvidence,
      nodeIds: [],
      unavailableReason: "No unique callee source node.",
    })),
    /unavailable despite exact serialized correspondence/,
  );
  const reverseSourceOrder = createAnnotatedSourceViewerModel(
    calleeEvidenceResult(
      {
        ...sampleCalleeEvidence,
        coordinates: [
          {
            ilOffset: 2,
            kind: "Localloc",
          },
          {
            ilOffset: 9,
            kind: "Localloc",
          },
        ],
        nodeIds: [0, 1],
      },
      {
        ...sampleCalleeDocument,
        text: `${sampleCalleeDocument.text}; stackalloc byte[2]`,
        nodes: [
          {
            id: 0,
            kind: "StackAllocationExpression",
            medium: "CSharp",
            spans: [{
              start: sampleCalleeDocument.text.length + 2,
              length: 18,
            }],
            provenance: {
              il_offsets: [9],
            },
          },
          {
            ...sampleCalleeDocument.nodes[0],
            id: 1,
          },
        ],
      },
    ),
  );
  assert.deepEqual(reverseSourceOrder.findingEvidence[0]?.nodeIds, [0, 1]);
  const sharedNode = createAnnotatedSourceViewerModel(
    calleeEvidenceResult(
      {
        ...sampleCalleeEvidence,
        coordinates: [
          {
            ilOffset: 2,
            kind: "Localloc",
          },
          {
            ilOffset: 9,
            kind: "Localloc",
          },
        ],
        nodeIds: [0],
      },
      {
        ...sampleCalleeDocument,
        nodes: [{
          ...sampleCalleeDocument.nodes[0],
          provenance: {
            il_offsets: [2, 9],
          },
        }],
      },
    ),
  );
  assert.deepEqual(sharedNode.findingEvidence[0]?.nodeIds, [0]);
  assert.throws(
    () => createAnnotatedSourceViewerModel({
      ...calleeEvidenceResult(),
      findingEvidenceDocuments: [],
      findingEvidence: [],
    }),
    /does not cover every callee Finding/,
  );
});

test("viewer model validates method-level cost evidence without a source coordinate", () => {
  const model = createAnnotatedSourceViewerModel(methodCostEvidenceResult());
  const evidence = model.findingEvidence[0]!;

  assert.equal(evidence.state, "Method");
  assert.deepEqual(evidence.aggregateInputs, [
    {
      kind: "AllocationInLoop",
      value: null,
    },
    {
      kind: "Reflection",
      value: 3,
    },
  ]);
  assert.equal(evidence.document, null);
  assert.deepEqual(evidence.coordinates, []);
  assert.deepEqual(evidence.nodeIds, []);

  assert.throws(
    () => createAnnotatedSourceViewerModel(methodCostEvidenceResult({
      ...evidence,
      aggregateInputs: [],
    })),
    /invalid aggregate inputs/,
  );
  assert.throws(
    () => createAnnotatedSourceViewerModel(methodCostEvidenceResult({
      ...evidence,
      coordinates: [{
        ilOffset: 2,
        kind: "Localloc",
      }],
    })),
    /instruction projection/,
  );
  assert.throws(
    () => createAnnotatedSourceViewerModel(methodCostEvidenceResult({
      ...evidence,
      documentId: 0,
    })),
    /instruction projection/,
  );
});

test("the pure renderer rejects an invalid document for the shell to surface", () => {
  assert.throws(
    () => embeddedHtml({
      ...result,
      document: {
        ...sampleDocument,
        targets: [{ fact_id: 0, node_id: 99 }],
      },
    }),
    /names a node that does not exist/,
  );
});

test("inline working surface starts with complete C# and ends with provenance", () => {
  const html = embeddedHtml();
  const source = html.indexOf("medium-csharp");
  const provenance = html.indexOf("decompiled from IL");

  assert.doesNotMatch(html, /Annotated Source|C# with default findings/);
  assert.doesNotMatch(html, /data-annotated-action="(?:copy|explore)"/);
  assert.match(html, /class="annotated-reader-footer"/);
  assert.ok(source >= 0);
  assert.ok(provenance > source);
  assert.match(html, /medium-csharp/);
  assert.doesNotMatch(html, /medium-il/);
  assert.match(html, /annotated-chip-embedded-0-1-CSharp/);
  assert.match(html, /annotated-chip-embedded-1-0-CSharp/);
  assert.doesNotMatch(html, /annotated-chip-embedded-0-3-Il/);
  assert.doesNotMatch(html, /data-annotated-source-start/);
});

test("page-owned actions expose Copy and Explore only when the document is ready", () => {
  const enabled = renderAnnotatedSourcePageActions(true);
  const disabled = renderAnnotatedSourcePageActions(false);

  assert.match(
    enabled,
    /id="copy-annotated"[^>]*data-annotated-action="copy"[^>]*>Copy<\/button>/,
  );
  assert.match(
    enabled,
    /id="explore-annotated"[^>]*data-annotated-action="explore"[^>]*>Explore<\/button>/,
  );
  assert.doesNotMatch(enabled, / disabled/);
  assert.match(disabled, /id="copy-annotated"[^>]* disabled/);
  assert.match(disabled, /id="explore-annotated"[^>]* disabled/);
});

test("a selected invocation exposes separate Member and Source destinations", () => {
  const source = invocationResult();
  const model = createAnnotatedSourceViewerModel(source);
  const modal = openModalSession(model, createEmbeddedSession(model)).modal;
  const unselected = renderAnnotatedSourceModal({
    result: source,
    session: modal,
    escapeHtml,
  });
  const selected = renderAnnotatedSourceModal({
    result: source,
    session: selectNode(modal, 1),
    escapeHtml,
  });

  assert.doesNotMatch(unselected, /data-annotated-action="destination-open"/);
  assert.match(
    selected,
    /data-destination-index="0"\s+data-destination="member"[\s\S]*?>Member<\/button>/,
  );
  assert.match(
    selected,
    /data-destination-index="0"\s+data-destination="source"[\s\S]*?>Source<\/button>/,
  );
  assert.match(selected, /Open member overview for Example\.Targets\.Target/);
  assert.match(selected, /Open source for Example\.Targets\.Target/);
  assert.doesNotMatch(selected, />Navigate</);
});

test("the Relationships table preserves repeated physical calls and typed actions", () => {
  const source = repeatedRelationshipResult();
  const model = createAnnotatedSourceViewerModel(source);
  const session = openModalSession(model, createEmbeddedSession(model)).modal;
  const hiddenCoordinates = renderAnnotatedSourceModal({
    result: source,
    session,
    escapeHtml,
  });
  const visibleCoordinates = renderAnnotatedSourceModal({
    result: source,
    session: toggleCoordinates(session).state,
    escapeHtml,
  });

  assert.match(hiddenCoordinates, /<p class="section-eyebrow">Relationships<\/p>/);
  assert.equal(
    [...hiddenCoordinates.matchAll(/data-relationship-fact-id="([34])"/g)]
      .map(match => match[1]).join(","),
    "3,4",
  );
  assert.equal(
    (hiddenCoordinates.match(
      /<small>edge 7(?: · in loop)?<\/small>/g,
    ) ?? []).length,
    2,
  );
  assert.match(hiddenCoordinates, />Call<\/strong>/);
  assert.match(hiddenCoordinates, />Virtual call<\/strong>/);
  assert.match(hiddenCoordinates, /edge 7 · in loop/);
  assert.doesNotMatch(hiddenCoordinates, /IL_000[01]<\/small>/);
  assert.match(
    hiddenCoordinates,
    /Example\.Targets\.Target\(System\.Int32\)/,
  );
  assert.match(
    hiddenCoordinates,
    /data-annotated-action="relationship-destination-open"[\s\S]*data-relationship-index="0"[\s\S]*data-destination="member"/,
  );
  assert.match(
    hiddenCoordinates,
    /data-annotated-action="relationship-destination-open"[\s\S]*data-relationship-index="1"[\s\S]*data-destination="source"/,
  );
  assert.match(
    hiddenCoordinates,
    /id="annotated-relationship-3"[\s\S]*data-annotated-action="relationship-open"\s+data-fact-id="3"/,
  );
  assert.equal(
    annotatedFocusSelector({ kind: "relationship", factId: 3 }),
    "#annotated-relationship-3",
  );
  assert.doesNotMatch(
    hiddenCoordinates,
    /aria-label="Inspect [^"]*IL_000[01]/,
  );
  assert.match(visibleCoordinates, /edge 7 · IL_0000/);
  assert.match(visibleCoordinates, /edge 7 · in loop · IL_0001/);
});

test("the Relationships diagram is opt-in and preserves a path to physical calls", () => {
  const source = repeatedRelationshipResult();
  const model = createAnnotatedSourceViewerModel(source);
  const tableSession =
    openModalSession(model, createEmbeddedSession(model)).modal;
  const diagramSession = {
    ...tableSession,
    relationshipPresentation: "Diagram" as const,
  };
  const table = renderAnnotatedSourceModal({
    result: source,
    session: tableSession,
    escapeHtml,
  });
  const diagram = renderAnnotatedSourceModal({
    result: source,
    session: diagramSession,
    escapeHtml,
  });

  assert.match(
    table,
    /id="annotated-relationships-table"[\s\S]*aria-pressed="true"/,
  );
  assert.match(table, /annotated-relationship-table/);
  assert.doesNotMatch(table, /id="annotated-relationship-diagram"/);
  assert.match(
    diagram,
    /id="annotated-relationships-diagram"[\s\S]*aria-pressed="true"/,
  );
  assert.match(diagram, /id="annotated-relationship-diagram"/);
  assert.doesNotMatch(diagram, /annotated-relationship-table-wrap/);
  assert.equal(
    (diagram.match(/class="annotated-relationship-diagram-target"/g) ?? [])
      .length,
    1,
  );
  assert.match(diagram, />\s*2 call sites\s*<\/button>/);
  assert.match(
    diagram,
    /data-annotated-action="relationship-occurrences-open"\s+data-fact-id="3"/,
  );
  assert.match(
    diagram,
    /data-relationship-index="0"\s+data-destination="member"/,
  );
  assert.equal(
    annotatedFocusSelector({
      kind: "relationship-presentation",
      value: "Diagram",
    }),
    "#annotated-relationships-diagram",
  );
});

test("the Relationships diagram preserves version-distinct typed destinations", () => {
  const source = versionDistinctRelationshipResult();
  const model = createAnnotatedSourceViewerModel(source);
  const diagram = renderAnnotatedSourceModal({
    result: source,
    session: {
      ...openModalSession(model, createEmbeddedSession(model)).modal,
      relationshipPresentation: "Diagram",
    },
    escapeHtml,
  });

  assert.equal(
    (diagram.match(
      /class="annotated-relationship-diagram-destination"/g,
    ) ?? []).length,
    2,
  );
  assert.match(diagram, /Example 1\.0\.0\.0 · surface surface-v1/);
  assert.match(diagram, /Example 2\.0\.0\.0 · surface surface-v2/);
  assert.match(
    diagram,
    /data-relationship-index="0"\s+data-destination="member"/,
  );
  assert.match(
    diagram,
    /data-relationship-index="1"\s+data-destination="member"/,
  );
  assert.match(
    diagram,
    /data-relationship-index="0"\s+data-destination="source"/,
  );
  assert.match(
    diagram,
    /data-relationship-index="1"\s+data-destination="source"/,
  );
});

test("the Relationships table distinguishes available-empty from unavailable", () => {
  const available = modalHtml({
    ...result,
    viewerCatalog: {
      ...sampleViewerCatalog,
      callRelationships: {
        available: true,
        unavailableReason: null,
      },
    },
  });
  const unavailable = modalHtml();

  assert.match(
    available,
    /No direct call relationships were projected for this exact body\./,
  );
  assert.doesNotMatch(available, /annotated-relationship-table"/);
  assert.doesNotMatch(available, /annotated-relationship-presentations/);
  assert.match(
    unavailable,
    /Not projected by the current product query/,
  );
  assert.doesNotMatch(unavailable, /annotated-relationship-table"/);
  assert.doesNotMatch(unavailable, /annotated-relationship-presentations/);
});

test("C# highlighting crosses product segments without changing source text", () => {
  const source = 'return Widget.Create("x");';
  const highlighter = createCSharpRangeHighlighter(
    source,
    {
      languages: { csharp: {} },
      tokenize: () => [
        { type: "keyword", content: "return" },
        " ",
        { type: "class-name", content: "Widget" },
        { type: "punctuation", content: "." },
        { type: "function", content: "Create" },
        { type: "punctuation", content: "(" },
        { type: "string", content: '"x"' },
        { type: "punctuation", content: ");" },
      ],
    },
    escapeHtml,
  );
  const start = 2;
  const length = source.length - 4;
  const html = highlighter.render(start, length);

  assert.match(html, /class="token keyword">turn<\/span>/);
  assert.match(html, /class="token class-name">Widget<\/span>/);
  assert.match(html, /class="token function">Create<\/span>/);
  assert.equal(
    html
      .replaceAll(/<[^>]+>/g, "")
      .replaceAll("&quot;", '"'),
    source.slice(start, start + length),
  );
});

test("C# highlighting leaves excluded IL ranges unstyled inside one Prism token", () => {
  const source = 'var s = @"start\nIL_0000: nop\nend";';
  const ilStart = source.indexOf("IL_0000");
  const ilLength = "IL_0000: nop".length;
  const tokenizationSource =
    source.slice(0, ilStart)
    + " ".repeat(ilLength)
    + source.slice(ilStart + ilLength);
  const highlighter = createCSharpRangeHighlighter(
    source,
    {
      languages: { csharp: {} },
      tokenize: value => [{ type: "string", content: value }],
    },
    escapeHtml,
    tokenizationSource,
    [{ start: ilStart, length: ilLength }],
  );
  const html = highlighter.render(0, source.length);

  assert.match(html, /class="token string">var s = @&quot;start\n<\/span>IL_0000: nop/);
  assert.doesNotMatch(html, /class="token string">IL_0000: nop/);
  assert.equal(
    html
      .replaceAll(/<[^>]+>/g, "")
      .replaceAll("&quot;", '"'),
    source,
  );
});

test("annotated source renderer keeps syntax tokens inside structural spans", () => {
  const html = renderAnnotatedSource({
    result,
    session: createEmbeddedSession(createAnnotatedSourceViewerModel(result)),
    escapeHtml,
    highlightCSharp: source => createCSharpRangeHighlighter(
      source,
      {
        languages: { csharp: {} },
        tokenize: () => [{ type: "keyword", content: source }],
      },
      escapeHtml,
    ),
  });

  assert.match(
    html,
    /class="annotated-source-segment"[^>]*><span class="token keyword">/,
  );
});

test("C# tokenization masks IL while preserving document UTF-16 offsets", () => {
  const document: AnnotatedSourceDocument = {
    text: "cs\nil\nab",
    nodes: [
      {
        id: 0,
        kind: "CSharp",
        medium: "CSharp",
        spans: [{ start: 0, length: 2 }],
      },
      {
        id: 1,
        kind: "Instruction",
        medium: "Il",
        spans: [{ start: 3, length: 2 }],
        il_offset: 0,
      },
      {
        id: 2,
        kind: "CSharp",
        medium: "CSharp",
        spans: [{ start: 6, length: 1 }],
      },
      {
        id: 3,
        kind: "Instruction",
        medium: "Il",
        spans: [{ start: 7, length: 1 }],
        il_offset: 1,
      },
    ],
    regions: [],
    facts: [],
    targets: [],
  };
  const highlightingInput = csharpHighlightingInput(document);
  const tokenizationSource = csharpHighlightingText(document);

  assert.equal(tokenizationSource, "cs\n  \na ");
  assert.equal(tokenizationSource.length, document.text.length);
  assert.deepEqual(highlightingInput, {
    text: tokenizationSource,
    excludedRanges: [
      { start: 3, length: 2 },
      { start: 7, length: 1 },
    ],
  });
});

test("C# highlighting falls back to escaped source when token text diverges", () => {
  const source = '<T value="x">';
  const highlighter = createCSharpRangeHighlighter(
    source,
    {
      languages: { csharp: {} },
      tokenize: () => ["x".repeat(source.length)],
    },
    escapeHtml,
  );

  assert.equal(highlighter.render(0, source.length), "&lt;T value=&quot;x&quot;&gt;");
});

test("annotation rows begin at their product-issued source span", () => {
  const html = embeddedHtml();
  const objectStart = sampleDocument.text.indexOf("new object()");
  const annotation = html.indexOf("annotated-chip-embedded-0-1-CSharp");
  const source = html.indexOf("new object()");

  assert.match(
    html,
    new RegExp(`data-annotated-anchor-start="${objectStart}"`),
  );
  assert.match(
    html,
    /class="annotated-row-prefix" aria-hidden="true">    return <\/span>\s*<div class="annotated-row-items"/,
  );
  assert.ok(annotation >= 0);
  assert.ok(source >= 0);
  assert.ok(annotation < source);
});

test("embedded reader renders a product context limitation without rewriting it", () => {
  const html = embeddedHtml({
    ...result,
    contextLimitation: "<b>partial</b> assembly",
  });

  assert.match(html, /annotated-context/);
  assert.match(html, /&lt;b&gt;partial&lt;\/b&gt; assembly/);
});

test("modal controls are exactly catalog-supported media and annotatable Findings", () => {
  const html = modalHtml();

  assert.match(html, /role="dialog" aria-modal="true"/);
  assert.match(html, /data-annotated-set="Default"/);
  assert.match(html, /data-annotated-set="All"/);
  assert.match(html, /data-annotated-set="Clear"/);
  assert.match(html, /annotated-finding-toggle-0/);
  assert.match(html, /annotated-finding-toggle-1/);
  assert.doesNotMatch(html, /annotated-finding-toggle-2/);
  assert.match(html, /annotated-medium-csharp/);
  assert.match(html, /annotated-medium-il/);
  assert.match(html, /annotated-coordinate-toggle/);
  assert.match(html, /data-annotated-source-start/);
  assert.match(html, /id="annotated-source-modal-segment-\d+"/);
});

test("Selection, Relationships, and Findings are peer inspector sections", () => {
  const html = modalHtml();
  const selection = html.indexOf('class="section-eyebrow">Selection');
  const relationships = html.indexOf('class="section-eyebrow">Relationships');
  const findings = html.indexOf('class="section-eyebrow">Findings');

  assert.ok(selection >= 0);
  assert.ok(relationships > selection);
  assert.ok(findings > relationships);
  assert.match(
    html,
    /class="annotated-selection-empty">\s*<strong>Nothing selected<\/strong>\s*<span>Select addressable source or inspect a Finding\.<\/span>/,
  );
  assert.doesNotMatch(html, /Persistent inspector/);
});

test("modal inspector has one persistent action for every Finding including unanchored", () => {
  const html = modalHtml();

  assert.match(html, /id="annotated-inspector-0"/);
  assert.match(html, /id="annotated-inspector-1"/);
  assert.match(html, /id="annotated-inspector-2"/);
  assert.match(html, /annotated-finding-status">unanchored/);
});

test("All renders product-issued structure without turning it into a chip", () => {
  const model = createAnnotatedSourceViewerModel(result);
  const all = selectAllAnnotations(
    model,
    openModalSession(model, createEmbeddedSession(model)).modal,
  ).state;
  const html = renderAnnotatedSourceModal({
    result,
    session: all,
    escapeHtml,
  });

  assert.match(html, /class="annotated-structure-mark">/);
  assert.match(html, /structure · Body · C#/);
  assert.doesNotMatch(
    html,
    /<button[^>]*class="annotated-structure-mark"/,
  );
});

test("chip and persistent inspector paths render identical non-empty detail", () => {
  const model = createAnnotatedSourceViewerModel(result);
  const modal = openModalSession(model, createEmbeddedSession(model)).modal;
  const annotation = renderAnnotatedSourceModal({
    result,
    session: selectFinding(modal, {
      kind: "annotation",
      factId: 0,
      nodeId: 1,
      medium: "CSharp",
    }),
    escapeHtml,
  });
  const inspector = renderAnnotatedSourceModal({
    result,
    session: selectFinding(modal, {
      kind: "inspector",
      factId: 0,
    }),
    escapeHtml,
  });
  const detail = (html: string) =>
    html.match(/<section class="annotated-detail"[\s\S]*?<\/section>\s*<\/section>/)?.[0];

  assert.ok(detail(annotation));
  assert.equal(detail(annotation), detail(inspector));
  assert.match(annotation, /<dt>Descriptor<\/dt><dd>alloc\.new<\/dd>/);
  assert.match(annotation, /<dt>Category<\/dt><dd>Allocation<\/dd>/);
  assert.match(annotation, /<dt>Conditionality<\/dt><dd>Always<\/dd>/);
  assert.match(annotation, /<dt>Detail<\/dt><dd>object<\/dd>/);
  assert.match(annotation, /<dt>Origin<\/dt><dd>Body<\/dd>/);
  assert.doesNotMatch(annotation, /<dt>Source offset<\/dt>/);
  assert.match(annotation, /ObjectCreationExpression · C#/);
  assert.match(annotation, /Instruction · IL/);
  assert.match(annotation, /Not projected by the current product query/);

  const coordinates = renderAnnotatedSourceModal({
    result,
    session: selectFinding(
      toggleCoordinates(modal).state,
      { kind: "inspector", factId: 0 },
    ),
    escapeHtml,
  });
  assert.match(coordinates, /<dt>Source offset<\/dt><dd>1<\/dd>/);
});

test("Finding detail separates caller targets from exact callee evidence", () => {
  const source = calleeEvidenceResult();
  const model = createAnnotatedSourceViewerModel(source);
  const modal = openModalSession(
    model,
    createEmbeddedSession(model),
  ).modal;
  const selected = selectFinding(
    modal,
    { kind: "inspector", factId: 0 },
  );
  const html = renderAnnotatedSourceModal({
    result: source,
    session: selected,
    escapeHtml,
  });

  assert.match(html, /Caller relationship targets/);
  assert.match(html, /<h4>Callee evidence<\/h4>/);
  assert.match(html, /Example\.Targets\.Target\(int\)/);
  assert.match(
    html,
    /data-annotated-action="finding-evidence-open"[\s\S]*data-destination="member">Member<\/button>/,
  );
  assert.match(
    html,
    /data-annotated-action="finding-evidence-open"[\s\S]*data-destination="source">Source<\/button>/,
  );
  assert.match(
    html,
    /class="annotated-evidence-selected">stackalloc int\[1\]<\/span>/,
  );
  assert.doesNotMatch(html, /IL_0002/);

  const coordinates = renderAnnotatedSourceModal({
    result: source,
    session: toggleCoordinates(selected).state,
    escapeHtml,
  });
  assert.match(coordinates, /stack allocation\s*· IL_0002/);

  const unavailableSource = calleeEvidenceResult(
    {
      ...sampleCalleeEvidence,
      nodeIds: [],
      unavailableReason: "No unique callee source node.",
    },
    {
      ...sampleCalleeDocument,
      nodes: [{
        ...sampleCalleeDocument.nodes[0],
        provenance: {
          il_offsets: [3],
        },
      }],
    },
  );
  const unavailableModel = createAnnotatedSourceViewerModel(unavailableSource);
  const unavailableHtml = renderAnnotatedSourceModal({
    result: unavailableSource,
    session: selectFinding(
      openModalSession(
        unavailableModel,
        createEmbeddedSession(unavailableModel),
      ).modal,
      { kind: "inspector", factId: 0 },
    ),
    escapeHtml,
  });
  assert.match(unavailableHtml, /No unique callee source node\./);
  assert.doesNotMatch(unavailableHtml, /annotated-evidence-source/);
});

test("Finding detail renders an exact call-cycle witness and completeness", () => {
  const factId = sampleDocument.facts.length;
  const { source } = callCycleRelationshipResult({
    available: true,
    unavailableReason: null,
    isComplete: true,
    limits: [],
    findings: [{
      findingKey: "cycle:key",
      ordinal: 0,
      edgeRows: [1],
      factIds: [factId],
      targets: [{
        ...sampleInvocationTarget,
        memberName: "Caller",
      }],
    }],
  });
  const model = createAnnotatedSourceViewerModel(source);
  const html = renderAnnotatedSourceModal({
    result: source,
    session: selectFinding(
      createEmbeddedSession(model),
      {
        kind: "inspector",
        factId,
      }),
    escapeHtml,
  });

  assert.match(html, /<h4>Call cycles<\/h4>/);
  assert.match(html, /Direct recursion/);
  assert.match(
    html,
    /Example\.Targets\.Caller → Example\.Targets\.Caller/);
  assert.match(
    html,
    /Cycle census complete for the projected focus graph/);
});

test("Finding detail does not turn an incomplete empty cycle census into absence", () => {
  const { source, factId } = callCycleRelationshipResult({
    available: true,
    unavailableReason: null,
    isComplete: false,
    limits: ["TraversalBoundary"],
    findings: [],
  });
  const model = createAnnotatedSourceViewerModel(source);
  const html = renderAnnotatedSourceModal({
    result: source,
    session: selectFinding(
      createEmbeddedSession(model),
      { kind: "inspector", factId },
    ),
    escapeHtml,
  });

  assert.match(html, /No focus cycle was observed through this relationship/);
  assert.match(html, /Additional cycles may be unobserved/);
  assert.match(html, /traversal boundary/);
  assert.doesNotMatch(html, /not recursive/);
});

test("Finding detail renders typed synchronous completion without a runtime claim", () => {
  const { source, factId } = callCycleRelationshipResult({
    available: true,
    unavailableReason: null,
    isComplete: true,
    limits: [],
    findings: [],
  });
  const completionResult: AnnotatedSourceResult = {
    ...source,
    viewerCatalog: {
      ...source.viewerCatalog,
      synchronousCompletions: {
        available: true,
        unavailableReason: null,
        observations: [{
          factId,
          kind: "TaskAwaiterGetResult",
        }],
      },
    },
  };
  const model = createAnnotatedSourceViewerModel(completionResult);
  const html = renderAnnotatedSourceModal({
    result: completionResult,
    session: selectFinding(
      createEmbeddedSession(model),
      { kind: "inspector", factId },
    ),
    escapeHtml,
  });

  assert.match(html, /<h4>Synchronous completion<\/h4>/);
  assert.match(html, /Task awaiter GetResult/);
  assert.match(html, /may block the current thread when the task is incomplete/);
  assert.match(html, /no runtime blocking or duration was measured/);
  assert.doesNotMatch(html, /blocks the current thread awaiting/);
});

test("Finding detail renders a bounded local throw path without propagation claims", () => {
  const { source, factId } = callCycleRelationshipResult({
    available: true,
    unavailableReason: null,
    isComplete: true,
    limits: [],
    findings: [],
  });
  const loadFunctionFactId = source.document.facts.length;
  const callFact = source.document.facts[factId];
  const callRelationship = source.callRelationships[0];
  assert.ok(callFact);
  assert.ok(callRelationship);
  const mixedSource: AnnotatedSourceResult = {
    ...source,
    document: {
      ...source.document,
      facts: [
        ...source.document.facts,
        {
          ...callFact,
          id: loadFunctionFactId,
          detail: "Example.Targets.Forward(System.String)",
        },
      ],
      targets: [
        ...source.document.targets,
        { fact_id: loadFunctionFactId, node_id: 1 },
      ],
    },
    callRelationships: [
      ...source.callRelationships,
      {
        ...callRelationship,
        factId: loadFunctionFactId,
        operandToken: 0x0A000002,
        kind: "LoadFunction",
      },
    ],
  };
  const localThrowResult: AnnotatedSourceResult = {
    ...mixedSource,
    viewerCatalog: {
      ...mixedSource.viewerCatalog,
      localThrowPaths: {
        available: true,
        unavailableReason: null,
        isComplete: true,
        boundaries: [],
        limits: {
          maximumDepth: 3,
          maximumNodes: 25,
          maximumEdges: 100,
          maximumPaths: 25,
        },
        receipt: {
          destinationSearches: 1,
          searchNodes: 3,
          searchedEdges: 2,
          observedReachablePairs: 1,
          returnedPaths: 1,
        },
        paths: [{
          factIds: [factId],
          targets: [
            {
              ...sampleInvocationTarget,
              memberName: "Forward",
            },
            {
              ...sampleInvocationTarget,
              selectorKey: "method:Throw",
              memberName: "Throw",
            },
          ],
          terminalThrows: [{
            exceptionType: "System.ArgumentNullException",
            definitionModuleVersionId:
              "11111111-1111-1111-1111-111111111111",
            definitionToken: 0x02000002,
            constructionOffset: 2,
            constructorToken: 0x0A000002,
            throwOffset: 7,
          }],
        }],
      },
    },
  };
  const model = createAnnotatedSourceViewerModel(localThrowResult);
  const html = renderAnnotatedSourceModal({
    result: localThrowResult,
    session: selectFinding(
      createEmbeddedSession(model),
      { kind: "inspector", factId },
    ),
    escapeHtml,
  });

  assert.match(html, /<h4>Local throw paths<\/h4>/);
  assert.match(
    html,
    /Selected member → Example\.Targets\.Forward → Example\.Targets\.Throw/);
  assert.match(
    html,
    /System\.ArgumentNullException constructed at IL_0002 and thrown at IL_0007/);
  assert.match(html, /Static direct-call evidence only/);
  assert.match(html, /does not prove the selected\s+method throws/);
  assert.match(html, /or an exception\s+propagates through the path/);
  assert.doesNotMatch(html, /exception propagates to the selected method/);
});

test("Finding detail keeps empty incomplete local throw evidence bounded", () => {
  const { source, factId } = callCycleRelationshipResult({
    available: true,
    unavailableReason: null,
    isComplete: true,
    limits: [],
    findings: [],
  });
  const localThrowResult: AnnotatedSourceResult = {
    ...source,
    viewerCatalog: {
      ...source.viewerCatalog,
      localThrowPaths: {
        available: true,
        unavailableReason: null,
        isComplete: false,
        boundaries: [{
          kind: "TraversalBoundary",
          value: 1,
        }],
        limits: {
          maximumDepth: 3,
          maximumNodes: 25,
          maximumEdges: 100,
          maximumPaths: 25,
        },
        receipt: {
          destinationSearches: 1,
          searchNodes: 3,
          searchedEdges: 2,
          observedReachablePairs: 0,
          returnedPaths: 0,
        },
        paths: [],
      },
    },
  };
  const model = createAnnotatedSourceViewerModel(localThrowResult);
  const html = renderAnnotatedSourceModal({
    result: localThrowResult,
    session: selectFinding(
      createEmbeddedSession(model),
      { kind: "inspector", factId },
    ),
    escapeHtml,
  });

  assert.match(
    html,
    /No bounded path to a proven local throw was observed in the retained evidence/);
  assert.match(html, /Additional paths may be unobserved/);
  assert.match(html, /callee traversal boundary/);
  assert.doesNotMatch(html, /cannot throw|does not throw/);
});

test("Finding detail does not turn one retained shortest path into per-edge absence", () => {
  const { source, factId } = callCycleRelationshipResult({
    available: true,
    unavailableReason: null,
    isComplete: true,
    limits: [],
    findings: [],
  });
  const alternateFactId = source.document.facts.length;
  const firstFact = source.document.facts[factId];
  const firstRelationship = source.callRelationships[0];
  assert.ok(firstFact);
  assert.ok(firstRelationship);
  const localThrowResult: AnnotatedSourceResult = {
    ...source,
    document: {
      ...source.document,
      facts: [
        ...source.document.facts,
        {
          ...firstFact,
          id: alternateFactId,
          detail: "Example.Targets.ForwardB(System.String)",
          source_offset: 1,
        },
      ],
      targets: [
        ...source.document.targets,
        { fact_id: alternateFactId, node_id: 1 },
      ],
    },
    callRelationships: [
      ...source.callRelationships,
      {
        ...firstRelationship,
        edgeRow: 2,
        factId: alternateFactId,
        ilOffset: 1,
        operandToken: 0x0A000002,
        target: {
          ...firstRelationship.target,
          memberName: "ForwardB",
        },
      },
    ],
    viewerCatalog: {
      ...source.viewerCatalog,
      localThrowPaths: {
        available: true,
        unavailableReason: null,
        isComplete: true,
        boundaries: [],
        limits: {
          maximumDepth: 3,
          maximumNodes: 25,
          maximumEdges: 100,
          maximumPaths: 25,
        },
        receipt: {
          destinationSearches: 1,
          searchNodes: 3,
          searchedEdges: 4,
          observedReachablePairs: 1,
          returnedPaths: 1,
        },
        paths: [{
          factIds: [factId],
          targets: [
            {
              ...sampleInvocationTarget,
              memberName: "ForwardA",
            },
            {
              ...sampleInvocationTarget,
              selectorKey: "method:Throw",
              memberName: "Throw",
            },
          ],
          terminalThrows: [{
            exceptionType: "Example.LocalThrowPathException",
            definitionModuleVersionId:
              "11111111-1111-1111-1111-111111111111",
            definitionToken: 0x02000002,
            constructionOffset: 2,
            constructorToken: 0x0A000003,
            throwOffset: 7,
          }],
        }],
      },
    },
  };
  const model = createAnnotatedSourceViewerModel(localThrowResult);
  const html = renderAnnotatedSourceModal({
    result: localThrowResult,
    session: selectFinding(
      createEmbeddedSession(model),
      { kind: "inspector", factId: alternateFactId },
    ),
    escapeHtml,
  });

  assert.match(
    html,
    /No retained deterministic shortest witness begins with this relationship/);
  assert.doesNotMatch(
    html,
    /No bounded path to a proven local throw was observed/);
  assert.match(html, /Bounded path search complete/);
});

test("Finding detail renders allocation exception paths without a runtime claim", () => {
  const cases = [
    [
      "ThrownValue",
      "Thrown value",
      "constructs the value used by a throw",
    ],
    [
      "ExceptionHandler",
      "Exception handler",
      "occurs in a catch, filter, or fault handler",
    ],
  ] as const;
  for (const [kind, label, statement] of cases) {
    const exceptionPathResult: AnnotatedSourceResult = {
      ...result,
      viewerCatalog: {
        ...sampleViewerCatalog,
        allocationExceptionPaths: {
          available: true,
          unavailableReason: null,
          observations: [{
            factId: 0,
            kind,
          }],
        },
      },
    };
    const model = createAnnotatedSourceViewerModel(exceptionPathResult);
    const html = renderAnnotatedSourceModal({
      result: exceptionPathResult,
      session: selectFinding(
        createEmbeddedSession(model),
        { kind: "inspector", factId: 0 },
      ),
      escapeHtml,
    });

    assert.match(html, /<h4>Exception path<\/h4>/);
    assert.ok(html.includes(label));
    assert.ok(html.includes(statement));
    assert.match(html, /no runtime exception, handler execution, or frequency was measured/);
    assert.doesNotMatch(html, /exception occurred/);
  }
});

test("selected await presents both compiled paths without a runtime path claim", () => {
  const awaitDocument: AnnotatedSourceDocument = {
    ...sampleDocument,
    nodes: sampleDocument.nodes.map(node =>
      node.id === 1
        ? { ...node, kind: "AwaitExpression" }
        : node),
  };
  const awaitResult: AnnotatedSourceResult = {
    ...result,
    document: awaitDocument,
    viewerCatalog: {
      ...sampleViewerCatalog,
      awaitCompletionPaths: {
        available: true,
        unavailableReason: null,
        observations: [{ nodeId: 1 }],
      },
    },
  };
  const model = createAnnotatedSourceViewerModel(awaitResult);
  const html = renderAnnotatedSourceModal({
    result: awaitResult,
    session: selectNode(
      openModalSession(
        model,
        createEmbeddedSession(model),
      ).modal,
      1),
    escapeHtml,
  });

  assert.match(html, /Compiled await paths/);
  assert.match(html, /Inline completion/);
  assert.match(html, /Suspension and resume/);
  assert.match(html, /completed edge reaches the matching GetResult continuation/);
  assert.match(html, /correlated resume reaches the same continuation/);
  assert.match(html, /Compiled structure only/);
  assert.match(html, /no runtime path, frequency, duration, scheduler, or thread was measured/);
  assert.doesNotMatch(html, /fast path|slow path|path ran|completed successfully/);
});

test("Finding detail presents method-level callee evidence without an invented line", () => {
  const source = methodCostEvidenceResult();
  const model = createAnnotatedSourceViewerModel(source);
  const html = renderAnnotatedSourceModal({
    result: source,
    session: selectFinding(
      openModalSession(
        model,
        createEmbeddedSession(model),
      ).modal,
      { kind: "inspector", factId: 0 },
    ),
    escapeHtml,
  });

  assert.match(html, /Caller relationship targets/);
  assert.match(html, /Method-level aggregate evidence/);
  assert.match(html, /no singular source line is claimed/);
  assert.match(html, /Allocation in loop/);
  assert.match(html, /Reflection calls[\s\S]*<strong>3<\/strong>/);
  assert.match(html, /data-destination="member">Member<\/button>/);
  assert.match(html, /data-destination="source">Source<\/button>/);
  assert.doesNotMatch(html, /annotated-evidence-source/);
  assert.doesNotMatch(html, /IL_000/);
});

test("mixed-line hidden media keeps its layout text but removes its action", () => {
  const source: AnnotatedSourceResult = {
    document: {
      text: "ab",
      nodes: [
        { id: 0, kind: "Name", medium: "CSharp", spans: [{ start: 0, length: 1 }] },
        {
          id: 1,
          kind: "Instruction",
          medium: "Il",
          spans: [{ start: 1, length: 1 }],
          il_offset: 0,
        },
      ],
      regions: [],
      facts: [],
      targets: [],
    },
    viewerCatalog: {
      defaultFindingIds: [],
      supportedMedia: ["CSharp", "Il"],
      invocationLikeNodeKinds: [],
      invocationDestinations: [],
      findingEvidence: {
        available: false,
        unavailableReason: "NotProjected",
      },
      destinations: {
        available: false,
        unavailableReason: "NotProjected",
      },
      callRelationships: {
        available: false,
        unavailableReason: "NotProjected",
      },
      callCycles: {
        available: false,
        unavailableReason: "NotProjected",
        isComplete: false,
        limits: [],
        findings: [],
      },
      synchronousCompletions: {
        available: false,
        unavailableReason: "NotProjected",
        observations: [],
      },
      awaitCompletionPaths: {
        available: false,
        unavailableReason: "NotProjected",
        observations: [],
      },
      allocationExceptionPaths: {
        available: false,
        unavailableReason: "NotProjected",
        observations: [],
      },
      localThrowPaths: {
        available: false,
        unavailableReason: "NotProjected",
        isComplete: false,
        boundaries: [],
        limits: null,
        receipt: null,
        paths: [],
      },
    },
    findingEvidenceDocuments: [],
    findingEvidence: [],
    callRelationships: [],
    provenance: inertStringFixture("mixed media"),
    contextLimitation: null,
  };
  const html = modalHtml(source);
  const hidden = html.match(
    /<span\s+class="annotated-source-segment medium-hidden"[^>]*>b<\/span>/,
  )?.[0] ?? "";

  assert.ok(hidden);
  assert.doesNotMatch(hidden, /role="button"|tabindex|data-annotated-source-start/);
  assert.match(html, /id="annotated-source-modal-segment-0"[\s\S]*>a<\/span>/);
});

test("source text is escaped while source actions and chrome remain separate", () => {
  const source: AnnotatedSourceResult = {
    document: {
      ...sampleDocument,
      text: "<script>alert(1)</script>",
      nodes: [],
      regions: [],
      facts: [],
      targets: [],
    },
    viewerCatalog: csharpOnlyEmptyViewerCatalog,
    findingEvidenceDocuments: [],
    findingEvidence: [],
    callRelationships: [],
    provenance: inertStringFixture("decompiled from IL"),
    contextLimitation: null,
  };
  const html = embeddedHtml(source);

  assert.match(html, /&lt;script&gt;alert\(1\)&lt;\/script&gt;/);
  assert.doesNotMatch(html, /<script>alert/);
  assert.doesNotMatch(html, /data-annotated-action="(?:copy|explore)"/);
});

test("every chip-shaped rendered action is a button with one documented verb", () => {
  const model = createAnnotatedSourceViewerModel(result);
  const selectedModal = renderAnnotatedSourceModal({
    result,
    session: selectNode(
      openModalSession(model, createEmbeddedSession(model)).modal,
      1,
    ),
    escapeHtml,
  });
  const detailedModal = renderAnnotatedSourceModal({
    result,
    session: selectFinding(
      openModalSession(model, createEmbeddedSession(model)).modal,
      { kind: "inspector", factId: 0 },
    ),
    escapeHtml,
  });
  const html =
    `${renderAnnotatedSourcePageActions(true)}${embeddedHtml()}`
    + `${modalHtml()}${selectedModal}${detailedModal}`;
  const actionTags = [...html.matchAll(
    /<([a-z]+)[^>]*data-annotated-action="([^"]+)"[^>]*>/g,
  )];

  assert.ok(actionTags.length > 0);
  assert.deepEqual(
    new Set(actionTags.map(match => match[2])),
    new Set([
      "copy",
      "explore",
      "annotation-open",
      "annotation-set",
      "finding-toggle",
      "medium-toggle",
      "coordinate-toggle",
      "node-select",
      "inspector-open",
      "close-modal",
      "close-detail",
    ]),
  );
  assert.ok(actionTags.every(match => match[1] === "button"));
});

test("persistent source affordances use no underline treatment", () => {
  const styles = readFileSync(
    new URL("../src/styles.css", import.meta.url),
    "utf8",
  );
  const sourceRules = [...styles.matchAll(
    /\.annotated-source-segment[^{]*\{([^}]*)\}/g,
  )].map(match => match[1]).join("\n");

  assert.ok(sourceRules.length > 0);
  assert.doesNotMatch(sourceRules, /text-decoration|border-bottom|inset 0 -/);

  const annotationRows =
    /\.annotated-row-items\s*\{([^}]*)\}/.exec(styles)?.[1] ?? "";
  assert.ok(annotationRows.length > 0);
  assert.doesNotMatch(annotationRows, /border-left|padding-left/);
});

test("Annotated Source composition requires a concrete overload and validated session", () => {
  const appSource = readFileSync(
    new URL("../src/dotnet-inspect.ts", import.meta.url),
    "utf8",
  );

  assert.match(
    appSource,
    /const annotatedPageContext =\s*activeScope === "member"\s*&& state\.memberSection === "annotated"\s*&& memberSourceHasConcreteOverload\(\);/);
  assert.match(
    appSource,
    /const annotatedWorkingSurface =\s*annotatedPageContext && state\.memberAnnotatedEmbedded !== null;/);
  assert.match(
    appSource,
    /class="working-surface-actions"[\s\S]*renderAnnotatedSourcePageActions\(annotatedWorkingSurface\)/);
  assert.doesNotMatch(
    appSource,
    /class="contextual-actions annotated-contextual-actions"/);
  assert.match(
    appSource,
    /class="working-surface-actions" role="group" aria-label="\$\{metadataWorkingSurface \? "Type graph actions" : packageDependenciesWorkingSurface \? "Dependency graph actions" : callGraphPageContext \? "Call graph actions" : annotatedPageContext \? "Annotated Source actions" : sourcePageKind \? "Source actions" : "Member actions"\}"/);
  assert.match(
    appSource,
    /class="working-surface-actions" role="group" aria-label="\$\{metadataWorkingSurface \? "Type graph actions" : packageDependenciesWorkingSurface \? "Dependency graph actions" : callGraphPageContext \? "Call graph actions" : annotatedPageContext \? "Annotated Source actions" : sourcePageKind \? "Source actions" : "Member actions"\}"/);
  assert.match(
    appSource,
    /detail-scroll\$\{annotatedWorkingSurface \? " annotated-working-surface" : ""\}/);

  const diagramRenderer =
    /async function renderAnnotatedRelationshipDiagram\(\) \{([\s\S]*?)\n\}/
      .exec(appSource)?.[1] ?? "";
  assert.match(
    diagramRenderer,
    /createAnnotatedSourceViewerModel\(result\)[\s\S]*buildAnnotatedRelationshipGraphMermaid\(\s*model\.callRelationships\)/,
  );
  assert.doesNotMatch(
    diagramRenderer,
    /createCallGraphInspectionCoordinator|loadSelectedMemberCallGraph|callGraphInspection\./,
  );
});

test("Annotated Source destination actions use typed graph routes and exact sections", () => {
  const appSource = readFileSync(
    new URL("../src/dotnet-inspect.ts", import.meta.url),
    "utf8",
  );

  assert.match(
    appSource,
    /case "destination-open":[\s\S]*model\.invocationDestinations\[action\.destinationIndex\][\s\S]*origin\?\.kind === "type-explorer" \? "retained" : "annotated"[\s\S]*callGraphTargetBinding\([\s\S]*destination\.target,[\s\S]*action\.destination,[\s\S]*failureSurface\)[\s\S]*if \(binding\.blocked\) \{[\s\S]*binding\.onSelect\(\);[\s\S]*return;[\s\S]*dismissAnnotatedSourceModal\(false\);[\s\S]*resetTypeExplorerRouteState\(\);[\s\S]*binding\.onSelect\(\)/,
  );
  assert.match(
    appSource,
    /case "relationship-destination-open":[\s\S]*model\.callRelationships\[action\.relationshipIndex\][\s\S]*origin\?\.kind === "type-explorer" \? "retained" : "annotated"[\s\S]*callGraphTargetBinding\([\s\S]*relationship\.target,[\s\S]*action\.destination,[\s\S]*failureSurface\)[\s\S]*if \(binding\.blocked\) \{[\s\S]*binding\.onSelect\(\);[\s\S]*return;[\s\S]*dismissAnnotatedSourceModal\(false\);[\s\S]*resetTypeExplorerRouteState\(\);[\s\S]*binding\.onSelect\(\)/,
  );
  assert.match(
    appSource,
    /case "finding-evidence-open":[\s\S]*model\.findingEvidenceByFactId\.get\(action\.factId\)[\s\S]*origin\?\.kind === "type-explorer" \? "retained" : "annotated"[\s\S]*callGraphTargetBinding\([\s\S]*evidence\.target,[\s\S]*action\.destination,[\s\S]*failureSurface\)[\s\S]*if \(binding\.blocked\) \{[\s\S]*binding\.onSelect\(\);[\s\S]*return;[\s\S]*dismissAnnotatedSourceModal\(false\);[\s\S]*resetTypeExplorerRouteState\(\);[\s\S]*binding\.onSelect\(\)/,
  );
  assert.match(
    appSource,
    /function showGraphNavigationFailureOutsideCallGraph\([\s\S]*switch \(failureSurface\) \{[\s\S]*case "call-graph":[\s\S]*return false;[\s\S]*case "annotated":[\s\S]*renderAndFocusAnnotated\(\{ kind: "explore" \}, "embedded"\);[\s\S]*case "retained":[\s\S]*showRetainedGraphNavigationError\(message\);[\s\S]*assertNever\([\s\S]*"graph navigation failure surface"\)/,
  );
  assert.match(
    appSource,
    /function showRetainedGraphNavigationError\(message: string\) \{\s*appendQueryNotice\(message\);\s*render\(\);\s*afterCurrentNavigationFrame\(\(\) => focusLevelOneHeading\(\)\);\s*\}/,
  );
  const blockedGraphBinding =
    /function blockedCallGraphNodeBinding\([\s\S]*?\n\}/
      .exec(appSource)?.[0] ?? "";
  assert.match(
    blockedGraphBinding,
    /showGraphNavigationFailureOutsideCallGraph\(\s*message,\s*failureSurface\)/,
  );
  assert.match(blockedGraphBinding, /invalidateGraphMemberNavigation\(\)/);
  const graphMemberFailure =
    /function showGraphMemberNavigationError\([\s\S]*?\n\}/
      .exec(appSource)?.[0] ?? "";
  assert.match(
    graphMemberFailure,
    /showGraphNavigationFailureOutsideCallGraph\(message, failureSurface\)/,
  );
  assert.match(
    graphMemberFailure,
    /state\.graphMemberNavigationError = message/,
  );
  const platformNavigation =
    /async function navigateOrDrillPlatform\([\s\S]*?\n\}/
      .exec(appSource)?.[0] ?? "";
  assert.equal(
    platformNavigation.match(
      /showGraphNavigationFailureOutsideCallGraph\(/g)?.length,
    2,
  );
  assert.match(platformNavigation, /state\.platformDrillError = message/);
  assert.match(platformNavigation, /await renderMermaidCallGraph\(\)/);
  const platformFailure =
    /async function showPlatformTargetError\([\s\S]*?\n\}/
      .exec(appSource)?.[0] ?? "";
  assert.match(
    platformFailure,
    /showGraphNavigationFailureOutsideCallGraph\(\s*message,\s*failureSurface\)/,
  );
  assert.match(platformFailure, /state\.platformDrillError = message/);
  assert.match(
    appSource,
    /const loadedSection = destination === "source" \? "source" : "overview"/,
  );
  assert.match(
    appSource,
    /const runtimeSection = destination === "member" \? "overview" : "call-graph"/,
  );
  assert.match(
    appSource,
    /candidate\.status === "resident"[\s\S]*coordinatePackage !== null \|\| destination !== "default"[\s\S]*navigateToUnprojectedGraphMember\([\s\S]*section/,
  );
  assert.match(
    appSource,
    /singleProjectedGraphMember\(projection\.type\)[\s\S]*createAppTypeSurface\(projection\.type\)[\s\S]*callGraphTargetMatchesType\(target, projectedType\)[\s\S]*graphOnly: true/,
  );
  assert.match(
    appSource,
    /if \(section === "source"\) \{\s*observeAsync\(loadSelectedMemberSource\(\), "Loading member source"\)/,
  );
  assert.match(
    appSource,
    /state\.memberAnnotatedModal !== null[\s\S]*state\.annotatedDestinationError[\s\S]*renderAndFocusAnnotated\(\s*"#annotated-destination-error",\s*"modal"/,
  );
  assert.match(
    appSource,
    /id="annotated-destination-error"[\s\S]*role="alert"/,
  );
  assert.match(
    appSource,
    /function openAnnotatedSourceModal\(\) \{[\s\S]*invalidateMemberDestinationWork\(state\);/,
  );
  assert.match(
    appSource,
    /case "destination-open":[\s\S]*invalidateMemberDestinationWork\(state\);[\s\S]*callGraphTargetBinding/,
  );
  assert.match(
    appSource,
    /function invalidateSourceCaches\(\) \{\s*invalidateSourceDestinationWork\(state\);/,
  );
  assert.equal(
    appSource.match(
      /state\.memberAnnotated = null;\s*state\.memberAnnotated(?:Key = "";\s*state\.memberAnnotated)?Error = "";\s*state\.memberFindingInteraction = null;\s*state\.memberFindingSelectionError = "";\s*state\.annotatedDestinationError = "";/g,
    )?.length,
    7,
  );
});
